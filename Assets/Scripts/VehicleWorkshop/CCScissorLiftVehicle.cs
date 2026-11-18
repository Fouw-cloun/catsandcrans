using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

[DefaultExecutionOrder(100)]
public sealed class CCScissorLiftVehicle : WheeledVehicle
{
    public Transform platform;
    public Transform[] bars;
    [Tooltip("Height of the moving deck above the bottom hinges, in meters.")]
    public float minHeight = .72f;
    public float maxHeight = 5.4f;
    [Min(.05f)] public float raiseSpeed = .68f;
    [Min(.05f)] public float lowerSpeed = .85f;
    [Min(.05f)] public float baseHeight = .45f;
    [Min(.2f)] public float armLength = 2.12f;
    [Min(.1f)] public float halfTrack = .59f;
    public Vector2 platformHalfSize = new(.81f, 1.11f);
    public LayerMask ceilingMask = ~0;
    public Transform console;
    [Range(0, 2)] public float raisedWobble = 1f;
    [Range(45, 84)] public float tipAngle = 58f;
    private const int Stages = 3;

    private readonly SyncVar<float> _height = new(.72f);
    private readonly RaycastHit[] _ceilingHits = new RaycastHit[48];
    public struct RightingOrder
    {
        public Vector3 Position;
        public float Yaw;
        public uint Serial;
    }

    private readonly SyncVar<RightingOrder> _righting = new();
    private readonly RaycastHit[] _landHits = new RaycastHit[48];
    private readonly Collider[] _clearanceHits = new Collider[48];
    private readonly Dictionary<Collider, bool> _operatorColliders = new();
    private float _command, _inputDeadline, _shown, _restDamping, _wobblePhase, _nextRighting;
    private bool _initialized, _exitArmed, _interactorEnabled, _wasSuspended;
    private uint _seenRighting;
    private NetworkObject _operator;
    private Vector3 _controlOffset;
    private PlayerInteractor _interactor;
    private FirstPersonController _operatorMovement;

    protected override void Awake()
    {
        base.Awake();
        if (_body != null) _restDamping = _body.angularDamping;
    }

    public float CurrentHeight => _height.Value;
    private float MinHeight => Mathf.Clamp(minHeight, .18f, armLength * Stages * .94f);
    private float MaxHeight => Mathf.Clamp(maxHeight, MinHeight, armLength * Stages * .97f);
    public bool IsTipped => Vector3.Dot(transform.up, Vector3.up) < Mathf.Cos(tipAngle * Mathf.Deg2Rad);
    protected override bool CanDrive => !IsTipped && !UiModal.AnyOpen;
    protected override bool WheelPhysicsActive => !IsTipped;

    // Chassis and rail colliders do not start the console interaction.
    public override void ServerInteract(PlayerInteractor interactor) { }

    public void ServerUseConsole(PlayerInteractor interactor)
    {
        if (IsServerInitialized && console != null)
            base.ServerInteract(interactor);
    }

    protected override bool CanEnter(PlayerInteractor interactor)
    {
        if (IsTipped || console == null || platform == null || interactor == null) return false;
        Vector3 point = interactor.transform.position;
        var controller = interactor.GetComponent<CharacterController>();
        if (controller != null && controller.enabled)
        {
            Bounds bounds = controller.bounds;
            point = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }
        Vector3 feet = platform.InverseTransformPoint(point);
        return Mathf.Abs(feet.x) < platformHalfSize.x + .05f && Mathf.Abs(feet.z) < platformHalfSize.y + .05f &&
            feet.y >= -.14f && feet.y <= .33f && (point - console.position).sqrMagnitude < 2.65f * 2.65f;
    }

