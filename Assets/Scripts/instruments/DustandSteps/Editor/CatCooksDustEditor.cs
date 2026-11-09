using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(CatCooksDustSystem))]
public sealed class CatCooksDustEditor : Editor
{
    Vector3 testOffset;
    float testScale=1;
    public override bool RequiresConstantRepaint() => Application.isPlaying || ((CatCooksDustSystem)target).IsPreviewing;
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector(); var world=(CatCooksDustSystem)target;
        EditorGUILayout.HelpBox("Один World System на сцену. Частицы хранят мировые координаты; Transform системы не задаёт размер эффектов. Размер меняется в эмиттерах через Effect Scale.",MessageType.Info);
        EditorGUILayout.LabelField("Живых частиц / пропущенных запросов",$"{world.LiveCount} / {world.SkippedCount}");
        testOffset=EditorGUILayout.Vector3Field("Проба: смещение от системы",testOffset);
        testScale=EditorGUILayout.Slider("Проба: размер",testScale,.1f,4);
        var names=new[] {"Шаг","Бег","Прыжок","Приземление","Удар материала","Колесо","Занос","Пылевой выброс","Дым"};
        for(int row=0;row<3;row++)
        using(new EditorGUILayout.HorizontalScope())
            for(int col=0;col<3;col++)
            {
                int k=row*3+col;
                if(GUILayout.Button(names[k]))
                {
                    if(!Application.isPlaying) world.StartPreview();
                    world.Emit((CatCooksDustSystem.Effect)k,world.transform.position+testOffset,Vector3.up,Vector3.forward,1,null,testScale);
                }
            }
        if(GUILayout.Button("Очистить частицы")) world.ClearParticles();
        using(new EditorGUI.DisabledScope(Application.isPlaying))
            if(GUILayout.Button("Остановить preview")) world.StopPreview();
        if(GUILayout.Button("Проверить геометрию и интервалы")) CatCooksDustValidation.Run();
    }
}

[CustomEditor(typeof(CatCooksDustWheels))]
public sealed class CatCooksDustWheelsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("Для рейкаст-колеса Point — центр визуального колеса или anchor подвески. Probe Distance — расстояние до земли: для центра обычно радиус + небольшой запас; для anchor — ход подвески + радиус.",MessageType.Info);
        if(GUILayout.Button("Найти WheelCollider / CartWheel / колёса техники")) CatCooksDustSetup.AutoWheels((CatCooksDustWheels)target);
    }
}

