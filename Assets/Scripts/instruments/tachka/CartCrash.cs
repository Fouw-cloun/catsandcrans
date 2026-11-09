using UnityEngine;

/// <summary>
/// УДАР ТАЧКИ В ПРЕПЯТСТВИЕ.
///
/// ═══ ПОЧЕМУ КОЛЛАЙДЕРЫ САМИ ПО СЕБЕ НЕ РАБОТАЮТ ═══
///
/// Пока тачку катят, её Rigidbody КИНЕМАТИЧЕСКИЙ и едет через MovePosition.
/// Это принципиально: только так стенки корыта выметают объём и честно толкают
/// груз. Но у кинематического тела есть обратная сторона, про которую легко
/// забыть:
///
///   кинематическое тело ТОЛКАЕТ динамику, но САМО НЕ ОСТАНАВЛИВАЕТСЯ
///   ни статикой, ни чем-либо ещё
///
/// Стена не может его задержать — у неё для этого нет никакого механизма. Она
/// не «слабее», она вообще не участвует. Поэтому тачка въезжает в колонну и
/// продолжает ехать, наполовину утонув в ней, а коллайдеры при этом целы,
/// правильно настроены и совершенно ни при чём. Крутить их размеры бесполезно:
/// проблема не в форме, а в том, что упор просто некому обработать.
///
/// Значит, упор надо посчитать самому. Отсюда и луч.
///
/// ═══ ЧТО ДЕЛАЕТ ЭТОТ КОМПОНЕНТ ═══
///
/// 1. Перед тачкой идёт ПРОБА ФОРМОЙ САМОЙ ТАЧКИ — не тонкий луч, а коробка
///    её габаритов. Тонкий луч из середины пролетает мимо угла ящика, и тачка
///    входит в него бортом, ничего не заметив.
///
/// 2. Наткнулась — цель движения ОБРЕЗАЕТСЯ по препятствию. Тачка встаёт, а не
///    проходит насквозь. Этим занимается ClampTarget, который зовёт PushCart.
///
/// 3. Влетела быстро — игрока ПЕРЕКИДЫВАЕТ ЧЕРЕЗ ТАЧКУ. Ручки вырывает, груз
///    вылетает, тачку встряхивает. Ровно та же последовательность, что и на
///    сверхперегрузе, и это не совпадение: исход один и тот же, а значит и код
///    должен быть один.
///
/// ═══ ЧТО ОТСЕИВАЕТСЯ ═══
///
/// ПОЛ И СКЛОНЫ — по нормали. Коробка габаритов задевает землю почти всегда, и
/// без этого фильтра тачка упиралась бы в собственную дорогу.
///
/// СОБСТВЕННЫЕ КОЛЛАЙДЕРЫ И ГРУЗ — по родителю. Груз лежит внутри корыта, то
/// есть внутри пробы, и каждое полено читалось бы как стена.
///
/// ДЕРЖАТЕЛЬ — по компоненту игрока. Он стоит вплотную сзади и сбоку; упор в
/// него запустил бы ровно тот самораскручивающийся цикл, ради которого в
/// PushCart уже сделано IgnoreCollision.
///
/// НУЛЕВАЯ ДИСТАНЦИЯ — отдельно. Если проба стартует уже внутри чего-то,
/// BoxCast возвращает ноль, и тачка залипла бы навсегда: выехать нельзя, потому
/// что упор считается на месте. Такие попадания игнорируются — лучше один кадр
/// внутри стены, чем намертво приклеенная тачка.
///
/// ═══ СБОРКА ═══
///
/// Вешается на КОРЕНЬ тачки, туда же, где PushCart, CartRig и CartBalance.
/// Работает и без правки PushCart — тогда останутся только бросок и встряска,
/// а упор нет: обрезать цель движения может только тот, кто её задаёт.
/// </summary>
[RequireComponent(typeof(PushCart))]
[AddComponentMenu("CatCooks/Тачка/Cart Crash")]
public class CartCrash : MonoBehaviour
{
    [Header("━━━ Проба ━━━")]
    [Tooltip("Во что тачка упирается. Убери отсюда слой самой тачки, слой " +
             "груза и слой игроков — эти три отсеиваются и кодом, но лишний " +
             "слой в маске это лишняя работа на каждом кадре.")]
    [SerializeField] private LayerMask _blockMask = ~0;

