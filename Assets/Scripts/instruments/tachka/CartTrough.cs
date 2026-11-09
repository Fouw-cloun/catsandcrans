using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// КОРЫТО ТАЧКИ. Груз лежит в нём НАСТОЯЩИМИ физическими телами: сваливается
/// кучей, перекатывается на кочках, высыпается, когда тачку наклонили.
///
/// ═══ ДВА РЕЖИМА, И ПОЧЕМУ ПОБЕДИЛИ ТОЧКИ ═══
///
/// Изначально груз ездил чистой физикой: сваливался кучей, перекатывался,
/// высыпался сам. Это давало живую картинку бесплатно — но и три беды, каждая
/// из которых неустранима в принципе, а не по недосмотру:
///
///   ДРОЖЬ. Контакт груза со стенками ДВИЖУЩЕГОСЯ корыта всегда чуть дрожит.
///   Это нормальная работа солвера, крошечные поправки на каждом шаге, и они
///   есть в любом физдвижке. Убрать их нельзя, можно только прятать.
///
///   СЕТЬ. Десяток тел с client-authoritative синхронизацией — это десяток
///   потоков поправок на каждую тачку, и любое расхождение видно как
///   просачивание сквозь дно.
///
///   ПРОСАЧИВАНИЕ. Быстрое корыто, быстрый груз, тонкие стенки. Continuous
///   Dynamic делает это редким, но не невозможным.
///
/// РЕЖИМ ОСЕДАНИЯ (по умолчанию) убирает все три, ничего не отняв. Предмет
/// падает в тачку обычным телом, скатывается и укладывается КАК ЛЯЖЕТ — а
/// через пару секунд, когда всё успокоилось, замирает на своём месте и дальше
/// едет как часть тачки.
///
/// Главное в нём: КУЧУ ПО-ПРЕЖНЕМУ СТРОИТ ФИЗИКА. Никто заранее не решает,
/// куда что положить, поэтому в тачку можно валить предметы любых размеров и
/// форм. Замораживается только РЕЗУЛЬТАТ укладки, и ровно тогда, когда он уже
/// сложился.
///
/// РЕЖИМ ТОЧЕК делает то же самое телепортацией в расставленные места. Кладка
/// выходит идеально ровной, но работает только под предметы известного
/// размера — под разнокалиберный хлам места не угадать.
///
/// А ЧТО ПОТЕРЯЛИ. Ничего из того, ради чего физика бралась:
///
///   • куча растёт сама и выглядит как куча;
///   • перевернули — груз мгновенно возвращается в физику и вылетает
///     настоящими телами, с пинком и закруткой;
///   • вес и смещение центра тяжести считаются по тем же телам, и мини-игра
///     на баланс работает ровно как работала.
///
/// То есть физика никуда не делась — она включается ровно в тот момент, когда
/// она и нужна: когда груз перестаёт быть грузом и становится летящими
/// предметами. Всё остальное время платить за неё не за что.
///
/// Чистая физика остаётся отдельным режимом — на случай, если живая
/// перекатывающаяся куча важнее спокойной картинки.
///
/// ═══ УСЛОВИЕ, БЕЗ КОТОРОГО ЭТО НЕ РАБОТАЕТ ═══
///
/// Тачку обязан двигать Rigidbody.MovePosition, а НЕ transform.position.
/// Присваивание позиции — это телепортация: тело переносится, ничего по пути не
/// задевая. Груз остался бы на месте, а стенки прошли бы сквозь него.
/// MovePosition выметает объём и толкает динамику как положено. См. PushCart.
///
/// ═══ ЧТО ЗДЕСЬ ВООБЩЕ ДЕЛАЕТ СКРИПТ, ЕСЛИ ВСЁ ДЕЛАЕТ ФИЗИКА ═══
///
/// Три вещи, которые физика сама не сделает:
///
///   1. ВЛАДЕНИЕ. Груз — сетевые объекты с client-authoritative
///      NetworkTransform. Пока их симулирует одна машина, всё хорошо; как
///      только тачку катит один клиент, а груз считает сервер, они начинают
///      спорить, и куча дрожит. Поэтому груз передаётся во владение тому, кто
///      взялся за ручки.
///
///   2. ЁМКОСТЬ. Физика с радостью примет сорок полений и превратит корыто в
///      кашу. Считаем, что внутри, и лишнее не пускаем.
///
///   3. СОН. Стоящая тачка не должна жечь процессор на десяти телах, которые
///      никуда не едут. Усыпляем груз, пока никто не катит.
/// </summary>
public class CartTrough : NetworkBehaviour
{
    /// <summary>Чем груз является, пока едет.</summary>
    public enum CargoMode
    {
        /// <summary>Всё время живая физика. Как было изначально.</summary>
        Physics = 0,

