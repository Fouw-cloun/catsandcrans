using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ТАРЕЛКА С ЗАПЕЧЁННОЙ АЛЯСКОЙ — десерт собирается прямо на тарелке, одним объектом,
    /// без подмены префабов.
    ///
    /// ═══ СБОРКА (идеальная, без провалов) ═══
    ///   1. Кубик бисквита: мастерком (ЛКМ по кубику → ПКМ в тарелку) или руками — на тарелку.
    ///      Кубик садится в центр.
    ///   2. Мороженое: мастерком из ведра → ПКМ в тарелку. Падает горкой на бисквит.
    ///   3. Меренга: мастерком → ПКМ. Шапка ложится на мороженое и за Spread Seconds
    ///      медленно стекает, закрывая мороженое и бисквит куполом-ульем.
    ///   4. Горелка: меренга румянится там, куда бьёт пламя (гребни быстрее бороздок).
    ///   5. Подача — несёшь тарелку (F).
    ///
    /// Всё, что бросили в тарелку, — обычное содержимое Cookware (по сети как у любой тары).
    /// Эта тарелка только рисует: сколько бисквита, мороженого и меренги внутри.
    /// Растекание идёт на сервере и приходит всем. Подпал — от струй горелки, у каждого сам.
    ///
    /// ═══ ТРЕБУЕТ ═══
    ///   Корень тарелки: NetworkObject, NetworkTransform, Rigidbody, коллайдер, PickupItem (нести),
    ///   Cookware (Def = Cookware_DessertPlate: глотает, без рецептов), этот компонент.
    ///   Anchor — пустой дочерний объект в центре на поверхности тарелки (ось Y — вверх).
    ///   Меню: Десерты → 7.
    /// </summary>
    [RequireComponent(typeof(Cookware))]
    public sealed class DessertPlate : NetworkBehaviour
    {
        [Header("Продукты (пусто — по названию)")]
        [SerializeField] private ProductDef _sponge;
        [SerializeField] private ProductDef _iceCream;
        [SerializeField] private ProductDef _meringue;

        [Header("Где и какого размера, м")]
        [Tooltip("Центр на поверхности тарелки, Y — вверх")]
        [SerializeField] private Transform _anchor;
        [Tooltip("Кубик бисквита: ширина, высота, глубина (м). Ставит меню по пласту.")]
        [SerializeField] private Vector3 _spongeSize = new Vector3(0.6f, 0.3f, 0.6f);
        [Tooltip("Горка мороженого: доля ширины бисквита")]
        [Range(0.3f, 1f)][SerializeField] private float _iceWidth = 0.78f;
        [Tooltip("Шарик мороженого: высота / диаметр (1 — круглый шар, 0.8 — чуть сплюснутый)")]
        [Range(0.5f, 1f)][SerializeField] private float _iceRound = 0.8f;
        [Tooltip("Меренга: насколько шире бисквита основание")]
        [Range(1.2f, 1.8f)][SerializeField] private float _meringueWide = 1.38f;
        [Tooltip("Меренга: толщина над мороженым, доля ширины бисквита")]
        [Range(0.1f, 0.8f)][SerializeField] private float _meringueTop = 0.25f;

        [Header("Сколько нужно (порций мастерка)")]
        [Min(1)][SerializeField] private int _iceFull = 2;
        [Min(1)][SerializeField] private int _meringueFull = 2;

        [Header("Меренга стекает")]
        [Tooltip("За сколько секунд меренга стекает до тарелки")]
        [SerializeField] private float _spreadSeconds = 9f;

        [Header("Горелка")]
        [Tooltip("Подпал меренги в секунду в центре пламени обычной горелки (0.16 — ~3 с до золотистого)")]
        [Range(0.02f, 1f)][SerializeField] private float _toastRate = 0.16f;
        [Tooltip("Ускорение подпала: Toast Rate × это. 2.5 — купол румянится примерно за 10–15 с горелкой")]
        [Range(1f, 6f)][SerializeField] private float _toastBoost = 2.5f;
        [Tooltip("Какая доля купола должна стать золотистой, чтобы десерт был ГОТОВ (100%)")]
        [Range(0.3f, 0.95f)][SerializeField] private float _doneShare = 0.6f;

        [Header("Готовность на экране")]
        [Tooltip("Надпись над тарелкой: что делать дальше и готовность в %")]
        [SerializeField] private bool _showHud = true;
        [Tooltip("С какого расстояния видна надпись, м")]
        [SerializeField] private float _hudDistance = 5f;
        [Tooltip("Насколько надо смотреть на тарелку (градусы от центра экрана)")]
        [Range(5f, 60f)][SerializeField] private float _hudAngle = 30f;

        [Header("Готово — золотые звёздочки")]
        [SerializeField] private bool _stars = true;
        [Range(5, 80)][SerializeField] private int _starCount = 26;
        [SerializeField] private Color _starColor = new Color(1f, 0.82f, 0.22f, 1f);

        [Header("Вид")]
        [SerializeField] private Material _spongeMat;
        [SerializeField] private Material _iceMat;
        [SerializeField] private Material _meringueMat;
        [SerializeField] private bool _log = true;

        // ── сеть ──
        private readonly SyncVar<byte> _spread = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _ready = new SyncVar<byte>(0);      // готовность подпала 0..100 (считает сервер)

        private Cookware _cw;
        private Transform _root, _spongeT, _iceT;
        private MeshRenderer _spongeR, _iceR;
        private MeringueCap _cap;
        private MaterialPropertyBlock _mpb;
        private int _shownSponge = -1, _shownIce = -1, _shownMer = -1;
        private float _spongeDrop, _iceDrop;          // 0..1 анимация падения
        private float _spreadShown, _spreadServer, _sendAt;
        private int _toastLog;
        private bool _doneLogged;
        private float _readyAt;
        private ParticleSystem _starPs;
        private static Texture2D _starTex;
        private static GUIStyle _hudStyle;

        public float Spread01 => _spread.Value / 255f;
        public float Toast01 => _cap != null ? _cap.AverageToast : 0f;
        public float ToastedShare => _cap != null ? _cap.ToastedShare : 0f;
        /// <summary>Готовность подпала 0..1 (с сервера): 1 — ГОТОВО.</summary>
        public float Ready01 => _ready.Value / 100f;
        public bool IsDone => _ready.Value >= 100;
        public bool HasSponge => Count(_sponge) > 0;
        public bool HasIceCream => Count(_iceCream) > 0;
        public bool HasMeringue => Count(_meringue) > 0;

        private void Awake()
        {
            _cw = GetComponent<Cookware>();
            if (_anchor == null) _anchor = transform.Find("DessertAnchor");
            if (_anchor == null) _anchor = transform;
            BuildVisual();
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            ResolveProducts();
            _cw.ContentsChanged += OnContents;
            OnContents();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            if (_cw != null) _cw.ContentsChanged -= OnContents;
        }

        private void ResolveProducts()
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return;
            if (_sponge == null) _sponge = ByTitle(db, CookRecipeBook.Dessert.Sponge);
            if (_iceCream == null) _iceCream = ByTitle(db, "Мороженое");
            if (_meringue == null) _meringue = ByTitle(db, CookRecipeBook.Dessert.Meringue);
            if (_log && (_sponge == null || _iceCream == null || _meringue == null))
                Debug.LogWarning($"[Аляска] «{name}»: не нашёл продукт: {(_sponge == null ? "Бисквит " : "")}{(_iceCream == null ? "Мороженое " : "")}{(_meringue == null ? "Меренга" : "")}", this);
        }

        private static ProductDef ByTitle(FoodDatabase db, string title)
        {
            FoodTransitionTable t = db.Table;
            if (t == null) return null;
            for (int id = 1; id < 2048; id++)
            {
                ProductDef p = t.GetProduct((ushort)id);
                if (p != null && p.Title == title) return p;
            }
            return null;
        }

        private int Count(ProductDef p) => p != null && _cw != null ? _cw.CountOf(p.Id) : 0;

        // ───────────────────────── ВИД ─────────────────────────

        /// <summary>Узел, который держит десерт в метрах (сам гасит масштаб тарелки).</summary>
        private void BuildVisual()
        {
            var r = new GameObject("Alaska (auto)");
            _root = r.transform;
            _root.SetParent(_anchor, false);
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.identity;
            Vector3 ps = _anchor.lossyScale;
            _root.localScale = new Vector3(1f / Mathf.Max(1e-4f, Mathf.Abs(ps.x)), 1f / Mathf.Max(1e-4f, Mathf.Abs(ps.y)), 1f / Mathf.Max(1e-4f, Mathf.Abs(ps.z)));

            _spongeT = Part("Sponge", _spongeMat, out _spongeR);
            _spongeT.GetComponent<MeshFilter>().sharedMesh = SpongeMesh.SharedPiece(_spongeSize, SpongeMesh.XNeg | SpongeMesh.ZPos);
            _iceT = Part("IceCream", _iceMat, out _iceR);
            _iceT.GetComponent<MeshFilter>().sharedMesh = SpongeMesh.IceScoop();

            var mg = new GameObject("Meringue");
            mg.layer = gameObject.layer;
            mg.transform.SetParent(_root, false);
            mg.AddComponent<MeshFilter>();
            MeshRenderer mr = mg.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _meringueMat;
            _cap = mg.AddComponent<MeringueCap>();
            _cap.Spread = 0f;
            _cap.Seed = Random.Range(1, 999);
            _cap.ToastRate = _toastRate * _toastBoost;
            ShapeMeringue();
            mr.enabled = false;
            _spongeR.enabled = false; _iceR.enabled = false;
        }

        private Transform Part(string n, Material m, out MeshRenderer mr)
        {
            var go = new GameObject(n);
            go.layer = gameObject.layer;
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>();
            mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            return go.transform;
        }

        private float SpongeH => HasSponge ? _spongeSize.y : 0f;
        private float Half => Mathf.Min(_spongeSize.x, _spongeSize.z) * 0.5f;
        private float IceR(int n) => Half * _iceWidth * Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(n / (float)_iceFull));
        private float IceH(int n) => n > 0 ? IceR(n) * 2f * _iceRound : 0f;

        private void ShapeMeringue()
        {
            int ice = Count(_iceCream);
            ConfigureCap(_cap, _spongeSize, HasSponge, ice > 0 ? IceR(ice) : 0f, IceH(ice), _meringueWide, _meringueTop);
            _cap.MarkShapeDirty();
        }

        /// <summary>
        /// Купол меренги над кубиком и горкой мороженого (метры). Общая настройка — тарелка и витрина (меню 6).
        /// Основание чуть квадратное и шире кубика (wide × половина кубика) — закрывает углы толстым слоем;
        /// низкие стенки, дальше круглый купол-улей; макушка — top × ширина кубика над мороженым.
        /// </summary>
        public static void ConfigureCap(MeringueCap cap, Vector3 sponge, bool hasSponge, float iceR, float iceH, float wide = 1.38f, float top = 0.25f)
        {
            float half = Mathf.Min(sponge.x, sponge.z) * 0.5f;
            float h0 = hasSponge ? sponge.y : 0f;
            float ax = sponge.x * 0.5f * wide, az = sponge.z * 0.5f * wide;
            cap.BaseHalf = new Vector2(ax, az);
            cap.BaseSquareness = 3f;
            cap.WallHeight = h0 * 0.6f;
            cap.Height = Mathf.Max(h0 + iceH + half * 2f * top, 0.78f * 2f * Mathf.Max(ax, az));
            cap.Radius = half;
            cap.RidgeDepth = 0.13f;
            cap.Turns = 7f;
            cap.MinThickness = 0.12f;
            cap.SupportBox = hasSponge ? new Vector3(sponge.x * 0.5f, sponge.y, sponge.z * 0.5f) : Vector3.zero;
            cap.SupportIce = iceR > 0f && iceH > 0f ? new Vector2(iceR, iceH) : Vector2.zero;
            cap.Rings = 64; cap.Segments = 120;
        }

        private void OnContents()
        {
            int s = Count(_sponge), ice = Count(_iceCream), mer = Count(_meringue);
            if (s != _shownSponge)
            {
                if (s > _shownSponge && _shownSponge >= 0) _spongeDrop = 1f;
                if (_log && s > 0 && _shownSponge <= 0 && base.IsServerInitialized) Debug.Log($"[Аляска] «{name}»: бисквит на тарелке — теперь мороженое мастерком", this);
            }
            if (ice != _shownIce)
            {
                if (ice > _shownIce && _shownIce >= 0) _iceDrop = 1f;
                if (_log && ice > 0 && _shownIce <= 0 && base.IsServerInitialized) Debug.Log($"[Аляска] «{name}»: мороженое горкой — теперь меренга мастерком", this);
            }
            if (_log && mer > 0 && _shownMer <= 0 && base.IsServerInitialized) Debug.Log($"[Аляска] «{name}»: меренга легла — стекает ~{_spreadSeconds:0} с, потом горелка", this);
            if (mer <= 0 && _shownMer > 0) ResetToast();     // меренгу убрали (K, тест) — подпал и готовность заново
            _shownSponge = s; _shownIce = ice; _shownMer = mer;
            _spongeR.enabled = s > 0;
            _iceR.enabled = ice > 0;
            _cap.GetComponent<MeshRenderer>().enabled = mer > 0;
            ShapeMeringue();
            if (_iceMat != null || _iceR != null) TintIce();
        }

        private void TintIce()
        {
            if (_iceCream == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _iceR.GetPropertyBlock(_mpb);
            Color c = _iceCream.BaseColor; c.a = 1f;
            _mpb.SetColor("_BaseColor", c);
            _iceR.SetPropertyBlock(_mpb);
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // ── сервер: меренга стекает ──
            if (base.IsServerInitialized && HasMeringue && _spread.Value < 255)
            {
                _spreadServer = Mathf.Max(_spreadServer, Spread01);
                _spreadServer = Mathf.Min(1f, _spreadServer + dt / Mathf.Max(0.5f, _spreadSeconds));
                if (Time.time >= _sendAt || _spreadServer >= 1f)
                {
                    _sendAt = Time.time + 0.1f;
                    _spread.Value = (byte)Mathf.RoundToInt(_spreadServer * 255f);
                    if (_spread.Value == 255 && _log) Debug.Log($"[Аляска] «{name}»: меренга закрыла бисквит куполом — румянь горелкой", this);
                }
            }

            // ── вид: падение кусков и растекание ──
            float sh = SpongeH;
            if (_spongeT != null)
            {
                _spongeDrop = Mathf.MoveTowards(_spongeDrop, 0f, dt * 3.2f);
                float k = _spongeDrop;
                float fall = Mathf.Clamp01((k - 0.35f) / 0.65f);                     // падает…
                float squash = 1f - 0.12f * Squash(k);                               // …и приседает при касании
                _spongeT.localPosition = new Vector3(0f, _spongeSize.y * 0.5f * squash + fall * fall * _spongeSize.y * 2f, 0f);
                _spongeT.localScale = new Vector3(1f / Mathf.Sqrt(squash), squash, 1f / Mathf.Sqrt(squash));
            }
            if (_iceT != null)
            {
                int ice = Count(_iceCream);
                _iceDrop = Mathf.MoveTowards(_iceDrop, 0f, dt * 3.5f);
                float k = _iceDrop;
                float r = IceR(ice), h = IceH(ice);
                float fall = Mathf.Clamp01((k - 0.35f) / 0.65f);
                float squash = 1f - 0.22f * Squash(k);                               // мягкое — приседает сильнее
                _iceT.localPosition = new Vector3(0f, sh + fall * fall * h * 3f, 0f);
                _iceT.localScale = new Vector3(r / Mathf.Sqrt(squash), h * squash, r / Mathf.Sqrt(squash));
            }
            if (_cap != null)
            {
                float target = Spread01;
                _spreadShown = Mathf.MoveTowards(_spreadShown, target, dt * 0.6f);
                if (target < _spreadShown) _spreadShown = target;
                _cap.Spread = _spreadShown;
            }

            // ── сервер: готовность подпала (0..100 %) ──
            if (base.IsServerInitialized && _cap != null && HasMeringue && !_doneLogged && Time.time >= _readyAt)
            {
                _readyAt = Time.time + 0.2f;
                float share = _cap.ToastedShare;
                int pct = Mathf.Clamp(Mathf.FloorToInt(share / Mathf.Max(0.05f, _doneShare) * 100f), 0, 100);
                if (pct > _ready.Value) _ready.Value = (byte)pct;
                int step = _ready.Value / 25;
                if (_log && step > _toastLog && _ready.Value < 100)
                {
                    _toastLog = step;
                    Debug.Log($"[Аляска] «{name}»: готовность {_ready.Value}% (золотистого {share * 100f:0}% купола из {_doneShare * 100f:0}%)", this);
                }
                if (_ready.Value >= 100)
                {
                    _doneLogged = true;
                    if (_log) Debug.Log($"[Аляска] «{name}»: ГОТОВО — Запечённая Аляска, неси тарелку ★", this);
                    RpcStars();
                }
            }
        }

        private void ResetToast()
        {
            if (_cap != null) _cap.Toast = 0f;
            _toastLog = 0; _doneLogged = false; _spreadServer = 0f; _spreadShown = 0f;
            if (base.IsServerInitialized)
            {
                if (_ready.Value != 0) _ready.Value = 0;
                if (_spread.Value != 0) _spread.Value = 0;
            }
        }

        // ───────────────────────── ЗВЁЗДОЧКИ ─────────────────────────

        [ObserversRpc]
        private void RpcStars() => PlayStars();

        /// <summary>Золотые звёздочки вылетают из тарелки — десерт готов. Только вид.</summary>
        private void PlayStars()
        {
            if (!_stars) return;
            float k = Mathf.Max(0.3f, Half / 0.3f);                      // под размер десерта (кубик 0.6 м → 1)
            if (_starPs == null) _starPs = MakeStars();
            if (_starPs == null) return;
            _starPs.transform.SetPositionAndRotation(TopPoint(), Quaternion.identity);
            ParticleSystem.MainModule m = _starPs.main;
            m.startSpeed = new ParticleSystem.MinMaxCurve(2.2f * k, 4.2f * k);
            m.startSize = new ParticleSystem.MinMaxCurve(0.09f * k, 0.2f * k);
            m.startColor = new ParticleSystem.MinMaxGradient(_starColor, Color.Lerp(_starColor, Color.white, 0.55f));
            ParticleSystem.ShapeModule sh = _starPs.shape;
            sh.radius = 0.12f * k;
            _starPs.Emit(_starCount);
            if (_log) Debug.Log($"[Аляска] «{name}»: ★ звёздочки — десерт готов", this);
        }

        private Vector3 TopPoint()
        {
            MeshRenderer mr = _cap != null ? _cap.GetComponent<MeshRenderer>() : null;
            if (mr != null && mr.enabled) { Bounds b = mr.bounds; return new Vector3(b.center.x, b.max.y, b.center.z); }
            return _anchor.position + Vector3.up * (SpongeH + IceH(Count(_iceCream)) + 0.1f);
        }

        private ParticleSystem MakeStars()
        {
            var go = new GameObject("AlaskaStars (auto)");
            go.SetActive(false);                                         // свойства — до первого Play
            go.transform.SetParent(transform, true);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule m = ps.main;
            m.playOnAwake = false;
            m.loop = false;
            m.duration = 1f;
            m.maxParticles = 200;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.7f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.gravityModifier = 0.75f;
            m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ParticleSystem.EmissionModule em = ps.emission; em.enabled = false;
            ParticleSystem.ShapeModule sh = ps.shape;
            sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 28f; sh.radius = 0.12f;
            sh.rotation = new Vector3(-90f, 0f, 0f);                      // конус вверх
            ParticleSystem.RotationOverLifetimeModule rl = ps.rotationOverLifetime;
            rl.enabled = true; rl.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
            ParticleSystem.SizeOverLifetimeModule sl = ps.sizeOverLifetime;
            sl.enabled = true;
            sl.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.15f, 1f), new Keyframe(0.75f, 0.85f), new Keyframe(1f, 0f)));
            ParticleSystem.ColorOverLifetimeModule cl = ps.colorOverLifetime;
            cl.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            cl.color = g;
            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            Shader shd = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shd == null) { Debug.LogWarning("[Аляска] нет шейдера Sprites/Default — звёздочки не видны", this); }
            else
            {
                var mat = new Material(shd) { name = "AlaskaStars (auto)" };
                mat.mainTexture = StarTexture();
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", StarTexture());
                r.sharedMaterial = mat;
            }
            go.SetActive(true);
            return ps;
        }

        /// <summary>Пятиконечная звёздочка с мягким ореолом (белая — цвет даёт частица).</summary>
        private static Texture2D StarTexture()
        {
            if (_starTex != null) return _starTex;
            const int N = 64;
            _starTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "AlaskaStar (auto)", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.Atan2(u, v);                              // луч вверх
                    float f = Mathf.Repeat(a / (Mathf.PI * 2f) * 5f, 1f);
                    float tri = 1f - Mathf.Abs(f - 0.5f) * 2f;               // 0 на луче, 1 между лучами
                    float rs = Mathf.Lerp(0.95f, 0.4f, Mathf.Pow(tri, 0.8f)); // на луче 0.95, между лучами 0.4
                    float star = Mathf.Clamp01((rs - r) / 0.06f);
                    float glow = 0.35f * Mathf.Exp(-r * r * 6f);
                    float alpha = Mathf.Clamp01(star + glow);
                    float core = Mathf.Lerp(0.85f, 1f, Mathf.Clamp01(1f - r * 1.6f));
                    px[y * N + x] = new Color(core, core, core, alpha);
                }
            _starTex.SetPixels(px);
            _starTex.Apply(false, true);
            return _starTex;
        }

        // ───────────────────────── НАДПИСЬ НАД ТАРЕЛКОЙ ─────────────────────────

        /// <summary>Что делать дальше и готовность — когда смотришь на тарелку.</summary>
        private string Stage(out float bar, out Color col)
        {
            bar = -1f; col = Color.white;
            if (!HasSponge && !HasIceCream && !HasMeringue) return null;    // пустая тарелка — молчим
            if (!HasSponge) { col = new Color(1f, 0.6f, 0.2f); return "Аляска: сначала кубик бисквита"; }
            if (!HasIceCream) return "Аляска: мороженое — 1 мастерок";
            if (!HasMeringue) return "Аляска: меренга — 1 мастерок";
            if (IsDone) { col = new Color(1f, 0.85f, 0.25f); bar = 1f; return "★ ГОТОВО — Запечённая Аляска"; }
            if (Spread01 < 0.999f && Ready01 <= 0f) { bar = Spread01; col = new Color(0.85f, 0.92f, 1f); return $"меренга оплывает {Spread01 * 100f:0}%"; }
            bar = Ready01; col = Color.Lerp(new Color(1f, 0.95f, 0.85f), new Color(1f, 0.7f, 0.3f), Ready01);
            return $"румянь горелкой — готовность {_ready.Value}%";
        }

        private void OnGUI()
        {
            if (!_showHud || Event.current.type != EventType.Repaint) return;
            Camera cam = Camera.main;
            if (cam == null || _anchor == null) return;
            Vector3 top = TopPoint() + Vector3.up * 0.25f;
            Vector3 to = _anchor.position - cam.transform.position;
            if (to.magnitude > _hudDistance || Vector3.Angle(cam.transform.forward, to) > _hudAngle) return;
            string text = Stage(out float bar, out Color col);
            if (text == null) return;
            Vector3 sp = cam.WorldToScreenPoint(top);
            if (sp.z <= 0f) return;
            if (_hudStyle == null)
            {
                _hudStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold };
            }
            float w = 300f, h = 24f, x = sp.x - w * 0.5f, y = Screen.height - sp.y - h - 12f;
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(x, y, w, h + (bar >= 0f ? 10f : 0f)), Texture2D.whiteTexture);
            if (bar >= 0f)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.15f);
                GUI.DrawTexture(new Rect(x + 6f, y + h, w - 12f, 6f), Texture2D.whiteTexture);
                GUI.color = col;
                GUI.DrawTexture(new Rect(x + 6f, y + h, (w - 12f) * Mathf.Clamp01(bar), 6f), Texture2D.whiteTexture);
            }
            GUI.color = old;
            _hudStyle.normal.textColor = col;
            GUI.Label(new Rect(x, y, w, h), text, _hudStyle);
        }

        /// <summary>Приседание при касании: 0 в воздухе, горб на k 0.35 → 0.</summary>
        private static float Squash(float k) => k <= 0f || k >= 0.35f ? 0f : Mathf.Sin(k / 0.35f * Mathf.PI);

        // ───────────────────────── ТЕСТ ─────────────────────────

        [ContextMenu("Тест: собрать Аляску (Play, хост)")]
        private void TestAssemble()
        {
            if (!NetTest.ServerReady(this, "Аляска")) return;
            ResolveProducts();
            if (_sponge != null && !HasSponge) _cw.ServerAddPinch(_sponge.Id, FoodState.Pieces, 1, 0);
            if (_iceCream != null && !HasIceCream) _cw.ServerAddPinch(_iceCream.Id, _iceCream.StartState, _iceFull, 0);
            if (_meringue != null && !HasMeringue) _cw.ServerAddPinch(_meringue.Id, _meringue.StartState, _meringueFull, 0);
        }

        [ContextMenu("Тест: очистить тарелку")]
        private void TestClear()
        {
            if (!NetTest.ServerReady(this, "Аляска")) return;
            _cw.ServerClear();
            ResetToast();
        }

        [ContextMenu("Тест: подрумянить всё (0.45)")]
        private void TestToast()
        {
            if (_cap != null) _cap.Toast = 0.45f;     // на хосте сервер сам досчитает готовность до 100% → звёздочки
        }

        [ContextMenu("Тест: звёздочки")]
        private void TestStars()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Аляска] звёздочки — только в Play", this); return; }
            PlayStars();
        }

#if UNITY_EDITOR
        public void EditorSetup(Transform anchor, Vector3 spongeSize, Material sponge, Material ice, Material meringue)
        {
            _anchor = anchor; _spongeSize = spongeSize; _spongeMat = sponge; _iceMat = ice; _meringueMat = meringue;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
