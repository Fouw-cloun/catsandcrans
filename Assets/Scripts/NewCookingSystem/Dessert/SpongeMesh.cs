using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// МЕШИ БИСКВИТА И МОРОЖЕНОГО — строятся кодом, в рантайме и в редакторе.
    ///
    /// Пласт = сетка клеток nx × nz, некоторые клетки уже отпилены. Меш — ОДИН на весь пласт:
    ///   • верх — общая гладкая «подушка» корочки через все клетки (швов между кубиками нет);
    ///   • бока — только по внешнему контуру оставшихся клеток:
    ///       край пласта — тёмная корочка сверху и мякиш ниже,
    ///       свежий срез (рядом отпиленная клетка) — светлый пористый мякиш с тонкой корочкой.
    /// Кусок = та же сетка 1×1: у каждой стороны своя — корочка или срез (маска).
    ///
    /// Цвет — в вершинах (шейдер «CatCooks/Food Heap»), альфа = сколько пор текстуры.
    /// Обход граней: передняя сторона — по часовой (как у Unity Quad), проверено.
    /// </summary>
    public static class SpongeMesh
    {
        // ── цвета шоколадного бисквита ──
        // v23.41: тёмный шоколадный бисквит (как на фото): гладкая тёмная корочка, мякиш темнее и пористее (альфа = поры)
        public static readonly Color TopCrust = new Color(0.20f, 0.11f, 0.07f, 0.35f);
        public static readonly Color SideCrust = new Color(0.18f, 0.10f, 0.06f, 0.45f);
        public static readonly Color SideCrumb = new Color(0.27f, 0.15f, 0.10f, 1f);
        public static readonly Color CutCrumb = new Color(0.31f, 0.18f, 0.12f, 1f);
        public static readonly Color CutCrust = new Color(0.19f, 0.10f, 0.07f, 0.6f);
        public static readonly Color Bottom = new Color(0.22f, 0.12f, 0.08f, 0.8f);

        /// <summary>Маска сторон куска: какая сторона — корочка (край пласта), остальные — срез.</summary>
        public const byte XNeg = 1, XPos = 2, ZNeg = 4, ZPos = 8, AllCrust = 15;

        private static readonly List<Vector3> V = new List<Vector3>();
        private static readonly List<Color> C = new List<Color>();
        private static readonly List<int> T = new List<int>();
        private static readonly List<Vector2> U = new List<Vector2>();   // v23.43: uv0 = (сколько пор, 7 — метка бисквита для шейдера)

        // v23.43: сколько пор (шейдер «CatCooks/Food Heap» рисует их сам, в любом масштабе)
        private const float PoreTop = 0.12f, PoreSideCrust = 0.15f, PoreCrumb = 1f, PoreBottom = 0.15f;   // v23.44: поры — в мякише; корочки почти гладкие

        private struct Grid
        {
            public int Nx, Nz, Sub;
            public Vector3 Cell;
            public bool[] Present;          // null — все на месте
            public byte OuterCrust;         // какие края сетки — корочка
            public float Bulge;             // подушка верха, доля высоты
            public float Wobble;            // неровность верха, доля высоты
            public float Seed;
            public bool Pillow;             // v23.41: две «подушки» с пережимом посередине, скруглённые плечи, бока чуть надуты
            public float Round;             // v23.43: радиус скругления верхних краёв корочки (м): верх плавно переходит в бока, одним куском
            public float Lx => Nx * Cell.x;
            public float Lz => Nz * Cell.z;
            public bool Has(int ix, int iz) => ix >= 0 && iz >= 0 && ix < Nx && iz < Nz && (Present == null || Present[ix + iz * Nx]);
            public bool Inside(int ix, int iz) => ix >= 0 && iz >= 0 && ix < Nx && iz < Nz;
        }

        /// <summary>Высота верха в точке (локально, от центра пласта). Одна функция для верха и бортиков — без щелей.</summary>
        private static float TopY(ref Grid g, float x, float z)
        {
            float u = x / Mathf.Max(1e-4f, g.Lx), w = z / Mathf.Max(1e-4f, g.Lz);
            float b = Mathf.Clamp01(1f - 4f * u * u) * Mathf.Clamp01(1f - 4f * w * w);
            float k = 3.1f / Mathf.Max(0.01f, Mathf.Min(g.Cell.x, g.Cell.z));
            float n = Mathf.PerlinNoise(x * k + g.Seed, z * k + g.Seed * 0.7f) - 0.5f;
            // v23.37: трещинки корочки — неглубокие бороздки по куполу (как у настоящего бисквита)
            float cr = CrackK(ref g, x, z);
            if (g.Pillow)
            {
                // v23.41: ДВЕ ПОДУШКИ с пережимом посередине (как на фото: верх продавлен поперёк), скруглённые плечи
                bool alongX = g.Lx >= g.Lz;
                float a = alongX ? u : w, bb = alongX ? w : u;
                float hump = Mathf.Clamp01(1f - Sq((Mathf.Abs(a) - 0.25f) / 0.3f));
                float across = Mathf.Sqrt(Mathf.Clamp01(1f - 4f * bb * bb));
                float groove = Mathf.Exp(-Sq(a / 0.04f));
                float shoulder = 1f - S01((0.5f - Mathf.Abs(a)) / 0.08f) * S01((0.5f - Mathf.Abs(bb)) / 0.08f);
                // v23.43: «пузырчатый» рельеф корочки — мягкие бугорки разного размера
                float n2 = Mathf.PerlinNoise(x * k * 2.6f + 31.7f, z * k * 2.6f + g.Seed * 1.3f) - 0.5f;
                float n3 = Mathf.PerlinNoise(x * k * 6.1f + 7.9f, z * k * 6.1f + 13.1f) - 0.5f;
                float bub = (n2 * 0.6f + n3 * 0.4f) * across;
                return g.Cell.y * (0.5f + g.Bulge * (0.55f + 0.45f * hump) * across - 0.09f * groove * across
                                   - 0.06f * shoulder + g.Wobble * n * 2f + 0.022f * bub - 0.012f * cr * Mathf.Sqrt(b));
            }
            return g.Cell.y * (0.5f + g.Bulge * b + g.Wobble * n * 2f - 0.03f * cr * Mathf.Sqrt(b));
        }

        private static float Sq(float x) => x * x;
        private static float S01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>v23.41: «живая» форма — бока чуть надуты, посередине (где пережат верх) бисквит чуть уже.</summary>
        private static void PillowDeform(ref Grid g, float half)
        {
            bool alongX = g.Lx >= g.Lz;
            float L = Mathf.Max(1e-4f, alongX ? g.Lx : g.Lz);
            float H = Mathf.Max(1e-4f, g.Cell.y);
            for (int i = 0; i < V.Count; i++)
            {
                Vector3 p = V[i];
                float f = Mathf.Clamp01((p.y + half) / H);
                float a = (alongX ? p.x : p.z) / L;
                float puff = 0.035f * Mathf.Sin(Mathf.PI * f);
                float pinch = 0.06f * Mathf.Exp(-Sq(a / 0.1f)) * (0.3f + 0.7f * f);
                float kShort = 1f + puff - pinch, kLong = 1f + puff * 0.5f;
                if (alongX) { p.x *= kLong; p.z *= kShort; } else { p.z *= kLong; p.x *= kShort; }
                V[i] = p;
            }
        }

        /// <summary>v23.37: трещина корочки 0..1 (узкие ломаные линии по куполу).</summary>
        private static float CrackK(ref Grid g, float x, float z)
        {
            float kc = 1.3f / Mathf.Max(0.01f, Mathf.Min(g.Cell.x, g.Cell.z));
            float a = Mathf.PerlinNoise(x * kc + g.Seed * 1.7f + 11.3f, z * kc + 4.1f);
            float b = Mathf.PerlinNoise(x * kc * 1.9f + 3.3f, z * kc * 1.9f + g.Seed + 17.7f);
            float r1 = Mathf.Pow(1f - Mathf.Abs(a * 2f - 1f), 14f);
            float r2 = Mathf.Pow(1f - Mathf.Abs(b * 2f - 1f), 18f) * 0.7f;
            return Mathf.Clamp01(Mathf.Max(r1, r2));
        }

        /// <summary>Центр клетки (локально).</summary>
        public static Vector3 CellCenter(int nx, int nz, Vector3 cell, int ix, int iz)
            => new Vector3((ix + 0.5f) * cell.x - nx * cell.x * 0.5f, 0f, (iz + 0.5f) * cell.z - nz * cell.z * 0.5f);

        /// <summary>Пласт из оставшихся клеток. present[ix + iz*nx]; null — целый.</summary>
        public static Mesh BuildSlab(Mesh m, int nx, int nz, Vector3 cell, bool[] present, float bulge = 0.05f, int sub = 2, float seed = 3.7f)
        {
            var g = new Grid { Nx = nx, Nz = nz, Sub = Mathf.Clamp(sub, 1, 6), Cell = cell, Present = present, OuterCrust = AllCrust, Bulge = bulge, Wobble = 0.012f, Seed = seed, Pillow = false,   // v23.45: один сегмент, как кусок (без подушек и пережима)
                               Round = RoundFor(cell, 1f) };
            return Build(m, ref g, "SpongeSlab (auto)");
        }

        /// <summary>
        /// v23.37: бисквит В ОПАЛУБКЕ (вместо коробки Fill): размер size (низ на y = 0, центр по x/z), купол dome (доля высоты),
        /// трещинки корочки. bake01: 0 — сырое тесто (ровное, цвета batter), 1 — корочка.
        /// </summary>
        public static Mesh BuildCake(Mesh m, Vector3 size, float dome, float bake01, Color batter, float seed = 5.1f)
            => BuildCake(m, size, dome, bake01, batter, 0, 0, null, seed);

        /// <summary>v23.51: бисквит в опалубке, сетка gx × gz и какие кубики ещё на месте (режут пилой прямо в опалубке).</summary>
        public static Mesh BuildCake(Mesh m, Vector3 size, float dome, float bake01, Color batter, int gx, int gz, bool[] present, float seed = 5.1f)
        {
            int nx, nz;
            if (gx > 0 && gz > 0) { nx = Mathf.Clamp(gx, 1, 30); nz = Mathf.Clamp(gz, 1, 30); }
            else
            {
                float cellXZ = Mathf.Max(0.02f, Mathf.Min(size.x, size.z) / 3f);
                nx = Mathf.Clamp(Mathf.RoundToInt(size.x / cellXZ), 1, 12); nz = Mathf.Clamp(Mathf.RoundToInt(size.z / cellXZ), 1, 12);
            }
            if (present != null && present.Length != nx * nz) present = null;
            var cell = new Vector3(size.x / nx, size.y, size.z / nz);
            float k = Mathf.Clamp01(bake01);
            var g = new Grid { Nx = nx, Nz = nz, Sub = 3, Cell = cell, Present = present, OuterCrust = AllCrust,
                               Bulge = dome * k, Wobble = 0.012f * k, Seed = seed, Pillow = false, Round = RoundFor(cell, 0.35f + 0.65f * k) };
            m = Build(m, ref g, "SpongeCake (auto)");
            // низ — на y = 0
            Vector3[] v = m.vertices;
            for (int i = 0; i < v.Length; i++) v[i].y += cell.y * 0.5f;
            m.vertices = v;
            // сырое тесто → корочка (и поры проступают, пока печётся)
            if (k < 0.999f)
            {
                Color[] c = m.colors;
                Color raw = new Color(batter.r, batter.g, batter.b, 0.15f);
                float kk = k * k * (3f - 2f * k);
                for (int i = 0; i < c.Length; i++) c[i] = Color.Lerp(raw, c[i], kk);
                m.colors = c;
                Vector2[] uv = m.uv;
                for (int i = 0; i < uv.Length; i++) uv[i].x *= 0.15f + 0.85f * kk;
                m.uv = uv;
            }
            m.RecalculateNormals();
            SmoothNormals(m);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Один кусок размером size (локально, по центру). crust — какие стороны корочка (XNeg|XPos|ZNeg|ZPos).</summary>
        public static Mesh BuildPiece(Mesh m, Vector3 size, byte crust, float seed = 1.3f)
        {
            var g = new Grid { Nx = 1, Nz = 1, Sub = 4, Cell = size, Present = null, OuterCrust = crust, Bulge = 0.06f, Wobble = 0.012f, Seed = seed,
                               Round = RoundFor(size, 1f) };   // v23.40: купол как у пласта; v23.43: скруглённые края корочки
            return Build(m, ref g, "SpongePiece (auto)");
        }

        private static Material _fallbackMat;

        /// <summary>
        /// v23.46: материал бисквита. Свой — если он на «CatCooks/Food Heap» (цвет в вершинах + поры).
        /// Иначе (не задан, другой шейдер) — общий авто-материал на «CatCooks/Food Heap» с тонкой обводкой и без бликов.
        /// </summary>
        public static Material PickMaterial(Material preferred)
        {
            if (preferred != null && preferred.shader != null && preferred.shader.name == "CatCooks/Food Heap") return preferred;
            if (_fallbackMat != null) return _fallbackMat;
            Shader sh = Shader.Find("CatCooks/Food Heap");
            if (sh == null) return preferred;
            _fallbackMat = new Material(sh) { name = "Sponge (auto)", hideFlags = HideFlags.DontSave };
            if (_fallbackMat.HasProperty("_BaseColor")) _fallbackMat.SetColor("_BaseColor", Color.white);
            if (_fallbackMat.HasProperty("_OutlineWidth")) _fallbackMat.SetFloat("_OutlineWidth", 0.003f);
            if (_fallbackMat.HasProperty("_Flat")) _fallbackMat.SetFloat("_Flat", 0.4f);
            if (_fallbackMat.HasProperty("_GlintStrength")) _fallbackMat.SetFloat("_GlintStrength", 0.06f);
            if (_fallbackMat.HasProperty("_TexDetail")) _fallbackMat.SetFloat("_TexDetail", 0.3f);
            return _fallbackMat;
        }

        private static readonly Dictionary<long, Mesh> _pieceCache = new Dictionary<long, Mesh>();

        /// <summary>Общий меш куска (кэш по размеру и маске) — все кубики одного пласта делят 16 мешей.</summary>
        public static Mesh SharedPiece(Vector3 size, byte crust)
        {
            long key = ((long)Mathf.RoundToInt(size.x * 1000f) << 40) ^ ((long)Mathf.RoundToInt(size.y * 1000f) << 20) ^ Mathf.RoundToInt(size.z * 1000f) ^ ((long)crust << 58);
            if (_pieceCache.TryGetValue(key, out Mesh m) && m != null) return m;
            m = BuildPiece(null, size, crust);
            m.hideFlags = HideFlags.DontSave;
            _pieceCache[key] = m;
            return m;
        }

        private static Mesh Build(Mesh m, ref Grid g, string name)
        {
            V.Clear(); C.Clear(); T.Clear(); U.Clear();
            // v23.43: верх мельче, чтобы скругление края корочки было плавным (2–3 ряда на радиус), но не больше ~6000 вершин
            if (g.Round > 1e-4f)
            {
                int want = Mathf.CeilToInt(Mathf.Min(g.Cell.x, g.Cell.z) / (g.Round * 0.55f));
                int cap = Mathf.Max(1, Mathf.FloorToInt(Mathf.Sqrt(6000f / Mathf.Max(1, g.Nx * g.Nz))));
                g.Sub = Mathf.Clamp(Mathf.Max(g.Sub, Mathf.Min(want, 10)), 1, Mathf.Max(g.Sub, cap));
            }
            int S = g.Sub;
            int vx = g.Nx * S + 1, vz = g.Nz * S + 1;
            float x0 = -g.Lx * 0.5f, z0 = -g.Lz * 0.5f;
            float sx = g.Cell.x / S, sz = g.Cell.z / S;
            float half = g.Cell.y * 0.5f;

            // ── верх: общая сетка вершин, треугольники только над оставшимися клетками ──
            int top0 = V.Count;
            for (int j = 0; j < vz; j++)
                for (int i = 0; i < vx; i++)
                {
                    float x = x0 + i * sx, z = z0 + j * sz;
                    V.Add(new Vector3(x, TopY(ref g, x, z), z));
                    C.Add(TopColor(ref g, x, z));
                    U.Add(new Vector2(PoreTop, 7f));
                }
            for (int iz = 0; iz < g.Nz; iz++)
                for (int ix = 0; ix < g.Nx; ix++)
                {
                    if (!g.Has(ix, iz)) continue;
                    for (int b = 0; b < S; b++)
                        for (int a = 0; a < S; a++)
                        {
                            int i = ix * S + a, j = iz * S + b;
                            int p00 = top0 + i + j * vx;      // (x, z)
                            int pz = p00 + vx;                // (x, z+1)
                            int px = p00 + 1;                 // (x+1, z)
                            int pxz = pz + 1;
                            // верх (N=+Y): U=+Z, V=+X
                            T.Add(p00); T.Add(pz); T.Add(px);
                            T.Add(pz); T.Add(pxz); T.Add(px);
                        }
                    // низ (N=−Y): U=+X, V=+Z — одной плиткой на клетку
                    Vector3 cc = CellCenter(g.Nx, g.Nz, g.Cell, ix, iz);
                    float hx = g.Cell.x * 0.5f, hz = g.Cell.z * 0.5f;
                    Quad(new Vector3(cc.x - hx, -half, cc.z - hz), new Vector3(cc.x + hx, -half, cc.z - hz),
                         new Vector3(cc.x + hx, -half, cc.z + hz), new Vector3(cc.x - hx, -half, cc.z + hz), Bottom, Bottom);

                    // бока: где соседа нет
                    Side(ref g, ix, iz, -1, 0, x0, z0, sx, sz, half);
                    Side(ref g, ix, iz, 1, 0, x0, z0, sx, sz, half);
                    Side(ref g, ix, iz, 0, -1, x0, z0, sx, sz, half);
                    Side(ref g, ix, iz, 0, 1, x0, z0, sx, sz, half);
                }

            if (g.Round > 1e-4f) RoundEdges(ref g);
            if (g.Pillow) PillowDeform(ref g, half);
            if (m == null) m = new Mesh();
            m.Clear();
            m.name = name;
            m.indexFormat = V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(V);
            m.SetColors(C);
            m.SetUVs(0, U);
            m.SetTriangles(T, 0);
            m.RecalculateNormals();
            SmoothNormals(m);
            m.RecalculateBounds();
            return m;
        }

        // v23.43: больше рядов у верха — там скругление корочки
        private static readonly float[] RowF = { 0f, 0.07f, 0.13f, 0.3f, 0.55f, 0.72f, 0.83f, 0.89f, 0.94f, 0.975f, 1f };   // v23.44: ряды по границам корочек

        /// <summary>Бортик клетки в сторону (dx,dz): полоса из Sub отрезков вдоль края × 4 ряда по высоте.</summary>
        private static void Side(ref Grid g, int ix, int iz, int dx, int dz, float x0, float z0, float sx, float sz, float half)
        {
            int nix = ix + dx, niz = iz + dz;
            if (g.Has(nix, niz)) return;
            bool crust;
            if (g.Inside(nix, niz)) crust = false;            // сосед отпилен — свежий срез
            else
            {
                byte bit = dx < 0 ? XNeg : dx > 0 ? XPos : dz < 0 ? ZNeg : ZPos;
                crust = (g.OuterCrust & bit) != 0;
            }
            int S = g.Sub;
            Vector3 N = new Vector3(dx, 0f, dz);
            Vector3 e = dx != 0 ? Vector3.forward : Vector3.right;          // вдоль края
            // начало края и шаг
            float ex = dx > 0 ? x0 + (ix + 1) * g.Cell.x : x0 + ix * g.Cell.x;
            float ez = dz > 0 ? z0 + (iz + 1) * g.Cell.z : z0 + iz * g.Cell.z;
            float step = dx != 0 ? sz : sx;
            Vector3 start = dx != 0 ? new Vector3(ex, 0f, z0 + iz * g.Cell.z) : new Vector3(x0 + ix * g.Cell.x, 0f, ez);
            bool upFirst = Vector3.Dot(Vector3.Cross(Vector3.up, e), N) > 0f;   // U=вверх, V=вдоль — или наоборот

            int rows = RowF.Length;
            int b0 = V.Count;
            for (int k = 0; k <= S; k++)
            {
                Vector3 p = start + e * (step * k);
                float top = TopY(ref g, p.x, p.z);
                for (int r = 0; r < rows; r++)
                {
                    float f = RowF[r];
                    V.Add(new Vector3(p.x, Mathf.Lerp(-half, top, f), p.z));
                    C.Add(RowColor(ref g, crust, f, p));
                    U.Add(new Vector2(SandwichCrumb(f) > 0.5f ? PoreCrumb : PoreSideCrust, 7f));
                }
            }
            for (int k = 0; k < S; k++)
                for (int r = 0; r < rows - 1; r++)
                {
                    int p00 = b0 + k * rows + r;
                    int pUp = p00 + 1;
                    int pAl = p00 + rows;
                    int pBoth = pAl + 1;
                    if (upFirst)
                    {   // U = вверх, V = вдоль
                        T.Add(p00); T.Add(pUp); T.Add(pAl);
                        T.Add(pUp); T.Add(pBoth); T.Add(pAl);
                    }
                    else
                    {   // U = вдоль, V = вверх
                        T.Add(p00); T.Add(pAl); T.Add(pUp);
                        T.Add(pAl); T.Add(pBoth); T.Add(pUp);
                    }
                }
        }

        /// <summary>v23.43: цвет корочки сверху в точке. Один для верха и верхнего ряда боков — переход без шва.</summary>
        private static Color TopColor(ref Grid g, float x, float z)
        {
            Color c = TopCrust;
            float n = Mathf.PerlinNoise(x * 7.3f + g.Seed, z * 7.3f) - 0.5f;
            c.r *= 1f + n * 0.12f; c.g *= 1f + n * 0.12f; c.b *= 1f + n * 0.12f;
            // v23.37: макушка купола чуть светлее, к краю темнее (поджаристее), в трещинках светлый мякиш
            float uu = x / Mathf.Max(1e-4f, g.Lx), ww = z / Mathf.Max(1e-4f, g.Lz);
            float dome = Mathf.Clamp01(1f - 4f * uu * uu) * Mathf.Clamp01(1f - 4f * ww * ww);
            float tone = Mathf.Lerp(0.86f, 1.1f, dome);
            c.r *= tone; c.g *= tone; c.b *= tone;
            float cr = CrackK(ref g, x, z) * Mathf.Sqrt(dome);
            c = Color.Lerp(c, new Color(CutCrumb.r * 1.08f, CutCrumb.g * 1.08f, CutCrumb.b * 1.08f, 1f), cr * 0.3f);   // v23.45: трещинки еле заметные
            return c;
        }

        /// <summary>v23.44: «бутерброд» по высоте бока: 0 — корочка (низ/верх), 1 — мякиш посередине.</summary>
        private static float SandwichCrumb(float f)
        {
            if (f <= 0.07f || f >= 0.89f) return 0f;
            if (f >= 0.13f && f <= 0.83f) return 1f;
            return f < 0.5f ? (f - 0.07f) / 0.06f : (0.89f - f) / 0.06f;
        }

        /// <summary>
        /// v23.44: цвет бока — КАК БУТЕРБРОД: снизу тонкая тёмная корочка, посередине пористый мякиш, сверху корочка,
        /// которая через скруглённый край переходит в верх. Одинаково у края пласта и у свежего среза
        /// (у края мякиш чуть темнее — запёкся о форму).
        /// </summary>
        private static Color RowColor(ref Grid g, bool crust, float f, Vector3 p)
        {
            Color crumb = crust ? Color.Lerp(CutCrumb, SideCrumb, 0.35f) : CutCrumb;
            Color crustLow = new Color(Bottom.r * 0.95f, Bottom.g * 0.95f, Bottom.b * 0.95f, 0.5f);
            Color crustTop = SideCrust;
            Color c;
            if (f < 0.5f) c = Color.Lerp(crustLow, crumb, SandwichCrumb(f));
            else c = Color.Lerp(crustTop, crumb, SandwichCrumb(f));
            if (f > 0.89f) c = Color.Lerp(crustTop, TopColor(ref g, p.x, p.z), S01((f - 0.89f) / 0.11f));
            float n = Mathf.PerlinNoise(p.x * 9.1f + 2.3f, p.z * 9.1f + p.y * 5f) - 0.5f;
            c.r *= 1f + n * 0.1f; c.g *= 1f + n * 0.1f; c.b *= 1f + n * 0.1f;
            return c;
        }

        /// <summary>v23.43: радиус скругления края корочки: ~20% высоты, не больше трети клетки.</summary>
        private static float RoundFor(Vector3 cell, float k)
            => Mathf.Max(0f, k) * Mathf.Min(cell.y * 0.2f, Mathf.Min(cell.x, cell.z) * 0.3f);

        /// <summary>
        /// v23.43: СКРУГЛЕНИЕ верхних краёв корочки. Двигает вершины по их положению (а не по граням),
        /// поэтому верх, бока и срезы у края сдвигаются одинаково — щелей нет. Профиль — четверть круга радиуса g.Round;
        /// вертикальные углы пласта тоже скруглены. Только края-корочки (маска OuterCrust); свежий срез остаётся острым.
        /// </summary>
        private static void RoundEdges(ref Grid g)
        {
            float R = g.Round;
            float x0 = -g.Lx * 0.5f, x1 = g.Lx * 0.5f, z0 = -g.Lz * 0.5f, z1 = g.Lz * 0.5f;
            bool xn = (g.OuterCrust & XNeg) != 0, xp = (g.OuterCrust & XPos) != 0, zn = (g.OuterCrust & ZNeg) != 0, zp = (g.OuterCrust & ZPos) != 0;
            for (int i = 0; i < V.Count; i++)
            {
                Vector3 p = V[i];
                // насколько вершина в полосе скругления у края по x и по z (0 — дальше R от края), и куда «внутрь»
                float ax = 0f, sxn = 0f, az = 0f, szn = 0f;
                if (xn && p.x - x0 < R) { ax = R - (p.x - x0); sxn = 1f; }
                if (xp && x1 - p.x < R && R - (x1 - p.x) > ax) { ax = R - (x1 - p.x); sxn = -1f; }
                if (zn && p.z - z0 < R) { az = R - (p.z - z0); szn = 1f; }
                if (zp && z1 - p.z < R && R - (z1 - p.z) > az) { az = R - (z1 - p.z); szn = -1f; }
                if (ax <= 0f && az <= 0f) continue;
                float a = Mathf.Sqrt(ax * ax + az * az);
                // вертикальный угол пласта — тоже скруглён (по кругу в плане)
                float kc = a > R ? R / a : 1f;
                float axc = ax * kc, azc = az * kc, ac = a * kc;
                float top = TopY(ref g, p.x, p.z);
                float b = Mathf.Min(R, p.y - (top - R));
                float kv = 1f;
                if (b > 0f && ac > 1e-6f)
                {
                    // квадрат R×R у ребра → четверть круга: верх и бок плавно сходятся
                    float mx = Mathf.Max(ac, b), len = Mathf.Sqrt(ac * ac + b * b);
                    float s = mx / Mathf.Max(1e-6f, len);
                    kv = s;
                    p.y = top - R + b * s;
                }
                p.x += (ax - axc * kv) * sxn;
                p.z += (az - azc * kv) * szn;
                V[i] = p;
            }
        }

        /// <summary>
        /// v23.43: гладкие нормали по шву «верх ↔ бок»: вершины в одной точке с близкими нормалями (угол меньше ~60°)
        /// получают общую нормаль. Свет и обводка идут по бисквиту целиком, корочка больше не «отходит» стенкой.
        /// Острые рёбра (срез, низ) остаются острыми.
        /// </summary>
        private static void SmoothNormals(Mesh m)
        {
            Vector3[] v = m.vertices;
            Vector3[] n = m.normals;
            if (n == null || n.Length != v.Length) return;
            var groups = new Dictionary<long, List<int>>();
            for (int i = 0; i < v.Length; i++)
            {
                long key = ((long)Mathf.RoundToInt(v[i].x * 5000f) & 0x1FFFFF)
                         | (((long)Mathf.RoundToInt(v[i].y * 5000f) & 0x1FFFFF) << 21)
                         | (((long)Mathf.RoundToInt(v[i].z * 5000f) & 0x1FFFFF) << 42);
                List<int> l;
                if (!groups.TryGetValue(key, out l)) { l = new List<int>(2); groups[key] = l; }
                l.Add(i);
            }
            Vector3[] o = new Vector3[n.Length];
            foreach (List<int> l in groups.Values)
            {
                for (int a = 0; a < l.Count; a++)
                {
                    Vector3 na = n[l[a]];
                    if (l.Count == 1) { o[l[a]] = na; continue; }
                    Vector3 sum = Vector3.zero;
                    for (int b = 0; b < l.Count; b++)
                    {
                        Vector3 nb = n[l[b]];
                        if (Vector3.Dot(na, nb) > 0.5f) sum += nb;
                    }
                    o[l[a]] = sum.sqrMagnitude > 1e-12f ? sum.normalized : na;
                }
            }
            m.normals = o;
        }

        /// <summary>Плитка: p00→p10 — ось U, p00→p01 — ось V, Cross(U,V) = наружу.</summary>
        private static void Quad(Vector3 p00, Vector3 p10, Vector3 p11, Vector3 p01, Color c0, Color c1)
        {
            int b = V.Count;
            V.Add(p00); V.Add(p10); V.Add(p11); V.Add(p01);
            C.Add(c0); C.Add(c0); C.Add(c1); C.Add(c1);
            Vector2 uv = new Vector2(PoreBottom, 7f);
            U.Add(uv); U.Add(uv); U.Add(uv); U.Add(uv);
            T.Add(b); T.Add(b + 1); T.Add(b + 3);
            T.Add(b + 1); T.Add(b + 2); T.Add(b + 3);
        }

        // ───────────────────────── МОРОЖЕНОЕ ─────────────────────────

        private static Mesh _scoop;

        /// <summary>Шарик мороженого из ведра мастерком: приплюснутый купол, рваный «воротничок» снизу,
        /// следы мастерка. Радиус 1, высота 1 — размер задаёт масштаб объекта. Цвет вершин белый — тон даёт материал.</summary>
        /// <summary>
        /// Шарик мороженого (как из ложки-скупа): почти шар, низ срезан — стоит на бисквите,
        /// у низа волнистая «губка» от ложки, по бокам лёгкие бугорки. Меш: ширина радиус 1, высота 0..1;
        /// сплющенность задаёт масштаб (у тарелки — Ice Round).
        /// </summary>
        public static Mesh IceScoop()
        {
            if (_scoop != null) return _scoop;
            const int rings = 30, segs = 60;
            const float thMax = 2.55f;                          // ~146° от макушки — низ срезан
            float cy = -Mathf.Cos(thMax), H = 1f + cy;          // высота шара над срезом, в радиусах
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            for (int i = 0; i <= rings; i++)
            {
                float th = i / (float)rings * thMax;            // от макушки вниз
                float lipW = Mathf.Exp(-Mathf.Pow((th - 2.2f) / 0.2f, 2f));
                for (int j = 0; j <= segs; j++)
                {
                    float ph = j / (float)segs * Mathf.PI * 2f;
                    float bump = 1f + 0.035f * Mathf.Sin(3f * ph + 2.1f * th) + 0.025f * Mathf.Sin(7f * ph - 4.3f * th + 1.3f)
                                    + 0.012f * Mathf.Sin(13f * ph + 6f * th);
                    float lip = lipW * (0.07f + 0.05f * Mathf.Sin(9f * ph + 0.7f));
                    float r = Mathf.Sin(th) * bump + lip;
                    float y = (cy + Mathf.Cos(th) * (1f + 0.02f * Mathf.Sin(5f * ph))) / H;
                    if (i == rings) y = 0f;
                    v.Add(new Vector3(r * Mathf.Cos(ph), y, r * Mathf.Sin(ph)));
                    c.Add(new Color(1f, 1f, 1f, 0.5f + 0.3f * lipW));
                }
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < segs; j++)
                {
                    int a = i * (segs + 1) + j, b = a + segs + 1;
                    t.Add(a); t.Add(a + 1); t.Add(b);
                    t.Add(a + 1); t.Add(b + 1); t.Add(b);
                }
            int last = rings * (segs + 1);
            int c0 = v.Count;
            v.Add(Vector3.zero); c.Add(Color.white);
            for (int j = 0; j <= segs; j++) { v.Add(v[last + j]); c.Add(Color.white); }
            for (int j = 0; j < segs; j++) { t.Add(c0); t.Add(c0 + 1 + j); t.Add(c0 + 2 + j); }
            _scoop = new Mesh { name = "IceScoop (auto)", hideFlags = HideFlags.DontSave };
            _scoop.SetVertices(v); _scoop.SetColors(c); _scoop.SetTriangles(t, 0);
            _scoop.RecalculateNormals();
            // шов по долготе и макушка — без полосы на свету
            Vector3[] n = _scoop.normals;
            for (int i = 0; i <= rings; i++)
            {
                int a = i * (segs + 1), b = a + segs;
                Vector3 m = (n[a] + n[b]).normalized;
                n[a] = m; n[b] = m;
            }
            for (int j = 0; j <= segs; j++) n[j] = Vector3.up;
            _scoop.normals = n;
            _scoop.RecalculateBounds();
            return _scoop;
        }
    }
}
