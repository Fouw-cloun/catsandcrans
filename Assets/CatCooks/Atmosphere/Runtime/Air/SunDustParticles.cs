using UnityEngine;

// ============================================================================
//  SunDustParticles — пылинки, плавающие в воздухе (магия «уюта»).
//  Сам создаёт и настраивает ParticleSystem по кнопке: тебе остаётся
//  только сделать материал из шейдера CatCooks/Dust Mote и нажать Build.
//
//  Ставь такой объект над зонами, где висят солнечные лучи, — пыль,
//  мерцающая в лучах, продаёт весь эффект.
//
//  Сеть: НЕ NetworkObject, чистая локальная косметика.
// ============================================================================
[RequireComponent(typeof(ParticleSystem))]
public class SunDustParticles : MonoBehaviour
{
    [Header("Material (CatCooks/Dust Mote)")]
    public Material dustMaterial;

    [Header("Zone")]
    [Tooltip("Объём, в котором плавает пыль (обычно чуть шире зоны лучей, высотой 10–20 м)")]
    public Vector3 volume = new Vector3(60f, 14f, 60f);

    [Header("Dust")]
    [Range(10, 2000)] public int particleCount = 350;
    public Vector2 sizeRange = new Vector2(0.02f, 0.07f);
    public Vector2 lifetimeRange = new Vector2(8f, 14f);
    [Tooltip("Скорость медленного оседания/дрейфа")]
    public float driftSpeed = 0.06f;
    [Tooltip("Хаотичное блуждание (турбулентность воздуха)")]
    public float wanderStrength = 0.08f;
    public Color dustTint = new Color(1f, 0.95f, 0.8f);

    [ContextMenu("Build / Rebuild Particle System")]
    public void Build()
    {
        var ps = GetComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeRange.x, lifetimeRange.y);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.0f, driftSpeed);
        main.startColor = dustTint;
        main.maxParticles = particleCount;
        main.prewarm = true;                       // пыль уже висит при старте, не «включается»
        main.loop = true;
        main.gravityModifier = -0.001f;            // еле заметно всплывает — живее, чем падение

        var emission = ps.emission;
        emission.rateOverTime = particleCount / ((lifetimeRange.x + lifetimeRange.y) * 0.5f);

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = volume;

        // хаотичное блуждание — главный источник «жизни» пылинок
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = wanderStrength;
        noise.frequency = 0.25f;
        noise.scrollSpeed = 0.04f;
        noise.damping = true;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        // плавное появление и исчезновение + лёгкое мерцание по альфе
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(0.55f, 0.5f),   // проседание в середине = мерцание
                new GradientAlphaKey(1f, 0.75f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = grad;

        // разный размер в течение жизни — пылинки «ловят свет»
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        var sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.7f), new Keyframe(0.4f, 1f),
            new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0.6f));
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var renderer = GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.None;   // аддитив — сортировка не нужна
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        if (dustMaterial != null) renderer.sharedMaterial = dustMaterial;

        ps.Play();
    }

    private void Reset() { Build(); }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.5f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, volume);
    }
}
