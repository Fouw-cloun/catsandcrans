using CatCooks.Food;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// КРОВЕЛЬНАЯ ГОРЕЛКА (как для наплавляемой гидроизоляции) — длинная трубка со стаканом,
/// питается по шлангу от баллона с редуктором (TorchGasCylinder).
///
///   ЛКМ (удержание) — ревущее пламя.
///   ПКМ             — вентиль: горит постоянно (ещё раз — закрыть).
///   G при открытом вентиле — ПОСТАВИТЬ: горелка ложится и бьёт туда, куда смотришь
///                     (под ковш), и горит, пока не закроешь или не кончится газ.
///   Колесо мыши     — давление на редукторе 1…5: длиннее и жарче пламя, газ уходит быстрее.
///
/// Греет металл ковша пятном (IHeatJet → SurfaceHeatGlow), мощность ~2.5 ручной горелки.
/// Шланг ограничивает: дальше длины шланга баллон волочится следом, дёрнул ещё —
/// шланг натянут, газ не идёт.
///
/// СЕТЬ: держащий шлёт «зажал/отпустил» и колесо; горит ли, решает сервер (газ, шланг)
/// и рассылает одним флагом. Пламя, шланг, свет — у каждого сами.
///
/// МОДЕЛЬ: RoofingTorch.obj (части HoseBarb, Handle, ValveBody, ValveKnob, Lever, Tube, BellNut, Bell).
/// Сопло (Nozzle) и штуцер шланга находятся сами по частям Bell и HoseBarb, если не заданы.
///
/// Требует: NetworkObject, NetworkTransform (Client Authoritative), Rigidbody, Collider.
/// </summary>
public class RoofingTorchItem : InteractableItem, IHeatJet
{

    public override bool UseHeldBlocking => false;     // v17.3: своя логика столкновений/позы
    [Header("Положение в руке")]
    [SerializeField] private Vector3 _holdPositionOffset = new Vector3(0.15f, -0.25f, 0.2f);
    [SerializeField] private Vector3 _holdEulerOffset = new Vector3(15f, 0f, 0f);

    [Header("Сопло и шланг")]
    [Tooltip("Сопло: синяя ось (Z) — куда бьёт пламя. Пусто — найдётся по стакану (часть Bell)")]
    [SerializeField] private Transform _nozzle;
    [Tooltip("Куда крепится шланг. Пусто — найдётся по штуцеру (HoseBarb)")]
    [SerializeField] private Transform _hosePort;
    [Tooltip("Баллон. Пусто — при старте подключится ближайший в радиусе Auto Connect")]
    [SerializeField] private TorchGasCylinder _cylinder;
    [SerializeField] private float _autoConnect = 8f;
    [Tooltip("Длина шланга, м")]
    [SerializeField] private float _hoseLength = 4f;
    [Tooltip("Толщина шланга (радиус), м")]
    [SerializeField] private float _hoseRadius = 0.011f;
    [SerializeField] private Color _hoseColor = new Color(1f, 0.42f, 0.08f);
    [Tooltip("Баллон волочится, если шланг натянут (выкл — просто гаснет)")]
    [SerializeField] private bool _dragCylinder = true;
    [Tooltip("v16: баллон стоит, где поставили, — шланг его НЕ тащит (Drag Cylinder не действует). Отошёл дальше шланга ×1.5 — гаснет: «подойди к баллону»")]
    [SerializeField] private bool _cylinderStays = true;
    [Tooltip("v16: в руке не проходит сквозь стены, стол и предметы — упирается и отъезжает к игроку")]
    [SerializeField] private bool _blockByWorld = true;
    [Tooltip("Без баллона не горит. Выкл — горит и без него (для тестов)")]
    [SerializeField] private bool _needsCylinder = true;

    [Header("Пламя")]
    [SerializeField] private float _flameLength = 0.8f;
    [SerializeField] private float _flameRadius = 0.1f;
    [Tooltip("Мощность для нагрева ковша (ручная горелка = 1)")]
    [SerializeField] private float _heatPower = 2.5f;
    [SerializeField] private Color _flameCore = new Color(0.45f, 0.65f, 1f);
    [SerializeField] private Color _flameTip = new Color(1f, 0.55f, 0.12f);
    [SerializeField] private float _flamePerSecond = 260f;
    [Tooltip("v14: мультяшный огонь (шейдер CatCooks/Toon Flame). Выкл — старые частицы")]
    [SerializeField] private bool _toonFlame = true;
    [Tooltip("v14: толщина пламени на вид, доля радиуса струи")]
    [Range(0.2f, 4f)][SerializeField] private float _toonWidth = 1.6f;

