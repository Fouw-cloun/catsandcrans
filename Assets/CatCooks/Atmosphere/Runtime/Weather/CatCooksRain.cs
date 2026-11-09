using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// ============================================================================
//  CatCooksRain — дождь с плавной интенсивностью.
//
//  УСТРОЙСТВО
//
//   Струи: ОДИН статичный меш из нескольких тысяч квадов, разложенных
//   по колонне вокруг камеры. Падение и зацикливание считает вершинный
//   шейдер по времени — процессор в этом не участвует вообще. Через
//   ParticleSystem пришлось бы гонять тысячи частиц каждый кадр.
//
//   Интенсивность: у каждого квада в вершинных данных лежит «ранг» 0…1.
//   Квады с рангом выше порога схлопываются в точку прямо в шейдере.
//   Плотность меняется одним числом, без пересборки меша и аллокаций.
//
//   Всплески: пул плоских колец. Каждое периодически стреляет лучом вниз,
//   находит землю и играет расходящийся круг. Лучей десятки в секунду,
//   а не тысячи, и работает на любом рельефе — привязки к плоскости нет.
//
//  ЗАЧЕМ ВСПЛЕСКИ. Дождь без них читается как полосы поверх картинки.
//  Контакт с землёй — то, что связывает его со сценой.
//
//  Сеть: косметика, НЕ NetworkObject. Всё детерминировано по времени.
// ============================================================================
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("CatCooks/Atmosphere/Rain")]
public class CatCooksRain : MonoBehaviour
{
    [Header("Материалы")]
    [Tooltip("Материал со шейдером CatCooks/Rain, Mode = 0")]
    public Material streakMaterial;

    [Tooltip("Материал со шейдером CatCooks/Rain, Mode = 1. Пусто — всплесков нет.")]
    public Material splashMaterial;

    [Header("Интенсивность")]
    [Tooltip("0 = сухо, 1 = ливень. Ведёт погодный пресет.")]
    [Range(0f, 1f)] public float intensity = 0f;

    [Tooltip("За сколько секунд дождь набирает или теряет силу. " +
             "Мгновенное включение читается как баг.")]
    public float rampSeconds = 4f;

    public enum Anchor
    {
        /// <summary>Область вокруг ЭТОГО объекта. Ничего не ищет.</summary>
        ObjectArea,
        /// <summary>Едет за целью из поля Follow Target. Для коопа:
        /// каждый клиент ставит туда СВОЕГО игрока.</summary>
        FollowTarget,
        /// <summary>Едет за Camera.main. Только для одиночной игры.</summary>
        FollowCamera
    }

    [Header("Область")]
    [Tooltip("ObjectArea — дождь идёт вокруг этого объекта и никуда " +
             "не привязан. ЭТО ПРАВИЛЬНЫЙ РЕЖИМ ДЛЯ КООПА: Camera.main " +
             "в сцене с несколькими игроками указывает на случайную " +
             "камеру, и дождь начинает ездить за чужим экраном.")]
    public Anchor anchor = Anchor.ObjectArea;

    [Tooltip("За кем едет дождь в режиме FollowTarget. В КООПЕ каждый " +
             "клиент выставляет сюда СВОЕГО игрока — вызовом SetTarget " +
             "из скрипта игрока при спавне, когда IsOwner. Дождь " +
             "не синхронизируется по сети, он у каждого свой и рисуется " +
             "только вокруг его камеры.")]
    public Transform followTarget;

    [Tooltip("Камера — только для режима FollowCamera. Пусто — Camera.main.")]
    public Camera targetCamera;

    [Tooltip("Если цель не назначена — найти объект по ТЕГУ.\n\n" +
             "Это надёжнее поиска камеры: тег ставится осознанно и ровно " +
             "на то, что нужно, а камер в сцене десятки — техника, " +
             "префабы, выключенные камеры чужих игроков.")]
    public bool findByTag = true;

    [Tooltip("Тег объекта игрока")]
    public string playerTag = "Player";

    [Tooltip("Совсем запасной путь: активная камера. Промахивается " +
             "на камерах техники, держи выключенным.")]
    public bool autoFollowLocalCamera = false;

    [Tooltip("Радиус области, м. В режиме ObjectArea это реальный размер " +
             "зоны дождя на карте — ставь под свою игровую площадку. " +
             "Увеличив радиус вдвое, поднимай Max Streaks ВЧЕТВЕРО: " +
             "площадь растёт квадратом. И наоборот — если жмёт по кадрам, уменьшай радиус, это дешевле, чем резать число капель.")]
    public float radius = 18f;

    [Tooltip("Высота колонны, м.")]
    public float height = 26f;

    [Tooltip("Максимум струй. 2000–4000 хватает: дальше они всё равно " +
             "сливаются в сетку.")]
    [Range(200, 20000)] public int maxStreaks = 2200;

