using System;
using UnityEngine;
using UnityEngine.Events;

// ============================================================================
//  CatCooksWeatherReport — погода для ГЕЙМПЛЕЯ.
//
//  ЗАЧЕМ ОТДЕЛЬНЫЙ КОМПОНЕНТ
//   Погода умеет выглядеть, но пока ни на что не влияет — а значит для
//   игрока её нет. Опасной её делает не картинка, а последствия.
//
//   Прямой путь — заставить геймплейные скрипты лезть в MoodDirector —
//   плохой: скрипт костра начнёт зависеть от пресетов, палитр и Volume.
//   Здесь тонкий слой между ними: читает погоду, отдаёт наружу голые
//   числа и события. Скрипт костра знает только «сыро на 0.7» и ничего
//   про рендер.
//
//  ЧТО МОЖНО ПОВЕСИТЬ
//   • Костёр гаснет под дождём, дольше разжигается на мокрой земле
//   • Мокрые доски и металл скользкие
//   • Ветер сносит переносимый груз и раскачивает краны
//   • Молния бьёт по высоким металлическим объектам
//   • Еда портится быстрее в сырость
//
//  Сеть: значения ведёт сервер через MoodDirector, здесь только чтение,
//  поэтому у всех клиентов картина одинаковая без отдельной синхронизации.
// ============================================================================
[DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Атмосфера/Weather Report")]
public class CatCooksWeatherReport : MonoBehaviour
{
    public static CatCooksWeatherReport Instance { get; private set; }

    [Header("Источники")]
    public CatCooksMoodDirector mood;
    public CatCooksRain rain;
    public CatCooksWeatherFX fx;
    public CatCooksLightning lightning;

    // ───────── что читают геймплейные скрипты ─────────

    /// <summary>0 = сухо, 1 = ливень.</summary>
    public float Rain { get; private set; }

    /// <summary>0 = сухой мир, 1 = всё промокло. Меняется медленно
    /// и продолжает расти уже после того, как дождь кончился.</summary>
    public float Wetness { get; private set; }

    /// <summary>0 = штиль, 1 = шторм.</summary>
    public float Wind { get; private set; }

    /// <summary>Направление ветра по горизонтали, единичный вектор.</summary>
    public Vector3 WindDirection { get; private set; } = Vector3.forward;

    /// <summary>0 = молний нет, 1 = гроза.</summary>
    public float Lightning { get; private set; }

    /// <summary>Общая суровость погоды. Считается из остального,
    /// удобно для «можно ли сейчас выходить».</summary>
    public float Severity { get; private set; }

    /// <summary>Под открытым небом ли игрок. Пишет CatCooksWeatherFX.</summary>
    public bool UnderOpenSky { get; private set; } = true;

    [Header("Пороги")]
    [Tooltip("Выше этого считаем, что идёт дождь")]
    [Range(0f, 1f)] public float rainThreshold = 0.15f;

    [Tooltip("Выше этого считаем, что мир мокрый")]
    [Range(0f, 1f)] public float wetThreshold = 0.35f;

    [Tooltip("Выше этого считаем, что погода опасна")]
    [Range(0f, 1f)] public float dangerThreshold = 0.6f;

    [Header("События")]
    [Tooltip("Дождь начался")]
    public UnityEvent onRainStart;
    public UnityEvent onRainStop;

    [Tooltip("Мир промок / высох")]
    public UnityEvent onWorldWet;
    public UnityEvent onWorldDry;

    [Tooltip("Погода стала опасной / успокоилась")]
    public UnityEvent onDangerStart;
    public UnityEvent onDangerEnd;

    /// <summary>Ударила молния. Аргумент — сила, 0…1.</summary>
    public event Action<float> OnLightningStrike;

    private bool _raining, _wet, _dangerous;

    static readonly int WindDirId = Shader.PropertyToID("_CatWindDir");
    static readonly int WindStrId = Shader.PropertyToID("_CatWindStrength");

    void Awake()
    {
        Instance = this;
        if (mood == null) mood = FindFirstObjectByType<CatCooksMoodDirector>();
        if (rain == null) rain = FindFirstObjectByType<CatCooksRain>();
        if (fx == null) fx = FindFirstObjectByType<CatCooksWeatherFX>();
        if (lightning == null) lightning = FindFirstObjectByType<CatCooksLightning>();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        Rain = rain != null ? Mathf.Clamp01(rain.intensity) : 0f;
        Wetness = fx != null ? Mathf.Clamp01(fx.wetness) : 0f;
        Lightning = lightning != null ? Mathf.Clamp01(lightning.activity) : 0f;
        UnderOpenSky = Shader.GetGlobalFloat("_CatOpenSky") > 0.5f;

        // Ветер читаем из тех же глобалей, что и вся визуальная часть —
        // так деревья, облака и физика гарантированно согласованы.
        Vector4 w = Shader.GetGlobalVector(WindDirId);
        Vector3 dir = new Vector3(w.x, 0f, w.z);
        if (dir.sqrMagnitude > 0.0001f) WindDirection = dir.normalized;
        Wind = Mathf.Clamp01(Mathf.InverseLerp(0.12f, 1.9f,
                                               Shader.GetGlobalFloat(WindStrId)));

        // Суровость: молния весит больше всего, потому что она — угроза,
        // а не помеха.
        Severity = Mathf.Clamp01(Rain * 0.4f + Wind * 0.3f + Lightning * 0.5f);

        Edge(ref _raining, Rain > rainThreshold, onRainStart, onRainStop);
        Edge(ref _wet, Wetness > wetThreshold, onWorldWet, onWorldDry);
        Edge(ref _dangerous, Severity > dangerThreshold, onDangerStart, onDangerEnd);
    }

    static void Edge(ref bool state, bool now, UnityEvent on, UnityEvent off)
    {
        if (now == state) return;
        state = now;
        if (now) on?.Invoke(); else off?.Invoke();
    }

    /// <summary>Зовёт CatCooksLightning в момент разряда.</summary>
    public void ReportStrike(float power) => OnLightningStrike?.Invoke(power);

    // ================================================== готовые вопросы
    /// <summary>Скользко ли сейчас. Вешай на доски, металл, крыши.</summary>
    public bool IsSlippery => Wetness > wetThreshold;

    /// <summary>Множитель трения: 1 сухо, 0.45 мокро насквозь.</summary>
    public float FrictionMultiplier => Mathf.Lerp(1f, 0.45f, Wetness);

    /// <summary>Сила ветра в ньютонах на единицу парусности.</summary>
    public Vector3 WindForce(float sail = 1f) => WindDirection * (Wind * 12f * sail);

    /// <summary>Может ли гореть открытый огонь под небом.</summary>
    public bool CanKeepFire => !(UnderOpenSky && Rain > 0.35f);

    /// <summary>Во сколько раз медленнее разгорается на сырой земле.</summary>
    public float IgnitionPenalty => Mathf.Lerp(1f, 3.5f, Wetness);
}
