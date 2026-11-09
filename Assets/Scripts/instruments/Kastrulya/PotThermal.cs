using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>Что происходит с посудой прямо сейчас.</summary>
public enum PotState : byte
{
    Cold,       // холодная
    Warm,       // греется
    Boiling,    // кипит
    Overheat,   // перегрев: давит крышку, трясётся, шипит
    Ruptured    // разорвана, надо варить
}

/// <summary>
/// ТЕМПЕРАТУРА И ДАВЛЕНИЕ В ПОСУДЕ. Вешается на кастрюлю рядом с CookingVessel.
///
/// ЧТО СЧИТАЕТ:
///   температуру — растёт от подводимого тепла, падает к комнатной;
///   давление    — копится, только когда КРЫШКА ЗАКРЫТА и внутри есть чему
///                 кипеть. Открыл крышку — стравилось.
///
/// ЗАЧЕМ ДВА ПАРАМЕТРА, А НЕ ОДИН. Пустая кастрюля на огне раскаляется, но не
/// взрывается — ей нечем давить. Полная под крышкой рвёт себя паром задолго до
/// того, как металл раскалится. Это разные вещи, и играются они по-разному.
///
/// ИСТОЧНИКА НАГРЕВА ПОКА НЕТ: тепло подаётся снаружи через ServerAddHeat.
/// Для проверки есть клавиша в настройках отладки.
/// </summary>
// Раньше: [RequireComponent(typeof(CookingVessel))]. Убрано — новая кастрюля работает с Cookware.
// Старой кастрюле ничего не меняется: CookingVessel на ней уже есть.
public class PotThermal : NetworkBehaviour, CatCooks.Food.ITemperatureSource, CatCooks.Food.IPotBody
{
    [Header("Температура")]
    [Tooltip("Комнатная температура, к ней всё и остывает")]
    [SerializeField] private float _ambient = 20f;

    [Tooltip("Температура кипения содержимого")]
    [SerializeField] private float _boilPoint = 100f;

    [Tooltip("Выше этого металл раскалён — предел шкалы градусника")]
    [SerializeField] private float _maxTemp = 400f;

    [Tooltip("Насколько быстро остывает")]
    [SerializeField] private float _coolRate = 8f;

    [Tooltip("Во сколько раз медленнее греется ПОЛНАЯ кастрюля. Вода забирает " +
             "тепло на себя, поэтому пустая раскаляется куда быстрее.")]
    [SerializeField] private float _fullHeatFactor = 0.35f;

    [Header("Давление")]
    [Tooltip("Как быстро копится давление на каждые 100 градусов перегрева")]
    [SerializeField] private float _pressureRate = 0.16f;

    [Tooltip("Как быстро стравливается при открытой крышке")]
    [SerializeField] private float _ventRate = 0.9f;

    [Tooltip("С какого давления начинается перегрев: тряска, шипение, красный " +
             "градусник. Это предупреждение — ещё можно успеть снять крышку.")]
    [Range(0.1f, 0.95f)][SerializeField] private float _overheatAt = 0.6f;

    [Header("Разрыв")]
    [Tooltip("Сколько содержимого вытекает в секунду из разорванной посуды, граммов")]
    [SerializeField] private int _leakPerSecond = 400;

    [Tooltip("Разорванная посуда быстро остывает через дыры")]
    [SerializeField] private float _rupturedCoolBonus = 3f;

    [Header("Новая тара (Cookware) — если кастрюля на новой системе")]
    [Tooltip("Куда выливается содержимое при разрыве и течи — префаб лужи (LiquidPuddle). " +
             "Нужно только новой кастрюле (с Cookware вместо CookingVessel).")]
    [SerializeField] private CatCooks.Food.LiquidPuddle _puddlePrefab;
    [Tooltip("Новая тара: сколько порций в секунду вытекает из разорванной кастрюли")]
    [SerializeField] private float _leakPortionsPerSecond = 3f;

    [Header("Отладка (источника нагрева пока нет)")]
    [Tooltip("Держи эту клавишу, глядя на посуду, чтобы её греть")]
    [SerializeField] private KeyCode _debugHeatKey = KeyCode.H;

    [Tooltip("Сколько градусов в секунду добавляет отладочный нагрев")]
    [SerializeField] private float _debugHeatRate = 90f;

    [Tooltip("Слой посуды. Луч отладочного нагрева бьёт только по нему, " +
             "иначе упирается в первую попавшуюся стену перед кастрюлей.")]
    [SerializeField] private LayerMask _potMask = ~0;

