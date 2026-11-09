using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// «МАЗКИ» по рёбрам и брискету — слой поверх мяса, как краска маслом.
    ///   • Руб (мастерок): песочная зернистая шапка специй с крапинками (перец, соль, чеснок), неровные края.
    ///   • Соус / горчица (кисть): гладкий блестящий слой, полосы от щетины.
    /// Каждый мазок рисуется в маску-текстуру там, где провёл инструмент (вытянут по движению, с полосами),
    /// поверх — тонкая копия поверхности пласта (свой меш, прозрачный материал URP Lit).
    /// Мазки приходят с сервера (RpcStamp у Ribs Rack), а клетки рёбер (SyncList) — «правда»:
    /// если клетка покрыта, а мазков там мало (зашёл позже, тест-меню, отрезали ребро) — дорисует сам.
    /// Только вид: на оценку и готовку не влияет. Работает у всех клиентов одинаково по форме.
    /// </summary>
    public sealed class RackPaintLayer
    {
        public enum Kind { Rub, Glaze }

        private const int PxPerRib = 32, SideH = 128;

        private readonly Kind _kind;
        private readonly string _name;
        private GameObject _go;
        private Mesh _mesh;
        private Material _mat;
        private Texture2D _tex;
        private float[] _mask;
        private Color32[] _px;
        private float[] _grain;           // 0.6..1.4 — «песчинки»: где слой тоньше рвётся
        private Color[] _alb;             // цвет зерна без учёта толщины
        private int _sw, _tw, _th, _ribs, _rows;
        private float _W, _D;
        private int _dx0 = int.MaxValue, _dy0 = int.MaxValue, _dx1 = -1, _dy1 = -1;
        private Color _base = new Color(-1f, 0f, 0f), _tint = Color.white;
        private float _cook = -1f;
        private bool _visible = true;

        public RackPaintLayer(Kind kind, string name) { _kind = kind; _name = name; }

        public bool Ready => _go != null;

        // ───────────────────────── построение ─────────────────────────

        /// <summary>Сделать (или переделать) слой под меш пласта. Маска очищается — потом FromCells дорисует.</summary>
        public void Build(Transform parent, RibsMesh rm, Material template, float lift, int order)
        {
            if (rm == null || rm.Mesh == null) return;
            _ribs = rm.Ribs; _rows = RibsMesh.Rows;
            _W = rm.Width; _D = rm.Dims.y;
            _sw = Mathf.Clamp(PxPerRib * _ribs, 32, 256);
            _tw = _sw * 2; _th = SideH;
            if (_go == null)
            {
                _go = new GameObject(_name + " (auto)");
                _go.layer = parent.gameObject.layer;
                _go.transform.SetParent(parent, false);
                _go.AddComponent<MeshFilter>();
                MeshRenderer mr = _go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _mat = MakeMaterial(template);
                mr.sharedMaterial = _mat;
            }
            if (_mat != null) _mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + order;
            if (_tex == null || _tex.width != _tw || _tex.height != _th)
            {
                if (_tex != null) SafeDestroy.Kill(_tex);
                _tex = new Texture2D(_tw, _th, TextureFormat.RGBA32, true) { name = _name + " mask (auto)", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                _mask = new float[_tw * _th];
                _px = new Color32[_tw * _th];
                _grain = new float[_tw * _th];
                _alb = new Color[_tw * _th];
                _base = new Color(-1f, 0f, 0f);
                if (_mat != null) { _mat.SetTexture("_BaseMap", _tex); _mat.mainTexture = _tex; }
            }
            else System.Array.Clear(_mask, 0, _mask.Length);
            BuildMesh(rm, lift);
            _go.GetComponent<MeshFilter>().sharedMesh = _mesh;
            MarkAll();
        }

        private void BuildMesh(RibsMesh rm, float lift)
        {
            Mesh src = rm.Mesh;
            Vector3[] v = src.vertices, n = src.normals;
            int[] t = src.triangles;
            if (n == null || n.Length != v.Length) { src.RecalculateNormals(); n = src.normals; }
            var map = new int[v.Length];
            for (int i = 0; i < map.Length; i++) map[i] = -1;
            var vv = new System.Collections.Generic.List<Vector3>();
            var nn = new System.Collections.Generic.List<Vector3>();
            var uu = new System.Collections.Generic.List<Vector2>();
            var tt = new System.Collections.Generic.List<int>();
            for (int k = 0; k + 2 < t.Length; k += 3)
            {
                byte s = rm.Side[t[k]];
                if (s == 255 || rm.Side[t[k + 1]] != s || rm.Side[t[k + 2]] != s) continue;
                for (int j = 0; j < 3; j++)
                {
                    int i = t[k + j];
                    if (map[i] < 0)
                    {
                        map[i] = vv.Count;
                        vv.Add(v[i] + n[i] * lift);
                        nn.Add(n[i]);
                        uu.Add(UV(s, v[i].x, v[i].z));
                    }
                    tt.Add(map[i]);
                }
            }
            if (_mesh == null) _mesh = new Mesh { name = _name + " (auto)" };
            _mesh.Clear();
            _mesh.SetVertices(vv); _mesh.SetNormals(nn); _mesh.SetUVs(0, uu); _mesh.SetTriangles(tt, 0);
            _mesh.RecalculateBounds();
        }

        private Vector2 UV(int side, float x, float z)
        {
            float u = Mathf.Clamp01((x + _W * 0.5f) / Mathf.Max(1e-4f, _W));
            float w = Mathf.Clamp01((z + _D * 0.5f) / Mathf.Max(1e-4f, _D));
            // атлас: левая половина — сторона 0, правая — 1; полпикселя от шва
            float half = 0.5f / _sw;
            return new Vector2((side * _sw + Mathf.Lerp(half, _sw - half, u)) / _tw, w);
        }

        private Material MakeMaterial(Material template)
        {
            Material m;
            if (template != null) m = new Material(template);
            else
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                m = new Material(sh);
                // прозрачный URP Lit
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_AlphaClip", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "Transparent");
            }
            m.name = _name + " (auto)";
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", _kind == Kind.Glaze ? 0.78f : 0.22f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Cull", 2f);
            return m;
        }

        public void SetVisible(bool on)
        {
            if (_go != null && _visible != on) { _visible = on; _go.SetActive(on); }
        }

        public void Destroy()
        {
            if (_mesh != null) SafeDestroy.Kill(_mesh);
            if (_tex != null) SafeDestroy.Kill(_tex);
            if (_mat != null) SafeDestroy.Kill(_mat);
            if (_go != null) SafeDestroy.Kill(_go);
            _mesh = null; _tex = null; _mat = null; _go = null;
        }

        // ───────────────────────── цвет зерна ─────────────────────────

        /// <summary>Цвет слоя (руб: паприка / перец-соль; соус; горчица). cook — 0..1 (запёкся — темнее).
        /// smoothness — блеск (лак у глазури растёт).</summary>
        public void SetLook(Color baseColor, float cook, float smoothness)
        {
            if (_tex == null) return;
            bool recolor = Mathf.Abs(baseColor.r - _base.r) + Mathf.Abs(baseColor.g - _base.g) + Mathf.Abs(baseColor.b - _base.b) > 0.02f;
            if (recolor) { _base = baseColor; Albedo(); _cook = -1f; }
            if (Mathf.Abs(cook - _cook) > 0.04f)
            {
                _cook = cook;
                _tint = Color.Lerp(Color.white, new Color(0.40f, 0.30f, 0.24f), cook);   // запёкся — темнеет до корочки
                MarkAll();
            }
            if (_mat != null) _mat.SetFloat("_Smoothness", smoothness);
        }

        private static float H(int x, int y, int s)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + s * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
        }

        private static float Smooth(float x, float y, int s)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = H(xi, yi, s), b = H(xi + 1, yi, s), c = H(xi, yi + 1, s), d = H(xi + 1, yi + 1, s);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private void Albedo()
        {
            Color b = _base; b.a = 1f;
            bool brisketRub = _kind == Kind.Rub && b.r < 0.4f;   // перец-соль-сахар (тёмный) — больше чёрных и белых крупинок
            for (int y = 0; y < _th; y++)
                for (int x = 0; x < _tw; x++)
                {
                    int i = y * _tw + x;
                    float g = H(x, y, 11);
                    if (_kind == Kind.Rub)
                    {
                        // песок: каждая песчинка своей яркости + пятна крупнее + крапинки
                        float blot = Smooth(x * 0.12f, y * 0.12f, 3);
                        float k = 0.78f + 0.36f * g + 0.18f * (blot - 0.5f);
                        Color c = b * k;
                        c = Color.Lerp(c, new Color(b.r * 1.15f, b.g * 0.9f, b.b * 0.7f), Smooth(x * 0.05f, y * 0.05f, 7) * 0.35f);   // тёплые разводы паприки
                        float sp = H(x / 2, y / 2, 5);                      // крупинки 2×2 пикселя
                        float pepper = brisketRub ? 0.85f : 0.94f, salt = brisketRub ? 0.045f : 0.02f;
                        if (sp > pepper) c = Color.Lerp(c, new Color(0.05f, 0.04f, 0.035f), 0.85f);            // перец
                        else if (sp < salt) c = Color.Lerp(c, new Color(0.96f, 0.94f, 0.88f), 0.8f);           // соль
                        else if (!brisketRub && sp > 0.90f) c = Color.Lerp(c, new Color(0.86f, 0.74f, 0.44f), 0.6f);   // чеснок
                        else if (brisketRub && sp > 0.78f) c = Color.Lerp(c, new Color(0.55f, 0.36f, 0.18f), 0.6f);   // сахар
                        c.a = 1f;
                        _alb[i] = c;
                        _grain[i] = 0.55f + 0.9f * Mathf.Lerp(g, Smooth(x * 0.35f, y * 0.35f, 9), 0.4f);
                    }
                    else
                    {
                        float flow = Smooth(x * 0.06f, y * 0.18f, 4);
                        Color c = b * (0.9f + 0.2f * flow + 0.06f * (g - 0.5f));
                        c.a = 1f;
                        _alb[i] = c;
                        _grain[i] = 0.85f + 0.3f * flow;
                    }
                }
            MarkAll();
        }

        // ───────────────────────── мазки ─────────────────────────

        /// <summary>Мазок от точки a до точки b (оси пласта), сторона side. radius — радиус кисти (м), amount — насыщенность за проход.</summary>
        public void Stroke(int side, Vector3 a, Vector3 b, float radius, float amount, int seed)
        {
            if (_mask == null) return;
            Vector2 pa = Px(side, a), pb = Px(side, b);
            float rx = radius / Mathf.Max(1e-4f, _W) * _sw, ry = radius / Mathf.Max(1e-4f, _D) * _th;
            float r = Mathf.Max(2f, (rx + ry) * 0.5f);
            Vector2 dir = pb - pa;
            float len = dir.magnitude;
            dir = len > 0.01f ? dir / len : new Vector2(Mathf.Cos(seed), Mathf.Sin(seed));
            int steps = Mathf.Clamp(Mathf.CeilToInt(len / (r * 0.3f)), 1, 64);
            float each = amount / Mathf.Sqrt(steps);
            for (int s = 0; s <= steps; s++)
            {
                Vector2 c = Vector2.Lerp(pa, pb, s / (float)steps);
                Dab(side, c, dir, r, each, seed);
            }
        }

        private Vector2 Px(int side, Vector3 local)
        {
            Vector2 uv = UV(side, local.x, local.z);
            return new Vector2(uv.x * _tw, uv.y * _th);
        }

        /// <summary>Один «мазок маслом»: эллипс вдоль движения, полосы от щетины поперёк, рваный край.</summary>
        private void Dab(int side, Vector2 c, Vector2 dir, float r, float amount, int seed)
        {
            Vector2 perp = new Vector2(-dir.y, dir.x);
            float along = r * 1.35f, across = r;
            int x0 = Mathf.FloorToInt(c.x - along - 1), x1 = Mathf.CeilToInt(c.x + along + 1);
            int y0 = Mathf.FloorToInt(c.y - along - 1), y1 = Mathf.CeilToInt(c.y + along + 1);
            int sx0 = side * _sw, sx1 = sx0 + _sw - 1;
            x0 = Mathf.Max(x0, sx0); x1 = Mathf.Min(x1, sx1);
            y0 = Mathf.Max(y0, 0); y1 = Mathf.Min(y1, _th - 1);
            if (x0 > x1 || y0 > y1) return;
            float bristleK = _kind == Kind.Glaze ? 0.55f : 0.35f;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 d = new Vector2(x + 0.5f - c.x, y + 0.5f - c.y);
                    float pa = Vector2.Dot(d, dir) / along, pc = Vector2.Dot(d, perp) / across;
                    float q = pa * pa + pc * pc;
                    // край «рвётся» шумом — не ровный круг
                    float edge = 1f + 0.35f * (Smooth(x * 0.4f, y * 0.4f, seed & 7) - 0.5f);
                    if (q >= edge) continue;
                    float f = 1f - q / edge;
                    f *= f;
                    // полосы щетины: зависят от смещения поперёк мазка (вдоль — тянутся)
                    float streak = Smooth(Vector2.Dot(new Vector2(x, y), perp) * 0.9f, seed * 0.37f, 13);
                    float add = amount * f * (1f - bristleK + bristleK * 2f * streak);
                    int i = y * _tw + x;
                    float m = _mask[i];
                    float nm = Mathf.Min(1.6f, m + add * (1.25f - Mathf.Min(m, 1f) * 0.5f));
                    if (nm > m) { _mask[i] = nm; Mark(x, y); }
                }
        }

        /// <summary>Клетки (правда с сервера) → маска: где клетка покрыта, а нарисовано мало — дорисовать пятнами.</summary>
        public void FromCells(SyncListReader cells, int side)
        {
            if (_mask == null || cells == null) return;
            int sx0 = side * _sw;
            for (int r = 0; r < _rows; r++)
                for (int col = 0; col < _ribs; col++)
                {
                    float want = cells(side, r, col);
                    if (want <= 0.02f) continue;
                    int x0 = sx0 + col * _sw / _ribs, x1 = sx0 + (col + 1) * _sw / _ribs;
                    int y0 = r * _th / _rows, y1 = (r + 1) * _th / _rows;
                    float sum = 0f; int n = 0;
                    for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) { sum += Mathf.Min(1f, _mask[y * _tw + x]); n++; }
                    float have = n > 0 ? sum / n : 0f;
                    if (have >= want * 0.8f - 0.05f) continue;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++)
                        {
                            int i = y * _tw + x;
                            float blob = Smooth(x * 0.15f, y * 0.15f, 21 + side);
                            float target = want * (0.65f + 0.7f * blob);
                            if (target > _mask[i]) { _mask[i] = target; Mark(x, y); }
                        }
                }
        }

        public delegate float SyncListReader(int side, int row, int col);

        /// <summary>Смыть всё (например, рёбра поменялись).</summary>
        public void Clear()
        {
            if (_mask == null) return;
            System.Array.Clear(_mask, 0, _mask.Length);
            MarkAll();
        }

        // ───────────────────────── в текстуру ─────────────────────────

        private void Mark(int x, int y)
        {
            if (x < _dx0) _dx0 = x; if (x > _dx1) _dx1 = x;
            if (y < _dy0) _dy0 = y; if (y > _dy1) _dy1 = y;
        }

        private void MarkAll() { _dx0 = 0; _dy0 = 0; _dx1 = _tw - 1; _dy1 = _th - 1; }

        /// <summary>Перенести изменения маски в текстуру (раз в кадр, только грязный прямоугольник).</summary>
        public void Flush()
        {
            if (_tex == null || _dx1 < _dx0 || _base.r < 0f) return;
            bool rub = _kind == Kind.Rub;
            for (int y = _dy0; y <= _dy1; y++)
                for (int x = _dx0; x <= _dx1; x++)
                {
                    int i = y * _tw + x;
                    float m = _mask[i];
                    float a;
                    Color c = _alb[i];
                    if (rub)
                    {
                        // тонко — песчинки отдельно (рвётся по зерну), толсто — сплошная сочная шапка, чуть темнее
                        a = Mathf.Clamp01((m * _grain[i] - 0.18f) / 0.42f);
                        a = a * a * (3f - 2f * a) * 0.98f;
                        float thick = Mathf.Clamp01((m - 0.8f) / 0.8f);
                        c *= Mathf.Lerp(1f, 0.82f, thick);
                    }
                    else
                    {
                        a = Mathf.Clamp01((m * _grain[i] - 0.1f) / 0.45f);
                        a = a * a * (3f - 2f * a) * 0.93f;
                        float thick = Mathf.Clamp01((m - 0.6f) / 1f);
                        c *= Mathf.Lerp(1.05f, 0.78f, thick);   // где гуще — темнее
                    }
                    c *= _tint;
                    _px[i] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), (byte)(a * 255f));
                }
            _tex.SetPixels32(_px);
            _tex.Apply(true);
            _dx0 = _dy0 = int.MaxValue; _dx1 = _dy1 = -1;
        }

        // ───────────────────────── горка ─────────────────────────

        /// <summary>Материал горки специй: та же зернистая текстура, непрозрачно.</summary>
        public Material PileMaterial(Material template)
        {
            if (_alb == null || _base.r < 0f) return null;
            if (_pileTex != null && _pileBase != _base)
            {
                _pileBase = _base;
                var q = new Color[64 * 64];
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) q[y * 64 + x] = _alb[(y % _th) * _tw + (x % _tw)];
                _pileTex.SetPixels(q); _pileTex.Apply(true);
            }
            if (_pileTex == null)
            {
                _pileBase = _base;
                _pileTex = new Texture2D(64, 64, TextureFormat.RGBA32, true) { name = _name + " pile (auto)", wrapMode = TextureWrapMode.Repeat };
                var p = new Color[64 * 64];
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) p[y * 64 + x] = _alb[(y % _th) * _tw + (x % _tw)];
                _pileTex.SetPixels(p); _pileTex.Apply(true);
                Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                _pileMat = sh != null ? new Material(sh) : (template != null ? new Material(template) : null);
                if (_pileMat != null)
                {
                    _pileMat.name = _name + " pile (auto)";
                    _pileMat.SetTexture("_BaseMap", _pileTex); _pileMat.mainTexture = _pileTex;
                    _pileMat.SetColor("_BaseColor", Color.white);
                    _pileMat.SetFloat("_Smoothness", 0.15f);
                }
            }
            return _pileMat;
        }

        private Texture2D _pileTex;
        private Material _pileMat;
        private Color _pileBase;

        public void DestroyPile()
        {
            if (_pileTex != null) SafeDestroy.Kill(_pileTex);
            if (_pileMat != null) SafeDestroy.Kill(_pileMat);
        }

        private static Mesh _mound;

        /// <summary>Горка сыпучего: бугристый конус (одна на всех).</summary>
        public static Mesh Mound()
        {
            if (_mound != null) return _mound;
            const int rings = 8, segs = 28;
            var v = new System.Collections.Generic.List<Vector3>();
            var uv = new System.Collections.Generic.List<Vector2>();
            var t = new System.Collections.Generic.List<int>();
            v.Add(new Vector3(0f, 1f, 0f)); uv.Add(new Vector2(0.5f, 0.5f));
            for (int r = 1; r <= rings; r++)
            {
                float rr = r / (float)rings;
                for (int s = 0; s < segs; s++)
                {
                    float a = s / (float)segs * Mathf.PI * 2f;
                    float bump = 1f + 0.16f * (Smooth(Mathf.Cos(a) * 3f + 5f, Mathf.Sin(a) * 3f + rr * 4f, 2) - 0.5f) * 2f;
                    float rad = rr * bump;
                    float h = Mathf.Pow(Mathf.Max(0f, 1f - rr * rr), 0.85f) * (1f + 0.1f * (H(r, s, 3) - 0.5f));
                    v.Add(new Vector3(Mathf.Cos(a) * rad, h, Mathf.Sin(a) * rad));
                    uv.Add(new Vector2(0.5f + Mathf.Cos(a) * rr * 0.5f, 0.5f + Mathf.Sin(a) * rr * 0.5f));
                }
            }
            for (int s = 0; s < segs; s++) { t.Add(0); t.Add(1 + (s + 1) % segs); t.Add(1 + s); }
            for (int r = 1; r < rings; r++)
                for (int s = 0; s < segs; s++)
                {
                    int a = 1 + (r - 1) * segs + s, b = 1 + (r - 1) * segs + (s + 1) % segs;
                    int c = a + segs, d = b + segs;
                    t.Add(a); t.Add(b); t.Add(d); t.Add(a); t.Add(d); t.Add(c);
                }
            // все грани — наружу (вверх)
            for (int k = 0; k + 2 < t.Count; k += 3)
            {
                Vector3 a0 = v[t[k]], b0 = v[t[k + 1]], c0 = v[t[k + 2]];
                Vector3 nrm = Vector3.Cross(b0 - a0, c0 - a0);
                Vector3 mid = (a0 + b0 + c0) / 3f;
                Vector3 outDir = new Vector3(mid.x, 0.35f, mid.z);
                if (Vector3.Dot(nrm, outDir) < 0f) { int tmp = t[k + 1]; t[k + 1] = t[k + 2]; t[k + 2] = tmp; }
            }
            _mound = new Mesh { name = "SpiceMound (auto)" };
            _mound.SetVertices(v); _mound.SetUVs(0, uv); _mound.SetTriangles(t, 0);
            _mound.RecalculateNormals(); _mound.RecalculateBounds();
            return _mound;
        }
    }
}
