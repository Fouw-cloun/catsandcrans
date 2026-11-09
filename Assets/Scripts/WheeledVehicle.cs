using UnityEngine;

/// <summary>
/// Колёсная техника на рейкаст-подвеске (без WheelCollider — предсказуемо и
/// сетевобезопасно). Схема под наши машины: обычно 6 колёс, 2 передних
/// поворотных + 4 задних ведущих. Каждое колесо — это точка (anchor) на
/// корпусе, из которой вниз пускается луч; на пружине держим корпус, гасим
/// боковое скольжение (сцепление шины) и толкаем вперёд (мотор).
///
///   W/S — газ вперёд/назад, A/D — поворот передних колёс.
///
/// Физика считается только на authority (см. NetworkVehicle).
/// </summary>
public class WheeledVehicle : NetworkVehicle
{
    [System.Serializable]
    public class Wheel
    {
        [Tooltip("Точка крепления на корпусе (верх хода подвески) — отсюда луч вниз")]
        public Transform anchor;
        [Tooltip("Меш колеса для визуала (опционально)")]
        public Transform visual;
        [Tooltip("Поворотное колесо (передние)")]
        public bool steering;
        [Tooltip("Ведущее колесо (крутит мотор)")]
        public bool motorized = true;

        [Header("Подгонка визуала под конкретный меш")]
        [Tooltip("Локальная ось качения ЭТОГО меша (после базового поворота). " +
                 "Если колесо крутится не туда при движении — поменяй ось: " +
                 "обычно (1,0,0) или (0,0,1) в зависимости от импорта модели.")]
        public Vector3 spinAxisLocal = Vector3.right;
        [Tooltip("Ручной сдвиг визуала (в системе координат корпуса), если у " +
                 "меша пивот не в геометрическом центре и колесо 'слетает' с " +
                 "оси / парит рядом с корпусом. Подбирай на глаз, обычно " +
                 "небольшие значения по одной оси.")]
        public Vector3 visualOffset = Vector3.zero;

        [HideInInspector] public float steerAngle;
        [HideInInspector] public float spin;      // накопленный угол качения (визуал)
        [HideInInspector] public bool grounded;
        [HideInInspector] public float lastCompression;

        // Базовый (импортный) локальный поворот меша колеса — захватывается
        // один раз при первом кадре, дальше крутим ОТНОСИТЕЛЬНО него.
        [HideInInspector] public Quaternion baseLocalRotation;
        [HideInInspector] public bool baseRotationCaptured;
    }

    [Header("Колёса")]
    public Wheel[] wheels;

    [Header("Подвеска")]
    [Tooltip("Длина хода подвески (насколько далеко достаёт колесо)")]
    public float restLength = 0.5f;
    public float wheelRadius = 0.4f;
    [Tooltip("Жёсткость пружины (выше = жёстче держит корпус)")]
    public float springStrength = 30000f;
    [Tooltip("Демпфер (гасит раскачку; выше = меньше прыгает)")]
    public float springDamper = 4000f;
    public LayerMask groundMask = ~0;

    [Header("Движение")]
    public float motorForce = 9000f;
    public float reverseFactor = 0.5f;   // задний ход слабее
    public float maxSteerAngle = 28f;
    public float steerSpeed = 6f;
    [Tooltip("Боковое сцепление шины 0..1 (1 = не скользит вбок совсем)")]
    [Range(0f, 1f)] public float tireGrip = 0.7f;
    [Tooltip("Макс. замедление двигателем при отпущенном газе, м/с² (НЕ мгновенный " +
             "стоп — плавно гасит скорость с этим ускорением). 2-3 мягко, 6-8 резко.")]
    public float engineBrakeDeceleration = 3f;

    [Header("Визуал колёс")]
    public float visualSpinSpeed = 1f;

    private float _throttle;
    private float _steer;

    /// <summary>Активна ли физика колёс (подвеска+тяга). База — да. Автокран
    /// переопределяет: при опущенных опорах колёса замирают/поднимаются,
    /// чтобы не растягивались и не отрывались, пока корпус стоит на опорах.</summary>
    protected virtual bool WheelPhysicsActive => true;

    /// <summary>Луч подвески вниз. База — обычный Raycast по groundMask. Каток переопределяет:
    /// не забирается на тесто, а едет по земле (по тонкому слою) и толкает тесто перед собой.</summary>
    protected virtual bool WheelGroundCast(Vector3 pos, Vector3 dir, float maxDist, out RaycastHit hit)
        => Physics.Raycast(pos, dir, out hit, maxDist, groundMask);

