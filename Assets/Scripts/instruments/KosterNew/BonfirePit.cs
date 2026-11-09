using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// В пространстве имён НАМЕРЕННО: в проекте ещё живёт старая система.
namespace CatCooks.Food.Tools
{
    /// <summary>
    /// КОСТРИЩЕ под колоннами с кастрюлей. Простая замена старому Firebox.
    ///
    /// ═══ ЦИКЛ ═══
    ///   1. ДОСКИ — кидаешь в кострище (зона приёма). Каждая исчезает и ложится
    ///      в стопку «колодцем»: слои крест-накрест. Видно, сколько топлива.
    ///   2. ГОРЕЛКА — подносишь горящую горелку к стопке. Пара секунд — занялось.
    ///      Убрал раньше — розжиг откатывается. Нужно минимум Min Planks досок.
    ///   3. ГОРИТ — сила огня = сколько досок × сколько воздуха. Воздух даёт
    ///      прыжок на шине рядом (SimpleTireBounce) и постепенно выдыхается.
    ///      Доски прогорают по одной, темнеют; кончились — огонь гаснет.
    ///   4. КАСТРЮЛЯ — стоит на колоннах (точка стыковки Pot Dock). Греется от
    ///      огня плавно, до Max Temperature при полном огне. Тара начинает готовить,
    ///      когда нагреется до своего «Active From C».
    ///
    /// Огонь — CatCooksFireV3: сила по огню, прыжок на шине — порыв с искрами,
    /// кастрюля над огнём — пламя упирается в дно и облизывает бока.
    ///
    /// Сервер считает всё; по сети — несколько байтов. Стопку досок и огонь
    /// каждый игрок рисует сам.
    ///
    /// ═══ ИЕРАРХИЯ ═══
    ///   Bonfire (корень)        NetworkObject + BonfirePit
    ///   ├─ Intake               BoxCollider IsTrigger над ямой + BonfireIntake
    ///   ├─ FirePoint            центр кострища на земле (от него стопка и огонь)
    ///   ├─ Fire                 CatCooksFireV3 (из пакета огня), на FirePoint
    ///   ├─ PotDock              CookwareDock — на верху колонн, где стоит дно кастрюли
    ///   ├─ Column ×4            модели колонн (коллайдеры)
    ///   └─ Tire ×2              шины со SimpleTireBounce, ветер — на кострище
    /// </summary>
    public sealed class BonfirePit : NetworkBehaviour
    {
        public static readonly List<BonfirePit> All = new List<BonfirePit>();

        public enum State : byte { Cold = 0, Catching = 1, Burning = 2 }

        [Header("Топливо")]
        [Tooltip("Предел досок (по сети — байт, до 250). Практически без ограничения.")]
        [Range(1, 250)][SerializeField] private int _maxPlanks = 250;
        [Tooltip("Сколько досок РИСОВАТЬ в стопке (остальные считаются, но не рисуются — производительность)")]
        [Range(6, 100)][SerializeField] private int _maxVisualPlanks = 60;

        [Header("Обугливание досок")]
        [Tooltip("За сколько секунд доска в центре огня чернеет полностью (при полном огне)")]
        [Min(1f)][SerializeField] private float _charSeconds = 45f;
        [Tooltip("Тление: оранжевое свечение углей (если у материала досок есть Emission)")]
        [SerializeField] private Color _emberColor = new Color(1f, 0.35f, 0.05f);
        [Range(0f, 4f)][SerializeField] private float _emberStrength = 1.5f;
        [Tooltip("Секунд горения одной доски при обычном огне (воздух ускоряет)")]
        [Min(1f)][SerializeField] private float _secondsPerPlank = 60f;
        [Tooltip("Столько досок — полный огонь (меньше — огонь слабее)")]
        [Range(1, 40)][SerializeField] private int _planksForFullFire = 6;

        [Header("Розжиг")]
        [Tooltip("Меньше этого досок — не разжечь")]
        [Range(1, 10)][SerializeField] private int _minPlanksToLight = 3;
        [Tooltip("Горелка ближе этого к центру кострища — поджигает, м")]
        [SerializeField] private float _ignitionRadius = 1.2f;
        [Tooltip("Докуда бьёт пламя горелки от носика, м — поджигает и кончик пламени")]
        [SerializeField] private float _torchFlameReach = 1f;
        [Tooltip("Секунд держать горелку до «занялось»")]
        [Min(0.2f)][SerializeField] private float _secondsToCatch = 2f;

        [Header("Воздух (шины)")]
        [Tooltip("Сколько огня без раздува, доля полного")]
        [Range(0f, 1f)][SerializeField] private float _baseFire = 0.15f;
        [Tooltip("Воздух затухает за столько секунд до нуля")]
        [Min(1f)][SerializeField] private float _airFadeSeconds = 25f;
        [Tooltip("Воздух ускоряет прогорание досок: ×(1 + это)")]
        [Range(0f, 3f)][SerializeField] private float _airBurnBoost = 0.4f;
        [Tooltip("Куда целится шина (луч продувки): высота над центром кострища, м. Низко — под кастрюлю, в основание огня.")]
        [Range(0f, 1f)][SerializeField] private float _airPointHeight = 0.08f;

        [Header("Кастрюля")]
        [Tooltip("Точка стыковки на колоннах — что в ней стоит, то и греется")]
        [SerializeField] private CookwareDock _potDock;
        [Tooltip("Температура кастрюли при полном огне, °C")]
        [SerializeField] private float _maxTemperatureC = 240f;
        [SerializeField] private float _roomTemperatureC = 20f;
        [Tooltip("Как быстро кастрюля догоняет температуру огня (1/с). Меньше — дольше греется и остывает.")]
        [Range(0.01f, 1f)][SerializeField] private float _heatRate = 0.08f;
        [Tooltip("Жар под кастрюлей БЕЗ раздува, доля от полного (досок хватает). 0.55 — около 140 °C: вода кипит " +
                 "сама; раздув шинами — сильнее, до Max Temperature. Раньше было как сила огня (0.15 — вода не кипела).")]
        [Range(0.1f, 1f)][SerializeField] private float _potHeatNoAir = 0.55f;

        [Header("Приём и стопка")]
        [Tooltip("Центр кострища на земле. Пусто — корень.")]
        [SerializeField] private Transform _firePoint;
        [Tooltip("ПРЕФАБ ДОСКИ — отсюда берутся меш, материал, настоящий размер и ось длины. " +
                 "Стопка совпадёт с брошенными досками. Если задан — поля Mesh/Material/Size ниже не нужны.")]
        [SerializeField] private GameObject _plankPrefab;
        [Tooltip("Доска, брошенная ближе этого к центру кострища (по горизонтали), ложится в стопку, м. " +
                 "0 — только через Intake.")]
        [SerializeField] private float _catchRadius = 1.2f;
        [Tooltip("Меш одной доски для стопки (если префаб не задан)")]
        [SerializeField] private Mesh _plankMesh;
        [SerializeField] private Material _plankMaterial;
        [Tooltip("Размер доски в стопке, м: длина, толщина, ширина")]
        [SerializeField] private Vector3 _plankSize = new Vector3(0.9f, 0.03f, 0.12f);
        public enum PileLayout : byte
        {
            [InspectorName("Колодец по кругу")] Ring = 0,
            [InspectorName("Крест-накрест")] Crossed = 1,
            [InspectorName("Костёр: шалаш + сруб + поленницы")] Complex = 2,
            [InspectorName("Клеть крест-накрест (массивно, между колоннами)")] Crib = 3,
        }
        [Tooltip("Как складываются доски")]
        [SerializeField] private PileLayout _layout = PileLayout.Crib;

