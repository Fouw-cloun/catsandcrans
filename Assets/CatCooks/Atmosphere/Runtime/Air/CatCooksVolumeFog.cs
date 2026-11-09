using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// ОБЪЁМНЫЙ ТУМАН И ПЕСЧАНАЯ ПЫЛЬ. Два режима, плавный переход между ними.
///
/// Собирает всё сам: бокс объёма, материал, текстуры симуляции, буфер
/// возмутителей. В сцену достаточно бросить компонент и назначить
/// compute-шейдер.
///
/// ═══ ПОЧЕМУ ЭТО, А НЕ ЧАСТИЦЫ ═══
///
/// У билборда всегда есть силуэт. Его можно рвать шумом и мять варпом, но он
/// остаётся конечной плоской фигурой, и глаз её находит. У объёма силуэта нет
/// вообще: есть плотность, которая копится вдоль луча. Ни «летающих кружков»,
/// ни «картинок» здесь не бывает по устройству.
///
/// ═══ ДВА РЕЖИМА ═══
///
/// ТУМАН   — холодный, лежит низко, медленный, ровный, почти не светится.
/// ПЕСОК   — тёплый, поднимается выше, быстрый, рваный, сильно светится
///           против солнца и вытянут по ветру.
///
/// Переход между ними непрерывный: SetMode(Mode.Sand, 20f) за двадцать секунд
/// превратит утренний туман над стройкой в песчаную бурю, и в середине это
/// будет выглядеть как мутная пыльная дымка, а не как две картинки сразу.
///
/// ═══ ВЗАИМОДЕЙСТВИЕ ═══
///
/// Всё, на чём висит CatCooksFogDisturber, попадает в симуляцию: техника
/// гонит туман перед собой и оставляет закручивающийся след, постройки
/// вытесняют его из своего объёма, а работающая техника с отрицательным carve
/// наоборот ДОБАВЛЯЕТ пыль.
///
/// ═══ ЧТО ОБЯЗАТЕЛЬНО ═══
///
/// 1. URP-ассет: Depth Texture ВКЛЮЧЕНА. Без неё туман закроет всю геометрию.
/// 2. Шейдер CatCooks/VolumeFog в Always Included Shaders, либо материал
///    задан вручную в поле Material Override.
/// 3. Compute-шейдер назначен. Без него симуляции не будет, но туман всё
///    равно заработает на одном процедурном шуме — просто перестанет
///    реагировать на объекты.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Атмосфера/Volume Fog")]
[ExecuteAlways]
public class CatCooksVolumeFog : MonoBehaviour
{
    public enum Mode { Fog = 0, Sand = 1 }

    /// <summary>
    /// ПРОФИЛЬ РЕЖИМА. Полный набор того, чем туман отличается от пыли.
    /// Между двумя профилями идёт непрерывная интерполяция.
    /// </summary>
    [System.Serializable]
    public class Profile
    {
        [Header("Цвет")]
        public Color colorTop = new Color(0.86f, 0.90f, 0.98f, 1f);
        public Color colorBottom = new Color(0.45f, 0.50f, 0.62f, 1f);
        [Range(0f, 4f)] public float emission = 1f;

        [Header("Плотность")]
        [Tooltip("ДАЛЬНОСТЬ ВИДИМОСТИ В МЕТРАХ. На этой дистанции остаётся " +
                 "около 5% света.\n\n" +
                 "Это физическая величина, а не абстрактный ползунок: 150 — " +
                 "лёгкая дымка, 60 — заметный туман, 25 — плохо видно " +
                 "соседний экскаватор, 10 — вытянутой руки не видно.\n\n" +
                 "Число НЕ ЗАВИСИТ от размера зоны и числа шагов трассировки.")]
        [Range(4f, 400f)] public float visibility = 130f;

        [Tooltip("КОНТРАСТ. Разброс между плотными и редкими местами. " +
                 "Единица — как есть, выше — резче переходы. Среднюю " +
                 "плотность не трогает.")]
        [Range(0.3f, 4f)] public float contrast = 1f;

        [Tooltip("ЧИСТЫЕ ПРОМЕЖУТКИ. Срезает слабую плотность в ноль.\n\n" +
                 "Без этого у среды нет по-настоящему пустых мест и она всегда " +
                 "чуть мутная везде. Подними до 0.25 — появятся разрывы, " +
                 "сквозь которые видно насквозь.")]
        [Range(0f, 0.9f)] public float clearHoles = 0.1f;

        [Tooltip("СПАД ПО ВЫСОТЕ. Большое значение прижимает среду к земле " +
                 "(приземный туман), малое даёт равномерный объём (пыль в " +
                 "воздухе).")]
        [Range(0.1f, 8f)] public float heightFalloff = 2.6f;

        [Tooltip("Где находится середина спада, в долях высоты бокса")]
        [Range(-0.5f, 0.5f)] public float heightCenter = -0.15f;

        [Header("Структура")]
        [Tooltip("Сила мелкого объёмного шума поверх симуляции. Симуляция даёт " +
                 "крупную форму, этот шум — структуру вблизи.")]
        [Range(0f, 1f)] public float detailAmount = 0.45f;

        [Tooltip("Крупность мелкого шума в метрах")]
        [Range(0.05f, 3f)] public float detailScale = 0.5f;

