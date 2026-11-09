using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ГУСТОЕ ВЫТЕКАЕТ (сметана, манная каша, слайм) — вместо обычной струи.
    ///
    /// С кромки свешивается толстый «язык»: медленно вытягивается вниз, у кончика
    /// набухает капля, шейка утончается — и капля отрывается шлепком, язык
    /// подтягивается и тянется снова. Шлепки падают, расплющиваются, на жидкости —
    /// круги. Только вид: сколько порций ушло, решает сервер (Cookware Spill).
    /// </summary>
    public sealed class ThickOozeFx : MonoBehaviour
    {
        private const int Rings = 18, Sides = 12;

        private struct Blob { public Vector3 P, V; public float R, Age; }

        private Mesh _mesh;
        private MeshRenderer _mr;
        private Material _mat;
        private Vector3[] _v;
        private Vector3[] _n;
        private readonly Vector3[] _path = new Vector3[Rings + 1];
        private readonly float[] _rad = new float[Rings + 1];
        private readonly List<Blob> _blobs = new List<Blob>();
        private Transform _ignore;
        private float _len, _maxLen, _t, _sway;
        private Color _color = Color.white;
        private static Mesh _sphere;

        public static ThickOozeFx Create(Transform owner)
        {
            var go = new GameObject("ThickOoze (auto)") { hideFlags = HideFlags.DontSave, layer = owner.gameObject.layer };
            var fx = go.AddComponent<ThickOozeFx>();
            fx._ignore = owner;
            fx.Build();
            return fx;
        }

        private void Build()
        {
            _mesh = new Mesh { name = "ThickOoze", hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
            int vc = (Rings + 1) * Sides + 1;
            _v = new Vector3[vc];
            _n = new Vector3[vc];
            var tri = new List<int>();
            for (int r = 0; r < Rings; r++)
                for (int s = 0; s < Sides; s++)
                {
                    int a = r * Sides + s, b = r * Sides + (s + 1) % Sides, c = a + Sides, d = b + Sides;
                    tri.Add(a); tri.Add(c); tri.Add(b); tri.Add(b); tri.Add(c); tri.Add(d);
                }
            int tip = vc - 1, last = Rings * Sides;
            for (int s = 0; s < Sides; s++) { tri.Add(last + s); tri.Add(tip); tri.Add(last + (s + 1) % Sides); }
            _mesh.vertices = _v;
            _mesh.SetTriangles(tri, 0);
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _mr = gameObject.AddComponent<MeshRenderer>();
            Shader sh = Shader.Find("CatCooks/Toon Food");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            _mat = new Material(sh) { name = "ThickOoze (auto)", hideFlags = HideFlags.DontSave };
            if (_mat.HasProperty("_Smoothness")) _mat.SetFloat("_Smoothness", 0.7f);
            _mr.sharedMaterial = _mat;
            _mr.enabled = false;
            _maxLen = Random.Range(0.18f, 0.3f);
        }

        /// <summary>Каждый кадр из Cookware Spill. on — сметана сейчас сползает с кромки rim наружу outward.</summary>
        public void Tick(bool on, Vector3 rim, Vector3 outward, Color color, float width, float flow01)
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            _t += dt;
            if (color != _color)
            {
                _color = color;
                if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", color); else _mat.color = color;
            }
            Vector3 flat = Vector3.ProjectOnPlane(outward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();
            float r0 = Mathf.Max(0.02f, width * 0.6f);

            if (on)
            {
                // тянется медленно, как каша: быстрее при большем перевесе
                _len += dt * (0.07f + 0.12f * flow01);
                if (_len > _maxLen) Detach(rim, flat, r0, 0.38f);
            }
            else if (_len > 0f)
            {
                // перестали лить — язык срывается последней каплей
                if (_len > 0.06f) Detach(rim, flat, r0, 0f);
                _len = Mathf.Max(0f, _len - dt * 0.8f);
            }

            _mr.enabled = _len > 0.01f;
            if (_mr.enabled) BuildTongue(rim, flat, r0);
            UpdateBlobs(dt);
        }

        private void Detach(Vector3 rim, Vector3 flat, float r0, float keep)
        {
            Vector3 tip = TipPoint(rim, flat, _len);
            float r = r0 * Mathf.Lerp(0.8f, 1.2f, Mathf.Clamp01(_len / 0.25f));
            _blobs.Add(new Blob { P = tip, V = Vector3.down * 0.4f + flat * 0.05f, R = r, Age = 0f });
            _len *= keep;
            _maxLen = Random.Range(0.18f, 0.32f);
        }

        private Vector3 TipPoint(Vector3 rim, Vector3 flat, float len)
        {
            float over = Mathf.Min(len, 0.05f);
            return rim + flat * (0.03f + over * 0.4f) + Vector3.down * len;
        }

        private void BuildTongue(Vector3 rim, Vector3 flat, float r0)
        {
            Vector3 side = Vector3.Cross(Vector3.up, flat).normalized;
            _sway = Mathf.Sin(_t * 2.3f) * 0.008f + Mathf.Sin(_t * 5.1f) * 0.003f;
            float neckAt = 0.62f;
            float bulb = Mathf.Clamp01(_len / 0.1f);
            for (int i = 0; i <= Rings; i++)
            {
                float t = i / (float)Rings;
                float s = t * _len;
                // переваливается через кромку, потом висит вниз
                float overK = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s / 0.05f));
                Vector3 p = rim + flat * (0.03f * overK + Mathf.Min(s, 0.05f) * 0.4f) + Vector3.down * s
                            + side * (_sway * t * t);
                _path[i] = p;
                // толстый у кромки → шейка → набухшая капля на конце
                float neck = Mathf.SmoothStep(0.1f, neckAt + 0.15f, t) * 0.5f;
                float drop = Mathf.Exp(-(t - 0.86f) * (t - 0.86f) / 0.008f) * 0.55f * bulb;
                float rr = r0 * (1f - neck + drop);
                if (t > 0.95f) rr *= Mathf.Lerp(1f, 0.4f, (t - 0.95f) / 0.05f);
                _rad[i] = rr;
            }
            for (int i = 0; i <= Rings; i++)
            {
                Vector3 fwd = i < Rings ? _path[i + 1] - _path[i] : _path[i] - _path[i - 1];
                if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.down;
                fwd.Normalize();
                Vector3 a = Vector3.Cross(fwd, side).normalized;
                Vector3 b = Vector3.Cross(a, fwd).normalized;
                // у кромки язык шире вдоль края — лежит на губе тары
                float wide = Mathf.Lerp(1.5f, 1f, Mathf.Clamp01(i / (Rings * 0.35f)));
                for (int k = 0; k < Sides; k++)
                {
                    float ang = k / (float)Sides * Mathf.PI * 2f;
                    Vector3 dir = a * Mathf.Cos(ang) + b * Mathf.Sin(ang) * wide;
                    // лёгкая неровность — не идеальная трубка
                    float lump = 1f + Mathf.Sin(ang * 3f + i * 0.9f + _t * 1.7f) * 0.06f;
                    _v[i * Sides + k] = _path[i] + dir * (_rad[i] * lump);
                }
            }
            Vector3 endDir = (_path[Rings] - _path[Rings - 1]).normalized;
            _v[_v.Length - 1] = _path[Rings] + endDir * _rad[Rings] * 0.6f;
            _mesh.vertices = _v;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        private void UpdateBlobs(float dt)
        {
            if (_blobs.Count == 0) return;
            if (_sphere == null)
            {
                GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _sphere = g.GetComponent<MeshFilter>().sharedMesh;
                Destroy(g);
            }
            for (int i = _blobs.Count - 1; i >= 0; i--)
            {
                Blob b = _blobs[i];
                b.Age += dt;
                Vector3 next = b.P + (b.V + Physics.gravity * dt) * dt;
                b.V += Physics.gravity * dt;
                Vector3 seg = next - b.P;
                bool hit = false;
                if (seg.sqrMagnitude > 1e-8f && Physics.Raycast(b.P, seg.normalized, out RaycastHit h, seg.magnitude + b.R * 0.5f,
                                                               ~0, QueryTriggerInteraction.Ignore))
                {
                    Cookware cw = h.collider.GetComponentInParent<Cookware>();
                    Cookware own = _ignore != null ? _ignore.GetComponent<Cookware>() : null;
                    bool skip = (cw != null && cw == own) || h.collider.GetComponentInParent<PlayerInteractor>() != null;
                    if (!skip)
                    {
                        hit = true;
                        LiquidFx.ImpactSplash(h.point + h.normal * 0.003f, h.normal, _color, 0.35f, 0.7f);
                        CookwareLiquidVisual lv = cw != null ? cw.GetComponentInChildren<CookwareLiquidVisual>() : null;
                        if (lv != null) lv.Ripple(h.point);
                        else
                        {
                            LiquidPuddle pd = LiquidPuddle.FindNear(h.point);
                            if (pd != null) pd.Ripple(h.point);
                        }
                    }
                }
                if (hit || b.Age > 3f) { _blobs.RemoveAt(i); continue; }
                b.P = next;
                _blobs[i] = b;
                // в полёте вытягивается по скорости и покачивается
                float st = Mathf.Clamp(1f + b.V.magnitude * 0.06f, 1f, 1.5f);
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, b.V.sqrMagnitude > 1e-4f ? b.V.normalized : Vector3.down);
                Vector3 sc = new Vector3(b.R * 2f / Mathf.Sqrt(st), b.R * 2f * st, b.R * 2f / Mathf.Sqrt(st));
                Graphics.DrawMesh(_sphere, Matrix4x4.TRS(b.P, rot, sc), _mat, gameObject.layer);
            }
        }

        private void OnDestroy()
        {
            if (_mesh != null) SafeDestroy.Kill(_mesh);
            if (_mat != null) SafeDestroy.Kill(_mat);
        }
    }
}
