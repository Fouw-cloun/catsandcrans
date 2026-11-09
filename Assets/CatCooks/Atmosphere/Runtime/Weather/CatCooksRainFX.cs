using UnityEngine;

// ============================================================================
//  CatCooksRainFX — мультяшный дождь вокруг игрока.
//   • Капли: вытянутые штрихи, падают с ветром, бьются о мир (Collision).
//   • При ударе — саб-эмиттеры: корона-брызги вверх и круг на земле.
//   • Сила — из CatCooksWeatherState.Rain (пишет Mood Director).
//  Зона едет за камерой, частицы в мировом пространстве — капли не дёргаются.
//  Дешевле: меньше Max Drops и Collision Quality = Low.
// ============================================================================
[AddComponentMenu("CatCooks/Атмосфера/Rain FX")]
public class CatCooksRainFX : MonoBehaviour
{
    [Header("Зона")]
    public float radius = 22f;
    public float height = 18f;
    [Header("Капли")]
    [Tooltip("Капель в секунду при Rain = 1")]
    public float maxRate = 2600f;
    public int maxDrops = 4000;
    public float fallSpeed = 22f;
    public Vector2 dropLength = new Vector2(0.35f, 0.8f);
    public float dropWidth = 0.025f;
    public Color dropColor = new Color(0.82f, 0.9f, 1f, 0.55f);
    [Header("Удар о землю")]
    public LayerMask collideWith = ~0;
    public ParticleSystemCollisionQuality collisionQuality = ParticleSystemCollisionQuality.Medium;
    [Range(0f, 1f)] public float splashChance = 0.6f;
    public Color splashColor = new Color(0.9f, 0.96f, 1f, 0.9f);
    [Tooltip("Ручная сила для теста; -1 = из погоды")]
    [Range(-1f, 1f)] public float overrideRain = -1f;

    ParticleSystem _drops, _crown, _ring;

    void OnEnable() { Build(); }
    void OnDisable() { if (_drops != null) Destroy(_drops.gameObject); _drops = null; }

    void Build()
    {
        if (_drops != null) return;
        // капли
        _drops = CatCooksFxKit.System(transform, "Rain Drops", CatCooksFxKit.Mat(CatCooksFxKit.Streak, false), maxDrops);
        var m = _drops.main;
        m.startLifetime = height / fallSpeed * 1.4f;
        m.startSpeed = 0f;
        m.startSize3D = true;
        m.startSizeX = new ParticleSystem.MinMaxCurve(dropWidth, dropWidth * 1.6f);
        m.startSizeY = new ParticleSystem.MinMaxCurve(dropLength.x, dropLength.y);
        m.startSizeZ = 1f;
        m.startColor = dropColor;
        var sh = _drops.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = new Vector3(radius * 2f, 0.1f, radius * 2f);
        var vel = _drops.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        var r = _drops.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.02f; r.lengthScale = 1f;
        var col = _drops.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World;
        col.mode = ParticleSystemCollisionMode.Collision3D; col.quality = collisionQuality;
        col.collidesWith = collideWith; col.lifetimeLoss = 1f; col.bounce = 0f;

        // корона: короткие штрихи вверх-вбок
        _crown = CatCooksFxKit.System(_drops.transform, "Rain Crown", CatCooksFxKit.Mat(CatCooksFxKit.Dot, false), 3000);
        var cm = _crown.main; cm.loop = false; cm.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
        cm.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f); cm.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
        cm.gravityModifier = 1.6f; cm.startColor = splashColor;
        var csh = _crown.shape; csh.enabled = true; csh.shapeType = ParticleSystemShapeType.Cone; csh.angle = 35f; csh.radius = 0.02f;
        csh.rotation = new Vector3(-90f, 0f, 0f);
        var ce = _crown.emission; ce.SetBursts(new[] { new ParticleSystem.Burst(0f, 3, 6) });
        var cc = _crown.colorOverLifetime; cc.enabled = true; cc.color = CatCooksFxKit.Fade(splashColor, 1f, 0.6f);

        // круг на земле: плоский, растёт и гаснет
        _ring = CatCooksFxKit.System(_drops.transform, "Rain Ring", CatCooksFxKit.Mat(CatCooksFxKit.Ring, false), 2000);
        var rm = _ring.main; rm.loop = false; rm.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
        rm.startSpeed = 0f; rm.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        rm.startRotation3D = true; rm.startRotationX = Mathf.PI * 0.5f;
        rm.startColor = new Color(splashColor.r, splashColor.g, splashColor.b, 0.6f);
        var re = _ring.emission; re.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
        var rs = _ring.sizeOverLifetime; rs.enabled = true; rs.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1f));
        var rc = _ring.colorOverLifetime; rc.enabled = true; rc.color = CatCooksFxKit.Fade(Color.white, 1f, 0.2f);
        var rr = _ring.GetComponent<ParticleSystemRenderer>(); rr.alignment = ParticleSystemRenderSpace.World;

        var sub = _drops.subEmitters; sub.enabled = true;
        sub.AddSubEmitter(_crown, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing, splashChance);
        sub.AddSubEmitter(_ring, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing, splashChance);
        _drops.Play();
    }

    void Update()
    {
        if (_drops == null) return;
        if (CatCooksWeatherState.TryGetPlayer(out Vector3 p)) transform.position = p + Vector3.up * height;
        float rain = overrideRain >= 0f ? overrideRain : CatCooksWeatherState.Rain;
        var em = _drops.emission; em.rateOverTime = maxRate * rain * rain;
        // ветер наклоняет дождь
        Vector3 w = CatCooksWeatherState.WindDir3 * (CatCooksWeatherState.Wind * 8f);
        var vel = _drops.velocityOverLifetime;
        vel.x = w.x; vel.y = -fallSpeed; vel.z = w.z;
    }
}
