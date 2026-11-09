using FishNet.Object;
using UnityEngine;

/// <summary>
/// БАЛАНС ТАЧКИ. Поворот, перегруз, инерция, тормоза.
///
/// Вешается на тот же объект, что и PushCart. Сам PushCart не трогает: он
/// по-прежнему не знает, почему его тряхнуло, и знать не должен.
///
/// ═══ КОНФЛИКТА A/D НЕТ ═══
///
/// Раньше казалось, что руль и баланс дерутся за одни клавиши. На деле они
/// про одно и то же:
///
///   поворот направо → тачка встаёт на левое колесо → валит вправо
///   чтобы удержать  → повернуть налево
///
/// То есть контрбаланс И ЕСТЬ руль в обратную сторону, ровно как на
/// велосипеде. Отдельные клавиши не нужны — нужна подсказка, в какую сторону
/// доворачивать. Именно её и показывает интерфейс.
///
/// Из этого следует важное для ощущения: удержаться и приехать куда хотел —
/// разные задачи. Спас тачку — сбился с курса. Решение имеет цену, а значит
/// это выбор, а не формальность.
///
/// ═══ ЧЕТЫРЕ УРОВНЯ ЗАГРУЗКИ ═══
///
///   Пусто (8)          — качается еле-еле, можно гнать
///   Норма (9)          — рабочий режим, за единицу принят он
///   Перегруз (0)       — валит с полуоборота, тормозит долго
///   СВЕРХ (минус)      — тачку не удержать в принципе: на первом же
///                        серьёзном крене игрока перекидывает через неё
///
/// Сверхперегруз — не «очень сложно», а ДРУГОЙ ИСХОД. Мини-игра там не
/// выигрывается, и это честно: игрок должен понять, что столько грузить
/// нельзя, а не что он плохо жмёт кнопки.
///
/// ═══ ИНЕРЦИЯ И ТОРМОЗА ═══
///
/// Выше нормы тачка набирает ход, который не пропадает от того, что игрок
/// отпустил W. Она продолжает катиться и тянуть его за собой, пока он не
/// зажмёт S.
///
/// Смысл в том, чтобы разгон стал решением, а не бесплатным действием.
/// Разогнался с полной тачкой — планируй остановку заранее.
/// </summary>
[RequireComponent(typeof(PushCart))]
[AddComponentMenu("CatCooks/Тачка/Cart Balance")]
public class CartBalance : NetworkBehaviour
{
    public enum Phase
    {
        Calm = 0,
        /// <summary>Крен вырос, кнопка появилась тускло. Окно ещё закрыто.</summary>
        Warn = 1,
        /// <summary>Окно открыто. Доворачивать сейчас.</summary>
        Call = 2,
        Hit = 3,
        Miss = 4,
        /// <summary>Сверхперегруз: игрока перекинуло через тачку.</summary>
        Thrown = 5
    }

    public enum LoadLevel
    {
        Empty = 0,
        Normal = 1,
        Overload = 2,
        Super = 3
    }

    // ═════════════════════════ ЗАГРУЗКА ═════════════════════════

    [Header("━━━ Уровни загрузки ━━━")]
    [Tooltip("За единицу принята НОРМА. Перегруз и сверхперегруз — множители " +
             "от неё, поэтому все остальные настройки можно держать в " +
             "понятных величинах.")]
    [Range(0f, 0.6f)][SerializeField] private float _valueEmpty = 0.12f;

    [Range(0.5f, 1.5f)][SerializeField] private float _valueNormal = 1f;
    [Range(1f, 2.5f)][SerializeField] private float _valueOverload = 1.6f;

    [Tooltip("СВЕРХПЕРЕГРУЗ. Здесь мини-игра не выигрывается — на первом же " +
             "серьёзном крене игрока перекидывает через тачку.")]
    [Range(1.5f, 4f)][SerializeField] private float _valueSuper = 2.4f;

    [Space(6)]
    [Tooltip("ПОДМЕНА ЗАГРУЗКИ КЛАВИШАМИ, пока в сцене нечем набрать вес.")]
    [SerializeField] private bool _debugLoadKeys = true;

    [SerializeField] private KeyCode _keyEmpty = KeyCode.Alpha8;
    [SerializeField] private KeyCode _keyNormal = KeyCode.Alpha9;
    [SerializeField] private KeyCode _keyOverload = KeyCode.Alpha0;
    [SerializeField] private KeyCode _keySuper = KeyCode.Minus;

    [Tooltip("Текущий уровень. Минус один в поле подмены — брать настоящий " +
             "вес корыта.")]
    [SerializeField] private LoadLevel _level = LoadLevel.Normal;

    [SerializeField] private bool _useRealMass = false;

    [Tooltip("Кривая: по горизонтали загрузка, по вертикали множитель ко " +
             "всему крену.")]
    [SerializeField] private AnimationCurve _loadCurve = new AnimationCurve(
        new Keyframe(0f, 0.12f), new Keyframe(1f, 1f),
        new Keyframe(1.6f, 1.9f), new Keyframe(2.4f, 3.2f));

    // ═════════════════════════ КРЕН ОТ ПОВОРОТА ═════════════════════════

    [Header("━━━ Крен ━━━")]
    [Tooltip("ОТ ПОВОРОТА. Главный и почти единственный источник: тачка на " +
             "одной оси встаёт на внешнее колесо ровно тогда, когда её " +
             "поворачивают.\n\n" +
             "Скорость и загрузка это только УСИЛИВАЮТ, но сами по себе " +
             "не кренят — стоящую тачку не должно валить.")]
    [Range(0f, 6f)][SerializeField] private float _fromTurn = 2.6f;

    [Tooltip("ОТ СКОРОСТИ. Не толкает вбок, а усиливает уже имеющийся крен: " +
             "чем быстрее едешь, тем хуже держишь то, что уже накренилось.")]
    [Range(0f, 3f)][SerializeField] private float _speedAmplify = 0.9f;

    [Tooltip("ОТ НЕРОВНОСТЕЙ. Мелкая тряска, чтобы прямая не была стеклом.")]
    [Range(0f, 1.5f)][SerializeField] private float _fromBumps = 0.22f;

    [Range(0.1f, 6f)][SerializeField] private float _bumpFrequency = 1.5f;

    [Tooltip("ВОЗВРАТ. Насколько быстро крен сходит сам на малой скорости.\n\n" +
             "Без него любая поездка кончается событием, и осторожность " +
             "перестаёт вознаграждаться.")]
    [Range(0f, 4f)][SerializeField] private float _recovery = 1.1f;

    [Tooltip("Инерция крена: насколько он продолжает расти после того, как " +
             "руль вернули. Ноль — мгновенно слушается, единица — вязко.")]
    [Range(0f, 1f)][SerializeField] private float _leanInertia = 0.55f;

