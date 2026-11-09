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
        public static readonly Color TopCrust = new Color(0.24f, 0.14f, 0.09f, 0.5f);
        public static readonly Color SideCrust = new Color(0.22f, 0.13f, 0.08f, 0.55f);
        public static readonly Color SideCrumb = new Color(0.33f, 0.20f, 0.13f, 1f);
        public static readonly Color CutCrumb = new Color(0.39f, 0.25f, 0.17f, 1f);
        public static readonly Color CutCrust = new Color(0.25f, 0.15f, 0.10f, 0.6f);
        public static readonly Color Bottom = new Color(0.27f, 0.16f, 0.10f, 0.8f);

        /// <summary>Маска сторон куска: какая сторона — корочка (край пласта), остальные — срез.</summary>
        public const byte XNeg = 1, XPos = 2, ZNeg = 4, ZPos = 8, AllCrust = 15;

        private static readonly List<Vector3> V = new List<Vector3>();
        private static readonly List<Color> C = new List<Color>();
        private static readonly List<int> T = new List<int>();

        private struct Grid
        {
            public int Nx, Nz, Sub;
            public Vector3 Cell;
            public bool[] Present;          // null — все на месте
            public byte OuterCrust;         // какие края сетки — корочка
            public float Bulge;             // подушка верха, доля высоты
            public float Wobble;            // неровность верха, доля высоты
            public float Seed;
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
            return g.Cell.y * (0.5f + g.Bulge * b + g.Wobble * n * 2f);
        }

        /// <summary>Центр клетки (локально).</summary>
        public static Vector3 CellCenter(int nx, int nz, Vector3 cell, int ix, int iz)
            => new Vector3((ix + 0.5f) * cell.x - nx * cell.x * 0.5f, 0f, (iz + 0.5f) * cell.z - nz * cell.z * 0.5f);

        /// <summary>Пласт из оставшихся клеток. present[ix + iz*nx]; null — целый.</summary>
        public static Mesh BuildSlab(Mesh m, int nx, int nz, Vector3 cell, bool[] present, float bulge = 0.05f, int sub = 2, float seed = 3.7f)
        {
            var g = new Grid { Nx = nx, Nz = nz, Sub = Mathf.Clamp(sub, 1, 6), Cell = cell, Present = present, OuterCrust = AllCrust, Bulge = bulge, Wobble = 0.012f, Seed = seed };
            return Build(m, ref g, "SpongeSlab (auto)");
        }

        /// <summary>Один кусок размером size (локально, по центру). crust — какие стороны корочка (XNeg|XPos|ZNeg|ZPos).</summary>
        public static Mesh BuildPiece(Mesh m, Vector3 size, byte crust, float seed = 1.3f)
        {
            var g = new Grid { Nx = 1, Nz = 1, Sub = 4, Cell = size, Present = null, OuterCrust = crust, Bulge = 0.025f, Wobble = 0.012f, Seed = seed };
            return Build(m, ref g, "SpongePiece (auto)");
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
            V.Clear(); C.Clear(); T.Clear();
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
                    Color c = TopCrust;
                    float n = Mathf.PerlinNoise(x * 7.3f + g.Seed, z * 7.3f) - 0.5f;
                    c.r *= 1f + n * 0.12f; c.g *= 1f + n * 0.12f; c.b *= 1f + n * 0.12f;
                    C.Add(c);
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

            if (m == null) m = new Mesh();
            m.Clear();
            m.name = name;
            if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(V);
            m.SetColors(C);
            m.SetTriangles(T, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static readonly float[] RowF = { 0f, 0.55f, 0.8f, 0.93f, 1f };

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
                    C.Add(RowColor(crust, f, p));
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

        private static Color RowColor(bool crust, float f, Vector3 p)
        {
            Color c;
            if (crust)
            {
                if (f <= 0.55f) c = SideCrumb;
                else if (f <= 0.8f) c = Color.Lerp(SideCrumb, SideCrust, 0.25f);
                else c = Color.Lerp(SideCrust, SideCrumb, 0.15f * (1f - f));
                if (f >= 0.99f) c = SideCrust;
            }
            else
            {
                c = f >= 0.99f ? CutCrust : f >= 0.93f ? Color.Lerp(CutCrumb, CutCrust, 0.6f) : CutCrumb;
            }
            float n = Mathf.PerlinNoise(p.x * 9.1f + 2.3f, p.z * 9.1f + p.y * 5f) - 0.5f;
            c.r *= 1f + n * 0.1f; c.g *= 1f + n * 0.1f; c.b *= 1f + n * 0.1f;
            return c;
        }

        /// <summary>Плитка: p00→p10 — ось U, p00→p01 — ось V, Cross(U,V) = наружу.</summary>
        private static void Quad(Vector3 p00, Vector3 p10, Vector3 p11, Vector3 p01, Color c0, Color c1)
        {
            int b = V.Count;
            V.Add(p00); V.Add(p10); V.Add(p11); V.Add(p01);
            C.Add(c0); C.Add(c0); C.Add(c1); C.Add(c1);
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
