using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using CatCooks.Food;

/// <summary>
/// v20: ТАЧКА-ТАРА, НАПИСАННАЯ ЗАНОВО. Один простой компонент вместо PushCart + CartRig + CartBalance +
/// CartCrash + CartTrough + CartDust. Главное — СТАБИЛЬНОСТЬ: тачка не летает, не скользит и не трясётся,
/// потому что у неё вообще НЕТ свободной физики.
///
/// Как устроено:
///   • Тело ВСЕГДА кинематическое. Стоит — стоит как вкопанная. Никаких пружин, подвески, баланса.
///   • F по тачке — взялся за ручки; F ещё раз — отпустил (тачка сама встаёт на ножки).
///   • Пока держишь: колесо едет ПО ЗЕМЛЕ в точке перед тобой (луч вниз находит пол; на столы, вёдра
///     и продукты не заезжает), корпус повёрнут туда же, куда ты. Движение сглажено — без рывков.
///     В стену колесо упирается и дальше не идёт.
///   • ПКМ (держать) — завалить вперёд ЧЕРЕЗ КОЛЕСО; отпустил — вернулась. Содержимое льётся и сыплется
///     само (CookwareSpill у тары смотрит на наклон).
///   • Полная тачка едет медленнее (Speed Empty → Speed Full).
///   • Колёса крутятся по пройденному пути.
///   • Жидкость в корыте плещется от разгона, торможения и поворотов сама (тара меряет своё движение);
///     волны при езде усилены, дно не открывается.
///
/// Геометрию меряет сама в Awake по модели: колёса ищет по имени (tire / wheel / колес), ось — между ними,
/// «перёд» — от середины модели к колёсам, длину до ручек — по габариту. Всё можно задать руками.
///
/// Ставится меню: CatCooks → Тачка → НОВАЯ тачка-тара (оно же снимает старые компоненты).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FoodCart : InteractableItem
{
    [Header("Геометрия (пусто/0 — померяет сама по модели)")]
    [Tooltip("Точка оси колёс. Пусто — середина между колёсами (ищет по имени tire/wheel/колес)")]
    [SerializeField] private Transform _axle;
    [Tooltip("Колёса (крутятся). Пусто — найдёт по имени")]
    [SerializeField] private Transform[] _wheels;
    [Tooltip("Куда у модели «перёд» (сторона колеса), в осях тачки. Ноль — определит сама")]
    [SerializeField] private Vector3 _forwardLocal = Vector3.zero;
    [Tooltip("Радиус колеса, м. 0 — по модели")]
    [Min(0f)][SerializeField] private float _wheelRadius = 0f;
    [Tooltip("От игрока до оси колёс, м. 0 — по длине модели (ручки у живота)")]
    [Min(0f)][SerializeField] private float _playerToAxle = 0f;
    [Tooltip("Зазор между концом ручек и игроком, м")]
    [Range(0f, 1f)][SerializeField] private float _handGap = 0.2f;

    [Header("Езда")]
    [Tooltip("Как быстро тачка догоняет своё место: 20 — как приклеенная, 8 — тяжёлая, с ленцой на поворотах")]
    [Range(3f, 30f)][SerializeField] private float _follow = 14f;
    [Tooltip("На сколько градусов приподняты ручки, пока катишь (ножки над землёй)")]
    [Range(0f, 20f)][SerializeField] private float _carryPitch = 5f;
    [Tooltip("Скорость игрока с пустой тачкой")]
    [Range(0.2f, 1f)][SerializeField] private float _speedEmpty = 0.9f;
    [Tooltip("Скорость игрока с полной тачкой")]
    [Range(0.2f, 1f)][SerializeField] private float _speedFull = 0.6f;
    [Tooltip("На какую ступеньку выше ног игрока тачка может заехать, м (выше — это стол или стена)")]
    [Range(0.05f, 1.5f)][SerializeField] private float _maxStep = 0.45f;
    [Tooltip("Слои земли и стен")]
    [SerializeField] private LayerMask _groundMask = ~0;

    [Header("Завал вперёд (ПКМ — держать)")]
    [SerializeField] private KeyCode _tipKey = KeyCode.Mouse1;
    [Range(20f, 120f)][SerializeField] private float _tipAngle = 75f;
    [Tooltip("Секунд на полный завал")]
    [Range(0.1f, 2f)][SerializeField] private float _tipSeconds = 0.5f;

    [Header("Отладка")]
    [SerializeField] private bool _hint = true;
    [SerializeField] private bool _log = true;

    private readonly SyncVar<NetworkObject> _gripper = new SyncVar<NetworkObject>(null);

    // геометрия (меряется в Awake)
    private Vector3 _fwdL = Vector3.forward;       // «перёд» в осях тачки
    private Vector3 _axleOffset;                   // от пивота до оси, в повороте тачки (мировые метры)
    private float _radius = 0.2f, _halfWidth = 0.3f, _toPlayer = 1.2f;
    private Renderer[] _wheelR;

    // состояние езды
    private bool _isLocalGripper;
    private Vector3 _fwdS;                         // сглаженный курс
    private float _pitch, _groundY;
    private bool _hasGround, _tipHeld;
    private float _settleT;                        // сервер: тачка встаёт на ножки
    private float _parkTick, _fallV;
    private bool _falling;
    private Vector3 _lastAxle;
    private bool _lastAxleOk;
    private Cookware _cookware;
    private Collider[] _solid;
    private FirstPersonController _localFpc;
    private readonly RaycastHit[] _hits = new RaycastHit[24];

    public bool IsGripped => _gripper.Value != null;
    private Vector3 AxleNow => _rb.position + _rb.rotation * _axleOffset;

    // ───────────────────────── запуск ─────────────────────────

    protected override void Awake()
    {
        base.Awake();
        _canPickup = false;
        _cookware = GetComponentInChildren<Cookware>(true);
        var solid = new System.Collections.Generic.List<Collider>();
        foreach (Collider c in GetComponentsInChildren<Collider>(true))
            if (c != null && !c.isTrigger) solid.Add(c);
        if (solid.Count == 0)
        {
            // v20.1: у тачки нет ни одного коллайдера — сквозь неё ходили. Даём по мешу (меню ставит их заранее).
            foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null || !CartBottomFilter.Counts(mf.GetComponent<Renderer>())) continue;
                MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                solid.Add(mc);
            }
            Debug.LogWarning($"[Тачка] «{name}»: не было коллайдеров — добавил по мешам ({solid.Count}). " +
                             "Лучше нажми меню CatCooks → Тачка → НОВАЯ тачка-тара: оно поставит их в сцене", this);
        }
        _solid = solid.ToArray();
        Measure();
        if (_rb != null)
        {
            _rb.isKinematic = true;                // всегда: никакой свободной физики — ничего не трясётся
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }
    }

    private void Start()
    {
        CookwareLiquidVisual lv = GetComponentInChildren<CookwareLiquidVisual>(true);
        if (lv != null) lv.TuneForVehicle();
    }

    /// <summary>Меряем модель в авторской позе: ось, радиус, перёд, длину до ручек.</summary>
    private void Measure()
    {
        // колёса
        var wr = new System.Collections.Generic.List<Renderer>();
        if (_wheels != null)
            foreach (Transform w in _wheels)
                if (w != null) { Renderer r = w.GetComponentInChildren<Renderer>(); if (r != null) wr.Add(r); }
        if (wr.Count == 0)
            foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.transform == transform) continue;
                string n = r.gameObject.name.ToLowerInvariant();
                if (n.Contains("barrow")) continue;
                if (n.Contains("tire") || n.Contains("wheel") || n.Contains("колес") || n.Contains("шина")) wr.Add(r);
            }
        _wheelR = wr.ToArray();

        // габарит модели (без жидкости/горки тары и частиц)
        bool any = false;
        Bounds all = new Bounds(transform.position, Vector3.zero);
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (!CartBottomFilter.Counts(r)) continue;
            if (!any) { all = r.bounds; any = true; } else all.Encapsulate(r.bounds);
        }

        // ось
        Vector3 axle = transform.position;
        float rad = 0f;
        if (_axle != null) axle = _axle.position;
        else if (_wheelR.Length > 0)
        {
            axle = Vector3.zero;
            foreach (Renderer r in _wheelR) axle += r.bounds.center;
            axle /= _wheelR.Length;
        }
        foreach (Renderer r in _wheelR) rad += r.bounds.extents.y;
        if (_wheelR.Length > 0) rad /= _wheelR.Length;
        if (rad < 0.01f && any) rad = Mathf.Max(0.05f, axle.y - all.min.y);
        _radius = _wheelRadius > 0.001f ? _wheelRadius : Mathf.Max(0.03f, rad);
        _axleOffset = Quaternion.Inverse(transform.rotation) * (axle - transform.position);

        // перёд: от середины модели к оси колёс, по ближайшей оси тачки
        Vector3 f = _forwardLocal;
        if (f.sqrMagnitude < 1e-4f)
        {
            Vector3 d = Quaternion.Inverse(transform.rotation) * (axle - (any ? all.center : transform.position));
            d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) f = Vector3.forward;
            else f = Mathf.Abs(d.x) > Mathf.Abs(d.z) ? new Vector3(Mathf.Sign(d.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(d.z));
        }
        f.y = 0f;
        _fwdL = f.normalized;

        // длина назад до ручек и полуширина
        Vector3 fw = transform.rotation * _fwdL;
        Vector3 rw = Vector3.Cross(Vector3.up, fw);
        float back = 0.8f;
        _halfWidth = 0.3f;
        if (any)
        {
            back = 0f; _halfWidth = 0f;
            Vector3 c = all.center, e = all.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                back = Mathf.Max(back, -Vector3.Dot(p - axle, fw));
                _halfWidth = Mathf.Max(_halfWidth, Mathf.Abs(Vector3.Dot(p - axle, rw)));
            }
        }
        _toPlayer = _playerToAxle > 0.001f ? _playerToAxle : back + _handGap;
        _fwdS = fw;

        if (_log)
            Debug.Log($"[Тачка] «{name}»: колёс {_wheelR.Length}, радиус {_radius:0.00} м, перёд по оси {_fwdL}, " +
                      $"до игрока {_toPlayer:0.00} м, полуширина {_halfWidth:0.00} м" +
                      (_wheelR.Length == 0 && _axle == null ? "  ! колёса не найдены — задай Axle и Wheels руками" : ""), this);
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _gripper.OnChange += OnGripperChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _gripper.OnChange -= OnGripperChanged;
        if (_localFpc != null) _localFpc.SpeedMultiplier = 1f;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _settleT = 0.6f;                           // на старте аккуратно ставим колесо на пол
    }

    // ───────────────────────── взять / отпустить ─────────────────────────

    public override void ServerInteract(PlayerInteractor interactor)
    {
        if (interactor == null) return;
        if (_gripper.Value != null)
        {
            if (_gripper.Value == interactor.NetworkObject) ServerRelease();
            return;
        }
        _settleT = 0f;
        _gripper.Value = interactor.NetworkObject;
        NetworkObject.GiveOwnership(interactor.Owner);
        if (_log) Debug.Log($"[Тачка] «{name}»: взялся {interactor.name}. F — отпустить, ПКМ (держать) — завалить вперёд", this);
    }

    [Server]
    private void ServerRelease()
    {
        _gripper.Value = null;
        NetworkObject.RemoveOwnership();
        _settleT = 0.6f;
        _hasGround = false;
        if (_log) Debug.Log($"[Тачка] «{name}»: отпустили — встаёт на ножки", this);
    }

    private void OnGripperChanged(NetworkObject prev, NetworkObject next, bool asServer)
    {
        if (asServer && base.IsClientInitialized) return;
        _isLocalGripper = next != null && next.IsOwner;
        IgnorePlayer(prev, false);
        if (next == null)
        {
            // v20.1: отпустили — тачка снова твёрдая для ВСЕХ игроков (на случай, если prev не пришёл)
            foreach (FirstPersonController p in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
            {
                NetworkObject pn = p != null ? p.GetComponent<NetworkObject>() : null;
                if (pn != null) IgnorePlayer(pn, false);
            }
        }
        IgnorePlayer(next, true);
        if (_isLocalGripper)
        {
            _localFpc = next.GetComponent<FirstPersonController>();
            _hasGround = false;
            Vector3 f = _rb.rotation * _fwdL; f.y = 0f;
            _fwdS = f.sqrMagnitude > 1e-4f ? f.normalized : next.transform.forward;
            _pitch = CurrentPitch();
            _tipHeld = false;
        }
        else if (_localFpc != null)
        {
            _localFpc.SpeedMultiplier = 1f;
            _localFpc = null;
        }
    }

    /// <summary>Игрок, который катит, не толкается с тачкой (иначе упирался бы в неё животом).</summary>
    private void IgnorePlayer(NetworkObject player, bool ignore)
    {
        if (player == null || _solid == null) return;
        foreach (Collider pc in player.GetComponentsInChildren<Collider>(true))
        {
            if (pc == null) continue;
            foreach (Collider c in _solid)
                if (c != null) Physics.IgnoreCollision(c, pc, ignore);
        }
    }

    // ───────────────────────── кадр ─────────────────────────

    private void Update()
    {
        SpinWheels();
        if (!_isLocalGripper) return;
        _tipHeld = Input.GetKey(_tipKey);
        if (_localFpc != null)
        {
            float fill = _cookware != null && _cookware.Capacity > 0 ? Mathf.Clamp01(_cookware.TotalCount / (float)_cookware.Capacity) : 0f;
            _localFpc.SpeedMultiplier = Mathf.Lerp(_speedEmpty, _speedFull, fill);
        }
    }

    private void FixedUpdate()
    {
        if (_rb == null) return;
        float dt = Time.fixedDeltaTime;

        if (_isLocalGripper)
        {
            NetworkObject g = _gripper.Value;
            if (g != null) Drive(g.transform, dt);
            return;
        }

        if (!base.IsServerInitialized) return;
        NetworkObject gr = _gripper.Value;
        if (gr != null)
        {
            // держатель пропал или убежал — отпускаем
            if (!gr.IsSpawned || (gr.transform.position - AxleNow).sqrMagnitude > (_toPlayer + 4f) * (_toPlayer + 4f)) ServerRelease();
            return;
        }
        Parked(dt);
    }

    /// <summary>Катим: ось колёс — на земле перед игроком, корпус — по его курсу.</summary>
    private void Drive(Transform player, float dt)
    {
        Vector3 f = player.forward; f.y = 0f;
        if (f.sqrMagnitude < 1e-4f) return;
        f.Normalize();
        float k = 1f - Mathf.Exp(-dt * _follow);
        _fwdS = Vector3.Slerp(_fwdS, f, k); _fwdS.y = 0f;
        if (_fwdS.sqrMagnitude < 1e-4f) _fwdS = f;
        _fwdS.Normalize();

        _pitch = Mathf.MoveTowards(_pitch, _tipHeld ? _tipAngle : _carryPitch, _tipAngle / Mathf.Max(0.05f, _tipSeconds) * dt);

        Vector3 cur = AxleNow;
        Vector3 want = player.position + f * _toPlayer;
        float gy;
        if (Ground(want, player.position.y + _maxStep, player, out gy))
        {
            _groundY = _hasGround ? Mathf.Lerp(_groundY, gy, 1f - Mathf.Exp(-dt * 16f)) : gy;
            _hasGround = true;
        }
        else if (!_hasGround) { _groundY = cur.y - _radius; _hasGround = true; }
        want.y = _groundY + _radius;

        Vector3 next = Vector3.Lerp(cur, want, k);
        next = Block(cur, next, player);
        next.y = want.y;
        Pose(next, _fwdS, _pitch);
    }

    /// <summary>
    /// v20.1: СТОИТ, НО НЕ ВМОРОЖЕНА. Сервер следит за землёй под колесом и под ножками:
    /// пол ушёл (люк, убрали плиту) — тачка падает на новый пол; пол неровный — ложится по уклону.
    /// Вбок и вперёд не двигается никогда — сама не ползает.
    /// </summary>
    private void Parked(float dt)
    {
        // только что отпустили — каждый шаг; дальше — 5 раз в секунду, пока не сдвинется земля
        _parkTick -= dt;
        if (_settleT > 0f) _settleT -= dt;
        else if (_parkTick > 0f && !_falling) return;
        _parkTick = 0.2f;

        Vector3 f = _rb.rotation * _fwdL; f.y = 0f;
        if (f.sqrMagnitude < 1e-4f) return;
        f.Normalize();
        Vector3 axle = AxleNow;
        float pitchNow = CurrentPitch();

        float gy, gyRear;
        bool g1 = Ground(axle, axle.y + _maxStep, null, out gy);
        float legs = Mathf.Max(0.2f, (_toPlayer - _handGap) * 0.55f);          // ножки — позади оси
        Vector3 rear = axle - f * legs;
        bool g2 = Ground(rear, axle.y + _maxStep, null, out gyRear);

        float wantY = g1 ? gy + _radius : axle.y - 10f;                          // пола нет — падаем
        float wantPitch = g1 && g2 ? Mathf.Clamp(Mathf.Atan2(gyRear - gy, legs) * Mathf.Rad2Deg, -35f, 35f) : 0f;

        float dy = wantY - axle.y;
        if (dy < -0.004f)
        {
            _falling = true;
            _fallV = Mathf.Min(_fallV + 9.81f * dt, 25f);
            axle.y = Mathf.Max(wantY, axle.y - _fallV * dt);
        }
        else
        {
            _fallV = 0f;
            _falling = Mathf.Abs(dy) > 0.004f || Mathf.Abs(Mathf.DeltaAngle(pitchNow, wantPitch)) > 0.3f;
            axle.y = Mathf.Lerp(axle.y, wantY, 1f - Mathf.Exp(-dt * 14f));
        }
        float pitch = Mathf.MoveTowards(pitchNow, wantPitch, 240f * dt);
        if (!_falling && _settleT <= 0f) return;                                 // всё на месте — не трогаем
        Pose(axle, f, pitch);
    }

    /// <summary>Наклон вперёд сейчас, градусы (по повороту тела).</summary>
    private float CurrentPitch()
    {
        Vector3 f = _rb.rotation * _fwdL;
        return -Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
    }

    /// <summary>Поставить тачку: ось в точке axle, курс fwd, наклон вперёд pitch (через колесо).</summary>
    private void Pose(Vector3 axle, Vector3 fwd, float pitch)
    {
        Quaternion baseRot = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(_fwdL, Vector3.up));
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Quaternion rot = Quaternion.AngleAxis(pitch, right) * baseRot;
        _rb.MoveRotation(rot);
        _rb.MovePosition(axle - rot * _axleOffset);
    }

    // ───────────────────────── земля и стены ─────────────────────────

    private bool Skip(Collider c, Transform player)
    {
        if (c == null || c.transform.IsChildOf(transform)) return true;
        if (player != null && c.transform.IsChildOf(player)) return true;
        Rigidbody rb = c.attachedRigidbody;
        if (rb != null && !rb.isKinematic) return true;                 // продукты, вёдра, инструменты — не земля
        if (c.GetComponentInParent<FirstPersonController>() != null) return true;
        return false;
    }

    /// <summary>Пол под точкой: самая высокая поверхность не выше maxY.</summary>
    private bool Ground(Vector3 at, float maxY, Transform player, out float y)
    {
        y = 0f;
        Vector3 from = new Vector3(at.x, maxY + 0.05f, at.z);
        int n = Physics.RaycastNonAlloc(from, Vector3.down, _hits, 6f, _groundMask, QueryTriggerInteraction.Ignore);
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            if (Skip(_hits[i].collider, player)) continue;
            if (_hits[i].normal.y < 0.5f) continue;                     // стенка, а не пол
            if (!found || _hits[i].point.y > y) { y = _hits[i].point.y; found = true; }
        }
        return found;
    }

    /// <summary>Колесо упирается в стены: дальше препятствия не едем.</summary>
    private Vector3 Block(Vector3 cur, Vector3 next, Transform player)
    {
        Vector3 d = next - cur; d.y = 0f;
        float dist = d.magnitude;
        if (dist < 1e-4f) return next;
        Vector3 dir = d / dist;
        float r = Mathf.Max(0.05f, Mathf.Min(_radius, _halfWidth) * 0.8f);
        Vector3 from = new Vector3(cur.x, _groundY + _maxStep + r + 0.02f, cur.z) - dir * 0.02f;
        int n = Physics.SphereCastNonAlloc(from, r, dir, _hits, dist + 0.04f, _groundMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            if (Skip(_hits[i].collider, player)) continue;
            if (_hits[i].distance <= 0f) continue;                      // уже внутри — не держим (не залипать)
            if (_hits[i].normal.y > 0.6f) continue;                     // пол/пандус
            if (_hits[i].distance < best) best = _hits[i].distance;
        }
        if (best == float.MaxValue) return next;
        float go = Mathf.Clamp(best - 0.04f, 0f, dist);
        return new Vector3(cur.x, next.y, cur.z) + dir * go;
    }

    // ───────────────────────── колёса ─────────────────────────

    private void SpinWheels()
    {
        if (_wheelR == null || _wheelR.Length == 0 || _rb == null) return;
        Vector3 axle = transform.position + transform.rotation * _axleOffset;
        if (!_lastAxleOk) { _lastAxle = axle; _lastAxleOk = true; return; }
        Vector3 f = transform.rotation * _fwdL; f.y = 0f;
        if (f.sqrMagnitude < 1e-4f) { _lastAxle = axle; return; }
        f.Normalize();
        float dist = Vector3.Dot(axle - _lastAxle, f);
        _lastAxle = axle;
        if (Mathf.Abs(dist) < 1e-5f || Mathf.Abs(dist) > 2f) return;
        float deg = dist / Mathf.Max(0.02f, _radius) * Mathf.Rad2Deg;
        Vector3 right = Vector3.Cross(Vector3.up, f);
        foreach (Renderer r in _wheelR)
            if (r != null) r.transform.RotateAround(r.bounds.center, right, deg);
    }

    private void OnGUI()
    {
        if (!_hint || !_isLocalGripper) return;
        GUIStyle st = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
        string fill = _cookware != null && _cookware.Capacity > 0 ? $"   заполнено {_cookware.TotalCount * 100 / _cookware.Capacity}%" : "";
        GUI.Box(new Rect(Screen.width * 0.5f - 250f, Screen.height - 70f, 500f, 28f),
                $"Тачка: F — отпустить · ПКМ (держать) — завалить вперёд{fill}", st);
    }
}
