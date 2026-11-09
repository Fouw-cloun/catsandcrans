using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>Что требуется от игрока, чтобы обработка шла.</summary>
public enum SweepMode
{
    /// <summary>Води как угодно, лишь бы прицел двигался по продукту.</summary>
    FreeSweep,

    /// <summary>Надо медленно провести слева направо. Движение назад не
    /// засчитывается; отъехал далеко назад — начинается новый проход.</summary>
    DirectionalPass
}

/// <summary>
/// ОБРАБОТКА ЕДЫ ВРАЩАЮЩИМСЯ ИНСТРУМЕНТОМ — чистка болгаркой/шлифмашинкой.
/// Вешается на КОРЕНЬ инструмента, туда же, где AngleGrinderItem.
///
/// ЧТОБЫ ЗАРАБОТАЛО, в AngleGrinderItem дописать 4 строки — см. ГАЙД.
///
/// ПРИЦЕЛ ИЗ КАМЕРЫ, инструмент — только визуал.
/// Корпус в руке повёрнут под своим углом и «режет» разными сторонами, поэтому
/// луч из корпуса уезжал бы куда попало. Целимся туда же, куда и F —
/// из _aimOrigin игрока (камеры).
///
/// ЛУЧ, А НЕ КОНТАКТ: у продукта Rigidbody, а инструмент в руке кинематический —
/// при физическом касании продукт отлетал бы, и его пришлось бы догонять.
///
/// СЕТЬ:
///   владелец — целится камерой, КАЖДЫЙ КАДР считает набежавшую работу и
///              шлёт накопленное 20 раз в секунду. Считать работу на сервере
///              по его редким пробам нельзя: движение туда-обратно между
///              пробами выглядело бы как стояние на месте;
///   сервер   — делает луч по присланному прицелу, ограничивает присланную
///              работу сверху, копит прогресс, публикует цель в SyncVar;
///   клиенты  — берут цель из SyncVar и рисуют ошмётки локально, без RPC.
///
/// Прицел считает клиент, потому что сервер не знает наклон камеры — ровно тот
/// же приём, что в твоём UseContext.
/// </summary>
public class GrinderFoodProcessor : NetworkBehaviour
{
    [Header("Что делает инструмент")]
    [Tooltip("Обработка, которую применяет инструмент. Для болгарки — чистка.")]
    [SerializeField] private ProcessDef _process;

    [Tooltip("СКОЛЬКО НАДО ПРОВЕСТИ, чтобы обработать продукт целиком. " +
             "В радиусах продукта: 8 = примерно четыре раза туда-обратно по " +
             "картошке. Считается пройденный путь, а не время, поэтому засчитывается " +
             "любое движение — хоть широкими взмахами, хоть мелкой дрожью.")]
    [Min(0.5f)][SerializeField] private float _sweepDistance = 8f;

    [Tooltip("Быстрее этого обработать нельзя, даже если хлестать мышью. " +
             "Секунд на весь продукт.")]
    [Min(0.2f)][SerializeField] private float _minSeconds = 1.2f;

    [Tooltip("Ниже этой раскрутки инструмент только шумит, но не работает")]
    [Range(0f, 1f)][SerializeField] private float _minPower = 0.35f;

    [Header("Прицел")]
    [Tooltip("Дальность от камеры, метры. Как _reach у PlayerInteractor.")]
    [SerializeField] private float _range = 2.5f;

    [Tooltip("Слои, где лежит еда. Убери отсюда слой игрока, иначе луч упрётся " +
             "в собственную капсулу.")]
    [SerializeField] private LayerMask _foodMask = ~0;

    [Header("Как надо водить")]
    [Tooltip("FreeSweep — води как угодно, лишь бы двигал.\n" +
             "DirectionalPass — надо медленно провести слева направо.")]
    [SerializeField] private SweepMode _sweepMode = SweepMode.FreeSweep;

    [Header("FreeSweep")]
    [Tooltip("Мёртвая зона на кадр, в радиусах продукта. Отсекает дрожание " +
             "луча на кривой поверхности, чтобы неподвижный прицел не набирал " +
             "прогресс сам по себе. Ставь очень мелким.")]
    [SerializeField] private float _sweepDeadzone = 0.0015f;

    [Tooltip("Максимум за кадр, в радиусах продукта. Не даёт скачку луча " +
             "(перевёл прицел через всю картошку) отдать кусок прогресса разом.")]
    [SerializeField] private float _sweepMaxStep = 0.25f;

    [Header("DirectionalPass")]
    [Tooltip("Сколько проходов слева направо нужно на одну чистку")]
    [Min(0.25f)][SerializeField] private float _passesNeeded = 1f;

    [Tooltip("Максимальная скорость проведения. Выше — начисляется хуже, " +
             "потому что вести надо МЕДЛЕННО. В долях продукта в секунду.")]
    [SerializeField] private float _maxPassSpeed = 0.9f;

    [Tooltip("Насколько надо отъехать назад, чтобы начать новый проход. " +
             "В долях ширины продукта.")]
    [Range(0.05f, 1f)][SerializeField] private float _passRestartBack = 0.25f;

    [Header("Вибрация инструмента")]
    [Tooltip("ЧТО трясти. Укажи дочерний объект с моделью инструмента — тогда " +
             "тряску увидят все игроки.\n\n" +
             "Пусто = трясётся корень, и тряску увидит ТОЛЬКО владелец. Иначе " +
             "остальным позиция приехала бы через NetworkTransform уже с " +
             "тряской, и они наложили бы поверх свою.")]
    [SerializeField] private Transform _shakeTarget;

