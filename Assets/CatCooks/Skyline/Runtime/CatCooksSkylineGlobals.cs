using UnityEngine;

/// <summary>
/// Передаёт дальнему городу цвет дымки и «ночь» из погоды (CatCooksWeatherState):
/// город сам розовеет на закате, синеет ночью и зажигает окна.
/// Один на сцену (кладётся генератором на корень города).
/// </summary>
[ExecuteAlways]
[AddComponentMenu("CatCooks/Skyline/Skyline Globals")]
public class CatCooksSkylineGlobals : MonoBehaviour
{
    [Tooltip("С какой дистанции город начинает уходить в дымку, м")]
    public float hazeStart = 250f;
    [Tooltip("На какой дистанции дом почти полностью цвета дымки, м")]
    public float hazeEnd = 1600f;
    [Range(0.3f, 3f)] public float hazePower = 1.1f;
    [Range(0f, 1f)] public float hazeMax = 0.85f;

    [Header("Дымка в улицах (как в Shady Job: низ домов тонет, верх чистый)")]
    [Tooltip("Уровень земли города (ставит генератор)")]
    public float groundY = 0f;
    [Tooltip("Высота слоя уличной дымки, м")]
    public float streetHazeHeight = 70f;
    [Range(0f, 1f)] public float streetHaze = 0.55f;

    static readonly int Near = Shader.PropertyToID("_CC_SkyFogNear"), Far = Shader.PropertyToID("_CC_SkyFogFar"), Par = Shader.PropertyToID("_CC_SkyParams"), Max = Shader.PropertyToID("_CC_SkyHazeMax"),
        Zen = Shader.PropertyToID("_CC_SkyZenith"), Hor = Shader.PropertyToID("_CC_SkyHorizon"), Sun = Shader.PropertyToID("_CC_SunCol"),
        Wx = Shader.PropertyToID("_CC_Weather"), Street = Shader.PropertyToID("_CC_StreetHaze");

    void Update()
    {
        var near = CatCooksWeatherState.FogNear;
        var far = CatCooksWeatherState.FogFar;
        Shader.SetGlobalColor(Near, near);
        Shader.SetGlobalColor(Far, far);
        float night = 1f - Mathf.Clamp01(CatCooksWeatherState.Day01);
        float endAdj = Mathf.Lerp(hazeEnd, hazeEnd * 0.6f, CatCooksWeatherState.FogDensity);   // в туман — ближе
        Shader.SetGlobalVector(Par, new Vector4(hazeStart, endAdj, hazePower, night));
        Shader.SetGlobalFloat(Max, hazeMax);   // дальше hazeEnd дом не растворяется целиком — силуэт остаётся
        // погода → дома: небо для отражений в окнах, мокрость, пыль, уличная дымка
        Shader.SetGlobalColor(Zen, CatCooksWeatherState.SkyZenith);
        Shader.SetGlobalColor(Hor, CatCooksWeatherState.SkyHorizon);
        Shader.SetGlobalColor(Sun, CatCooksWeatherState.SunColor);
        float street = streetHaze * Mathf.Lerp(0.6f, 1.4f, CatCooksWeatherState.FogDensity) + CatCooksWeatherState.Dust * 0.4f;
        Shader.SetGlobalVector(Wx, new Vector4(CatCooksWeatherState.Wetness, CatCooksWeatherState.Dust, groundY, streetHazeHeight));
        Shader.SetGlobalFloat(Street, Mathf.Clamp01(street));
    }
}