public static class CatCooksDustSetup
{
    static string Root
    {
        get
        {
            var shader=Shader.Find("CatCooks/Dust and Smoke/Toon Puff");
            string path=shader==null ? "" : AssetDatabase.GetAssetPath(shader);
            return string.IsNullOrEmpty(path) ? "Assets/CatCooks/DustSmoke/" :
                System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(path)).Replace('\\','/')+"/";
        }
    }
    [MenuItem("GameObject/CatCooks/Dust and Smoke/World System",false,10)]
    public static void CreateSystem()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Prefabs/CCD_WorldSystem.prefab");
        GameObject go=prefab!=null ? (GameObject)PrefabUtility.InstantiatePrefab(prefab) : new GameObject("CatCooks Dust World");
        Undo.RegisterCreatedObjectUndo(go,"Create dust world");
        var world=go.GetComponent<CatCooksDustSystem>(); if(world==null) world=Undo.AddComponent<CatCooksDustSystem>(go);
        world.puffShader=Shader.Find("CatCooks/Dust and Smoke/Toon Puff");
        world.defaultProfile=AssetDatabase.LoadAssetAtPath<CatCooksDustProfile>(Root+"Profiles/DrySoil.asset");
        EditorUtility.SetDirty(world); Selection.activeGameObject=go;
    }
    [MenuItem("Tools/CatCooks/Dust/Add player effects to selected")]
    public static void AddPlayer()
    {
        GameObject go=Selection.activeGameObject; if(go==null) return;
        var controller=go.GetComponentInParent<CharacterController>(); if(controller!=null) go=controller.gameObject;
        var receiver=go.GetComponent<CatCooksDustPlayer>(); if(receiver==null) receiver=Undo.AddComponent<CatCooksDustPlayer>(go);
        Undo.RecordObject(receiver,"Set up dust player"); receiver.ignoreRoot=go.transform; receiver.system=CatCooksDustSystem.Active;
        EditorUtility.SetDirty(receiver); Selection.activeGameObject=go;
    }
    [MenuItem("Tools/CatCooks/Dust/Add material impact to selected")]
    public static void AddImpact()
    {
        GameObject go=Selection.activeGameObject; if(go==null) return;
        var body=go.GetComponentInParent<Rigidbody>();
        if(body==null) { Debug.LogWarning("Выбери материал с уже настроенным Rigidbody."); return; }
        var impact=body.GetComponent<CatCooksDustImpact>(); if(impact==null) impact=Undo.AddComponent<CatCooksDustImpact>(body.gameObject);
        Undo.RecordObject(impact,"Set up dust impact"); impact.system=CatCooksDustSystem.Active;
        EditorUtility.SetDirty(impact); Selection.activeGameObject=body.gameObject;
    }
    [MenuItem("Tools/CatCooks/Dust/Add wheel trails to selected")]
    public static void AddWheels()
    {
        GameObject go=Selection.activeGameObject; if(go==null) return;
        var receiver=go.GetComponent<CatCooksDustWheels>(); if(receiver==null) receiver=Undo.AddComponent<CatCooksDustWheels>(go);
        AutoWheels(receiver); Selection.activeGameObject=go;
    }
    public static void AutoWheels(CatCooksDustWheels receiver)
    {
        var result=new List<CatCooksDustWheels.Wheel>();
        foreach(var wheel in receiver.GetComponentsInChildren<WheelCollider>(true))
            result.Add(new CatCooksDustWheels.Wheel { wheelCollider=wheel,point=wheel.transform });
        if(result.Count==0)
        {
            // Editor-only introspection: no runtime dependency on FishNet, CartWheel or WheeledVehicle.
            foreach(var component in receiver.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if(component==null || component==receiver) continue;
                var so=new SerializedObject(component);
                if(component.GetType().Name=="CartWheel")
                {
                    var radius=so.FindProperty("_radius");
                    result.Add(new CatCooksDustWheels.Wheel { point=component.transform,probeDistance=(radius!=null ? radius.floatValue : .35f)+.12f });
                    continue;
                }
                var wheels=so.FindProperty("wheels"); var wheelRadius=so.FindProperty("wheelRadius");
                if(wheels==null || !wheels.isArray || wheelRadius==null) continue;
                var rest=so.FindProperty("restLength");
                for(int i=0;i<wheels.arraySize;i++)
                {
                    var element=wheels.GetArrayElementAtIndex(i); var a=element.FindPropertyRelative("anchor"); var v=element.FindPropertyRelative("visual");
                    Transform anchor=a==null ? null : a.objectReferenceValue as Transform;
                    Transform visual=v==null ? null : v.objectReferenceValue as Transform;
                    if(anchor==null && visual==null) continue;
                    result.Add(new CatCooksDustWheels.Wheel { point=visual!=null ? visual : anchor,
                        probeDistance=wheelRadius.floatValue+(visual!=null ? .12f : (rest!=null ? rest.floatValue : .5f)+.08f) });
                }
            }
        }
        if(result.Count==0) { Debug.LogWarning("Автоматически колёса не найдены. Добавь точки в Wheels вручную.",receiver); return; }
        Undo.RecordObject(receiver,"Detect dust wheels"); receiver.wheels=result.ToArray();
        receiver.ignoreRoot=receiver.transform; receiver.system=CatCooksDustSystem.Active;
        EditorUtility.SetDirty(receiver); receiver.ResetTracking();
    }

    [MenuItem("Tools/CatCooks/Dust/Create preview gallery (new additive scene)")]
    public static void Gallery()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        CreateSystem(); var world=Selection.activeGameObject.GetComponent<CatCooksDustSystem>();
        Vector3 origin=new Vector3(0,1000,0); world.transform.position=origin;
        var plane=GameObject.CreatePrimitive(PrimitiveType.Plane); plane.name="Dust preview floor";
        plane.transform.localScale=new Vector3(1,1,1); plane.transform.position=origin;
        if(!AssetDatabase.IsValidFolder(Root+"Preview")) AssetDatabase.CreateFolder(Root.TrimEnd('/'),"Preview");
        string materialPath=Root+"Preview/DustPreviewFloor.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(material==null)
        {
            Shader shader=Shader.Find("Universal Render Pipeline/Lit");
            if(shader!=null)
            {
                material=new Material(shader); material.SetColor("_BaseColor",new Color(.19f,.23f,.22f));
                AssetDatabase.CreateAsset(material,materialPath);
            }
        }
        if(material!=null) plane.GetComponent<MeshRenderer>().sharedMaterial=material;
        for(int i=0;i<9;i++)
        {
            var go=new GameObject(((CatCooksDustSystem.Effect)i).ToString());
            go.transform.position=origin+new Vector3((i%3-1)*3,0,(i/3-1)*3);
            var e=go.AddComponent<CatCooksDustEmitter>(); e.system=world; e.effect=(CatCooksDustSystem.Effect)i;
            e.loop=true; e.eventsPerSecond=i==8 ? 1 : .65f;
            if(i==8) e.profile=AssetDatabase.LoadAssetAtPath<CatCooksDustProfile>(Root+"Profiles/Ash.asset");
        }
        foreach(var root in scene.GetRootGameObjects())
        {
            var camera=root.GetComponent<Camera>(); if(camera==null) continue;
            camera.transform.position=origin+new Vector3(7,8,-10); camera.transform.LookAt(origin); camera.fieldOfView=45;
            camera.farClipPlane=40; camera.depth=100; camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.09f,.13f,.16f);
        }
        EditorSceneManager.MarkSceneDirty(scene); Selection.activeGameObject=world.gameObject;
        Debug.Log("Галерея создана в отдельной сцене. Нажми Play. Если в другой загруженной сцене есть камера, временно выключи её для просмотра галереи.");
    }
}