    // ═════════════════════════ СОБЫТИЕ ═════════════════════════

    [Header("━━━ Событие ━━━")]
    [Range(0.15f, 0.9f)][SerializeField] private float _warnAt = 0.4f;

    [Tooltip("ТЕЛЕГРАФ, секунды. Пауза между началом крена и открытием окна.\n\n" +
             "Отличает «реакцию на понятный сигнал» от «проверки скорости " +
             "реакции». Меньше 0.2 — событие ощущается внезапным.")]
    [Range(0f, 1.5f)][SerializeField] private float _telegraph = 0.35f;

    [Range(0.2f, 3f)][SerializeField] private float _windowNormal = 1f;

    [Tooltip("Окно при перегрузе. Короче, но не сильно: сложность должна " +
             "идти от частоты событий, а не от нечестно узкого окна.")]
    [Range(0.2f, 3f)][SerializeField] private float _windowOverload = 0.65f;

    [Range(0f, 3f)][SerializeField] private float _cooldown = 0.8f;

    [Header("━━━ Исходы ━━━")]
    [Tooltip("Насколько доворот возвращает тачку к равновесию")]
    [Range(0.2f, 1f)][SerializeField] private float _pushBack = 0.75f;

    [Tooltip("Сколько крена добавляет промах. Одно проваленное событие не " +
             "должно ронять — ронять должна серия.")]
    [Range(0f, 1f)][SerializeField] private float _missAdds = 0.42f;

    [Range(0f, 1f)][SerializeField] private float _missStumble = 0.55f;
    [Range(0.6f, 1.5f)][SerializeField] private float _tipAt = 1f;

    [Tooltip("СИЛА ПАДЕНИЯ, когда тачка ложится набок. Умножает и толчок, и " +
             "раскрутку, настроенные в PushCart: там форма падения, здесь — " +
             "насколько тяжело оно вышло.")]
    [Range(0.3f, 1.5f)][SerializeField] private float _tipStumble = 1f;

    // ═════════════════════════ БРОСОК ═════════════════════════

    [Header("━━━ Сверхперегруз: бросок через тачку ━━━")]
    [Tooltip("При каком крене срабатывает бросок. На сверхперегрузе такой " +
             "крен набирается почти сразу.")]
    [Range(0.3f, 1.2f)][SerializeField] private float _throwAt = 0.55f;

    [Tooltip("Скорость подброса вверх, м/с")]
    [Range(1f, 12f)][SerializeField] private float _throwUp = 5.5f;

    [Tooltip("Скорость вперёд, через тачку, м/с")]
    [Range(1f, 14f)][SerializeField] private float _throwForward = 6f;

    [Tooltip("Пауза после броска, пока мини-игра молчит")]
    [Range(0.5f, 6f)][SerializeField] private float _throwLockout = 2.5f;

    // ═════════════════════════ ИНЕРЦИЯ И ТОРМОЗА ═════════════════════════

    [Header("━━━ Инерция и тормоза ━━━")]
    [Tooltip("С какой загрузки появляется инерция. Норма принята за единицу, " +
             "поэтому 1.0 значит «тяжелее нормы».")]
    [Range(0.5f, 2f)][SerializeField] private float _momentumFrom = 1f;

    [Tooltip("Как быстро набирается ход. Разгон должен занимать пару секунд: " +
             "мгновенный не даёт игроку понять, что он его набрал.")]
    [Range(0.1f, 3f)][SerializeField] private float _momentumGain = 0.7f;

    [Tooltip("С какой силой набранный ход тянет игрока вперёд, м/с")]
    [Range(0f, 8f)][SerializeField] private float _carryPush = 2.6f;

    [Tooltip("ТОРМОЖЕНИЕ ЗАЖАТОЙ S. Делится на загрузку: чем тяжелее, тем " +
             "дольше останавливаться.")]
    [Range(0.1f, 4f)][SerializeField] private float _brakeRate = 1.1f;

    [Tooltip("Само по себе гаснет очень медленно — иначе тормоза не нужны.")]
    [Range(0f, 0.6f)][SerializeField] private float _coastDrain = 0.06f;

    [Tooltip("С какого хода интерфейс просит зажать S")]
    [Range(0.1f, 1f)][SerializeField] private float _brakePromptAt = 0.3f;

    [Tooltip("Насколько набранный ход усиливает крен. Разогнался — держать " +
             "тяжелее, и это честно.")]
    [Range(0f, 2f)][SerializeField] private float _momentumLean = 0.8f;

    // ═════════════════════════ ВИД ═════════════════════════

    [Header("━━━ Наклон тачки ━━━")]
    [Tooltip("ХОДОВАЯ ЧАСТЬ. Пусто — ищется на этом же объекте.\n\n" +
             "Крен отдаётся ЕЙ, а не пишется в трансформ напрямую: у кузова " +
             "должен быть ровно один владелец, иначе кто победит — зависит от " +
             "порядка вызова Update, который Unity не гарантирует. CartRig " +
             "складывает подвеску и баланс и применяет их одной записью.")]
    [SerializeField] private CartRig _rig;

    [Tooltip("ЗАПАСНОЙ ПУТЬ, если ходовой нет: наклонять этот трансформ " +
             "напрямую. Только НЕ корень тачки — его ведёт PushCart.")]
    [SerializeField] private Transform _visualRoot;

    [Range(0f, 60f)][SerializeField] private float _visualRollDeg = 26f;

    [Tooltip("ПОДЪЁМ НА ОДНО КОЛЕСО. Тачка не только кренится, но и слегка " +
             "приподнимается — второе колесо отрывается от земли.")]
    [Range(0f, 0.5f)][SerializeField] private float _visualLift = 0.09f;

    [Range(0.02f, 0.6f)][SerializeField] private float _visualSmooth = 0.1f;

    [Tooltip("Двигать трансформ по вертикали при подъёме на колесо.\n\n" +
             "Работает только на ЗАПАСНОМ пути, когда CartRig нет. С ходовой " +
             "частью подъём приходит через BalanceLift и это поле не " +
             "используется.")]
    [SerializeField] private bool _affectPosition = true;

    // ═════════════════════════ НА ОДНО КОЛЕСО ═════════════════════════

    [Header("━━━ Подъём на одно колесо ━━━")]
    [Tooltip("НАКЛОН И ПОДЪЁМ — РАЗНЫЕ ВЕЩИ.\n\n" +
             "Наклон вокруг собственной оси кузова: тачку накренило, но она " +
             "по-прежнему стоит на двух колёсах — так читается лёгкий крен на " +
             "неровности.\n\n" +
             "Подъём: та же тачка разворачивается вокруг пятна контакта " +
             "внешнего колеса, и внутреннее уходит вверх по дуге. Так " +
             "читается занос.\n\n" +
             "Величина здесь — доля второго. Считает её CartRig, ему и " +
             "отдаётся: у кузова один владелец.")]
    [SerializeField] private bool _wheelieEnabled = true;

