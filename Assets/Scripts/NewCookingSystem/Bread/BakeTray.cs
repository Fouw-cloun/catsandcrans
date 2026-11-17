using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ОПАЛУБКА (ФОРМА ДЛЯ БИСКВИТА) — противень с бортиками для ЖИДКОГО теста.
    ///
    ///   1. Налей бисквитное тесто из ведра (ЛКМ ведром — как воду). Это обычная тара (Cookware):
    ///      уровень теста видно, лишнее переливается.
    ///   2. ЛКМ с опалубкой в руке, глядя в духовку, — встаёт ровно на решётку (как форма для хлеба).
    ///   3. Духовка на огне, от Bake From C: тесто превращается в пласт (Fill) — поднимается и темнеет.
    ///      Открыл крышку духовки в первой половине выпечки — бисквит ОСЕДАЕТ (ниже, плотнее).
    ///   4. Готово — v23.51: бисквит НЕ вынимается: режь пилой прямо в опалубке, кубики вылезают наверх (сетка — как у Slab Prefab),
    ///      его режут пилой на квадраты. F по пустой — просто взять.
    ///
    /// ═══ ЧТО НА КОРНЕ ═══
    ///   NetworkObject · NetworkTransform · Rigidbody · Cookware (Def — «Опалубка», Absorbs выкл) ·
    ///   ЭТОТ компонент (он же «предмет в руки» — Pickup Item / Carried Container не нужны) ·
    ///   коллайдеры дна и бортиков · LiquidAnchor (Cookware Liquid Visual, форма «Прямоугольная») ·
    ///   Fill — меш пласта бисквита внутри (низ — у дна опалубки), сначала скрыт.
    ///   Cookware Heat НЕ нужен: опалубка смотрит на температуру духовки сама.
    /// Настроить: CatCooks → Десерты → 3. Настроить выделенную опалубку.
    /// </summary>
    public class BakeTray : PickupItem, ICuttable, ICuttableAim      // v23.51: бисквит режут пилой ПРЯМО В ОПАЛУБКЕ
    {
        [Header("Что печём")]
        [Tooltip("Жидкое тесто (Бисквитное тесто)")]
        [SerializeField] private ProductDef _batter;
        [Tooltip("Что получается (Бисквит)")]
        [SerializeField] private ProductDef _result;
        [Tooltip("Сетевой префаб пласта (FoodPiece; для пилы — Food Sliceable с квадратами-слайсами)")]
        [SerializeField] private GameObject _slabPrefab;
        [Tooltip("Меньше этого теста — не печётся (порций)")]
        [Min(1)][SerializeField] private int _minPortions = 12;
        [Tooltip("Порций теста на одну порцию бисквита (30 порций теста → 6)")]
        [Min(1)][SerializeField] private int _batterPerPortion = 5;

        [Header("Выпечка")]
        [Tooltip("С какой температуры духовки печётся, °C")]
        [SerializeField] private float _bakeFromC = 150f;
        [Tooltip("Сколько секунд при ~180 °C с закрытой крышкой")]
        [Min(1f)][SerializeField] private float _bakeSeconds = 60f;
        [Tooltip("Открыли духовку раньше этой доли выпечки — бисквит оседает")]
        [Range(0f, 1f)][SerializeField] private float _fallBefore = 0.5f;

        [Header("Пласт в опалубке (Fill)")]
        [Tooltip("Меш пласта внутри опалубки, низ — у дна. Пусто — дочерний «Fill»")]
        [SerializeField] private Transform _fill;
        [Tooltip("Высота сырого теста от высоты готового бисквита")]
        [Range(0.2f, 1f)][SerializeField] private float _rawHeight = 0.5f;
        [Tooltip("Высота осевшего бисквита (открыли духовку рано)")]
        [Range(0.2f, 1f)][SerializeField] private float _fallenHeight = 0.65f;
        [Tooltip("v23.37: рисовать пласт бисквитом (купол, корочка с трещинками, дрожит как желе) вместо простой коробки Fill")]
        [SerializeField] private bool _cakeLook = true;
        [Tooltip("v23.39: готовый бисквит выше БОРТИКА опалубки: 1.25 — на четверть глубины опалубки выше края")]
        [Range(1f, 1.8f)][SerializeField] private float _bakedRise = 1.25f;
        [Tooltip("v23.39: панель выпечки (прогресс, температура, сколько ждать) — когда смотришь на опалубку в духовке рядом")]
        [SerializeField] private bool _bakeHud = true;
        [Tooltip("v23.37: купол сверху у готового (доля высоты)")]
        [Range(0f, 0.4f)][SerializeField] private float _dome = 0.14f;
        [SerializeField] private Color _batterColor = new Color(0.36f, 0.22f, 0.15f);
        [SerializeField] private Color _midColor = new Color(0.33f, 0.2f, 0.13f);
        [Tooltip("Цвет к концу. Если у материала пласта свой цвет (не белый) — берётся он")]
        [SerializeField] private Color _crustColor = new Color(0.26f, 0.15f, 0.09f);

        [Header("Постановка (ЛКМ в руке)")]
        [SerializeField] private float _placeReach = 3f;
        [SerializeField] private float _placeLift = 0.004f;
        [SerializeField] private bool _placeHint = true;
        [SerializeField, HideInInspector] private Quaternion _uprightRot = Quaternion.identity;
        [SerializeField, HideInInspector] private bool _hasUpright;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        public enum TrayState : byte { Empty = 0, Baking = 1, Baked = 2 }

        private readonly SyncVar<byte> _state = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _bake = new SyncVar<byte>(0);
        private readonly SyncVar<bool> _fallen = new SyncVar<bool>(false);
        private readonly SyncVar<byte> _portions = new SyncVar<byte>(0);
        private readonly SyncList<ushort> _cut = new SyncList<ushort>();          // v23.51: отпиленные кубики
        private readonly SyncVar<ushort> _aimCell = new SyncVar<ushort>(ushort.MaxValue);

        private Cookware _cw;
        private Renderer[] _fillR;
        private bool _steamMarked;
        private Material _fillMat;
        private Vector3 _fillScale, _fillPos;
        private float _fillMinY;
        private Color _crustEnd;
        private MaterialPropertyBlock _mpb;
        private float _progress, _hintAt;
        private int _lastQuarter = -1;
        private bool _heating;
        private Mesh _cakeMesh;                                                    // v23.37
        private Bounds _fillBounds;
        private SpongeJiggle _jiggle;
        private int _cakeKey = -1;
        private float _rimDepth;                                                   // v23.39: глубина опалубки до края бортика (в осях Fill)

        public TrayState State => (TrayState)_state.Value;
        public float Bake01 => _bake.Value / 255f;
        /// <summary>Сейчас печётся (для давления в духовке).</summary>
        public bool IsBakingNow => base.IsServerInitialized && _heating;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            _cw = GetComponent<Cookware>();
            if (_fill == null) _fill = transform.Find("Fill");
            if (_fill != null)
            {
                _fillScale = _fill.localScale;
                _fillPos = _fill.localPosition;
                _fillR = _fill.GetComponentsInChildren<Renderer>(true);
                _fillMat = _fillR.Length > 0 ? _fillR[0].sharedMaterial : null;
                MeshFilter mf = _fill.GetComponent<MeshFilter>();
                _fillMinY = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.min.y : 0f;
                // v23.37: вместо коробки — бисквит кодом (купол, корочка, трещинки), материал — как у пласта (цвет в вершинах)
                if (_cakeLook && mf != null && mf.sharedMesh != null && !Application.isBatchMode)
                {
                    _fillBounds = mf.sharedMesh.bounds;
                    _cakeMesh = new Mesh { name = "SpongeCake (auto)", hideFlags = HideFlags.DontSave };
                    _cakeMesh.MarkDynamic();
                    mf.sharedMesh = _cakeMesh;
                    MeshRenderer smr = _slabPrefab != null ? _slabPrefab.GetComponentInChildren<MeshRenderer>(true) : null;
                    if (smr != null && smr.sharedMaterial != null)
                        foreach (Renderer r in _fillR) if (r != null) r.sharedMaterial = smr.sharedMaterial;
                    _jiggle = _fill.GetComponent<SpongeJiggle>();
                    if (_jiggle == null) _jiggle = _fill.gameObject.AddComponent<SpongeJiggle>();
                    _jiggle.Strength = 0.014f;                                      // v23.39: трясётся меньше
                    _rimDepth = RimDepth();
                }
            }
            Color bc = BakeColor.BaseOf(_fillMat);
            _crustEnd = _fillMat != null && bc.r + bc.g + bc.b < 2.7f ? bc : _crustColor;
            if (!_hasUpright) { _uprightRot = transform.rotation; _hasUpright = true; }
            // v23.51: опалубка не «взрывается» о игрока — физика мягко расталкивает пересечения
            Rigidbody trb = GetComponent<Rigidbody>();
            if (trb != null) trb.maxDepenetrationVelocity = Mathf.Min(trb.maxDepenetrationVelocity, 1.5f);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (_log)
            {
                string warn = "";
                if (_cw == null) warn += " ✖ нет Cookware на корне — тесто не налить;";
                if (_batter == null) warn += " ✖ не задан Batter (Бисквитное тесто);";
                if (_slabPrefab == null) warn += " ⚠ не задан Slab Prefab — бисквит не достать;";
                if (_fill == null) warn += " ⚠ нет Fill — пласт в опалубке не видно;";
                if (GetComponent<CookwareHeat>() != null) warn += " ⚠ на опалубке Cookware Heat — убери (греет духовка сама);";
                if (warn.Length > 0) Debug.Log($"[Опалубка] «{name}»:{warn}", this);
            }
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _state.OnChange += OnChanged;
            _bake.OnChange += OnBake;
            _fallen.OnChange += OnFallen;
            _cut.OnChange += OnCut;
            _netStartAt = Time.time;
            ApplyLook();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _state.OnChange -= OnChanged;
            _bake.OnChange -= OnBake;
            _fallen.OnChange -= OnFallen;
            _cut.OnChange -= OnCut;
        }

        private void OnChanged(byte a, byte b, bool asServer) { if (asServer && base.IsClientInitialized) return; ApplyLook(); }
        private void OnBake(byte a, byte b, bool asServer) { if (asServer && base.IsClientInitialized) return; ApplyLook(); }
        private void OnFallen(bool a, bool b, bool asServer) { if (asServer && base.IsClientInitialized) return; ApplyLook(); }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        private void Update()
        {
            if (base.IsServerInitialized)
            {
                ServerTick(Time.deltaTime);
                if (base.IsClientInitialized) TestKeys();         // хост: тестовая клавиша
            }
        }

        private void ServerTick(float dt)
        {
            _heating = false;
            if (dt <= 0f) return;
            TrayState st = State;
            if (st == TrayState.Baked) return;
            BakeOven oven = FindOven();

            if (st == TrayState.Empty)
            {
                if (_cw == null) return;
                if (_batter == null)
                {
                    FoodDatabase db = FoodDatabase.Instance;
                    if (db != null) _batter = MacaronTitles.Find(db, "Бисквитное тесто");
                    if (_batter == null) { Hint("не задан Batter (Бисквитное тесто) — CatCooks → Десерты → 1 / 3"); return; }
                }
                int n = _cw.CountOf(_batter.Id);
                if (n <= 0) return;
                if (oven == null)
                {
                    BakeOven near = NearOven(4f);
                    if (near != null) Hint($"тесто налито ({n} порц.), но опалубка не внутри духовки «{near.name}» — ЛКМ с опалубкой в руках, глядя на решётку, и закрой (L)");
                    return;
                }
                if (n < _minPortions) { Hint($"в духовке, но теста мало: {n} порц., нужно от {_minPortions} — долей"); return; }
                if (_cw.TotalCount > n) { Hint("в опалубке кроме теста есть что-то ещё — вылей и налей чистое тесто"); return; }
                if (oven.TemperatureC < _bakeFromC) { Hint($"в духовке {oven.TemperatureC:0} °C — печётся с {_bakeFromC:0} °C (раздуй огонь)"); return; }
                // тесто стало пластом: из тары — в опалубку
                int took = _cw.ServerTake(_batter.Id, n);
                _portions.Value = (byte)Mathf.Clamp(took, 1, 255);
                _progress = 0f; _bake.Value = 0; _lastQuarter = -1; _fallen.Value = false;
                if (_cut.Count > 0) _cut.Clear();
                _state.Value = (byte)TrayState.Baking;
                if (_log) Debug.Log($"[Опалубка] «{name}»: тесто ({took} порц.) пошло в выпечку — не открывай духовку первые {_fallBefore * 100f:0}%", this);
                return;
            }

            // печётся
            if (oven == null) { Hint($"не в духовке — выпечка на паузе ({_progress * 100f:0}%)"); return; }
            float t = oven.TemperatureC;
            if (t < _bakeFromC) { Hint($"остыла духовка: {t:0} °C, нужно {_bakeFromC:0} — выпечка на паузе"); return; }
            _heating = true;
            if (oven.DoorOpen && _progress < _fallBefore && !_fallen.Value)
            {
                _fallen.Value = true;
                if (_log) Debug.Log($"[Опалубка] «{name}»: открыли духовку на {_progress * 100f:0}% — бисквит ОСЕЛ (будет ниже и плотнее)", this);
            }
            float speed = Mathf.Clamp(t / 180f, 0.6f, 1.5f) * oven.BakeSpeed;
            _progress = Mathf.Min(1f, _progress + dt * speed / _bakeSeconds);
            byte b = (byte)Mathf.RoundToInt(_progress * 255f);
            if (b != _bake.Value) _bake.Value = b;
            int q = Mathf.FloorToInt(_progress * 4f);
            if (_log && q != _lastQuarter && q > 0 && q < 4) Debug.Log($"[Опалубка] «{name}»: бисквит печётся {q * 25}% ({t:0} °C{(oven.DoorOpen ? ", открыто — медленнее" : "")})", this);
            _lastQuarter = q;
            if (_progress >= 1f)
            {
                _state.Value = (byte)TrayState.Baked;
                if (_log) Debug.Log($"[Опалубка] «{name}»: БИСКВИТ ГОТОВ{(_fallen.Value ? " (осевший)" : "")} — режь пилой прямо в опалубке (кубики вылезают наверх)", this);
            }
        }

        /// <summary>
        /// Духовка, в которой стоит опалубка. Опалубка широкая: её корень может торчать за полость,
        /// поэтому проверяем несколько точек — корень, пласт внутри, полость тары, горловину.
        /// </summary>
        private BakeOven FindOven()
        {
            BakeOven o = BakeOven.Find(transform.position + Vector3.up * 0.05f);
            if (o != null) return o;
            if (_fill != null && (o = BakeOven.Find(_fill.position + Vector3.up * 0.02f)) != null) return o;
            if (_cw != null)
            {
                if (_cw.InsideVolume != null && (o = BakeOven.Find(_cw.InsideVolume.bounds.center)) != null) return o;
                if ((o = BakeOven.Find(_cw.MouthPoint + Vector3.down * 0.03f)) != null) return o;
            }
            Renderer r = GetComponentInChildren<Renderer>();
            if (r != null && (o = BakeOven.Find(r.bounds.center)) != null) return o;
            // духовка сама видит коллайдеры опалубки у себя в полости
            if ((o = BakeOven.Holding(_cw)) != null) return o;
            // стоит на решётке, но чуть съехала/торчит: полость с запасом
            foreach (BakeOven ov in BakeOven.All)
            {
                if (ov == null) continue;
                Bounds b = ov.ChamberBounds;
                if (b.size.sqrMagnitude < 1e-6f) continue;
                b.Expand(0.3f);
                if (b.Contains(transform.position) || (_fill != null && b.Contains(_fill.position))) return ov;
            }
            return null;
        }

        // ───────────────────────── ТЕСТ (хост) ─────────────────────────
        //   P (смотришь на опалубку) — налить полную опалубку бисквитного теста;
        //   Shift+P — испечь сразу (тесто в опалубке → готовый пласт, F — достать).

        [Header("Тест (хост)")]
        [Tooltip("Клавиша: смотришь на опалубку — налить теста; с Shift — испечь сразу")]
        [SerializeField] private KeyCode _testKey = KeyCode.P;

        private void TestKeys()
        {
            if (_testKey == KeyCode.None || !Input.GetKeyDown(_testKey)) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            if (!Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out RaycastHit h, 6f, ~0, QueryTriggerInteraction.Collide)) return;
            if (!h.collider.transform.IsChildOf(transform) && h.collider.GetComponentInParent<BakeTray>() != this) return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shift) TestBake(); else TestFill();
        }

        [ContextMenu("Тест: налить полную опалубку теста (Play, хост)")]
        private void TestFill()
        {
            if (!NetTest.ServerReady(this, "Опалубка")) return;
            if (State != TrayState.Empty) { Debug.Log($"[Опалубка] «{name}»: тест — уже печётся/испечено, сначала достань пласт (F)", this); return; }
            if (_batter == null) { FoodDatabase db = FoodDatabase.Instance; if (db != null) _batter = MacaronTitles.Find(db, "Бисквитное тесто"); }
            if (_batter == null || _cw == null) { Debug.LogWarning($"[Опалубка] «{name}»: тест — нет продукта «Бисквитное тесто» (Десерты → 1)", this); return; }
            int room = Mathf.Max(0, _cw.Capacity - _cw.TotalCount);
            if (room > 0) _cw.ServerAddContent(_batter.Id, _batter.StartState, room, 0, false);
            Debug.Log($"[Опалубка] «{name}»: тест — налито теста до {_cw.CountOf(_batter.Id)} порц. Ставь в духовку (≥ {_bakeFromC:0} °C) или Shift+P — испечь сразу", this);
        }

        /// <summary>v23.49: Shift+N (аналитик тары) — налить теста и сразу испечь. Возвращает строку для подсказки.</summary>
        public string DebugBakeNow()
        {
            if (!base.IsServerInitialized) return "работает у хоста";
            if (State == TrayState.Baked) return "бисквит уже испечён — режь пилой прямо в опалубке";
            TestBake();
            return State == TrayState.Baked ? $"бисквит испечён сразу ({_portions.Value} порц.) — режь пилой прямо в опалубке"
                                            : "не вышло — нет продукта «Бисквитное тесто» (Десерты → 1)";
        }

        [ContextMenu("Тест: испечь сразу (Play, хост)")]
        private void TestBake()
        {
            if (!NetTest.ServerReady(this, "Опалубка")) return;
            if (State == TrayState.Baked) { Debug.Log($"[Опалубка] «{name}»: тест — уже испечено, F — достать", this); return; }
            if (State == TrayState.Empty)
            {
                if (_batter == null) { FoodDatabase db = FoodDatabase.Instance; if (db != null) _batter = MacaronTitles.Find(db, "Бисквитное тесто"); }
                int n = _batter != null && _cw != null ? _cw.CountOf(_batter.Id) : 0;
                if (n <= 0) { TestFill(); n = _batter != null && _cw != null ? _cw.CountOf(_batter.Id) : 0; }
                if (n <= 0) return;
                int took = _cw.ServerTake(_batter.Id, n);
                _portions.Value = (byte)Mathf.Clamp(took, 1, 255);
                _fallen.Value = false;
            }
            _progress = 1f;
            _bake.Value = 255;
            if (_cut.Count > 0) _cut.Clear();
            _state.Value = (byte)TrayState.Baked;
            Debug.Log($"[Опалубка] «{name}»: тест — ИСПЕЧЕНО сразу ({_portions.Value} порц.). Режь пилой прямо в опалубке", this);
        }

        private BakeOven NearOven(float maxDist)
        {
            BakeOven best = null;
            float bd = maxDist * maxDist;
            foreach (BakeOven o in BakeOven.All)
            {
                if (o == null) continue;
                float d = (o.transform.position - transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        private void Hint(string text)
        {
            if (!_log || Time.time < _hintAt) return;
            _hintAt = Time.time + 6f;
            Debug.Log($"[Опалубка] «{name}»: {text}", this);
        }

        /// <summary>F: взять опалубку (v23.51: бисквит из неё не вынимается — его режут пилой прямо в опалубке).</summary>
        public override void ServerInteract(PlayerInteractor interactor)
        {
            base.ServerInteract(interactor);
        }

        // ───────────────────────── v23.51: ПИЛА РЕЖЕТ В ОПАЛУБКЕ ─────────────────────────

        private int _gx, _gz;
        private bool[] _present;
        private Vector3 _cakeSize;
        private GameObject _piecePrefab;
        private bool _clampedK, _wasKin;
        private float _netStartAt;

        /// <summary>Сетка кубиков — как у пласта (Slab Prefab), иначе 4 × 3.</summary>
        private void EnsureGrid()
        {
            if (_gx > 0) return;
            FoodGridSlab slab = _slabPrefab != null ? _slabPrefab.GetComponent<FoodGridSlab>() : null;
            _gx = slab != null ? slab.CountX : 4;
            _gz = slab != null ? slab.CountZ : 3;
            _piecePrefab = slab != null ? slab.PiecePrefab : null;
            _present = new bool[_gx * _gz];
        }

        private void RefreshPresent()
        {
            EnsureGrid();
            for (int i = 0; i < _present.Length; i++) _present[i] = true;
            for (int i = 0; i < _cut.Count; i++) if (_cut[i] < _present.Length) _present[_cut[i]] = false;
        }

        public int CellsLeft { get { EnsureGrid(); return _gx * _gz - _cut.Count; } }

        private void OnCut(SyncListOperation op, int index, ushort a, ushort b, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            _cakeKey = -1;
            ApplyLook();
            if (op == SyncListOperation.Add && !asServer && Time.time - _netStartAt > 1f) CrumbBurst(b);
        }

        /// <summary>Центр кубика (оси Fill), низ бисквита — у дна опалубки.</summary>
        private Vector3 CellLocal(int i, float yK)
        {
            EnsureGrid();
            Vector3 sz = _cakeSize.sqrMagnitude > 1e-8f ? _cakeSize : _fillBounds.size;
            float cx = sz.x / _gx, cz = sz.z / _gz;
            int ix = i % _gx, iz = i / _gx;
            return new Vector3(_fillBounds.center.x - sz.x * 0.5f + (ix + 0.5f) * cx, _fillBounds.min.y + sz.y * yK, _fillBounds.center.z - sz.z * 0.5f + (iz + 0.5f) * cz);
        }

        private Vector3 CellWorld(int i, float yK) => _fill != null ? _fill.TransformPoint(CellLocal(i, yK)) : transform.position;

        private int CellAt(Vector3 world)
        {
            if (_fill == null) return -1;
            RefreshPresent();
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < _present.Length; i++)
            {
                if (!_present[i]) continue;
                Vector3 c = CellWorld(i, 0.5f);
                Vector3 d = c - world; d.y = 0f;
                if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = i; }
            }
            return best;
        }

        public bool CanCut => State == TrayState.Baked && _fill != null && CellsLeft > 0;
        public float SecondsPerCut => 0f;
        public bool ClampWhileCutting => true;
        public string CutLabel => $"кубиков {CellsLeft} из {_gx * _gz}";
        public Color CrumbColor { get { Color c = SpongeMesh.CutCrumb; c.a = 1f; return c; } }

        public Vector3 CutPoint()
        {
            int c = _aimCell.Value < (_gx * _gz) ? _aimCell.Value : CellAt(transform.position);
            return c >= 0 ? CellWorld(c, 1f) : transform.position;
        }

        public void ServerAimAt(Vector3 worldPoint)
        {
            if (!base.IsServerInitialized) return;
            int c = CellAt(worldPoint);
            if (c >= 0 && _aimCell.Value != c) _aimCell.Value = (ushort)c;
        }

        public bool AimBox(Vector3 worldPoint, Vector3[] corners)
        {
            if (corners == null || corners.Length < 8 || !CanCut) return false;
            int i = CellAt(worldPoint);
            if (i < 0) return false;
            Vector3 sz = _cakeSize.sqrMagnitude > 1e-8f ? _cakeSize : _fillBounds.size;
            Vector3 c = CellLocal(i, 0f);
            float hx = sz.x / _gx * 0.52f, hz = sz.z / _gz * 0.52f;
            float y0 = c.y - 0.004f, y1 = c.y + sz.y * (1f + _dome) + 0.006f;
            for (int k = 0; k < 2; k++)
            {
                float y = k == 0 ? y0 : y1;
                corners[k * 4 + 0] = _fill.TransformPoint(new Vector3(c.x - hx, y, c.z - hz));
                corners[k * 4 + 1] = _fill.TransformPoint(new Vector3(c.x + hx, y, c.z - hz));
                corners[k * 4 + 2] = _fill.TransformPoint(new Vector3(c.x + hx, y, c.z + hz));
                corners[k * 4 + 3] = _fill.TransformPoint(new Vector3(c.x - hx, y, c.z + hz));
            }
            return true;
        }

        public void ServerSetClamped(bool value)
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null || value == _clampedK) return;
            _clampedK = value;
            if (value) { _wasKin = rb.isKinematic; rb.isKinematic = true; }
            else { rb.isKinematic = _wasKin; rb.WakeUp(); }
        }

        /// <summary>Пила отпилила кубик: он «вылезает» из опалубки наверх и ложится рядом. Последний — опалубка пустая.</summary>
        public void ServerCutOne(ProcessDef cutProcess, float quality01)
        {
            if (!base.IsServerInitialized || !CanCut) return;
            EnsureGrid();
            int cell = _aimCell.Value < _gx * _gz ? _aimCell.Value : -1;
            RefreshPresent();
            if (cell < 0 || !_present[cell]) cell = CellAt(transform.position);
            if (cell < 0) return;
            _cut.Add((ushort)cell);
            _present[cell] = false;
            _aimCell.Value = ushort.MaxValue;
            SpawnPiece(cell);
            _cakeKey = -1;
            if (CellsLeft <= 0)
            {
                _state.Value = (byte)TrayState.Empty;
                _bake.Value = 0; _fallen.Value = false; _portions.Value = 0;
                _cut.Clear();
                if (_log) Debug.Log($"[Опалубка] «{name}»: бисквит разрезан весь — опалубка пустая", this);
            }
            else if (_log) Debug.Log($"[Опалубка] «{name}»: отрезан кубик — {CutLabel}", this);
        }

        [Server]
        private void SpawnPiece(int cell)
        {
            if (_piecePrefab == null) { Debug.LogError($"[Опалубка] «{name}»: у Slab Prefab нет Food Grid Slab с Piece Prefab — кубик не появится", this); return; }
            Vector3 sz = _cakeSize.sqrMagnitude > 1e-8f ? _cakeSize : _fillBounds.size;
            Vector3 cellSize = new Vector3(sz.x / _gx, sz.y, sz.z / _gz);
            int ix = cell % _gx, iz = cell / _gx;
            byte crust = 0;
            if (ix == 0) crust |= SpongeMesh.XNeg;
            if (ix == _gx - 1) crust |= SpongeMesh.XPos;
            if (iz == 0) crust |= SpongeMesh.ZNeg;
            if (iz == _gz - 1) crust |= SpongeMesh.ZPos;
            Vector3 at = CellWorld(cell, 1.05f) + Vector3.up * 0.02f;           // вылезает над опалубкой
            GameObject go = Instantiate(_piecePrefab, at, _fill.rotation);
            Vector3 ls = _fill.lossyScale;
            ls = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            FoodPiece piece = go.GetComponent<FoodPiece>();
            if (piece == null) { Destroy(go); return; }
            SpongePiece sp = go.GetComponent<SpongePiece>();
            if (sp != null) { go.transform.localScale = ls; sp.ServerSetup(cellSize, crust); }
            else go.transform.localScale = Vector3.Scale(cellSize, ls);
            FoodState st = FoodState.Pieces;
            FoodDatabase db = FoodDatabase.Instance;
            if (db != null && _result != null && db.Table != null && db.Table.TryGet(_result.Id, FoodState.Baked, FoodProcess.Cut, out FoodTransition t)) st = t.To;
            if (_result != null) piece.ServerPreset(_result, st, 100, 100, (byte)(_fallen.Value ? 70 : 100));
            base.Spawn(go);
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 out2 = CellWorld(cell, 0.5f) - (_fill != null ? _fill.position : transform.position);
                out2.y = 0f;
                if (out2.sqrMagnitude < 1e-6f) out2 = transform.right;
                rb.linearVelocity = out2.normalized * 0.6f + Vector3.up * 1.6f;     // мягко выпрыгивает наверх и в сторону
                rb.angularVelocity = Vector3.Cross(Vector3.up, out2.normalized) * 1.2f;
            }
        }

        /// <summary>Крошки от пилы: вверх и в стороны из места распила. Только вид.</summary>
        private void CrumbBurst(int cell)
        {
            if (Application.isBatchMode || _fill == null) return;
            EnsureGrid();
            if (cell < 0 || cell >= _gx * _gz) return;
            Vector3 c = CellWorld(cell, 0.95f);
            Vector3 sz = _cakeSize.sqrMagnitude > 1e-8f ? _cakeSize : _fillBounds.size;
            float s = Mathf.Clamp(Mathf.Min(sz.x / _gx, sz.z / _gz) * Mathf.Abs(_fill.lossyScale.x), 0.03f, 0.8f);
            Color crumb = SpongeMesh.CutCrumb, crust = SpongeMesh.SideCrust;
            int n = Mathf.Clamp(Mathf.RoundToInt(14f + s * 30f), 14, 40);
            float spd = Mathf.Sqrt(s / 0.3f);
            for (int i = 0; i < n; i++)
            {
                Vector2 h = Random.insideUnitCircle;
                Vector3 vel = new Vector3(h.x * 1.4f, Random.Range(1.6f, 3.4f), h.y * 1.4f) * spd;
                Color col = (Random.value < 0.75f ? crumb : crust) * Random.Range(0.85f, 1.1f); col.a = 1f;
                CCPowderFx.Crumb(c + new Vector3(h.x, 0f, h.y) * s * 0.3f, vel, col, s * Random.Range(0.04f, 0.1f), Random.value < 0.6f);
            }
        }

        // ───────────────────────── ПОСТАНОВКА (ЛКМ) ─────────────────────────

        public override void ServerPrimaryUse(UseContext ctx)
        {
            if (ctx.Hand == null) return;
            if (!GrillPlacement.Find(ctx, transform, _placeReach, out GrillPlacement.Spot spot))
            {
                if (_log) Debug.Log($"[Опалубка] «{name}»: ЛКМ — некуда поставить: смотри на решётку духовки или на ровную поверхность ближе {_placeReach:0.#} м", this);
                return;
            }
            Quaternion rot = GrillPlacement.FitRot(transform, GrillPlacement.UprightRot(transform, _uprightRot, spot.Frame), spot);
            ctx.Hand.ServerDrop(Vector3.zero, 0f);
            GrillPlacement.PlaceAt(transform, GetComponent<Rigidbody>(), rot, spot, _placeLift);
            if (_log) Debug.Log(spot.Oven != null ? $"[Опалубка] «{name}» стоит ровно на решётке «{spot.Oven.name}»" : $"[Опалубка] «{name}» поставлена ровно", this);
        }

        [ContextMenu("Запомнить позу: так опалубка стоит ровно")]
        private void CaptureUpright()
        {
            _uprightRot = transform.rotation; _hasUpright = true;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            Debug.Log($"[Опалубка] «{name}»: поза запомнена", this);
        }

        private void OnGUI()
        {
            DrawBakeHud();                                                          // v23.39: прогресс и температура выпечки
            if (!_placeHint || !base.IsOwner) return;
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null || !rb.isKinematic) return;                 // не в руках
            GUI.Label(new Rect(Screen.width * 0.5f - 120f, Screen.height * 0.62f, 240f, 24f), "ЛКМ — поставить ровно");
        }

        /// <summary>v23.39: от дна пласта (Fill) до верхнего края бортиков опалубки — в осях Fill. 0 — не понять.</summary>
        private float RimDepth()
        {
            if (_fill == null) return 0f;
            float top = float.MinValue; bool any = false;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.transform.IsChildOf(_fill) || !(r is MeshRenderer)) continue;
                bool skip = false;
                for (Transform p = r.transform; p != null && p != transform; p = p.parent)
                    if (p.name.Contains("LiquidAnchor") || p.name.Contains("(auto)")) { skip = true; break; }
                if (skip) continue;
                Bounds b = r.bounds;
                Vector3 lp = _fill.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
                if (!any || lp.y > top) { top = lp.y; any = true; }
            }
            if (!any) return 0f;
            float d = top - _fillBounds.min.y;
            return d > 1e-4f ? d : 0f;
        }

        // ───────────────────────── ПАНЕЛЬ ВЫПЕЧКИ (v23.39) ─────────────────────────

        private readonly float[] _tHist = new float[90];
        private int _tHistN, _tHistAt;
        private float _tHistNext, _hudOvenAt;
        private BakeOven _hudOven;
        private static Texture2D _px;

        private void HudSample()
        {
            if (Time.time < _hudOvenAt) return;
            _hudOvenAt = Time.time + 0.5f;
            _hudOven = FindOven();
            float t = _hudOven != null ? _hudOven.TemperatureC : 20f;
            _tHist[_tHistAt] = t;
            _tHistAt = (_tHistAt + 1) % _tHist.Length;
            if (_tHistN < _tHist.Length) _tHistN++;
        }

        private void DrawBakeHud()
        {
            if (!_bakeHud) return;
            Camera cam = Camera.main;
            if (cam == null || (cam.transform.position - transform.position).sqrMagnitude > 64f) return;
            TrayState st = State;
            bool batterIn = st == TrayState.Empty && _cw != null && _batter != null && _cw.CountOf(_batter.Id) > 0;
            if (st == TrayState.Empty && !batterIn) return;
            HudSample();
            BakeOven oven = _hudOven;
            if (st == TrayState.Empty && oven == null) return;
            if (_px == null) { _px = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave }; _px.SetPixel(0, 0, Color.white); _px.Apply(); }

            float k = st == TrayState.Baked ? 1f : Bake01;
            float t = oven != null ? oven.TemperatureC : 20f;
            float speed = oven != null && t >= _bakeFromC ? Mathf.Clamp(t / 180f, 0.6f, 1.5f) * oven.BakeSpeed : 0f;
            string line2;
            if (st == TrayState.Baked) line2 = $"ГОТОВО — режь пилой в опалубке ({CellsLeft} кубиков)";
            else if (oven == null) line2 = "не в духовке — пауза";
            else if (t < _bakeFromC) line2 = $"холодно: {t:0} °C, печётся с {_bakeFromC:0} °C — раздуй огонь";
            else
            {
                float left = (1f - k) * _bakeSeconds / Mathf.Max(0.05f, speed);
                line2 = st == TrayState.Empty ? "начинается…" : $"осталось ≈ {left:0} с";
                if (oven.DoorOpen) line2 += k < _fallBefore ? " · ДВЕРЦА ОТКРЫТА — осядет!" : " · дверца открыта — медленнее";
            }

            float W = 300f, H = 128f, x = Screen.width - W - 16f, y = 80f;
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(new Rect(x, y, W, H), _px); GUI.color = old;
            GUI.Label(new Rect(x + 8f, y + 4f, W - 16f, 20f), $"<b>Бисквит</b>  {k * 100f:0}%{(_fallen.Value ? " (осел)" : "")}   {t:0} °C");
            GUI.Label(new Rect(x + 8f, y + 22f, W - 16f, 20f), line2);
            // полоса выпечки
            Rect bar = new Rect(x + 8f, y + 44f, W - 16f, 10f);
            GUI.color = new Color(1f, 1f, 1f, 0.15f); GUI.DrawTexture(bar, _px);
            GUI.color = Color.Lerp(new Color(0.95f, 0.8f, 0.45f), new Color(0.55f, 0.32f, 0.16f), k);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * k, bar.height), _px);
            GUI.color = new Color(1f, 0.3f, 0.2f, 0.9f);
            GUI.DrawTexture(new Rect(bar.x + bar.width * _fallBefore, bar.y - 2f, 2f, bar.height + 4f), _px);   // до этой метки не открывать
            // график температуры (последние 45 с): 0…250 °C, линия «печётся с»
            Rect g = new Rect(x + 8f, y + 60f, W - 16f, H - 68f);
            GUI.color = new Color(1f, 1f, 1f, 0.08f); GUI.DrawTexture(g, _px);
            float yFrom = g.yMax - g.height * Mathf.Clamp01(_bakeFromC / 250f);
            GUI.color = new Color(1f, 0.85f, 0.3f, 0.7f); GUI.DrawTexture(new Rect(g.x, yFrom, g.width, 1f), _px);
            float y180 = g.yMax - g.height * (180f / 250f);
            GUI.color = new Color(0.4f, 1f, 0.5f, 0.35f); GUI.DrawTexture(new Rect(g.x, y180, g.width, 1f), _px);
            int n = _tHistN;
            for (int i = 0; i < n; i++)
            {
                float tv = _tHist[(_tHistAt - n + i + _tHist.Length * 2) % _tHist.Length];
                float h = g.height * Mathf.Clamp01(tv / 250f);
                float bx = g.x + g.width * i / (float)_tHist.Length;
                GUI.color = tv >= _bakeFromC ? new Color(1f, 0.55f, 0.25f, 0.95f) : new Color(0.5f, 0.75f, 1f, 0.95f);
                GUI.DrawTexture(new Rect(bx, g.yMax - h, Mathf.Max(1f, g.width / _tHist.Length - 1f), Mathf.Max(1f, h)), _px);
            }
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(g.x + 2f, yFrom - 16f, 140f, 16f), $"<size=10>печётся с {_bakeFromC:0} °C</size>");
            GUI.color = old;
        }

        // ───────────────────────── ВИД ─────────────────────────

        private void ApplyLook()
        {
            if (_fill == null || _fillR == null) return;
            TrayState st = State;
            bool show = st != TrayState.Empty;
            foreach (Renderer r in _fillR) if (r != null) r.enabled = show;
            // v23.50: только что испёкся — пар как из печи (стихает за минуту)
            if (st == TrayState.Baked && !_steamMarked)
            {
                _steamMarked = true;
                SpongeSteam.MarkBaked();
                if (_fillR.Length > 0 && _fillR[0] != null && _fillR[0].GetComponent<SpongeSteam>() == null) _fillR[0].gameObject.AddComponent<SpongeSteam>();
            }
            else if (st != TrayState.Baked) _steamMarked = false;
            if (!show) { _cakeKey = -1; return; }

            float k = st == TrayState.Baked ? 1f : Bake01;
            float top = _fallen.Value ? _fallenHeight : 1f;
            // поднимается быстрее в начале, к концу — почти не растёт (а осевший — опускается к своей высоте)
            float rise = BakeColor.Smooth(Mathf.Clamp01(k / 0.7f));
            if (_cakeMesh != null)
            {
                // v23.37: бисквит-меш: поднимается ВЫШЕ опалубки (Baked Rise), купол, корочка темнеет, трещинки
                int key = Mathf.RoundToInt(k * 200f) + (_fallen.Value ? 1000 : 0) + _cut.Count * 2000;   // v23.51: + отпиленные
                if (key == _cakeKey) return;
                _cakeKey = key;
                _fill.localScale = _fillScale;
                _fill.localPosition = _fillPos;
                // v23.39: сырое — уровень теста; готовое — выше КРАЯ опалубки (Baked Rise от глубины до бортика)
                float rawH = _fillBounds.size.y * _rawHeight;
                float deep = Mathf.Max(_fillBounds.size.y, _rimDepth);
                float bakedH = _fallen.Value ? deep * _fallenHeight : deep * _bakedRise;
                Vector3 sz = new Vector3(_fillBounds.size.x, Mathf.Lerp(rawH, bakedH, rise), _fillBounds.size.z);
                float domeK = _dome * (_fallen.Value ? 0.35f : 1f) * rise;
                _cakeSize = sz;
                RefreshPresent();
                bool cutAny = st == TrayState.Baked && _cut.Count > 0;
                SpongeMesh.BuildCake(_cakeMesh, sz, domeK, Mathf.Clamp01(k / 0.85f), _batterColor, _gx, _gz, cutAny ? _present : null);
                Vector3[] cv = _cakeMesh.vertices;
                Vector3 off = new Vector3(_fillBounds.center.x, _fillBounds.min.y, _fillBounds.center.z);
                for (int i = 0; i < cv.Length; i++) cv[i] += off;
                _cakeMesh.vertices = cv;
                _cakeMesh.RecalculateBounds();
                foreach (Renderer r in _fillR) if (r != null) BakeColor.Tint(r, ref _mpb, null);
                if (_jiggle != null) _jiggle.Rebase();
                return;
            }
            float h = Mathf.Lerp(_rawHeight, top, rise);
            Vector3 s = new Vector3(_fillScale.x, _fillScale.y * h, _fillScale.z);
            _fill.localScale = s;
            // низ пласта — у дна опалубки, растёт вверх
            _fill.localPosition = _fillPos + _fill.localRotation * new Vector3(0f, _fillMinY * (_fillScale.y - s.y), 0f);

            bool baked = st == TrayState.Baked;
            foreach (Renderer r in _fillR)
            {
                if (r == null) continue;
                if (baked) { BakeColor.Tint(r, ref _mpb, null); continue; }
                BakeColor.Tint(r, ref _mpb, BakeColor.Ramp(_batterColor, _midColor, _crustEnd, k));
            }
        }
    }
}
