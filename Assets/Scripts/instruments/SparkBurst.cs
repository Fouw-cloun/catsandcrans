using UnityEngine;

/// <summary>
/// ОДНОРАЗОВЫЙ ЗАЛП ИСКР. Вызывается статически из любого места:
///
///     SparkBurst.Play(transform.position);
///
/// Система частиц собирается из кода и сама себя удаляет — настраивать в
/// инспекторе нечего, префаб не нужен.
///
/// СЕТЬ: это чистый визуал. Вызывать нужно у КАЖДОГО клиента (обычно из
/// ObserversRpc), а не только на сервере.
/// </summary>
public static class SparkBurst
{
    /// <summary>Тёплый жёлтый — сердцевина искры.</summary>
    public static readonly Color DefaultHot = new Color(1f, 0.95f, 0.4f);

    /// <summary>Оранжевый — остывающий хвост.</summary>
    public static readonly Color DefaultCool = new Color(1f, 0.42f, 0.05f);

    public static void Play(Vector3 position, int count = 110, float speed = 7f)
        => Play(position, Vector3.up, 180f, count, speed, DefaultHot, DefaultCool, 1f);

    /// <summary>
    /// Залп в заданном направлении.
    /// </summary>
    /// <param name="direction">Куда бьёт струя искр.</param>
    /// <param name="coneAngle">Разброс вокруг направления, градусы.
    /// 180 = во все стороны (фонтан), 20 = узкая струя.</param>
    /// <param name="intensity">Пиковая яркость. Больше 1 уводит цвет в HDR:
    /// с аддитивным смешиванием и Bloom искры начинают ощутимо светиться.
    /// Без Bloom всё выше 1 просто упрётся в белый.</param>
    public static void Play(Vector3 position, Vector3 direction, float coneAngle,
                            int count, float speed, Color hot, Color cool,
                            float intensity)
    {
        if (count <= 0) return;

        GameObject go = new GameObject("SparkBurst");

        // ВЫКЛЮЧЕННЫМ. Система частиц начинает играть сразу при AddComponent,
        // а менять duration у играющей нельзя — Unity ругается в консоль.
        // Настраиваем на выключенном объекте и только потом включаем.
        go.SetActive(false);
        go.transform.position = position;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        Configure(ps, speed, hot, cool);

        go.SetActive(true);
        ps.Play();

        Vector3 dir = direction.sqrMagnitude < 0.0001f ? Vector3.up : direction.normalized;

        // Яркость множим только на цвет, альфу не трогаем: иначе искры стали бы
        // непрозрачными кубиками вместо светящихся штрихов.
        hot = Boost(hot, intensity);
        cool = Boost(cool, intensity);

        // Эмиссия ручная: система не должна сыпать искры сама по себе,
        // нам нужен ровно один залп.
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams
        {
            applyShapeToPosition = true
        };

        for (int i = 0; i < count; i++)
        {
            // Небольшая доля искр всё же уходит вбок от струи — от идеального
            // конуса веет искусственностью.
            float spread = Random.value < 0.12f ? Mathf.Min(coneAngle * 3f, 180f) : coneAngle;
            Vector3 v = RandomInCone(dir, spread);

            ep.velocity = v * speed * Random.Range(0.35f, 1.4f);
            ep.startColor = Color.Lerp(hot, cool, Random.Range(0f, 0.55f));
            ep.startSize = Random.Range(0.008f, 0.022f);
            ep.startLifetime = Random.Range(0.25f, 0.9f);

            ps.Emit(ep, 1);
        }

        // Живём чуть дольше самой долгой искры и уходим.
        Object.Destroy(go, 1.6f);
    }

    private static Color Boost(Color c, float k)
        => new Color(c.r * k, c.g * k, c.b * k, c.a);

    /// <summary>
    /// Равномерная точка внутри конуса. Через Quaternion.Euler со случайными
    /// углами получалось бы сгущение к оси — здесь распределение честное.
    /// </summary>
    private static Vector3 RandomInCone(Vector3 axis, float degrees)
    {
        if (degrees <= 0.01f) return axis;

        float rad = Mathf.Deg2Rad * Mathf.Min(degrees, 180f);
        float z = Random.Range(Mathf.Cos(rad), 1f);
        float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
        float t = Random.Range(0f, Mathf.PI * 2f);

        Vector3 local = new Vector3(r * Mathf.Cos(t), r * Mathf.Sin(t), z);
        return Quaternion.FromToRotation(Vector3.forward, axis) * local;
    }

    private static void Configure(ParticleSystem ps, float speed, Color hot, Color cool)
    {
        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 512;
        main.startSpeed = 0f;          // скорость задаём поштучно
        main.startLifetime = 1f;       // тоже поштучно
        main.gravityModifier = 1.6f;   // искры быстро проваливаются вниз

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = false;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        // Гаснут к концу жизни: остывающий металл темнеет, а не исчезает разом.
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 0.55f),
                new GradientColorKey(new Color(0.6f, 0.15f, 0.02f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.6f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = g;

        // Тормозим в воздухе — искры лёгкие.
        ParticleSystem.LimitVelocityOverLifetimeModule lim = ps.limitVelocityOverLifetime;
        lim.enabled = true;
        lim.dampen = 0.08f;
        lim.limit = new ParticleSystem.MinMaxCurve(speed);

        ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();

        // Растянутый билборд — именно он даёт искре характерный штрих вместо
        // точки. Длина зависит от скорости, поэтому быстрые искры длиннее.
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.06f;
        r.lengthScale = 2.5f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.material = BuildSparkMaterial();
    }

    /// <summary>
    /// Материал искр. Пытаемся включить аддитивное смешивание — тогда искры
    /// светятся и перекрывают друг друга ярче. Если шейдер таких свойств не
    /// знает, останется обычная прозрачность: чуть тусклее, но работает.
    /// </summary>
    private static Material BuildSparkMaterial()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Particles/Standard Unlit")
                 ?? Shader.Find("Sprites/Default");

        if (sh == null) return null;

        Material m = new Material(sh) { name = "Sparks (auto)" };

        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);

        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);   // прозрачный
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);       // аддитивный
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);

        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return m;
    }
}
