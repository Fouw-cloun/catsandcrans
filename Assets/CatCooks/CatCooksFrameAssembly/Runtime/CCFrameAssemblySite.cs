using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using CatCooks.CraneLogistics;
namespace CatCooks.FrameAssembly
{
    [RequireComponent(typeof(NetworkObject)),DisallowMultipleComponent]
    public sealed class CCFrameAssemblySite:NetworkBehaviour,ICCMountRequester
    {
        public CCWorkTerritory Territory;
        public CCZoneCraneRoute Route;
        [Tooltip("Ordered column BASES on one floor. Steel is delivered here, not to a Receiving zone.")]
        public Transform[] Nodes=new Transform[4];
        public Transform[] Columns=new Transform[4],Beams=new Transform[3];
        public Transform[] Cats=new Transform[2],Ladders=new Transform[2],CarriedLadders=new Transform[2],Hammers=new Transform[2],Torches=new Transform[2],WeldFlashes=new Transform[2];
        public bool Run=true;
        [Min(1)] public float ColumnHeight=3;
        [Min(.1f)] public float WalkSpeed=1.5f,ClimbSpeed=.7f,LadderSeconds=2,HammerSeconds=3,WeldSeconds=4;
        [Min(.1f)] public float WorkOffset=.8f;
        [Min(.1f)] public float SnapshotInterval=.2f;
        [TextArea] public string Status="Not started";
        public struct State
        {
            public bool Ready,Fastened,Detached;
            public uint Token;
            public int Completed,Phase;
            public float Progress;
            public Vector3 A,B;
            public float YawA,YawB;
        }
        readonly SyncVar<State> _net=new SyncVar<State>(new SyncTypeSettings(.1f));
        State _s;
        CCDirectMountJob _job;
        float _elapsed,_nextSend,_nextStatus;
        public bool MountRequestsEnabled=>Run&&isActiveAndEnabled;
        public int Total=>Nodes==null?0:Nodes.Length*2-1;
        public static bool IsColumn(int step)=>step<2||step%2==1;
        public static int PieceIndex(int step)=>step<2?step:IsColumn(step)?(step+1)/2:(step-2)/2;
        public int Completed=>Application.isPlaying&&NetworkObject!=null?(IsServerInitialized?_s.Completed:_net.Value.Completed):0;
        public CCDirectMountJob DescribeMount(int step)
        {
            int i=PieceIndex(step);bool column=IsColumn(step);
            Vector3 direction=column?transform.right:Nodes[i+1].position-Nodes[i].position;direction.y=0;
            Quaternion rotation=column?transform.rotation:Quaternion.LookRotation(Vector3.Cross(direction.normalized,Vector3.up),Vector3.up);
            Vector3 size=column?new Vector3(.45f,ColumnHeight,.45f):new Vector3(direction.magnitude,.3f,.3f);
            Vector3 top=column?Nodes[i].position+Vector3.up*ColumnHeight:(Nodes[i].position+Nodes[i+1].position)*.5f+Vector3.up*(ColumnHeight+.3f);
            return new CCDirectMountJob{Owner=this,Route=Route,Step=step,Resource=column?CCZoneResource.SteelColumn:CCZoneResource.SteelBeam,Top=top,Rotation=rotation,Size=size};
        }
        public bool ValidateSetup(out string error)
        {
            error="";
            if(Territory==null||Route==null||Route.Owner!=Territory||Route.Crane==null||Route.Crane.Territory!=Territory||!Route.DemandDriven)error="Assign this territory's crane and DemandDriven Route";
            else if(transform.lossyScale!=Vector3.one||Vector3.Dot(transform.up,Vector3.up)<.999f)error="Frame root must be upright and scale 1,1,1";
            else if(Nodes==null||Nodes.Length<2||Nodes.Length>16||Columns==null||Columns.Length!=Nodes.Length||Beams==null||Beams.Length!=Nodes.Length-1)error="Need 2..16 ordered Nodes, N Columns and N-1 Beams";
            else
            {
                for(int i=0;i<Nodes.Length;i++)
                {
                    if(Nodes[i]==null||Columns[i]==null){error="Missing node or column";break;}
                    if(Nodes[0]!=null&&Mathf.Abs(Nodes[i].position.y-Nodes[0].position.y)>.02f)error="All column bases must be on the same floor";
                    if(i>0&&Nodes[i-1]!=null&&Vector3.Distance(Nodes[i].position,Nodes[i-1].position)<1.5f)error="Adjacent column bases need at least 1.5 m spacing";
                    for(int j=0;j<i;j++)if(Nodes[j]==Nodes[i])error="Repeated column node";
                }
                foreach(var t in Beams)if(t==null)error="Missing beam visual";
                foreach(var list in new[]{Cats,Ladders,CarriedLadders,Hammers,Torches,WeldFlashes})
                {if(list==null||list.Length!=2){error="Each crew/tool/ladder array needs two references";break;}foreach(var t in list)if(t==null)error="Missing crew/tool/ladder reference";}
                foreach(var resource in new[]{CCZoneResource.SteelColumn,CCZoneResource.SteelBeam})
                {
                    bool found=false;for(int i=0;i<Route.SourceCount;i++){var z=Route.SourceAt(i);if(z!=null&&z.Owner==Territory&&z.Kind==CCZoneKind.Supply&&z.Resource==resource)found=true;}
                    if(!found)error="Route Sources missing "+resource;
                    var lib=Route.Crane.PayloadLibrary;
                    if(lib==null||lib.Find((int)resource)==null||lib.Find((int)resource).Visual==null)error="Crane payload model missing: "+resource;
                }
            }
            return error.Length==0;
        }
        // Editor-only caller may validate all installation tops without mutating live transforms.
        public bool ValidateReach(out string error)
        {
            if(!ValidateSetup(out error))return false;
            for(int step=0;step<Total;step++)
            {
                var j=DescribeMount(step);bool reachable=false;string why="No matching supply";
                for(int k=0;k<Route.SourceCount;k++)
                {var z=Route.SourceAt(k);if(z==null||z.Resource!=j.Resource)continue;if(Territory.ResolveMount(Route,z,j,out _,out _,out why))reachable=true;}
                if(!reachable){error="Step "+(step+1)+" "+j.Resource+": "+why;return false;}
            }
            error="";return true;
        }
        public override void OnStartServer()
        {
            base.OnStartServer();_s=default;_job=null;_elapsed=0;
            if(!ValidateSetup(out string error)){Status=error;Debug.LogError("[Frame crew] "+error,this);return;}
            _s=new State{Ready=true,A=Cats[0].position,B=Cats[1].position};_nextSend=0;_net.Value=_s;
        }
        public override void OnStopNetwork(){base.OnStopNetwork();_s=default;_job=null;}
        void Update()
        {
            if(NetworkObject==null)return;
            if(IsServerInitialized)
            {
                if(!_s.Ready)return;
                if(Run&&Territory!=null&&Territory.isActiveAndEnabled&&Territory.IsServerInitialized&&_s.Completed<Total)Tick(Time.deltaTime);
                else Status=_s.Completed>=Total?"Frame complete":!Run?"Paused; suspended part stays on crane":"Territory unavailable";
                if(Time.time>=_nextSend){_net.Value=_s;_nextSend=Time.time+Mathf.Max(.1f,SnapshotInterval);}
                Draw(_s,false);
            }
            else if(IsClientInitialized)Draw(_net.Value,true);
        }
        void Tick(float dt)
        {
            if(_job!=null&&_job.Cancelled&&!_job.Fastened){_job=null;Enter(0);}
            if(_job==null||_job.Step!=_s.Completed)
            {
                var d=DescribeMount(_s.Completed);
                _job=Territory.ServerRequestMount(this,Route,d.Step,d.Resource,d.Top,d.Rotation,d.Size);
                if(_job==null){Status="Waiting for previous crane job to clear / route unavailable";return;}
                _s.Token=_job.Token;
            }
            _s.Fastened=_job.Fastened;_s.Detached=_job.Detached;
            if(Time.unscaledTime>=_nextStatus){Status=Names[_s.Phase]+" | step "+(_s.Completed+1)+" / "+Total+" | job #"+_job.Token+" "+_job.Status;_nextStatus=Time.unscaledTime+.25f;}
            bool column=IsColumn(_s.Completed);
            if(_s.Phase==3)
            {
                if(_job.Holding)Enter(4);
                return;
            }
            if(_s.Phase==5)
            {
                if(_job.Detached){_s.Detached=true;Enter(6);}
                return;
            }
            if(_s.Phase==4&&!_job.Holding&&!_job.Fastened){Enter(3);return;}
            _elapsed+=dt;
            float duration=_s.Phase==4?HammerSeconds:_s.Phase==6?WeldSeconds:(_s.Phase==1||_s.Phase==8)?(column?.1f:LadderSeconds):1;
            _s.Progress=Mathf.Clamp01(_elapsed/Mathf.Max(.1f,duration));
            bool done=_s.Progress>=1;
            if(_s.Phase==0||_s.Phase==2||_s.Phase==7)
            {
                bool elevated=!column&&_s.Phase==2;
                Vector3 a=WorkPosition(_s.Completed,0,elevated),b=WorkPosition(_s.Completed,1,elevated);
                float speed=Mathf.Max(.1f,_s.Phase==0?WalkSpeed:ClimbSpeed);
                _s.A=Vector3.MoveTowards(_s.A,a,speed*dt);_s.B=Vector3.MoveTowards(_s.B,b,speed*dt);
                done=(_s.A-a).sqrMagnitude<.0001f&&(_s.B-b).sqrMagnitude<.0001f;
            }
            Vector3 look=_job.Top-Vector3.up*(_job.Size.y*.5f);
            _s.YawA=Yaw(look-_s.A);_s.YawB=Yaw(look-_s.B);
            if(!done)return;
            if(_s.Phase==4)
            {
                // Both cats reached their work positions before phase 3; both hammer for the full duration.
                if(!Territory.ServerFastenMount(this,_job))return;
                _s.Fastened=true;Enter(5);return;
            }
            if(_s.Phase==8)
            {_s.Completed++;_s.Fastened=false;_s.Detached=false;_s.Token=0;Enter(0);return;}
            Enter(_s.Phase+1);
        }
        static readonly string[] Names={"Walk to installation","Place beam ladders","Reach both bolt points","Waiting for crane to hold part","BOTH workers hammer bolts","Waiting for unhook","Weld independently of crane","Descend","Pack ladders"};
        void Enter(int phase){_s.Phase=phase;_s.Progress=0;_elapsed=0;_net.Value=_s;}
        Vector3 Foot(int step,int cat)
        {
            int i=PieceIndex(step);Vector3 right=transform.right,forward=transform.forward;
            if(!IsColumn(step)){Vector3 d=Nodes[i+1].position-Nodes[i].position;d.y=0;right=d.normalized;forward=Vector3.Cross(right,Vector3.up);}
            Vector3 p=IsColumn(step)?Nodes[i].position+right*(cat==0?-.65f:.65f):Nodes[i+cat].position;
            return p-forward*WorkOffset;
        }
        Vector3 WorkPosition(int step,int cat,bool elevated)=>Foot(step,cat)+(elevated?Vector3.up*Mathf.Max(.2f,ColumnHeight-.8f):Vector3.zero);
        static float Yaw(Vector3 dir){dir.y=0;return dir.sqrMagnitude>.001f?Quaternion.LookRotation(dir).eulerAngles.y:0;}
        void Draw(State s,bool smooth)
        {
            if(!s.Ready)return;
            bool detached=s.Detached;
            // Crane snapshot can arrive before the crew snapshot; use the same token for a clean handoff.
            if(Route!=null&&Route.Crane!=null&&s.Token!=0)
            {var c=Route.Crane.Current;if(c.DirectMount&&c.MountToken==s.Token&&c.DirectDetached)detached=true;}
            for(int step=0;step<Total;step++)
            {
                int i=PieceIndex(step);bool column=IsColumn(step);Transform part=column?Columns[i]:Beams[i];
                SetVisible(part,step<s.Completed||(step==s.Completed&&detached));
                Vector3 d=column?transform.right:Nodes[i+1].position-Nodes[i].position;d.y=0;
                Quaternion rot=column?transform.rotation:Quaternion.LookRotation(Vector3.Cross(d.normalized,Vector3.up),Vector3.up);
                Vector3 pos=column?Nodes[i].position:(Nodes[i].position+Nodes[i+1].position)*.5f+Vector3.up*(ColumnHeight+.15f);
                part.SetPositionAndRotation(pos,rot);part.localScale=column?new Vector3(1,ColumnHeight,1):new Vector3(d.magnitude,1,1);
            }
            for(int i=0;i<2;i++)
            {
                float f=smooth?1-Mathf.Exp(-14*Time.deltaTime):1;
                Cats[i].position=Vector3.Lerp(Cats[i].position,i==0?s.A:s.B,f);Cats[i].rotation=Quaternion.Slerp(Cats[i].rotation,Quaternion.Euler(0,i==0?s.YawA:s.YawB,0),f);
                bool working=s.Completed<Total;bool beam=working&&!IsColumn(s.Completed);bool ladder=beam&&s.Phase>=1&&s.Phase<=8;
                SetVisible(Ladders[i],ladder);SetVisible(CarriedLadders[i],working&&!ladder);
                if(beam)
                {
                    Vector3 d=Nodes[PieceIndex(s.Completed)+1].position-Nodes[PieceIndex(s.Completed)].position;d.y=0;
                    Ladders[i].SetPositionAndRotation(Foot(s.Completed,i),Quaternion.LookRotation(Vector3.Cross(d.normalized,Vector3.up),Vector3.up));
                    float grow=s.Phase==1?s.Progress:s.Phase==8?1-s.Progress:1;
                    Ladders[i].localScale=new Vector3(1,ColumnHeight*Mathf.Max(.05f,grow),1);
                }
                SetVisible(Hammers[i],working&&s.Phase==4);SetVisible(Torches[i],working&&s.Phase==6);
                Hammers[i].localRotation=Quaternion.Euler(Mathf.Sin(Time.time*10+i)*45,0,0);
                SetVisible(WeldFlashes[i],working&&s.Phase==6&&Mathf.Sin(Time.time*25+i)>.1f);
            }
        }
        static void SetVisible(Transform part,bool active){if(part.gameObject.activeSelf!=active)part.gameObject.SetActive(active);}
        void OnDrawGizmosSelected()
        {
            if(Nodes==null)return;Gizmos.color=Color.cyan;
            for(int i=0;i<Nodes.Length;i++)if(Nodes[i]!=null)
            {Gizmos.DrawWireSphere(Nodes[i].position,.2f);Gizmos.DrawLine(Nodes[i].position,Nodes[i].position+Vector3.up*ColumnHeight);if(i>0&&Nodes[i-1]!=null)Gizmos.DrawLine(Nodes[i-1].position+Vector3.up*(ColumnHeight+.15f),Nodes[i].position+Vector3.up*(ColumnHeight+.15f));}
        }
    }
}
