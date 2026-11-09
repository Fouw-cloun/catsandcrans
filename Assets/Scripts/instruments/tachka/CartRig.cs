using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ХОДОВАЯ ЧАСТЬ ТАЧКИ. Подвеска, колёса и качка кузова в одном месте.
///
/// ═══ ЗАЧЕМ ОБЪЕДИНЯТЬ ═══
///
/// Раньше подвеска считалась дважды: один компонент делал раскаст на каждое
/// колесо, второй — по тем же точкам для кузова, и оба писали разные
/// трансформы. Помимо двойной работы это давало обратную связь:
///
///   подвеска качнула кузов → вместе с ним поехала точка Mount →
///   колесо померило высоту от уехавшей точки → решило, что тачка тронулась →
///   закрутилось на месте
///
/// Именно так и выглядела поломка: тачка стоит, а колесо крутится. Причём у
/// двух колёс дуга направлена в разные стороны, поэтому казалось, что крутится
/// только одно.
///
/// Здесь всё считается ОДИН раз и пишется ОДНИМ владельцем.
///
/// ═══ ГЛАВНОЕ РЕШЕНИЕ: ТОЧКИ ПОКОЯ ЗАМОРОЖЕНЫ ═══
///
/// Положение каждой точки опоры запоминается в системе координат КОРНЯ тачки
/// один раз при старте. Дальше живой трансформ Mount не читается вообще — он
/// нужен только чтобы удобно расставить точки в редакторе.
///
/// Из этого следует, что иерархия перестаёт иметь значение: Mount может лежать
/// хоть внутри качающегося кузова, петля всё равно невозможна. Раньше за этим
/// приходилось следить руками, и одна перестановка объектов ломала всё.
///
/// ═══ ПОЧЕМУ НЕ WheelCollider ═══
///
/// Пока тачку катят, её Rigidbody КИНЕМАТИЧЕСКИЙ: движение идёт через
/// PushCart.MovePosition, чтобы стенки корыта честно толкали груз. Силы на
/// кинематическое тело не действуют, поэтому настоящая пружина ничего бы не
/// сделала.
///
/// Считаем в обратную сторону: не подвеска двигает тачку, а КОЛЁСА СООБЩАЮТ
/// КУЗОВУ, что под ними. Для взгляда игрока разницы нет — наехал на доску,
/// кузов качнулся и отыграл, — но ничто не спорит с MovePosition за то, кто
/// на самом деле везёт тачку.
///
/// ═══ СБОРКА ═══
///
///   Cart            — PushCart, Rigidbody, CartRig, CartBalance, CartDust
///   ├ Body          — кузов и корыто. Его качает ЭТОТ компонент
///   ├ MountL/R      — точки опоры, где угодно в иерархии
///   └ WheelL/R      — меши колёс, где угодно
///
/// Отдельные компоненты на колёсах не нужны: список колёс живёт здесь, и
/// подвеска у них общая — только так кузов может знать разницу между левым и
/// правым сжатием, а без неё нет ни крена, ни амортизации.
/// </summary>
[AddComponentMenu("CatCooks/Тачка/Cart Rig")]
public class CartRig : MonoBehaviour
{
    [System.Serializable]
    public class Wheel
    {
        [Tooltip("Меш колеса. Его позицию и вращение пишет этот компонент.")]
        public Transform mesh;

        [Tooltip("Точка опоры на раме. Читается ОДИН РАЗ при старте — дальше " +
                 "её можно двигать, перемещать по иерархии или удалять, на " +
                 "работу это не повлияет.")]
        public Transform mount;

        [Tooltip("НАСТОЯЩИЙ радиус колеса, метры.\n\n" +
                 "ЕСЛИ КОЛЕСО ТОНЕТ В ЗЕМЛЕ — почти всегда дело здесь. " +
                 "Подвеска ставит центр на высоту «точка касания + радиус». " +
                 "Радиус меньше настоящего — центр вычисляется ниже, и шина " +
                 "уходит под землю ровно на разницу.")]
        [Min(0.01f)] public float radius = 0.35f;

        [Tooltip("Ось вращения в местных координатах колеса. Обычно (1,0,0).\n\n" +
                 "Если колесо крутится не туда или буксует — меняй ЭТО, а не " +
                 "геометрию: почти всегда дело в оси, а не в физике.")]
        public Vector3 spinAxis = Vector3.right;

        [Tooltip("Вес точки в качке кузова. У колёс больше, у ножек меньше.")]
        [Range(0f, 2f)] public float weight = 1f;

        [Tooltip("Подъём точки покоя, метры. Быстрая подгонка на глаз, если " +
                 "колесо чуть тонет или висит.")]
        public float rideOffset = 0f;

        // ── состояние ──
        [System.NonSerialized] public Vector3 restLocal;   // заморожено при старте

        /// <summary>
        /// АВТОРСКИЙ ПОВОРОТ МЕША относительно корня, тоже заморожен.
        ///
        /// Без него ходовая ставила колесу поворот корня плюс отмотанный угол
        /// качения — и начисто стирала то, как колесо было развёрнуто в
        /// редакторе. Для колеса с единичным поворотом разницы нет, поэтому
        /// одно из двух обычно выглядит правильно, а второе развёрнуто набок:
        /// экспорт из Blender сплошь и рядом кладёт правому колесу поворот на
        /// 180°, чтобы протектор смотрел наружу.
        /// </summary>
        [System.NonSerialized] public Quaternion restRotLocal;
        [System.NonSerialized] public float offset;        // сжатие, метры
        [System.NonSerialized] public float velocity;
        [System.NonSerialized] public float spinAngle;
        [System.NonSerialized] public bool grounded;
        [System.NonSerialized] public Vector3 contact;
        [System.NonSerialized] public Vector3 normal;
        [System.NonSerialized] public float impact;

        // ПОЗА МЕША ДО ПОДЪЁМА НА ОДНО КОЛЕСО.
        //
        // Раньше меш писался прямо в TickWheels. Теперь между расчётом подвески
        // и записью появился ещё один шаг: подъём на одно колесо разворачивает
        // ВСЮ картинку вокруг пятна контакта опорного колеса, и знать это пятно
        // можно только после того, как посчитаны все колёса. Поэтому поза
        // сначала копится здесь, а пишется одним проходом в конце.
        [System.NonSerialized] public Vector3 poseP;
        [System.NonSerialized] public Quaternion poseR;
    }

    [Header("━━━ Колёса и опоры ━━━")]
    [Tooltip("Пусто — соберутся по именам объектов, содержащим «wheel» или " +
             "«колесо». Это удобство на первую настройку: найденное сразу " +
             "пишется в этот список и дальше живёт само.\n\n" +
             "РАДИУСЫ после автосборки проверь обязательно — ставится 0.35.")]
    [SerializeField] private List<Wheel> _wheels = new List<Wheel>();

    [Tooltip("КУЗОВ. Его качает подвеска и кренит баланс.\n\n" +
             "НЕ корень тачки: корень ведёт PushCart через MovePosition, и " +
             "два источника дрались бы за один трансформ — тачка зависает на " +
             "месте, будто физика на неё не действует.")]
    [SerializeField] private Transform _body;

    [Header("━━━ Пружина ━━━")]
    [Tooltip("Ход подвески от точки покоя, метры")]
    [Range(0.02f, 0.5f)][SerializeField] private float _travel = 0.13f;

    [Min(1f)][SerializeField] private float _stiffness = 220f;

    [Tooltip("ЗАТУХАНИЕ. Ниже единицы намеренно: недодемпфированная пружина " +
             "ПРОСКАКИВАЕТ цель и качается — именно это читается как " +
             "подвеска. При единице кузов просто следит за землёй, и " +
             "амортизации не чувствуется.")]
    [Range(0.1f, 2f)][SerializeField] private float _damping = 0.6f;

    [Tooltip("Слои земли. Без слоя самой тачки — иначе колесо будет мерить " +
             "высоту от собственной рамы.")]
    [SerializeField] private LayerMask _groundMask = ~0;

    [Header("━━━ Как это видно на кузове ━━━")]
    [Tooltip("Насколько кузов проседает вместе с колёсами. Единица — один в " +
             "один за средним сжатием.")]
    [Range(0f, 2f)][SerializeField] private float _heave = 0.8f;

