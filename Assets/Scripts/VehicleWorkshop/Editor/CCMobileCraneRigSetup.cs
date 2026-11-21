#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CatCooks.Food;
using W = CCWorkshopEditor;

public static class CCMobileCraneRigSetup
{
    const string Path="Assets/VehicleWorkshop/Models/MobileCrane_Workshop.fbx";
    const string Menu="CatCooks/Vehicle Workshop/09 Собрать автокран на месте выбранного";
    static readonly string[] Tires={"fortire left","fortire right","tire.001","tire.002","tire.003","tire.004"};

    [MenuItem(Menu)]
    static void Install()
    {
        var pick=Selection.activeTransform;
        var old=pick ? pick.GetComponentInParent<MobileCraneChassis>(true) : null;
        if(EditorApplication.isPlaying || !old || EditorUtility.IsPersistent(old))
            throw new InvalidOperationException("Выйди из Play Mode и выбери в сцене автокран с Mobile Crane Chassis.");
        if(old is CCWorkshopCraneChassis || !old.gameObject.activeInHierarchy)
        {
            Debug.Log("Выбран уже собранный кран или отключённый экземпляр.",old); return;
        }
        var art=AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        if(!art) throw new InvalidOperationException("Дождись импорта "+Path);
        Undo.IncrementCurrentGroup(); int undo=Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Собрать автокран");
        try
        {
            Vector3 forward=Vector3.ProjectOnPlane(old.transform.forward,Vector3.up);
            Vector3 location=old.transform.position;
            var front=W.Find(old.transform,Tires[0]); var back=W.Find(old.transform,Tires[5]);
            if(front && back && front.TryGetComponent<Renderer>(out var fr) && back.TryGetComponent<Renderer>(out var br))
            {
                var otherFront=W.Find(old.transform,Tires[1]); var otherBack=W.Find(old.transform,Tires[4]);
                if(otherFront && otherBack && otherFront.TryGetComponent<Renderer>(out var fr2) && otherBack.TryGetComponent<Renderer>(out var br2))
                    forward=Vector3.ProjectOnPlane((fr.bounds.center+fr2.bounds.center-br.bounds.center-br2.bounds.center)*.5f,Vector3.up);
                if(forward.sqrMagnitude>.01f && otherFront && otherFront.TryGetComponent<Renderer>(out var frontRight))
                    location=(fr.bounds.center+frontRight.bounds.center)*.5f-forward.normalized*2.85f-Vector3.up*.65f;
            }
            GameObject root=new GameObject("Автокран Workshop");
            Undo.RegisterCreatedObjectUndo(root,"Автокран");
            if(root.scene!=old.gameObject.scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,old.gameObject.scene);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(art,root.scene);
            Undo.RegisterCreatedObjectUndo(model,"Модель автокрана");
            Undo.SetTransformParent(model.transform,root.transform,"Модель автокрана");
            PrefabUtility.UnpackPrefabInstance(model,PrefabUnpackMode.Completely,InteractionMode.UserAction);
            Assemble(root,model.transform);
            Undo.RecordObject(old.gameObject,"Отключить прежний автокран");
            old.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(old.gameObject);
            root.transform.SetPositionAndRotation(location,Quaternion.LookRotation(forward.sqrMagnitude>.001f?forward:Vector3.forward));
            Physics.SyncTransforms();
            // Probe the slab next to the chassis. Its own colliders are filtered out.
            var scene=root.scene.GetPhysicsScene();
            var rays=new RaycastHit[64];
            float maxY=float.NegativeInfinity;
            foreach(var w in root.GetComponent<CCWorkshopCraneChassis>().wheels)
            {
                var pt=w.visual.position;
                int n=scene.Raycast(pt+Vector3.up*.9f,Vector3.down,rays,2.7f,~0,QueryTriggerInteraction.Ignore);
                for(int i=0;i<n;i++)
                {
                    var h=rays[i];
                    if(h.transform.IsChildOf(root.transform)||h.normal.y<.85f||h.rigidbody) continue;
                    if(Mathf.Abs(h.point.y-location.y)>.95f) continue;
                    maxY=Mathf.Max(maxY,h.point.y);
                }
            }
            if(!float.IsNegativeInfinity(maxY))
                root.transform.position=new Vector3(location.x,maxY+.035f,location.z);
            Selection.activeGameObject=root;
            EditorSceneManager.MarkSceneDirty(root.scene);
            Undo.CollapseUndoOperations(undo);
            Debug.Log("Автокран собран. Прежний экземпляр отключён. Сохрани сцену.", root);
        }
        catch { Undo.RevertAllDownToGroup(undo); throw; }
    }

