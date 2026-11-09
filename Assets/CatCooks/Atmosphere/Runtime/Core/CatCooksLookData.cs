using UnityEngine;

// ============================================================================
//  ДАННЫЕ ПРЕСЕТОВ АТМОСФЕРЫ
//
//  Три уровня, каждый отвечает ровно за одно:
//
//    CatCooksPalette       — ЦВЕТ. Голая гамма: небо, облака, солнце,
//                            свет, туман. Ничего кроме цвета.
//    CatCooksSkyStyle      — ПОДАЧА. Палитра + мультяшность + пост.
//                            Один стиль может использовать чужую палитру.
//    CatCooksWeatherPreset — СТРУКТУРА. Плотность облаков, ветер, лучи,
//                            туман, тени. Цвета не трогает вообще.
//
//  ЗАЧЕМ ТАКОЕ ДЕЛЕНИЕ. Раньше все 18 стилей были switch-ом в коде
//  директора: чтобы поправить оттенок теней, художник лез в C#. И оси
//  были склеены — «гроза» несла в себе и цвет, и плотность облаков,
//  поэтому гроза в тёплой гамме и в холодной требовали двух отдельных
//  кейсов. Теперь палитр может быть сколько угодно, стилей поверх них —
//  тоже, а любая погода накладывается на любой стиль.
//
//  Все три — ScriptableObject: правятся в инспекторе, лежат в проекте,
//  видны в системе контроля версий как отдельные файлы.
// ============================================================================

[CreateAssetMenu(fileName = "Palette", menuName = "CatCooks/Атмосфера/Палитра", order = 0)]
public class CatCooksPalette : ScriptableObject
{
    [TextArea] public string note;

    [Header("Небо")]
    public Color zenith = new Color(0.30f, 0.55f, 0.90f);
    public Color horizon = new Color(0.75f, 0.85f, 0.95f);
    public Color ground = new Color(0.55f, 0.60f, 0.65f);

    [Header("Облака")]
    [Tooltip("Макушка, куда бьёт солнце")]
    public Color cloudLit = Color.white;

    [Tooltip("Средний тон — бок облака")]
    public Color cloudShadow = new Color(0.66f, 0.72f, 0.86f);

    [Tooltip("Основание. Должно быть ЗАМЕТНО темнее среднего тона: " +
             "у облака под низом вся его толща. Светлое основание — " +
             "первое, из-за чего облако выглядит плоским пятном")]
    public Color cloudBase = new Color(0.38f, 0.42f, 0.56f);

    [Tooltip("Свет неба, падающий на облака сверху")]
    public Color cloudAmbientTop = new Color(0.32f, 0.46f, 0.68f);

    [Tooltip("Отражённый от земли свет, подсвечивает низ облака")]
    public Color cloudAmbientBottom = new Color(0.30f, 0.32f, 0.36f);

    [Header("Солнце")]
    public Color sunDisc = new Color(1f, 0.95f, 0.85f);
    public Color sunLight = new Color(1f, 0.96f, 0.88f);

    [Header("Заполняющий свет")]
    public Color ambientSky = new Color(0.55f, 0.66f, 0.82f);
    public Color ambientEquator = new Color(0.52f, 0.54f, 0.58f);
    public Color ambientGround = new Color(0.34f, 0.32f, 0.34f);

    [Header("Даль")]
    public Color fogNear = new Color(0.88f, 0.91f, 0.93f);
    public Color fogFar = new Color(0.74f, 0.82f, 0.90f);
    public Color rayTint = new Color(1f, 0.97f, 0.88f);
    public Color windStreak = new Color(1f, 1f, 1f);

