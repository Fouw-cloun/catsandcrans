#if UNITY_EDITOR
using FishNet.Component.Transforming;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Настраивает выбранные объекты сцены или объекты открытого Prefab Mode. Все изменения — через Undo.</summary>
public static class CraneSetupTools
{
    private const string Menu = "Tools/CatCooks/Crane/";
    [MenuItem(Menu + "1 - Upgrade selected tower crane")]
    private static void UpgradeCrane()
    {
        if (!EditableSelection()) return;
        TowerCraneVehicle crane = Selection.activeGameObject.GetComponentInParent<TowerCraneVehicle>();
        if (crane == null) { Debug.LogError("Выдели корень с TowerCraneVehicle."); return; }
        SerializedObject so = new SerializedObject(crane);
        ConfigurableJoint joint = so.FindProperty("_hookJoint").objectReferenceValue as ConfigurableJoint;
        Transform top = so.FindProperty("_ropeTopPoint").objectReferenceValue as Transform;
        if (joint == null || top == null)
        { Debug.LogError("В TowerCraneVehicle сначала назначь старые Hook Joint и Rope Top Point.", crane); return; }
        if (joint.transform.IsChildOf(crane.transform))
        { Debug.LogError("Крюк должен быть отдельным корневым объектом сцены, вне иерархии башни/каретки. Перенеси его и повтори.", joint); return; }
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Upgrade CatCooks crane");
        CraneHook hook = GetOrAdd<CraneHook>(joint.gameObject);
        NetworkTransform nt = GetOrAdd<NetworkTransform>(joint.gameObject);
        ConfigureTransform(nt, false);
        Rigidbody rb = joint.GetComponent<Rigidbody>();
        Undo.RecordObject(rb, "Configure hook body");
        rb.mass = 20f;
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.constraints = RigidbodyConstraints.None;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.solverIterations = 20;
        rb.solverVelocityIterations = 10;
        if (joint.GetComponentInChildren<Collider>() == null) Undo.AddComponent<BoxCollider>(joint.gameObject);
        Transform latch = joint.transform.Find("CraneLatch");
        if (latch == null)
        {
            GameObject go = new GameObject("CraneLatch");
            Undo.RegisterCreatedObjectUndo(go, "Create hook latch");
            Undo.SetTransformParent(go.transform, joint.transform, "Parent hook latch");
            go.transform.localPosition = new Vector3(0f, -0.25f, 0f);
            latch = go.transform;
        }
        SerializedObject hs = new SerializedObject(hook);
        hs.FindProperty("_latchPoint").objectReferenceValue = latch;
        hs.ApplyModifiedProperties();
        CraneCableVisual cable = crane.GetComponentInChildren<CraneCableVisual>(true);
        if (cable == null)
        {
            GameObject go = new GameObject("CraneCableVisual");
            Undo.RegisterCreatedObjectUndo(go, "Create cable visual");
            Undo.SetTransformParent(go.transform, crane.transform, "Parent cable visual");
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            cable = Undo.AddComponent<CraneCableVisual>(go);
        }
        SerializedObject cs = new SerializedObject(cable);
        cs.FindProperty("_top").objectReferenceValue = top;
        cs.FindProperty("_hook").objectReferenceValue = hook;
        cs.FindProperty("_material").objectReferenceValue = SteelMaterial();
        cs.ApplyModifiedProperties();
        so.FindProperty("_hook").objectReferenceValue = hook;
        so.FindProperty("_cable").objectReferenceValue = cable;
        so.FindProperty("_isStatic").boolValue = true;
        so.FindProperty("_body").objectReferenceValue = null;
        so.ApplyModifiedProperties();
        SaveObject(crane); SaveObject(joint); SaveObject(hook); SaveObject(rb); SaveObject(cable);
        Debug.Log("[Кран] Готово. Поставь CraneLatch точно в нижнее место зацепа крюка. Старый LineRenderer выключится при запуске. Сохрани сцену.", crane);
    }

