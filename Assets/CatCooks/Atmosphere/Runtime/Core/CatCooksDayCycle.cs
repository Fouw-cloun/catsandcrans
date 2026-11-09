using UnityEngine;

// ============================================================================
//  CatCooksDayCycle — время суток для всей атмосферы.
//
//  Берёт часы из GameClock (он уже синхронизирован по сети — у всех игроков
//  одно и то же время) и превращает их в:
//    • высоту и азимут солнца, ночью — луны;
//    • «день» 0..1, «золотой час» 0..1, «ночь» 0..1;
//    • веса палитр рассвета, заката и ночи.
//
//  Сам ничего не рисует и никуда не пишет. Читает его Mood Director:
//  он смешивает палитру стиля с палитрой времени суток и ставит солнце.
//  Поэтому любая погода и любой стиль работают в любое время суток.
//
//  Без GameClock (сцена без сети, редактор) — берёт ручной час.
// ============================================================================
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Атмосфера/Day Cycle")]
public class CatCooksDayCycle : MonoBehaviour
{
    public enum TimeSource { GameClock = 0, Manual = 1 }

    [Header("━━━ Время ━━━")]
    [Tooltip("GameClock — время игры (сеть). Manual — ручной час ниже.")]
    public TimeSource source = TimeSource.GameClock;

    [Tooltip("Час, если GameClock нет или выбран Manual. В редакторе — предпросмотр.")]
    [Range(0f, 24f)] public float manualHour = 10f;

    [Tooltip("Для теста без сети: сколько игровых часов проходит за секунду в режиме Manual. 0 = стоит.")]
    public float manualHoursPerSecond = 0f;

    [Header("━━━ Солнце ━━━")]
    [Range(0f, 12f)] public float sunrise = 6f;
    [Range(12f, 24f)] public float sunset = 20.5f;

    [Tooltip("Высота солнца в полдень. 55–62 — тени короткие, но ещё с формой.")]
    [Range(15f, 85f)] public float noonElevation = 58f;

    [Tooltip("Азимут восхода и заката. Разница — сколько солнце проходит по небу.")]
    public float riseAzimuth = 80f;
    public float setAzimuth = 280f;

    [Tooltip("Как глубоко солнце уходит под горизонт в полночь (для плавных сумерек)")]
    [Range(5f, 40f)] public float midnightDepth = 22f;

    [Header("━━━ Луна (ночной свет) ━━━")]
    public bool moonLight = true;
    [Range(10f, 80f)] public float moonElevation = 38f;
    [Range(0f, 1f)] public float moonIntensity = 0.22f;
    public Color moonColor = new Color(0.62f, 0.72f, 1f);

    [Header("━━━ Палитры времени суток ━━━")]
    [Tooltip("Рассвет. Смешивается со стилем в «золотой час» утром. Пусто — без подмеса.")]
    public CatCooksPalette dawn;
    [Tooltip("Закат. Смешивается со стилем в «золотой час» вечером.")]
    public CatCooksPalette dusk;
    [Tooltip("Ночь. Полностью берёт верх глубокой ночью.")]
    public CatCooksPalette night;

    [Header("━━━ Разные закаты и ночи ━━━")]
    [Tooltip("Варианты рассвета. Каждый игровой день берётся один (по номеру дня). Пусто — Dawn.")]
    public CatCooksPalette[] dawnVariants = new CatCooksPalette[0];
    [Tooltip("Варианты заката: оранжевый, розовый… Каждый день свой. Пусто — Dusk.")]
    public CatCooksPalette[] duskVariants = new CatCooksPalette[0];
    [Tooltip("Варианты ночи: синяя, фиолетовая… Пусто — Night.")]
    public CatCooksPalette[] nightVariants = new CatCooksPalette[0];
    [Tooltip("Яркость луны для каждого варианта ночи (по порядку Night Variants). 0 или пусто — общая Moon Intensity. " +
             "Например, «ночь с яркой луной» — 0.6, неоновая — 0.15")]
    public float[] nightVariantMoon = new float[0];
    [Tooltip("Предпросмотр: принудительно этот вариант (индекс в списке), −1 — по номеру дня. Ставит меню P.")]
    public int forceDawn = -1, forceDusk = -1, forceNight = -1;
    [Tooltip("Зерно выбора вариантов. Одинаковое у всех игроков (день общий из GameClock).")]
    public int variantSeed = 7;

