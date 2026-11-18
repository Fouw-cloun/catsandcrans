#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class CCLargeExcavatorUpdate
{
    [MenuItem("CatCooks/Vehicle Workshop/01B Tune large excavator and align bucket piston")]
    public static void Apply()
    {
        var root = Selection.activeGameObject;
        if (EditorApplication.isPlaying || root == null || EditorUtility.IsPersistent(root))
        {
            Debug.LogWarning("Выйди из Play Mode и выдели корень экскаватора в Hierarchy.");
            return;
        }
        var vehicle = root.GetComponent<ExcavatorVehicle>();
        if (vehicle == null || !vehicle.stableSuspension)
        {
            Debug.LogWarning("Выбери большой экскаватор после команды 01 Repair selected excavator.", root);
            return;
        }

        Undo.RecordObject(vehicle, "Tune large excavator drive");
        vehicle.heavyTrackDrive = true;
        vehicle.maxForwardSpeed = 4.6f;
        vehicle.maxReverseSpeed = 3f;
        vehicle.tractionAcceleration = 2.6f;
        vehicle.reverseAcceleration = 2.35f;
        vehicle.throttleRise = 2.7f;
        vehicle.pivotAcceleration = 4.5f;
        vehicle.pivotSpeedDegrees = 42f;
        vehicle.pivotScrub = .10f;
        vehicle.pivotYawAssist = 45f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(vehicle);
        EditorUtility.SetDirty(vehicle);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);

        PistonLookAt old = null;
        foreach (var component in root.GetComponents<PistonLookAt>())
        {
            if (component.basePart != null && component.rodPart != null &&
                component.basePart.name == "bucket piston base" && component.rodPart.name == "bucket piston tip")
            {
                old = component;
                break;
            }
        }
        if (old == null || old.baseTarget == null || old.rodTarget == null)
        {
            Debug.LogWarning("Езда настроена. Для поршня заполни ссылки PistonLookAt пары bucket piston base / bucket piston tip и повтори 01B.", root);
            return;
        }
        Transform barrel = old.basePart, rod = old.rodPart;
        var barrelPin = old.rodTarget;
        var rodPin = old.baseTarget;
        if (barrelPin.IsChildOf(barrel) || barrelPin.IsChildOf(rod) ||
            rodPin.IsChildOf(barrel) || rodPin.IsChildOf(rod))
        {
            Debug.LogWarning("Езда настроена. Крепления поршня должны быть на рукояти и ковше, вне мешей поршня.", root);
            return;
        }

        var cylinder = root.GetComponent<CCBucketCylinder>();
        if (cylinder == null) cylinder = Undo.AddComponent<CCBucketCylinder>(root);
        Undo.RecordObject(cylinder, "Align bucket cylinder");
        cylinder.Bind(barrel, rod, barrelPin, rodPin,
            MeshAxis(barrel, rodPin.position), MeshAxis(rod, barrelPin.position));
        Undo.RecordObject(old, "Disable old bucket piston aiming");
        old.enabled = false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(old);
        PrefabUtility.RecordPrefabInstancePropertyModifications(cylinder);
        EditorUtility.SetDirty(cylinder);
        Debug.Log("01B: тяга и разворот настроены, поршень ковша перепривязан. Сохрани сцену и проверь в Play Mode.", root);
    }

    private static Vector3 MeshAxis(Transform part, Vector3 oppositePin)
    {
        Vector3 towardPin = part.InverseTransformPoint(oppositePin).normalized;
        var filter = part.GetComponentInChildren<MeshFilter>(true);
        var mesh = filter != null ? filter.sharedMesh : null;
        if (mesh == null) return towardPin;

        Matrix4x4 toPart = part.worldToLocalMatrix * filter.transform.localToWorldMatrix;
        Vector3 center = toPart.MultiplyPoint3x4(mesh.bounds.center);
        Vector3 outward = center.sqrMagnitude > .00000001f ? center.normalized : towardPin;
        if (outward.sqrMagnitude == 0) outward = Vector3.forward;

        // The model can be imported with Read/Write disabled. Fall back to the
        // direction from the end pivot to the mesh center if vertices are unavailable.
        Vector3[] vertices;
        try { vertices = mesh.vertices; }
        catch (UnityException) { return outward; }
        if (vertices == null || vertices.Length < 3) return outward;

        Vector3 mean = Vector3.zero;
        foreach (var vertex in vertices) mean += toPart.MultiplyPoint3x4(vertex);
        mean /= vertices.Length;
        Vector3 axis = outward;
        for (int i = 0; i < 16; i++)
        {
            Vector3 next = Vector3.zero;
            foreach (var vertex in vertices)
            {
                Vector3 offset = toPart.MultiplyPoint3x4(vertex) - mean;
                next += offset * Vector3.Dot(offset, axis);
            }
            if (next.sqrMagnitude < 1e-25f) return outward;
            axis = next / Mathf.Sqrt(next.sqrMagnitude);
        }
        return Vector3.Dot(axis, outward) < 0 ? -axis : axis;
    }
}
#endif
