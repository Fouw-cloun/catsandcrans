using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.FoodLab
{
    [Serializable] public sealed class FoodParticle
    {
        public int id, batch, grams, profile;
        public Vector3 position, previous, velocity, display, target;
        public int heldBy = -1;
        public bool pinned;
        public Vector3 gripOffset, contactNormal, boundaryVelocity;
        public float contactFriction;
        public float Mobility => pinned || heldBy >= 0 ? 0 : 1;
    }
    public sealed class FoodBond
    {
        public FoodParticle a, b;
        public float rest, lambda;
        public ulong key;
    }
    // Coarse PBD contact/cohesion + XPBD bonds; deliberately NOT advertised as a full SPH/PBF solver.
    // All pairs use equal mechanical particle mass; grams are an exact, separate accounting quantity.
    public sealed class CCFoodSolver
    {
        public readonly List<FoodParticle> particles = new List<FoodParticle>();
        public readonly List<FoodBond> bonds = new List<FoodBond>();
        readonly Dictionary<Vector3Int, List<int>> grid = new Dictionary<Vector3Int, List<int>>();
        readonly Stack<List<int>> pool = new Stack<List<int>>();
        readonly List<Vector2Int> pairs = new List<Vector2Int>();
        readonly HashSet<ulong> links = new HashSet<ulong>();
        Vector3[] velocityChange = new Vector3[0];
        int[] neighbourCount = new int[0];
        public int NextId = 1, NextBatch = 1;
        public float spacing = .055f;
        public int iterations = 5;
        public int brokenBonds;
        float healClock;
        public long TotalGrams { get { long sum=0; foreach (var p in particles) sum+=p.grams; return sum; } }
        public long BatchGrams(int batch) { long n=0; foreach(var p in particles) if(p.batch==batch)n+=p.grams; return n; }
        public static ulong Key(int a,int b) => ((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);
        public void Clear() { particles.Clear(); bonds.Clear(); links.Clear(); pairs.Clear(); brokenBonds=0; }
        public void RemoveBatch(int batch)
        {
            particles.RemoveAll(p=>p.batch==batch);
            for(int k=bonds.Count-1;k>=0;k--) if(bonds[k].a.batch==batch || bonds[k].b.batch==batch) RemoveBond(k);
        }
        public void AddBonds(CCFoodProfile[] profiles, bool onlyNewContacts=false)
        {
            BuildPairs(spacing*1.65f);
            var degrees=new Dictionary<int,int>();
            foreach(var b in bonds) { Increment(degrees,b.a.id); Increment(degrees,b.b.id); }
            foreach(var ij in pairs)
            {
                var a=particles[ij.x];var b=particles[ij.y];var m=profiles[a.profile];
                if(a.profile!=b.profile || !m.UsesBonds || (onlyNewContacts&&!m.healContacts))continue;
                if(a.heldBy!=b.heldBy || a.pinned!=b.pinned)continue;
                if((a.position-b.position).sqrMagnitude>spacing*spacing*(onlyNewContacts?1.3f:2.15f))continue;
                if(degrees.TryGetValue(a.id,out int da)&&da>=14 || degrees.TryGetValue(b.id,out int db)&&db>=14)continue;
                ulong key=Key(a.id,b.id);if(!links.Add(key))continue;
                bonds.Add(new FoodBond{a=a,b=b,key=key,rest=Mathf.Max(spacing*.6f,Vector3.Distance(a.position,b.position))});
                Increment(degrees,a.id);Increment(degrees,b.id);
            }
        }
        static void Increment(Dictionary<int,int> d,int k) { d.TryGetValue(k,out int n);d[k]=n+1; }
        void RemoveBond(int i) { links.Remove(bonds[i].key);bonds.RemoveAt(i); }
        public void CutGrip(int owner)
        {
            for(int k=bonds.Count-1;k>=0;k--)
                if((bonds[k].a.heldBy==owner)!=(bonds[k].b.heldBy==owner)) {RemoveBond(k);brokenBonds++;}
        }
        void BuildPairs(float cell)
        {
            foreach(var list in grid.Values){list.Clear();pool.Push(list);}grid.Clear();pairs.Clear();
            for(int i=0;i<particles.Count;i++)
            {
                Vector3Int key=Vector3Int.FloorToInt(particles[i].position/cell);
                if(!grid.TryGetValue(key,out var list)){list=pool.Count>0?pool.Pop():new List<int>(12);grid.Add(key,list);}list.Add(i);
            }
            for(int i=0;i<particles.Count;i++)
            {
                Vector3Int key=Vector3Int.FloorToInt(particles[i].position/cell);
                for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)
                    if(grid.TryGetValue(key+new Vector3Int(x,y,z),out var list))
                        foreach(int j in list) if(j>i&&(particles[i].position-particles[j].position).sqrMagnitude<cell*cell)
                            pairs.Add(new Vector2Int(i,j));
            }
        }
        public void Step(float dt, CCFoodProfile[] profiles, List<CCFoodBoundary> boundaries, Bounds domain)
        {
            if(dt<=0 || particles.Count==0)return;
            float radius=spacing*.46f;
            foreach(var boundary in boundaries) if(boundary!=null&&boundary.isActiveAndEnabled)boundary.Prepare(dt);
            foreach(var p in particles)
            {
                p.previous=p.position;p.contactNormal=Vector3.zero;p.boundaryVelocity=Vector3.zero;p.contactFriction=0;
                if(p.Mobility>0){p.velocity+=Vector3.down*(9.81f*dt);p.velocity=Vector3.ClampMagnitude(p.velocity,spacing*.65f/dt);p.position+=p.velocity*dt;}
            }
            foreach(var b in bonds)b.lambda=0;
            for(int iteration=0;iteration<iterations;iteration++)
            {
                BuildPairs(spacing*1.7f);
                foreach(var ij in pairs)
                {
                    var a=particles[ij.x];var b=particles[ij.y];float weights=a.Mobility+b.Mobility;if(weights==0)continue;
                    Vector3 d=b.position-a.position;float len=d.magnitude;
                    Vector3 axis=len>1e-7f?d/len:((a.id&1)==0?Vector3.right:Vector3.forward);
                    var ma=profiles[a.profile];var mb=profiles[b.profile];
                    float contact=spacing*(ma.behaviour==FoodBehaviour.Granular||mb.behaviour==FoodBehaviour.Granular?.94f:.88f);
                    float error=len-contact;
                    if(error<0)
                    {
                        Vector3 correction=axis*(error/weights);a.position+=correction*a.Mobility;b.position-=correction*b.Mobility;
                        // Tangential positional friction gives loose piles an angle of repose.
                        Vector3 relative=(b.position-b.previous)-(a.position-a.previous);
                        Vector3 tangent=relative-axis*Vector3.Dot(relative,axis);
                        float friction=Mathf.Min(ma.friction,mb.friction);
                        Vector3 stick=Vector3.ClampMagnitude(tangent, -error*friction)/weights;
                        a.position+=stick*a.Mobility;b.position-=stick*b.Mobility;
                    }
                    else if(len<spacing*1.5f && a.profile==b.profile && !ma.UsesBonds)
                    {
                        // Short-range attraction is art-directed, not a physical surface tension coefficient.
                        float strength=ma.cohesion*(1-len/(spacing*1.5f))*dt*18/iterations;
                        Vector3 correction=axis*(Mathf.Min(error,spacing*.12f)*strength/weights);
                        a.position+=correction*a.Mobility;b.position-=correction*b.Mobility;
                    }
                }
                for(int k=bonds.Count-1;k>=0;k--)
                {
                    var b=bonds[k];var m=profiles[b.a.profile];Vector3 d=b.b.position-b.a.position;float length=d.magnitude;
                    if(length>b.rest*m.tearStretch){RemoveBond(k);brokenBonds++;continue;}
                    float weights=b.a.Mobility+b.b.Mobility;if(weights==0||length<1e-7f)continue;
                    float alpha=m.compliance/(dt*dt), delta=(-(length-b.rest)-alpha*b.lambda)/(weights+alpha);
                    delta=Mathf.Clamp(delta,-spacing*.2f,spacing*.2f);b.lambda+=delta;
                    Vector3 correction=d/length*delta;b.a.position-=correction*b.a.Mobility;b.b.position+=correction*b.b.Mobility;
                }
                foreach(var p in particles)
                {
                    if(p.Mobility==0)continue;
                    foreach(var boundary in boundaries)
                        if(boundary!=null&&boundary.isActiveAndEnabled&&boundary.Project(ref p.position,radius,out var n,out var v))
                        {p.contactNormal=n;p.boundaryVelocity=v;p.contactFriction=boundary.friction;}
                    Vector3 min=domain.min+Vector3.one*radius,max=domain.max-Vector3.one*radius;
                    Vector3 q=new Vector3(Mathf.Clamp(p.position.x,min.x,max.x),Mathf.Clamp(p.position.y,min.y,max.y),Mathf.Clamp(p.position.z,min.z,max.z));
                    if(q!=p.position){p.contactNormal=(q-p.position).normalized;p.contactFriction=.6f;p.position=q;}
                }
            }
            foreach(var b in bonds)
            {
                var m=profiles[b.a.profile];float length=Vector3.Distance(b.a.position,b.b.position);
                if(length>b.rest*m.yieldStretch || length<b.rest/m.yieldStretch)
                    b.rest=Mathf.Clamp(Mathf.Lerp(b.rest,length,1-Mathf.Exp(-m.plasticity*dt)),spacing*.65f,spacing*2);
            }
            foreach(var p in particles)
            {
                if(p.Mobility==0){p.velocity=Vector3.zero;continue;}
                p.velocity=(p.position-p.previous)/dt;
                if(p.contactNormal.sqrMagnitude>.1f)
                {
                    Vector3 v=p.velocity-p.boundaryVelocity;float dot=Vector3.Dot(v,p.contactNormal);
                    if(dot<0)v-=p.contactNormal*dot;
                    Vector3 normal=p.contactNormal*Vector3.Dot(v,p.contactNormal);
                    p.velocity=normal+(v-normal)*Mathf.Exp(-p.contactFriction*45*dt)+p.boundaryVelocity;
                }
            }
            int count=particles.Count;
            if(velocityChange.Length<count){Array.Resize(ref velocityChange,count*2);Array.Resize(ref neighbourCount,count*2);}
            Array.Clear(velocityChange,0,count);Array.Clear(neighbourCount,0,count);
            // Symmetric velocity exchange avoids order-dependent one-sided smoothing.
            foreach(var ij in pairs)
            {neighbourCount[ij.x]++;neighbourCount[ij.y]++;}
            foreach(var ij in pairs)
            {
                var a=particles[ij.x];var b=particles[ij.y];
                if(a.Mobility==0||b.Mobility==0)continue;
                float mix=1-Mathf.Exp(-Mathf.Min(profiles[a.profile].viscosity,profiles[b.profile].viscosity)*dt);
                Vector3 dv=(b.velocity-a.velocity)*(mix/Mathf.Max(1,Mathf.Max(neighbourCount[ij.x],neighbourCount[ij.y])));
                velocityChange[ij.x]+=dv;velocityChange[ij.y]-=dv;
            }
            for(int i=0;i<count;i++)
            {
                var p=particles[i];p.velocity=Vector3.ClampMagnitude((p.velocity+velocityChange[i])*.999f,spacing*.65f/dt);
                if(!Finite(p.position)){p.position=p.previous;p.velocity=Vector3.zero;}
            }
            healClock+=dt;if(healClock>.3f){healClock=0;AddBonds(profiles,true);}
        }
        public static bool Finite(Vector3 p)=> !(float.IsNaN(p.x)||float.IsNaN(p.y)||float.IsNaN(p.z)||float.IsInfinity(p.x)||float.IsInfinity(p.y)||float.IsInfinity(p.z));
    }
}
