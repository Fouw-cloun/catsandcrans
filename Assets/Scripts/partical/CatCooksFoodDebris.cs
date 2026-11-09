using UnityEngine;

/// <summary>
/// ОБЩАЯ СЛУЖБА ОШМЁТКОВ. Одна на всю игру.
///
/// ═══ ЗАЧЕМ ОНА ПОЯВИЛАСЬ ═══
///
/// До неё каждый инструмент собирал свою ParticleSystem из кода: пила, цепная
/// пила, болгарка, мойка, пылесос — пять почти дословно одинаковых кусков по
/// сорок строк. Пять систем, пять материалов, пять мешей, пять наборов
/// настроек, которые расходились друг с другом при каждой правке. И каждая
/// умела ровно одно: сыпать однотонные кубики.
///
/// Но ошмёток не бывает однотонным. Когда инструмент идёт по грязной картошке,
/// в воздух летит четыре РАЗНЫХ вещества сразу, и они ведут себя по-разному:
///
///   ЗЕМЛЯ    — лёгкая, висит облаком, сносится ветром, не отскакивает;
///   КОЖУРА   — плоская, планирует и кувыркается, тормозится о воздух;
///   МЯКОТЬ   — тяжёлая, летит по баллистике, отскакивает и лежит;
///   СОК      — капли, летят дальше всех и оставляют следы.
///
/// Свалить их в одну систему нельзя: у них разная гравитация, разное
/// сопротивление, разный материал (мякоть непрозрачная, сок — нет) и разный
/// режим отрисовки. Поэтому здесь ЧЕТЫРЕ системы, а смешиваются они на уровне
/// пропорций: сколько чего сыпать, решает палитра состояния продукта.
///
/// ═══ КАК ПОЛЬЗОВАТЬСЯ ═══
///
///   private FoodDebrisStream _spray;                       // в полях
///   _spray = CatCooksFoodDebris.CreateStream();            // в Awake
///   _spray.Spray(point, dir, food, power01, Time.deltaTime); // каждый кадр
///   _spray.Burst(point, dir, food, 1f);                    // на удар
///
/// Поток — это НЕ система частиц, а только счётчики дробных остатков. Систем
/// по-прежнему четыре на всю игру, сколько бы инструментов ни работало.
///
/// ═══ ПОЧЕМУ ПОТОК ОТДЕЛЬНЫМ ОБЪЕКТОМ ═══
///
/// При 600 частиц в секунду и 60 кадрах на кадр приходится 10 штук — но не
/// ровно, а с остатком. Остаток надо копить, иначе он теряется каждый кадр и
/// поток идёт заметно реже заказанного. Если счётчик держать в самой службе,
/// два инструмента, работающие одновременно, будут воровать остатки друг у
/// друга. Поэтому счётчик принадлежит инструменту, а системы — общие.
/// </summary>
[DefaultExecutionOrder(-50)]
public sealed class CatCooksFoodDebris : MonoBehaviour
{
    // ───────────────────────── НАСТРОЙКИ ─────────────────────────

    [Header("Бюджет частиц")]
    [Tooltip("Потолок на каждый слой. Четыре слоя — значит в худшем случае " +
             "вчетверо больше. Урезай, если проседает кадр.")]
    [SerializeField] private int _budgetChunks = 3000;
    [SerializeField] private int _budgetShreds = 2000;
    [SerializeField] private int _budgetDroplets = 2500;
    [SerializeField] private int _budgetDust = 1500;

    [Header("Густота потока, частиц в секунду")]
    [Tooltip("Куски мякоти на полной мощности")]
    [SerializeField] private float _chunksPerSecond = 260f;

    [Tooltip("Стружка кожуры. Умножается на количество земли/кожуры в палитре.")]
    [SerializeField] private float _shredsPerSecond = 170f;

    [Tooltip("Капли сока. Умножается на сочность палитры.")]
    [SerializeField] private float _dropletsPerSecond = 220f;

    [Tooltip("Пыль. Умножается на грязность палитры.")]
    [SerializeField] private float _dustPerSecond = 300f;

    [Header("Разлёт")]
    [Tooltip("Скорость кусков мякоти: минимум и максимум, м/с")]
    [SerializeField] private Vector2 _chunkSpeed = new Vector2(2.2f, 6.5f);

    [Tooltip("Скорость стружки. Ниже — она лёгкая и сразу тормозится.")]
    [SerializeField] private Vector2 _shredSpeed = new Vector2(1.6f, 4.5f);

    [Tooltip("Скорость капель. Выше всех — сок летит дальше всего.")]
    [SerializeField] private Vector2 _dropletSpeed = new Vector2(3.0f, 8.5f);

    [Tooltip("Скорость пыли. Почти нулевая: пыль не летит, а всплывает.")]
    [SerializeField] private Vector2 _dustSpeed = new Vector2(0.4f, 1.8f);

    [Tooltip("Основной разброс вокруг струи, градусы")]
    [Range(0f, 90f)][SerializeField] private float _spread = 20f;

