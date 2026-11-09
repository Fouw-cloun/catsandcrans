using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

/// <summary>Объёмный трос с тремя прядями. Только визуал; физикой управляет CraneHook.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[DefaultExecutionOrder(1000)]
public sealed class CraneCableVisual : MonoBehaviour
{
    // Names retained so existing scenes/prefabs keep their references and settings.
    [SerializeField] private Transform _top;
    [SerializeField] private CraneHook _hook;
    [Min(0.005f)][SerializeField] private float _radius = 0.035f;
    [Min(0.2f)][SerializeField] private float _twistPitch = 0.8f;
    [Range(24, 384)][SerializeField] private int _segments = 192;
    [Range(4, 10)][SerializeField] private int _sides = 6;
    [SerializeField] private Material _material;
    [SerializeField] private Color _steelColor = new Color(0.28f, 0.31f, 0.34f);

    public enum CableQuality { Balanced, Detailed, Fast }
    [Header("Производительность визуала")]
    [Tooltip("Balanced: до 96 сегментов/6 граней. Detailed: старые Segments/Sides. Fast: до 48/4.")]
    [SerializeField] private CableQuality _quality = CableQuality.Balanced;
    [Tooltip("Допуск неподвижности в метрах. Изменения накапливаются относительно последнего построения.")]
    [Range(0.0001f, 0.01f)][SerializeField] private float _movementTolerance = 0.0005f;
    [Tooltip("Интервал обновления скрытого троса. 0 — без ограничения. Видимый движущийся трос обновляется каждый кадр.")]
    [Range(0f, 1f)][SerializeField] private float _hiddenUpdateInterval = 0.25f;
    [Tooltip("Отбрасывать тень от тонкого троса. На физику и тени модели крана не влияет.")]
    [SerializeField] private bool _castShadows;
    [Tooltip("В облегчённых режимах разрежает витки на очень длинном тросе, чтобы они не превратились в ломаную.")]
    [SerializeField] private bool _limitTwistToResolution = true;

    public float Length { get; set; } = 5f;
    public int RuntimeVertexCount => _vertices != null ? _vertices.Length : 0;
    private static readonly ProfilerMarker RebuildMarker = new ProfilerMarker("CatCooks.CraneCable.Rebuild");
    private Mesh _mesh;
    private Material _ownedMaterial;
    private Material _appliedMaterial;
    private Color _appliedColor;
    private MeshRenderer _renderer;
    private Vector3[] _vertices, _normals, _centres;
    private Vector2[] _uv;
    private float[] _distance, _offsetX, _offsetY, _normalX, _normalY, _uvX;
    private int[] _triangles;
    private int _rings, _radial, _crossCount, _builtSegments, _builtSides;
    private float _sag, _targetSag, _solvedSpan = -1f, _solvedLength = -1f;
    private float _nextHiddenUpdate, _lastBuildTime;
    private bool _sagReady, _hasGeometry;
    private Vector3 _lastA, _lastB, _lastRight;
    private float _lastLength, _lastRadius, _lastPitch;
    private CableQuality _lastQuality;
    private bool _lastLimitTwist;
    private Matrix4x4 _lastMatrix;
    private Bounds _geometryBounds;