        [Header("Фигура «Клеть» (Crib)")]
        [Tooltip("Ширина клети, доля длины бруса. Подгони, чтобы клеть заполнила место между колоннами.")]
        [Range(0.6f, 3f)][SerializeField] private float _cribSize = 1.6f;
        [Tooltip("Больше этого рядов в слое (толстые брусья — меньше, скрипт сам ограничит)")]
        [Range(1, 8)][SerializeField] private int _cribRows = 4;
        [Tooltip("Насколько клеть сужается к верху на каждый слой (0 — ровный куб)")]
        [Range(0f, 0.25f)][SerializeField] private float _cribPyramid = 0.1f;
        [Tooltip("Зазор между рядами, доля ширины бруса — видно «колодцы» между брусьями")]
        [Range(1f, 2f)][SerializeField] private float _cribGap = 1.25f;

        [Header("Фигура «Костёр» (Complex)")]
        [Tooltip("Досок в шалаше внутри (стоят наклонно, сходятся к центру)")]
        [Range(3, 16)][SerializeField] private int _coreCount = 8;
        [Tooltip("Наклон досок шалаша от земли, градусы")]
        [Range(35f, 85f)][SerializeField] private float _coreTilt = 62f;
        [Tooltip("Радиус основания шалаша, доля длины доски")]
        [Range(0.1f, 0.8f)][SerializeField] private float _coreBase = 0.33f;
        [Tooltip("Колец в срубе вокруг шалаша")]
        [Range(1, 8)][SerializeField] private int _ringLayers = 3;
        [Tooltip("Поленниц между колоннами (0 — без них)")]
        [Range(0, 4)][SerializeField] private int _sideStacks = 4;
        [Tooltip("Досок в одной поленнице")]
        [Range(2, 16)][SerializeField] private int _sideStackPlanks = 6;
        [Tooltip("Расстояние поленниц от центра, доля длины доски (подгони под колонны)")]
        [Range(0.5f, 3f)][SerializeField] private float _sideDistance = 1.55f;
        [Tooltip("Поворот поленниц вокруг центра, градусы (чтобы встали между колоннами)")]
        [SerializeField] private float _sideYaw = 0f;
        [Tooltip("Колодец: досок в одном кольце (6 — шестигранник)")]
        [Range(3, 10)][SerializeField] private int _ringSides = 6;
        [Tooltip("Крест-накрест: досок в одном слое")]
        [Range(2, 5)][SerializeField] private int _planksPerLayer = 3;
        [Tooltip("Размах стопки (полуширина), м — доски в слое расставлены в этих пределах")]
        [SerializeField] private float _pileHalfWidth = 0.4f;
        [SerializeField] private Color _charColor = new Color(0.18f, 0.12f, 0.1f);

        [Header("Старая кастрюля (подставка)")]
        [Tooltip("Подставка FireCauldronStand на колоннах — для старых кастрюль (CookingVessel + PotThermal). " +
                 "Она сама притягивает кастрюлю и берёт тепло отсюда (Degrees Per Second).")]
        [SerializeField] private FireCauldronStand _stand;
        [Tooltip("Сколько градусов в секунду получает старая кастрюля при полном огне")]
        [SerializeField] private float _maxDegreesPerSecond = 14f;

        [Header("Огонь (вид)")]
        [SerializeField] private CatCooksFireV3 _fire;
        [Tooltip("Сила огня на экране при самом слабом горении (чтобы не был еле видным). " +
                 "Выше — ровнее высота; рост высоты даёт в основном пик силы.")]
        [Range(0f, 1f)][SerializeField] private float _visualFloor = 0.85f;
        [Tooltip("ШИРИНА огня: без раздува / на пике силы (множитель размера)")]
        [SerializeField] private Vector2 _widthScale = new Vector2(0.8f, 2.8f);
        [Tooltip("ВЫСОТА огня: до пика / на полной силе. Растёт только после того, как огонь достиг полной ширины.")]
        [SerializeField] private Vector2 _heightScale = new Vector2(0.95f, 1.08f);
        [Tooltip("С какой силы (0..1) огонь уже на полной ширине — дальше растёт только ввысь")]
        [Range(0.2f, 1f)][SerializeField] private float _widthPeakAt = 0.45f;
        [Tooltip("Насколько плавно меняется размер огня")]
        [SerializeField] private float _fireScaleSpeed = 1.5f;

        [Header("Конусы огня между колоннами")]
        [Tooltip("Наибольшее число конусов. Колонны ищутся сами (лучами от центра): в каждый просвет — 1 конус, в широкий — 2. 0 — выкл.")]
        [Range(0, 12)][SerializeField] private int _gapFlamesMax = 8;
        [Tooltip("Свой префаб конуса (любой огонь). Пусто — облегчённая копия основного огня.")]
        [SerializeField] private GameObject _sideFlamePrefab;
        [Tooltip("Ширина конуса у основания, м")]
        [SerializeField] private float _gapFlameSize = 0.5f;
        [Tooltip("Высота конуса на полной силе, м — выше основного огня, чтобы обвивал кастрюлю")]
        [SerializeField] private float _gapFlameHeight = 1.7f;
        [Tooltip("Наклон конусов К кастрюле, градусы — обвивают её бока")]
        [Range(-30f, 45f)][SerializeField] private float _gapLean = 12f;
        [Tooltip("Сила конусов без раздува (0..1); с воздухом растут до полной")]
        [Range(0f, 1f)][SerializeField] private float _gapNoAir = 0.6f;
        [Tooltip("Просвет шире этого (градусы) — два конуса вместо одного")]
        [Range(30f, 180f)][SerializeField] private float _gapTwoFrom = 60f;
        [Tooltip("Колонн не нашлось — столько конусов по кругу")]
        [Range(0, 8)][SerializeField] private int _gapFallback = 4;
        [Tooltip("Искать кастрюлю над огнём и без точки стыковки (стоит на колоннах ближе этого к центру), м")]
        [SerializeField] private float _potSearchRadius = 1.6f;
        [Tooltip("Как сильно основной огонь расходится по дну кастрюли и облизывает бока")]
        [Range(0f, 1.5f)][SerializeField] private float _potSpread = 1.1f;
        [Range(0f, 1f)][SerializeField] private float _potLick = 0.9f;

        [Header("Реакция на воздух (прыжок на шине)")]
        [Tooltip("Вспышка: насколько огонь раздаётся вширь (доля размера)")]
        [Range(0f, 1.5f)][SerializeField] private float _flareWiden = 0.9f;
        [Tooltip("Вспышка: насколько вырастает ввысь")]
        [Range(0f, 1f)][SerializeField] private float _flareTaller = 0.02f;
        [Tooltip("Вспышка: во сколько раз быстрее бежит пламя")]
        [Range(0f, 3f)][SerializeField] private float _flareFlow = 1.5f;
        [Tooltip("Искр при вспышке")]
        [Range(0, 200)][SerializeField] private int _flareSparks = 60;
        [Tooltip("За сколько секунд вспышка оседает")]
        [SerializeField] private float _flareSeconds = 1.6f;
        [Tooltip("Постоянно, пока держится воздух: насколько огонь шире и быстрее")]
        [Range(0f, 1f)][SerializeField] private float _airWiden = 0.2f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        // ───────── сеть ─────────
        private readonly SyncVar<byte> _planks = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _state = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _intensity = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _catch = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _char = new SyncVar<byte>(0);
        private readonly SyncVar<ushort> _potTemp = new SyncVar<ushort>(20);
        private readonly SyncVar<byte> _airNet = new SyncVar<byte>(0);

        // ───────── сервер ─────────
        private float _burnLeft, _air, _catchProgress, _potT = 20f, _burnedTotal;
        private readonly List<BlowTorchItem> _torches = new List<BlowTorchItem>();
        private float _torchScan, _torchLogAt;

        // ───────── вид ─────────
        private readonly List<Transform> _pile = new List<Transform>();
        private MaterialPropertyBlock _mpb;
        private Component _obstacleFor;
        private Vector3 _obsBottom; private float _obsRadius, _obsHeight;
        private float _shownIntensity = -1f;
        private Vector3 _fireBaseScale;
        private bool _fireScaleCaptured;
        private float _fireScaleNow = 1f, _fireHeightNow = 1f;
        private bool _warnedNoPlank;
        private float[] _plankChar = new float[0];
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private float _flare;
        private float _baseFlow = -1f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private readonly List<Transform> _sides = new List<Transform>();
        private readonly List<CatCooksFireV3> _sideFires = new List<CatCooksFireV3>();
        private readonly List<Vector3> _sideBaseScale = new List<Vector3>();
        private float _sideNow, _potRealRadius;
        private Vector3 _potBottomW;
        private bool _hasPot;

