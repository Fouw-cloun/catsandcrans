using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// База пылесоса с компрессором на колёсах. Машина (Rigidbody), ничья — физику
/// считает СЕРВЕР, клиенты кинематичны и идут за NetworkTransform.
///
/// ПОДТЯГИВАНИЕ теперь ВНУТРИ этого скрипта (надёжная серверная проверка). Пока
/// расстояние до дуло больше длины шланга — база едет к дулу ГОРИЗОНТАЛЬНОЙ
/// тягой. Тянет ВСЕГДА (связаны шлангом), не завися от кнопки-компрессора.
/// Отдельный HoseConnection БОЛЬШЕ НЕ НУЖЕН — удали его с базы.
///
///   - Кнопка (F на VacuumButtonPart) вкл/выкл компрессор (пока просто флаг).
///   - Клавиша Y (у ближнего игрока) — поставить базу на колёса, если упала.
///
/// Требует: NetworkObject, NetworkTransform (Client Authoritative), Rigidbody
/// (Is Kinematic СНЯТО, Collision Detection = Continuous), Collider.
/// </summary>
public class VacuumStation : NetworkBehaviour
{
    [Header("Подтягивание за дулом")]
    [Tooltip("Точка крепления шланга на ДУЛЕ (перетащи Nozzle/HoseAttach). БЕЗ неё база не поедет!")]
    [SerializeField] private Transform _nozzleAttach;
    [Tooltip("Длина шланга. Дальше этого расстояния база едет к дулу.")]
    [SerializeField] private float _hoseLength = 4f;
    [Tooltip("Запас: тянуть, только если превысили длину на эту величину")]
    [SerializeField] private float _tensionThreshold = 0.2f;
    [Tooltip("Сила подтягивания на метр перетяга")]
    [SerializeField] private float _pullForce = 12f;
    [Tooltip("Ограничение силы (чтобы не улетала при резком рывке)")]
    [SerializeField] private float _maxPull = 40f;
    [Tooltip("Максимальная горизонтальная скорость подтягивания")]
    [SerializeField] private float _maxSpeed = 5f;

    [Header("Устойчивость")]
    [Tooltip("Центр масс по Y (ниже = устойчивее, меньше качается/опрокидывается)")]
    [SerializeField] private float _centerOfMassY = -0.5f;

    [Header("Компрессор (пока просто флаг)")]
    private readonly SyncVar<bool> _isRunning = new(false);

    [Header("Подъём на колёса (Y)")]
    [SerializeField] private KeyCode _rightUpKey = KeyCode.Y;
    [SerializeField] private float _rightUpRange = 4f;
    [SerializeField] private float _rightUpLift = 0.4f;

    private Rigidbody _rb;
    private bool _warnedNoNozzle;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (_rb != null)
        {
            _rb.isKinematic = false; // сервер считает физику базы
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            _rb.centerOfMass = new Vector3(0f, _centerOfMassY, 0f);
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // Кинематим ТОЛЬКО на чистом клиенте (не на хосте!) — иначе на хосте база
        // стала бы кинематичной и не двигалась. IsServerStarted = есть локальный сервер.
        if (!base.IsServerStarted && _rb != null)
            _rb.isKinematic = true;
    }

    // ───────── КНОПКА / Y ─────────

    public void ServerToggleCompressor() => _isRunning.Value = !_isRunning.Value;

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
        Vector3 fwd = transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
        _rb.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
        _rb.position += Vector3.up * _rightUpLift;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
    }

    // ───────── ФИЗИКА + ТЯГА (сервер) ─────────

    private void FixedUpdate()
    {
        if (!base.IsServerInitialized || _rb == null) return;

        if (_nozzleAttach == null)
        {
            if (!_warnedNoNozzle)
            {
                Debug.LogError($"[VacuumStation] Не назначен Nozzle Attach на '{name}' — база не будет тянуться! Перетащи точку крепления шланга дуло.");
                _warnedNoNozzle = true;
            }
            return;
        }

        Vector3 to = _nozzleAttach.position - transform.position;
        float dist = to.magnitude;
        float excess = dist - _hoseLength;
        if (excess <= _tensionThreshold) return;

        // Горизонтальная тяга к дулу.
        Vector3 dir = to; dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();

        // Лимит по ГОРИЗОНТАЛЬНОЙ скорости (вертикаль/гравитацию не считаем).
        Vector3 v = _rb.linearVelocity; v.y = 0f;
        if (v.magnitude >= _maxSpeed) return;

        float force = Mathf.Min(excess * _pullForce, _maxPull);
        _rb.AddForce(dir * force, ForceMode.Acceleration);
    }

    public bool IsRunning => _isRunning.Value;

    private void OnDrawGizmosSelected()
    {
        if (_nozzleAttach == null) return;
        // Радиус длины шланга и линия до дуло — видно, когда начнётся тяга.
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, _nozzleAttach.position);
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, _hoseLength);
    }
}
