#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using FishNet.Object;
using FishNet.Component.Transforming;
using FishNet.Managing.Object;
using CatCooks.Food;

public static class CCWorkshopEditor
{
    public const string Models = "Assets/VehicleWorkshop/Models/";
    public const string Generated = "Assets/VehicleWorkshop/Generated/";

    public static T Add<T>(GameObject go) where T : Component
    {
        var component = go.GetComponent<T>();
        // Unity's editor can represent missing components with a Unity-null object.
        if (component == null)
            component = Undo.AddComponent<T>(go);
        return component;
    }

    public static Transform Find(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

    public static Transform Child(Transform parent, string name, Vector3 local)
    {
        var child = parent.Find(name);
        if (child == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            child = go.transform;
            child.SetParent(parent, false);
        }
        child.localPosition = local;
        return child;
    }

    public static void Set(UnityEngine.Object target, string key, object value)
    {
        var serialized = new SerializedObject(target);
        var property = serialized.FindProperty(key);
        if (property == null)
            throw new InvalidOperationException(target.GetType().Name + " missing serialized field " + key);
        if (value == null || value is UnityEngine.Object) property.objectReferenceValue = value as UnityEngine.Object;
        else if (value is bool b) property.boolValue = b;
        else if (value is int i) property.intValue = i;
        else if (value is float f) property.floatValue = f;
        else if (value is Vector3 v) property.vector3Value = v;
        else if (value is Vector2 v2) property.vector2Value = v2;
        else if (value is Quaternion q) property.quaternionValue = q;
        else throw new ArgumentException(key);
        serialized.ApplyModifiedProperties();
    }

    public static Transform Ref(UnityEngine.Object target, string key) =>
        new SerializedObject(target).FindProperty(key)?.objectReferenceValue as Transform;

    public static GameObject Selected()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Выйди из Play Mode.");
        var go = Selection.activeGameObject;
        if (go == null || EditorUtility.IsPersistent(go))
            throw new InvalidOperationException("Выдели объект в сцене (Hierarchy).");
        Undo.RegisterFullObjectHierarchyUndo(go, "Vehicle Workshop");
        return go;
    }

    public static void Done(GameObject go, string message)
    {
        Selection.activeGameObject = go;
        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log(message, go);
    }

