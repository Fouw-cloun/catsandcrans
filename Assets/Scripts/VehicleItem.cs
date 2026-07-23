using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Техника (трактор). В руку не берётся — F садит/высаживает водителя.
/// Пока за рулём: WASD — езда, F — выйти. Управление у владельца, позиция
/// расходится через client-authoritative NetworkTransform.
///
/// Пока водитель в технике:
///   - его FirstPersonController и PlayerInteractor выключаются
///   - он каждый кадр ставится в точку сиденья (едет вместе с техникой)
///   - камера игрока остаётся на нём, поэтому смотришь из кабины
///
/// Это рабочий скелет. Полировка (нормальное родительство, точка высадки,
/// поворот колёс) — точки расширения, помечены TODO.
/// </summary>
public class VehicleItem : InteractableItem
{
    [Header("Техника")]
    [Tooltip("Точка посадки водителя (пустой объект в кабине)")]
    [SerializeField] private Transform _seatAnchor;
    [Tooltip("Куда высадить при выходе")]
    [SerializeField] private Transform _exitPoint;
    [SerializeField] private float _driveSpeed = 8f;
    [SerializeField] private float _turnSpeed = 60f;

    // Текущий водитель (его NetworkObject), синхронизируется всем.
    private readonly SyncVar<NetworkObject> _driver = new(null);

    private FirstPersonController _driverController;
    private PlayerInteractor _driverInteractor;
    private bool _isLocalDriver;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = false; // технику нельзя взять в руку
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _driver.OnChange += OnDriverChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _driver.OnChange -= OnDriverChanged;
    }

    // ───────── ПОСАДКА (F с пустой рукой) ─────────

    public override void ServerInteract(PlayerInteractor interactor)
    {
        if (_driver.Value != null) return; // занято

        _driver.Value = interactor.NetworkObject;
        // Управление техникой — водителю.
        NetworkObject.GiveOwnership(interactor.Owner);
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdExit()
    {
        if (_driver.Value == null) return;
        _driver.Value = null;
        NetworkObject.RemoveOwnership();
    }

    private void OnDriverChanged(NetworkObject prev, NetworkObject next, bool asServer)
    {
        // Сел новый водитель.
        if (next != null)
        {
            _driverController = next.GetComponent<FirstPersonController>();
            _driverInteractor = next.GetComponent<PlayerInteractor>();
            _isLocalDriver = next.IsOwner; // true только на клиенте водителя

            if (_isLocalDriver)
            {
                if (_driverController != null) _driverController.enabled = false;
                if (_driverInteractor != null) _driverInteractor.enabled = false;
            }
        }
        // Водитель вышел.
        else if (prev != null)
        {
            FirstPersonController fpc = prev.GetComponent<FirstPersonController>();
            PlayerInteractor pi = prev.GetComponent<PlayerInteractor>();
            bool wasLocal = prev.IsOwner;

            if (wasLocal)
            {
                if (fpc != null) fpc.enabled = true;
                if (pi != null) pi.enabled = true;
                // Высадить рядом.
                if (_exitPoint != null) prev.transform.position = _exitPoint.position;
            }
            _isLocalDriver = false;
        }
    }

    // ───────── ЕЗДА (клиент-водитель) ─────────

    private void Update()
    {
        if (!_isLocalDriver) return;

        // F — выйти.
        if (Input.GetKeyDown(KeyCode.F))
        {
            CmdExit();
            return;
        }

        // WASD — движение и поворот.
        float v = Input.GetAxisRaw("Vertical");
        float h = Input.GetAxisRaw("Horizontal");

        transform.Translate(Vector3.forward * v * _driveSpeed * Time.deltaTime, Space.Self);
        transform.Rotate(Vector3.up, h * _turnSpeed * Time.deltaTime);

        // Водитель едет вместе с кабиной.
        if (_driver.Value != null && _seatAnchor != null)
            _driver.Value.transform.SetPositionAndRotation(_seatAnchor.position, _seatAnchor.rotation);

        // TODO: поворот колёс, звук двигателя, пыль из-под колёс.
    }
}
