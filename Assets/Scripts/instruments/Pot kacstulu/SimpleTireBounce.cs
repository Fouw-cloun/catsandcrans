using UnityEngine;

/// <summary>
/// ПРОСТОЙ ОТСКОК НА ШИНЕ. Минимальная версия для отладки: только подброс и
/// сплющивание. Ни сети, ни ветра, ни эффектов.
///
/// ═══ ГЛАВНОЕ ОТЛИЧИЕ ОТ BellowsTire ═══
///
/// ЭТОТ СКРИПТ ВЕШАЕТСЯ ПРЯМО НА ОБЪЕКТ С ТРИГГЕРОМ, а не на родителя.
///
/// Unity доставляет OnTriggerEnter только на тот GameObject, где висит сам
/// коллайдер (и на объект его Rigidbody). Вверх по иерархии событие не
/// всплывает. Все прошлые попытки развязать это ретрансляторами добавляли ещё
/// одно место, где может не сработать. Здесь мест нет вообще: скрипт и триггер
/// на одном объекте, и промахнуться негде.
///
/// ═══ ОН ГРОМКО РУГАЕТСЯ ═══
///
/// Каждая проверка, которая может провалиться, пишет в консоль ПРИЧИНУ. Молчит
/// он только когда всё в порядке. Если после установки в консоли пусто и при
/// этом ничего не происходит — значит скрипт вообще не запустился, и проблема
/// в компиляции, а не в настройке.
///
/// ═══ КУДА ВЕШАТЬ ═══
///
///   TireRoot
///    ├─ TireMesh        визуал (ссылка в поле Tire Mesh)
///    ├─ TireCollider    обычный коллайдер, Is Trigger СНЯТ
///    └─ JumpZone        BoxCollider, Is Trigger ✓  ◄── ЭТОТ СКРИПТ СЮДА
/// </summary>
[RequireComponent(typeof(Collider))]
public class SimpleTireBounce : MonoBehaviour
{
    [Header("Ссылки")]
    [Tooltip("Меш шины — он сплющивается. Пусто — сплющивания не будет, " +
             "но подброс всё равно работает.")]
    [SerializeField] private Transform _tireMesh;

    [Header("Отскок")]
    [Tooltip("Минимальный подброс, м/с. Работает даже когда просто зашёл " +
             "на шину пешком.")]
    [SerializeField] private float _minBounce = 7f;

    [Tooltip("Во сколько раз отскок сильнее удара. Больше 1 — раскачка.")]
    [Range(0.5f, 2.5f)][SerializeField] private float _restitution = 1.45f;

    [Tooltip("Потолок отскока, м/с")]
    [SerializeField] private float _maxBounce = 18f;

    [Tooltip("ВО СКОЛЬКО РАЗ СИЛЬНЕЕ ПЕРВЫЙ ПРЫЖОК.\n\n" +
             "Шина накачана туго, и первое касание должно отбрасывать резко — " +
             "«наступил и улетел». Дальнейшие отскоки в серии идут обычной " +
             "силой, иначе раскачка мгновенно уходит в потолок и теряет смысл.")]
    [Range(1f, 3f)][SerializeField] private float _firstBounceBoost = 1.6f;

    [Tooltip("Через сколько секунд без касаний прыжок снова считается ПЕРВЫМ. " +
             "Больше паузы между отскоками в серии, меньше времени на обход " +
             "шины кругом.")]
    [Min(0.1f)][SerializeField] private float _freshGap = 0.9f;

    [Tooltip("Пауза между срабатываниями, сек. Защита от двойного " +
             "срабатывания на двух коллайдерах игрока.")]
    [SerializeField] private float _cooldown = 0.15f;

    [Header("Боковой удар")]
    [Tooltip("Отбрасывать при налёте сбоку, а не только сверху")]
    [SerializeField] private bool _sideBounce = true;

