using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if FISHNET
using FishNet.Object;
using FishNet.Object.Synchronizing;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

// ============================================================================
//  CatCooksMoodDirector — единственный пульт атмосферы.
//  Замена CatCooksWorldDirector: та же роль, но пресеты стали данными.
//
//  ЧТО ИЗМЕНИЛОСЬ ПО СУТИ
//
//   1. ПРЕСЕТЫ — АССЕТЫ, А НЕ switch В КОДЕ. Раньше 18 стилей были
//      кейсами в C#: чтобы поправить оттенок теней, художник лез
//      в исходник и пересобирал проект. Теперь это файлы, которые
//      правятся в инспекторе и видны в системе контроля версий.
//
//   2. ОСИ РАЗВЕДЕНЫ. Стиль отвечает ТОЛЬКО за цвет и подачу, погода —
//      ТОЛЬКО за структуру. Любая погода ложится на любой стиль. Раньше
//      они были склеены, и «гроза в тёплой гамме» требовала отдельного
//      кейса.
//
//   3. ПЕРЕХОДЫ. Смена стиля или погоды идёт плавно, через смесь двух
//      пресетов по кривой. Раньше значения переставлялись мгновенно.
//
//  ОДИН ХОЗЯИН НА ОДИН РЕСУРС. Этот компонент — единственный, кто пишет
//  в RenderSettings.ambient, в профиль Volume и в поля исполнителей.
//  У SkyController поэтому выключены Enable Fog и Enable Cloud Shadows.
//
//  Сеть: под FishNet синхронизируются индексы стиля и погоды. Сервер
//  ведёт, клиенты плавно доезжают тем же переходом.
//
//  v2 (единая система):
//   • ВРЕМЯ СУТОК. Если назначен Day Cycle, солнце ставит он (по GameClock),
//     а палитра стиля смешивается с палитрами рассвета/заката/ночи.
//   • ВЕТЕР ДВИЖЕТ НЕБО. Направление и сила ветра гонят облака скайбокса
//     и гроздья Clouds; скорость смены формы облаков — из погоды.
//   • ВОЗДУХ. Листья, песчаная пыль (Volume Fog) и пылинки в лучах
//     теперь тоже ведёт погода.
//   • Расписание погоды — отдельный компонент Weather Schedule.
// ============================================================================
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Атмосфера/Mood Director")]
#if FISHNET
public class CatCooksMoodDirector : NetworkBehaviour
#else
public class CatCooksMoodDirector : MonoBehaviour
#endif
{
    [Header("━━━ Библиотека ━━━")]
    public List<CatCooksSkyStyle> styles = new List<CatCooksSkyStyle>();
    public List<CatCooksWeatherPreset> weathers = new List<CatCooksWeatherPreset>();

    [Header("━━━ Текущее ━━━")]
    [Tooltip("Индекс стиля. Меняй прямо здесь — переход пойдёт сам.")]
    public int styleIndex = 0;

    [Tooltip("Индекс погоды. Меняй прямо здесь — переход пойдёт сам.")]
    public int weatherIndex = 0;

    [Header("━━━ Переходы ━━━")]
    [Tooltip("Переключать мгновенно, без наплыва. Удобно при подборе: " +
             "выбрал стиль и погоду — сразу видно результат.")]
    public bool instantSwitch = true;

    [Tooltip("Секунд на смену стиля. Работает, только если Instant Switch снят.")]
    public float styleTransition = 6f;

    [Tooltip("Секунд на смену погоды. Пусто — берётся из самого пресета.")]
    public float weatherTransition = 0f;

    [Tooltip("Форма перехода. Ease-in-out читается естественнее линейного.")]
    public AnimationCurve transitionCurve =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("━━━ Исполнители ━━━")]
    public SkyController sky;

    public SunRayLayer sunRays;
    public CatCooksWindController wind;
    public CatCooksWindStreaks windStreaks;
    public CatCooksCloudShadows cloudShadows;
    public CatCooksFogController fog;
    public CatCooksRain rain;

    public CatCooksLightning lightning;
    public CatCooksWeatherFX weatherFX;
    public Volume postVolume;
    public Light sun;

    [Header("━━━ Время суток и воздух ━━━")]
    [Tooltip("Время суток по GameClock. Пусто — солнце стоит, как задано стилем.")]
    public CatCooksDayCycle dayCycle;

    [Tooltip("Сколько раз в секунду пересчитывать цвета неба по времени суток. " +
             "Солнце при этом двигается каждый кадр.")]
    [Range(1f, 30f)] public float dayUpdatesPerSecond = 4f;

    [Tooltip("Листопад (FallingLeaves)")]
    public FallingLeaves leaves;

    [Tooltip("Песчаная пыль у земли (Volume Fog). Погода ставит режим Sand и плотность.")]
    public CatCooksVolumeFog dust;
    public bool driveDust = true;
    [Tooltip("Плотность пыли при dust = 1 в пресете погоды")]
    [Range(0.005f, 0.3f)] public float dustMaxIntensity = 0.1f;

    [Tooltip("Пылинки в солнечных лучах (Sun Dust)")]
    public SunDustParticles sunDust;

    [Tooltip("Ветер гонит облака скайбокса и гроздья Clouds")]
    public bool windMovesClouds = true;

    [Header("━━━ Ручные поправки ━━━")]
    [Tooltip("Поверх стиля. Ноль — стиль как есть.")]
    [Range(-1f, 1f)] public float corExposure = 0f;
    [Range(-1f, 1f)] public float corCoverage = 0f;
    [Range(0f, 2f)] public float corRays = 1f;
    [Range(0f, 2f)] public float corWind = 1f;

    [Tooltip("Множитель дождя поверх пресета")]
    [Range(0f, 2f)] public float corRain = 1f;

    // ========================================================================
    //  РУЧНЫЕ НАСТРОЙКИ ПОВЕРХ ПРЕСЕТА
    //
    //  Каждая группа включается своей галкой и применяется ПОСЛЕ пресета,
    //  затирая соответствующие значения. Пресет при этом не меняется —
    //  переключил стиль, ручные правки остались на месте.
    //
    //  Смысл: пресеты дают быстрый старт, а тонкую доводку конкретной
    //  сцены делаешь здесь, не плодя новых ассетов под каждую мелочь.
    //  Понравилось — перенеси числа в пресет и сними галку.
    // ========================================================================

    [Header("━━━ Ручное: скайбокс ━━━")]
    public bool overrideSky = false;

    [Tooltip("Плотность облаков на скайбоксе, отдельно от объёмных")]
    [Range(0f, 1f)] public float skyCoverageManual = 0.5f;

    [Tooltip("Мягкость края. 0.03 = плакатный контур, 0.2 = размытые")]
    [Range(0.01f, 0.5f)] public float skyCloudSoftness = 0.11f;

    [Tooltip("Размер облаков на небе. Больше = мельче и чаще")]
    [Range(0.2f, 8f)] public float skyCloudScale = 2f;

    [Tooltip("Пушистость. 0 = гладкие кляксы, 1 = комковатые кучевые")]
    [Range(0f, 1f)] public float skyCloudPuff = 0.85f;

    [Tooltip("Искажение координат шума. Убирает вытянутость по сетке")]
    [Range(0f, 1.5f)] public float skyCloudWarp = 0.55f;

    [Tooltip("Тёмный контур по кромке. Больше 0.3 — облака как лужи")]
    [Range(0f, 1f)] public float skyCloudOutline = 0f;

    [Tooltip("Октав детализации силуэта")]
    [Range(3f, 6f)] public float skyCloudDetail = 5f;

    [Tooltip("Глубина самозатенения облаков неба")]
    [Range(0f, 1f)] public float skyCloudShadowDepth = 0.75f;

    [Tooltip("Сдвиг сэмпла к солнцу. Больше = длиннее тени внутри облака")]
    [Range(0f, 0.4f)] public float skyCloudLightShift = 0.13f;

    [Tooltip("Свечение кромки облаков неба")]
    [Range(0f, 2f)] public float skyCloudRim = 0.6f;

    [Tooltip("Полосы градиента неба. 1 = гладко, 4-6 = рисованные ступени")]
    [Range(1f, 8f)] public float skyBandsManual = 1f;

    [Tooltip("Дымка у горизонта. 0 = выключена")]
    [Range(0f, 0.6f)] public float skyHazeHeight = 0.05f;

    [Tooltip("Эрозия кромки. Выгрызает край мелким шумом и не трогает ядро — " +
             "именно этого не хватает сплошной облачности, чтобы она " +
             "перестала быть залитым пятном")]
    [Range(0f, 1f)] public float skyCloudErosion = 0.45f;

    [Tooltip("Размер выгрызаемых клочьев. Больше = мельче рельеф")]
    [Range(1f, 24f)] public float skyCloudErosionScale = 9f;

    [Tooltip("Округлость. Поджимает плотность к середине: доли становятся " +
             "куполами, а не рваными кляксами")]
    [Range(0f, 1f)] public float skyCloudRound = 0.45f;

    [Header("━━━ Ручное: объёмные облака ━━━")]
    public bool overrideClouds = false;

    [Tooltip("Доля цвета солнца в облаках. Больше 0.4 на тёплой палитре " +
             "красит их в бежевый — отсюда были коричневые комья")]
    [Range(0f, 1f)] public float cloudSunTint = 0.25f;

    [Tooltip("Мягкость силуэта. ГЛАВНАЯ ручка пушистости: 0 = резкие шары, " +
             "0.7 = растворяются в небе. Нужен MSAA в URP Asset")]
    [Range(0f, 1f)] public float cloudEdgeFade = 0.7f;

    [Range(0.5f, 6f)] public float cloudEdgePower = 2.5f;

    [Tooltip("Неровность кромки")]
    [Range(0f, 1f)] public float cloudEdgeNoise = 0.45f;

    [Tooltip("Рыхлость поверхности через изгиб нормали")]
    [Range(0f, 3f)] public float cloudBumpStrength = 0.9f;

    [Range(0.05f, 3f)] public float cloudNoiseScale = 0.45f;

    [Tooltip("Ступенчатость заливки. 0 = мягко, 1 = плакат")]
    [Range(0f, 1f)] public float cloudBanding = 0.2f;

    [Range(2f, 6f)] public float cloudRampSteps = 3f;

    [Tooltip("Подъём теней. Тени в облаке не бывают чёрными")]
    [Range(0f, 1f)] public float cloudShadowLift = 0.15f;

    [Tooltip("Голубая подсветка неба. Гасит желтизну")]
    [Range(0f, 1.5f)] public float cloudAmbientBoost = 0.55f;

    [Tooltip("Светлый пушистый край")]
    [Range(0f, 2f)] public float cloudRimStrength = 0.5f;

    [Range(0.5f, 8f)] public float cloudRimPower = 3f;

    [Tooltip("Вспышка кромки против солнца")]
    [Range(0f, 3f)] public float cloudSilver = 1f;

    [Tooltip("Насколько низ облака темнее верха")]
    [Range(0f, 1f)] public float cloudBottomShade = 0.35f;

    [Header("━━━ Ручное: слой облаков ━━━")]
    public bool overrideLayer = false;

    [Range(0, 80)] public int cloudCount = 28;
    public float cloudHeightManual = 230f;
    public float cloudAreaSize = 900f;
    public float cloudSizeMin = 34f;
    public float cloudSizeMax = 150f;

    [Tooltip("1 = все облака одного размера, 3 = много мелких и редкие крупные")]
    [Range(1f, 6f)] public float cloudSizeBias = 3f;

    [Tooltip("Ровность основания. У кучевых дно плоское")]
    [Range(0f, 1f)] public float cloudFlatBottom = 0.8f;

    [Tooltip("Приплюснутость")]
    [Range(0.2f, 1.2f)] public float cloudFlatness = 0.52f;

    [Tooltip("Долей в мелком и крупном облаке (только Volume Puffs)")]
    public Vector2Int cloudLobes = new Vector2Int(5, 13);

    [Tooltip("КАРТ НА ОБЛАКО. Объём набирается наложением: одна карта — " +
             "плоское пятно, четыре-пять — рыхлая масса")]
    public Vector2Int cloudCardsPerCloud = new Vector2Int(5, 9);

    [Tooltip("Разброс карт внутри облака, доля от его размера")]
    [Range(0f, 0.6f)] public float cloudClusterSpread = 0.22f;

    [Header("━━━ Ручное: плотность облака ━━━")]
    public bool overrideCardLook = false;

    [Tooltip("Общая непрозрачность облака")]
    [Range(0f, 1f)] public float cardOpacity = 1f;

    [Tooltip("ГЛАВНАЯ ручка плотности. Растягивает шум на весь диапазон: " +
             "без неё порог отрезает от шума тонкий ломтик, и облака " +
             "выходят полупрозрачными призраками")]
    [Range(1f, 4f)] public float cardContrast = 2f;

    [Tooltip("Прозрачность рыхлого края относительно ядра")]
    [Range(0f, 1f)] public float cardEdgeOpacity = 0.5f;

    [Tooltip("Размытость края. У тумана край размазан на треть радиуса")]
    [Range(0.02f, 0.6f)] public float cardEdgeSoft = 0.18f;

    [Tooltip("Сколько площади карты занято облаком")]
    [Range(0f, 1f)] public float cardCoverage = 0.62f;

    [Tooltip("Рваность контура")]
    [Range(0f, 1f)] public float cardLumpiness = 0.45f;

    [Tooltip("Радиус плато. ГЛАВНАЯ ручка против «лампы»: внутри плато " +
             "форму задаёт шум, а не радиус. Меньше — тело уже, кромка шире")]
    [Range(0.1f, 0.95f)] public float cardCoreSize = 0.5f;

    [Tooltip("Комковатость. 0 = гладкое мыло, 1 = округлые сгустки")]
    [Range(0f, 1f)] public float cardBillow = 0.85f;

    [Tooltip("Искажение координат шума — убирает вытянутость по сетке")]
    [Range(0f, 1.5f)] public float cardWarp = 0.55f;

    [Tooltip("Эрозия кромки: мелкий шум выгрызает край, ядро не трогает. " +
             "Отсюда рваные дымные лохмотья вместо ровной окружности")]
    [Range(0f, 1f)] public float cardErosion = 0.45f;

    [Tooltip("Размер лохмотьев. Больше = мельче клочья")]
    [Range(1f, 12f)] public float cardErosionScale = 3.5f;

    [Tooltip("ГЛАВНАЯ ручка объёма. Сколько света съедает толща облака. " +
             "0 = плоское и белое, 3–5 = глубокие тени и звонкие кромки")]
    [Range(0f, 8f)] public float cardAbsorption = 3.5f;

    [Tooltip("Длина луча самозатенения. Длиннее — мягче и шире тени")]
    [Range(0f, 0.6f)] public float cardLightShift = 0.28f;

    [Tooltip("Насколько темнее основание. У облака под низом вся толща")]
    [Range(0f, 1f)] public float cardBaseDark = 0.65f;

    [Tooltip("Заполняющий свет неба, чтобы тени не были чёрными")]
    [Range(0f, 1f)] public float cardAmbient = 0.25f;

    [Tooltip("Ровность основания. У кучевых низ отрезан ровно")]
    [Range(0f, 1f)] public float cardFlatBottom = 0.8f;

    [Tooltip("Скругление макушки")]
    [Range(0f, 1f)] public float cardDomeTop = 0.5f;

    [Tooltip("Размер внутреннего рисунка")]
    [Range(0.5f, 8f)] public float cardNoiseScale = 2.5f;

    [Tooltip("Доля цвета солнца. Выше 0.5 на тёплой палитре красит в бежевый")]
    [Range(0f, 1f)] public float cardSunTint = 0.3f;

    [Tooltip("Глубина самозатенения")]
    [Range(0f, 1f)] public float cardShadowDepth = 0.55f;

    [Tooltip("Вспышка кромки против солнца")]
    [Range(0f, 3f)] public float cardSilver = 0.9f;

    [Tooltip("Доворот карты к камере. 0 = стоит строго вертикально " +
             "(снизу почти не видна), 1 = всегда лицом. Крена нет ни при " +
             "каком значении — горизонталь карты привязана к миру")]
    [Range(0f, 1f)] public float cardBillboardTilt = 1f;

    [Header("━━━ Ручное: пост ━━━")]
    public bool overridePost = false;
    [Range(-2f, 2f)] public float postExposure = 0f;
    [Range(-100f, 100f)] public float postSaturation = 0f;
    [Range(-100f, 100f)] public float postContrast = 0f;
    [Range(-100f, 100f)] public float postTemperature = 0f;
    [Range(0f, 2f)] public float postBloom = 0.35f;
    [Range(0f, 0.8f)] public float postVignette = 0.2f;

    [Header("━━━ Производительность ━━━")]
    [Tooltip("Сколько раз в секунду обновлять ТЯЖЁЛЫЕ вещи во время " +
             "перехода: цвета неба и заполняющий свет.\n\n" +
             "Почему это важно: запись в RenderSettings.ambient и смена " +
             "параметров неба заставляют Unity пересчитывать окружающее " +
             "освещение (DynamicGI). Это дорого. Пока значения менялись " +
             "КАЖДЫЙ КАДР, пересчёт шёл шестьдесят раз в секунду — отсюда " +
             "просадка именно во время перехода.\n\n" +
             "Растянуть переход не помогает: просадка просто длится дольше. " +
             "Помогает считать реже — на 15 Гц разницы на глаз нет, " +
             "а нагрузка падает вчетверо.")]
    [Range(4f, 60f)] public float heavyUpdatesPerSecond = 15f;

    [Header("━━━ Автосмена погоды ━━━")]
    public bool autoWeather = false;

    [Tooltip("Секунд между сменами. Погода выбирается случайно из списка.")]
    public Vector2 autoWeatherInterval = new Vector2(90f, 240f);

    // ───────── переход ─────────
    private int _styleFrom = -1, _styleTo = -1;
    private int _weatherFrom = -1, _weatherTo = -1;
    private float _styleT = 1f, _weatherT = 1f;
    // снимки недоехавших переходов (CatCooksBlendSnapshot): новый переход идёт от ТЕКУЩЕЙ картинки
    private CatCooksSkyStyle _styleSnap;
    private CatCooksWeatherPreset _weatherSnap;
    private bool _styleSnapOn, _weatherSnapOn;
    private float _styleDur = 1f, _weatherDur = 1f;
    private float _autoTimer;
    private double _lastEditorTime;
    private float _heavyTimer;
    private bool _applyHeavy = true;

    // Рабочие копии — чтобы не портить ассеты смешиванием.
    private CatCooksPalette _paletteMix;
    private CatCooksWeatherPreset _weatherMix;
    /// <summary>Текущая ПЛАВНАЯ смесь погоды (то, что сейчас на экране). Читает небо города (форма облаков).</summary>
    public CatCooksWeatherPreset CurrentWeatherMix => _weatherMix;

    // время суток и воздух
    private float _dayTimer;
    private float _lastLeaves = -1f, _lastDust = -1f, _sunDustBase = -1f;
    private float _sunStyleIntensity = 1.2f, _sunDim = 1f;
    private Color _sunDayColor = Color.white;

    bool DayOn => dayCycle != null && dayCycle.isActiveAndEnabled;

    /// <summary>Этот экземпляр решает за всех (сервер или одиночная игра).</summary>
    public bool HasAuthority => IsAuthority();