    public static Bounds BoundsIn(Transform root, IEnumerable<Renderer> renderers)
    {
        Bounds bounds = default;
        bool any = false;
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            Bounds worldBounds = renderer.bounds;
            for (int i = 0; i < 8; i++)
            {
                var signs = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                var point = root.InverseTransformPoint(worldBounds.center + Vector3.Scale(worldBounds.extents, signs));
                if (any) bounds.Encapsulate(point);
                else
                {
                    bounds = new Bounds(point, Vector3.zero);
                    any = true;
                }
            }
        }
        if (!any) throw new InvalidOperationException("Не найдены меши для определения габаритов.");
        return bounds;
    }

    public static Rigidbody Body(NetworkVehicle vehicle, float mass)
    {
        Add<NetworkObject>(vehicle.gameObject);
        Set(Add<NetworkTransform>(vehicle.gameObject), "_clientAuthoritative", true);
        var body = Add<Rigidbody>(vehicle.gameObject);
        Undo.RecordObject(body, "Stable body");
        body.mass = mass;
        body.linearDamping = .12f;
        body.angularDamping = 1.8f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.solverIterations = 12;
        body.solverVelocityIterations = 6;
        Set(vehicle, "_body", body);
        Set(vehicle, "_isStatic", false);
        return body;
    }

    public static void Seat(NetworkVehicle v, Transform seat, Transform exit, Transform camera)
    {
        if (seat == null || exit == null || camera == null)
            throw new InvalidOperationException("Seat / exit / camera missing");
        Set(v, "_seatPoint", seat);
        Set(v, "_exitPoint", exit);
        Add<Camera>(camera.gameObject);
        Add<AudioListener>(camera.gameObject);
        camera.gameObject.SetActive(false);
        Set(v, "_vehicleCamera", camera.gameObject);

        var hit = Child(seat, "WorkshopSeatHit", seat.InverseTransformVector(v.transform.up * .3f));
        hit.rotation = v.transform.rotation;
        var box = Add<BoxCollider>(hit.gameObject);
        Vector3 scale = hit.lossyScale;
        box.size = new Vector3(
            .65f / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
            .8f / Mathf.Max(.0001f, Mathf.Abs(scale.y)),
            .65f / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
        Set(v, "_seatCollider", box);

        int layer = LayerMask.NameToLayer("Interactable");
        if (layer >= 0)
        {
            v.gameObject.layer = layer;
            hit.gameObject.layer = layer;
        }
        Add<CCVehicleHelp>(v.gameObject);
    }

    public static void Tracks(TrackedVehicle v, Bounds b, float mass)
    {
        Body(v, mass);
        const float sag = .16f;
        v.restLength = .55f;
        v.trackRadius = .25f;
        var points = Child(v.transform, "WorkshopSuspension", Vector3.zero);
        v.leftTrackPoints = new Transform[4];
        v.rightTrackPoints = new Transform[4];
        for (int side = 0; side < 2; side++)
        {
            float x = side == 0 ? b.min.x + b.size.x * .12f : b.max.x - b.size.x * .12f;
            var sidePoints = side == 0 ? v.leftTrackPoints : v.rightTrackPoints;
            for (int j = 0; j < 4; j++)
            {
                float y = b.min.y + v.restLength + v.trackRadius - sag;
                float z = Mathf.Lerp(b.min.z, b.max.z, .12f + j * .253333f);
                sidePoints[j] = Child(points, (side == 0 ? "L" : "R") + j, new Vector3(x, y, z));
            }
        }
        v.springStrength = mass * Physics.gravity.magnitude / (8 * sag);
        v.springDamper = 1.7f * Mathf.Sqrt(v.springStrength * mass / 8);
        v.stableSuspension = true;
        v.groundMask = ~0;
        v.maxForwardSpeed = 4;
        v.maxReverseSpeed = 2;
        v.tractionAcceleration = 1.5f;
        Set(v, "_centerOfMass", new Vector3(b.center.x, b.min.y + .35f, b.center.z));
    }

    public static void Wheels(WheeledVehicle v, IList<Transform> meshes, float mass)
    {
        Body(v, mass);
        if (meshes.Count < 4) throw new InvalidOperationException("Нужны минимум 4 колеса.");
        var wheels = new List<WheeledVehicle.Wheel>();
        var holder = Child(v.transform, "WorkshopSuspension", Vector3.zero);
        float front = meshes.Max(mesh => v.transform.InverseTransformPoint(mesh.GetComponent<Renderer>().bounds.center).z);
        const float sag = .14f;
        v.restLength = .42f;
        v.wheelRadius = meshes.Average(mesh => mesh.GetComponent<Renderer>().bounds.size.y * .5f);
        foreach (var mesh in meshes)
        {
            var worldBounds = mesh.GetComponent<Renderer>().bounds;
            var center = v.transform.InverseTransformPoint(worldBounds.center);
            string id = "WheelPivot_" + mesh.name;
            var pivot = Find(v.transform, id);
            if (pivot == null)
            {
                pivot = Child(mesh.parent, id, mesh.parent.InverseTransformPoint(worldBounds.center));
                pivot.rotation = v.transform.rotation;
                Undo.SetTransformParent(mesh, pivot, "Wheel pivot");
            }
            var anchor = Child(holder, "Anchor_" + mesh.name, center + Vector3.up * (v.restLength - sag));
            wheels.Add(new WheeledVehicle.Wheel
            {
                anchor = anchor,
                visual = pivot,
                steering = center.z > front - .5f,
                motorized = true,
                spinAxisLocal = Vector3.right
            });
        }
        v.wheels = wheels.ToArray();
        v.stableSuspension = true;
        v.groundMask = ~0;
        v.maxForwardSpeed = 6;
        v.maxReverseSpeed = 2.5f;
        v.tractionAcceleration = 1.1f;
        v.springStrength = mass * Physics.gravity.magnitude / (meshes.Count * sag);
        v.springDamper = 1.7f * Mathf.Sqrt(v.springStrength * mass / meshes.Count);
        Set(v, "_centerOfMass", new Vector3(0, .7f, 0));
    }

    public static void Quiet(Cookware cookware)
    {
        foreach (var visual in cookware.GetComponentsInChildren<CookwareLiquidVisual>(true))
        {
            Set(visual, "_sloshGain", .22f);
            Set(visual, "_sloshDamping", 7f);
            Set(visual, "_maxSloshAngle", 6f);
        }
    }

    public static Cookware Container(GameObject go, string id, Vector3 bottom, Quaternion worldFrame, Vector2 half, float depth, int capacity)
    {
        var existing = go.GetComponent<Cookware>();
        if (existing != null)
        {
            Quiet(existing);
            return existing;
        }
        Directory.CreateDirectory(Generated);
        AssetDatabase.Refresh();
        string path = Generated + id + "_Cookware.asset";
        var def = AssetDatabase.LoadAssetAtPath<CookwareDef>(path);
        if (def == null)
        {
            def = ScriptableObject.CreateInstance<CookwareDef>();
            def.Title = id;
            def.Kind = CookwareKind.Pot;
            def.Capacity = capacity;
            def.Absorbs = true;
            AssetDatabase.CreateAsset(def, path);
        }
        int cookwareLayer = LayerMask.NameToLayer("Cookware");
        if (cookwareLayer >= 0) go.layer = cookwareLayer;
        var cookware = Add<Cookware>(go);
        Set(cookware, "Def", def);
        Add<CookwareHeat>(go);
        Add<CookwareSpill>(go);
        Add<ChocolateSurfaceLook>(go);
        var cavity = Add<CookwareCavity>(go);
        Set(cavity, "_bottomLocal", bottom);
        Set(cavity, "_frameLocal", Quaternion.Inverse(go.transform.rotation) * worldFrame);
        Set(cavity, "_depth", depth);
        Set(cavity, "_bottomHalf", half);
        Set(cavity, "_topHalf", half);
        Set(cavity, "_bands", 1);
        Set(cavity, "_wall", .08f);
        Set(cavity, "_disableRootColliders", false);
        cavity.EditorBuild();
        Quiet(cookware);
        return cookware;
    }

    public static GameObject Model(string file, string rootName)
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Выйди из Play Mode.");
        string path = Models + file;
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer != null && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) throw new InvalidOperationException("Нет модели " + path);
        var go = new GameObject(rootName);
        Undo.RegisterCreatedObjectUndo(go, rootName);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(model, "Model");
        model.transform.SetParent(go.transform, false);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.UserAction);
        var seat = Find(model.transform, "SEAT1");
        if (seat != null && go.transform.InverseTransformPoint(seat.position).z < 0)
            model.transform.localRotation = Quaternion.Euler(0, 180, 0);
        return go;
    }

    public static void TruckSeat(NetworkVehicle v)
    {
        var source = Find(v.transform, "SEAT1");
        var position = source == null ? new Vector3(-.7f, 2.2f, 2.6f) : v.transform.InverseTransformPoint(source.position);
        var seat = Child(v.transform, "SeatPoint", position);
        var camera = Child(seat, "CameraPoint", Vector3.up * .8f);
        Seat(v, seat, Child(v.transform, "ExitPoint", new Vector3(-2.2f, .3f, position.z)), camera);
        var hull = Child(v.transform, "WorkshopChassis", new Vector3(0, 1, 0));
        Add<BoxCollider>(hull.gameObject).size = new Vector3(2.2f, .6f, 6);
    }

    public static List<Transform> TruckWheels(Transform root) =>
        root.GetComponentsInChildren<Transform>()
            .Where(t => (t.name.StartsWith("fortire") || t.name.StartsWith("tire.")) && t.GetComponent<Renderer>() != null)
            .ToList();

    public static void SavePrototype(GameObject go, string id)
    {
        Directory.CreateDirectory(Generated);
        AssetDatabase.Refresh();
        string path = Generated + id + ".prefab";
        if (File.Exists(path)) path = AssetDatabase.GenerateUniqueAssetPath(path);
        var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.UserAction);
        var networkObject = prefab.GetComponent<NetworkObject>();
        foreach (string guid in AssetDatabase.FindAssets("t:DefaultPrefabObjects"))
        {
            var list = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(AssetDatabase.GUIDToAssetPath(guid));
            if (list == null) continue;
            list.AddObject(networkObject, true);
            EditorUtility.SetDirty(list);
        }
        AssetDatabase.SaveAssets();
        Done(go, "Создан " + path + ". Сохрани сцену. Для хоста и клиента используй одинаковые prefab-списки.");
    }

    [Serializable] public class Blueprint { public Node[] nodes; }
    [Serializable] public class Node { public string name, parent, kind; public float[] position, size, rotation, color; }

    static Vector3 V(float[] values, Vector3 fallback) =>
        values != null && values.Length >= 3 ? new Vector3(values[0], values[1], values[2]) : fallback;

    public static GameObject BlueprintModel(string id)
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Выйди из Play Mode.");
        var data = JsonUtility.FromJson<Blueprint>(File.ReadAllText(Models + id + "_Prototype.json"));
        var nodes = new Dictionary<string, Transform>();
        GameObject root = null;
        Directory.CreateDirectory(Generated);
        AssetDatabase.Refresh();
        foreach (var node in data.nodes)
        {
            GameObject go;
            if (node.kind == "box") go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            else if (node.kind == "cylinder") go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            else go = new GameObject();
            go.name = node.name;
            Undo.RegisterCreatedObjectUndo(go, node.name);
            if (!string.IsNullOrEmpty(node.parent)) go.transform.SetParent(nodes[node.parent], false);
            else root = go;
            go.transform.localPosition = V(node.position, Vector3.zero);
            go.transform.localRotation = Quaternion.Euler(V(node.rotation, Vector3.zero));
            Vector3 size = V(node.size, Vector3.one);
            if (node.kind == "cylinder") size.y *= .5f;
            go.transform.localScale = size;

            if (go.TryGetComponent(out Collider collider)) UnityEngine.Object.DestroyImmediate(collider);
            if (go.TryGetComponent(out Renderer renderer))
            {
                Color color = node.color != null ? new Color(node.color[0], node.color[1], node.color[2], node.color[3]) : Color.gray;
                string path = Generated + "Mat_" + ColorUtility.ToHtmlStringRGBA(color) + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (shader == null) shader = Shader.Find("Standard");
                    material = new Material(shader);
                    material.color = color;
                    AssetDatabase.CreateAsset(material, path);
                }
                renderer.sharedMaterial = material;
            }
            nodes.Add(node.name, go.transform);
        }
        return root;
    }
}
#endif