    [Tooltip("Доля кусков, прошивающих широко. Без них струя — ровный конус, " +
             "и это сразу читается как «частицы».")]
    [Range(0f, 1f)][SerializeField] private float _wildChance = 0.18f;

    [Range(0f, 180f)][SerializeField] private float _wildSpread = 80f;

    [Header("Столкновения")]
    [Tooltip("Куски и капли отскакивают от мира и лежат. Первое, что стоит " +
             "выключить, если просядет кадр.")]
    [SerializeField] private bool _collide = true;

    [Tooltip("Слои, обо что бьются ошмётки. Убери отсюда слой еды и игрока: " +
             "иначе кусок, родившийся на поверхности картошки, засчитается " +
             "как столкнувшийся в тот же кадр и погаснет.")]
    [SerializeField] private LayerMask _collisionMask = ~0;

    [Header("Попадание в камеру")]
    [Tooltip("Ошмётки могут залепить объектив. Разлетевшееся в сторону " +
             "зрителя не пропадает в никуда, а остаётся на стекле.")]
    [SerializeField] private bool _splatterCamera = true;

    [Tooltip("Дальше этого в камеру не долетает, метры")]
    [SerializeField] private float _splatterRange = 2.6f;

    [Tooltip("Насколько точно струя должна смотреть в камеру. 1 = только в " +
             "упор по оси, 0.4 = достаточно общего направления.")]
    [Range(0f, 1f)][SerializeField] private float _splatterAim = 0.45f;

    [Tooltip("Клякс в секунду на полной мощности при идеальном попадании")]
    [SerializeField] private float _splatterPerSecond = 3.5f;

    [Header("Отладка")]
    [SerializeField] private bool _logSetup = false;

    // ───────────────────────── ДОСТУП ─────────────────────────

    private static CatCooksFoodDebris _instance;
    private static bool _quitting;

    /// <summary>
    /// Служба создаётся сама при первом обращении и живёт между сценами.
    ///
    /// Почему не искать её в сцене: инструменты спавнятся через сеть, а
    /// сценный объект в префабе не сошлёшь — ссылка стала бы null. Тот же
    /// довод, по которому системы частиц у тебя уже собираются из кода.
    /// </summary>
    public static CatCooksFoodDebris Instance
    {
        get
        {
            if (_quitting) return null;
            if (_instance != null) return _instance;

            _instance = FindAnyObjectByType<CatCooksFoodDebris>();
            if (_instance != null) return _instance;

            GameObject go = new GameObject("CatCooks Food Debris (auto)");
            _instance = go.AddComponent<CatCooksFoodDebris>();
            DontDestroyOnLoad(go);
            return _instance;
        }
    }

    /// <summary>Новый счётчик остатков для одного инструмента.</summary>
    public static FoodDebrisStream CreateStream() => new FoodDebrisStream(Instance);

    // ───────────────────────── ВНУТРЕННОСТИ ─────────────────────────

    private ParticleSystem _chunks;
    private ParticleSystem _shreds;
    private ParticleSystem _droplets;
    private ParticleSystem _dust;

    private Transform _viewCam;
    private CatCooksLensSplatter _lens;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;

        _chunks   = BuildChunks();
        _shreds   = BuildShreds();
        _droplets = BuildDroplets();
        _dust     = BuildDust();