    [Tooltip("Насколько сильно палитра рассвета/заката перекрывает стиль")]
    [Range(0f, 1f)] public float goldenStrength = 0.75f;
    [Tooltip("Ширина золотого часа в градусах высоты солнца")]
    [Range(2f, 25f)] public float goldenWidth = 11f;
    [Range(0f, 1f)] public float nightStrength = 1f;

    [Header("━━━ Ночь: камера ━━━")]
    [Tooltip("Смещение экспозиции глубокой ночью. Не делай ночь чёрной — она синяя.")]
    [Range(-2f, 0f)] public float nightExposure = -0.55f;

    // ─────────── выходы (читает Mood Director) ───────────
    public float Hour { get; private set; }
    public int Day { get; private set; } = 1;
    /// <summary>Абсолютные игровые часы с начала сессии (для расписания погоды).</summary>
    public double AbsoluteHours { get; private set; }
    public float SunElevation { get; private set; }
    public float SunAzimuth { get; private set; }
    /// <summary>0 ночь … 1 день (с мягкими сумерками).</summary>
    public float Day01 { get; private set; }
    /// <summary>Золотой час: солнце у горизонта.</summary>
    public float Golden01 { get; private set; }
    public float Night01 { get; private set; }
    /// <summary>true — утро (рассвет), false — вечер (закат).</summary>
    public bool Morning { get; private set; }
    /// <summary>Солнце под горизонтом, светит луна.</summary>
    public bool MoonUp => moonLight && SunElevation < 0f;

    public bool HasClock => source == TimeSource.GameClock && GameClock.Instance != null;

    double _manualAbs;

    void OnEnable() { _manualAbs = manualHour; Evaluate(); }
    void OnValidate() { Evaluate(); }

    void Update()
    {
        if (!HasClock && Application.isPlaying && manualHoursPerSecond != 0f)
        {
            _manualAbs += manualHoursPerSecond * Time.deltaTime;
            manualHour = Mathf.Repeat((float)_manualAbs, 24f);
        }
        Evaluate();
    }

    /// <summary>Пересчитать всё из текущего часа.</summary>
    public void Evaluate()
    {
        if (HasClock)
        {
            var c = GameClock.Instance;
            Hour = c.Hours;
            Day = c.Day;
            AbsoluteHours = (c.Day - 1) * 24.0 + c.Hours;
        }
        else
        {
            Hour = Mathf.Repeat(manualHour, 24f);
            Day = 1 + (int)System.Math.Floor(_manualAbs / 24.0);
            AbsoluteHours = System.Math.Max(0.0, _manualAbs);
            if (!Application.isPlaying) AbsoluteHours = Hour;
        }

        float h = Hour;
        float dayLen = Mathf.Max(1f, sunset - sunrise);
        if (h >= sunrise && h <= sunset)
        {
            float d = (h - sunrise) / dayLen;
            SunElevation = Mathf.Sin(d * Mathf.PI) * noonElevation;
            SunAzimuth = Mathf.Lerp(riseAzimuth, setAzimuth, d);
            Morning = d < 0.5f;
        }
        else
        {
            // ночь: солнце плавно уходит под горизонт и возвращается
            float nightLen = 24f - dayLen;
            float into = h > sunset ? h - sunset : h + 24f - sunset;
            float d = Mathf.Clamp01(into / Mathf.Max(0.5f, nightLen));
            SunElevation = -Mathf.Sin(d * Mathf.PI) * midnightDepth;
            SunAzimuth = Mathf.Lerp(setAzimuth, riseAzimuth + 360f, d) % 360f;
            Morning = d > 0.5f;
        }

        Day01 = Smooth(-5f, 7f, SunElevation);
        Night01 = 1f - Smooth(-10f, 1f, SunElevation);
        float g = (SunElevation - 3f) / Mathf.Max(1f, goldenWidth);
        Golden01 = Mathf.Exp(-g * g * 2f) * (1f - Night01 * 0.6f);
    }