    [Tooltip("С КАКОГО КРЕНА НАЧИНАЕТ ВСТАВАТЬ. Ниже — просто наклон.\n\n" +
             "Ставить около нуля не стоит: тогда тачка отрывает колесо от " +
             "каждой кочки, и подъём перестаёт что-либо значить. Он должен " +
             "означать «ты в заносе», а не «ты едешь».")]
    [Range(0f, 1f)][SerializeField] private float _wheelieFrom = 0.32f;

    [Tooltip("На каком крене подъём полный")]
    [Range(0.2f, 1.5f)][SerializeField] private float _wheelieFull = 0.85f;

    [Tooltip("ДОБАВКА К УГЛУ В ПОДЪЁМЕ, градусов.\n\n" +
             "Крена, набранного мини-игрой, для честной картинки мало: колесо " +
             "должно ОТОРВАТЬСЯ, а не приподняться на сантиметр. Эти градусы " +
             "приходят только вместе с подъёмом и уходят вместе с ним.")]
    [Range(0f, 40f)][SerializeField] private float _wheelieExtraDeg = 14f;

    [Tooltip("ВКЛАД ЧИСТОГО ПОВОРОТА, помимо накопленного крена.\n\n" +
             "Без него тачка встаёт на колесо только после того, как крен уже " +
             "накопился, — то есть с задержкой в полсекунды от резкого руля. " +
             "На глаз это выглядит как «встала сама», а не «встала из-за " +
             "поворота». Небольшая доля от руля привязывает подъём к причине.")]
    [Range(0f, 1f)][SerializeField] private float _wheelieFromTurn = 0.35f;

    [Tooltip("Сглаживание подъёма. Держи медленнее, чем сглаживание крена: " +
             "колесо должно вставать увесисто, а не мигать вверх-вниз.")]
    [Range(0.02f, 0.6f)][SerializeField] private float _wheelieSmooth = 0.14f;

    [Tooltip("ВСТАЁТ НЕ НА ТО КОЛЕСО — включи это.\n\n" +
             "Знак крена здесь задан мини-игрой: «валит вправо» значит «жми A». " +
             "Какое колесо при этом отрывается от земли — вопрос отдельный, и " +
             "зависит он от того, куда смотрит модель тачки и как расставлены " +
             "точки опоры. Проверять это рассуждением дольше, чем галкой.\n\n" +
             "Подсказку клавиши галка НЕ трогает: та берётся из знака крена " +
             "напрямую, и перепутать одно с другим нельзя.")]
    [SerializeField] private bool _flipWheelieSide = false;

    // ═════════════════════════ КРЕН КАК РУЛЬ ═════════════════════════

    [Header("━━━ Отзывчивость крена ━━━")]
    [Tooltip("ПРЯМОЙ ВКЛАД РУЛЯ В КРЕН, минуя инерцию.\n\n" +
             "Ноль — старое поведение: руль разгоняет скорость крена, и только " +
             "она двигает сам крен. Два интегрирования подряд всегда " +
             "запаздывают, сколько силу ни выкручивай.\n\n" +
             "Больше нуля — тачка кренится СРАЗУ, а инерционный путь остаётся " +
             "рядом и продолжает давать раскачку. Это и есть «наклоняет " +
             "быстрее».")]
    [Range(0f, 6f)][SerializeField] private float _leanDirect = 2.2f;

    [Header("━━━ Крен рулит ━━━")]
    [Tooltip("НАСКОЛЬКО КРЕН ПОВОРАЧИВАЕТ ТАЧКУ, градусов в секунду при " +
             "полном крене.\n\n" +
             "Это меняет смысл всей мини-игры. Раньше крен был штрафом: " +
             "набрался — жми клавишу, иначе тряхнёт. Теперь крен это РУЛЬ, и " +
             "он нужен, чтобы вообще поворачивать. Игрок сам заваливает тачку " +
             "ради поворота и сам же удерживает её на грани — а контр-клавиша " +
             "из «спасения» превращается в «выход из поворота».\n\n" +
             "Ноль возвращает старую схему: поворот только от A/D, крен только " +
             "мешает.")]
    [Range(0f, 160f)][SerializeField] private float _leanSteerDeg = 70f;

    [Header("━━━ Переворот на провале ━━━")]
    [Tooltip("Опрокидывать ли тачку набок, когда мини-игра провалена")]
    [SerializeField] private bool _flipEnabled = true;

    [Tooltip("ПЕРЕВОРАЧИВАТЬ ЛЮБОЙ ПРОМАХ, не глядя на величину крена.\n\n" +
             "Включено — не нажал вовремя, и тачка легла. Ровно и жёстко.\n\n" +
             "Выключено — промах сначала ДОБАВЛЯЕТ крена, и тачка ложится " +
             "только если он перевалил за Tip At. На практике это значит " +
             "«первый промах прощается, второй нет», и такая мини-игра " +
             "читается мягче: у игрока остаётся кадр на исправление, и " +
             "проигрыш ощущается заработанным, а не внезапным.")]
    [SerializeField] private bool _flipOnAnyMiss = true;

    [Tooltip("Куда отбрасывает игрока, м/с вбок. Ту же сторону, что и тачку: " +
             "их выдернуло друг из друга одним движением, и разлетаться в " +
             "разные стороны им неоткуда.")]
    [Range(0f, 10f)][SerializeField] private float _flipPlayerPush = 3.2f;

    [Range(0f, 8f)][SerializeField] private float _flipPlayerLift = 2f;

    [Header("━━━ Отладка ━━━")]
    [SerializeField] private bool _showReadout = false;
    [SerializeField] private bool _debugLog = false;

    // ───────────────────────── состояние ─────────────────────────

    private PushCart _cart;
    private Transform _holder;
    private FirstPersonController _holderFpc;
    private NetworkObject _holderNo;

    private float _lean, _leanVel;
    private float _phaseTimer, _windowLeft, _cooldownLeft, _lockout;
    private float _bumpSeed;
    private float _visualRoll, _visualRollVel, _visualY, _visualYVel;
    private float _wheelie, _wheelieVel;
    private Vector3 _lastPos;
    private float _speed01;
    private float _momentum;
    private int _combo;

    private Phase _phase = Phase.Calm;
    private bool _leanRight;

    private Vector3 _visualBasePos;
    private Quaternion _visualBaseRot = Quaternion.identity;

    // ───────────────────── публичное для интерфейса ─────────────────────

