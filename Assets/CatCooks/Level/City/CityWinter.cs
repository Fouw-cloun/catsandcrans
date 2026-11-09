using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ЗИМА — заморозки, снег, буран (визуал).
    ///   • ЗАМОРОЗКИ: пол быстро покрывается пятнами льда (трещины, отражение, искорки, иней по краю), иней на всём;
    ///     на краях балок/навесов/зонтов растут сосульки (точки капели), у игрока — пар изо рта.
    ///   • СНЕГ: хлопья (кружат, сносятся ветром, под крышей и зонтом — нет), пол засыпает пятнами → сплошь,
    ///     снег на крышах и верхушках пропсов, игрок оставляет следы-лапки (постепенно засыпаются).
    ///   • БУРАН: густые хлопья полосами по ветру, белая пелена вдали, позёмка (пыль порывов становится снежной).
    /// v2: лёд по текстуре Ice004 с преломлением пола; снежинки-дендриты и пластинки; в буран — следы движения вместо
    ///     растянутых хлопьев, плотный ближний слой и пелена «почти ничего не видно»; снег ложится сугробами с рябью;
    ///     крупные чёткие следы-лапки (формула в шейдере, без пикселей карты).
    /// Сила — по таблице «Погода → зима» (Weather Table) или тест (меню P → Город → ЗИМА).
    /// Пол — тем же проходом, что мокрость (City Rain Surface), и только на отмеченном (City Wet Surface), если так включено.
    /// </summary>
    [ExecuteAlways]
    public class CityWinter : MonoBehaviour
    {
        public static CityWinter Instance { get; private set; }

        [Serializable]
        public class WeatherWinter
        {
            [Tooltip("Кусок имени/названия погоды (CW_19_Blizzard → blizzard)")] public string Weather;
            [Range(0f, 1f)] public float Frost;
            [Range(0f, 1f)] public float Snow;
            [Range(0f, 1f)] public float Blizzard;
            [Tooltip("Размер хлопьев (×)")] public float Flakes = 1f;
            public WeatherWinter() { }
            public WeatherWinter(string w, float f, float s, float b, float fl) { Weather = w; Frost = f; Snow = s; Blizzard = b; Flakes = fl; }
        }

        [Header("Материалы (ставит меню «Зима: поставить в сцену»)")]
        public Material SnowMaterial;
        public Material IcicleMaterial;
        public Material BreathMaterial;

        [Header("Погода → зима (не нашлась — тепло)")]
        public List<WeatherWinter> WeatherTable = DefaultTable();
        [Tooltip("Секунд на смену силы при смене погоды")]
        public float WeatherBlendSeconds = 6f;

        [Header("Тест (меню P)")]
        public bool TestOverride;
        [Range(0f, 1f)] public float TestFrost = 0.8f;
        [Range(0f, 1f)] public float TestSnow = 0.7f;
        [Range(0f, 1f)] public float TestBlizzard = 0f;

        [Header("Заморозки")]
        [Tooltip("За сколько секунд замерзает (резко!)")]
        public float FreezeSeconds = 8f;
        [Tooltip("За сколько секунд оттаивает")]
        public float ThawSeconds = 60f;
        [Range(0f, 1.5f)] public float IceAmount = 1f;
        [Tooltip("Размер пятен льда, м")]
        public float IceSize = 2.4f;
        [Range(0f, 1.5f)] public float Rime = 1f;
        [Range(0f, 1f)] public float Cracks = 1f;

        [Header("Лёд и снег: мультяшные текстуры (ставятся сами)")]
        [Tooltip("CC_Ice_Toon.png — плиты льда с фасками и царапинами-бликами")]
        public Texture2D IceToon;
        [Tooltip("CC_Snow_Toon.png — барханы-рябь: R высота, G свет, B гребни")]
        public Texture2D SnowToon;
        [Tooltip("Размер плитки снега, м (больше — крупнее барханы)")]
        public float SnowTile = 3f;
        [Tooltip("Размер плитки текстуры льда, м")]
        public float IceTile = 2.2f;
        [Tooltip("Непрозрачность: 0 — пол чуть просвечивает, 1 — плотный лёд")]
        [Range(0f, 1f)] public float IceCloudiness = 0.6f;
        [Tooltip("Блики-полоски на льду")]
        [Range(0f, 2f)] public float IceShine = 1f;
        [Tooltip("Цвет толщи льда")]
        public Color IceTint = new Color(0.55f, 0.8f, 0.95f, 1f);

        [Header("Сосульки")]
        [Tooltip("За сколько секунд вырастают")]
        public float IcicleGrowSeconds = 40f;
        [Tooltip("Макс. длина, м; толщина у основания, м")]
        public float IcicleLength = 0.45f, IcicleWidth = 0.06f;
        [Range(0f, 1f)] public float IcicleDrops = 0.35f;
        [Range(100, 4000)] public int MaxIciclePoints = 1500;
        public float UmbrellaIcicleSpacing = 0.3f;

        [Header("Пар изо рта")]
        public bool Breath = true;
        [Tooltip("Игрок (пусто — по тегу Player, иначе CharacterController, иначе камера)")]
        public Transform Player;
        [Tooltip("Рот относительно игрока, м (0 — сам по габариту: верх и вперёд)")]
        public Vector3 MouthOffset = Vector3.zero;
        [Tooltip("Выдох раз в … с")]
        public float BreathPeriod = 3f;
        [Tooltip("Размер клубка, м; жизнь, с")]
        public float BreathSize = 0.12f, BreathLife = 1.6f;
        [Tooltip("Размер пара при виде от первого лица (× от обычного)")]
        [Range(0.3f, 1.5f)] public float BreathFirstPerson = 1f;

        [Header("Снег")]
        [Tooltip("За сколько секунд сильного снега пол засыпает целиком")]
        public float AccumulateSeconds = 45f;
        [Tooltip("За сколько секунд тает, когда тепло")]
        public float MeltSeconds = 150f;
        [Range(1000, 40000)] public int MaxFlakes = 16000;
        [Tooltip("Коробка хлопьев вокруг камеры, м: ширина, высота")]
        public Vector2 FlakeBox = new Vector2(30f, 24f);
        [Tooltip("Размер хлопьев, м")]
        public float FlakeSize = 0.035f;
        [Tooltip("Скорость падения, м/с (снегопад / буран)")]
        public Vector2 FallSpeed = new Vector2(1.2f, 2.4f);
        [Tooltip("Доля крупных «витринных» снежинок вблизи (дендриты и пластинки)")]
        [Range(0f, 1f)] public float ShowcaseShare = 0.3f;

        [Header("Буран")]
        [Tooltip("Плотный слой хлопьев у самой камеры в буран")]
        [Range(0, 40000)] public int BlizzardNearFlakes = 18000;
        [Tooltip("Размер ближней коробки (× основной)")]
        [Range(0.15f, 1f)] public float NearBoxScale = 0.35f;
        [Tooltip("Выдержка следа хлопьев, с: длиннее — длиннее хвосты в буран")]
        public float StreakShutter = 0.06f;

        [Header("Видимость (пелена)")]
        [Tooltip("Видимость в снегопад, м; сколько дымки")]
        public float SnowfallVisibility = 160f;
        [Range(0f, 1f)] public float SnowfallHaze = 0.55f;
        [Tooltip("Видимость в буран, м; сколько пелены (1 — совсем белое)")]
        public float BlizzardVisibility = 9f;
        [Range(0f, 1f)] public float BlizzardHaze = 0.97f;
        [Tooltip("Тени от солнца на снегу и льду (0 — выкл, если вдруг мешают)")]
        [Range(0f, 1f)] public float SnowShadows = 1f;

        [Header("Сугробы")]
        [Tooltip("Рельеф: насколько неровно лежит снег")]
        [Range(0f, 2f)] public float DriftRelief = 1f;
        [Tooltip("Рябь от ветра поперёк направления ветра")]
        [Range(0f, 2f)] public float DriftRipples = 1f;
        [Tooltip("Позёмка по снегу (кроме бурана, где она всегда)")]
        [Range(0f, 1f)] public float GroundDrift = 0.3f;

        [Header("Снег на полу: ровный слой ↔ барханы")]
        [Tooltip("За сколько секунд метели снег на полу собирается в барханы")]
        public float DriftBuildSeconds = 25f;
        [Tooltip("За сколько секунд тихого снегопада барханы заносит ровным слоем")]
        public float DriftFadeSeconds = 90f;

        [Header("Сугробы-кучки (настоящие 3D, растут в метель)")]
        public bool Piles = true;
        [Tooltip("Материал кучек (меню «Зима: поставить в сцену» создаёт CC_SnowPile)")]
        public Material PileMaterial;
        [Range(0, 40)] public int PileCount = 14;
        [Tooltip("Радиус вокруг игрока, где ставить кучки, м")]
        public float PileArea = 9f;
        [Tooltip("Диаметр кучки, м (от и до)")]
        public Vector2 PileSize = new Vector2(0.5f, 1.4f);
        [Tooltip("Высота кучки к диаметру")]
        [Range(0.1f, 0.8f)] public float PileHeight = 0.38f;
        [Tooltip("За сколько секунд метели кучки вырастают полностью")]
        public float PileGrowSeconds = 50f;

        [Header("Следы-лапки")]
        public bool Footprints = true;
        [Tooltip("Ширина лапы, м (0 — по размеру игрока, крупно)")]
        public float PawSize = 0f;
        [Tooltip("Шаг между следами, м (0 — по размеру игрока)")]
        public float PrintStride = 0f;
        [Range(0.2f, 1.5f)] public float PrintDepth = 1f;
        [Tooltip("За сколько секунд снегопада след засыпает")]
        public float PrintFillSeconds = 40f;

        public bool Log = true;

        // состояние
        public float Frost { get; private set; }
        public float SnowNow { get; private set; }
        public float SnowCover { get; private set; }
        public float BlizzardNow { get; private set; }
        public float IcicleGrow { get; private set; }

        private WeatherWinter _row;
        private CatCooksWeatherPreset _lastPreset;
        private float _frostT, _snowT, _blizT, _flakeMul = 1f;
        private Vector3 _fall;
        private Vector2 _windSm;
        private readonly CityRainDrips _points = new CityRainDrips();
        private int _flakesMain = 16000, _flakesNear;

        // пар
        private readonly Vector4[] _breath = new Vector4[16];
        private readonly Vector4[] _breathV = new Vector4[16];
        private int _breathI;
        private float _nextBreath, _puffsLeft, _nextPuff;
        private Vector3 _mouthLocal; private bool _mouthOk; private float _mouthCheck;
        private bool _breathFP;   // вид от первого лица — пар меньше и прозрачнее

        // следы-лапки: кольцо последних 48 (xy позиция XZ, z угол, w засыпано)
        public const int MaxPaws = 48;
        private readonly Vector4[] _paws = new Vector4[MaxPaws];
        private int _pawN, _pawI;
        private Vector3 _lastFoot;
        private bool _footInit;
        private int _side = 1;
        private float _playerW = 0.4f;
        private Vector2 _drift = new Vector2(0.8f, 0.6f);
        private float _driftAmt;

        // кучки
        public float PileGrow { get; private set; }
        private readonly List<Transform> _piles = new List<Transform>();
        private readonly List<Vector3> _pileBase = new List<Vector3>();   // x диаметр, y высота, z вытянутость
        private Mesh[] _pileMeshes;
        private Material _pileMatRT;

        static readonly int IdWinterOn = Shader.PropertyToID("_CC_WinterOn");
        static readonly int IdFrost = Shader.PropertyToID("_CC_Frost");
        static readonly int IdFrostP = Shader.PropertyToID("_CC_FrostP");
        static readonly int IdSnowCover = Shader.PropertyToID("_CC_SnowCover");
        static readonly int IdSnowNow = Shader.PropertyToID("_CC_SnowNow");
        static readonly int IdBlizzard = Shader.PropertyToID("_CC_Blizzard");
        static readonly int IdSnowP = Shader.PropertyToID("_CC_SnowP");
        static readonly int IdSnowP2 = Shader.PropertyToID("_CC_SnowP2");
        static readonly int IdSnowFall = Shader.PropertyToID("_CC_SnowFall");
        static readonly int IdSnowWind = Shader.PropertyToID("_CC_SnowWind");
        static readonly int IdIcicles = Shader.PropertyToID("_CC_Icicles");
        static readonly int IdBreath = Shader.PropertyToID("_CC_Breath");
        static readonly int IdBreathV = Shader.PropertyToID("_CC_BreathV");
        static readonly int IdBreathP = Shader.PropertyToID("_CC_BreathP");
        static readonly int IdSnowP3 = Shader.PropertyToID("_CC_SnowP3");
        static readonly int IdSnowVis = Shader.PropertyToID("_CC_SnowVis");
        static readonly int IdDriftP = Shader.PropertyToID("_CC_DriftP");
        static readonly int IdPaws = Shader.PropertyToID("_CC_Paws");
        static readonly int IdPawP = Shader.PropertyToID("_CC_PawP");
        static readonly int IdIceP = Shader.PropertyToID("_CC_IceP");
        static readonly int IdIceP2 = Shader.PropertyToID("_CC_IceP2");
        static readonly int IdIceTint = Shader.PropertyToID("_CC_IceTint");
        static readonly int IdIceToon = Shader.PropertyToID("_CC_IceToon");
        static readonly int IdSnowToon = Shader.PropertyToID("_CC_SnowToon");
        static readonly int IdSnowTex = Shader.PropertyToID("_CC_SnowTex");

        // GUID мультяшной текстуры льда из пакета (Textures/CC_Ice_Toon.png) — найдётся, где бы ни лежала папка
        public const string GuidIceToon = "f1c3a9e2b7d84c51a0e6c4d2b9a71e05";
        public const string GuidSnowToon = "f1c3a9e2b7d84c51a0e6c4d2b9a71e06";
        static readonly int IdMeter = Shader.PropertyToID("_CC_Meter");

        public static List<WeatherWinter> DefaultTable()
        {
            return new List<WeatherWinter>
            {
                new WeatherWinter("flashfreeze", 1f, 0f, 0f, 1f),     // резкие заморозки: лёд, сосульки, пар
                new WeatherWinter("blizzard", 0.6f, 1f, 1f, 0.8f),    // метель / буран
                new WeatherWinter("snowfall", 0.3f, 0.65f, 0f, 1.4f), // снегопад: крупные хлопья, тихо
                new WeatherWinter("snowclouds", 0.35f, 0.3f, 0f, 1f), // снежные тучи: редкие хлопья
            };
        }

        private void MergeTable()
        {
            if (WeatherTable == null) WeatherTable = new List<WeatherWinter>();
            foreach (WeatherWinter d in DefaultTable())
            {
                bool has = false;
                foreach (WeatherWinter r in WeatherTable) if (r != null && r.Weather == d.Weather) { has = true; break; }
                if (!has) WeatherTable.Add(d);
            }
        }

        public WeatherWinter ForWeather(CatCooksWeatherPreset w)
        {
            if (w == null || WeatherTable == null) return null;
            string key = (w.name + " " + w.title).ToLowerInvariant().Replace("_", "");
            foreach (WeatherWinter r in WeatherTable)
                if (r != null && !string.IsNullOrEmpty(r.Weather) && key.Contains(r.Weather.ToLowerInvariant().Replace("_", ""))) return r;
            return null;
        }

        private void OnEnable()
        {
            Instance = this; MergeTable(); _footInit = false;
#if UNITY_EDITOR
            if (AutoTextures() && !Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

#if UNITY_EDITOR
        // В редакторе Unity компилирует шейдеры в фоне и пока рисует голубую заглушку (весь экран синий секунды).
        // Когда зима включается впервые, на несколько кадров компилируем сразу: одна короткая пауза вместо синего экрана.
        private int _syncFrames = -1;
        private bool _asyncWas = true;

        private void EditorSyncCompile(bool on)
        {
            if (!Application.isPlaying) return;
            if (on && _syncFrames < 0)
            {
                _asyncWas = UnityEditor.ShaderUtil.allowAsyncCompilation;
                UnityEditor.ShaderUtil.allowAsyncCompilation = false;
                _syncFrames = 6;
                if (Log) Debug.Log("[Зима] первая загрузка шейдеров зимы — компилирую сразу (в редакторе возможна короткая пауза, в сборке её нет)", this);
            }
            else if (_syncFrames > 0)
            {
                _syncFrames--;
                if (_syncFrames == 0) UnityEditor.ShaderUtil.allowAsyncCompilation = _asyncWas;
            }
        }

        /// <summary>Подставить текстуры льда из пакета (по GUID), если не стоят. true — что-то поставили.</summary>
        public bool AutoTextures()
        {
            bool any = false;
            if (IceToon == null) { IceToon = TexByGuid(GuidIceToon); any |= IceToon != null; }
            if (SnowToon == null) { SnowToon = TexByGuid(GuidSnowToon); any |= SnowToon != null; }
            if (any && Log) Debug.Log("[Зима] мультяшные текстуры подставлены: лёд " + (IceToon != null ? "есть" : "нет") + ", снег " + (SnowToon != null ? "есть" : "нет"), this);
            return any;
        }

        private static Texture2D TexByGuid(string guid)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
#endif

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
#if UNITY_EDITOR
            if (_syncFrames > 0) { UnityEditor.ShaderUtil.allowAsyncCompilation = _asyncWas; _syncFrames = 0; }
#endif
            Shader.SetGlobalFloat(IdWinterOn, 0f);
            Shader.SetGlobalFloat(IdFrost, 0f);
            Shader.SetGlobalFloat(IdSnowCover, 0f);
            Shader.SetGlobalFloat(IdSnowNow, 0f);
            Shader.SetGlobalFloat(IdBlizzard, 0f);
            Shader.SetGlobalVector(IdIcicles, Vector4.zero);
            Shader.SetGlobalVector(IdBreathP, Vector4.zero);
            Shader.SetGlobalVector(IdPawP, Vector4.zero);
            Shader.SetGlobalVector(IdSnowVis, Vector4.zero);
            _points.Release();
            DestroyPiles();
        }

        private float Meter()
        {
            if (CityRain.Instance != null) return CityRain.Instance.Meter;
            float m = Shader.GetGlobalFloat(IdMeter);
            return m > 0f ? m : 3.5f;
        }

        // ── кнопки меню P ──
        public void FreezeNow() { Frost = 1f; IcicleGrow = Mathf.Max(IcicleGrow, 0.6f); }
        public void CoverNow() { SnowCover = 1f; }
        public void MeltNow() { Frost = 0f; SnowCover = 0f; IcicleGrow = 0f; _driftAmt = 0f; PileGrow = 0f; ClearPaws(); DestroyPiles(); }

        private void Update()
        {
            float dt = Application.isPlaying ? Time.deltaTime : 0f;
            float m = Meter();
            CityWeatherArea.Refresh();

            // цели по погоде
            CatCooksWeatherPreset cur = CatCooksWeatherState.Current;
            if (cur != _lastPreset) { _lastPreset = cur; _row = ForWeather(cur); }
            float k = dt / Mathf.Max(0.1f, WeatherBlendSeconds);
            _frostT = Mathf.MoveTowards(_frostT, TestOverride ? TestFrost : (_row != null ? _row.Frost : 0f), Application.isPlaying ? k : 1f);
            _snowT = Mathf.MoveTowards(_snowT, TestOverride ? TestSnow : (_row != null ? _row.Snow : 0f), Application.isPlaying ? k : 1f);
            _blizT = Mathf.MoveTowards(_blizT, TestOverride ? TestBlizzard : (_row != null ? _row.Blizzard : 0f), Application.isPlaying ? k : 1f);
            _flakeMul = Mathf.MoveTowards(_flakeMul, _row != null && !TestOverride ? _row.Flakes : 1f, k);

            if (Application.isPlaying)
            {
                // заморозки: резко замерзает, долго тает
                if (Frost < _frostT) Frost = Mathf.Min(_frostT, Frost + dt / Mathf.Max(0.5f, FreezeSeconds));
                else if (_snowT < 0.05f && SnowCover < 0.1f) Frost = Mathf.Max(_frostT, Frost - dt / Mathf.Max(1f, ThawSeconds));
                // пока идёт или лежит снег — холодно: лёд не тает, снег ложится поверх
                SnowNow = Mathf.MoveTowards(SnowNow, _snowT, dt / 4f);
                BlizzardNow = Mathf.MoveTowards(BlizzardNow, _blizT, dt / 4f);
                // снег копится до предела этого снега; тепло — тает
                float cap = Mathf.Clamp01(SnowNow * 1.25f);
                if (SnowNow > 0.01f && SnowCover < cap) SnowCover = Mathf.Min(cap, SnowCover + dt * SnowNow / Mathf.Max(1f, AccumulateSeconds));
                else if (SnowNow <= 0.01f && Frost < 0.3f) SnowCover = Mathf.Max(0f, SnowCover - dt / Mathf.Max(1f, MeltSeconds));
                // сосульки: в мороз растут, в тепло тают
                bool cold = Frost > 0.3f || SnowNow > 0.3f;
                IcicleGrow = Mathf.MoveTowards(IcicleGrow, cold ? 1f : 0f, dt / Mathf.Max(1f, cold ? IcicleGrowSeconds : ThawSeconds));
            }
            else
            {
                Frost = TestOverride ? TestFrost : Frost; SnowNow = TestOverride ? TestSnow : SnowNow;
            }

            bool on = Frost > 0.002f || SnowCover > 0.002f || SnowNow > 0.002f || BlizzardNow > 0.002f;
#if UNITY_EDITOR
            EditorSyncCompile(on);
#endif
            Shader.SetGlobalFloat(IdWinterOn, on ? 1f : 0f);
            Shader.SetGlobalFloat(IdFrost, Frost);
            Shader.SetGlobalVector(IdFrostP, new Vector4(IceAmount, IceSize * m, Rime, Cracks));
            Shader.SetGlobalFloat(IdSnowCover, SnowCover);
            Shader.SetGlobalFloat(IdSnowNow, SnowNow);
            Shader.SetGlobalFloat(IdBlizzard, BlizzardNow);
            if (!on && IcicleGrow <= 0.001f) { Shader.SetGlobalVector(IdBreathP, Vector4.zero); Shader.SetGlobalVector(IdSnowVis, Vector4.zero); return; }

            PushAmbient();
            PushIce(m);
            PushSnow(dt, m);
            PushIcicles(dt, m);
            if (Application.isPlaying)
            {
                UpdateBreath(dt, m);
                UpdatePrints(dt, m);
                UpdatePiles(dt, m);
            }
            Draw(m);
        }

        // ── снегопад ──
        private void PushSnow(float dt, float m)
        {
            float bl = BlizzardNow;
            Vector2 wd = CatCooksWeatherState.WindDir.sqrMagnitude > 1e-4f ? CatCooksWeatherState.WindDir.normalized : new Vector2(0.8f, 0.6f);
            float windMs = (0.4f + CatCooksWeatherState.Wind * 3f) * (1f + CityStorm.GustNow * 1.5f) * Mathf.Lerp(0.6f, 3.2f, bl);
            Vector2 wt = wd * windMs * m;
            _windSm = Vector2.Lerp(_windSm, wt, 1f - Mathf.Exp(-Mathf.Max(dt, 0.016f) * 1.5f));
            float fall = Mathf.Lerp(FallSpeed.x, FallSpeed.y, bl) * m;
            _fall += new Vector3(_windSm.x, -fall, _windSm.y) * dt;
            if (Mathf.Abs(_fall.x) > 1e5f || Mathf.Abs(_fall.y) > 1e5f || Mathf.Abs(_fall.z) > 1e5f) _fall = Vector3.zero;
            float share = Mathf.Clamp01(SnowNow * (1f + bl * 0.6f));
            float nearShare = Mathf.Clamp01(bl * 1.2f) * Mathf.Clamp01(SnowNow * 1.5f) * 0.45f;   // ближний слой — пореже, видимость даёт пелена
            // зоны погоды: в жёлтой хлопьев-экземпляров втрое меньше и реже; вне зон снега в воздухе нет
            float qk = CityWeatherArea.CamFull > 0.5f ? 1f : 0.35f;
            float thin = Mathf.Min(1f, CityWeatherArea.CamK / qk);
            _flakesMain = Mathf.Max(1, Mathf.RoundToInt(MaxFlakes * qk));
            _flakesNear = BlizzardNow > 0.01f ? Mathf.Max(0, Mathf.RoundToInt(BlizzardNearFlakes * qk)) : 0;
            share *= thin; nearShare *= thin;
            Shader.SetGlobalVector(IdSnowP, new Vector4(share, fall, FlakeSize * _flakeMul * Mathf.Lerp(1f, 0.75f, bl) * m, FlakeBox.x * m));
            Shader.SetGlobalVector(IdSnowP2, new Vector4(FlakeBox.y * m, 0.35f * m * (1f - bl * 0.6f), bl, _flakesMain));
            Shader.SetGlobalVector(IdSnowP3, new Vector4(nearShare, 0.012f + Mathf.Max(0f, StreakShutter) * bl, NearBoxScale, ShowcaseShare * (1f - bl)));
            Shader.SetGlobalVector(IdSnowFall, new Vector4(_fall.x, _fall.y, _fall.z, 0f));
            Shader.SetGlobalVector(IdSnowWind, new Vector4(_windSm.x, _windSm.y, 0f, 0f));

            // снег на полу: метель собирает барханы, тихий снегопад заносит их ровным слоем
            if (Application.isPlaying)
            {
                if (SnowCover < 0.03f) _driftAmt = 0f;
                else if (bl > 0.15f) _driftAmt = Mathf.Min(1f, _driftAmt + dt * bl / Mathf.Max(1f, DriftBuildSeconds));
                else if (SnowNow > 0.05f) _driftAmt = Mathf.Max(0f, _driftAmt - dt / Mathf.Max(1f, DriftFadeSeconds));
            }
            else _driftAmt = bl > 0.15f ? 1f : 0f;

            // пелена: снегопад — дымка, буран — почти ничего не видно (видимость падает плавно, по экспоненте)
            float visA = Mathf.Max(1f, SnowfallVisibility), visB = Mathf.Max(1f, BlizzardVisibility);
            float vis = visA * Mathf.Pow(visB / visA, Mathf.Clamp01(bl));
            float haze = Mathf.Max(Mathf.Clamp01(SnowNow) * SnowfallHaze, bl * BlizzardHaze);
            Shader.SetGlobalVector(IdSnowVis, new Vector4(vis * m, haze, 0f, SnowShadows));   // позёмку убрали — странно смотрелась

            // сугробы: направление ветра меняется медленно, чтобы рисунок на снегу не «ехал»
            _drift = Vector2.Lerp(_drift, wd, Application.isPlaying ? 1f - Mathf.Exp(-dt / 30f) : 1f);
            Vector2 dn = _drift.sqrMagnitude > 1e-4f ? _drift.normalized : new Vector2(0.8f, 0.6f);
            Shader.SetGlobalVector(IdDriftP, new Vector4(dn.x, dn.y, DriftRelief * 0.8f, DriftRipples));
        }

        // ── окружение для снега на полу и кучек: один и тот же свет неба (в полноэкранном проходе своего SH нет) ──
        private static readonly Vector3[] _upDir = { Vector3.up };
        private static readonly Color[] _ambRes = new Color[1];
        static readonly int IdAmb = Shader.PropertyToID("_CC_Amb");

        private void PushAmbient()
        {
            RenderSettings.ambientProbe.Evaluate(_upDir, _ambRes);
            Color a = _ambRes[0];
            Shader.SetGlobalVector(IdAmb, new Vector4(Mathf.Max(0f, a.r), Mathf.Max(0f, a.g), Mathf.Max(0f, a.b), 1f));
        }

        // ── лёд ──
        private void PushIce(float m)
        {
            bool tex = IceToon != null;
            if (tex) Shader.SetGlobalTexture(IdIceToon, IceToon);
            Shader.SetGlobalVector(IdIceP, new Vector4(Mathf.Max(0.05f, IceTile) * m, 0f, 0f, IceCloudiness));
            Shader.SetGlobalVector(IdIceP2, new Vector4(tex ? IceToon.width : 512f, tex ? 1f : 0f, 0f, IceShine));
            if (SnowToon != null) Shader.SetGlobalTexture(IdSnowToon, SnowToon);
            Shader.SetGlobalVector(IdSnowTex, new Vector4(Mathf.Max(0.1f, SnowTile) * m, SnowToon != null ? SnowToon.width : 1024f, SnowToon != null ? 1f : 0f, _driftAmt));
            Shader.SetGlobalVector(IdIceTint, new Vector4(IceTint.r, IceTint.g, IceTint.b, IceTint.a));
        }

        // ── сосульки ──
        private void PushIcicles(float dt, float m)
        {
            Shader.SetGlobalVector(IdIcicles, new Vector4(IcicleGrow, IcicleLength * m, IcicleWidth * m, IcicleDrops * (Frost < 0.9f ? 1f : 0.3f)));
            if (IcicleGrow > 0.001f) _points.Tick(Mathf.Max(dt, 0.05f), m, MaxIciclePoints, UmbrellaIcicleSpacing, Physics.DefaultRaycastLayers);
        }

        // ── пар изо рта ──
        private Transform FindPlayer()
        {
            if (Player != null) return Player;
            GameObject go = null;
            try { go = GameObject.FindWithTag("Player"); } catch (UnityException) { }
            if (go != null) { Player = go.transform; return Player; }
            CharacterController cc = FindFirstObjectByType<CharacterController>();
            if (cc != null) { Player = cc.transform; return Player; }
            return Camera.main != null ? Camera.main.transform : null;
        }

        private bool MouthWorld(Transform p, float m, out Vector3 mouth, out Vector3 fwd)
        {
            mouth = p.position; fwd = p.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            if (Camera.main != null && p == Camera.main.transform) { mouth = p.position + p.forward * 0.4f * m - Vector3.up * 0.15f * m; return true; }
            if (MouthOffset != Vector3.zero) { mouth = p.TransformPoint(MouthOffset * m); return true; }
            if (Time.time >= _mouthCheck)
            {
                _mouthCheck = Time.time + 2f;
                Bounds b = new Bounds(p.position, Vector3.zero); bool any = false;
                foreach (Collider c in p.GetComponentsInChildren<Collider>()) { if (c.isTrigger) continue; if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds); }
                if (!any) foreach (Renderer r in p.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                _mouthOk = any;
                if (any)
                {
                    _playerW = Mathf.Max(b.size.x, b.size.z) / m;
                    Vector3 lp = p.InverseTransformPoint(b.center + Vector3.up * b.extents.y * 0.7f);
                    _mouthLocal = lp + Vector3.forward * Mathf.Max(b.extents.x, b.extents.z) / Mathf.Max(p.lossyScale.z, 1e-3f) * 0.9f;
                }
            }
            if (_mouthOk) mouth = p.TransformPoint(_mouthLocal);
            return true;
        }

        private void UpdateBreath(float dt, float m)
        {
            float cold = Mathf.Clamp01(Mathf.Max(Frost, SnowNow * 0.8f, SnowCover * 0.6f));
            float t = Time.timeSinceLevelLoad;
            Shader.SetGlobalVector(IdBreathP, new Vector4(BreathSize * m * (_breathFP ? BreathFirstPerson : 1f), BreathLife, Breath ? Mathf.Clamp01(cold * 1.5f) : 0f, t));
            if (!Breath || cold < 0.15f) return;
            Transform p = FindPlayer();
            if (p == null) return;
            if (t >= _nextBreath)
            {
                _nextBreath = t + BreathPeriod * UnityEngine.Random.Range(0.8f, 1.2f);
                _puffsLeft = 4; _nextPuff = t;
            }
            if (_puffsLeft > 0 && t >= _nextPuff)
            {
                Vector3 mouth, fwd;
                MouthWorld(p, m, out mouth, out fwd);
                Camera cm = Camera.main;
                _breathFP = cm != null && (mouth - cm.transform.position).magnitude < 0.9f * m;
                if (_breathFP)
                {   // вид от первого лица / камера у самой головы: пар чуть впереди и ниже глаз, а не облаком на весь экран
                    Transform ct = cm.transform;
                    mouth = ct.position + ct.forward * 0.7f * m - ct.up * 0.3f * m;
                    fwd = ct.forward;
                }
                Vector3 v = (fwd * UnityEngine.Random.Range(0.35f, 0.6f) + UnityEngine.Random.insideUnitSphere * 0.08f) * m;
                _breath[_breathI] = new Vector4(mouth.x, mouth.y, mouth.z, t);
                _breathV[_breathI] = new Vector4(v.x, v.y, v.z, 0f);
                _breathI = (_breathI + 1) % 16;
                _puffsLeft--; _nextPuff = t + 0.08f;
                Shader.SetGlobalVectorArray(IdBreath, _breath);
                Shader.SetGlobalVectorArray(IdBreathV, _breathV);
            }
        }

        // ── следы-лапки (в шейдере — формулой: подушечка с тремя долями + 4 пальчика) ──
        private void ClearPaws()
        {
            _pawN = 0; _pawI = 0;
            Array.Clear(_paws, 0, _paws.Length);
            Shader.SetGlobalVector(IdPawP, Vector4.zero);
        }

        private void AddPaw(Vector2 xz, Vector2 dir)
        {
            _paws[_pawI] = new Vector4(xz.x, xz.y, Mathf.Atan2(dir.x, dir.y), 0f);
            _pawI = (_pawI + 1) % MaxPaws;
            _pawN = Mathf.Min(MaxPaws, _pawN + 1);
        }

        private void UpdatePrints(float dt, float m)
        {
            if (!Footprints || SnowCover < 0.03f)
            {
                if (SnowCover < 0.01f && _pawN > 0) ClearPaws();
                Shader.SetGlobalVector(IdPawP, Vector4.zero);
                _footInit = false;
                return;
            }
            Transform p = FindPlayer();
            if (p == null || (Camera.main != null && p == Camera.main.transform)) { Shader.SetGlobalVector(IdPawP, Vector4.zero); return; }
            Vector3 mouth, fwd;
            MouthWorld(p, m, out mouth, out fwd);                                       // заодно габарит игрока
            Vector3 pos = p.position;
            float paw = (PawSize > 0f ? PawSize : Mathf.Clamp(_playerW * 0.3f, 0.08f, 0.25f)) * m;
            float stride = (PrintStride > 0f ? PrintStride : Mathf.Max(0.15f, _playerW * 0.6f)) * m;
            if (!_footInit) { _lastFoot = pos; _footInit = true; }
            Vector3 d = pos - _lastFoot; d.y = 0f;
            bool grounded = true;
            CharacterController cc = p.GetComponent<CharacterController>();
            if (cc != null) grounded = cc.isGrounded;
            if (d.magnitude >= stride)
            {
                if (grounded && Mathf.Abs(pos.y - _lastFoot.y) < stride)
                {
                    Vector2 dir = new Vector2(d.x, d.z).normalized;
                    Vector2 side = new Vector2(dir.y, -dir.x);
                    AddPaw(new Vector2(pos.x, pos.z) + side * _side * paw * 0.45f, dir);   // кошка ставит лапы почти в линию
                    _side = -_side;
                }
                _lastFoot = pos;
            }
            // снегопад понемногу засыпает следы
            if (SnowNow > 0.01f && dt > 0f)
            {
                float k = dt * SnowNow / Mathf.Max(1f, PrintFillSeconds);
                for (int i = 0; i < MaxPaws; i++) _paws[i].w = Mathf.Min(1f, _paws[i].w + k);
            }
            Shader.SetGlobalVectorArray(IdPaws, _paws);
            Shader.SetGlobalVector(IdPawP, new Vector4(paw, _pawN, PrintDepth, 0f));
        }

        // ── сугробы-кучки: настоящие 3D-модели, растут в метель, остаются, пока лежит снег ──
        private void UpdatePiles(float dt, float m)
        {
            if (!Piles || PileCount <= 0) { if (_piles.Count > 0) DestroyPiles(); PileGrow = 0f; return; }
            if (CityWeatherArea.CamFull < 0.5f) { if (_piles.Count > 0) DestroyPiles(); return; }   // сугробы-кучки — только в красной зоне
            if (BlizzardNow > 0.1f && SnowCover > 0.05f) PileGrow = Mathf.Min(1f, PileGrow + dt * BlizzardNow / Mathf.Max(1f, PileGrowSeconds));
            PileGrow = Mathf.Min(PileGrow, Mathf.Clamp01(SnowCover * 1.3f));            // тает вместе со снегом
            if (PileGrow > 0.01f && _piles.Count == 0) SpawnPiles(m);
            if (PileGrow <= 0.002f) { if (_piles.Count > 0) DestroyPiles(); return; }
            float g = PileGrow;
            for (int i = 0; i < _piles.Count; i++)
            {
                if (_piles[i] == null) continue;
                Vector3 b = _pileBase[i];
                float w = b.x * (0.35f + 0.65f * g);
                _piles[i].localScale = new Vector3(w, b.y * g, w * b.z);
            }
        }

        private Material PileMat()
        {
            if (PileMaterial != null) return PileMaterial;
            if (_pileMatRT == null)
            {
                Shader sh = Shader.Find("CatCooks/City/Snow Pile");
                if (sh == null) return null;
                _pileMatRT = new Material(sh) { name = "CC_SnowPile (временный)", hideFlags = HideFlags.DontSave };
                if (Log) Debug.LogWarning("[Зима] нет материала кучек — создал временный. Нажми «Зима: поставить в сцену», чтобы был постоянный (нужен для сборки).", this);
            }
            return _pileMatRT;
        }

        private void SpawnPiles(float m)
        {
            Transform p = FindPlayer();
            Material mat = PileMat();
            if (p == null || mat == null) return;
            if (_pileMeshes == null)
            {
                _pileMeshes = new Mesh[4];
                for (int i = 0; i < 4; i++) _pileMeshes[i] = BuildPileMesh(i * 7 + 3);
            }
            Vector3 c = p.position;
            Vector3 wind = new Vector3(_drift.x, 0f, _drift.y);
            if (wind.sqrMagnitude < 1e-4f) wind = Vector3.forward;
            int placed = 0;
            for (int t = 0; t < PileCount * 8 && placed < PileCount; t++)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle * PileArea * m;
                if (r.magnitude < 1.3f * m) continue;                                     // не под ногами у игрока
                Vector3 from = c + new Vector3(r.x, 6f * m, r.y);
                RaycastHit hit;
                if (!Physics.Raycast(from, Vector3.down, out hit, 12f * m, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.92f) continue;                                        // только ровный пол
                float d = UnityEngine.Random.Range(PileSize.x, PileSize.y) * m;
                float elong = UnityEngine.Random.Range(1.1f, 1.6f);
                // место свободно: рядом нет стен и предметов
                float rad = d * 0.5f * elong;
                Vector3 ctr = hit.point + Vector3.up * rad * 0.5f;
                Collider[] near = Physics.OverlapSphere(ctr, rad, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                bool free = true;
                foreach (Collider col in near)
                {
                    if (col == hit.collider) continue;
                    if (col.bounds.max.y <= hit.point.y + 0.04f * m) continue;              // соседние плиты пола — не помеха
                    free = false; break;
                }
                if (!free) continue;
                if (Physics.Raycast(hit.point + Vector3.up * 0.1f * m, Vector3.up, 25f * m, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;   // под крышей не наметает
                GameObject go = new GameObject("CC_SnowPile_" + placed) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, true);
                go.transform.position = hit.point - Vector3.up * 0.01f * m;
                go.transform.rotation = Quaternion.LookRotation(wind) * Quaternion.Euler(0f, UnityEngine.Random.Range(-25f, 25f), 0f);
                go.AddComponent<MeshFilter>().sharedMesh = _pileMeshes[placed % 4];
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _piles.Add(go.transform);
                _pileBase.Add(new Vector3(d, d * PileHeight * UnityEngine.Random.Range(0.8f, 1.2f), elong));
                placed++;
            }
            if (Log) Debug.Log("[Зима] сугробы-кучки: поставлено " + placed + " из " + PileCount + (placed == 0 ? " (не нашлось ровного свободного пола с коллайдером под открытым небом)" : ""), this);
        }

        private void DestroyPiles()
        {
            foreach (Transform t in _piles)
                if (t != null) { if (Application.isPlaying) Destroy(t.gameObject); else DestroyImmediate(t.gameObject); }
            _piles.Clear();
            _pileBase.Clear();
        }

        /// <summary>Купол сугроба: диаметр 1, высота 1; неровный край, пологая наветренная и крутая подветренная сторона.</summary>
        private static Mesh BuildPileMesh(int seed)
        {
            const int rings = 12, seg = 32;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float sd = seed * 1.37f;
            verts.Add(new Vector3(0f, 1f, 0.08f));
            for (int r = 1; r <= rings; r++)
            {
                float t = (float)r / rings;
                for (int k = 0; k < seg; k++)
                {
                    float a = k * Mathf.PI * 2f / seg;
                    float wob = 1f + 0.12f * Mathf.Sin(a * 2f + sd) + 0.07f * Mathf.Sin(a * 3f + sd * 1.7f) + 0.04f * Mathf.Sin(a * 5f + sd * 2.3f);
                    float rad = 0.5f * t * wob;
                    float x = Mathf.Cos(a) * rad, z = Mathf.Sin(a) * rad;
                    float lee = z > 0f ? 1f + z * 0.6f : 1f;                                     // с подветренной стороны чуть круче
                    float h = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Pow(t * lee, 2f)), 1.25f);
                    h *= 1f + 0.06f * Mathf.Sin(a * 4f + sd * 0.7f) * t;
                    if (r == rings) h = -0.03f;
                    verts.Add(new Vector3(x, h, z + 0.08f * (1f - t)));
                }
            }
            for (int k = 0; k < seg; k++) { tris.Add(0); tris.Add(1 + (k + 1) % seg); tris.Add(1 + k); }
            for (int r = 0; r < rings - 1; r++)
            {
                int a0 = 1 + r * seg, b0 = 1 + (r + 1) * seg;
                for (int k = 0; k < seg; k++)
                {
                    int k1 = (k + 1) % seg;
                    tris.Add(a0 + k); tris.Add(a0 + k1); tris.Add(b0 + k);
                    tris.Add(a0 + k1); tris.Add(b0 + k1); tris.Add(b0 + k);
                }
            }
            Mesh mesh = new Mesh { name = "CC_SnowPile", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ── рисование ──
        private void Draw(float m)
        {
            Camera cam = Camera.main;
            Vector3 cp = cam != null ? cam.transform.position : transform.position;
            Bounds big = new Bounds(cp, Vector3.one * 1e5f);
            float camK = CityWeatherArea.CamK, camFull = CityWeatherArea.CamFull;
            if (SnowMaterial != null && SnowNow > 0.002f && camK > 0.01f)
            {
                RenderParams rp = new RenderParams(SnowMaterial) { worldBounds = big, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6, Mathf.Max(1, _flakesMain) + _flakesNear);
            }
            if (IcicleMaterial != null && IcicleGrow > 0.005f && camFull > 0.01f) _points.DrawWith(IcicleMaterial, big, 12);
            if (BreathMaterial != null && Breath && Application.isPlaying && camK > 0.01f)
            {
                RenderParams rb = new RenderParams(BreathMaterial) { worldBounds = big, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderPrimitives(rb, MeshTopology.Triangles, 6, 16);
            }
        }
    }
}
