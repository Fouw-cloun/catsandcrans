#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using W = CCWorkshopEditor;

public static class CCConcreteTruckSetup
{
    private const string DefaultModel = "Assets/TerrainPrefs/Concrete truck.fbx";
    private const string DrumName = "concrete container";
    private const string ReplacementName = "ConcreteDrum_Centered";

    private sealed class DrumFit
    {
        public Vector3 Center;
        public Vector3 Axis;
        public Vector2[] Profile;
    }

    [MenuItem("CatCooks/Vehicle Workshop/03 Create corrected concrete truck")]
    public static void Create()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Выйди из Play Mode.");
        var model = FindModel();
        var root = new GameObject("ConcreteTruck_Workshop");
        Undo.RegisterCreatedObjectUndo(root, "Create concrete truck");
        try
        {
            var artwork = (GameObject)PrefabUtility.InstantiatePrefab(model);
            Undo.RegisterCreatedObjectUndo(artwork, "Concrete truck model");
            artwork.transform.SetParent(root.transform, false);
            PrefabUtility.UnpackPrefabInstance(artwork, PrefabUnpackMode.Completely, InteractionMode.UserAction);
            var seat = W.Find(artwork.transform, "SEAT1");
            if (seat != null && root.transform.InverseTransformPoint(seat.position).z < 0)
                artwork.transform.localRotation = Quaternion.Euler(0, 180, 0);

            Transform drum = RequiredDrum(root.transform);
            Transform pivot = ConfigureDrum(drum, true);
            var truck = W.Add<CCConcreteTruckVehicle>(root);
            truck.drumPivot = pivot;
            W.Wheels(truck, W.TruckWheels(root.transform), 6500);
            W.TruckSeat(truck);

            Bounds bounds = W.BoundsIn(root.transform, new[] { drum.GetComponent<Renderer>() });
            var volume = W.Child(root.transform, "DrumContents",
                new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * .13f, bounds.center.z));
            truck.cookware = W.Container(volume.gameObject, "ConcreteDrum", Vector3.zero, root.transform.rotation,
                new Vector2(bounds.extents.x * .63f, bounds.extents.z * .60f), bounds.size.y * .43f, 320);
            CCConcreteTruckRearSetup.Setup(truck);
            W.SavePrototype(root, "ConcreteTruck_Workshop");
        }
        catch
        {
            if (root != null) Undo.DestroyObjectImmediate(root);
            throw;
        }
    }

    [MenuItem("CatCooks/Vehicle Workshop/03 Repair selected concrete drum pivot")]
    public static void RepairPivot()
    {
        var selected = W.Selected();
        var drum = RequiredDrum(selected.transform);
        var pivot = ConfigureDrum(drum, false);
        BindExistingController(pivot);
        W.Done(pivot.gameObject, "DrumAxis построена. Продольная ось вращения — локальная Z.");
    }

    [MenuItem("CatCooks/Vehicle Workshop/03A Replace selected concrete drum only")]
    public static void ReplaceDrum()
    {
        var selected = W.Selected();
        var drum = RequiredDrum(selected.transform);
        var pivot = ConfigureDrum(drum, true);
        BindExistingController(pivot);
        W.Done(pivot.gameObject, "03A: новый барабан установлен. Вращение — вокруг локальной Z объекта DrumAxis.");
    }

    private static Transform ConfigureDrum(Transform drum, bool replace)
    {
        var filter = drum.GetComponent<MeshFilter>();
        Transform existingPivot = drum.parent != null && drum.parent.name == "DrumAxis" ? drum.parent : null;
        if (existingPivot != null && filter.sharedMesh.name == ReplacementName)
            return existingPivot;

        DrumFit fit = FitToChassis(drum);

        var instance = PrefabUtility.GetOutermostPrefabInstanceRoot(drum.gameObject);
        if (instance != null)
        {
            if (W.Find(instance.transform, "SEAT1") == null)
                throw new InvalidOperationException("Сначала открой отдельный экземпляр бетоновоза.");
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.UserAction);
        }

        if (existingPivot != null)
        {
            // Preserve the old shell's world pose while updating the rotation axis.
            Undo.SetTransformParent(drum, existingPivot.parent, "Reset original drum axis");
            Undo.RecordObject(existingPivot, "Fit centered drum pivot");
            SetPivotPose(existingPivot, fit.Center, fit.Axis);
            Undo.SetTransformParent(drum, existingPivot, "Mount drum onto centered axis");
        }
        var pivot = existingPivot != null ? existingPivot : InsertPivot(drum, fit.Center, fit.Axis);
        if (replace) ReplaceMesh(filter, fit);
        return pivot;
    }

    private static void BindExistingController(Transform pivot)
    {
        var truck = pivot.GetComponentInParent<CCConcreteTruckVehicle>();
        if (truck == null) return;
        Undo.RecordObject(truck, "Assign centered drum axis");
        truck.drumPivot = pivot;
        PrefabUtility.RecordPrefabInstancePropertyModifications(truck);
    }

    private static GameObject FindModel()
    {
        var fb = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultModel);
        if (fb != null) return fb;
        foreach (string guid in AssetDatabase.FindAssets("Concrete truck"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase)) continue;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model != null && W.Find(model.transform, DrumName) != null && W.Find(model.transform, "SEAT1") != null)
                return model;
        }
        throw new InvalidOperationException("Импортируй исходный Concrete truck.fbx. Обычный путь: " + DefaultModel);
    }

    private static Transform RequiredDrum(Transform selected)
    {
        var drum = W.Find(selected, DrumName);
        if (drum == null || drum.GetComponent<MeshFilter>() == null ||
            drum.GetComponent<MeshFilter>().sharedMesh == null || drum.GetComponent<Renderer>() == null)
            throw new InvalidOperationException("Не найден меш concrete container. Выдели барабан или корень бетоновоза.");
        return drum;
    }

    private static Transform InsertPivot(Transform drum, Vector3 center, Vector3 axis)
    {
        var node = new GameObject("DrumAxis");
        Undo.RegisterCreatedObjectUndo(node, "Centered drum pivot");
        node.transform.SetParent(drum.parent, false);
        SetPivotPose(node.transform, center, axis);
        Undo.SetTransformParent(drum, node.transform, "Preserve drum pose under axis");
        return node.transform;
    }

    private static Quaternion AxisRotation(Vector3 axis)
    {
        Vector3 up = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > .98f ? Vector3.right : Vector3.up;
        return Quaternion.LookRotation(axis, up);
    }

    private static void SetPivotPose(Transform pivot, Vector3 center, Vector3 axis)
    {
        pivot.SetPositionAndRotation(center, AxisRotation(axis));
    }

    private static DrumFit FitToChassis(Transform drum)
    {
        Transform chassis = drum.parent;
        Transform seat = null;
        while (chassis != null)
        {
            seat = W.Find(chassis, "SEAT1");
            if (seat != null) break;
            chassis = chassis.parent;
        }

        Vector3 up = Vector3.up;
        Vector3 rear = RearDirection(drum, chassis, seat, up);
        const float slopeDegrees = 15.1f;
        float slope = slopeDegrees * Mathf.Deg2Rad;
        Vector3 axis = rear * Mathf.Cos(slope) + up * Mathf.Sin(slope);
        Quaternion frame = AxisRotation(axis);
        var mesh = drum.GetComponent<MeshFilter>().sharedMesh;
        Matrix4x4 toFrame = Matrix4x4.TRS(drum.position, frame, Vector3.one).inverse *
            drum.localToWorldMatrix;

        Bounds envelope;
        using (var data = MeshUtility.AcquireReadOnlyMeshData(mesh))
        using (var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp))
        {
            if (vertices.Length == 0) throw new InvalidOperationException("Меш барабана пуст.");
            data[0].GetVertices(vertices);
            envelope = new Bounds(toFrame.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            for (int i = 1; i < vertices.Length; i++)
                envelope.Encapsulate(toFrame.MultiplyPoint3x4(vertices[i]));
        }

        float length = envelope.size.z;
        // A round shell fitting inside both measured transverse dimensions.
        float radius = Mathf.Min(envelope.extents.x, envelope.extents.y);
        if (length < .05f || radius < .02f)
            throw new InvalidOperationException("У выбранной детали слишком малы габариты. Проверь, что это меш барабана.");
        var design = new[]
        {
            new Vector2(0f, .57f), new Vector2(.05f, .61f),
            new Vector2(.23f, .91f), new Vector2(.32f, 1f),
            new Vector2(.61f, 1f), new Vector2(.68f, .95f),
            new Vector2(.91f, .48f), new Vector2(.97f, .42f),
            new Vector2(1f, .42f)
        };
        for (int i = 0; i < design.Length; i++)
            design[i] = new Vector2((design[i].x - .5f) * length, design[i].y * radius);

        return new DrumFit { Center = drum.position + frame * envelope.center, Axis = axis, Profile = design };
    }

    private static Vector3 RearDirection(Transform drum, Transform chassis, Transform seat, Vector3 up)
    {
        if (chassis != null)
        {
            Vector3 front = Vector3.zero, back = Vector3.zero;
            int frontCount = 0, backCount = 0;
            foreach (var wheel in W.TruckWheels(chassis))
            {
                Vector3 point = wheel.GetComponent<Renderer>().bounds.center;
                if (wheel.name.StartsWith("fortire", StringComparison.OrdinalIgnoreCase))
                {
                    front += point;
                    frontCount++;
                }
                else
                {
                    back += point;
                    backCount++;
                }
            }
            if (frontCount > 0 && backCount > 0)
            {
                Vector3 fromAxles = Vector3.ProjectOnPlane(back / backCount - front / frontCount, up);
                if (fromAxles.sqrMagnitude > .0001f) return fromAxles.normalized;
            }
        }
        if (seat != null)
        {
            Vector3 fromCab = Vector3.ProjectOnPlane(drum.GetComponent<Renderer>().bounds.center - seat.position, up);
            if (fromCab.sqrMagnitude > .0001f)
            {
                // Snap the offset of the driver's seat to the chassis axes.
                Vector3 forward = Vector3.ProjectOnPlane(chassis.forward, up).normalized;
                Vector3 right = Vector3.ProjectOnPlane(chassis.right, up).normalized;
                float f = Vector3.Dot(fromCab, forward), r = Vector3.Dot(fromCab, right);
                return Mathf.Abs(f) >= Mathf.Abs(r) ? forward * Mathf.Sign(f) : right * Mathf.Sign(r);
            }
        }
        if (drum.parent != null)
        {
            Vector3 parentForward = Vector3.ProjectOnPlane(drum.parent.forward, up);
            if (parentForward.sqrMagnitude > .0001f) return -parentForward.normalized;
        }
        return Vector3.back;
    }

    private static void ReplaceMesh(MeshFilter filter, DrumFit fit)
    {
        var profile = new List<Vector2>(fit.Profile.Length);
        foreach (var ring in fit.Profile)
        {
            if (profile.Count > 0 && ring.x - profile[profile.Count - 1].x < .0001f)
            {
                if (ring.y > profile[profile.Count - 1].y) profile[profile.Count - 1] = ring;
                continue;
            }
            profile.Add(ring);
        }
        if (profile.Count < 3) throw new InvalidOperationException("Недостаточно сечений для нового барабана.");

        const int sides = 24;
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        var uv = new List<Vector2>();
        Vector2 front = profile[0];
        Vector2 rear = profile[profile.Count - 1];
        float length = rear.x - front.x;
        float wall = Mathf.Min(rear.y * .18f, length * .025f);
        Matrix4x4 toLocal = filter.transform.worldToLocalMatrix *
            Matrix4x4.TRS(fit.Center, AxisRotation(fit.Axis), Vector3.one);
        for (int i = 0; i < profile.Count; i++)
            AppendRing(profile[i].x, profile[i].y);
        for (int i = 0; i < profile.Count - 1; i++)
            Connect(i * (sides + 1), (i + 1) * (sides + 1));

        // Beveled rear rim and an inset return. The visual shell is open.
        int opening = vertices.Count;
        AppendRing(rear.x, Mathf.Max(rear.y - wall, rear.y * .72f));
        int throat = vertices.Count;
        AppendRing(rear.x - wall * 2, Mathf.Max(rear.y - wall * 1.4f, rear.y * .66f));
        Connect((profile.Count - 1) * (sides + 1), opening);
        Connect(opening, throat);
        int frontCenter = vertices.Count;
        vertices.Add(toLocal.MultiplyPoint3x4(new Vector3(0, 0, front.x)));
        uv.Add(new Vector2(.5f, 0));
        for (int j = 0; j < sides; j++) Triangle(frontCenter, j + 1, j);

        if (toLocal.determinant < 0)
            for (int i = 0; i < indices.Count; i += 3)
                (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);

        var mesh = new Mesh { name = ReplacementName };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(indices, 0);
        mesh.SetUVs(0, uv);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        Directory.CreateDirectory(W.Generated);
        AssetDatabase.Refresh();
        string path = AssetDatabase.GenerateUniqueAssetPath(W.Generated + ReplacementName + ".asset");
        AssetDatabase.CreateAsset(mesh, path);
        Undo.RecordObject(filter, "Replace concrete drum mesh");
        filter.sharedMesh = mesh;
        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
        AssetDatabase.SaveAssets();

        void AppendRing(float z, float radius)
        {
            for (int j = 0; j <= sides; j++)
            {
                float u = j / (float)sides;
                float angle = u * 2 * Mathf.PI;
                vertices.Add(toLocal.MultiplyPoint3x4(new Vector3(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle), z)));
                uv.Add(new Vector2(u, (z - front.x) / length));
            }
        }

        void Connect(int a, int b)
        {
            for (int j = 0; j < sides; j++)
            {
                Triangle(a + j, a + j + 1, b + j);
                Triangle(a + j + 1, b + j + 1, b + j);
            }
        }

        void Triangle(int a, int b, int c) { indices.Add(a); indices.Add(b); indices.Add(c); }
    }

}
#endif
