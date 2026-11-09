using UnityEngine;

/// <summary>
/// ШЛАНГ между горелкой и баллоном — только вид, у каждого игрока свой.
/// Трубка провисает и ложится на пол, когда есть слабина; натянулся — прямой и
/// краснеет на концах (вот-вот сорвётся).
///
/// Создаёт кровельная горелка сама (RoofingTorchItem), руками вешать не нужно.
/// </summary>
public sealed class GasHose : MonoBehaviour
{
    private const int Segs = 28;
    private const int Sides = 8;

    private Mesh _mesh;
    private MeshRenderer _mr;
    private Vector3[] _v, _n, _pts, _prev;
    private Transform _ignoreA, _ignoreB;
    private bool _hasPrev;
    private MaterialPropertyBlock _mpb;
    private static Material _mat;
    private readonly RaycastHit[] _hits = new RaycastHit[8];

    public float Length = 4f;
    public float Radius = 0.011f;
    public Color Color = new Color(1f, 0.42f, 0.08f);
    /// <summary>Насколько натянут (1 — на пределе длины).</summary>
    public float Tension { get; private set; }

    public static GasHose Create(Transform owner)
    {
        var go = new GameObject("GasHose (auto)") { hideFlags = HideFlags.DontSave };
        go.layer = owner != null ? owner.gameObject.layer : 0;
        var h = go.AddComponent<GasHose>();
        h.Init();
        return h;
    }

    private void Init()
    {
        _mesh = new Mesh { name = "GasHose", hideFlags = HideFlags.DontSave };
        _mesh.MarkDynamic();
        int vc = (Segs + 1) * Sides;
        _v = new Vector3[vc];
        _n = new Vector3[vc];
        _pts = new Vector3[Segs + 1];
        _prev = new Vector3[Segs + 1];
        var tris = new int[Segs * Sides * 6];
        int t = 0;
        for (int s = 0; s < Segs; s++)
            for (int k = 0; k < Sides; k++)
            {
                int a = s * Sides + k, b = s * Sides + (k + 1) % Sides;
                int c = a + Sides, d = b + Sides;
                // По часовой снаружи — лицевая сторона в Unity.
                tris[t++] = a; tris[t++] = b; tris[t++] = c;
                tris[t++] = b; tris[t++] = d; tris[t++] = c;
            }
        _mesh.vertices = _v;
        _mesh.normals = _n;
        _mesh.triangles = tris;
        gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
        _mr = gameObject.AddComponent<MeshRenderer>();
        _mr.sharedMaterial = Mat;
        _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        _mpb = new MaterialPropertyBlock();
    }

    private static Material Mat
    {
        get
        {
            if (_mat != null) return _mat;
            Shader sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            _mat = new Material(sh) { name = "GasHose (auto)", hideFlags = HideFlags.DontSave };
            if (_mat.HasProperty("_Smoothness")) _mat.SetFloat("_Smoothness", 0.35f);
            return _mat;
        }
    }

    /// <summary>Каждый кадр: от a (горелка) до b (редуктор), с направлениями штуцеров.</summary>
    public void Tick(Vector3 a, Vector3 dirA, Vector3 b, Vector3 dirB, Transform ignoreA, Transform ignoreB)
    {
        _ignoreA = ignoreA; _ignoreB = ignoreB;
        float dist = Vector3.Distance(a, b);
        Tension = Length > 0.01f ? dist / Length : 1f;
        float slack = Mathf.Max(0f, Length - dist);
        float sag = slack * 0.5f + dist * 0.04f;
        float lead = Mathf.Min(0.1f, dist * 0.15f);
        Vector3 a1 = a + (dirA.sqrMagnitude > 1e-6f ? dirA.normalized : Vector3.down) * lead;
        Vector3 b1 = b + (dirB.sqrMagnitude > 1e-6f ? dirB.normalized : Vector3.down) * lead;

        for (int i = 0; i <= Segs; i++)
        {
            float t = i / (float)Segs;
            Vector3 p;
            if (i == 0) p = a;
            else if (i == Segs) p = b;
            else
            {
                float u = Mathf.InverseLerp(1f / Segs, 1f - 1f / Segs, t);
                p = Vector3.Lerp(a1, b1, u) + Vector3.down * sag * 4f * u * (1f - u);
                // Ложится на пол.
                if (FloorY(p, sag + 1f, out float fy) && p.y < fy + Radius) p.y = fy + Radius;
            }
            _pts[i] = _hasPrev ? Vector3.Lerp(_prev[i], p, 1f - Mathf.Exp(-Time.deltaTime * 18f)) : p;
            _prev[i] = _pts[i];
        }
        _pts[0] = a; _pts[Segs] = b;
        _prev[0] = a; _prev[Segs] = b;
        _hasPrev = true;
        Build();

        Color c = Tension > 0.98f ? Color.Lerp(Color, new Color(1f, 0.15f, 0.1f), Mathf.InverseLerp(0.98f, 1.4f, Tension)) : Color;
        _mpb.SetColor("_BaseColor", c);
        _mpb.SetColor("_Color", c);
        _mr.SetPropertyBlock(_mpb);
    }

    private bool FloorY(Vector3 p, float down, out float y)
    {
        y = 0f;
        int n = Physics.RaycastNonAlloc(p + Vector3.up * 0.4f, Vector3.down, _hits, down + 0.4f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            Transform t = _hits[i].transform;
            if ((_ignoreA != null && t.IsChildOf(_ignoreA)) || (_ignoreB != null && t.IsChildOf(_ignoreB))) continue;
            if (_hits[i].collider.GetComponentInParent<PlayerInteractor>() != null) continue;
            if (_hits[i].distance < best) { best = _hits[i].distance; y = _hits[i].point.y; found = true; }
        }
        return found;
    }

    private void Build()
    {
        Vector3 prevN = Vector3.zero;
        for (int i = 0; i <= Segs; i++)
        {
            Vector3 tng = i == 0 ? _pts[1] - _pts[0] : i == Segs ? _pts[Segs] - _pts[Segs - 1] : _pts[i + 1] - _pts[i - 1];
            if (tng.sqrMagnitude < 1e-10f) tng = Vector3.forward;
            tng.Normalize();
            Vector3 nrm;
            if (i == 0)
            {
                Vector3 refv = Mathf.Abs(tng.y) < 0.9f ? Vector3.up : Vector3.right;
                nrm = Vector3.Cross(tng, refv).normalized;
            }
            else nrm = (prevN - tng * Vector3.Dot(prevN, tng)).normalized;
            if (nrm.sqrMagnitude < 1e-6f) nrm = Vector3.Cross(tng, Vector3.up).normalized;
            prevN = nrm;
            Vector3 bin = Vector3.Cross(tng, nrm);
            for (int k = 0; k < Sides; k++)
            {
                float ang = k / (float)Sides * Mathf.PI * 2f;
                Vector3 d = nrm * Mathf.Cos(ang) + bin * Mathf.Sin(ang);
                _v[i * Sides + k] = transform.InverseTransformPoint(_pts[i] + d * Radius);
                _n[i * Sides + k] = transform.InverseTransformDirection(d);
            }
        }
        _mesh.vertices = _v;
        _mesh.normals = _n;
        _mesh.RecalculateBounds();
    }

    public void SetVisible(bool on) { if (_mr != null && _mr.enabled != on) _mr.enabled = on; }

    private void OnDestroy() { if (_mesh != null) Destroy(_mesh); }
}
