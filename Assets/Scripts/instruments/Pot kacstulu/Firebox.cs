using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>Что сейчас с костром.</summary>
public enum FireState : byte
{
    /// <summary>Холодный. Топливо может быть, огня нет.</summary>
    Cold = 0,

    /// <summary>Занимается: горелку поднесли, растопка тлеет, огня ещё нет.
    /// Убрал горелку — откатывается обратно.</summary>
    Catching = 1,

    /// <summary>Горит пламенем.</summary>
    Burning = 2,

    /// <summary>Угли: пламени нет, но угольная подушка отдаёт ровное тепло.
    /// Подкинул дров — снова разгорится.</summary>
    Embers = 3
}

/// <summary>
/// КОСТЁР. Кострище под котлом: сюда кидают топливо, поджигают горелкой,
/// раздувают мехами.
///
/// ═══ ГЛАВНОЕ ОТЛИЧИЕ ОТ ПЛИТЫ ═══
///
/// У конфорки есть УСТАВКА: выставил 180 и ушёл. Костёр уставки не имеет — его
/// мощность задаётся дровами и тягой, и она ВСЁ ВРЕМЯ СПОЛЗАЕТ ВНИЗ. Воздух
/// после раздува затухает, дрова прогорают. Удержание температуры здесь —
/// работа, а не однократная настройка.
///
/// Костёр НЕ ЗНАЕТ про кастрюлю. Он отдаёт наружу одно число —
/// DegreesPerSecond. Подводит его FireCauldronStand.
///
/// ═══ ТРИ ПУЛА, И ЭТО ВАЖНО ═══
///
///   РАСТОПКА (Tinder)   — щепа, стружка. Вспыхивает от искры, сгорает за
///                         секунды, жара почти не даёт. Нужна ТОЛЬКО чтобы
///                         поджечь следующий слой.
///   ХВОРОСТ (Kindling)  — палки. Загорается от растопки, горит минуту-две,
///                         держит огонь, пока не займутся дрова.
///   ДРОВА (Firewood)    — наши четвертинки. Сами не поджигаются никогда,
///                         зато дают основной жар и превращаются в угли.
///   УГОЛЬ (Coal)        — не кидается, получается сам из дров.
///
/// Пулы РАЗДЕЛЬНЫЕ, а не один общий, потому что закладка — это ЛЕСТНИЦА.
/// Растопка поджигает хворост, хворост поджигает дрова. Свали всё в одно
/// число, и правило «нужна растопка» выразить нечем: получится «накидай
/// побольше», а это не решение, а объём работы.
///
/// ═══ ТРЕБОВАНИЯ К ЗАКЛАДКЕ ═══
///
/// Розжиг требует МИНИМУМА каждого вида — три отдельных порога. Не хватает
/// чего-то — костёр дымит и не занимается, а LayupStatus говорит человеческим
/// языком, чего именно мало.
///
/// Проверка стоит на РОЗЖИГЕ, а не на приёме топлива. Отказ на входе («не
/// приму дрова, пока нет растопки») выглядел бы поломкой и лишал бы игрока
/// возможности загрузить костёр в удобном ему порядке. А отказ разгораться со
/// внятным объяснением — это задача, которую видно и понятно как решить.
///
/// ═══ ДВА КОНТУРА ОБСЛУЖИВАНИЯ ═══
///
/// ТОПЛИВО кончается медленно — подкинуть раз в несколько минут.
/// ВОЗДУХ затухает быстро — вернуться к мехам раз в полминуты.
///
/// Разные периоды не случайность: будь они одинаковыми, игрок стоял бы у
/// костра безвылазно и до готовки дело не дошло бы.
///
/// ═══ СЕТЬ ═══
///
/// Мощность меняется каждый кадр — гнать её SyncVar'ом значит забить канал.
/// Наружу уходят БАЙТЫ с зоной нечувствительности, не чаще пяти раз в секунду.
/// Точные float'ы живут только на сервере.
/// </summary>
public class Firebox : NetworkBehaviour
{
    [Header("Зона закладки")]
    [Tooltip("Триггер, в который кидают топливо. Короб по размеру кострища.")]
    [SerializeField] private Collider _intakeZone;

