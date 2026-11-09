using FishNet.Object;
using UnityEngine;

/// <summary>
/// ПОДСТАВКА ПОД КОТЁЛ. Четыре колонны, между ними кострище. Делает две вещи:
/// ловит котёл «магнитом» и переливает в него жар от костра.
///
/// ═══ МАГНИТ — ЭТО ДОВОДКА, А НЕ УДЕРЖАНИЕ ═══
///
/// Котёл никто не приклеивает и не делает кинематическим. Подставка просто
/// ДОВОДИТ его на место, когда он оказался рядом и почти остановился: ставит
/// ровно по центру, гасит скорость, выравнивает крен. Держат его дальше сами
/// колонны, обычной физикой.
///
/// Так сделано, чтобы не воевать с краном. Приклеенный котёл пришлось бы
/// специально отпускать, кран должен был бы знать про подставку, подставка про
/// кран — и любое расхождение оставляло бы котёл висеть в воздухе. А доводка
/// ни с чем не спорит: кран поднял, котёл вышел из радиуса, связь отпала сама.
///
/// ПРОВЕРКА СКОРОСТИ ОБЯЗАТЕЛЬНА. Без неё магнит хватал бы котёл прямо на
/// крюке, пока крановщик ещё несёт его над площадкой, и дёргал бы к себе.
/// Ловим только то, что уже опускается и почти встало.
///
/// ═══ ПОЧЕМУ ТЕПЛО ИДЁТ ЧЕРЕЗ ЭТОТ СКРИПТ, А НЕ ИЗ КОСТРА ═══
///
/// Костёр считает своё горение и отдаёт наружу одно число — градусы в секунду.
/// Он не знает ни про котёл, ни про то, что над ним вообще что-то стоит.
/// Подставка — единственное место, где эти двое встречаются. Слепи их вместе,
/// и костёр стало бы нельзя развести без котла, а котёл — погреть чем-то ещё.
///
/// Ровно так же устроена плита: StoveBurner отдельно, PotThermal отдельно.
/// </summary>
public class FireCauldronStand : NetworkBehaviour
{
    [Header("Связи")]
    [Tooltip("Костёр под подставкой. Пусто — ищется в детях.")]
    [SerializeField] private Firebox _firebox;
    [Tooltip("Новое кострище (Bonfire Pit). Если задано — тепло берётся от него, а не от Firebox.")]
    [SerializeField] private CatCooks.Food.Tools.BonfirePit _bonfire;

    [Tooltip("Точка, куда встаёт дно котла — центр между колоннами")]
    [SerializeField] private Transform _seat;

    [Header("Магнит")]
    [Tooltip("В каком радиусе от Seat ловится котёл")]
    [SerializeField] private float _snapRadius = 1.8f;

    [Tooltip("МАКСИМАЛЬНАЯ СКОРОСТЬ КОТЛА, ПРИ КОТОРОЙ ЕГО ЛОВИМ, м/с.\n\n" +
             "Без этого порога магнит выдёргивал бы котёл прямо с крюка крана, " +
             "пока крановщик ещё несёт его над площадкой.")]
    [SerializeField] private float _maxCatchSpeed = 1.2f;

    [Tooltip("Слой посуды")]
    [SerializeField] private LayerMask _potMask = ~0;

    [Tooltip("Как часто искать котёл, сек. Каждый кадр незачем: ставят и " +
             "снимают его редко.")]
    [SerializeField] private float _scanInterval = 0.3f;

    [Header("Передача тепла")]
    [Tooltip("КАКАЯ ДОЛЯ ЖАРА КОСТРА ДОХОДИТ ДО КОТЛА.\n\n" +
             "Костёр греет во все стороны, котёл ловит лишь то, что под ним. " +
             "0.6–0.8 разумно. Единица означала бы идеальный теплообмен, при " +
             "котором костёр перестаёт отличаться от плиты.")]
    [Range(0.1f, 1f)][SerializeField] private float _coupling = 0.7f;

    [Header("Отладка")]
    [SerializeField] private bool _debugLog;

    private CookingVessel _vessel;
    private PotThermal _thermal;
    private float _nextScan;
    private readonly Collider[] _hits = new Collider[8];