public static class CatCooksDustValidation
{
    [MenuItem("Tools/CatCooks/Dust/Validate geometry and timing")]
    public static void Run()
    {
        try
        {
            for(int variant=0;variant<3;variant++)
            {
                var t=CatCooksPuffMesh.Build(variant); double volume=0;
                for(int i=0;i<t.vertices.Length;i++)
                {
                    Check(CatCooksDustUtility.Finite(t.vertices[i]),"Non-finite cloud vertex");
                    Check(t.normals[i].sqrMagnitude>.8f,"Cloud normal");
                }
                for(int i=0;i<t.triangles.Length;i+=3)
                {
                    Vector3 a=t.vertices[t.triangles[i]],b=t.vertices[t.triangles[i+1]],c=t.vertices[t.triangles[i+2]];
                    Check(Vector3.Cross(b-a,c-a).sqrMagnitude>1e-12f,"Degenerate cloud triangle");
                    volume+=Vector3.Dot(a,Vector3.Cross(b,c))/6.0;
                }
                Check(volume>0,"Inverted cloud winding");
            }
            foreach(int fps in new[] {30,60,120})
            {
                var clock=new CatCooksDustUtility.DistanceClock(); int total=0;
                for(int frame=0;frame<fps*10;frame++) total+=clock.Advance(3f/fps,.5f,3);
                Check(total==60,"Distance clock differs at "+fps+" FPS");
                Check(clock.Advance(100,.5f,2)==2,"Stall cap");
                Check(clock.Advance(0,.5f,3)==0,"Backlog after stall");
                clock.Reset(); Check(clock.remainder==0,"Teleport reset");
                Check(clock.Advance(float.NaN,.5f,3)==0,"Invalid distance");
            }
            var shader=Shader.Find("CatCooks/Dust and Smoke/Toon Puff");
            Check(shader!=null && shader.isSupported,"Missing or unsupported Toon Puff shader");
            Check(!ShaderUtil.ShaderHasError(shader),"Shader import error");
            Debug.Log("Dust PASS: 3 cloud templates, distance emission at 30/60/120 FPS, stall/reset guards, shader import. Gameplay, appearance and player-build tests are separate.");
        }
        catch(Exception e) { Debug.LogError("Dust validation FAILED: "+e.Message); }
    }
    static void Check(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
}