        if (_logSetup)
            Debug.Log("[FoodDebris] Собрано 4 слоя: куски, стружка, капли, пыль.");
    }

    private void OnApplicationQuit() => _quitting = true;

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        DestroyLayer(_chunks); DestroyLayer(_shreds);
        DestroyLayer(_droplets); DestroyLayer(_dust);
    }

    private static void DestroyLayer(ParticleSystem ps)
    {
        if (ps != null) Destroy(ps.gameObject);
    }

    // ───────────────────────── ВЫДАЧА ЧАСТИЦ ─────────────────────────

    /// <summary>
    /// Один кадр струи. Зовётся из FoodDebrisStream, который держит остатки.
    /// </summary>
    internal void EmitStream(FoodDebrisStream s, Vector3 origin, Vector3 dir,
                             in FoodDebrisPalette pal, FoodDebrisProfile prof,
                             float power01, float dt, float sizeScale)
    {
        if (dt <= 0f) return;

        dir = Safe(dir);
        float density = prof != null ? prof.Density : 1f;
        float p = Mathf.Clamp01(power01);

        // Мощность влияет НЕЛИНЕЙНО: на четверти оборотов должно сыпать
        // заметно, а не еле-еле, иначе кажется, что инструмент сломан.
        float flow = Mathf.Sqrt(Mathf.Max(0.05f, p)) * density * dt;

        float chunkN   = _chunksPerSecond   * flow;
        float shredN   = _shredsPerSecond   * flow * Mathf.Lerp(0.35f, 1f, pal.DirtAmount);
        float dropletN = _dropletsPerSecond * flow * Mathf.Lerp(0.10f, 1f, pal.JuiceAmount);
        float dustN    = _dustPerSecond     * flow * pal.DirtAmount;

        EmitChunks  (ref s.ChunkCarry,   chunkN,   origin, dir, pal, prof, p, sizeScale);
        EmitShreds  (ref s.ShredCarry,   shredN,   origin, dir, pal, prof, p, sizeScale);
        EmitDroplets(ref s.DropletCarry, dropletN, origin, dir, pal, prof, p, sizeScale);
        EmitDust    (ref s.DustCarry,    dustN,    origin, dir, pal, prof, p, sizeScale);

        if (_splatterCamera)
            TrySplatter(ref s.SplatterCarry, origin, dir, pal, prof, p, dt);
    }

    /// <summary>
    /// Разовый выброс: удар молотком, отвалившийся кусок, шлепок.
    /// Отличается от струи не только количеством — у всплеска шире конус и
    /// выше скорости, потому что бьёт во все стороны, а не точит в одну.
    /// </summary>
    internal void EmitBurst(Vector3 origin, Vector3 dir, in FoodDebrisPalette pal,
                            FoodDebrisProfile prof, float strength01, float sizeScale)
    {
        dir = Safe(dir);
        float k = Mathf.Clamp01(strength01);
        float density = prof != null ? prof.Density : 1f;

        int chunks   = Mathf.RoundToInt(28f * k * density);
        int shreds   = Mathf.RoundToInt(14f * k * density * Mathf.Lerp(0.3f, 1f, pal.DirtAmount));
        int droplets = Mathf.RoundToInt(22f * k * density * Mathf.Lerp(0.1f, 1f, pal.JuiceAmount));
        int dust     = Mathf.RoundToInt(30f * k * density * pal.DirtAmount);

        float wide = 55f;   // всплеск летит во все стороны, а не струёй

        for (int i = 0; i < chunks; i++)
            OneChunk(origin, dir, pal, prof, 1f, sizeScale, wide);
        for (int i = 0; i < shreds; i++)
            OneShred(origin, dir, pal, prof, 1f, sizeScale, wide);
        for (int i = 0; i < droplets; i++)
            OneDroplet(origin, dir, pal, prof, 1f, sizeScale, wide);
        for (int i = 0; i < dust; i++)
            OneDust(origin, dir, pal, prof, 1f, sizeScale, wide);

        if (_splatterCamera && k > 0.3f)
        {
            float carry = _splatterPerSecond * k * 0.5f;
            TrySplatter(ref carry, origin, dir, pal, prof, k, 1f);
        }
    }

    // ── слой 1: КУСКИ МЯКОТИ ──

    private void EmitChunks(ref float carry, float want, Vector3 o, Vector3 d,
                            in FoodDebrisPalette pal, FoodDebrisProfile prof,
                            float p, float scale)
    {
        carry += want;
        int n = Mathf.FloorToInt(carry);
        if (n <= 0) return;
        carry -= n;
        if (n > 24) n = 24;                 // страховка от скачка dt
        for (int i = 0; i < n; i++) OneChunk(o, d, pal, prof, p, scale, _spread);
    }

    private void OneChunk(Vector3 o, Vector3 d, in FoodDebrisPalette pal,
                          FoodDebrisProfile prof, float p, float scale, float spread)
    {
        if (_chunks == null) return;

        float size = (prof != null ? prof.ChunkSize : 0.016f) * scale;
        Vector3 v = Cone(d, Random.value < _wildChance ? _wildSpread : spread);

        // Часть кусков — не чистая мякоть, а мякоть с прилипшей землёй.
        // Без этого срез выглядит стерильным: грязная картошка обязана сыпать
        // и бурым тоже, а не только жёлтым.
        float grime = Random.value * pal.DirtAmount;
        Color c = Color.Lerp(pal.Flesh, pal.Dirt, grime * 0.75f);
        float shade = Random.Range(0.62f, 1.18f);

        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams
        {
            position = o + Random.insideUnitSphere * size * 0.8f,
            velocity = v * Random.Range(_chunkSpeed.x, _chunkSpeed.y) * Mathf.Max(0.45f, p),
            startColor = new Color(c.r * shade, c.g * shade, c.b * shade, 1f),
            startSize = size * Random.Range(0.55f, 1.75f),
            startLifetime = Random.Range(1.6f, 3.4f)
        };
        _chunks.Emit(ep, 1);
    }

    // ── слой 2: СТРУЖКА КОЖУРЫ ──

    private void EmitShreds(ref float carry, float want, Vector3 o, Vector3 d,
                            in FoodDebrisPalette pal, FoodDebrisProfile prof,
                            float p, float scale)
    {
        carry += want;
        int n = Mathf.FloorToInt(carry);
        if (n <= 0) return;
        carry -= n;
        if (n > 18) n = 18;
        for (int i = 0; i < n; i++) OneShred(o, d, pal, prof, p, scale, _spread);
    }

    private void OneShred(Vector3 o, Vector3 d, in FoodDebrisPalette pal,
                          FoodDebrisProfile prof, float p, float scale, float spread)
    {
        if (_shreds == null) return;

        float size = (prof != null ? prof.ShredSize : 0.030f) * scale;

        // Стружка идёт КУЧНЕЕ мякоти: она снимается лентой, а не колется.
        Vector3 v = Cone(d, spread * 0.65f);

        Color c = Color.Lerp(pal.Skin, pal.Dirt, Random.value * pal.DirtAmount * 0.6f);
        float shade = Random.Range(0.7f, 1.2f);

        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams
        {
            position = o + Random.insideUnitSphere * size * 0.5f,
            velocity = v * Random.Range(_shredSpeed.x, _shredSpeed.y) * Mathf.Max(0.45f, p),
            startColor = new Color(c.r * shade, c.g * shade, c.b * shade, 1f),
            startSize = size * Random.Range(0.6f, 1.6f),
            startLifetime = Random.Range(2.0f, 4.0f)
        };
        _shreds.Emit(ep, 1);
    }

    // ── слой 3: КАПЛИ СОКА ──

    private void EmitDroplets(ref float carry, float want, Vector3 o, Vector3 d,
                              in FoodDebrisPalette pal, FoodDebrisProfile prof,
                              float p, float scale)
    {
        carry += want;
        int n = Mathf.FloorToInt(carry);
        if (n <= 0) return;
        carry -= n;
        if (n > 22) n = 22;
        for (int i = 0; i < n; i++) OneDroplet(o, d, pal, prof, p, scale, _spread);
    }

    private void OneDroplet(Vector3 o, Vector3 d, in FoodDebrisPalette pal,
                            FoodDebrisProfile prof, float p, float scale, float spread)
    {
        if (_droplets == null) return;

        float size = (prof != null ? prof.DropletSize : 0.009f) * scale;

        // Капли разлетаются ШИРЕ всего: жидкость срывается с кромки диска во
        // все стороны, а не летит организованной струёй.
        Vector3 v = Cone(d, spread * 1.5f);

        Color c = pal.Juice;
        float shade = Random.Range(0.85f, 1.15f);

        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams
        {
            position = o + Random.insideUnitSphere * size,
            velocity = v * Random.Range(_dropletSpeed.x, _dropletSpeed.y) * Mathf.Max(0.5f, p),
            startColor = new Color(c.r * shade, c.g * shade, c.b * shade, c.a),
            startSize = size * Random.Range(0.5f, 1.9f),
            startLifetime = Random.Range(1.2f, 2.6f)
        };
        _droplets.Emit(ep, 1);
    }

    // ── слой 4: ПЫЛЬ ──

    private void EmitDust(ref float carry, float want, Vector3 o, Vector3 d,
                          in FoodDebrisPalette pal, FoodDebrisProfile prof,
                          float p, float scale)
    {
        carry += want;
        int n = Mathf.FloorToInt(carry);
        if (n <= 0) return;
        carry -= n;
        if (n > 20) n = 20;
        for (int i = 0; i < n; i++) OneDust(o, d, pal, prof, p, scale, _spread);
    }

    private void OneDust(Vector3 o, Vector3 d, in FoodDebrisPalette pal,
                         FoodDebrisProfile prof, float p, float scale, float spread)
    {
        if (_dust == null) return;

        float size = (prof != null ? prof.DustSize : 0.020f) * scale;

        // Пыль почти не слушается струи: она клубится вокруг точки касания.
        Vector3 v = Cone(d, 70f);

        Color c = pal.Dirt;
        float shade = Random.Range(0.75f, 1.25f);

        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams
        {
            position = o + Random.insideUnitSphere * size * 1.5f,
            velocity = v * Random.Range(_dustSpeed.x, _dustSpeed.y),
            startColor = new Color(c.r * shade, c.g * shade, c.b * shade, 0.5f),
            startSize = size * Random.Range(0.7f, 2.2f),
            startLifetime = Random.Range(0.7f, 1.8f)
        };
        _dust.Emit(ep, 1);
    }

    // ───────────────────────── ПОПАДАНИЕ В КАМЕРУ ─────────────────────────

    /// <summary>
    /// Клякса на объективе.
    ///
    /// СЧИТАЕТСЯ АНАЛИТИЧЕСКИ, а не столкновением частиц с коллайдером камеры.
    /// Коллайдер на камере ловил бы всё подряд — руки игрока, свою же еду, —
    /// и требовал бы включить у частиц отправку сообщений о столкновениях,
    /// а это самая дорогая галка во всей системе. Здесь вместо этого простая
    /// проверка: смотрит ли струя в камеру и близко ли она. Стоит три
    /// скалярных произведения на кадр вместо тысячи проверок физики.
    /// </summary>
    private void TrySplatter(ref float carry, Vector3 origin, Vector3 dir,
                             in FoodDebrisPalette pal, FoodDebrisProfile prof,
                             float power01, float dt)
    {
        Transform cam = ViewCamera();
        if (cam == null) return;

        Vector3 toCam = cam.position - origin;
        float dist = toCam.magnitude;
        if (dist > _splatterRange || dist < 0.05f) return;

        toCam /= dist;

        // Струя должна смотреть примерно в зрителя.
        float aim = Vector3.Dot(dir, toCam);
        if (aim < _splatterAim) return;

        // Ближе — чаще. Точнее в лицо — чаще.
        float near = 1f - Mathf.Clamp01(dist / Mathf.Max(0.1f, _splatterRange));
        float acc = Mathf.InverseLerp(_splatterAim, 1f, aim);

        carry += _splatterPerSecond * power01 * near * acc * dt;
        int n = Mathf.FloorToInt(carry);
        if (n <= 0) return;
        carry -= n;
        if (n > 3) n = 3;

        CatCooksLensSplatter lens = Lens();
        if (lens == null) return;

        for (int i = 0; i < n; i++)
        {
            // Чем летит — тем и мажет. Сочная картошка даёт каплю, грязная —
            // ком земли. Пропорции те же, что и в струе, поэтому клякса всегда
            // того же цвета, что и то, что в этот момент летит мимо.
            float roll = Random.value;
            Color c;
            float wet;

            if (roll < pal.DirtAmount * 0.5f)      { c = pal.Dirt;  wet = 0.15f; }
            else if (roll < pal.DirtAmount * 0.5f + pal.JuiceAmount * 0.45f)
                                                   { c = pal.Juice; wet = 1f; }
            else                                    { c = pal.Flesh; wet = 0.5f; }

            float stick = prof != null ? prof.Stickiness : 0.6f;
            lens.Splat(c, Random.Range(0.5f, 1.6f), wet, stick);
        }
    }

    private CatCooksLensSplatter Lens()
    {
        if (_lens != null) return _lens;
        _lens = CatCooksLensSplatter.Instance;
        return _lens;
    }

    /// <summary>
    /// Камера ЛОКАЛЬНОГО игрока. Кэш сбрасывается сам, когда камера умирает:
    /// при смене сцены Camera.main меняется, а протухшая ссылка означала бы,
    /// что кляксы перестали появляться и непонятно почему.
    /// </summary>
    private Transform ViewCamera()
    {
        if (_viewCam != null) return _viewCam;
        Camera c = Camera.main;
        _viewCam = c != null ? c.transform : null;
        return _viewCam;
    }

    // ───────────────────────── СБОРКА СЛОЁВ ─────────────────────────

    private ParticleSystem BuildChunks()
    {
        ParticleSystem ps = NewLayer("Debris/Chunks", _budgetChunks, out ParticleSystemRenderer r);

        ParticleSystem.MainModule main = ps.main;
        main.gravityModifier = 1.15f;
        main.startLifetime = 2.5f;

        Tumble(ps, 12f);
        FadeAtEnd(ps, 0.85f);
        if (_collide) Collide(ps, 0.55f, 0.22f, 0.05f);

        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = MeshLib.Chunk();
        r.alignment = ParticleSystemRenderSpace.World;
        r.material = MatLib.Opaque("Debris Chunks");
        ps.gameObject.SetActive(true);
        ps.Play();
        return ps;
    }

    private ParticleSystem BuildShreds()
    {
        ParticleSystem ps = NewLayer("Debris/Shreds", _budgetShreds, out ParticleSystemRenderer r);

        ParticleSystem.MainModule main = ps.main;
        // Стружка ЛЁГКАЯ: падает медленнее куска и успевает спланировать.
        main.gravityModifier = 0.55f;
        main.startLifetime = 3f;

        Tumble(ps, 22f);            // кувыркается сильнее всех — она плоская
        Drag(ps, 1.6f);             // и сильнее всех тормозится о воздух
        Swirl(ps, 0.35f, 0.6f);     // и её сносит завихрениями
        FadeAtEnd(ps, 0.8f);
        if (_collide) Collide(ps, 0.75f, 0.1f, 0.08f);

        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = MeshLib.Shred();
        r.alignment = ParticleSystemRenderSpace.World;
        r.material = MatLib.Opaque("Debris Shreds");
        ps.gameObject.SetActive(true);
        ps.Play();
        return ps;
    }

    private ParticleSystem BuildDroplets()
    {
        ParticleSystem ps = NewLayer("Debris/Droplets", _budgetDroplets, out ParticleSystemRenderer r);

        ParticleSystem.MainModule main = ps.main;
        main.gravityModifier = 1.35f;   // капля тяжелее всех по ощущению
        main.startLifetime = 2f;

        Shrink(ps, 1f, 0.55f);          // капля усыхает в полёте
        FadeAtEnd(ps, 0.7f);
        if (_collide) Collide(ps, 0.85f, 0.05f, 0.35f);

        // Капли ВЫТЯГИВАЮТСЯ ПО СКОРОСТИ. Это единственный дешёвый способ
        // отличить каплю от камешка: круглая точка в полёте читается как
        // мусор, а вытянутая — как жидкость.
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.045f;
        r.lengthScale = 2.2f;
        r.material = MatLib.Transparent("Debris Droplets");
        ps.gameObject.SetActive(true);
        ps.Play();
        return ps;
    }

    private ParticleSystem BuildDust()
    {
        ParticleSystem ps = NewLayer("Debris/Dust", _budgetDust, out ParticleSystemRenderer r);

        ParticleSystem.MainModule main = ps.main;
        // Пыль почти невесома и слегка ВСПЛЫВАЕТ: отрицательная гравитация
        // читается как тёплый воздух от работы инструмента.
        main.gravityModifier = -0.05f;
        main.startLifetime = 1.2f;

        Grow(ps, 1f, 2.6f);             // облако расходится
        Drag(ps, 2.8f);                 // и быстро вязнет
        Swirl(ps, 0.5f, 0.9f);
        FadeCurve(ps, 0.15f, 0.35f);    // проявляется и тает, а не моргает
        // Пыль НЕ сталкивается: облако, застревающее на столешнице, выглядит
        // как баг, а стоит дороже всего остального вместе взятого.

        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.material = MatLib.Soft("Debris Dust");
        ps.gameObject.SetActive(true);
        ps.Play();
        return ps;
    }

    /// <summary>
    /// Общий каркас слоя. Собирается ВЫКЛЮЧЕННЫМ: система начинает играть сразу
    /// при AddComponent, а часть свойств на играющей менять нельзя — Unity
    /// сыплет ошибками в консоль.
    /// </summary>
    private ParticleSystem NewLayer(string name, int budget, out ParticleSystemRenderer r)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.SetActive(false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = budget;
        main.startSpeed = 0f;        // задаём поштучно в EmitParams
        main.startSize = 0.02f;      // тоже поштучно

        // КРИТИЧНО. При Local частицы привязаны к системе: она висит на объекте
        // службы, и вылетевшие ошмётки поехали бы за ним по всей сцене.
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // Служба стоит в нуле мира, а работают инструменты далеко от него.
        // При Automatic Unity посчитал бы её границы вокруг нуля и отключил
        // симуляцию целиком — ошмётки просто перестали бы появляться.
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = false;          // всё через Emit вручную

        ParticleSystem.ShapeModule sh = ps.shape;
        sh.enabled = false;          // позиция задаётся вручную

        r = go.GetComponent<ParticleSystemRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    // ── модули, вынесенные, чтобы четыре слоя читались, а не тонули в настройках ──

    private static void Tumble(ParticleSystem ps, float speed)
    {
        ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.separateAxes = true;
        rot.x = new ParticleSystem.MinMaxCurve(-speed, speed);
        rot.y = new ParticleSystem.MinMaxCurve(-speed, speed);
        rot.z = new ParticleSystem.MinMaxCurve(-speed, speed);
    }

    private static void Drag(ParticleSystem ps, float drag)
    {
        ParticleSystem.LimitVelocityOverLifetimeModule lim = ps.limitVelocityOverLifetime;
        lim.enabled = true;
        lim.dampen = 0.06f;
        lim.drag = new ParticleSystem.MinMaxCurve(drag * 0.01f);
        lim.multiplyDragByParticleSize = true;
    }

    private static void Swirl(ParticleSystem ps, float strength, float freq)
    {
        ParticleSystem.NoiseModule n = ps.noise;
        n.enabled = true;
        n.strength = new ParticleSystem.MinMaxCurve(strength);
        n.frequency = freq;
        n.scrollSpeed = new ParticleSystem.MinMaxCurve(0.4f);
        n.quality = ParticleSystemNoiseQuality.Low;
        n.damping = true;
    }

    /// <summary>
    /// Столкновения с миром. МАСКА ОБЯЗАТЕЛЬНА: без неё кусок, родившийся на
    /// поверхности картошки, считается столкнувшимся с ней же в тот же кадр и
    /// гаснет, не успев отлететь — выглядит как «частиц почти нет».
    /// </summary>
    private void Collide(ParticleSystem ps, float dampen, float bounce, float loss)
    {
        ParticleSystem.CollisionModule cm = ps.collision;
        cm.enabled = true;
        cm.type = ParticleSystemCollisionType.World;
        cm.mode = ParticleSystemCollisionMode.Collision3D;
        cm.quality = ParticleSystemCollisionQuality.Low;
        cm.collidesWith = _collisionMask;
        cm.dampen = new ParticleSystem.MinMaxCurve(dampen);
        cm.bounce = new ParticleSystem.MinMaxCurve(bounce);
        cm.lifetimeLoss = new ParticleSystem.MinMaxCurve(loss);
        cm.sendCollisionMessages = false;   // самая дорогая галка — выключена
    }

    /// <summary>Держится непрозрачным и гаснет в конце: кусок должен успеть
    /// долететь и лечь, а не растаять в воздухе.</summary>
    private static void FadeAtEnd(ParticleSystem ps, float holdUntil)
    {
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, holdUntil),
                    new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }

    private static void FadeCurve(ParticleSystem ps, float inAt, float outAt)
    {
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, inAt),
                    new GradientAlphaKey(0.7f, outAt),
                    new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }

    private static void Shrink(ParticleSystem ps, float from, float to)
    {
        ParticleSystem.SizeOverLifetimeModule s = ps.sizeOverLifetime;
        s.enabled = true;
        s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, from, 1f, to));
    }

    private static void Grow(ParticleSystem ps, float from, float to)
    {
        ParticleSystem.SizeOverLifetimeModule s = ps.sizeOverLifetime;
        s.enabled = true;
        s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, from, 1f, to));
    }

    // ───────────────────────── МЕЛОЧИ ─────────────────────────

    private static Vector3 Safe(Vector3 d)
    {
        if (d.sqrMagnitude < 0.000001f) return Vector3.up;
        return d.normalized;
    }

    /// <summary>Равномерная точка в конусе. Через случайные углы Эйлера вышло
    /// бы сгущение к оси, и струя выглядела бы как ниточка с бахромой.</summary>
    private static Vector3 Cone(Vector3 axis, float degrees)
    {
        if (degrees <= 0.01f) return axis;
        float rad = Mathf.Deg2Rad * Mathf.Min(degrees, 180f);
        float z = Random.Range(Mathf.Cos(rad), 1f);
        float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
        float t = Random.Range(0f, Mathf.PI * 2f);
        Vector3 local = new Vector3(r * Mathf.Cos(t), r * Mathf.Sin(t), z);
        return Quaternion.FromToRotation(Vector3.forward, axis) * local;
    }

    // ───────────────────────── МЕШИ И МАТЕРИАЛЫ ─────────────────────────

    private static class MeshLib
    {
        private static Mesh _chunk, _shred;

        /// <summary>Кусок мякоти — НЕПРАВИЛЬНЫЙ многогранник, а не кубик.
        /// Ровный кубик читается как «частица»: у него одинаковые грани, и
        /// сотня одинаковых кубиков в кадре выглядит как крупа.</summary>
        public static Mesh Chunk()
        {
            if (_chunk != null) return _chunk;

            // Фиксированное зерно: меш строится один раз на всю игру, и куски
            // должны быть похожи между запусками, иначе не настроить размер.
            Random.State keep = Random.state;
            Random.InitState(12345);

            Vector3[] c = new Vector3[8];
            const float s = 0.5f;
            Vector3[] baseC =
            {
                new Vector3(-s,-s,-s), new Vector3( s,-s,-s), new Vector3( s,-s, s), new Vector3(-s,-s, s),
                new Vector3(-s, s,-s), new Vector3( s, s,-s), new Vector3( s, s, s), new Vector3(-s, s, s)
            };
            for (int i = 0; i < 8; i++)
                c[i] = baseC[i] + Random.insideUnitSphere * 0.22f;

            Random.state = keep;
            _chunk = FromCorners(c, "DebrisChunk");
            return _chunk;
        }

        /// <summary>Стружка кожуры — длинная и очень тонкая пластинка.
        /// Именно объёмная, а не квад: квад пропадал бы, повернувшись ребром,
        /// а стружка кувыркается постоянно.</summary>
        public static Mesh Shred()
        {
            if (_shred != null) return _shred;
            const float x = 0.5f, y = 0.055f, z = 0.16f;
            Vector3[] c =
            {
                new Vector3(-x,-y,-z), new Vector3( x,-y,-z), new Vector3( x,-y, z), new Vector3(-x,-y, z),
                new Vector3(-x, y,-z), new Vector3( x, y,-z), new Vector3( x, y, z), new Vector3(-x, y, z)
            };
            // Сужаем один конец — стружка не прямоугольная, она сходит на нет.
            // Покомпонентно, а не умножением на Vector3: у Vector3 нет
            // почленного умножения, для этого есть Vector3.Scale.
            c[1].z *= 0.45f; c[2].z *= 0.45f;
            c[5].z *= 0.45f; c[6].z *= 0.45f;

            _shred = FromCorners(c, "DebrisShred");
            return _shred;
        }

        private static Mesh FromCorners(Vector3[] c, string name)
        {
            int[] faces =
            {
                0,2,1, 0,3,2,   4,5,6, 4,6,7,
                0,1,5, 0,5,4,   1,2,6, 1,6,5,
                2,3,7, 2,7,6,   3,0,4, 3,4,7
            };
            Vector3[] verts = new Vector3[faces.Length];
            int[] tris = new int[faces.Length];
            for (int i = 0; i < faces.Length; i++) { verts[i] = c[faces[i]]; tris[i] = i; }

            Mesh m = new Mesh { name = name };
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }

    private static class MatLib
    {
        /// <summary>
        /// Материал ОБЯЗАН умножать на цвет вершин — именно через него
        /// подставляется цвет конкретного овоща и конкретной фракции. Обычный
        /// Unlit цвет вершин игнорирует, поэтому сперва ищем шейдеры для частиц.
        /// </summary>
        private static Shader Particle()
        {
            return Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Sprites/Default");
        }

        public static Material Opaque(string name)
        {
            Shader sh = Particle();
            if (sh == null) return null;
            Material m = new Material(sh) { name = name };
            SetColor(m, Color.white);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 0f);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 1f);
            m.renderQueue = 2000;
            return m;
        }

        public static Material Transparent(string name)
        {
            Shader sh = Particle();
            if (sh == null) return null;
            Material m = new Material(sh) { name = name };
            SetColor(m, Color.white);
            MakeTransparent(m);
            return m;
        }

        /// <summary>То же, что Transparent, но заведомо мягкое: пыль не должна
        /// давать резких краёв, иначе облако выглядит как рой квадратов.</summary>
        public static Material Soft(string name)
        {
            Material m = Transparent(name);
            if (m != null && m.HasProperty("_SoftParticlesEnabled"))
                m.SetFloat("_SoftParticlesEnabled", 1f);
            return m;
        }

        /// <summary>
        /// Перевод URP-материала в прозрачный режим. Одной установкой _Surface
        /// не обойтись: URP смотрит на ключевые слова и на режим смешивания,
        /// а они при создании материала из кода остаются от непрозрачного.
        /// </summary>
        private static void MakeTransparent(Material m)
        {
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);          // Alpha
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }
    }
}

