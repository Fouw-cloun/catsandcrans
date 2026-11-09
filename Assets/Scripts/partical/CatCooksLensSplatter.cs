using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// КЛЯКСЫ НА ОБЪЕКТИВЕ. То, что прилетело в лицо, остаётся на стекле.
///
/// ═══ ЧТО ЭТО ДАЁТ ИГРЕ ═══
///
/// Пока ошмётки просто пролетали сквозь камеру, работа инструментом ничем не
/// грозила: можно было сунуться вплотную и смотреть на срез в упор. Клякса
/// делает расстояние решением. Подошёл близко — видишь лучше, но заляпаешься;
/// отошёл — чисто, но мельче. Это бесплатная механика: ничего не отнимает у
/// игрока, только ставит выбор.
///
/// ═══ ПОЧЕМУ ЭТО UI, А НЕ ШЕЙДЕР ═══
///
/// Полноэкранный эффект пришлось бы вписывать в URP как Renderer Feature,
/// заводить свой Blit, следить за порядком проходов и совместимостью с уже
/// имеющимися CatCooksVolumeFog и прочими. Кляксы того не стоят: их
/// одновременно десяток, они плоские и им не нужен доступ к кадру. Канвас с
/// пулом картинок делает ровно то же самое, стоит один Update и правится
/// мышкой без пересборки шейдеров.
///
/// ═══ ПОЧЕМУ ТЕКСТУРЫ РИСУЮТСЯ КОДОМ ═══
///
/// Клякса — это пятно с рваным краем и парой брызг рядом. Хранить такое
/// ассетом значило бы завести десяток PNG, следить за их импортом и получить
/// повторяемость: один и тот же силуэт узнаётся с третьего раза. Здесь
/// силуэты генерируются при старте и их столько, сколько нужно.
///
/// ВЕШАТЬ НИКУДА НЕ НАДО: служба ошмётков создаст объект сама.
/// </summary>
[DefaultExecutionOrder(-40)]
public sealed class CatCooksLensSplatter : MonoBehaviour
{
    // ───────────────────────── НАСТРОЙКИ ─────────────────────────

    [Header("Сколько")]
    [Tooltip("Потолок клякс на экране. Больше — экран залеплен наглухо, и это " +
             "перестаёт быть неудобством и становится наказанием.")]
    [SerializeField] private int _maxSplats = 22;

    [Tooltip("Размер кляксы в долях высоты экрана")]
    [SerializeField] private Vector2 _sizeRange = new Vector2(0.045f, 0.15f);

    [Header("Как живёт")]
    [Tooltip("Сколько секунд держится, пока не начнёт таять")]
    [SerializeField] private Vector2 _lifetime = new Vector2(6f, 13f);

    [Tooltip("Секунд на растворение в конце жизни")]
    [SerializeField] private float _fadeSeconds = 2.5f;

    [Tooltip("Насколько клякса непрозрачна. Единица закрыла бы обзор наглухо.")]
    [Range(0.1f, 1f)][SerializeField] private float _opacity = 0.82f;

    [Header("Сползание")]
    [Tooltip("С какой скоростью ползёт вниз, долей экрана в секунду. " +
             "Мокрое сползает, сухое висит.")]
    [SerializeField] private float _slideSpeed = 0.035f;

    [Tooltip("Сколько секунд клякса держится, прежде чем сорваться. Именно " +
             "эта задержка читается как «прилипло», а не «стекает».")]
    [SerializeField] private Vector2 _gripSeconds = new Vector2(0.4f, 2.2f);

    [Tooltip("Сползая, клякса ВЫТЯГИВАЕТСЯ. Без этого она едет вниз как " +
             "наклейка, и жидкость не читается.")]
    [Range(1f, 3f)][SerializeField] private float _stretchMax = 1.8f;

    [Tooltip("Оставлять след за сползающей каплей")]
    [SerializeField] private bool _leaveTrail = true;

    [Header("Стереть")]
    [Tooltip("Протереть объектив рукавом. Стирает не всё сразу, а по одной — " +
             "мгновенная очистка обесценила бы всю механику.")]
    [SerializeField] private KeyCode _wipeKey = KeyCode.V;

    [Tooltip("Сколько клякс убирает одно нажатие")]
    [SerializeField] private int _wipePerPress = 6;

    [Header("Отладка")]
    [Tooltip("Ставить кляксу по нажатию — проверить вид, не заводя инструмент")]
    [SerializeField] private KeyCode _testKey = KeyCode.None;

    // ───────────────────────── ДОСТУП ─────────────────────────

    private static CatCooksLensSplatter _instance;
    private static bool _quitting;