    [Tooltip("Сила бокового отлёта, м/с. Отталкивает ПО РАДИУСУ от центра " +
             "шины — влетел бочком, отлетел в ту же сторону, откуда пришёл.")]
    [SerializeField] private float _sidePush = 9f;

    [Tooltip("Сколько вертикали добавляется к боковому отлёту, доля от силы. " +
             "Совсем без подъёма игрок скребёт по земле и выглядит не " +
             "отброшенным, а просто оттолкнутым.")]
    [Range(0f, 1f)][SerializeField] private float _sideUpward = 0.55f;

    [Tooltip("МИНИМАЛЬНАЯ горизонтальная скорость для бокового удара, м/с.\n\n" +
             "Без порога шина отбрасывала бы любого, кто просто медленно " +
             "прислонился к ней, — а это раздражает.")]
    [SerializeField] private float _sideMinSpeed = 1.5f;

    [Header("ВЕТЕР")]
    [Tooltip("Выдувать воздух при отскоке")]
    [SerializeField] private bool _windEnabled = true;

    [Tooltip("Откуда дует. Пусто — от этого объекта.")]
    [SerializeField] private Transform _windOrigin;

    [Tooltip("НАПРАВЛЕНИЕ ВЕТРА в локальных координатах точки выдува.\n\n" +
             "(0,1,0) — вверх, (0,0,1) — вперёд по синей стрелке, " +
             "(1,0,0) — вправо.\n\n" +
             "Проверяй голубым гизмо: выдели объект и смотри, куда смотрит " +
             "конус.")]
    [SerializeField] private Vector3 _windAxis = Vector3.up;

    [Tooltip("Дальность ветра, метры")]
    [Min(0.5f)][SerializeField] private float _windRange = 9f;

    [Tooltip("ПОЛОВИНА угла раствора. 30 даёт конус в 60 градусов.")]
    [Range(5f, 89f)][SerializeField] private float _windHalfAngle = 30f;

    [Tooltip("Что может сдуваться. Убери отсюда слой самой шины.")]
    [SerializeField] private LayerMask _windMask = ~0;

    [Header("Сила ветра")]
    [Tooltip("Импульс предметам. Дрова должно заметно расшвыривать.")]
    [SerializeField] private float _itemPush = 7f;

    [Tooltip("Толчок ДРУГИМ игрокам, м/с. Прыгуна не толкает — его и так " +
             "подбросило отскоком.")]
    [SerializeField] private float _playerPush = 7f;

    [Tooltip("Сколько тяги получает костёр в конусе, 0..1")]
    [Range(0f, 1f)][SerializeField] private float _fireAir = 1f;

    [Tooltip("Насколько ветер поддувает вверх, доля от силы")]
    [Range(0f, 1f)][SerializeField] private float _windUpward = 0.35f;

    [Tooltip("Частицы ветра (WindParticles). Необязательно.")]
    [SerializeField] private WindParticles _windParticles;

    [Header("Сплющивание")]
    [Range(0f, 0.7f)][SerializeField] private float _squash = 0.4f;
    [Min(1f)][SerializeField] private float _stiffness = 90f;
    [Range(0.05f, 2f)][SerializeField] private float _damping = 0.35f;
    [Range(0f, 1.5f)][SerializeField] private float _bulge = 0.8f;

    [Header("Отладка")]
    [Tooltip("Писать в консоль каждое касание триггера")]
    [SerializeField] private bool _logEverything = true;

    [Tooltip("Показывать счётчик прямо на экране")]
    [SerializeField] private bool _showCounter = true;

    private Vector3 _tireHome;
    private float _spring, _springVel, _lastHit;

    /// <summary>
    /// Состояние одного игрока в зоне.
    ///
    /// Горизонтальную скорость считаем САМИ по смещению между кадрами:
    /// FirstPersonController наружу отдаёт только вертикальную, а для бокового
    /// удара нужна именно горизонтальная. Трогать ради этого контроллер не
    /// хочется — данные и так выводятся из позиции.
    /// </summary>
    private class Tracked
    {
        public FirstPersonController Fpc;
        public Vector3 LastPos;
        public Vector3 Velocity;
        public float LastBounceTime = -99f;
    }

