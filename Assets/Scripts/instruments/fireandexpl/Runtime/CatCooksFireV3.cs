using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways, DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Effects/Zelda Style Fire V3")]
public sealed class CatCooksFireV3 : MonoBehaviour
{
    public enum Layout { Ellipse, Rectangle, Manual }

    /// <summary>Уровень детализации геометрии частиц и пламени.</summary>
    public enum Detail { Low, Medium, High }
    [Serializable] public sealed class FireSource
    {
        public Transform anchor;
        [Tooltip("Radii in this fire root's local metres; anchor scale is ignored.")]
        public Vector2 radius = new Vector2(.35f, .35f);
        [Range(.2f, 2f)] public float heightMultiplier = 1;
    }

    [Header("Shaders — assign all three assets for player builds")]
    public Shader bodyShader;
    public Shader particlesShader;
    public Shader heatShader;

    [Header("Footprint — local metres; uniform root scale scales the whole effect")]
    public Layout layout = Layout.Ellipse;
    public Vector2 footprint = new Vector2(2, 2);
    [Range(.12f, 2)] public float sourceSpacing = .55f;
    [Range(1, 128)] public int maxSources = 64;
    public bool automaticHeight = true;
    [Range(.2f, 1.5f)] public float heightToWidth = .7f;
    [Range(.15f, 12)] public float flameHeight = 1.4f;
    [Range(0, .4f)] public float sinkIntoLogs = .08f;
    public int seed = 137;
    public FireSource[] manualSources = Array.Empty<FireSource>();

    [Header("Shape and animation")]
    [Range(0, 1)] public float intensity = 1;
    [Min(.05f)] public float transitionSeconds = .65f;
    [Range(.1f, 3)] public float flowSpeed = 1;
    [Range(0, 1)] public float sway = .55f;
    [Range(0, 1)] public float erosion = .55f;
    [Range(0, 1)] public float colourSoftness = .22f;
    [Range(.3f, 2)] public float emission = 1.15f;
    [Tooltip("World direction and speed. Converted once to the fire's local axes.")]
    public Vector3 wind = new Vector3(.15f, 0, .04f);
    [Range(0, 1)] public float windResponse = .6f;

    [Header("Препятствие — пламя огибает кастрюлю (задаёт костёр через SetObstacle)")]
    [Tooltip("Насколько языки пламени расходятся в стороны вокруг дна, доля радиуса препятствия")]
    [Range(0, 1.5f)] public float obstacleSpread = .35f;
    [Tooltip("Насколько высоко языки облизывают бока, доля высоты препятствия")]
    [Range(0, 1)] public float obstacleLick = .55f;

    [Header("Palette — large warm areas, small pale accents")]
    public Color outer = new Color(.95f, .12f, .015f);
    public Color middle = new Color(1, .37f, .035f);
    public Color inner = new Color(1, .78f, .16f);
    public Color core = new Color(1, .94f, .51f);

    [Header("Secondary effects — rates per source, with a shared budget")]
    [Range(0, 4)] public float fragmentsPerSource = .35f;
    [Range(0, 12)] public float sparksPerSource = 1.8f;
    [Range(0, 3)] public float smokePerSource = .28f;
    [Range(32, 1024)] public int maxParticles = 160;
    [Range(0, .7f)] public float smokeOpacity = .26f;
    public Color smokeColour = new Color(.27f, .25f, .29f);
    public bool glowingCoals = true;

    [Header("Производительность")]
    [Tooltip("ЧАСТОТА ПЕРЕСБОРКИ МЕШЕЙ, кадров в секунду.\n\n" +
             "Сама симуляция от этого не грубеет — она идёт шагами по 1/60 " +
             "независимо. Меняется только то, как часто результат заливается " +
             "в меш, а заливка и есть дорогая часть.\n\n" +
             "30 неотличимо от 60: частицы мелкие, быстрые и размазанные, " +
             "глазу не за что зацепиться. Ниже 20 начинает читаться " +
             "ступенчатость у крупных клубов дыма.")]
    [Range(10, 60)] public int meshUpdatesPerSecond = 30;

    [Tooltip("ДЕТАЛИЗАЦИЯ КЛУБОВ И ОСКОЛКОВ.\n\n" +
             "Это ЧИСЛО ВЕРШИН на каждую частицу, а частиц сотни. Один шаг " +
             "вниз убирает примерно половину всей работы процессора над " +
             "эффектом.\n\n" +
             "Дымный шарик размером с ладонь из семидесяти вершин — это " +
             "вдвое больше, чем видно.")]
    public Detail particleDetail = Detail.Medium;

    [Tooltip("Детализация самого пламени. Оно крупное и близко, ему " +
             "детализация нужнее: пересобирается тело редко, только при " +
             "смене раскладки.")]
    public Detail flameDetail = Detail.High;

    [Header("Light — a single bounded light per fire")]
    public bool createLight = true;
    [Range(0, 5)] public float lightIntensity = 1.8f;

    [Header("Optional local heat haze — URP Opaque + Depth textures required")]
    public bool enableHeatHaze;
    [Range(0, 2)] public float heatPixels = .7f;
    [Range(0, .25f)] public float heatOpacity = .12f;

