using UnityEngine;

/// <summary>
/// «Живой» свет: мерцание огня, тёплая пульсация гирлянд, дыхание ламп.
/// Использует шум Перлина — движение плавное и органичное, не дёрганое.
/// Повесь на GameObject с компонентом Light. Работает и с эмиссией меша.
/// </summary>
[RequireComponent(typeof(Light))]
public class LivingLight : MonoBehaviour
{
    public enum Preset { Fire, Garland, Lamp, Custom }

    [Header("Пресет")]
    [Tooltip("Fire — огонь плиты/гриля. Garland — медленная тёплая пульсация. Lamp — едва заметное дыхание.")]
    public Preset preset = Preset.Fire;

    [Header("Custom-настройки (для Preset = Custom)")]
    [Tooltip("Насколько сильно гуляет интенсивность (доля от базовой). 0.25 = ±25%")]
    [Range(0f, 1f)] public float intensityJitter = 0.25f;
    [Tooltip("Скорость мерцания")]
    public float speed = 3f;
    [Tooltip("Смещение цвета к красному при провалах яркости (эффект углей)")]
    [Range(0f, 0.5f)] public float redShift = 0.15f;

    [Header("Эмиссия меша (опционально)")]
    [Tooltip("Renderer со светящимся материалом (лампочка, угли). Эмиссия будет пульсировать вместе со светом.")]
    public Renderer emissiveRenderer;
    public string emissionProperty = "_EmissionColor";

    private Light lt;
    private float baseIntensity;
    private Color baseColor;
    private Color baseEmission;
    private float seed;
    private MaterialPropertyBlock mpb;

    void Awake()
    {
        lt = GetComponent<Light>();
        baseIntensity = lt.intensity;
        baseColor = lt.color;
        seed = Random.value * 100f; // чтобы соседние лампы не мигали синхронно

        if (emissiveRenderer != null)
        {
            mpb = new MaterialPropertyBlock();
            baseEmission = emissiveRenderer.sharedMaterial.HasProperty(emissionProperty)
                ? emissiveRenderer.sharedMaterial.GetColor(emissionProperty)
                : Color.white;
        }

        ApplyPreset();
    }

    void ApplyPreset()
    {
        switch (preset)
        {
            case Preset.Fire:    intensityJitter = 0.35f; speed = 6f;   redShift = 0.20f; break;
            case Preset.Garland: intensityJitter = 0.15f; speed = 0.8f; redShift = 0.05f; break;
            case Preset.Lamp:    intensityJitter = 0.05f; speed = 0.5f; redShift = 0f;    break;
            case Preset.Custom:  break;
        }
    }

    void Update()
    {
        // Два слоя шума Перлина: медленная волна + быстрая рябь
        float t = Time.time * speed + seed;
        float slow = Mathf.PerlinNoise(t * 0.3f, seed);
        float fast = Mathf.PerlinNoise(t, seed + 17f);
        float noise = slow * 0.6f + fast * 0.4f;          // 0..1
        float factor = 1f + (noise - 0.5f) * 2f * intensityJitter;

        lt.intensity = baseIntensity * factor;

        // при провалах яркости свет чуть краснеет — как настоящие угли
        float dim = Mathf.Clamp01(1f - factor);
        lt.color = Color.Lerp(baseColor,
                              new Color(baseColor.r, baseColor.g * 0.7f, baseColor.b * 0.5f),
                              dim * redShift * 4f);

        if (emissiveRenderer != null)
        {
            emissiveRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(emissionProperty, baseEmission * factor);
            emissiveRenderer.SetPropertyBlock(mpb);
        }
    }
}
