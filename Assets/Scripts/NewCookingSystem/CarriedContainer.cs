using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ТАРА В РУКАХ — ведро, банка, кастрюля. Замена PickupItem для тары.
    ///
    /// ═══ КАК ВИСИТ ═══
    /// v3: движение сглажено (не дёргается), интерполяция Rigidbody в руке
    /// выключается (она перетирала позу), шаги дают ритмичные волны в таре,
    /// при выливании ведро держится ровнее — струя стабильная.
    ///
    /// Не приклеена к камере: смотришь вверх-вниз — ведро остаётся у пояса и
    /// стоит ровно. Поворачивается только вместе с игроком (по кругу).
    /// При ходьбе и рывках — качается, как настоящее ведро на ручке: наклон
    /// от ускорения + покачивание в такт шагам. Полное ведро при резком
    /// движении выплёскивается (это делает CookwareSpill — сам по наклону).
    ///
    /// v4: ПОЗА СЧИТАЕТСЯ В LateUpdate — после того, как игрок сдвинулся (раньше рука и
    /// ходьба шли в Update в случайном порядке — ведро дёргалось через кадр); положение
    /// игрока дополнительно сглаживается. ЗА РУЧКУ: если на ведре есть BucketHandle —
    /// ведро висит под точкой хвата (верх дужки) и качается маятником, а не «в двух руках».
    ///
    /// ═══ ЛКМ / ПКМ (LMB Pour ✔, по умолчанию) ═══
    ///   • ЛКМ (держать) — ВЫЛИВАТЬ: смотришь на тару — ведро наклоняется к ней и
    ///     переливает (струя с края, куда упала — туда и попало); смотришь в пол — на пол (лужа).
    ///     Рядом точка стыковки (костёр) — ЛКМ ставит тару туда, а не льёт.
    ///   • ПКМ (держать) — ЗАЧЕРПНУТЬ из тары, на которую смотришь: ведро опускается в неё
    ///     и набирается до полного примерно за Scoop Fill Seconds.
    /// LMB Pour ✖ — по-старому: ПКМ льёт, если в ведре что-то есть, и черпает, если пустое.
    ///
    /// ═══ ТРЕБУЕТ ═══
    /// Вместо Pickup Item на корне тары. Рядом: Cookware, CookwareSpill.
    /// </summary>
    [DefaultExecutionOrder(-30)]      // поза ведра — раньше жидкости (CookwareLiquidVisual) и ручки (BucketHandle)
    public sealed class CarriedContainer : PickupItem
    {
        public override bool UseHeldBlocking => false;     // v17.3: своя логика столкновений/позы
        public enum Action : byte { None = 0, Pour = 1, Scoop = 2 }

        [Header("Как висит в руке")]
        [Tooltip("Где висит ведро относительно ног игрока, м: вправо, вверх, вперёд")]
        [SerializeField] private Vector3 _carryOffset = new Vector3(0.35f, 0.85f, 0.45f);
        [Tooltip("Дополнительный поворот модели в руке, градусы")]
        [SerializeField] private Vector3 _carryEuler = Vector3.zero;

        [Header("Качание")]
        [Tooltip("Насколько сильно раскачивается от рывков")]
        [Range(0f, 3f)][SerializeField] private float _swayGain = 1f;
        [SerializeField] private float _swayStiffness = 30f;
        [SerializeField] private float _swayDamping = 4f;
        [Tooltip("Наибольший наклон от качания, градусы")]
        [Range(0f, 45f)][SerializeField] private float _maxSway = 22f;
        [Tooltip("Покачивание в такт шагам, градусы")]
        [Range(0f, 15f)][SerializeField] private float _walkBob = 5f;
        [Tooltip("Шагов в секунду при обычной ходьбе")]
        [SerializeField] private float _stepFrequency = 1.8f;
        [Tooltip("Подпрыгивание ведра в такт шагам, м — от него по молоку бегут волны")]
        [Range(0f, 0.05f)][SerializeField] private float _walkBounce = 0.014f;
        [Tooltip("Сглаживание движения игрока, с. Убирает дёрганье ведра и молока.")]
        [Range(0f, 0.15f)][SerializeField] private float _motionSmoothing = 0.05f;
        [Tooltip("При выливании ведро держится ровнее (0 — качается как обычно, 1 — не качается)")]
        [Range(0f, 1f)][SerializeField] private float _pourSteady = 0.75f;

        [Header("Стабильность и хват за ручку (v4)")]
        [Tooltip("Сглаживание положения игрока, с — ведро не дрожит от рывков кадров и сети")]
        [Range(0f, 0.12f)][SerializeField] private float _followTime = 0.035f;
        [Tooltip("Сглаживание поворота игрока, с")]
        [Range(0f, 0.12f)][SerializeField] private float _yawTime = 0.04f;
        [Tooltip("Держать за ручку (нужен BucketHandle на ведре): ведро висит под рукой и качается маятником")]
        [SerializeField] private bool _holdByHandle = true;
        [Tooltip("Где рука держит ручку относительно ног игрока, м (вправо, вверх, вперёд). " +
                 "Нули — само: ведро остаётся там же, где было при Carry Offset")]
        [SerializeField] private Vector3 _gripOffset = Vector3.zero;
        [Tooltip("Раскачка ведра на ручке сильнее обычной (маятник), множитель")]
        [Range(0.5f, 3f)][SerializeField] private float _handleSwayGain = 1.4f;

        [Header("Выливать (ЛКМ) / черпать (ПКМ)")]
        [SerializeField] private float _range = 2.6f;
        [SerializeField] private LayerMask _cookwareMask = ~0;
        [Tooltip("Наклон при переливании, градусы")]
        [Range(20f, 130f)][SerializeField] private float _pourTilt = 80f;
        [Tooltip("Насколько ведро тянется к таре при переливании, м")]
        [SerializeField] private float _pourLean = 0.25f;
        [Tooltip("Порций в секунду при зачерпывании (не меньше)")]
        [SerializeField] private float _scoopRate = 4f;
        [Tooltip("За сколько секунд набирается ПОЛНОЕ ведро при зачерпывании (ПКМ). 0.15 — почти мгновенно")]
        [Min(0.02f)][SerializeField] private float _scoopTime = 0.15f;
        [Tooltip("За сколько секунд выливается ПОЛНАЯ тара (ЛКМ)")]
        [Min(0.2f)][SerializeField] private float _pourTime = 2f;
        [Tooltip("ЛКМ — выливать, ПКМ — черпать. Выкл — по-старому: всё на ПКМ")]
        [SerializeField] private bool _lmbPour = true;
        [Tooltip("Насколько ведро опускается в тару при зачерпывании, м")]
        [SerializeField] private float _scoopDip = 0.35f;
        [Tooltip("Наклон ведра при зачерпывании, градусы (больше 30 — вода начнёт выливаться обратно)")]
        [Range(0f, 35f)][SerializeField] private float _scoopTilt = 15f;
        [SerializeField] private float _poseSpeed = 3f;

        [Header("Тест: поставить на точку стыковки (ЛКМ)")]
        [Tooltip("Держишь тару, рядом кострище/станок с точкой стыковки — ЛКМ ставит тару туда " +
                 "(магнит сам выровняет). Для тестов; выключи — ЛКМ ничего не делает.")]
        [SerializeField] private bool _placeOnDock = true;
        [Tooltip("Насколько далеко от игрока может быть точка, м")]
        [SerializeField] private float _placeReach = 3f;
        [Tooltip("Подсказка на экране, когда можно поставить")]
        [SerializeField] private bool _showPlaceHint = true;

        [SerializeField] private bool _log = true;

        private CookwareDock[] _docks;
        private float _dockScan;

        private readonly SyncVar<byte> _action = new SyncVar<byte>(0);
        private readonly SyncVar<NetworkObject> _target = new SyncVar<NetworkObject>();

        private Cookware _self;
        private bool _rmbWas, _lmbWas;
        private Action _lastSent;
        private NetworkObject _lastTarget;
        private float _scoopAcc;
        private float _actionSince;            // сервер: когда началось текущее действие
        private int _scoopedTotal;             // сервер: сколько зачерпнули за это нажатие
        private string _scoopFrom;
        private float _scoopWhyAt;
        private readonly RaycastHit[] _hits = new RaycastHit[32];

        // поза
        private Vector2 _sway, _swayVel;
        private Vector3 _lastPos, _lastVel, _velF;
        private bool _tracked;
        private float _poseT, _stepPhase, _walk01;

        // интерполяция Rigidbody в руке (иначе она перетирает позу каждый кадр — ведро дёргается)
        private BucketHandle _handle;
        private Transform _poseAnchor;
        private int _poseFrame = -10;
        private Vector3 _basePosF, _baseVelF;
        private float _yawF, _yawVelF;
        private bool _baseTracked;
        private RigidbodyInterpolation _savedInterp;
        private bool _interpSaved, _clientKinematic;
        private float _heldSeen = -10f;

        public Action CurrentAction => (Action)_action.Value;
        /// <summary>Порций в секунду при выливании ЛКМ (полная тара — за Pour Time). CookwareSpill льёт не медленнее.</summary>
        public float PourRate => (_self != null && _self.Def != null ? _self.Def.Capacity : 30) / Mathf.Max(0.2f, _pourTime);

        /// <summary>
        /// Тара по сетевому объекту цели. Ковш экскаватора: NetworkObject — на машине,
        /// а Cookware — на дочернем ковше, поэтому ищем и в детях (ближайшую к ведру).
        /// </summary>
        private Cookware TargetCookware()
        {
            NetworkObject no = _target.Value;
            if (no == null) { _tcFor = null; _tc = null; return null; }
            if (no == _tcFor && _tc != null) return _tc;
            _tcFor = no;
            _tc = TargetCookwareFor(no);
            return _tc;
        }
        private Cookware TargetCookwareFor(NetworkObject no)
        {
            if (no == null) return null;
            Cookware c = no.GetComponent<Cookware>();
            if (c != null) return c;
            float best = float.MaxValue;
            foreach (Cookware x in no.GetComponentsInChildren<Cookware>(true))
            {
                if (x == null || x == _self) continue;
                float d = (x.MouthPoint - transform.position).sqrMagnitude;
                if (d < best) { best = d; c = x; }
            }
            return c;
        }
        private NetworkObject _tcFor;
        private Cookware _tc;

        protected override void Awake()
        {
            base.Awake();
            _self = GetComponent<Cookware>();
            _handle = GetComponent<BucketHandle>();
        }

        // ───────────────────────── ВЛАДЕЛЕЦ ─────────────────────────

        private void Update()
        {
            if (base.IsOwner) OwnerInput();
            if (base.IsServerInitialized) ServerScoop(Time.deltaTime);
            if (_interpSaved && Time.time - _heldSeen > 0.25f) RestoreBody();
        }

        /// <summary>В руке: интерполяция Rigidbody выключена (иначе физика перетирает позу и ведро дрожит).</summary>
        private void HoldBody()
        {
            _heldSeen = Time.time;
            if (_rb == null) return;
            if (!_interpSaved) { _savedInterp = _rb.interpolation; _interpSaved = true; }
            if (_rb.interpolation != RigidbodyInterpolation.None) _rb.interpolation = RigidbodyInterpolation.None;
            // Сервер сам делает тару кинематической; у чистого клиента-владельца — делаем мы.
            if (!base.IsServerInitialized && !_rb.isKinematic) { _rb.isKinematic = true; _clientKinematic = true; }
        }

        private void RestoreBody()
        {
            _interpSaved = false;
            _tracked = false;
            if (_rb == null) return;
            _rb.interpolation = _savedInterp;
            if (_clientKinematic) { _rb.isKinematic = false; _clientKinematic = false; }
        }

        private void OwnerInput()
        {
            bool rmb = Input.GetMouseButton(1);
            bool lmb = Input.GetMouseButton(0);
            Action want = Action.None;
            NetworkObject target = null;
            if (_self != null)
            {
                if (_lmbPour)
                {
                    if (lmb && !_self.IsEmpty && !NearDockNow())
                    {
                        Cookware aimed = Aimed();
                        want = Action.Pour; target = aimed != null ? aimed.NetworkObject : null;
                    }
                    else if (rmb)
                    {
                        Cookware aimed = Aimed();
                        if (aimed != null && !aimed.IsEmpty && !_self.IsFull) { want = Action.Scoop; target = aimed.NetworkObject; }
                        else if (!_rmbWas && _log)
                            Debug.Log($"[Ведро] «{name}» ПКМ — не черпаю: " +
                                      (aimed == null ? $"под прицелом нет тары (луч {_range:0.#} м от центра экрана — подойди ближе и смотри на ковш)"
                                       : aimed.IsEmpty ? $"«{aimed.name}» пустая"
                                       : $"ведро уже полное ({_self.TotalCount}/{_self.Capacity} порц.)"), this);
                    }
                }
                else if (rmb)
                {
                    Cookware aimed = Aimed();
                    bool haveStuff = !_self.IsEmpty;
                    if (haveStuff) { want = Action.Pour; target = aimed != null ? aimed.NetworkObject : null; }
                    else if (aimed != null && !aimed.IsEmpty) { want = Action.Scoop; target = aimed.NetworkObject; }
                }
            }
            if (want != _lastSent || target != _lastTarget || rmb != _rmbWas || lmb != _lmbWas)
            {
                _lastSent = want; _lastTarget = target; _rmbWas = rmb; _lmbWas = lmb;
                CmdAction((byte)want, target);
            }
        }

        /// <summary>
        /// Рядом свободная точка стыковки — ЛКМ ставит тару, а не льёт. НО смотришь на другую тару
        /// (бетономешалка, опалубка, ведро) — льём в неё: точка стыковки рядом не мешает.
        /// </summary>
        private bool NearDockNow()
        {
            if (!_placeOnDock) return false;
            Camera cam = Camera.main;
            if (cam == null || NearestDock(cam.transform.position) == null) return false;
            return Aimed() == null;
        }

        /// <summary>Тара под прицелом (сервер, по лучу из UseContext).</summary>
        private Cookware AimedFrom(Vector3 origin, Vector3 dir)
        {
            if (dir.sqrMagnitude < 1e-6f) return null;
            int n = Physics.RaycastNonAlloc(new Ray(origin, dir.normalized), _hits, _range, _cookwareMask, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            Cookware found = null;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].transform.IsChildOf(transform) || _hits[i].distance >= best) continue;
                Cookware c = _hits[i].collider.GetComponentInParent<Cookware>();
                if (c == null || c == _self) continue;
                best = _hits[i].distance; found = c;
            }
            return found;
        }

        private Cookware Aimed()
        {
            Camera cam = Camera.main;
            if (cam == null) return null;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int n = Physics.RaycastNonAlloc(ray, _hits, _range, _cookwareMask, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            Cookware found = null;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].transform.IsChildOf(transform) || _hits[i].distance >= best) continue;
                Cookware c = _hits[i].collider.GetComponentInParent<Cookware>();
                if (c == null || c == _self) continue;
                best = _hits[i].distance; found = c;
            }
            if (found == null)
            {
                // тара на «чужом» слое (бочка бетономешалки, ковш экскаватора) — второй луч по всем слоям
                n = Physics.RaycastNonAlloc(ray, _hits, _range, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < n; i++)
                {
                    if (_hits[i].transform.IsChildOf(transform) || _hits[i].distance >= best) continue;
                    Cookware c = _hits[i].collider.GetComponentInParent<Cookware>();
                    if (c == null || c == _self) continue;
                    best = _hits[i].distance; found = c;
                }
            }
            return found;
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdAction(byte action, NetworkObject target)
        {
            if (_action.Value != action || _target.Value != target)
            {
                ScoopSummary();
                _actionSince = Time.time;
                if (_log && (Action)action == Action.Scoop && target != null)
                    Debug.Log($"[Ведро] «{name}»: черпаю из «{target.name}» → тара «{(TargetCookwareFor(target) is Cookware tc ? tc.name : "НЕТ Cookware")}» (в ведре {(_self != null ? _self.TotalCount : 0)}/{(_self != null ? _self.Capacity : 0)} порц.)", this);
            }
            if (_action.Value != action) _action.Value = action;
            if (_target.Value != target) _target.Value = target;
            _scoopAcc = 0f;
        }

        /// <summary>Итог зачерпывания в консоль — один раз, когда отпустили ПКМ.</summary>
        private void ScoopSummary()
        {
            if (_scoopedTotal <= 0) return;
            if (_log)
                Debug.Log($"[Ведро] «{name}»: зачерпнуло {_scoopedTotal} порц. ({FoodUnits.Describe(_scoopedTotal, CookUnit.Bucket)}) из «{_scoopFrom}» — " +
                          $"в ведре {(_self != null ? _self.TotalCount : 0)}/{(_self != null ? _self.Capacity : 0)} порц.", this);
            _scoopedTotal = 0;
        }

        public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
        {
            ScoopSummary();
            _action.Value = 0;
            _target.Value = null;
            base.ServerOnDrop(hand, tossDirection, force);
        }

        // ───────────────────────── ТЕСТ: ПОСТАВИТЬ НА КОСТРИЩЕ ─────────────────────────

        /// <summary>Ближайшая свободная точка стыковки, куда эту тару можно поставить.</summary>
        private CookwareDock NearestDock(Vector3 from)
        {
            if (_docks == null || Time.time >= _dockScan)
            {
                _dockScan = Time.time + 2f;
                _docks = FindObjectsByType<CookwareDock>(FindObjectsSortMode.None);
            }
            CookwareDock best = null;
            float bd = _placeReach;
            foreach (CookwareDock d in _docks)
            {
                if (d == null || !d.IsFree || _self == null || !d.Accepts(_self)) continue;
                Vector3 v = d.transform.position - from;
                float h = new Vector2(v.x, v.z).magnitude;
                if (h < bd) { bd = h; best = d; }
            }
            return best;
        }

        // ЛКМ с тарой в руках — поставить на точку стыковки.
        public override void ServerPrimaryUse(UseContext ctx)
        {
            if (!_placeOnDock || ctx.Hand == null) return;
            CookwareDock dock = NearestDock(ctx.Origin);
            if (dock == null) return;
            // смотрим на тару — значит льём в неё, а не ставим ведро на подставку
            Cookware aimed = AimedFrom(ctx.Origin, ctx.Direction);
            if (aimed != null)
            {
                if (_log) Debug.Log($"[Тара] «{name}»: смотришь на «{aimed.name}» — льём туда, на «{dock.name}» не ставлю", this);
                return;
            }

            ctx.Hand.ServerDrop(Vector3.zero, 0f);
            // Точка сама ставит ровно: дном вниз, на подставку, и фиксирует (большая тара — тоже).
            dock.ServerPlace(_self);
            if (_log) Debug.Log($"[Тара] «{name}» поставлена на «{dock.name}»", this);
        }

        private void OnGUI()
        {
            if (!_placeOnDock || !_showPlaceHint || !base.IsOwner) return;
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null || !rb.isKinematic) return;                 // не в руках
            Camera cam = Camera.main;
            if (cam == null || NearestDock(cam.transform.position) == null) return;
            Cookware aimed = Aimed();
            GUI.Label(new Rect(Screen.width * 0.5f - 160f, Screen.height * 0.62f, 320f, 24f),
                      aimed != null ? $"ЛКМ — вылить в «{aimed.name}»" : "ЛКМ — поставить на место");
        }

        // ───────────────────────── СЕРВЕР: ЗАЧЕРПНУТЬ ─────────────────────────

        private void ServerScoop(float dt)
        {
            // Время позы считает сервер сам: у клиента-владельца поза крутится только на его машине.
            if ((Action)_action.Value != Action.Scoop || _self == null || _target.Value == null) return;
            if (Time.time - _actionSince < 0.08f) return;
            Cookware from = TargetCookware();
            string why = null;
            if (from == null) why = "цель не тара";
            else if (from.IsEmpty) why = $"«{from.name}» опустела";
            else if (_self.IsFull) why = null;
            else if (from == _self) why = "цель — само ведро";
            // до горловины тары, а не до корня (ковш экскаватора: корень сетевого объекта — за метры)
            else if ((from.MouthPoint - transform.position).sqrMagnitude > (_range + 2f) * (_range + 2f)) why = $"«{from.name}» слишком далеко";
            if (why != null)
            {
                if (_log && Time.time >= _scoopWhyAt) { _scoopWhyAt = Time.time + 2f; Debug.Log($"[Ведро] «{name}»: не черпаю — {why}", this); }
                return;
            }
            if (_self.IsFull) return;

            float rate = Mathf.Max(_scoopRate, (_self.Def != null ? _self.Def.Capacity : 30) / _scoopTime);
            _scoopAcc += dt * rate;
            while (_scoopAcc >= 1f)
            {
                _scoopAcc -= 1f;
                if (!TakeOne(from, out ushort product, out FoodState state, out byte cook)) break;
                int added = _self.ServerAddContent(product, state, 1, cook, false);
                if (added < 1) { from.ServerAddContent(product, state, 1, cook, false); break; }
                _scoopedTotal++;
                _scoopFrom = from.name;
                from.ServerAgitate(0.6f);
                if (_self.IsFull) { ScoopSummary(); if (_log) Debug.Log($"[Ведро] «{name}»: ПОЛНОЕ", this); break; }
            }
        }

        /// <summary>
        /// Одна порция из тары: сперва ЖИДКОСТЬ сверху (вода, соус — то, что ведро черпает),
        /// только если жидкого нет совсем — верхнее что есть (кусочки, кучка).
        /// </summary>
        private static bool TakeOne(Cookware from, out ushort product, out FoodState state, out byte cook)
        {
            product = 0; state = FoodState.None; cook = 0;
            FoodDatabase db = FoodDatabase.Instance;
            CookwareLiquidVisual lv = from.GetComponentInChildren<CookwareLiquidVisual>(true);
            for (int i = from.ViewCount - 1; i >= 0; i--)
            {
                BagView v = from.GetView(i);
                if (v.Count == 0) continue;
                bool liquid;
                if (lv != null && lv.TryGetRole(v.Product, out CookwareLiquidVisual.Role role))
                    liquid = role != CookwareLiquidVisual.Role.Floating && role != CookwareLiquidVisual.Role.Paste;
                else liquid = FoodHeapLook.IsLiquid(db != null ? db.Get(v.Product) : null, v.State);
                if (!liquid) continue;
                if (from.ServerTake(v.Product, 1) <= 0) continue;
                product = v.Product; state = v.State; cook = v.Cook;
                return true;
            }
            return from.ServerTakeTop(1, out product, out state, out cook) > 0;
        }

        // ───────────────────────── ПОЗА (у всех) ─────────────────────────

        /// <summary>
        /// Рука зовёт в Update (порядок с ходьбой случайный) — запоминаем и ставим позу в LateUpdate,
        /// когда игрок уже сдвинулся. Так ведро не дёргается через кадр.
        /// </summary>
        public override void UpdateHeldPose(Transform handAnchor)
        {
            HoldBody();
            _poseAnchor = handAnchor;
            _poseFrame = Time.frameCount;
        }

        private void LateUpdate()
        {
            if (_poseAnchor == null || Time.frameCount - _poseFrame > 1) { _baseTracked = false; return; }
            ApplyHeldPose(_poseAnchor);
        }

        private void ApplyHeldPose(Transform handAnchor)
        {
            Transform body = handAnchor.root;
            float dt = Mathf.Clamp(Time.deltaTime, 1e-4f, 0.05f);
            // v4: сглаженные положение и поворот игрока — без дрожи кадров/сети
            float yawRaw = body.eulerAngles.y;
            if (!_baseTracked) { _basePosF = body.position; _baseVelF = Vector3.zero; _yawF = yawRaw; _yawVelF = 0f; _baseTracked = true; }
            _basePosF = _followTime > 0f ? Vector3.SmoothDamp(_basePosF, body.position, ref _baseVelF, _followTime, 100f, dt) : body.position;
            _yawF = _yawTime > 0f ? Mathf.SmoothDampAngle(_yawF, yawRaw, ref _yawVelF, _yawTime, 5000f, dt) : yawRaw;
            Quaternion yaw = Quaternion.Euler(0f, _yawF, 0f);
            bool byHandle = _holdByHandle && _handle != null && _handle.Ready;
            Vector3 gripLocal = byHandle ? _handle.GripLocal : Vector3.zero;
            Vector3 grip = _gripOffset.sqrMagnitude > 1e-6f ? _gripOffset : _carryOffset + Vector3.up * gripLocal.y;
            Vector3 pos = _basePosF + yaw * _carryOffset;

            // ── качание: пружина от ускорения + такт шагов ──
            // Скорость сглажена: персонаж двигается рывками кадров — без этого ведро дрожит.
            if (!_tracked) { _lastPos = pos; _lastVel = _velF = Vector3.zero; _tracked = true; }
            Vector3 rawVel = (pos - _lastPos) / dt;
            if (rawVel.sqrMagnitude > 400f) rawVel = _velF;                        // телепорт
            _lastPos = pos;
            float kv = _motionSmoothing > 0f ? 1f - Mathf.Exp(-dt / _motionSmoothing) : 1f;
            _velF = Vector3.Lerp(_velF, rawVel, kv);
            Vector3 acc = Vector3.ClampMagnitude((_velF - _lastVel) / dt, 40f);
            _lastVel = _velF;
            Vector3 accLocal = Quaternion.Inverse(yaw) * acc;
            Vector2 drive = new Vector2(accLocal.z, -accLocal.x) * (_swayGain * 2.2f * (byHandle ? _handleSwayGain : 1f));     // градусы
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt * 240f), 1, 8);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                _swayVel += ((drive - _sway) * _swayStiffness - _swayVel * _swayDamping) * h;
                _sway += _swayVel * h;
            }
            _sway = Vector2.ClampMagnitude(_sway, _maxSway);

            float speed = new Vector2(_velF.x, _velF.z).magnitude;
            _walk01 = Mathf.MoveTowards(_walk01, Mathf.Clamp01(speed / 1.2f), dt * 4f);
            _stepPhase += dt * _stepFrequency * Mathf.Clamp(speed / 1.6f, 0.35f, 2f) * Mathf.PI * 2f;
            float bob = Mathf.Sin(_stepPhase) * _walkBob * _walk01;

            // Модель «стоит ровно»: запомненная поза тары без её поворота по кругу.
            Quaternion model = Quaternion.identity;
            if (_self != null && _self.HasUprightPose)
            {
                Quaternion q = _self.UprightRotation;
                model = Quaternion.Inverse(Quaternion.Euler(0f, q.eulerAngles.y, 0f)) * q;
            }
            Quaternion rot = yaw * Quaternion.Euler(_carryEuler) * model;

            // ── переливание / зачерпывание ──
            Action act = (Action)_action.Value;
            _poseT = Mathf.MoveTowards(_poseT, act != Action.None ? 1f : 0f, dt * _poseSpeed);
            float t = Mathf.SmoothStep(0f, 1f, _poseT);
            if (t > 0.001f)
            {
                Vector3 toTarget = yaw * Vector3.forward;
                Vector3 mouth = pos + toTarget;
                if (_target.Value != null)
                {
                    Cookware c = TargetCookware();
                    if (c != null) mouth = c.MouthPoint;
                    Vector3 d = Vector3.ProjectOnPlane(mouth - pos, Vector3.up);
                    if (d.sqrMagnitude > 1e-4f) toTarget = d.normalized;
                }
                Vector3 axis = Vector3.Cross(Vector3.up, toTarget);
                if (act == Action.Pour || (act == Action.None && _lastPourVisual))
                {
                    rot = Quaternion.AngleAxis(_pourTilt * t, axis) * rot;
                    pos += toTarget * _pourLean * t + Vector3.up * 0.1f * t;
                    _lastPourVisual = true;
                }
                else
                {
                    // Зачерпнуть: опустить в тару и наклонить краем к ней.
                    Vector3 dipTo = new Vector3(mouth.x, mouth.y - _scoopDip, mouth.z) - toTarget * 0.1f;
                    pos = Vector3.Lerp(pos, dipTo, t * 0.85f);
                    rot = Quaternion.AngleAxis(_scoopTilt * t, axis) * rot;
                    _lastPourVisual = false;
                }
            }
            else _lastPourVisual = false;

            // Качание поверх всего. При выливании — спокойнее: струя ровная.
            float calm = 1f - _pourSteady * (act == Action.Pour ? t : 0f);
            rot = Quaternion.AngleAxis(_sway.x * calm, yaw * Vector3.right) *
                  Quaternion.AngleAxis((_sway.y + bob) * calm, yaw * Vector3.forward) * rot;
            // Подпрыгивание в такт шагам: вверх-вниз дважды за шаг, чуть вбок — раз за шаг.
            // От этого по молоку бегут ритмичные волны.
            float bounce = _walkBounce * _walk01 * calm;
            Vector3 bounceV = Vector3.up * ((Mathf.Abs(Mathf.Sin(_stepPhase)) - 0.63f) * bounce)
                            + yaw * Vector3.right * (Mathf.Sin(_stepPhase) * bounce * 0.4f);
            pos += bounceV;
            // v4: за ручку — ведро висит ПОД точкой хвата: качание вращает его вокруг руки (маятник).
            // При выливании/зачерпывании хват плавно переходит в обычную позу (вторая рука под дно).
            if (byHandle)
            {
                Vector3 gripW = _basePosF + yaw * grip + bounceV;
                Vector3 hung = gripW - rot * gripLocal;
                pos = Vector3.Lerp(hung, pos, t);
            }
            transform.SetPositionAndRotation(pos, rot);
        }

        private bool _lastPourVisual;
    }
}
