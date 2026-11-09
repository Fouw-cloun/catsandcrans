using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using CatCooks.Food.Tools;

/// <summary>
/// КАТОК (дорожный, сидячий). На базе колёсной техники (WheeledVehicle):
///   F — сесть / выйти, W/S — вперёд / назад (медленно), A/D — поворот переднего вальца.
/// Вальцы крутятся по пройденному пути, передняя вилка с вальцом поворачивается (видно всем).
/// Раскатка теста — у самого теста: оно смотрит, где проехали вальцы (All, DrumContact).
///
/// ═══ НАСТРОЙКА ═══ CatCooks → Ньокки → 1. Каток (выдели модель катка).
///   Подвеска — 4 точки (по 2 на валец), без WheelCollider. Вальцы-меши без коллайдеров.
///   Корпус — Box Collider над осями вальцов (лучи подвески его не задевают).
/// </summary>
public class RoadRollerVehicle : WheeledVehicle, CatCooks.Food.IScoopReceiver, ISpatulaTarget
{
    [Header("Каток — вальцы")]
    [Tooltip("Передняя поворотная вилка (рама над вальцом + валец). Поворачивается рулём")]
    [SerializeField] private Transform _frontFork;
    [SerializeField] private Transform _frontDrum;
    [SerializeField] private Transform _rearDrum;
    [Tooltip("Руль (крутится вместе с поворотом). Можно пусто")]
    [SerializeField] private Transform _steeringWheel;
    [Tooltip("Ось руля в его локальных осях (меню 1 считает сам по ободу руля)")]
    [SerializeField] private Vector3 _steeringAxis = Vector3.forward;
    [Tooltip("Центр обода руля в его локальных осях — крутится вокруг него, без «восьмёрки»")]
    [SerializeField] private Vector3 _steeringPivot = Vector3.zero;
    [SerializeField] private float _steeringTurns = 2.5f;
    [Tooltip("Радиус вальца, м")]
    [SerializeField] private float _drumRadius = 0.35f;
    [Tooltip("Ширина вальца, м")]
    [SerializeField] private float _drumWidth = 0.65f;
    [Tooltip("Макс. скорость, м/с (каток медленный)")]
    [SerializeField] private float _maxSpeed = 2.2f;
    [SerializeField] private bool _logRoller = true;
    [Tooltip("Насколько куча теста перед вальцом тормозит каток (0 — не тормозит, 0.8 — почти встаёт)")]
    [Range(0f, 0.9f)][SerializeField] private float _plowDrag = 0.6f;

    [Header("Мука на вальцах (иначе тесто липнет)")]
    [Tooltip("Мука за одну порцию мастерка на валец (1 = полностью)")]
    [Range(0.1f, 1f)][SerializeField] private float _flourPerPortion = 0.5f;
    [Tooltip("Расход муки на метр езды ПО тесту")]
    [SerializeField] private float _flourPerMeter = 0.05f;
    [Tooltip("Сколько налипшего теста (литры) — и валец ЗАЛИП: каток встаёт, пока не порубишь шпателем")]
    [SerializeField] private float _jamLiters = 60f;
    [Tooltip("Залип: тесто держит валец «резинкой» — свободно на столько м, дальше тянет каток назад")]
    [SerializeField] private float _tetherSlack = 0.35f;
    [Tooltip("Сила резинки (ускорение на метр растяжки)")]
    [SerializeField] private float _tetherPull = 9f;

    [Header("Вид от первого лица")]
    [Tooltip("Камера водителя ставится на высоту глаз игрока над сиденьем (берётся с камеры игрока при посадке)")]
    [SerializeField] private bool _firstPerson = true;
    [Tooltip("Мышью можно оглядываться из кабины: влево-вправо, градусы")]
    [SerializeField] private float _lookYaw = 110f;
    [Tooltip("Вверх-вниз, градусы")]
    [SerializeField] private float _lookPitch = 60f;
    [SerializeField] private float _lookSensitivity = 2f;

    public static readonly List<RoadRollerVehicle> All = new List<RoadRollerVehicle>();

