using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// РАСКЛАДНАЯ ЛЕСТНИЦА ИЗ ДВУХ ПОЛОВИН НА ШАРНИРЕ.
///
///   F   — взять (и снять поставленную)
///   G   — бросить
///   ПКМ — сложить пополам / разложить в одну прямую
///   ЛКМ — поставить: нога встаёт туда, куда смотришь, лестница ПАДАЕТ ВПЕРЁД,
///         пока не упрётся. Круто (> 45°) — лезут, полого/плашмя — идут, мост.
///
/// ═══ ИЕРАРХИЯ (v2: шарнир — настоящий объект) ═══
///   FoldingLadder            ← корень: NetworkObject, NetworkTransform, Rigidbody, этот скрипт
///   ├─ ladder1               ← половина A (не двигается)
///   │   └─ AutoCollider      ← создаёт кнопка «2) Собрать коллайдеры»
///   └─ Hinge                 ← ШАРНИР: пустышка в месте соединения, красная ось X — ось складывания
///       └─ ladder2           ← половина B, вложена в шарнир и крутится вместе с ним
///           └─ AutoCollider
///
/// Всё собирается кнопками ⋮ у компонента (сначала распаковать префаб модели:
/// ПКМ по корню → Prefab → Unpack Completely):
///   1) Создать шарнир и вложить ladder2
///   2) Собрать коллайдеры под углом
///   Предпросмотр: сложена / разложена / как в модели — смотреть, правильно ли гнётся.
///
/// Углы: по умолчанию считаются сами (Auto Angles). Выключи — и задай Folded Turn /
/// Unfolded Turn руками: на сколько градусов повернуть шарнир от позы модели. Их можно
/// крутить прямо в Play — лестница сразу перегибается.
/// </summary>
public class FoldingLadderItem : InteractableItem, IClimbable
{
    private enum Mode : byte { Free = 0, Falling = 1, Planted = 2 }
    public enum Axis : byte { X = 0, Y = 1, Z = 2 }

    [Header("Половины и шарнир")]
    [Tooltip("Первая половина (ladder1) — не двигается. Пусто — первый ребёнок с мешем.")]
    [SerializeField] private Transform _sideA;
    [Tooltip("Вторая половина (ladder2) — должна лежать ВНУТРИ шарнира (Hinge).")]
    [SerializeField] private Transform _sideB;
    [Tooltip("Шарнир — пустышка в месте соединения половин, ladder2 — её ребёнок. " +
             "Создаётся кнопкой ⋮ → «1) Создать шарнир и вложить ladder2».")]
    [SerializeField] private Transform _hinge;
    [Tooltip("Вокруг какой СВОЕЙ оси крутится шарнир. Созданный кнопкой — X (красная).")]
    [SerializeField] private Axis _hingeAxis = Axis.X;

    [Header("Складывание (ПКМ)")]
    [Tooltip("Считать углы самим: сложенная — половины почти вместе, разложенная — в одну прямую")]
    [SerializeField] private bool _autoAngles = true;
    [Tooltip("Авто: угол между половинами в сложенной, градусы (0 — совпадают, могут влезть друг в друга)")]
    [Range(0f, 40f)][SerializeField] private float _foldedGap = 5f;
    [Tooltip("Авто: угол между половинами в разложенной, градусы (180 — прямая)")]
    [Range(90f, 200f)][SerializeField] private float _unfoldedAngle = 180f;
    [Tooltip("Ручной режим: поворот шарнира от позы модели для СЛОЖЕННОЙ, градусы")]
    [SerializeField] private float _foldedTurn = -20f;
    [Tooltip("Ручной режим: поворот шарнира от позы модели для РАЗЛОЖЕННОЙ, градусы")]
    [SerializeField] private float _unfoldedTurn = 160f;
    [Tooltip("Секунд на складывание/раскладывание")]
    [Min(0.05f)][SerializeField] private float _foldSeconds = 0.7f;

    [Header("В руках")]
    [SerializeField] private float _carryForward = 0.9f;
    [SerializeField] private float _carrySide = 0.45f;
    [Range(0f, 30f)][SerializeField] private float _carryLean = 8f;
    [Range(0.2f, 1f)][SerializeField] private float _carrySpeed = 0.75f;

    [Header("Постановка (ЛКМ)")]
    [SerializeField] private float _placeReach = 3.5f;
    [SerializeField] private float _placeForward = 1.0f;
    [Tooltip("Слои, на которые ставится и обо что упирается при падении")]
    [SerializeField] private LayerMask _groundMask = ~0;
    [Tooltip("Наибольший угол падения от вертикали. 90 — плашмя; больше — краем вниз через яму")]
    [Range(30f, 150f)][SerializeField] private float _maxFallAngle = 130f;
    [SerializeField] private float _fallAccel = 520f;
    [Tooltip("Низ лестницы от ноги, который не проверяется на упор, м")]
    [SerializeField] private float _footSkip = 0.35f;

