using FishNet.Object;
using UnityEngine;
namespace CatCooks.Builders
{
    [RequireComponent(typeof(ExcavatorVehicle)),DisallowMultipleComponent]
    public sealed class CCBuilderExcavatorOperator : NetworkBehaviour
    {
        public CCBuilderCat Cat;
        public CCBuilderWorksite Worksite;
        public Transform BoardPoint,CatSeat,BucketTip,ParkingA,ParkingB;
        public CCBuilderSandPile Source,Target;
        [Header("Углы: башня / стрела / рукоять / ковш")]
        public Vector4 ScoopPose=new Vector4(-30,-25,35,-40);
        public Vector4 DumpPose=new Vector4(30,-10,20,60);
        public Vector4 TravelPose=Vector4.zero;
        [Min(.1f)] public float BucketReach=2.2f;
        [Min(.1f)] public float SandPerBucket=8,ScoopSeconds=2,DumpSeconds=1.5f;
        [Min(5)] public float PoseTimeout=20,TravelTimeout=35;
        [Header("Короткие переезды по свободной площадке")]
        public bool AllowDriving=true;
        [Min(1)] public int RepositionEveryCycles=3;
        [Range(.01f,.3f)] public float DriveInput=.1f;
        [Min(.1f)] public float MaxSpeed=.7f;
        [Min(.1f)] public float ArrivalRadius=.2f;
        public LayerMask ObstacleMask=~0;
        public Vector3 ProbeHalfExtents=new Vector3(2.6f,.65f,2.6f);
        public enum Stage { Idle,Approach,Scoop,Dig,Dump,Unload,Return,Travel }
        public Stage State { get; private set; }
        private ExcavatorVehicle _vehicle;
        private CCBuilderDirector _director;
        private CCBuilderCat _activeCat;
        private float _next,_deadline,_retry;
        private int _cycles;
        private bool _atB,_sourceReached;
        private readonly RaycastHit[] _hits=new RaycastHit[24];
        private void Awake() { _vehicle=GetComponent<ExcavatorVehicle>(); }
        public override void OnStartServer()
        {
            base.OnStartServer();State=Stage.Idle;_activeCat=null;_cycles=0;_atB=false;_sourceReached=false;_next=_retry=0;
        }
        private bool WorkingHours()
        {
            if(Worksite==null || !Worksite.isActiveAndEnabled || Cat==null || !Cat.IsServerInitialized || !Cat.isActiveAndEnabled || Cat.IsCarried) return false;
            if(_director==null) _director=Worksite.GetComponent<CCBuilderDirector>();
            return _director!=null && _director.isActiveAndEnabled && _director.RunSchedule && Cat.Director==_director && Worksite.PhaseFor(Cat.GroupId)==CCBuilderWorksite.ShiftPhase.Work;
        }
        private bool Ready => _vehicle!=null && _vehicle.IsServerInitialized && BoardPoint!=null && CatSeat!=null && BucketTip!=null &&
            Source!=null && Target!=null && Source!=Target && Source.IsServerInitialized && Target.IsServerInitialized &&
            Source.isActiveAndEnabled && Target.isActiveAndEnabled && Source.Amount>.01f && Target.Free>.01f;
        private void Update()
        {
            if(!IsServerInitialized || Time.time<_next) return;
            _next=Time.time+.1f;
            if(!WorkingHours() || !Ready || (_activeCat!=null && Cat!=_activeCat)) { Cancel();return; }
            if(State==Stage.Idle)
            {
                if(Time.time<_retry || _vehicle.HasDriver || _vehicle.HasNpcDriver || Cat.ExternalBusy || !Cat.NavigationReady) return;
                if(!Cat.BeginMachine(this)) return;
                _activeCat=Cat;Cat.RequestMove(BoardPoint.position,transform.position);Enter(Stage.Approach,TravelTimeout);return;
            }
            if(_activeCat==null || !_activeCat.IsServerInitialized || _activeCat.MachineObject!=NetworkObject || _vehicle.HasDriver) { Cancel();return; }
            if(Time.time>_deadline) { Cancel();return; }
            if(State==Stage.Approach)
            {
                if(Cat.Arrived)
                {
                    if(!_vehicle.ServerAcquireNpc(Cat.NetworkObject)) { Cancel();return; }
                    Cat.BoardMachine();Enter(Stage.Scoop,PoseTimeout);
                }
                else if(Cat.PathFailed) Cancel();
                return;
            }
            if(!Cat.MachineSeated || !_vehicle.HasNpcDriver) { Cancel();return; }
            switch(State)
            {
                case Stage.Scoop:
                    Command(ScoopPose);
                    if(AtPose(ScoopPose) && Near(Source)) { _sourceReached=true;Enter(Stage.Dig,ScoopSeconds+2);_nextAction=Time.time+Mathf.Max(.1f,ScoopSeconds); }
                    break;
                case Stage.Dig:
                    Command(ScoopPose);
                    if(!Near(Source)) { _sourceReached=false;Enter(Stage.Scoop,PoseTimeout); }
                    else if(Time.time>=_nextAction) Enter(Stage.Dump,PoseTimeout);
                    break;
                case Stage.Dump:
                    Command(DumpPose);
                    if(AtPose(DumpPose) && Near(Target)) { Enter(Stage.Unload,DumpSeconds+2);_nextAction=Time.time+Mathf.Max(.1f,DumpSeconds); }
                    break;
                case Stage.Unload:
                    Command(DumpPose);
                    if(!Near(Target)) Enter(Stage.Dump,PoseTimeout);
                    else if(Time.time>=_nextAction)
                    {
                        if(_sourceReached) Source.TransferTo(Target,SandPerBucket);
                        _sourceReached=false;_cycles++;Enter(Stage.Return,PoseTimeout);
                    }
                    break;
                case Stage.Return:
                    Command(TravelPose);
                    if(AtPose(TravelPose))
                    {
                        if(AllowDriving && ParkingA!=null && ParkingB!=null && _cycles%Mathf.Max(1,RepositionEveryCycles)==0)
                            Enter(Stage.Travel,TravelTimeout);
                        else Enter(Stage.Scoop,PoseTimeout);
                    }
                    break;
                case Stage.Travel: Drive();break;
            }
        }
        private float _nextAction;
        private void Enter(Stage state,float timeout) { State=state;_deadline=Time.time+Mathf.Max(.5f,timeout); }
        private void Command(Vector4 pose,float move=0,float turn=0,bool drive=false)
        { _vehicle.ServerNpcControl(_activeCat.NetworkObject,pose,move,turn,drive,.1f); }
        private bool AtPose(Vector4 pose)
        {
            Vector4 actual=_vehicle.CurrentArmPose;pose=_vehicle.ClampNpcPose(pose);
            return Mathf.Abs(Mathf.DeltaAngle(actual.x,pose.x))<2 && Mathf.Abs(actual.y-pose.y)<2 &&
                Mathf.Abs(actual.z-pose.z)<2 && Mathf.Abs(actual.w-pose.w)<2;
        }
        private bool Near(CCBuilderSandPile pile) => Vector3.Distance(BucketTip.position,pile.transform.position+Vector3.up*.35f)<=Mathf.Max(.1f,BucketReach);
        private void Drive()
        {
            Transform parking=_atB?ParkingA:ParkingB;
            if(!AllowDriving || parking==null) { Command(TravelPose);Enter(Stage.Scoop,PoseTimeout);return; }
            Vector3 d=parking.position-transform.position;d.y=0;
            if(d.magnitude<=Mathf.Max(.1f,ArrivalRadius))
            {
                Command(TravelPose);
                if(_vehicle.BodySpeed<.15f) { _atB=!_atB;Enter(Stage.Scoop,PoseTimeout); }
                return;
            }
            Vector3 local=transform.InverseTransformDirection(d.normalized);float direction=local.z>=0?1:-1;
            float angle=Mathf.Atan2(local.x*direction,Mathf.Abs(local.z))*Mathf.Rad2Deg;
            if(!ClearTravel(direction)) { Command(TravelPose);return; }
            float move=_vehicle.BodySpeed>MaxSpeed || Mathf.Abs(angle)>40?0:direction*Mathf.Clamp(DriveInput,.01f,.3f);
            Command(TravelPose,move,Mathf.Clamp(angle/60,-.12f,.12f),true);
        }
        private bool ClearTravel(float sign)
        {
            Vector3 origin=transform.position+Vector3.up*1.2f,direction=transform.forward*sign;
            Vector3 half=new Vector3(Mathf.Max(.2f,ProbeHalfExtents.x),Mathf.Max(.1f,ProbeHalfExtents.y),Mathf.Max(.2f,ProbeHalfExtents.z));
            int count=Physics.BoxCastNonAlloc(origin,half,direction,_hits,transform.rotation,.65f,ObstacleMask,QueryTriggerInteraction.Ignore);
            if(count>=_hits.Length) return false;
            for(int i=0;i<count;i++)
            {
                Transform hit=_hits[i].collider.transform;
                if(!hit.IsChildOf(transform) && (_activeCat==null || !hit.IsChildOf(_activeCat.transform))) return false;
            }
            Vector3 probe=transform.position+direction*(half.z+.7f)+Vector3.up;
            return Physics.Raycast(probe,Vector3.down,out RaycastHit ground,2.5f,_vehicle.groundMask,QueryTriggerInteraction.Ignore) &&
                !ground.transform.IsChildOf(transform) && Vector3.Dot(ground.normal,Vector3.up)>.85f;
        }
        public void Cancel()
        {
            if(!IsServerInitialized) return;
            if(_activeCat!=null)
            {
                _vehicle.ServerReleaseNpc(_activeCat.NetworkObject);
                if(_activeCat.IsServerInitialized && _activeCat.MachineObject==NetworkObject)
                    _activeCat.ServerLeaveMachine(BoardPoint!=null?BoardPoint.position:_activeCat.LastGroundPosition);
            }
            else if(State!=Stage.Idle) _vehicle.ServerReleaseNpc(null);
            if(State!=Stage.Idle) _retry=Time.time+8;
            _activeCat=null;_sourceReached=false;State=Stage.Idle;
        }
        private void OnDisable() { Cancel(); }
        public override void OnStopServer() { Cancel();base.OnStopServer(); }
    }
}
