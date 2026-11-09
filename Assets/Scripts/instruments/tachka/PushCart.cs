using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// ТАЧКА. Подошёл, нажал F — взялся за ручки, катишь перед собой. ПКМ —
/// заваливаешь вперёд через колесо, груз высыпается сам.
///
/// ═══ ГЛАВНАЯ ПРАВКА ПРОТИВ ПЕРВОЙ ВЕРСИИ ═══
///
/// Тачка двигается через Rigidbody.MovePosition в FixedUpdate, а НЕ через
/// transform.position в Update.
///
/// Присваивание позиции — это ТЕЛЕПОРТАЦИЯ. Тело переносится в новую точку, не
/// задев ничего по пути. Пока корыто было набором слотов, это не мешало: груз
/// был приколочен и ехал вместе с ним. Но живой груз при телепортации остался
/// бы на месте, а стенки прошли бы сквозь него — и всё высыпалось бы прямо на
/// ходу.
///
/// MovePosition на кинематическом теле ВЫМЕТАЕТ объём: стенки честно толкают
/// динамику. Это единственное, что делает физический груз возможным.
///
/// Побочно тачка стала приятнее и без груза: она перестала проходить сквозь
/// углы и застревать в геометрии.
///
/// ═══ ПОВОРОТ — ПРОСТОЕ ПЛАВНОЕ СЛЕЖЕНИЕ ═══
///
/// Была попытка сделать курс через пружину с перелётом и накреном в
/// сторону поворота — на практике это давало нестабильное, дёргающееся
/// поведение вместо красивого изгиба. Вернулись к простому: позиция и
/// поворот экспоненциально подтягиваются к цели (Lerp/Slerp), без лишней
/// физики курса. Это то, что уже было проверено и работало нормально.
///
/// ═══ ЗАВАЛИВАЕТСЯ ВСЯ ТАЧКА, А НЕ КУЗОВ ═══
///
/// Настоящей тачкой не «поднимают кузов» — её наклоняют ВПЕРЁД ЧЕРЕЗ КОЛЕСО,
/// упираясь в него как в ось. Поэтому вращаем весь объект вокруг точки оси, а
/// не отдельный кузов вокруг своего шарнира.
///
/// Это ещё и проще: корыто со стенками остаётся неподвижным относительно
/// корпуса, компаундный коллайдер не перестраивается каждый кадр, и груз не
/// получает лишних толчков от едущих под ним стенок.
///
/// ═══ ПОЧЕМУ НЕ НАСЛЕДНИК NetworkVehicle ═══
///
/// Тот прячет модель игрока, подменяет камеру и сажает его в _seatPoint — это
/// про кабину. Тачку ТОЛКАЮТ: игрок остаётся собой, идёт ногами, смотрит своей
/// камерой. Прогони его через NetworkVehicle — придётся заново городить ходьбу
/// внутри машины, которая уже есть в FirstPersonController.
///
/// ═══ ТАЧКА НЕ ПОПАДАЕТ В РУКУ ═══
///
/// _canPickup гасится в Awake (CanPickup в базе не виртуальный — это свойство
/// над полем). Рука остаётся СВОБОДНОЙ, иначе, чтобы закинуть в тачку полено,
/// пришлось бы сначала её бросить.
///
/// Гасим кодом, а не галкой в инспекторе: галку однажды поставят обратно
/// «чтобы проверить» и забудут.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PushCart : InteractableItem
{
    [Header("Части")]
    [Tooltip("ТОЧКА ОСИ КОЛЕСА. Вокруг неё тачка заваливается вперёд — как " +
             "настоящая, упираясь в колесо.\n\n" +
             "Ставится в центр между колёсами, на высоте оси. Пусто — " +
             "возьмётся центр объекта, и наклон будет выглядеть, будто тачку " +
             "крутят за середину.")]
    [SerializeField] private Transform _axle;

    // Вращение и подвеска колёс больше не здесь — см. CartWheel.cs. Он висит
    // на каждом колесе отдельно: у тебя их два на общей оси, и при наезде на
    // кочку одним колесом второе не должно повторять его подскок. Одним полем
    // _wheel на корне тачки такую независимость не сделать.

    [Header("Толкание")]
    [Tooltip("На сколько метров впереди игрока едет тачка")]
    [SerializeField] private float _pushDistance = 1.35f;

    [Tooltip("МНОЖИТЕЛЬ СКОРОСТИ ПУСТОЙ ТАЧКИ")]
    [Range(0.2f, 1f)][SerializeField] private float _speedEmpty = 0.85f;

    [Tooltip("МНОЖИТЕЛЬ СКОРОСТИ ПОЛНОЙ ТАЧКИ.\n\n" +
             "Разница между пустой и полной — весь смысл возить её " +
             "туда-обратно вместо того, чтобы забивать под завязку каждый раз. " +
             "Сблизишь — и оптимальной стратегией станет «грузи всё и иди».")]
    [Range(0.2f, 1f)][SerializeField] private float _speedFull = 0.5f;

    [Tooltip("Как быстро тачка догоняет своё место. На ощупь это ВЕС: 30 — " +
             "приклеенная к игроку палка, 6 — тяжёлая телега, отстающая на " +
             "поворотах.")]
    [Range(1f, 30f)][SerializeField] private float _followSharpness = 12f;

    [Tooltip("КОМПЕНСАЦИЯ ОТСТАВАНИЯ НА РАЗГОНЕ.\n\n" +
             "Тачка догоняет цель экспоненциально, и на РАВНОМЕРНОМ ходу это " +
             "даёт постоянную ошибку: она едет ровно на «скорость делить на " +
             "резкость» метров позади того места, где должна быть. Сглаживание " +
             "не догоняет цель, оно к ней стремится — а цель убегает с той же " +
             "скоростью.\n\n" +
             "Снаружи это выглядит так, будто камера медленно въезжает в " +
             "тачку, пока игрок разгоняется, и отъезжает назад на " +
             "торможении — хотя игрок всё это время стоит на месте " +
             "относительно ручек.\n\n" +
             "Единица убирает ошибку ровно: упреждение считается из той же " +
             "резкости, что её и создаёт. Ноль возвращает прежнее отставание.")]
    [Range(0f, 1.5f)][SerializeField] private float _leadCompensation = 1f;

    [Tooltip("Слои земли. Убери отсюда слой самой тачки, слой игрока и слой " +
             "груза — иначе тачка будет «приземляться» на собственные дрова.")]
    [SerializeField] private LayerMask _groundMask = ~0;

    [Header("Опрокидывание")]
    [Tooltip("На сколько градусов заваливается тачка вперёд")]
    [Range(20f, 110f)][SerializeField] private float _tipAngle = 70f;

    [Tooltip("Секунд на полный наклон")]
    [Min(0.05f)][SerializeField] private float _tipSeconds = 0.45f;

    [Tooltip("С какого наклона груз получает пинок, доля 0..1.\n\n" +
             "Сыпать с первого градуса выглядит так, будто провалилось дно.")]
    [Range(0f, 1f)][SerializeField] private float _kickAt = 0.5f;

    [Tooltip("ЧЕРЕЗ СКОЛЬКО СЕКУНД ПОСЛЕ ОПРОКИДЫВАНИЯ ТАЧКА САМА ВЕРНЁТСЯ.\n\n" +
             "ПКМ теперь одноразовое действие, а не тумблер: нажал — высыпал, " +
             "подождал — сама встала на колёса. Повторные нажатия ПКМ, пока " +
             "тачка ещё опрокинута или возвращается, игнорируются — досрочно " +
             "прервать возврат нельзя, это упростило бы себе жизнь ценой " +
             "странного поведения на спам ПКМ.")]
    [Min(0.2f)][SerializeField] private float _autoReturnSeconds = 2f;

    [Header("━━━ Переворот набок ━━━")]
    [Tooltip("Боковая скорость, которую получает тачка, когда её вырывает из " +
             "рук на провале мини-игры, м/с")]
    [Range(0f, 8f)][SerializeField] private float _flipSideSpeed = 2.2f;

    [Tooltip("ПОДБРОС ВВЕРХ, м/с. Без него тачка скребёт колёсами по земле и " +
             "не столько падает, сколько уезжает вбок: колёса катятся, и " +
             "переворачиваться ей нечем.")]
    [Range(0f, 5f)][SerializeField] private float _flipLift = 1.2f;

    [Tooltip("РАСКРУТКА НАБОК, радиан в секунду.\n\n" +
             "Это главное из трёх. Тачка на одной оси устойчива поперёк, и " +
             "сама она не ляжет — её надо именно ОПРОКИНУТЬ. Занижено — " +
             "покачается и встанет обратно, и провал прочитается как «ничего " +
             "не случилось».")]
    [Range(0f, 20f)][SerializeField] private float _flipSpin = 7f;

    [Header("Спотыкание")]
    [Tooltip("Сила рывка при спотыкании, градусов крена")]
    [SerializeField] private float _stumbleRoll = 35f;

    [Tooltip("Сколько секунд длится рывок")]
    [Min(0.05f)][SerializeField] private float _stumbleSeconds = 0.7f;

    [Header("Руление (пока держишь ручки)")]
    [Tooltip("СКОРОСТЬ ПОВОРОТА ТАЧКИ, градусов в секунду при полном A/D.\n\n" +
             "Это НЕ угол поворота — это скорость, с которой курс тачки " +
             "непрерывно вращается, пока зажата клавиша. Отсюда и широкий, " +
             "\"по долгой оси\" разворот вместо резкого щелчка: держишь A — " +
             "тачка медленно доворачивает влево, отпустил — осталась смотреть " +
             "туда, куда довернул. 45 — разворот на 180° занимает 4 секунды.")]
    [SerializeField] private float _turnRateDegPerSec = 45f;

    [Header("Отладка")]
    [SerializeField] private bool _debugLog;

    // ───────── сеть ─────────
    /// <summary>Кто держится за ручки. null — тачка стоит.</summary>
    private readonly SyncVar<NetworkObject> _gripper = new(null);

    /// <summary>Опрокинута ли. Наклон анимируется локально по этому флагу.</summary>
    private readonly SyncVar<bool> _tipped = new(false);

    // ───────── локальное ─────────
    private CartTrough _trough;
    private float _tip01;
    private bool _kicked;
    private bool _isLocalGripper;

    /// <summary>Курс тачки, градусы по мировой Y. Крутится непрерывно от A/D,
    /// пока держишь ручки — это и есть новое руление, вместо слежения за
    /// направлением взгляда игрока.</summary>
    private float _headingDeg;

    /// <summary>Секунд осталось до автоматического возврата из опрокинутого
    /// состояния. Считает только сервер — это его решение, когда вернуть.</summary>
    private float _autoReturnTimer;

    // Был ли МЕСТНЫЙ игрок держателем в ПРОШЛОМ кадре, и чей это контроллер.
    // Нужны, чтобы поймать момент отпускания локально и сразу же вернуть
    // управление — не полагаясь на сетевой колбэк OnGripperChanged. Тот в
    // связке с хостовой веткой FishNet иногда не успевает добежать до нужного
    // экземпляра к нужному моменту, и CartSteering залипает включённым
    // навсегда, намертво блокируя мышь и стрейф до перезахода. Здесь же всё
    // решается в один кадр на той же машине, где и стоит реальная проблема.
    private bool _wasLocalGripper;
    private FirstPersonController _localFpc;

    // Свои коллайдеры (рама, стенки, ножки) и коллайдер того, кто держится за
    // ручки прямо сейчас. Нужны, чтобы на время толкания погасить между ними
    // столкновение — см. IgnoreCollisionWithGripper.
    private Collider[] _solid;

    // Цель для FixedUpdate: считаем в Update у водителя, применяем в физике.
    private Vector3 _targetPos;
    private Quaternion _targetRot;
    private bool _hasTarget;

    private float _stumbleTime;
    private float _stumbleSign = 1f;

    // УПОР В ПРЕПЯТСТВИЕ. Необязательный: нет компонента — тачка ведёт себя
    // ровно как раньше.
    //
    // Считать упор здесь было бы неправильно. Кинематическое тело не
    // останавливается ничем, и обработать это должен кто-то один, отдельно от
    // руления, — иначе ComputeTarget превращается в свалку из следования за
    // игроком, поиска земли, наклона вокруг оси и ещё разбора столкновений.
    private CartCrash _crash;

    // Крен для руления. Необязательный: нет компонента — тачка поворачивает
    // только от A/D, как раньше.
    private CartBalance _balance;

    // Ходовая. Если она есть, посадочную высоту спрашиваем у неё: держать
    // колёса на земле — её работа, и второго мнения тут быть не должно.
    private CartRig _rig;

    /// <summary>Расстояние от пивота до самой нижней точки тачки, метры.
    /// Меряется один раз — см. MeasureRideHeight.</summary>
    private float _pivotToBottom;

    private Vector3 _lastPlayerPos;
    private Vector3 _playerVel;
    private bool _hasPlayerPos;

    public bool IsGripped => _gripper.Value != null;
    public bool IsTipped => _tipped.Value;
    public CartTrough Trough => _trough;

    private Vector3 AxlePoint => _axle != null ? _axle.position : transform.position;

    protected override void Awake()
    {
        base.Awake();

        // CanPickup в базе не виртуальный — гасим поле.
        _canPickup = false;

        _trough = GetComponentInChildren<CartTrough>();
        _crash = GetComponent<CartCrash>();
        _balance = GetComponent<CartBalance>();
        _rig = GetComponentInChildren<CartRig>();
        CacheSolidColliders();
        MeasureRideHeight();

        if (_rb != null)
        {
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        if (_axle == null)
            Debug.LogWarning($"[Тачка] «{name}»: не назначена точка оси — " +
                             "опрокидывание будет крутить тачку за середину.", this);
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _gripper.OnChange += OnGripperChanged;
        _tipped.OnChange += OnTippedChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _gripper.OnChange -= OnGripperChanged;
        _tipped.OnChange -= OnTippedChanged;
    }

    // ───────────────────────── F: ВЗЯТЬСЯ / ОТПУСТИТЬ ─────────────────────────

    /// <summary>base.ServerInteract НЕ зовём — он положил бы тачку в руку.</summary>
    public override void ServerInteract(PlayerInteractor interactor)
    {
        if (interactor == null) return;

        if (_gripper.Value != null)
        {
            if (_gripper.Value == interactor.NetworkObject) ServerRelease();
            return;
        }

        _gripper.Value = interactor.NetworkObject;
        NetworkObject.GiveOwnership(interactor.Owner);

        // Груз должен считаться ТОЙ ЖЕ машиной, что и тачка, иначе стенки и
        // содержимое симулируются в разных местах и куча дрожит.
        if (_trough != null) _trough.ServerSyncAllOwnership();

        if (_debugLog) Debug.Log($"[Тачка] «{name}»: взялся {interactor.name}", this);
    }

    [Server]
    private void ServerRelease()
    {
        _gripper.Value = null;
        _tipped.Value = false;
        NetworkObject.RemoveOwnership();

        if (_trough != null) _trough.ServerSyncAllOwnership();
    }

    private void OnGripperChanged(NetworkObject prev, NetworkObject next, bool asServer)
    {
        if (asServer && base.IsClientInitialized) return;

        _isLocalGripper = next != null && next.IsOwner;
        _hasTarget = false;

        if (_rb != null)
        {
            // Пока катят — кинематическая, её ведёт MovePosition. Отпустили —
            // вернули физике, иначе тачка зависнет там, где её бросили.
            _rb.isKinematic = next != null;
            _rb.useGravity = next == null;
            if (next == null) _rb.WakeUp();
        }

        // ГЛАВНОЕ. Пока коллайдеры тачки не игнорируют держателя, она с ним
        // физически сталкивается КАЖДЫЙ кадр: MovePosition гонит корпус к
        // позиции игрока, задевает его CharacterController, того выталкивает,
        // тачка на следующем кадре снова едет туда, где игрок оказался, —
        // и это самораскручивающийся цикл, который и швыряет обоих вверх с
        // вращением. Толкающий не должен физически сталкиваться со своей же
        // тачкой, он ведёт её за ручки, а не врезается в неё.
        SetIgnoreGripper(prev, false);
        SetIgnoreGripper(next, true);

        if (next != null)
        {
            // Курс начинаем с ТЕКУЩЕГО направления тачки, а не с нуля и не с
            // взгляда игрока — иначе в момент захвата её резко довернуло бы
            // на угол между тем, куда она уже смотрела, и тем, куда смотрит
            // игрок.
            _headingDeg = transform.eulerAngles.y;

            // Включение здесь ДУБЛИРУЕТ то же самое в Update() (см.
            // _wasLocalGripper) — это не забытый старый код, а сознательная
            // подстраховка на случай, если сетевой колбэк добежит раньше
            // локальной проверки. Дважды присвоить true безопасно.
            FirstPersonController nfpc = next.GetComponent<FirstPersonController>();
            if (nfpc != null) nfpc.CartSteering = true;
        }
        if (next == null && prev != null && prev.IsOwner)
        {
            // ЭТОТ СБРОС — ТОЛЬКО ПОДСТРАХОВКА, не основная защита. Основная
            // теперь в Update() (_wasLocalGripper): именно она надёжно ловит
            // момент отпускания на самой машине игрока, без зависимости от
            // порядка сетевых колбэков, который и давал залипание раньше.
            FirstPersonController fpc = prev.GetComponent<FirstPersonController>();
            if (fpc != null)
            {
                fpc.SpeedMultiplier = 1f;
                fpc.CartSteering = false;
            }
        }
    }

    // ───────────────────────── ВОЖДЕНИЕ ─────────────────────────

    private void Update()
    {
        float target = _tipped.Value ? 1f : 0f;
        _tip01 = Mathf.MoveTowards(_tip01, target, Time.deltaTime / _tipSeconds);

        if (_stumbleTime > 0f) _stumbleTime -= Time.deltaTime;

        if (base.IsServerInitialized)
        {
            if (_tipped.Value && !_kicked && _tip01 >= _kickAt)
            {
                _kicked = true;
                if (_trough != null) _trough.ServerKickOut();
            }

            // АВТОВОЗВРАТ. Решает его только сервер — иначе на клиентах с
            // разным пингом таймер досчитал бы до нуля в разное время, и
            // тачка встала бы на колёса не одновременно у всех.
            if (_tipped.Value)
            {
                _autoReturnTimer -= Time.deltaTime;
                if (_autoReturnTimer <= 0f) _tipped.Value = false;
            }
        }

        // ОТСЛЕЖИВАНИЕ ПЕРЕХОДА, ЛОКАЛЬНО, БЕЗ СЕТИ. Захват и отпускание сами
        // по себе решает сервер через _gripper — но РЕАКЦИЮ на них для СВОЕГО
        // же контроллера надёжнее ловить прямо здесь, сравнивая с прошлым
        // кадром, а не через OnGripperChanged: это устраняет саму
        // возможность гонки с хостовой веткой колбэка.
        if (_isLocalGripper && !_wasLocalGripper)
        {
            NetworkObject g0 = _gripper.Value;
            _localFpc = g0 != null ? g0.GetComponent<FirstPersonController>() : null;
            if (_localFpc != null) _localFpc.CartSteering = true;
        }
        else if (!_isLocalGripper && _wasLocalGripper)
        {
            // ГЛАВНОЕ МЕСТО ПРАВКИ. Отпустил ручки — управление возвращается
            // СРАЗУ ЖЕ, в этом же кадре, на этой же машине. Раньше это
            // выполнялось только внутри OnGripperChanged, и в редких случаях
            // залипало: CartSteering оставался включён навсегда, и мышь с
            // A/D переставали слушаться до перезахода тачки или перезапуска.
            if (_localFpc != null)
            {
                _localFpc.CartSteering = false;
                _localFpc.SpeedMultiplier = 1f;
            }
            _localFpc = null;
        }
        _wasLocalGripper = _isLocalGripper;

        if (!_isLocalGripper) return;

        NetworkObject g = _gripper.Value;
        if (g == null) return;

        // ПКМ читаем здесь: тачки в руке нет, и ServerSecondaryUse до неё не
        // доходит — тот вызывает PlayerHand для предмета в руке. Теперь это
        // ОДНОРАЗОВОЕ действие: пока тачка опрокинута или едет обратно,
        // повторные нажатия ничего не делают — CmdTipOver сам их отфильтрует.
        if (Input.GetMouseButtonDown(1)) CmdTipOver();

        SteerAndFollow(g.transform);
    }

    /// <summary>
    /// НОВАЯ СХЕМА РУЛЕНИЯ. Раньше тачка просто копировала направление
    /// взгляда игрока — снаружи это выглядело так, будто её ТАЩАТ силой в
    /// сторону поворота, а не поворачивают: довернул мышью на 90° — и тачка в
    /// тот же миг обязана оказаться под тем же углом.
    ///
    /// Теперь курс тачки — это НЕЗАВИСИМАЯ величина, которая крутится
    /// непрерывно, пока зажат A или D, с постоянной скоростью
    /// (_turnRateDegPerSec). Это и есть "поворот по долгой оси": на 180°
    /// нужно несколько секунд удержания клавиши, а не один взмах мыши.
    ///
    /// A/D ПЕРЕСТАЮТ БЫТЬ СТРЕЙФОМ, ПОКА ДЕРЖИШЬ РУЧКИ (см. CartSteering в
    /// FirstPersonController) — та же самая клавиша теперь крутит тачку, а не
    /// двигает игрока вбок. Иначе получались бы два независимых движения
    /// одновременно: тачка поворачивает сама по себе, а игрок ещё и уезжает
    /// боком, как будто на середине поворота отпустил одну ручку.
    ///
    /// КУРС ИГРОКА ПРИНУДИТЕЛЬНО РАВНЯЕТСЯ КУРСУ ТАЧКИ (SetYawDegrees) —
    /// человек, толкающий тачку, физически смотрит туда же, куда она едет.
    /// Мышь в это время поворот тела не трогает (тоже через CartSteering) —
    /// два источника поворота одного и того же угла дрались бы каждый кадр.
    /// W/S при этом работают как обычно и двигают игрока вдоль этого курса
    /// вперёд-назад — "ходит вперёд-назад", а поворачивает именно тачка.
    /// </summary>
    private void SteerAndFollow(Transform player)
    {
        float turn = Input.GetAxisRaw("Horizontal");

        // ── КРЕН КАК РУЛЬ ──
        //
        // Прямой поворот от A/D остаётся, но основную работу делает крен: чем
        // сильнее тачку завалило, тем круче она идёт в ту сторону.
        //
        // Это переворачивает смысл мини-игры. Раньше крен был чистым штрафом,
        // и правильной игрой было держать его в нуле — то есть не поворачивать
        // вовсе. Теперь крен НУЖЕН, чтобы ехать по дуге, и игрок сам заваливает
        // тачку ради поворота, сам же удерживая её на грани. Контр-клавиша при
        // этом перестаёт быть только спасением и становится ещё и выходом из
        // поворота.
        //
        // Знак совпадает с креном по построению: «валит вправо» набирается от
        // D, а D — это поворот вправо. Разойтись они не могут.
        float leanTurn = _balance != null ? _balance.Lean * _balance.LeanSteerDegrees : 0f;

        _headingDeg += (turn * _turnRateDegPerSec + leanTurn) * Time.deltaTime;

        FirstPersonController fpc = player.GetComponent<FirstPersonController>();
        if (fpc != null) fpc.SetYawDegrees(_headingDeg);

        ComputeTarget(player);
    }

    /// <summary>
    /// Куда тачка должна приехать. Считаем в Update — здесь свежий ввод
    /// игрока, — а ПРИМЕНЯЕМ в FixedUpdate, где живёт физика.
    ///
    /// Тачка стоит РОВНО ПО МИРУ и не наследует наклон камеры: посмотрел под
    /// ноги — воткнулась бы в землю, посмотрел вверх — улетела. Та же причина,
    /// что и у разложенной стремянки.
    /// </summary>
    private void ComputeTarget(Transform player)
    {
        FirstPersonController fpc = player.GetComponent<FirstPersonController>();

        if (fpc != null && _trough != null)
        {
            float load = Mathf.Clamp01(_trough.MassKg / 160f);
            fpc.SpeedMultiplier = Mathf.Lerp(_speedEmpty, _speedFull, load);
        }

        Vector3 fwd = player.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = transform.forward;
        fwd.Normalize();

        // ── УПРЕЖДЕНИЕ ──
        //
        // Скорость игрока меряется здесь, а не берётся у контроллера: тачку
        // может катить чужой персонаж, и лезть в его внутренности ради одного
        // вектора незачем.
        float pdt = Mathf.Max(0.0001f, Time.deltaTime);

        Vector3 raw = _hasPlayerPos
            ? (player.position - _lastPlayerPos) / pdt : Vector3.zero;

        _lastPlayerPos = player.position;
        _hasPlayerPos = true;
        raw.y = 0f;

        _playerVel = Vector3.Lerp(_playerVel, raw, 1f - Mathf.Exp(-10f * pdt));

        // Ошибка экспоненциального сглаживания на равномерном ходу равна
        // «скорость / резкость». Прибавляем ровно её — не подгонка, а та же
        // формула наоборот.
        Vector3 lead = _playerVel * (_leadCompensation / Mathf.Max(1f, _followSharpness));

        Vector3 pos = player.position + fwd * _pushDistance + lead;

        if (FindGround(pos + Vector3.up * 1.5f, out float y))
        {
            // ВЫСОТУ СПРАШИВАЕМ У ХОДОВОЙ, а не считаем сами.
            //
            // Своя мерка по нижней точке геометрии почти верна — и «почти»
            // здесь стоит дорого. Ходовая держит колёса по СВОИМ точкам
            // опоры, и стоит числам разойтись на сантиметр, как подвеска
            // начинает выбирать эту разницу, упирается в предел хода и
            // остаётся в упоре. А в упоре она перестаёт отрабатывать что-либо
            // ещё: ни ударов, ни качки, ни сплющивания.
            //
            // Спорить о том, где земля, могут только двое. Оставляем одного.
            float ride = _rig != null ? _rig.RideHeight : -1f;

            pos.y = y + (ride >= 0f ? ride : _pivotToBottom);
        }

        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);

        // Наклон — вокруг ОСИ КОЛЕСА, а не вокруг пивота. Вращаем rot, потом
        // компенсируем смещение, чтобы ось осталась на месте.
        float pitch = _tipAngle * _tip01;
        float roll = _stumbleTime > 0f
            ? _stumbleRoll * _stumbleSign * Mathf.Sin(_stumbleTime / _stumbleSeconds * Mathf.PI)
            : 0f;

        rot *= Quaternion.Euler(pitch, 0f, roll);

        // Смещение пивота относительно оси в локальных координатах — постоянно,
        // поэтому его можно просто повернуть новым поворотом.
        Vector3 axleLocal = _axle != null ? transform.InverseTransformPoint(_axle.position)
                                          : Vector3.zero;
        Vector3 axleWorldTarget = pos + rot * axleLocal;
        Vector3 axleWanted = pos;   // ось должна остаться там, куда ведёт игрок

        Vector3 want = pos + (axleWanted - axleWorldTarget);

        // УПОР. Обрезаем цель ПОСЛЕ компенсации оси и ДО передачи в физику:
        // раньше — и обрезка съехала бы на смещение пивота, позже — и обрезать
        // было бы уже нечего, MovePosition тянет к цели независимо от того, что
        // стоит на пути.
        if (_crash != null)
            want = _crash.ClampTarget(_rb != null ? _rb.position : transform.position, want);

        _targetPos = want;
        _targetRot = rot;
        _hasTarget = true;
    }

    /// <summary>
    /// ЕДИНСТВЕННОЕ МЕСТО, ГДЕ ТАЧКА ДВИГАЕТСЯ.
    ///
    /// MovePosition, а не transform.position: кинематическое тело, двинутое
    /// так, ВЫМЕТАЕТ объём и толкает динамику. Присваивание позиции — это
    /// телепортация, и груз при ней остался бы на месте, а стенки прошли бы
    /// сквозь него.
    /// </summary>
    private void FixedUpdate()
    {
        if (!_isLocalGripper || !_hasTarget || _rb == null) return;

        float k = 1f - Mathf.Exp(-_followSharpness * Time.fixedDeltaTime);

        _rb.MovePosition(Vector3.Lerp(_rb.position, _targetPos, k));
        _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, _targetRot, k));
    }

    /// <summary>
    /// НАСКОЛЬКО НИЗ ТАЧКИ НИЖЕ ЕЁ ПИВОТА. Меряется ОДИН РАЗ при старте.
    ///
    /// ═══ ПОЧЕМУ НЕ ПЕРВЫЙ КОЛЛАЙДЕР ═══
    ///
    /// Раньше здесь стоял GetComponentInChildren&lt;Collider&gt;() — он
    /// возвращает ПЕРВЫЙ коллайдер в иерархии, а не самый нижний. У тачки их
    /// с десяток: рама, стенки корыта, ножки, колёса. Первым оказывался тот,
    /// что стоит выше колёс, и тачку ставили по его низу — то есть УТАПЛИВАЛИ
    /// в землю ровно на разницу.
    ///
    /// Симптом при этом очень характерный: тачка проваливается ТОЛЬКО пока за
    /// неё держатся. Отпустил — включилась обычная физика, и она честно встала
    /// на колёса. Выглядит как отскок, а на деле это единственный момент, когда
    /// высоту считает не этот код.
    ///
    /// ═══ ПОЧЕМУ ОДИН РАЗ, А НЕ КАЖДЫЙ КАДР ═══
    ///
    /// Колёса двигает подвеска. Померь низ на ходу — и получится петля:
    ///
    ///   подвеска поджала колесо → низ тачки стал выше → тачку поставили
    ///   ниже → подвеска поджалась ещё сильнее
    ///
    /// Замер в Awake берёт авторскую позу, где колёса стоят там, где их
    /// поставили в редакторе, и петля становится невозможной.
    ///
    /// Меши учитываются наравне с коллайдерами: у колеса своего коллайдера
    /// может не быть вовсе, а на земле стоит именно оно.
    /// </summary>
    private void MeasureRideHeight()
    {
        float min = float.MaxValue;

        if (_solid != null)
            foreach (Collider c in _solid)
                if (c != null) min = Mathf.Min(min, c.bounds.min.y);

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            if (r != null) min = Mathf.Min(min, r.bounds.min.y);

        if (min == float.MaxValue)
        {
            _pivotToBottom = 0f;
            Debug.LogWarning($"[Тачка] «{name}»: нечем померить низ — тачку " +
                             "будут ставить пивотом на землю.", this);
            return;
        }

        _pivotToBottom = transform.position.y - min;

        if (_pivotToBottom < 0.01f)
            Debug.LogWarning(
                $"[Тачка] «{name}»: пивот стоит на самом низу модели или ниже " +
                $"({_pivotToBottom:0.000} м). Тачка будет ездить, но подвеске " +
                "нечем работать вверх. Подними пивот на высоту оси.", this);
    }

    private float GroundOffsetY() => transform.position.y - _pivotToBottom;

    private bool FindGround(Vector3 from, out float y)
    {
        y = 0f;
        if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 4f,
                             _groundMask, QueryTriggerInteraction.Ignore))
            return false;

        if (hit.collider.transform.IsChildOf(transform)) return false;

        y = hit.point.y;
        return true;
    }

    // ───────────────────────── ПКМ: ОПРОКИНУТЬ ─────────────────────────

    /// <summary>
    /// Опрокинуть. ОДНОРАЗОВОЕ действие, не тумблер: сработало только если
    /// тачка сейчас на колёсах. Пока она опрокинута или едет обратно —
    /// повторные вызовы молча игнорируются. Возврат происходит сам, по
    /// таймеру в Update, а не по второму нажатию ПКМ.
    /// </summary>
    [ServerRpc(RequireOwnership = true)]
    private void CmdTipOver()
    {
        if (_tipped.Value) return;

        _tipped.Value = true;
        _autoReturnTimer = _autoReturnSeconds;
    }

    private void OnTippedChanged(bool prev, bool next, bool asServer)
    {
        if (asServer && base.IsClientInitialized) return;
        if (!next) _kicked = false;
    }

    // ───────────────────────── СПОТЫКАНИЕ ─────────────────────────

    /// <summary>
    /// Тряхнуть тачку. Груз при этом вываливается САМ, если тряхнуло сильно —
    /// специального кода для этого нет и не нужно, физика не спрашивает
    /// разрешения.
    ///
    /// Звать можно откуда угодно: с кочки, с удара, с проваленной мини-игры на
    /// баланс. Тачка не знает и не должна знать, почему её тряхнуло.
    /// </summary>
    /// <summary>
    /// ВЫРВАТЬ ИЗ РУК И ПОЛОЖИТЬ НАБОК. Исход проваленной мини-игры на баланс.
    ///
    /// ═══ ЧЕМ ЭТО ОТЛИЧАЕТСЯ ОТ ОПРОКИДЫВАНИЯ НА ПКМ ═══
    ///
    /// ПКМ кладёт тачку ПО АНИМАЦИИ: она остаётся в руках, под управлением, и
    /// сама встаёт обратно по таймеру. Это осознанное действие игрока.
    ///
    /// Здесь всё наоборот — тачка должна вырваться и упасть САМА. Поэтому
    /// ручки отпускаются, тело перестаёт быть кинематическим, и дальше падение
    /// считает физика. Анимация всегда кладёт тачку одинаково; физика уронит
    /// её на склоне не так, как на ровном, и зацепит то, что рядом.
    ///
    /// ═══ ПОРЯДОК ═══
    ///
    /// Груз → ручки → импульс, и переставить нельзя:
    ///
    ///   груз ПЕРВЫМ — пока стенки ещё на месте, ему есть откуда вылететь;
    ///   ручки ВТОРЫМИ — отпускание и делает тело динамическим;
    ///   импульс ПОСЛЕДНИМ — кинематическое тело импульс просто проглотит.
    ///
    /// Раскрутка идёт вокруг оси «вверх × вбок»: это продольная ось тачки, та
    /// самая, вокруг которой она заваливается на одно колесо. Толкать вбок без
    /// раскрутки бесполезно — тачка на одной оси устойчива поперёк и просто
    /// укатится в сторону.
    /// </summary>
    [Server]
    public void ServerFlipOver(Vector3 side, float strength01 = 1f)
    {
        side.y = 0f;
        if (side.sqrMagnitude < 0.0001f) side = transform.right;
        side.Normalize();

        strength01 = Mathf.Clamp(strength01, 0.2f, 2f);

        if (_trough != null)
        {
            _trough.ServerWakeCargo();
            _trough.ServerKickOut();
        }

        ServerRelease();

        if (_rb != null)
        {
            // OnGripperChanged уже должен был это сделать. Повторяем явно:
            // порядок колбэков SyncVar на хосте зависит от того, поднят ли
            // клиент, и полагаться тут на него — значит иногда молча
            // прикладывать импульс к кинематическому телу.
            _rb.isKinematic = false;
            _rb.useGravity = true;
            _rb.WakeUp();

            _rb.AddForce(side * (_flipSideSpeed * strength01)
                       + Vector3.up * (_flipLift * strength01),
                         ForceMode.VelocityChange);

            _rb.AddTorque(Vector3.Cross(Vector3.up, side) * (_flipSpin * strength01),
                          ForceMode.VelocityChange);
        }

        if (_debugLog) Debug.Log($"[Тачка] «{name}»: вырвало из рук и положило набок", this);
    }

    [Server]
    public void ServerStumble(float strength01 = 1f)
    {
        _stumbleTime = _stumbleSeconds * Mathf.Clamp01(strength01);
        _stumbleSign = Random.value > 0.5f ? 1f : -1f;

        RpcStumble(_stumbleTime, _stumbleSign);

        // Спящий Rigidbody не заметит, что стенки поехали, пока его не
        // разбудят — груз просто провис бы сквозь дно.
        if (_trough != null) _trough.ServerWakeCargo();

        if (_debugLog) Debug.Log($"[Тачка] «{name}»: споткнулся ({strength01:0.00})", this);
    }

    [ObserversRpc]
    private void RpcStumble(float time, float sign)
    {
        if (base.IsServerInitialized) return;
        _stumbleTime = time;
        _stumbleSign = sign;
    }

    // ───────────────────────── СТОЛКНОВЕНИЕ С ДЕРЖАТЕЛЕМ ─────────────────────────

    /// <summary>Все нетриггерные коллайдеры тачки: рама, стенки корыта, ножки,
    /// колёса. Груз в этот список не входит — у него свои отдельные объекты.</summary>
    private void CacheSolidColliders()
    {
        Collider[] all = GetComponentsInChildren<Collider>(true);
        var list = new System.Collections.Generic.List<Collider>(all.Length);

        foreach (Collider c in all)
            if (c != null && !c.isTrigger) list.Add(c);

        _solid = list.ToArray();

        if (_solid.Length == 0)
            Debug.LogWarning($"[Тачка] «{name}»: ни одного обычного коллайдера " +
                             "в иерархии — тачка провалится сквозь землю.", this);
    }

    /// <summary>Включить/выключить столкновение со ВСЕМИ коллайдерами того, кто
    /// держится за ручки. У игрока обычно один CharacterController, но берём
    /// первый попавшийся коллайдер как запасной вариант на случай другой
    /// модели персонажа.</summary>
    private void SetIgnoreGripper(NetworkObject who, bool ignore)
    {
        if (who == null || _solid == null) return;

        Collider body = who.GetComponent<CharacterController>();
        if (body == null) body = who.GetComponentInChildren<Collider>();
        if (body == null) return;

        foreach (Collider c in _solid)
            if (c != null) Physics.IgnoreCollision(c, body, ignore);
    }

    // ───────────────────────── ГИЗМО ─────────────────────────

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(AxlePoint, 0.1f);
        Gizmos.DrawLine(AxlePoint - transform.right * 0.5f,
                        AxlePoint + transform.right * 0.5f);
    }
}
