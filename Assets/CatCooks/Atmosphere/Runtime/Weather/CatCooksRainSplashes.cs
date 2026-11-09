using UnityEngine;

// ============================================================================
//  CatCooksRainSplashes — частицы удара капель вокруг игрока.
//
//  ПОЧЕМУ НА ВСТРОЕННОЙ СИСТЕМЕ ЧАСТИЦ
//   Прошлые всплески держали свой пул из сотен GameObject с MeshRenderer,
//   своим мешем, своим шейдером и своим отсечением. Слишком много мест,
//   где всё это молча не срабатывало: незаполненный материал, режим
//   шейдера, кольцо в десять экранных пикселей, границы меша нулевой
//   высоты, старое сериализованное значение в компоненте. Отлаживать
//   такую конструкцию по скриншотам — угадайка, чем мы и занимались.
//
//   Встроенная система частиц закрывает это разом: отрисовка, отсечение,
//   сортировка и пулинг — забота движка. На нас остаётся ровно одно —
//   КУДА поставить частицу.
//
//  НИ ОДНОГО АССЕТА. Материал и обе текстуры создаются в коде. Нечего
//  назначить неправильно, нечего потерять при переносе папки, нечему
//  отвязаться после переимпорта. Это главная причина выбора: прошлые
//  версии ломались именно на связях с ассетами, а не на математике.
//
//  ДВА СЛОЯ, потому что удар капли — это не одно кольцо:
//    • плоское расходящееся кольцо на поверхности;
//    • корона мелких брызг, взлетающих и падающих обратно.
//   Одно кольцо читается как круг на воде, одни брызги — как искры.
//
//  ПОЗИЦИЯ КАЖДОЙ ЧАСТИЦЫ задаётся вручную через Emit: только так она
//  ложится на РЕЛЬЕФ, а не на общую плоскость. Высоту берём у самого
//  дождя — он уже держит кэшированную сетку лучей вокруг игрока.
//
//  Сеть: чисто локально, у каждого клиента свои брызги. Синхронизировать
//  нечего — их не видно дальше десятка метров.
// ============================================================================
[RequireComponent(typeof(CatCooksRain))]
[DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Атмосфера/Rain Splashes")]
public class CatCooksRainSplashes : MonoBehaviour
{
    [Header("Плотность")]
    [Tooltip("Ударов в секунду при полном ливне. Триста по площади " +
             "радиусом пятнадцать метров — заметная рябь, но ещё не каша.")]
    [Range(0f, 2000f)] public float ratePerSecond = 320f;

    [Tooltip("Радиус пятна брызг. 0 = взять от радиуса дождя.")]
    public float radius = 0f;

    [Tooltip("Потолок живых частиц: защита от провала кадров, если " +
             "частоту и радиус выкрутят одновременно.")]
    [Range(50, 4000)] public int maxParticles = 900;

    [Header("Вид")]
    [Tooltip("Диаметр кольца, м. Полметра видно с роста человека; " +
             "двадцать сантиметров теряются в траве — на этом и " +
             "погорела прошлая версия.")]
    public float ringSize = 0.55f;

    [Tooltip("Сколько живёт кольцо, секунд.")]
    public float ringLife = 0.42f;

    [Tooltip("Брызг на один удар. Ноль — только кольца.")]
    [Range(0, 8)] public int dropletsPerHit = 3;

    [Tooltip("Как высоко подлетают брызги, м.")]
    public float dropletHeight = 0.35f;

    public Color tint = new Color(0.85f, 0.92f, 1f, 0.85f);

    [Header("Отладка")]
    [Tooltip("Раз в секунду печатать, сколько ударов поставлено и куда.")]
    public bool logSpawns = false;

    // ───────── внутреннее ─────────
    private CatCooksRain _rain;
    private ParticleSystem _rings;
    private ParticleSystem _drops;
    private float _accum, _logTimer;
    private int _logCount;

    void OnEnable()
    {
        _rain = GetComponent<CatCooksRain>();
        Build();
    }

    void OnDisable() { Cleanup(); }

    float R { get { return radius > 0.1f ? radius : (_rain != null ? _rain.AreaRadius : 15f); } }

    // ============================================================ сборка
    void Build()
    {
        Cleanup();
        _rings = MakeSystem("RainSplashRings", true);
        _drops = MakeSystem("RainSplashDrops", false);
    }

    ParticleSystem MakeSystem(string name, bool isRing)
    {
        var go = new GameObject(name);
        go.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
        go.transform.SetParent(transform, false);

        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        // МИРОВОЕ ПРОСТРАНСТВО обязательно: объект дождя едет за игроком,
        // и в локальном уже поставленные брызги тащились бы за ним по земле.
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        main.maxParticles = maxParticles;
        main.startLifetime = isRing ? ringLife : ringLife * 1.4f;
        main.startSpeed = 0f;
        main.startSize = isRing ? ringSize : ringSize * 0.16f;
        main.startColor = tint;
        main.gravityModifier = isRing ? 0f : 1.6f;

        var emission = ps.emission;
        emission.enabled = false;      // частицы ставим вручную через Emit

        var shape = ps.shape;
        shape.enabled = false;         // позиция приходит в EmitParams

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = isRing
            // Кольцо РАСХОДИТСЯ.
            ? new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                  new Keyframe(0f, 0.25f), new Keyframe(1f, 1f)))
            // Брызги СЖИМАЮТСЯ: капля в полёте дробится и тает.
            : new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                  new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.9f, 0.25f),
                    new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var r = ps.GetComponent<ParticleSystemRenderer>();
        // Кольцо лежит НА ЗЕМЛЕ, брызги смотрят на камеру. Плоское кольцо,
        // развёрнутое к глазам, выглядит парящим обручем.
        r.renderMode = isRing
            ? ParticleSystemRenderMode.HorizontalBillboard
            : ParticleSystemRenderMode.Billboard;
        r.material = MakeMaterial(isRing);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sortingFudge = isRing ? 10f : 0f;

        ps.Play();
        return ps;
    }

    Material MakeMaterial(bool isRing)
    {
        // СВОЙ ШЕЙДЕР, а не встроенный URP-овский.
        //
        // У «Universal Render Pipeline/Particles/Unlit» прозрачность
        // включается ключевыми словами и состоянием прохода, а не полями
        // материала. Выставить _Surface = 1 из кода мало: смешивание,
        // запись глубины и очередь остаются непрозрачными, альфа текстуры
        // игнорируется — и вместо колец получаются БЕЛЫЕ КВАДРАТЫ.
        // Именно это и было на экране.
        Shader sh = Shader.Find("CatCooks/Splash Particle");
        if (sh == null)
        {
            // Sprites/Default прозрачен по умолчанию и умножает на цвет
            // вершины — годится как запасной путь без настройки.
            sh = Shader.Find("Sprites/Default");
            Debug.LogWarning("[RainSplashes] Шейдер «CatCooks/Splash Particle» " +
                             "не найден, беру Sprites/Default. Положи " +
                             "CatCooks_SplashParticle.shader в проект.", this);
        }

        var m = new Material(sh);
        m.hideFlags = HideFlags.DontSave;
        var tex = isRing ? RingTexture() : DotTexture();
        m.mainTexture = tex;
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        if (m.HasProperty("_Tint")) m.SetColor("_Tint", Color.white);
        if (m.HasProperty("_EdgeGlow")) m.SetFloat("_EdgeGlow", isRing ? 0.8f : 0.35f);
        m.renderQueue = 3000;
        return m;
    }

    static Texture2D _ringTex, _dotTex;

    /// <summary>Кольцо: тонкая полоса у края круга.</summary>
    static Texture2D RingTexture()
    {
        if (_ringTex != null) return _ringTex;
        const int N = 64;
        _ringTex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        _ringTex.name = "SplashRing";
        _ringTex.hideFlags = HideFlags.DontSave;
        _ringTex.wrapMode = TextureWrapMode.Clamp;

        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float dx = (x + 0.5f) / N - 0.5f, dy = (y + 0.5f) / N - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;

            // ВОДЯНОЕ КОЛЬЦО, А НЕ ОБРУЧ. Настоящий всплеск — это не
            // контур, а тонкая плёнка: внутри она почти прозрачна, к
            // кромке собирается валиком, за кромкой обрывается резко.
            // Один только контур выглядит нарисованным кругом.
            float rim = Mathf.Clamp01(1f - Mathf.Abs(r - 0.8f) / 0.2f);
            rim = rim * rim;
            float film = Mathf.Clamp01(1f - r / 0.85f) * 0.22f;
            float a = Mathf.Clamp01(rim + film);

            // Обрыв снаружи: без него круг размазывается в пятно.
            a *= 1f - Mathf.Clamp01((r - 0.98f) * 8f);
            px[x + y * N] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        _ringTex.SetPixels32(px);
        _ringTex.Apply(false, false);
        return _ringTex;
    }

    /// <summary>Брызга: мягкая точка.</summary>
    static Texture2D DotTexture()
    {
        if (_dotTex != null) return _dotTex;
        const int N = 32;
        _dotTex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        _dotTex.name = "SplashDrop";
        _dotTex.hideFlags = HideFlags.DontSave;
        _dotTex.wrapMode = TextureWrapMode.Clamp;

        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float dx = (x + 0.5f) / N - 0.5f, dy = (y + 0.5f) / N - 0.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
            float a = Mathf.Clamp01(1f - r);
            px[x + y * N] = new Color32(255, 255, 255, (byte)(a * a * 255f));
        }
        _dotTex.SetPixels32(px);
        _dotTex.Apply(false, false);
        return _dotTex;
    }

    void Cleanup()
    {
        DestroyPS(ref _rings);
        DestroyPS(ref _drops);
    }

    void DestroyPS(ref ParticleSystem ps)
    {
        if (ps == null) return;
        var go = ps.gameObject;
        if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        ps = null;
    }

    // ============================================================= кадр
    void LateUpdate()
    {
        if (!Application.isPlaying) return;
        if (_rain == null) _rain = GetComponent<CatCooksRain>();
        if (_rain == null || _rings == null) return;

        float intensity = _rain.CurrentIntensity;
        if (intensity <= 0.01f) { _accum = 0f; return; }

        // Квадрат: при мороси одиночные удары, при ливне сплошная рябь.
        // Линейная связь даёт слишком много брызг на слабом дожде.
        float rate = ratePerSecond * intensity * intensity;
        _accum += rate * Time.deltaTime;

        int count = Mathf.FloorToInt(_accum);
        _accum -= count;
        // Потолок на кадр: при просадке deltaTime подскакивает, и без
        // ограничения можно выплюнуть тысячу частиц разом, углубив провал.
        count = Mathf.Min(count, 60);

        Vector3 centre = _rain.Centre;
        Vector3 land = _rain.SlantOffset;   // косой дождь падает в стороне
        for (int i = 0; i < count; i++) SpawnHit(centre, land);

        if (!logSpawns) return;
        _logCount += count;
        _logTimer += Time.deltaTime;
        if (_logTimer >= 1f)
        {
            Debug.Log("[RainSplashes] ударов за секунду: " + _logCount
                    + ", центр " + centre + ", радиус " + R.ToString("0.0")
                    + ", живых колец " + _rings.particleCount, this);
            _logCount = 0; _logTimer = 0f;
        }
    }

    void SpawnHit(Vector3 centre, Vector3 land)
    {
        // Равномерно ПО ПЛОЩАДИ: без корня удары кучкуются у центра.
        float ang = Random.value * Mathf.PI * 2f;
        float rad = Mathf.Sqrt(Random.value) * R;

        float wx = centre.x + Mathf.Cos(ang) * rad + land.x;
        float wz = centre.z + Mathf.Sin(ang) * rad + land.z;
        float wy = _rain.GroundAt(wx, wz) + 0.02f;   // чуть над поверхностью

        var p = new ParticleSystem.EmitParams();
        p.position = new Vector3(wx, wy, wz);
        p.velocity = Vector3.zero;
        p.startSize = ringSize * Random.Range(0.6f, 1.45f);
        p.startLifetime = ringLife * Random.Range(0.8f, 1.2f);
        p.startColor = tint;
        p.applyShapeToPosition = false;
        _rings.Emit(p, 1);

        if (dropletsPerHit <= 0 || _drops == null) return;

        // Скорость подобрана так, чтобы при gravityModifier 1.6 капля
        // поднялась примерно на dropletHeight и успела упасть за свою
        // жизнь: v = sqrt(2 * g * modifier * h).
        float up = Mathf.Sqrt(2f * 9.81f * 1.6f * Mathf.Max(dropletHeight, 0.01f));

        for (int d = 0; d < dropletsPerHit; d++)
        {
            float da = Random.value * Mathf.PI * 2f;
            float side = Random.Range(0.4f, 1.3f);

            var dp = new ParticleSystem.EmitParams();
            dp.position = new Vector3(wx, wy + 0.02f, wz);
            dp.velocity = new Vector3(Mathf.Cos(da) * side,
                                      up * Random.Range(0.7f, 1.15f),
                                      Mathf.Sin(da) * side);
            dp.startSize = ringSize * Random.Range(0.08f, 0.2f);
            dp.startLifetime = ringLife * Random.Range(1.0f, 1.6f);
            dp.startColor = tint;
            dp.applyShapeToPosition = false;
            _drops.Emit(dp, 1);
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Пересобрать")]
    void RebuildMenu() { Build(); }

    [ContextMenu("Ударить сто раз сейчас")]
    void TestBurst()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[RainSplashes] Частицы живут доли секунды — "
                           + "проверяй в Play Mode.", this);
            return;
        }
        if (_rain == null) _rain = GetComponent<CatCooksRain>();
        if (_rings == null) Build();
        for (int i = 0; i < 100; i++) SpawnHit(_rain.Centre, _rain.SlantOffset);
        Debug.Log("[RainSplashes] Поставлено 100 ударов вокруг " + _rain.Centre
                + ". Если их не видно — дело не в спавне.", this);
    }
#endif
}
