using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CatCooks.Level
{
    /// <summary>
    /// СВЕТ ПРОПСА — фонарь светит (лампа, ореол, конус вниз, пятно на земле), щит подсвечен (лампы сверху и заливка лица),
    /// антенна мигает красным сверху (и посередине, если высокая), вертолётная площадка — огни по кругу бегущей волной.
    /// Только для списка моделей (Table): lamppillar, roadbillboard, billboard1–4, little_billboard1–5, antenna1–2,
    /// helicopter_landing_area. Вешается само: на всё, что уже стоит в сцене, и на то, что ставишь руками
    /// (редактор следит за иерархией), плюс в игре при загрузке сцены. Настоящих источников света нет —
    /// один прибавочный меш на пропс (шейдер «Prop Glow»), ночью ярко, днём фонари и щиты выключены, мигалки тусклее.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("CatCooks/Город/Свет пропса (фонарь, щит, антенна)")]
    public sealed class CityPropGlow : MonoBehaviour
    {
        public enum GlowKind { Auto = 0, Lamp = 1, Billboard = 2, Antenna = 3, Helipad = 4 }

        public const string GlowName = "CC_PropGlow";
        /// <summary>Части света: 0 — всегда (мигалки), 1 — ночью (лампы, ореолы, заливка щитов), 2 — ночью и только вблизи (пятна на земле, конусы).</summary>
        public static readonly string[] PartNames = { "CC_PropGlow", "CC_PropGlow_N", "CC_PropGlow_F" };

        [Tooltip("Что это (Auto — по имени модели)")]
        public GlowKind Kind = GlowKind.Auto;
        [Tooltip("Общая сила")]
        [Range(0f, 3f)] public float Strength = 1f;
        [Tooltip("Цвет света (фонари, щиты). Мигалки антенн — красные, площадка — зелёно-жёлтая")]
        public Color LightColor = new Color(1f, 0.83f, 0.55f, 1f);
        [Tooltip("Пятно света на земле под фонарём")]
        public bool GroundPool = true;
        [Tooltip("Конус света от лампы вниз")]
        public bool LightCone = true;
        [Tooltip("Материал (ставит меню; пусто — создаётся сам)")]
        public Material GlowMaterial;

        [SerializeField, HideInInspector] private int _builtHash;
        [SerializeField, HideInInspector] private bool _approx;

        /// <summary>Собрано по габаритам (меш модели не читается — включи Read/Write, меню делает само).</summary>
        public bool Approx { get { return _approx; } }

        // ── список моделей: имя (без _ пробелов и регистра) → вид ──
        private static readonly Dictionary<string, GlowKind> Table = new Dictionary<string, GlowKind>
        {
            { "lamppillar", GlowKind.Lamp },
            { "roadbillboard", GlowKind.Billboard },
            { "billboard1", GlowKind.Billboard }, { "billboard2", GlowKind.Billboard },
            { "billboard3", GlowKind.Billboard }, { "billboard4", GlowKind.Billboard },
            { "littlebillboard1", GlowKind.Billboard }, { "littlebillboard2", GlowKind.Billboard },
            { "littlebillboard3", GlowKind.Billboard }, { "littlebillboard4", GlowKind.Billboard },
            { "littlebillboard5", GlowKind.Billboard },
            { "antenna1", GlowKind.Antenna }, { "antenna2", GlowKind.Antenna },
            { "helicopterlandingarea", GlowKind.Helipad },
        };

        /// <summary>Имя → ключ списка: без «(Clone)», « (3)», регистра, _, пробелов, -, точек.</summary>
        public static string Key(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string n = name.ToLowerInvariant().Replace("(clone)", "");
            int br = n.LastIndexOf(" (");
            if (br > 0 && n.EndsWith(")")) n = n.Substring(0, br);
            System.Text.StringBuilder sb = new System.Text.StringBuilder(n.Length);
            foreach (char ch in n) if (ch != '_' && ch != ' ' && ch != '-' && ch != '.') sb.Append(ch);
            return sb.ToString();
        }

        public static bool Match(string name, out GlowKind kind) { return Table.TryGetValue(Key(name), out kind); }

        public GlowKind ResolvedKind()
        {
            if (Kind != GlowKind.Auto) return Kind;
            GlowKind k;
            return Match(gameObject.name, out k) ? k : GlowKind.Lamp;
        }

        /// <summary>Единиц сцены в метре: из «Пропсы и машины», иначе из дождя, иначе 3.5.</summary>
        public static float CityMeter()
        {
            CityBlockGenerator g = FindFirstObjectByType<CityBlockGenerator>();
            CityProps p = g != null ? g.GetComponent<CityProps>() : null;
            if (p != null) return p.Meter();
            if (CityRain.Instance != null && CityRain.Instance.Meter > 0f) return CityRain.Instance.Meter;
            return 3.5f;
        }

        /// <summary>Можно ли вешать свет на этот объект: имя из списка и нет света выше/ниже по иерархии.</summary>
        public static bool Eligible(GameObject go)
        {
            GlowKind k;
            if (go == null || !Match(go.name, out k)) return false;
            if (go.GetComponentInParent<CityPropGlow>(true) != null) return false;
            if (go.GetComponentInChildren<CityPropGlow>(true) != null) return false;
            return go.GetComponentInChildren<MeshFilter>(true) != null;
        }

        /// <summary>Пройтись по сцене и повесить свет на всё подходящее. Возвращает, сколько добавлено.</summary>
        public static int AttachAll(Material mat)
        {
            int n = 0;
            List<GameObject> found = new List<GameObject>();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene sc = SceneManager.GetSceneAt(s);
                if (!sc.isLoaded) continue;
                foreach (GameObject root in sc.GetRootGameObjects())
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        GlowKind k;
                        if (Match(t.name, out k)) found.Add(t.gameObject);
                    }
            }
            foreach (GameObject go in found)
            {
                if (!Eligible(go)) continue;
                CityPropGlow g = go.AddComponent<CityPropGlow>();
                if (mat != null) g.GlowMaterial = mat;
                g.Rebuild();
                n++;
            }
            return n;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RuntimeAttach()
        {
            Material mat = null;
            CityPropGlow any = FindFirstObjectByType<CityPropGlow>();
            if (any != null) mat = any.GlowMaterial;
            AttachAll(mat);
            CityGlowBatcher.Run();                                             // в игре: склеить свет в куски, днём ночное выключать
        }

        private void OnEnable()
        {
            if (_builtHash != Hash() || (transform.Find(PartNames[0]) == null && transform.Find(PartNames[1]) == null)) Rebuild();
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.delayCall += DelayedRebuild;
#endif
        }

        private void DelayedRebuild() { if (this != null && _builtHash != Hash()) Rebuild(); }

        private int Hash()
        {
            unchecked
            {
                int h = (int)Kind * 31 + Mathf.RoundToInt(Strength * 100f);
                h = h * 31 + LightColor.GetHashCode();
                h = h * 31 + (GroundPool ? 1 : 0) + (LightCone ? 2 : 0);
                return h * 31 + 8;     // версия формы (8 — три части: всегда / ночь / ночь вблизи)
            }
        }

        private Material Mat()
        {
            if (GlowMaterial != null) return GlowMaterial;
            Shader sh = Shader.Find("CatCooks/City/Prop Glow");
            if (sh == null) return null;
            GlowMaterial = new Material(sh) { name = "Prop Glow (auto)", hideFlags = HideFlags.DontSave };
            return GlowMaterial;
        }

        // ═════════════════════════ сборка ═════════════════════════

        private sealed class Part
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<Vector2> U = new List<Vector2>();
            public readonly List<Vector4> P = new List<Vector4>();
            public readonly List<Vector4> Q = new List<Vector4>();
            public readonly List<int> T = new List<int>();
        }

        private sealed class B
        {
            public readonly Part[] Parts = { new Part(), new Part(), new Part() };
            public Transform Root;
            private Part _p;
            public int Count { get { return Parts[0].V.Count + Parts[1].V.Count + Parts[2].V.Count; } }

            private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Color col, Vector4 p, Vector4 q)
            {
                List<Vector3> V = _p.V; List<Vector2> U = _p.U; List<Color> C = _p.C; List<Vector4> P = _p.P, Q = _p.Q; List<int> T = _p.T;
                int s = V.Count;
                V.Add(a); V.Add(b); V.Add(c); V.Add(d);
                U.Add(ua); U.Add(ub); U.Add(uc); U.Add(ud);
                for (int i = 0; i < 4; i++) { C.Add(col); P.Add(p); Q.Add(q); }
                T.Add(s); T.Add(s + 1); T.Add(s + 2); T.Add(s); T.Add(s + 2); T.Add(s + 3);
            }

            private Vector3 L(Vector3 w) { return Root.InverseTransformPoint(w); }

            /// <summary>Ореол к камере в точке (мир), размер — мир. type 0 — мягкий, 1 — ядро.</summary>
            public void Halo(Vector3 w, float size, Color col, float type, bool night, float blink, float phase, float duty)
            {
                Vector3 c = L(w);
                Vector4 p = new Vector4(type, night ? 1f : 0f, blink, phase), q = new Vector4(size, duty, 0f, 0f);
                _p = Parts[night ? 1 : 0];
                Quad(c, c, c, c, new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1), col, p, q);
            }

            /// <summary>Горизонтальный круг (пятно на земле) — мир.</summary>
            public void Pool(Vector3 w, float r, Color col, bool night)
            {
                Vector4 p = new Vector4(0f, night ? 1f : 0f, 0f, 0f);
                _p = Parts[night ? 2 : 0];
                Quad(L(w + new Vector3(-r, 0, -r)), L(w + new Vector3(-r, 0, r)), L(w + new Vector3(r, 0, r)), L(w + new Vector3(r, 0, -r)),
                     new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1), col, p, new Vector4(0f, 0f, night ? 1f : 0f, 0f));
            }

            /// <summary>Вертикальная полоса-градиент от top (ярко) вниз к bottom: ширина wTop → wBot, вдоль оси side (мир).</summary>
            public void Gradient(Vector3 top, Vector3 bottom, Vector3 side, float wTop, float wBot, Color col, bool night, bool nearOnly)
            {
                Vector4 p = new Vector4(2f, night ? 1f : 0f, 0f, 0f);
                _p = Parts[!night ? 0 : (nearOnly ? 2 : 1)];
                Quad(L(bottom - side * wBot), L(top - side * wTop), L(top + side * wTop), L(bottom + side * wBot),
                     new Vector2(-1, 0), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, 0), col, p, new Vector4(0f, 0f, nearOnly ? 1f : 0f, 0f));
            }

            public Mesh Build(int part)
            {
                Part q = Parts[part];
                if (q.V.Count == 0) return null;
                Mesh m = new Mesh { name = PartNames[part] };
                m.SetVertices(q.V); m.SetColors(q.C); m.SetUVs(0, q.U); m.SetUVs(1, q.P); m.SetUVs(2, q.Q);
                m.SetTriangles(q.T, 0);
                m.RecalculateBounds();
                Bounds b = m.bounds; b.Expand(4f);                                   // ореолы раздуваются в шейдере
                m.bounds = b;
                return m;
            }
        }

        /// <summary>Вершины модели (мир). Если меш не читается — углы габаритов.</summary>
        private List<Vector3> WorldVerts(out Bounds wb)
        {
            List<Vector3> pts = new List<Vector3>();
            wb = new Bounds(transform.position, Vector3.zero);
            bool any = false;
            _approx = false;
            List<Vector3> tmp = new List<Vector3>();
            foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null || mf.gameObject.name.StartsWith(GlowName)) continue;
                Mesh m = mf.sharedMesh;
                Matrix4x4 w = mf.transform.localToWorldMatrix;
                if (m.isReadable)
                {
                    tmp.Clear();
                    m.GetVertices(tmp);
                    int step = Mathf.Max(1, tmp.Count / 4000);
                    for (int i = 0; i < tmp.Count; i += step) pts.Add(w.MultiplyPoint3x4(tmp[i]));
                }
                else
                {
                    _approx = true;
                    Bounds b = m.bounds;
                    for (int i = 0; i < 8; i++)
                        pts.Add(w.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                }
            }
            foreach (Vector3 p in pts) { if (!any) { wb = new Bounds(p, Vector3.zero); any = true; } else wb.Encapsulate(p); }
            return pts;
        }

        [ContextMenu("Пересобрать свет")]
        public void Rebuild()
        {
            foreach (string pn in PartNames)
            {
                Transform old = transform.Find(pn);
                if (old == null) continue;
                if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
            }
            Bounds wb;
            List<Vector3> pts = WorldVerts(out wb);
            if (pts.Count == 0) return;
            float M = CityMeter();
            B b = new B { Root = transform };
            switch (ResolvedKind())
            {
                case GlowKind.Lamp: Lamp(b, pts, wb, M); break;
                case GlowKind.Billboard: Board(b, pts, M); break;
                case GlowKind.Antenna: Antenna(b, pts, wb, M); break;
                case GlowKind.Helipad: Helipad(b, pts, wb, M); break;
            }
            if (b.Count == 0) return;
            for (int part = 0; part < 3; part++)
            {
                Mesh pm = b.Build(part);
                if (pm == null) continue;
                GameObject go = new GameObject(PartNames[part]);
                go.hideFlags = HideFlags.NotEditable;
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = pm;
                MeshRenderer r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = Mat();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.allowOcclusionWhenDynamic = true;
            }
            _builtHash = Hash();
        }

        private Color Col(Color c, float a) { return new Color(c.r, c.g, c.b, a * Strength); }

        // ── фонарь: лампа на конце кронштейна (или наверху), ореол, конус вниз, пятно на земле ──
        private void Lamp(B b, List<Vector3> pts, Bounds wb, float M)
        {
            float H = Mathf.Max(wb.size.y, 0.01f), y0 = wb.min.y;
            Vector3 axis = Vector3.zero; int na = 0;
            foreach (Vector3 p in pts) if (p.y < y0 + 0.06f * H) { axis += p; na++; }
            axis = na > 0 ? axis / na : wb.center;
            Vector2 ax = new Vector2(axis.x, axis.z);
            // самая дальняя от столба точка в верхней части — голова лампы
            float dMax = 0f; Vector3 far = wb.center;
            foreach (Vector3 p in pts)
            {
                if (p.y < y0 + 0.55f * H) continue;
                float d = (new Vector2(p.x, p.z) - ax).magnitude;
                if (d > dMax) { dMax = d; far = p; }
            }
            List<Vector3> heads = new List<Vector3>();
            if (dMax > 0.12f * H)
            {
                heads.Add(far);
                Vector2 dir = (new Vector2(far.x, far.z) - ax).normalized;
                float d2 = 0f; Vector3 far2 = far;
                foreach (Vector3 p in pts)                                        // вторая голова — с другой стороны
                {
                    if (p.y < y0 + 0.55f * H) continue;
                    Vector2 v = new Vector2(p.x, p.z) - ax;
                    if (Vector2.Dot(v.normalized, dir) > -0.4f) continue;
                    if (v.magnitude > d2) { d2 = v.magnitude; far2 = p; }
                }
                if (d2 > 0.6f * dMax) heads.Add(far2);
            }
            else heads.Add(new Vector3(ax.x, wb.max.y - 0.07f * H, ax.y));
            foreach (Vector3 h in heads)
            {
                // низ головы над этой точкой — оттуда светит
                float r = Mathf.Max(0.12f * H, 0.25f * M), yb = h.y;
                Vector2 hx = new Vector2(h.x, h.z);
                foreach (Vector3 p in pts)
                    if (p.y > y0 + 0.5f * H && (new Vector2(p.x, p.z) - hx).magnitude < r) yb = Mathf.Min(yb, p.y);
                Vector3 bulb = new Vector3(h.x, yb - 0.04f * M, h.z);
                float hb = Mathf.Max(bulb.y - y0, 1f * M);
                b.Halo(bulb, 0.28f * M, Col(new Color(1f, 0.97f, 0.88f), 2.2f), 1f, true, 0f, 0f, 0f);       // ядро
                b.Halo(bulb, 1.5f * M, Col(LightColor, 0.55f), 0f, true, 0f, 0f, 0f);                      // ореол
                float poolR = Mathf.Clamp(hb * 0.55f, 2f * M, 6f * M);
                if (LightCone)
                {
                    Vector3 ground = new Vector3(bulb.x, y0 + 0.05f * M, bulb.z);
                    b.Gradient(bulb, ground, Vector3.right, 0.18f * M, poolR * 0.75f, Col(LightColor, 0.16f), true, true);
                    b.Gradient(bulb, ground, Vector3.forward, 0.18f * M, poolR * 0.75f, Col(LightColor, 0.16f), true, true);
                }
                if (GroundPool) b.Pool(new Vector3(bulb.x, y0 + 0.04f * M, bulb.z), poolR, Col(LightColor, 0.55f), true);
            }
        }

        // ── щит: доска (там, где модель широкая), лампы над верхним краем с двух сторон и заливка лица ──
        private void Board(B b, List<Vector3> wpts, float M)
        {
            List<Vector3> pts = new List<Vector3>(wpts.Count);
            foreach (Vector3 p in wpts) pts.Add(transform.InverseTransformPoint(p));        // в осях модели — доска ровная
            Bounds lb = new Bounds(pts[0], Vector3.zero);
            foreach (Vector3 p in pts) lb.Encapsulate(p);
            float H = Mathf.Max(lb.size.y, 1e-4f);
            const int N = 16;
            Vector2[] xr = new Vector2[N], zr = new Vector2[N];
            for (int i = 0; i < N; i++) { xr[i] = new Vector2(float.MaxValue, float.MinValue); zr[i] = xr[i]; }
            foreach (Vector3 p in pts)
            {
                int i = Mathf.Clamp((int)((p.y - lb.min.y) / H * N), 0, N - 1);
                xr[i] = new Vector2(Mathf.Min(xr[i].x, p.x), Mathf.Max(xr[i].y, p.x));
                zr[i] = new Vector2(Mathf.Min(zr[i].x, p.z), Mathf.Max(zr[i].y, p.z));
            }
            float wMax = 0f;
            for (int i = 0; i < N; i++) if (xr[i].x <= xr[i].y) wMax = Mathf.Max(wMax, Mathf.Max(xr[i].y - xr[i].x, zr[i].y - zr[i].x));
            int i0 = N, i1 = -1;
            Vector2 bx = new Vector2(float.MaxValue, float.MinValue), bz = bx;
            for (int i = 0; i < N; i++)
            {
                if (xr[i].x > xr[i].y) continue;
                if (Mathf.Max(xr[i].y - xr[i].x, zr[i].y - zr[i].x) < 0.6f * wMax) continue;
                i0 = Mathf.Min(i0, i); i1 = Mathf.Max(i1, i);
                bx = new Vector2(Mathf.Min(bx.x, xr[i].x), Mathf.Max(bx.y, xr[i].y));
                bz = new Vector2(Mathf.Min(bz.x, zr[i].x), Mathf.Max(bz.y, zr[i].y));
            }
            if (i1 < 0) return;
            float yb = lb.min.y + i0 * H / N, yt = lb.min.y + (i1 + 1) * H / N;
            bool thinX = (bx.y - bx.x) < (bz.y - bz.x);
            Vector3 nrm = thinX ? Vector3.right : Vector3.forward, side = thinX ? Vector3.forward : Vector3.right;
            float half = thinX ? (bz.y - bz.x) * 0.5f : (bx.y - bx.x) * 0.5f;
            float thick = thinX ? (bx.y - bx.x) * 0.5f : (bz.y - bz.x) * 0.5f;
            Vector3 c = new Vector3((bx.x + bx.y) * 0.5f, 0f, (bz.x + bz.y) * 0.5f);
            // перевести размеры в мир (масштаб пропса)
            float sc = Mathf.Max(1e-4f, (transform.TransformVector(side).magnitude + transform.TransformVector(nrm).magnitude) * 0.5f);
            float boardH = yt - yb;
            int lamps = Mathf.Clamp(Mathf.RoundToInt(half * 2f * sc / (2.4f * M)), 1, 4);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 face = c + nrm * (s * (thick + 0.03f * M / sc));
                Vector3 topL = face + Vector3.up * yt, botL = face + Vector3.up * yb;
                // заливка лица: ярко сверху, к низу слабее
                Vector3 topW = transform.TransformPoint(topL), botW = transform.TransformPoint(botL);
                Vector3 sideW = transform.TransformDirection(side).normalized;
                b.Gradient(topW, botW, sideW, half * sc, half * sc, Col(LightColor, 0.38f), true, false);
                // лампы над верхним краем, вынесены вперёд
                for (int i = 0; i < lamps; i++)
                {
                    float t = lamps > 1 ? (i / (float)(lamps - 1) - 0.5f) * 1.5f * half : 0f;
                    Vector3 lp = transform.TransformPoint(c + nrm * (s * (thick + Mathf.Max(0.5f * M / sc, boardH * 0.12f))) + side * t + Vector3.up * (yt + 0.12f * M / sc));
                    b.Halo(lp, 0.18f * M, Col(new Color(1f, 0.97f, 0.9f), 2f), 1f, true, 0f, 0f, 0f);
                    b.Halo(lp, 0.9f * M, Col(LightColor, 0.35f), 0f, true, 0f, 0f, 0f);
                }
            }
        }

        // ── антенна: красная мигалка на самом верху, ещё одна посередине у высоких ──
        private void Antenna(B b, List<Vector3> pts, Bounds wb, float M)
        {
            Vector3 top = wb.center; float ty = float.MinValue;
            foreach (Vector3 p in pts) if (p.y > ty) { ty = p.y; top = p; }
            Color red = new Color(1f, 0.12f, 0.08f);
            float ph = Mathf.Repeat(transform.position.x * 0.137f + transform.position.z * 0.071f, 1f);
            Vector3 t = top + Vector3.up * 0.12f * M;
            b.Halo(t, 0.3f * M, Col(new Color(1f, 0.55f, 0.5f), 2.5f), 1f, false, 0.8f, ph, 0.35f);
            b.Halo(t, 2.2f * M, Col(red, 0.9f), 0f, false, 0.8f, ph, 0.35f);
            float H = wb.size.y;
            if (H > 7f * M)
            {
                float ym = wb.min.y + H * 0.55f; Vector3 mid = Vector3.zero; int n = 0;
                foreach (Vector3 p in pts) if (Mathf.Abs(p.y - ym) < H * 0.04f) { mid += p; n++; }
                if (n > 0)
                {
                    mid /= n;
                    b.Halo(mid, 0.25f * M, Col(new Color(1f, 0.55f, 0.5f), 2f), 1f, false, 0.8f, ph + 0.5f, 0.35f);
                    b.Halo(mid, 1.6f * M, Col(red, 0.7f), 0f, false, 0.8f, ph + 0.5f, 0.35f);
                }
            }
        }

        // ── вертолётная площадка: огни по кругу (зелёный/жёлтый), бегущая волна ──
        private void Helipad(B b, List<Vector3> pts, Bounds wb, float M)
        {
            Vector2 c = new Vector2(wb.center.x, wb.center.z);
            float ext = Mathf.Min(wb.size.x, wb.size.z) * 0.5f, top = wb.min.y;
            foreach (Vector3 p in pts) if ((new Vector2(p.x, p.z) - c).magnitude < ext * 0.4f) top = Mathf.Max(top, p.y);
            float R = ext * 0.85f;
            const int N = 12;
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                Vector3 p = new Vector3(c.x + Mathf.Cos(a) * R, top + 0.15f * M, c.y + Mathf.Sin(a) * R);
                Color col = (i % 2 == 0) ? new Color(0.25f, 1f, 0.35f) : new Color(1f, 0.85f, 0.2f);
                float ph = 1f - i / (float)N;
                b.Halo(p, 0.22f * M, Col(Color.Lerp(col, Color.white, 0.5f), 2f), 1f, false, 0.5f, ph, 0.5f);
                b.Halo(p, 1.1f * M, Col(col, 0.6f), 0f, false, 0.5f, ph, 0.5f);
            }
            b.Pool(new Vector3(c.x, top + 0.05f * M, c.y), R * 0.9f, Col(new Color(0.6f, 0.9f, 1f), 0.12f), true);
        }
    }
}
