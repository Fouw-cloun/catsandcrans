using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// БРАНДСПОЙТ (ствол) — предмет в руке, второй конец шланга.
///
///   ЛКМ — ПЕРЕКЛЮЧАТЕЛЬ струи (нажал — льёт, нажал ещё раз — выключил).
///         Держать не нужно.
///   Напор берётся с ВЕРХНЕЙ гайки гидранта, к которому подключено кольцо.
///
/// РЕАКТИВНАЯ ТЯГА: струя толкает того, кто её держит, В ОБРАТНУЮ сторону.
///   Льёшь под себя — ВЗЛЕТАЕШЬ (реактивный ранец), но шланг держит: дальше
///   _maxHoseLength не пускает НИ ПО ГОРИЗОНТАЛИ, НИ ПО ВЫСОТЕ.
///
/// БРОШЕННЫЙ ВКЛЮЧЁННЫМ: ствол не выключается, а начинает МЕТАТЬСЯ как змея —
///   реактивная тяга кидает его, случайный момент вращает, струя сбивает всё.
///
/// СЕТЬ: физика ствола и тяга — на сервере; ограничение длины считает владелец.
/// </summary>
public class NozzleGunItem : InteractableItem
{
    [Header("Положение в руке")]
    [SerializeField] private Vector3 _holdPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    [Header("Струя (на ДОЧЕРНЕМ объекте, не на стволе!)")]
    [Tooltip("WaterJet на дочернем JetOrigin (forward = куда бьёт ствол)")]
    [SerializeField] private WaterJet _jet;

    [Header("Связь со шлангом")]
    [SerializeField] private HoseRingItem _ring;
    [Tooltip("Точка крепления шланга на брандспойте")]
    [SerializeField] private Transform _hoseAttach;

    [Header("Ограничение длины шланга")]
    [Tooltip("Максимальная длина шланга, м — дальше не пускает (и вверх тоже)")]
    [SerializeField] private float _maxHoseLength = 6f;

    [Header("Реактивная тяга ДЕРЖАТЕЛЮ (полёт на струе)")]
    [Tooltip("ВЫКЛ по умолчанию: в руках отдачи нет, ствол пляшет только когда " +
             "брошен. Включи, если захочешь вернуть полёт на струе.\n\n" +
             "Поле специально переименовано — старое значение из префаба не " +
             "переносится, поэтому отдача выключится сама.")]
    [SerializeField] private bool _thrustWhenHeld = false;
    [Tooltip("Сила отдачи на игрока при полном напоре")]
    [SerializeField] private float _thrustForce = 16f;
    [Tooltip("Напор, ниже которого отдачи нет")]
    [Range(0f, 1f)] [SerializeField] private float _thrustMinFlow = 0.35f;

    [Header("Брошенный включённым (пляска шланга)")]
    [Tooltip("ВКЛ — брошенный включённый ствол мечется как змея")]
    [SerializeField] private bool _wildWhenDropped = true;
    [Tooltip("Сила реактивного метания ствола")]
    [SerializeField] private float _wildThrust = 9f;
    [Tooltip("Хаотичность вращения")]
    [SerializeField] private float _wildTorque = 12f;
    [Tooltip("Держать ствол на этой высоте над землёй (эффект пляшущего шланга)")]
    [SerializeField] private float _wildHoverHeight = 0.8f;
    [Tooltip("Сила удержания высоты (больше = бодрее парит, не вязнет)")]
    [SerializeField] private float _wildHoverForce = 20f;
    [Tooltip("Максимальная скорость метания")]
    [SerializeField] private float _wildMaxSpeed = 9f;
    [Tooltip("Скорость, ниже которой считаем что ствол ЗАСТРЯЛ")]
    [SerializeField] private float _stuckSpeed = 0.6f;
    [Tooltip("Сколько секунд в застревании до рывка")]
    [SerializeField] private float _stuckTime = 0.35f;
    [Tooltip("Сила рывка при застревании")]
    [SerializeField] private float _unstickImpulse = 5f;
    [SerializeField] private LayerMask _groundMask = ~0;

    /// <summary>Включена ли струя (переключается ЛКМ).</summary>
    private readonly SyncVar<bool> _firing = new(false);

