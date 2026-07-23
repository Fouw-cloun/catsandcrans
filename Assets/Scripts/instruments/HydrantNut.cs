using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// ГАЙКА гидранта — универсальная деталь. Повесь ОДИН такой скрипт на каждую из
/// 4 частей (верхний вентиль + 3 боковых выхода). Работают одинаково.
///
///   F (с ключом в руке, целясь в гайку)  — насадить ключ.
///   F (целясь в НАСАЖЕННЫЙ ключ)         — забрать ключ обратно в руку.
///   ЛКМ зажата (целясь в ключ) + мышь ПО КРУГУ — ключ ТЯНЕТСЯ ЗА МЫШЬЮ вокруг
///       оси гайки: куда ведёшь курсор, туда гайка и крутится. Это не «мышь
///       вправо = +градус», а настоящее слежение за углом курсора — ощущается,
///       будто сам водишь ключ рукой.
///
/// СЕТЬ (паттерн проекта):
///   - _angle — SyncVar (абсолютный накопленный угол, многооборотный). Крутящий
///     считает угол ЛОКАЛЬНО (без задержки) и шлёт абсолютное значение —
///     идемпотентно; у остальных угол сглаживается MoveTowards.
///   - _wrench / _operator — SyncVar: какой ключ насажен и кто крутит.
///
/// ПРИНЦИП (переделано — стало проще):
///   Насаженный ключ становится НАСТОЯЩИМ ДОЧЕРНИМ объектом NutPivot и встаёт в
///   позу эталонной точки WrenchSeat. Дальше крутится ПИВОТ, а ключ едет вместе
///   с ним как часть иерархии — ничего пересчитывать каждый кадр не нужно.
///   Позу ключа настраиваешь МЫШКОЙ В РЕДАКТОРЕ, двигая WrenchSeat.
///
/// СТРУКТУРА (на каждой гайке):
///   Nut (этот скрипт)
///     ├─ NutPivot        (пустышка — КРУТИТСЯ)        → поле Nut Pivot
///     │   ├─ [меш гайки/крышки]
///     │   └─ WrenchSeat  (эталонная поза ключа)       → поле Wrench Seat
///     └─ GrabTarget      (Collider + HydrantNutPart — цель для F)
///
/// NetworkObject у гайки СВОЙ НЕ НУЖЕН, если она — часть гидранта: скрипт
/// возьмёт NetworkObject гидранта (несколько NetworkBehaviour на одном NOB — норма).
/// </summary>
public class HydrantNut : NetworkBehaviour
{
    public enum NutKind
    {
        TopValve,    // верхний кран: НЕ срывается, регулирует напор (Open01)
        SideOutlet   // боковой выход: откручивается до конца → крышка и ключ ОТПАДАЮТ
    }

    [Header("Тип гайки")]
    [Tooltip("TopValve — верхний кран (не срывается, регулирует напор).\n" +
             "SideOutlet — боковой (открутил до конца → крышка и ключ отпадают).")]
    [SerializeField] private NutKind _kind = NutKind.TopValve;

    [Header("Гайка")]
    [Tooltip("Пивот, который КРУТИТСЯ. Меш гайки и WrenchSeat — его дети. " +
             "Насаженный ключ становится ребёнком этого пивота.")]
    [SerializeField] private Transform _nutPivot;
    [Tooltip("Ось вращения гайки в локальных координатах пивота (подгони под модель)")]
    [SerializeField] private Vector3 _nutAxis = Vector3.up;
    [Tooltip("ЭТАЛОННАЯ ПОЗА КЛЮЧА: пустышка-ребёнок пивота. Поставь её мышкой в " +
             "редакторе туда, где ключ должен сидеть на гайке — ключ встанет ровно " +
             "сюда и станет ребёнком пивота (крутится вместе с гайкой).")]
    [SerializeField] private Transform _wrenchSeat;

    [Header("Точка крепления КОЛЬЦА шланга (боковой выход)")]
    [Tooltip("ОТДЕЛЬНАЯ пустышка, куда садится кольцо шланга. Ставь её в устье " +
             "бокового выхода (там, где была крышка), forward = наружу из гидранта. " +
             "Должна быть СТАТИЧНОЙ (НЕ ребёнком NutPivot), иначе поедет с гайкой. " +
             "Если пусто — возьмётся Wrench Seat (запасной вариант).")]
    [SerializeField] private Transform _ringSeat;

