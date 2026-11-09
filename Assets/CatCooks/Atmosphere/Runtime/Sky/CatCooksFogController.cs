using UnityEngine;

// ============================================================================
//  CatCooksFogController — единственный хозяин тумана в проекте.
//
//  Пишет глобальные шейдерные параметры _CatFog*, которые читает
//  CatCooks/Stylized Fog (fullscreen renderer feature).
//
//  И СРАЗУ ГЛУШИТ СТАРЫЙ ТУМАН: RenderSettings.fog = false каждый кадр.
//  Это принципиально. Раньше за RenderSettings.fog дрались трое —
//  SkyController в своём Update, CatCooksWorldDirector через поля sky,
//  и CandyWorldConductor. Кто последний в кадре, тот и прав, а порядок
//  Update Unity не гарантирует — отсюда «туман какой-то странный».
//  Теперь источник один, и он здесь.
//
//  ПЕРЕД ЗАПУСКОМ снять две галки, иначе они будут включать туман обратно:
//    SkyController            → Enable Fog  = OFF
//    CatCooksWorldDirector    → Use Fog     = OFF
//  (Контроллер всё равно погасит его следующим кадром, но лишний
//   пинг-понг за один флаг лучше убрать.)
//
//  Установка: пустой GameObject → этот компонент. Материал тумана
//  подключается не сюда, а в Renderer Feature (см. шапку шейдера).
//
//  Сеть: чистая косметика, НЕ NetworkObject.
// ============================================================================
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Atmosphere/Fog Controller")]
public class CatCooksFogController : MonoBehaviour
{
    public enum FogPreset
    {
        Off,
        ClearAir,      // почти нет — только даль мягчеет
        SoftHaze,      // дефолт: лёгкая дымка в низинах
        MorningMist,   // утро: густо внизу, чисто выше крыш
        Valley,        // долина в молоке, верхушки торчат
        Overcast,      // ровная пасмурная пелена
        Storm,         // близкий тёмный туман
        Milk           // «плотный туман»: видно на 40 м
    }

    [Header("Пресет")]
    public FogPreset preset = FogPreset.SoftHaze;

    [Tooltip("Применять пресет. Сними, чтобы крутить всё вручную.")]
    public bool usePreset = true;

    [Header("Цвет")]
    [Tooltip("Тон воздуха вблизи — обычно теплее и светлее дали")]
    public Color colorNear = new Color(0.88f, 0.91f, 0.93f);

    [Tooltip("Тон дали. Держи его близким к Horizon Color скайбокса, " +
             "иначе на линии горизонта будет видна склейка.")]
    public Color colorFar = new Color(0.74f, 0.82f, 0.90f);

    [Tooltip("Цвет засветки со стороны солнца")]
    public Color sunColor = new Color(1f, 0.94f, 0.80f);

    [Header("Автосвязь с небом")]
    [Tooltip("Тянуть colorFar из SkyController.horizonColor — тогда туман " +
             "сам следует за стилем и не надо красить его руками 18 раз")]
    public bool matchSkyHorizon = true;
    public SkyController sky;

    [Range(0f, 1f)] public float skyMatchAmount = 0.85f;

    [Header("Дистанция")]
    public float start = 60f;
    public float end = 420f;

    [Tooltip("Форма набега. 1 = линейно, 2 = долго чисто и резко в конце, " +
             "0.5 = сразу мутнеет.")]
    [Range(0.2f, 4f)] public float curve = 1.6f;

    [Range(0f, 1f)] public float density = 0.85f;

    [Header("Высота")]
    [Tooltip("Ниже этой высоты туман максимальный, м")]
    public float heightBottom = 0f;

    [Tooltip("Выше этой высоты тумана нет, м. Ставь чуть выше крыш — " +
             "тогда постройки и горы будут торчать из дымки.")]
    public float heightTop = 60f;

    [Tooltip("0 = туман только по дистанции (как старый Unity Fog), " +
             "1 = только по высоте. 0.7–0.9 — то, что нужно.")]
    [Range(0f, 1f)] public float heightAmount = 0.8f;

    [Header("Солнце")]
    [Tooltip("Насколько узкая засветка вокруг солнца. Больше = уже.")]
    [Range(1f, 32f)] public float sunPower = 8f;

    [Range(0f, 1f)] public float sunAmount = 0.45f;

