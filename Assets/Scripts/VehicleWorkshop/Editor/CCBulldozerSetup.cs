#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using CatCooks.Food;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using W = CCWorkshopEditor;

public static class CCBulldozerSetup
{
    private const string MeshName = "WorkshopBulldozerDeepBucket";

    [MenuItem("CatCooks/Vehicle Workshop/02 Fit deep bulldozer bucket")]
    public static void Install()
    {
        var selected = W.Selected();
        var shield = selected.name == "shield" ? selected.transform : W.Find(selected.transform, "shield");
        var filter = shield != null ? shield.GetComponent<MeshFilter>() : null;
        if (filter == null || filter.sharedMesh == null)
            throw new InvalidOperationException("Выдели деталь shield с MeshFilter внутри бульдозера.");
        var network = shield.GetComponentInParent<NetworkObject>();
        if (network == null || network.transform == shield)
            throw new InvalidOperationException("На родительском корне бульдозера должен быть NetworkObject.");
        if (filter.sharedMesh.name == MeshName)
        {
            Debug.Log("Глубокий ковш уже установлен. Его размер тары можно настроить в CookwareCavity.", shield);
            return;
        }

        var root = network.transform;
        var meshBounds = filter.sharedMesh.bounds;
        Vector3 center = shield.TransformPoint(meshBounds.center);
        Vector3 forward = Vector3.ProjectOnPlane(center - root.position, root.up);
        if (forward.sqrMagnitude < .01f) forward = root.forward;
        // Snap to the closest signed chassis axis to avoid skew from an offset blade.
        Vector3 f = root.forward, r = root.right;
        float z = Vector3.Dot(forward, f), x = Vector3.Dot(forward, r);
        forward = Mathf.Abs(z) >= Mathf.Abs(x) ? f * Mathf.Sign(z) : r * Mathf.Sign(x);
        Quaternion frame = Quaternion.LookRotation(forward, root.up);
        Bounds bounds = MeasureBlade(shield, meshBounds, center, Quaternion.Inverse(frame));

        float width = bounds.size.x;
        float height = bounds.size.y;
        if (width < .2f || height < .15f)
            throw new InvalidOperationException("Размеры shield слишком малы. Проверь масштаб и ориентацию модели.");

        float length = height * 1.25f;
        float lip = height * .30f;
        float wall = Mathf.Min(height * .07f, width * .04f);
        Vector3 floorBack = center + frame * new Vector3(bounds.center.x, bounds.min.y, bounds.min.z);
        Matrix4x4 toShield = shield.worldToLocalMatrix *
            Matrix4x4.TRS(floorBack, frame, Vector3.one);
        Mesh mesh = BuildMesh(width, height, length, lip, wall, toShield);
        Directory.CreateDirectory(W.Generated);
        AssetDatabase.Refresh();
        string meshPath = AssetDatabase.GenerateUniqueAssetPath(W.Generated + MeshName + ".asset");
        AssetDatabase.CreateAsset(mesh, meshPath);

        // Set up the cavity before swapping the mesh, so a failed setup can be retried.
        float halfWidth = width * .5f - wall;
        float halfLength = length * .5f - wall;
        Vector3 floorCenter = floorBack + frame * new Vector3(0, 0, length * .5f);
        var bottom = shield.InverseTransformPoint(floorCenter);
        var half = new Vector2(halfWidth, halfLength);
        var cookware = shield.GetComponent<Cookware>();
        if (cookware == null)
            cookware = W.Container(shield.gameObject, "BulldozerBucket", bottom, frame, half, lip, 120);

        var cavity = W.Add<CookwareCavity>(shield.gameObject);
        SetTrough(cavity, bottom, frame, half, lip, wall);
        cavity.EditorBuild();
        W.Quiet(cookware);

        // Replace the original blade's solid collider with the generated hollow compound.
        foreach (var collider in shield.GetComponents<Collider>())
        {
            if (collider.isTrigger) continue;
            Undo.RecordObject(collider, "Disable solid bulldozer blade collider");
            collider.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
        }
        Undo.RecordObject(filter, "Install deep bulldozer bucket mesh");
        filter.sharedMesh = mesh;
        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
        AssetDatabase.SaveAssets();
        W.Done(shield.gameObject, "02: ковш установлен. Проверь Receive, дно, стенки и сохрани сцену.");
    }

