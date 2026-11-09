using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// КОНФОРКА ПЛИТЫ. Греет посуду, которая на ней стоит.
///
/// ГЛАВНОЕ ОТЛИЧИЕ ОТ ПРЕЖНЕЙ ВЕРСИИ: ручка задаёт не «мощность 0..100%»,
/// а ГРАДУСЫ. Игрок ставит 180 — конфорка сама подводит столько тепла, чтобы
/// содержимое пришло к 180 и там осталось.
///
/// ПОЧЕМУ ТАК ПРАВИЛЬНЕЕ:
///   при постоянной мощности температура никогда не встаёт — она ползёт вверх,
///   пока подвод не сравняется с потерями. Держать коридор блюда в 10 градусов
///   ручкой мощности физически невозможно, и мини-игра превращалась в
///   бесконечное подкручивание. С уставкой в градусах плита держит сама, а
///   игра начинается только тогда, когда настройку СБИВАЕТ.
///
/// ДВА ЗНАЧЕНИЯ ВМЕСТО ОДНОГО:
///   SetpointC — что игрок ВЫСТАВИЛ. Это память, сама она не меняется.
///   KnobC     — где ручка стоит СЕЙЧАС. Её уводит сбой, её же игрок
///               возвращает мини-игрой.
///   Расхождение между ними и есть «настройка сбилась».
///
/// ПОЛОМКА: провалил мини-игру — ручку скорёжило. Конфорка уходит на максимум,
/// искрит и умирает. Чинится через ServerRepair (сварка).
/// </summary>
public class StoveBurner : NetworkBehaviour
{
    [Header("Где стоит посуда")]
    [Tooltip("Точка над конфоркой, где ищется кастрюля")]
    [SerializeField] private Transform _plate;

    [Tooltip("Радиус поиска посуды, метры")]
    [SerializeField] private float _plateRadius = 0.25f;

    [Tooltip("Слой посуды")]
    [SerializeField] private LayerMask _potMask = ~0;

    [Header("Газ")]
    [Tooltip("Порт подачи газа. Без газа конфорка не горит.")]
    [SerializeField] private GasPort _gasPort;

    [Tooltip("Работать без газа. Только для отладки.")]
    [SerializeField] private bool _ignoreGas;

    // ───────────────────────── ШКАЛА ─────────────────────────

    [Header("Шкала температуры")]
    [Tooltip("Нижний край шкалы")]
    [SerializeField] private float _minC = 20f;

    [Tooltip("Верхний край шкалы — упор ручки")]
    [SerializeField] private float _maxC = 300f;

    [Tooltip("Ниже этой уставки конфорка считается выключенной и не горит")]
    [SerializeField] private float _offBelowC = 40f;

    [Header("Нагрев")]
    [Tooltip("Потолок подвода тепла, градусов в секунду. Ограничивает, как " +
             "быстро конфорка разгоняется к уставке.")]
    [SerializeField] private float _maxHeatRate = 120f;

    [Tooltip("Жёсткость регулятора: сколько градусов в секунду добавлять на " +
             "каждый градус недобора. Больше — быстрее выходит на уставку, " +
             "но сильнее проскакивает.")]
    [SerializeField] private float _gain = 1.6f;

    [Tooltip("Как быстро ручка доворачивается к новому значению, градусов " +
             "ШКАЛЫ в секунду. Мгновенный доворот выглядит как телепорт.")]
    [SerializeField] private float _knobSlew = 90f;

    // ───────────────────────── СБОЙ ─────────────────────────

    [Header("Сбой настройки — то, за чем возвращается игрок")]
    [Tooltip("Через сколько секунд настройка сбивается. Между сбоями можно " +
             "спокойно уйти нарезать.")]
    [SerializeField] private Vector2 _driftAfter = new Vector2(22f, 48f);

    [Tooltip("На сколько градусов уводит ручку при сбое")]
    [SerializeField] private Vector2 _driftAmountC = new Vector2(25f, 60f);

    [Header("Поломка")]
    [Tooltip("Сколько секунд конфорка искрит и жарит на максимуме, прежде " +
             "чем окончательно сдохнуть")]
    [SerializeField] private float _breakdownSeconds = 8f;

