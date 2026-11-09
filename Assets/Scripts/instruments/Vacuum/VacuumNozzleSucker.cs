using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using CatCooks.Food;
using CatCooks.Food.Tools;

/// <summary>
/// ВСАСЫВАНИЕ И ВЫСТРЕЛ. Вешается на дуло, рядом с NozzleItem.
///
/// РЕЖИМЫ (переключаются клавишей на дуле):
///   Suck — мелочь втягивается внутрь и копится в баке, крупное присасывается
///          к раструбу и висит на нём;
///   Blow — выплёвывает накопленное по очереди, по одному предмету.
///
/// Работает, только когда включён компрессор на базе: дуло само по себе
/// не сосёт, как и в жизни.
///
/// ЧТО ГДЕ СЧИТАЕТСЯ:
///   сервер  — ищет добычу, тянет её, глотает и выплёвывает. Он же владеет
///             списком проглоченного;
///   клиенты — рисуют воздушный вихрь у раструба по синхронизированному режиму.
///
/// ДЫМ ДЛЯ КОПЧЕНИЯ:
///   Suck над горящим костром (дуло в столбе дыма над огнём) — бак набирает ДЫМ (полоска в подсказке);
///   Blow у ОТКРЫТОГО люка духовки (дуло рядом с дыркой) — дым уходит в духовку, рёбра/брискет коптятся.
///   Пока в баке есть дым, Blow выдувает сначала дым, предметы — потом.
///
/// Присосанное крупное НЕ становится дочерним объектом: оно просто удерживается
/// у раструба каждый кадр. Так проще и не ломает сетевую иерархию — вложенные
/// NetworkObject в проекте и так работают плохо.
/// </summary>
public class VacuumNozzleSucker : NetworkBehaviour
{
    [Header("Связь с базой")]
    [Tooltip("База с компрессором. Пусто = найдётся по сцене при старте.")]
    [SerializeField] private VacuumStation _station;

    [Tooltip("Работать даже при выключенном компрессоре. Для отладки.")]
    [SerializeField] private bool _ignoreCompressor;

    [Header("Раструб")]
    [Tooltip("Кончик дула: отсюда тянет и отсюда же вылетает. Пусто = сам объект.")]
    [SerializeField] private Transform _mouth;

    [Tooltip("Дальность всасывания, метры")]
    [SerializeField] private float _range = 3.5f;

    [Tooltip("Половина угла раструба, градусы. Узкий конус — надо целиться.")]
    [Range(5f, 90f)][SerializeField] private float _coneAngle = 35f;

    [Tooltip("Слои, откуда можно сосать")]
    [SerializeField] private LayerMask _suckMask = ~0;

    [Header("Тяга: мелочь")]
    [Tooltip("С какой скоростью мелочь летит в раструб, м/с. " +
             "Больше — резче всасывает.")]
    [SerializeField] private float _suckSpeed = 7f;

    [Tooltip("Как быстро предмет разгоняется до этой скорости. Высокое значение " +
             "означает, что его буквально выдёргивает.")]
    [SerializeField] private float _suckAccel = 40f;

    [Tooltip("Насколько слабее тянет на дальнем краю конуса")]
    [Range(0f, 1f)][SerializeField] private float _falloff = 0.5f;

    [Tooltip("С какого расстояния мелочь проглатывается, метры. Держи с запасом: " +
             "предмет упирается коллайдером в дуло и ближе просто не подлетит.")]
    [SerializeField] private float _swallowDistance = 0.6f;

    [Header("Тяга: крупное")]
    [Tooltip("На каком расстоянии удерживается присосанное крупное, метры")]
    [SerializeField] private float _holdDistance = 0.55f;

    [Tooltip("Жёсткость удержания. Больше — сильнее прилипает и меньше болтается.")]
    [SerializeField] private float _holdStiffness = 25f;

    [Tooltip("Гашение болтанки присосанного. 1 = намертво, 0 = свободно висит.")]
    [Range(0f, 1f)][SerializeField] private float _holdDamping = 0.85f;

    [Tooltip("Гашение вращения: без него присосанный предмет крутится волчком")]
    [Range(0f, 1f)][SerializeField] private float _spinDamping = 0.8f;

    [Header("Бак")]
    [Tooltip("Сколько мелочи влезает. Больше — не всасывает.")]
    [Min(1)][SerializeField] private int _capacity = 24;

