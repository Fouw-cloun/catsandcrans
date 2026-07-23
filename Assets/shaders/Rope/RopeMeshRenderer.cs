using UnityEngine;

/// <summary>
/// Генерация tube-меша вокруг узлов верёвки (адаптация части "generate the mesh", oSqJ4R0wZGo).
/// Читает VerletRope.Nodes в LateUpdate и строит трубу с parallel-transport фреймом,
/// чтобы не было паразитного скручивания. Работает и на сервере, и на клиенте —
/// ему всё равно, кто заполнил позиции узлов.
/// </summary>
[RequireComponent(typeof(VerletRope))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class RopeMeshRenderer : MonoBehaviour
{
    [Header("Труба")]
    [Min(0.005f)] public float radius = 0.05f;
    [Range(3, 16)] public int radialSegments = 6;
    public float uvTilesPerMeter = 2f;

    VerletRope _rope;
    Mesh _mesh;

    Vector3[] _verts;
    Vector3[] _normals;
    Vector2[] _uvs;
    int[] _tris;
    int _cachedNodes = -1, _cachedRadial = -1;

    void Awake()
    {
        _rope = GetComponent<VerletRope>();
        _mesh = new Mesh { name = "RopeTube" };
        _mesh.MarkDynamic();
        GetComponent<MeshFilter>().sharedMesh = _mesh;
    }

    void LateUpdate()
    {
        var nodes = _rope.Nodes;
        if (nodes == null || nodes.Count < 2) return;
        EnsureBuffers(nodes.Count);
        BuildMesh();
    }

    void EnsureBuffers(int nodeCount)
    {
        int vpr = radialSegments + 1;               // +1 для шва UV
        if (nodeCount == _cachedNodes && radialSegments == _cachedRadial) return;

        _verts = new Vector3[nodeCount * vpr];
        _normals = new Vector3[_verts.Length];
        _uvs = new Vector2[_verts.Length];
        _tris = new int[(nodeCount - 1) * radialSegments * 6];

        int t = 0;
        for (int i = 0; i < nodeCount - 1; i++)
            for (int s = 0; s < radialSegments; s++)
            {
                int a = i * vpr + s;
                int b = a + vpr;
                _tris[t++] = a; _tris[t++] = b; _tris[t++] = a + 1;
                _tris[t++] = a + 1; _tris[t++] = b; _tris[t++] = b + 1;
            }

        _cachedNodes = nodeCount;
        _cachedRadial = radialSegments;
    }

    void BuildMesh()
    {
        var nodes = _rope.Nodes;
        int count = nodes.Count;
        int vpr = radialSegments + 1;

        // Стартовый фрейм
        Vector3 prevTangent = SafeTangent(0);
        Vector3 normal = Vector3.Cross(prevTangent, Vector3.up);
        if (normal.sqrMagnitude < 1e-4f) normal = Vector3.Cross(prevTangent, Vector3.right);
        normal.Normalize();

        float v = 0f;

        for (int i = 0; i < count; i++)
        {
            Vector3 tangent = SafeTangent(i);

            // Parallel transport: поворачиваем нормаль минимально между сегментами
            Quaternion rot = Quaternion.FromToRotation(prevTangent, tangent);
            normal = (rot * normal).normalized;
            normal = (normal - Vector3.Dot(normal, tangent) * tangent).normalized; // ортогонализация
            Vector3 binormal = Vector3.Cross(tangent, normal);
            prevTangent = tangent;

            if (i > 0)
                v += (nodes[i].position - nodes[i - 1].position).magnitude * uvTilesPerMeter;

            Vector3 localCenter = transform.InverseTransformPoint(nodes[i].position);

            for (int s = 0; s < vpr; s++)
            {
                float ang = (float)s / radialSegments * Mathf.PI * 2f;
                Vector3 dir = Mathf.Cos(ang) * normal + Mathf.Sin(ang) * binormal;
                int idx = i * vpr + s;
                _verts[idx] = localCenter + transform.InverseTransformDirection(dir) * radius;
                _normals[idx] = transform.InverseTransformDirection(dir);
                _uvs[idx] = new Vector2((float)s / radialSegments, v);
            }
        }

        _mesh.Clear();
        _mesh.vertices = _verts;
        _mesh.normals = _normals;
        _mesh.uv = _uvs;
        _mesh.triangles = _tris;
        _mesh.RecalculateBounds();
    }

    Vector3 SafeTangent(int i)
    {
        var nodes = _rope.Nodes;
        Vector3 t = (i < nodes.Count - 1)
            ? nodes[i + 1].position - nodes[i].position
            : nodes[i].position - nodes[i - 1].position;
        if (t.sqrMagnitude < 1e-8f) t = Vector3.down;
        return t.normalized;
    }
}
