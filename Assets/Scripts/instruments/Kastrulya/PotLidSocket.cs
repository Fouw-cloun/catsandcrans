using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// ГНЕЗДО КРЫШКИ НА ПОСУДЕ. Вешается на кастрюлю.
///
/// Крышка — САМОСТОЯТЕЛЬНЫЙ сетевой предмет, который прицепляется к гнезду,
/// а не дочерний объект модели. Из этого следует главное: крышка одна и та же
/// и когда лежит на кастрюле, и когда валяется на полу — её не пересоздают,
/// а значит нечему рассинхронизироваться.
///
/// СИНХРОНИЗАЦИЯ: в SyncVar лежит ССЫЛКА на прицепленную крышку. Все клиенты
/// в любой момент знают, есть крышка или нет, и какая именно. Раньше это был
/// флаг плюс спавн нового предмета — и при срыве старая крышка удалялась,
/// а новая появлялась, что при лаге давало моргание.
///
/// Кто ведёт крышку к гнезду: сама крышка в PotLidItem, локально у каждого.
/// По сети позиция прицепленной крышки не гоняется вовсе.
/// </summary>
public class PotLidSocket : NetworkBehaviour
{
    [Header("Гнездо")]
    [Tooltip("Куда садится крышка. Пустышка на горловине кастрюли.")]
    [SerializeField] private Transform _socket;

    [Tooltip("СВОЯ крышка этой кастрюли (объект в сцене). Клавиша U и «накрыть» берут именно её.")]
    [SerializeField] private PotLidItem _assignedLid;

    [Tooltip("Префаб крышки: если своей крышки в сцене нет, при старте создаётся из него " +
             "и становится своей крышкой этой кастрюли.")]
    [SerializeField] private GameObject _startingLidPrefab;

    [Tooltip("Накрыть кастрюлю при старте сцены. Выкл (по умолчанию) — крышка лежит рядом.")]
    [SerializeField] private bool _attachOnStart = false;

    [Header("Живая крышка")]
    [Tooltip("Подпрыгивание крышки при кипении (доля от дребезга перегрева)")]
    [Range(0f, 2f)][SerializeField] private float _boilJiggle = 0.6f;
    [Tooltip("Покачивание крышки при кипении, градусы")]
    [Range(0f, 10f)][SerializeField] private float _boilTilt = 2.5f;

    [Header("Срыв давлением")]
    [Tooltip("С какой силой улетает крышка")]
    [SerializeField] private float _blowForce = 9f;

    [Tooltip("Разброс направления от вертикали, градусы. Небольшой: крышку " +
             "выбивает паром прямо вверх, а не отбрасывает вбок.")]
    [Range(0f, 45f)][SerializeField] private float _blowSpread = 8f;

    [Tooltip("Как сильно закручивает")]
    [SerializeField] private float _blowTorque = 8f;

    [Header("Дребезг перед разрывом")]
    [Tooltip("Насколько подпрыгивает крышка на перегреве, метры")]
    [SerializeField] private float _rattle = 0.008f;

    /// <summary>Прицепленная крышка. Null — кастрюля открыта.</summary>
    private readonly SyncVar<NetworkObject> _lid = new SyncVar<NetworkObject>();

    private CatCooks.Food.IPotBody _body;

    public bool HasLid => _lid.Value != null;
    public NetworkObject Lid => _lid.Value;
    public Transform Socket => _socket != null ? _socket : transform;
    /// <summary>Своя крышка этой кастрюли (может быть null).</summary>
    public PotLidItem AssignedLid => _assignedLid;