    /// <summary>Линейная смесь двух палитр — основа плавных переходов.</summary>
    public static void Blend(CatCooksPalette a, CatCooksPalette b, float t, CatCooksPalette dst)
    {
        if (a == null || b == null || dst == null) return;
        dst.zenith = Color.Lerp(a.zenith, b.zenith, t);
        dst.horizon = Color.Lerp(a.horizon, b.horizon, t);
        dst.ground = Color.Lerp(a.ground, b.ground, t);
        dst.cloudLit = Color.Lerp(a.cloudLit, b.cloudLit, t);
        dst.cloudShadow = Color.Lerp(a.cloudShadow, b.cloudShadow, t);
        dst.cloudBase = Color.Lerp(a.cloudBase, b.cloudBase, t);
        dst.cloudAmbientTop = Color.Lerp(a.cloudAmbientTop, b.cloudAmbientTop, t);
        dst.cloudAmbientBottom = Color.Lerp(a.cloudAmbientBottom, b.cloudAmbientBottom, t);
        dst.sunDisc = Color.Lerp(a.sunDisc, b.sunDisc, t);
        dst.sunLight = Color.Lerp(a.sunLight, b.sunLight, t);
        dst.ambientSky = Color.Lerp(a.ambientSky, b.ambientSky, t);
        dst.ambientEquator = Color.Lerp(a.ambientEquator, b.ambientEquator, t);
        dst.ambientGround = Color.Lerp(a.ambientGround, b.ambientGround, t);
        dst.fogNear = Color.Lerp(a.fogNear, b.fogNear, t);
        dst.fogFar = Color.Lerp(a.fogFar, b.fogFar, t);
        dst.rayTint = Color.Lerp(a.rayTint, b.rayTint, t);
        dst.windStreak = Color.Lerp(a.windStreak, b.windStreak, t);
    }
}


// ============================================================================
[CreateAssetMenu(fileName = "Style", menuName = "CatCooks/Атмосфера/Стиль", order = 1)]
public class CatCooksSkyStyle : ScriptableObject
{
    public string title = "Без названия";
    [TextArea] public string note;

    [Header("Палитра")]
    public CatCooksPalette palette;

    [Header("Солнце")]
    [Range(0f, 3f)] public float sunIntensity = 1.1f;
    [Range(0f, 90f)] public float sunElevation = 42f;
    [Range(0f, 360f)] public float sunAzimuth = 40f;
    [Range(0f, 5f)] public float sunDiscIntensity = 2f;
    [Range(0f, 2f)] public float sunGlow = 0.5f;

    [Header("Мультяшность неба")]
    [Tooltip("1 = гладкий градиент, 3–5 = рисованные полосы")]
    [Range(1f, 8f)] public float skyBands = 1f;
    [Range(0.01f, 1f)] public float bandSoftness = 0.6f;

    [Header("Мультяшность облаков")]
    [Tooltip("0 = мягкая заливка, 1 = чистые ступени")]
    [Range(0f, 1f)] public float cloudToon = 0.55f;
    [Range(2f, 6f)] public float cloudSteps = 3f;

    [Tooltip("Свет заходит за терминатор — облако просвечивает")]
    [Range(0f, 1f)] public float lightWrap = 0.55f;

    [Tooltip("Вспышка кромки, когда смотришь в сторону солнца")]
    [Range(0f, 3f)] public float silverLining = 1.1f;

    [Tooltip("Насколько низ облака темнее верха")]
    [Range(0f, 1f)] public float cloudBottomShade = 0.45f;

    [Tooltip("Бугристость поверхности облака")]
    [Range(0f, 0.35f)] public float cloudBump = 0.14f;

    [Header("Скайбоксные облака")]
    [Range(0.01f, 0.5f)] public float skyCloudSoft = 0.11f;
    [Range(0f, 1f)] public float skyCloudPuff = 0.85f;
    [Range(0f, 1.5f)] public float skyCloudWarp = 0.55f;
    [Range(0f, 1f)] public float skyCloudOutline = 0f;
    [Range(0.2f, 8f)] public float skyCloudScale = 2f;

    [Header("Пост-обработка")]
    [Range(-2f, 2f)] public float postExposure = 0f;
    [Range(-100f, 100f)] public float saturation = 0f;
    [Range(-100f, 100f)] public float contrast = 0f;
    [Range(-100f, 100f)] public float temperature = 0f;
    [Range(0f, 2f)] public float bloom = 0.35f;
    [Range(0f, 0.8f)] public float vignette = 0.2f;

    [Header("Заполняющий свет")]
    [Range(0f, 2f)] public float ambientBoost = 1f;
}


// ============================================================================
[CreateAssetMenu(fileName = "Weather", menuName = "CatCooks/Атмосфера/Погода", order = 2)]
public class CatCooksWeatherPreset : ScriptableObject
{
    public string title = "Без названия";
    [TextArea] public string note;

    [Header("Общая величина непогоды")]
    [Tooltip("0 = ясно, 1 = максимум. Ею питаются ветер, туман и тени.")]
    [Range(0f, 1f)] public float severity = 0f;