    public Firebox Fire => _firebox;
    public CookingVessel Vessel => _vessel;
    public bool IsFree => _vessel == null;
    public Transform Seat => _seat != null ? _seat : transform;

    private void Awake()
    {
        if (_firebox == null) _firebox = GetComponentInChildren<Firebox>();
        if (_bonfire == null) _bonfire = GetComponentInParent<CatCooks.Food.Tools.BonfirePit>();
        if (_bonfire != null) return;     // тепло — от нового кострища, Firebox не нужен

        if (_firebox == null)
            Debug.LogWarning($"[Подставка] «{name}»: не найден Firebox — греть " +
                             "будет нечем.", this);
    }

    private void Update()
    {
        if (!base.IsServerInitialized) return;

        float dt = Mathf.Min(Time.deltaTime, 0.1f);

        ScanForVessel();

        if (_thermal == null || (_firebox == null && _bonfire == null)) return;

        float source = _bonfire != null ? _bonfire.DegreesPerSecond : _firebox.DegreesPerSecond;
        float rate = source * _coupling;
        if (rate <= 0f) return;

        // Костёр умеет ТОЛЬКО греть. Остывание — забота PotThermal, и это
        // важно: именно из-за него температура всё время сползает вниз, а
        // удержание коридора становится работой.
        _thermal.ServerAddHeat(rate, dt);
    }

    // ───────────────────────── МАГНИТ ─────────────────────────

    [Server]
    private void ScanForVessel()
    {
        if (Time.time < _nextScan) return;
        _nextScan = Time.time + _scanInterval;

        int n = Physics.OverlapSphereNonAlloc(Seat.position, _snapRadius, _hits,
                                              _potMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (_hits[i] == null) continue;

            CookingVessel v = _hits[i].GetComponentInParent<CookingVessel>();
            if (v == null) continue;

            if (v != _vessel)
            {
                CraneLoad suspended = v.GetComponent<CraneLoad>();
                if (suspended != null && suspended.IsAttached) return;
                Rigidbody rb = v.GetComponent<Rigidbody>();

                // Летит или несут краном — не трогаем.
                if (rb != null && !rb.isKinematic && rb.linearVelocity.magnitude > _maxCatchSpeed)
                    return;

                Snap(v);
            }
            return;
        }

        if (_vessel != null) Bind(null);
    }

    /// <summary>Доводка на место: ровно по центру, без крена, без скорости.
    /// Котёл должен ВСТАТЬ, а не плюхнуться и укатиться — тем более с кипятком.</summary>
    [Server]
    private void Snap(CookingVessel v)
    {
        Transform t = v.transform;
        Collider col = v.GetComponentInChildren<Collider>();

        // Насколько центр объекта выше его дна — на столько и поднимаем.
        float lift = col != null ? t.position.y - col.bounds.min.y : 0f;
        t.position = Seat.position + Vector3.up * lift;

        Vector3 fwd = t.forward;
        fwd.y = 0f;
        t.rotation = fwd.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(fwd.normalized, Vector3.up)
            : Quaternion.identity;

        Rigidbody rb = v.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        Bind(v);

        if (_debugLog)
            Debug.Log($"[Подставка] «{v.name}» встал на колонны", this);
    }

    /// <summary>Подписка на разрыв идёт здесь, в одном месте, а не в скане:
    /// подписаться надо ровно один раз, а скан бегает трижды в секунду и
    /// навесил бы полторы сотни обработчиков за минуту.</summary>
    private void Bind(CookingVessel v)
    {
        if (_thermal != null) _thermal.Ruptured -= OnRuptured;

        _vessel = v;
        _thermal = v != null ? v.GetComponent<PotThermal>() : null;

        if (_thermal != null) _thermal.Ruptured += OnRuptured;
    }

    /// <summary>Котёл разорвало. Отвязываемся — но КОСТЁР НЕ ТУШИМ: он про
    /// катастрофу наверху ничего не знает и знать не должен, дрова в нём горят
    /// сами по себе.</summary>
    private void OnRuptured()
    {
        if (!base.IsServerInitialized) return;

        Debug.LogWarning($"[Подставка] «{name}»: котёл разорвало", this);
        Bind(null);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireSphere(Seat.position, _snapRadius);
        Gizmos.DrawWireCube(Seat.position, new Vector3(0.4f, 0.05f, 0.4f));
    }
}