    [Header("Подставка")]
    [SerializeField] private bool _standOnDrop = true;
    [SerializeField] private float _standReach = 5f;

    [Header("Свет и звук")]
    [SerializeField] private Light _light;
    [SerializeField] private float _lightIntensity = 3.5f;
    [SerializeField] private float _lightRange = 4f;
    [SerializeField] private AudioSource _roarLoop;

    [Header("Отладка")]
    [SerializeField] private bool _debugHud = true;
    [SerializeField] private bool _log = true;

    // ───────── сеть ─────────
    private readonly SyncVar<bool> _held = new SyncVar<bool>(false);     // ЛКМ зажата
    private readonly SyncVar<bool> _valve = new SyncVar<bool>(false);    // вентиль открыт
    private readonly SyncVar<bool> _flame = new SyncVar<bool>(false);    // горит (решает сервер)
    private readonly SyncVar<bool> _onStand = new SyncVar<bool>(false);
    private readonly SyncVar<NetworkObject> _cylNob = new SyncVar<NetworkObject>();
    private readonly SyncVar<byte> _why = new SyncVar<byte>(0);          // почему не горит

    private bool _lastHeld;
    private bool _registered;
    private ParticleSystem _ps;
    private ToonGasFlame _toon;
    private HeldBlocker _blocker;
    private Transform _holderCam;
    private bool _toonFailed;
    private float _carry;
    private GasHose _hose;
    private TorchGasCylinder _cylLocal;
    private TorchGasCylinder _cylRef;     // найден поиском (если SyncVar ещё пуст)
    private float _scanAt, _whyLogAt;
    private bool _wasOn;
    private float _ignite;               // 0..1 разгорается

    private const byte WhyNone = 0, WhyNoCylinder = 1, WhyNoGas = 2, WhyHose = 3;

    public bool IsBurning => _flame.Value;
    public TorchGasCylinder Cylinder
    {
        get
        {
            if (_cylNob.Value != null) { TorchGasCylinder c = _cylNob.Value.GetComponent<TorchGasCylinder>(); if (c != null) return c; }
            return _cylinder != null ? _cylinder : _cylRef;
        }
    }

    /// <summary>Ближайший баллон в радиусе Auto Connect (или null).</summary>
    private TorchGasCylinder FindNearestCylinder(out float dist)
    {
        TorchGasCylinder best = null;
        dist = _autoConnect;
        foreach (TorchGasCylinder g in FindObjectsByType<TorchGasCylinder>(FindObjectsSortMode.None))
        {
            float d = Vector3.Distance(g.transform.position, transform.position);
            if (d < dist) { dist = d; best = g; }
        }
        return best;
    }
    private float PressureK { get { TorchGasCylinder c = Cylinder; return c != null ? c.PressureK : 1f; } }
    public Transform Nozzle => _nozzle != null ? _nozzle : transform;

