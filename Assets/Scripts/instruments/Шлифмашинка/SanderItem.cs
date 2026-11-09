using CatCooks.Food;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// ШЛИФМАШИНКА. Своя, не болгарка.
///
/// ═══ ЧТО ДЕЛАЕТ ═══
///
///   • ЛКМ зажать — диск крутится (у всех игроков, по сети). Отпустил — стоп.
///   • Диск — дочерний объект, крутится вокруг СВОЕЙ оси Y.
///   • Чистит кожуру: прицелом из камеры через FoodAimTool на этом же объекте
///     (процесс «Чистить»). Навёл на картошку в пределах досягаемости —
///     машинка тянется к ней («вытянутая рука») и чистит, из-под диска летит
///     стружка. Сам предмет про еду ничего не знает — он только говорит
///     «кручусь / не кручусь» через ISpinningTool.
///
/// ═══ ОСОБОЕ: НАЖДАЧКА ЗАБИВАЕТСЯ КОЖУРОЙ ═══
///
/// Бензопила вылетает из рук, у болгарки слетает диск, миксер крутит игрока.
/// У шлифмашинки — ГРЯЗЬ.
///
///   1. Пока она реально чистит картошку, наждачка забивается кожурой.
///   2. С середины забитости начинает ПЛЕВАТЬСЯ: комки кожуры летят во все
///      стороны, прилипают на экран тому, кто держит, и тем, кто стоит рядом.
///      Чем сильнее забита — тем чаще.
///   3. Забилась полностью — перестаёт чистить. Диск крутится, толку ноль.
///   4. Продолжаешь давить забитую — через пару секунд она ВЫСТРЕЛИВАЕТ всем
///      комом тебе в лицо: экран залеплен, наждачка наполовину очистилась.
///
/// Как почистить:
///   • R — выбить об колено. Каждый удар снимает треть грязи… и плюёт в тебя.
///   • Облить из брандспойта — отмывается за пару секунд. Друг с водой рядом
///     с тобой — лучший друг (и заодно мокрый ты).
///
/// ═══ ТРЕБУЕТ ═══
///   NetworkObject, NetworkTransform (Client Authoritative), Rigidbody, Collider.
///   Дочерний диск (поле Disc). FoodAimTool на этом же объекте.
/// </summary>
public class SanderItem : InteractableItem, ISpinningTool, IWashable
{
    [Header("Диск")]
    [Tooltip("Дочерний меш диска")]
    [SerializeField] private Transform _disc;
    [Tooltip("Ось вращения в ЛОКАЛЬНЫХ координатах диска. Y = (0,1,0).")]
    [SerializeField] private Vector3 _discSpinAxis = Vector3.up;
    [Tooltip("Скорость вращения на полных оборотах, град/с")]
    [SerializeField] private float _spinSpeed = 1800f;
    [SerializeField] private float _spinUpTime = 0.25f;
    [SerializeField] private float _spinDownTime = 1.1f;
    [Tooltip("Лёгкая тряска корпуса на оборотах — шлифмашинка вибрирует")]
    [Range(0f, 0.01f)][SerializeField] private float _bodyShake = 0.0025f;

    [Header("Ввод")]
    [Tooltip("ВКЛ — крутится, пока ЛКМ зажата. ВЫКЛ — ЛКМ переключает.")]
    [SerializeField] private bool _holdToSpin = true;

    [Header("Работа по еде")]
    [Tooltip("Прицел по еде. Пусто — найдётся на этом объекте.")]
    [SerializeField] private FoodAimTool _aim;

    [Header("Забивается кожурой")]
    [Tooltip("Сколько секунд РЕАЛЬНОЙ чистки до полной забитости (~3 картошки)")]
    [Min(1f)][SerializeField] private float _secondsToClog = 8f;
    [Tooltip("С какой забитости начинает плеваться, 0..1")]
    [Range(0f, 1f)][SerializeField] private float _spitFrom = 0.5f;
    [Tooltip("Пауза между плевками: при начале плевания … при полной забитости, с")]
    [SerializeField] private Vector2 _spitInterval = new Vector2(1.6f, 0.45f);
    [Tooltip("Сколько секунд давить забитую до выстрела комом в лицо")]
    [Min(0.5f)][SerializeField] private float _overloadTime = 2.2f;
    [Tooltip("Цвет кожуры-грязи")]
    [SerializeField] private Color _gunkColor = new Color(0.45f, 0.32f, 0.18f);

