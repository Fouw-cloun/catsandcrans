using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Подвес игрока без смены ownership: серверная капсула даёт вес и столкновения,
/// владелец следует её снимкам, штатный NetworkTransform игрока передаёт позу наблюдателям.
/// Камера сохраняет обзор, управление ходьбой приостановлено. Это подвес, не ragdoll.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(FirstPersonController), typeof(CharacterController))]
public sealed class CraneRider : NetworkBehaviour
{
    [SerializeField] private Vector3 _harnessLocal = new Vector3(0f, 1.6f, 0f);
    [Min(1f)][SerializeField] private float _mass = 70f;
    [Min(0f)][SerializeField] private float _swingAcceleration = 2f;
    [Min(0.01f)][SerializeField] private float _followTime = 0.045f;
    [Tooltip("Слой капсулы подвеса. -1 — слой корня игрока. Должен сталкиваться с миром.")]
    [SerializeField] private int _proxyLayer = -1;
    public struct RideState
    {
        public bool Hanging;
        public Vector3 Position, Velocity;
    }
    private readonly SyncVar<RideState> _state = new(new SyncTypeSettings(0.05f));
    private Rigidbody _proxy;
    private ConfigurableJoint _joint;
    private CraneHook _hook;
    private FirstPersonController _controller;
    private CharacterController _character;
    private PlayerInteractor _interactor;
    private Collider[] _colliders;
    private bool[] _colliderEnabled;
    private bool _interactorEnabled, _applied;
    private Vector3 _swing, _followVelocity;
    private float _lastSwing, _nextInput, _nextSnapshot, _snapshotTime;
    public bool IsHanging => _state.Value.Hanging;
    public float Mass => _mass;
    public Vector3 HarnessWorld => transform.TransformPoint(_harnessLocal);

    private void Awake()
    {
        _controller = GetComponent<FirstPersonController>();
        _character = GetComponent<CharacterController>();
        _interactor = GetComponent<PlayerInteractor>();
        _colliders = GetComponentsInChildren<Collider>(true);
    }
    public override void OnStartNetwork()
    {
        base.OnStartNetwork(); _state.OnChange += StateChanged;
    }
    public override void OnStartClient() { base.OnStartClient(); ApplyState(); }
    private void StateChanged(RideState previous, RideState next, bool asServer)
    {
        _snapshotTime = Time.unscaledTime;
        ApplyState();
    }

