using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// СВАРОЧНЫЙ АППАРАТ. Предмет в руке, чинит раскорёженную посуду.
///
///   ЛКМ (удержание) — горит дуга. Наводишь на разорванную кастрюлю и держишь,
///                     пока металл не выправится.
///
/// ЭТОТ ИНСТРУМЕНТ ЧИНИТ ТОЛЬКО ДЫРЫ (PotDamage). Форму кастрюли выправляет
/// отдельный инструмент — молоток (HammerItem). Раньше сварка умела и то, и
/// другое, но с появлением молотка это стало дублированием: два инструмента
/// не должны уметь одно и то же по-разному.
///
/// ЗАВАРИВАЕТСЯ ПО ОДНОЙ ДЫРЕ — ровно та, на которую наведён электрод.
/// Промахнулся мимо дыры — ничего не происходит.
///
/// ПОРЯДОК С МОЛОТКОМ НЕ ВАЖЕН. Можно сначала заварить все дыры на ещё
/// мятой кастрюле, можно сначала выправить форму дырявой — кастрюля
/// становится годной только тогда, когда закрыты и дыры, и форма, и решает
/// это PotThermal.ServerCheckFullyRepaired(), а не тот инструмент, что
/// сработал последним.
///
/// СЕТЬ: горение дуги — один флаг, цель — один объект. Искры и свет каждый
/// клиент рисует локально; чинит только сервер.
///
/// Требует: NetworkObject, NetworkTransform (Client Authoritative), Collider,
/// Rigidbody.
/// </summary>
public class WeldingTorchItem : InteractableItem
{
    [Header("Положение в руке")]
    [SerializeField] private Vector3 _holdPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    [Header("Аппарат")]
    [Tooltip("Коробка сварочника. Пусто = найдётся по сцене при старте.")]
    [SerializeField] private WeldingStation _station;

    [Tooltip("Работать без включённого аппарата. Только для отладки.")]
    [SerializeField] private bool _ignoreStation;

    [Header("Электрод")]
    [Tooltip("На сколько секунд горения хватает одного электрода")]
    [Min(1f)][SerializeField] private float _electrodeSeconds = 12f;

    [Tooltip("Сколько секунд занимает перезарядка")]
    [SerializeField] private float _reloadSeconds = 1.6f;

    [Tooltip("Клавиша перезарядки. Работает рядом с коробкой.")]
    [SerializeField] private KeyCode _reloadKey = KeyCode.R;

    [Header("Горелка")]
    [Tooltip("Кончик горелки: оттуда бьют искры и светит дуга")]
    [SerializeField] private Transform _tip;

    [Tooltip("Дальность работы, метры")]
    [SerializeField] private float _range = 2.2f;

    [Tooltip("Слои, по которым работает сварка")]
    [SerializeField] private LayerMask _weldMask = ~0;

    [Header("Дуга")]
    [Tooltip("Свет дуги. Пусто — создастся сам.")]
    [SerializeField] private Light _arcLight;

    [SerializeField] private Color _arcColor = new Color(0.75f, 0.85f, 1f);
    [SerializeField] private float _arcIntensity = 6f;
    [SerializeField] private float _arcRange = 4f;

    [Header("Искры")]
    [SerializeField] private int _sparksPerBurst = 14;
    [SerializeField] private float _sparkSpeed = 4.5f;
    [SerializeField] private float _sparkInterval = 0.05f;

    [Tooltip("Пиковая яркость искр. Выше 1 уводит в HDR — с Bloom светятся.")]
    [Range(0.2f, 8f)][SerializeField] private float _sparkIntensity = 3f;

    [Header("Звук")]
    [SerializeField] private AudioSource _arcLoop;

    [Header("ОТЛАДКА (выключить в релизе)")]
    [SerializeField] private bool _debugHud = true;

    // ───────── сеть ─────────
    private readonly SyncVar<bool> _burning = new SyncVar<bool>(false);