    [Header("Связь (для бокового выхода)")]
    [Tooltip("ВЕРХНЯЯ гайка гидранта (нужна боковому: её Open01 = напор в шланге). " +
             "Заполняется только у боковых выходов.")]
    [SerializeField] private HydrantNut _topValve;

    [Header("Стартовое состояние")]
    [Tooltip("Верхний вентиль: ВКЛ = базово ОТКРЫТ на максимум (Open01=1). " +
             "Тогда боковой выход сразу льёт воду, а верхним ты потом ГЛУШИШЬ. " +
             "Для боковых крышек оставь ВЫКЛ.")]
    [SerializeField] private bool _startFullyOpen = false;

    [Header("Кручение (ключ тянется за мышью)")]
    [Tooltip("Сколько ОБОРОТОВ от закрыто (0) до полностью открыто (1)")]
    [SerializeField] private float _turnsToOpen = 3f;
    [Tooltip("Скорость, с которой ключ догоняет курсор, град/сек. Больше = резче.")]
    [SerializeField] private float _followSpeed = 720f;
    [Tooltip("Инвертировать направление кручения")]
    [SerializeField] private bool _invert = false;
    [Tooltip("Скорость сглаживания у наблюдателей, град/сек")]
    [SerializeField] private float _smoothSpeed = 720f;
    [Tooltip("Если игрок отошёл дальше — кручение прерывается")]
    [SerializeField] private float _operateRange = 5f;
    [Tooltip("Насколько сильно движение мыши двигает виртуальную точку (радиус круга)")]
    [SerializeField] private float _mouseScale = 12f;
    [Tooltip("Множитель угла: 1 = оборот мыши даёт оборот гайки, 2 = вдвое быстрее")]
    [SerializeField] private float _turnGain = 1.5f;

    [Header("Отпадение (только для SideOutlet)")]
    [Tooltip("Меш крышки на гайке — прячется, когда крышка отпала")]
    [SerializeField] private GameObject _capVisual;
    [Tooltip("Префаб крышки-ПРЕДМЕТА (подбирается). В Spawnable Prefabs у NetworkManager!")]
    [SerializeField] private GameObject _capPrefab;
    [Tooltip("Где спавнится отпавшая крышка (пусто = позиция пивота)")]
    [SerializeField] private Transform _capSpawnPoint;
    [Tooltip("Сила, с которой отлетает крышка")]
    [SerializeField] private float _capPopForce = 3f;
    [Tooltip("Сила, с которой отлетает ключ")]
    [SerializeField] private float _wrenchPopForce = 4f;
    [Tooltip("Направление вылета (локальное для гайки) — куда бьёт напор")]
    [SerializeField] private Vector3 _popDirection = Vector3.forward;

    // ───────── СЕТЕВОЕ СОСТОЯНИЕ ─────────
    private readonly SyncVar<float> _angle = new(0f);              // накопленный угол, град
    private readonly SyncVar<NetworkBehaviour> _wrench = new(null); // насаженный ключ
    private readonly SyncVar<NetworkObject> _operator = new(null); // кто крутит (зажал ЛКМ)
    private readonly SyncVar<bool> _capOff = new(false);           // крышка отпала (только боковые)
    private readonly SyncVar<bool> _ringAttached = new(false);     // подключено кольцо шланга

    // ───────── ЛОКАЛЬНОЕ ─────────
    private Quaternion _baseRot;      // базовый поворот пивота (готча #3 — не стираем оффсет)
    private float _localAngle;        // угол у крутящего (без сетевой задержки)
    private float _displayAngle;      // сглаженный угол у остальных
    private bool _hasLastMouseAngle;  // есть ли предыдущий угол курсора
    private float _lastMouseAngle;    // предыдущий угол курсора вокруг оси
    private Vector2 _virtualCursor;   // виртуальная точка (курсор залочен!)
    private Camera _cam;
    private const float MaxDt = 0.05f;

    /// <summary>Пивот гайки — ключ становится ЕГО ребёнком (читает WrenchItem).</summary>
    public Transform NutPivot => _nutPivot;

    /// <summary>Эталонная поза ключа на гайке (читает WrenchItem).</summary>
    public Transform WrenchSeat => _wrenchSeat;

    /// <summary>Тип гайки (верхний кран / боковой выход).</summary>
    public NutKind Kind => _kind;

    /// <summary>Верхняя гайка гидранта (для бокового выхода) — напор в шланге.</summary>
    public HydrantNut TopValve => _topValve;