    public int SourceCount => sources.Count;
    public int LiveParticleCount => liveCount;
    public float CurrentIntensity => currentIntensity;
    public bool IsBuilt => generated != null;
    public bool IsPreviewing => previewing;
    public float Height => automaticHeight
        ? Mathf.Clamp(Mathf.Sqrt(Mathf.Max(.01f, footprint.x * footprint.y)) * heightToWidth, .15f, 12)
        : Mathf.Clamp(flameHeight, .15f, 12);

    const string GeneratedName = "CCF3 generated (owned)";
    readonly List<CatCooksFireGeometry.Source> sources = new List<CatCooksFireGeometry.Source>();
    readonly List<Material> materials = new List<Material>();
    Transform generated;
    MeshRenderer bodyRenderer, fragmentsRenderer, sparksRenderer, smokeRenderer, heatRenderer;
    CatCooksFireGeometry.Buffer body, fragments, sparks, smoke, heat;
    CatCooksFireGeometry.Template flameTemplate, fragmentTemplate, puffTemplate, sparkTemplate;
    Material bodyMat, sparkMat, smokeMat, heatMat;
    Light fireLight;
    bool previewing, dirty = true, failed;
    int layoutHash, liveCount;

    // ═══════════════ ПРОИЗВОДИТЕЛЬНОСТЬ ═══════════════

    /// <summary>Готовая коробка под все меши эффекта. Считается при сборке
    /// и заменяет собой пересчёт границ по вершинам на каждом кадре.</summary>
    /// <summary>Шаг между пересборками мешей, секунды.</summary>
    float meshInterval => 1f / Mathf.Max(1, meshUpdatesPerSecond);

    Bounds effectBounds = new Bounds(Vector3.zero, Vector3.one);
    Bounds smokeBounds = new Bounds(Vector3.zero, Vector3.one);

    /// <summary>В каком кадре дым уже собран. Без этого он пересобирается на
    /// каждую камеру: в редакторе с открытым Scene это трижды за кадр, и
    /// каждый раз с сортировкой по глубине.</summary>
    int smokeFrame = -1;

    /// <summary>Накопитель для ограничения частоты пересборки мешей.</summary>
    float meshCarry;

    /// <summary>Виден ли эффект. Считает Unity по границам рендерера.</summary>
    bool visible = true;

    /// <summary>Сколько кадров эффект уже не видно. Симуляция гасится не
    /// сразу: пара кадров запаса спасает от мигания на границе кадра, когда
    /// костёр то попадает в него, то нет.</summary>
    int hiddenFrames;
    readonly int[] sourceCursors = new int[3];
    Bounds visibleBounds;
    float currentIntensity, flowTime, fragmentCarry, sparkCarry, smokeCarry, gust;
    Vector3 gustDirection;
    uint rng;
    struct Mote
    {
        public Vector3 position, velocity;
        public float age, life, size, phase;
        public byte kind; // 0 = fragment, 1 = spark, 2 = smoke
    }
    Mote[] motes;
    int[] smokeOrder;
    float[] smokeDepth;
#if UNITY_EDITOR
    double editorTime;
#endif