    static Transform Part(Transform root,string name) =>
        W.Find(root,name) ?? throw new InvalidOperationException("В комплектном FBX отсутствует "+name);
    static Transform Node(Transform parent,string name,Vector3 world)
    {
        var go=new GameObject(name); go.transform.SetParent(parent,false); go.transform.position=world;
        return go.transform;
    }
    static void Mount(Transform visual,Transform parent) => visual.SetParent(parent,true);
    static void Box(Transform parent,string name,Vector3 local,Vector3 size)
    {
        var t=Node(parent,name,parent.TransformPoint(local));
        t.gameObject.AddComponent<BoxCollider>().size=size;
    }
    static void Assemble(GameObject root,Transform model)
    {
        Transform r=root.transform;
        Vector3 Mark(string n)=>Part(model,"Rig_"+n).position;
        Vector3 up=Mark("Up")-Mark("Zero"), fwd=Mark("Forward")-Mark("Zero");
        if(up.magnitude<.0001f || fwd.magnitude<.0001f) throw new InvalidOperationException("Пустые осевые метки FBX.");
        model.localScale/=up.magnitude;
        model.rotation=Quaternion.Inverse(Quaternion.LookRotation(fwd,up))*model.rotation;
        model.position-=Mark("Zero");
        var hubs=Enumerable.Range(0,6).Select(i=>Mark("Hub"+i)).ToArray();
        if(Mathf.Abs(hubs[0].z-hubs[5].z-4.71f)>.10f) throw new InvalidOperationException("Масштаб осей FBX отличается от исходного.");
        var drive=W.Add<CCWorkshopCraneChassis>(root);
        var body=W.Body(drive,7200f);
        W.Set(drive,"_centerOfMass",new Vector3(0,.76f,.22f));
        body.angularDamping=2.4f;body.maxAngularVelocity=3f;
        var state=W.Add<CraneState>(root); var op=W.Add<CraneOperatorSeat>(root);
        drive.state=state;drive.operatorSeat=op;
        W.Set(drive,"_craneState",state);W.Set(op,"_craneState",state);
        W.Set(op,"_isStatic",true);W.Set(op,"_body",null);

        drive.restLength=.43f;drive.wheelRadius=.65f;drive.groundMask=~0;
        drive.springStrength=body.mass*9.81f/(6f*.14f);
        drive.springDamper=1.65f*Mathf.Sqrt(drive.springStrength*body.mass/6f);
        drive.motorForce=3700f;drive.maxSteerAngle=26f;drive.tireGrip=.60f;
        drive.engineBrakeDeceleration=3.4f; drive.visualSpinSpeed=.98f;
        drive.wheels=new WheeledVehicle.Wheel[6];
        for(int i=0;i<6;i++)
        {
            var visual=Node(r,"Центр колеса "+i,hubs[i]);
            Mount(Part(model,Tires[i]),visual);
            var anchor=Node(r,"Подвеска "+i,hubs[i]+Vector3.up*(drive.restLength-.14f));
            drive.wheels[i]=new WheeledVehicle.Wheel{anchor=anchor,visual=visual,steering=i<2,motorized=true,spinAxisLocal=Vector3.right,baseRotationCaptured=true,baseLocalRotation=Quaternion.identity};
            var bump=Node(r,"Ограничитель подвески "+i,hubs[i]);
            bump.gameObject.AddComponent<SphereCollider>().radius=.44f;
        }
        Box(r,"Рама",new Vector3(0,1.16f,.30f),new Vector3(2.50f,.68f,6.60f));
        var turn=Node(r,"Поворотная платформа",Mark("Turn"));
        foreach(string n in new[]{"cran cabin","cran door","SEAT1.002","joystick2","joystick2.001"}) Mount(Part(model,n),turn);
        var boom=Node(turn,"Шарнир стрелы",Mark("Lift")); Mount(Part(model,"crane boom 1"),boom);
        var sectionA=Node(boom,"Телескоп 1",boom.position);
        sectionA.rotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);
        Mount(Part(model,"crane boom2"),sectionA);
        var sectionB=Node(sectionA,"Телескоп 2",sectionA.position);
        Mount(Part(model,"crane boom3"),sectionB);Mount(Part(model,"crane block"),sectionB);
        W.Set(op,"_towerBase",turn);W.Set(op,"_towerAxis",Vector3.up);
        W.Set(op,"_boomBase",boom);W.Set(op,"_boomAxis",Vector3.left);W.Set(op,"_pitchLimit",new Vector2(0,67));
        var so=new SerializedObject(op);
        var sections=so.FindProperty("_sections");var ranges=so.FindProperty("_sectionYRange");
        sections.arraySize=ranges.arraySize=2;
        for(int i=0;i<2;i++){
            sections.GetArrayElementAtIndex(i).objectReferenceValue=i==0?sectionA:sectionB;
            ranges.GetArrayElementAtIndex(i).vector2Value=new Vector2(0,i==0?4.15f:4.30f);
        }
        so.ApplyModifiedProperties();
        drive.barrelPin=Node(turn,"Ось цилиндра",Mark("Barrel"));drive.rodPin=Node(boom,"Ось штока",Mark("Rod"));
        drive.barrel=Node(turn,"Цилиндр",drive.barrelPin.position);drive.rod=Node(boom,"Шток",drive.rodPin.position);
        drive.rod.rotation=Quaternion.LookRotation(drive.barrelPin.position-drive.rodPin.position,Vector3.up);
        Mount(Part(model,"boom base piston base"),drive.barrel);Mount(Part(model,"boom base piston tip"),drive.rod);
        drive.feet=new Transform[4];
        for(int i=0;i<4;i++)
        {
            drive.feet[i]=Node(r,"Пята "+(i+1),Mark("Foot"+i));
            Mount(Part(model,"support moving.00"+(i+1)),drive.feet[i]);
        }
        void Cabin(NetworkVehicle seat,Transform frame,string id,string seatName)
        {
            var eye=Mark(id+"Eye");var chair=Node(frame,id+"Seat",Part(r,seatName).position);
            W.Seat(seat,chair,Node(r,id+"Exit",Mark(id+"Exit")),Node(frame,id+"Camera",eye));
            var c=(BoxCollider)seat.SeatCollider;
            c.transform.position=eye-Vector3.up*.56f;
            c.size=new Vector3(id=="Driver"?3.05f:1.85f,1.65f,1.48f);
            if(id=="Driver") c.center=new Vector3(-c.transform.position.x,0,0);
        }
        Cabin(drive,r,"Driver","SEAT1");Cabin(op,turn,"Operator","SEAT1.002");
        ConfigureControls(drive);
    }

    [MenuItem("CatCooks/Vehicle Workshop/09B Обновить рычаги и телескоп выбранного крана")]
    static void UpdateControls()
    {
        var selected=Selection.activeTransform;
        var crane=selected ? selected.GetComponentInParent<CCWorkshopCraneChassis>(true) : null;
        if(EditorApplication.isPlaying || !crane || EditorUtility.IsPersistent(crane))
            throw new InvalidOperationException("Выйди из Play Mode и выбери собранный Автокран Workshop в Hierarchy.");
        Undo.IncrementCurrentGroup();
        int group=Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Рычаги и телескоп автокрана");
        try
        {
            ConfigureControls(crane);
            EditorSceneManager.MarkSceneDirty(crane.gameObject.scene);
            Undo.CollapseUndoOperations(group);
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    static void ConfigureControls(CCWorkshopCraneChassis crane)
    {
        var root=crane.transform;
        var boom=Part(root,"Шарнир стрелы");
        var outer=Part(boom,"Телескоп 1");
        var inner=Part(outer,"Телескоп 2");
        var rail=boom.Find("Направляющая телескопа");
        if(inner.parent!=outer || (outer.parent!=boom && outer.parent!=rail))
            throw new InvalidOperationException("Проверь вложенность двух секций стрелы.");
        if(!rail)
        {
            var go=new GameObject("Направляющая телескопа");
            Undo.RegisterCreatedObjectUndo(go,"Направляющая телескопа");
            rail=go.transform;
            rail.SetParent(boom,false);
            rail.localPosition=outer.localPosition;
            // CraneOperatorSeat translates along localPosition.y in the PARENT frame.
            // The long edges of all three meshes are parallel to boom.forward.
            rail.localRotation=Quaternion.FromToRotation(Vector3.up,Vector3.forward);
            Undo.SetTransformParent(outer,rail,"Вложить первую секцию в направляющую");
        }
        if(Vector3.Dot(outer.parent.up,boom.forward)<.999f || Vector3.Dot(inner.parent.up,boom.forward)<.999f)
            throw new InvalidOperationException("Локальная Y направляющих не совпадает с продольной осью стрелы.");

        SetHandle(crane,Part(root,"lever.001"),true);
        SetHandle(crane,Part(root,"lever"),false);
        var old=root.Find("Рычаг опор");
        if(old && old.GetComponent<OutriggerLever>()) Undo.DestroyObjectImmediate(old.gameObject);
        var help=W.Add<CCVehicleHelp>(crane.gameObject);
        Undo.RecordObject(help,"Подсказка автокрана");
        help.text="F — кабина / выход • WASD — ехать • Space — тормоз\nСнаружи: F на рукоятке — опоры • На опорах: A/D — башня • W/S — стрела • E/Q — телескоп";
        PrefabUtility.RecordPrefabInstancePropertyModifications(help);
    }

    static void SetHandle(CCWorkshopCraneChassis crane,Transform handle,bool deploy)
    {
        var filter=handle.GetComponent<MeshFilter>();
        if(!filter || !filter.sharedMesh) throw new InvalidOperationException("У рукоятки отсутствует меш: "+handle.name);
        var bounds=filter.sharedMesh.bounds;
        var box=W.Add<BoxCollider>(handle.gameObject);
        Undo.RecordObject(box,"Попадание по рукоятке");
        box.center=bounds.center;
        Vector3 scale=handle.lossyScale;
        box.size=bounds.size+new Vector3(.025f/Mathf.Max(.0001f,Mathf.Abs(scale.x)),
            .025f/Mathf.Max(.0001f,Mathf.Abs(scale.y)),.025f/Mathf.Max(.0001f,Mathf.Abs(scale.z)));
        box.enabled=true; box.isTrigger=false;
        Undo.RecordObject(handle.gameObject,"Слой рукоятки");
        handle.gameObject.layer=crane.gameObject.layer;
        var part=W.Add<CCCranePadHandle>(handle.gameObject);
        Undo.RecordObject(part,"Управление опорами");
        part.chassis=crane; part.deploy=deploy;
        Vector3 outward=handle.TransformPoint(bounds.center)-handle.position;
        part.hingeAxis=handle.InverseTransformDirection(Vector3.Cross(outward,-crane.transform.up).normalized);
        PrefabUtility.RecordPrefabInstancePropertyModifications(handle.gameObject);
        PrefabUtility.RecordPrefabInstancePropertyModifications(box);
        PrefabUtility.RecordPrefabInstancePropertyModifications(part);
    }
}
#endif
