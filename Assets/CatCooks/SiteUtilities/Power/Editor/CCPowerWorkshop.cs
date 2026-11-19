using System.Linq;
using UnityEditor;
using UnityEngine;
using static CatCooks.SiteTools.CCUtilityArt;

namespace CatCooks.SiteTools
{
    public sealed class CCPowerWorkshop : EditorWindow
    {
        enum Device { Pole, Mobile, PoleBox, MastLamp, TripodFlood, TripodTwin }
        static readonly string[] Titles={"Опора с изоляторами","Трансформатор на колёсах","Малый щит на опору","Мачтовый прожектор","Тренога с широким светом","Тренога с двумя головами"};
        const string Steel="505D67", Yellow="D19838", Rubber="303638", Ceramic="B3C7BB", Ivory="D7D3BC", Green="527570", Ink="273840";
        static readonly Color Warm=new Color(1f,.76f,.49f);
        [MenuItem("CatCooks/Строительные сети/Электрика")]
        static void Open()=>GetWindow<CCPowerWorkshop>("Электрика");
        void OnGUI(){
            using(new EditorGUI.DisabledScope(Application.isPlaying)){
                for(int i=0;i<Titles.Length;i++)if(GUILayout.Button(Titles[i]))Place((Device)i);
                EditorGUILayout.Space();
                if(GUILayout.Button("Соединить две выбранные клеммные пары"))Connect();
            }
        }
        static void Place(Device d){
            var selected=Selection.activeTransform;
            var pole=selected!=null?selected.GetComponentInParent<CCPowerPort>():null;
            bool mount=d==Device.PoleBox && pole!=null && pole.name.StartsWith(Titles[0]);
            // Create editable prefabs once. Further placements retain the artist's prefab edits.
            Paint(Steel);
            string path=Folder+"/Power_"+d+".prefab";
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(prefab==null){
                var model=Node(null,Titles[(int)d],Vector3.zero);
                try{Build(model,d);prefab=PrefabUtility.SaveAsPrefabAsset(model.gameObject,path);}
                finally{Object.DestroyImmediate(model.gameObject);}
            }
            var root=((GameObject)PrefabUtility.InstantiatePrefab(prefab)).transform;
            if(mount){root.SetParent(pole.transform,false);root.localPosition=V(0,2.65f,.23f);}
            else root.position=SceneView.lastActiveSceneView!=null?SceneView.lastActiveSceneView.pivot:Vector3.zero;
            Finish(root);
        }
        static void Build(Transform root,Device d){
            var port=root.gameObject.AddComponent<CCPowerPort>();
            if(d==Device.Pole || d==Device.MastLamp){
                Rod(root,"Бетонная пята",V(0,0,0),V(0,.38f,0),.36f,Steel,true);
                Rod(root,"Мачта",V(0,.22f,0),V(0,5.52f,0),.145f,"87918A",true);
                Rod(root,"Оголовок",V(0,4.55f,0),V(0,5.68f,0),.114f,Steel);
                Box(root,"Траверса",V(0,5.35f,0),V(1.72f,.13f,.14f),Green);
                for(int s=-1;s<=1;s+=2)Rod(root,"Подкос",V(0,4.80f,0),V(s*.70f,5.32f,0),.048f,Steel);
                Terminals(port,5.40f,.72f,0);
                if(d==Device.MastLamp){
                    Rod(root,"Вынос",V(0,5.12f,0),V(0,5.58f,.82f),.07f,Steel);
                    Lamp(port,new[]{V(0,5.45f,.85f)},true);
                }
            }else if(d==Device.Mobile || d==Device.PoleBox)Panel(port,d==Device.Mobile);
            else{
                for(int j=0;j<3;j++){
                    float a=j*Mathf.PI*2/3;Vector3 foot=V(.70f*Mathf.Cos(a),.045f,.70f*Mathf.Sin(a));
                    Rod(root,"Ножка",foot,V(0,.85f,0),.043f,Yellow,true);
                    Rod(root,"Распорка",foot*.62f+Vector3.up*.29f,V(0,.34f,0),.024f,Steel);
                    Box(root,"Башмак",foot,V(.19f,.07f,.14f),Rubber,true);
                }
                Rod(root,"Стойка",V(0,.30f,0),V(0,1.6f,0),.072f,Yellow,true);
                Rod(root,"Выдвижная секция",V(0,1.42f,0),V(0,2.14f,0),.044f,Ceramic);
                Rod(root,"Зажим",V(0,1.50f,0),V(0,1.61f,0),.09f,Steel);
                bool twin=d==Device.TripodTwin;
                Box(root,"Планка",V(0,2.1f,0),V(twin?1.03f:.48f,.085f,.09f),Steel);
                Terminals(port,.38f,.105f,-.14f);
                Lamp(port,twin?new[]{V(-.36f,2.25f,0),V(.36f,2.25f,0)}:new[]{V(0,2.27f,0)},!twin);
            }
        }
        static void Terminals(CCPowerPort port,float y,float gap,float z){
            for(int s=-1;s<=1;s+=2){
                Vector3 b=V(s*gap,y,z);
                Rod(port.transform,"Штырь",b,b+Vector3.up*.30f,.048f,Steel);
                for(int j=0;j<3;j++)Rod(port.transform,"Фарфоровое ребро",b+Vector3.up*(.055f+j*.075f),b+Vector3.up*(.09f+j*.075f),.093f,Ceramic);
                var end=Node(port.transform,s<0?"Клемма L":"Клемма R",b+Vector3.up*.31f);
                if(s<0)port.left=end;else port.right=end;
            }
        }
        static void Lamp(CCPowerPort port,Vector3[] positions,bool wide){
            port.beams=new Light[positions.Length];port.lenses=new Renderer[positions.Length];
            port.dark=Paint("524C43");port.luminous=Paint("FFD28A",true);
            for(int i=0;i<positions.Length;i++){
                var head=Node(port.transform,"Поворотная скоба "+(i+1),positions[i]);
                head.localRotation=Quaternion.Euler(wide?32:18,wide?0:(i==0?-23:23),0);
                float w=wide?.69f:.44f;
                Box(head,"Корпус",Vector3.zero,V(w,.36f,.22f),Steel);
                for(int j=-2;j<=2;j++)Box(head,"Радиатор",V(j*w/6,0,-.13f),V(.032f,.30f,.085f),Ink);
                var lens=Box(head,"Рассеиватель",V(0,0,.12f),V(w-.075f,.28f,.035f),"524C43");
                port.lenses[i]=lens.GetComponent<Renderer>();
                Box(head,"Козырёк",V(0,.18f,.055f),V(w+.05f,.035f,.34f),Yellow);
                var lt=Node(head,"Тёплый свет",V(0,0,.16f)).gameObject.AddComponent<Light>();
                lt.type=LightType.Spot;lt.color=Warm;lt.range=wide?19:24;lt.spotAngle=wide?70:41;lt.innerSpotAngle=lt.spotAngle*.60f;
                lt.intensity=wide?16:22;lt.enabled=false;port.beams[i]=lt;
            }
        }
        static void Panel(CCPowerPort port,bool mobile){
            var root=port.transform;
            root.gameObject.AddComponent<FishNet.Object.NetworkObject>();
            var panel=root.gameObject.AddComponent<CCPowerPanel>();panel.source=mobile;panel.port=port;port.panel=panel;
            float w=mobile?1.62f:.69f,h=mobile?1.32f:.76f,d=mobile?.87f:.37f,b=mobile?.55f:0;
            if(mobile){
                Box(root,"Шасси",V(0,.49f,0),V(1.88f,.16f,1.06f),Ink,true);
                foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1}){
                    var center=V(x*.88f,.234f,z*.39f);
                    Rod(root,"Колесо",center-Vector3.right*.09f,center+Vector3.right*.09f,.235f,Rubber,true);
                    Rod(root,"Ступица",center+Vector3.right*x*.092f,center+Vector3.right*x*.11f,.11f,Steel);
                }
                Rod(root,"Дышло",V(0,.45f,-.42f),V(0,.29f,-1.06f),.06f,Steel);
            }else foreach(float y in new[]{.12f,.61f})Box(root,"Хомут опоры",V(0,y,-.23f),V(.59f,.065f,.17f),Steel);
            Box(root,"Задняя стенка",V(0,b+h*.5f,-d*.45f),V(w,h,.11f),Green,true);
            foreach(float s in new[]{-1f,1f}){
                Box(root,"Боковина",V(s*w*.48f,b+h*.5f,0),V(.055f,h,d),Green,true);
                for(int j=0;j<6;j++)Box(root,"Охлаждение",V(s*w*.51f,b+h*.48f,(j-2.5f)*d*.14f),V(.09f,h*.72f,.035f),Steel);
            }
            Box(root,"Основание",V(0,b+.04f,0),V(w,.09f,d),Steel,true);
            Box(root,"Козырёк",V(0,b+h,0),V(w+.08f,.09f,d+.11f),Green,true);
            Terminals(port,b+h+.045f,w*.29f,-d*.24f);
            var face=Node(root,"Панель приборов",V(0,b+h*.49f,d*.12f));
            Box(face,"Панель",Vector3.zero,V(w*.89f,h*.81f,.065f),Ivory);
            if(mobile)Art(face,"Assets/CatCooks/SiteUtilities/Power/Models/PanelMeter.fbx",V(0,-.39f,.069f),.23f);
            panel.levers=new Transform[mobile?2:1];
            for(int i=0;i<panel.levers.Length;i++){
                var pivot=Node(face,"Автомат "+(i+1),V(mobile?(i-.5f)*w*.35f:0,-h*.10f,.09f));
                Box(face,"Колодка",pivot.localPosition-Vector3.forward*.04f,V(w*.22f,h*.26f,.08f),Ink);
                var knob=Box(pivot,"Тумблер",V(0,.02f,.065f),V(w*.16f,h*.12f,.11f),i==0?Yellow:Green,true);
                Control(knob,panel,i+1);panel.levers[i]=pivot;
            }
            panel.indicators=new Renderer[4];panel.glowing=new Material[4];panel.dark=Paint("382F2B");
            string[] colors={"FF4933","469EFF","5CFF8C"};
            for(int i=0;i<3;i++){
                var pos=V((i-1)*w*.22f,h*.21f,.048f);
                Rod(face,"Обойма",pos,pos+Vector3.forward*.040f,w*.06f,Steel);
                panel.indicators[i]=Rod(face,"Индикатор",pos+Vector3.forward*.04f,pos+Vector3.forward*.062f,w*.044f,"382F2B").GetComponent<Renderer>();
                panel.glowing[i]=Paint(colors[i],true);
            }
            var beacon=V(-w*.40f,b+h+.055f,d*.31f);
            Rod(root,"Цоколь маяка",beacon,beacon+Vector3.up*.042f,.061f,Ink);
            panel.indicators[3]=Shape(root,"Аварийный маяк",PrimitiveType.Sphere,beacon+Vector3.up*.088f,V(.097f,.12f,.097f),"382F2B").GetComponent<Renderer>();
            panel.glowing[3]=Paint(colors[0],true);
            var door=Node(root,"Петля двери",V(-w*.5f,b+h*.5f,d*.5f));
            panel.door=door;
            var plate=Box(door,"Дверца",V(w*.5f,0,0),V(w,h*.97f,.045f),Green,true);Control(plate,panel,0);
            Box(door,"Защёлка",V(w*.90f,0,.047f),V(.045f,h*.22f,.062f),Steel);
            Box(door,"Табличка",V(w*.50f,h*.18f,.031f),V(w*.54f,h*.22f,.018f),Yellow);
            var label=Node(door,"Маркировка",V(w*.50f,h*.18f,.043f));
            label.localRotation=Quaternion.Euler(0,180,0);
            var txt=label.gameObject.AddComponent<TextMesh>();txt.text=mobile?"ТП / 04":"ЩР / 02";txt.anchor=TextAnchor.MiddleCenter;txt.alignment=TextAlignment.Center;txt.fontSize=55;txt.characterSize=w*.052f;txt.color=Color.black;
        }
        static void Control(Transform target,CCPowerPanel panel,int bit){
            Layer(target);var b=target.gameObject.AddComponent<CCPanelControl>();b.panel=panel;b.bit=bit;
        }
        static void Connect(){
            var ports=Selection.gameObjects.Select(t=>t.GetComponentInParent<CCPowerPort>()).Where(p=>p!=null).Distinct().ToArray();
            if(ports.Length!=2 || ports[0].gameObject.scene!=ports[1].gameObject.scene){Debug.LogWarning("Выдели два устройства в одной сцене через Ctrl в Hierarchy.");return;}
            foreach(var s in Object.FindObjectsByType<CCWireSpan>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if((s.a==ports[0] && s.b==ports[1])||(s.a==ports[1] && s.b==ports[0])){Selection.activeObject=s;return;}
            var root=Node(null,"Провода — "+ports[0].name+" — "+ports[1].name,ports[0].transform.position);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root.gameObject,ports[0].gameObject.scene);
            var span=root.gameObject.AddComponent<CCWireSpan>();span.a=ports[0];span.b=ports[1];
            for(int i=0;i<2;i++){
                var line=Node(root,"Жила "+(i+1),Vector3.zero).gameObject.AddComponent<LineRenderer>();
                line.sharedMaterial=Paint("302F30");line.widthMultiplier=.027f;line.useWorldSpace=true;line.numCapVertices=3;
                if(i==0)span.left=line;else span.right=line;
            }
            span.Register();span.Draw();Finish(root);
        }
    }
}
