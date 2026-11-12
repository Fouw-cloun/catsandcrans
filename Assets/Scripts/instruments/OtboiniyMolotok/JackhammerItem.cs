using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Отбойный молоток. Инструмент в руке.
///
///   ЛКМ (удержание) — работает: инструмент дёргается/вибрирует.
///   G              — бросить (база).
///
/// ЩУП НА НОСИКЕ (как у миксера):
///     • НУЖНЫЙ слой (то, что долбим) → вибрирует ТОЛЬКО инструмент.
///     • НЕ нужный слой (камень/металл) → отдача: трясётся ещё и КАМЕРА игрока
///       (инструмент при этом продолжает трястись) — «весь игрок».
///
/// НАДЁЖНАЯ РЕАЛИЗАЦИЯ (после багов):
///   - Тряска инструмента — дрожь добавляется прямо к ПОЗЕ В РУКЕ у владельца,
///     остальным расходится через NetworkTransform. НЕ нужен отдельный меш.
///   - Тряска игрока — дрожим локальной позицией КАМЕРЫ у владельца (FPC крутит
///     наклон камеры поворотом, а мы двигаем позицию — не конфликтует).
///   - Флаги _toolVibrating/_playerVibrating — SyncVar, ставит владелец по щупу.
///
/// Требует: NetworkObject, NetworkTransform (Client Authoritative), Collider,
/// Rigidbody (Is Kinematic СНЯТО, Use Gravity ВКЛ — иначе будет висеть в воздухе).
/// </summary>
public class JackhammerItem : InteractableItem
{

    public override bool UseHeldBlocking => false;     // v17.3: своя логика столкновений/позы
    [Header("Ввод")]
    [Tooltip("ВКЛ — работает, пока ЛКМ зажата. ВЫКЛ — ЛКМ переключает.")]
    [SerializeField] private bool _holdToRun = true;

    [Header("Щуп на носике (сфера)")]
    [Tooltip("Точка проверки — кончик носика (дочерний пустой объект)")]
    [SerializeField] private Transform _probePoint;
    [SerializeField] private float _probeRadius = 0.12f;
    [Tooltip("НУЖНЫЙ слой (то, что долбим) — вибрирует только инструмент")]
    [SerializeField] private LayerMask _correctMask;
    [Tooltip("НЕ нужный слой (камень/металл) — отдача, трясётся ещё и камера")]
    [SerializeField] private LayerMask _wrongMask;
    [Tooltip("ВКЛ — инструмент дрожит на ЛКМ всегда. ВЫКЛ — только когда носик в слое.")]
    [SerializeField] private bool _toolVibratesInAir = true;

    [Header("Тряска инструмента (в руке)")]
    [Tooltip("Амплитуда смещения инструмента, метры")]
    [SerializeField] private float _toolShakePos = 0.02f;
    [Tooltip("Амплитуда поворота инструмента, градусы")]
    [SerializeField] private float _toolShakeAngle = 2f;
    [Tooltip("Частота тряски инструмента")]
    [SerializeField] private float _toolShakeFreq = 40f;

    [Header("Тряска игрока/камеры (неправильный слой)")]
    [Tooltip("Амплитуда тряски камеры, метры")]
    [SerializeField] private float _playerShakePos = 0.05f;
    [Tooltip("Частота тряски камеры")]
    [SerializeField] private float _playerShakeFreq = 30f;

    [Header("Положение в руке (регулировка под модель)")]
    [SerializeField] private Vector3 _holdPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    [Header("v17: в таре — сверху, водить как вибратор")]
    [Tooltip("ЛКМ + прицел на ведро/ковш/банку — носик опускается в горловину сверху и стоит на продукте; " +
             "куда смотришь — туда и ходит (по кругу/эллипсу горловины, не в стенку)")]
    [SerializeField] private bool _dockInCookware = true;
    [SerializeField] private float _dockRange = 2.4f;
    [Tooltip("Насколько глубоко носик может уйти в тару от горловины, м")]
    [SerializeField] private float _dockDepthMax = 0.6f;
    [Tooltip("Наклон молотка к игроку в таре, градусы (0 — строго вертикально)")]
    [Range(0f, 35f)][SerializeField] private float _dockTilt = 12f;
    [Tooltip("Доля горловины, по которой ходит носик (0.8 — не ближе 20% к стенке)")]
    [Range(0.2f, 0.95f)][SerializeField] private float _dockReach = 0.78f;
    [Tooltip("Ход долбления (вверх-вниз), м")]
    [Range(0f, 0.06f)][SerializeField] private float _poundAmp = 0.018f;
    [Tooltip("Частота долбления, удары в секунду")]
    [Range(2f, 30f)][SerializeField] private float _poundFreq = 13f;
    [Tooltip("Водишь носиком по таре — толчёт быстрее, до этого множителя")]
    [Range(1f, 3f)][SerializeField] private float _workBoostMax = 1.8f;
    [Tooltip("В руке (не в таре) не проходит сквозь стены, стол и предметы")]
    [SerializeField] private bool _blockByWorld = true;

