using UnityEngine;

/// <summary>
/// БРЫЗГИ-ОШМЁТКИ пены в точке удара струи. Полностью настраивает Particle
/// System кодом — тебе не нужно собирать модули руками: повесь на пустышку,
/// задай материал, готово.
///
/// Летят вверх-в-стороны рваными белыми хлопьями и падают (как на референсе).
/// Кладётся РЕБЁНКОМ объекта короны (Splash Effect), чтобы WaterJet включал/
/// выключал и масштабировал всё вместе.
///
/// Материал: URP/Particles/Unlit (Surface Transparent, Blend Additive),
/// цвет HDR белый ~(3,3.5,4). Текстура — мягкое пятно или WaterNoise_Bubbles.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class FoamBurstParticles : MonoBehaviour
{
    [Tooltip("Частиц в секунду на полном напоре")]
    [SerializeField] private float _maxRate = 60f;
    [Tooltip("Скорость разлёта")]
    [SerializeField] private float _speed = 3.5f;
    [Tooltip("Размер хлопьев (мин/макс)")]
    [SerializeField] private Vector2 _size = new Vector2(0.06f, 0.22f);
    [Tooltip("Время жизни хлопа")]
    [SerializeField] private Vector2 _lifetime = new Vector2(0.35f, 0.8f);
    [Tooltip("Гравитация хлопьев")]
    [SerializeField] private float _gravity = 1.6f;

    private ParticleSystem _ps;
    private ParticleSystem.EmissionModule _emission;

    private void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
        Configure();
    }

    /// <summary>Вся настройка PS кодом — единый источник правды.</summary>
    private void Configure()
    {
        var main = _ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(_lifetime.x, _lifetime.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(_speed * 0.5f, _speed);
        main.startSize = new ParticleSystem.MinMaxCurve(_size.x, _size.y);
        main.gravityModifier = _gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        var shape = _ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 55f;              // широкий веер вверх
        shape.radius = 0.25f;
        shape.rotation = new Vector3(-90f, 0f, 0f); // конус смотрит вверх

        _emission = _ps.emission;
        _emission.enabled = true;
        _emission.rateOverTime = _maxRate;

        // Затухание размера и альфы к концу жизни.
        var sol = _ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.1f)));

        var col = _ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        // Немного вращения хлопьев — живее.
        var rot = _ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
    }

    private void Update()
    {
        // Масштабируем интенсивность от масштаба родителя (WaterJet масштабирует
        // Splash Effect от напора — используем это как источник напора).
        float k = Mathf.Clamp01(transform.lossyScale.x);
        _emission.rateOverTime = _maxRate * k;
    }
}
