using CatCooks.Level;
using FishNet.Object;
using UnityEngine;
using static CatCooks.SiteTools.CCUtilityArt;
using Kind=CatCooks.Level.CityModuleHouse.CellKind;

namespace CatCooks.SiteTools
{
    public static class CCGridUtilityBuilder
    {
        const string Beam="545F66",Deck="777E7C",Stripe="E0A842";
        static readonly Vector2Int[] Directions={Vector2Int.right,Vector2Int.left,Vector2Int.up,Vector2Int.down};
        public static void Build(CityModuleHouse h,CityModuleHouse.Cell cell,Transform root)
        {
            if(cell.Kind==Kind.Lift) Lift(root,h.Pitch,h.Height);
            if(cell.HasPipes) Pipes(h,cell,root);
        }
        static void Pipes(CityModuleHouse h,CityModuleHouse.Cell c,Transform parent)
        {
            var root=Node(parent,"Трубная сеть",Vector3.zero);
            float half=h.Pitch*.5f;
            float spacing=Mathf.Clamp(half-.80f,1.37f,1.52f);
            if(c.Kind==Kind.Riser || (c.PipeVerticalOverlay && CityModuleHouse.HasSlab(c.Kind))){
                ServiceDeck(root,c.Kind==Kind.Riser?0:h.SlabTop,half,spacing);
                if(c.Kind==Kind.Riser){
                    float side=half-.18f;
                    Box(root,"Сервисная стойка",V(-side,h.Height*.48f,-side),V(.18f,h.Height*.96f,.18f),Beam,true);
                    Box(root,"Табло стояка",V(-side,1.25f,-side+.12f),V(.48f,.72f,.16f),Deck);
                    for(int line=0;line<3;line++)
                        Box(root,"Маркировка линии",V(-side,1.44f-.17f*line,-side+.22f),V(.36f,.10f,.03f),CCPipeBuilder.Colors[line]);
                }
            }
            if(c.PipeVerticalOverlay && h.FloorUnder && CityModuleHouse.WantsFloor(c.Kind)){
                var below=h.Find(c.X,c.Y-1,c.Z);
                if(below==null || !CityModuleHouse.HasSlab(below.Kind))ServiceDeck(root,0,half,spacing);
            }
            for(int i=0;i<3;i++){
                float offset=PipeOffset(i,half,spacing);
                var hub=V(offset,Mathf.Lerp(.52f,Mathf.Min(3.22f,h.Height-.77f),i*.5f),offset);
                string color=CCPipeBuilder.Colors[i];
                float r=CCPipeBuilder.Radii[i];
                if(c.VerticalPipe){
                    var foot=V(offset,0,offset);
                    Rod(root,"Стояк",foot,foot+Vector3.up*h.Height,r,color,true);
                    foreach(float y in new[]{.17f,h.Height-.19f})
                        Rod(root,"Фланец стояка",foot+Vector3.up*y,foot+Vector3.up*(y+.10f),r+.055f,Beam);
                }
                int connections=0;
                Vector3 axis=c.VerticalPipe?Vector3.up:Vector3.right;
                foreach(var d in Directions){
                    var neighbour=h.Find(c.X+d.x,c.Y,c.Z+d.y);
                    if(neighbour==null || !neighbour.HasPipes)continue;
                    var end=d.x!=0?V(d.x*half,hub.y,hub.z):V(hub.x,hub.y,d.y*half);
                    Vector3 along=(end-hub).normalized;
                    if(connections==0 && !c.VerticalPipe)axis=along;
                    Rod(root,"Секция",hub,end,r,color,true);
                    Rod(root,"Стыковочный фланец",end-along*.14f,end-along*.025f,r+.065f,Beam);
                    connections++;
                }
                if(connections==0 && !c.VerticalPipe){
                    var stub=Vector3.right*Mathf.Min(1.15f,Mathf.Max(.18f,half-Mathf.Abs(offset)-.12f));
                    Rod(root,"Концевая секция",hub-stub,hub+stub,r,color,true);
                }
                if(connections>0)
                    Shape(root,"Стык",PrimitiveType.Sphere,hub,Vector3.one*(2.06f*r),color);
                int fitting=c.PartAt(i);
                if(fitting>0){
                    var device=CCPipeBuilder.BuildGridFitting(root,i,fitting-1);
                    if (i==0) CCColdPipeSetup.Configure(device,new Vector3Int(c.X,c.Y,c.Z));
                    device.name=FittingName(i);
                    device.localPosition=hub;
                    CCPipeBuilder.OrientMount(device,i,c.AnglesAt(i));
                    device.localRotation=Quaternion.LookRotation(axis,c.VerticalPipe?Vector3.forward:Vector3.up);
                }
            }
        }
        internal static Transform FindFitting(Transform network,CityModuleHouse.Cell cell,int line)
        {
            if (network == null) return null;
            var current = network.Find(FittingName(line));
            if (current != null) return current;
            // 07B writes fittings in line order and names each root after its model.
            int earlier = 0;
            for (int i=0;i<line;i++) if (cell.PartAt(i)>0) earlier++;
            foreach (Transform child in network){
                if (child.Find("Корпус арматуры")==null) continue;
                if (earlier==0) return child;
                earlier--;
            }
            return null;
        }
        static string FittingName(int line) => "Арматура линии " + (line+1);

