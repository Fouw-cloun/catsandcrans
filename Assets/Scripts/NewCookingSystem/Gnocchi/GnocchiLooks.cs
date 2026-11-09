using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ВИД ЕДЫ ДЛЯ НЬОККОВ — общие меши, текстуры и материалы (создаются в рантайме, один раз):
    ///   • лужица масла (неровный край, мягкий бортик, чуть выпуклая);
    ///   • лист шалфея, обжаренный в масле (овальный, прожилка, хрустящие загнутые края);
    ///   • стружка пармезана (закрученная лента) и крошка;
    ///   • капля масла;
    ///   • текстуры: крапинки теста, зерно сыра.
    /// Всё — только вид, без сети и физики.
    /// </summary>
    public static class GnocchiLooks
    {
        private static readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
        private static readonly Dictionary<int, Mesh> _pools = new Dictionary<int, Mesh>(), _shavings = new Dictionary<int, Mesh>();
        private static Mesh _leaf, _crumb, _bead;
        private static Texture2D _doughTex, _cheeseTex;

        // ───────────────────────── цвета ─────────────────────────
        public static readonly Color Butter = new Color(0.93f, 0.66f, 0.22f);        // ореховое масло (beurre noisette)
        public static readonly Color ButterBurnt = new Color(0.20f, 0.12f, 0.06f);
        public static readonly Color SageFried = new Color(0.27f, 0.40f, 0.20f);
        public static readonly Color SageBurnt = new Color(0.10f, 0.10f, 0.06f);
        public static readonly Color Cheese = new Color(0.99f, 0.93f, 0.70f);
        public static readonly Color CheeseMelted = new Color(1.00f, 0.84f, 0.46f);
        public static readonly Color Fleck = new Color(0.42f, 0.24f, 0.09f);         // коричневые крупинки масла

        // ───────────────────────── материалы ─────────────────────────

        /// <summary>Материал URP Lit (кэш по имени). tex — карта цвета (умножается на цвет).</summary>
        public static Material Mat(string name, Color c, float smooth, Texture2D tex = null, float tiling = 1f)
        {
            Material m;
            if (_mats.TryGetValue(name, out m) && m != null) return m;
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            m = new Material(s) { name = name + " (runtime)" };
            SetColor(m, c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (tex != null)
            {
                if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", Vector2.one * tiling); }
                if (m.HasProperty("_MainTex")) { m.SetTexture("_MainTex", tex); m.SetTextureScale("_MainTex", Vector2.one * tiling); }
            }
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);              // листья и стружка — двусторонние
            _mats[name] = m;
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m == null) return;
            c.a = 1f;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static void SetSmooth(Material m, float s)
        {
            if (m == null) return;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", s);
        }

        // ───────────────────────── текстуры ─────────────────────────

        /// <summary>Тесто ньокков: мелкие крапинки картофеля и муки, лёгкая неровность цвета.</summary>
        public static Texture2D DoughTex
        {
            get
            {
                if (_doughTex != null) return _doughTex;
                _doughTex = Noise("GnocchiDough", 128, 11, 0.06f, 0.035f, 0.80f, 1.04f);
                return _doughTex;
            }
        }

        /// <summary>Сыр: зернистый мякиш, белые кристаллики и тёмные точки.</summary>
        public static Texture2D CheeseTex
        {
            get
            {
                if (_cheeseTex != null) return _cheeseTex;
                _cheeseTex = Noise("Cheese", 128, 23, 0.05f, 0.05f, 0.86f, 1.12f);
                return _cheeseTex;
            }
        }

        /// <summary>
        /// Шумовая текстура (оттенки серого, умножается на цвет материала): мягкие пятна + крапинки.
        /// darkShare/lightShare — доля тёмных/светлых крапинок.
        /// </summary>
        public static Texture2D Noise(string name, int size, int seed, float darkShare, float lightShare, float dark, float light)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name + " (runtime)", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            var rnd = new System.Random(seed);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    // мягкие пятна — сумма синусов (бесшовно по краям)
                    float blot = 0.5f + 0.25f * Mathf.Sin(u * Mathf.PI * 2f * 3f + Mathf.Sin(v * Mathf.PI * 2f * 2f) * 1.3f)
                                      + 0.25f * Mathf.Sin(v * Mathf.PI * 2f * 4f + Mathf.Sin(u * Mathf.PI * 2f * 3f) * 1.1f);
                    float k = Mathf.Lerp(0.95f, 1.02f, blot);
                    double r = rnd.NextDouble();
                    if (r < darkShare) k *= Mathf.Lerp(dark, 0.95f, (float)rnd.NextDouble());
                    else if (r < darkShare + lightShare) k *= Mathf.Lerp(1.02f, light, (float)rnd.NextDouble());
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(255f * Mathf.Clamp01(k * 0.97f)), 0, 255);
                    px[y * size + x] = new Color32(b, b, b, 255);
                }
            t.SetPixels32(px);
            t.Apply(true, false);
            return t;
        }

        // ───────────────────────── меши ─────────────────────────

        /// <summary>
        /// Лужица: диск радиуса 0.5 (XZ), неровный «каплями» край, высота 0..1 по Y (масштабом — толщина слоя):
        /// плоский верх с лёгкой выпуклостью и скруглённым бортиком. Варианты — по seed.
        /// </summary>
        public static Mesh Pool(int seed)
        {
            seed = Mathf.Abs(seed) % 4;
            Mesh m;
            if (_pools.TryGetValue(seed, out m) && m != null) return m;
            const int N = 56, Rr = 7;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            v.Add(new Vector3(0f, 1f, 0f)); uv.Add(new Vector2(0.5f, 0.5f));
            float p1 = seed * 1.7f, p2 = seed * 2.9f + 1f;
            for (int ring = 1; ring <= Rr; ring++)
            {
                float rk = ring / (float)Rr;                                      // 0..1 от центра к краю
                for (int i = 0; i < N; i++)
                {
                    float a = i / (float)N * Mathf.PI * 2f;
                    float edge = 0.5f * (1f + 0.08f * Mathf.Sin(a * 3f + p1) + 0.06f * Mathf.Sin(a * 7f + p2) + 0.035f * Mathf.Sin(a * 13f + p1 * 2f));
                    float r = edge * rk;
                    // высота: плато с выпуклостью, у края — круглый бортик вниз до 0
                    float h = rk < 0.78f ? 1f - 0.12f * rk * rk : Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((rk - 0.78f) / 0.22f, 2f))) * 0.93f;
                    if (ring == Rr) h = 0f;
                    v.Add(new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r));
                    uv.Add(new Vector2(0.5f + Mathf.Cos(a) * r, 0.5f + Mathf.Sin(a) * r));
                }
            }
            for (int i = 0; i < N; i++) { t.Add(0); t.Add(1 + (i + 1) % N); t.Add(1 + i); }
            for (int ring = 1; ring < Rr; ring++)
            {
                int a0 = 1 + (ring - 1) * N, b0 = 1 + ring * N;
                for (int i = 0; i < N; i++)
                {
                    int i1 = (i + 1) % N;
                    t.Add(a0 + i); t.Add(a0 + i1); t.Add(b0 + i);
                    t.Add(a0 + i1); t.Add(b0 + i1); t.Add(b0 + i);
                }
            }
            m = Build("ButterPool" + seed, v, uv, t, Vector3.up, 0);
            _pools[seed] = m;
            return m;
        }

        /// <summary>
        /// Лист шалфея, обжаренный в масле: длина 1 по Z, ширина ~0.46, ось-прожилка вдавлена,
        /// поверхность «вафельная» (у шалфея так), края загнуты вверх. Двусторонний.
        /// </summary>
        public static Mesh SageLeaf
        {
            get
            {
                if (_leaf != null) return _leaf;
                const int L = 16, W = 10;
                var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
                for (int i = 0; i <= L; i++)
                {
                    float s = i / (float)L;                                   // 0 — черешок, 1 — кончик
                    float z = s - 0.5f;
                    float half = 0.23f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(s * 0.96f + 0.02f)), 0.75f) * (1.15f - 0.35f * s);
                    for (int j = 0; j <= W; j++)
                    {
                        float q = j / (float)W * 2f - 1f;                         // −1..1 поперёк
                        float x = q * half;
                        float y = 0.10f * q * q                                       // края вверх (хрустящий лист скручивается)
                                - 0.035f * Mathf.Exp(-q * q * 40f)                    // прожилка
                                + 0.012f * Mathf.Sin(s * 38f) * Mathf.Sin(q * 9f)     // «вафельная» поверхность
                                + 0.05f * (s - 0.5f) * (s - 0.5f);                    // чуть выгнут вдоль
                        v.Add(new Vector3(x, y, z));
                        uv.Add(new Vector2(j / (float)W, s));
                    }
                }
                int row = W + 1;
                for (int i = 0; i < L; i++)
                    for (int j = 0; j < W; j++)
                    {
                        int a = i * row + j, b = a + 1, c = a + row, d = c + 1;
                        t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
                    }
                _leaf = Build("SageLeaf", v, uv, t, Vector3.up, 1);
                return _leaf;
            }
        }

        /// <summary>
        /// Стружка пармезана: тонкая лента длиной 1, закрученная дугой (вокруг Z), чуть перекрученная.
        /// Двусторонняя. Варианты — разная закрутка.
        /// </summary>
        public static Mesh Shaving(int variant)
        {
            variant = Mathf.Abs(variant) % 4;
            Mesh m;
            if (_shavings.TryGetValue(variant, out m) && m != null) return m;
            const int S = 18, Wd = 3;
            float curl = 2.2f + variant * 0.7f;                 // радиан на длину
            float twist = (variant % 2 == 0 ? 1f : -1f) * 0.5f;
            float R = 1f / curl;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= S; i++)
            {
                float s = i / (float)S;
                float th = (s - 0.5f) * curl;
                Vector3 c = new Vector3(Mathf.Sin(th) * R, (1f - Mathf.Cos(th)) * R, 0f);
                Vector3 n = new Vector3(-Mathf.Sin(th), Mathf.Cos(th), 0f);              // нормаль ленты (наружу дуги)
                float w = 0.16f * (0.55f + 0.45f * Mathf.Sin(Mathf.PI * s)) * (1f + 0.15f * Mathf.Sin(s * 11f + variant));
                float tw = twist * (s - 0.5f);
                for (int j = 0; j <= Wd; j++)
                {
                    float q = j / (float)Wd - 0.5f;
                    Vector3 across = Vector3.forward * Mathf.Cos(tw) + n * Mathf.Sin(tw);
                    v.Add(c + across * (q * w * 2f) + n * (0.02f * Mathf.Sin(s * 23f + q * 5f)));   // рваный край
                    uv.Add(new Vector2(s, q + 0.5f));
                }
            }
            int row = Wd + 1;
            for (int i = 0; i < S; i++)
                for (int j = 0; j < Wd; j++)
                {
                    int a = i * row + j, b = a + 1, c2 = a + row, d = c2 + 1;
                    t.Add(a); t.Add(b); t.Add(c2); t.Add(b); t.Add(d); t.Add(c2);
                }
            m = Build("CheeseShaving" + variant, v, uv, t, Vector3.zero, 1);
            _shavings[variant] = m;
            return m;
        }

        /// <summary>Крошка: неровный многогранник ~1 (тёртый сыр, коричневые крупинки масла).</summary>
        public static Mesh Crumb
        {
            get
            {
                if (_crumb != null) return _crumb;
                Vector3[] dirs =
                {
                    new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, -1, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1),
                };
                float[] k = { 0.55f, 0.42f, 0.35f, 0.3f, 0.5f, 0.38f };
                var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
                int[,] faces = { { 0, 2, 4 }, { 4, 2, 1 }, { 1, 2, 5 }, { 5, 2, 0 }, { 0, 4, 3 }, { 4, 1, 3 }, { 1, 5, 3 }, { 5, 0, 3 } };
                for (int f = 0; f < 8; f++)
                {
                    for (int c = 0; c < 3; c++) { int id = faces[f, c]; v.Add(dirs[id] * k[id]); uv.Add(new Vector2(c * 0.5f, f / 8f)); }
                    int b = f * 3; t.Add(b); t.Add(b + 1); t.Add(b + 2);
                }
                _crumb = Build("Crumb", v, uv, t, Vector3.zero, 0);
                return _crumb;
            }
        }

        /// <summary>Капля масла: приплюснутый купол (полусфера, Y 0..1).</summary>
        public static Mesh Bead
        {
            get
            {
                if (_bead != null) return _bead;
                const int A = 16, B = 5;
                var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
                v.Add(new Vector3(0f, 1f, 0f)); uv.Add(new Vector2(0.5f, 0.5f));
                for (int r = 1; r <= B; r++)
                {
                    float lat = r / (float)B * Mathf.PI * 0.5f;
                    for (int i = 0; i < A; i++)
                    {
                        float a = i / (float)A * Mathf.PI * 2f;
                        v.Add(new Vector3(Mathf.Cos(a) * Mathf.Sin(lat) * 0.5f, Mathf.Cos(lat), Mathf.Sin(a) * Mathf.Sin(lat) * 0.5f));
                        uv.Add(new Vector2(i / (float)A, r / (float)B));
                    }
                }
                for (int i = 0; i < A; i++) { t.Add(0); t.Add(1 + (i + 1) % A); t.Add(1 + i); }
                for (int r = 1; r < B; r++)
                {
                    int a0 = 1 + (r - 1) * A, b0 = 1 + r * A;
                    for (int i = 0; i < A; i++)
                    {
                        int i1 = (i + 1) % A;
                        t.Add(a0 + i); t.Add(a0 + i1); t.Add(b0 + i);
                        t.Add(a0 + i1); t.Add(b0 + i1); t.Add(b0 + i);
                    }
                }
                _bead = Build("ButterBead", v, uv, t, Vector3.zero, 0);
                return _bead;
            }
        }

        /// <summary>
        /// Собрать меш. faceUp ≠ 0 — обход проверяется так, чтобы нормали смотрели вверх (по большинству);
        /// faceUp = 0 — наружу от центра. twoSided — дублировать треугольники обратной стороной.
        /// </summary>
        private static Mesh Build(string name, List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 faceUp, int twoSided)
        {
            // ориентация: каждый треугольник — наружу (от центра) или вверх
            Vector3 cen = Vector3.zero; foreach (Vector3 p in v) cen += p; cen /= Mathf.Max(1, v.Count);
            for (int i = 0; i < t.Count; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                Vector3 want = faceUp != Vector3.zero ? faceUp : (a + b + c) / 3f - cen;
                if (Vector3.Dot(n, want) < 0f) { int q = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = q; }
            }
            if (twoSided != 0)
            {
                int n0 = v.Count, tc = t.Count;
                for (int i = 0; i < n0; i++) { v.Add(v[i]); uv.Add(uv[i]); }
                for (int i = 0; i < tc; i += 3) { t.Add(t[i] + n0); t.Add(t[i + 2] + n0); t.Add(t[i + 1] + n0); }
            }
            var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