    [Header("Чистка наждачки")]
    [Tooltip("Сколько снимает один удар R, 0..1")]
    [Range(0.05f, 1f)][SerializeField] private float _knockClean = 0.34f;
    [SerializeField] private float _knockCooldown = 0.5f;
    [Tooltip("Сколько секунд под полным напором воды отмывает от полной забитости")]
    [Min(0.2f)][SerializeField] private float _washSeconds = 2f;

    [Header("Брызги на экран")]
    [Tooltip("Кого ещё залепляет плевком, кроме держащего: ближе этого расстояния, м")]
    [SerializeField] private float _splashRadius = 2.5f;
    [Tooltip("Готовый эффект комков (необязательно). Пусто — простые частицы из кода.")]
    [SerializeField] private ParticleSystem _gunkPrefab;

    [Header("Положение в руке")]
    [SerializeField] private Vector3 _holdPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    [Header("Отладка")]
    [SerializeField] private bool _log = true;

    // ───────── сеть ─────────
    private readonly SyncVar<bool> _spinning = new SyncVar<bool>(false);
    /// <summary>Забитость 0..100. Видят все — пригодится для вида наждачки и звука.</summary>
    private readonly SyncVar<byte> _clogNet = new SyncVar<byte>(0);

    // ───────── локально ─────────
    private float _spinScale;
    private bool _lastHeld;
    private Vector3 _baseLocalPos;
    private bool _baseCaptured;

    // ───────── сервер ─────────
    private float _clog;            // 0..1
    private float _spitTimer;
    private float _overload;
    private float _knockReady;

    private const float MaxDt = 0.05f;

    /// <summary>Забитость 0..1 — для HUD, звука, материала наждачки.</summary>
    public float Clog01 => _clogNet.Value / 100f;
    public bool IsClogged => _clogNet.Value >= 100;

