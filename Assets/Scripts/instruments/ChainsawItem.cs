using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Бензопила. Инструмент в руке.
///
///   ЛКМ (удержание) — работает пила: играет анимация пиления (Animator bool).
///   G              — ОБЫЧНЫЙ бросок: просто падает под гравитацией (база).
///   F              — подобрать (в т.ч. пойманную «убежавшую» пилу).
///
/// ОСОБОЕ ДЕЙСТВИЕ (отдельно от G):
///   Пока пила пилит, в СЛУЧАЙНЫЙ момент она может ВЫПРЫГНУТЬ из рук: сервер
///   выкидывает её из руки с импульсом, и дальше она физикой ЕЗДИТ ПО КРУГУ по
///   земле (толкается, врезается) с включённой анимацией пиления, пока её не
///   подберут обратно. Это НЕ обычный дроп — путь полностью отдельный
///   (ServerEject → _looseRunning), G его не трогает.
///
/// СЕТЬ:
///   - _sawing / _looseRunning — SyncVar (флаги знают все).
///   - Анимация — локально у всех по (_sawing || _looseRunning).
///   - Пока пила «убежала», она НИЧЬЯ → авторитет физики у СЕРВЕРА: он гоняет её
///     по кругу, клиенты кинематичны и следуют за NetworkTransform.
///
/// Требует: NetworkObject, NetworkTransform (Client Authoritative), Collider,
/// Rigidbody (Collision Detection = Continuous!), Animator (можно на дочернем).
/// </summary>
public class ChainsawItem : InteractableItem
{
    [Header("Анимация пиления")]
    [Tooltip("Animator пилы (можно на дочернем меше)")]
    [SerializeField] private Animator _animator;
    [Tooltip("Имя BOOL-параметра в Animator, который включает пиление")]
    [SerializeField] private string _animBoolParam = "Sawing";
    [Tooltip("Множитель скорости анимации пиления (на будущее)")]
    [SerializeField] private float _animSpeed = 1f;
    [Tooltip("Проигрывать анимацию в обратную сторону")]
    [SerializeField] private bool _animReverse = false;

    [Header("Ввод")]
    [Tooltip("ВКЛ — пилит, пока ЛКМ зажата. ВЫКЛ — ЛКМ переключает.")]
    [SerializeField] private bool _holdToSaw = true;

    [Header("Вылет из рук (случайный, во время пиления)")]
    [Tooltip("Мин. суммарное время пиления до возможного вылета, сек")]
    [SerializeField] private float _minSawBeforeEject = 4f;
    [Tooltip("Макс. суммарное время пиления до вылета, сек")]
    [SerializeField] private float _maxSawBeforeEject = 14f;
    [Tooltip("Сила выброса из рук")]
    [SerializeField] private float _ejectForce = 6f;
    [Tooltip("Насколько выброс направлен вверх (0..1)")]
    [SerializeField] private float _ejectUpBias = 0.6f;

    [Header("Езда по земле (пока пила убежала)")]
    [Tooltip("Целевая горизонтальная скорость езды")]
    [SerializeField] private float _driveSpeed = 3.5f;
    [Tooltip("Ускорение разгона вперёд")]
    [SerializeField] private float _driveAccel = 12f;
    [Tooltip("Скорость доворота, град/сек (даёт круг; минус = в другую сторону)")]
    [SerializeField] private float _driveYawRate = 120f;
    [Tooltip("Слой(и) земли — для проверки, что пила приземлилась (тогда едет)")]
    [SerializeField] private LayerMask _groundMask;
    [Tooltip("Дальность луча вниз для проверки земли")]
    [SerializeField] private float _groundedDist = 0.6f;

    [Header("Положение в руке (регулировка под модель)")]
    [SerializeField] private Vector3 _holdPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    // ───────── СЕТЕВОЕ СОСТОЯНИЕ ─────────
    private readonly SyncVar<bool> _sawing = new(false);       // пилит в руке (ЛКМ)
    private readonly SyncVar<bool> _looseRunning = new(false); // убежала и ездит

    // ───────── ЛОКАЛЬНОЕ / СЕРВЕР ─────────
    private bool _lastHeld;
    private bool _ejecting;         // флаг: текущий дроп — это вылет, а не G
    private bool _ejectScheduled;
    private float _sawAccum;
    private float _ejectAt;
    private PlayerHand _holdingHand; // кто держит (для выброса из руки), сервер