    public void Bind(Transform top, CraneHook hook)
    {
        _top = top; _hook = hook; _hasGeometry = false; _sagReady = false;
    }
    private void Awake() => EnsureResources();
    private void OnEnable() { _hasGeometry = false; _nextHiddenUpdate = 0f; }
    private void EnsureResources()
    {
        if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
        if (_mesh == null)
        {
            _mesh = new Mesh { name = "Crane braided cable (optimized)", hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _hasGeometry = false;
        }
        ShadowCastingMode shadows = _castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        if (_renderer.shadowCastingMode != shadows) _renderer.shadowCastingMode = shadows;
        if (!_renderer.receiveShadows) _renderer.receiveShadows = true;
        if (_material != null)
        {
            if (_appliedMaterial != _material) { _renderer.sharedMaterial = _material; _appliedMaterial = _material; }
        }
        else
        {
            if (_ownedMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader != null)
                {
                    _ownedMaterial = new Material(shader) { name = "Crane steel (runtime)", hideFlags = HideFlags.DontSave };
                    if (_ownedMaterial.HasProperty("_Metallic")) _ownedMaterial.SetFloat("_Metallic", 0.7f);
                    if (_ownedMaterial.HasProperty("_Smoothness")) _ownedMaterial.SetFloat("_Smoothness", 0.55f);
                    ApplyOwnedColor();
                }
            }
            if (_ownedMaterial != null && _appliedMaterial != _ownedMaterial)
            { _renderer.sharedMaterial = _ownedMaterial; _appliedMaterial = _ownedMaterial; }
            if (_ownedMaterial != null && _appliedColor != _steelColor) ApplyOwnedColor();
        }
        int segments = Mathf.Clamp(_segments, 24, 384), sides = Mathf.Clamp(_sides, 4, 10);
        if (_quality == CableQuality.Balanced) { segments = Mathf.Min(segments, 96); sides = Mathf.Min(sides, 6); }
        if (_quality == CableQuality.Fast) { segments = Mathf.Min(segments, 48); sides = Mathf.Min(sides, 4); }
        if (_vertices == null || segments != _builtSegments || sides != _builtSides) BuildTopology(segments, sides);
    }
    private void ApplyOwnedColor()
    {
        if (_ownedMaterial.HasProperty("_BaseColor")) _ownedMaterial.SetColor("_BaseColor", _steelColor);
        if (_ownedMaterial.HasProperty("_Color")) _ownedMaterial.SetColor("_Color", _steelColor);
        _appliedColor = _steelColor;
    }
    private void BuildTopology(int segments, int sides)
    {
        _builtSegments = segments; _builtSides = sides;
        _rings = segments + 1; _radial = sides + 1; _crossCount = _radial * 3;
        int count = _rings * _crossCount;
        _vertices = new Vector3[count]; _normals = new Vector3[count]; _uv = new Vector2[count];
        _centres = new Vector3[_rings]; _distance = new float[_rings];
        _offsetX = new float[_crossCount]; _offsetY = new float[_crossCount];
        _normalX = new float[_crossCount]; _normalY = new float[_crossCount]; _uvX = new float[_crossCount];
        _triangles = new int[segments * sides * 3 * 6];
        // All radial trigonometry is constant. Only one twist sin/cos per ring remains at runtime.
        for (int strand = 0; strand < 3; strand++)
        {
            float phase = strand * Mathf.PI * 2f / 3f;
            float cx = Mathf.Cos(phase), cy = Mathf.Sin(phase);
            for (int side = 0; side < _radial; side++)
            {
                int i = strand * _radial + side;
                float angle = side / (float)sides * Mathf.PI * 2f + phase;
                _normalX[i] = Mathf.Cos(angle); _normalY[i] = Mathf.Sin(angle);
                _offsetX[i] = cx + _normalX[i]; _offsetY[i] = cy + _normalY[i];
                _uvX[i] = side / (float)sides;
            }
        }
        int index = 0;
        for (int ring = 0; ring < segments; ring++)
            for (int strand = 0; strand < 3; strand++)
                for (int side = 0; side < sides; side++)
                {
                    int a = ring * _crossCount + strand * _radial + side, b = a + _crossCount;
                    _triangles[index++] = a; _triangles[index++] = a + 1; _triangles[index++] = b;
                    _triangles[index++] = a + 1; _triangles[index++] = b + 1; _triangles[index++] = b;
                }
        _mesh.Clear();
        _mesh.SetVertices(_vertices);
        _mesh.SetTriangles(_triangles, 0, false);
        _hasGeometry = false;
    }

