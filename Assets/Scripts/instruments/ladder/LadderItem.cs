using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// СТРЕМЯНКА. Двухсекционная раскладная лестница — предмет в руке.
///
///   F   — взять / положить (база)
///   G   — бросить (база)
///   ЛКМ — сложить / разложить
///   ПКМ — воткнуть в землю: лестница выпадает из рук и стоит сама.
///         Забрать обратно — F по ней.
///
/// ═══ ЧТО ЗДЕСЬ НЕОЧЕВИДНО ═══
///
/// РАЗЛОЖЕННАЯ В РУКАХ СТОИТ ВЕРТИКАЛЬНО ПО МИРУ, а не по руке. Двухметровая
/// стремянка, наследующая наклон камеры, при взгляде вниз втыкается игроку в
/// ноги, а при взгляде вверх улетает в небо. Поэтому поза считается своя
/// (FollowHand = false, как у BalanceItem): за игроком она едет только по
/// горизонтали, а основание постоянно прижимается к земле лучом вниз.
/// Выглядит это как «несёт стремянку рядом с собой», а не «держит в кулаке».
///
/// ПО НЕЙ МОЖНО ЛАЗИТЬ ПРЯМО ИЗ ЧУЖИХ РУК. Держатель лезть не может — он бы
/// поднимал сам себя, и это разошлось бы в физике мгновенно. А вот напарник
/// может: один держит, второй лезет. Проверка одна — сравнение с _holder.
///
/// ЛАЗАНИЕ ВКЛЮЧАЕТСЯ ПО УГЛУ, а не по флагу «разложена». Круче 45° от земли —
/// лезешь; положе — просто идёшь по перекладинам ногами, обычной физикой, без
/// всякого кода. Один порог закрывает сразу три случая: стоит, прислонена,
/// лежит плашмя. Держать под них отдельные состояния незачем — угол и так
/// однозначно говорит, что делать.
///
/// ДЛИНА — ОДНО ЧИСЛО (CurrentLength), из него выводится и позиция верхней
/// секции, и размер зоны лазания. Иначе при правке высоты пришлось бы помнить
/// про второе место, и зона однажды осталась бы от сложенной лестницы.
///
/// КОЛЛАЙДЕРЫ ЖИВУТ НА ДОЧЕРНИХ СЕКЦИЯХ, И ЛЕСТНИЦА ВЕДЁТ ИХ САМА. База ищет
/// коллайдер строчкой GetComponent&lt;Collider&gt;() на корне; у составного предмета
/// с пустым корнем там null, и тогда молча отваливаются сразу три вещи:
/// материал против скольжения, гашение коллайдеров при взятии в руку и защита
/// бросившего от собственного броска. Поэтому здесь свой список всех
/// нетриггерных коллайдеров иерархии.
///
/// ГАСИМ ИХ ПО SyncVar _holder, А НЕ В ServerOnPickup. ServerOnPickup выполняется
/// только на сервере, а коллизия считается у каждого клиента своя: удалённый
/// игрок продолжал бы толкать сам себя стремянкой, которую держит. OnChange
/// приходит всем — значит и выключится у всех.
///
/// СЕТЬ: три SyncVar (сложена / воткнута / кто держит). Выдвижение секции —
/// локальная анимация у всех по флагу, как вращение барабана бетономешалки.
/// Позиция при переноске считается у владельца и расходится через
/// client-authoritative NetworkTransform.
///
/// ═══ НАСТРОЙКА ПРЕФАБА ═══
///
///   Ladder (NetworkObject, NetworkTransform Client Authoritative,
///           Rigidbody, Collider, этот скрипт)
///    ├─ LowerSection (меш + коллайдер) — не двигается
///    ├─ UpperSection (меш + коллайдер) — выезжает вверх по ЛОКАЛЬНОЙ Y
///    └─ ClimbZone    (BoxCollider, Is Trigger, скрипт LadderClimbZone)
///
/// ЛОКАЛЬНАЯ ОСЬ Y КОРНЯ ДОЛЖНА СМОТРЕТЬ ВДОЛЬ ЛЕСТНИЦЫ, вверх. Blender отдаёт
/// модели с произвольной ориентацией — если стремянка легла набок, разверни
/// меши внутри корня, а сам корень не трогай: по его Y считается и выдвижение,
/// и угол наклона, и направление лазания.
/// </summary>
public class LadderItem : InteractableItem, IClimbable
{
    [Header("Секции")]
    [Tooltip("Верхняя секция — выезжает вверх по локальной Y. Нижняя стоит на " +
             "месте и в поле не нужна.")]
    [SerializeField] private Transform _upperSection;

