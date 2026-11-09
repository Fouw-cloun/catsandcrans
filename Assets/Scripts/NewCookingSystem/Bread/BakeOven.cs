using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ДУХОВКА-БОЧКА НА КОСТРЕ. НЕ тара: в ней ничего не лежит «порциями» — она ГРЕЕТ то, что внутри.
    ///
    ///   • Внутри решётка (твоя модель — просто коллайдеры). На неё ставят:
    ///       – тару (кастрюлю, сковороду, противень) — тара получает «огонь» = температуре духовки
    ///         и варит/жарит/кипит как на плите (Cookware Heat);
    ///       – формы с тестом (Bread Mold) — пекутся в хлеб;
    ///       – куски/шары теста прямо на решётку (Dough Blob) — пекутся в булку;
    ///       – любые продукты (Food Piece) — процесс «Запекать», если для них есть правило.
    ///   • Сама духовка греется от КОСТРА под ней (или от горелки, бьющей в неё).
    ///     Закрыта — держит жар; открыта — холоднее, печётся медленнее.
    ///   • Откидная крышка (Oven Hinged Lid), люк (Oven Vent Hatch).
    ///     Закрыто и люк закрыт, а внутри что-то печётся — копится ДАВЛЕНИЕ → дребезг → ВЗРЫВ
    ///     (крышку откидывает, всё изнутри разлетается). Открыл люк — пар выходит.
    ///
    /// ═══ НА КОРНЕ ДУХОВКИ ═══
    ///   NetworkObject · NetworkTransform · Rigidbody · этот компонент · Oven Hinged Lid · Oven Vent Hatch.
    ///   Cookware / Cookware Heat / Cookware Pressure / Carried Container — НЕ нужны (убери).
    ///   Chamber — Box Collider (Is Trigger) по пустоте внутри: что в нём — то «в духовке».
    ///
    /// ═══ НА ОГОНЬ ═══
    ///   Тест-клавиша O ставит духовку на ближайший костёр: на пустышку «OvenSeat» у костра
    ///   (центр дна духовки, синяя ось — куда смотрит перед), нет её — на Pot Dock костра.
    ///   Поза «как стоит ровно» — запоминается при старте (или ⋮ → «Запомнить позу»).
    ///
    /// ═══ ДЫМ (копчение) ═══
    ///   Пылесосом набрал дым у костра (режим «сосать») → открыл люк → дуло к люку, режим «выдувать» —
    ///   дым заходит в духовку. Закрыл люк — дым держится дольше (открытая крышка/люк — быстро уходит).
    ///   Рёбра и брискет внутри коптятся (дымок → +к оценке). Smoke01 — сколько дыма внутри.
    /// </summary>
    public sealed class BakeOven : NetworkBehaviour
    {
        public static readonly List<BakeOven> All = new List<BakeOven>();

        [Header("Полость")]
        [Tooltip("Box Collider (Is Trigger) по пустоте внутри. Что в нём — то в духовке")]
        [SerializeField] private Collider _chamber;

        [Header("Жар")]
        [SerializeField] private float _ambientC = 20f;
        [Tooltip("Костёр считается «под духовкой», если ближе этого по горизонтали, м")]
        [SerializeField] private float _fireReach = 1.8f;
        [Tooltip("Как быстро нагревается к огню (1/с)")]
        [SerializeField] private float _heatRate = 0.06f;
        [Tooltip("Как быстро остывает (1/с)")]
        [SerializeField] private float _coolRate = 0.025f;
        [Tooltip("Открытая духовка держит такую долю жара")]
        [Range(0.3f, 1f)][SerializeField] private float _openHeat = 0.7f;
        [Tooltip("Горелка, бьющая в духовку, тоже греет (°C при мощности 1)")]
        [SerializeField] private float _torchC = 200f;

        [Header("Выпечка")]
        [Tooltip("Открытая — печётся во столько раз медленнее")]
        [Range(0.1f, 1f)][SerializeField] private float _openSpeed = 0.5f;
        [Tooltip("С какой температуры продукты на решётке «запекаются» (процесс Запекать)")]
        [SerializeField] private float _bakeFoodFromC = 120f;

        [Header("Давление (закрыто + люк закрыт + что-то печётся)")]
        [Tooltip("Рост давления в секунду на каждую вещь, что печётся (4 булки — взрыв примерно через минуту)")]
        [SerializeField] private float _pressurePerItem = 0.004f;
        [Tooltip("Сброс давления в секунду, когда люк или крышка открыты")]
        [SerializeField] private float _ventRate = 0.25f;
        [Range(0.3f, 0.95f)][SerializeField] private float _overheatAt = 0.7f;
        [SerializeField] private float _blastForce = 5f;
        [SerializeField] private float _selfKick = 3f;

        [Header("Посадка на огонь")]
        [Tooltip("Поднять/опустить духовку над точкой посадки, м")]
        [SerializeField] private float _seatLift = 0f;

        [Header("Пар")]
        [SerializeField] private Color _steamColor = new Color(1f, 1f, 1f, 0.55f);

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly SyncVar<ushort> _tempNet = new SyncVar<ushort>(20);
        private readonly SyncVar<byte> _baking = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _pressNet = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _smokeNet = new SyncVar<byte>(0);

        [Header("Дым (копчение пылесосом)")]
        [Tooltip("За сколько секунд уходит полный дым при закрытом люке и крышке")]
        [SerializeField] private float _smokeHoldSeconds = 45f;
        [Tooltip("За сколько секунд уходит дым, если люк или крышка открыты")]
        [SerializeField] private float _smokeLeakSeconds = 14f;
        [SerializeField] private Color _smokeColor = new Color(0.55f, 0.55f, 0.58f, 0.6f);
        private float _smoke, _smokeAcc, _smokeLogAt;

        [SerializeField, HideInInspector] private Quaternion _uprightRot = Quaternion.identity;
        [SerializeField, HideInInspector] private bool _hasUpright;

        // посадка на огонь: сервер держит духовку в точке, пока её не заберут (кран — ServerUnseat)
        private bool _seated, _moveWarned;
        private Vector3 _seatPos, _seatPoint;
        private Quaternion _seatRot;
        private float _seatRadius;

        private OvenHingedLid _hinged;
        private OvenVentHatch _ventHatch;
        private Rigidbody _rb;
        private float _t = 20f, _press, _steamAcc, _scanAt, _scanLast, _logAt, _rattleAt;
        private readonly HashSet<Cookware> _heldCw = new HashSet<Cookware>();
        private readonly List<Cookware> _cwTmp = new List<Cookware>();
        private readonly HashSet<FoodPiece> _foodTmp = new HashSet<FoodPiece>();
        private readonly HashSet<BreadMold> _moldTmp = new HashSet<BreadMold>();
        private readonly HashSet<BakeTray> _trayTmp = new HashSet<BakeTray>();
        private readonly HashSet<IOvenBaking> _ovenBakeTmp = new HashSet<IOvenBaking>();

        public float TemperatureC => _tempNet.Value;
        public int BakingCount => _baking.Value;
        public float Pressure01 => _pressNet.Value / 255f;
        /// <summary>Духовка открыта (крышка откинута). Нет откидной крышки — считается закрытой.</summary>
        public bool DoorOpen => _hinged != null && _hinged.IsOpen;
        public bool VentOpen => _ventHatch != null && _ventHatch.IsOpen;
        public OvenHingedLid HingedLid => _hinged;
        public OvenVentHatch Vent => _ventHatch;
        /// <summary>Множитель скорости выпечки (открыта — медленнее).</summary>
        public float BakeSpeed => DoorOpen ? _openSpeed : 1f;
        /// <summary>Сколько дыма внутри (0..1) — для копчения.</summary>
        public float Smoke01 => _smokeNet.Value / 255f;

        /// <summary>Пылесос задувает дым через люк. Сервер.</summary>
        [Server]
        public void ServerAddSmoke(float amount01)
        {
            if (amount01 <= 0f) return;
            bool wasLow = _smoke < 0.05f;
            _smoke = Mathf.Min(1f, _smoke + amount01);
            PushSmoke();
            if (_log && (wasLow || Time.time >= _smokeLogAt))
            {
                _smokeLogAt = Time.time + 3f;
                Debug.Log($"[Духовка] «{name}»: задули дым — {_smoke * 100f:0}%" + (_smoke >= 0.99f ? " (полная, закрой люк — дым продержится дольше)" : ""), this);
            }
        }

        private void PushSmoke()
        {
            byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(_smoke) * 255f);
            if (Mathf.Abs(b - _smokeNet.Value) >= 2 || (b == 0) != (_smokeNet.Value == 0)) _smokeNet.Value = b;
        }

        private void Awake()
        {
            _hinged = GetComponentInChildren<OvenHingedLid>(true);
            _ventHatch = GetComponentInChildren<OvenVentHatch>(true);
            _rb = GetComponent<Rigidbody>();
            if (!_hasUpright) { _uprightRot = transform.rotation; _hasUpright = true; }
        }

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _t = _ambientC;
            if (_log)
                Debug.Log($"[Духовка] «{name}»: " + (_chamber != null ? "полость есть" : "НЕТ полости (Chamber) — внутри ничего не печётся") +
                          (_hinged != null ? ", откидная крышка есть" : ", НЕТ откидной крышки (Oven Hinged Lid) — всегда «закрыта»") +
                          (_ventHatch != null ? ", люк есть" : ", люка нет") +
                          (GetComponent<Cookware>() != null ? ". ⚠ На духовке есть Cookware — она больше не тара: убери Cookware, Cookware Heat, Cookware Pressure, Carried Container" : ""), this);
        }

        // ───────────────────────── ЧТО ВНУТРИ ─────────────────────────

        /// <summary>Внутри ли духовки точка (в полости).</summary>
        public bool IsInside(Vector3 p)
        {
            if (_chamber == null || !_chamber.enabled) return false;
            if (!_chamber.bounds.Contains(p)) return false;
            return (_chamber.ClosestPoint(p) - p).sqrMagnitude < 1e-6f;
        }

        public static BakeOven Find(Vector3 p)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null && All[i].IsInside(p)) return All[i];
            return null;
        }

        /// <summary>Стоит ли какая-то духовка на огне в этой точке (магнит костра тогда не тянет кастрюли).</summary>
        public static bool SeatedAt(Vector3 p)
        {
            for (int i = 0; i < All.Count; i++)
            {
                BakeOven o = All[i];
                if (o == null || !o._seated) continue;
                if (new Vector2(p.x - o._seatPoint.x, p.z - o._seatPoint.z).magnitude <= o._seatRadius) return true;
            }
            return false;
        }

        /// <summary>
        /// Решётка духовки: ребёнок «Grill» с коллайдером (делает окно «Духовка-бочка»),
        /// иначе — самый высокий плоский коллайдер внутри полости.
        /// </summary>
        public bool TryGetGrill(out Collider grill)
        {
            grill = null;
            Transform g = FindDeep(transform, "Grill");
            if (g != null)
            {
                Collider c = g.GetComponent<Collider>();
                if (c != null && !c.isTrigger && c.enabled) { grill = c; return true; }
            }
            if (_chamber == null) return false;
            Bounds ch = _chamber.bounds;
            float best = float.MinValue;
            foreach (Collider c in GetComponentsInChildren<Collider>())
            {
                if (c == null || c.isTrigger || !c.enabled || c == _chamber) continue;
                Bounds b = c.bounds;
                if (b.size.y > 0.1f || !ch.Contains(b.center)) continue;
                if (b.max.y > best) { best = b.max.y; grill = c; }
            }
            return grill != null;
        }

        private static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n) return t;
            for (int i = 0; i < t.childCount; i++) { Transform r = FindDeep(t.GetChild(i), n); if (r != null) return r; }
            return null;
        }

        /// <summary>Духовка стоит на огне (зафиксирована).</summary>
        public bool IsSeated => _seated;

        /// <summary>Тару держит духовка (стоит внутри) — костёр и его магнит её не трогают.</summary>
        /// <summary>Габарит полости духовки (мир). Пусто — нулевой.</summary>
        public Bounds ChamberBounds => _chamber != null ? _chamber.bounds : new Bounds(transform.position, Vector3.zero);

        /// <summary>Полость для «большого» мяса: коллайдер Chamber (даже если выключен — по его форме),
        /// иначе — габарит модели духовки. Никогда не пустой.</summary>
        public Bounds CavityBounds
        {
            get
            {
                if (_chamber != null)
                {
                    if (_chamber.enabled && _chamber.gameObject.activeInHierarchy) return _chamber.bounds;
                    BoxCollider bx = _chamber as BoxCollider;
                    if (bx != null)
                    {
                        Transform t = bx.transform;
                        var b = new Bounds(t.TransformPoint(bx.center), Vector3.zero);
                        Vector3 e = bx.size * 0.5f;
                        for (int i = 0; i < 8; i++)
                            b.Encapsulate(t.TransformPoint(bx.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
                        return b;
                    }
                }
                Bounds r = new Bounds(transform.position, Vector3.zero);
                bool any = false;
                foreach (Renderer rr in GetComponentsInChildren<Renderer>())
                {
                    if (rr == null || rr is ParticleSystemRenderer) continue;
                    if (!any) { r = rr.bounds; any = true; } else r.Encapsulate(rr.bounds);
                }
                if (any) r.Expand(-Mathf.Min(r.size.x, r.size.z) * 0.1f);
                return r;
            }
        }

        /// <summary>Духовка, в полости которой стоит эта тара (по её коллайдерам), или null.</summary>
        public static BakeOven Holding(Cookware c)
        {
            if (c == null) return null;
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null && All[i]._heldCw.Contains(c)) return All[i];
            return null;
        }

        public static bool Holds(Cookware c)
        {
            if (c == null) return false;
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null && All[i]._heldCw.Contains(c)) return true;
            return false;
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        private void FixedUpdate()
        {
            if (!_seated || !base.IsServerInitialized) return;
            // стоит на огне: если что-то сдвинуло/выключило кинематику — вернуть и сказать, что именно
            bool moved = (transform.position - _seatPos).sqrMagnitude > 0.0004f || Quaternion.Angle(transform.rotation, _seatRot) > 1f;
            bool unk = _rb != null && !_rb.isKinematic;
            if (!moved && !unk) return;
            if (_log && !_moveWarned)
            {
                _moveWarned = true;
                Debug.LogWarning($"[Духовка] «{name}»: её сдвинули с огня на {(transform.position - _seatPos).magnitude:0.00} м / {Quaternion.Angle(transform.rotation, _seatRot):0}°" +
                                 (unk ? ", кто-то выключил кинематику Rigidbody" : "") + $" — возвращаю. На корне: {Suspects()}", this);
            }
            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero;
                _rb.position = _seatPos; _rb.rotation = _seatRot;
            }
            transform.SetPositionAndRotation(_seatPos, _seatRot);
        }

        /// <summary>Снять с огня (кран, игрок): духовка снова физическая.</summary>
        [Server]
        public void ServerUnseat()
        {
            if (!_seated) return;
            _seated = false;
            if (_rb != null) { _rb.isKinematic = false; _rb.useGravity = true; _rb.WakeUp(); }
            if (_log) Debug.Log($"[Духовка] «{name}» снята с огня", this);
        }

        /// <summary>Компоненты, которые могут двигать духовку сами (остались от «тары»).</summary>
        private string Suspects()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Component c in GetComponents<Component>())
            {
                if (c == null) continue;
                string n = c.GetType().Name;
                if (n == "Cookware" || n == "CookwareHeat" || n == "CookwarePressure" || n == "PotLidSocket" || n.Contains("Pickup") || n.Contains("Carried") || n.Contains("Interactable") || n.Contains("CraneLoad"))
                    sb.Append(n).Append(' ');
            }
            return sb.Length > 0 ? sb.ToString() + "(лишнее — убери)" : "ничего лишнего (двигает кто-то снаружи — пришли лог)";
        }

        private void Update()
        {
            if (base.IsServerInitialized) ServerTick(Mathf.Min(Time.deltaTime, 0.1f));
            SteamTick(Time.deltaTime);
            SmokeTick(Time.deltaTime);
        }

        /// <summary>Дым струйкой из люка/щелей, пока внутри есть.</summary>
        private void SmokeTick(float dt)
        {
            float s = Smoke01;
            if (s < 0.03f) { _smokeAcc = 0f; return; }
            bool open = DoorOpen, vent = VentOpen;
            _smokeAcc += dt * (open || vent ? 5f : 1.2f) * s;
            if (_smokeAcc < 1f) return;
            _smokeAcc -= 1f;
            Vector3 at = vent && _ventHatch != null ? _ventHatch.VentPoint
                       : _chamber != null ? _chamber.bounds.center + Vector3.up * _chamber.bounds.extents.y : transform.position;
            var look = CCRuntimeParticles.Look.Dust.Scaled(5f);
            look.Lifetime = new Vector2(1.2f, 2.2f); look.Gravity = -0.12f; look.ConeAngle = 25f;
            CCRuntimeParticles.Burst(at, Vector3.up, _smokeColor, _smokeColor * 0.8f, 2, look);
        }

        private void ServerTick(float dt)
        {
            // ── жар ──
            float fire = FireUnder();
            float target = fire > _ambientC + 1f ? Mathf.Lerp(_ambientC, fire, DoorOpen ? _openHeat : 1f) : _ambientC;
            float k = target > _t ? _heatRate : _coolRate;
            _t = Mathf.Lerp(_t, target, 1f - Mathf.Exp(-k * dt));
            ushort tv = (ushort)Mathf.Clamp(Mathf.RoundToInt(_t), 0, 2000);
            if (tv != _tempNet.Value) _tempNet.Value = tv;

            // ── содержимое (4 раза в секунду) ──
            if (Time.time >= _scanAt)
            {
                float sdt = _scanLast > 0f ? Mathf.Clamp(Time.time - _scanLast, 0f, 0.5f) : 0.25f;
                _scanLast = Time.time;
                _scanAt = Time.time + 0.25f;
                int n = ScanContents(sdt);
                if (_baking.Value != n) _baking.Value = (byte)Mathf.Min(n, 255);
            }

            // ── давление ──
            bool closed = !DoorOpen && !VentOpen;
            int baking = _baking.Value;
            if (closed && baking > 0 && _t > 100f) _press += _pressurePerItem * baking * dt;
            else _press = Mathf.Max(0f, _press - _ventRate * dt);
            byte pb = (byte)Mathf.RoundToInt(Mathf.Clamp01(_press) * 255f);
            if (Mathf.Abs(pb - _pressNet.Value) >= 3 || (pb == 0) != (_pressNet.Value == 0)) _pressNet.Value = pb;
            if (_press >= _overheatAt && Time.time >= _rattleAt)
            {
                _rattleAt = Time.time + 3f;
                if (_log) Debug.Log($"[Духовка] «{name}»: ДАВЛЕНИЕ {_press * 100f:0}% — крышка дребезжит, открой люк!", this);
            }
            if (_press >= 1f) ServerExplode();

            // ── дым: уходит, быстрее — если открыто ──
            if (_smoke > 0f)
            {
                float leak = (DoorOpen || VentOpen) ? _smokeLeakSeconds : _smokeHoldSeconds;
                _smoke = Mathf.Max(0f, _smoke - dt / Mathf.Max(1f, leak));
                PushSmoke();
            }

            if (_log && baking > 0 && Time.time >= _logAt)
            {
                _logAt = Time.time + 6f;
                Debug.Log($"[Духовка] «{name}»: {_t:0} °C, внутри печётся {baking}, " +
                          (DoorOpen ? "открыта — медленнее" : VentOpen ? "люк открыт — пар выходит" : $"закрыта — давление {_press * 100f:0}%"), this);
            }
        }

        /// <summary>Жар снизу: горящий костёр под духовкой (и горелки, бьющие в неё).</summary>
        private float FireUnder()
        {
            float fire = 0f;
            Bounds ob = OvenBounds();
            Vector3 bottom = new Vector3(ob.center.x, ob.min.y, ob.center.z);
            foreach (Tools.BonfirePit pit in Tools.BonfirePit.All)
            {
                if (pit == null || !pit.IsBurning) continue;
                Vector3 seat = pit.PotDock != null ? pit.PotDock.transform.position : pit.transform.position;
                Vector3 d = bottom - seat;
                if (d.y < -1.5f || d.y > 2.5f) continue;
                if (new Vector2(d.x, d.z).magnitude > _fireReach) continue;
                fire = Mathf.Max(fire, pit.PotTemperatureC);
            }
            if (_torchC > 0f)
            {
                IReadOnlyList<IHeatJet> jets = HeatJets.All;
                for (int i = 0; i < jets.Count; i++)
                {
                    IHeatJet j = jets[i];
                    if (j == null || !j.JetActive) continue;
                    Bounds b = ob;
                    b.Expand(j.JetRadius * 2f);
                    Vector3 tip = j.JetOrigin + j.JetDirection * j.JetLength;
                    bool hits = b.Contains(tip) || (b.IntersectRay(new Ray(j.JetOrigin, j.JetDirection), out float dist) && dist <= j.JetLength);
                    if (hits) fire = Mathf.Max(fire, _ambientC + _torchC * Mathf.Clamp(j.JetPower, 0.3f, 3f));
                }
            }
            return fire;
        }

        /// <summary>Что внутри: тару греем, продукты запекаем. Возвращает, сколько всего «печётся».</summary>
        private int ScanContents(float dt)
        {
            _heldCw.Clear();
            if (_chamber == null) return 0;
            int baking = 0;
            Bounds cb = _chamber.bounds;
            Collider[] over = Physics.OverlapBox(cb.center, cb.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            int cnt = over.Length;
            _cwTmp.Clear(); _foodTmp.Clear(); _moldTmp.Clear(); _trayTmp.Clear(); _ovenBakeTmp.Clear();
            for (int i = 0; i < cnt; i++)
            {
                Collider c = over[i];
                if (c == null || c.transform.IsChildOf(transform)) continue;
                Cookware cw = c.GetComponentInParent<Cookware>();
                if (cw != null && !_cwTmp.Contains(cw) && IsInside(c.bounds.center)) _cwTmp.Add(cw);
                BreadMold m = c.GetComponentInParent<BreadMold>();
                if (m != null && _moldTmp.Add(m) && m.IsBakingNow) baking++;
                BakeTray tray = c.GetComponentInParent<BakeTray>();
                if (tray != null && _trayTmp.Add(tray) && tray.IsBakingNow) baking++;
                IOvenBaking ob = c.GetComponentInParent<IOvenBaking>();   // противень макарон и другое, что печётся само
                if (ob != null && tray == null && _ovenBakeTmp.Add(ob) && ob.IsBakingNow) baking++;
                FoodPiece fp = c.GetComponentInParent<FoodPiece>();
                if (fp != null && cw == null && m == null) _foodTmp.Add(fp);
            }
            // тара на решётке: «огонь» под ней = жар духовки
            foreach (Cookware cw in _cwTmp)
            {
                _heldCw.Add(cw);
                if (cw.GetComponent<CookwareHeat>() == null) continue;
                cw.ServerSetExternalTemperature(_t);
                if (_t > 100f && !cw.IsEmpty) baking++;
            }
            // продукты прямо на решётке
            foreach (FoodPiece fp in _foodTmp)
            {
                if (fp == null || fp.IsConsumed || fp.IsHeld || !IsInside(fp.transform.position)) continue;
                DoughBlob db = fp.GetComponent<DoughBlob>();
                if (db != null) { if (db.IsBakingNow) baking++; continue; }   // тесто печётся само — в булку
                if (_t >= _bakeFoodFromC)
                {
                    fp.ServerAddWork(FoodProcess.Bake, dt * BakeSpeed);
                    baking++;
                }
            }
            return baking;
        }

        [Server]
        private void ServerExplode()
        {
            _press = 0f;
            _pressNet.Value = 0;
            if (_hinged != null) _hinged.ServerBlowOpen();
            if (_rb != null && !_rb.isKinematic)
                _rb.AddForce((Vector3.up + Random.insideUnitSphere * 0.3f).normalized * _selfKick, ForceMode.Impulse);
            if (_chamber != null)
            {
                Bounds cb = _chamber.bounds;
                var seen = new HashSet<Rigidbody>();
                foreach (Collider c in Physics.OverlapBox(cb.center, cb.extents * 1.2f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                {
                    Rigidbody rb = c.attachedRigidbody;
                    if (rb == null || rb == _rb || !seen.Add(rb)) continue;
                    if (rb.isKinematic) { rb.isKinematic = false; rb.useGravity = true; }
                    rb.AddExplosionForce(_blastForce, cb.center - Vector3.up * cb.extents.y, cb.extents.magnitude * 3f, 1.2f, ForceMode.Impulse);
                }
            }
            RpcBoom(_chamber != null ? _chamber.bounds.center : transform.position);
            if (_log) Debug.Log($"[Духовка] «{name}»: ВЗРЫВ ДАВЛЕНИЯ — крышку откинуло, всё изнутри разлетелось", this);
        }

        [ObserversRpc]
        private void RpcBoom(Vector3 at)
        {
            CCRuntimeParticles.Burst(at, Vector3.up, Color.white, new Color(0.8f, 0.8f, 0.8f, 0.6f), 30, CCRuntimeParticles.Look.Dust.Scaled(3f));
        }

        // ───────────────────────── НА ОГОНЬ ─────────────────────────

        /// <summary>
        /// Поставить духовку на точку (сервер): поза «как стоит ровно», поворот вокруг вертикали — по синей оси точки,
        /// центр дна — в точке (+ Seat Lift). Духовка фиксируется (кинематика).
        /// </summary>
        [Server]
        public void ServerPlaceAt(Transform seat)
        {
            if (seat == null) return;
            NetworkObject nob = NetworkObject;
            if (nob != null && nob.Owner.IsValid) nob.RemoveOwnership();
            Quaternion up = _uprightRot;
            Quaternion rot = Quaternion.Euler(0f, seat.eulerAngles.y, 0f) * Quaternion.Inverse(Quaternion.Euler(0f, up.eulerAngles.y, 0f)) * up;

            // габарит в целевом повороте → где центр дна относительно опоры
            Quaternion wasR = transform.rotation;
            Vector3 wasP = transform.position;
            transform.rotation = rot;
            Bounds b = OvenBounds();
            Vector3 bottomOffset = new Vector3(b.center.x, b.min.y, b.center.z) - transform.position;
            transform.SetPositionAndRotation(wasP, wasR);

            Vector3 pos = seat.position + Vector3.up * _seatLift - bottomOffset;
            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
                _rb.position = pos; _rb.rotation = rot;
            }
            transform.SetPositionAndRotation(pos, rot);
            _seated = true; _moveWarned = false;
            _seatPos = pos; _seatRot = rot; _seatPoint = seat.position;
            _seatRadius = Mathf.Max(0.3f, Mathf.Max(b.extents.x, b.extents.z));
            if (_log)
                Debug.Log($"[Духовка] «{name}» поставлена на «{seat.name}» ({seat.position:F2}): габарит {b.size:F2} м, центр дна → точка, " +
                          $"поза ровно {_uprightRot.eulerAngles:F0}, поворот по точке {seat.eulerAngles.y:0}°. Держу на месте (кинематика). " +
                          $"Лишние компоненты: {Suspects()}", this);
        }

        [ContextMenu("Запомнить позу: так духовка стоит правильно")]
        private void CaptureUpright()
        {
            _uprightRot = transform.rotation;
            _hasUpright = true;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            Debug.Log($"[Духовка] «{name}»: поза запомнена", this);
        }

        /// <summary>Габарит мешей духовки (мир).</summary>
        private Bounds OvenBounds()
        {
            bool any = false;
            Bounds b = new Bounds(transform.position, Vector3.zero);
            foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled || (r.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        // ───────────────────────── ВИД ─────────────────────────

        /// <summary>Пар наружу: из открытой духовки или из люка, пока внутри что-то печётся.</summary>
        private void SteamTick(float dt)
        {
            int n = _baking.Value;
            bool open = DoorOpen, vent = !open && VentOpen;
            if (n <= 0 || (!open && !vent)) { _steamAcc = 0f; return; }
            _steamAcc += dt * (2f + n);
            if (_steamAcc < 1f) return;
            _steamAcc -= 1f;
            Vector3 at = vent ? _ventHatch.VentPoint
                       : _chamber != null ? _chamber.bounds.center + Vector3.up * _chamber.bounds.extents.y : transform.position;
            CCRuntimeParticles.Burst(at, Vector3.up, _steamColor, _steamColor * 0.85f, 3, CCRuntimeParticles.Look.Dust.Scaled(vent ? 1.4f : 2.2f));
        }

        private void OnDrawGizmosSelected()
        {
            if (_chamber == null) return;
            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.6f);
            Gizmos.DrawWireCube(_chamber.bounds.center, _chamber.bounds.size);
        }
    }
}
