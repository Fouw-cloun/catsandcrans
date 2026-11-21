using CatCooks.Food.Tools;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using Food = CatCooks.Food;
using Cookware = CatCooks.Food.Cookware;
using FoodPiece = CatCooks.Food.FoodPiece;
using SupplyCrate = CatCooks.Food.Supply.SupplyCrate;
using System.Collections.Generic;

public sealed class CCMiniExcavatorVehicle : ExcavatorVehicle, IScoopSource
{
    public CCMiniExcavatorAttachment[] attachments;
    public Cookware bucket;
    public LayerMask toolMask = ~0;
    [Min(1)] public float maxCargoMass = 350f;
    [Tooltip("Вилы: радиус поиска ящика поставки от кончика вил (ящик большой — кончик может быть под полозьями).")]
    [Min(.1f)] public float forkCrateReach = 1.8f;
    [Tooltip("Вилы: ящик берётся, только если его дно не выше кончика вил на это значение (вилы должны быть под ящиком).")]
    public float forkCrateMaxAbove = .9f;
    [Tooltip("v25.5: траншеекопатель по туше — сколько шва пропиливает за касание (× ширины шва). Больше ручной пилы, но грубее")]
    [Range(.5f, 5f)] public float trencherCarcassWidth = 1.7f;
    [Tooltip("v28.5: дисковая пила по туше — ширина пропила за касание (× ширины шва). Тоньше траншеекопателя")]
    [Range(.3f, 5f)] public float sawCarcassWidth = 1.1f;

    /// <summary>v28.5: размер машины (масштаб корня; 1 — исходный, 2 — увеличенный). Все расстояния в коде × Scale.</summary>
    public float Scale => Mathf.Max(.01f, transform.lossyScale.x);
    private float Reach(CCMiniExcavatorAttachment t) => t.reach * Scale;

    private readonly SyncVar<int> _selected = new(0);
    private readonly SyncVar<bool> _power = new(false);
    private readonly SyncVar<bool> _grip = new(false);
    private readonly SyncVar<bool> _stuck = new(false);                  // v25.6: цепь траншеекопателя застряла в туше
    // v25.6b: мульчер — наматывает мясо на винт и перегревается
    private readonly SyncVar<byte> _wound = new(0);
    private readonly SyncVar<ushort> _woundProduct = new(0);
    private readonly SyncVar<bool> _overheat = new(false);
    private readonly SyncVar<byte> _heatNet = new(0);
    private float _heat, _woundAcc;
    private ParticleSystem _steam;
    private readonly Collider[] _contacts = new Collider[256];   // v25.2: у ящика с продуктами много коллайдеров — 64 не хватало
    private readonly RaycastHit[] _parkingHits = new RaycastHit[24];
    private const RigidbodyConstraints ParkingAxes =
        RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ;
    private RigidbodyConstraints _addedParkingAxes;
    private bool _parkingHeld;
    private Rigidbody _cargo;
    private NetworkObject _cargoNetwork;
    private Vector3 _cargoOffset, _lastPosition;
    private Quaternion _cargoRotation;
    // v24.7: содержимое ящика едет вместе с ним (иначе продукты вываливаются при подъёме).
    private struct Rider { public Rigidbody body; public Vector3 pos; public Quaternion rot; public bool wasKinematic; }
    private readonly List<Rider> _riders = new();
    private readonly Collider[] _inside = new Collider[256];
    private float _inputExpires, _nextInput, _nextHit, _speed;
    private int _visibleTool = -1;
    private bool _sentPower;

    private bool InWorkMode => !base.CanDrive;
    private CCMiniExcavatorAttachment ActiveTool =>
        attachments != null && _selected.Value >= 0 && _selected.Value < attachments.Length ?
            attachments[_selected.Value] : null;
    protected override bool CanDrive => base.CanDrive && !UiModal.AnyOpen && !_stuck.Value;