/// <summary>
/// СЧЁТЧИК ОСТАТКОВ одного инструмента. Систем частиц не держит — только
/// дробные хвосты, чтобы два работающих одновременно инструмента не воровали
/// их друг у друга.
///
/// Создаётся через CatCooksFoodDebris.CreateStream(), уничтожать не нужно.
/// </summary>
public sealed class FoodDebrisStream
{
    internal float ChunkCarry, ShredCarry, DropletCarry, DustCarry, SplatterCarry;

    private readonly CatCooksFoodDebris _fx;

    internal FoodDebrisStream(CatCooksFoodDebris fx) { _fx = fx; }

    /// <summary>
    /// Непрерывная струя. Зови каждый кадр, пока инструмент работает.
    /// </summary>
    /// <param name="point">точка касания инструмента с продуктом</param>
    /// <param name="dir">куда бьёт струя</param>
    /// <param name="food">продукт — из него берутся палитра и размер</param>
    /// <param name="power01">мощность 0..1: обороты, темп ведения</param>
    public void Spray(Vector3 point, Vector3 dir, FoodItem food, float power01, float dt)
    {
        if (_fx == null || food == null) return;
        FoodDebrisProfile prof = FoodDebrisLookup.ProfileOf(food);
        FoodDebrisPalette pal = FoodDebrisLookup.PaletteOf(food, prof);
        _fx.EmitStream(this, point, dir, pal, prof, power01, dt,
                       FoodDebrisLookup.SizeScale(food));
    }