    [Header("Оседание при спавне")]
    [Tooltip("Сколько секунд после спавна предмет заморожен (нейтрален), чтобы " +
             "не дёргаться при загрузке сцены. Потом включается обычная физика.")]
    [SerializeField] private float _spawnSettleTime = 5f;

    // ───────── СЕТЕВОЕ СОСТОЯНИЕ ─────────
    private readonly SyncVar<bool> _toolVibrating = new SyncVar<bool>(false);
    private readonly SyncVar<bool> _playerVibrating = new SyncVar<bool>(false);
    // v17: множитель работы (водишь по таре — толчёт быстрее), ×100
    private readonly SyncVar<byte> _boost = new SyncVar<byte>(100);
    // v17.3: носик в таре — видят все (для пыли)
    private readonly SyncVar<bool> _docked = new SyncVar<bool>(false);

    // ───────── ЛОКАЛЬНОЕ ─────────
    private bool _lastTool, _lastPlayer;
    private bool _runToggle;

    private Transform _holderCam;   // камера держателя (её позицию дрожим)
    private Vector3 _camBasePos;
    private bool _camBaseCached;

    // v17: тара
    private CatCooks.Food.Cookware _dockCw;
    private float _dockK;
    private Vector2 _stirN, _stirPrev;
    private float _moveAcc, _boostSendAt;
    private byte _boostSent = 100;
    private readonly RaycastHit[] _dockHits = new RaycastHit[16];
    private CatCooks.Food.HeldBlocker _blocker;
    private Vector3 _dockTip;
    private bool _dockTipOk;
    private CatCooks.Food.CookwareLiquidVisual _dockLv;

    /// <summary>v17: носик сейчас в таре (у владельца).</summary>
    public CatCooks.Food.Cookware DockedCookware => _dockCw;
    /// <summary>v17: множитель работы (водишь — быстрее), видят все.</summary>
    public float WorkBoost => _boost.Value / 100f;