    [Header("Струи")]
    public float fallSpeed = 22f;
    public Vector2 streakLength = new Vector2(0.5f, 1.4f);
    public Vector2 streakWidth = new Vector2(0.012f, 0.03f);

    [Tooltip("Наклон от ветра. Берётся из глобального ветра, если включено.")]
    public bool useGlobalWind = true;

    [Tooltip("УГОЛ ПАДЕНИЯ в градусах от вертикали ПРИ ПОЛНОМ ВЕТРЕ.\n\n" +
             "0 — отвесно, 15–25 — обычный дождь с ветром, 40+ — шторм, " +
             "когда капли летят почти горизонтально.\n\n" +
             "Это ПОТОЛОК, а не постоянный угол: при слабом ветре наклон " +
             "пропорционально меньше. Хочешь строго отвесный дождь всегда " +
             "— оставь ноль здесь ИЛИ сними галку Use Global Wind: без " +
             "неё ветер на дождь не влияет вовсе.\n\n" +
             "По умолчанию ноль: отвесный дождь читается чище, а наклон " +
             "стоит включать осознанно, под конкретную погоду.")]
    [Range(0f, 65f)] public float slantDegrees = 0f;

    [Tooltip("Старое поле-множитель. Оставлено, чтобы не сбросить " +
             "настройки существующих сцен; итоговый наклон = угол × это.")]
    [Range(0f, 2f)] public float slantAmount = 1f;

    [Header("Всплески")]
    [Tooltip("Старые кольца на пуле GameObject. ВЫКЛЮЧЕНО по умолчанию: " +
             "их заменил компонент CatCooksRainSplashes на встроенных " +
             "частицах. Две системы разом дадут двойные кольца.")]
    public bool legacySplashes = false;

    [Tooltip("Пул всплесков. Пятьдесят на радиус в десять метров — это " +
             "одиночные кольца, которые глаз читает как случайный мусор, " +
             "а не как дождь. Для ливня нужно от двухсот.")]
    [Range(0, 600)] public int maxSplashes = 260;

    [Tooltip("Всплесков в секунду при полной интенсивности.")]
    public float splashRate = 55f;

    [Tooltip("Радиус пятна всплесков, м. 0 = взять от радиуса дождя — " +
             "так кольца ложатся по всей колонне, а не в пятне вдвое " +
             "меньше неё. Дальние кольца всё равно съедаются затуханием.")]
    public float splashRadius = 0f;

    public float splashSize = 0.35f;
    public float splashLife = 0.45f;

    [Tooltip("Уровень земли по Y. Всплески ложатся на эту высоту вокруг " +
             "игрока. Никаких лучей — на глаз всё равно не видно, куда " +
             "именно упала капля, а рейкасты стоят процессорного времени " +
             "и молча ничего не дают, если у земли нет коллайдера.")]
    public float groundLevel = 0f;

    [Tooltip("Подобрать уровень земли одним лучом при старте. Если " +
             "коллайдера нет — останется значение из поля выше.")]
    public bool autoGroundLevel = true;

    [Tooltip("По каким слоям искать землю при автоподборе.")]
    public LayerMask groundMask = ~0;

    [Tooltip("Разброс высоты всплесков, м. Нужен на неровной земле, " +
             "чтобы кольца не лежали в одной плоскости как по линейке.")]
    public float groundJitter = 0.6f;

    [Tooltip("Класть всплески НА РЕЛЬЕФ. Выключено — все кольца лежат " +
             "на одной высоте, и на склоне половина висит в воздухе, " +
             "а половина уходит под землю.")]
    public bool followGround = true;

    // ====================================================================
    //  ХАРАКТЕР ДОЖДЯ
    //  Одной интенсивности мало: морось, ливень и косой шторм отличаются
    //  не количеством капель, а их РАЗМЕРОМ, СКОРОСТЬЮ и НАКЛОНОМ.
    //  Меняя только intensity, получаешь один и тот же дождь то гуще,
    //  то реже — ровно то, что выглядит однообразно.
    // ====================================================================
    [Header("Характер")]
    [Tooltip("0 — морось: короткие медленные нити, почти без всплесков. " +
             "1 — ливень: длинные быстрые струи и сплошная рябь.")]
    [Range(0f, 1f)] public float harshness = 0.5f;

    [Tooltip("Сила бокового сноса поверх общего ветра. Косой дождь " +
             "читается как шторм даже при той же плотности.")]
    [Range(0f, 1f)] public float gust = 0f;

    [Tooltip("Блеск капель. В грозу под тучей света мало и капли тусклее, " +
             "в солнечный ливень они вспыхивают.")]
    [Range(0f, 2f)] public float sheen = 1f;

    public int seed = 4242;

    // ───────── внутреннее ─────────
    private GameObject _streakGO;
    private MeshRenderer _streakMR;
    private Mesh _streakMesh;
    private int _builtCount = -1;