    void Reset()
    {
        bodyShader = Shader.Find("CatCooks/Fire V3/Body");
        particlesShader = Shader.Find("CatCooks/Fire V3/Particles");
        heatShader = Shader.Find("CatCooks/Fire V3/Heat");
    }
    void OnEnable()
    {
        dirty = true; failed = false;
        RenderPipelineManager.beginCameraRendering += BeforeCamera;
        if (Application.IsPlaying(gameObject)) { currentIntensity = Mathf.Clamp01(intensity); Rebuild(); }
    }
    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        StopPreview();
    }
    void OnDestroy() => Dispose();
    void OnValidate()
    {
        footprint.x = Safe(footprint.x, .1f, 32, 2);
        footprint.y = Safe(footprint.y, .1f, 32, 2);
        sourceSpacing = Safe(sourceSpacing, .12f, 2, .55f);
        flameHeight = Safe(flameHeight, .15f, 12, 1.4f);
        heightToWidth = Safe(heightToWidth, .2f, 1.5f, .7f);
        maxSources = Mathf.Clamp(maxSources, 1, 128); maxParticles = Mathf.Clamp(maxParticles, 32, 1024);
        intensity = Safe(intensity, 0, 1, 1); transitionSeconds = Safe(transitionSeconds, .05f, 30, .65f);
        dirty = true; failed = false; // No scene mutations from OnValidate.
    }
    static float Safe(float x, float min, float max, float fallback)
        => float.IsNaN(x) || float.IsInfinity(x) ? fallback : Mathf.Clamp(x, min, max);
    static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
        || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    Vector3 SourcePosition(Transform anchor)
        => anchor.parent == transform ? anchor.localPosition : transform.InverseTransformPoint(anchor.position);
    static int PositionHash(Vector3 p)
    {
        // Millimetre precision avoids rebuilding from inverse-matrix round-off.
        unchecked { return (Mathf.RoundToInt(p.x * 1000) * 397 ^ Mathf.RoundToInt(p.y * 1000))
            * 397 ^ Mathf.RoundToInt(p.z * 1000); }
    }

    /// <summary>
    /// НЕВИДИМЫЙ КОСТЁР НЕ СИМУЛИРУЕТСЯ.
    ///
    /// Раньше он жил всегда: за спиной, за стеной, в двухстах метрах — всё
    /// равно каждый кадр пересобирались меши на пятнадцать тысяч вершин.
    /// В сцене с несколькими кострами это и было основным расходом.
    ///
    /// Пара кадров запаса нужна против мигания на самом краю кадра: там
    /// видимость дёргается туда-сюда, и мгновенное отключение дало бы рывки
    /// частиц каждый раз, когда игрок ведёт камерой мимо.
    /// </summary>
    void OnBecameVisible()   { visible = true; hiddenFrames = 0; }
    void OnBecameInvisible() { visible = false; }

    void Update()
    {
        if (!Application.IsPlaying(gameObject)) return;

        if (!visible)
        {
            // Пару кадров ещё досчитываем, дальше замираем.
            if (++hiddenFrames > 2) return;
        }
        else hiddenFrames = 0;

        Advance(Time.deltaTime);
    }
    public void SetIntensity(float value, float seconds = .65f)
    { intensity = Safe(value, 0, 1, 0); transitionSeconds = Safe(seconds, .05f, 30, .65f); }
    public void Ignite(float seconds = .65f) => SetIntensity(1, seconds);
    public void Extinguish(float seconds = 1) => SetIntensity(0, seconds);
    public void SetFootprint(float width, float depth)
    {
        footprint = new Vector2(Safe(width, .1f, 32, 2), Safe(depth, .1f, 32, 2));
        dirty = true; failed = false;
    }
    public void SetHeight(float metres)
    { automaticHeight = false; flameHeight = Safe(metres, .15f, 12, 1.4f); dirty = true; }
    public void SetWind(Vector3 worldWind) => wind = Finite(worldWind) ? worldWind : Vector3.zero;

    // ───────── препятствие: кастрюля над огнём ─────────
    bool hasObstacle;
    Vector3 obstacleCentreWS;
    float obstacleRadius, obstacleBottomWS, obstacleHeight;

    /// <summary>
    /// Над огнём стоит кастрюля: цилиндр с центром дна bottomCentreWS, радиусом
    /// radius и высотой height (мир). Языки пламени упираются в дно, расходятся
    /// и облизывают бока. Вызывать каждый кадр или при смене; ClearObstacle — убрать.
    /// </summary>
    public void SetObstacle(Vector3 bottomCentreWS, float radius, float height)
    {
        if (!Finite(bottomCentreWS) || radius <= 0f) { hasObstacle = false; return; }
        hasObstacle = true;
        obstacleCentreWS = bottomCentreWS;
        obstacleRadius = radius;
        obstacleBottomWS = bottomCentreWS.y;
        obstacleHeight = Mathf.Max(0.01f, height);
    }
    public void ClearObstacle() => hasObstacle = false;
    public void Blow(Vector3 worldDirection, float strength = 1)
    {
        if (!IsBuilt || currentIntensity < .01f || !Finite(worldDirection)) return;
        gustDirection = transform.InverseTransformDirection(worldDirection).normalized;
        gust = Mathf.Clamp(strength, 0, 2); BurstSparks(Mathf.RoundToInt(12 * gust));
    }
    public void BurstSparks(int count = 20) => Burst(1, Mathf.Clamp(count, 0, 200));
    public void PuffSmoke(int count = 8) => Burst(2, Mathf.Clamp(count, 0, 64));
    void Burst(byte kind, int count)
    {
        if (!IsBuilt || sources.Count == 0) return;
        for (int i = 0; i < count; i++) Spawn(kind);
        RebuildMovingMeshes();
    }

    int GetLayoutHash()
    {
        unchecked
        {
            int h = footprint.GetHashCode();
            h = h * 31 + (int)layout; h = h * 31 + sourceSpacing.GetHashCode();
            h = h * 31 + maxSources; h = h * 31 + Height.GetHashCode();
            h = h * 31 + sinkIntoLogs.GetHashCode(); h = h * 31 + seed;
            h = h * 31 + maxParticles; h = h * 31 + glowingCoals.GetHashCode();

            // Детализация меняет ШАБЛОНЫ, то есть число вершин и индексов.
            // Не учтёшь её здесь — смена уровня не перестроит меши, и в
            // редакторе ползунок будет выглядеть сломанным.
            h = h * 31 + (int)particleDetail; h = h * 31 + (int)flameDetail;
            h = h * 31 + (bodyShader != null ? bodyShader.GetInstanceID() : 0);
            h = h * 31 + (particlesShader != null ? particlesShader.GetInstanceID() : 0);
            h = h * 31 + (heatShader != null ? heatShader.GetInstanceID() : 0);
            if (layout == Layout.Manual && manualSources != null)
                foreach (var s in manualSources)
                {
                    if (s == null || s.anchor == null) continue;
                    h = h * 31 + PositionHash(SourcePosition(s.anchor));
                    h = h * 31 + s.radius.GetHashCode(); h = h * 31 + s.heightMultiplier.GetHashCode();
                }
            return h;
        }
    }

    public void Rebuild()
    {
        if (!isActiveAndEnabled) return;
        if (bodyShader == null) bodyShader = Shader.Find("CatCooks/Fire V3/Body");
        if (particlesShader == null) particlesShader = Shader.Find("CatCooks/Fire V3/Particles");
        if (heatShader == null) heatShader = Shader.Find("CatCooks/Fire V3/Heat");
        if (bodyShader == null || particlesShader == null || !bodyShader.isSupported || !particlesShader.isSupported)
        {
            if (!failed) Debug.LogError("CatCooks Fire V3: assign the supplied Body and Particles shaders; URP is required.", this);
            failed = true; return;
        }
        Dispose();
        failed = false; dirty = false;
        rng = unchecked((uint)seed) ^ 0xa3c59ac3u; if (rng == 0) rng = 1;
        Array.Clear(sourceCursors, 0, sourceCursors.Length); fragmentCarry = sparkCarry = smokeCarry = 0;
        if (layout == Layout.Manual && manualSources != null)
        {
            foreach (var s in manualSources)
            {
                if (s == null || s.anchor == null || sources.Count >= maxSources) continue;
                Vector3 p = SourcePosition(s.anchor);
                if (!Finite(p)) continue;
                sources.Add(new CatCooksFireGeometry.Source { position = p,
                    radius = new Vector2(Safe(s.radius.x, .03f, 16, .35f), Safe(s.radius.y, .03f, 16, .35f)),
                    height = Height * Safe(s.heightMultiplier, .2f, 2, 1),
                    phase = CatCooksFireGeometry.Hash(seed + sources.Count * 137) * 60 });
            }
        }
        else CatCooksFireGeometry.Grid(footprint, sourceSpacing, maxSources, layout == Layout.Ellipse, Height, seed, sources);
        if (sources.Count == 0) { failed = true; Debug.LogWarning("Fire V3: Manual layout needs at least one source anchor.", this); return; }
        generated = NewObject(GeneratedName, transform).transform;
        bodyMat = NewMaterial(bodyShader, "Body");
        sparkMat = NewMaterial(particlesShader, "Sparks"); sparkMat.SetFloat("_Kind", 0);
        smokeMat = NewMaterial(particlesShader, "Smoke"); smokeMat.SetFloat("_Kind", 1);
        smokeMat.renderQueue = 3010; sparkMat.renderQueue = 3020;
        // ── РАЗРЕШЕНИЕ ШАБЛОНОВ ──
        //
        // Вершины здесь умножаются на число частиц, а те пересобираются
        // каждый кадр. Шаг вниз по этим числам экономит больше, чем любая
        // правка в шейдере: шейдер работает с готовым мешем, а эти вершины
        // сперва надо построить на процессоре.
        //
        // Пламени детализация нужнее — оно крупное, близко и пересобирается
        // редко, только при смене раскладки.
        int fr, fs, pr, ps;

        switch (flameDetail)
        {
            case Detail.Low:  fr = 8;  fs = 8;  break;
            case Detail.High: fr = 16; fs = 16; break;
            default:          fr = 12; fs = 12; break;
        }

        switch (particleDetail)
        {
            case Detail.Low:  pr = 3; ps = 5; break;
            case Detail.High: pr = 6; ps = 9; break;
            default:          pr = 4; ps = 6; break;
        }

        flameTemplate = CatCooksFireGeometry.MakeTemplate(fr, fs, true);
        fragmentTemplate = CatCooksFireGeometry.MakeTemplate(pr + 1, ps + 1, true);
        puffTemplate = CatCooksFireGeometry.MakeTemplate(pr, ps, false);
        sparkTemplate = CatCooksFireGeometry.MakeTemplate(2, 4, false);
        body = NewBuffer("Body", bodyMat, false, out bodyRenderer);
        fragments = NewBuffer("Fragments", bodyMat, true, out fragmentsRenderer);
        sparks = NewBuffer("Sparks", sparkMat, true, out sparksRenderer);
        smoke = NewBuffer("Smoke", smokeMat, true, out smokeRenderer);
        BuildBody();
        if (heatShader != null && heatShader.isSupported)
        {
            heatMat = NewMaterial(heatShader, "Heat");
            heat = NewBuffer("Heat", heatMat, false, out heatRenderer);
            Bounds b = visibleBounds;
            heat.Add(puffTemplate, new Vector3(b.center.x, b.min.y, b.center.z),
                new Vector3(Mathf.Max(.1f, b.extents.x), Mathf.Max(.2f, b.size.y), Mathf.Max(.1f, b.extents.z)),
                Quaternion.identity, Vector4.zero, Color.white);
            heat.Commit(effectBounds, 0);
        }
        fireLight = NewObject("Light", generated).AddComponent<Light>();
        fireLight.type = LightType.Point; fireLight.shadows = LightShadows.None;
        motes = new Mote[Mathf.Clamp(maxParticles, 32, 1024)];
        smokeOrder = new int[motes.Length]; smokeDepth = new float[motes.Length]; liveCount = 0;
        ComputeBounds();
        layoutHash = GetLayoutHash(); UpdateMaterials(); RebuildMovingMeshes();
    }

    /// <summary>
    /// ОДНА КОРОБКА НА ВЕСЬ ЭФФЕКТ, посчитанная при сборке.
    ///
    /// Раньше границы каждого меша пересчитывались по вершинам на каждой
    /// заливке — то есть лишний полный проход по пятнадцати тысячам вершин
    /// несколько раз за кадр, ради коробки, которая и так известна: костёр
    /// стоит на месте, его пятно и высота заданы полями.
    ///
    /// Запас по бокам и вверх берётся щедрый: частицы улетают по ветру, и
    /// коробка, обрезанная впритык, начнёт гасить эффект отсечением ровно
    /// тогда, когда искры разлетелись эффектнее всего.
    /// </summary>
    void ComputeBounds()
    {
        float w = Mathf.Max(footprint.x, footprint.y);
        float h = Height;

        // Вбок — на радиус пятна плюс разлёт, вверх — на три высоты пламени:
        // столько успевает пройти дым за свою жизнь.
        Vector3 size = new Vector3(w * 2.2f + h, h * 3.2f, w * 2.2f + h);
        effectBounds = new Bounds(new Vector3(0, size.y * .38f, 0), size);

        // Дым уходит выше и дальше всех, ему коробка отдельная и шире.
        smokeBounds = new Bounds(new Vector3(0, size.y * .6f, 0), size * 1.6f);
    }

    void BuildBody()
    {
        foreach (var s in sources)
        {
            float sink = Mathf.Min(sinkIntoLogs, s.height * .14f);
            Vector3 p = s.position - Vector3.up * sink;
            float r = Mathf.Min(s.radius.x, s.radius.y);
            Quaternion turn = Quaternion.Euler(0, s.phase * 21, 0);
            // Keep anisotropic footprint axes fixed: rotate the asymmetry only
            // for circular sources, otherwise a long strip would widen unexpectedly.
            if (Mathf.Abs(s.radius.x - s.radius.y) > .02f) turn = Quaternion.identity;
            body.Add(flameTemplate, p, new Vector3(s.radius.x, s.height, s.radius.y), turn,
                new Vector4(s.phase, r, s.height, 0), Color.white);
            for (int k = 0; k < 2; k++)
            {
                float sign = k == 0 ? -1 : 1;
                Vector3 off = new Vector3(sign * s.radius.x * .43f, s.height * .04f, sign * s.radius.y * .12f);
                float h = s.height * (k == 0 ? .64f : .52f);
                body.Add(flameTemplate, p + off, new Vector3(s.radius.x * .55f, h, s.radius.y * .55f),
                    Quaternion.identity, new Vector4(s.phase + 4 + k * 9, r * .55f, h, 0), Color.white);
            }
            if (glowingCoals)
                for (int k = 0; k < 3; k++)
                {
                    float a = (s.phase + k * 2.4f);
                    Vector3 off = new Vector3(Mathf.Cos(a) * s.radius.x * .6f, .012f, Mathf.Sin(a) * s.radius.y * .6f);
                    body.Add(puffTemplate, s.position + off, new Vector3(s.radius.x * .2f, r * .10f, s.radius.y * .2f),
                        Quaternion.identity, new Vector4(s.phase + k, r, .1f, 2), Color.white);
                }
        }
        float maxH = 0, maxR = 0;
        foreach (var s in sources) { maxH = Mathf.Max(maxH, s.height); maxR = Mathf.Max(maxR, s.radius.x, s.radius.y); }
        // Matches the bounded vertex displacement, independent of camera and time.
        body.Commit(effectBounds, 0);
        visibleBounds = body.mesh.bounds;
        Bounds expanded = visibleBounds;
        expanded.Expand((maxH * .4f + maxR * .55f) * 2);
        body.mesh.bounds = expanded;
    }

    static GameObject NewObject(string name, Transform parent)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave, layer = parent.gameObject.layer };
        go.transform.SetParent(parent, false); return go;
    }
    Material NewMaterial(Shader shader, string label)
    {
        var m = new Material(shader) { name = "CCF3/" + label, hideFlags = HideFlags.HideAndDontSave,
            enableInstancing = false };
        materials.Add(m); return m;
    }
    CatCooksFireGeometry.Buffer NewBuffer(string name, Material material, bool dynamic, out MeshRenderer renderer)
    {
        var go = NewObject(name, generated);
        var buffer = new CatCooksFireGeometry.Buffer(name, dynamic);
        go.AddComponent<MeshFilter>().sharedMesh = buffer.mesh;
        renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        return buffer;
    }

    void Advance(float delta)
    {
        if (failed && !dirty) return;
        if (dirty || !IsBuilt || layoutHash != GetLayoutHash())
        {
            // Colour/animation edits do not reset particles or change the layout.
            if (!IsBuilt || layoutHash != GetLayoutHash()) Rebuild();
            dirty = false;
        }
        if (!IsBuilt || motes == null) return;
        // Погашенный костёр без живых частиц — ничего не считаем и не перестраиваем.
        // Холодное кострище в сцене не стоит ни одного кадра.
        if (currentIntensity <= 0f && intensity <= 0f && liveCount == 0 && gust <= 0f)
        {
            if (bodyRenderer != null && bodyRenderer.enabled) UpdateMaterials();
            return;
        }
        float remaining = Mathf.Clamp(delta, 0, .2f);
        while (remaining > .00001f)
        {
            float dt = Mathf.Min(remaining, 1f / 60f); remaining -= dt;
            currentIntensity = Mathf.MoveTowards(currentIntensity, Mathf.Clamp01(intensity), dt / Mathf.Max(.05f, transitionSeconds));
            flowTime += dt * Safe(flowSpeed, .1f, 3, 1) * (1 + gust * .3f);
            gust = Mathf.MoveTowards(gust, 0, dt * 1.7f);
            Vector3 drift = LocalWind();
            for (int i = liveCount - 1; i >= 0; i--)
            {
                Mote p = motes[i]; p.age += dt;
                if (p.age >= p.life) { motes[i] = motes[--liveCount]; continue; }
                float drag = p.kind == 1 ? .25f : .9f;
                p.velocity.x += (drift.x - p.velocity.x) * drag * dt;
                p.velocity.z += (drift.z - p.velocity.z) * drag * dt;
                if (p.kind == 1) p.velocity.y -= Height * .18f * dt;
                else
                {
                    p.velocity.x += Mathf.Sin(p.phase + p.age * 3) * p.size * dt;
                    p.velocity.z += Mathf.Cos(p.phase + p.age * 2.2f) * p.size * dt;
                }
                p.position += p.velocity * dt; motes[i] = p;
            }
            EmitRate(ref fragmentCarry, fragmentsPerSource, 0, dt);
            EmitRate(ref sparkCarry, sparksPerSource * (1 + gust), 1, dt);
            EmitRate(ref smokeCarry, smokePerSource, 2, dt);
        }
        UpdateMaterials();

        // ЧАСТОТА ПЕРЕСБОРКИ МЕШЕЙ ОТВЯЗАНА ОТ ЧАСТОТЫ КАДРОВ.
        //
        // Сама симуляция идёт шагами по 1/60 и остаётся точной — меняется
        // только то, как часто результат заливается в меш. На тридцати
        // герцах частицы неотличимы от шестидесяти: они мелкие, быстрые и
        // размазанные, глазу не за что зацепиться.
        //
        // А заливка — это ровно то, что стоит дорого.
        meshCarry += Mathf.Clamp(delta, 0, .2f);

        if (meshCarry >= meshInterval)
        {
            meshCarry = 0f;
            RebuildMovingMeshes();
        }
    }
    Vector3 LocalWind()
    {
        Vector3 w = Finite(wind) ? transform.InverseTransformDirection(wind) * windResponse : Vector3.zero;
        w.y = 0; return Vector3.ClampMagnitude(w + gustDirection * gust, 3);
    }
    float Random01()
    { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xffffffu) / 16777216f; }
    void EmitRate(ref float carry, float rate, byte kind, float dt)
    {
        carry += Mathf.Clamp(rate * sources.Count, 0, 120) * currentIntensity * dt;
        int n = Mathf.Min(24, Mathf.FloorToInt(carry)); carry -= n;
        for (int i = 0; i < n; i++) Spawn(kind);
    }
    void Spawn(byte kind)
    {
        if (motes == null || liveCount >= motes.Length || sources.Count == 0) return;
        int cursor = sourceCursors[kind];
        var s = sources[cursor];
        sourceCursors[kind] = (cursor + 1) % sources.Count;
        float a = Random01() * Mathf.PI * 2, r = Mathf.Sqrt(Random01()) * .66f;
        Vector3 p = s.position + new Vector3(Mathf.Cos(a) * s.radius.x * r, 0, Mathf.Sin(a) * s.radius.y * r);
        float u = Random01(), size, life, up;
        if (kind == 0)
        { p.y += s.height * (.48f + .28f * u); size = Mathf.Min(s.radius.x, s.radius.y) * (.2f + .17f * Random01()); life = .45f + Random01() * .55f; up = s.height * .55f; }
        else if (kind == 1)
        { p.y += s.height * .12f; size = Mathf.Clamp(s.height * (.005f + .006f * u), .002f, .06f); life = .6f + Random01() * 1.3f; up = s.height * (.85f + Random01() * .8f); }
        else
        { p.y += s.height * .7f; size = Mathf.Min(s.radius.x, s.radius.y) * (.65f + .45f * u); life = 1.8f + Random01() * 1.4f; up = s.height * .34f; }
        Vector3 v = new Vector3(Mathf.Cos(a) * s.height * .14f, up, Mathf.Sin(a) * s.height * .14f) + LocalWind() * .2f;
        motes[liveCount++] = new Mote { position = p, velocity = v, size = size, life = life,
            phase = Random01() * 60, kind = kind };
    }
    void UpdateMaterials()
    {
        Vector3 w = Vector3.ClampMagnitude(LocalWind() * .12f, .32f);
        bodyMat.SetFloat("_FlowTime", flowTime); bodyMat.SetFloat("_Sway", Mathf.Clamp01(sway));
        bodyMat.SetFloat("_Erosion", Mathf.Clamp01(erosion)); bodyMat.SetFloat("_Softness", Mathf.Clamp01(colourSoftness));
        bodyMat.SetFloat("_Intensity", currentIntensity); bodyMat.SetFloat("_Emission", Mathf.Clamp(emission, .3f, 2));
        bodyMat.SetColor("_Outer", outer); bodyMat.SetColor("_Middle", middle);
        bodyMat.SetColor("_Inner", inner); bodyMat.SetColor("_Core", core);
        bodyMat.SetVector("_Wind", new Vector4(w.x, 0, w.z, 0));
        if (hasObstacle)
        {
            // В оси огня: шейдер деформирует вершины в object space.
            Vector3 c = transform.InverseTransformPoint(obstacleCentreWS);
            float s = Mathf.Max(1e-4f, transform.lossyScale.x);
            bodyMat.SetVector("_Obstacle", new Vector4(c.x, c.y, c.z, obstacleRadius / s));
            bodyMat.SetVector("_ObstacleShape", new Vector4(obstacleHeight / s, obstacleSpread, obstacleLick, 1));
        }
        else bodyMat.SetVector("_ObstacleShape", Vector4.zero);
        bodyRenderer.enabled = currentIntensity > .002f;
        // Fragments remain alive briefly after the source is extinguished.
        smokeMat.SetColor("_Tint", smokeColour); smokeMat.SetFloat("_Opacity", Mathf.Clamp(smokeOpacity, 0, .7f));
        sparkMat.SetFloat("_Emission", Mathf.Clamp(emission, .3f, 2));
        if (heatRenderer != null)
        {
            heatRenderer.enabled = enableHeatHaze && currentIntensity > .01f;
            heatMat.SetFloat("_FlowTime", flowTime);
            heatMat.SetFloat("_Pixels", Mathf.Clamp(heatPixels, 0, 2));
            heatMat.SetFloat("_Opacity", Mathf.Clamp(heatOpacity, 0, .25f) * currentIntensity);
        }
        Bounds b = visibleBounds;
        fireLight.enabled = createLight && currentIntensity > .01f;
        fireLight.transform.localPosition = new Vector3(b.center.x, b.min.y + b.size.y * .35f, b.center.z);
        fireLight.color = new Color(1, .49f, .14f);
        fireLight.intensity = Mathf.Clamp(lightIntensity, 0, 5) * currentIntensity * (1 + .04f * Mathf.Sin(flowTime * 4.1f));
        // Geometry inherits the transform once. Light range is explicitly scaled once.
        Vector3 scale = transform.lossyScale;
        float k = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        fireLight.range = Mathf.Max(.1f, b.size.magnitude * 1.1f * k);
    }
    static float Fade(Mote p)
        => Mathf.Clamp01(p.age / .09f) * Mathf.Clamp01((p.life - p.age) / (p.kind == 2 ? .9f : .25f));
    void RebuildMovingMeshes()
    {
        if (motes == null) return;
        fragments.Clear(); sparks.Clear();
        for (int i = 0; i < liveCount; i++)
        {
            var p = motes[i]; float age = p.age / p.life, fade = Fade(p);
            if (p.kind == 0)
            {
                float size = p.size * Mathf.Lerp(1, .35f, age);
                fragments.Add(fragmentTemplate, p.position, new Vector3(size, size * 2.3f, size),
                    Quaternion.Euler(0, p.phase * 18 + p.age * 55, 0),
                    new Vector4(p.phase, size, size * 2.3f, 1), new Color(1, 1, 1, fade));
            }
            else if (p.kind == 1)
            {
                Vector3 axis = p.velocity.sqrMagnitude > .001f ? p.velocity.normalized : Vector3.up;
                float len = Mathf.Clamp(p.velocity.magnitude * .035f, p.size * 2, p.size * 9);
                Color c = Color.Lerp(core, middle, age); c.a = fade;
                sparks.Add(sparkTemplate, p.position - axis * len * .5f, new Vector3(p.size, len, p.size),
                    Quaternion.FromToRotation(Vector3.up, axis), new Vector4(p.phase, 0, age, 0), c);
            }
        }
        fragments.Commit(effectBounds, Height * .15f); sparks.Commit(effectBounds, 0);
        fragmentsRenderer.enabled = fragments.mesh.vertexCount > 0; sparksRenderer.enabled = sparks.mesh.vertexCount > 0;
        // ЗДЕСЬ ДЫМ БОЛЬШЕ НЕ СОБИРАЕТСЯ.
        //
        // Раньше стояло RebuildSmoke(null) — и результат немедленно
        // перезатирался вызовом из BeforeCamera. То есть вся работа, включая
        // сортировку по глубине, делалась впустую.
        //
        // Дым сортируется от камеры, значит собирать его имеет смысл только
        // когда камера известна.
    }

    /// <summary>
    /// ДЫМ СОБИРАЕТСЯ ОДИН РАЗ ЗА КАДР.
    ///
    /// Событие приходит на КАЖДУЮ камеру. В редакторе их две — игровая и
    /// Scene, — а с отражениями и порталами бывает больше. Каждый вызов
    /// пересобирал меш и сортировал частицы по глубине заново.
    ///
    /// Сортировка от разных камер даёт разный порядок, но разница видна
    /// только если смотреть на один дым с двух сторон одновременно. Ради
    /// этого случая платить троекратной пересборкой в каждом кадре не стоит.
    /// </summary>
    void BeforeCamera(ScriptableRenderContext context, Camera camera)
    {
        if (!IsBuilt || camera == null) return;
        if (!Application.IsPlaying(gameObject) && !previewing) return;
        if (!visible && hiddenFrames > 2) return;

        if (smokeFrame == Time.frameCount) return;
        smokeFrame = Time.frameCount;

        RebuildSmoke(camera);
    }
    void RebuildSmoke(Camera camera)
    {
        smoke.Clear(); int n = 0;
        for (int i = 0; i < liveCount; i++) if (motes[i].kind == 2)
        {
            smokeOrder[n] = i;
            smokeDepth[n] = camera == null ? -motes[i].position.y :
                -Vector3.Dot(transform.TransformPoint(motes[i].position) - camera.transform.position, camera.transform.forward);
            n++;
        }
        if (n > 1) Array.Sort(smokeDepth, smokeOrder, 0, n);
        for (int j = 0; j < n; j++)
        {
            var p = motes[smokeOrder[j]]; float age = p.age / p.life;
            float size = p.size * (.5f + age * 1.15f);
            smoke.Add(puffTemplate, p.position - Vector3.up * size * .7f,
                new Vector3(size, size * 1.4f, size * .93f), Quaternion.Euler(0, p.phase * 20 + p.age * 15, 0),
                new Vector4(p.phase, 0, age, 0), new Color(1, 1, 1, Fade(p)));
        }
        smoke.Commit(smokeBounds, 0); smokeRenderer.enabled = n > 0 && smokeOpacity > 0;
    }

    public void StartPreview()
    {
#if UNITY_EDITOR
        if (Application.isPlaying || EditorUtility.IsPersistent(this) || !isActiveAndEnabled) return;
        currentIntensity = Mathf.Clamp01(intensity); Rebuild();
        if (!IsBuilt) return;
        previewing = true; editorTime = EditorApplication.timeSinceStartup;
        EditorApplication.update -= EditorAdvance; EditorApplication.update += EditorAdvance;
#endif
    }
    public void StopPreview()
    {
        previewing = false;
#if UNITY_EDITOR
        EditorApplication.update -= EditorAdvance;
#endif
        Dispose();
    }
