using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// ГАЗОВЫЙ БАЛЛОН. Предмет: берётся, бросается, катается по полу.
///
/// ВЕНТИЛЬ: навёлся на любое место баллона, нажал клавишу (по умолчанию R) —
/// открывается, ещё раз — закрывается. Управление живёт в ValveTurner на
/// игроке. Поворот не мгновенный: успеть закрыть до взрыва — часть игры.
///
/// ОТКРЫТ И НЕ ПОДКЛЮЧЁН — газ уходит в воздух и копится облаком рядом.
/// Поднеси туда огонь, и рванёт. Подключил шланг — газ идёт по нему, и утечки
/// нет: облако рассеивается само.
///
/// Именно поэтому вентиль стоит закрывать, уходя. Открытый баллон в углу
/// кухни — мина замедленного действия, и это ровно то веселье, ради которого
/// он тут и нужен.
/// </summary>
public class GasCylinder : InteractableItem
{
    [Header("Положение в руке")]
    [SerializeField] private Vector3 _holdPositionOffset = new Vector3(0f, -0.2f, 0.5f);
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    [Header("Вентиль")]
    [Tooltip("Меш вентиля: крутится при открытии. Пусто = не крутится.")]
    [SerializeField] private Transform _valveWheel;

    [Tooltip("На сколько градусов проворачивается от закрытого до открытого")]
    [SerializeField] private float _valveTurn = 540f;

    [Tooltip("За сколько секунд вентиль доходит от закрытого до открытого")]
    [Min(0.1f)][SerializeField] private float _valveSeconds = 1.5f;



    [Header("Газ")]
    [Tooltip("БЕСКОНЕЧНЫЙ ГАЗ. Баллон не пустеет и не требует замены.\n\n" +
             "Пока на кухне нет ни доставки баллонов, ни индикации остатка, " +
             "кончившийся газ читается игроком не как «сходи за новым», а как " +
             "«плита сломалась» — и это ровно тот баг, который ловился в " +
             "тестах. Когда появится снабжение, флаг снимается одной галкой.")]
    [SerializeField] private bool _infiniteFuel = true;

    [Tooltip("Запас газа в баллоне, условные единицы")]
    [Min(1f)][SerializeField] private float _capacity = 100f;

    [Tooltip("Сколько уходит в секунду при полностью открытом вентиле")]
    [SerializeField] private float _flowPerSecond = 4f;

    [Tooltip("Откуда выходит газ. Пусто = чуть выше баллона.")]
    [SerializeField] private Transform _outlet;

    [Header("Шланг")]
    [Tooltip("Конец шланга этого баллона. Пока он не подключён к порту, " +
             "открытый газ уходит в воздух.")]
    [SerializeField] private GasHoseEnd _hose;

    [Header("Облако при утечке")]
    [Tooltip("Префаб облака газа. ОБЯЗАН быть в Spawnable Prefabs.")]
    [SerializeField] private GameObject _cloudPrefab;

    [Tooltip("Как часто подпитывать облако, сек")]
    [SerializeField] private float _cloudFeedInterval = 0.25f;

    [Header("Отладка")]
    [SerializeField] private bool _debugHud = true;

    // ───────── сеть ─────────
    /// <summary>Открытость вентиля 0..255. Плавная, а не флаг: приоткрытый
    /// баллон травит медленно, и это заметно по звуку и по облаку.</summary>
    private readonly SyncVar<byte> _open = new SyncVar<byte>(0);

    private readonly SyncVar<byte> _fuel = new SyncVar<byte>(255);

    private float _open01;
    private float _openTarget;
    private float _fuelLeft;
    private float _nextFeed;
    private GasCloud _cloud;

    private Quaternion _valveBase;
    private bool _hasValveBase;


    public float Open01 => _open.Value / 255f;
    public float Fuel01 => _fuel.Value / 255f;
    public bool IsOpen => _open.Value > 8;
    public bool HasFuel => _infiniteFuel || _fuel.Value > 0;

    /// <summary>Шланг подключён к порту — газ идёт по назначению, а не в воздух.</summary>
    public bool IsPiped => _hose != null && _hose.IsConnected;

    /// <summary>Сколько газа реально выходит прямо сейчас.</summary>
    public float Flow => IsOpen && HasFuel ? Open01 * _flowPerSecond : 0f;

    public Transform Outlet => _outlet != null ? _outlet : transform;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
        _fuelLeft = _capacity;