    private class Splash
    {
        public Transform t;
        public MeshRenderer mr;
        public float age;
        public bool alive;
    }

    private readonly List<Splash> _splashes = new List<Splash>();
    private Mesh _splashMesh;
    private MaterialPropertyBlock _mpb;
    private System.Random _rng;
    private float _current;
    private float _splashTimer;
    private Vector3 _slant;               // снос за высоту колонны
    private System.Random _bodyRng = new System.Random(9173);
    private Vector3 _centre;
    private float _findTimer;
    private double _lastEditorTime;

    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    static readonly int SlantId = Shader.PropertyToID("_Slant");
    static readonly int SpeedId = Shader.PropertyToID("_FallSpeed");
    static readonly int BoxId = Shader.PropertyToID("_BoxHeight");
    static readonly int GroundYId = Shader.PropertyToID("_GroundY");
    static readonly int SplashTId = Shader.PropertyToID("_SplashT");
    static readonly int SpraySplashId = Shader.PropertyToID("_SplashSpray");
    static readonly int LenScaleId = Shader.PropertyToID("_LenScale");
    static readonly int WidScaleId = Shader.PropertyToID("_WidScale");
    static readonly int WindDirId = Shader.PropertyToID("_CatWindDir");
    static readonly int WindStrId = Shader.PropertyToID("_CatWindStrength");

    // ========================================================== жизненный цикл
    void OnEnable()
    {
        _mpb = new MaterialPropertyBlock();
        _rng = new System.Random(seed);
        Build();
#if UNITY_EDITOR
        _lastEditorTime = EditorApplication.timeSinceStartup;
        EditorApplication.update -= EditorTick;
        EditorApplication.update += EditorTick;
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
#endif
        Clear();
    }

    void OnValidate()
    {
        maxStreaks = Mathf.Clamp(maxStreaks, 200, 20000);
#if UNITY_EDITOR
        EditorApplication.delayCall += () =>
        {
            if (this == null || !isActiveAndEnabled) return;
            if (_builtCount != maxStreaks) Build();
        };
#endif
    }

    void LateUpdate()
    {
        // Пул создаётся один раз при сборке, поэтому смена maxSplashes
        // в инспекторе раньше не давала ничего до перезапуска сцены.
        if (legacySplashes && _splashes.Count != maxSplashes
            && splashMaterial != null) Build();
        if (Application.isPlaying) Tick(Time.deltaTime);
    }

#if UNITY_EDITOR
    void EditorTick()
    {
        if (Application.isPlaying || this == null || !isActiveAndEnabled) return;
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - _lastEditorTime);
        _lastEditorTime = now;
        if (dt <= 0f || dt > 0.5f) return;
        Tick(dt);
        SceneView.RepaintAll();
    }
#endif

    // ================================================================== кадр
    void Tick(float dt)
    {
        if (_builtCount != maxStreaks) Build();

        // Плавный набор силы. Мгновенное включение ливня читается как баг.
        _current = Mathf.MoveTowards(_current, Mathf.Clamp01(intensity),
                                     dt / Mathf.Max(rampSeconds, 0.05f));

        // ЦЕНТР ОБЛАСТИ. В режиме ObjectArea — позиция самого компонента,
        // и никакого поиска камер: в коопе Camera.main указывает на
        // случайного игрока, и дождь уезжал за чужим экраном.
        Vector3 centre = transform.position;

        // Цель могла ещё не заспавниться — ищем её периодически,
        // а не один раз при старте.
        if (anchor == Anchor.FollowTarget && followTarget == null)
        {
            // Ищем периодически, а не один раз: игрок в коопе спавнится
            // позже сцены, и на старте его ещё нет.
            _findTimer -= dt;
            if (_findTimer <= 0f)
            {
                _findTimer = 0.5f;

                if (findByTag && !string.IsNullOrEmpty(playerTag))
                {
                    var go = GameObject.FindGameObjectWithTag(playerTag);
                    if (go != null) followTarget = go.transform;
                }

                if (followTarget == null && autoFollowLocalCamera)
                    followTarget = FindLocalCamera();
            }
        }

        if (anchor == Anchor.FollowTarget && followTarget != null)
        {
            centre = followTarget.position;
        }
        else if (anchor == Anchor.FollowCamera)
        {
            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam != null) centre = cam.transform.position;
        }

#if UNITY_EDITOR
        // В редакторе без Play смотрим от вида сцены, иначе колонну
        // не видно и настраивать нечего.
        if (!Application.isPlaying && anchor != Anchor.ObjectArea
            && SceneView.lastActiveSceneView != null)
            centre = SceneView.lastActiveSceneView.camera.transform.position;