    [Tooltip("НА СКОЛЬКО СМОТРЕТЬ ВПЕРЁД сверх текущего шага, метры.\n\n" +
             "Ноль — тачка останавливается ровно в момент касания, и на " +
             "быстром ходу это выглядит как удар в невидимую стену за кадр до " +
             "препятствия. Небольшой запас даёт ей замедлиться заранее.")]
    [Range(0f, 1f)][SerializeField] private float _lookAhead = 0.25f;

    [Tooltip("ЗАЗОР ДО ПРЕПЯТСТВИЯ, метры. Тачка встаёт не впритык: впритык " +
             "проба на следующем кадре стартует изнутри и вернёт ноль.")]
    [Range(0.005f, 0.2f)][SerializeField] private float _skin = 0.04f;

    [Tooltip("УСАДКА КОРОБКИ. Единица — точные габариты, и тачка цепляется " +
             "дверными косяками, в которые физически проходит. 0.85 " +
             "оставляет запас на неточность мешей.")]
    [Range(0.4f, 1f)][SerializeField] private float _fit = 0.85f;

    [Tooltip("ПОДНЯТЬ НИЗ КОРОБКИ НАД ЗЕМЛЁЙ, метры. Иначе проба скребёт " +
             "грунт и ловит каждый бордюр как стену.")]
    [Range(0f, 0.5f)][SerializeField] private float _riseFromGround = 0.14f;

    [Tooltip("С какой нормали поверхность считается ПОЛОМ, а не стеной. 0.5 — " +
             "примерно 60°: по такому склону тачка ещё едет.")]
    [Range(0f, 1f)][SerializeField] private float _floorNormal = 0.5f;

    [Header("━━━ Удар ━━━")]
    [Tooltip("С КАКОЙ СКОРОСТИ УДАР КИДАЕТ ИГРОКА, м/с.\n\n" +
             "Ниже — тачка просто упрётся и встанет. Наезд на угол шагом не " +
             "должен разбирать всю сцену.")]
    [Range(0.5f, 8f)][SerializeField] private float _crashSpeed = 2.2f;

    [Tooltip("Скорость, на которой бросок выходит на полную силу")]
    [Range(1f, 14f)][SerializeField] private float _crashFullAt = 4.5f;

    [Range(0.2f, 1.5f)][SerializeField] private float _throwMin = 0.65f;
    [Range(0.5f, 2f)][SerializeField] private float _throwMax = 1.25f;

    [Tooltip("Пауза между ударами. Без неё одна стена даёт очередь бросков, " +
             "пока игрок не отойдёт.")]
    [Range(0.2f, 5f)][SerializeField] private float _cooldown = 1.4f;

    [Tooltip("КУВЫРКАТЬ ТАЧКУ ЧЕРЕЗ ПЕРЁД при сильном ударе.\n\n" +
             "Выключено — летит только игрок, а тачка остаётся стоять, будто " +
             "ничего не было. Это самое неубедительное, что может случиться " +
             "после лобового: удар был, последствие только у одного из двоих.\n\n" +
             "Включено — тачку вырывает из рук, груз вылетает, и она " +
             "перекувыркивается через нос. Ось переворота поперечная, в " +
             "отличие от провала мини-игры, где тачка ложится набок.")]
    [SerializeField] private bool _flipOnCrash = true;

    [Header("━━━ Мягкий упор ━━━")]
    [Tooltip("НА СКОЛЬКО ВСТРЯХИВАЕТ КРЕН при упоре без броска. Тачка, " +
             "которая просто беззвучно встала, читается как застрявшая в " +
             "текстуре. Толчок в баланс сообщает, что она во что-то уткнулась.")]
    [Range(0f, 2f)][SerializeField] private float _bumpLean = 0.5f;