    private const float MaxDt = 0.05f;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _sawing.OnChange += OnSawingChanged;
        _looseRunning.OnChange += OnLooseChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _sawing.OnChange -= OnSawingChanged;
        _looseRunning.OnChange -= OnLooseChanged;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        UpdateAnimator();
        // ВАЖНО: трогаем физику ТОЛЬКО если пила реально «убежала» (поздний вход
        // в уже едущую пилу). Для лежащей/спавнящейся пилы НИЧЕГО не меняем —
        // иначе она станет кинематичной (не падает) с выключенным коллайдером
        // (не подобрать). Покой/рука рулятся базой и дефолтом префаба.
        if (_looseRunning.Value) ApplyLoosePhysics(true);
    }

    // ───────── КАЖДЫЙ КАДР ─────────

    private void Update()
    {
        // Сервер: таймер случайного вылета (только пока пилит в руке).
        if (base.IsServerInitialized && _sawing.Value && _ejectScheduled)
        {
            _sawAccum += Mathf.Min(Time.deltaTime, MaxDt);
            if (_sawAccum >= _ejectAt)
            {
                _ejectScheduled = false;
                ServerEject();
            }
        }

        // Владелец (держатель) читает ЛКМ. Пока пила убежала — владельца нет.
        if (base.IsOwner) HandleOwnerInput();
    }

    private void FixedUpdate()
    {
        // Езду считает только сервер (авторитет физики «ничьей» пилы).
        if (!base.IsServerInitialized || !_looseRunning.Value || _rb == null) return;

        // Пока в воздухе после выброса — просто падает; едет только на земле.
        bool grounded = Physics.Raycast(transform.position, Vector3.down,
                                        _groundedDist, _groundMask, QueryTriggerInteraction.Ignore);
        if (!grounded) return;

        // Направление «вперёд» по горизонтали (по морде пилы).
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(transform.right, Vector3.up);
        fwd.Normalize();

        // Разгон вперёд до целевой скорости.
        Vector3 v = _rb.linearVelocity;
        Vector3 horiz = new Vector3(v.x, 0f, v.z);
        if (horiz.magnitude < _driveSpeed)
            _rb.AddForce(fwd * _driveAccel, ForceMode.Acceleration);

        // Постоянный доворот вокруг вертикали → едет по кругу.
        Vector3 ang = _rb.angularVelocity;
        ang.y = _driveYawRate * Mathf.Deg2Rad;
        _rb.angularVelocity = ang;
    }

    private void HandleOwnerInput()
    {
        if (_holdToSaw)
        {
            bool held = Input.GetMouseButton(0);
            if (held != _lastHeld)
            {
                _lastHeld = held;
                CmdSetSawing(held);
            }
        }
        else
        {
            if (Input.GetMouseButtonDown(0))
                CmdToggleSawing();
        }
    }

    // ───────── СЕРВЕР: ПИЛЕНИЕ ─────────

    [ServerRpc(RequireOwnership = true)]
    private void CmdSetSawing(bool value)
    {
        if (_looseRunning.Value) return;         // пила не в руке — пилить нечем
        if (_sawing.Value == value) return;
        _sawing.Value = value;
        if (value) ScheduleEject(); else _ejectScheduled = false;
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdToggleSawing()
    {
        if (_looseRunning.Value) return;
        _sawing.Value = !_sawing.Value;
        if (_sawing.Value) ScheduleEject(); else _ejectScheduled = false;
    }

    private void ScheduleEject()
    {
        _sawAccum = 0f;
        _ejectAt = Random.Range(_minSawBeforeEject, _maxSawBeforeEject);
        _ejectScheduled = true;
    }

    // ───────── СЕРВЕР: ВЫЛЕТ ИЗ РУК (отдельно от G) ─────────

    private void ServerEject()
    {
        if (_holdingHand == null) return; // не в руке — нечему вылетать

        _sawing.Value = false;

        // Направление выброса: вверх + случайный разброс в стороны.
        Vector3 rnd = Random.insideUnitSphere;
        Vector3 dir = (Vector3.up * _ejectUpBias + rnd * (1f - _ejectUpBias)).normalized;
        if (dir.y < 0f) dir.y = -dir.y;

        // Помечаем, что это ВЫЛЕТ (не обычный дроп), и выкидываем из руки штатным
        // путём — рука очистит слот и вызовет наш ServerOnDrop, где по флагу
        // _ejecting включим режим «убежала».
        _ejecting = true;
        _holdingHand.ServerDrop(dir, _ejectForce);
        _ejecting = false;
    }

    // ───────── ВЗЯТЬ / БРОСИТЬ ─────────

    public override void ServerOnPickup(PlayerHand hand)
    {
        _looseRunning.Value = false; // подобрали — перестаёт ездить
        _holdingHand = hand;
        base.ServerOnPickup(hand);   // ownership + kinematic (в руке)
    }

    public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
    {
        _sawing.Value = false;
        base.ServerOnDrop(hand, tossDirection, force); // dynamic + снять ownership + импульс
        _holdingHand = null;

        // Отдельная развилка: если это ВЫЛЕТ — включаем режим «убежала и ездит».
        // Если обычный G (force=0, _ejecting=false) — просто упала, всё как всегда.
        if (_ejecting)
            _looseRunning.Value = true;
    }

    // ───────── РЕАКЦИЯ НА ФЛАГИ ─────────

    private void OnSawingChanged(bool prev, bool next, bool asServer) => UpdateAnimator();

    private void OnLooseChanged(bool prev, bool next, bool asServer)
    {
        ApplyLoosePhysics(next);
        UpdateAnimator();
    }

    private void UpdateAnimator()
    {
        if (_animator == null || string.IsNullOrEmpty(_animBoolParam)) return;
        _animator.SetBool(_animBoolParam, _sawing.Value || _looseRunning.Value);
        // Скорость и направление (минус = реверс). Применяется при старте и при
        // смене флагов; для живой подстройки в игре меняй поля до входа в режим.
        _animator.speed = _animSpeed * (_animReverse ? -1f : 1f);
    }

    /// <summary>Физика режима «убежала»: сервер считает (dynamic), клиенты
    /// кинематичны (визуал ведёт NetworkTransform), коллайдер включён
    /// (сталкивается и ловится подбором). В ПОКОЕ/РУКЕ не вмешиваемся — там рулит
    /// база (SetPhysicsHeld) и дефолт префаба. Раньше здесь форсился kinematic и
    /// выключался коллайдер → лежащая пила «летала» и не подбиралась.</summary>
    private void ApplyLoosePhysics(bool loose)
    {
        if (!loose) return; // покой/рука — не трогаем
        if (_rb != null) _rb.isKinematic = !base.IsServerInitialized; // server физика, клиенты за NT
        if (_col != null) _col.enabled = true;
    }

    // ───────── ПОЗА В РУКЕ ─────────

    public override void UpdateHeldPose(Transform handAnchor)
    {
        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);
        transform.SetPositionAndRotation(pos, rot);
    }
}