    [Tooltip("Крен от разницы левых и правых опор, градусов на метр разницы")]
    [Range(0f, 250f)][SerializeField] private float _rollPerMeter = 95f;

    [Tooltip("Тангаж от разницы передних и задних. С одной осью работает " +
             "только если добавить точку у ножек или ручек.")]
    [Range(0f, 250f)][SerializeField] private float _pitchPerMeter = 75f;

    [Range(0f, 45f)][SerializeField] private float _maxAngle = 15f;
    [Range(0.01f, 0.4f)][SerializeField] private float _smooth = 0.06f;

    [Header("━━━ Удары ━━━")]
    [Tooltip("С какого рывка считать это ударом. Удар — это резкая смена " +
             "скорости сжатия, а НЕ само сжатие: медленно въехать на бордюр " +
             "и влететь в него дают одинаковое сжатие, но разные события.")]
    [Min(0.1f)][SerializeField] private float _impactAt = 1.2f;

    // ═════════════════════════ СВОЯ ЖИЗНЬ КУЗОВА ═════════════════════════

    [Header("━━━ Своя тряска (не зависит от дороги) ━━━")]
    [Tooltip("ЗАЧЕМ ОНА, если есть подвеска.\n\n" +
             "Подвеска отрабатывает только то, что реально лежит под колёсами. " +
             "На ровном бетоне под ней ничего нет, и тачка едет как по стеклу — " +
             "на глаз это читается как «скользит по земле», а не «катится».\n\n" +
             "Эта тряска — про саму тачку, а не про дорогу: разболтанная ось, " +
             "гуляющее колесо, дребезг рамы. Поэтому она есть ВСЕГДА и не " +
             "спрашивает, по чему едут.")]
    [Range(0f, 6f)][SerializeField] private float _shakeRoll = 0.9f;

    [Range(0f, 6f)][SerializeField] private float _shakePitch = 1.2f;

    [Tooltip("Вертикальный дребезг, метры. Держи мелким: выше 3 см это уже не " +
             "дребезг, а вторая подвеска поверх первой.")]
    [Range(0f, 0.06f)][SerializeField] private float _shakeHeave = 0.012f;

    [Tooltip("Частота. Перлин, а не синус: у синуса на длинной прямой слышен " +
             "период, и тряска через минуту читается как метроном.")]
    [Range(0.2f, 12f)][SerializeField] private float _shakeFreq = 4.5f;

    [Tooltip("СКОЛЬКО ТРЯСКИ ОСТАЁТСЯ У СТОЯЩЕЙ ТАЧКИ.\n\n" +
             "Ноль — стоит мёртво, и это выглядит как выключенная игра. " +
             "Единица — трясётся стоя, и это выглядит как баг. Небольшая доля " +
             "читается как «живая вещь, которую держат руками».")]
    [Range(0f, 1f)][SerializeField] private float _shakeAtRest = 0.18f;

    [Tooltip("Скорость, на которой тряска выходит на полную, м/с")]
    [Min(0.2f)][SerializeField] private float _shakeFullAt = 2.5f;

    [Header("━━━ Подскоки ━━━")]
    [Tooltip("Редкий толчок в пружину случайного колеса. Не отдельная анимация: " +
             "импульс уходит в ту же пружину, что отрабатывает кочки, поэтому " +
             "подскок сам собой даёт и крен, и качку, и сплющивание — всё, что " +
             "уже умеет ходовая.")]
    [SerializeField] private bool _hopsEnabled = true;

    [Range(0.2f, 12f)][SerializeField] private float _hopEveryMin = 1.8f;
    [Range(0.2f, 20f)][SerializeField] private float _hopEveryMax = 5f;

    [Tooltip("Сила толчка. Выше 1.5 тачка начинает козлить.")]
    [Range(0f, 3f)][SerializeField] private float _hopForce = 0.8f;

    [Tooltip("НЕРАВНОМЕРНОСТЬ. Ноль — оба колеса подскакивают одинаково, и " +
             "тачка просто прыгает вверх. Ближе к единице одно колесо получает " +
             "заметно больше — тачку клюёт вбок, и это читается как «поймала " +
             "камень», а не как «сработал скрипт».")]
    [Range(0f, 1f)][SerializeField] private float _hopUneven = 0.7f;

    [Tooltip("Ниже этой скорости не подскакивает: стоящая тачка, прыгающая " +
             "сама по себе, выглядит сломанной.")]
    [Range(0f, 3f)][SerializeField] private float _hopMinSpeed = 0.6f;

    [Header("━━━ Мультяшное сплющивание ━━━")]
    [Tooltip("ЧТО МАСШТАБИРОВАТЬ. Пусто — кузов.\n\n" +
             "ЛУЧШЕ ЗАВЕСТИ ОТДЕЛЬНЫЙ ПУСТЫШКУ-ДЕТЁНЫША С ОДНИМИ МЕШАМИ: " +
             "масштаб тянет за собой коллайдеры, а значит стенки корыта поедут " +
             "относительно груза и будут его пинать на каждом сжатии. Для чисто " +
             "визуального объекта такой проблемы нет.")]
    [SerializeField] private Transform _squashTarget;

    [Tooltip("СЖАТИЕ ОТ ДВИЖЕНИЯ КУЗОВА. Едет вниз — приседает и раздаётся " +
             "вширь, идёт вверх — вытягивается. Это и есть мультяшный squash " +
             "and stretch: объём как будто сохраняется.")]
    [Range(0f, 1f)][SerializeField] private float _squashFromMotion = 0.35f;

    [Tooltip("Хлопок от удара. Отдельно от движения: на удар кузов должен " +
             "приседать резко, а не так же плавно, как на кочке.")]
    [Range(0f, 1f)][SerializeField] private float _squashFromImpact = 0.5f;

    [Tooltip("ПОТОЛОК. 0.18 — это ±18% высоты, уже отчётливо мультяшно. Выше " +
             "0.3 меш начинает выглядеть порванным, особенно на колёсах.")]
    [Range(0f, 0.6f)][SerializeField] private float _squashLimit = 0.18f;

    [Tooltip("Насколько раздаётся вширь относительно приседания. 0.5 — " +
             "классическая половина, объём примерно сохраняется.")]
    [Range(0f, 1f)][SerializeField] private float _squashWidth = 0.5f;

    [Range(0.01f, 0.3f)][SerializeField] private float _squashSmooth = 0.05f;

    [Tooltip("Скорость кузова, принятая за полное сплющивание, м/с.\n\n" +
             "Держи МАЛЕНЬКОЙ. Кузов ходит вверх-вниз медленно — доли метра в " +
             "секунду, — и если поставить сюда «скорость тачки», сплющивание " +
             "никогда не наберётся и будет выглядеть выключенным.")]
    [Range(0.1f, 2f)][SerializeField] private float _squashSpeedRef = 0.45f;

    [Tooltip("ДРОЖЬ НА ХОДУ. Ритмичное сплющивание просто от того, что тачка " +
             "едет, без всякой кочки.\n\n" +
             "Нужна ровно затем же, зачем своя тряска: остальное сплющивание " +
             "отзывается на события, а на гладком бетоне событий нет. Мультяшной " +
             "тачка выглядит именно тогда, когда она мнётся ВСЕГДА, пока " +
             "катится.")]
    [Range(0f, 0.3f)][SerializeField] private float _squashRolling = 0.06f;

    [Header("━━━ Подъём на одно колесо ━━━")]
    [Tooltip("Разрешить баллансу поднимать тачку на одно колесо.\n\n" +
             "Долю подъёма пишет CartBalance в BalanceWheelie01. Ноль — крен " +
             "вокруг собственной оси кузова, как было. Единица — вся видимая " +
             "тачка разворачивается вокруг ПЯТНА КОНТАКТА опорного колеса, и " +
             "второе колесо честно уходит в воздух по дуге.")]
    [SerializeField] private bool _wheelieEnabled = true;

