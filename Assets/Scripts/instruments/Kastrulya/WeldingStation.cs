using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// СВАРОЧНЫЙ АППАРАТ — коробка на колёсах. Устроена так же, как база пылесоса:
/// кнопка включения на корпусе, кабель до держателя, подтягивание за ним.
///
/// МАССЫ НЕТ НАМЕРЕННО. В жизни без неё цепь не замкнётся, но в игре это
/// вторая точка, которую надо таскать и цеплять — а веселья ноль. Кабель один:
/// от коробки до держателя.
///
/// ЗАПАС ЭЛЕКТРОДОВ живёт здесь. Держатель приходит сюда перезаряжаться,
/// поэтому коробку приходится подтаскивать к работе, а не бросать у стены.
///
/// Требует: NetworkObject, NetworkTransform (Client Authoritative), Rigidbody
/// (Is Kinematic снято, Collision Detection = Continuous), Collider.
/// </summary>
public class WeldingStation : NetworkBehaviour, IVehiclePart
{
    [Header("Кабель до держателя")]
    [Tooltip("Точка крепления кабеля на ДЕРЖАТЕЛЕ. Без неё коробка не поедет.")]
    [SerializeField] private Transform _torchAttach;

    [Tooltip("Длина кабеля. Дальше этого коробка едет за держателем.")]
    [SerializeField] private float _cableLength = 4f;

    [Tooltip("Запас: тянуть, только если превысили длину на эту величину")]
    [SerializeField] private float _tensionThreshold = 0.2f;

    [Tooltip("Сила подтягивания на метр перетяга")]
    [SerializeField] private float _pullForce = 12f;

    [Tooltip("Ограничение силы, чтобы не улетала при рывке")]
    [SerializeField] private float _maxPull = 40f;

    [Tooltip("Максимальная горизонтальная скорость подтягивания")]
    [SerializeField] private float _maxSpeed = 5f;

    [Header("Устойчивость")]
    [Tooltip("Центр масс по Y. Ниже — устойчивее, меньше опрокидывается.")]
    [SerializeField] private float _centerOfMassY = -0.4f;

    [Header("Электроды")]
    [Tooltip("Сколько электродов в коробке")]
    [Min(0)][SerializeField] private int _electrodeStock = 20;

    [Tooltip("С какого расстояния держатель может перезарядиться, метры")]
    [SerializeField] private float _reloadRange = 2.5f;

    [Header("Включение")]
    [Tooltip("F по ЛЮБОМУ месту корпуса включает и выключает.\n\n" +
             "Нужно потому, что луч чаще всего попадает в основной коллайдер " +
             "коробки, а не в маленькую кнопку: IVehiclePart на кнопке при этом " +
             "не находится, и нажатие уходит в никуда.")]
    [SerializeField] private bool _wholeBodyIsButton = true;

    [Tooltip("Номер части для корпуса. Не должен совпадать с номером кнопки.")]
    [SerializeField] private int _bodyPartId = 90;

    [Header("Подъём на колёса")]
    [Tooltip("Вставать самой, если завалилась. Ручная клавиша остаётся " +
             "на случай, если застряла совсем криво.")]
    [SerializeField] private bool _autoRightUp = true;

    [Tooltip("С какого наклона считать, что упала, градусы")]
    [Range(20f, 90f)][SerializeField] private float _fallenAngle = 45f;

    [Tooltip("Сколько секунд надо пролежать, чтобы начать вставать. " +
             "Не мгновенно: иначе коробка дёргалась бы на каждой кочке.")]
    [SerializeField] private float _fallenDelay = 1.2f;

    [Tooltip("Как бодро выравнивается")]
    [SerializeField] private float _rightUpTorque = 14f;

    [SerializeField] private KeyCode _rightUpKey = KeyCode.Y;
    [SerializeField] private float _rightUpRange = 4f;
    [SerializeField] private float _rightUpLift = 0.4f;

    // ───────── сеть ─────────
    private readonly SyncVar<bool> _isRunning = new SyncVar<bool>(false);
    private readonly SyncVar<int> _stock = new SyncVar<int>(0);

    private Rigidbody _rb;
    private bool _warnedNoTorch;
    private float _fallenTime;

    public bool IsRunning => _isRunning.Value;
    public int Stock => _stock.Value;
    public float ReloadRange => _reloadRange;

    private void Awake() => _rb = GetComponent<Rigidbody>();

    // ───────────────────────── IVehiclePart ─────────────────────────