    [Tooltip("Длина СЛОЖЕННОЙ лестницы, метры. По ней строится зона лазания, " +
             "когда стремянка не разложена.")]
    [SerializeField] private float _foldedLength = 1.8f;

    [Tooltip("На сколько выезжает верхняя секция, метры. Разложенная длина = " +
             "сложенная + это значение.")]
    [SerializeField] private float _extendDistance = 1.6f;

    [Tooltip("Секунд на полное раскладывание")]
    [Min(0.05f)][SerializeField] private float _foldSeconds = 0.6f;

    [Header("Зона лазания")]
    [Tooltip("Триггер, внутри которого игрок цепляется за лестницу. Размер и " +
             "центр пересчитываются скриптом — руками их задавать не надо.")]
    [SerializeField] private BoxCollider _climbZone;

    [Tooltip("Ширина зоны, метры. Чуть шире самой лестницы, чтобы не надо было " +
             "попадать в неё пиксель в пиксель.")]
    [SerializeField] private float _zoneWidth = 0.9f;

    [Tooltip("Толщина зоны поперёк лестницы, метры")]
    [SerializeField] private float _zoneThickness = 0.8f;

    [Tooltip("НА СКОЛЬКО ЗОНА ТОРЧИТ ВЫШЕ ВЕРХНЕЙ ПЕРЕКЛАДИНЫ, метры.\n\n" +
             "Без запаса игрок, долезший до верха, вываливается из триггера " +
             "ровно там, где ещё не на что встать, и падает обратно. Запас даёт " +
             "ему долезть выше края и уже оттуда шагнуть на площадку.")]
    [SerializeField] private float _topOverhang = 0.6f;

    [Tooltip("КРУЧЕ СКОЛЬКИХ ГРАДУСОВ ОТ ЗЕМЛИ НАЧИНАЕТСЯ ЛАЗАНИЕ.\n\n" +
             "Положе этого лестница считается пологой: по ней просто идут " +
             "ногами по перекладинам, обычной физикой. 45 — золотая середина, " +
             "и она же честно ощущается: пологую хочется пройти, крутую — " +
             "полезть.")]
    [Range(10f, 85f)][SerializeField] private float _climbAboveDegrees = 45f;

    [Header("В руках (разложенная)")]
    [Tooltip("На сколько метров вперёд от игрока едет разложенная лестница")]
    [SerializeField] private float _carryForward = 0.8f;

    [Tooltip("Сдвиг вбок, метры. Плюс — вправо. Чтобы стремянка не загораживала " +
             "весь экран.")]
    [SerializeField] private float _carrySide = 0.55f;

    [Tooltip("Разворачивать лестницу вслед за игроком. Выкл — она сохраняет " +
             "тот поворот, в котором её взяли, и игрок носит её боком.")]
    [SerializeField] private bool _yawFollowsPlayer = true;

    [Tooltip("Множитель скорости, пока несёшь РАЗЛОЖЕННУЮ. Сложенная " +
             "переносится без штрафа.")]
    [Range(0.2f, 1f)][SerializeField] private float _carrySpeedUnfolded = 0.6f;

    [Tooltip("Слои земли — по ним основание прижимается к поверхности и по ним " +
             "же ищется точка втыкания. Убери отсюда слой самой лестницы.")]
    [SerializeField] private LayerMask _groundMask = ~0;

    [Header("Втыкание (ПКМ)")]
    [Tooltip("На сколько метров вперёд от игрока втыкается лестница")]
    [SerializeField] private float _plantForward = 1.2f;

    [Tooltip("Насколько глубоко искать землю от уровня пояса игрока, метры")]
    [SerializeField] private float _plantProbe = 4f;

    [Header("Отладка")]
    [SerializeField] private bool _debugGizmo = true;

    // ───────── сеть ─────────
    private readonly SyncVar<bool> _unfolded = new(false);
    private readonly SyncVar<bool> _planted = new(false);

    /// <summary>Кто держит (объект игрока) или null. Нужен ровно за тем, чтобы
    /// держатель не полез по лестнице, которую сам же и несёт.</summary>
    private readonly SyncVar<NetworkObject> _holder = new(null);

    // ───────── локальное ─────────
    private float _extend01;          // 0 сложена, 1 разложена
    private Vector3 _upperHome;       // где верхняя секция стоит сложенной
    private readonly RaycastHit[] _probe = new RaycastHit[8];

    // Все нетриггерные коллайдеры иерархии — секции лестницы. Зона лазания
    // сюда не попадает: она триггер, её гасить нельзя, иначе лезть будет не за что.
    private Collider[] _solid;
    private static PhysicsMaterial _ladderGrip;

