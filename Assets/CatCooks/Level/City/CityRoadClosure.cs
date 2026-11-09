using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CatCooks.Level
{
    /// <summary>
    /// ПЕРЕКРЁСТОК ЗАКРЫТ (ПОД КРАН). Ставится в угол модулей — в центр перекрёстка (сам прилипает к ближайшему).
    /// Кнопка «Построить»:
    ///   • на каждом из 4 въездов — ряд конусов поперёк дороги, барьер в красно-белую полоску и знак «внимание»
    ///     (конусы и знак — из списка «Пропсы и машины», если там есть; иначе — свои мультяшные);
    ///   • машины не едут через перекрёсток: круг квартала у соседних модулей заменяется овалами вдоль улиц —
    ///     доехали до конусов, развернулись, уехали до следующего перекрёстка, развернулись там и обратно.
    /// Площадка = весь перекрёсток (жёлтый квадрат в сцене) — туда ставишь кран сам.
    /// Перерасставил пропсы или перестроил модуль — разворот применяется заново сам.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("CatCooks/Город/Перекрёсток закрыт (под кран)")]
    public sealed class CityRoadClosure : MonoBehaviour
    {
        public const string GenName = "Closure (generated)";
        public const string OvalPrefix = "Traffic (U-turn) ";

        public CityBlockGenerator City;

        [Header("Конусы и знаки")]
        [Tooltip("Свой конус (пусто — из списка «Пропсы и машины», нет там — мультяшный)")]
        public GameObject ConePrefab;
        [Tooltip("Свой знак (пусто — из списка «Пропсы и машины»: знак со словами work/closed/stop/warning…, нет — треугольник «внимание»)")]
        public GameObject SignPrefab;
        [Tooltip("Барьер в красно-белую полоску на каждом въезде")]
        public bool Barriers = true;
        [Tooltip("Треугольный знак «внимание» у барьера (если нет своего знака)")]
        public bool WarningSigns = true;
        [Tooltip("Шаг конусов поперёк дороги, м")]
        [Range(0.8f, 4f)] public float ConeStep = 1.6f;
        [Tooltip("Отступ ряда конусов от края перекрёстка (доля полудороги)")]
        [Range(0f, 1f)] public float Gap = 0.2f;
        [Tooltip("Второй ряд конусов вдоль бордюров (воронкой к барьеру)")]
        public bool Funnel = true;

        [Header("Машины")]
        [Tooltip("Машины разворачиваются перед перекрёстком (вместо круга квартала — овалы вдоль улиц)")]
        public bool UTurn = true;

        public bool Log = true;

        // кто из модулей вокруг (квадранты: 0 (+x,+z), 1 (-x,+z), 2 (-x,-z), 3 (+x,-z)) и чьи улицы
        [SerializeField, HideInInspector] private string[] _quad = new string[4];
        [SerializeField, HideInInspector] private string[] _armOwner = new string[4];   // руки: 0 +X, 1 +Z, 2 -X, 3 -Z

        private static readonly int[] QSX = { 1, -1, -1, 1 };
        private static readonly int[] QSZ = { 1, 1, -1, -1 };

        public CityBlockGenerator Gen()
        {
            if (City == null) City = FindFirstObjectByType<CityBlockGenerator>();
            return City;
        }

        public float Meter()
        {
            CityBlockGenerator g = Gen();
            CityProps p = g != null ? g.GetComponent<CityProps>() : null;
            return p != null ? p.Meter() : 3.5f;
        }

        /// <summary>Полуширина дороги: вдоль оси X (рука ±X — дорога между модулями по Z) и вдоль Z.</summary>
        public void HalfRoad(out float hx, out float hz)
        {
            CityBlockGenerator g = Gen();
            Vector2 ms = g != null ? g.ModuleSize() : new Vector2(250f, 250f);
            float rm = g != null ? g.RoadMargin : 0.085f;
            hx = ms.x * rm;          // перекрёсток по X: |x| < hx (это ширина дорог, идущих вдоль Z)
            hz = ms.y * rm;
        }

        /// <summary>Прилипнуть к ближайшему углу модулей (центр перекрёстка) и повернуться по сетке.</summary>
        public bool Snap()
        {
            CityBlockGenerator g = Gen();
            if (g == null) return false;
            Vector2 ms = g.ModuleSize();
            float best = float.MaxValue; Vector3 bp = transform.position; Quaternion br = transform.rotation;
            for (int z = 0; z < g.Depth; z++)
                for (int x = 0; x < g.Width; x++)
                {
                    Vector3 c; Quaternion r;
                    g.ModuleFrame(x, z, out c, out r);
                    for (int q = 0; q < 4; q++)
                    {
                        Vector3 p = c + r * new Vector3(QSX[q] * ms.x * 0.5f, 0f, QSZ[q] * ms.y * 0.5f);
                        Vector3 d = p - transform.position; d.y = 0f;
                        if (d.sqrMagnitude < best) { best = d.sqrMagnitude; bp = p; br = r; }
                    }
                }
            transform.SetPositionAndRotation(bp, br);
            // модули вокруг угла
            for (int q = 0; q < 4; q++)
            {
                _quad[q] = "";
                Vector3 want = bp + br * new Vector3(QSX[q] * ms.x * 0.5f, 0f, QSZ[q] * ms.y * 0.5f);
                for (int z = 0; z < g.Depth; z++)
                    for (int x = 0; x < g.Width; x++)
                    {
                        Vector3 c; Quaternion r;
                        g.ModuleFrame(x, z, out c, out r);
                        Vector3 d = c - want; d.y = 0f;
                        if (d.magnitude < Mathf.Min(ms.x, ms.y) * 0.25f) _quad[q] = CityBlockGenerator.ModuleName(x, z) + "|" + (g.GetRole(x, z) == CityRole.Ring1 ? "1" : "0");
                    }
            }
            // хозяин каждой улицы-руки — модуль ближнего слоя (с машинами); у руки два соседа, берём первого с машинами
            for (int a = 0; a < 4; a++)
            {
                int qa, qb;
                ArmQuads(a, out qa, out qb);
                _armOwner[a] = Ring1(qa) ? Name(qa) : (Ring1(qb) ? Name(qb) : "");
            }
            return true;
        }

        private bool Ring1(int q) { return !string.IsNullOrEmpty(_quad[q]) && _quad[q].EndsWith("|1"); }
        private string Name(int q) { string s = _quad[q]; if (string.IsNullOrEmpty(s)) return ""; int i = s.IndexOf('|'); return i > 0 ? s.Substring(0, i) : s; }

        /// <summary>Квадранты по обе стороны руки a.</summary>
        private static void ArmQuads(int a, out int qa, out int qb)
        {
            switch (a)
            {
                case 0: qa = 0; qb = 3; break;   // +X: (+,+) и (+,-)
                case 1: qa = 0; qb = 1; break;   // +Z: (+,+) и (-,+)
                case 2: qa = 1; qb = 2; break;   // -X
                default: qa = 3; qb = 2; break;  // -Z
            }
        }

        private static Vector2 ArmDir(int a)
        {
            switch (a) { case 0: return new Vector2(1, 0); case 1: return new Vector2(0, 1); case 2: return new Vector2(-1, 0); default: return new Vector2(0, -1); }
        }

        private void OnDrawGizmos()
        {
            float hx, hz;
            HalfRoad(out hx, out hz);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.9f);
            Gizmos.DrawWireCube(Vector3.up * 0.2f, new Vector3(hx * 2f, 0.1f, hz * 2f));
            Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.15f);
            Gizmos.DrawCube(Vector3.up * 0.1f, new Vector3(hx * 2f, 0.05f, hz * 2f));
            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.9f);
            Gizmos.DrawLine(new Vector3(-hx, 0.3f, -hz), new Vector3(hx, 0.3f, hz));
            Gizmos.DrawLine(new Vector3(-hx, 0.3f, hz), new Vector3(hx, 0.3f, -hz));
            Gizmos.matrix = Matrix4x4.identity;
        }

