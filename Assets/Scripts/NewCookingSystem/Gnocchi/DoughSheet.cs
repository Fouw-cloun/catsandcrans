using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;
using CatCooks.Food.Tools;

namespace CatCooks.Food
{
    /// <summary>Продукт «Пельмени»: Слеплено = сырые, Сварено = готовые, Переварено = разварились (правила — группа «Тесто»).</summary>
    public static class PelmeniProduct
    {
        public const string Title = "Пельмени";
        public const FoodState Raw = FoodState.Shaped, Done = FoodState.Boiled, Over = FoodState.Overcooked;
        private static ProductDef _def; private static FoodDatabase _forDb;
        public static ProductDef Def
        {
            get
            {
                FoodDatabase db = FoodDatabase.Instance;
                if (db == null) return null;
                if (_def != null && _forDb == db) return _def;
                _forDb = db; _def = null;
                for (int id = 1; id < 4096 && _def == null; id++)
                {
                    ProductDef p = db.Get((ushort)id);
                    if (p != null && p.Title == Title) _def = p;
                }
                return _def;
            }
        }
        public static ushort Id => Def != null ? Def.Id : (ushort)0;
        public static bool Is(ushort product) => product != 0 && product == Id;
    }

    /// <summary>Тип теста: пресет толщины, мягкости и вида. «Своё» — поля руками.</summary>
    public enum DoughKind : byte { Gnocchi = 0, Pelmeni = 1, Custom = 2 }

    /// <summary>
    /// БОЛЬШОЕ ТЕСТО (ньокки, потом пельмени) — мягкий шар на подстилке, который плющат и раскатывают.
    /// Внутри — мелкая сетка толщины, объём сохраняется, тесто ОПЛЫВАЕТ (круче Max Slope не держится) —
    /// поэтому ведёт себя как масса: где надавили — уходит в стороны, горбом перед вальцом, краем дальше.
    ///   • КУВАЛДА: удар сминает пятно (до Hammer Min) — тесто выдавливается кольцом вокруг и оплывает.
    ///   • КАТОК: можно въехать прямо в шар — валец ПРОДАВЛИВАЕТ тесто, оно выдавливается вперёд и в стороны
    ///     (горка с другой стороны), каток постепенно заезжает наверх. По пласту — раскатывает тоньше и шире.
    ///   • МУКА: мастерком ПКМ мукой по тесту. Без муки липнет к вальцу и кувалде — рвётся кусочками (минус).
    /// Поставили выше пола — падает и садится на пол / подстилку.
    /// ═══ НА КОРНЕ ═══ NetworkObject · Mesh Filter · Mesh Renderer · Mesh Collider · этот компонент. Без Rigidbody.
    /// Тест: CatCooks → Ньокки → 4. Ком теста в сцену.
    /// ЗАМЕС: пельменное тесто из бетономешалки / ведра вываливается таким же комом (DoughSheetDump, CatCooks → Пельмени).
    /// ПЕЛЬМЕНИ: кружки ведром (BucketDoughCutter), начинка мастерком, плоскогубцы (PliersItem) — см. блок «ПЕЛЬМЕНИ» ниже.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class DoughSheet : NetworkBehaviour, IHammerTarget, IScoopReceiver, ISpatulaTarget, IScoopSource
    {
        [Header("Тип теста (пресет)")]
        [Tooltip("Ньокки — толстое жёлтое картофельное, комковатое. Пельмени — тонкое, тугое, гладкое. Своё — поля ниже руками.\nСменил — поля ниже выставятся сами")]
        [SerializeField] private DoughKind _kind = DoughKind.Gnocchi;
        [SerializeField, HideInInspector] private DoughKind _kindApplied = DoughKind.Custom;
        [SerializeField, HideInInspector] private int _presetVer;
        private const int PresetVersion = 3;      // поменял числа в пресете — +1, и у теста в сцене они обновятся сами
        [Tooltip("Комковатость вида: пятна цвета и мелкие бугорки (0 — гладкое)")]
        [Range(0f, 1f)][SerializeField] private float _lumpy = 1f;
        [Tooltip("Высота бугорков, м")]
        [SerializeField] private float _lumpHeight = 0.02f;

        [Header("Сетка")]
        [Range(32, 128)][SerializeField] private int _n = 96;
        [Tooltip("Клетка, м (при масштабе мяса 4)")]
        [SerializeField] private float _cell = 0.065f;
        [SerializeField] private float _maxH = 1.6f;

        [Header("Шар")]
        [Tooltip("Радиус шара теста, м")]
        [SerializeField] private float _ballRadius = 0.75f;
        [Tooltip("Насколько шар оседает лёжа (0 — шар, 0.3 — приплюснут)")]
        [Range(0f, 0.6f)][SerializeField] private float _sag = 0.25f;

        [Header("Мягкость")]
        [Tooltip("Круче этого (высота/ширина) тесто не держится — оплывает. Меньше — жиже")]
        [SerializeField] private float _maxSlope = 1.2f;
        [Range(0, 6)][SerializeField] private int _relaxPasses = 1;
        [Tooltip("Как быстро оплывает (0.05 — густое, держит вмятины и горбы; 0.15 — жидкое)")]
        [Range(0.01f, 0.2f)][SerializeField] private float _flowRate = 0.05f;

        [Header("Кувалда")]
        [SerializeField] private float _hammerRadius = 0.7f;
        [Tooltip("Доля толщины, что сминается в центре удара")]
        [Range(0.05f, 0.9f)][SerializeField] private float _hammerSquash = 0.45f;
        [Tooltip("Тоньше кувалда не сплющит, м")]
        [SerializeField] private float _hammerMin = 0.15f;

        [Header("Каток")]
        [Tooltip("Тоньше каток не раскатает, м")]
        [SerializeField] private float _rollMin = 0.04f;
        [Tooltip("Доля «лишней» толщины за проезд, когда каток едет ПО тесту")]
        [Range(0.05f, 0.8f)][SerializeField] private float _rollPerPass = 0.3f;
        [Tooltip("Въехал в толщу (валец ниже верха) — доля лишнего, что продавливается за кадр-проход")]
        [Range(0.05f, 1f)][SerializeField] private float _plowRate = 0.8f;
        [Tooltip("Валец касается теста, если его низ не выше верха теста больше чем на, м")]
        [SerializeField] private float _rollContact = 0.05f;
        [Tooltip("Каток едет ПО слою теста не толще этого, м. Толще — срезает круглым вальцом и толкает кучу вперёд")]
        [SerializeField] private float _rideMax = 0.12f;
        [Tooltip("Голый валец (без муки): какая доля теста под ним прилипает за проезд клетки")]
        [Range(0.05f, 0.6f)][SerializeField] private float _stickBite = 0.1f;
        [Tooltip("Доля срезанного, что размазывается в каждую сторону от вальца")]
        [Range(0f, 0.25f)][SerializeField] private float _smearSide = 0.04f;
        [Tooltip("Куча перед вальцом едет вместе с ним: 1 — со скоростью катка (толкает), меньше — валец её догоняет и сминает")]
        [Range(0.5f, 1f)][SerializeField] private float _pushCarry = 1f;
        [Tooltip("Насколько медленнее едет дальняя часть кучи (в метре впереди) — куча немного сжимается и густеет")]
        [Range(0f, 0.5f)][SerializeField] private float _pushCarryLoss = 0.15f;
        [Tooltip("Крайние куски кучи уходят в стороны (доля клетки за шаг) — размазывается веером")]
        [Range(0f, 1f)][SerializeField] private float _pushFan = 0.5f;
        [Tooltip("Куча перед вальцом круче этого (высота/длина) — осыпается. Меньше — ниже и шире")]
        [Range(0.2f, 1.5f)][SerializeField] private float _pushSlope = 0.6f;

        [Header("Форма (без «сумасшествия»)")]
        [Tooltip("Сглаживание бугров и ряби за проход (объём тот же)")]
        [Range(0f, 0.25f)][SerializeField] private float _smooth = 0.08f;
        [Tooltip("Тонкие «пальцы» и висящие клетки по краю втягиваются обратно в пласт")]
        [SerializeField] private bool _cohesion = true;

        [Header("Цель (подсказка)")]
        [SerializeField] private float _targetMin = 0.06f;
        [SerializeField] private float _targetMax = 0.10f;

        [Header("Нарезка шпателем")]
        [Tooltip("Префаб КУСКА теста (меню 6 → сохрани префабом → сюда). Кусок — такое же тесто, только своё и маленькое")]
        [SerializeField] private GameObject _piecePrefab;
        [Tooltip("Длина реза за один удар, м. Рез ложится по линии сетки (вдоль X или Z — куда ближе лезвие)")]
        [SerializeField] private float _bladeLen = 0.8f;
        [Tooltip("Ньокк «в самый раз»: длина и ширина, м (при масштабе мира 4)")]
        [SerializeField] private float _pieceMin = 0.12f;
        [SerializeField] private float _pieceMax = 0.21f;
        [Tooltip("Это заготовка КУСКА (префаб для нарезки): при появлении пустой, тесто приходит от родителя")]
        [SerializeField] private bool _startEmpty;

        [Header("Мука")]
        [SerializeField] private ProductDef _flour;
        [SerializeField] private float _flourPerPortion = 0.25f;

        [Header("Вид")]
        [SerializeField] private Color _doughColor = new Color(0.95f, 0.88f, 0.68f);
        [Tooltip("Повторов текстуры на метр")]
        [SerializeField] private float _texPerMeter = 0.8f;
        [SerializeField] private bool _log = true;

        private float[] _h, _flow, _blur;
        private float _flourAmt, _vy;
        private float[] _dirt;                 // 1 — клетка лежала на голом полу (грязь)
        private float _stuckVol, _dirtAt; private bool _dirtCheck = true, _dirtWarned, _warnNoCoat;
        private Vector3 _dirtPos;
        private byte _qNet = 255, _dirtNet;    // у клиентов — с сервера
        private int _stuck;
        private bool _grounded, _dirtyNet, _dirtyMesh = true, _needRelax;
        private float _sendAt, _meshAt, _colAt, _relaxAt, _hintAt, _ignoreAt;
        private readonly Vector3[] _lastDrum = new Vector3[16];
        private readonly RoadRollerVehicle[] _lastRoller = new RoadRollerVehicle[16];
        private readonly HashSet<RoadRollerVehicle> _ignored = new HashSet<RoadRollerVehicle>();
        private MeshFilter _mf;
        private MeshCollider _mc;
        private MeshRenderer _mr;
        private Mesh _mesh;
        private float _cw = -1f;
        private static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 }, DY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        private float S => Cell / _cell;
        private float Cell { get { if (_cw <= 0f) _cw = _cell * Mathf.Max(0.25f, MeatCutSettings.WorldScale) / 4f; return _cw; } }
        private float Half => _n * Cell * 0.5f;
        public float Flour01 => _flourAmt;
        /// <summary>Карта грязи совпадает с сеткой (после смены N в инспекторе — нет, пока не пересоздана).</summary>
        private bool DirtOk => _dirt != null && _h != null && _dirt.Length == _h.Length;
        /// <summary>Доля теста (по объёму), что лежала на голом полу.</summary>
        public float Dirty01
        {
            get
            {
                if (!base.IsServerInitialized) return _dirtNet / 255f;
                float v = 0f, vd = 0f;
                for (int i = 0; i < _h.Length; i++) { v += _h[i]; if (DirtOk && _dirt[i] > 0.5f) vd += _h[i]; }
                return v > 1e-6f ? vd / v : 0f;
            }
        }
        /// <summary>Качество теста 0..1: грязь с пола (сильно), налипло и порвалось (инструмент без муки).</summary>
        public float Quality01
        {
            get
            {
                if (!base.IsServerInitialized) return _qNet / 255f;
                return Mathf.Clamp01(QualityNoDirt - 0.85f * Dirty01);
            }
        }
        /// <summary>Качество без грязи (её кусок несёт своей картой): налипания, разрывы и то, что досталось от родителя.</summary>
        private float QualityNoDirt
        {
            get
            {
                float v0 = _vol0 > 0f ? _vol0 : Mathf.Max(1e-4f, Volume);
                return Mathf.Clamp01(_qBase - 2.5f * _stuckVol / v0 - 0.02f * _stuck);
            }
        }
        private float _qBase = 1f;

        // ───────────────────────── жизнь ─────────────────────────

        private void Awake()
        {
            _mf = GetComponent<MeshFilter>();
            _mc = GetComponent<MeshCollider>();
            _mr = GetComponent<MeshRenderer>();
            _h = new float[_n * _n];
            _dirt = null;
            if (!_startEmpty) MakeBall();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (_flour == null) _flour = FindFlour();
            _grounded = false;
            _dirtyNet = true;
            if (_log && !_startEmpty) Debug.Log($"[Тесто] «{name}»: шар {_ballRadius * S * 2f:0.0} м, объём {Volume:0.00} м³, сетка {_n}×{_n} по {Cell * 100f:0.0} см · " +
                                "кувалда — сплющить, каток — въезжай прямо в тесто. Мука — на ИНСТРУМЕНТЫ (вальцы, кувалда), тесто держи на подстилке", this);
        }

        private void OnDestroy() { if (_mesh != null) SafeDestroy.Kill(_mesh); if (_circMesh != null) SafeDestroy.Kill(_circMesh); }

        private void MakeBall()
        {
            float R = _ballRadius * S, H = 2f * R * (1f - _sag), Rb = R * (1f + _sag * 0.6f);
            for (int y = 0; y < _n; y++)
                for (int x = 0; x < _n; x++)
                {
                    float r = CellPos(x, y).magnitude / Mathf.Max(0.01f, Rb);
                    _h[y * _n + x] = r < 1f ? H * Mathf.Pow(1f - r * r, 0.42f) : 0f;   // приплюснутый шар: круглый бок, плотная шапка
                }
            Relax(4);
            if (_dirt != null) System.Array.Clear(_dirt, 0, _dirt.Length);
            _stuckVol = 0f; _stuck = 0; _dirtCheck = true;
            _dirtyMesh = true;
        }

        private Vector2 CellPos(int x, int y) => new Vector2((x + 0.5f) * Cell - Half, (y + 0.5f) * Cell - Half);

        private bool CellAt(Vector3 world, out int x, out int y)
        {
            Vector3 l = world - transform.position;
            x = Mathf.FloorToInt((l.x + Half) / Cell); y = Mathf.FloorToInt((l.z + Half) / Cell);
            return x >= 0 && y >= 0 && x < _n && y < _n;
        }

        public float Volume { get { float s = 0f; foreach (float v in _h) s += v; return s * Cell * Cell; } }

        // ───────────────────────── оплывание ─────────────────────────