    [Header("Облака")]
    [Range(0f, 1f)] public float coverage = 0.45f;
    [Range(0f, 1f)] public float skyCoverage = 0.5f;
    [Range(0f, 1f)] public float darkness = 0f;
    [Range(0f, 1f)] public float overcast = 0f;

    [Tooltip("Высота облачного яруса, м. В грозу облака садятся ниже.")]
    public float cloudHeight = 230f;

    // ====================================================================
    //  ФОРМА ОБЛАКОВ
    //  Отдельно от плотности — и это принципиально. Гроза и ясный день
    //  отличаются не только количеством облаков, но и их ПОРОДОЙ: в грозу
    //  компактные кучевые, в ясную погоду редкие вытянутые перья.
    //  Пока форма была общей, ясная погода выглядела как гроза,
    //  у которой просто убавили облака, — отсюда и мелкие обрывки.
    // ====================================================================
    [Header("Форма облаков")]
    [Tooltip("Растяжка вдоль ветра. 1 = кучевые, 3–6 = перистые полосы.")]
    [Range(1f, 8f)] public float cloudStretch = 1.5f;

    [Tooltip("Волокнистость. У перистых это вся форма, у кучевых почти ноль.")]
    [Range(0f, 1f)] public float cloudWisp = 0.2f;

    [Tooltip("Ширина отдельного облака. Меньше — компактные, " +
             "больше — размашистые поля.")]
    [Range(0.3f, 2f)] public float cloudWidth = 1.1f;

    // ====================================================================
    //  ОБЪЁМНЫЕ ТУЧИ
    //  Отдельные поля, потому что у тел есть то, чего не было у слоя:
    //  собственный РАЗМЕР и КОЛИЧЕСТВО. Ясный день — это не «то же небо
    //  пожиже», а десяток мелких кучевых; гроза — полтора десятка
    //  огромных, сомкнутых в потолок. Одним покрытием такое не задать.
    // ====================================================================
    [Header("Объёмные тучи")]
    [Tooltip("Множитель размера тел. Ясная погода 0.6–0.8, гроза 1.4–1.8.")]
    [Range(0.3f, 2.5f)] public float cloudBodyScale = 1f;

    [Tooltip("Доля тел из пула, которые вообще участвуют. Отдельно от " +
             "покрытия: редкие крупные тучи и частые мелкие — разное небо " +
             "при одинаковой доле закрытого неба.")]
    [Range(0.1f, 1f)] public float cloudPopulation = 0.7f;

    [Tooltip("Покачивание. В штиль почти ноль, в шторм заметное.")]
    [Range(0f, 0.3f)] public float cloudSway = 0.06f;

    [Header("Низкая дымка")]
    public bool hazeEnabled = false;
    [Range(0f, 1f)] public float hazeDensity = 0.3f;

    [Header("Туман")]
    public bool fogEnabled = false;
    [Range(0f, 1f)] public float fogDensity = 0.6f;
    public float fogStart = 60f;
    public float fogEnd = 420f;

    [Tooltip("Выше этой высоты тумана нет. Ставь чуть выше крыш.")]
    public float fogHeightTop = 60f;

    [Header("Солнечные лучи")]
    [Range(0f, 3f)] public float rayIntensity = 1f;

    [Header("Тени от облаков")]
    public bool cloudShadows = true;
    [Range(0f, 1f)] public float shadowStrength = 0.45f;

    [Header("Дождь и молния")]
    [Range(0f, 1f)] public float rain = 0f;

    [Tooltip("0 = молний нет, 1 = гроза")]
    [Range(0f, 1f)] public float lightning = 0f;

    // ====================================================================
    //  НАСТРОЕНИЕ ПОГОДЫ.
    //  Складывается со стилем, а не заменяет его. Стиль отвечает за гамму
    //  сцены, погода — за то, как эта гамма себя чувствует: гроза давит
    //  и обесцвечивает, утро осветляет и теплеет, туман съедает контраст.
    //  Без этого все двенадцать погод выглядели одинаково — менялись
    //  только облака.
    //
    //  Все значения — СМЕЩЕНИЯ. Ноль = стиль как есть.
    // ====================================================================
    [Header("Настроение (поверх стиля)")]
    [Tooltip("Смещение экспозиции. МИНУС для непогоды. Плюс ставь скупо: " +
             "пересвет бьёт по глазам сильнее, чем недосвет.")]
    [Range(-1f, 0.4f)] public float exposureShift = 0f;

