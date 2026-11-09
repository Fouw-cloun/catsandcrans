using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>Башенный кран: сервер считает движение и физику, водитель отправляет только оси.</summary>
public class TowerCraneVehicle : NetworkVehicle
{
    [Header("Части крана (старые ссылки сохранены)")]
    [SerializeField] private Transform _tower;
    [SerializeField] private Vector3 _towerAxis = Vector3.up;
    [SerializeField] private Transform _trolley;
    [SerializeField] private Vector3 _trolleyAxis = Vector3.right;
    [SerializeField] private ConfigurableJoint _hookJoint;
    [SerializeField] private LineRenderer _rope;
    [SerializeField] private Transform _ropeTopPoint;
    [SerializeField] private Transform _hookTransform;

    [Header("Новый подвес")]
    [SerializeField] private CraneHook _hook;
    [SerializeField] private CraneCableVisual _cable;
    [Tooltip("Начать с фактического расстояния до крюка, без рывка при запуске")]
    [SerializeField] private bool _measureInitialRope = true;
    [Header("Скорости")]
    [SerializeField] private float _rotateSpeed = 25f;
    [SerializeField] private float _trolleySpeed = 3f;
    [SerializeField] private float _hookSpeed = 4f;
    [SerializeField] private float _visualSmoothSpeed = 90f;
    [Tooltip("Время разгона и торможения механизма, секунды")]
    [Min(0.05f)][SerializeField] private float _accelerationTime = 0.6f;
    [Header("Пределы")]
    [SerializeField] private Vector2 _trolleyLimit = new Vector2(2f, 25f);
    [SerializeField] private Vector2 _hookLimit = new Vector2(1f, 30f);

    public struct MotionState
    {
        public bool Ready;
        public float Rotation, Trolley, Rope;
    }
    private readonly SyncVar<MotionState> _motion = new(new SyncTypeSettings(0.05f));
    private Quaternion _towerBaseRot;
    private Vector3 _trolleyBasePos;
    private Vector3 _input, _velocity;
    private MotionState _visual;
    private float _lastInputTime, _nextToggle;
    private bool _visualReady;
    public CraneHook Hook => _hook;

