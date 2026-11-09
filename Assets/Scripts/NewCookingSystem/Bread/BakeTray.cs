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
    ///   4. Готово — F по опалубке: пласт бисквита выскакивает отдельным предметом (Slab Prefab),
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
    public class BakeTray : PickupItem
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

        private Cookware _cw;
        private Renderer[] _fillR;
        private Material _fillMat;
        private Vector3 _fillScale, _fillPos;
        private float _fillMinY;
        private Color _crustEnd;
        private MaterialPropertyBlock _mpb;
        private float _progress, _hintAt;
        private int _lastQuarter = -1;
        private bool _heating;

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
            }
            Color bc = BakeColor.BaseOf(_fillMat);
            _crustEnd = _fillMat != null && bc.r + bc.g + bc.b < 2.7f ? bc : _crustColor;
            if (!_hasUpright) { _uprightRot = transform.rotation; _hasUpright = true; }
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
            ApplyLook();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _state.OnChange -= OnChanged;
            _bake.OnChange -= OnBake;
            _fallen.OnChange -= OnFallen;
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
                if (_log) Debug.Log($"[Опалубка] «{name}»: БИСКВИТ ГОТОВ{(_fallen.Value ? " (осевший)" : "")} — F по опалубке, чтобы достать пласт", this);
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
            if (!h.collider.transform.IsChildOf(transform)) return;
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
            _state.Value = (byte)TrayState.Baked;
            Debug.Log($"[Опалубка] «{name}»: тест — ИСПЕЧЕНО сразу ({_portions.Value} порц.). F по опалубке — достать пласт", this);
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

        /// <summary>F: готовый бисквит — достать пласт, иначе — взять опалубку.</summary>
        public override void ServerInteract(PlayerInteractor interactor)
        {
            if (State == TrayState.Baked) { ServerPopSlab(); return; }
            base.ServerInteract(interactor);
        }

        [Server]
        private void ServerPopSlab()
        {
            int portions = Mathf.Max(1, _portions.Value / Mathf.Max(1, _batterPerPortion));
            bool fallen = _fallen.Value;
            _state.Value = (byte)TrayState.Empty;
            _bake.Value = 0; _fallen.Value = false; _portions.Value = 0;
            if (_slabPrefab == null) { Debug.LogError($"[Опалубка] «{name}»: не задан Slab Prefab — бисквит пропал", this); return; }
            Transform at = _fill != null ? _fill : transform;
            Quaternion rot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            GameObject go = Instantiate(_slabPrefab, at.position + Vector3.up * 0.06f, rot);
            FoodPiece fp = go.GetComponent<FoodPiece>();
            if (fp != null && _result != null) fp.ServerPreset(_result, FoodState.Baked, 100, 100, 100);
            if (fallen) go.transform.localScale = Vector3.Scale(go.transform.localScale, new Vector3(1f, _fallenHeight, 1f));
            base.Spawn(go);
            if (fp != null && go.GetComponent<ICuttable>() == null) fp.ServerSetCount(portions);   // пласт из кубиков считает сам
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.AddForce(Vector3.up * 1.8f, ForceMode.VelocityChange);
            if (_log) Debug.Log($"[Опалубка] «{name}»: пласт «{(_result != null ? _result.Title : go.name)}» достали{(fallen ? " (осевший)" : "")} — режь пилой на квадраты", this);
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
            if (!_placeHint || !base.IsOwner) return;
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null || !rb.isKinematic) return;                 // не в руках
            GUI.Label(new Rect(Screen.width * 0.5f - 120f, Screen.height * 0.62f, 240f, 24f), "ЛКМ — поставить ровно");
        }

        // ───────────────────────── ВИД ─────────────────────────

        private void ApplyLook()
        {
            if (_fill == null || _fillR == null) return;
            TrayState st = State;
            bool show = st != TrayState.Empty;
            foreach (Renderer r in _fillR) if (r != null) r.enabled = show;
            if (!show) return;

            float k = st == TrayState.Baked ? 1f : Bake01;
            float top = _fallen.Value ? _fallenHeight : 1f;
            // поднимается быстрее в начале, к концу — почти не растёт (а осевший — опускается к своей высоте)
            float rise = BakeColor.Smooth(Mathf.Clamp01(k / 0.7f));
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