    private static Vector3 Perpendicular(Vector3 direction)
        => Vector3.Cross(direction, Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
    private static Vector3 Curve(Vector3 a, Vector3 b, Vector3 bend, float sag, float t)
        => Vector3.Lerp(a, b, t) + bend * (4f * t * (1f - t) * sag);

    // Exact arc length of this parabola: bend is a unit vector perpendicular to the chord.
    // Replaces 24 vector samples for every binary-search step.
    private static float ArcLength(float span, float sag)
    {
        float k = 4f * sag / span;
        if (k < 0.001f) return span * (1f + k * k / 6f);
        float root = Mathf.Sqrt(1f + k * k);
        return 0.5f * span * (root + Mathf.Log(k + root) / k);
    }
    private float SolveSag(float span, float wanted)
    {
        if (wanted - span < 0.015f)
        {
            _solvedSpan = span; _solvedLength = wanted;
            return 0f;
        }
        if (Mathf.Abs(span - _solvedSpan) < 0.0001f && Mathf.Abs(wanted - _solvedLength) < 0.0001f) return _targetSag;
        float low = 0f, high = wanted * 0.5f;
        for (int i = 0; i < 14; i++)
        {
            float middle = (low + high) * 0.5f;
            if (ArcLength(span, middle) < wanted) low = middle; else high = middle;
        }
        _solvedSpan = span; _solvedLength = wanted;
        return (low + high) * 0.5f;
    }

    private void LateUpdate()
    {
        EnsureResources();
        if (_top == null || _hook == null) { _renderer.enabled = false; _hasGeometry = false; return; }
        Vector3 a = _top.position, b = _hook.RopeAnchorWorld, right = _top.right;
        Vector3 delta = b - a;
        float span = delta.magnitude;
        if (span < 0.001f) { _renderer.enabled = false; _hasGeometry = false; return; }
        _renderer.enabled = true;
        float wanted = Mathf.Max(span, Length), radius = Mathf.Max(0.005f, _radius), pitch = Mathf.Max(0.2f, _twistPitch);
        Vector3 tangent = delta / span;
        Vector3 bend = Vector3.ProjectOnPlane(Vector3.down, tangent);
        if (bend.sqrMagnitude < 0.01f) bend = Vector3.ProjectOnPlane(right, tangent);
        if (bend.sqrMagnitude < 0.01f) bend = Vector3.ProjectOnPlane(Vector3.forward, tangent);
        bend.Normalize();
        Matrix4x4 localToWorld = transform.localToWorldMatrix;
        float tolerance = Mathf.Max(0.0001f, _movementTolerance), sqrTolerance = tolerance * tolerance;
        bool changed = !_hasGeometry || (a - _lastA).sqrMagnitude > sqrTolerance || (b - _lastB).sqrMagnitude > sqrTolerance ||
            (right - _lastRight).sqrMagnitude > 0.000001f || Mathf.Abs(wanted - _lastLength) > tolerance ||
            localToWorld != _lastMatrix || radius != _lastRadius || pitch != _lastPitch ||
            _quality != _lastQuality || _limitTwistToResolution != _lastLimitTwist;
        bool settling = !_sagReady || Mathf.Abs(_targetSag - _sag) > tolerance * 0.25f;
        if (!changed && !settling) return;

        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
        float now = Time.time;
        if (_hasGeometry && !_renderer.isVisible && _hiddenUpdateInterval > 0f && now < _nextHiddenUpdate)
        {
            // Refresh conservative bounds while hidden so movement back into view is detected.
            Bounds pending = CableBounds(worldToLocal, a, b, bend, Mathf.Max(_sag, wanted * 0.5f), radius);
            pending.Encapsulate(_geometryBounds); _mesh.bounds = pending;
            return;
        }
        using (RebuildMarker.Auto())
        {
            _targetSag = SolveSag(span, wanted);
            float dt = _hasGeometry ? Mathf.Max(0f, now - _lastBuildTime) : Time.deltaTime;
            if (!_sagReady) { _sag = _targetSag; _sagReady = true; }
            else _sag = Mathf.Lerp(_sag, _targetSag, 1f - Mathf.Exp(-15f * dt));
            if (Mathf.Abs(_sag - _targetSag) <= tolerance * 0.25f) _sag = _targetSag;
            BuildGeometry(a, b, right, tangent, bend, radius, pitch, worldToLocal, localToWorld);
            _geometryBounds = CableBounds(worldToLocal, a, b, bend, _sag, radius);
            _mesh.SetVertices(_vertices, 0, _vertices.Length, MeshUpdateFlags.DontRecalculateBounds);
            _mesh.SetNormals(_normals);
            _mesh.SetUVs(0, _uv);
            _mesh.bounds = _geometryBounds;
        }
        _lastA = a; _lastB = b; _lastRight = right; _lastMatrix = localToWorld;
        _lastLength = wanted; _lastRadius = radius; _lastPitch = pitch;
        _lastQuality = _quality; _lastLimitTwist = _limitTwistToResolution;
        _lastBuildTime = now; _nextHiddenUpdate = now + _hiddenUpdateInterval; _hasGeometry = true;
    }

    private void BuildGeometry(Vector3 a, Vector3 b, Vector3 right, Vector3 tangent, Vector3 bend,
        float radius, float pitch, Matrix4x4 worldToLocal, Matrix4x4 localToWorld)
    {
        _distance[0] = 0f;
        for (int ring = 0; ring < _rings; ring++)
        {
            float t = ring / (float)(_rings - 1);
            _centres[ring] = Curve(a, b, bend, _sag, t);
            if (ring > 0) _distance[ring] = _distance[ring - 1] + Vector3.Distance(_centres[ring], _centres[ring - 1]);
        }
        // Four segments per revolution minimum in reduced-detail modes.
        if (_limitTwistToResolution && _quality != CableQuality.Detailed)
            pitch = Mathf.Max(pitch, _distance[_rings - 1] * 4f / (_rings - 1));
        float twistScale = Mathf.PI * 2f / pitch, halfRadius = radius * 0.5f;
        Vector3 normal = Vector3.ProjectOnPlane(right, tangent).normalized;
        if (normal.sqrMagnitude < 0.1f) normal = Perpendicular(tangent);
        Matrix4x4 normalToLocal = localToWorld.transpose;
        for (int ring = 0; ring < _rings; ring++)
        {
            Vector3 dir = (_centres[Mathf.Min(_rings - 1, ring + 1)] - _centres[Mathf.Max(0, ring - 1)]).normalized;
            normal = Vector3.ProjectOnPlane(normal, dir).normalized;
            if (normal.sqrMagnitude < 0.1f) normal = Perpendicular(dir);
            Vector3 binormal = Vector3.Cross(dir, normal).normalized;
            float twist = _distance[ring] * twistScale;
            float c = Mathf.Cos(twist), s = Mathf.Sin(twist);
            Vector3 twistN = normal * c + binormal * s, twistB = binormal * c - normal * s;
            // Matrix multiplies only per ring, not per vertex; supports scaled parents.
            Vector3 centre = worldToLocal.MultiplyPoint3x4(_centres[ring]);
            Vector3 localN = worldToLocal.MultiplyVector(twistN) * halfRadius;
            Vector3 localB = worldToLocal.MultiplyVector(twistB) * halfRadius;
            Vector3 normalN = normalToLocal.MultiplyVector(twistN), normalB = normalToLocal.MultiplyVector(twistB);
            int first = ring * _crossCount;
            for (int i = 0; i < _crossCount; i++)
            {
                int v = first + i;
                _vertices[v] = centre + localN * _offsetX[i] + localB * _offsetY[i];
                _normals[v] = (normalN * _normalX[i] + normalB * _normalY[i]).normalized;
                _uv[v] = new Vector2(_uvX[i], _distance[ring]);
            }
        }
    }
    private static Bounds CableBounds(Matrix4x4 worldToLocal, Vector3 a, Vector3 b, Vector3 bend, float sag, float radius)
    {
        // Quadratic Bezier hull contains the whole curve (control point at midpoint + 2*sag).
        Vector3 p0 = worldToLocal.MultiplyPoint3x4(a), p1 = worldToLocal.MultiplyPoint3x4(b);
        Vector3 control = worldToLocal.MultiplyPoint3x4((a + b) * 0.5f + bend * (2f * sag));
        Vector3 padding = new Vector3(
            new Vector3(worldToLocal.m00, worldToLocal.m01, worldToLocal.m02).magnitude,
            new Vector3(worldToLocal.m10, worldToLocal.m11, worldToLocal.m12).magnitude,
            new Vector3(worldToLocal.m20, worldToLocal.m21, worldToLocal.m22).magnitude) * (radius + 0.001f);
        Bounds bounds = new Bounds();
        bounds.SetMinMax(Vector3.Min(Vector3.Min(p0, p1), control) - padding,
                         Vector3.Max(Vector3.Max(p0, p1), control) + padding);
        return bounds;
    }
    private void OnDestroy()
    {
        if (_mesh != null) Destroy(_mesh);
        if (_ownedMaterial != null) Destroy(_ownedMaterial);
    }
}