    /// <summary>Палитра рассвета или заката по текущему часу (может быть null).</summary>
    public CatCooksPalette GoldenPalette
    {
        get
        {
            var dw = Forced(dawnVariants, forceDawn) ?? Pick(dawnVariants, 0) ?? dawn;
            var dk = Forced(duskVariants, forceDusk) ?? Pick(duskVariants, 1) ?? dusk;
            return Morning ? (dw != null ? dw : dk) : (dk != null ? dk : dw);
        }
    }

    /// <summary>Ночная палитра этой ночи (вариант по номеру дня; ночь после заката считается тем же днём).</summary>
    public CatCooksPalette NightPalette => Forced(nightVariants, forceNight) ?? Pick(nightVariants, 2, Hour < 12f ? -1 : 0) ?? night;

    static CatCooksPalette Forced(CatCooksPalette[] arr, int i)
    {
        return (arr != null && i >= 0 && i < arr.Length) ? arr[i] : null;
    }

    /// <summary>Предпросмотр: поставить ручной час (меню P). Время игры (GameClock) при этом не трогается.</summary>
    public void SetManualHour(float h)
    {
        source = TimeSource.Manual;
        manualHour = Mathf.Repeat(h, 24f);
        _manualAbs = System.Math.Floor(_manualAbs / 24.0) * 24.0 + manualHour;
        Evaluate();
    }

    CatCooksPalette Pick(CatCooksPalette[] arr, int salt, int dayShift = 0)
    {
        if (arr == null || arr.Length == 0) return null;
        int n = 0;
        foreach (var p in arr) if (p != null) n++;
        if (n == 0) return null;
        unchecked
        {
            int h = (Day + dayShift) * 73856093 ^ variantSeed * 19349663 ^ salt * 83492791;
            int k = (int)((uint)h % (uint)n);
            foreach (var p in arr)
                if (p != null) { if (k == 0) return p; k--; }
        }
        return null;
    }

    /// <summary>Яркость луны этой ночью: своя у варианта ночи (nightVariantMoon) или общая.</summary>
    public float MoonIntensityNow
    {
        get
        {
            var np = NightPalette;
            if (np != null && nightVariants != null && nightVariantMoon != null)
                for (int i = 0; i < nightVariants.Length && i < nightVariantMoon.Length; i++)
                    if (nightVariants[i] == np && nightVariantMoon[i] > 0f) return nightVariantMoon[i];
            return moonIntensity;
        }
    }

    public float GoldenWeight => Golden01 * goldenStrength;
    public float NightWeight => Night01 * nightStrength;

    /// <summary>Направление света: днём солнце, ночью луна (напротив солнца).</summary>
    public void LightAngles(out float elevation, out float azimuth, out float intensityMul, out bool moon)
    {
        moon = MoonUp && Day01 < 0.5f;
        if (moon)
        {
            elevation = moonElevation;
            azimuth = (SunAzimuth + 180f) % 360f;
            intensityMul = MoonIntensityNow * Night01;
        }
        else
        {
            // у самого горизонта не даём свету лечь в ноль — иначе тени рвутся
            elevation = Mathf.Max(SunElevation, 2f);
            azimuth = SunAzimuth;
            intensityMul = Day01;
        }
    }

    static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    public string TimeString => string.Format("{0:00}:{1:00}", Mathf.FloorToInt(Hour), Mathf.FloorToInt((Hour % 1f) * 60f));
}