    private readonly System.Collections.Generic.List<Tracked> _inside
        = new System.Collections.Generic.List<Tracked>();

    // Буферы ветра. Заводим один раз: OverlapSphere в каждом залпе иначе
    // выделял бы массив заново и мусорил сборщику на ровном месте.
    private readonly Collider[] _windHits = new Collider[64];
    private readonly System.Collections.Generic.HashSet<Rigidbody> _blown
        = new System.Collections.Generic.HashSet<Rigidbody>();

    private int _windBlasts, _windItems, _windFires;

    // ───────── чтение снаружи (для сводного оверлея) ─────────

    /// <summary>Насколько сейчас продавлена шина, 0..1.</summary>
    public float Squash01 => Mathf.Clamp01(_spring);

    /// <summary>Сколько игроков сейчас в зоне.</summary>
    public int InsideCount => _inside.Count;

    /// <summary>Сколько раз вообще что-то входило в триггер. Ноль при попытках
    /// прыгать — значит зона не ловит, и дело в её размере или в слоях.</summary>
    public int EnterCount => _enterCount;

    public int BounceCount => _bounceCount;
    public int WindBlasts => _windBlasts;
    public int WindItems => _windItems;
    public int WindFires => _windFires;

    /// <summary>Что произошло в последний раз — с причиной, если не сработало.</summary>
    public string LastReason => _lastReason;

    private int _enterCount;      // сколько раз ВООБЩЕ что-то вошло в триггер
    private int _bounceCount;     // сколько раз реально подбросило
    private string _lastReason = "ещё ничего не было";

    private void Start()
    {
        if (_tireMesh != null) _tireHome = _tireMesh.localScale;

        // Проверка сборки. Всё это молчаливые отказы — без сообщения их можно
        // искать часами.
        Collider col = GetComponent<Collider>();

        if (!col.isTrigger)
        {
            Debug.LogError($"[Шина] «{name}»: КОЛЛАЙДЕР НЕ ТРИГГЕР. Ставлю " +
                           "Is Trigger сам, но лучше поставь галку в " +
                           "инспекторе.", this);
            col.isTrigger = true;
        }

        if (_tireMesh == null)
            Debug.LogWarning($"[Шина] «{name}»: не назначен Tire Mesh — " +
                             "сплющивания не будет. Подброс работает.", this);

        // Твёрдая опора ищется у РОДИТЕЛЯ, потому что этот объект — триггер.
        bool solid = false;
        Transform root = transform.parent != null ? transform.parent : transform;

        foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
        {
            if (c == null || c.isTrigger) continue;
            solid = true;
            break;
        }

        if (!solid)
            Debug.LogError($"[Шина] «{name}»: рядом НЕТ ТВЁРДОГО КОЛЛАЙДЕРА. " +
                           "Игрок будет проваливаться сквозь шину и никогда на " +
                           "неё не приземлится. Добавь объект с обычным " +
                           "коллайдером по габаритам шины.", this);

        Debug.Log($"[Шина] «{name}» запущена. Триггер {col.GetType().Name}, " +
                  $"размер {col.bounds.size:0.00}, твёрдая опора рядом: {solid}", this);
    }

    private void Update()
    {
        TickBounce();

        float dt = Mathf.Min(Time.deltaTime, 0.05f);

        // Затухающий осциллятор: недодемпфированная пружина проскакивает покой
        // и качается — это и читается как упругость резины, в отличие от Lerp.
        float accel = -_stiffness * _spring
                      - 2f * _damping * Mathf.Sqrt(_stiffness) * _springVel;
        _springVel += accel * dt;
        _spring = Mathf.Clamp(_spring + _springVel * dt, -0.25f, 1.2f);

        if (_tireMesh == null) return;

        // Сохранение объёма: сплющиваясь, резина раздаётся вширь.
        float sy = 1f - _spring * _squash;
        float sxz = 1f + _spring * _squash * _bulge;
        _tireMesh.localScale = new Vector3(_tireHome.x * sxz,
                                           _tireHome.y * sy,
                                           _tireHome.z * sxz);
    }

