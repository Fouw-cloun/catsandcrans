using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.AI;

namespace CatCooks.Builders
{
    [DisallowMultipleComponent, RequireComponent(typeof(NetworkObject), typeof(NavMeshAgent))]
    public sealed class CCBuilderCat : NetworkBehaviour
    {
        public CCBuilderDirector Director;
        public int CatId;
        public Transform Visual;
        public Transform RightHandSocket;
        public Transform CarrySocket;
        [Tooltip("Слои пола и препятствий для безопасного отпускания кота.")]
        public LayerMask RecoveryMask = ~0;
        private readonly SyncVar<NetworkObject> _carrier = new(null);
        private CCBuilderCatCarrier _carrierBridge;
        private NetworkObject _bridgeObject;
        private Vector3 _homeGround, _lastGround;
        private bool _hasHome;
        private readonly Collider[] _clearance = new Collider[32];
        private readonly SyncVar<NetworkObject> _machine = new(null);
        private readonly SyncVar<bool> _seated = new(false);
        private CCBuilderExcavatorOperator _machineBridge;
        public NetworkObject MachineObject => _machine.Value;
        public bool ExternalBusy => _machine.Value!=null;
        public bool MachineSeated => _seated.Value;
        public bool IsCarried => _carrier.Value!=null;
        public NetworkObject Carrier => _carrier.Value;
        public Vector3 LastGroundPosition => _lastGround;
        public int GroupSlot => IsServerInitialized ? _slot : _remote.Slot;
        public float ActivityAge => IsServerInitialized ? Time.time-_activitySince : _remote.ActivityAge+Time.time-_remoteAt;
        public Renderer BodyRenderer;
        [Min(.1f)] public float WalkSpeed = 1.35f;
        [Min(0)] public float HopHeight = .2f;
        public struct Pose
        {
            public bool Ready;
            public Vector3 Position;
            public float Yaw, Speed, ActivityAge;
            public CCBuilderActivity Activity;
            public int Group;
            public byte Slot;
        }
        private readonly SyncVar<Pose> _pose = new(new SyncTypeSettings(0f));
        private NavMeshAgent _agent;
        private NavMeshPath _path;
        private Vector3 _visualBase, _visualScale, _faceTarget, _destination;
        private Quaternion _visualRotation;
        private CCBuilderActivity _activity;
        private float _activitySince, _nextSnapshot, _lastSent;
        private int _group, _slot, _lastMaterialGroup = -1;
        private bool _pendingDestination, _hasDestination;
        private Vector3 _remoteFrom, _remoteTo;
        private Quaternion _remoteFromRot, _remoteToRot;
        private float _remoteAt;
        private Pose _remote;
        private bool _remoteReady;
        public bool NavigationReady => _agent != null && _agent.enabled && _agent.isOnNavMesh;
        public bool NeedsPath => IsServerInitialized && !IsCarried && !MachineSeated && _pendingDestination;
        public CCBuilderActivity Activity => IsServerInitialized ? _activity : _remote.Activity;
        public int GroupId => IsServerInitialized ? _group : _remote.Group;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _agent.enabled = false; // only the server may move the root
            _path = new NavMeshPath();
            if (Visual != null) { _visualBase=Visual.localPosition; _visualScale=Visual.localScale; _visualRotation=Visual.localRotation; }
        }
        public override void OnStartNetwork()
        {
            base.OnStartNetwork(); _pose.OnChange += PoseChanged;
        }
        public override void OnStartServer()
        {
            base.OnStartServer();
            _machine.Value=null;_seated.Value=false;_carrier.Value=null;_homeGround=_lastGround=transform.position;_hasHome=false;
            PlaceOnNavigation();
            if(NavigationReady) { _homeGround=_lastGround=transform.position;_hasHome=true; }
            SetActivity(NavigationReady ? CCBuilderActivity.Idle : CCBuilderActivity.Stranded, transform.position);
            _nextSnapshot=Time.time + (CatId % 10)*.01f;
            if (Director != null) Director.Register(this);
            Publish();
        }
        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsServerInitialized)
            {
                _agent.enabled=false;
                Receive(_pose.Value, true);
            }
        }
        private bool PlaceOnNavigation()
        {
            if (IsCarried || MachineSeated) return false;
            if (NavigationReady) return true;
            NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID=_agent.agentTypeID, areaMask=_agent.areaMask };
            if (!NavMesh.SamplePosition(transform.position,out NavMeshHit hit,1.5f,filter) || Mathf.Abs(hit.position.y-transform.position.y)>1f) return false;
            transform.position=hit.position;
            _agent.enabled=true;
            if (!_agent.isOnNavMesh) { _agent.enabled=false; return false; }
            _agent.Warp(hit.position);
            _agent.speed=WalkSpeed*(.94f+(CatId%7)*.02f);
            _agent.autoTraverseOffMeshLink=false;
            return true;
        }
        public void AssignGroup(int group,int slot)
        {
            if (!IsServerInitialized) return;
            _group=group;_slot=slot;
        }
        public void RequestMove(Vector3 destination,Vector3 faceTarget)
        {
            if (!IsServerInitialized || IsCarried || MachineSeated) return;
            _destination=destination;_faceTarget=faceTarget;
            _pendingDestination=true;_hasDestination=false;
            if (NavigationReady) { _agent.ResetPath();_agent.isStopped=true; }
            SetActivity(CCBuilderActivity.Gathering, faceTarget);
        }
        // Called by the director under a global path budget, never every frame for all cats.
        public void ProcessPath()
        {
            if (!NeedsPath) return;
            _pendingDestination=false;
            if (!PlaceOnNavigation()) { SetActivity(CCBuilderActivity.Stranded,_faceTarget);return; }
            NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID=_agent.agentTypeID, areaMask=_agent.areaMask };
            if (!NavMesh.SamplePosition(_destination,out NavMeshHit hit,.65f,filter) || Mathf.Abs(hit.position.y-_destination.y)>.65f ||
                !_agent.CalculatePath(hit.position,_path) || _path.status!=NavMeshPathStatus.PathComplete)
            { SetActivity(CCBuilderActivity.Stranded,_faceTarget);return; }
            _destination=hit.position;
            if (!_agent.SetPath(_path)) { SetActivity(CCBuilderActivity.Stranded,_faceTarget);return; }
            _agent.isStopped=false;_hasDestination=true;
            SetActivity(CCBuilderActivity.Walking,_faceTarget);
        }
        public bool Arrived
        {
            get
            {
                if (!NavigationReady || _pendingDestination || !_hasDestination || _agent.pathPending) return false;
                Vector3 d=transform.position-_destination;
                return new Vector2(d.x,d.z).sqrMagnitude < .32f*.32f && Mathf.Abs(d.y)<.5f;
            }
        }
        public bool PathFailed => !_pendingDestination && (!_hasDestination || !NavigationReady ||
            (!_agent.pathPending && _agent.pathStatus!=NavMeshPathStatus.PathComplete && !Arrived));
        public void SetActivity(CCBuilderActivity activity,Vector3 faceTarget)
        {
            if (!IsServerInitialized || (IsCarried && activity!=CCBuilderActivity.Carried)) return;
            _activity=activity;_activitySince=Time.time;_faceTarget=faceTarget;
            if (activity!=CCBuilderActivity.Walking && NavigationReady) _agent.isStopped=true;
            if (activity!=CCBuilderActivity.Walking && activity!=CCBuilderActivity.Gathering) _pendingDestination=false;
        }
        public void Face(Vector3 point) { if(IsServerInitialized) _faceTarget=point; }
        public void HoldPosition()
        {
            if (!IsServerInitialized || IsCarried || MachineSeated) return;
            _pendingDestination=false;_hasDestination=false;
            if (NavigationReady) _agent.ResetPath();
            SetActivity(CCBuilderActivity.Idle,transform.position+transform.forward);
        }
        private void Update()
        {
            if (IsServerInitialized)
            {
                if((ExternalBusy && !_machine.Value.IsSpawned) || (MachineSeated && !ExternalBusy)) ServerLeaveMachine(_lastGround);
                if(MachineSeated)
                {
                    ApplyMachinePose();
                    if(Time.time>=_nextSnapshot) { _nextSnapshot=Time.time+.1f;Publish(); }
                    return;
                }
                if (IsCarried)
                {
                    if(!_carrier.Value.IsSpawned) { ServerRelease(transform.position,true);return; }
                    ApplyCarryPose();
                    if(Time.time>=_nextSnapshot) { _nextSnapshot=Time.time+.1f;Publish(); }
                    return;
                }
                if(_activity==CCBuilderActivity.Carried) ServerRecover();
                if(NavigationReady) _lastGround=transform.position;
                if (_activity==CCBuilderActivity.Walking && Arrived) SetActivity(CCBuilderActivity.Gathering,_faceTarget);
                if (_activity!=CCBuilderActivity.Walking)
                {
                    Vector3 d=_faceTarget-transform.position;d.y=0;
                    if (d.sqrMagnitude>.02f) transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(d),180f*Time.deltaTime);
                }
                if (Time.time>=_nextSnapshot) { _nextSnapshot=Time.time+.1f;Publish(); }
            }
            else if (IsClientInitialized && MachineSeated) ApplyMachinePose();
            else if (IsClientInitialized && IsCarried) ApplyCarryPose();
            else if (IsClientInitialized && _remoteReady)
            {
                float t=Mathf.Clamp01((Time.time-_remoteAt)/.1f);
                transform.SetPositionAndRotation(Vector3.Lerp(_remoteFrom,_remoteTo,t),Quaternion.Slerp(_remoteFromRot,_remoteToRot,t));
            }
        }
        private void Publish()
        {
            float speed=NavigationReady ? _agent.velocity.magnitude : 0f;
            Pose next=new Pose { Ready=true,Position=transform.position,Yaw=transform.eulerAngles.y,Speed=speed,
                Activity=_activity,ActivityAge=Time.time-_activitySince,Group=_group,Slot=(byte)_slot };
            Pose old=_pose.Value;
            if (old.Ready && (old.Position-next.Position).sqrMagnitude<.000025f && Mathf.Abs(Mathf.DeltaAngle(old.Yaw,next.Yaw))<1f &&
                Mathf.Abs(old.Speed-speed)<.05f && old.Activity==next.Activity && old.Group==next.Group && old.Slot==next.Slot && Time.time-_lastSent<.75f) return;
            _pose.Value=next;_lastSent=Time.time;
        }
        private void PoseChanged(Pose previous,Pose next,bool asServer)
        {
            if (asServer || IsServerInitialized) return;
            Receive(next,!_remoteReady);
        }
        private void Receive(Pose pose,bool snap)
        {
            if (!pose.Ready) return;
            _remote=pose;_remoteAt=Time.time;_remoteTo=pose.Position;_remoteToRot=Quaternion.Euler(0,pose.Yaw,0);
            if (snap || (transform.position-pose.Position).sqrMagnitude>16f) transform.SetPositionAndRotation(_remoteTo,_remoteToRot);
            _remoteFrom=transform.position;_remoteFromRot=transform.rotation;_remoteReady=true;
        }
        private void LateUpdate()
        {
            if(MachineSeated && (IsClientInitialized || IsServerInitialized)) ApplyMachinePose();
            if(IsCarried && (IsClientInitialized || IsServerInitialized)) ApplyCarryPose();
            if (Visual==null || !IsClientInitialized) return;
            int group=GroupId;
            if (group!=_lastMaterialGroup && Director!=null && Director.GroupMaterials!=null && Director.GroupMaterials.Length>0)
            {
                if (BodyRenderer!=null) BodyRenderer.sharedMaterial=Director.GroupMaterials[Mathf.Abs(group)%Director.GroupMaterials.Length];
                _lastMaterialGroup=group;
            }
            float age=IsServerInitialized ? Time.time-_activitySince : _remote.ActivityAge+Time.time-_remoteAt;
            float speed=IsServerInitialized ? (NavigationReady ? _agent.velocity.magnitude : 0f) : _remote.Speed;
            int slot=IsServerInitialized ? _slot : _remote.Slot;
            float lift=0,lean=0,pitch=0;Vector3 scale=_visualScale;
            switch(Activity)
            {
                case CCBuilderActivity.Walking:
                    lift=Mathf.Abs(Mathf.Sin(age*10f+CatId))*.025f*Mathf.Clamp01(speed);lean=Mathf.Sin(age*10f+CatId)*3f;break;
                case CCBuilderActivity.Digging: pitch=12f+Mathf.Sin(age*4.5f)*12f;break;
                case CCBuilderActivity.Hammering: pitch=5f+Mathf.Sin(age*8f)*5f;break;
                case CCBuilderActivity.Carried: lean=Mathf.Sin(age*2)*4;break;
                case CCBuilderActivity.Working: pitch=8f+Mathf.Sin(age*5f+slot*.45f)*8f;break;
                case CCBuilderActivity.Chatting: lean=Mathf.Sin(age*2.6f+slot)*5f;pitch=Mathf.Sin(age*3.1f+slot)*3f;break;
                case CCBuilderActivity.Hopping:
                    float phase=Mathf.Repeat(age-slot*.1f,1.6f);
                    float jump=phase<.65f ? Mathf.Sin(phase/.65f*Mathf.PI) : 0f;
                    lift=Mathf.Max(0,jump)*HopHeight;scale.y*=1f+Mathf.Max(0,jump)*.05f;break;
                case CCBuilderActivity.Fallen: case CCBuilderActivity.Sleeping: lean=90;lift=_agent.height*.25f;break;
                case CCBuilderActivity.GettingUp: lean=90*(1-Mathf.Clamp01(age/.65f));lift=_agent.height*.25f*(1-Mathf.Clamp01(age/.65f));break;
                case CCBuilderActivity.Collecting: pitch=45*Mathf.Sin(Mathf.Clamp01(age/.7f)*Mathf.PI);break;
                case CCBuilderActivity.Operating: pitch=8;break;
                case CCBuilderActivity.Resting: scale.y*=.9f;break;
            }
            Visual.localPosition=_visualBase+Vector3.up*lift;Visual.localRotation=_visualRotation*Quaternion.Euler(pitch,0,lean);Visual.localScale=scale;
        }
        // Carrying never transfers ownership or parents a scene NetworkObject to a player.
        public bool ServerTake(CCBuilderCatCarrier carrier)
        {
            if(!IsServerInitialized || IsCarried || carrier==null || !carrier.IsServerInitialized) return false;
            if(ExternalBusy)
            {
                CCBuilderExcavatorOperator op=_machine.Value.GetComponent<CCBuilderExcavatorOperator>();
                if(op!=null) op.Cancel();else ServerLeaveMachine(_lastGround);
            }
            if(!NavigationReady) return false;
            _lastGround=transform.position;
            CCBuilderWorksite worksite=Director!=null ? Director.GetComponent<CCBuilderWorksite>() : null;
            if(worksite!=null) worksite.Interrupt(this);
            HoldPosition();_agent.enabled=false;
            _carrier.Value=carrier.NetworkObject;_carrierBridge=carrier;_bridgeObject=_carrier.Value;
            SetActivity(CCBuilderActivity.Carried,transform.position);ApplyCarryPose();Publish();return true;
        }
        private void ApplyCarryPose()
        {
            NetworkObject carrier=_carrier.Value;if(carrier==null) return;
            if(_bridgeObject!=carrier || _carrierBridge==null)
            { _bridgeObject=carrier;_carrierBridge=carrier.GetComponent<CCBuilderCatCarrier>(); }
            if(_carrierBridge!=null) transform.SetPositionAndRotation(_carrierBridge.CatPosition,_carrierBridge.CatRotation);
        }
        public bool BeginMachine(CCBuilderExcavatorOperator op)
        {
            if(!IsServerInitialized || ExternalBusy || IsCarried || !NavigationReady || op==null) return false;
            if(Director!=null) { CCBuilderWorksite site=Director.GetComponent<CCBuilderWorksite>();if(site!=null) site.Interrupt(this); }
            HoldPosition();_machine.Value=op.NetworkObject;_machineBridge=op;return true;
        }
        public void BoardMachine()
        {
            if(!IsServerInitialized || !ExternalBusy) return;
            HoldPosition();_lastGround=transform.position;_agent.enabled=false;_seated.Value=true;
            SetActivity(CCBuilderActivity.Operating,transform.position);ApplyMachinePose();Publish();
        }
        public void ServerLeaveMachine(Vector3 near)
        {
            if(!IsServerInitialized) return;
            _machine.Value=null;_machineBridge=null;_seated.Value=false;ServerRelease(near,true);
        }
        private void ApplyMachinePose()
        {
            if(_machine.Value==null) return;
            if(_machineBridge==null || _machineBridge.NetworkObject!=_machine.Value)
                _machineBridge=_machine.Value.GetComponent<CCBuilderExcavatorOperator>();
            if(_machineBridge!=null && _machineBridge.CatSeat!=null)
                transform.SetPositionAndRotation(_machineBridge.CatSeat.position,_machineBridge.CatSeat.rotation);
        }
        public Vector3 ScatterGround(Vector3 desired)
        {
            NavMeshQueryFilter filter=new NavMeshQueryFilter { agentTypeID=_agent.agentTypeID,areaMask=_agent.areaMask };
            if(NavMesh.SamplePosition(desired,out NavMeshHit hit,.4f,filter) && Mathf.Abs(hit.position.y-_lastGround.y)<.3f &&
                !NavMesh.Raycast(_lastGround,hit.position,out NavMeshHit obstruction,filter)) return hit.position;
            return _lastGround;
        }
        private bool ClearAt(Vector3 ground)
        {
            float radius=Mathf.Max(.1f,_agent.radius*.9f),height=Mathf.Max(radius*2+.02f,_agent.height);
            int n=Physics.OverlapCapsuleNonAlloc(ground+Vector3.up*(radius+.06f),ground+Vector3.up*(height-radius),radius,
                _clearance,RecoveryMask,QueryTriggerInteraction.Ignore);
            if(n>=_clearance.Length) return false;
            for(int i=0;i<n;i++)
            {
                Collider c=_clearance[i];if(c==null || c.transform.IsChildOf(transform)) continue;
                return false;
            }
            return true;
        }
        private bool SampleSafe(Vector3 ground,out Vector3 result,bool mustReachHome)
        {
            result=default;
            NavMeshQueryFilter filter=new NavMeshQueryFilter { agentTypeID=_agent.agentTypeID,areaMask=_agent.areaMask };
            if(!NavMesh.SamplePosition(ground,out NavMeshHit hit,.7f,filter) || Mathf.Abs(hit.position.y-ground.y)>.55f || !ClearAt(hit.position)) return false;
            if(!Physics.Raycast(hit.position+Vector3.up*.3f,Vector3.down,out RaycastHit support,.65f,RecoveryMask,QueryTriggerInteraction.Ignore) ||
                Mathf.Abs(support.point.y-hit.position.y)>.25f) return false;
            if(mustReachHome && _hasHome && (!NavMesh.CalculatePath(hit.position,_homeGround,filter,_path) || _path.status!=NavMeshPathStatus.PathComplete)) return false;
            result=hit.position;return true;
        }
        private bool ActivateAt(Vector3 ground)
        {
            _agent.enabled=false;transform.position=ground;_agent.enabled=true;
            if(!_agent.isOnNavMesh || !_agent.Warp(ground)) { _agent.enabled=false;return false; }
            _agent.speed=WalkSpeed*(.94f+(CatId%7)*.02f);_agent.isStopped=true;
            _lastGround=ground;
            if(!_hasHome) { _homeGround=ground;_hasHome=true; }
            _pendingDestination=_hasDestination=false;SetActivity(CCBuilderActivity.Idle,ground+transform.forward);Publish();return true;
        }
        public bool ServerRelease(Vector3 near,bool force)
        {
            if(!IsServerInitialized) return false;
            // Ground ray first, then a small typed NavMesh sample: never snap to the floor above.
            Vector3 safe=default;bool found=false;
            for(int i=0;i<8 && !found;i++)
            {
                float angle=i*Mathf.PI*.25f;
                Vector3 probe=near+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*(i==0?0:.7f);
                if(Physics.Raycast(probe+Vector3.up*.75f,Vector3.down,out RaycastHit hit,2.5f,RecoveryMask,QueryTriggerInteraction.Ignore))
                {
                    Vector3 a=near+Vector3.up*.5f,b=hit.point+Vector3.up*.5f;
                    if(Physics.Linecast(a,b,RecoveryMask,QueryTriggerInteraction.Ignore)) continue;
                    found=SampleSafe(hit.point,out safe,true);
                }
            }
            if(!found) found=SampleSafe(_lastGround,out safe,true) || (_hasHome && SampleSafe(_homeGround,out safe,false));
            if(!found && !force) return false;
            _carrier.Value=null;_carrierBridge=null;_bridgeObject=null;
            if(found && ActivateAt(safe)) return true;
            _agent.enabled=false;_pendingDestination=_hasDestination=false;
            SetActivity(CCBuilderActivity.Stranded,transform.position+transform.forward);Publish();return true;
        }
        public bool ServerRecover()
        {
            if(!IsServerInitialized || IsCarried || MachineSeated) return false;
            if(NavigationReady) return true;
            if(SampleSafe(transform.position,out Vector3 safe,true) || SampleSafe(_lastGround,out safe,true) ||
                (_hasHome && SampleSafe(_homeGround,out safe,false))) return ActivateAt(safe);
            SetActivity(CCBuilderActivity.Stranded,transform.position+transform.forward);return false;
        }
        public override void OnStopServer()
        {
            if(_machine.Value!=null)
            {
                CCBuilderExcavatorOperator op=_machine.Value.GetComponent<CCBuilderExcavatorOperator>();if(op!=null) op.Cancel();
            }
            _machine.Value=null;_seated.Value=false;_machineBridge=null;
            _carrier.Value=null;
            if (Director!=null) Director.Unregister(this);
            _agent.enabled=false;_pendingDestination=false;_hasDestination=false;
            base.OnStopServer();
        }
        public override void OnStopNetwork()
        {
            _pose.OnChange-=PoseChanged;_agent.enabled=false;_remoteReady=false;_lastMaterialGroup=-1;
            if (Visual!=null) { Visual.localPosition=_visualBase;Visual.localRotation=_visualRotation;Visual.localScale=_visualScale; }
            base.OnStopNetwork();
        }
    }
}
