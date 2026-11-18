#if UNITY_EDITOR
using System;
using System.IO;
using CatCooks.Food;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using W = CCWorkshopEditor;

public static class CCBulldozerCompactSetup
{
    private const string ModelPath = W.Models + "Bulldozer_Compact.fbx";
    private const string MeshName = "WorkshopBulldozerCompactBucket";

    [MenuItem("CatCooks/Vehicle Workshop/02B Fit compact bulldozer bucket")]
    public static void Install()
    {
        var selected = W.Selected();
        var shield = selected.name == "shield" ? selected.transform : W.Find(selected.transform, "shield");
        var filter = shield != null ? shield.GetComponent<MeshFilter>() : null;
        if (filter == null || filter.sharedMesh == null) throw new InvalidOperationException("Выдели shield с MeshFilter.");
        var network = shield.GetComponentInParent<NetworkObject>();
        if (network == null || network.transform == shield)
            throw new InvalidOperationException("На родительском корне бульдозера нужен NetworkObject.");

        string modelPath = FindCompactPath();
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null)
        {
            AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
            importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        }
        if (importer != null && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
            throw new InvalidOperationException("Unity не импортировала " + modelPath + ". Открой FBX в Project и проверь ошибку импортера в Console.");

        MeshFilter prefabFilter = PrefabUtility.GetCorrespondingObjectFromOriginalSource(filter);
        string originalPath = AssetDatabase.GetAssetPath(prefabFilter != null ? prefabFilter.sharedMesh : filter.sharedMesh);
        var original = FindOriginal(originalPath, modelPath);
        if (original == null)
            throw new InvalidOperationException("Нужен исходный FBX «бульдозер (2)». Импортируй его и оставь в проекте.");

        var sourceShield = W.Find(model.transform, "shield");
        var originalShield = W.Find(original.transform, "shield");
        if (sourceShield == null || originalShield == null || sourceShield.GetComponent<MeshFilter>() == null)
            throw new InvalidOperationException("В исходной или компактной модели нет детали shield.");

        // Match four unchanged chassis pivots, so FBX axis and unit settings may differ.
        Matrix4x4 alignment = Frame(original.transform) * Frame(model.transform).inverse;
        Matrix4x4 toLocal = originalShield.worldToLocalMatrix * alignment * sourceShield.localToWorldMatrix;
        Vector3 floor = Point(sourceShield, shield, toLocal, "CCBucket_Floor");
        Vector3 floorFront = Point(sourceShield, shield, toLocal, "CCBucket_FloorFront");
        Vector3 right = Point(sourceShield, shield, toLocal, "CCBucket_Right") - floor;
        Vector3 rim = Point(sourceShield, shield, toLocal, "CCBucket_Rim");
        Vector3 rimFront = Point(sourceShield, shield, toLocal, "CCBucket_RimFront");
        Vector3 forward = (floorFront - floor).normalized;
        Vector3 up = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(rim - floor, forward), right.normalized).normalized;
        if (up.sqrMagnitude < .5f || forward.sqrMagnitude < .5f)
            throw new InvalidOperationException("Не удалось определить направление полости ковша.");
        Quaternion frame = Quaternion.LookRotation(forward, up);
        float depth = Vector3.Dot(rim - floor, up);
        var bottomHalf = new Vector2(right.magnitude, Vector3.Distance(floor, floorFront));
        var topHalf = new Vector2(right.magnitude, Vector3.Distance(rim, rimFront));
        var bottom = shield.InverseTransformPoint(floor);

        var cookware = shield.GetComponent<Cookware>();
        if (cookware == null)
            cookware = W.Container(shield.gameObject, "BulldozerBucket", bottom, frame, bottomHalf, depth, 120);
        var cavity = W.Add<CookwareCavity>(shield.gameObject);
        var serialized = new SerializedObject(cavity);
        serialized.FindProperty("_shape").enumValueIndex = 0;
        serialized.ApplyModifiedProperties();
        W.Set(cavity, "_bottomLocal", bottom);
        W.Set(cavity, "_frameLocal", Quaternion.Inverse(shield.rotation) * frame);
        W.Set(cavity, "_bottomHalf", bottomHalf);
        W.Set(cavity, "_topHalf", topHalf);
        W.Set(cavity, "_topShift", new Vector2(0, Vector3.Dot(rim - floor, forward)));
        W.Set(cavity, "_depth", depth);
        W.Set(cavity, "_wall", depth * .275f);
        W.Set(cavity, "_bands", 2);
        W.Set(cavity, "_makeColliders", true);
        W.Set(cavity, "_disableRootColliders", false);
        cavity.EditorBuild();