    private float _sizeK = -1f;
    /// <summary>Во сколько раз кастрюля больше «обычной» (30 см высоты) — для амплитуд.</summary>
    private float SizeK
    {
        get
        {
            if (_sizeK > 0f) return _sizeK;
            bool any = false; Bounds b = default(Bounds);
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || !PotBodyFilter.IsBody(r, transform)) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            _sizeK = any ? Mathf.Max(1f, b.size.y / 0.3f) : 1f;
            return _sizeK;
        }
    }

    /// <summary>
    /// Куда крышке становиться прямо сейчас. Зовётся самой крышкой каждый кадр.
    /// На перегреве добавляется дребезг — единственное предупреждение,
    /// которое видно без градусника.
    /// </summary>
    public void GetLidPose(out Vector3 pos, out Quaternion rot)
    {
        Transform s = Socket;
        pos = s.position;
        rot = s.rotation;

        if (_body == null) return;
        float k = SizeK;

        // Кипит — крышка «дышит»: подпрыгивает и покачивается, сильнее с температурой.
        if (_body.Condition == CatCooks.Food.PotCondition.Boiling || _body.Condition == CatCooks.Food.PotCondition.Warm)
        {
            float heat = Mathf.Clamp01(Mathf.InverseLerp(80f, 100f, _body.TemperatureC));
            if (_body.Condition == CatCooks.Food.PotCondition.Boiling) heat = 1f;
            float tb = Time.time;
            float hop = Mathf.Pow(Mathf.Abs(Mathf.Sin(tb * 7.3f)), 6f) + 0.4f * Mathf.Pow(Mathf.Abs(Mathf.Sin(tb * 11.1f + 1f)), 8f);
            pos += s.up * (hop * _rattle * k * _boilJiggle * heat);
            rot = rot * Quaternion.Euler(Mathf.Sin(tb * 5.1f) * _boilTilt * heat * hop, 0f, Mathf.Cos(tb * 4.3f) * _boilTilt * heat * hop);
            return;
        }
        if (_body.Condition != CatCooks.Food.PotCondition.Overheat) return;

        float p = _body.Pressure01;
        float t = Time.time * 30f;

        pos += new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f,
                           Mathf.Abs(Mathf.Sin(t * 1.7f)) * 0.6f,
                           Mathf.PerlinNoise(t, 4.1f) - 0.5f) * (_rattle * k * p * 3f);
        rot = rot * Quaternion.Euler((Mathf.PerlinNoise(t, 8f) - 0.5f) * 12f * p, 0f, (Mathf.PerlinNoise(t, 9f) - 0.5f) * 12f * p);
    }

    /// <summary>Поза С УЧЁТОМ модели крышки: плашмя, ободом в точку гнезда.</summary>
    public void GetLidPose(PotLidItem lid, out Vector3 pos, out Quaternion rot)
    {
        GetLidPose(out pos, out rot);
        if (lid != null) lid.ApplySeat(ref pos, ref rot);
    }

    private void Awake() => _body = GetComponent<CatCooks.Food.IPotBody>();

    /// <summary>
    /// Страховка: гнездо само держит свою крышку на месте у всех. Обычно её ведёт
    /// сама крышка (PotLidItem), но если она не узнала о прицепке (не заспавнена,
    /// пропустила OnChange) — крышка оставалась там, где лежала, и кастрюля
    /// «закрыта» без крышки на ней.
    /// </summary>
    private NetworkObject _lidItemNob;
    private PotLidItem _lidItem;

    private void LateUpdate()
    {
        NetworkObject nob = _lid.Value;
        if (nob == null) return;
        if (_lidItemNob != nob) { _lidItemNob = nob; _lidItem = nob.GetComponent<PotLidItem>(); }
        GetLidPose(_lidItem, out Vector3 p, out Quaternion r);
        nob.transform.SetPositionAndRotation(p, r);
        Rigidbody rb = nob.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic) { rb.isKinematic = true; }
    }

    private void OnDrawGizmos()
    {
        Transform s = Socket;
        if (s == null) return;
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(s.position, 0.04f);
        Gizmos.DrawLine(s.position, s.position + s.up * 0.25f);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Своей крышки в сцене нет, но есть префаб — создаём и делаем своей.
        if (_assignedLid == null && _startingLidPrefab != null)
        {
            Vector3 side = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            Vector3 at = _attachOnStart ? Socket.position : transform.position + side * (1.2f * 0.3f * SizeK) + Vector3.up * 0.1f * SizeK;
            GameObject go = Instantiate(_startingLidPrefab, at, Socket.rotation);
            PotLidItem lid = go.GetComponent<PotLidItem>();
            if (lid == null)
            {
                Debug.LogError($"[Крышка] на «{_startingLidPrefab.name}» нет PotLidItem", this);
                Destroy(go);
                return;
            }
            // Глушим физику ДО спавна, если сразу на кастрюлю: внутри коллайдера кастрюли
            // даже кадр динамики вышвырнул бы её прочь.
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null && _attachOnStart) rb.isKinematic = true;
            base.Spawn(go);
            _assignedLid = lid;
        }

        if (_attachOnStart && _assignedLid != null) ServerAttach(_assignedLid);
    }

    // ───────────────────────── СЕРВЕР ─────────────────────────

    /// <summary>Прицепить крышку к гнезду.</summary>
    [Server]
    public bool ServerAttach(PotLidItem lid)
    {
        if (lid == null || HasLid) return false;

        _lid.Value = lid.NetworkObject;
        lid.ServerAttachTo(this);
        return true;
    }

    /// <summary>Отцепить крышку: она становится обычным предметом на полу.</summary>
    [Server]
    public PotLidItem ServerDetach()
    {
        NetworkObject nob = _lid.Value;
        if (nob == null) return null;

        _lid.Value = null;

        PotLidItem lid = nob.GetComponent<PotLidItem>();
        if (lid != null) lid.ServerDetach();
        return lid;
    }

    /// <summary>Снять крышку руками. Это и есть спасение от разрыва:
    /// давление тут же начинает стравливаться.</summary>
    [Server]
    public void ServerTakeLid()
    {
        PotLidItem lid = ServerDetach();
        if (lid != null) Debug.Log("[Крышка] снята руками, давление стравливается");
    }

    /// <summary>Сорвать крышку давлением — с импульсом и вращением.</summary>
    [Server]
    public void ServerBlowOff(int seed)
    {
        PotLidItem lid = ServerDetach();
        if (lid == null) return;

        Rigidbody rb = lid.GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogWarning("[Крышка] на префабе нет Rigidbody — улетать нечему", this);
            return;
        }

        // Подстраховка: если физику по какой-то причине не вернули, импульс
        // к кинематическому телу просто не применится, и крышка повиснет.
        if (rb.isKinematic) rb.isKinematic = false;

        System.Random rnd = new System.Random(seed);
        float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
        float s = Mathf.Tan(_blowSpread * Mathf.Deg2Rad) * (float)rnd.NextDouble();

        // Вверх — основное направление, вбок только лёгкий разброс.
        Vector3 dir = (Vector3.up + new Vector3(Mathf.Cos(a) * s, 0f, Mathf.Sin(a) * s))
                      .normalized;

        // Скорость обнуляем: крышка могла лежать на кастрюле, которую несут,
        // и унаследованное движение сбило бы ей траекторию.
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        rb.AddForce(dir * _blowForce, ForceMode.Impulse);
        rb.AddTorque(new Vector3((float)rnd.NextDouble() - 0.5f,
                                 (float)rnd.NextDouble() - 0.5f,
                                 (float)rnd.NextDouble() - 0.5f) * _blowTorque,
                     ForceMode.Impulse);

        Debug.Log("[Крышка] сорвана давлением");
    }
}