    [Header("Выстрел")]
    [Tooltip("Секунд между выстрелами: предметы вылетают по очереди")]
    [SerializeField] private float _blowInterval = 0.35f;

    [Tooltip("Разброс направления вылета, градусы")]
    [SerializeField] private float _blowSpread = 12f;

    [Header("Дым (копчение)")]
    [Tooltip("Сколько бака дыма набирается в секунду у жаркого костра (1 — полный за секунду)")]
    [SerializeField] private float _smokeSuckPerSecond = 0.2f;
    [Tooltip("Дуло ближе этого к столбу дыма над огнём — сосёт дым, м")]
    [SerializeField] private float _smokeReach = 1.1f;
    [Tooltip("Высота столба дыма над огнём, м")]
    [SerializeField] private float _smokeColumn = 2.2f;
    [Tooltip("ЗОНА КОСТРА: дуло в цилиндре вокруг огня (радиус, м) — сосёт дым. Не надо искать столб: просто у костра")]
    [SerializeField] private float _smokeZone = 3f;
    [Tooltip("Высота зоны над огнём, м")]
    [SerializeField] private float _smokeZoneHeight = 5f;
    [Tooltip("Сколько бака дыма выдувается в секунду")]
    [SerializeField] private float _smokeBlowPerSecond = 0.3f;
    [Tooltip("Дуло ближе этого к люку духовки — дым идёт внутрь, м")]
    [SerializeField] private float _ventReach = 0.8f;
    [Tooltip("Сколько дыма в духовке даёт полный бак (1 — полная духовка)")]
    [SerializeField] private float _smokeToOven = 1.2f;
    [SerializeField] private Color _smokeColor = new Color(0.55f, 0.55f, 0.58f, 0.55f);

    [Header("Управление")]
    [Tooltip("Переключить режим: сосать / выплёвывать")]
    [SerializeField] private KeyCode _modeKey = KeyCode.R;

    [Header("Отладка")]
    [SerializeField] private bool _debugHud = true;

    // ───────── сеть ─────────
    /// <summary>Всасывать или выплёвывать. ВКЛЮЧЕНИЕ здесь ни при чём — им
    /// ведает кнопка компрессора на корпусе базы.</summary>
    private readonly SyncVar<byte> _mode = new SyncVar<byte>((byte)VacuumMode.Suck);

    /// <summary>Что присосано к раструбу. Крупное держится снаружи.</summary>
    private readonly SyncVar<NetworkObject> _stuck = new SyncVar<NetworkObject>();

    /// <summary>Сколько мелочи в баке — для подсказки игроку.</summary>
    private readonly SyncVar<byte> _stored = new SyncVar<byte>(0);

    /// <summary>Дым в баке 0..255.</summary>
    private readonly SyncVar<byte> _smokeNet = new SyncVar<byte>(0);
    private float _smoke, _smokeLogAt;

    // ───────── сервер ─────────
    /// <summary>Проглоченное. Живёт только на сервере: клиентам достаточно
    /// количества, а сами объекты деспавнены.</summary>
    private readonly List<GameObject> _belly = new List<GameObject>();

    private readonly Collider[] _hits = new Collider[48];
    private Collider[] _ownColliders;
    private readonly List<Collider> _ignoredColliders = new List<Collider>();
    private float _nextBlow;
    private NozzleItem _item;

    private ParticleSystem _swirl;
    private float _swirlCarry;

    public VacuumMode Mode => (VacuumMode)_mode.Value;
    public int Stored => _stored.Value;
    public float Smoke01 => _smokeNet.Value / 255f;
    public Transform Mouth => _mouth != null ? _mouth : transform;

    /// <summary>Компрессор гудит и дуло в руках — только тогда есть тяга.</summary>
    public bool IsActive
    {
        get
        {
            if (_ignoreCompressor) return true;
            return _station != null && _station.IsRunning;
        }
    }

    private void Awake()
    {
        _item = GetComponent<NozzleItem>();
        if (_station == null) _station = FindFirstObjectByType<VacuumStation>();
        CreateSwirl();
    }

    private void OnDestroy()
    {
        RestoreIgnored();
        if (_swirl != null) Destroy(_swirl.gameObject);

        // Проглоченное надо убрать за собой, иначе объекты повиснут
        // деспавненными в памяти до конца сессии.
        foreach (GameObject g in _belly) if (g != null) Destroy(g);
    }

