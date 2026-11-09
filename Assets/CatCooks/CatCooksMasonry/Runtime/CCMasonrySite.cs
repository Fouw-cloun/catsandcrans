using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using CatCooks.CraneLogistics;

namespace CatCooks.Masonry
{
    [DisallowMultipleComponent, RequireComponent(typeof(NetworkObject))]
    public sealed class CCMasonrySite : NetworkBehaviour
    {
        public CCWorkTerritory Territory;
        public CCWorkZone Receiving;
        public CCMasonryWall Wall;
        [Tooltip("0/1 brick carriers, 2/3 mortar mixers/carriers, 4/5 masons. Roots at feet.")]
        public Transform[] Cats=new Transform[6];
        public Transform[] HeldItems=new Transform[6];
        public Transform[] MixingTools=new Transform[2];
        [Tooltip("Two brick drop points, two mixing points, two mason work points. Same floor.")]
        public Transform[] Posts=new Transform[6];
        [Tooltip("Optional intermediate WALK points: depot to brick post (one path per carrier).")]
        public Transform[] BrickPath0=new Transform[0],BrickPath1=new Transform[0];
        [Tooltip("Optional intermediate WALK points: mixer to mason (one path per mixer).")]
        public Transform[] MortarPath0=new Transform[0],MortarPath1=new Transform[0];
        public Transform DepotPartialPile,WorkBrickPile,MortarPile;
        public bool Run=true;
        public bool SingleMixer=true;
        [Tooltip("The shared crane route in DemandDriven mode. Leave empty for legacy stock delivery.")]
        public CCZoneCraneRoute DemandRoute;
        [Min(.1f)] public float WalkSpeed=1.5f,CarrySpeed=1.1f;
        [Min(.1f)] public float MixSeconds=4,PlaceSeconds=2,UnloadSeconds=.6f;
        [Min(1)] public int BricksPerStockUnit=12,BricksPerCarry=4,BrickBufferLimit=16,MortarPerBucket=4,MortarBufferLimit=12;
        [Min(.05f)] public float SnapshotInterval=.2f;
        [TextArea] public string Status="Not started";
        public struct Actor { public Vector3 Position; public float Yaw,Timer; public byte Stage; public int Carry,Waypoint; }
        public struct Snapshot { public bool Ready; public Actor[] Actors; public int Built,LooseBricks,BrickBuffer,Mortar; }
        readonly SyncVar<Snapshot> _snapshot=new SyncVar<Snapshot>(new SyncTypeSettings(.1f));
        Actor[] _actors;
        int _loose,_brickBuffer,_mortar,_built;
        float _nextSend;
        bool _ready;
        public int Built=>Application.isPlaying&&NetworkObject!=null?(IsServerInitialized?_built:_snapshot.Value.Built):0;
        public bool ValidateSetup(out string error)
        {
            error="";
            if(Territory==null||Receiving==null||Wall==null)error="Assign Territory, Receiving and Wall";
            else if(Receiving.Owner!=Territory||!Receiving.AcceptsDelivery||Receiving.Resource!=CCZoneResource.Bricks)error="Receiving must be a Bricks Receiving/Work zone of this Territory";
            else if(transform.lossyScale!=Vector3.one||Vector3.Dot(transform.up,Vector3.up)<.999f)error="Site root must be upright with Scale 1,1,1; resize the Wall using Width/Height";
            else if(Cats==null||Cats.Length!=6||Posts==null||Posts.Length!=6||HeldItems==null||HeldItems.Length!=6)error="Exactly six Cats, Posts and HeldItems slots required";
            else
            {
                for(int i=0;i<6;i++)
                {
                    if(i==3&&SingleMixer)continue;
                    if(Cats[i]==null||Posts[i]==null||HeldItems[i]==null){error="Missing role reference "+i;break;}
                    for(int j=0;j<i;j++)if(Cats[j]==Cats[i])error="Two roles share the same cat";
                    if(Mathf.Abs(Posts[i].position.y-Receiving.StagingPosition.y)>.3f)error="All posts and receiving staging must be on the same floor";
                }
            }
            if(error.Length==0)
            {
                bool registered=false;foreach(var z in Territory.Zones)if(z==Receiving)registered=true;
                if(!registered)error="Receiving is not registered. Refresh territory before Play";
                foreach(var path in new[]{BrickPath0,BrickPath1,MortarPath0,MortarPath1})
                    if(path!=null)foreach(var point in path)if(point!=null&&Mathf.Abs(point.position.y-Receiving.StagingPosition.y)>.3f)error="Walking waypoints must stay on the same floor";
            }
            return error.Length==0;
        }
        public override void OnStartServer()
        {
            base.OnStartServer();_ready=false;
            if(!ValidateSetup(out string error)){Status=error;Debug.LogError("[Masonry] "+error,this);return;}
            _actors=new Actor[6];_loose=_brickBuffer=_mortar=_built=0;
            for(int i=0;i<6;i++)if(Posts[i]!=null)_actors[i]=new Actor{Position=Posts[i].position,Yaw=Posts[i].eulerAngles.y};
            _ready=true;_nextSend=0;Publish();
        }
        public override void OnStopNetwork(){base.OnStopNetwork();_ready=false;_actors=null;}
        void Update()
        {
            if(NetworkObject==null)return;
            if(IsServerInitialized)
            {
                if(!_ready)return;
                if(Run&&Territory!=null&&Territory.isActiveAndEnabled&&Territory.IsServerInitialized&&Receiving!=null&&Receiving.isActiveAndEnabled&&_built<Wall.Total)
                {
                    float dt=Time.deltaTime;
                    for(int i=0;i<2;i++)TickBrick(i,dt);
                    for(int i=2;i<(SingleMixer?3:4);i++)TickMortar(i,dt);
                    for(int i=4;i<6;i++)TickMason(i,dt);
                    Status=_built>=Wall.Total?"Wall complete":_brickBuffer==0?"Working / waiting for bricks":_mortar==0?"Working / waiting for mortar":"Building";
                }
                else Status=_built>=Wall.Total?"Wall complete":!Run?"Paused":"Territory/receiving unavailable";
                if(Territory!=null&&Territory.NetworkObject!=null&&Territory.IsServerInitialized&&DemandRoute!=null)
                {
                    int need=Wall.Total-_built-_loose-_brickBuffer-CarryingBricks()-MasonReservations();
                    bool request=Run&&Receiving!=null&&Receiving.isActiveAndEnabled&&need>0;
                    if(!Territory.ServerSetDemand(this,DemandRoute,Receiving,request?2:0))Status="Demand route/receiving not configured";
                }
                if(Time.time>=_nextSend){Publish();_nextSend=Time.time+Mathf.Max(.05f,SnapshotInterval);}
                Draw(new Snapshot{Ready=true,Actors=_actors,Built=_built,LooseBricks=_loose,BrickBuffer=_brickBuffer,Mortar=_mortar},false);
            }
            else if(IsClientInitialized)Draw(_snapshot.Value,true);
        }
        int CarryingBricks(){return _actors[0].Carry+_actors[1].Carry;}
        int MortarReserved(){int n=0;for(int i=2;i<(SingleMixer?3:4);i++)if(_actors[i].Stage==1||_actors[i].Stage==2)n+=_actors[i].Carry;return n;}
        Vector3 Depot(int lane)=>Receiving.StagingPosition+Receiving.transform.right*(lane==0?-.45f:.45f);
        void TickBrick(int i,float dt)
        {
            Actor a=_actors[i];Transform[] path=i==0?BrickPath0:BrickPath1;
            if(a.Stage==0) // empty trip to receiving pile
            {
                if(Follow(ref a,path,Depot(i),true,WalkSpeed,dt))a.Stage=1;
            }
            else if(a.Stage==1)
            {
                int remaining=Wall.Total-_built-_brickBuffer-CarryingBricks()-MasonReservations();
                int room=Mathf.Min(BrickBufferLimit-_brickBuffer-CarryingBricks(),remaining);
                if(room>0)
                {
                    if(_loose==0&&Territory.ServerConsume(Receiving,1))_loose+=Mathf.Max(1,BricksPerStockUnit);
                    int n=Mathf.Min(Mathf.Max(1,BricksPerCarry),Mathf.Min(room,_loose));
                    if(n>0){_loose-=n;a.Carry=n;a.Stage=2;a.Waypoint=0;}
                }
            }
            else if(a.Stage==2)
            {
                if(Follow(ref a,path,Posts[i].position,false,CarrySpeed,dt)){a.Stage=3;a.Timer=UnloadSeconds;}
            }
            else if(a.Stage==3)
            {
                a.Timer-=dt;if(a.Timer<=0){_brickBuffer+=a.Carry;a.Carry=0;a.Stage=0;a.Waypoint=0;}
            }
            _actors[i]=a;
        }
        void TickMortar(int i,float dt)
        {
            Actor a=_actors[i];Transform[] path=i==2?MortarPath0:MortarPath1;
            if(a.Stage==0)
            {
                int n=Mathf.Min(Mathf.Max(1,MortarPerBucket),MortarBufferLimit-_mortar-MortarReserved());
                if(n>0&&_built+MasonReservations()<Wall.Total){a.Carry=n;a.Stage=1;a.Timer=MixSeconds;}
            }
            else if(a.Stage==1){a.Timer-=dt;if(a.Timer<=0){a.Stage=2;a.Waypoint=0;}}
            else if(a.Stage==2)
            {
                if(Follow(ref a,path,Posts[i+2].position,false,CarrySpeed,dt)){_mortar+=a.Carry;a.Carry=0;a.Stage=3;a.Waypoint=0;}
            }
            else if(a.Stage==3&&Follow(ref a,path,Posts[i].position,true,WalkSpeed,dt))a.Stage=0;
            _actors[i]=a;
        }
        int MasonReservations(){return (_actors[4].Stage==1?1:0)+(_actors[5].Stage==1?1:0);}
        void TickMason(int i,float dt)
        {
            Actor a=_actors[i];
            if(a.Stage==0&&_built+MasonReservations()<Wall.Total&&_brickBuffer>0&&_mortar>0)
            {_brickBuffer--;_mortar--;a.Stage=1;a.Timer=Mathf.Max(.1f,PlaceSeconds);}
            else if(a.Stage==1){a.Timer-=dt;if(a.Timer<=0){_built++;a.Stage=0;}}
            _actors[i]=a;
        }
        static bool Follow(ref Actor a,Transform[] path,Vector3 end,bool reverse,float speed,float dt)
        {
            int count=path==null?0:path.Length;
            while(a.Waypoint<count&&path[reverse?count-1-a.Waypoint:a.Waypoint]==null)a.Waypoint++;
            Vector3 target=a.Waypoint<count?path[reverse?count-1-a.Waypoint:a.Waypoint].position:end;
            Vector3 delta=target-a.Position;
            if(delta.sqrMagnitude>.0001f){Vector3 flat=delta;flat.y=0;if(flat.sqrMagnitude>.001f)a.Yaw=Quaternion.LookRotation(flat).eulerAngles.y;}
            a.Position=Vector3.MoveTowards(a.Position,target,Mathf.Max(.1f,speed)*dt);
            if((a.Position-target).sqrMagnitude>.0001f)return false;
            if(a.Waypoint<count){a.Waypoint++;return false;}
            a.Waypoint=0;return true;
        }
        void Publish(){_snapshot.Value=new Snapshot{Ready=true,Actors=(Actor[])_actors.Clone(),Built=_built,LooseBricks=_loose,BrickBuffer=_brickBuffer,Mortar=_mortar};}
        void Draw(Snapshot s,bool smooth)
        {
            if(!s.Ready||s.Actors==null||s.Actors.Length!=6)return;
            Wall.Show(s.Built);
            for(int i=0;i<6;i++)
            {
                if(i==3&&SingleMixer){if(Cats[i]!=null)Cats[i].gameObject.SetActive(false);continue;}
                if(Cats[i]==null||HeldItems[i]==null)continue;
                if(!Cats[i].gameObject.activeSelf)Cats[i].gameObject.SetActive(true);
                Actor a=s.Actors[i];float f=smooth?1-Mathf.Exp(-14*Time.deltaTime):1;
                Cats[i].position=Vector3.Lerp(Cats[i].position,a.Position,f);
                float bend=s.Built<Wall.Total&&((i>=4&&a.Stage==1)||(i>=2&&i<4&&a.Stage==1))?Mathf.Sin(Time.time*5+i)*9:0;
                Cats[i].rotation=Quaternion.Slerp(Cats[i].rotation,Quaternion.Euler(bend,a.Yaw,0),f);
                bool held=i<2?a.Carry>0:i<4?a.Stage!=1:true;
                if(HeldItems[i].gameObject.activeSelf!=held)HeldItems[i].gameObject.SetActive(held);
                if(i==2||i==3)HeldItems[i].localRotation=Quaternion.Euler(a.Stage==1?Mathf.Sin(Time.time*5+i)*20:0,0,0);
                if(i>=2&&i<4&&MixingTools!=null&&MixingTools.Length>i-2&&MixingTools[i-2]!=null)
                {
                    MixingTools[i-2].gameObject.SetActive(a.Stage==1);
                    MixingTools[i-2].localRotation=Quaternion.Euler(Mathf.Sin(Time.time*5+i)*25,0,0);
                }
            }
            SetPile(DepotPartialPile,s.LooseBricks,Mathf.Max(1,BricksPerStockUnit));
            SetPile(WorkBrickPile,s.BrickBuffer,Mathf.Max(1,BrickBufferLimit));
            SetPile(MortarPile,s.Mortar,Mathf.Max(1,MortarBufferLimit));
        }
        static void SetPile(Transform p,int count,int max)
        {
            if(p==null)return;bool show=count>0;if(p.gameObject.activeSelf!=show)p.gameObject.SetActive(show);
            p.localScale=new Vector3(1,Mathf.Max(.05f,(float)count/max),1);
        }
        void OnDrawGizmosSelected()
        {
            if(Receiving!=null&&Posts!=null&&Posts.Length==6)
            {Gizmos.color=Color.yellow;for(int i=0;i<2;i++)if(Posts[i]!=null)Gizmos.DrawLine(Receiving.StagingPosition,Posts[i].position);}
        }
    }
}
