using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ГАШЕНИЕ ОБВОДКИ В ТУМАНЕ.
///
/// Обводка выделения рисуется поверх прозрачной геометрии и туманом не
/// гасится: сквозь плотную пыль видны чёткие цветные контуры техники, и
/// именно они выдают, что тумана как бы и нет.
///
/// Строго это лечится в шейдере обводки одной строкой — CatCooksVolumeFog
/// пишет глобаль _CatFogOutlineFade, её достаточно домножить на альфу:
///
///     alpha *= _CatFogOutlineFade;
///
/// Но обводка часто сделана отдельным компонентом или Renderer Feature, куда
/// строку не вставить. Тогда вешается этот компонент: он гасит обводку по
/// реальному светопропусканию до объекта.
///
/// ═══ ПОЧЕМУ НЕ ПРОСТО «ВЫКЛЮЧИТЬ, ПОКА ИГРОК В ЗОНЕ» ═══
///
/// Зона в семьдесят метров накрывает всю площадку, и игрок будет внутри неё
/// всегда. Выключение по зоне означает выключение навсегда.
///
/// Правильный признак — не «внутри ли зоны», а «сколько света дошло». Тогда
/// близкая тачка обводится нормально даже в бурю, а дальний экскаватор теряет
/// контур ровно тогда же, когда пропадает сам. Это к тому же само собой даёт
/// подсказку игроку: обводка есть — объект в пределах досягаемости.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Атмосфера/Fog Outline Fade")]
public class CatCooksFogOutlineFade : MonoBehaviour
{
    [Tooltip("Туман. Пусто — найдётся в сцене.")]
    [SerializeField] private CatCooksVolumeFog _fog;

    [Header("Что гасить")]
    [Tooltip("Компоненты обводки, которые выключаются при плохой видимости. " +
             "Сюда идёт твой Outline-скрипт.")]
    [SerializeField] private List<Behaviour> _outlineBehaviours = new List<Behaviour>();

    [Tooltip("Рендереры обводки, которые выключаются целиком")]
    [SerializeField] private List<Renderer> _outlineRenderers = new List<Renderer>();

    [Header("Плавное гашение (если шейдер обводки умеет)")]
    [Tooltip("Имя float-свойства в материале обводки, куда писать множитель " +
             "0..1. Пусто — плавного гашения не будет, только включение и " +
             "выключение по порогу.")]
    [SerializeField] private string _fadeProperty = "";

    [Header("Пороги")]
    [Tooltip("Ниже этого светопропускания обводка выключается совсем. " +
             "0.25 значит «когда до объекта доходит меньше четверти света».")]
    [Range(0f, 1f)][SerializeField] private float _hideBelow = 0.25f;

    [Tooltip("Выше этого — обводка на полную. Между порогами идёт плавный " +
             "переход, если задано имя свойства.")]
    [Range(0f, 1f)][SerializeField] private float _fullAbove = 0.6f;

    [Tooltip("Как часто пересчитывать, секунды. Каждый кадр не нужно: " +
             "видимость меняется медленно.")]
    [Min(0f)][SerializeField] private float _interval = 0.15f;

    private float _timer;
    private MaterialPropertyBlock _mpb;
    private int _fadeId = -1;
    private bool _hidden;

    private void OnEnable()
    {
        if (_fog == null) _fog = Object.FindFirstObjectByType<CatCooksVolumeFog>();

        if (!string.IsNullOrEmpty(_fadeProperty))
        {
            _fadeId = Shader.PropertyToID(_fadeProperty);
            _mpb = new MaterialPropertyBlock();
        }

        _hidden = false;
        Apply(1f);
    }

    private void OnDisable() => Apply(1f);

    private void LateUpdate()
    {
        if (_fog == null) return;

        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = _interval;

        float t = _fog.TransmittanceAt(transform.position);
        Apply(t);
    }

    private void Apply(float transmittance)
    {
        bool hide = transmittance < _hideBelow;

        if (hide != _hidden)
        {
            _hidden = hide;

            for (int i = 0; i < _outlineBehaviours.Count; i++)
                if (_outlineBehaviours[i] != null) _outlineBehaviours[i].enabled = !hide;

            for (int i = 0; i < _outlineRenderers.Count; i++)
                if (_outlineRenderers[i] != null) _outlineRenderers[i].enabled = !hide;
        }

        if (_fadeId == -1 || _mpb == null) return;

        float fade = Mathf.InverseLerp(_hideBelow, Mathf.Max(_hideBelow + 0.01f, _fullAbove),
                                       transmittance);

        for (int i = 0; i < _outlineRenderers.Count; i++)
        {
            Renderer r = _outlineRenderers[i];
            if (r == null) continue;

            r.GetPropertyBlock(_mpb);
            _mpb.SetFloat(_fadeId, fade);
            r.SetPropertyBlock(_mpb);
        }
    }
}