    [Header("Мультяшность")]
    [Tooltip("Число ступеней плотности")]
    [Range(2f, 8f)] public float bands = 4f;

    [Tooltip("0 = гладкий набег, 1 = чистые ступени")]
    [Range(0f, 1f)] public float toon = 0.35f;

    [Header("Стык с небом")]
    [Tooltip("Ширина полосы тумана у горизонта на самом небе. " +
             "Слишком много — небо сядет на землю, слишком мало — будет шов.")]
    [Range(0f, 0.5f)] public float skyBand = 0f;

    [Header("Погода (ведёт CatCooksWorldDirector)")]
    [Tooltip("0 = ясно, 1 = максимум непогоды. Сжимает дистанции и " +
             "поднимает плотность поверх пресета.")]
    [Range(0f, 1f)] public float weather = 0f;

    // ───────── id свойств ─────────
    static readonly int NearId = Shader.PropertyToID("_CatFogColorNear");
    static readonly int FarId = Shader.PropertyToID("_CatFogColorFar");
    static readonly int SunColId = Shader.PropertyToID("_CatFogSunColor");
    static readonly int StartId = Shader.PropertyToID("_CatFogStart");
    static readonly int EndId = Shader.PropertyToID("_CatFogEnd");
    static readonly int DensityId = Shader.PropertyToID("_CatFogDensity");
    static readonly int CurveId = Shader.PropertyToID("_CatFogCurve");
    static readonly int HBotId = Shader.PropertyToID("_CatFogHeightBottom");
    static readonly int HTopId = Shader.PropertyToID("_CatFogHeightTop");
    static readonly int HAmtId = Shader.PropertyToID("_CatFogHeightAmount");
    static readonly int SunPowId = Shader.PropertyToID("_CatFogSunPower");
    static readonly int SunAmtId = Shader.PropertyToID("_CatFogSunAmount");
    static readonly int BandsId = Shader.PropertyToID("_CatFogBands");
    static readonly int ToonId = Shader.PropertyToID("_CatFogToon");
    static readonly int SkyBandId = Shader.PropertyToID("_CatFogSkyBand");

    void OnEnable() { AutoFind(); Apply(); }
    void LateUpdate() { Apply(); }

#if UNITY_EDITOR
    void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        { if (this != null) { AutoFind(); Apply(); } };
    }