        /// <summary>Где перепад с соседом круче Max Slope — часть теста перетекает вниз (во все 8 сторон). Объём тот же.</summary>
        private void Relax(int passes)
        {
            if (passes <= 0) return;
            float lim = _maxSlope * Cell;
            int n = _n;
            if (_flow == null || _flow.Length != _h.Length) _flow = new float[_h.Length];
            for (int it = 0; it < passes; it++)
            {
                System.Array.Clear(_flow, 0, _flow.Length);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int i = y * n + x;
                        float h = _h[i];
                        if (h <= 0.0005f) continue;
                        for (int k = 0; k < 8; k++)
                        {
                            int xx = x + DX[k], yy = y + DY[k];
                            if (xx < 0 || yy < 0 || xx >= n || yy >= n) continue;
                            int j = yy * n + xx;
                            bool diag = k >= 4;
                            float d = h - _h[j], l = diag ? lim * 1.4142f : lim;
                            if (d <= l) continue;
                            float t = (d - l) * (diag ? _flowRate * 0.7f : _flowRate);
                            _flow[i] -= t; _flow[j] += t;
                        }
                    }
                for (int i = 0; i < _h.Length; i++) _h[i] = Mathf.Max(0f, _h[i] + _flow[i]);
            }
        }

        // ───────────────────────── кувалда ─────────────────────────

        [Server]
        public void ServerHammerHit(Vector3 point, Vector3 dir)
        {
            if (!CellAt(point, out int cx, out int cy)) return;
            float R = _hammerRadius * S, hmin = _hammerMin * S;
            // мука на кувалде: нет — бьёт слабее и тесто прилипает к бойку
            ToolFlourCoat tool = ToolFlourCoat.Nearest(point, 4f * S);
            if (tool == null && !_warnNoCoat && _log) { _warnNoCoat = true; Debug.LogWarning($"[Тесто] «{name}»: у кувалды нет Tool Flour Coat (CatCooks → Ньокки → 5) — мука на ней не учитывается, тесто не липнет", this); }
            if (tool != null && tool.IsStuck) { Hint($"«{tool.Title}» застряла в тесте — бить нельзя. Брось её и выдолби шпателем"); RpcPuff(point, 0.3f); return; }
            bool floured = tool == null || tool.ServerUseHit();
            float squash = _hammerSquash * (floured ? 1f : 0.4f);
            int rr = Mathf.CeilToInt(R * 1.9f / Cell);
            Vector2 c0 = CellPos(cx, cy);
            float removed = 0f, wSum = 0f;
            var ring = new List<int>(); var ringW = new List<float>();
            for (int y = Mathf.Max(0, cy - rr); y <= Mathf.Min(_n - 1, cy + rr); y++)
                for (int x = Mathf.Max(0, cx - rr); x <= Mathf.Min(_n - 1, cx + rr); x++)
                {
                    int i = y * _n + x;
                    float d = (CellPos(x, y) - c0).magnitude;
                    if (d < R)
                    {
                        float h = _h[i];
                        if (h <= hmin) continue;
                        float f = 1f - (d / R) * (d / R);
                        float nh = Mathf.Max(hmin, h - (h - hmin) * squash * f);
                        removed += h - nh; _h[i] = nh;
                    }
                    else if (d < R * 1.9f && (_h[i] > 0.002f || NearDough(x, y)))
                    {
                        float w = 1f - (d - R) / (R * 0.9f);
                        ring.Add(i); ringW.Add(w); wSum += w;
                    }
                }
            if (removed <= 0f) { Hint($"кувалдой уже не сплющить (тоньше {_hammerMin * S * 100f:0} см) — дальше катком"); RpcPuff(point, 0.4f); return; }
            if (wSum <= 0f) _h[cy * _n + cx] += removed;
            else for (int k = 0; k < ring.Count; k++) _h[ring[k]] += removed * ringW[k] / wSum;
            if (!floured && tool != null)
            {
                tool.ServerStickInto(point);
                _stuck++;
                Hint($"«{tool.Title}» без муки — ЗАСТРЯЛА в тесте (качество {Quality01 * 100f:0}%). Выдолби шпателем (ЛКМ по кувалде), потом посыпь её мукой");
            }
            if (_hasCuts || _hasSoft) HealCuts(point, R);
            Changed();
            RpcPuff(point, floured ? 1f : 0.3f);
            if (_log) Debug.Log($"[Тесто] «{name}»: удар кувалдой — {Stats()}", this);
        }

        private bool NearDough(int x, int y)
        {
            for (int k = 0; k < 8; k++)
            {
                int xx = x + DX[k], yy = y + DY[k];
                if (xx < 0 || yy < 0 || xx >= _n || yy >= _n) continue;
                if (_h[yy * _n + xx] > 0.002f) return true;
            }
            return false;
        }

        // ───────────────────────── мука ─────────────────────────

        /// <summary>Тесто мукой НЕ посыпают (и на пол не сыпем) — мука нужна на инструментах: вальцах катка и кувалде.
        /// Принимаем бросок, чтобы мастерок не высыпал кучку на тесто, — и отказываем с подсказкой.
        /// ТАКОЕ ЖЕ тесто (замес из тары мастерком) — прилипает к пласту бугром: можно докинуть теста.</summary>
        public int ServerReceiveScoop(ushort product, FoodState state, int count, Vector3 point, Vector3 normal, out string why)
        {
            // пельмени: начинка мастерком — в кружок
            if (count > 0 && base.IsServerInitialized && _kind == DoughKind.Pelmeni)
            {
                FoodDatabase fdb = FoodDatabase.Instance;
                ProductDef pd = fdb != null ? fdb.Get(product) : null;
                if (IsFilling(pd)) return ServerFillCircle(count, point, out why);
                if (pd != null && !string.IsNullOrEmpty(pd.Title) && pd.Title.ToLowerInvariant().Contains("фарш"))
                { why = "это просто фарш — для пельменей нужен «Фарш для пельменей» (фарш + лук + зелень + соль + перец, миксером)"; return 0; }
            }
            if (count > 0 && base.IsServerInitialized && DoughSheetDump.IsSheetDough(product, state, out DoughKind dk) && dk == _kind)
            {
                float v = PortionVolume * count;
                if (ServerDropLump(point, v))
                {
                    _vol0 = (_vol0 > 0f ? _vol0 : Volume - v) + v;
                    why = null;
                    if (_log) Debug.Log($"[Тесто] «{name}»: докинули теста мастерком — +{count} порц. ({v * 1000f:0} л), объём {Volume:0.00} м³", this);
                    return count;
                }
            }
            why = DoughStick.IsFlour(product)
                ? "тесто посыпать не надо — кинь муку на ИНСТРУМЕНТ: вальцы катка, кувалду (иначе липнет)"
                : "на тесто ничего не кидаем";
            return 0;
        }

        private ProductDef FindFlour()
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return null;
            for (int id = 1; id < 2048; id++)
            {
                ProductDef p = db.Table.GetProduct((ushort)id);
                if (p != null && !string.IsNullOrEmpty(p.Title) && p.Title.ToLowerInvariant().StartsWith("мука")) return p;
            }
            return null;
        }


        // ───────────────────────── каток, падение ─────────────────────────

        private void Update()
        {
            IgnoreRollers();
            if (base.IsServerInitialized)
            {
                FallTick(Time.deltaTime);
                RollerTick();
                DirtTick();
                if (_needRelax && Time.time >= _relaxAt) { _relaxAt = Time.time + 0.05f; _needRelax = false; Relax(_relaxPasses); Despike(); Smooth(); Cohesion(); Recenter(); _dirtyNet = true; _dirtyMesh = true; }
                NetTick();
            }
            if (_dirtyMesh && Time.time >= _meshAt) { _meshAt = Time.time + 0.1f; _dirtyMesh = false; BuildMesh(); }
        }

        /// <summary>Каток не упирается в тесто корпусом — въезжает и продавливает (лучи подвески тесто видят и заезжают наверх).</summary>
        private void IgnoreRollers()
        {
            if (_mc == null || Time.time < _ignoreAt) return;
            _ignoreAt = Time.time + 1f;
            foreach (RoadRollerVehicle v in RoadRollerVehicle.All)
            {
                if (v == null || _ignored.Contains(v)) continue;
                foreach (Collider c in v.GetComponentsInChildren<Collider>(true))
                {
                    if (c == null) continue;
                    Physics.IgnoreCollision(c, _mc, true);
                    if (_circCol != null) Physics.IgnoreCollision(c, _circCol, true);
                }
                _ignored.Add(v);
            }
        }

        [Server]
        private void FallTick(float dt)
        {
            if (_grounded) return;
            Vector3 p = transform.position;
            float ground = GroundBelow(p + Vector3.up * 0.6f, 60f);
            if (float.IsNaN(ground)) { _grounded = true; return; }
            if (p.y <= ground + 0.01f) { transform.position = new Vector3(p.x, ground, p.z); _grounded = true; _vy = 0f; _dirtyNet = true; return; }
            _vy -= 9.81f * dt;
            float ny = Mathf.Max(ground, p.y + _vy * dt);
            transform.position = new Vector3(p.x, ny, p.z);
            _dirtyNet = true;
            if (ny <= ground + 0.001f)
            {
                _grounded = true;
                float fall = -_vy; _vy = 0f;
                if (fall > 1.5f) { float k = Mathf.Clamp(fall * 0.025f, 0f, 0.15f); for (int i = 0; i < _h.Length; i++) _h[i] *= 1f - k; Relax(6); Normalize(); Changed(); }
                if (_log) Debug.Log($"[Тесто] «{name}»: упало и село на пол (скорость {fall:0.0} м/с)", this);
            }
        }

        private float _vol0 = -1f;
        /// <summary>После «шлёпа» вернуть объём (сплющилось — значит шире; оплывание разнесло край).</summary>
        private void Normalize()
        {
            if (_vol0 <= 0f) return;
            float v = Volume;
            if (v <= 1e-6f) return;
            float k = _vol0 / v;
            for (int i = 0; i < _h.Length; i++) _h[i] *= k;
        }

        private float GroundBelow(Vector3 from, float dist)
        {
            RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, dist, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NaN;
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
                if (h.collider.GetComponentInParent<RoadRollerVehicle>() != null) continue;
                if (h.collider.GetComponentInParent<DoughSheet>() != null) continue;           // другое тесто (куски, пласт) — не пол
                if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue;
                if (h.point.y > from.y - 0.05f) continue;
                if (float.IsNaN(best) || h.point.y > best) best = h.point.y;
            }
            return best;
        }

        [Server]
        private void RollerTick()
        {
            if (!_grounded) return;
            int slot = 0;
            foreach (RoadRollerVehicle v in RoadRollerVehicle.All)
            {
                if (v == null) continue;
                for (int d = 0; d < 2 && slot < _lastDrum.Length; d++, slot++)
                {
                    if (!v.DrumContact(d, out Vector3 g, out Vector3 axle)) continue;
                    Vector3 last = _lastRoller[slot] == v ? _lastDrum[slot] : g;
                    _lastDrum[slot] = g; _lastRoller[slot] = v;
                    Vector3 move = g - last; move.y = 0f;
                    float travel = move.magnitude;
                    if (travel < 1e-4f || travel > 1f) continue;
                    Vector3 fwd = move / travel;
                    if (v.DrumJammed(d)) continue;                                  // залип — не катится
                    bool onDough = HeightAt(g, out _) || HeightAt(g + fwd * v.DrumRadius * 0.9f, out _);
                    bool floured = !onDough || v.ServerDrumFlour(d, travel);
                    RollLine(g, axle, fwd, travel, v.DrumWidth, v.DrumRadius);
                    if (_hasCuts || _hasSoft) HealCuts(g, v.DrumWidth * 0.6f);
                    if (!onDough) continue;
                    if (_circles.Count > 0) CrushCircles(g, axle, v.DrumWidth);       // каток по кружкам — смял
                    if (!floured)
                    {
                        // голый валец — тесто сразу хватает его: кусок пласта уходит на валец, каток ЗАЛИП
                        float st = StickUnder(g, axle, fwd, Cell * 2f, v.DrumWidth);
                        v.ServerDrumJam(d, st); _stuckVol += st; _stuck++;
                        Changed();
                        Hint($"валец без муки — ЗАЛИП в тесте (качество {Quality01 * 100f:0}%)! Слезь, поруби налипшее шпателем (ЛКМ по вальцу), посыпь вальцы мукой");
                        continue;
                    }
                    float shed = v.ServerDrumShed(d, travel);
                    if (shed > 0f) ServerDropLump(g - fwd * v.DrumRadius * 0.6f, shed);
                }
            }
        }

        /// <summary>Валец проехал travel м — как отвал бульдозера с круглым ножом.
        /// Передняя половина вальца (его круглый профиль) срезает всё, что выше неё, и гонит КУЧУ перед собой:
        /// срезанное ложится валом сразу перед вальцом, лишнее с вала ссыпается дальше вперёд и в стороны (размазывается).
        /// Под самым низом вальца тесто прижимается до слоя, по которому каток едет (Ride Max), а поверху — раскатывается по чуть-чуть.</summary>
        private void RollLine(Vector3 ground, Vector3 axle, Vector3 fwd, float travel, float width, float radius)
        {
            float c = Cell, hmin = _rollMin * S, contact = _rollContact * S, R = Mathf.Max(c, radius);
            Vector3 l = ground - transform.position;
            Vector2 la = new Vector2(axle.x, axle.z).normalized, lf = new Vector2(fwd.x, fwd.z).normalized;
            float kRoll = Mathf.Clamp01(travel / c * _rollPerPass);
            float kPlow = Mathf.Clamp01(_plowRate * Mathf.Max(0.3f, travel / c));
            float bottom = Mathf.Max(0f, l.y);
            Vector2 g = new Vector2(l.x, l.z);
            int nu = Mathf.Max(1, Mathf.CeilToInt(width / (c * 0.5f)));
            int nd = Mathf.Max(1, Mathf.CeilToInt(R * 0.97f / (c * 0.5f)));

            // 1) пятно вальца: от низа до передней кромки
            _under.Clear();
            float removed = 0f, plowed = 0f;
            for (int s = 0; s <= nu; s++)
                for (int t = -1; t <= nd; t++)
                {
                    Vector2 p = g + la * (-width * 0.5f + width * s / nu) + lf * (t * c * 0.5f);
                    if (!Idx(p, out int i, out int x, out int y) || !_under.Add(i)) continue;
                    float h = _h[i];
                    if (h <= hmin + 0.001f) continue;
                    float dd = Vector2.Dot(CellPos(x, y) - g, lf);
                    float dc = Mathf.Clamp(dd, 0f, R * 0.97f);
                    float surf = Mathf.Max(hmin, bottom + R - Mathf.Sqrt(R * R - dc * dc));   // круглый профиль вальца
                    float nh = h;
                    if (h > surf + contact) nh = Mathf.Max(surf, h - (h - surf) * kPlow);        // срезал кучу — погнал вперёд
                    else if (dd < c * 0.75f && h > bottom - contact) nh = h - (h - hmin) * kRoll; // едет поверху — раскатывает
                    if (nh >= h) continue;
                    removed += h - nh; _h[i] = nh;
                    if (h > surf + contact) plowed += h - nh;
                }
            if (removed <= 0f) return;

            // 2) срезанное — сразу перед вальцом (по ширине), чуть-чуть по бокам
            float side = removed * _smearSide;
            Vector2 row0 = g + lf * (nd * c * 0.5f + c);                                   // точно за пятном вальца
            _bow.Clear();
            for (int s = 0; s <= nu; s++) if (Idx(row0 + la * (-width * 0.5f + width * s / nu), out int i, out _, out _) && !_under.Contains(i)) _bow.Add(i);
            if (_bow.Count == 0) DropNear(row0, removed - side * 2f); else foreach (int i in _bow) _h[i] += (removed - side * 2f) / _bow.Count;
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                _bow.Clear();
                for (int t = 0; t <= nd + 2; t++)
                    if (Idx(g + la * (sgn * (width * 0.5f + c * 0.6f)) + lf * (t * c * 0.5f), out int i, out _, out _) && !_under.Contains(i)) _bow.Add(i);
                if (_bow.Count == 0) DropNear(row0, side); else foreach (int i in _bow) _h[i] += side / _bow.Count;
            }

            // 3) КУЧУ ВПЕРЁД: всё тесто перед вальцом (выше слоя, по которому каток едет) едет вместе с вальцом —
            //    ряд за рядом от дальнего к ближнему, каждый кусок сдвигается на пройденный путь (не дальше клетки за кадр).
            //    По краям полосы — плавно меньше (без стенок), крайние куски слегка уходят в стороны (размазывается веером).
            PushAhead(g, lf, la, travel, width, R, bottom, contact);

            Changed();
            if (Time.time >= _hintAt + 3f && _log)
            {
                _hintAt = Time.time;
                Debug.Log($"[Тесто] «{name}»: каток — {(plowed > removed * 0.5f ? $"толкает кучу (срезал {plowed * c * c * 1000f:0} л за кадр)" : "раскатывает")} · {Stats()}", this);
            }
        }


        private bool _edgeWarned;

        /// <summary>Голый валец: тесто под ним прилипает и уходит на валец (пласт рвётся). Возвращает м³.</summary>
        private float StickUnder(Vector3 ground, Vector3 axle, Vector3 fwd, float travel, float width)
        {
            float c = Cell;
            Vector3 l = ground - transform.position;
            Vector2 g = new Vector2(l.x, l.z), la = new Vector2(axle.x, axle.z).normalized, lf = new Vector2(fwd.x, fwd.z).normalized;
            float f = Mathf.Min(0.45f, _stickBite * travel / c);
            int nu = Mathf.Max(1, Mathf.CeilToInt(width / (c * 0.5f)));
            _under.Clear();
            float got = 0f;
            for (int s = 0; s <= nu; s++)
                for (int t = -1; t <= 1; t++)
                {
                    if (!Idx(g + la * (-width * 0.5f + width * s / nu) + lf * (t * c * 0.5f), out int i, out _, out _) || !_under.Add(i)) continue;
                    float take = _h[i] * f; _h[i] -= take; got += take;
                }
            return got * c * c;
        }

        /// <summary>Кусок теста (м³) шлёпнулся на пласт бугром. false — мимо теста (не над сеткой).</summary>
        [Server]
        public bool ServerDropLump(Vector3 world, float vol)
        {
            if (vol <= 1e-6f || !CellAt(world, out int cx, out int cy)) return false;
            float r = Mathf.Max(Cell * 1.5f, Mathf.Pow(vol, 1f / 3f) * 0.9f);
            int rr = Mathf.CeilToInt(r * 2f / Cell);
            Vector2 c0 = CellPos(cx, cy);
            float wSum = 0f;
            for (int pass = 0; pass < 2; pass++)
                for (int y = Mathf.Max(0, cy - rr); y <= Mathf.Min(_n - 1, cy + rr); y++)
                    for (int x = Mathf.Max(0, cx - rr); x <= Mathf.Min(_n - 1, cx + rr); x++)
                    {
                        float d = (CellPos(x, y) - c0).magnitude / r;
                        if (d >= 2f) continue;
                        float w = Mathf.Exp(-d * d * 1.5f);
                        if (pass == 0) wSum += w; else _h[y * _n + x] += vol / (Cell * Cell) * w / wSum;
                    }
            Changed();
            return true;
        }

        /// <summary>Сервер, 2 раза в секунду (если тесто менялось/двигалось): что под каждым кусочком пласта —
        /// подстилка (Food Clean Surface) или голый пол. На голом — грязь (навсегда), качество падает.</summary>
        [Server]
        private void DirtTick()
        {
            if (!_grounded || Time.time < _dirtAt) return;
            _dirtAt = Time.time + 0.5f;
            if (!_dirtCheck && (transform.position - _dirtPos).sqrMagnitude < 1e-4f) return;
            _dirtCheck = false; _dirtPos = transform.position;
            if (_dirt == null || _dirt.Length != _h.Length) _dirt = new float[_h.Length];
            int n = _n, added = 0; float baseY = transform.position.y;
            for (int y = 0; y < n; y += 2)
                for (int x = 0; x < n; x += 2)
                {
                    bool need = false;
                    for (int dy = 0; dy < 2 && y + dy < n; dy++) for (int dx = 0; dx < 2 && x + dx < n; dx++) { int i = (y + dy) * n + x + dx; if (_h[i] > 0.004f && _dirt[i] < 0.5f) need = true; }
                    if (!need) continue;
                    Vector3 wp = transform.position + new Vector3((x + 1) * Cell - Half, 0.6f, (y + 1) * Cell - Half);
                    if (CleanBelow(wp, baseY)) continue;
                    for (int dy = 0; dy < 2 && y + dy < n; dy++) for (int dx = 0; dx < 2 && x + dx < n; dx++) { int i = (y + dy) * n + x + dx; if (_h[i] > 0.004f && _dirt[i] < 0.5f) { _dirt[i] = 1f; added++; } }
                }
            if (added <= 0) return;
            _dirtyNet = true; _dirtyMesh = true;
            if (_log && (!_dirtWarned || Time.time >= _hintAt + 3f))
            {
                _dirtWarned = true; _hintAt = Time.time;
                Debug.LogWarning($"[Тесто] «{name}»: тесто на ГОЛОМ ПОЛУ — пачкается ({Dirty01 * 100f:0}% грязное), качество {Quality01 * 100f:0}%. Кидай и раскатывай на подстилке (рулон гидроизоляции)", this);
            }
        }

        private readonly RaycastHit[] _dh = new RaycastHit[12];
        private bool CleanBelow(Vector3 from, float baseY)
        {
            int k = Physics.RaycastNonAlloc(from, Vector3.down, _dh, 2.5f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity; Collider bc = null;
            for (int i = 0; i < k; i++)
            {
                RaycastHit h = _dh[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
                if (h.collider.GetComponentInParent<DoughSheet>() != null) continue;      // соседний кусок теста — не пол
                if (h.point.y > baseY + 0.08f) continue;                                  // игрок, каток, предметы над тестом
                if (h.point.y > best) { best = h.point.y; bc = h.collider; }
            }
            return bc == null || bc.GetComponentInParent<FoodCleanSurface>() != null;    // ничего под — не пачкаем
        }
        private readonly List<int> _zone = new List<int>();
        private readonly List<float> _zoneHalf = new List<float>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly Dictionary<int, float> _zflow = new Dictionary<int, float>();

        private void PushAhead(Vector2 g, Vector2 lf, Vector2 la, float travel, float width, float R, float bottom, float contact)
        {
            float c = Cell, floor = Mathf.Max(bottom, _rideMax * S) + contact, hw = width * 0.5f;
            _zone.Clear(); _zoneHalf.Clear(); _seen.Clear();
            int empty = 0, rowStart = 0;
            var rowEnds = _rowEnds; rowEnds.Clear();
            for (int k = 0; k < 160 && empty < 4; k++)
            {
                float d = R * 0.6f + k * c * 0.5f;
                float half = Mathf.Min(hw + d * 0.25f, hw + 0.6f * S);
                int nk = Mathf.Max(1, Mathf.CeilToInt(half * 2f / (c * 0.5f)));
                bool any = false;
                for (int s = 0; s <= nk; s++)
                {
                    if (!Idx(g + lf * d + la * (-half + half * 2f * s / nk), out int i, out _, out _) || !_seen.Add(i)) continue;
                    if (_h[i] > floor) { _zone.Add(i); _zoneHalf.Add(half); any = true; }
                }
                rowEnds.Add(_zone.Count);
                empty = any ? 0 : empty + 1;
            }
            // от дальнего ряда к ближнему: каждый кусок двигается один раз за кадр
            for (int r = rowEnds.Count - 1; r >= 0; r--)
            {
                rowStart = r > 0 ? rowEnds[r - 1] : 0;
                for (int z = rowEnds[r] - 1; z >= rowStart; z--)
                {
                    int i = _zone[z];
                    Vector2 p = CellPos(i % _n, i / _n);
                    float u = Vector2.Dot(p - g, la), dd = Vector2.Dot(p - g, lf);
                    float wq = Mathf.Clamp01((_zoneHalf[z] - Mathf.Abs(u)) / (c * 3f));
                    float carry = _pushCarry - _pushCarryLoss * Mathf.Clamp01((dd - R) / (1f * S));
                    float q = Mathf.Min(0.95f, travel * carry / c);
                    float ex = (_h[i] - floor) * q * wq;
                    if (ex <= 0f) continue;
                    float t = Mathf.Clamp01(Mathf.Abs(u) / Mathf.Max(c, hw));
                    Vector2 to = p + lf * c + la * (Mathf.Sign(u == 0f ? 1f : u) * c * _pushFan * t * t);
                    if (Splat(to, ex)) _h[i] -= ex;
                }
            }
            ZoneRelax();
        }

        private readonly List<int> _rowEnds = new List<int>();

        /// <summary>Объём в точку с раскладкой по 4 клеткам (плавно, без ступенек). false — за краем сетки.</summary>
        private bool Splat(Vector2 t, float v)
        {
            float fx = (t.x + Half) / Cell - 0.5f, fy = (t.y + Half) / Cell - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            if (x0 < 0 || y0 < 0 || x0 + 1 >= _n || y0 + 1 >= _n) return false;
            float tx = fx - x0, ty = fy - y0; int i = y0 * _n + x0;
            _h[i] += v * (1f - tx) * (1f - ty); _h[i + 1] += v * tx * (1f - ty);
            _h[i + _n] += v * (1f - tx) * ty; _h[i + _n + 1] += v * tx * ty;
            return true;
        }

        /// <summary>В зоне толкания куча не встаёт стеной: круче Push Slope — быстро осыпается к соседям (вперёд, в стороны).</summary>
        private void ZoneRelax()
        {
            float lim = _pushSlope * Cell;
            _zflow.Clear();
            foreach (int i in _zone)
            {
                int x = i % _n, y = i / _n; float h = _h[i];
                for (int k = 0; k < 4; k++)
                {
                    int xx = x + DX[k], yy = y + DY[k];
                    if (xx < 0 || yy < 0 || xx >= _n || yy >= _n) continue;
                    int j = yy * _n + xx; float d = h - _h[j];
                    if (d <= lim) continue;
                    float t = (d - lim) * 0.075f;
                    _zflow[i] = (_zflow.TryGetValue(i, out float a) ? a : 0f) - t;
                    _zflow[j] = (_zflow.TryGetValue(j, out float b) ? b : 0f) + t;
                }
            }
            foreach (KeyValuePair<int, float> kv in _zflow) _h[kv.Key] = Mathf.Max(0f, _h[kv.Key] + kv.Value);
        }

        /// <summary>Сглаживание между соседними клетками С тестом (край не расползается, объём тот же): гасит рябь и острые гребни.</summary>
        private void Smooth()
        {
            if (_smooth <= 0f) return;
            int n = _n; float eps = 0.004f;
            if (_flow == null || _flow.Length != _h.Length) _flow = new float[_h.Length];
            System.Array.Clear(_flow, 0, _flow.Length);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x; float h = _h[i];
                    if (h <= eps) continue;
                    if (x + 1 < n) { int j = i + 1; if (_h[j] > eps) { float t = (h - _h[j]) * _smooth * 0.5f; _flow[i] -= t; _flow[j] += t; } }
                    if (y + 1 < n) { int j = i + n; if (_h[j] > eps) { float t = (h - _h[j]) * _smooth * 0.5f; _flow[i] -= t; _flow[j] += t; } }
                }
            for (int i = 0; i < _h.Length; i++) _h[i] = Mathf.Max(0f, _h[i] + _flow[i]);
        }

        /// <summary>Одиночные «иглы»: клетка намного выше всех четырёх соседей — лишнее раздаём соседям (объём тот же). Убирает «взрывы» в пару пикселей.</summary>
        private void Despike()
        {
            int n = _n; float lim = _maxSlope * Cell * 1.5f;
            for (int y = 1; y < n - 1; y++)
                for (int x = 1; x < n - 1; x++)
                {
                    int i = y * n + x; float h = _h[i];
                    if (h <= 0.004f) continue;
                    float mx = Mathf.Max(Mathf.Max(_h[i - 1], _h[i + 1]), Mathf.Max(_h[i - n], _h[i + n]));
                    if (h - mx <= lim) continue;
                    float ex = (h - mx - lim * 0.5f) * 0.25f;
                    _h[i] -= ex * 4f; _h[i - 1] += ex; _h[i + 1] += ex; _h[i - n] += ex; _h[i + n] += ex;
                }
        }

        /// <summary>«Поверхностное натяжение»: клетка на краю, у которой ≤2 соседей с тестом (палец, капля), отдаёт себя самому толстому соседу.</summary>
        private void Cohesion()
        {
            if (!_cohesion) return;
            int n = _n; float eps = 0.004f;
            for (int y = 1; y < n - 1; y++)
                for (int x = 1; x < n - 1; x++)
                {
                    int i = y * n + x; float h = _h[i];
                    if (h <= 0f) continue;
                    int cnt = 0, best = -1; float bh = 0f;
                    for (int k = 0; k < 8; k++)
                    {
                        int j = (y + DY[k]) * n + x + DX[k];
                        if (_h[j] > eps) { cnt++; if (_h[j] > bh) { bh = _h[j]; best = j; } }
                    }
                    if (cnt <= 2 && best >= 0 && (h <= eps * 8f || cnt <= 1)) { _h[best] += h; _h[i] = 0f; }
                    else if (cnt == 0 && h <= eps) _h[i] = 0f;   // пылинка
                }
        }

        /// <summary>Тесто уехало к краю сетки — сдвинуть сетку (и объект) целыми клетками, чтобы пласт снова был в середине.</summary>
        private void Recenter()
        {
            if (_n < 40) return;                                   // кусок — своя маленькая сетка, двигать незачем
            int n = _n, x0 = n, y0 = n, x1 = -1, y1 = -1;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (_h[y * n + x] > 0.004f) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
            if (x1 < 0) return;
            int m = 6;
            bool near = x0 < m || y0 < m || x1 > n - 1 - m || y1 > n - 1 - m;
            if (!near) return;
            if (x1 - x0 + 1 > n - 2 * m || y1 - y0 + 1 > n - 2 * m)
            {
                if (_log && !_edgeWarned) { _edgeWarned = true; Debug.LogWarning($"[Тесто] «{name}»: пласт ({(x1 - x0 + 1) * Cell:0.0}×{(y1 - y0 + 1) * Cell:0.0} м) больше сетки — у края копится. Увеличь N у Dough Sheet (сейчас {_n})", this); }
                return;
            }
            int sx = (n - 1 - x1 - x0) / 2, sy = (n - 1 - y1 - y0) / 2;   // сдвиг, чтобы центр рамки стал центром сетки
            if (sx == 0 && sy == 0) return;
            float[] nh = new float[_h.Length];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int xx = x + sx, yy = y + sy;
                    if (xx >= 0 && yy >= 0 && xx < n && yy < n) nh[yy * n + xx] = _h[y * n + x];
                }
            _h = nh;
            if (_dirt != null && _dirt.Length == nh.Length)
            {
                float[] nd = new float[nh.Length];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int xx = x + sx, yy = y + sy;
                        if (xx >= 0 && yy >= 0 && xx < n && yy < n) nd[yy * n + xx] = _dirt[y * n + x];
                    }
                _dirt = nd;
            }
            transform.position -= new Vector3(sx * Cell, 0f, sy * Cell);
            if (_log) Debug.Log($"[Тесто] «{name}»: пласт уехал к краю — сетка сдвинута на {sx},{sy} клеток", this);
        }

        private bool Idx(Vector2 p, out int i, out int x, out int y)
        {
            x = Mathf.FloorToInt((p.x + Half) / Cell); y = Mathf.FloorToInt((p.y + Half) / Cell);
            i = y * _n + x;
            return x >= 0 && y >= 0 && x < _n && y < _n;
        }

        private int IdxOr(Vector2 p, int fallback) => Idx(p, out int i, out _, out _) ? i : fallback;

        /// <summary>Объём, которому некуда (край сетки) — в ближайшую клетку внутри, чтобы не пропал.</summary>
        private void DropNear(Vector2 p, float v)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((p.x + Half) / Cell), 0, _n - 1), y = Mathf.Clamp(Mathf.FloorToInt((p.y + Half) / Cell), 0, _n - 1);
            _h[y * _n + x] += v;
        }

        // ───────────────────────── нарезка шпателем ─────────────────────────
        // Рез — по ЛИНИИ СЕТКИ (между рядами клеток), вдоль X или Z: целые числа, без «координатных конфликтов».
        // Удар рядом с уже начатой линией (±1 клетка) продолжает её — ровные длинные резы, без полосок в одну клетку.
        // Отрезанный со всех сторон остров уходит в отдельный КУСОК — это такое же тесто (DoughSheet) со своей маленькой
        // сеткой, клетки копируются один в один: на вид ничего не меняется, просто его теперь можно резать/двигать отдельно.
        // Кусок режется точно так же — большой → полосы → ньокки, в любом порядке.

        private bool[] _cutX, _cutZ;     // _cutX[y*n+x]: рез между (x,y) и (x,y+1); _cutZ[y*n+x]: между (x,y) и (x+1,y)
        private bool[] _softX, _softZ;   // «мягкие» резы: ньокки внутри полосы — видно щель, но объект один (не плодим сотни кусков)
        private bool _hasCuts, _hasSoft, _warnNoPrefab;
        private int _lastLine = -1; private bool _lastAlongX; private float _lastCutAt = -99f;
        private int[] _lab;
        private readonly Queue<int> _bfs = new Queue<int>();
        private readonly List<int> _reg = new List<int>();

        private void EnsureCuts()
        {
            if (_cutX == null || _cutX.Length != _h.Length) { _cutX = new bool[_h.Length]; _cutZ = new bool[_h.Length]; _hasCuts = false; }
            if (_softX == null || _softX.Length != _h.Length) { _softX = new bool[_h.Length]; _softZ = new bool[_h.Length]; _hasSoft = false; }
        }

        /// <summary>Точка над моей клеткой с тестом — 1; рядом (в паре клеток) — чуть дальше; иначе — не я.</summary>
        public float SpatulaDistance(Vector3 point)
        {
            if (!_grounded || _h == null) return float.MaxValue;
            Vector3 l = point - transform.position;
            if (l.y > _maxH * S + 0.3f || l.y < -0.3f) return float.MaxValue;
            int cx = Mathf.FloorToInt((l.x + Half) / Cell), cy = Mathf.FloorToInt((l.z + Half) / Cell);
            float best = float.MaxValue;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= _n || y >= _n || _h[y * _n + x] <= 0.004f) continue;
                    float d = (CellPos(x, y) - new Vector2(l.x, l.z)).magnitude;
                    if (d < best) best = d;
                }
            return best == float.MaxValue ? float.MaxValue : 1f + best;   // хуже, чем «попал» в залипший инструмент рядом
        }

        /// <summary>Толщина пласта без тонкой кромки (по клеткам толще половины минимума катка).</summary>
        private float CoreThickness(out int cells)
        {
            float thr = Mathf.Max(0.012f * S, _rollMin * S * 0.5f), s = 0f; cells = 0;
            foreach (float h in _h) if (h > thr) { s += h; cells++; }
            return cells > 0 ? s / cells : 0f;
        }

        /// <summary>Размер теста по клеткам (длина ≥ ширина), м.</summary>
        public void PieceSize(out float length, out float width)
        {
            int x0 = _n, y0 = _n, x1 = -1, y1 = -1;
            for (int y = 0; y < _n; y++) for (int x = 0; x < _n; x++) if (_h[y * _n + x] > 0.004f) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
            if (x1 < 0) { length = width = 0f; return; }
            float a = (x1 - x0 + 1) * Cell, b = (y1 - y0 + 1) * Cell;
            length = Mathf.Max(a, b); width = Mathf.Min(a, b);
        }

        /// <summary>Сколько клеток ньокк (длина ломтика в полосе и ширина полосы при нарезке пласта).</summary>
        private int GnocchiCells => Mathf.Max(2, Mathf.RoundToInt((_pieceMin + _pieceMax) * 0.5f * S / Cell));

        /// <summary>Рамка теста по клеткам.</summary>
        private bool Bounds(out int x0, out int x1, out int y0, out int y1)
        {
            x0 = _n; y0 = _n; x1 = -1; y1 = -1;
            for (int y = 0; y < _n; y++) for (int x = 0; x < _n; x++) if (_h[y * _n + x] > 0.004f) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
            return x1 >= 0;
        }

        /// <summary>Это полоса (колбаска): узкая и вытянутая. longX — длинная сторона вдоль X.</summary>
        private bool IsStrip(out bool longX, out int x0, out int x1, out int y0, out int y1)
        {
            longX = true;
            if (!Bounds(out x0, out x1, out y0, out y1)) return false;
            int ex = x1 - x0 + 1, ez = y1 - y0 + 1;
            longX = ex >= ez;
            int lo = Mathf.Min(ex, ez), hi = Mathf.Max(ex, ez);
            return lo * Cell <= 0.45f * S && hi >= lo * 1.6f && hi >= GnocchiCells * 2;
        }

        public bool ServerSpatulaHit(Vector3 point, Vector3 dir, out string why)
        {
            why = null;
            if (!base.IsServerInitialized) return false;
            float mean = CoreThickness(out int cnt);
            if (cnt < 1) { why = "теста тут почти нет"; return false; }
            if (mean > _targetMax * S * 1.05f)
            {
                Hint($"сначала раскатай: толщина {mean * 100f:0} см, режется с {_targetMax * S * 100f:0} см (каток тоньше {_rollMin * S * 100f:0} см не раскатает)");
                RpcPuff(point, 0.3f);
                return true;                                                     // удар «съели» — дальше не ищем
            }
            EnsureCuts();
            Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up);
            Vector3 blade = flat.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
            bool alongX = Mathf.Abs(blade.x) >= Mathf.Abs(blade.z);            // лезвие вдоль X — рез между рядами (по Z)
            Vector3 l = point - transform.position;
            float c = Cell;
            int n = _n;
            float along = alongX ? l.x : l.z, across = alongX ? l.z : l.x;
            int rawLine = Mathf.Clamp(Mathf.RoundToInt((across + Half) / c), 1, n - 1);

            // ── ПОЛОСА, режем поперёк: ньокки ОДНОГО размера, отсчёт от конца полосы; объект остаётся один ──
            if (IsStrip(out bool longX, out int bx0, out int bx1, out int by0, out int by1) && alongX != longX)
            {
                int g = GnocchiCells;
                int s0 = longX ? bx0 : by0, s1 = longX ? bx1 : by1;               // клетки полосы вдоль длинной стороны
                int k = Mathf.RoundToInt((rawLine - s0) / (float)g);
                int line = s0 + k * g;
                if (line < s0 + 2 || line > s1 - 1)
                {
                    Hint($"у края полосы — ньокк тут короче нормы ({g * c * 100f:0} см); режь дальше от края");
                    RpcPuff(point, 0.2f); return true;
                }
                int p0 = longX ? by0 : bx0, p1 = longX ? by1 : bx1;               // поперёк полосы — вся ширина
                int set = 0;
                for (int a = p0; a <= p1; a++)
                {
                    int i0 = alongX ? (line - 1) * n + a : a * n + line - 1;
                    if (!HasAt(alongX, line, a)) continue;
                    bool[] arr = alongX ? _softX : _softZ;
                    if (arr[i0]) continue;
                    arr[i0] = true; set++;
                }
                if (set == 0) { Hint("тут уже разрезано"); return true; }
                _hasSoft = true;
                _dirtyNet = true; _dirtyMesh = true;
                RpcPuff(point, 0.35f);
                if (_log) Debug.Log($"[Ньокки] «{name}»: ломтик полосы на {line - s0} кл. от края — в полосе ньокков {GnocchiCount()} по {g * c * 100f:0} см", this);
                return true;
            }

            // ── ПЛАСТ (или полосу вдоль): настоящий рез, отрезанное уходит отдельным куском ──
            float half = _bladeLen * S * 0.5f;
            int a0 = Mathf.Clamp(Mathf.FloorToInt((along - half + Half) / c), 0, n - 1), a1 = Mathf.Clamp(Mathf.FloorToInt((along + half + Half) / c), 0, n - 1);
            int ln;
            // ведёшь ту же линию дальше (на этом участке она ещё не прорезана) — держим её, даже если прицел чуть уехал
            bool keep = alongX == _lastAlongX && Time.time - _lastCutAt < 20f && _lastLine > 0 && _lastLine < n && Mathf.Abs(rawLine - _lastLine) <= 2;
            if (keep)
            {
                int fresh = 0, total = 0;
                for (int a = a0; a <= a1; a++)
                {
                    if (!HasAt(alongX, _lastLine, a)) continue;
                    total++;
                    if (!(alongX ? _cutX[(_lastLine - 1) * n + a] : _cutZ[a * n + _lastLine - 1])) fresh++;
                }
                keep = total > 0 && fresh * 3 >= total;                            // здесь уже прорезано — значит это НОВАЯ параллельная линия
            }
            if (keep) ln = _lastLine;                                              // ведёшь ту же линию — не «соскакивает»
            else ln = EvenStrip(alongX, SnapLine(alongX, rawLine, a0, a1), a0, a1);
            ln = FixThin(alongX, ln, a0, a1);
            if (ln < 0)
            {
                Hint("тут отрежется полоска тоньше двух клеток — режь чуть дальше или в другую сторону");
                RpcPuff(point, 0.2f);
                return true;
            }
            // край пласта близко (до полудлины лезвия) — дорезаем до края, чтобы кусок отделился
            int ext = Mathf.Max(3, (a1 - a0) / 2), lo = a0, hi = a1;
            { int k = 0; while (k < ext && lo - 1 >= 0 && HasAt(alongX, ln, lo - 1)) { lo--; k++; } if (k >= ext) lo = a0; }
            { int k = 0; while (k < ext && hi + 1 <= n - 1 && HasAt(alongX, ln, hi + 1)) { hi++; k++; } if (k >= ext) hi = a1; }
            int cutN = 0;
            for (int a = lo; a <= hi; a++)
            {
                int i0 = alongX ? (ln - 1) * n + a : a * n + ln - 1;
                if (!HasAt(alongX, ln, a)) continue;
                if (alongX) { if (_cutX[i0]) continue; _cutX[i0] = true; } else { if (_cutZ[i0]) continue; _cutZ[i0] = true; }
                cutN++;
            }
            _hasCuts |= cutN > 0;
            _lastLine = ln; _lastAlongX = alongX; _lastCutAt = Time.time;
            int made = Split(out string what);
            _dirtyNet = true; _dirtyMesh = true; _dirtCheck = true;                // без оплывания — края реза остаются чёткими
            RpcPuff(point, 0.35f);
            if (_log) Debug.Log($"[Ньокки] «{name}»: рез {(alongX ? "вдоль X" : "вдоль Z")} по линии {ln}{(keep ? " (та же линия)" : "")} (+{cutN} кл.)" + (made > 0 ? $" — отрезано: {made} ({what})" : " — ещё держится (рез не до конца)"), this);
            return true;
        }

        /// <summary>Новая параллельная линия — на ровном шаге от ближайшей уже сделанной (полосы одной ширины = ширина ньокка).</summary>
        private int EvenStrip(bool alongX, int line, int a0, int a1)
        {
            int n = _n, g = GnocchiCells, ext = Mathf.Max(2, (a1 - a0) / 2);
            bool[] cut = alongX ? _cutX : _cutZ;
            for (int d = 1; d <= g * 4; d++)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    int L = line + d * sgn;
                    if (L < 1 || L > n - 1) continue;
                    bool any = false;
                    for (int a = Mathf.Max(0, a0 - ext); a <= Mathf.Min(n - 1, a1 + ext) && !any; a++) any = cut[alongX ? (L - 1) * n + a : a * n + L - 1];
                    if (!any) continue;
                    int steps = Mathf.Max(1, Mathf.FloorToInt(d / (float)g + 0.25f));   // чуть дальше шага — всё равно одна полоса
                    int res = L - sgn * steps * g;                                 // от найденной линии в сторону удара
                    return Mathf.Clamp(res, 1, n - 1);
                }
            return line;
        }

        // ───────────────────────── лопатой: ньокки из полосы ─────────────────────────

        /// <summary>Есть что черпать лопатой: нарезанная полоса или кусок размером с ньокк. Клиент и сервер.</summary>
        public bool PeekScoop(out ushort product, out FoodState state)
        {
            product = GnocchiProduct.Id; state = GnocchiProduct.Raw;
            if (_kind == DoughKind.Pelmeni)                                      // пельменное тесто: лопатой — только готовые пельмени
            {
                product = PelmeniProduct.Id; state = PelmeniProduct.Raw;
                if (product == 0) return false;
                foreach (DoughCircle c in _circles) if (c.State == CircleShaped) return true;
                return false;
            }
            if (product == 0 || _h == null) return false;
            if (CoreThickness(out int cells) > _targetMax * S * 1.05f || cells == 0) return false;
            if (IsStrip(out _, out _, out _, out _, out _) && _cutX != null && (_hasCuts || _hasSoft)) return true;
            if (Volume <= GnocchiVolume * 4.5f) return true;                       // обрезок/кусочек — целиком (до 4 ньокков)
            PieceSize(out _, out float w);
            return w <= 0.45f * S;                                                 // неразрезанная полоса — лопата сама «отрежет» с конца
        }

        /// <summary>Объём одного ньокка (ломтик полосы: клетки × клетки × толщина), м³.</summary>
        private float GnocchiVolume
        {
            get { float g = GnocchiCells * Cell; return Mathf.Max(1e-5f, g * g * Mathf.Max(_rollMin * S, CoreThickness(out _))); }
        }

        /// <summary>Лопата берёт ньокки с конца полосы (по мягким резам) или весь кусок-ньокк. Пусто — кусок исчезает.</summary>
        public int ServerScoop(int max, out ushort product, out FoodState state, out byte cook, out byte clean)
        {
            if (_kind == DoughKind.Pelmeni) return ServerScoopPelmeni(max, out product, out state, out cook, out clean);
            product = GnocchiProduct.Id; state = GnocchiProduct.Raw; cook = 0;
            clean = (byte)Mathf.RoundToInt(Quality01 * 100f);
            max = Mathf.Min(max, 4);                                              // крупные — на лопату влезает 4
            if (product == 0 || max <= 0) return 0;
            int took = 0, n = _n;
            if (IsStrip(out bool longX, out int x0, out int x1, out int y0, out int y1) && _softX != null && (_hasCuts || _hasSoft))
            {
                int s0 = longX ? x0 : y0, s1 = longX ? x1 : y1, p0 = longX ? y0 : x0, p1 = longX ? y1 : x1;
                bool[] arr = longX ? _softZ : _softX;
                bool[] hard = longX ? _cutZ : _cutX;
                // границы ломтиков вдоль полосы
                var lines = new List<int>();
                for (int L = s0 + 1; L <= s1; L++)
                {
                    bool any = false;
                    for (int a = p0; a <= p1 && !any; a++) { int i = longX ? a * n + L - 1 : (L - 1) * n + a; any = arr[i] || hard[i]; }
                    if (any) lines.Add(L);
                }
                if (lines.Count == 0) return 0;
                int end = s1 + 1;
                for (int k = lines.Count - 1; k >= 0 && took < max; k--)
                {
                    int from = lines[k];                                            // ломтик [from .. end-1] — с дальнего конца
                    for (int L = from; L < end; L++)
                        for (int a = p0; a <= p1; a++)
                        {
                            int i = longX ? a * n + L : L * n + a;
                            _h[i] = 0f; if (DirtOk) _dirt[i] = 0f;
                            _cutX[i] = _cutZ[i] = false; _softX[i] = _softZ[i] = false;
                        }
                    end = from; took++;
                    if (k == 0 && took < max) { ClearRange(longX, s0, end, p0, p1); took++; }   // последний ломтик — сам остаток
                }
            }
            else
            {
                // кусочек/обрезок — целиком: сколько ньокков по объёму (кривые — чуть хуже качество)
                float vol = Volume, gv = GnocchiVolume;
                PieceSize(out float pl, out float pw);
                if (vol > gv * 4.5f)
                {
                    // неразрезанная полоса: лопата берёт ломтики по длине ньокка с дальнего конца
                    if (pw > 0.45f * S || !Bounds(out int bx0, out int bx1, out int by0, out int by1)) return 0;
                    bool lx = (bx1 - bx0) >= (by1 - by0);
                    int g = GnocchiCells, e0 = lx ? bx0 : by0, e = (lx ? bx1 : by1) + 1, q0 = lx ? by0 : bx0, q1 = lx ? by1 : bx1;
                    while (took < max && e > e0)
                    {
                        int from = Mathf.Max(e0, e - g);
                        if (from - e0 < 2) from = e0;                                     // остаток короче 2 клеток — заберём с этим ломтиком
                        ClearRange(lx, from, e, q0, q1);
                        e = from; took++;
                    }
                    clean = (byte)Mathf.RoundToInt(clean * 0.92f);                         // не нарезано ровно — чуть хуже
                    _dirtyNet = true; _dirtyMesh = true;
                    if (_log) Debug.Log($"[Ньокки] «{name}»: лопатой с конца неразрезанной полосы — {took} ньокк(ов) (нарежь шпателем — будут ровнее)", this);
                    if (CountCells() == 0 && _startEmpty && base.IsSpawned) base.NetworkObject.Despawn();
                    return took;
                }
                took = Mathf.Clamp(Mathf.RoundToInt(vol / GnocchiVolume), 1, max);
                PieceSize(out float L, out float W);
                bool neat = L <= _pieceMax * S * 1.1f && W >= _pieceMin * S * 0.9f && took == 1;
                if (!neat) clean = (byte)Mathf.RoundToInt(clean * 0.85f);
                for (int i = 0; i < _h.Length; i++) { _h[i] = 0f; if (DirtOk) _dirt[i] = 0f; }
                if (_cutX != null) { System.Array.Clear(_cutX, 0, _cutX.Length); System.Array.Clear(_cutZ, 0, _cutZ.Length); }
                if (_softX != null) { System.Array.Clear(_softX, 0, _softX.Length); System.Array.Clear(_softZ, 0, _softZ.Length); }
                if (_log && !neat) Debug.Log($"[Ньокки] «{name}»: обрезок {L * 100f:0}×{W * 100f:0} см — лопатой как {took} ньокк(а), кривые — качество чуть ниже", this);
            }
            _dirtyNet = true; _dirtyMesh = true;
            if (_log) Debug.Log($"[Ньокки] «{name}»: лопатой взяли {took} ньокк(ов), осталось в куске {GnocchiCount()}", this);
            if (CountCells() == 0 && _startEmpty && base.IsSpawned) base.NetworkObject.Despawn();            // кусок кончился — убрать
            return took;
        }

        private void ClearRange(bool longX, int from, int to, int p0, int p1)
        {
            int n = _n;
            for (int L = from; L < to; L++)
                for (int a = p0; a <= p1; a++)
                {
                    int i = longX ? a * n + L : L * n + a;
                    _h[i] = 0f; if (DirtOk) _dirt[i] = 0f;
                    if (_cutX != null) { _cutX[i] = _cutZ[i] = false; }
                    if (_softX != null) { _softX[i] = _softZ[i] = false; }
                }
        }

        /// <summary>Сколько ньокков в полосе (по мягким резам + 1).</summary>
        public int GnocchiCount()
        {
            if (!_hasSoft || _softX == null || !IsStrip(out bool longX, out int x0, out int x1, out int y0, out int y1)) return 0;
            int lines = 0, n = _n;
            int s0 = longX ? x0 : y0, s1 = longX ? x1 : y1, p0 = longX ? y0 : x0, p1 = longX ? y1 : x1;
            bool[] arr = longX ? _softZ : _softX;                                  // поперёк длинной стороны
            for (int L = s0 + 1; L <= s1; L++)
            {
                bool any = false;
                for (int a = p0; a <= p1 && !any; a++) any = arr[longX ? a * n + L - 1 : (L - 1) * n + a];
                if (any) lines++;
            }
            return lines > 0 ? lines + 1 : 0;
        }

        /// <summary>Рядом (±1 линия) уже есть рез в эту сторону и на этом участке — продолжаем его, а не режем полоску в одну клетку.</summary>
        private int SnapLine(bool alongX, int line, int a0, int a1)
        {
            int n = _n, ext = Mathf.Max(2, (a1 - a0) / 2);
            int bestLine = line, bestHits = int.MinValue;
            for (int d = 0; d <= 2; d++)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    if (d == 0 && sgn > 0) continue;
                    int L = line + d * sgn;
                    if (L < 1 || L > n - 1) continue;
                    int hits = 0;
                    for (int a = Mathf.Max(0, a0 - ext); a <= Mathf.Min(n - 1, a1 + ext); a++)
                    {
                        int i0 = alongX ? (L - 1) * n + a : a * n + L - 1;
                        if (alongX ? _cutX[i0] : _cutZ[i0]) hits++;
                    }
                    int score = hits * 4 - d;                                          // больше совпадений и ближе
                    if (hits > 0 && score > bestHits) { bestHits = score; bestLine = L; }
                }
            return bestLine;
        }

        private bool HasAt(bool alongX, int line, int a)
        {
            int n = _n;
            int i0 = alongX ? (line - 1) * n + a : a * n + line - 1, i1 = alongX ? line * n + a : a * n + line;
            return _h[i0] > 0.004f || _h[i1] > 0.004f;
        }

        /// <summary>Сколько клеток теста подряд от линии в сторону side (−1 «до», +1 «после») до пустоты или другого реза (не больше 3).</summary>
        private int Depth(bool alongX, int line, int a, int side)
        {
            int n = _n, k = 0, p = side < 0 ? line - 1 : line;
            bool[] cut = alongX ? _cutX : _cutZ;
            while (p >= 0 && p < n && k < 3)
            {
                int i = alongX ? p * n + a : a * n + p;
                if (_h[i] <= 0.004f) break;
                k++;
                int q = side < 0 ? p - 1 : p;                                      // граница между q и q+1
                if (q < 0 || q + 1 >= n) break;
                if (cut[alongX ? q * n + a : a * n + q]) break;
                p += side;
            }
            return k;
        }

        /// <summary>Не даём отрезать полоску в одну клетку: по обе стороны линии должно быть ≥ 2 клеток (по медиане вдоль реза).
        /// Не так — пробуем соседнюю линию; совсем никак — −1.</summary>
        private int FixThin(bool alongX, int line, int a0, int a1)
        {
            int n = _n;
            var tb = new List<int>(); var ta = new List<int>();
            for (int d = 0; d < 3; d++)
            {
                int cand = d == 0 ? line : d == 1 ? line - 1 : line + 1;
                if (cand < 1 || cand > n - 1) continue;
                tb.Clear(); ta.Clear();
                for (int a = a0; a <= a1; a++)
                {
                    int i0 = alongX ? (cand - 1) * n + a : a * n + cand - 1, i1 = alongX ? cand * n + a : a * n + cand;
                    if (_h[i0] <= 0.004f || _h[i1] <= 0.004f) continue;
                    tb.Add(Depth(alongX, cand, a, -1)); ta.Add(Depth(alongX, cand, a, 1));
                }
                if (tb.Count == 0) continue;
                tb.Sort(); ta.Sort();
                if (tb[tb.Count / 2] >= 2 && ta[ta.Count / 2] >= 2) return cand;
            }
            return -1;
        }

        private bool BlockedStep(int i, int k)
        {
            if (!_hasCuts) return false;
            int n = _n;
            switch (k)
            {
                case 0: return _cutZ[i];            // x+1
                case 1: return _cutZ[i - 1];        // x-1
                case 2: return _cutX[i];            // y+1
                default: return _cutX[i - n];       // y-1
            }
        }

        /// <summary>Острова теста с учётом резов: самый большой остаётся здесь, остальные — отдельными кусками (мелочь — в обрезки).</summary>
        [Server]
        private int Split(out string what)
        {
            what = "";
            int n = _n; float eps = 0.004f;
            if (_lab == null || _lab.Length != _h.Length) _lab = new int[_h.Length];
            for (int i = 0; i < _lab.Length; i++) _lab[i] = -1;
            var regs = new List<int[]>(); var vols = new List<float>();
            for (int i0 = 0; i0 < _h.Length; i0++)
            {
                if (_h[i0] <= eps || _lab[i0] >= 0) continue;
                int id = regs.Count; float vol = 0f;
                _reg.Clear(); _bfs.Clear(); _bfs.Enqueue(i0); _lab[i0] = id;
                while (_bfs.Count > 0)
                {
                    int i = _bfs.Dequeue(); _reg.Add(i); vol += _h[i];
                    int x = i % n, y = i / n;
                    for (int k = 0; k < 4; k++)
                    {
                        int xx = x + DX[k], yy = y + DY[k];
                        if (xx < 0 || yy < 0 || xx >= n || yy >= n) continue;
                        int j = yy * n + xx;
                        if (_h[j] <= eps || _lab[j] >= 0 || BlockedStep(i, k)) continue;
                        _lab[j] = id; _bfs.Enqueue(j);
                    }
                }
                regs.Add(_reg.ToArray()); vols.Add(vol);
            }
            if (regs.Count <= 1) return 0;
            int main = 0; for (int r = 1; r < regs.Count; r++) if (vols[r] > vols[main]) main = r;
            int made = 0, scrap = 0;
            var sb = new System.Text.StringBuilder();
            for (int r = 0; r < regs.Count; r++)
            {
                if (r == main) continue;
                int[] cells = regs[r];
                if (cells.Length < 3) { foreach (int i in cells) _h[i] = 0f; scrap += cells.Length; continue; }   // мелкая обрезка
                if (!SpawnPiece(cells, out string d)) continue;
                made++;
                if (sb.Length < 160) sb.Append(sb.Length > 0 ? ", " : "").Append(d);
            }
            if (scrap > 0 && _log) Debug.Log($"[Ньокки] «{name}»: обрезки в мусор — {scrap} кл.", this);
            what = sb.ToString();
            return made;
        }

        /// <summary>Остров → новый кусок-тесто: своя сетка ровно под остров (+2 клетки поля), клетки — один в один, на том же месте.</summary>
        [Server]
        private bool SpawnPiece(int[] cells, out string desc)
        {
            desc = null;
            if (_piecePrefab == null)
            {
                if (!_warnNoPrefab && _log) { _warnNoPrefab = true; Debug.LogError($"[Ньокки] «{name}»: кусок отрезан, но не задан Piece Prefab — меню 6 (заготовка) → сохрани префабом → Spawnable Prefabs → меню 7", this); }
                return false;
            }
            int n = _n, x0 = n, y0 = n, x1 = -1, y1 = -1;
            foreach (int i in cells) { int x = i % n, y = i / n; x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
            const int pad = 2;
            int w = x1 - x0 + 1, hgt = y1 - y0 + 1;
            int pn = Mathf.Max(8, Mathf.Max(w, hgt) + pad * 2);
            int ox = (pn - w) / 2, oy = (pn - hgt) / 2;                         // куда в новой сетке ляжет (x0, y0)
            var ph = new float[pn * pn]; var pd = new float[pn * pn]; var pcx = new bool[pn * pn]; var pcz = new bool[pn * pn];
            var psx = new bool[pn * pn]; var psz = new bool[pn * pn];
            foreach (int i in cells)
            {
                int x = i % n, y = i / n, j = (y - y0 + oy) * pn + (x - x0 + ox);
                ph[j] = _h[i];
                if (DirtOk) pd[j] = _dirt[i];
                // резы внутри куска (не до конца) — переносим
                if (_cutX[i] && y + 1 < n && _lab[i + n] == _lab[i]) pcx[j] = true;
                if (_cutZ[i] && x + 1 < n && _lab[i + 1] == _lab[i]) pcz[j] = true;
                if (_softX != null && _softX[i] && y + 1 < n && _lab[i + n] == _lab[i]) psx[j] = true;
                if (_softZ != null && _softZ[i] && x + 1 < n && _lab[i + 1] == _lab[i]) psz[j] = true;
            }
            float c = Cell;
            float pHalf = pn * c * 0.5f;
            Vector3 pos = transform.position + new Vector3((x0 - ox) * c - Half + pHalf, 0f, (y0 - oy) * c - Half + pHalf);
            GameObject go = Instantiate(_piecePrefab, pos, Quaternion.identity);
            DoughSheet ps = go.GetComponent<DoughSheet>();
            if (ps == null) { Destroy(go); Debug.LogError($"[Ньокки] «{name}»: на Piece Prefab нет Dough Sheet — заготовку делай меню 6", this); return false; }
            foreach (int i in cells) { _h[i] = 0f; if (DirtOk) _dirt[i] = 0f; _cutX[i] = false; _cutZ[i] = false; if (_softX != null) { _softX[i] = false; _softZ[i] = false; } }
            ps.ServerPresetPiece(this, pn, ph, pd, pcx, pcz, psx, psz);
            base.Spawn(go);
            ps.PieceSize(out float L, out float W);
            desc = W >= 0.45f * S ? $"пласт {L:0.00}×{W:0.00} м" : L > _pieceMax * S * 1.05f ? $"полоса {L:0.00} м × {W * 100f:0} см" : $"ньокк {L * 100f:0}×{W * 100f:0} см";
            return true;
        }

        /// <summary>До Spawn: кусок от родителя — те же настройки теста, своя сетка и клетки.</summary>
        // БЕЗ [Server]: зовётся ДО Spawn — у ещё не заспавненного объекта FishNet такой метод молча пропускает (кусок оставался пустым)
        public void ServerPresetPiece(DoughSheet parent, int n, float[] h, float[] dirt, bool[] cutX, bool[] cutZ, bool[] softX, bool[] softZ)
        {
            _kind = parent._kind; if (_kind != DoughKind.Custom) ApplyPreset(_kind);
            _doughColor = parent._doughColor; _lumpy = parent._lumpy; _lumpHeight = parent._lumpHeight;
            _cell = parent._cell; _cw = -1f; _maxH = parent._maxH;
            _rollMin = parent._rollMin; _targetMin = parent._targetMin; _targetMax = parent._targetMax; _rideMax = parent._rideMax;
            _pieceMin = parent._pieceMin; _pieceMax = parent._pieceMax; _bladeLen = parent._bladeLen;
            _piecePrefab = parent._piecePrefab; _log = parent._log;
            _n = n; _h = h; _dirt = dirt; _cutX = cutX; _cutZ = cutZ;
            _hasCuts = false; foreach (bool b in cutX) _hasCuts |= b; foreach (bool b in cutZ) _hasCuts |= b;
            _softX = softX; _softZ = softZ; _hasSoft = false; foreach (bool b in softX) _hasSoft |= b; foreach (bool b in softZ) _hasSoft |= b;
            _qBase = parent.QualityNoDirt; _stuck = 0; _stuckVol = 0f;
            _vol0 = Volume;
            _grounded = parent._grounded;
            _needRelax = false; _dirtyNet = true; _dirtyMesh = true; _dirtCheck = true;
            name = parent._startEmpty ? parent.name : "DoughPiece";
            if (_log) Debug.Log($"[Ньокки] кусок «{name}»: сетка {n}×{n}, клеток с тестом {CountCells()}, объём {Volume * 1000f:0} л", this);
        }

        // ───────────────────────── ком из тары (замес) ─────────────────────────

        private float _ballK = 1f;          // во сколько раз ком больше пресетного (по объёму)

        /// <summary>Объём шара пресета (радиус R при масштабе мира), м³: приплюснутая шапка H·π·Rb²/1.42 (как в MakeBall).</summary>
        private float BallVolume(float radius)
        {
            float R = radius * S, H = 2f * R * (1f - _sag), Rb = R * (1f + _sag * 0.6f);
            return H * Mathf.PI * Rb * Rb / 1.42f;
        }

        /// <summary>Объём одной порции замеса (ком пресета = Portions Per Ball порций), м³.</summary>
        private float PortionVolume => BallVolume(_ballRadius / Mathf.Pow(Mathf.Max(0.05f, _ballK), 1f / 3f)) / Mathf.Max(1, DoughSheetDump.PortionsPerBall);

        /// <summary>Шар теста (portions порций) упал на пласт — прилипает бугром, объём растёт. Сервер.</summary>
        public bool ServerAddPortions(Vector3 world, int portions)
        {
            if (!base.IsServerInitialized || portions <= 0) return false;
            float v = PortionVolume * portions;
            if (!ServerDropLump(world, v)) return false;
            _vol0 = (_vol0 > 0f ? _vol0 : Volume - v) + v;
            return true;
        }

        /// <summary>Радиус лежащего кома по полу, м (куда ставить, чтобы не в стенку тары).</summary>
        public float FootRadius => _ballRadius * S * (1f + _sag * 0.6f) * 1.1f;

        /// <summary>До Spawn: ком из тары (бетономешалка, ведро) — пресет теста, шар по числу порций.
        /// portionsPerBall порций = ком как у меню 4/4б. selfPrefab — этот же префаб: им потом отделяются куски.</summary>
        // БЕЗ [Server]: зовётся ДО Spawn — у ещё не заспавненного объекта FishNet такой метод молча пропускает
        public void ServerPresetBall(DoughKind kind, int portions, int portionsPerBall, GameObject selfPrefab)
        {
            _kind = kind;
            if (kind != DoughKind.Custom) ApplyPreset(kind);
            _kindApplied = kind; _presetVer = PresetVersion;
            _n = Mathf.Max(_n, 96); _cw = -1f;
            float k = Mathf.Clamp(portions / (float)Mathf.Max(1, portionsPerBall), 0.1f, 2.2f);
            float target = BallVolume(_ballRadius) * k;
            _ballK = k;
            _ballRadius *= Mathf.Pow(k, 1f / 3f);
            _h = new float[_n * _n]; _dirt = null;
            _cutX = _cutZ = _softX = _softZ = null; _hasCuts = _hasSoft = false;
            MakeBall();
            float v = Volume;
            if (v > 1e-6f) { float s = target / v; for (int i = 0; i < _h.Length; i++) _h[i] *= s; }
            _vol0 = Volume; _qBase = 1f; _stuck = 0; _stuckVol = 0f;
            _grounded = false; _vy = 0f;
            if (_piecePrefab == null) _piecePrefab = selfPrefab;
            _needRelax = false; _dirtyNet = true; _dirtyMesh = true; _dirtCheck = true;
            name = kind == DoughKind.Pelmeni ? "PelmeniDough" : kind == DoughKind.Gnocchi ? "GnocchiDough" : "Dough";
            if (_log) Debug.Log($"[Тесто] ком из тары «{name}» ({KindTitle(kind)}): {portions} порц. → шар {FootRadius * 2f:0.0} м, объём {_vol0:0.00} м³ (×{k:0.00} от обычного кома)", this);
        }

        private int CountCells() { int k = 0; foreach (float v in _h) if (v > 0.004f) k++; return k; }

        /// <summary>Каток/кувалда снова слепили тесто — резы в этом месте «заживают».</summary>
        private void HealCuts(Vector3 world, float r)
        {
            if ((!_hasCuts && !_hasSoft) || _cutX == null) return;
            Vector3 l = world - transform.position;
            int cx = Mathf.FloorToInt((l.x + Half) / Cell), cy = Mathf.FloorToInt((l.z + Half) / Cell), rr = Mathf.CeilToInt(r / Cell);
            for (int y = Mathf.Max(0, cy - rr); y <= Mathf.Min(_n - 1, cy + rr); y++)
                for (int x = Mathf.Max(0, cx - rr); x <= Mathf.Min(_n - 1, cx + rr); x++) { int i = y * _n + x; _cutX[i] = false; _cutZ[i] = false; if (_softX != null) { _softX[i] = false; _softZ[i] = false; } }
        }

        // ───────────────────────── ПЕЛЬМЕНИ: кружки ведром, начинка, плоскогубцы ─────────────────────────
        // Кружки и пельмени — ДАННЫЕ внутри пласта (без сотен сетевых объектов). Центр — в мировых X/Z
        // (сдвиг сетки Recenter их не трогает). Вид — один меш-накладка «Circles (auto)» у всех игроков.
        //   1. Вырубка: пустое ведро ЛКМ — прижал к тесту, ПРОВЕРНУЛ (мышью) — кружок (BucketDoughCutter).
        //   2. Начинка: мастерок ПКМ «Фарш для пельменей» — в ближайший кружок, 1 порция за бросок.
        //   3. Плоскогубцы (PliersItem): ЛКМ держать по кружку — сложился пополам; не отпуская — веди по шву (защип);
        //      отпустил — шов зажат. ПКМ по защипнутому — концы загнулись «ушком», готово.
        //   Каток по кружку — кружок пропал; по пельменю — лепёшка (брак).

        public struct DoughCircle { public float X, Z, R; public byte State, Fill, Acc, Q, Dir; }
        public const byte CircleCut = 0, CircleShaped = 1, CircleFlat = 2, CircleFolded = 3, CircleSealed = 4;
        private readonly List<DoughCircle> _circles = new List<DoughCircle>();
        private bool _circlesDirty;
        private float _circSendAt;
        private static Vector3 _hudPoint;
        private GameObject _circGo;
        private Mesh _circMesh;
        private MeshCollider _circCol;
        private readonly List<Vector3> _cv = new List<Vector3>();
        private readonly List<Color> _cc = new List<Color>();
        private readonly List<Vector2> _cuv = new List<Vector2>();
        private readonly List<int> _ct = new List<int>();

        // локальная анимация (у того, кто держит плоскогубцы): складывание / защип / ушко — плавно, до ответа сервера
        public const byte AnimNone = 0, AnimFold = 1, AnimSeal = 2, AnimCurl = 3;
        private byte _animKind;
        private float _animX, _animZ, _animT, _animYaw;
        private int _animMask;
        private bool _animActive;

        /// <summary>Кружок «в самый раз», м (пельменный кружок при масштабе мира).</summary>
        public float IdealCircleR => 0.4f * S;
        public int CircleCount => _circles.Count;
        public DoughCircle GetCircle(int i) => _circles[i];
        public const int SealBins = 12;

        /// <summary>Кружок под точкой (клиент и сервер, по своим данным). -1 — нет.</summary>
        public int CircleAt(Vector3 world, float slack = 1.05f)
        {
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < _circles.Count; i++)
            {
                DoughCircle o = _circles[i];
                float d = new Vector2(o.X - world.x, o.Z - world.z).magnitude;
                if (d <= o.R * slack && d / o.R < bd) { bd = d / o.R; best = i; }
            }
            return best;
        }

        private int CircleNear(float x, float z)
        {
            int best = -1; float bd = 0.25f * S;
            for (int i = 0; i < _circles.Count; i++)
            {
                float d = new Vector2(_circles[i].X - x, _circles[i].Z - z).magnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        public static bool IsFilling(ProductDef p) => p != null && !string.IsNullOrEmpty(p.Title) && p.Title.Trim().ToLowerInvariant() == "фарш для пельменей";

        /// <summary>Можно ли тут вырубить кружок радиусом r (клиент — по своим данным, для призрака; сервер — по правде).</summary>
        public bool PreviewStamp(Vector3 world, float r, out string why)
        {
            if (_kind != DoughKind.Pelmeni) { why = "вырубка ведром — для пельменного теста"; return false; }
            return CheckStamp(world, r, out _, out _, out _, out why);
        }

        private bool CheckStamp(Vector3 world, float r, out float mean, out float even, out float dirt, out string why)
        {
            mean = 0f; even = 0f; dirt = 0f; why = null;
            if (_h == null || r <= 0.01f) { why = "нет теста"; return false; }
            Vector3 l = world - transform.position;
            Vector2 c0 = new Vector2(l.x, l.z);
            float c = Cell, thr = 0.012f * S;
            int x0 = Mathf.FloorToInt((l.x - r + Half) / c), x1 = Mathf.FloorToInt((l.x + r + Half) / c);
            int y0 = Mathf.FloorToInt((l.z - r + Half) / c), y1 = Mathf.FloorToInt((l.z + r + Half) / c);
            int inside = 0, on = 0; float s = 0f, s2 = 0f, dsum = 0f;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if ((CellPos(x, y) - c0).sqrMagnitude > r * r) continue;
                    inside++;
                    if (x < 0 || y < 0 || x >= _n || y >= _n) continue;
                    int i = y * _n + x; float h = _h[i];
                    if (h <= thr) continue;
                    on++; s += h; s2 += h * h;
                    if (DirtOk && _dirt[i] > 0.5f) dsum++;
                }
            if (inside == 0) { why = "мимо теста"; return false; }
            float cover = on / (float)inside;
            if (on > 0)
            {
                mean = s / on;
                float sd = Mathf.Sqrt(Mathf.Max(0f, s2 / on - mean * mean));
                even = Mathf.Clamp01(1f - sd / Mathf.Max(1e-4f, mean) * 2f);
                dirt = dsum / on;
            }
            if (cover < 0.9f) { why = cover < 0.3f ? "мимо теста" : $"край пласта или дырка — кружок неполный ({cover * 100f:0}%), руби дальше от края"; return false; }
            foreach (DoughCircle o in _circles)
            {
                float d = new Vector2(o.X - world.x, o.Z - world.z).magnitude;
                if (d < (r + o.R) * 0.97f) { why = o.State == CircleCut ? "наложится на другой кружок — руби рядом" : "тут пельмень — руби рядом"; return false; }
            }
            if (mean > _targetMax * S * 1.6f) { why = $"толсто ({mean * 100f:0} см) — раскатай катком до {_targetMin * S * 100f:0}–{_targetMax * S * 100f:0} см"; return false; }
            return true;
        }

        /// <summary>Ведро прижали и провернули: вырубить кружок радиусом r с центром в точке. Сервер.</summary>
        [Server]
        public bool ServerStampCircle(Vector3 world, float r, out string msg)
        {
            msg = null;
            if (!_grounded) { msg = "тесто ещё не легло"; return false; }
            if (_kind != DoughKind.Pelmeni) { msg = "вырубка ведром — для пельменного теста"; return false; }
            if (_circles.Count >= 160) { msg = "на пласте уже 160 кружков"; return false; }
            if (!CheckStamp(world, r, out float mean, out float even, out float dirt, out string why)) { msg = why; RpcPuff(world, 0.2f); return false; }
            float tMax = _targetMax * S;
            float qTh = mean <= tMax * 1.05f ? 1f : Mathf.Clamp01(1f - (mean - tMax * 1.05f) / (tMax * 0.6f));
            float q = Mathf.Clamp01(qTh * Mathf.Lerp(0.6f, 1f, even) * (1f - 0.85f * dirt) * QualityNoDirt);
            _circles.Add(new DoughCircle { X = world.x, Z = world.z, R = r, State = CircleCut, Q = (byte)Mathf.RoundToInt(q * 255f) });
            _circlesDirty = true; _dirtyMesh = true;
            RpcRing(new Vector3(world.x, transform.position.y + mean, world.z), r);
            float size = r / IdealCircleR;
            msg = $"кружок №{_circles.Count}: Ø {r * 200f:0} см{(size < 0.8f ? " (мелковат — ведро узкое)" : size > 1.25f ? " (крупноват — ведро широкое)" : "")}, толщина {mean * 100f:0.0} см{(qTh < 0.99f ? " — толстовато" : "")}, качество {q * 100f:0}%";
            if (_log) Debug.Log($"[Пельмени] «{name}»: вырубка — {msg}. Кружков {_circles.Count}", this);
            return true;
        }

        /// <summary>Мастерком начинку: в кружок под точкой (или ближайший рядом), 1 порция за бросок. Сервер.</summary>
        private int ServerFillCircle(int count, Vector3 point, out string why)
        {
            why = null;
            int i = CircleAt(point, 1.35f);
            if (i < 0) { why = "начинку — на кружок (сначала вырубить ведром)"; return 0; }
            DoughCircle o = _circles[i];
            if (o.State != CircleCut) { why = o.State == CircleFlat ? "это лепёшка — брак" : "пельмень уже сложен — начинку раньше"; return 0; }
            if (o.Fill >= 3) { why = "в кружке и так много начинки"; return 0; }
            o.Fill++;
            _circles[i] = o;
            _circlesDirty = true; _dirtyMesh = true;
            if (_log) Debug.Log($"[Пельмени] «{name}»: начинка в кружок — {o.Fill} порц.{(o.Fill == 1 ? " ✔ в самый раз" : " — многовато, полезет при лепке")}", this);
            return 1;
        }

        /// <summary>Плоскогубцы зажали кружок: сложить пополам (шов — к игроку, сгиб — от него). Сервер.</summary>
        [Server]
        public bool ServerFold(float x, float z, float yaw, out string msg)
        {
            msg = null;
            int i = CircleNear(x, z);
            if (i < 0) { msg = "кружка тут нет"; return false; }
            DoughCircle o = _circles[i];
            if (o.State == CircleFolded) return true;                                  // уже сложен — дальше защип
            if (o.State != CircleCut) { msg = o.State == CircleFlat ? "лепёшка — брак" : "уже защипнут"; return false; }
            o.State = CircleFolded; o.Acc = 0;
            o.Dir = (byte)Mathf.RoundToInt(Mathf.Repeat(yaw / (Mathf.PI * 2f), 1f) * 255f);
            float removed = ClearCircleCells(o);
            if (_vol0 > 0f) _vol0 = Mathf.Max(1e-4f, _vol0 - removed);
            _circles[i] = o;
            _circlesDirty = true; _dirtyNet = true; _dirtyMesh = true;
            msg = o.Fill == 0 ? "сложен — БЕЗ начинки (будет пустышка)" : o.Fill > 1 ? "сложен — начинки много, края расходятся" : "сложен — теперь веди плоскогубцами по шву";
            if (_log) Debug.Log($"[Пельмени] «{name}»: плоскогубцы — кружок {msg}", this);
            return true;
        }

        /// <summary>Отпустили ЛКМ: шов прошли на cover (0..1) с точностью quality. Хватает — защипнут. Сервер.</summary>
        [Server]
        public bool ServerSeal(float x, float z, float cover, float quality, out string msg)
        {
            msg = null;
            int i = CircleNear(x, z);
            if (i < 0) { msg = "кружка тут нет"; return false; }
            DoughCircle o = _circles[i];
            if (o.State != CircleFolded) return false;
            cover = Mathf.Clamp01(cover); quality = Mathf.Clamp01(quality);
            if (cover < 0.75f)
            {
                msg = $"шов зажат на {cover * 100f:0}% — мало, зажми ЛКМ ещё раз и проведи по всему краю";
                if (_log) Debug.Log($"[Пельмени] «{name}»: {msg}", this);
                return false;
            }
            float fill = o.Fill == 1 ? 1f : o.Fill == 0 ? 0.5f : 0.7f;                  // пустышка и «лезет» — хуже
            float q = Mathf.Clamp01(Mathf.Lerp(0.4f, 1f, quality) * Mathf.Lerp(0.6f, 1f, (cover - 0.75f) / 0.25f) * fill);
            o.State = CircleSealed; o.Acc = (byte)Mathf.RoundToInt(q * 255f);
            _circles[i] = o;
            _circlesDirty = true; _dirtyMesh = true;
            msg = $"шов зажат ✔ ({cover * 100f:0}%, ровность {quality * 100f:0}%) — пельмень {q * 100f:0}%: {GradeName(q)}. ПКМ — загнуть ушком";
            if (_log) Debug.Log($"[Пельмени] «{name}»: {msg}", this);
            return true;
        }

        /// <summary>ПКМ плоскогубцами по защипнутому: концы загнуть «ушком» — готовый пельмень. Сервер.</summary>
        [Server]
        public bool ServerCurl(float x, float z, out string msg)
        {
            msg = null;
            int i = CircleNear(x, z);
            if (i < 0) { msg = "кружка тут нет"; return false; }
            DoughCircle o = _circles[i];
            if (o.State == CircleCut) { msg = "сначала сложи: ЛКМ держать по кружку"; return false; }
            if (o.State == CircleFolded) { msg = "сначала зажми шов: ЛКМ держать и веди по краю"; return false; }
            if (o.State != CircleSealed) { msg = o.State == CircleShaped ? "уже готов" : "лепёшка — брак"; return false; }
            o.State = CircleShaped;
            _circles[i] = o;
            _circlesDirty = true; _dirtyMesh = true;
            RpcPuff(new Vector3(o.X, transform.position.y + o.R * 0.2f, o.Z), 0.4f);
            msg = $"пельмень готов ✔ — {o.Acc / 2.55f:0}%: {GradeName(o.Acc / 255f)}{(o.Fill == 0 ? " (пустышка)" : "")}";
            if (_log) Debug.Log($"[Пельмени] «{name}»: ушко — {msg}", this);
            return true;
        }

        public static string GradeName(float acc) => acc >= 0.8f ? "идеальный" : acc >= 0.5f ? "кривой" : "рваный";

        /// <summary>Тесто кружка ушло в пельмень: клетки внутри — пусто. Возвращает объём, м³.</summary>
        private float ClearCircleCells(DoughCircle o)
        {
            Vector3 l = new Vector3(o.X, 0f, o.Z) - transform.position;
            Vector2 c0 = new Vector2(l.x, l.z);
            float r = o.R, sum = 0f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((l.x - r + Half) / Cell)), x1 = Mathf.Min(_n - 1, Mathf.FloorToInt((l.x + r + Half) / Cell));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((l.z - r + Half) / Cell)), y1 = Mathf.Min(_n - 1, Mathf.FloorToInt((l.z + r + Half) / Cell));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if ((CellPos(x, y) - c0).sqrMagnitude > r * r) continue;
                    int i = y * _n + x;
                    sum += _h[i]; _h[i] = 0f;
                    if (DirtOk) _dirt[i] = 0f;
                }
            return sum * Cell * Cell;
        }

        /// <summary>Валец катка проехал: кружки под ним пропали (тесто осталось), пельмени — в лепёшку.</summary>
        private void CrushCircles(Vector3 ground, Vector3 axle, float width)
        {
            Vector2 g = new Vector2(ground.x, ground.z), a = new Vector2(axle.x, axle.z);
            if (a.sqrMagnitude < 1e-6f) return;
            a.Normalize();
            float half = width * 0.5f;
            for (int i = _circles.Count - 1; i >= 0; i--)
            {
                DoughCircle o = _circles[i];
                Vector2 d = new Vector2(o.X, o.Z) - g;
                float along = Mathf.Clamp(Vector2.Dot(d, a), -half, half);
                if ((d - a * along).magnitude > o.R * 0.6f) continue;
                if (o.State == CircleCut)
                {
                    _circles.RemoveAt(i);
                    if (_log) Debug.Log($"[Пельмени] «{name}»: каток проехал по кружку — смял, кружка нет (осталось {_circles.Count})", this);
                }
                else if (o.State != CircleFlat)
                {
                    o.State = CircleFlat; _circles[i] = o;
                    if (_log) Debug.Log($"[Пельмени] «{name}»: каток проехал по пельменю — ЛЕПЁШКА", this);
                }
                else continue;
                _circlesDirty = true; _dirtyMesh = true;
            }
        }

        [Server]
        private void SendCircles()
        {
            int k = _circles.Count;
            var xs = new float[k]; var zs = new float[k]; var meta = new byte[k * 7];
            for (int i = 0; i < k; i++)
            {
                DoughCircle o = _circles[i];
                xs[i] = o.X; zs[i] = o.Z;
                int mm = Mathf.Clamp(Mathf.RoundToInt(o.R * 1000f), 0, 65535);
                meta[i * 7] = (byte)(mm & 255); meta[i * 7 + 1] = (byte)(mm >> 8);
                meta[i * 7 + 2] = o.State; meta[i * 7 + 3] = o.Fill; meta[i * 7 + 4] = o.Acc; meta[i * 7 + 5] = o.Q; meta[i * 7 + 6] = o.Dir;
            }
            RpcCircles(xs, zs, meta);
        }

        [ObserversRpc(BufferLast = true)]
        private void RpcCircles(float[] xs, float[] zs, byte[] meta)
        {
            if (base.IsServerInitialized) return;          // хост — своя правда
            _circles.Clear();
            int k = xs != null && zs != null && meta != null ? Mathf.Min(xs.Length, Mathf.Min(zs.Length, meta.Length / 7)) : 0;
            for (int i = 0; i < k; i++)
                _circles.Add(new DoughCircle
                {
                    X = xs[i], Z = zs[i], R = (meta[i * 7] | (meta[i * 7 + 1] << 8)) / 1000f,
                    State = meta[i * 7 + 2], Fill = meta[i * 7 + 3], Acc = meta[i * 7 + 4], Q = meta[i * 7 + 5], Dir = meta[i * 7 + 6]
                });
            _dirtyMesh = true;
        }

        /// <summary>Ведро провернули: пыль мукой по кольцу (у всех).</summary>
        [ObserversRpc]
        private void RpcRing(Vector3 at, float r)
        {
            for (int k = 0; k < 8; k++)
            {
                float a = k / 8f * Mathf.PI * 2f;
                Vector3 p = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                CCRuntimeParticles.Burst(p, Vector3.up, new Color(0.97f, 0.95f, 0.9f), _doughColor, 3, CCRuntimeParticles.Look.Dust.Scaled(2f));
            }
        }

        // ── локальная анимация у держащего плоскогубцы ──

        /// <summary>Плоскогубцы: показать у себя складывание/защип/ушко кружка (x,z), t 0..1, mask — защипнутые участки шва.</summary>
        public void LocalAnim(byte kind, float x, float z, float yaw, float t, int mask)
        {
            _animKind = kind; _animX = x; _animZ = z; _animYaw = yaw; _animT = Mathf.Clamp01(t); _animMask = mask;
            _animActive = kind != AnimNone;
            BuildCircleMesh(false);                                               // сразу, без коллайдера — каждый кадр дёшево
        }

        public void LocalAnimStop()
        {
            if (!_animActive) return;
            _animActive = false; _animKind = AnimNone;
            _dirtyMesh = true;
        }

        /// <summary>Точка шва (дуга сложенного пельменя) по s∈[-1,1] — в мире. Для плоскогубцев.</summary>
        public Vector3 SeamPoint(DoughCircle o, float yaw, float s)
        {
            float r = o.R * 0.97f, x = s * r, zz = r * Mathf.Sqrt(Mathf.Max(0f, 1f - s * s));
            float cy = Mathf.Cos(yaw), sy = Mathf.Sin(yaw);
            return new Vector3(o.X + x * cy + zz * sy, transform.position.y + o.R * 0.08f, o.Z - x * sy + zz * cy);
        }

        /// <summary>
        /// Лопата / мастерок по пласту: забрать готовые пельмени (до 6 за раз, ближние к прицелу — первыми).
        /// Качество (точность шва, начинка) уходит в «чистоту» порции — её видит тарелка. Лепёшки и недолепленные не берутся.
        /// </summary>
        [Server]
        private int ServerScoopPelmeni(int max, out ushort product, out FoodState state, out byte cook, out byte clean)
        {
            product = PelmeniProduct.Id; state = PelmeniProduct.Raw; cook = 0; clean = 100;
            max = Mathf.Min(max, 6);
            if (product == 0 || max <= 0) { if (product == 0 && _log) Debug.LogWarning($"[Пельмени] «{name}»: нет продукта «Пельмени» — CatCooks → Пельмени → 1", this); return 0; }
            var idx = new List<int>();
            for (int i = 0; i < _circles.Count; i++) if (_circles[i].State == CircleShaped) idx.Add(i);
            if (idx.Count == 0) return 0;
            Vector3 aim = _hudPoint;                                                 // у хоста — его прицел; у прочих — просто по порядку
            idx.Sort((a, c) => (new Vector2(_circles[a].X - aim.x, _circles[a].Z - aim.z)).sqrMagnitude.CompareTo((new Vector2(_circles[c].X - aim.x, _circles[c].Z - aim.z)).sqrMagnitude));
            int took = Mathf.Min(max, idx.Count);
            float q = 0f; int empty = 0;
            var gone = new List<int>();
            for (int k = 0; k < took; k++)
            {
                DoughCircle c = _circles[idx[k]];
                q += c.Acc / 255f;
                if (c.Fill == 0) empty++;
                gone.Add(idx[k]);
            }
            gone.Sort();
            for (int k = gone.Count - 1; k >= 0; k--) _circles.RemoveAt(gone[k]);
            clean = (byte)Mathf.Clamp(Mathf.RoundToInt(q / took * 100f), 1, 100);
            _circlesDirty = true; _dirtyMesh = true;
            if (_log) Debug.Log($"[Пельмени] «{name}»: лопатой взяли {took} пельм. (среднее {clean}%{(empty > 0 ? $", пустышек {empty}" : "")}), на пласте ещё {CountShaped()}", this);
            return took;
        }

        private int CountShaped() { int n = 0; foreach (DoughCircle c in _circles) if (c.State == CircleShaped) n++; return n; }

        /// <summary>Подсказка про кружки (вторая строка HUD).</summary>
        private bool CircleHud(out string text)
        {
            int cut = 0, folded = 0, sealedN = 0, ideal = 0, crooked = 0, torn = 0, flat = 0, empty = 0, filled = 0;
            for (int i = 0; i < _circles.Count; i++)
            {
                DoughCircle o = _circles[i];
                if (o.State == CircleCut) { cut++; if (o.Fill > 0) filled++; }
                else if (o.State == CircleFlat) flat++;
                else if (o.State == CircleFolded) folded++;
                else if (o.State == CircleSealed) sealedN++;
                else
                {
                    float a = o.Acc / 255f;
                    if (a >= 0.8f) ideal++; else if (a >= 0.5f) crooked++; else torn++;
                    if (o.Fill == 0) empty++;
                }
            }
            int shaped = ideal + crooked + torn;
            string head = $"<b>Кружков</b> {cut}{(filled > 0 ? $" (с начинкой {filled})" : "")}" + (folded + sealedN > 0 ? $" · в работе {folded + sealedN}" : "") +
                          $" · <b>пельменей</b> {shaped}" +
                          (shaped > 0 ? $": <color=#9BE37A>{ideal} идеальн.</color> / <color=#FFD25A>{crooked} кривых</color> / <color=#FF6A4A>{torn} рваных</color>" : "") +
                          (empty > 0 ? $" · пустышек {empty}" : "") + (flat > 0 ? $" · <color=#FF6A4A>лепёшек {flat}</color>" : "");
            string sub = "";
            int at = CircleAt(_hudPoint);
            if (at >= 0)
            {
                DoughCircle o = _circles[at];
                float a = o.Acc / 255f;
                string col = a >= 0.8f ? "#9BE37A" : a >= 0.5f ? "#FFD25A" : "#FF6A4A";
                switch (o.State)
                {
                    case CircleCut: sub = $"кружок Ø {o.R * 200f:0} см, качество {o.Q / 2.55f:0}%, " + (o.Fill == 0 ? "без начинки — мастерком «Фарш для пельменей»" : o.Fill == 1 ? "начинка ✔ — плоскогубцы: ЛКМ держать" : "начинки много"); break;
                    case CircleFolded: sub = "сложен — плоскогубцы ЛКМ держать и вести по шву (краю)"; break;
                    case CircleSealed: sub = $"шов зажат: <color={col}>{a * 100f:0}%</color> — плоскогубцы ПКМ: загнуть ушком"; break;
                    case CircleShaped: sub = $"пельмень: <color={col}>{a * 100f:0}% — {GradeName(a)}</color>{(o.Fill == 0 ? " (пустышка)" : "")}"; break;
                    default: sub = "<color=#FF6A4A>лепёшка — брак</color>"; break;
                }
            }
            text = sub.Length > 0 ? head + "\n" + sub : head;
            return true;
        }

        // ── вид: кольца-бороздки, шарик начинки, пельмени (у всех) ──

        private void BuildCircleMesh() => BuildCircleMesh(true);

        private void BuildCircleMesh(bool withCollider)
        {
            if (_circles.Count == 0)
            {
                if (_circMesh != null) _circMesh.Clear();
                if (_circCol != null) _circCol.sharedMesh = null;
                return;
            }
            if (_circGo == null)
            {
                _circGo = new GameObject("Circles (auto)") { hideFlags = HideFlags.DontSave };
                _circGo.transform.SetParent(transform, false);
                _circMesh = new Mesh { name = "pelmeni (auto)", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                _circMesh.MarkDynamic();
                _circGo.AddComponent<MeshFilter>().sharedMesh = _circMesh;
                MeshRenderer r = _circGo.AddComponent<MeshRenderer>();
                r.sharedMaterial = _mr != null ? _mr.sharedMaterial : null;
                _circCol = _circGo.AddComponent<MeshCollider>();          // прицел попадает в пельмень (под ним теста уже нет)
                _ignored.Clear(); _ignoreAt = 0f;                           // каток — сквозь, как и через пласт
            }
            _circGo.transform.localPosition = Vector3.zero; _circGo.transform.localRotation = Quaternion.identity; _circGo.transform.localScale = Vector3.one;
            _cv.Clear(); _cc.Clear(); _cuv.Clear(); _ct.Clear();
            foreach (DoughCircle o in _circles)
            {
                Vector3 lc = new Vector3(o.X, 0f, o.Z) - transform.position;
                bool anim = _animActive && Mathf.Abs(o.X - _animX) < 0.05f && Mathf.Abs(o.Z - _animZ) < 0.05f;
                float yaw = o.Dir / 255f * Mathf.PI * 2f;
                if (anim && _animKind == AnimFold && o.State == CircleCut) AddFoldAnim(lc, o, _animYaw, _animT);
                else if (anim && _animKind == AnimFold) AddPelmen(lc, o, _animYaw, 0f, 0, false);
                else if (anim && _animKind == AnimSeal && o.State != CircleShaped && o.State != CircleFlat) AddPelmen(lc, o, o.State == CircleCut ? _animYaw : yaw, 0f, _animMask, false);
                else if (anim && _animKind == AnimCurl && o.State == CircleSealed) AddPelmen(lc, o, yaw, _animT, (1 << SealBins) - 1, false);
                else if (o.State == CircleCut)
                {
                    AddRing(lc, o.R);
                    if (o.Fill > 0) AddFillBall(lc, o.R, o.Fill);
                }
                else if (o.State == CircleFolded) AddPelmen(lc, o, yaw, 0f, 0, false);
                else if (o.State == CircleSealed) AddPelmen(lc, o, yaw, 0f, (1 << SealBins) - 1, false);
                else if (o.State == CircleShaped) AddPelmen(lc, o, yaw, 1f, (1 << SealBins) - 1, false);
                else AddPelmen(lc, o, yaw, 1f, (1 << SealBins) - 1, true);
            }
            _circMesh.Clear();
            _circMesh.SetVertices(_cv); _circMesh.SetColors(_cc); _circMesh.SetUVs(0, _cuv);
            _circMesh.SetTriangles(_ct, 0);
            _circMesh.RecalculateNormals(); _circMesh.RecalculateBounds();
            if (withCollider && _circCol != null && _ct.Count > 0) { _circCol.sharedMesh = null; _circCol.sharedMesh = _circMesh; }
        }

        private float SurfY(float lx, float lz)
        {
            Vector3 w = transform.position + new Vector3(lx, 0f, lz);
            return HeightAt(w, out float h) ? h : 0f;
        }

        private void CV(float x, float y, float z, Color c) { _cv.Add(new Vector3(x, y, z)); _cuv.Add(new Vector2(x * _texPerMeter, z * _texPerMeter)); c.a = 0.55f; _cc.Add(c); }

        private void Quad(int a, int b, int c, int d) { TriUp(a, b, c); TriUp(a, c, d); }

        private void TriUp(int a, int b, int d)
        {
            Vector3 va = _cv[a], vb = _cv[b], vd = _cv[d];
            float cy = (vb.z - va.z) * (vd.x - va.x) - (vb.x - va.x) * (vd.z - va.z);
            if (cy >= 0f) { _ct.Add(a); _ct.Add(b); _ct.Add(d); } else { _ct.Add(a); _ct.Add(d); _ct.Add(b); }
        }

        /// <summary>Бороздка от кромки ведра: тёмное кольцо по поверхности, внутренний край чуть приподнят (кружок «отжат»).</summary>
        private void AddRing(Vector3 lc, float r)
        {
            const int seg = 48;
            float w = 0.014f * S, lift = 0.004f * S;
            Color dark = _doughColor * 0.68f, lip = Color.Lerp(_doughColor, Color.white, 0.12f);
            int b = _cv.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                float xi = lc.x + cs * (r - w * 1.6f), zi = lc.z + sn * (r - w * 1.6f);
                float xm = lc.x + cs * (r - w), zm = lc.z + sn * (r - w);
                float xo = lc.x + cs * (r + w), zo = lc.z + sn * (r + w);
                CV(xi, SurfY(xi, zi) + lift, zi, lip);
                CV(xm, SurfY(xm, zm) + lift * 0.6f, zm, dark);
                CV(xo, SurfY(xo, zo) + lift * 0.6f, zo, dark);
            }
            for (int i = 0; i < seg; i++)
            {
                int p = b + i * 3, q = p + 3;
                Quad(p, q, q + 1, p + 1);
                Quad(p + 1, q + 1, q + 2, p + 2);
            }
        }

        /// <summary>Шарик фарша на кружке.</summary>
        private void AddFillBall(Vector3 lc, float r, byte fill)
        {
            const int seg = 16, rings = 5;
            float br = r * (fill > 1 ? 0.42f : 0.32f), bh = br * 0.75f, y0 = SurfY(lc.x, lc.z);
            Color meat = new Color(0.62f, 0.38f, 0.32f);
            int b = _cv.Count;
            for (int j = 0; j <= rings; j++)
            {
                float t = j / (float)rings, rr = br * Mathf.Cos(t * Mathf.PI * 0.5f), yy = y0 + bh * Mathf.Sin(t * Mathf.PI * 0.5f);
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    float bump = 1f + 0.08f * Mathf.Sin(a * 5f + j * 1.7f);
                    CV(lc.x + Mathf.Cos(a) * rr * bump, yy, lc.z + Mathf.Sin(a) * rr * bump, meat * (0.9f + 0.1f * Mathf.Sin(a * 7f + j)));
                }
            }
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    int p = b + j * (seg + 1) + i;
                    Quad(p, p + 1, p + seg + 2, p + seg + 1);
                }
        }

        /// <summary>
        /// Складывание (у держащего плоскогубцы): ближняя половина кружка лежит, дальняя поднимается по оси сгиба
        /// и ложится сверху на начинку. t 0 — плоский кружок, 1 — сложен.
        /// </summary>
        private void AddFoldAnim(Vector3 lc, DoughCircle o, float yaw, float t)
        {
            const int ns = 16, nd = 6;
            float r = o.R * 0.97f, y0 = SurfY(lc.x, lc.z) + 0.004f * S;
            float fillH = o.R * (o.Fill == 0 ? 0.08f : o.Fill == 1 ? 0.22f : 0.3f);
            float alpha = Mathf.SmoothStep(0f, 1f, t) * Mathf.PI;
            float cy = Mathf.Cos(yaw), sy = Mathf.Sin(yaw);
            Color dough = Color.Lerp(_doughColor, Color.white, 0.06f), under = _doughColor * 0.9f;
            for (int half = 0; half < 2; half++)
            {
                int b = _cv.Count;
                for (int j = 0; j <= nd; j++)
                    for (int i = 0; i <= ns; i++)
                    {
                        float s = -1f + 2f * i / ns, x = s * r, dmax = r * Mathf.Sqrt(Mathf.Max(0f, 1f - s * s)), d = dmax * j / nd;
                        float zz, yy;
                        if (half == 0) { zz = d; yy = y0 + fillH * 0.25f * Mathf.Sin(Mathf.PI * j / nd) * (1f - s * s) * (o.Fill > 0 ? 1f : 0f); }
                        else { zz = -d * Mathf.Cos(alpha); yy = y0 + d * Mathf.Sin(alpha) + fillH * (alpha / Mathf.PI) * Mathf.Sin(Mathf.PI * j / nd) * (1f - s * s); }
                        CV(lc.x + x * cy + zz * sy, yy, lc.z - x * sy + zz * cy, half == 0 ? under : dough);
                    }
                for (int j = 0; j < nd; j++)
                    for (int i = 0; i < ns; i++)
                    {
                        int p = b + j * (ns + 1) + i;
                        // двусторонне: верхняя половина переворачивается — рисуем обе стороны
                        _ct.Add(p); _ct.Add(p + ns + 1); _ct.Add(p + 1); _ct.Add(p + 1); _ct.Add(p + ns + 1); _ct.Add(p + ns + 2);
                        _ct.Add(p); _ct.Add(p + 1); _ct.Add(p + ns + 1); _ct.Add(p + 1); _ct.Add(p + ns + 2); _ct.Add(p + ns + 1);
                    }
            }
        }

        /// <summary>
        /// Пельмень: кружок, сложенный пополам. bend 0 — полумесяц (сгиб прямой, шов к игроку), 1 — настоящий пельмень:
        /// концы сведены сзади, пузико круглым куполом, шов — волнистый воротник. sealMask — какие участки шва зажаты.
        /// Пустышка — плоский, лепёшка — расплющен, рваный (плохой шов) — начинка видна по шву.
        /// </summary>
        private void AddPelmen(Vector3 lc, DoughCircle o, float yaw, float bend, int sealMask, bool flat)
        {
            // Одна сетка (s — вдоль сгиба/по кругу, t — от сгиба к шву) и две формы, между ними плавно (bend):
            //   0 — ПОЛУМЕСЯЦ (вареник): сгиб прямой, шов — дуга к игроку;
            //   1 — ПЕЛЬМЕНЬ (как настоящий): концы сведены и защипнуты сзади, пузико с фаршем — круглый купол в центре,
            //       шов — волнистый «воротник» вокруг, приподнят и отогнут наружу.
            const int ns = 28, nt = 12;
            float r = o.R * 0.97f, acc = o.State == CircleShaped || o.State == CircleFlat ? o.Acc / 255f : 1f;
            bool torn = acc < 0.5f, filled = o.Fill > 0;
            float seed = Mathf.Abs(Mathf.Sin(o.X * 12.9898f + o.Z * 78.233f) * 43758.5453f) % 1f;
            float skew = acc < 0.8f ? (0.8f - acc) * 0.9f * (seed > 0.5f ? 1f : -1f) : 0f;
            float fillK = filled ? (o.Fill > 1 ? 1.15f : 1f) : 0.55f;
            float flatK = flat ? 0.28f : 1f;
            float H = o.R * 0.34f * fillK * flatK;                          // полумесяц
            float hB = o.R * 0.46f * fillK * flatK;                         // купол пельменя
            float spread = flat ? 1.25f : 1f;
            float b = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(bend));
            float cy = Mathf.Cos(yaw), sy = Mathf.Sin(yaw);
            float tilt = acc < 0.8f ? (0.8f - acc) * 0.4f * (seed > 0.3f ? 1f : -1f) : 0f;
            Color dough = Color.Lerp(_doughColor, Color.white, 0.08f), meat = new Color(0.6f, 0.36f, 0.3f);
            float leakS = seed * 1.2f - 0.6f;
            float y0 = 0.003f * S;
            const float tb = 0.5f;                                          // доля сетки под пузико (остальное — воротник)
            float rB = r * 0.48f * spread, rRim = r * 0.92f * spread;
            int first = _cv.Count;
            for (int j = 0; j <= nt; j++)
                for (int i = 0; i <= ns; i++)
                {
                    float s = -1f + 2f * i / ns, t = j / (float)nt;
                    int bin = Mathf.Clamp(Mathf.FloorToInt((s + 1f) * 0.5f * SealBins), 0, SealBins - 1);
                    bool sealedHere = (sealMask & (1 << bin)) != 0;
                    float crimp = t > 0.75f ? (t - 0.75f) / 0.25f : 0f;
                    float wave = Mathf.Sin(s * 30f + seed * 6f);

                    // ── полумесяц ──
                    float zArc = r * spread * Mathf.Sqrt(Mathf.Max(0.02f, 1f - s * s));
                    float hx = (s + skew * 0.3f * (1f - s * s)) * r * spread;
                    float hz = t * zArc + (sealedHere ? crimp * r * 0.02f * wave : 0f);
                    bool hBorder = i == 0 || i == ns || j == 0 || j == nt;
                    float prof = 0.2f + 0.8f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * (0.2f + 0.8f * t))), 0.8f);
                    float end = 0.25f + 0.75f * Mathf.Pow(Mathf.Max(0f, 1f - s * s), 0.4f);
                    float hy = hBorder ? y0 : H * prof * end + y0;
                    if (!hBorder && crimp > 0f) hy += sealedHere ? crimp * H * 0.05f * wave : crimp * H * 0.12f;

                    // ── круглый пельмень ──
                    float phi = (s + skew * 0.25f * (1f - s * s)) * Mathf.PI * 0.92f;   // концы сходятся сзади (φ = ±π)
                    float pinch = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, Mathf.Abs(s)));
                    float rho, ry;
                    if (t <= tb)
                    {
                        float u = t / tb;                                                   // пузико: купол
                        rho = rB * u;
                        ry = y0 + hB * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u * 0.85f));
                    }
                    else
                    {
                        float u = (t - tb) / (1f - tb);                                     // воротник: от купола наружу и вверх, волной
                        float ruff = Mathf.Sin(s * 26f + seed * 5f + u * 2f);
                        rho = Mathf.Lerp(rB, rRim, u) + r * 0.035f * u * ruff * (torn ? 2.5f : 1f);
                        ry = y0 + hB * Mathf.Lerp(0.39f, 0.62f, u) + hB * 0.07f * u * ruff;
                        if (j == nt) ry -= hB * 0.04f;                                       // край воротника тоньше
                    }
                    rho *= 1f - 0.4f * pinch;                                                // сзади концы стянуты защипом
                    ry += pinch * hB * 0.12f * (t > tb ? 1f : 0.3f);
                    float rx = Mathf.Sin(phi) * rho, rz = Mathf.Cos(phi) * rho;

                    float px = Mathf.Lerp(hx, rx, b), pz = Mathf.Lerp(hz, rz, b), y = Mathf.Lerp(hy, ry, b);
                    y += px * tilt * 0.5f;
                    if (y < y0) y = y0;
                    float wx = px * cy + pz * sy, wz = -px * sy + pz * cy;
                    float shade = Mathf.Lerp(0.93f + 0.07f * prof, t <= tb ? 1f : 0.95f + 0.05f * Mathf.Sin(s * 26f), b);
                    Color col = dough * (shade + (sealedHere && crimp > 0.5f ? -0.05f * wave * (1f - b) : 0f));
                    if (!sealedHere && crimp > 0.6f && filled) col = Color.Lerp(col, meat, 0.35f);                         // щель — видно фарш
                    if (filled && (torn || flat) && t > 0.55f && Mathf.Abs(s - leakS) < 0.3f) col = Color.Lerp(col, meat, 0.75f);
                    CV(lc.x + wx, y, lc.z + wz, col);
                }
            for (int j = 0; j < nt; j++)
                for (int i = 0; i < ns; i++)
                {
                    int p = first + j * (ns + 1) + i;
                    TriUp(p, p + 1, p + ns + 2); TriUp(p, p + ns + 2, p + ns + 1);
                }
        }

        [ContextMenu("Тест пельменей: начинка во все кружки (Play, хост)")]
        private void TestFillAll()
        {
            if (!base.IsServerInitialized) { Debug.LogWarning("[Пельмени] только в Play на хосте", this); return; }
            int k = 0;
            for (int i = 0; i < _circles.Count; i++) { DoughCircle o = _circles[i]; if (o.State == CircleCut && o.Fill == 0) { o.Fill = 1; _circles[i] = o; k++; } }
            _circlesDirty = true; _dirtyMesh = true;
            Debug.Log($"[Пельмени] «{name}»: тест — начинка в {k} кружков", this);
        }

        [ContextMenu("Тест пельменей: убрать все кружки и пельмени (Play, хост)")]
        private void TestClearCircles()
        {
            if (!base.IsServerInitialized) return;
            _circles.Clear(); _circlesDirty = true; _dirtyMesh = true;
        }

        // ───────────── для катка: по какому слою он едет и сколько кучи перед ним ─────────────

        public static readonly List<DoughSheet> All = new List<DoughSheet>();
        private void OnEnable() { if (!All.Contains(this)) All.Add(this); if (!DoughStick.Spatulable.Contains(this)) DoughStick.Spatulable.Add(this); }
        private void OnDisable() { All.Remove(this); DoughStick.Spatulable.Remove(this); }
        public GameObject PiecePrefab => _piecePrefab;

        /// <summary>Высота теста в точке (м, плавно между клетками). false — не над тестом.</summary>
        public bool HeightAt(Vector3 world, out float h)
        {
            h = 0f;
            if (_h == null) return false;
            Vector3 l = world - transform.position;
            float fx = (l.x + Half) / Cell - 0.5f, fy = (l.z + Half) / Cell - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            if (x0 < -1 || y0 < -1 || x0 >= _n || y0 >= _n) return false;
            float tx = fx - x0, ty = fy - y0;
            h = Mathf.Lerp(Mathf.Lerp(Hc(x0, y0), Hc(x0 + 1, y0), tx), Mathf.Lerp(Hc(x0, y0 + 1), Hc(x0 + 1, y0 + 1), tx), ty);
            return h > 0.001f;
        }

        private float Hc(int x, int y) => x < 0 || y < 0 || x >= _n || y >= _n ? 0f : _h[y * _n + x];

        /// <summary>Каток едет ПО слою теста не толще Ride Max (что толще — срезает и толкает). Мир, высота опоры над землёй.</summary>
        public float RideOn(Vector3 world) => HeightAt(world, out float h) ? Mathf.Min(h, _rideMax * S) : 0f;

        /// <summary>Сколько кучи выше вальца перед ним (м, в среднем по ширине) — каток от неё тормозит.</summary>
        public float LoadAhead(Vector3 ground, Vector3 dir, Vector3 axle, float radius, float width)
        {
            float sum = 0f; int n = 0;
            float bottom = ground.y - transform.position.y;
            for (int s = -1; s <= 1; s++)
            {
                Vector3 p = ground + axle.normalized * (s * width * 0.4f) + dir.normalized * (radius * 0.9f + Cell);
                if (HeightAt(p, out float h)) sum += Mathf.Max(0f, h - bottom - radius * 0.25f);
                n++;
            }
            return sum / Mathf.Max(1, n);
        }

        private readonly HashSet<int> _under = new HashSet<int>(), _bow = new HashSet<int>();

        private void AddCell(HashSet<int> set, Vector2 p)
        {
            int x = Mathf.FloorToInt((p.x + Half) / Cell), y = Mathf.FloorToInt((p.y + Half) / Cell);
            if (x >= 0 && y >= 0 && x < _n && y < _n) set.Add(y * _n + x);
        }

        private void Changed() { _needRelax = true; _dirtyNet = true; _dirtyMesh = true; _dirtCheck = true; }

        // ───────────────────────── сеть ─────────────────────────

        [Server]
        private void NetTick()
        {
            if (_circlesDirty && Time.time >= _circSendAt) { _circSendAt = Time.time + 0.1f; _circlesDirty = false; SendCircles(); }
            if (!_dirtyNet || Time.time < _sendAt) return;
            _sendAt = Time.time + 0.25f;
            _dirtyNet = false;
            // высота по сети — корнем: тонкий пласт точнее (пельмени 4–5 см: шаг ~1.6 мм вместо ~4), толстый ком — грубее, но ему и не надо
            var b = new byte[_h.Length];
            float inv = 1f / Mathf.Max(0.01f, _maxH * S);
            for (int i = 0; i < b.Length; i++) b[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(Mathf.Clamp01(_h[i] * inv)) * 255f), 0, 255);
            byte[] db = new byte[(_h.Length + 7) / 8];
            if (DirtOk) for (int i = 0; i < _h.Length; i++) if (_dirt[i] > 0.5f && _h[i] > 0.004f) db[i >> 3] |= (byte)(1 << (i & 7));
            byte[] cb = null;
            if (_cutX != null && _cutX.Length == _h.Length && (_hasCuts || _hasSoft))
            {
                cb = new byte[((_h.Length + 7) / 8) * 2];
                int half = (_h.Length + 7) / 8;
                bool soft = _softX != null && _softX.Length == _h.Length;
                for (int i = 0; i < _h.Length; i++)
                {
                    if (_cutX[i] || (soft && _softX[i])) cb[i >> 3] |= (byte)(1 << (i & 7));
                    if (_cutZ[i] || (soft && _softZ[i])) cb[half + (i >> 3)] |= (byte)(1 << (i & 7));
                }
            }
            RpcState(transform.position, (ushort)_n, _cell, (byte)_kind, _maxH, b, db, cb, (byte)Mathf.RoundToInt(Quality01 * 255f), (byte)Mathf.RoundToInt(Dirty01 * 255f), (byte)Mathf.Min(255, _stuck));
        }

        [ObserversRpc(BufferLast = true)]
        private void RpcState(Vector3 pos, ushort n, float cell, byte kind, float maxH, byte[] b, byte[] dirt, byte[] cuts, byte quality, byte dirty, byte stuck)
        {
            if (base.IsServerInitialized) return;          // хост — своя правда
            transform.position = pos;
            if ((DoughKind)kind != _kind) { _kind = (DoughKind)kind; if (_kind != DoughKind.Custom) ApplyPreset(_kind); }
            _maxH = maxH;
            if (n != _n || Mathf.Abs(cell - _cell) > 1e-6f) { _n = n; _cell = cell; _cw = -1f; _h = new float[_n * _n]; _dirt = null; }
            if (b != null && b.Length == _h.Length)
            {
                float mh = Mathf.Max(0.01f, _maxH * S);
                for (int i = 0; i < b.Length; i++) { float t = b[i] / 255f; _h[i] = t * t * mh; }     // корень на сервере — квадрат тут
            }
            if (dirt != null && dirt.Length == (_h.Length + 7) / 8)
            {
                if (_dirt == null || _dirt.Length != _h.Length) _dirt = new float[_h.Length];
                for (int i = 0; i < _h.Length; i++) _dirt[i] = (dirt[i >> 3] >> (i & 7) & 1) != 0 ? 1f : 0f;
            }
            int hb = (_h.Length + 7) / 8;
            if (cuts != null && cuts.Length == hb * 2)
            {
                if (_cutX == null || _cutX.Length != _h.Length) { _cutX = new bool[_h.Length]; _cutZ = new bool[_h.Length]; }
                for (int i = 0; i < _h.Length; i++) { _cutX[i] = (cuts[i >> 3] >> (i & 7) & 1) != 0; _cutZ[i] = (cuts[hb + (i >> 3)] >> (i & 7) & 1) != 0; }
                _hasCuts = true;
            }
            else if (_cutX != null) { System.Array.Clear(_cutX, 0, _cutX.Length); System.Array.Clear(_cutZ, 0, _cutZ.Length); _hasCuts = false; }
            _qNet = quality; _dirtNet = dirty; _stuck = stuck;
            _dirtyMesh = true;
        }

        [ObserversRpc]
        private void RpcPuff(Vector3 at, float k)
        {
            CCRuntimeParticles.Burst(at, Vector3.up, new Color(0.97f, 0.95f, 0.9f), _doughColor, Mathf.RoundToInt(14 * k), CCRuntimeParticles.Look.Dust.Scaled(3f));
        }

        // ───────────────────────── вид ─────────────────────────

        private MaterialPropertyBlock _mpb;

        private float[] _occ, _cm, _ch;
        private readonly List<Vector3> _mv = new List<Vector3>();
        private readonly List<Vector2> _muv = new List<Vector2>();
        private readonly List<Color> _mcol = new List<Color>();
        private readonly List<int> _mt = new List<int>();
        private int[] _ci, _hi, _vi;

        /// <summary>Пласт по сетке, но край — гладкий контур (marching squares), кромка скруглена «под себя»:
        /// у края толщина плавно уходит вниз (√ от «насколько внутри»), стенок-ступенек нет.</summary>
        private void BuildMesh()
        {
            int n = _n, m = n + 1;
            float c = Cell, eps = 0.006f * S;
            if (_mesh == null) { _mesh = new Mesh { name = name + " dough (auto)", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; _mesh.MarkDynamic(); }
            if (_occ == null || _occ.Length != _h.Length) { _occ = new float[_h.Length]; _blur = new float[_h.Length]; }
            if (_cm == null || _cm.Length != m * m) { _cm = new float[m * m]; _ch = new float[m * m]; _ci = new int[m * m]; _hi = new int[n * m]; _vi = new int[m * n]; }
            for (int i = 0; i < _h.Length; i++) _occ[i] = Mathf.Clamp01(_h[i] / eps);
            // мягкая «маска» (3×3) — по ней край и скругление
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float s = 0f, w = 0f;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        float k = (dx == 0 ? 2f : 1f) * (dy == 0 ? 2f : 1f);
                        w += k;
                        if (xx < 0 || yy < 0 || xx >= n || yy >= n) continue;
                        s += _occ[yy * n + xx] * k;
                    }
                    _blur[y * n + x] = s / w;
                }
            // углы: маска и толщина (средняя по тесту рядом, без «нулей» снаружи)
            for (int y = 0; y < m; y++)
                for (int x = 0; x < m; x++)
                {
                    float ms = 0f, hs = 0f, hw = 0f;
                    for (int dy = -1; dy <= 0; dy++) for (int dx = -1; dx <= 0; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= n || yy >= n) continue;
                        int j = yy * n + xx;
                        ms += _blur[j];
                        if (_h[j] > eps * 0.25f) { hs += _h[j]; hw += 1f; }
                    }
                    int i = y * m + x;
                    _cm[i] = ms * 0.25f;
                    _ch[i] = hw > 0f ? hs / hw : 0f;
                }
            // резы (настоящие и «мягкие» — ньокки в полосе): углы на линии реза — пусто, видна щель до низа
            if (_cutX != null && _cutX.Length == _h.Length && (_hasCuts || _hasSoft))
            {
                bool soft = _softX != null && _softX.Length == _h.Length;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int i = y * n + x;
                        if (_cutX[i] || (soft && _softX[i])) { _cm[(y + 1) * m + x] = 0f; _cm[(y + 1) * m + x + 1] = 0f; }
                        if (_cutZ[i] || (soft && _softZ[i])) { _cm[y * m + x + 1] = 0f; _cm[(y + 1) * m + x + 1] = 0f; }
                    }
            }
            const float iso = 0.22f;
            _mv.Clear(); _muv.Clear(); _mcol.Clear(); _mt.Clear();
            for (int i = 0; i < _ci.Length; i++) _ci[i] = -1;
            for (int i = 0; i < _hi.Length; i++) _hi[i] = -1;
            for (int i = 0; i < _vi.Length; i++) _vi[i] = -1;
            Color dc = Color.Lerp(_doughColor, new Color(0.99f, 0.98f, 0.95f), _flourAmt * 0.3f);
            var poly = new int[8];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int c00 = y * m + x, c10 = c00 + 1, c11 = c00 + m + 1, c01 = c00 + m;
                    float m00 = _cm[c00], m10 = _cm[c10], m11 = _cm[c11], m01 = _cm[c01];
                    if (m00 < iso && m10 < iso && m11 < iso && m01 < iso) continue;
                    int k = 0;
                    // обход квадрата: (0,0) → (1,0) → (1,1) → (0,1); между «внутри» и «снаружи» — точка контура
                    k = Corner(poly, k, c00, x, y, m00, iso, c, dc);
                    k = EdgeH(poly, k, x, y, m00, m10, iso, c, dc, m);            // низ: (x,y)–(x+1,y)
                    k = Corner(poly, k, c10, x + 1, y, m10, iso, c, dc);
                    k = EdgeV(poly, k, x + 1, y, m10, m11, iso, c, dc, m);        // право: (x+1,y)–(x+1,y+1)
                    k = Corner(poly, k, c11, x + 1, y + 1, m11, iso, c, dc);
                    k = EdgeH(poly, k, x, y + 1, m01, m11, iso, c, dc, m, true);  // верх: (x+1,y+1)–(x,y+1)
                    k = Corner(poly, k, c01, x, y + 1, m01, iso, c, dc);
                    k = EdgeV(poly, k, x, y, m00, m01, iso, c, dc, m, true);      // лево: (x,y+1)–(x,y)
                    for (int t = 1; t + 1 < k; t++) Tri(poly[0], poly[t], poly[t + 1]);
                }
            _mesh.Clear();
            _mesh.SetVertices(_mv); _mesh.SetUVs(0, _muv); _mesh.SetColors(_mcol);
            _mesh.SetTriangles(_mt, 0);
            _mesh.RecalculateNormals(); _mesh.RecalculateBounds();
            _mf.sharedMesh = _mesh;
            if (_mc != null && _mt.Count > 0 && Time.time >= _colAt) { _colAt = Time.time + 0.25f; _mc.sharedMesh = null; _mc.sharedMesh = _mesh; }
            else if (_mc != null && _mt.Count > 0) _dirtyMesh = true;
            if (_mr != null)
            {
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                _mr.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", Color.Lerp(Color.white, new Color(1.06f, 1.06f, 1.06f), _flourAmt));
                _mr.SetPropertyBlock(_mpb);
            }
            BuildCircleMesh();
        }

        private int Corner(int[] poly, int k, int ci, int x, int y, float mv, float iso, float c, Color dc)
        {
            if (mv < iso) return k;
            if (_ci[ci] < 0)
            {
                float rim = Mathf.Clamp01((mv - iso) / (0.62f - iso));
                float h = _ch[ci] * Mathf.Sqrt(rim);                        // кромка скруглена «под себя»
                _ci[ci] = AddV(x * c - Half, h + 0.003f, y * c - Half, x + y * 7, dc);
            }
            poly[k] = _ci[ci];
            return k + 1;
        }

        private int EdgeH(int[] poly, int k, int x, int y, float ma, float mb, float iso, float c, Color dc, int m, bool rev = false)
        {
            // ребро (x,y)–(x+1,y); rev — обходим справа налево (значения ma — слева, mb — справа)
            if ((ma >= iso) == (mb >= iso)) return k;
            int id = y * _n + x;
            if (_hi[id] < 0)
            {
                float t = Mathf.Clamp01((iso - ma) / (mb - ma));
                _hi[id] = AddV((x + t) * c - Half, 0.003f, y * c - Half, x * 3 + y, dc);
            }
            poly[k] = _hi[id];
            return k + 1;
        }

        private int EdgeV(int[] poly, int k, int x, int y, float ma, float mb, float iso, float c, Color dc, int m, bool rev = false)
        {
            // ребро (x,y)–(x,y+1); ma — снизу, mb — сверху
            if ((ma >= iso) == (mb >= iso)) return k;
            int id = y * (_n + 1) + x;
            if (_vi[id] < 0)
            {
                float t = Mathf.Clamp01((iso - ma) / (mb - ma));
                _vi[id] = AddV(x * c - Half, 0.003f, (y + t) * c - Half, x + y * 5, dc);
            }
            poly[k] = _vi[id];
            return k + 1;
        }

        private int AddV(float px, float py, float pz, int seed, Color dc)
        {
            // комки: шум в мировых координатах (сдвиг сетки не «прыгает»), бугорки только на верху пласта
            float wx = px + transform.position.x, wz = pz + transform.position.z;
            float big = _lumpy > 0f ? Mathf.PerlinNoise(wx * 1.7f + 13.1f, wz * 1.7f + 7.3f) : 0.5f;
            float small = _lumpy > 0f ? Mathf.PerlinNoise(wx * 6.5f + 3.7f, wz * 6.5f + 21.9f) : 0.5f;
            if (_lumpy > 0f && py > 0.01f) py += (small - 0.45f) * _lumpHeight * S * _lumpy * Mathf.Clamp01(py / (0.06f * S));
            _mv.Add(new Vector3(px, py, pz));
            _muv.Add(new Vector2(px * _texPerMeter, pz * _texPerMeter));
            float hash = Mathf.Abs(Mathf.Sin(seed * 12.9898f + px * 3.1f + pz * 7.7f) * 43758.5453f) % 1f;
            Color cc = dc * (0.95f + 0.08f * hash);
            if (_lumpy > 0f)
            {
                float m = Mathf.Lerp(1f, 0.86f + 0.24f * big, _lumpy) * Mathf.Lerp(1f, 0.94f + 0.1f * small, _lumpy);
                cc = new Color(cc.r * m, cc.g * m, cc.b * Mathf.Lerp(m, m * 0.92f, _lumpy), cc.a);   // тёмные пятна — чуть желтее
            }
            if (hash < _flourAmt * 0.4f) cc = Color.Lerp(cc, Color.white, 0.6f);
            if (DirtOk)
            {
                int dx = Mathf.Clamp(Mathf.FloorToInt((px + Half) / Cell), 0, _n - 1), dz = Mathf.Clamp(Mathf.FloorToInt((pz + Half) / Cell), 0, _n - 1);
                if (_dirt[dz * _n + dx] > 0.5f || (dx > 0 && dz > 0 && _dirt[(dz - 1) * _n + dx - 1] > 0.5f))
                {
                    cc = Color.Lerp(cc, new Color(0.62f, 0.55f, 0.45f), 0.35f);                    // серо-бурый налёт
                    if (hash > 0.8f) cc = Color.Lerp(cc, new Color(0.3f, 0.26f, 0.22f), 0.6f);     // крапинки грязи
                }
            }
            cc.a = 0.55f;
            _mcol.Add(cc);
            return _mv.Count - 1;
        }

        /// <summary>Треугольник лицом ВВЕРХ (верх теста), какая бы ни была очерёдность.</summary>
        private void Tri(int a, int b, int d)
        {
            Vector3 va = _mv[a], vb = _mv[b], vd = _mv[d];
            float cy = (vb.z - va.z) * (vd.x - va.x) - (vb.x - va.x) * (vd.z - va.z);   // = Cross(vb−va, vd−va).y
            if (cy >= 0f) { _mt.Add(a); _mt.Add(b); _mt.Add(d); }
            else { _mt.Add(a); _mt.Add(d); _mt.Add(b); }
        }

        // ───────────────────────── подсказка ─────────────────────────

        public void Measure(out float mean, out float even, out float area)
        {
            float s = 0f, s2 = 0f; int k = 0;
            float thr = 0.012f * S;
            foreach (float h in _h) if (h > thr) { s += h; s2 += h * h; k++; }
            mean = k > 0 ? s / k : 0f;
            float sd = k > 0 ? Mathf.Sqrt(Mathf.Max(0f, s2 / k - mean * mean)) : 0f;
            even = mean > 0f ? Mathf.Clamp01(1f - sd / mean) : 0f;
            area = k * Cell * Cell;
        }

        private string Stats()
        {
            Measure(out float mean, out float even, out float area);
            float max = 0f; foreach (float h in _h) max = Mathf.Max(max, h);
            return $"толщина {mean * 100f:0.0} см (макс {max * 100f:0}), ровность {even * 100f:0}%, площадь {area:0.00} м², качество {Quality01 * 100f:0}%" + (Dirty01 > 0.005f ? $", грязное {Dirty01 * 100f:0}%" : "") + (_stuck > 0 ? $", прилипало {_stuck} раз" : "");
        }

        private void Hint(string text)
        {
            if (!_log || Time.time < _hintAt) return;
            _hintAt = Time.time + 3f;
            Debug.Log($"[Тесто] «{name}»: {text}", this);
        }

        private static GUIStyle _st;

        private static int _hudFrame = -1;
        private static DoughSheet _hudTarget;

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (Time.frameCount != _hudFrame)
            {
                _hudFrame = Time.frameCount; _hudTarget = null;                       // один луч на кадр на все куски
                Camera cam = Camera.main;
                if (cam != null && Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out RaycastHit hit, 14f, ~0, QueryTriggerInteraction.Ignore))
                { _hudTarget = hit.collider.GetComponentInParent<DoughSheet>(); _hudPoint = hit.point; }
            }
            if (_hudTarget != this) return;
            if (_circles.Count > 0 && CircleHud(out string ctext))
            {
                if (_st == null) _st = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = true };
                Rect cr = new Rect(Screen.width * 0.5f - 340f, Screen.height * 0.60f - 50f, 680f, 46f);
                Color co = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(cr, Texture2D.whiteTexture); GUI.color = co;
                GUI.Label(cr, ctext, _st);
            }
            float mean = CoreThickness(out int cells);
            if (cells == 0) return;
            PieceSize(out float L, out float W);
            float tMax = _targetMax * S, pMin = _pieceMin * S, pMax = _pieceMax * S;
            float q = Quality01, dq = Dirty01;
            string qc = q >= 0.85f ? "#9BE37A" : q >= 0.6f ? "#FFD25A" : "#FF6A4A";
            string fl = $"<color={qc}>качество {q * 100f:0}%</color>" + (dq > 0.005f ? $" <color=#FF8A5A>(грязное {dq * 100f:0}% — лежало на голом полу!)</color>" : "") + (_stuck > 0 ? $" · прилипало {_stuck}" : "");
            string text;
            if (mean > tMax * 1.05f)
            {
                string th = mean > _hammerMin * S * 1.6f ? "толсто — кувалдой или въезжай катком" : "ещё катком (крест-накрест)";
                text = $"<b>Тесто</b> · толщина {mean * 100f:0} см ({th}; режется с {tMax * 100f:0} см, тоньше {_rollMin * S * 100f:0} см не раскатать) · {fl}";
            }
            else if (W >= 0.45f * S && _kind == DoughKind.Pelmeni)
                text = $"<b>Пласт</b> {L:0.0}×{W:0.0} м × {mean * 100f:0} см — <color=#9BE37A>раскатан ✔</color> руби кружки ПУСТЫМ ведром: ЛКМ прижать и провернуть мышью · начинка мастерком · плоскогубцы · {fl}";
            else if (W >= 0.45f * S)
                text = $"<b>Пласт</b> {L:0.0}×{W:0.0} м × {mean * 100f:0} см — <color=#9BE37A>раскатан ✔</color> режь шпателем на полосы (лезвие поперёк взгляда, удары по одной линии) · {fl}";
            else if (L > pMax * 1.05f)
            {
                int gc = base.IsServerInitialized ? GnocchiCount() : 0;
                float gl = GnocchiCells * Cell;
                text = gc > 0
                    ? $"<b>Полоса</b> {L:0.00} м × {W * 100f:0} см — нарезано ньокков: <color=#9BE37A>{gc}</color> по {gl * 100f:0} см · {fl}"
                    : $"<b>Полоса</b> {L:0.00} м × {W * 100f:0} см — руби поперёк: ньокки ровно по {gl * 100f:0} см (от края) · {fl}";
            }
            else
            {
                bool ok = L <= pMax * 1.05f && W >= pMin * 0.95f;
                string v = ok ? "<color=#9BE37A>в самый раз ✔</color>" : "<color=#FFD25A>мелко</color>";
                text = $"<b>Ньокк</b> {L * 100f:0}×{W * 100f:0}×{mean * 100f:0} см — {v} (норма {pMin * 100f:0}–{pMax * 100f:0} см) · {fl}";
            }
            if (_st == null) _st = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true, richText = true };
            Rect r = new Rect(Screen.width * 0.5f - 340f, Screen.height * 0.60f, 680f, 44f);
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old;
            GUI.Label(r, text, _st);
        }

        // ───────────────────────── пресеты ─────────────────────────

        public DoughKind Kind => _kind;
        public static string KindTitle(DoughKind k) => k == DoughKind.Gnocchi ? "ньокки (картофельное)" : k == DoughKind.Pelmeni ? "пельмени" : "своё";

        /// <summary>Толщины — игровые, не настоящие: ньокки заметно толще, пельмени — заметно тоньше. Сетка та же (производительность та же).</summary>
        private void ApplyPreset(DoughKind k)
        {
            if (k == DoughKind.Gnocchi)
            {
                _doughColor = new Color(0.97f, 0.84f, 0.48f);       // жёлтое, картофельное
                _lumpy = 1f; _lumpHeight = 0.02f;
                _ballRadius = 0.75f; _sag = 0.25f; _maxH = 1.6f;
                _maxSlope = 1.1f; _flowRate = 0.06f;                  // мягкое, оплывает
                _hammerMin = 0.26f;
                _rollMin = 0.17f; _targetMin = 0.17f; _targetMax = 0.21f; _rideMax = 0.22f;
                _pieceMin = 0.12f; _pieceMax = 0.21f;                                  // 2–3 клетки сетки   // тоньше 17 см каток не берёт — дошёл до упора = готово
                _rollPerPass = 0.3f; _rollContact = 0.05f;
            }
            else if (k == DoughKind.Pelmeni)
            {
                _doughColor = new Color(0.95f, 0.88f, 0.68f);       // как было
                _lumpy = 0.12f; _lumpHeight = 0.004f;
                _ballRadius = 0.5f; _sag = 0.25f; _maxH = 1.0f;       // меньше кома — тонкий пласт не на полплощадки; шаг высоты по сети ~4 мм
                _maxSlope = 1.6f; _flowRate = 0.03f;                  // тугое, держит форму
                _hammerMin = 0.09f;
                _rollMin = 0.04f; _targetMin = 0.04f; _targetMax = 0.055f; _rideMax = 0.06f;
                _pieceMin = 0.3f; _pieceMax = 0.6f;                                    // пельмени режут ведром, не шпателем  // тоньше 4 см не раскатать
                _rollPerPass = 0.35f; _rollContact = 0.03f;
            }
        }

        // ───────────────────────── тест ─────────────────────────

        [ContextMenu("Тест: снова шар (Play, хост)")]
        private void TestBall()
        {
            if (!base.IsServerInitialized) { Debug.LogWarning("[Тесто] только в Play на хосте", this); return; }
            MakeBall(); _vol0 = Volume; Changed();
        }

        [ContextMenu("Тест: сбросить грязь и прилипания")]
        private void TestClean()
        {
            if (!base.IsServerInitialized) return;
            if (_dirt != null) System.Array.Clear(_dirt, 0, _dirt.Length);
            _stuckVol = 0f; _stuck = 0; _dirtyNet = true; _dirtyMesh = true;
        }

        private void Start() { if (_vol0 <= 0f) _vol0 = Volume; }

