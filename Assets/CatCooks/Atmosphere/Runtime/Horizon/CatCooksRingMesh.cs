using UnityEngine;

/// <summary>Кольцо/дуга-стена для тумана, облаков и песка. UV: x — по окружности, y — по высоте.</summary>
public static class CatCooksRingMesh
{
    public static Transform Create(Transform parent, string name, float radius, float height, float arcDeg, int segs, int rows, Material mat)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(parent, false);
        var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
        int cols = segs + 1, n = cols * (rows + 1);
        var v = new Vector3[n]; var nr = new Vector3[n]; var uv = new Vector2[n];
        float arc = arcDeg * Mathf.Deg2Rad;
        for (int y = 0; y <= rows; y++)
            for (int x = 0; x < cols; x++)
            {
                float t = x / (float)segs;
                float a = -arc * 0.5f + arc * t;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                int i = y * cols + x;
                v[i] = dir * radius + Vector3.up * (height * y / rows);
                nr[i] = dir;
                uv[i] = new Vector2(t * arcDeg / 360f, y / (float)rows);
            }
        var tri = new int[segs * rows * 6]; int k = 0;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < segs; x++)
            {
                int a = y * cols + x, b = a + 1, c = a + cols, d = c + 1;
                tri[k++] = a; tri[k++] = c; tri[k++] = b; tri[k++] = b; tri[k++] = c; tri[k++] = d;
            }
        mesh.vertices = v; mesh.normals = nr; mesh.uv = uv; mesh.triangles = tri;
        mesh.bounds = new Bounds(Vector3.up * height * 0.5f, new Vector3(radius * 2.4f + 200f, height * 2f, radius * 2.4f + 200f));
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go.transform;
    }
}