    public bool ServerHang(CraneHook hook)
    {
        if (!IsServerInitialized || IsHanging || hook == null || Owner == null || !Owner.IsValid) return false;
        // Тот же NetworkObject остаётся за игроком. Посадка/лазание/ручной перенос несовместимы.
        if (_controller.IsClimbing || _controller.CartSteering) return false;
        foreach (NetworkVehicle vehicle in FindObjectsByType<NetworkVehicle>(FindObjectsSortMode.None))
            if (vehicle.IsDriver(NetworkObject)) return false;
        PlayerHand hand = GetComponent<PlayerHand>();
        if (hand != null && hand.HasItem) return false;
        foreach (CooperativeCarry carry in FindObjectsByType<CooperativeCarry>(FindObjectsSortMode.None))
            if (carry.Holds(NetworkObject)) return false;
        Vector3 initial = hook.LatchWorld - transform.TransformVector(_harnessLocal);
        // Перед snap проверяем объём, чтобы не посадить человека внутрь перекрытия.
        float scale = Mathf.Abs(transform.lossyScale.y);
        float radius = _character.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));
        float height = Mathf.Max(radius * 2f, _character.height * scale);
        Vector3 center = initial + transform.TransformVector(_character.center);
        float half = height * 0.5f - radius;
        foreach (Collider c in Physics.OverlapCapsule(center + Vector3.up * half, center - Vector3.up * half,
                     radius * 0.95f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c.transform.IsChildOf(transform) || c.attachedRigidbody == hook.Body) continue;
            return false;
        }
        GameObject body = new GameObject(name + "_ServerCraneRider");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(body, gameObject.scene);
        body.layer = _proxyLayer >= 0 && _proxyLayer <= 31 ? _proxyLayer : gameObject.layer;
        body.transform.SetPositionAndRotation(initial, Quaternion.identity);
        CapsuleCollider capsule = body.AddComponent<CapsuleCollider>();
        capsule.radius = radius;
        capsule.height = height;
        capsule.center = transform.TransformVector(_character.center);
        _proxy = body.AddComponent<Rigidbody>();
        _proxy.mass = _mass;
        _proxy.constraints = RigidbodyConstraints.FreezeRotation;
        _proxy.linearDamping = 0.12f;
        _proxy.solverIterations = 20;
        _proxy.solverVelocityIterations = 10;
        _proxy.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _proxy.linearVelocity = hook.Body.GetPointVelocity(hook.LatchWorld);
        _joint = body.AddComponent<ConfigurableJoint>();
        _joint.autoConfigureConnectedAnchor = false;
        _joint.connectedBody = hook.Body;
        _joint.anchor = transform.TransformVector(_harnessLocal);
        _joint.connectedAnchor = hook.transform.InverseTransformPoint(hook.LatchWorld);
        _joint.xMotion = _joint.yMotion = _joint.zMotion = ConfigurableJointMotion.Locked;
        _joint.angularXMotion = _joint.angularYMotion = _joint.angularZMotion = ConfigurableJointMotion.Free;
        _joint.enableCollision = false;
        _joint.projectionMode = JointProjectionMode.PositionAndRotation;
        _joint.projectionDistance = 0.1f;
        _hook = hook;
        _state.Value = new RideState { Hanging = true, Position = initial, Velocity = _proxy.linearVelocity };
        ApplyState();
        return true;
    }

    public void ServerRelease()
    {
        if (!IsServerInitialized || !IsHanging) return;
        RideState final = new RideState {
            Hanging = false,
            Position = _proxy != null ? _proxy.position : _state.Value.Position,
            Velocity = _proxy != null ? _proxy.linearVelocity : _state.Value.Velocity
        };
        DestroyProxy();
        _hook = null;
        _swing = Vector3.zero;
        _state.Value = final;
        ApplyState();
    }

    private void ApplyState()
    {
        bool hanging = IsHanging;
        if (hanging == _applied) return;
        if (hanging)
        {
            _colliderEnabled = new bool[_colliders.Length];
            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] == null) continue;
                _colliderEnabled[i] = _colliders[i].enabled;
                _colliders[i].enabled = false;
            }
            _controller.SetCraneSuspended(true, Vector3.zero);
            if (IsOwner)
            {
                if (_interactor != null) { _interactorEnabled = _interactor.enabled; _interactor.enabled = false; }
                transform.position = _state.Value.Position;
                _followVelocity = Vector3.zero;
            }
        }
        else
        {
            if (IsOwner) transform.position = _state.Value.Position;
            _controller.SetCraneSuspended(false, _state.Value.Velocity);
            RestoreColliders();
            if (IsOwner && _interactor != null) _interactor.enabled = _interactorEnabled;
        }
        _applied = hanging;
    }
    private void Update()
    {
        if (!IsOwner || !IsHanging) return;
        if (!UiModal.AnyOpen && Input.GetKeyDown(KeyCode.Space)) CmdRelease();
        if (Time.unscaledTime < _nextInput) return;
        _nextInput = Time.unscaledTime + 0.05f;
        Vector3 direction = Vector3.zero;
        if (!UiModal.AnyOpen && Application.isFocused)
            direction = transform.right * Input.GetAxisRaw("Horizontal") + transform.forward * Input.GetAxisRaw("Vertical");
        direction.y = 0f;
        CmdSwing(Vector3.ClampMagnitude(direction, 1f));
    }
    private void LateUpdate()
    {
        if (!IsOwner || !IsHanging) return;
        float age = Mathf.Clamp(Time.unscaledTime - _snapshotTime, 0f, 0.075f);
        Vector3 target = _state.Value.Position + _state.Value.Velocity * age;
        transform.position = Vector3.SmoothDamp(transform.position, target, ref _followVelocity, _followTime);
    }
    [ServerRpc]
    private void CmdSwing(Vector3 direction)
    {
        if (!IsHanging || !CraneMath.Finite(direction)) return;
        direction.y = 0f;
        _swing = Vector3.ClampMagnitude(direction, 1f);
        _lastSwing = Time.unscaledTime;
    }
    [ServerRpc]
    private void CmdRelease()
    {
        if (IsHanging && _hook != null) _hook.ServerDetach();
    }
    private void FixedUpdate()
    {
        if (!IsServerInitialized || !IsHanging) return;
        if (_hook == null || !_hook.IsSpawned || _proxy == null || Owner == null || !Owner.IsValid)
        {
            if (_hook != null) _hook.ServerDetach(); else ServerRelease();
            return;
        }
        if (Time.unscaledTime - _lastSwing > 0.25f) _swing = Vector3.zero;
        _proxy.AddForce(_swing * _swingAcceleration, ForceMode.Acceleration);
        if (Time.unscaledTime < _nextSnapshot) return;
        _nextSnapshot = Time.unscaledTime + 0.05f;
        _state.Value = new RideState { Hanging = true, Position = _proxy.position, Velocity = _proxy.linearVelocity };
    }
    private void DestroyProxy()
    {
        if (_proxy == null) return;
        // Destroy отложен: сразу прекращаем физику и столкновения.
        _proxy.detectCollisions = false;
        _proxy.collisionDetectionMode = CollisionDetectionMode.Discrete;
        _proxy.isKinematic = true;
        if (_joint != null) _joint.xMotion = _joint.yMotion = _joint.zMotion = ConfigurableJointMotion.Free;
        Destroy(_proxy.gameObject);
        _proxy = null; _joint = null;
    }
    private void RestoreColliders()
    {
        if (_colliderEnabled == null) return;
        for (int i = 0; i < _colliders.Length; i++)
            if (_colliders[i] != null) _colliders[i].enabled = _colliderEnabled[i];
    }
    public override void OnStopServer()
    {
        if (_hook != null) _hook.ServerDetach();
        DestroyProxy();
        base.OnStopServer();
    }
    public override void OnStopNetwork()
    {
        _state.OnChange -= StateChanged;
        if (_applied)
        {
            _controller.SetCraneSuspended(false, Vector3.zero);
            RestoreColliders();
            if (_interactor != null && IsOwner) _interactor.enabled = _interactorEnabled;
            _applied = false;
        }
        DestroyProxy();
        base.OnStopNetwork();
    }
}