    protected override void VehicleFixedUpdate()
    {
        // Опоры выдвинуты (у крана) — физику колёс не считаем: корпус держат
        // опоры. НО визуал колёс всё равно двигаем вверх вместе с корпусом
        // (в верхнюю точку подвески), иначе колёса застынут на месте, а
        // поднявшийся корпус "оторвётся" от них.
        if (!WheelPhysicsActive)
        {
            _throttle = 0f;
            _steer = Mathf.MoveTowards(_steer, 0f, steerSpeed * Time.fixedDeltaTime);
            ParkWheelsToBody();
            return;
        }

        // Ввод — только если реально ведём машину; иначе просто подвеска (стоим).
        if (IsDriving)
        {
            _throttle = Input.GetAxisRaw("Vertical");
            float steerTarget = Input.GetAxisRaw("Horizontal");
            _steer = Mathf.MoveTowards(_steer, steerTarget, steerSpeed * Time.fixedDeltaTime);
        }
        else
        {
            _throttle = 0f;
            _steer = Mathf.MoveTowards(_steer, 0f, steerSpeed * Time.fixedDeltaTime);
        }

        if (wheels == null) return;

        float dt = Time.fixedDeltaTime;
        float tireMass = _body.mass / Mathf.Max(1, wheels.Length);
        Vector3 up = transform.up;

        foreach (Wheel w in wheels)
        {
            if (w.anchor == null) continue;

            // Угол поворота (плавно) — только у поворотных колёс.
            float targetAngle = w.steering ? _steer * maxSteerAngle : 0f;
            w.steerAngle = Mathf.Lerp(w.steerAngle, targetAngle, steerSpeed * dt);

            Quaternion steerRot = Quaternion.AngleAxis(w.steerAngle, up);
            Vector3 fwd = steerRot * transform.forward;
            Vector3 right = steerRot * transform.right;
            Vector3 pos = w.anchor.position;

            float maxDist = restLength + wheelRadius;
            if (WheelGroundCast(pos, -up, maxDist, out RaycastHit hit))
            {
                w.grounded = true;
                Vector3 tireVel = _body.GetPointVelocity(pos);

                // 1. Подвеска (пружина − демпфер).
                float compression = maxDist - hit.distance;
                float springVel = Vector3.Dot(up, tireVel);
                float suspForce = compression * springStrength - springVel * springDamper;
                if (suspForce > 0f)
                    _body.AddForceAtPosition(up * suspForce, pos);
                w.lastCompression = compression;

                // 2. Боковое сцепление (гасим скольжение вбок, ограниченно).
                float latVel = Vector3.Dot(right, tireVel);
                float maxLatDeltaV = tireGrip * 20f * dt; // "20" — макс. боковое ускорение, м/с²
                float latDeltaV = Mathf.Clamp(-latVel, -maxLatDeltaV, maxLatDeltaV);
                _body.AddForceAtPosition(right * (latDeltaV / dt) * tireMass, pos);

                // 3. Тяга / торможение двигателем вдоль направления колеса.
                if (w.motorized)
                {
                    if (Mathf.Abs(_throttle) > 0.01f)
                    {
                        float f = _throttle > 0f ? motorForce : motorForce * reverseFactor;
                        _body.AddForceAtPosition(fwd * _throttle * f, pos);
                    }
                    else
                    {
                        // Плавное торможение двигателем: ограничено, не мгновенный
                        // стоп (см. комментарий в TrackedVehicle — та же логика).
                        float fwdVel = Vector3.Dot(fwd, tireVel);
                        float maxDeltaV = engineBrakeDeceleration * dt;
                        float deltaV = Mathf.Clamp(-fwdVel, -maxDeltaV, maxDeltaV);
                        _body.AddForceAtPosition(fwd * (deltaV / dt) * tireMass, pos);
                    }
                }

                // Визуал: колесо на поверхности (+ ручная компенсация пивота).
                if (w.visual != null)
                    w.visual.position = hit.point + up * wheelRadius + transform.TransformDirection(w.visualOffset);
            }
            else
            {
                w.grounded = false;
                // Визуал: колесо в нижней точке хода (+ та же компенсация).
                if (w.visual != null)
                    w.visual.position = pos - up * restLength + transform.TransformDirection(w.visualOffset);
            }
        }
    }

    /// <summary>Прижимает визуал колёс к якорю (верхняя точка подвески) —
    /// используется, когда физика колёс выключена (кран на опорах): колёса
    /// поднимаются вместе с корпусом, а не остаются на земле.</summary>
    private void ParkWheelsToBody()
    {
        if (wheels == null) return;
        Vector3 up = transform.up;
        foreach (Wheel w in wheels)
        {
            if (w.anchor == null || w.visual == null) continue;
            // Ставим колесо чуть ниже якоря (как в поджатой подвеске), но
            // жёстко относительно корпуса — значит едет вместе с ним вверх.
            w.visual.position = w.anchor.position - up * (wheelRadius * 0.5f)
                + transform.TransformDirection(w.visualOffset);
        }
    }

    // Визуал вращения/поворота колёс — у всех клиентов (косметика).
    protected override void ApplyVisuals()
    {
        if (wheels == null) return;
        float speed = _body != null ? Vector3.Dot(_body.linearVelocity, transform.forward) : 0f;

        foreach (Wheel w in wheels)
        {
            if (w.visual == null) continue;

            // Захватываем базовый (импортный) поворот меша один раз — это твой
            // ручной оффсет из редактора (например Rotation 0,-90,90 на колесе).
            // ВАЖНО: используем LOCAL rotation (относительно родителя, обычно
            // это Anchor или корпус машины), а не мировой — раньше код считал
            // мировой поворот с нуля каждый кадр (transform.rotation * steer *
            // spin), что стирало этот оффсет и "выворачивало" колесо.
            if (!w.baseRotationCaptured)
            {
                w.baseLocalRotation = w.visual.localRotation;
                w.baseRotationCaptured = true;
            }

            w.spin += speed * visualSpinSpeed * Time.deltaTime * 90f;

            // Поворот руля — в системе координат родителя (вокруг локального
            // "вверх"), качение — относительно базового оффсета меша, вокруг
            // НАСТРАИВАЕМОЙ оси (spinAxisLocal) — у разных мешей после импорта
            // "своя" ось качения, не всегда X.
            Quaternion steerYaw = Quaternion.AngleAxis(w.steerAngle, Vector3.up);
            Quaternion spinRot = Quaternion.AngleAxis(w.spin, w.spinAxisLocal.normalized);
            w.visual.localRotation = steerYaw * w.baseLocalRotation * spinRot;
        }
    }
}