        /// <summary>Падает физикой, потом замирает там, где лёг.</summary>
        Settle = 1,

        /// <summary>Телепортируется в расставленные места.</summary>
        Slots = 2,
    }

    [Header("Зона корыта")]
    [Tooltip("Триггер ВНУТРИ корыта, повторяющий его объём.\n\n" +
             "Он не ловит и не поглощает — он только СЧИТАЕТ, что внутри. " +
             "Держать груз обязаны стенки-коллайдеры, а не этот триггер.")]
    [SerializeField] private Collider _volume;

    [Tooltip("Сколько предметов влезает. Физика примет и сорок, превратив " +
             "корыто в кашу, поэтому потолок нужен.")]
    [Min(1)][SerializeField] private int _maxItems = 12;

    [Tooltip("Сколько килограммов влезает. Что раньше упрётся — счёт или вес.")]
    [Min(1f)][SerializeField] private float _maxMassKg = 160f;

    [Header("Как везётся груз")]
    [Tooltip("ОСЕДАНИЕ — предмет падает в тачку обычным телом, укладывается " +
             "как ляжет, и через пару секунд ЗАМИРАЕТ на своём месте. Куча " +
             "формируется физикой, а везётся уже как одно целое. Размеры и " +
             "формы могут быть любыми: никто заранее не решает, куда что " +
             "положить.\n\n" +
             "ТОЧКИ — предмет телепортируется в заранее расставленное место. " +
             "Идеально ровная кладка, но только под предметы известного " +
             "размера.\n\n" +
             "ФИЗИКА — ничего не замирает, груз живёт всё время. Живее всех " +
             "прочих и дрожит соответственно.")]
    [SerializeField] private CargoMode _mode = CargoMode.Settle;

    [Header("Оседание")]
    [Tooltip("ЧЕРЕЗ СКОЛЬКО СЕКУНД ПОСЛЕ ПОПАДАНИЯ ЗАМИРАТЬ.\n\n" +
             "Это время, за которое предмет должен успеть скатиться и лечь. " +
             "Слишком мало — замрёт в воздухе на полпути; слишком много — " +
             "игрок успеет заметить дрожь, ради избавления от которой всё и " +
             "делается.")]
    [Range(0.2f, 6f)][SerializeField] private float _settleSeconds = 2f;

    [Tooltip("НАСКОЛЬКО МЕДЛЕННО ДОЛЖЕН ДВИГАТЬСЯ ПРЕДМЕТ, чтобы считаться " +
             "улёгшимся, м/с.\n\n" +
             "Один таймер без этой проверки замораживал бы предмет, который " +
             "всё ещё катится, — прямо в движении, посреди корыта. Условия " +
             "работают вместе: время вышло И предмет успокоился.")]
    [Range(0.01f, 2f)][SerializeField] private float _settleMaxSpeed = 0.35f;

    [Tooltip("ПОТОЛОК ОЖИДАНИЯ, секунды. Предмет, который никак не " +
             "успокоится — застрял между стенкой и соседом и мелко " +
             "подрагивает, — замораживается принудительно.\n\n" +
             "Без потолка ровно такой предмет и остался бы единственным " +
             "дрожащим в тачке, то есть самым заметным.")]
    [Range(1f, 20f)][SerializeField] private float _settleHardCap = 6f;

    [Tooltip("РАСТРЯСАТЬ КУЧУ ПРИ СПОТЫКАНИИ. Включено — на каждой сильной " +
             "встряске груз оживает и укладывается заново.\n\n" +
             "Выглядит живее, но и стоит дороже: после каждой кочки куча снова " +
             "проходит все секунды оседания, и всё это время она дрожит.")]
    [SerializeField] private bool _thawOnStumble = false;

    [Header("Складирование по точкам")]

    [Tooltip("РОДИТЕЛЬ ТОЧЕК. Каждый его прямой ребёнок — одно место под " +
             "предмет.\n\n" +
             "ПОРЯДОК В ИЕРАРХИИ = ПОРЯДОК ЗАПОЛНЕНИЯ. Ставь нижний ряд " +
             "первым, верхние следом — тогда пирамидка будет расти снизу " +
             "вверх сама, безо всякого кода. Поворот точки задаёт поворот " +
             "предмета: разверни их немного по-разному, и кладка перестанет " +
             "выглядеть машинной.\n\n" +
             "Выключенные объекты пропускаются — так удобно временно " +
             "уменьшить вместимость, не удаляя точки.")]
    [SerializeField] private Transform _slotRoot;

