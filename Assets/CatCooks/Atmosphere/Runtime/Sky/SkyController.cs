// =============================================================
//  SkyController.cs  v2
//  Единая панель: солнце, небо, облака, ПОГОДА (ясно/пасмурно/гроза),
//  мягкий туман и движущиеся тени от облаков.
//  Погода темнит одновременно скайбоксные И сферные облака (через
//  глобальный _GlobalCloudDarkness).
//  URP, Unity 2022.3+ / Unity 6.
// =============================================================
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[ExecuteAlways]
public class SkyController : MonoBehaviour
{
    [Header("References")]
    public Light sun;
    public Material skyMaterial;

    [Header("Sun / Time of Day")]
    [Range(-10, 90)] public float sunElevation = 40f;
    [Range(0, 360)]  public float sunAzimuth   = 40f;
    public Color sunColor = new Color(1f, 0.96f, 0.88f);
    [Range(0, 5)] public float sunIntensity = 1.3f;

    [Header("Sky Gradient")]
    public Color zenithColor  = new Color(0.35f, 0.60f, 0.92f);
    public Color horizonColor = new Color(0.82f, 0.90f, 0.97f);
    public Color groundColor  = new Color(0.60f, 0.63f, 0.68f);
    [Range(0.1f, 4f)] public float skyExponent = 1.4f;

    [Header("Clouds (skybox layer)")]
    public Color cloudLitColor    = Color.white;
    public Color cloudShadowColor = new Color(0.72f, 0.78f, 0.90f);
    [Range(0, 1)]   public float coverage   = 0.45f;   // ПЛОТНОСТЬ облаков
    [Range(0.01f, 0.5f)] public float cloudSoftness = 0.14f;
    [Range(0.2f, 8f)] public float cloudScale = 2.0f;
    public Vector2 cloudWind = new Vector2(0.008f, 0.005f);
    [Tooltip("Скорость, с которой облака МЕНЯЮТ ФОРМУ (а не только едут). 0 = застывшая картинка. "
           + "Mood Director выставляет это сам из погоды: в штиль медленно, в грозу быстро.")]
    [Range(0, 0.3f)] public float cloudEvolve = 0.05f;
    [Tooltip("Облака собираются к горизонту, над головой чистое небо. 0 — равномерно по всему небу.")]
    [Range(0, 1f)] public float cloudHorizonBias = 0.6f;
    [Tooltip("Перистость: облака вытянуты вдоль ветра. 1 — круглые, 2–3 — мягкие пряди.")]
    [Range(1, 5f)] public float cloudStretch = 1f;

    [Header("WEATHER  (ясно -> пасмурно -> гроза)")]
    [Range(0, 1)] public float cloudDarkness = 0f;   // потемнение ВСЕХ облаков (гроза)
    [Range(0, 1)] public float skyOvercast   = 0f;   // пасмурность неба + гашение солнца

    [Header("Fog (мягкий, по умолчанию выкл)")]
    public bool  enableFog = false;
    public Color fogColor = new Color(0.82f, 0.90f, 0.97f);
    public float fogStart = 60f;    // с какой дистанции начинается
    public float fogEnd   = 500f;   // где полностью

    [Header("Cloud Shadows (moving cookie on sun)")]
    public bool  enableCloudShadows = true;
    [Range(50, 2000)] public float shadowTiling = 500f;
    public Vector2 shadowSpeed = new Vector2(6f, 3f);
    [Range(0, 1)] public float shadowStrength = 0.5f;
    [Range(0, 1)] public float shadowCoverage = 0.45f;

    private Texture2D _cookie;
    private Vector2   _cookieOffset;
    private float     _lastShadowCov = -1f;
    private int _lastSkyHash = 0;

    void OnEnable()  { Apply(); }
    void OnValidate(){ Apply(); }

    void Update()
    {
        Apply();
        if (enableCloudShadows && sun != null && _cookie != null)
        {
            _cookieOffset += shadowSpeed * (Application.isPlaying ? Time.deltaTime : 0f);
            var data = sun.GetUniversalAdditionalLightData();
            data.lightCookieSize   = new Vector2(shadowTiling, shadowTiling);
            data.lightCookieOffset = _cookieOffset;
        }
    }