    [Tooltip("Смещение насыщенности. В непогоду цвет уходит из мира.")]
    [Range(-60f, 30f)] public float saturationShift = 0f;

    [Tooltip("Смещение контраста")]
    [Range(-40f, 40f)] public float contrastShift = 0f;

    [Tooltip("Смещение температуры. Минус — холоднее и синее, " +
             "плюс — теплее и золотистее.")]
    [Range(-40f, 40f)] public float temperatureShift = 0f;

    [Tooltip("Добавка к виньетке. Непогода сжимает кадр по краям.")]
    [Range(0f, 0.4f)] public float vignetteAdd = 0f;

    [Tooltip("Добавка к свечению. Утро и разрывы туч светятся сильнее.")]
    [Range(-0.5f, 1f)] public float bloomAdd = 0f;

    [Tooltip("Множитель яркости солнца. Меньше единицы для пасмурного.")]
    [Range(0.2f, 1.2f)] public float sunDim = 1f;

    [Header("Эффекты на камере")]
    [Tooltip("Тёмная рамка по краям кадра. В грозу 0.5–0.7: картинка " +
             "сжимается и давит. В ясный день 0.1.")]
    [Range(0f, 1f)] public float screenFrame = 0.15f;

    [Tooltip("Цвет рамки. Не чёрный, а холодный тёмный — чёрная читается " +
             "как ошибка рендера, цветная как атмосфера.")]
    public Color frameColor = new Color(0.22f, 0.26f, 0.36f);

    [Tooltip("Обесцвечивание по краям кадра")]
    [Range(0f, 1f)] public float edgeDesaturate = 0.35f;

    [Tooltip("Насколько быстро мир промокает в эту погоду. Множитель " +
             "поверх дождя: в грозу с ветром вода залетает под навесы.")]
    [Range(0f, 2f)] public float wetnessRate = 1f;

    [Header("Ветер")]
    [Range(0f, 1f)] public float wind = 0.3f;

    [Tooltip("Видимые росчерки ветра. В штиль их не бывает.")]
    [Range(0f, 1f)] public float windStreaks = 0.25f;

    [Header("Живое небо")]
    [Tooltip("Как быстро облака скайбокса МЕНЯЮТ ФОРМУ. 0 = застывшие. " +
             "Ясно 0.03–0.05, ветрено 0.08, гроза 0.12–0.18.")]
    [Range(0f, 0.3f)] public float cloudEvolve = 0.05f;

    [Header("Воздух: листья и пыль")]
    [Tooltip("Листопад. Множитель поверх ветра: при сильном ветре листьев больше, " +
             "в дождь меньше. 0 = нет листьев.")]
    [Range(0f, 1f)] public float leaves = 0.3f;

    [Tooltip("Песчаная пыль у земли (Volume Fog, режим Sand). 0 = чистый воздух, " +
             "1 = пыльная буря.")]
    [Range(0f, 1f)] public float dust = 0f;

    [Tooltip("Пылинки в солнечных лучах. Сами гаснут в дождь, пасмурность и ночью.")]
    [Range(0f, 1f)] public float sunDust = 0.5f;

    [System.Flags]
    public enum Hazard { None = 0, Rain = 1, Wind = 2, Storm = 4, Fog = 8, Dust = 16, Heat = 32, Cold = 64 }

    [Header("Геймплей")]
    [Tooltip("Сложность этой погоды для игрока 0..1. -1 = брать Severity.")]
    [Range(-1f, 1f)] public float difficulty = -1f;
    [Tooltip("Что эта погода делает с геймплеем (читают системы игры через CatCooksWeatherState.Hazards).")]
    public Hazard hazards = Hazard.None;
    [Tooltip("С какого игрового дня эта погода может выпадать по расписанию (прогресс). 1 = сразу.")]
    [Min(1)] public int unlockDay = 1;

    [Header("Переход")]
    [Tooltip("За сколько секунд погода меняется на эту, если не указано иное.")]
    public float transitionSeconds = 12f;