    protected override void ApplyDriverState(NetworkObject player, bool entering)
    {
        if (entering)
        {
            if (_operator == player) return;
            if (_operator != null) ReleaseOperator();
            _operator = player;
            _isLocalDriver = player != null && player.IsOwner;
            if (player == null) return;
            if (_isLocalDriver && platform != null)
                _controlOffset = platform.InverseTransformPoint(player.transform.position);

            _operatorColliders.Clear();
            foreach (var collider in player.GetComponentsInChildren<Collider>(true))
            {
                _operatorColliders[collider] = collider.enabled;
                collider.enabled = false;
            }
            if (!_isLocalDriver) return;
            _exitArmed = false;
            _operatorMovement = player.GetComponent<FirstPersonController>();
            if (_operatorMovement != null)
            {
                _wasSuspended = _operatorMovement.CraneSuspended;
                _operatorMovement.SetCraneSuspended(true, Vector3.zero);
            }
            _interactor = player.GetComponent<PlayerInteractor>();
            if (_interactor != null)
            {
                _interactorEnabled = _interactor.enabled;
                _interactor.enabled = false;
            }
        }
        else if (_operator == player)
        {
            ReleaseOperator();
        }
    }

    private void ReleaseOperator()
    {
        if (_isLocalDriver && _operator != null && _operator.TryGetComponent(out CharacterController controller))
        {
            if (IsTipped) StepOffTippedDeck(controller);
            if (platform != null && platform.TryGetComponent(out CCLiftPassenger passenger))
                passenger.SkipTransfer(controller);
        }
        foreach (var entry in _operatorColliders)
            if (entry.Key != null) entry.Key.enabled = entry.Value;
        _operatorColliders.Clear();
        if (_operatorMovement != null) _operatorMovement.SetCraneSuspended(_wasSuspended, Vector3.zero);
        if (_interactor != null) _interactor.enabled = _interactorEnabled;
        _operator = null;
        _operatorMovement = null;
        _interactor = null;
        _isLocalDriver = false;
        _exitArmed = false;
    }