    public bool IsUnfolded => _unfolded.Value;
    public bool IsPlanted => _planted.Value;
    public NetworkObject Holder => _holder.Value;

    /// <summary>Текущая длина лестницы, метры. Единственный источник размера —
    /// и для выдвижения секции, и для зоны лазания.</summary>
    public float CurrentLength => _foldedLength + _extend01 * _extendDistance;

    /// <summary>Ось лестницы в мире: куда лезть.</summary>
    public Vector3 ClimbAxis => transform.up;

    /// <summary>Точка у нижней перекладины — от неё считается высота подъёма.</summary>
    public Vector3 ClimbBase => transform.position;

    /// <summary>Наклон к горизонту: 90 — стоит вертикально, 0 — лежит.</summary>
    public float AngleFromGround => 90f - Vector3.Angle(transform.up, Vector3.up);

    // ───────────────────────── ЖИЗНЕННЫЙ ЦИКЛ ─────────────────────────

    protected override void Awake()
    {
        base.Awake();

        if (_upperSection != null) _upperHome = _upperSection.localPosition;

        CacheSolidColliders();

        if (_climbZone == null)
            Debug.LogWarning($"[Лестница] «{name}»: не назначен Climb Zone — " +
                             "лазить будет не за что.", this);
        else if (!_climbZone.isTrigger)
            Debug.LogWarning($"[Лестница] «{name}»: на Climb Zone не стоит " +
                             "Is Trigger. Игрок будет об неё спотыкаться, а " +
                             "не цепляться.", this);

        ApplyExtension();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _planted.OnChange += OnPlantedChanged;
        _holder.OnChange += OnHolderChanged;

        // Догоняем состояние без анимации: подключившийся позже игрок должен
        // увидеть лестницу уже разложенной, а не раскладывающейся заново.
        _extend01 = _unfolded.Value ? 1f : 0f;
        ApplyExtension();
        ApplyPlantedPhysics(_planted.Value);
        SetSolid(_holder.Value == null);
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _planted.OnChange -= OnPlantedChanged;
        _holder.OnChange -= OnHolderChanged;
    }

    private void Update()
    {
        float target = _unfolded.Value ? 1f : 0f;
        if (Mathf.Approximately(_extend01, target)) return;

        _extend01 = Mathf.MoveTowards(_extend01, target, Time.deltaTime / _foldSeconds);
        ApplyExtension();
    }

    // ───────────────────────── ГЕОМЕТРИЯ ─────────────────────────

    /// <summary>Выдвинуть секцию и подогнать зону лазания. Считается из одного
    /// _extend01, поэтому разъехаться они не могут.</summary>
    private void ApplyExtension()
    {
        if (_upperSection != null)
            _upperSection.localPosition = _upperHome + Vector3.up * (_extend01 * _extendDistance);

        if (_climbZone == null) return;

        float h = CurrentLength + _topOverhang;
        _climbZone.size = new Vector3(_zoneWidth, h, _zoneThickness);
        _climbZone.center = new Vector3(0f, h * 0.5f, 0f);
    }

    // ───────────────────────── ЛАЗАНИЕ ─────────────────────────

    /// <summary>
    /// Можно ли этому игроку лезть по лестнице прямо сейчас.
    ///
    /// Три отказа, и каждый по своей причине:
    ///   свой держатель — поднимал бы сам себя;
    ///   в руках и сложена — цепляться не за что, это ещё палка, а не лестница;
    ///   положе порога — по такой ходят ногами, лазание только мешало бы.
    /// </summary>
    public bool CanClimb(NetworkObject player)
    {
        if (player != null && _holder.Value == player) return false;
        if (_holder.Value != null && !_unfolded.Value) return false;
        return AngleFromGround > _climbAboveDegrees;
    }

    /// <summary>Кто-нибудь висит на лестнице прямо сейчас. Пока висит, её
    /// нельзя ни складывать, ни выдёргивать из земли — иначе человек уедет
    /// вместе с ней или провалится.</summary>
    public bool AnyoneClimbing { get; set; }

    // ───────────────────────── ЛКМ: СЛОЖИТЬ / РАЗЛОЖИТЬ ─────────────────────────

    public override void ServerPrimaryUse(UseContext ctx)
    {
        if (ctx.Hand == null || ctx.Hand.HeldItem != this) return;

        if (AnyoneClimbing)
        {
            Debug.Log($"[Лестница] «{name}»: на ней кто-то висит — не складываю", this);
            return;
        }

        _unfolded.Value = !_unfolded.Value;
    }

