using System.Collections.Generic;
using UnityEngine;
namespace CatCooks.RandomConstruction
{
    public sealed class CCZoneBridge
    {
        public Vector2Int Start,End;
        public Vector3 A,B;
        public float Width,Gap;
    }
    // Coordinates are mirrored with SafeCorner, so equipment in the safe quarter never moves with seed.
    // Planned voids and skylights are permanent. Unfinished cells INSIDE islands remain work tasks.
    public sealed class CCZonePlan
    {
        readonly CCBuildSettings s;
        readonly int seed;
        readonly RectInt[] islands=new RectInt[4];
        readonly int firstTower,secondTower;
        public readonly List<CCZoneBridge> Bridges=new List<CCZoneBridge>();
        public CCZonePlan(CCBuildSettings settings,int mapSeed)
        {
            s=settings;seed=mapSeed;
            int hx=s.Width/2,hz=s.Depth/2;
            int rw=Mathf.Max(1,hx*2/3),rd=Mathf.Max(1,hz*2/3);
            // Larger maps vary whole island boundaries, never isolated cells.
            if(s.Width>=8)rw+= (int)(CCBuildGraph.Hash(seed,0,0,0,801)%2);
            if(s.Depth>=8)rd+= (int)(CCBuildGraph.Hash(seed,0,0,0,802)%2);
            rw=Mathf.Min(rw,hx-1);rd=Mathf.Min(rd,hz-1);
            islands[0]=new RectInt(0,0,hx,hz);
            int eastDepth=rw==hx-1?Mathf.Max(1,hz-1):hz;
            islands[1]=new RectInt(s.Width-rw,0,rw,eastDepth);
            islands[2]=new RectInt(0,s.Depth-rd,rw,rd);
            islands[3]=new RectInt(s.Width-rw,s.Depth-rd,rw,rd);
            firstTower=1+(int)(CCBuildGraph.Hash(seed,0,0,0,803)%3);
            secondTower=1+firstTower%3;
            int eastZ=(eastDepth-1)/2,westX=(rw-1)/2,northZ=s.Depth-rd+(rd-1)/2,eastX=s.Width-rw+(rw-1)/2;
            AddBridge(new Vector2Int(hx-1,eastZ),new Vector2Int(s.Width-rw,eastZ),0);
            AddBridge(new Vector2Int(westX,hz-1),new Vector2Int(westX,s.Depth-rd),0);
            AddBridge(new Vector2Int(eastX,eastDepth-1),new Vector2Int(eastX,s.Depth-rd),0);
            if(!s.DisableJumpShortcut)AddBridge(new Vector2Int(rw-1,northZ),new Vector2Int(s.Width-rw,northZ),s.JumpGap);
        }
        Vector2Int WorldCell(Vector2Int p)=>new Vector2Int(s.SafeCorner%2==0?p.x:s.Width-1-p.x,s.SafeCorner<2?p.y:s.Depth-1-p.y);
        Vector2Int Canonical(int x,int z)=>WorldCell(new Vector2Int(x,z));
        public bool InBounds(int x,int z)=>x>=0&&z>=0&&x<s.Width&&z<s.Depth;
        public int Zone(int x,int z)
        {
            if(!InBounds(x,z))return -1;var p=Canonical(x,z);
            // Connected 2x2 plaza at the inner corner of the safe quarter; sky remains clear above it.
            if(p.x>=s.Width/2-1&&p.x<=s.Width/2&&p.y>=s.Depth/2-1&&p.y<=s.Depth/2)return 0;
            for(int i=0;i<4;i++)if(islands[i].Contains(p))return i;
            return -1;
        }
        public bool FloorAllowed(int level,int x,int z)
        {
            int zone=Zone(x,z);if(zone<0)return false;
            if(level<2)return true;
            return zone==firstTower||zone==secondTower;
        }
        bool Node(int l,int x,int z)=>FloorAllowed(l,x-1,z-1)||FloorAllowed(l,x,z-1)||FloorAllowed(l,x-1,z)||FloorAllowed(l,x,z);
        bool Edge(int l,int x,int z,int axis)=>FloorAllowed(l,x,z)||(axis==0?FloorAllowed(l,x,z-1):FloorAllowed(l,x-1,z));
        bool OuterEdge(int x,int z,int axis)=>axis==0?(z==0||z==s.Depth):(x==0||x==s.Width);
        public bool Allows(CCBuildKind kind,int l,int x,int z,int axis)
        {
            switch(kind)
            {
                case CCBuildKind.Column:return Node(l,x,z);
                case CCBuildKind.Beam:case CCBuildKind.Brace:case CCBuildKind.SteelCoat:return Edge(l,x,z,axis);
                case CCBuildKind.Deck:case CCBuildKind.Rebar:case CCBuildKind.Pour:case CCBuildKind.CureFloor:return FloorAllowed(l,x,z);
                case CCBuildKind.Services:return false; // Reserved for a future interior zone, not across the open plaza.
                default:
                    // Only outward-facing backs of the two tall clusters. No perimeter wall enclosing the map.
                    int cx=Mathf.Clamp(x,0,s.Width-1),cz=Mathf.Clamp(z,0,s.Depth-1);
                    return OuterEdge(x,z,axis)&&FloorAllowed(l,cx,cz)&&FloorAllowed(l+1,cx,cz)&&Zone(cx,cz)!=0;
            }
        }
        public int Stage(int l,int x,int z)
        {
            if(!FloorAllowed(l,x,z))return 0;
            int zone=Zone(x,z);if(zone==0)return 4;
            var p=Canonical(x,z);var r=islands[zone];
            int cx=r.xMin+(r.width-1)/2,cz=r.yMin+(r.height-1)/2;
            if(l==1&&(p.x==cx||(zone!=1&&p.y==cz)))return 4; // Continuous landing/circulation cross.
            int band=p.x-r.xMin;
            int progress=1+(int)(CCBuildGraph.Hash(seed,l,zone,0,810)%3);
            if(l<=1)return band<Mathf.Max(1,r.width/2)?4:progress;
            if(l==2)return band<Mathf.Max(1,r.width/2)?4:progress;
            // One coherent unfinished edge on the highest deck, never isolated random boxes.
            return band<Mathf.Max(1,r.width/2)?progress:0;
        }
        void AddBridge(Vector2Int start,Vector2Int end,float gap)
        {
            start=WorldCell(start);end=WorldCell(end);
            Vector3 a=new Vector3((start.x+.5f)*s.Bay,0,(start.y+.5f)*s.Bay);
            Vector3 b=new Vector3((end.x+.5f)*s.Bay,0,(end.y+.5f)*s.Bay),dir=(b-a).normalized;
            a+=dir*s.Bay*.5f;b-=dir*s.Bay*.5f;
            if((b-a).magnitude<.01f)return;
            Bridges.Add(new CCZoneBridge{Start=start,End=end,A=a,B=b,Width=s.BridgeWidth,Gap=Mathf.Min(gap,(b-a).magnitude*.4f)});
        }
        public bool LandingOnEdge(Vector3 a,Vector3 b,out Vector3 centre,out float width)
        {
            foreach(var bridge in Bridges)foreach(var p in new[]{bridge.A,bridge.B})
            {
                Vector3 d=b-a;float t=Vector3.Dot(p-a,d)/d.sqrMagnitude;
                if(t>=0&&t<=1&&(a+t*d-p).sqrMagnitude<.001f){centre=p;width=bridge.Width;return true;}
            }
            centre=Vector3.zero;width=0;return false;
        }
        public string Describe()=>"Zonal islands: 4 | tall clusters: "+firstTower+", "+secondTower+" | bridges: "+Bridges.Count+" | central plaza: open sky";
    }
}
