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
        [Range(0.01f, 1f)] public float ToastRate = 0.16f;
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
            else _shownSpread = -1f;
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
            _topoDirty = true; _colorDirty = true;
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
            float s2 = t * turns + a;
            float p = s2 - Mathf.Floor(s2);
            float bump = Mathf.Pow(Mathf.Sin(Mathf.PI * p), RidgeRound);
            noise = Mathf.PerlinNoise(Seed * 13.37f + Mathf.Cos(th) * 1.7f + t * 3f, Seed * 7.13f + Mathf.Sin(th) * 1.7f + t * 5f) - 0.5f;
            float r = baseR + RidgeDepth * _ridgeK * R * bump * (0.35f + 0.65f * Mathf.Min(1f, g)) * fade + Wobble * R * noise * 2f;
            tip = bump * fade + (1f - fade) * 0.85f;
            return new Vector3(r * Mathf.Cos(th), H * t + curl, r * Mathf.Sin(th));
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

        /// <summary>Положение вершин: купол × растекание по опоре. Топология та же.</summary>
        private void Shape()
        {
            if (_v == null) return;
            _shownSpread = Spread;
            int rings = _rings, segs = _segs;
            float H = FullH, R = RefR;
            bool boxy = Boxy;
            float wallT = boxy ? Mathf.Clamp(WallHeight / Mathf.Max(1e-4f, H), 0f, 0.8f) : 0f;
            float ax = boxy ? BaseHalf.x : Radius, az = boxy ? BaseHalf.y : Radius;
            float minT = MinThickness * R;

            float boxH = Mathf.Max(0f, SupportBox.y), iceH = SupportIce.y > 1e-4f ? SupportIce.y : 0f;
            _c = new Vector3(0f, boxH > 1e-4f ? boxH * 0.5f : Mathf.Max(0.001f, iceH * 0.25f), 0f);
            bool hasSupport = boxH > 1e-4f || iceH > 1e-4f;
            float tStart = hasSupport ? Mathf.Clamp((boxH + iceH * 0.8f) / Mathf.Max(1e-4f, H), 0.05f, 0.95f) : SpreadFrom;
            float tTop = Mathf.Max(minT * 2f, H - (boxH + iceH));          // толщина на макушке готового купола

            float sp = Mathf.Clamp01(Spread);
            float eased = 1f - (1f - sp) * (1f - sp);                      // к концу медленнее — густая масса
            float blend = Smooth01(0.75f, 1f, sp);                         // в конце пушится в купол
            float cap = tTop * Mathf.Lerp(1.1f, 1f, sp);
            _ridgeK = 0.3f + 0.7f * eased;                                 // жгут проступает, пока оседает
            const float band = 0.07f;                                      // ширина «носа» кромки (доля высоты)

            for (int j = 0; j <= segs; j++)
            {
                float a = j / (float)segs;
                float th = a * Mathf.PI * 2f;
                float drip = (0.7f * (0.5f + 0.5f * Mathf.Sin(3f * th + 1.3f + Seed)) + 0.4f * (0.5f + 0.5f * Mathf.Sin(7f * th + 0.4f + Seed * 0.7f)))
                             * Drips * Mathf.Sin(Mathf.PI * sp);
                float tF = Mathf.Lerp(tStart, -0.03f, eased) - drip;     // кромка: ниже неё меренги ещё нет

                // кромка: где она, какой толщины, куда «вниз» по куполу
                Vector3 dF = Vector3.up, down = Vector3.down;
                float rSF = 0f, LF = 0f;
                if (tF > 0f)
                {
                    float tc = Mathf.Clamp01(tF);
                    dF = Final(tc, a, ax, az, wallT, H, R, boxy, minT, out float rFF, out rSF, out _, out _);
                    LF = Layer(rFF - rSF, cap * Lay(tc, tF), blend);
                    Vector3 fu = Surf(tc, a, ax, az, wallT, H, R, boxy, Turns, out _, out _);
                    Vector3 fd = Surf(Mathf.Clamp01(tc - 0.03f), a, ax, az, wallT, H, R, boxy, Turns, out _, out _);
                    down = fd - fu;
                    down = down.sqrMagnitude > 1e-10f ? down.normalized : Vector3.down;
                }

                for (int i = 0; i <= rings; i++)
                {
                    float t = i / (float)rings;
                    Vector3 d = Final(t, a, ax, az, wallT, H, R, boxy, minT, out float rF, out float rS, out float tip, out float n);
                    Vector3 pos;
                    if (t >= tF || tF <= 0f)
                        pos = _c + d * (rS + Layer(rF - rS, cap * Lay(t, tF), blend));
                    else
                    {
                        float u = (tF - t) / band;
                        if (u < 1f)
                        {
                            // круглый толстый нос кромки: от наружной поверхности заворачивает к бисквиту
                            float ang = u * Mathf.PI * 0.5f;
                            pos = _c + dF * (rSF + LF * Mathf.Cos(ang)) + down * (LF * 0.45f * Mathf.Sin(ang));
                        }
                        else pos = _c + dF * (rSF * 0.97f);                  // ещё не дотекло — спрятано внутри
                    }
                    int idx = i * (segs + 1) + j;
                    _v[idx] = pos;
                    _uv[idx] = new Vector2(a, t);
                    _tip[idx] = tip;
                    _noise[idx] = n;
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
                    float rr = rad * (0.6f + along / l);
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
