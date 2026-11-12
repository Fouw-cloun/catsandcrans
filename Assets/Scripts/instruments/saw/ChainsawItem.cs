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
public class ChainsawItem : InteractableItem, ISawTool
{

    public override bool UseHeldBlocking => false;     // v17.3: своя логика столкновений/позы
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

    [Header("Полотно (для нарезки продуктов)")]
    [Tooltip("Дочерний объект — шина пилы. От него летят крошки. " +
             "Пусто = сам корень.")]
    [SerializeField] private Transform _blade;

    [Header("Положение в руке (регулировка под модель)")]
    [SerializeField] private Vector3 _holdPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    [Header("v14: столкновения в руке")]
    [Tooltip("Пила в руке не проходит сквозь стены, стол и предметы — упирается и отъезжает к игроку")]
    [SerializeField] private bool _blockByWorld = true;
    [Tooltip("Слои, о которые упирается")]
    [SerializeField] private LayerMask _blockMask = ~0;
    [Tooltip("Насколько отъезжает к камере, упёршись (0 — до самой камеры, 1 — не отъезжает)")]
    [Range(0f, 0.9f)][SerializeField] private float _retractTo = 0.35f;
    [Tooltip("Работающая пила входит в то, что пилит (бревно, какао-боб, мясо), с этой скоростью (доля хода в секунду) — «тяжело»")]
    [Range(0.05f, 3f)][SerializeField] private float _cutSinkSpeed = 0.45f;
    [Tooltip("Свободно — догоняет руку с этой скоростью (доля хода в секунду)")]
    [Range(1f, 40f)][SerializeField] private float _freeSpeed = 14f;
    [Tooltip("Насколько замедляется игрок, пока пила в распиле")]
    [Range(0.2f, 1f)][SerializeField] private float _cutWalkSlow = 0.6f;
    [Tooltip("Дрожь пилы в распиле, м")]
    [Range(0f, 0.02f)][SerializeField] private float _cutShake = 0.004f;
    [Tooltip("Габарит пилы для проверки, доля (0.9 — чуть меньше модели, чтобы не цеплялась краешком)")]
    [Range(0.5f, 1f)][SerializeField] private float _boxShrink = 0.88f;

    // ───────── СЕТЕВОЕ СОСТОЯНИЕ ─────────
    private readonly SyncVar<bool> _sawing = new SyncVar<bool>(false);       // пилит в руке (ЛКМ)
    private readonly SyncVar<bool> _looseRunning = new SyncVar<bool>(false); // убежала и ездит

    // ───────── ЛОКАЛЬНОЕ / СЕРВЕР ─────────
    private bool _lastHeld;
    private bool _ejecting;         // флаг: текущий дроп — это вылет, а не G
    private bool _ejectScheduled;
    private float _sawAccum;
    private float _ejectAt;
    private PlayerHand _holdingHand; // кто держит (для выброса из руки), сервер

    private Transform _holderCam;

    private const float MaxDt = 0.05f;

    // v14: столкновения в руке (только у владельца — поза считается у него)
    private bool _boxReady;
    private Vector3 _boxCenter, _boxHalf;          // в осях корня, без масштаба корня
    private float _push = 1f;                      // 1 — где рука, 0 — отъехала к камере
    private bool _inCut;
    private readonly Collider[] _overlap = new Collider[24];

    /// <summary>Пока пила в распиле — игрок идёт медленнее.</summary>
    public override float CarrySpeedMultiplier => _inCut ? _cutWalkSlow : 1f;

    // ───────── ISawTool: для ChainsawFoodProcessor ─────────

    /// <summary>
    /// Работает ли пила. У ВЛАДЕЛЬЦА подмешиваем локальный флаг: реакция должна
    /// быть в тот же кадр, а SyncVar приезжает только через круг до сервера.
    /// Убежавшая пила не пилит: при вылете _sawing сбрасывается.
    /// </summary>
    public bool IsSawing => _sawing.Value ||
                            (base.IsOwner && _holdToSaw && _lastHeld && !_looseRunning.Value);

    public Transform Blade => _blade != null ? _blade : transform;