    [Header("━━━ Покой ━━━")]
    [Tooltip("ЗАТИХАТЬ, КОГДА ТАЧКУ НИКТО НЕ КАТИТ.\n\n" +
             "Брошенная тачка — обычное динамическое тело, и солвер держит её " +
             "в равновесии крошечными поправками. Это нормальная работа физики. " +
             "Но подвеска читает эти поправки как настоящую езду: щупает землю " +
             "заново каждый кадр, ловит разные расстояния и дрожит. А вращение " +
             "колеса берётся из пройденного пути — и колесо начинает медленно " +
             "крутиться само по себе, стоя на месте.\n\n" +
             "Включено — брошенная тачка ставит колёса в покой и не щупает " +
             "землю вовсе. Дрожать нечему.")]
    [SerializeField] private bool _idleWhenFree = true;

    [Tooltip("МЁРТВАЯ ЗОНА ВРАЩЕНИЯ, метры за кадр.\n\n" +
             "Ниже этого пути колесо не крутится. Нужна даже когда за тачку " +
             "держатся: сглаживание позиции и микродрожь физики дают " +
             "ненулевой путь на стоянке, и без зоны колесо ползёт.")]
    [Range(0f, 0.01f)][SerializeField] private float _spinDeadzone = 0.0015f;

    [Header("━━━ Отладка ━━━")]
    [SerializeField] private bool _drawGizmos = true;

    // ───────────────────────── состояние ─────────────────────────

    private Vector3 _bodyBasePos;
    private Quaternion _bodyBaseRot;
    private float _roll, _rollVel, _pitch, _pitchVel, _y, _yVel;
    private Vector3 _lastRootPos;
    private bool _ready;

    // Кто катит. Нужен только чтобы отличить езду от покоя — брошенная тачка
    // не должна щупать землю и трястись.
    private PushCart _cart;

    // ── своя тряска ──
    private float _shakeSeed;
    private float _speedNow;

    // ── подскоки ──
    private float _hopTimer;

    // ── сплющивание ──
    private Vector3 _squashBaseScale = Vector3.one;
    private float _squashNow, _squashVel;

    // ── подъём на одно колесо ──
    private bool _wheelieOn;
    private Vector3 _wheeliePivot;
    private Quaternion _wheelieQ = Quaternion.identity;

    /// <summary>
    /// ДОЛЯ ПОДЪЁМА НА ОДНО КОЛЕСО, 0..1. Пишет CartBalance.
    ///
    /// Ноль — крен отыгрывается поворотом кузова вокруг его собственной оси:
    /// тачка выглядит наклонённой, но всё ещё стоящей на обоих колёсах.
    /// Единица — тот же крен разворачивает всю видимую тачку вокруг пятна
    /// контакта опорного колеса, и второе уходит вверх по дуге, как настоящее.
    ///
    /// Промежуточные значения смешивают одно с другим, поэтому переход в
    /// подъём и обратно не даёт скачка.
    /// </summary>
    [System.NonSerialized] public float BalanceWheelie01;

    /// <summary>Едем ли сейчас на одном колесе — для пыли, звука и следов.</summary>
    public bool OnOneWheel => _wheelieOn;

    /// <summary>Текущее сплющивание: минус вытянут, плюс приседает.</summary>
    public float Squash => _squashNow;

    /// <summary>
    /// НА КАКОЙ ВЫСОТЕ НАД ЗЕМЛЁЙ ДОЛЖЕН СТОЯТЬ КОРЕНЬ ТАЧКИ, метры.
    /// Меньше нуля — высоту спросить не у кого.
    ///
    /// ═══ ЗАЧЕМ ЭТО НАРУЖУ ═══
    ///
    /// Высоту тачки задаёт PushCart, а держит колёса ходовая. Пока каждый
    /// считал её сам — он по самой нижней точке геометрии, она по своим точкам
    /// опоры, — числа расходились. Расхождение в сантиметр уже видно: подвеска
    /// честно пытается его выбрать, упирается в предел хода и остаётся в упоре
    /// навсегда.
    ///
    /// Симптом при этом обманчивый до последнего. Брошенная тачка стоит
    /// правильно — её ставит физика, а не PushCart. Взял за ручки, и она
    /// просела; отпустил, и она вернулась. Выглядит как баг захвата, хотя
    /// захват ни при чём: просто ровно в эти секунды высоту считает второй
    /// участник спора.
    ///
    /// Теперь участник один. Ходовая знает, где стоят её точки опоры и какой
    /// радиус под каждой, — значит только она и может сказать, где земля.
    /// Спорить больше не с кем.
    /// </summary>
    public float RideHeight
    {
        get
        {
            if (!_ready || _wheels.Count == 0) return -1f;

            float best = -1f;

            foreach (Wheel w in _wheels)
            {
                if (w == null || w.mesh == null) continue;

                // Точка опоры должна встать на «земля плюс радиус».
                // Отсюда высота корня: радиус минус то, насколько точка
                // опоры ниже самого корня.
                float need = w.radius - w.restLocal.y;

                // Берём наибольшую: по самому «глубокому» колесу. По любому
                // другому оно бы утонуло.
                if (need > best) best = need;
            }

            return best;
        }
    }

    /// <summary>
    /// КРЕН ОТ БАЛАНСА, градусы. Пишет CartBalance.
    ///
    /// Он НЕ трогает трансформ сам: у кузова должен быть ровно один владелец,
    /// иначе кто победит — зависит от порядка вызова Update, который Unity не
    /// гарантирует. Здесь оба вклада складываются и применяются одной записью.
    /// </summary>
    [System.NonSerialized] public float BalanceRoll;

    /// <summary>Подъём на одно колесо от баланса, метры.</summary>
    [System.NonSerialized] public float BalanceLift;

    public IReadOnlyList<Wheel> Wheels => _wheels;
    public int WheelCount => _wheels.Count;

    /// <summary>Среднее сжатие 0..1 — насколько тряско прямо сейчас.</summary>
    public float Compression { get; private set; }

    /// <summary>Сила последнего удара, гаснет сама.</summary>
    public float Impact { get; private set; }

    public bool Grounded { get; private set; }

    private void Awake()
    {
        if (_body == null)
            Debug.LogWarning($"[Ходовая] «{name}»: не задан кузов — качать " +
                             "будет нечего, колёса при этом работают.", this);

        if (_body == transform)
        {
            Debug.LogError($"[Ходовая] «{name}»: в качестве кузова назначен САМ " +
                           "КОРЕНЬ ТАЧКИ.\n\nЕго ведёт PushCart через " +
                           "MovePosition, и подвеска дралась бы с ним за один " +
                           "трансформ: тачка зависает на месте, будто физика на " +
                           "неё не действует. Ссылка снята.", this);
            _body = null;
        }

        if (_body != null)
        {
            _bodyBasePos = _body.localPosition;
            _bodyBaseRot = _body.localRotation;

            // ПОДЪЁМ НА ОДНО КОЛЕСО СЧИТАЕТСЯ В КООРДИНАТАХ КОРНЯ, потому что
            // разворачивать надо и кузов, и меши колёс вокруг ОДНОЙ точки. Если
            // кузов лежит глубже одного уровня, его localPosition меряется уже
            // не от корня, и точка развода разъедется с колёсами.
            if (_body.parent != transform)
                Debug.LogWarning(
                    $"[Ходовая] «{name}»: кузов «{_body.name}» не прямой ребёнок " +
                    "корня тачки. Качка отработает нормально, а вот подъём на " +
                    "одно колесо считается в координатах корня и разъедется с " +
                    "колёсами. Подними кузов на один уровень.", this);
        }

        // Пусто — сплющиваем кузов. Работает, но тянет за собой коллайдеры
        // корыта; про это сказано в подсказке к полю.
        if (_squashTarget == null) _squashTarget = _body;
        if (_squashTarget != null) _squashBaseScale = _squashTarget.localScale;

        _cart = GetComponentInParent<PushCart>();

        _shakeSeed = Random.value * 400f;
        _hopTimer = Random.Range(_hopEveryMin, _hopEveryMax);

        AutoCollect();

        // РАДИУС НОЛЬ — это не «маленькое колесо», это выключенная подвеска.
        // Ход пружины считается от «касание + радиус», и с нулём цель всегда
        // лежит на самой земле: сжатие не набирается, а вместе с ним молча
        // пропадают удары, качка и сплющивание. Чинится замером, а не
        // требованием заполнить поле, — потому что поле уже забыли заполнить.
        foreach (Wheel w in _wheels)
        {
            if (w == null || w.mesh == null || w.radius > 0.02f) continue;

            if (!TryMeasureRadius(w.mesh, w.mount, out float measured)) continue;

            w.radius = Mathf.Max(0.01f, measured);

            Debug.LogWarning(
                $"[Ходовая] «{name}»: у колеса «{w.mesh.name}» радиус был нулевой. " +
                $"Измерен по мешу: {w.radius:0.000} м.\n\n" +
                "С нулём подвеска не работает вообще: цель пружины лежит на " +
                "самой земле, сжатие не набирается, и вместе с ним пропадают " +
                "удары, крен и сплющивание. Впиши радиус в компонент — замер " +
                "по габаритам приблизителен.", this);
        }

        FreezeRestPoints();

        _lastRootPos = transform.position;
        _ready = _wheels.Count > 0;

        if (!_ready)
            Debug.LogWarning($"[Ходовая] «{name}»: нет ни одного колеса.", this);
    }

