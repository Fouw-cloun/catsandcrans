using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using FishNet.Object;
using CatCooks.CraneLogistics;
using CatCooks.Masonry;
namespace CatCooks.FrameAssembly.Editor
{
    public sealed class CCFrameAssemblySetup:EditorWindow
    {
        CCZoneCraneRoute _route;
        CCMasonrySite _m1,_m2,_m3;
        Transform _origin;
        int _columns=4;
        float _spacing=6,_height=3;
        string _result="";
        [MenuItem("Tools/CatCooks/Frame Assembly/Connect crane and create frame crew")]
        public static void Open(){GetWindow<CCFrameAssemblySetup>("Direct mount v2").Show();}
        void OnGUI()
        {
            EditorGUILayout.HelpBox("Один общий маршрут работающего кирпичного крана. Создаёт 2 монтажников, цепочку каркаса и 2 нижних склада металла. Receiving остаются только для поддонов. Кран подаёт металл прямо в Nodes и держит до крепления. После создания расставить Supply и Nodes, проверить Validate ALL direct targets.",MessageType.Info);
            _route=(CCZoneCraneRoute)EditorGUILayout.ObjectField("Shared crane route",_route,typeof(CCZoneCraneRoute),true);
            _m1=(CCMasonrySite)EditorGUILayout.ObjectField("Masonry 1 (optional)",_m1,typeof(CCMasonrySite),true);
            _m2=(CCMasonrySite)EditorGUILayout.ObjectField("Masonry 2 (optional)",_m2,typeof(CCMasonrySite),true);
            _m3=(CCMasonrySite)EditorGUILayout.ObjectField("Masonry 3 (optional)",_m3,typeof(CCMasonrySite),true);
            _origin=(Transform)EditorGUILayout.ObjectField("First column marker",_origin,typeof(Transform),true);
            _columns=EditorGUILayout.IntSlider("Columns",_columns,2,16);
            _spacing=EditorGUILayout.FloatField("Span length",_spacing);_height=EditorGUILayout.FloatField("Column height",_height);
            using(new EditorGUI.DisabledScope(Application.isPlaying))if(GUILayout.Button("Create and connect demand queue"))Create();
            EditorGUILayout.HelpBox(_result.Length==0?"Для отдельного теста монтажа Masonry можно оставить пустыми. Маркер поставь у основания первой колонны.":_result,MessageType.None);
        }
        void Create()
        {
            if(_route==null||_route.Crane==null||_route.Owner==null||_origin==null){_result="Назначь маршрут и маркер первой колонны.";return;}
            var territory=_route.Owner;var crane=_route.Crane;
            if(_spacing<1.5f||_height<1.5f){_result="Размеры должны быть не меньше 1.5 м.";return;}
            foreach(var crew in Object.FindObjectsByType<CCFrameAssemblySite>(FindObjectsInactive.Include,FindObjectsSortMode.None))if(crew.Route==_route){_result="У маршрута уже есть монтажная бригада. Настраивай существующую.";return;}
            for(int i=0;i<_route.SourceCount;i++){var z=_route.SourceAt(i);if(z!=null&&z.Resource!=CCZoneResource.Bricks){_result="Для первого подключения используй маршрут с кирпичными складами. Смешанные старые визуалы не заменяются автоматически.";return;}}
            if(crane.Cargo==null||crane.Cargo.Length!=2||crane.Cargo[0]==null||crane.Cargo[1]==null){_result="У крана должны быть оба Cargo root.";return;}
            if(crane.Platform==null||crane.Cargo[0].IsChildOf(crane.Platform)||crane.Crew[0].IsChildOf(crane.Platform)||crane.Crew[1].IsChildOf(crane.Platform)||crane.Platform==crane.transform){_result="Platform должна быть отдельным объектом: Cargo/Crew и геометрия крана не должны быть её дочерними.";return;}
            foreach(var m in new[]{_m1,_m2,_m3})if(m!=null)
            {
                bool listed=false;for(int i=0;i<_route.DestinationCount;i++)if(_route.DestinationAt(i)==m.Receiving)listed=true;
                if(m.Territory!=territory||!listed||m.Receiving==null||m.Receiving.Resource!=CCZoneResource.Bricks){_result="Каждая кладочная бригада должна ссылаться на кирпичный приёмник этого маршрута.";return;}
            }
            CCWorkZone initial=_route.SourceAt(0);if(initial==null){_result="Нужен первый существующий Supply как ориентир нижнего этажа.";return;}
            FrameAssets.CheckModels();
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Create frame demand system");
            Vector3 side=_origin.right;
            CCWorkZone cs=Zone(territory,"Column supply — MOVE before Host",CCZoneKind.Supply,CCZoneResource.SteelColumn,initial.DockPosition+side*10);
            CCWorkZone bs=Zone(territory,"Beam supply — MOVE before Host",CCZoneKind.Supply,CCZoneResource.SteelBeam,initial.DockPosition+side*20);
            Undo.RecordObject(_route,"Add frame zones and demand mode");_route.Sources=Append(_route,true,cs,bs);_route.DemandDriven=true;_route.Enabled=true;_route.TrafficGroup="";_route.UnitsPerTrip=2;_route.AllowPartialLoad=false;
            Undo.RecordObject(territory,"Register frame zones");territory.RefreshLocal();territory.Dispatch=true;
            if(crane.Platform==null||crane.Cargo[0].IsChildOf(crane.Platform)||crane.Crew[0].IsChildOf(crane.Platform)||crane.Crew[1].IsChildOf(crane.Platform)||crane.Platform==crane.transform){_result="Platform должна быть отдельным объектом: Cargo/Crew и геометрия крана не должны быть её дочерними.";return;}
            foreach(var m in new[]{_m1,_m2,_m3})if(m!=null)
            {Undo.RecordObject(m,"Connect masonry demand");m.DemandRoute=_route;m.SingleMixer=true;if(m.Cats.Length>3&&m.Cats[3]!=null){Undo.RecordObject(m.Cats[3].gameObject,"Hide extra mixer");m.Cats[3].gameObject.SetActive(false);}EditorUtility.SetDirty(m);EditorSceneManager.MarkSceneDirty(m.gameObject.scene);}
            GameObject g=FrameAssets.Root("Frame crew — "+crane.name,null,_origin.position);g.transform.rotation=_origin.rotation;
            Undo.AddComponent<NetworkObject>(g);var site=Undo.AddComponent<CCFrameAssemblySite>(g);site.Territory=territory;site.Route=_route;site.ColumnHeight=_height;
            site.Nodes=new Transform[_columns];site.Columns=new Transform[_columns];site.Beams=new Transform[_columns-1];
            var nodes=FrameAssets.Root("COLUMN BASES — ordered",g.transform,Vector3.zero).transform;
            var metal=FrameAssets.Material("Steel",new Color(.3f,.4f,.43f));
            for(int i=0;i<_columns;i++)
            {
                site.Nodes[i]=FrameAssets.Root("Column base "+i,nodes,new Vector3(i*_spacing,0,0)).transform;
                site.Columns[i]=FrameAssets.Model("SteelColumnUnit",g.transform,new Vector3(i*_spacing,0,0),metal).transform;site.Columns[i].localScale=new Vector3(1,_height,1);site.Columns[i].gameObject.SetActive(false);
                if(i<_columns-1){site.Beams[i]=FrameAssets.Model("SteelBeamUnit",g.transform,new Vector3((i+.5f)*_spacing,_height,0),metal).transform;site.Beams[i].localScale=new Vector3(_spacing,1,1);site.Beams[i].gameObject.SetActive(false);}
            }
            for(int i=0;i<2;i++)
            {
                var cat=FrameAssets.Root("Steel worker "+(i+1),g.transform,new Vector3(i,0,-2)).transform;site.Cats[i]=cat;
                var body=GameObject.CreatePrimitive(PrimitiveType.Capsule);Undo.RegisterCreatedObjectUndo(body,"Create steel cat");body.name="Visual capsule";body.transform.SetParent(cat,false);body.transform.localPosition=Vector3.up*.5f;body.transform.localScale=new Vector3(.42f,.5f,.42f);Object.DestroyImmediate(body.GetComponent<Collider>());body.GetComponent<Renderer>().sharedMaterial=FrameAssets.Material("Crew",new Color(.9f,.55f,.2f));FrameAssets.Render(body.GetComponent<Renderer>());
                site.Ladders[i]=FrameAssets.Model("LadderUnit",g.transform,new Vector3(i,0,-1),metal).transform;site.Ladders[i].gameObject.SetActive(false);
                site.CarriedLadders[i]=FrameAssets.Model("LadderUnit",cat,new Vector3(0,.1f,-.3f),metal).transform;site.CarriedLadders[i].localScale=new Vector3(.65f,1.7f,.65f);
                site.Hammers[i]=FrameAssets.Model("BoltHammer",cat,new Vector3(.25f,.6f,.3f),metal).transform;
                site.Torches[i]=FrameAssets.Model("WeldingTorch",cat,new Vector3(.25f,.6f,.3f),metal).transform;
                var flash=GameObject.CreatePrimitive(PrimitiveType.Sphere);Undo.RegisterCreatedObjectUndo(flash,"Create weld flash");flash.name="Weld flash — no particles";flash.transform.SetParent(site.Torches[i],false);flash.transform.localPosition=new Vector3(0,.22f,.16f);flash.transform.localScale=Vector3.one*.07f;Object.DestroyImmediate(flash.GetComponent<Collider>());flash.GetComponent<Renderer>().sharedMaterial=FrameAssets.Material("Weld",Color.cyan);FrameAssets.Render(flash.GetComponent<Renderer>());site.WeldFlashes[i]=flash.transform;site.Hammers[i].gameObject.SetActive(false);site.Torches[i].gameObject.SetActive(false);flash.SetActive(false);
            }
            ConfigureCargo(crane);StockModel(cs);StockModel(bs);
            foreach(var obj in new Object[]{site,_route,territory,crane})EditorUtility.SetDirty(obj);
            EditorSceneManager.MarkSceneDirty(g.scene);EditorSceneManager.MarkSceneDirty(territory.gameObject.scene);EditorSceneManager.MarkSceneDirty(crane.gameObject.scene);Undo.CollapseUndoOperations(group);
            _result="Готово. Расставь два Supply металла и основания Nodes. На Frame crew нажми Validate ALL direct targets. Сохрани сцену, включи Run крана и запускай Host. Receiving для металла не нужны. В очереди — заявки подключённых кладчиков и монтажников.";
            Selection.activeGameObject=g;
        }
        static CCWorkZone[] Append(CCZoneCraneRoute route,bool sources,params CCWorkZone[] added)
        {var list=new List<CCWorkZone>();int n=sources?route.SourceCount:route.DestinationCount;for(int i=0;i<n;i++){var z=sources?route.SourceAt(i):route.DestinationAt(i);if(z!=null&&!list.Contains(z))list.Add(z);}foreach(var z in added)if(!list.Contains(z))list.Add(z);return list.ToArray();}
        static CCWorkZone Zone(CCWorkTerritory owner,string name,CCZoneKind kind,CCZoneResource resource,Vector3 world)
        {
            var g=FrameAssets.Root(name,owner.transform,owner.transform.InverseTransformPoint(world));var z=Undo.AddComponent<CCWorkZone>(g);z.Kind=kind;z.Resource=resource;z.Size=new Vector3(12,4,12);z.Capacity=kind==CCZoneKind.Supply?20:1;z.InitialStock=kind==CCZoneKind.Supply?1:0;z.AutoRefillSupply=kind==CCZoneKind.Supply;z.RefillDelay=5;
            z.Dock=FrameAssets.Root("Dock — steel BASE on floor",g.transform,Vector3.zero).transform;z.Staging=FrameAssets.Root("Staging — reserved (steel uses Dock)",g.transform,new Vector3(0,0,4)).transform;z.Label=name;return z;
        }
        void ConfigureCargo(CCSupplyCrane c)
        {
            Undo.RecordObject(c,"Install payload library");
            foreach(Transform child in c.Cargo[0]){Undo.RecordObject(child.gameObject,"Hide old payload");child.gameObject.SetActive(false);}
            CCCranePayloadLibrary lib=c.GetComponent<CCCranePayloadLibrary>();if(lib==null)lib=Undo.AddComponent<CCCranePayloadLibrary>(c.gameObject);Undo.RecordObject(lib,"Payload entries");
            var brick=FrameAssets.Root("Payload Bricks",c.Cargo[0],Vector3.zero);
            FrameAssets.Model("PalletBase",brick.transform,Vector3.zero,FrameAssets.Material("Wood",new Color(.5f,.34f,.15f)));
            FrameAssets.Model("PalletBricks24",brick.transform,Vector3.zero,FrameAssets.Material("Brick",new Color(.72f,.28f,.12f)));
            var column=FrameAssets.Model("SteelColumnUnit",c.Cargo[0],Vector3.down*_height,FrameAssets.Material("Steel",Color.gray));column.transform.localScale=new Vector3(1,_height,1);
            var beam=FrameAssets.Model("SteelBeamUnit",c.Cargo[0],new Vector3(0,-.15f,0),FrameAssets.Material("Steel",Color.gray));beam.transform.localScale=new Vector3(_spacing,1,1);
            lib.Entries=new[]{new CCCranePayloadLibrary.Entry{Resource=CCZoneResource.Bricks,Visual=brick},new CCCranePayloadLibrary.Entry{Resource=CCZoneResource.SteelColumn,Visual=column,Footprint=new Vector2(.45f,.45f),Clearance=_height},new CCCranePayloadLibrary.Entry{Resource=CCZoneResource.SteelBeam,Visual=beam,Footprint=new Vector2(_spacing,.3f),Clearance=.3f}};
            c.CooperativePallet=true;c.PayloadLibrary=lib;lib.Apply((int)CCZoneResource.Bricks);EditorUtility.SetDirty(lib);
        }
        void StockModel(CCWorkZone z)
        {
            var v=Undo.AddComponent<CCZoneStockVisual>(z.gameObject);v.Zone=z;v.UnitsPerSlot=1;
            Transform root=FrameAssets.Root("Stock visual",z.Dock,Vector3.zero).transform;v.VisualRoot=root;
            bool column=z.Resource==CCZoneResource.SteelColumn;
            var model=FrameAssets.Model(column?"SteelColumnUnit":"SteelBeamUnit",root,column?Vector3.zero:new Vector3(0,.15f,0),FrameAssets.Material("Steel",Color.gray));
            model.transform.localScale=column?new Vector3(1,_height,1):new Vector3(_spacing,1,1);
            v.Slots=new[]{model};v.PreviewInitial();
        }
    }
    [CustomEditor(typeof(CCFrameAssemblySite))]
    public sealed class CCFrameAssemblyInspector:UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {DrawDefaultInspector();var s=(CCFrameAssemblySite)target;if(GUILayout.Button("Validate frame crew")){if(s.ValidateSetup(out string error))Debug.Log("Frame references OK. Check ALL direct targets next.",s);else Debug.LogError(error,s);}if(GUILayout.Button("Validate ALL direct targets")){if(s.ValidateReach(out string error))Debug.Log("All direct installation tops are reachable. Collision/path clearance still needs a Host test.",s);else Debug.LogError(error,s);}if(Application.isPlaying){EditorGUILayout.LabelField("Completed",s.Completed+" / "+s.Total);Repaint();}}
    }
    static class FrameAssets
    {
        static string RootPath()
        {foreach(string guid in AssetDatabase.FindAssets("CCFrameAssemblySetup t:MonoScript")){string p=AssetDatabase.GUIDToAssetPath(guid);if(p.EndsWith("/Editor/CCFrameAssemblySetup.cs"))return p.Substring(0,p.Length-"/Editor/CCFrameAssemblySetup.cs".Length);}throw new System.InvalidOperationException("Cannot locate frame package");}
        static Mesh Mesh(string name)
        {
            foreach(string guid in AssetDatabase.FindAssets(name+" t:Model"))
            {string path=AssetDatabase.GUIDToAssetPath(guid);if(!path.EndsWith("/"+name+".obj"))continue;foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(path))if(asset is Mesh m)return m;}
            throw new System.InvalidOperationException("Missing imported OBJ "+name);
        }
        public static void CheckModels(){foreach(var name in new[]{"SteelColumnUnit","SteelBeamUnit","LadderUnit","BoltHammer","WeldingTorch","PalletBase","PalletBricks24"})Mesh(name);}
        public static Material Material(string name,Color color)
        {string root=RootPath();if(!AssetDatabase.IsValidFolder(root+"/Generated"))AssetDatabase.CreateFolder(root,"Generated");string path=root+"/Generated/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m!=null)return m;Shader s=Shader.Find("Universal Render Pipeline/Unlit")??Shader.Find("Unlit/Color")??Shader.Find("Standard");if(s==null)throw new System.InvalidOperationException("No material shader");m=new Material(s){name=name,color=color,enableInstancing=true};if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",color);AssetDatabase.CreateAsset(m,path);return m;}
        public static GameObject Root(string name,Transform parent,Vector3 p){var g=new GameObject(name);Undo.RegisterCreatedObjectUndo(g,"Create frame object");if(parent!=null)g.transform.SetParent(parent,false);g.transform.localPosition=p;return g;}
        public static GameObject Model(string name,Transform parent,Vector3 p,Material material){var mesh=Mesh(name);var g=Root(name,parent,p);g.AddComponent<MeshFilter>().sharedMesh=mesh;var r=g.AddComponent<MeshRenderer>();r.sharedMaterial=material;Render(r);return g;}
        public static void Render(Renderer r){r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;}
    }
}