    [Tooltip("Размах дрожи, метры")]
    [SerializeField] private float _vibratePos = 0.004f;

    [Tooltip("Размах дрожи по повороту, градусы")]
    [SerializeField] private float _vibrateRot = 0.9f;

    [Tooltip("Частота дрожи. Выше — злее звучит визуально.")]
    [SerializeField] private float _vibrateFreq = 45f;

    [Tooltip("Во сколько раз сильнее трясёт, когда реально пилим продукт")]
    [SerializeField] private float _vibrateOnContact = 2.2f;

    [Header("Тянуться к цели")]
    [Tooltip("На сколько инструмент подаётся вперёд при наводке, метры. 0 = не тянуться.")]
    [SerializeField] private float _leanDistance = 0.14f;

    [SerializeField] private float _leanSpeed = 0.6f;

    [Header("Ошмётки")]
    [Tooltip("ОСТАВЬ ПУСТЫМ — система частиц соберётся скриптом сама. " +
             "Префаб нужен, только если захочешь свою.")]
    [SerializeField] private ParticleSystem _debrisPrefab;

    [Tooltip("Частиц в секунду на полных оборотах")]
    [SerializeField] private float _debrisPerSecond = 420f;

    [Tooltip("Скорость вылета: минимум и максимум")]
    [SerializeField] private Vector2 _debrisSpeed = new Vector2(2.5f, 6.5f);

    [Tooltip("Разброс яркости кусочков. Кожура неоднородная — без разброса " +
             "ошмётки выглядят как крашеная крупа.")]
    [SerializeField] private Vector2 _debrisShade = new Vector2(0.55f, 1.2f);

    [Tooltip("Ошмётки отскакивают от пола и лежат. Выключи, если просядет fps.")]
    [SerializeField] private bool _debrisCollide = true;

    [Header("Размер ошмётков")]
    [Tooltip("Множитель к размеру из IngredientDef. Крути это, если в целом " +
             "мелко или крупно — размер продукта останется относительным.")]
    [SerializeField] private float _debrisSizeMul = 2f;

    [Tooltip("Разброс размера: минимум и максимум от базового")]
    [SerializeField] private Vector2 _debrisSizeRandom = new Vector2(0.6f, 1.6f);

    [Header("Струя — направление вылета")]
    [Tooltip("Угол в плоскости ЭКРАНА, градусы. 0 = вверх, ПОЛОЖИТЕЛЬНЫЙ = " +
             "влево, отрицательный = вправо. 45 = вверх-влево под 45°.")]
    [Range(-180f, 180f)][SerializeField] private float _jetScreenAngle = 45f;

    [Tooltip("Насколько струя выходит из экрана НА ЗРИТЕЛЯ. 0 = строго вдоль " +
             "экрана, 1 = прямо в камеру.")]
    [Range(0f, 1f)][SerializeField] private float _jetTowardCamera = 0.25f;

    [Tooltip("Насколько слушаться струю против физики. 1 = только струя, " +
             "0 = только отскок от продукта в сторону инструмента.")]
    [Range(0f, 1f)][SerializeField] private float _jetWeight = 0.85f;

    [Header("Разброс")]
    [Tooltip("Основной разброс вокруг струи, градусы. Так летит большинство.")]
    [Range(0f, 90f)][SerializeField] private float _debrisSpread = 16f;

    [Tooltip("Доля кусочков, которые прошивают широко. 0.15 = каждый седьмой.")]
    [Range(0f, 1f)][SerializeField] private float _wildChance = 0.15f;

    [Tooltip("Разброс для этих залётных, градусы")]
    [Range(0f, 180f)][SerializeField] private float _wildSpread = 85f;

    [Header("Звук")]
    [SerializeField] private AudioSource _grindLoop;

    [Header("ОТЛАДКА (выключить в релизе)")]
    [Tooltip("Рисовать луч прицела. Цвет показывает, на каком шаге всё встало.")]
    [SerializeField] private bool _debugLine = true;

    [Tooltip("Текст в углу экрана: что именно мешает чистке")]
    [SerializeField] private bool _debugHud = true;

    // ───────── сеть: текущая цель, чтобы все рисовали ошмётки ─────────
    private readonly SyncVar<NetworkObject> _target = new SyncVar<NetworkObject>();

    /// <summary>Насколько бодро идёт работа прямо сейчас, 0..255. Считается от
    /// скорости ведения. Нужен всем: по нему масштабируется поток ошмётков,
    /// иначе у наблюдателей искры сыпались бы даже когда владелец замер.</summary>
    private readonly SyncVar<byte> _workRate = new SyncVar<byte>(0);

    private ISpinningTool _tool;
    private Transform _aim;                  // камера владельца (прицел)
    private Transform _viewCam;              // камера локального игрока (визуал струи)
    private readonly RaycastHit[] _hits = new RaycastHit[8];

    private ParticleSystem _debris;          // созданный экземпляр
    private float _debrisCarry;
    private float _lean;
    private float _nextSend;
    private float _lastCmdTime;              // на сервере: когда пришла команда

    private bool _grinding;                  // владелец: шлём ли мы сейчас команды
    private bool _hasPoint;                  // владелец: есть ли свежая точка касания
    private Vector3 _lastPoint;

    private Vector3 _ownPrevLocal;
    private int _ownPrevTarget;
    private bool _ownHasPrev;
    private float _ownSweep;                 // 0..1, для HUD и частиц у владельца