    public NetworkObject OwningNetworkObject => base.NetworkObject;
    public int PartId => _bodyPartId;

    /// <summary>F по корпусу. Отдельная кнопка тоже работает — у неё свой номер.</summary>
    public void ServerUse(PlayerInteractor interactor)
    {
        if (!_wholeBodyIsButton) return;
        ServerTogglePower();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        _stock.Value = _electrodeStock;

        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (_rb == null) return;

        _rb.isKinematic = false;                 // физику коробки считает сервер
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        _rb.centerOfMass = new Vector3(0f, _centerOfMassY, 0f);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // Кинематим только на ЧИСТОМ клиенте: на хосте коробка стала бы
        // кинематичной и перестала двигаться.
        if (!base.IsServerStarted && _rb != null) _rb.isKinematic = true;
    }

    // ───────────────────────── КНОПКА ─────────────────────────

    /// <summary>Зовётся кнопкой на корпусе.</summary>
    [Server]
    public void ServerTogglePower()
    {
        _isRunning.Value = !_isRunning.Value;
        Debug.Log($"[Сварка] аппарат {(_isRunning.Value ? "ВКЛЮЧЁН" : "выключен")}, " +
                  $"электродов {_stock.Value}", this);
    }

    /// <summary>Выдать электрод держателю. Возвращает false, если пусто.</summary>
    [Server]
    public bool ServerTakeElectrode()
    {
        if (_stock.Value <= 0) return false;
        _stock.Value--;
        return true;
    }

    // ───────────────────────── ПОДЪЁМ НА КОЛЁСА ─────────────────────────

    private void Update()
    {
        if (!Input.GetKeyDown(_rightUpKey)) return;

        Camera cam = Camera.main;
        if (cam == null) return;
        if (Vector3.Distance(cam.transform.position, transform.position) > _rightUpRange) return;

        CmdRightUp();
    }

    [ServerRpc(RequireOwnership = false)]
    private void CmdRightUp()
    {
        if (_rb == null) return;

        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;

        _rb.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
        _rb.position += Vector3.up * _rightUpLift;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
    }

    // ───────────────────────── ТЯГА ─────────────────────────

    private void FixedUpdate()
    {
        if (!base.IsServerInitialized || _rb == null) return;

        AutoRightUp();

        if (_torchAttach == null)
        {
            if (!_warnedNoTorch)
            {
                Debug.LogError($"[Сварка] на «{name}» не назначен Torch Attach — " +
                               "коробка не поедет за держателем", this);
                _warnedNoTorch = true;
            }
            return;
        }

        Vector3 to = _torchAttach.position - transform.position;
        float excess = to.magnitude - _cableLength;
        if (excess <= _tensionThreshold) return;

        // Тянем ГОРИЗОНТАЛЬНО: вертикаль оставляем гравитации, иначе коробка
        // подпрыгивала бы за держателем, поднятым над головой.
        Vector3 dir = to;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        Vector3 v = _rb.linearVelocity;
        v.y = 0f;
        if (v.magnitude >= _maxSpeed) return;

        _rb.AddForce(dir * Mathf.Min(excess * _pullForce, _maxPull), ForceMode.Acceleration);
    }

    /// <summary>
    /// Коробка встаёт сама, если завалилась.
    ///
    /// Выравниваем МОМЕНТОМ, а не поворотом напрямую: рывком она подпрыгивала
    /// бы и застревала в стенах, а так поднимается как настоящая тележка —
    /// и её можно придержать или толкнуть обратно.
    /// </summary>
    private void AutoRightUp()
    {
        if (!_autoRightUp) return;

        float tilt = Vector3.Angle(transform.up, Vector3.up);

        if (tilt < _fallenAngle)
        {
            _fallenTime = 0f;
            return;
        }

        // Ждём: на кочке коробка кренится на мгновение, и вставать не надо.
        _fallenTime += Time.fixedDeltaTime;
        if (_fallenTime < _fallenDelay) return;

        // Момент, который разворачивает «верх» коробки обратно вверх.
        Vector3 axis = Vector3.Cross(transform.up, Vector3.up);
        _rb.AddTorque(axis * _rightUpTorque, ForceMode.Acceleration);

        // Гасим кувыркание, иначе она перекатится через себя и ляжет снова.
        _rb.angularVelocity *= 0.9f;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, _cableLength);

        Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, _reloadRange);

        if (_torchAttach == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, _torchAttach.position);
    }
}