        [Range(0f, 2f)] public float detailSpeed = 0.1f;

        [Tooltip("КРУПНАЯ НЕОДНОРОДНОСТЬ. Разброс плотности в масштабе " +
                 "десятков метров.\n\n" +
                 "Между симуляцией (метры) и мелким шумом (сантиметры) была " +
                 "дыра: на средних масштабах среда оставалась равномерной и " +
                 "читалась как заливка. Это главная ручка «разнообразия».")]
        [Range(0f, 1f)] public float macroAmount = 0.5f;

        [Tooltip("Размер крупных пятен, метры")]
        [Range(3f, 120f)] public float macroScale = 28f;

        [Tooltip("РАСТЯЖЕНИЕ ПО ВЕТРУ. Единица — клочья круглые, стоячий " +
                 "туман. Больше — вытянутые вдоль потока: несомая пыль.")]
        [Range(1f, 8f)] public float anisotropy = 1f;

        [Header("Солнце")]
        [Tooltip("РАССЕЯНИЕ ВПЕРЁД. Насколько среда светится, когда смотришь " +
                 "против солнца. Для пыли это главный признак: без него " +
                 "песчаная буря выглядит коричневым фильтром.")]
        [Range(0f, 3f)] public float sunScatter = 0.3f;

        [Range(1f, 32f)] public float sunSharpness = 10f;
        public Color sunTint = new Color(1f, 0.95f, 0.85f, 1f);

        [Header("Симуляция")]
        [Tooltip("Крупность целевого шума, к которому зарастают следы")]
        [Range(1f, 24f)] public float simNoiseScale = 6f;

        [Range(0f, 1f)] public float simNoiseDetail = 0.35f;

        [Tooltip("КЛОЧКОВАТОСТЬ. Ноль — ровная муть, единица — рваные клочья " +
                 "с чистыми промежутками между ними.")]
        [Range(0f, 1f)] public float patchiness = 0.5f;

        [Tooltip("Скорость зарастания следов. Низкая — след от машины держится " +
                 "долго, высокая — затягивается почти сразу.")]
        [Range(0.02f, 3f)] public float regrow = 0.3f;

        [Tooltip("Множитель к скорости ветра для переноса поля")]
        [Range(0f, 6f)] public float windScale = 1f;

        public static void Lerp(Profile a, Profile b, float t, Profile into)
        {
            into.colorTop = Color.Lerp(a.colorTop, b.colorTop, t);
            into.colorBottom = Color.Lerp(a.colorBottom, b.colorBottom, t);
            into.sunTint = Color.Lerp(a.sunTint, b.sunTint, t);

            into.emission = Mathf.Lerp(a.emission, b.emission, t);
            into.visibility = Mathf.Lerp(a.visibility, b.visibility, t);
            into.contrast = Mathf.Lerp(a.contrast, b.contrast, t);
            into.clearHoles = Mathf.Lerp(a.clearHoles, b.clearHoles, t);
            into.macroAmount = Mathf.Lerp(a.macroAmount, b.macroAmount, t);
            into.macroScale = Mathf.Lerp(a.macroScale, b.macroScale, t);
            into.heightFalloff = Mathf.Lerp(a.heightFalloff, b.heightFalloff, t);
            into.heightCenter = Mathf.Lerp(a.heightCenter, b.heightCenter, t);

            into.detailAmount = Mathf.Lerp(a.detailAmount, b.detailAmount, t);
            into.detailScale = Mathf.Lerp(a.detailScale, b.detailScale, t);
            into.detailSpeed = Mathf.Lerp(a.detailSpeed, b.detailSpeed, t);
            into.anisotropy = Mathf.Lerp(a.anisotropy, b.anisotropy, t);

            into.sunScatter = Mathf.Lerp(a.sunScatter, b.sunScatter, t);
            into.sunSharpness = Mathf.Lerp(a.sunSharpness, b.sunSharpness, t);

            into.simNoiseScale = Mathf.Lerp(a.simNoiseScale, b.simNoiseScale, t);
            into.simNoiseDetail = Mathf.Lerp(a.simNoiseDetail, b.simNoiseDetail, t);
            into.patchiness = Mathf.Lerp(a.patchiness, b.patchiness, t);
            into.regrow = Mathf.Lerp(a.regrow, b.regrow, t);
            into.windScale = Mathf.Lerp(a.windScale, b.windScale, t);
        }
    }

    // ═════════════════════════ ПОЛЯ ═════════════════════════

    [Header("━━━ Режим ━━━")]
    [Tooltip("Целевой режим. Переход между туманом и песком непрерывный.")]
    [SerializeField] private Mode _mode = Mode.Fog;

    [Tooltip("Текущее положение между режимами: 0 — туман, 1 — песок")]
    [Range(0f, 1f)][SerializeField] private float _blend = 0f;

    [Tooltip("За сколько секунд идёт переход между режимами")]
    [Min(0.1f)][SerializeField] private float _modeSeconds = 12f;

    [Tooltip("ОБЩАЯ СИЛА. Ноль — чистый воздух, единица — режим на полную. " +
             "Это отдельная ручка от режима: можно держать режим «песок» и " +
             "плавно поднимать и опускать силу.")]
    [Range(0f, 1f)][SerializeField] private float _intensity = 1f;