    private static Bounds MeasureBlade(Transform shield, Bounds local, Vector3 center, Quaternion inverseFrame)
    {
        var bounds = new Bounds(Vector3.zero, Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? local.min.x : local.max.x,
                (i & 2) == 0 ? local.min.y : local.max.y, (i & 4) == 0 ? local.min.z : local.max.z);
            bounds.Encapsulate(inverseFrame * (shield.TransformPoint(corner) - center));
        }
        return bounds;
    }

    private static void SetTrough(CookwareCavity cavity, Vector3 bottom, Quaternion frame, Vector2 half, float lip, float wall)
    {
        var serialized = new SerializedObject(cavity);
        serialized.FindProperty("_shape").enumValueIndex = 0;
        serialized.ApplyModifiedProperties();
        W.Set(cavity, "_bottomLocal", bottom);
        W.Set(cavity, "_frameLocal", Quaternion.Inverse(cavity.transform.rotation) * frame);
        W.Set(cavity, "_bottomHalf", half);
        W.Set(cavity, "_topHalf", half);
        W.Set(cavity, "_topShift", Vector2.zero);
        W.Set(cavity, "_depth", lip);
        W.Set(cavity, "_wall", wall);
        W.Set(cavity, "_bands", 2);
        W.Set(cavity, "_makeColliders", true);
        W.Set(cavity, "_disableRootColliders", false);
    }

    private static Mesh BuildMesh(float width, float height, float length, float lip, float wall, Matrix4x4 toShield)
    {
        var shell = new Plates();
        float side = width * .5f;
        shell.Box(-side, side, -wall, 0, 0, length);
        shell.Box(-side, side, 0, height, 0, wall);
        shell.Box(-side, side, 0, lip, length - wall, length);
        var cheek = new[] { new Vector2(0, 0), new Vector2(0, length),
            new Vector2(lip, length), new Vector2(height, 0) };
        shell.Prism(-side, -side + wall, cheek);
        shell.Prism(side - wall, side, cheek);
        for (int i = 0; i < 7; i++)
        {
            float x = width * ((i + .5f) / 7f - .5f);
            float half = width * .034f;
            shell.Box(x - half, x + half, -wall, -wall * .15f, length, length + wall * 1.8f);
        }

        for (int i = 0; i < shell.vertices.Count; i++)
            shell.vertices[i] = toShield.MultiplyPoint3x4(shell.vertices[i]);
        if (toShield.determinant < 0)
        {
            for (int i = 0; i < shell.triangles.Count; i += 3)
            {
                int old = shell.triangles[i + 1];
                shell.triangles[i + 1] = shell.triangles[i + 2];
                shell.triangles[i + 2] = old;
            }
        }

        var mesh = new Mesh { name = MeshName };
        mesh.SetVertices(shell.vertices);
        mesh.SetTriangles(shell.triangles, 0);
        mesh.SetUVs(0, shell.uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    private sealed class Plates
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<int> triangles = new List<int>();
        public readonly List<Vector2> uvs = new List<Vector2>();

        public void Box(float left, float right, float low, float high, float back, float front)
        {
            Prism(left, right, new[]
            {
                new Vector2(low, back), new Vector2(low, front),
                new Vector2(high, front), new Vector2(high, back)
            });
        }

        public void Prism(float left, float right, Vector2[] profile)
        {
            // Profile is clockwise in the Y/Z plane as viewed from +X.
            var a = new Vector3[profile.Length];
            var b = new Vector3[profile.Length];
            for (int i = 0; i < profile.Length; i++)
            {
                a[i] = new Vector3(left, profile[i].x, profile[i].y);
                b[i] = new Vector3(right, profile[i].x, profile[i].y);
            }
            Face(a);
            Array.Reverse(b);
            Face(b);
            Array.Reverse(b);
            for (int i = 0; i < a.Length; i++)
            {
                int next = (i + 1) % a.Length;
                Face(new[] { a[next], a[i], b[i], b[next] });
            }
        }

        private void Face(Vector3[] face)
        {
            int start = vertices.Count;
            Vector3 axis = (face[1] - face[0]).normalized;
            Vector3 normal = Vector3.Cross(face[1] - face[0], face[2] - face[0]).normalized;
            Vector3 vertical = Vector3.Cross(normal, axis);
            foreach (var point in face)
            {
                vertices.Add(point);
                Vector3 offset = point - face[0];
                uvs.Add(new Vector2(Vector3.Dot(offset, axis), Vector3.Dot(offset, vertical)));
            }
            for (int i = 1; i + 1 < face.Length; i++)
            {
                triangles.Add(start);
                triangles.Add(start + i);
                triangles.Add(start + i + 1);
            }
        }
    }
}
#endif
