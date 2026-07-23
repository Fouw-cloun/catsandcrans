using UnityEngine;

// ============================================================================
//  CatCooksMountainHaze — привязывает дымку гор к текущей погоде/стилю.
//
//  Зачем: у шейдера CatCooks/Toon Mountain цвет дымки задан в материале.
//  Если его не обновлять, при смене пресета (18 стилей x 12 погод) горы
//  останутся в старой палитре и вывалятся из кадра. Этот скрипт каждый кадр
//  копирует в материал актуальный RenderSettings.fogColor, которым уже
//  рулит CatCooksWorldDirector.
//
//  Установка: повесь на любой объект (например, на родителя гор),
//  укажи материалы гор. Работает и в редакторе, и в игре.
//
//  Сеть: чистая локальная косметика, НЕ NetworkObject.
// ============================================================================
[ExecuteAlways]
[DisallowMultipleComponent]
public class CatCooksMountainHaze : MonoBehaviour
{
    [Header("Материалы гор (CatCooks/Toon Mountain)")]
    public Material[] mountainMaterials;

    [Header("Настройка")]
    [Tooltip("Сдвиг цвета дымки относительно тумана. 0 = точно как туман, " +
             "1 = полностью свой цвет ниже")]
    [Range(0f, 1f)] public float customBlend = 0f;
    public Color customHaze = new Color(0.78f, 0.82f, 0.86f);

    [Tooltip("Осветлить дымку относительно тумана (горы дальше, воздуха больше)")]
    [Range(0.5f, 1.6f)] public float hazeBrightness = 1.05f;

    [Tooltip("Также подводить оттенок теней к цвету неба — горы садятся в атмосферу")]
    public bool syncShadowTint = true;
    [Range(0f, 1f)] public float shadowTintFromSky = 0.35f;

    static readonly int HazeId = Shader.PropertyToID("_HazeColor");
    static readonly int ShadowTintId = Shader.PropertyToID("_ShadowTint");

    void LateUpdate() => Apply();
#if UNITY_EDITOR
    void OnValidate() => UnityEditor.EditorApplication.delayCall += () => { if (this != null) Apply(); };
#endif

    public void Apply()
    {
        if (mountainMaterials == null) return;

        Color fog = RenderSettings.fog ? RenderSettings.fogColor : RenderSettings.ambientSkyColor;
        Color haze = Color.Lerp(fog, customHaze, customBlend) * hazeBrightness;
        haze.a = 1f;

        Color skyTint = RenderSettings.ambientSkyColor;

        foreach (var m in mountainMaterials)
        {
            if (m == null) continue;
            if (m.HasProperty(HazeId)) m.SetColor(HazeId, haze);

            if (syncShadowTint && m.HasProperty(ShadowTintId))
            {
                Color baseTint = new Color(0.60f, 0.70f, 0.95f);
                m.SetColor(ShadowTintId, Color.Lerp(baseTint, skyTint, shadowTintFromSky));
            }
        }
    }
}