    /// <summary>Разовый всплеск: удар, отвалившийся кусок, шлепок.</summary>
    public void Burst(Vector3 point, Vector3 dir, FoodItem food, float strength01)
    {
        if (_fx == null || food == null) return;
        FoodDebrisProfile prof = FoodDebrisLookup.ProfileOf(food);
        FoodDebrisPalette pal = FoodDebrisLookup.PaletteOf(food, prof);
        _fx.EmitBurst(point, dir, pal, prof, strength01,
                      FoodDebrisLookup.SizeScale(food));
    }
}

/// <summary>
/// Мост между продуктом и палитрой.
///
/// Отдельным классом, потому что профиль может быть не назначен: тогда палитра
/// собирается из старого DebrisColor, и все инструменты продолжают работать
/// ровно как раньше. Без этого моста внедрение профилей было бы «всё или
/// ничего» — пришлось бы завести профиль каждому продукту в игре разом.
/// </summary>
public static class FoodDebrisLookup
{
    /// <summary>Профиль продукта. Ищется по имени ассета рядом с ингредиентом —
    /// см. FoodDebrisProfileBinder, если решишь хранить ссылку явно.</summary>
    public static FoodDebrisProfile ProfileOf(FoodItem food)
    {
        if (food == null) return null;
        FoodDebrisProfileBinder b = food.GetComponent<FoodDebrisProfileBinder>();
        return b != null ? b.Profile : null;
    }