        public State FireState => (State)_state.Value;
        public bool IsBurning => FireState == State.Burning;
        public int Planks => _planks.Value;
        public float Intensity01 => _intensity.Value / 255f;
        public float PotTemperatureC => _potTemp.Value;
        public Vector3 AirPoint => FirePoint.position + Vector3.up * _airPointHeight;
        /// <summary>Тепло для старых кастрюль (PotThermal) через FireCauldronStand, °C/с.</summary>
        public float DegreesPerSecond => IsBurning ? _maxDegreesPerSecond * Intensity01 : 0f;
        private Transform FirePoint => _firePoint != null ? _firePoint : transform;

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        // ───────────────────────── ПРИЁМ ─────────────────────────

        /// <summary>Доска (или кусок поддона) упала в кострище. Зовёт BonfireIntake на сервере.</summary>
        [Server]
        public void ServerAccept(BonfireFuel fuel)
        {
            if (fuel == null || fuel.Consumed) return;
            int have = Mathf.Max(1, fuel.Planks);
            int room = _maxPlanks - _planks.Value;
            if (room <= 0)
            {
                if (_log) Debug.Log("[Костёр] полон — доска не лезет", this);
                return;
            }
            int add = Mathf.Min(have, room);
            _planks.Value = (byte)(_planks.Value + add);
            if (_log) Debug.Log($"[Костёр] +{add} доск. · в стопке {_planks.Value}", this);
            if (add < have)
            {
                // Стопка больше, чем влезло: остаток остаётся предметом.
                fuel.ServerSetPlanks(have - add);
                return;
            }
            fuel.Consumed = true;
            NetworkObject nob = fuel.GetComponent<NetworkObject>();
            if (nob != null && nob.IsSpawned) nob.Despawn(); else Destroy(fuel.gameObject);
        }

        /// <summary>Кострище, в которое попадает точка (упавшая доска). Сервер.</summary>
        public static BonfirePit FindCatching(Vector3 point)
        {
            foreach (BonfirePit p in All)
            {
                if (p == null || p._catchRadius <= 0f) continue;
                Vector3 d = point - p.FirePoint.position;
                if (d.y < -0.5f || d.y > 2.5f) continue;
                if (new Vector2(d.x, d.z).magnitude <= p._catchRadius) return p;
            }
            return null;
        }

        /// <summary>Прыжок на шине — порыв воздуха. Зовёт SimpleTireBounce на сервере.</summary>
        [Server]
        public void ServerAddAir(float amount01, Vector3 windDir)
        {
            if (!IsBurning || amount01 <= 0f) return;
            _air = Mathf.Clamp01(_air + amount01);
            RpcGust(windDir, Mathf.Clamp01(amount01));
            if (_log) Debug.Log($"[Костёр] раздули · воздух {Mathf.RoundToInt(_air * 100f)}%", this);
        }

        /// <summary>Точка, куда ставится кастрюля (для тест-клавиш).</summary>
        public CookwareDock PotDock => _potDock;

        /// <summary>ТЕСТ: поджечь без горелки (BonfireTestKeys). Досок должно быть хоть сколько-то. Сервер.</summary>
        [Server]
        public void ServerDebugIgnite()
        {
            if (IsBurning) return;
            _state.Value = (byte)State.Burning;
            _burnLeft = _secondsPerPlank;
            _air = Mathf.Max(_air, 0.3f);
            _catchProgress = 0f;
            if (_log) Debug.Log("[Костёр] подожжён (тест)", this);
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        private void Update()
        {
            if (base.IsServerInitialized) ServerTick(Time.deltaTime);
            UpdateVisual();
        }

        private void ServerTick(float dt)
        {
            State st = FireState;

            if (st != State.Burning)
            {
                bool torch = TorchNear();
                bool enough = _planks.Value >= _minPlanksToLight;
                if (torch && enough)
                {
                    if (st == State.Cold) _state.Value = (byte)State.Catching;
                    _catchProgress += dt / _secondsToCatch;
                    if (_log && Time.time >= _torchLogAt)
                    {
                        _torchLogAt = Time.time + 0.5f;
                        Debug.Log($"[Костёр] разгорается… {Mathf.RoundToInt(_catchProgress * 100f)}%", this);
                    }
                    if (_catchProgress >= 1f)
                    {
                        _state.Value = (byte)State.Burning;
                        _burnLeft = _secondsPerPlank;
                        _air = Mathf.Max(_air, 0.3f);
                        _catchProgress = 0f;
                        if (_log) Debug.Log("[Костёр] занялся!", this);
                    }
                }
                else
                {
                    if (torch && !enough && _log && Time.time >= _torchLogAt)
                    {
                        _torchLogAt = Time.time + 1f;
                        Debug.Log($"[Костёр] мало досок для розжига: {_planks.Value}/{_minPlanksToLight} (Min Planks To Light)", this);
                    }
                    _catchProgress = Mathf.Max(0f, _catchProgress - dt * 0.5f);
                    if (_catchProgress <= 0f && st == State.Catching) _state.Value = (byte)State.Cold;
                }
            }
            else
            {
                _air = Mathf.Max(0f, _air - dt / _airFadeSeconds);
                float burn = dt * (1f + _air * _airBurnBoost);
                _burnLeft -= burn;
                _burnedTotal += burn;
                if (_burnLeft <= 0f)
                {
                    if (_planks.Value > 0) _planks.Value = (byte)(_planks.Value - 1);
                    if (_planks.Value == 0)
                    {
                        _state.Value = (byte)State.Cold;
                        _air = 0f;
                        _burnedTotal = 0f;
                        if (_log) Debug.Log("[Костёр] прогорел", this);
                    }
                    else _burnLeft += _secondsPerPlank;
                }
            }

            // Сила огня: доски × воздух.
            float fuel01 = Mathf.Pow(Mathf.Clamp01(_planks.Value / (float)_planksForFullFire), 0.7f);
            float intensity = IsBurning ? fuel01 * (_baseFire + (1f - _baseFire) * _air) : 0f;
            SetByte(_intensity, intensity);
            SetByte(_airNet, _air);
            SetByte(_catch, _catchProgress);
            // Обугленность стопки: сколько прогорело относительно текущих досок.
            SetByte(_char, IsBurning ? Mathf.Clamp01(0.25f + _burnedTotal / (_secondsPerPlank * 3f)) : 0f);

            // Кастрюля на колоннах.
            float heat01 = IsBurning ? fuel01 * Mathf.Lerp(_potHeatNoAir, 1f, _air) : 0f;
            float target = _roomTemperatureC + (_maxTemperatureC - _roomTemperatureC) * heat01;
            _potT = Mathf.Lerp(_potT, target, 1f - Mathf.Exp(-_heatRate * dt));
            ushort pt = (ushort)Mathf.RoundToInt(_potT);
            if (Mathf.Abs(pt - _potTemp.Value) >= 2) _potTemp.Value = pt;
            Cookware pot = HeatedPot();
            if (_log && IsBurning && Time.time >= _potLogAt)
            {
                _potLogAt = Time.time + 5f;
                Debug.Log(pot != null ? $"[Костёр] греет «{pot.name}»: огонь {_potT:0} °C"
                                      : "[Костёр] горит, но над огнём нет кастрюли (Pot Dock пуст, рядом с центром тары нет)", this);
            }
            if (pot != null)
            {
                // Кастрюля со своей температурой (PotThermal): даём ей тепло — дальше
                // она сама кипит, копит давление, может рвануть. Иначе — температура напрямую.
                PotThermal thermal = pot.GetComponent<PotThermal>();
                if (thermal != null) thermal.ServerAddHeat(DegreesPerSecond, dt);
                else pot.ServerSetExternalTemperature(_potT);
            }
        }

        private static void SetByte(SyncVar<byte> v, float x01)
        {
            byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(x01) * 255f);
            if (Mathf.Abs(b - v.Value) >= 3 || (b == 0 && v.Value != 0) || (b == 255 && v.Value != 255)) v.Value = b;
        }