    // ───────── сеть ─────────
    private readonly SyncVar<short> _tempC = new SyncVar<short>(20);
    private readonly SyncVar<byte> _pressure = new SyncVar<byte>(0);
    private readonly SyncVar<byte> _state = new SyncVar<byte>((byte)PotState.Cold);

    private CookingVessel _vessel;
    private CatCooks.Food.Cookware _cookware;       // новая система вместо CookingVessel

    /// <summary>Для Cookware: температура кастрюли (ITemperatureSource).</summary>
    public float TemperatureC => _tempC.Value;

    /// <summary>Есть ли в кастрюле содержимое — старая ёмкость или новая тара.</summary>
    private bool HasContents => _vessel != null ? !_vessel.IsEmpty : _cookware != null && !_cookware.IsEmpty;

    /// <summary>Заполненность 0..1 — полная кастрюля греется медленнее.</summary>
    private float LoadFraction
    {
        get
        {
            if (_vessel != null && _vessel.Def != null && _vessel.Def.CapacityG > 0)
                return Mathf.Clamp01((float)(_vessel.Def.CapacityG - _vessel.FreeSpaceG) / _vessel.Def.CapacityG);
            return _cookware != null ? _cookware.Fill01 : 0f;
        }
    }
    private PotLidSocket _lid;
    private DeformableMesh _shell;

    private float _temp;
    private float _press;
    private float _leakCarry;

    public float Temperature => _tempC.Value;
    public float Pressure01 => _pressure.Value / 255f;
    public PotState State => (PotState)_state.Value;
    /// <summary>Для общих скриптов кастрюли (IPotBody): то же состояние, новое перечисление.</summary>
    public CatCooks.Food.PotCondition Condition => (CatCooks.Food.PotCondition)_state.Value;
    public bool IsRuptured => State == PotState.Ruptured;
    public float BoilPoint => _boilPoint;

    /// <summary>Температура в долях шкалы — для градусника.</summary>
    public float Temp01 => Mathf.InverseLerp(_ambient, _maxTemp, _tempC.Value);

    /// <summary>Рвануло. На это подписываются звук, пар и тряска.</summary>
    public event System.Action Ruptured;

    private void Awake()
    {
        _vessel = GetComponent<CookingVessel>();
        if (_vessel == null) _cookware = GetComponent<CatCooks.Food.Cookware>();
        _lid = GetComponent<PotLidSocket>();
        _shell = GetComponent<DeformableMesh>();
        _temp = _ambient;
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _state.OnChange += OnStateChanged;
        Diagnose();
    }

    /// <summary>
    /// Разбор забытых настроек одной строкой в логе. Без этого нехватка,
    /// скажем, PotLidSocket проявляется как «давление почему-то не копится»,
    /// и искать причину приходится вслепую.
    /// </summary>
    private void Diagnose()
    {
        if (!base.IsServerInitialized) return;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append($"[Кастрюля] {name}: ");

        sb.Append(_lid != null ? "крышка есть" : "НЕТ PotLidSocket — давление копиться не будет");
        sb.Append(_shell != null ? ", деформация есть" : ", НЕТ DeformableMesh — корёжить нечего");

        int layer = gameObject.layer;
        sb.Append($", слой «{LayerMask.LayerToName(layer)}»");

        if (((1 << layer) & _potMask.value) == 0)
            sb.Append(" — ВНИМАНИЕ: слой не входит в Pot Mask, отладочный нагрев не сработает");

        Debug.Log(sb.ToString(), this);
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _state.OnChange -= OnStateChanged;
    }

    // ───────────────────────── СЕРВЕР ─────────────────────────

    private void Update()
    {
        if (!base.IsServerInitialized) return;

        float dt = Mathf.Min(Time.deltaTime, 0.1f);
        Cool(dt);
        UpdatePressure(dt);
        Leak(dt);
        Publish();
    }

    /// <summary>
    /// Подвести тепло. Единственная точка входа для плиты, горелки, костра —
    /// чего угодно. Источник сам решает, сколько и как часто.
    /// </summary>
    [Server]
    public void ServerAddHeat(float degreesPerSecond, float dt)
    {
        if (degreesPerSecond <= 0f) return;

        // Полная кастрюля греется медленнее: вода забирает тепло на себя.
        float load = LoadFraction;

        _temp = Mathf.Min(_temp + degreesPerSecond * Mathf.Lerp(1f, _fullHeatFactor, load) * dt,
                          _maxTemp);
    }

    private void Cool(float dt)
    {
        float rate = _coolRate * (IsRuptured ? _rupturedCoolBonus : 1f);

        // Чем сильнее разница с комнатой, тем быстрее остывает — как в жизни.
        _temp = Mathf.Max(_ambient, _temp - (_temp - _ambient) * 0.02f * rate * dt);
    }