    [Tooltip("Доворачивать предмет по точке. Выключи, если предметы должны " +
             "сохранять тот поворот, с которым прилетели.")]
    [SerializeField] private bool _alignRotation = true;

    [Tooltip("БЫСТРОТА ПРИТЯГИВАНИЯ. Высокие значения — мгновенный щелчок; " +
             "пониже — предмет заметно подлетает к месту.\n\n" +
             "Полностью мгновенный перенос читается как подмена предмета, " +
             "поэтому даже очень короткий подлёт лучше нуля.")]
    [Range(1f, 60f)][SerializeField] private float _snapSpeed = 22f;

    [Header("Физика груза")]
    [Tooltip("ОБЯЗАТЕЛЬНО. Груз в движущемся корыте летает быстро относительно " +
             "стенок, и на дискретном определении столкновений он их прошьёт.")]
    [SerializeField] private bool _forceContinuous = true;

    [Tooltip("Усыплять груз, когда тачку никто не катит. Стоящая тачка не " +
             "должна жечь процессор на телах, которые никуда не едут.")]
    [SerializeField] private bool _sleepWhenIdle = true;

    [Header("Микровзрыв при вываливании")]
    [Tooltip("Импульс, который получает груз в момент опрокидывания, м/с.\n\n" +
             "Гравитация высыпает всё и без него, но куча сползает вяло. " +
             "Небольшой пинок делает это событием.")]
    [SerializeField] private float _dumpKick = 1.6f;

    [Tooltip("Разброс направления пинка")]
    [Range(0f, 1f)][SerializeField] private float _dumpSpread = 0.45f;

    [Tooltip("Закрутка при вываливании, рад/с")]
    [SerializeField] private float _dumpSpin = 3f;

    [Header("Отладка")]
    [SerializeField] private bool _debugLog;
    [SerializeField] private bool _debugGizmo = true;

    /// <summary>Что сейчас в корыте. Только на сервере: клиентам это не нужно,
    /// они и так видят предметы физически.</summary>
    private readonly List<PickupItem> _inside = new List<PickupItem>();

    private PushCart _cart;

    /// <summary>Места под груз, в порядке иерархии.</summary>
    private readonly List<Transform> _slots = new List<Transform>();

    /// <summary>
    /// КТО В КАКОМ МЕСТЕ ЛЕЖИТ. Индекс совпадает с индексом точки, null —
    /// место свободно.
    ///
    /// Синхронизируемый список, а не обычный: клиенту нужно знать раскладку,
    /// чтобы прижимать предметы у себя. Через RPC это не решается — тот, кто
    /// подключился позже, события не услышит и увидит гружёную тачку пустой.
    /// </summary>
    private readonly SyncList<NetworkObject> _slotted = new SyncList<NetworkObject>();

    /// <summary>Кого мы сделали кинематическим У СЕБЯ. Нужен, чтобы вернуть
    /// физику ровно тем, кого выложили, и не трогать чужое.</summary>
    private readonly HashSet<NetworkObject> _pinnedLocal = new HashSet<NetworkObject>();

    /// <summary>
    /// КТО УЖЕ ЗАМЕР. Только список, без поз.
    ///
    /// Позу каждая машина снимает СВОЮ, в тот момент, когда впервые видит
    /// предмет в этом списке. Присылать позу по сети было бы точнее на
    /// сантиметры и хуже по сути: приехавшая позиция всегда чуть отстаёт от
    /// того места, где корыто нарисовано у ЭТОГО игрока, и груз бы плавал
    /// внутри. Лучше стабильная куча со сдвигом в пару сантиметров между
    /// клиентами, чем идеально одинаковая и плавающая у каждого.
    /// </summary>
    private readonly SyncList<NetworkObject> _frozen = new SyncList<NetworkObject>();

    /// <summary>Поза замерших ОТНОСИТЕЛЬНО КОРЫТА. Снимается один раз.</summary>
    private readonly Dictionary<NetworkObject, Pose> _localPose =
        new Dictionary<NetworkObject, Pose>();

    /// <summary>Сколько предмет уже лежит в тачке. Только на сервере.</summary>
    private readonly Dictionary<PickupItem, float> _settleTimer =
        new Dictionary<PickupItem, float>();

    private readonly List<NetworkObject> _scratch = new List<NetworkObject>();
    private readonly List<PickupItem> _stale = new List<PickupItem>();

    private bool UsingSlots => _mode == CargoMode.Slots && _slots.Count > 0;
    private bool UsingSettle => _mode == CargoMode.Settle;

    public int Count => _inside.Count;

    /// <summary>Сколько влезет. В режиме точек их число — жёсткий потолок:
    /// класть некуда, сколько бы ни разрешал счётчик.</summary>
    public int Capacity => UsingSlots ? Mathf.Min(_slots.Count, _maxItems) : _maxItems;