    [Range(0f, 1f)][SerializeField] private float _targetIntensity = 1f;
    [Min(0.1f)][SerializeField] private float _intensitySeconds = 8f;

    [Header("━━━ Профили ━━━")]
    [SerializeField] private Profile _fogProfile = new Profile
    {
        colorTop = new Color(0.88f, 0.92f, 0.98f, 1f),
        colorBottom = new Color(0.46f, 0.52f, 0.63f, 1f),
        emission = 1f,
        visibility = 150f,          // лёгкая утренняя дымка
        contrast = 1.1f,
        clearHoles = 0.12f,
        macroAmount = 0.55f,
        macroScale = 34f,
        heightFalloff = 3.6f,       // прижат к земле
        heightCenter = -0.22f,
        detailAmount = 0.4f,
        detailScale = 0.7f,
        detailSpeed = 0.06f,
        anisotropy = 1f,            // клочья круглые: туман стоит
        sunScatter = 0.18f,
        sunSharpness = 14f,
        sunTint = new Color(1f, 0.97f, 0.92f, 1f),
        simNoiseScale = 5f,
        simNoiseDetail = 0.3f,
        patchiness = 0.35f,
        regrow = 0.22f,             // след держится долго
        windScale = 0.35f
    };

    [SerializeField] private Profile _sandProfile = new Profile
    {
        colorTop = new Color(0.88f, 0.78f, 0.60f, 1f),
        colorBottom = new Color(0.46f, 0.37f, 0.26f, 1f),
        emission = 1.15f,
        visibility = 55f,           // соседний экскаватор уже мутный
        contrast = 1.4f,
        clearHoles = 0.18f,
        macroAmount = 0.7f,
        macroScale = 22f,
        heightFalloff = 1.1f,       // поднимается выше
        heightCenter = 0.05f,
        detailAmount = 0.65f,
        detailScale = 0.4f,
        detailSpeed = 0.5f,
        anisotropy = 3.5f,          // вытянута по потоку
        sunScatter = 1.3f,          // светится против солнца
        sunSharpness = 6f,
        sunTint = new Color(1f, 0.86f, 0.62f, 1f),
        simNoiseScale = 9f,
        simNoiseDetail = 0.6f,
        patchiness = 0.75f,         // рваная
        regrow = 0.9f,              // следы затягивает быстро
        windScale = 2.6f
    };

    [Header("━━━ Объём ━━━")]
    [Tooltip("Размер зоны по X и Z, метры. Зона едет за камерой, накрывать " +
             "весь мир бессмысленно.")]
    [SerializeField] private Vector2 _areaSize = new Vector2(70f, 70f);

    [Tooltip("Высота объёма, метры")]
    [Min(1f)][SerializeField] private float _height = 14f;

    [Tooltip("Смещение низа объёма относительно объекта, метры")]
    [SerializeField] private float _groundOffset = -1f;

    [SerializeField] private bool _followCamera = true;

    [Tooltip("Шаг привязки зоны при следовании за камерой, метры.\n\n" +
             "Без привязки поле симуляции ползёт вместе с камерой непрерывно, " +
             "и туман выглядит приклеенным к игроку. Крупный шаг делает " +
             "перескоки заметными. Четверть зоны — разумный компромисс.")]
    [Min(0f)][SerializeField] private float _followSnap = 8f;

    [Header("━━━ Симуляция ━━━")]
    [Tooltip("Compute-шейдер CatCooksFogSim. Без него туман работает на одном " +
             "процедурном шуме и перестаёт реагировать на объекты.")]
    [SerializeField] private ComputeShader _compute;

    [Tooltip("Разрешение поля. 256 на зону в семьдесят метров даёт ячейку " +
             "около четверти метра — мелкую структуру всё равно рисует " +
             "процедурный шум в шейдере, поэтому выше поднимать смысла мало.")]
    [SerializeField] private int _resolution = 256;

    [Tooltip("Сколько шагов симуляции прокрутить при старте, чтобы туман не " +
             "проявлялся из пустоты на глазах у игрока.")]
    [Range(0, 200)][SerializeField] private int _prewarmSteps = 90;

    [Tooltip("Максимум возмутителей. Больше в буфер не поедет.")]
    [Min(1)][SerializeField] private int _maxDisturbers = 48;

    [Tooltip("Как часто пересобирать список возмутителей, секунды. Поиск по " +
             "сцене дорогой, каждый кадр его делать нельзя.")]
    [Min(0.1f)][SerializeField] private float _rescanInterval = 2f;

    [Header("━━━ Ветер ━━━")]
    [SerializeField] private CatCooksWindController _wind;
    [SerializeField] private Vector3 _fallbackDirection = new Vector3(1f, 0f, 0.3f);
    [SerializeField] private float _baseWindSpeed = 1.4f;

    [Header("━━━ Порывы ━━━")]
    [SerializeField] private bool _gustsEnabled = true;
    [Range(0f, 1f)][SerializeField] private float _gustBase = 0.1f;
    [Range(0f, 1f)][SerializeField] private float _gustAmplitude = 0.8f;
    [Range(0.5f, 40f)][SerializeField] private float _gustsPerMinute = 8f;