    private void StepOffTippedDeck(CharacterController controller)
    {
        Transform player = controller.transform;
        Vector3 scale = player.lossyScale;
        float halfHeight = Mathf.Max(.2f, controller.height * Mathf.Abs(scale.y) * .5f);
        float radius = Mathf.Clamp(controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)), .1f, halfHeight);
        Vector3 centerOffset = player.TransformVector(controller.center);
        Vector3 direction = Vector3.ProjectOnPlane(player.position - transform.position, Vector3.up);
        if (direction.sqrMagnitude < .01f) direction = Vector3.forward;
        direction.Normalize();

        for (int i = 0; i < 4; i++)
        {
            Vector3 near = player.position + Quaternion.AngleAxis(i * 90, Vector3.up) * direction * 1.45f + Vector3.up;
            if (!TryGround(near, 8f, out RaycastHit floor)) continue;
            if (Vector3.Dot(floor.normal, Vector3.up) < .75f) continue;
            Vector3 bottom = floor.point + Vector3.up * (radius + .08f);
            Vector3 top = bottom + Vector3.up * (2f * (halfHeight - radius));
            if (Physics.CheckCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore)) continue;
            player.position = (bottom + top) * .5f - centerOffset;
            return;
        }
    }

    public override void OnStopNetwork()
    {
        ReleaseOperator();
        base.OnStopNetwork();
    }

    protected override void VehicleFixedUpdate()
    {
        base.VehicleFixedUpdate();
        if (_body == null) return;
        float raised = Mathf.InverseLerp(MinHeight, Mathf.Max(MinHeight + .2f, MaxHeight), _shown);
        _body.centerOfMass = _centerOfMass + Vector3.up * (raised * .88f) * raisedWobble;
        _body.angularDamping = Mathf.Lerp(_restDamping, _restDamping * .55f, raised * Mathf.Clamp01(raisedWobble));
        float speed = Vector3.ProjectOnPlane(_body.linearVelocity, Vector3.up).magnitude;
        if (IsTipped || raised < .02f || speed < .13f || raisedWobble <= 0) return;
        float motion = Mathf.InverseLerp(.13f, 1.55f, speed);
        _wobblePhase += Time.fixedDeltaTime * (5.2f + motion * 2.25f);
        float steer = _isLocalDriver && !UiModal.AnyOpen ? Input.GetAxisRaw("Horizontal") : 0;
        float roll = Mathf.Sin(_wobblePhase) * 2.85f + steer * 1.5f;
        float pitch = Mathf.Sin(_wobblePhase * .72f + .5f) * 1.05f;
        _body.AddRelativeTorque(new Vector3(pitch, 0, roll) * (raised * motion * raisedWobble), ForceMode.Acceleration);
    }

    [ServerRpc]
    private void RequestRightLift()
    {
        if (HasDriver) ServerTryRightLift();
    }

    public void ServerTryRightLift()
    {
        if (!IsServerInitialized || !IsTipped || Time.time < _nextRighting) return;
        _nextRighting = Time.time + .55f;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (forward.sqrMagnitude < .0025f) forward = Vector3.ProjectOnPlane(transform.right, Vector3.up);
        if (forward.sqrMagnitude < .0025f) forward = Vector3.forward;
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
        if (!FindClearRightingPoint(rotation, out Vector3 position))
        {
            Debug.Log("[Подъёмник] Нет свободного места для постановки на колёса.", this);
            return;
        }

        _command = 0;
        _height.Value = MinHeight;
        var order = new RightingOrder
        {
            Position = position,
            Yaw = rotation.eulerAngles.y,
            Serial = _righting.Value.Serial + 1
        };
        _righting.Value = order;
        if (HasMoveAuthority) ApplyRighting(order);
    }

    private bool TryGround(Vector3 origin, float reach, out RaycastHit ground)
    {
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, _landHits, reach,
            groundMask, QueryTriggerInteraction.Ignore);
        ground = default;
        if (count == _landHits.Length) return false;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _landHits[i];
            Collider col = hit.collider;
            if (col == null || col.transform.IsChildOf(transform) || col.attachedRigidbody != null ||
                col.GetComponentInParent<PlayerInteractor>() != null || hit.distance >= nearest) continue;
            ground = hit;
            nearest = hit.distance;
        }
        return ground.collider != null;
    }

    private bool FindClearRightingPoint(Quaternion rotation, out Vector3 position)
    {
        float clearanceTop = baseHeight + MinHeight + (HasDriver ? 1.95f : 1.58f);
        const float clearanceBottom = .12f;
        float clearanceCenter = (clearanceTop + clearanceBottom) * .5f;
        float clearanceHalf = Mathf.Max(.1f, (clearanceTop - clearanceBottom) * .5f);
        for (int i = 0; i < 5; i++)
        {
            Vector3 offset = i == 0 ? Vector3.zero : rotation *
                (i == 1 ? Vector3.right : i == 2 ? Vector3.left : i == 3 ? Vector3.forward : Vector3.back) * 1.5f;
            Vector3 origin = transform.position + offset + Vector3.up * 1.8f;
            if (!TryGround(origin, 4.2f, out RaycastHit floor)) continue;
            if (floor.point.y > transform.position.y + .95f || Vector3.Dot(floor.normal, Vector3.up) < .88f) continue;

            Vector3 test = floor.point + Vector3.up * .11f;
            int overlaps = Physics.OverlapBoxNonAlloc(test + Vector3.up * clearanceCenter,
                new Vector3(1.01f, clearanceHalf, 1.26f), _clearanceHits, rotation, ~0, QueryTriggerInteraction.Ignore);
            bool blocked = overlaps >= _clearanceHits.Length;
            for (int j = 0; j < overlaps && !blocked; j++)
            {
                var collider = _clearanceHits[j];
                if (collider != null && !collider.transform.IsChildOf(transform)) blocked = true;
            }
            if (!blocked) { position = test; return true; }
        }
        position = default;
        return false;
    }

    private void ApplyRighting(RightingOrder order)
    {
        _seenRighting = order.Serial;
        _shown = MinHeight;
        if (!HasMoveAuthority || _body == null || _body.isKinematic) return;
        var rotation = Quaternion.Euler(0, order.Yaw, 0);
        _body.linearVelocity = Vector3.zero;
        _body.angularVelocity = Vector3.zero;
        _body.position = order.Position;
        _body.rotation = rotation;
        transform.SetPositionAndRotation(order.Position, rotation);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _height.Value = MinHeight;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // The current order describes a historical snap for a late observer.
        if (!HasMoveAuthority) _seenRighting = _righting.Value.Serial;
        if (_driver.Value != null) ApplyDriverState(_driver.Value, true);
    }

    protected override void DriverTick()
    {
        if (UiModal.AnyOpen || IsTipped) return;
        float input = (Input.GetKey(KeyCode.Q) ? 1 : 0) - (Input.GetKey(KeyCode.E) ? 1 : 0);
        SetLiftInput(input);
    }

    [ServerRpc]
    private void SetLiftInput(float input)
    {
        if (!HasDriver || float.IsNaN(input) || float.IsInfinity(input)) return;
        _command = Mathf.Clamp(input, -1, 1);
        _inputDeadline = Time.time + .27f;
    }

    protected override void Update()
    {
        if ((IsServerInitialized || IsClientInitialized) && _seenRighting != _righting.Value.Serial)
            ApplyRighting(_righting.Value);
        if (IsServerInitialized && platform != null)
        {
            float input = HasDriver && !IsTipped && Time.time <= _inputDeadline ? _command : 0;
            float speed = input < 0 ? lowerSpeed : raiseSpeed;
            float next = Mathf.Clamp(_height.Value + input * speed * Mathf.Min(.05f, Time.deltaTime),
                MinHeight, MaxHeight);
            if (next <= _height.Value || !CeilingBlocked(next - _height.Value))
                _height.Value = next;
        }
        ApplyVisuals();
        if (!_isLocalDriver || _operator == null) return;
        if (platform != null) _operator.transform.position = platform.TransformPoint(_controlOffset);
        if (!Input.GetKey(KeyCode.F)) _exitArmed = true;
        if (UiModal.AnyOpen) return;
        if (_exitArmed && Input.GetKeyDown(KeyCode.F))
        {
            _exitArmed = false;
            CmdExit();
        }
        else if (IsTipped && Input.GetKeyDown(KeyCode.R))
            RequestRightLift();
    }

    protected override void ApplyVisuals()
    {
        base.ApplyVisuals();
        if (platform == null) return;
        if (!_initialized)
        {
            _shown = IsServerInitialized || IsClientInitialized ? _height.Value : MinHeight;
            _initialized = true;
        }
        float goal = IsServerInitialized || IsClientInitialized ? _height.Value : MinHeight;
        _shown = Mathf.MoveTowards(_shown, goal, Mathf.Max(raiseSpeed, lowerSpeed) * 1.65f * Time.deltaTime);
        DrawPose(_shown);
    }

    // Also called by the editor setup for an exact folded pose before Play Mode.
    public void DrawPose(float height)
    {
        if (platform == null) return;
        platform.localPosition = new Vector3(0, baseHeight + height, 0);
        if (bars == null) return;

        float dy = height / Stages;
        float span = Mathf.Sqrt(Mathf.Max(.0025f, armLength * armLength - dy * dy));
        for (int i = 0; i < Mathf.Min(bars.Length, Stages * 4); i++)
        {
            var bar = bars[i];
            if (bar == null) continue;
            int stage = i / 4;
            float side = i % 4 < 2 ? -1 : 1;
            bool crossing = i % 2 != 0;
            float x = side * halfTrack + (crossing ? .038f : -.038f);
            float z = (crossing ? 1 : -1) * span * .5f;
            Vector3 a = new Vector3(x, baseHeight + stage * dy, z);
            Vector3 b = new Vector3(x, baseHeight + (stage + 1) * dy, -z);
            bar.localPosition = (a + b) * .5f;
            bar.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
            bar.localScale = new Vector3(.072f, (b - a).magnitude + .06f, .115f);
        }
    }

    private bool CeilingBlocked(float rise)
    {
        Vector3 up = transform.up;
        Vector3 origin = transform.TransformPoint(new Vector3(0, baseHeight + _height.Value + 1.84f, 0));
        Vector3 half = new Vector3(platformHalfSize.x * .96f, .055f, platformHalfSize.y * .96f);
        int count = Physics.BoxCastNonAlloc(origin, half, up, _ceilingHits, transform.rotation,
            rise + .13f, ceilingMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var hit = _ceilingHits[i].collider;
            if (hit == null || hit.transform.IsChildOf(transform)) continue;
            var player = hit.GetComponentInParent<PlayerInteractor>();
            if (player != null)
            {
                if (IsDriver(player.NetworkObject)) continue;
                Vector3 pos = platform.InverseTransformPoint(player.transform.position);
                if (Mathf.Abs(pos.x) < platformHalfSize.x && Mathf.Abs(pos.z) < platformHalfSize.y &&
                    pos.y > -.25f && pos.y < 1.5f) continue;
            }
            return true;
        }
        return count == _ceilingHits.Length;
    }
}
