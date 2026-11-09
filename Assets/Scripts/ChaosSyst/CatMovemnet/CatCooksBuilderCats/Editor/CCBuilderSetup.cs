using System.Collections.Generic;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CatCooks.Builders.Editor
{
    public sealed class CCBuilderSetup : EditorWindow
    {
        private const string GeneratedFolder = "Assets/Scripts/ChaosSyst/CatMovemnet/Generated";
        private int _count=20;
        private float _playerHeight=1.8f;
        private bool _floor;
        private Vector3 _origin;
        [MenuItem("Tools/CatCooks/Builder Cats/1 - Create test colony")]
        private static void Open()
        {
            CCBuilderSetup window=GetWindow<CCBuilderSetup>("Коты-строители");
            window.minSize=new Vector2(380,290);
            window._origin=Selection.activeTransform!=null?Selection.activeTransform.position:
                SceneView.lastActiveSceneView!=null?SceneView.lastActiveSceneView.pivot:Vector3.zero;
            CharacterController player=Selection.activeGameObject!=null?Selection.activeGameObject.GetComponent<CharacterController>():null;
            if(player!=null)window._playerHeight=player.height*Mathf.Abs(player.transform.lossyScale.y);
        }
        private void OnGUI()
        {
            EditorGUILayout.LabelField("Шаг 1: движение и группы",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Создаст капсулы и 8 точек в текущей сцене. Для движения запусти Host/Server через свой FishNet NetworkManager.",MessageType.Info);
            _count=EditorGUILayout.IntSlider("Количество котов",_count,6,64);
            _playerHeight=EditorGUILayout.Slider("Рост игрока (метры)",_playerHeight,1f,3f);
            EditorGUILayout.LabelField("Рост кота: "+(_playerHeight*.5f).ToString("0.00")+" м");
            _origin=EditorGUILayout.Vector3Field("Центр площадки",_origin);
            _floor=EditorGUILayout.Toggle("Добавить тестовый пол",_floor);
            EditorGUILayout.HelpBox("На своей карте оставь пол выключенным. Центр поставь на землю, затем подвинь рабочие точки и выполни Bake.",MessageType.None);
            GUI.enabled=!Application.isPlaying;
            if(GUILayout.Button("Создать котов и точки",GUILayout.Height(34)))Create();
            GUI.enabled=true;
        }
        private void Create()
        {
            if(Application.isPlaying)return;
            if(!SceneManager.GetActiveScene().IsValid() || PrefabStageUtility.GetCurrentPrefabStage()!=null)
            { Debug.LogError("[Коты] Открой игровую сцену, не Prefab Mode.");return; }
            // Prepare assets before touching the scene: a failed save must not leave an empty colony.
            Material[] palette;
            try { palette=CreateMaterials(); }
            catch (System.Exception exception)
            {
                Debug.LogError("[Коты] Не удалось подготовить материалы в "+GeneratedFolder+": "+exception.Message);
                return;
            }
            if(palette==null)return;
            Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Create builder cat colony");
            GameObject root=new GameObject("Builder Cats - Step 1");
            Undo.RegisterCreatedObjectUndo(root,"Create colony");root.transform.position=_origin;
            CCBuilderDirector director=Undo.AddComponent<CCBuilderDirector>(root);
            CCBuilderNavArea area=Undo.AddComponent<CCBuilderNavArea>(root);
            float height=_playerHeight*.5f,diameter=height*.48f;
            area.AgentHeight=height;area.AgentRadius=diameter*.5f+.015f;
            director.GroupMaterials=palette;
            if(_floor)
            {
                MakeBlock(root.transform,"Test Floor",new Vector3(0,-.2f,0),new Vector3(30,.4f,30),palette[6],true);
                MakeBlock(root.transform,"Obstacle A",new Vector3(-6,.8f,3),new Vector3(2,1.6f,3),palette[6],true);
                MakeBlock(root.transform,"Obstacle B",new Vector3(6,.8f,-3),new Vector3(2,1.6f,3),palette[6],true);
            }
            Physics.SyncTransforms(); // Editor setup only: newly created floor colliders must be queryable.
            GameObject points=new GameObject("Activity Points");Undo.RegisterCreatedObjectUndo(points,"Create points");points.transform.SetParent(root.transform,false);
            director.Points=new CCBuilderPoint[8];
            for(int i=0;i<8;i++)
            {
                float angle=i*Mathf.PI*2f/8;
                Vector3 position=_origin+new Vector3(Mathf.Sin(angle)*10f,0,Mathf.Cos(angle)*10f);
                GameObject go=new GameObject("Point "+(i+1));Undo.RegisterCreatedObjectUndo(go,"Create point");go.transform.SetParent(points.transform,false);go.transform.position=Ground(position);
                CCBuilderPoint point=Undo.AddComponent<CCBuilderPoint>(go);
                point.Kind=i<4?CCBuilderPlace.Work:i<6?CCBuilderPlace.Meeting:CCBuilderPlace.Rest;
                point.GroupRadius=Mathf.Max(1.1f,diameter*2.3f);director.Points[i]=point;
            }
            director.Schedule=new[] {
                new CCBuilderStep { Name="Работа",Place=CCBuilderPlace.Work,Activity=CCBuilderActivity.Working,Duration=new Vector2(12,18) },
                new CCBuilderStep { Name="Разговор",Place=CCBuilderPlace.Meeting,Activity=CCBuilderActivity.Chatting,Duration=new Vector2(5,9) },
                new CCBuilderStep { Name="Другая работа",Place=CCBuilderPlace.Work,Activity=CCBuilderActivity.Working,Duration=new Vector2(10,16) },
                new CCBuilderStep { Name="Общие прыжки",Place=CCBuilderPlace.Meeting,Activity=CCBuilderActivity.Hopping,Duration=new Vector2(4,6) },
                new CCBuilderStep { Name="Перерыв",Place=CCBuilderPlace.Rest,Activity=CCBuilderActivity.Resting,Duration=new Vector2(5,9) }
            };
            GameObject actors=new GameObject("Cats");Undo.RegisterCreatedObjectUndo(actors,"Create cats");actors.transform.SetParent(root.transform,false);
            int columns=Mathf.CeilToInt(Mathf.Sqrt(_count));
            for(int i=0;i<_count;i++)
            {
                GameObject cat=new GameObject("Builder Cat "+(i+1).ToString("00"));Undo.RegisterCreatedObjectUndo(cat,"Create cat");cat.transform.SetParent(actors.transform,false);
                cat.transform.position=Ground(_origin+new Vector3((i%columns-(columns-1)*.5f)*1.1f,0,(i/columns-(_count/columns-1)*.5f)*1.1f));
                Undo.AddComponent<NetworkObject>(cat);
                NavMeshAgent agent=Undo.AddComponent<NavMeshAgent>(cat);agent.enabled=false;
                agent.height=height;agent.radius=diameter*.5f;agent.baseOffset=0;agent.speed=1.35f;
                agent.acceleration=5f;agent.angularSpeed=360f;agent.stoppingDistance=.18f;
                agent.obstacleAvoidanceType=ObstacleAvoidanceType.MedQualityObstacleAvoidance;
                agent.avoidancePriority=30+i%40;agent.autoRepath=false;agent.autoTraverseOffMeshLink=false;
                CCBuilderCat behaviour=Undo.AddComponent<CCBuilderCat>(cat);behaviour.CatId=i;behaviour.Director=director;
                GameObject visual=new GameObject("Visual");Undo.RegisterCreatedObjectUndo(visual,"Create visual");visual.transform.SetParent(cat.transform,false);behaviour.Visual=visual.transform;
                GameObject body=GameObject.CreatePrimitive(PrimitiveType.Capsule);Undo.RegisterCreatedObjectUndo(body,"Create capsule");body.name="Capsule Body";body.transform.SetParent(visual.transform,false);
                body.transform.localPosition=new Vector3(0,height*.5f,0);body.transform.localScale=new Vector3(diameter,height*.5f,diameter);
                Undo.DestroyObjectImmediate(body.GetComponent<Collider>());
                Renderer renderer=body.GetComponent<Renderer>();renderer.sharedMaterial=palette[i%6];renderer.shadowCastingMode=ShadowCastingMode.Off;behaviour.BodyRenderer=renderer;
                MakeBlock(visual.transform,"Facing Marker",new Vector3(0,height*.72f,diameter*.48f),new Vector3(diameter*.45f,height*.10f,.025f),palette[7],false);
                Socket(visual.transform,"RightHandSocket",new Vector3(diameter*.65f,height*.55f,.08f));
                Socket(visual.transform,"CarrySocket",new Vector3(0,height*.45f,diameter*.8f));
            }
            // Only the six group colors, not floor/marker materials.
            director.GroupMaterials=new[]{palette[0],palette[1],palette[2],palette[3],palette[4],palette[5]};
            Selection.activeGameObject=root;EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("[Коты] Создано "+_count+" котов. Размести точки на земле, затем Tools/CatCooks/Builder Cats/2 - Bake selected colony. Сохрани сцену и запусти Host.",root);
        }
        private static Vector3 Ground(Vector3 p)
        {
            return Physics.Raycast(p+Vector3.up*3f,Vector3.down,out RaycastHit hit,8f,~0,QueryTriggerInteraction.Ignore)?hit.point:p;
        }
        private static void Socket(Transform parent,string name,Vector3 local)
        {
            GameObject go=new GameObject(name);Undo.RegisterCreatedObjectUndo(go,"Create socket");go.transform.SetParent(parent,false);go.transform.localPosition=local;
        }
        private static GameObject MakeBlock(Transform parent,string name,Vector3 pos,Vector3 scale,Material material,bool collision)
        {
            GameObject go=GameObject.CreatePrimitive(PrimitiveType.Cube);Undo.RegisterCreatedObjectUndo(go,"Create block");go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=pos;go.transform.localScale=scale;
            if(!collision)Undo.DestroyObjectImmediate(go.GetComponent<Collider>());
            Renderer r=go.GetComponent<Renderer>();r.sharedMaterial=material;r.shadowCastingMode=collision?ShadowCastingMode.On:ShadowCastingMode.Off;return go;
        }
        private static Material[] CreateMaterials()
        {
            if(!EnsureGeneratedFolder())return null;
            const string dir=GeneratedFolder;
            Color[] colors={new Color(.97f,.65f,.2f),new Color(.28f,.72f,.9f),new Color(.82f,.4f,.55f),new Color(.42f,.8f,.45f),new Color(.69f,.53f,.9f),new Color(.9f,.86f,.45f),new Color(.34f,.38f,.4f),Color.white};
            Shader shader=Shader.Find(GraphicsSettings.currentRenderPipeline==null?"Standard":"Universal Render Pipeline/Lit");
            if(shader==null)shader=Shader.Find("Standard");
            if(shader==null) { Debug.LogError("[Коты] Не найден шейдер для материалов.");return null; }
            Material[] result=new Material[colors.Length];
            for(int i=0;i<colors.Length;i++)
            {
                string path=dir+"/BuilderColor"+i+".mat";Material m=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(m==null && shader!=null)
                {
                    m=new Material(shader) { color=colors[i],enableInstancing=true };
                    if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.22f);
                    // Do not replace an unrelated asset with the same name.
                    if(AssetDatabase.LoadMainAssetAtPath(path)!=null)
                    {
                        Object.DestroyImmediate(m);
                        Debug.LogError("[Коты] Путь уже занят другим ассетом: "+path);
                        return null;
                    }
                    try { AssetDatabase.CreateAsset(m,path); }
                    catch
                    {
                        if(!EditorUtility.IsPersistent(m))Object.DestroyImmediate(m);
                        throw;
                    }
                }
                result[i]=m;
            }
            AssetDatabase.SaveAssets();
            return result;
        }
        private static bool EnsureGeneratedFolder()
        {
            string[] parts=GeneratedFolder.Split('/');
            string parent=parts[0]; // Unity asset paths are relative to the project, starting with Assets.
            if(!AssetDatabase.IsValidFolder(parent))
            { Debug.LogError("[Коты] Не найдена папка Assets текущего проекта.");return false; }
            for(int i=1;i<parts.Length;i++)
            {
                string child=parent+"/"+parts[i];
                if(!AssetDatabase.IsValidFolder(child))
                {
                    if(AssetDatabase.LoadMainAssetAtPath(child)!=null)
                    { Debug.LogError("[Коты] Вместо папки найден ассет: "+child);return false; }
                    string guid=AssetDatabase.CreateFolder(parent,parts[i]);
                    if(string.IsNullOrEmpty(guid) || !AssetDatabase.IsValidFolder(child))
                    { Debug.LogError("[Коты] Не удалось создать папку: "+child+". Проверь доступ к папке проекта.");return false; }
                }
                parent=child;
            }
            return true;
        }
        [MenuItem("Tools/CatCooks/Builder Cats/2 - Bake selected colony")]
        private static void BakeSelected()
        {
            if(Application.isPlaying)return;
            CCBuilderNavArea area=Selection.activeGameObject!=null?Selection.activeGameObject.GetComponentInParent<CCBuilderNavArea>():null;
            if(area==null) { Debug.LogError("[Коты] Выдели корень Builder Cats - Step 1.");return; }
            if(!EnsureGeneratedFolder())return;
            Bounds bounds=area.WorldBounds;
            if(bounds.size.x<1 || bounds.size.y<1 || bounds.size.z<1) { Debug.LogError("[Коты] Слишком маленький Size области.",area);return; }
            NavMeshBuildSettings settings=NavMesh.GetSettingsByID(area.AgentTypeId);
            if(area.AgentTypeId==0 || settings.agentTypeID!=area.AgentTypeId)settings=NavMesh.CreateSettings();
            settings.agentRadius=area.AgentRadius;settings.agentHeight=area.AgentHeight;
            settings.agentClimb=Mathf.Min(.22f,area.AgentHeight*.25f);settings.agentSlope=42f;
            settings.overrideVoxelSize=true;settings.voxelSize=Mathf.Max(.04f,area.AgentRadius/3f);
            List<NavMeshBuildSource> sources=new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds,area.GeometryMask,NavMeshCollectGeometry.PhysicsColliders,0,new List<NavMeshBuildMarkup>(),sources);
            sources.RemoveAll(source=>
            {
                Component c=source.component;
                if(c==null)return false;
                if(c.gameObject.scene!=area.gameObject.scene)return true;
                if(area.GeometryRoot!=null && !c.transform.IsChildOf(area.GeometryRoot))return true;
                Collider collider=c as Collider;
                return c.GetComponentInParent<CCBuilderCat>()!=null || collider is CharacterController ||
                    (collider!=null && (collider.isTrigger || collider.attachedRigidbody!=null));
            });
            if(sources.Count==0) { Debug.LogError("[Коты] В области Bake нет подходящих статичных коллайдеров. Проверь Geometry Mask/Root и Size.",area);return; }
            NavMeshData data=NavMeshBuilder.BuildNavMeshData(settings,sources,bounds,Vector3.zero,Quaternion.identity);
            if(data==null) { Debug.LogError("[Коты] Bake не создал NavMesh.",area);return; }
            const string dir=GeneratedFolder;
            string path=AssetDatabase.GenerateUniqueAssetPath(dir+"/BuilderNavMesh.asset");
            try { AssetDatabase.CreateAsset(data,path); }
            catch (System.Exception exception)
            {
                if(!EditorUtility.IsPersistent(data))Object.DestroyImmediate(data);
                Debug.LogError("[Коты] Не удалось сохранить NavMesh: "+path+". "+exception.Message,area);
                return;
            }
            Undo.RecordObject(area,"Assign cat NavMesh");area.Data=data;area.AgentTypeId=settings.agentTypeID;
            NavMeshDataInstance preview=NavMesh.AddNavMeshData(data);
            int badCats=0,badSlots=0;
            try
            {
                NavMeshQueryFilter filter=new NavMeshQueryFilter { agentTypeID=settings.agentTypeID,areaMask=NavMesh.AllAreas };
                CCBuilderCat[] cats=area.GetComponentsInChildren<CCBuilderCat>(true);
                foreach(CCBuilderCat cat in cats)
                {
                    NavMeshAgent agent=cat.GetComponent<NavMeshAgent>();Undo.RecordObject(agent,"Set cat agent type");agent.enabled=false;agent.agentTypeID=settings.agentTypeID;
                    agent.height=area.AgentHeight;agent.radius=Mathf.Max(.1f,area.AgentRadius-.015f);
                    if(NavMesh.SamplePosition(cat.transform.position,out NavMeshHit hit,1.5f,filter) && Mathf.Abs(hit.position.y-cat.transform.position.y)<1f)
                    { Undo.RecordObject(cat.transform,"Place cat on NavMesh");cat.transform.position=hit.position; }
                    else { badCats++;Debug.LogWarning("[Коты] Капсула вне NavMesh: "+cat.name,cat); }
                    EditorUtility.SetDirty(agent);
                }
                CCBuilderDirector director=area.GetComponent<CCBuilderDirector>();
                NavMeshPath testPath=new NavMeshPath();
                if(director!=null && director.Points!=null)
                    foreach(CCBuilderPoint point in director.Points)
                    {
                        if(point==null)continue;
                        for(int groupSize=3;groupSize<=4;groupSize++)
                        for(int slot=0;slot<groupSize;slot++)
                        {
                            Vector3 target=point.Slot(slot,groupSize);
                            bool ok=NavMesh.SamplePosition(target,out NavMeshHit hit,.65f,filter) && Mathf.Abs(hit.position.y-target.y)<.65f;
                            if(ok && cats.Length>0)ok=NavMesh.CalculatePath(cats[0].transform.position,hit.position,filter,testPath) && testPath.status==NavMeshPathStatus.PathComplete;
                            if(!ok) { badSlots++;Debug.LogWarning("[Коты] Проверь подход: "+point.name+", группа "+groupSize+", место "+slot,point); }
                        }
                    }
            }
            finally { if(preview.valid)preview.Remove(); }
            EditorUtility.SetDirty(area);EditorSceneManager.MarkSceneDirty(area.gameObject.scene);AssetDatabase.SaveAssets();
            Debug.Log("[Коты] Bake готов. Проблемных капсул: "+badCats+", мест у точек: "+badSlots+". Сохрани сцену. Agent Type ID: "+settings.agentTypeID,area);
        }
    }
    [CustomEditor(typeof(CCBuilderDirector))]
    public sealed class CCBuilderDirectorInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();CCBuilderDirector d=(CCBuilderDirector)target;
            if(Application.isPlaying)EditorGUILayout.HelpBox("Сервер: котов "+d.ServerCatCount+", групп "+d.ServerGroupCount+", вне NavMesh "+d.NavigationFailures,MessageType.Info);
        }
    }
}