    [Tooltip("РЕЗКОСТЬ. Единица — плавная волна, тройка — редкие удары с " +
             "затишьем. Ветер узнаётся именно по чередованию, а не по ровной " +
             "раскачке.")]
    [Range(1f, 6f)][SerializeField] private float _gustSharpness = 2.6f;

    [Range(0f, 1f)][SerializeField] private float _gustReadout;

    [Header("━━━ Рендер ━━━")]
    [SerializeField] private Material _materialOverride;

    [Tooltip("Шагов трассировки. Шестнадцать хватает почти всегда: банды " +
             "убирает дизеринг, а не количество шагов.")]
    [Range(4, 48)][SerializeField] private int _steps = 16;

    [Range(0f, 1f)][SerializeField] private float _dither = 1f;
    [Range(0f, 0.5f)][SerializeField] private float _edgeFade = 0.12f;
    [SerializeField] private bool _castOnSceneView = true;

    [Header("━━━ Отладка ━━━")]
    [SerializeField] private bool _showReadout = false;
    [SerializeField] private bool _drawGizmos = true;

    // ───────────────────────── состояние ─────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct DisturberData
    {
        public Vector2 position;
        public Vector2 velocity;
        public float radius;
        public float push;
        public float swirl;
        public float carve;
    }

    private const int DisturberStride = 32;   // 8 float

    private Transform _cam;
    private Transform _box;
    private MeshRenderer _renderer;
    private Material _material;
    private Mesh _cubeMesh;

    private RenderTexture[] _density;
    private int _current, _kernel, _groups;
    private bool _simReady;

    private ComputeBuffer _buffer;
    private DisturberData[] _cpuData;
    private readonly List<CatCooksFogDisturber> _disturbers = new List<CatCooksFogDisturber>();
    private float _rescanTimer;

    private Vector3 _windDir = Vector3.right;
    private float _windStrength = 1f;
    private float _gust, _gustImpulse, _gustImpulseDecay, _gustSeed;
    private float _intensityVel;

    private readonly Profile _blended = new Profile();

    private static readonly int DensityTexId = Shader.PropertyToID("_DensityTex");
    private static readonly int ExtinctionId = Shader.PropertyToID("_Extinction");
    private static readonly int ContrastId = Shader.PropertyToID("_Contrast");
    private static readonly int ClearHolesId = Shader.PropertyToID("_ClearHoles");
    private static readonly int MacroAmountId = Shader.PropertyToID("_MacroAmount");
    private static readonly int MacroScaleId = Shader.PropertyToID("_MacroScale");
    private static readonly int OutlineFadeId = Shader.PropertyToID("_CatFogOutlineFade");
    private static readonly int SimAmountId = Shader.PropertyToID("_SimAmount");
    private static readonly int ColorTopId = Shader.PropertyToID("_ColorTop");
    private static readonly int ColorBottomId = Shader.PropertyToID("_ColorBottom");
    private static readonly int EmissionId = Shader.PropertyToID("_Emission");
    private static readonly int HeightFalloffId = Shader.PropertyToID("_HeightFalloff");
    private static readonly int HeightCenterId = Shader.PropertyToID("_HeightCenter");
    private static readonly int DetailAmountId = Shader.PropertyToID("_DetailAmount");
    private static readonly int DetailScaleId = Shader.PropertyToID("_DetailScale");
    private static readonly int DetailSpeedId = Shader.PropertyToID("_DetailSpeed");
    private static readonly int AnisotropyId = Shader.PropertyToID("_Anisotropy");
    private static readonly int WindDirId = Shader.PropertyToID("_WindDir");
    private static readonly int SunScatterId = Shader.PropertyToID("_SunScatter");
    private static readonly int SunSharpnessId = Shader.PropertyToID("_SunSharpness");
    private static readonly int SunTintId = Shader.PropertyToID("_SunTint");
    private static readonly int StepsId = Shader.PropertyToID("_Steps");
    private static readonly int DitherId = Shader.PropertyToID("_Dither");
    private static readonly int EdgeFadeId = Shader.PropertyToID("_EdgeFade");

    /// <summary>
    /// Экстинкция на метр. Переводится из дальности видимости: коэффициент 3
    /// означает, что на заданной дистанции остаётся exp(-3) ≈ 5% света —
    /// общепринятое определение метеорологической видимости.
    /// </summary>
    public float CurrentExtinction
    {
        get
        {
            float v = Mathf.Max(1f, _blended.visibility);
            return (3f / v) * _intensity * (1f + _gust * 0.35f);
        }
    }

    /// <summary>Текущая эффективная дальность видимости, метры.</summary>
    public float CurrentVisibility =>
        CurrentExtinction < 1e-5f ? 99999f : 3f / CurrentExtinction;

    public float Blend => _blend;
    public Mode CurrentMode => _mode;
    public float Gust01 => _gust;
    public float Intensity => _intensity;

    // ───────────────────────── жизненный цикл ─────────────────────────

    private void OnEnable()
    {
        _gustSeed = Random.value * 1000f;
        Build();
    }

    private void OnDisable() => Teardown();

    private void OnDestroy() => Teardown();