    /// <summary>Накопленная работа с прошлой отправки, в долях чистки.
    /// Копится КАЖДЫЙ КАДР — в этом весь смысл: сервер опрашивается 20 раз в
    /// секунду, и если мерить только по его пробам, движение туда-обратно
    /// между пробами выглядело бы как стояние на месте.</summary>
    private float _pendingWork;

    private float _passMax;                  // самая правая достигнутая точка прохода
    private bool _hasPass;

    private Vector3 _shakeBasePos;           // исходная поза визуала до тряски
    private Quaternion _shakeBaseRot;
    private bool _shaking;

    private LineRenderer _line;
    private string _status = "—";
    private Color _statusColor = Color.gray;

    private const float SendInterval = 0.05f;   // 20 команд в секунду
    private const float ServerTimeout = 0.3f;   // молчит дольше — цель сбрасываем
    private const float MaxDt = 0.25f;

    private void Awake()
    {
        _tool = GetComponent<ISpinningTool>();
        if (_tool == null)
            Debug.LogError($"[GrinderFoodProcessor] На {name} нет компонента с " +
                           "ISpinningTool. Допиши интерфейс в AngleGrinderItem.", this);

        if (_shakeTarget != null)
        {
            _shakeBasePos = _shakeTarget.localPosition;
            _shakeBaseRot = _shakeTarget.localRotation;
        }

        CreateDebris();
    }

    /// <summary>
    /// Система частиц создаётся В РАНТАЙМЕ из префаба, а не берётся из сцены.
    /// Причина: префаб не может хранить ссылку на сценный объект — как только
    /// болгарку начнут спавнить через NetworkManager, ссылка стала бы null,
    /// и частицы молча исчезли бы.
    ///
    /// Экземпляр НЕ дочерний инструменту и живёт в мировых координатах.
    /// </summary>
    private void CreateDebris()
    {
        _debris = _debrisPrefab != null ? Instantiate(_debrisPrefab) : BuildDebrisSystem();
        if (_debris == null) return;

        _debris.transform.SetParent(null, true);

        ParticleSystem.MainModule main = _debris.main;

        // КРИТИЧНО. При Local частицы намертво привязаны к системе: она едет
        // за диском, и уже вылетевшие ошмётки поехали бы вместе с ней по всей
        // комнате. Форсируем из кода, чтобы не зависеть от галки в инспекторе.
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;

        // Эмиссию гоняем вручную через Emit — автоэмиссию глушим, иначе
        // частицы полетят даже когда инструмент выключен.
        ParticleSystem.EmissionModule em = _debris.emission;
        em.enabled = false;
    }