    private void OnTriggerEnter(Collider other)
    {
        _enterCount++;

        if (_logEverything)
            Debug.Log($"[Шина] «{name}»: вошёл «{other.name}» " +
                      $"(слой {LayerMask.LayerToName(other.gameObject.layer)})", this);

        if (other == null) return;

        FirstPersonController fpc = other.GetComponentInParent<FirstPersonController>();
        if (fpc == null)
        {
            _lastReason = $"«{other.name}» — не игрок";
            return;
        }

        if (Find(fpc) != null) return;

        // Стартовую позицию запоминаем сразу: иначе в первый же кадр скорость
        // посчиталась бы от нуля и вышла бы гигантской, а игрока выстрелило бы
        // боковым ударом на ровном месте.
        _inside.Add(new Tracked { Fpc = fpc, LastPos = fpc.transform.position });
    }

    private void OnTriggerExit(Collider other)
    {
        if (other == null) return;

        FirstPersonController fpc = other.GetComponentInParent<FirstPersonController>();
        if (fpc == null) return;

        Tracked t = Find(fpc);
        if (t != null) _inside.Remove(t);
    }

    private Tracked Find(FirstPersonController fpc)
    {
        for (int i = 0; i < _inside.Count; i++)
            if (_inside[i].Fpc == fpc) return _inside[i];
        return null;
    }