    private void UpdatePressure(float dt)
    {
        if (IsRuptured) { _press = 0f; return; }

        bool closed = _lid != null && _lid.HasLid;
        bool hasLiquid = HasContents;

        if (!closed || !hasLiquid || _temp <= _boilPoint)
        {
            _press = Mathf.Max(0f, _press - _ventRate * dt);
            UpdateState();
            return;
        }

        _press += _pressureRate * ((_temp - _boilPoint) / 100f) * dt;

        if (_press >= 1f) { ServerRupture(); return; }
        UpdateState();
    }

    /// <summary>Разорванная посуда не держит содержимое — оно вытекает.</summary>
    private void Leak(float dt)
    {
        if (!IsRuptured) return;
        if (_vessel == null) { LeakCookware(dt); return; }
        if (_vessel.IsEmpty) return;

        _leakCarry += _leakPerSecond * dt;
        int take = Mathf.FloorToInt(_leakCarry);
        if (take <= 0) return;

        _leakCarry -= take;
        _vessel.TakeMass(take);       // вытекло на пол, никому не досталось
    }

    private void UpdateState()
    {
        if (IsRuptured) return;

        // ПУСТАЯ ПОСУДА НЕ КИПИТ, как бы ни была раскалена — испаряться нечему.
        //
        // Без этой проверки заваренная кастрюля продолжала парить: содержимое
        // пропало при разрыве, а температура осталась высокой, и состояние
        // честно считалось «кипит».
        bool hasLiquid = HasContents;

        PotState s;
        if (_press >= _overheatAt) s = PotState.Overheat;
        else if (_temp >= _boilPoint && hasLiquid) s = PotState.Boiling;
        else if (_temp >= _ambient + 25f) s = PotState.Warm;
        else s = PotState.Cold;

        if (_state.Value != (byte)s) _state.Value = (byte)s;
    }

    /// <summary>Наружу уходят огрублённые значения: трафик на ровном месте нам
    /// не нужен, а градуснику хватает целых градусов.</summary>
    private void Publish()
    {
        short t = (short)Mathf.RoundToInt(_temp);
        if (Mathf.Abs(t - _tempC.Value) >= 2) _tempC.Value = t;

        byte p = (byte)Mathf.RoundToInt(Mathf.Clamp01(_press) * 255f);
        if (Mathf.Abs(p - _pressure.Value) >= 3 || (p == 0) != (_pressure.Value == 0))
            _pressure.Value = p;
    }

    // ───────────────────────── РАЗРЫВ ─────────────────────────

    /// <summary>Рвануло: крышка улетает, корпус корёжит, содержимое пропадает.</summary>
    [Server]
    public void ServerRupture()
    {
        if (IsRuptured) return;

        _press = 0f;
        _state.Value = (byte)PotState.Ruptured;

        // Крышку срывает первой — по ней и понятно, что случилось.
        if (_lid != null) _lid.ServerBlowOff(Random.Range(int.MinValue, int.MaxValue));

        // Корпус корёжит. Зерно случайности синхронизируется, чтобы вмятины
        // у всех совпали.
        if (_shell != null) _shell.ServerDamage(Random.Range(int.MinValue, int.MaxValue));

        // Содержимое выплёскивается: варить дальше нечего.
        if (_vessel != null) _vessel.ServerClear();
        else DumpCookware();

        Debug.Log($"[Кастрюля] РАЗРЫВ при {_temp:0} градусах");
    }

    // ───────── новая тара (Cookware): течь и выплеск при разрыве ─────────

    private float _cwLeak;

    /// <summary>Разорванная новая кастрюля течёт через пробоины — лужа под ними.</summary>
    private void LeakCookware(float dt)
    {
        if (_cookware == null || _cookware.IsEmpty) return;
        _cwLeak += _leakPortionsPerSecond * dt;
        while (_cwLeak >= 1f)
        {
            _cwLeak -= 1f;
            int took = _cookware.ServerTakeTop(1, out ushort product, out CatCooks.Food.FoodState _, out byte _);
            if (took <= 0) break;
            if (_puddlePrefab != null) CatCooks.Food.LiquidPuddle.ServerAddAt(_puddlePrefab, product, LeakPoint(), took);
        }
    }

