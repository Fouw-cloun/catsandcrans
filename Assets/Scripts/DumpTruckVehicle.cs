using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

public class DumpTruckVehicle : WheeledVehicle
{
    [SerializeField] private Transform _bed;
    [SerializeField] private float _tiltSpeed = 12;
    [SerializeField] private float _visualSmoothSpeed = 90;
    [SerializeField] private Vector2 _bedLimit = new Vector2(0, 55);
    public Vector3 bedAxis = Vector3.right;
    public Transform tailgate;
    public Vector3 gateAxis = Vector3.right;
    public float gateOpenAngle = 100;
    public float gateSpeed = 120;
    public float gateOpenFrom = 12;
    public bool automaticGate = true;
    [Header("Борт на ходу")]
    [Range(0, 2)] public float gateRattleStrength = 1.0f;

    [Header("Поворот задних колёс")]
    public Transform[] rearWheelVisuals;
    private Quaternion[] _rearBind;
    private Vector3[] _previousWheelPoints;
    private float[] _rearSpin;
    private Wheel[] _rearWheels;
    private Vector3 _previousPosition;
    private bool _travelReady;
    private float _roadSpeed, _roadVelocity, _roadAcceleration, _rattlePhase;

    private readonly SyncVar<float> _bedAngle = new(0);
    private Quaternion _bedRest, _gateRest;
    private float _localBedAngle, _visualBed, _visualGate, _gateVelocity;
    private bool _captured;

    public Transform Bed => _bed;
    public bool DischargeOpen => tailgate != null && _visualBed >= gateOpenFrom && _visualGate > 8;

    protected override void ApplyDriverState(NetworkObject player, bool entering)
    {
        if (entering) _localBedAngle = _bedAngle.Value;
        base.ApplyDriverState(player, entering);
    }

    protected override void DriverUpdate()
    {
        float direction = (Input.GetKey(KeyCode.Q) ? 1 : 0) - (Input.GetKey(KeyCode.E) ? 1 : 0);
        _localBedAngle = Mathf.Clamp(_localBedAngle + direction * _tiltSpeed * Mathf.Min(.05f, Time.deltaTime),
            _bedLimit.x, _bedLimit.y);
    }

    protected override void DriverTick() => SetBedAngle(_localBedAngle);

    [ServerRpc]
    private void SetBedAngle(float angle)
    {
        if (!HasDriver || float.IsNaN(angle) || float.IsInfinity(angle)) return;
        _bedAngle.Value = Mathf.Clamp(angle, _bedLimit.x, _bedLimit.y);
    }

    protected override void ApplyVisuals()
    {
        UpdateRoadSpeed();
        PrepareRearWheels();
        base.ApplyVisuals();
        RollRearWheels();
        if (_bed == null) return;
        if (!_captured)
        {
            _bedRest = _bed.localRotation;
            if (tailgate != null) _gateRest = tailgate.localRotation;
            _visualBed = _localBedAngle = _bedAngle.Value;
            _captured = true;
        }
        _visualBed = _isLocalDriver ? _localBedAngle :
            Mathf.MoveTowards(_visualBed, _bedAngle.Value, _visualSmoothSpeed * Time.deltaTime);
        _bed.localRotation = _bedRest * Quaternion.AngleAxis(_visualBed, bedAxis);

        if (tailgate == null) return;
        UpdateTailgate(Mathf.Min(.05f, Time.deltaTime));
    }

