using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FishNet.Object;
using FishNet.Object.Synchronizing;
namespace CatCooks.RandomConstruction
{
    [DisallowMultipleComponent,RequireComponent(typeof(NetworkObject))]
    public sealed class CCRandomConstruction:NetworkBehaviour
    {
        public CCBuildSettings Settings=CCBuildSettings.Default;
        public int PreviewSeed=1729;
        public bool RandomizeOnHost=true;
        public Material AtlasMaterial;
        public bool CastShadows=false;
        [Tooltip("Only explicitly selected OLD generated geometry, never cranes/crews/player or this root.")]
        public GameObject[] ReplacedGeometry=new GameObject[0];
        [Header("Stable points inside the protected quarter")]
        public Transform PlayerSpawn,KitchenAnchor,ReceivingA,ReceivingB,CrewStaging,LiftStop;
        [HideInInspector] public GameObject Generated;
        [HideInInspector] public UnityEngine.Object PreviewBake;
        [TextArea] public string Status="Create preview in Inspector";
        public struct Snapshot
        {public int Version,Revision,Seed,Width,Depth,SafeCorner;public float Bay,Height,BridgeWidth,JumpGap;public bool DisableJumpShortcut;public byte[] Completed;}
        readonly SyncVar<Snapshot> _snapshot=new SyncVar<Snapshot>(new SyncTypeSettings(.15f));
        CCBuildGraph _graph;
        byte[] _states;
        bool[] _connected;
        int _revision,_appliedRevision;
        readonly Dictionary<int,GameObject> _chunks=new Dictionary<int,GameObject>();
        readonly Dictionary<int,Lease> _leases=new Dictionary<int,Lease>();
        int[] _delivered;
        sealed class Lease {public Component Worker;public float Expiry,Started=-1;}
        public event Action MapReady;
        public event Action<int> TaskCompleted;
        public CCBuildGraph Graph=>_graph;
        public int ActiveSeed {get;private set;}
        public bool IsReady=>_graph!=null&&_states!=null;
        public bool IsDone(int taskId)=>IsReady&&taskId>=0&&taskId<_states.Length&&_states[taskId]!=0;
        public byte[] CopyStates()=>_states==null?null:(byte[])_states.Clone();
        public Vector3 WorldPoint(CCBuildTask task)=>transform.TransformPoint(task.LocalPoint);
        public bool Valid(out string error)
        {
            error="";
            if(AtlasMaterial==null)error="Assign Atlas Material using the setup window";
            else if((transform.lossyScale-Vector3.one).sqrMagnitude>.00001f||Vector3.Dot(transform.up,Vector3.up)<.9999f)error="Root/parents: scale 1,1,1; only Y rotation";
            foreach(var old in ReplacedGeometry)if(old!=null&&(old==gameObject||transform.IsChildOf(old.transform)||old.transform.IsChildOf(transform)))error="Old geometry must be separate from the new generator";
            return error.Length==0;
        }
        public override void OnStartServer()
        {
            base.OnStartServer();_revision=0;
            if(!Valid(out string error)){Status=error;Debug.LogError("[Random construction] "+error,this);return;}
            GenerateServer(RandomizeOnHost?Guid.NewGuid().GetHashCode():PreviewSeed);
        }
        // Regeneration is deliberately unavailable once workers/players have entered a running map.
        void GenerateServer(int seed)
        {
            _leases.Clear();var graph=new CCBuildGraph(Settings,seed);var states=graph.InitialState();
            string problem=graph.Validate(states);if(problem.Length>0){Status=problem;Debug.LogError(problem,this);return;}
            var s=graph.Settings;
            var snap=new Snapshot{Version=2,Revision=++_revision,Seed=seed,Width=s.Width,Depth=s.Depth,SafeCorner=s.SafeCorner,Bay=s.Bay,Height=s.Height,BridgeWidth=s.BridgeWidth,JumpGap=s.JumpGap,DisableJumpShortcut=s.DisableJumpShortcut,Completed=states};
            _delivered=new int[graph.Tasks.Count];_snapshot.Value=snap;ApplySnapshot(snap,true);
        }
        public override void OnStopNetwork(){base.OnStopNetwork();_appliedRevision=0;_leases.Clear();}
        void Update()
        {
            if(NetworkObject==null||IsServerInitialized||!IsClientInitialized)return;
            Snapshot s=_snapshot.Value;if(s.Revision>0&&s.Revision!=_appliedRevision)ApplySnapshot(s,false);
        }
        public void BuildPreview()
        {
            if(Application.isPlaying)return;
            if(!Valid(out string error))throw new InvalidOperationException(error);
            var s=Settings.Sanitized();var graph=new CCBuildGraph(s,PreviewSeed);var state=graph.InitialState();
            ApplySnapshot(new Snapshot{Version=2,Revision=1,Seed=PreviewSeed,Width=s.Width,Depth=s.Depth,SafeCorner=s.SafeCorner,Bay=s.Bay,Height=s.Height,BridgeWidth=s.BridgeWidth,JumpGap=s.JumpGap,DisableJumpShortcut=s.DisableJumpShortcut,Completed=state},true);
        }
        void ApplySnapshot(Snapshot data,bool rebuild)
        {
            if(data.Version!=2){Status="Unsupported map protocol version; Host/client packages differ";return;}
            var settings=new CCBuildSettings{Width=data.Width,Depth=data.Depth,Bay=data.Bay,Height=data.Height,SafeCorner=data.SafeCorner,BridgeWidth=data.BridgeWidth,JumpGap=data.JumpGap,DisableJumpShortcut=data.DisableJumpShortcut}.Sanitized();
            bool full=rebuild||_graph==null||Generated==null||_chunks.Count==0||ActiveSeed!=data.Seed||_graph.Settings.Width!=settings.Width||_graph.Settings.Depth!=settings.Depth||_graph.Settings.Bay!=settings.Bay||_graph.Settings.Height!=settings.Height||_graph.Settings.SafeCorner!=settings.SafeCorner||_graph.Settings.BridgeWidth!=settings.BridgeWidth||_graph.Settings.JumpGap!=settings.JumpGap||_graph.Settings.DisableJumpShortcut!=settings.DisableJumpShortcut;
            var graph=full?new CCBuildGraph(settings,data.Seed):_graph;
            string error=graph.Validate(data.Completed);if(error.Length>0){Status="Rejected map snapshot: "+error;Debug.LogError(Status,this);return;}
            var changed=new HashSet<int>();
            if(!full)for(int i=0;i<data.Completed.Length;i++)if(_states[i]!=data.Completed[i])MarkChunks(changed,graph.Tasks[i],settings);
            _graph=graph;_states=(byte[])data.Completed.Clone();ActiveSeed=data.Seed;_connected=graph.ConnectedPlayerFloor(_states);
            if(full)
            {
                ClearGenerated();Generated=new GameObject("Generated random construction — owned meshes");Generated.transform.SetParent(transform,false);Generated.layer=gameObject.layer;
                int nx=(settings.Width+1)/2,nz=(settings.Depth+1)/2;
                for(int l=0;l<4;l++)for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)RebuildChunk(Key(l,x,z));
                UpdateAnchors(settings);
            }
            else foreach(int id in changed)RebuildChunk(id);
            foreach(var old in ReplacedGeometry)if(old!=null&&old!=gameObject&&!transform.IsChildOf(old.transform)&&!old.transform.IsChildOf(transform))old.SetActive(false);
            _appliedRevision=data.Revision;
            int done=0;foreach(byte value in _states)if(value!=0)done++;
            Status="Seed="+ActiveSeed+" | levels=4 | tasks="+done+"/"+_states.Length+" | chunks="+_chunks.Count+" | safe quarter="+settings.SafeCorner+" | "+graph.Plan.Describe();
            if(full)MapReady?.Invoke();
        }
        static int Key(int level,int x,int z)=>level*10000+z*100+x;
        void MarkChunks(HashSet<int> set,CCBuildTask t,CCBuildSettings s)
        {
            int x=Mathf.Min(t.X,s.Width-1)/2,z=Mathf.Min(t.Z,s.Depth-1)/2;
            set.Add(Key(t.Level,x,z));
            // Neighbor rails may disappear when a floor cell is completed.
            if(t.Kind==CCBuildKind.Deck||t.Kind==CCBuildKind.CureFloor)
            {if(x>0)set.Add(Key(t.Level,x-1,z));if(z>0)set.Add(Key(t.Level,x,z-1));if(x+1<(s.Width+1)/2)set.Add(Key(t.Level,x+1,z));if(z+1<(s.Depth+1)/2)set.Add(Key(t.Level,x,z+1));}
        }
        void RebuildChunk(int key)
        {
            int level=key/10000,z=key%10000/100,x=key%100;
            var data=CCBuildGeometry.Chunk(_graph,_states,level,x,z,2);
            if(!_chunks.TryGetValue(key,out GameObject go)||go==null)
            {go=new GameObject("Level "+level+" chunk "+x+","+z);go.transform.SetParent(Generated.transform,false);go.layer=gameObject.layer;go.AddComponent<MeshFilter>();go.AddComponent<MeshRenderer>();go.AddComponent<MeshCollider>();_chunks[key]=go;}
            var mf=go.GetComponent<MeshFilter>();var mc=go.GetComponent<MeshCollider>();var mr=go.GetComponent<MeshRenderer>();
            Mesh oldVisual=mf.sharedMesh,oldCollision=mc.sharedMesh;mc.sharedMesh=null;mf.sharedMesh=null;
            DisposeMesh(oldVisual);DisposeMesh(oldCollision);
            if(data.Visual.VertexCount>0)mf.sharedMesh=data.Visual.ToMesh(go.name+" visual");
            if(data.Collision.VertexCount>0)mc.sharedMesh=data.Collision.ToMesh(go.name+" collision");
            mr.sharedMaterial=AtlasMaterial;mr.shadowCastingMode=CastShadows?ShadowCastingMode.On:ShadowCastingMode.Off;mr.receiveShadows=CastShadows;mr.lightProbeUsage=LightProbeUsage.Off;mr.reflectionProbeUsage=ReflectionProbeUsage.Off;
            mr.enabled=mf.sharedMesh!=null;mc.enabled=mc.sharedMesh!=null;
        }
        void ClearGenerated()
        {
            if(Generated!=null)
            {
                foreach(var f in Generated.GetComponentsInChildren<MeshFilter>(true))DisposeMesh(f.sharedMesh);
                foreach(var c in Generated.GetComponentsInChildren<MeshCollider>(true))DisposeMesh(c.sharedMesh);
                Generated.SetActive(false);if(Application.isPlaying)Destroy(Generated);else DestroyImmediate(Generated);
            }
            _chunks.Clear();
        }
        static void DisposeMesh(Mesh mesh)
        {
            if(mesh==null)return;
#if UNITY_EDITOR
            if(UnityEditor.EditorUtility.IsPersistent(mesh))return;
#endif
            if(Application.isPlaying)UnityEngine.Object.Destroy(mesh);else UnityEngine.Object.DestroyImmediate(mesh);
        }
        void OnDestroy(){if(Application.isPlaying)ClearGenerated();}
        Transform Anchor(Transform existing,string name,Vector3 position)
        {if(existing==null){var go=new GameObject(name);go.transform.SetParent(transform,false);existing=go.transform;}existing.localPosition=position;existing.localRotation=Quaternion.identity;return existing;}
        void UpdateAnchors(CCBuildSettings s)
        {
            Vector3 centre=s.SafeCentre;float d=s.Bay*.35f;
            PlayerSpawn=Anchor(PlayerSpawn,"Player spawn — wait IsReady",centre+Vector3.up*.15f);
            KitchenAnchor=Anchor(KitchenAnchor,"Kitchen anchor",centre+new Vector3(-d,0,-d));
            ReceivingA=Anchor(ReceivingA,"Receiving A reference",centre+new Vector3(d,0,-d));
            ReceivingB=Anchor(ReceivingB,"Receiving B reference",centre+new Vector3(d,0,d));
            CrewStaging=Anchor(CrewStaging,"Crew staging reference",centre+new Vector3(-d,0,d));
            LiftStop=Anchor(LiftStop,"Future lift stop reference",centre+new Vector3(0,0,d));
        }
        // An element target (e.g. beam top) is NOT a cat's foot position.
        // Return a conservative nearby cured-floor cell; navigation/ladder/lift remains explicit.
        public bool TryGetWorkFoot(int taskId,out Vector3 world)
        {
            world=Vector3.zero;if(!IsReady||taskId<0||taskId>=_graph.Tasks.Count)return false;
            var t=_graph.Tasks[taskId];var s=_graph.Settings;
            int l=t.Kind==CCBuildKind.Column||t.Kind==CCBuildKind.Beam||t.Kind==CCBuildKind.Brace||t.Kind==CCBuildKind.SteelCoat?t.Level-1:t.Level;
            float best=float.PositiveInfinity;Vector3 point=Vector3.zero;bool found=false;
            for(int z=t.Z-1;z<=t.Z+1;z++)for(int x=t.X-1;x<=t.X+1;x++)
            {
                if(x<0||z<0||x>=s.Width||z>=s.Depth||!_graph.Done(_states,"F",l,x,z))continue;
                if(l==1&&!_connected[z*s.Width+x])continue;
                var p=new Vector3((x+.5f)*s.Bay,s.Y(l),(z+.5f)*s.Bay);float distance=(p-t.LocalPoint).sqrMagnitude;
                if(distance<best){best=distance;point=p;found=true;}
            }
            if(found)world=transform.TransformPoint(point);return found;
        }
        public bool ReadyForWork(int id)
        {return IsReady&&id>=0&&id<_states.Length&&_states[id]==0&&!_graph.Tasks[id].Protected&&_graph.DependenciesDone(id,_states);}
        public CCAccessNeed AccessFor(int id)=>_graph.Access(_graph.Tasks[id],_states,_connected);
        // Caller-owned list: no allocation per worker scan. Sort lower floors before upper work.
        public void CollectReady(List<int> result,int max=32,int levelMask=15,bool onlyWalkable=false)
        {
            result.Clear();if(!IsReady||max<=0)return;
            for(int level=0;level<4;level++)if((levelMask&(1<<level))!=0)
                foreach(var t in _graph.Tasks)
                {if(t.Level!=level||!ReadyForWork(t.Id)||LeaseAlive(t.Id))continue;if(onlyWalkable&&AccessFor(t.Id)!=CCAccessNeed.Floor)continue;result.Add(t.Id);if(result.Count>=max)return;}
        }
        bool LeaseAlive(int id)
        {
            if(!_leases.TryGetValue(id,out Lease l))return false;
            if(l.Worker==null||!l.Worker.gameObject.activeInHierarchy||Time.time>l.Expiry||(l.Worker is Behaviour b&&!b.enabled)){_leases.Remove(id);return false;}return true;
        }
        public bool ServerTryClaim(int id,Component worker,float seconds=30)
        {
            if(NetworkObject==null||!IsServerInitialized||worker==null||!worker.gameObject.activeInHierarchy||!ReadyForWork(id)||LeaseAlive(id))return false;
            _leases[id]=new Lease{Worker=worker,Expiry=Time.time+Mathf.Clamp(seconds,2,120)};return true;
        }
        bool Owned(int id,Component worker)=>NetworkObject!=null&&IsServerInitialized&&worker!=null&&LeaseAlive(id)&&_leases[id].Worker==worker;
        public bool ServerRenew(int id,Component worker,float seconds=30)
        {if(!Owned(id,worker))return false;_leases[id].Expiry=Time.time+Mathf.Clamp(seconds,2,120);return true;}
        public bool ServerDeliver(int id,int units)
        {
            if(NetworkObject==null||!IsServerInitialized||!ReadyForWork(id)||units<=0||_delivered==null)return false;
            int remaining=_graph.Tasks[id].MaterialUnits-_delivered[id];if(units>remaining)return false;_delivered[id]+=units;return true;
        }
        public bool ServerBeginWork(int id,Component worker)
        {
            if(!Owned(id,worker)||!ReadyForWork(id)||_delivered[id]<_graph.Tasks[id].MaterialUnits)return false;
            if(_leases[id].Started<0)_leases[id].Started=Time.time;return true;
        }
        public bool ServerCompleteWork(int id,Component worker)
        {
            if(!Owned(id,worker)||!ReadyForWork(id))return false;Lease l=_leases[id];
            if(l.Started<0||Time.time-l.Started<_graph.Tasks[id].WorkSeconds||_delivered[id]<_graph.Tasks[id].MaterialUnits)return false;
            var data=_snapshot.Value;data.Completed=(byte[])_states.Clone();data.Completed[id]=1;data.Revision=++_revision;
            _leases.Remove(id);ApplySnapshot(data,false);_snapshot.Value=data;TaskCompleted?.Invoke(id);return true;
        }
        public bool ServerRelease(int id,Component worker)
        {if(!Owned(id,worker))return false;_leases.Remove(id);return true;}
        public string WorkReport(int limit=24)
        {
            if(!IsReady)return "No generated graph. Build preview / start Host.";
            var list=new List<int>();CollectReady(list,limit);var b=new System.Text.StringBuilder(Status+"\n");
            foreach(int id in list){var t=_graph.Tasks[id];b.AppendLine(id+" "+t.Key+" "+t.Kind+" | floor="+t.Level+" | access="+AccessFor(id)+" | cargo="+t.Cargo+" x"+t.MaterialUnits+" | crane="+t.Crane+" | world="+WorldPoint(t));}
            return b.ToString();
        }
        void OnDrawGizmosSelected()
        {
            var s=Settings.Sanitized();Gizmos.matrix=transform.localToWorldMatrix;
            Gizmos.color=Color.green;Gizmos.DrawWireCube(s.SafeCentre+Vector3.up*.1f,new Vector3(s.Width*s.Bay/2,.2f,s.Depth*s.Bay/2));
            var plan=IsReady?_graph.Plan:new CCZonePlan(s,PreviewSeed);
            for(int z=0;z<s.Depth;z++)for(int x=0;x<s.Width;x++)
            {
                int zone=plan.Zone(x,z);if(zone<0)continue;
                Gizmos.color=zone==0?new Color(.2f,1,.4f,.35f):new Color(1,.65f,.1f,.35f);
                Gizmos.DrawWireCube(new Vector3((x+.5f)*s.Bay,.05f,(z+.5f)*s.Bay),new Vector3(s.Bay,.1f,s.Bay));
                if(plan.FloorAllowed(2,x,z)){Gizmos.color=new Color(.4f,.65f,1,.4f);Gizmos.DrawWireCube(new Vector3((x+.5f)*s.Bay,s.Height,(z+.5f)*s.Bay),new Vector3(s.Bay,.1f,s.Bay));}
            }
            foreach(var bridge in plan.Bridges){Gizmos.color=bridge.Gap>0?Color.magenta:Color.cyan;Gizmos.DrawLine(bridge.A+Vector3.up*.15f,bridge.B+Vector3.up*.15f);}
            Gizmos.matrix=Matrix4x4.identity;
        }
    }
}
