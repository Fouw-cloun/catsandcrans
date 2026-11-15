using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v18: ФОРМЫ ВЕЩЕСТВ — меши для кучки на полу, горки в таре и на полотне мастерка/лопаты.
    ///   • Порошок — немного кривая горка с глыбами и комками (мелочь и зерно рисует шейдер Powder Heap);
    ///   • Масло   — плотный пласт со скруглённым краем, мазки мастерком (бороздка + выдавленный валик),
    ///               сверху завиток-«пика», как будто срезали ножом и собрали.
    /// Точка меша = центр основания. Раскладка зависит только от seed — у всех игроков одинаково.
    /// </summary>
    public static class FoodSubstanceMesh
    {
        private static int Rings = 16, Segs = 44;      // v18.7: у масла сетка гуще (складки)

        private static readonly List<Vector3> V = new List<Vector3>(2048);
        private static readonly List<Color32> C = new List<Color32>(2048);
        private static readonly List<int> T = new List<int>(8192);
        private static readonly List<int> Centres = new List<int>(4);
        private static readonly List<Vector2> U1 = new List<Vector2>(2048);   // v23.6: высота 0..1 и радиус кучки (для шейдера Anime Heap)
        private static readonly List<Vector2> U2 = new List<Vector2>(2048);   // v23.6: точка края полотна у висящих капель
        private static readonly List<int> DripI = new List<int>(256);
        private static readonly List<float> DripT = new List<float>(256);
        private static readonly List<Vector2> DripR = new List<Vector2>(256);
        private static float _dripMaxL;

        /// <summary>v23.6: сколько висящих капель пасты с края полотна (ставит рисующий перед сборкой, потом 0).</summary>
        public static int SpreadDrips;
        /// <summary>v23.24: во сколько раз крупнее кучки пасты и масла на мастерке, лопате и полу (в таре — свой размер).</summary>
        public static float SoftHeapScale = 3.2f;
        /// <summary>v23.25: куда смотрит плоский хвост капли пасты (угол в осях сетки, рад); NaN — случайно. Ставят мастерок/лопата — к ручке.</summary>
        public static float SpreadTailAng = float.NaN;

        /// <summary>
        /// v23.25: угол в осях сетки (x, z) в сторону ручки инструмента: центр всех моделей инструмента смещён к ручке
        /// относительно центра полотна. NaN — не понять (тогда хвост случайно).
        /// </summary>
        public static float HandleAngle(Transform toolRoot, Quaternion frameRot, Vector3 bladeCenter, Renderer skip)
        {
            if (toolRoot == null) return float.NaN;
            Renderer[] rs = toolRoot.GetComponentsInChildren<Renderer>(false);
            bool any = false;
            Bounds b = new Bounds();
            foreach (Renderer r in rs)
            {
                if (r == null || r == skip || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                if (r.name.Contains("(auto)") || r.name.Contains("SubstanceHeap")) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return float.NaN;
            Vector3 loc = Quaternion.Inverse(frameRot) * (b.center - bladeCenter);
            Vector2 xz = new Vector2(loc.x, loc.z);
            if (xz.magnitude < Mathf.Max(0.004f, b.extents.magnitude * 0.05f)) return float.NaN;
            return Mathf.Atan2(xz.y, xz.x);
        }
        private static Dims SoftScaled(Dims d) { float k = Mathf.Max(0.1f, SoftHeapScale); d.Rx *= k; d.Rz *= k; d.H *= k; return d; }
        /// <summary>v23.6: где край полотна, в долях радиуса кучки (капля доползает до него и свисает).</summary>
        public static float DripEdge = 1.08f;
        // v23.6: свой цвет вершины сетки (x, z, rho, theta, цвет) — ставится на время одной сетки
        private static System.Func<float, float, float, float, Color, Color> GridTint;

        /// <summary>
        /// Общие размеры. rx, rz — полуоси основания; h — высота в центре; edge — доля высоты у края
        /// (0 — горка до земли, 0.5 — тара); rectN — 2 овал, больше — скруглённый прямоугольник.
        /// </summary>
        public struct Dims
        {
            public float Rx, Rz, H, Edge, RectN;
        }

        // ───────────────────────── ПОРОШОК ─────────────────────────

        public static void Powder(Mesh m, Dims d, float wonky, Color col, int seed)
        {
            Begin();
            Rings = 30; Segs = 84;
            var rnd = new System.Random(seed);
            float ph1 = (float)rnd.NextDouble() * 6.28f, ph2 = (float)rnd.NextDouble() * 6.28f;
            float lean = (float)rnd.NextDouble() * 6.28f;
            float ox = (float)rnd.NextDouble() * 50f, oz = (float)rnd.NextDouble() * 50f;
            float R = Mathf.Max(d.Rx, d.Rz);
            bool mound = d.Edge <= 0.001f;
            float wob = mound ? wonky * 0.14f : wonky * 0.035f;

            // v23.8: РЫХЛАЯ ГОРКА ИЗ КОМЬЕВ (рельеф — в модели): комья двух размеров (крупные мягкие + мелкие),
            // часть клеток пустая — гладкие места между комьями; ложбинки между комьями — в альфу цвета
            // (шейдер кладёт туда тень-бороздку). Фракция по цвету: белая мука — мельче и мягче,
            // бежевая (миндальная) — крупнее, тёмная (какао) — крупная и горка конусом.
            float coarse = Mathf.Clamp01((col.r - col.b - 0.12f) / 0.13f);
            float lum = col.r * 0.299f + col.g * 0.587f + col.b * 0.114f;
            float peaky = lum < 0.45f ? 1f : 0f;
            if (lum < 0.45f) coarse = Mathf.Max(coarse, 0.65f);
            float ls1 = 4.6f - 1.4f * coarse, ls2 = 11f - 4f * coarse;
            // v23.9: бежевая (миндальная) — хлопья, цвет насыщеннее; крупные (миндальная, какао) — мелкое зерно по комьям
            float beige = lum >= 0.45f ? Mathf.Clamp01((col.r - col.b - 0.12f) / 0.13f) : 0f;
            if (beige > 0.01f) col = Saturate(col, 1f + 0.25f * beige);
            if (beige < 0.3f && lum > 0.7f) col = Color.Lerp(col, new Color(0.99f, 0.985f, 0.97f, 1f), 0.65f);   // v23.9b: обычная мука — белоснежная
            float ls3 = beige > 0.3f ? 16f : 22f;                                     // v23.12: миндальная — крупное зерно
            float g3 = beige > 0.3f ? 0.75f : 0.5f * coarse;
            float p3 = beige > 0.3f ? 0.93f : 0.8f;
            bool darkP = lum < 0.45f;
            float cx1 = ox * 0.37f, cz1 = oz * 0.37f;
            HeightFn hf = (x, z, rho, th) =>
            {
                float rc = Mathf.Min(rho, 1f);
                float dome = Mathf.Pow(Mathf.Max(0f, 1f - rho * rho), 0.8f), cone = Mathf.Pow(Mathf.Max(0f, 1f - rho), 1.1f);
                float prof = mound ? Mathf.Lerp(dome, cone, peaky * 0.6f) : d.Edge + (1f - d.Edge) * (1f - rho * rho);
                float y = d.H * prof;
                y *= 1f + wonky * 0.15f * (x / R * Mathf.Cos(lean) + z / R * Mathf.Sin(lean));
                float inner = 1f - Mathf.Pow(rc, 4f);
                float c1, k1, c2, k2;
                CapField(x / R * ls1 + cx1, z / R * ls1 + cz1, 0.55f, 0, out c1, out k1);
                CapField(x / R * ls2 + cz1, z / R * ls2 + cx1, 0.7f, 13, out c2, out k2);
                y += (c1 * (R / ls1) * 0.45f + c2 * (R / ls2) * 0.35f) * inner * (mound ? 1f : 0.5f);
                float c3, k3;
                CapField(x / R * ls3 + cx1 * 1.7f, z / R * ls3 + cz1 * 1.3f, p3, 27, out c3, out k3);
                y += c3 * (R / ls3) * g3 * inner * (mound ? 1f : 0.5f);
                y += (Mathf.PerlinNoise(x / (R * 0.5f) + ox, z / (R * 0.5f) + oz) - 0.5f) * d.H * 0.12f * inner;
                return Mathf.Max(0f, y);
            };
            GridTint = (x, z, rho, th, vc) =>
            {
                float c1, k1, c2, k2;
                CapField(x / R * ls1 + cx1, z / R * ls1 + cz1, 0.55f, 0, out c1, out k1);
                CapField(x / R * ls2 + cz1, z / R * ls2 + cx1, 0.7f, 13, out c2, out k2);
                float inner = 1f - Mathf.Pow(Mathf.Min(rho, 1f), 4f);
                float c3, k3;
                CapField(x / R * ls3 + cx1 * 1.7f, z / R * ls3 + cz1 * 1.3f, p3, 27, out c3, out k3);
                float crease = Mathf.Max(Mathf.Max(k1 * 0.5f, k2 * 0.3f), k3 * (beige > 0.3f ? 0.6f : 0.4f * coarse)) * inner;
                return new Color(vc.r, vc.g, vc.b, 1f - crease);
            };
            Grid(d, hf, th => 1f + wob * (Mathf.Sin(2f * th + ph1) * 0.6f + Mathf.Sin(3f * th + ph2) * 0.4f), col, true);
            GridTint = null;
            // v23.12: КОМОЧКИ И ПЕСЧИНКИ ПО ГОРКЕ (фракция видна моделью):
            //   какао — много мелких песчинок и комочков; миндальная — густое крупное зерно; обычная мука — немного круглых комков
            if (mound)
            {
                int nSurf = darkP ? 130 : (beige > 0.3f ? 130 : 70);                // v23.14: комков намного больше
                for (int i = 0; i < nSurf; i++)
                {
                    float rr = Mathf.Sqrt((float)rnd.NextDouble()) * 0.9f, a = (float)rnd.NextDouble() * 6.28f;
                    float x = Mathf.Cos(a) * rr * d.Rx, z = Mathf.Sin(a) * rr * d.Rz;
                    float sz = R * (darkP ? Mathf.Lerp(0.012f, 0.05f, Mathf.Pow((float)rnd.NextDouble(), 1.6f))
                                 : beige > 0.3f ? Mathf.Lerp(0.018f, 0.032f, (float)rnd.NextDouble())
                                 : Mathf.Lerp(0.035f, 0.085f, (float)rnd.NextDouble()));
                    float tint = 0.93f + 0.14f * (float)rnd.NextDouble();
                    Color gc = new Color(Mathf.Min(1f, col.r * tint), Mathf.Min(1f, col.g * tint), Mathf.Min(1f, col.b * tint), 1f);
                    float sink = beige < 0.3f && !darkP ? -0.25f : 0.1f;                // комки муки — наполовину в горке
                    // v23.14: разной округлой формы — круглые, приплюснутые, вытянутые, чуть бугристые; мелких больше
                    if (rnd.NextDouble() < 0.45) sz *= 0.55f;
                    Lump(new Vector3(x, hf(x, z, rr, a) + sz * sink, z), sz, Mathf.Lerp(0.6f, 1f, (float)rnd.NextDouble()),
                         Mathf.Lerp(1f, 1.6f, (float)rnd.NextDouble()), (float)rnd.NextDouble() * 6.28f, gc, rnd);
                }
            }
            // крошки у подножия
            if (mound)
            {
                int nc = Mathf.RoundToInt(Mathf.Lerp(8f, 18f, coarse)) + rnd.Next(4) + (darkP || beige > 0.3f ? 45 : 20);   // v23.14: песчинки и комочки вокруг
                Color cc = new Color(col.r * 0.96f, col.g * 0.96f, col.b * 0.96f, 1f);
                for (int i = 0; i < nc; i++)
                {
                    float a = (float)rnd.NextDouble() * 6.28f, rr = Mathf.Lerp(0.97f, 1.18f, (float)rnd.NextDouble());
                    float sz = R * Mathf.Lerp(0.022f, 0.045f, coarse) * (0.6f + 0.8f * (float)rnd.NextDouble());
                    if (i >= nc - (darkP || beige > 0.3f ? 45 : 20)) sz *= 0.5f;             // мелкие песчинки
                    Blob(new Vector3(Mathf.Cos(a) * rr * d.Rx, sz * 0.45f, Mathf.Sin(a) * rr * d.Rz), sz, 0.9f, cc);
                }
            }
            Finish(m);
        }

        /// <summary>v23.9: хлопье — тонкая неровная пластинка (7 углов) с толщиной, грани резкие.</summary>
        private static void Flake(Vector3 c, Vector3 n, float r, Color col, System.Random rnd)
        {
            const int K = 7;
            n.Normalize();
            Vector3 t1 = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 t2 = Vector3.Cross(n, t1);
            Vector3 th = n * (r * 0.12f);
            var ring = new Vector3[K];
            for (int k = 0; k < K; k++)
            {
                float a = k / (float)K * Mathf.PI * 2f + ((float)rnd.NextDouble() - 0.5f) * 0.5f;
                float rr = r * (0.75f + 0.4f * (float)rnd.NextDouble());
                ring[k] = c + (t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a)) * rr;
            }
            Color32 cc = col;
            Color32 cb = new Color(col.r * 0.9f, col.g * 0.9f, col.b * 0.9f, 1f);
            for (int k = 0; k < K; k++)
            {
                int k1 = (k + 1) % K;
                TriOut(c, ring[k], ring[k1], n, cc);                                     // верх
                TriOut(c - th, ring[k] - th, ring[k1] - th, -n, cb);                     // низ
                Vector3 sideN = ((ring[k] + ring[k1]) * 0.5f - c).normalized;            // бок
                TriOut(ring[k], ring[k1], ring[k1] - th, sideN, cb);
                TriOut(ring[k], ring[k1] - th, ring[k] - th, sideN, cb);
            }
        }

        /// <summary>Треугольник лицом в сторону out (свои вершины — грани резкие).</summary>
        private static void TriOut(Vector3 a, Vector3 b, Vector3 c, Vector3 outDir, Color32 col)
        {
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c);
            C.Add(col); C.Add(col); C.Add(col);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outDir) >= 0f) { T.Add(i); T.Add(i + 1); T.Add(i + 2); }
            else { T.Add(i); T.Add(i + 2); T.Add(i + 1); }
        }

        /// <summary>Насыщеннее (k &gt; 1) или бледнее (k &lt; 1), яркость та же.</summary>
        public static Color Saturate(Color c, float k)
        {
            float l = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            return new Color(Mathf.Clamp01(l + (c.r - l) * k), Mathf.Clamp01(l + (c.g - l) * k), Mathf.Clamp01(l + (c.b - l) * k), c.a);
        }

        /// <summary>
        /// v23.8: поле комьев — клетки, в части клеток круглый ком (полусфера) своего размера и высоты.
        /// best — высота кома 0..1, crease — ложбинка между двумя соседними комьями (0..1).
        /// </summary>
        private static void CapField(float u, float v, float presence, int seed, out float best, out float crease)
        {
            int iu = Mathf.FloorToInt(u), iv = Mathf.FloorToInt(v);
            float fu = u - iu, fv = v - iv;
            best = 0f;
            float second = 0f;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = iu + dx, cy = iv + dy;
                    if (Hash01(cx, cy, 13 + seed) >= presence) continue;
                    float rx = Hash01(cx, cy, 11 + seed), ry = Hash01(cx, cy, 12 + seed);
                    float px = dx + 0.15f + 0.7f * rx - fu, py = dy + 0.15f + 0.7f * ry - fv;
                    float rad = 0.4f + 0.45f * ry;
                    float q = rad * rad - px * px - py * py;
                    if (q <= 0f) continue;
                    float cap = Mathf.Sqrt(q) / rad * (0.45f + 0.55f * rx);
                    if (cap > best) { second = best; best = cap; }
                    else if (cap > second) second = cap;
                }
            crease = second > 0.05f ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((best - second) / 0.15f)) : 0f;
        }

        /// <summary>v23.14: комок — бугристый шарик: приплюснут (flat), вытянут (stretch) по направлению ang.</summary>
        private static void Lump(Vector3 c, float r, float flat, float stretch, float ang, Color col, System.Random rnd)
        {
            const int RR = 5, SS = 9;
            int start = V.Count;
            Color32 cc = col;
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            float p1 = (float)rnd.NextDouble() * 6.28f, p2 = (float)rnd.NextDouble() * 6.28f;
            for (int i = 0; i <= RR; i++)
            {
                float phi = i / (float)RR * Mathf.PI;
                for (int j = 0; j < SS; j++)
                {
                    float th = j / (float)SS * Mathf.PI * 2f;
                    float w = 1f + 0.14f * Mathf.Sin(3f * th + p1) * Mathf.Sin(phi * 2f + p2);
                    float lx = Mathf.Sin(phi) * Mathf.Cos(th) * r * w * stretch, lz = Mathf.Sin(phi) * Mathf.Sin(th) * r * w;
                    float ly = Mathf.Cos(phi) * r * w * flat;
                    V.Add(c + new Vector3(lx * ca - lz * sa, ly, lx * sa + lz * ca));
                    C.Add(cc);
                }
            }
            for (int i = 0; i < RR; i++)
                for (int j = 0; j < SS; j++)
                {
                    int j1 = (j + 1) % SS;
                    int a = start + i * SS + j, b = start + i * SS + j1, c2 = a + SS, d2 = b + SS;
                    T.Add(a); T.Add(b); T.Add(c2);
                    T.Add(b); T.Add(d2); T.Add(c2);
                }
        }

        /// <summary>v23.7: крошка — приплюснутый шарик.</summary>
        private static void Blob(Vector3 c, float r, float flat, Color col)
        {
            const int RR = 5, SS = 8;
            int start = V.Count;
            Color32 cc = col;
            for (int i = 0; i <= RR; i++)
            {
                float phi = i / (float)RR * Mathf.PI;
                for (int j = 0; j < SS; j++)
                {
                    float th = j / (float)SS * Mathf.PI * 2f;
                    V.Add(c + new Vector3(Mathf.Sin(phi) * Mathf.Cos(th) * r, Mathf.Cos(phi) * r * flat, Mathf.Sin(phi) * Mathf.Sin(th) * r));
                    C.Add(cc);
                }
            }
            for (int i = 0; i < RR; i++)
                for (int j = 0; j < SS; j++)
                {
                    int j1 = (j + 1) % SS;
                    int a = start + i * SS + j, b = start + i * SS + j1, c2 = a + SS, d2 = b + SS;
                    T.Add(a); T.Add(b); T.Add(c2);
                    T.Add(b); T.Add(d2); T.Add(c2);
                }
        }

        // ───────────────────────── МАСЛО ─────────────────────────


        /// <summary>
        /// v18.8: КОМКИ «фермерского» масла: ближайшие клетки (9 соседей), у каждой — свой круглый
        /// комок своей высоты и размера; соседние сливаются мягко. 0..~1.
        /// </summary>
        public static float ClumpField(float u, float v)
        {
            int iu = Mathf.FloorToInt(u), iv = Mathf.FloorToInt(v);
            float h = 0f;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = iu + dx, cy = iv + dy;
                    float px = cx + 0.15f + 0.7f * Hash01(cx, cy, 11), py = cy + 0.15f + 0.7f * Hash01(cx, cy, 12);
                    float rad = 0.5f + 0.45f * Hash01(cx, cy, 13);
                    float sx = 0.75f + 0.5f * Hash01(cx, cy, 15);                     // чуть вытянутые
                    float ddx = (u - px) * sx, ddy = (v - py) / sx;
                    float k = Mathf.Clamp01(1f - Mathf.Sqrt(ddx * ddx + ddy * ddy) / rad);
                    float hk = Mathf.Sqrt(k * (2f - k)) * (0.45f + 0.55f * Hash01(cx, cy, 14));  // круглый верх
                    h = SMax(h, hk, 0.18f);
                }
            return h;
        }

        /// <summary>Трещинки-разрывы между комками: узкие ложбинки (0..1).</summary>
        public static float Creases(float u, float v)
        {
            float n = Mathf.PerlinNoise(u + 3.7f, v + 9.2f);
            float r = 1f - Mathf.Abs(n * 2f - 1f);
            return Mathf.Pow(r, 6f);
        }

        /// <summary>
        /// Высота «рваного» фермерского масла в долях (u, v — доли основания, rho — 0 центр, 1 край):
        /// мягкий купол + крупные комки + мелкие комочки − трещинки. Без отвесных стенок.
        /// </summary>
        public static float FarmButter(float u, float v, float rho, float ox, float oz, float lumpScale)
        {
            float dome = Mathf.Pow(Mathf.Max(0f, 1f - rho * rho), 1.2f);
            float env = Mathf.SmoothStep(1f, 0.45f, rho);
            float big = ClumpField(u * lumpScale + ox, v * lumpScale + oz);
            float small = ClumpField(u * lumpScale * 2.6f + oz * 1.7f, v * lumpScale * 2.6f + ox * 1.3f);
            float cr = Creases(u * 3.2f + ox, v * 3.2f + oz);
            float h = dome * 0.5f + big * 0.42f * env + small * 0.12f * env - cr * 0.12f * env;
            return Mathf.Max(0f, h);
        }

        private static float SMax(float a, float b, float r)
        {
            float h = Mathf.Max(r - Mathf.Abs(a - b), 0f);
            return Mathf.Max(a, b) + h * h / (4f * Mathf.Max(1e-6f, r));
        }

        /// <summary>
        /// v23.9: МАСЛО — БРУСОК (пол, тара): прямоугольник со скруглёнными краями и плоским верхом,
        /// по верху — три бороздки от ножа с выдавленным валиком (на валике — полоска глянца),
        /// сверху лежит стружка-завиток. Аппетитно и сразу читается как масло.
        /// </summary>
        public static void Butter(Mesh m, Dims d, float curl, int smears, float smearDepth, Color col, int seed)
        {
            ButterCubes(m, d, col, seed, true);                                         // v23.12: мягкие кубики горкой
        }

        /// <summary>v23.9: МАСЛО НА ПОЛОТНЕ — тонкий брусок-пластинка с бороздками (без стружки).</summary>
        public static void ButterSmear(Mesh m, Dims d, float curl, Color col, int seed)
        {
            ButterCubes(m, d, col, seed, false);
        }

        /// <summary>
        /// v23.12: МАСЛО — МЯГКИЕ КУБИКИ: на полу горка из 4–5 кубиков (3 внизу, 1–2 сверху наискосок),
        /// на полотне — 2–3 кубика рядом. Края скруглены, низ чуть шире (масло мягкое, чуть «осело»).
        /// </summary>
        private static void ButterCubes(Mesh m, Dims d, Color col, int seed, bool pile)
        {
            Begin();
            var rnd = new System.Random(seed);
            bool mound = d.Edge <= 0.001f;
            if (mound) d = SoftScaled(d);                                               // v23.24: кубики масла в 3 раза крупнее
            float Rm = Mathf.Min(d.Rx, d.Rz);
            float baseY = mound ? 0f : d.H * d.Edge;
            // v23.13: кубики помельче и побольше, лежат неряшливо — криво, друг на друге
            int nCube = pile && mound ? 10 + rnd.Next(4) : 4 + rnd.Next(3);
            float s = pile && mound ? Rm * 0.36f : Mathf.Min(d.Rx, d.Rz) * 0.42f;
            int[] perLayer = pile && mound ? new[] { 6, 4, 3 } : new[] { 4, 2, 1 };
            int made = 0;
            for (int L = 0; L < perLayer.Length && made < nCube; L++)
            {
                float ringR = (pile && mound ? Rm * 0.55f : Mathf.Min(d.Rx, d.Rz) * 0.5f) * (1f - 0.35f * L);
                for (int k = 0; k < perLayer[L] && made < nCube; k++, made++)
                {
                    float a = (k + (float)rnd.NextDouble() * 0.6f) / perLayer[L] * 6.283f + L * 0.7f;
                    float rr = ringR * Mathf.Lerp(0.3f, 1f, (float)rnd.NextDouble());
                    float sz = s * Mathf.Lerp(0.8f, 1.12f, (float)rnd.NextDouble());
                    float tiltA = L == 0 ? 8f : 28f;
                    Quaternion q = Quaternion.Euler(((float)rnd.NextDouble() - 0.5f) * 2f * tiltA, (float)rnd.NextDouble() * 360f, ((float)rnd.NextDouble() - 0.5f) * 2f * tiltA);
                    Vector3 c = new Vector3(Mathf.Cos(a) * rr, baseY + sz * 0.42f + L * s * 0.78f, Mathf.Sin(a) * rr);
                    SoftCube(c, q, sz * 0.5f, 0.3f, Tint(col, rnd));
                }
            }
            Finish(m);
        }

        private static Color Tint(Color c, System.Random rnd)
        {
            float t = 0.95f + 0.1f * (float)rnd.NextDouble();
            return new Color(Mathf.Min(1f, c.r * t), Mathf.Min(1f, c.g * t), Mathf.Min(1f, c.b * t), 1f);
        }

        /// <summary>v23.12: кубик со скруглёнными краями (round — доля половины ребра), низ чуть шире, верх чуть выпуклый.</summary>
        private static void SoftCube(Vector3 c, Quaternion q, float half, float round, Color col)
        {
            const int N = 6;
            Color32 cc = col;
            float lim = 1f - round;
            for (int f = 0; f < 6; f++)
            {
                Vector3 n = CubeN[f];
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(n, u);
                int b = V.Count;
                for (int i = 0; i <= N; i++)
                    for (int j = 0; j <= N; j++)
                    {
                        Vector3 p = n + u * (-1f + 2f * i / N) + v * (-1f + 2f * j / N);
                        Vector3 inner = new Vector3(Mathf.Clamp(p.x, -lim, lim), Mathf.Clamp(p.y, -lim, lim), Mathf.Clamp(p.z, -lim, lim));
                        Vector3 dd = p - inner;
                        Vector3 lp = inner + (dd.sqrMagnitude > 1e-8f ? dd.normalized * round : Vector3.zero);
                        float sag = 1f + 0.1f * (1f - (lp.y + 1f) * 0.5f);              // осело: низ шире
                        lp.x *= sag; lp.z *= sag;
                        if (lp.y > 0f) lp.y += 0.05f * (1f - lp.x * lp.x) * (1f - lp.z * lp.z);   // верх чуть выпуклый
                        V.Add(c + q * (lp * half));
                        C.Add(cc);
                    }
                Vector3 p0 = V[b], p1 = V[b + N + 1], p2 = V[b + 1];
                bool outward = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), q * n) > 0f;
                for (int i = 0; i < N; i++)
                    for (int j = 0; j < N; j++)
                    {
                        int i0 = b + i * (N + 1) + j, i1 = i0 + N + 1, i2 = i0 + 1, i3 = i1 + 1;
                        if (outward) { T.Add(i0); T.Add(i1); T.Add(i2); T.Add(i2); T.Add(i1); T.Add(i3); }
                        else { T.Add(i0); T.Add(i2); T.Add(i1); T.Add(i2); T.Add(i3); T.Add(i1); }
                    }
            }
        }

        private static void ButterPat(Mesh m, Dims d, float curl, Color col, int seed, float hK, bool shaving)
        {
            Begin();
            Rings = 36; Segs = 96;
            var rnd = new System.Random(seed);
            bool mound = d.Edge <= 0.001f;
            Dims dd = d;
            dd.RectN = 4f;
            if (Mathf.Abs(d.Rx - d.Rz) < 0.05f * Mathf.Max(d.Rx, d.Rz)) dd.Rz = d.Rz * 0.74f;    // брусок, а не круг
            float rx0 = Mathf.Max(1e-4f, dd.Rx), rz0 = Mathf.Max(1e-4f, dd.Rz);
            float tilt = ((float)rnd.NextDouble() - 0.5f) * 0.08f;
            float[] v0s = { -0.32f, 0.02f, 0.34f };
            HeightFn hf = (x, z, rho, th) =>
            {
                float u = x / rx0, v = z / rz0;
                float top = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(Mathf.Max(0f, (rho - 0.68f) / 0.32f), 2f)));
                float flat = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rho - 0.55f) / 0.15f));
                float y = 0.62f * hK * top * (1f + tilt * u);
                for (int i = 0; i < 3; i++)
                {
                    float sv = v - (v0s[i] + 0.07f * Mathf.Sin(u * 2.2f + v0s[i] * 5f));
                    y += (-0.05f * Mathf.Exp(-(sv / 0.05f) * (sv / 0.05f)) + 0.035f * Mathf.Exp(-((sv - 0.075f) / 0.035f) * ((sv - 0.075f) / 0.035f))) * flat * hK;
                }
                if (!mound) return Mathf.Max(0f, d.H * (d.Edge + (1f - d.Edge) * y));
                return Mathf.Max(0f, d.H * y * 1.6f);
            };
            GridTint = (x, z, rho, th, vc) =>
            {
                float u = x / rx0, v = z / rz0;
                float flat = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rho - 0.55f) / 0.15f));
                float grv = 0f, lip = 0f;
                for (int i = 0; i < 3; i++)
                {
                    float sv = v - (v0s[i] + 0.07f * Mathf.Sin(u * 2.2f + v0s[i] * 5f));
                    grv = Mathf.Max(grv, Mathf.Exp(-(sv / 0.05f) * (sv / 0.05f)));
                    lip = Mathf.Max(lip, Mathf.Exp(-((sv - 0.075f) / 0.035f) * ((sv - 0.075f) / 0.035f)));
                }
                float t = 1f - 0.1f * grv * flat;
                return new Color(vc.r * t, vc.g * t, vc.b * t, 1f - lip * flat);
            };
            Grid(dd, hf, th => 1f, col, true);
            GridTint = null;
            if (shaving && mound && curl > 0.01f)
            {
                float topY = hf(-0.15f * rx0, 0.05f * rz0, 0.2f, 0f);
                float rc = Mathf.Min(rx0, rz0) * 0.22f * Mathf.Min(curl, 1.2f);
                Color sc = new Color(Mathf.Min(1f, col.r * 1.04f), Mathf.Min(1f, col.g * 1.04f), Mathf.Min(1f, col.b * 1.04f), 1f);
                Curl(new Vector3(-0.15f * rx0, topY - rc * 0.3f, 0.05f * rz0), new Vector3(1f, 0f, 0.3f).normalized, rc, sc);
            }
            Finish(m);
        }

        /// <summary>Рваный край: несколько гармоник + острые «надрывы».</summary>
        private static RimFn RaggedRim(System.Random rnd, float amp)
        {
            float p1 = (float)rnd.NextDouble() * 6.28f, p2 = (float)rnd.NextDouble() * 6.28f;
            float p3 = (float)rnd.NextDouble() * 6.28f, p4 = (float)rnd.NextDouble() * 6.28f;
            return th =>
            {
                float w = Mathf.Sin(2f * th + p1) * 0.4f + Mathf.Sin(3f * th + p2) * 0.3f
                        + Mathf.Sin(5f * th + p3) * 0.2f + Mathf.Sin(9f * th + p4) * 0.1f;
                float tear = Mathf.Pow(Mathf.Abs(Mathf.Sin(4f * th + p2 * 1.3f)), 8f);    // надрывы
                return 1f + amp * w - amp * 0.8f * tear;
            };
        }

        private static void Curl(Vector3 basePt, Vector3 dir, float rc, Color col)
        {
            const int N = 14, A = 10;
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            int start = V.Count;
            Color32 cc = col;
            Vector3 prev = basePt;
            for (int i = 0; i <= N; i++)
            {
                float t = i / (float)N;
                float ph = t * Mathf.PI * 1.15f;
                Vector3 p = basePt + Vector3.up * (rc * Mathf.Sin(ph)) + dir * (rc * (1f - Mathf.Cos(ph)) * 0.9f);
                float t2 = Mathf.Min(1f, t + 1f / N);
                float ph2 = t2 * Mathf.PI * 1.15f;
                Vector3 p2 = basePt + Vector3.up * (rc * Mathf.Sin(ph2)) + dir * (rc * (1f - Mathf.Cos(ph2)) * 0.9f);
                Vector3 tan = i < N ? (p2 - p).normalized : (p - prev).normalized;
                if (tan.sqrMagnitude < 1e-8f) tan = Vector3.up;
                Vector3 nrm = Vector3.Cross(side, tan).normalized;
                float r = rc * 0.5f * Mathf.Pow(1f - t, 0.75f) + rc * 0.02f;
                float w = r * 1.7f, th = r * 0.6f;                         // плоская лента, а не трубка
                for (int a = 0; a < A; a++)
                {
                    float an = a / (float)A * Mathf.PI * 2f;
                    V.Add(p + side * (Mathf.Cos(an) * w) + nrm * (Mathf.Sin(an) * th));
                    C.Add(cc);
                }
                prev = p;
            }
            for (int i = 0; i < N; i++)
                for (int a = 0; a < A; a++)
                {
                    int a1 = (a + 1) % A;
                    int i0 = start + i * A + a, i1 = start + i * A + a1, j0 = i0 + A, j1 = i1 + A;
                    T.Add(i0); T.Add(j0); T.Add(i1);
                    T.Add(i1); T.Add(j0); T.Add(j1);
                }
        }

        // ───────────────────────── ИСПОРЧЕНО (универсально) ─────────────────────────

        /// <summary>v19: 0 — нормально, 1 — испорчено (ставит рисующий перед сборкой меша, потом сбрасывает).</summary>
        public static float SpoilK;

        /// <summary>
        /// Испорченная масса — для ЛЮБОГО вещества: тусклая, серо-зелёные пятна «плесени», белёсые точки,
        /// тёмные подтёки. x, z — метры (узор не зависит от сетки).
        /// </summary>
        public static Color SpoilColor(Color c, float x, float z, float k)
        {
            float n = Mathf.PerlinNoise(x * 18f + 31.7f, z * 18f + 7.3f);
            float n2 = Mathf.PerlinNoise(x * 55f + 3.1f, z * 55f + 19.9f);
            float n3 = Mathf.PerlinNoise(x * 9f + 11.1f, z * 9f + 41.3f);
            Color dull = new Color(c.r * 0.7f + 0.08f, c.g * 0.72f + 0.09f, c.b * 0.6f + 0.05f, 1f);   // тусклее, в серо-зелень
            Color mold = new Color(0.4f, 0.47f, 0.31f, 1f);
            Color fuzz = new Color(0.86f, 0.89f, 0.8f, 1f);
            Color r = Color.Lerp(c, dull, 0.7f * k);
            r = Color.Lerp(r, mold, Mathf.SmoothStep(0.52f, 0.68f, n) * 0.85f * k);
            r = Color.Lerp(r, fuzz, Mathf.SmoothStep(0.74f, 0.82f, n2) * 0.8f * k);
            r = Color.Lerp(r, r * 0.55f, Mathf.SmoothStep(0.6f, 0.75f, n3) * 0.5f * k);
            r.a = 1f;
            return r;
        }

        // ───────────────────────── КРИСТАЛЛЫ (сахар, соль) ─────────────────────────

        /// <summary>
        /// v19: КУЧКА САХАРА — пологая горка, на ней россыпь мелких кубиков-кристаллов разного размера
        /// и поворота (каждый своего оттенка); блеск даёт шейдер CatCooks/Sugar.
        /// </summary>
        public static void Crystals(Mesh m, Dims d, float crystalSize, float density, Color col, int seed)
        {
            Begin();
            Rings = 18; Segs = 48;
            var rnd = new System.Random(seed);
            bool mound = d.Edge <= 0.001f;
            float R = Mathf.Max(d.Rx, d.Rz);
            float ox = (float)rnd.NextDouble() * 50f, oz = (float)rnd.NextDouble() * 50f;
            HeightFn hf = (x, z, rho, th) =>
            {
                float prof = mound ? Mathf.Pow(Mathf.Max(0f, 1f - rho * rho), 0.85f)
                                   : d.Edge + (1f - d.Edge) * (1f - rho * rho);
                float y = d.H * prof * 0.9f;
                y += (Mathf.PerlinNoise(x / (R * 0.4f) + ox, z / (R * 0.4f) + oz) - 0.5f) * d.H * 0.3f * (1f - Mathf.Pow(rho, 4f));   // v23.7: рельефнее
                y += (Chunks(x / (R * 0.22f) + oz, z / (R * 0.22f) + ox) + 0.1f) * d.H * 0.12f * (1f - Mathf.Pow(rho, 3f));
                return Mathf.Max(0f, y);
            };
            Grid(d, hf, RaggedRim(rnd, mound ? 0.06f : 0.02f), col * 0.92f, true);

            // кристаллики
            float cs = Mathf.Max(0.002f, crystalSize * R);
            float area = Mathf.PI * d.Rx * d.Rz;
            int n = Mathf.Clamp(Mathf.RoundToInt(area / (cs * cs) * 0.35f * density), 40, 700);
            for (int i = 0; i < n; i++)
            {
                float rr = Mathf.Sqrt((float)rnd.NextDouble()) * 0.97f, a = (float)rnd.NextDouble() * 6.28f;
                float x = Mathf.Cos(a) * rr * d.Rx, z = Mathf.Sin(a) * rr * d.Rz;
                float y = hf(x, z, rr, a);
                float sz = cs * 0.6f * (0.5f + (float)rnd.NextDouble() * 1.2f);            // v23.9: кристаллы мельче (столько же)
                Quaternion q = Quaternion.Euler((float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f);
                float tint = 0.95f + (float)rnd.NextDouble() * 0.2f;
                Color cc = new Color(Mathf.Min(1f, col.r * tint), Mathf.Min(1f, col.g * tint), Mathf.Min(1f, col.b * tint), 1f);
                if (SpoilK > 0.001f) cc = SpoilColor(cc, x, z, SpoilK);
                Cube(new Vector3(x, y + sz * 0.3f, z), q, sz, cc);
            }
            Finish(m);
        }

        private static readonly Vector3[] CubeN = { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

        private static void Cube(Vector3 c, Quaternion q, float s, Color col)
        {
            float h = s * 0.5f;
            Color32 cc = col;
            for (int f = 0; f < 6; f++)
            {
                Vector3 n = CubeN[f];
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(n, u);
                int b = V.Count;
                V.Add(c + q * ((n - u - v) * h)); V.Add(c + q * ((n + u - v) * h));
                V.Add(c + q * ((n + u + v) * h)); V.Add(c + q * ((n - u + v) * h));
                C.Add(cc); C.Add(cc); C.Add(cc); C.Add(cc);
                Vector3 p0 = V[b], p1 = V[b + 1], p2 = V[b + 2];
                bool outward = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), q * n) > 0f;   // в Unity лицевая — когда нормаль наружу
                if (outward) { T.Add(b); T.Add(b + 1); T.Add(b + 2); T.Add(b); T.Add(b + 2); T.Add(b + 3); }
                else { T.Add(b); T.Add(b + 2); T.Add(b + 1); T.Add(b); T.Add(b + 3); T.Add(b + 2); }
            }
        }

        // ───────────────────────── ПАСТА (нутелла) ─────────────────────────

        /// <summary>
        /// v19: ШОКОЛАДНАЯ ПАСТА — гладкая глянцевая горка, закрученная спиралью (как нутелла из ложки),
        /// мягкие волны по краю, на макушке завиток. Испорченная — «свернувшаяся», в комочках.
        /// </summary>
        public static void Spread(Mesh m, Dims d, float curl, Color col, int seed)
        {
            Begin();
            var rnd = new System.Random(seed);
            bool mound = d.Edge <= 0.001f;
            float ph = (float)rnd.NextDouble() * 6.28f;
            float ox = (float)rnd.NextDouble() * 50f, oz = (float)rnd.NextDouble() * 50f;
            float spoil = SpoilK;
            // v23.11: тёплый насыщенный молочный шоколад (#4E2A1E–#6B3E2E) — без серо-коричневых и болотных тонов
            col = Color.Lerp(col, new Color(0.37f, 0.21f, 0.15f, 1f), 0.75f);
            col.a = 1f;
            if (!mound)
            {
                // поверхность в таре — ровная масса с мягкой спиралью (как раньше)
                Rings = 28; Segs = 72;
                HeightFn hft = (x, z, rho, th) =>
                {
                    float u = x / Mathf.Max(1e-4f, d.Rx), v = z / Mathf.Max(1e-4f, d.Rz);
                    float a = Mathf.Atan2(v, u);
                    float sw = Mathf.Sin(rho * Mathf.PI * 2.2f - a + ph);                  // v23.11: 1–2 широкие мягкие волны
                    float h = (1f - rho * rho) + (0.5f + 0.5f * sw) * 0.12f * (1f - Mathf.Pow(rho, 3f));
                    if (spoil > 0.001f) h += (Mathf.PerlinNoise(u * 7f + ox, v * 7f + oz) - 0.45f) * 0.14f * spoil * (1f - rho);
                    return Mathf.Max(0f, d.H * (d.Edge + (1f - d.Edge) * h));
                };
                Grid(d, hft, th => 1f, col, true);
                Finish(m);
                return;
            }

            // v23.11: ПАСТА — ГЛАДКАЯ КНЕЛЯ (как нутелла в ложке): обтекаемый овал без бугров и пик,
            // 1–2 широкие мягкие волны и мягкая кромка сложенного слоя; на мастерке — мягкий язычок к краю
            // полотна и одна тонкая длинная нить, висящая вниз (всегда к полу).
            bool blade = SpreadDrips > 0;
            // v23.12: гладкий глянцевый КОМ (как нутелла на ложке), без висящей нити, сверху — кусочки какао-бобов
            // v23.13: ЗАМКНУТЫЙ ГЛАДКИЙ КОМ (дно закрыто), без гребней-полосок (никаких «крестиков»), кусочки какао-бобов
            // v23.14: БОЛЬШАЯ КАПЛЯ пасты (чуть темнее) с лёгкими мазками от мастерка + комочки пасты и шоколадные осколки рядом
            // v23.15: МЯГКИЕ КУСКИ пасты (как шоколадное масло на тарелке): 2–3 скруглённых бруска друг на друге,
            // на верхних гранях — гладкие глянцевые мазки ножом (полоска блеска), пара мягких пиков там, где нож оторвали
            Color dc = new Color(col.r * 0.85f, col.g * 0.85f, col.b * 0.85f, 1f);
            PasteDropSmear(SoftScaled(d), dc, rnd, spoil, ox, oz);               // v23.18: мазок-капля; v23.24: в 3 раза крупнее
            Finish(m);
        }

        private static void Quenelle(Dims d, Color col, float ph, float spoil, float ox, float oz, System.Random rnd, bool blade, float edge)
        {
            Rings = 40; Segs = 104;
            float rx0 = Mathf.Max(1e-4f, d.Rx), rz0 = Mathf.Max(1e-4f, d.Rz), H = d.H;
            float rot = blade ? 0f : (float)rnd.NextDouble() * 6.28f;
            float cr = Mathf.Cos(rot), sr = Mathf.Sin(rot);
            float wph = ph * 0.3f;
            // язычок: в сторону +x полотна сетка дотягивается до края
            RimFn rim = th =>
            {
                if (!blade) return 1f;
                float dth = Mathf.DeltaAngle(0f, th * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                return 1f + (edge - 1f) * Mathf.Exp(-(dth / 0.4f) * (dth / 0.4f));
            };
            HeightFn hf = (x, z, rho, th) =>
            {
                float y, crest, tone;
                QuenelleEval(x / rx0, z / rz0, cr, sr, wph, blade, edge, out y, out crest, out tone);
                y *= H;
                if (spoil > 0.001f) y += (Mathf.PerlinNoise(x / rx0 * 7f + ox, z / rz0 * 7f + oz) - 0.45f) * 0.14f * spoil * H;
                return Mathf.Max(0f, y * (1f - Mathf.Pow(Mathf.Min(rho, 1f), 10f)));
            };
            GridTint = (x, z, rho, th, vc) =>
            {
                float y, crest, tone;
                QuenelleEval(x / rx0, z / rz0, cr, sr, wph, blade, edge, out y, out crest, out tone);
                return new Color(Mathf.Min(1f, vc.r * tone), Mathf.Min(1f, vc.g * tone), Mathf.Min(1f, vc.b * tone), 1f - crest);
            };
            Grid(d, hf, rim, col, true);
            GridTint = null;
        }

        /// <summary>
        /// v23.13: ком пасты — деформированная сфера: снизу приплюснут и чуть шире (осел), сверху мягкий кончик,
        /// одна едва заметная спиральная волна. Возвращает высоту кома.
        /// </summary>
        private static float PasteBall(Dims d, Color col, float ph, float spoil, float ox, float oz, System.Random rnd)
        {
            const int LAT = 22, LON = 44;
            float rx = d.Rx * 0.82f, rz = d.Rz * 0.82f;
            float hh = Mathf.Max(d.H * 1.1f, Mathf.Min(rx, rz) * 0.95f);
            float lean = (float)rnd.NextDouble() * 6.28f;
            Vector2 tipOff = new Vector2(Mathf.Cos(lean), Mathf.Sin(lean)) * Mathf.Min(rx, rz) * 0.12f;
            int start = V.Count;
            Color32 cc = col;
            var ring = new Vector3[LAT + 1, LON];
            float minY = float.MaxValue;
            for (int i = 0; i <= LAT; i++)
            {
                float phi = i / (float)LAT * Mathf.PI;                          // 0 верх … π низ
                float cy = Mathf.Cos(phi), sy = Mathf.Sin(phi);
                for (int j = 0; j < LON; j++)
                {
                    float th = j / (float)LON * Mathf.PI * 2f;
                    float wob = 1f + 0.03f * Mathf.Sin(th + phi * 5f + ph);      // одна мягкая спиральная волна
                    // v23.14: лёгкие мазки от мастерка — 3 пологих среза наискосок (бороздка и валик), едва заметно
                    float sm = Mathf.Sin(th * 1f + phi * 3.2f + ph * 2f) * 0.5f + Mathf.Sin(th * 2f - phi * 2.1f + ph) * 0.5f;
                    wob += -0.022f * Mathf.Exp(-((sm - 0.55f) / 0.12f) * ((sm - 0.55f) / 0.12f)) + 0.014f * Mathf.Exp(-((sm - 0.75f) / 0.08f) * ((sm - 0.75f) / 0.08f));
                    if (spoil > 0.001f) wob += (Mathf.PerlinNoise(th * 2f + ox, phi * 3f + oz) - 0.5f) * 0.12f * spoil;
                    float drop = Mathf.Pow(Mathf.Sin(phi * 0.5f), 0.9f);             // v23.14: форма капли — узкий мягкий верх, широкий низ
                    Vector3 p = new Vector3(sy * drop * 1.25f * Mathf.Cos(th) * rx * wob, cy * hh * 0.5f, sy * drop * 1.25f * Mathf.Sin(th) * rz * wob);
                    if (cy < 0f) { p.y *= 0.35f; float w = 1f + 0.12f * (-cy); p.x *= w; p.z *= w; }   // низ приплюснут, шире
                    float tipK = Mathf.Pow(Mathf.Max(0f, cy), 6f);
                    p.y += hh * 0.08f * tipK;                                     // мягкий кончик
                    p.x += tipOff.x * tipK; p.z += tipOff.y * tipK;
                    ring[i, j] = p;
                    if (p.y < minY) minY = p.y;
                }
            }
            Centres.Add(start);                                                   // макушка: одна нормаль, без «звёздочки»
            Segs = LON;
            for (int i = 0; i <= LAT; i++)
                for (int j = 0; j < LON; j++)
                {
                    Vector3 p = ring[i, j];
                    p.y -= minY;
                    V.Add(p);
                    C.Add(cc);
                }
            for (int i = 0; i < LAT; i++)
                for (int j = 0; j < LON; j++)
                {
                    int j1 = (j + 1) % LON;
                    int a = start + i * LON + j, b = start + i * LON + j1, c2 = a + LON, d2 = b + LON;
                    T.Add(a); T.Add(b); T.Add(c2);
                    T.Add(b); T.Add(d2); T.Add(c2);
                }
            // точки верхней части капли — для кусочков бобов (лежат точно на поверхности)
            _ballPts.Clear(); _ballNrm.Clear();
            float cyAxis = -minY + hh * 0.05f;
            for (int i = 3; i <= LAT / 2; i++)
                for (int j = 0; j < LON; j += 3)
                {
                    Vector3 p = V[start + i * LON + j];
                    _ballPts.Add(p);
                    _ballNrm.Add(new Vector3(p.x, (p.y - cyAxis) * 1.2f, p.z).normalized);
                }
            return hh;
        }

        private static readonly List<Vector3> _ballPts = new List<Vector3>(128), _ballNrm = new List<Vector3>(128);

        /// <summary>v23.13: кусочки какао-бобов — тёмные угловатые сколы (сплюснутые брусочки), лежат на коме.</summary>
        private static void BeanChips(Dims d, float hh, System.Random rnd)
        {
            if (_ballPts.Count == 0) return;
            float rm = Mathf.Min(d.Rx, d.Rz) * 0.82f;
            int nn = 3 + rnd.Next(3);
            for (int i = 0; i < nn; i++)
            {
                int k = rnd.Next(_ballPts.Count);
                Vector3 p = _ballPts[k], n = _ballNrm[k];
                float sz = rm * Mathf.Lerp(0.07f, 0.11f, (float)rnd.NextDouble());
                Vector3 fwd = Vector3.Cross(n, Vector3.up);
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                Quaternion q = Quaternion.LookRotation(fwd.normalized, n) * Quaternion.Euler(0f, ((float)rnd.NextDouble() - 0.5f) * 80f, ((float)rnd.NextDouble() - 0.5f) * 20f);
                Color nc = Tint(new Color(0.26f, 0.12f, 0.08f, 1f), rnd);
                Box(p + n * (sz * 0.12f), q, new Vector3(sz, sz * 0.45f, sz * 0.7f), nc);
            }
        }

        /// <summary>
        /// v23.16: ПАСТА — «КОПНУЛИ ЛОПАТОЙ»: пологая мягкая горка, из неё вынут гладкий вогнутый след лопаты
        /// (края-стенки чёткие, по краям лёгкие валики, в конце следа — выдавленный валик). Срез светлее и глянцевый
        /// (гребни и середина среза — в альфу: шейдер кладёт полоску блеска). Никаких пиков.
        /// </summary>
        private static void PasteScoop(Dims d, Color col, System.Random rnd, float spoil, float ox, float oz)
        {
            Rings = 44; Segs = 112;
            float rx0 = Mathf.Max(1e-4f, d.Rx), rz0 = Mathf.Max(1e-4f, d.Rz), H = d.H;
            float rot = (float)rnd.NextDouble() * 6.28f, cr = Mathf.Cos(rot), sr = Mathf.Sin(rot);
            float p1 = (float)rnd.NextDouble() * 6.28f;
            HeightFn hf = (x, z, rho, th) =>
            {
                float y, crest, tone;
                ScoopEval(x / rx0, z / rz0, cr, sr, ox, oz, out y, out crest, out tone);
                y *= H;
                if (spoil > 0.001f) y += (Mathf.PerlinNoise(x / rx0 * 7f + ox, z / rz0 * 7f + oz) - 0.45f) * 0.14f * spoil * H;
                return Mathf.Max(0f, y * (1f - Mathf.Pow(Mathf.Min(rho, 1f), 10f)));
            };
            GridTint = (x, z, rho, th, vc) =>
            {
                float y, crest, tone;
                ScoopEval(x / rx0, z / rz0, cr, sr, ox, oz, out y, out crest, out tone);
                return new Color(Mathf.Min(1f, vc.r * tone), Mathf.Min(1f, vc.g * tone), Mathf.Min(1f, vc.b * tone), 1f - crest);
            };
            Grid(d, hf, th => 1f + 0.035f * Mathf.Sin(3f * th + p1) + 0.02f * Mathf.Sin(7f * th + p1 * 2f), col, true);
            GridTint = null;
        }

        /// <summary>
        /// v23.17: ПАСТА — МАЗОК НА ШПАТЕЛЕ (как шпаклёвка на шпателе): гладкий округлый ком, с одной стороны ровно срезан
        /// другим шпателем — плоская наклонная грань с чёткой кромкой; у кромки — одна мягкая складка-завиток;
        /// вокруг — тонкий подмазанный слой с полосами от шпателя. Кромка, складка и полосы — в альфу (полоски блеска).
        /// </summary>
        private static void PasteSpatula(Dims d, Color col, System.Random rnd, float spoil, float ox, float oz)
        {
            Rings = 44; Segs = 112;
            float rx0 = Mathf.Max(1e-4f, d.Rx), rz0 = Mathf.Max(1e-4f, d.Rz), H = d.H;
            float rot = (float)rnd.NextDouble() * 6.28f, cr = Mathf.Cos(rot), sr = Mathf.Sin(rot);
            float p1 = (float)rnd.NextDouble() * 6.28f;
            HeightFn hf = (x, z, rho, th) =>
            {
                float y, crest, tone;
                SpatulaEval(x / rx0, z / rz0, cr, sr, out y, out crest, out tone);
                y *= H;
                if (spoil > 0.001f) y += (Mathf.PerlinNoise(x / rx0 * 7f + ox, z / rz0 * 7f + oz) - 0.45f) * 0.14f * spoil * H;
                return Mathf.Max(0f, y * (1f - Mathf.Pow(Mathf.Min(rho, 1f), 10f)));
            };
            GridTint = (x, z, rho, th, vc) =>
            {
                float y, crest, tone;
                SpatulaEval(x / rx0, z / rz0, cr, sr, out y, out crest, out tone);
                return new Color(Mathf.Min(1f, vc.r * tone), Mathf.Min(1f, vc.g * tone), Mathf.Min(1f, vc.b * tone), 1f - crest);
            };
            Grid(d, hf, th => 1f + 0.03f * Mathf.Sin(3f * th + p1) + 0.02f * Mathf.Sin(7f * th + p1 * 2f), col, true);
            GridTint = null;
        }

        /// <summary>
        /// v23.18: ПАСТА — МАЗОК-КАПЛЯ на шпателе: толстая округлая «голова» плавно переходит в заострённый хвост
        /// (по нему прошёл шпатель): вдоль мазка мягкая бороздка и глянцевый валик сбоку. Контур сетки — сама капля,
        /// поэтому у основания нет плоской подложки.
        /// </summary>
        private static void PasteDropSmear(Dims d, Color col, System.Random rnd, float spoil, float ox, float oz)
        {
            Rings = 44; Segs = 120;
            float rx0 = Mathf.Max(1e-4f, d.Rx), rz0 = Mathf.Max(1e-4f, d.Rz), H = d.H;
            float rotT = (float)rnd.NextDouble() * 6.28f;
            if (!float.IsNaN(SpreadTailAng)) rotT = SpreadTailAng;                // v23.25: плоский хвост — к ручке
            float cr = Mathf.Cos(rotT), sr = Mathf.Sin(rotT);
            RimFn rim = th => 0.55f + 0.45f * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(th - rotT)), 6f);   // капля: хвост в сторону rotT
            HeightFn hf = (x, z, rho, th) =>
            {
                float y, crest, tone;
                DropSmearEval(x / rx0, z / rz0, rho, th, rotT, cr, sr, out y, out crest, out tone);
                y *= H;
                if (spoil > 0.001f) y += (Mathf.PerlinNoise(x / rx0 * 7f + ox, z / rz0 * 7f + oz) - 0.45f) * 0.14f * spoil * H;
                return Mathf.Max(0f, y);
            };
            GridTint = (x, z, rho, th, vc) =>
            {
                float y, crest, tone;
                DropSmearEval(x / rx0, z / rz0, rho, th, rotT, cr, sr, out y, out crest, out tone);
                return new Color(Mathf.Min(1f, vc.r * tone), Mathf.Min(1f, vc.g * tone), Mathf.Min(1f, vc.b * tone), 1f - crest);
            };
            Grid(d, hf, rim, col, true);
            GridTint = null;
        }

        private static void DropSmearEval(float u0, float v0, float rho, float th, float rotT, float cr, float sr,
                                          out float y, out float crest, out float tone)
        {
            float tailK = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(th - rotT)), 6f);
            float body = Mathf.Pow(Mathf.Max(0f, 1f - rho * rho), 0.55f);
            float hgtK = 1f - 0.72f * tailK;                                                   // к хвосту ниже (v23.19)
            float u = u0 * cr + v0 * sr, v = -u0 * sr + v0 * cr;
            // v23.19: мазок по всей капле — 4 ленты от шпателя внахлёст: пологий подъём и чёткая блестящая кромка с одной стороны
            y = 0.72f * body * hgtK;
            crest = 0f; tone = 1f;
            for (int k = 0; k < 4; k++)
            {
                float o = -0.36f + 0.24f * k;
                float vs = o + 0.09f * Mathf.Sin(u * 2f + k * 1.3f) + 0.05f * u;
                float sx = v - vs;
                float prof = sx < 0f ? Mathf.Exp(-(sx / 0.16f) * (sx / 0.16f)) : Mathf.Exp(-(sx / 0.035f) * (sx / 0.035f));
                float along = SS01((u + 0.55f - 0.08f * k) / 0.25f) * (1f - SS01((u - (0.75f - 0.1f * k)) / 0.25f));
                y += 0.13f * prof * along * body;
                float ce = (sx - 0.012f) / 0.022f;
                crest = Mathf.Max(crest, Mathf.Exp(-ce * ce) * along * body);
                tone *= 1f + 0.05f * prof * along;
            }
            y = Mathf.Max(0f, y);
            crest = Mathf.Clamp01(crest);
        }

        private static float SS01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        private static void SpatulaEval(float u0, float v0, float cr, float sr, out float y, out float crest, out float tone)
        {
            float u = u0 * cr + v0 * sr, v = -u0 * sr + v0 * cr;
            // тонкий подмазанный слой с полосами шпателя
            float streak = 0.5f + 0.5f * Mathf.Sin(v * 22f + Mathf.Sin(u * 3.1f) * 1.5f);
            float smearMask = SS01((0.95f - Mathf.Sqrt(u * u + v * v)) / 0.15f) * SS01((u + 0.9f) / 0.4f);
            float layer = (0.03f + 0.03f * streak) * smearMask;
            // ком
            float du = (u + 0.12f) / 0.62f, dv = (v - 0.04f) / 0.55f;
            float rr = Mathf.Sqrt(du * du + dv * dv);
            float dome = 0.85f * Mathf.Pow(Mathf.Max(0f, 1f - rr * rr), 0.55f);
            // срез другим шпателем со стороны +u: наклонная плоская грань
            float cutX = 0.22f + dome * 0.18f;
            float cutK = SS01((u - cutX) / 0.04f);
            float dollop = dome * (1f - cutK);
            // мягкая складка-завиток у кромки
            float fx = u - (cutX - 0.12f);
            float body = dome > 0.1f ? 1f : 0f;
            float fold = 0.07f * Mathf.Exp(-(fx / 0.05f) * (fx / 0.05f)) * body * (1f - cutK);
            y = Mathf.Max(layer, dollop + fold);
            float edge = Mathf.Exp(-((u - cutX) / 0.03f) * ((u - cutX) / 0.03f)) * (dome > 0.15f ? 1f : 0f);
            crest = Mathf.Clamp01(edge + 0.7f * Mathf.Exp(-(fx / 0.02f) * (fx / 0.02f)) * body * (1f - cutK)
                                  + 0.35f * streak * smearMask * (dollop < layer + 0.01f ? 1f : 0f));
            tone = 0.96f + 0.1f * SS01((u - cutX + 0.06f) / 0.06f) * body;                      // свежий срез светлее
        }

        private static void ScoopEval(float u0, float v0, float cr, float sr, float ox, float oz, out float y, out float crest, out float tone)
        {
            float u = u0 * cr + v0 * sr, v = -u0 * sr + v0 * cr;
            float rr = Mathf.Sqrt(u * u + (v / 0.9f) * (v / 0.9f));
            float mound = 0.75f * Mathf.Pow(Mathf.Max(0f, 1f - rr * rr), 0.7f);
            mound *= 1f + 0.08f * (Mathf.PerlinNoise(u * 2.3f + ox * 0.1f, v * 2.3f + oz * 0.1f) - 0.5f);
            // след лопаты: вдоль u (от -0.5 до 0.55), ширина ±0.42 по v
            float su = u - 0.05f;
            float across = Mathf.Abs(v) / 0.42f;
            float along = (su + 0.5f) / 1.05f;
            float depA = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(along / 0.15f)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((along - 0.85f) / 0.15f)));
            float wall = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((across - 0.88f) / 0.12f));      // чёткая стенка
            float carve = 0.42f * (1f - Mathf.Min(1f, across * across)) * depA * wall;
            float rim = 0.05f * Mathf.Exp(-((across - 1.03f) / 0.06f) * ((across - 1.03f) / 0.06f)) * depA;
            float lip = 0.12f * Mathf.Exp(-((along - 1.03f) / 0.07f) * ((along - 1.03f) / 0.07f)) * Mathf.Max(0f, 1f - across * across);
            y = Mathf.Max(0.04f * mound, mound - carve) + (rim + lip) * Mathf.Clamp01(mound * 3f);
            float inScoop = depA * wall * (across < 1f ? 1f : 0f);
            crest = Mathf.Clamp01(Mathf.Exp(-((across - 1.03f) / 0.03f) * ((across - 1.03f) / 0.03f)) * depA
                                  + Mathf.Exp(-((along - 1.03f) / 0.03f) * ((along - 1.03f) / 0.03f)) * Mathf.Max(0f, 1f - across * across)
                                  + 0.6f * Mathf.Exp(-(across / 0.22f) * (across / 0.22f)) * inScoop);
            tone = 0.95f + 0.11f * inScoop;                                                       // свежий срез светлее
        }

        private static void PasteChunks(Dims d, Color col, System.Random rnd, float spoil, float ox, float oz)
        {
            float rm = Mathf.Min(d.Rx, d.Rz);
            int n = rm > 0.12f ? 2 + rnd.Next(2) : 1 + rnd.Next(2);
            float yaw0 = (float)rnd.NextDouble() * 360f;
            var tops = new List<Vector3>();
            for (int k = 0; k < n; k++)
            {
                Vector3 half = new Vector3(rm * Mathf.Lerp(0.55f, 0.75f, (float)rnd.NextDouble()), rm * Mathf.Lerp(0.2f, 0.28f, (float)rnd.NextDouble()), rm * Mathf.Lerp(0.26f, 0.34f, (float)rnd.NextDouble()));
                float yaw = yaw0 + (k - (n - 1) * 0.5f) * 22f + ((float)rnd.NextDouble() - 0.5f) * 14f;
                float side = (k - (n - 1) * 0.5f) * rm * 0.5f;
                bool onTop = k == n - 1 && n > 2;
                Vector3 c = Quaternion.Euler(0f, yaw0, 0f) * new Vector3(0f, 0f, onTop ? 0f : side);
                c.y = half.y * 0.85f + (onTop ? half.y * 1.2f : 0f);
                Quaternion q = Quaternion.Euler(onTop ? 10f : ((float)rnd.NextDouble() - 0.5f) * 8f, yaw, onTop ? 14f : ((float)rnd.NextDouble() - 0.5f) * 10f);
                SoftBlock(c, q, half, 0.42f, col, rnd, spoil, ox, oz);
                tops.Add(c + q * new Vector3(((float)rnd.NextDouble() - 0.5f) * half.x, half.y * 0.95f, ((float)rnd.NextDouble() - 0.5f) * half.z * 0.6f));
            }
            // пара мягких пиков
            for (int k = 0; k < Mathf.Min(2, tops.Count); k++)
            {
                float sz = rm * Mathf.Lerp(0.08f, 0.12f, (float)rnd.NextDouble());
                Lump(tops[k] + Vector3.up * (sz * 0.3f), sz, 1.9f, 1.1f, (float)rnd.NextDouble() * 6.28f, col, rnd);
            }
        }

        /// <summary>
        /// v23.15: мягкий брусок (половины рёбер half, скругление round). Верх срезан ножом: гладкий, с лёгкими бороздками
        /// и глянцевыми мазками (гребень — в альфу цвета: шейдер кладёт полоску блеска); бока чуть рыхлее.
        /// </summary>
        private static void SoftBlock(Vector3 c, Quaternion q, Vector3 half, float round, Color col, System.Random rnd, float spoil, float ox, float oz)
        {
            const int N = 10;
            float lim = 1f - round;
            float ph = (float)rnd.NextDouble() * 6.28f;
            for (int f = 0; f < 6; f++)
            {
                Vector3 n = CubeN[f];
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(n, u);
                int b = V.Count;
                for (int i = 0; i <= N; i++)
                    for (int j = 0; j <= N; j++)
                    {
                        Vector3 p = n + u * (-1f + 2f * i / N) + v * (-1f + 2f * j / N);
                        Vector3 inner = new Vector3(Mathf.Clamp(p.x, -lim, lim), Mathf.Clamp(p.y, -lim, lim), Mathf.Clamp(p.z, -lim, lim));
                        Vector3 dd = p - inner;
                        Vector3 dir = dd.sqrMagnitude > 1e-8f ? dd.normalized : n;
                        Vector3 lp = inner + dir * round;
                        float topK = Mathf.Clamp01(dir.y);
                        // мазки ножом по верху: пологие бороздки вдоль бруска
                        float sm = Mathf.Sin(lp.z * 7f + Mathf.Sin(lp.x * 2.3f + ph) * 1.4f + ph);
                        lp += dir * (topK * 0.035f * sm);
                        // бока и низ — чуть мягче, осели
                        float sag = 1f + 0.08f * (1f - (lp.y + 1f) * 0.5f);
                        lp.x *= sag; lp.z *= sag;
                        if (spoil > 0.001f) lp += dir * (Mathf.PerlinNoise(lp.x * 3f + ox, lp.z * 3f + oz) - 0.5f) * 0.12f * spoil;
                        V.Add(c + q * Vector3.Scale(lp, half));
                        float gloss = topK * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((sm - 0.55f) / 0.35f));
                        float t = 0.95f + 0.1f * topK;
                        C.Add(new Color(Mathf.Min(1f, col.r * t), Mathf.Min(1f, col.g * t), Mathf.Min(1f, col.b * t), 1f - gloss * 0.9f));
                    }
                Vector3 p0 = V[b], p1 = V[b + N + 1], p2 = V[b + 1];
                bool outward = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), q * n) > 0f;
                for (int i = 0; i < N; i++)
                    for (int j = 0; j < N; j++)
                    {
                        int i0 = b + i * (N + 1) + j, i1 = i0 + N + 1, i2 = i0 + 1, i3 = i1 + 1;
                        if (outward) { T.Add(i0); T.Add(i1); T.Add(i2); T.Add(i2); T.Add(i1); T.Add(i3); }
                        else { T.Add(i0); T.Add(i2); T.Add(i1); T.Add(i2); T.Add(i3); T.Add(i1); }
                    }
            }
        }

        /// <summary>v23.14: вокруг капли — 3–5 маленьких комочков пасты и 3–4 тонких шоколадных осколка.</summary>
        private static void PasteBits(Dims d, Color col, System.Random rnd)
        {
            float rx = d.Rx * 0.82f, rz = d.Rz * 0.82f, rm = Mathf.Min(rx, rz);
            int nl = 3 + rnd.Next(3);
            for (int i = 0; i < nl; i++)
            {
                float a = (float)rnd.NextDouble() * 6.28f, rr = Mathf.Lerp(1.05f, 1.3f, (float)rnd.NextDouble());
                float sz = rm * Mathf.Lerp(0.09f, 0.16f, (float)rnd.NextDouble());
                Lump(new Vector3(Mathf.Cos(a) * rr * rx, sz * 0.4f, Mathf.Sin(a) * rr * rz), sz, 0.65f, Mathf.Lerp(1f, 1.4f, (float)rnd.NextDouble()), a, col, rnd);
            }
            int ns = 3 + rnd.Next(2);
            Color sc = new Color(0.22f, 0.11f, 0.07f, 1f);
            for (int i = 0; i < ns; i++)
            {
                float a = (float)rnd.NextDouble() * 6.28f, rr = Mathf.Lerp(1.05f, 1.35f, (float)rnd.NextDouble());
                float sz = rm * Mathf.Lerp(0.14f, 0.22f, (float)rnd.NextDouble());
                Quaternion q = Quaternion.Euler(Mathf.Lerp(10f, 50f, (float)rnd.NextDouble()), (float)rnd.NextDouble() * 360f, ((float)rnd.NextDouble() - 0.5f) * 30f);
                Box(new Vector3(Mathf.Cos(a) * rr * rx, sz * 0.25f, Mathf.Sin(a) * rr * rz), q, new Vector3(sz, sz * 0.12f, sz * 0.65f), Tint(sc, rnd));
            }
        }

        /// <summary>Брусок (размеры по осям).</summary>
        private static void Box(Vector3 c, Quaternion q, Vector3 size, Color col)
        {
            Vector3 h = size * 0.5f;
            Color32 cc = col;
            for (int f = 0; f < 6; f++)
            {
                Vector3 n = CubeN[f];
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(n, u);
                int b = V.Count;
                Vector3[] cs = { n - u - v, n + u - v, n + u + v, n - u + v };
                for (int k = 0; k < 4; k++) V.Add(c + q * Vector3.Scale(cs[k], h));
                C.Add(cc); C.Add(cc); C.Add(cc); C.Add(cc);
                Vector3 p0 = V[b], p1 = V[b + 1], p2 = V[b + 2];
                bool outward = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), q * n) > 0f;
                if (outward) { T.Add(b); T.Add(b + 1); T.Add(b + 2); T.Add(b); T.Add(b + 2); T.Add(b + 3); }
                else { T.Add(b); T.Add(b + 2); T.Add(b + 1); T.Add(b); T.Add(b + 3); T.Add(b + 2); }
            }
        }

        /// <summary>v23.12: кусочки какао-бобов на коме пасты — маленькие угловатые тёмные кубики, утоплены наполовину.</summary>
        private static void Nibs(Dims d, Color col, System.Random rnd)
        {
            float rx0 = Mathf.Max(1e-4f, d.Rx), rz0 = Mathf.Max(1e-4f, d.Rz);
            int nn = 4 + rnd.Next(3);
            Color nc = new Color(0.24f, 0.11f, 0.07f, 1f);
            for (int i = 0; i < nn; i++)
            {
                float rr = Mathf.Sqrt((float)rnd.NextDouble()) * 0.7f, a = (float)rnd.NextDouble() * 6.28f;
                float u = Mathf.Cos(a) * rr, v = Mathf.Sin(a) * rr;
                float y, crest, tone;
                QuenelleEval(u, v, 1f, 0f, 0f, false, 1f, out y, out crest, out tone);
                y *= d.H * (1f - Mathf.Pow(rr, 10f));
                float sz = Mathf.Min(rx0, rz0) * Mathf.Lerp(0.05f, 0.08f, (float)rnd.NextDouble());
                Quaternion q = Quaternion.Euler((float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f);
                Cube(new Vector3(u * rx0, y + sz * 0.1f, v * rz0), q, sz, Tint(nc, rnd));
            }
        }

        private static void QuenelleEval(float u0, float v0, float cr, float sr, float wph, bool blade, float edge,
                                         out float y, out float crest, out float tone)
        {
            float u = u0 * cr + v0 * sr, v = -u0 * sr + v0 * cr;
            if (blade && u > 0f) u /= Mathf.Lerp(1f, edge * 1.02f, Mathf.Exp(-(v / 0.35f) * (v / 0.35f)));   // язычок к краю
            float rr = Mathf.Sqrt(u * u + (v / 0.9f) * (v / 0.9f));
            float body = Mathf.Pow(Mathf.Max(0f, 1f - rr * rr), 0.45f);                    // v23.12: круглый гладкий ком
            y = 0.85f * body * (1f + 0.05f * Mathf.Clamp(-u, -1f, 1f));
            float s1 = v - 0.25f * Mathf.Sin(u * 2.2f + 0.7f + wph);
            float wave = Mathf.Exp(-(s1 / 0.2f) * (s1 / 0.2f));
            float s3 = v + 0.42f - 0.18f * Mathf.Sin(u * 1.8f + 2f + wph);
            float wave2 = Mathf.Exp(-(s3 / 0.16f) * (s3 / 0.16f));
            float s2 = u + 0.45f * v * v - 0.15f;
            float fold = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s2 + 0.06f) / 0.12f));
            y += (0.06f * wave + 0.03f * wave2 + 0.03f * fold) * body;
            float c1 = (s1 - 0.08f) / 0.05f, c3 = (s3 - 0.06f) / 0.04f, c2 = (s2 + 0.02f) / 0.04f;
            crest = Mathf.Clamp01((Mathf.Exp(-c1 * c1) + Mathf.Exp(-c3 * c3) * 0.7f + Mathf.Exp(-c2 * c2) * 0.8f) * body);
            tone = 1f + 0.06f * wave;
        }

        /// <summary>
        /// v23.11: ОДНА ТОНКАЯ ДЛИННАЯ НИТЬ пасты с кончика язычка: переваливается через край полотна и тянется вниз,
        /// к концу тоньше, на кончике — маленькая капля. Висящая часть помечена — шейдер держит её к полу и покачивает.
        /// </summary>
        private static void Strand(Dims d, Color col, System.Random rnd, float edge)
        {
            const int A = 8;
            float rx0 = Mathf.Max(1e-4f, d.Rx), rz0 = Mathf.Max(1e-4f, d.Rz);
            float r0 = Mathf.Min(rx0, rz0) * 0.05f;
            float L = rx0 * Mathf.Lerp(1.2f, 1.7f, (float)rnd.NextDouble());
            float zOff = ((float)rnd.NextDouble() - 0.5f) * rz0 * 0.2f;
            Vector3 E = new Vector3(rx0 * edge * 0.98f, r0 * 0.9f, zOff);
            var pts = new List<Vector3>(); var rad = new List<float>(); var tt = new List<float>();
            pts.Add(E - Vector3.right * (r0 * 3f) + Vector3.up * (r0 * 0.3f)); rad.Add(r0 * 1.6f); tt.Add(-1f);   // начало — в язычке
            pts.Add(E - Vector3.right * (r0 * 1.2f)); rad.Add(r0 * 1.3f); tt.Add(-1f);
            const int NA = 5, NB = 14;
            for (int i = 0; i <= NA; i++)                                                  // дуга через край
            {
                float a = i / (float)NA * Mathf.PI * 0.5f;
                pts.Add(E + new Vector3(Mathf.Sin(a) * r0 * 1.5f, -(1f - Mathf.Cos(a)) * r0 * 1.5f, 0f));
                rad.Add(r0 * Mathf.Lerp(1.15f, 0.95f, i / (float)NA)); tt.Add(i / (float)(NA + NB) * 0.3f);
            }
            Vector3 top = pts[pts.Count - 1];
            for (int i = 1; i <= NB; i++)                                                  // тянется вниз, тоньше
            {
                float u = i / (float)NB;
                pts.Add(top + Vector3.down * (L * u));
                float b = (u - 0.93f) / 0.06f;
                rad.Add(r0 * Mathf.Lerp(0.95f, 0.45f, Mathf.Pow(u, 0.7f)) + r0 * 0.55f * Mathf.Exp(-b * b));
                tt.Add(0.3f + 0.7f * u);
            }
            Vector2 root = new Vector2(E.x, E.z);
            Color32 cc = new Color(Mathf.Min(1f, col.r * 1.05f), Mathf.Min(1f, col.g * 1.05f), Mathf.Min(1f, col.b * 1.05f), 1f);
            int start = V.Count, n = pts.Count;
            Vector3 side = Vector3.forward, lastTan = Vector3.down;
            for (int i = 0; i < n; i++)
            {
                Vector3 tan = (pts[Mathf.Min(n - 1, i + 1)] - pts[Mathf.Max(0, i - 1)]).normalized;
                if (tan.sqrMagnitude < 1e-8f) tan = Vector3.down;
                lastTan = tan;
                Vector3 nrm = Vector3.Cross(side, tan).normalized;
                for (int a = 0; a < A; a++)
                {
                    float an = a / (float)A * Mathf.PI * 2f;
                    if (tt[i] >= 0f) { DripI.Add(V.Count); DripT.Add(tt[i]); DripR.Add(root); }
                    V.Add(pts[i] + side * (Mathf.Cos(an) * rad[i]) + nrm * (Mathf.Sin(an) * rad[i]));
                    C.Add(cc);
                }
            }
            if (L > _dripMaxL) _dripMaxL = L;
            for (int i = 0; i < n - 1; i++)
                for (int a = 0; a < A; a++)
                {
                    int a1 = (a + 1) % A;
                    int i0 = start + i * A + a, i1 = start + i * A + a1, j0 = i0 + A, j1 = i1 + A;
                    T.Add(i0); T.Add(j0); T.Add(i1);
                    T.Add(i1); T.Add(j0); T.Add(j1);
                }
            int tip = V.Count;
            DripI.Add(tip); DripT.Add(1f); DripR.Add(root);
            V.Add(pts[n - 1] + lastTan * (rad[n - 1] * 0.9f));
            C.Add(cc);
            int last = start + (n - 1) * A;
            for (int a = 0; a < A; a++) { T.Add(last + a); T.Add(tip); T.Add(last + (a + 1) % A); }
        }

        /// <summary>
        /// v23.9: ПАСТА РАЗМАЗАНА лопаткой — плоский глянцевый слой со скруглённым краем (поверхностное натяжение),
        /// по нему 3 дуги-волны от лопатки: валик с острым гребнем (на гребне полоска глянца) и ложбинка перед ним,
        /// в середине мягкий наплыв. Низкая — не «башенка».
        /// </summary>
        private static void PasteSmear(Dims d, Color col, float ph, float spoil, float ox, float oz, System.Random rnd)
        {
            Rings = 40; Segs = 104;
            float rx0 = Mathf.Max(1e-4f, d.Rx), rz0 = Mathf.Max(1e-4f, d.Rz), H = d.H;
            float rot = (float)rnd.NextDouble() * 6.28f, cr = Mathf.Cos(rot), sr = Mathf.Sin(rot);
            float flip = rnd.NextDouble() < 0.5 ? -1f : 1f;
            float[] cu = { 0.3f, -0.25f, 0f }, cv = { 0.12f, -0.12f, 0.32f }, r0 = { 0.5f, 0.55f, 0.28f };
            float[] a0 = { 0f, 2.5f, 4.2f }, span = { 3.8f, 3.6f, 4.5f };
            float p1 = (float)rnd.NextDouble() * 6.28f;
            HeightFn hf = (x, z, rho, th) =>
            {
                float y, crest, crease, tone;
                SmearEval(x / rx0, z / rz0, rho, cr, sr, flip, ph, cu, cv, r0, a0, span, out y, out crest, out crease, out tone);
                y *= H;
                if (spoil > 0.001f) y += (Mathf.PerlinNoise(x / rx0 * 7f + ox, z / rz0 * 7f + oz) - 0.45f) * 0.14f * spoil * H;
                return Mathf.Max(0f, y * (1f - Mathf.Pow(Mathf.Min(rho, 1f), 10f)));
            };
            GridTint = (x, z, rho, th, vc) =>
            {
                float y, crest, crease, tone;
                SmearEval(x / rx0, z / rz0, rho, cr, sr, flip, ph, cu, cv, r0, a0, span, out y, out crest, out crease, out tone);
                return new Color(vc.r * tone, vc.g * tone, vc.b * tone, 1f - crest * (1f - crease));
            };
            Grid(d, hf, th => 1f + 0.03f * Mathf.Sin(5f * th + p1) + 0.02f * Mathf.Sin(9f * th + p1 * 2f), col, true);
            GridTint = null;
        }

        private static void SmearEval(float u0, float v0, float rho, float cr, float sr, float flip, float ph,
                                      float[] cu, float[] cv, float[] r0, float[] a0, float[] span,
                                      out float y, out float crest, out float crease, out float tone)
        {
            float u = u0 * cr + v0 * sr, v = (-u0 * sr + v0 * cr) * flip;
            float plate = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(Mathf.Max(0f, (rho - 0.62f) / 0.38f), 2f)));
            y = 0.4f * plate * (1f + 0.06f * Mathf.Sin(u * 3f + ph) * Mathf.Cos(v * 2.5f));
            crest = 0f; crease = 0f; tone = 1f;
            for (int i = 0; i < cu.Length; i++)
            {
                float du = u - cu[i], dv = v - cv[i];
                float sgn = Mathf.Sqrt(du * du + dv * dv) - r0[i];
                float ang = Mathf.Repeat(Mathf.Atan2(dv, du) - a0[i], 6.2832f);
                float win = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ang / 0.5f)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((ang - (span[i] - 0.6f)) / 0.6f)));
                float rid = Mathf.Exp(-((sgn + 0.01f) / 0.06f) * ((sgn + 0.01f) / 0.06f)) * win;
                float grv = Mathf.Exp(-((sgn + 0.11f) / 0.06f) * ((sgn + 0.11f) / 0.06f)) * win;
                y += (0.15f * rid - 0.07f * grv) * plate;
                crest = Mathf.Max(crest, Mathf.Exp(-(sgn / 0.025f) * (sgn / 0.025f)) * win);
                crease = Mathf.Max(crease, grv * 0.8f);
                tone *= 1f + 0.08f * rid - 0.1f * grv;
            }
            y += 0.18f * Mathf.Exp(-((u - 0.05f) * (u - 0.05f) + (v - 0.05f) * (v - 0.05f)) / 0.05f);
        }

        /// <summary>
        /// v23.8: СКЛАДКИ ПАСТЫ — три наплыва (большой, средний, малый), сглаженный максимум с заметной бороздкой.
        /// Нижний наплыв темнее, верхний светлее; гребень-плечо каждого — в альфу (глянец), бороздка — темнее.
        /// </summary>
        private static void BlobStack(Dims d, Color col, float ph, float curl, float spoil, float ox, float oz, System.Random rnd)
        {
            Rings = 40; Segs = 104;
            float rot = (float)rnd.NextDouble() * 6.28f;
            float cr = Mathf.Cos(rot), sr = Mathf.Sin(rot);
            float[] bx = { 0f, 0.14f * cr + 0.06f * sr, -0.03f * cr - 0.06f * sr };
            float[] bz = { 0f, 0.14f * sr - 0.06f * cr, -0.03f * sr + 0.06f * cr };
            float[] br = { 1f, 0.72f, 0.45f };
            float[] bh = { 0.55f, 0.92f, 1.22f };
            float[] bt = { 0.9f, 1f, 1.07f };
            float H = d.H, kk = 0.035f * d.H;
            float rx0 = Mathf.Max(1e-4f, d.Rx), rz0 = Mathf.Max(1e-4f, d.Rz);
            float p1 = (float)rnd.NextDouble() * 6.28f;
            RimFn rim = th => 1f + 0.03f * Mathf.Sin(5f * th + p1) + 0.02f * Mathf.Sin(9f * th + p1 * 2f);
            HeightFn hf = (x, z, rho, th) =>
            {
                float y, crease, crest, tone;
                StackEval(x / rx0, z / rz0, bx, bz, br, bh, bt, H, kk, ph, out y, out crease, out crest, out tone);
                y += 0.28f * H * Mathf.Exp(-((x / rx0 - bx[2]) * (x / rx0 - bx[2]) + (z / rz0 - bz[2]) * (z / rz0 - bz[2])) / 0.01f);   // пика
                if (spoil > 0.001f) y += (Mathf.PerlinNoise(x / rx0 * 7f + ox, z / rz0 * 7f + oz) - 0.45f) * 0.14f * spoil * H;
                return Mathf.Max(0f, y * (1f - Mathf.Pow(Mathf.Min(rho, 1f), 8f)));
            };
            GridTint = (x, z, rho, th, vc) =>
            {
                float y, crease, crest, tone;
                StackEval(x / rx0, z / rz0, bx, bz, br, bh, bt, H, kk, ph, out y, out crease, out crest, out tone);
                float t = tone * (1f - 0.15f * crease);
                return new Color(vc.r * t, vc.g * t, vc.b * t, 1f - crest * (1f - crease));
            };
            Grid(d, hf, rim, col, true);
            GridTint = null;
            if (curl > 1.5f)
            {
                Vector3 top = new Vector3(bx[2] * rx0, hf(bx[2] * rx0, bz[2] * rz0, 0f, 0f), bz[2] * rz0);
                float rc = Mathf.Min(rx0, rz0) * 0.12f * Mathf.Min(curl, 2f) * 0.5f;
                Curl(top + Vector3.down * (rc * 0.6f), new Vector3(cr, 0f, sr), rc, col);
            }
        }

        private static void StackEval(float u, float v, float[] bx, float[] bz, float[] br, float[] bh, float[] bt, float H, float kk, float ph,
                                      out float y, out float crease, out float crest, out float tone)
        {
            y = 0f; crease = 0f; crest = 0f; tone = 1f;
            float ang = Mathf.Atan2(v, u);
            for (int i = 0; i < bx.Length; i++)
            {
                float rr = Mathf.Sqrt((u - bx[i]) * (u - bx[i]) + (v - bz[i]) * (v - bz[i])) / br[i];
                float yb = bh[i] * H * Mathf.Sqrt(Mathf.Max(0f, 1f - rr * rr)) * (1f + 0.05f * Mathf.Sin(rr * Mathf.PI * 3f - ang + i + ph));
                bool dom = yb >= y;
                if (yb > 0.01f * H && y > 0.01f * H)
                    crease = Mathf.Max(crease, 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Abs(yb - y) / (0.07f * H))));
                if (dom)
                {
                    crest = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rr - 0.58f) / 0.08f)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((rr - 0.72f) / 0.08f)));
                    tone = bt[i];
                }
                float hh = Mathf.Clamp01(0.5f + 0.5f * (yb - y) / kk);
                y = y + (yb - y) * hh + kk * hh * (1f - hh);
            }
        }

        /// <summary>v23.7: толстая лента пасты витками вверх: сужается к верху, кончик — пика.</summary>
        private static void Coil(Dims d, float t0, Color col, float ph, float curl, float spoil, float ox, float oz)
        {
            const int N = 110;
            float turns = 2.8f;
            float R = Mathf.Min(d.Rx, d.Rz);
            float rt0 = R * 0.26f, rt1 = R * 0.11f;
            var pts = new List<Vector3>(N + 1);
            var ws = new List<float>(N + 1);
            var hs = new List<float>(N + 1);
            var tones = new List<float>(N + 1);
            // естественная высота, потом подгоняем под d.H
            float hAvg = (rt0 + rt1) * 0.5f * 0.8f;
            float rise = hAvg * 1.25f;
            float natural = t0 + rt0 * 0.8f * 0.7f + turns * rise + rt1 * 2.2f;
            float f = Mathf.Clamp(d.H * 1.4f / Mathf.Max(1e-5f, natural), 0.7f, 1.9f);
            for (int i = 0; i <= N; i++)
            {
                float s = i / (float)N;
                float phi = ph + s * turns * Mathf.PI * 2f;
                float rc = Mathf.Lerp(0.66f, 0.05f, Mathf.Pow(s, 0.85f));
                float tip = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s - 0.86f) / 0.14f));
                rc *= 1f - tip * 0.9f;
                float rt = Mathf.Lerp(rt0, rt1, s) * (1f + 0.13f * Mathf.Sin(s * 38f + ph * 2f));          // наплывы-«капли» вдоль ленты
                rt *= Mathf.Lerp(0.65f, 1f, Mathf.Clamp01(s / 0.06f));                                     // начало прячется в подложку
                rt *= 1f - 0.7f * tip;                                                                     // кончик — острая пика
                float h = rt * 0.8f * f;
                float yc = t0 + h * 0.7f + s * turns * rise * f + rt1 * 2.2f * f * tip * tip;
                if (spoil > 0.001f) yc += (Mathf.PerlinNoise(s * 9f + ox, oz) - 0.5f) * rt * 0.5f * spoil;
                pts.Add(new Vector3(Mathf.Cos(phi) * rc * d.Rx, yc, Mathf.Sin(phi) * rc * d.Rz));
                ws.Add(rt * 1.18f);
                hs.Add(h);
                tones.Add(Mathf.Lerp(0.9f, 1.07f, s));                                                     // выше — светлее
            }
            Tube(pts, ws, hs, tones, col, true, true, 0.95f);
            if (curl > 1.5f)                                              // v23.7: пика — сам кончик ленты (крючок убран)
            {
                Vector3 top = pts[N];
                Vector3 dir = new Vector3(Mathf.Cos(ph + turns * Mathf.PI * 2f + 1.6f), 0f, Mathf.Sin(ph + turns * Mathf.PI * 2f + 1.6f));
                float rcurl = rt1 * 1.3f * curl;
                Curl(top + Vector3.down * (rcurl * 0.6f), dir, rcurl, col);
            }
        }

        /// <summary>
        /// v23.7: валик пасты через край полотна (side 0..3 — сторона): провисает между концами,
        /// в одном месте — тяжёлая круглая капля, она висит, но не отрывается.
        /// </summary>
        private static void Lip(Dims d, float edge, float t0, Color col, int side, System.Random rnd)
        {
            const int N = 22;
            float th = side * Mathf.PI * 0.5f;
            Vector3 dir = new Vector3(Mathf.Cos(th), 0f, Mathf.Sin(th));
            Vector3 tdir = new Vector3(-Mathf.Sin(th), 0f, Mathf.Cos(th));
            bool alongX = side % 2 == 0;
            float ex = (alongX ? d.Rx : d.Rz) * edge;
            float L = (alongX ? d.Rz : d.Rx) * edge * Mathf.Lerp(0.55f, 0.75f, (float)rnd.NextDouble());
            float R = Mathf.Min(d.Rx, d.Rz);
            float rt = R * Mathf.Lerp(0.14f, 0.18f, (float)rnd.NextDouble());
            float u0 = Mathf.Lerp(-0.35f, 0.35f, (float)rnd.NextDouble());
            float off = Mathf.Lerp(-0.15f, 0.15f, (float)rnd.NextDouble()) * L;
            var pts = new List<Vector3>(N + 1);
            var ws = new List<float>(N + 1);
            var hs = new List<float>(N + 1);
            var tones = new List<float>(N + 1);
            for (int i = 0; i <= N; i++)
            {
                float u = Mathf.Lerp(-1f, 1f, i / (float)N);
                float db = Mathf.Exp(-((u - u0) / 0.2f) * ((u - u0) / 0.2f));                 // капля
                float sag = rt * 1.05f * (1f - u * u);
                float y = t0 * 0.55f - sag - db * rt * 1.5f;
                float r = rt * Mathf.Lerp(0.55f, 1f, 1f - u * u) * (1f + 0.5f * db);
                float tuck = u * u * u * u;                                              // концы уходят в пасту на полотне
                pts.Add(dir * (ex + rt * 0.2f + db * rt * 0.25f - tuck * rt * 1.1f) + tdir * (u * L + off) + Vector3.up * (y + tuck * rt * 0.3f));
                ws.Add(r);
                hs.Add(r * (1f + 0.15f * db));
                tones.Add(0.96f);
            }
            Tube(pts, ws, hs, tones, col, true, true, 0.8f);
        }

        /// <summary>
        /// v23.7: труба по точкам (сечение — овал: ширина w, высота h); низ сплющен — лежит на том, что под ним.
        /// Цвет: тон по длине; гребень (верх сечения) — в альфу цвета, шейдер кладёт туда полоску глянца.
        /// </summary>
        private static void Tube(List<Vector3> pts, List<float> ws, List<float> hs, List<float> tones, Color col, bool capStart, bool capEnd, float crestK)
        {
            const int A = 14;
            int n = pts.Count;
            if (n < 2) return;
            int start = V.Count;
            Vector3 firstTan = Vector3.forward, lastTan = Vector3.forward;
            for (int i = 0; i < n; i++)
            {
                Vector3 tan = (pts[Mathf.Min(n - 1, i + 1)] - pts[Mathf.Max(0, i - 1)]).normalized;
                if (tan.sqrMagnitude < 1e-8f) tan = Vector3.forward;
                if (i == 0) firstTan = tan;
                lastTan = tan;
                Vector3 side = Vector3.Cross(Vector3.up, tan);
                if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
                side.Normalize();
                Vector3 nrm = Vector3.Cross(side, tan).normalized;
                float upSign = Vector3.Dot(nrm, Vector3.up) >= 0f ? 1f : -1f;
                Vector3 upish = (Vector3.up + side * 0.25f).normalized;
                for (int a = 0; a < A; a++)
                {
                    float an = a / (float)A * Mathf.PI * 2f;
                    float cs = Mathf.Cos(an), sn = Mathf.Sin(an);
                    float vert = sn * upSign;                                       // +1 верх сечения, −1 низ
                    float hk = vert < 0f ? 0.6f : 1f;                               // низ сплющен
                    Vector3 o = side * (cs * ws[i]) + nrm * (sn * hs[i] * hk);
                    V.Add(pts[i] + o);
                    float crest = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Vector3.Dot(o.normalized, upish) - 0.66f) / 0.3f)) * crestK;
                    float tn = tones[i] * (vert < 0f ? 0.9f : 1f);
                    C.Add(new Color(Mathf.Min(1f, col.r * tn), Mathf.Min(1f, col.g * tn), Mathf.Min(1f, col.b * tn), 1f - crest));
                }
            }
            for (int i = 0; i < n - 1; i++)
                for (int a = 0; a < A; a++)
                {
                    int a1 = (a + 1) % A;
                    int i0 = start + i * A + a, i1 = start + i * A + a1, j0 = i0 + A, j1 = i1 + A;
                    T.Add(i0); T.Add(j0); T.Add(i1);
                    T.Add(i1); T.Add(j0); T.Add(j1);
                }
            Color32 cEnd = new Color(col.r, col.g, col.b, 1f);
            if (capEnd)
            {
                int tip = V.Count;
                V.Add(pts[n - 1] + lastTan * (ws[n - 1] * 0.6f));
                C.Add(cEnd);
                int last = start + (n - 1) * A;
                for (int a = 0; a < A; a++) { T.Add(last + a); T.Add(tip); T.Add(last + (a + 1) % A); }
            }
            if (capStart)
            {
                int tip = V.Count;
                V.Add(pts[0] - firstTan * (ws[0] * 0.6f));
                C.Add(cEnd);
                for (int a = 0; a < A; a++) { T.Add(start + (a + 1) % A); T.Add(tip); T.Add(start + a); }
            }
        }

        // ───────────────────────── СЕТКА ─────────────────────────

        private delegate float HeightFn(float x, float z, float rho, float theta);
        private delegate float RimFn(float theta);

        /// <summary>Полярная сетка основания (кольца × сектора), высота — из функции; у тары — юбка до низа.</summary>
        private static void Grid(Dims d, HeightFn hf, RimFn rim, Color col, bool ao)
        {
            int start = V.Count;
            Centres.Add(start);
            bool skirt = d.Edge > 0.001f;
            float n = Mathf.Max(2f, d.RectN);
            float maxH = Mathf.Max(1e-4f, d.H);
            for (int i = 0; i <= Rings; i++)
            {
                float rho = i / (float)Rings;
                for (int j = 0; j < Segs; j++)
                {
                    float th = j / (float)Segs * Mathf.PI * 2f;
                    float c = Mathf.Cos(th), s = Mathf.Sin(th);
                    float rb = 1f / Mathf.Pow(Mathf.Pow(Mathf.Abs(c), n) + Mathf.Pow(Mathf.Abs(s), n), 1f / n);
                    float k = rho * rb * rim(th);
                    float x = d.Rx * k * c, z = d.Rz * k * s;
                    float y = hf(x, z, rho, th);
                    V.Add(new Vector3(x, y, z));
                    float shade = ao ? Mathf.Lerp(0.8f, 1f, Mathf.Clamp01(y / maxH * 3f)) : 1f;
                    Color vc = new Color(col.r * shade, col.g * shade, col.b * shade, 1f);
                    if (SpoilK > 0.001f) vc = SpoilColor(vc, x, z, SpoilK);
                    if (GridTint != null) vc = GridTint(x, z, rho, th, vc);
                    C.Add(vc);
                }
            }
            for (int i = 0; i < Rings; i++)
                for (int j = 0; j < Segs; j++)
                {
                    int j1 = (j + 1) % Segs;
                    int a = start + i * Segs + j, b = start + i * Segs + j1, c2 = a + Segs, d2 = b + Segs;
                    T.Add(a); T.Add(b); T.Add(c2);
                    T.Add(b); T.Add(d2); T.Add(c2);
                }
            if (!skirt) return;
            int outer = start + Rings * Segs, low = V.Count;
            for (int j = 0; j < Segs; j++)
            {
                Vector3 p = V[outer + j];
                V.Add(new Vector3(p.x * 1.01f, 0f, p.z * 1.01f));
                C.Add(C[outer + j]);
            }
            for (int j = 0; j < Segs; j++)
            {
                int j1 = (j + 1) % Segs;
                int a = outer + j, b = outer + j1, c2 = low + j, d2 = low + j1;
                T.Add(a); T.Add(b); T.Add(c2);
                T.Add(b); T.Add(d2); T.Add(c2);
            }
        }

        /// <summary>Глыбы: ближайшая «клетка» (9 соседей), у каждой своя высота и радиус. −0.35..0.65.</summary>
        public static float Chunks(float u, float v)
        {
            int iu = Mathf.FloorToInt(u), iv = Mathf.FloorToInt(v);
            float best = 0f;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = iu + dx, cy = iv + dy;
                    float px = cx + 0.1f + 0.8f * Hash01(cx, cy, 1), py = cy + 0.1f + 0.8f * Hash01(cx, cy, 2);
                    float rad = 0.35f + 0.5f * Hash01(cx, cy, 3);
                    float dd = Mathf.Sqrt((u - px) * (u - px) + (v - py) * (v - py));
                    float k = Mathf.Clamp01(1f - dd / rad);
                    float hk = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k / 0.55f)) * (0.3f + 0.7f * Hash01(cx, cy, 4));
                    if (hk > best) best = hk;
                }
            return best - 0.35f;
        }

        public static float Hash01(int x, int y, int k)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(k * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        private static void Begin() { V.Clear(); C.Clear(); T.Clear(); Centres.Clear(); DripI.Clear(); DripT.Clear(); DripR.Clear(); _dripMaxL = 0f; GridTint = null; }

        private static void Finish(Mesh m)
        {
            m.Clear();
            m.SetVertices(V);
            m.SetColors(C);
            // v23.6: UV2 — x: высота точки (0 низ … 1 макушка), y: радиус кучки (м меша). Шейдер CatCooks/Anime Heap
            // по ним рисует узор одинаково на любой кучке (крошка на мастерке и большая горка) и темнит низ у пола
            float maxY = 1e-5f, rad = 1e-5f;
            int firstDrip = DripI.Count > 0 ? DripI[0] : int.MaxValue;
            for (int i = 0; i < V.Count; i++)
            {
                Vector3 p = V[i];
                if (i >= firstDrip && p.y < 0f) continue;                          // висящие капли не считаем
                if (p.y > maxY) maxY = p.y;
                float rr = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z));
                if (rr > rad && i < firstDrip) rad = rr;
            }
            U1.Clear();
            for (int i = 0; i < V.Count; i++) U1.Add(new Vector2(Mathf.Clamp01(V[i].y / maxY), rad));
            if (DripI.Count > 0)
            {
                U2.Clear();
                for (int i = 0; i < V.Count; i++) U2.Add(Vector2.zero);
                for (int k = 0; k < DripI.Count; k++)
                {
                    U1[DripI[k]] = new Vector2(-(DripT[k] + 0.002f), rad);
                    U2[DripI[k]] = DripR[k];
                }
                m.SetUVs(2, U2);
            }
            m.SetUVs(1, U1);
            m.SetTriangles(T, 0);
            m.RecalculateNormals();
            // макушка: все вершины центра — одна точка, нормаль усредняем (без «звёздочки»)
            if (Centres.Count > 0)
            {
                Vector3[] nr = m.normals;
                foreach (int st in Centres)
                {
                    Vector3 sum = Vector3.zero;
                    for (int j = 0; j < Segs; j++) sum += nr[st + j];
                    sum = sum.sqrMagnitude > 1e-8f ? sum.normalized : Vector3.up;
                    for (int j = 0; j < Segs; j++) nr[st + j] = sum;
                }
                m.normals = nr;
            }
            m.RecalculateBounds();
            if (DripI.Count > 0)                                                    // капли качаются и висят к полу — шире границы
            {
                Bounds b = m.bounds;
                b.Expand(_dripMaxL * 2.5f);
                m.bounds = b;
            }
        }
    }
}
