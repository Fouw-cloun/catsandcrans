using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatCooks.FoodLab
{
    [RequireComponent(typeof(MeshFilter),typeof(MeshRenderer))]
    public sealed class CCFoodSurface : MonoBehaviour
    {
        public CCFoodWorld world;
        public Material material;
        [Range(4,24)] public float refreshHz=10;
        [Range(.5f,1.5f)] public float voxelScale=.8f;
        [Range(.1f,.8f)] public float isoLevel=.34f;
        public int maxGridNodes=90000,maxVertices=180000;
        [Range(0,1)]public float inclusionScale=.35f;
        public Mesh inclusionMesh;
        public Material inclusionMaterial;
        public float lastBuildMs {get;private set;}
        public bool surfaceBudgetExceeded {get;private set;}
        struct Sample {public float density;public Vector3 gradient;public Vector4 color;public Vector2 properties;}
        readonly Dictionary<Vector3Int,Sample> field=new Dictionary<Vector3Int,Sample>();
        readonly HashSet<Vector3Int> cells=new HashSet<Vector3Int>();
        readonly List<Vector3> vertices=new List<Vector3>(),normals=new List<Vector3>();
        readonly List<Color> colors=new List<Color>();
        readonly List<Vector2> properties=new List<Vector2>();
        readonly List<int> triangles=new List<int>();
        readonly Vector3Int[] corners={new Vector3Int(0,0,0),new Vector3Int(1,0,0),new Vector3Int(1,1,0),new Vector3Int(0,1,0),new Vector3Int(0,0,1),new Vector3Int(1,0,1),new Vector3Int(1,1,1),new Vector3Int(0,1,1)};
        readonly int[,] tetra={{0,5,1,6},{0,1,2,6},{0,2,3,6},{0,3,7,6},{0,7,4,6},{0,4,5,6}};
        readonly Sample[] samples=new Sample[8];readonly Vector3[] points=new Vector3[8];
        readonly int[] inside=new int[4],outside=new int[4];
        Mesh mesh;float nextBuild;MaterialPropertyBlock block;
        readonly Matrix4x4[] matrices=new Matrix4x4[1023];
        void Awake()
        {
            mesh=new Mesh{name="CCFood dynamic surface",indexFormat=IndexFormat.UInt32};mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh=mesh;GetComponent<MeshRenderer>().sharedMaterial=material;
            block=new MaterialPropertyBlock();
        }
        void OnDestroy(){if(mesh!=null)Destroy(mesh);}
        void LateUpdate()
        {
            if(world==null||world.Profiles==null||SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)return;
            if(Time.unscaledTime>=nextBuild){nextBuild=Time.unscaledTime+1/refreshHz;Rebuild();}
            DrawInclusions();
        }
        public void Rebuild()
        {
            if(mesh==null||world==null)return;
            var timer=System.Diagnostics.Stopwatch.StartNew();
            float step=world.particleSpacing*voxelScale;
            bool success=false;
            // Retry with coarser cells, keeping the entire mass rather than truncating a random half.
            for(int attempt=0;attempt<4;attempt++)
            {if(BuildField(step)&&Polygonize(step)){success=true;break;}step*=1.4f;}
            surfaceBudgetExceeded=!success;
            if(success)
            {
                mesh.Clear();mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetColors(colors);mesh.SetUVs(0,properties);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
            }
            else mesh.Clear(); // No stale surface in an old location; diagnostic particles remain visible.
            timer.Stop();lastBuildMs=(float)timer.Elapsed.TotalMilliseconds;
        }
        bool BuildField(float step)
        {
            field.Clear();cells.Clear();
            foreach(var p in world.Solver.particles)
            {
                var profile=world.Profiles[p.profile];
                float support=world.particleSpacing*(profile.behaviour==FoodBehaviour.Granular?.8f:1.12f);
                // Even an isolated drop at a cell centre must reach the isosurface threshold
                // at its nearest grid corner; otherwise drops blink as they cross the grid.
                float sampleRadius=Mathf.Sqrt(Mathf.Max(.02f,1-Mathf.Pow(Mathf.Clamp(isoLevel,.01f,.95f),1f/3)));
                support=Mathf.Max(support,step*.91f/sampleRadius);
                Vector3 centre=p.display;float rr=support*support;
                Vector3Int lo=Vector3Int.FloorToInt((centre-Vector3.one*support)/step),hi=Vector3Int.CeilToInt((centre+Vector3.one*support)/step);
                float variation=.92f+.12f*Hash(p.id);Color col=profile.body*variation;
                for(int x=lo.x;x<=hi.x;x++)for(int y=lo.y;y<=hi.y;y++)for(int z=lo.z;z<=hi.z;z++)
                {
                    var key=new Vector3Int(x,y,z);Vector3 delta=(Vector3)key*step-centre;
                    float q=1-delta.sqrMagnitude/rr;if(q<=0)continue;
                    float weight=q*q*q;field.TryGetValue(key,out var sample);
                    sample.density+=weight;sample.gradient+=delta*(-6*q*q/rr);
                    sample.color+=(Vector4)col*weight;sample.properties+=new Vector2(profile.roughness,profile.inclusions)*weight;
                    field[key]=sample;if(field.Count>maxGridNodes)return false;
                }
            }
            foreach(var key in field.Keys)
                for(int i=0;i<8;i++)cells.Add(key-corners[i]);
            return true;
        }
        bool Polygonize(float step)
        {
            vertices.Clear();normals.Clear();colors.Clear();properties.Clear();triangles.Clear();
            foreach(var cell in cells)
            {
                int over=0;
                for(int i=0;i<8;i++){var key=cell+corners[i];field.TryGetValue(key,out samples[i]);points[i]=(Vector3)key*step;if(samples[i].density>=isoLevel)over++;}
                if(over==0||over==8)continue;
                for(int t=0;t<6;t++)
                {
                    int a=0,b=0;
                    for(int k=0;k<4;k++){int i=tetra[t,k];if(samples[i].density>=isoLevel)inside[a++]=i;else outside[b++]=i;}
                    if(a==0||a==4)continue;
                    if(a==1)Emit(Edge(inside[0],outside[0]),Edge(inside[0],outside[1]),Edge(inside[0],outside[2]));
                    else if(a==3)Emit(Edge(outside[0],inside[0]),Edge(outside[0],inside[2]),Edge(outside[0],inside[1]));
                    else
                    {
                        Vertex v0=Edge(inside[0],outside[0]),v1=Edge(inside[0],outside[1]),v2=Edge(inside[1],outside[0]),v3=Edge(inside[1],outside[1]);
                        Emit(v0,v1,v2);Emit(v1,v3,v2);
                    }
                    if(vertices.Count>maxVertices)return false;
                }
            }
            return true;
        }
        struct Vertex {public Vector3 p,n;public Color c;public Vector2 properties;}
        Vertex Edge(int a,int b)
        {
            Sample sa=samples[a],sb=samples[b];float t=Mathf.Clamp01((isoLevel-sa.density)/(sb.density-sa.density));
            Vector4 col=Vector4.Lerp(sa.color,sb.color,t)/Mathf.Max(.001f,Mathf.Lerp(sa.density,sb.density,t));
            Vector2 props=Vector2.Lerp(sa.properties,sb.properties,t)/Mathf.Max(.001f,Mathf.Lerp(sa.density,sb.density,t));
            return new Vertex{p=transform.InverseTransformPoint(Vector3.Lerp(points[a],points[b],t)),n=transform.InverseTransformDirection(-Vector3.Lerp(sa.gradient,sb.gradient,t)).normalized,c=new Color(col.x,col.y,col.z,1),properties=props};
        }
        void Emit(Vertex a,Vertex b,Vertex c)
        {
            Vector3 cross=Vector3.Cross(b.p-a.p,c.p-a.p);if(cross.sqrMagnitude<1e-15f)return;
            if(Vector3.Dot(cross,a.n+b.n+c.n)<0){Vertex swap=b;b=c;c=swap;}
            Add(a);Add(b);Add(c);
        }
        void Add(Vertex a){triangles.Add(vertices.Count);vertices.Add(a.p);normals.Add(a.n);colors.Add(a.c);properties.Add(a.properties);}
        public static float Hash(int i){uint n=(uint)i*747796405u+2891336453u;n=((n>>((int)(n>>28)+4))^n)*277803737u;return ((n>>22)^n)/(float)uint.MaxValue;}
        void DrawInclusions()
        {
            if(inclusionMesh==null||inclusionMaterial==null)return;
            for(int profile=0;profile<world.Profiles.Length;profile++)
            {
                var preset=world.Profiles[profile];int count=0;
                foreach(var p in world.Solver.particles)
                {
                    if(p.profile!=profile||(!surfaceBudgetExceeded&&Hash(p.id*7)>preset.inclusions))continue;
                    float size=world.particleSpacing*(surfaceBudgetExceeded?.85f:inclusionScale)*(.6f+Hash(p.id*13)*.6f);
                    Vector3 offset=surfaceBudgetExceeded?Vector3.zero:new Vector3(Hash(p.id*3)-.5f,.35f,Hash(p.id*5)-.5f)*world.particleSpacing;
                    matrices[count++]=Matrix4x4.TRS(p.display+offset,Quaternion.Euler(Hash(p.id)*360,Hash(p.id*2)*360,Hash(p.id*3)*360),Vector3.one*size);
                    if(count==1023){Draw(count,preset);count=0;}
                }
                if(count>0)Draw(count,preset);
            }
        }
        void Draw(int count,CCFoodProfile profile)
        {
            block.SetColor("_BaseColor",surfaceBudgetExceeded?profile.body:profile.fleck);
            Graphics.DrawMeshInstanced(inclusionMesh,0,inclusionMaterial,matrices,count,block,ShadowCastingMode.Off,true,gameObject.layer);
        }
    }
}