    [Tooltip("Сколько килограммов влезает всего")]
    [Min(1f)][SerializeField] private float _capacityKg = 300f;

    [Tooltip("Откуда бьёт пламя и где ищется огонь для розжига")]
    [SerializeField] private Transform _firePoint;

    [Header("ТРЕБОВАНИЯ К ЗАКЛАДКЕ")]
    [Tooltip("Минимум РАСТОПКИ для розжига, кг.\n\n" +
             "Без неё костёр не займётся вообще, сколько горелку ни держи.")]
    [Min(0f)][SerializeField] private float _needTinderKg = 2f;

    [Tooltip("Минимум ХВОРОСТА, кг.\n\n" +
             "Растопка сгорает за секунды. Если ей нечего поджечь, огонь " +
             "гаснет, не дойдя до дров.")]
    [Min(0f)][SerializeField] private float _needKindlingKg = 8f;

    [Tooltip("Минимум ДРОВ, кг.\n\n" +
             "Можно разжечь и на одном хворосте, но гореть это будет минуту. " +
             "Порог заставляет закладывать костёр по-настоящему.")]
    [Min(0f)][SerializeField] private float _needWoodKg = 25f;

    [Header("Розжиг")]
    [Tooltip("Радиус поиска источника огня от Fire Point")]
    [SerializeField] private float _ignitionRadius = 1.2f;

    [Tooltip("Секунд держать горелку до розжига при полной закладке")]
    [Min(0.2f)][SerializeField] private float _secondsToCatch = 2.5f;

    [Tooltip("Как быстро откатывается розжиг, если горелку убрали, доля/сек")]
    [SerializeField] private float _catchDecay = 0.35f;

    [Header("Горение")]
    [Tooltip("Скорость прогорания растопки, кг/сек. Большая: на то и растопка.")]
    [SerializeField] private float _tinderBurnKgPerSec = 0.6f;

    [Tooltip("Скорость прогорания хвороста, кг/сек")]
    [SerializeField] private float _kindlingBurnKgPerSec = 0.12f;

    [Tooltip("Скорость прогорания дров, кг/сек, БЕЗ поддува")]
    [SerializeField] private float _woodBurnKgPerSec = 0.045f;

    [Tooltip("Скорость прогорания угля, кг/сек. Меньше дровяной в разы.")]
    [SerializeField] private float _coalBurnKgPerSec = 0.012f;

    [Tooltip("ВО СКОЛЬКО РАЗ ПОЛНЫЙ ПОДДУВ УСКОРЯЕТ ГОРЕНИЕ.\n\n" +
             "Здесь зарыт компромисс мехов: раздул — жарче, но дрова тают " +
             "втрое быстрее. Поставь 1 — и меха станут бесплатной кнопкой " +
             "«сделать хорошо».")]
    [Min(1f)][SerializeField] private float _airBurnBoost = 3f;

    [Header("Жар")]
    [Tooltip("Градусов в секунду при полном пламени и полном поддуве")]
    [SerializeField] private float _maxDegreesPerSecond = 14f;

    [Tooltip("Какую долю мощности даёт голый уголь без пламени")]
    [Range(0f, 1f)][SerializeField] private float _emberPower = 0.35f;

    [Header("Поддув")]
    [Tooltip("Какую долю тяги даёт один прыжок на мехах")]
    [Range(0f, 1f)][SerializeField] private float _airPerPulse = 0.3f;

    [Tooltip("КАК БЫСТРО ЗАТУХАЕТ ТЯГА, доля/сек. Главная ручка ритма.\n\n" +
             "0.05 — возвращаться к мехам примерно раз в полминуты.")]
    [SerializeField] private float _airDecay = 0.05f;

    [Tooltip("Собственная тяга угольной подушки. Устоявшийся костёр тянет сам " +
             "и меньше просит мехов.")]
    [Range(0f, 1f)][SerializeField] private float _coalDraft = 0.25f;

    [Header("Поленница")]
    [Tooltip("Меши дров внутри кострища, снизу вверх. Показываются по мере " +
             "заполнения — топливо поглощается числом, а видимая куча " +
             "собирается отсюда.")]
    [SerializeField] private Transform[] _pileMeshes;