#if FISHNET
    private readonly SyncVar<int> _netStyle = new SyncVar<int>();
    private readonly SyncVar<int> _netWeather = new SyncVar<int>();
#endif

    // ========================================================== жизненный цикл
    void OnEnable()
    {
        EnsureMixers();
        _styleFrom = _styleTo = Mathf.Clamp(styleIndex, 0, Mathf.Max(styles.Count - 1, 0));
        _weatherFrom = _weatherTo = Mathf.Clamp(weatherIndex, 0, Mathf.Max(weathers.Count - 1, 0));
        _styleT = _weatherT = 1f;
        _styleSnapOn = _weatherSnapOn = false;
        Apply();
#if UNITY_EDITOR
        _lastEditorTime = EditorApplication.timeSinceStartup;
        EditorApplication.update -= EditorTick;
        EditorApplication.update += EditorTick;
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
#endif
    }

    void OnValidate()
    {
#if UNITY_EDITOR
        EditorApplication.delayCall += () =>
        {
            if (this == null || !isActiveAndEnabled) return;
            CheckIndexChange();
            Apply();
        };
#endif
    }

    void Update()
    {
        if (Application.isPlaying) Tick(Time.deltaTime);
    }

#if UNITY_EDITOR
    void EditorTick()
    {
        if (Application.isPlaying || this == null || !isActiveAndEnabled) return;
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - _lastEditorTime);
        _lastEditorTime = now;
        if (dt <= 0f || dt > 0.5f) return;
        Tick(dt);
    }