    private void Teardown()
    {
        _buffer?.Release();
        _buffer = null;

        if (_density != null)
        {
            foreach (RenderTexture rt in _density)
                if (rt != null) rt.Release();
            _density = null;
        }

        _simReady = false;
    }

    // ───────────────────────── сборка ─────────────────────────

    public void Build()
    {
        Teardown();

        if (_wind == null) _wind = Object.FindFirstObjectByType<CatCooksWindController>();

        BuildBox();
        BuildSimulation();
        Rescan();

        SampleWind();
        PushMaterial();

        // Прогрев: без него первые секунды туман проявляется из пустоты прямо
        // на глазах, и это видно как включение эффекта.
        if (_simReady && Application.isPlaying)
            for (int i = 0; i < _prewarmSteps; i++) Step(0.1f);
    }

    private void BuildBox()
    {
        const string objName = "__FogVolume";
        Transform t = transform.Find(objName);

        if (t == null)
        {
            GameObject go = new GameObject(objName)
            {
                hideFlags = HideFlags.DontSave
            };
            go.transform.SetParent(transform, false);
            t = go.transform;
        }

        _box = t;

        MeshFilter mf = t.GetComponent<MeshFilter>();
        if (mf == null) mf = t.gameObject.AddComponent<MeshFilter>();

        if (_cubeMesh == null) _cubeMesh = BuildCube();
        mf.sharedMesh = _cubeMesh;

        _renderer = t.GetComponent<MeshRenderer>();
        if (_renderer == null) _renderer = t.gameObject.AddComponent<MeshRenderer>();

        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        _renderer.allowOcclusionWhenDynamic = false;

        _renderer.sharedMaterial = GetMaterial();

        ApplyBoxTransform();
    }

    /// <summary>
    /// Куб [-0.5, 0.5]. Строим сами, а не берём встроенный: встроенный меш
    /// приходится тащить через Resources, а это лишняя зависимость ради
    /// двенадцати треугольников.
    /// </summary>
    private static Mesh BuildCube()
    {
        Vector3[] v =
        {
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3( 0.5f, -0.5f, -0.5f),
            new Vector3( 0.5f,  0.5f, -0.5f), new Vector3(-0.5f,  0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f,  0.5f), new Vector3( 0.5f, -0.5f,  0.5f),
            new Vector3( 0.5f,  0.5f,  0.5f), new Vector3(-0.5f,  0.5f,  0.5f)
        };

        int[] tri =
        {
            0, 2, 1, 0, 3, 2,   // -Z
            1, 6, 5, 1, 2, 6,   // +X
            5, 7, 4, 5, 6, 7,   // +Z
            4, 3, 0, 4, 7, 3,   // -X
            3, 6, 2, 3, 7, 6,   // +Y
            4, 1, 5, 4, 0, 1    // -Y
        };

        Mesh m = new Mesh { name = "FogVolumeCube", hideFlags = HideFlags.DontSave };
        m.vertices = v;
        m.triangles = tri;
        m.RecalculateNormals();

        // Границы задаём руками: меш масштабируется трансформом, и авторасчёт
        // ничего не испортит, но так надёжнее при Cull Front.
        m.bounds = new Bounds(Vector3.zero, Vector3.one);

        return m;
    }

    private Material GetMaterial()
    {
        if (_materialOverride != null)
        {
            _material = _materialOverride;
            return _material;
        }

        if (_material != null) return _material;

        Shader sh = Shader.Find("CatCooks/VolumeFog");
        if (sh == null)
        {
            Debug.LogError("[VolumeFog] Шейдер CatCooks/VolumeFog не найден. " +
                           "Добавь его в Always Included Shaders или задай " +
                           "Material Override.", this);
            return null;
        }

        _material = new Material(sh)
        {
            name = "VolumeFog (auto)",
            hideFlags = HideFlags.DontSave
        };

        return _material;
    }

    private void BuildSimulation()
    {
        if (_compute == null) return;

        _resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(_resolution), 32, 1024);
        _kernel = _compute.FindKernel("CSMain");
        _groups = Mathf.CeilToInt(_resolution / 8f);

        _density = new RenderTexture[2];

        for (int i = 0; i < 2; i++)
        {
            _density[i] = new RenderTexture(_resolution, _resolution, 0,
                                            RenderTextureFormat.RFloat)
            {
                name = $"FogDensity_{i}",
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            _density[i].Create();
        }

        _cpuData = new DisturberData[Mathf.Max(1, _maxDisturbers)];
        _buffer = new ComputeBuffer(_cpuData.Length, DisturberStride);

        _current = 0;
        _simReady = true;
    }

    // ───────────────────────── цикл ─────────────────────────

    private void LateUpdate()
    {
        float dt = Application.isPlaying ? Time.deltaTime : 0.02f;

        // Режим и сила едут независимо: можно держать «песок» и плавно
        // поднимать или опускать силу, не трогая переход между режимами.
        float targetBlend = _mode == Mode.Sand ? 1f : 0f;
        _blend = Mathf.MoveTowards(_blend, targetBlend, dt / Mathf.Max(0.1f, _modeSeconds));

        _intensity = Mathf.SmoothDamp(_intensity, _targetIntensity, ref _intensityVel,
                                      _intensitySeconds);

        SampleWind();
        TickGust(dt);
        FollowCamera();

        _rescanTimer -= dt;
        if (_rescanTimer <= 0f) { Rescan(); _rescanTimer = _rescanInterval; }

        if (_simReady && Application.isPlaying) Step(dt);

        PushMaterial();

        _gustReadout = _gust;
    }