    /// <summary>Смесь двух погод — для плавного перехода.</summary>
    public static void Blend(CatCooksWeatherPreset a, CatCooksWeatherPreset b,
                             float t, CatCooksWeatherPreset dst)
    {
        if (a == null || b == null || dst == null) return;
        dst.severity = Mathf.Lerp(a.severity, b.severity, t);
        dst.coverage = Mathf.Lerp(a.coverage, b.coverage, t);
        dst.skyCoverage = Mathf.Lerp(a.skyCoverage, b.skyCoverage, t);
        dst.darkness = Mathf.Lerp(a.darkness, b.darkness, t);
        dst.overcast = Mathf.Lerp(a.overcast, b.overcast, t);
        dst.cloudHeight = Mathf.Lerp(a.cloudHeight, b.cloudHeight, t);
        dst.cloudStretch = Mathf.Lerp(a.cloudStretch, b.cloudStretch, t);
        dst.cloudWisp = Mathf.Lerp(a.cloudWisp, b.cloudWisp, t);
        dst.cloudWidth = Mathf.Lerp(a.cloudWidth, b.cloudWidth, t);
        dst.cloudBodyScale = Mathf.Lerp(a.cloudBodyScale, b.cloudBodyScale, t);
        dst.cloudPopulation = Mathf.Lerp(a.cloudPopulation, b.cloudPopulation, t);
        dst.cloudSway = Mathf.Lerp(a.cloudSway, b.cloudSway, t);

        // Булевы переключаются по середине перехода — плавно их не сделать.
        dst.hazeEnabled = t < 0.5f ? a.hazeEnabled : b.hazeEnabled;
        dst.hazeDensity = Mathf.Lerp(a.hazeDensity, b.hazeDensity, t);

        // Туман считается «включённым», если он есть хотя бы на одном конце:
        // иначе он моргнёт в середине перехода вместо того, чтобы наплыть.
        dst.fogEnabled = a.fogEnabled || b.fogEnabled;
        dst.fogDensity = Mathf.Lerp(a.fogEnabled ? a.fogDensity : 0f,
                                    b.fogEnabled ? b.fogDensity : 0f, t);
        dst.fogStart = Mathf.Lerp(a.fogStart, b.fogStart, t);
        dst.fogEnd = Mathf.Lerp(a.fogEnd, b.fogEnd, t);
        dst.fogHeightTop = Mathf.Lerp(a.fogHeightTop, b.fogHeightTop, t);

        dst.rayIntensity = Mathf.Lerp(a.rayIntensity, b.rayIntensity, t);
        dst.cloudShadows = a.cloudShadows || b.cloudShadows;
        dst.shadowStrength = Mathf.Lerp(a.cloudShadows ? a.shadowStrength : 0f,
                                        b.cloudShadows ? b.shadowStrength : 0f, t);
        dst.rain = Mathf.Lerp(a.rain, b.rain, t);
        dst.lightning = Mathf.Lerp(a.lightning, b.lightning, t);
        dst.exposureShift = Mathf.Lerp(a.exposureShift, b.exposureShift, t);
        dst.saturationShift = Mathf.Lerp(a.saturationShift, b.saturationShift, t);
        dst.contrastShift = Mathf.Lerp(a.contrastShift, b.contrastShift, t);
        dst.temperatureShift = Mathf.Lerp(a.temperatureShift, b.temperatureShift, t);
        dst.vignetteAdd = Mathf.Lerp(a.vignetteAdd, b.vignetteAdd, t);
        dst.bloomAdd = Mathf.Lerp(a.bloomAdd, b.bloomAdd, t);
        dst.sunDim = Mathf.Lerp(a.sunDim, b.sunDim, t);
        dst.screenFrame = Mathf.Lerp(a.screenFrame, b.screenFrame, t);
        dst.frameColor = Color.Lerp(a.frameColor, b.frameColor, t);
        dst.edgeDesaturate = Mathf.Lerp(a.edgeDesaturate, b.edgeDesaturate, t);
        dst.wetnessRate = Mathf.Lerp(a.wetnessRate, b.wetnessRate, t);
        dst.wind = Mathf.Lerp(a.wind, b.wind, t);
        dst.windStreaks = Mathf.Lerp(a.windStreaks, b.windStreaks, t);
        dst.cloudEvolve = Mathf.Lerp(a.cloudEvolve, b.cloudEvolve, t);
        dst.leaves = Mathf.Lerp(a.leaves, b.leaves, t);
        dst.dust = Mathf.Lerp(a.dust, b.dust, t);
        dst.sunDust = Mathf.Lerp(a.sunDust, b.sunDust, t);
    }
}