    private PlayerHand _holdingHand;
    private FirstPersonController _holderFpc;
    private float _stuckTimer;

    public bool IsConnected => _ring != null && _ring.IsAttached;
    public bool IsHeld => _holdingHand != null;
    public bool IsFiring => _firing.Value;
    public Rigidbody Body => _rb;
    public HoseRingItem Ring => _ring;
    public Transform HoseAttach => _hoseAttach != null ? _hoseAttach : transform;

    /// <summary>Напор: верхняя гайка гидранта, если подключено; иначе 0.</summary>
    public float SuppliedFlow
    {
        get
        {
            if (!IsConnected) return 0f;
            HydrantNut outlet = _ring.AttachedOutlet;
            HydrantNut top = outlet != null ? outlet.TopValve : null;
            return top != null ? top.Open01 : 0f;
        }
    }

    /// <summary>Куда бьёт струя (мировое направление).</summary>
    private Vector3 JetDir => _jet != null ? _jet.transform.forward : transform.forward;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
        if (_jet == null) _jet = GetComponentInChildren<WaterJet>(true);
    }

    public void ServerSetRing(HoseRingItem ring) => _ring = ring;

    public override void ServerOnPickup(PlayerHand hand)
    {
        _holdingHand = hand;
        base.ServerOnPickup(hand);
    }

    public override void ServerOnDrop(PlayerHand hand, Vector3 dir, float force = 0f)
    {
        _holdingHand = null;
        _holderFpc = null;
        // ВАЖНО: НЕ выключаем струю — брошенный включённым ствол должен плясать.
        base.ServerOnDrop(hand, dir, force);

        // Готовим физику к пляске: непрерывные коллизии (иначе на скорости
        // проваливается сквозь пол), сон запрещён, стартовый подброс.
        if (_rb != null && !_rb.isKinematic)
        {
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.sleepThreshold = 0f;
            _rb.WakeUp();
            if (_firing.Value)
                _rb.AddForce(Vector3.up * 2.5f, ForceMode.VelocityChange);
        }
        _stuckTimer = 0f;
    }

    // ───────── ЛКМ: ПЕРЕКЛЮЧАТЕЛЬ ─────────

    /// <summary>ЛКМ переключает струю (одно нажатие вкл, ещё одно — выкл).</summary>
    public override void ServerPrimaryUse(UseContext ctx)
    {
        _firing.Value = !_firing.Value;
    }

    private void Update()
    {
        // Струя: льёт когда включена И подключено кольцо И есть напор.
        if (_jet != null)
            _jet.SetExternalFlow(_firing.Value && IsConnected, SuppliedFlow);

        // Владелец: ограничение длины шланга (в 3D — и вверх тоже).
        if (base.IsOwner && IsHeld) EnforceHoseLength();
    }

    private void FixedUpdate()
    {
        if (!base.IsServerInitialized) return;

        float flow = _firing.Value && IsConnected ? SuppliedFlow : 0f;
        if (flow <= 0.02f) return;

        // В руках — ничего: ни отдачи, ни пляски. Пляшет только брошенный.
        if (IsHeld)
        {
            if (_thrustWhenHeld) ServerThrustHolder(flow);
            return;
        }

        if (_wildWhenDropped) ServerWildDance(flow);
    }

    /// <summary>РЕАКТИВНАЯ ТЯГА держателю: толкает В ОБРАТНУЮ от струи сторону.
    /// Льёшь вниз — летишь вверх.</summary>
    private void ServerThrustHolder(float flow)
    {
        if (!_thrustWhenHeld || flow < _thrustMinFlow) return;
        if (_holdingHand == null) return;

        FirstPersonController fpc = _holdingHand.GetComponentInParent<FirstPersonController>();
        if (fpc == null) return;

        Vector3 thrust = -JetDir * (_thrustForce * flow);
        fpc.ServerApplyThrust(thrust);
    }

    /// <summary>ПЛЯСКА брошенного ствола: летает и вертится во все стороны.
    /// Ключевое — НЕ дать вжаться в землю: тяга всегда с наклоном ВВЕРХ, есть
    /// принудительное парение и антизастревание (рывок вверх, если завис).</summary>
    private void ServerWildDance(float flow)
    {
        if (_rb == null || _rb.isKinematic) return;

        // Гасим сон и трение о землю, иначе ствол «прилипает».
        if (_rb.IsSleeping()) _rb.WakeUp();

        // ── Высота над землёй ──
        float height = 99f;
        Vector3 groundNormal = Vector3.up;
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down,
                            out RaycastHit h, 20f, _groundMask, QueryTriggerInteraction.Ignore))
        {
            height = transform.position.y - h.point.y;
            groundNormal = h.normal;
        }

        // ── ПАРЕНИЕ: держим на _wildHoverHeight, сила растёт чем ниже ствол.
        //    Это главное лекарство от «застрял в земле».
        if (height < _wildHoverHeight)
        {
            float deficit = Mathf.Clamp01(1f - height / Mathf.Max(0.05f, _wildHoverHeight));
            _rb.AddForce(groundNormal * (_wildHoverForce * (1f + deficit * 2f)),
                         ForceMode.Acceleration);

            // Вниз не даём набирать скорость у земли.
            Vector3 v = _rb.linearVelocity;
            if (v.y < 0f) { v.y *= 0.2f; _rb.linearVelocity = v; }
        }

        // ── РЕАКТИВНАЯ ТЯГА с наклоном ВВЕРХ: если струя бьёт в пол, ствол не
        //    вкапывается, а подпрыгивает и уносится в сторону.
        Vector3 thrust = -JetDir;
        thrust.y = Mathf.Max(thrust.y, 0.25f);   // всегда есть подъёмная доля
        thrust.Normalize();
        _rb.AddForce(thrust * (_wildThrust * flow), ForceMode.Acceleration);

        // ── Хаотичное вращение — струя водит по сторонам.
        _rb.AddTorque(Random.insideUnitSphere * (_wildTorque * flow), ForceMode.Acceleration);

        // ── АНТИЗАСТРЕВАНИЕ: если ствол почти не двигается (упёрся/зажат) —
        //    выкидываем его рывком вверх-вбок.
        if (_rb.linearVelocity.sqrMagnitude < _stuckSpeed * _stuckSpeed)
        {
            _stuckTimer += Time.fixedDeltaTime;
            if (_stuckTimer > _stuckTime)
            {
                _stuckTimer = 0f;
                Vector3 kick = (Vector3.up * 1.5f + Random.insideUnitSphere).normalized;
                _rb.AddForce(kick * _unstickImpulse, ForceMode.VelocityChange);
                _rb.AddTorque(Random.insideUnitSphere * _wildTorque, ForceMode.VelocityChange);
            }
        }
        else
        {
            _stuckTimer = 0f;
        }

        // ── Ограничение скорости, чтобы не улетал в космос.
        if (_rb.linearVelocity.magnitude > _wildMaxSpeed)
            _rb.linearVelocity = _rb.linearVelocity.normalized * _wildMaxSpeed;
    }

    /// <summary>Шланг не пускает дальше длины — В 3D (и по высоте тоже, иначе на
    /// реактивной тяге можно улететь в небо).</summary>
    private void EnforceHoseLength()
    {
        if (!IsConnected) return;
        HydrantNut outlet = _ring.AttachedOutlet;
        if (outlet == null) return;

        Transform anchor = outlet.RingSeat != null ? outlet.RingSeat : outlet.transform;

        if (_holderFpc == null && _holdingHand != null)
            _holderFpc = _holdingHand.GetComponentInParent<FirstPersonController>();
        if (_holderFpc == null) return;

        Transform player = _holderFpc.transform;
        Vector3 to = player.position - anchor.position;
        float dist = to.magnitude;
        if (dist <= _maxHoseLength) return;

        // Возвращаем на сферу радиуса длины шланга (включая высоту).
        Vector3 clamped = anchor.position + to.normalized * _maxHoseLength;
        _holderFpc.ServerClampPosition(clamped);
    }

    public override void UpdateHeldPose(Transform handAnchor)
    {
        if (_holderFpc == null)
            _holderFpc = handAnchor.GetComponentInParent<FirstPersonController>();

        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);
        transform.SetPositionAndRotation(pos, rot);
    }
}
