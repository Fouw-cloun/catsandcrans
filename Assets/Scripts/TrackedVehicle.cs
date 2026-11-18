using UnityEngine;

/// <summary>
/// Гусеничная техника на рейкаст-подвеске. Каждая гусеница — набор точек
/// (leftTrackPoints / rightTrackPoints), из которых вниз пускаются лучи: на них
/// держим корпус пружиной. Поворот — дифференциальной тягой (танковый принцип):
/// левая и правая гусеницы толкают с разной силой.
///
///   W/S — обе гусеницы вперёд/назад, A/D — поворот (разная тяга сторон).
///
/// Физика считается только на authority (см. NetworkVehicle).
/// </summary>
public class TrackedVehicle : NetworkVehicle
{
    [Header("Гусеницы — точки подвески")]
    public Transform[] leftTrackPoints;
    public Transform[] rightTrackPoints;

    [Header("Подвеска")]
    public float restLength = 0.6f;
    public float trackRadius = 0.3f;
    public float springStrength = 45000f;
    public float springDamper = 4000f;
    public LayerMask groundMask = ~0;

    [Header("Движение")]
    [Tooltip("Тяга вперёд/назад на гусеницу")]
    public float driveForce = 14000f;
    [Tooltip("Разница тяги сторон при повороте")]
    public float turnForce = 10000f;
    public float reverseFactor = 0.6f;
    [Tooltip("Боковое сцепление гусениц 0..1 (гусеницы держат вбок сильно)")]
    [Range(0f, 1f)] public float trackGrip = 0.85f;
    [Tooltip("Макс. замедление при отпущенных рычагах, м/с² (НЕ мгновенный стоп " +
             "— плавно гасит скорость с этим ускорением). 2-3 мягко, 6-8 резко.")]
    public float idleBrakeDeceleration = 4f;

    [Header("Vehicle Workshop")]
    public bool stableSuspension;
    [Min(.5f)] public float maxForwardSpeed = 5f, maxReverseSpeed = 2.5f;
    [Min(.1f)] public float tractionAcceleration = 2f;
    [Range(1, 5)] public float supportLimit = 2.5f;

    [Header("Track steering and throttle")]
    public bool heavyTrackDrive;
    [Min(.1f)] public float reverseAcceleration = 2.4f;
    [Min(.1f)] public float throttleRise = 3f;
    [Min(.1f)] public float pivotAcceleration = 4f;
    [Range(10, 90)] public float pivotSpeedDegrees = 42f;
    [Range(0, 1)] public float pivotScrub = .1f;
    [Range(0, 100)] public float pivotYawAssist = 45f;

    private readonly RaycastHit[] _groundHits = new RaycastHit[64];
    private float _throttleCommand;
    private float _steeringCommand;
    public int GroundContacts { get; private set; }

    protected virtual bool TryAutomatedInput(out float move,out float turn) { move=turn=0;return false; }
    private float _move;
    private float _turn;

    protected override void VehicleFixedUpdate()
    {
        if (IsDriving)
        {
            _move = Input.GetAxisRaw("Vertical");
            _turn = Input.GetAxisRaw("Horizontal");
        }
        else if(IsServerInitialized && CanDrive && TryAutomatedInput(out _move,out _turn)) { _move=Mathf.Clamp(_move,-1,1);_turn=Mathf.Clamp(_turn,-1,1); }
        else { _move = 0f; _turn = 0f; }

        if (stableSuspension)
        {
            StableFixedUpdate();
            return;
        }
        GroundContacts = 0;
        _throttleCommand = _steeringCommand = 0;

        // Дифференциал: поворот = разная тяга сторон.
        float leftIn = Mathf.Clamp(_move + _turn, -1f, 1f);
        float rightIn = Mathf.Clamp(_move - _turn, -1f, 1f);

        float dt = Time.fixedDeltaTime;
        int total = (leftTrackPoints?.Length ?? 0) + (rightTrackPoints?.Length ?? 0);
        if (total == 0) return;
        float tireMass = _body.mass / total;

        ApplyTrack(leftTrackPoints, leftIn, tireMass, dt);
        ApplyTrack(rightTrackPoints, rightIn, tireMass, dt);

        // Явный доворот вокруг вертикали — делает поворот гусениц крутым и
        // отзывчивым (в дополнение к дифференциалу тяги). Только когда есть
        // контакт с землёй хотя бы одной точкой.
        if (Mathf.Abs(_turn) > 0.01f && IsAnyTrackGrounded())
            _body.AddTorque(transform.up * _turn * turnForce, ForceMode.Force);
    }

