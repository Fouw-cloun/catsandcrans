using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// КОНЕЦ ГАЗОВОГО ШЛАНГА. Предмет, который тянут от баллона и цепляют к порту.
///
///   F   — взять / положить (база)
///   ЛКМ — прицепить к ближайшей точке подключения
///
/// Держишь конец в руке, подходишь к плите, жмёшь ЛКМ — прицепился. Пока порт
/// рядом, на экране висит подсказка, к чему именно прицепится.
///
/// Порты искать не нужно ни лучом, ни по слоям: они регистрируются сами, а
/// конец цепляется к БЛИЖАЙШЕМУ свободному, до которого дотянулся. Ни масок,
/// ни ссылок заполнять не приходится.
///
/// Пока прицеплен, конец сидит в порту и физики у него нет. Отцепить — взять
/// в руку: за шланг просто тянут, и он выходит.
///
/// СЕТЬ: прицепленность — ссылка в SyncVar. Позиция прицепленного конца
/// по сети не гоняется вовсе, его ведёт сам порт.
/// </summary>
public class GasHoseEnd : InteractableItem
{
    [Header("Положение в руке")]
    [SerializeField] private Vector3 _holdPositionOffset = new Vector3(0.2f, -0.15f, 0.5f);
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    [Header("Баллон")]
    [Tooltip("Чей это шланг. Через него конец знает, идёт ли газ.")]
    [SerializeField] private GasCylinder _cylinder;

    [Header("Подключение")]
    [Tooltip("К каким портам можно подключаться. ПУСТО = к любым на сцене — " +
             "порты регистрируются сами, и настраивать ничего не надо.\n\n" +
             "Заполняй, только если нужно ограничить: например, этот баллон " +
             "цепляется исключительно к своей плите.")]
    [SerializeField] private GasPort[] _allowedPorts;

    [Header("Отладка")]
    [SerializeField] private bool _debugHud = true;

    /// <summary>Порт, в котором сидим. Null — свободен.</summary>
    private readonly SyncVar<NetworkObject> _portOwner = new SyncVar<NetworkObject>();

    private GasPort _port;
    private PlayerHand _holdingHand;
    private Transform _holderCam;
    private bool _wasKinematic;
    private bool _parked;

    private GasPort _hover;      // порт под прицелом у владельца
    private string _status = "";

    public bool IsConnected => _portOwner.Value != null;

    /// <summary>Идёт ли газ через этот конец. Спрашивает порт, а через него —
    /// техника.</summary>
    public bool IsSupplying => IsConnected && _cylinder != null &&
                               _cylinder.IsOpen && _cylinder.HasFuel;