#endif

    void Tick(float dt)
    {
        CheckIndexChange();

        // Время суток: солнце двигаем каждый кадр (дёшево), цвета — по таймеру.
        bool dayMoving = false;
        if (DayOn && Application.isPlaying)
        {
            UpdateSunFrame();
            _dayTimer -= dt;
            if (_dayTimer <= 0f)
            {
                _dayTimer = 1f / Mathf.Max(dayUpdatesPerSecond, 0.5f);
                dayMoving = true;
            }
        }

        bool moving = false;
        if (_styleT < 1f) { _styleT = Mathf.Min(1f, _styleT + dt / Mathf.Max(_styleDur, 0.01f)); moving = true; }
        if (_weatherT < 1f) { _weatherT = Mathf.Min(1f, _weatherT + dt / Mathf.Max(_weatherDur, 0.01f)); moving = true; }

        if (autoWeather && Application.isPlaying && IsAuthority())
        {
            _autoTimer -= dt;
            if (_autoTimer <= 0f && weathers.Count > 1)
            {
                int next;
                do { next = Random.Range(0, weathers.Count); } while (next == weatherIndex);
                SetWeather(next);
                _autoTimer = Random.Range(autoWeatherInterval.x, autoWeatherInterval.y);
            }
        }

        // Тяжёлые записи (цвета неба, ambient) дёргают пересчёт GI,
        // поэтому во время перехода идут не каждый кадр, а по таймеру.
        _heavyTimer -= dt;
        _applyHeavy = _heavyTimer <= 0f;
        if (_applyHeavy) _heavyTimer = 1f / Mathf.Max(heavyUpdatesPerSecond, 1f);

        // Переход доехал — обязательно применяем последний кадр целиком,
        // иначе картинка застынет чуть-чуть не доехав до пресета.
        if (!moving) _applyHeavy = true;

        if (moving || dayMoving || !Application.isPlaying) Apply();
    }

    /// <summary>Только солнце/луна по времени суток — каждый кадр, без тяжёлых записей.</summary>
    void UpdateSunFrame()
    {
        if (!DayOn || sky == null) return;
        dayCycle.LightAngles(out float el, out float az, out float mul, out bool moon);
        sky.sunElevation = el;
        sky.sunAzimuth = az;
        sky.sunIntensity = _sunStyleIntensity * _sunDim * mul;
        sky.sunColor = moon ? dayCycle.moonColor : _sunDayColor;
    }

    Vector2 WindDir2()
    {
        if (wind != null)
        {
            float r = wind.directionDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        }
        return new Vector2(0.9f, 0.4f).normalized;
    }

    void CheckIndexChange()
    {
#if FISHNET
        if (Application.isPlaying && !IsAuthority())
        {
            styleIndex = _netStyle.Value;
            weatherIndex = _netWeather.Value;
        }
#endif
        if (styles.Count > 0)
        {
            styleIndex = Mathf.Clamp(styleIndex, 0, styles.Count - 1);
            if (styleIndex != _styleTo) StartStyle(styleIndex);
        }
        if (weathers.Count > 0)
        {
            weatherIndex = Mathf.Clamp(weatherIndex, 0, weathers.Count - 1);
            if (weatherIndex != _weatherTo) StartWeather(weatherIndex);
        }
    }

    bool IsAuthority()
    {
#if FISHNET
        return !Application.isPlaying || IsServerInitialized;
#else
        return true;
#endif
    }

    // ================================================================ API
    /// <summary>Сменить стиль с переходом. seconds &lt; 0 — взять из настроек.</summary>
    public void SetStyle(int index, float seconds = -1f)
    {
        if (styles.Count == 0) return;
        index = Mathf.Clamp(index, 0, styles.Count - 1);
        styleIndex = index;
        StartStyle(index, seconds);
#if FISHNET
        if (Application.isPlaying && IsServerInitialized) _netStyle.Value = index;
#endif
    }

    /// <summary>Сменить погоду с переходом. seconds &lt; 0 — взять из пресета.</summary>
    public void SetWeather(int index, float seconds = -1f)
    {
        if (weathers.Count == 0) return;
        index = Mathf.Clamp(index, 0, weathers.Count - 1);
        weatherIndex = index;
        StartWeather(index, seconds);
#if FISHNET
        if (Application.isPlaying && IsServerInitialized) _netWeather.Value = index;
#endif
    }

    void StartStyle(int index, float seconds = -1f)
    {
        // Если предыдущий переход не доехал, стартуем от ТЕКУЩЕЙ смеси,
        // а не от исходного пресета — иначе картинка дёрнется назад.
        // Переход не доехал — снимаем текущую смесь и ведём от неё (без рывка назад/вперёд).
        if (_styleT < 1f)
        {
            var a0 = _styleSnapOn ? _styleSnap : Get(styles, _styleFrom);
            var b0 = Get(styles, _styleTo);
            if (a0 != null && b0 != null)
            {
                _styleSnap = CatCooksBlendSnapshot.Style(a0, b0, transitionCurve.Evaluate(Mathf.Clamp01(_styleT)), _styleSnap);
                _styleSnapOn = true;
            }
        }
        else _styleSnapOn = false;
        _styleFrom = _styleTo;

        _styleTo = index;
        _styleDur = instantSwitch ? 0f : (seconds >= 0f ? seconds : styleTransition);
        _styleT = _styleDur <= 0.01f ? 1f : 0f;
    }

    void StartWeather(int index, float seconds = -1f)
    {
        if (_weatherT < 1f)
        {
            var a0 = _weatherSnapOn ? _weatherSnap : Get(weathers, _weatherFrom);
            var b0 = Get(weathers, _weatherTo);
            if (a0 != null && b0 != null)
            {
                _weatherSnap = CatCooksBlendSnapshot.Weather(a0, b0, transitionCurve.Evaluate(Mathf.Clamp01(_weatherT)), _weatherSnap);
                _weatherSnapOn = true;
            }
        }
        else _weatherSnapOn = false;
        _weatherFrom = _weatherTo;

        _weatherTo = index;
        float dur = seconds;
        if (dur < 0f)
        {
            dur = weatherTransition > 0.01f ? weatherTransition
                : (Get(weathers, index) != null ? Get(weathers, index).transitionSeconds : 10f);
        }
        _weatherDur = instantSwitch ? 0f : dur;
        _weatherT = _weatherDur <= 0.01f ? 1f : 0f;
    }

    // ============================================================== применение
    [ContextMenu("Применить сейчас")]
    public void Apply()
    {
        // Вызов извне (кнопка, редактор, смена пресета) всегда полный:
        // экономить имеет смысл только на промежуточных кадрах перехода.
        if (!Application.isPlaying) _applyHeavy = true;

        EnsureMixers();

        var sA = _styleSnapOn ? _styleSnap : Get(styles, _styleFrom);
        var sB = Get(styles, _styleTo);
        if (sB == null) return;
        if (sA == null) sA = sB;

        var wA = _weatherSnapOn ? _weatherSnap : Get(weathers, _weatherFrom);
        var wB = Get(weathers, _weatherTo);
        if (wB == null) return;
        if (wA == null) wA = wB;

        float st = transitionCurve.Evaluate(Mathf.Clamp01(_styleT));
        float wt = transitionCurve.Evaluate(Mathf.Clamp01(_weatherT));

        CatCooksPalette.Blend(sA.palette, sB.palette, st, _paletteMix);
        CatCooksWeatherPreset.Blend(wA, wB, wt, _weatherMix);

        // Время суток поверх стиля: рассвет/закат в золотой час, ночь ночью.
        if (DayOn)
        {
            dayCycle.Evaluate();
            var gp = dayCycle.GoldenPalette;
            if (gp != null && dayCycle.GoldenWeight > 0.001f)
                CatCooksPalette.Blend(_paletteMix, gp, dayCycle.GoldenWeight, _paletteMix);
            var np = dayCycle.NightPalette;
            if (np != null && dayCycle.NightWeight > 0.001f)
                CatCooksPalette.Blend(_paletteMix, np, dayCycle.NightWeight, _paletteMix);
        }

        var P = _paletteMix;
        var W = _weatherMix;

        // Числа стиля смешиваем на месте: их немного и все линейные.
        float Lerp(float a, float b) => Mathf.Lerp(a, b, st);

        float coverage = Mathf.Clamp01(W.coverage + corCoverage);
        float skyCoverage = Mathf.Clamp01(W.skyCoverage + corCoverage);
        float sev = W.severity;

        // ------------------------------------------------------------ солнце
        // ВАЖНО: солнцем владеет SkyController — он каждый кадр сам ставит
        // поворот, цвет и яркость Light из своих полей. Писать в Light
        // напрямую нельзя, он тут же перетрёт. Поэтому кормим ЕГО поля.
        _sunStyleIntensity = Lerp(sA.sunIntensity, sB.sunIntensity);
        _sunDim = W.sunDim;
        _sunDayColor = P.sunDisc;
        if (sky != null && DayOn)
        {
            UpdateSunFrame();
        }
        else if (sky != null)
        {
            sky.sunElevation = Lerp(sA.sunElevation, sB.sunElevation);
            sky.sunAzimuth = Lerp(sA.sunAzimuth, sB.sunAzimuth);
            sky.sunColor = P.sunDisc;
            sky.sunIntensity = Lerp(sA.sunIntensity, sB.sunIntensity) * W.sunDim;
        }
        else if (sun != null)
        {
            // Запасной путь, если SkyController в сцене нет.
            sun.color = P.sunLight;
            sun.intensity = Lerp(sA.sunIntensity, sB.sunIntensity)
                          * Mathf.Lerp(1f, 0.55f, W.overcast);
            sun.transform.rotation = Quaternion.Euler(
                Lerp(sA.sunElevation, sB.sunElevation),
                Lerp(sA.sunAzimuth, sB.sunAzimuth), 0f);
        }

        // -------------------------------------------------------------- небо
        if (sky != null)
        {
            // ЦВЕТА НЕБА — тяжёлая часть: SkyController по их изменению
            // пересобирает окружающее освещение. Пишем по таймеру.
            if (_applyHeavy)
            {
                sky.zenithColor = P.zenith;
                sky.horizonColor = P.horizon;
                sky.groundColor = P.ground;
                sky.cloudLitColor = P.cloudLit;
                sky.cloudShadowColor = P.cloudShadow;
            }
            sky.coverage = skyCoverage;

            // Мягкость и масштаб скайбоксных облаков SkyController тоже
            // пишет в материал сам — задаём через его поля, иначе он
            // перетрёт наши значения следующим кадром.
            sky.cloudSoftness = Lerp(sA.skyCloudSoft, sB.skyCloudSoft);
            sky.cloudScale = Lerp(sA.skyCloudScale, sB.skyCloudScale);
            sky.cloudDarkness = W.darkness;
            sky.skyOvercast = W.overcast;

            // Туман и тени ведут отдельные компоненты. Один хозяин на ресурс.
            sky.enableFog = false;
            sky.enableCloudShadows = false;

            if (sky.skyMaterial != null)
            {
                var m = sky.skyMaterial;
                m.SetFloat("_SkyBands", Lerp(sA.skyBands, sB.skyBands));
                m.SetFloat("_BandBlend", Lerp(sA.bandSoftness, sB.bandSoftness));
                // Только те свойства, которых SkyController не касается.
                // _CloudSoft и _CloudScale идут выше, через его поля.
                m.SetFloat("_CloudPuff", Lerp(sA.skyCloudPuff, sB.skyCloudPuff));
                m.SetFloat("_CloudWarp", Lerp(sA.skyCloudWarp, sB.skyCloudWarp));
                m.SetFloat("_CloudOutline", Lerp(sA.skyCloudOutline, sB.skyCloudOutline));
                m.SetFloat("_CloudToon", Lerp(sA.cloudToon, sB.cloudToon));
                m.SetFloat("_CloudSteps", Lerp(sA.cloudSteps, sB.cloudSteps));
                m.SetColor("_HazeColor", P.fogFar);
                m.SetFloat("_HazeHeight", W.fogEnabled ? 0.10f : 0.02f);
            }
        }

        // -------------------------------------------------------------- лучи
        if (sunRays != null)
        {
            // Яркость лучей задаёт ПОГОДА и только она. Раньше поверх
            // пресета накручивался ещё и «бонус за переменную облачность»,
            // из-за чего лучи били всегда, даже там, где по замыслу их
            // быть не должно.
            float rays = W.rayIntensity * corRays * (DayOn ? dayCycle.Day01 : 1f);
            sunRays.intensityMultiplier = rays;
            sunRays.tint = P.rayTint;

            // Ниже порога выключаем объект целиком: карточки лучей
            // с нулевой яркостью всё равно рисуются и мылят кадр.
            bool rayOn = rays > 0.02f;
            if (sunRays.gameObject.activeSelf != rayOn)
                sunRays.gameObject.SetActive(rayOn);
        }

        // -------------------------------------------------------------- ветер
        float windAmt = Mathf.Clamp01(W.wind * corWind);
        if (wind != null) wind.SetWeather(windAmt);
        // Ветер гонит небо: облака скайбокса едут по ветру, а форма
        // меняется тем быстрее, чем злее погода.
        Vector2 wdir = WindDir2();
        if (sky != null)
        {
            sky.cloudEvolve = W.cloudEvolve * Mathf.Lerp(0.8f, 1.8f, windAmt);
            if (windMovesClouds) sky.cloudWind = wdir * Mathf.Lerp(0.004f, 0.03f, windAmt);
        }

        // Листья: больше в ветер, меньше в дождь.
        if (leaves != null)
        {
            float amt = W.leaves * Mathf.Lerp(0.4f, 1.6f, windAmt) * (1f - Mathf.Clamp01(W.rain) * 0.7f);
            if (Mathf.Abs(amt - _lastLeaves) > 0.02f) { leaves.SetDensity(amt * 2f); _lastLeaves = amt; }
        }

        // Песчаная пыль у земли.
        if (dust != null && driveDust && Application.isPlaying)
        {
            float d = W.dust * dustMaxIntensity;
            if (Mathf.Abs(d - _lastDust) > 0.0005f)
            {
                if (d > 0.0005f) dust.SetMode(CatCooksVolumeFog.Mode.Sand, 6f);
                dust.SetIntensity(d, 6f);
                _lastDust = d;
            }
        }

        // Пылинки в лучах: только при солнце.
        if (sunDust != null)
        {
            var ps = sunDust.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                var em = ps.emission;
                if (_sunDustBase < 0f) _sunDustBase = em.rateOverTimeMultiplier;
                float k = W.sunDust * (1f - Mathf.Clamp01(W.rain)) * (1f - W.overcast * 0.8f)
                        * (DayOn ? dayCycle.Day01 : 1f);
                em.rateOverTimeMultiplier = _sunDustBase * k * 2f;
            }
        }

        // Табличка погоды для эффектов (дождь, лужи, молнии по зонам, буря, туман города)
        CatCooksWeatherState.Rain = Mathf.Clamp01(W.rain);
        CatCooksWeatherState.Wind = windAmt;
        CatCooksWeatherState.WindDir = wdir;
        CatCooksWeatherState.Dust = W.dust;
        CatCooksWeatherState.Leaves = W.leaves;
        CatCooksWeatherState.Storm = W.lightning;
        CatCooksWeatherState.Overcast = W.overcast;
        CatCooksWeatherState.Darkness = W.darkness;
        CatCooksWeatherState.FogDensity = W.fogEnabled ? W.fogDensity : 0f;
        CatCooksWeatherState.Day01 = DayOn ? dayCycle.Day01 : 1f;
        CatCooksWeatherState.FogNear = P.fogNear;
        CatCooksWeatherState.FogFar = P.fogFar;
        CatCooksWeatherState.CloudLit = P.cloudLit;
        CatCooksWeatherState.CloudShadow = P.cloudShadow;
        CatCooksWeatherState.SunColor = P.sunLight;
        CatCooksWeatherState.Ambient = P.ambientSky;
        CatCooksWeatherState.SkyZenith = P.zenith;
        CatCooksWeatherState.SkyHorizon = P.horizon;
        var curW = (weatherIndex >= 0 && weatherIndex < weathers.Count) ? weathers[weatherIndex] : null;
        if (curW != CatCooksWeatherState.Current) CatCooksWeatherState.RaiseChanged(CatCooksWeatherState.Current, curW);

        if (windStreaks != null)
        {
            windStreaks.weather = W.windStreaks;
            if (windStreaks.streakMaterial != null)
                windStreaks.streakMaterial.SetColor("_Color", P.windStreak);
        }

        // -------------------------------------------------------------- дождь
        if (rain != null)
        {
            rain.SetIntensity(W.rain * corRain);

            // ХАРАКТЕР дождя, а не только его количество. Жёсткость от
            // силы непогоды, снос от ветра, блеск падает под грозовой
            // тучей — там света просто меньше. Без этого морось и ливень
            // отличались только числом капель и выглядели одинаково.
            rain.SetCharacter(W.severity,
                              Mathf.Clamp01(W.wind * 0.8f),
                              Mathf.Lerp(1.3f, 0.35f, W.darkness));
        }
        if (lightning != null) lightning.SetActivity(W.lightning);

        // ---------------------------------------------------- эффекты камеры
        if (weatherFX != null)
            weatherFX.SetMood(W.screenFrame, W.frameColor, W.edgeDesaturate);

        // -------------------------------------------------------------- тени
        if (cloudShadows != null)
        {
            bool on = W.cloudShadows && W.shadowStrength > 0.01f;
            if (cloudShadows.enabled != on) cloudShadows.enabled = on;
            if (on) cloudShadows.SetWeather(coverage, W.shadowStrength);
        }

        // -------------------------------------------------------------- туман
        if (fog != null)
        {
            fog.usePreset = false;
            fog.matchSkyHorizon = false;
            fog.colorNear = P.fogNear;
            fog.colorFar = P.fogFar;
            fog.sunColor = P.sunDisc;
            fog.density = W.fogEnabled ? W.fogDensity : 0f;
            fog.start = W.fogStart;
            fog.end = W.fogEnd;
            fog.heightTop = W.fogHeightTop;
            fog.Apply();
        }

        // ------------------------------------------------------------ ambient
        // Заполняющий свет — тоже пересчёт GI на каждую запись.
        if (_applyHeavy)
        {
            float amb = Lerp(sA.ambientBoost, sB.ambientBoost) * Mathf.Lerp(1f, 0.8f, sev);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = P.ambientSky * amb;
            RenderSettings.ambientEquatorColor = P.ambientEquator * amb;
            RenderSettings.ambientGroundColor = P.ambientGround * amb;
        }

        // ---------------------------------------------------------------- пост
        if (postVolume != null && postVolume.profile != null)
        {
            var prof = postVolume.profile;
            // Стиль задаёт гамму, погода её СМЕЩАЕТ. Раньше погода влияла
            // на пост одним общим sev, поэтому все двенадцать выглядели
            // одинаково — менялись только облака.
            if (prof.TryGet(out ColorAdjustments ca))
            {
                // Экспозицию сверху ограничиваем жёстко: пересвет бьёт
                // по глазам сильнее, чем недосвет, и «поярче» почти всегда
                // оказывается ошибкой.
                float exp = Lerp(sA.postExposure, sB.postExposure)
                          + W.exposureShift + corExposure
                          + (DayOn ? dayCycle.nightExposure * dayCycle.Night01 : 0f);
                ca.postExposure.Override(Mathf.Clamp(exp, -1.5f, 0.45f));

                ca.saturation.Override(Lerp(sA.saturation, sB.saturation)
                                       + W.saturationShift);
                ca.contrast.Override(Lerp(sA.contrast, sB.contrast) + W.contrastShift);
            }
            if (prof.TryGet(out WhiteBalance wb))
                wb.temperature.Override(Mathf.Clamp(
                    Lerp(sA.temperature, sB.temperature) + W.temperatureShift, -100f, 100f));
            if (prof.TryGet(out Bloom bl))
                bl.intensity.Override(Mathf.Max(0f,
                    Lerp(sA.bloom, sB.bloom) + W.bloomAdd));
            if (prof.TryGet(out Vignette vg))
                vg.intensity.Override(Mathf.Clamp01(
                    Lerp(sA.vignette, sB.vignette) + W.vignetteAdd));
        }

        ApplyOverrides();
    }

    // ============================================== ручные настройки поверх
    /// <summary>
    /// Применяется ПОСЛЕ пресета и затирает его значения там, где включена
    /// галка. Пресет при этом не меняется: переключил стиль — ручные
    /// правки остались.
    /// </summary>
    void ApplyOverrides()
    {
        // ---------------------------------------------------------- скайбокс
        if (overrideSky && sky != null)
        {
            // Softness, Scale и Coverage идут ЧЕРЕЗ ПОЛЯ SkyController:
            // он пишет их в материал сам каждый кадр и перетёр бы прямую
            // запись следующим же кадром.
            sky.coverage = skyCoverageManual;
            sky.cloudSoftness = skyCloudSoftness;
            sky.cloudScale = skyCloudScale;

            if (sky.skyMaterial != null)
            {
                var m = sky.skyMaterial;
                m.SetFloat("_CloudPuff", skyCloudPuff);
                m.SetFloat("_CloudWarp", skyCloudWarp);
                m.SetFloat("_CloudOutline", skyCloudOutline);
                m.SetFloat("_CloudDetail", skyCloudDetail);
                m.SetFloat("_CloudShadowDepth", skyCloudShadowDepth);
                m.SetFloat("_CloudLightShift", skyCloudLightShift);
                m.SetFloat("_CloudRim", skyCloudRim);
                m.SetFloat("_SkyBands", skyBandsManual);
                m.SetFloat("_HazeHeight", skyHazeHeight);
                m.SetFloat("_CloudErosion", skyCloudErosion);
                m.SetFloat("_CloudErosionScale", skyCloudErosionScale);
                m.SetFloat("_CloudRound", skyCloudRound);
            }
        }

        // ---------------------------------------------------------------- пост
        if (overridePost && postVolume != null && postVolume.profile != null)
        {
            var prof = postVolume.profile;
            if (prof.TryGet(out ColorAdjustments ca))
            {
                ca.postExposure.Override(postExposure);
                ca.saturation.Override(postSaturation);
                ca.contrast.Override(postContrast);
            }
            if (prof.TryGet(out WhiteBalance wb)) wb.temperature.Override(postTemperature);
            if (prof.TryGet(out Bloom bl)) bl.intensity.Override(postBloom);
            if (prof.TryGet(out Vignette vg)) vg.intensity.Override(postVignette);
        }
    }

    /// <summary>Скопировать текущие значения пресета в ручные поля —
    /// чтобы крутить от того, что уже видишь, а не от нуля.</summary>
    [ContextMenu("Забрать текущие значения в ручные")]
    public void PullCurrentIntoOverrides()
    {
        var st = Get(styles, _styleTo);
        if (st != null)
        {
            skyCloudSoftness = st.skyCloudSoft;
            skyCloudScale = st.skyCloudScale;
            skyCloudPuff = st.skyCloudPuff;
            skyCloudWarp = st.skyCloudWarp;
            skyCloudOutline = st.skyCloudOutline;
            skyBandsManual = st.skyBands;
            cloudBanding = st.cloudToon;
            cloudRampSteps = st.cloudSteps;
            cloudSilver = st.silverLining;
            cloudBottomShade = st.cloudBottomShade;
            postExposure = st.postExposure;
            postSaturation = st.saturation;
            postContrast = st.contrast;
            postTemperature = st.temperature;
            postBloom = st.bloom;
            postVignette = st.vignette;
        }

        var w = Get(weathers, _weatherTo);
        if (w != null)
        {
            skyCoverageManual = w.skyCoverage;
            cloudHeightManual = w.cloudHeight;
        }
    }

    // ================================================================ служебное
    static T Get<T>(List<T> list, int i) where T : Object
        => (list != null && i >= 0 && i < list.Count) ? list[i] : null;

    void EnsureMixers()
    {
        // Смешиваем в отдельные экземпляры, а не в ассеты: иначе первый же
        // переход перезапишет пресеты на диске.
        if (_paletteMix == null)
        {
            _paletteMix = ScriptableObject.CreateInstance<CatCooksPalette>();
            _paletteMix.hideFlags = HideFlags.HideAndDontSave;
        }
        if (_weatherMix == null)
        {
            _weatherMix = ScriptableObject.CreateInstance<CatCooksWeatherPreset>();
            _weatherMix.hideFlags = HideFlags.HideAndDontSave;
        }
    }

    [ContextMenu("Следующий стиль")]
    void NextStyle() => SetStyle((styleIndex + 1) % Mathf.Max(styles.Count, 1));

    [ContextMenu("Следующая погода")]
    void NextWeather() => SetWeather((weatherIndex + 1) % Mathf.Max(weathers.Count, 1));
}