    // ───────── ISpinningTool (читает FoodToolDriver) ─────────
    /// <summary>Чистит только крутящаяся и не забитая.</summary>
    public bool IsActive => _spinning.Value && !IsClogged;
    /// <summary>Качество падает по мере забивания: забитая наполовину — чистит хуже.</summary>
    public float Power01 => _spinScale * Mathf.Lerp(1f, 0.4f, Clog01);
    public Transform WorkPoint => _disc;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
        if (_aim == null) _aim = GetComponent<FoodAimTool>();
    }

    // ───────────────────────── КАДР ─────────────────────────

    private void Update()
    {
        UpdateDiscVisual();
        if (base.IsServerInitialized) ServerTick();
        if (base.IsOwner) HandleOwnerInput();
    }

    private void UpdateDiscVisual()
    {
        float dt = Mathf.Min(Time.deltaTime, MaxDt);
        float target = _spinning.Value ? 1f : 0f;
        float time = target > _spinScale ? _spinUpTime : _spinDownTime;
        _spinScale = Mathf.MoveTowards(_spinScale, target, time > 0.0001f ? dt / time : 1f);

        if (_disc != null && _spinScale > 0.0001f)
        {
            // Забитая крутится натужно: медленнее и рывками.
            float clogDrag = Mathf.Lerp(1f, 0.55f, Clog01);
            float stutter = IsClogged ? (0.75f + 0.25f * Mathf.Sin(Time.time * 23f)) : 1f;
            _disc.Rotate(_discSpinAxis.normalized * (_spinSpeed * _spinScale * clogDrag * stutter * dt), Space.Self);
        }

        // Диск дребезжит на оборотах — чисто визуально, у всех игроков.
        if (_disc != null && _bodyShake > 0f && _spinScale > 0.01f)
        {
            if (!_baseCaptured) { _baseLocalPos = _disc.localPosition; _baseCaptured = true; }
            _disc.localPosition = _baseLocalPos + Random.insideUnitSphere * (_bodyShake * _spinScale);
        }
        else if (_baseCaptured && _disc != null)
        {
            _disc.localPosition = _baseLocalPos;
        }
    }

    private void HandleOwnerInput()
    {
        if (Input.GetKeyDown(KeyCode.R)) CmdKnock();

        if (_holdToSpin)
        {
            bool held = Input.GetMouseButton(0);
            if (held != _lastHeld)
            {
                _lastHeld = held;
                CmdSetSpinning(held);
            }
        }
        else if (Input.GetMouseButtonDown(0))
        {
            CmdSetSpinning(!_spinning.Value);
        }
    }

    // ───────────────────────── СЕРВЕР ─────────────────────────

    [ServerRpc(RequireOwnership = true)]
    private void CmdSetSpinning(bool value)
    {
        if (_spinning.Value != value) _spinning.Value = value;
        if (!value) _overload = 0f;
    }

    /// <summary>R — выбить наждачку об колено.</summary>
    [ServerRpc(RequireOwnership = true)]
    private void CmdKnock()
    {
        if (Time.time < _knockReady || _clog <= 0.01f) return;
        _knockReady = Time.time + _knockCooldown;

        _clog = Mathf.Max(0f, _clog - _knockClean);
        PublishClog();
        // Выбиваешь — летит в тебя. Всегда.
        RpcSpit(SpitPoint(), SpitDirection(), 2, true);
        if (_log) Debug.Log($"[Шлифмашинка] выбили об колено, забитость {Mathf.RoundToInt(_clog * 100f)}%", this);
    }

    /// <summary>Облили водой (зовёт FoodJetWasher, сервер).</summary>
    public void ServerWashTick(float amount01)
    {
        if (!base.IsServerInitialized || _clog <= 0f) return;
        // amount01 = секунды под полным напором за этот кадр (dt × напор).
        _clog = Mathf.Max(0f, _clog - amount01 / _washSeconds);
        _overload = 0f;
        PublishClog();
    }

    private void ServerTick()
    {
        float dt = Mathf.Min(Time.deltaTime, MaxDt);

        // Забивается только от НАСТОЯЩЕЙ работы: прицел на продукте, и продукт
        // реально чистится (уже очищенную картошку тереть можно сколько угодно).
        if (IsActive && _aim != null && _aim.IsProcessing)
        {
            _clog = Mathf.Min(1f, _clog + dt / _secondsToClog);
            PublishClog();
            if (_clog >= 1f && _log)
                Debug.Log("[Шлифмашинка] наждачка забилась кожурой — R выбить или облить водой", this);
        }

        if (!_spinning.Value) { _overload = 0f; return; }

        // Плевки: чем сильнее забита, тем чаще.
        if (_clog >= _spitFrom)
        {
            _spitTimer -= dt;
            if (_spitTimer <= 0f)
            {
                float k = Mathf.InverseLerp(_spitFrom, 1f, _clog);
                _spitTimer = Mathf.Lerp(_spitInterval.x, _spitInterval.y, k) * Random.Range(0.7f, 1.3f);
                RpcSpit(SpitPoint(), SpitDirection(), k > 0.8f ? 2 : 1, false);
            }
        }

        // Давит забитую — копит «перегруз» и стреляет всем комом.
        if (IsClogged)
        {
            _overload += dt;
            if (_overload >= _overloadTime)
            {
                _overload = 0f;
                _clog = 0.5f;
                PublishClog();
                RpcSpit(SpitPoint(), SpitDirection(), 6, true);
                if (_log) Debug.Log("[Шлифмашинка] БАХ — ком кожуры в лицо", this);
            }
        }
        else _overload = 0f;
    }

    private void PublishClog()
    {
        byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(_clog) * 100f);
        // Не дёргаем SyncVar каждый кадр: только шаг в 2% и границы.
        if (b != _clogNet.Value && (b == 0 || b == 100 || Mathf.Abs(b - _clogNet.Value) >= 2))
            _clogNet.Value = b;
    }

    private Vector3 SpitPoint() => _disc != null ? _disc.position : transform.position;

    private Vector3 SpitDirection()
    {
        // Из-под диска вбок и вверх, в случайную сторону.
        Vector3 side = Random.value < 0.5f ? transform.right : -transform.right;
        return (side + Vector3.up * 0.8f + Random.insideUnitSphere * 0.4f).normalized;
    }

    // ───────────────────────── ВСЕМ: ПЛЕВОК ─────────────────────────

    /// <summary>
    /// Плевок кожурой. Комки видят все. На экран липнет держащему
    /// (всегда, если faceShot) и тем, кто стоит ближе Splash Radius.
    /// </summary>
    [ObserversRpc]
    private void RpcSpit(Vector3 at, Vector3 dir, int blobs, bool faceShot)
    {
        PlayGunk(at, dir, blobs);

        CatCooksLensSplatter lens = CatCooksLensSplatter.Instance;
        if (lens == null) return;

        if (base.IsOwner)
        {
            // Держащему: плевок иногда, выстрел и выбивание — всегда и много.
            int n = faceShot ? blobs : (Random.value < 0.6f ? 1 : 0);
            for (int i = 0; i < n; i++)
                lens.Splat(_gunkColor, Random.Range(0.7f, faceShot ? 1.8f : 1.1f), 0.55f, 0.7f);
            return;
        }

        Camera cam = Camera.main;
        if (cam == null) return;
        float d = Vector3.Distance(cam.transform.position, at);
        if (d > _splashRadius) return;
        // Стоишь рядом — прилетает, если смотришь в сторону машинки.
        Vector3 toSander = (at - cam.transform.position).normalized;
        if (Vector3.Dot(cam.transform.forward, toSander) < 0.2f) return;
        if (Random.value < Mathf.InverseLerp(_splashRadius, 0.5f, d))
            lens.Splat(_gunkColor, Random.Range(0.6f, 1.2f), 0.55f, 0.7f);
    }

    private void PlayGunk(Vector3 at, Vector3 dir, int blobs)
    {
        if (_gunkPrefab != null)
        {
            ParticleSystem ps = Instantiate(_gunkPrefab, at, Quaternion.LookRotation(dir));
            ParticleSystem.MainModule main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(_gunkColor * 0.8f, _gunkColor * 1.15f);
            ps.Emit(8 + blobs * 10);
            Destroy(ps.gameObject, 3f);
            return;
        }
        // Собрано из кода: комки кожуры с гравитацией, шлёпаются и катятся.
        CCRuntimeParticles.Burst(at, dir, _gunkColor * 0.75f, _gunkColor * 1.2f,
                                 8 + blobs * 10, CCRuntimeParticles.Look.Gunk);
        // И мелкая стружка веером — плевок «разлетается».
        CCRuntimeParticles.Burst(at, dir, _gunkColor * 0.9f, _gunkColor * 1.3f,
                                 12 + blobs * 6, CCRuntimeParticles.Look.Chips);
    }

    // ───────────────────────── РУКА ─────────────────────────

    public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
    {
        if (_spinning.Value) _spinning.Value = false;
        _lastHeld = false;
        _overload = 0f;
        base.ServerOnDrop(hand, tossDirection, force);
    }

    public override void UpdateHeldPose(Transform handAnchor)
    {
        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);
        // Вытянутая рука: при работе машинка тянется к продукту под прицелом.
        if (_aim != null) pos += _aim.LeanOffset;
        // Вибрация в руке: на оборотах руку потряхивает, забитую — сильнее.
        if (_spinScale > 0.01f)
            pos += Random.insideUnitSphere * (_bodyShake * _spinScale * (1f + Clog01 * 2f));
        transform.SetPositionAndRotation(pos, rot);
    }
}