    // ───────────────────────── ЦИКЛ ─────────────────────────

    private void Update()
    {
        if (base.IsOwner) OwnerInput();
        DrawSwirl();
    }

    /// <summary>
    /// Тяга считается в FixedUpdate, вместе с самой физикой.
    ///
    /// В Update скорости правились между шагами физического движка: часть
    /// правок он успевал перетереть своими, часть применялась дважды за шаг.
    /// Отсюда и дрожание, и то, что предмет то втягивает, то отбрасывает.
    /// </summary>
    private void FixedUpdate()
    {
        if (base.IsServerInitialized) ServerTick();
    }

    private void OwnerInput()
    {
        if (!Input.GetKeyDown(_modeKey)) return;

        // Только направление: всасывать или выплёвывать. Выключение — кнопкой
        // на корпусе базы, дулом им не управляют.
        VacuumMode next = Mode == VacuumMode.Suck ? VacuumMode.Blow : VacuumMode.Suck;
        CmdSetMode((byte)next);
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdSetMode(byte mode)
    {
        _mode.Value = mode;

        // Сменили режим — присосанное отпускаем.
        if ((VacuumMode)mode != VacuumMode.Suck) ServerReleaseStuck();
    }

    [Server]
    private void ServerTick()
    {
        // Дуло выронили или компрессор заглушили — всё бросаем.
        if (!IsActive)
        {
            if (_stuck.Value != null) ServerReleaseStuck();
            RestoreIgnored();
            return;
        }

        if (Mode == VacuumMode.Suck) Suck();
        else if (Mode == VacuumMode.Blow) Blow();
    }

    // ───────────────────────── ВСАСЫВАНИЕ ─────────────────────────

    [Server]
    private void Suck()
    {
        Transform m = Mouth;
        Vector3 origin = m.position;
        Vector3 dir = m.forward;

        // Присосанное держим у раструба каждый кадр.
        HoldStuck(origin, dir);

        // Дым над костром — в бак.
        SuckSmoke(origin);

        int n = Physics.OverlapSphereNonAlloc(origin, _range, _hits, _suckMask,
                                              QueryTriggerInteraction.Ignore);

        for (int i = 0; i < n; i++)
        {
            Collider col = _hits[i];
            if (col == null) continue;

            Suckable s = col.GetComponentInParent<Suckable>();
            if (s == null || s.Size == SuckableSize.None) continue;
            if (s.transform.IsChildOf(transform)) continue;    // само дуло

            Vector3 to = s.transform.position - origin;
            float dist = to.magnitude;
            if (dist < 0.001f) continue;

            // Конус: тянет только то, на что наведено.
            if (Vector3.Angle(dir, to) > _coneAngle) continue;

            Rigidbody rb = s.GetComponentInParent<Rigidbody>();
            if (rb == null || rb.isKinematic) continue;

            if (s.Size == SuckableSize.Small && dist <= _swallowDistance)
            {
                Swallow(s.gameObject);
                continue;
            }

            if (s.Size == SuckableSize.Large && dist <= _holdDistance * 1.4f)
            {
                TryStick(s);
                continue;
            }

            // ВЕДЁМ СКОРОСТЬЮ, А НЕ СИЛОЙ.
            //
            // Сила к центру предмета не гасит его боковое движение, и он
            // выходит на орбиту вокруг раструба — крутится рядом, но не
            // залетает. Ровно как спутник: тяга есть, а тангенциальная
            // скорость живёт своей жизнью.
            //
            // MoveTowards ведёт ВЕСЬ вектор скорости к нужному направлению,
            // поэтому боковая составляющая гасится сама собой.
            Vector3 toMouth = -to.normalized;
            float t = 1f - Mathf.Clamp01(dist / _range) * _falloff;
            float speed = _suckSpeed * t * s.DragFactor;

            rb.linearVelocity = Vector3.MoveTowards(
                rb.linearVelocity, toMouth * speed,
                _suckAccel * s.DragFactor * Time.fixedDeltaTime);

            // Гасим вращение: кувыркающийся предмет чиркает коллайдером о дуло
            // и отскакивает в сторону, так и не долетев.
            rb.angularVelocity *= 1f - _spinDamping * Time.fixedDeltaTime;

            // Столкновения с дулом снимаем заранее, а не у самой глотки:
            // иначе предмет успевает удариться и отлететь.
            if (s.Size == SuckableSize.Small) IgnoreNozzle(col, true);
        }
    }

    /// <summary>
    /// Отключить столкновения предмета с дулом, пока его засасывает.
    ///
    /// Без этого мелочь просто упирается коллайдером в раструб и зависает
    /// рядом — до глотки не доходит, и со стороны кажется, что её присосало.
    /// </summary>
    private void IgnoreNozzle(Collider other, bool ignore)
    {
        if (other == null) return;
        if (_ownColliders == null) _ownColliders = GetComponentsInChildren<Collider>(true);

        foreach (Collider c in _ownColliders)
        {
            if (c == null || c == other) continue;
            Physics.IgnoreCollision(other, c, ignore);
            if (ignore && !_ignoredColliders.Contains(other)) _ignoredColliders.Add(other);
        }
    }

    /// <summary>Вернуть столкновения всем, кому отключали. Иначе выплюнутый
    /// предмет пролетал бы сквозь дуло, да и список рос бы всю сессию.</summary>
    private void RestoreIgnored()
    {
        if (_ignoredColliders.Count == 0 || _ownColliders == null) return;

        foreach (Collider other in _ignoredColliders)
        {
            if (other == null) continue;
            foreach (Collider c in _ownColliders)
            {
                if (c == null) continue;
                Physics.IgnoreCollision(other, c, false);
            }
        }
        _ignoredColliders.Clear();
    }

    /// <summary>Мелочь исчезает внутрь: деспавним и кладём в бак.</summary>
    [Server]
    private void Swallow(GameObject go)
    {
        if (_belly.Count >= _capacity) return;

        NetworkObject nob = go.GetComponent<NetworkObject>();
        if (nob == null) return;

        // Столкновения с дулом вернуть: предмет уходит в бак, и когда он
        // вылетит обратно, он снова должен от дула отскакивать.
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
            IgnoreNozzle(c, false);

        // Владение снимаем ДО деспавна: клиент-владелец не должен слать
        // позицию уже мёртвого объекта.
        nob.RemoveOwnership();

        // Прячем, а не уничтожаем: при выстреле этот же объект вылетит обратно
        // со всем своим содержимым — журналом обработок, массой, состоянием.
        // Пересоздавать его значило бы потерять всё, что с ним делали.
        go.SetActive(false);
        base.Despawn(nob, DespawnType.Pool);

        _belly.Add(go);
        _stored.Value = (byte)Mathf.Min(_belly.Count, 255);
    }

    /// <summary>Крупное присасывается к раструбу и висит снаружи.</summary>
    [Server]
    private void TryStick(Suckable s)
    {
        if (_stuck.Value != null) return;

        NetworkObject nob = s.GetComponentInParent<NetworkObject>();
        if (nob == null || nob == base.NetworkObject) return;

        _stuck.Value = nob;
    }

    /// <summary>Удерживаем присосанное у раструба. Не делаем дочерним: вложенные
    /// NetworkObject в проекте работают плохо, а так это просто сила каждый кадр.</summary>
    [Server]
    private void HoldStuck(Vector3 origin, Vector3 dir)
    {
        NetworkObject nob = _stuck.Value;
        if (nob == null) return;

        if (!nob.IsSpawned) { _stuck.Value = null; return; }

        Rigidbody rb = nob.GetComponent<Rigidbody>();
        if (rb == null || rb.isKinematic) { _stuck.Value = null; return; }

        Vector3 target = origin + dir * _holdDistance;
        Vector3 delta = target - nob.transform.position;

        // Отцепился слишком далеко — значит вырвался.
        if (delta.magnitude > _holdDistance * 3f) { _stuck.Value = null; return; }

        // ПРУЖИНА С ДЕМПФИРОВАНИЕМ, а не просто пружина.
        //
        // Голая пружина даёт маятник: предмет проскакивает точку удержания,
        // возвращается, снова проскакивает — и болтается на дуле, как луна
        // на орбите. Демпфирование гасит именно эту раскачку.
        Vector3 want = delta * _holdStiffness;
        rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, want,
                                         _holdDamping);

        // И вращение: иначе присосанное крутится волчком.
        rb.angularVelocity *= 1f - _spinDamping * Time.fixedDeltaTime;
    }