#if UNITY_EDITOR
        // ═════════════════════════ постройка ═════════════════════════

        private Transform GenRoot(bool create)
        {
            Transform t = transform.Find(GenName);
            if (t == null && create)
            {
                GameObject go = new GameObject(GenName);
                Undo.RegisterCreatedObjectUndo(go, "Перекрёсток");
                go.transform.SetParent(transform, false);
                t = go.transform;
            }
            return t;
        }

        public void Clear()
        {
            Transform t = GenRoot(false);
            if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
        }

        public void Build()
        {
            CityBlockGenerator g = Gen();
            if (g == null) { Debug.LogWarning("[Город/кран] нет генератора застройки (City) в сцене", this); return; }
            Undo.RecordObject(transform, "Перекрёсток");
            Undo.RecordObject(this, "Перекрёсток");
            List<string> before = new List<string>();                          // модули вокруг прошлого места (если объект передвинули — им вернуть круг)
            for (int q = 0; q < 4; q++) if (Ring1(q)) before.Add(Name(q));
            Snap();
            Clear();
            Transform root = GenRoot(true);
            float M = Meter();
            float hx, hz;
            HalfRoad(out hx, out hz);

            PropEntry cone, sign;
            FindProps(g, out cone, out sign);
            Material toon = ToonMat();
            Mesh coneMesh = cone == null ? ProcMesh("CC_Cone", 0) : null;
            Mesh barMesh = Barriers ? ProcMesh("CC_Barrier", 1) : null;
            Mesh warnMesh = sign == null && WarningSigns ? ProcMesh("CC_WarnSign", 2) : null;

            int nCones = 0, nSigns = 0;
            for (int a = 0; a < 4; a++)
            {
                Vector2 d = ArmDir(a);
                Vector2 side = new Vector2(-d.y, d.x);                         // поперёк дороги
                float edge = (a % 2 == 0) ? hx : hz;                          // край перекрёстка по направлению руки
                float half = (a % 2 == 0) ? hz : hx;                          // полуширина этой дороги
                float line = edge + Gap * half;
                // ряд конусов поперёк
                float w = half * 2f * 0.9f;
                int n = Mathf.Max(4, Mathf.RoundToInt(w / (ConeStep * M)) + 1);
                for (int i = 0; i < n; i++)
                {
                    float t = n > 1 ? (i / (float)(n - 1) - 0.5f) * w : 0f;
                    Vector2 p = d * line + side * t;
                    if (PlaceCone(root, cone, coneMesh, toon, p, M, i)) nCones++;
                }
                // воронка: вдоль бордюров по 3 конуса — дальше от перекрёстка
                if (Funnel)
                    for (int s = -1; s <= 1; s += 2)
                        for (int i = 1; i <= 3; i++)
                        {
                            Vector2 p = d * (line + i * ConeStep * M * 1.6f) + side * (s * (half * 0.9f - i * half * 0.06f));
                            if (PlaceCone(root, cone, coneMesh, toon, p, M, i + 7 * a)) nCones++;
                        }
                // барьер в полоску — по центру въезда, лицом к машинам
                float yawOut = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                if (barMesh != null)
                {
                    float bw = 2.6f;                                           // ширина барьера, м
                    int nb = Mathf.Clamp(Mathf.RoundToInt(half * 1.1f / (bw * M)), 1, 4);
                    for (int i = 0; i < nb; i++)
                    {
                        float t = nb > 1 ? (i / (float)(nb - 1) - 0.5f) * half * 0.9f : 0f;
                        Vector2 p = d * (line + 1.2f * M) + side * t;
                        Proc(root, "Barrier", barMesh, toon, p, yawOut, M);
                        nSigns++;
                    }
                }
                // знак — справа по ходу въезжающей машины (у бордюра)
                Vector2 sp = d * (line + 3.5f * M) + side * (half * 0.75f);
                if (sign != null) { Prop(root, sign, sp, CityProps.YawTo(sign, d), M); nSigns++; }
                else if (warnMesh != null) { Proc(root, "WarnSign", warnMesh, toon, sp, yawOut, M); nSigns++; }
            }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;

            // машины: перерасставить пропсы в модулях вокруг (круги квартала создадутся заново, разворот применится сам)
            ClearOvals(null);
            CityProps cp = g.GetComponent<CityProps>();
            if (cp != null)
            {
                List<string> mods = new List<string>(before);
                for (int q = 0; q < 4; q++) if (Ring1(q) && !mods.Contains(Name(q))) mods.Add(Name(q));
                foreach (string mn in mods)
                {
                    int mx, mz;
                    if (ParseModule(mn, out mx, out mz)) cp.GenerateModule(mx, mz);
                }
            }
            int moved = 0;
            foreach (Transform t in transform)
            {
                CityTraffic tr = t.GetComponent<CityTraffic>();
                if (tr != null && t.name.StartsWith(OvalPrefix)) moved += tr.Cars.Count;
            }
            if (Log) Debug.Log("[Город/кран] перекрёсток закрыт: конусов " + nCones + ", барьеров и знаков " + nSigns +
                               (cone == null ? " (конусы — мультяшные, своих в «Пропсы и машины» нет)" : "") +
                               ", машин на развороте " + moved + ". Площадка под кран — жёлтый квадрат " +
                               (hx * 2f / M).ToString("0") + "×" + (hz * 2f / M).ToString("0") + " м.", this);
            EditorUtility.SetDirty(this);
        }

        private static bool ParseModule(string n, out int x, out int z)
        {
            x = 0; z = 0;
            if (string.IsNullOrEmpty(n)) return false;
            string[] p = n.Split('_');
            return p.Length == 3 && int.TryParse(p[1], out x) && int.TryParse(p[2], out z);
        }

        // ── пропсы ──
        private void FindProps(CityBlockGenerator g, out PropEntry cone, out PropEntry sign)
        {
            cone = null; sign = null;
            if (ConePrefab != null) cone = new PropEntry { Prefab = ConePrefab, Kind = PropKind.Cone, Size = 0.75f };
            if (SignPrefab != null) sign = new PropEntry { Prefab = SignPrefab, Kind = PropKind.StreetSign, Size = 2.6f };
            CityProps cp = g.GetComponent<CityProps>();
            if (cp == null) return;
            int bestS = -1;
            string[] words = { "work", "closed", "close", "stop", "warning", "attention", "danger", "detour", "road", "block", "repair" };
            foreach (PropEntry e in cp.Props)
            {
                if (e == null || e.Prefab == null) continue;
                if (cone == null && e.Kind == PropKind.Cone) cone = e;
                if (SignPrefab == null && e.Kind == PropKind.StreetSign)
                {
                    string n = e.Prefab.name.ToLowerInvariant();
                    int sc = 0;
                    foreach (string w in words) if (n.Contains(w)) sc += 2;
                    if (sc > bestS) { bestS = sc; sign = e; }
                }
            }
            if (bestS == 0 && SignPrefab == null) sign = null;                // обычный дорожный знак без слов — лучше свой «внимание»
        }

        private static float Scale(PropEntry e, float m)
        {
            Bounds b;
            if (e.Size <= 0f || !TileGridGenerator.MeasurePrefab(e.Prefab, out b)) return 1f;
            float have = e.Axis == PropAxis.Height ? b.size.y : Mathf.Max(b.size.x, b.size.z);
            return have > 1e-4f ? e.Size * m / have : 1f;
        }

        private static GameObject Prop(Transform root, PropEntry e, Vector2 p, float yaw, float m)
        {
            Bounds b;
            if (!TileGridGenerator.MeasurePrefab(e.Prefab, out b)) return null;
            GameObject pivot = new GameObject("P_" + e.Prefab.name);
            pivot.transform.SetParent(root, false);
            pivot.transform.localPosition = new Vector3(p.x, 0f, p.y);
            pivot.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            pivot.transform.localScale = Vector3.one * Scale(e, m);
            GameObject go = CityBlockGenerator.Spawn(e.Prefab, pivot.transform);
            go.transform.localRotation = e.Prefab.transform.localRotation;
            go.transform.localScale = e.Prefab.transform.localScale;
            go.transform.localPosition = -new Vector3(b.center.x, b.min.y, b.center.z);
            return pivot;
        }

        private static bool PlaceCone(Transform root, PropEntry cone, Mesh mesh, Material toon, Vector2 p, float m, int i)
        {
            float yaw = (i * 47.3f) % 360f;
            if (cone != null) return Prop(root, cone, p, yaw, m) != null;
            if (mesh == null || toon == null) return false;
            Proc(root, "Cone", mesh, toon, p, yaw, m);
            return true;
        }

        private static void Proc(Transform root, string name, Mesh mesh, Material mat, Vector2 p, float yaw, float m)
        {
            GameObject go = CityToonMesh.Make(name, root, mesh, mat, true);
            go.transform.localPosition = new Vector3(p.x, 0f, p.y);
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * m;
        }

        // ── свои мультяшные: конус, барьер, знак «внимание» (меши в метрах, цвет в вершинах, шейдер Toon) ──
        private const string Folder = "Assets/Generated/City/Closure";

        private static void MakeFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Generated")) AssetDatabase.CreateFolder("Assets", "Generated");
            if (!AssetDatabase.IsValidFolder("Assets/Generated/City")) AssetDatabase.CreateFolder("Assets/Generated", "City");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Generated/City", "Closure");
        }

        private static Material ToonMat()
        {
            MakeFolder();
            string path = Folder + "/CC_Closure_Toon.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            Shader sh = Shader.Find("CatCooks/City/Toon");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (sh == null) return null;
            m = new Material(sh);
            m.enableInstancing = true;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static Mesh ProcMesh(string name, int kind)
        {
            MakeFolder();
            string path = Folder + "/" + name + ".asset";
            Mesh old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) return old;
            CityToonMesh b = new CityToonMesh();
            Color orange = new Color(1f, 0.45f, 0.08f), white = new Color(0.96f, 0.96f, 0.92f), red = new Color(0.86f, 0.12f, 0.1f),
                  dark = new Color(0.18f, 0.18f, 0.2f), yellow = new Color(1f, 0.82f, 0.15f), black = new Color(0.08f, 0.08f, 0.08f);
            if (kind == 0)
            {
                // конус 0.75 м: квадратная подошва + ствол с белой полосой
                b.Box(new Vector3(0f, 0.02f, 0f), new Vector3(0.42f, 0.04f, 0.42f), orange);
                float[] hs = { 0.04f, 0.36f, 0.5f, 0.75f };
                Color[] cs = { orange, white, orange };
                for (int k = 0; k < 3; k++)
                {
                    float y0 = hs[k], y1 = hs[k + 1];
                    float r0 = Mathf.Lerp(0.17f, 0.025f, (y0 - 0.04f) / 0.71f), r1 = Mathf.Lerp(0.17f, 0.025f, (y1 - 0.04f) / 0.71f);
                    for (int i = 0; i < 10; i++)
                    {
                        float a0 = i * Mathf.PI * 0.2f, a1 = (i + 1) * Mathf.PI * 0.2f;
                        Vector3 p00 = new Vector3(Mathf.Cos(a0) * r0, y0, Mathf.Sin(a0) * r0), p01 = new Vector3(Mathf.Cos(a1) * r0, y0, Mathf.Sin(a1) * r0);
                        Vector3 p10 = new Vector3(Mathf.Cos(a0) * r1, y1, Mathf.Sin(a0) * r1), p11 = new Vector3(Mathf.Cos(a1) * r1, y1, Mathf.Sin(a1) * r1);
                        b.Quad(p00, p10, p11, p01, cs[k]);
                    }
                }
                b.Disc(new Vector3(0f, 0.75f, 0f), Vector3.up, 0.025f, 10, orange);
            }
            else if (kind == 1)
            {
                // барьер 2.6 м: две ноги-«домики», доска в красно-белую полоску, жёлтый фонарик
                for (int s = -1; s <= 1; s += 2)
                {
                    float x = s * 1.15f;
                    b.Beam(new Vector3(x, 0f, -0.3f), new Vector3(x, 1.05f, 0f), 0.06f, dark);
                    b.Beam(new Vector3(x, 0f, 0.3f), new Vector3(x, 1.05f, 0f), 0.06f, dark);
                    b.Box(new Vector3(x, 0.03f, 0f), new Vector3(0.12f, 0.06f, 0.7f), dark);
                }
                int seg = 8;
                for (int i = 0; i < seg; i++)
                {
                    float x0 = -1.3f + i * 2.6f / seg;
                    b.Box(new Vector3(x0 + 1.3f / seg, 0.95f, 0f), new Vector3(2.6f / seg, 0.3f, 0.05f), (i % 2 == 0) ? red : white);
                    b.Box(new Vector3(x0 + 1.3f / seg, 0.5f, 0f), new Vector3(2.6f / seg, 0.16f, 0.04f), (i % 2 == 0) ? white : red);
                }
                b.Box(new Vector3(-1.15f, 1.17f, 0f), new Vector3(0.14f, 0.14f, 0.1f), yellow);
                b.Box(new Vector3(1.15f, 1.17f, 0f), new Vector3(0.14f, 0.14f, 0.1f), yellow);
            }
            else
            {
                // знак «внимание»: столб 2.2 м + треугольник (красная кайма, белое поле, чёрный «!»), с двух сторон
                b.Cylinder(new Vector3(0f, 0f, -0.05f), 0.04f, 2.3f, 8, new Color(0.6f, 0.62f, 0.65f));
                float cy = 1.9f, R = 0.48f, r = 0.34f;
                Vector3 c = new Vector3(0f, cy, 0f);                              // лицо знака — к +Z (к въезжающим машинам)
                Vector3 t0 = c + new Vector3(0f, R, 0f), t1 = c + new Vector3(R * 0.866f, -R * 0.5f, 0f), t2 = c + new Vector3(-R * 0.866f, -R * 0.5f, 0f);
                Vector3 i0 = c + new Vector3(0f, r, 0.012f), i1 = c + new Vector3(r * 0.866f, -r * 0.5f, 0.012f), i2 = c + new Vector3(-r * 0.866f, -r * 0.5f, 0.012f);
                for (int f = 0; f < 2; f++)
                {
                    b.Tri(t0, t1, t2, red); b.Tri(t0, t2, t1, red);
                    b.Tri(i0, i2, i1, white); b.Tri(i0, i1, i2, white);
                }
                b.Box(c + new Vector3(0f, 0.03f, 0.022f), new Vector3(0.05f, 0.17f, 0.008f), black);
                b.Box(c + new Vector3(0f, -0.11f, 0.022f), new Vector3(0.05f, 0.05f, 0.008f), black);
            }
            Mesh m = b.Build(name);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ═════════════════════════ машины: разворот ═════════════════════════

        private void ClearOvals(string module)
        {
            List<GameObject> kill = new List<GameObject>();
            foreach (Transform t in transform)
                if (t.name.StartsWith(OvalPrefix) && (module == null || t.name.EndsWith("@" + module))) kill.Add(t.gameObject);
            foreach (GameObject go in kill) Undo.DestroyObjectImmediate(go);
        }

        /// <summary>Круги квартала у модулей вокруг → овалы-развороты вдоль улиц. Возвращает число машин на разворотах.</summary>
        public int ApplyTraffic()
        {
            CityBlockGenerator g = Gen();
            if (g == null || g.Container() == null || !UTurn) return 0;
            float hx, hz;
            HalfRoad(out hx, out hz);
            Vector2 ms = g.ModuleSize();
            int total = 0;
            for (int q = 0; q < 4; q++)
            {
                string mn = Name(q);
                if (string.IsNullOrEmpty(mn)) continue;
                Transform mod = g.Container().Find(mn);
                if (mod == null) continue;
                Transform loopT = mod.Find(CityProps.TrafficName);
                if (loopT == null) continue;
                CityTraffic loop = loopT.GetComponent<CityTraffic>();
                if (loop == null) continue;
                ClearOvals(mn);                                                // старые овалы этого модуля — заново
                // свои руки: у квадранта q — рука по X (знак QSX) и по Z (знак QSZ)
                List<int> arms = new List<int>();
                int ax = QSX[q] > 0 ? 0 : 2, az = QSZ[q] > 0 ? 1 : 3;
                if (_armOwner[ax] == mn) arms.Add(ax);
                if (_armOwner[az] == mn) arms.Add(az);
                List<CityTraffic> ovals = new List<CityTraffic>();
                foreach (int a in arms)
                {
                    bool alongX = a % 2 == 0;
                    float sgn = (a == 0 || a == 1) ? 1f : -1f;
                    float halfRoad = alongX ? hz : hx;                          // полуширина этой улицы
                    float edge = alongX ? hx : hz;                              // край перекрёстка
                    float len = alongX ? ms.x : ms.y;                           // до следующего перекрёстка
                    float lane = Mathf.Clamp(halfRoad - loop.Lane, halfRoad * 0.2f, halfRoad * 0.8f);   // от оси дороги до полосы
                    float near = edge + (Gap * halfRoad) + 1.6f * Meter() + lane + 0.5f * halfRoad;    // разворот — не задевая барьер
                    float far = len - lane;
                    if (far - near < lane * 2f) continue;
                    GameObject go = new GameObject(OvalPrefix + (alongX ? (sgn > 0 ? "+X" : "-X") : (sgn > 0 ? "+Z" : "-Z")) + " @" + mn);
                    Undo.RegisterCreatedObjectUndo(go, "Разворот");
                    go.transform.SetParent(transform, false);
                    CityTraffic tr = go.AddComponent<CityTraffic>();
                    float a0 = sgn * near, a1 = sgn * far;
                    if (alongX) { tr.X0 = Mathf.Min(a0, a1); tr.X1 = Mathf.Max(a0, a1); tr.Z0 = 0f; tr.Z1 = 0f; }
                    else { tr.Z0 = Mathf.Min(a0, a1); tr.Z1 = Mathf.Max(a0, a1); tr.X0 = 0f; tr.X1 = 0f; }
                    tr.Lane = lane;
                    tr.Speed = loop.Speed;
                    tr.Reverse = true;                                          // правостороннее: к перекрёстку — по своей половине
                    ovals.Add(tr);
                }
                // машины круга — по овалам (по очереди), круг убрать
                if (ovals.Count > 0)
                {
                    for (int i = 0; i < loop.Cars.Count; i++)
                    {
                        Transform car = loop.Cars[i];
                        if (car == null) continue;
                        CityTraffic tr = ovals[i % ovals.Count];
                        Undo.SetTransformParent(car, tr.transform, "Разворот");
                        tr.Cars.Add(car);
                        tr.YawOffset.Add(i < loop.YawOffset.Count ? loop.YawOffset[i] : 0f);
                        total++;
                    }
                    foreach (CityTraffic tr in ovals)
                    {
                        float per = tr.Perimeter();
                        for (int i = 0; i < tr.Cars.Count; i++) tr.Dist.Add((i + 0.3f * ((i * 7) % 3) / 3f) * per / Mathf.Max(1, tr.Cars.Count));
                        tr.Apply();
                    }
                }
                Undo.DestroyObjectImmediate(loopT.gameObject);                  // круг через перекрёсток — нельзя
            }
            return total;
        }

        /// <summary>Пропсы перерасставлены (весь город или модуль) — применить разворот заново.</summary>
        public static void AfterProps(CityBlockGenerator g)
        {
            if (Application.isPlaying) return;
            foreach (CityRoadClosure c in FindObjectsByType<CityRoadClosure>(FindObjectsSortMode.None))
                if (c.isActiveAndEnabled && c.UTurn && (c.City == null || c.City == g)) c.ApplyTraffic();
        }

        /// <summary>Пропсы убраны — убрать и овалы.</summary>
        public static void PropsCleared(CityBlockGenerator g)
        {
            if (Application.isPlaying) return;
            foreach (CityRoadClosure c in FindObjectsByType<CityRoadClosure>(FindObjectsSortMode.None))
                if (c.City == null || c.City == g) c.ClearOvals(null);
        }
#endif
    }
}
