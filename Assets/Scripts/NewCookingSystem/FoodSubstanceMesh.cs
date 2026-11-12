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
            Rings = 16; Segs = 44;
            var rnd = new System.Random(seed);
            float ph1 = (float)rnd.NextDouble() * 6.28f, ph2 = (float)rnd.NextDouble() * 6.28f;
            float lean = (float)rnd.NextDouble() * 6.28f;
            float ox = (float)rnd.NextDouble() * 50f, oz = (float)rnd.NextDouble() * 50f;
            float R = Mathf.Max(d.Rx, d.Rz);
            bool mound = d.Edge <= 0.001f;
            float wob = mound ? wonky * 0.14f : wonky * 0.035f;

            Grid(d, (x, z, rho, th) =>
            {
                float prof = mound ? Mathf.Pow(Mathf.Max(0f, 1f - rho * rho), 0.8f)
                                   : d.Edge + (1f - d.Edge) * (1f - rho * rho);
                float y = d.H * prof;
                // наклон и кривизна (горка «съехала» набок)
                y *= 1f + wonky * 0.22f * (x / R * Mathf.Cos(lean) + z / R * Mathf.Sin(lean));
                float inner = 1f - Mathf.Pow(rho, 5f);
                // крупные бугры и глыбы
                float n1 = Mathf.PerlinNoise(x / (R * 0.38f) + ox, z / (R * 0.38f) + oz) - 0.5f;
                y += n1 * d.H * 0.3f * (0.4f + wonky) * inner;
                y += Chunks(x / (R * 0.2f) + ox, z / (R * 0.2f) + oz) * d.H * 0.16f * inner;
                return Mathf.Max(0f, y);
            }, th => 1f + wob * (Mathf.Sin(2f * th + ph1) * 0.6f + Mathf.Sin(3f * th + ph2) * 0.4f), col, true);
            Finish(m);
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
        /// v18.8: КОМ МАСЛА (пол, тара) — «фермерское», рваное: комки разной величины слеплены в кучку,
        /// между ними трещинки, край неровный и рваный, без ровных стенок и «кубиков».
        /// </summary>
        public static void Butter(Mesh m, Dims d, float curl, int smears, float smearDepth, Color col, int seed)
        {
            Begin();
            Rings = 32; Segs = 80;
            var rnd = new System.Random(seed);
            bool mound = d.Edge <= 0.001f;
            float ox = (float)rnd.NextDouble() * 50f, oz = (float)rnd.NextDouble() * 50f;
            float lump = 1.6f + Mathf.Clamp(smears, 0, 9) * 0.12f;          // больше «мазков» — мельче комки
            float rough = Mathf.Clamp(smearDepth, 0f, 2f);
            HeightFn hf = (x, z, rho, th) =>
            {
                float u = x / Mathf.Max(1e-4f, d.Rx), v = z / Mathf.Max(1e-4f, d.Rz);
                float h = FarmButter(u, v, rho, ox, oz, lump);
                h = Mathf.Lerp(Mathf.Pow(Mathf.Max(0f, 1f - rho * rho), 1.2f) * 0.6f, h, Mathf.Clamp01(rough));
                float y = mound ? d.H * h * 1.15f : d.H * (d.Edge + (1f - d.Edge) * h * 1.1f);
                y *= 1f - Mathf.Pow(rho, 8f);
                return mound && y < 1e-4f ? -0.002f : y;                  // ровное поле вокруг — под пол (без мерцания)
            };
            Grid(d, hf, RaggedRim(rnd, mound ? 0.16f : 0.04f), col, true);
            Finish(m);
        }

        /// <summary>
        /// v18.8: МАСЛО НА ПОЛОТНЕ мастерка/лопаты — та же рваная фермерская кучка, по форме полотна,
        /// пониже (лежит, а не стоит столбиком).
        /// </summary>
        public static void ButterSmear(Mesh m, Dims d, float curl, Color col, int seed)
        {
            Begin();
            Rings = 28; Segs = 72;
            var rnd = new System.Random(seed);
            float ox = (float)rnd.NextDouble() * 50f, oz = (float)rnd.NextDouble() * 50f;
            HeightFn hf = (x, z, rho, th) =>
            {
                float u = x / Mathf.Max(1e-4f, d.Rx), v = z / Mathf.Max(1e-4f, d.Rz);
                float h = FarmButter(u, v, rho, ox, oz, 1.9f);
                return Mathf.Max(0f, d.H * h * 1.1f * (1f - Mathf.Pow(rho, 8f)));
            };
            Grid(d, hf, RaggedRim(rnd, 0.13f), col, true);
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
                y += (Mathf.PerlinNoise(x / (R * 0.4f) + ox, z / (R * 0.4f) + oz) - 0.5f) * d.H * 0.15f * (1f - Mathf.Pow(rho, 4f));
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
                float sz = cs * (0.55f + (float)rnd.NextDouble() * 0.8f);
                Quaternion q = Quaternion.Euler((float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f);
                float tint = 0.95f + (float)rnd.NextDouble() * 0.2f;
                Color cc = new Color(Mathf.Min(1f, col.r * tint), Mathf.Min(1f, col.g * tint), Mathf.Min(1f, col.b * tint), 1f);
                if (SpoilK > 0.001f) cc = SpoilColor(cc, x, z, SpoilK);
                Cube(new Vector3(x, y + sz * 0.15f, z), q, sz, cc);
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
            Rings = 28; Segs = 72;
            var rnd = new System.Random(seed);
            bool mound = d.Edge <= 0.001f;
            float ph = (float)rnd.NextDouble() * 6.28f;
            float ox = (float)rnd.NextDouble() * 50f, oz = (float)rnd.NextDouble() * 50f;
            float spoil = SpoilK;
            HeightFn hf = (x, z, rho, th) =>
            {
                float u = x / Mathf.Max(1e-4f, d.Rx), v = z / Mathf.Max(1e-4f, d.Rz);
                float a = Mathf.Atan2(v, u);
                float dome = Mathf.Pow(Mathf.Max(0f, 1f - rho * rho), 0.75f) * 0.72f;
                float sw = Mathf.Sin(rho * Mathf.PI * 4.2f - a + ph);                 // спираль
                float ridge = Mathf.Pow(0.5f + 0.5f * sw, 3f) * 0.16f * (1f - Mathf.Pow(rho, 3f));
                float peak = 0.22f * Mathf.Exp(-(rho * rho) / 0.03f);
                float h = dome + ridge + peak;
                if (spoil > 0.001f)                                                   // свернулась: комочки
                    h += (Mathf.PerlinNoise(u * 7f + ox, v * 7f + oz) - 0.45f) * 0.14f * spoil * (1f - rho);
                float y = mound ? d.H * h : d.H * (d.Edge + (1f - d.Edge) * h);
                return Mathf.Max(0f, y * (1f - Mathf.Pow(rho, 8f)));
            };
            float p1 = (float)rnd.NextDouble() * 6.28f;
            Grid(d, hf, th => 1f + 0.04f * Mathf.Sin(5f * th + p1) + 0.02f * Mathf.Sin(9f * th + p1 * 2f), col, true);
            if (curl > 0.01f)
            {
                float baseY = hf(0f, 0f, 0f, 0f);
                float ca = (float)rnd.NextDouble() * 6.28f;
                float rc = Mathf.Min(Mathf.Max(d.Rx, d.Rz) * 0.22f, d.H * 0.45f) * curl;
                Curl(new Vector3(0f, baseY - rc * 0.35f, 0f), new Vector3(Mathf.Cos(ca), 0f, Mathf.Sin(ca)), rc, col);
            }
            Finish(m);
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

        private static void Begin() { V.Clear(); C.Clear(); T.Clear(); Centres.Clear(); }

        private static void Finish(Mesh m)
        {
            m.Clear();
            m.SetVertices(V);
            m.SetColors(C);
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
        }
    }
}