    /// <summary>Точка посадки КОЛЬЦА шланга. Если своя не задана — Wrench Seat.</summary>
    public Transform RingSeat => _ringSeat != null ? _ringSeat : _wrenchSeat;

    /// <summary>ВОДА ИДЁТ из ДЫРКИ (крышка отпала И кольцо шланга НЕ подключено).
    /// Если кольцо подключено — вода идёт в шланг, а из дырки НЕ льёт.</summary>
    public bool IsOutletOpen => _kind == NutKind.SideOutlet && _capOff.Value && !_ringAttached.Value;

    /// <summary>Можно ли подключить кольцо: выход открыт (крышки нет), кольца ещё нет.</summary>
    public bool CanAttachRing => _kind == NutKind.SideOutlet && _capOff.Value && !_ringAttached.Value;

    /// <summary>Подключено ли кольцо шланга к этому выходу.</summary>
    public bool RingAttached => _ringAttached.Value;

    /// <summary>ОТКРЫТОСТЬ 0..1 — готовая ручка под поток воды/VFX.
    /// Для верхнего крана = напор (0 закрыт, 1 полный).</summary>
    public float Open01
    {
        get
        {
            float full = Mathf.Max(1f, _turnsToOpen * 360f);
            return Mathf.Clamp01(Mathf.Abs(_angle.Value) / full);
        }
    }

    private void Awake()
    {
        if (_nutPivot != null) _baseRot = _nutPivot.localRotation;
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();

        // Верхний вентиль базово открыт на максимум — тогда боковой сразу льёт,
        // а верхним потом ГЛУШИШЬ поток. Ставит только сервер.
        if (base.IsServerInitialized && _startFullyOpen && _kind == NutKind.TopValve)
            _angle.Value = Mathf.Max(1f, _turnsToOpen * 360f);

        _localAngle = _angle.Value;
        _displayAngle = _angle.Value;
        _capOff.OnChange += OnCapOffChanged;
        ApplyCapVisual(_capOff.Value); // поздний вход: крышка могла уже отпасть
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _capOff.OnChange -= OnCapOffChanged;
    }

    private void OnCapOffChanged(bool prev, bool next, bool asServer)
        => ApplyCapVisual(next);

    /// <summary>Крышка на гайке видна, пока не отпала.</summary>
    private void ApplyCapVisual(bool capOff)
    {
        if (_capVisual != null) _capVisual.SetActive(!capOff);
    }

    private HoseRingItem _attachedRing; // серверная ссылка на кольцо

    /// <summary>Подключить кольцо шланга к этому выходу (только если открыт и НЕ льёт).</summary>
    public bool ServerAttachRing(HoseRingItem ring)
    {
        if (!CanAttachRing) return false;
        _ringAttached.Value = true;
        _attachedRing = ring;
        return true;
    }

    /// <summary>Отключить кольцо (F по кольцу). Если сверху ВОДА ВКЛЮЧЕНА —
    /// снятие под напором ВЫШИБАЕТ игрока струёй (как срыв крышки).</summary>
    public void ServerDetachRing(PlayerHand taker)
    {
        bool underWater = _topValve != null && _topValve.Open01 > 0.05f;

        // Направление выхода наружу (по WrenchSeat/пивоту) — куда бьёт напор.
        Transform seat = RingSeat;
        Vector3 outDir = seat != null ? seat.forward
                       : (_nutPivot != null ? _nutPivot.forward : transform.forward);

        _ringAttached.Value = false;

        if (_attachedRing != null)
            _attachedRing.ServerDetach(taker, underWater, outDir);
        _attachedRing = null;
    }

    /// <summary>Крышку ВЕРНУЛИ на место (вызывает HydrantCapItem). Выход снова
    /// закрыт, гайку можно закручивать заново — НЕ одноразово.</summary>
    public void ServerCapReturned()
    {
        _ringAttached.Value = false; // страховка: без крышки кольца тоже быть не может
        _attachedRing = null;
        _capOff.Value = false;
        _angle.Value = 0f;
        _localAngle = 0f;
        _displayAngle = 0f;
    }

    // ───────── F НА ГАЙКЕ: НАСАДИТЬ КЛЮЧ (сервер) ─────────