    /// <summary>
    /// СБОРКА КОЛЁС ПО ИМЕНАМ, если список пуст.
    ///
    /// Это только удобство на первую настройку, а не механизм: имена объектов
    /// вещь ненадёжная, и полагаться на них в работающем проекте нельзя.
    /// Поэтому найденное сразу пишется в сериализованный список — дальше он
    /// живёт сам, и переименование объектов ничего не сломает.
    /// </summary>
    private void AutoCollect()
    {
        if (_wheels.Count > 0) return;

        Transform[] all = GetComponentsInChildren<Transform>(true);

        foreach (Transform t in all)
        {
            string n = t.name.ToLowerInvariant();

            if (!n.Contains("wheel") && !n.Contains("колес") && !n.Contains("колёс"))
                continue;

            // Точки крепления по имени тоже отсеиваем: Mount не колесо, и
            // ставить на него меш нечего.
            if (n.Contains("mount") || n.Contains("point")) continue;

            _wheels.Add(new Wheel
            {
                mesh = t,
                mount = t.parent,
                radius = 0.35f,
                spinAxis = Vector3.right,
                weight = 1f
            });
        }

        if (_wheels.Count == 0) return;

        Debug.LogWarning(
            $"[Ходовая] «{name}»: колёса собраны по именам, найдено {_wheels.Count}.\n\n" +
            "ПРОВЕРЬ РАДИУСЫ — поставлено 0.35, а настоящий надо замерить по " +
            "мешу: заниженный радиус утапливает шину в землю ровно на разницу.\n\n" +
            "И проверь сам список: подбор по имени это удобство на первую " +
            "настройку, а не механизм.", this);
    }

    /// <summary>
    /// ЗАМОРОЗКА ТОЧЕК ПОКОЯ — то, ради чего всё переписано.
    ///
    /// Положение каждой опоры запоминается в системе координат КОРНЯ тачки
    /// один раз. Дальше живой трансформ Mount не читается вообще.
    ///
    /// Благодаря этому иерархия перестаёт иметь значение: Mount может лежать
    /// внутри качающегося кузова, петля «кузов качнулся → точка уехала →
    /// колесо решило, что тачка поехала» всё равно невозможна.
    ///
    /// ═══ ПОЧЕМУ ЦЕНТР МЕША, А НЕ ЕГО ПИВОТ ═══
    ///
    /// Когда Mount не задан, точка берётся по ЦЕНТРУ ВИДИМОЙ ГЕОМЕТРИИ, а не
    /// по transform.position меша. Причина сугубо практическая: экспорт из
    /// Blender сплошь и рядом кладёт пивоты всех объектов в начало координат.
    /// Тогда оба колеса «стоят» в одной точке, разница левого и правого равна
    /// нулю, и молча пропадает всё, что из неё считается — крен от
    /// неровностей и подъём на одно колесо.
    ///
    /// Симптом при этом обманчивый: подвеска работает, колёса крутятся, тачка
    /// проседает на кочках — не работает только то, что требует РАЗНИЦЫ между
    /// колёсами. Искать это можно долго.
    /// </summary>
    private void FreezeRestPoints()
    {
        foreach (Wheel w in _wheels)
        {
            if (w == null) continue;

            Vector3 src;

            if (w.mount != null)
            {
                src = w.mount.position;
            }
            else if (w.mesh != null)
            {
                src = w.mesh.position;

                Renderer r = w.mesh.GetComponentInChildren<Renderer>();
                if (r != null) src = r.bounds.center;
            }
            else
            {
                src = transform.position;
            }

            w.restLocal = transform.InverseTransformPoint(src)
                        + Vector3.up * w.rideOffset;

            w.normal = Vector3.up;

            w.restRotLocal = w.mesh != null
                ? Quaternion.Inverse(transform.rotation) * w.mesh.rotation
                : Quaternion.identity;

            if (w.spinAxis.sqrMagnitude < 0.0001f) w.spinAxis = Vector3.right;
        }

        WarnIfPointsCollapsed();
    }

    /// <summary>
    /// ТОЧКИ ОПОРЫ СХЛОПНУЛИСЬ В ОДНУ. Отдельная проверка, потому что этот
    /// случай не ломает ничего заметного: тачка ездит, колёса крутятся,
    /// подвеска отрабатывает. Тихо пропадает только разница между колёсами.
    /// </summary>
    private void WarnIfPointsCollapsed()
    {
        if (_wheels.Count < 2) return;

        float min = float.MaxValue, max = float.MinValue;

        foreach (Wheel w in _wheels)
        {
            if (w == null) continue;
            min = Mathf.Min(min, w.restLocal.x);
            max = Mathf.Max(max, w.restLocal.x);
        }

        if (max - min >= 0.05f) return;

        Debug.LogError(
            $"[Ходовая] «{name}»: все точки опоры стоят в одной точке по " +
            $"поперечной оси (размах {max - min:0.000} м).\n\n" +
            "Крен кузова считается из РАЗНИЦЫ левых и правых сжатий — а её " +
            "нет. Поэтому не будет ни качки на неровностях, ни подъёма на " +
            "одно колесо, хотя подвеска и вращение колёс работают нормально.\n\n" +
            "Почти всегда это пивоты из Blender, лежащие в начале координат. " +
            "ЛЕЧЕНИЕ: правой кнопкой по компоненту → «★ ИЗМЕРИТЬ КОЛЁСА ПО " +
            "МЕШАМ», либо расставь Mount-пустышки на местах колёс вручную.", this);
    }

    /// <summary>
    /// САМАЯ НИЖНЯЯ ТОЧКА ВСЕЙ МОДЕЛИ, мировая высота.
    ///
    /// Мерится по видимой геометрии, потому что на земле стоит именно она.
    /// Тем же способом PushCart вычисляет посадочную высоту тачки — и это не
    /// совпадение: обе величины обязаны исходить из одного и того же «низа»,
    /// иначе ходовая и следование за игроком будут спорить о том, где земля.
    /// </summary>
    private bool TryModelBottom(out float worldY)
    {
        float min = float.MaxValue;

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            if (r != null) min = Mathf.Min(min, r.bounds.min.y);

        worldY = min;
        return min != float.MaxValue;
    }

    /// <summary>
    /// РАДИУС = ВЫСОТА ТОЧКИ ОПОРЫ НАД ЗЕМЛЁЙ В АВТОРСКОЙ ПОЗЕ.
    ///
    /// ═══ ПОЧЕМУ НЕ ПО КОЛЕСУ ═══
    ///
    /// Казалось бы, радиус колеса надо мерить по колесу. Но подвеска этим
    /// числом решает совсем другую задачу: она ставит ТОЧКУ ОПОРЫ на «высота
    /// касания плюс радиус». То есть радиус здесь — это не размер шины, а
    /// РАССТОЯНИЕ ОТ ТОЧКИ ОПОРЫ ДО ЗЕМЛИ.
    ///
    /// Совпадают они только если точка опоры стоит ровно в центре колеса. А
    /// ставят её обычно на раму — на ось, на кронштейн, — и рама проходит ниже
    /// ступицы. Разница получается в несколько сантиметров, и её хватает.
    ///
    /// Наружу расхождение выходит так: подвеска считает, что точка опоры
    /// должна быть выше, чем она есть в модели, и всё время тянет тачку вверх.
    /// Дашь пружине ход — колёса задерутся над рамой. Зажмёшь ход в ноль —
    /// колёса останутся утопленными в земле. Ни то ни другое не лечится
    /// настройкой пружины, потому что пружина ни при чём: ей задали неверную
    /// цель.
    ///
    /// ═══ ПОЧЕМУ ЭТО ВЕРНО ПО ПОСТРОЕНИЮ ═══
    ///
    /// В редакторе тачка стоит на земле правильно — так её собрал автор. Взяв
    /// радиусом высоту точки опоры над низом модели, мы говорим подвеске:
    /// «держи всё ровно так, как собрано». Не подгонка под глаз, а перенос
    /// авторской позы в число.
    /// </summary>
    private bool TryMeasureRadius(Transform mesh, Transform mount, out float radius)
    {
        radius = 0f;

        if (!TryModelBottom(out float bottomY)) return false;

        float restY;

        if (mount != null)
        {
            restY = mount.position.y;
        }
        else
        {
            if (mesh == null) return false;

            Renderer r = mesh.GetComponentInChildren<Renderer>();
            if (r == null) return false;

            restY = r.bounds.center.y;
        }

        radius = restY - bottomY;
        return radius > 0.005f;
    }

