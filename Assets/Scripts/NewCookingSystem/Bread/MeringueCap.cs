using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// МЕРЕНГОВАЯ ШАПКА «УЛЕЙ» — купол, по которому спиралью идёт отсаженный жгут меренги:
    /// белые бороздки и подрумяненные гребни, как у Запечённой Аляски.
    ///
    /// ═══ ФОРМА ═══
    ///   Круглая (Base Half = 0): купол радиуса Radius, высота Radius × Height K.
    ///   Над бисквитом (Base Half задан): основание чуть квадратное (Base Squareness),
    ///   невысокие стенки до Wall Height, выше — круглый купол до Height.
    ///
    /// ═══ РАСТЕКАНИЕ (Spread 0..1) — как плавленый сыр ═══
    ///   Меренга — толстый слой ПОВЕРХ того, что под ней (Support: кубик бисквита + горка
    ///   мороженого). Она никогда не уходит внутрь бисквита — не «краска», а масса.
    ///   0 — комок лежит на мороженом; дальше слой оплывает вниз, кромка — круглый
    ///   толстый «нос» с подтёками, переваливает через край бисквита и доходит до тарелки;
    ///   в конце слой пушится в купол-улей со спиральным жгутом.
    ///   Растекание ведёт DessertPlate (по сети).
    ///
    /// ═══ ПОДПАЛ ═══
    ///   У каждой вершины свой: гребни темнеют первыми, бороздки остаются белыми.
    ///   Toast — весь купол разом (ползунок для проверки);
    ///   AddToastAt — пятно; Toast By Jets — сама румянится от горелок (IHeatJet),
    ///   куда бьёт пламя и только с той стороны, что смотрит на сопло.
    ///   Струи горелки есть у всех игроков — подпал считается у каждого сам, по сети не идёт.
    ///
    /// Цвет — в вершинах, под шейдер «CatCooks/Food Heap». Меш строится сам (видно и в редакторе).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class MeringueCap : MonoBehaviour
    {
        /// <summary>v23.47: вид шапки — «улей» со спиральным жгутом или «ёжики» (много отсаженных острых пиков-завитков).</summary>
        public enum CapStyle { Hive = 0, Spiky = 1, Piped = 2 }   // v23.48: Piped — гладкий купол, хвостики отсаживают рукавом (SetGrow)

        [Header("v23.47: вид шапки")]
        [Tooltip("Улей — спиральный жгут. Ёжики — купол из отсаженных острых пиков с завитком, кончики румянятся первыми. " +
                 "Рукав (Piped) — гладкий купол, хвостики появляются там, где их отсадили кондитерским рукавом")]
        public CapStyle Style = CapStyle.Hive;
        [Tooltip("Ёжики: рядов пиков снизу доверху")]
        [Range(3, 12)] public int PeakRows = 5;
        [Tooltip("Ёжики: высота пика (доля радиуса)")]
        [Range(0.05f, 0.6f)] public float PeakHeight = 0.36f;
        [Tooltip("Ёжики: насколько кончики загибаются вверх (доля радиуса)")]
        [Range(0f, 0.4f)] public float PeakLean = 0.2f;
        [Tooltip("Ёжики: бороздки от звёздчатой насадки на каждом пике")]
        [Range(0f, 0.3f)] public float PeakGrooves = 0.12f;

        [Header("Форма — улей со спиралью")]
        [Tooltip("Радиус основания (круглая шапка), в осях объекта")]
        [Min(0.005f)] public float Radius = 0.25f;
        [Tooltip("Высота от радиуса (1 — полусфера, больше — улей). Только для круглой.")]
        [Range(0.5f, 1.8f)] public float HeightK = 1.2f;
        [Tooltip("Над бисквитом: половина ширины основания по X и Z. 0 — круглая шапка.")]
        public Vector2 BaseHalf = Vector2.zero;
        [Tooltip("Над бисквитом: высота стенок (бисквит), выше — купол")]
        [Min(0f)] public float WallHeight = 0f;
        [Tooltip("Над бисквитом: полная высота шапки")]
        [Min(0f)] public float Height = 0f;
        [Tooltip("Витков спирали снизу доверху")]
        [Range(2f, 14f)] public float Turns = 6.5f;
        [Tooltip("Глубина бороздок (доля радиуса)")]
        [Range(0f, 0.3f)] public float RidgeDepth = 0.16f;
        [Tooltip("Округлость жгута: меньше — полнее жгут и уже бороздка")]
        [Range(0.2f, 1f)] public float RidgeRound = 0.45f;
        [Tooltip("Неровность, как от кондитерского мешка")]
        [Range(0f, 0.05f)] public float Wobble = 0.015f;
        [Tooltip("Юбочка у основания")]
        [Range(0f, 0.25f)] public float Skirt = 0.07f;
        [Tooltip("Хохолок на макушке (доля радиуса)")]
        [Range(0f, 0.3f)] public float TopCurl = 0.12f;
        [Range(16, 96)] public int Rings = 56;
        [Range(32, 192)] public int Segments = 112;
        public int Seed = 1;

        [Tooltip("Над бисквитом: квадратность основания (2 — круг, 3 — чуть квадратное, 6 — почти квадрат)")]
        [Range(2f, 8f)] public float BaseSquareness = 3f;

        [Header("Что под меренгой (по нему она стекает)")]
        [Tooltip("Кубик бисквита: половина X, высота, половина Z. Стоит на основании (y = 0). 0 — нет")]
        public Vector3 SupportBox = Vector3.zero;
        [Tooltip("Горка мороженого на кубике: радиус, высота. 0 — нет")]
        public Vector2 SupportIce = Vector2.zero;
        [Tooltip("Слой меренги не тоньше этого (доля Radius)")]
        [Range(0.02f, 0.5f)] public float MinThickness = 0.12f;

        [Header("Растекание")]
        [Tooltip("0 — комок на мороженом, 1 — стекла до низа и закрыла бисквит куполом")]
        [Range(0f, 1f)] public float Spread = 1f;
        [Tooltip("Если под меренгой ничего нет: с какой высоты (доля) начинает стекать")]
        [Range(0.3f, 0.95f)] public float SpreadFrom = 0.72f;
        [Tooltip("Подтёки на кромке, пока стекает")]
        [Range(0f, 0.1f)] public float Drips = 0.05f;

        [Header("Подпал")]
        [Range(0f, 1f)][SerializeField] private float _toast;
        [Tooltip("Насколько гребни темнее бороздок")]
        [Range(0.5f, 4f)] public float TipBias = 2.2f;
        [Tooltip("Сила узора текстуры (пузырьки пены) — альфа вершин для шейдера Food Heap")]
        [Range(0f, 1f)] public float DetailAlpha = 0.7f;
        [Tooltip("Румяниться от горелок самой")]
        public bool ToastByJets = true;
        [Tooltip("Подпал в секунду в центре пламени обычной горелки")]
        [Range(0.01f, 3f)] public float ToastRate = 0.16f;
        [Tooltip("v23.50: пятно пламени шире во столько раз (быстрее румянится весь купол)")]
        [Range(1f, 4f)] public float ToastSpot = 1f;
        public Color[] Ramp =
        {
            new Color(0.99f, 0.97f, 0.93f), new Color(0.98f, 0.90f, 0.74f), new Color(0.90f, 0.66f, 0.38f),
            new Color(0.66f, 0.38f, 0.17f), new Color(0.36f, 0.20f, 0.10f), new Color(0.16f, 0.11f, 0.08f),
        };

        private Mesh _mesh;
        private Vector3[] _v, _nrm;
        private Vector2[] _uv;
        private float[] _tip, _noise, _vt;
        private Color[] _col;
        private int _rings, _segs, _sideCount;
        private bool _topoDirty = true, _shapeDirty = true, _colorDirty = true;
        private float _colorAt, _jetAt, _shapeAt;
        private float _shownSpread = -1f;
        private float _growAt;

        /// <summary>Подпал всего купола (0 — белая, 1 — уголь).</summary>
        public float Toast
        {
            get => _toast;
            set { _toast = Mathf.Clamp01(value); if (_vt != null) for (int i = 0; i < _vt.Length; i++) _vt[i] = _toast; _colorDirty = true; }
        }

        /// <summary>Средний подпал по видимой части купола.</summary>
        public float AverageToast
        {
            get
            {
                if (_vt == null || _sideCount == 0) return _toast;
                float s = 0f;
                for (int i = 0; i < _sideCount; i++) s += _vt[i];
                return s / _sideCount;
            }
        }

        /// <summary>Доля купола, подрумяненная хотя бы до золотистого (≥ 0.3).</summary>
        public float ToastedShare
        {
            get
            {
                if (_vt == null || _sideCount == 0) return _toast >= 0.3f ? 1f : 0f;
                int n = 0;
                for (int i = 0; i < _sideCount; i++) if (_vt[i] >= 0.3f) n++;
                return n / (float)_sideCount;
            }
        }

        /// <summary>Горелка: подрумянить пятно вокруг точки (мир). amount — сколько добавить в центре пятна.</summary>
        public void AddToastAt(Vector3 worldPoint, float worldRadius, float amount)
        {
            if (_v == null || _vt == null) return;
            Vector3 lp = transform.InverseTransformPoint(worldPoint);
            float r = Mathf.Max(1e-4f, worldRadius / Mathf.Max(1e-4f, MaxScale()));
            for (int i = 0; i < _sideCount; i++)
            {
                float d = (_v[i] - lp).magnitude / r;
                if (d >= 1f) continue;
                _vt[i] = Mathf.Clamp01(_vt[i] + amount * (1f - d * d));
            }
            _colorDirty = true;
        }

        /// <summary>Форму поменяли из кода (размеры) — перестроить.</summary>
        public void MarkShapeDirty()
        {
            if (Rings != _rings || Segments != _segs) _topoDirty = true;
            else InvalidateFinal();
        }

        private float MaxScale()
        {
            Vector3 s = transform.lossyScale;
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
        }

        private void OnEnable()
        {
            _topoDirty = true;
            if (!Application.isPlaying) Rebuild();
        }

        private void OnValidate()
        {
            _topoDirty = true; _colorDirty = true; _fValid = false;
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        private void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
        }

        private void Update()
        {
            if (_topoDirty) { Rebuild(); return; }
            float now = Time.unscaledTime;
            if (_growAnim && (now >= _shapeAt || !Application.isPlaying)) { StepGrow(Application.isPlaying ? Mathf.Clamp(now - _growAt, 0.02f, 0.2f) : 1f); _growAt = now; }
            else if (!_growAnim) _growAt = now;
            if (Mathf.Abs(Spread - _shownSpread) > 0.002f && (now >= _shapeAt || !Application.isPlaying))
            {
                _shapeAt = now + 0.05f;
                Shape();
            }
            if (ToastByJets && Application.isPlaying && now >= _jetAt)
            {
                float dt = _jetAt > 0f ? Mathf.Min(0.3f, now - _jetAt + 0.1f) : 0.1f;
                _jetAt = now + 0.1f;
                JetTick(dt);
            }
            if (_colorDirty && (now >= _colorAt || !Application.isPlaying))
            {
                _colorAt = now + 0.06f;
                Paint();
            }
        }

        // ───────────────────────── МЕШ ─────────────────────────

        private bool Boxy => BaseHalf.x > 0.001f && BaseHalf.y > 0.001f && Height > 0.001f;
        private float RefR => Boxy ? Mathf.Min(BaseHalf.x, BaseHalf.y) : Radius;
        private float FullH => Boxy ? Height : Radius * HeightK;

        /// <summary>Сетка и треугольники (меняются только при смене Rings/Segments).</summary>
        private void Rebuild()
        {
            _topoDirty = false;
            _rings = Mathf.Max(8, Rings); _segs = Mathf.Max(16, Segments);
            int side = (_rings + 1) * (_segs + 1);
            int total = side + _segs + 2;
            _sideCount = side;
            float[] oldVt = _vt;
            _v = new Vector3[total]; _nrm = new Vector3[total]; _uv = new Vector2[total];
            _tip = new float[total]; _noise = new float[total]; _vt = new float[total]; _col = new Color[total];
            for (int i = 0; i < total; i++) _vt[i] = oldVt != null && oldVt.Length == total && Application.isPlaying ? oldVt[i] : _toast;

            var tris = new int[_rings * _segs * 6 + _segs * 3];
            int ti = 0;
            for (int i = 0; i < _rings; i++)
                for (int j = 0; j < _segs; j++)
                {
                    int a = i * (_segs + 1) + j, b = a + _segs + 1;
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = a + 1;
                    tris[ti++] = a + 1; tris[ti++] = b; tris[ti++] = b + 1;
                }
            int c0 = side;
            for (int j = 0; j < _segs; j++) { tris[ti++] = c0; tris[ti++] = c0 + 1 + j; tris[ti++] = c0 + 2 + j; }

            if (_mesh == null) { _mesh = new Mesh { name = "MeringueCap (auto)" }; _mesh.hideFlags = HideFlags.DontSave; }
            _mesh.Clear();
            if (total > 65000) _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _fValid = false;
            _shownSpread = -1f;
            Shape();                        // вершины
            _mesh.triangles = tris;
            _mesh.uv = _uv;
            Normals();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _colorDirty = true;
            Paint();
        }

        /// <summary>Расстояние до края основания под углом th: суперэллипс (n=2 — эллипс, 6 — почти квадрат).</summary>
        private static float Foot(float th, float a, float b, float n)
        {
            float c = Mathf.Abs(Mathf.Cos(th)) / a, s = Mathf.Abs(Mathf.Sin(th)) / b;
            float sum = Mathf.Pow(c, n) + Mathf.Pow(s, n);
            return sum > 1e-8f ? Mathf.Pow(sum, -1f / n) : Mathf.Max(a, b);
        }

        /// <summary>Точка поверхности купола: t — высота 0..1, a — угол 0..1.</summary>
        private Vector3 Surf(float t, float a, float ax, float az, float wallT, float H, float R, bool boxy, float turns, out float tip, out float noise)
        {
            float th = a * Mathf.PI * 2f;
            float g, nExp;
            if (t <= wallT)
            {
                g = 1f + Skirt * Mathf.Pow(1f - t / Mathf.Max(1e-4f, wallT), 6f);
                nExp = boxy ? BaseSquareness : 2f;
            }
            else
            {
                float s = (t - wallT) / Mathf.Max(1e-4f, 1f - wallT);
                g = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(s, 2.4f)), 0.55f);
                if (wallT <= 0f) g *= 1f + Skirt * Mathf.Pow(1f - s, 6f);
                nExp = boxy ? Mathf.Lerp(BaseSquareness, 2f, Mathf.SmoothStep(0f, 1f, s * 2.5f)) : 2f;   // выше — круглее
            }
            float fade = Mathf.Clamp01((1f - t) * 9f);
            float curl = TopCurl * R * Mathf.Pow(Mathf.Max(0f, (t - 0.86f) / 0.14f), 2f);
            float baseR = Foot(th, ax, az, nExp) * g;
            noise = Mathf.PerlinNoise(Seed * 13.37f + Mathf.Cos(th) * 1.7f + t * 3f, Seed * 7.13f + Mathf.Sin(th) * 1.7f + t * 5f) - 0.5f;
            if (Style == CapStyle.Spiky || Style == CapStyle.Piped)
            {
                // v23.47: ЁЖИКИ — пики отсажены рядами по куполу, острые, кончик загнут вверх, на боках бороздки от насадки
                float pk = Spiky(t, a, H, (ax + az) * 0.5f, wallT, out float lift);
                float amp = PeakHeight * R * (0.3f + 0.7f * _ridgeK);
                float rS = baseR * (Style == CapStyle.Piped ? 0.97f : 0.94f) + amp * pk + Wobble * R * noise * 2f;
                tip = Mathf.Clamp01(pk * 1.15f);
                return new Vector3(rS * Mathf.Cos(th), H * t + PeakLean * R * lift * (0.3f + 0.7f * _ridgeK), rS * Mathf.Sin(th));
            }
            float s2 = t * turns + a;
            float p = s2 - Mathf.Floor(s2);
            float bump = Mathf.Pow(Mathf.Sin(Mathf.PI * p), RidgeRound);
            float r = baseR + RidgeDepth * _ridgeK * R * bump * (0.35f + 0.65f * Mathf.Min(1f, g)) * fade + Wobble * R * noise * 2f;
            tip = bump * fade + (1f - fade) * 0.85f;
            return new Vector3(r * Mathf.Cos(th), H * t + curl, r * Mathf.Sin(th));
        }

        private static float Hash(float x) { float s = Mathf.Sin(x * 12.9898f + 78.233f) * 43758.5453f; return s - Mathf.Floor(s); }

        private static float GFun(float t, float wallT)
        {
            if (t <= wallT) return 1f;
            float s = (t - wallT) / Mathf.Max(1e-4f, 1f - wallT);
            return Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(s, 2.4f)), 0.55f);
        }

        /// <summary>
        /// v23.47: «ёжики» в точке (t — высота 0..1, a — угол 0..1): 0 — впадина между пиками, ~1 — кончик пика.
        /// Пики стоят рядами, в ряду их столько, сколько влезает по окружности; ряды сдвинуты в шахматку, чуть случайно.
        /// lift — насколько кончик загнут вверх (только у самого кончика).
        /// </summary>
        private float Spiky(float t, float a, float H, float avgA, float wallT, out float lift)
        {
            lift = 0f;
            int rows = Mathf.Max(2, PeakRows);
            const float t0 = 0.03f, t1 = 0.97f;
            float dtR = (t1 - t0) / rows;
            int r0 = Mathf.FloorToInt((t - t0) / dtR - 0.5f);
            float best = 0f;
            for (int k = r0 - 1; k <= r0 + 2; k++)
            {
                if (k < 0 || k >= rows) continue;
                float tk = t0 + (k + 0.5f) * dtR;
                float radk = Mathf.Max(1e-4f, avgA * GFun(tk, wallT));
                float circ = 2f * Mathf.PI * radk;
                float rowSp = Mathf.Max(1e-4f, dtR * H * 1.15f);
                int n = Mathf.Max(3, Mathf.RoundToInt(circ / rowSp));
                bool piped = Style == CapStyle.Piped;
                int baseIdx = piped && _rowStart != null && k < _rowStart.Length ? _rowStart[k] : -1;
                float off = (k % 2) * 0.5f + Hash(k * 7.31f + Seed) * 0.3f;
                float ai = a * n - off;
                int i0 = Mathf.RoundToInt(ai);
                float w = 0.75f * Mathf.Max(rowSp, circ / n);
                for (int ii = i0 - 1; ii <= i0 + 1; ii++)
                {
                    int iw = ((ii % n) + n) % n;                         // шов a = 0/1 — те же пики
                    float gk = 1f;
                    if (piped)
                    {
                        int gi = baseIdx + iw;
                        gk = baseIdx >= 0 && _growShown != null && gi < _growShown.Length ? _growShown[gi] : 0f;
                        if (gk <= 0.001f) continue;                      // тут ещё не отсадили
                    }
                    float hs = Hash(k * 13.1f + iw * 3.7f + Seed * 1.9f);
                    float jit = (hs - 0.5f) * 0.5f;
                    float jitT = (Hash(k * 5.7f + iw * 11.3f + Seed) - 0.5f) * dtR * 0.35f;
                    float sizeK = 0.75f + 0.5f * Hash(k * 2.3f + iw * 17.9f + Seed * 0.7f);
                    float du = (ai - ii - jit) / n * circ;
                    float dv = (t - tk - jitT) * H;
                    float dvs = dv * (dv > 0f ? 0.65f : 1.25f);           // пик как язычок пламени: снизу полого, кончик тянется вверх
                    float d = Mathf.Sqrt(du * du + dvs * dvs) / (w * sizeK);
                    if (d >= 1f) continue;
                    float s = 1f - d;
                    float ang = Mathf.Atan2(dv, du);
                    float groove = Mathf.Cos(ang * 6f + d * 5f + hs * 6.28f) * PeakGrooves * s * (1f - s);   // насадка-звёздочка, к кончику — гладко
                    float v = (Mathf.Pow(s, 1.5f) + groove) * sizeK * gk;
                    if (v > best) { best = v; lift = Mathf.Pow(s, 4f) * sizeK * gk; }
                }
            }
            return best;
        }

        // ───────────────────────── v23.48: ХВОСТИКИ РУКАВОМ ─────────────────────────

        private int[] _rowStart;
        private int _peakTotal;
        private float[] _grow, _growShown;
        private float _lH, _lA, _lWall, _lR, _lMinT;
        private bool _layoutOk, _growAnim;

        /// <summary>Сколько мест под хвостики (пики) на куполе.</summary>
        public int PeakCount { get { EnsureLayout(); return _peakTotal; } }

        /// <summary>Расстояние между соседними хвостиками (оси объекта).</summary>
        public float PeakSpacing { get { EnsureLayout(); int rows = Mathf.Max(2, PeakRows); return (0.97f - 0.03f) / rows * _lH * 1.15f; } }

        /// <summary>Доля мест, где хвостик отсажен хотя бы наполовину.</summary>
        public float PipedShare
        {
            get
            {
                EnsureLayout();
                if (_grow == null || _peakTotal == 0) return 0f;
                int n = 0;
                for (int i = 0; i < _peakTotal; i++) if (_grow[i] >= 0.5f) n++;
                return n / (float)_peakTotal;
            }
        }

        /// <summary>Хвостик i вырос до g (0..1). Растёт плавно.</summary>
        public void SetGrow(int i, float g)
        {
            EnsureLayout();
            if (_grow == null || i < 0 || i >= _grow.Length) return;
            g = Mathf.Clamp01(g);
            if (Mathf.Abs(_grow[i] - g) < 1e-4f) return;
            _grow[i] = g;
            _growAnim = true;
        }

        public float GetGrow(int i) => _grow != null && i >= 0 && i < _grow.Length ? _grow[i] : 0f;

        /// <summary>Убрать все хвостики (меренгу убрали).</summary>
        public void ClearGrow()
        {
            if (_grow != null) System.Array.Clear(_grow, 0, _grow.Length);
            if (_growShown != null) System.Array.Clear(_growShown, 0, _growShown.Length);
            _shownSpread = -1f;
        }

        /// <summary>Раскладка мест под хвостики по готовому куполу (одинаково у всех: зависит только от размеров).</summary>
        private void EnsureLayout()
        {
            float H = FullH, R = RefR;
            bool boxy = Boxy;
            float wallT = boxy ? Mathf.Clamp(WallHeight / Mathf.Max(1e-4f, H), 0f, 0.8f) : 0f;
            float avgA = boxy ? (BaseHalf.x + BaseHalf.y) * 0.5f : Radius;
            if (_layoutOk && Mathf.Abs(H - _lH) < 1e-5f && Mathf.Abs(avgA - _lA) < 1e-5f && Mathf.Abs(wallT - _lWall) < 1e-5f
                && _rowStart != null && _rowStart.Length == Mathf.Max(2, PeakRows)) return;
            _layoutOk = true; _lH = H; _lA = avgA; _lWall = wallT; _lR = R; _lMinT = MinThickness * R;
            int rows = Mathf.Max(2, PeakRows);
            const float t0 = 0.03f, t1 = 0.97f;
            float dtR = (t1 - t0) / rows;
            _rowStart = new int[rows];
            int total = 0;
            for (int k = 0; k < rows; k++)
            {
                _rowStart[k] = total;
                float tk = t0 + (k + 0.5f) * dtR;
                float circ = 2f * Mathf.PI * Mathf.Max(1e-4f, avgA * GFun(tk, wallT));
                float rowSp = Mathf.Max(1e-4f, dtR * H * 1.15f);
                total += Mathf.Max(3, Mathf.RoundToInt(circ / rowSp));
            }
            float[] old = _grow;
            _peakTotal = total;
            _grow = new float[total];
            _growShown = new float[total];
            if (old != null) for (int i = 0; i < Mathf.Min(old.Length, total); i++) { _grow[i] = old[i]; _growShown[i] = old[i]; }
        }

        /// <summary>Центр места под хвостик i на готовом куполе (оси объекта). false — нет такого.</summary>
        public bool PeakLocal(int i, out Vector3 pos, out Vector3 nrm)
        {
            pos = Vector3.zero; nrm = Vector3.up;
            EnsureLayout();
            if (i < 0 || i >= _peakTotal) return false;
            int rows = _rowStart.Length, k = rows - 1;
            for (int q = 1; q < rows; q++) if (i < _rowStart[q]) { k = q - 1; break; }
            int iw = i - _rowStart[k];
            int n = (k + 1 < rows ? _rowStart[k + 1] : _peakTotal) - _rowStart[k];
            const float t0 = 0.03f, t1 = 0.97f;
            float dtR = (t1 - t0) / rows;
            float tk = t0 + (k + 0.5f) * dtR;
            float hs = Hash(k * 13.1f + iw * 3.7f + Seed * 1.9f);
            float jit = (hs - 0.5f) * 0.5f;
            float jitT = (Hash(k * 5.7f + iw * 11.3f + Seed) - 0.5f) * dtR * 0.35f;
            float off = (k % 2) * 0.5f + Hash(k * 7.31f + Seed) * 0.3f;
            float a = (iw + jit + off) / n;
            float t = Mathf.Clamp01(tk + jitT);
            float th = a * Mathf.PI * 2f;
            bool boxy = Boxy;
            float ax = boxy ? BaseHalf.x : Radius, az = boxy ? BaseHalf.y : Radius;
            float nExp = 2f;
            if (boxy) nExp = t <= _lWall ? BaseSquareness : Mathf.Lerp(BaseSquareness, 2f, Mathf.SmoothStep(0f, 1f, (t - _lWall) / Mathf.Max(1e-4f, 1f - _lWall) * 2.5f));
            float r = Foot(th, ax, az, nExp) * GFun(t, _lWall) * 0.97f;
            Vector3 p = new Vector3(r * Mathf.Cos(th), _lH * t, r * Mathf.Sin(th));
            Vector3 v = p - _c;
            float rF = v.magnitude;
            Vector3 d = rF > 1e-6f ? v / rF : Vector3.up;
            float rS = SupportDist(d);
            if (rF < rS + _lMinT) p = _c + d * (rS + _lMinT);
            pos = p;
            Vector3 hz = new Vector3(p.x, 0f, p.z);
            nrm = (hz.normalized * (1f - t) + Vector3.up * t).normalized;
            return true;
        }

        private void StepGrow(float dt)
        {
            if (!_growAnim || _grow == null || _growShown == null) return;
            bool more = false;
            bool full = !_fValid || _fD == null;
            for (int i = 0; i < _grow.Length; i++)
            {
                if (_growShown[i] == _grow[i]) continue;
                _growShown[i] = Mathf.MoveTowards(_growShown[i], _grow[i], dt * 6f);     // v23.50: хвостик выдавливается ~0,15 с
                if (_growShown[i] != _grow[i]) more = true;
                if (!full) RecomputePeak(i);                              // v23.50: только окно вокруг хвостика
            }
            _growAnim = more;
            _shownSpread = -1f;                                           // перестроить форму (дёшево, из кэша)
            _colorDirty = true;                                           // кончики хвостиков румянятся первыми
        }

        /// <summary>v23.50: пересчитать кэш купола только вокруг хвостика i (а не все вершины).</summary>
        private void RecomputePeak(int i)
        {
            if (_rowStart == null || i < 0 || i >= _peakTotal || _fD == null) return;
            int rows = _rowStart.Length, k = rows - 1;
            for (int q = 1; q < rows; q++) if (i < _rowStart[q]) { k = q - 1; break; }
            int iw = i - _rowStart[k];
            int n = (k + 1 < rows ? _rowStart[k + 1] : _peakTotal) - _rowStart[k];
            const float t0 = 0.03f, t1 = 0.97f;
            float dtR = (t1 - t0) / rows;
            float tk = t0 + (k + 0.5f) * dtR;
            float off = (k % 2) * 0.5f + Hash(k * 7.31f + Seed) * 0.3f;
            float jit = (Hash(k * 13.1f + iw * 3.7f + Seed * 1.9f) - 0.5f) * 0.5f;
            float ac = (iw + jit + off) / n;
            float circ = 2f * Mathf.PI * Mathf.Max(1e-4f, _lA * GFun(tk, _lWall));
            float rowSp = Mathf.Max(1e-4f, dtR * _lH * 1.15f);
            float w = 0.75f * Mathf.Max(rowSp, circ / n) * 1.3f;          // радиус пика с запасом (size до 1.25)
            float tUp = w / 0.65f / Mathf.Max(1e-4f, _lH), tDn = w / Mathf.Max(1e-4f, _lH) + dtR * 0.2f;
            float aW = w / Mathf.Max(1e-4f, circ) + 0.5f / n;
            int i0 = Mathf.FloorToInt((tk - tDn) * _rings) - 1, i1 = Mathf.CeilToInt((tk + tUp) * _rings) + 1;
            int j0 = Mathf.FloorToInt((ac - aW) * _segs) - 1, j1 = Mathf.CeilToInt((ac + aW) * _segs) + 1;
            ComputeFinal(i0, i1, j0, j1, true);
        }

        // ───────────────────────── ОПОРА: кубик + мороженое ─────────────────────────

        private Vector3 _c;           // центр лучей — середина кубика (внутри опоры)
        private float _ridgeK = 1f;   // жгут проступает, пока меренга оседает

        /// <summary>Сколько от центра до поверхности опоры по направлению d (выход из кубика ∪ горки).</summary>
        private float SupportDist(Vector3 d)
        {
            float t = 0f;
            if (SupportBox.y > 1e-4f)
            {
                Vector3 lo = new Vector3(-SupportBox.x, 0f, -SupportBox.z), hi = new Vector3(SupportBox.x, SupportBox.y, SupportBox.z);
                t = float.MaxValue;
                for (int k = 0; k < 3; k++)
                {
                    if (Mathf.Abs(d[k]) < 1e-8f) continue;
                    float t1 = (lo[k] - _c[k]) / d[k], t2 = (hi[k] - _c[k]) / d[k];
                    t = Mathf.Min(t, Mathf.Max(t1, t2));
                }
                if (t == float.MaxValue) t = 0f;
                t = Mathf.Max(0f, t);
            }
            if (SupportIce.x > 1e-4f && SupportIce.y > 1e-4f)
            {
                // эллипсоид с центром на верхе кубика
                Vector3 ce = new Vector3(0f, Mathf.Max(0f, SupportBox.y), 0f);
                Vector3 r = new Vector3(SupportIce.x, SupportIce.y, SupportIce.x);
                Vector3 o = new Vector3((_c.x - ce.x) / r.x, (_c.y - ce.y) / r.y, (_c.z - ce.z) / r.z);
                Vector3 dd = new Vector3(d.x / r.x, d.y / r.y, d.z / r.z);
                float qa = Vector3.Dot(dd, dd), qb = 2f * Vector3.Dot(o, dd), qc = Vector3.Dot(o, o) - 1f;
                float disc = qb * qb - 4f * qa * qc;
                if (disc >= 0f)
                {
                    float sq = Mathf.Sqrt(disc);
                    float t0 = (-qb - sq) / (2f * qa), t1 = (-qb + sq) / (2f * qa);
                    if (t0 <= t + 1e-5f && t1 > t) t = t1;
                }
            }
            return t;
        }

        /// <summary>Готовый купол в точке (t, a): направление от центра, радиус купола, радиус опоры.</summary>
        private Vector3 Final(float t, float a, float ax, float az, float wallT, float H, float R, bool boxy, float minT,
                              out float rF, out float rS, out float tip, out float noise)
        {
            Vector3 f = Surf(t, a, ax, az, wallT, H, R, boxy, Turns, out tip, out noise);
            Vector3 v = f - _c;
            rF = v.magnitude;
            Vector3 d = rF > 1e-6f ? v / rF : Vector3.up;
            rS = SupportDist(d);
            if (rF < rS + minT) rF = rS + minT;     // меренга всегда снаружи бисквита и мороженого
            return d;
        }

        /// <summary>Толщина слоя: пока стекает — не толще cap, к концу — до полного купола.</summary>
        private static float Layer(float full, float cap, float blend)
        {
            float m = Mathf.Min(full, cap);
            return m + (full - m) * blend;
        }

        /// <summary>Слой у кромки тоньше (не меньше 45%), к макушке — полный.</summary>
        private static float Lay(float t, float tF)
        {
            float f = Mathf.Max(0f, tF);
            float k = Mathf.Clamp01((t - f) / Mathf.Max(1e-3f, 1f - f));
            return 0.45f + 0.55f * Mathf.Sqrt(k);
        }

        private static float Smooth01(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // v23.50: КЭШ готового купола (направление, радиус купола, радиус опоры, гребень, шум) — от растекания не зависит,
        // поэтому пока меренга стекает, считается один раз (было: каждые 0,05 с по всем вершинам — лагало).
        private Vector3[] _fD;
        private float[] _fRF, _fRS, _fTip, _fNoise;
        private bool _fValid;
        private float _fH, _fR, _fWallT, _fAx, _fAz, _fMinT;
        private bool _fBoxy;

        private void InvalidateFinal() { _fValid = false; _shownSpread = -1f; }

        /// <summary>Пересчитать готовый купол в вершинах (все или только окно строк/столбцов).</summary>
        private void ComputeFinal(int i0, int i1, int j0, int j1, bool wrapJ)
        {
            int rings = _rings, segs = _segs;
            for (int jj = j0; jj <= j1; jj++)
            {
                int j = wrapJ ? ((jj % segs) + segs) % segs : jj;
                if (j < 0 || j > segs) continue;
                float a = j / (float)segs;
                for (int i = Mathf.Max(0, i0); i <= Mathf.Min(rings, i1); i++)
                {
                    float t = i / (float)rings;
                    int idx = i * (segs + 1) + j;
                    _fD[idx] = Final(t, a, _fAx, _fAz, _fWallT, _fH, _fR, _fBoxy, _fMinT, out _fRF[idx], out _fRS[idx], out _fTip[idx], out _fNoise[idx]);
                    if (j == 0)                                           // шов: последний столбец = первый
                    {
                        int idx2 = i * (segs + 1) + segs;
                        _fD[idx2] = _fD[idx]; _fRF[idx2] = _fRF[idx]; _fRS[idx2] = _fRS[idx]; _fTip[idx2] = _fTip[idx]; _fNoise[idx2] = _fNoise[idx];
                    }
                }
            }
        }

        private void EnsureFinal()
        {
            int n = (_rings + 1) * (_segs + 1);
            if (_fD == null || _fD.Length != n)
            {
                _fD = new Vector3[n]; _fRF = new float[n]; _fRS = new float[n]; _fTip = new float[n]; _fNoise = new float[n];
                _fValid = false;
            }
            float H = FullH, R = RefR;
            bool boxy = Boxy;
            float wallT = boxy ? Mathf.Clamp(WallHeight / Mathf.Max(1e-4f, H), 0f, 0.8f) : 0f;
            float ax = boxy ? BaseHalf.x : Radius, az = boxy ? BaseHalf.y : Radius;
            float minT = MinThickness * R;
            if (_fValid && H == _fH && R == _fR && wallT == _fWallT && ax == _fAx && az == _fAz && minT == _fMinT && boxy == _fBoxy) return;
            _fH = H; _fR = R; _fWallT = wallT; _fAx = ax; _fAz = az; _fMinT = minT; _fBoxy = boxy;
            float boxH = Mathf.Max(0f, SupportBox.y), iceH = SupportIce.y > 1e-4f ? SupportIce.y : 0f;
            _c = new Vector3(0f, boxH > 1e-4f ? boxH * 0.5f : Mathf.Max(0.001f, iceH * 0.25f), 0f);
            _ridgeK = 1f;
            if (Style == CapStyle.Piped) EnsureLayout();
            ComputeFinal(0, _rings, 0, _segs - 1, false);
            _fValid = true;
            _colorDirty = true;
        }

        /// <summary>
        /// Положение вершин. v23.50: меренга СРАЗУ своего размера — не набухает: готовый купол «срезан» кромкой,
        /// кромка (круглый толстый нос с подтёками) сползает от макушки мороженого до тарелки. Дешёво: купол из кэша.
        /// </summary>
        private void Shape()
        {
            if (_v == null) return;
            _shownSpread = Spread;
            EnsureFinal();
            int rings = _rings, segs = _segs;
            float H = _fH;
            float boxH = Mathf.Max(0f, SupportBox.y), iceH = SupportIce.y > 1e-4f ? SupportIce.y : 0f;
            bool hasSupport = boxH > 1e-4f || iceH > 1e-4f;
            float tStart = hasSupport ? Mathf.Clamp((boxH + iceH * 0.8f) / Mathf.Max(1e-4f, H), 0.05f, 0.95f) : SpreadFrom;
            float sp = Mathf.Clamp01(Spread);
            float eased = 1f - (1f - sp) * (1f - sp) * (1f - sp);           // падает быстро, у тарелки — мягко
            const float band = 0.07f;                                      // ширина «носа» кромки (доля высоты)

            for (int j = 0; j <= segs; j++)
            {
                float a = j / (float)segs;
                float th = a * Mathf.PI * 2f;
                float drip = (0.7f * (0.5f + 0.5f * Mathf.Sin(3f * th + 1.3f + Seed)) + 0.4f * (0.5f + 0.5f * Mathf.Sin(7f * th + 0.4f + Seed * 0.7f)))
                             * Drips * Mathf.Sin(Mathf.PI * sp);
                float tF = sp >= 0.999f ? -1f : Mathf.Lerp(tStart, -0.03f, eased) - drip;   // кромка: ниже неё меренги ещё нет

                Vector3 dF = Vector3.up, down = Vector3.down;
                float rSF = 0f, LF = 0f;
                if (tF > 0f)
                {
                    // кромка — из кэша ближайшей строки (без новых расчётов купола)
                    int iF = Mathf.Clamp(Mathf.RoundToInt(tF * rings), 0, rings);
                    int iD = Mathf.Clamp(iF - Mathf.Max(1, Mathf.RoundToInt(0.03f * rings)), 0, rings);
                    int kF = iF * (segs + 1) + j, kD = iD * (segs + 1) + j;
                    dF = _fD[kF]; rSF = _fRS[kF]; LF = Mathf.Max(0f, _fRF[kF] - _fRS[kF]);
                    Vector3 fu = _c + _fD[kF] * _fRF[kF], fd = _c + _fD[kD] * _fRF[kD];
                    down = fd - fu;
                    down = down.sqrMagnitude > 1e-10f ? down.normalized : Vector3.down;
                }

                for (int i = 0; i <= rings; i++)
                {
                    float t = i / (float)rings;
                    int idx = i * (segs + 1) + j;
                    Vector3 pos;
                    if (t >= tF || tF <= 0f) pos = _c + _fD[idx] * _fRF[idx];
                    else
                    {
                        float u = (tF - t) / band;
                        if (u < 1f)
                        {
                            float ang = u * Mathf.PI * 0.5f;
                            pos = _c + dF * (rSF + LF * Mathf.Cos(ang)) + down * (LF * 0.45f * Mathf.Sin(ang));
                        }
                        else pos = _c + dF * (rSF * 0.97f);                  // ещё не дотекло — спрятано внутри
                    }
                    _v[idx] = pos;
                    _uv[idx] = new Vector2(a, t);
                    _tip[idx] = _fTip[idx];
                    _noise[idx] = _fNoise[idx];
                }
            }
            int c0 = _sideCount;
            _v[c0] = new Vector3(0f, _v[0].y, 0f); _uv[c0] = new Vector2(0.5f, 0f); _tip[c0] = 0f;
            for (int j = 0; j <= segs; j++) { _v[c0 + 1 + j] = _v[j]; _uv[c0 + 1 + j] = _uv[j]; _tip[c0 + 1 + j] = 0f; }

            if (_mesh == null) return;
            _mesh.vertices = _v;
            if (_mesh.triangles != null && _mesh.triangles.Length > 0) Normals();
            _mesh.RecalculateBounds();
        }

        private void Normals()
        {
            _mesh.RecalculateNormals();
            Vector3[] nr = _mesh.normals;
            // шов спирали (первый и последний столбец — одни точки): нормали усредняем
            for (int i = 0; i <= _rings; i++)
            {
                int a = i * (_segs + 1), b = a + _segs;
                Vector3 m = (nr[a] + nr[b]).normalized;
                nr[a] = m; nr[b] = m;
            }
            for (int j = 0; j <= _segs + 1; j++) nr[_sideCount + j] = Vector3.down;
            _nrm = nr;
            _mesh.normals = nr;
        }

        // ───────────────────────── ПОДПАЛ ─────────────────────────

        private void JetTick(float dt)
        {
            IReadOnlyList<IHeatJet> jets = HeatJets.All;
            if (jets.Count == 0 || _v == null || _nrm == null) return;
            MeshRenderer mr = GetComponent<MeshRenderer>();
            if (mr == null || !mr.enabled || Spread <= 0.001f) return;
            Bounds wb = mr.bounds;
            float inv = 1f / Mathf.Max(1e-4f, MaxScale());
            bool any = false;
            for (int q = 0; q < jets.Count; q++)
            {
                IHeatJet j = jets[q];
                if (j == null || (j is Object o && o == null) || !j.JetActive) continue;
                float len = Mathf.Max(0.05f, j.JetLength) * 1.2f;
                // грубо: струя вообще рядом с куполом?
                Vector3 mid = j.JetOrigin + j.JetDirection * Mathf.Clamp(Vector3.Dot(wb.center - j.JetOrigin, j.JetDirection), 0f, len);
                if ((mid - wb.ClosestPoint(mid)).magnitude > j.JetRadius * 2f + 0.05f) continue;

                Vector3 o0 = transform.InverseTransformPoint(j.JetOrigin);
                Vector3 dir = transform.InverseTransformDirection(j.JetDirection).normalized;
                float l = len * inv, rad = Mathf.Max(0.03f, j.JetRadius) * inv;
                float power = Mathf.Max(0.2f, j.JetPower);
                for (int i = 0; i < _sideCount; i++)
                {
                    Vector3 d = _v[i] - o0;
                    float along = Vector3.Dot(d, dir);
                    if (along < 0f || along > l) continue;
                    float radial = (d - dir * along).magnitude;
                    float rr = rad * (0.6f + along / l) * ToastSpot;
                    if (radial > rr) continue;
                    float facing = -Vector3.Dot(_nrm[i], dir);           // смотрит на сопло
                    if (facing <= 0.05f) continue;
                    float k = power * (1f - 0.5f * along / l) * (1f - radial / rr) * facing;
                    _vt[i] = Mathf.Min(1f, _vt[i] + ToastRate * k * dt);
                    any = true;
                }
            }
            if (any) _colorDirty = true;
        }

        private void Paint()
        {
            _colorDirty = false;
            if (_mesh == null || _v == null) return;
            if (!Application.isPlaying) for (int i = 0; i < _vt.Length; i++) _vt[i] = _toast;   // в редакторе — ползунок
            for (int i = 0; i < _v.Length; i++)
            {
                float c = _vt[i] * (0.15f + 1.05f * Mathf.Pow(_tip[i], TipBias)) + 0.06f * _noise[i] * 2f;
                Color col = RampColor(c);
                col.a = DetailAlpha;        // сколько узора текстуры (пена) — шейдер Food Heap
                _col[i] = col;
            }
            _mesh.colors = _col;
        }

        private Color RampColor(float c)
        {
            if (Ramp == null || Ramp.Length == 0) return Color.white;
            if (Ramp.Length == 1) return Ramp[0];
            c = Mathf.Clamp01(c) * (Ramp.Length - 1);
            int i = Mathf.Min(Ramp.Length - 2, Mathf.FloorToInt(c));
            return Color.Lerp(Ramp[i], Ramp[i + 1], c - i);
        }
    }
}
