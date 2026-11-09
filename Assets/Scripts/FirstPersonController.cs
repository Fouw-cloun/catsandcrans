using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Движение и обзор от первого лица. Owner-authoritative: ввод читает только
/// владелец, NetworkTransform (Client Authoritative) синхронизирует позицию и
/// поворот тела остальным. Наклон камеры (pitch) — локальный, не синхронизируется.
///
/// ЗАМЕНЯЕТ старые скрипты PlayerMovement.cs и OwnerCamera.cs — удали их с префаба.
///
/// Структура префаба игрока:
///   Player (CharacterController, NetworkObject, NetworkTransform, этот скрипт)
///    └─ CameraRoot (пустой объект на высоте головы, ~1.6 по Y)
///        └─ Main Camera (тег MainCamera, + AudioListener)
/// Перетащи CameraRoot в поле Camera Root.
///
/// ВВОД: старый Input Manager. Если включён только новый Input System —
/// Project Settings → Player → Active Input Handling → Both.
///
/// ─────────────────────────────────────────────────────────────────────────
/// ИЗМЕНЕНО ПОД UI:
///
/// 1. Esc больше НЕ обрабатывается здесь — его забрал PauseMenuUI. Раньше Esc
///    просто освобождал курсор, а любой следующий клик захватывал обратно.
///    С меню это ломалось напрочь: открыл паузу, кликнул кнопку — курсор
///    мгновенно залочился, и меню стало неуправляемым.
///
/// 2. Пока открыто любое модальное окно (UiModal.AnyOpen), ввод не читается
///    вообще: ни движение, ни мышь, ни перезахват курсора. Курсором в это
///    время управляет UiModal, чтобы два места не дрались за него.
///
/// 3. Move() разбит на Move() и MoveInternal(): при открытом меню физика
///    продолжает считаться с нулевым вводом. Так игрок не зависает в воздухе,
///    если открыл паузу в прыжке, и его не выталкивает из коллизий.
///
/// 4. Чувствительность мыши умножается на GameSettings.MouseSensitivity из
///    меню настроек. Поле _mouseSensitivity стало базовым множителем —
///    поставь его в 1, иначе чувствительность умножится дважды.
/// ─────────────────────────────────────────────────────────────────────────
/// ДОБАВЛЕНО ЛАЗАНИЕ (LadderItem / LadderClimbZone):
///
/// 5. MoveInternal() уводит в ClimbInternal(), когда игрок висит на лестнице.
///    Отдельным компонентом это сделать нельзя: MoveInternal безусловно льёт
///    гравитацию и двигает CharacterController, и второй компонент дрался бы
///    с ним за одно и то же тело каждый кадр. Точка входа тут может быть
///    только одна.
///
/// 6. BeginClimb / UpdateClimb / EndClimb зовёт LadderClimbZone у СВОЕГО
///    игрока. По сети это не ходит: движение персонажа и так считает его
///    владелец, а позиция расходится через NetworkTransform.
///
/// 7. UpdateClimb приходит каждый кадр, а не один раз при захвате: напарник
///    может нести лестницу в руках, и висящего на ней надо тащить вместе с
///    ней. Смещение основания между кадрами копится в _climbCarry и
///    применяется к контроллеру.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : NetworkBehaviour
{
    [Header("Движение")]
    [SerializeField] private float _moveSpeed = 5f;
    [SerializeField] private float _gravity = -20f;

    [Header("Мышь")]
    [Tooltip("Пустой объект на высоте головы, к нему прикреплена камера")]
    [SerializeField] private Transform _cameraRoot;
    [Tooltip("Базовый множитель. Итог = это значение × настройка из меню. " +
             "Поставь 1, если пользуешься меню настроек")]
    [SerializeField] private float _mouseSensitivity = 1f;
    [SerializeField] private float _minPitch = -80f;
    [SerializeField] private float _maxPitch = 80f;

    [Header("Толчок/шланг (вышибание, упор)")]
    [Tooltip("Как быстро гаснет толчок от струи, ед/сек")]
    [SerializeField] private float _knockbackDrag = 8f;

    [Header("Прыжок")]
    [Tooltip("Разрешить прыжок. Выключи, если где-то понадобится персонаж, " +
             "который прыгать не должен.")]
    [SerializeField] private bool _jumpEnabled = true;

    [Tooltip("ВЫСОТА ПРЫЖКА В МЕТРАХ, а не скорость.\n\n" +
             "Скорость считается из неё и текущей гравитации по формуле " +
             "sqrt(2*g*h), поэтому подкрутишь гравитацию — высота останется " +
             "ровно такой, как здесь написано. Задавать скорость напрямую " +
             "было бы неудобно: она уезжала бы при каждой правке гравитации.\n\n" +
             "1.1 — комфортный прыжок, чтобы запрыгнуть на шину и раскачать меха.")]
    [Min(0.05f)][SerializeField] private float _jumpHeight = 1.1f;

    [Tooltip("Клавиша прыжка")]
    [SerializeField] private KeyCode _jumpKey = KeyCode.Space;

    [Header("Лазание по лестнице")]
    [Tooltip("Скорость подъёма и спуска, м/с")]
    [SerializeField] private float _climbSpeed = 2.6f;
    [Tooltip("Скорость перехвата вбок вдоль перекладин, доля от скорости подъёма")]
    [Range(0f, 1f)][SerializeField] private float _climbSideFactor = 0.5f;
    [Tooltip("С какой силой игрок отталкивается от лестницы на пробел")]
    [SerializeField] private float _climbPushOff = 4f;
    [Tooltip("Клавиша «спрыгнуть с лестницы»")]
    [SerializeField] private KeyCode _climbJumpKey = KeyCode.Space;

    private Vector3 _externalVelocity; // толчок струёй, затухает
    private CharacterController _controller;
    private Camera _camera;
    private AudioListener _listener;
    private float _pitch;
    private float _verticalVelocity;
    private bool _craneSuspended;
    public bool CraneSuspended => _craneSuspended;

    // Пока серверный подвес двигает тело, оставляем только обзор мышью.
    public void SetCraneSuspended(bool suspended, Vector3 releaseVelocity)
    {
        _craneSuspended = suspended;
        if (suspended) { _climbing = false; _climbCarry = Vector3.zero; }
        _verticalVelocity = suspended ? 0f : releaseVelocity.y;
        _externalVelocity = suspended ? Vector3.zero : new Vector3(releaseVelocity.x, 0f, releaseVelocity.z);
    }


    // ───────── состояние лазания ─────────
    private bool _climbing;
    private Vector3 _climbBase;               // нижняя перекладина в мире
    private Vector3 _climbAxis = Vector3.up;  // куда лезть
    private float _climbLength;               // длина лестницы, метры
    private Vector3 _climbCarry;              // на сколько уехала лестница за кадр

    /// <summary>Множитель скорости (1 = норма). Снижается при переноске тяжёлого.</summary>
    [System.NonSerialized] public float SpeedMultiplier = 1f;

    /// <summary>Включён, пока игрок толкает тачку с рулением по A/D (см.
    /// PushCart). Пока включён: мышь не поворачивает тело по горизонтали
    /// (курс задаёт тачка), а стрейф (A/D) не двигает игрока вбок (те же
    /// клавиши уходят в поворот тачки). Наклон камеры мышью работает всегда.</summary>
    [System.NonSerialized] public bool CartSteering = false;

    // ───────── ПАУЗА ОБЗОРА ДЛЯ ИНСТРУМЕНТОВ ─────────
    // Ножовка, грохот, шприц: пока работаешь, мышь уходит в инструмент, а камера стоит.
    // Инструмент зовёт HoldLook() КАЖДЫЙ кадр, пока держит взгляд. Перестал звать —
    // на следующий кадр обзор возвращается сам (выронил пилу, отдал владение — не залипнет).
    // Углы при этом не трогаются, поэтому после отпускания камера остаётся там же, куда смотрел.
    private int _lookHoldFrame = -10;
    public void HoldLook() => _lookHoldFrame = Time.frameCount;
    public bool LookHeld => Time.frameCount - _lookHoldFrame <= 1;

    /// <summary>Жёстко поставить курс тела, градусы по мировой Y. Зовёт
    /// PushCart каждый кадр, пока рулит тачкой, — так тело игрока всегда
    /// смотрит туда же, куда сейчас повёрнута тачка, без борьбы с обычным
    /// поворотом мышью (который на это время выключен через CartSteering).</summary>
    public void SetYawDegrees(float yawDeg) => transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        if (_cameraRoot != null)
        {
            _camera = _cameraRoot.GetComponentInChildren<Camera>(true);
            _listener = _cameraRoot.GetComponentInChildren<AudioListener>(true);
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        bool owner = base.IsOwner;

        // Камера и AudioListener активны ТОЛЬКО у владельца, иначе будет
        // конфликт нескольких камер и слушателей звука в сцене.
        if (_camera != null) _camera.enabled = owner;
        if (_listener != null) _listener.enabled = owner;

        if (!owner)
        {
            // Чужим персонажем не управляем — выключаем скрипт.
            enabled = false;
            return;
        }

        // Настройки читаем здесь: к этому моменту игрок точно в игровой сцене,
        // а Load() внутри защищён от повторного вызова.
        GameSettings.Load();

        // Курсор захватываем только если меню не открыто. Иначе, если игрок
        // зашёл в сессию с открытой паузой, мы бы отобрали у него курсор.
        if (!UiModal.AnyOpen) LockCursor(true);
    }

    private void Update()
    {
        if (!base.IsOwner) return;
        if (_craneSuspended)
        {
            if (!UiModal.AnyOpen && Cursor.lockState == CursorLockMode.Locked) Look();
            return;
        }

        // Открыто меню — ввод не читаем совсем. Курсором управляет UiModal:
        // держать это в двух местах нельзя, они начнут перебивать друг друга.
        if (UiModal.AnyOpen)
        {
            // Физику всё равно крутим, но с нулевым вводом — чтобы игрок,
            // открывший паузу в прыжке, нормально приземлился, а не завис.
            MoveInternal(0f, 0f);
            return;
        }

        // Esc обрабатывает PauseMenuUI. Здесь оставлен только перезахват
        // курсора кликом — удобно после Alt+Tab.
        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
            LockCursor(true);

        if (Cursor.lockState == CursorLockMode.Locked) Look();
        Move();
    }

    private void Look()
    {
        if (LookHeld) return;   // инструмент держит взгляд (HoldLook) — мышь его, камера стоит

        // Базовый множитель × настройка игрока из меню.
        float sens = _mouseSensitivity * GameSettings.MouseSensitivity;

        float mouseX = Input.GetAxisRaw("Mouse X") * sens;
        float mouseY = Input.GetAxisRaw("Mouse Y") * sens;

        // Горизонталь — поворот всего тела (синхронизируется NetworkTransform).
        // ПОКА ВЕДЁШЬ ТАЧКУ, ЭТОТ ПОВОРОТ ОТКЛЮЧЕН: курс в этот момент задаёт
        // PushCart через SetYawDegrees, а не мышь. Две независимые команды на
        // один и тот же yaw дрались бы каждый кадр — то мышь довернёт, то
        // тачка перезапишет обратно, и вид дёргался бы. Просто не читаем
        // mouseX в тело, пока CartSteering включён; накопленное значение
        // теряется само собой (это дельта за кадр, а не абсолютный угол), так
        // что при отпускании тачки скачка не будет.
        if (!CartSteering) transform.Rotate(Vector3.up, mouseX);

        // Вертикаль — наклон только камеры (локально). Работает всегда, даже
        // с тачкой: смотреть под ноги или по сторонам, толкая её, можно.
        _pitch = Mathf.Clamp(_pitch - mouseY, _minPitch, _maxPitch);
        if (_cameraRoot != null)
            _cameraRoot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    private void Move()
    {
        // ПОКА ВЕДЁШЬ ТАЧКУ, СТРЕЙФ (A/D) ВЫКЛЮЧЕН ЦЕЛИКОМ. Это не баг и не
        // временная мера: PushCart сам читает Horizontal и превращает его в
        // поворот тачки, а не в шаг вбок. Если бы игрок при этом ещё и
        // стрейфился, получалось бы два независимых движения от одной кнопки
        // — тачка поворачивает, а сам игрок вдобавок уезжает боком, как будто
        // выпустил ручки на середине поворота.
        float h = CartSteering ? 0f : Input.GetAxisRaw("Horizontal");
        MoveInternal(h, Input.GetAxisRaw("Vertical"));
    }

    /// <summary>Движение с заданным вводом. Вынесено отдельно, чтобы при
    /// открытом меню можно было прогнать гравитацию и затухание толчка,
    /// подав нули вместо клавиш.</summary>
    private void MoveInternal(float h, float v)
    {
        // Висим на лестнице — там своя физика, без гравитации.
        if (_climbing) { ClimbInternal(h, v); return; }

        Vector3 move = transform.right * h + transform.forward * v;
        if (move.sqrMagnitude > 1f) move.Normalize();

        // ПРИЖИМ К ЗЕМЛЕ. -1 вместо 0: с нулём CharacterController на первом же
        // кадре после шага "теряет" опору на неровностях и начинает мигать
        // isGrounded, а вместе с ним мигает и возможность прыгнуть.
        if (_controller.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -1f;

        // ПРЫЖОК. Читаем только когда реально можем прыгнуть — и обязательно
        // ДО прибавки гравитации: если прибавить сначала, то на кадре отрыва
        // гравитация успеет откусить от импульса, и высота прыжка будет
        // плавать в зависимости от FPS.
        //
        // Формула v = sqrt(2 * g * h) даёт ровно ту скорость, при которой
        // игрок поднимется на _jumpHeight метров. Задавать скорость напрямую
        // неудобно: подкрутил гравитацию — и высота прыжка уехала, а так она
        // остаётся тем, что написано в поле.
        if (_jumpEnabled && _controller.isGrounded && !UiModal.AnyOpen
            && Input.GetKeyDown(_jumpKey))
        {
            _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(_gravity) * _jumpHeight);
        }

        _verticalVelocity += _gravity * Time.deltaTime;

        Vector3 velocity = move * (_moveSpeed * SpeedMultiplier);
        velocity.y = _verticalVelocity;
        velocity += _externalVelocity;          // толчок от струи (вышибание)
        _controller.Move(velocity * Time.deltaTime);

        // Затухание толчка.
        _externalVelocity = Vector3.MoveTowards(_externalVelocity, Vector3.zero,
                                                _knockbackDrag * Time.deltaTime);
    }

    private void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    /// <summary>Выровнять взгляд после выхода из техники: убрать крен/тангаж
    /// тела (оставить только рыскание по Y) и обнулить наклон камеры. Нужно,
    /// потому что пока игрок сидел, его transform приравнивался к сиденью,
    /// которое могло крутиться/наклоняться (башня крана) — без сброса камеру
    /// после выхода мотает и переворачивает.</summary>
    public void ResetLookToCurrent()
    {
        // Тело — только горизонтальное направление, без крена и тангажа.
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
        transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);

        // Наклон камеры — в ноль, и синхронизируем внутреннее поле _pitch.
        _pitch = 0f;
        if (_cameraRoot != null)
            _cameraRoot.localRotation = Quaternion.identity;

        _verticalVelocity = 0f;
    }

    // ───────── ЛАЗАНИЕ ПО ЛЕСТНИЦЕ ─────────

    /// <summary>Висит ли игрок на лестнице. Читают инструменты, которым нельзя
    /// работать в подвешенном состоянии, и техника — чтобы не усадить в кабину
    /// человека, который сейчас на середине стремянки.</summary>
    public bool IsClimbing => _climbing;

    /// <summary>Зацепиться за лестницу. Зовёт LadderClimbZone у СВОЕГО игрока.</summary>
    public void BeginClimb(Vector3 basePoint, Vector3 axis, float length)
    {
        // Базу ставим ДО UpdateClimb, иначе тот посчитает смещение от прошлой
        // лестницы и в первый же кадр швырнёт игрока через всю площадку.
        _climbBase = basePoint;
        _climbCarry = Vector3.zero;

        _climbing = true;
        _verticalVelocity = 0f;

        UpdateClimb(basePoint, axis, length);
    }

    /// <summary>Обновить геометрию лестницы. Зовётся каждый кадр, пока игрок
    /// висит: напарник может нести стремянку в руках, и держаться надо за её
    /// текущее место. Смещение основания копим и отдаём контроллеру в
    /// ClimbInternal — порядок Update у зоны и у контроллера не гарантирован,
    /// поэтому применять его прямо здесь нельзя.</summary>
    public void UpdateClimb(Vector3 basePoint, Vector3 axis, float length)
    {
        if (_climbing) _climbCarry += basePoint - _climbBase;

        _climbBase = basePoint;
        _climbAxis = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.up;
        _climbLength = length;
    }

    /// <summary>Отцепиться без толчка: вышел из зоны, лестницу сложили, унесли,
    /// выдернули из земли.</summary>
    public void EndClimb()
    {
        _climbing = false;
        _climbCarry = Vector3.zero;
    }

    /// <summary>
    /// Текущая вертикальная скорость, м/с. Отрицательная — падает.
    ///
    /// Нужна мехам: сила залпа там равна СИЛЕ УДАРА, а удар — это и есть
    /// скорость падения. Копить отдельный «заряд» незачем, физика уже всё
    /// посчитала.
    /// </summary>
    public float VerticalVelocity => _verticalVelocity;

    /// <summary>
    /// Толкнуть игрока. Зовут ветер от мехов и всё прочее, что должно сдувать.
    ///
    /// Пишем в _externalVelocity, а не в _verticalVelocity: этот вектор гаснет
    /// дрэгом и складывается с обычной ходьбой, поэтому толчок ощущается как
    /// «понесло», а игрок при этом сохраняет управление. Присваивать нельзя —
    /// два источника ветра должны складываться, а не перебивать друг друга.
    /// </summary>
    public void AddPush(Vector3 velocity)
    {
        if (_climbing) EndClimb();   // сдуло с лестницы
        _externalVelocity += velocity;

        // Если толкнули вверх — гасим падение, иначе гравитация съест подъём
        // в тот же кадр и вертикальная составляющая просто пропадёт.
        if (velocity.y > 0.1f && _verticalVelocity < 0f) _verticalVelocity = 0f;
    }

    // ───────── ПОДБРОС ─────────

    /// <summary>
    /// Подбросить вверх. Зовут меха-батут — локально, у своего же игрока.
    ///
    /// Пишем в _verticalVelocity, а НЕ в _externalVelocity: второй гаснет
    /// дрэгом за доли секунды, и подброс превратился бы в короткий рывок вместо
    /// нормальной дуги. Вертикальная скорость же интегрируется гравитацией —
    /// то есть игрок честно взлетает и падает.
    ///
    /// Присваиваем, а не прибавляем: прыжок с высоты не должен складываться со
    /// скоростью падения и выстреливать в стратосферу.
    /// </summary>
    public void LaunchUp(float speed)
    {
        if (_climbing) EndClimb();
        _verticalVelocity = Mathf.Max(speed, _verticalVelocity);
    }

    /// <summary>
    /// Движение на лестнице. Гравитации нет, вместо неё — ось лестницы.
    ///
    /// W/S — ВСЕГДА вверх и вниз по оси, независимо от того, куда смотрит
    /// игрок. Привязка к взгляду («лезу туда, куда гляжу») звучит логично, но
    /// на деле означает, что стоит оглянуться на напарника — и ты поехал вниз.
    /// A/D — перехват вбок вдоль перекладин.
    ///
    /// ДВА СПОСОБА СЛЕЗТЬ, и оба нужны. Пробел — сойти где угодно, оттолкнувшись
    /// туда, куда смотришь. А наверху игрок отцепляется САМ: долез выше
    /// последней перекладины — получает толчок вперёд, на площадку. Без этого
    /// он висел бы у самого края, видел бы, куда надо шагнуть, и не мог бы.
    ///
    /// ВЫСОТА СЧИТАЕТСЯ ВДОЛЬ ОСИ, а не по мировому Y: у прислонённой лестницы
    /// это разные величины, и по Y игрок отцеплялся бы, не доехав до верха.
    /// </summary>
    private void ClimbInternal(float h, float v)
    {
        Vector3 side = Vector3.Cross(Vector3.up, _climbAxis);
        if (side.sqrMagnitude < 0.0001f) side = transform.right;
        side.Normalize();

        // Спрыгнуть. Проверку меню дублируем: сюда заходят и из ветки
        // MoveInternal(0,0) при открытой паузе, а там ввод читать нельзя.
        if (!UiModal.AnyOpen && Input.GetKeyDown(_climbJumpKey))
        {
            EndClimb();
            _externalVelocity += transform.forward * _climbPushOff;
            _verticalVelocity = 1.5f;
            return;
        }

        float along = Vector3.Dot(transform.position - _climbBase, _climbAxis);

        // Перелезть через верх.
        if (v > 0f && along > _climbLength)
        {
            EndClimb();
            _externalVelocity += transform.forward * (_climbPushOff * 0.6f);
            _verticalVelocity = 1f;
            return;
        }

        // Ниже нижней перекладины делать нечего — просто встаём на землю.
        if (v < 0f && along < 0.1f)
        {
            EndClimb();
            return;
        }

        Vector3 velocity = _climbAxis * (v * _climbSpeed)
                         + side * (h * _climbSpeed * _climbSideFactor)
                         + _externalVelocity;

        _verticalVelocity = 0f;

        // _climbCarry — уже готовое смещение за кадр, на deltaTime не умножаем.
        _controller.Move(velocity * Time.deltaTime + _climbCarry);
        _climbCarry = Vector3.zero;

        _externalVelocity = Vector3.MoveTowards(_externalVelocity, Vector3.zero,
                                                _knockbackDrag * Time.deltaTime);
    }

    // ───────── ШЛАНГ / ВЫШИБАНИЕ ─────────

    /// <summary>ПОСТОЯННАЯ реактивная тяга (полёт на струе брандспойта).
    /// Зовётся каждый FixedUpdate с сервера, пока льёт вода.</summary>
    public void ServerApplyThrust(Vector3 accel)
    {
        TargetThrust(base.Owner, accel);
    }

    [TargetRpc]
    private void TargetThrust(NetworkConnection conn, Vector3 accel)
    {
        // Струя срывает с лестницы: держаться одной рукой под напором нельзя.
        if (_climbing) EndClimb();

        // Прибавляем к внешней скорости; она же гасится дрэгом в MoveInternal().
        _externalVelocity += accel * Time.fixedDeltaTime;
        // Пока тянет — гасим падение, иначе гравитация съедает подъём.
        if (accel.y > 0.1f && _verticalVelocity < 0f) _verticalVelocity = 0f;
    }

    /// <summary>Толчок игрока струёй (вышибание при снятии кольца под напором).
    /// Зовётся на СЕРВЕРЕ — отправляем владельцу, движение считает он.</summary>
    public void ServerApplyKnockback(Vector3 velocity)
    {
        TargetKnockback(base.Owner, velocity);
    }

    [TargetRpc]
    private void TargetKnockback(NetworkConnection conn, Vector3 velocity)
    {
        if (_climbing) EndClimb();     // вышибло — значит сорвало и с лестницы
        _externalVelocity += velocity; // добавится к движению и плавно погаснет
    }

    /// <summary>Не пустить дальше длины шланга — вернуть на границу (упор как в
    /// стенку). Обычно зовётся уже у владельца (из NozzleGunItem.Update); если
    /// вызвано на сервере — переправляем владельцу.</summary>
    public void ServerClampPosition(Vector3 worldPos)
    {
        if (base.IsOwner) ApplyClamp(worldPos);
        else TargetClamp(base.Owner, worldPos);
    }

    [TargetRpc]
    private void TargetClamp(NetworkConnection conn, Vector3 worldPos) => ApplyClamp(worldPos);

    private void ApplyClamp(Vector3 worldPos)
    {
        // Двигаем CharacterController обратно к границе по горизонтали.
        Vector3 delta = worldPos - transform.position;
        delta.y = 0f;
        if (delta.sqrMagnitude > 0.0000001f)
            _controller.Move(delta);
    }
}