    [MenuItem(Menu + "2 - Add lift point to selected load")]
    private static void AddLoad()
    {
        if (!EditableSelection()) return;
        GameObject go = Selection.activeGameObject;
        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb == null) { Debug.LogError("Выдели корень груза, на котором уже есть Rigidbody и сетевой предмет."); return; }
        if (go.GetComponent<FirstPersonController>() != null || go.GetComponent<NetworkVehicle>() != null || go.GetComponent<CraneHook>() != null)
        { Debug.LogError("Для игрока используй пункт 3. Этот пункт — только для грузов."); return; }
        GetOrAdd<NetworkObject>(go);
        GetOrAdd<CraneLoad>(go);
        ConfigureTransform(GetOrAdd<NetworkTransform>(go), true);
        Undo.RecordObject(rb, "Configure load body");
        rb.isKinematic = false; rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.solverIterations = 16; rb.solverVelocityIterations = 8;
        GameObject socket = new GameObject("CraneLiftPoint");
        Undo.RegisterCreatedObjectUndo(socket, "Create lift point");
        Undo.SetTransformParent(socket.transform, go.transform, "Parent lift point");
        Bounds bounds = new Bounds(go.transform.position, Vector3.zero);
        bool first = true;
        foreach (Collider collider in go.GetComponentsInChildren<Collider>())
        {
            if (collider.isTrigger || collider.attachedRigidbody != rb) continue;
            if (first) { bounds = collider.bounds; first = false; } else bounds.Encapsulate(collider.bounds);
        }
        socket.transform.position = first ? go.transform.position : new Vector3(bounds.center.x, bounds.max.y + 0.03f, bounds.center.z);
        socket.transform.localRotation = Quaternion.identity;
        socket.transform.localScale = Vector3.one;
        socket.layer = go.layer;
        Undo.AddComponent<CraneLiftPoint>(socket);
        SaveObject(go); SaveObject(rb);
        Selection.activeGameObject = socket;
        Debug.Log("[Кран] Точка создана. Переставь её на ручку/кольцо банки. Зелёная сфера — радиус захвата в метрах. Допускается несколько точек на одном грузе.", socket);
    }

    [MenuItem(Menu + "3 - Configure selected player")]
    private static void AddPlayer()
    {
        if (!EditableSelection()) return;
        GameObject go = Selection.activeGameObject;
        if (go.GetComponent<FirstPersonController>() == null || go.GetComponent<PlayerInteractor>() == null)
        { Debug.LogError("Открой префаб игрока и выдели корень с FirstPersonController и PlayerInteractor."); return; }
        GetOrAdd<CraneRider>(go);
        GetOrAdd<CranePlayerInteractor>(go);
        SaveObject(go);
        Debug.Log("[Кран] Игрок настроен. NetworkTransform игрока оставлен прежним: Client Authoritative. Сохрани префаб и пересобери все клиенты.", go);
    }

    [MenuItem(Menu + "4 - Validate open scene")]
    private static void ValidateScene()
    {
        int errors = 0;
        foreach (CraneHook hook in Object.FindObjectsByType<CraneHook>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!CheckTransform(hook.GetComponent<NetworkTransform>(), false)) errors++;
            if (hook.transform.parent != null) Debug.LogWarning("[Кран] Проверь родителя крюка: он не должен двигать физический объект.", hook);
            if (hook.GetComponent<Rigidbody>().mass < 10f) Debug.LogWarning("[Кран] Очень лёгкий крюк: тяжёлый груз может растягивать joint. Начни с 20 кг.", hook);
        }
        foreach (CraneLoad load in Object.FindObjectsByType<CraneLoad>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!CheckTransform(load.GetComponent<NetworkTransform>(), true)) errors++;
            if (load.GetComponentsInChildren<CraneLiftPoint>(true).Length == 0)
            { Debug.LogError("[Кран] У груза нет CraneLiftPoint.", load); errors++; }
            foreach (MeshCollider mesh in load.GetComponentsInChildren<MeshCollider>())
                if (!mesh.convex) { Debug.LogError("[Кран] Динамическому грузу нужен Convex MeshCollider или простые коллайдеры.", mesh); errors++; }
        }
        foreach (TowerCraneVehicle crane in Object.FindObjectsByType<TowerCraneVehicle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SerializedObject so = new SerializedObject(crane);
            foreach (string name in new[] { "_tower", "_trolley", "_ropeTopPoint", "_hook", "_cable" })
                if (so.FindProperty(name).objectReferenceValue == null)
                { Debug.LogError("[Кран] Не назначено " + name, crane); errors++; }
        }
        Debug.Log("[Кран] Проверка закончена. Ошибок настройки: " + errors + ". Это не заменяет сетевой Play Mode тест.");
    }

    private static bool EditableSelection()
    {
        if (Application.isPlaying || Selection.activeGameObject == null) return false;
        if (EditorUtility.IsPersistent(Selection.activeGameObject))
        { Debug.LogError("Открой префаб двойным щелчком (Prefab Mode) или выдели объект в сцене."); return false; }
        return true;
    }
    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(go);
    }
    private static void ConfigureTransform(NetworkTransform nt, bool clientAuthority)
    {
        SerializedObject so = new SerializedObject(nt);
        SetBool(so, "_clientAuthoritative", clientAuthority);
        SetBool(so, "_sendToOwner", true);
        SetBool(so, "_synchronizePosition", true);
        SetBool(so, "_synchronizeRotation", true);
        SerializedProperty config = so.FindProperty("_componentConfiguration");
        if (config != null && config.propertyType == SerializedPropertyType.Enum)
        {
            int disabled = System.Array.IndexOf(config.enumNames, "Disabled");
            if (disabled >= 0) config.enumValueIndex = disabled;
            else Debug.LogWarning("[Кран] Выставь Component Configuration = Disabled вручную.", nt);
        }
        else Debug.LogWarning("[Кран] Версия FishNet отличается: выставь Component Configuration = Disabled вручную.", nt);
        so.ApplyModifiedProperties(); SaveObject(nt);
    }
    private static bool CheckTransform(NetworkTransform nt, bool clientAuthority)
    {
        if (nt == null) { Debug.LogError("[Кран] Нет NetworkTransform."); return false; }
        SerializedObject so = new SerializedObject(nt);
        SerializedProperty authority = so.FindProperty("_clientAuthoritative");
        SerializedProperty config = so.FindProperty("_componentConfiguration");
        bool ok = authority != null && authority.boolValue == clientAuthority && config != null &&
            config.propertyType == SerializedPropertyType.Enum && config.enumNames[config.enumValueIndex] == "Disabled";
        if (!ok) Debug.LogError("[Кран] NetworkTransform: Client Authoritative = " + clientAuthority + ", Component Configuration = Disabled.", nt);
        return ok;
    }
    private static void SetBool(SerializedObject so, string name, bool value)
    {
        SerializedProperty p = so.FindProperty(name);
        if (p != null) p.boolValue = value;
        else Debug.LogWarning("[Кран] Не найдено поле NetworkTransform " + name + "; проверь инспектор вручную.");
    }
    private static Material SteelMaterial()
    {
        const string directory = "Assets/CatCooksCraneGenerated";
        const string path = directory + "/CraneCableSteel.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return null;
        if (!AssetDatabase.IsValidFolder(directory)) AssetDatabase.CreateFolder("Assets", "CatCooksCraneGenerated");
        material = new Material(shader) { name = "CraneCableSteel" };
        Color color = new Color(0.28f, 0.31f, 0.34f);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.7f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.55f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
    private static void SaveObject(Object obj)
    {
        EditorUtility.SetDirty(obj);
        if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
        GameObject go = obj as GameObject;
        if (go == null && obj is Component component) go = component.gameObject;
        if (go != null && go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
    }
}
#endif