    private readonly SyncVar<float> _steerNet = new SyncVar<float>(0f);
    private readonly SyncVar<byte> _flourF = new SyncVar<byte>(0), _flourR = new SyncVar<byte>(0);   // 0..200
    private readonly SyncVar<float> _wrapF = new SyncVar<float>(0f), _wrapR = new SyncVar<float>(0f); // м³ налипшего
    private readonly SyncVar<Vector3> _anchorF = new SyncVar<Vector3>(Vector3.zero), _anchorR = new SyncVar<Vector3>(Vector3.zero); // где тесто схватило валец
    private readonly DoughStrand[] _band = new DoughStrand[2];
    private readonly Transform[] _hit = new Transform[2], _sleeve = new Transform[2];
    private readonly float[] _revAcc = new float[2];
    private Renderer[][] _drumRs = new Renderer[2][];
    private Color[][] _drumBase = new Color[2][];
    private MaterialPropertyBlock _mpb;
    private float _puffAt, _lookAt; private bool _lookingAtMe;
    private Quaternion _forkRel, _frontRel, _rearRel, _wheelBase;
    private Vector3 _wheelBasePos;
    private float _camYaw, _camPitch;
    private bool _captured;
    private float _spinF, _spinR, _steerShown;
    private Vector3 _lastF, _lastR;

    public float DrumRadius => _drumRadius;
    public float DrumWidth => _drumWidth;
    public Transform FrontDrum => _frontDrum;
    public Transform RearDrum => _rearDrum;

    /// <summary>Центр вальца на земле (мир), ось вальца (мир, поперёк) — для теста под катком.</summary>
    public bool DrumContact(int i, out Vector3 groundPoint, out Vector3 axle)
    {
        Transform d = i == 0 ? _frontDrum : _rearDrum;
        groundPoint = Vector3.zero; axle = transform.right;
        if (d == null) return false;
        Vector3 c = d.position;
        axle = i == 0 && _frontFork != null ? Quaternion.AngleAxis(SteerAngle, transform.up) * transform.right : transform.right;
        groundPoint = c - transform.up * _drumRadius;
        return true;
    }

    private float SteerAngle
    {
        get
        {
            if (_isLocalDriver && wheels != null)
                foreach (Wheel w in wheels) if (w != null && w.steering) return w.steerAngle;
            return _steerNet.Value;
        }
    }

    protected override void Awake()
    {
        BuildDrumHits();     // до base.Awake: центр масс корпуса задаётся там и не должен сбиться от новых коллайдеров
        base.Awake();
    }

    /// <summary>Невидимые «попадалки» на вальцах (капсулы чуть меньше вальца — до земли не достают):
    /// по ним кидают муку мастерком и рубят налипшее шпателем. Передняя — на вилке (поворачивается), не крутится.</summary>
    private void BuildDrumHits()
    {
        for (int d = 0; d < 2; d++)
        {
            Transform drum = d == 0 ? _frontDrum : _rearDrum;
            if (drum == null || _hit[d] != null) continue;
            Transform parent = d == 0 && _frontFork != null ? _frontFork : transform;
            var go = new GameObject(d == 0 ? "DrumHitFront" : "DrumHitRear");
            go.transform.SetParent(parent, true);
            Renderer dr = drum.GetComponentInChildren<Renderer>();
            go.transform.SetPositionAndRotation(dr != null ? dr.bounds.center : drum.position, transform.rotation);
            go.transform.localScale = Vector3.one;
            Vector3 ls = go.transform.lossyScale;
            CapsuleCollider cap = go.AddComponent<CapsuleCollider>();
            cap.direction = 0;
            cap.radius = _drumRadius * 0.8f / Mathf.Max(1e-3f, Mathf.Max(ls.y, ls.z));
            cap.height = Mathf.Max(_drumWidth, _drumRadius * 2f) / Mathf.Max(1e-3f, ls.x);
            _hit[d] = go.transform;
            _drumRs[d] = drum.GetComponentsInChildren<Renderer>(true);
            _drumBase[d] = DoughStick.BaseColors(_drumRs[d]);
        }
    }

    private SyncVar<byte> FlourVar(int d) => d == 0 ? _flourF : _flourR;
    private SyncVar<float> WrapVar(int d) => d == 0 ? _wrapF : _wrapR;
    private SyncVar<Vector3> AnchorVar(int d) => d == 0 ? _anchorF : _anchorR;
    private float JamVol => Mathf.Max(0.001f, _jamLiters / 1000f);
    public float DrumFlour01(int d) => FlourVar(d).Value / 200f;
    public float DrumStuck01(int d) => Mathf.Clamp01(WrapVar(d).Value / JamVol);
    public bool DrumJammed(int d) => WrapVar(d).Value >= JamVol;
    private static string DrumName(int d) => d == 0 ? "передний валец" : "задний валец";