#endif

        bool on = _current > 0.002f;
        if (_streakGO != null && _streakGO.activeSelf != on) _streakGO.SetActive(on);
        if (!on) { KillAllSplashes(); return; }

        if (autoGroundLevel) AutoGround(centre);

        // По вертикали объект не двигаем: высоту капель задаёт _GroundY
        // в шейдере. Иначе колонна ездила бы вверх-вниз вместе с прыжками
        // игрока, и дождь дёргался бы по высоте.
        if (_streakGO != null)
            _streakGO.transform.position = new Vector3(centre.x, groundLevel, centre.z);
        _centre = centre;

        // Ветер наклоняет струи.
        Vector3 slant = Vector3.zero;   // копится ниже, см. _slant
        if (useGlobalWind)
        {
            Vector4 w = Shader.GetGlobalVector(WindDirId);
            float str = Mathf.InverseLerp(0.12f, 1.9f, Shader.GetGlobalFloat(WindStrId));
            // Наклон = тангенс угла: именно он даёт горизонтальный снос
            // на единицу падения, а значит и настоящий угол струи.
            float tan = Mathf.Tan(slantDegrees * Mathf.Deg2Rad) * slantAmount;
            slant = new Vector3(w.x, 0f, w.z).normalized * (str * tan);
        }

        if (streakMaterial != null)
        {
            // ХАРАКТЕР. Морось — короткие медленные нити, ливень —
            // длинные быстрые. Это те же капли, но читаются как разная
            // погода, чего одной плотностью не добиться.
            float speed = fallSpeed * Mathf.Lerp(0.55f, 1.45f, harshness);

            // Порыв добавляет сноса поверх общего ветра.
            //
            // slant лежит в плоскости XZ, а усиливались x и Y — то есть
            // икс и жёсткий ноль. Половина сноса не усиливалась вовсе,
            // и при ветре вдоль Z порыв не делал ничего. Шейдер читает
            // _Slant.x и _Slant.z, их и правим.
            // Порыв УСИЛИВАЕТ наклон, но не создаёт его: при нулевом
            // угле умножать нечего, и дождь остаётся отвесным даже
            // в шторм. Так галка «отвесно» действительно означает
            // отвесно, а не «отвесно, пока не задует».
            Vector4 sl = slant;
            float gustMul = 1f + gust * 1.8f;
            sl.x *= gustMul;
            sl.z *= gustMul;

            streakMaterial.SetFloat(IntensityId, _current);
            streakMaterial.SetFloat(SpeedId, speed);
            streakMaterial.SetFloat(BoxId, height);
            streakMaterial.SetVector(SlantId, sl);
            // Запоминаем итоговый снос: капля при косом дожде приземляется
            // не под колонной, а в стороне, и всплески обязаны лечь ТУДА ЖЕ.
            _slant = new Vector3(sl.x, 0f, sl.z);
            streakMaterial.SetFloat(LenScaleId, Mathf.Lerp(0.45f, 1.6f, harshness));
            streakMaterial.SetFloat(WidScaleId, Mathf.Lerp(0.7f, 1.35f, harshness));
            if (streakMaterial.HasProperty("_CoreBright"))
                streakMaterial.SetFloat("_CoreBright", Mathf.Lerp(0.4f, 2.2f, sheen * 0.5f));
            // Низ колонны — уровень земли. Тот же, на который ложатся
            // всплески: капля гаснет ровно там, где появляется кольцо.
            streakMaterial.SetFloat(GroundYId, groundLevel);
        }

        RefreshGround(centre);
        TickSplashes(dt);
    }

    // ================================================================ всплески
    void TickSplashes(float dt)
    {
        if (!legacySplashes) { KillAllSplashes(); return; }
        if (splashMaterial == null || _splashes.Count == 0) return;

        for (int i = 0; i < _splashes.Count; i++)
        {
            var s = _splashes[i];
            if (!s.alive) continue;

            s.age += dt;
            if (s.age >= splashLife) { s.alive = false; s.t.gameObject.SetActive(false); continue; }

            s.mr.GetPropertyBlock(_mpb);
            _mpb.SetFloat(SplashTId, s.age / splashLife);
            // Корона брызг растёт с силой дождя: морось даёт плоское
            // кольцо, ливень выбивает вверх заметный фонтанчик.
            _mpb.SetFloat(SpraySplashId, Mathf.Lerp(0.15f, 1f, harshness));
            s.mr.SetPropertyBlock(_mpb);
        }

        _splashTimer -= dt;
        // Квадрат: при слабом дожде редкие одиночные кольца, при ливне
        // сплошная рябь. Линейная связь давала слишком много всплесков
        // на мороси.
        // Морось почти не всплёскивает, ливень бьёт сплошной рябью.
        float rate = splashRate * _current * _current
                   * Mathf.Lerp(0.3f, 1.8f, harshness);
        if (rate <= 0.01f) return;

        int guard = 0;
        while (_splashTimer <= 0f && guard++ < 20)
        {
            _splashTimer += 1f / rate;
            SpawnSplash();
        }
        if (_splashTimer < 0f) _splashTimer = 0f;
    }

    void SpawnSplash()
    {
        Splash s = null;
        for (int i = 0; i < _splashes.Count; i++)
            if (!_splashes[i].alive) { s = _splashes[i]; break; }
        if (s == null) return;

        // Точку берём равномерно ПО ПЛОЩАДИ. insideUnitCircle тоже
        // равномерен, но корень от квадрата радиуса читается яснее и
        // позволяет отодвинуть кольца от самых ног, где они всё равно
        // не видны из-за ближнего затухания.
        float ang = Random.value * Mathf.PI * 2f;
        float rad = Mathf.Sqrt(Mathf.Lerp(0.04f, 1f, Random.value)) * SplashR;
        // Сдвиг по сносу: при косом дожде колонна стоит над игроком,
        // а капли из неё приземляются в стороне. Кольца без этого
        // сдвига оказывались там, где ничего не падает.
        Vector3 land = _slant * height;
        float px = _centre.x + Mathf.Cos(ang) * rad + land.x;
        float pz = _centre.z + Mathf.Sin(ang) * rad + land.z;

        float y = SampleGround(px, pz) + (Random.value - 0.5f) * groundJitter;

        s.t.position = new Vector3(px, y, pz);
        s.t.rotation = Quaternion.identity;

        // Разнокалиберные кольца: одинаковые выдают штамп мгновенно.
        s.t.localScale = Vector3.one * splashSize
                       * Mathf.Lerp(0.55f, 1.5f, Random.value);
        s.age = 0f;
        s.alive = true;
        s.t.gameObject.SetActive(true);
    }

    // ============================================ высота земли под точкой
    //  ПОЧЕМУ СЕТКА, А НЕ ЛУЧ НА КАЖДЫЙ ВСПЛЕСК
    //   При ливне всплесков за секунду сотни. Столько рейкастов в кадр
    //   заметно на профайлере, а результат почти не меняется: земля под
    //   соседними каплями одна и та же. Держим сетку высот вокруг игрока
    //   и обновляем по несколько ячеек за кадр — рейкастов выходит
    //   единицы, а кольца ложатся на рельеф, а не висят на общей плоскости.
    private float[] _groundGrid;
    private const int GridSide = 16;
    private Vector2 _gridOrigin;
    private float _gridCell = 2f;
    private int _gridCursor;

    /// <summary>Рабочий радиус всплесков.</summary>
    float SplashR => splashRadius > 0.1f ? splashRadius : radius * 0.9f;

    void RefreshGround(Vector3 centre, int cellsPerFrame = 6)
    {
        if (!followGround) return;
        if (_groundGrid == null || _groundGrid.Length != GridSide * GridSide)
        {
            _groundGrid = new float[GridSide * GridSide];
            for (int i = 0; i < _groundGrid.Length; i++) _groundGrid[i] = float.NaN;
        }

        _gridCell = Mathf.Max(SplashR * 2f / GridSide, 0.5f);
        Vector2 want = new Vector2(centre.x - SplashR, centre.z - SplashR);

        // Сдвинулись больше чем на ячейку — сетка устарела целиком.
        if (Vector2.Distance(want, _gridOrigin) > _gridCell)
        {
            _gridOrigin = want;
            for (int i = 0; i < _groundGrid.Length; i++) _groundGrid[i] = float.NaN;
        }

        for (int n = 0; n < cellsPerFrame; n++)
        {
            int idx = _gridCursor++ % _groundGrid.Length;
            int gx = idx % GridSide, gz = idx / GridSide;
            float wx = _gridOrigin.x + (gx + 0.5f) * _gridCell;
            float wz = _gridOrigin.y + (gz + 0.5f) * _gridCell;

            if (Physics.Raycast(new Vector3(wx, centre.y + 40f, wz), Vector3.down,
                                out RaycastHit hit, 120f, groundMask,
                                QueryTriggerInteraction.Ignore))
                _groundGrid[idx] = hit.point.y;
            else
                _groundGrid[idx] = float.NaN;   // дыра: используем общий уровень
        }
    }

    float SampleGround(float wx, float wz)
    {
        // Запасной уровень: если игрок ушёл далеко по высоте — поднялся
        // на крышу, спустился в карьер — держим кольца под ним, а не там,
        // где земля была при старте.
        float fallback = Mathf.Abs(_centre.y - groundLevel) > 30f
                       ? _centre.y - 2f : groundLevel;
        if (!followGround || _groundGrid == null) return fallback;

        int gx = Mathf.Clamp(Mathf.FloorToInt((wx - _gridOrigin.x) / _gridCell), 0, GridSide - 1);
        int gz = Mathf.Clamp(Mathf.FloorToInt((wz - _gridOrigin.y) / _gridCell), 0, GridSide - 1);
        float v = _groundGrid[gx + gz * GridSide];
        return float.IsNaN(v) ? fallback : v;
    }

