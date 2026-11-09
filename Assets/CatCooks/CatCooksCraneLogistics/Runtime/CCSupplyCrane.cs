using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
namespace CatCooks.CraneLogistics
{
    [DisallowMultipleComponent, RequireComponent(typeof(NetworkObject))]
    public sealed class CCSupplyCrane : NetworkBehaviour
    {
        [Header("Geometry — copied from your crane")]
        public Transform Tower, Trolley, RopeTop, HookVisual, Platform;
        public Transform[] Crew = new Transform[2], Cargo = new Transform[2], Corners = new Transform[4];
        public Transform[] SlingHooks = new Transform[4];
        public LineRenderer Rope;
        public LineRenderer[] Slings = new LineRenderer[4];
        public Vector3 TowerAxis=Vector3.up, TrolleyAxis=Vector3.right, HookAnchorLocal;
        public Quaternion TowerRest=Quaternion.identity, HookRest=Quaternion.identity;
        public Vector3 TrolleyRest;
        [Header("Dock poses: yaw / trolley offset in local metres / WORLD rope length")]
        public Vector3 DockA=new Vector3(0,0,15), DockB=new Vector3(65,3,5);
        public Vector2 TrolleyRange=new Vector2(-10,20), RopeRange=new Vector2(1,100);
        [Min(.2f)] public float TravelRope=2, SlingHeight=3;
        [Header("Crew walking — world metres per second")]
        [Min(.1f)] public float CrewWalkSpeed=1.4f, CrewCarrySpeed=1.1f;
        [Min(0)] public float PickupPlaceSeconds=.6f;
        [Header("Editor travel height helper")]
        public Transform HighestObstacleMarker;
        [Min(.3f)] public float TravelClearance=2;
        public Vector2 DeckSize=new Vector2(4,3);
        [Tooltip("Offset from deck centre to storage, in crane-root axes; Y is floor offset.")]
        public Vector3 StorageA=new Vector3(4,0,0), StorageB=new Vector3(4,0,0);
        [Header("Optional NEW territory (empty = legacy loop)")]
        public CCWorkTerritory Territory;
        [Header("Optional two-rigger single pallet")]
        public bool CooperativePallet=false;
        public CCCranePayloadLibrary PayloadLibrary;
        [Min(.1f)] public float PalletCrewHalfWidth=.85f;
        [Min(0)] public float PalletCarryHeight=.4f;
        [Header("Cycle")]
        public bool Run=false;
        [Min(.1f)] public float RotationSpeed=8, TrolleySpeed=1, HoistSpeed=1.5f;
        [Min(.5f)] public float HandlingSeconds=6, RiggingSeconds=3;
        [Min(0)] public float StartDelay;
        [Header("Server obstacle stop — layers of building, loads and other cranes")]
        public bool CheckObstacles=true;
        public LayerMask ObstacleMask=~0;
        [Min(.1f)] public float CargoClearance=1.6f;
        [TextArea] public string SetupNote="Set both docks and travel clearance before enabling Run.";
        public struct State
        {
            public bool Ready, Reverse, Blocked, Managed;
            public uint CompletedTrips;
            public bool DirectMount,DirectDetached;
            public uint MountToken;
            public Vector3 MountSize;
            public Quaternion MountRotation;
            public int PayloadCount,Resource;
            public Vector3 FromPose, ToPose, FromStorage, ToStorage;
            public byte Phase;
            public float Progress, PhaseSeconds;
            public Vector3 Pose;
        }
        readonly SyncVar<State> _state=new SyncVar<State>(new SyncTypeSettings(.1f));
        State _current, _view;
        float _elapsed, _publish, _delay;
        Rigidbody _deckBody;
        Quaternion _hookRotation, _deckRotation;
        readonly RaycastHit[] _hits=new RaycastHit[32];
        readonly Collider[] _overlaps=new Collider[32];
        // Local diagnostics only: no extra RPC, physics queries or per-frame log messages.
        public bool DebugStartServerSeen { get; private set; }
        public string DebugStartupError { get; private set; } = "";
        public uint DebugFixedSteps { get; private set; }
        public float DebugLastFixedAt { get; private set; }
        public float DebugDelayRemaining => Mathf.Max(0,_delay);
        public Object DebugBlocker { get; private set; }
        public string DebugBlockReason { get; private set; } = "";
        public bool DebugHasQuery { get; private set; }
        public Vector3 DebugQueryCenter { get; private set; }
        public Vector3 DebugQueryHalfSize { get; private set; }
        public Quaternion DebugQueryRotation { get; private set; }
        public State Current
        {
            get
            {
                if(!Application.isPlaying||NetworkObject==null)return default;
                return IsServerInitialized?_current:_state.Value;
            }
        }
        static readonly string[] Names={"Loading","Rigging / boarding","Hoisting","Slewing","Lowering","Unrigging","Unloading","Waiting for territory job","Empty hoist","Empty slew","Empty lowering","Crew returning to deck","Crew approaching supply","Holding for BOTH workers bolts","Unhooking secured structure","Clearing installed structure"};
        public string PhaseName => !Current.Ready?"Not started / network not initialized":Current.Blocked ? "Blocked — obstacle / exclusion zone" : Names[Mathf.Clamp(Current.Phase,0,Names.Length-1)];
        public bool WaitingForJob => Application.isPlaying && NetworkObject!=null && IsServerInitialized && _current.Ready && _current.Managed && _current.Phase==7;
        public bool TrySolveDock(Vector3 world,Vector3 seed,out Vector3 pose)
        {
            pose=seed;if(Tower==null||Trolley==null||RopeTop==null) return false;
            for(int i=0;i<32;i++)
            {
                Vector3 p=TopAt(pose),dx=(TopAt(pose+new Vector3(.1f,0,0))-p)/.1f,dy=(TopAt(pose+new Vector3(0,.01f,0))-p)/.01f,e=world-p;
                float det=dx.x*dy.z-dy.x*dx.z;if(Mathf.Abs(det)<.00001f) break;
                pose.x+=Mathf.Clamp((e.x*dy.z-dy.x*e.z)/det,-15,15);
                pose.y=Mathf.Clamp(pose.y+Mathf.Clamp((dx.x*e.z-e.x*dx.z)/det,-2,2),TrolleyRange.x,TrolleyRange.y);
                if(new Vector2(e.x,e.z).sqrMagnitude<.0001f) break;
            }
            pose.z=TopAt(pose).y-world.y-SlingHeight;
            return pose.z>=RopeRange.x&&pose.z<=RopeRange.y&&Vector3.Distance(DeckAt(pose),world)<.08f;
        }
        public bool ServerStartDelivery(CCWorkTerritory owner,Vector3 from,Vector3 to,Vector3 fromStorage,Vector3 toStorage,int count,int resource=-1)
        {
            if(owner==null||Territory!=owner||!WaitingForJob||!Run||_delay>0) return false;
            if(TravelRope>Mathf.Min(from.z,to.z)||TravelRope<RopeRange.x) return false;
            _current.DirectMount=false;_current.DirectDetached=false;_current.MountToken=0;
            _current.Resource=resource;_current.PayloadCount=Mathf.Clamp(count,1,2);_current.FromPose=from;_current.ToPose=to;_current.FromStorage=fromStorage;_current.ToStorage=toStorage;
            // The initial platform already starts at DockA: do not send it up and down again.
            bool atSource=Mathf.Abs(Mathf.DeltaAngle(_current.Pose.x,from.x))<.05f&&Mathf.Abs(_current.Pose.y-from.y)<.01f&&Mathf.Abs(_current.Pose.z-from.z)<.03f;
            _current.Phase=(byte)(atSource?12:8);_current.PhaseSeconds=PhaseDuration(_current);_current.Progress=0;_current.Blocked=false;_current.Reverse=false;_elapsed=0;
            _state.Value=_current;return true;
        }
        public static Vector2 WorldFootprint(Vector3 size,Quaternion rotation)
        {
            Vector3 x=rotation*Vector3.right,z=rotation*Vector3.forward;
            return new Vector2(Mathf.Abs(x.x)*size.x+Mathf.Abs(z.x)*size.z,Mathf.Abs(x.z)*size.x+Mathf.Abs(z.z)*size.z);
        }
        public bool ServerStartMount(CCWorkTerritory owner,Vector3 from,Vector3 to,CCDirectMountJob job)
        {
            if(job==null||PayloadLibrary==null||PayloadLibrary.Find((int)job.Resource)==null)return false;
            if(!ServerStartDelivery(owner,from,to,Vector3.zero,Vector3.zero,1,(int)job.Resource))return false;
            _current.DirectMount=true;_current.DirectDetached=false;_current.MountToken=job.Token;
            _current.MountSize=job.Size;_current.MountRotation=job.Rotation;
            if(_current.Phase==12)_current.Phase=1;
            _current.PhaseSeconds=PhaseDuration(_current);_state.Value=_current;return true;
        }
        public bool ServerReleaseMount(CCWorkTerritory owner,uint token)
        {
            if(!IsServerInitialized||Territory!=owner||!Run||!_current.DirectMount||_current.Phase!=13||_current.MountToken!=token)return false;
            _current.Phase=14;_current.Progress=0;_elapsed=0;_current.PhaseSeconds=Mathf.Max(.1f,RiggingSeconds);_state.Value=_current;return true;
        }
        public void ServerCancelDelivery(CCWorkTerritory owner)
        {
            if(!IsServerInitialized||Territory!=owner||!_current.Managed) return;
            _current.Phase=7;_current.MountToken=0;_current.DirectDetached=true;_current.Progress=0;_current.Blocked=false;_elapsed=0;_state.Value=_current;
        }
        void Awake()
        {
            if(Platform!=null) { _deckBody=Platform.GetComponent<Rigidbody>();_deckRotation=Platform.rotation; }
            if(HookVisual!=null) _hookRotation=transform.rotation*HookRest;
        }
        public bool Valid(out string error)
        {
            error="";
            if(Tower==null||Trolley==null||RopeTop==null||Platform==null||HookVisual==null) error="Missing crane geometry.";
            else if(Crew==null||Crew.Length!=2||Crew[0]==null||Crew[1]==null||Cargo==null||Cargo.Length!=2||Cargo[0]==null||Cargo[1]==null) error="Assign exactly two riggers and two cargo roots.";
            else if(Vector3.Dot(transform.up,Vector3.up)<.999f) error="Crane root must be upright (yaw rotation only).";
            else if(transform.lossyScale!=Vector3.one) error="Crane logistics root scale must remain 1,1,1.";
            else if(TowerAxis.sqrMagnitude<.01f||TrolleyAxis.sqrMagnitude<.01f) error="Axes must not be zero.";
            else if(TrolleyRange.x>TrolleyRange.y||RopeRange.x<=0||RopeRange.x>RopeRange.y) error="Invalid limits.";
            else if(DockA.y<TrolleyRange.x||DockA.y>TrolleyRange.y||DockB.y<TrolleyRange.x||DockB.y>TrolleyRange.y) error="Dock trolley outside limits.";
            else if(DockA.z<RopeRange.x||DockA.z>RopeRange.y||DockB.z<RopeRange.x||DockB.z>RopeRange.y) error="Dock rope outside limits.";
            else if(TravelRope<RopeRange.x||TravelRope>Mathf.Min(DockA.z,DockB.z)) error="Travel rope must be between minimum rope and shortest dock rope.";
            return error.Length==0;
        }
        public override void OnStartServer()
        {
            base.OnStartServer();
            DebugStartServerSeen=true;DebugStartupError="";DebugFixedSteps=0;DebugLastFixedAt=0;
            DebugBlocker=null;DebugBlockReason="";DebugHasQuery=false;
            if(!Valid(out string error)) { DebugStartupError=error;Debug.LogError("[Crane logistics] "+error,this);Run=false;return; }
            _current=new State{Ready=true,PayloadCount=2,Pose=DockA,Managed=Territory!=null,Phase=(byte)(Territory!=null?7:0),FromPose=DockA,ToPose=DockB,FromStorage=StorageA,ToStorage=StorageB};_view=_current;_delay=StartDelay;
            _current.PhaseSeconds=PhaseDuration(_current);_state.Value=_current;Apply(_current,false);
        }
        public override void OnStopNetwork()
        {
            base.OnStopNetwork();_current=default;_view=default;_elapsed=0;_publish=0;
        }
        public Vector3 TopAt(Vector3 pose)
        {
            // Pure hierarchy evaluation: reach checks/gizmos must not move live transforms.
            Vector3 point=Vector3.zero;
            for(Transform node=RopeTop;node!=null;node=node.parent)
            {
                Vector3 position=node==Trolley?TrolleyRest+TrolleyAxis.normalized*pose.y:node.localPosition;
                Quaternion rotation=node==Tower?TowerRest*Quaternion.AngleAxis(pose.x,TowerAxis.normalized):node.localRotation;
                point=Matrix4x4.TRS(position,rotation,node.localScale).MultiplyPoint3x4(point);
            }
            return point;
        }
        public Vector3 DeckAt(Vector3 pose) => TopAt(pose)-Vector3.up*(pose.z+SlingHeight);
        void SetParts(Vector3 pose)
        {
            Tower.localRotation=TowerRest*Quaternion.AngleAxis(pose.x,TowerAxis.normalized);
            Trolley.localPosition=TrolleyRest+TrolleyAxis.normalized*pose.y;
        }
        void FixedUpdate()
        {
            if(!IsServerInitialized||!_current.Ready) return;
            DebugFixedSteps++;DebugLastFixedAt=Time.unscaledTime;
            if(_delay>0) { _delay-=Time.fixedDeltaTime;return; }
            if(Run && _current.Phase!=7 && (Territory==null||Territory.isActiveAndEnabled))
            {
                float dt=Time.fixedDeltaTime;
                Vector3 a=_current.Managed?_current.FromPose:DockA,b=_current.Managed?_current.ToPose:DockB;
                Vector3 from=_current.Reverse?b:a, to=_current.Reverse?a:b;
                int phase=_current.Phase;
                if(phase==2||phase==3||phase==4||phase==8||phase==9||phase==10||phase==15)
                {
                    Vector3 target=phase==15?new Vector3(_current.Pose.x,_current.Pose.y,TravelRope):phase==8?new Vector3(_current.Pose.x,_current.Pose.y,TravelRope):phase==9?new Vector3(from.x,from.y,TravelRope):phase==10?from:phase==2?new Vector3(from.x,from.y,TravelRope):phase==3?new Vector3(to.x,to.y,TravelRope):to;
                    Vector3 next=new Vector3(Mathf.MoveTowardsAngle(_current.Pose.x,target.x,Mathf.Max(.1f,RotationSpeed)*dt),Mathf.MoveTowards(_current.Pose.y,target.y,Mathf.Max(.1f,TrolleySpeed)*dt),Mathf.MoveTowards(_current.Pose.z,target.z,Mathf.Max(.1f,HoistSpeed)*dt));
                    Vector3 nextDeck=DeckAt(next);
                    DebugBlocker=null;DebugBlockReason="";DebugHasQuery=false;
                    CCWorkZone excluded=Territory!=null?Territory.FindExclusion(_current.DirectMount?nextDeck-Vector3.up*CurrentLoadHeight():nextDeck,CurrentExclusionSize(),CurrentLoadHeight()):null;
                    if(excluded!=null) { DebugBlocker=excluded;DebugBlockReason="Exclusion zone"; }
                    _current.Blocked=excluded!=null || (CheckObstacles && Obstructed(DeckAt(_current.Pose),nextDeck));
                    if(!_current.Blocked)
                    {
                        _current.Pose=next;
                        if(Mathf.Abs(Mathf.DeltaAngle(next.x,target.x))<.01f&&Mathf.Abs(next.y-target.y)<.001f&&Mathf.Abs(next.z-target.z)<.001f) Advance();
                    }
                }
                else if(phase==13)
                {
                    // No timer may release a suspended part. Only ServerReleaseMount with the job token.
                    _current.Blocked=false;_current.Progress=0;
                }
                else
                {
                    _current.Blocked=false;_elapsed+=dt;
                    _current.Progress=Mathf.Clamp01(_elapsed/Mathf.Max(.1f,_current.PhaseSeconds));
                    if(_current.Progress>=1) Advance();
                }
            }
            Apply(_current,true);
            _publish+=Time.fixedDeltaTime;
            if(_publish>=.1f) { _publish=0;_state.Value=_current; }
        }
        void Advance()
        {
            if(_current.DirectMount)
            {
                switch(_current.Phase)
                {
                    case 8:_current.Phase=9;break;
                    case 9:_current.Phase=10;break;
                    case 10:case 12:_current.Phase=1;break;
                    case 1:_current.Phase=2;break;
                    case 2:_current.Phase=3;break;
                    case 3:_current.Phase=4;break;
                    case 4:_current.Phase=13;break;
                    case 14:_current.DirectDetached=true;_current.Phase=15;break;
                    case 15:_current.Phase=7;_current.CompletedTrips++;break;
                    default:return;
                }
                _elapsed=0;_current.Progress=0;_current.PhaseSeconds=PhaseDuration(_current);return;
            }
            if(_current.Phase==10) _current.Phase=12;
            else if(_current.Phase==12) _current.Phase=0;
            else if(_current.Phase==11) { _current.Phase=7;_current.CompletedTrips++; }
            else if(_current.Phase==6 && _current.Managed) _current.Phase=11;
            else if(_current.Phase==6) { _current.Phase=0;_current.Reverse=!_current.Reverse; }
            else _current.Phase++;
            _elapsed=0;_current.Progress=0;_current.PhaseSeconds=PhaseDuration(_current);
        }
        float PhaseDuration(State s)
        {
            if(s.Phase==0||s.Phase==6||s.Phase==11||s.Phase==12)
            {
                bool destination=s.Phase==6||s.Phase==11;
                Vector3 sa=s.Managed?s.FromStorage:StorageA,sb=s.Managed?s.ToStorage:StorageB;
                Vector3 offset=destination?(s.Reverse?sa:sb):(s.Reverse?sb:sa);
                float distance=transform.TransformVector(offset).magnitude;
                bool carrying=s.Phase==0||s.Phase==6;
                float seconds=distance/Mathf.Max(.1f,carrying?CrewCarrySpeed:CrewWalkSpeed);
                return carrying?Mathf.Max(HandlingSeconds,seconds+2*Mathf.Max(0,PickupPlaceSeconds)):Mathf.Max(.1f,seconds);
            }
            return Mathf.Max(.1f,RiggingSeconds);
        }
        public bool TryTravelRopeForHeight(Vector3 a,Vector3 b,float deckHeight,out float length,out string error)
        {
            length=TravelRope;error="";
            if(Tower==null||Trolley==null||RopeTop==null){error="Missing crane geometry";return false;}
            // Min top Y along the same yaw/trolley speed profile as runtime (bounded editor-only sampling).
            float time=Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(a.x,b.x))/Mathf.Max(.1f,RotationSpeed),Mathf.Abs(a.y-b.y)/Mathf.Max(.1f,TrolleySpeed));
            float minimum=float.PositiveInfinity;
            for(int direction=0;direction<2;direction++)for(int i=0;i<=128;i++)
            { Vector3 from=direction==0?a:b,to=direction==0?b:a;float t=time*i/128f;Vector3 p=new Vector3(Mathf.MoveTowardsAngle(from.x,to.x,Mathf.Max(.1f,RotationSpeed)*t),Mathf.MoveTowards(from.y,to.y,Mathf.Max(.1f,TrolleySpeed)*t),0);minimum=Mathf.Min(minimum,TopAt(p).y); }
            length=minimum-deckHeight-SlingHeight;
            if(length<RopeRange.x||length>Mathf.Min(RopeRange.y,Mathf.Min(a.z,b.z)))
            { error="Requested height unreachable or below a dock. Raise crane / adjust height marker. No change applied.";return false; }
            return true;
        }
        public Vector2 LoadSize(int resource)
        {
            var e=PayloadLibrary!=null?PayloadLibrary.Find(resource):null;
            return e==null?DeckSize:new Vector2(Mathf.Max(DeckSize.x,e.Footprint.x),Mathf.Max(DeckSize.y,e.Footprint.y));
        }
        public float LoadHeight(int resource)
        {var e=PayloadLibrary!=null?PayloadLibrary.Find(resource):null;return e==null?CargoClearance:Mathf.Max(CargoClearance,e.Clearance);}
        bool DirectLoaded=>_current.DirectMount&&!_current.DirectDetached&&((_current.Phase>=1&&_current.Phase<=4)||_current.Phase==13||_current.Phase==14);
        Vector2 CurrentLoadSize()=>_current.DirectMount?(DirectLoaded?new Vector2(_current.MountSize.x,_current.MountSize.z):Vector2.one*.15f):_current.Phase<=6?LoadSize(_current.Resource):DeckSize;
        Vector2 CurrentExclusionSize()=>DirectLoaded?WorldFootprint(_current.MountSize,_current.MountRotation):CurrentLoadSize();
        float CurrentLoadHeight()=>_current.DirectMount?(DirectLoaded?_current.MountSize.y:.15f):_current.Phase<=6?LoadHeight(_current.Resource):CargoClearance;
        bool Own(Collider c) => c!=null && c.transform.IsChildOf(transform);
        bool Obstructed(Vector3 a,Vector3 b)
        {
            Vector2 size=CurrentLoadSize();
            Vector3 half=new Vector3(size.x*.5f,Mathf.Max(.2f,CurrentLoadHeight())*.5f,size.y*.5f);
            Vector3 offset=Vector3.up*(_current.DirectMount?-half.y:half.y+.08f);
            Quaternion rotation=_current.DirectMount?_current.MountRotation:_deckRotation;
            // Inset the structural body 2 cm so its supporting face can meet the slab/column.
            // No floor collider is ignored: side and transit obstacles still block the load.
            if(_current.DirectMount)half=new Vector3(Mathf.Max(.025f,half.x-.02f),Mathf.Max(.025f,half.y-.02f),Mathf.Max(.025f,half.z-.02f));
            DebugHasQuery=true;DebugQueryCenter=b+offset;DebugQueryHalfSize=half;DebugQueryRotation=rotation;
            Vector3 delta=b-a;float distance=delta.magnitude;
            int n=Physics.OverlapBoxNonAlloc(b+offset,half,_overlaps,rotation,ObstacleMask,QueryTriggerInteraction.Ignore);
            if(n==_overlaps.Length) { DebugBlockReason="Overlap buffer full (32); conservative stop";return true; }
            for(int i=0;i<n;i++) if(!Own(_overlaps[i])) { DebugBlocker=_overlaps[i];DebugBlockReason="Overlap at next load position";return true; }
            if(distance<.00001f) return false;
            n=Physics.BoxCastNonAlloc(a+offset,half,delta/distance,_hits,rotation,distance,ObstacleMask,QueryTriggerInteraction.Ignore);
            if(n==_hits.Length) { DebugBlockReason="BoxCast buffer full (32); conservative stop";return true; }
            for(int i=0;i<n;i++) if(!Own(_hits[i].collider)) { DebugBlocker=_hits[i].collider;DebugBlockReason="BoxCast along load movement";return true; }
            return false;
        }
        void Update()
        {
            if(IsServerInitialized||!IsClientInitialized||!_state.Value.Ready) return;
            State target=_state.Value;
            if(!_view.Ready) _view=target;
            float dt=Time.deltaTime;
            _view.Pose=new Vector3(Mathf.MoveTowardsAngle(_view.Pose.x,target.Pose.x,Mathf.Max(.1f,RotationSpeed)*dt*1.3f),Mathf.MoveTowards(_view.Pose.y,target.Pose.y,Mathf.Max(.1f,TrolleySpeed)*dt*1.3f),Mathf.MoveTowards(_view.Pose.z,target.Pose.z,Mathf.Max(.1f,HoistSpeed)*dt*1.3f));
            if(_view.Phase!=target.Phase||_view.Reverse!=target.Reverse) _view.Progress=target.Progress;
            else _view.Progress=Mathf.MoveTowards(_view.Progress,target.Progress,dt*2);
            _view.DirectMount=target.DirectMount;_view.DirectDetached=target.DirectDetached;_view.MountToken=target.MountToken;_view.MountSize=target.MountSize;_view.MountRotation=target.MountRotation;
            // Contact/holding is exact, not an interpolated transform drifting toward the bolts.
            if(target.DirectMount&&(target.Phase==13||target.Phase==14))_view.Pose=target.Pose;
            _view.PhaseSeconds=target.PhaseSeconds;_view.PayloadCount=target.PayloadCount;_view.Resource=target.Resource;_view.Managed=target.Managed;_view.CompletedTrips=target.CompletedTrips;_view.FromPose=target.FromPose;_view.ToPose=target.ToPose;_view.FromStorage=target.FromStorage;_view.ToStorage=target.ToStorage;
            _view.Phase=target.Phase;_view.Reverse=target.Reverse;_view.Blocked=target.Blocked;
            Apply(_view,false);
        }
        public void Preview(Vector3 pose)
        {
            if(Application.isPlaying) return;
            _hookRotation=transform.rotation*HookRest;_deckRotation=transform.rotation;
            Apply(new State{Ready=true,Pose=pose,Phase=3,Resource=(int)CCZoneResource.Bricks},false);
        }
        void Apply(State s,bool physics)
        {
            SetParts(s.Pose);
            if(PayloadLibrary!=null)PayloadLibrary.Apply(s.Resource);
            Vector3 hub=RopeTop.position-Vector3.up*s.Pose.z, deck=hub-Vector3.up*SlingHeight;
            HookVisual.rotation=_hookRotation;HookVisual.position=hub-HookVisual.TransformVector(HookAnchorLocal);
            if(s.DirectMount)
            {
                ApplyDirect(s,hub,deck);return;
            }
            if(!Platform.gameObject.activeSelf)Platform.gameObject.SetActive(true);
            if(physics&&_deckBody!=null) { _deckBody.MovePosition(deck);_deckBody.MoveRotation(_deckRotation); }
            else Platform.SetPositionAndRotation(deck,_deckRotation);
            if(Rope!=null) { Rope.SetPosition(0,RopeTop.position);Rope.SetPosition(1,hub); }
            float rig=s.Phase==0||s.Phase==6?0:s.Phase==1?s.Progress:s.Phase==5?1-s.Progress:1;
            for(int i=0;i<4;i++) if(Slings!=null&&i<Slings.Length&&Slings[i]!=null&&Corners!=null&&i<Corners.Length&&Corners[i]!=null)
            {
                Vector3 corner=deck+_deckRotation*Corners[i].localPosition;
                Vector3 end=Vector3.Lerp(hub+Vector3.down*.35f+_deckRotation*new Vector3((i%2==0?-.15f:.15f),0,(i<2?-.15f:.15f)),corner,rig);
                Slings[i].SetPosition(0,hub);Slings[i].SetPosition(1,end);
                if(SlingHooks!=null&&i<SlingHooks.Length&&SlingHooks[i]!=null) SlingHooks[i].SetPositionAndRotation(end,_deckRotation);
            }
            bool unload=s.Phase==6||s.Phase==11;
            Vector3 a=s.Managed?s.FromPose:DockA,b=s.Managed?s.ToPose:DockB;
            Vector3 sa=s.Managed?s.FromStorage:StorageA,sb=s.Managed?s.ToStorage:StorageB;
            Vector3 dock=DeckAt(unload?(s.Reverse?a:b):(s.Reverse?b:a));
            Vector3 storage=transform.TransformDirection(unload?(s.Reverse?sa:sb):(s.Reverse?sb:sa));
            if(CooperativePallet)
            {
                ApplyPallet(s,deck,dock,storage);
                return;
            }
            for(int i=0;i<2;i++)
            {
                if(Crew[i]==null||Cargo[i]==null) continue;
                bool showCargo=!s.Managed||(s.Phase<7&&i<s.PayloadCount);
                if(Cargo[i].gameObject.activeSelf!=showCargo) Cargo[i].gameObject.SetActive(showCargo);
                float side=i==0?-1:1;
                Vector3 seat=deck+_deckRotation*new Vector3(side*DeckSize.x*.28f,0,DeckSize.y*.22f);
                Vector3 loadSlot=deck+_deckRotation*new Vector3(side*DeckSize.x*.23f,0,-DeckSize.y*.2f);
                Vector3 stock=dock+storage+_deckRotation*new Vector3(side*DeckSize.x*.28f,0,DeckSize.y*.22f);
                Vector3 cat=seat,cargo=loadSlot;
                bool walking=s.Phase==0||s.Phase==6||s.Phase==11||s.Phase==12;
                bool carrying=s.Phase==0||s.Phase==6;
                float duration=Vector3.Distance(seat,stock)/Mathf.Max(.1f,carrying?CrewCarrySpeed:CrewWalkSpeed);
                float age=s.Progress*Mathf.Max(.1f,s.PhaseSeconds);
                float t=walking?Mathf.Clamp01((age-(carrying?PickupPlaceSeconds:0))/Mathf.Max(.01f,duration)):s.Progress;
                if(s.Phase==0) { cat=Vector3.Lerp(stock,seat,t);cargo=Vector3.Lerp(stock,loadSlot,t)+Vector3.up*(Mathf.Min(1,Mathf.Min(t,1-t)*8)*.45f); }
                if(unload) { cat=Vector3.Lerp(seat,stock,t);cargo=Vector3.Lerp(loadSlot,stock,t)+Vector3.up*(Mathf.Min(1,Mathf.Min(t,1-t)*8)*.45f); }
                if(s.Phase==11) cat=Vector3.Lerp(stock,seat,t);
                if(s.Phase==12) cat=Vector3.Lerp(seat,stock,t);
                if(s.Phase==1||s.Phase==5)
                {
                    float bend=Mathf.Sin(s.Progress*Mathf.PI);
                    cat+=_deckRotation*new Vector3(side*.25f*bend,0,0);
                    Crew[i].rotation=_deckRotation*Quaternion.Euler(25*bend,side*70,0);
                }
                else
                {
                    Vector3 dir=(s.Phase==0||s.Phase==11)?seat-stock:stock-seat;dir.y=0;
                    Crew[i].rotation=walking&&dir.sqrMagnitude>.001f?Quaternion.LookRotation(dir,Vector3.up):_deckRotation;
                }
                Crew[i].position=cat;Cargo[i].SetPositionAndRotation(cargo,_deckRotation);
            }
        }
        void ApplyDirect(State s,Vector3 hub,Vector3 top)
        {
            // The transport platform is exchanged for a direct sling in this prototype.
            // Crew remain at their previous position; they do not ride structural steel.
            if(Platform.gameObject.activeSelf)Platform.gameObject.SetActive(false);
            bool attached=!s.DirectDetached&&((s.Phase>=1&&s.Phase<=4)||s.Phase==13||s.Phase==14);
            Cargo[0].gameObject.SetActive(attached);Cargo[1].gameObject.SetActive(false);
            if(attached)
            {
                Cargo[0].SetPositionAndRotation(top,s.MountRotation);
                var e=PayloadLibrary.Find(s.Resource);
                if(e!=null&&e.Visual!=null)
                {
                    bool column=s.Resource==(int)CCZoneResource.SteelColumn;
                    Transform t=e.Visual.transform;
                    t.localRotation=Quaternion.identity;
                    t.localScale=column?new Vector3(1,s.MountSize.y,1):new Vector3(s.MountSize.x,1,1);
                    t.localPosition=Vector3.down*(column?s.MountSize.y:s.MountSize.y*.5f);
                }
            }
            if(Rope!=null){Rope.SetPosition(0,RopeTop.position);Rope.SetPosition(1,hub);}
            float rig=s.Phase==1?s.Progress:s.Phase==14?1-s.Progress:attached?1:0;
            for(int i=0;i<4;i++)
            {
                bool beam=s.Resource==(int)CCZoneResource.SteelBeam;
                Vector3 anchor=top+s.MountRotation*new Vector3((i%2==0?-1:1)*(beam?s.MountSize.x*.35f:.16f),0,(i<2?-1:1)*.1f);
                Vector3 end=Vector3.Lerp(hub+Vector3.down*.25f,anchor,rig);
                if(Slings!=null&&i<Slings.Length&&Slings[i]!=null){Slings[i].SetPosition(0,hub);Slings[i].SetPosition(1,end);}
                if(SlingHooks!=null&&i<SlingHooks.Length&&SlingHooks[i]!=null)SlingHooks[i].SetPositionAndRotation(end,s.MountRotation);
            }
        }
        void ApplyPallet(State s,Vector3 deck,Vector3 dock,Vector3 storage)
        {
            // Cargo[0] is one shared visual pallet representing PayloadCount stock units.
            bool visible=!s.Managed||s.Phase<7||s.Phase==11;
            Cargo[0].gameObject.SetActive(visible);Cargo[1].gameObject.SetActive(false);
            bool unload=s.Phase==6||s.Phase==11;
            Vector3 stock=dock+storage;
            bool walk=s.Phase==0||s.Phase==6||s.Phase==11||s.Phase==12;
            bool carry=s.Phase==0||s.Phase==6;
            float duration=storage.magnitude/Mathf.Max(.1f,carry?CrewCarrySpeed:CrewWalkSpeed);
            float age=s.Progress*Mathf.Max(.1f,s.PhaseSeconds);
            float t=Mathf.Clamp01((age-(carry?PickupPlaceSeconds:0))/Mathf.Max(.01f,duration));
            Vector3 centre=deck,pallet=deck;
            if(s.Phase==0){centre=Vector3.Lerp(stock,deck,t);pallet=centre;}
            if(s.Phase==6){centre=Vector3.Lerp(deck,stock,t);pallet=centre;}
            if(s.Phase==11){centre=Vector3.Lerp(stock,deck,t);pallet=stock;}
            if(s.Phase==12)centre=Vector3.Lerp(deck,stock,t);
            if(carry)pallet+=Vector3.up*(Mathf.Min(1,Mathf.Min(t,1-t)*8)*PalletCarryHeight);
            Vector3 direction=(s.Phase==0||s.Phase==11)?-storage:storage;direction.y=0;
            Quaternion facing=walk&&direction.sqrMagnitude>.001f?Quaternion.LookRotation(direction,Vector3.up):_deckRotation;
            for(int i=0;i<2;i++)
            {
                float side=i==0?-1:1;
                Vector3 pos=centre+_deckRotation*Vector3.right*(side*PalletCrewHalfWidth);
                Quaternion rot=facing;
                if(s.Phase==1||s.Phase==5)rot=_deckRotation*Quaternion.Euler(20*Mathf.Sin(s.Progress*Mathf.PI),side*70,0);
                Crew[i].SetPositionAndRotation(pos,rot);
            }
            Cargo[0].SetPositionAndRotation(pallet,_deckRotation);
        }
        void OnDrawGizmosSelected()
        {
            if(Tower==null||Trolley==null||RopeTop==null) return;
            Vector3 a=DeckAt(DockA),b=DeckAt(DockB);
            Gizmos.color=Color.cyan;Gizmos.DrawWireCube(a,new Vector3(DeckSize.x,.15f,DeckSize.y));
            Gizmos.color=Color.yellow;Gizmos.DrawWireCube(b,new Vector3(DeckSize.x,.15f,DeckSize.y));
            Gizmos.color=Color.green;
            Vector3 last=DeckAt(new Vector3(DockA.x,DockA.y,TravelRope));Gizmos.DrawLine(a,last);
            for(int i=1;i<=32;i++) { float t=i/32f;Vector3 p=DeckAt(new Vector3(Mathf.LerpAngle(DockA.x,DockB.x,t),Mathf.Lerp(DockA.y,DockB.y,t),TravelRope));Gizmos.DrawLine(last,p);last=p; }
            Gizmos.DrawLine(last,b);
            Gizmos.color=Color.magenta;Gizmos.DrawWireSphere(a+transform.TransformDirection(StorageA),.35f);Gizmos.DrawWireSphere(b+transform.TransformDirection(StorageB),.35f);
        }
    }
}
