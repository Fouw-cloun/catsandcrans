using System.Collections.Generic;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace CatCooks.Builders.Editor
{
    public static class CCBuilderWorksiteSetup
    {
        [MenuItem("Tools/CatCooks/Builder Cats/3 - Add worksite to selected colony")]
        private static void AddWorksite()
        {
            if(Application.isPlaying) { Debug.LogWarning("[Коты] Выйди из Play перед изменением сцены.");return; }
            CCBuilderDirector director=Selection.activeGameObject!=null ? Selection.activeGameObject.GetComponentInParent<CCBuilderDirector>() : null;
            if(director==null || PrefabStageUtility.GetCurrentPrefabStage()!=null)
            { Debug.LogError("[Коты] Выдели корень существующей колонии в сцене.");return; }
            Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Upgrade builder worksite");
            CCBuilderWorksite site=director.GetComponent<CCBuilderWorksite>();
            bool fresh=site==null;
            if(fresh) site=Undo.AddComponent<CCBuilderWorksite>(director.gameObject);
            Undo.RecordObject(director,"Enable builder schedule");director.RunSchedule=true;
            foreach(CCBuilderCat cat in director.GetComponentsInChildren<CCBuilderCat>(true)) UpgradeCat(cat);
            if(fresh)
            {
                Transform props=Empty("Worksite Props",director.transform,Vector3.zero);
                Transform stations=Empty("Workstations",props,Vector3.zero);
                CreateStation(stations,"01 Dig pit",CCBuilderJob.Dig,CCBuilderItemKind.Shovel,new Vector3(-6,0,-6));
                CreateStation(stations,"02 Hammer bench",CCBuilderJob.Hammer,CCBuilderItemKind.Hammer,new Vector3(6,0,6));
                CreateStation(stations,"03 Brick delivery",CCBuilderJob.Deliver,CCBuilderItemKind.Brick,new Vector3(-6,0,6));
                CreateStation(stations,"04 Plank delivery",CCBuilderJob.Deliver,CCBuilderItemKind.Plank,new Vector3(6,0,-6));
                CreateStation(stations,"05 Frame delivery",CCBuilderJob.Deliver,CCBuilderItemKind.WindowFrame,new Vector3(0,0,8));
                Transform stock=Empty("Loose tools and materials",props,Vector3.zero);
                Material[] colors=director.GroupMaterials;
                int itemIndex=0;
                foreach(CCBuilderItemKind kind in System.Enum.GetValues(typeof(CCBuilderItemKind)))
                {
                    int count=kind==CCBuilderItemKind.Shovel || kind==CCBuilderItemKind.Hammer ? 4 : 6;
                    for(int i=0;i<count;i++)
                    {
                        Vector3 p=new Vector3((itemIndex%9-4)*.72f,0,-3.6f-(itemIndex/9)*.85f);
                        Material material=colors!=null && colors.Length>0 ? colors[(int)kind%colors.Length] : null;
                        CreateItem(stock,kind,p,material);itemIndex++;
                    }
                }
            }
            Refresh(site);Selection.activeGameObject=site.gameObject;
            EditorSceneManager.MarkSceneDirty(site.gameObject.scene);Undo.CollapseUndoOperations(undo);
            Debug.Log("[Коты] Рабочая система готова: "+site.Stations.Length+" мест, "+site.Items.Length+
                " предметов. Передвинь точки под свою карту, Bake (меню 2), проверь подходы (меню 6), СОХРАНИ сцену. На префаб игрока добавь CCBuilderCatCarrier (меню 4).",site);
        }
        private static void UpgradeCat(CCBuilderCat cat)
        {
            Undo.RecordObject(cat,"Set cat sockets");
            NavMeshAgent agent=cat.GetComponent<NavMeshAgent>();float height=agent!=null ? agent.height : .9f;
            Transform visual=cat.Visual!=null ? cat.Visual : cat.transform;
            if(cat.RightHandSocket==null) cat.RightHandSocket=FindOrCreate(visual,"RightHandSocket",new Vector3(height*.31f,height*.55f,.08f));
            if(cat.CarrySocket==null) cat.CarrySocket=FindOrCreate(visual,"CarrySocket",new Vector3(0,height*.45f,height*.38f));
            Transform hit=cat.transform.Find("Cat Pickup Trigger");
            if(hit==null) hit=Empty("Cat Pickup Trigger",cat.transform,Vector3.zero);
            CapsuleCollider collider=hit.GetComponent<CapsuleCollider>();
            if(collider==null) collider=Undo.AddComponent<CapsuleCollider>(hit.gameObject);
            Undo.RecordObject(collider,"Configure cat hit area");
            collider.isTrigger=true;collider.center=Vector3.up*height*.5f;collider.height=height;collider.radius=height*.24f;
            EditorUtility.SetDirty(cat);
        }
        [MenuItem("Tools/CatCooks/Builder Cats/4 - Add cat carrying to selected player")]
        private static void AddCarrier()
        {
            if(Application.isPlaying) return;
            GameObject selected=Selection.activeGameObject;
            if(selected==null) { Debug.LogError("[Коты] Открой префаб игрока и выдели корень с PlayerInteractor.");return; }
            if(EditorUtility.IsPersistent(selected))
            { Debug.LogError("[Коты] Дважды кликни префаб, чтобы открыть Prefab Mode; затем выдели корень игрока.");return; }
            PlayerInteractor interactor=selected.GetComponentInParent<PlayerInteractor>();
            if(interactor==null || interactor.GetComponent<PlayerHand>()==null || interactor.GetComponent<NetworkObject>()==null)
            { Debug.LogError("[Коты] Нужен объект с PlayerInteractor, PlayerHand и NetworkObject.");return; }
            CCBuilderCatCarrier carrier=interactor.GetComponent<CCBuilderCatCarrier>();
            if(carrier==null) carrier=Undo.AddComponent<CCBuilderCatCarrier>(interactor.gameObject);
            CarryGrabber grabber=interactor.GetComponent<CarryGrabber>();
            if(grabber!=null)
            {
                Undo.RecordObject(carrier,"Block cookware grab input while carrying cat");
                List<MonoBehaviour> blocked=new List<MonoBehaviour>(carrier.AdditionalBlockedBehaviours ?? new MonoBehaviour[0]);
                if(!blocked.Contains(grabber)) blocked.Add(grabber);
                carrier.AdditionalBlockedBehaviours=blocked.ToArray();EditorUtility.SetDirty(carrier);
            }
            EditorUtility.SetDirty(interactor.gameObject);EditorSceneManager.MarkSceneDirty(interactor.gameObject.scene);
            Debug.Log("[Коты] Добавлен перенос по B. Сохрани префаб игрока; если менял экземпляр в сцене, примени нужный override к префабу.",interactor);
        }
        [MenuItem("Tools/CatCooks/Builder Cats/5 - Refresh selected worksite lists")]
        private static void RefreshSelected()
        {
            if(Application.isPlaying) return;
            CCBuilderWorksite site=Selection.activeGameObject!=null ? Selection.activeGameObject.GetComponentInParent<CCBuilderWorksite>() : null;
            if(site==null) { Debug.LogError("[Коты] Выдели колонию с CCBuilderWorksite.");return; }
            Refresh(site);EditorSceneManager.MarkSceneDirty(site.gameObject.scene);
        }
        private static void Refresh(CCBuilderWorksite site)
        {
            Undo.RecordObject(site,"Refresh worksite references");
            // Preserve explicitly assigned external points/items; append children without duplicates.
            List<CCBuilderWorkstation> stations=new List<CCBuilderWorkstation>();
            List<CCBuilderWorkItem> items=new List<CCBuilderWorkItem>();
            if(site.Stations!=null) foreach(CCBuilderWorkstation s in site.Stations) if(s!=null && !stations.Contains(s)) stations.Add(s);
            if(site.Items!=null) foreach(CCBuilderWorkItem i in site.Items) if(i!=null && !items.Contains(i)) items.Add(i);
            foreach(CCBuilderWorkstation s in site.GetComponentsInChildren<CCBuilderWorkstation>(true)) if(!stations.Contains(s)) stations.Add(s);
            foreach(CCBuilderWorkItem i in site.GetComponentsInChildren<CCBuilderWorkItem>(true)) if(!items.Contains(i)) items.Add(i);
            site.Stations=stations.ToArray();site.Items=items.ToArray();EditorUtility.SetDirty(site);
        }
        private static Transform Empty(string name,Transform parent,Vector3 local)
        {
            GameObject go=new GameObject(name);Undo.RegisterCreatedObjectUndo(go,"Create worksite object");
            go.transform.SetParent(parent,false);go.transform.localPosition=local;return go.transform;
        }
        private static Transform FindOrCreate(Transform parent,string name,Vector3 local)
        { Transform existing=parent.Find(name);return existing!=null ? existing : Empty(name,parent,local); }
        private static Vector3 Ground(Vector3 p)
        { return Physics.Raycast(p+Vector3.up*.7f,Vector3.down,out RaycastHit hit,2,~0,QueryTriggerInteraction.Ignore) ? hit.point : p; }
        private static void CreateStation(Transform parent,string name,CCBuilderJob job,CCBuilderItemKind kind,Vector3 local)
        {
            Transform root=Empty(name,parent,local);root.position=Ground(root.position);
            CCBuilderWorkstation station=Undo.AddComponent<CCBuilderWorkstation>(root.gameObject);station.Job=job;station.MaterialKind=kind;
            station.LookAtPoint=Empty("Look At",root,new Vector3(0,.3f,0));
            station.DeliveryPoint=Empty("Delivery Ground",root,Vector3.zero);
            Empty("Scenery ModelSlot",root,Vector3.zero);
            station.WorkSlots=new Transform[4];
            for(int i=0;i<4;i++)
            {
                float a=i*Mathf.PI*.5f;
                Transform slot=Empty("Stand "+(i+1),root,new Vector3(Mathf.Sin(a),0,Mathf.Cos(a))*1.7f);
                slot.position=Ground(slot.position);station.WorkSlots[i]=slot;
            }
        }
        private static void CreateItem(Transform parent,CCBuilderItemKind kind,Vector3 local,Material material)
        {
            Transform root=Empty(kind.ToString(),parent,local);root.position=Ground(root.position);
            Undo.AddComponent<NetworkObject>(root.gameObject);
            CCBuilderWorkItem item=Undo.AddComponent<CCBuilderWorkItem>(root.gameObject);item.Kind=kind;
            item.ModelSlot=Empty("ModelSlot",root,Vector3.zero);
            Transform placeholder=Empty("Placeholder - replace with your model",item.ModelSlot,Vector3.zero);
            if(kind==CCBuilderItemKind.Shovel)
            {
                Block(placeholder,"Handle",new Vector3(0,.055f,0),new Vector3(.045f,.055f,.6f),material);
                Block(placeholder,"Blade",new Vector3(0,.045f,.35f),new Vector3(.22f,.06f,.22f),material);
                item.HeldEuler=new Vector3(-70,0,0);
            }
            else if(kind==CCBuilderItemKind.Hammer)
            {
                Block(placeholder,"Handle",new Vector3(0,.05f,0),new Vector3(.045f,.05f,.32f),material);
                Block(placeholder,"Head",new Vector3(0,.075f,.17f),new Vector3(.2f,.12f,.1f),material);
                item.HeldEuler=new Vector3(-75,0,0);
            }
            else if(kind==CCBuilderItemKind.Brick) Block(placeholder,"Brick",new Vector3(0,.065f,0),new Vector3(.28f,.13f,.16f),material);
            else if(kind==CCBuilderItemKind.Plank) Block(placeholder,"Plank",new Vector3(0,.04f,0),new Vector3(.6f,.08f,.17f),material);
            else
            {
                Block(placeholder,"Left",new Vector3(-.22f,.035f,0),new Vector3(.05f,.07f,.65f),material);
                Block(placeholder,"Right",new Vector3(.22f,.035f,0),new Vector3(.05f,.07f,.65f),material);
                Block(placeholder,"Top",new Vector3(0,.035f,.3f),new Vector3(.49f,.07f,.05f),material);
                Block(placeholder,"Bottom",new Vector3(0,.035f,-.3f),new Vector3(.49f,.07f,.05f),material);
                item.HeldEuler=new Vector3(75,0,0);
            }
        }
        private static void Block(Transform parent,string name,Vector3 position,Vector3 scale,Material material)
        {
            GameObject go=GameObject.CreatePrimitive(PrimitiveType.Cube);Undo.RegisterCreatedObjectUndo(go,"Create placeholder");
            go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;
            Undo.DestroyObjectImmediate(go.GetComponent<Collider>());
            Renderer renderer=go.GetComponent<Renderer>();if(material!=null) renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
        }
        [MenuItem("Tools/CatCooks/Builder Cats/6 - Validate worksite navigation")]
        private static void Validate()
        {
            if(Application.isPlaying) return;
            CCBuilderWorksite site=Selection.activeGameObject!=null ? Selection.activeGameObject.GetComponentInParent<CCBuilderWorksite>() : null;
            CCBuilderNavArea area=site!=null ? site.GetComponent<CCBuilderNavArea>() : null;
            if(area==null || area.Data==null) { Debug.LogError("[Коты] Сначала выдели колонию и выполни Bake (меню 2).");return; }
            NavMeshDataInstance preview=NavMesh.AddNavMeshData(area.Data);int bad=0;
            try
            {
                NavMeshQueryFilter filter=new NavMeshQueryFilter { agentTypeID=area.AgentTypeId,areaMask=NavMesh.AllAreas };
                CCBuilderCat cat=site.GetComponentInChildren<CCBuilderCat>();NavMeshPath path=new NavMeshPath();
                if(cat==null) { Debug.LogError("[Коты] Нет котов.",site);return; }
                foreach(CCBuilderWorkstation station in site.Stations)
                {
                    if(station==null || !station.isActiveAndEnabled) continue;
                    for(int i=0;i<station.SlotCount;i++) if(!CanReach(cat.transform.position,station.SlotPosition(i),filter,path))
                    { bad++;Debug.LogWarning("[Коты] Недоступно рабочее место "+(i+1)+": "+station.name,station); }
                }
                foreach(CCBuilderWorkItem item in site.Items)
                    if(item!=null && item.isActiveAndEnabled && !CanReach(cat.transform.position,item.transform.position,filter,path))
                    { bad++;Debug.LogWarning("[Коты] Недоступен предмет: "+item.name,item); }
                Debug.Log("[Коты] Проверка от первого кота: проблемных подходов "+bad+". Это не заменяет тест Host + Client.",site);
            }
            finally { if(preview.valid) preview.Remove(); }
        }
        private static bool CanReach(Vector3 from,Vector3 to,NavMeshQueryFilter filter,NavMeshPath path)
        {
            return NavMesh.SamplePosition(from,out NavMeshHit start,.7f,filter) && Mathf.Abs(start.position.y-from.y)<.55f &&
                NavMesh.SamplePosition(to,out NavMeshHit end,.65f,filter) && Mathf.Abs(end.position.y-to.y)<.55f &&
                NavMesh.CalculatePath(start.position,end.position,filter,path) && path.status==NavMeshPathStatus.PathComplete;
        }
    }
    [CustomEditor(typeof(CCBuilderWorksite))]
    public sealed class CCBuilderWorksiteInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();CCBuilderWorksite site=(CCBuilderWorksite)target;
            if(Application.isPlaying)
                EditorGUILayout.HelpBox("Сервер: работают "+site.ActiveJobs+", ищут работу "+site.WaitingCats+", выполнено "+site.CompletedJobs,MessageType.Info);
        }
        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        private static void DrawLabels(CCBuilderCat cat,GizmoType gizmo)
        {
            if(!Application.isPlaying || !cat.IsServerInitialized || cat.Director==null) return;
            CCBuilderWorksite site=cat.Director.GetComponent<CCBuilderWorksite>();
            if(site!=null && site.ShowDebugLabels) Handles.Label(cat.transform.position+Vector3.up*1.2f,site.Status(cat));
        }
    }
}
