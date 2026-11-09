using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ШТОРМ — молнии и порывы ветра (только визуал, без физики).
    /// МОЛНИИ: частота — по таблице «Погода → молнии и порывы» (Weather Table) или тест. Виды:
    ///   • зарница — тучи вспыхивают изнутри, без разряда;
    ///   • разряд вдали — зигзаг с ветками от туч до земли, 2–4 мерцания, свечение в тучах и у земли;
    ///   • близкий удар (редко) — ярко, близко.
    /// Вспышка подсвечивает город (_CC_Flash в шейдерах домов, стёкол, toon, капель) и обычные материалы
    /// (временный направленный свет без теней).
    /// ПОРЫВЫ: по той же таблице (тихие погоды — без порывов) — волны порывов; во время порыва летят мультяшные линии ветра,
    /// дождь сильнее косит (CityRain), деревья гнутся сильнее (_CatWindStrength).
    /// Событие OnStrike(расстояние, м) — на потом, для грома.
    /// </summary>
    [ExecuteAlways]
    public class CityStorm : MonoBehaviour
    {
        public static CityStorm Instance { get; private set; }
        /// <summary>Порыв сейчас 0..1 (читает дождь).</summary>
        public static float GustNow { get; private set; }
        /// <summary>Удар молнии: расстояние до неё в метрах (для звука грома потом).</summary>
        public static event Action<float> OnStrike;

        [Header("Материалы (ставит меню «Шторм: поставить в сцену»)")]
        public Material LightningMaterial;
        public Material StreakMaterial;

        [Serializable]
        public class WeatherStorm
        {
            [Tooltip("Кусок имени/названия погоды (CW_13_Tempest → tempest)")] public string Weather;
            [Tooltip("Сила грозы 0..1 (частота молний)")] [Range(0f, 1f)] public float Lightning;
            [Tooltip("Порывистость 0..1 (как часто и сильно)")] [Range(0f, 1f)] public float Gusts;
            [Tooltip("Доля зарниц (без разряда); -1 — общая Sheet Share")] public float Sheets = -1f;
            [Tooltip("Пылинки в солнце 0..1 (тихие солнечные погоды)")] [Range(0f, 1f)] public float Motes;
            [Tooltip("Листопад 0..1 (листья даже без сильного ветра, вдвое больше)")] [Range(0f, 1f)] public float Leaves;
            [Tooltip("Листья: 0 — какие угодно, 1 — осенние, 2 — летние зелёные")] [Range(0, 2)] public int LeafKind;
            [Tooltip("Листья ложатся на пол и лежат (выкл — кружат в воздухе и тают)")] public bool LeavesLie = true;
            [Tooltip("Тонна мусора 0..1: со всех сторон, предметов вчетверо больше")] [Range(0f, 1f)] public float Trash;
            public WeatherStorm() { }
            public WeatherStorm(string w, float l, float g, float sh) { Weather = w; Lightning = l; Gusts = g; Sheets = sh; }
            public WeatherStorm(string w, float motes) { Weather = w; Motes = motes; Sheets = -1f; }
        }

        [Header("Погода → молнии и порывы (не нашлась — тихо)")]
        public List<WeatherStorm> WeatherTable = DefaultWeatherTable();
        [Tooltip("Секунд на смену силы грозы/ветра при смене погоды")]
        public float WeatherBlendSeconds = 6f;

        [Header("Тест: клавиша — молния туда, куда смотришь")]
        [Tooltip("Нажал — молния бьёт точно в точку по лучу из центра камеры (None — выкл.)")]
        public KeyCode StrikeKey = KeyCode.L;
        [Tooltip("Бить по курсору мыши, а не по центру экрана")]
        public bool StrikeAtMouse = false;

        [Header("Тест (меню P)")]
        public bool TestOverride;
        [Range(0f, 1f)] public float TestLightning = 0.7f;
        [Range(0f, 1f)] public float TestWind = 0.8f;

        [Header("Молнии")]
        [Range(0f, 2f)] public float LightningAmount = 1f;
        [Tooltip("Пауза между молниями при самой сильной / самой слабой грозе, с")]
        public float MinInterval = 3f, MaxInterval = 30f;
        [Tooltip("Доля зарниц (тучи вспыхивают без разряда)")]
        [Range(0f, 1f)] public float SheetShare = 0.4f;
        [Tooltip("Шанс близкого удара (умножается на силу грозы)")]
        [Range(0f, 0.5f)] public float NearChance = 0.06f;
        [Tooltip("Расстояние до разряда, м: от–до")]
        public Vector2 BoltDistance = new Vector2(120f, 600f);
        [Tooltip("Высота туч над землёй, м")]
        public float CloudHeight = 160f;
        public Color FlashColor = new Color(0.78f, 0.83f, 1f);
        [Tooltip("Насколько вспышка подсвечивает город")]
        [Range(0f, 3f)] public float FlashStrength = 1.2f;
        [Tooltip("Временный свет вспышки для обычных (не городских) материалов")]
        public bool UseFlashLight = true;
        [Range(0f, 8f)] public float FlashLightIntensity = 2.5f;
        [Tooltip("Близкие удары по погоде бьют в игровую зону (у камеры) и оставляют пятно")]
        public bool NaturalZoneStrikes = true;
        [Tooltip("Удар в зону: разброс вокруг точки взгляда камеры, м")]
        public float ZoneStrikeRadius = 6f;
        [Tooltip("Выжженное пятно: материал (меню ставит CC_Scorch), размер (м), сколько живёт (с), сколько сразу")]
        public Material ScorchMaterial;
        public float ScorchSize = 1.5f;
        public float ScorchSeconds = 45f;
        [Range(1, 16)] public int MaxScorches = 6;
        [Tooltip("Яркость самого разряда (множитель)")]
        [Range(0.2f, 4f)] public float BoltBrightness = 1.4f;
        [Tooltip("Во сколько раз разряд толще в момент удара (+)")]
        [Range(0f, 4f)] public float BoltThickBoost = 1.4f;
        [Tooltip("Лидер: сколько секунд разряд «бежит» сверху вниз до главного удара")]
        [Range(0.02f, 0.3f)] public float LeaderSeconds = 0.09f;
        [Tooltip("Ширина ауры вокруг ствола (× ширина разряда)")]
        [Range(0f, 16f)] public float AuraWidth = 7f;
        [Tooltip("Шанс второго цветного разряда, вьющегося вокруг (у близких — всегда)")]
        [Range(0f, 1f)] public float SecondStrandChance = 0.5f;
        [Tooltip("Шанс сразу 2–3 разрядов (умножается на силу грозы)")]
        [Range(0f, 1f)] public float MultiBoltChance = 0.35f;
        [Tooltip("Рикошеты по полу при ударе рядом: сколько разрядов (от–до) и как далеко (×)")]
        public int GroundArcsMin = 4, GroundArcsMax = 7;
        [Range(0.2f, 3f)] public float GroundArcReach = 1f;
        [Tooltip("Удар в землю: корона шипов, искры, куски бетона, пыль, дым (материал ставит меню)")]
        public Material ImpactMaterial;
        [Tooltip("Выключить старые молнии Atmosphere (CatCooksLightning и т.п.)")]
        public bool DisableLegacyLightning = true;

        [Header("Порывы ветра")]
        [Range(0f, 2f)] public float GustAmount = 1f;
        [Tooltip("Пауза между порывами при слабом / сильном ветре, с")]
        public Vector2 GustInterval = new Vector2(16f, 3.5f);
        [Tooltip("Линий максимум (пачками по 6)")]
        [Range(6, 384)] public int MaxStreaks = 144;
        [Tooltip("Линии ветра: радиус вокруг камеры, длина, толщина (м), жизнь (с)")]
        public float StreakRadius = 16f, StreakLength = 11f, StreakWidth = 0.09f, StreakLife = 1.3f;
        [Tooltip("Летящий мусор в порыв: листья, бумажки (сколько максимум)")]
        [Range(0, 512)] public int MaxDebris = 160;
        [Tooltip("Насколько порыв видно даже при слабом ветре (0 — только сильные)")]
        [Range(0f, 1f)] public float WeakGustVisibility = 0.6f;
        public Material DebrisMaterial;
        [Tooltip("Волна порыва: доля эффектов вне бегущей полосы (0 — только в полосе)")]
        [Range(0f, 1f)] public float WaveBaseline = 0.15f;
        [Tooltip("Ширина полосы волны порыва, м")]
        public float WaveWidth = 7f;
        [Tooltip("Пыльная полоса по полу в порыв (в дождь — водяная взвесь): сколько пятен максимум")]
        [Range(0, 64)] public int MaxDust = 20;
        public Material DustMaterial;

        [Header("Ветер и мусор (визуал)")]
        [Tooltip("Летающие предметы в порыв: пакеты, газеты, стаканчики/банки, картонки, изредка зонтик")]
        [Range(0f, 2f)] public float JunkAmount = 1f;
        [Range(0, 128)] public int MaxJunk = 36;
        public Material JunkMaterial;
        [Tooltip("Листья, которые падают на пол и лежат (больше в ветреную погоду)")]
        [Range(0f, 2f)] public float LeavesAmount = 1f;
        [Range(0, 1024)] public int MaxLeaves = 220;
        public Material LeavesMaterial;
        [Tooltip("Тучки пыли местами (разного объёма), в дождь — водяная взвесь")]
        [Range(0f, 2f)] public float DustCloudAmount = 1f;
        [Range(0, 40)] public int MaxDustClouds = 10;
        public Material DustCloudMaterial;

        [Tooltip("Тонна мусора поверх любой погоды (тест и для веселья)")]
        public bool TrashOverlay = false;
        [Tooltip("Во сколько раз больше предметов в «тонне мусора»")]
        [Range(1, 8)] public int TrashMultiplier = 4;

        [Header("Пылинки в солнце (тихие солнечные погоды)")]
        [Range(0f, 2f)] public float MotesAmount = 1f;
        [Range(0, 1024)] public int MaxMotes = 260;
        [Tooltip("Коробка вокруг точки взгляда (полуразмер), м; размер пылинки, м")]
        public float MotesRadius = 12f, MotesSize = 0.025f;
        public Material MotesMaterial;

        [Tooltip("Деревья в порыв гнутся сильнее")]
        [Range(0f, 3f)] public float TreeGustBoost = 1.8f;
        [Tooltip("Дождь в порыв косит сильнее (множитель)")]
        [Range(0f, 4f)] public float RainGustBoost = 2.2f;

        [SerializeField, HideInInspector] private int _version;
        private const int Version = 4;
        private float _impactAge = -1f;
        private Vector3 _impactPos, _impactN = Vector3.up;
        static readonly int IdImpact = Shader.PropertyToID("_CC_Impact");
        static readonly int IdBoltT = Shader.PropertyToID("_CC_BoltT");
        static readonly int IdImpactN = Shader.PropertyToID("_CC_ImpactN");
        static readonly int IdImpactCol = Shader.PropertyToID("_CC_ImpactCol");
        static readonly int IdGlowColor = Shader.PropertyToID("_GlowColor");
        private float _waveFront0, _waveSpeed, _motesSm, _leavesSm;
        static readonly int IdWindFx = Shader.PropertyToID("_CC_WindFx");
        static readonly int IdWindFx2 = Shader.PropertyToID("_CC_WindFx2");
        private float _trashSm, _leafKindAutumn, _leafKindSummer, _leafLie = 1f;
        private bool _leafFall;
        private Vector2 _waveDir = new Vector2(0.8f, 0.6f);
        static readonly int IdGustWave = Shader.PropertyToID("_CC_GustWave");
        static readonly int IdMotes = Shader.PropertyToID("_CC_Motes");
        private float _gain = 1f;

        public bool Log = true;

        // молния
        private System.Random _rng = new System.Random(12345);
        private float _next = 6f, _age = -1f, _life, _strength, _seed;
        private bool _sheet;
        private readonly float[] _pt = new float[5], _pa = new float[5];
        private int _pn;
        private Vector3 _flashDir = Vector3.up;
        private Mesh _mesh;
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Vector3> _n = new List<Vector3>();
        private readonly List<Vector4> _uv = new List<Vector4>();
        private readonly List<Color> _c = new List<Color>();
        private readonly List<int> _t = new List<int>();
        private readonly List<Vector3> _path = new List<Vector3>();
        private Light _light;
        private Vector3 _boltTop, _boltGround;

        // порывы
        private float _gustNext = 5f, _gustT = 99f, _gustDur = 3f, _gustPeak;
        private float _treeBase, _treeWritten = float.NaN;
        private bool _treeOn;

        // старые молнии
        private readonly List<MonoBehaviour> _legacy = new List<MonoBehaviour>();
        private float _legacyCheck = -1f;

        static readonly int IdBolt = Shader.PropertyToID("_CC_Bolt");
        static readonly int IdFlash = Shader.PropertyToID("_CC_Flash");
        static readonly int IdFlashDir = Shader.PropertyToID("_CC_FlashDir");
        static readonly int IdGust = Shader.PropertyToID("_CC_Gust");
        static readonly int IdGustP = Shader.PropertyToID("_CC_GustP");
        static readonly int IdGustFocus = Shader.PropertyToID("_CC_GustFocus");
        static readonly int IdTreeWind = Shader.PropertyToID("_CatWindStrength");
        static readonly int IdGround = Shader.PropertyToID("_CC_GroundY");
        static readonly int IdMeter = Shader.PropertyToID("_CC_Meter");

        public float Activity { get; private set; }
        public float Windiness { get; private set; }
        public bool Flashing { get { return _age >= 0f; } }

        public static List<WeatherStorm> DefaultWeatherTable()
        {
            return new List<WeatherStorm>
            {
                new WeatherStorm("tempest", 0.9f, 1f, -1f),           // шторм: часто, бывают близкие удары
                new WeatherStorm("thunderstorm", 0.6f, 0.65f, -1f),   // гроза
                new WeatherStorm("hailthunder", 0.75f, 0.75f, -1f),   // гроза с градом: молнии, ливень
                new WeatherStorm("hailstorm", 0.3f, 1f, 0.7f),        // буря с градом: ветер, зарницы
                new WeatherStorm("hail", 0.3f, 0.6f, -1f),            // град
                new WeatherStorm("hurricane", 0.25f, 0.95f, 0.8f),    // ураганное предупреждение: в основном зарницы
                new WeatherStorm("prestorm", 0.12f, 0.35f, 0.9f),     // перед грозой: редкие зарницы вдали
                new WeatherStorm("downpour", 0.08f, 0.35f, 0.9f),     // ливень: изредка зарница
                new WeatherStorm("highwind", 0f, 0.95f, -1f),         // сильный ветер
                new WeatherStorm("blizzard", 0f, 0.9f, -1f),          // метель
                new WeatherStorm("duststorm", 0f, 0.9f, -1f),         // пыльная буря
                new WeatherStorm("gloomy", 0f, 0.2f, -1f),            // хмуро — изредка слабые
                new WeatherStorm("steadyrain", 0f, 0.2f, -1f),        // дождь — изредка слабые
                // пылинки в солнце (тихие солнечные погоды)
                new WeatherStorm("scorcher", 1f),                    // пекло
                new WeatherStorm("heathaze", 1f),                    // жара
                new WeatherStorm("pinkhaze", 1f),                    // розовая дымка
                new WeatherStorm("morningmist", 0.8f),               // утренняя дымка
                new WeatherStorm("sunbreaks", 0.7f),                 // солнце сквозь облака
                new WeatherStorm("clear", 0.6f),                     // ясно
                new WeatherStorm("rainbow", 0.6f),                   // радуга
                new WeatherStorm("afterstorm", 0.6f),                // после грозы
                new WeatherStorm("faircumulus", 0.5f),               // редкие кучевые
                new WeatherStorm("sunshower", 0.5f),                 // грибной дождь
                new WeatherStorm("cirrus", 0.4f),                    // перистые
                new WeatherStorm("cumulus", 0.3f),                   // кучевые
                // листопады и мусор
                new WeatherStorm { Weather = "autumnleaves", Gusts = 0.45f, Sheets = -1f, Leaves = 1f, LeafKind = 1, LeavesLie = true },
                new WeatherStorm { Weather = "summerleaves", Gusts = 0.35f, Sheets = -1f, Leaves = 0.8f, LeafKind = 2, LeavesLie = false, Motes = 0.5f },
                new WeatherStorm { Weather = "trashstorm", Gusts = 0.9f, Sheets = -1f, Trash = 1f },
                new WeatherStorm { Weather = "snowfall", Gusts = 0.2f, Sheets = -1f },
            };
        }

        /// <summary>Строка таблицы для погоды (null — тихая погода).</summary>
        public WeatherStorm ForWeather(CatCooksWeatherPreset w)
        {
            if (w == null || WeatherTable == null) return null;
            string key = (w.name + " " + w.title).ToLowerInvariant().Replace("_", "");
            WeatherStorm best = null;
            int bestLen = 0;
            for (int i = 0; i < WeatherTable.Count; i++)                    // побеждает самый длинный ключ: «hailstorm» точнее «hail»
            {
                WeatherStorm r = WeatherTable[i];
                if (r == null || string.IsNullOrEmpty(r.Weather)) continue;
                string k = r.Weather.ToLowerInvariant().Replace("_", "");
                if (k.Length > bestLen && key.Contains(k)) { best = r; bestLen = k.Length; }
            }
            return best;
        }

        private void MergeWeatherTable()
        {
            if (WeatherTable == null) WeatherTable = new List<WeatherStorm>();
            foreach (WeatherStorm d in DefaultWeatherTable())
            {
                bool has = false;
                foreach (WeatherStorm r in WeatherTable) if (r != null && r.Weather == d.Weather) { has = true; break; }
                if (!has) WeatherTable.Add(d);
            }
        }

        private float _actSm, _windSm, _sheetNow = -1f;
        private CatCooksWeatherPreset _lastPreset;
        private WeatherStorm _row;

        private void OnEnable()
        {
            MergeWeatherTable();
            if (_version < Version)
            {
                // новые значения по умолчанию (порывы заметнее) — на уже стоящем объекте
                MaxStreaks = 120; StreakRadius = 16f; StreakLength = 11f; StreakWidth = 0.09f; StreakLife = 1.3f;
                TreeGustBoost = 1.8f; RainGustBoost = 2.2f; MaxDebris = 160; WeakGustVisibility = 0.6f;
                ScorchSize = 1.5f; BoltBrightness = 1.4f; BoltThickBoost = 1.4f; LeaderSeconds = 0.09f;
                MaxStreaks = 144;                                       // 24 пачки по 6 линий
                _version = Version;
            }
            Instance = this;
            _rng = new System.Random(Environment.TickCount);
            _next = Rand(3f, 8f);
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            GustNow = 0f;
            ClearFlash();
            Shader.SetGlobalVector(IdGust, Vector4.zero);
            Shader.SetGlobalVector(IdWindFx, Vector4.zero);
            Shader.SetGlobalVector(IdWindFx2, Vector4.zero);
            Shader.SetGlobalVector(IdImpact, new Vector4(0f, 0f, 0f, -1f));
            if (_treeOn && !float.IsNaN(_treeWritten) && Mathf.Abs(Shader.GetGlobalFloat(IdTreeWind) - _treeWritten) < 1e-4f)
                Shader.SetGlobalFloat(IdTreeWind, _treeBase);
            _treeOn = false;
            if (_light != null) { if (Application.isPlaying) Destroy(_light.gameObject); else DestroyImmediate(_light.gameObject); }
            _light = null;
            foreach (Scorch sc in _scorches) if (sc != null && sc.Go != null) { if (Application.isPlaying) Destroy(sc.Go); else DestroyImmediate(sc.Go); }
            _scorches.Clear();
        }

        private void OnDestroy()
        {
            if (_mesh != null) { if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh); }
            if (_quad != null) { if (Application.isPlaying) Destroy(_quad); else DestroyImmediate(_quad); }
        }

        private float Meter()
        {
            if (CityRain.Instance != null) return CityRain.Instance.Meter;
            float m = Shader.GetGlobalFloat(IdMeter);
            return m > 0f ? m : 3.5f;
        }

        private float Rand(float a, float b) { return a + (float)_rng.NextDouble() * (b - a); }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                // в редакторе без Play — ничего не мигает
                GustNow = 0f;
                Shader.SetGlobalVector(IdGust, Vector4.zero);
                Shader.SetGlobalVector(IdFlash, Vector4.zero);
                Shader.SetGlobalVector(IdMotes, Vector4.zero);
                Shader.SetGlobalVector(IdWindFx, Vector4.zero);
                Shader.SetGlobalVector(IdWindFx2, Vector4.zero);
                return;
            }
            float dt = Time.deltaTime;
            if (DisableLegacyLightning) TickLegacy();

            // погода → строка таблицы (молнии в пресетах погоды не записаны — берём из своей таблицы)
            CatCooksWeatherPreset cur = CatCooksWeatherState.Current;
            if (cur != _lastPreset) { _lastPreset = cur; _row = ForWeather(cur); }
            float actT = _row != null ? _row.Lightning : 0f;
            float windT = _row != null ? _row.Gusts : 0f;
            _sheetNow = _row != null ? _row.Sheets : -1f;
            float k = dt / Mathf.Max(0.1f, WeatherBlendSeconds);
            _actSm = Mathf.MoveTowards(_actSm, actT, k);
            _windSm = Mathf.MoveTowards(_windSm, windT, k);
            Activity = Mathf.Clamp01((TestOverride ? TestLightning : _actSm) * LightningAmount);
            Windiness = Mathf.Clamp01(TestOverride ? TestWind : _windSm);

            if (StrikeKey != KeyCode.None && KeyDown(StrikeKey)) StrikeAtView();
            UpdateLightning(dt);
            UpdateImpact(dt);
            UpdateGusts(dt);
            UpdateScorches();
            Draw();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;
            // деревья: множим силу ветра контроллера на порыв (база — то, что пишет контроллер)
            float cur = Shader.GetGlobalFloat(IdTreeWind);
            if (float.IsNaN(_treeWritten) || Mathf.Abs(cur - _treeWritten) > 1e-4f) _treeBase = cur;
            if (_treeBase <= 0f) return;
            float v = _treeBase * (1f + GustNow * TreeGustBoost);
            Shader.SetGlobalFloat(IdTreeWind, v);
            _treeWritten = v;
            _treeOn = true;
        }

        // ─────────────── молнии ───────────────
        private void UpdateLightning(float dt)
        {
            if (_age < 0f)
            {
                if (Activity > 0.03f)
                {
                    _next -= dt;
                    if (_next <= 0f) Strike(0);
                }
                else _next = Mathf.Min(_next, Rand(2f, 6f));
                return;
            }

            _age += dt;
            if (_age > _life)
            {
                _age = -1f;
                _next = Mathf.Lerp(MaxInterval, MinInterval, Activity) * Rand(0.4f, 1.6f);
                ClearFlash();
                return;
            }
            // вспышки: быстрый подъём (20 мс) и плавный спад — без стробоскопа
            float I = 0f;
            for (int i = 0; i < _pn; i++)
            {
                float d = _age - _pt[i];
                if (d < 0f) continue;
                I = Mathf.Max(I, _pa[i] * Mathf.Clamp01(d / 0.02f) * Mathf.Exp(-d / 0.11f));
            }
            float strokeI = I;                                                              // главный удар/повторы без лидера
            if (!_sheet)
            {
                if (_age < LeaderSeconds) I = Mathf.Max(I, 0.3f);                          // лидер: тусклый, бежит вниз
                I += 0.12f * Mathf.Clamp01(1f - _age / _life);                             // послесвечение разряда
            }
            I = Mathf.Min(I, 1.6f);
            float reveal = _sheet ? 1f : Mathf.Clamp01(_age / LeaderSeconds);
            float thick = 1f + BoltThickBoost * Mathf.Clamp01(strokeI);                  // в момент удара — толще
            Shader.SetGlobalVector(IdBolt, new Vector4(I * _gain * BoltBrightness, _age, reveal, thick));
            Shader.SetGlobalVector(IdBoltT, new Vector4(_sheet ? 0f : LeaderSeconds, 0f, 0f, 0f));
            float fl = I * _strength * FlashStrength * 0.6f;
            Shader.SetGlobalVector(IdFlash, new Vector4(FlashColor.r, FlashColor.g, FlashColor.b, fl));
            Shader.SetGlobalVector(IdFlashDir, new Vector4(_flashDir.x, _flashDir.y, _flashDir.z, 0f));
            if (UseFlashLight)
            {
                EnsureLight();
                _light.color = FlashColor;
                _light.intensity = I * _strength * FlashLightIntensity;
                _light.transform.rotation = Quaternion.LookRotation(-_flashDir);
                _light.enabled = _light.intensity > 0.02f;
            }
        }

        private void ClearFlash()
        {
            Shader.SetGlobalVector(IdBolt, Vector4.zero);
            Shader.SetGlobalVector(IdFlash, Vector4.zero);
            if (_light != null) { _light.intensity = 0f; _light.enabled = false; }
        }

        private void EnsureLight()
        {
            if (_light != null) return;
            GameObject g = new GameObject("Lightning Flash Light");
            g.hideFlags = HideFlags.DontSave;
            g.transform.SetParent(transform, false);
            _light = g.AddComponent<Light>();
            _light.type = LightType.Directional;
            _light.shadows = LightShadows.None;
            _light.intensity = 0f;
            _light.enabled = false;
        }

        /// <summary>Молния прямо сейчас (кнопка в меню P). bolt — обязательно разряд, не зарница.</summary>
        public void StrikeNow(bool bolt) { if (Application.isPlaying) Strike(bolt ? 1 : 0); }

        /// <summary>Удар прямо в игровую зону (туда, куда смотрит камера) — с выжженным пятном. Кнопка в меню P.</summary>
        public void StrikeZone() { if (Application.isPlaying) Strike(2); }

        /// <summary>Точка, куда смотрит камера: луч по коллайдерам, иначе плоскость земли.</summary>
        public static Vector3 FocusPoint(Camera cam, float meter)
        {
            Ray r = new Ray(cam.transform.position, cam.transform.forward);
            RaycastHit hit;
            if (Physics.Raycast(r, out hit, cam.farClipPlane * 0.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return hit.point;
            float gy = Shader.GetGlobalFloat(IdGround);
            if (r.direction.y < -0.05f)
            {
                float t = (gy - r.origin.y) / r.direction.y;
                if (t > 0f) return r.origin + r.direction * Mathf.Min(t, 300f * meter);
            }
            return r.origin + r.direction * 20f * meter;
        }

        private bool _inputWarned;
        private bool KeyDown(KeyCode k)
        {
            try { return Input.GetKeyDown(k); }
            catch (InvalidOperationException)
            {
                if (!_inputWarned && Log) Debug.LogWarning("[Шторм] старый Input выключен (Player → Active Input Handling) — клавиша молнии не работает, есть кнопка в меню P", this);
                _inputWarned = true;
                return false;
            }
        }

        private Vector3 _aimPoint, _aimNormal;

        /// <summary>Молния точно туда, куда смотрит камера (луч из центра экрана или из-под курсора). Клавиша Strike Key.</summary>
        public void StrikeAtView()
        {
            if (!Application.isPlaying) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Ray r;
            if (StrikeAtMouse)
            {
                try { r = cam.ScreenPointToRay(Input.mousePosition); }
                catch (InvalidOperationException) { r = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)); }
            }
            else r = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            RaycastHit hit;
            if (Physics.Raycast(r, out hit, cam.farClipPlane, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                _aimPoint = hit.point;
                _aimNormal = hit.normal;
                Strike(3);
            }
            else
            {
                if (Log) Debug.Log("[Шторм] луч ни во что не попал — молния бьёт в зону взгляда без пятна", this);
                Strike(2);
            }
        }

        // mode: 0 — по погоде, 1 — точно разряд, 2 — удар в игровую зону, 3 — точно в точку прицела (_aimPoint)
        private void Strike(int mode)
        {
            Camera cam = Camera.main;
            if (cam == null) { _next = 2f; return; }
            float m = Meter();
            float groundY = Shader.GetGlobalFloat(IdGround);
            Vector3 cp = cam.transform.position;
            Vector3 fwd = cam.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            float farU = cam.farClipPlane * 0.85f;

            bool zone = mode >= 2 || (mode == 0 && NaturalZoneStrikes && Rand(0f, 1f) < NearChance * Activity);
            bool near = zone || (mode == 1 && Rand(0f, 1f) < 0.25f);
            float sheetShare = (!TestOverride && _sheetNow >= 0f) ? _sheetNow : SheetShare;
            _sheet = mode == 0 && !near && Rand(0f, 1f) < sheetShare;

            Vector3 ground, groundN = Vector3.up;
            bool hitSurface = false;
            if (mode == 3)
            {
                ground = _aimPoint; groundN = _aimNormal; hitSurface = true;            // точно куда смотришь
            }
            else if (zone)
            {
                // в игровую зону: рядом с точкой взгляда, на первую поверхность сверху
                Vector3 f = FocusPoint(cam, m);
                Vector2 rnd = UnityEngine.Random.insideUnitCircle * ZoneStrikeRadius * m;
                Vector3 probe = new Vector3(f.x + rnd.x, f.y + 200f * m, f.z + rnd.y);
                RaycastHit hit;
                if (Physics.Raycast(probe, Vector3.down, out hit, 400f * m, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                { ground = hit.point; groundN = hit.normal; hitSurface = true; }
                else ground = new Vector3(probe.x, f.y, probe.z);
            }
            else
            {
                float dist0 = near ? Rand(30f, 70f) : Rand(BoltDistance.x, BoltDistance.y);
                dist0 = Mathf.Min(dist0 * m, farU * 0.8f);
                float ang = Rand(0f, 1f) < 0.7f ? Rand(-55f, 55f) : Rand(0f, 360f);              // чаще — в кадре
                ground = new Vector3(cp.x, groundY, cp.z) + Quaternion.Euler(0f, ang, 0f) * fwd * dist0;
            }
            float dist = Vector3.Distance(new Vector3(cp.x, ground.y, cp.z), ground);
            float h = Mathf.Min(CloudHeight * m, Mathf.Sqrt(Mathf.Max(farU * farU - dist * dist, 1f)) * 0.9f);
            Vector3 top = ground + Vector3.up * h + new Vector3(Rand(-1f, 1f), 0f, Rand(-1f, 1f)) * h * (zone ? 0.08f : 0.15f);
            _boltTop = top; _boltGround = ground;
            _seed = Rand(0f, 1f);

            BeginMesh();
            if (_sheet)
            {
                Vector3 c = top + new Vector3(Rand(-1f, 1f), Rand(-0.1f, 0.15f), Rand(-1f, 1f)) * h * 0.4f;
                AddGlow(c, Rand(60f, 140f) * m, 1f);
                AddGlow(c + new Vector3(Rand(-1f, 1f), 0f, Rand(-1f, 1f)) * 40f * m, Rand(40f, 80f) * m, 0.6f);
            }
            else
            {
                float w = (zone ? 1.0f : near ? 1.2f : 1.8f) * m;
                BuildBolt(top, ground, w, near);
                AddGlow(top, (near ? 40f : 70f) * m, 1f);                                     // подсветка тучи
                AddGlow(top, (near ? 30f : 55f) * m, 1.3f, 0f, 4f);                           // плоская вспышка-диск в туче
                if (!zone) AddGlow(ground + groundN * 0.6f * m, (near ? 10f : 16f) * m, near ? 1.2f : 0.7f);   // свечение у земли (только вдали)
                if (zone)
                {
                    AddGroundDisc(ground + groundN * 0.03f * m, groundN, 2.2f * m, 1.3f);       // рваное пятно света на полу
                    BuildGroundArcs(ground, groundN, w, m);                                   // разряды-рикошеты по полу
                }
                // в сильную грозу — ещё 1–2 разряда рядом одновременно
                if (mode == 0 && !near && Rand(0f, 1f) < MultiBoltChance * Activity)
                {
                    int extra = 1 + _rng.Next(2);
                    Vector3 side = Vector3.Cross(Vector3.up, (ground - cp).normalized);
                    for (int e = 0; e < extra; e++)
                    {
                        Vector3 g2 = ground + side * Rand(-1f, 1f) * Rand(40f, 140f) * m + (ground - cp).normalized * Rand(-30f, 60f) * m;
                        g2.y = ground.y;
                        Vector3 t2 = g2 + Vector3.up * h * Rand(0.85f, 1.05f) + new Vector3(Rand(-1f, 1f), 0f, Rand(-1f, 1f)) * h * 0.12f;
                        BuildBolt(t2, g2, w * Rand(0.6f, 0.9f), false);
                        AddGlow(t2, 50f * m, 0.7f);
                    }
                }
            }
            EndMesh();

            // мерцание: лидер бежит вниз (тускло) → главный удар (ярко и толсто) → 1–3 повтора слабее, плавно гаснут
            float t;
            if (_sheet)
            {
                _pn = 2 + _rng.Next(2);
                t = 0f;
                for (int i = 0; i < _pn; i++) { _pt[i] = t; _pa[i] = i == 0 ? 1f : Rand(0.4f, 0.8f); t += Rand(0.1f, 0.22f); }
            }
            else
            {
                _pn = 2 + _rng.Next(3);
                t = LeaderSeconds;
                for (int i = 0; i < _pn; i++) { _pt[i] = t; _pa[i] = i == 0 ? 1f : Rand(0.5f, 0.85f) * (1f - 0.15f * i); t += Rand(0.12f, 0.22f); }
            }
            _life = t + (_sheet ? 0.3f : zone ? 0.8f : 0.5f);
            _gain = zone ? 1.7f : near ? 1.35f : 1f;
            _age = 0f;
            float k = Mathf.InverseLerp(BoltDistance.x * m, BoltDistance.y * m, dist);
            _strength = zone ? 1.9f : near ? 1.6f : _sheet ? 0.5f : Mathf.Lerp(1.1f, 0.5f, k);
            _flashDir = (top - cp).normalized;
            if (zone && hitSurface && ScorchMaterial != null) SpawnScorch(ground, groundN, m);
            if (zone) { _impactAge = 0f; _impactPos = ground; _impactN = groundN; }
            if (OnStrike != null) OnStrike(dist / m);
        }

        // ─────────────── удар в землю ───────────────
        private void UpdateImpact(float dt)
        {
            if (_impactAge < 0f) { Shader.SetGlobalVector(IdImpact, new Vector4(0f, 0f, 0f, -1f)); return; }
            _impactAge += dt;
            if (_impactAge > 2.4f) { _impactAge = -1f; Shader.SetGlobalVector(IdImpact, new Vector4(0f, 0f, 0f, -1f)); return; }
            Shader.SetGlobalVector(IdImpact, new Vector4(_impactPos.x, _impactPos.y, _impactPos.z, _impactAge));
            Shader.SetGlobalVector(IdImpactN, new Vector4(_impactN.x, _impactN.y, _impactN.z, Meter()));
            Color gc = LightningMaterial != null && LightningMaterial.HasProperty(IdGlowColor) ? LightningMaterial.GetColor(IdGlowColor) : FlashColor;
            Shader.SetGlobalVector(IdImpactCol, new Vector4(gc.r, gc.g, gc.b, 1f));
        }

        // ─────────────── выжженное пятно ───────────────
        private class Scorch { public GameObject Go; public MeshRenderer R; public float Born; public float Seed; }
        private readonly List<Scorch> _scorches = new List<Scorch>();
        private Mesh _quad;
        private MaterialPropertyBlock _mpb;
        static readonly int IdAge = Shader.PropertyToID("_Age");
        static readonly int IdSeed = Shader.PropertyToID("_Seed");
        static readonly int IdLife = Shader.PropertyToID("_Life");

        private void SpawnScorch(Vector3 p, Vector3 n, float m)
        {
            if (_quad == null)
            {
                _quad = new Mesh();
                _quad.name = "ScorchQuad";
                _quad.hideFlags = HideFlags.HideAndDontSave;
                _quad.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
                _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                _quad.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
                _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                _quad.RecalculateBounds();
            }
            Scorch sc;
            if (_scorches.Count >= Mathf.Max(1, MaxScorches))
            {
                sc = _scorches[0];
                _scorches.RemoveAt(0);
            }
            else
            {
                sc = new Scorch();
                sc.Go = new GameObject("Lightning Scorch");
                sc.Go.hideFlags = HideFlags.DontSave;
                sc.Go.transform.SetParent(transform, true);
                sc.Go.AddComponent<MeshFilter>().sharedMesh = _quad;
                sc.R = sc.Go.AddComponent<MeshRenderer>();
                sc.R.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sc.R.receiveShadows = false;
            }
            sc.R.sharedMaterial = ScorchMaterial;
            sc.Go.SetActive(true);
            sc.Go.transform.position = p + n * 0.02f * m;
            sc.Go.transform.rotation = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0f, Rand(0f, 360f), 0f);
            sc.Go.transform.localScale = Vector3.one * ScorchSize * m * Rand(0.85f, 1.2f);
            sc.Born = Time.time;
            sc.Seed = Rand(0f, 1f);
            _scorches.Add(sc);
            if (Log) Debug.Log("[Шторм] удар в зону: " + p.ToString("F1") + ", выжженное пятно (" + _scorches.Count + "/" + MaxScorches + ")", this);
        }

        private void UpdateScorches()
        {
            if (_scorches.Count == 0) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            for (int i = _scorches.Count - 1; i >= 0; i--)
            {
                Scorch sc = _scorches[i];
                if (sc.Go == null) { _scorches.RemoveAt(i); continue; }
                float age = Time.time - sc.Born;
                if (age > ScorchSeconds)
                {
                    if (Application.isPlaying) Destroy(sc.Go); else DestroyImmediate(sc.Go);
                    _scorches.RemoveAt(i);
                    continue;
                }
                _mpb.SetFloat(IdAge, age);
                _mpb.SetFloat(IdSeed, sc.Seed);
                _mpb.SetFloat(IdLife, ScorchSeconds);
                sc.R.SetPropertyBlock(_mpb);
            }
        }

        // ─────────────── меш молнии ───────────────
        private void BeginMesh() { _v.Clear(); _n.Clear(); _uv.Clear(); _c.Clear(); _t.Clear(); }

        private void EndMesh()
        {
            if (_mesh == null)
            {
                _mesh = new Mesh();
                _mesh.name = "CityLightning";
                _mesh.hideFlags = HideFlags.HideAndDontSave;
                _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                _mesh.MarkDynamic();
            }
            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetNormals(_n);
            _mesh.SetUVs(0, _uv);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_t, 0);
            _mesh.bounds = new Bounds((_boltTop + _boltGround) * 0.5f, Vector3.one * 1e5f);
        }

        private void BuildBolt(Vector3 top, Vector3 ground, float width) { BuildBolt(top, ground, width, false); }

        private void BuildBolt(Vector3 top, Vector3 ground, float width, bool rich)
        {
            float H = Mathf.Max(top.y - ground.y, 1f);
            // ствол
            _path.Clear();
            _path.Add(top);
            Subdivide(top, ground, 7, 0.2f);
            List<Vector3> main = new List<Vector3>(_path);
            AddRibbon(main, top.y, H, width, 1f, false);
            AddRibbon(main, top.y, H, width * AuraWidth, 1f, false, 3f);                     // аура вокруг ствола
            // второй цветной разряд: вьётся вокруг ствола (рваная спираль)
            if (rich || _rng.NextDouble() < SecondStrandChance)
            {
                List<Vector3> s2 = new List<Vector3>(main.Count);
                float ph = Rand(0f, 6.28f), rad = width * Rand(1.6f, 2.8f);
                for (int i = 0; i < main.Count; i++)
                {
                    Vector3 T = main[Mathf.Min(i + 1, main.Count - 1)] - main[Mathf.Max(i - 1, 0)];
                    if (T.sqrMagnitude < 1e-8f) T = Vector3.down;
                    T.Normalize();
                    Vector3 a1 = Vector3.Cross(T, Vector3.right).normalized, a2 = Vector3.Cross(T, a1);
                    float an = ph + i * Rand(0.25f, 0.55f);
                    float r = rad * Rand(0.4f, 1.3f) * (i == 0 || i == main.Count - 1 ? 0.2f : 1f);
                    s2.Add(main[i] + (a1 * Mathf.Cos(an) + a2 * Mathf.Sin(an)) * r);
                }
                AddRibbon(s2, top.y, H, width * 0.5f, 1f, false, 2f);
            }
            // яркие «бусины» на изломах ствола
            int beads = 3 + _rng.Next(3);
            for (int i = 0; i < beads; i++)
            {
                int idx = Mathf.Clamp((int)(main.Count * Rand(0.15f, 0.95f)), 1, main.Count - 2);
                AddGlow(main[idx], width * Rand(2.5f, 4.5f), 1.2f, 1f, 1f);
            }
            // ветки
            int nb = 3 + _rng.Next(4);
            for (int b = 0; b < nb; b++)
            {
                int idx = Mathf.Clamp((int)(main.Count * Rand(0.1f, 0.7f)), 1, main.Count - 2);
                Vector3 s = main[idx];
                Vector3 along = (main[idx + 1] - main[idx - 1]).normalized;
                Vector3 side = new Vector3(Rand(-1f, 1f), 0f, Rand(-1f, 1f)).normalized;
                Vector3 bd = (along * 0.5f + side * 0.8f + Vector3.down * 0.5f).normalized;
                float len = H * Rand(0.12f, 0.3f);
                Vector3 e = s + bd * len;
                _path.Clear();
                _path.Add(s);
                Subdivide(s, e, 5, 0.26f);
                List<Vector3> br = new List<Vector3>(_path);
                AddRibbon(br, top.y, H, width * 0.6f, 0.6f, true);
                if (_rng.NextDouble() < 0.5)
                {
                    int j = Mathf.Clamp((int)(br.Count * Rand(0.3f, 0.7f)), 1, br.Count - 2);
                    Vector3 s2 = br[j];
                    Vector3 bd2 = (bd + new Vector3(Rand(-1f, 1f), -0.4f, Rand(-1f, 1f))).normalized;
                    _path.Clear();
                    _path.Add(s2);
                    Subdivide(s2, s2 + bd2 * len * 0.45f, 4, 0.3f);
                    AddRibbon(new List<Vector3>(_path), top.y, H, width * 0.4f, 0.4f, true);
                }
            }
        }

        // ломаная: середина смещается вбок пропорционально длине (обход в порядке «от a к b»)
        private void Subdivide(Vector3 a, Vector3 b, int depth, float amp)
        {
            if (depth <= 0) { _path.Add(b); return; }
            float L = Vector3.Distance(a, b);
            Vector3 mid = (a + b) * 0.5f + new Vector3(Rand(-1f, 1f), Rand(-0.3f, 0.3f), Rand(-1f, 1f)) * L * amp;
            Subdivide(a, mid, depth - 1, amp);
            Subdivide(mid, b, depth - 1, amp);
        }

        private void AddRibbon(List<Vector3> p, float topY, float H, float width, float bright, bool taper)
        {
            AddRibbon(p, topY, H, width, bright, taper, 0f);
        }

        // type: 0 ствол/ветки, 2 второй цветной разряд, 3 аура
        private void AddRibbon(List<Vector3> p, float topY, float H, float width, float bright, bool taper, float type)
        {
            int n = p.Count;
            if (n < 2) return;
            int baseI = _v.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 T = p[Mathf.Min(i + 1, n - 1)] - p[Mathf.Max(i - 1, 0)];
                if (T.sqrMagnitude < 1e-8f) T = Vector3.down;
                T.Normalize();
                float along = Mathf.Clamp01((topY - p[i].y) / H);
                float w = taper ? width * (1f - 0.75f * i / (n - 1f)) : width * (0.75f + 0.25f * (1f - along));
                Color c = new Color(1f, 1f, 1f, bright);
                _v.Add(p[i]); _n.Add(T); _uv.Add(new Vector4(-1f, along, type, w)); _c.Add(c);
                _v.Add(p[i]); _n.Add(T); _uv.Add(new Vector4(1f, along, type, w)); _c.Add(c);
                if (i > 0)
                {
                    int a = baseI + (i - 1) * 2, b = baseI + i * 2;
                    _t.Add(a); _t.Add(b); _t.Add(a + 1);
                    _t.Add(a + 1); _t.Add(b); _t.Add(b + 1);
                }
            }
        }

        private void AddGlow(Vector3 c, float radius, float alpha) { AddGlow(c, radius, alpha, 0f, 1f); }

        // type 1 — круглое к камере (hot — горячая серединка, для бусин), 4 — плоская вспышка в туче
        private void AddGlow(Vector3 c, float radius, float alpha, float hot, float type)
        {
            int b = _v.Count;
            Color col = new Color(hot, 1f, 1f, alpha);
            Vector2[] k = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            for (int i = 0; i < 4; i++)
            {
                _v.Add(c); _n.Add(Vector3.up); _uv.Add(new Vector4(k[i].x, k[i].y, type, radius)); _c.Add(col);
            }
            _t.Add(b); _t.Add(b + 1); _t.Add(b + 2);
            _t.Add(b); _t.Add(b + 2); _t.Add(b + 3);
        }

        /// <summary>
        /// Разряды-рикошеты: из точки удара по полу разбегаются 4–7 рваных разрядов, часть подпрыгивает дугой
        /// и приземляется дальше, у некоторых — короткая вилка. Тип 6: uv.y — доля пути от центра (разбегаются).
        /// </summary>
        private void BuildGroundArcs(Vector3 c, Vector3 n, float width, float m)
        {
            Vector3 t = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 bt = Vector3.Cross(n, t);
            int count = GroundArcsMin + _rng.Next(Mathf.Max(1, GroundArcsMax - GroundArcsMin + 1));
            float a0 = Rand(0f, 6.28f);
            for (int k = 0; k < count; k++)
            {
                float a = a0 + k * 6.28318f / count + Rand(-0.35f, 0.35f);
                Vector3 dir = t * Mathf.Cos(a) + bt * Mathf.Sin(a);
                float len = Rand(1.4f, 3.8f) * m * GroundArcReach;
                bool hop = Rand(0f, 1f) < 0.45f;
                List<Vector3> pts = ArcPath(c, dir, n, len, hop, m);
                AddArc(pts, width * Rand(0.25f, 0.4f), 1f);
                if (Rand(0f, 1f) < 0.5f && pts.Count > 6)
                {
                    int j = pts.Count / 2 + _rng.Next(pts.Count / 3);
                    j = Mathf.Clamp(j, 1, pts.Count - 2);
                    float a2 = a + Rand(0.4f, 0.9f) * (Rand(0f, 1f) < 0.5f ? -1f : 1f);
                    Vector3 d2 = t * Mathf.Cos(a2) + bt * Mathf.Sin(a2);
                    List<Vector3> fork = ArcPath(pts[j], d2, n, len * Rand(0.3f, 0.55f), false, m);
                    AddArc(fork, width * 0.22f, 0.7f);
                }
            }
        }

        private List<Vector3> ArcPath(Vector3 start, Vector3 dir, Vector3 n, float len, bool hop, float m)
        {
            _path.Clear();
            Vector3 s0 = start + n * 0.04f * m;
            _path.Add(s0);
            Subdivide(s0, s0 + dir * len, 5, 0.22f);
            List<Vector3> pts = new List<Vector3>(_path);
            for (int i = 0; i < pts.Count; i++)
            {
                float f = i / (pts.Count - 1f);
                Vector3 p = pts[i];
                p -= n * Vector3.Dot(p - s0, n);                                     // прижать к полу
                float lift = hop ? Mathf.Sin(f * Mathf.PI) * len * Rand(0.2f, 0.3f) : 0f; // рикошет: подпрыгивает дугой
                pts[i] = p + n * (0.04f * m + lift);
            }
            return pts;
        }

        private void AddArc(List<Vector3> p, float width, float bright)
        {
            int n = p.Count;
            if (n < 2) return;
            int baseI = _v.Count;
            Color c = new Color(1f, Rand(0f, 1f), 1f, bright);
            for (int i = 0; i < n; i++)
            {
                Vector3 T = p[Mathf.Min(i + 1, n - 1)] - p[Mathf.Max(i - 1, 0)];
                if (T.sqrMagnitude < 1e-8f) T = Vector3.forward;
                T.Normalize();
                float f = i / (n - 1f);
                float w = width * (1f - 0.8f * f);
                _v.Add(p[i]); _n.Add(T); _uv.Add(new Vector4(-1f, f, 6f, w)); _c.Add(c);
                _v.Add(p[i]); _n.Add(T); _uv.Add(new Vector4(1f, f, 6f, w)); _c.Add(c);
                if (i > 0)
                {
                    int a = baseI + (i - 1) * 2, b = baseI + i * 2;
                    _t.Add(a); _t.Add(b); _t.Add(a + 1);
                    _t.Add(a + 1); _t.Add(b); _t.Add(b + 1);
                }
            }
        }

        /// <summary>Светящийся диск на поверхности в точке удара (тип 5, вершины сразу в мире).</summary>
        private void AddGroundDisc(Vector3 c, Vector3 n, float radius, float alpha)
        {
            Vector3 t = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 bt = Vector3.Cross(n, t);
            int b = _v.Count;
            Color col = new Color(1f, Rand(0f, 1f), 1f, alpha);                                  // g — зерно рваного края
            Vector2[] k = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            for (int i = 0; i < 4; i++)
            {
                _v.Add(c + (t * k[i].x + bt * k[i].y) * radius); _n.Add(n); _uv.Add(new Vector4(k[i].x, k[i].y, 5f, radius)); _c.Add(col);
            }
            _t.Add(b); _t.Add(b + 1); _t.Add(b + 2);
            _t.Add(b); _t.Add(b + 2); _t.Add(b + 3);
        }

        // ─────────────── порывы ───────────────
        /// <summary>Порыв прямо сейчас (кнопка в меню P).</summary>
        public void GustNowStart() { StartGust(Mathf.Max(Windiness, 0.7f)); }

        private void StartGust(float windiness)
        {
            _gustT = 0f;
            _gustDur = Rand(2.5f, 5f);
            _gustPeak = Rand(0.8f, 1f) * Mathf.Lerp(0.7f, 1f, windiness);
            _gustNext = Mathf.Lerp(GustInterval.x, GustInterval.y, windiness) * Rand(0.6f, 1.4f) + _gustDur;
            // волна: фронт стартует с наветренной стороны и проходит через всю зону за время порыва
            float m = Meter();
            Camera cam = Camera.main;
            Vector3 f = cam != null ? FocusPoint(cam, m) : transform.position;
            _waveDir = CatCooksWeatherState.WindDir.sqrMagnitude > 1e-4f ? CatCooksWeatherState.WindDir.normalized : new Vector2(0.8f, 0.6f);
            float R = StreakRadius * m;
            _waveFront0 = f.x * _waveDir.x + f.z * _waveDir.y - R * 1.4f;
            _waveSpeed = R * 2.8f / Mathf.Max(0.5f, _gustDur);
        }

        private void UpdateGusts(float dt)
        {
            _gustNext -= dt;
            if (Windiness > 0.15f && _gustNext <= 0f) StartGust(Windiness);
            _gustT += dt;
            float env = _gustT < _gustDur ? Mathf.Pow(Mathf.Sin(Mathf.PI * _gustT / _gustDur), 1.5f) * _gustPeak : 0f;
            float bas = Mathf.PerlinNoise(Time.time * 0.25f, 0.71f) * Windiness * 0.3f;
            GustNow = Mathf.Clamp01(Mathf.Max(env, bas));

            float m = Meter();
            Vector2 wd = CatCooksWeatherState.WindDir.sqrMagnitude > 1e-4f ? CatCooksWeatherState.WindDir.normalized : new Vector2(0.8f, 0.6f);
            // заметно на всех уровнях: даже слабый порыв даёт линии (WeakGustVisibility), сильный — максимум
            float vis = Mathf.Lerp(Mathf.InverseLerp(0.15f, 0.5f, GustNow), Mathf.Clamp01(GustNow * 2.2f), WeakGustVisibility);
            float streaks = Mathf.Clamp01(vis * GustAmount);
            Camera cam = Camera.main;
            Vector3 focus = cam != null ? FocusPoint(cam, m) : transform.position;
            Shader.SetGlobalVector(IdGustFocus, new Vector4(focus.x, focus.y, focus.z, 0f));
            Shader.SetGlobalVector(IdGust, new Vector4(streaks, wd.x, wd.y, 0f));
            Shader.SetGlobalVector(IdGustP, new Vector4(StreakRadius * m, StreakLength * m, StreakWidth * m, Mathf.Max(0.3f, StreakLife)));
            bool waveOn = env > 0.01f && env >= bas;
            // направление волны — то, что было на старте порыва (ветер в шейдерах тот же, кроме резкой смены погоды)
            Shader.SetGlobalVector(IdGustWave, new Vector4(_waveFront0 + _waveSpeed * _gustT, WaveWidth * m, waveOn ? 1f : 0f, WaveBaseline));

            // пылинки: по таблице погоды, днём ярче, в дождь нет
            float motesT = _row != null ? _row.Motes : 0f;
            if (TestOverride) motesT = Mathf.Clamp01(1f - Windiness * 1.3f) * 0.8f;
            float day = Mathf.Clamp01(CatCooksWeatherState.Day01);
            float rain = CityRain.Instance != null ? CityRain.Instance.Intensity : Mathf.Clamp01(CatCooksWeatherState.Rain);
            motesT *= Mathf.Lerp(0.3f, 1f, day) * (1f - Mathf.Clamp01(rain * 3f)) * (1f - Mathf.Clamp01((GustNow - 0.3f) * 2f)) * MotesAmount;
            _motesSm = Mathf.MoveTowards(_motesSm, Mathf.Clamp01(motesT), dt / Mathf.Max(0.1f, WeatherBlendSeconds));
            Shader.SetGlobalVector(IdMotes, new Vector4(_motesSm, MotesRadius * m, MotesSize * m, 0f));

            // мусор: в порыв (и немного фоном в сильный ветер); листья: от ветрености, меняются медленно (лежащие тают плавно)
            // тонна мусора: погода или галка поверх любой погоды
            float trashT = Mathf.Max(TrashOverlay ? 1f : 0f, (!TestOverride && _row != null) ? _row.Trash : 0f) * JunkAmount;
            _trashSm = Mathf.MoveTowards(_trashSm, Mathf.Clamp01(trashT), dt / 3f);
            float vis2 = Mathf.Max(vis, _trashSm * 0.75f);
            float junk = Mathf.Clamp01(Mathf.Max(vis2, Windiness > 0.6f ? (Windiness - 0.6f) * 0.5f : 0f) * JunkAmount);
            // листья: от ветрености; в листопад — по таблице, даже без сильного ветра
            float rowLeaves = (!TestOverride && _row != null) ? _row.Leaves : 0f;
            _leafFall = rowLeaves > 0.01f;
            float leavesT = Windiness > 0.15f ? (0.25f + Windiness * 0.75f) : 0f;
            leavesT = Mathf.Clamp01(Mathf.Max(leavesT, rowLeaves) * LeavesAmount);
            _leavesSm = Mathf.MoveTowards(_leavesSm, leavesT, dt / 10f);
            int kind = (!TestOverride && _row != null) ? _row.LeafKind : 0;
            bool lie = TestOverride || _row == null || _row.LeavesLie;
            float kb = dt / Mathf.Max(0.1f, WeatherBlendSeconds);
            _leafKindAutumn = Mathf.MoveTowards(_leafKindAutumn, kind == 1 ? 1f : 0f, kb);
            _leafKindSummer = Mathf.MoveTowards(_leafKindSummer, kind == 2 ? 1f : 0f, kb);
            _leafLie = Mathf.MoveTowards(_leafLie, lie ? 1f : 0f, kb);
            float clouds = Mathf.Clamp01(vis2 * DustCloudAmount);
            Shader.SetGlobalVector(IdWindFx, new Vector4(junk, Mathf.Max(Windiness, _trashSm * 0.8f), _leavesSm, clouds));
            Shader.SetGlobalVector(IdWindFx2, new Vector4(_trashSm, _leafKindAutumn, _leafKindSummer, _leafLie));
            if (_trashSm > 0.01f)
            {
                // в тонну мусора — порывы видно всё время (линии, пыль, мусор)
                Vector4 gv = Shader.GetGlobalVector(IdGust);
                gv.x = Mathf.Max(gv.x, _trashSm * 0.7f * GustAmount);
                Shader.SetGlobalVector(IdGust, gv);
            }
        }

        // ─────────────── рисование ───────────────
        private void Draw()
        {
            Camera cam = Camera.main;
            Vector3 cp = cam != null ? cam.transform.position : transform.position;
            Bounds big = new Bounds(cp, Vector3.one * 1e5f);
            CityWeatherArea.Refresh();
            bool wxOn = CityWeatherArea.CamK > 0.02f;                       // вне зон погоды ветер-частицы (пыль, мусор, листья, линии) не рисуем; молния и небо остаются
            if (_age >= 0f && _mesh != null && LightningMaterial != null)
            {
                RenderParams rp = new RenderParams(LightningMaterial) { worldBounds = big, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderMesh(rp, _mesh, 0, Matrix4x4.identity);
            }
            if (_impactAge >= 0f && ImpactMaterial != null)
            {
                RenderParams ri = new RenderParams(ImpactMaterial) { worldBounds = big, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderPrimitives(ri, MeshTopology.Triangles, 6, 96);
            }
            if (wxOn && DebrisMaterial != null && MaxDebris > 0 && Shader.GetGlobalVector(IdGust).x > 0.01f)
            {
                RenderParams rd = new RenderParams(DebrisMaterial) { worldBounds = big, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderPrimitives(rd, MeshTopology.Triangles, 6, MaxDebris);
            }
            // в снег/метель пыль, тучки пыли и линии порывов не рисуем (белые полосы «метут» по полу); ветер видно по хлопьям и пелене
            bool snowing = CityWinter.Instance != null && (CityWinter.Instance.SnowNow > 0.05f || CityWinter.Instance.BlizzardNow > 0.05f || CityWinter.Instance.SnowCover > 0.05f);
            if (wxOn && DustMaterial != null && !snowing && MaxDust > 0 && Shader.GetGlobalVector(IdGust).x > 0.01f)
            {
                RenderParams rs = new RenderParams(DustMaterial) { worldBounds = big, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderPrimitives(rs, MeshTopology.Triangles, 6, MaxDust);
            }
            Vector4 fx = Shader.GetGlobalVector(IdWindFx);
            if (wxOn && JunkMaterial != null && MaxJunk > 0 && (fx.x > 0.01f || _trashSm > 0.01f))
            {
                RenderParams rj = new RenderParams(JunkMaterial) { worldBounds = big, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                int nj = _trashSm > 0.01f ? MaxJunk * Mathf.Max(1, TrashMultiplier) : MaxJunk;
                Graphics.RenderPrimitives(rj, MeshTopology.Triangles, 12, nj);
            }
            if (wxOn && LeavesMaterial != null && MaxLeaves > 0 && fx.z > 0.01f)
            {
                RenderParams rl = new RenderParams(LeavesMaterial) { worldBounds = big, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderPrimitives(rl, MeshTopology.Triangles, 6, _leafFall ? MaxLeaves * 2 : MaxLeaves);
            }
            if (wxOn && DustCloudMaterial != null && !snowing && MaxDustClouds > 0 && fx.w > 0.01f)
            {
                RenderParams rc = new RenderParams(DustCloudMaterial) { worldBounds = big, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderPrimitives(rc, MeshTopology.Triangles, 6, MaxDustClouds * 7);
            }
            if (wxOn && MotesMaterial != null && MaxMotes > 0 && _motesSm > 0.01f)
            {
                RenderParams rm = new RenderParams(MotesMaterial) { worldBounds = big, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderPrimitives(rm, MeshTopology.Triangles, 6, MaxMotes);
            }
            if (wxOn && StreakMaterial != null && !snowing && Shader.GetGlobalVector(IdGust).x > 0.01f)
            {
                RenderParams rp = new RenderParams(StreakMaterial) { worldBounds = big, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 32 * 6, Mathf.Max(1, MaxStreaks));
            }
        }

        // ─────────────── старые молнии Atmosphere ───────────────
        private static readonly string[] LegacyNames = { "CatCooksLightning", "CatCooksLightningBolt", "CatCooksLightningZones" };

        private void TickLegacy()
        {
            if (Time.time < _legacyCheck) return;
            bool first = _legacyCheck < 0f;
            _legacyCheck = Time.time + 5f;
            if (first)
            {
                _legacy.Clear();
                foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (mb != null && Array.IndexOf(LegacyNames, mb.GetType().Name) >= 0) _legacy.Add(mb);
            }
            int n = 0;
            foreach (MonoBehaviour mb in _legacy)
            {
                if (mb == null || !mb.enabled) continue;
                mb.enabled = false;            // только сам компонент (свет/солнце рядом не трогаем); Mood Director включит — гасим снова
                n++;
            }
            if (n > 0 && first && Log) Debug.Log("[Шторм] старые молнии Atmosphere выключены (компонентов: " + n + ") — работают молнии города", this);
        }
    }
}
