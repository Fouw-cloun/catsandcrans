using CatCooks.Food.Tools;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Food = CatCooks.Food;
using Cookware = CatCooks.Food.Cookware;
using FoodPiece = CatCooks.Food.FoodPiece;

public sealed class CCMiniExcavatorVehicle : ExcavatorVehicle
{
    public CCMiniExcavatorAttachment[] attachments;
    public Cookware bucket;
    public LayerMask toolMask = ~0;
    [Min(1)] public float maxCargoMass = 350f;

    private readonly SyncVar<int> _selected = new(0);
    private readonly SyncVar<bool> _power = new(false);
    private readonly SyncVar<bool> _grip = new(false);
    private readonly Collider[] _contacts = new Collider[64];
    private readonly RaycastHit[] _parkingHits = new RaycastHit[24];
    private const RigidbodyConstraints ParkingAxes =
        RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
    private RigidbodyConstraints _addedParkingAxes;
    private bool _parkingHeld;
    private Rigidbody _cargo;
    private NetworkObject _cargoNetwork;
    private Vector3 _cargoOffset, _lastPosition;
    private Quaternion _cargoRotation;
    private float _inputExpires, _nextInput, _nextHit, _speed;
    private int _visibleTool = -1;
    private bool _sentPower;

    private bool InWorkMode => !base.CanDrive;
    private CCMiniExcavatorAttachment ActiveTool =>
        attachments != null && _selected.Value >= 0 && _selected.Value < attachments.Length ?
            attachments[_selected.Value] : null;
    protected override bool CanDrive => base.CanDrive && !UiModal.AnyOpen;

