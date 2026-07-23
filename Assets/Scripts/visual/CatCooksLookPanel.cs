using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using WP = CatCooksWorldDirector.WorldPreset;
using WX = CatCooksWorldDirector.WeatherPreset;

// ============================================================================
//  CatCooksLookPanel — ЕДИНЫЙ ПУЛЬТ ВИЗУАЛА.
//  Всё в одном компоненте: 18 стилей, 12 погод, 10 готовых комбо и понятные
//  ползунки-коррекции («теплее», «больше тумана», «темнее тени»...).
//
//  Коррекции живут ПОВЕРХ пресетов: сменил пресет — коррекции сохранились.
//  Всё работает вживую и в редакторе, и в игре. Панель ничего не ломает:
//  она пишет cor-поля директора, а применяет их сам директор — хозяин один.
//
//  Установка: пустой GameObject → CatCooksLookPanel (директора найдёт сам).
//  Кнопки применения пресетов — вверху инспектора (нужен
//  CatCooksLookPanelEditor.cs в папке Editor).
//
//  ⚠ Чтобы ползунок «Тени от облаков +/-» работал вживую, нужен мини-патч
//  SkyController (перестройка cookie при смене силы теней):
//    1) поле:   private float _lastShadowStr = -1f;
//    2) условие в Apply() вместо  if (_cookie == null || _lastShadowCov != shadowCoverage):
//       if (_cookie == null || Mathf.Abs(_lastShadowCov - shadowCoverage) > 0.01f
//           || Mathf.Abs(_lastShadowStr - shadowStrength) > 0.05f)
//       { BuildCookie(); _lastShadowCov = shadowCoverage; _lastShadowStr = shadowStrength; }
// ============================================================================
[ExecuteAlways]
[DisallowMultipleComponent]
public class CatCooksLookPanel : MonoBehaviour
{
    [Header("Ссылки (найдутся сами, если пусто)")]
    public CatCooksWorldDirector director;
    public Volume postVolume;

    public enum LookCombo
    {
        SolarpunkDay,        // Solarpunk + Лучистый день — дефолт игры
        CinematicGold,       // Золотой час + Утренняя дымка — самый киношный
        StormDrama,          // Свет сквозь грозу + Разрыв грозы — кульминация
        MilkWorld,           // Туманное утро + Плотный туман — лампы-маяки
        GhibliWalk,          // Луга Гибли + Лёгкие облака — добрая сказка
        MagicFog,            // Мистическая бирюза + Плотный туман — магия, огонь сияет
        NeonRainParty,       // Неоновые сумерки + Морось — мокрый неон
        TropicNoon,          // Тропический пунш + Ясно — курорт
        CrystalAutumn,       // Осень + После дождя — умытая осень
        SepiaCalm            // Сепия + Светлая пасмурность — воспоминание
    }

    [Header("ПРЕСЕТЫ — выбери и жми кнопки вверху инспектора")]
    public WP style = WP.SolarpunkNoon;
    public WX weatherPreset = WX.SunbeamDay;
    public LookCombo combo = LookCombo.SolarpunkDay;

    [Header("КОРРЕКЦИИ · Свет")]
    [Tooltip("Теплее (+) / холоднее (−) весь кадр")]
    [Range(-1f, 1f)] public float warmth = 0f;
    [Tooltip("Светлее (+) / темнее (−)")]
    [Range(-1f, 1f)] public float brightness = 0f;
    [Tooltip("Сочнее (+) / спокойнее (−)")]
    [Range(-30f, 30f)] public float saturation = 0f;
    [Tooltip("Сила солнца")]
    [Range(0.5f, 1.6f)] public float sunPower = 1f;

    [Header("КОРРЕКЦИИ · Тени")]
    [Tooltip("Светлее / темнее теневые стороны предметов (ambient)")]
    [Range(0.6f, 1.4f)] public float shadowBrightness = 1f;
    [Tooltip("Тёплые (+) или холодные (−) тени — самая «настроенческая» ручка")]
    [Range(-1f, 1f)] public float shadowWarmth = 0f;
    [Tooltip("Сильнее (+) / слабее (−) тени от облаков (нужен патч SkyController — см. шапку файла)")]
    [Range(-0.3f, 0.3f)] public float cloudShadowsAdd = 0f;

    [Header("КОРРЕКЦИИ · Атмосфера")]
    [Tooltip("Больше (+) / меньше (−) тумана")]
    [Range(-1f, 1f)] public float fogAmount = 0f;
    [Tooltip("Больше / меньше облаков в небе")]
    [Range(-0.3f, 0.3f)] public float cloudAmount = 0f;
    [Tooltip("Сила солнечных лучей")]
    [Range(0f, 2f)] public float rayPower = 1f;