    private bool IsAnyTrackGrounded()
    {
        Vector3 up = transform.up;
        float maxDist = restLength + trackRadius;
        if (leftTrackPoints != null)
            foreach (Transform p in leftTrackPoints)
                if (p != null && Physics.Raycast(p.position, -up, maxDist, groundMask)) return true;
        if (rightTrackPoints != null)
            foreach (Transform p in rightTrackPoints)
                if (p != null && Physics.Raycast(p.position, -up, maxDist, groundMask)) return true;
        return false;
    }

    private void ApplyTrack(Transform[] points, float input, float tireMass, float dt)
    {
        if (points == null) return;
        Vector3 up = transform.up;
        Vector3 fwd = transform.forward;
        Vector3 right = transform.right;
        float maxDist = restLength + trackRadius;

        foreach (Transform p in points)
        {
            if (p == null) continue;
            Vector3 pos = p.position;

            if (!Physics.Raycast(pos, -up, out RaycastHit hit, maxDist, groundMask))
                continue;

            Vector3 vel = _body.GetPointVelocity(pos);

            // 1. Подвеска.
            float compression = maxDist - hit.distance;
            float springVel = Vector3.Dot(up, vel);
            float susp = compression * springStrength - springVel * springDamper;
            if (susp > 0f) _body.AddForceAtPosition(up * susp, pos);

            // 2. Боковое сцепление гусеницы (тоже ограниченное — см. комментарий
            //    у торможения ниже, тот же принцип "не мгновенно, а с пределом").
            float latVel = Vector3.Dot(right, vel);
            float maxLatDeltaV = trackGrip * 20f * dt; // "20" — макс. боковое ускорение, м/с²
            float latDeltaV = Mathf.Clamp(-latVel, -maxLatDeltaV, maxLatDeltaV);
            _body.AddForceAtPosition(right * (latDeltaV / dt) * tireMass, pos);

            // 3. Тяга вдоль корпуса (или торможение при отпущенных рычагах).
            if (Mathf.Abs(input) > 0.01f)
            {
                float f = input > 0f ? driveForce : driveForce * reverseFactor;
                _body.AddForceAtPosition(fwd * input * f, pos);
            }
            else
            {
                // Плавное торможение: ограничиваем изменение скорости за этот
                // шаг величиной idleBrakeDeceleration*dt — НЕ мгновенный стоп,
                // а честное "тормозим с таким-то ускорением". Раньше формула
                // делила на dt без ограничения, что давало почти мгновенное
                // гашение скорости в нескольких точках сразу → машину дёргало
                // и подбрасывало ("вставала на дыбы") при отпускании хода.
                float fwdVel = Vector3.Dot(fwd, vel);
                float maxDeltaV = idleBrakeDeceleration * dt;
                float deltaV = Mathf.Clamp(-fwdVel, -maxDeltaV, maxDeltaV);
                _body.AddForceAtPosition(fwd * (deltaV / dt) * tireMass, pos);
            }
        }
    }

    private void StableFixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        bool brake = _isLocalDriver && !UiModal.AnyOpen && Input.GetKey(KeyCode.Space);
        float move = brake ? 0 : _move;
        float turn = brake ? 0 : _turn;
        if (brake || (Mathf.Abs(move) < .01f && Mathf.Abs(turn) < .01f))
        {
            _throttleCommand = 0;
            _steeringCommand = 0;
        }
        else
        {
            _throttleCommand = Mathf.MoveTowards(_throttleCommand, move, throttleRise * dt);
            _steeringCommand = Mathf.MoveTowards(_steeringCommand, turn, 5f * dt);
        }

        int total = CountPoints(leftTrackPoints) + CountPoints(rightTrackPoints);
        GroundContacts = 0;
        if (total == 0) return;
        float pointMass = _body.mass / total;
        if (!heavyTrackDrive)
        {
            ApplyStableTrack(leftTrackPoints, Mathf.Clamp(move + turn, -1, 1), 0, false, pointMass, brake, dt);
            ApplyStableTrack(rightTrackPoints, Mathf.Clamp(move - turn, -1, 1), 0, false, pointMass, brake, dt);
            return;
        }