    private void UpdateTailgate(float dt)
    {
        float goal = 0;
        float limit = Mathf.Abs(gateOpenAngle);
        bool released = automaticGate && _visualBed > .08f;
        if (released)
        {
            var parent = tailgate.parent;
            Vector3 worldAxis = _gateRest * gateAxis;
            Vector3 closedDown = _gateRest * Vector3.down;
            if (parent != null)
            {
                worldAxis = parent.TransformDirection(worldAxis);
                closedDown = parent.TransformDirection(closedDown);
            }

            // A hanging gate tends away from acceleration and back with braking.
            float inertia = Mathf.Clamp(_roadAcceleration * .48f * gateRattleStrength, -2.1f, 2.1f);
            Vector3 effectiveGravity = Physics.gravity - transform.forward * inertia;
            Vector3 down = Vector3.ProjectOnPlane(effectiveGravity, worldAxis);
            if (down.sqrMagnitude > .001f)
                goal = Mathf.Clamp(Vector3.SignedAngle(closedDown, down, worldAxis), 0, limit);

            float motion = Mathf.InverseLerp(.15f, 2.3f, _roadSpeed);
            _rattlePhase = Mathf.Repeat(_rattlePhase + dt * (.50f + Mathf.Min(6, _roadSpeed) * .12f) *
                Mathf.PI * 2f, Mathf.PI * 2f);
            goal += Mathf.Sin(_rattlePhase) * 5.5f * motion * gateRattleStrength;
        }

        // Minor over-travel at the target gives one soft impact when the gate
        // returns, without a strong reverse impulse or a forced slam.
        goal = Mathf.Clamp(goal, -2f, limit);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 120f));
        float step = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            _gateVelocity += ((goal - _visualGate) * 27f - _gateVelocity * 7.6f) * step;
            float closingLimit = Mathf.Lerp(13f, 78f, Mathf.InverseLerp(0, 13f, _visualGate));
            float openingLimit = Mathf.Clamp(gateSpeed, 25f, 86f);
            _gateVelocity = Mathf.Clamp(_gateVelocity, -closingLimit, openingLimit);
            float next = _visualGate + _gateVelocity * step;
            if (next <= 0 && _gateVelocity < 0)
            {
                float rebound = released ? -_gateVelocity * .09f : 0;
                _visualGate = 0;
                _gateVelocity = rebound >= .45f ? rebound : 0;
            }
            else if (next >= limit && _gateVelocity > 0)
            {
                _visualGate = limit;
                _gateVelocity = 0;
            }
            else
            {
                _visualGate = Mathf.Clamp(next, 0, limit);
            }
        }
        if (!released && _visualGate < .10f && Mathf.Abs(_gateVelocity) < 1f)
            _visualGate = _gateVelocity = 0;
        tailgate.localRotation = _gateRest * Quaternion.AngleAxis(_visualGate, gateAxis);
    }

    private void UpdateRoadSpeed()
    {
        Vector3 position = transform.position;
        if (!_travelReady)
        {
            _previousPosition = position;
            _travelReady = true;
            return;
        }
        Vector3 delta = Vector3.ProjectOnPlane(position - _previousPosition, transform.up);
        _previousPosition = position;
        float dt = Time.deltaTime;
        if (dt < .0001f) return;
        if (delta.magnitude > .8f + dt * 15f)
        {
            _roadSpeed = _roadVelocity = _roadAcceleration = 0;
            return;
        }
        float oldVelocity = _roadVelocity;
        float forwardSpeed = Vector3.Dot(delta, transform.forward) / dt;
        _roadSpeed = Mathf.Lerp(_roadSpeed, delta.magnitude / dt, 1 - Mathf.Exp(-10f * dt));
        _roadVelocity = Mathf.Lerp(oldVelocity, forwardSpeed, 1 - Mathf.Exp(-9f * dt));
        float acceleration = Mathf.Clamp((_roadVelocity - oldVelocity) / dt, -9f, 9f);
        _roadAcceleration = Mathf.Lerp(_roadAcceleration, acceleration, 1 - Mathf.Exp(-6f * dt));
    }

    private void PrepareRearWheels()
    {
        if (_rearWheels != null || rearWheelVisuals == null || rearWheelVisuals.Length == 0 || wheels == null) return;
        int n = rearWheelVisuals.Length;
        _rearWheels = new Wheel[n];
        _rearBind = new Quaternion[n];
        _previousWheelPoints = new Vector3[n];
        _rearSpin = new float[n];
        for (int i = 0; i < n; i++)
        {
            var visual = rearWheelVisuals[i];
            if (visual == null) continue;
            foreach (var w in wheels)
            {
                if (w == null || w.visual != visual || w.anchor == null) continue;
                _rearWheels[i] = w;
                _rearBind[i] = Quaternion.Inverse(transform.rotation) * visual.rotation;
                _previousWheelPoints[i] = w.anchor.position;
                break;
            }
        }
    }

    private void RollRearWheels()
    {
        if (_rearWheels == null) return;
        for (int i = 0; i < _rearWheels.Length; i++)
        {
            var w = _rearWheels[i];
            if (w == null || w.anchor == null || w.visual == null) continue;
            Vector3 current = w.anchor.position;
            Vector3 delta = current - _previousWheelPoints[i];
            _previousWheelPoints[i] = current;
            if (delta.magnitude < .8f + Time.deltaTime * 15f)
            {
                float distance = Vector3.Dot(delta, transform.forward);
                _rearSpin[i] = Mathf.Repeat(_rearSpin[i] + distance / Mathf.Max(.1f, wheelRadius) * Mathf.Rad2Deg, 360f);
            }
            Vector3 neutral = w.anchor.position - transform.up * (restLength - .14f);
            float travel = Mathf.Clamp(Vector3.Dot(w.visual.position - neutral, transform.up), -.18f, .19f);
            w.visual.SetPositionAndRotation(neutral + transform.up * travel,
                transform.rotation * Quaternion.AngleAxis(_rearSpin[i], Vector3.right) * _rearBind[i]);
        }
    }
}