    /// <summary>Радиус по самому колесу — только для сверки в диагностике.
    /// Вертикальный полуразмер не зависит от разворота колеса вокруг
    /// вертикали, в отличие от половины наибольшего габарита.</summary>
    private static bool TryMeshRadius(Transform mesh, out float radius)
    {
        radius = 0f;
        if (mesh == null) return false;

        Renderer r = mesh.GetComponentInChildren<Renderer>();
        if (r == null) return false;

        radius = r.bounds.extents.y;
        return radius > 0.005f;
    }

    /// <summary>
    /// ИЗМЕРИТЬ РАДИУСЫ И МЕСТА КОЛЁС ПО ВИДИМОЙ ГЕОМЕТРИИ.
    ///
    /// Радиус — половина наибольшего габарита меша: у колеса два измерения
    /// равны диаметру, третье равно ширине шины, поэтому наибольшее из них
    /// всегда диаметр, как бы колесо ни было повёрнуто.
    ///
    /// Mount-пустышки при этом НЕ создаются: точка покоя и так берётся по
    /// центру геометрии. Команда нужна ради радиусов и ради того, чтобы
    /// увидеть в консоли, где именно ходовая считает колёса — если числа
    /// разойдутся с тем, что видно в сцене, дело в мешах, а не в настройках.
    /// </summary>
    [ContextMenu("★ ИЗМЕРИТЬ КОЛЁСА ПО МЕШАМ")]
    public void MeasureWheels()
    {
        if (_wheels.Count == 0) AutoCollect();

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"═══ ЗАМЕР КОЛЁС: {name} ═══\n");

        int fixedCount = 0;

        foreach (Wheel w in _wheels)
        {
            if (w == null || w.mesh == null) continue;

            if (!TryMeasureRadius(w.mesh, w.mount, out float measured))
            {
                sb.AppendLine($"  {w.mesh.name}: замерить нечем — нет ни " +
                              "точки крепления, ни рендерера");
                continue;
            }

            float was = w.radius;
            w.radius = Mathf.Max(0.01f, measured);

            Vector3 local = RestLocalPreview(w);

            sb.AppendLine($"  {w.mesh.name}: радиус {was:0.000} → {w.radius:0.000}   " +
                          $"точка опоры x={local.x:+0.000;-0.000} y={local.y:0.000}");

            if (TryMeshRadius(w.mesh, out float byMesh))
                sb.AppendLine($"      (само колесо: {byMesh:0.000}; разница — это " +
                              "насколько точка крепления ниже ступицы, и она " +
                              "нормальна)");

            fixedCount++;
        }

        sb.AppendLine($"\nИзмерено колёс: {fixedCount}");
        sb.AppendLine("Точки опоры по X должны РАЗЛИЧАТЬСЯ — из их разницы " +
                      "считается крен. Одинаковые значения означают пивоты из " +
                      "Blender в начале координат: тогда ставь Mount-пустышки " +
                      "руками, замер по мешу здесь не спасёт.");

        Debug.Log(sb.ToString(), this);

#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    private void LateUpdate()
    {
        if (!_ready) return;

        float dt = Mathf.Min(Time.deltaTime, 0.05f);

        Impact = Mathf.MoveTowards(Impact, 0f, dt * 3f);

        // Путь корня, а не колёс: колёса ходят вверх-вниз подвеской, и
        // вертикаль подмешалась бы в обороты.
        Vector3 delta = transform.position - _lastRootPos;
        _lastRootPos = transform.position;

        // Скорость по горизонтали: вертикаль — это работа подвески, и в
        // «быстро ли едем» ей делать нечего.
        Vector3 flat = delta; flat.y = 0f;
        _speedNow = Mathf.Lerp(_speedNow, flat.magnitude / dt, 1f - Mathf.Exp(-8f * dt));

        TickHops(dt);

        // ПОРЯДОК ВАЖЕН.
        //
        //   колёса  — считают подвеску и КОПЯТ позу, не записывая её
        //   кузов   — выбирает опорное колесо, потому что пятна контакта уже
        //             посчитаны, и задаёт разворот вокруг него
        //   запись  — один проход, где поза колёс наконец уходит в трансформы
        //
        // Наоборот не выйдет: чтобы поднять тачку на колесо, надо знать, где
        // это колесо касается земли, а это известно только после расчёта.
        TickWheels(dt, delta);
        TickBody(dt);
        ApplyWheelPoses();
        TickSquash(dt);
    }

    /// <summary>
    /// ПОДСКОКИ.
    ///
    /// Импульс уходит в СКОРОСТЬ СЖАТИЯ пружины, а не в позицию кузова. Из-за
    /// этого подскок отрабатывается тем же механизмом, что и настоящая кочка:
    /// пружина проскакивает, качается и возвращается, попутно давая крен,
    /// тангаж, удар и сплющивание. Отдельная анимация подскока всего этого не
    /// умела бы и выглядела бы приклеенной поверх.
    ///
    /// Колёса получают разные доли — иначе тачка просто прыгает вверх ровно,
    /// а это читается как скрипт, а не как камень под колесом.
    /// </summary>
    private void TickHops(float dt)
    {
        if (!_hopsEnabled || _wheels.Count == 0) return;

        // Брошенная тачка не подпрыгивает сама: подскок читался бы как
        // полтергейст, а не как камень под колесом.
        if (_idleWhenFree && _cart != null && !_cart.IsGripped) return;

        if (_speedNow < _hopMinSpeed) return;

        _hopTimer -= dt;
        if (_hopTimer > 0f) return;

        _hopTimer = Random.Range(_hopEveryMin, Mathf.Max(_hopEveryMin, _hopEveryMax));

        int lucky = Random.Range(0, _wheels.Count);

        for (int i = 0; i < _wheels.Count; i++)
        {
            Wheel w = _wheels[i];
            if (w == null || w.mesh == null) continue;

            float share = i == lucky ? 1f : 1f - _hopUneven;

            w.velocity += _hopForce * share
                        * Mathf.Clamp01(_speedNow / Mathf.Max(0.2f, _shakeFullAt))
                        * Random.Range(0.75f, 1.25f);
        }
    }