        if (_valveWheel != null)
        {
            _valveBase = _valveWheel.localRotation;
            _hasValveBase = true;
        }

    }

    // ───────────────────────── ВЕНТИЛЬ ─────────────────────────

    private void Update()
    {
        if (base.IsServerInitialized) ServerTick();
        UpdateValveVisual();
    }

    /// <summary>
    /// Открыть или закрыть вентиль. Зовётся игроком через ValveTurner.
    ///
    /// Поворот НЕ мгновенный: вентиль доворачивается за пару секунд, и успеть
    /// закрыть его до взрыва — часть игры. Мгновенное переключение убило бы
    /// всё напряжение.
    /// </summary>
    [Server]
    public void ServerToggleValve()
    {
        _openTarget = IsOpen ? 0f : 1f;
        Debug.Log($"[Баллон] вентиль {(_openTarget > 0f ? "открывают" : "закрывают")}", this);
    }

    /// <summary>Задать открытость напрямую. Пригодится, если понадобится
    /// приоткрыть вентиль наполовину.</summary>
    [Server]
    public void ServerSetOpen(float value01)
    {
        _openTarget = Mathf.Clamp01(value01);
        _open01 = _openTarget;
    }

    [Server]
    private void ServerTick()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.1f);

        // Вентиль доворачивается плавно — успеть закрыть это часть игры.
        _open01 = Mathf.MoveTowards(_open01, _openTarget,
                                    dt / Mathf.Max(0.1f, _valveSeconds));

        byte o = (byte)Mathf.RoundToInt(Mathf.Clamp01(_open01) * 255f);
        if (o != _open.Value && (o == 0 || o == 255 || Mathf.Abs(o - _open.Value) >= 6))
            _open.Value = o;

        float flow = Flow;
        if (flow <= 0f) return;

        // Бесконечный баллон газ всё равно ВЫПУСКАЕТ (облако при утечке
        // должно копиться как обычно) — он просто не пустеет.
        if (!_infiniteFuel)
        {
            _fuelLeft = Mathf.Max(0f, _fuelLeft - flow * dt);

            byte f = (byte)Mathf.RoundToInt(_fuelLeft / _capacity * 255f);
            if (Mathf.Abs(f - _fuel.Value) >= 3 || f == 0) _fuel.Value = f;
        }

        // Подключён — газ уходит по шлангу, в воздух ничего не попадает.
        if (IsPiped) return;

        FeedCloud(flow, dt);
    }

    /// <summary>Копим облако у выхода. Не каждый кадр: облако и так живёт
    /// своей жизнью, ему хватает подпитки несколько раз в секунду.</summary>
    [Server]
    private void FeedCloud(float flow, float dt)
    {
        if (Time.time < _nextFeed) return;
        _nextFeed = Time.time + _cloudFeedInterval;

        if (_cloud == null || !_cloud.IsSpawned)
        {
            if (_cloudPrefab == null)
            {
                Debug.LogWarning($"[Баллон] на «{name}» не назначен Cloud Prefab — " +
                                 "газ уходит впустую", this);
                return;
            }

            GameObject go = Instantiate(_cloudPrefab, Outlet.position, Quaternion.identity);
            base.Spawn(go);
            _cloud = go.GetComponent<GasCloud>();

            if (_cloud == null)
            {
                Debug.LogError($"[Баллон] на «{_cloudPrefab.name}» нет GasCloud", this);
                return;
            }
        }

        // Облако держится у выхода, но не приклеено: баллон можно унести,
        // а газ останется там, где натекло.
        _cloud.ServerFeed(Outlet.position, flow * _cloudFeedInterval);
    }

    // ───────────────────────── ВИЗУАЛ ─────────────────────────

    /// <summary>Вентиль крутится локально у всех по синхронизированной
    /// открытости — гонять поворот отдельно незачем.</summary>
    private void UpdateValveVisual()
    {
        if (!_hasValveBase) return;
        _valveWheel.localRotation = _valveBase * Quaternion.Euler(0f, 0f, Open01 * _valveTurn);
    }

    public override void UpdateHeldPose(Transform handAnchor)
    {
        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);
        transform.SetPositionAndRotation(pos, rot);
    }

    // ───────────────────────── ОТЛАДКА ─────────────────────────

    private void OnGUI()
    {
        if (!_debugHud) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 world = transform.position + Vector3.up * 0.4f;
        if (Vector3.Distance(cam.transform.position, world) > 6f) return;

        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z <= 0f) return;

        GUIStyle st = new GUIStyle(GUI.skin.box) { fontSize = 13 };

        bool leaking = IsOpen && HasFuel && !IsPiped;
        st.normal.textColor = leaking ? new Color(1f, 0.4f, 0.2f)
                            : IsOpen ? new Color(0.5f, 1f, 0.6f) : Color.white;

        string state = !HasFuel ? "пусто"
                     : leaking ? "ТРАВИТ ГАЗ"
                     : IsOpen ? "открыт, идёт по шлангу"
                     : "закрыт";

        string txt = $"Баллон: {state}\nвентиль {Open01 * 100f:0}%   газ {Fuel01 * 100f:0}%";
        Vector2 size = st.CalcSize(new GUIContent(txt));

        GUI.Box(new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y, size.x + 12f, 40f),
                txt, st);
    }
}