    [Header("Отладка")]
    [SerializeField] private bool _debugHud;

    // ───────── сеть ─────────
    private readonly SyncVar<byte> _state = new SyncVar<byte>((byte)FireState.Cold);
    private readonly SyncVar<byte> _intensity = new SyncVar<byte>(0);
    private readonly SyncVar<byte> _fill = new SyncVar<byte>(0);

    // ───────── правда сервера ─────────
    private float _tinderKg, _kindlingKg, _woodKg, _coalKg;
    private float _heatTinder = 20f, _heatKindling = 60f, _heatWood = 140f;
    private float _coalYieldSum, _coalYieldMass;
    private float _ignitabilitySum, _ignitabilityMass;
    private float _air, _catch01, _power01, _nextSync;

    private bool _fireRegistered;

    public FireState State => (FireState)_state.Value;
    public bool IsBurning => State == FireState.Burning || State == FireState.Embers;
    public float Intensity01 => _intensity.Value / 255f;
    public float Fill01 => _fill.Value / 255f;
    public Transform FirePoint => _firePoint != null ? _firePoint : transform;

    public float TinderKg => _tinderKg;
    public float KindlingKg => _kindlingKg;
    public float WoodKg => _woodKg;
    public float CoalKg => _coalKg;
    public float FuelKg => _tinderKg + _kindlingKg + _woodKg + _coalKg;

    /// <summary>Тяга целиком: мехи плюс собственная тяга углей.</summary>
    public float Draft01 => Mathf.Clamp01(_air + _coalDraft * CoalShare);
    private float CoalShare => FuelKg <= 0.01f ? 0f : Mathf.Clamp01(_coalKg / FuelKg);

    /// <summary>ЕДИНСТВЕННОЕ, что костёр сообщает наружу. Кто заберёт это
    /// тепло и заберёт ли вообще — не его забота.</summary>
    public float DegreesPerSecond => _power01 * _maxDegreesPerSecond;

    // ───────────────────────── ТРЕБОВАНИЯ ─────────────────────────

    public bool HasTinder => _tinderKg >= _needTinderKg;
    public bool HasKindling => _kindlingKg >= _needKindlingKg;
    public bool HasWood => _woodKg >= _needWoodKg;
    public bool LayupReady => HasTinder && HasKindling && HasWood;

    /// <summary>
    /// Готовность закладки 0..1 — среднее по трём требованиям. Годится для
    /// полоски на экране: игрок видит, что до розжига осталось немного, а не
    /// гадает между «пусто» и «готово».
    /// </summary>
    public float Layup01
    {
        get
        {
            float a = _needTinderKg <= 0f ? 1f : Mathf.Clamp01(_tinderKg / _needTinderKg);
            float b = _needKindlingKg <= 0f ? 1f : Mathf.Clamp01(_kindlingKg / _needKindlingKg);
            float c = _needWoodKg <= 0f ? 1f : Mathf.Clamp01(_woodKg / _needWoodKg);
            return (a + b + c) / 3f;
        }
    }

    /// <summary>Чего не хватает, человеческим языком. Для подсказки на экране
    /// и для логов.</summary>
    public string LayupStatus
    {
        get
        {
            if (LayupReady) return "закладка готова";

            string s = "не хватает:";
            if (!HasTinder) s += $" растопки {_needTinderKg - _tinderKg:0.0} кг;";
            if (!HasKindling) s += $" хвороста {_needKindlingKg - _kindlingKg:0.0} кг;";
            if (!HasWood) s += $" дров {_needWoodKg - _woodKg:0.0} кг;";
            return s.TrimEnd(';');
        }
    }

    // ───────────────────────── ЖИЗНЕННЫЙ ЦИКЛ ─────────────────────────

    private void Awake()
    {
        if (_intakeZone != null) _intakeZone.isTrigger = true;
        else Debug.LogWarning($"[Костёр] «{name}»: нет зоны закладки — топливо " +
                              "кидать будет некуда.", this);

        // Подписка на дочерний триггер. Без ретранслятора события с IntakeZone
        // до этого скрипта не доходят вообще — см. TriggerRelay.
        TriggerRelay relay = TriggerRelay.Attach(_intakeZone);
        if (relay != null) relay.Entered += HandleIntakeEnter;
    }

