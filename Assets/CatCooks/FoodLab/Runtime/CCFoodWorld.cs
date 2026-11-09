using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.FoodLab
{
    public enum FoodCommandKind { Spawn, Grab, Scoop, Drag, Release, Stir, Push, Pin, Unpin, Clear }
    [Serializable] public struct FoodCommand
    {
        public FoodCommandKind kind;
        public Vector3 point;
        public float radius, strength;
        public int profile, grams;
    }
    public sealed class CCFoodWorld : MonoBehaviour
    {
        public CCFoodProfile[] presets;
        [Range(.015f,.09f)] public float particleSpacing=.04f;
        [Min(1)] public int gramsPerParticle=35;
        [Range(64,4096)] public int maxParticles=1280;
        [Range(2,8)] public int iterations=5;
        [Range(1,6)] public int substeps=3;
        public Bounds domain=new Bounds(new Vector3(0,1.2f,0),new Vector3(6,3,4));
        public bool paused;
        [NonSerialized] public bool networkControlled, simulate=true;
        [NonSerialized] public string status="Готов к опыту";
        public CCFoodSolver Solver {get;private set;} = new CCFoodSolver();
        public CCFoodProfile[] Profiles {get;private set;}
        public readonly List<CCFoodBoundary> boundaries=new List<CCFoodBoundary>();
        public event Action<FoodCommand> CommandRequested;
        public event Action<int,int> BatchSpawned;
        public event Action<int,int> ScoopReleased;
        public float lastStepMs {get;private set;}
        sealed class Grip { public Vector3 point; public float lastTime; }
        readonly Dictionary<int,Grip> grips=new Dictionary<int,Grip>();
        readonly List<int> expired=new List<int>();
        void Awake(){Initialize();}
        public void Initialize()
        {
            if(Profiles!=null)return;
            if(presets==null||presets.Length==0||presets.Length>256){enabled=false;status="Назначь 1–256 Profiles или создай стенд через Tools/CatCooks/Food Lab";return;}
            for(int i=0;i<presets.Length;i++)
                if(presets[i]==null){enabled=false;status="Пустой профиль "+i;return;}
            Profiles=new CCFoodProfile[presets.Length];
            for(int i=0;i<presets.Length;i++)
            {
                Profiles[i]=Instantiate(presets[i]);
            }
            enabled=true;
            Solver.spacing=particleSpacing;Solver.iterations=iterations;
        }
        void OnDestroy(){if(Profiles!=null)foreach(var p in Profiles)if(p!=null)Destroy(p);}
        public void Register(CCFoodBoundary b){if(!boundaries.Contains(b))boundaries.Add(b);}
        public void Unregister(CCFoodBoundary b){boundaries.Remove(b);}
        public void Submit(FoodCommand command)
        {
            if(networkControlled){CommandRequested?.Invoke(command);return;}
            Execute(command,0);
        }
        public void Execute(FoodCommand c,int owner)
        {
            Initialize();if(!enabled||!simulate||Profiles==null)return;
            if(!CCFoodSolver.Finite(c.point)||!domain.Contains(c.point)||!Enum.IsDefined(typeof(FoodCommandKind),c.kind))return;
            if(float.IsNaN(c.radius)||float.IsInfinity(c.radius)||float.IsNaN(c.strength)||float.IsInfinity(c.strength))return;
            float radius=Mathf.Clamp(c.radius,particleSpacing,.4f),power=Mathf.Clamp(c.strength,0,4);
            if(c.kind==FoodCommandKind.Spawn){Spawn(c.profile,c.grams,c.point);return;}
            if(c.kind==FoodCommandKind.Clear){Solver.Clear();grips.Clear();return;}
            if(c.kind==FoodCommandKind.Release){Release(owner);return;}
            if(c.kind==FoodCommandKind.Grab||c.kind==FoodCommandKind.Scoop)
            {
                Release(owner);grips[owner]=new Grip{point=c.point,lastTime=Time.unscaledTime};int n=0;
                foreach(var p in Solver.particles)
                    if(!p.pinned&&p.heldBy<0&&(p.position-c.point).sqrMagnitude<radius*radius&&n++<100)
                    {p.heldBy=owner;p.gripOffset=p.position-c.point;p.velocity=Vector3.zero;}
                if(c.kind==FoodCommandKind.Scoop)Solver.CutGrip(owner);
                return;
            }
            if(c.kind==FoodCommandKind.Drag)
            {
                if(grips.TryGetValue(owner,out var grip))
                {grip.point=c.point;grip.lastTime=Time.unscaledTime;}
                return;
            }
            foreach(var p in Solver.particles)
            {
                Vector3 d=p.position-c.point;float distance=d.magnitude;if(distance>radius)continue;
                if(p.heldBy>=0&&p.heldBy!=owner)continue;
                if(c.kind==FoodCommandKind.Pin){p.pinned=true;p.velocity=Vector3.zero;continue;}
                if(c.kind==FoodCommandKind.Unpin){p.pinned=false;continue;}
                if(p.Mobility==0)continue;
                float weight=1-distance/radius;
                if(c.kind==FoodCommandKind.Stir)
                {
                    Vector3 tangent=Vector3.Cross(Vector3.up,d).normalized;
                    // Commands arrive at 20 Hz. Strength is velocity kick per command.
                    p.velocity+= (tangent*.18f+Vector3.up*.012f)*power*weight;
                }
                if(c.kind==FoodCommandKind.Push)
                    p.velocity+=(d.normalized*.13f+Vector3.down*.12f)*power*weight;
            }
        }
        // Exact ingredient accounting per batch. An accepted spawn always adds exactly grams.
        // A rejected spawn adds nothing; callers retain their source portion.
        public int Spawn(int profile,int grams,Vector3 centre)
        {
            if(!simulate||Profiles==null||profile<0||profile>=Profiles.Length||grams<=0||grams>200000||!CCFoodSolver.Finite(centre)||!domain.Contains(centre))return -1;
            int count=Mathf.CeilToInt(grams/(float)Mathf.Max(1,gramsPerParticle));
            if(count>maxParticles-Solver.particles.Count){status="Бюджет заполнен: порция не создана, масса не списана";return -1;}
            int batch=Solver.NextBatch++;
            int side=Mathf.CeilToInt(Mathf.Pow(count,1f/3));
            for(int i=0;i<count;i++)
            {
                Vector3 offset=new Vector3(i%side-(side-1)*.5f,i/(side*side),(i/side)%side-(side-1)*.5f)*particleSpacing;
                Vector3 pos=centre+offset;
                pos=new Vector3(Mathf.Clamp(pos.x,domain.min.x+.02f,domain.max.x-.02f),Mathf.Clamp(pos.y,domain.min.y+.02f,domain.max.y-.02f),Mathf.Clamp(pos.z,domain.min.z+.02f,domain.max.z-.02f));
                // Distribution differs by at most one gram. No zero-mass visible nodes.
                int mass=grams/count+(i<grams%count?1:0);
                Solver.particles.Add(new FoodParticle{id=Solver.NextId++,batch=batch,grams=mass,profile=profile,position=pos,previous=pos,display=pos,target=pos});
            }
            Solver.AddBonds(Profiles);BatchSpawned?.Invoke(batch,grams);status="Создана порция "+grams+" г";return batch;
        }
        public void Release(int owner)
        {
            int grams=0;
            foreach(var p in Solver.particles)if(p.heldBy==owner){p.heldBy=-1;grams+=p.grams;}
            grips.Remove(owner);if(grams>0)ScoopReleased?.Invoke(owner,grams);
        }
        public long HeldGrams(int owner){long n=0;foreach(var p in Solver.particles)if(p.heldBy==owner)n+=p.grams;return n;}
        void FixedUpdate()
        {
            if(!simulate||paused||Profiles==null)return;
            var timer=System.Diagnostics.Stopwatch.StartNew();
            expired.Clear();foreach(var grip in grips)if(Time.unscaledTime-grip.Value.lastTime>1.5f)expired.Add(grip.Key);
            foreach(int owner in expired)Release(owner);
            Solver.spacing=particleSpacing;Solver.iterations=iterations;
            float dt=Time.fixedDeltaTime/Mathf.Max(1,substeps);
            for(int s=0;s<substeps;s++)
            {
                foreach(var p in Solver.particles)
                    if(p.heldBy>=0&&grips.TryGetValue(p.heldBy,out var grip))
                        p.position=Vector3.MoveTowards(p.position,grip.point+p.gripOffset,particleSpacing*.6f);
                Solver.Step(dt,Profiles,boundaries,domain);
            }
            timer.Stop();lastStepMs=(float)timer.Elapsed.TotalMilliseconds;
        }
        void Update()
        {
            float t=1-Mathf.Exp(-Time.deltaTime*18);
            foreach(var p in Solver.particles)p.display=simulate?p.position:Vector3.Lerp(p.display,p.target,t);
        }
    }
}