    /// <summary>
    /// ПРУЖИНА НА КАЖДОМ КОЛЕСЕ.
    ///
    /// Прямое следование за высотой земли дало бы колесо-липучку: на
    /// ступеньке оно телепортируется вслед за раскастом, и подвески как будто
    /// нет. Недодемпфированная пружина проскакивает цель и качается — это и
    /// есть ощущение подвески.
    /// </summary>
    private void TickWheels(float dt, Vector3 rootDelta)
    {
        bool anyGround = false;
        float sum = 0f, wsum = 0f;

        Vector3 fwd = transform.forward;
        float along = Vector3.Dot(rootDelta, fwd);

        // МЁРТВАЯ ЗОНА. Сглаживание позиции тачки и микропоправки солвера дают
        // ненулевой путь даже на стоянке. Без зоны колесо от них медленно
        // ползёт — и это тем заметнее, чем спокойнее сцена.
        if (Mathf.Abs(along) < _spinDeadzone) along = 0f;

        // Брошенную тачку землёй не щупаем. Она обычное динамическое тело, и
        // солвер удерживает её равновесие крошечными поправками; подвеска
        // читает их как езду по кочкам и дрожит на ровном месте.
        bool probe = !_idleWhenFree || _cart == null || _cart.IsGripped;

        foreach (Wheel w in _wheels)
        {
            if (w == null || w.mesh == null) continue;

            Vector3 rest = transform.TransformPoint(w.restLocal);

            // Раскаст из точки покоя ВВЕРХ и вниз на полный ход плюс запас:
            // иначе на резкой яме колесо потеряет землю и повиснет вверху,
            // что выглядит хуже, чем просто дно ямы.
            Vector3 origin = rest + Vector3.up * _travel;
            float len = _travel * 2f + w.radius + 0.15f;

            float target;

            if (probe && Physics.Raycast(origin, Vector3.down, out RaycastHit hit, len,
                                _groundMask, QueryTriggerInteraction.Ignore))
            {
                w.grounded = true;
                w.contact = hit.point;
                w.normal = hit.normal;
                anyGround = true;

                // Колесо касается земли ОБОДОМ, а не центром.
                target = Mathf.Clamp(hit.point.y + w.radius - rest.y, -_travel, _travel);
            }
            else if (!probe)
            {
                // ПОКОЙ. Колесо стоит ровно в точке покоя — там, где его
                // поставили в редакторе. Пружина при этом жива и плавно
                // доводит его до места, поэтому переход в покой и обратно не
                // даёт скачка.
                w.grounded = true;
                w.contact = rest - Vector3.up * w.radius;
                w.normal = Vector3.up;
                target = 0f;
            }
            else
            {
                w.grounded = false;
                w.contact = rest - Vector3.up * (_travel + w.radius);
                w.normal = Vector3.up;
                target = -_travel;
            }

            float prevVel = w.velocity;

            float accel = -_stiffness * (w.offset - target)
                          - 2f * _damping * Mathf.Sqrt(_stiffness) * w.velocity;

            w.velocity += accel * dt;
            w.offset = Mathf.Clamp(w.offset + w.velocity * dt, -_travel, _travel);

            // Удар — это РЫВОК скорости сжатия, а не её величина.
            float jerk = Mathf.Abs(w.velocity - prevVel) / Mathf.Max(0.001f, dt);
            w.impact = Mathf.Max(0f, jerk / 100f - _impactAt);

            if (w.impact > Impact) Impact = Mathf.Min(1f, w.impact);

            // ── ПОЗА МЕША ──
            //
            // Не запись, а именно поза: между этим местом и трансформом стоит
            // подъём на одно колесо, который развернёт всю картинку вокруг
            // пятна контакта опорного колеса. Пятно станет известно только
            // после того, как посчитаны ВСЕ колёса.
            w.poseP = rest + Vector3.up * w.offset;

            w.spinAngle += along / Mathf.Max(0.01f, w.radius) * Mathf.Rad2Deg;

            // Авторский поворот идёт ПЕРЕД качением: ось вращения задана в
            // системе координат самого колеса, и умножать её на что-то после
            // означало бы крутить колесо вокруг чужой оси.
            w.poseR = transform.rotation
                    * w.restRotLocal
                    * Quaternion.AngleAxis(w.spinAngle, w.spinAxis.normalized);

            sum += w.offset * w.weight;
            wsum += w.weight;
        }

        Grounded = anyGround;
        Compression = wsum > 0.001f
            ? Mathf.Clamp01(Mathf.Abs(sum / wsum) / _travel) : 0f;
    }

    /// <summary>
    /// КУЗОВ. Единственное место, где он вообще пишется.
    ///
    /// Наклон берётся из разницы между опорами и делится на РАЗМАХ между ними:
    /// иначе широкая тачка кренилась бы на тот же угол при вдвое меньшей
    /// неровности, и настройки пришлось бы подбирать под каждую модель.
    ///
    /// Сюда же приходит крен от баланса — складывается, а не спорит.
    /// </summary>
    private void TickBody(float dt)
    {
        if (_body == null) return;

        float lSum = 0f, lSpan = 0f, fSum = 0f, fSpan = 0f, ySum = 0f, wSum = 0f;

        foreach (Wheel w in _wheels)
        {
            if (w == null || w.mesh == null) continue;

            lSum += w.offset * w.restLocal.x * w.weight;
            lSpan += Mathf.Abs(w.restLocal.x) * w.weight;

            fSum += w.offset * w.restLocal.z * w.weight;
            fSpan += Mathf.Abs(w.restLocal.z) * w.weight;

            ySum += w.offset * w.weight;
            wSum += w.weight;
        }

        float wantRoll = lSpan > 0.01f
            ? Mathf.Clamp(-lSum / lSpan * _rollPerMeter, -_maxAngle, _maxAngle) : 0f;

        float wantPitch = fSpan > 0.01f
            ? Mathf.Clamp(fSum / fSpan * _pitchPerMeter, -_maxAngle, _maxAngle) : 0f;

        float wantY = wSum > 0.001f ? ySum / wSum * _heave : 0f;

        _roll = Mathf.SmoothDamp(_roll, wantRoll, ref _rollVel, _smooth);
        _pitch = Mathf.SmoothDamp(_pitch, wantPitch, ref _pitchVel, _smooth);
        _y = Mathf.SmoothDamp(_y, wantY, ref _yVel, _smooth);

        // ── СВОЯ ТРЯСКА ──
        //
        // Кладётся ПОСЛЕ сглаживания, а не до. Сглаживание с постоянной 0.06
        // работает как фильтр низких частот: пропущенный через него дребезг
        // потерял бы ровно то, ради чего он и нужен — мелкую высокую рябь.
        float t = Time.time * _shakeFreq;
        float live = Mathf.Lerp(_shakeAtRest, 1f,
                                Mathf.Clamp01(_speedNow / Mathf.Max(0.2f, _shakeFullAt)));

        // Тряска — про вещь, которую ВЕЗУТ. Брошенная тачка просто лежит.
        if (_idleWhenFree && _cart != null && !_cart.IsGripped) live = 0f;

        float shakeRoll = Noise(t, 0f) * _shakeRoll * live;
        float shakePitch = Noise(t * 0.83f, 11f) * _shakePitch * live;
        float shakeY = Noise(t * 1.31f, 23f) * _shakeHeave * live;

        // ── ЧЕМ ОТЫГРЫВАЕТСЯ КРЕН ОТ БАЛАНСА ──
        //
        // Одна и та же величина в градусах, но повёрнутая вокруг РАЗНЫХ точек,
        // читается как две разные вещи:
        //
        //   вокруг своей оси      — тачку наклонили, она стоит на двух колёсах
        //   вокруг пятна контакта — тачка встала на одно колесо
        //
        // Поэтому крен делится между двумя пивотами, а не удваивается.
        float wheelie01 = _wheelieEnabled ? Mathf.Clamp01(BalanceWheelie01) : 0f;
        float pivotRoll = BalanceRoll * wheelie01;
        float ownRoll = BalanceRoll - pivotRoll;

        Quaternion rot = _bodyBaseRot
                       * Quaternion.Euler(_pitch + shakePitch, 0f, _roll + shakeRoll + ownRoll);

        Vector3 pos = _bodyBasePos + Vector3.up * (_y + shakeY + BalanceLift);

        _wheelieOn = false;

        if (Mathf.Abs(pivotRoll) > 0.05f && TryWheeliePivot(BalanceRoll, out Vector3 pivotWorld))
        {
            _wheelieOn = true;
            _wheeliePivot = pivotWorld;
            _wheelieQ = Quaternion.AngleAxis(pivotRoll, transform.forward);

            // Тот же разворот, но в координатах корня: кузов пишется локально,
            // и переводить его позу в мир и обратно ради этого незачем.
            Vector3 pivotLocal = transform.InverseTransformPoint(pivotWorld);
            Quaternion qLocal = Quaternion.AngleAxis(pivotRoll, Vector3.forward);

            pos = pivotLocal + qLocal * (pos - pivotLocal);
            rot = qLocal * rot;
        }

        // ОДНА запись на кадр, все вклады сразу.
        _body.localRotation = rot;
        _body.localPosition = pos;
    }