    [Header("АБСОЛЮТНЫЕ ОВЕРРАЙДЫ (галка = панель берёт контроль над эффектом)")]
    public bool overrideBloom = false;
    [Range(0f, 1.2f)] public float bloomIntensity = 0.35f;
    public bool overrideVignette = false;
    [Range(0f, 0.6f)] public float vignetteIntensity = 0.2f;
    public bool overrideContrast = false;
    [Range(-40f, 60f)] public float contrast = 15f;
    public bool overrideColorFilter = false;
    public Color colorFilterColor = Color.white;

    private Bloom _bloom;
    private Vignette _vig;
    private ColorAdjustments _ca;

    // ---------------------------------------------------------------- lifecycle
    void OnEnable() { AutoFind(); CacheVolume(); Push(); }
    void Update()   { if (Application.isPlaying) Push(); }

#if UNITY_EDITOR
    void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        { if (this != null) { AutoFind(); CacheVolume(); Push(); } };
    }
#endif

    void AutoFind()
    {
        if (director == null) director = FindFirstObjectByType<CatCooksWorldDirector>();
        if (postVolume == null && director != null) postVolume = director.postVolume;
    }

    void CacheVolume()
    {
        if (postVolume == null || postVolume.profile == null) return;
        postVolume.profile.TryGet(out _bloom);
        postVolume.profile.TryGet(out _vig);
        postVolume.profile.TryGet(out _ca);
    }

    // ---------------------------------------------------------------- главное
    /// <summary>Передать коррекции директору и применить оверрайды.</summary>
    public void Push()
    {
        if (director != null)
        {
            director.corWarmth     = warmth * 18f;
            director.corExposure   = brightness * 0.8f;
            director.corSaturation = saturation;
            director.corSunBoost   = sunPower;
            director.corAmbBright  = shadowBrightness;
            director.corAmbWarmth  = shadowWarmth;
            director.corShadow     = cloudShadowsAdd;
            director.corCoverage   = cloudAmount;
            director.corRayMult    = rayPower;
            // туман: + = ближе (больше), − = дальше (меньше); 0 = нейтрально
            director.corFogMult = fogAmount >= 0f
                ? Mathf.Lerp(1f, 0.45f, fogAmount)
                : Mathf.Lerp(1f, 1.9f, -fogAmount);
#if UNITY_EDITOR
            if (!Application.isPlaying) director.Apply();
#endif
        }

        if (_bloom == null || _vig == null || _ca == null) CacheVolume();
        if (overrideBloom && _bloom != null)      _bloom.intensity.Override(bloomIntensity);
        if (overrideVignette && _vig != null)     _vig.intensity.Override(vignetteIntensity);
        if (overrideContrast && _ca != null)      _ca.contrast.Override(contrast);
        if (overrideColorFilter && _ca != null)   _ca.colorFilter.Override(colorFilterColor);
    }

    public void ResetCorrections()
    {
        warmth = brightness = saturation = 0f;
        shadowWarmth = fogAmount = cloudAmount = cloudShadowsAdd = 0f;
        sunPower = shadowBrightness = rayPower = 1f;
        Push();
    }

    // ---------------------------------------------------------------- пресеты
    public void ApplyStyle()        { if (director != null) { director.ApplyPreset(style); Push(); } }
    public void ApplyWeatherPick()  { if (director != null) { director.ApplyWeather(weatherPreset); Push(); } }
    public void ApplyBoth()         { ApplyStyle(); ApplyWeatherPick(); }

    public void ApplyCombo()
    {
        var (s, w) = ComboMap(combo);
        style = s; weatherPreset = w;
        ApplyBoth();
    }

    public static (WP, WX) ComboMap(LookCombo c)
    {
        switch (c)
        {
            case LookCombo.CinematicGold:  return (WP.GoldenHour,      WX.MorningHaze);
            case LookCombo.StormDrama:     return (WP.StormLight,      WX.StormBreak);
            case LookCombo.MilkWorld:      return (WP.MistyMorning,    WX.DenseFog);
            case LookCombo.GhibliWalk:     return (WP.GhibliMeadow,    WX.FairClouds);
            case LookCombo.MagicFog:       return (WP.OriMystic,       WX.DenseFog);
            case LookCombo.NeonRainParty:  return (WP.NeonDusk,        WX.Drizzle);
            case LookCombo.TropicNoon:     return (WP.TropicalPunch,   WX.ClearSky);
            case LookCombo.CrystalAutumn:  return (WP.AutumnAmber,     WX.AfterRain);
            case LookCombo.SepiaCalm:      return (WP.SepiaMemory,     WX.BrightOvercast);
            default:                       return (WP.SolarpunkNoon,   WX.SunbeamDay);
        }
    }
}
