using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(1000)]
[AddComponentMenu("CatCooks/Dust and smoke/World system")]
public sealed class CatCooksDustSystem : MonoBehaviour
{
    public enum Effect { Step, Run, Jump, Land, Impact, Wheel, Skid, Burst, Smoke, Exhaust }
    public Shader puffShader;
    public CatCooksDustProfile defaultProfile;
    /// <summary>Разрешение геометрии клуба.</summary>
    public enum PuffDetail { Low, Medium, High }

    [Range(64, 2048)] public int maxParticles = 256;
    [Range(16, 256)] public int maxSpawnsPerFrame = 128;

    [Header("Производительность")]
    [Tooltip("ЧАСТОТА ПЕРЕСБОРКИ МЕША, кадров в секунду.\n\n" +
             "Симуляция от этого не грубеет: она идёт шагами по 1/60 " +
             "независимо. Реже заливается только результат — а заливка " +
             "девяноста тысяч вершин и есть дорогая часть.\n\n" +
             "30 неотличимо от 60: клубы мягкие и медленные, глазу не за что " +
             "зацепиться.")]
    [Range(10, 60)] public int meshUpdatesPerSecond = 30;

    [Tooltip("ДЕТАЛИЗАЦИЯ КЛУБА. Это ЧИСЛО ВЕРШИН на каждую частицу, а " +
             "частиц сотни.\n\n" +
             "High — 187 вершин на клуб. При двухстах частицах это 37 тысяч " +
             "вершин, которые процессор строит заново каждый кадр.\n\n" +
             "Клуб дыма — мягкое размытое пятно без единой чёткой грани. " +
             "Разницу между 187 и 70 вершинами на нём увидеть нельзя даже " +
             "вплотную.")]
    public PuffDetail puffDetail = PuffDetail.Medium;

    [Tooltip("Не считать дым, пока его не видно. Пара кадров запаса — против " +
             "мигания на самом краю экрана.")]
    public bool skipWhenInvisible = true;
    [Range(0, 1)] public float opacity = 1;
    [Range(0, 1)] public float bandSoftness = .22f;
    public Vector3 wind = new Vector3(.10f, 0, .04f);
    public int seed = 719;
    public int LiveCount => count;
    public int SkippedCount { get; private set; }
    public bool IsPreviewing => preview;
    [NonSerialized] public bool isolatedPreview;