    /// <summary>
    /// РЕШЕНИЕ ПРИНИМАЕТСЯ КАЖДЫЙ КАДР, А НЕ В МОМЕНТ ВХОДА В ТРИГГЕР.
    ///
    /// Раньше отскок висел на OnTriggerEnter, и это давало ровно ту беду, из-за
    /// которой прыжок работал только по краю шины: стоя в середине, игрок
    /// оказывался ЦЕЛИКОМ ВЫШЕ зоны и события входа просто не было. У края
    /// капсула сползала ниже, задевала зону — и там работало.
    ///
    /// Точную подгонку высоты триггера это лечит лишь до следующей правки
    /// модели. Поэтому теперь важен не момент пересечения границы, а два
    /// состояния: игрок В ЗОНЕ и игрок ПАДАЕТ. Зону можно делать заведомо
    /// большой, и куда именно он приземлился, значения больше не имеет.
    ///
    /// Заодно получается настоящий батут: пока стоишь на шине, она продолжает
    /// подбрасывать, а не ждёт, когда ты выйдешь и войдёшь заново.
    /// </summary>
    private void TickBounce()
    {
        // Подчищаем исчезнувших: игрок мог отключиться прямо в зоне, и
        // OnTriggerExit тогда не придёт.
        for (int i = _inside.Count - 1; i >= 0; i--)
            if (_inside[i].Fpc == null) _inside.RemoveAt(i);

        if (_inside.Count == 0) return;

        Collider zone = GetComponent<Collider>();
        float dt = Mathf.Max(0.0001f, Time.deltaTime);

        // Верх твёрдой части — граница между «сверху» и «сбоку».
        float solidTop = SolidTopY();

        for (int i = 0; i < _inside.Count; i++)
        {
            Tracked t = _inside[i];
            FirstPersonController fpc = t.Fpc;

            // Скорость считаем ВСЕГДА, даже когда отскок на кулдауне: иначе в
            // момент срабатывания она окажется от старой позиции и боковой
            // удар выйдет случайной силы.
            Vector3 pos = fpc.transform.position;
            t.Velocity = (pos - t.LastPos) / dt;
            t.LastPos = pos;

            if (!fpc.IsOwner) continue;
            if (Time.time - _lastHit < _cooldown) continue;

            // Подстраховка от «залипшего» игрока: если OnTriggerExit не пришёл,
            // проверяем принадлежность зоне сами.
            if (zone != null &&
                !zone.bounds.Contains(new Vector3(pos.x, zone.bounds.center.y, pos.z)))
                continue;

            // ПЕРВЫЙ ПРЫЖОК СИЛЬНЕЕ. Шина накачана туго, и первое касание
            // должно отбрасывать резко. Дальнейшие отскоки в серии идут обычной
            // силой — иначе раскачка мгновенно упирается в потолок и теряет
            // смысл.
            bool fresh = Time.time - t.LastBounceTime > _freshGap;
            float boost = fresh ? _firstBounceBoost : 1f;

            // СВЕРХУ ИЛИ СБОКУ. Ноги выше верха шины — приземление; ниже —
            // влетел в бок. Разделение по высоте, а не по направлению скорости:
            // спрыгнув сверху вплотную к краю, игрок движется и вниз, и вбок,
            // и по одной скорости эти случаи не различить.
            bool fromTop = pos.y >= solidTop - 0.15f;

            if (fromTop)
            {
                // Пока летит вверх после предыдущего отскока — не трогаем,
                // иначе получал бы добавку на подъёме и ушёл бы в стратосферу.
                if (fpc.VerticalVelocity > 0.5f) continue;

                float impact = Mathf.Max(0f, -fpc.VerticalVelocity);
                float bounce = Mathf.Clamp(impact * _restitution * boost,
                                           _minBounce * boost, _maxBounce);

                fpc.LaunchUp(bounce);
                Kick(impact);

                // Сила залпа берётся от отскока, а не от удара: удар при
                // спокойном приземлении почти нулевой, а шина при этом всё
                // равно резко распрямляется и воздух выдавливает.
                Blast(Mathf.Clamp01(bounce / _maxBounce));

                _lastReason = $"верх: подброс {bounce:0.0} м/с " +
                              $"(удар {impact:0.0}{(fresh ? ", ПЕРВЫЙ" : "")})";
            }
            else
            {
                if (!_sideBounce) continue;

                // Отталкиваем ПО РАДИУСУ от оси шины: влетел бочком — отлетел
                // туда, откуда пришёл. Направление берём от геометрии, а не от
                // скорости игрока, иначе скользящий удар вдоль борта швырял бы
                // его вперёд, а не в сторону.
                Vector3 center = zone != null ? zone.bounds.center : transform.position;
                Vector3 radial = pos - center;
                radial.y = 0f;

                if (radial.sqrMagnitude < 0.0001f) continue;
                radial.Normalize();

                // Учитываем только скорость В СТОРОНУ шины. Уходящего от неё
                // игрока догонять и подталкивать в спину незачем.
                Vector3 flat = new Vector3(t.Velocity.x, 0f, t.Velocity.z);
                float closing = Mathf.Max(0f, -Vector3.Dot(flat, radial));

                if (closing < _sideMinSpeed) continue;

                float power = Mathf.Clamp01(closing / 8f) * boost;
                Vector3 push = (radial + Vector3.up * _sideUpward).normalized
                               * (_sidePush * Mathf.Max(0.45f, power));

                fpc.AddPush(push);
                Kick(closing * 0.6f);

                // Боковой удар мнёт шину слабее прямого приземления, значит и
                // воздуха выдавливает меньше.
                Blast(Mathf.Clamp01(power) * 0.6f);

                _lastReason = $"бок: отлёт {push.magnitude:0.0} м/с " +
                              $"(налёт {closing:0.0}{(fresh ? ", ПЕРВЫЙ" : "")})";
            }

            t.LastBounceTime = Time.time;
            _lastHit = Time.time;
            _bounceCount++;

            if (_logEverything) Debug.Log($"[Шина] «{name}»: {_lastReason}", this);
            break;
        }
    }