    [Server]
    private void ServerReleaseStuck() => _stuck.Value = null;

    // ───────────────────────── ВЫСТРЕЛ ─────────────────────────

    [Server]
    private void Blow()
    {
        // Сначала — дым (если есть), предметы потом.
        if (_smoke > 0.001f) { BlowSmoke(); return; }
        if (_belly.Count == 0) return;
        if (Time.time < _nextBlow) return;
        _nextBlow = Time.time + _blowInterval;

        // По очереди, с начала: что первым засосали, то первым и вылетит.
        GameObject go = _belly[0];
        _belly.RemoveAt(0);
        _stored.Value = (byte)_belly.Count;

        if (go == null) return;

        Transform m = Mouth;
        Vector3 dir = Quaternion.Euler(Random.Range(-_blowSpread, _blowSpread),
                                       Random.Range(-_blowSpread, _blowSpread),
                                       0f) * m.forward;

        go.transform.SetPositionAndRotation(m.position + dir * 0.3f, Random.rotation);
        go.SetActive(true);

        NetworkObject nob = go.GetComponent<NetworkObject>();
        if (nob != null) base.Spawn(go);

        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            Suckable s = go.GetComponent<Suckable>();
            float force = s != null ? s.LaunchForce : 9f;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.AddForce(dir.normalized * force, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * force * 0.4f, ForceMode.Impulse);
        }
    }

    // ───────────────────────── ДЫМ ─────────────────────────

    /// <summary>Горящий костёр, в столбе дыма которого сейчас дуло. 0 — нет.</summary>
    private BonfirePit SmokeSource(Vector3 mouth, out float strength)
    {
        strength = 0f;
        BonfirePit best = null;
        foreach (BonfirePit pit in BonfirePit.All)
        {
            if (pit == null || !pit.IsBurning) continue;
            // зона костра — цилиндр вокруг огня (духовка над огнём не мешает: можно сбоку, сверху, у края)
            Vector3 c = pit.AirPoint;
            float zone = Mathf.Max(_smokeZone, _smokeReach);
            Vector2 flat = new Vector2(mouth.x - c.x, mouth.z - c.z);
            float hd = flat.magnitude, dy = mouth.y - c.y;
            if (hd > zone || dy < -1f || dy > Mathf.Max(_smokeZoneHeight, _smokeColumn)) continue;
            float k = Mathf.Lerp(1f, 0.55f, hd / zone) * Mathf.Max(0.4f, pit.Intensity01);
            if (k > strength) { strength = k; best = pit; }
        }
        return best;
    }

    [Server]
    private void SuckSmoke(Vector3 mouth)
    {
        if (_smoke >= 1f) return;
        if (SmokeSource(mouth, out float k) == null) return;
        bool wasEmpty = _smoke < 0.02f;
        _smoke = Mathf.Min(1f, _smoke + _smokeSuckPerSecond * Mathf.Clamp01(k * 1.5f) * Time.fixedDeltaTime);
        PushSmoke();
        if (wasEmpty || _smoke >= 1f || Time.time >= _smokeLogAt)
        {
            _smokeLogAt = Time.time + 2f;
            Debug.Log($"[Пылесос] набираю дым у костра — {_smoke * 100f:0}%" + (_smoke >= 1f ? " (полный бак: открой люк духовки, R — выдувать, дуло к люку)" : ""), this);
        }
    }

    /// <summary>Духовка с ОТКРЫТЫМ люком, к которому поднесли дуло.</summary>
    private BakeOven OvenAtVent(Vector3 mouth, out bool ventClosed)
    {
        ventClosed = false;
        BakeOven best = null; float bd = _ventReach;
        foreach (BakeOven ov in BakeOven.All)
        {
            if (ov == null || ov.Vent == null) continue;
            float d = Vector3.Distance(mouth, ov.Vent.VentPoint);
            if (d > bd) continue;
            if (!ov.VentOpen) { ventClosed = true; continue; }
            bd = d; best = ov;
        }
        return best;
    }

    [Server]
    private void BlowSmoke()
    {
        float out01 = Mathf.Min(_smoke, _smokeBlowPerSecond * Time.fixedDeltaTime);
        _smoke -= out01;
        if (_smoke < 0.001f) _smoke = 0f;
        PushSmoke();
        BakeOven ov = OvenAtVent(Mouth.position, out bool closed);
        RibsRack meat = ov == null ? MeatInFront(Mouth.position, Mouth.forward) : null;
        string why = null;
        bool onMeat = false;
        if (ov != null) ov.ServerAddSmoke(out01 * _smokeToOven);
        else if (meat != null) onMeat = meat.ServerSmokeDirect(out01, out why);
        if (Time.time >= _smokeLogAt)
        {
            _smokeLogAt = Time.time + 2f;
            Debug.Log(ov != null ? $"[Пылесос] дым в духовку «{ov.name}» — в баке {_smoke * 100f:0}%"
                     : onMeat ? $"[Пылесос] дым на «{meat.name}» — дымок {meat.Smoke01 * 100f:0}%, в баке {_smoke * 100f:0}%"
                     : meat != null ? $"[Пылесос] «{meat.name}»: {why}"
                     : $"[Пылесос] дым уходит в воздух — направь дуло на рёбра/брискет (до {SmokeMeatReach:0.0} м) или в открытый люк (в баке {_smoke * 100f:0}%)", this);
        }
    }

    [Tooltip("Дым на мясо: дуло смотрит на рёбра/брискет ближе этого, м (растёт с масштабом мяса)")]
    [SerializeField] private float _smokeMeatReach = 2.5f;
    private static readonly Collider[] _meatHits = new Collider[24];

    private float SmokeMeatReach => Mathf.Max(_smokeMeatReach, 0.8f * CatCooks.Food.MeatCutSettings.WorldScale);

    /// <summary>Рёбра/брискет перед дулом (конус ~60°), ближайшие.</summary>
    private RibsRack MeatInFront(Vector3 mouth, Vector3 dir)
    {
        float reach = SmokeMeatReach;
        int n = Physics.OverlapSphereNonAlloc(mouth + dir * reach * 0.5f, reach * 0.6f, _meatHits, ~0, QueryTriggerInteraction.Ignore);
        RibsRack best = null; float bd = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Collider c = _meatHits[i];
            RibsRack r = c != null ? c.GetComponentInParent<RibsRack>() : null;
            if (r == null) continue;
            Vector3 p = c.ClosestPoint(mouth);
            Vector3 to = p - mouth;
            float d = to.magnitude;
            if (d > reach) continue;
            if (d > 0.05f && Vector3.Dot(to / d, dir) < 0.5f) continue;
            if (d < bd) { bd = d; best = r; }
        }
        return best;
    }

    private void PushSmoke()
    {
        byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(_smoke) * 255f);
        if (b != _smokeNet.Value) _smokeNet.Value = b;
    }

    /// <summary>Клиент: струйка дыма от костра в дуло / из дула.</summary>
    private void DrawSmoke(Transform m, bool suck)
    {
        if (_swirl == null) return;
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        if (suck)
        {
            BonfirePit pit = SmokeSource(m.position, out float k);
            if (pit == null || Smoke01 >= 0.999f) return;
            for (int i = 0; i < 2; i++)
            {
                Vector3 from = pit.AirPoint + Vector3.up * Random.Range(0.3f, 0.9f) + Random.insideUnitSphere * 0.2f;
                Vector3 to = m.position - from;
                float life = Mathf.Clamp(to.magnitude / 2.5f, 0.15f, 1f);
                ep.position = from;
                ep.velocity = to / life;
                ep.startLifetime = life;
                ep.startSize = Random.Range(0.06f, 0.14f);
                ep.startColor = _smokeColor;
                _swirl.Emit(ep, 1);
            }
        }
        else if (Smoke01 > 0.001f)
        {
            for (int i = 0; i < 2; i++)
            {
                ep.position = m.position + m.forward * 0.05f;
                ep.velocity = (m.forward + Random.insideUnitSphere * 0.25f) * 2.2f + Vector3.up * 0.3f;
                ep.startLifetime = Random.Range(0.6f, 1.2f);
                ep.startSize = Random.Range(0.06f, 0.15f);
                ep.startColor = _smokeColor;
                _swirl.Emit(ep, 1);
            }
        }
    }

    // ───────────────────────── ВИХРЬ ─────────────────────────

    /// <summary>Воздушный вихрь у раструба — локально у каждого клиента
    /// по синхронизированному режиму.</summary>
    private void DrawSwirl()
    {
        if (_swirl == null || !IsActive) return;

        _swirlCarry += 60f * Time.deltaTime;
        int count = Mathf.FloorToInt(_swirlCarry);
        if (count <= 0) return;
        _swirlCarry -= count;
        if (count > 8) count = 8;

        Transform m = Mouth;
        bool suck = Mode == VacuumMode.Suck;
        DrawSmoke(m, suck);

        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();

        for (int i = 0; i < count; i++)
        {
            // При всасывании частицы рождаются далеко и летят В раструб,
            // при выдуве — наоборот. Так сразу видно, какой режим включён.
            float d = suck ? Random.Range(0.3f, _range * 0.7f) : 0.1f;
            Vector3 spread = Random.insideUnitSphere * (suck ? d * 0.35f : 0.05f);

            ep.position = m.position + m.forward * d + spread;
            ep.startSize = Random.Range(0.02f, 0.05f);
            ep.startLifetime = suck ? d / 4f : Random.Range(0.2f, 0.5f);
            ep.startColor = new Color(0.85f, 0.9f, 1f, Random.Range(0.15f, 0.4f));
            ep.velocity = suck ? -m.forward * 4f : m.forward * 6f;

            _swirl.Emit(ep, 1);
        }
    }

    private void CreateSwirl()
    {
        GameObject go = new GameObject("VacuumSwirl (auto)");

        // ВЫКЛЮЧЕННЫМ: система играет сразу при AddComponent, и часть свойств
        // на играющей менять нельзя.
        go.SetActive(false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = 500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startSize = 0.03f;
        main.startLifetime = 0.5f;
        main.gravityModifier = 0f;

        // При сцене далеко от нуля Automatic отключил бы систему целиком:
        // её трансформ вне экрана, хотя частицы рядом с игроком.
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = false;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Particles/Standard Unlit")
                 ?? Shader.Find("Sprites/Default");
        if (sh != null) r.material = new Material(sh) { name = "VacuumSwirl (auto)" };

        go.SetActive(true);
        ps.Play();
        _swirl = ps;
    }

    // ───────────────────────── ОТЛАДКА ─────────────────────────

    private void OnGUI()
    {
        if (!_debugHud || !base.IsOwner) return;

        GUIStyle st = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 14,
            richText = true
        };

        bool compressor = _ignoreCompressor || (_station != null && _station.IsRunning);

        string mode = !compressor ? "стоит"
                    : Mode == VacuumMode.Suck ? "ВСАСЫВАЕТ" : "ВЫПЛЁВЫВАЕТ";

        st.normal.textColor = !compressor ? new Color(1f, 0.6f, 0f) : Color.green;

        string hint = _station == null ? "  БАЗА НЕ НАЙДЕНА"
                    : !compressor ? "  компрессор выключен (F по кнопке на корпусе)"
                    : "";

        GUI.Box(new Rect(12f, 282f, 660f, 62f),
                $"Пылесос → {mode}{hint}\n" +
                $"в мешке {Stored} / {_capacity}   держит: " +
                $"{(_stuck.Value != null ? _stuck.Value.name : "—")}   " +
                $"«{_modeKey}» — сосать/плевать\n" +
                $"дым в баке {Smoke01 * 100f:0}%  " + SmokeHudHint(), st);
    }

    private string SmokeHudHint()
    {
        Transform m = _mouth != null ? _mouth : transform;
        if (SmokeSource(m.position, out _) != null)
            return Smoke01 >= 0.999f ? "<color=#7CFC7C>бак полный ✔</color> — открой люк духовки, R → выдувать, дуло к люку"
                                     : "<color=#7CFC7C>в зоне костра ✔</color> — режим «сосать» набирает дым";
        return Smoke01 > 0.02f ? "(R → выдувать: прямо на рёбра/брискет или в открытый люк)" : "(подойди с дулом к горящему костру — в любом месте рядом с огнём)";
    }

    private void OnDrawGizmosSelected()
    {
        Transform m = _mouth != null ? _mouth : transform;

        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
        Gizmos.DrawLine(m.position, m.position + m.forward * _range);

        // Конус всасывания: видно, куда надо целиться.
        float r = Mathf.Tan(_coneAngle * Mathf.Deg2Rad) * _range;
        Gizmos.DrawWireSphere(m.position + m.forward * _range, r);
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(m.position, _swallowDistance);
    }
}