    // ── IHeatJet ──
    public bool JetActive => _flame.Value && _ignite > 0.3f;
    public Vector3 JetOrigin => Nozzle.position;
    public Vector3 JetDirection => Nozzle.forward;
    public float JetLength => _flameLength * PressureK;
    public float JetRadius => _flameRadius * Mathf.Lerp(0.8f, 1.2f, PressureK - 0.6f);
    public float JetPower => _heatPower * PressureK;
    public Transform JetRoot => transform;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
        if (_nozzle == null) _nozzle = AutoNozzle();
        if (_hosePort == null) _hosePort = AutoPort();
        EnsureLight();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        TorchGasCylinder c = _cylinder;
        float best = 0f;
        if (c == null) c = FindNearestCylinder(out best);
        if (c != null) { _cylRef = c; _cylNob.Value = c.NetworkObject; }
        if (_log)
            Debug.Log($"[Кровельная горелка] «{name}»: сопло — {(_nozzle != null ? $"«{_nozzle.name}»" : "НЕ НАЙДЕНО (нет части Bell) — пламя из центра")}, " +
                      $"шланг — {(_hosePort != null ? $"«{_hosePort.name}»" : "из центра")}, " +
                      (c != null ? $"баллон «{c.name}» ({Vector3.Distance(c.transform.position, transform.position):0.0} м)"
                                 : _needsCylinder ? $"БАЛЛОНА НЕТ ближе {_autoConnect:0.#} м — поднеси баллон, подключится сам" : "баллон не нужен"), this);
    }

    /// <summary>Баллон не найден при старте (поставили позже, принесли) — ищем раз в секунду.</summary>
    private void ServerFindCylinder()
    {
        if (Cylinder != null || Time.time < _scanAt) return;
        _scanAt = Time.time + 1f;
        TorchGasCylinder c = FindNearestCylinder(out float d);
        if (c == null) return;
        _cylRef = c;
        if (c.NetworkObject != null && c.NetworkObject.IsSpawned) _cylNob.Value = c.NetworkObject;
        if (_log) Debug.Log($"[Кровельная горелка] «{name}»: подключил баллон «{c.name}» ({d:0.0} м)", this);
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

    private void OnDestroy()
    {
        HeatJets.Unregister(this);
        if (_registered) IgnitionRegistry.Unregister(Nozzle);
        if (_ps != null) Destroy(_ps.gameObject);
        if (_toon != null) Destroy(_toon.gameObject);
        if (_hose != null) Destroy(_hose.gameObject);
    }

    // ───────────────────────── КАДР ─────────────────────────

    private void Update()
    {
        if (base.IsOwner) OwnerInput();
        if (base.IsServerInitialized) ServerTick(Time.deltaTime);
        UpdateHose();
        UpdateVisual(Time.deltaTime);
    }

    private void OwnerInput()
    {
        bool held = Input.GetMouseButton(0);
        if (held != _lastHeld) { _lastHeld = held; CmdHeld(held); }
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.01f) CmdPressure(wheel > 0f ? 1 : -1);
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdHeld(bool v) { if (_held.Value != v) _held.Value = v; }

    [ServerRpc(RequireOwnership = true)]
    private void CmdPressure(int d)
    {
        TorchGasCylinder c = Cylinder;
        if (c != null) c.ServerAddPressure(d);
    }

    /// <summary>ПКМ — вентиль: постоянный огонь.</summary>
    public override void ServerSecondaryUse(UseContext ctx)
    {
        _valve.Value = !_valve.Value;
        if (_log) Debug.Log(_valve.Value ? "[Кровельная горелка] вентиль открыт — горит постоянно (G — поставить)" : "[Кровельная горелка] вентиль закрыт", this);
    }

    [Server]
    private void ServerTick(float dt)
    {
        ServerFindCylinder();
        // SyncVar мог остаться пустым, если баллон заспавнился позже горелки
        if (_cylNob.Value == null && _cylRef != null && _cylRef.NetworkObject != null && _cylRef.NetworkObject.IsSpawned)
            _cylNob.Value = _cylRef.NetworkObject;
        bool want = _held.Value || _valve.Value;
        TorchGasCylinder c = Cylinder;
        byte why = WhyNone;
        if (c == null) { if (_needsCylinder) why = WhyNoCylinder; }
        else if (!c.HasGas) why = WhyNoGas;
        else
        {
            float d = Vector3.Distance(PortPoint, c.HoseOutletPoint);
            if (d > _hoseLength)
            {
                float excess = d - _hoseLength;
                Rigidbody rb = c.GetComponent<Rigidbody>();
                if (_dragCylinder && !_cylinderStays && rb != null && !rb.isKinematic && !c.IsCarried)
                {
                    Vector3 dir = PortPoint - c.HoseOutletPoint;
                    dir.y = Mathf.Max(0f, dir.y) * 0.2f;
                    rb.AddForce(dir.normalized * Mathf.Min(40f, excess * 60f), ForceMode.Acceleration);
                    rb.WakeUp();
                }
                if (d > _hoseLength * 1.5f) why = WhyHose;
            }
        }
        bool on = want && why == WhyNone;
        if (_flame.Value != on) _flame.Value = on;
        if (_log)
        {
            if (on && !_wasOn)
                Debug.Log($"[Кровельная горелка] ГОРИТ: давление {(c != null ? c.Pressure : 0)}/5, пламя {JetLength:0.00} м, мощность {JetPower:0.0}", this);
            if (want && !on && Time.time >= _whyLogAt)
            {
                _whyLogAt = Time.time + 2f;
                Debug.Log($"[Кровельная горелка] не горит: {WhyText(why)}", this);
            }
        }
        _wasOn = on;
        if (_why.Value != why) _why.Value = why;
        if (on && c != null) c.ServerConsume(dt * PressureK);
    }

    private static string WhyText(byte w)
    {
        switch (w)
        {
            case WhyNoCylinder: return "нет баллона (TorchGasCylinder рядом или в поле Cylinder)";
            case WhyNoGas: return "газ в баллоне кончился";
            case WhyHose: return "шланг натянут — подойди к баллону";
            default: return "";
        }
    }

    // ───────────────────────── РУКА ─────────────────────────

    public override void ServerOnPickup(PlayerHand hand)
    {
        if (_onStand.Value) _onStand.Value = false;
        base.ServerOnPickup(hand);
    }

    public override void ServerOnDrop(PlayerHand hand, Vector3 dir, float force = 0f)
    {
        _held.Value = false;
        _lastHeld = false;
        base.ServerOnDrop(hand, dir, force);
        if (!_valve.Value) return;                                     // вентиль закрыт — просто положили
        if (_standOnDrop && TorchStand.ServerPlace(transform, Nozzle, TorchStand.EyeOf(hand), dir, _standReach, JetLength, out Vector3 at))
        {
            _onStand.Value = true;
            if (_log) Debug.Log($"[Кровельная горелка] поставлена, пламя в {at}", this);
        }
        else if (_log) Debug.Log("[Кровельная горелка] не вижу цели — лежит и горит, куда упала", this);
    }

    private void OnStandChanged(bool prev, bool next, bool asServer)
    {
        if (asServer && base.IsClientInitialized) return;
        if (_rb == null || base.IsServerInitialized) return;
        if (next) { _rb.isKinematic = true; _rb.useGravity = false; }
    }

    public override void UpdateHeldPose(Transform handAnchor)
    {
        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);
        if (_blockByWorld)
        {
            if (_holderCam == null)
            {
                FirstPersonController fpc = handAnchor.GetComponentInParent<FirstPersonController>();
                Camera cam = fpc != null ? fpc.GetComponentInChildren<Camera>(true) : null;
                if (cam != null) _holderCam = cam.transform;
            }
            if (_holderCam != null)
            {
                if (_blocker == null) _blocker = new HeldBlocker(transform);
                pos = _blocker.Solve(pos, rot, _holderCam.position);
            }
        }
        if (_flame.Value) pos += Random.insideUnitSphere * 0.0015f;      // ревёт — рука подрагивает
        transform.SetPositionAndRotation(pos, rot);
    }

    // ───────────────────────── ВИД ─────────────────────────

    private Vector3 PortPoint => _hosePort != null ? _hosePort.position : transform.position;

    private void UpdateHose()
    {
        // у клиента баллон мог не приехать по сети — ищем сами (только для шланга и стрелки)
        if (Cylinder == null && !base.IsServerInitialized && Time.time >= _scanAt)
        {
            _scanAt = Time.time + 2f;
            _cylRef = FindNearestCylinder(out _);
        }
        TorchGasCylinder c = Cylinder;
        if (c != _cylLocal)
        {
            if (_cylLocal != null) _cylLocal.Flow = 0f;
            _cylLocal = c;
        }
        if (c == null) { if (_hose != null) _hose.SetVisible(false); return; }
        if (_hose == null) _hose = GasHose.Create(transform);
        _hose.SetVisible(true);
        _hose.Length = _hoseLength;
        _hose.Radius = _hoseRadius;
        _hose.Color = _hoseColor;
        Vector3 dirA = _hosePort != null ? _hosePort.forward : -transform.forward;
        _hose.Tick(PortPoint, dirA, c.HoseOutletPoint, c.HoseOutletDir, transform, c.transform);
        c.Flow = _flame.Value ? 1f : 0f;
    }

    private void UpdateVisual(float dt)
    {
        bool on = _flame.Value;
        _ignite = Mathf.MoveTowards(_ignite, on ? 1f : 0f, dt * (on ? 4f : 6f));

        if (on != _registered)
        {
            _registered = on;
            if (on) { IgnitionRegistry.Register(Nozzle); HeatJets.Register(this); }
            else { IgnitionRegistry.Unregister(Nozzle); HeatJets.Unregister(this); }
        }

        if (_light != null)
        {
            _light.enabled = _ignite > 0.01f;
            if (_light.enabled)
            {
                float f = 0.8f + Mathf.PerlinNoise(Time.time * 20f, 0.7f) * 0.4f;
                _light.color = Color.Lerp(_flameTip, _flameCore, 0.3f);
                _light.intensity = _lightIntensity * f * _ignite * PressureK;
                _light.range = _lightRange * PressureK;
                _light.transform.position = Nozzle.position + Nozzle.forward * JetLength * 0.4f;
            }
        }
        if (_roarLoop != null)
        {
            if (on && !_roarLoop.isPlaying) _roarLoop.Play();
            else if (!on && _roarLoop.isPlaying) _roarLoop.Stop();
            _roarLoop.pitch = 0.85f + 0.25f * PressureK;
        }

        // v14: мультяшный огонь
        if (_toonFlame)
        {
            if (_toon == null && !_toonFailed && _ignite > 0.01f)
            {
                _toon = ToonGasFlame.Create("RoofingToonFlame (auto)", ToonGasFlame.Kind.Roofing);
                _toonFailed = _toon == null;
            }
            if (_toon != null)
            {
                _toon.Tick(Nozzle, _ignite, JetLength, JetRadius * _toonWidth, transform);
                return;
            }
        }

        if (_ignite <= 0.01f) return;
        if (_ps == null) _ps = CreateFlame();
        _carry += _flamePerSecond * dt * _ignite * PressureK;
        int n = Mathf.Min(30, Mathf.FloorToInt(_carry));
        if (n <= 0) return;
        _carry -= n;
        Transform nz = Nozzle;
        float L = JetLength, R = JetRadius;
        var ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < n; i++)
        {
            bool core = i % 3 == 0;
            float along = core ? Random.Range(0f, 0.25f) : Random.Range(0f, 0.6f);
            ep.position = nz.position + nz.forward * (along * L * 0.4f) + Random.insideUnitSphere * R * 0.25f * (0.4f + along);
            ep.velocity = nz.forward * Random.Range(4f, 7f) * L + Random.insideUnitSphere * 0.6f;
            ep.startColor = core ? new Color(_flameCore.r, _flameCore.g, _flameCore.b, 0.9f)
                                 : Color.Lerp(new Color(1f, 0.85f, 0.4f, 0.95f), _flameTip, Random.value);
            ep.startSize = core ? R * Random.Range(0.6f, 0.9f) : R * Random.Range(0.9f, 1.8f);
            ep.startLifetime = core ? Random.Range(0.05f, 0.09f) : Random.Range(0.1f, 0.22f);
            _ps.Emit(ep, 1);
        }
    }

    private ParticleSystem CreateFlame()
    {
        var go = new GameObject("RoofingFlame (auto)") { hideFlags = HideFlags.DontSave };
        go.SetActive(false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = 1500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.gravityModifier = -0.15f;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.6f));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.5f, 0.3f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.6f;
        noise.frequency = 2.5f;
        noise.scrollSpeed = 2f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (s != null)
        {
            var m = new Material(s) { name = "RoofingFlame (auto)", hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            Texture2D tex = CCRuntimeParticles.SharedMaterial != null ? CCRuntimeParticles.SharedMaterial.mainTexture as Texture2D : null;
            if (tex != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            r.sharedMaterial = m;
        }
        go.SetActive(true);
        ps.Play();
        return ps;
    }

    private void EnsureLight()
    {
        if (_light != null) return;
        var go = new GameObject("RoofingLight (auto)");
        go.transform.SetParent(transform, false);
        _light = go.AddComponent<Light>();
        _light.type = LightType.Point;
        _light.shadows = LightShadows.None;
        _light.enabled = false;
    }

    // ───────────────────────── АВТО-ТОЧКИ ПО МОДЕЛИ ─────────────────────────

    /// <summary>
    /// Сопло — центр кромки стакана (Bell), ось — от гайки стакана (BellNut) к стакану.
    /// Нужны только габариты частей — Read/Write у модели не обязателен.
    /// </summary>
    private Transform AutoNozzle()
    {
        MeshFilter bell = FindMesh("bell", "bellnut");
        if (bell == null)
        {
            Debug.LogWarning($"[Кровельная горелка] «{name}»: не задан Nozzle и нет части Bell — пламя пойдёт из центра вперёд. " +
                             "Создай пустой объект на кромке стакана (синяя ось наружу) и назначь в Nozzle", this);
            return null;
        }
        Bounds bb = bell.GetComponent<Renderer>().bounds;
        MeshFilter nut = FindMesh("bellnut", null);
        Vector3 from = nut != null ? nut.GetComponent<Renderer>().bounds.center : PortGuess();
        Vector3 dir = bb.center - from;
        if (dir.sqrMagnitude < 1e-8f) dir = transform.forward;
        dir.Normalize();
        // Кромка — центр стакана плюс половина его длины вдоль оси.
        float half = Mathf.Abs(dir.x) * bb.extents.x + Mathf.Abs(dir.y) * bb.extents.y + Mathf.Abs(dir.z) * bb.extents.z;
        // Габариты наклонного стакана больше его длины — подрезаем по форме: длина ≈ 0.85 проекции.
        Vector3 mouth = bb.center + dir * half * 0.85f;
        var go = new GameObject("Nozzle (auto)");
        go.transform.SetParent(transform, true);
        go.transform.SetPositionAndRotation(mouth, Quaternion.LookRotation(dir, transform.up));
        return go.transform;
    }

    private Transform AutoPort()
    {
        MeshFilter barb = FindMesh("hosebarb");
        if (barb == null) barb = FindMesh("handle");
        if (barb == null) return null;
        Renderer r = barb.GetComponent<Renderer>();
        Vector3 c = r.bounds.center;
        Vector3 back = -transform.forward;
        Transform nz = _nozzle;
        if (nz != null) back = (c - nz.position).normalized;
        Vector3 e = r.bounds.extents;
        float ext = Mathf.Abs(back.x) * e.x + Mathf.Abs(back.y) * e.y + Mathf.Abs(back.z) * e.z;
        var go = new GameObject("HosePort (auto)");
        go.transform.SetParent(transform, true);
        go.transform.SetPositionAndRotation(c + back * ext, Quaternion.LookRotation(back, transform.up));
        return go.transform;
    }

    private Vector3 PortGuess()
    {
        MeshFilter h = FindMesh("hosebarb");
        if (h == null) h = FindMesh("handle");
        return h != null ? h.GetComponent<Renderer>().bounds.center : transform.position;
    }

    private MeshFilter FindMesh(string key, string notKey = null)
    {
        foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            string n = mf.name.ToLowerInvariant();
            if (n.Contains(key) && (notKey == null || !n.Contains(notKey))) return mf;
        }
        return null;
    }

    // ───────────────────────── ОТЛАДКА ─────────────────────────

    private void OnGUI()
    {
        if (!_debugHud || !base.IsOwner) return;
        TorchGasCylinder c = Cylinder;
        string gas = c == null ? "НЕТ БАЛЛОНА" : $"газ {c.Gas01 * 100f:0}% · давление {c.Pressure}/5 (колесо) · шланг {Vector3.Distance(PortPoint, c.HoseOutletPoint):0.0}/{_hoseLength:0.#} м";
        string st = _flame.Value ? "ГОРИТ" + (_valve.Value ? " (вентиль открыт: G — поставить)" : "")
                  : (_held.Value || _valve.Value) ? "НЕ ГОРИТ: " + WhyText(_why.Value)
                  : "ЛКМ — огонь, ПКМ — вентиль";
        var style = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
        style.normal.textColor = _flame.Value ? new Color(1f, 0.6f, 0.2f) : Color.gray;
        GUI.Box(new Rect(12f, 336f, 760f, 26f), $"Кровельная горелка → {st}   [{gas}]", style);
    }

    private void OnDrawGizmosSelected()
    {
        Transform n = _nozzle != null ? _nozzle : transform;
        Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.9f);
        Gizmos.DrawLine(n.position, n.position + n.forward * _flameLength);
        Gizmos.DrawWireSphere(n.position + n.forward * _flameLength, _flameRadius);
        if (_hosePort != null) { Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(_hosePort.position, 0.02f); }
    }
}