    public Phase CurrentPhase => _phase;
    public float Lean => _lean;
    public int Combo => _combo;
    public LoadLevel Level => _level;

    public float Danger => Mathf.Clamp01(Mathf.Abs(_lean) / Mathf.Max(0.1f, _tipAt));

    /// <summary>Куда доворачивать. Валит вправо — жать A, и наоборот.</summary>
    public KeyCode RequiredKey => _leanRight ? KeyCode.A : KeyCode.D;

    public string RequiredLabel => _leanRight ? "A" : "D";

    public float Window01 => _windowLeft <= 0f ? 0f
        : Mathf.Clamp01(_windowLeft / Mathf.Max(0.05f, CurrentWindow));

    public bool UiVisible => _phase != Phase.Calm;

    public bool IsLocalDriver => _cart != null && _cart.IsGripped && _holder != null;

    /// <summary>
    /// КТО ДЕРЖИТ РУЧКИ, если это местный игрок. Иначе null.
    ///
    /// Отдаётся наружу, чтобы каждому новому компоненту тачки не приходилось
    /// заново перебирать всех игроков в поисках держателя: этот перебор и так
    /// самое хрупкое место здесь, и размножать его по файлам не стоит.
    /// </summary>
    public FirstPersonController Holder => _holderFpc;

    /// <summary>Доля подъёма на одно колесо, 0..1 — для пыли, звука, следов.</summary>
    public float Wheelie01 => _wheelie;

    /// <summary>Насколько крен поворачивает тачку. Читает PushCart —
    /// поворотом занимается он, а настройка баланса живёт здесь.</summary>
    public float LeanSteerDegrees => _leanSteerDeg;

    /// <summary>Набранный ход 0..1.</summary>
    public float Momentum01 => Mathf.Clamp01(_momentum);

    /// <summary>Просить ли зажать S.</summary>
    public bool BrakeNeeded => IsLocalDriver && _momentum >= _brakePromptAt;

    /// <summary>Зажата ли S прямо сейчас — интерфейс подсвечивает нажатие.</summary>
    public bool Braking => Input.GetKey(KeyCode.S);

    /// <summary>Сверхперегруз: мини-игра не выигрывается.</summary>
    public bool IsSuper => Load >= _valueSuper - 0.01f;

    public float Load
    {
        get
        {
            if (_useRealMass && _cart != null && _cart.Trough != null)
                return Mathf.Clamp(_cart.Trough.MassKg / 160f, 0f, 3f);

            return _level switch
            {
                LoadLevel.Empty => _valueEmpty,
                LoadLevel.Overload => _valueOverload,
                LoadLevel.Super => _valueSuper,
                _ => _valueNormal
            };
        }
    }

    private float CurrentWindow =>
        Mathf.Lerp(_windowNormal, _windowOverload,
                   Mathf.InverseLerp(_valueNormal, _valueOverload, Load));

    // ───────────────────────── жизненный цикл ─────────────────────────

    private void Awake()
    {
        _cart = GetComponent<PushCart>();
        if (_rig == null) _rig = GetComponent<CartRig>();

        _bumpSeed = Random.value * 500f;
        _lastPos = transform.position;

        ValidateVisualRoot();

        if (_visualRoot != null)
        {
            _visualBaseRot = _visualRoot.localRotation;
            _visualBasePos = _visualRoot.localPosition;
        }
    }

    /// <summary>
    /// ПРОВЕРКА ВИЗУАЛЬНОГО КОРНЯ.
    ///
    /// Две ошибки здесь ломают тачку так, что это выглядит как отказ физики,
    /// а не как неверная ссылка — поэтому ловим их сразу и вслух.
    ///
    /// 1. НАЗНАЧЕН САМ КОРЕНЬ ТАЧКИ. Тогда наклон переписывает позицию и
    ///    поворот того же трансформа, который PushCart ведёт через
    ///    MovePosition. Два источника дерутся за одно значение, и тачка
    ///    зависает на месте: физика будто перестаёт на неё действовать.
    ///
    /// Правильный Visual Root — ТОЛЬКО кузов и корыто.
    ///
    /// Проверка нужна лишь для запасного пути: когда есть CartRig, трансформ
    /// пишет он один, и здесь проверять нечего.
    /// </summary>
    private void ValidateVisualRoot()
    {
        // При наличии ходовой части трансформ вообще не трогаем — проверять
        // нечего.
        if (_rig != null) return;

        if (_visualRoot == null) return;

        if (_visualRoot == transform)
        {
            Debug.LogError(
                $"[Баланс] «{name}»: в Visual Root назначен САМ КОРЕНЬ ТАЧКИ.\n\n" +
                "Наклон переписывал бы позицию и поворот того же трансформа, " +
                "который PushCart ведёт через MovePosition — тачка зависла бы " +
                "на месте, и это выглядит как отказ физики.\n\n" +
                "Создай дочерний объект с кузовом и назначь его. Ссылка снята.",
                this);

            _visualRoot = null;
            return;
        }

        if (_visualRoot.GetComponent<Rigidbody>() != null ||
            _visualRoot.GetComponent<PushCart>() != null)
        {
            Debug.LogError(
                $"[Баланс] «{name}»: на Visual Root висит Rigidbody или PushCart. " +
                "Наклонять физическое тело нельзя — оно спорит с MovePosition. " +
                "Ссылка снята.", this);

            _visualRoot = null;
            return;
        }

    }

    private void Update()
    {
        TrackHolder();
        ReadDebugKeys();

        float dt = Mathf.Min(Time.deltaTime, 0.05f);

        if (_lockout > 0f) _lockout -= dt;

        MeasureSpeed(dt);
        TickMomentum(dt);
        TickLean(dt);
        TickPhase(dt);
        TickVisual(dt);
    }

    private void TrackHolder()
    {
        if (_cart == null || !_cart.IsGripped)
        {
            if (_holder != null) ResetAll();
            _holder = null;
            _holderFpc = null;
            _holderNo = null;
            return;
        }

        if (_holder != null) return;

        // Держателя ищем среди игроков: PushCart хранит его в приватном поле,
        // поэтому опираемся на признак, доступный снаружи — включённый режим
        // управления тачкой у СВОЕГО игрока.
        FirstPersonController[] all =
            Object.FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);