#if UNITY_EDITOR
    [ContextMenu("Проверить дождь")]
    void CheckRain()
    {
        var sb = new System.Text.StringBuilder("[Rain]\n");
        sb.AppendLine($"  режим {anchor}, центр {_centre}, интенсивность {_current:0.00}");
        sb.AppendLine($"  цель: {(followTarget != null ? followTarget.name : "НЕТ")}");
        sb.AppendLine(streakMaterial == null ? "  материал струй НЕ НАЗНАЧЕН"
                                             : $"  струи «{streakMaterial.name}»");

        // Самая частая причина «всплесков нет»: они есть, но лежат на
        // нулевой высоте, а карта поднята на сотню метров.
        if (splashMaterial == null) sb.AppendLine("  материал всплесков НЕ НАЗНАЧЕН ← колец не будет");
        else
        {
            float mode = splashMaterial.HasProperty("_Mode") ? splashMaterial.GetFloat("_Mode") : -1f;
            sb.AppendLine($"  всплески «{splashMaterial.name}», _Mode={mode:0} "
                        + (mode < 0.5f ? "← ДОЛЖНО БЫТЬ 1, иначе рисуются струи вместо колец" : ""));
        }

        int alive = 0;
        foreach (var sp in _splashes) if (sp.alive) alive++;
        sb.AppendLine($"  пул всплесков {_splashes.Count} (поле maxSplashes {maxSplashes}), живых {alive}");
        if (_splashes.Count != maxSplashes)
            sb.AppendLine("  ← пул не совпадает с полем: нажми «Пересобрать»");

        sb.AppendLine($"  groundLevel {groundLevel:0.0}, центр по Y {_centre.y:0.0}, "
                    + $"расхождение {Mathf.Abs(_centre.y - groundLevel):0.0} м");

        int hits = 0, holes = 0;
        if (_groundGrid != null)
            foreach (var g in _groundGrid) { if (float.IsNaN(g)) holes++; else hits++; }
        sb.AppendLine($"  сетка земли: попаданий {hits}, промахов {holes}"
                    + (hits == 0 ? "  ← луч не находит землю, проверь Ground Mask и коллайдеры" : ""));
        Debug.Log(sb.ToString(), this);
    }
