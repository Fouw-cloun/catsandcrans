using System.Collections.Generic;
using System.IO;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CatCooks.Builders.Editor
{
    public static class CCBuilderStep3Setup
    {
        private const string Generated="Assets/Scripts/ChaosSyst/CatMovemnet/Generated/Step3";
        private static Material _brick,_wood,_sand,_frame;
        private static string _models;
        private static bool Begin()
        {
            if(Application.isPlaying || PrefabStageUtility.GetCurrentPrefabStage()!=null)
            { Debug.LogError("[Step3] Выйди из Play/Prefab Mode. Настройка выполняется в игровой сцене.");return false; }
            string[] ids=AssetDatabase.FindAssets("CCBuilderStep3Setup t:MonoScript");_models=null;
            foreach(string id in ids)
            {
                string script=AssetDatabase.GUIDToAssetPath(id);
                if(Path.GetFileName(script)=="CCBuilderStep3Setup.cs") { _models=Path.GetDirectoryName(Path.GetDirectoryName(script)).Replace('\\','/')+"/Models/";break; }
            }
            if(_models==null || AssetDatabase.LoadAssetAtPath<GameObject>(_models+"CC_Brick.obj")==null)
            { Debug.LogError("[Step3] Дождись импорта Models/CC_Brick.obj. Папка Models должна лежать рядом с Editor и Runtime.");return false; }
            Folder(Generated);
            _brick=Material("Brick",new Color(.65f,.25f,.15f));_wood=Material("Wood",new Color(.57f,.36f,.16f));
            _sand=Material("Sand",new Color(.75f,.57f,.3f));_frame=Material("Frame",new Color(.83f,.7f,.49f));
            return true;
        }
        private static void Folder(string path)
        {
            if(AssetDatabase.IsValidFolder(path)) return;
            string parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        private static Material Material(string name,Color color)
        {
            string path=Generated+"/"+name+".mat";Material m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m!=null) return m;
            Shader shader=Shader.Find("Universal Render Pipeline/Lit");if(shader==null) shader=Shader.Find("Standard");
            if(shader==null) throw new System.InvalidOperationException("Не найден URP/Lit или Standard shader.");
            m=new Material(shader) { name=name,color=color,enableInstancing=true };AssetDatabase.CreateAsset(m,path);return m;
        }
        private static Transform Empty(string name,Transform parent,Vector3 local)
        {
            GameObject go=new GameObject(name);Undo.RegisterCreatedObjectUndo(go,"Step3 setup");
            go.transform.SetParent(parent,false);go.transform.localPosition=local;return go.transform;
        }
        private static Transform Model(string name,Transform parent,Material material)
        {
            GameObject asset=AssetDatabase.LoadAssetAtPath<GameObject>(_models+name+".obj");
            if(asset==null) throw new System.InvalidOperationException("Не импортирована модель "+name);
            GameObject go=(GameObject)PrefabUtility.InstantiatePrefab(asset,parent);Undo.RegisterCreatedObjectUndo(go,"Step3 model");
            go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.identity;
            foreach(Renderer renderer in go.GetComponentsInChildren<Renderer>()) { renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off; }
            return go.transform;
        }
        private static Vector3 Ground(Vector3 p,Transform ignore=null)
        {
            RaycastHit[] hits=Physics.RaycastAll(p+Vector3.up*2,Vector3.down,8,~0,QueryTriggerInteraction.Ignore);
            float best=float.MaxValue;Vector3 result=p;
            foreach(RaycastHit hit in hits)
            {
                if(ignore!=null && hit.transform.IsChildOf(ignore)) continue;
                float distance=Mathf.Abs(hit.point.y-p.y);
                if(Vector3.Dot(hit.normal,Vector3.up)>.8f && distance<best) { best=distance;result=hit.point; }
            }
            return result;
        }
        private static void Finish(Object selected)
        {
            EditorUtility.SetDirty(selected);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeObject=selected;
        }
        [MenuItem("Tools/CatCooks/Builder Cats/7 - Add material loops and sand")]
        private static void Loops()
        {
            CCBuilderWorksite site=Selection.activeGameObject!=null?Selection.activeGameObject.GetComponentInParent<CCBuilderWorksite>():null;
            if(site==null) { Debug.LogError("[Step3] Выдели колонию с CCBuilderWorksite (сначала меню 3).");return; }
            if(site.transform.Find("Step3 Construction Loops")!=null) { Debug.Log("[Step3] Комплект уже существует.",site);return; }
            if(!Begin()) return;
            Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Step3 worksite");
            Undo.RecordObject(site,"Step3 tasks");
            if(site.Stations!=null) foreach(CCBuilderWorkstation s in site.Stations)
                if(s!=null && s.Job!=CCBuilderJob.Hammer) { Undo.RecordObject(s,"Disable old task");s.enabled=false; }
            if(site.Items!=null) foreach(CCBuilderWorkItem i in site.Items)
                if(i!=null && !i.IsTool && i.Route==null) { Undo.RecordObject(i.gameObject,"Hide old placeholder");i.gameObject.SetActive(false); }
            Transform root=Empty("Step3 Construction Loops",site.transform,Vector3.zero);
            Route(root,"Bricks A-B",CCBuilderItemKind.Brick,4,6);
            Route(root,"Planks A-B",CCBuilderItemKind.Plank,0,4);
            Route(root,"Frames A-B",CCBuilderItemKind.WindowFrame,-4,3);
            CCBuilderSandPile source=Pile("Shovel Sand Source",root,new Vector3(-3,0,-8),120,null);
            CCBuilderSandPile target=Pile("Shovel Sand Target",root,new Vector3(1,0,-8),0,null);
            Transform dig=Empty("Shovel transfer station",root,new Vector3(-1,0,-5.7f));
            CCBuilderWorkstation station=Undo.AddComponent<CCBuilderWorkstation>(dig.gameObject);
            station.Job=CCBuilderJob.Dig;station.SandSource=source;station.SandTarget=target;station.LookAtPoint=source.transform;
            station.WorkSeconds=new Vector2(12,20);station.WorkSlots=new Transform[4];
            for(int i=0;i<4;i++) { station.WorkSlots[i]=Empty("Stand "+i,dig,new Vector3((i-1.5f)*.75f,0,0));station.WorkSlots[i].position=Ground(station.WorkSlots[i].position); }
            CCBuilderPoint meeting=Undo.AddComponent<CCBuilderPoint>(Empty("Evening Meeting",root,new Vector3(0,0,7)).gameObject);
            meeting.Kind=CCBuilderPlace.Meeting;meeting.transform.position=Ground(meeting.transform.position);
            CCBuilderPoint sleep=Undo.AddComponent<CCBuilderPoint>(Empty("Sleeping Area",root,new Vector3(0,0,12)).gameObject);
            sleep.Kind=CCBuilderPlace.Rest;sleep.transform.position=Ground(sleep.transform.position);
            site.EveningMeeting=meeting;site.SleepingArea=sleep;site.UseGameClock=true;
            Transform decor=Empty("Decor Brick Pile",root,new Vector3(-8,0,6));decor.position=Ground(decor.position);Model("CC_BrickPile",decor,_brick);
            decor=Empty("Decor Plank Pile",root,new Vector3(-8,0,1));decor.position=Ground(decor.position);Model("CC_PlankPile",decor,_wood);
            List<CCBuilderWorkstation> stations=new List<CCBuilderWorkstation>(site.Stations??new CCBuilderWorkstation[0]);
            foreach(CCBuilderWorkstation s in root.GetComponentsInChildren<CCBuilderWorkstation>()) if(!stations.Contains(s)) stations.Add(s);
            List<CCBuilderWorkItem> items=new List<CCBuilderWorkItem>(site.Items??new CCBuilderWorkItem[0]);
            foreach(CCBuilderWorkItem i in root.GetComponentsInChildren<CCBuilderWorkItem>()) if(!items.Contains(i)) items.Add(i);
            site.Stations=stations.ToArray();site.Items=items.ToArray();
            CCBuilderDirector director=site.GetComponent<CCBuilderDirector>();Undo.RecordObject(director,"Rest points");
            List<CCBuilderPoint> points=new List<CCBuilderPoint>(director.Points??new CCBuilderPoint[0]);points.Add(meeting);points.Add(sleep);director.Points=points.ToArray();
            Undo.CollapseUndoOperations(undo);Finish(site);
            Debug.Log("[Step3] Расставь A/B, Stand, кучи и точки отдыха на полу. Затем меню 2 Bake и 6 Validate. Сохрани сцену.",site);
        }
        private static void Route(Transform root,string name,CCBuilderItemKind kind,float z,int count)
        {
            Transform group=Empty(name,root,new Vector3(0,0,z));
            CCBuilderMaterialRoute route=Undo.AddComponent<CCBuilderMaterialRoute>(group.gameObject);
            route.PointA=Empty("Point A",group,new Vector3(-6,0,0));route.PointB=Empty("Point B",group,new Vector3(6,0,0));
            route.PointA.position=Ground(route.PointA.position);route.PointB.position=Ground(route.PointB.position);
            route.StackSpacing=kind==CCBuilderItemKind.Plank?1.4f:.55f;
            for(int end=0;end<2;end++)
            {
                Transform endpoint=end==0?route.PointA:route.PointB;
                CCBuilderWorkstation station=Undo.AddComponent<CCBuilderWorkstation>(Empty("Delivery "+end,endpoint,Vector3.zero).gameObject);
                station.Job=CCBuilderJob.Deliver;station.MaterialKind=kind;station.Route=route;station.RouteEnd=end;
                station.DeliveryPoint=endpoint;station.LookAtPoint=endpoint;station.WorkSeconds=new Vector2(.7f,1.3f);station.Capacity=3;
                station.WorkSlots=new Transform[3];
                for(int i=0;i<3;i++) { station.WorkSlots[i]=Empty("Stand "+i,station.transform,new Vector3((i-1)*.85f,0,-.75f));station.WorkSlots[i].position=Ground(station.WorkSlots[i].position); }
            }
            for(int n=0;n<count;n++)
            {
                Transform itemRoot=Empty(kind+" "+n,group,Vector3.zero);itemRoot.position=route.Location(0,n);
                Undo.AddComponent<NetworkObject>(itemRoot.gameObject);
                CCBuilderWorkItem item=Undo.AddComponent<CCBuilderWorkItem>(itemRoot.gameObject);item.Kind=kind;item.Route=route;item.RouteSlot=n;
                item.ModelSlot=Empty("ModelSlot",itemRoot,Vector3.zero);
                if(kind==CCBuilderItemKind.Brick)
                {
                    item.BundleCount=n%2==0?3:4;
                    CCBuilderBrickSpill spill=Undo.AddComponent<CCBuilderBrickSpill>(itemRoot.gameObject);spill.Bricks=new Transform[4];
                    for(int b=0;b<4;b++)
                    {
                        spill.Bricks[b]=Empty("Brick "+b,item.ModelSlot,new Vector3(0,b*.13f,0));
                        Model("CC_Brick",spill.Bricks[b],_brick);spill.Bricks[b].gameObject.SetActive(b<item.BundleCount);
                    }
                }
                else Model(kind==CCBuilderItemKind.Plank?"CC_Plank":"CC_WindowFrame",item.ModelSlot,kind==CCBuilderItemKind.Plank?_wood:_frame);
            }
        }
        private static CCBuilderSandPile Pile(string name,Transform parent,Vector3 local,float amount,Transform ignore)
        {
            Transform root=Empty(name,parent,local);root.position=Ground(root.position,ignore);
            Undo.AddComponent<NetworkObject>(root.gameObject);CCBuilderSandPile pile=Undo.AddComponent<CCBuilderSandPile>(root.gameObject);
            pile.InitialAmount=amount;pile.Visual=Empty("Visual",root,Vector3.zero);
            Model(amount>0?"CC_SandPile_1":"CC_SandPile_2",pile.Visual,_sand);return pile;
        }
        private static T One<T>() where T:Component
        {
            T result=null;
            foreach(T item in Object.FindObjectsByType<T>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(item.gameObject.scene==SceneManager.GetActiveScene())
                { if(result!=null) { Debug.LogError("[Step3] В активной сцене больше одного "+typeof(T).Name+". Назначь ссылки вручную.");return null; }result=item; }
            return result;
        }
        [MenuItem("Tools/CatCooks/Builder Cats/8 - Connect clock and daylight")]
        private static void Clock()
        {
            if(!Begin()) return;
            GameClock[] clocks=Object.FindObjectsByType<GameClock>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            GameClock clock=null;int count=0;
            foreach(GameClock c in clocks) if(c.gameObject.scene==SceneManager.GetActiveScene()) { clock=c;count++; }
            if(count>1) { Debug.LogError("[Step3] Оставь один GameClock.");return; }
            if(clock==null)
            {
                Transform root=Empty("GameClock",null,Vector3.zero);Undo.AddComponent<NetworkObject>(root.gameObject);clock=Undo.AddComponent<GameClock>(root.gameObject);
                Debug.LogWarning("[Step3] Создан GameClock. Если часы уже находятся в Bootstrap/DontDestroyOnLoad, удали дубликат из игровой сцены.",clock);
            }
            if(clock.GetComponent<CCBuilderClockPanel>()==null) Undo.AddComponent<CCBuilderClockPanel>(clock.gameObject);
            // Свет дня теперь ведёт CatCooksDayCycle (Assets/CatCooks/Atmosphere) по этому же GameClock.
            // Старый CCBuilderDaylight писал солнце сам и ставил погоду на паузу — удалён.
            Finish(clock);
            Debug.Log("[Step3] Часы подключены. F8 у Host. Время суток и погоду по часам ведёт CatCooks Atmosphere (Day Cycle).",clock);
        }
        [MenuItem("Tools/CatCooks/Builder Cats/9 - Add cat operator to selected excavator")]
        private static void Excavator()
        {
            ExcavatorVehicle vehicle=Selection.activeGameObject!=null?Selection.activeGameObject.GetComponentInParent<ExcavatorVehicle>():null;
            if(vehicle==null) { Debug.LogError("[Step3] Выдели экскаватор в сцене.");return; }
            if(vehicle.GetComponent<CCBuilderExcavatorOperator>()!=null) { Debug.Log("[Step3] Оператор уже добавлен.",vehicle);return; }
            if(!Begin()) return;
            CCBuilderWorksite site=One<CCBuilderWorksite>();if(site==null) { Debug.LogError("[Step3] Нужна одна колония в активной сцене.");return; }
            CCBuilderCat cat=site.GetComponentInChildren<CCBuilderCat>();
            if(cat==null || vehicle.NpcSeat==null || vehicle.BucketPart==null)
            { Debug.LogError("[Step3] Нужны кот, Seat Point и Bucket в исходном ExcavatorVehicle.");return; }
            CCBuilderExcavatorOperator op=Undo.AddComponent<CCBuilderExcavatorOperator>(vehicle.gameObject);op.Worksite=site;op.Cat=cat;
            Transform props=Empty("Excavator task - "+vehicle.name,site.transform,Vector3.zero);
            props.SetPositionAndRotation(vehicle.transform.position,Quaternion.Euler(0,vehicle.transform.eulerAngles.y,0));
            op.BoardPoint=Empty("Board Point",props,new Vector3(-3.6f,0,-2));op.BoardPoint.position=Ground(op.BoardPoint.position,vehicle.transform);
            op.CatSeat=Empty("Cat Seat",vehicle.NpcSeat,Vector3.up*.15f);
            op.BucketTip=Empty("Cat Bucket Tip - move to bucket lip",vehicle.BucketPart,Vector3.zero);
            op.ParkingA=Empty("Parking A",props,Vector3.zero);op.ParkingB=Empty("Parking B",props,new Vector3(0,0,-1.2f));
            Vector3 source=Ground(PreviewTip(vehicle,op.BucketTip,op.ScoopPose),vehicle.transform);
            Vector3 target=Ground(PreviewTip(vehicle,op.BucketTip,op.DumpPose),vehicle.transform);
            op.Source=Pile("Excavator Source",props,props.InverseTransformPoint(source),160,vehicle.transform);
            op.Target=Pile("Excavator Target",props,props.InverseTransformPoint(target),0,vehicle.transform);
            NavMeshObstacle obstacle=vehicle.GetComponent<NavMeshObstacle>();
            if(obstacle==null)
            {
                obstacle=Undo.AddComponent<NavMeshObstacle>(vehicle.gameObject);obstacle.shape=NavMeshObstacleShape.Box;
                obstacle.center=new Vector3(0,1,0);obstacle.size=new Vector3(5.4f,3,5.2f);obstacle.carving=true;
                obstacle.carveOnlyStationary=true;obstacle.carvingTimeToStationary=.7f;
            }
            Finish(op);
            Debug.LogWarning("[Step3] Обязательно подгони Cat Seat, край Bucket Tip и Scoop/Dump Pose. Стартовые углы примерные. Для проверки сначала выключи Allow Driving. Затем Bake и сохранение сцены.",op);
        }
        private static Vector3 PreviewTip(ExcavatorVehicle v,Transform tip,Vector4 pose)
        {
            Transform[] parts={v.TowerPart,v.BoomPart,v.ArmPart,v.BucketPart};Quaternion[] old=new Quaternion[4];
            for(int i=0;i<4;i++) if(parts[i]!=null) old[i]=parts[i].localRotation;
            pose=v.ClampNpcPose(pose);
            try
            {
                if(parts[0]!=null) parts[0].localRotation=old[0]*Quaternion.AngleAxis(pose.x,v.TowerAxis);
                if(parts[1]!=null) parts[1].localRotation=old[1]*Quaternion.Euler(pose.y,0,0);
                if(parts[2]!=null) parts[2].localRotation=old[2]*Quaternion.Euler(pose.z,0,0);
                if(parts[3]!=null) parts[3].localRotation=old[3]*Quaternion.Euler(pose.w,0,0);
                return tip.position;
            }
            finally { for(int i=0;i<4;i++) if(parts[i]!=null) parts[i].localRotation=old[i]; }
        }
    }
}
