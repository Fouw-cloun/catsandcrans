using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// ГАЗОВЫЙ БАЛЛОН С РЕДУКТОРОМ ДЛЯ КРОВЕЛЬНОЙ ГОРЕЛКИ — питает её по шлангу.
/// (Не путать с GasCylinder из Plita — тот баллон для плиты, у него свои вентиль и шланг.)
///
///   F — взять (тяжёлый: идёшь медленнее), G — поставить.
///   Давление на редукторе меняют колесом мыши, держа горелку: стрелка манометра
///   ходит, пламя длиннее и жарче, газ уходит быстрее.
///
/// Шланг короткий: отошёл с горелкой дальше длины шланга — баллон волочится следом,
/// дёрнул сильнее — шланг натянулся, газ перестал идти.
///
/// МОДЕЛЬ: GasCylinder.obj (части Body, Valve, ValveOutlet...) + дочерний GasReducer.obj
/// (части Inlet, Body, Cap, Outlet, GaugeFace, Needle). Редуктор сам садится
/// входной гайкой на выход вентиля (Snap Reducer) — модель можно не двигать руками.
///
/// Требует: NetworkObject, NetworkTransform, Rigidbody (масса 30–50), Collider.
/// </summary>
public class TorchGasCylinder : InteractableItem
{
    [Header("Газ")]
    [Tooltip("Бесконечный — для тестов")]
    [SerializeField] private bool _infinite = true;
    [Tooltip("На сколько секунд горения на среднем давлении хватает полного баллона")]
    [Min(10f)][SerializeField] private float _fullSeconds = 900f;

    [Header("Редуктор")]
    [Tooltip("Дочерний редуктор (модель GasReducer). Пусто — найдётся по имени (Reducer/Редуктор)")]
    [SerializeField] private Transform _reducer;
    [Tooltip("Посадить редуктор входной гайкой на выход вентиля при старте")]
    [SerializeField] private bool _snapReducer = true;
    [Tooltip("Куда крепится шланг. Пусто — штуцер Outlet у редуктора")]
    [SerializeField] private Transform _hoseOutlet;
    [Tooltip("Стрелка манометра. Пусто — часть Needle у редуктора")]
    [SerializeField] private Transform _needle;
    [Range(1, 5)][SerializeField] private int _startPressure = 3;
    [Tooltip("Размах стрелки от 1 до 5, градусы")]
    [SerializeField] private float _needleSweep = 220f;

    [Header("Ноша")]
    [Range(0.3f, 1f)][SerializeField] private float _carrySpeed = 0.7f;
    [SerializeField] private Vector3 _holdPositionOffset = new Vector3(0f, -0.4f, 0.5f);
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    [Header("Отладка")]
    [SerializeField] private bool _log = true;

    private readonly SyncVar<byte> _gasPct = new SyncVar<byte>(100);
    private readonly SyncVar<byte> _pressure = new SyncVar<byte>(3);

    private float _gas = 1f;                 // сервер, 0..1
    private Transform _needlePivot;
    private Vector3 _needleAxisLocal = Vector3.forward;
    private float _needleShown;
    private float _flowShown;
    private bool _held;

    /// <summary>Горит ли сейчас горелка от этого баллона (для дрожания стрелки). Ставит горелка, у всех.</summary>
    public float Flow { get; set; }

    public bool HasGas => _infinite || _gasPct.Value > 0;
    public float Gas01 => _infinite ? 1f : _gasPct.Value / 100f;
    public int Pressure => Mathf.Clamp(_pressure.Value, 1, 5);
    /// <summary>Множитель пламени от давления: 1 → 0.6, 3 → 1, 5 → 1.35.</summary>
    public float PressureK => Pressure <= 3 ? Mathf.Lerp(0.6f, 1f, (Pressure - 1) / 2f) : Mathf.Lerp(1f, 1.35f, (Pressure - 3) / 2f);
    public bool IsCarried => _held;
    public override float CarrySpeedMultiplier => _carrySpeed;