    private void SampleWind()
    {
        if (_wind != null)
        {
            Vector3 w = _wind.SampleWind(transform.position);
            _windStrength = Mathf.Max(0.05f, w.magnitude);
            w.y = 0f;

            if (w.sqrMagnitude > 0.0001f) { _windDir = w.normalized; return; }
        }
        else _windStrength = 1f;

        Vector3 f = _fallbackDirection;
        f.y = 0f;
        _windDir = f.sqrMagnitude < 0.0001f ? Vector3.right : f.normalized;
    }

    /// <summary>
    /// Порыв из шума Перлина, а не из синуса: синус даёт правильную волну,
    /// которая через минуту наблюдения становится заметной и перестаёт
    /// читаться как ветер.
    ///
    /// Резкость важнее, чем кажется: ветер узнаётся по ЧЕРЕДОВАНИЮ удара и
    /// затишья. При единице среда просто равномерно дышит.
    /// </summary>
    private void TickGust(float dt)
    {
        if (!_gustsEnabled) { _gust = 0f; return; }

        float f = _gustsPerMinute / 60f;
        float n = Mathf.PerlinNoise(_gustSeed + Time.time * f, 0.37f);

        // Перлин держится около половины и краёв почти не достаёт —
        // растягиваем, иначе амплитуда вдвое меньше заданной.
        n = Mathf.Clamp01((n - 0.25f) * 2f);
        n = Mathf.Pow(n, _gustSharpness);

        if (_gustImpulse > 0.001f) _gustImpulse -= _gustImpulse * _gustImpulseDecay * dt;
        else _gustImpulse = 0f;

        _gust = Mathf.Clamp01(_gustBase + n * _gustAmplitude + _gustImpulse);
    }

    private void FollowCamera()
    {
        if (_followCamera)
        {
            if (_cam == null)
            {
                Camera c = Camera.main;
#if UNITY_EDITOR
                if (c == null && !Application.isPlaying && _castOnSceneView &&
                    UnityEditor.SceneView.lastActiveSceneView != null)
                    c = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
                if (c != null) _cam = c.transform;
            }

            if (_cam != null)
            {
                Vector3 p = _cam.position;

                // ПРИВЯЗКА К СЕТКЕ. Без неё поле симуляции ползёт вместе с
                // камерой непрерывно, и клочья тумана выглядят приклеенными к
                // игроку: он движется, а рисунок стоит на месте относительно
                // экрана. Перескок раз в несколько метров глаз не ловит.
                if (_followSnap > 0.01f)
                {
                    p.x = Mathf.Round(p.x / _followSnap) * _followSnap;
                    p.z = Mathf.Round(p.z / _followSnap) * _followSnap;
                }

                transform.position = new Vector3(p.x, transform.position.y, p.z);
            }
        }

        ApplyBoxTransform();
    }

    private void ApplyBoxTransform()
    {
        if (_box == null) return;

        _box.localPosition = new Vector3(0f, _groundOffset + _height * 0.5f, 0f);
        _box.localRotation = Quaternion.identity;
        _box.localScale = new Vector3(_areaSize.x, _height, _areaSize.y);
    }

    private void Rescan()
    {
        _disturbers.Clear();

        CatCooksFogDisturber[] found =
            Object.FindObjectsByType<CatCooksFogDisturber>(FindObjectsSortMode.None);

        // Ближние важнее: если возмутителей больше лимита, отбрасывать надо
        // дальних, а не случайных.
        Vector3 center = transform.position;

        System.Array.Sort(found, (a, b) =>
            (a.transform.position - center).sqrMagnitude
            .CompareTo((b.transform.position - center).sqrMagnitude));

        int limit = Mathf.Min(found.Length, _maxDisturbers);
        for (int i = 0; i < limit; i++)
            if (found[i].isActiveAndEnabled) _disturbers.Add(found[i]);
    }

    private void Step(float dt)
    {
        if (_compute == null || _density == null || _buffer == null) return;

        int next = 1 - _current;

        int count = 0;

        for (int i = 0; i < _disturbers.Count && count < _cpuData.Length; i++)
        {
            CatCooksFogDisturber d = _disturbers[i];
            if (d == null || !d.active || !d.isActiveAndEnabled) continue;

            Vector3 pos = d.transform.position;

            if (!d.initialized) { d.previousPosition = pos; d.initialized = true; }

            Vector3 delta = pos - d.previousPosition;
            d.previousPosition = pos;

            Vector2 uv = WorldToUv(pos);
            Vector2 velUv = dt > 0.0001f
                ? new Vector2(delta.x / _areaSize.x, delta.z / _areaSize.y) / dt
                : Vector2.zero;

            // Радиус в UV. Зона неквадратная, поэтому берём по большей
            // стороне: круг в мире не должен становиться эллипсом в поле.
            float radiusUv = d.radius / Mathf.Max(_areaSize.x, _areaSize.y);

            _cpuData[count] = new DisturberData
            {
                position = uv,
                velocity = velUv,
                radius = Mathf.Max(0.001f, radiusUv),
                push = d.push * d.strength,
                swirl = d.swirl * d.strength,
                carve = d.carve * d.strength
            };

            count++;
        }

        _buffer.SetData(_cpuData);

        Profile p = _blended;

        _compute.SetInt("_Resolution", _resolution);
        _compute.SetVector("_Wind", WindUv(p));
        _compute.SetFloat("_Regrow", p.regrow);
        _compute.SetFloat("_NoiseScale", p.simNoiseScale);
        _compute.SetFloat("_NoiseDetail", p.simNoiseDetail);
        _compute.SetFloat("_Patchiness", p.patchiness);
        _compute.SetFloat("_Gust", _gust);
        _compute.SetFloat("_Time", Time.time);
        _compute.SetFloat("_Dt", dt);
        _compute.SetInt("_DisturberCount", count);
        _compute.SetBuffer(_kernel, "_Disturbers", _buffer);
        _compute.SetTexture(_kernel, "_Source", _density[_current]);
        _compute.SetTexture(_kernel, "_Result", _density[next]);

        _compute.Dispatch(_kernel, _groups, _groups, 1);

        _current = next;
    }