    /// <summary>Сколько предметов уже замерло — для отладки и интерфейса.</summary>
    public int Settled => _frozen.Count;

    public bool IsFull => Count >= Capacity || MassKg >= _maxMassKg;
    public bool IsEmpty => Count == 0;

    /// <summary>Общий вес груза. Понадобится балансу: гружёная тачка должна
    /// быть тяжелее не только на словах.</summary>
    public float MassKg
    {
        get
        {
            float m = 0f;
            foreach (PickupItem it in _inside)
            {
                if (it == null) continue;
                Rigidbody rb = it.GetComponent<Rigidbody>();
                if (rb != null) m += rb.mass;
            }
            return m;
        }
    }

    /// <summary>
    /// Смещение центра тяжести груза от оси колеса, метры. Вперёд — плюс,
    /// вправо — по X.
    ///
    /// Пока никем не читается, но это готовая основа для мини-игры на баланс:
    /// перегрузил перед — тачка клюёт носом, сместил вбок — заваливается.
    /// </summary>
    public Vector3 LoadOffset
    {
        get
        {
            if (_inside.Count == 0) return Vector3.zero;

            Vector3 sum = Vector3.zero;
            float total = 0f;

            foreach (PickupItem it in _inside)
            {
                if (it == null) continue;
                Rigidbody rb = it.GetComponent<Rigidbody>();
                float m = rb != null ? rb.mass : 1f;
                sum += it.transform.position * m;
                total += m;
            }

            if (total <= 0.001f) return Vector3.zero;
            return transform.InverseTransformPoint(sum / total);
        }
    }

    private void Awake()
    {
        _cart = GetComponentInParent<PushCart>();

        if (_volume == null)
            Debug.LogWarning($"[Корыто] «{name}»: не назначен объём — считать " +
                             "груз будет нечем.", this);
        else
            _volume.isTrigger = true;

        CollectSlots();
    }

    /// <summary>
    /// Точки берутся ПО ИЕРАРХИИ, в порядке детей. Не по именам и не по
    /// сортировке высоты: порядок заполнения — это художественное решение, и
    /// автор должен задавать его перетаскиванием, а не подбором названий.
    /// </summary>
    private void CollectSlots()
    {
        _slots.Clear();

        if (_slotRoot != null)
            foreach (Transform t in _slotRoot)
                if (t != null && t.gameObject.activeSelf) _slots.Add(t);

        if (_mode == CargoMode.Slots && _slots.Count == 0)
            Debug.LogWarning(
                $"[Корыто] «{name}»: выбран режим точек, но ни одной точки " +
                "нет. Назначь Slot Root и положи в него пустышки по числу " +
                "мест.\n\n" +
                "Пока их нет, груз ездит чистой физикой — то есть дрожит. " +
                "Если точки не нужны, переключи Cargo Mode на Settle: там " +
                "куча укладывается сама и замирает, ничего расставлять не " +
                "надо.", this);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Список заводится РОВНО ПОД ЧИСЛО ТОЧЕК и дальше только меняет
        // содержимое ячеек. Добавлять и удалять на ходу нельзя: индекс ячейки
        // и индекс точки должны совпадать всегда, иначе груз поедет по чужим
        // местам.
        _slotted.Clear();
        for (int i = 0; i < _slots.Count; i++) _slotted.Add(null);
    }

    // ───────────────────────── УЧЁТ ─────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (!base.IsServerInitialized || other == null) return;

        PickupItem item = other.GetComponentInParent<PickupItem>();
        if (item == null || _inside.Contains(item)) return;

        NetworkObject no = item.NetworkObject;
        if (no == null || !no.IsSpawned) return;

        // Проверки «а не в руке ли он» здесь нет и не нужно: у предмета в руке
        // коллайдеры выключены, а выключенный коллайдер триггеров не порождает.
        // Пройти мимо тачки с поленом и потерять его невозможно физически.

        if (IsFull)
        {
            // НЕ ВЫТАЛКИВАЕМ. Предмет просто ляжет сверху и скатится сам —
            // переполненная тачка и должна выглядеть переполненной. Мы лишь не
            // берём его на учёт, чтобы он не уехал вместе с тачкой.
            if (_debugLog)
                Debug.Log($"[Корыто] «{name}»: полно ({Count} шт, {MassKg:0} кг)", this);
            return;
        }