    /// <summary>Вызывается через IVehiclePart-часть (HydrantNutPart) или напрямую.</summary>
    public void ServerMountWrench(PlayerInteractor interactor)
    {
        // ДИАГНОСТИКА: дошёл ли F до гайки и ЧТО именно в руке.
        {
            PlayerHand h = interactor != null ? interactor.Hand : null;
            InteractableItem held = h != null ? h.HeldItem : null;
            Debug.Log($"[Nut] F дошёл до '{name}'. Kind={_kind}, " +
                      $"вРуке={(held != null ? held.GetType().Name : "ПУСТО")}, " +
                      $"этоКольцо={(held is HoseRingItem)}, этоКлюч={(held is WrenchItem)}, " +
                      $"крышкаСорвана={_capOff.Value}, кольцоЕсть={_ringAttached.Value}", this);
        }

        if (interactor == null) return;

        // Если на гайке «висит» призрак (ключ уже снят) — чистим и работаем.
        if (_wrench.Value != null)
        {
            WrenchItem existing = _wrench.Value as WrenchItem;
            if (existing == null || !existing.IsMounted)
            {
                _wrench.Value = null;
                _operator.Value = null;
            }
            else
            {
                return; // на гайке реально сидит ключ
            }
        }

        PlayerHand hand = interactor.Hand;
        if (hand == null || !hand.HasItem) return;

        // ВАЖЕН ПОРЯДОК: кольцо проверяем ДО отсечки "крышки нет — крутить нечего".
        // Кольцо вставляют именно когда крышка СОРВАНА, поэтому та отсечка не
        // должна его блокировать (раньше из-за неё F с кольцом молча выходил).
        HoseRingItem ring = hand.HeldItem as HoseRingItem;
        if (ring != null)
        {
            // САМОЛЕЧЕНИЕ: если флаг "кольцо надето" висит, а кольца реально нет
            // (упало/деспавнилось/прошлая попытка сорвалась) — чистим флаг, иначе
            // выход навсегда останется "занятым" и кольцо больше не вставить.
            if (_ringAttached.Value && (_attachedRing == null || !_attachedRing.IsAttached))
            {
                _ringAttached.Value = false;
                _attachedRing = null;
            }

            if (!CanAttachRing)
            {
                Debug.LogWarning($"[Ring] Не вставить в '{name}': Kind={_kind} " +
                                 $"(нужен SideOutlet), крышкаСорвана={_capOff.Value} " +
                                 $"(нужно True), кольцоУжеЕсть={_ringAttached.Value} " +
                                 $"(нужно False).", this);
                return;
            }

            if (!ServerAttachRing(ring)) return;
            hand.ServerDrop(Vector3.zero, 0f);
            ring.ServerAttachTo(this);
            return;
        }

        // Дальше — только КЛЮЧ. На боковом выходе без крышки крутить нечего.
        if (_kind == NutKind.SideOutlet && _capOff.Value) return;

        WrenchItem wrench = hand.HeldItem as WrenchItem;
        if (wrench == null) return; // в руке не ключ и не кольцо

        // Забираем ключ из руки БЕЗ броска и сажаем на гайку (он станет ребёнком пивота).
        hand.ServerDrop(Vector3.zero, 0f);
        wrench.ServerMountTo(this);   // ВАЖНО: сам HydrantNut, а не NOB гидранта!
        _wrench.Value = wrench;
        _operator.Value = null; // крутить начнут отдельно (зажав ЛКМ)
    }

    /// <summary>Снять ключ и вернуть в руку тому, кто его ЗАБИРАЕТ (F по ключу).
    /// taker — рука того игрока, который жмёт F. Раньше сюда шёл _operator (кто
    /// крутил), а он null, если просто забирают без кручения — ключ падал, а
    /// состояние гайки чистилось криво, и повторно насадить не получалось.</summary>
    public void ServerReleaseWrench(PlayerHand taker = null)
    {
        // Чистим состояние ВСЕГДА, даже если ключа уже нет — чтобы гайка не
        // осталась «занятой» призраком и на неё можно было насадить снова.
        WrenchItem wrench = _wrench.Value as WrenchItem;
        _wrench.Value = null;
        _operator.Value = null;

        if (wrench != null) wrench.ServerUnmount(taker);
    }

    // ───────── КАЖДЫЙ КАДР ─────────

    private void Update()
    {
        HandleLocalInput();
        UpdateVisual();
    }

