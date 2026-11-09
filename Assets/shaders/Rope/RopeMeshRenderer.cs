using UnityEngine;

/// <summary>
/// Tube-меш верёвки с переменной толщиной (профиль радиуса вдоль длины) и parallel-transport
/// фреймом (без скручивания). Работает в редакторе ([ExecuteAlways]) — толщину видно сразу.
///
/// UV: uv.x = вокруг трубы, uv.y = длина * uvTilesPerMeter (для волокон, тайлится).
/// UV2: uv2.x = НОРМАЛИЗОВАННАЯ длина 0..1 (для градиента/износа в шейдере, не тайлится).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(VerletRope))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class RopeMeshRenderer : MonoBehaviour
{
    [Header("Толщина")]
    [Min(0.005f)] public float radius = 0.05f;
    [Tooltip("Профиль толщины вдоль верёвки (X: 0..1 длина, Y: множитель радиуса). Плоская = 1.")]
    public AnimationCurve radiusProfile = AnimationCurve.Constant(0f, 1f, 1f);
    [Range(3, 16)] public int radialSegments = 6;

    [Header("Текстурирование")]
    public float uvTilesPerMeter = 2f;

    VerletRope _rope;
    Mesh _mesh;
    Vector3[] _verts, _normals;
    Vector2[] _uvs, _uv2;
    int[] _tris;
    int _cachedNodes = -1, _cachedRadial = -1;

    void OnEnable()
    {
        _rope = GetComponent<VerletRope>();
        if (_mesh == null)
        {
            _mesh = new Mesh { name = "RopeTube" };
            _mesh.MarkDynamic();
        }
        GetComponent<MeshFilter>().sharedMesh = _mesh;
    }

    void LateUpdate()
    {
        if (_rope == null) _rope = GetComponent<VerletRope>();
        var nodes = _rope.Nodes;
        if (nodes == null || nodes.Count < 2) return;
        EnsureBuffers(nodes.Count);
        BuildMesh();
    }

    void EnsureBuffers(int nodeCount)
    {
        int vpr = radialSegments + 1;
        if (nodeCount == _cachedNodes && radialSegments == _cachedRadial && _verts != null) return;

        _verts = new Vector3[nodeCount * vpr];
        _normals = new Vector3[_verts.Length];
        _uvs = new Vector2[_verts.Length];
        _uv2 = new Vector2[_verts.Length];
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

        Vector3 prevTangent = SafeTangent(0);
        Vector3 normal = Vector3.Cross(prevTangent, Vector3.up);
        if (normal.sqrMagnitude < 1e-4f) normal = Vector3.Cross(prevTangent, Vector3.right);
        normal.Normalize();

        float v = 0f;

        for (int i = 0; i < count; i++)
        {
            float tNorm = (float)i / (count - 1);
            Vector3 tangent = SafeTangent(i);

            Quaternion rot = Quaternion.FromToRotation(prevTangent, tangent);
            normal = (rot * normal).normalized;
            normal = (normal - Vector3.Dot(normal, tangent) * tangent).normalized;
            Vector3 binormal = Vector3.Cross(tangent, normal);
            prevTangent = tangent;

            if (i > 0) v += (nodes[i].position - nodes[i - 1].position).magnitude * uvTilesPerMeter;

            float ringRadius = radius * Mathf.Max(0.001f, radiusProfile.Evaluate(tNorm));
            Vector3 localCenter = transform.InverseTransformPoint(nodes[i].position);

            for (int s = 0; s < vpr; s++)
            {
                float ang = (float)s / radialSegments * Mathf.PI * 2f;
                Vector3 dir = Mathf.Cos(ang) * normal + Mathf.Sin(ang) * binormal;
                Vector3 ldir = transform.InverseTransformDirection(dir);
                int idx = i * vpr + s;
                _verts[idx] = localCenter + ldir * ringRadius;
                _normals[idx] = ldir;
                _uvs[idx] = new Vector2((float)s / radialSegments, v);
                _uv2[idx] = new Vector2(tNorm, 0f);
            }
        }

        _mesh.Clear();
        _mesh.vertices = _verts;
        _mesh.normals = _normals;
        _mesh.uv = _uvs;
        _mesh.uv2 = _uv2;
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