    [Tooltip("На сколько градусов доворачивается скорёженная ручка — она " +
             "должна выглядеть свёрнутой набок, а не просто на максимуме")]
    [SerializeField] private float _brokenKnobTwist = 42f;

    // ───────────────────────── РУЧКА ─────────────────────────

    [Header("Ручка")]
    [Tooltip("Меш ручки: поворачивается по выставленной температуре")]
    [SerializeField] private Transform _knob;

    [Tooltip("ВОКРУГ КАКОЙ ОСИ крутится ручка. У модели из блендера ось почти " +
             "никогда не Z — поэтому это поле и появилось.")]
    [SerializeField] private KnobAxis _knobAxis = KnobAxis.Z;

    [Tooltip("Крутить в обратную сторону")]
    [SerializeField] private bool _knobInvert;

    [Tooltip("На сколько градусов проворачивается от нижнего края шкалы до " +
             "верхнего")]
    [SerializeField] private float _knobTurn = 270f;

    [Tooltip("Смещение нуля: если у модели ручка изначально смотрит не туда")]
    [SerializeField] private float _knobOffset;

    // ───────────────────────── ПЛАМЯ ─────────────────────────

    [Header("Пламя")]
    [Tooltip("Цвет внутреннего конуса. У газа он синий и почти прозрачный.")]
    [SerializeField] private Color _flameCoreColor = new Color(0.35f, 0.60f, 1f);

    [Tooltip("Цвет языков наверху. Именно они читаются как ОГОНЬ — чисто " +
             "синее пламя на тёмной плите почти не видно.")]
    [SerializeField] private Color _flameTipColor = new Color(1f, 0.62f, 0.20f);

    [Tooltip("Сколько частиц ядра в секунду на полной мощности")]
    [SerializeField] private float _flamePerSecond = 140f;

    [Tooltip("Сколько форсунок по кругу. Настоящая конфорка — это венец " +
             "отверстий, а не одно пятно.")]
    [Min(3)][SerializeField] private int _flameJets = 12;

    [Tooltip("Радиус венца форсунок в долях радиуса конфорки")]
    [Range(0.2f, 1f)][SerializeField] private float _flameRingRadius = 0.7f;

    [Tooltip("Высота языков на полной мощности, метры")]
    [SerializeField] private float _flameHeight = 0.09f;

    [SerializeField] private Light _flameLight;

    [Header("Выброс посуды при разрыве")]
    [Tooltip("Вверх, метров в секунду")]
    [SerializeField] private float _ejectUp = 4.5f;

    [Tooltip("Вбок, метров в секунду")]
    [SerializeField] private float _ejectSide = 2.2f;

    [Tooltip("Закрутка при выбросе")]
    [SerializeField] private float _ejectSpin = 9f;

    public enum KnobAxis { X, Y, Z }

    // ───────── сеть ─────────
    /// <summary>Что игрок выставил, градусы. Память настройки.</summary>
    private readonly SyncVar<short> _setC = new SyncVar<short>(20);

    /// <summary>Где ручка стоит сейчас, градусы. Её уводит сбой.</summary>
    private readonly SyncVar<short> _knobC = new SyncVar<short>(20);

    /// <summary>Настройка сбилась — надо подойти и поправить.</summary>
    private readonly SyncVar<bool> _needsTuning = new SyncVar<bool>(false);

    /// <summary>Ручку скорёжило: жарит на максимуме и искрит.</summary>
    private readonly SyncVar<bool> _broken = new SyncVar<bool>(false);

    /// <summary>Догорела: не греет и не горит, пока не починят.</summary>
    private readonly SyncVar<bool> _dead = new SyncVar<bool>(false);

    private CookingVessel _vessel;
    private PotThermal _thermal;
    private float _knobShown;
    private float _nextScan;
    private float _nextDrift;
    private float _brokenSince;
    private float _nextSpark;

    private ParticleSystem _flameCore;
    private ParticleSystem _flameTip;
    private float _coreCarry, _tipCarry;
    private int _jetIndex;
    private readonly Collider[] _hits = new Collider[8];

