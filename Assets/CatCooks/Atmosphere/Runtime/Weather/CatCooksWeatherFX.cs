using UnityEngine;

// ============================================================================
//  CatCooksWeatherFX — эффекты на камере и мокрость мира.
//
//  ДВЕ РАЗНЫЕ ЗАДАЧИ В ОДНОМ КОМПОНЕНТЕ, потому что у них общий источник —
//  сила непогоды:
//
//   1. ЭКРАННЫЕ ЭФФЕКТЫ. Рамка, капли на объективе, вспышка. Рисуются
//      в общем проходе CatCooks/Weather Composite вместе с туманом
//      и тенями — отдельного прохода не заводим.
//
//   2. МОКРОСТЬ МИРА. Глобальный _CatWetness: копится под дождём,
//      высыхает на солнце. Раздаётся в материалы, которые её понимают.
//
//  ПОЧЕМУ МОКРОСТЬ ВАЖНЕЕ САМОГО ДОЖДЯ
//   Дождь без неё — полосы поверх картинки: мир под ним не меняется,
//   и ливень читается как эффект, а не как событие. Мокрость превращает
//   его в последствие: земля темнеет, появляется блеск, потом всё
//   медленно сохнет. Именно это делает погоду ощутимой.
//
//  ВЫСЫХАНИЕ СЧИТАЕТСЯ ЗА СЕКУНДУ, А НЕ ЗА КАДР. Кажется мелочью, но
//  ошибка здесь встречается сплошь и рядом: если Time.deltaTime забыть
//  или прицепить не ко всему выражению, мир на 30 кадрах будет сохнуть
//  вдвое медленнее, чем на 60.
//
//  Сеть: косметика, НЕ NetworkObject.
// ============================================================================
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Атмосфера/Weather FX")]
public class CatCooksWeatherFX : MonoBehaviour
{
    [Header("━━━ Рамка ━━━")]
    [Tooltip("Сила затемнения по краям кадра. В грозу 0.5–0.7 — картинка " +
             "сжимается и давит; в ясный день 0.1–0.2.")]
    [Range(0f, 1f)] public float vignette = 0.2f;

    [Tooltip("Насколько резко рамка нарастает к краю. Больше — уже полоса.")]
    [Range(0.5f, 6f)] public float vignettePower = 1.6f;

    [Tooltip("ШИРИНА полосы от края экрана, в долях высоты кадра. " +
         "0.12–0.2 — рамка по периметру, центр чистый. " +
         "Больше 0.3 затемнение полезет в середину.")]
    [Range(0.02f, 0.45f)] public float vignetteSoftness = 0.16f;

    [Tooltip("Цвет рамки. Не чёрный, а холодный тёмный — чёрная рамка " +
             "выглядит как ошибка рендера, цветная как атмосфера.")]
    public Color vignetteColor = new Color(0.22f, 0.26f, 0.36f);

    [Tooltip("Обесцвечивание по краям. В непогоду периферия зрения " +
             "теряет цвет первой.")]
    [Range(0f, 1f)] public float edgeDesaturate = 0.4f;

    [Header("━━━ Под крышей ━━━")]
    [Tooltip("Проверять, стоит ли игрок под навесом. Влияет на намокание: " +
             "под крышей мир не мокнет.")]
    public bool checkRoof = true;

    [Tooltip("Кого проверяем. Ставит CatCooksRainAnchor с игрока.")]
    public Transform player;
    public LayerMask roofMask = ~0;

    [Header("━━━ Вспышка ━━━")]
    [Tooltip("Пишется молнией, руками не трогать.")]
    [Range(0f, 2f)] public float flash = 0f;
    public Color flashColor = new Color(0.85f, 0.9f, 1f);

    [Header("━━━ Мокрость мира ━━━")]
    [Tooltip("0 = сухо, 1 = всё мокрое. Копится под дождём сама.")]
    [Range(0f, 1f)] public float wetness = 0f;

    [Tooltip("За сколько секунд ливня мир промокает полностью")]
    public float soakSeconds = 25f;

    [Tooltip("За сколько секунд высыхает на полном солнце")]
    public float drySeconds = 90f;

    [Tooltip("В пасмурную погоду сохнет медленнее. 0 = скорость не зависит " +
             "от солнца, 1 = без солнца не сохнет вовсе.")]
    [Range(0f, 1f)] public float sunDryInfluence = 0.7f;

    [Tooltip("Материалы, которые понимают _Wetness. Пусто — раздаём " +
             "только глобальный параметр, и шейдеры берут его сами.")]
    public Material[] weatherableMaterials;

    [Header("━━━ Источники ━━━")]
    public CatCooksRain rain;
    public Light sun;

    // ───────── глобальные ─────────
    static readonly int VigId = Shader.PropertyToID("_CatFX_Vignette");
    static readonly int VigColId = Shader.PropertyToID("_CatFX_VignetteColor");
    static readonly int FlashId = Shader.PropertyToID("_CatFX_Flash");
    static readonly int FlashColId = Shader.PropertyToID("_CatFX_FlashColor");
    static readonly int WetId = Shader.PropertyToID("_CatWetness");
    static readonly int MatWetId = Shader.PropertyToID("_Wetness");
    static readonly int MatRainId = Shader.PropertyToID("_RainAmount");