    /// <summary>Ударить по пружине. Скоростью, а не выставленным положением:
    /// резкий удар даёт настоящий провал с отскоком, а присвоенное положение
    /// выглядело бы подменой кадра.</summary>
    private void Kick(float impact)
        => _springVel += 9f * Mathf.Clamp01(impact / 10f + 0.35f);

    // ───────────────────────── ВЕТЕР ─────────────────────────

    private Transform Nozzle => _windOrigin != null ? _windOrigin : transform;

    /// <summary>Откуда дует ветер (для TireAimer — навести на костёр).</summary>
    public Transform WindOrigin => Nozzle;
    /// <summary>Есть ли у шины отдельная точка выдува (дочерний объект), а не сама шина.</summary>
    public bool HasSeparateWindOrigin => _windOrigin != null && _windOrigin != transform;
    /// <summary>Ось ветра в осях точки выдува.</summary>
    public Vector3 WindAxisLocal => _windAxis.sqrMagnitude < 0.0001f ? Vector3.up : _windAxis.normalized;

    /// <summary>Направление ветра в мире. Ось задана локально, потому что так
    /// её понятно крутить в инспекторе; в мир переводим здесь.</summary>
    private Vector3 WindDir
    {
        get
        {
            Vector3 a = _windAxis.sqrMagnitude < 0.0001f ? Vector3.up : _windAxis.normalized;
            return Nozzle.TransformDirection(a).normalized;
        }
    }

    /// <summary>
    /// Залп воздуха. Сила 0..1 — от неё зависит всё сразу.
    ///
    /// Считается ЛОКАЛЬНО, без ServerRpc. Для отладки этого достаточно, а на
    /// хосте (сервер и клиент в одном процессе) работает и по-настоящему.
    /// Для выделенного сервера толчок предметов надо будет унести на сервер —
    /// но сначала пусть заработает вообще.
    /// </summary>
    private void Blast(float strength01)
    {
        if (!_windEnabled) return;

        float s = Mathf.Clamp01(strength01);
        if (s <= 0.01f) return;

        Vector3 origin = Nozzle.position;
        Vector3 dir = WindDir;

        if (_windParticles != null) _windParticles.Blast(s);

        BlowItems(s, origin, dir);
        BlowPlayers(s, origin, dir);
        FanFires(s, origin, dir);

        _windBlasts++;
    }

    private void BlowItems(float s, Vector3 origin, Vector3 dir)
    {
        int n = Physics.OverlapSphereNonAlloc(origin, _windRange, _windHits,
                                              _windMask, QueryTriggerInteraction.Ignore);

        // Одно тело обычно несёт несколько коллайдеров — без этой отсечки
        // полено получило бы импульс столько раз, сколько у него коллайдеров.
        _blown.Clear();

        for (int i = 0; i < n; i++)
        {
            Collider c = _windHits[i];
            if (c == null) continue;

            Rigidbody rb = c.attachedRigidbody;
            if (rb == null || rb.isKinematic) continue;
            if (rb.transform.IsChildOf(transform.root)) continue;   // не себя
            if (_blown.Contains(rb)) continue;
            _blown.Add(rb);

            float f = ConeFalloff(rb.worldCenterOfMass, origin, dir);
            if (f <= 0f) continue;

            // Смешиваем направление ветра с направлением «от сопла»: чистый
            // ветер сдувал бы всё строго параллельно, и предметы у краёв конуса
            // ехали бы сквозь друг друга вместо того, чтобы расходиться.
            Vector3 away = (rb.worldCenterOfMass - origin).normalized;
            Vector3 push = (Vector3.Lerp(dir, away, 0.5f) + Vector3.up * _windUpward).normalized;

            rb.WakeUp();
            rb.AddForce(push * (_itemPush * s * f), ForceMode.Impulse);
            _windItems++;
        }
    }

