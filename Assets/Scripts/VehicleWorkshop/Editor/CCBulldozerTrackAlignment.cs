#if UNITY_EDITOR
using System;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using W = CCWorkshopEditor;

public static class CCBulldozerTrackAlignment
{
    [MenuItem("CatCooks/Vehicle Workshop/02E Align bulldozer belts to chassis")]
    public static void Align()
    {
        var selected = W.Selected();
        var drive = selected.GetComponentInParent<TrackedVehicle>();
        if (drive == null || drive.GetComponent<CCBulldozerBlade>() == null)
            throw new InvalidOperationException("Выдели корень бульдозера, настроенного командой 02C.");
        var root = drive.transform;
        var body = root.GetComponent<Rigidbody>();
        if (body == null) throw new InvalidOperationException("На корне бульдозера не найден Rigidbody.");
        var tracks = Find(root, "tracks");
        var group = Find(tracks, "WorkshopBelts");
        var a = Find(group, "DozerBelt_A");
        var b = Find(group, "DozerBelt_B");
        var suspension = Find(root, "WorkshopDozerSuspension");

        Bounds chassis = BoundsIn(root, tracks);
        Bounds boundsA = BoundsIn(root, a);
        Bounds boundsB = BoundsIn(root, b);
        var combined = boundsA;
        combined.Encapsulate(boundsB);
        // In the original FBX the combined belt bounds and the rigid undercarriage
        // bounds have the same center. The belts are slightly taller and longer.
        Vector3 offset = chassis.center - combined.center;
        bool aIsLeft = boundsA.center.x < boundsB.center.x;
        Bounds left = aIsLeft ? boundsA : boundsB;
        Bounds right = aIsLeft ? boundsB : boundsA;
        left.center += offset;
        right.center += offset;
        if (right.center.x - left.center.x < chassis.size.x * .3f)
            throw new InvalidOperationException("Не удалось разделить левую и правую ленты вдоль X корня машины.");

        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Align bulldozer belts");
        Undo.RecordObject(group, "Align belts to undercarriage");
        group.position += root.TransformVector(offset);
        PrefabUtility.RecordPrefabInstancePropertyModifications(group);

        Undo.RecordObject(drive, "Align bulldozer lugs and suspension");
        const float sag = .15f;
        drive.leftTrackPoints = SetPoints(root, suspension, left, "L", drive, sag);
        drive.rightTrackPoints = SetPoints(root, suspension, right, "R", drive, sag);
        drive.springStrength = body.mass * Physics.gravity.magnitude / (8 * sag);
        drive.springDamper = 1.7f * Mathf.Sqrt(drive.springStrength * body.mass / 8);
        var centerOfMass = new Vector3((left.center.x + right.center.x) * .5f,
            Mathf.Min(left.min.y, right.min.y) + root.InverseTransformVector(root.up * .5f).y,
            (left.center.z + right.center.z) * .5f);
        W.Set(drive, "_centerOfMass", centerOfMass);
        W.Set(drive, "_body", body);
        PrefabUtility.RecordPrefabInstancePropertyModifications(drive);
        EditorUtility.SetDirty(drive);
        W.Done(root.gameObject, "02E: ленты совмещены с ходовой, опоры пересчитаны. Проверь машину на ровном полу.");
    }

    private static Transform[] SetPoints(Transform root, Transform holder, Bounds track, string side,
        TrackedVehicle drive, float sag)
    {
        float meters = drive.restLength + drive.trackRadius - sag;
        float height = root.InverseTransformVector(root.up * meters).y;
        var points = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            Vector3 position = root.TransformPoint(new Vector3(track.center.x, track.min.y + height,
                Mathf.Lerp(track.min.z, track.max.z, .13f + i * .246667f)));
            points[i] = W.Child(holder, side + i, holder.InverseTransformPoint(position));
            PrefabUtility.RecordPrefabInstancePropertyModifications(points[i]);
        }
        return points;
    }

    private static Bounds BoundsIn(Transform root, Transform part)
    {
        var filter = part.GetComponent<MeshFilter>();
        var mesh = filter != null ? filter.sharedMesh : null;
        if (mesh == null) throw new InvalidOperationException("На " + part.name + " нужен видимый меш в MeshFilter.");
        Matrix4x4 matrix = root.worldToLocalMatrix * part.localToWorldMatrix;
        using (var data = MeshUtility.AcquireReadOnlyMeshData(mesh))
        using (var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp))
        {
            if (vertices.Length == 0) throw new InvalidOperationException("Меш " + part.name + " пуст.");
            data[0].GetVertices(vertices);
            var bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            for (int i = 1; i < vertices.Length; i++) bounds.Encapsulate(matrix.MultiplyPoint3x4(vertices[i]));
            return bounds;
        }
    }

    private static Transform Find(Transform root, string name)
    {
        var part = W.Find(root, name);
        if (part == null) throw new InvalidOperationException("В иерархии не найден " + name + ". Нужна версия после 02D.");
        return part;
    }
}
#endif