        private bool TorchNear()
        {
            if (Time.time >= _torchScan)
            {
                _torchScan = Time.time + 2f;
                _torches.Clear();
                _torches.AddRange(FindObjectsByType<BlowTorchItem>(FindObjectsSortMode.None));
            }
            Vector3 c = FirePoint.position + Vector3.up * PileHeight * 0.5f;
            float r2 = _ignitionRadius * _ignitionRadius;
            bool anyBurning = false;
            foreach (BlowTorchItem t in _torches)
            {
                if (t == null || !t.IsBurning) continue;
                anyBurning = true;
                Vector3 n = t.Nozzle.position;
                Vector3 tip = n + t.Nozzle.forward * _torchFlameReach;
                // Ближайшая к кострищу точка струи пламени (носик … кончик).
                Vector3 seg = tip - n;
                float k = Mathf.Clamp01(Vector3.Dot(c - n, seg) / Mathf.Max(1e-5f, seg.sqrMagnitude));
                Vector3 closest = n + seg * k;
                float d2 = (closest - c).sqrMagnitude;
                if (d2 <= r2) return true;
                if (_log && Time.time >= _torchLogAt)
                {
                    _torchLogAt = Time.time + 1f;
                    Debug.Log($"[Костёр] горелка горит, но далеко: {Mathf.Sqrt(d2):0.0} м (надо ≤ {_ignitionRadius:0.0} м — Ignition Radius)", this);
                }
            }
            if (!anyBurning && _log && _planks.Value > 0 && Time.time >= _torchLogAt && _torches.Count == 0)
            {
                _torchLogAt = Time.time + 5f;
                Debug.Log("[Костёр] горелок в сцене не найдено (BlowTorchItem)", this);
            }
            return false;
        }

        // ───────────────────────── ВИД (у всех) ─────────────────────────

        [ObserversRpc]
        private void RpcGust(Vector3 dir, float strength)
        {
            if (_fire == null) return;
            // Вспышка: раздаться вширь, взметнуться, сноп искр, дымок, наклон от шины.
            _flare = Mathf.Min(1.5f, _flare + 0.6f + strength);
            Vector3 d = dir.sqrMagnitude > 1e-4f ? dir : Vector3.up;
            _fire.Blow(d, 1f + strength);
            _fire.BurstSparks(Mathf.RoundToInt(_flareSparks * (0.6f + strength)));
            _fire.PuffSmoke(6);
            foreach (CatCooksFireV3 f in _sideFires) if (f != null && f.isActiveAndEnabled) f.Blow(d, 0.6f + strength);
        }

        // ───────────────────────── какая кастрюля над огнём ─────────────────────────

        private bool _dockSearched;
        private float _potLogAt;
        private Cookware _lastHeated;

        /// <summary>
        /// Кастрюля над огнём: из точки стыковки, а если её нет или она пуста —
        /// любая тара прямо над центром кострища (поставили на колонны руками).
        /// </summary>
        private Cookware HeatedPot()
        {
            if (_potDock == null && !_dockSearched)
            {
                _dockSearched = true;
                _potDock = GetComponentInChildren<CookwareDock>();
                if (_potDock == null)
                {
                    float best = 2f;
                    foreach (CookwareDock d in FindObjectsByType<CookwareDock>(FindObjectsSortMode.None))
                    {
                        Vector3 dd = d.transform.position - FirePoint.position;
                        float h = new Vector2(dd.x, dd.z).magnitude;
                        if (h < best) { best = h; _potDock = d; }
                    }
                }
                if (_log) Debug.Log(_potDock != null ? $"[Костёр] точка кастрюли: «{_potDock.name}»"
                                                     : "[Костёр] точки кастрюли (Pot Dock) нет — греется тара, стоящая над центром", this);
            }

            Cookware pot = _potDock != null ? _potDock.Current : null;
            if (pot != null && !BakeOven.Holds(pot)) return pot;
            pot = null;

            // Поиск без точки — не чаще двух раз в секунду (он перебирает всю тару).
            if (Time.time < _potSearchAt) return _potFound;
            _potSearchAt = Time.time + 0.5f;

            // Точка пуста (или не принимает эту тару) — ищем тару над центром кострища
            // или у самой точки стыковки. Меряем по ЦЕНТРУ коллайдеров/мешей, а не по
            // опоре объекта: у модели опора бывает где угодно.
            float bd = float.MaxValue;
            foreach (Cookware cw in Cookware.All)
            {
                if (cw == null || BakeOven.Holds(cw)) continue;     // стоит в духовке — греет духовка, не костёр
                Vector3 p = PotCentre(cw);
                float h = Near(p, FirePoint.position);
                if (_potDock != null) h = Mathf.Min(h, Near(p, _potDock.transform.position));
                if (h < bd) { bd = h; pot = cw; }
            }
            if (pot == null || bd > _potSearchRadius) pot = null;

            if (pot != null && _potDock != null && !_potDock.Accepts(pot) && _log && Time.time >= _acceptWarnAt)
            {
                _acceptWarnAt = Time.time + 10f;
                Debug.LogWarning($"[Костёр] точка «{_potDock.name}» НЕ ПРИНИМАЕТ «{pot.name}» (список Accept у Cookware Dock) — " +
                                 "греем всё равно, но магнит её не ставит. Добавь её Def в Accept или очисти список.", this);
            }
            _potFound = pot;
            return pot;
        }

        private float _acceptWarnAt, _potSearchAt;
        private Cookware _potFound;

        /// <summary>Расстояние по горизонтали, если тара над точкой (от −0.5 до +3.5 м по высоте); иначе бесконечность.</summary>
        private static float Near(Vector3 pot, Vector3 point)
        {
            Vector3 d = pot - point;
            if (d.y < -0.5f || d.y > 3.5f) return float.MaxValue;
            return new Vector2(d.x, d.z).magnitude;
        }

        private static Vector3 PotCentre(Cookware cw)
        {
            bool any = false; Bounds b = default(Bounds);
            foreach (Collider c in cw.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || !c.enabled) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            return any ? b.center : cw.transform.position;
        }

        private void UpdateVisual()
        {
            SyncPile();

            float intensity = Intensity01;
            float air = _airNet.Value / 255f;
            _flare = Mathf.MoveTowards(_flare, 0f, Time.deltaTime / Mathf.Max(0.1f, _flareSeconds));
            // Фазы силы: сначала ширина (до Width Peak At), потом — немного высота.
            float widthPhase = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(intensity / Mathf.Max(0.05f, _widthPeakAt)));
            float heightPhase = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((intensity - _widthPeakAt) / Mathf.Max(0.05f, 1f - _widthPeakAt)));
            float shown = IsBurning ? Mathf.Clamp01(Mathf.Lerp(_visualFloor, 1f, 0.3f * widthPhase + 0.7f * heightPhase) + _flare * 0.15f) : 0f;
            if (_fire != null && Mathf.Abs(shown - _shownIntensity) > 0.01f)
            {
                _shownIntensity = shown;
                _fire.SetIntensity(shown, 0.5f);
            }
            // Раздули — огонь растёт вширь и ввысь (масштаб корня огня: без пересборки, дёшево).
            if (_fire != null)
            {
                if (!_fireScaleCaptured) { _fireBaseScale = _fire.transform.localScale; _fireScaleCaptured = true; }
                // Ширина и высота — отдельно: вширь по силе, ввысь только на пике.
                float wantW = IsBurning ? Mathf.Lerp(_widthScale.x, _widthScale.y, widthPhase) : _widthScale.x;
                float wantH = IsBurning ? Mathf.Lerp(_heightScale.x, _heightScale.y, heightPhase) : _heightScale.x;
                _fireScaleNow = Mathf.MoveTowards(_fireScaleNow, wantW, Time.deltaTime * _fireScaleSpeed);
                _fireHeightNow = Mathf.MoveTowards(_fireHeightNow, wantH, Time.deltaTime * _fireScaleSpeed * 0.6f);
                // Вспышка от прыжка — в основном вширь.
                float on = IsBurning ? 1f : 0f;
                float wide = _fireScaleNow * (1f + (_flareWiden * _flare + _airWiden * air) * on);
                float tall = _fireHeightNow * (1f + _flareTaller * _flare * on);
                _fire.transform.localScale = new Vector3(_fireBaseScale.x * wide, _fireBaseScale.y * tall, _fireBaseScale.z * wide);

                // Пламя «бежит» быстрее при воздухе и во вспышке.
                if (_baseFlow < 0f) _baseFlow = _fire.flowSpeed;
                _fire.flowSpeed = Mathf.Clamp(_baseFlow * (1f + _flareFlow * _flare + air * 0.8f), 0.1f, 3f);
            }

