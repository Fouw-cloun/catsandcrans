using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// ГАЗОВАЯ ГОРЕЛКА. Предмет в руке.
///
///   ЛКМ (удержание) — горит пламя. Отпустил — погасло.
///   ПКМ             — фиксатор: горит постоянно (ещё раз ПКМ — погасить).
///   G при включённом фиксаторе — ПОСТАВИТЬ горелку: она ложится на пол и бьёт
///                     пламенем туда, куда ты смотрел (под ковш), и продолжает гореть.
///   Наводишь на продукт и держишь — он карамелизуется.
///   Пламя греет ковши-кастрюли (IHeatJet → SurfaceHeatGlow).
///
/// В отличие от болгарки водить ничего не надо: держи пламя на месте и жди.
/// Карамель — процесс не про точность руки, а про терпение и про то, чтобы
/// вовремя убрать, пока не сгорело.
///
/// СЕТЬ:
///   владелец — целится камерой и шлёт факт горения;
///   сервер   — сам делает луч и копит обработку;
///   клиенты  — рисуют пламя и свет локально по одному флагу.
///
/// Требует: NetworkObject, NetworkTransform (Client Authoritative), Collider,
/// Rigidbody.
/// </summary>
public class BlowTorchItem : InteractableItem, CatCooks.Food.IHeatJet
{

    public override bool UseHeldBlocking => false;     // v17.3: своя логика столкновений/позы
    [Header("Положение в руке")]
    [SerializeField] private Vector3 _holdPositionOffset = new Vector3(0.18f, -0.2f, 0.35f);
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;
    [Tooltip("В руке горелка сама поворачивается соплом туда, куда смотришь (как бы ни была повёрнута модель)")]
    [SerializeField] private bool _aimNozzle = true;

    [Header("Сопло")]
    [Tooltip("Кончик горелки: отсюда бьёт пламя, синяя ось (Z) — куда. Пусто — угадается по габаритам " +
             "(лучше создать меню CatCooks → Горелки → 1. Настроить выделенное)")]
    [SerializeField] private Transform _nozzle;

    [Tooltip("Длина пламени, метры. Горелка работает вплотную.")]
    [SerializeField] private float _flameLength = 0.9f;

    [Tooltip("Слои, по которым работает")]
    [SerializeField] private LayerMask _targetMask = ~0;

    [Header("Обработка")]
    [Tooltip("Что делает горелка. Обычно — карамелизация.")]
    [SerializeField] private ProcessDef _process;

    [Tooltip("Секунд под пламенем, чтобы обработать продукт целиком")]
    [Min(0.2f)][SerializeField] private float _secondsToFinish = 3f;

    [Tooltip("Сколько продуктов обрабатывает разом. Пламя широкое: ткнул " +
             "в кучку — карамелизуются все, кого задело.")]
    [Min(1)][SerializeField] private int _maxTargets = 4;

    [Tooltip("Радиус захвата вокруг линии пламени")]
    [SerializeField] private float _flameRadius = 0.18f;

    [Header("Огонь")]
    [SerializeField] private Color _flameCore = new Color(0.55f, 0.75f, 1f);
    [SerializeField] private Color _flameTip = new Color(1f, 0.6f, 0.15f);
    [SerializeField] private float _flamePerSecond = 130f;
    [Tooltip("v14: мультяшная газовая струя (шейдер CatCooks/Toon Flame). Выкл — старые частицы")]
    [SerializeField] private bool _toonFlame = true;
    [Tooltip("v14: толщина струи на вид, доля Flame Radius (радиус нагрева широкий, пламя — тоньше)")]
    [Range(0.1f, 2f)][SerializeField] private float _toonWidth = 0.45f;
    [Tooltip("v16: в руке не проходит сквозь стены, стол и предметы — упирается и отъезжает к игроку")]
    [SerializeField] private bool _blockByWorld = true;