#endif

    /// <summary>
    /// Характер дождя от погоды. Вызывается MoodDirector рядом с
    /// установкой интенсивности.
    /// </summary>
    public void SetCharacter(float harsh, float gustAmount, float sheenAmount)
    {
        harshness = Mathf.Clamp01(harsh);
        gust = Mathf.Clamp01(gustAmount);
        sheen = Mathf.Clamp(sheenAmount, 0f, 2f);
    }

    /// <summary>
    /// Активная камера = камера локального игрока. На клиенте чужие
    /// камеры выключены, включённой остаётся ровно одна — именно поэтому
    /// поиск по активности надёжнее, чем Camera.main: тег MainCamera
    /// висит на всех префабах игрока разом.
    /// </summary>
    static Transform FindLocalCamera()
    {
        Camera best = null;
        foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Exclude,
                                                    FindObjectsSortMode.None))
        {
            if (!c.isActiveAndEnabled) continue;
            if (c.cameraType != CameraType.Game) continue;   // не Scene View
            if (best == null || c.depth > best.depth) best = c;
        }
        return best != null ? best.transform : null;
    }

    /// <summary>Один луч вниз, чтобы подобрать уровень земли на старте.</summary>
    void AutoGround(Vector3 from)
    {
        if (!autoGroundLevel) return;

        if (Physics.Raycast(from + Vector3.up * 2f, Vector3.down,
                            out RaycastHit hit, 800f, groundMask,
                            QueryTriggerInteraction.Ignore))
        {
            groundLevel = hit.point.y;
            autoGroundLevel = false;   // нашли — дальше значение ручное
            return;
        }

        // ВОТ ПОЧЕМУ ВСПЛЕСКОВ МОГЛО НЕ БЫТЬ.
        // Раньше при первой же неудаче автоподбор выключался, а
        // groundLevel оставался нулём. В сцене, где игрок живёт на
        // высоте 160 м, кольца падали на сто шестьдесят метров под
        // землю — то есть их не было видно никогда.
        // Теперь: держим уровень чуть ниже центра области и продолжаем
        // пробовать, пока коллайдер не найдётся.
        groundLevel = from.y - 2f;
    }

    void KillAllSplashes()
    {
        foreach (var s in _splashes)
            if (s.alive) { s.alive = false; if (s.t != null) s.t.gameObject.SetActive(false); }
    }

    // ================================================================== сборка
    [ContextMenu("Пересобрать")]
    public void Build()
    {
        Clear();
        if (streakMaterial == null)
        {
            Debug.LogWarning("[Rain] Не назначен Streak Material.", this);
            return;
        }

        _rng = new System.Random(seed);
        BuildStreaks();
        BuildSplashPool();
        _builtCount = maxStreaks;
    }

    void BuildStreaks()
    {
        _streakGO = new GameObject("RainStreaks");
        _streakGO.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
        _streakGO.transform.SetParent(transform, false);

        int n = maxStreaks;
        var verts = new Vector3[n * 4];
        var uvs = new Vector2[n * 4];
        var cells = new Vector2[n * 4];
        var rank = new Vector2[n * 4];
        var tris = new int[n * 6];

        for (int i = 0; i < n; i++)
        {
            // Равномерно по кругу: sqrt убирает сгущение в центре,
            // которое даёт наивный случайный радиус.
            float ang = (float)_rng.NextDouble() * Mathf.PI * 2f;
            float rad = Mathf.Sqrt((float)_rng.NextDouble()) * radius;
            var cell = new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);

            float len = Mathf.Lerp(streakLength.x, streakLength.y, (float)_rng.NextDouble());
            float wid = Mathf.Lerp(streakWidth.x, streakWidth.y, (float)_rng.NextDouble());

            // Ранг решает, при какой интенсивности капля появится.
            // Ближние получают ранг пониже — при слабом дожде видно
            // редкие капли рядом, а не сетку по всему горизонту.
            float near = 1f - rad / Mathf.Max(radius, 0.01f);
            float rk = Mathf.Clamp01((float)_rng.NextDouble() * (1.15f - near * 0.35f));
            float phase = (float)_rng.NextDouble();

            int b = i * 4;
            verts[b + 0] = new Vector3(-wid, 0f, 0f);
            verts[b + 1] = new Vector3(wid, 0f, 0f);
            verts[b + 2] = new Vector3(-wid, len, 0f);
            verts[b + 3] = new Vector3(wid, len, 0f);
            uvs[b + 0] = new Vector2(0, 0); uvs[b + 1] = new Vector2(1, 0);
            uvs[b + 2] = new Vector2(0, 1); uvs[b + 3] = new Vector2(1, 1);
            for (int v = 0; v < 4; v++)
            {
                cells[b + v] = cell;
                rank[b + v] = new Vector2(rk, phase);
            }

            int t = i * 6;
            tris[t + 0] = b; tris[t + 1] = b + 2; tris[t + 2] = b + 1;
            tris[t + 3] = b + 2; tris[t + 4] = b + 3; tris[t + 5] = b + 1;
        }

        _streakMesh = new Mesh
        {
            name = "CatCooksRainStreaks",
            hideFlags = HideFlags.HideAndDontSave,
            indexFormat = n * 4 > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16
        };
        _streakMesh.vertices = verts;
        _streakMesh.uv = uvs;
        _streakMesh.SetUVs(1, new List<Vector2>(cells));
        _streakMesh.SetUVs(2, new List<Vector2>(rank));
        _streakMesh.triangles = tris;

        // Границы вручную: капли расставляет шейдер, посчитанные по
        // исходным вершинам границы не совпадут с тем, что рисуется,
        // и дождь начнёт пропадать при повороте камеры.
        // Границы вручную и с запасом: капли расставляет шейдер, и
        // посчитанные по исходным вершинам границы не совпадут с тем,
        // что рисуется. Центр смещён вверх — колонна растёт от земли.
        _streakMesh.bounds = new Bounds(new Vector3(0f, height * 0.5f, 0f),
            new Vector3(radius * 2.4f, height * 1.4f, radius * 2.4f));

        _streakGO.AddComponent<MeshFilter>().sharedMesh = _streakMesh;
        _streakMR = _streakGO.AddComponent<MeshRenderer>();
        _streakMR.sharedMaterial = streakMaterial;
        _streakMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _streakMR.receiveShadows = false;
        _streakMR.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _streakGO.SetActive(false);
    }

    void BuildSplashPool()
    {
        if (!legacySplashes || splashMaterial == null) return;

        _splashMesh = new Mesh { name = "RainSplashQuad", hideFlags = HideFlags.HideAndDontSave };
        _splashMesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
            new Vector3(-0.5f, 0f,  0.5f), new Vector3(0.5f, 0f,  0.5f)
        };
        _splashMesh.uv = new[]
        {
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1)
        };
        _splashMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        _splashMesh.RecalculateBounds();

        for (int i = 0; i < maxSplashes; i++)
        {
            var go = new GameObject($"Splash_{i:00}");
            go.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
            go.transform.SetParent(transform, false);
            go.SetActive(false);

            go.AddComponent<MeshFilter>().sharedMesh = _splashMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = splashMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

            _splashes.Add(new Splash { t = go.transform, mr = mr });
        }
    }

    void Clear()
    {
        _splashes.Clear();
        if (_streakMesh != null) DestroySafe(_streakMesh);
        if (_splashMesh != null) DestroySafe(_splashMesh);
        _streakMesh = null; _splashMesh = null;

        var kill = new List<GameObject>();
        foreach (Transform t in transform) kill.Add(t.gameObject);
        foreach (var g in kill) DestroySafe(g);

        _streakGO = null;
        _builtCount = -1;
    }

    static void DestroySafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    /// <summary>Дёргать из погодных пресетов: 0 = сухо, 1 = ливень.</summary>
    public void SetIntensity(float t) => intensity = Mathf.Clamp01(t);