    /// <summary>Камера держателя — берётся через руку, значит это точно
    /// его камера, а не первая попавшаяся в сцене.</summary>
    public Transform AimOrigin => _holderCam;

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
        _holderCam = null;

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
        if (_holderCam == null)
        {
            FirstPersonController fpc = handAnchor.GetComponentInParent<FirstPersonController>();
            if (fpc != null)
            {
                Camera c = fpc.GetComponentInChildren<Camera>(true);
                if (c != null) _holderCam = c.transform;
            }
        }

        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);
        if (_blockByWorld) pos = BlockedPose(pos, rot);
        transform.SetPositionAndRotation(pos, rot);
    }

    // ───────── v14: НЕ СКВОЗЬ ПРЕДМЕТЫ ─────────
    //
    // Габарит пилы — коробка по её мешам. Каждый кадр ищем, насколько далеко от камеры к руке
    // пила может встать, ни во что не влезая (бисекция по ходу 0..1). Не заведена — упирается во всё.
    // Заведена — то, что можно пилить (ICuttable, CanCut), её пускает, но медленно (Cut Sink Speed),
    // с дрожью и замедлением игрока. Остальное (стол, стены, другие предметы) — всё равно упор.

    private Vector3 BlockedPose(Vector3 want, Quaternion rot)
    {
        if (!_boxReady) BuildBox();
        Vector3 cam = _holderCam != null ? _holderCam.position : want;
        Vector3 back = cam + (want - cam) * _retractTo;
        bool sawing = IsSawing;
        float dt = Mathf.Min(Time.deltaTime, MaxDt);

        // жёсткий предел: куда можно, если цели распила пропускают (когда пила работает)
        float hard = MaxFree(back, want, rot, sawing);
        // мягкий: где начинается цель распила
        float soft = sawing ? MaxFree(back, want, rot, false) : hard;

        if (_push > hard) _push = hard;                                    // упёрлась — сразу назад
        else if (_push < soft) _push = Mathf.MoveTowards(_push, Mathf.Min(hard, soft), _freeSpeed * dt);
        else _push = Mathf.MoveTowards(_push, hard, (soft < hard ? _cutSinkSpeed : _freeSpeed) * dt);
        _inCut = sawing && _push > soft + 0.01f;

        Vector3 p = Vector3.Lerp(back, want, _push);
        if (_inCut && _cutShake > 0f)
        {
            float t = Time.time * 55f;
            p += new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(t, 2.1f) - 0.5f, Mathf.PerlinNoise(t, 4.7f) - 0.5f) * (2f * _cutShake);
        }
        return p;
    }

    /// <summary>Наибольший ход 0..1, при котором пила ни во что не влезает. passCut — цели распила не мешают.</summary>
    private float MaxFree(Vector3 back, Vector3 want, Quaternion rot, bool passCut)
    {
        if (!Blocked(want, rot, passCut)) return 1f;
        if (Blocked(back, rot, passCut)) return 0f;
        float lo = 0f, hi = 1f;
        for (int i = 0; i < 7; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (Blocked(Vector3.Lerp(back, want, mid), rot, passCut)) hi = mid; else lo = mid;
        }
        return lo;
    }

    private bool Blocked(Vector3 pos, Quaternion rot, bool passCut)
    {
        Vector3 ls = transform.lossyScale;
        Vector3 c = pos + rot * Vector3.Scale(_boxCenter, ls);
        Vector3 h = Vector3.Scale(_boxHalf, new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z))) * _boxShrink;
        int n = Physics.OverlapBoxNonAlloc(c, h, _overlap, rot, _blockMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider col = _overlap[i];
            if (col == null || !col.enabled) continue;
            if (col.transform.IsChildOf(transform)) continue;
            if (col.GetComponentInParent<CharacterController>() != null) continue;      // игроки
            if (col.GetComponentInParent<FirstPersonController>() != null) continue;
            if (passCut)
            {
                ICuttable cut = col.GetComponentInParent<ICuttable>();
                if (cut != null && cut.CanCut) continue;
            }
            return true;
        }
        return false;
    }

    /// <summary>Коробка пилы по её мешам (в осях корня, без масштаба корня).</summary>
    private void BuildBox()
    {
        _boxReady = true;
        bool any = false;
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        Vector3 ls = transform.lossyScale;
        Vector3 inv = new Vector3(1f / Mathf.Max(1e-5f, Mathf.Abs(ls.x)), 1f / Mathf.Max(1e-5f, Mathf.Abs(ls.y)), 1f / Mathf.Max(1e-5f, Mathf.Abs(ls.z)));
        foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || (mf.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int k = 0; k < 8; k++)
            {
                Vector3 v = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                Vector3 w = mf.transform.TransformPoint(v);
                // в оси корня, но без его масштаба (масштаб умножим в Blocked)
                Vector3 l = Quaternion.Inverse(transform.rotation) * (w - transform.position);
                if (!any) { b = new Bounds(l, Vector3.zero); any = true; } else b.Encapsulate(l);
            }
        }
        if (!any && _col != null)
        {
            Bounds cb = _col.bounds;
            b = new Bounds(Quaternion.Inverse(transform.rotation) * (cb.center - transform.position), cb.size);
        }
        _boxCenter = Vector3.Scale(b.center, inv);
        _boxHalf = Vector3.Scale(b.extents, inv);
        Debug.Log($"[Бензопила] «{name}»: габарит для столкновений {Vector3.Scale(b.size, Vector3.one):0.00} м — в руке не проходит сквозь предметы", this);
    }
}