#if UNITY_EDITOR
        public void EditorSetup(ProductDef flour) => EditorSetup(flour, _kind);

        public void EditorSetupPieceTemplate()
        {
            UnityEditor.Undo.RecordObject(this, "Заготовка куска");
            _startEmpty = true;
            UnityEditor.EditorUtility.SetDirty(this);
        }
        public bool IsPieceTemplate => _startEmpty;

        public void EditorSetPiecePrefab(GameObject prefab)
        {
            UnityEditor.Undo.RecordObject(this, "Префаб куска теста");
            _piecePrefab = prefab;
            UnityEditor.EditorUtility.SetDirty(this);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this)) UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }

        public void EditorSetup(ProductDef flour, DoughKind kind)
        {
            UnityEditor.Undo.RecordObject(this, "Тесто");
            _flour = flour;
            _kind = kind;
            if (kind != DoughKind.Custom) { ApplyPreset(kind); _kindApplied = kind; _presetVer = PresetVersion; }
            UnityEditor.EditorUtility.SetDirty(this);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this)) UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }

        private void OnValidate()
        {
            if (_kind != DoughKind.Custom && (_kind != _kindApplied || _presetVer != PresetVersion))
            {
                ApplyPreset(_kind); _kindApplied = _kind; _presetVer = PresetVersion;
                Debug.Log($"[Тесто] «{name}»: пресет «{KindTitle(_kind)}» — поля выставлены (толщина {_targetMin * 100f:0}–{_targetMax * 100f:0} см, каток не тоньше {_rollMin * 100f:0} см)", this);
            }
            if (Application.isPlaying || UnityEditor.EditorUtility.IsPersistent(this)) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null || Application.isPlaying) return;
                _cw = -1f;
                _mf = GetComponent<MeshFilter>(); _mc = GetComponent<MeshCollider>(); _mr = GetComponent<MeshRenderer>();
                if (_mf == null) return;
                _h = new float[_n * _n];
                _dirt = null;
                if (!_startEmpty) MakeBall();
                BuildMesh();
            };
        }
#endif
    }
}
