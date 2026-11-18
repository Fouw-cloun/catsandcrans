#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using W = CCWorkshopEditor;

public static class CCDumpTruckFeedbackSetup
{
    private static readonly string[] Wheels =
        { "fortire left", "fortire right", "tire.001", "tire.002", "tire.003", "tire.004" };
    private const string WheelAsset = W.Models + "DumpTruck_WheelGeometry.json";
    private const float StockRadius = .652f;

    [Serializable] private sealed class WheelsFile { public Tire[] wheels; }
    [Serializable] private sealed class Tire
    {
        public string name;
        public Vector3 center;
        public Vector3[] vertices, normals;
        public Vector2[] uv;
    }

    [MenuItem("CatCooks/Vehicle Workshop/04D Rebuild dump truck wheels at stock scale")]
    public static void Install()
    {
        var selected = W.Selected();
        var truck = selected.GetComponentInParent<DumpTruckVehicle>();
        if (truck == null || truck.tailgate == null)
            throw new InvalidOperationException("Выдели корень настроенного самосвала.");
        string result = CCDumpTruckRideSetup.Apply(truck, true);
        W.Done(truck.gameObject, "04D: восстановлены шесть опор и четыре задних меша. " + result);
    }

    internal static void Configure(DumpTruckVehicle truck)
    {
        var root = truck.transform;
        var text = AssetDatabase.LoadAssetAtPath<TextAsset>(WheelAsset);
        if (text == null) throw new InvalidOperationException("Не найден " + WheelAsset);
        var blueprint = JsonUtility.FromJson<WheelsFile>(text.text);
        if (blueprint?.wheels == null || blueprint.wheels.Length != Wheels.Length)
            throw new InvalidOperationException("Файл исходных колёс повреждён.");

        var scene = new MeshRenderer[6];
        var old = new WheeledVehicle.Wheel[6];
        var used = new HashSet<WheeledVehicle.Wheel>();
        for (int i = 0; i < Wheels.Length; i++)
        {
            var spec = blueprint.wheels[i];
            if (spec == null || spec.name != Wheels[i] || spec.vertices == null ||
                spec.vertices.Length == 0 || spec.vertices.Length % 3 != 0)
                throw new InvalidOperationException("Неполная геометрия колеса " + Wheels[i]);
            scene[i] = RequirePart(root, Wheels[i]).GetComponent<MeshRenderer>();
            if (scene[i] == null) throw new InvalidOperationException("Нет MeshRenderer у " + Wheels[i]);
            old[i] = FindExisting(truck, scene[i].transform, used);
            if (old[i] != null) used.Add(old[i]);
        }

        Vector3 frontLeft = scene[0].bounds.center;
        Vector3 frontRight = scene[1].bounds.center;
        Vector3 frontMid = (frontLeft + frontRight) * .5f;
        // The visible front axle is the reference. Avoid using an affine fit
        // that can skew units when different FBX hierarchies are compared.
        Vector3 oldTrack = frontRight - frontLeft;
        float signedWidth = Vector3.Dot(oldTrack, root.right);
        Vector3 right = root.right * (signedWidth >= 0 ? 1f : -1f);
        float originalWidth = blueprint.wheels[1].center.x - blueprint.wheels[0].center.x;
        float scale = Mathf.Abs(signedWidth) / originalWidth;
        if (scale < .65f || scale > 1.5f)
        {
            scale = 1;
            Debug.LogWarning("04D: передняя колея отличается от исходной; использован штатный радиус 0,65 м.", truck);
        }

        Vector3 stockMid = (blueprint.wheels[0].center + blueprint.wheels[1].center) * .5f;
        var frame = new Matrix4x4(
            new Vector4(right.x * scale, right.y * scale, right.z * scale, 0),
            new Vector4(root.up.x * scale, root.up.y * scale, root.up.z * scale, 0),
            new Vector4(root.forward.x * scale, root.forward.y * scale, root.forward.z * scale, 0),
            new Vector4(0, 0, 0, 1));
        float radius = StockRadius * scale;

        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Restore dump truck wheels");
        Undo.RecordObject(truck, "Rebind clean wheel anchors");
        truck.wheelRadius = radius;
        const float sag = .14f;
        var anchors = W.Child(root, "WorkshopCleanWheelAnchors", Vector3.zero);
        var visuals = W.Child(root, "WorkshopCleanWheelVisuals", Vector3.zero);
        var rear = new Transform[4];
        var wheels = new WheeledVehicle.Wheel[6];

        Directory.CreateDirectory(W.Generated);
        AssetDatabase.Refresh();
        for (int i = 0; i < Wheels.Length; i++)
        {
            Vector3 center = frontMid + frame.MultiplyVector(blueprint.wheels[i].center - stockMid);
            Vector3 mount = center + root.up * (truck.restLength - sag);
            var anchor = W.Child(anchors, "Anchor_" + Wheels[i], anchors.InverseTransformPoint(mount));
            Transform visual;
            if (i < 2 && old[i]?.visual != null)
            {
                visual = old[i].visual;
            }
            else
            {
                visual = W.Child(visuals, "Centered_" + Wheels[i], visuals.InverseTransformPoint(center));
                visual.rotation = root.rotation;
                InstallVisual(visual, blueprint.wheels[i], scene[i], frame, Wheels[i]);
                HideOldWheel(scene[i], old[i]?.visual);
            }
            wheels[i] = new WheeledVehicle.Wheel
            {
                anchor = anchor,
                visual = visual,
                steering = i < 2,
                motorized = old[i] == null || old[i].motorized,
                spinAxisLocal = i < 2 && old[i] != null ? old[i].spinAxisLocal : Vector3.right,
                visualOffset = i < 2 && old[i] != null ? old[i].visualOffset : Vector3.zero
            };
            if (i >= 2) rear[i - 2] = visual;
        }

        var oldSound = truck.tailgate.Find("WorkshopGateClang");
        if (oldSound != null && oldSound.TryGetComponent(out AudioSource sound))
            Undo.DestroyObjectImmediate(sound);

        truck.wheels = wheels;
        truck.rearWheelVisuals = rear;
        EditorUtility.SetDirty(truck);
        PrefabUtility.RecordPrefabInstancePropertyModifications(truck);
    }

