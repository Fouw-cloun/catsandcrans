using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if FISHNET
using FishNet.Object;
using FishNet.Object.Synchronizing;
#endif

// ============================================================================
//  CatCooksWorldDirector  v3  —  арт-директорский пульт мира.
//
//  ДВЕ НЕЗАВИСИМЫЕ ОСИ (комбинируются как угодно, 18 × 12 = 216 вариантов):
//
//  СТИЛЬ (ApplyPreset) — цветовое решение: палитра света/неба/теней, грейдинг.
//   1 SolarpunkNoon    — золото + голубые тени (базовый геймплей)
//   2 GoldenHour       — янтарь + фиолет (кино-вечер)
//   3 MistyMorning     — молочная прохлада (тишина)
//   4 SpringFresh      — звонкий чистый день (трейлеры)
//   5 CandySunset      — розово-лавандовый (десерт)
//   6 StormLight       — тёмный мир + яркое солнце (драма)
//   7 GhibliMeadow     — кремовое небо + живая зелень (луговая сказка)
//   8 WindWakerPop     — плоские ультрасочные циан+белый (мультфильм)
//   9 BotWPastel       — приглушённая пастель + голубая дымка (приключение)
//  10 FirewatchPoster  — киноварь/слива слоями (постерный закат)
//  11 OriMystic        — бирюза + фиолет, холодная магия (тёплые лампы сияют)
//  12 TealOrangeCinema — блокбастерный teal&orange
//  13 NordicFrost      — сталь + лёд, скандинавский минимализм
//  14 TropicalPunch    — бирюза + манго, курортная сочность
//  15 AutumnAmber      — охра + багрянец, осень
//  16 ChocolateBakery  — какао + крем + карамель (кондитерская)
//  17 NeonDusk         — сумерки + неоново-розовый (вечер с гирляндами)
//  18 SepiaMemory      — тёплая сепия, почти ч/б (флешбеки, меню)
//
//  ПОГОДА (ApplyWeather) — структура неба: облака, туман, лучи, тени, ветер.
//   1 ClearSky       — ясно, прозрачный воздух
//   2 FairClouds     — лёгкие кучевые
//   3 SunbeamDay     — рваная облачность, МАКСИМУМ лучей («то самое»)
//   4 BrightOvercast — светлая пасмурность, мягкий бестеневой свет
//   5 Gloomy         — хмуро, низкое серое небо
//   6 Drizzle        — морось: хмуро + близкий туман
//   7 Storm          — гроза: темно, ветер, плотно
//   8 StormBreak     — разрыв грозы: тёмное небо, столбы света
//   9 DenseFog       — молоко: туман 8–65 м, лучи тлеют в дымке
//  10 MorningHaze    — утренняя дымка + сильные мягкие лучи
//  11 DustyHeat      — сухая пыльная жара, дымка у горизонта
//  12 AfterRain      — кристальный воздух, контрастные тени, резкие лучи
//
//  Сеть: два float (время, погода-величина) через FishNet. Пресеты применяй
//  детерминированно при загрузке уровня или своим ObserversRpc.
// ============================================================================
#if FISHNET
public class CatCooksWorldDirector : NetworkBehaviour
#else
public class CatCooksWorldDirector : MonoBehaviour
#endif
{
    public enum WorldPreset
    {
        SolarpunkNoon, GoldenHour, MistyMorning, SpringFresh, CandySunset, StormLight,
        GhibliMeadow, WindWakerPop, BotWPastel, FirewatchPoster, OriMystic, TealOrangeCinema,
        NordicFrost, TropicalPunch, AutumnAmber, ChocolateBakery, NeonDusk, SepiaMemory
    }

    public enum WeatherPreset
    {
        ClearSky, FairClouds, SunbeamDay, BrightOvercast, Gloomy, Drizzle,
        Storm, StormBreak, DenseFog, MorningHaze, DustyHeat, AfterRain
    }

    [Header("Ссылки")]
    public SkyController sky;
    public FogCloudLayer fogClouds;
    public SunRayLayer sunRays;
    public Volume postVolume;

    [Header("Время суток")]
    [Range(0f, 1f)] public float timeOfDay = 0.5f;
    public float dayLengthMinutes = 0f;
    public Vector2 dayRange = new Vector2(0.28f, 0.72f);

    [Header("Погода-величина: 0 = ясно, 1 = максимум непогоды")]
    [Range(0f, 1f)] public float weather = 0f;
    public float weatherBlendSpeed = 0.15f;

    // ------------------------------------------------ СТИЛЬ (цвет)
    [Header("СТИЛЬ · Солнце")]
    public Gradient sunColor = new Gradient();
    public AnimationCurve sunIntensity = AnimationCurve.Linear(0, 1.15f, 1, 1.15f);
    public float sunElevationMin = 14f;
    public float sunElevationMax = 58f;
    public float sunAzimuthStart = 25f;
    public float sunAzimuthEnd = 115f;

    [Header("СТИЛЬ · Небо")]
    public Gradient zenithColor = new Gradient();
    public Gradient horizonColor = new Gradient();
    public Gradient groundColor = new Gradient();

    [Header("СТИЛЬ · Облака (цвет)")]
    public Gradient cloudLitColor = new Gradient();
    public Gradient cloudShadowColor = new Gradient();

    [Header("СТИЛЬ · Туман (цвет)")]
    public bool useFog = true;
    public Gradient fogColor = new Gradient();

    [Header("СТИЛЬ · Ambient (цвет теней!)")]
    public Gradient ambientSky = new Gradient();
    public Gradient ambientEquator = new Gradient();
    public Gradient ambientGround = new Gradient();
    [Range(0f, 1f)] public float ambientStormDim = 0.4f;

    [Header("СТИЛЬ · Грейдинг")]
    public AnimationCurve postExposure = AnimationCurve.Linear(0, 0.25f, 1, 0.25f);
    public AnimationCurve whiteBalanceTemp = AnimationCurve.Linear(0, 14f, 1, 14f);
    public float baseSaturation = 12f;
    [Range(0f, 30f)] public float stormDesaturate = 12f;

    [Header("СТИЛЬ · Лучи (базовая яркость стиля)")]
    public float rayBaseIntensity = 1f;

    // ------------------------------------------------ ПОГОДА (структура)
    [Header("ПОГОДА · Облака и небо")]
    [Tooltip("Плотность облаков: при weather = 0 и при weather = 1")]
    public Vector2 coverageClearStorm = new Vector2(0.35f, 0.85f);
    [Tooltip("Множитель потемнения облаков (гроза = 1, светлая пасмурность = 0.35)")]
    [Range(0f, 1f)] public float darknessScale = 1f;
    [Tooltip("Множитель пасмурности неба и гашения солнца")]
    [Range(0f, 1f)] public float overcastScale = 1f;
    public Vector2 cloudWind = new Vector2(0.008f, 0.005f);

    [Header("ПОГОДА · Туман (дистанции)")]
    public Vector2 fogStartClearStorm = new Vector2(70f, 25f);
    public Vector2 fogEndClearStorm = new Vector2(420f, 160f);

    [Header("ПОГОДА · Fog cards (сферные облака)")]
    [Range(0f, 1f)] public float fogCloudDensity = 0.5f;

    [Header("ПОГОДА · Лучи")]
    [Tooltip("Погодный множитель лучей (SunbeamDay/StormBreak > 1.5)")]
    public float rayWeatherBoost = 1f;

    [Header("ПОГОДА · Тени от облаков")]
    public bool cloudShadows = true;
    public Vector2 shadowStrengthClearStorm = new Vector2(0.4f, 0.7f);
    public Vector2 cloudShadowSpeed = new Vector2(4f, 2f);

    // ---- Коррекции (пишет CatCooksLookPanel; нейтральные значения = без эффекта) ----
    [HideInInspector] public float corExposure = 0f;
    [HideInInspector] public float corSaturation = 0f;
    [HideInInspector] public float corWarmth = 0f;
    [HideInInspector] public float corSunBoost = 1f;
    [HideInInspector] public float corCoverage = 0f;
    [HideInInspector] public float corShadow = 0f;
    [HideInInspector] public float corFogMult = 1f;
    [HideInInspector] public float corRayMult = 1f;
    [HideInInspector] public float corAmbBright = 1f;
    [HideInInspector] public float corAmbWarmth = 0f;

    private ColorAdjustments _colorAdj;
    private WhiteBalance _whiteBal;
    private float _weatherSmooth;

    // ================================================================= сеть
#if FISHNET
    private readonly SyncVar<float> _netTime = new SyncVar<float>();
    private readonly SyncVar<float> _netWeather = new SyncVar<float>();

    private void Awake()
    {
        _netTime.OnChange += (p, n, s) => { timeOfDay = n; };
        _netWeather.OnChange += (p, n, s) => { weather = n; };
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        timeOfDay = _netTime.Value;
        weather = _netWeather.Value;
        _weatherSmooth = weather;
        Apply();
    }

    [Server] public void ServerSetWeather(float w) => _netWeather.Value = Mathf.Clamp01(w);
    [Server] public void ServerSetTime(float t) => _netTime.Value = Mathf.Clamp01(t);
#endif

    // ================================================================= цикл
    private void OnEnable() { CacheVolume(); _weatherSmooth = weather; }

    private void Update()
    {
        if (dayLengthMinutes > 0.01f)
        {
            float span = dayRange.y - dayRange.x;
            float t = Mathf.PingPong(Time.time / (dayLengthMinutes * 60f) * span * 2f, span) + dayRange.x;
#if FISHNET
            if (base.IsServerInitialized) _netTime.Value = t;
#else
            timeOfDay = t;
#endif
        }
        _weatherSmooth = Mathf.MoveTowards(_weatherSmooth, weather, weatherBlendSpeed * Time.deltaTime);
        Apply();
    }

    private void CacheVolume()
    {
        if (postVolume == null || postVolume.profile == null) return;
        postVolume.profile.TryGet(out _colorAdj);
        postVolume.profile.TryGet(out _whiteBal);
    }

    // ================================================================= применение
    public void Apply()
    {
        float t = Mathf.Clamp01(timeOfDay);
        float w = Mathf.Clamp01(Application.isPlaying ? _weatherSmooth : weather);

        if (sky != null)
        {
            sky.sunElevation = Mathf.Lerp(sunElevationMin, sunElevationMax, Mathf.Sin(t * Mathf.PI));
            sky.sunAzimuth = Mathf.Lerp(sunAzimuthStart, sunAzimuthEnd, t);
            sky.sunColor = sunColor.Evaluate(t);
            sky.sunIntensity = sunIntensity.Evaluate(t) * corSunBoost;

            sky.zenithColor = zenithColor.Evaluate(t);
            sky.horizonColor = horizonColor.Evaluate(t);
            sky.groundColor = groundColor.Evaluate(t);

            sky.cloudLitColor = cloudLitColor.Evaluate(t);
            sky.cloudShadowColor = cloudShadowColor.Evaluate(t);
            sky.coverage = Mathf.Clamp01(Mathf.Lerp(coverageClearStorm.x, coverageClearStorm.y, w) + corCoverage);
            sky.cloudWind = cloudWind;

            sky.cloudDarkness = w * darknessScale;
            sky.skyOvercast = w * overcastScale;

            sky.enableFog = useFog;
            sky.fogColor = fogColor.Evaluate(t);
            sky.fogStart = Mathf.Lerp(fogStartClearStorm.x, fogStartClearStorm.y, w) * corFogMult;
            sky.fogEnd = Mathf.Lerp(fogEndClearStorm.x, fogEndClearStorm.y, w) * corFogMult;

            sky.enableCloudShadows = cloudShadows;
            sky.shadowStrength = Mathf.Clamp01(Mathf.Lerp(shadowStrengthClearStorm.x, shadowStrengthClearStorm.y, w) + corShadow);
            sky.shadowSpeed = cloudShadowSpeed;
        }

        if (fogClouds != null)
        {
            fogClouds.density = fogCloudDensity;
            fogClouds.darkness = w * darknessScale;
            fogClouds.cloudColor = cloudLitColor.Evaluate(t);
            fogClouds.shadowTint = cloudShadowColor.Evaluate(t);
            // PushMaterial у FogCloudLayer приватный — вызываем без правки твоего файла
            fogClouds.SendMessage("PushMaterial", SendMessageOptions.DontRequireReceiver);
        }

        if (sunRays != null)
        {
            float partial = Mathf.Clamp01(1f - Mathf.Abs(w - 0.42f) / 0.5f);
            float lowSunBoost = Mathf.Lerp(1.25f, 0.8f, Mathf.Sin(t * Mathf.PI));
            sunRays.intensityMultiplier =
                rayBaseIntensity * rayWeatherBoost * (0.25f + 1.1f * partial) * lowSunBoost * corRayMult;
            sunRays.tint = sunColor.Evaluate(t);
        }

        float dim = Mathf.Lerp(1f, 1f - ambientStormDim, w);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        float ab = dim * corAmbBright;
        RenderSettings.ambientSkyColor = TintAmb(ambientSky.Evaluate(t)) * ab;
        RenderSettings.ambientEquatorColor = TintAmb(ambientEquator.Evaluate(t)) * ab;
        RenderSettings.ambientGroundColor = TintAmb(ambientGround.Evaluate(t)) * ab;

        if (_colorAdj == null || _whiteBal == null) CacheVolume();
        if (_colorAdj != null)
        {
            _colorAdj.postExposure.Override(postExposure.Evaluate(t) - w * 0.35f + corExposure);
            _colorAdj.saturation.Override(baseSaturation - stormDesaturate * w + corSaturation);
        }
        if (_whiteBal != null)
            _whiteBal.temperature.Override(whiteBalanceTemp.Evaluate(t) * (1f - w * 0.6f) + corWarmth);
    }

    // ================================================================= СТИЛИ
    public void ApplyPreset(WorldPreset p)
    {
        switch (p)
        {
            //     sun-градиент                                            int   elev      azimuth  зенит                гориз                 земля                облака-свет          облака-тень          туман                amb-небо(тени!)       amb-экватор           amb-земля            expo  wb   sat  ray  tod
            case WorldPreset.SolarpunkNoon:
                S(G(C(1f,.80f,.58f), C(1f,.93f,.80f), C(1f,.74f,.48f)), 1.30f, 14,58, 25,115, G1(C(.35f,.60f,.92f)), G1(C(.84f,.90f,.95f)), G1(C(.60f,.63f,.68f)), G1(C(1f,.96f,.90f)),  G1(C(.72f,.78f,.90f)), G1(C(.91f,.86f,.78f)), G1(C(.62f,.74f,.90f)), G1(C(.76f,.70f,.62f)), G1(C(.52f,.44f,.36f)), .25f, 14f, 12f, 0.8f, .50f); break;

            case WorldPreset.GoldenHour:
                S(G(C(1f,.62f,.34f), C(1f,.72f,.42f), C(1f,.55f,.30f)), 1.15f,  6,30, 60,130, G1(C(.30f,.36f,.68f)), G1(C(1f,.78f,.54f)),  G1(C(.50f,.42f,.39f)), G1(C(1f,.86f,.62f)),  G1(C(.62f,.50f,.74f)), G1(C(.96f,.78f,.58f)), G1(C(.56f,.51f,.80f)), G1(C(.86f,.64f,.47f)), G1(C(.44f,.33f,.27f)), .15f, 22f, 16f, 1.35f, .82f); break;

            case WorldPreset.MistyMorning:
                S(G1(C(.96f,.94f,.88f)),                                0.85f, 10,35, 20, 80, G1(C(.54f,.64f,.76f)), G1(C(.87f,.90f,.92f)), G1(C(.63f,.65f,.67f)), G1(C(.95f,.96f,.97f)), G1(C(.75f,.79f,.85f)), G1(C(.86f,.89f,.90f)), G1(C(.63f,.69f,.77f)), G1(C(.69f,.73f,.71f)), G1(C(.53f,.53f,.51f)), .10f,  2f,  4f, 1.5f, .22f); break;

            case WorldPreset.SpringFresh:
                S(G1(C(1f,.97f,.90f)),                                  1.40f, 20,65, 30,120, G1(C(.25f,.58f,.95f)), G1(C(.78f,.92f,1f)),  G1(C(.60f,.66f,.60f)), G1(Color.white),       G1(C(.78f,.85f,.94f)), G1(C(.85f,.92f,.97f)), G1(C(.61f,.75f,.93f)), G1(C(.73f,.79f,.69f)), G1(C(.47f,.55f,.39f)), .30f,  4f, 20f, 0.6f, .45f); break;

            case WorldPreset.CandySunset:
                S(G(C(1f,.60f,.52f), C(1f,.68f,.55f), C(.98f,.52f,.55f)), 0.95f, 4,22, 70,140, G1(C(.38f,.30f,.62f)), G1(C(1f,.70f,.78f)),  G1(C(.50f,.40f,.52f)), G1(C(1f,.84f,.83f)),  G1(C(.63f,.53f,.81f)), G1(C(.95f,.77f,.83f)), G1(C(.60f,.53f,.85f)), G1(C(.84f,.62f,.68f)), G1(C(.43f,.33f,.47f)), .12f, 10f, 18f, 1.2f, .90f); break;

            case WorldPreset.StormLight:
                S(G1(C(1f,.72f,.40f)),                                  1.50f,  8,28, 45,110, G1(C(.20f,.23f,.33f)), G1(C(.72f,.62f,.47f)), G1(C(.37f,.36f,.37f)), G1(C(1f,.86f,.63f)),  G1(C(.35f,.36f,.45f)), G1(C(.71f,.65f,.55f)), G1(C(.39f,.43f,.55f)), G1(C(.53f,.51f,.43f)), G1(C(.31f,.29f,.27f)), .05f,  8f, 10f, 1.6f, .70f); break;

            case WorldPreset.GhibliMeadow:
                S(G1(C(1f,.96f,.88f)),                                  1.25f, 22,60, 30,115, G1(C(.44f,.66f,.86f)), G1(C(.87f,.92f,.83f)), G1(C(.60f,.64f,.55f)), G1(C(.99f,.98f,.94f)), G1(C(.70f,.77f,.85f)), G1(C(.87f,.91f,.83f)), G1(C(.62f,.76f,.82f)), G1(C(.74f,.80f,.66f)), G1(C(.46f,.54f,.38f)), .20f,  6f, 14f, 0.9f, .45f); break;

            case WorldPreset.WindWakerPop:
                S(G1(Color.white),                                      1.50f, 25,70, 30,120, G1(C(.10f,.52f,.95f)), G1(C(.55f,.90f,1f)),  G1(C(.55f,.65f,.55f)), G1(Color.white),       G1(C(.65f,.80f,.95f)), G1(C(.70f,.92f,1f)),  G1(C(.55f,.80f,1f)),  G1(C(.85f,.85f,.70f)), G1(C(.55f,.65f,.45f)), .35f,  2f, 28f, 0.4f, .50f); break;

            case WorldPreset.BotWPastel:
                S(G1(C(1f,.96f,.88f)),                                  1.10f, 16,52, 25,115, G1(C(.55f,.70f,.88f)), G1(C(.85f,.90f,.92f)), G1(C(.62f,.66f,.62f)), G1(C(.96f,.97f,.96f)), G1(C(.72f,.78f,.86f)), G1(C(.80f,.87f,.90f)), G1(C(.64f,.73f,.85f)), G1(C(.72f,.76f,.68f)), G1(C(.50f,.55f,.45f)), .18f,  5f,  6f, 1.0f, .48f); break;

            case WorldPreset.FirewatchPoster:
                S(G1(C(1f,.45f,.25f)),                                  1.20f,  5,20, 65,135, G1(C(.35f,.18f,.35f)), G1(C(1f,.55f,.30f)),  G1(C(.32f,.20f,.24f)), G1(C(1f,.62f,.40f)),  G1(C(.45f,.22f,.35f)), G1(C(.95f,.52f,.36f)), G1(C(.50f,.30f,.45f)), G1(C(.85f,.45f,.30f)), G1(C(.30f,.18f,.20f)), .05f, 18f, 20f, 1.3f, .85f); break;

            case WorldPreset.OriMystic:
                S(G1(C(.78f,.90f,1f)),                                  1.00f, 14,45, 30,110, G1(C(.15f,.25f,.55f)), G1(C(.38f,.75f,.85f)), G1(C(.30f,.35f,.50f)), G1(C(.78f,.90f,1f)),  G1(C(.35f,.40f,.70f)), G1(C(.46f,.70f,.80f)), G1(C(.36f,.50f,.80f)), G1(C(.42f,.65f,.70f)), G1(C(.26f,.30f,.50f)), .10f, -8f, 16f, 1.4f, .50f); break;

            case WorldPreset.TealOrangeCinema:
                S(G1(C(1f,.72f,.45f)),                                  1.35f, 12,45, 35,120, G1(C(.15f,.45f,.55f)), G1(C(.58f,.80f,.82f)), G1(C(.35f,.45f,.46f)), G1(C(1f,.88f,.72f)),  G1(C(.45f,.62f,.68f)), G1(C(.60f,.78f,.78f)), G1(C(.36f,.60f,.65f)), G1(C(.56f,.62f,.60f)), G1(C(.26f,.40f,.42f)), .15f, 16f, 15f, 1.1f, .55f); break;

            case WorldPreset.NordicFrost:
                S(G1(C(.92f,.95f,1f)),                                  0.95f,  8,30, 20, 95, G1(C(.45f,.55f,.70f)), G1(C(.80f,.85f,.90f)), G1(C(.55f,.58f,.62f)), G1(C(.92f,.94f,.97f)), G1(C(.60f,.66f,.76f)), G1(C(.82f,.86f,.90f)), G1(C(.58f,.64f,.74f)), G1(C(.62f,.66f,.70f)), G1(C(.45f,.48f,.52f)), .12f, -6f,  5f, 0.7f, .40f); break;

            case WorldPreset.TropicalPunch:
                S(G1(C(1f,.92f,.75f)),                                  1.45f, 25,68, 30,120, G1(C(.05f,.60f,.75f)), G1(C(.55f,.95f,.90f)), G1(C(.50f,.62f,.50f)), G1(Color.white),       G1(C(.55f,.78f,.85f)), G1(C(.70f,.94f,.90f)), G1(C(.40f,.80f,.85f)), G1(C(.95f,.75f,.45f)), G1(C(.45f,.60f,.40f)), .30f,  8f, 26f, 0.8f, .50f); break;

            case WorldPreset.AutumnAmber:
                S(G1(C(1f,.75f,.45f)),                                  1.20f, 12,40, 40,125, G1(C(.45f,.50f,.70f)), G1(C(.95f,.78f,.55f)), G1(C(.48f,.40f,.32f)), G1(C(1f,.90f,.72f)),  G1(C(.60f,.55f,.70f)), G1(C(.90f,.78f,.58f)), G1(C(.55f,.55f,.72f)), G1(C(.85f,.62f,.40f)), G1(C(.45f,.32f,.22f)), .18f, 16f, 17f, 1.2f, .60f); break;

            case WorldPreset.ChocolateBakery:
                S(G1(C(1f,.85f,.65f)),                                  1.10f, 15,45, 30,110, G1(C(.50f,.42f,.40f)), G1(C(.90f,.78f,.65f)), G1(C(.42f,.34f,.28f)), G1(C(.98f,.90f,.80f)), G1(C(.62f,.50f,.44f)), G1(C(.88f,.78f,.66f)), G1(C(.55f,.46f,.42f)), G1(C(.80f,.66f,.52f)), G1(C(.38f,.28f,.22f)), .15f, 14f, 13f, 0.8f, .50f); break;

            case WorldPreset.NeonDusk:
                S(G1(C(1f,.40f,.55f)),                                  0.85f,  3,15, 75,145, G1(C(.12f,.10f,.30f)), G1(C(.85f,.30f,.55f)), G1(C(.22f,.20f,.36f)), G1(C(.95f,.55f,.75f)), G1(C(.30f,.25f,.55f)), G1(C(.55f,.30f,.55f)), G1(C(.35f,.30f,.60f)), G1(C(.65f,.35f,.55f)), G1(C(.20f,.18f,.38f)), .00f,  4f, 22f, 1.0f, .92f); break;

            case WorldPreset.SepiaMemory:
                S(G1(C(1f,.90f,.72f)),                                  1.00f, 12,42, 30,115, G1(C(.62f,.58f,.48f)), G1(C(.85f,.78f,.64f)), G1(C(.55f,.50f,.42f)), G1(C(.92f,.86f,.74f)), G1(C(.66f,.60f,.50f)), G1(C(.85f,.78f,.64f)), G1(C(.66f,.62f,.54f)), G1(C(.72f,.66f,.56f)), G1(C(.52f,.46f,.38f)), .10f, 20f,  0f, 1.0f, .50f); break;
        }
        _weatherSmooth = weather;
        Apply();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    // ================================================================= ПОГОДЫ
    public void ApplyWeather(WeatherPreset p)
    {
        switch (p)
        {
            //     w     dark  over  cov ясно/буря   fogStart fogEnd  shadow min/max  rayB  fcDens  ветер облаков      скорость теней
            case WeatherPreset.ClearSky:       W(0.05f, 1.0f, 1.0f, .15f, .80f,  90f, 500f, .30f, .60f, 0.5f, .25f, .004f, .003f, 2f, 1f);   break;
            case WeatherPreset.FairClouds:     W(0.25f, 1.0f, 1.0f, .35f, .85f,  75f, 430f, .40f, .65f, 0.9f, .40f, .008f, .005f, 4f, 2f);   break;
            case WeatherPreset.SunbeamDay:     W(0.42f, 0.8f, 0.7f, .45f, .90f,  60f, 380f, .50f, .70f, 1.6f, .55f, .010f, .006f, 5f, 2.5f); break;
            case WeatherPreset.BrightOvercast: W(0.65f, 0.35f,0.85f,.70f, .95f,  55f, 320f, .18f, .35f, 0.35f,.60f, .012f, .007f, 6f, 3f);   break;
            case WeatherPreset.Gloomy:         W(0.75f, 0.8f, 1.0f, .75f, .97f,  40f, 240f, .30f, .50f, 0.2f, .70f, .012f, .008f, 6f, 3f);   break;
            case WeatherPreset.Drizzle:        W(0.80f, 0.7f, 1.0f, .80f, .98f,  25f, 150f, .25f, .45f, 0.15f,.80f, .015f, .009f, 7f, 4f);   break;
            case WeatherPreset.Storm:          W(1.00f, 1.0f, 1.0f, .85f, 1f,    18f, 120f, .55f, .80f, 0.1f, .90f, .020f, .012f, 9f, 5f);   break;
            case WeatherPreset.StormBreak:     W(0.62f, 1.0f, 0.75f,.70f, .95f,  30f, 190f, .55f, .78f, 1.9f, .75f, .016f, .010f, 7f, 4f);   break;
            case WeatherPreset.DenseFog:       W(0.55f, 0.45f,0.9f, .60f, .95f,   8f,  65f, .15f, .30f, 0.9f, 1f,   .004f, .002f, 2f, 1f);   break;
            case WeatherPreset.MorningHaze:    W(0.35f, 0.5f, 0.6f, .50f, .90f,  25f, 160f, .35f, .55f, 1.5f, .65f, .005f, .003f, 3f, 1.5f); break;
            case WeatherPreset.DustyHeat:      W(0.18f, 0.9f, 0.5f, .20f, .80f,  45f, 260f, .45f, .65f, 0.6f, .30f, .006f, .002f, 3f, 1f);   break;
            case WeatherPreset.AfterRain:      W(0.30f, 0.9f, 0.6f, .40f, .90f, 110f, 600f, .50f, .70f, 1.7f, .45f, .014f, .009f, 6f, 3f);   break;
        }
        _weatherSmooth = Mathf.MoveTowards(_weatherSmooth, weather, 0.001f); // плавный доход
        Apply();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    // ---------------------------------------------------------------- helpers
    private void S(Gradient sun, float inten, float eMin, float eMax, float azS, float azE,
                   Gradient zen, Gradient hor, Gradient gnd,
                   Gradient cLit, Gradient cShd, Gradient fogC,
                   Gradient aSky, Gradient aEq, Gradient aGnd,
                   float expo, float wb, float sat, float rayB, float tod)
    {
        sunColor = sun; sunIntensity = Flat(inten);
        sunElevationMin = eMin; sunElevationMax = eMax;
        sunAzimuthStart = azS; sunAzimuthEnd = azE;
        zenithColor = zen; horizonColor = hor; groundColor = gnd;
        cloudLitColor = cLit; cloudShadowColor = cShd; fogColor = fogC;
        ambientSky = aSky; ambientEquator = aEq; ambientGround = aGnd;
        postExposure = Flat(expo); whiteBalanceTemp = Flat(wb);
        baseSaturation = sat; rayBaseIntensity = rayB; timeOfDay = tod;
    }

    private void W(float wv, float dark, float over, float covC, float covS,
                   float fs, float fe, float shMin, float shMax,
                   float rayB, float fcDens, float windX, float windY, float shSpX, float shSpY)
    {
        weather = wv; darknessScale = dark; overcastScale = over;
        coverageClearStorm = new Vector2(covC, covS);
        fogStartClearStorm = new Vector2(fs, fs * 0.55f);
        fogEndClearStorm = new Vector2(fe, fe * 0.55f);
        shadowStrengthClearStorm = new Vector2(shMin, shMax);
        rayWeatherBoost = rayB; fogCloudDensity = fcDens;
        cloudWind = new Vector2(windX, windY);
        cloudShadowSpeed = new Vector2(shSpX, shSpY);
    }

    private Color TintAmb(Color c)
    {
        float w = corAmbWarmth;
        return new Color(c.r * (1f + w * .25f), c.g * (1f + w * .05f), c.b * (1f - w * .25f), 1f);
    }

    private static Color C(float r, float g, float b) => new Color(r, g, b);
    private static AnimationCurve Flat(float v) => AnimationCurve.Linear(0f, v, 1f, v);
    private static Gradient G1(Color c) => G(c, c, c);
    private static Gradient G(Color a, Color b, Color c)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 0.5f), new GradientColorKey(c, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

#if UNITY_EDITOR
    // ---------- контекст-меню: СТИЛИ ----------
    [ContextMenu("Стиль 01: Solarpunk-полдень")]   void S01() => ApplyPreset(WorldPreset.SolarpunkNoon);
    [ContextMenu("Стиль 02: Золотой час")]          void S02() => ApplyPreset(WorldPreset.GoldenHour);
    [ContextMenu("Стиль 03: Туманное утро")]        void S03() => ApplyPreset(WorldPreset.MistyMorning);
    [ContextMenu("Стиль 04: Весенняя свежесть")]    void S04() => ApplyPreset(WorldPreset.SpringFresh);
    [ContextMenu("Стиль 05: Карамельный закат")]    void S05() => ApplyPreset(WorldPreset.CandySunset);
    [ContextMenu("Стиль 06: Свет сквозь грозу")]    void S06() => ApplyPreset(WorldPreset.StormLight);
    [ContextMenu("Стиль 07: Луга Гибли")]           void S07() => ApplyPreset(WorldPreset.GhibliMeadow);
    [ContextMenu("Стиль 08: Wind Waker поп")]       void S08() => ApplyPreset(WorldPreset.WindWakerPop);
    [ContextMenu("Стиль 09: Пастель приключений")]  void S09() => ApplyPreset(WorldPreset.BotWPastel);
    [ContextMenu("Стиль 10: Постерный закат")]      void S10() => ApplyPreset(WorldPreset.FirewatchPoster);
    [ContextMenu("Стиль 11: Мистическая бирюза")]   void S11() => ApplyPreset(WorldPreset.OriMystic);
    [ContextMenu("Стиль 12: Teal и Orange кино")]   void S12() => ApplyPreset(WorldPreset.TealOrangeCinema);
    [ContextMenu("Стиль 13: Северный минимализм")]  void S13() => ApplyPreset(WorldPreset.NordicFrost);
    [ContextMenu("Стиль 14: Тропический пунш")]     void S14() => ApplyPreset(WorldPreset.TropicalPunch);
    [ContextMenu("Стиль 15: Янтарная осень")]       void S15() => ApplyPreset(WorldPreset.AutumnAmber);
    [ContextMenu("Стиль 16: Шоколадная пекарня")]   void S16() => ApplyPreset(WorldPreset.ChocolateBakery);
    [ContextMenu("Стиль 17: Неоновые сумерки")]     void S17() => ApplyPreset(WorldPreset.NeonDusk);
    [ContextMenu("Стиль 18: Сепия-воспоминание")]   void S18() => ApplyPreset(WorldPreset.SepiaMemory);

    // ---------- контекст-меню: ПОГОДЫ ----------
    [ContextMenu("Погода 01: Ясно")]                 void W01() => ApplyWeather(WeatherPreset.ClearSky);
    [ContextMenu("Погода 02: Лёгкие облака")]        void W02() => ApplyWeather(WeatherPreset.FairClouds);
    [ContextMenu("Погода 03: Лучистый день ★")]      void W03() => ApplyWeather(WeatherPreset.SunbeamDay);
    [ContextMenu("Погода 04: Светлая пасмурность")]  void W04() => ApplyWeather(WeatherPreset.BrightOvercast);
    [ContextMenu("Погода 05: Хмуро")]                void W05() => ApplyWeather(WeatherPreset.Gloomy);
    [ContextMenu("Погода 06: Морось")]               void W06() => ApplyWeather(WeatherPreset.Drizzle);
    [ContextMenu("Погода 07: Гроза")]                void W07() => ApplyWeather(WeatherPreset.Storm);
    [ContextMenu("Погода 08: Разрыв грозы ★")]       void W08() => ApplyWeather(WeatherPreset.StormBreak);
    [ContextMenu("Погода 09: Плотный туман")]        void W09() => ApplyWeather(WeatherPreset.DenseFog);
    [ContextMenu("Погода 10: Утренняя дымка")]       void W10() => ApplyWeather(WeatherPreset.MorningHaze);
    [ContextMenu("Погода 11: Сухая жара")]           void W11() => ApplyWeather(WeatherPreset.DustyHeat);
    [ContextMenu("Погода 12: После дождя")]          void W12() => ApplyWeather(WeatherPreset.AfterRain);

    private void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        UnityEditor.EditorApplication.delayCall += () => { if (this != null) Apply(); };
    }
#endif
}