    void Apply()
    {
        // погода темнит и сферные облака (PuffyCloud) — глобально
        Shader.SetGlobalFloat("_GlobalCloudDarkness", cloudDarkness);

        // --- Солнце (гаснет при пасмурности) ---
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(sunElevation, sunAzimuth, 0f);
            sun.color = sunColor;
            sun.intensity = sunIntensity * Mathf.Lerp(1f, 0.4f, skyOvercast);
        }

        // --- Скайбокс ---
        if (skyMaterial != null)
        {
            skyMaterial.SetColor("_ZenithColor",  zenithColor);
            skyMaterial.SetColor("_HorizonColor", horizonColor);
            skyMaterial.SetColor("_GroundColor",  groundColor);
            skyMaterial.SetFloat("_SkyExponent",  skyExponent);

            if (sun != null)
                skyMaterial.SetVector("_SunDir", -sun.transform.forward);
            skyMaterial.SetColor("_SunColor", sunColor);
            skyMaterial.SetFloat("_SunIntensity", Mathf.Max(sunIntensity, 0.5f));

            skyMaterial.SetColor("_CloudColor",       cloudLitColor);
            skyMaterial.SetColor("_CloudShadowColor", cloudShadowColor);
            skyMaterial.SetFloat("_Coverage",  coverage);
            skyMaterial.SetFloat("_CloudSoft", cloudSoftness);
            skyMaterial.SetFloat("_CloudScale", cloudScale);
            skyMaterial.SetFloat("_CloudDarkness", cloudDarkness);
            skyMaterial.SetFloat("_Overcast", skyOvercast);
            skyMaterial.SetVector("_Wind", cloudWind);
            skyMaterial.SetFloat("_CloudEvolve", cloudEvolve);
            skyMaterial.SetFloat("_CloudHorizonBias", cloudHorizonBias);
            skyMaterial.SetFloat("_CloudStretch", cloudStretch);

            int skyHash = zenithColor.GetHashCode() ^ horizonColor.GetHashCode()
                        ^ groundColor.GetHashCode() ^ coverage.GetHashCode()
                        ^ skyOvercast.GetHashCode() ^ sunColor.GetHashCode()
                        ^ sunIntensity.GetHashCode();
            if (skyHash != _lastSkyHash)
            {
                _lastSkyHash = skyHash;
                DynamicGI.UpdateEnvironment();
            }
        }

        // --- Туман (Linear, мягкий — не убивает цвет) ---
        RenderSettings.fog = enableFog;
        if (enableFog)
        {
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
        }

        // --- Cookie теней ---
        if (enableCloudShadows && sun != null)
        {
            if (_cookie == null || _lastShadowCov != shadowCoverage)
            {
                BuildCookie();
                _lastShadowCov = shadowCoverage;
            }
            sun.cookie = _cookie;
        }
        else if (sun != null)
        {
            sun.cookie = null;
        }
    }

    void BuildCookie()
    {
        const int res = 512;
        if (_cookie == null || _cookie.width != res)
            _cookie = new Texture2D(res, res, TextureFormat.R8, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };

        var px = new Color[res * res];
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float n = Fbm((float)x / res * 4f, (float)y / res * 4f);
                float shade = Mathf.SmoothStep(shadowCoverage - 0.07f, shadowCoverage + 0.07f, n);
                float halo = Mathf.SmoothStep(shadowCoverage - 0.2f, shadowCoverage + 0.2f, n);
                shade = shade * 0.75f + halo * 0.25f;
                float lum = Mathf.Lerp(1f - shadowStrength, 1f, shade);
                lum = Mathf.Pow(lum, 1.35f);
                px[x + y * res] = new Color(lum, lum, lum, lum);
            }
        _cookie.SetPixels(px);
        _cookie.Apply();
    }

    static float Fbm(float x, float y)
    {
        float v = 0f, a = 0.5f, f = 1f;
        for (int o = 0; o < 4; o++) { v += a * ValueNoiseTiled(x * f, y * f); f *= 2f; a *= 0.5f; }
        return v;
    }
    static float ValueNoiseTiled(float x, float y)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float xf = x - xi, yf = y - yi;
        float u = xf * xf * (3 - 2 * xf), vv = yf * yf * (3 - 2 * yf);
        float a = Hash(xi, yi), b = Hash(xi + 1, yi);
        float c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), vv);
    }
    static float Hash(int x, int y)
    {
        int h = x * 374761393 + y * 668265263;
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
    }
}
