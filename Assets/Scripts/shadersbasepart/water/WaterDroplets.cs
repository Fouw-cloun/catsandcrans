using UnityEngine;

/// <summary>
/// КАПЛИ ВДОЛЬ СТРУИ. Полностью настраивает Particle System кодом (руками модули
/// собирать не нужно) и ПРИВЯЗЫВАЕТ к напору: капли гуще на сильном напоре,
/// пропадают при закрытии. Рождаются ВДОЛЬ ВСЕЙ дуги струи (эмиттер тянется по
/// пути), летят чуть во все стороны и падают.
///
/// НАСТРОЙКА:
///   1. Дочерний объект к JetOrigin (где WaterJet) → Particle System → этот скрипт.
///   2. Материал частиц: Custom/WaterDropletURP (шейдер капель).
///   3. В WaterJet перетащи этот объект в поле Droplets (привязка к напору).
///      Либо оставь пустым — тогда капли по масштабу родителя.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class WaterDroplets : MonoBehaviour
{
    [Header("Плотность")]
    [Tooltip("Частиц в секунду на ПОЛНОМ напоре")]
    [SerializeField] private float _maxRate = 90f;
    [Tooltip("Ниже этого напора капель нет")]
    [Range(0f, 1f)] [SerializeField] private float _minFlow = 0.05f;

    [Header("Движение")]
    [Tooltip("Скорость отрыва капель (мин/макс)")]
    [SerializeField] private Vector2 _speed = new Vector2(0.4f, 1.6f);
    [Tooltip("Разлёт во все стороны, м/сек")]
    [SerializeField] private float _sideScatter = 1.2f;
    [Tooltip("Гравитация капель")]
    [SerializeField] private float _gravity = 1.0f;
    [Tooltip("Время жизни капли (мин/макс)")]
    [SerializeField] private Vector2 _lifetime = new Vector2(0.3f, 0.75f);
    [Tooltip("Размер капли (мин/макс)")]
    [SerializeField] private Vector2 _size = new Vector2(0.02f, 0.07f);

    [Header("Форма эмиттера")]
    [Tooltip("Длина зоны рождения вдоль струи, м (тянется по локальному Z)")]
    [SerializeField] private float _emitLength = 3.5f;
    [Tooltip("Толщина зоны рождения")]
    [SerializeField] private float _emitThickness = 0.15f;

    private ParticleSystem _ps;
    private ParticleSystem.EmissionModule _emission;
    private WaterJet _jet; // если рядом/в родителе — берём напор оттуда

    private void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
        _jet = GetComponentInParent<WaterJet>();
        Configure();
    }

    /// <summary>Единая настройка PS кодом — источник правды.</summary>
    private void Configure()
    {
        var main = _ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(_lifetime.x, _lifetime.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(_speed.x, _speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(_size.x, _size.y);
        main.gravityModifier = _gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // отрываются от струи
        main.maxParticles = 500;
        main.startColor = Color.white;

        var shape = _ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        // Зона рождения тянется вдоль локального Z (направление струи).
        shape.scale = new Vector3(_emitThickness, _emitThickness, _emitLength);
        shape.position = new Vector3(0f, 0f, _emitLength * 0.5f);

        // Разлёт во все стороны.
        var vel = _ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(-_sideScatter, _sideScatter);
        vel.y = new ParticleSystem.MinMaxCurve(-_sideScatter * 0.6f, _sideScatter);
        vel.z = new ParticleSystem.MinMaxCurve(-_sideScatter * 0.5f, _sideScatter);

        // Затухание размера и альфы.
        var sol = _ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f)));

        var col = _ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var renderer = GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        _emission = _ps.emission;
        _emission.enabled = true;
        _emission.rateOverTime = 0f; // задаём в Update от напора
    }

    private void Update()
    {
        // Напор: из WaterJet (если найден) — иначе по масштабу родителя.
        float flow = _jet != null ? _jet.Flow01 : Mathf.Clamp01(transform.lossyScale.x);

        if (flow < _minFlow)
        {
            _emission.rateOverTime = 0f;
        }
        else
        {
            _emission.rateOverTime = _maxRate * flow;
        }
    }
}
