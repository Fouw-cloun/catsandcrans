using UnityEngine;

/// <summary>
/// Подсветка интерактивного предмета: при наведении плавно разгорается Rim
/// и утолщается Outline (свойства из шейдеров CatCooks), с мягкой пульсацией.
/// Работает через MaterialPropertyBlock — материалы НЕ копируются,
/// батчинг и память не страдают.
///
/// Использование: повесь на предмет, из своего кода взаимодействия вызывай
/// SetHighlighted(true/false). Или включи hoverByMouse для теста мышью
/// (нужен Collider).
/// </summary>
public class InteractableHighlight : MonoBehaviour
{
    [Header("Что подсвечиваем")]
    [Tooltip("Если пусто — возьмёт все Renderer'ы на объекте и детях")]
    public Renderer[] renderers;

    [Header("Rim")]
    public Color highlightRimColor = new Color(1f, 0.85f, 0.4f); // тёплое золото
    [Tooltip("Во сколько раз усилить Rim Intensity при подсветке")]
    public float rimBoost = 2.5f;

    [Header("Outline")]
    [Tooltip("Прибавка к ширине обводки в пикселях при подсветке")]
    public float outlineWidthAdd = 1.2f;
    public Color highlightOutlineColor = new Color(1f, 0.75f, 0.25f);

    [Header("Анимация")]
    [Tooltip("Скорость появления/затухания подсветки")]
    public float fadeSpeed = 8f;
    [Tooltip("Скорость пульсации")]
    public float pulseSpeed = 3f;
    [Tooltip("Глубина пульсации (0 = без пульса)")]
    [Range(0f, 1f)] public float pulseAmount = 0.25f;

    [Header("Отладка")]
    public bool hoverByMouse = false;

    // ID свойств шейдеров CatCooks
    static readonly int RimColorId      = Shader.PropertyToID("_RimColor");
    static readonly int RimIntensityId  = Shader.PropertyToID("_RimIntensity");
    static readonly int OutlineWidthId  = Shader.PropertyToID("_OutlineWidth");
    static readonly int OutlineColorId  = Shader.PropertyToID("_OutlineColor");

    private MaterialPropertyBlock mpb;
    private float highlight;          // 0..1 текущее состояние
    private bool targetOn;

    // базовые значения с материала (берём с первого рендерера)
    private Color baseRimColor;
    private float baseRimIntensity;
    private float baseOutlineWidth;
    private Color baseOutlineColor;

    void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>();

        mpb = new MaterialPropertyBlock();

        var m = renderers.Length > 0 ? renderers[0].sharedMaterial : null;
        baseRimColor     = m != null && m.HasProperty(RimColorId)     ? m.GetColor(RimColorId)     : Color.white;
        baseRimIntensity = m != null && m.HasProperty(RimIntensityId) ? m.GetFloat(RimIntensityId) : 0.7f;
        baseOutlineWidth = m != null && m.HasProperty(OutlineWidthId) ? m.GetFloat(OutlineWidthId) : 1.5f;
        baseOutlineColor = m != null && m.HasProperty(OutlineColorId) ? m.GetColor(OutlineColorId) : Color.black;
    }

    /// <summary>Включить/выключить подсветку из твоего кода взаимодействия.</summary>
    public void SetHighlighted(bool on) => targetOn = on;

    void OnMouseEnter() { if (hoverByMouse) targetOn = true; }
    void OnMouseExit()  { if (hoverByMouse) targetOn = false; }

    void Update()
    {
        float t = 1f - Mathf.Exp(-fadeSpeed * Time.deltaTime);
        highlight = Mathf.Lerp(highlight, targetOn ? 1f : 0f, t);

        if (highlight < 0.001f && !targetOn)
        {
            // полностью погасло — сбрасываем блоки, чтобы вернуть чистый материал
            foreach (var r in renderers) r.SetPropertyBlock(null);
            return;
        }

        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed * Mathf.PI) * pulseAmount * highlight;
        float h = highlight * pulse;

        Color rimC   = Color.Lerp(baseRimColor, highlightRimColor, highlight);
        float rimI   = Mathf.Lerp(baseRimIntensity, baseRimIntensity * rimBoost, h);
        float outW   = baseOutlineWidth + outlineWidthAdd * h;
        Color outC   = Color.Lerp(baseOutlineColor, highlightOutlineColor, highlight);

        foreach (var r in renderers)
        {
            r.GetPropertyBlock(mpb);
            mpb.SetColor(RimColorId, rimC);
            mpb.SetFloat(RimIntensityId, rimI);
            mpb.SetFloat(OutlineWidthId, outW);
            mpb.SetColor(OutlineColorId, outC);
            r.SetPropertyBlock(mpb);
        }
    }
}
