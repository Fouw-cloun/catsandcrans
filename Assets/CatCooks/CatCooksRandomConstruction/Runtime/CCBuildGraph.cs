using System;
using System.Collections.Generic;
using UnityEngine;
namespace CatCooks.RandomConstruction
{
    public enum CCBuildKind { Column,Beam,Deck,Rebar,Pour,CureFloor,MarkWall,BrickLow,BrickMiddle,Lintel,BrickTop,PointJoints,CureWall,FacadePanel,WindowFrame,Glass,Seal,Door,Services,Brace,SteelCoat }
    public enum CCCargoKind { None,SteelColumn,SteelBeam,DeckPack,RebarPack,ConcreteBucket,BrickPallet,Mortar,FacadePanel,WindowFrame,Glass,Door,UtilityPack,Brace,Coating }
    public enum CCAccessNeed { Floor,Scaffold,InterfloorLift,NoConnectedFloor }
    [Serializable] public struct CCBuildSettings
    {
        [Range(4,16)] public int Width,Depth;
        [Min(4)] public float Bay,Height;
        [Range(0,3)] public int SafeCorner;
        [Tooltip("Width of the three continuous bridges, metres. Zero in an older scene uses 2.4.")]
        public float BridgeWidth;
        [Tooltip("Gap in the optional jump shortcut, metres. Zero in an older scene uses 1.2.")]
        public float JumpGap;
        public bool DisableJumpShortcut;
        public static CCBuildSettings Default=>new CCBuildSettings{Width=8,Depth=6,Bay=12,Height=12,SafeCorner=0,BridgeWidth=2.4f,JumpGap=1.2f};
        public CCBuildSettings Sanitized()
        {var s=this;s.Width=Mathf.Clamp(s.Width/2*2,4,16);s.Depth=Mathf.Clamp(s.Depth/2*2,4,16);s.Bay=Mathf.Clamp(s.Bay,4,20);s.Height=Mathf.Clamp(s.Height,4,20);s.SafeCorner=Mathf.Clamp(s.SafeCorner,0,3);s.BridgeWidth=Mathf.Clamp(s.BridgeWidth<=0?2.4f:s.BridgeWidth,1.2f,s.Bay*.6f);s.JumpGap=Mathf.Clamp(s.JumpGap<=0?1.2f:s.JumpGap,.4f,2.5f);return s;}
        public float Y(int level)=>(level-1)*Height;
        public bool Safe(int x,int z)
        {return x>=0&&z>=0&&x<Width&&z<Depth&&(SafeCorner%2==0?x<Width/2:x>=Width/2)&&(SafeCorner<2?z<Depth/2:z>=Depth/2);}
        public Vector3 SafeCentre=>new Vector3(Bay*Width*(SafeCorner%2==0?.25f:.75f),0,Bay*Depth*(SafeCorner<2?.25f:.75f));
    }
    public sealed class CCBuildTask
    {
        public int Id,Level,X,Z,Axis,Variant;
        public string Key;
        public CCBuildKind Kind;
        public CCCargoKind Cargo;
        public bool Crane,Protected;
        public int[] Requires;
        public Vector3 LocalPoint;
        public int MaterialUnits;
        public float WorkSeconds;
    }
    // Pure deterministic topology. IDs do not change with seed or current completion state.
    public sealed class CCBuildGraph
    {
        public readonly CCBuildSettings Settings;
        public readonly int Seed;
        public readonly CCZonePlan Plan;
        public readonly List<CCBuildTask> Tasks=new List<CCBuildTask>();
        readonly Dictionary<string,int> _ids=new Dictionary<string,int>();
        public CCBuildGraph(CCBuildSettings settings,int seed)
        {Settings=settings.Sanitized();Seed=seed;Plan=new CCZonePlan(Settings,seed);Build();}
        static string K(string kind,int l,int x,int z,int axis=0)=>kind+":"+l+":"+x+":"+z+":"+axis;
        public int Id(string kind,int l,int x,int z,int axis=0)=>_ids.TryGetValue(K(kind,l,x,z,axis),out int i)?i:-1;
        public static uint Hash(int seed,int l,int x,int z,int salt)
        {unchecked{uint h=(uint)seed^((uint)l*0x9e3779b9u)^((uint)x*0x85ebca6bu)^((uint)z*0xc2b2ae35u)^((uint)salt*0x27d4eb2du);h^=h>>16;h*=0x7feb352du;h^=h>>15;h*=0x846ca68bu;return h^(h>>16);}}
        int Add(string name,CCBuildKind kind,int l,int x,int z,int axis,int variant,Vector3 pos,CCCargoKind cargo,bool crane,params int[] dependencies)
        {
            var deps=new List<int>();foreach(int d in dependencies)if(d>=0&&!deps.Contains(d))deps.Add(d);
            var t=new CCBuildTask{Id=Tasks.Count,Key=K(name,l,x,z,axis),Kind=kind,Level=l,X=x,Z=z,Axis=axis,Variant=variant,LocalPoint=pos,Cargo=cargo,Crane=crane,Protected=!Plan.Allows(kind,l,x,z,axis),Requires=deps.ToArray(),MaterialUnits=cargo==CCCargoKind.None?0:1,WorkSeconds=kind==CCBuildKind.CureFloor?90:kind==CCBuildKind.CureWall?45:15};
            _ids.Add(t.Key,t.Id);Tasks.Add(t);return t.Id;
        }
        void Build()
        {
            var s=Settings;float b=s.Bay;
            // Level 0 rests on an abstract existing substructure; no fifth visible storey is added.
            for(int l=1;l<4;l++)
            {
                for(int z=0;z<=s.Depth;z++)for(int x=0;x<=s.Width;x++)
                {var p=new Vector3(x*b,s.Y(l),z*b);Add("C",CCBuildKind.Column,l,x,z,0,0,p,CCCargoKind.SteelColumn,true,Id("C",l-1,x,z));}
                for(int axis=0;axis<2;axis++)for(int z=0;z<(axis==0?s.Depth+1:s.Depth);z++)for(int x=0;x<(axis==0?s.Width:s.Width+1);x++)
                {
                    var p=new Vector3((x+(axis==0?.5f:0))*b,s.Y(l), (z+(axis==1?.5f:0))*b);
                    int beam=Add("B",CCBuildKind.Beam,l,x,z,axis,0,p,CCCargoKind.SteelBeam,true,Id("C",l,x,z),Id("C",l,x+(axis==0?1:0),z+(axis==1?1:0)));
                    bool boundary=axis==0?(z==0||z==s.Depth):(x==0||x==s.Width);
                    if(boundary&&Hash(0,l,x,z,axis+80)%4==0)Add("X",CCBuildKind.Brace,l,x,z,axis,0,p-Vector3.up*s.Height*.5f,CCCargoKind.Brace,true,beam);
                    if(Hash(0,l,x,z,axis+90)%5==0)Add("Paint",CCBuildKind.SteelCoat,l,x,z,axis,0,p,CCCargoKind.Coating,false,beam);
                }
            }
            for(int l=0;l<4;l++)for(int z=0;z<s.Depth;z++)for(int x=0;x<s.Width;x++)
            {
                var p=new Vector3((x+.5f)*b,s.Y(l),(z+.5f)*b);
                int d=Add("D",CCBuildKind.Deck,l,x,z,0,0,p,CCCargoKind.DeckPack,true,Id("B",l,x,z,0),Id("B",l,x,z+1,0),Id("B",l,x,z,1),Id("B",l,x+1,z,1));
                int r=Add("R",CCBuildKind.Rebar,l,x,z,0,0,p,CCCargoKind.RebarPack,true,d);
                int pour=Add("P",CCBuildKind.Pour,l,x,z,0,0,p,CCCargoKind.ConcreteBucket,true,r);
                Add("F",CCBuildKind.CureFloor,l,x,z,0,0,p,CCCargoKind.None,false,pour);
            }
            for(int l=0;l<3;l++)for(int axis=0;axis<2;axis++)for(int z=0;z<(axis==0?s.Depth+1:s.Depth);z++)for(int x=0;x<(axis==0?s.Width:s.Width+1);x++)
            {
                bool boundary=axis==0?(z==0||z==s.Depth):(x==0||x==s.Width);
                // All seeds share the same task plan. Seed chooses progress, not missing definitions.
                uint plan=Hash(0,l,x,z,axis+10);if(!boundary&&plan%5!=0)continue;
                int cx=Mathf.Clamp(x,0,s.Width-1),cz=Mathf.Clamp(z,0,s.Depth-1);
                var p=new Vector3((x+(axis==0?.5f:0))*b,s.Y(l),(z+(axis==1?.5f:0))*b);
                int support=Id("F",l,cx,cz),beam=Id("B",l+1,x,z,axis);
                bool panel=boundary&&plan%3==0;int variant=boundary?1:2;int finish;
                if(panel)finish=Add("Panel",CCBuildKind.FacadePanel,l,x,z,axis,1,p,CCCargoKind.FacadePanel,true,support,beam);
                else
                {
                    int m=Add("Mark",CCBuildKind.MarkWall,l,x,z,axis,variant,p,CCCargoKind.None,false,support,beam);
                    int low=Add("W1",CCBuildKind.BrickLow,l,x,z,axis,variant,p,CCCargoKind.BrickPallet,false,m);
                    int mid=Add("W2",CCBuildKind.BrickMiddle,l,x,z,axis,variant,p,CCCargoKind.BrickPallet,false,low);
                    int lintel=Add("Lintel",CCBuildKind.Lintel,l,x,z,axis,variant,p,CCCargoKind.SteelBeam,true,mid);
                    int top=Add("W3",CCBuildKind.BrickTop,l,x,z,axis,variant,p,CCCargoKind.BrickPallet,false,lintel);
                    int joints=Add("Joints",CCBuildKind.PointJoints,l,x,z,axis,variant,p,CCCargoKind.Mortar,false,top);
                    finish=Add("WC",CCBuildKind.CureWall,l,x,z,axis,variant,p,CCCargoKind.None,false,joints);
                }
                if(boundary)
                {
                    int frame=Add("Window",CCBuildKind.WindowFrame,l,x,z,axis,1,p,CCCargoKind.WindowFrame,true,finish);
                    int glass=Add("Glass",CCBuildKind.Glass,l,x,z,axis,1,p,CCCargoKind.Glass,true,frame);
                    Add("Seal",CCBuildKind.Seal,l,x,z,axis,1,p,CCCargoKind.None,false,glass);
                }
                else Add("Door",CCBuildKind.Door,l,x,z,axis,2,p,CCCargoKind.Door,false,finish);
            }
            for(int l=0;l<3;l++)for(int z=0;z<s.Depth;z++)for(int x=0;x<s.Width;x++)
                if(Hash(0,l,x,z,66)%6==0)Add("Services",CCBuildKind.Services,l,x,z,0,0,new Vector3((x+.5f)*b,s.Y(l)+s.Height-.8f,(z+.5f)*b),CCCargoKind.UtilityPack,false,Id("F",l,x,z),Id("B",l+1,x,z,0));
        }
        public bool DependenciesDone(int id,byte[] states)
        {if(id<0||id>=Tasks.Count||states==null||states.Length!=Tasks.Count)return false;foreach(int d in Tasks[id].Requires)if(states[d]==0)return false;return true;}
        bool SetWithSupports(int id,byte[] state)
        {
            if(id<0)return true;if(state[id]!=0)return true;
            var t=Tasks[id];if(t.Protected)return false;
            foreach(int d in t.Requires)if(!SetWithSupports(d,state))return false;
            state[id]=1;return true;
        }
        public byte[] InitialState()
        {
            var state=new byte[Tasks.Count];var s=Settings;
            for(int l=0;l<4;l++)for(int z=0;z<s.Depth;z++)for(int x=0;x<s.Width;x++)
            {
                int stage=Plan.Stage(l,x,z);
                if(stage>0)SetWithSupports(Id(stage==1?"D":stage==2?"R":stage==3?"P":"F",l,x,z),state);
            }
            // One progress band for a whole work island. No independent column/window coin flips.
            foreach(var t in Tasks)
            {
                if(t.Protected||!DependenciesDone(t.Id,state))continue;
                int zone=Plan.Zone(Mathf.Clamp(t.X,0,s.Width-1),Mathf.Clamp(t.Z,0,s.Depth-1));
                int finish=7+(int)(Hash(Seed,t.Level,zone,0,830)%10);
                int k=(int)t.Kind;
                if(k>=(int)CCBuildKind.MarkWall&&k<=(int)CCBuildKind.Seal&&k<=finish)state[t.Id]=1;
                if(t.Kind==CCBuildKind.Brace||t.Kind==CCBuildKind.SteelCoat)state[t.Id]=1;
            }
            return state;
        }
        public bool Done(byte[] state,string kind,int l,int x,int z,int axis=0){int id=Id(kind,l,x,z,axis);return id>=0&&state!=null&&id<state.Length&&state[id]!=0;}
        public bool[] ConnectedPlayerFloor(byte[] states)
        {
            var s=Settings;var visited=new bool[s.Width*s.Depth];var q=new Queue<int>();
            int sx=s.SafeCorner%2==0?0:s.Width-1,sz=s.SafeCorner<2?0:s.Depth-1;
            int start=sz*s.Width+sx;if(!Done(states,"F",1,sx,sz))return visited;visited[start]=true;q.Enqueue(start);
            int[] dx={-1,1,0,0},dz={0,0,-1,1};
            while(q.Count>0){int k=q.Dequeue(),x=k%s.Width,z=k/s.Width;
                foreach(var bridge in Plan.Bridges)
                {
                    if(bridge.Gap>0)continue;int a=bridge.Start.y*s.Width+bridge.Start.x,b=bridge.End.y*s.Width+bridge.End.x;
                    int next=k==a?b:k==b?a:-1;
                    if(next>=0&&!visited[next]&&Done(states,"F",1,next%s.Width,next/s.Width)){visited[next]=true;q.Enqueue(next);}
                }
                for(int d=0;d<4;d++){int nx=x+dx[d],nz=z+dz[d];if(nx<0||nz<0||nx>=s.Width||nz>=s.Depth)continue;int next=nz*s.Width+nx;if(!visited[next]&&Done(states,"F",1,nx,nz)){visited[next]=true;q.Enqueue(next);}}}
            return visited;
        }
        public CCAccessNeed Access(CCBuildTask task,byte[] states,bool[] connected)
        {
            int floor=task.Kind==CCBuildKind.Column||task.Kind==CCBuildKind.Beam||task.Kind==CCBuildKind.Brace||task.Kind==CCBuildKind.SteelCoat?task.Level-1:task.Level;
            if(floor!=1)return CCAccessNeed.InterfloorLift;
            int x=Mathf.Clamp(task.X,0,Settings.Width-1),z=Mathf.Clamp(task.Z,0,Settings.Depth-1);
            bool reachable=connected!=null&&connected[z*Settings.Width+x];
            if(!reachable)return CCAccessNeed.NoConnectedFloor;
            return task.Kind==CCBuildKind.Beam||task.Kind==CCBuildKind.BrickMiddle||task.Kind==CCBuildKind.Brace||task.Kind==CCBuildKind.SteelCoat||task.Kind==CCBuildKind.Lintel||task.Kind==CCBuildKind.Services||task.Kind==CCBuildKind.BrickTop||task.Kind==CCBuildKind.FacadePanel||task.Kind==CCBuildKind.Glass||task.Kind==CCBuildKind.WindowFrame?CCAccessNeed.Scaffold:CCAccessNeed.Floor;
        }
        public string Validate(byte[] state)
        {
            if(state==null||state.Length!=Tasks.Count)return "State length mismatch";
            foreach(var t in Tasks)if(state[t.Id]!=0&&(t.Protected||!DependenciesDone(t.Id,state)))return "Invalid dependency/protected task: "+t.Key;
            var s=Settings;for(int l=0;l<=1;l++)for(int z=0;z<s.Depth;z++)for(int x=0;x<s.Width;x++)if(s.Safe(x,z)&&!Done(state,"F",l,x,z))return "Safe floor incomplete";
            var connected=ConnectedPlayerFloor(state);
            foreach(var bridge in Plan.Bridges)if(bridge.Gap==0)
                if(!connected[bridge.Start.y*s.Width+bridge.Start.x]||!connected[bridge.End.y*s.Width+bridge.End.x])return "Disconnected island / bridge landing";
            return "";
        }
    }
}