    private void BlowPlayers(float s, Vector3 origin, Vector3 dir)
    {
        foreach (FirstPersonController p in
                 Object.FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None))
        {
            if (p == null || !p.IsOwner) continue;

            // Прыгуна не толкаем: его и так подбросило отскоком, а добавка
            // ветра унесла бы его с шины боком в тот же кадр.
            if (Find(p) != null) continue;

            float f = ConeFalloff(p.transform.position + Vector3.up, origin, dir);
            if (f <= 0f) continue;

            p.AddPush((dir + Vector3.up * _windUpward).normalized * (_playerPush * s * f));
        }
    }

    private void FanFires(float s, Vector3 origin, Vector3 dir)
    {
        foreach (Firebox fire in Object.FindObjectsByType<Firebox>(FindObjectsSortMode.None))
        {
            if (fire == null) continue;

            float f = ConeFalloff(fire.FirePoint.position, origin, dir);
            if (f <= 0f) continue;

            // ServerAddAir помечен [Server]. Вне сервера он не сработает и
            // насорит предупреждениями, поэтому проверяем сами и говорим
            // прямо — иначе «костёр не раздувается» выглядело бы новой
            // загадкой.
            if (!fire.IsServerInitialized)
            {
                if (_logEverything)
                    Debug.LogWarning($"[Шина] «{name}»: костёр «{fire.name}» в " +
                                     "конусе, но мы не сервер — раздув только " +
                                     "на хосте.", this);
                continue;
            }

            fire.ServerAddAir(_fireAir * s * f);
            _windFires++;

            if (_logEverything)
                Debug.Log($"[Шина] «{name}»: раздуваю «{fire.name}», " +
                          $"сила {s * f:0.00}", this);
        }

        // Новое кострище раздувает TireAimer (он и с клиентов доходит до сервера) —
        // здесь не дублируем, иначе один прыжок считался бы дважды.
    }

    /// <summary>
    /// Насколько ветер достаёт до точки, 0..1. Ноль — вне конуса.
    ///
    /// Затухание идёт И по расстоянию, И по углу: у кромки конуса дует слабее,
    /// чем по оси. Без углового затухания граница ощущается стеной — шаг вбок,
    /// и ветер обрывается полностью.
    /// </summary>
    private float ConeFalloff(Vector3 point, Vector3 origin, Vector3 dir)
    {
        Vector3 to = point - origin;
        float dist = to.magnitude;
        if (dist > _windRange || dist < 0.001f) return 0f;

        float angle = Vector3.Angle(dir, to);
        if (angle > _windHalfAngle) return 0f;

        float byDist = 1f - dist / _windRange;
        float byAngle = 1f - angle / _windHalfAngle;

        return byDist * Mathf.Lerp(0.4f, 1f, byAngle);
    }

    /// <summary>Высота верха твёрдой части шины — по ней различаем удар сверху
    /// и сбоку.</summary>
    private float SolidTopY()
    {
        Transform root = transform.parent != null ? transform.parent : transform;
        float top = float.NegativeInfinity;

        foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
        {
            if (c == null || c.isTrigger) continue;
            top = Mathf.Max(top, c.bounds.max.y);
        }

        return float.IsNegativeInfinity(top) ? transform.position.y : top;
    }

    /// <summary>Подогнать триггер под твёрдую часть шины: по габаритам сверху,
    /// с запасом по высоте. Кнопка в шестерёнке компонента — чтобы не ловить
    /// высоту руками.</summary>
    [ContextMenu("Подогнать зону под шину")]
    private void FitZoneToTire()
    {
        BoxCollider zone = GetComponent<BoxCollider>();
        if (zone == null)
        {
            Debug.LogError($"[Шина] «{name}»: подгонка работает только для " +
                           "BoxCollider.", this);
            return;
        }

        Transform root = transform.parent != null ? transform.parent : transform;

        Bounds b = default;
        bool has = false;

        foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
        {
            if (c == null || c.isTrigger) continue;
            if (!has) { b = c.bounds; has = true; }
            else b.Encapsulate(c.bounds);
        }

        if (!has)
        {
            Debug.LogError($"[Шина] «{name}»: твёрдых коллайдеров рядом нет — " +
                           "подгонять не подо что.", this);
            return;
        }

        // Зона накрывает шину ЦЕЛИКОМ — и сверху, и с боков. Сверху уходит на
        // метр вверх (там ловится приземление), по бокам чуть шире габаритов
        // (там ловится налёт бочком). С проверкой по состоянию, а не по моменту
        // входа, большая зона ничему не мешает и снимает всю возню с точной
        // подгонкой.
        float extraTop = 1.0f;
        float height = b.size.y + extraTop;

        Vector3 centerWorld = new Vector3(b.center.x,
                                          b.min.y + height * 0.5f,
                                          b.center.z);

        zone.center = transform.InverseTransformPoint(centerWorld);
        zone.size = new Vector3(b.size.x * 1.25f, height, b.size.z * 1.25f);
        zone.isTrigger = true;

        Debug.Log($"[Шина] «{name}»: зона подогнана — размер {zone.size:0.00}, " +
                  $"верх твёрдой части {b.max.y:0.00}", this);
    }

    private void OnGUI()
    {
        if (!_showCounter) return;

        GUI.color = Color.white;
        GUI.Label(new Rect(10, 320, 700, 80),
            $"ШИНА «{name}»\n" +
            $"входов в триггер: {_enterCount}   подбросов: {_bounceCount}   " +
            $"сейчас в зоне: {_inside.Count}\n" +
            $"ветер: залпов {_windBlasts}   сдуто предметов {_windItems}   " +
            $"раздувов костра {_windFires}\n" +
            $"последнее: {_lastReason}");
    }

    /// <summary>
    /// Рисуется ВСЕГДА, а не только у выделенного объекта.
    ///
    /// Скрипт живёт на дочернем JumpZone, и чтобы увидеть конус, приходилось
    /// выделять именно его, а не корень шины. При наведении ветра на костёр это
    /// мешает: смотришь на шину целиком, а подсказки нет.
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!_windEnabled) return;
        DrawWindCone(false);
    }

    private void OnDrawGizmosSelected()
    {
        Collider c = GetComponent<Collider>();
        if (c != null)
        {
            Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.9f);
            Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
        }

        if (_windEnabled) DrawWindCone(true);
    }

    /// <summary>Конус ветра. Внутрь него должна попасть точка FirePoint
    /// костра — не костёр целиком, а именно она.</summary>
    private void DrawWindCone(bool detailed)
    {
        Transform n = Nozzle;
        if (n == null) return;

        Vector3 dir = WindDir;
        Vector3 tip = n.position + dir * _windRange;
        float r = Mathf.Tan(_windHalfAngle * Mathf.Deg2Rad) * _windRange;

        // Опорные оси строим сами, а не берём n.right / n.up: сопло может быть
        // повёрнуто как угодно, и тогда конус нарисовался бы косым.
        Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.95f
                                          ? Vector3.forward : Vector3.up).normalized;
        Vector3 up = Vector3.Cross(side, dir).normalized;

        Gizmos.color = detailed
            ? new Color(0.4f, 0.85f, 1f, 0.95f)
            : new Color(0.4f, 0.85f, 1f, 0.45f);

        Gizmos.DrawLine(n.position, tip);

        int steps = detailed ? 16 : 8;
        Vector3 prev = Vector3.zero;

        for (int i = 0; i <= steps; i++)
        {
            float a = i / (float)steps * Mathf.PI * 2f;
            Vector3 edge = tip + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * r;

            if (i % (steps / 4) == 0) Gizmos.DrawLine(n.position, edge);
            if (i > 0) Gizmos.DrawLine(prev, edge);
            prev = edge;
        }

        // Шарик на конце — чтобы направление читалось даже когда конус узкий и
        // сливается в линию.
        Gizmos.DrawWireSphere(tip, 0.15f);
    }
}