    [Header("Фиксатор и подставка")]
    [Tooltip("ПКМ включает постоянное пламя (как фиксатор на кнопке)")]
    [SerializeField] private bool _allowLatch = true;
    [Tooltip("G с включённым фиксатором — горелка ложится и бьёт туда, куда смотришь")]
    [SerializeField] private bool _standOnDrop = true;
    [Tooltip("Докуда искать цель при постановке, м")]
    [SerializeField] private float _standReach = 4f;

    [Header("Нагрев металла (ковш)")]
    [Tooltip("Мощность для нагрева ковша: 1 — обычная, кровельная — 2.5")]
    [SerializeField] private float _heatPower = 1f;

    [Header("Свет")]
    [SerializeField] private Light _flameLight;
    [SerializeField] private float _lightIntensity = 2.2f;
    [SerializeField] private float _lightRange = 3f;

    [Header("Звук")]
    [SerializeField] private AudioSource _hissLoop;

    [Header("ОТЛАДКА (выключить в релизе)")]
    [SerializeField] private bool _debugHud = true;

    // ───────── сеть ─────────
    private readonly SyncVar<bool> _burning = new SyncVar<bool>(false);
    private readonly SyncVar<bool> _latched = new SyncVar<bool>(false);
    private readonly SyncVar<bool> _onStand = new SyncVar<bool>(false);
    private bool _jetRegistered;

    private Transform _holderCam;
    private bool _lastHeld;
    private readonly RaycastHit[] _hits = new RaycastHit[16];

    private ParticleSystem _flame;
    private float _flameCarry;
    private CatCooks.Food.ToonGasFlame _toon;
    private CatCooks.Food.HeldBlocker _blocker;
    private bool _toonFailed;
    private bool _fireRegistered;

    private string _status = "—";
    private Quaternion _nozzleLocalRot = Quaternion.identity;
    private bool _wasBurning;
    private Color _statusColor = Color.gray;

    /// <summary>У владельца берём локальный флаг: реакция должна быть в тот же
    /// кадр, а SyncVar приезжает только через круг до сервера.</summary>
    public bool IsBurning => (base.IsOwner ? (_lastHeld || _burning.Value) : _burning.Value) || _latched.Value;

    /// <summary>Фиксатор включён — горит без ЛКМ.</summary>
    public bool IsLatched => _latched.Value;

    // ── IHeatJet ──
    public bool JetActive => IsBurning;
    public Vector3 JetOrigin => Nozzle.position;
    public Vector3 JetDirection => Nozzle.forward;
    public float JetLength => _flameLength;
    public float JetRadius => _flameRadius;
    public float JetPower => _heatPower;
    public Transform JetRoot => transform;

    public Transform Nozzle => _nozzle != null ? _nozzle : transform;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
        if (_nozzle == null && GuessNozzlePose(transform, out Vector3 np, out Quaternion nr))
        {
            var go = new GameObject("Nozzle (auto)");
            go.transform.SetParent(transform, true);
            go.transform.SetPositionAndRotation(np, nr);
            _nozzle = go.transform;
            Debug.LogWarning($"[Горелка] «{name}»: Nozzle не задан — угадал по габаритам (конец трубки сверху). " +
                             "Если пламя бьёт не туда — выдели горелку и CatCooks → Горелки → 1. Настроить выделенное / 2. Сопло на другой конец", this);
        }
        if (_nozzle == null) _nozzle = transform;
        _nozzleLocalRot = Quaternion.Inverse(transform.rotation) * _nozzle.rotation;