        Accept(item);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!base.IsServerInitialized || other == null) return;

        PickupItem item = other.GetComponentInParent<PickupItem>();
        if (item == null) return;

        Release(item);
    }

    [Server]
    private void Accept(PickupItem item)
    {
        _inside.Add(item);

        Rigidbody rb = item.GetComponent<Rigidbody>();
        if (rb != null)
        {
            if (_forceContinuous)
            {
                // ДИСКРЕТНОЕ ОПРЕДЕЛЕНИЕ ЗДЕСЬ НЕ РАБОТАЕТ. Груз в катящемся корыте
                // движется быстро ОТНОСИТЕЛЬНО стенок, и за кадр успевает
                // перескочить сквозь них. Continuous Dynamic дороже, но десяток тел
                // это выдержит.
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }

            // ГЛАВНОЕ ПРОТИВ ВИБРАЦИИ. Контакт груза со стенками движущегося
            // корыта физически ВСЕГДА чуть дрожит — это нормальная работа
            // солвера, крошечные поправки на каждом шаге неизбежны даже у
            // Unity/Unreal/любого физдвижка. Разница в том, ВИДНО ли это.
            //
            // Тачка (её Rigidbody) уже интерполирована — PushCart сам ставит
            // это в Awake. Если у ГРУЗА интерполяции нет (Unity по умолчанию
            // ставит None на любой новый Rigidbody), его визуальная позиция
            // дёргается СИНХРОННО с каждым шагом физики без сглаживания —
            // и тогда те самые нормальные микропоправки становятся видимой
            // тряской. Заставляем интерполировать здесь программно, а не
            // полагаемся на то, что это выставили на каждом префабе руками:
            // один забытый префаб — и именно он будет "странно дрожать".
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        if (UsingSlots) TakeSlot(item);
        else if (UsingSettle) _settleTimer[item] = 0f;

        SyncOwnership(item);

        if (_debugLog)
            Debug.Log($"[Корыто] «{name}» + «{item.name}» " +
                      $"({Count} шт, {MassKg:0} кг)", this);
    }

    [Server]
    private void Release(PickupItem item)
    {
        if (!_inside.Remove(item)) return;

        FreeSlot(item.NetworkObject);
        Thaw(item.NetworkObject);
        _settleTimer.Remove(item);

        // Владение возвращаем серверу: предмет больше не едет с тачкой, и
        // держать его за клиентом незачем.
        NetworkObject no = item.NetworkObject;
        if (no != null && no.IsSpawned) no.RemoveOwnership();

        if (_debugLog)
            Debug.Log($"[Корыто] «{name}» − «{item.name}» ({Count} шт)", this);
    }

    // ───────────────────────── МЕСТА ─────────────────────────

    /// <summary>Положить предмет в первое свободное место.</summary>
    [Server]
    private void TakeSlot(PickupItem item)
    {
        NetworkObject no = item.NetworkObject;
        if (no == null) return;

        for (int i = 0; i < _slotted.Count; i++)
        {
            if (_slotted[i] != null) continue;

            _slotted[i] = no;
            return;
        }

        // Мест нет. Предмет останется обычным телом и просто ляжет сверху —
        // ровно как в старом режиме. Выталкивать его незачем: переполненная
        // тачка и должна выглядеть переполненной.
    }

    [Server]
    private void FreeSlot(NetworkObject no)
    {
        if (no == null) return;

        for (int i = 0; i < _slotted.Count; i++)
            if (_slotted[i] == no) _slotted[i] = null;
    }

    /// <summary>
    /// ВЕРНУТЬ ПРЕДМЕТУ ФИЗИКУ.
    ///
    /// Предмет В РУКЕ не трогаем. Опознаём по выключенному коллайдеру: так его
    /// выключает InteractableItem, когда предмет берут. Иначе вышло бы вот
    /// что — игрок вытаскивает полено прямо из тачки, корыто честно замечает
    /// уход и «возвращает физику» тому, что уже лежит в руке. Полено оживает
    /// посреди ладони и падает.
    /// </summary>
    private void Unpin(NetworkObject no)
    {
        if (no == null) return;

        Collider col = no.GetComponentInChildren<Collider>();
        if (col != null && !col.enabled) return;

        Rigidbody rb = no.GetComponent<Rigidbody>();
        if (rb == null) return;

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.WakeUp();
    }

    /// <summary>
    /// ПРИЖИМАНИЕ. Идёт у ВСЕХ, а не только у хозяина.
    ///
    /// Место — это точка внутри тачки, а тачка у каждого своя и уже
    /// интерполирована. Считать позу локально дешевле и ТОЧНЕЕ, чем присылать
    /// её по сети: приехавшая позиция всегда чуть отстаёт от того места, где
    /// корыто нарисовано именно у этого игрока, и груз бы плавал внутри.
    ///
    /// LateUpdate, а не Update: ходовая пишет кузов именно здесь, и груз
    /// должен встать ПОСЛЕ неё. Иначе он будет отставать от корыта ровно на
    /// кадр — а это и есть заметная дрожь, ради избавления от которой всё
    /// затевалось.
    /// </summary>
    private void LateUpdate()
    {
        if (_mode == CargoMode.Physics) return;

        float k = 1f - Mathf.Exp(-_snapSpeed * Mathf.Max(0.0001f, Time.deltaTime));

        if (UsingSlots) PinToSlots(k);
        if (UsingSettle) HoldFrozen();

        ReleaseUnpinned();
    }

    /// <summary>Груз стоит в назначенных местах.</summary>
    private void PinToSlots(float k)
    {
        int n = Mathf.Min(_slots.Count, _slotted.Count);

        for (int i = 0; i < n; i++)
        {
            NetworkObject no = _slotted[i];
            Transform slot = _slots[i];

            if (no == null || slot == null) continue;

            Freeze(no);
            _pinnedLocal.Add(no);

            Transform t = no.transform;

            t.SetPositionAndRotation(
                Vector3.Lerp(t.position, slot.position, k),
                _alignRotation ? Quaternion.Slerp(t.rotation, slot.rotation, k)
                               : t.rotation);
        }
    }

    /// <summary>
    /// ЗАМЕРШИЙ ГРУЗ ДЕРЖИТСЯ ТАМ, ГДЕ ЛЁГ.
    ///
    /// Поза снимается ОДИН РАЗ, в тот кадр, когда предмет впервые появился в
    /// списке замерших, и хранится ОТНОСИТЕЛЬНО КОРЫТА. Дальше она просто
    /// разворачивается обратно в мир каждый кадр — поэтому груз едет с тачкой
    /// точно, включая повороты, качку и наклон на одно колесо, и при этом не
    /// участвует в физике вообще.
    ///
    /// Без снятия позы пришлось бы каждый кадр спрашивать у физики, где
    /// предмет, — а он там ровно потому и дрожал.
    /// </summary>
    private void HoldFrozen()
    {
        for (int i = 0; i < _frozen.Count; i++)
        {
            NetworkObject no = _frozen[i];
            if (no == null) continue;

            Transform t = no.transform;

            if (!_localPose.TryGetValue(no, out Pose p))
            {
                p = new Pose(transform.InverseTransformPoint(t.position),
                             Quaternion.Inverse(transform.rotation) * t.rotation);

                _localPose[no] = p;
            }

            Freeze(no);
            _pinnedLocal.Add(no);

            t.SetPositionAndRotation(transform.TransformPoint(p.position),
                                     transform.rotation * p.rotation);
        }
    }

    /// <summary>Сделать тело кинематическим, если оно ещё не.</summary>
    private void Freeze(NetworkObject no)
    {
        Rigidbody rb = no.GetComponent<Rigidbody>();
        if (rb == null || rb.isKinematic) return;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
    }

    /// <summary>
    /// Кого выложили — тому вернуть физику.
    ///
    /// Проверяем СВОИМ списком, а не синхронизируемым: вернуть тело в игру
    /// обязан тот же, кто его оттуда забрал. Иначе на клиенте, подключившемся
    /// в середине, чужая кинематика осталась бы навсегда.
    /// </summary>
    private void ReleaseUnpinned()
    {
        if (_pinnedLocal.Count == 0) return;

        _scratch.Clear();

        foreach (NetworkObject no in _pinnedLocal)
            if (no == null || (!_slotted.Contains(no) && !_frozen.Contains(no)))
                _scratch.Add(no);

        foreach (NetworkObject no in _scratch)
        {
            _pinnedLocal.Remove(no);
            _localPose.Remove(no);
            Unpin(no);
        }
    }

    // ───────────────────────── ОСЕДАНИЕ ─────────────────────────

    /// <summary>
    /// КТО УЖЕ УЛЁГСЯ И ПОРА ЛИ ЕГО ЗАМОРАЖИВАТЬ. Только сервер.
    ///
    /// ═══ ПОЧЕМУ ДВА УСЛОВИЯ, А НЕ ОДНО ═══
    ///
    /// Один таймер заморозил бы предмет, который всё ещё катится — прямо в
    /// движении, посреди корыта, и он завис бы в воздухе. Одна проверка
    /// скорости сработала бы в первый же кадр контакта, когда предмет ещё
    /// только коснулся дна и на мгновение замер перед отскоком.
    ///
    /// Вместе они означают ровно то, что нужно: «упал, полежал, никуда не
    /// едет».
    ///
    /// ═══ ЗАЧЕМ ПОТОЛОК ═══
    ///
    /// Предмет, застрявший между стенкой и соседом, может подрагивать
    /// бесконечно и НИКОГДА не пройти проверку на скорость. Без потолка именно
    /// он остался бы единственным дрожащим предметом в тачке — то есть самым
    /// заметным. Потолок замораживает такие принудительно.
    /// </summary>
    [Server]
    private void TickSettle(float dt)
    {
        foreach (PickupItem it in _inside)
        {
            if (it == null) continue;

            NetworkObject no = it.NetworkObject;
            if (no == null || _frozen.Contains(no)) continue;

            if (!_settleTimer.TryGetValue(it, out float t)) t = 0f;
            t += dt;
            _settleTimer[it] = t;

            if (t < _settleSeconds) continue;

            Rigidbody rb = it.GetComponent<Rigidbody>();

            bool calm = rb == null
                     || rb.IsSleeping()
                     || rb.linearVelocity.sqrMagnitude <= _settleMaxSpeed * _settleMaxSpeed;

            if (calm || t >= _settleHardCap) _frozen.Add(no);
        }
    }

    /// <summary>Вернуть предмет в физику: убрать из списка замерших.</summary>
    [Server]
    private void Thaw(NetworkObject no)
    {
        if (no == null) return;
        _frozen.Remove(no);
    }

    /// <summary>
    /// РАСТРЯСТИ ВСЮ КУЧУ. Груз оживает и укладывается заново.
    ///
    /// Зовётся при опрокидывании — там это обязательно, иначе замершие
    /// предметы останутся висеть в перевёрнутой тачке, — и по желанию при
    /// сильной встряске.
    /// </summary>
    [Server]
    public void ServerThawAll()
    {
        if (_frozen.Count == 0) return;

        _frozen.Clear();

        // Таймеры сбрасываем: куче надо дать те же секунды на новую укладку,
        // иначе первый же кадр после встряски заморозит её обратно в том же
        // положении, и растряска ничего не изменит.
        foreach (PickupItem it in _inside)
            if (it != null) _settleTimer[it] = 0f;
    }

    // ───────────────────────── ВЛАДЕНИЕ ─────────────────────────

    /// <summary>
    /// Груз должен считаться ТОЙ ЖЕ машиной, что и тачка.
    ///
    /// Иначе выходит спор: тачку двигает клиент того, кто взялся за ручки, а
    /// груз симулирует сервер. Каждый видит свою версию столкновений со
    /// стенками, и куча начинает дрожать и просачиваться. Отдаём груз во
    /// владение водителю — и вся сцена считается в одном месте.
    /// </summary>
    [Server]
    public void ServerSyncAllOwnership()
    {
        for (int i = _inside.Count - 1; i >= 0; i--)
        {
            if (_inside[i] == null) { _inside.RemoveAt(i); continue; }
            SyncOwnership(_inside[i]);
        }
    }

    [Server]
    private void SyncOwnership(PickupItem item)
    {
        NetworkObject no = item.NetworkObject;
        if (no == null || !no.IsSpawned) return;

        NetworkObject cartNo = _cart != null ? _cart.NetworkObject : null;

        if (cartNo != null && cartNo.Owner != null && cartNo.Owner.IsValid)
            no.GiveOwnership(cartNo.Owner);
        else
            no.RemoveOwnership();
    }

    // ───────────────────────── ВЫВАЛИВАНИЕ ─────────────────────────

    /// <summary>
    /// Пинок грузу в момент опрокидывания.
    ///
    /// Формально он не нужен: наклонили корыто — гравитация высыплет всё сама.
    /// Но сползающая куча выглядит вяло, а событие должно ощущаться событием.
    /// Пинок направлен ВПЕРЁД И ВВЕРХ от корыта, так что это ускорение того же
    /// движения, а не подмена его.
    /// </summary>
    [Server]
    public void ServerKickOut()
    {
        ServerThawAll();

        // СНАЧАЛА ВЫПУСТИТЬ ИЗ МЕСТ. Пинать кинематическое тело бессмысленно —
        // импульс оно проглотит, и груз останется висеть в перевёрнутой тачке.
        //
        // Освобождение идёт через список: как только ячейка опустела, все
        // машины у себя вернут телу физику сами, каждая в своём LateUpdate.
        for (int i = 0; i < _slotted.Count; i++)
        {
            NetworkObject no = _slotted[i];
            if (no == null) continue;

            _slotted[i] = null;
            Unpin(no);
        }

        foreach (PickupItem it in _inside)
        {
            if (it == null) continue;

            Rigidbody rb = it.GetComponent<Rigidbody>();
            if (rb == null) continue;

            // ВОЗВРАЩАЕМ ФИЗИКУ ПРЯМО ЗДЕСЬ, не дожидаясь LateUpdate.
            //
            // Списки уже очищены, и через кадр всё разморозилось бы само — но
            // пинок раздаётся СЕЙЧАС, а кинематическое тело его проглотит.
            // Груз остался бы висеть в перевёрнутой тачке, и лишь потом
            // осыпался бы без всякого импульса.
            if (rb.isKinematic)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
            }

            rb.WakeUp();

            Vector3 dir = (transform.forward
                           + Vector3.up * 0.35f
                           + Random.insideUnitSphere * _dumpSpread).normalized;

            rb.linearVelocity += dir * _dumpKick;
            rb.angularVelocity += Random.insideUnitSphere * _dumpSpin;
        }

        if (_debugLog) Debug.Log($"[Корыто] «{name}»: пинок {Count} предметам", this);
    }

    /// <summary>Растолкать груз — зовётся, когда тачку тряхнуло. Без этого
    /// уснувшие тела не заметят толчка: спящий Rigidbody не реагирует на
    /// движение соседей, пока его не разбудят.</summary>
    [Server]
    public void ServerWakeCargo()
    {
        if (_thawOnStumble) ServerThawAll();

        foreach (PickupItem it in _inside)
        {
            if (it == null) continue;
            Rigidbody rb = it.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) rb.WakeUp();
        }
    }

    // ───────────────────────── СОН ─────────────────────────

    private void Update()
    {
        if (!base.IsServerInitialized) return;

        // Подчищаем исчезнувшее: предмет могли сжечь или деспавнить прямо
        // в корыте, и OnTriggerExit тогда не придёт.
        for (int i = _inside.Count - 1; i >= 0; i--)
            if (_inside[i] == null) _inside.RemoveAt(i);

        if (UsingSettle)
        {
            // Замершие тела не спят и не двигаются, поэтому чистить список
            // приходится вручную: предмет могли сжечь прямо в тачке.
            for (int i = _frozen.Count - 1; i >= 0; i--)
                if (_frozen[i] == null || !_frozen[i].IsSpawned) _frozen.RemoveAt(i);

            TickSettle(Time.deltaTime);

            // Таймеры уничтоженных предметов. Ключ-объект в словаре остаётся
            // даже после Destroy — и за долгую сессию их накопится сколько
            // угодно. Чистим только когда счёт разошёлся, а не каждый кадр.
            if (_settleTimer.Count > _inside.Count)
            {
                _stale.Clear();

                foreach (KeyValuePair<PickupItem, float> kv in _settleTimer)
                    if (kv.Key == null || !_inside.Contains(kv.Key)) _stale.Add(kv.Key);

                foreach (PickupItem dead in _stale) _settleTimer.Remove(dead);
            }
        }

        // Ячейки с исчезнувшими предметами. Как и с _inside: объект могли
        // сжечь или деспавнить прямо в тачке, и уход через триггер не придёт.
        for (int i = 0; i < _slotted.Count; i++)
            if (_slotted[i] != null && !_slotted[i].IsSpawned) _slotted[i] = null;

        // Кинематический груз усыплять нечего: он и так не считается физикой.
        if (_mode != CargoMode.Physics) return;

        if (!_sleepWhenIdle || _cart == null) return;

        // Никто не катит — груз спит. Десять тел, которые никуда не едут, не
        // должны стоить ничего.
        if (_cart.IsGripped) return;

        foreach (PickupItem it in _inside)
        {
            if (it == null) continue;
            Rigidbody rb = it.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic && rb.linearVelocity.sqrMagnitude < 0.01f)
                rb.Sleep();
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!_debugGizmo) return;

        if (_volume != null)
        {
            Gizmos.color = new Color(0.5f, 1f, 0.6f, 0.35f);
            Bounds b = _volume.bounds;
            Gizmos.DrawWireCube(b.center, b.size);
        }

        if (_slotRoot == null) return;

        // Порядок заполнения виден линией: она проходит через точки в том же
        // порядке, в каком они лежат в иерархии. Если линия скачет сверху вниз
        // и обратно, кладка будет расти рвано — это и надо увидеть заранее.
        Vector3 prev = Vector3.zero;
        bool has = false;

        foreach (Transform t in _slotRoot)
        {
            if (t == null) continue;

            bool on = t.gameObject.activeSelf;

            Gizmos.color = on ? new Color(1f, 0.85f, 0.3f, 0.9f)
                              : new Color(0.5f, 0.5f, 0.5f, 0.4f);

            Gizmos.DrawWireSphere(t.position, 0.06f);
            Gizmos.DrawRay(t.position, t.up * 0.12f);

            if (!on) continue;

            if (has) Gizmos.DrawLine(prev, t.position);
            prev = t.position;
            has = true;
        }
    }
}