    /// <summary>Взрыв: всё содержимое новой тары — на пол, одной большой лужей.</summary>
    private void DumpCookware()
    {
        if (_cookware == null) return;
        Vector3 at = GroundBelow(transform.position);
        for (int guard = 0; guard < 200 && !_cookware.IsEmpty; guard++)
        {
            int took = _cookware.ServerTakeTop(99, out ushort product, out CatCooks.Food.FoodState _, out byte _);
            if (took <= 0) break;
            if (_puddlePrefab != null) CatCooks.Food.LiquidPuddle.ServerAddAt(_puddlePrefab, product, at, took);
        }
    }

    private Vector3 LeakPoint()
    {
        PotDamage dmg = GetComponent<PotDamage>() ?? GetComponentInChildren<PotDamage>();
        if (dmg != null)
        {
            var pts = new System.Collections.Generic.List<Vector3>();
            dmg.GetLeakPoints(pts);
            if (pts.Count > 0) return GroundBelow(pts[Random.Range(0, pts.Count)]);
        }
        return GroundBelow(transform.position);
    }

    private Vector3 GroundBelow(Vector3 from)
    {
        RaycastHit[] hits = Physics.RaycastAll(from + Vector3.up * 0.2f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue; Vector3 p = from;
        foreach (RaycastHit h in hits)
        {
            if (h.transform.IsChildOf(transform)) continue;
            if (h.distance < best) { best = h.distance; p = h.point; }
        }
        return p;
    }

    /// <summary>
    /// Проверить, готова ли посуда, и если да — восстановить.
    ///
    /// ЭТО ТОЧКА ВХОДА ДЛЯ ОБОИХ ИНСТРУМЕНТОВ. И молоток (форма), и сварка
    /// (дыры) чинят каждый своё, а после каждого удачного действия зовут этот
    /// метод, а не ServerRepaired напрямую. Порядок не важен — можно сначала
    /// выправить форму, потом заварить дыры, или наоборот, — потому что решение
    /// принимается здесь и сразу по ОБОИМ компонентам, а не по тому, какой из
    /// инструментов сработал последним.
    ///
    /// Раньше PotDamage и WeldingTorchItem каждый по отдельности звали
    /// ServerRepaired() сам, как только закрывалась последняя дыра — не
    /// заглядывая в DeformableMesh. С появлением отдельного молотка это стало
    /// дырой в логике: заварил все дыры на ещё мятой кастрюле — и она
    /// объявлялась годной, хотя стенки так и остались раскорёженными.
    /// </summary>
    [Server]
    public void ServerCheckFullyRepaired()
    {
        if (!IsRuptured) return;

        PotDamage dmg = GetComponent<PotDamage>() ?? GetComponentInChildren<PotDamage>();
        DeformableMesh shape = GetComponent<DeformableMesh>() ?? GetComponentInChildren<DeformableMesh>();

        bool holesOk = dmg == null || dmg.IsSealed;
        bool shapeOk = shape == null || !shape.IsBroken;

        if (holesOk && shapeOk) ServerRepaired();
    }

    /// <summary>Заварили — посуда снова цела и держит давление.</summary>
    [Server]
    public void ServerRepaired()
    {
        if (!IsRuptured) return;

        _press = 0f;

        // Заодно остужаем: заваренная кастрюля побывала под дугой и остыла,
        // а держать её раскалённой без содержимого бессмысленно.
        _temp = _ambient;
        _tempC.Value = (short)Mathf.RoundToInt(_ambient);

        _state.Value = (byte)PotState.Cold;

        // Ёмкость снова принимает содержимое: после разрыва её очистили,
        // и если флаг «закрыта» где-то остался, воду было бы не налить.
        if (_vessel != null) _vessel.ServerClear();

        Debug.Log($"[Кастрюля] «{name}» заварена и снова годна", this);
    }

    private void OnStateChanged(byte prev, byte next, bool asServer)
    {
        // У ХОСТА OnChange приходит дважды: asServer = true, потом false.
        if (asServer && base.IsClientInitialized) return;
        if ((PotState)next == PotState.Ruptured) Ruptured?.Invoke();
    }

    // ───────────────────────── ОТЛАДКА ─────────────────────────

    /// <summary>Пока источника нагрева нет — греем клавишей, глядя на посуду.
    /// Луч из камеры, чтобы не греть все кастрюли разом.</summary>
    private void LateUpdate()
    {
        if (!base.IsServerInitialized) return;
        if (_debugHeatKey == KeyCode.None || !Input.GetKey(_debugHeatKey)) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        if (!Physics.Raycast(cam.transform.position, cam.transform.forward,
                             out RaycastHit hit, 6f, _potMask, QueryTriggerInteraction.Ignore))
            return;

        if (hit.collider.GetComponentInParent<PotThermal>() != this) return;
        ServerAddHeat(_debugHeatRate, Mathf.Min(Time.deltaTime, 0.1f));
    }
}