    /// <summary>Валец проехал travel м ПО тесту: true — мука есть (тратим), false — голый, тесто липнет.</summary>
    [Server]
    public bool ServerDrumFlour(int d, float travel)
    {
        float c = DrumFlour01(d);
        if (c <= 0.001f) return false;
        float nc = Mathf.Max(0f, c - travel * _flourPerMeter);
        FlourVar(d).Value = (byte)Mathf.RoundToInt(nc * 200f);
        if (nc <= 0.001f && _logRoller) Debug.Log($"[Каток] «{name}»: на {DrumName(d)}е кончилась мука — сейчас начнёт липнуть!", this);
        return true;
    }

    /// <summary>Тесто намоталось на валец (м³). Набралось Jam Liters — валец залип, каток стоит.</summary>
    [Server]
    public void ServerDrumStick(int d, float vol)
    {
        if (vol <= 0f) return;
        bool was = DrumJammed(d), first = WrapVar(d).Value <= 1e-6f;
        WrapVar(d).Value += vol;
        if (!_logRoller) return;
        if (first) Debug.Log($"[Каток] «{name}»: тесто ЛИПНЕТ к {DrumName(d)}у (нет муки) — его засасывает, пласт рвётся. Посыпь валец мукой (мастерком ПКМ по вальцу)", this);
        if (!was && DrumJammed(d)) Debug.Log($"[Каток] «{name}»: {DrumName(d)} ЗАЛИП тестом ({WrapVar(d).Value * 1000f:0} л) — каток встал. Слезь и поруби налипшее шпателем (ЛКМ по вальцу), потом посыпь мукой", this);
    }

    /// <summary>Голый валец коснулся теста — сразу ЗАЛИП (налипло vol м³, минимум — на «залип»). Рубить шпателем.</summary>
    [Server]
    public void ServerDrumJam(int d, float vol)
    {
        if (DrumJammed(d)) { WrapVar(d).Value += vol; return; }
        WrapVar(d).Value = Mathf.Max(JamVol, WrapVar(d).Value + vol);
        if (DrumContact(d, out Vector3 g, out _)) AnchorVar(d).Value = g;
        if (_logRoller) Debug.Log($"[Каток] «{name}»: {DrumName(d)} без муки — тесто схватило его, ЗАЛИП ({WrapVar(d).Value * 1000f:0} л). Слезь и поруби шпателем (ЛКМ по вальцу) ×3, потом посыпь мукой (мастерком ПКМ по вальцу)", this);
    }

    /// <summary>Налипшее понемногу шлёпается обратно буграми (раз в оборот вальца). Возвращает м³.</summary>
    [Server]
    public float ServerDrumShed(int d, float travel)
    {
        float w = WrapVar(d).Value;
        if (w <= 1e-6f || DrumJammed(d)) { _revAcc[d] = 0f; return 0f; }
        _revAcc[d] += travel;
        float rev = 2f * Mathf.PI * Mathf.Max(0.05f, _drumRadius);
        if (_revAcc[d] < rev) return 0f;
        _revAcc[d] = 0f;
        float v = w * 0.12f;
        WrapVar(d).Value = w - v;
        return v;
    }

    private int NearestDrum(Vector3 p, float maxR)
    {
        int best = -1; float bd = float.MaxValue;
        for (int d = 0; d < 2; d++)
        {
            Transform h = _hit[d] != null ? _hit[d] : d == 0 ? _frontDrum : _rearDrum;
            if (h == null) continue;
            Vector3 a = h.position, ax = h.right;
            Vector3 v = p - a;
            float along = Mathf.Clamp(Vector3.Dot(v, ax), -_drumWidth * 0.5f, _drumWidth * 0.5f);
            float dist = (v - ax * along).magnitude;
            if (dist < bd) { bd = dist; best = d; }
        }
        return bd <= maxR ? best : -1;
    }