#endif

    void OnDisable()
    {
        // Уходя, гасим за собой: иначе выключенный компонент оставит
        // сцену в тумане, и это будет выглядеть как баг шейдера.
        Shader.SetGlobalFloat(DensityId, 0f);
    }

    void AutoFind()
    {
        if (sky == null) sky = FindFirstObjectByType<SkyController>();
    }

    public void Apply()
    {
        if (usePreset) LoadPreset(preset);

        // СТАРЫЙ ТУМАН ВЫКЛЮЧЕН. Один хозяин на один ресурс.
        if (RenderSettings.fog) RenderSettings.fog = false;

        float w = Mathf.Clamp01(weather);

        Color far = colorFar;
        if (matchSkyHorizon && sky != null)
            far = Color.Lerp(colorFar, sky.horizonColor, skyMatchAmount);

        // Непогода: даль подтягивается ближе, плотность растёт,
        // солнечная засветка гаснет.
        float s = Mathf.Lerp(start, start * 0.35f, w);
        float e = Mathf.Lerp(end, end * 0.35f, w);
        float d = Mathf.Clamp01(Mathf.Lerp(density, Mathf.Min(1f, density + 0.25f), w));
        float sunA = sunAmount * (1f - w * 0.75f);

        Shader.SetGlobalColor(NearId, colorNear);
        Shader.SetGlobalColor(FarId, far);
        Shader.SetGlobalColor(SunColId, sunColor);

        Shader.SetGlobalFloat(StartId, s);
        Shader.SetGlobalFloat(EndId, Mathf.Max(e, s + 1f));
        Shader.SetGlobalFloat(DensityId, d);
        Shader.SetGlobalFloat(CurveId, curve);

        Shader.SetGlobalFloat(HBotId, heightBottom);
        Shader.SetGlobalFloat(HTopId, Mathf.Max(heightTop, heightBottom + 1f));
        Shader.SetGlobalFloat(HAmtId, heightAmount);

        Shader.SetGlobalFloat(SunPowId, sunPower);
        Shader.SetGlobalFloat(SunAmtId, sunA);

        Shader.SetGlobalFloat(BandsId, bands);
        Shader.SetGlobalFloat(ToonId, toon);
        Shader.SetGlobalFloat(SkyBandId, skyBand);
    }

    /// <summary>Дёргать из погодных пресетов директора: 0 = ясно, 1 = буря.</summary>
    public void SetWeather(float t) { weather = Mathf.Clamp01(t); Apply(); }

    // ================================================================ пресеты
    void LoadPreset(FogPreset p)
    {
        switch (p)
        {
            case FogPreset.Off:
                density = 0f;
                break;

            case FogPreset.ClearAir:
                start = 180f; end = 900f; curve = 2.2f; density = 0.35f;
                heightBottom = 0f; heightTop = 140f; heightAmount = 0.45f;
                sunPower = 10f; sunAmount = 0.35f; bands = 5f; toon = 0.2f;
                skyBand = 0.06f;
                break;

            case FogPreset.SoftHaze:
                start = 60f; end = 420f; curve = 1.6f; density = 0.85f;
                heightBottom = 0f; heightTop = 60f; heightAmount = 0.80f;
                sunPower = 8f; sunAmount = 0.45f; bands = 4f; toon = 0.35f;
                skyBand = 0.10f;
                break;

            case FogPreset.MorningMist:
                start = 25f; end = 260f; curve = 1.2f; density = 0.95f;
                heightBottom = 0f; heightTop = 28f; heightAmount = 0.95f;
                sunPower = 5f; sunAmount = 0.70f; bands = 4f; toon = 0.30f;
                skyBand = 0.14f;
                break;

            case FogPreset.Valley:
                start = 15f; end = 200f; curve = 1.0f; density = 1f;
                heightBottom = 0f; heightTop = 18f; heightAmount = 1f;
                sunPower = 6f; sunAmount = 0.55f; bands = 3f; toon = 0.45f;
                skyBand = 0.12f;
                break;

            case FogPreset.Overcast:
                start = 45f; end = 300f; curve = 1.3f; density = 0.9f;
                heightBottom = 0f; heightTop = 120f; heightAmount = 0.35f;
                sunPower = 16f; sunAmount = 0.10f; bands = 5f; toon = 0.15f;
                skyBand = 0.16f;
                break;

            case FogPreset.Storm:
                start = 18f; end = 130f; curve = 1.1f; density = 1f;
                heightBottom = 0f; heightTop = 90f; heightAmount = 0.45f;
                sunPower = 20f; sunAmount = 0.05f; bands = 3f; toon = 0.25f;
                skyBand = 0.20f;
                break;

            case FogPreset.Milk:
                start = 5f; end = 45f; curve = 1f; density = 1f;
                heightBottom = 0f; heightTop = 200f; heightAmount = 0.15f;
                sunPower = 3f; sunAmount = 0.40f; bands = 4f; toon = 0.20f;
                skyBand = 0.30f;
                break;
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Проверить настройку")]
    void CheckSetup()
    {
        var sb = new System.Text.StringBuilder("[Fog] Проверка\n");

        var urp = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline
                  as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        if (urp == null)
            sb.AppendLine("  ОШИБКА: в Graphics не назначен URP Asset.");
        else if (!urp.supportsCameraDepthTexture)
            sb.AppendLine("  ОШИБКА: Depth Texture выключен в URP Asset — " +
                          "туман не увидит геометрию и не отрисуется.");

        if (RenderSettings.fog)
            sb.AppendLine("  ВНИМАНИЕ: старый Unity Fog кто-то включает обратно. " +
                          "Сними Enable Fog в SkyController и Use Fog в WorldDirector.");

        if (sky != null && matchSkyHorizon)
            sb.AppendLine($"  Даль тянется из неба: {ColorUtility.ToHtmlStringRGB(sky.horizonColor)}");

        if (heightTop <= heightBottom + 1f)
            sb.AppendLine("  ОШИБКА: Height Top не выше Height Bottom.");
        if (end <= start)
            sb.AppendLine("  ОШИБКА: End не больше Start.");

        sb.AppendLine("  Не забудь Full Screen Pass Renderer Feature с материалом " +
                      "Hidden/CatCooks/Stylized Fog, Injection Point = " +
                      "Before Rendering Transparents.");

        Debug.Log(sb.ToString(), this);
    }
#endif
}