    public GasCylinder Cylinder => _cylinder;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // НОВАЯ СЕССИЯ — ШЛАНГ ОТЦЕПЛЕН.
        //
        // Конец шланга и порты — СЦЕННЫЕ объекты: при остановке сети они не
        // уничтожаются, OnStopNetwork только отписывается, а значение SyncVar
        // остаётся с прошлого запуска. Ссылка при этом указывает на
        // NetworkObject, которого в новой сессии уже нет, — и получается
        // «подключено, но газа нет». Именно так плита и просила подключить
        // газ, который вроде бы включен с прошлого раза.
        //
        // Дешевле явно отцепить на старте, чем разбирать, жива ссылка или
        // нет: заново воткнуть шланг — одно движение.
        if (_portOwner.Value != null) _portOwner.Value = null;
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _portOwner.OnChange += OnPortChanged;
        Refresh(_portOwner.Value);
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _portOwner.OnChange -= OnPortChanged;
    }

    private void OnPortChanged(NetworkObject prev, NetworkObject next, bool asServer)
    {
        // У ХОСТА OnChange приходит дважды: asServer = true, потом false.
        if (asServer && base.IsClientInitialized) return;
        Refresh(next);
    }

    /// <summary>Локальная реакция на подключение: физику глушим у ВСЕХ, иначе
    /// у одних конец сидит в порту, а у других продолжает падать.</summary>
    private void Refresh(NetworkObject owner)
    {
        // Проверка на «фальшивый null» Unity: объект уничтожен, а ссылка
        // не пустая. Без неё GetComponentInChildren кинет исключение прямо
        // в OnStartNetwork, и шланг останется в подвешенном состоянии.
        if (owner == null || owner.gameObject == null) owner = null;

        GasPort newPort = owner != null ? owner.GetComponentInChildren<GasPort>() : null;

        if (_port != null && _port != newPort) _port.SetConnected(null);
        _port = newPort;
        if (_port != null) _port.SetConnected(this);

        if (_port != null && !_parked)
        {
            _parked = true;
            if (_rb != null) { _wasKinematic = _rb.isKinematic; _rb.isKinematic = true; }
        }
        else if (_port == null && _parked)
        {
            _parked = false;
            if (_rb != null) { _rb.isKinematic = _wasKinematic; _rb.WakeUp(); }
        }
    }

    // ───────────────────────── ПРОТЯЖКА ─────────────────────────

    private void Update()
    {
        if (base.IsOwner) OwnerCheck();
        else if (_debugHud) _status = "";   // не наш — подсказку не рисуем

        FollowPort();
    }

    /// <summary>
    /// Подсказка и подключение по ЛКМ.
    ///
    /// Тянуть конец никуда не надо: он и так в руке, а порт ищется по
    /// расстоянию от самого конца. Подошёл — нажал — прицепился.
    /// </summary>
    private void OwnerCheck()
    {
        if (IsConnected) { _status = ""; _hover = null; return; }

        _hover = FindPort();

        if (_hover != null)
        {
            _status = $"ЛКМ — подключить к «{_hover.Label}»";
        }
        else
        {
            // Показываем, СКОЛЬКО не хватает: «не цепляется» иначе может
            // значить и «порта нет», и «поднеси ещё чуть-чуть».
            GasPort near = GasPort.Nearest(transform.position, out float dist);
            _status = near == null
                ? "портов на сцене нет"
                : $"до «{near.Label}» {dist:0.00} м, надо ближе {near.SnapRange:0.00} м";
        }

        if (_hover == null || !Input.GetMouseButtonDown(0)) return;

        NetworkObject portObject = _hover.GetComponentInParent<NetworkObject>();
        if (portObject == null)
        {
            Debug.LogWarning($"[Газ] у порта «{_hover.Label}» нет NetworkObject выше " +
                             "по иерархии — подключить не к чему", this);
            return;
        }

        CmdConnect(portObject);
    }

    /// <summary>
    /// Ближайший порт к концу шланга. Луча нет и масок нет: порты
    /// регистрируются сами, а искать надо по расстоянию, а не по прицелу —
    /// шланг цепляют поднося, а не наводя.
    /// </summary>
    private GasPort FindPort() => GasPort.FindNearest(transform.position, _allowedPorts);

    /// <summary>Прицепленный конец сидит в порту. Ведём его локально у всех:
    /// по сети идёт только ссылка на порт.</summary>
    private void FollowPort()
    {
        if (_port == null) return;
        transform.SetPositionAndRotation(_port.Socket.position, _port.Socket.rotation);
    }

    // ───────────────────────── СЕРВЕР ─────────────────────────

    [ServerRpc(RequireOwnership = true)]
    private void CmdConnect(NetworkObject portObject)
    {
        if (portObject == null) return;
        ConnectTo(portObject, portObject.GetComponentInChildren<GasPort>());
    }

    /// <summary>
    /// ЛКМ через штатный путь предмета. Дублирует прямое чтение мыши
    /// намеренно: если ЛКМ где-то перехватывается, сработает хотя бы один
    /// из двух путей, а двойного подключения быть не может — порт занимается
    /// с первого раза.
    /// </summary>
    public override void ServerPrimaryUse(UseContext ctx)
    {
        if (IsConnected) return;

        GasPort p = GasPort.FindNearest(transform.position, _allowedPorts);
        if (p == null)
        {
            GasPort near = GasPort.Nearest(transform.position, out float d);
            Debug.Log(near == null
                ? "[Газ] портов на сцене нет"
                : $"[Газ] до «{near.Label}» {d:0.00} м, надо ближе {near.SnapRange:0.00} м",
                this);
            return;
        }

        NetworkObject portObject = p.GetComponentInParent<NetworkObject>();
        if (portObject == null)
        {
            Debug.LogWarning($"[Газ] у порта «{p.Label}» нет NetworkObject выше " +
                             "по иерархии — подключить не к чему", this);
            return;
        }

        ConnectTo(portObject, p);
    }

    /// <summary>Общая часть подключения: и для команды клиента, и для ЛКМ.</summary>
    [Server]
    private void ConnectTo(NetworkObject portObject, GasPort p)
    {
        if (p == null || !p.IsFree) return;

        // Проверка на вменяемость: цеплять порт на другом конце карты нельзя.
        // Запас втрое — у сервера позиция конца приезжает с задержкой.
        if (Vector3.Distance(p.Socket.position, transform.position) > p.SnapRange * 3f) return;

        // ИЗ РУКИ ОТПУСКАЕМ. Конец уходит в гнездо, и держать его одновременно
        // нельзя: иначе он остаётся в руке, а в порт садится только после
        // того, как его бросят вручную.
        if (_holdingHand != null && _holdingHand.HeldItem == this)
        {
            PlayerHand hand = _holdingHand;
            _holdingHand = null;
            hand.ServerDrop(Vector3.zero, 0f);
        }

        _portOwner.Value = portObject;
        Debug.Log($"[Газ] шланг подключён к «{p.Label}»", this);
    }

    /// <summary>Взяли в руку — вынули из порта: за шланг просто тянут.</summary>
    public override void ServerOnPickup(PlayerHand hand)
    {
        _holdingHand = hand;
        if (_portOwner.Value != null) _portOwner.Value = null;
        base.ServerOnPickup(hand);
    }

    public override void ServerOnDrop(PlayerHand hand, Vector3 dir, float force = 0f)
    {
        _holdingHand = null;
        _holderCam = null;
        base.ServerOnDrop(hand, dir, force);
    }

    public override void UpdateHeldPose(Transform handAnchor)
    {
        if (_holderCam == null)
        {
            FirstPersonController fpc = handAnchor.GetComponentInParent<FirstPersonController>();
            if (fpc != null)
            {
                Camera c = fpc.GetComponentInChildren<Camera>(true);
                if (c != null) _holderCam = c.transform;
            }
        }

        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);
        transform.SetPositionAndRotation(pos, rot);
    }

    private void OnGUI()
    {
        // Показываем, пока конец в руках у нас. Пустая строка тоже пишется:
        // так видно, что скрипт живой, а не молча спит.
        if (!_debugHud || !base.IsOwner) return;

        string txt = string.IsNullOrEmpty(_status)
            ? (IsConnected ? "подключён" : "поднеси к точке подключения")
            : _status;

        GUIStyle st = new GUIStyle(GUI.skin.box) { fontSize = 14 };
        st.normal.textColor = _hover != null ? Color.green
                            : IsConnected ? new Color(0.6f, 1f, 0.7f)
                            : new Color(1f, 0.85f, 0.3f);

        GUI.Box(new Rect(12f, 372f, 660f, 26f), $"Шланг → {txt}", st);
    }
}