    /// <summary>Ввод у ЛОКАЛЬНОГО игрока: целюсь в ключ/гайку и зажимаю ЛКМ —
    /// начинаю крутить; пока держу — вожу мышью ПО КРУГУ, ключ идёт за мышью.
    ///
    /// ВАЖНО: курсор в игре ЗАЛОЧЕН (FirstPersonController), поэтому мы НЕ можем
    /// использовать Input.mousePosition — он не двигается. Считаем по ДЕЛЬТЕ мыши
    /// (Mouse X/Y): ведём виртуальную точку и берём её угол — круговое движение
    /// мышью даёт круговое движение ключа.</summary>
    private void HandleLocalInput()
    {
        if (_wrench.Value == null) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        bool iAmOperator = _operator.Value != null && _operator.Value.IsOwner;

        // Начало кручения: зажал ЛКМ, целясь в ключ/гайку.
        if (!iAmOperator && Input.GetMouseButtonDown(0) && IsAimingAtWrench())
        {
            _localAngle = _angle.Value;
            _hasLastMouseAngle = false;
            _virtualCursor = Vector2.right * 100f; // стартовая точка «виртуальной руки»
            CmdBeginTurn();
            return;
        }

        if (!iAmOperator) return;

        // Отпустил ЛКМ или отошёл — стоп.
        if (!Input.GetMouseButton(0) || DistanceToPlayer() > _operateRange)
        {
            CmdEndTurn();
            _hasLastMouseAngle = false;
            return;
        }

        DragWrenchWithMouse();
    }

    /// <summary>Дистанция от локального игрока до гайки (по пивоту, если он есть).</summary>
    private float DistanceToPlayer()
    {
        Vector3 nutPos = _nutPivot != null ? _nutPivot.position : transform.position;
        return Vector3.Distance(_cam.transform.position, nutPos);
    }

    /// <summary>Целюсь ли я в ключ/гайку. Луч — из центра экрана (курсор залочен).</summary>
    private bool IsAimingAtWrench()
    {
        Ray ray = new Ray(_cam.transform.position, _cam.transform.forward);
        // Берём ВСЕ попадания на пути — корпус гидранта не должен «загораживать» гайку.
        RaycastHit[] hits = Physics.RaycastAll(ray, _operateRange, ~0, QueryTriggerInteraction.Collide);
        foreach (RaycastHit h in hits)
        {
            if (h.collider.GetComponentInParent<WrenchItem>() != null) return true;
            if (h.collider.GetComponentInParent<HydrantNut>() == this) return true;
            if (h.collider.GetComponentInParent<HydrantNutPart>() is HydrantNutPart p && p.Nut == this) return true;
        }
        return false;
    }

    /// <summary>КЛЮЧ ТЯНЕТСЯ ЗА МЫШЬЮ. Курсор залочен, поэтому ведём ВИРТУАЛЬНУЮ
    /// точку по дельте мыши и берём её угол: водишь мышью круги — точка описывает
    /// круги — гайка крутится следом, накапливая обороты.</summary>
    private void DragWrenchWithMouse()
    {
        float mx = Input.GetAxisRaw("Mouse X");
        float my = Input.GetAxisRaw("Mouse Y");

        // Двигаем виртуальную точку вокруг центра.
        _virtualCursor += new Vector2(mx, my) * _mouseScale;

        // Не даём точке схлопнуться в центр (иначе угол скачет).
        float r = _virtualCursor.magnitude;
        if (r < 20f)
        {
            if (r < 0.001f) { _virtualCursor = Vector2.right * 20f; return; }
            _virtualCursor = _virtualCursor / r * 20f;
        }
        else if (r > 300f)
        {
            _virtualCursor = _virtualCursor / r * 300f; // и не убегать далеко
        }

        float mouseAngle = Mathf.Atan2(_virtualCursor.y, _virtualCursor.x) * Mathf.Rad2Deg;

        if (!_hasLastMouseAngle)
        {
            _lastMouseAngle = mouseAngle;
            _hasLastMouseAngle = true;
            return;
        }

        // Дельта угла за кадр (DeltaAngle корректно проходит через ±180°).
        float delta = Mathf.DeltaAngle(_lastMouseAngle, mouseAngle);
        _lastMouseAngle = mouseAngle;

        if (_invert) delta = -delta;

        // Ограничение скорости — ключ «догоняет» движение, а не телепортируется.
        float dt = Mathf.Min(Time.deltaTime, MaxDt);
        delta = Mathf.Clamp(delta * _turnGain, -_followSpeed * dt, _followSpeed * dt);

        float full = Mathf.Max(1f, _turnsToOpen * 360f);
        float newAngle = Mathf.Clamp(_localAngle + delta, 0f, full);
        if (Mathf.Abs(newAngle - _localAngle) < 0.001f) return;

        _localAngle = newAngle;
        CmdSetAngle(_localAngle); // идемпотентная отправка абсолютного угла
    }

