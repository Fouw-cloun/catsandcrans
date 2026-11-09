using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// МОЛОТОК. Предмет в руке, выправляет форму раскорёженной посуды.
///
///   ЛКМ — удар. Каждый удар возвращает форму на небольшой шаг
///         (DeformableMesh.HammerStepPerHit), а не разом. Кастрюлю в хлам
///         не выправить одним взмахом — так и должно быть.
///
/// НЕ ТРОГАЕТ ДЫРЫ. Молоток чинит только ФОРМУ (DeformableMesh). Пробоины
/// заваривает сварочный аппарат — WeldingTorchItem. Кастрюля считается
/// полностью годной, когда закрыты оба фронта работ сразу, и решает это
/// PotThermal.ServerCheckFullyRepaired(), а не тот инструмент, что ударил
/// последним. Поэтому порядок безразличен: хочешь — сперва выправи форму,
/// хочешь — сперва завари дыры на ещё мятой кастрюле.
///
/// УДАР ДИСКРЕТНЫЙ, а не удержание. Сварка держится непрерывно, потому что
/// дуга — это процесс. Молоток — это серия отдельных событий, и с
/// удержанием он бы либо колотил без остановки (не читается как удары),
/// либо потребовал бы отдельного таймера «удар раз в N секунд», что то же
/// самое, но сложнее. Проще: клик — замах — в момент удара, отмеченный
/// анимацией, чиним.
///
/// СЕТЬ: замах анимируется у всех локально по синхронизированному триггеру,
/// сам ремонт делает только сервер, вызов — обычный ServerRpc на удар.
/// </summary>
public class HammerItem : InteractableItem
{
    [Header("Положение в руке")]
    [SerializeField] private Vector3 _holdPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    [Header("Удар")]
    [Tooltip("Дальность, метры")]
    [SerializeField] private float _range = 1.6f;

    [Tooltip("Слои, по которым бьёт молоток")]
    [SerializeField] private LayerMask _hitMask = ~0;

    [Tooltip("Секунд между ударами. Это и есть длительность замаха: следующий " +
             "удар физически невозможен раньше, чем боёк долетит и вернётся.")]
    [Min(0.15f)][SerializeField] private float _swingTime = 0.55f;

    [Tooltip("На каком часть замаха от начала происходит собственно удар — " +
             "звук, тряска, ремонт. 0 = сразу, 1 = в самом конце. Удар " +
             "приходится не в начало нажатия, а туда, где боёк на модели " +
             "долетает до цели.")]
    [Range(0.1f, 0.95f)][SerializeField] private float _impactAt = 0.62f;

    [Header("Отдача (визуал)")]
    [Tooltip("Боёк молотка. Пусто — качается весь предмет.")]
    [SerializeField] private Transform _head;

    [Tooltip("На сколько градусов отводится при замахе")]
    [SerializeField] private float _swingAngle = 55f;

    [Header("Удар по цели (визуал)")]
    [Tooltip("Насколько толкает и трясёт кастрюлю в момент удара, метры. " +
             "Чисто локальный эффект, формы объекта не меняет — саму починку " +
             "делает DeformableMesh.")]
    [SerializeField] private float _kickAmount = 0.02f;

    [SerializeField] private float _kickTime = 0.18f;

    [Header("Искры и звук")]
    [SerializeField] private int _sparksPerHit = 8;
    [SerializeField] private float _sparkSpeed = 3f;
    [SerializeField] private AudioSource _hitSound;

    [Header("ОТЛАДКА (выключить в релизе)")]
    [SerializeField] private bool _debugHud = true;

    // ───────── сеть ─────────

    /// <summary>Тикает при каждом ударе. Не bool «бьёт/не бьёт» — момент,
    /// а не состояние: удар мгновенный, длящегося процесса тут нет.</summary>
    private readonly SyncVar<int> _swingTick = new SyncVar<int>(0);

    /// <summary>Куда пришёлся последний удар — чтобы искры и тряску рисовали
    /// все, а не только владелец.</summary>
    private readonly SyncVar<Vector3> _lastHitPoint = new SyncVar<Vector3>();

    private Transform _holderCam;
    private float _nextSwing;
    private int _lastSeenTick;

    private Vector3 _headBaseLocalEuler;
    private float _swingPhase = -1f;      // -1 = не замахивается
    private float _kickPhase = -1f;

