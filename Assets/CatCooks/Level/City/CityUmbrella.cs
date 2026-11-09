using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// НАПОЛЬНЫЙ ЗОНТ, как на курортах: тяжёлая подставка, шест, купол из 8 полосатых клиньев с фестонами
    /// и бахромой, шишечка сверху. Модель строится кодом (без FBX). Раскрывается и складывается с пружинкой.
    /// Открыт — под ним сухо: капли не идут, всплески бьют в купол, пол под ним не мокнет (CityRainBlocker).
    /// Управление: Toggle()/Open()/Close(), клавиша Key, клик мышью по шесту (в игре), «Сам открываться в дождь».
    /// Ставится меню «CatCooks/Застройка/Зонт: поставить» (в точку, куда смотрит Scene).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class CityUmbrella : MonoBehaviour
    {
        [Header("Состояние")]
        public bool IsOpen = true;
        [Tooltip("Клавиша раскрыть/сложить (None — нет)")]
        public KeyCode Key = KeyCode.None;
        [Tooltip("Сам раскрывается, когда пошёл дождь, и складывается, когда кончился")]
        public bool AutoOpenInRain = false;
        [Tooltip("Пружинистость раскрытия")]
        [Range(20f, 200f)] public float Stiffness = 70f;
        [Range(2f, 30f)] public float Damping = 8f;

        [Header("Размер, м")]
        public float Height = 2.4f;
        public float CanopyRadius = 1.45f;
        [Range(6, 12)] public int Ribs = 8;
        [Tooltip("Множитель всего зонта")]
        public float Scale = 1f;

        [Header("Цвета")]
        public Color StripeA = new Color(0.93f, 0.33f, 0.30f);
        public Color StripeB = new Color(0.98f, 0.94f, 0.84f);
        public Color PoleColor = new Color(0.86f, 0.80f, 0.70f);
        public Color BaseColor = new Color(0.30f, 0.33f, 0.38f);

        [Header("Материал (ставит меню; шейдер CatCooks/City/Toon)")]
        public Material Material;

        public float OpenAmount { get { return _open; } }

        private float _open = 1f, _vel;
        private float _builtOpen = -1f;
        private Mesh _canopy;
        private Transform _canopyT, _poleT;
        private CityRainBlocker _blocker;
        private float _meter = -1f;

        public void Toggle() { IsOpen = !IsOpen; }
        [ContextMenu("Раскрыть")] public void Open() { IsOpen = true; }
        [ContextMenu("Сложить")] public void Close() { IsOpen = false; }

        private void OnEnable()
        {
            _open = IsOpen ? 1f : 0f; _vel = 0f; _builtOpen = -1f;
            Ensure();
        }

        private void OnValidate() { _builtOpen = -1f; _meter = -1f; if (isActiveAndEnabled) Ensure(true); }

        private float Meter()
        {
            if (_meter > 0f) return _meter;
            CityProps p = FindFirstObjectByType<CityProps>();
            _meter = p != null ? p.Meter() : 3.5f;
            if (_meter <= 0f) _meter = 3.5f;
            return _meter;
        }

        private void Update()
        {
            if (Application.isPlaying)
            {
                if (Key != KeyCode.None && Input.GetKeyDown(Key)) Toggle();
                if (AutoOpenInRain && CityRain.Instance != null)
                {
                    float r = CityRain.Instance.Intensity;
                    if (r > 0.08f) IsOpen = true; else if (r < 0.02f) IsOpen = false;
                }
                // пружина: раскрывается с лёгким «хлопком» (перелёт и назад)
                float target = IsOpen ? 1f : 0f;
                float dt = Mathf.Min(Time.deltaTime, 0.05f);
                _vel += ((target - _open) * Stiffness - _vel * Damping) * dt;
                _open = Mathf.Clamp(_open + _vel * dt, -0.02f, 1.08f);
            }
            else _open = IsOpen ? 1f : 0f;

            if (Mathf.Abs(_open - _builtOpen) > 0.0005f) Ensure(true);
            UpdateBlocker();
        }

        private void OnMouseDown() { if (Application.isPlaying) Toggle(); }

        // ─────────────── модель ───────────────
        private void Ensure(bool rebuildCanopy = false)
        {
            if (_poleT == null) _poleT = transform.Find("Pole");
            if (_canopyT == null) _canopyT = transform.Find("Canopy");
            float m = Meter() * Scale;
            if (_poleT == null)
            {
                GameObject g = new GameObject("Pole");
                g.transform.SetParent(transform, false);
                g.AddComponent<MeshFilter>(); g.AddComponent<MeshRenderer>();
                _poleT = g.transform;
                rebuildCanopy = true;
            }
            if (_canopyT == null)
            {
                GameObject g = new GameObject("Canopy");
                g.transform.SetParent(transform, false);
                g.AddComponent<MeshFilter>(); g.AddComponent<MeshRenderer>();
                _canopyT = g.transform;
            }
            if (_poleT.GetComponent<MeshFilter>().sharedMesh == null || rebuildCanopy && _builtOpen < 0f) BuildPole(m);
            if (rebuildCanopy || _canopy == null) BuildCanopy(m);
            Material mat = Material;
            foreach (MeshRenderer r in new[] { _poleT.GetComponent<MeshRenderer>(), _canopyT.GetComponent<MeshRenderer>() })
            {
                if (mat != null && r.sharedMaterial != mat) r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            CapsuleCollider cc = GetComponent<CapsuleCollider>();
            if (cc == null) cc = gameObject.AddComponent<CapsuleCollider>();
            cc.center = new Vector3(0, Height * m * 0.5f, 0); cc.height = Height * m; cc.radius = 0.12f * m;
            if (_blocker == null) _blocker = GetComponent<CityRainBlocker>();
            if (_blocker == null) _blocker = gameObject.AddComponent<CityRainBlocker>();
        }

        private void BuildPole(float m)
        {
            CityToonMesh b = new CityToonMesh();
            float H = Height * m;
            b.Cylinder(Vector3.zero, 0.32f * m, 0.07f * m, 20, BaseColor);                       // тяжёлая подставка
            b.Cylinder(new Vector3(0, 0.07f * m, 0), 0.22f * m, 0.06f * m, 20, BaseColor * 1.1f);
            b.Cylinder(new Vector3(0, 0.13f * m, 0), 0.07f * m, 0.12f * m, 12, BaseColor * 1.2f);  // втулка
            b.Cylinder(new Vector3(0, 0.13f * m, 0), 0.028f * m, H - 0.13f * m, 10, PoleColor);    // шест
            Mesh mesh = b.Build("UmbrellaPole");
            MeshFilter mf = _poleT.GetComponent<MeshFilter>();
            if (mf.sharedMesh != null && mf.sharedMesh.name == "UmbrellaPole") DestroyMesh(mf.sharedMesh);
            mf.sharedMesh = mesh;
        }

        /// <summary>Купол при раскрытии o (0 — сложен вдоль шеста, 1 — раскрыт). Пересобирается только при движении.</summary>
        private void BuildCanopy(float m)
        {
            float o = Mathf.Clamp(_open, -0.02f, 1.08f);
            float H = Height * m, R = CanopyRadius * m;
            int n = Mathf.Clamp(Ribs, 6, 12);
            // угол спиц от горизонтали: сложен −84° (висят вдоль шеста), раскрыт −12° (лёгкий купол)
            float ang = Mathf.Lerp(-84f, -12f, o) * Mathf.Deg2Rad;
            float L = R / Mathf.Cos(12f * Mathf.Deg2Rad);
            Vector3 hub = new Vector3(0, H, 0);
            float o01 = Mathf.Clamp01(o);
            float pleat = Mathf.Lerp(0.8f, 1f, o01);                                 // ткань между спицами чуть собирается
            float fold = 0.085f * R * (1f - o01);                                    // сложен: складки ткани наружу между спицами
            float bulge = 0.12f * R * Mathf.Clamp01(o);                              // купол чуть выпуклый
            CityToonMesh b = new CityToonMesh();
            const int segs = 3;                                                      // точек по клину (фестоны)
            for (int i = 0; i < n; i++)
            {
                Color c = (i % 2 == 0) ? StripeA : StripeB;
                float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                // два кольца: середина и край; между спицами ткань провисает/собирается
                Vector3[] mid = new Vector3[segs + 1], rim = new Vector3[segs + 1];
                for (int k = 0; k <= segs; k++)
                {
                    float t = k / (float)segs;
                    float az = Mathf.Lerp(a0, a1, t);
                    float between = Mathf.Sin(t * Mathf.PI);                         // 0 у спиц, 1 посередине клина
                    float rs = Mathf.Lerp(1f, pleat, between);
                    Vector3 dir = new Vector3(Mathf.Cos(az) * Mathf.Cos(ang), Mathf.Sin(ang), Mathf.Sin(az) * Mathf.Cos(ang));
                    Vector3 outw = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az)) * between;
                    Vector3 pr = hub + dir * L * rs + outw * fold;
                    pr.y -= between * 0.05f * R * o01;                               // фестон: край между спицами чуть ниже
                    Vector3 pm = hub + dir * L * 0.52f * Mathf.Lerp(1f, pleat, between) + Vector3.up * bulge + outw * fold * 0.55f;
                    mid[k] = pm; rim[k] = pr;
                }
                for (int k = 0; k < segs; k++)
                {
                    b.Tri(hub + Vector3.up * bulge * 0.6f, mid[k], mid[k + 1], c);
                    b.Quad(mid[k], rim[k], rim[k + 1], mid[k + 1], c);
                    // бахрома: короткая юбочка с зубцами по краю
                    // бахрома висит всегда (сложенный — короче); нулевой длины не бывает — иначе грани без нормали и вспышка
                    Vector3 down = Vector3.down * 0.11f * R * Mathf.Lerp(0.5f, 1f, o01);
                    Vector3 dz = Vector3.down * 0.05f * R * Mathf.Lerp(0.5f, 1f, o01);
                    Vector3 midEdge = (rim[k] + rim[k + 1]) * 0.5f + down + dz;
                    b.Quad(rim[k], rim[k] + down, rim[k + 1] + down, rim[k + 1], c);
                    b.Tri(rim[k] + down, midEdge, rim[k + 1] + down, c);
                }
            }
            // ткань двусторонняя: построенные грани смотрят вниз/внутрь — это изнанка (темнее),
            // их копия наоборот — лицевая сторона (вверх/наружу, яркая)
            int tc = b.T.Count;
            int vc0 = b.V.Count;
            for (int t = 0; t < tc; t += 3)
            {
                int i0 = b.T[t], i1 = b.T[t + 1], i2 = b.T[t + 2];
                int baseI = b.V.Count;
                int[] ord = { i0, i2, i1 };
                for (int k = 0; k < 3; k++)
                {
                    b.V.Add(b.V[ord[k]]); b.N.Add(-b.N[ord[k]]);
                    b.C.Add(b.C[ord[k]]);
                    b.UV.Add(b.UV.Count > ord[k] ? b.UV[ord[k]] : Vector2.zero);
                }
                b.T.Add(baseI); b.T.Add(baseI + 1); b.T.Add(baseI + 2);
            }
            for (int v = 0; v < vc0; v++) { Color cc = b.C[v]; b.C[v] = new Color(cc.r * 0.72f, cc.g * 0.7f, cc.b * 0.68f, cc.a); }
            // спицы под тканью и шишечка сверху
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2f / n;
                Vector3 under = Vector3.down * 0.03f * m;
                b.Beam(hub + under, hub + under + new Vector3(Mathf.Cos(a0) * Mathf.Cos(ang), Mathf.Sin(ang), Mathf.Sin(a0) * Mathf.Cos(ang)) * L * 0.95f, 0.012f * m, PoleColor * 0.9f);
            }
            b.Ellipsoid(hub + Vector3.up * (bulge * 0.6f + 0.05f * m), 0.055f * m, 0.08f * m, 0.055f * m, 6, 10, (u, w) => PoleColor, null);
            Mesh mesh = b.Build("UmbrellaCanopy");
            FixNormals(mesh);
            MeshFilter mf = _canopyT.GetComponent<MeshFilter>();
            if (_canopy != null) DestroyMesh(_canopy);
            _canopy = mesh;
            mf.sharedMesh = mesh;
            _builtOpen = _open;
        }

        /// <summary>Нулевые/битые нормали (вырожденные грани при складывании) → вверх, чтобы шейдер не давал NaN и вспышку.</summary>
        private static void FixNormals(Mesh mesh)
        {
            Vector3[] nr = mesh.normals;
            bool bad = false;
            for (int i = 0; i < nr.Length; i++)
            {
                Vector3 v = nr[i];
                if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || v.sqrMagnitude < 1e-8f) { nr[i] = Vector3.up; bad = true; }
            }
            if (bad) mesh.normals = nr;
        }

        private void UpdateBlocker()
        {
            if (_blocker == null) return;
            float m = Meter() * Scale;
            float o = Mathf.Clamp01(_open);
            float H = Height * m, R = CanopyRadius * m;
            float ang = Mathf.Lerp(-84f, -12f, o) * Mathf.Deg2Rad;
            float L = R / Mathf.Cos(12f * Mathf.Deg2Rad);
            float reach = Mathf.Cos(ang) * L;                                        // радиус проекции купола
            _blocker.Active = o > 0.35f;
            _blocker.Radius = reach * 0.97f / Mathf.Max(Mathf.Abs(transform.lossyScale.x), 1e-3f);
            _blocker.LocalCenter = new Vector3(0f, (H + Mathf.Sin(ang) * L) / Mathf.Max(Mathf.Abs(transform.lossyScale.y), 1e-3f), 0f);
        }

        private static void DestroyMesh(Mesh m)
        {
            if (m == null) return;
            if (Application.isPlaying) Destroy(m); else DestroyImmediate(m);
        }

        private void OnDestroy() { DestroyMesh(_canopy); }
    }
}