    private void OnDestroy()
    {
        if (_fireRegistered) IgnitionRegistry.Unregister(FirePoint);

        if (_intakeZone == null) return;
        TriggerRelay relay = _intakeZone.GetComponent<TriggerRelay>();
        if (relay != null) relay.Entered -= HandleIntakeEnter;
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        RefreshPile();
    }

    private void Update()
    {
        if (base.IsServerInitialized) ServerTick();
        UpdateIgnitionSource();
        RefreshPile();
    }

    // ───────────────────────── ПРИЁМ ТОПЛИВА ─────────────────────────

    /// <summary>
    /// Топливо ПОГЛОЩАЕТСЯ, а не лежит физическим предметом.
    ///
    /// Тридцать четвертинок в кострище — это тридцать Rigidbody, толкающих друг
    /// друга под котлом. Они начнут выдавливаться наружу, проваливаться сквозь
    /// пол и стоить физике больше, чем весь остальной костёр вместе взятый.
    /// Вместо этого предмет исчезает, вес уходит в число, а видимая куча
    /// собирается из заранее расставленных мешей — тот же приём, что у частей
    /// бревна.
    /// </summary>
    /// <summary>
    /// Что-то влетело в зону закладки. Зовётся ретранслятором с дочернего
    /// объекта IntakeZone, а не Unity напрямую: OnTriggerEnter приходит только
    /// на объект самого коллайдера и вверх по иерархии не всплывает, а этот
    /// скрипт сидит на родителе — ему нужны и точка огня, и поленница.
    /// </summary>
    private void HandleIntakeEnter(Collider other)
    {
        if (other == null) return;

        FuelItem fuel = other.GetComponentInParent<FuelItem>();

        // Лог ДО проверки на сервер: если сюда не доходит вовсе — дело в
        // коллайдерах или слоях, а не в сетевой роли. Иначе эти два случая
        // неотличимы.
        if (_debugHud)
            Debug.Log($"[Костёр] «{name}»: в зону влетел «{other.name}», " +
                      $"топливо {(fuel == null ? "НЕ НАЙДЕНО" : fuel.Kind.ToString())}, " +
                      $"сервер {base.IsServerInitialized}", this);

        if (!base.IsServerInitialized) return;
        if (fuel == null) return;

        NetworkObject no = fuel.GetComponent<NetworkObject>();
        if (no == null || !no.IsSpawned) return;

        if (FuelKg >= _capacityKg)
        {
            Debug.Log($"[Костёр] «{name}»: полон ({FuelKg:0} кг) — «{fuel.name}» не влез", this);
            return;
        }

        ServerAddFuel(fuel);
        no.RemoveOwnership();
        base.Despawn(no);
    }

    [Server]
    private void ServerAddFuel(FuelItem fuel)
    {
        float kg = Mathf.Max(0.01f, fuel.MassKg);

        switch (fuel.Kind)
        {
            case FuelKind.Tinder:
                _tinderKg += kg;
                _heatTinder = Mathf.Lerp(_heatTinder, fuel.HeatPerKg, 0.5f);
                break;

            case FuelKind.Kindling:
                _kindlingKg += kg;
                _heatKindling = Mathf.Lerp(_heatKindling, fuel.HeatPerKg, 0.4f);
                break;

            case FuelKind.Coal:
                _coalKg += kg;
                break;

            default:
                _woodKg += kg;
                _heatWood = Mathf.Lerp(_heatWood, fuel.HeatPerKg, 0.35f);
                break;
        }

        // Выход угля и разгораемость — средневзвешенные по массе. Копим СУММУ
        // и МАССУ, а не среднее: усредняя среднее, оно уползало бы к
        // последнему добавленному.
        if (fuel.Kind != FuelKind.Coal)
        {
            _coalYieldSum += fuel.CoalYield * kg;
            _coalYieldMass += kg;

            _ignitabilitySum += fuel.Ignitability * kg;
            _ignitabilityMass += kg;
        }

        SyncOut(true);

        Debug.Log($"[Костёр] «{name}» + «{fuel.name}» ({fuel.Kind}, {kg:0.0} кг). " +
                  $"Растопка {_tinderKg:0.0} / хворост {_kindlingKg:0.0} / " +
                  $"дрова {_woodKg:0.0}. {LayupStatus}", this);
    }