    [Tooltip("ОТТАЛКИВАНИЕ ДЕРЖАТЕЛЯ, м/с.\n\n" +
             "Тачка встала, а игрок продолжает идти — и въезжает в неё " +
             "спиной ручек. Небольшой встречный толчок держит его на " +
             "расстоянии вытянутых рук, вместо того чтобы дать ему просочиться " +
             "сквозь собственную тачку.")]
    [Range(0f, 6f)][SerializeField] private float _pushBackHolder = 2.4f;

    [Header("━━━ Отладка ━━━")]
    [SerializeField] private bool _drawGizmos = true;
    [SerializeField] private bool _debugLog = false;

    // ───────────────────────── состояние ─────────────────────────

    private PushCart _cart;
    private CartBalance _balance;
    private Rigidbody _rb;

    private Vector3 _boxCenterLocal;
    private Vector3 _boxHalf;
    private bool _measured;

    private readonly RaycastHit[] _hits = new RaycastHit[12];

    private Vector3 _lastPos;
    private float _speedNow;

    // ПИКОВАЯ СКОРОСТЬ ЗА ПОСЛЕДНИЕ ДОЛИ СЕКУНДЫ.
    //
    // Мгновенная не годится: к тому кадру, когда упор уже посчитан, тачка
    // именно из-за него и остановилась. Мерить силу удара по ней — значит
    // мерить её ПОСЛЕ удара и всегда получать ноль.
    private float _recentSpeed;

    private float _cooldownLeft;
    private bool _blocked;
    private bool _clampedThisFrame;
    private Vector3 _blockPoint, _blockNormal, _blockDir;

    /// <summary>Упёрлась ли тачка прямо сейчас — для пыли, звука, интерфейса.</summary>
    public bool Blocked => _blocked;

    public Vector3 BlockPoint => _blockPoint;
    public Vector3 BlockNormal => _blockNormal;

    private void Awake()
    {
        _cart = GetComponent<PushCart>();
        _balance = GetComponent<CartBalance>();
        _rb = GetComponent<Rigidbody>();

        Measure();

        _lastPos = transform.position;
    }

