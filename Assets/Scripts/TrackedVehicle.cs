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

    private float _move;
    private float _turn;

    protected override void VehicleFixedUpdate()
    {
        if (IsDriving)
        {
            _move = Input.GetAxisRaw("Vertical");
            _turn = Input.GetAxisRaw("Horizontal");
        }
        else { _move = 0f; _turn = 0f; }

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
}
