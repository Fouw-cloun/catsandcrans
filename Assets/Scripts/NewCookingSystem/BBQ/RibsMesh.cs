using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// МОДЕЛЬ РЁБЕР (процедурная) — пласт свиных рёбер: N рёбер в ряд.
    ///   Оси: X — поперёк рёбер (длина пласта), Z — вдоль кости (−Z мясистый край, +Z — торчат кости),
    ///   Y — вверх: +Y мясная сторона, −Y сторона кости (там белая плёнка).
    ///   • сверху — мясо горбиками над каждой костью, к краю с костями тоньше, пласт чуть изогнут;
    ///   • снизу — под плёнкой проступают кости полосками;
    ///   • с +Z торчат концы костей (цвет кости, на них не ложатся специи и соус).
    /// Кроме меша — данные каждой вершины для покраски: сторона, клетка (ребро × треть длины), шум
    /// жира/плёнки и крапинок специй. Красит RibsRack (цвета вершин), шейдер — CatCooks/Food Heap.
    /// </summary>
    public sealed class RibsMesh
    {
        public const int Rows = 3;                 // клетки вдоль кости (Z): мясо, середина, у кости

        public Mesh Mesh;
        public int Ribs;
        public Vector3 Dims;                       // ширина одного ребра, длина кости (Z), толщина
        public byte[] Side;                        // 0 — мясная (+Y), 1 — костяная (−Y), 255 — кость (не красится)
        public byte[] Col, Row;                    // клетка
        public float[] Fat;                        // 0..1: жир/плёнка (белые прожилки)
        public float[] Speck;                      // 0..1: крапинки специй
        public float[] BoneLine;                   // 0..1: снизу — где под плёнкой кость
        public float[] Edge;                       // 0..1: бок/ребро пласта (сильнее румянится)

        public float Width => Ribs * Dims.x;
        /// <summary>Брискет: толстый пласт без костей, сверху жировая «шапка», волокна вдоль X (резать поперёк).</summary>
        public bool Brisket;

        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<int> _t = new List<int>();
        private readonly List<byte> _side = new List<byte>(), _col = new List<byte>(), _row = new List<byte>();
        private readonly List<float> _fat = new List<float>(), _speck = new List<float>(), _bone = new List<float>(), _edge = new List<float>();

        /// <param name="ribs">Сколько рёбер</param>
        /// <param name="dims">x — ширина ребра, y — длина кости (Z), z — толщина мяса</param>
        public static RibsMesh Build(int ribs, Vector3 dims, int seed = 7, bool brisket = false)
        {
            var b = new RibsMesh { Ribs = Mathf.Max(1, ribs), Dims = dims, Brisket = brisket };
            b.Make(seed);
            return b;
        }

        // ─────────────────────────────────────────────────────────────

        private float W, D, T;
        private int _seed;

        private void Make(int seed)
        {
            _seed = seed;
            W = Ribs * Dims.x; D = Dims.y; T = Dims.z;
            int sub = 6;                                   // делений на ребро по X
            int nx = Ribs * sub + 1, nz = 15;

            // ── верх (мясо) и низ (кость) — сетки ──
            int top = _v.Count;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = -W * 0.5f + W * i / (nx - 1), z = -D * 0.5f + D * j / (nz - 1);
                    Add(new Vector3(x, TopY(x, z), z), 0, x, z, 0f);
                }
            int bot = _v.Count;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = -W * 0.5f + W * i / (nx - 1), z = -D * 0.5f + D * j / (nz - 1);
                    float bl = BoneAt(x);
                    Add(new Vector3(x, BotY(x, z), z), 1, x, z, 0f, bl);
                }
            for (int j = 0; j < nz - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    int a = top + j * nx + i;
                    Quad(a, a + 1, a + nx + 1, a + nx, Vector3.up);
                    int c = bot + j * nx + i;
                    Quad(c, c + 1, c + nx + 1, c + nx, Vector3.down);
                }

            // ── бока: 3 ряда (низ, выпуклая середина, верх) по периметру ──
            Wall(nx, nz, true, -1);   // −Z (мясистый край)
            Wall(nx, nz, true, +1);   // +Z (край с костями)
            Wall(nx, nz, false, -1);  // −X
            Wall(nx, nz, false, +1);  // +X

            // ── концы костей с +Z ──
            if (!Brisket) for (int k = 0; k < Ribs; k++) BoneEnd(k);

            Mesh = new Mesh { name = (Brisket ? "Brisket_" : "Ribs_") + Ribs + " (auto)", hideFlags = HideFlags.DontSave };
            Mesh.SetVertices(_v);
            Mesh.SetTriangles(_t, 0);
            var cols = new Color[_v.Count];
            for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
            Mesh.colors = cols;
            Mesh.RecalculateNormals();
            Mesh.RecalculateBounds();
            Side = _side.ToArray(); Col = _col.ToArray(); Row = _row.ToArray();
            Fat = _fat.ToArray(); Speck = _speck.ToArray(); BoneLine = _bone.ToArray(); Edge = _edge.ToArray();
        }

        /// <summary>Сколько мяса над точкой: у мясистого края толще, к костям тоньше; горбик над каждым ребром.</summary>
        private float Thick(float x, float z)
        {
            if (Brisket)
            {
                // «плоская» часть тоньше, «горбушка» (+X) толще; края по Z скруглены
                float u = Mathf.InverseLerp(-W * 0.5f, W * 0.5f, x);
                float zz = Mathf.Abs(Mathf.InverseLerp(-D * 0.5f, D * 0.5f, z) * 2f - 1f);
                float round = Mathf.Sqrt(Mathf.Max(0.05f, 1f - Mathf.Pow(zz, 4f)));
                float pf = Mathf.Lerp(0.78f, 1.18f, u * u * (3f - 2f * u));
                return T * pf * Mathf.Lerp(0.55f, 1f, round) * (1f + 0.06f * (Noise(x * 11f, z * 11f) - 0.5f));
            }
            float zn = Mathf.InverseLerp(-D * 0.5f, D * 0.5f, z);          // 0 мясо … 1 кости
            float prof = Mathf.Lerp(1f, 0.62f, zn * zn);
            float rib = Mathf.Repeat(x + W * 0.5f, Dims.x) / Dims.x;        // 0..1 внутри ребра
            float hump = 0.86f + 0.14f * Mathf.Sin(rib * Mathf.PI);
            float n = 1f + 0.05f * (Noise(x * 9f, z * 9f) - 0.5f);
            return T * prof * hump * n;
        }

        private float Bend(float z)
        {
            if (Brisket) return 0f;
            float zn = Mathf.InverseLerp(-D * 0.5f, D * 0.5f, z);
            return -D * 0.06f * zn * zn;                                    // рёбра чуть загибаются вниз
        }

        private float TopY(float x, float z) => Bend(z) + Thick(x, z) * 0.5f;

        private float BotY(float x, float z)
        {
            if (Brisket) return -Thick(x, z) * 0.5f;
            float zn = Mathf.InverseLerp(-D * 0.5f, D * 0.5f, z);
            float bone = BoneAt(x) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.3f, zn));
            return Bend(z) - Thick(x, z) * 0.5f - bone * T * 0.12f;         // кости проступают снизу
        }

        /// <summary>1 — по центру кости, 0 — между костями.</summary>
        private float BoneAt(float x)
        {
            float rib = Mathf.Repeat(x + W * 0.5f, Dims.x) / Dims.x;
            return Mathf.Clamp01(1f - Mathf.Abs(rib - 0.5f) / 0.18f);
        }

        private void Wall(int nx, int nz, bool alongX, int sign)
        {
            int n = alongX ? nx : nz;
            int start = _v.Count;
            for (int i = 0; i < n; i++)
            {
                float x, z;
                if (alongX) { x = -W * 0.5f + W * i / (n - 1); z = sign * D * 0.5f; }
                else { x = sign * W * 0.5f; z = -D * 0.5f + D * i / (n - 1); }
                float yb = BotY(x, z), yt = TopY(x, z);
                Vector3 outward = alongX ? new Vector3(0f, 0f, sign) : new Vector3(sign, 0f, 0f);
                float bulge = (yt - yb) * 0.22f;
                Add(new Vector3(x, yb, z), 1, x, z, 1f);
                Add(new Vector3(x, (yb + yt) * 0.5f, z) + outward * bulge, (byte)(Brisket ? 1 : 0), x, z, 1f);   // у брискета бок — мясо, жир только сверху
                Add(new Vector3(x, yt, z), 0, x, z, 1f);
            }
            Vector3 o = alongX ? new Vector3(0f, 0f, sign) : new Vector3(sign, 0f, 0f);
            for (int i = 0; i < n - 1; i++)
            {
                int a = start + i * 3, b = a + 3;
                Quad(a, b, b + 1, a + 1, o);
                Quad(a + 1, b + 1, b + 2, a + 2, o);
            }
        }

        private void BoneEnd(int k)
        {
            const int seg = 10;
            float x = -W * 0.5f + Dims.x * (k + 0.5f);
            float z0 = D * 0.5f - D * 0.02f;
            float len = D * 0.09f * (0.85f + 0.3f * Noise(k * 3.1f, 1.7f));
            float r = Mathf.Max(0.004f, Dims.x * 0.16f);
            float y = Bend(D * 0.5f) - Thick(x, D * 0.5f) * 0.12f;
            int ring0 = _v.Count;
            for (int s = 0; s <= seg; s++)
            {
                float a = s / (float)seg * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.8f, 0f);
                AddBone(new Vector3(x, y, z0) + d);
                AddBone(new Vector3(x, y - len * 0.25f, z0 + len) + d * 1.12f);   // концы чуть толще и вниз
            }
            for (int s = 0; s < seg; s++)
            {
                int a = ring0 + s * 2, b = a + 2;
                Vector3 c = new Vector3(x, y, z0 + len * 0.5f);
                Tri(a, b, a + 1, ((_v[a] + _v[b] + _v[a + 1]) / 3f) - c);
                Tri(b, b + 1, a + 1, ((_v[b] + _v[b + 1] + _v[a + 1]) / 3f) - c);
            }
            // торец
            int cap = _v.Count;
            AddBone(new Vector3(x, y - len * 0.25f, z0 + len + r * 0.25f));
            for (int s = 0; s < seg; s++)
            {
                int a = ring0 + s * 2 + 1, b = ring0 + (s + 1) * 2 + 1;
                Tri(cap, a, b, Vector3.forward);
            }
        }

        // ───────── сборка ─────────

        private void Add(Vector3 p, byte side, float x, float z, float edge, float boneLine = 0f)
        {
            _v.Add(p);
            int col = Mathf.Clamp(Mathf.FloorToInt((x + W * 0.5f) / Dims.x), 0, Ribs - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt(Mathf.InverseLerp(-D * 0.5f, D * 0.5f, z) * Rows), 0, Rows - 1);
            _side.Add(side); _col.Add((byte)col); _row.Add((byte)row);
            // Мясо с прожилками соединительной ткани — НЕ сплошная белая плёнка.
            //   сверху — тонкие кривые прожилки жира и редкая мраморность (~20%);
            //   снизу — полупрозрачная плёнка рваными полосами вдоль костей и между ними (~40%).
            // Рубанок снимает именно прожилки: под ними — чистое красное мясо.
            float fat;
            if (Brisket)
            {
                if (side == 0)
                {
                    // жировая шапка сверху: почти сплошная, с прожилками мяса
                    float n = Noise(x * 13f + 2f, z * 13f);
                    fat = 0.78f + 0.22f * SStep(0.3f, 0.7f, n) - 0.25f * SStep(0.82f, 0.95f, Noise(x * 37f, z * 29f));
                }
                else
                {
                    // мясо: волокна вдоль X (резать поперёк!), серебристая плёнка пятнами
                    float grain = SStep(0.88f, 0.98f, Mathf.Abs(Mathf.Sin(z * 140f + Noise(x * 6f, z * 4f) * 3f)));
                    float patch = SStep(0.55f, 0.8f, Noise(x * 8f + 7f, z * 8f));
                    fat = grain * 0.35f + patch * 0.55f;
                }
            }
            else if (side == 0)
            {
                float wob = Noise(x * 5f / Dims.x * 0.07f + 1.3f, z * 3f) * 4.5f + Noise(x * 23f, z * 17f) * 1.2f;
                float stripe = Mathf.Abs(Mathf.Sin(z / D * 13f + wob));
                float marble = Noise(x * 31f + 3f, z * 27f) * Noise(x * 9f + 8f, z * 7f);
                fat = SStep(0.955f, 0.995f, stripe) * 0.85f + SStep(0.42f, 0.62f, marble) * 0.55f;
            }
            else
            {
                float wob = Noise(x * 7f + 2.1f, z * 4f) * 3f;
                float streak = SStep(0.80f, 0.97f, Mathf.Abs(Mathf.Sin(x / Dims.x * Mathf.PI * 1.0f + wob)));   // полосы вдоль костей
                float patch = SStep(0.55f, 0.8f, Noise(x * 9f + 7f, z * 6f));                                    // рваные пятна плёнки
                fat = streak * 0.7f + patch * 0.5f + boneLine * 0.15f;
            }
            _fat.Add(Mathf.Clamp01(fat));
            _speck.Add(Noise(x * 61f + 11f, z * 61f + 5f));
            _bone.Add(boneLine);
            _edge.Add(edge);
        }

        private void AddBone(Vector3 p)
        {
            _v.Add(p);
            _side.Add(255); _col.Add(0); _row.Add(0);
            _fat.Add(0f); _speck.Add(0f); _bone.Add(1f); _edge.Add(1f);
        }

        private void Quad(int a, int b, int c, int d, Vector3 outward)
        {
            Tri(a, b, c, outward);
            Tri(a, c, d, outward);
        }

        /// <summary>Треугольник наружу (Unity: лицевая сторона — где Cross(b−a, c−a)).</summary>
        private void Tri(int a, int b, int c, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]);
            if (Vector3.Dot(n, outward) < 0f) { int t = b; b = c; c = t; }
            _t.Add(a); _t.Add(b); _t.Add(c);
        }

        /// <summary>smoothstep как в шейдерах (Mathf.SmoothStep в Unity — интерполяция from→to, не порог!).</summary>
        private static float SStep(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        private float Noise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private float Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + _seed * 2147483647;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f;
            }
        }
    }
}