    // ───────────────────────── ПКМ: ВОТКНУТЬ ─────────────────────────

    /// <summary>
    /// Воткнуть в землю перед игроком.
    ///
    /// Точку считаем ОТ ИГРОКА, а не от текущей позиции лестницы. Пока она в
    /// руках, её двигает клиент-владелец через client-authoritative
    /// NetworkTransform, и на сервере позиция отстаёт на пинг — воткнулась бы
    /// не там, куда целились. Позиция игрока серверу известна точно.
    /// </summary>
    public override void ServerSecondaryUse(UseContext ctx)
    {
        if (ctx.Hand == null || ctx.Hand.HeldItem != this) return;

        Vector3 fwd = ctx.Direction;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = ctx.Hand.transform.forward;
        fwd.Normalize();

        Vector3 spot = ctx.Hand.transform.position + fwd * _plantForward;

        if (!FindGround(spot + Vector3.up * 1.2f, _plantProbe, out float y))
        {
            Debug.Log($"[Лестница] «{name}»: под ногами нет опоры — некуда втыкать", this);
            return;
        }
        spot.y = y;

        // ПОРЯДОК ВАЖЕН. Сначала выпускаем из рук — ServerOnDrop вернёт физику
        // и снимет владение, — и только потом ставим флаг: он делает тело
        // кинематическим. Наоборот лестница успела бы моргнуть падением.
        ctx.Hand.ServerDrop(Vector3.zero);

        transform.SetPositionAndRotation(spot, Quaternion.LookRotation(fwd, Vector3.up));
        _planted.Value = true;

        Debug.Log($"[Лестница] «{name}» воткнута", this);
    }

    // ───────────────────────── F: ВЗЯТЬ / ВЫДЕРНУТЬ ─────────────────────────

    public override void ServerInteract(PlayerInteractor interactor)
    {
        if (_planted.Value)
        {
            if (AnyoneClimbing)
            {
                Debug.Log($"[Лестница] «{name}»: на ней кто-то стоит — не выдёргиваю", this);
                return;
            }
            _planted.Value = false;
        }

        base.ServerInteract(interactor);
    }

    public override void ServerOnPickup(PlayerHand hand)
    {
        base.ServerOnPickup(hand);
        _holder.Value = hand.NetworkObject;
    }

    public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
    {
        base.ServerOnDrop(hand, tossDirection, force);
        _holder.Value = null;
    }

    // ───────────────────────── ФИЗИКА ВОТКНУТОЙ ─────────────────────────

    private void OnPlantedChanged(bool prev, bool next, bool asServer)
    {
        // У ХОСТА OnChange приходит дважды: asServer = true, потом false.
        if (asServer && base.IsClientInitialized) return;
        ApplyPlantedPhysics(next);
    }

    /// <summary>Воткнутая не падает и не сдвигается: по ней лезут, и любое
    /// шевеление тела под человеком читалось бы как баг.</summary>
    private void ApplyPlantedPhysics(bool planted)
    {
        if (_rb == null) return;

        if (planted)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
        _rb.isKinematic = planted;
        _rb.useGravity = !planted;
    }

    // ───────────────────────── СОСТАВНОЕ ТЕЛО ─────────────────────────