    /// <summary>
    /// ГАБАРИТЫ ТАЧКИ, один раз.
    ///
    /// Считаются по собственным коллайдерам, а не задаются полем: подгонять
    /// коробку руками под каждую модель — это ровно тот вид настройки, который
    /// однажды забудут поправить после замены меша, и тачка начнёт упираться в
    /// воздух в метре от стены.
    ///
    /// Триггеры пропускаются: зона подбора и зона корыта не мешают ехать.
    /// </summary>
    private void Measure()
    {
        Collider[] all = GetComponentsInChildren<Collider>(true);

        bool any = false;
        Bounds local = new Bounds(Vector3.zero, Vector3.zero);

        foreach (Collider c in all)
        {
            if (c == null || c.isTrigger) continue;

            Bounds b = c.bounds;

            // Восемь углов мирового AABB, переведённые в свои координаты. Не
            // идеально точно для повёрнутых коллайдеров, но для габаритной
            // коробки этого более чем достаточно.
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);

                Vector3 p = transform.InverseTransformPoint(corner);

                if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                else local.Encapsulate(p);
            }
        }

        if (!any)
        {
            Debug.LogWarning($"[Удар] «{name}»: ни одного обычного коллайдера — " +
                             "мерить нечего, упор считаться не будет.", this);
            _measured = false;
            return;
        }

        // Низ поднимаем: проба не должна скрести землю.
        float bottom = local.min.y + _riseFromGround;
        float top = Mathf.Max(bottom + 0.05f, local.max.y);

        _boxCenterLocal = new Vector3(local.center.x, (bottom + top) * 0.5f, local.center.z);
        _boxHalf = new Vector3(local.extents.x, (top - bottom) * 0.5f, local.extents.z) * _fit;

        _measured = true;
    }

    /// <summary>
    /// ОБРЕЗКА ЦЕЛИ ДВИЖЕНИЯ. Зовёт PushCart из ComputeTarget.
    ///
    /// Обрезается ТОЛЬКО горизонталь. Высоту задаёт поиск земли, и трогать её
    /// здесь нельзя: тачка перестала бы подниматься по склону перед тем, во
    /// что упёрлась, и повисла бы в воздухе.
    /// </summary>
    public Vector3 ClampTarget(Vector3 from, Vector3 want)
    {
        _clampedThisFrame = true;
        _blocked = false;

        if (!_measured) return want;

        Vector3 flat = want - from;
        flat.y = 0f;

        float dist = flat.magnitude;
        if (dist < 0.0005f) return want;

        Vector3 dir = flat / dist;
        float allowed = Sweep(from, dir, dist + _lookAhead);

        if (allowed >= dist) return want;

        _blocked = true;
        _blockDir = dir;

        Vector3 stopped = from + dir * Mathf.Max(0f, allowed);
        stopped.y = want.y;

        return stopped;
    }

    /// <summary>
    /// ПРОБА КОРОБКОЙ. Возвращает, сколько метров свободно по направлению.
    /// </summary>
    private float Sweep(Vector3 from, Vector3 dir, float maxDist)
    {
        Vector3 center = from + transform.rotation * _boxCenterLocal;

        int n = Physics.BoxCastNonAlloc(center, _boxHalf, dir, _hits, transform.rotation,
                                        maxDist + _skin, _blockMask,
                                        QueryTriggerInteraction.Ignore);

        float best = maxDist;

        for (int i = 0; i < n; i++)
        {
            RaycastHit h = _hits[i];

            if (h.collider == null) continue;

            // Своё: рама, корыто, колёса и весь груз внутри.
            if (h.collider.transform.IsChildOf(transform)) continue;

            // Игроки. Держатель стоит вплотную, а упор в него — это тот самый
            // самораскручивающийся цикл, от которого в PushCart уже стоит
            // IgnoreCollision.
            if (h.collider.GetComponentInParent<FirstPersonController>() != null) continue;

            // Пол и пологие склоны: коробка задевает землю почти всегда.
            if (h.normal.y > _floorNormal) continue;

            // Стартовали изнутри. Считать это упором — значит приклеить тачку
            // намертво: выехать из стены она уже не сможет.
            if (h.distance <= 0.0001f) continue;

            float d = h.distance - _skin;

            if (d < best)
            {
                best = d;
                _blockPoint = h.point;
                _blockNormal = h.normal;
            }
        }

        return best;
    }

    private void LateUpdate()
    {
        float dt = Mathf.Max(0.0001f, Time.deltaTime);

        if (_cooldownLeft > 0f) _cooldownLeft -= dt;

        Vector3 d = transform.position - _lastPos;
        _lastPos = transform.position;
        d.y = 0f;

        _speedNow = d.magnitude / dt;

        // Пик держится примерно четверть секунды и сходит сам.
        _recentSpeed = Mathf.Max(_speedNow, _recentSpeed - dt * 6f);

        // ЗАПАСНОЙ ПУТЬ: PushCart не пропатчен и ClampTarget никто не звал.
        // Упора не будет — обрезать цель может только тот, кто её задаёт, — но
        // удар и встряска должны работать и так.
        if (!_clampedThisFrame && _measured && _cart != null && _cart.IsGripped)
        {
            Vector3 dir = d.sqrMagnitude > 1e-6f ? d.normalized : transform.forward;
            dir.y = 0f;

            if (dir.sqrMagnitude > 1e-6f)
            {
                dir.Normalize();
                _blocked = Sweep(transform.position, dir, _lookAhead) < _lookAhead;
                _blockDir = dir;
            }
        }

        _clampedThisFrame = false;

        if (_blocked) OnBlocked(dt);
    }

    /// <summary>
    /// ТАЧКА ВО ЧТО-ТО УПЁРЛАСЬ. Дальше решается только одно: это остановка
    /// или это удар.
    /// </summary>
    private void OnBlocked(float dt)
    {
        if (_cart == null || !_cart.IsGripped) return;

        // ── ЖЁСТКО: ПЕРЕКИДЫВАЕТ ЧЕРЕЗ ТАЧКУ ──
        if (_recentSpeed >= _crashSpeed && _cooldownLeft <= 0f &&
            _balance != null && !_balance.ThrowBusy)
        {
            float hard = Mathf.InverseLerp(_crashSpeed, _crashFullAt, _recentSpeed);
            float strength = Mathf.Lerp(_throwMin, _throwMax, hard);

            _cooldownLeft = _cooldown;
            _recentSpeed = 0f;

            if (_flipOnCrash) _balance.CrashOut(_blockDir, strength, "УДАР");
            else _balance.ThrowOverCart(strength, "УДАР");

            if (_debugLog)
                Debug.Log($"[Удар] «{name}»: врезался на {_speedNow:0.0} м/с " +
                          $"(пик {strength:0.00})", this);
            return;
        }

        // ── МЯГКО: ПРОСТО УПЁРЛАСЬ ──
        //
        // Толчок в баланс, чтобы упор было видно, и встречный толчок игроку,
        // чтобы он не прошёл сквозь собственную тачку.
        if (_balance != null && _bumpLean > 0.01f && _speedNow > 0.2f)
        {
            bool toRight = Vector3.Dot(_blockNormal, transform.right) < 0f;
            _balance.Nudge(_bumpLean * dt * 4f, toRight);
        }

        if (_pushBackHolder > 0.01f && _balance != null && _balance.Holder != null)
            _balance.Holder.AddPush(-_blockDir * (_pushBackHolder * dt * 4f));
    }

    // ───────────────────────── диагностика ─────────────────────────

    [ContextMenu("★ ПОЧЕМУ НЕ РАБОТАЕТ")]
    public void Diagnose()
    {
        if (!_measured) Measure();

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        int problems = 0;

        sb.AppendLine($"═══ УДАР ТАЧКИ: {name} ═══\n");

        sb.AppendLine($"PushCart: {(_cart != null ? "есть" : "НЕТ")}");
        sb.AppendLine($"CartBalance: {(_balance != null ? "есть" : "НЕТ")}");

        if (_balance == null)
        {
            sb.AppendLine("\n❌ Без CartBalance некому перекидывать игрока — " +
                          "останется только упор.");
            problems++;
        }

        if (!_measured)
        {
            sb.AppendLine("\n❌❌ Габариты не измерены: нет ни одного обычного " +
                          "коллайдера.");
            problems++;
        }
        else
        {
            sb.AppendLine($"\nКоробка пробы: {_boxHalf * 2f} м, центр {_boxCenterLocal}");
        }

        if (_blockMask == 0)
        {
            sb.AppendLine("\n❌ Block Mask пуста — упираться не во что.");
            problems++;
        }

        sb.AppendLine($"\nСкорость {_speedNow:0.00} м/с, пик {_recentSpeed:0.00}");
        sb.AppendLine($"Упёрлась: {(_blocked ? "ДА" : "нет")}");
        sb.AppendLine($"Обрезку цели зовут: " +
                      $"{(_clampedThisFrame ? "да" : "НЕТ — PushCart не пропатчен")}");

        sb.AppendLine($"\n═══ ПРОБЛЕМ: {problems} ═══");

        if (problems == 0)
            sb.AppendLine("Чисто. Если тачка проходит сквозь стену — проверь, " +
                          "что слой стены есть в Block Mask, и что PushCart " +
                          "зовёт ClampTarget: без этого вызова упор посчитан, " +
                          "но применить его некому.");

        if (problems > 0) Debug.LogWarning(sb.ToString(), this);
        else Debug.Log(sb.ToString(), this);
    }

    private void OnDrawGizmosSelected()
    {
        if (!_drawGizmos || !_measured) return;

        Matrix4x4 old = Gizmos.matrix;

        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.color = _blocked
            ? new Color(1f, 0.35f, 0.3f, 0.9f)
            : new Color(0.4f, 0.9f, 1f, 0.5f);

        Gizmos.DrawWireCube(_boxCenterLocal, _boxHalf * 2f);

        Gizmos.matrix = old;

        if (Application.isPlaying && _blocked)
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 1f);
            Gizmos.DrawWireSphere(_blockPoint, 0.08f);
            Gizmos.DrawLine(_blockPoint, _blockPoint + _blockNormal * 0.5f);
        }
    }
}