    /// <summary>
    /// Перлин, сдвинутый в минус-плюс. Синус здесь не годится: у него слышен
    /// период, и через минуту езды тряска читается как метроном.
    /// </summary>
    private float Noise(float t, float lane)
        => (Mathf.PerlinNoise(_shakeSeed + t, lane) - 0.5f) * 2f;

    /// <summary>
    /// ОПОРНОЕ КОЛЕСО И ЕГО ПЯТНО КОНТАКТА.
    ///
    /// Знак крена задаёт сторону: кренится вправо — опора справа, левое колесо
    /// уходит в воздух. Пивот берётся на ПЯТНЕ КОНТАКТА, а не в центре колеса:
    /// настоящая тачка встаёт не на ось, а на землю под шиной, и разница в
    /// один радиус на глаз очень заметна — при пивоте в центре опорное колесо
    /// начинает подтапливать в грунт.
    ///
    /// Если опорное колесо в воздухе (тачка в прыжке), пятно считается там, где
    /// оно было бы: подъём в полёте выглядит странно, но не ломается.
    /// </summary>
    private bool TryWheeliePivot(float rollDeg, out Vector3 pivot)
    {
        pivot = Vector3.zero;

        // Крен по Z отрицательный — кузов валится вправо, значит опора справа.
        float side = rollDeg < 0f ? 1f : -1f;

        Wheel best = null;
        float bestScore = 0.01f;

        foreach (Wheel w in _wheels)
        {
            if (w == null || w.mesh == null) continue;

            float score = side * w.restLocal.x;
            if (score > bestScore) { bestScore = score; best = w; }
        }

        // Ни одного колеса с нужной стороны — тачка одноколёсная или все точки
        // стоят по центру. Разводить нечего, крутим вокруг своей оси.
        if (best == null) return false;

        pivot = best.grounded
            ? best.contact
            : transform.TransformPoint(best.restLocal)
              + Vector3.up * (best.offset - best.radius);

        return true;
    }

    /// <summary>
    /// ЗАПИСЬ КОЛЁС. Единственное место, где меши колёс вообще пишутся.
    ///
    /// Здесь же на них ложится подъём на одно колесо: та же самая пара
    /// «точка и поворот», что применилась к кузову. Без этого тачка кренилась
    /// бы кузовом, а колёса продолжали бы стоять на земле — самое заметное
    /// расхождение из возможных.
    /// </summary>
    private void ApplyWheelPoses()
    {
        foreach (Wheel w in _wheels)
        {
            if (w == null || w.mesh == null) continue;

            Vector3 p = w.poseP;
            Quaternion r = w.poseR;

            if (_wheelieOn)
            {
                p = _wheeliePivot + _wheelieQ * (p - _wheeliePivot);
                r = _wheelieQ * r;
            }

            w.mesh.SetPositionAndRotation(p, r);
        }
    }

    /// <summary>
    /// МУЛЬТЯШНОЕ СПЛЮЩИВАНИЕ.
    ///
    /// Берётся из СКОРОСТИ кузова, а не из его положения. Положение сказало бы
    /// только «низко», и тачка оставалась бы приплюснутой всю яму. Скорость
    /// говорит «едет вниз» — приседает в момент удара и вытягивается на
    /// отскоке, ровно как в рисованной анимации.
    ///
    /// Ширина растёт вдвое медленнее, чем падает высота: так объём выглядит
    /// сохранённым, и меш не раздувается пузырём.
    /// </summary>
    private void TickSquash(float dt)
    {
        if (_squashTarget == null) return;

        float v = Mathf.Clamp(-_yVel / Mathf.Max(0.05f, _squashSpeedRef), -1f, 1f);

        float want = v * _squashFromMotion + Impact * _squashFromImpact;

        // Дрожь от самой езды. Своя частота, заметно быстрее тряски рамы:
        // совпади они — оба движения слились бы в одно, и сплющивание
        // перестало бы читаться отдельно.
        float rolling = Mathf.Clamp01(_speedNow / Mathf.Max(0.2f, _shakeFullAt));
        want += Noise(Time.time * _shakeFreq * 1.7f, 37f) * _squashRolling * rolling;

        want = Mathf.Clamp(want, -_squashLimit, _squashLimit);

        _squashNow = Mathf.SmoothDamp(_squashNow, want, ref _squashVel, _squashSmooth);

        float wide = 1f + _squashNow * _squashWidth;
        float tall = 1f - _squashNow;

        _squashTarget.localScale = new Vector3(
            _squashBaseScale.x * wide,
            _squashBaseScale.y * tall,
            _squashBaseScale.z * wide);
    }

    // ───────────────────── для пыли, звука, следов ─────────────────────

    public bool TryGetContact(int index, out Vector3 point, out Vector3 normal)
    {
        point = Vector3.zero;
        normal = Vector3.up;

        if (index < 0 || index >= _wheels.Count) return false;

        Wheel w = _wheels[index];
        if (w == null || w.mesh == null || !w.grounded) return false;

        point = w.contact;
        normal = w.normal;
        return true;
    }

    /// <summary>Куда смотрит колесо по горизонтали — для следов и заноса.</summary>
    public Vector3 Forward
    {
        get
        {
            Vector3 f = transform.forward;
            f.y = 0f;
            return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }
    }

    // ───────────────────────── диагностика ─────────────────────────

    [ContextMenu("★ ПОЧЕМУ НЕ РАБОТАЕТ")]
    public void Diagnose()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        int problems = 0;

        sb.AppendLine($"═══ ХОДОВАЯ: {name} ═══\n");

        PushCart cart = GetComponent<PushCart>();
        sb.AppendLine($"PushCart: {(cart != null ? "есть" : "НЕТ")}");
        sb.AppendLine($"Кузов: {(_body != null ? _body.name : "НЕ ЗАДАН")}");
        sb.AppendLine($"Колёс: {_wheels.Count}");

        if (_body == transform)
        {
            sb.AppendLine("\n❌❌ КУЗОВ = КОРЕНЬ ТАЧКИ. PushCart ведёт корень через " +
                          "MovePosition, подвеска дерётся с ним за один трансформ.");
            problems++;
        }

        if (_body != null && _body.GetComponent<Rigidbody>() != null)
        {
            sb.AppendLine("\n❌ На кузове висит Rigidbody. Наклонять физическое " +
                          "тело нельзя.");
            problems++;
        }

        // Проверки на старый CartWheel здесь нет намеренно: компонент удалён
        // из проекта, и ссылка на несуществующий тип не дала бы собраться.
        // Если он где-то остался — просто удали его скрипт.

        // Проверку на CartSuspension здесь не делаем намеренно: файл этого
        // компонента удалён, и ссылка на удалённый тип не дала бы проекту
        // собраться. Если он у тебя остался — просто удали его файл.

        CartBalance bal = GetComponent<CartBalance>();

        if (bal != null)
            sb.AppendLine($"\nCartBalance: есть. Крен приходит сюда через " +
                          $"BalanceRoll = {BalanceRoll:0.0}°, " +
                          $"подъём на колесо {BalanceWheelie01:0.00}");

        // Сверка двух мнений о том, где земля. Пока их было два, расхождение
        // проявлялось только при захвате — и выглядело как баг захвата.
        if (Application.isPlaying)
        {
            PushCart pc = GetComponentInParent<PushCart>();

            if (pc != null && RideHeight >= 0f)
            {
                sb.AppendLine($"\n── ПОСАДОЧНАЯ ВЫСОТА ──");
                sb.AppendLine($"  по точкам опоры: {RideHeight:0.000} м " +
                              "— это число и уходит в PushCart");
                sb.AppendLine("  если тачка просаживается ТОЛЬКО пока за неё " +
                              "держатся, значит PushCart всё ещё считает " +
                              "высоту сам: обнови и его тоже.");
            }
        }

        sb.AppendLine($"\n── СВОЯ ЖИЗНЬ ──");
        sb.AppendLine($"  скорость {_speedNow:0.00} м/с   сплющивание {_squashNow:+0.000;-0.000}");
        sb.AppendLine($"  на одном колесе: {(_wheelieOn ? "ДА" : "нет")}");