    private float CoalYield => _coalYieldMass <= 0.01f
        ? 0.2f
        : Mathf.Clamp01(_coalYieldSum / _coalYieldMass);

    /// <summary>
    /// Средневзвешенная разгораемость закладки, 0..1. Управляет СКОРОСТЬЮ
    /// розжига, а не самим фактом: разгорится или нет — решают требования по
    /// килограммам (LayupReady), а это число говорит, насколько быстро.
    ///
    /// Так растопка получает смысл сверх «просто ещё одного килограмма»:
    /// закладка из одних дров формально может пройти по весу, но заниматься
    /// будет вечность.
    /// </summary>
    public float Ignitability01 => _ignitabilityMass <= 0.01f
        ? 0f
        : Mathf.Clamp01(_ignitabilitySum / _ignitabilityMass);

    // ───────────────────────── ПОДДУВ ─────────────────────────

    /// <summary>Раздуть. Зовут меха. Складывается: двое в противофазе
    /// накачивают вдвое быстрее одного.</summary>
    [Server]
    public void ServerAddAir(float amount01)
        => _air = Mathf.Clamp01(_air + amount01 * _airPerPulse);

    // ───────────────────────── ЧИТЫ ДЛЯ ОТЛАДКИ ─────────────────────────
    //
    // Нужны, чтобы проверять горение и баланс, не бегая каждый раз за дровами
    // через всю площадку. Зовёт CatCooksDebugHUD по горячим клавишам.
    //
    // Все три — [Server]: состояние костра живёт там, и правка его с клиента
    // просто разошлась бы с настоящим при первой же синхронизации.

    /// <summary>Насыпать топлива напрямую, минуя предметы. Веса берутся такие,
    /// чтобы требования закладки заведомо выполнились.</summary>
    [Server]
    public void DebugFill(float tinder, float kindling, float wood)
    {
        _tinderKg += tinder;
        _kindlingKg += kindling;
        _woodKg += wood;

        // Разгораемость и выход угля тоже надо подкинуть, иначе розжиг решит,
        // что закладка состоит из чего-то абсолютно негорючего.
        float total = tinder + kindling + wood;
        if (total > 0.01f)
        {
            _ignitabilitySum += tinder * 1f + kindling * 0.7f + wood * 0.05f;
            _ignitabilityMass += total;
            _coalYieldSum += kindling * 0.05f + wood * 0.25f;
            _coalYieldMass += total;
        }

        SyncOut(true);
        Debug.Log($"[Костёр/чит] «{name}»: насыпано. {LayupStatus}", this);
    }

    /// <summary>Поджечь мгновенно, без горелки и без проверки закладки.
    /// Проверка требований при этом НЕ обходится молча — если закладка не
    /// готова, костёр честно откажется и скажет почему.</summary>
    [Server]
    public void DebugIgnite()
    {
        if (FuelKg <= 0.01f)
        {
            Debug.LogWarning($"[Костёр/чит] «{name}»: пусто, гореть нечему", this);
            return;
        }

        if (!LayupReady)
        {
            Debug.LogWarning($"[Костёр/чит] «{name}»: {LayupStatus}", this);
            return;
        }

        _catch01 = 1f;
        SetState(FireState.Burning);
        Debug.Log($"[Костёр/чит] «{name}» подожжён принудительно", this);
    }

    /// <summary>Погасить и вычистить всё топливо — вернуть костёр в исходное.</summary>
    [Server]
    public void DebugExtinguish()
    {
        _tinderKg = _kindlingKg = _woodKg = _coalKg = 0f;
        _ignitabilitySum = _ignitabilityMass = 0f;
        _coalYieldSum = _coalYieldMass = 0f;
        _air = 0f;
        _catch01 = 0f;
        _power01 = 0f;
        SetState(FireState.Cold);
        SyncOut(true);
        Debug.Log($"[Костёр/чит] «{name}» потушен и вычищен", this);
    }