    /// <summary>
    /// Палитра с учётом состояния и идущей обработки.
    /// Нет профиля — собираем из DebrisColor: тускло, но работает.
    /// </summary>
    public static FoodDebrisPalette PaletteOf(FoodItem food, FoodDebrisProfile prof)
    {
        if (prof != null)
            return prof.Resolve(food.StateIndex, food.ProgressTo, food.Progress01);

        Color b = food.Ingredient != null
                ? food.Ingredient.DebrisColor
                : new Color(0.78f, 0.66f, 0.45f);

        return new FoodDebrisPalette
        {
            Label = "из DebrisColor",
            Dirt  = b * 0.45f,
            Skin  = b * 0.8f,
            Flesh = b,
            Juice = new Color(b.r, b.g, b.b, 0.4f),
            DirtAmount = 0.4f,
            JuiceAmount = 0.35f
        };
    }

    /// <summary>
    /// Множитель размера от габаритов продукта: с картошки летят куски одного
    /// масштаба, с кочана капусты — другого, и настраивать это отдельно для
    /// каждого продукта не приходится.
    /// </summary>
    public static float SizeScale(FoodItem food)
    {
        Collider col = food.GetComponentInChildren<Collider>();
        if (col == null) return 1f;
        // 0.05 м — характерный радиус картофелины, от неё и нормируем.
        return Mathf.Clamp(col.bounds.extents.magnitude / 0.05f, 0.4f, 3f);
    }
}