            // Кастрюля над огнём → пламя огибает её.
            if (_fire != null)
            {
                Component pot = HeatedPot();
                if (pot == null && _stand != null && _stand.Vessel != null) pot = _stand.Vessel;
                if (pot != _obstacleFor) { _obstacleFor = pot; MeasurePot(pot); }
                if (pot != null)
                {
                    _fire.SetObstacle(pot.transform.position + _obsBottom, _obsRadius, _obsHeight);
                    _fire.obstacleSpread = _potSpread;
                    _fire.obstacleLick = _potLick;
                }
                else _fire.ClearObstacle();
                _hasPot = pot != null;
                if (_hasPot) _potBottomW = pot.transform.position + _obsBottom;
            }
            UpdateSideFlames(intensity, air);
        }

        // ───────────────────────── конусы огня между колоннами ─────────────────────────

        private readonly List<float> _gapAngles = new List<float>();
        private float _gapRadius = 0.6f, _gapScanAt = -1f;
        private Component _gapScanFor;
        private readonly RaycastHit[] _gapHits = new RaycastHit[8];

        /// <summary>
        /// Найти колонны: лучи от центра во все стороны на высоте между стопкой и дном
        /// кастрюли. Где луч упёрся — колонна, где прошёл — просвет. В середину каждого
        /// просвета — конус (в широкий — два).
        /// </summary>
        private void ScanGaps()
        {
            _gapAngles.Clear();
            Vector3 c = FirePoint.position;
            if (_hasPot) { c.x = _potBottomW.x; c.z = _potBottomW.z; }
            float y = _hasPot ? Mathf.Lerp(FirePoint.position.y, _potBottomW.y, 0.5f) : FirePoint.position.y + 0.4f;
            Vector3 o = new Vector3(c.x, y, c.z);
            float reach = (_hasPot ? _potRealRadius : 0.6f) + 1.2f;

            const int N = 72;
            bool[] blocked = new bool[N];
            float distSum = 0f; int distN = 0, nBlocked = 0;
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                int n = Physics.RaycastNonAlloc(o, dir, _gapHits, reach, ~0, QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue;
                for (int k = 0; k < n; k++)
                {
                    Collider col = _gapHits[k].collider;
                    if (col == null || col.GetComponentInParent<Cookware>() != null) continue;
                    if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) continue;
                    if (col is CharacterController) continue;
                    if (_gapHits[k].distance < 0.15f) continue;
                    nearest = Mathf.Min(nearest, _gapHits[k].distance);
                }
                if (nearest < float.MaxValue) { blocked[i] = true; nBlocked++; distSum += nearest; distN++; }
            }

            _gapRadius = distN > 0 ? distSum / distN : (_hasPot ? _potRealRadius + 0.1f : 0.6f);

            if (nBlocked == 0 || nBlocked == N)
            {
                for (int i = 0; i < _gapFallback; i++) _gapAngles.Add((i + 0.5f) / _gapFallback * 360f);
            }
            else
            {
                // Начинаем с занятого луча, чтобы просвет не разрезало по нулю.
                int start = 0; while (!blocked[start]) start++;
                int run = 0, runStart = 0;
                for (int k = 1; k <= N; k++)
                {
                    int i = (start + k) % N;
                    if (!blocked[i]) { if (run == 0) runStart = start + k; run++; }
                    if ((blocked[i] || k == N) && run > 0)
                    {
                        float width = run * 360f / N;
                        if (width >= 15f)
                        {
                            float a0 = runStart * 360f / N, a1 = (runStart + run - 1) * 360f / N;
                            if (width >= _gapTwoFrom) { _gapAngles.Add(Mathf.Lerp(a0, a1, 0.3f)); _gapAngles.Add(Mathf.Lerp(a0, a1, 0.7f)); }
                            else _gapAngles.Add((a0 + a1) * 0.5f);
                        }
                        run = 0;
                    }
                }
            }
            while (_gapAngles.Count > _gapFlamesMax) _gapAngles.RemoveAt(_gapAngles.Count - 1);
            if (_log) Debug.Log($"[Костёр] конусы огня: {_gapAngles.Count} (колонн найдено по {nBlocked}/{N} лучам, радиус {_gapRadius:0.00} м)", this);
        }

        private void BuildSideFlames(int count)
        {
            if (_sides.Count == count) return;
            foreach (Transform t in _sides) if (t != null) Destroy(t.gameObject);
            _sides.Clear(); _sideFires.Clear(); _sideBaseScale.Clear();
            if (count <= 0 || (_fire == null && _sideFlamePrefab == null)) return;

            for (int i = 0; i < count; i++)
            {
                GameObject go;
                if (_sideFlamePrefab != null) go = Instantiate(_sideFlamePrefab, transform);
                else
                {
                    // Облегчённая копия основного огня: мало источников, без света и углей.
                    go = new GameObject("GapFlame");
                    go.SetActive(false);
                    go.transform.SetParent(transform, false);
                    CatCooksFireV3 f = go.AddComponent<CatCooksFireV3>();
                    f.bodyShader = _fire.bodyShader; f.particlesShader = _fire.particlesShader; f.heatShader = _fire.heatShader;
                    f.outer = _fire.outer; f.middle = _fire.middle; f.inner = _fire.inner; f.core = _fire.core;
                    f.emission = _fire.emission; f.colourSoftness = _fire.colourSoftness;
                    f.erosion = _fire.erosion; f.sway = _fire.sway; f.flowSpeed = _fire.flowSpeed;
                    f.smokeColour = _fire.smokeColour; f.smokeOpacity = _fire.smokeOpacity * 0.5f;
                    f.layout = CatCooksFireV3.Layout.Ellipse;
                    f.footprint = new Vector2(_gapFlameSize, _gapFlameSize);
                    f.sourceSpacing = Mathf.Max(0.12f, _gapFlameSize * 0.5f);
                    f.maxSources = 5;
                    f.automaticHeight = false;
                    f.flameHeight = Mathf.Max(0.15f, _gapFlameHeight);
                    f.maxParticles = 48;
                    f.sparksPerSource = _fire.sparksPerSource * 0.5f;
                    f.smokePerSource = 0f;
                    f.glowingCoals = false;
                    f.createLight = false;
                    f.enableHeatHaze = false;
                    f.flameDetail = CatCooksFireV3.Detail.Medium;
                    f.particleDetail = CatCooksFireV3.Detail.Low;
                    f.intensity = 0f;
                    f.seed = 1000 + i * 37;
                }
                go.name = "GapFlame " + i;
                _sides.Add(go.transform);
                _sideFires.Add(go.GetComponentInChildren<CatCooksFireV3>(true));
                _sideBaseScale.Add(go.transform.localScale);
                go.SetActive(false);
            }
        }

        private void UpdateSideFlames(float intensity, float air)
        {
            // Пересканировать колонны: сменилась кастрюля или раз в 10 с (передвинули).
            Component potNow = _hasPot ? _obstacleFor : null;
            if (_gapScanAt < 0f || potNow != _gapScanFor || Time.time >= _gapScanAt)
            {
                _gapScanFor = potNow;
                _gapScanAt = Time.time + 10f;
                if (_gapFlamesMax > 0) ScanGaps(); else _gapAngles.Clear();
            }
            BuildSideFlames(_gapAngles.Count);
            if (_sides.Count == 0) return;

            float want = IsBurning
                ? Mathf.Clamp01(Mathf.Lerp(_gapNoAir, 1f, Mathf.Clamp01(air * 1.4f)) * Mathf.Clamp01(intensity * 5f) + _flare * 0.3f)
                : 0f;
            _sideNow = Mathf.MoveTowards(_sideNow, want, Time.deltaTime * 1.2f);

            Vector3 c = FirePoint.position;
            if (_hasPot) { c.x = _potBottomW.x; c.z = _potBottomW.z; }
            float t = Time.time;

            for (int i = 0; i < _sides.Count && i < _gapAngles.Count; i++)
            {
                Transform tr = _sides[i];
                if (tr == null) continue;
                float flick = 0.85f + 0.15f * Mathf.Sin(t * (2.1f + i * 0.37f) + i * 1.7f);
                float s = _sideNow * flick;
                bool on = s > 0.03f;
                if (tr.gameObject.activeSelf != on) tr.gameObject.SetActive(on);
                if (!on) continue;

                float ang = _gapAngles[i] * Mathf.Deg2Rad;
                Vector3 outDir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                Vector3 pos = c + outDir * _gapRadius;
                pos.y = FirePoint.position.y;
                // Наклон к кастрюле: верх конуса тянется внутрь, обвивая бок.
                Quaternion rot = Quaternion.AngleAxis(_gapLean, Vector3.Cross(outDir, Vector3.up)) *
                                 Quaternion.LookRotation(outDir, Vector3.up);
                tr.SetPositionAndRotation(pos, rot);
                Vector3 b = _sideBaseScale[i];
                tr.localScale = new Vector3(b.x * (0.7f + 0.3f * s), b.y * Mathf.Max(0.05f, s), b.z * (0.7f + 0.3f * s));

                CatCooksFireV3 f = _sideFires[i];
                if (f != null)
                {
                    f.SetIntensity(Mathf.Clamp01(0.5f + s * 0.5f), 0.3f);
                    f.flowSpeed = _fire != null ? _fire.flowSpeed : 1f;
                }
            }
        }

        /// <summary>
        /// Замерить кастрюлю: дно, радиус, высота (относительно её опоры).
        /// По КОЛЛАЙДЕРАМ корпуса, а не по мешам: у кастрюли есть градусник над ней
        /// и авто-объекты — по ним размер выходил огромным, и пламя «улетало» за ложный край.
        /// </summary>
        private void MeasurePot(Component pot)
        {
            if (pot == null) return;
            Bounds b = default;
            bool any = false;
            foreach (Collider c in pot.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || !c.enabled) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            if (!any)
            {
                foreach (Renderer r in pot.GetComponentsInChildren<Renderer>())
                {
                    if ((r.gameObject.hideFlags & HideFlags.DontSave) != 0 || r.name.Contains("(auto)")) continue;
                    if (r.GetComponentInParent<PotThermometer3D>() != null && r.transform != pot.transform) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
            }
            if (!any) { _obsRadius = 0.3f; _obsHeight = 0.3f; _obsBottom = Vector3.zero; return; }
            _obsRadius = Mathf.Max(b.extents.x, b.extents.z) * 0.95f;
            _potRealRadius = Mathf.Max(b.extents.x, b.extents.z);
            _obsHeight = b.size.y;
            _obsBottom = new Vector3(b.center.x, b.min.y, b.center.z) - pot.transform.position;

            // Не больше огня: иначе всё пламя выталкивается за край.
            if (_fire != null)
            {
                Vector2 fp = _fire.footprint;
                float fireHalf = Mathf.Max(fp.x, fp.y) * 0.5f * Mathf.Abs(_fire.transform.lossyScale.x);
                if (fireHalf > 0.01f) _obsRadius = Mathf.Min(_obsRadius, fireHalf * 1.1f);
            }
            if (_log) Debug.Log($"[Костёр] над огнём «{pot.name}»: радиус {_obsRadius:0.00} м, высота {_obsHeight:0.00} м", this);
        }

        // ── доска стопки: меш, материал, размеры (мир) и поворот меша «длина → X, толщина → Y» ──
        private bool _plankReady;
        private Mesh _pMesh;
        private Material[] _pMats;
        private Vector3 _pSize;          // мир: длина, толщина, ширина
        private Vector3 _pScaleMesh;     // мировой масштаб меша (как в префабе)
        private Quaternion _pRot;        // меш → оси стопки
        private Vector3 _pCenter;        // центр меша (оси меша)

        private int PerLayer => _layout == PileLayout.Crossed ? _planksPerLayer : _ringSides;
        private float PileHeight
        {
            get
            {
                if (!_plankReady) return 0f;
                int v = Mathf.Min(_planks.Value, _maxVisualPlanks);
                if (_layout == PileLayout.Crib) { BuildCribSlots(); return v > 0 ? _pSize.y * (_crib[Mathf.Min(v, _crib.Count) - 1].Layer + 1) : 0f; }
                return _pSize.y * Mathf.Ceil(v / (float)PerLayer);
            }
        }

        private void PreparePlank()
        {
            if (_plankReady) return;
            if (_plankPrefab == null && _plankMesh == null)
            {
                if (!_warnedNoPlank && _planks.Value > 0)
                {
                    _warnedNoPlank = true;
                    Debug.LogWarning($"[Костёр] «{name}»: не задан Plank Prefab — доски принимаются, но стопку не видно", this);
                }
                return;
            }
            MeshFilter mf = _plankPrefab != null ? _plankPrefab.GetComponentInChildren<MeshFilter>(true) : null;
            if (mf != null && mf.sharedMesh != null)
            {
                _pMesh = mf.sharedMesh;
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                _pMats = mr != null ? mr.sharedMaterials : new[] { _plankMaterial };
                _pScaleMesh = mf.transform.lossyScale;             // масштаб меша в префабе = в мире
            }
            else if (_plankMesh != null)
            {
                _pMesh = _plankMesh;
                _pMats = new[] { _plankMaterial };
                Vector3 ms = _plankMesh.bounds.size;
                // Ручной размер: подогнать меш под Plank Size (длина — самая длинная ось).
                float[] m = { ms.x, ms.y, ms.z };
                float[] want = { _plankSize.x, _plankSize.y, _plankSize.z };
                System.Array.Sort(m); System.Array.Sort(want);
                float k = want[2] / Mathf.Max(1e-5f, m[2]);
                _pScaleMesh = Vector3.one * k;
            }
            else return;

            Bounds b = _pMesh.bounds;
            Vector3 d = Vector3.Scale(b.size, _pScaleMesh);
            d = new Vector3(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z));
            int L = d.x >= d.y && d.x >= d.z ? 0 : (d.y >= d.z ? 1 : 2);
            int T = d.x <= d.y && d.x <= d.z ? 0 : (d.y <= d.z ? 1 : 2);
            if (T == L) T = (L + 1) % 3;
            int M = 3 - L - T;
            _pSize = new Vector3(d[L], d[T], d[M]);
            _pCenter = Vector3.Scale(b.center, _pScaleMesh);

            // Поворот: ось L меша → X стопки, T → Y, M → ±Z (правая тройка).
            Vector3[] img = new Vector3[3];
            img[L] = Vector3.right; img[T] = Vector3.up; img[M] = Vector3.forward;
            if (Vector3.Dot(Vector3.Cross(img[0], img[1]), img[2]) < 0f) img[M] = -img[M];
            _pRot = Quaternion.LookRotation(img[2], img[1]);
            _plankReady = true;
        }

        /// <summary>Стопка досок колодцем: слои крест-накрест, лишние — убрать сверху.</summary>
        private void SyncPile()
        {
            PreparePlank();
            int want = _plankReady ? Mathf.Min(_planks.Value, _maxVisualPlanks) : 0;
            while (_pile.Count > want)
            {
                Transform t = _pile[_pile.Count - 1];
                if (t != null) Destroy(t.gameObject);
                _pile.RemoveAt(_pile.Count - 1);
            }
            while (_pile.Count < want)
            {
                int i = _pile.Count;
                GameObject go = new GameObject("Plank (auto)") { hideFlags = HideFlags.DontSave, layer = gameObject.layer };
                go.transform.SetParent(FirePoint, false);
                go.AddComponent<MeshFilter>().sharedMesh = _pMesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = _pMats;
                PlacePlank(go.transform, i);
                _pile.Add(go.transform);
            }

            // ── обугливание: каждая доска сама; в огне быстрее, у колонн медленнее ──
            _mpb ??= new MaterialPropertyBlock();
            if (_plankChar.Length < _maxVisualPlanks) System.Array.Resize(ref _plankChar, _maxVisualPlanks);
            float dt = Time.deltaTime;
            float heat = IsBurning ? Mathf.Lerp(0.4f, 1f, Intensity01) : 0f;
            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * 3.1f);
            for (int i = 0; i < _pile.Count; i++)
            {
                if (_pile[i] == null) continue;
                _plankChar[i] = Mathf.Min(1f, _plankChar[i] + dt * heat * CharFactor(i) / _charSeconds);
                float c = _plankChar[i];
                Color col = Color.Lerp(Color.white, _charColor, c);
                _mpb.SetColor(BaseColorId, col);
                _mpb.SetColor(ColorId, col);
                // Тлеют угли: чёрные доски в горящем костре светятся оранжевым.
                float ember = IsBurning ? Mathf.Clamp01((c - 0.4f) / 0.6f) * _emberStrength * pulse * (0.5f + 0.5f * Intensity01) : 0f;
                _mpb.SetColor(EmissionId, _emberColor * ember);
                _pile[i].GetComponent<MeshRenderer>().SetPropertyBlock(_mpb);
            }
            // Прогоревшие места сбрасываются: новая доска на этом месте — светлая.
            for (int i = _pile.Count; i < _plankChar.Length; i++) _plankChar[i] = 0f;
        }

        private void PlacePlank(Transform t, int i)
        {
            if (_layout == PileLayout.Ring) { PlaceRing(t, i); return; }
            if (_layout == PileLayout.Complex) { PlaceComplex(t, i); return; }
            if (_layout == PileLayout.Crib) { PlaceCrib(t, i); return; }
            int layer = i / _planksPerLayer, idx = i % _planksPerLayer;
            // Ширина колодца = длина доски: слои крест-накрест складываются в квадрат.
            float half = Mathf.Max(_pileHalfWidth, _pSize.x * 0.5f);
            float spacing = _planksPerLayer > 1 ? (half * 2f - _pSize.z) / (_planksPerLayer - 1) : 0f;
            float across = -half + _pSize.z * 0.5f + idx * spacing;
            System.Random rnd = new System.Random(i * 7919 + 17);
            float jitterYaw = ((float)rnd.NextDouble() - 0.5f) * 8f;
            float jitterPos = ((float)rnd.NextDouble() - 0.5f) * _pSize.z * 0.3f;
            bool rotated = (layer & 1) == 1;

            // Мир (оси кострища): центр доски и её поворот.
            Vector3 centre = rotated ? new Vector3(across, 0f, jitterPos) : new Vector3(jitterPos, 0f, across);
            centre.y = _pSize.y * (layer + 0.5f);
            Quaternion pileRot = Quaternion.Euler(((float)rnd.NextDouble() - 0.5f) * 3f, (rotated ? 90f : 0f) + jitterYaw, 0f);
            Quaternion meshRot = pileRot * _pRot;

            // Центр меша не обязан быть в опоре модели — сдвинуть, чтобы центр доски встал куда надо.
            Vector3 offset = meshRot * _pCenter;
            Vector3 worldPos = FirePoint.TransformPoint(Vector3.zero) + FirePoint.rotation * (centre - offset);
            t.position = worldPos;
            t.rotation = FirePoint.rotation * meshRot;
            Vector3 ls = FirePoint.lossyScale;
            t.localScale = new Vector3(_pScaleMesh.x / Mathf.Max(1e-5f, ls.x), _pScaleMesh.y / Mathf.Max(1e-5f, ls.y),
                                       _pScaleMesh.z / Mathf.Max(1e-5f, ls.z));
        }

        // ── клеть: слоты считаются один раз под размеры бруса ──
        private struct CribSlot { public int Layer, Row, Rows, Seg, Segs; public float Span; }
        private readonly List<CribSlot> _crib = new List<CribSlot>();
        private Vector3 _cribFor;

        private void BuildCribSlots()
        {
            if (_crib.Count >= _maxVisualPlanks && _cribFor == _pSize) return;
            _crib.Clear();
            _cribFor = _pSize;
            float L = _pSize.x, W = _pSize.z;
            for (int layer = 0; _crib.Count < _maxVisualPlanks && layer < 200; layer++)
            {
                float span = L * _cribSize * Mathf.Max(0.45f, 1f - _cribPyramid * layer);
                int rows = Mathf.Clamp(Mathf.FloorToInt(span / Mathf.Max(1e-4f, W * _cribGap)), 1, _cribRows);
                int segs = L >= span * 0.95f ? 1 : 2;
                for (int row = 0; row < rows; row++)
                for (int sg = 0; sg < segs; sg++)
                    _crib.Add(new CribSlot { Layer = layer, Row = row, Rows = rows, Seg = sg, Segs = segs, Span = span });
            }
        }

        /// <summary>
        /// Клеть крест-накрест: слой — несколько рядов брусьев, ряд — один или два бруса
        /// встык (стыки соседних рядов разнесены, как в кладке); слои через один повёрнуты
        /// на 90°, клеть сужается кверху. Заполняет место между колоннами.
        /// </summary>
        private void PlaceCrib(Transform t, int i)
        {
            BuildCribSlots();
            if (i >= _crib.Count) i = _crib.Count - 1;
            CribSlot c = _crib[i];
            System.Random rnd = new System.Random(i * 7919 + 17);
            float L = _pSize.x, W = _pSize.z;
            float across = -c.Span * 0.5f + W * 0.5f + c.Row * (c.Rows > 1 ? (c.Span - W) / (c.Rows - 1) : 0f);
            float stagger = c.Segs == 2 ? ((c.Row & 1) == 1 ? L * 0.22f : -L * 0.22f) : 0f;
            float along = c.Segs == 2 ? (c.Seg - 0.5f) * L + stagger : ((float)rnd.NextDouble() - 0.5f) * 0.08f * L;
            Quaternion rot = Quaternion.Euler(((float)rnd.NextDouble() - 0.5f) * 2f,
                                              ((c.Layer & 1) == 1 ? 90f : 0f) + ((float)rnd.NextDouble() - 0.5f) * 4f, 0f);
            Vector3 centre = rot * new Vector3(along, 0f, across);
            centre.y = _pSize.y * (c.Layer + 0.5f);
            Apply(t, centre, rot);
        }

        /// <summary>Насколько быстро чернеет доска в этом месте фигуры.</summary>
        private float CharFactor(int i)
        {
            if (_layout == PileLayout.Crib)
            {
                BuildCribSlots();
                if (i >= _crib.Count) return 1f;
                CribSlot c = _crib[i];
                // В середине и внизу — жарче всего; края и верх — медленнее.
                float mid = c.Rows > 1 ? 1f - Mathf.Abs(c.Row - (c.Rows - 1) * 0.5f) / ((c.Rows - 1) * 0.5f) : 1f;
                return Mathf.Lerp(0.5f, 1.6f, mid) * Mathf.Lerp(1.2f, 0.7f, Mathf.Clamp01(c.Layer / 5f));
            }
            if (_layout != PileLayout.Complex) return 1f;
            int ring = Mathf.Max(3, _ringSides) * _ringLayers;
            if (i < _coreCount) return 1.6f;                  // шалаш — в самом огне
            if (i < _coreCount + ring) return 1f;             // сруб
            if (i < _coreCount + ring + _sideStacks * _sideStackPlanks) return 0.25f;   // поленницы у колонн
            return 1f;                                        // верхние кольца
        }

        /// <summary>
        /// «Костёр»: сперва шалаш в центре, потом сруб-колодец вокруг, потом поленницы
        /// между колоннами, дальше — ещё кольца сруба сверху. Доски прогорают с конца:
        /// первыми уходят поленницы (как будто их подкидывают в огонь).
        /// </summary>
        private void PlaceComplex(Transform t, int i)
        {
            int n = Mathf.Max(3, _ringSides);
            int ringCount = n * _ringLayers;
            int sideCount = _sideStacks * _sideStackPlanks;
            System.Random rnd = new System.Random(i * 7919 + 17);
            float L = _pSize.x, W = _pSize.z, T = _pSize.y;

            if (i < _coreCount)
            {
                // ── шалаш: доска наклонно, низом наружу, верхом к центру ──
                float ang = i * 360f / _coreCount + ((float)rnd.NextDouble() - 0.5f) * 10f;
                Quaternion around = Quaternion.Euler(0f, ang, 0f);
                Vector3 radial = around * Vector3.forward;
                Vector3 tangent = around * Vector3.right;
                float tilt = (_coreTilt + ((float)rnd.NextDouble() - 0.5f) * 8f) * Mathf.Deg2Rad;
                Vector3 dir = (-radial * Mathf.Cos(tilt) + Vector3.up * Mathf.Sin(tilt)).normalized;   // вдоль доски, вверх к центру
                Vector3 basePt = radial * (L * _coreBase);
                Vector3 centre = basePt + dir * (L * 0.5f) + Vector3.up * (T * 0.5f);
                Quaternion pileRot = Quaternion.LookRotation(tangent, Vector3.Cross(tangent, dir));
                Apply(t, centre, pileRot);
                return;
            }
            int k = i - _coreCount;
            if (k < ringCount) { PlaceRingSlot(t, k, rnd, 0f); return; }
            k -= ringCount;
            if (k < sideCount)
            {
                // ── поленница между колоннами: крест-накрест, по 2 доски в слое ──
                int stack = k / _sideStackPlanks, j = k % _sideStackPlanks;
                int layer = j / 2, idx = j % 2;
                Quaternion side = Quaternion.Euler(0f, _sideYaw + stack * (360f / Mathf.Max(1, _sideStacks)), 0f);
                Vector3 out_ = side * Vector3.forward;
                Vector3 along = side * Vector3.right;
                bool across = (layer & 1) == 1;
                float off = (idx == 0 ? -1f : 1f) * L * 0.28f;
                Vector3 centre = out_ * (L * _sideDistance) + (across ? along * off : out_ * off * 0.8f);
                centre.y = T * (layer + 0.5f);
                // Доска вдоль стены между колоннами, через слой — поперёк.
                Quaternion pileRot = side * Quaternion.Euler(((float)rnd.NextDouble() - 0.5f) * 3f,
                                                             (across ? 90f : 0f) + ((float)rnd.NextDouble() - 0.5f) * 6f, 0f);
                Apply(t, centre, pileRot);
                return;
            }
            // ── сверх фигуры — ещё кольца сруба сверху ──
            PlaceRingSlot(t, ringCount + (k - sideCount), rnd, 0f);
        }

        /// <summary>Место в срубе-колодце: кольцо (слой) из n досок, каждый слой повёрнут на полшага.</summary>
        private void PlaceRingSlot(Transform t, int k, System.Random rnd, float yOffset)
        {
            int n = Mathf.Max(3, _ringSides);
            int layer = k / n, idx = k % n;
            float step = 360f / n;
            float ang = idx * step + ((layer & 1) == 1 ? step * 0.5f : 0f);
            float inR = _pSize.x / (2f * Mathf.Tan(Mathf.PI / n)) * 0.92f;
            Quaternion around = Quaternion.Euler(0f, ang, 0f);
            Vector3 centre = around * new Vector3(0f, 0f, inR + ((float)rnd.NextDouble() - 0.5f) * _pSize.z * 0.25f);
            centre.y = _pSize.y * (layer + 0.5f) + yOffset;
            Quaternion pileRot = around * Quaternion.Euler(((float)rnd.NextDouble() - 0.5f) * 3f, ((float)rnd.NextDouble() - 0.5f) * 5f, 0f);
            Apply(t, centre, pileRot);
        }

        /// <summary>Поставить доску: centre — центр доски в осях кострища, pileRot — её поворот (длина по X).</summary>
        private void Apply(Transform t, Vector3 centre, Quaternion pileRot)
        {
            Quaternion meshRot = pileRot * _pRot;
            Vector3 offset = meshRot * _pCenter;
            t.position = FirePoint.position + FirePoint.rotation * (centre - offset);
            t.rotation = FirePoint.rotation * meshRot;
            Vector3 ls = FirePoint.lossyScale;
            t.localScale = new Vector3(_pScaleMesh.x / Mathf.Max(1e-5f, ls.x), _pScaleMesh.y / Mathf.Max(1e-5f, ls.y),
                                       _pScaleMesh.z / Mathf.Max(1e-5f, ls.z));
        }

        /// <summary>
        /// Колодец по кругу: слой — многоугольник из досок (доска — сторона), каждая
        /// развёрнута по касательной; следующий слой повёрнут на полшага — доски
        /// ложатся на стыки нижних, как сруб колодца.
        /// </summary>
        private void PlaceRing(Transform t, int i)
        {
            int n = Mathf.Max(3, _ringSides);
            int layer = i / n, idx = i % n;
            float step = 360f / n;
            float ang = idx * step + ((layer & 1) == 1 ? step * 0.5f : 0f);
            // Радиус до середины доски: вписанная окружность многоугольника со стороной = длина доски.
            float inR = _pSize.x / (2f * Mathf.Tan(Mathf.PI / n)) * 0.92f;   // 0.92 — доски чуть заходят друг на друга
            System.Random rnd = new System.Random(i * 7919 + 17);
            float jitterYaw = ((float)rnd.NextDouble() - 0.5f) * 5f;
            float jitterR = ((float)rnd.NextDouble() - 0.5f) * _pSize.z * 0.25f;

            Quaternion around = Quaternion.Euler(0f, ang, 0f);
            Vector3 centre = around * new Vector3(0f, 0f, inR + jitterR);
            centre.y = _pSize.y * (layer + 0.5f);
            // Длина доски (X меша стопки) — по касательной, т.е. вдоль «вправо» от направления наружу.
            Quaternion pileRot = around * Quaternion.Euler(((float)rnd.NextDouble() - 0.5f) * 3f, jitterYaw, 0f);
            Quaternion meshRot = pileRot * _pRot;

            Vector3 offset = meshRot * _pCenter;
            t.position = FirePoint.position + FirePoint.rotation * (centre - offset);
            t.rotation = FirePoint.rotation * meshRot;
            Vector3 ls = FirePoint.lossyScale;
            t.localScale = new Vector3(_pScaleMesh.x / Mathf.Max(1e-5f, ls.x), _pScaleMesh.y / Mathf.Max(1e-5f, ls.y),
                                       _pScaleMesh.z / Mathf.Max(1e-5f, ls.z));
        }

        private void OnDestroy()
        {
            foreach (Transform t in _pile) if (t != null) Destroy(t.gameObject);
            foreach (Transform t in _sides) if (t != null) Destroy(t.gameObject);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 c = FirePoint.position;
            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.8f);
            Gizmos.DrawWireSphere(c, _ignitionRadius);
            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.8f);
            Gizmos.DrawWireCube(c + Vector3.up * _plankSize.y * 2f, new Vector3(_pileHalfWidth * 2f, _plankSize.y * 4f, _pileHalfWidth * 2f));
        }
    }
}