    /// <summary>Мастерок (ПКМ) мукой по вальцу — налёт муки на этот валец (кучка не ложится).</summary>
    public int ServerReceiveScoop(ushort product, CatCooks.Food.FoodState state, int count, Vector3 point, Vector3 normal, out string why)
    {
        why = null;
        int d = NearestDrum(point, _drumRadius * 2.2f);
        if (d < 0) { why = "кидай муку прямо на валец (передний или задний)"; return 0; }
        if (!DoughStick.IsFlour(product)) { why = "на валец — только мука (чтобы тесто не липло)"; return 0; }
        if (DrumFlour01(d) >= 0.99f) { why = $"{DrumName(d)} уже в муке"; return 0; }
        float c = Mathf.Min(1f, DrumFlour01(d) + _flourPerPortion);
        FlourVar(d).Value = (byte)Mathf.RoundToInt(c * 200f);
        RpcDust(_hit[d] != null ? _hit[d].position : point, 1.2f);
        if (_logRoller) Debug.Log($"[Каток] «{name}»: {DrumName(d)} посыпан мукой — {c * 100f:0}%" + (WrapVar(d).Value > 1e-6f ? " (налипшее мука не снимает — поруби шпателем)" : ""), this);
        return 1;
    }

    /// <summary>Шпатель (ЛКМ) по вальцу — рубит налипшее тесто: за удар отваливается кусок. 3 удара — залипший валец свободен.</summary>
    public bool ServerSpatulaHit(Vector3 point, Vector3 dir, out string why)
    {
        why = null;
        int d = SpatulaDrum(point, out float dist);
        if (d < 0 || dist > _drumRadius * 2.5f) { why = "руби по вальцу или по тесту, что его держит"; return false; }
        float w = WrapVar(d).Value;
        if (w <= 1e-6f) { why = $"на {DrumName(d)}е ничего не налипло"; return false; }
        bool jam = DrumJammed(d);
        float v = Mathf.Min(w, Mathf.Max(w * 0.4f, JamVol * 0.34f));
        if (w - v < JamVol * 0.03f) v = w;
        WrapVar(d).Value = w - v;
        bool onDough = false;
        if (DrumContact(d, out Vector3 g, out _))
            foreach (CatCooks.Food.DoughSheet ds in CatCooks.Food.DoughSheet.All) if (ds != null && ds.ServerDropLump(g, v)) { onDough = true; break; }
        RpcDust(_hit[d] != null ? _hit[d].position : point, 0.8f);
        if (_logRoller)
            Debug.Log($"[Каток] «{name}»: шпателем срубили с {DrumName(d)}а {v * 1000f:0} л ({(onDough ? "шлёпнулось на тесто бугром" : "упало на пол — в мусор")}), осталось {WrapVar(d).Value * 1000f:0} л" +
                      (jam && !DrumJammed(d) ? " — валец СВОБОДЕН, посыпь мукой" : ""), this);
        return true;
    }

    /// <summary>Ближе всего к точке: залипший валец или тяж теста от него к месту, где схватило.</summary>
    private int SpatulaDrum(Vector3 p, out float dist)
    {
        int best = -1; dist = float.MaxValue;
        for (int d = 0; d < 2; d++)
        {
            if (WrapVar(d).Value <= 1e-6f) continue;
            Transform h = _hit[d] != null ? _hit[d] : d == 0 ? _frontDrum : _rearDrum;
            if (h == null) continue;
            float dd = Mathf.Max(0f, (p - h.position).magnitude - _drumRadius);
            if (DrumJammed(d) && DrumContact(d, out Vector3 g, out _)) dd = Mathf.Min(dd, DoughStick.SegDist(p, AnchorVar(d).Value, g));
            if (dd < dist) { dist = dd; best = d; }
        }
        return best;
    }

    public float SpatulaDistance(Vector3 point)
    {
        int d = SpatulaDrum(point, out float dist);
        return d < 0 ? float.MaxValue : dist;
    }