#if UNITY_EDITOR
    [ContextMenu("Проверить настройку")]
    void CheckSetup()
    {
        var sb = new System.Text.StringBuilder("[Rain] Проверка\n");
        sb.AppendLine($"  Intensity {intensity:0.00} (текущая {_current:0.00})");
        sb.AppendLine($"  режим {anchor}, цель: " +
                      (followTarget != null ? followTarget.name : "НЕТ"));
        sb.AppendLine($"  струй построено: {(_streakGO != null ? maxStreaks : 0)}, " +
                      $"объект активен: {(_streakGO != null && _streakGO.activeSelf)}");
        sb.AppendLine($"  всплесков в пуле: {_splashes.Count}, " +
                      $"уровень земли Y={groundLevel:0.0}");

        if (streakMaterial == null)
            sb.AppendLine("  ОШИБКА: не назначен Streak Material — дождь " +
                          "не строится вообще.");
        if (splashMaterial == null)
            sb.AppendLine("  ВНИМАНИЕ: не назначен Splash Material — колец не будет.");
        if (intensity <= 0.001f)
            sb.AppendLine("  Intensity в нуле. Если ведёт пульт — проверь, " +
                          "что у текущей погоды поле Rain не ноль, и что " +
                          "в Mood Director заполнена ссылка Rain.");

        Debug.Log(sb.ToString(), this);
    }

    [ContextMenu("Тест: ливень")]
    void TestRain() { intensity = 1f; _current = 1f; }

    void OnDrawGizmosSelected()
    {
        // Показываем реальную зону: где на самом деле идёт дождь и на
        // какой высоте лежат кольца. Без этого «странное позиционирование»
        // невозможно ни увидеть, ни объяснить.
        Vector3 c = Application.isPlaying ? _centre : transform.position;
        Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.9f);
        DrawCircle(new Vector3(c.x, groundLevel, c.z), radius);
        DrawCircle(new Vector3(c.x, groundLevel + height, c.z), radius);

        Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.9f);
        DrawCircle(new Vector3(c.x, groundLevel, c.z), splashRadius);

        Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.6f);
        Gizmos.DrawLine(new Vector3(c.x, groundLevel, c.z),
                        new Vector3(c.x, groundLevel + height, c.z));
    }

    static void DrawCircle(Vector3 centre, float r)
    {
        const int N = 40;
        Vector3 prev = centre + new Vector3(r, 0, 0);
        for (int i = 1; i <= N; i++)
        {
            float a = i / (float)N * Mathf.PI * 2f;
            Vector3 p = centre + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }
#endif

    /// <summary>
    /// Привязать дождь к своему игроку. В коопе вызывать НА КЛИЕНТЕ,
    /// когда заспавнился локальный игрок:
    ///
    ///   if (IsOwner)
    ///       FindFirstObjectByType&lt;CatCooksRain&gt;()?.SetTarget(transform);
    ///
    /// Дождь по сети не синхронизируется и не должен: он рисуется только
    /// вокруг камеры того, кто на него смотрит. У каждого игрока идёт
    /// свой, и это дешевле, чем одна общая зона на всю карту.
    /// </summary>
    /// <summary>Текущий центр колонны в мире. Читают частицы удара.</summary>
    public Vector3 CurrentCentre { get { return _centre; } }

    /// <summary>Сглаженная интенсивность 0..1.</summary>
    public float CurrentIntensity { get { return _current; } }

    /// <summary>Центр области дождя в мире.</summary>
    public Vector3 Centre { get { return _centre; } }

    /// <summary>Рабочий радиус области.</summary>
    public float AreaRadius { get { return radius; } }

    /// <summary>Горизонтальный снос за высоту колонны.</summary>
    public Vector3 SlantOffset { get { return _slant * height; } }

    /// <summary>Высота земли под точкой — из той же сетки, что у колец.</summary>
    public float GroundAt(float wx, float wz) { return SampleGround(wx, wz); }

    public void SetTarget(Transform t)
    {
        followTarget = t;
        if (t != null) { anchor = Anchor.FollowTarget; return; }

        // Цель отпустили — возвращаемся к области. Иначе режим остался бы
        // FollowTarget с пустой целью, и дождь замер бы там, где стоял
        // прошлый игрок: после смерти колонна висит над трупом.
        anchor = Anchor.ObjectArea;
    }
}