        EnsureLight();
        CreateFlame();
    }

    /// <summary>
    /// Угадать сопло по габаритам (модель стоит ровно): верх модели, конец самой длинной
    /// горизонтальной стороны (трубка горелки), синяя ось — наружу. Для меню и запасной вариант.
    /// </summary>
    public static bool GuessNozzlePose(Transform root, out Vector3 pos, out Quaternion rot)
    {
        pos = root.position; rot = root.rotation;
        Bounds b = default(Bounds);
        bool any = false;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if ((r.gameObject.hideFlags & HideFlags.DontSave) != 0 || r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any) return false;
        Vector3 axis = b.extents.x >= b.extents.z ? Vector3.right : Vector3.forward;
        float ext = b.extents.x >= b.extents.z ? b.extents.x : b.extents.z;
        pos = b.center + axis * ext;
        pos.y = b.max.y - b.size.y * 0.12f;
        rot = Quaternion.LookRotation(axis, Vector3.up);
        return true;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        Debug.Log($"[Горелка] «{name}»: сопло «{Nozzle.name}», пламя {_flameLength:0.##} м, мощность {_heatPower:0.#}" +
                  (_process == null ? " · Process не задан — продукты не карамелизует (ковш греет)" : $" · обработка «{_process.DisplayName}»"), this);
    }

    private void OnDestroy()
    {
        // Иначе в реестре останется мёртвая ссылка, и газ будет взрываться
        // от несуществующей горелки.
        if (_fireRegistered) IgnitionRegistry.Unregister(Nozzle);
        CatCooks.Food.HeatJets.Unregister(this);
        if (_flame != null) Destroy(_flame.gameObject);
        if (_toon != null) Destroy(_toon.gameObject);
    }

    private void EnsureLight()
    {
        if (_flameLight != null) return;

        GameObject go = new GameObject("FlameLight");
        go.transform.SetParent(Nozzle, false);

        _flameLight = go.AddComponent<Light>();
        _flameLight.type = LightType.Point;
        _flameLight.shadows = LightShadows.None;
        _flameLight.enabled = false;
    }

    // ───────────────────────── ЦИКЛ ─────────────────────────

    private void Update()
    {
        if (base.IsOwner) OwnerInput();
        if (base.IsServerInitialized) ServerBurn();
        UpdateVisual();
    }

    private void OwnerInput()
    {
        // Шлём только на смену состояния — идемпотентно, без спама каждый кадр.
        bool held = Input.GetMouseButton(0);
        if (held == _lastHeld) return;

        _lastHeld = held;
        CmdSetBurning(held);
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdSetBurning(bool value)
    {
        if (_burning.Value == value) return;
        _burning.Value = value;
    }

    /// <summary>
    /// Обработка идёт на сервере, и луч он делает СВОЙ.
    ///
    /// Прицел тут не нужен: пламя бьёт из сопла вдоль корпуса горелки, а её
    /// положение уже реплицируется через NetworkTransform. Гонять направление
    /// отдельной командой незачем.
    /// </summary>
    [Server]
    private void ServerBurn()
    {
        if (!(_burning.Value || _latched.Value) || _process == null) return;

        Transform n = Nozzle;
        float dt = Mathf.Min(Time.deltaTime, 0.1f);
        float delta = dt / Mathf.Max(0.2f, _secondsToFinish);

        int hits = Physics.SphereCastNonAlloc(n.position, _flameRadius, n.forward,
                                              _hits, _flameLength, _targetMask,
                                              QueryTriggerInteraction.Ignore);
        int done = 0;

        for (int i = 0; i < hits && done < _maxTargets; i++)
        {
            Collider c = _hits[i].collider;
            if (c == null || c.transform.IsChildOf(transform)) continue;

            FoodItem food = c.GetComponentInParent<FoodItem>();
            if (food == null || !food.IsSpawned || food.Consumed) continue;
            if (!food.CanApplyNow(_process)) continue;

            done++;
            food.ServerAddProgress(_process, delta, 1f);
        }
    }

    // ───────────────────────── ВИЗУАЛ ─────────────────────────

    private void UpdateVisual()
    {
        bool on = IsBurning;

        // Пламя — источник огня для газа. Регистрируем только пока реально
        // горит: погасла горелка, и газ рядом с ней снова безопасен.
        if (on != _fireRegistered)
        {
            _fireRegistered = on;
            if (on) IgnitionRegistry.Register(Nozzle);
            else IgnitionRegistry.Unregister(Nozzle);
        }
        if (on != _wasBurning)
        {
            _wasBurning = on;
            if (on && base.IsOwner) Debug.Log($"[Горелка] «{name}» ГОРИТ — пламя из «{Nozzle.name}» {_flameLength:0.##} м (ковш греется, если пламя в него упирается)", this);
        }
        if (on != _jetRegistered)
        {
            _jetRegistered = on;
            if (on) CatCooks.Food.HeatJets.Register(this);
            else CatCooks.Food.HeatJets.Unregister(this);
        }

        if (_flameLight != null)
        {
            _flameLight.enabled = on;
            if (on)
            {
                // Пламя дышит: ровный свет выглядит как фонарик, а не как огонь.
                float f = 0.95f + Mathf.PerlinNoise(Time.time * 2f, 0f) * 0.1f;            // v19: ровный свет, едва дышит
                _flameLight.color = Color.Lerp(_flameTip, _flameCore, 0.4f);
                _flameLight.intensity = _lightIntensity * f;
                _flameLight.range = _lightRange;
            }
        }

        if (_hissLoop != null)
        {
            if (on && !_hissLoop.isPlaying) _hissLoop.Play();
            else if (!on && _hissLoop.isPlaying) _hissLoop.Stop();
        }

        // v14: мультяшная струя (разгорается и гаснет плавно — поэтому тикаем и когда погасла)
        if (_toonFlame)
        {
            if (_toon == null && !_toonFailed && on)
            {
                _toon = CatCooks.Food.ToonGasFlame.Create("BlowTorchToonFlame (auto)", CatCooks.Food.ToonGasFlame.Kind.Blow);
                _toonFailed = _toon == null;
            }
            if (_toon != null)
            {
                _toon.Tick(Nozzle, on ? 1f : 0f, _flameLength, _flameRadius * _toonWidth, transform);
                return;
            }
        }

        if (!on || _flame == null) return;

        _flameCarry += _flamePerSecond * Time.deltaTime;
        int count = Mathf.FloorToInt(_flameCarry);
        if (count <= 0) return;
        _flameCarry -= count;
        if (count > 14) count = 14;

        Transform n = Nozzle;
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();

        for (int i = 0; i < count; i++)
        {
            // Ближе к соплу — голубое ядро, дальше — оранжевый язык.
            float along = Random.value;
            float spread = _flameRadius * along * 0.8f;

            ep.position = n.position + n.forward * (along * _flameLength * 0.4f)
                        + Random.insideUnitSphere * spread;

            ep.startColor = Color.Lerp(_flameCore, _flameTip, along * along);
            ep.startSize = Mathf.Lerp(0.035f, 0.09f, along);
            ep.startLifetime = Random.Range(0.1f, 0.28f);
            ep.velocity = n.forward * Random.Range(3.5f, 6.5f)
                        + Random.insideUnitSphere * 0.4f;

            _flame.Emit(ep, 1);
        }
    }

    private void CreateFlame()
    {
        GameObject go = new GameObject("BlowTorchFlame (auto)");

        // ВЫКЛЮЧЕННЫМ: система играет сразу при AddComponent, и часть свойств
        // на играющей менять нельзя.
        go.SetActive(false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = 800;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.startSize = 0.05f;
        main.startLifetime = 0.2f;
        main.gravityModifier = -0.1f;      // язык пламени тянется вверх

        // При сцене далеко от нуля Automatic отключил бы систему целиком:
        // её трансформ вне экрана, хотя частицы рядом с игроком.
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        ParticleSystem.EmissionModule em = ps.emission;
        em.enabled = false;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        // Пламя разрастается и гаснет.
        ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f,
            AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.5f),
                    new GradientAlphaKey(0f, 1f) });
        col.color = g;

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                 ?? Shader.Find("Particles/Standard Unlit")
                 ?? Shader.Find("Sprites/Default");

        if (sh != null)
        {
            Material m = new Material(sh) { name = "BlowTorchFlame (auto)" };

            // Аддитивное смешивание: языки пламени складываются и светятся,
            // а не перекрывают друг друга плоскими пятнами.
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);
            if (m.HasProperty("_SrcBlend"))
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend"))
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);

            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            r.material = m;
        }

        go.SetActive(true);
        ps.Play();
        _flame = ps;
    }

    // ───────────────────────── ПРЕДМЕТ ─────────────────────────

    public override void ServerOnDrop(PlayerHand hand, Vector3 dir, float force = 0f)
    {
        _burning.Value = false;
        _lastHeld = false;
        _holderCam = null;
        base.ServerOnDrop(hand, dir, force);

        // С фиксатором — ставим горелку пламенем туда, куда смотрел игрок.
        // Без фиксатора — выронил, погасло.
        if (!_latched.Value) return;
        if (_standOnDrop && TorchStand.ServerPlace(transform, Nozzle, TorchStand.EyeOf(hand), dir, _standReach, _flameLength, out Vector3 at))
        {
            _onStand.Value = true;
            Debug.Log($"[Горелка] поставлена, бьёт в {at}", this);
        }
        else if (!_standOnDrop) { /* лежит и горит, куда упала */ }
        else Debug.Log("[Горелка] некуда направить — лежит и горит, куда упала (смотри на цель, когда ставишь)", this);
    }

    /// <summary>ПКМ — фиксатор: постоянное пламя вкл/выкл.</summary>
    public override void ServerSecondaryUse(UseContext ctx)
    {
        if (!_allowLatch) return;
        _latched.Value = !_latched.Value;
        Debug.Log(_latched.Value ? "[Горелка] фиксатор: горит постоянно (G — поставить, ПКМ — погасить)" : "[Горелка] фиксатор снят", this);
    }

    public override void ServerOnPickup(PlayerHand hand)
    {
        if (_onStand.Value) _onStand.Value = false;
        base.ServerOnPickup(hand);
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _onStand.OnChange += OnStandChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _onStand.OnChange -= OnStandChanged;
    }

    /// <summary>На подставке — замирает у всех (иначе у клиентов физика роняла бы её).</summary>
    private void OnStandChanged(bool prev, bool next, bool asServer)
    {
        if (asServer && base.IsClientInitialized) return;
        if (_rb == null || base.IsServerInitialized) return;
        if (next) { _rb.isKinematic = true; _rb.useGravity = false; }
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
        // сопло — туда, куда смотришь: модель может быть повёрнута как угодно
        if (_aimNozzle && _nozzle != null && _nozzle != transform) rot = rot * Quaternion.Inverse(_nozzleLocalRot);
        if (_blockByWorld && _holderCam != null)
        {
            if (_blocker == null) _blocker = new CatCooks.Food.HeldBlocker(transform);
            pos = _blocker.Solve(pos, rot, _holderCam.position);
        }
        transform.SetPositionAndRotation(pos, rot);
    }

    // ───────────────────────── ОТЛАДКА ─────────────────────────

    private void OnGUI()
    {
        if (!_debugHud || !base.IsOwner) return;

        GUIStyle st = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 14
        };

        if (!IsBurning)
        {
            _status = (_allowLatch ? "зажми ЛКМ (ПКМ — фиксатор, потом G — поставить)" : "зажми ЛКМ")
                      + (_process == null ? "   [Process не задан — только греет]" : "");
            _statusColor = Color.gray;
        }
        else
        {
            _status = (_latched.Value ? "ГОРИТ (фиксатор)" : "ГОРИТ") +
                      (_process != null ? $" — наведи на продукт ({_process.DisplayName}) или на ковш" : " — наведи пламя на ковш");
            _statusColor = new Color(1f, 0.6f, 0.2f);
        }

        st.normal.textColor = _statusColor;
        GUI.Box(new Rect(12f, 336f, 660f, 26f), $"Горелка → {_status}", st);
    }

    private void OnDrawGizmosSelected()
    {
        Transform n = _nozzle != null ? _nozzle : transform;

        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
        Gizmos.DrawLine(n.position, n.position + n.forward * _flameLength);
        Gizmos.DrawWireSphere(n.position + n.forward * _flameLength, _flameRadius);
    }
}