    private float _indoorBlend = 1f;
    private float _roofTimer;
    private double _lastEditorTime;

    void OnEnable()
    {
        if (rain == null) rain = FindFirstObjectByType<CatCooksRain>();
        if (sun == null)
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude,
                                                       FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }

        Push();
#if UNITY_EDITOR
        _lastEditorTime = UnityEditor.EditorApplication.timeSinceStartup;
        UnityEditor.EditorApplication.update -= EditorTick;
        UnityEditor.EditorApplication.update += EditorTick;
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update -= EditorTick;
#endif
        // Гасим за собой: выключенный компонент не должен оставлять
        // кадр в рамке и каплях — это читалось бы как баг.
        Shader.SetGlobalVector(VigId, Vector4.zero);
        Shader.SetGlobalVector(FlashId, Vector4.zero);
    }

    void OnValidate() => Push();

    void LateUpdate() { if (Application.isPlaying) Tick(Time.deltaTime); }

#if UNITY_EDITOR
    void EditorTick()
    {
        if (Application.isPlaying || this == null || !isActiveAndEnabled) return;
        double now = UnityEditor.EditorApplication.timeSinceStartup;
        float dt = (float)(now - _lastEditorTime);
        _lastEditorTime = now;
        if (dt <= 0f || dt > 0.5f) return;
        Tick(dt);
    }
#endif

    void Tick(float dt)
    {
        float rainAmount = rain != null ? Mathf.Clamp01(rain.intensity) : 0f;

        // ---- под крышей или нет ----
        if (checkRoof && player != null)
        {
            // Луч раз в четверть секунды, а не каждый кадр: под навесом
            // игрок не окажется за один кадр, а рейкаст стоит времени.
            _roofTimer -= dt;
            if (_roofTimer <= 0f)
            {
                _roofTimer = 0.25f;
                bool covered = Physics.Raycast(player.position + Vector3.up * 0.5f,
                                               Vector3.up, 60f, roofMask,
                                               QueryTriggerInteraction.Ignore);
                _indoorBlend = covered ? 0f : 1f;
            }
        }
        else _indoorBlend = 1f;

        float openSky = Mathf.MoveTowards(
            Shader.GetGlobalFloat("_CatOpenSky"), _indoorBlend, dt * 2f);
        Shader.SetGlobalFloat("_CatOpenSky", openSky);

        // ---- мокрость ----
        // Оба слагаемых умножены на dt ЦЕЛИКОМ: иначе скорость поехала бы
        // за частотой кадров, и мир сох бы по-разному на разном железе.
        if (rainAmount > 0.01f)
        {
            wetness += rainAmount * openSky * (dt / Mathf.Max(soakSeconds, 0.1f));
        }
        else
        {
            // Солнце ускоряет высыхание: в пасмурную погоду лужи стоят
            // дольше, и это заметно без единой подсказки игроку.
            float sunPower = sun != null ? Mathf.Clamp01(sun.intensity / 1.5f) : 0.5f;
            float rate = Mathf.Lerp(1f, sunPower, sunDryInfluence);
            wetness -= rate * (dt / Mathf.Max(drySeconds, 0.1f));
        }
        wetness = Mathf.Clamp01(wetness);

        // ---- вспышка гаснет сама ----
        flash = Mathf.MoveTowards(flash, 0f, dt * 4f);

        Push();
    }

    void Push()
    {
        Shader.SetGlobalVector(VigId, new Vector4(
            vignette, vignettePower, vignetteSoftness, edgeDesaturate));
        Shader.SetGlobalColor(VigColId, vignetteColor);

        Shader.SetGlobalVector(FlashId, new Vector4(flash, 0, 0, 0));
        Shader.SetGlobalColor(FlashColId, flashColor);

        Shader.SetGlobalFloat(WetId, wetness);

        // Материалы, у которых свойство объявлено явно. Глобального
        // параметра хватает большинству, но Shader Graph видит только
        // то, что объявлено в самом материале.
        if (weatherableMaterials != null)
        {
            float rainAmount = rain != null ? rain.intensity : 0f;
            for (int i = 0; i < weatherableMaterials.Length; i++)
            {
                var m = weatherableMaterials[i];
                if (m == null) continue;
                if (m.HasProperty(MatWetId)) m.SetFloat(MatWetId, wetness);
                if (m.HasProperty(MatRainId)) m.SetFloat(MatRainId, rainAmount);
            }
        }
    }

    // ================================================================ API
    /// <summary>Дёргать из погодных пресетов.</summary>
    public void SetMood(float vignetteAmount, Color frameColor, float desat)
    {
        vignette = Mathf.Clamp01(vignetteAmount);
        vignetteColor = frameColor;
        edgeDesaturate = Mathf.Clamp01(desat);
    }

    /// <summary>Вызывает молния: мгновенная засветка кадра.</summary>
    public void Flash(float amount) => flash = Mathf.Max(flash, amount);

    /// <summary>Промочить или высушить мир сразу — для катсцен и загрузки.</summary>
    public void SetWetness(float t) { wetness = Mathf.Clamp01(t); Push(); }
}