    public static CatCooksLensSplatter Instance
    {
        get
        {
            if (_quitting) return null;
            if (_instance != null) return _instance;

            _instance = FindAnyObjectByType<CatCooksLensSplatter>();
            if (_instance != null) return _instance;

            GameObject go = new GameObject("CatCooks Lens Splatter (auto)");
            _instance = go.AddComponent<CatCooksLensSplatter>();
            DontDestroyOnLoad(go);
            return _instance;
        }
    }

    // ───────────────────────── СОСТОЯНИЕ ─────────────────────────

    /// <summary>Одна клякса. Класс, а не структура: живёт в пуле и правится
    /// на месте каждый кадр — копирование структуры туда-сюда только мешало бы.</summary>
    private sealed class LensSplat
    {
        public RectTransform Rect;
        public RawImage Image;

        public bool Alive;
        public float Age;
        public float Life;
        public float Grip;          // сколько ещё держится, прежде чем поедет
        public float Speed;         // текущая скорость сползания
        public float Wet;           // 0 сухая земля, 1 сок
        public float BaseAlpha;
        public Vector2 Pos;         // в долях экрана, 0..1
        public float Size;          // доля высоты экрана
        public float TrailCarry;
        public bool IsTrail;
    }

    private readonly List<LensSplat> _pool = new List<LensSplat>();
    private Canvas _canvas;
    private RectTransform _root;
    private Texture2D[] _shapes;
    private Texture2D[] _trailShapes;

    // ───────────────────────── ЖИЗНЕННЫЙ ЦИКЛ ─────────────────────────

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;

