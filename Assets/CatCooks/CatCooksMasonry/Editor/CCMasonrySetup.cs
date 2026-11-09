using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using FishNet.Object;
using CatCooks.CraneLogistics;

namespace CatCooks.Masonry.Editor
{
    public sealed class CCMasonrySetup:EditorWindow
    {
        CCWorkTerritory _territory;
        CCWorkZone _a,_b,_c;
        string _message="";
        [MenuItem("Tools/CatCooks/Masonry/Setup three brigades")]
        public static void Open(){GetWindow<CCMasonrySetup>("Masonry step 1").Show();}
        void OnGUI()
        {
            EditorGUILayout.HelpBox("Создаёт 3 новых участка по 6 капсул. Назначьте три РАЗНЫЕ приёмные зоны Bricks. Существующих котов и краны не пересоздаёт. Настройка только вне Play.",MessageType.Info);
            _territory=(CCWorkTerritory)EditorGUILayout.ObjectField("Territory",_territory,typeof(CCWorkTerritory),true);
            _a=(CCWorkZone)EditorGUILayout.ObjectField("Receiving — brigade 1",_a,typeof(CCWorkZone),true);
            _b=(CCWorkZone)EditorGUILayout.ObjectField("Receiving — brigade 2",_b,typeof(CCWorkZone),true);
            _c=(CCWorkZone)EditorGUILayout.ObjectField("Receiving — brigade 3",_c,typeof(CCWorkZone),true);
            using(new EditorGUI.DisabledScope(Application.isPlaying))if(GUILayout.Button("Create THREE masonry sites"))Create();
            EditorGUILayout.HelpBox(_message.Length==0?"После создания расставьте стены между колоннами и все Posts на полу. Сохраните сцену, Play -> Host.":_message,MessageType.None);
        }
        void Create()
        {
            if(_territory==null||_a==null||_b==null||_c==null){_message="Заполните четыре ссылки.";return;}
            if(_a==_b||_a==_c||_b==_c){_message="Для первого теста нужны три разные приёмные зоны.";return;}
            foreach(var z in new[]{_a,_b,_c})if(z.Owner!=_territory||!z.AcceptsDelivery||z.Resource!=CCZoneResource.Bricks){_message=z.name+": нужна зона Receiving/Work ресурса Bricks этой территории.";return;}
            foreach(var site in Object.FindObjectsByType<CCMasonrySite>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(site.Receiving==_a||site.Receiving==_b||site.Receiving==_c){_message="У одной из зон уже есть бригада. Измените существующий участок, не создавайте дубликат.";return;}
            Assets.Ensure();
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Create three masonry sites");
            int i=0;foreach(var zone in new[]{_a,_b,_c})Build(++i,zone);
            Undo.CollapseUndoOperations(group);_message="Созданы 18 котов, 3 стены, инструменты и кучи. Расставьте участки и проверьте пути. Краны настраиваются отдельными кнопками из гайда.";
        }
        void Build(int index,CCWorkZone zone)
        {
            GameObject g=Assets.Root("Masonry brigade "+index,null,zone.StagingPosition+Vector3.forward*5);
            Undo.AddComponent<NetworkObject>(g);CCMasonrySite s=Undo.AddComponent<CCMasonrySite>(g);s.Territory=_territory;s.Receiving=zone;
            GameObject wall=Assets.Root("WALL — centre bottom; local X along span",g.transform,Vector3.zero);
            s.Wall=Undo.AddComponent<CCMasonryWall>(wall);wall.GetComponent<MeshRenderer>().sharedMaterial=Assets.Material("Brick",new Color(.72f,.28f,.12f));Assets.Render(wall.GetComponent<MeshRenderer>());
            Vector3[] posts={new Vector3(-1,0,-1.7f),new Vector3(1,0,-1.7f),new Vector3(-3,0,-3),new Vector3(3,0,-3),new Vector3(-1.6f,0,-.65f),new Vector3(1.6f,0,-.65f)};
            string[] names={"Brick carrier A","Brick carrier B","Mortar mixer A","Mortar mixer B","Mason A","Mason B"};
            Transform points=Assets.Root("POSTS — move on floor",g.transform,Vector3.zero).transform;
            Transform cats=Assets.Root("CREW — visual roots",g.transform,Vector3.zero).transform;
            for(int i=0;i<6;i++)
            {
                s.Posts[i]=Assets.Root(names[i]+" post",points,posts[i]).transform;
                Transform actor=Assets.Root(names[i],cats,posts[i]).transform;s.Cats[i]=actor;
                GameObject body=GameObject.CreatePrimitive(PrimitiveType.Capsule);Undo.RegisterCreatedObjectUndo(body,"Create capsule");body.name="Visual — replace with your cat model";body.transform.SetParent(actor,false);body.transform.localPosition=new Vector3(0,.5f,0);body.transform.localScale=new Vector3(.42f,.5f,.42f);
                Object.DestroyImmediate(body.GetComponent<Collider>());body.GetComponent<Renderer>().sharedMaterial=Assets.Material(i<2?"Carrier":i<4?"Mixer":"Mason",i<2?new Color(.95f,.65f,.2f):i<4?new Color(.35f,.65f,.9f):new Color(.8f,.8f,.65f));Assets.Render(body.GetComponent<Renderer>());
                string model=i<2?"CarryBricks4":i<4?"Bucket":"Trowel";
                s.HeldItems[i]=Assets.Model(model,actor,new Vector3(0,.55f,.32f),Assets.Material(i<2?"Brick":"Metal",i<2?new Color(.72f,.28f,.12f):new Color(.35f,.4f,.42f))).transform;
                if(i>=2&&i<4)
                {
                    s.MixingTools[i-2]=Assets.Model("MixingShovel",actor,new Vector3(.15f,.2f,.35f),Assets.Material("Metal",Color.gray)).transform;
                    Assets.Model("MixingTrough",g.transform,posts[i]+Vector3.forward*.8f,Assets.Material("Metal",Color.gray));
                    Assets.Model("MortarHeap",g.transform,posts[i]+Vector3.forward*.8f,Assets.Material("Mortar",new Color(.56f,.53f,.46f)));
                }
            }
            s.WorkBrickPile=Assets.Model("CarryBricks4",g.transform,new Vector3(0,0,-1.7f),Assets.Material("Brick",Color.red)).transform;
            s.MortarPile=Assets.Model("MortarHeap",g.transform,new Vector3(.6f,0,-1.1f),Assets.Material("Mortar",Color.gray)).transform;
            // Keep the partial receiving pile beside the receiving zone when the whole wall site is moved.
            GameObject pile=Assets.Model("CarryBricks4",zone.transform,zone.transform.InverseTransformPoint(zone.StagingPosition+zone.transform.right*1.5f),Assets.Material("Brick",Color.red));
            pile.name="Opened batch — brigade "+index;s.DepotPartialPile=pile.transform;
            EditorUtility.SetDirty(s);EditorSceneManager.MarkSceneDirty(g.scene);EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);
        }
    }
    [CustomEditor(typeof(CCMasonrySite))]
    public sealed class CCMasonrySiteInspector:UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();var s=(CCMasonrySite)target;
            EditorGUILayout.HelpBox("Roles: 0/1 кирпичи; 2 замес + вёдра; 3 отключён при SingleMixer; 4/5 кладчики. Пути — прямые отрезки через optional waypoints, без NavMesh/обхода препятствий. Высота капсул 1 м.",MessageType.Info);
            if(!Application.isPlaying&&GUILayout.Button("Validate this site"))
            {
                if(s.ValidateSetup(out string why))Debug.Log("[Masonry] References OK. Check WALK paths and wall span manually.",s);
                else Debug.LogError("[Masonry] "+why,s);
            }
            if(Application.isPlaying){EditorGUILayout.LabelField("Built",s.Built+" / "+(s.Wall!=null?s.Wall.Total:0));Repaint();}
        }
    }
    public static class CCMasonryCraneTools
    {
        [MenuItem("Tools/CatCooks/Masonry/Configure selected crane for cooperative brick pallet")]
        static void Crane()
        {
            if(Application.isPlaying)return;
            CCSupplyCrane c=Selection.activeGameObject!=null?Selection.activeGameObject.GetComponent<CCSupplyCrane>():null;
            if(c==null||c.Cargo==null||c.Cargo.Length<2||c.Cargo[0]==null||c.Cargo[1]==null){Debug.LogError("Select crane root with CCSupplyCrane and both Cargo roots.");return;}
            Assets.Ensure();Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Cooperative pallet visual");Undo.RecordObject(c,"Set pallet mode");
            c.CooperativePallet=true;c.PalletCrewHalfWidth=.95f;
            // Preserve old visuals, deactivate only the children of Cargo roots. Undo restores.
            for(int i=0;i<2;i++)foreach(Transform child in c.Cargo[i]){Undo.RecordObject(child.gameObject,"Hide old cargo model");child.gameObject.SetActive(false);}
            Assets.Model("PalletBase",c.Cargo[0],Vector3.zero,Assets.Material("Wood",new Color(.5f,.34f,.15f)));
            Assets.Model("PalletBricks24",c.Cargo[0],Vector3.zero,Assets.Material("Brick",new Color(.72f,.28f,.12f)));
            EditorUtility.SetDirty(c);EditorSceneManager.MarkSceneDirty(c.gameObject.scene);Undo.CollapseUndoOperations(group);
            Debug.Log("Cooperative pallet enabled. Keep UnitsPerTrip=2; both cargo root references remain assigned. Resource/stock settings unchanged.",c);
        }
        [MenuItem("Tools/CatCooks/Masonry/Configure selected brick zone visuals and supply refill")]
        static void Stock()
        {
            if(Application.isPlaying)return;
            CCWorkZone z=Selection.activeGameObject!=null?Selection.activeGameObject.GetComponent<CCWorkZone>():null;
            if(z==null||z.Resource!=CCZoneResource.Bricks||(!z.AcceptsDelivery&&z.Kind!=CCZoneKind.Supply)){Debug.LogError("Select a Bricks Supply/Receiving/Work zone.");return;}
            Assets.Ensure();Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Brick supply visuals");
            Undo.RecordObject(z,"Configure brick stock");
            if(z.Kind==CCZoneKind.Supply){z.AutoRefillSupply=true;z.RefillDelay=5;z.InitialStock=2;z.Capacity=Mathf.Max(20,z.Capacity);}
            CCZoneStockVisual visual=z.GetComponent<CCZoneStockVisual>();if(visual==null)visual=Undo.AddComponent<CCZoneStockVisual>(z.gameObject);
            Undo.RecordObject(visual,"Configure pallet slots");
            if(visual.VisualRoot!=null)Undo.DestroyObjectImmediate(visual.VisualRoot.gameObject);
            visual.Zone=z;visual.UnitsPerSlot=2;
            Transform root=Assets.Root("Pallet stock — move slots near staging",z.transform,z.transform.InverseTransformPoint(z.StagingPosition)).transform;visual.VisualRoot=root;
            int count=Mathf.Clamp(Mathf.CeilToInt(z.Capacity/2f),1,16);visual.Slots=new GameObject[count];
            for(int i=0;i<count;i++)
            {
                Transform slot=Assets.Root("Pallet "+i,root,new Vector3(i%4*1.7f,0,i/4*1.3f)).transform;
                Assets.Model("PalletBase",slot,Vector3.zero,Assets.Material("Wood",new Color(.5f,.34f,.15f)));
                Assets.Model("PalletBricks24",slot,Vector3.zero,Assets.Material("Brick",new Color(.72f,.28f,.12f)));
                visual.Slots[i]=slot.gameObject;
            }
            visual.PreviewInitial();EditorUtility.SetDirty(z);EditorUtility.SetDirty(visual);EditorSceneManager.MarkSceneDirty(z.gameObject.scene);Undo.CollapseUndoOperations(group);
            Debug.Log("Pallet slots created. Each visual = up to 2 stock units. Keep the first slot at Staging, clear the walking lane. Supplies refill to InitialStock after pickup + 5 seconds.",z);
        }
    }
    static class Assets
    {
        static string _root;
        public static void Ensure()
        {
            if(!string.IsNullOrEmpty(_root))return;
            foreach(string guid in AssetDatabase.FindAssets("CCMasonrySetup t:MonoScript"))
            {
                string p=AssetDatabase.GUIDToAssetPath(guid);
                if(p.EndsWith("/Editor/CCMasonrySetup.cs")){_root=p.Substring(0,p.Length-"/Editor/CCMasonrySetup.cs".Length);break;}
            }
            if(string.IsNullOrEmpty(_root))throw new System.InvalidOperationException("Cannot locate CatCooksMasonry package");
            if(!AssetDatabase.IsValidFolder(_root+"/Generated"))AssetDatabase.CreateFolder(_root,"Generated");
        }
        public static Material Material(string name,Color color)
        {
            Ensure();string path=_root+"/Generated/"+name+".mat";Material m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m!=null)return m;
            Shader shader=Shader.Find("Universal Render Pipeline/Unlit");if(shader==null)shader=Shader.Find("Unlit/Color");if(shader==null)shader=Shader.Find("Standard");
            if(shader==null)throw new System.InvalidOperationException("No supported shader found");
            m=new Material(shader){name=name,enableInstancing=true};m.color=color;if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",color);AssetDatabase.CreateAsset(m,path);return m;
        }
        public static GameObject Root(string name,Transform parent,Vector3 local)
        {GameObject g=new GameObject(name);Undo.RegisterCreatedObjectUndo(g,"Create masonry object");if(parent!=null)g.transform.SetParent(parent,false);g.transform.localPosition=local;return g;}
        public static GameObject Model(string model,Transform parent,Vector3 pos,Material material)
        {
            Ensure();Mesh mesh=null;foreach(Object asset in AssetDatabase.LoadAllAssetsAtPath(_root+"/Models/"+model+".obj"))if(asset is Mesh m){mesh=m;break;}
            if(mesh==null)throw new System.InvalidOperationException("Missing imported mesh: "+model+".obj");
            GameObject g=Root(model,parent,pos);g.AddComponent<MeshFilter>().sharedMesh=mesh;MeshRenderer r=g.AddComponent<MeshRenderer>();r.sharedMaterial=material;Render(r);return g;
        }
        public static void Render(Renderer r){r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;}
    }
}
