using UnityEngine;

/// <summary>
/// ВИЗУАЛ ЭЛЕКТРОДА. Вешается на держатель, рядом с WeldingTorchItem.
///
/// Электрод КРАСНЕЕТ, пока горит дуга, и медленно остывает после. Длина не
/// меняется — расход виден по нагреву и по цифре в углу экрана.
///
/// Укорачивание убрано намеренно: масштабирование меша по одной оси заметно
/// дёргало модель и почти не читалось глазом, а возни с точкой опоры и осью
/// требовало много. Нагрев даёт ту же обратную связь и выглядит честнее.
///
/// Чисто локальная штука: и остаток, и горение уже синхронизированы
/// в WeldingTorchItem, по сети ничего не гоняется.
/// </summary>
[RequireComponent(typeof(WeldingTorchItem))]
public class WeldingElectrodeVisual : MonoBehaviour
{
    [Header("Что греется")]
    [Tooltip("Меш электрода. Меняется только ЦВЕТ, размер и положение " +
             "не трогаются вовсе.")]
    [SerializeField] private Renderer _electrode;

    [Header("Нагрев")]
    [Tooltip("За сколько секунд горения раскаляется докрасна")]
    [SerializeField] private float _heatUpSeconds = 1.2f;

    [Tooltip("За сколько секунд остывает после дуги")]
    [SerializeField] private float _coolDownSeconds = 4f;

    [Tooltip("Цвет раскалённого электрода")]
    [SerializeField] private Color _hotColor = new Color(1f, 0.35f, 0.08f);

    [Tooltip("Во сколько раз ярче на пике. Выше 1 уводит в HDR — с Bloom " +
             "электрод начинает светиться.")]
    [Range(1f, 6f)][SerializeField] private float _hotIntensity = 3f;

    [Header("Догорание")]
    [Tooltip("С какого остатка электрод раскаляется сильнее обычного. " +
             "Огарок горит злее — заодно видно, что пора перезаряжаться.")]
    [Range(0f, 0.6f)][SerializeField] private float _lowFrom = 0.3f;

    [Tooltip("Насколько ярче светится догорающий огарок")]
    [Range(1f, 3f)][SerializeField] private float _lowBoost = 1.6f;

    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorIdLegacy = Shader.PropertyToID("_Color");
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    private WeldingTorchItem _torch;
    private MaterialPropertyBlock _mpb;
    private Color _coldColor = Color.white;
    private float _heat;

    private void Awake()
    {
        _torch = GetComponent<WeldingTorchItem>();

        if (_electrode == null) _electrode = GetComponentInChildren<Renderer>();

        if (_electrode == null)
        {
            Debug.LogWarning($"[Электрод] на {name} не найден меш электрода — " +
                             "греться будет нечему", this);
            enabled = false;
            return;
        }

        _mpb = new MaterialPropertyBlock();

        // Запоминаем исходный цвет, чтобы возвращаться к нему при остывании.
        Material m = _electrode.sharedMaterial;
        if (m == null) return;

        if (m.HasProperty(ColorId)) _coldColor = m.GetColor(ColorId);
        else if (m.HasProperty(ColorIdLegacy)) _coldColor = m.GetColor(ColorIdLegacy);
    }

    private void LateUpdate()
    {
        UpdateHeat();
        ApplyColor();
    }

    /// <summary>
    /// Нагрев копится, пока горит дуга, и медленно спадает.
    ///
    /// Именно медленно: раскалённый металл не остывает мгновенно, и по красному
    /// электроду видно, что держатель только что работал.
    /// </summary>
    private void UpdateHeat()
    {
        bool burning = _torch.IsBurning;

        float rate = burning
            ? 1f / Mathf.Max(0.05f, _heatUpSeconds)
            : 1f / Mathf.Max(0.05f, _coolDownSeconds);

        _heat = Mathf.MoveTowards(_heat, burning ? 1f : 0f, rate * Time.deltaTime);
    }

    /// <summary>Цвет через MaterialPropertyBlock, чтобы не плодить копию
    /// материала на каждый держатель.</summary>
    private void ApplyColor()
    {
        if (_electrode == null || _mpb == null) return;

        // Догорающий огарок раскаляется сильнее — заодно подсказка, что пора
        // идти за новым.
        float boost = 1f;
        if (_lowFrom > 0.001f && _torch.Electrode01 < _lowFrom)
            boost = Mathf.Lerp(_lowBoost, 1f, _torch.Electrode01 / _lowFrom);

        Color hot = _hotColor * (_hotIntensity * boost);
        Color c = Color.Lerp(_coldColor, hot, _heat);

        _electrode.GetPropertyBlock(_mpb);
        _mpb.SetColor(ColorId, c);
        _mpb.SetColor(ColorIdLegacy, c);
        _mpb.SetColor(EmissionId, _hotColor * (_hotIntensity * boost * _heat));
        _electrode.SetPropertyBlock(_mpb);
    }
}