    /// <summary>
    /// Собирает систему частиц кожуры целиком из кода — в инспекторе настраивать
    /// нечего. Цвет и размер каждой частицы подставляются при вылете из
    /// IngredientDef, поэтому одной системы хватает на все овощи.
    /// </summary>
    private ParticleSystem BuildDebrisSystem()
    {
        GameObject go = new GameObject("FoodDebris (auto)");

        // ВЫКЛЮЧЕННЫМ. Система частиц начинает играть сразу при AddComponent,
        // и часть свойств на играющей менять нельзя — Unity сыплет ошибки.
        go.SetActive(false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 4000;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
        main.startSpeed = 0f;          // скорость задаём поштучно в EmitParams
        main.startSize = 0.02f;        // тоже перекрывается поштучно
        main.gravityModifier = 1.15f;

        // Стартовый поворот у каждого свой, иначе все кусочки летят одинаково
        // ориентированными и это сразу читается как «частицы».
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = false;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;         // позицию задаём вручную

        // Кувыркание в полёте.
        ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.separateAxes = true;
        rot.x = new ParticleSystem.MinMaxCurve(-9f, 9f);
        rot.y = new ParticleSystem.MinMaxCurve(-9f, 9f);
        rot.z = new ParticleSystem.MinMaxCurve(-9f, 9f);

        // Гаснут только в самом конце: кусочки должны успеть долететь и лечь.
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.8f),
                    new GradientAlphaKey(0f, 1f) });
        col.color = g;

        if (_debrisCollide)
        {
            ParticleSystem.CollisionModule cm = ps.collision;
            cm.enabled = true;
            cm.type = ParticleSystemCollisionType.World;
            cm.mode = ParticleSystemCollisionMode.Collision3D;
            // Low — приближённая, но на четырёх сотнях частиц в секунду
            // High сожрала бы кадр.
            cm.quality = ParticleSystemCollisionQuality.Low;
            cm.dampen = 0.6f;
            cm.bounce = 0.15f;
            cm.lifetimeLoss = 0.1f;
        }

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = BuildChunkMesh();
        r.alignment = ParticleSystemRenderSpace.World;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.material = BuildDebrisMaterial();

        go.SetActive(true);
        ps.Play();
        return ps;
    }

    /// <summary>
    /// Кусочек кожуры — плоская пластинка (тонкий параллелепипед). Именно
    /// объёмная, а не квад: квад пропадал бы, повернувшись ребром к камере,
    /// а кусочки кувыркаются.
    /// </summary>
    private static Mesh BuildChunkMesh()
    {
        Mesh m = new Mesh { name = "PeelChunk" };

        const float x = 0.5f, y = 0.12f, z = 0.32f;   // плоская и вытянутая
        Vector3[] c =
        {
            new Vector3(-x, -y, -z), new Vector3( x, -y, -z),
            new Vector3( x, -y,  z), new Vector3(-x, -y,  z),
            new Vector3(-x,  y, -z), new Vector3( x,  y, -z),
            new Vector3( x,  y,  z), new Vector3(-x,  y,  z)
        };

        int[] faces =
        {
            0,2,1, 0,3,2,   // низ
            4,5,6, 4,6,7,   // верх
            0,1,5, 0,5,4,   // бока
            1,2,6, 1,6,5,
            2,3,7, 2,7,6,
            3,0,4, 3,4,7
        };

        Vector3[] verts = new Vector3[faces.Length];
        int[] tris = new int[faces.Length];
        for (int i = 0; i < faces.Length; i++)
        {
            verts[i] = c[faces[i]];
            tris[i] = i;
        }

        m.vertices = verts;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>
    /// Материал должен умножать на цвет вершин — именно через него подставляется
    /// цвет конкретного овоща. Обычный Unlit цвет вершин игнорирует, поэтому
    /// сначала пробуем шейдеры для частиц.
    /// </summary>
    private static Material BuildDebrisMaterial()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Universal Render Pipeline/Particles/Lit")
                 ?? Shader.Find("Particles/Standard Unlit")
                 ?? Shader.Find("Sprites/Default");

        if (sh == null)
        {
            Debug.LogWarning("[GrinderFoodProcessor] Не найден шейдер для частиц — " +
                             "ошмётки будут белыми.");
            return null;
        }

        Material m = new Material(sh) { name = "FoodDebris (auto)" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
        return m;
    }

    private void OnDestroy()
    {
        // Система частиц не дочерняя — сама она не удалится.
        if (_debris != null) Destroy(_debris.gameObject);
    }

    // ───────────────────────── ВЛАДЕЛЕЦ: ПРИЦЕЛ ─────────────────────────

    private void Update()
    {
        if (base.IsServerInitialized) ServerTickTimeout();

        // Ошмётки рисуют ВСЕ по синхронизированной цели.
        DrawDebrisForCurrentTarget();

        if (!base.IsOwner) return;
        OwnerAim();
    }

    private void OwnerAim()
    {
        if (_tool == null) { SetStatus("нет ISpinningTool", Color.red); return; }
        if (_process == null) { SetStatus("не назначен Process", Color.red); return; }

        Transform aim = ResolveAim();
        if (aim == null) { SetStatus("не найдена камера прицела", Color.red); return; }

        float power = _tool.Power01;

        if (!_tool.IsActive)
        {
            SetStatus("инструмент не крутится (зажми ЛКМ)", Color.red);
            DrawLine(aim.position, aim.position + aim.forward * _range);
            SendStopIfNeeded();
            return;
        }

        if (power < _minPower)
        {
            SetStatus($"обороты малы: {power:0.00} < {_minPower:0.00}", new Color(1f, 0.5f, 0f));
            DrawLine(aim.position, aim.position + aim.forward * _range);
            SendStopIfNeeded();
            return;
        }

        AimResult r = Trace(aim.position, aim.forward);
        DrawLine(aim.position, r.EndPoint);

        switch (r.State)
        {
            case AimState.NoHit:
                SetStatus("луч ни во что не попал — наведись на продукт",
                          new Color(1f, 0.6f, 0f));
                SendStopIfNeeded();
                return;

            case AimState.NotFood:
                SetStatus($"попал в «{r.HitName}» — там нет FoodItem", Color.yellow);
                SendStopIfNeeded();
                return;

            case AimState.NoRoute:
                SetStatus($"еда есть, но из состояния «{r.StateLabel}» нет хода " +
                          $"по «{_process.DisplayName}» — дорисуй ребро в графе",
                          new Color(0.4f, 0.7f, 1f));
                SendStopIfNeeded();
                return;
        }

        // Работу считаем КАЖДЫЙ КАДР и копим. Сервер получает уже накопленное,
        // поэтому быстрые движения между отправками не теряются.
        _pendingWork += ComputeWork(r, aim, Mathf.Min(Time.deltaTime, MaxDt));

        if (_ownSweep <= 0.02f)
            SetStatus(_sweepMode == SweepMode.DirectionalPass
                          ? "ВЕДИ СЛЕВА НАПРАВО — медленно, одним движением"
                          : "ВОДИ ПРИЦЕЛОМ по продукту — на месте не чистит",
                      new Color(1f, 0.85f, 0.2f));
        else
            SetStatus($"ЧИСТИТ «{r.StateLabel}» — прогресс {r.Food.Progress01 * 100f:0}%" +
                      $"   темп {_ownSweep * 100f:0}%", Color.green);

        _lastPoint = r.Point;
        _hasPoint = true;
        _grinding = true;

        // Отправка реже кадров, но накопленное не теряется.
        if (Time.time >= _nextSend)
        {
            _nextSend = Time.time + SendInterval;
            CmdGrind(aim.position, aim.forward, _pendingWork);
            _pendingWork = 0f;
        }
    }

    /// <summary>
    /// Стоп шлём РОВНО ОДИН РАЗ. Завязываться на _target.Value нельзя: это
    /// SyncVar, он приезжает с сервера с задержкой, и пока едет — условие
    /// оставалось бы истинным, а CmdStop летел бы каждый кадр. На пинге 100 мс
    /// это десяток лишних RPC, при лаге куда больше.
    /// </summary>
    /// <summary>
    /// Сколько чистки набежало за этот кадр, в долях от целого (0..1).
    /// Считается на владельце покадрово, поэтому ничего не теряется между
    /// отправками. Сервер результат ограничивает сверху.
    /// </summary>
    private float ComputeWork(AimResult r, Transform aim, float dt)
    {
        return _sweepMode == SweepMode.DirectionalPass
            ? WorkDirectional(r, aim, dt)
            : WorkFree(r, dt);
    }

    /// <summary>
    /// Води как угодно. Считается ПРОЙДЕННЫЙ ПУТЬ, а не скорость.
    ///
    /// Почему путь: при пороге по скорости всё, что медленнее порога, давало
    /// ровно ноль — мелкие движения мышью просто не существовали для системы.
    /// С накоплением пути порога нет вообще: каждое движение отдаёт свою долю,
    /// хоть широкий взмах, хоть дрожь руки. А стоять на месте по-прежнему
    /// бесполезно — нулевой путь даёт нулевой прогресс.
    /// </summary>
    private float WorkFree(AimResult r, float dt)
    {
        int id = r.Food.NetworkObject.ObjectId;

        // В ЛОКАЛЬНЫХ координатах продукта: иначе достаточно было бы держать
        // прицел неподвижно, а картошку двигал бы кто-то другой.
        Vector3 local = r.Food.transform.InverseTransformPoint(r.Point);

        if (!_ownHasPrev || _ownPrevTarget != id)
        {
            _ownHasPrev = true;
            _ownPrevTarget = id;
            _ownPrevLocal = local;
            _ownSweep = 0f;
            return 0f;
        }

        // Локальное смещение обратно в метры, чтобы масштаб объекта не искажал
        // замер, и делим на радиус: путь считается в размерах продукта, поэтому
        // морковка и капуста требуют одинакового по ощущению ведения.
        Vector3 moved = r.Food.transform.TransformVector(local - _ownPrevLocal);
        _ownPrevLocal = local;

        float step = moved.magnitude / TargetRadius(r.Food);

        if (step < _sweepDeadzone) step = 0f;              // дрожание луча
        else if (step > _sweepMaxStep) step = _sweepMaxStep; // скачок прицела

        // Показатель для подсказки и для густоты искр — насколько бодро идёт.
        float rate = Mathf.Clamp01(step / Mathf.Max(0.0001f, dt) / 0.6f);
        _ownSweep = Mathf.Lerp(_ownSweep, rate, 1f - Mathf.Exp(-12f * dt));

        return step / Mathf.Max(0.5f, _sweepDistance);
    }

    /// <summary>
    /// Характерный размер продукта в метрах. По нему нормируется путь ведения,
    /// поэтому мелкая морковка и крупная капуста требуют одинакового по
    /// ощущению движения, а не одинакового в метрах.
    /// </summary>
    private static float TargetRadius(FoodItem food)
    {
        Collider col = food.GetComponentInChildren<Collider>();
        return col != null ? Mathf.Max(0.02f, col.bounds.extents.magnitude) : 0.05f;
    }

    /// <summary>
    /// Медленный проход слева направо. Засчитывается только продвижение вправо,
    /// и чем быстрее ведёшь — тем меньше берётся, потому что вести надо медленно.
    /// </summary>
    private float WorkDirectional(AimResult r, Transform aim, float dt)
    {
        int id = r.Food.NetworkObject.ObjectId;
        float u = SweepCoordinate(r, aim);

        if (!_hasPass || _ownPrevTarget != id)
        {
            _hasPass = true;
            _ownPrevTarget = id;
            _passMax = u;
            _ownSweep = 0f;
            return 0f;
        }

        float advance = u - _passMax;

        if (advance <= 0f)
        {
            // Отъехал заметно назад — значит замахнулся на новый проход.
            if (u < _passMax - _passRestartBack) _passMax = u;
            _ownSweep = Mathf.Lerp(_ownSweep, 0f, 1f - Mathf.Exp(-10f * dt));
            return 0f;
        }

        _passMax = u;

        // Слишком быстро — начисляем пропорционально меньше. Так спешка не даёт
        // выигрыша, но и не наказывает полным нулём.
        float speed = advance / Mathf.Max(0.0001f, dt);
        float f = speed > _maxPassSpeed ? Mathf.Clamp01(_maxPassSpeed / speed) : 1f;

        _ownSweep = Mathf.Lerp(_ownSweep, f, 1f - Mathf.Exp(-10f * dt));

        // Полный проход по продукту — это примерно 2 единицы u (от -1 до +1).
        return advance * f / (2f * Mathf.Max(0.25f, _passesNeeded));
    }

    /// <summary>
    /// Положение точки касания по горизонтали ЭКРАНА, примерно -1..1 поперёк
    /// продукта.
    ///
    /// Ось "вправо" берём как Cross(вверх, направление взгляда): у камеры от
    /// первого лица нет крена, поэтому это ровно её правый вектор. И сервер
    /// может вычислить то же самое, зная только присланное направление.
    /// </summary>
    private static float SweepCoordinate(AimResult r, Transform aim)
    {
        Vector3 right = Vector3.Cross(Vector3.up, aim.forward);
        if (right.sqrMagnitude < 0.0001f) right = aim.right;   // смотрим в зенит
        right.Normalize();

        Collider col = r.Food.GetComponentInChildren<Collider>();
        Vector3 center = col != null ? col.bounds.center : r.Food.transform.position;
        float radius = col != null ? Mathf.Max(0.02f, col.bounds.extents.magnitude) : 0.1f;

        return Vector3.Dot(r.Point - center, right) / radius;
    }

    private void SendStopIfNeeded()
    {
        if (!_grinding) return;
        _grinding = false;
        _hasPoint = false;
        _ownHasPrev = false;
        _hasPass = false;
        _ownPrevTarget = 0;
        _ownSweep = 0f;
        _pendingWork = 0f;
        _nextSend = 0f;
        CmdStop();
    }

    /// <summary>
    /// Камера ИМЕННО ТОГО игрока, которому принадлежит инструмент.
    ///
    /// Camera.main берёт первую попавшуюся с тегом MainCamera — при куче камер
    /// в сцене (обзорная, вид из техники) она легко окажется не той. Поэтому
    /// сперва ищем камеру в объекте владельца, и только потом падаем на main.
    /// </summary>
    private Transform ResolveAim()
    {
        if (_aim != null) return _aim;

        if (base.Owner != null && base.Owner.FirstObject != null)
        {
            Camera own = base.Owner.FirstObject.GetComponentInChildren<Camera>(true);
            if (own != null) { _aim = own.transform; return _aim; }
        }

        Camera cam = Camera.main;
        _aim = cam != null ? cam.transform : null;
        return _aim;
    }

    // ───────────────────────── СЕРВЕР ─────────────────────────

    [ServerRpc(RequireOwnership = true)]
    private void CmdGrind(Vector3 origin, Vector3 direction, float work01)
    {
        if (_tool == null || _process == null) return;
        if (!_tool.IsActive || _tool.Power01 < _minPower) { ClearTarget(); return; }

        // Прицел не должен быть где-то в другом конце карты — простая проверка
        // на вменяемость присланных данных.
        if ((origin - transform.position).sqrMagnitude > 25f) { ClearTarget(); return; }

        float dt = _lastCmdTime > 0f
            ? Mathf.Min(Time.time - _lastCmdTime, MaxDt)
            : SendInterval;
        _lastCmdTime = Time.time;

        AimResult r = Trace(origin, direction);
        if (r.State != AimState.Working) { ClearTarget(); return; }

        _target.Value = r.Food.NetworkObject;

        // Работу считает владелец покадрово — иначе быстрые движения между
        // редкими серверными пробами терялись бы, и чистка постоянно замирала.
        // Но верить на слово нельзя: ограничиваем тем, что физически могло
        // набежать за прошедшее время.
        float maxWork = dt / Mathf.Max(0.2f, _minSeconds);
        float work = Mathf.Clamp(work01, 0f, maxWork);

        SetWorkRate(maxWork > 0.0001f ? work / maxWork : 0f);
        if (work <= 0.000001f) return;

        float power = _tool.Power01;
        r.Food.ServerAddProgress(_process, work * power, power);
    }

    private void SetWorkRate(float value01)
    {
        byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(value01) * 255f);

        // Не дёргаем SyncVar на каждую мелочь — только на заметное изменение
        // либо на полную остановку.
        if (v == _workRate.Value) return;
        if (v != 0 && _workRate.Value != 0 && Mathf.Abs(v - _workRate.Value) < 12) return;
        _workRate.Value = v;
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdStop() => ClearTarget();

    /// <summary>Владелец замолчал (отпустил, отвернулся, отвалился по сети) —
    /// цель надо погасить, иначе у всех будут вечно сыпаться ошмётки.</summary>
    private void ServerTickTimeout()
    {
        if (_target.Value == null) return;
        if (Time.time - _lastCmdTime <= ServerTimeout) return;
        ClearTarget();
    }

    private void ClearTarget()
    {
        _lastCmdTime = 0f;
        if (_workRate.Value != 0) _workRate.Value = 0;
        if (_target.Value != null) _target.Value = null;
    }

    // ───────────────────────── ЛУЧ ─────────────────────────

    private enum AimState { NoHit, NotFood, NoRoute, Working }

    private struct AimResult
    {
        public AimState State;
        public FoodItem Food;
        public Vector3 Point;
        public Vector3 EndPoint;
        public string HitName;
        public string StateLabel;
    }

    private AimResult Trace(Vector3 origin, Vector3 dir)
    {
        AimResult res = new AimResult
        {
            State = AimState.NoHit,
            EndPoint = origin + dir * _range
        };

        int n = Physics.RaycastNonAlloc(origin, dir, _hits, _range, _foodMask,
                                        QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;

        for (int i = 0; i < n; i++)
        {
            RaycastHit h = _hits[i];
            if (h.collider == null) continue;
            if (h.collider.transform.IsChildOf(transform)) continue;   // сам инструмент
            if (h.distance >= nearest) continue;

            nearest = h.distance;
            res.EndPoint = h.point;
            res.Point = h.point;
            res.HitName = h.collider.name;

            FoodItem f = h.collider.GetComponentInParent<FoodItem>();
            if (f == null || !f.IsSpawned || f.Consumed)
            {
                res.State = AimState.NotFood;
                res.Food = null;
                continue;
            }

            res.Food = f;
            res.StateLabel = f.Form != null ? f.Form.GetLabel(f.StateIndex) : "?";
            res.State = f.CanApplyNow(_process) ? AimState.Working : AimState.NoRoute;
        }

        return res;
    }

    // ───────────────────────── ПОЗА: ПОДАЧА И ДРОЖЬ ─────────────────────────

    private void LateUpdate()
    {
        // Подача вперёд — только у владельца, она едет к остальным через
        // NetworkTransform. Дрожь — у всех, но по дочернему визуалу.
        if (base.IsOwner) ApplyLean();
        ApplyVibration();
    }

    /// <summary>
    /// Инструмент подаётся вперёд к цели.
    ///
    /// Идёт в LateUpdate — ПОСЛЕ того, как PlayerHand выставил позу в своём
    /// Update, иначе поза перезатёрла бы подачу.
    ///
    /// Только у владельца: остальным поза приезжает уже готовой через
    /// NetworkTransform, и повторное смещение применилось бы дважды.
    /// </summary>
    private void ApplyLean()
    {
        if (_leanDistance <= 0.001f) return;

        Transform aim = ResolveAim();
        bool reaching = aim != null && _target.Value != null;

        float target = reaching ? _leanDistance : 0f;
        _lean = Mathf.MoveTowards(_lean, target, _leanSpeed * Time.deltaTime);

        if (_lean > 0.0001f && aim != null) transform.position += aim.forward * _lean;
    }

    /// <summary>
    /// Дрожь работающего инструмента.
    ///
    /// Через шум Перлина, а не через Random: случайное смещение каждый кадр
    /// выглядит как рябь на изображении, а Перлин даёт связную дрожь, которая
    /// читается как вибрация железки.
    ///
    /// Трясётся ДОЧЕРНИЙ ВИЗУАЛ, а не корень: NetworkTransform гоняет чистую
    /// позу, и каждый клиент накладывает дрожь сам. Если визуал не назначен —
    /// трясём корень, но тогда только у владельца, иначе остальным дрожь
    /// наложилась бы поверх уже дрожащей присланной позиции.
    /// </summary>
    private void ApplyVibration()
    {
        if (_tool == null) { ResetShake(); return; }

        float power = _tool.Power01;
        if (!_tool.IsActive || power <= 0.01f) { ResetShake(); return; }

        // Пилим продукт — трясёт заметно сильнее, чем на холостом ходу.
        float amp = power * (_workRate.Value > 6 ? _vibrateOnContact : 1f);

        float t = Time.time * _vibrateFreq;
        Vector3 pos = new Vector3(Noise(t, 0.13f), Noise(t, 3.71f), Noise(t, 7.29f))
                    * (2f * _vibratePos * amp);
        Vector3 rot = new Vector3(Noise(t, 11.3f), Noise(t, 17.9f), Noise(t, 23.1f))
                    * (2f * _vibrateRot * amp);

        if (_shakeTarget != null)
        {
            _shakeTarget.localPosition = _shakeBasePos + pos;
            _shakeTarget.localRotation = _shakeBaseRot * Quaternion.Euler(rot);
            _shaking = true;
            return;
        }

        if (!base.IsOwner) return;
        transform.position += pos;
        transform.rotation *= Quaternion.Euler(rot);
    }

    /// <summary>Шум Перлина, приведённый к диапазону -0.5..0.5.</summary>
    private static float Noise(float t, float seed) => Mathf.PerlinNoise(t, seed) - 0.5f;

    private void ResetShake()
    {
        if (!_shaking || _shakeTarget == null) return;
        _shakeTarget.localPosition = _shakeBasePos;
        _shakeTarget.localRotation = _shakeBaseRot;
        _shaking = false;
    }

    // ───────────────────────── ОШМЁТКИ ─────────────────────────

    /// <summary>
    /// Ошмётки у ВСЕХ клиентов по синхронизированной цели. Точка контакта
    /// считается локально — ближайшая точка коллайдера цели к диску. По сети
    /// летит только ссылка на цель, и то лишь когда она меняется.
    /// </summary>
    private void DrawDebrisForCurrentTarget()
    {
        NetworkObject t = _target.Value;

        if (t == null)
        {
            StopSound();
            return;
        }

        // Замер на месте — ни искр, ни звука: это и есть обратная связь,
        // по которой понятно, что надо водить.
        float rate = _workRate.Value / 255f;
        if (rate <= 0.02f) { StopSound(); return; }

        // Звук НЕ зависит от частиц: не назначил префаб — пусть хотя бы шумит.
        PlaySound();

        if (_debris == null || _tool == null) return;
        Transform work = _tool.WorkPoint;
        if (work == null) return;

        // Точка касания: у владельца она от собственного луча и точнее.
        // Остальные считают ближайшую точку коллайдера цели к диску — по сети
        // летит только ссылка на цель, и то лишь когда она меняется.
        Vector3 contact;
        if (base.IsOwner && _hasPoint)
        {
            contact = _lastPoint;
        }
        else
        {
            Collider col = t.GetComponentInChildren<Collider>();
            contact = col != null ? col.ClosestPoint(work.position) : t.transform.position;
        }

        // Цвет и размер ошмётков — из справочника продукта. Одна система
        // частиц обслуживает все овощи: у картошки летит коричневое,
        // у свёклы бордовое.
        Color color = new Color(0.78f, 0.66f, 0.45f);
        float size = 0.02f;

        FoodItem food = t.GetComponent<FoodItem>();
        if (food != null && food.Ingredient != null)
        {
            IngredientDef def = food.Ingredient;
            // Альфа 0 = поле в ассете не трогали. Берём заметный цвет,
            // иначе ошмётки были бы прозрачными и их будто бы не было.
            if (def.DebrisColor.a > 0.01f) color = def.DebrisColor;
            if (def.DebrisSize > 0.0001f) size = def.DebrisSize;
        }

        SpawnDebris(contact, work.position, Mathf.Min(Time.deltaTime, 0.05f),
                    _tool.Power01 * rate, color, size);
    }

    private void SpawnDebris(Vector3 contact, Vector3 workPos, float dt, float power,
                             Color color, float size)
    {
        // Физическое направление: от продукта обратно к инструменту.
        Vector3 surface = workPos - contact;
        if (surface.sqrMagnitude < 0.0001f) surface = Vector3.up;
        surface.Normalize();

        // Струя задаётся в экранных координатах, поэтому смешиваем с физикой.
        Vector3 jet = JetDirection();
        Vector3 baseDir = Vector3.Slerp(surface, jet, _jetWeight);
        if (baseDir.sqrMagnitude < 0.0001f) baseDir = surface;
        baseDir.Normalize();

        // Копим дробную часть: при 420 частиц/сек и 60 fps за кадр выходит 7,
        // и без накопления остаток терялся бы каждый кадр.
        _debrisCarry += _debrisPerSecond * Mathf.Max(0.4f, power) * dt;
        int count = Mathf.FloorToInt(_debrisCarry);
        if (count <= 0) return;
        _debrisCarry -= count;

        // Позицию задаём прямо в EmitParams, а не двигаем трансформ системы:
        // при мировом пространстве это одно и то же, но так не задевает
        // уже живые частицы.
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams
        {
            position = contact
        };

        for (int i = 0; i < count; i++)
        {
            // Двухуровневый разброс: большинство идёт кучно по струе, но
            // время от времени кусок прошивает вбок. Один общий угол давал бы
            // ровное облако без характера.
            float spread = Random.value < _wildChance ? _wildSpread : _debrisSpread;
            Vector3 d = RandomInCone(baseDir, spread);

            ep.velocity = d * Random.Range(_debrisSpeed.x, _debrisSpeed.y)
                            * Mathf.Max(0.4f, power);

            // Каждому кусочку своя яркость: кожура неоднородная, и без разброса
            // ошмётки выглядят как крашеная крупа одного тона.
            float shade = Random.Range(_debrisShade.x, _debrisShade.y);
            ep.startColor = new Color(color.r * shade, color.g * shade,
                                      color.b * shade, 1f);

            ep.startSize = size * _debrisSizeMul
                         * Random.Range(_debrisSizeRandom.x, _debrisSizeRandom.y);

            _debris.Emit(ep, 1);
        }
    }

    /// <summary>
    /// Направление струи в координатах ЭКРАНА зрителя.
    ///
    /// Берётся камера ЛОКАЛЬНОГО игрока, а не владельца инструмента: это чистый
    /// визуал, и каждый должен видеть, что кожура летит вверх-влево от своего
    /// экрана. Если считать от камеры владельца, у остальных струя била бы
    /// в произвольную сторону.
    /// </summary>
    private Vector3 JetDirection()
    {
        Transform cam = ViewCamera();
        if (cam == null) return Vector3.up;

        // В плоскости экрана: поворот вектора "вверх" вокруг оси взгляда.
        // Положительный угол уводит влево.
        Vector3 local = Quaternion.AngleAxis(_jetScreenAngle, Vector3.forward) * Vector3.up;

        // И наклон на зрителя: -Z в координатах камеры смотрит из экрана.
        if (_jetTowardCamera > 0.001f)
            local = Vector3.Slerp(local, Vector3.back, _jetTowardCamera).normalized;

        return cam.rotation * local;
    }

    /// <summary>
    /// Равномерная точка внутри конуса. Через Quaternion.Euler со случайными
    /// углами получалось бы сгущение к оси и к полюсам — здесь распределение
    /// честное.
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

    /// <summary>Камера ЛОКАЛЬНОГО игрока — только для визуала струи.</summary>
    private Transform ViewCamera()
    {
        if (_viewCam != null) return _viewCam;
        Camera c = Camera.main;
        _viewCam = c != null ? c.transform : null;
        return _viewCam;
    }

    // ───────────────────────── ЗВУК ─────────────────────────

    private void PlaySound()
    {
        if (_grindLoop != null && !_grindLoop.isPlaying) _grindLoop.Play();
    }

    private void StopSound()
    {
        if (_grindLoop != null && _grindLoop.isPlaying) _grindLoop.Stop();
    }

    // ───────────────────────── ОТЛАДКА ─────────────────────────

    private void CreateDebugLine()
    {
        GameObject go = new GameObject("AimDebugLine");
        go.transform.SetParent(transform, false);

        _line = go.AddComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _line.positionCount = 2;
        _line.widthMultiplier = 0.006f;
        _line.numCapVertices = 2;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                 ?? Shader.Find("Sprites/Default")
                 ?? Shader.Find("Unlit/Color");
        if (sh != null) _line.material = new Material(sh);
    }

    private void DrawLine(Vector3 a, Vector3 b)
    {
        // Линия создаётся лениво и ТОЛЬКО у владельца: DrawLine зовётся лишь
        // из его кода, а у остальных LineRenderer остался бы неинициализированным
        // и рисовал точку в начале координат.
        if (_line == null)
        {
            if (!_debugLine) return;
            CreateDebugLine();
            if (_line == null) return;
        }
        _line.SetPosition(0, a);
        _line.SetPosition(1, b);
        _line.startColor = _statusColor;
        _line.endColor = _statusColor;
    }

    private void SetStatus(string text, Color color)
    {
        _status = text;
        _statusColor = color;
    }

    private void OnGUI()
    {
        if (!_debugHud || !base.IsOwner) return;

        GUIStyle st = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 14
        };
        st.normal.textColor = _statusColor;

        GUI.Box(new Rect(12f, 12f, 620f, 44f),
                $"Болгарка → {_status}\n" +
                $"дальность {_range:0.0} м   провести надо {_sweepDistance:0.#} радиусов", st);
    }
}