    public float MinC => _minC;
    public float MaxC => _maxC;
    public float OffBelowC => _offBelowC;

    /// <summary>Что игрок выставил, градусы.</summary>
    public float SetpointC => _setC.Value;

    /// <summary>Где ручка стоит сейчас, градусы.</summary>
    public float KnobC => _knobC.Value;

    public bool NeedsTuning => _needsTuning.Value;
    public bool IsBroken => _broken.Value;
    public bool IsDead => _dead.Value;

    /// <summary>Газ есть — конфорку можно зажечь.</summary>
    public bool HasGas => _ignoreGas || (_gasPort != null && _gasPort.HasGas);

    /// <summary>Горит прямо сейчас.</summary>
    public bool IsLit => HasGas && !_dead.Value && _knobC.Value > _offBelowC;

    /// <summary>Положение ручки в долях шкалы. Оставлено для совместимости:
    /// пламя и старый код думают в долях, игрок — в градусах.</summary>
    public float Setting01 => Mathf.InverseLerp(_minC, _maxC, _knobC.Value);

    public float Power01 => IsLit ? Setting01 : 0f;

    /// <summary>Что стоит на конфорке. Может быть пусто.</summary>
    public CookingVessel Vessel => _vessel;
    public PotThermal Thermal => _thermal;

    public Transform Plate => _plate != null ? _plate : transform;
    public Transform Knob => _knob;

    /// <summary>Свободна ли конфорка — на неё ещё ничего не поставили.</summary>
    public bool IsFree => _vessel == null;

    private void Awake()
    {
        _knobShown = _minC;

        // Ядро придавлено к металлу, языки тянет вверх — поэтому две системы
        // с разной гравитацией, а не одна.
        _flameCore = CreateFlameLayer("BurnerFlameCore (auto)", 0.10f);
        _flameTip = CreateFlameLayer("BurnerFlameTip (auto)", -0.05f);
    }

    private void OnDestroy()
    {
        if (_flameCore != null) Destroy(_flameCore.gameObject);
        if (_flameTip != null) Destroy(_flameTip.gameObject);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // НОВАЯ СЕССИЯ — КОНФОРКА ВЫКЛЮЧЕНА И ЦЕЛА.
        //
        // Без явного сброса SyncVar на СЦЕННОМ объекте переживает остановку
        // сети: объект не уничтожается, OnStopNetwork только отписывается,
        // а значения остаются с прошлого запуска. Отсюда и брался эффект
        // «плита помнит настройку с прошлого раза, но при этом не работает».
        _setC.Value = (short)_minC;
        _knobC.Value = (short)_minC;
        _needsTuning.Value = false;
        _broken.Value = false;
        _dead.Value = false;
        _knobShown = _minC;
    }

    // ───────────────────────── УПРАВЛЕНИЕ РУЧКОЙ ─────────────────────────

    /// <summary>
    /// Выставить температуру. Зовётся, пока игрок ведёт мышь по ручке.
    ///
    /// Ставит И уставку, И саму ручку: игрок крутит физическую ручку, а
    /// уставка — это просто то, что он ей задал. Расходятся они только при сбое.
    /// </summary>
    [Server]
    public void ServerSetTemperature(float celsius)
    {
        if (_dead.Value || _broken.Value) return;

        short v = (short)Mathf.RoundToInt(Mathf.Clamp(celsius, _minC, _maxC));
        _setC.Value = v;
        _knobC.Value = v;

        // Ручку тронули руками — значит, разошедшуюся настройку выправили.
        if (_needsTuning.Value) _needsTuning.Value = false;

        ScheduleDrift();
    }

    /// <summary>Мини-игра пройдена: ручка возвращается туда, где была.</summary>
    [Server]
    public void ServerTuningSucceeded()
    {
        if (_dead.Value || _broken.Value) return;

        _knobC.Value = _setC.Value;
        _needsTuning.Value = false;
        ScheduleDrift();

        Debug.Log($"[Плита] настройка восстановлена: {_setC.Value}°", this);
    }