    protected override void Awake()
    {
        _isStatic = true;
        base.Awake();
        if (_tower != null) _towerBaseRot = _tower.localRotation;
        if (_trolley != null) _trolleyBasePos = _trolley.localPosition;
        if (_hook == null && _hookJoint != null) _hook = _hookJoint.GetComponent<CraneHook>();
        if (_cable == null && _rope != null) _cable = _rope.GetComponent<CraneCableVisual>();
        if (_cable != null && _hook != null)
        {
            _cable.Bind(_ropeTopPoint, _hook);
            if (_rope != null) _rope.enabled = false;
        }
        if (_trolley != null && _trolley.TryGetComponent(out Rigidbody trolleyBody))
        {
            trolleyBody.isKinematic = true;
            trolleyBody.useGravity = false;
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (_hook == null || _tower == null || _trolley == null || _ropeTopPoint == null)
        {
            Debug.LogError("[Кран] Запусти настройку выбранного крана через Tools/CatCooks/Crane.", this);
            return;
        }
        float length = _measureInitialRope
            ? Vector3.Distance(_ropeTopPoint.position, _hook.RopeAnchorWorld)
            : (_hookJoint != null ? _hookJoint.linearLimit.limit : _hookLimit.x);
        // Не подтягиваем крюк телепортом, даже если сцена настроена вне диапазона.
        if (length > _hookLimit.y || length < _hookLimit.x)
            Debug.LogWarning("[Кран] Стартовая длина вне Hook Limit. Поправь высоту крюка/пределы в сцене.", this);
        _motion.Value = new MotionState {
            Ready = true, Rotation = 0f,
            Trolley = Vector3.Dot(_trolley.localPosition, Axis(_trolleyAxis)),
            Rope = Mathf.Max(0.1f, length)
        };
        _hook.ServerSetSuspension(_ropeTopPoint.position, _motion.Value.Rope);
    }

    protected override void DriverUpdate()
    {
        if (UiModal.AnyOpen) return;
        if (Input.GetKeyDown(KeyCode.H)) CmdToggleHook();
    }

    protected override void DriverTick()
    {
        Vector3 axes = Vector3.zero;
        if (!UiModal.AnyOpen && Application.isFocused)
        {
            axes.x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            axes.y = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            // R поднимает (укорачивает трос), G опускает.
            axes.z = (Input.GetKey(KeyCode.G) ? 1f : 0f) - (Input.GetKey(KeyCode.R) ? 1f : 0f);
        }
        CmdInput(axes);
    }

    [ServerRpc]
    private void CmdInput(Vector3 axes)
    {
        if (_driver.Value == null || !CraneMath.Finite(axes)) return;
        _input = new Vector3(Mathf.Clamp(axes.x, -1f, 1f), Mathf.Clamp(axes.y, -1f, 1f), Mathf.Clamp(axes.z, -1f, 1f));
        _lastInputTime = Time.unscaledTime;
    }

    [ServerRpc]
    private void CmdToggleHook()
    {
        if (_driver.Value == null || _hook == null || Time.unscaledTime < _nextToggle) return;
        _nextToggle = Time.unscaledTime + 0.35f;
        _hook.ServerToggleNearest();
    }

    public override void OnOwnershipServer(NetworkConnection previous)
    {
        base.OnOwnershipServer(previous);
        _input = Vector3.zero;
        if (Owner == null || !Owner.IsValid) _driver.Value = null;
    }

    protected override void FixedUpdate()
    {
        if (!IsServerInitialized || !_motion.Value.Ready || _hook == null) return;
        if (_driver.Value == null || Owner == null || !Owner.IsValid || Time.unscaledTime - _lastInputTime > 0.35f)
            _input = Vector3.zero;
        float dt = Time.fixedDeltaTime;
        float ramp = Mathf.Max(0.05f, _accelerationTime);
        _velocity.x = Mathf.MoveTowards(_velocity.x, _input.x * _rotateSpeed, _rotateSpeed / ramp * dt);
        _velocity.y = Mathf.MoveTowards(_velocity.y, _input.y * _trolleySpeed, _trolleySpeed / ramp * dt);
        _velocity.z = Mathf.MoveTowards(_velocity.z, _input.z * _hookSpeed, _hookSpeed / ramp * dt);
        MotionState state = _motion.Value;
        state.Rotation = Mathf.Repeat(state.Rotation + _velocity.x * dt, 360f);
        state.Trolley = CraneMath.StepIntoRange(state.Trolley, _velocity.y * dt, _trolleyLimit);
        state.Rope = CraneMath.StepIntoRange(state.Rope, _velocity.z * dt, _hookLimit);
        _motion.Value = state;
        ApplyParts(state);
        _hook.ServerSetSuspension(_ropeTopPoint.position, state.Rope);
    }

    protected override void ApplyVisuals()
    {
        if (!_motion.Value.Ready) return;
        if (!IsServerInitialized)
        {
            MotionState target = _motion.Value;
            if (!_visualReady) { _visual = target; _visualReady = true; }
            float dt = Time.deltaTime;
            _visual.Rotation = Mathf.MoveTowardsAngle(_visual.Rotation, target.Rotation, _visualSmoothSpeed * dt);
            _visual.Trolley = Mathf.MoveTowards(_visual.Trolley, target.Trolley, _trolleySpeed * 4f * dt);
            _visual.Rope = Mathf.MoveTowards(_visual.Rope, target.Rope, _hookSpeed * 4f * dt);
            ApplyParts(_visual);
        }
        if (_cable != null) _cable.Length = IsServerInitialized ? _motion.Value.Rope : _visual.Rope;
        // Совместимость до установки нового визуала.
        if (_cable == null && _rope != null && _ropeTopPoint != null && _hookTransform != null)
        {
            _rope.useWorldSpace = true;
            _rope.positionCount = 2;
            _rope.SetPosition(0, _ropeTopPoint.position);
            _rope.SetPosition(1, _hookTransform.position);
        }
    }

    private void ApplyParts(MotionState state)
    {
        if (_tower != null) _tower.localRotation = _towerBaseRot * Quaternion.AngleAxis(state.Rotation, Axis(_towerAxis));
        if (_trolley != null)
        {
            Vector3 axis = Axis(_trolleyAxis);
            _trolley.localPosition = _trolleyBasePos + axis * (state.Trolley - Vector3.Dot(_trolleyBasePos, axis));
        }
    }
    private static Vector3 Axis(Vector3 value) => value.sqrMagnitude > 0.001f ? value.normalized : Vector3.right;
}