    /// <summary>
    /// Собрать все нетриггерные коллайдеры иерархии и дать им сцепление с
    /// землёй. Базовый ApplyAntiSlideMaterial работает только с коллайдером на
    /// корне, а у лестницы корень пустой — без этого брошенная стремянка
    /// катается по площадке, как по льду.
    /// </summary>
    private void CacheSolidColliders()
    {
        Collider[] all = GetComponentsInChildren<Collider>(true);
        List<Collider> solid = new List<Collider>(all.Length);

        foreach (Collider c in all)
        {
            if (c == null || c.isTrigger) continue;
            solid.Add(c);
        }
        _solid = solid.ToArray();

        if (_solid.Length == 0)
        {
            Debug.LogWarning($"[Лестница] «{name}»: ни одного обычного коллайдера " +
                             "в иерархии. Секции надо снабдить коллайдерами, " +
                             "иначе стремянка провалится сквозь землю.", this);
            return;
        }

        if (_ladderGrip == null)
        {
            _ladderGrip = new PhysicsMaterial("LadderGrip")
            {
                dynamicFriction = 1f,
                staticFriction = 1f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }

        // Ручную настройку не перебиваем: если материал уже назначен в
        // инспекторе, значит его туда положили осознанно.
        foreach (Collider c in _solid)
            if (c.sharedMaterial == null) c.sharedMaterial = _ladderGrip;
    }

    private void SetSolid(bool on)
    {
        if (_solid == null) return;
        foreach (Collider c in _solid)
            if (c != null) c.enabled = on;
    }

    /// <summary>Взяли или отпустили. В руке лестница нематериальна — иначе она
    /// упирается в собственного носильщика и выталкивает его из-под себя.</summary>
    private void OnHolderChanged(NetworkObject prev, NetworkObject next, bool asServer)
    {
        // У ХОСТА OnChange приходит дважды: asServer = true, потом false.
        if (asServer && base.IsClientInitialized) return;

        SetSolid(next == null);

        // Отпустили — на пару кадров не сталкиваемся с тем, кто отпустил.
        // База делает это только для коллайдера на корне, которого у нас нет.
        if (next == null && prev != null) StartCoroutine(IgnoreDropperRoutine(prev));
    }

    private IEnumerator IgnoreDropperRoutine(NetworkObject dropper)
    {
        if (dropper == null || _solid == null) yield break;

        Collider body = dropper.GetComponent<CharacterController>();
        if (body == null) body = dropper.GetComponentInChildren<Collider>();
        if (body == null) yield break;

        foreach (Collider c in _solid)
            if (c != null) Physics.IgnoreCollision(c, body, true);

        yield return new WaitForSeconds(0.4f);

        foreach (Collider c in _solid)
            if (c != null && body != null) Physics.IgnoreCollision(c, body, false);
    }

    // ───────────────────────── ПОЗА В РУКЕ ─────────────────────────

    public override bool FollowHand => !_unfolded.Value;

    public override float CarrySpeedMultiplier => _unfolded.Value ? _carrySpeedUnfolded : 1f;

    public override void UpdateHeldPose(Transform handAnchor)
    {
        // Штраф скорости обновляем ЗДЕСЬ, а не полагаемся на PlayerHand: тот
        // читает CarrySpeedMultiplier только в момент смены предмета в руке, а
        // у лестницы множитель меняется от ЛКМ, когда предмет уже взят.
        FirstPersonController fpc = handAnchor.GetComponentInParent<FirstPersonController>();
        if (fpc != null) fpc.SpeedMultiplier = CarrySpeedMultiplier;

        if (!_unfolded.Value)
        {
            transform.SetPositionAndRotation(handAnchor.position, handAnchor.rotation);
            return;
        }

        // ── разложенная: вертикально по миру, за игроком только по горизонтали ──

        Vector3 fwd = handAnchor.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = transform.forward;
        fwd.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 pos = handAnchor.position + fwd * _carryForward + right * _carrySide;

        // Основание прижимаем к земле. Без этого лестница висит на высоте
        // кисти, и напарник лезет по воздуху.
        pos.y = FindGround(pos + Vector3.up * 1.5f, 5f, out float y)
            ? y
            : handAnchor.position.y - 1.4f;

        Quaternion rot = _yawFollowsPlayer
            ? Quaternion.LookRotation(fwd, Vector3.up)
            : Quaternion.LookRotation(Flatten(transform.forward), Vector3.up);

        transform.SetPositionAndRotation(pos, rot);
    }

    private static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 0.0001f ? Vector3.forward : v.normalized;
    }

    /// <summary>
    /// Земля под точкой. Свои коллайдеры пропускаем: у разложенной лестницы
    /// нижняя секция висит ровно под лучом, и без фильтра основание
    /// «приземлялось» бы на само себя, зависая на высоте собственной толщины.
    /// </summary>
    private bool FindGround(Vector3 from, float distance, out float y)
    {
        y = 0f;
        int n = Physics.RaycastNonAlloc(from, Vector3.down, _probe, distance,
                                        _groundMask, QueryTriggerInteraction.Ignore);

        float best = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            if (_probe[i].collider == null) continue;
            if (_probe[i].collider.transform.IsChildOf(transform)) continue;
            if (_probe[i].point.y > best) best = _probe[i].point.y;
        }

        if (float.IsNegativeInfinity(best)) return false;
        y = best;
        return true;
    }

    // ───────────────────────── ОТЛАДКА ─────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (!_debugGizmo) return;

        float len = Application.isPlaying ? CurrentLength : _foldedLength + _extendDistance;

        Gizmos.color = AngleFromGround > _climbAboveDegrees
            ? new Color(0.4f, 1f, 0.5f, 0.9f)
            : new Color(1f, 0.7f, 0.3f, 0.9f);

        Gizmos.DrawLine(transform.position, transform.position + transform.up * len);
        Gizmos.DrawWireSphere(transform.position + transform.up * len, 0.08f);
    }
}