    // ───────────────────────── СЕРВЕРНЫЙ ТИК ─────────────────────────

    [Server]
    private void ServerTick()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.1f);
        _air = Mathf.Max(0f, _air - _airDecay * dt);

        switch (State)
        {
            case FireState.Cold: TickCold(); break;
            case FireState.Catching: TickCatching(dt); break;
            default: TickBurning(dt); break;
        }

        SyncOut(false);
    }

    [Server]
    private void TickCold()
    {
        _power01 = 0f;
        if (FuelKg <= 0.01f) return;
        if (!IgnitionRegistry.AnyNear(FirePoint.position, _ignitionRadius, out _)) return;

        SetState(FireState.Catching);
    }

    /// <summary>
    /// Занимается. Порог по закладке проверяется ЗДЕСЬ, чтобы игрок увидел
    /// причину: горелка работает, что-то дымит, а костёр не занимается, и в
    /// логе написано чего не хватает. Отказ на приёме топлива выглядел бы
    /// поломкой.
    /// </summary>
    [Server]
    private void TickCatching(float dt)
    {
        _power01 = _catch01 * 0.15f;

        if (!IgnitionRegistry.AnyNear(FirePoint.position, _ignitionRadius, out _))
        {
            _catch01 -= _catchDecay * dt;
            if (_catch01 <= 0f) { _catch01 = 0f; SetState(FireState.Cold); }
            return;
        }

        if (!LayupReady)
        {
            if (Time.frameCount % 120 == 0)
                Debug.Log($"[Костёр] «{name}»: только дымит — {LayupStatus}", this);
            return;
        }

        // РАЗГОРАЕМОСТЬ ЗАДАЁТ СКОРОСТЬ. Требования по весу уже пропустили нас
        // сюда — значит вопрос не "займётся ли", а "как быстро". Хорошая
        // растопка вспыхивает за Seconds To Catch, закладка из одного сырого
        // хвороста растянет то же время в разы.
        //
        // Нижняя граница 0.15 обязательна: без неё закладка из одних дров
        // (Ignitability 0.05) делила бы на почти ноль и заниматься не начала
        // бы никогда — а это уже поведение, неотличимое от зависшего костра.
        float ease = Mathf.Max(0.15f, Ignitability01);
        _catch01 += dt / (_secondsToCatch / ease);

        if (_catch01 >= 1f)
        {
            _catch01 = 1f;
            SetState(FireState.Burning);
            Debug.Log($"[Костёр] «{name}» разгорелся: {FuelKg:0.0} кг топлива", this);
        }
    }

    /// <summary>
    /// Горение. Слои сгорают СВЕРХУ ВНИЗ по лестнице: сначала растопка, потом
    /// хворост, потом дрова, в конце угли. Каждый следующий слой начинает
    /// гореть, только когда предыдущий уже вовсю — это и есть та самая
    /// лестница, ради которой пулы разделены.
    /// </summary>
    [Server]
    private void TickBurning(float dt)
    {
        float draft = Draft01;
        float speed = Mathf.Lerp(1f, _airBurnBoost, draft);
        float heat = 0f;

        if (_tinderKg > 0f)
        {
            float b = Mathf.Min(_tinderKg, _tinderBurnKgPerSec * speed * dt);
            _tinderKg -= b;
            heat += b * _heatTinder;
        }

        if (_kindlingKg > 0f)
        {
            float b = Mathf.Min(_kindlingKg, _kindlingBurnKgPerSec * speed * dt);
            _kindlingKg -= b;
            heat += b * _heatKindling;
            _coalKg += b * CoalYield * 0.3f;   // хворост углей почти не даёт
        }

        if (_woodKg > 0f)
        {
            float b = Mathf.Min(_woodKg, _woodBurnKgPerSec * speed * dt);
            _woodKg -= b;
            heat += b * _heatWood;
            _coalKg += b * CoalYield;
        }

        if (_coalKg > 0f)
        {
            float b = Mathf.Min(_coalKg, _coalBurnKgPerSec * Mathf.Lerp(1f, 1.6f, draft) * dt);
            _coalKg -= b;
            heat += b * _heatWood * _emberPower;
        }

        // Нормируем на «сколько сгорело бы при полном раздуве» — так мощность
        // получается в понятных 0..1 независимо от абсолютных настроек.
        float reference = _woodBurnKgPerSec * _airBurnBoost * _heatWood * dt;
        _power01 = reference > 0f ? Mathf.Clamp01(heat / reference) : 0f;

        bool flame = _tinderKg + _kindlingKg + _woodKg > 0.01f;

        if (!flame && _coalKg > 0.01f) SetState(FireState.Embers);
        else if (flame && State == FireState.Embers) SetState(FireState.Burning);

        if (FuelKg <= 0.01f)
        {
            _power01 = 0f;
            _catch01 = 0f;
            _coalYieldSum = _coalYieldMass = 0f;
            // Разгораемость тоже обнуляем: без этого новая закладка из одних
            // дров унаследовала бы высокую разгораемость от прошлой растопки
            // и вспыхнула бы мгновенно, хотя растопки в ней нет.
            _ignitabilitySum = _ignitabilityMass = 0f;
            SetState(FireState.Cold);
            Debug.Log($"[Костёр] «{name}» прогорел дотла", this);
        }
    }

    [Server]
    private void SetState(FireState s)
    {
        if (_state.Value != (byte)s) _state.Value = (byte)s;
    }

    /// <summary>Байты с зоной нечувствительности, не чаще пяти раз в секунду.
    /// Дрожь интенсивности глазом не отличить от локальной, а канал она
    /// съедает полностью.</summary>
    [Server]
    private void SyncOut(bool force)
    {
        if (!force && Time.time < _nextSync) return;
        _nextSync = Time.time + 0.2f;

        byte p = (byte)Mathf.RoundToInt(Mathf.Clamp01(_power01) * 255f);
        if (force || Mathf.Abs(p - _intensity.Value) > 3) _intensity.Value = p;

        byte f = (byte)Mathf.RoundToInt(Mathf.Clamp01(FuelKg / _capacityKg) * 255f);
        if (force || Mathf.Abs(f - _fill.Value) > 3) _fill.Value = f;
    }

    // ───────────────────────── ВИЗУАЛ ─────────────────────────

    /// <summary>
    /// Костёр — ИСТОЧНИК ОГНЯ для всего прочего, и регистрироваться в реестре
    /// обязан. Иначе газовый баллон, оставленный у полыхающего костра, считался
    /// бы в безопасности — а этим же реестром пользуются облака газа.
    /// </summary>
    private void UpdateIgnitionSource()
    {
        bool lit = IsBurning || State == FireState.Catching;
        if (lit == _fireRegistered) return;

        _fireRegistered = lit;
        if (lit) IgnitionRegistry.Register(FirePoint);
        else IgnitionRegistry.Unregister(FirePoint);
    }

    private void RefreshPile()
    {
        if (_pileMeshes == null || _pileMeshes.Length == 0) return;

        int visible = Mathf.CeilToInt(Fill01 * _pileMeshes.Length);
        for (int i = 0; i < _pileMeshes.Length; i++)
            if (_pileMeshes[i] != null) _pileMeshes[i].gameObject.SetActive(i < visible);
    }

    // ───────────────────────── ОТЛАДКА ─────────────────────────

    private void OnGUI()
    {
        if (!_debugHud || !base.IsServerInitialized) return;

        GUI.Label(new Rect(10, 200, 520, 140),
            $"КОСТЁР «{name}»   [{State}]   {_power01 * 100f:0}%   " +
            $"{DegreesPerSecond:0.0} °C/сек\n" +
            $"растопка {_tinderKg:0.0}/{_needTinderKg:0}   " +
            $"хворост {_kindlingKg:0.0}/{_needKindlingKg:0}   " +
            $"дрова {_woodKg:0.0}/{_needWoodKg:0}   уголь {_coalKg:0.0}\n" +
            $"тяга {Draft01 * 100f:0}% (мехи {_air * 100f:0}%)   " +
            $"розжиг {_catch01 * 100f:0}%   разгораемость {Ignitability01:0.00}\n" +
            $"{LayupStatus}");
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.8f);
        Gizmos.DrawWireSphere(FirePoint.position, _ignitionRadius);
    }
}