    /// <summary>
    /// Мини-игра провалена: ручку скорёжило. Конфорка уходит вразнос — жарит
    /// на максимуме, искрит и через несколько секунд умирает.
    /// </summary>
    [Server]
    public void ServerTuningFailed()
    {
        if (_dead.Value || _broken.Value) return;

        _broken.Value = true;
        _needsTuning.Value = false;
        _brokenSince = Time.time;
        _knobC.Value = (short)_maxC;

        Debug.LogWarning("[Плита] ручку сорвало — конфорка вразнос", this);
    }

    /// <summary>Починили. Возвращает конфорку в исходное.</summary>
    [Server]
    public void ServerRepair()
    {
        _broken.Value = false;
        _dead.Value = false;
        _needsTuning.Value = false;
        _setC.Value = (short)_minC;
        _knobC.Value = (short)_minC;

        Debug.Log("[Плита] конфорка починена", this);
    }

    /// <summary>Грубая подстройка колесом мыши — оставлена как запасной ввод.</summary>
    [Server]
    public void ServerTurnKnob(int steps)
        => ServerSetTemperature(_knobC.Value + steps * 10f);

    /// <summary>Совместимость со старым кодом, который думал в долях.</summary>
    [Server]
    public void ServerSetKnob(float value01)
        => ServerSetTemperature(Mathf.Lerp(_minC, _maxC, Mathf.Clamp01(value01)));

    // ───────────────────────── ПОСУДА ─────────────────────────

    /// <summary>
    /// Поставить посуду на конфорку. Ставим РОВНО и гасим скорость: кастрюля
    /// должна встать, а не плюхнуться и укатиться, тем более с кипятком.
    /// </summary>
    [Server]
    public bool ServerPlace(CookingVessel vessel)
    {
        if (vessel == null || !IsFree) return false;

        Transform t = vessel.transform;
        Collider col = vessel.GetComponentInChildren<Collider>();

        // Насколько центр объекта выше его дна — на столько и поднимаем.
        float lift = col != null ? t.position.y - col.bounds.min.y : 0f;

        t.position = Plate.position + Vector3.up * lift;

        Vector3 fwd = t.forward;
        fwd.y = 0f;
        t.rotation = fwd.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(fwd.normalized, Vector3.up)
            : Quaternion.identity;

        Rigidbody rb = vessel.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Подхватываем сразу, не дожидаясь скана: иначе конфорка ещё полсекунды
        // считалась бы свободной, и туда влезла бы вторая кастрюля.
        Bind(vessel);
        _nextScan = Time.time + 0.3f;

        Debug.Log($"[Плита] «{vessel.name}» поставлена на конфорку", this);
        return true;
    }

    /// <summary>
    /// Подписка на разрыв идёт ЗДЕСЬ, в одном месте, а не в скане: подписаться
    /// надо ровно один раз на кастрюлю, а скан бегает каждые 0.3 секунды и
    /// навесил бы полторы сотни обработчиков за минуту.
    /// </summary>
    private void Bind(CookingVessel vessel)
    {
        if (_thermal != null) _thermal.Ruptured -= OnPotRuptured;

        _vessel = vessel;
        _thermal = vessel != null ? vessel.GetComponent<PotThermal>() : null;

        if (_thermal != null) _thermal.Ruptured += OnPotRuptured;
    }

    /// <summary>
    /// Кастрюлю разорвало. Её сдувает с плиты, а конфорка обнуляется:
    /// продолжать готовку на прежней уставке после взрыва бессмысленно, да и
    /// выглядит так, будто игра не заметила катастрофы.
    /// </summary>
    private void OnPotRuptured()
    {
        if (!base.IsServerInitialized || _vessel == null) return;

        Rigidbody rb = _vessel.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;

            // Вбок в случайную сторону, но обязательно вверх: кастрюлю должно
            // ПОДБРОСИТЬ, а не толкнуть — иначе не читается как выброс.
            Vector2 side = Random.insideUnitCircle.normalized;
            rb.linearVelocity = new Vector3(side.x * _ejectSide, _ejectUp, side.y * _ejectSide);
            rb.angularVelocity = Random.insideUnitSphere * _ejectSpin;
        }