#if UNITY_EDITOR
    void EditorAdvance()
    {
        if (this == null || !previewing) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) { StopPreview(); return; }
        double now = EditorApplication.timeSinceStartup;
        Advance((float)(now - editorTime)); editorTime = now;
        SceneView.RepaintAll();
    }
    void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1, .5f, .1f, .7f);
        if (layout == Layout.Rectangle)
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(footprint.x, 0, footprint.y));
        else if (layout == Layout.Ellipse)
            for (int i = 0; i < 48; i++)
            {
                float a = i * Mathf.PI * 2 / 48, b = (i + 1) * Mathf.PI * 2 / 48;
                Gizmos.DrawLine(new Vector3(Mathf.Cos(a) * footprint.x * .5f, 0, Mathf.Sin(a) * footprint.y * .5f),
                    new Vector3(Mathf.Cos(b) * footprint.x * .5f, 0, Mathf.Sin(b) * footprint.y * .5f));
            }
        foreach (var s in sources) Gizmos.DrawWireSphere(s.position, Mathf.Min(s.radius.x, s.radius.y) * .2f);
    }
#endif
    void Dispose()
    {
        // Also recover ownership after an editor domain reload. Never release
        // imported assets or a user's ordinary child objects.
        var owned = new HashSet<UnityEngine.Object>();
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name != GeneratedName || (child.gameObject.hideFlags & HideFlags.DontSave) == 0) continue;
            child.gameObject.SetActive(false);
            foreach (var f in child.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh != null && f.sharedMesh.name.StartsWith("CCF3/")) owned.Add(f.sharedMesh);
            foreach (var r in child.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.name.StartsWith("CCF3/") && (m.hideFlags & HideFlags.DontSave) != 0) owned.Add(m);
            Release(child.gameObject);
        }
        foreach (var m in materials) if (m != null) owned.Add(m);
        foreach (var obj in owned) Release(obj);
        materials.Clear(); sources.Clear(); generated = null; liveCount = 0; motes = null;
        body = fragments = sparks = smoke = heat = null;
        bodyMat = sparkMat = smokeMat = heatMat = null;
    }
    static void Release(UnityEngine.Object obj)
    { if (obj == null) return; if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
}