    private static void HideOldWheel(MeshRenderer renderer, Transform previousPivot)
    {
        Undo.RecordObject(renderer, "Hide misaligned wheel renderer");
        renderer.enabled = false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        DisableColliders(renderer.transform);
        if (previousPivot != null && (previousPivot.name.StartsWith("WheelPivot_", StringComparison.Ordinal) ||
            previousPivot.name.StartsWith("WorkshopRearSpin_", StringComparison.Ordinal)))
            DisableColliders(previousPivot);
    }

    private static void DisableColliders(Transform part)
    {
        foreach (var collider in part.GetComponentsInChildren<Collider>(true))
        {
            Undo.RecordObject(collider, "Disable old moving wheel collider");
            collider.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
        }
    }

    private static void InstallVisual(Transform pivot, Tire stock, MeshRenderer source, Matrix4x4 frame, string label)
    {
        var filter = W.Add<MeshFilter>(pivot.gameObject);
        var renderer = W.Add<MeshRenderer>(pivot.gameObject);
        if (filter.sharedMesh == null || filter.sharedMesh.name != "DumpTruck_Stock_" + label)
        {
            Mesh mesh = MakeMesh(stock, pivot.worldToLocalMatrix * frame);
            mesh.name = "DumpTruck_Stock_" + label;
            string path = AssetDatabase.GenerateUniqueAssetPath(W.Generated + mesh.name + ".asset");
            AssetDatabase.CreateAsset(mesh, path);
            Undo.RecordObject(filter, "Assign stock wheel geometry");
            filter.sharedMesh = mesh;
        }
        Undo.RecordObject(renderer, "Restore wheel material and visibility");
        renderer.enabled = true;
        renderer.sharedMaterials = source.sharedMaterials;
        renderer.shadowCastingMode = source.shadowCastingMode;
        renderer.receiveShadows = source.receiveShadows;
    }

    private static WheeledVehicle.Wheel FindExisting(DumpTruckVehicle truck, Transform tire,
        HashSet<WheeledVehicle.Wheel> used)
    {
        if (truck.wheels == null) return null;
        foreach (var wheel in truck.wheels)
        {
            if (wheel == null || used.Contains(wheel)) continue;
            if (wheel.anchor != null && wheel.anchor.name == "Anchor_" + tire.name) return wheel;
            if (wheel.visual != null && (wheel.visual == tire || tire.IsChildOf(wheel.visual))) return wheel;
        }
        return null;
    }

    private static Mesh MakeMesh(Tire tire, Matrix4x4 intoLocal)
    {
        var points = new Vector3[tire.vertices.Length];
        var normals = new Vector3[points.Length];
        var triangles = new int[points.Length];
        var normalMap = intoLocal.inverse.transpose;
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = intoLocal.MultiplyVector(tire.vertices[i]);
            triangles[i] = i;
            if (tire.normals != null && tire.normals.Length == points.Length)
                normals[i] = normalMap.MultiplyVector(tire.normals[i]).normalized;
        }
        if (intoLocal.determinant < 0)
            for (int i = 0; i < triangles.Length; i += 3)
                (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);

        var mesh = new Mesh { vertices = points, triangles = triangles };
        if (tire.uv != null && tire.uv.Length == points.Length) mesh.uv = tire.uv;
        if (tire.normals != null && tire.normals.Length == points.Length) mesh.normals = normals;
        else mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static Transform RequirePart(Transform root, string name)
    {
        var part = W.Find(root, name);
        if (part == null) throw new InvalidOperationException("На модели не найдено " + name);
        return part;
    }
}
#endif
