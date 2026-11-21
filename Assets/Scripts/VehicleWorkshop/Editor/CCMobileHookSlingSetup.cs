using System;
using CatCooks.Food;
using FishNet.Component.Transforming;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CCMobileHookSlingSetup
{
    private const string Menu = "CatCooks/Vehicle Workshop/";
    private const string Webbing = "Assets/VehicleWorkshop/Models/Winch_Ochre.mat";

    [MenuItem(Menu + "09D Физический крюк и стропы в сцене")]
    private static void Install()
    {
        var selected = Selection.activeTransform;
        var winch = selected ? selected.GetComponentInParent<CCMobileCraneWinch>(true) : null;
        if (EditorApplication.isPlayingOrWillChangePlaymode || !winch || EditorUtility.IsPersistent(winch))
            throw new InvalidOperationException("Выбери в сцене автокран с лебёдкой 09C вне Play Mode.");
        if (!winch.hook || !winch.upperBlock || !winch.cable)
            throw new InvalidOperationException("Сначала установи лебёдку 09C.");
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Физический крюк и стропы");
        try
        {
            if (!winch.physicalHook) Upgrade(winch);
            var scene = winch.gameObject.scene;
            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var carcass in root.GetComponentsInChildren<FoodCarcass>(true))
                    if (Prepare(carcass)) count++;
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"Крюк готов. Новых комплектов строп в сцене: {count}. Для появляющейся в игре туши выбери её префаб и выполни команду 09D для туши.", winch);
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    private static T Add<T>(GameObject obj) where T : Component
    {
        if (obj.TryGetComponent<T>(out var component) && component)
            return component;

        component = Undo.AddComponent<T>(obj);
        if (!component)
            throw new InvalidOperationException($"Unity не удалось добавить {typeof(T).Name} на «{obj.name}».");
        return component;
    }

    private static Transform Node(Transform parent, string name, Vector3 world)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, name);
        if (go.scene != parent.gameObject.scene)
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, parent.gameObject.scene);
        Undo.SetTransformParent(go.transform, parent, name);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.transform.position = world;
        return go.transform;
    }

    private static void ConfigureSync(NetworkTransform net, bool clientAuthority)
    {
        var ns = new SerializedObject(net);
        ns.FindProperty("_clientAuthoritative").boolValue = clientAuthority;
        ns.FindProperty("_synchronizePosition").boolValue = true;
        ns.FindProperty("_synchronizeRotation").boolValue = true;
        ns.FindProperty("_sendToOwner").boolValue = true;
        var conf = ns.FindProperty("_componentConfiguration");
        conf.enumValueIndex = Array.IndexOf(conf.enumNames, "Disabled");
        ns.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(net);
    }

    private static void Upgrade(CCMobileCraneWinch w)
    {
        Transform block = w.hook;
        var seat = w.GetComponent<CraneOperatorSeat>();
        var state = w.GetComponent<CraneState>();
        if (!seat || !state || block == w.transform || block.gameObject.scene != w.gameObject.scene ||
            (block.parent && block.parent != w.transform))
            throw new InvalidOperationException("Проверь привязку подвеса 09C и место оператора.");
        Undo.RecordObject(w, "Ссылки подвеса");
        Undo.RecordObject(block, "Парковка крюка");
        if (block.parent)
            Undo.SetTransformParent(block, null, "Самостоятельный сетевой крюк");
        block.position = w.upperBlock.position + Vector3.down * CCMobileCraneWinch.ParkedLength;
        var body = Add<Rigidbody>(block.gameObject);
        body.mass = 45f;
        body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var shape = Add<CapsuleCollider>(block.gameObject);
        shape.direction = 1;
        shape.center = Vector3.down * (w.eyeToBottom * .48f);
        shape.height = w.eyeToBottom; shape.radius = .14f;
        Add<NetworkObject>(block.gameObject);
        ConfigureSync(Add<NetworkTransform>(block.gameObject), false);
        var joint = Add<ConfigurableJoint>(block.gameObject);
        joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Free;
        var hook = Add<CraneHook>(block.gameObject);
        var mouth = Node(block, "Зев крюка", block.position - Vector3.up * (w.eyeToBottom - .105f));
        var hand = Node(block, "Хват за корпус", block.position - Vector3.up * .46f);
        var hs = new SerializedObject(hook);
        hs.FindProperty("_ropeAnchor").objectReferenceValue = block;
        hs.FindProperty("_latchPoint").objectReferenceValue = mouth;
        hs.FindProperty("_maxLoadMass").floatValue = 1500f;
        hs.FindProperty("_maxCaptureSpeed").floatValue = 3.5f;
        hs.FindProperty("_autoAttach").boolValue = false;
        hs.ApplyModifiedProperties();
        var guide = Add<CCMobileHookGuide>(block.gameObject);
        guide.hook = hook; guide.winch = w; guide.seat = seat; guide.outriggers = state; guide.grip = hand;
        var cable = Add<CraneCableVisual>(Node(w.transform, "Плетёный трос", w.upperBlock.position).gameObject);
        var cs = new SerializedObject(cable);
        cs.FindProperty("_top").objectReferenceValue = w.upperBlock;
        cs.FindProperty("_hook").objectReferenceValue = hook;
        cs.FindProperty("_radius").floatValue = .017f;
        cs.FindProperty("_twistPitch").floatValue = .27f;
        cs.ApplyModifiedProperties();
        w.physicalHook = hook; w.braidedCable = cable;
        Undo.RecordObject(w.cable, "Сменить трос");
        w.cable.enabled = false;
        EditorUtility.SetDirty(guide);
        EditorUtility.SetDirty(w);
        PrefabUtility.RecordPrefabInstancePropertyModifications(w);
    }

    [MenuItem(Menu + "09D Подготовить выбранную тушу")]
    private static void SelectedCarcass()
    {
        var pick = Selection.activeGameObject;
        if (!pick || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Вне Play Mode выбери тушу в Hierarchy или её префаб в Project.");
        if (!EditorUtility.IsPersistent(pick))
        {
            var food = pick.GetComponentInParent<FoodCarcass>(true);
            if (!food) food = pick.GetComponentInChildren<FoodCarcass>(true);
            if (!food) throw new InvalidOperationException("Нужна целая туша с FoodCarcass.");
            if (Prepare(food)) EditorSceneManager.MarkSceneDirty(food.gameObject.scene);
            return;
        }
        string path = AssetDatabase.GetAssetPath(pick);
        if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Выбери игровой префаб туши.");
        var contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var foods = contents.GetComponentsInChildren<FoodCarcass>(true);
            if (foods.Length == 0) throw new InvalidOperationException("В префабе отсутствует FoodCarcass.");
            bool changed = false;
            foreach (var food in foods) changed |= Prepare(food);
            if (changed && !PrefabUtility.SaveAsPrefabAsset(contents, path))
                throw new InvalidOperationException("Unity не сохранила префаб туши.");
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
    }

    private static bool Prepare(FoodCarcass food)
    {
        var root = food.transform;
        if (food.GetComponent<CCCarcassSling>()) return false;
        if (!food.GetComponent<NetworkObject>() || !food.GetComponent<Rigidbody>())
        {
            Debug.LogWarning("Для строп туше нужны NetworkObject и Rigidbody на корне.", food);
            return false;
        }
        Vector3 facing = Vector3.ProjectOnPlane(root.forward, Vector3.up);
        if (facing.sqrMagnitude < .001f) facing = Vector3.ProjectOnPlane(root.up, Vector3.up);
        Quaternion frame = Quaternion.LookRotation(facing, Vector3.up);
        Quaternion inv = Quaternion.Inverse(frame);
        bool found = false;
        Bounds bounds = default;
        foreach (var renderer in food.GetComponentsInChildren<Renderer>(true))
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
            Bounds b = renderer.localBounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 p = inv * (renderer.transform.TransformPoint(corner) - root.position);
                if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; }
                else bounds.Encapsulate(p);
            }
        }
        if (!found || bounds.extents.sqrMagnitude < .001f)
        {
            Debug.LogWarning("У туши нет видимой геометрии для строп.", food);
            return false;
        }
        var material = AssetDatabase.LoadAssetAtPath<Material>(Webbing);
        if (!material) throw new InvalidOperationException("Не найден материал корпуса крюка из 09C: " + Webbing);
        Add<CraneLoad>(root.gameObject);
        // The food remains client-authoritative when held and server-driven when
        // unowned, matching the project's CraneLoad/PlayerHand integration.
        ConfigureSync(Add<NetworkTransform>(root.gameObject), true);
        var sling = Add<CCCarcassSling>(root.gameObject);
        Vector3 half = bounds.extents;
        bool longX = half.x > half.z;
        Vector3 axis = longX ? Vector3.right : Vector3.forward;
        Vector3 across = longX ? Vector3.forward : Vector3.right;
        float length = longX ? half.x : half.z;
        float width = longX ? half.z : half.x;
        Vector3 World(Vector3 p) => root.position + frame * (bounds.center + p);
        Vector3 apex = World(Vector3.up * (half.y + Mathf.Clamp(width * .85f, .28f, .7f)));
        var point = Add<CraneLiftPoint>(Node(root, "Петля строп туши", apex).gameObject);
        point.CaptureRadius = 1.4f;
        point.LockRotation = false;
        sling.point = point;
        sling.webbing = material;
        Vector3[] Band(float offset)
        {
            Vector3 Local(float cross, float height) => root.InverseTransformPoint(World(
                axis * (length * offset) + across * (width * cross) + Vector3.up * (half.y * height)));
            return new[] { root.InverseTransformPoint(apex), Local(-.86f,.70f), Local(-1f,.03f),
                Local(-.71f,-.76f), Local(0f,-.98f), Local(.71f,-.76f),
                Local(1f,.03f), Local(.86f,.70f), root.InverseTransformPoint(apex) };
        }
        sling.frontWrap = Band(.47f);
        sling.rearWrap = Band(-.47f);
        EditorUtility.SetDirty(sling);
        PrefabUtility.RecordPrefabInstancePropertyModifications(sling);
        return true;
    }
}