    [Header("Коллайдеры (кнопка «2) Собрать коллайдеры»)")]
    [Tooltip("Наименьшая толщина авто-коллайдера, м — чтобы по мосту не проваливаться")]
    [SerializeField] private float _minColliderThickness = 0.06f;
    [Tooltip("Толщина авто-коллайдера, если меш модели не читается (Read/Write выкл), м")]
    [SerializeField] private float _colliderThickness = 0.1f;

    [Header("Лазание")]
    [Range(10f, 85f)][SerializeField] private float _climbAboveDegrees = 45f;
    [SerializeField] private float _zoneWidth = 0.9f;
    [SerializeField] private float _zoneThickness = 0.8f;
    [SerializeField] private float _topOverhang = 0.6f;

    [Header("Отладка")]
    [SerializeField] private bool _log = true;
    [SerializeField] private bool _debugGizmo = true;

    [SerializeField, HideInInspector] private bool _restCaptured;
    [SerializeField, HideInInspector] private Quaternion _hingeRest = Quaternion.identity;

    // ───────── сеть ─────────
    private readonly SyncVar<bool> _unfolded = new(false);
    private readonly SyncVar<byte> _mode = new(0);
    private readonly SyncVar<NetworkObject> _holder = new(null);

    // ───────── геометрия ─────────
    private bool _measured;
    private Vector3 _hingeL, _footAL, _axisL;     // в осях корня
    private Vector3 _footBH;                       // нога B в осях шарнира
    private float _width = 0.5f, _thick = 0.12f;
    private float _turnNow, _turnFolded, _turnUnfolded;

    // ───────── падение (сервер) ─────────
    private Vector3 _fallP, _fallFwd, _fallRight;
    private float _fallTheta, _fallOmega, _fallTarget;

    private Collider[] _solid;
    private BoxCollider _zone;
    private static PhysicsMaterial _grip;
    private readonly RaycastHit[] _hits = new RaycastHit[16];
    private readonly Collider[] _over = new Collider[32];

    public bool IsUnfolded => _unfolded.Value;
    public bool AnyoneClimbing { get; set; }

    // ───────────────────────── ЖИЗНЕННЫЙ ЦИКЛ ─────────────────────────

    protected override void Awake()
    {
        base.Awake();
        ResolveSides();
        Measure(true);
        CacheSolidColliders();
        BuildZone();
        _turnNow = _turnFolded;
        ApplyTurn(_turnNow);
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _holder.OnChange += OnHolderChanged;
        _mode.OnChange += OnModeChanged;
        _turnNow = _unfolded.Value ? _turnUnfolded : _turnFolded;
        ApplyTurn(_turnNow);
        SetSolid(_holder.Value == null);
        ApplyModePhysics((Mode)_mode.Value);
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _holder.OnChange -= OnHolderChanged;
        _mode.OnChange -= OnModeChanged;
    }

    private void Update()
    {
        // Ручные углы можно крутить прямо в Play.
        if (_measured && !_autoAngles) { _turnFolded = _foldedTurn; _turnUnfolded = _unfoldedTurn; }

        float target = _unfolded.Value ? _turnUnfolded : _turnFolded;
        if (!Mathf.Approximately(_turnNow, target))
        {
            float speed = Mathf.Max(1f, Mathf.Abs(_turnUnfolded - _turnFolded)) / _foldSeconds;
            _turnNow = Mathf.MoveTowards(_turnNow, target, speed * Time.deltaTime);
            ApplyTurn(_turnNow);
        }

        if (base.IsServerInitialized && (Mode)_mode.Value == Mode.Falling) ServerFallTick(Time.deltaTime);
    }

    private void LateUpdate() => UpdateZone();

    // ───────────────────────── ГЕОМЕТРИЯ ─────────────────────────

    private Vector3 LocalAxis => _hingeAxis == Axis.X ? Vector3.right : _hingeAxis == Axis.Y ? Vector3.up : Vector3.forward;

    private void ResolveSides()
    {
        if (_hinge != null && _sideB == null)
            foreach (Transform c in _hinge)
                if (c.GetComponentInChildren<Renderer>(true) != null) { _sideB = c; break; }

        if (_sideA != null && _sideB != null) return;
        var found = new List<Transform>();
        foreach (Transform c in transform)
            if (c != _sideA && c != _sideB && c != _hinge && c.name != "ClimbZone" &&
                c.GetComponentInChildren<Renderer>(true) != null) found.Add(c);
        if (_sideA == null && found.Count > 0) { _sideA = found[0]; found.RemoveAt(0); }
        if (_sideB == null && found.Count > 0) _sideB = found[0];
    }

