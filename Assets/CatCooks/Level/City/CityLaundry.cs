using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CatCooks.Level
{
    /// <summary>
    /// БЕЛЬЁ НА ВЕРЁВКАХ над дорогой между домами соседних модулей: верёвка с провисом от фасада до фасада через улицу,
    /// на ней футболки, штаны, носки, полотенца в полоску, платья — мультяшные плоские кусочки, один меш на верёвку.
    /// Колышется в шейдере (Sway Toon, сильнее в ветреную погоду), ничего не считается на процессоре.
    /// Кнопка «Построить» — ищет пары домов напротив друг друга через дорогу (средние по высоте) и натягивает верёвки.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CatCooks/Город/Бельё на верёвках")]
    public sealed class CityLaundry : MonoBehaviour
    {
        public const string GenName = "Laundry (generated)";

        public CityBlockGenerator City;
        [Tooltip("Материал (шейдер Sway Toon). Ставит меню")]
        public Material LaundryMaterial;

        [Header("Где")]
        [Tooltip("Строить вокруг этого объекта: дороги в этом радиусе, м (ближние — первыми). Поставь объект в центр уровня")]
        public float Radius = 160f;
        [Tooltip("Между модулями ближнего слоя (у уровня)")]
        public bool Ring1 = true;
        [Tooltip("Между модулями среднего слоя")]
        public bool Ring2 = true;
        [Tooltip("Высота домов, м: от и до (только средние дома)")]
        public Vector2 HouseHeight = new Vector2(9f, 45f);
        [Tooltip("Дом должен стоять у дороги: фасад не дальше этого от края тротуара, м")]
        public float FrontGap = 7f;

        [Header("Сколько")]
        [Tooltip("Шанс, что над дорогой между двумя модулями будет бельё")]
        [Range(0f, 1f)] public float RoadChance = 0.7f;
        [Tooltip("Верёвок над одной дорогой (максимум)")]
        [Range(1, 6)] public int PerRoad = 3;
        [Tooltip("Всего верёвок (максимум)")]
        [Range(1, 200)] public int MaxLines = 40;
        [Tooltip("Насколько густо висит бельё (0 — пусто, 1 — плотно)")]
        [Range(0.1f, 1f)] public float Density = 0.7f;
        [Tooltip("Размер вещей (1 — как в жизни, мультяшно крупнее — лучше видно)")]
        [Range(0.5f, 3f)] public float ClothScale = 1.6f;
        public int Seed = 3;

        [Header("Оптимизация")]
        [Tooltip("Верёвки склеиваются в куски такого размера, м (один вызов отрисовки на кусок)")]
        public float ChunkMeters = 70f;
        [Tooltip("Кусок пропадает, когда он меньше этой доли высоты экрана (вдали бельё не видно)")]
        [Range(0f, 0.1f)] public float CullScreen = 0.012f;

        public bool Log = true;

        private void OnDrawGizmosSelected()
        {
            float M = 3.5f;
            CityProps p = City != null ? City.GetComponent<CityProps>() : null;
            if (p != null) M = p.Meter();
            Gizmos.color = new Color(1f, 0.6f, 0.8f, 0.9f);
            Vector3 c = transform.position;
            float R = Radius * M;
            for (int i = 0; i < 48; i++)
            {
                float a0 = i * Mathf.PI * 2f / 48f, a1 = (i + 1) * Mathf.PI * 2f / 48f;
                Gizmos.DrawLine(c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * R, c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * R);
            }
        }

#if UNITY_EDITOR
        private sealed class House { public Bounds L; public float Top, Bottom; }

        private sealed class Chunk
        {
            public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<Vector4> A = new List<Vector4>();
            public readonly List<Vector2> X = new List<Vector2>();
            public readonly List<int> T = new List<int>();
            public int Lines;
        }

        private Dictionary<long, Chunk> _chunks;

        private System.Random _r;
        private float Rn(float a, float b) { return a + (b - a) * (float)_r.NextDouble(); }

        public CityBlockGenerator Gen()
        {
            if (City == null) City = FindFirstObjectByType<CityBlockGenerator>();
            return City;
        }

        public void Clear()
        {
            Transform t = transform.Find(GenName);
            if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
        }

        private bool RoleOk(CityBlockGenerator g, int x, int z)
        {
            CityRole r = g.GetRole(x, z);
            return (r == CityRole.Ring1 && Ring1) || (r == CityRole.Ring2 && Ring2);
        }

        /// <summary>Дома модуля («B_…») — габариты в осях модуля a (центр, поворот).</summary>
        private List<House> Houses(CityBlockGenerator g, int x, int z, Vector3 ac, Quaternion ar, float M)
        {
            List<House> list = new List<House>();
            Transform cont = g.Container();
            Transform mod = cont != null ? cont.Find(CityBlockGenerator.ModuleName(x, z)) : null;
            if (mod == null) return list;
            Quaternion inv = Quaternion.Inverse(ar);
            foreach (Transform h in mod)
            {
                if (!h.name.StartsWith("B_")) continue;
                Renderer[] rs = h.GetComponentsInChildren<Renderer>(false);
                if (rs.Length == 0) continue;
                Bounds wb = rs[0].bounds;
                foreach (Renderer r in rs) wb.Encapsulate(r.bounds);
                float hh = wb.size.y / M;
                if (hh < HouseHeight.x || hh > HouseHeight.y) continue;
                Bounds lb = new Bounds();
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 l = inv * (c - ac);
                    if (i == 0) lb = new Bounds(l, Vector3.zero); else lb.Encapsulate(l);
                }
                list.Add(new House { L = lb, Top = wb.max.y, Bottom = wb.min.y });
            }
            return list;
        }

        public void Build()
        {
            CityBlockGenerator g = Gen();
            if (g == null) { Debug.LogWarning("[Город/бельё] нет генератора застройки (City) в сцене", this); return; }
            Clear();
            _r = new System.Random(Seed);
            float M = CityPropGlow.CityMeter();
            Vector2 ms = g.ModuleSize();
            GameObject root = new GameObject(GenName);
            Undo.RegisterCreatedObjectUndo(root, "Бельё");
            root.transform.SetParent(transform, false);
            // все дороги между соседними модулями — по расстоянию от этого объекта (ближние первыми), в радиусе
            List<Vector4> list = new List<Vector4>();                         // x, z, dir, расстояние
            Vector3 me = transform.position;
            float R = Radius * M;
            for (int z = 0; z < g.Depth; z++)
                for (int x = 0; x < g.Width; x++)
                    for (int dir = 0; dir < 2; dir++)
                    {
                        int nx = x + (dir == 0 ? 1 : 0), nz = z + (dir == 1 ? 1 : 0);
                        if (nx >= g.Width || nz >= g.Depth) continue;
                        if (!RoleOk(g, x, z) && !RoleOk(g, nx, nz)) continue;           // хотя бы с одной стороны — слой с домами
                        if (g.GetRole(x, z) == CityRole.Empty || g.GetRole(nx, nz) == CityRole.Empty) continue;
                        Vector3 c; Quaternion q;
                        g.ModuleFrame(x, z, out c, out q);
                        Vector3 mid = c + q * (dir == 0 ? new Vector3(ms.x * 0.5f, 0f, 0f) : new Vector3(0f, 0f, ms.y * 0.5f));
                        Vector3 d = mid - me; d.y = 0f;
                        float dist = d.magnitude;
                        if (dist > R) continue;
                        list.Add(new Vector4(x, z, dir, dist));
                    }
            list.Sort((a, b) => a.w.CompareTo(b.w));
            int lines = 0, roads = 0;
            _chunks = new Dictionary<long, Chunk>();
            foreach (Vector4 rd in list)
            {
                if (lines >= MaxLines) break;
                if (_r.NextDouble() > RoadChance) continue;
                int x = (int)rd.x, z = (int)rd.y, dir = (int)rd.z;
                int nx = x + (dir == 0 ? 1 : 0), nz = z + (dir == 1 ? 1 : 0);
                int made = Road(g, x, z, nx, nz, dir, ms, M, root.transform, MaxLines - lines);
                if (made > 0) { lines += made; roads++; }
            }
            int nChunks = FlushChunks(root.transform);
            if (Log) Debug.Log("[Город/бельё] склеено в кусков: " + nChunks + " (вызовов отрисовки столько же), вдали куски пропадают", this);
            if (Log) Debug.Log("[Город/бельё] вокруг «" + name + "» (радиус " + Radius.ToString("0") + " м): дорог рядом " + list.Count + ", верёвок с бельём " + lines + " над " + roads + " дорогами" +
                               (list.Count == 0 ? ". Дорог в радиусе нет — передвинь объект к домам или увеличь Radius" : ""), this);
        }

        /// <summary>Бельё над дорогой между модулем (x,z) и соседом (dir 0 — по X, 1 — по Z).</summary>
        private int Road(CityBlockGenerator g, int x, int z, int nx, int nz, int dir, Vector2 ms, float M, Transform root, int left)
        {
            Vector3 ac; Quaternion ar;
            g.ModuleFrame(x, z, out ac, out ar);
            List<House> ha = Houses(g, x, z, ac, ar, M), hb = Houses(g, nx, nz, ac, ar, M);
            float S = dir == 0 ? ms.x : ms.y;
            float half = S * (0.5f - g.RoadMargin);                // край участка модуля a (в его осях)
            float far = S * 0.5f + S * g.RoadMargin;               // край участка соседа
            float gap = FrontGap * M;
            // кандидаты: пары домов напротив
            List<Vector3[]> cand = new List<Vector3[]>();
            foreach (House a in ha)
            {
                float aFront = dir == 0 ? a.L.max.x : a.L.max.z;
                if (aFront < half - gap) continue;
                foreach (House b in hb)
                {
                    float bFront = dir == 0 ? b.L.min.x : b.L.min.z;
                    if (bFront > far + gap) continue;
                    float o0 = dir == 0 ? Mathf.Max(a.L.min.z, b.L.min.z) : Mathf.Max(a.L.min.x, b.L.min.x);
                    float o1 = dir == 0 ? Mathf.Min(a.L.max.z, b.L.max.z) : Mathf.Min(a.L.max.x, b.L.max.x);
                    o0 += 1.5f * M; o1 -= 1.5f * M;
                    if (o1 - o0 < 2f * M) continue;
                    float y0 = Mathf.Max(a.Bottom, b.Bottom) + 4.5f * M, y1 = Mathf.Min(a.Top, b.Top) - 2f * M;
                    if (y1 <= y0) continue;
                    cand.Add(new Vector3[] { new Vector3(aFront, bFront, 0f), new Vector3(o0, o1, 0f), new Vector3(y0, y1, 0f) });
                }
            }
            if (cand.Count == 0) return 0;
            int want = Mathf.Min(left, 1 + _r.Next(PerRoad));
            List<Vector2> used = new List<Vector2>();
            int made = 0;
            for (int tries = 0; tries < want * 4 && made < want; tries++)
            {
                Vector3[] c = cand[_r.Next(cand.Count)];
                float along = Rn(c[1].x, c[1].y), y = Mathf.Lerp(c[2].x, c[2].y, Rn(0.1f, 0.75f));
                bool clash = false;
                foreach (Vector2 u in used) if (Mathf.Abs(u.x - along) < 3f * M && Mathf.Abs(u.y - y) < 4f * M) { clash = true; break; }
                if (clash) continue;
                used.Add(new Vector2(along, y));
                Vector3 pa = dir == 0 ? new Vector3(c[0].x, 0f, along) : new Vector3(along, 0f, c[0].x);
                Vector3 pb = dir == 0 ? new Vector3(c[0].y, 0f, along) : new Vector3(along, 0f, c[0].y);
                Vector3 wa = ac + ar * pa, wb = ac + ar * pb;
                wa.y = y; wb.y = y + Rn(-0.6f, 0.6f) * M;
                Line(root, wa, wb, M);
                made++;
            }
            return made;
        }

        private void Line(Transform root, Vector3 a, Vector3 b, float M)
        {
            Vector3 d = b - a; Vector3 flat = new Vector3(d.x, 0f, d.z);
            float L = flat.magnitude;
            if (L < 1f) return;
            Vector3 mid = (a + b) * 0.5f;
            Quaternion rot = Quaternion.LookRotation(Vector3.Cross(flat.normalized, Vector3.up));   // локальный X — вдоль верёвки
            float dy = d.y;
            CityToonMesh t = new CityToonMesh();
            float sag = L * Rn(0.05f, 0.09f);
            // верёвка: две ленты (плашмя и на ребро)
            const int N = 14;
            Color rope = new Color(0.92f, 0.9f, 0.84f, 0f);
            float w = 0.035f * M;
            for (int i = 0; i < N; i++)
            {
                float x0 = -L * 0.5f + L * i / N, x1 = -L * 0.5f + L * (i + 1) / N;
                Vector3 p0 = RopeAt(x0, L, sag, dy), p1 = RopeAt(x1, L, sag, dy);
                Color c0 = Weighted(rope, RopeW(x0, L)), c1 = Weighted(rope, RopeW(x1, L));
                AddQuad(t, p0 + Vector3.forward * w, p1 + Vector3.forward * w, p1 - Vector3.forward * w, p0 - Vector3.forward * w, c0, c1, c1, c0);
                AddQuad(t, p0 + Vector3.up * w, p1 + Vector3.up * w, p1 - Vector3.up * w, p0 - Vector3.up * w, c0, c1, c1, c0);
            }
            // вещи
            float cs = ClothScale * M;
            float x = -L * 0.5f + 0.8f * M;
            while (x < L * 0.5f - 0.8f * M)
            {
                float wItem = Cloth(t, x, L, sag, dy, cs);
                x += wItem + Rn(0.15f, 1.2f) * cs / Mathf.Max(0.2f, Density);
            }
            // в кусок по клетке: вершины — в осях корня, ось качания и фаза — свои у каждой верёвки (uv1), вдоль верёвки — uv2
            float cell = Mathf.Max(5f, ChunkMeters * M);
            long key = ((long)Mathf.FloorToInt(mid.x / cell) * 73856093L) ^ ((long)Mathf.FloorToInt(mid.z / cell) * 19349663L);
            Chunk ch;
            if (!_chunks.TryGetValue(key, out ch)) { ch = new Chunk(); _chunks[key] = ch; }
            Vector3 axis = root.InverseTransformDirection(rot * Vector3.forward).normalized;
            float phase = Rn(0f, 6.2832f);
            int s0 = ch.V.Count;
            for (int i = 0; i < t.V.Count; i++)
            {
                ch.V.Add(root.InverseTransformPoint(mid + rot * t.V[i]));
                ch.N.Add(root.InverseTransformDirection(rot * t.N[i]));
                ch.C.Add(t.C[i]);
                ch.A.Add(new Vector4(axis.x, axis.y, axis.z, phase));
                ch.X.Add(new Vector2(t.V[i].x, 0f));
            }
            for (int i = 0; i < t.T.Count; i++) ch.T.Add(t.T[i] + s0);
            ch.Lines++;
        }

        /// <summary>Создать объекты кусков: меш, материал, LOD Group (пропадает вдали).</summary>
        private int FlushChunks(Transform root)
        {
            int n = 0;
            float M = CityPropGlow.CityMeter();
            foreach (Chunk ch in _chunks.Values)
            {
                if (ch.V.Count == 0) continue;
                Mesh m = new Mesh { name = "LaundryChunk" };
                if (ch.V.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(ch.V); m.SetNormals(ch.N); m.SetColors(ch.C); m.SetUVs(1, ch.A); m.SetUVs(2, ch.X);
                m.SetTriangles(ch.T, 0);
                m.RecalculateBounds();
                Bounds bb = m.bounds; bb.Expand(1.5f * M); m.bounds = bb;            // качание в шейдере
                GameObject go = new GameObject("LaundryChunk (" + ch.Lines + ")");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                MeshRenderer r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = LaundryMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (CullScreen > 0f)
                {
                    LODGroup lg = go.AddComponent<LODGroup>();
                    lg.SetLODs(new LOD[] { new LOD(CullScreen, new Renderer[] { r }) });
                    lg.RecalculateBounds();
                }
                n++;
            }
            _chunks = null;
            return n;
        }

        private static Vector3 RopeAt(float x, float L, float sag, float dy)
        {
            float u = 2f * x / L;
            return new Vector3(x, -sag * (1f - u * u) + dy * (x / L), 0f);
        }

        private static float RopeW(float x, float L) { float u = 2f * x / L; return 0.25f * (1f - u * u); }

        private static Color Weighted(Color c, float a) { return new Color(c.r, c.g, c.b, Mathf.Clamp01(a)); }

        private static void AddQuad(CityToonMesh t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, Color cc, Color cd)
        {
            int s = t.V.Count;
            t.Quad(a, b, c, d, ca);
            t.C[s] = ca; t.C[s + 1] = cb; t.C[s + 2] = cc; t.C[s + 3] = cd;
        }

        private static readonly Color[] Pal =
        {
            new Color(0.95f, 0.33f, 0.3f), new Color(0.35f, 0.65f, 0.95f), new Color(1f, 0.85f, 0.3f), new Color(0.97f, 0.97f, 0.95f),
            new Color(0.98f, 0.6f, 0.75f), new Color(0.5f, 0.88f, 0.7f), new Color(0.25f, 0.32f, 0.6f), new Color(1f, 0.6f, 0.25f),
            new Color(0.7f, 0.55f, 0.9f)
        };

        /// <summary>Прямоугольник вещи: верх по линии top (у верёвки), вниз на h; вес движения растёт к низу.</summary>
        private void Rect(CityToonMesh t, float x0, float x1, float yTop0, float yTop1, float h0, float h1, float baseW, float hTotal, float hFrom, Color col)
        {
            float wTop0 = baseW + (hFrom / hTotal) * 0.9f, wTop1 = wTop0;
            float wBot = baseW + ((hFrom + Mathf.Max(h0, h1)) / hTotal) * 0.9f;
            Vector3 a = new Vector3(x0, yTop0 - hFrom, 0f), b = new Vector3(x1, yTop1 - hFrom, 0f);
            Vector3 c = new Vector3(x1, yTop1 - hFrom - h1, 0f), d = new Vector3(x0, yTop0 - hFrom - h0, 0f);
            AddQuad(t, d, a, b, c, Weighted(col, wBot), Weighted(col, wTop0), Weighted(col, wTop1), Weighted(col, wBot));
        }

        /// <summary>Одна вещь на верёвке начиная с x. Возвращает её ширину.</summary>
        private float Cloth(CityToonMesh t, float x, float L, float sag, float dy, float cs)
        {
            int kind = _r.Next(7);
            Color c1 = Pal[_r.Next(Pal.Length)], c2 = Pal[_r.Next(Pal.Length)];
            float wdt, hgt;
            switch (kind)
            {
                case 0: wdt = 0.55f; hgt = 0.6f; break;     // футболка
                case 1: wdt = 0.42f; hgt = 0.85f; break;    // штаны
                case 2: wdt = 0.12f; hgt = 0.3f; break;     // носок
                case 3: wdt = Rn(0.5f, 0.9f); hgt = Rn(0.7f, 1.2f); break;  // полотенце / простыня
                case 4: wdt = 0.5f; hgt = 0.9f; break;      // платье
                case 5: wdt = 0.3f; hgt = 0.25f; break;     // трусы
                default: wdt = 0.6f; hgt = 0.45f; break;    // наволочка
            }
            wdt *= cs; hgt *= cs;
            float x0 = x, x1 = x + wdt;
            float y0 = RopeAt(x0, L, sag, dy).y, y1 = RopeAt(x1, L, sag, dy).y;
            float bw = RopeW((x0 + x1) * 0.5f, L);
            switch (kind)
            {
                case 0:
                    Rect(t, x0 + wdt * 0.2f, x1 - wdt * 0.2f, y0, y1, hgt, hgt, bw, hgt, 0f, c1);
                    Rect(t, x0, x0 + wdt * 0.2f, y0, y0, hgt * 0.35f, hgt * 0.25f, bw, hgt, 0f, c1);         // рукава
                    Rect(t, x1 - wdt * 0.2f, x1, y1, y1, hgt * 0.25f, hgt * 0.35f, bw, hgt, 0f, c1);
                    if (_r.NextDouble() < 0.5) Rect(t, x0 + wdt * 0.2f, x1 - wdt * 0.2f, y0, y1, hgt * 0.15f, hgt * 0.15f, bw, hgt, hgt * 0.4f, c2);   // полоска
                    break;
                case 1:
                    Rect(t, x0, x1, y0, y1, hgt * 0.15f, hgt * 0.15f, bw, hgt, 0f, c1);
                    Rect(t, x0, x0 + wdt * 0.44f, y0, y0, hgt * 0.85f, hgt * 0.85f, bw, hgt, hgt * 0.15f, c1);
                    Rect(t, x1 - wdt * 0.44f, x1, y1, y1, hgt * 0.85f, hgt * 0.85f, bw, hgt, hgt * 0.15f, c1);
                    break;
                case 2:
                    Rect(t, x0, x1, y0, y1, hgt, hgt, bw, hgt, 0f, c1);
                    Rect(t, x1, x1 + wdt * 0.8f, y1, y1, hgt * 0.3f, hgt * 0.3f, bw, hgt, hgt * 0.7f, c1);  // носок — пятка
                    Rect(t, x0, x1, y0, y1, hgt * 0.15f, hgt * 0.15f, bw, hgt, 0f, c2);                      // резинка
                    wdt *= 1.8f;
                    break;
                case 3:
                    {
                        int bands = 1 + _r.Next(4);
                        for (int i = 0; i < bands; i++)
                            Rect(t, x0, x1, y0, y1, hgt / bands, hgt / bands, bw, hgt, hgt * i / bands, (i % 2 == 0) ? c1 : c2);
                    }
                    break;
                case 4:
                    {
                        // платье — шире книзу
                        Color a1 = Weighted(c1, bw), a2 = Weighted(c1, bw + 0.9f);
                        float yb0 = y0 - hgt, yb1 = y1 - hgt;
                        AddQuad(t, new Vector3(x0 - wdt * 0.2f, yb0, 0f), new Vector3(x0 + wdt * 0.2f, y0, 0f),
                                   new Vector3(x1 - wdt * 0.2f, y1, 0f), new Vector3(x1 + wdt * 0.2f, yb1, 0f), a2, a1, a1, a2);
                    }
                    break;
                case 5:
                    Rect(t, x0, x1, y0, y1, hgt * 0.5f, hgt * 0.5f, bw, hgt, 0f, c1);
                    Rect(t, x0 + wdt * 0.3f, x1 - wdt * 0.3f, y0, y1, hgt * 0.5f, hgt * 0.5f, bw, hgt, hgt * 0.5f, c1);
                    break;
                default:
                    Rect(t, x0, x1, y0, y1, hgt, hgt, bw, hgt, 0f, c1);
                    break;
            }
            // прищепки
            Color pin = Weighted(new Color(0.85f, 0.65f, 0.4f), bw);
            float pw = 0.03f * cs, ph = 0.07f * cs;
            AddQuad(t, new Vector3(x0 + pw, y0 - ph, -0.01f), new Vector3(x0 + pw, y0 + ph * 0.4f, -0.01f), new Vector3(x0 + 3f * pw, y0 + ph * 0.4f, -0.01f), new Vector3(x0 + 3f * pw, y0 - ph, -0.01f), pin, pin, pin, pin);
            return wdt;
        }
#endif
    }
}