    private bool _spawnReleased;    // снята ли заморозка после оседания
    private float _spawnTime;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // Физику предмета считает СЕРВЕР; на клиентах держим Rigidbody
        // кинематичным, визуал ведёт NetworkTransform. Иначе динамический RB на
        // клиенте дерётся с NT → предмет дрожит, лёжа на земле. Коллайдер не
        // трогаем, подбор работает. Сервер/хост остаётся динамическим (падает).
        if (!base.IsServerInitialized && _rb != null)
            _rb.isKinematic = true;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        // Замораживаем на _spawnSettleTime секунд, чтобы при загрузке сцены
        // предмет не дёргался от оседания физики. Потом — обычная физика.
        _spawnTime = Time.time;
        _spawnReleased = false;
        if (_rb != null) _rb.isKinematic = true;
    }

    // ───────── КАЖДЫЙ КАДР (только владелец) ─────────

    private void Update()
    {
        if (base.IsServerInitialized) ServerSpawnSettle();

        PoundTick();
        if (!base.IsOwner) return;
        OwnerDetect();
        OwnerDock();
        UpdateCameraShake();
    }

    // ───────── v17: В ТАРЕ — сверху, водить как вибратор ─────────

    private void OwnerDock()
    {
        CatCooks.Food.Cookware cw = null;
        bool lmb = _holdToRun ? Input.GetMouseButton(0) : _runToggle;
        if (_dockInCookware && lmb && _probePoint != null) cw = AimedCookware();
        if (cw != _dockCw) { _dockCw = cw; _dockLv = cw != null ? cw.GetComponentInChildren<CatCooks.Food.CookwareLiquidVisual>(true) : null; }
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (cw != null)
        {
            Quaternion fr; Vector2 half;
            Frame(cw, out fr, out half);
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 mouth = cw.MouthPoint, up = fr * Vector3.up;
                var plane = new Plane(up, mouth - up * Mathf.Min(0.12f, Mathf.Min(half.x, half.y) * 0.6f));
                Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                float e;
                if (plane.Raycast(ray, out e))
                {
                    Vector3 l = Quaternion.Inverse(fr) * (ray.GetPoint(e) - mouth);
                    Vector2 n = Vector2.ClampMagnitude(new Vector2(l.x / half.x, l.z / half.y), _dockReach);
                    _stirN = Vector2.Lerp(_stirN, n, 1f - Mathf.Exp(-dt * 14f));
                }
            }
            // водишь — быстрее (скорость носика по горловине + мышь)
            float sp = (_stirN - _stirPrev).magnitude / Mathf.Max(1e-4f, dt);
            float m = Mathf.Abs(Input.GetAxis("Mouse X")) + Mathf.Abs(Input.GetAxis("Mouse Y"));
            _moveAcc = Mathf.Lerp(_moveAcc, Mathf.Clamp01(sp * 0.6f + m * 0.15f), 1f - Mathf.Exp(-dt * 4f));
        }
        else _moveAcc = Mathf.Lerp(_moveAcc, 0f, 1f - Mathf.Exp(-dt * 6f));
        _stirPrev = _stirN;
        byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(100f * (1f + (_workBoostMax - 1f) * _moveAcc)), 100, 255);
        bool dockedNow = _dockCw != null && _dockK > 0.6f;
        if ((Time.time >= _boostSendAt && Mathf.Abs(b - _boostSent) >= 5) || dockedNow != _dockedSent)
        {
            _boostSendAt = Time.time + 0.2f;
            _boostSent = b;
            _dockedSent = dockedNow;
            CmdSetBoost(b, dockedNow);
        }
    }

    private bool _dockedSent;

    [ServerRpc(RequireOwnership = true)]
    private void CmdSetBoost(byte b, bool docked)
    {
        if (_boost.Value != b) _boost.Value = b;
        if (_docked.Value != docked) _docked.Value = docked;
    }

    // ───────── v17.3: ПЫЛЬ ПРИ ДОЛБЛЕНИИ (у всех) ─────────
    private float _poundAt, _dentAt;
    private bool _poundLogged;
    private readonly Collider[] _poundOv = new Collider[12];

    private void PoundTick()
    {
        if (!_toolVibrating.Value || !_docked.Value || _probePoint == null) return;
        if (Time.time < _poundAt) return;
        float boost = Mathf.Max(1f, WorkBoost);
        _poundAt = Time.time + Random.Range(0.14f, 0.2f) / boost;
        Vector3 at = _probePoint.position;
        CatCooks.Food.Cookware cw = FindCookwareAround(at);
        if (cw == null) cw = FindCookwareAround(at + Vector3.up * 0.08f);
        if (cw == null) { if (!_poundLogged) { _poundLogged = true; Debug.Log("[Отбойник] носик не в таре — пыли нет (проверь Probe Point на кончике)", this); } return; }
        CatCooks.Food.CookwareLiquidVisual lv = cw.GetComponentInChildren<CatCooks.Food.CookwareLiquidVisual>(true);
        Color c;
        bool any = false;
        if (lv != null && lv.HasLiquid) { c = lv.SurfaceColor; any = true; }
        else c = new Color(0.45f, 0.28f, 0.18f);
        // целые куски (зёрна) под носиком — их цвет
        int n = Physics.OverlapSphereNonAlloc(at, 0.12f, _poundOv, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            CatCooks.Food.FoodPiece fp = _poundOv[i] != null ? _poundOv[i].GetComponentInParent<CatCooks.Food.FoodPiece>() : null;
            if (fp == null || fp.Def == null) continue;
            if (!any)
            {
                CatCooks.Food.FoodRoastable fr = fp.GetComponent<CatCooks.Food.FoodRoastable>();
                c = fr != null ? fr.PowderColor : CatCooks.Food.FoodColors.Of(fp.Def, fp.State);
            }
            any = true;
            break;
        }
        if (!any) return;                                     // пустая тара — нечего пылить
        float sy;
        if (lv != null && lv.TryGetSurfaceY(at, out sy)) at.y = Mathf.Max(sy, Mathf.Min(at.y, sy + 0.03f));
        float size = lv != null ? lv.RadiusAtRimWorld : 0.2f;
        CatCooks.Food.CCPowderFx.Pound(at, cw.transform.up, c, size, Mathf.Clamp01(boost - 0.5f));
        if (lv != null && Time.time >= _dentAt) { _dentAt = Time.time + 0.22f; lv.Dent(at); }
        if (!_poundLogged) { _poundLogged = true; Debug.Log($"[Отбойник] долблю в «{cw.name}» — пыль облачками и комочки", this); }
    }

    private CatCooks.Food.Cookware AimedCookware()
    {
        Camera cam = Camera.main;
        if (cam == null) return null;
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        int n = Physics.RaycastNonAlloc(ray, _dockHits, _dockRange, ~0, QueryTriggerInteraction.Collide);
        float best = float.MaxValue;
        CatCooks.Food.Cookware found = null;
        for (int i = 0; i < n; i++)
        {
            if (_dockHits[i].collider == null || _dockHits[i].transform.IsChildOf(transform) || _dockHits[i].distance >= best) continue;
            CatCooks.Food.Cookware c = _dockHits[i].collider.GetComponentInParent<CatCooks.Food.Cookware>();
            if (c == null)
            {
                // попали в продукт, лежащий в таре, — тоже эта тара
                CatCooks.Food.FoodPiece fp = _dockHits[i].collider.GetComponentInParent<CatCooks.Food.FoodPiece>();
                if (fp != null) c = FindCookwareAround(_dockHits[i].point);
            }
            if (c == null || c.transform.IsChildOf(transform)) continue;
            best = _dockHits[i].distance; found = c;
        }
        return found;
    }

    private static CatCooks.Food.Cookware FindCookwareAround(Vector3 p)
    {
        foreach (CatCooks.Food.Cookware c in CatCooks.Food.Cookware.All)
        {
            if (c == null) continue;
            if (c.IsInside(p)) return c;
            Collider iv = c.InsideVolume;
            if (iv != null && iv.bounds.Contains(p)) return c;
        }
        return null;
    }

    /// <summary>Оси и полуоси горловины: круглая — радиус жидкости, некруглая — триггер Receive (как у вибратора).</summary>
    private void Frame(CatCooks.Food.Cookware cw, out Quaternion rot, out Vector2 half)
    {
        rot = cw.transform.rotation;
        CatCooks.Food.CookwareLiquidVisual lv = _dockLv;
        if (lv != null && lv.Shape != CatCooks.Food.CookwareLiquidVisual.Form.Box)
        {
            float R = Mathf.Max(0.04f, lv.RadiusAtRimWorld);
            half = new Vector2(R, R);
            return;
        }
        Transform rt = cw.transform.Find("Receive");
        BoxCollider bc = rt != null ? rt.GetComponent<BoxCollider>() : null;
        if (bc != null)
        {
            Vector3 ls = rt.lossyScale;
            half = new Vector2(Mathf.Max(0.04f, Mathf.Abs(bc.size.x * ls.x) * 0.5f), Mathf.Max(0.04f, Mathf.Abs(bc.size.z * ls.z) * 0.5f));
            rot = rt.rotation;
            return;
        }
        if (lv != null)
        {
            float s = lv.RadiusAtRimWorld / Mathf.Max(1e-4f, lv.RadiusAtRim);
            Vector2 h = lv.HalfSize * s;
            half = new Vector2(Mathf.Max(0.04f, h.x), Mathf.Max(0.04f, h.y));
            rot = lv.transform.rotation;
            return;
        }
        half = new Vector2(0.15f, 0.15f);
    }

    /// <summary>Куда встать носику: точка горловины под прицелом, вниз — до продукта/дна (луч), не глубже Dock Depth Max.</summary>
    private bool DockTip(out Vector3 tip, out Vector3 up)
    {
        tip = Vector3.zero; up = Vector3.up;
        if (_dockCw == null) return false;
        Quaternion fr; Vector2 half;
        Frame(_dockCw, out fr, out half);
        up = fr * Vector3.up;
        Vector3 mouth = _dockCw.MouthPoint;
        Vector3 p = mouth + fr * new Vector3(_stirN.x * half.x, 0f, _stirN.y * half.y);
        float depth = _dockDepthMax;
        int n = Physics.RaycastNonAlloc(p + up * 0.05f, -up, _dockHits, depth + 0.05f, ~0, QueryTriggerInteraction.Ignore);
        float best = depth + 0.05f;
        for (int i = 0; i < n; i++)
        {
            Collider c = _dockHits[i].collider;
            if (c == null || c.transform.IsChildOf(transform)) continue;
            if (c.GetComponentInParent<CharacterController>() != null) continue;
            if (_dockHits[i].distance < best) best = _dockHits[i].distance;
        }
        tip = p + up * 0.05f - up * best;
        // поверхность порошка/жидкости выше дна — носик стоит на ней (чуть утоплен)
        float sy;
        if (_dockLv != null && _dockLv.TryGetSurfaceY(p, out sy) && sy - 0.01f > tip.y && sy < mouth.y + 0.02f) tip.y = sy - 0.01f;
        return true;
    }

    /// <summary>После оседания снимаем заморозку — но только если предмет не в
    /// руке (в руке физикой рулит база). Держит предмет нейтральным при спавне.</summary>
    private void ServerSpawnSettle()
    {
        if (_spawnReleased) return;
        if (Time.time - _spawnTime < _spawnSettleTime) return;
        _spawnReleased = true;

        bool held = base.Owner != null && base.Owner.IsValid;
        if (!held && _rb != null) _rb.isKinematic = false;
    }

    private void OwnerDetect()
    {
        bool running;
        if (_holdToRun) running = Input.GetMouseButton(0);
        else { if (Input.GetMouseButtonDown(0)) _runToggle = !_runToggle; running = _runToggle; }

        bool inWrong = false, inCorrect = false;
        if (running && _probePoint != null)
        {
            inWrong = Physics.CheckSphere(_probePoint.position, _probeRadius,
                                          _wrongMask, QueryTriggerInteraction.Collide);
            inCorrect = Physics.CheckSphere(_probePoint.position, _probeRadius,
                                            _correctMask, QueryTriggerInteraction.Collide);
        }

        // Инструмент дрожит, пока работает (в воздухе — по галке, либо в любом слое).
        bool wantTool = running && (_toolVibratesInAir || inCorrect || inWrong);
        // Камера трясётся только в НЕ нужном слое (отдача).
        bool wantPlayer = running && inWrong;

        if (wantTool != _lastTool || wantPlayer != _lastPlayer)
        {
            _lastTool = wantTool;
            _lastPlayer = wantPlayer;
            CmdSetVibration(wantTool, wantPlayer);
        }
    }

    /// <summary>Тряска камеры держателя (только позиция — не мешает наклону из
    /// FirstPersonController). Рука с инструментом висит рядом, так что зрительно
    /// «весь игрок» дёргается вместе с видом.</summary>
    private void UpdateCameraShake()
    {
        if (_holderCam == null || !_camBaseCached) return;

        if (_playerVibrating.Value)
            _holderCam.localPosition = _camBasePos + ShakeOffset(_playerShakePos, _playerShakeFreq, 100f);
        else
            _holderCam.localPosition = _camBasePos;
    }

    private static Vector3 ShakeOffset(float amp, float freq, float seed)
    {
        float t = Time.time * freq;
        return new Vector3(
            (Mathf.PerlinNoise(t, seed) - 0.5f) * 2f * amp,
            (Mathf.PerlinNoise(t, seed + 11f) - 0.5f) * 2f * amp,
            (Mathf.PerlinNoise(t, seed + 23f) - 0.5f) * 2f * amp);
    }

    // ───────── СЕРВЕР ─────────

    [ServerRpc(RequireOwnership = true)]
    private void CmdSetVibration(bool tool, bool player)
    {
        if (_toolVibrating.Value != tool) _toolVibrating.Value = tool;
        if (_playerVibrating.Value != player) _playerVibrating.Value = player;
    }

    // ───────── БРОСОК: стоп ─────────

    public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
    {
        if (_toolVibrating.Value) _toolVibrating.Value = false;
        if (_playerVibrating.Value) _playerVibrating.Value = false;
        _lastTool = false;
        _lastPlayer = false;
        _runToggle = false;
        base.ServerOnDrop(hand, tossDirection, force);
    }

    public override void OnOwnershipClient(NetworkConnection prevOwner)
    {
        base.OnOwnershipClient(prevOwner);
        RestoreCamera();
        _runToggle = false;
        _lastTool = false;
        _lastPlayer = false;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        RestoreCamera();
    }

    private void RestoreCamera()
    {
        if (_holderCam != null && _camBaseCached)
            _holderCam.localPosition = _camBasePos;
    }

    // ───────── ПОЗА В РУКЕ + тряска инструмента ─────────

    public override void UpdateHeldPose(Transform handAnchor)
    {
        // Кэшируем камеру держателя (её позицию дрожим при отдаче).
        if (_holderCam == null)
        {
            FirstPersonController fpc = handAnchor.GetComponentInParent<FirstPersonController>();
            if (fpc != null)
            {
                Camera cam = fpc.GetComponentInChildren<Camera>(true);
                if (cam != null)
                {
                    _holderCam = cam.transform;
                    _camBasePos = _holderCam.localPosition;
                    _camBaseCached = true;
                }
            }
        }

        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);

        // v17: в руке — не сквозь мир
        if (_blockByWorld && _holderCam != null && _dockK < 0.99f)
        {
            if (_blocker == null) _blocker = new CatCooks.Food.HeldBlocker(transform);
            pos = _blocker.Solve(pos, rot, _holderCam.position);
        }

        // v17: в таре — носик сверху в горловину, стоит на продукте, ходит за прицелом, долбит вверх-вниз
        Vector3 tipW = Vector3.zero, upW = Vector3.up;
        bool dock = false;
        if (_dockCw != null) dock = DockTip(out tipW, out upW);
        _dockK = Mathf.MoveTowards(_dockK, dock ? 1f : 0f, Time.deltaTime * 5f);
        if (dock) { _dockTip = tipW; _dockTipOk = true; } else upW = Vector3.up;
        if (_dockK > 0.001f && _dockTipOk && _probePoint != null)
        {
            // ось молотка: вверх от носика, чуть к игроку
            Vector3 toCam = _holderCam != null ? _holderCam.position - _dockTip : -handAnchor.forward;
            Vector3 side = Vector3.ProjectOnPlane(toCam, upW);
            Vector3 axis = side.sqrMagnitude > 1e-6f ? Vector3.Slerp(upW, side.normalized, _dockTilt / 90f).normalized : upW;
            // носик долбит: вниз-вверх
            Vector3 tip = _dockTip;
            if (_toolVibrating.Value && _poundAmp > 0f)
            {
                float ph = Mathf.Repeat(Time.time * _poundFreq, 1f);
                float hit = ph < 0.35f ? Mathf.Sin(ph / 0.35f * Mathf.PI) : 0f;     // быстрый удар, пауза
                tip -= axis * (_poundAmp * hit - _poundAmp * 0.3f);
            }
            Vector3 tipOff = _probePoint.position - transform.position;    // при позе из руки
            transform.SetPositionAndRotation(pos, rot);
            tipOff = _probePoint.position - transform.position;
            if (tipOff.sqrMagnitude > 1e-6f)
            {
                Quaternion q = Quaternion.FromToRotation(tipOff, -axis * tipOff.magnitude);
                Quaternion rotI = q * rot;
                Vector3 posI = tip - q * tipOff;
                float k = Mathf.SmoothStep(0f, 1f, _dockK);
                pos = Vector3.Lerp(pos, posI, k);
                rot = Quaternion.Slerp(rot, rotI, k);
            }
        }

        // Тряска инструмента добавляется прямо к позе в руке (у владельца), NT
        // разошлёт остальным. Не зависит от отдельного меша — работает всегда.
        if (_toolVibrating.Value)
        {
            float k = _dockK > 0.5f ? 0.35f : 1f;                  // в таре трясётся меньше — долбит
            pos += handAnchor.rotation * ShakeOffset(_toolShakePos * k, _toolShakeFreq, 0f);
            rot *= Quaternion.Euler(ShakeOffset(_toolShakeAngle * k, _toolShakeFreq, 50f));
        }

        transform.SetPositionAndRotation(pos, rot);
    }

    // ───────── ГИЗМО ЩУПА ─────────

    private void OnDrawGizmosSelected()
    {
        if (_probePoint == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(_probePoint.position, _probeRadius);
    }

    // ─── Наружу: для JackhammerFoodPounder ───
    //
    // Тот же приём, каким AngleGrinderItem отдаёт себя обработчику еды.
    // Отдаём ИМЕННО _toolVibrating, а не сырое нажатие ЛКМ: работой считается
    // то, что инструмент реально дёргается, и это одинаково видно всем
    // клиентам, а не только владельцу.
    //
    // Щуп отдаём свой, а не заводим второй в компоненте толчения: две точки на
    // одном носике однажды разъедутся при правке модели.

    /// <summary>Молоток работает прямо сейчас.</summary>
    public bool IsRunning => _toolVibrating.Value;

    /// <summary>Отдача — носик в неподходящем слое. Пригодится, если толчение
    /// захочет отличать удар по продукту от удара по дну ведра.</summary>
    public bool IsKicking => _playerVibrating.Value;

    /// <summary>Кончик носика. Тот же, которым проверяются слои.</summary>
    public Transform ProbePoint => _probePoint;
}