        bool pivot = Mathf.Abs(move) < .01f && Mathf.Abs(turn) > .01f;
        float throttle = pivot ? 0 : _throttleCommand;
        float travelSpeed = throttle * (throttle < 0 ? maxReverseSpeed : maxForwardSpeed);
        float trackHalfGauge = TrackHalfGauge();
        float requestedYaw = _steeringCommand * pivotSpeedDegrees * Mathf.Deg2Rad * (pivot ? 1f : .7f);
        float differential = requestedYaw * trackHalfGauge;
        int left = ApplyStableTrack(leftTrackPoints, travelSpeed + differential, requestedYaw, pivot, pointMass, brake, dt);
        int right = ApplyStableTrack(rightTrackPoints, travelSpeed - differential, requestedYaw, pivot, pointMass, brake, dt);
        if (pivot && left > 0 && right > 0)
        {
            Vector3 up = transform.up;
            float actualYaw = Vector3.Dot(_body.angularVelocity, up);
            float maxAssist = pivotYawAssist * Mathf.Deg2Rad;
            float acceleration = Mathf.Clamp((requestedYaw - actualYaw) * 4f, -maxAssist, maxAssist);
            float groundedFraction = (left + right) / (float)total;
            _body.AddTorque(up * (acceleration * groundedFraction), ForceMode.Acceleration);
        }
    }

    private float TrackHalfGauge()
    {
        return Mathf.Max(.2f, Mathf.Abs(AverageTrackX(rightTrackPoints) - AverageTrackX(leftTrackPoints)) * .5f);
    }

    private float AverageTrackX(Transform[] points)
    {
        if (points == null) return 0;
        float sum = 0;
        int count = 0;
        foreach (var point in points)
        {
            if (point == null) continue;
            sum += Vector3.Dot(point.position - transform.position, transform.right);
            count++;
        }
        return count > 0 ? sum / count : 0;
    }

    private static int CountPoints(Transform[] points)
    {
        if (points == null) return 0;
        int count = 0;
        foreach (var point in points)
            if (point != null) count++;
        return count;
    }

    private int ApplyStableTrack(Transform[] points, float input, float requestedYaw, bool pivot, float pointMass, bool brake, float dt)
    {
        if (points == null) return 0;
        int contacts = 0;
        Vector3 up = transform.up;
        float rayLength = restLength + trackRadius;
        foreach (Transform point in points)
        {
            if (point == null) continue;
            Vector3 position = point.position;
            if (!CCVehicleGround.Cast(transform, _body, position, -up, rayLength, groundMask, _groundHits, out RaycastHit hit))
                continue;

            contacts++;
            GroundContacts++;
            Vector3 velocity = _body.GetPointVelocity(position);
            if (hit.rigidbody != null) velocity -= hit.rigidbody.GetPointVelocity(hit.point);
            float support = (rayLength - hit.distance) * springStrength - Vector3.Dot(velocity, up) * springDamper;
            float supportCap = pointMass * Physics.gravity.magnitude * supportLimit;
            _body.AddForceAtPosition(up * Mathf.Clamp(support, 0, supportCap), position);

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized;
            Vector3 right = Vector3.Cross(hit.normal, forward).normalized;
            float sideSpeed = Vector3.Dot(velocity, right);
            float sideLimit = trackGrip * 20f;
            if (heavyTrackDrive && Mathf.Abs(requestedYaw) > .001f)
            {
                // Steering deliberately scrubs the tracks sideways at the front and rear.
                Vector3 yawVelocity = Vector3.Cross(up * requestedYaw, position - _body.worldCenterOfMass);
                sideSpeed -= Vector3.Dot(yawVelocity, right);
                sideLimit *= pivot ? pivotScrub : .45f;
            }
            else if (!heavyTrackDrive && Mathf.Abs(_turn) > .01f)
                sideLimit *= .25f;
            _body.AddForceAtPosition(right * (Mathf.Clamp(-sideSpeed / dt, -sideLimit, sideLimit) * pointMass), position);

            float speed = Vector3.Dot(velocity, forward);
            float acceleration;
            if (heavyTrackDrive)
            {
                float limit = brake ? 8f : Mathf.Abs(input) < .01f ? idleBrakeDeceleration :
                    pivot ? pivotAcceleration : _throttleCommand < 0 ? reverseAcceleration : tractionAcceleration;
                acceleration = Mathf.Clamp((input - speed) * 4f, -limit, limit);
            }
            else if (Mathf.Abs(input) > .01f)
            {
                float cap = input > 0 ? maxForwardSpeed : maxReverseSpeed;
                acceleration = input * tractionAcceleration *
                    Mathf.Clamp01((cap - Mathf.Sign(input) * speed) / Mathf.Max(.5f, cap * .3f));
            }
            else
            {
                float limit = brake ? 8f : idleBrakeDeceleration;
                acceleration = Mathf.Clamp(-speed / dt, -limit, limit);
            }
            _body.AddForceAtPosition(forward * (acceleration * pointMass), position);
        }
        return contacts;
    }
}