        SparkBurst.Play(Plate.position + Vector3.up * 0.05f, 90, 6f);

        Bind(null);
        ServerResetCycle();

        Debug.LogWarning("[Плита] кастрюлю сорвало с конфорки — цикл сброшен", this);
    }

    /// <summary>Обнулить работу конфорки: ручка в ноль, сбой и мини-игра сняты.</summary>
    [Server]
    public void ServerResetCycle()
    {
        _setC.Value = (short)_minC;
        _knobC.Value = (short)_minC;
        _needsTuning.Value = false;

        BurnerTuning tuning = GetComponent<BurnerTuning>();
        if (tuning != null) tuning.ServerAbort();
    }

    // ───────────────────────── ЦИКЛ ─────────────────────────

    private void Update()
    {
        if (base.IsServerInitialized) ServerTick();
        UpdateKnobVisual();
        DrawFlame();
    }

    [Server]
    private void ServerTick()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.1f);

        FindVessel();
        ServerWatchDrift();
        ServerWatchBreakdown();

        if (!IsLit || _thermal == null) return;

        // РЕГУЛЯТОР: подводим тепло пропорционально недобору до уставки.
        //
        // Перебор НЕ «охлаждаем» — конфорка умеет только греть, остальное
        // делает естественное остывание в PotThermal. Иначе плита работала бы
        // как кондиционер, и перегрев стало бы нечем заслужить.
        float err = _knobC.Value - _thermal.Temperature;
        if (err <= 0f) return;

        float rate = Mathf.Min(err * _gain, _maxHeatRate);
        _thermal.ServerAddHeat(rate, dt);
    }

    /// <summary>Ищем посуду не каждый кадр: её ставят и снимают редко.</summary>
    [Server]
    private void FindVessel()
    {
        if (Time.time < _nextScan) return;
        _nextScan = Time.time + 0.3f;

        int n = Physics.OverlapSphereNonAlloc(Plate.position, _plateRadius, _hits,
                                              _potMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (_hits[i] == null) continue;

            CookingVessel v = _hits[i].GetComponentInParent<CookingVessel>();
            if (v == null) continue;

            if (v != _vessel) Bind(v);
            return;
        }

        if (_vessel != null) Bind(null);
    }

    /// <summary>
    /// Настройка сбивается сама: подача просела, пламя поддало, ручку повело.
    /// Ручка при этом ВИДИМО проворачивается — сбой игрок должен заметить
    /// глазом, а не только по цифре на градуснике.
    /// </summary>
    [Server]
    private void ServerWatchDrift()
    {
        if (_broken.Value || _dead.Value) return;
        if (!IsLit || _needsTuning.Value) return;
        if (_nextDrift <= 0f) { ScheduleDrift(); return; }
        if (Time.time < _nextDrift) return;

        float amount = Random.Range(_driftAmountC.x, _driftAmountC.y);
        if (Random.value < 0.5f) amount = -amount;

        // У краёв шкалы уводим внутрь: сбой, упёршийся в упор, игрок бы не
        // увидел — ручка просто осталась бы на месте.
        float target = _knobC.Value + amount;
        if (target < _offBelowC || target > _maxC) target = _knobC.Value - amount;

        _knobC.Value = (short)Mathf.RoundToInt(Mathf.Clamp(target, _minC, _maxC));
        _needsTuning.Value = true;

        Debug.Log($"[Плита] настройку сбило: {_setC.Value}° → {_knobC.Value}°", this);
    }

    [Server]
    private void ScheduleDrift()
        => _nextDrift = Time.time + Random.Range(_driftAfter.x, _driftAfter.y);

    /// <summary>Скорёженная конфорка жарит на максимуме, искрит и умирает.</summary>
    [Server]
    private void ServerWatchBreakdown()
    {
        if (!_broken.Value || _dead.Value) return;

        // Пока не сдохла — держим на упоре, чтобы игрок не спас её ручкой.
        if (_knobC.Value != (short)_maxC) _knobC.Value = (short)_maxC;

        if (Time.time - _brokenSince < _breakdownSeconds) return;

        _dead.Value = true;
        _knobC.Value = (short)_minC;

        Debug.LogWarning("[Плита] конфорка сгорела — нужна починка", this);
    }

    // ───────────────────────── ВИЗУАЛ ─────────────────────────

    /// <summary>
    /// Ручка крутится вокруг ВЫБРАННОЙ оси, а не жёстко вокруг Z.
    ///
    /// Блендер экспортирует модели с произвольной ориентацией, и захардкоженная
    /// ось означала, что ручка либо едет вбок, либо кувыркается. Ось, инверсия
    /// и смещение нуля вынесены в инспектор — подобрать за три секунды быстрее,
    /// чем перевыгружать модель.
    /// </summary>
    private void UpdateKnobVisual()
    {
        if (_knob == null) return;

        // Доворачиваем плавно: при сбое ручка должна ПОВЕРНУТЬСЯ на глазах,
        // а не оказаться в новом положении между кадрами.
        _knobShown = Mathf.MoveTowards(_knobShown, _knobC.Value, _knobSlew * Time.deltaTime);

        float t = Mathf.InverseLerp(_minC, _maxC, _knobShown);
        float angle = _knobOffset + (_knobInvert ? -1f : 1f) * t * _knobTurn;

        // Скорёженная ручка вдобавок свёрнута набок и мелко трясётся.
        if (_broken.Value && !_dead.Value)
            angle += _brokenKnobTwist + Mathf.Sin(Time.time * 30f) * 3f;
        else if (_dead.Value)
            angle += _brokenKnobTwist;

        _knob.localRotation = _knobAxis switch
        {
            KnobAxis.X => Quaternion.Euler(angle, 0f, 0f),
            KnobAxis.Y => Quaternion.Euler(0f, angle, 0f),
            _ => Quaternion.Euler(0f, 0f, angle)
        };
    }

    /// <summary>
    /// ПЛАМЯ ГАЗОВОЙ КОНФОРКИ — венец форсунок, а не одно пятно.
    ///
    /// Частицы вылетают из точек, расставленных по кругу, и по одной форсунке
    /// за раз, по очереди. Это даёт ровное кольцо даже при слабой подаче: при
    /// случайном разбросе на малой мощности пламя собиралось в одном месте и
    /// выглядело как утечка, а не как конфорка.
    ///
    /// Два слоя: синее ядро у металла и оранжевые языки над ним. Чисто синий
    /// газ на тёмной плите глазом почти не читается — цветные кончики делают
    /// огонь видимым, не ломая правду про газ.
    /// </summary>
    private void DrawFlame()
    {
        float p01 = Power01;

        // Сорванная конфорка жарит на полную и швыряется искрами.
        if (_broken.Value && !_dead.Value)
        {
            p01 = 1f;
            if (Time.time >= _nextSpark)
            {
                _nextSpark = Time.time + Random.Range(0.12f, 0.4f);
                SparkBurst.Play(Plate.position + Vector3.up * 0.02f, 30, 5f);
            }
        }

        bool on = p01 > 0.02f && (IsLit || _broken.Value);

        if (_flameLight != null)
        {
            _flameLight.enabled = on;
            if (on)
            {
                // Мерцание по шуму, а не по Random: свет должен дрожать,
                // а не мигать стробоскопом.
                float flicker = 0.85f + Mathf.PerlinNoise(Time.time * 12f, 0.3f) * 0.3f;

                // Слабое пламя оранжевее, сильное синее — как в жизни.
                _flameLight.color = Color.Lerp(_flameTipColor, _flameCoreColor,
                                               Mathf.Clamp01(p01 * 1.3f));
                _flameLight.intensity = p01 * 2.5f * flicker;
                _flameLight.range = 1.5f + p01;
            }
        }

        if (!on) return;

        Transform plate = Plate;
        float ring = _plateRadius * _flameRingRadius;
        float height = _flameHeight * (0.45f + p01);

        EmitCore(plate, ring, height, p01);
        EmitTips(plate, ring, height, p01);
    }

    private void EmitCore(Transform plate, float ring, float height, float p01)
    {
        if (_flameCore == null) return;

        int count = Take(ref _coreCarry, _flamePerSecond * p01 * Time.deltaTime, 10);
        if (count <= 0) return;

        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = JetPosition(plate, ring, out Vector3 outward);

            ep.position = pos;
            ep.startSize = Random.Range(0.008f, 0.016f) * (0.7f + p01);
            ep.startLifetime = Random.Range(0.10f, 0.18f);
            ep.startColor = _flameCoreColor;

            // Конус: наружу от центра и вверх. Строго вертикальная струя
            // выглядит как свеча, наклон наружу — как горелка.
            ep.velocity = Vector3.up * (height * Random.Range(5f, 8f))
                        + outward * Random.Range(0.05f, 0.16f);

            _flameCore.Emit(ep, 1);
        }
    }

    private void EmitTips(Transform plate, float ring, float height, float p01)
    {
        if (_flameTip == null) return;

        // Языков меньше, чем ядра: они крупные и живут дольше, при равном
        // числе получалась сплошная оранжевая клякса.
        int count = Take(ref _tipCarry, _flamePerSecond * 0.45f * p01 * Time.deltaTime, 6);
        if (count <= 0) return;

        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = JetPosition(plate, ring, out Vector3 outward);

            // Языки рождаются НАД ядром: газ сначала должен прогореть синим.
            ep.position = pos + Vector3.up * height * Random.Range(0.5f, 0.9f);
            ep.startSize = Random.Range(0.014f, 0.030f) * (0.6f + p01);
            ep.startLifetime = Random.Range(0.16f, 0.32f);

            ep.startColor = Color.Lerp(_flameTipColor,
                                       new Color(1f, 0.30f, 0.06f), Random.value * 0.7f);

            ep.velocity = Vector3.up * (height * Random.Range(4f, 7f))
                        + outward * Random.Range(0.02f, 0.10f)
                        + Random.insideUnitSphere * 0.04f;

            _flameTip.Emit(ep, 1);
        }
    }

    /// <summary>Следующая форсунка по кругу. Счётчик просто крутится — так
    /// кольцо заполняется равномерно, без дыр и сгустков.</summary>
    private Vector3 JetPosition(Transform plate, float ring, out Vector3 outward)
    {
        _jetIndex++;
        int jets = Mathf.Max(3, _flameJets);

        float ang = (_jetIndex % jets) / (float)jets * Mathf.PI * 2f;
        outward = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));

        float r = ring * Random.Range(0.92f, 1.06f);
        return plate.position + outward * r + Vector3.up * Random.Range(0f, 0.004f);
    }

    /// <summary>Накопитель дробных частиц: при 0.4 частицы за кадр без него
    /// не вылетело бы вообще ничего.</summary>
    private static int Take(ref float carry, float add, int max)
    {
        carry += add;
        int n = Mathf.FloorToInt(carry);
        if (n <= 0) return 0;
        carry -= n;
        return n > max ? max : n;
    }

    /// <summary>
    /// Система создаётся ВЫКЛЮЧЕННОЙ: она играет сразу при AddComponent, и
    /// часть свойств на играющей менять нельзя — Unity сыплет ошибки про
    /// duration.
    /// </summary>
    private ParticleSystem CreateFlameLayer(string name, float gravity)
    {
        GameObject go = new GameObject(name);
        go.SetActive(false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = 500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startSize = 0.02f;
        main.startLifetime = 0.25f;
        main.gravityModifier = gravity;

        // При сцене далеко от нуля Automatic отключил бы систему целиком:
        // её трансформ вне экрана, хотя частицы рядом с игроком.
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = false;                        // испускаем вручную

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0.95f, 0f),
                new GradientAlphaKey(0.75f, 0.4f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = g;

        // Язык пламени сужается кверху.
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(
            1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.25f));

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Particles/Standard Unlit")
                 ?? Shader.Find("Sprites/Default");

        if (sh != null)
        {
            Material m = new Material(sh) { name = name };
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);   // Additive
            if (m.HasProperty("_SrcBlend"))
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend"))
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            r.material = m;
        }

        go.SetActive(true);
        ps.Play();
        return ps;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(Plate.position, _plateRadius);
    }
}
