#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using W = CCWorkshopEditor;

public static class CCBulldozerTrackRepair
{
    private const string ModelPath = W.Models + "Bulldozer_TrackBelts.fbx";
    private const string Group = "WorkshopBelts";

    [MenuItem("CatCooks/Vehicle Workshop/02D Repair bulldozer tracks and suspension")]
    public static void Install()
    {
        var selection = W.Selected();
        var drive = selection.GetComponentInParent<TrackedVehicle>();
        if (drive == null || drive.GetComponent<CCBulldozerBlade>() == null)
            throw new InvalidOperationException("Выдели корень бульдозера, настроенного командой 02C.");
        var root = drive.transform;
        var body = root.GetComponent<Rigidbody>();
        if (body == null) throw new InvalidOperationException("На корне бульдозера не найден Rigidbody.");

        var tracks = Required(root, "tracks");
        var oldA = Required(root, "track").GetComponent<Renderer>();
        var oldB = Required(root, "track.003").GetComponent<Renderer>();
        if (oldA == null || oldB == null)
            throw new InvalidOperationException("Не найдены исходные рендереры track и track.003.");

        var source = LoadSource();
        var sourceA = Required(source.transform, "CCDozerBelt_0").GetComponent<MeshFilter>();
        var sourceB = Required(source.transform, "CCDozerBelt_1").GetComponent<MeshFilter>();
        if (sourceA == null || sourceB == null || sourceA.sharedMesh == null || sourceB.sharedMesh == null)
            throw new InvalidOperationException("В файле TrackBelts отсутствует меш одной из лент.");

        // These chassis pivots are untouched by the blade setup, even if the
        // instance's root was reoriented or the FBX import scale differs.
        Matrix4x4 map = Frame(root, "") * Frame(source.transform, "CCTrackRef_").inverse;
        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Repair bulldozer belts");

        var holder = W.Child(tracks, Group, Vector3.zero);
        Undo.RecordObject(holder, "Align track group");
        holder.localRotation = Quaternion.identity;
        holder.localScale = Vector3.one;
        Directory.CreateDirectory(W.Generated);
        AssetDatabase.Refresh();

        var a = AddBelt(holder, sourceA, oldA, map, "DozerBelt_A");
        var b = AddBelt(holder, sourceB, oldB, map, "DozerBelt_B");
        // Use the new static meshes. Skinned renderer bounds can include bone
        // offsets from the detached pose and are unsuitable for suspension.
        Bounds boundsA = BoundsInChassis(root, a.GetComponent<MeshFilter>());
        Bounds boundsB = BoundsInChassis(root, b.GetComponent<MeshFilter>());
        bool aIsLeft = boundsA.center.x < boundsB.center.x;
        Bounds left = aIsLeft ? boundsA : boundsB;
        Bounds right = aIsLeft ? boundsB : boundsA;

        var suspension = W.Child(root, "WorkshopDozerSuspension", Vector3.zero);
        Undo.RecordObject(drive, "Move suspension onto track contact patches");
        const float sag = .15f;
        drive.leftTrackPoints = Points(drive, suspension, left, "L", sag);
        drive.rightTrackPoints = Points(drive, suspension, right, "R", sag);
        drive.springStrength = body.mass * Physics.gravity.magnitude / (8 * sag);
        drive.springDamper = 1.7f * Mathf.Sqrt(drive.springStrength * body.mass / 8);
        W.Set(drive, "_centerOfMass", new Vector3(
            (left.center.x + right.center.x) * .5f,
            Mathf.Min(left.min.y, right.min.y) + root.InverseTransformVector(root.up * .5f).y,
            (left.center.z + right.center.z) * .5f));

        foreach (Renderer old in new[] { oldA, oldB })
        {
            Undo.RecordObject(old, "Disable detached belt rendering");
            old.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(old);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(drive);
        EditorUtility.SetDirty(drive);
        AssetDatabase.SaveAssets();
        W.Done(root.gameObject, "02D: ленты закреплены в tracks/WorkshopBelts. Обновлены восемь точек подвески.");
    }

    private static GameObject LoadSource()
    {
        string path = ModelPath;
        if (!File.Exists(path))
        {
            foreach (string guid in AssetDatabase.FindAssets("Bulldozer_TrackBelts"))
            {
                string candidate = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(candidate) != "Bulldozer_TrackBelts.fbx") continue;
                path = candidate;
                break;
            }
        }
        if (!File.Exists(path)) throw new InvalidOperationException("Скопируй Assets из 02D, включая " + ModelPath);
        if (!(AssetImporter.GetAtPath(path) is ModelImporter))
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer != null && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null) throw new InvalidOperationException("Unity не импортировала " + path + ". Проверь Console.");
        return model;
    }

    private static MeshRenderer AddBelt(Transform holder, MeshFilter source, Renderer old, Matrix4x4 map, string name)
    {
        var target = W.Child(holder, name, Vector3.zero);
        Undo.RecordObject(target, "Align new track mesh");
        target.localRotation = Quaternion.identity;
        target.localScale = Vector3.one;
        var filter = W.Add<MeshFilter>(target.gameObject);
        var renderer = W.Add<MeshRenderer>(target.gameObject);
        if (filter.sharedMesh == null)
        {
            Matrix4x4 local = target.worldToLocalMatrix * map * source.transform.localToWorldMatrix;
            var mesh = UnityEngine.Object.Instantiate(source.sharedMesh);
            mesh.name = name;
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = local.MultiplyPoint3x4(vertices[i]);
            mesh.vertices = vertices;
            if (local.determinant < 0)
            {
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var triangles = mesh.GetTriangles(sub);
                    for (int i = 0; i < triangles.Length; i += 3)
                        (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
                    mesh.SetTriangles(triangles, sub);
                }
            }
            var normals = mesh.normals;
            if (normals.Length == vertices.Length)
            {
                Matrix4x4 normalMatrix = local.inverse.transpose;
                for (int i = 0; i < normals.Length; i++)
                    normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
                mesh.normals = normals;
            }
            else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(W.Generated + name + ".asset"));
            Undo.RecordObject(filter, "Assign bulldozer belt mesh");
            filter.sharedMesh = mesh;
        }
        Undo.RecordObject(renderer, "Copy track materials");
        renderer.sharedMaterials = old.sharedMaterials;
        renderer.shadowCastingMode = old.shadowCastingMode;
        renderer.receiveShadows = old.receiveShadows;
        return renderer;
    }

    private static Bounds BoundsInChassis(Transform root, MeshFilter filter)
    {
        Matrix4x4 local = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
        var vertices = filter.sharedMesh.vertices;
        if (vertices.Length == 0) throw new InvalidOperationException("Меш ленты пуст.");
        var bounds = new Bounds(local.MultiplyPoint3x4(vertices[0]), Vector3.zero);
        for (int i = 1; i < vertices.Length; i++) bounds.Encapsulate(local.MultiplyPoint3x4(vertices[i]));
        return bounds;
    }

    private static Transform[] Points(TrackedVehicle drive, Transform holder, Bounds track, string side, float sag)
    {
        float support = drive.restLength + drive.trackRadius - sag;
        float height = holder.InverseTransformVector(drive.transform.up * support).y;
        var points = new Transform[4];
        for (int i = 0; i < points.Length; i++)
            points[i] = W.Child(holder, side + i, new Vector3(track.center.x, track.min.y + height,
                Mathf.Lerp(track.min.z, track.max.z, .13f + i * .246667f)));
        return points;
    }

    private static Matrix4x4 Frame(Transform root, string prefix)
    {
        Vector3 origin = Required(root, prefix + "hull").position;
        Vector3 x = Required(root, prefix + "door").position - origin;
        Vector3 y = Required(root, prefix + "cabin").position - origin;
        Vector3 z = Required(root, prefix + "pipe").position - origin;
        var frame = new Matrix4x4(new Vector4(x.x, x.y, x.z, 0), new Vector4(y.x, y.y, y.z, 0),
            new Vector4(z.x, z.y, z.z, 0), new Vector4(origin.x, origin.y, origin.z, 1));
        if (Mathf.Abs(frame.determinant) < x.magnitude * y.magnitude * z.magnitude * .01f || frame.determinant == 0)
            throw new InvalidOperationException("Не удалось совместить точки корпуса.");
        return frame;
    }

    private static Transform Required(Transform root, string name)
    {
        var part = W.Find(root, name);
        if (part == null) throw new InvalidOperationException("Не найдена деталь " + name);
        return part;
    }
}
#endif
