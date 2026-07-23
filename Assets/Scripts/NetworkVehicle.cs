using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// База любой техники, в которую садятся. Наследник InteractableItem, поэтому
/// вход — через нашу систему (F на технику с пустой рукой). В руку не берётся.
///
/// ФИЗИКА: машину симулирует тот, у кого AUTHORITY — водитель-владелец, либо
/// сервер, когда машина пустая (без владельца). У остальных Rigidbody
/// кинематичный и просто следует за client-authoritative NetworkTransform.
/// Поэтому движение идёт без RPC: authority считает физику локально, NT
/// рассылает позицию/поворот. Наследники реализуют VehicleFixedUpdate
/// (подвеска + тяга) — он вызывается только на authority.
///
/// Требует на объекте: NetworkObject, NetworkTransform (Client Authoritative),
/// Rigidbody. Подвижные части (рука/кузов) синхронятся наследниками по SyncVar.
/// </summary>
public abstract class NetworkVehicle : InteractableItem
{
    [Header("Техника — сиденье и камера")]
    [SerializeField] protected Transform _seatPoint;
    [SerializeField] protected Transform _exitPoint;
    [SerializeField] protected GameObject _vehicleCamera;
    [Tooltip("Коллайдер ИМЕННО этой кабины (нужен, если на одном объекте " +
             "несколько кабин — например автокран: шасси и кран). Игрок сядет " +
             "в ту кабину, на чей коллайдер смотрит. Если пусто — сработает " +
             "любой коллайдер машины (для техники с одной кабиной ок).")]
    [SerializeField] protected Collider _seatCollider;

    public Collider SeatCollider => _seatCollider;