        foreach (var collider in shield.GetComponents<Collider>())
        {
            if (collider.isTrigger) continue;
            Undo.RecordObject(collider, "Disable old solid shield collider");
            collider.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
        }

        if (filter.sharedMesh.name != MeshName)
        {
            var mesh = CopyMesh(sourceShield.GetComponent<MeshFilter>().sharedMesh, toLocal);
            Directory.CreateDirectory(W.Generated);
            AssetDatabase.Refresh();
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(W.Generated + MeshName + ".asset"));
            Undo.RecordObject(filter, "Install compact bulldozer bucket");
            filter.sharedMesh = mesh;
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
        }
        AssetDatabase.SaveAssets();
        W.Done(shield.gameObject, "02B: компактный ковш установлен. Проверь Receive и сохрани сцену.");
    }

    private static string FindCompactPath()
    {
        if (File.Exists(ModelPath)) return ModelPath;
        string path = null;
        foreach (string guid in AssetDatabase.FindAssets("Bulldozer_Compact"))
        {
            string candidate = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.Equals(Path.GetFileName(candidate), "Bulldozer_Compact.fbx", StringComparison.OrdinalIgnoreCase))
                continue;
            if (path != null)
                throw new InvalidOperationException("Найдено несколько Bulldozer_Compact.fbx. Оставь одну копию или положи нужную в " + ModelPath);
            path = candidate;
        }
        if (path != null) return path;
        throw new InvalidOperationException("В Project не найден Bulldozer_Compact.fbx. Распакуй Assets из 02C. Ожидаемый путь: " + ModelPath);
    }

    private static GameObject FindOriginal(string path, string compactPath)
    {
        if (!string.IsNullOrEmpty(path) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) && path != compactPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset != null && W.Find(asset.transform, "shield") != null) return asset;
        }

        var standard = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TerrainPrefs/бульдозер (2).fbx");
        if (standard != null) return standard;
        foreach (string guid in AssetDatabase.FindAssets("бульдозер t:Model"))
        {
            string candidatePath = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(candidatePath).IndexOf("(2)", StringComparison.Ordinal) < 0) continue;
            var candidate = AssetDatabase.LoadAssetAtPath<GameObject>(candidatePath);
            if (candidate != null && W.Find(candidate.transform, "shield") != null) return candidate;
        }
        return null;
    }

    private static Matrix4x4 Frame(Transform root)
    {
        Vector3 origin = Anchor(root, "hull");
        Vector3 x = Anchor(root, "door") - origin;
        Vector3 y = Anchor(root, "cabin") - origin;
        Vector3 z = Anchor(root, "hands") - origin;
        var frame = new Matrix4x4(new Vector4(x.x, x.y, x.z, 0), new Vector4(y.x, y.y, y.z, 0),
            new Vector4(z.x, z.y, z.z, 0), new Vector4(origin.x, origin.y, origin.z, 1));
        float fullScale = x.magnitude * y.magnitude * z.magnitude;
        if (fullScale <= 0 || Mathf.Abs(frame.determinant) < fullScale * .01f)
            throw new InvalidOperationException("Не удалось совместить исходную и компактную модели.");
        return frame;
    }

    private static Vector3 Anchor(Transform parent, string name)
    {
        var point = W.Find(parent, name);
        if (point == null) throw new InvalidOperationException("В модели отсутствует " + name);
        return point.position;
    }

    private static Vector3 Point(Transform source, Transform target, Matrix4x4 matrix, string name)
    {
        var point = W.Find(source, name);
        if (point == null) throw new InvalidOperationException("В компактной модели отсутствует " + name);
        return target.TransformPoint(matrix.MultiplyPoint3x4(source.InverseTransformPoint(point.position)));
    }

    private static Mesh CopyMesh(Mesh source, Matrix4x4 matrix)
    {
        var copy = UnityEngine.Object.Instantiate(source);
        copy.name = MeshName;
        Vector3[] vertices = copy.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
        copy.vertices = vertices;
        if (matrix.determinant < 0)
        {
            for (int s = 0; s < copy.subMeshCount; s++)
            {
                var indices = copy.GetTriangles(s);
                for (int i = 0; i < indices.Length; i += 3)
                    (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
                copy.SetTriangles(indices, s);
            }
        }
        // Imported normals preserve the rolled surface and its hard edges.
        Vector3[] normals = copy.normals;
        if (normals.Length == vertices.Length)
        {
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            for (int i = 0; i < normals.Length; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            copy.normals = normals;
        }
        else copy.RecalculateNormals();
        copy.RecalculateBounds();
        copy.RecalculateTangents();
        return copy;
    }
}
#endif