    private Vector2 WindUv(Profile p)
    {
        Vector3 w = _windDir * (_baseWindSpeed * _windStrength * p.windScale);
        return new Vector2(w.x / _areaSize.x, w.z / _areaSize.y);
    }

    private Vector2 WorldToUv(Vector3 worldPos)
    {
        Vector3 local = worldPos - transform.position;
        return new Vector2(local.x / _areaSize.x + 0.5f, local.z / _areaSize.y + 0.5f);
    }

    private void PushMaterial()
    {
        Material m = GetMaterial();
        if (m == null) return;

        Profile.Lerp(_fogProfile, _sandProfile, _blend, _blended);
        Profile p = _blended;

        if (_density != null && _density[_current] != null)
        {
            m.SetTexture(DensityTexId, _density[_current]);
            m.SetFloat(SimAmountId, 1f);
        }
        else
        {
            // Симуляции нет — работаем на одном процедурном шуме. Туман
            // остаётся, просто перестаёт реагировать на объекты.
            m.SetFloat(SimAmountId, 0f);
        }

        m.SetColor(ColorTopId, p.colorTop);
        m.SetColor(ColorBottomId, p.colorBottom);
        m.SetColor(SunTintId, p.sunTint);

        // Сила гасит только ПЛОТНОСТЬ, а не цвет: если гасить цветом, среда
        // при затухании сереет вместо того чтобы редеть.
        m.SetFloat(ExtinctionId, CurrentExtinction);

        m.SetFloat(ContrastId, p.contrast);
        m.SetFloat(ClearHolesId, p.clearHoles);
        m.SetFloat(MacroAmountId, p.macroAmount);
        m.SetFloat(MacroScaleId, p.macroScale);

        m.SetFloat(EmissionId, p.emission);
        m.SetFloat(HeightFalloffId, p.heightFalloff);
        m.SetFloat(HeightCenterId, p.heightCenter);

        m.SetFloat(DetailAmountId, p.detailAmount);
        m.SetFloat(DetailScaleId, p.detailScale);
        m.SetFloat(DetailSpeedId, p.detailSpeed * (1f + _gust));
        m.SetFloat(AnisotropyId, p.anisotropy);

        m.SetVector(WindDirId, new Vector4(_windDir.x, 0f, _windDir.z, 0f));

        m.SetFloat(SunScatterId, p.sunScatter);
        m.SetFloat(SunSharpnessId, p.sunSharpness);

        m.SetFloat(StepsId, _steps);
        m.SetFloat(DitherId, _dither);
        m.SetFloat(EdgeFadeId, _edgeFade);

        if (_renderer != null) _renderer.enabled = _intensity > 0.002f;

        // ГЛОБАЛЬ ДЛЯ ОБВОДКИ И ДРУГИХ ЭФФЕКТОВ.
        //
        // Обводка объектов рисуется поверх прозрачки и туманом не гасится —
        // сквозь плотную пыль видны чёткие контуры техники, чего быть не
        // должно. Отсюда её можно погасить одной строкой в шейдере обводки:
        //
        //     alpha *= _CatFogOutlineFade;
        //
        // Значение считается по видимости на характерной дистанции сцены,
        // а не по позиции камеры: обводка гаснет тогда же, когда пропадает
        // сам объект.
        float refDist = Mathf.Max(_areaSize.x, _areaSize.y) * 0.35f;
        float fade = Mathf.Exp(-CurrentExtinction * refDist);

        Shader.SetGlobalFloat(OutlineFadeId, fade);
    }

    // ───────────────────── управление из игры ─────────────────────

    /// <summary>
    /// Переключить режим. Переход непрерывный: в середине это будет мутная
    /// пыльная дымка, а не две картинки сразу.
    ///
    ///   SetMode(Mode.Sand, 20f)  — туман над стройкой превращается в бурю
    ///   SetMode(Mode.Fog, 40f)   — буря оседает в утреннюю дымку
    /// </summary>
    public void SetMode(Mode mode, float seconds = -1f)
    {
        _mode = mode;
        if (seconds > 0f) _modeSeconds = seconds;
    }