    /// <summary>v25.6: туша зажала цепь — экскаватор не едет, цепь стоит, пока цепь не отпилят пилой.</summary>
    [Server]
    public void ServerSetStuck(bool stuck)
    {
        if (_stuck.Value == stuck) return;
        _stuck.Value = stuck;
        if (stuck) { _inputExpires = 0f; _power.Value = false; }
        Debug.Log(stuck ? "[Мини-экскаватор] цепь ЗАСТРЯЛА в туше — не едет, пока цепь не отпилят пилой" : "[Мини-экскаватор] цепь свободна", this);
    }

    protected override void DriverUpdate()
    {
        if (UiModal.AnyOpen) return;
        base.DriverUpdate(); // Tab and arm controls from the full-size excavator.
        if (!InWorkMode) return;
        if (Input.GetKeyDown(KeyCode.G)) RequestGrip();
        if (_body != null && _body.linearVelocity.sqrMagnitude > .25f) return;
        if (attachments == null) return;
        for (int i = 0; i < attachments.Length && i < 9; i++)
        {
            if (!Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i))) continue;
            RequestTool(i);
            break;
        }
    }

    protected override void DriverTick()
    {
        base.DriverTick();
        var tool = ActiveTool;
        bool power = InWorkMode && !UiModal.AnyOpen && tool != null &&
            HasMotor(tool.kind) && Input.GetKey(KeyCode.Y);
        if (power == _sentPower && (!power || Time.time < _nextInput)) return;
        _sentPower = power;
        _nextInput = Time.time + .12f;
        RequestPower(power);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RequestTool(int index)
    {
        if (!HasDriver || !InWorkMode || !Stopped() || _power.Value || _grip.Value ||
            attachments == null || index < 0 || index >= attachments.Length ||
            index == _selected.Value || attachments[index] == null) return;
        if (_selected.Value == 0 && bucket != null)
        {
            if (bucket.TotalCount > 0) return;
            foreach (var piece in bucket.Loose)
                if (piece != null && !piece.IsConsumed && !piece.IsHeld && bucket.IsInside(piece.transform.position))
                    return;
        }

        _inputExpires = 0;
        _selected.Value = index;
        ApplyToolSelection();
    }

    [ServerRpc(RequireOwnership = true)]
    private void RequestPower(bool power)
    {
        var tool = ActiveTool;
        _inputExpires = power && HasDriver && InWorkMode && tool != null &&
            HasMotor(tool.kind) ? Time.time + .4f : 0f;
    }

    [ServerRpc(RequireOwnership = true)]
    private void RequestGrip()
    {
        var tool = ActiveTool;
        if (!HasDriver || !InWorkMode || tool == null || !HasGrip(tool.kind)) return;
        if (_grip.Value)
        {
            ReleaseCargo();
            return;
        }

        Rigidbody candidate = FindCargo(tool, out NetworkObject networkObject);
        if (tool.kind == CCMiniTool.Forks)                                       // v25.2: понятно, почему не взял
            Debug.Log(candidate != null ? $"[Мини-экскаватор] вилы: взял «{candidate.name}» ({candidate.mass:0} кг)"
                : $"[Мини-экскаватор] вилы: рядом с кончиком (≤{Mathf.Max(Reach(tool), forkCrateReach * Scale):0.0} м) нет ящика/поддона, или его дно выше вил больше чем на {forkCrateMaxAbove * Scale:0.0} м. Кончик: {(tool.tip != null ? tool.tip.position.ToString("F1") : "НЕ ЗАДАН")}", this);
        _grip.Value = candidate != null || tool.kind != CCMiniTool.Forks;
        if (candidate == null) return;
        _cargo = candidate;
        _cargoNetwork = networkObject;
        _cargoOffset = tool.tip.InverseTransformPoint(_cargo.position);
        _cargoRotation = Quaternion.Inverse(tool.tip.rotation) * _cargo.rotation;
        _cargo.linearVelocity = Vector3.zero;
        _cargo.angularVelocity = Vector3.zero;
        _cargo.isKinematic = true;
        CollectRiders();
    }

    protected override void ApplyDriverState(NetworkObject player, bool entering)
    {
        base.ApplyDriverState(player, entering);
        if (!entering)
        {
            _sentPower = false;
            if (IsServerInitialized) _inputExpires = 0f;
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _lastPosition = transform.position;
    }

    public override void OnStopServer()
    {
        ReleaseCargo();
        base.OnStopServer();
    }

    protected override void Update()
    {
        base.Update();
        if (!IsServerInitialized) return;
        var tool = ActiveTool;
        float dt = Time.deltaTime;
        Vector3 position = transform.position;
        if (dt > .0001f)
            _speed = Mathf.Lerp(_speed, (position - _lastPosition).magnitude / dt, 1f - Mathf.Exp(-12f * dt));
        _lastPosition = position;

        _power.Value = HasDriver && InWorkMode && tool != null && HasMotor(tool.kind) &&
            Time.time < _inputExpires && !_stuck.Value && !(tool.kind == CCMiniTool.Mulcher && _overheat.Value);
        // v25.6b: перегрев мульчера — греется, пока крутится; остывает, пока стоит
        bool mulching = _power.Value && tool != null && tool.kind == CCMiniTool.Mulcher;
        _heat = Mathf.Clamp01(_heat + (mulching ? dt / mulchOverheatSeconds : -dt / mulchCooldownSeconds * .7f));
        if (!_overheat.Value && _heat >= 1f) { _overheat.Value = true; Debug.Log("[Мини-экскаватор] мульчер ПЕРЕГРЕЛСЯ — пусть остынет", this); }
        else if (_overheat.Value && _heat <= .3f) { _overheat.Value = false; Debug.Log("[Мини-экскаватор] мульчер остыл", this); }
        byte hn = (byte)Mathf.RoundToInt(_heat * 100f);
        if (hn / 5 != _heatNet.Value / 5) _heatNet.Value = hn;
        if (_power.Value && tool.tip != null) RunTool(tool, Mathf.Min(dt, .05f));
    }

    public override void OnStopNetwork()
    {
        ReleaseParkingBrake();
        base.OnStopNetwork();
    }

    protected override void VehicleFixedUpdate()
    {
        bool driving = IsDriving && !Input.GetKey(KeyCode.Space) &&
            (Mathf.Abs(Input.GetAxisRaw("Vertical")) > .01f ||
             Mathf.Abs(Input.GetAxisRaw("Horizontal")) > .01f);
        if (driving) ReleaseParkingBrake();

        base.VehicleFixedUpdate();
        if (driving) return;

        Vector3 velocity = _body.linearVelocity;
        if (GroundContacts < 4 || Vector3.Dot(transform.up, Vector3.up) < .65f ||
            Mathf.Abs(velocity.y) > .45f ||
            !HasStaticSupport(leftTrackPoints) || !HasStaticSupport(rightTrackPoints))
        {
            ReleaseParkingBrake();
            return;
        }

        if (_parkingHeld) return;
        Vector3 travel = new Vector3(velocity.x, 0f, velocity.z);
        if (travel.sqrMagnitude > .23f * .23f)
        {
            // Settle gently before holding the horizontal position.
            Vector3 brake = Vector3.ClampMagnitude(-travel / Time.fixedDeltaTime, 5f);
            _body.AddForce(brake, ForceMode.Acceleration);
            return;
        }

        _body.linearVelocity = new Vector3(0f, velocity.y, 0f);
        _addedParkingAxes = ParkingAxes & ~_body.constraints;
        _body.constraints |= ParkingAxes;
        _parkingHeld = true;
    }

    private bool HasStaticSupport(Transform[] points)
    {
        if (points == null) return false;
        Vector3 down = -transform.up;
        float reach = restLength + trackRadius;
        foreach (Transform point in points)
        {
            if (point == null) continue;
            if (CCVehicleGround.Cast(transform, _body, point.position, down, reach,
                groundMask, _parkingHits, out RaycastHit hit) && hit.rigidbody == null)
                return true;
        }
        return false;
    }

    private void ReleaseParkingBrake()
    {
        if (!_parkingHeld) return;
        if (_body != null) _body.constraints &= ~_addedParkingAxes;
        _addedParkingAxes = RigidbodyConstraints.None;
        _parkingHeld = false;
    }

    protected override void FixedUpdate()
    {
        if (!HasMoveAuthority || _body == null || _body.isKinematic) ReleaseParkingBrake();
        base.FixedUpdate();
        if (!IsServerInitialized || _cargo == null) return;
        var tool = ActiveTool;
        if (!CargoAvailable() || tool == null || tool.tip == null)
        {
            ReleaseCargo();
            return;
        }
        _cargo.MovePosition(tool.tip.TransformPoint(_cargoOffset));
        _cargo.MoveRotation(tool.tip.rotation * _cargoRotation);
        MoveRiders(tool.tip.TransformPoint(_cargoOffset), tool.tip.rotation * _cargoRotation);
    }

    protected override void ApplyVisuals()
    {
        base.ApplyVisuals();
        ApplyToolSelection();
        var tool = ActiveTool;
        if (tool != null) tool.Animate(_power.Value, _grip.Value, Time.deltaTime);
    }

    private void ApplyToolSelection()
    {
        if (attachments == null || _visibleTool == _selected.Value) return;
        _visibleTool = _selected.Value;
        for (int i = 0; i < attachments.Length; i++)
        {
            var attachment = attachments[i];
            if (attachment != null) attachment.Show(i == _visibleTool);
        }
    }

    private bool Stopped() =>
        HasMoveAuthority && _body != null ? _body.linearVelocity.sqrMagnitude <= .25f : _speed <= .5f;

    private static bool HasGrip(CCMiniTool kind) =>
        kind == CCMiniTool.Clamp || kind == CCMiniTool.Grapple || kind == CCMiniTool.Forks;

    private static bool HasMotor(CCMiniTool kind) =>
        kind == CCMiniTool.Trencher || kind == CCMiniTool.Mulcher ||
        kind == CCMiniTool.Hammer || kind == CCMiniTool.Mixer || kind == CCMiniTool.Saw;

    private bool CargoAvailable()
    {
        if (_cargoNetwork == null || !_cargoNetwork.IsSpawned || (_cargoNetwork.Owner != null && _cargoNetwork.Owner.IsValid)) return false;
        var food = _cargo.GetComponentInParent<FoodPiece>();
        return food == null || (!food.IsConsumed && !food.IsHeld);
    }

    private void ReleaseCargo()
    {
        if (_cargo != null && CargoAvailable())
        {
            _cargo.isKinematic = false;
            _cargo.WakeUp();
        }
        DropRiders();
        _cargo = null;
        _cargoNetwork = null;
        if (IsServerInitialized) _grip.Value = false;
    }

    private Rigidbody FindCargo(CCMiniExcavatorAttachment tool, out NetworkObject networkObject)
    {
        networkObject = null;
        if (tool.tip == null) return null;
        Vector3 point = tool.tip.position;
        bool forks = tool.kind == CCMiniTool.Forks;
        int count = forks ?
            Physics.OverlapSphereNonAlloc(point, Mathf.Max(Reach(tool), forkCrateReach * Scale), _contacts, toolMask, QueryTriggerInteraction.Ignore) :
            Scan(tool);
        float nearest = float.PositiveInfinity;
        Rigidbody result = null;
        for (int i = 0; i < count; i++)
        {
            var col = _contacts[i];
            Rigidbody body = col.attachedRigidbody;
            if (body == null || body.isKinematic || body.mass > maxCargoMass ||
                !body.TryGetComponent(out NetworkObject net) || !net.IsSpawned ||
                (net.Owner != null && net.Owner.IsValid) || body.transform.IsChildOf(transform)) continue;
            var food = body.GetComponentInParent<FoodPiece>();
            var pallet = body.GetComponentInParent<BreakablePallet>();
            bool canLiftFood = tool.kind != CCMiniTool.Forks && food != null && !food.IsHeld && !food.IsConsumed;
            // v24.7: вилы поднимают ящики поставки (S/M/L) — если вилы подведены под дно.
            bool canLiftCrate = forks && body.GetComponent<SupplyCrate>() != null &&
                body.GetComponentInChildren<Collider>().bounds.min.y <= point.y + forkCrateMaxAbove * Scale;   // v25.2: дно ящика не выше вил (+запас)
            if (!canLiftFood && pallet == null && !canLiftCrate) continue;

            float distance = (col.ClosestPoint(point) - point).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance;
            result = body;
            networkObject = net;
        }
        return result;
    }

    private void CollectRiders()
    {
        _riders.Clear();
        if (_cargo == null) return;
        var crate = _cargo.GetComponent<SupplyCrate>();
        if (crate == null) return;
        Transform ct = _cargo.transform;
        Vector3 inner = crate.InnerSize;
        Vector3 center = ct.TransformPoint(crate.InnerBottom + Vector3.up * (inner.y * .5f + .25f));
        Vector3 half = new Vector3(inner.x * .5f, inner.y * .5f + .3f, inner.z * .5f);
        int n = Physics.OverlapBoxNonAlloc(center, half, _inside, ct.rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Rigidbody rb = _inside[i].attachedRigidbody;
            if (rb == null || rb == _cargo || rb.transform.IsChildOf(transform)) continue;
            if (rb.TryGetComponent(out NetworkObject net) && net.Owner != null && net.Owner.IsValid) continue; // в руке у игрока
            bool dup = false;
            foreach (var r in _riders) if (r.body == rb) { dup = true; break; }
            if (dup) continue;
            _riders.Add(new Rider
            {
                body = rb,
                pos = ct.InverseTransformPoint(rb.position),
                rot = Quaternion.Inverse(ct.rotation) * rb.rotation,
                wasKinematic = rb.isKinematic
            });
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            rb.isKinematic = true;
        }
    }

    private void MoveRiders(Vector3 cratePos, Quaternion crateRot)
    {
        for (int i = _riders.Count - 1; i >= 0; i--)
        {
            var r = _riders[i];
            if (r.body == null) { _riders.RemoveAt(i); continue; }
            // игрок схватил продукт из ящика — отпускаем его
            if (r.body.TryGetComponent(out NetworkObject net) && net.Owner != null && net.Owner.IsValid)
            { _riders.RemoveAt(i); continue; }
            r.body.MovePosition(cratePos + crateRot * r.pos);
            r.body.MoveRotation(crateRot * r.rot);
        }
    }

    private void DropRiders()
    {
        foreach (var r in _riders)
        {
            if (r.body == null) continue;
            if (r.body.TryGetComponent(out NetworkObject net) && net.Owner != null && net.Owner.IsValid) continue;
            r.body.isKinematic = r.wasKinematic;
            if (!r.wasKinematic) r.body.WakeUp();
        }
        _riders.Clear();
    }

    [Server]
    private void Wind(ushort product, int n)
    {
        if (n <= 0) return;
        _woundProduct.Value = product;
        int w = Mathf.Min(255, _wound.Value + n);
        _wound.Value = (byte)w;
        if (w >= mulchWoundLimit) Debug.Log($"[Мини-экскаватор] на винт мульчера НАМОТАЛО мясо ({w}) — встал. Сними мастерком или лопатой", this);
    }

    // ── v25.6b: IScoopSource — намотанное на винт мульчера снимают мастерком / лопатой ──
    public bool PeekScoop(out ushort product, out Food.FoodState state)
    {
        product = _woundProduct.Value;
        state = Food.FoodState.Minced;
        var tool = ActiveTool;
        return _wound.Value > 0 && product != 0 && tool != null && tool.kind == CCMiniTool.Mulcher && !_power.Value;
    }

    public int ServerScoop(int max, out ushort product, out Food.FoodState state, out byte cook, out byte clean)
    {
        cook = 0; clean = 45;                                                      // с винта — не первой свежести
        if (!PeekScoop(out product, out state) || max <= 0) return 0;
        int got = Mathf.Min(max, Mathf.Min(3, _wound.Value));
        _wound.Value = (byte)(_wound.Value - got);
        Debug.Log($"[Мини-экскаватор] сняли с винта {got} порц., осталось {_wound.Value}", this);
        return got;
    }

    private static GUIStyle _mulchHint;

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        var tool = ActiveTool;
        if (tool == null || tool.kind != CCMiniTool.Mulcher) return;
        bool wound = _wound.Value >= mulchWoundLimit, hot = _overheat.Value;
        bool warm = _heatNet.Value >= 70;
        if (!wound && !hot && !warm && _wound.Value == 0) return;
        Camera cam = Camera.main;
        bool near = IsDriving || (cam != null && (cam.transform.position - tool.transform.position).sqrMagnitude < 64f);
        if (!near) return;
        string t = hot ? $"Мульчер ПЕРЕГРЕЛСЯ — остывает ({_heatNet.Value}%)"
                 : wound ? $"На винт мульчера намотало мясо — встал. Мотор выкл, сними мастерком или лопатой (ЛКМ по мульчеру): {_wound.Value}"
                 : warm ? $"Мульчер греется: {_heatNet.Value}% — дай передышку"
                 : $"На винте мясо: {_wound.Value}/{mulchWoundLimit}";
        if (_mulchHint == null) { _mulchHint = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true }; _mulchHint.normal.textColor = Color.white; }
        float w = Mathf.Min(760f, Screen.width - 40f);
        GUI.Box(new Rect((Screen.width - w) * .5f, Screen.height * .34f, w, 42f), t, _mulchHint);
    }

    private void LateUpdate()
    {
        // пар над перегретым мульчером (у всех)
        var tool = ActiveTool;
        bool on = _overheat.Value && tool != null && tool.kind == CCMiniTool.Mulcher && tool.tip != null && !Application.isBatchMode;
        if (on && _steam == null)
        {
            var go = new GameObject("Mulcher Steam (auto)") { hideFlags = HideFlags.DontSave };
            _steam = go.AddComponent<ParticleSystem>();
            _steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _steam.main; main.loop = true; main.startLifetime = 1.6f; main.startSpeed = 1.2f;
            main.startSize = new ParticleSystem.MinMaxCurve(.4f, 1f); main.startColor = new Color(1f, 1f, 1f, .5f);
            main.gravityModifier = -.15f; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = _steam.emission; em.rateOverTime = 25f;
            var sh = _steam.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = .3f * Scale;
            var r = go.GetComponent<ParticleSystemRenderer>();
            Shader sd = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sd != null) r.sharedMaterial = new Material(sd) { hideFlags = HideFlags.DontSave };
        }
        if (_steam == null) return;
        if (tool != null && tool.tip != null) _steam.transform.position = tool.tip.position;
        if (on && !_steam.isEmitting) _steam.Play();
        else if (!on && _steam.isEmitting) _steam.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private float _mulchT;
    [Tooltip("v25.6: мульчер в таре — секунд на один кусок мяса (кусок → фарш прямо в таре)")]
    [Min(.1f)] public float mulchSecondsPerPiece = .6f;
    [Header("v25.6b: мульчер ломается")]
    [Tooltip("Сколько порций мяса намотается на винт — и он встанет. Снимать мастерком или лопатой (ЛКМ по мульчеру)")]
    [Range(2, 40)] public int mulchWoundLimit = 10;
    [Tooltip("Шанс, что кусок намотается на винт (за каждый смолотый кусок)")]
    [Range(0f, 1f)] public float mulchWindChance = .35f;
    [Tooltip("Сколько секунд мотор (мульчер) работает подряд до перегрева")]
    [Min(5f)] public float mulchOverheatSeconds = 90f;
    [Tooltip("Сколько секунд остывать после перегрева (до 30% нагрева)")]
    [Min(3f)] public float mulchCooldownSeconds = 40f;

    /// <summary>
    /// v25.6: мульчер опущен в тару (корыто, ведро, кузов) — куски мяса в ней по одному мелются в фарш:
    /// кусок исчезает, в таре прибавляется фарш (столько порций, сколько дал бы измельчитель).
    /// </summary>
    private bool MulchInCookware(Vector3 point, float dt)
    {
        Cookware cw = null;
        float sc = Scale, closest = .9f * sc * .9f * sc;
        foreach (var c in Cookware.All)
        {
            if (c == null || c == bucket || !c.IsServerInitialized) continue;
            if (c.IsInside(point)) { cw = c; break; }
            Vector3 d = c.MouthPoint - point;
            float h = d.x * d.x + d.z * d.z;
            if (h >= closest || d.y < -.3f * sc || d.y > .8f * sc) continue;
            cw = c; closest = h;
        }
        if (cw == null) return false;
        FoodPiece best = null; float bd = float.PositiveInfinity;
        foreach (FoodPiece p in cw.Loose)
        {
            if (p == null || p.IsHeld || p.IsConsumed || !cw.IsInside(p.transform.position)) continue;
            if (!ShredderStation.MinceOf(p, out _, out _, out _)) continue;
            float d = (p.transform.position - point).sqrMagnitude;
            if (d < bd) { bd = d; best = p; }
        }
        if (best == null) return false;
        _mulchT += dt;
        cw.ServerAgitate(.3f);
        if (_mulchT < mulchSecondsPerPiece) return true;
        _mulchT = 0f;
        if (!ShredderStation.MinceOf(best, out ushort product, out Food.FoodState state, out int portions)) return true;
        byte cook = best.Cook;
        if (!best.ServerTryConsume()) return true;
        int added = cw.ServerAddContent(product, state, portions, cook);
        Wind(product, Random.value < mulchWindChance ? Random.Range(1, 4) : 0);
        Debug.Log($"[Мини-экскаватор] мульчер: «{best.Def?.Title}» → фарш {added} порц. в «{cw.Def?.Title}»", this);
        best.NetworkObject.Despawn();
        return true;
    }

    private float _lastCarcassSaw = -10f;

    /// <summary>v25.5: траншеекопатель у туши — пропиливает шов там, где цепь касается (грубо и широко).</summary>
    private bool SawCarcass(CCMiniExcavatorAttachment tool)
    {
        int count = Scan(tool);
        Vector3 tip = tool.tip.position;
        Food.FoodCarcass best = null; Vector3 at = tip; float bd = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider col = _contacts[i];
            if (col == null || col.transform.IsChildOf(transform)) continue;
            var fc = col.GetComponentInParent<Food.FoodCarcass>();
            if (fc == null || !fc.TraceSeam || !fc.CanCut) continue;
            Vector3 p = col.ClosestPoint(tip);
            float d = (p - tip).sqrMagnitude;
            // v28.5: коллайдер куска — коробка больше мяса; кончик внутри коробки → пилим там, где кончик
            if (d < bd) { bd = d; best = fc; at = d < 1e-6f ? tip : p; }
        }
        if (best == null) return false;
        if (_stuck.Value) return true;                                      // застряла — цепь стоит
        if (Time.time - _lastCarcassSaw < .08f) return true;
        bool newStroke = Time.time - _lastCarcassSaw > .4f;
        _lastCarcassSaw = Time.time;
        best.ServerSawAt(at, tool.kind == CCMiniTool.Saw ? sawCarcassWidth : trencherCarcassWidth, newStroke, NetworkObject, Food.FoodCarcass.JamKind.Trencher);
        return true;
    }

    private int Scan(CCMiniExcavatorAttachment tool) =>
        Physics.OverlapSphereNonAlloc(tool.tip.position, Reach(tool), _contacts, toolMask,
            QueryTriggerInteraction.Collide);

    private void RunTool(CCMiniExcavatorAttachment tool, float dt)
    {
        if ((tool.kind == CCMiniTool.Trencher || tool.kind == CCMiniTool.Saw) && SawCarcass(tool)) return;   // v25.5 / v28.5: траншеекопатель и диск пилят тушу по шву
        if (tool.kind == CCMiniTool.Mulcher && (_wound.Value >= mulchWoundLimit || _overheat.Value)) return;   // v25.6b: намотало / перегрев — винт стоит
        if (tool.kind == CCMiniTool.Mulcher && MulchInCookware(tool.tip.position, dt)) return;   // v25.6: мульчер в таре — фарш
        if (tool.kind == CCMiniTool.Mixer)
        {
            Mix(tool.tip.position, dt);
            return;
        }

        if (tool.kind == CCMiniTool.Hammer && Time.time < _nextHit) return;
        int count = Scan(tool);
        Vector3 point = tool.tip.position;
        float nearest = float.PositiveInfinity;
        FoodPiece piece = null;
        IHammerTarget target = null;
        Food.FoodProcess process = Food.FoodProcess.None;

        for (int i = 0; i < count; i++)
        {
            Collider col = _contacts[i];
            if (col.transform.IsChildOf(transform)) continue;
            float distance = (col.ClosestPoint(point) - point).sqrMagnitude;
            if (distance >= nearest) continue;

            var hitTarget = tool.kind == CCMiniTool.Hammer ? col.GetComponentInParent<IHammerTarget>() : null;
            var hitFood = col.GetComponentInParent<FoodPiece>();
            Food.FoodProcess work = GetProcess(tool.kind, hitFood);
            if (hitTarget == null && work == Food.FoodProcess.None) continue;
            nearest = distance;
            target = hitTarget;
            piece = hitFood;
            process = work;
        }

        if (tool.kind == CCMiniTool.Hammer)
        {
            if (target == null && piece == null) return;
            _nextHit = Time.time + .26f;
            if (target != null) target.ServerHammerHit(point, -tool.transform.up);
            else piece.ServerAddWork(process, .26f);
        }
        else if (piece != null)
        {
            piece.ServerAddWork(process, dt);
            if (tool.kind == CCMiniTool.Mulcher && process == Food.FoodProcess.Mince)
            {
                _woundAcc += dt * mulchWindChance * .5f;                           // мелешь по полу — тоже наматывается
                if (_woundAcc >= 1f) { _woundAcc -= 1f; Wind(piece.ProductId, 1); }
            }
        }
    }

    private static Food.FoodProcess GetProcess(CCMiniTool kind, FoodPiece piece)
    {
        if (piece == null || piece.IsHeld || piece.IsConsumed) return Food.FoodProcess.None;
        if ((kind == CCMiniTool.Trencher || kind == CCMiniTool.Saw) && piece.CanApplyNow(Food.FoodProcess.Cut)) return Food.FoodProcess.Cut;
        if (kind == CCMiniTool.Hammer && piece.CanApplyNow(Food.FoodProcess.Pound)) return Food.FoodProcess.Pound;
        if (kind != CCMiniTool.Mulcher) return Food.FoodProcess.None;

        if (piece.CanApplyNow(Food.FoodProcess.Mince)) return Food.FoodProcess.Mince;
        if (piece.CanApplyNow(Food.FoodProcess.Mash)) return Food.FoodProcess.Mash;
        if (piece.CanApplyNow(Food.FoodProcess.Grate)) return Food.FoodProcess.Grate;
        return Food.FoodProcess.None;
    }

    private void Mix(Vector3 point, float dt)
    {
        Cookware nearest = null;
        float sc = Scale, closest = .55f * sc * .55f * sc;
        foreach (var cookware in Cookware.All)
        {
            if (cookware == null || cookware == bucket || !cookware.IsServerInitialized) continue;
            if (cookware.IsInside(point)) { nearest = cookware; break; }
            Vector3 delta = cookware.MouthPoint - point;
            float horizontal = delta.x * delta.x + delta.z * delta.z;
            if (horizontal >= closest || delta.y < -.18f * sc || delta.y > .6f * sc) continue;
            nearest = cookware;
            closest = horizontal;
        }
        if (nearest == null) return;
        nearest.ServerAgitate(.85f);
        if (!Food.CookRecipeBook.ServerProcessAny(nearest, 0f, Food.CookStepKind.Whip, Food.CookStepKind.Mix))
            Food.ChocolateWork.ServerMix(nearest, dt);
    }
}