        if (_squashTarget == _body && _body != null)
        {
            Collider[] sc = _body.GetComponentsInChildren<Collider>(true);

            if (sc.Length > 0)
                sb.AppendLine($"\n⚠ Сплющивается сам кузов, а внутри него " +
                              $"{sc.Length} коллайдеров. Масштаб потянет стенки " +
                              "корыта, и груз будет получать пинки на каждом " +
                              "сжатии. Заведи пустышку с одними мешами и " +
                              "назначь её в Squash Target.");
        }

        if (_body != null && _body.parent != transform)
        {
            sb.AppendLine("\n❌ Кузов не прямой ребёнок корня. Подъём на одно " +
                          "колесо считается в координатах корня и разъедется " +
                          "с колёсами.");
            problems++;
        }

        sb.AppendLine($"\n── КОЛЁСА ──");

        if (!Application.isPlaying)
            sb.AppendLine("  (не в Play: сжатие и «на земле» здесь всегда " +
                          "нулевые — их считает LateUpdate)");

        float minX = float.MaxValue, maxX = float.MinValue;

        for (int i = 0; i < _wheels.Count; i++)
        {
            Wheel w = _wheels[i];

            if (w == null || w.mesh == null)
            {
                sb.AppendLine($"  {i}: ПУСТО");
                problems++;
                continue;
            }

            // Вне Play точки покоя ещё не заморожены — считаем их так же, как
            // это сделает старт, иначе диагностика показывала бы нули и
            // «схлопнутые точки» выглядели бы нормой.
            Vector3 rl = Application.isPlaying ? w.restLocal : RestLocalPreview(w);

            minX = Mathf.Min(minX, rl.x);
            maxX = Mathf.Max(maxX, rl.x);

            sb.AppendLine($"  {i}: {w.mesh.name}   радиус {w.radius:0.000}   " +
                          $"опора x={rl.x:+0.000;-0.000}   " +
                          $"{(w.grounded ? "на земле" : "в воздухе")}   " +
                          $"сжатие {w.offset:+0.000;-0.000}");

            // Расхождение с мешем. Не ошибка сама по себе — радиус можно
            // задать руками, — но именно завышенный радиус даёт «ось ниже
            // ступицы», и увидеть это иначе почти невозможно: всё остальное
            // выглядит нормально.
            if (!Application.isPlaying
                && TryMeasureRadius(w.mesh, w.mount, out float should)
                && Mathf.Abs(should - w.radius) > 0.01f)
            {
                sb.AppendLine($"     ⚠ радиус {w.radius:0.000}, а по авторской " +
                              $"позе должен быть {should:0.000}.");

                sb.AppendLine(w.radius > should
                    ? "        Завышен: подвеска тянет тачку ВВЕРХ, колёса " +
                      "задираются над рамой."
                    : "        Занижен: подвеска тянет ВНИЗ, колёса тонут в земле.");

                sb.AppendLine("        Пружиной это не лечится — ей задана " +
                              "неверная цель. Зажатый Travel только прячет " +
                              "симптом.");
                sb.AppendLine("        ЛЕЧЕНИЕ: «★ ИЗМЕРИТЬ КОЛЁСА ПО МЕШАМ».");
            }

            if (w.radius < 0.02f)
            {
                sb.AppendLine($"     ❌❌ РАДИУС НОЛЬ. Подвеска выключена: цель " +
                              "пружины лежит на самой земле, сжатие не " +
                              "набирается. Без него нет ни ударов, ни качки, " +
                              "ни сплющивания.");
                sb.AppendLine("     ЛЕЧЕНИЕ: «★ ИЗМЕРИТЬ КОЛЁСА ПО МЕШАМ», " +
                              "потом проверь числа глазами.");
                problems++;
            }
        }

        if (_wheels.Count >= 2 && maxX - minX < 0.05f)
        {
            sb.AppendLine($"\n❌❌ ТОЧКИ ОПОРЫ СХЛОПНУЛИСЬ: размах по поперечной " +
                          $"оси {maxX - minX:0.000} м.");
            sb.AppendLine("   Крен считается из РАЗНИЦЫ левых и правых сжатий, " +
                          "а её нет. Пропадает качка на неровностях и подъём на " +
                          "одно колесо — при этом подвеска и вращение колёс " +
                          "работают, поэтому на глаз всё «почти нормально».");
            sb.AppendLine("   Почти всегда это пивоты из Blender в начале " +
                          "координат.");
            sb.AppendLine("   ЛЕЧЕНИЕ: поставь две пустышки MountL и MountR на " +
                          "местах колёс и назначь их в поля Mount.");
            problems++;
        }

        // ── ПРУЖИНЫ УПЁРТЫ В ПРЕДЕЛ ──
        //
        // Самая обманчивая из поломок: все поля заполнены правильно, колёса
        // найдены, радиусы верные, и ходовая честно докладывает «на земле».
        // Только сжатие стоит колом на пределе и не меняется, а из его
        // ИЗМЕНЕНИЯ считается всё остальное — удары, крен, сплющивание.
        // Поэтому наружу это выходит как «половина нововведений не работает».
        if (Application.isPlaying)
        {
            int pinned = 0;

            foreach (Wheel w in _wheels)
                if (w != null && w.mesh != null && Mathf.Abs(w.offset) >= _travel * 0.97f)
                    pinned++;

            if (pinned > 0 && pinned == _wheels.Count)
            {
                bool down = _wheels[0].offset > 0f;

                sb.AppendLine($"\n❌❌ ВСЕ ПРУЖИНЫ УПЁРТЫ В ПРЕДЕЛ ХОДА " +
                              $"({_travel:0.000} м).");

                sb.AppendLine(down
                    ? "   Сжатие на плюсовом упоре — тачка УТОПЛЕНА в землю, и " +
                      "подвеска пытается её вытолкнуть."
                    : "   Сжатие на минусовом упоре — тачка ВИСИТ над землёй, и " +
                      "колёса до неё не достают.");

                sb.AppendLine("   Пока сжатие стоит на упоре, оно не меняется — " +
                              "а из его изменения берутся удары, качка и " +
                              "сплющивание. Все они честно равны нулю.");
                sb.AppendLine("   ЛЕЧЕНИЕ: высоту тачки задаёт НЕ ходовая, а " +
                              "PushCart. Проверь, что тачка проваливается " +
                              "только пока держат ручки — если да, дело в замере " +
                              "низа тачки в PushCart, а не здесь.");
                problems++;
            }
        }

        sb.AppendLine($"\n═══ ПРОБЛЕМ: {problems} ═══");

        if (problems == 0)
            sb.AppendLine("Всё чисто. Если колесо тонет в земле — проверь его " +
                          "радиус: подвеска ставит центр на «касание + радиус», " +
                          "и заниженный радиус утапливает шину ровно на разницу.");

        if (problems > 0) Debug.LogWarning(sb.ToString(), this);
        else Debug.Log(sb.ToString(), this);
    }

    /// <summary>
    /// Где точка покоя ОКАЖЕТСЯ после старта. Нужна только диагностике: вне
    /// Play заморозка ещё не прошла, и без этого схлопнутые точки выглядели бы
    /// нулями, то есть нормой.
    /// </summary>
    private Vector3 RestLocalPreview(Wheel w)
    {
        Vector3 src;

        if (w.mount != null) src = w.mount.position;
        else if (w.mesh != null)
        {
            src = w.mesh.position;
            Renderer r = w.mesh.GetComponentInChildren<Renderer>();
            if (r != null) src = r.bounds.center;
        }
        else src = transform.position;

        return transform.InverseTransformPoint(src) + Vector3.up * w.rideOffset;
    }

    private void OnDrawGizmosSelected()
    {
        if (!_drawGizmos) return;

        foreach (Wheel w in _wheels)
        {
            if (w == null) continue;

            Vector3 rest = Application.isPlaying
                ? transform.TransformPoint(w.restLocal)
                : transform.TransformPoint(RestLocalPreview(w));

            Gizmos.color = w.grounded
                ? new Color(0.4f, 1f, 0.5f, 0.9f)
                : new Color(1f, 0.45f, 0.3f, 0.9f);

            Gizmos.DrawLine(rest + Vector3.up * _travel, rest - Vector3.up * _travel);
            Gizmos.DrawWireSphere(rest + Vector3.up * w.offset, w.radius);

            if (Application.isPlaying && w.grounded)
            {
                Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
                Gizmos.DrawWireSphere(w.contact, 0.06f);
            }
        }
    }
}