    /// <summary>Точки меша половины в мире: вершины (если меш читается) или углы габарита.</summary>
    private static List<Vector3> WorldPoints(Transform side)
    {
        var pts = new List<Vector3>();
        if (side == null) return pts;
        foreach (MeshFilter mf in side.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh m = mf.sharedMesh;
            if (m == null) continue;
            Vector3[] v = null;
            if (m.isReadable) { try { v = m.vertices; } catch { v = null; } }
            if (v != null && v.Length > 0)
            {
                int step = Mathf.Max(1, v.Length / 4000);
                for (int i = 0; i < v.Length; i += step) pts.Add(mf.transform.TransformPoint(v[i]));
            }
            else
            {
                Bounds b = m.bounds;
                for (int i = 0; i < 8; i++)
                    pts.Add(mf.transform.TransformPoint(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            }
        }
        return pts;
    }

    private static Vector3 BoxCenter(List<Vector3> pts)
    {
        if (pts.Count == 0) return Vector3.zero;
        Vector3 min = pts[0], max = pts[0];
        foreach (Vector3 p in pts) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        return (min + max) * 0.5f;
    }

    private bool Measure(bool log)
    {
        _measured = false;
        if (_sideA == null || _sideB == null || _hinge == null)
        {
            if (log && Application.isPlaying)
                Debug.LogError($"[Раскладная лестница] «{name}»: нет " +
                               (_hinge == null ? "шарнира (Hinge) — ⋮ → «1) Создать шарнир и вложить ladder2»"
                                               : "половин ladder1/ladder2"), this);
            return false;
        }
        if (!_sideB.IsChildOf(_hinge))
        {
            if (log && Application.isPlaying)
                Debug.LogError($"[Раскладная лестница] «{name}»: ladder2 не вложен в Hinge — складываться не будет", this);
            return false;
        }

        // Меряем в позе модели.
        if (!_restCaptured) { _hingeRest = _hinge.localRotation; _restCaptured = true; }
        // Трогаем трансформ, только если шарнир сейчас не в позе модели (иначе сцена «грязнеет» от гизмо).
        Quaternion keep = _hinge.localRotation;
        bool swap = Quaternion.Angle(keep, _hingeRest) > 0.01f;
        if (swap) _hinge.localRotation = _hingeRest;

        List<Vector3> a = WorldPoints(_sideA), b = WorldPoints(_sideB);
        if (a.Count == 0 || b.Count == 0) { if (swap) _hinge.localRotation = keep; return false; }

        Vector3 hW = _hinge.position;
        Vector3 cA = BoxCenter(a), cB = BoxCenter(b);
        Vector3 footAW = 2f * cA - hW, footBW = 2f * cB - hW;
        Vector3 axisW = _hinge.TransformDirection(LocalAxis).normalized;

        _hingeL = transform.InverseTransformPoint(hW);
        _footAL = transform.InverseTransformPoint(footAW);
        _axisL = transform.InverseTransformDirection(axisW).normalized;
        _footBH = _hinge.InverseTransformPoint(footBW);

        // Ширина вдоль оси шарнира, толщина поперёк — по половине A.
        Vector3 u = (hW - footAW).normalized, t = Vector3.Cross(axisW, u).normalized;
        float wMin = float.MaxValue, wMax = float.MinValue, tMin = float.MaxValue, tMax = float.MinValue;
        foreach (Vector3 p in a)
        {
            float w = Vector3.Dot(p, axisW), th = Vector3.Dot(p, t);
            wMin = Mathf.Min(wMin, w); wMax = Mathf.Max(wMax, w);
            tMin = Mathf.Min(tMin, th); tMax = Mathf.Max(tMax, th);
        }
        _width = Mathf.Clamp(wMax - wMin, 0.2f, 1.5f);
        _thick = Mathf.Clamp(tMax - tMin, 0.04f, 0.3f);

        // Углы: в какую сторону поворот шарнира раскрывает половины.
        Vector3 dA = footAW - hW, dB = footBW - hW;
        float angle0 = Vector3.Angle(dA, dB);
        float sign = Vector3.Angle(dA, Quaternion.AngleAxis(2f, axisW) * dB) > angle0 ? 1f : -1f;
        if (_autoAngles)
        {
            _turnFolded = sign * (_foldedGap - angle0);
            _turnUnfolded = sign * (_unfoldedAngle - angle0);
            _foldedTurn = _turnFolded;           // видно в инспекторе — можно перейти на ручной
            _unfoldedTurn = _turnUnfolded;
        }
        else { _turnFolded = _foldedTurn; _turnUnfolded = _unfoldedTurn; }

        if (swap) _hinge.localRotation = keep;
        _measured = true;

        if (log && _log)
            Debug.Log($"[Раскладная лестница] «{name}»: половина {LengthA:0.00} м, угол в модели {angle0:0}°, " +
                      $"сложить {_turnFolded:0}°, разложить {_turnUnfolded:0}°, ширина {_width:0.00} м", this);
        return true;
    }

    private float LengthA => transform.TransformVector(_hingeL - _footAL).magnitude;
    private float LengthB => _hinge != null ? _hinge.TransformVector(_footBH).magnitude : 0f;

    private void ApplyTurn(float turn)
    {
        if (!_measured || _hinge == null) return;
        _hinge.localRotation = _hingeRest * Quaternion.AngleAxis(turn, LocalAxis);
    }

    private Vector3 HingeW => transform.TransformPoint(_hingeL);
    private Vector3 FootAW => transform.TransformPoint(_footAL);
    private Vector3 FootBW => _hinge.TransformPoint(_footBH);

    /// <summary>Поворот корня: «нога A → шарнир» по dir, ось шарнира — по right.</summary>
    private Quaternion RootRotation(Vector3 dir, Vector3 right)
    {
        Vector3 u = (_hingeL - _footAL).normalized;
        Vector3 w = Vector3.ProjectOnPlane(_axisL, u).normalized;
        Quaternion local = Quaternion.LookRotation(w, u);
        Quaternion world = Quaternion.LookRotation(right, dir);
        return world * Quaternion.Inverse(local);
    }

    private Vector3 RootPosition(Vector3 footWorld, Quaternion rot) =>
        footWorld - rot * Vector3.Scale(transform.lossyScale, _footAL);

    // ───────────────────────── ЛАЗАНИЕ ─────────────────────────

    private void Ends(out Vector3 bottom, out Vector3 top)
    {
        Vector3 a = FootAW, h = HingeW, b = FootBW;
        Vector3 t = Vector3.Angle(a - h, b - h) >= 90f ? b : h;   // раскрыта — до ноги B, сложена — до шарнира
        if (a.y <= t.y) { bottom = a; top = t; } else { bottom = t; top = a; }
    }

    public Vector3 ClimbBase { get { if (!_measured) return transform.position; Ends(out Vector3 b, out _); return b; } }
    public Vector3 ClimbAxis
    {
        get
        {
            if (!_measured) return Vector3.up;
            Ends(out Vector3 b, out Vector3 t);
            Vector3 d = t - b;
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.up;
        }
    }
    public float CurrentLength { get { if (!_measured) return 0f; Ends(out Vector3 b, out Vector3 t); return (t - b).magnitude; } }
    public float AngleFromGround => 90f - Vector3.Angle(ClimbAxis, Vector3.up);

    public bool CanClimb(NetworkObject player)
    {
        if (!_measured || _holder.Value != null) return false;
        if ((Mode)_mode.Value == Mode.Falling) return false;
        return AngleFromGround > _climbAboveDegrees;
    }

    private void BuildZone()
    {
        Transform z = transform.Find("ClimbZone");
        if (z == null)
        {
            var go = new GameObject("ClimbZone");
            go.layer = 2;   // Ignore Raycast
            z = go.transform;
            z.SetParent(transform, false);
        }
        _zone = z.GetComponent<BoxCollider>();
        if (_zone == null) _zone = z.gameObject.AddComponent<BoxCollider>();
        _zone.isTrigger = true;
        if (z.GetComponent<LadderClimbZone>() == null) z.gameObject.AddComponent<LadderClimbZone>();
    }

    private void UpdateZone()
    {
        if (_zone == null || !_measured) return;
        Ends(out Vector3 b, out Vector3 t);
        Vector3 ax = t - b;
        float len = ax.magnitude;
        if (len < 0.01f) return;
        ax /= len;
        Vector3 right = transform.TransformDirection(_axisL);
        Vector3 fwd = Vector3.Cross(right, ax);
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(Vector3.forward, ax);

        Transform z = _zone.transform;
        z.SetPositionAndRotation(b, Quaternion.LookRotation(fwd.normalized, ax));
        Vector3 ls = transform.lossyScale;
        z.localScale = new Vector3(1f / Mathf.Max(1e-5f, Mathf.Abs(ls.x)), 1f / Mathf.Max(1e-5f, Mathf.Abs(ls.y)),
                                   1f / Mathf.Max(1e-5f, Mathf.Abs(ls.z)));
        float h = len + _topOverhang;
        _zone.size = new Vector3(_zoneWidth, h, _zoneThickness);
        _zone.center = new Vector3(0f, h * 0.5f, 0f);
    }

    // ───────────────────────── ПКМ / ЛКМ ─────────────────────────

    public override void ServerSecondaryUse(UseContext ctx)
    {
        if (ctx.Hand == null || ctx.Hand.HeldItem != this) return;
        _unfolded.Value = !_unfolded.Value;
        if (_log) Debug.Log($"[Раскладная лестница] «{name}»: {(_unfolded.Value ? "разложена" : "сложена")}", this);
    }

    public override void ServerPrimaryUse(UseContext ctx)
    {
        if (ctx.Hand == null || ctx.Hand.HeldItem != this || !_measured) return;

        Vector3 fwd = ctx.Direction; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = ctx.Hand.transform.forward;
        fwd.y = 0f; fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        if (!FindPlaceSpot(ctx, fwd, out Vector3 spot))
        {
            if (_log) Debug.Log($"[Раскладная лестница] «{name}»: некуда поставить — нет пола", this);
            return;
        }

        _turnNow = _unfolded.Value ? _turnUnfolded : _turnFolded;
        ApplyTurn(_turnNow);
        float len = _unfolded.Value ? LengthA + LengthB : LengthA;
        float rest = RestAngle(spot, fwd, right, len);

        ctx.Hand.ServerDrop(Vector3.zero);

        _fallP = spot; _fallFwd = fwd; _fallRight = right;
        _fallTheta = 0f; _fallOmega = 20f; _fallTarget = rest;
        SetFallPose(0f);
        _mode.Value = (byte)Mode.Falling;
        ApplyModePhysics(Mode.Falling);

        if (_log)
            Debug.Log($"[Раскладная лестница] «{name}»: поставлена, упадёт на {rest:0}° от вертикали " +
                      (rest >= 85f ? "(мост / лежит)" : rest > 90f - _climbAboveDegrees ? "(полого — идти ногами)" : "(круто — можно лезть)"), this);
    }

    private bool FindPlaceSpot(UseContext ctx, Vector3 fwd, out Vector3 spot)
    {
        spot = default(Vector3);
        int n = Physics.RaycastNonAlloc(ctx.Origin, ctx.Direction, _hits, _placeReach, _groundMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = _hits[i];
            if (h.collider == null || Ignored(h.collider) || h.normal.y < 0.5f) continue;
            if (h.distance < best) { best = h.distance; spot = h.point; }
        }
        if (best < float.MaxValue) return true;

        Vector3 p = ctx.Hand.transform.position + fwd * _placeForward;
        if (!Ground(p + Vector3.up * 1.2f, 4f, out float y)) return false;
        spot = new Vector3(p.x, y, p.z);
        return true;
    }

    private bool Ignored(Collider c)
    {
        if (c.transform.IsChildOf(transform)) return true;
        if (c is CharacterController) return true;
        if (c.GetComponentInParent<FirstPersonController>() != null) return true;
        Rigidbody rb = c.attachedRigidbody;
        return rb != null && !rb.isKinematic;
    }

    private bool Ground(Vector3 from, float dist, out float y)
    {
        y = 0f;
        int n = Physics.RaycastNonAlloc(from, Vector3.down, _hits, dist, _groundMask, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            if (_hits[i].collider == null || Ignored(_hits[i].collider)) continue;
            if (_hits[i].point.y > best) best = _hits[i].point.y;
        }
        if (float.IsNegativeInfinity(best)) return false;
        y = best;
        return true;
    }

    private static Vector3 FallDir(float thetaDeg, Vector3 fwd)
    {
        float r = thetaDeg * Mathf.Deg2Rad;
        return Vector3.up * Mathf.Cos(r) + fwd * Mathf.Sin(r);
    }

    private float RestAngle(Vector3 foot, Vector3 fwd, Vector3 right, float len)
    {
        float body = Mathf.Max(0.1f, len - _footSkip);
        Vector3 half = new Vector3(_width * 0.5f, body * 0.5f, _thick * 0.5f);
        Vector3 lift = Vector3.up * (_thick * 0.5f + 0.03f);
        for (float th = 0f; th <= _maxFallAngle; th += 1f)
        {
            Vector3 d = FallDir(th, fwd);
            Vector3 center = foot + d * (_footSkip + body * 0.5f) + lift;
            Quaternion q = Quaternion.LookRotation(Vector3.Cross(right, d), d);
            int n = Physics.OverlapBoxNonAlloc(center, half, _over, q, _groundMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (_over[i] != null && !Ignored(_over[i])) return Mathf.Max(0f, th - 1f);
        }
        return _maxFallAngle;
    }

    private void SetFallPose(float theta)
    {
        Quaternion rot = RootRotation(FallDir(theta, _fallFwd), _fallRight);
        transform.SetPositionAndRotation(RootPosition(_fallP, rot), rot);
    }

    private void ServerFallTick(float dt)
    {
        _fallOmega += _fallAccel * Mathf.Max(0.15f, Mathf.Sin(_fallTheta * Mathf.Deg2Rad)) * dt;
        _fallTheta += _fallOmega * dt;
        bool done = _fallTheta >= _fallTarget;
        if (done) _fallTheta = _fallTarget;
        SetFallPose(_fallTheta);
        if (!done) return;
        _mode.Value = (byte)Mode.Planted;
        ApplyModePhysics(Mode.Planted);
        if (_log) Debug.Log($"[Раскладная лестница] «{name}»: встала, наклон {AngleFromGround:0}° от земли", this);
    }

    // ───────────────────────── F / руки ─────────────────────────

    public override void ServerInteract(PlayerInteractor interactor)
    {
        if ((Mode)_mode.Value == Mode.Falling) return;
        if (AnyoneClimbing)
        {
            if (_log) Debug.Log($"[Раскладная лестница] «{name}»: на ней кто-то есть — не снимаю", this);
            return;
        }
        base.ServerInteract(interactor);
    }

    public override void ServerOnPickup(PlayerHand hand)
    {
        _mode.Value = (byte)Mode.Free;
        base.ServerOnPickup(hand);
        _holder.Value = hand.NetworkObject;
    }

    public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
    {
        base.ServerOnDrop(hand, tossDirection, force);
        _holder.Value = null;
    }

    private void OnModeChanged(byte prev, byte next, bool asServer)
    {
        if (asServer && base.IsClientInitialized) return;
        ApplyModePhysics((Mode)next);
    }

    private void ApplyModePhysics(Mode m)
    {
        if (_rb == null || _holder.Value != null) return;
        bool frozen = m != Mode.Free;
        if (frozen && !_rb.isKinematic) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
        _rb.isKinematic = frozen;
        _rb.useGravity = !frozen;
    }

    private void CacheSolidColliders()
    {
        var list = new List<Collider>();
        foreach (Collider c in GetComponentsInChildren<Collider>(true))
            if (c != null && !c.isTrigger && c.enabled) list.Add(c);
        _solid = list.ToArray();
        if (_solid.Length == 0)
        {
            Debug.LogWarning($"[Раскладная лестница] «{name}»: нет коллайдеров — ⋮ → «2) Собрать коллайдеры под углом»", this);
            return;
        }
        if (_grip == null)
            _grip = new PhysicsMaterial("LadderGrip")
            {
                dynamicFriction = 1f, staticFriction = 1f, bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Maximum, bounceCombine = PhysicsMaterialCombine.Minimum
            };
        foreach (Collider c in _solid) if (c.sharedMaterial == null) c.sharedMaterial = _grip;
    }

    private void SetSolid(bool on)
    {
        if (_solid == null) return;
        foreach (Collider c in _solid) if (c != null) c.enabled = on;
    }

    private void OnHolderChanged(NetworkObject prev, NetworkObject next, bool asServer)
    {
        if (asServer && base.IsClientInitialized) return;
        SetSolid(next == null);
        if (next == null && prev != null) StartCoroutine(IgnoreDropper(prev));
    }

    private IEnumerator IgnoreDropper(NetworkObject dropper)
    {
        if (dropper == null || _solid == null) yield break;
        Collider body = dropper.GetComponent<CharacterController>();
        if (body == null) body = dropper.GetComponentInChildren<Collider>();
        if (body == null) yield break;
        foreach (Collider c in _solid) if (c != null) Physics.IgnoreCollision(c, body, true);
        yield return new WaitForSeconds(0.5f);
        foreach (Collider c in _solid) if (c != null && body != null) Physics.IgnoreCollision(c, body, false);
    }

    public override bool FollowHand => false;
    public override float CarrySpeedMultiplier => _carrySpeed;

    public override void UpdateHeldPose(Transform handAnchor)
    {
        FirstPersonController fpc = handAnchor.GetComponentInParent<FirstPersonController>();
        if (fpc != null) fpc.SpeedMultiplier = _carrySpeed;
        if (!_measured) { transform.SetPositionAndRotation(handAnchor.position, handAnchor.rotation); return; }

        Vector3 fwd = handAnchor.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 foot = handAnchor.position + fwd * _carryForward + right * _carrySide;
        foot.y = Ground(foot + Vector3.up * 1.5f, 5f, out float y) ? y : handAnchor.position.y - 1.4f;
        Vector3 dir = Quaternion.AngleAxis(-_carryLean, right) * Vector3.up;
        Quaternion rot = RootRotation(dir, right);
        transform.SetPositionAndRotation(RootPosition(foot, rot), rot);
    }

    // ───────────────────────── КНОПКИ В РЕДАКТОРЕ ─────────────────────────

#if UNITY_EDITOR
    private bool BlockedByPrefab()
    {
        if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(gameObject))
        {
            Debug.LogError($"[Раскладная лестница] «{name}» — экземпляр префаба/модели: Unity не даст переложить ladder2. " +
                           "ПКМ по корню → Prefab → Unpack Completely (или открой префаб двойным кликом) и жми кнопку ещё раз.", this);
            return true;
        }
        return false;
    }

    [ContextMenu("1) Создать шарнир и вложить ladder2")]
    private void EditorCreateHinge()
    {
        ResolveSides();
        if (_sideA == null || _sideB == null) { Debug.LogError("[Раскладная лестница] не найдены ladder1 / ladder2", this); return; }
        if (BlockedByPrefab()) return;

        // Был шарнир (в том числе неправильный) — вернуть ladder2 на место и убрать его.
        if (_hinge != null)
        {
            if (_restCaptured) { UnityEditor.Undo.RecordObject(_hinge, "Hinge rest"); _hinge.localRotation = _hingeRest; }
            if (_sideB.IsChildOf(_hinge)) UnityEditor.Undo.SetTransformParent(_sideB, transform, "ladder2 → root");
            if (_hinge.childCount == 0 && _hinge != transform) UnityEditor.Undo.DestroyObjectImmediate(_hinge.gameObject);
            _hinge = null;
        }
        foreach (Transform c in transform)
            if (c.name == "Hinge" && c.childCount == 0) { UnityEditor.Undo.DestroyObjectImmediate(c.gameObject); break; }

        List<Vector3> a = WorldPoints(_sideA), b = WorldPoints(_sideB);
        if (a.Count == 0 || b.Count == 0) return;

        // Лестница стоит буквой «А»: шарнир — наверху посередине.
        var all = new List<Vector3>(a); all.AddRange(b);
        Vector3 min = all[0], max = all[0];
        foreach (Vector3 p in all) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        Vector3 hingePos = new Vector3((min.x + max.x) * 0.5f, max.y - (max.y - min.y) * 0.02f, (min.z + max.z) * 0.5f);

        Vector3 cA = BoxCenter(a), cB = BoxCenter(b);
        Vector3 footA = 2f * cA - hingePos, footB = 2f * cB - hingePos;
        if (footA.y > hingePos.y - (max.y - min.y) * 0.5f || footB.y > hingePos.y - (max.y - min.y) * 0.5f)
            Debug.LogWarning("[Раскладная лестница] лестница должна СТОЯТЬ буквой «А» (ноги внизу) — поставь ровно и нажми ещё раз", this);

        Vector3 axis = Vector3.Cross(footA - hingePos, footB - hingePos);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(footA - hingePos, Vector3.forward);
        axis.Normalize();
        Vector3 up = Vector3.ProjectOnPlane(hingePos - footA, axis).normalized;

        var go = new GameObject("Hinge");
        UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Hinge");
        go.transform.SetParent(transform, false);
        go.transform.SetPositionAndRotation(hingePos, Quaternion.LookRotation(Vector3.Cross(axis, up), up));
        UnityEditor.Undo.SetTransformParent(_sideB, go.transform, "ladder2 → Hinge");

        UnityEditor.Undo.RecordObject(this, "Hinge");
        _hinge = go.transform;
        _hingeAxis = Axis.X;
        _hingeRest = _hinge.localRotation;
        _restCaptured = true;
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log("[Раскладная лестница] Hinge — наверху, в месте соединения; ladder2 вложен в него. " +
                  "Проверь: ⋮ → «Предпросмотр: разложена».", this);
    }

    [ContextMenu("2) Собрать коллайдеры под углом")]
    private void EditorBuildColliders()
    {
        ResolveSides();
        if (_sideA == null || _sideB == null) { Debug.LogError("[Раскладная лестница] не найдены ladder1 / ladder2", this); return; }
        if (_hinge == null) { Debug.LogError("[Раскладная лестница] сначала ⋮ → «1) Создать шарнир и вложить ladder2»", this); return; }

        // Мерить в позе модели.
        if (_restCaptured && Quaternion.Angle(_hinge.localRotation, _hingeRest) > 0.01f)
        { UnityEditor.Undo.RecordObject(_hinge, "Hinge rest"); _hinge.localRotation = _hingeRest; }

        Vector3 axis = _hinge.TransformDirection(LocalAxis).normalized;
        int made = 0;
        foreach (Transform side in new[] { _sideA, _sideB })
            if (BuildSideCollider(side, _hinge.position, axis)) made++;
        Debug.Log($"[Раскладная лестница] коллайдеров собрано: {made} — по одному на половину, вдоль неё " +
                  "(дочерние AutoCollider; старые коллайдеры половин выключены)", this);
    }

    private bool BuildSideCollider(Transform side, Vector3 hinge, Vector3 axis)
    {
        Transform old = side.Find("AutoCollider");
        if (old != null) UnityEditor.Undo.DestroyObjectImmediate(old.gameObject);

        List<Vector3> pts = WorldPoints(side);
        if (pts.Count == 0) return false;

        // Половина — от шарнира до ноги (нога = зеркало шарнира через центр половины).
        Vector3 c = BoxCenter(pts);
        Vector3 foot = 2f * c - hinge;
        Vector3 u = hinge - foot;
        float len = u.magnitude;
        if (len < 0.05f) return false;
        u /= len;
        Vector3 w = Vector3.ProjectOnPlane(axis, u).normalized;
        Vector3 t = Vector3.Cross(w, u).normalized;

        // Ширина — по точкам вдоль оси шарнира; толщина — по вершинам, если меш читается, иначе поле.
        float wMin = float.MaxValue, wMax = float.MinValue, tMin = float.MaxValue, tMax = float.MinValue;
        foreach (Vector3 p in pts)
        {
            Vector3 d = p - c;
            wMin = Mathf.Min(wMin, Vector3.Dot(d, w)); wMax = Mathf.Max(wMax, Vector3.Dot(d, w));
            tMin = Mathf.Min(tMin, Vector3.Dot(d, t)); tMax = Mathf.Max(tMax, Vector3.Dot(d, t));
        }
        bool exact = pts.Count > 8 * side.GetComponentsInChildren<MeshFilter>(true).Length;
        float width = Mathf.Clamp(wMax - wMin, 0.1f, 2f);
        float thick = exact ? Mathf.Max(_minColliderThickness, tMax - tMin) : _colliderThickness;
        Vector3 center = c + w * ((wMin + wMax) * 0.5f) + (exact ? t * ((tMin + tMax) * 0.5f) : Vector3.zero);

        foreach (Collider oc in side.GetComponentsInChildren<Collider>(true))
            if (!oc.isTrigger && oc.enabled) { UnityEditor.Undo.RecordObject(oc, "Collider off"); oc.enabled = false; }

        var go = new GameObject("AutoCollider");
        UnityEditor.Undo.RegisterCreatedObjectUndo(go, "AutoCollider");
        go.layer = side.gameObject.layer;
        go.transform.SetParent(side, false);
        // Y коробки — вдоль половины (от ноги к шарниру), X — ширина, Z — толщина.
        go.transform.SetPositionAndRotation(center, Quaternion.LookRotation(t, u));
        Vector3 ps = side.lossyScale;
        go.transform.localScale = new Vector3(1f / Mathf.Max(1e-5f, Mathf.Abs(ps.x)), 1f / Mathf.Max(1e-5f, Mathf.Abs(ps.y)),
                                              1f / Mathf.Max(1e-5f, Mathf.Abs(ps.z)));
        Vector3 ls = go.transform.lossyScale;
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = new Vector3(width / Mathf.Max(1e-5f, Mathf.Abs(ls.x)), len / Mathf.Max(1e-5f, Mathf.Abs(ls.y)),
                               thick / Mathf.Max(1e-5f, Mathf.Abs(ls.z)));
        return true;
    }

    [ContextMenu("Предпросмотр: сложена")]
    private void EditorPreviewFolded() => EditorPreview(false);

    [ContextMenu("Предпросмотр: разложена")]
    private void EditorPreviewUnfolded() => EditorPreview(true);

    [ContextMenu("Предпросмотр: как в модели")]
    private void EditorPreviewRest()
    {
        if (_hinge == null || !_restCaptured) return;
        UnityEditor.Undo.RecordObject(_hinge, "Preview");
        _hinge.localRotation = _hingeRest;
    }

    private void EditorPreview(bool unfolded)
    {
        ResolveSides();
        if (!Measure(false)) { Debug.LogError("[Раскладная лестница] сначала ⋮ → «1) Создать шарнир и вложить ladder2»", this); return; }
        UnityEditor.Undo.RecordObject(_hinge, "Preview");
        _hinge.localRotation = _hingeRest * Quaternion.AngleAxis(unfolded ? _turnUnfolded : _turnFolded, LocalAxis);
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[Раскладная лестница] предпросмотр: {(unfolded ? "разложена" : "сложена")} ({(unfolded ? _turnUnfolded : _turnFolded):0}°). " +
                  "Вернуть — «Предпросмотр: как в модели».", this);
    }

    [ContextMenu("Запомнить текущую позу шарнира как позу модели")]
    private void EditorCaptureRest()
    {
        if (_hinge == null) return;
        UnityEditor.Undo.RecordObject(this, "Rest");
        _hingeRest = _hinge.localRotation;
        _restCaptured = true;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    private void OnDrawGizmosSelected()
    {
        if (!_debugGizmo || _hinge == null) return;
        if (!Application.isPlaying) { ResolveSides(); if (!Measure(false)) return; }
        if (!_measured) return;
        Vector3 h = HingeW, a = FootAW, b = FootBW;
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(h, 0.07f);
        Gizmos.color = Color.green; Gizmos.DrawLine(a, h); Gizmos.DrawWireSphere(a, 0.05f);
        Gizmos.color = Color.cyan; Gizmos.DrawLine(h, b); Gizmos.DrawWireSphere(b, 0.05f);
        Gizmos.color = Color.red; Gizmos.DrawLine(h, h + _hinge.TransformDirection(LocalAxis) * 0.4f);
    }
}
