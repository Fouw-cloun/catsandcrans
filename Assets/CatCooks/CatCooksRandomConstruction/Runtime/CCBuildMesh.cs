using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace CatCooks.RandomConstruction
{
    public sealed class CCBuildMesh
    {
        readonly List<Vector3> _v=new List<Vector3>(),_n=new List<Vector3>();
        readonly List<Vector2> _uv=new List<Vector2>();
        readonly List<int> _t=new List<int>();
        public int VertexCount=>_v.Count;
        public void Box(Vector3 centre,Vector3 size,int tile,Quaternion rotation)
        {
            if(size.x<=0||size.y<=0||size.z<=0)return;
            Vector3 a=-size*.5f,b=size*.5f;
            Face(new Vector3(b.x,a.y,a.z),new Vector3(a.x,a.y,a.z),new Vector3(a.x,b.y,a.z),new Vector3(b.x,b.y,a.z),centre,rotation,tile);
            Face(new Vector3(a.x,a.y,b.z),new Vector3(b.x,a.y,b.z),new Vector3(b.x,b.y,b.z),new Vector3(a.x,b.y,b.z),centre,rotation,tile);
            Face(new Vector3(a.x,a.y,a.z),new Vector3(a.x,a.y,b.z),new Vector3(a.x,b.y,b.z),new Vector3(a.x,b.y,a.z),centre,rotation,tile);
            Face(new Vector3(b.x,a.y,b.z),new Vector3(b.x,a.y,a.z),new Vector3(b.x,b.y,a.z),new Vector3(b.x,b.y,b.z),centre,rotation,tile);
            Face(new Vector3(a.x,a.y,a.z),new Vector3(b.x,a.y,a.z),new Vector3(b.x,a.y,b.z),new Vector3(a.x,a.y,b.z),centre,rotation,tile);
            Face(new Vector3(a.x,b.y,b.z),new Vector3(b.x,b.y,b.z),new Vector3(b.x,b.y,a.z),new Vector3(a.x,b.y,a.z),centre,rotation,tile);
        }
        void Face(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 centre,Quaternion q,int tile)
        {
            int k=_v.Count;Vector3 n=q*Vector3.Cross(b-a,c-a).normalized;
            _v.Add(centre+q*a);_v.Add(centre+q*b);_v.Add(centre+q*c);_v.Add(centre+q*d);
            for(int i=0;i<4;i++)_n.Add(n);
            float x=(tile%4)*.25f+.006f,y=(tile/4)*.25f+.006f,w=.238f;
            _uv.Add(new Vector2(x,y));_uv.Add(new Vector2(x+w,y));_uv.Add(new Vector2(x+w,y+w));_uv.Add(new Vector2(x,y+w));
            _t.Add(k);_t.Add(k+1);_t.Add(k+2);_t.Add(k);_t.Add(k+2);_t.Add(k+3);
        }
        public void Rod(Vector3 a,Vector3 b,float width,int tile)
        {var d=b-a;if(d.sqrMagnitude<.00001f)return;Box((a+b)*.5f,new Vector3(width,d.magnitude,width),tile,Quaternion.FromToRotation(Vector3.up,d.normalized));}
        public Mesh ToMesh(string name)
        {
            var m=new Mesh{name=name,indexFormat=_v.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            m.SetVertices(_v);m.SetNormals(_n);m.SetUVs(0,_uv);m.SetTriangles(_t,0);m.RecalculateBounds();return m;
        }
    }
    public sealed class CCBuildGeometry
    {
        public readonly CCBuildMesh Visual=new CCBuildMesh(),Collision=new CCBuildMesh();
        public void Box(Vector3 p,Vector3 size,int tile,bool solid=false,Quaternion? rotation=null)
        {Quaternion q=rotation??Quaternion.identity;Visual.Box(p,size,tile,q);if(solid)Collision.Box(p,size,0,q);}
        public void Column(Vector3 top,float h,int tile=1)
        {
            var c=top-Vector3.up*h*.5f;
            Box(c,new Vector3(.16f,h,.42f),tile);Box(c+Vector3.right*.23f,new Vector3(.12f,h,.65f),tile);Box(c-Vector3.right*.23f,new Vector3(.12f,h,.65f),tile);
            Box(top-Vector3.up*(h-.06f),new Vector3(.8f,.12f,.8f),tile);Box(top-Vector3.up*.06f,new Vector3(.8f,.12f,.8f),tile);
            Collision.Box(c,new Vector3(.65f,h,.65f),0,Quaternion.identity);
        }
        public void Beam(Vector3 p,float length,Quaternion q,int tile=1)
        {
            Box(p-Vector3.up*.23f,new Vector3(length,.46f,.1f),tile,false,q);
            Box(p-Vector3.up*.04f,new Vector3(length,.08f,.45f),tile,false,q);
            Box(p-Vector3.up*.44f,new Vector3(length,.08f,.45f),tile,false,q);
            Collision.Box(p-Vector3.up*.24f,new Vector3(length,.48f,.45f),0,q);
        }
        public void Deck(Vector3 p,float b)
        {
            Box(p-Vector3.up*.05f,new Vector3(b-.04f,.08f,b-.04f),2);
            for(int i=1;i<10;i++)Box(p+Vector3.right*((i/10f-.5f)*b)-Vector3.up*.005f,new Vector3(.08f,.025f,b-.05f),8);
            Collision.Box(p-Vector3.up*.055f,new Vector3(b,.11f,b),0,Quaternion.identity);
        }
        public void Rebar(Vector3 p,float b)
        {for(int i=1;i<9;i++){float d=(i/9f-.5f)*b;Box(p+new Vector3(d,.06f,0),new Vector3(.035f,.035f,b-.3f),3);Box(p+new Vector3(0,.1f,d),new Vector3(b-.3f,.035f,.035f),3);}}
        public void Slab(Vector3 p,float b,bool cured)
        {Box(p-Vector3.up*.15f,new Vector3(b-.045f,.3f,b-.045f),cured?0:4);Collision.Box(p-Vector3.up*.15f,new Vector3(b,.3f,b),0,Quaternion.identity);}
        public void WallBand(Vector3 p,Quaternion q,float b,float h,float low,float high,int tile,bool door)
        {
            float opening=door?Mathf.Min(2.4f,b*.3f):b*.55f;
            float bottom=door?0:h*.22f,top=door?Mathf.Min(3.2f,h*.75f):h*.8f;
            float side=(b-opening)*.5f;
            LocalBox(p,q,new Vector3(-(opening+side)*.5f,(low+high)*.5f,0),new Vector3(side,high-low,.22f),tile,true);
            LocalBox(p,q,new Vector3((opening+side)*.5f,(low+high)*.5f,0),new Vector3(side,high-low,.22f),tile,true);
            float a=low,c=Mathf.Min(high,bottom);if(c>a)LocalBox(p,q,new Vector3(0,(a+c)*.5f,0),new Vector3(opening,c-a,.22f),tile,true);
            a=Mathf.Max(low,top);c=high;if(c>a)LocalBox(p,q,new Vector3(0,(a+c)*.5f,0),new Vector3(opening,c-a,.22f),tile,true);
        }
        public void Window(Vector3 p,Quaternion q,float b,float h,bool glass)
        {
            float width=b*.55f,bottom=h*.22f,top=h*.8f,height=top-bottom;
            if(glass){LocalBox(p,q,new Vector3(0,(bottom+top)*.5f,0),new Vector3(width-.14f,height-.14f,.07f),7,true);return;}
            foreach(float x in new[]{-width*.5f,width*.5f,0})LocalBox(p,q,new Vector3(x,(bottom+top)*.5f,0),new Vector3(.12f,height,.18f),6,true);
            foreach(float y in new[]{bottom,top})LocalBox(p,q,new Vector3(0,y,0),new Vector3(width,.12f,.18f),6,true);
        }
        public void LocalBox(Vector3 p,Quaternion q,Vector3 local,Vector3 size,int tile,bool solid=false)=>Box(p+q*local,size,tile,solid,q);
        public void Rail(Vector3 a,Vector3 b)
        {
            for(int i=0;i<=4;i++){Vector3 p=Vector3.Lerp(a,b,i/4f);Box(p+Vector3.up*.58f,new Vector3(.07f,1.16f,.07f),9);}
            Visual.Rod(a+Vector3.up*1.12f,b+Vector3.up*1.12f,.07f,9);Visual.Rod(a+Vector3.up*.55f,b+Vector3.up*.55f,.06f,9);
            var d=b-a;Collision.Box((a+b)*.5f+Vector3.up*.6f,new Vector3(.1f,1.2f,d.magnitude),0,Quaternion.LookRotation(d));
        }
        void EdgeRail(CCZonePlan plan,int level,Vector3 a,Vector3 b)
        {
            if(level!=1||!plan.LandingOnEdge(a,b,out Vector3 centre,out float width)){Rail(a,b);return;}
            Vector3 d=(b-a).normalized;Vector3 left=centre-d*(width*.5f+.08f),right=centre+d*(width*.5f+.08f);
            if(Vector3.Dot(left-a,d)>.15f)Rail(a,left);
            if(Vector3.Dot(b-right,d)>.15f)Rail(right,b);
        }
        void BridgePiece(Vector3 a,Vector3 b,float width)
        {
            var dir=(b-a).normalized;var right=Vector3.Cross(Vector3.up,dir);var q=Quaternion.LookRotation(dir);
            float length=(b-a).magnitude;if(length<.01f)return;var centre=(a+b)*.5f;
            Box(centre-Vector3.up*.10f,new Vector3(width,.20f,length),2,true,q);
            for(int sign=-1;sign<=1;sign+=2)
            {
                Vector3 side=right*(width*.5f)*sign;
                Box(centre+side-Vector3.up*.28f,new Vector3(.16f,.35f,length),1,false,q);
                Rail(a+side,b+side);
            }
            // Broad stripes mark the walkable deck, no particles or additional renderers.
            Box(a+dir*.15f+Vector3.up*.005f,new Vector3(width,.01f,.22f),9,false,q);
            Box(b-dir*.15f+Vector3.up*.005f,new Vector3(width,.01f,.22f),9,false,q);
        }
        void Bridge(CCZoneBridge b)
        {
            if(b.Gap<=0){BridgePiece(b.A,b.B,b.Width);return;}
            Vector3 dir=(b.B-b.A).normalized,mid=(b.A+b.B)*.5f;
            BridgePiece(b.A,mid-dir*b.Gap*.5f,b.Width);
            BridgePiece(mid+dir*b.Gap*.5f,b.B,b.Width);
        }
        public void Task(CCBuildGraph g,CCBuildTask t,byte[] states)
        {
            float b=g.Settings.Bay,h=g.Settings.Height;Vector3 p=t.LocalPoint;Quaternion q=Quaternion.Euler(0,t.Axis==0?0:90,0);
            bool door=t.Variant==2;float lowTop=door?Mathf.Min(1.4f,h*.25f):h*.22f;float openingTop=door?Mathf.Min(3.2f,h*.75f):h*.8f;
            switch(t.Kind)
            {
                case CCBuildKind.Column:Column(p,h);break;
                case CCBuildKind.Beam:Beam(p,b,q,g.Done(states,"Paint",t.Level,t.X,t.Z,t.Axis)?12:1);break;
                case CCBuildKind.Deck:if(!g.Done(states,"P",t.Level,t.X,t.Z))Deck(p,b);break;
                case CCBuildKind.Rebar:if(!g.Done(states,"P",t.Level,t.X,t.Z))Rebar(p,b);break;
                case CCBuildKind.Pour:if(!g.Done(states,"F",t.Level,t.X,t.Z))Slab(p,b,false);break;
                case CCBuildKind.CureFloor:Slab(p,b,true);break;
                case CCBuildKind.MarkWall:if(!g.Done(states,"W1",t.Level,t.X,t.Z,t.Axis))LocalBox(p,q,new Vector3(0,.012f,0),new Vector3(b,.015f,.06f),10);break;
                case CCBuildKind.BrickLow:WallBand(p,q,b,h,0,lowTop,5,door);break;
                case CCBuildKind.BrickMiddle:WallBand(p,q,b,h,lowTop,openingTop,5,door);break;
                case CCBuildKind.BrickTop:WallBand(p,q,b,h,openingTop,h-.5f,5,door);break;
                case CCBuildKind.Lintel:LocalBox(p,q,new Vector3(0,openingTop+.09f,0),new Vector3(door?Mathf.Min(2.4f,b*.3f)+.4f:b*.55f+.4f,.18f,.3f),1,true);break;
                case CCBuildKind.FacadePanel:WallBand(p,q,b,h,0,h-.5f,6,false);break;
                case CCBuildKind.WindowFrame:Window(p,q,b,h,false);break;
                case CCBuildKind.Glass:Window(p,q,b,h,true);break;
                case CCBuildKind.Door:LocalBox(p,q,new Vector3(0,1.4f,.05f),new Vector3(Mathf.Min(2.2f,b*.28f),2.8f,.12f),11,true);break;
                case CCBuildKind.Services:Box(p,new Vector3(b*.8f,.3f,.4f),8);Box(p+new Vector3(0,-.4f,.5f),new Vector3(b*.8f,.08f,.08f),14);Box(p+new Vector3(0,-.6f,.7f),new Vector3(b*.8f,.07f,.13f),3);break;
                case CCBuildKind.Brace:Visual.Rod(p+q*new Vector3(-b*.5f,-h*.5f,0),p+q*new Vector3(b*.5f,h*.5f,0),.14f,1);break;
                // Curing, pointing, sealing and fireproofing have logical states; no extra overlapping renderer.
            }
        }
        public static CCBuildGeometry Chunk(CCBuildGraph g,byte[] state,int level,int chunkX,int chunkZ,int chunkSize)
        {
            var r=new CCBuildGeometry();var s=g.Settings;
            foreach(var t in g.Tasks)if(state[t.Id]!=0&&t.Level==level&&Mathf.Min(t.X,s.Width-1)/chunkSize==chunkX&&Mathf.Min(t.Z,s.Depth-1)/chunkSize==chunkZ)r.Task(g,t,state);
            int x0=chunkX*chunkSize,z0=chunkZ*chunkSize;
            for(int z=z0;z<Mathf.Min(z0+chunkSize,s.Depth);z++)for(int x=x0;x<Mathf.Min(x0+chunkSize,s.Width);x++)
            {
                if(!g.Done(state,"D",level,x,z))continue;
                float y=s.Y(level),b=s.Bay;
                if(x==0||!g.Done(state,"D",level,x-1,z))r.EdgeRail(g.Plan,level,new Vector3(x*b,y,z*b),new Vector3(x*b,y,(z+1)*b));
                if(x==s.Width-1||!g.Done(state,"D",level,x+1,z))r.EdgeRail(g.Plan,level,new Vector3((x+1)*b,y,z*b),new Vector3((x+1)*b,y,(z+1)*b));
                if(z==0||!g.Done(state,"D",level,x,z-1))r.EdgeRail(g.Plan,level,new Vector3(x*b,y,z*b),new Vector3((x+1)*b,y,z*b));
                if(z==s.Depth-1||!g.Done(state,"D",level,x,z+1))r.EdgeRail(g.Plan,level,new Vector3(x*b,y,(z+1)*b),new Vector3((x+1)*b,y,(z+1)*b));
                if(level==1&&s.Safe(x,z))
                {var p=new Vector3((x+.5f)*b,.006f,(z+.5f)*b);r.Box(p+Vector3.forward*(b*.5f-.12f),new Vector3(b-.25f,.012f,.08f),9);r.Box(p+Vector3.right*(b*.5f-.12f),new Vector3(.08f,.012f,b-.25f),9);}
            }
            if(level==1)foreach(var bridge in g.Plan.Bridges)
                if(bridge.Start.x/chunkSize==chunkX&&bridge.Start.y/chunkSize==chunkZ)r.Bridge(bridge);
            return r;
        }
        public static CCBuildGeometry Sample(string name)
        {
            var r=new CCBuildGeometry();var p=Vector3.zero;var q=Quaternion.identity;
            switch(name)
            {
                case "Column":r.Column(Vector3.up*6,6);break;
                case "Beam":r.Beam(Vector3.up*.48f,6,q);break;
                case "ProfileDeck":r.Deck(p,6);break;
                case "RebarGrid":r.Rebar(p,6);break;
                case "ConcreteSlab":r.Slab(p,6,true);break;
                case "BrickHalf":r.WallBand(p,q,6,4,0,1.8f,5,false);break;
                case "BrickWindow":r.WallBand(p,q,6,4,0,4,5,false);break;
                case "BrickDoor":r.WallBand(p,q,6,4,0,4,5,true);break;
                case "FacadePanel":r.WallBand(p,q,6,4,0,4,6,false);break;
                case "WindowFrame":r.Window(p,q,6,4,false);break;
                case "Glass":r.Window(p,q,6,4,true);break;
                case "Door":r.Box(Vector3.up*1.4f,new Vector3(2,2.8f,.12f),11,true);break;
                case "Lintel":r.Beam(Vector3.up*.48f,3,q);break;
                case "Brace":r.Visual.Rod(p,p+new Vector3(4,4,0),.14f,1);break;
                case "ConcreteBucket":r.Box(Vector3.up*.5f,new Vector3(1.3f,1,1.3f),8);r.Box(Vector3.up*1.02f,new Vector3(1.18f,.05f,1.18f),4);for(int i=-1;i<=1;i+=2)r.Visual.Rod(new Vector3(i*.7f,.8f,0),new Vector3(i*.7f,1.8f,0),.06f,1);r.Visual.Rod(new Vector3(-.7f,1.8f,0),new Vector3(.7f,1.8f,0),.06f,1);break;
                case "CementBags":for(int i=0;i<6;i++)r.Box(new Vector3(i%2*.65f,.15f+i/4*.3f,(i/2%2)*.5f),new Vector3(.6f,.28f,.45f),13);break;
                case "PanelPack":for(int i=0;i<4;i++)r.Box(Vector3.up*(.1f+i*.14f),new Vector3(3,.12f,1.5f),6);break;
                case "SheetPack":for(int i=0;i<4;i++)r.Box(Vector3.up*(.08f+i*.08f),new Vector3(3,.06f,1.5f),2);break;
                case "RebarBundle":for(int i=0;i<8;i++)r.Box(new Vector3(0,.06f+(i/4)*.08f,(i%4)*.1f),new Vector3(3,.05f,.05f),3);break;
                case "Scaffold":for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)r.Box(new Vector3(x*1.2f,1.5f,z*.6f),new Vector3(.08f,3,.08f),9);r.Box(Vector3.up*2.8f,new Vector3(2.6f,.12f,1.4f),11,true);r.Rail(new Vector3(-1.2f,2.8f,-.6f),new Vector3(1.2f,2.8f,-.6f));break;
            }
            return r;
        }
        public static readonly string[] ModelNames={"Column","Beam","ProfileDeck","RebarGrid","ConcreteSlab","BrickHalf","BrickWindow","BrickDoor","FacadePanel","WindowFrame","Glass","Door","Lintel","Brace","ConcreteBucket","CementBags","PanelPack","SheetPack","RebarBundle","Scaffold"};
    }
}