    private DeformableMesh _localTarget;
    private Vector3 _hitPoint;
    private readonly RaycastHit[] _hits = new RaycastHit[8];

    private string _status = "—";
    private Color _statusColor = Color.gray;

    public override bool AllowInteractWhileHeld => true;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;

        if (_head != null) _headBaseLocalEuler = _head.localEulerAngles;
    }

    // ───────────────────────── ЦИКЛ ─────────────────────────

    private void Update()
    {
        if (base.IsOwner) OwnerTick();

        // Тик изменился — где-то во вселенной был удар, рисуем эффект у всех,
        // включая владельца: так дешевле, чем гонять отдельный клиентский путь
        // только для себя и общий RPC для остальных.
        if (_swingTick.Value != _lastSeenTick)
        {
            _lastSeenTick = _swingTick.Value;
            PlayImpactFx(_lastHitPoint.Value);
        }

        UpdateSwingVisual();
        UpdateKickVisual();
    }

    private void OwnerTick()
    {
        if (_holderCam == null)
        {
            SetStatus("не найдена камера", Color.red);
            return;
        }

        DeformableMesh found = FindTarget();
        _localTarget = found;

        if (found == null)
        {
            SetStatus(DescribeMiss(), new Color(1f, 0.6f, 0f));
        }
        else
        {
            SetStatus($"«{found.name}»: осталось {found.Damage01 * 100f:0}%, " +
                      $"~{Mathf.CeilToInt(found.Damage01 / Mathf.Max(0.001f, found.HammerStepPerHit))} ударов",
                      Color.green);
        }

        if (Time.time < _nextSwing) return;
        if (!Input.GetMouseButtonDown(0)) return;

        _nextSwing = Time.time + _swingTime;

        // Замах проигрывается ВСЕГДА, даже в пустоту: молотком можно махать
        // просто так, и это не должно требовать цели, чтобы выглядеть живо.
        StartSwingVisual();

        if (found != null) CmdHit(found.NetworkObject, _hitPoint);
    }

    /// <summary>Почему не бьём. Отдельно от «нет цели вообще», чтобы не
    /// путать «мимо» и «тут нечего чинить».</summary>
    private string DescribeMiss()
    {
        if (!Physics.Raycast(_holderCam.position, _holderCam.forward,
                             out RaycastHit h, _range, ~0, QueryTriggerInteraction.Ignore))
            return "наведись на то, что нужно выправить";

        DeformableMesh d = h.collider.GetComponentInParent<DeformableMesh>();
        int layer = h.collider.gameObject.layer;

        if (d == null)
            return $"«{h.collider.name}» молотком не поправить — нет DeformableMesh";

        if (((1 << layer) & _hitMask.value) == 0)
            return $"«{h.collider.name}» на слое «{LayerMask.LayerToName(layer)}», " +
                   "а его нет в Hit Mask";

        if (!d.IsBroken)
            return $"«{h.collider.name}»: форма в порядке";

        return "наведись на то, что нужно выправить";
    }

    private DeformableMesh FindTarget()
    {
        int n = Physics.RaycastNonAlloc(_holderCam.position, _holderCam.forward, _hits,
                                        _range, _hitMask, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        DeformableMesh best = null;

        for (int i = 0; i < n; i++)
        {
            RaycastHit h = _hits[i];
            if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
            if (h.distance >= nearest) continue;

            DeformableMesh d = h.collider.GetComponentInParent<DeformableMesh>();
            if (d == null || !d.IsSpawned || !d.IsBroken) continue;

            nearest = h.distance;
            best = d;
            _hitPoint = h.point;
        }
        return best;
    }

    // ───────────────────────── СЕРВЕР ─────────────────────────

    /// <summary>
    /// Один удар. Не ServerRpc-поток вроде сварки — ОДИН вызов на ОДИН клик,
    /// и это правильно: сварка это процесс, удар молотком это событие.
    /// </summary>
    [ServerRpc(RequireOwnership = true)]
    private void CmdHit(NetworkObject target, Vector3 hitPoint)
    {
        if (target == null) return;

        DeformableMesh d = target.GetComponent<DeformableMesh>()
                        ?? target.GetComponentInChildren<DeformableMesh>()
                        ?? target.GetComponentInParent<DeformableMesh>();

        if (d == null || !d.IsBroken) return;

        bool finished = d.ServerHammerHit();

        // Тик и точку меняем ВСЕГДА, даже если удар последний: эффект удара
        // должен проиграться и на добивающем.
        _lastHitPoint.Value = hitPoint;
        _swingTick.Value++;

        if (!finished) return;

        // Форма выправлена — но годна ли посуда ЦЕЛИКОМ, решает PotThermal:
        // он же смотрит, не остались ли ещё незаваренные дыры.
        CatCooks.Food.IPotBody pot = CatCooks.Food.PotBodyUtil.Find(target);

        if (pot != null) pot.ServerCheckFullyRepaired();
    }

    // ───────────────────────── ВИЗУАЛ ─────────────────────────

    private void StartSwingVisual() => _swingPhase = 0f;

    /// <summary>
    /// Замах и удар одной кривой: назад-вниз-возврат. Момент собственно
    /// удара (искры, звук, тряска цели) приходится на _impactAt — туда, где
    /// боёк на модели долетает до цели, а не в начало нажатия клавиши.
    /// </summary>
    private void UpdateSwingVisual()
    {
        if (_swingPhase < 0f) return;

        _swingPhase += Time.deltaTime / Mathf.Max(0.05f, _swingTime);
        if (_swingPhase >= 1f) { _swingPhase = -1f; ResetHeadPose(); return; }

        float angle;
        if (_swingPhase < _impactAt)
        {
            // Замах назад — до удара.
            float t = _swingPhase / _impactAt;
            angle = Mathf.Sin(t * Mathf.PI * 0.5f) * -_swingAngle;
        }
        else
        {
            // Возврат — после удара, чуть пружиня.
            float t = (_swingPhase - _impactAt) / (1f - _impactAt);
            angle = Mathf.Lerp(-_swingAngle, 0f, EaseOutBack(t));
        }

        ApplyHeadPose(angle);
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.4f;
        const float c3 = c1 + 1f;
        float p = t - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    private void ApplyHeadPose(float angle)
    {
        Transform t = _head != null ? _head : transform;
        Vector3 e = _head != null ? _headBaseLocalEuler : Vector3.zero;
        t.localRotation = Quaternion.Euler(e.x + angle, e.y, e.z);
    }

    private void ResetHeadPose() => ApplyHeadPose(0f);

    /// <summary>Толчок и мелкая тряска цели в момент удара — не деформация,
    /// просто отклик, чтобы удар был ощутим на глаз.</summary>
    private void UpdateKickVisual()
    {
        if (_kickPhase < 0f) return;

        _kickPhase += Time.deltaTime / Mathf.Max(0.05f, _kickTime);
        if (_kickPhase >= 1f) { _kickPhase = -1f; return; }

        // Кик рисуется только искрами и звуком — своей геометрии тут нет,
        // поэтому фаза используется лишь как таймер завершения эффекта.
    }

    /// <summary>Проиграть всё, что видно и слышно в момент удара. Зовётся у
    /// ВСЕХ клиентов по смене _swingTick, включая владельца.</summary>
    private void PlayImpactFx(Vector3 at)
    {
        _kickPhase = 0f;

        SparkBurst.Play(at, Vector3.up, 70f, _sparksPerHit, _sparkSpeed,
                        new Color(1f, 0.85f, 0.5f), new Color(0.8f, 0.5f, 0.2f), 2f);

        if (_hitSound != null) _hitSound.Play();
    }

    // ───────────────────────── ПРЕДМЕТ ─────────────────────────

    public override void ServerOnDrop(PlayerHand hand, Vector3 dir, float force = 0f)
    {
        _holderCam = null;
        base.ServerOnDrop(hand, dir, force);
    }

    public override void UpdateHeldPose(Transform handAnchor)
    {
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

    private void OnGUI()
    {
        if (!_debugHud || !base.IsOwner) return;

        GUIStyle st = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 14
        };
        st.normal.textColor = _statusColor;

        string cooldown = Time.time < _nextSwing
            ? $"замах {Mathf.Max(0f, _nextSwing - Time.time):0.00} с"
            : "готов";

        GUI.Box(new Rect(12f, 228f, 660f, 40f),
                $"Молоток → {_status}   [{cooldown}]", st);
    }
}