    /// <summary>Точка крепления шланга (мир).</summary>
    public Vector3 HoseOutletPoint => _hoseOutlet != null ? _hoseOutlet.position : transform.position + Vector3.up * 0.9f;
    /// <summary>Куда смотрит штуцер (мир).</summary>
    public Vector3 HoseOutletDir => _hoseOutlet != null ? _hoseOutlet.forward : transform.right;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
        if (_reducer == null) _reducer = FindChild(transform, "reducer", "редуктор");
        if (_snapReducer && _reducer != null) SnapReducer();
        if (_hoseOutlet == null) _hoseOutlet = MakeOutlet();
        if (_needle == null && _reducer != null) _needle = FindChild(_reducer, "needle", "стрелк");
        if (_needle != null) MakeNeedlePivot();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _pressure.Value = (byte)Mathf.Clamp(_startPressure, 1, 5);
        _gas = 1f;
        _gasPct.Value = 100;
    }

    // ───────────────────────── СЕРВЕР ─────────────────────────

    /// <summary>Горелка горит seconds секунд с множителем k (давление). Сервер.</summary>
    [Server]
    public void ServerConsume(float seconds)
    {
        if (_infinite || seconds <= 0f) return;
        _gas = Mathf.Max(0f, _gas - seconds / _fullSeconds);
        byte b = (byte)Mathf.CeilToInt(_gas * 100f);
        if (b != _gasPct.Value)
        {
            _gasPct.Value = b;
            if (b == 0 && _log) Debug.Log("[Баллон] газ кончился", this);
        }
    }

    [Server]
    public void ServerAddPressure(int delta)
    {
        int p = Mathf.Clamp(Pressure + delta, 1, 5);
        if (p == Pressure) return;
        _pressure.Value = (byte)p;
        if (_log) Debug.Log($"[Редуктор] давление {p}/5", this);
    }

    public override void ServerOnPickup(PlayerHand hand) { base.ServerOnPickup(hand); _held = true; }
    public override void ServerOnDrop(PlayerHand hand, Vector3 dir, float force = 0f)
    {
        base.ServerOnDrop(hand, dir, 0f);
        _held = false;
        // Ставим ровно, а не валим на бок.
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
    }

    public override void UpdateHeldPose(Transform handAnchor)
    {
        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Vector3 fwd = Vector3.ProjectOnPlane(handAnchor.forward, Vector3.up);
        Quaternion rot = (fwd.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fwd) : transform.rotation) * Quaternion.Euler(_holdEulerOffset);
        transform.SetPositionAndRotation(pos, rot);
    }

    // ───────────────────────── ВИД ─────────────────────────

    private void Update()
    {
        if (_needlePivot == null) return;
        _flowShown = Mathf.MoveTowards(_flowShown, Flow, Time.deltaTime * 3f);
        float target = (Pressure - 3) / 4f * _needleSweep;
        if (!HasGas) target = -_needleSweep * 0.5f;
        // Горит — стрелку чуть «трясёт» от расхода.
        target += _flowShown * (Mathf.PerlinNoise(Time.time * 9f, 0.3f) - 0.5f) * 10f - _flowShown * 6f;
        _needleShown = Mathf.Lerp(_needleShown, target, 1f - Mathf.Exp(-Time.deltaTime * 10f));
        _needlePivot.localRotation = Quaternion.AngleAxis(-_needleShown, _needleAxisLocal);
    }

    // ───────────────────────── МОДЕЛЬ ─────────────────────────

    [ContextMenu("Посадить редуктор на вентиль")]
    private void SnapReducer()
    {
        if (_reducer == null) _reducer = FindChild(transform, "reducer", "редуктор");
        if (_reducer == null) { Debug.LogWarning($"[Баллон] «{name}»: нет дочернего редуктора (Reducer)", this); return; }
        Renderer outlet = FindRenderer(transform, "valveoutlet", _reducer);
        Renderer valve = FindRenderer(transform, "valve", _reducer, "valveoutlet");
        Renderer inlet = FindRenderer(_reducer, "inlet", null);
        Renderer body = FindRenderer(_reducer, "body", null);
        if (outlet == null || valve == null || inlet == null || body == null)
        {
            if (_log) Debug.Log($"[Баллон] «{name}»: модель не из генератора (нет ValveOutlet/Valve/Inlet/Body) — редуктор не двигаю", this);
            return;
        }
        Vector3 d = Vector3.ProjectOnPlane(outlet.bounds.center - valve.bounds.center, transform.up);
        if (d.sqrMagnitude < 1e-8f) return;
        d.Normalize();
        Vector3 tip = outlet.bounds.center + d * Extent(outlet.bounds, d);

        Vector3 a = Vector3.ProjectOnPlane(body.bounds.center - inlet.bounds.center, transform.up);
        if (a.sqrMagnitude < 1e-8f) return;
        a.Normalize();
        Quaternion turn = Quaternion.FromToRotation(a, d);
        Vector3 pivot = inlet.bounds.center;
        _reducer.SetPositionAndRotation(pivot + turn * (_reducer.position - pivot), turn * _reducer.rotation);
        Vector3 back = inlet.bounds.center - d * Extent(inlet.bounds, d) * 0.9f;
        _reducer.position += tip - back;
    }

    private static float Extent(Bounds b, Vector3 dir)
        => Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.y) * b.extents.y + Mathf.Abs(dir.z) * b.extents.z;

    /// <summary>Точка шланга — на дальнем конце штуцера редуктора (Outlet).</summary>
    private Transform MakeOutlet()
    {
        Transform root = _reducer != null ? _reducer : transform;
        Renderer outlet = FindRenderer(root, "outlet", null, _reducer != null ? null : "valveoutlet");
        Renderer body = _reducer != null ? FindRenderer(_reducer, "body", null) : null;
        if (outlet == null && _reducer == null) outlet = FindRenderer(transform, "valveoutlet", null);
        if (outlet == null) return null;
        Vector3 from = body != null ? body.bounds.center : transform.position + Vector3.up * outlet.bounds.center.y;
        Vector3 d = Vector3.ProjectOnPlane(outlet.bounds.center - from, transform.up);
        d = d.sqrMagnitude > 1e-8f ? d.normalized : transform.right;
        var go = new GameObject("HoseOutlet (auto)");
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(outlet.bounds.center + d * Extent(outlet.bounds, d), Quaternion.LookRotation(d, transform.up));
        return go.transform;
    }

    /// <summary>Стрелка в модели вращается вокруг начала координат файла — подкладываем ось в центр манометра.</summary>
    private void MakeNeedlePivot()
    {
        Renderer face = _reducer != null ? FindRenderer(_reducer, "gaugeface", null) : null;
        Renderer nr = _needle.GetComponent<Renderer>();
        Vector3 c = face != null ? face.bounds.center : (nr != null ? nr.bounds.center : _needle.position);
        Vector3 normal = _needle.forward;
        MeshFilter mf = face != null ? face.GetComponent<MeshFilter>() : null;
        if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable)
        {
            Vector3 s = Vector3.zero;
            foreach (Vector3 n in mf.sharedMesh.normals) s += n;
            if (s.sqrMagnitude > 1e-6f) normal = mf.transform.TransformDirection(s.normalized);
        }
        else if (face != null)
        {
            Vector3 e = face.bounds.extents;     // плоский циферблат: нормаль — самая тонкая ось
            normal = e.x <= e.y && e.x <= e.z ? Vector3.right : e.y <= e.z ? Vector3.up : Vector3.forward;
        }
        var pivot = new GameObject("NeedlePivot (auto)").transform;
        pivot.SetParent(_needle.parent, false);
        pivot.position = c;
        pivot.rotation = _needle.parent != null ? _needle.parent.rotation : Quaternion.identity;
        _needle.SetParent(pivot, true);
        _needleAxisLocal = pivot.InverseTransformDirection(normal).normalized;
        _needlePivot = pivot;
    }

    private static Transform FindChild(Transform root, string a, string b)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root) continue;
            string n = t.name.ToLowerInvariant();
            if (n.Contains(a) || (b != null && n.Contains(b))) return t;
        }
        return null;
    }

    private static Renderer FindRenderer(Transform root, string key, Transform exclude, string notKey = null)
    {
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (exclude != null && r.transform.IsChildOf(exclude)) continue;
            string n = r.name.ToLowerInvariant();
            if (!n.Contains(key)) continue;
            if (notKey != null && n.Contains(notKey)) continue;
            return r;
        }
        return null;
    }

    private void OnDrawGizmosSelected()
    {
        if (_hoseOutlet == null) return;
        Gizmos.color = new Color(1f, 0.5f, 0.1f);
        Gizmos.DrawWireSphere(_hoseOutlet.position, 0.02f);
        Gizmos.DrawLine(_hoseOutlet.position, _hoseOutlet.position + _hoseOutlet.forward * 0.1f);
    }
}