    static readonly List<CatCooksDustSystem> systems = new List<CatCooksDustSystem>();
    static int searchFrame=-1;
    public static CatCooksDustSystem Active
    {
        get
        {
            foreach (var s in systems) if (s != null && s.isActiveAndEnabled && !s.isolatedPreview) return s;
            if(searchFrame==Time.frameCount) return null;
            searchFrame=Time.frameCount;
            // Covers entering Play with both Domain Reload and Scene Reload disabled.
            var found=UnityEngine.Object.FindFirstObjectByType<CatCooksDustSystem>();
            if(found!=null && found.isActiveAndEnabled && !found.isolatedPreview) { systems.Add(found); return found; }
            return null;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { systems.Clear(); searchFrame=-1; }
    struct Puff
    {
        public Vector3 position, velocity, floor, up, aspect;
        public Quaternion rotation;
        public Color colour, endColour;
        public Vector4 style;
        public float colourFade, growth, turbulence, rise, patternMotion;
        public float age, life, diameter, phase, spin, drag, erosion;
        public byte variant, bounce;
        public bool grain, smoke;
    }
    struct Recipe
    {
        public int count;
        public float size, life, spread, speed, lift;
        public Recipe(int n, float s, float l, float r, float v, float up)
        { count=n; size=s; life=l; spread=r; speed=v; lift=up; }
    }
    Puff[] particles;
    CatCooksPuffMesh.Template[] shapes;
    CatCooksPuffMesh.Template grainShape;
    int count, frameSpawns, spawnFrame = -1;
    uint random;
    GameObject generated;
    /// <summary>В каком кадре меш уже собран. Событие BeforeCamera приходит
    /// на КАЖДУЮ камеру: в редакторе их две, и без этой защиты все девяносто
    /// тысяч вершин строятся дважды за кадр.</summary>
    int builtFrame = -1;
    int uploadedIndices = -1, uploadedVerts = -1;
    PuffDetail builtDetail = (PuffDetail)(-1);

    /// <summary>Когда меш собирали в последний раз.</summary>
    float lastBuild = -99f;

    bool visible = true;
    int hiddenFrames;

    float MeshInterval => 1f / Mathf.Max(1, meshUpdatesPerSecond);

    Mesh mesh;
    MeshRenderer meshRenderer;
    Material material;
    bool preview, failed;
    int[] order;
    float[] depths;
    readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>(), shapeUV = new List<Vector3>();
    readonly List<Vector4> dataUV = new List<Vector4>(), styleUV = new List<Vector4>();
    readonly List<Color> colours = new List<Color>();
    readonly List<int> indices = new List<int>();
    const string OwnedName = "CC Dust generated (owned)";
#if UNITY_EDITOR
    double editorTime;
#endif

    void Reset() => puffShader = Shader.Find("CatCooks/Dust and Smoke/Toon Puff");
    void OnEnable()
    {
        if (!isolatedPreview && !systems.Contains(this)) systems.Add(this);
        RenderPipelineManager.beginCameraRendering += BeforeCamera;
        failed = false;
        if (Application.IsPlaying(gameObject)) Initialise();
    }
    void OnDisable()
    {
        systems.Remove(this); RenderPipelineManager.beginCameraRendering -= BeforeCamera;
        StopPreview();
    }
    void OnDestroy() => Dispose();
    void OnValidate()
    {
        maxParticles = Mathf.Clamp(maxParticles,64,2048);
        maxSpawnsPerFrame = Mathf.Clamp(maxSpawnsPerFrame,16,256);
        opacity = CatCooksDustUtility.Safe(opacity,0,1,1);
        bandSoftness = CatCooksDustUtility.Safe(bandSoftness,0,1,.22f);
        failed = false;
    }
    void OnBecameVisible()   { visible = true; hiddenFrames = 0; }
    void OnBecameInvisible() { visible = false; }

    void LateUpdate()
    {
        if (!Application.IsPlaying(gameObject)) return;

        // НЕВИДИМЫЙ ДЫМ НЕ СЧИТАЕТСЯ.
        //
        // Пыль от колёс и выхлоп техники живут по всей карте. Каждый такой
        // источник считал свои частицы и строил свой меш всегда — за спиной,
        // за стеной, в двухстах метрах. Расход при этом ровно тот же, что и
        // перед носом.
        //
        // Пара кадров запаса нужна против мигания на самом краю экрана: там
        // видимость дёргается, и мгновенная остановка давала бы рывок частиц
        // каждый раз, когда игрок ведёт камерой мимо.
        if (skipWhenInvisible && !visible)
        {
            if (++hiddenFrames > 2) return;
        }
        else hiddenFrames = 0;

        Tick(Time.deltaTime);
    }
    bool Initialise()
    {
        if (failed || !isActiveAndEnabled) return false;
        // Смена детализации меняет ШАБЛОНЫ. Не учтёшь её здесь — ползунок
        // в инспекторе будет выглядеть сломанным: значение меняется, а
        // картинка нет.
        if (mesh != null && particles != null && particles.Length == Mathf.Clamp(maxParticles,64,2048)
            && material.shader == puffShader && builtDetail == puffDetail) return true;

        builtDetail = puffDetail;
        if (puffShader == null) puffShader = Shader.Find("CatCooks/Dust and Smoke/Toon Puff");
        if (puffShader == null || !puffShader.isSupported)
        {
            Debug.LogError("Dust: assign the supplied Toon Puff shader. Unity 6 / URP is required.",this);
            failed = true; return false;
        }
        Dispose();
        particles = new Puff[Mathf.Clamp(maxParticles,64,2048)]; order = new int[particles.Length]; depths = new float[particles.Length];
        random = unchecked((uint)seed) ^ 0x9e3779b9u; if (random == 0) random = 1;
        shapes = new CatCooksPuffMesh.Template[6];
        for(int i=0;i<3;i++)
        {
            // ── РАЗРЕШЕНИЕ КЛУБА ──
            //
            // Эти два числа умножаются на число частиц, а те пересобираются
            // каждый кадр. Шаг вниз здесь экономит больше, чем любая правка
            // в шейдере: шейдер работает с готовым мешем, а эти вершины
            // сперва надо построить на процессоре.
            //
            // Клуб дыма — мягкое размытое пятно без единой чёткой грани.
            // Разницу между 187 вершинами и 70 на нём не видно даже вплотную.
            int rings, sides;

            switch (puffDetail)
            {
                case PuffDetail.Low:  rings = 5;  sides = 8;  break;   // 54
                case PuffDetail.High: rings = 10; sides = 16; break;   // 187
                default:              rings = 7;  sides = 10; break;   // 88
            }

            shapes[i]=CatCooksPuffMesh.Build(i, rings, sides);
            var round=CatCooksPuffMesh.Build(i, rings, sides);
            for(int j=0;j<round.vertices.Length;j++)
            {
                Vector3 direction=round.vertices[j].normalized;
                round.vertices[j]=Vector3.Lerp(round.vertices[j],direction*.45f,.8f);
                round.normals[j]=Vector3.Lerp(round.normals[j],direction,.8f).normalized;
            }
            shapes[i+3]=round;
        }
        grainShape = MakeGrain();
        generated = new GameObject(OwnedName) { hideFlags = HideFlags.HideAndDontSave, layer = gameObject.layer };
        generated.transform.SetParent(transform,false);
        mesh = new Mesh { name = "CCD/Puffs", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
        mesh.MarkDynamic(); generated.AddComponent<MeshFilter>().sharedMesh = mesh;
        material = new Material(puffShader) { name = "CCD/Puff material", hideFlags = HideFlags.HideAndDontSave };
        meshRenderer = generated.AddComponent<MeshRenderer>(); meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off; meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off; meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        meshRenderer.enabled = false; return true;
    }

    public void Emit(Effect effect, Vector3 position, Vector3 normal, Vector3 velocity,
        float strength = 1, CatCooksDustProfile profile = null, float scale = 1, float amount = 1, Color? tint = null, float delay = 0)
    {
        if (!isActiveAndEnabled || !CatCooksDustUtility.Finite(position) || !CatCooksDustUtility.Finite(velocity)) return;
        strength = CatCooksDustUtility.Safe(strength,0,3,0);
        amount = CatCooksDustUtility.Safe(amount,0,3,0);
        if (strength <= 0 || amount <= 0 || (!Application.IsPlaying(gameObject) && !preview)) return;
        if (!Initialise()) return;
        if (spawnFrame != Time.frameCount) { spawnFrame = Time.frameCount; frameSpawns = 0; }
        if (profile == null) profile = defaultProfile;
        float density = profile == null ? 1 : Mathf.Clamp(profile.amount,0,2);
        if (density <= 0) return;
        delay = CatCooksDustUtility.Safe(delay,0,3,0);
        normal = CatCooksDustUtility.Normal(normal); velocity = Vector3.ClampMagnitude(velocity,40);
        scale = CatCooksDustUtility.Safe(scale,.05f,8,1);
        Recipe recipe = GetRecipe(effect);
        bool exhaust = effect == Effect.Exhaust;
        bool smoke = effect == Effect.Smoke || exhaust;
        bool small = effect == Effect.Step || effect == Effect.Run || effect == Effect.Wheel || effect == Effect.Skid;
        int capacity = small ? particles.Length - Mathf.Min(48,particles.Length/4) : particles.Length;
        float desired = recipe.count * density * amount * Mathf.Lerp(.7f,1.5f,Mathf.Clamp01((strength-.3f)/2.7f));
        int n = Mathf.FloorToInt(desired); if (Rand() < desired-n) n++;
        n = Mathf.Clamp(n,0,64);
        float sizeScale = scale * Mathf.Sqrt(Mathf.Max(.1f,strength));
        float profileSize = profile == null ? 1 : Mathf.Clamp(profile.size,.1f,2);
        float profileLife = profile == null ? 1 : Mathf.Clamp(profile.lifetime,.2f,2);
        Color colour = profile == null ? new Color(.68f,.55f,.39f,1) : profile.colour;
        // Mesh vertex colours are raw values; match the project's material colour space.
        Color secondary = profile != null && profile.usePalette ? profile.secondaryColour : colour;
        Color ending = profile != null && profile.usePalette ? profile.endColour : colour;
        Color debrisColour = profile != null && profile.usePalette ? profile.grainColour : colour * .64f;
        Color multiplier = tint ?? Color.white;
        colour *= multiplier; secondary *= multiplier; ending *= multiplier; debrisColour *= multiplier;
        if (QualitySettings.activeColorSpace == ColorSpace.Linear)
        { colour = colour.linear; secondary = secondary.linear; ending = ending.linear; debrisColour = debrisColour.linear; }
        colour.a = (profile == null ? .85f : Mathf.Clamp01(profile.opacity)) * Mathf.Clamp01(multiplier.a);
        float erosion = profile == null ? .75f : Mathf.Clamp01(profile.erosion);
        float drag = profile == null ? 3 : Mathf.Clamp(profile.drag,.2f,8);
        float sizeVariation = profile == null ? .25f : Mathf.Clamp(profile.sizeVariation,0,.8f);
        float rise = profile == null ? 1 : Mathf.Clamp(profile.rise,0,3);
        float outward = profile == null ? 1 : Mathf.Clamp(profile.outwardSpeed,0,3);
        Vector4 style = profile == null ? Vector4.zero : new Vector4((float)profile.pattern,
            Mathf.Clamp01(profile.patternStrength),Mathf.Clamp(profile.patternScale,.5f,12),0);
        Quaternion basis = Quaternion.FromToRotation(Vector3.up,normal);
        Vector3 tangentVelocity = Vector3.ProjectOnPlane(velocity,normal);
        for (int i = 0; i < n; i++)
        {
            if (!CanSpawn(capacity)) break;
            float angle = ((i+Rand()*.7f)/Mathf.Max(1,n))*Mathf.PI*2;
            Vector3 radial = basis * new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            float diameter = Mathf.Clamp(recipe.size*sizeScale*profileSize*Mathf.Lerp(1-sizeVariation,1+sizeVariation,Rand()),.012f,12);
            float speed = recipe.speed*sizeScale*outward*Mathf.Lerp(.65f,1.3f,Rand());
            Vector3 vel = radial*speed + normal*recipe.lift*sizeScale*rise;
            if (small) vel -= Vector3.ClampMagnitude(tangentVelocity*.10f,2*scale);
            else vel += Vector3.ClampMagnitude(tangentVelocity*.08f,2*scale);
            if (smoke) vel = Vector3.up*.38f*scale*rise + radial*.06f*scale*outward;
            if (exhaust) vel = normal*recipe.speed*scale*outward + radial*.045f*scale + velocity;
            float variation = profile == null ? .08f : Mathf.Clamp(profile.brightnessVariation,0,.4f);
            float mix = profile != null && profile.usePalette ? Mathf.Clamp01(profile.paletteMix) : 0;
            Color c = Color.Lerp(colour,secondary,Rand()*mix) * Mathf.Lerp(1-variation,1+variation,Rand()); c.a = colour.a;
            int shape = profile == null ? 0 : Mathf.Clamp((int)profile.shape,0,3);
            Vector3 aspect = new Vector3(Mathf.Lerp(.9f,1.2f,Rand()),smoke ? 1.1f : .66f,Mathf.Lerp(.85f,1.1f,Rand()));
            if (shape == 1) aspect = Vector3.one;
            if (shape == 2) aspect = new Vector3(.62f,1.65f,.66f);
            if (shape == 3) aspect = new Vector3(1.3f,.55f,1.2f);
            aspect.y *= profile == null ? 1 : Mathf.Clamp(profile.verticalStretch,.2f,2);
            Add(new Puff { position = position + radial*recipe.spread*sizeScale*Mathf.Lerp(.3f,1,Rand()) + normal*diameter*.22f,
                velocity = vel, floor = position, up = normal, aspect = aspect,
                rotation = basis*Quaternion.Euler(0,Rand()*360,0), spin = Mathf.Lerp(-35,35,Rand()) * (profile == null ? 1 : Mathf.Clamp(profile.spin,0,3)),
                diameter = diameter, age = -delay - (i < n/2 ? 0 : Rand()*.065f),
                life = Mathf.Clamp(recipe.life*profileLife*Mathf.Lerp(.8f,1.2f,Rand()),.12f,6),
                phase = Rand()*70, colour = c, endColour = ending, style = style,
                colourFade = profile != null && profile.usePalette ? Mathf.Clamp01(profile.colourOverLife) : 0,
                patternMotion = profile == null ? 0 : Mathf.Clamp(profile.patternMotion,0,2),
                growth = profile == null ? 1 : Mathf.Clamp(profile.growth,.5f,2),
                turbulence = profile == null ? 1 : Mathf.Clamp(profile.turbulence,0,3), rise = rise,
                erosion = erosion, drag = smoke ? drag*.14f : drag,
                variant = (byte)(Mathf.Min(2,(int)(Rand()*3)) + (shape == 1 ? 3 : 0)), smoke = smoke });
        }
        bool debris = effect == Effect.Impact || effect == Effect.Land || effect == Effect.Burst;
        if (debris || (profile != null && profile.grainsOnAllEffects))
        {
            float grains = profile == null ? .5f : Mathf.Clamp01(profile.grains);
            int grainsCount = Mathf.Clamp(Mathf.RoundToInt(n*grains*.5f),0,16);
            for (int i = 0; i < grainsCount; i++)
            {
                if (!CanSpawn(particles.Length)) break;
                float angle = Rand()*Mathf.PI*2;
                Vector3 radial = basis*new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                Color c = debrisColour; c.a = colour.a;
                Vector3 debrisAspect = Vector3.one;
                if (profile != null && profile.debrisShape == CatCooksDustProfile.Debris.Splinters) debrisAspect = new Vector3(.45f,2,.45f);
                if (profile != null && profile.debrisShape == CatCooksDustProfile.Debris.Flakes) debrisAspect = new Vector3(1.6f,.22f,1.1f);
                Add(new Puff { position = position+normal*.04f*scale, floor=position, up=normal,
                    velocity = (radial*Mathf.Lerp(.5f,1.4f,Rand())+normal*Mathf.Lerp(.7f,1.6f,Rand()))*sizeScale,
                    diameter = Mathf.Clamp(.027f*sizeScale*profileSize*(profile == null ? 1 : Mathf.Clamp(profile.grainSize,.3f,3)),.008f,.3f),
                    life = Mathf.Lerp(.35f,.75f,Rand()), age=-delay, phase=Rand()*70, colour=c, endColour=c,
                    aspect=debrisAspect, rotation=Quaternion.Euler(Rand()*180,Rand()*180,Rand()*180),
                    spin=Rand()*200, drag=.4f, grain=true });
            }
        }
    }
    public void Impact(Vector3 point, Vector3 normal, float strength=1, CatCooksDustProfile profile=null, float scale=1)
        => Emit(Effect.Impact,point,normal,Vector3.zero,strength,profile,scale);
    public void Smoke(Vector3 point, float scale=1, CatCooksDustProfile profile=null)
        => Emit(Effect.Smoke,point,Vector3.up,Vector3.zero,1,profile,scale);
    bool CanSpawn(int capacity)
    {
        if (count < capacity && frameSpawns < Mathf.Clamp(maxSpawnsPerFrame,16,256)) return true;
        SkippedCount++; return false;
    }
    void Add(Puff p) { particles[count++]=p; frameSpawns++; }
    float Rand() { random^=random<<13; random^=random>>17; random^=random<<5; return (random&0xffffffu)/16777216f; }
    static Recipe GetRecipe(Effect effect)
    {
        switch (effect)
        {
            case Effect.Step: return new Recipe(3,.16f,.48f,.035f,.16f,.20f);
            case Effect.Run: return new Recipe(4,.20f,.57f,.06f,.28f,.24f);
            case Effect.Jump: return new Recipe(8,.20f,.55f,.12f,.8f,.16f);
            case Effect.Land: return new Recipe(13,.26f,.72f,.15f,1.05f,.28f);
            case Effect.Impact: return new Recipe(10,.25f,.75f,.11f,.95f,.45f);
            case Effect.Wheel: return new Recipe(2,.19f,.65f,.04f,.14f,.21f);
            case Effect.Skid: return new Recipe(4,.24f,.75f,.07f,.38f,.3f);
            case Effect.Burst: return new Recipe(26,.42f,1.0f,.22f,1.65f,.75f);
            case Effect.Exhaust: return new Recipe(2,.19f,1.35f,.018f,.65f,.08f);
            default: return new Recipe(2,.40f,2.4f,.04f,.08f,.42f);
        }
    }
    public void ClearParticles() { count=0; SkippedCount=0; if (mesh!=null) { mesh.Clear(); meshRenderer.enabled=false; } }
    void Tick(float elapsed)
    {
        if (!Initialise()) return;
        float remaining = Mathf.Clamp(elapsed,0,.15f);
        Vector3 air = CatCooksDustUtility.Finite(wind) ? Vector3.ClampMagnitude(wind,3) : Vector3.zero;
        while (remaining > .00001f)
        {
            float dt = Mathf.Min(remaining,1f/60f); remaining-=dt;
            for (int i=count-1;i>=0;i--)
            {
                Puff p=particles[i]; p.age+=dt;
                if (p.age >= p.life) { particles[i]=particles[--count]; continue; }
                if (p.age<0) { particles[i]=p; continue; }
                if (p.grain) p.velocity += Physics.gravity*dt;
                else
                {
                    Vector3 target=air+(p.smoke ? Vector3.up*.24f*p.rise : p.up*.025f*p.rise)*p.diameter;
                    p.velocity=Vector3.Lerp(p.velocity,target,1-Mathf.Exp(-p.drag*dt));
                    p.velocity += new Vector3(Mathf.Sin(p.phase+p.age*3),0,Mathf.Cos(p.phase+p.age*2.7f))*p.diameter*.22f*p.turbulence*dt;
                }
                p.position+=p.velocity*dt;
                if (p.grain)
                {
                    float below=Vector3.Dot(p.position-p.floor,p.up)-p.diameter*.4f;
                    if (below<0)
                    {
                        p.position-=p.up*below;
                        if (p.bounce==0) { p.velocity=Vector3.Reflect(p.velocity,p.up)*.28f; p.bounce=1; }
                        else p.velocity=Vector3.ProjectOnPlane(p.velocity,p.up)*Mathf.Exp(-8*dt);
                    }
                }
                particles[i]=p;
            }
        }
        material.SetFloat("_Opacity",opacity); material.SetFloat("_Softness",bandSoftness);
        BuildMesh(null);
    }
    /// <summary>
    /// СБОРКА МЕША — ОДИН РАЗ ЗА КАДР И НЕ ЧАЩЕ ЗАДАННОЙ ЧАСТОТЫ.
    ///
    /// Событие приходит на каждую камеру. В редакторе их две — игровая и
    /// Scene, — а с отражениями бывает больше. Раньше каждая строила меш
    /// заново, вместе с сортировкой всех частиц по глубине.
    ///
    /// Сортировка от разных камер даёт разный порядок, но заметить это можно
    /// только глядя на один дым с двух сторон одновременно. Платить за такой
    /// случай двойной пересборкой в каждом кадре не стоит.
    /// </summary>
    void BeforeCamera(ScriptableRenderContext context,Camera camera)
    {
        if (mesh==null || camera==null) return;
        if (!Application.IsPlaying(gameObject) && !preview) return;
        if (skipWhenInvisible && !visible && hiddenFrames > 2) return;

        if (builtFrame == Time.frameCount) return;

        float now = Application.IsPlaying(gameObject) ? Time.time : (float)UnityEditor_TimeSafe();
        if (now - lastBuild < MeshInterval) return;

        builtFrame = Time.frameCount;
        lastBuild = now;

        BuildMesh(camera);
    }

    static double UnityEditor_TimeSafe() => Time.realtimeSinceStartupAsDouble;
    void BuildMesh(Camera camera)
    {
        vertices.Clear(); normals.Clear(); shapeUV.Clear(); dataUV.Clear(); styleUV.Clear(); colours.Clear(); indices.Clear();
        if (count==0)
        {
            mesh.Clear();
            uploadedIndices = -1; uploadedVerts = -1;
            meshRenderer.enabled=false;
            return;
        }
        Matrix4x4 worldToLocal=generated.transform.worldToLocalMatrix;
        Matrix4x4 normalToLocal=generated.transform.localToWorldMatrix.transpose;

        // Коробка копится ПО ЧАСТИЦАМ, пока мы по ним и так идём. Сфера на
        // частицу вместо прохода по всем её вершинам.
        Bounds localBounds = new Bounds();
        bool hasBounds = false;

        int visible=0;
        for (int i=0;i<count;i++) if (particles[i].age>=0)
        {
            order[visible]=i;
            depths[visible]=camera==null ? -particles[i].position.y :
                -Vector3.Dot(particles[i].position-camera.transform.position,camera.transform.forward);
            visible++;
        }
        if (visible>1) Array.Sort(depths,order,0,visible);
        for (int j=0;j<visible;j++)
        {
            Puff p=particles[order[j]]; float age=Mathf.Clamp01(p.age/p.life);
            float growth=p.smoke ? .5f+age*1.3f : Mathf.Lerp(.35f,1,Mathf.SmoothStep(0,1,age/.22f));
            if (!p.smoke) growth*=Mathf.Lerp(1,.5f,Mathf.SmoothStep(0,1,(age-.65f)/.35f));
            growth *= Mathf.Lerp(1,p.growth,age);
            if (p.grain) growth=Mathf.Lerp(1,.3f,age);
            float diameter=Mathf.Max(.001f,p.diameter*growth);
            Vector3 dimensions=p.aspect*diameter;
            if (!p.grain && !p.smoke) dimensions.y*=Mathf.Lerp(.7f,1.25f,age);
            Quaternion rotation=p.rotation*Quaternion.Euler(p.grain ? p.age*65 : 0,p.spin*p.age,0);
            Color colour=Color.Lerp(p.colour,p.endColour,Mathf.SmoothStep(0,1,age)*p.colourFade);
            colour.a=p.colour.a;
            colour.a*=Mathf.Clamp01(p.age/.055f)*(1-Mathf.SmoothStep(0,1,(age-.67f)/.33f));
            // Коробка по частице: центр плюс её наибольший радиус. Сфера
            // вместо прохода по всем вершинам клуба.
            Vector3 centreLocal = worldToLocal.MultiplyPoint3x4(p.position);
            float reach = Mathf.Max(dimensions.x, Mathf.Max(dimensions.y, dimensions.z));

            if (!hasBounds)
            {
                localBounds = new Bounds(centreLocal, Vector3.one * reach * 2f);
                hasBounds = true;
            }
            else
            {
                localBounds.Encapsulate(centreLocal + Vector3.one * reach);
                localBounds.Encapsulate(centreLocal - Vector3.one * reach);
            }

            var t=p.grain ? grainShape : shapes[p.variant]; int start=vertices.Count;
            for (int k=0;k<t.vertices.Length;k++)
            {
                Vector3 wp=p.position+rotation*Vector3.Scale(t.vertices[k],dimensions);
                // A contact-plane constraint prevents half a cloud appearing under the floor.
                if (!p.smoke && !p.grain)
                { float d=Vector3.Dot(wp-p.floor,p.up); if (d<.003f) wp+=p.up*(.003f-d); }
                vertices.Add(worldToLocal.MultiplyPoint3x4(wp));
                Vector3 n=t.normals[k];
                Vector3 wn=(rotation*new Vector3(n.x/dimensions.x,n.y/dimensions.y,n.z/dimensions.z)).normalized;
                normals.Add(normalToLocal.MultiplyVector(wn).normalized);
                shapeUV.Add(t.vertices[k]); dataUV.Add(new Vector4(p.phase,age,p.erosion,p.grain?1:0)); Vector4 style=p.style; style.w=p.age*p.patternMotion; styleUV.Add(style); colours.Add(colour);
            }
            foreach (int index in t.triangles) indices.Add(start+index);
        }
        // ═══ ЗАЛИВКА ═══
        //
        // ГРАНИЦЫ НЕ СЧИТАЮТСЯ ПО ВЕРШИНАМ.
        //
        // Было: SetTriangles(indices, 0, true). Флаг true — это
        // calculateBounds, ещё один полный проход по всем вершинам. При
        // девяноста тысячах он стоит больше, чем сама заливка данных.
        //
        // Коробку собираем по ЧАСТИЦАМ, а их сотни против десятков тысяч
        // вершин: точность та же, работы в двести раз меньше.
        //
        // ИНДЕКСЫ ЗАЛИВАЮТСЯ ТОЛЬКО КОГДА МЕНЯЮТСЯ.
        //
        // Пока число видимых частиц то же, треугольники те же — меняются
        // лишь позиции вершин. Заливка индексов вдобавок тянет проверку
        // каждого на выход за буфер, а их под двести тысяч.
        //
        // Сверяем И вершины, И индексы: набор частиц мог смениться так, что
        // число треугольников совпало при другой раскладке, и старые индексы
        // указали бы не туда.
        bool sameLayout = indices.Count == uploadedIndices
                       && vertices.Count == uploadedVerts;

        if (!sameLayout) mesh.Clear(false);

        mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetColors(colours);
        mesh.SetUVs(1,dataUV); mesh.SetUVs(2,shapeUV); mesh.SetUVs(3,styleUV);

        if (!sameLayout)
        {
            mesh.SetTriangles(indices,0,false);
            uploadedIndices = indices.Count;
            uploadedVerts = vertices.Count;
        }

        mesh.bounds = localBounds;
        meshRenderer.enabled=visible>0;
    }
    static CatCooksPuffMesh.Template MakeGrain()
    {
        Vector3[] points={ Vector3.up*.5f,Vector3.down*.5f,Vector3.right*.5f,Vector3.forward*.5f,Vector3.left*.5f,Vector3.back*.5f };
        int[] top={0,3,2,0,4,3,0,5,4,0,2,5,1,2,3,1,3,4,1,4,5,1,5,2};
        var v=new Vector3[24];var n=new Vector3[24];var tris=new int[24];
        for(int i=0;i<24;i+=3)
        {
            Vector3 normal=Vector3.Cross(points[top[i+1]]-points[top[i]],points[top[i+2]]-points[top[i]]).normalized;
            for(int k=0;k<3;k++) { v[i+k]=points[top[i+k]]; n[i+k]=normal; tris[i+k]=i+k; }
        }
        return new CatCooksPuffMesh.Template { vertices=v,normals=n,triangles=tris,uv=new Vector2[24] };
    }
    public void StartPreview()
    {
#if UNITY_EDITOR
        if (Application.isPlaying || EditorUtility.IsPersistent(this) || !isActiveAndEnabled) return;
        preview=true; if (!Initialise()) { preview=false; return; }
        editorTime=EditorApplication.timeSinceStartup;
        EditorApplication.update-=EditorTick; EditorApplication.update+=EditorTick;
#endif
    }
    public void StopPreview()
    {
        preview=false;
#if UNITY_EDITOR
        EditorApplication.update-=EditorTick;
#endif
        Dispose();
    }
#if UNITY_EDITOR
    // PreviewRenderUtility owns the clock and scene; never register its world as Active.
    public void BeginIsolatedPreview()
    {
        isolatedPreview=true; systems.Remove(this); preview=true;
        EditorApplication.update-=EditorTick; Initialise();
    }
    public void SimulateIsolatedPreview(float delta)
    { if (isolatedPreview && preview) { frameSpawns=0; Tick(delta); } }
    void EditorTick()
    {
        if (this==null || !preview) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) { StopPreview(); return; }
        double now=EditorApplication.timeSinceStartup;
        frameSpawns=0; Tick((float)(now-editorTime)); editorTime=now;
        SceneView.RepaintAll();
    }
#endif
    void Dispose()
    {
        for(int i=transform.childCount-1;i>=0;i--)
        {
            var child=transform.GetChild(i);
            if(child.name!=OwnedName || (child.gameObject.hideFlags&HideFlags.DontSave)==0) continue;
            child.gameObject.SetActive(false);
            var f=child.GetComponent<MeshFilter>();var r=child.GetComponent<MeshRenderer>();
            if(f!=null && f.sharedMesh!=null && f.sharedMesh.name.StartsWith("CCD/")) Release(f.sharedMesh);
            if(r!=null && r.sharedMaterial!=null && r.sharedMaterial.name.StartsWith("CCD/")) Release(r.sharedMaterial);
            Release(child.gameObject);
        }
        generated=null; mesh=null; material=null; particles=null; count=0;
    }
    static void Release(UnityEngine.Object obj)
    { if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
}
