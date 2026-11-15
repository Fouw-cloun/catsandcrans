using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v23.5: АНИМАЦИИ ЧЕРПАЛОК (мастерок, лопата, черпак) — только вид, логика зачерпывания прежняя.
    ///  • Зачерпнул (груз прибавился) — «боковой черпок»: полотно ныряет вперёд-вниз, поворачивается на бок,
    ///    проходит дугой справа налево и возвращается в руку.
    ///  • Высыпал (груз убавился) — «бросок»: замах назад-вверх, щелчок вперёд кончиком вниз,
    ///    и из полотна в тару под прицелом летит комок того, что на нём было (цвет — как у груза).
    /// Инструмент всё время в руке — никакого «магнита» к таре.
    /// </summary>
    public sealed class ScoopAnimator
    {
        public static bool Enabled = true;
        public static float ScoopTime = 0.5f;
        public static float TossTime = 0.42f;

        private int _lastLoad = -1;
        private float _scoopT = -1f, _tossT = -1f;
        private bool _flew;
        private Color _clumpColor = new Color(0.9f, 0.85f, 0.75f, 1f);
        private Material _clumpMat;
        private float _clumpSize = 0.08f;
        private bool _needSample;

        public void Reset() { _lastLoad = -1; _scoopT = _tossT = -1f; }

        /// <summary>load — сколько на полотне (−1 — неизвестно). point — полотно.</summary>
        public void Apply(Transform cam, Transform item, Transform point, int load)
        {
            if (!Enabled || cam == null || item == null || load < 0) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (_lastLoad >= 0)
            {
                if (load > _lastLoad) { _scoopT = 0f; _tossT = -1f; _needSample = true; }
                else if (load < _lastLoad) { _tossT = 0f; _scoopT = -1f; _flew = false; }
            }
            _lastLoad = load;
            if (load > 0 && _needSample && point != null) Sample(point);

            float dist = Mathf.Max(0.2f, (item.position - cam.position).magnitude);
            Vector3 f = cam.forward, u = cam.up, r = cam.right;
            Vector3 pos = item.position;
            Quaternion rot = item.rotation;

            if (_scoopT >= 0f)
            {
                _scoopT += dt / ScoopTime;
                float t = Mathf.Clamp01(_scoopT);
                float a = Mathf.Sin(t * Mathf.PI);                                     // туда-обратно
                float sweep = Mathf.Lerp(0.12f, -0.12f, Mathf.SmoothStep(0.15f, 0.85f, t));
                pos += f * (dist * 0.2f * a) - u * (dist * 0.16f * a) + r * (dist * sweep * a);
                rot = Quaternion.AngleAxis(-40f * a, f) * Quaternion.AngleAxis(28f * a, r) * rot;
                if (_scoopT >= 1f) _scoopT = -1f;
            }
            else if (_tossT >= 0f)
            {
                _tossT += dt / TossTime;
                float t = Mathf.Clamp01(_tossT);
                float k1 = Mathf.SmoothStep(0f, 1f, t / 0.3f) * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.3f) / 0.15f));   // замах
                float k2 = Mathf.SmoothStep(0f, 1f, (t - 0.3f) / 0.2f) * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.6f) / 0.4f)); // щелчок
                pos += f * (dist * (-0.08f * k1 + 0.22f * k2)) + u * (dist * (0.06f * k1 + 0.03f * k2));
                rot = Quaternion.AngleAxis(-25f * k1 + 55f * k2, r) * rot;
                if (!_flew && t >= 0.38f && point != null)
                {
                    _flew = true;
                    Throw(point.position, cam);
                }
                if (_tossT >= 1f) _tossT = -1f;
            }
            item.SetPositionAndRotation(pos, rot);
        }

        private void Sample(Transform point)
        {
            foreach (MeshFilter mf in point.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m == null || m.vertexCount == 0) continue;
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled) continue;
                var cols = m.colors32;
                if (cols != null && cols.Length > 0) _clumpColor = cols[cols.Length / 2];
                _clumpMat = mr.sharedMaterial;
                Bounds b = mr.bounds;
                _clumpSize = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 0.55f, 0.03f, 0.6f);
                _needSample = false;
                return;
            }
        }

        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private void Throw(Vector3 from, Transform cam)
        {
            Vector3 to = cam.position + cam.forward * 2f;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, 5f, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Cookware cw = _hits[i].collider != null ? _hits[i].collider.GetComponentInParent<Cookware>() : null;
                if (cw == null || _hits[i].distance >= best) continue;
                best = _hits[i].distance;
                to = cw.MouthPoint;
                CookwareLiquidVisual lv = cw.GetComponentInChildren<CookwareLiquidVisual>();
                float sy;
                if (lv != null && lv.HasLiquid && lv.TryGetSurfaceY(to, out sy)) to.y = sy;
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "ScoopClump (auto)";
            go.hideFlags = HideFlags.DontSave;
            MeshFilter f = go.GetComponent<MeshFilter>();
            f.sharedMesh = ClumpMesh(f.sharedMesh, _clumpColor);
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (_clumpMat != null) mr.sharedMaterial = _clumpMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<FlyingClump>().Init(from, to, _clumpSize);
        }

        private static Mesh ClumpMesh(Mesh src, Color c)
        {
            if (src == null) return null;
            Mesh m = Object.Instantiate(src);
            m.hideFlags = HideFlags.DontSave;
            // комок: чуть бугристый
            Vector3[] v = m.vertices;
            var cols = new Color[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 d = v[i].normalized;
                float k = 1f + 0.18f * (Mathf.PerlinNoise(d.x * 2.3f + 5f, d.z * 2.3f + d.y * 1.7f + 3f) - 0.5f);
                v[i] = v[i] * k;
                cols[i] = c;
            }
            m.vertices = v;
            m.colors = cols;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>v23.5: летящий комок (только вид): дуга с подъёмом, кувырок, сплющивается к концу и исчезает.</summary>
    public sealed class FlyingClump : MonoBehaviour
    {
        private Vector3 _a, _b;
        private float _t, _size, _dur;
        private Vector3 _spin;

        public void Init(Vector3 from, Vector3 to, float size)
        {
            _a = from; _b = to; _size = size;
            _dur = Mathf.Clamp(Vector3.Distance(from, to) * 0.22f, 0.18f, 0.45f);
            _spin = Random.onUnitSphere * 540f;
            transform.position = from;
            transform.localScale = Vector3.one * size;
        }

        private void Update()
        {
            _t += Time.deltaTime / _dur;
            float t = Mathf.Clamp01(_t);
            Vector3 p = Vector3.Lerp(_a, _b, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * Mathf.Max(0.15f, Vector3.Distance(_a, _b) * 0.35f));
            transform.position = p;
            transform.Rotate(_spin * Time.deltaTime, Space.World);
            float sq = t > 0.85f ? Mathf.Lerp(1f, 0.2f, (t - 0.85f) / 0.15f) : 1f;
            transform.localScale = new Vector3(_size * (2f - sq), _size * sq, _size * (2f - sq)) * (t > 0.85f ? 0.9f : 1f);
            if (_t >= 1f)
            {
                MeshFilter mf = GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
                Destroy(gameObject);
            }
        }
    }
}