        static float PipeOffset(int line,float half,float spacing)
        {
            // The drain has the largest flange. Give its side a little more clearance in narrow cells.
            float shift=Mathf.Max(0f,spacing+CCPipeBuilder.Radii[2]+.08f-half);
            return (line-1)*spacing-shift;
        }
        // Each pipe gets its own square penetration. Adjacent rows meet edge to edge.
        static void ServiceDeck(Transform root,float y,float half,float spacing)
        {
            float start=-half;
            for(int i=0;i<3;i++){
                float offset=PipeOffset(i,half,spacing);
                float radius=CCPipeBuilder.Radii[i]+.06f;
                float left=Mathf.Max(-half,offset-radius),right=Mathf.Min(half,offset+radius);
                DeckPanel(root,y,-half,half,start,left);
                DeckPanel(root,y,-half,left,left,right);
                DeckPanel(root,y,right,half,left,right);
                start=right;
            }
            DeckPanel(root,y,-half,half,start,half);
        }
        static void DeckPanel(Transform root,float y,float x0,float x1,float z0,float z1)
        {
            if(x1-x0<.015f || z1-z0<.015f)return;
            Box(root,"Секция настила",V((x0+x1)*.5f,y-.075f,(z0+z1)*.5f),V(x1-x0,.15f,z1-z0),Deck,true);
        }
        static void Landing(Transform r,float y,float half,float opening)
        {
            float width=half-opening;
            foreach(int side in new[]{-1,1}){
                Box(r,"Боковой настил",V(side*(half+opening)*.5f,y-.09f,0),V(width,.18f,2*half),Deck,true);
                Box(r,"Порог",V(0,y-.09f,side*(half+opening)*.5f),V(2*opening,.18f,width),Deck,true);
                Box(r,"Кромка",V(0,y+.009f,side*(opening+.055f)),V(opening*2,.018f,.10f),Stripe);
            }
        }
        static void Lift(Transform root,float w,float h)
        {
            float half=w*.5f, opening=Mathf.Min(half*.69f,2.45f), cab=opening-.055f;
            Landing(root,0,half,opening);
            Landing(root,-h,half,opening);
            foreach(int s in new[]{-1,1}){
                Box(root,"Обшивка шахты",V(s*(opening+.12f),-h*.5f,0),V(.12f,h,opening*2),Beam,true);
                Box(root,"Направляющая",V(s*(opening+.018f),-.45f*h,0),V(.035f,h*1.16f,.13f),Stripe);
                Box(root,"Верхняя боковина",V(s*(half-.11f),h*.5f,0),V(.14f,h,w-.22f),Deck,true);
            }
            Box(root,"Задняя стенка",V(0,0,-opening-.14f),V(opening*2,h*1.98f,.14f),Beam,true);
            // The actual roof is the house's standard slab generated above this landing.
            var machine=Node(root,"Лифт",Vector3.zero);
            machine.gameObject.AddComponent<NetworkObject>();
            var lift=machine.gameObject.AddComponent<CCGridLift>();lift.storey=h;
            var car=Node(root,"Кабина",Vector3.zero);lift.platform=car;
            Box(car,"Пол кабины",V(0,-.085f,0),V(cab*2,.17f,cab*2),Deck,true);
            var carry=car.gameObject.AddComponent<CCLiftPassenger>();carry.halfDeck=new Vector2(cab,cab);carry.deckTop=0;
            foreach(int s in new[]{-1,1}){
                Box(car,"Борт",V(s*(cab-.07f),.53f,0),V(.09f,1.06f,cab*2),Stripe,true);
            }
            var panel=Node(car,"Пульт кабины",V(cab-.21f,1.02f,cab-.48f));
            Button(panel,Vector3.zero,lift,2,panel.gameObject.AddComponent<NetworkObject>());
            for(int i=0;i<2;i++){
                var station=Node(root,i==0?"Верхний вызов":"Нижний вызов",V(opening+.24f,1.04f-i*h,opening+.2f));
                var id=station.gameObject.AddComponent<NetworkObject>();
                Box(station,"Стойка",V(0,-.48f,0),V(.13f,1.12f,.14f),Beam,true);
                Button(station,Vector3.zero,lift,i,id);
            }
        }
        static void Button(Transform parent,Vector3 at,CCGridLift lift,int stop,NetworkObject id)
        {
            Box(parent,"Пульт",at,V(.26f,.28f,.12f),Beam);
            var key=Box(parent,"F",at+Vector3.forward*.08f,V(.20f,.20f,.07f),stop==2?"55B195":Stripe,true,true);
            Layer(key);
            var input=key.gameObject.AddComponent<CCGridLiftButton>();input.lift=lift;input.stop=stop;input.station=id;
        }
    }
}