    /// <summary>Что варим прямо сейчас. По этому объекту все рисуют искры.</summary>
    private readonly SyncVar<NetworkObject> _target = new SyncVar<NetworkObject>();

    /// <summary>Остаток электрода, 0..255. Ноль — надо перезаряжаться.</summary>
    private readonly SyncVar<byte> _electrode = new SyncVar<byte>(255);

    private readonly SyncVar<bool> _reloading = new SyncVar<bool>(false);

    private Transform _holderCam;
    private bool _lastHeld;
    private readonly RaycastHit[] _hits = new RaycastHit[8];

    private PotDamage _localDamage;

    /// <summary>Что мы УЖЕ отправили серверу. Сравнивать с _target.Value
    /// нельзя: SyncVar возвращается через круг, и до его прихода клиент слал
    /// бы CmdSetTarget каждый кадр.</summary>
    private NetworkObject _sentTarget;
    private Vector3 _hitPoint;
    private float _nextSpark;

    private float _charge = 1f;      // точный остаток электрода на сервере
    private float _reloadTime;
    private bool _arcRegistered;

    private string _status = "—";
    private Color _statusColor = Color.gray;

    /// <summary>Горит ли дуга. У владельца берём локальный флаг: реакция должна
    /// быть в тот же кадр, а SyncVar приезжает через круг до сервера.</summary>
    public bool IsBurning => HasPower && HasElectrode && !_reloading.Value &&
                             (base.IsOwner ? (_lastHeld || _burning.Value) : _burning.Value);

    /// <summary>Аппарат включён. Без него держатель просто железка.</summary>
    public bool HasPower => _ignoreStation || (_station != null && _station.IsRunning);

    public bool HasElectrode => _electrode.Value > 0;
    public float Electrode01 => _electrode.Value / 255f;
    public bool IsReloading => _reloading.Value;

    /// <summary>Работает в связке с аппаратом: F по кнопке на корпусе должен
    /// проходить, не выпуская держатель из рук.</summary>
    public override bool AllowInteractWhileHeld => true;