    /// <summary>Индекс этой кабины среди всех NetworkVehicle на том же
    /// NetworkObject (стабилен, т.к. порядок компонентов фиксирован). Нужен,
    /// чтобы сервер знал, в какую именно кабину сажать (автокран: шасси/кран).</summary>
    public int SeatIndexOnObject
    {
        get
        {
            NetworkVehicle[] all = NetworkObject.GetComponentsInChildren<NetworkVehicle>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i] == this) return i;
            return -1;
        }
    }

    [Header("Физика корпуса")]
    [Tooltip("СТАТИЧНАЯ техника (башенный кран и т.п.): не ездит, у корпуса нет " +
             "Rigidbody. Включи — и вся физика корпуса/authority отключается, " +
             "остаётся только сиденье/вход/выход/камера.")]
    [SerializeField] protected bool _isStatic = false;
    [Tooltip("Rigidbody корпуса машины (не нужен для статичной техники)")]
    [SerializeField] protected Rigidbody _body;
    [Tooltip("Смещение центра масс (ниже = устойчивее, не опрокидывается)")]
    [SerializeField] protected Vector3 _centerOfMass = new Vector3(0f, -0.6f, 0f);

    // Текущий водитель (его NetworkObject), синхронизируется всем.
    protected readonly SyncVar<NetworkObject> _driver = new(null);

    protected bool _isLocalDriver;
    public bool HasDriver => _driver.Value != null;

    /// <summary>Есть ли у этой копии право симулировать физику: либо мы владелец
    /// (водитель), либо мы сервер и машина ничья (пустая стоит на земле).</summary>
    protected bool HasMoveAuthority =>
        base.IsOwner || (base.IsServerInitialized && (base.Owner == null || !base.Owner.IsValid));

    protected override void Awake()
    {
        base.Awake();
        _canPickup = false; // технику в руку не берут
        // Для статичной техники (кран) корпусной Rigidbody не нужен и не ищется.
        if (!_isStatic)
        {
            if (_body == null) _body = GetComponent<Rigidbody>();
            if (_body != null) _body.centerOfMass = _centerOfMass;
        }
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _driver.OnChange += OnDriverChanged;
        if (_vehicleCamera != null) _vehicleCamera.SetActive(false);
        base.TimeManager.OnTick += OnTick;
        RefreshBodyKinematic();
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _driver.OnChange -= OnDriverChanged;
        if (base.TimeManager != null) base.TimeManager.OnTick -= OnTick;
    }

    // Authority может смениться при посадке/выходе (GiveOwnership/RemoveOwnership).
    public override void OnOwnershipClient(NetworkConnection prevOwner)
    {
        base.OnOwnershipClient(prevOwner);
        RefreshBodyKinematic();
    }
    public override void OnOwnershipServer(NetworkConnection prevOwner)
    {
        base.OnOwnershipServer(prevOwner);
        RefreshBodyKinematic();
    }

    /// <summary>Rigidbody динамический только у authority; у остальных
    /// кинематический (его двигает NetworkTransform).</summary>
    private void RefreshBodyKinematic()
    {
        if (_body == null) return;
        bool authority = HasMoveAuthority;
        _body.isKinematic = !authority;
        _body.interpolation = authority ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        _body.useGravity = authority;
    }

    // ───────── ВХОД (F с пустой рукой) ─────────

    public override void ServerInteract(PlayerInteractor interactor)
    {
        if (_driver.Value != null) return;
        if (!CanEnter(interactor)) return;
        _driver.Value = interactor.NetworkObject;
        NetworkObject.GiveOwnership(interactor.Owner); // управление — водителю
    }

    protected virtual bool CanEnter(PlayerInteractor interactor) => true;

    [ServerRpc(RequireOwnership = true)]
    protected void CmdExit()
    {
        if (_driver.Value == null) return;
        _driver.Value = null;
        NetworkObject.RemoveOwnership(); // authority вернётся серверу
    }

    // ───────── НАСТРОЙКА ВОДИТЕЛЯ ─────────

    private void OnDriverChanged(NetworkObject prev, NetworkObject next, bool asServer)
    {
        if (next != null) ApplyDriverState(next, true);
        else if (prev != null) ApplyDriverState(prev, false);
    }

    protected virtual void ApplyDriverState(NetworkObject player, bool entering)
    {
        bool local = player.IsOwner;
        if (entering) _isLocalDriver = local && entering;

        // ── Физика: гасим столкновение тела водителя с машиной (для ЛЮБОГО
        //    водителя, не только локального — иначе на стороне authority
        //    коллайдер игрока выталкивает корпус → тряска и взрывы). ──
        // CharacterController игрока (он же коллайдер) — выключаем.
        if (player.TryGetComponent(out CharacterController cc))
            cc.enabled = !entering;

        // Любые прочие коллайдеры на игроке (Capsule/Box и т.п.) — тоже, чтобы
        // тело не толкалось с корпусом машины изнутри.
        foreach (Collider pcol in player.GetComponentsInChildren<Collider>(true))
            pcol.enabled = !entering;

        // ── Управление/камера/видимость — только у локального водителя. ──
        if (local)
        {
            // При ВЫХОДЕ ставим игрока в точку выхода и выравниваем взгляд ДО
            // включения контроллера. Пока игрок сидел, его transform каждый кадр
            // приравнивался к _seatPoint, который у крана крутится/наклоняется
            // вместе с башней — без сброса FirstPersonController включится с
            // "кривым" наклоном/креном и камеру начинает мотать/переворачивать.
            if (!entering && _exitPoint != null)
                player.transform.position = _exitPoint.position;

            FirstPersonController fpc = player.GetComponent<FirstPersonController>();
            PlayerInteractor pi = player.GetComponent<PlayerInteractor>();

            // Выравниваем взгляд (тело в вертикаль, наклон камеры в ноль) перед
            // включением контроллера.
            if (fpc != null && !entering) fpc.ResetLookToCurrent();

            if (fpc != null) fpc.enabled = !entering;
            if (pi != null) pi.enabled = !entering;

            Camera pcam = player.GetComponentInChildren<Camera>(true);
            if (pcam != null) pcam.gameObject.SetActive(!entering);
            if (_vehicleCamera != null) _vehicleCamera.SetActive(entering);

            foreach (Renderer r in player.GetComponentsInChildren<Renderer>(true))
                r.enabled = !entering;
        }

        if (!entering) _isLocalDriver = false;
    }

    // ───────── ЦИКЛЫ ─────────

    private void OnTick()
    {
        if (_isLocalDriver) DriverTick();
    }

    protected virtual void Update()
    {
        ApplyVisuals(); // визуал у всех

        if (!_isLocalDriver) return;

        if (_driver.Value != null && _seatPoint != null)
            _driver.Value.transform.SetPositionAndRotation(_seatPoint.position, _seatPoint.rotation);

        if (Input.GetKeyDown(KeyCode.F)) { CmdExit(); return; }

        DriverUpdate();
    }

    protected virtual void FixedUpdate()
    {
        // Физику (подвеска + тяга) считает только authority на динамичном теле.
        if (_body == null || _body.isKinematic || !HasMoveAuthority) return;
        VehicleFixedUpdate();
    }

    /// <summary>Каждый кадр у водителя: переключение режима, дискретный ввод.</summary>
    protected virtual void DriverUpdate() { }

    /// <summary>Каждый сетевой тик у водителя: отправка ввода частей (рука/кран).</summary>
    protected virtual void DriverTick() { }

    /// <summary>Каждый кадр у ВСЕХ: применить визуал (углы частей, колёса).</summary>
    protected virtual void ApplyVisuals() { }

    /// <summary>Каждый FixedUpdate у AUTHORITY: подвеска и тяга. Реализуют
    /// наследники (WheeledVehicle / TrackedVehicle).</summary>
    protected virtual void VehicleFixedUpdate() { }

    /// <summary>Активно ли машина управляется прямо сейчас (есть локальный
    /// водитель и езда разрешена). Наследники сужают через CanDrive.</summary>
    protected bool IsDriving => _isLocalDriver && CanDrive;

    /// <summary>Разрешена ли езда (кран блокирует при опорах, экскаватор — в
    /// режиме работы рукой). База — да.</summary>
    protected virtual bool CanDrive => true;
}