    /// <summary>Общая сила, 0..1. Отдельная ручка от режима.</summary>
    public void SetIntensity(float value, float seconds = -1f)
    {
        _targetIntensity = Mathf.Clamp01(value);
        if (seconds > 0f) _intensitySeconds = seconds;
    }

    public void SetIntensityImmediate(float value)
    {
        _intensity = _targetIntensity = Mathf.Clamp01(value);
        _intensityVel = 0f;
    }

    /// <summary>Ручной порыв поверх автоматики.</summary>
    public void Gust(float strength01 = 1f, float seconds = 2.5f)
    {
        _gustImpulse = Mathf.Max(_gustImpulse, Mathf.Clamp01(strength01));
        _gustImpulseDecay = 3f / Mathf.Max(0.1f, seconds);
    }

    /// <summary>Мгновенно поставить режим без перехода.</summary>
    public void SetModeImmediate(Mode mode)
    {
        _mode = mode;
        _blend = mode == Mode.Sand ? 1f : 0f;
    }

    /// <summary>Пересобрать список возмутителей. Зови после того, как в сцене
    /// появилась новая техника: автоматический пересбор идёт раз в две
    /// секунды.</summary>
    public void RefreshDisturbers() => Rescan();

    /// <summary>Сменить размер зоны на лету.</summary>
    public void SetArea(Vector2 size, float height = -1f)
    {
        _areaSize = new Vector2(Mathf.Max(1f, size.x), Mathf.Max(1f, size.y));
        if (height > 0f) _height = height;
        ApplyBoxTransform();
    }

    /// <summary>Находится ли точка внутри объёма.</summary>
    public bool IsInside(Vector3 worldPos)
    {
        Vector3 d = worldPos - transform.position;
        float bottom = _groundOffset;

        return Mathf.Abs(d.x) <= _areaSize.x * 0.5f
            && Mathf.Abs(d.z) <= _areaSize.y * 0.5f
            && d.y >= bottom && d.y <= bottom + _height;
    }

    /// <summary>
    /// ОЦЕНКА СВЕТОПРОПУСКАНИЯ от камеры до точки: 1 — видно ясно, 0 — не
    /// видно совсем.
    ///
    /// Считается на CPU по средней экстинкции и высоте, без обращения к полю
    /// симуляции: оно живёт на GPU, и читать его обратно каждый кадр стоило бы
    /// дороже всего остального вместе взятого. Для затухания обводки точности
    /// с запасом.
    /// </summary>
    public float TransmittanceAt(Vector3 worldPos)
    {
        float ext = CurrentExtinction;
        if (ext < 1e-5f) return 1f;

        Vector3 eye = _cam != null ? _cam.position
                                   : (Camera.main != null ? Camera.main.transform.position
                                                          : worldPos);

        float dist = Vector3.Distance(eye, worldPos);

        // Высотный множитель берём на середине пути — тот же спад, что в
        // шейдере, только в одной точке вместо шестнадцати.
        float midY = (eye.y + worldPos.y) * 0.5f;
        float local = midY - (transform.position.y + _groundOffset + _height * 0.5f);
        float hn = local / Mathf.Max(0.01f, _height);

        float h = Mathf.Exp(-(hn - _blended.heightCenter) * _blended.heightFalloff * 2f);

        return Mathf.Exp(-ext * Mathf.Clamp01(h) * dist);
    }

    // ───────────────────────── отладка ─────────────────────────

    [ContextMenu("Пересобрать")]
    private void EditorRebuild() => Build();

    [ContextMenu("Режим: туман")]
    private void EditorFog() => SetMode(Mode.Fog);

    [ContextMenu("Режим: песок")]
    private void EditorSand() => SetMode(Mode.Sand);

    [ContextMenu("Порыв")]
    private void EditorGust() => Gust(1f, 3f);

    private void OnValidate()
    {
        _areaSize.x = Mathf.Max(1f, _areaSize.x);
        _areaSize.y = Mathf.Max(1f, _areaSize.y);

        ApplyBoxTransform();

        if (Application.isPlaying) PushMaterial();
    }

    private void OnGUI()
    {
        if (!_showReadout) return;

        GUI.Label(new Rect(10, 420, 640, 76),
            $"ТУМАН  режим {_mode}   переход {_blend:0.00}   сила {_intensity:0.00} → {_targetIntensity:0.00}\n" +
            $"порыв {_gust:0.00}   ветер {_windStrength:0.0}   " +
            $"видимость {CurrentVisibility:0} м\n" +
            $"симуляция {(_simReady ? _resolution + "²" : "ВЫКЛ (нет compute)")}   " +
            $"возмутителей {_disturbers.Count}   шагов {_steps}");
    }

    private void OnDrawGizmosSelected()
    {
        if (!_drawGizmos) return;

        Gizmos.color = new Color(0.6f, 0.85f, 1f, 0.6f);
        Gizmos.DrawWireCube(
            transform.position + Vector3.up * (_groundOffset + _height * 0.5f),
            new Vector3(_areaSize.x, _height, _areaSize.y));

        Gizmos.color = new Color(1f, 0.85f, 0.4f, 0.95f);
        Gizmos.DrawRay(transform.position, _windDir * (8f + _gust * 10f));
    }
}