        BuildCanvas();
        BuildShapes();
    }

    private void OnApplicationQuit() => _quitting = true;

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        if (_shapes != null) foreach (Texture2D t in _shapes) if (t != null) Destroy(t);
        if (_trailShapes != null) foreach (Texture2D t in _trailShapes) if (t != null) Destroy(t);
    }

    private void BuildCanvas()
    {
        GameObject go = new GameObject("Lens Canvas");
        go.transform.SetParent(transform, false);

        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Поверх всего игрового интерфейса, но это всё-таки грязь на стекле,
        // а не элемент HUD — меню и подсказки должны читаться сквозь неё.
        _canvas.sortingOrder = 900;

        CanvasScaler sc = go.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 1f;   // считаем от высоты — размеры заданы в её долях

        // Ни одна клякса не должна перехватывать клики: GraphicRaycaster
        // не добавляем вовсе, а картинкам ставим raycastTarget = false.

        _root = go.GetComponent<RectTransform>();
    }

    // ───────────────────────── ПУБЛИЧНОЕ ─────────────────────────

    /// <summary>
    /// Поставить кляксу в случайном месте экрана.
    /// </summary>
    /// <param name="color">цвет фракции, что прилетела</param>
    /// <param name="scale">множитель размера, обычно 0.5..1.6</param>
    /// <param name="wet">0 = сухая земля, висит; 1 = сок, ползёт</param>
    /// <param name="stickiness">липкость: держится дольше перед срывом</param>
    public void Splat(Color color, float scale, float wet, float stickiness)
    {
        // Ближе к центру чаще, но не строго в центр: экран, залепленный ровно
        // по краям, не мешает, а залепленный ровно в центре бесит.
        Vector2 pos = new Vector2(
            0.5f + RandomBiased() * 0.52f,
            0.5f + RandomBiased() * 0.5f);

        SpawnAt(pos, color, scale, wet, stickiness, false);
    }

    /// <summary>Стереть несколько клякс — самые старые уходят первыми.</summary>
    public void Wipe(int count)
    {
        for (int k = 0; k < count; k++)
        {
            LensSplat oldest = null;
            foreach (LensSplat s in _pool)
                if (s.Alive && (oldest == null || s.Age > oldest.Age)) oldest = s;
            if (oldest == null) return;
            Retire(oldest);
        }
    }

    /// <summary>Стереть всё. Для кат-сцен и смерти игрока.</summary>
    public void WipeAll()
    {
        foreach (LensSplat s in _pool) if (s.Alive) Retire(s);
    }

    /// <summary>
    /// Смещение от центра экрана, −1..1.
    ///
    /// Сумма двух равномерных даёт треугольное распределение — кляксы садятся
    /// в рабочей части экрана, а не расползаются по углам, где их всё равно
    /// никто не заметит.
    ///
    /// А вот РОВНО в центр не садятся никогда: там прицел, и первая же клякса
    /// поверх него сделала бы инструмент неуправляемым. Грязь на стекле должна
    /// мешать смотреть, а не отнимать управление — поэтому в середине выемка.
    /// </summary>
    private static float RandomBiased()
    {
        float t = Random.value + Random.value - 1f;

        const float hole = 0.12f;
        return t >= 0f
             ?  hole + t * (1f - hole)
             : -hole + t * (1f - hole);
    }

    // ───────────────────────── КАЖДЫЙ КАДР ─────────────────────────

    private void Update()
    {
        if (_wipeKey != KeyCode.None && Input.GetKeyDown(_wipeKey)) Wipe(_wipePerPress);

        if (_testKey != KeyCode.None && Input.GetKeyDown(_testKey))
            Splat(new Color(1f, 0.86f, 0.44f), Random.Range(0.6f, 1.5f),
                  Random.value, 0.6f);

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        for (int i = 0; i < _pool.Count; i++)
        {
            LensSplat s = _pool[i];
            if (!s.Alive) continue;
            Step(s, dt);
        }
    }

    private void Step(LensSplat s, float dt)
    {
        s.Age += dt;

        if (s.Age >= s.Life + _fadeSeconds) { Retire(s); return; }

        // ── сползание ──
        // Сначала клякса ДЕРЖИТСЯ. Это и есть «прилипло»: если сразу поехать,
        // читается как стекающая вода, а не как шлепок.
        if (s.Grip > 0f)
        {
            s.Grip -= dt;
        }
        else if (s.Wet > 0.15f)
        {
            // Разгоняется, но с потолком: капля на стекле не падает свободно,
            // её держит поверхностное натяжение.
            float target = _slideSpeed * s.Wet * Mathf.Lerp(0.6f, 1.8f, s.Size / _sizeRange.y);
            s.Speed = Mathf.Lerp(s.Speed, target, 1f - Mathf.Exp(-1.5f * dt));
            s.Pos.y -= s.Speed * dt;

            // Сухая земля не ползёт вовсе, поэтому вся ветка под условием.
            if (_leaveTrail && s.Speed > 0.004f && !s.IsTrail)
            {
                s.TrailCarry += s.Speed * dt;
                if (s.TrailCarry > 0.012f)
                {
                    s.TrailCarry = 0f;
                    SpawnAt(s.Pos + new Vector2(0f, s.Size * 0.35f),
                            s.Image.color, 0.25f, s.Wet * 0.4f, 1f, true);
                }
            }
        }

        // Уползла за нижний край — снимаем, не дожидаясь конца жизни.
        if (s.Pos.y < -0.1f) { Retire(s); return; }

        // ── вид ──
        float fade = 1f;
        if (s.Age > s.Life)
            fade = 1f - Mathf.Clamp01((s.Age - s.Life) / _fadeSeconds);

        // Проявляется быстро, но не мгновенно: мгновенное появление читается
        // как артефакт отрисовки, а не как попадание.
        float appear = Mathf.Clamp01(s.Age / 0.08f);

        Color c = s.Image.color;
        c.a = s.BaseAlpha * fade * appear;
        s.Image.color = c;

        // Вытягивание по ходу сползания.
        float stretch = 1f;
        if (s.Wet > 0.15f && s.Grip <= 0f)
            stretch = Mathf.Lerp(1f, _stretchMax,
                                 Mathf.Clamp01(s.Speed / Mathf.Max(0.001f, _slideSpeed)) * s.Wet);

        float h = _root.rect.height;
        float px = s.Size * h;
        s.Rect.sizeDelta = new Vector2(px, px * stretch);
        s.Rect.anchoredPosition = new Vector2(
            (s.Pos.x - 0.5f) * _root.rect.width,
            (s.Pos.y - 0.5f) * h);
    }

    // ───────────────────────── ПУЛ ─────────────────────────

    private void SpawnAt(Vector2 pos, Color color, float scale, float wet,
                         float stickiness, bool isTrail)
    {
        LensSplat s = Take();
        if (s == null) return;

        s.Alive = true;
        s.IsTrail = isTrail;
        s.Age = 0f;
        s.Life = Random.Range(_lifetime.x, _lifetime.y) * (isTrail ? 0.45f : 1f);
        s.Wet = Mathf.Clamp01(wet);
        s.Speed = 0f;
        s.TrailCarry = 0f;
        s.Pos = pos;
        s.Size = Random.Range(_sizeRange.x, _sizeRange.y) * scale;

        // Сухое не срывается вообще — грипу даём заведомо больше жизни.
        s.Grip = s.Wet < 0.15f
               ? s.Life + _fadeSeconds + 1f
               : Random.Range(_gripSeconds.x, _gripSeconds.y) * Mathf.Lerp(0.4f, 1.8f, stickiness);

        // Мокрое ПРОЗРАЧНЕЕ сухого: сок на стекле просвечивает, земля — нет.
        s.BaseAlpha = _opacity * Mathf.Lerp(1f, 0.55f, s.Wet) * (isTrail ? 0.6f : 1f);

        Texture2D[] set = isTrail ? _trailShapes : _shapes;
        s.Image.texture = set[Random.Range(0, set.Length)];

        Color c = color;
        c.a = 0f;                            // проявится в Step
        s.Image.color = c;

        s.Rect.localEulerAngles = new Vector3(0f, 0f, Random.Range(0f, 360f));
        s.Rect.gameObject.SetActive(true);
    }

    private LensSplat Take()
    {
        foreach (LensSplat s in _pool)
            if (!s.Alive) return s;

        if (_pool.Count >= _maxSplats)
        {
            // Пул полон — переиспользуем самую старую. Отказ поставить кляксу
            // выглядел бы как «иногда не работает».
            LensSplat oldest = null;
            foreach (LensSplat s in _pool)
                if (oldest == null || s.Age > oldest.Age) oldest = s;
            return oldest;
        }

        return Create();
    }

    private LensSplat Create()
    {
        GameObject go = new GameObject("Splat");
        go.transform.SetParent(_root, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        RawImage img = go.AddComponent<RawImage>();
        img.raycastTarget = false;           // не перехватывать клики

        LensSplat s = new LensSplat { Rect = rt, Image = img, Alive = false };
        go.SetActive(false);
        _pool.Add(s);
        return s;
    }

    private static void Retire(LensSplat s)
    {
        s.Alive = false;
        s.Rect.gameObject.SetActive(false);
    }

    // ───────────────────────── СИЛУЭТЫ ─────────────────────────

    private void BuildShapes()
    {
        _shapes = new Texture2D[6];
        for (int i = 0; i < _shapes.Length; i++)
            _shapes[i] = BlobTexture(96, 1000 + i * 37, satellites: true);

        _trailShapes = new Texture2D[3];
        for (int i = 0; i < _trailShapes.Length; i++)
            _trailShapes[i] = BlobTexture(48, 500 + i * 19, satellites: false);
    }

    /// <summary>
    /// Пятно с рваным краем.
    ///
    /// Радиус края гуляет по шуму от угла, поэтому силуэт получается
    /// неправильным, а не эллипсом. Плюс несколько мелких брызг рядом —
    /// именно они превращают «круглое пятно» в «шлепок»: одиночный овал
    /// читается как виньетка, а не как попадание.
    /// </summary>
    private static Texture2D BlobTexture(int size, int seed, bool satellites)
    {
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color[] px = new Color[size * size];
        float half = size * 0.5f;

        Random.State keep = Random.state;
        Random.InitState(seed);

        float noiseOx = Random.Range(0f, 100f);
        float noiseOy = Random.Range(0f, 100f);

        // Брызги рядом с основным пятном.
        int satCount = satellites ? Random.Range(2, 6) : 0;
        Vector3[] sats = new Vector3[satCount];   // x, y, радиус — всё в пикселях
        for (int i = 0; i < satCount; i++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float d = Random.Range(half * 0.5f, half * 0.92f);
            sats[i] = new Vector3(half + Mathf.Cos(ang) * d,
                                  half + Mathf.Sin(ang) * d,
                                  Random.Range(size * 0.03f, size * 0.09f));
        }

        Random.state = keep;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - half, dy = y - half;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);

                // Край гуляет: радиус зависит от угла через шум.
                float wobble = Mathf.PerlinNoise(
                    noiseOx + Mathf.Cos(ang) * 1.7f + 5f,
                    noiseOy + Mathf.Sin(ang) * 1.7f + 5f);
                float edge = half * (0.52f + wobble * 0.34f);

                // Мягкая, но не размытая граница: слишком мягкая читается как
                // грязный экран целиком, слишком резкая — как наклейка.
                float a = 1f - Mathf.SmoothStep(edge * 0.72f, edge, dist);

                for (int i = 0; i < satCount; i++)
                {
                    float sd = Mathf.Sqrt((x - sats[i].x) * (x - sats[i].x) +
                                          (y - sats[i].y) * (y - sats[i].y));
                    float sa = 1f - Mathf.SmoothStep(sats[i].z * 0.55f, sats[i].z, sd);
                    if (sa > a) a = sa;
                }

                // Внутри пятна плотность неровная — иначе оно выглядит как
                // плоская заливка одного тона.
                float grain = 0.82f + Mathf.PerlinNoise(x * 0.09f + seed, y * 0.09f) * 0.18f;

                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a) * grain);
            }
        }

        t.SetPixels(px);
        t.Apply(false, true);   // больше не читаем — освобождаем копию в памяти
        return t;
    }
}