        for (int i = 0; i < all.Length; i++)
        {
            if (!all[i].CartSteering) continue;

            NetworkObject no = all[i].GetComponent<NetworkObject>();
            if (no != null && !no.IsOwner) continue;

            _holder = all[i].transform;
            _holderFpc = all[i];
            _holderNo = no;
            _lastPos = transform.position;
            return;
        }
    }

    private void ReadDebugKeys()
    {
        if (!_debugLoadKeys) return;

        if (Input.GetKeyDown(_keyEmpty)) SetLevel(LoadLevel.Empty);
        else if (Input.GetKeyDown(_keyNormal)) SetLevel(LoadLevel.Normal);
        else if (Input.GetKeyDown(_keyOverload)) SetLevel(LoadLevel.Overload);
        else if (Input.GetKeyDown(_keySuper)) SetLevel(LoadLevel.Super);
    }

    private void MeasureSpeed(float dt)
    {
        Vector3 d = transform.position - _lastPos;
        _lastPos = transform.position;
        d.y = 0f;

        float mps = d.magnitude / Mathf.Max(0.0001f, dt);
        _speed01 = Mathf.Clamp01(mps / 3.5f);
    }

    /// <summary>
    /// ИНЕРЦИЯ.
    ///
    /// Набирается только выше нормы: пустую тачку останавливать нечем и не
    /// нужно, а на норме это была бы просто помеха.
    ///
    /// Гаснет сама почти незаметно — иначе тормоза становятся необязательными,
    /// и вся механика превращается в предупреждение, которое можно
    /// проигнорировать.
    /// </summary>
    private void TickMomentum(float dt)
    {
        if (_holder == null)
        {
            _momentum = Mathf.MoveTowards(_momentum, 0f, dt * 2f);
            return;
        }

        float over = Mathf.Max(0f, Load - _momentumFrom);

        if (over > 0.01f)
        {
            float fwd = Input.GetAxisRaw("Vertical");

            // Набирает только движение ВПЕРЁД: пятиться с разгона тачка не
            // должна, это выглядело бы как ошибка физики.
            if (fwd > 0.1f)
                _momentum += dt * _momentumGain * over * _speed01;
        }

        if (Input.GetKey(KeyCode.S))
        {
            // Тормозной путь ДЕЛИТСЯ на загрузку: чем тяжелее, тем дольше.
            // Это и есть цена разгона с полной тачкой.
            _momentum -= dt * _brakeRate / Mathf.Max(0.4f, Load);
        }
        else
        {
            _momentum -= dt * _coastDrain;
        }

        _momentum = Mathf.Clamp01(_momentum);

        // ТЯНЕТ ВПЕРЁД. Толкаем каждый кадр небольшими порциями: внешняя
        // скорость в контроллере гаснет дрэгом, и единичный толчок исчез бы
        // за долю секунды вместо того, чтобы нести игрока.
        if (_momentum > 0.02f && _holderFpc != null)
        {
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            fwd.Normalize();

            _holderFpc.AddPush(fwd * (_carryPush * _momentum * dt * 4f));
        }
    }

    /// <summary>
    /// КРЕН ОТ ПОВОРОТА.
    ///
    /// Тачка на одной оси встаёт на внешнее колесо ровно тогда, когда её
    /// поворачивают, — поэтому руль здесь почти единственный источник.
    /// Скорость, загрузка и набранный ход только усиливают то, что уже есть:
    /// стоящую тачку валить не должно ничем.
    /// </summary>
    private void TickLean(float dt)
    {
        if (_holder == null || _cart.IsTipped || _phase == Phase.Thrown)
        {
            _lean = Mathf.MoveTowards(_lean, 0f, dt * 2.5f);
            return;
        }

        float loadMul = Mathf.Max(0f, _loadCurve.Evaluate(Load));

        // Повернул направо — встал на левое колесо — валит вправо.
        float turn = Input.GetAxisRaw("Horizontal");
        float drive = turn * _fromTurn * _speed01 * loadMul;

        // ── ПРЯМОЙ ВКЛАД ──
        //
        // Крен раньше набирался ТОЛЬКО через скорость: руль толкал _leanVel,
        // а тот уже двигал _lean. Два интегрирования подряд — это всегда вязко
        // и с запозданием, сколько ни крути силу: сначала должна разогнаться
        // скорость, и только потом поедет сам крен.
        //
        // Прямой вклад идёт в крен минуя скорость — одна ступень вместо двух.
        // Тачка отзывается на руль СРАЗУ, а инерционный путь остаётся рядом и
        // по-прежнему даёт раскачку и перелёт через ноль. Убери его в ноль —
        // вернётся прежнее поведение.
        _lean += drive * _leanDirect * dt;

        // Перлин, а не синус: у синуса слышен период, и тряска через минуту
        // читается как метроном.
        float bump = (Mathf.PerlinNoise(_bumpSeed + Time.time * _bumpFrequency, 0.4f) - 0.5f)
                   * 2f * _fromBumps * _speed01 * loadMul;

        _leanVel += (drive + bump) * dt;
        _leanVel *= 1f - Mathf.Clamp01(dt * Mathf.Lerp(6f, 1.2f, _leanInertia));

        float amplify = 1f + _speed01 * _speedAmplify + _momentum * _momentumLean;
        _lean += _leanVel * dt * amplify;

        // Возврат работает тем сильнее, чем медленнее едешь.
        float rec = _recovery * (1f - _speed01 * 0.7f) / Mathf.Max(0.15f, loadMul);
        _lean = Mathf.MoveTowards(_lean, 0f, rec * dt);

        _lean = Mathf.Clamp(_lean, -1.5f, 1.5f);
    }

    private void TickPhase(float dt)
    {
        if (_cooldownLeft > 0f) _cooldownLeft -= dt;

        switch (_phase)
        {
            case Phase.Calm:
            {
                if (_holder == null || _cart.IsTipped) break;
                if (_cooldownLeft > 0f || _lockout > 0f) break;

                // СВЕРХПЕРЕГРУЗ проверяется ПЕРВЫМ и по своему, меньшему
                // порогу: там ничего не разыгрывается, там сразу исход.
                if (IsSuper && Mathf.Abs(_lean) >= _throwAt)
                {
                    ThrowRider();
                    break;
                }

                if (Mathf.Abs(_lean) >= _warnAt)
                {
                    _leanRight = _lean > 0f;
                    _phase = Phase.Warn;
                    _phaseTimer = _telegraph;
                    Log($"крен {_lean:0.00} → нужна {RequiredLabel}");
                }
                break;
            }

            case Phase.Warn:
            {
                _phaseTimer -= dt;

                if (IsSuper && Mathf.Abs(_lean) >= _throwAt) { ThrowRider(); break; }

                // Успел выровняться до открытия окна — событие отменяется.
                // Это награда за осторожность, и без неё телеграф был бы
                // формальностью.
                if (Mathf.Abs(_lean) < _warnAt * 0.7f) { _phase = Phase.Calm; break; }

                if (_phaseTimer <= 0f)
                {
                    _phase = Phase.Call;
                    _windowLeft = CurrentWindow;
                }
                break;
            }

            case Phase.Call:
            {
                _windowLeft -= dt;

                if (IsSuper && Mathf.Abs(_lean) >= _throwAt) { ThrowRider(); break; }

                if (Input.GetKeyDown(RequiredKey)) { Succeed(); break; }

                KeyCode wrong = RequiredKey == KeyCode.A ? KeyCode.D : KeyCode.A;
                if (Input.GetKeyDown(wrong)) { Fail(true); break; }

                if (_windowLeft <= 0f) Fail(false);
                break;
            }

            case Phase.Hit:
            case Phase.Miss:
            case Phase.Thrown:
            {
                _phaseTimer -= dt;
                if (_phaseTimer <= 0f)
                {
                    _phase = Phase.Calm;
                    _cooldownLeft = _cooldown;
                }
                break;
            }
        }
    }

    private void Succeed()
    {
        _lean *= 1f - _pushBack;
        _leanVel *= 0.25f;

        _combo++;
        _phase = Phase.Hit;
        _phaseTimer = 0.35f;

        Log($"удержал, серия {_combo}");
    }

    private void Fail(bool wrongKey)
    {
        _combo = 0;

        _lean += (_leanRight ? 1f : -1f) * _missAdds;
        _leanVel += (_leanRight ? 1f : -1f) * 0.6f;

        bool tips = _flipOnAnyMiss || Mathf.Abs(_lean) >= _tipAt;

        if (_flipEnabled && tips)
        {
            FlipOut(_leanRight, wrongKey);
            return;
        }

        _phase = Phase.Miss;
        _phaseTimer = 0.45f;

        CmdStumble(_missStumble);

        Log(wrongKey ? "не та клавиша" : "не успел");
    }

    /// <summary>
    /// ТАЧКА ЛОЖИТСЯ НАБОК. Исход провала, а не наказание за него.
    ///
    /// ═══ ПОЧЕМУ НЕ ЧЕРЕЗ ФЛАГ «ОПРОКИНУТА» ═══
    ///
    /// У тачки уже есть состояние опрокидывания на ПКМ — но там она ложится по
    /// анимации, оставаясь в руках и под управлением. Здесь нужно другое: она
    /// должна ВЫРВАТЬСЯ и упасть сама. Поэтому ручки отпускаются, тело
    /// становится обычным динамическим, и дальше падение считает физика.
    ///
    /// Разница видна сразу: анимация всегда кладёт тачку одинаково, а физика
    /// уронит её на склоне иначе, чем на ровном, и зацепит то, что рядом.
    /// Проигрыш от этого перестаёт выглядеть заскриптованным.
    ///
    /// ═══ ПОЧЕМУ ИГРОКА КИДАЕТ В ТУ ЖЕ СТОРОНУ ═══
    ///
    /// Их не растащило взрывом — у них вырвало из рук общую вещь, и обоих
    /// повело туда, куда валилась она. Разлёт в разные стороны читался бы как
    /// отталкивание, то есть как совсем другое событие.
    /// </summary>
    private void FlipOut(bool toRight, bool wrongKey)
    {
        // Переиспользуем фазу броска: снаружи это одно и то же состояние —
        // «исход разыгран, ввод больше не слушаем, идёт пауза».
        _phase = Phase.Thrown;
        _phaseTimer = 1.2f;
        _lockout = _throwLockout;

        _combo = 0;
        _lean = 0f;
        _leanVel = 0f;
        _momentum = 0f;
        _cooldownLeft = _cooldown * 2.5f;

        Vector3 side = transform.right * (toRight ? 1f : -1f);
        side.y = 0f;

        if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
        side.Normalize();

        // Локально — сразу, чтобы толчок не ждал ответа сервера.
        if (_holderFpc != null)
        {
            _holderFpc.LaunchUp(_flipPlayerLift);
            _holderFpc.AddPush(side * _flipPlayerPush);
        }

        CmdFlip(_holderNo, side);

        Log(wrongKey ? "не та клавиша — тачка легла" : "не успел — тачка легла");
    }

    /// <summary>
    /// ЛОБОВОЙ УДАР. Тачка кувыркается ЧЕРЕЗ ПЕРЁД, игрок летит следом.
    ///
    /// ═══ ЧЕМ ЭТО ОТЛИЧАЕТСЯ ОТ ПРОВАЛА МИНИ-ИГРЫ ═══
    ///
    /// Ось переворота другая, и это не мелочь. Провал баланса кладёт тачку
    /// НАБОК — вокруг продольной оси, туда же, куда её и валило. Удар в стену
    /// швыряет её ЧЕРЕЗ НОС — вокруг поперечной.
    ///
    /// Механизм при этом один и тот же: ServerFlipOver крутит тачку вокруг оси
    /// «вверх × направление». Подставь вбок — ляжет набок; подставь вперёд —
    /// перекувыркнётся. Второй оси и второго метода не нужно.
    ///
    /// Раньше удар вызывал бросок игрока и обычную встряску тачки. Игрок летел,
    /// а тачка оставалась стоять как ни в чём не бывало — самое неубедительное
    /// место во всей связке.
    /// </summary>
    public void CrashOut(Vector3 travelDir, float strength01, string reason)
    {
        if (ThrowBusy) return;

        travelDir.y = 0f;
        if (travelDir.sqrMagnitude < 0.0001f) travelDir = transform.forward;
        travelDir.Normalize();

        strength01 = Mathf.Clamp(strength01, 0.2f, 2f);

        _phase = Phase.Thrown;
        _phaseTimer = 1.2f;
        _lockout = _throwLockout;

        _combo = 0;
        _lean = 0f;
        _leanVel = 0f;
        _momentum = 0f;
        _cooldownLeft = _cooldown * 2f;

        if (_holderFpc != null)
        {
            _holderFpc.LaunchUp(_throwUp * strength01);
            _holderFpc.AddPush(travelDir * (_throwForward * strength01));
        }

        CmdCrash(_holderNo, travelDir, strength01);

        Log($"{reason}: тачка кувыркнулась через перёд");
    }

    [ServerRpc(RequireOwnership = false)]
    private void CmdCrash(NetworkObject holder, Vector3 dir, float strength01)
    {
        if (holder != null)
        {
            FirstPersonController fpc = holder.GetComponent<FirstPersonController>();

            if (fpc != null)
                fpc.ServerApplyKnockback(
                    (dir * _throwForward + Vector3.up * _throwUp) * strength01);
        }

        if (_cart != null) _cart.ServerFlipOver(dir, strength01);
    }

    [ServerRpc(RequireOwnership = false)]
    private void CmdFlip(NetworkObject holder, Vector3 side)
    {
        if (holder != null)
        {
            FirstPersonController fpc = holder.GetComponent<FirstPersonController>();

            if (fpc != null)
                fpc.ServerApplyKnockback(side * _flipPlayerPush
                                       + Vector3.up * _flipPlayerLift);
        }

        // Порядок: сначала груз и ручки, потом импульс. Наоборот тачка успела
        // бы начать падать, всё ещё будучи кинематической, — то есть не начала
        // бы вовсе.
        if (_cart != null) _cart.ServerFlipOver(side, _tipStumble);
    }

    /// <summary>
    /// БРОСОК ЧЕРЕЗ ТАЧКУ.
    ///
    /// Сверхперегруз — не «очень сложно», а другой исход. Мини-игра там не
    /// выигрывается, и это честно: игрок должен понять, что столько грузить
    /// нельзя, а не что он плохо жмёт кнопки.
    ///
    /// Порядок важен: сначала вырвать ручки, потом бросить. Наоборот игрок
    /// улетит, продолжая держаться, и тачка поедет за ним.
    /// </summary>
    private void ThrowRider() => ThrowOverCart(1f, "СВЕРХПЕРЕГРУЗ");

    /// <summary>Идёт ли бросок прямо сейчас, или пауза после него.</summary>
    public bool ThrowBusy => _phase == Phase.Thrown || _lockout > 0f;

    /// <summary>
    /// ПЕРЕКИНУТЬ ИГРОКА ЧЕРЕЗ ТАЧКУ. Публичный, потому что причин для этого
    /// больше одной: сверхперегруз и удар с разгона в стену.
    ///
    /// Сама последовательность одна и та же, и дублировать её во втором месте
    /// нельзя — порядок шагов здесь неочевидный и легко переставляется
    /// местами при копировании, а переставленный он ломается тихо.
    /// </summary>
    /// <param name="strength01">Доля от полной силы броска.</param>
    /// <param name="reason">Что писать в лог.</param>
    public void ThrowOverCart(float strength01 = 1f, string reason = "бросок")
    {
        if (ThrowBusy) return;

        strength01 = Mathf.Clamp(strength01, 0.2f, 2f);

        _phase = Phase.Thrown;
        _phaseTimer = 0.9f;
        _lockout = _throwLockout;

        _combo = 0;
        _lean = 0f;
        _leanVel = 0f;
        _momentum = 0f;

        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        fwd.Normalize();

        // Локально у себя — сразу, чтобы бросок не ждал ответа сервера:
        // задержка здесь читается как залипание управления.
        if (_holderFpc != null)
        {
            _holderFpc.LaunchUp(_throwUp * strength01);
            _holderFpc.AddPush(fwd * (_throwForward * strength01));
        }

        CmdThrow(_holderNo, fwd, strength01);

        Log($"{reason}: перекинуло через тачку");
    }

    [ServerRpc(RequireOwnership = false)]
    private void CmdThrow(NetworkObject holder, Vector3 forward, float strength01)
    {
        // Вырвать ручки. ServerInteract на том, кто уже держит, работает как
        // отпускание — отдельного публичного метода в PushCart нет, и лезть
        // в его приватную кухню ради этого не стоит.
        if (holder != null && _cart != null)
        {
            PlayerInteractor pi = holder.GetComponent<PlayerInteractor>();
            if (pi != null) _cart.ServerInteract(pi);

            FirstPersonController fpc = holder.GetComponent<FirstPersonController>();
            if (fpc != null)
                fpc.ServerApplyKnockback(
                    (forward * _throwForward + Vector3.up * _throwUp) * strength01);
        }

        if (_cart != null)
        {
            _cart.ServerStumble(Mathf.Clamp01(strength01));

            // Груз летит ВПЕРЁД: корыто пинает содержимое по своему forward,
            // а тачка в этот момент клюёт носом.
            if (_cart.Trough != null) _cart.Trough.ServerKickOut();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void CmdStumble(float strength01)
    {
        if (_cart != null) _cart.ServerStumble(strength01);
    }

    /// <summary>
    /// Наклон и подъём живут на ДОЧЕРНЕМ объекте: корень ведёт PushCart через
    /// MovePosition и каждый кадр перезаписывает его поворот целиком.
    /// </summary>
    private void TickVisual(float dt)
    {
        // ── СКОЛЬКО СЕЙЧАС ПОДЪЁМА ──
        //
        // Два источника, и берётся БОЛЬШИЙ, а не сумма. Сумма дала бы подъём
        // от резкого руля ПЛЮС от накопленного крена, то есть двойной счёт
        // одной и той же причины: крен-то и набирается рулём.
        float fromLean = Mathf.InverseLerp(_wheelieFrom, _wheelieFull, Mathf.Abs(_lean));

        // Поворот считается только на ходу: провернуть руль стоя — это не
        // занос, а разворот на месте, и вставать там не на что.
        float turn = _holder != null ? Mathf.Abs(Input.GetAxisRaw("Horizontal")) : 0f;
        float fromTurn = turn * _speed01 * _wheelieFromTurn;

        float wantWheelie = _wheelieEnabled && !_cart.IsTipped
            ? Mathf.Clamp01(Mathf.Max(fromLean, fromTurn)) : 0f;

        // Подъём идёт в сторону крена. Нулевой крен при живом руле — тачка
        // только начала валиться, и стороны ещё нет: тогда подъёма тоже нет,
        // иначе он дёрнулся бы в случайную сторону.
        if (Mathf.Abs(_lean) < 0.02f) wantWheelie = 0f;

        _wheelie = Mathf.SmoothDamp(_wheelie, wantWheelie, ref _wheelieVel, _wheelieSmooth);

        // ── КРЕН ──
        //
        // Добавка приходит только вместе с подъёмом: на двух колёсах лишние
        // градусы выглядели бы просто кривым кузовом.
        // Знак меняется ТОЛЬКО у картинки. RequiredKey считается от _lean
        // напрямую и остаётся прежней — иначе галка «встаёт не туда» тихо
        // перевернула бы ещё и подсказку, и мини-игра стала бы врать.
        float side = _flipWheelieSide ? -1f : 1f;

        float wantRoll = side * (-_lean * _visualRollDeg
                               - Mathf.Sign(_lean) * _wheelieExtraDeg * _wheelie);

        // ПОДЪЁМ ПО ВЕРТИКАЛИ.
        //
        // Пока тачка стоит на двух колёсах, её надо приподнимать руками —
        // иначе крен выглядит как наклон, а не как отрыв. Но как только она
        // встаёт на одно, разворот вокруг пятна контакта поднимает её САМ, по
        // честной дуге, и ручная добавка начинает задирать её в воздух поверх
        // этого. Поэтому одно плавно уступает другому.
        float wantY = _affectPosition
            ? Mathf.Abs(_lean) * _visualLift * (1f - _wheelie) : 0f;

        _visualRoll = Mathf.SmoothDamp(_visualRoll, wantRoll, ref _visualRollVel, _visualSmooth);
        _visualY = Mathf.SmoothDamp(_visualY, wantY, ref _visualYVel, _visualSmooth);

        // ОТДАЁМ ХОДОВОЙ, а не пишем сами.
        //
        // У кузова ровно один владелец — CartRig. Он складывает качку от
        // неровностей с этим креном и применяет всё одной записью. Два
        // источника, пишущие один трансформ, затирали бы друг друга, и кто
        // победит, зависело бы от порядка вызова Update.
        if (_rig != null)
        {
            _rig.BalanceRoll = _visualRoll;
            _rig.BalanceLift = _visualY;
            _rig.BalanceWheelie01 = _wheelie;
            return;
        }

        // Запасной путь: ходовой нет, пишем трансформ напрямую.
        if (_visualRoot == null) return;

        _visualRoot.localRotation = _visualBaseRot * Quaternion.Euler(0f, 0f, _visualRoll);

        if (_affectPosition)
            _visualRoot.localPosition = _visualBasePos + Vector3.up * _visualY;
    }

    private void ResetAll()
    {
        _lean = 0f; _leanVel = 0f; _combo = 0; _momentum = 0f;
        _phase = Phase.Calm; _windowLeft = 0f; _cooldownLeft = 0f;
    }

    private void Log(string msg)
    {
        if (_debugLog) Debug.Log($"[Баланс] {msg}", this);
    }

    // ───────────────────── управление из игры ─────────────────────

    public void SetLevel(LoadLevel level)
    {
        _level = level;
        _useRealMass = false;
        Log($"загрузка: {level} ({Load:0.00})");
    }

    /// <summary>Толкнуть вручную: наезд на доску, удар, порыв ветра.</summary>
    public void Nudge(float amount, bool toRight)
        => _leanVel += (toRight ? 1f : -1f) * Mathf.Abs(amount);

    // ═════════════════════════ ДИАГНОСТИКА ═════════════════════════

    /// <summary>
    /// Правой кнопкой по компоненту. Проверяет всю цепочку и печатает разбор.
    /// </summary>
    [ContextMenu("★ ПОЧЕМУ НЕ РАБОТАЕТ")]
    public void Diagnose()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        int problems = 0;

        sb.AppendLine($"═══ БАЛАНС ТАЧКИ: {name} ═══\n");

        // ── тачка ──
        if (_cart == null) _cart = GetComponent<PushCart>();

        if (_cart == null)
        {
            Debug.LogError($"[Баланс] «{name}»: нет PushCart на объекте.", this);
            return;
        }

        Rigidbody rb = _cart.GetComponent<Rigidbody>();

        sb.AppendLine($"Тачка: держат {_cart.IsGripped}, опрокинута {_cart.IsTipped}");
        sb.AppendLine($"Rigidbody: kinematic {(rb != null ? rb.isKinematic.ToString() : "НЕТ ТЕЛА")}");
        sb.AppendLine($"Загрузка: {_level} = {Load:0.00}{(IsSuper ? "  ← СВЕРХ" : "")}");
        sb.AppendLine($"Держатель найден: {(_holder != null ? _holder.name : "НЕТ")}");
        sb.AppendLine($"Крен {_lean:+0.00;-0.00}   ход {_momentum:0.00}   фаза {_phase}");

        // ── визуальный корень ──
        sb.AppendLine($"\n── VISUAL ROOT ──");

        if (_visualRoot == null)
        {
            sb.AppendLine(_rig != null
                ? "   не задан — И ПРАВИЛЬНО. Это ЗАПАСНОЙ путь на случай, " +
                  "когда CartRig нет. Ходовая есть, значит кузов пишет она " +
                  "одна, и заполнять это поле НЕ НУЖНО: заполнишь — получишь " +
                  "двух владельцев одного трансформа."
                : "   не задан — наклона не будет, но это не поломка");
        }
        else
        {
            sb.AppendLine($"   {_visualRoot.name}");

            if (_visualRoot == transform)
            {
                sb.AppendLine("\n❌❌ ЭТО САМ КОРЕНЬ ТАЧКИ.");
                sb.AppendLine("   Наклон переписывает тот же трансформ, который " +
                              "PushCart ведёт через MovePosition. Тачка зависает " +
                              "на месте — выглядит как отказ физики.");
                sb.AppendLine("   ЛЕЧЕНИЕ: создай дочерний объект с кузовом.");
                problems++;
            }

            if (_visualRoot.GetComponent<Rigidbody>() != null)
            {
                sb.AppendLine("\n❌ На Visual Root есть Rigidbody. Наклонять " +
                              "физическое тело нельзя.");
                problems++;
            }

            Collider[] cols = _visualRoot.GetComponentsInChildren<Collider>(true);

            if (cols.Length > 0)
            {
                sb.AppendLine($"\n⚠ Внутри {cols.Length} коллайдеров. Наклон будет " +
                              "их двигать, и стенки корыта поедут относительно " +
                              "груза. Для кузова это нормально, для рамы — нет.");
            }
        }

        // ── ходовая ──
        sb.AppendLine($"\nХодовая часть: " +
                      (_rig != null ? $"есть, колёс {_rig.WheelCount}" : "НЕТ"));

        if (_rig == null)
            sb.AppendLine("   Без CartRig наклон пишется прямо в Visual Root — " +
                          "работает, но качки от неровностей не будет.");

        sb.AppendLine($"\n═══ ПРОБЛЕМ: {problems} ═══");

        if (problems == 0)
        {
            sb.AppendLine("Со стороны баланса всё чисто. Если тачка всё равно " +
                          "висит:");
            sb.AppendLine("  • проверь, что Rigidbody кинематический ТОЛЬКО пока " +
                          "за ручки держатся — это делает PushCart;");
            sb.AppendLine("  • сними CartBalance временно и посмотри, вернётся ли " +
                          "поведение: так сразу видно, он виноват или нет.");
        }

        if (problems > 0) Debug.LogWarning(sb.ToString(), this);
        else Debug.Log(sb.ToString(), this);
    }

    private void OnValidate()
    {
        if (Application.isPlaying) return;
        ValidateVisualRoot();
    }

    private void OnGUI()
    {
        if (!_showReadout) return;

        GUI.Label(new Rect(10, 560, 660, 74),
            $"БАЛАНС  крен {_lean:+0.00;-0.00}   фаза {_phase}   серия {_combo}\n" +
            $"загрузка {_level} ({Load:0.00}){(IsSuper ? "  ← СВЕРХ" : "")}   " +
            $"скорость {_speed01:0.00}   окно {CurrentWindow:0.00} сек\n" +
            $"ход {_momentum:0.00}{(Braking ? "  [тормоз]" : "")}   " +
            $"держит: {(_holder != null ? "да" : "нет")}");
    }
}
