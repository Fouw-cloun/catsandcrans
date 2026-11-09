using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ТЕЛЯТИНА ПО-ОРЛОВСКИ — сборка и подача (шаги 4–7). Висит на префабе корейки рядом с VealLoinCook (меню Орлов → 8).
    /// Работает, когда корейка протушилась («Телятина тушёная»; не дал отдохнуть — тоже можно, но сок вытечет).
    ///   4. НАРЕЗКА: ручной пилой (или любой пилой по еде) — 5 разрезов → 6 медальонов, стоят вместе «буханкой», видны щели.
    ///   5. ПРОСЛОЙКА: мастерком в щели — сначала «Дюксель», потом «Субиз» (1 порция на щель, мастерок = 2 щели;
    ///      ложится в ближайшую к броску щель).
    ///   6. СВЕРХУ: «Соус морне» мастерком (от 1 до 3 мастерков) + «Сыр твёрдый» тёртый 1 мастерок → в духовку (от 140 °C, ПКМ по решётке),
    ///      ~25 с — корочка; передержал ещё 60% — подгорело. Над буханкой табличка с полоской и «ещё ~N с».
    ///   7. ПОДАЧА: целиком, как в XIX веке, — положить на тарелку / блюдо / доску → «Подано ✔» и звёзды.
    /// Звёзды: испечено (1) · отдохнула, сок не вытек · все 5 щелей с дюкселем и субизом · морне без комков · корочка не сгорела.
    /// По сети — SyncVar'ы (разрезы, щели, морне, сыр, выпечка); вид строится у каждого сам.
    /// </summary>
    [RequireComponent(typeof(FoodPiece))]
    public sealed class OrloffLoaf : NetworkBehaviour, ICuttable, IScoopReceiver, IFoodSecondaryHandler
    {
        public const int Cuts = 5;

        [Header("Сборка")]
        [Tooltip("Морне сверху: сколько порций максимум (мастерок = 2)")]
        [Range(2, 12)][SerializeField] private int _mornayMax = 6;
        [Range(1, 12)][SerializeField] private int _mornayMin = 2;
        [Range(1, 6)][SerializeField] private int _cheeseMax = 2;

        [Header("Духовка")]
        [Tooltip("С какой температуры духовки печётся корочка, °C (как хлеб и пампушки)")]
        [SerializeField] private float _ovenFromC = 140f;
        [Min(1f)][SerializeField] private float _bakeSeconds = 25f;
        [Tooltip("После готовности: сколько ещё (доля) до «подгорело»")]
        [Range(0.2f, 2f)][SerializeField] private float _burnAfter = 0.6f;

        [Header("Подсказка")]
        [SerializeField] private bool _hud = true;
        [SerializeField] private bool _log = true;

        private readonly SyncVar<byte> _cuts = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _dux = new SyncVar<byte>(0);      // биты щелей с дюкселем
        private readonly SyncVar<byte> _soub = new SyncVar<byte>(0);     // биты щелей с субизом
        private readonly SyncVar<byte> _mornay = new SyncVar<byte>(0);   // порций морне сверху
        private readonly SyncVar<byte> _cheese = new SyncVar<byte>(0);   // порций сыра
        private readonly SyncVar<byte> _bake = new SyncVar<byte>(0);     // 0..255 = 0..(1 + burnAfter)
        private readonly SyncVar<bool> _juice = new SyncVar<bool>(false); // резали, не дав отдохнуть
        private readonly SyncVar<bool> _lumpy = new SyncVar<bool>(false); // морне из бешамели с комками

        private FoodPiece _food;
        private Rigidbody _rb;
        private bool _clamped, _clampWas;
        private float _bakeProgress;
        private int _bakeQuarter = -1;
        private bool _servedLogged;

        // ───────────────────────── состояние ─────────────────────────

        private bool IsStewed => _food != null && _food.Def != null && _food.Def.Title == CookRecipeBook.Orloff.VealStewed;
        private bool IsLoinStewed => _food != null && _food.Def != null && _food.Def.Title == CookRecipeBook.Orloff.VealLoin && _food.State == FoodState.Stewed;
        public bool Active => IsStewed;
        public int CutCount => _cuts.Value;
        public bool AllFilled => _cuts.Value >= Cuts && Bits(_dux.Value) >= Cuts && Bits(_soub.Value) >= Cuts;
        public bool Topped => _mornay.Value >= _mornayMin && _cheese.Value > 0;
        private float BakeK => _bake.Value / 255f * (1f + _burnAfter);
        public bool Baked => BakeK >= 1f;
        public bool Burnt => BakeK >= 1f + _burnAfter - 0.001f;

        private static int Bits(byte b) { int n = 0; for (int i = 0; i < 8; i++) if ((b & (1 << i)) != 0) n++; return n; }

        /// <summary>Звёзды 0–5 (0 — ещё не испечено).</summary>
        public int Stars
        {
            get
            {
                if (!Baked) return 0;
                int s = 1;
                if (!_juice.Value) s++;
                if (AllFilled) s++;
                if (!_lumpy.Value) s++;
                if (!Burnt) s++;
                else s = Mathf.Min(s, 2);
                return s;
            }
        }

        private void Awake()
        {
            _food = GetComponent<FoodPiece>();
            _rb = GetComponent<Rigidbody>();
        }

        // ───────────────────────── ПИЛА (ICuttable) ─────────────────────────

        public bool CanCut => (IsStewed || IsLoinStewed) && _cuts.Value < Cuts && _mornay.Value == 0;
        public float SecondsPerCut => 0f;
        public bool ClampWhileCutting => true;
        public string CutLabel => $"медальонов {_cuts.Value + 1}/{Cuts + 1}";
        public Color CrumbColor => new Color(0.55f, 0.32f, 0.22f);

        public Vector3 CutPoint()
        {
            Bounds b = LocalBounds();
            float x = Mathf.Lerp(b.min.x, b.max.x, (_cuts.Value + 1) / (float)(Cuts + 1));
            return transform.TransformPoint(new Vector3(x, b.max.y, b.center.z));
        }

        [Server]
        public void ServerCutOne(ProcessDef cutProcess, float quality01)
        {
            if (!CanCut) return;
            if (IsLoinStewed)
            {
                _juice.Value = true;
                ProductDef st = StewedProduct();
                if (st != null) _food.ServerSetProductState(st, FoodState.Stewed);
                if (_log) Debug.Log($"[Орлов] «{name}»: режут, не дав отдохнуть — СОК ВЫТЕК (звезды за мясо не будет)", this);
            }
            _cuts.Value = (byte)(_cuts.Value + 1);
            if (_log) Debug.Log($"[Орлов] «{name}»: разрез {_cuts.Value}/{Cuts} — медальонов {_cuts.Value + 1}" +
                                (_cuts.Value >= Cuts ? " · теперь мастерком в щели: дюксель, потом субиз" : ""), this);
        }

        public void ServerSetClamped(bool value)
        {
            if (_rb == null || _clamped == value) return;
            _clamped = value;
            if (value) { _clampWas = _rb.isKinematic; _rb.isKinematic = true; }
            else { _rb.isKinematic = _clampWas; _rb.WakeUp(); }
        }

        private ProductDef _stewedP;
        private ProductDef StewedProduct()
        {
            if (_stewedP != null) return _stewedP;
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return null;
            for (int id = 1; id < 2048; id++)
            {
                ProductDef p = db.Get((ushort)id);
                if (p != null && p.Title == CookRecipeBook.Orloff.VealStewed) { _stewedP = p; return p; }
            }
            return null;
        }

        // ───────────────────────── МАСТЕРОК (IScoopReceiver) ─────────────────────────

        public int ServerReceiveScoop(ushort product, FoodState state, int count, Vector3 point, Vector3 normal, out string why)
        {
            why = null;
            if (count <= 0) return 0;
            ProductDef d = FoodDatabase.Instance != null ? FoodDatabase.Instance.Get(product) : null;
            string t = d != null && d.Title != null ? d.Title.Trim().ToLowerInvariant() : "";
            if (!IsStewed && !IsLoinStewed) { why = "телятина ещё не протушилась — сначала обжарить, тушить под крышкой, дать отдохнуть"; return 0; }
            if (_bake.Value > 0) { why = "уже в духовке — сверху больше не кладут"; return 0; }

            bool dux = t == "дюксель", soub = t == "субиз";
            if (dux || soub)
            {
                if (_cuts.Value == 0) { why = "сначала нарежь пилой на медальоны — прослаивают щели между ними"; return 0; }
                if (_mornay.Value > 0) { why = "уже под соусом — прослойку кладут до морне"; return 0; }
                float lx = transform.InverseTransformPoint(point).x;
                int took = 0;
                for (int k = 0; k < count; k++)
                {
                    int g = NearestGap(lx, dux ? _dux.Value : _soub.Value, soub);
                    if (g < 0) break;
                    if (dux) _dux.Value = (byte)(_dux.Value | (1 << g)); else _soub.Value = (byte)(_soub.Value | (1 << g));
                    took++;
                }
                if (took == 0)
                {
                    why = soub && Bits(_dux.Value) <= Bits(_soub.Value) ? "субиз кладут ПОСЛЕ дюкселя — сначала дюксель в щель"
                        : $"все разрезанные щели уже с «{d.Title}»" + (_cuts.Value < Cuts ? " — нарежь ещё" : "");
                    return 0;
                }
                if (_log) Debug.Log($"[Орлов] «{name}»: «{d.Title}» в щели +{took} — дюксель {Bits(_dux.Value)}/{Cuts}, субиз {Bits(_soub.Value)}/{Cuts}", this);
                return took;
            }

            bool morn = t == "соус морне", lumpy = t == "бешамель с комками", bech = t == "бешамель";
            if (morn || lumpy || bech)
            {
                if (bech) { why = "это ещё бешамель — добавь сыр тёртый и мускат, миксером → «Соус морне»"; return 0; }
                if (!AllFilled) { why = $"сначала прослойка: дюксель {Bits(_dux.Value)}/{Cuts}, субиз {Bits(_soub.Value)}/{Cuts}" + (_cuts.Value < Cuts ? $", разрезов {_cuts.Value}/{Cuts}" : ""); return 0; }
                int room = _mornayMax - _mornay.Value;
                if (room <= 0) { why = "соуса сверху и так достаточно — посыпь сыром и в духовку"; return 0; }
                int took = Mathf.Min(room, count);
                _mornay.Value = (byte)(_mornay.Value + took);
                if (lumpy) _lumpy.Value = true;
                if (_log) Debug.Log($"[Орлов] «{name}»: морне сверху {_mornay.Value}/{_mornayMax} порц." + (lumpy ? " (с комками — звезды за соус не будет)" : ""), this);
                return took;
            }

            if (t.Contains("сыр"))
            {
                if (state != FoodState.Grated) { why = "сыр — тёртый: головку через измельчитель"; return 0; }
                if (_mornay.Value < _mornayMin) { why = $"сначала соус морне сверху (от {_mornayMin} порц.)"; return 0; }
                int room = _cheeseMax - _cheese.Value;
                if (room <= 0) { why = "сыра достаточно — в духовку"; return 0; }
                int took = Mathf.Min(room, count);
                _cheese.Value = (byte)(_cheese.Value + took);
                if (_log) Debug.Log($"[Орлов] «{name}»: сыр сверху — готово к духовке (от {_ovenFromC:0} °C)", this);
                return took;
            }
            why = "на телятину по-орловски: дюксель и субиз в щели, сверху соус морне и тёртый сыр";
            return 0;
        }

        /// <summary>Свободная щель (уже разрезанная), ближайшая к броску. Субиз — только туда, где уже дюксель.</summary>
        private int NearestGap(float localX, byte filled, bool needDux)
        {
            Bounds b = LocalBounds();
            int best = -1; float bd = float.MaxValue;
            for (int g = 0; g < _cuts.Value; g++)
            {
                if ((filled & (1 << g)) != 0) continue;
                if (needDux && (_dux.Value & (1 << g)) == 0) continue;
                float gx = Mathf.Lerp(b.min.x, b.max.x, (g + 1) / (float)(Cuts + 1));
                float dd = Mathf.Abs(gx - localX);
                if (dd < bd) { bd = dd; best = g; }
            }
            return best;
        }

        // ───────────────────────── ДУХОВКА ─────────────────────────

        /// <summary>Духовка, в которой лежит буханка: по центру габарита (у корейки опорная точка внизу), чуть выше и по опорной точке.</summary>
        private BakeOven OvenHere()
        {
            Bounds wb = WorldBounds();
            BakeOven o = BakeOven.Find(wb.center);
            if (o == null) o = BakeOven.Find(wb.center + Vector3.up * wb.extents.y * 0.5f);
            if (o == null) o = BakeOven.Find(transform.position + Vector3.up * 0.02f);
            return o;
        }

        private void Update()
        {
            if (base.IsServerInitialized) { ServerHoldPlaced(); ServerBake(Time.deltaTime); }
            UpdateLook();
        }

        [Server]
        private void ServerBake(float dt)
        {
            if (!IsStewed || !Topped || _food.IsHeld || Burnt) return;
            BakeOven oven = OvenHere();
            if (oven == null || oven.TemperatureC < _ovenFromC) return;
            float speed = Mathf.Clamp(oven.TemperatureC / 200f, 0.6f, 1.5f) * oven.BakeSpeed;
            float k = Mathf.Max(_bakeProgress, BakeK) + dt * speed / _bakeSeconds;
            _bakeProgress = Mathf.Min(1f + _burnAfter, k);
            byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(_bakeProgress / (1f + _burnAfter) * 255f), 0, 255);
            if (b != _bake.Value) _bake.Value = b;
            int q = Mathf.FloorToInt(_bakeProgress * 4f);
            if (_log && q != _bakeQuarter)
            {
                _bakeQuarter = q;
                if (q > 0 && q < 4) Debug.Log($"[Орлов] «{name}»: корочка {q * 25}% ({oven.TemperatureC:0} °C)", this);
                else if (q == 4) Debug.Log($"[Орлов] «{name}»: ГОТОВО — корочка золотая, {Stars}★. Доставай и подавай на тарелку (ещё ~{_burnAfter * _bakeSeconds:0} с — подгорит)", this);
            }
            if (_log && Burnt) Debug.Log($"[Орлов] «{name}»: корочка ПОДГОРЕЛА — {Stars}★", this);
        }

        // ───────────────────────── ПКМ: НА РЕШЁТКУ ─────────────────────────

        private readonly RaycastHit[] _ray = new RaycastHit[16];
        private Vector3 _holdAt;
        private Quaternion _holdRot;
        private float _holdUntil;
        private BakeOven _placedOven;
        private Vector3 _placedOvenPos;

        /// <summary>
        /// ПКМ с буханкой (корейкой) в руке, прицел на решётку открытой духовки: кладётся ровно, вдоль решётки, по центру прицела
        /// (не вылезая за края), стоит на месте — не катается. Мимо духовки — ничего.
        /// </summary>
        public bool ServerHandleSecondary(FoodPiece piece, UseContext ctx)
        {
            if (ctx.Hand == null) return false;
            Vector3 dir = ctx.Direction.sqrMagnitude > 1e-6f ? ctx.Direction.normalized : Vector3.forward;
            BakeOven oven = null; Collider grill = null; Vector3 aim = Vector3.zero; float bestT = float.MaxValue;
            for (int i = 0; i < BakeOven.All.Count; i++)
            {
                BakeOven o = BakeOven.All[i];
                if (o == null || !o.TryGetGrill(out Collider g) || Mathf.Abs(dir.y) < 1e-3f) continue;
                float tt = (g.bounds.max.y - ctx.Origin.y) / dir.y;
                if (tt <= 0f || tt > 5f || tt >= bestT) continue;
                Vector3 p = ctx.Origin + dir * tt;
                Bounds gb0 = g.bounds;
                if (p.x < gb0.min.x - 0.5f || p.x > gb0.max.x + 0.5f || p.z < gb0.min.z - 0.5f || p.z > gb0.max.z + 0.5f) continue;
                bestT = tt; oven = o; grill = g; aim = p;
            }
            if (oven == null)
            {
                if (_log) Debug.Log($"[Орлов] «{name}»: ПКМ — не вижу решётки духовки (смотри на решётку, дверца открыта)", this);
                return false;
            }
            Bounds gb = grill.bounds, cb = oven.ChamberBounds, lb = LocalBounds();
            float minX = Mathf.Max(gb.min.x, cb.min.x), maxX = Mathf.Min(gb.max.x, cb.max.x);
            float minZ = Mathf.Max(gb.min.z, cb.min.z), maxZ = Mathf.Min(gb.max.z, cb.max.z);
            bool alongX = (maxX - minX) >= (maxZ - minZ);
            Quaternion rot = alongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
            Vector3 sc = transform.lossyScale;
            float halfLen = lb.extents.x * Mathf.Abs(sc.x), halfWid = lb.extents.z * Mathf.Abs(sc.z);
            float hx = alongX ? halfLen : halfWid, hz = alongX ? halfWid : halfLen;
            Vector3 c = new Vector3(Fit(aim.x, minX, maxX, hx), 0f, Fit(aim.z, minZ, maxZ, hz));
            if (hx * 2f > maxX - minX || hz * 2f > maxZ - minZ)
                Debug.LogWarning($"[Орлов] «{name}»: буханка больше решётки ({hx * 2f:0.00}×{hz * 2f:0.00} м, решётка {maxX - minX:0.00}×{maxZ - minZ:0.00}) — положил по центру; уменьши корейку (меню Орлов → 4) или масштаб префаба", this);
            // опорная точка корейки — снизу посередине: сдвиг центра габарита
            Vector3 centerOff = rot * Vector3.Scale(new Vector3(lb.center.x, 0f, lb.center.z), sc);
            float y = gb.max.y - lb.min.y * Mathf.Abs(sc.y) + 0.004f;
            Vector3 target = new Vector3(c.x, y, c.z) - centerOff;

            ctx.Hand.ServerDrop(dir, 0f);
            if (_rb != null) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; _rb.isKinematic = true; }
            transform.SetPositionAndRotation(target, rot);
            if (_rb != null) { _rb.position = target; _rb.rotation = rot; }
            _holdAt = target; _holdRot = rot; _holdUntil = Time.time + 1f;
            _placedOven = oven; _placedOvenPos = oven.transform.position;
            if (_log) Debug.Log($"[Орлов] «{name}»: уложили на решётку «{oven.name}» ({oven.TemperatureC:0} °C)" +
                                (IsStewed && Topped ? "" : " — но ещё не готово к духовке (нужны морне и сыр)"), this);
            return true;
        }

        private static float Fit(float v, float min, float max, float half)
        {
            if (max - min <= half * 2f) return (min + max) * 0.5f;
            return Mathf.Clamp(v, min + half, max - half);
        }

        /// <summary>Уложенная на решётку — стоит (кинематика), пока не возьмут в руку или не сдвинут духовку.</summary>
        [Server]
        private void ServerHoldPlaced()
        {
            if (_placedOven == null) return;
            bool release = _food == null || _food.IsHeld || _food.IsConsumed || (_placedOven.transform.position - _placedOvenPos).sqrMagnitude > 1e-4f;
            if (release)
            {
                _placedOven = null;
                if (_rb != null && !_food.IsHeld && !_clamped) { _rb.isKinematic = false; _rb.linearVelocity = Vector3.zero; }
                return;
            }
            if (_rb != null && !_rb.isKinematic) _rb.isKinematic = true;
            if (Time.time < _holdUntil && (transform.position - _holdAt).sqrMagnitude > 1e-6f)
            {
                transform.SetPositionAndRotation(_holdAt, _holdRot);
                if (_rb != null) { _rb.position = _holdAt; _rb.rotation = _holdRot; }
            }
        }

        // ───────────────────────── ПОДАЧА ─────────────────────────

        private readonly RaycastHit[] _down = new RaycastHit[8];
        private float _servedAt;
        private bool _served;

        /// <summary>Лежит на тарелке / блюде / доске (смотрим вниз под буханку).</summary>
        public bool Served
        {
            get
            {
                if (Time.time < _servedAt) return _served;
                _servedAt = Time.time + 0.3f;
                _served = false;
                if (!Baked || _food == null || _food.IsHeld) return false;
                Bounds wb = WorldBounds();
                int n = Physics.RaycastNonAlloc(new Ray(wb.center, Vector3.down), _down, wb.extents.y + 0.4f, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < n; i++)
                {
                    Collider c = _down[i].collider;
                    if (c == null || c.transform.IsChildOf(transform)) continue;
                    if (BakeOven.Find(wb.center) != null) continue;
                    if (IsPlate(c.transform)) { _served = true; break; }
                }
                if (_served && !_servedLogged && base.IsServerInitialized && _log)
                {
                    _servedLogged = true;
                    Debug.Log($"[Орлов] «{name}»: ПОДАНО — телятина по-орловски {Stars}★" +
                              (_juice.Value ? " · сок вытек" : "") + (!AllFilled ? " · не все щели" : "") + (_lumpy.Value ? " · морне с комками" : "") + (Burnt ? " · подгорело" : ""), this);
                }
                return _served;
            }
        }

        private static bool IsPlate(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
            {
                string n = p.name.ToLowerInvariant();
                if (n.Contains("plate") || n.Contains("tarel") || n.Contains("тарел") || n.Contains("board") || n.Contains("доск") || n.Contains("bowl") || n.Contains("dish") || n.Contains("tray") || n.Contains("блюд")) return true;
                Cookware cw = p.GetComponent<Cookware>();
                if (cw != null && cw.Def != null && !string.IsNullOrEmpty(cw.Def.Title))
                {
                    string d = cw.Def.Title.ToLowerInvariant();
                    if (d.Contains("тарел") || d.Contains("блюд") || d.Contains("миск") || d.Contains("доск") || d.Contains("поднос")) return true;
                }
            }
            return false;
        }

        // ───────────────────────── ВИД ─────────────────────────

        private Transform _layers, _coat, _cheeseT;
        private Mesh _layersMesh;
        private MeshRenderer _coatMr, _cheeseMr;
        private Material _coatMat, _cheeseMat;
        private int _shownKey = -1;
        private Bounds _lb;
        private bool _lbSet;

        private Bounds LocalBounds()
        {
            if (_lbSet) return _lb;
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) { _lb = mf.sharedMesh.bounds; _lbSet = true; return _lb; }
            BoxCollider bc = GetComponent<BoxCollider>();
            return bc != null ? new Bounds(bc.center, bc.size) : new Bounds(Vector3.zero, Vector3.one * 0.5f);
        }

        private Bounds WorldBounds()
        {
            Renderer r = GetComponent<Renderer>();
            return r != null ? r.bounds : new Bounds(transform.position, Vector3.one * 0.5f);
        }

        private void UpdateLook()
        {
            if (_food == null || !(IsStewed || IsLoinStewed) && _cuts.Value == 0) { Hide(); return; }
            int key = _cuts.Value | (_dux.Value << 3) | (_soub.Value << 8) | (_mornay.Value > 0 ? 1 << 13 : 0) | (Mathf.Min(_mornay.Value, 15) << 14) | (_cheese.Value > 0 ? 1 << 18 : 0);
            if (key != _shownKey) { _shownKey = key; BuildLayers(); }
            Tint();
        }

        private void Hide()
        {
            if (_layers != null) _layers.gameObject.SetActive(false);
            if (_coat != null) _coat.gameObject.SetActive(false);
            if (_cheeseT != null) _cheeseT.gameObject.SetActive(false);
        }

        /// <summary>Щели (тёмные), в них — слой дюкселя (тёмно-коричневый) и субиза (белый): сечения корейки чуть шире её.</summary>
        private void BuildLayers()
        {
            Bounds b = LocalBounds();
            if (_layers == null)
            {
                var go = new GameObject("Orloff_Layers (auto)") { hideFlags = HideFlags.DontSave };
                go.layer = gameObject.layer;
                _layers = go.transform; _layers.SetParent(transform, false);
                _layersMesh = new Mesh { name = "OrloffLayers", hideFlags = HideFlags.DontSave };
                go.AddComponent<MeshFilter>().sharedMesh = _layersMesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                MeshRenderer own = GetComponent<MeshRenderer>();
                mr.sharedMaterial = own != null ? own.sharedMaterial : null;        // мультяшный, цвет в вершинах
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _layers.gameObject.SetActive(true);
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            float L = b.size.x, W = b.size.z * 0.5f, H = b.size.y;
            Color gap = new Color(0.30f, 0.10f, 0.08f), dux = new Color(0.26f, 0.17f, 0.10f), soub = new Color(0.97f, 0.94f, 0.84f);
            for (int g = 0; g < _cuts.Value; g++)
            {
                float x = Mathf.Lerp(b.min.x, b.max.x, (g + 1) / (float)(Cuts + 1));
                bool hd = (_dux.Value & (1 << g)) != 0, hs = (_soub.Value & (1 << g)) != 0;
                if (!hd && !hs) Slab(v, c, t, x, L * 0.012f, W * 1.02f, H * 1.01f, b, gap);
                if (hd) Slab(v, c, t, x - (hs ? L * 0.017f : 0f), L * 0.032f, W * 1.08f, H * 1.07f, b, dux);
                if (hs) Slab(v, c, t, x + (hd ? L * 0.017f : 0f), L * 0.032f, W * 1.10f, H * 1.09f, b, soub);
            }
            _layersMesh.Clear();
            _layersMesh.SetVertices(v); _layersMesh.SetColors(c); _layersMesh.SetTriangles(t, 0);
            _layersMesh.RecalculateNormals(); _layersMesh.RecalculateBounds();

            // морне сверху — та же форма, чуть больше; сыр — крапинки
            bool coat = _mornay.Value > 0;
            if (coat && _coat == null)
            {
                MeshFilter own = GetComponent<MeshFilter>();
                var go = new GameObject("Orloff_Mornay (auto)") { hideFlags = HideFlags.DontSave };
                go.layer = gameObject.layer;
                _coat = go.transform; _coat.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = own != null ? own.sharedMesh : null;
                _coatMr = go.AddComponent<MeshRenderer>();
                _coatMat = NewMat(new Color(0.98f, 0.93f, 0.74f), 0.55f);
                _coatMr.sharedMaterial = _coatMat;
            }
            if (_coat != null)
            {
                _coat.gameObject.SetActive(coat);
                float k = 1.02f + 0.006f * _mornay.Value;
                _coat.localPosition = new Vector3(b.center.x * (1f - k), b.min.y * (1f - k) + H * 0.01f, b.center.z * (1f - k));
                _coat.localScale = new Vector3(k, 1f + 0.012f * _mornay.Value, k * 1.01f);
            }
            bool ch = _cheese.Value > 0;
            if (ch && _cheeseT == null)
            {
                var go = new GameObject("Orloff_Cheese (auto)") { hideFlags = HideFlags.DontSave };
                go.layer = gameObject.layer;
                _cheeseT = go.transform; _cheeseT.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = CheeseMesh(b);
                _cheeseMr = go.AddComponent<MeshRenderer>();
                _cheeseMat = NewMat(new Color(0.99f, 0.86f, 0.35f), 0.3f);
                _cheeseMr.sharedMaterial = _cheeseMat;
                _cheeseMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (_cheeseT != null) _cheeseT.gameObject.SetActive(ch);
        }

        /// <summary>Сечение корейки (скруглённое: плоское дно, купол) толщиной th вдоль X.</summary>
        private static void Slab(List<Vector3> v, List<Color> c, List<int> t, float x, float th, float halfW, float H, Bounds b, Color col)
        {
            const int N = 20;
            float y0 = b.min.y, zc = b.center.z;
            var ring = new Vector3[N];
            for (int i = 0; i < N; i++)
            {
                float a = i / (float)N * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                float z = Mathf.Sign(ca) * Mathf.Pow(Mathf.Abs(ca), 0.7f) * halfW;
                float y = sa >= 0f ? H * 0.42f + sa * H * 0.58f : H * 0.42f + Mathf.Sign(sa) * Mathf.Pow(Mathf.Abs(sa), 0.45f) * H * 0.42f;
                ring[i] = new Vector3(0f, y0 + y, zc + z);
            }
            for (int side = 0; side < 2; side++)
            {
                float sx = x + (side == 0 ? -th * 0.5f : th * 0.5f);
                Vector3 nn = side == 0 ? Vector3.left : Vector3.right;
                int ctr = v.Count; v.Add(new Vector3(sx, y0 + H * 0.42f, zc)); c.Add(col);
                int r0 = v.Count;
                for (int i = 0; i < N; i++) { v.Add(new Vector3(sx, ring[i].y, ring[i].z)); c.Add(col); }
                for (int i = 0; i < N; i++) Tri(v, t, ctr, r0 + i, r0 + (i + 1) % N, nn);
            }
            int s0 = v.Count;
            for (int i = 0; i < N; i++)
            {
                v.Add(new Vector3(x - th * 0.5f, ring[i].y, ring[i].z)); c.Add(col * 0.9f);
                v.Add(new Vector3(x + th * 0.5f, ring[i].y, ring[i].z)); c.Add(col * 0.9f);
            }
            for (int i = 0; i < N; i++)
            {
                int a = s0 + i * 2, bb = s0 + ((i + 1) % N) * 2;
                Vector3 nn = new Vector3(0f, ring[i].y - (y0 + H * 0.42f), ring[i].z - zc);
                Tri(v, t, a, a + 1, bb, nn); Tri(v, t, bb, a + 1, bb + 1, nn);
            }
        }

        private static void Tri(List<Vector3> v, List<int> t, int a, int b, int c, Vector3 nn)
        {
            Vector3 fn = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(fn, nn) >= 0f) { t.Add(a); t.Add(b); t.Add(c); } else { t.Add(a); t.Add(c); t.Add(b); }
        }

        /// <summary>Тёртый сыр: россыпь мелких плашек по верху буханки.</summary>
        private static Mesh CheeseMesh(Bounds b)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            var rnd = new System.Random(11);
            float L = b.size.x, W = b.size.z * 0.5f, H = b.size.y;
            for (int k = 0; k < 160; k++)
            {
                float u = (float)rnd.NextDouble() * 2f - 1f, w = (float)rnd.NextDouble() * 2f - 1f;
                float x = b.center.x + u * L * 0.46f, z = b.center.z + w * W * 0.8f;
                float y = b.min.y + H * (1.08f - 0.35f * w * w) + 0.004f;
                float s = Mathf.Max(0.004f, L * 0.006f) * (0.6f + (float)rnd.NextDouble());
                float ang = (float)rnd.NextDouble() * 6.28f;
                Vector3 ax = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * s * 1.8f, az = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * s * 0.5f;
                Vector3 p = new Vector3(x, y, z);
                int i = v.Count;
                v.Add(p - ax - az); v.Add(p + ax - az); v.Add(p + ax + az); v.Add(p - ax + az);
                t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2);
            }
            var m = new Mesh { name = "OrloffCheese", hideFlags = HideFlags.DontSave };
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private static Material NewMat(Color c, float smooth)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh) { hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            return m;
        }

        /// <summary>Корочка: кремовый соус → золотистый → подгоревший; сыр — жёлтый → золотисто-коричневый.</summary>
        private void Tint()
        {
            float k = BakeK;
            Color cream = new Color(0.98f, 0.93f, 0.74f), gold = new Color(0.86f, 0.58f, 0.24f), burnt = new Color(0.25f, 0.15f, 0.08f);
            Color cc = k <= 1f ? Color.Lerp(cream, gold, k) : Color.Lerp(gold, burnt, (k - 1f) / _burnAfter);
            if (_coatMat != null) Set(_coatMat, cc);
            if (_cheeseMat != null) Set(_cheeseMat, k <= 1f ? Color.Lerp(new Color(0.99f, 0.86f, 0.35f), new Color(0.80f, 0.45f, 0.14f), k) : Color.Lerp(new Color(0.80f, 0.45f, 0.14f), burnt, (k - 1f) / _burnAfter));
        }

        private static void Set(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        private void OnDestroy()
        {
            if (_layersMesh != null) Destroy(_layersMesh);
            if (_coatMat != null) Destroy(_coatMat);
            if (_cheeseMat != null) Destroy(_cheeseMat);
        }

        // ───────────────────────── ПОДСКАЗКА ─────────────────────────

        private static GUIStyle _style;

        private void OnGUI()
        {
            if (!_hud || Event.current.type != EventType.Repaint || _food == null || _food.IsConsumed || !(IsStewed || IsLoinStewed)) return;
            if (IsLoinStewed && _cuts.Value == 0) return;            // отдыхает — подсказку даёт VealLoinCook
            Camera cam = Camera.main;
            if (cam == null) return;
            Bounds wb = WorldBounds();
            Vector3 w = new Vector3(wb.center.x, wb.max.y + 0.3f, wb.center.z);
            if ((w - cam.transform.position).sqrMagnitude > 144f) return;
            Vector3 s = cam.WorldToScreenPoint(w);
            if (s.z <= 0f) return;
            string text = Status(out float bar, out Color barCol, out float zoneFrom);
            if (string.IsNullOrEmpty(text)) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 13, wordWrap = false, richText = true };
            Vector2 size = _style.CalcSize(new GUIContent(text));
            float bw = Mathf.Max(size.x + 12f, 160f), bh = size.y + (bar >= 0f ? 14f : 4f);
            Rect box = new Rect(s.x - bw * 0.5f, Screen.height - s.y - bh, bw, bh);
            GUI.Box(box, text, _style);
            if (bar >= 0f)
            {
                Color was = GUI.color;
                Rect r = new Rect(box.x + 6f, box.yMax - 9f, box.width - 12f, 6f);
                GUI.color = new Color(0f, 0f, 0f, 0.65f); GUI.DrawTexture(r, Texture2D.whiteTexture);
                if (zoneFrom >= 0f)
                {
                    GUI.color = new Color(0.30f, 0.75f, 0.30f, 0.45f);
                    GUI.DrawTexture(new Rect(r.x + r.width * zoneFrom, r.y, r.width * (1f - zoneFrom), r.height), Texture2D.whiteTexture);
                }
                GUI.color = barCol; GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(bar), r.height), Texture2D.whiteTexture);
                GUI.color = was;
            }
        }

        private static string StarsText(int n) => new string('★', n) + new string('☆', 5 - n);

        private string Status(out float bar, out Color barCol, out float zoneFrom)
        {
            bar = -1f; zoneFrom = -1f; barCol = new Color(0.95f, 0.6f, 0.2f);
            int cuts = _cuts.Value, nd = Bits(_dux.Value), ns = Bits(_soub.Value);
            if (cuts < Cuts) { bar = cuts / (float)Cuts; return $"Нарежь пилой на 6 медальонов: {cuts + 1}/6" + (cuts > 0 ? $" · в щелях: дюксель {nd}, субиз {ns}" : ""); }
            if (!AllFilled) { bar = (nd + ns) / (2f * Cuts); return $"Прослойка мастерком: дюксель {nd}/{Cuts} · субиз {ns}/{Cuts}" + (nd > ns ? " (субиз — поверх дюкселя)" : ""); }
            if (_mornay.Value < _mornayMin) return $"Сверху соус морне мастерком: {_mornay.Value}/{_mornayMin}+ порц.";
            if (_cheese.Value == 0) return $"Морне {_mornay.Value} порц. ✔ — посыпь тёртым сыром (мастерок)";
            float k = BakeK;
            if (k <= 0f)
            {
                BakeOven ov = OvenHere();
                if (ov != null && ov.TemperatureC < _ovenFromC) return $"<color=#FF8A6A>В духовке холодно: {ov.TemperatureC:0} °C</color>, нужно от {_ovenFromC:0} °C";
                bar = 0f; zoneFrom = 1f / (1f + _burnAfter);
                return $"Готово к духовке (от {_ovenFromC:0} °C) — ПКМ по решётке, корочка ~{_bakeSeconds:0} с";
            }
            float full = 1f + _burnAfter;
            zoneFrom = 1f / full;
            if (k < 1f)
            {
                bar = k / full;
                BakeOven ov = OvenHere();
                float sp = ov != null && ov.TemperatureC >= _ovenFromC ? Mathf.Clamp(ov.TemperatureC / 200f, 0.6f, 1.5f) * ov.BakeSpeed : 0f;
                return sp > 0f ? $"Корочка {Mathf.RoundToInt(k * 100f)}% · ещё ~{Mathf.CeilToInt((1f - k) * _bakeSeconds / sp)} с" : $"Корочка {Mathf.RoundToInt(k * 100f)}% — верни в горячую духовку";
            }
            barCol = Burnt ? new Color(0.6f, 0.25f, 0.15f) : new Color(0.35f, 0.85f, 0.3f);
            string stars = StarsText(Stars);
            if (Served) return $"<color=#7CFC6A>Подано ✔</color> Телятина по-орловски {stars}";
            if (Burnt) return $"<color=#FF8A6A>Подгорело</color> {stars} — на тарелку";
            bool inOven = OvenHere() != null;
            bar = k / full;
            return inOven ? $"Корочка золотая ✔ {stars} — <color=#FFD36A>доставай, а то подгорит</color>" : $"Готово ✔ {stars} — подавай на тарелку / блюдо / доску";
        }
    }
}