    protected override void DriverUpdate()
    {
        if (UiModal.AnyOpen) return;
        base.DriverUpdate(); // Tab and arm controls from the full-size excavator.
        if (!InWorkMode) return;
        if (Input.GetKeyDown(KeyCode.G)) RequestGrip();
        if (_body != null && _body.linearVelocity.sqrMagnitude > .25f) return;
        if (attachments == null) return;
        for (int i = 0; i < attachments.Length && i < 8; i++)
        {
            if (!Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i))) continue;
            RequestTool(i);
            break;
        }
    }

    protected override void DriverTick()
    {
        base.DriverTick();
        var tool = ActiveTool;
        bool power = InWorkMode && !UiModal.AnyOpen && tool != null &&
            HasMotor(tool.kind) && Input.GetKey(KeyCode.Y);
        if (power == _sentPower && (!power || Time.time < _nextInput)) return;
        _sentPower = power;
        _nextInput = Time.time + .12f;
        RequestPower(power);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RequestTool(int index)
    {
        if (!HasDriver || !InWorkMode || !Stopped() || _power.Value || _grip.Value ||
            attachments == null || index < 0 || index >= attachments.Length ||
            index == _selected.Value || attachments[index] == null) return;
        if (_selected.Value == 0 && bucket != null)
        {
            if (bucket.TotalCount > 0) return;
            foreach (var piece in bucket.Loose)
                if (piece != null && !piece.IsConsumed && !piece.IsHeld && bucket.IsInside(piece.transform.position))
                    return;
        }

        _inputExpires = 0;
        _selected.Value = index;
        ApplyToolSelection();
    }

    [ServerRpc(RequireOwnership = true)]
    private void RequestPower(bool power)
    {
        var tool = ActiveTool;
        _inputExpires = power && HasDriver && InWorkMode && tool != null &&
            HasMotor(tool.kind) ? Time.time + .4f : 0f;
    }

    [ServerRpc(RequireOwnership = true)]
    private void RequestGrip()
    {
        var tool = ActiveTool;
        if (!HasDriver || !InWorkMode || tool == null || !HasGrip(tool.kind)) return;
        if (_grip.Value)
        {
            ReleaseCargo();
            return;
        }

        Rigidbody candidate = FindCargo(tool, out NetworkObject networkObject);
        _grip.Value = candidate != null || tool.kind != CCMiniTool.Forks;
        if (candidate == null) return;
        _cargo = candidate;
        _cargoNetwork = networkObject;
        _cargoOffset = tool.tip.InverseTransformPoint(_cargo.position);
        _cargoRotation = Quaternion.Inverse(tool.tip.rotation) * _cargo.rotation;
        _cargo.linearVelocity = Vector3.zero;
        _cargo.angularVelocity = Vector3.zero;
        _cargo.isKinematic = true;
    }

    protected override void ApplyDriverState(NetworkObject player, bool entering)
    {
        base.ApplyDriverState(player, entering);
        if (!entering)
        {
            _sentPower = false;
            if (IsServerInitialized) _inputExpires = 0f;
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _lastPosition = transform.position;
    }

    public override void OnStopServer()
    {
        ReleaseCargo();
        base.OnStopServer();
    }

    protected override void Update()
    {
        base.Update();
        if (!IsServerInitialized) return;
        var tool = ActiveTool;
        float dt = Time.deltaTime;
        Vector3 position = transform.position;
        if (dt > .0001f)
            _speed = Mathf.Lerp(_speed, (position - _lastPosition).magnitude / dt, 1f - Mathf.Exp(-12f * dt));
        _lastPosition = position;

        _power.Value = HasDriver && InWorkMode && tool != null && HasMotor(tool.kind) &&
            Time.time < _inputExpires;
        if (_power.Value && tool.tip != null) RunTool(tool, Mathf.Min(dt, .05f));
    }

    public override void OnStopNetwork()
    {
        ReleaseParkingBrake();
        base.OnStopNetwork();
    }

    protected override void VehicleFixedUpdate()
    {
        bool driving = IsDriving && !Input.GetKey(KeyCode.Space) &&
            (Mathf.Abs(Input.GetAxisRaw("Vertical")) > .01f ||
             Mathf.Abs(Input.GetAxisRaw("Horizontal")) > .01f);
        if (driving) ReleaseParkingBrake();

        base.VehicleFixedUpdate();
        if (driving) return;

        Vector3 velocity = _body.linearVelocity;
        if (GroundContacts < 4 || Vector3.Dot(transform.up, Vector3.up) < .65f ||
            Mathf.Abs(velocity.y) > .45f ||
            !HasStaticSupport(leftTrackPoints) || !HasStaticSupport(rightTrackPoints))
        {
            ReleaseParkingBrake();
            return;
        }

        if (_parkingHeld) return;
        Vector3 travel = new Vector3(velocity.x, 0f, velocity.z);
        if (travel.sqrMagnitude > .23f * .23f)
        {
            // Settle gently before holding the horizontal position.
            Vector3 brake = Vector3.ClampMagnitude(-travel / Time.fixedDeltaTime, 5f);
            _body.AddForce(brake, ForceMode.Acceleration);
            return;
        }

        _body.linearVelocity = new Vector3(0f, velocity.y, 0f);
        _addedParkingAxes = ParkingAxes & ~_body.constraints;
        _body.constraints |= ParkingAxes;
        _parkingHeld = true;
    }

    private bool HasStaticSupport(Transform[] points)
    {
        if (points == null) return false;
        Vector3 down = -transform.up;
        float reach = restLength + trackRadius;
        foreach (Transform point in points)
        {
            if (point == null) continue;
            if (CCVehicleGround.Cast(transform, _body, point.position, down, reach,
                groundMask, _parkingHits, out RaycastHit hit) && hit.rigidbody == null)
                return true;
        }
        return false;
    }

    private void ReleaseParkingBrake()
    {
        if (!_parkingHeld) return;
        if (_body != null) _body.constraints &= ~_addedParkingAxes;
        _addedParkingAxes = RigidbodyConstraints.None;
        _parkingHeld = false;
    }

    protected override void FixedUpdate()
    {
        if (!HasMoveAuthority || _body == null || _body.isKinematic) ReleaseParkingBrake();
        base.FixedUpdate();
        if (!IsServerInitialized || _cargo == null) return;
        var tool = ActiveTool;
        if (!CargoAvailable() || tool == null || tool.tip == null)
        {
            ReleaseCargo();
            return;
        }
        _cargo.MovePosition(tool.tip.TransformPoint(_cargoOffset));
        _cargo.MoveRotation(tool.tip.rotation * _cargoRotation);
    }

    protected override void ApplyVisuals()
    {
        base.ApplyVisuals();
        ApplyToolSelection();
        var tool = ActiveTool;
        if (tool != null) tool.Animate(_power.Value, _grip.Value, Time.deltaTime);
    }

    private void ApplyToolSelection()
    {
        if (attachments == null || _visibleTool == _selected.Value) return;
        _visibleTool = _selected.Value;
        for (int i = 0; i < attachments.Length; i++)
        {
            var attachment = attachments[i];
            if (attachment != null) attachment.Show(i == _visibleTool);
        }
    }

    private bool Stopped() =>
        HasMoveAuthority && _body != null ? _body.linearVelocity.sqrMagnitude <= .25f : _speed <= .5f;

    private static bool HasGrip(CCMiniTool kind) =>
        kind == CCMiniTool.Clamp || kind == CCMiniTool.Grapple || kind == CCMiniTool.Forks;

    private static bool HasMotor(CCMiniTool kind) =>
        kind == CCMiniTool.Trencher || kind == CCMiniTool.Mulcher ||
        kind == CCMiniTool.Hammer || kind == CCMiniTool.Mixer;

    private bool CargoAvailable()
    {
        if (_cargoNetwork == null || !_cargoNetwork.IsSpawned || (_cargoNetwork.Owner != null && _cargoNetwork.Owner.IsValid)) return false;
        var food = _cargo.GetComponentInParent<FoodPiece>();
        return food == null || (!food.IsConsumed && !food.IsHeld);
    }

    private void ReleaseCargo()
    {
        if (_cargo != null && CargoAvailable())
        {
            _cargo.isKinematic = false;
            _cargo.WakeUp();
        }
        _cargo = null;
        _cargoNetwork = null;
        if (IsServerInitialized) _grip.Value = false;
    }

    private Rigidbody FindCargo(CCMiniExcavatorAttachment tool, out NetworkObject networkObject)
    {
        networkObject = null;
        if (tool.tip == null) return null;
        Vector3 point = tool.tip.position;
        int count = Scan(tool);
        float nearest = float.PositiveInfinity;
        Rigidbody result = null;
        for (int i = 0; i < count; i++)
        {
            var col = _contacts[i];
            Rigidbody body = col.attachedRigidbody;
            if (body == null || body.isKinematic || body.mass > maxCargoMass ||
                !body.TryGetComponent(out NetworkObject net) || !net.IsSpawned ||
                (net.Owner != null && net.Owner.IsValid) || body.transform.IsChildOf(transform)) continue;
            var food = body.GetComponentInParent<FoodPiece>();
            var pallet = body.GetComponentInParent<BreakablePallet>();
            bool canLiftFood = tool.kind != CCMiniTool.Forks && food != null && !food.IsHeld && !food.IsConsumed;
            if (!canLiftFood && pallet == null) continue;

            float distance = (col.ClosestPoint(point) - point).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance;
            result = body;
            networkObject = net;
        }
        return result;
    }

    private int Scan(CCMiniExcavatorAttachment tool) =>
        Physics.OverlapSphereNonAlloc(tool.tip.position, tool.reach, _contacts, toolMask,
            QueryTriggerInteraction.Collide);

    private void RunTool(CCMiniExcavatorAttachment tool, float dt)
    {
        if (tool.kind == CCMiniTool.Mixer)
        {
            Mix(tool.tip.position, dt);
            return;
        }

        if (tool.kind == CCMiniTool.Hammer && Time.time < _nextHit) return;
        int count = Scan(tool);
        Vector3 point = tool.tip.position;
        float nearest = float.PositiveInfinity;
        FoodPiece piece = null;
        IHammerTarget target = null;
        Food.FoodProcess process = Food.FoodProcess.None;

        for (int i = 0; i < count; i++)
        {
            Collider col = _contacts[i];
            if (col.transform.IsChildOf(transform)) continue;
            float distance = (col.ClosestPoint(point) - point).sqrMagnitude;
            if (distance >= nearest) continue;

            var hitTarget = tool.kind == CCMiniTool.Hammer ? col.GetComponentInParent<IHammerTarget>() : null;
            var hitFood = col.GetComponentInParent<FoodPiece>();
            Food.FoodProcess work = GetProcess(tool.kind, hitFood);
            if (hitTarget == null && work == Food.FoodProcess.None) continue;
            nearest = distance;
            target = hitTarget;
            piece = hitFood;
            process = work;
        }

        if (tool.kind == CCMiniTool.Hammer)
        {
            if (target == null && piece == null) return;
            _nextHit = Time.time + .26f;
            if (target != null) target.ServerHammerHit(point, -tool.transform.up);
            else piece.ServerAddWork(process, .26f);
        }
        else if (piece != null)
        {
            piece.ServerAddWork(process, dt);
        }
    }

    private static Food.FoodProcess GetProcess(CCMiniTool kind, FoodPiece piece)
    {
        if (piece == null || piece.IsHeld || piece.IsConsumed) return Food.FoodProcess.None;
        if (kind == CCMiniTool.Trencher && piece.CanApplyNow(Food.FoodProcess.Cut)) return Food.FoodProcess.Cut;
        if (kind == CCMiniTool.Hammer && piece.CanApplyNow(Food.FoodProcess.Pound)) return Food.FoodProcess.Pound;
        if (kind != CCMiniTool.Mulcher) return Food.FoodProcess.None;

        if (piece.CanApplyNow(Food.FoodProcess.Mince)) return Food.FoodProcess.Mince;
        if (piece.CanApplyNow(Food.FoodProcess.Mash)) return Food.FoodProcess.Mash;
        if (piece.CanApplyNow(Food.FoodProcess.Grate)) return Food.FoodProcess.Grate;
        return Food.FoodProcess.None;
    }

    private void Mix(Vector3 point, float dt)
    {
        Cookware nearest = null;
        float closest = .55f * .55f;
        foreach (var cookware in Cookware.All)
        {
            if (cookware == null || cookware == bucket || !cookware.IsServerInitialized) continue;
            if (cookware.IsInside(point)) { nearest = cookware; break; }
            Vector3 delta = cookware.MouthPoint - point;
            float horizontal = delta.x * delta.x + delta.z * delta.z;
            if (horizontal >= closest || delta.y < -.18f || delta.y > .6f) continue;
            nearest = cookware;
            closest = horizontal;
        }
        if (nearest == null) return;
        nearest.ServerAgitate(.85f);
        if (!Food.CookRecipeBook.ServerProcessAny(nearest, 0f, Food.CookStepKind.Whip, Food.CookStepKind.Mix))
            Food.ChocolateWork.ServerMix(nearest, dt);
    }
}