    [ObserversRpc]
    private void RpcDust(Vector3 at, float k)
    {
        CatCooks.Food.CCRuntimeParticles.Burst(at, Vector3.up, DoughStick.FlourColor, new Color(0.9f, 0.88f, 0.82f), Mathf.RoundToInt(18 * k), CatCooks.Food.CCRuntimeParticles.Look.Dust.Scaled(2f));
    }

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); if (!DoughStick.Spatulable.Contains(this)) DoughStick.Spatulable.Add(this); }
    private void OnDisable() { All.Remove(this); DoughStick.Spatulable.Remove(this); }
    private void OnDestroy() { for (int d = 0; d < 2; d++) _band[d]?.Destroy(); }

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (_logRoller)
            Debug.Log($"[Каток] «{name}»: вальцы {(_frontDrum != null ? "✔" : "✖ перед")}/{(_rearDrum != null ? "✔" : "✖ зад")}, " +
                      $"колёс подвески {(wheels != null ? wheels.Length : 0)}, сиденье {(_seatPoint != null ? "✔" : "✖")}, " +
                      $"камера {(_vehicleCamera != null ? "✔" : "✖")} · F — сесть, WASD — ехать", this);
    }

    protected override void VehicleFixedUpdate()
    {
        base.VehicleFixedUpdate();
        if (_body == null) return;
        Vector3 v = _body.linearVelocity;
        Vector3 h = new Vector3(v.x, 0f, v.z);
        float stuck = Mathf.Max(DrumStuck01(0), DrumStuck01(1));
        float limit = _maxSpeed * (1f - PlowLoad01(h) * _plowDrag) * (1f - 0.5f * stuck);
        if (h.magnitude > limit) _body.linearVelocity = h.normalized * limit + Vector3.up * v.y;
        Tether();
    }

    /// <summary>Залипший валец держит тесто «резинкой»: ехать можно, но чем дальше от места, где схватило, тем сильнее тянет назад.</summary>
    private void Tether()
    {
        for (int d = 0; d < 2; d++)
        {
            if (!DrumJammed(d) || !DrumContact(d, out Vector3 g, out _)) continue;
            Vector3 to = AnchorVar(d).Value - g; to.y = 0f;
            float dist = to.magnitude;
            if (dist <= _tetherSlack) continue;
            Vector3 dir = to / dist;
            Transform drum = d == 0 ? _frontDrum : _rearDrum;
            Vector3 at = drum != null ? drum.position : g;
            float away = -Vector3.Dot(_body.GetPointVelocity(at), dir);                       // скорость «от якоря»
            float acc = (dist - _tetherSlack) * _tetherPull * (dist > _tetherSlack * 6f ? 2.5f : 1f) + Mathf.Max(0f, away) * 2f;
            _body.AddForceAtPosition(dir * acc * _body.mass * 0.5f, at, ForceMode.Force);
        }
    }

    private float _loadLogAt;
    /// <summary>0..1 — сколько кучи теста перед вальцом, что едет первым (вперёд — передний, назад — задний).</summary>
    private float PlowLoad01(Vector3 hv)
    {
        if (CatCooks.Food.DoughSheet.All.Count == 0 || hv.sqrMagnitude < 0.0025f) return 0f;
        float sign = Vector3.Dot(hv, transform.forward) >= 0f ? 1f : -1f;
        if (!DrumContact(sign > 0f ? 0 : 1, out Vector3 g, out Vector3 axle)) return 0f;
        Vector3 dir = transform.forward * sign; dir.y = 0f;
        float load = 0f;
        foreach (CatCooks.Food.DoughSheet d in CatCooks.Food.DoughSheet.All) if (d != null) load = Mathf.Max(load, d.LoadAhead(g, dir, axle, _drumRadius, _drumWidth));
        float k = Mathf.Clamp01(load / Mathf.Max(0.05f, _drumRadius * 2f));
        if (_logRoller && k > 0.3f && Time.time >= _loadLogAt) { _loadLogAt = Time.time + 3f; Debug.Log($"[Каток] «{name}»: толкает кучу теста ({load * 100f:0} см выше вальца) — едет медленнее ×{1f - k * _plowDrag:0.00}", this); }
        return k;
    }

    /// <summary>Подвеска не видит тесто как землю: валец едет по земле/подстилке, а поверх — только по тонкому слою (Ride Max у теста).
    /// Толще — валец в него врезается и толкает кучу вперёд (это считает CatCooks.Food.DoughSheet), а не забирается на гору и не переворачивается.</summary>
    protected override bool WheelGroundCast(Vector3 pos, Vector3 dir, float maxDist, out RaycastHit hit)
    {
        hit = default(RaycastHit);
        RaycastHit[] hits = Physics.RaycastAll(pos, dir, maxDist, groundMask);
        float best = float.MaxValue; bool found = false;
        foreach (RaycastHit h in hits)
        {
            if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
            if (h.collider.GetComponentInParent<CatCooks.Food.DoughSheet>() != null) continue;
            if (h.distance < best) { best = h.distance; hit = h; found = true; }
        }
        if (!found) return false;
        float ride = 0f;
        foreach (CatCooks.Food.DoughSheet d in CatCooks.Food.DoughSheet.All) if (d != null) ride = Mathf.Max(ride, d.RideOn(hit.point));
        if (ride > 0f)
        {
            ride = Mathf.Min(ride, hit.distance);
            hit.point -= dir * ride;
            hit.distance -= ride;
        }
        return true;
    }

    protected override void DriverTick()
    {
        base.DriverTick();
        float a = 0f;
        if (wheels != null) foreach (Wheel w in wheels) if (w != null && w.steering) { a = w.steerAngle; break; }
        if (Mathf.Abs(a - _steerNet.Value) > 0.5f) CmdSteer(a);
    }

    [ServerRpc]
    private void CmdSteer(float a) => _steerNet.Value = Mathf.Clamp(a, -60f, 60f);

    /// <summary>Посадка: камера водителя — на высоту глаз этого игрока над сиденьем, смотрит вперёд (от первого лица).</summary>
    protected override void ApplyDriverState(NetworkObject player, bool entering)
    {
        if (entering && _firstPerson && player != null && player.IsOwner && _vehicleCamera != null && _seatPoint != null)
        {
            Camera pcam = player.GetComponentInChildren<Camera>(true);
            float eye = pcam != null ? Mathf.Clamp(pcam.transform.position.y - player.transform.position.y, 0.3f, 3f) : 1.2f;
            Transform c = _vehicleCamera.transform;
            c.position = _seatPoint.position + transform.up * eye;
            c.rotation = Quaternion.LookRotation(transform.forward, transform.up);
            Camera vc = _vehicleCamera.GetComponent<Camera>();
            if (vc != null && pcam != null) { vc.fieldOfView = pcam.fieldOfView; vc.nearClipPlane = Mathf.Min(pcam.nearClipPlane, 0.05f); }
            _camYaw = 0f; _camPitch = 0f;
            if (_logRoller) Debug.Log($"[Каток] «{name}»: сел — вид из кабины (глаза {eye:0.00} м над сиденьем). WASD — ехать, мышь — оглядеться, F — выйти", this);
        }
        base.ApplyDriverState(player, entering);
        if (entering && player != null && player.IsOwner) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        if (!entering && player != null && player.IsOwner) PlaceOnGround(player.transform);
    }

    /// <summary>Вышел — встать НА землю рядом (точка выхода может оказаться ниже пола или в тесте): луч сверху вниз.</summary>
    private void PlaceOnGround(Transform p)
    {
        if (!GroundUnder(p, out float best)) { if (_logRoller) Debug.LogWarning($"[Каток] «{name}»: вышел — под точкой выхода не нашёл землю", this); return; }
        Put(p, best);
        StartCoroutine(WatchExit(p, best));
        if (_logRoller)
        {
            CharacterController cc = p.GetComponent<CharacterController>();
            Debug.Log($"[Каток] «{name}»: вышел — встал на землю (земля {best:0.00} м, игрок {p.position.y:0.00} м" +
                      (cc != null ? $", капсула: центр {cc.center.y:0.00}, высота {cc.height:0.00}" : "") + ")", this);
        }
    }

    private bool GroundUnder(Transform p, out float best)
    {
        Vector3 from = new Vector3(p.position.x, (_exitPoint != null ? _exitPoint.position.y : p.position.y) + 3f, p.position.z);
        RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 15f, ~0, QueryTriggerInteraction.Ignore);
        best = float.NegativeInfinity; bool found = false;
        foreach (RaycastHit h in hits)
        {
            if (h.collider == null || h.collider.transform.IsChildOf(transform) || h.collider.transform.IsChildOf(p)) continue;
            if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue;   // ящики, еда — не земля
            if (h.point.y > best) { best = h.point.y; found = true; }
        }
        return found;
    }

    /// <summary>Поставить игрока НОГАМИ на землю: учитываем, где низ капсулы относительно пивота игрока (центр капсулы бывает не в ногах).</summary>
    private static void Put(Transform p, float ground)
    {
        CharacterController cc = p.GetComponent<CharacterController>();
        float foot = 0f, skin = 0.05f;
        if (cc != null)
        {
            float sy = Mathf.Abs(p.lossyScale.y);
            foot = (cc.center.y - cc.height * 0.5f) * sy;              // низ капсулы от пивота (обычно 0 или −1)
            skin = cc.skinWidth + 0.05f;
        }
        bool was = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false;
        p.position = new Vector3(p.position.x, ground - foot + skin, p.position.z);
        if (cc != null) cc.enabled = was;
    }

    /// <summary>Секунду после выхода следим: провалился ниже земли — вернуть наверх (бывает, что что-то ещё двигает игрока в этот кадр).</summary>
    private System.Collections.IEnumerator WatchExit(Transform p, float ground)
    {
        float until = Time.time + 1f;
        while (Time.time < until && p != null)
        {
            yield return null;
            CharacterController cc = p.GetComponent<CharacterController>();
            float foot = cc != null ? (cc.center.y - cc.height * 0.5f) * Mathf.Abs(p.lossyScale.y) : 0f;
            if (p.position.y + foot < ground - 0.25f)
            {
                Put(p, ground);
                if (_logRoller) Debug.LogWarning($"[Каток] «{name}»: игрок провалился после выхода — поставил обратно на землю", this);
            }
        }
    }

    /// <summary>Мышью — оглядеться из кабины (камера водителя).</summary>
    protected override void DriverUpdate()
    {
        base.DriverUpdate();
        if (!_firstPerson || _vehicleCamera == null || _seatPoint == null || UiModal.AnyOpen) return;
        _camYaw = Mathf.Clamp(_camYaw + Input.GetAxis("Mouse X") * _lookSensitivity, -_lookYaw, _lookYaw);
        _camPitch = Mathf.Clamp(_camPitch - Input.GetAxis("Mouse Y") * _lookSensitivity, -_lookPitch, _lookPitch);
        Transform c = _vehicleCamera.transform;
        c.rotation = transform.rotation * Quaternion.Euler(_camPitch, _camYaw, 0f);
    }

    /// <summary>Вид: вилка поворачивается, вальцы катятся по пути (у всех: считаем по сдвигу).</summary>
    protected override void ApplyVisuals()
    {
        if (!_captured)
        {
            Quaternion inv = Quaternion.Inverse(transform.rotation);
            if (_frontFork != null) _forkRel = inv * _frontFork.rotation;
            if (_frontDrum != null) { _frontRel = inv * _frontDrum.rotation; _lastF = _frontDrum.position; }
            if (_rearDrum != null) { _rearRel = inv * _rearDrum.rotation; _lastR = _rearDrum.position; }
            if (_steeringWheel != null) { _wheelBase = _steeringWheel.localRotation; _wheelBasePos = _steeringWheel.localPosition; }
            _captured = true;
        }
        _steerShown = Mathf.Lerp(_steerShown, SteerAngle, 1f - Mathf.Exp(-12f * Time.deltaTime));
        Quaternion yaw = Quaternion.AngleAxis(_steerShown, Vector3.up);
        float r = Mathf.Max(0.05f, _drumRadius);
        if (_frontFork != null) _frontFork.rotation = transform.rotation * yaw * _forkRel;
        DrumLook();
        if (_frontDrum != null)
        {
            Vector3 fwd = transform.rotation * yaw * Vector3.forward;
            _spinF += Vector3.Dot(_frontDrum.position - _lastF, fwd) / r * Mathf.Rad2Deg;
            _lastF = _frontDrum.position;
            _frontDrum.rotation = transform.rotation * yaw * Quaternion.AngleAxis(_spinF, Vector3.right) * _frontRel;
        }
        if (_rearDrum != null)
        {
            _spinR += Vector3.Dot(_rearDrum.position - _lastR, transform.forward) / r * Mathf.Rad2Deg;
            _lastR = _rearDrum.position;
            _rearDrum.rotation = transform.rotation * Quaternion.AngleAxis(_spinR, Vector3.right) * _rearRel;
        }
        if (_steeringWheel != null)
        {
            // вокруг оси руля через центр обода (pivot в осях руля): поворот + сдвиг, чтобы центр стоял на месте
            Quaternion turn = Quaternion.AngleAxis(-_steerShown / Mathf.Max(1f, maxSteerAngle) * 180f * _steeringTurns, _steeringAxis.normalized);
            Quaternion now = _wheelBase * turn;
            Vector3 s = _steeringWheel.localScale;
            Vector3 p = Vector3.Scale(_steeringPivot, s);
            _steeringWheel.localRotation = now;
            _steeringWheel.localPosition = _wheelBasePos + _wheelBase * p - now * p;
        }
    }

    /// <summary>У всех: налёт муки на вальцах (белее), намотанное тесто — «рукав» на вальце, пыль муки при езде по тесту.</summary>
    private void DrumLook()
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        for (int d = 0; d < 2; d++)
        {
            if (_drumRs[d] != null) DoughStick.Tint(_drumRs[d], _drumBase[d], DrumFlour01(d), _mpb);
            if (_sleeve[d] != null) _sleeve[d].gameObject.SetActive(false);
            // тяж теста от места, где схватило, к вальцу — как у кувалды (рукава на вальце нет)
            Transform drum = _hit[d] != null ? _hit[d] : d == 0 ? _frontDrum : _rearDrum;
            if (DrumJammed(d) && drum != null)
            {
                if (_band[d] == null) _band[d] = new DoughStrand("DoughStrand_" + name + "_" + d);
                Vector3 a = AnchorVar(d).Value, c = drum.position;
                Vector3 toA = a - c; toA = Vector3.ProjectOnPlane(toA, transform.right);       // в плоскости вальца
                Vector3 b = c + (toA.sqrMagnitude > 1e-6f ? toA.normalized : Vector3.down) * (_drumRadius * 0.97f);
                _band[d].Set(a, b, 0.08f, 0.25f, 1.3f, transform.right);
            }
            else _band[d]?.Hide();
        }
        // пыль муки из-под вальца, когда он в муке и едет по тесту
        if (Time.time < _puffAt || _body == null || _body.linearVelocity.sqrMagnitude < 0.04f) return;
        _puffAt = Time.time + 0.35f;
        for (int d = 0; d < 2; d++)
        {
            if (DrumFlour01(d) <= 0.01f || !DrumContact(d, out Vector3 g, out _)) continue;
            foreach (CatCooks.Food.DoughSheet ds in CatCooks.Food.DoughSheet.All)
                if (ds != null && ds.HeightAt(g, out _)) { CatCooks.Food.CCRuntimeParticles.Burst(g + Vector3.up * 0.05f, Vector3.up, DoughStick.FlourColor, DoughStick.FlourColor, 4, CatCooks.Food.CCRuntimeParticles.Look.Dust.Scaled(1.2f)); break; }
        }
    }

    /// <summary>Подсказка: водителю всегда, остальным — когда смотрят на каток вблизи.</summary>
    private void OnGUI()
    {
        if (!_isLocalDriver)
        {
            if (Time.time >= _lookAt)
            {
                _lookAt = Time.time + 0.2f;
                Camera cam = Camera.main;
                _lookingAtMe = cam != null && Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out RaycastHit h, 6f, ~0, QueryTriggerInteraction.Ignore)
                               && h.collider.transform.IsChildOf(transform);
            }
            if (!_lookingAtMe) return;
        }
        string t = $"Каток · {DrumHud(0)} · {DrumHud(1)}";
        var st = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15, alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(0, Screen.height - 150, Screen.width, 24), t, st);
    }

    private string DrumHud(int d)
    {
        string n = d == 0 ? "перед" : "зад";
        if (DrumJammed(d)) return $"<color=#FF5A3A>{n}: ЗАЛИП — слезь, поруби шпателем</color>";
        if (DrumStuck01(d) > 0.001f) return $"<color=#FF8A5A>{n}: липнет тесто {DrumStuck01(d) * 100f:0}%</color>";
        if (DrumFlour01(d) <= 0.001f) return $"<color=#FFB060>{n}: без муки (кинь муку мастерком)</color>";
        return $"{n}: мука {DrumFlour01(d) * 100f:0}%";
    }

#if UNITY_EDITOR
    public void EditorSetup(Transform fork, Transform front, Transform rear, Transform steering, float radius, float width, Vector3 steerAxis, Vector3 steerPivot)
    {
        UnityEditor.Undo.RecordObject(this, "Каток");
        _frontFork = fork; _frontDrum = front; _rearDrum = rear; _steeringWheel = steering; _drumRadius = radius; _drumWidth = width;
        _steeringAxis = steerAxis; _steeringPivot = steerPivot;
        UnityEditor.EditorUtility.SetDirty(this);
        if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this)) UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
    }
#endif
}