    /// <summary>Показ: крутящий — сразу свой угол, остальные сглаживают к
    /// синхронизированному. Крутим ОТНОСИТЕЛЬНО базового поворота (готча #3).</summary>
    private void UpdateVisual()
    {
        if (_nutPivot == null) return;

        bool iAmOperator = _operator.Value != null && _operator.Value.IsOwner;
        float target;
        if (iAmOperator)
        {
            target = _localAngle;
        }
        else
        {
            _displayAngle = Mathf.MoveTowards(_displayAngle, _angle.Value,
                                              _smoothSpeed * Mathf.Min(Time.deltaTime, MaxDt));
            target = _displayAngle;
        }

        _nutPivot.localRotation = _baseRot * Quaternion.AngleAxis(target, _nutAxis.normalized);
        _displayAngle = target;
    }

    // ───────── СЕРВЕРНЫЕ RPC ─────────

    [ServerRpc(RequireOwnership = false)]
    private void CmdBeginTurn(NetworkConnection conn = null)
    {
        if (_wrench.Value == null) return;
        if (_operator.Value != null) return; // уже крутят

        // Находим NetworkObject игрока по соединению.
        foreach (NetworkObject nob in conn.Objects)
        {
            if (nob.GetComponent<PlayerInteractor>() != null)
            {
                _operator.Value = nob;
                return;
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void CmdSetAngle(float value, NetworkConnection conn = null)
    {
        if (_operator.Value == null || _operator.Value.Owner != conn) return;

        float full = Mathf.Max(1f, _turnsToOpen * 360f);
        _angle.Value = Mathf.Clamp(value, 0f, full);

        // БОКОВОЙ ВЫХОД: открутили до конца → крышка и ключ ОТПАДАЮТ.
        // ВЕРХНИЙ КРАН не срывается никогда — он только регулирует напор (Open01).
        if (_kind == NutKind.SideOutlet && !_capOff.Value && _angle.Value >= full - 0.01f)
            ServerPopCap();
    }

    /// <summary>Крышка и ключ ОТПАДАЮТ: спавним крышку-предмет (подбирается),
    /// выбрасываем ключ. Всё можно подобрать и поставить обратно — НЕ одноразово.</summary>
    private void ServerPopCap()
    {
        _capOff.Value = true;

        Vector3 popDir = transform.TransformDirection(_popDirection.normalized);
        Vector3 spawnPos = _capSpawnPoint != null ? _capSpawnPoint.position
                         : (_nutPivot != null ? _nutPivot.position : transform.position);

        // 1) Крышка отлетает предметом (её можно подобрать и вернуть).
        if (_capPrefab != null)
        {
            GameObject go = Instantiate(_capPrefab, spawnPos, Quaternion.identity);
            base.ServerManager.Spawn(go, null); // префаб — в Spawnable Prefabs!

            HydrantCapItem cap = go.GetComponent<HydrantCapItem>();
            if (cap != null) cap.ServerSetHomeNut(this); // знает, куда вернуться

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                Vector3 d = (popDir + Vector3.up * 0.5f + Random.insideUnitSphere * 0.25f).normalized;
                rb.linearVelocity = d * _capPopForce;
                rb.angularVelocity = Random.insideUnitSphere * 6f;
            }
        }

        // 2) Ключ вылетает из гайки (падает рядом, подбирается как обычно).
        ServerPopWrench(popDir);
    }

    /// <summary>Выбить ключ с гайки напором.</summary>
    private void ServerPopWrench(Vector3 popDir)
    {
        WrenchItem wrench = _wrench.Value as WrenchItem;
        _wrench.Value = null;
        _operator.Value = null;

        if (wrench == null) return;

        wrench.ServerUnmount(null); // не в руку — освобождаем (физика включится)

        Rigidbody rb = wrench.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            Vector3 d = (popDir + Vector3.up * 0.6f + Random.insideUnitSphere * 0.25f).normalized;
            rb.linearVelocity = d * _wrenchPopForce;
            rb.angularVelocity = Random.insideUnitSphere * 8f;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void CmdEndTurn(NetworkConnection conn = null)
    {
        if (_operator.Value != null && _operator.Value.Owner == conn)
            _operator.Value = null;
    }
}