    public Transform Tip => _tip != null ? _tip : transform;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
        if (_tip == null) _tip = transform;
        if (_station == null) _station = FindFirstObjectByType<WeldingStation>();
        EnsureLight();
    }

    private void EnsureLight()
    {
        if (_arcLight != null) return;

        GameObject go = new GameObject("ArcLight");
        go.transform.SetParent(Tip, false);
        _arcLight = go.AddComponent<Light>();
        _arcLight.type = LightType.Point;
        _arcLight.shadows = LightShadows.None;
        _arcLight.enabled = false;
    }

    // ───────────────────────── ЦИКЛ ─────────────────────────

    private void Update()
    {
        if (base.IsServerInitialized) ServerTick();
        if (base.IsOwner) OwnerTick();
        UpdateArcVisual();
    }

    [Server]
    private void ServerTick()
    {
        // Сторож: дуга погасла, а цель висит — снимаем. Чинит и обрыв связи,
        // и выпадение горелки из рук.
        if (_target.Value != null && !IsBurning) _target.Value = null;

        if (_reloading.Value)
        {
            _reloadTime -= Time.deltaTime;
            if (_reloadTime > 0f) return;

            _reloading.Value = false;

            // Ставим НОВЫЙ электрод: остаток снова полный, а визуал сам
            // вернёт ему полную длину — он ведётся от этого же значения.
            _charge = 1f;
            _electrode.Value = 255;

            Debug.Log($"[Сварка] вставлен новый электрод, в аппарате осталось " +
                      $"{(_station != null ? _station.Stock : 0)}");
            return;
        }

        if (!_burning.Value || !HasPower || !HasElectrode) return;

        // Электрод сгорает, пока горит дуга. Не когда просто держишь ЛКМ,
        // а именно когда аппарат включён и работа идёт.
        _charge -= Time.deltaTime / Mathf.Max(1f, _electrodeSeconds);
        _charge = Mathf.Max(0f, _charge);

        byte e = (byte)Mathf.RoundToInt(_charge * 255f);
        if (e != _electrode.Value && (e == 0 || Mathf.Abs(e - _electrode.Value) >= 4))
            _electrode.Value = e;

        if (_charge <= 0f)
        {
            _burning.Value = false;
            _target.Value = null;
            Debug.Log("[Сварка] электрод сгорел — нужна перезарядка (R у аппарата)");
        }
    }

    private void OwnerTick()
    {
        if (Input.GetKeyDown(_reloadKey)) CmdReload();

        bool held = Input.GetMouseButton(0);
        if (held != _lastHeld)
        {
            _lastHeld = held;
            CmdSetBurning(held);
        }

        if (_reloading.Value) { SetStatus("меняю электрод...", Color.yellow); return; }

        if (!HasPower)
        {
            SetStatus("аппарат выключен — F по кнопке на корпусе",
                      new Color(1f, 0.6f, 0f));
            return;
        }

        if (!HasElectrode)
        {
            SetStatus($"электрод сгорел — подойди к аппарату и нажми {_reloadKey}",
                      new Color(1f, 0.5f, 0.2f));
            return;
        }

        if (!held)
        {
            _localDamage = null;
            _sentTarget = null;
            SetStatus("зажми ЛКМ", Color.gray);
            return;
        }
        if (_holderCam == null) { SetStatus("не найдена камера", Color.red); return; }

        NetworkObject found = FindTarget();

        if (found == null)
        {
            if (_sentTarget != null)
            {
                _localDamage = null;
                _sentTarget = null;
                CmdSetTarget(null);
            }
            SetStatus(DescribeMiss(), new Color(1f, 0.6f, 0f));
            return;
        }

        if (_sentTarget != found)
        {
            _sentTarget = found;
            CmdSetTarget(found);
        }

        // Точку прицела шлём КАЖДЫЙ КАДР вместе с командой: именно она
        // выбирает, какую из дыр варить. Без неё сервер не знал бы, куда
        // смотрит игрок, и заваривал бы всегда одну и ту же.
        CmdWeld(_hitPoint);

        SetStatus(DescribeWork(), Color.green);
    }

    /// <summary>Что именно сейчас варится и насколько.</summary>
    private string DescribeWork()
    {
        if (_localDamage == null) return "готово";

        if (_localDamage.IsSealed)
            return "дыр не осталось — форму правит молоток";

        int idx = _localDamage.FindHoleNear(_hitPoint, out float dist);
        if (idx < 0) return "дыр не осталось";

        // Промах мимо дыры показываем ЧИСЛОМ: «наведись точнее» не говорит,
        // на сколько именно промазал, и игрок водит электродом вслепую.
        if (dist > 0.10f)
            return $"мимо дыры на {dist * 100f:0} см — веди электрод точнее " +
                   $"(осталось дыр: {_localDamage.OpenHoles})";

        return $"ВАРИТ дыру — {_localDamage.WeldingProgress * 100f:0}%, " +
               $"осталось {_localDamage.OpenHoles} из {_localDamage.TotalHoles}";
    }

    /// <summary>
    /// Почему цель не нашлась. Причины выглядят одинаково — «не варит», —
    /// поэтому разбираем их явно: иначе забытый слой ищется вслепую.
    /// </summary>
    private string DescribeMiss()
    {
        if (!Physics.Raycast(_holderCam.position, _holderCam.forward,
                             out RaycastHit h, _range, ~0, QueryTriggerInteraction.Ignore))
            return "наведись на то, что нужно заварить";

        PotDamage dmg = h.collider.GetComponentInParent<PotDamage>();
        int layer = h.collider.gameObject.layer;

        if (dmg == null)
            return $"«{h.collider.name}» варить нельзя — на нём нет PotDamage";

        if (((1 << layer) & _weldMask.value) == 0)
            return $"«{h.collider.name}» на слое «{LayerMask.LayerToName(layer)}», " +
                   "а его нет в Weld Mask";

        if (dmg.IsSealed)
            return $"«{h.collider.name}»: дыр нет, форму правит молоток";

        return "наведись на то, что нужно заварить";
    }

    /// <summary>
    /// Что под прицелом. Годится и пробитая посуда, и смятая — раньше сюда
    /// проходил только DeformableMesh, и дырявая, но не помятая кастрюля
    /// вообще не считалась целью.
    /// </summary>
    private NetworkObject FindTarget()
    {
        int n = Physics.RaycastNonAlloc(_holderCam.position, _holderCam.forward, _hits,
                                        _range, _weldMask, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        NetworkObject best = null;
        PotDamage bestDamage = null;

        for (int i = 0; i < n; i++)
        {
            RaycastHit h = _hits[i];
            if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
            if (h.distance >= nearest) continue;

            PotDamage dmg = h.collider.GetComponentInParent<PotDamage>();
            if (dmg == null || !dmg.IsSpawned || dmg.IsSealed) continue;

            NetworkObject no = dmg.NetworkObject;
            if (no == null) continue;

            nearest = h.distance;
            best = no;
            bestDamage = dmg;

            // Точка попадания — именно то место стенки, куда смотрит игрок.
            // По ней и выбирается конкретная дыра.
            _hitPoint = h.point;
        }

        _localDamage = bestDamage;
        return best;
    }

    // ───────────────────────── СЕРВЕР ─────────────────────────

    /// <summary>
    /// Перезарядка. Только рядом с коробкой: за электродом надо подойти,
    /// а значит коробку приходится подтаскивать к работе.
    /// </summary>
    [ServerRpc(RequireOwnership = true)]
    private void CmdReload()
    {
        if (_reloading.Value) return;
        if (_electrode.Value >= 250) return;      // и так почти целый

        if (_station == null)
        {
            Debug.LogWarning("[Сварка] аппарат не найден — перезарядиться не у чего", this);
            return;
        }

        float d = Vector3.Distance(transform.position, _station.transform.position);
        if (d > _station.ReloadRange)
        {
            Debug.Log($"[Сварка] до аппарата {d:0.0} м, надо ближе " +
                      $"{_station.ReloadRange:0.0} м");
            return;
        }

        if (!_station.ServerTakeElectrode())
        {
            Debug.Log("[Сварка] электроды в аппарате кончились");
            return;
        }

        _burning.Value = false;
        _target.Value = null;
        _reloading.Value = true;
        _reloadTime = _reloadSeconds;
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdSetBurning(bool value)
    {
        if (_burning.Value == value) return;
        _burning.Value = value;
        if (!value) _target.Value = null;
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdSetTarget(NetworkObject target) => _target.Value = target;

    /// <summary>
    /// Варить. Клиент зовёт каждый кадр, сервер прибавляет прогресс по своему
    /// времени — так частота кадров у игрока ни на что не влияет.
    /// </summary>
    [ServerRpc(RequireOwnership = true)]
    private void CmdWeld(Vector3 hitPoint)
    {
        NetworkObject t = _target.Value;
        if (t == null || !_burning.Value) return;

        PotDamage dmg = t.GetComponent<PotDamage>()
                     ?? t.GetComponentInChildren<PotDamage>()
                     ?? t.GetComponentInParent<PotDamage>();

        if (dmg == null) { _target.Value = null; return; }

        float dt = Mathf.Min(Time.deltaTime, 0.1f);
        dmg.ServerWeldAt(hitPoint, dt);   // сама PotDamage зовёт координатор,
                                          // когда закрывается последняя дыра
    }

    // ───────────────────────── ВИЗУАЛ ─────────────────────────

    /// <summary>Свет и искры — локально у всех по синхронизированной цели.</summary>
    private void UpdateArcVisual()
    {
        bool on = _burning.Value && _target.Value != null;

        // Дугу регистрируем в общем списке — по нему каждый игрок сам считает,
        // насколько его слепит. По сети для этого ничего гонять не нужно.
        if (on != _arcRegistered)
        {
            _arcRegistered = on;

            if (on)
            {
                WeldingFlashOverlay.RegisterArc(Tip);
                IgnitionRegistry.Register(Tip);      // дуга поджигает газ
            }
            else
            {
                WeldingFlashOverlay.UnregisterArc(Tip);
                IgnitionRegistry.Unregister(Tip);
            }
        }

        if (_arcLight != null)
        {
            _arcLight.enabled = on;
            if (on)
            {
                // Дуга мерцает — ровный свет выглядит как фонарик, а не сварка.
                float f = 0.75f + Mathf.PerlinNoise(Time.time * 25f, 0f) * 0.5f;
                _arcLight.color = _arcColor;
                _arcLight.intensity = _arcIntensity * f;
                _arcLight.range = _arcRange;
            }
        }

        if (_arcLoop != null)
        {
            if (on && !_arcLoop.isPlaying) _arcLoop.Play();
            else if (!on && _arcLoop.isPlaying) _arcLoop.Stop();
        }

        if (!on || Time.time < _nextSpark) return;
        _nextSpark = Time.time + _sparkInterval;

        // Искры от кончика горелки вниз и в стороны — как от настоящей дуги.
        Vector3 at = Tip.position;
        SparkBurst.Play(at, Vector3.down * 0.6f + Tip.forward, 55f,
                        _sparksPerBurst, _sparkSpeed,
                        new Color(0.85f, 0.92f, 1f), new Color(1f, 0.6f, 0.2f),
                        _sparkIntensity);
    }

    // ───────────────────────── ПРЕДМЕТ ─────────────────────────

    private void OnDestroy()
    {
        // Иначе в списке дуг останется мёртвая ссылка и всех будет слепить
        // от несуществующей горелки.
        if (!_arcRegistered) return;
        WeldingFlashOverlay.UnregisterArc(Tip);
        IgnitionRegistry.Unregister(Tip);
    }

    public override void ServerOnDrop(PlayerHand hand, Vector3 dir, float force = 0f)
    {
        _burning.Value = false;
        _target.Value = null;
        _lastHeld = false;
        _holderCam = null;
        _sentTarget = null;
        _localDamage = null;
        base.ServerOnDrop(hand, dir, force);
    }

    public override void UpdateHeldPose(Transform handAnchor)
    {
        // Камеру берём через держателя — так это точно его камера.
        if (_holderCam == null)
        {
            FirstPersonController fpc = handAnchor.GetComponentInParent<FirstPersonController>();
            if (fpc != null)
            {
                Camera c = fpc.GetComponentInChildren<Camera>(true);
                if (c != null) _holderCam = c.transform;
            }
        }

        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);
        transform.SetPositionAndRotation(pos, rot);
    }

    // ───────────────────────── ОТЛАДКА ─────────────────────────

    private void SetStatus(string text, Color color)
    {
        _status = text;
        _statusColor = color;
    }

    /// <summary>Луч прицела и точка попадания прямо в сцене: видно, куда
    /// на самом деле смотрит электрод, а не куда кажется.</summary>
    private void OnDrawGizmos()
    {
        if (!_debugHud || !Application.isPlaying || _holderCam == null) return;

        Gizmos.color = new Color(0.6f, 0.85f, 1f, 0.7f);
        Gizmos.DrawLine(_holderCam.position,
                        _holderCam.position + _holderCam.forward * _range);

        if (_localDamage == null) return;

        Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.9f);
        Gizmos.DrawWireSphere(_hitPoint, 0.02f);
    }

    private void OnGUI()
    {
        if (!_debugHud || !base.IsOwner) return;

        GUIStyle st = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 14
        };
        st.normal.textColor = _statusColor;

        string stock = _station != null ? $"в аппарате {_station.Stock}" : "аппарата нет";

        string holes = _localDamage != null
            ? $"   дыр открыто {_localDamage.OpenHoles}/{_localDamage.TotalHoles}"
            : "";

        GUI.Box(new Rect(12f, 228f, 660f, 44f),
                $"Сварка → {_status}\n" +
                $"электрод {Electrode01 * 100f:0}%   {stock}{holes}", st);
    }
}
