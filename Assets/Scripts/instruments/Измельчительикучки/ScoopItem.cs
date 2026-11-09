using CatCooks.Food;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// В пространстве имён НАМЕРЕННО: в проекте ещё живёт старая система со
// своими глобальными FoodState и FoodTransition. Из глобального
// пространства компилятор взял бы их вместо новых (CatCooks.Food).
namespace CatCooks.Food.Tools
{
    /// <summary>
    /// ЛОПАТА / МАСТЕРОК. Один скрипт, разница — в цифрах (вместимость, дальность).
    ///
    /// ═══ УПРАВЛЕНИЕ ═══
    ///
    ///   ЛКМ            — зачерпнуть. Навёл прицел на кучку на полу или на тару
    ///                    с содержимым — в лопату набирается до Capacity порций.
    ///                    Можно добирать к уже набранному, если это то же самое.
    ///   ПКМ коротко    — кинуть туда, куда смотришь. Кучка летит дугой ровно в
    ///                    точку прицела (не дальше Toss Max Distance).
    ///   ПКМ зажать     — РОГАТКА. Лопата оттягивается назад, копит силу. Отпустил —
    ///                    выстрел по прицелу с этой силой. Держишь до конца
    ///                    (Charge Time) — выстрелит сама на полную.
    ///
    /// Что летит — обычная кучка FoodHeap: попала в тару — высыпалась в неё,
    /// упала на пол — легла кучкой (и испачкалась).
    ///
    /// ═══ ТРЕБУЕТ ═══
    ///   NetworkObject, NetworkTransform (Client Authoritative), Rigidbody, Collider.
    ///   Blade — дочерний пустой объект на полотне лопаты (там рисуется набранное):
    ///     на нём FoodHeapRenderer (Shape = Fill, Footprint = полотно, Max Height ~0.1).
    ///   Heap Prefab — тот же префаб FoodHeap, что у измельчителя.
    /// </summary>
    public class ScoopItem : PickupItem
    {
        [Header("Вместимость")]
        [Tooltip("Порций за раз. Лопата ~6, мастерок ~2.")]
        [Range(1, 40)][SerializeField] private int _capacity = 6;

        [Header("Зачерпнуть (ЛКМ)")]
        [Tooltip("Дальность от камеры, м")]
        [SerializeField] private float _range = 2.6f;
        [Tooltip("Слои кучек и тары (Food + Cookware)")]
        [SerializeField] private LayerMask _scoopMask = ~0;
        [SerializeField] private float _scoopCooldown = 0.35f;

        [Header("Кинуть (ПКМ коротко)")]
        [Tooltip("Дальше этого коротким броском не докинуть, м")]
        [SerializeField] private float _tossMaxDistance = 7f;
        [Tooltip("Время полёта на 1 м (дуга): больше — выше дуга")]
        [SerializeField] private float _tossTimePerMeter = 0.07f;
        [SerializeField] private float _tossTimeMin = 0.35f;
        [Tooltip("Что считается целью для броска (всё, кроме игроков)")]
        [SerializeField] private LayerMask _aimMask = ~0;

        [Header("Рогатка (ПКМ зажать)")]
        [Tooltip("Дольше этого — уже не короткий бросок, а натяжение, с")]
        [SerializeField] private float _tapTime = 0.2f;
        [Tooltip("Время натяжения до полной силы — дальше выстрел сам, с")]
        [SerializeField] private float _chargeTime = 1f;
        [Tooltip("Скорость выстрела: при минимальном и полном натяжении, м/с")]
        [SerializeField] private Vector2 _shotSpeed = new Vector2(6f, 18f);
        [Tooltip("На сколько лопата оттягивается назад при полном натяжении, м")]
        [SerializeField] private float _pullBack = 0.3f;

        [Header("Что вылетает")]
        [SerializeField] private FoodHeap _heapPrefab;

        [Header("Вид")]
        [Tooltip("Точка на полотне, где лежит набранное. FoodHeapRenderer на ней.")]
        [SerializeField] private Transform _blade;

        [Header("Отладка")]
        [SerializeField] private bool _debugRay;
        [SerializeField] private bool _log = true;

        // ───────── сеть: что в лопате ─────────
        private readonly SyncVar<ushort> _loadProduct = new SyncVar<ushort>(0);
        private readonly SyncVar<byte> _loadState = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _loadCount = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _loadCook = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _loadClean = new SyncVar<byte>(100);
        /// <summary>Натяжение 0..255 — чтобы остальные видели оттянутую лопату.</summary>
        private readonly SyncVar<byte> _chargeNet = new SyncVar<byte>(0);

        // ───────── владелец ─────────
        private bool _rmbDown;
        private float _rmbTime;
        private bool _firedThisPress;
        private float _localCharge;
        private byte _sentCharge;
        private float _nextScoop;

        // ───────── визуал ─────────
        private FoodHeapRenderer _bladeHeap;
        private float _shownCharge;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        // ── рёбра: растереть специи (ЛКМ зажать и водить мышью) ──
        private readonly HeldToolLook _look = new HeldToolLook();
        private readonly RackStroke _stroke = new RackStroke();
        private float _rubHintAt;

        public int LoadCount => _loadCount.Value;
        public bool IsEmpty => _loadCount.Value == 0;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            SetupBlade();
        }

        /// <summary>
        /// Полотно: точка Blade + FoodHeapRenderer на ней. Чего нет — создаётся само,
        /// чтобы набранное (сметана, сахар, нарезка) было видно на лопате сразу.
        /// </summary>
        private void SetupBlade()
        {
            if (_blade == null)
            {
                var b = new GameObject("Blade (auto)");
                b.transform.SetParent(transform, false);
                _blade = b.transform;
                Debug.LogWarning($"[Лопата] «{name}»: Blade не задан — горка будет над центром. " +
                                 "Создай пустой дочерний объект на полотне и укажи его в Blade.", this);
            }
            _bladeHeap = _blade.GetComponent<FoodHeapRenderer>();
            if (_bladeHeap == null)
            {
                _bladeHeap = _blade.gameObject.AddComponent<FoodHeapRenderer>();
                Vector2 fp = _capacity <= 2 ? new Vector2(0.06f, 0.045f) : new Vector2(0.11f, 0.09f);
                _bladeHeap.ConfigureAsToolBlade(fp, _capacity <= 2 ? 0.06f : 0.09f);
                if (_log) Debug.Log($"[Лопата] «{name}»: на Blade добавлен FoodHeapRenderer (полотно {fp.x * 2:0.##}×{fp.y * 2:0.##} м)", this);
            }
        }

        /// <summary>Префаб кучки: свой или взятый у любой тары (Spill Heap Prefab).</summary>
        private FoodHeap HeapPrefab
        {
            get
            {
                if (_heapPrefab != null) return _heapPrefab;
                foreach (Cookware c in Cookware.All)
                    if (c != null && c.SpillHeapPrefab != null) { _heapPrefab = c.SpillHeapPrefab; break; }
                return _heapPrefab;
            }
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _loadCount.OnChange += OnLoadChanged;
            _loadState.OnChange += OnLoadStateChanged;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _loadCount.OnChange -= OnLoadChanged;
            _loadState.OnChange -= OnLoadStateChanged;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            RefreshBlade();
        }

        private void OnLoadChanged(byte a, byte b, bool asServer)
        {
            RefreshBlade();
            // Зачерпнули — с края полотна срываются 1–2 капли. У каждого игрока
            // свои (визуал), поэтому на хосте — только клиентский вызов.
            if (b > a && !(asServer && base.IsClientInitialized)) DropFromEdge(Random.Range(1, 3));
        }

        [Header("Капли при зачерпывании")]
        [Tooltip("Откуда срываются капли: край полотна. Пусто — край считается от Blade.")]
        [SerializeField] private Transform _dripEdge;

        private void DropFromEdge(int count)
        {
            if (_bladeHeap == null || _blade == null) return;
            Vector3 edge = _dripEdge != null ? _dripEdge.position
                                             : _blade.position + _blade.forward * 0.12f;
            Vector3 outward = _dripEdge != null ? _dripEdge.forward : _blade.forward;
            _bladeHeap.SpawnDrops(edge, outward, count);
        }
        private void OnLoadStateChanged(byte a, byte b, bool asServer) => RefreshBlade();

        private void RefreshBlade()
        {
            if (_bladeHeap == null) return;
            FoodDatabase db = FoodDatabase.Instance;
            ProductDef d = db != null ? db.Get(_loadProduct.Value) : null;
            Color c = FoodColors.Of(d, (FoodState)_loadState.Value) * Mathf.Lerp(0.6f, 1f, _loadClean.Value / 100f);
            int seed = base.IsSpawned ? base.ObjectId * 31 + 5 : 3;
            // Кусок (кубик бисквита) лежит на полотне кубиком, а не кучкой
            GameObject piecePrefab = null;
            bool asPiece = _loadCount.Value > 0 && SpongePiece.TryGetPrefab(_loadProduct.Value, out piecePrefab);
            ShowBladePiece(asPiece ? piecePrefab : null);
            if (asPiece) { _bladeHeap.Show(0, c, seed); return; }
            if (_loadCount.Value > 0) FoodHeapLook.Apply(_bladeHeap, d, (FoodState)_loadState.Value);
            _bladeHeap.Show(_loadCount.Value, c, seed);
        }

        private Transform _bladePiece;

        /// <summary>Кубик на полотне мастерка (вид). null — убрать.</summary>
        private void ShowBladePiece(GameObject prefab)
        {
            if (prefab == null) { if (_bladePiece != null) _bladePiece.gameObject.SetActive(false); return; }
            if (_bladePiece == null)
            {
                var go = new GameObject("BladePiece (auto)");
                go.transform.SetParent(_blade, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                _bladePiece = go.transform;
            }
            SpongePiece sp = prefab.GetComponent<SpongePiece>();
            Vector3 size = sp != null ? sp.DefaultSize : prefab.transform.localScale;
            _bladePiece.GetComponent<MeshFilter>().sharedMesh = SpongeMesh.SharedPiece(size, SpongeMesh.XNeg | SpongeMesh.ZPos);
            MeshRenderer src = prefab.GetComponent<MeshRenderer>();
            if (src != null) _bladePiece.GetComponent<MeshRenderer>().sharedMaterial = src.sharedMaterial;
            Vector3 bs = _blade.lossyScale;
            _bladePiece.localScale = new Vector3(1f / Mathf.Max(1e-4f, Mathf.Abs(bs.x)), 1f / Mathf.Max(1e-4f, Mathf.Abs(bs.y)), 1f / Mathf.Max(1e-4f, Mathf.Abs(bs.z)));
            _bladePiece.localPosition = new Vector3(0f, size.y * 0.5f / Mathf.Max(1e-4f, Mathf.Abs(bs.y)), 0f);
            _bladePiece.localRotation = Quaternion.identity;
            _bladePiece.gameObject.SetActive(true);
        }

        // ───────────────────────── КАДР ─────────────────────────

        private void Update()
        {
            if (base.IsOwner) HandleOwnerInput();

            float target = base.IsOwner ? _localCharge : _chargeNet.Value / 255f;
            _shownCharge = Mathf.MoveTowards(_shownCharge, target, Time.deltaTime * 6f);
        }

        private void HandleOwnerInput()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            // ── рёбра: ЛКМ зажал на рёбрах со специями — растирать (камера стоит, мышь водит по мясу) ──
            bool rubbing = RubInput(cam);

            // ── ЛКМ: зачерпнуть ──
            if (!rubbing && Input.GetMouseButtonDown(0) && Time.time >= _nextScoop && _loadCount.Value < _capacity)
            {
                _nextScoop = Time.time + _scoopCooldown;
                NetworkObject target = FindScoopTarget(ray, out Vector3 hitPoint);
                if (target != null) CmdScoop(target, hitPoint);
                else if (_log) Debug.Log("[Лопата] " + WhyNothing(ray), this);
            }

            // ── ПКМ: бросок / рогатка ──
            if (Input.GetMouseButtonDown(1))
            {
                _rmbDown = true;
                _rmbTime = 0f;
                _firedThisPress = false;
            }

            if (_rmbDown && Input.GetMouseButton(1) && !_firedThisPress)
            {
                _rmbTime += Time.deltaTime;
                if (_rmbTime > _tapTime && !IsEmpty)
                {
                    _localCharge = Mathf.Clamp01((_rmbTime - _tapTime) / _chargeTime);
                    SendCharge(_localCharge);
                    if (_localCharge >= 1f)
                    {
                        // Дотянул до конца — выстрел сам.
                        CmdShoot(ray.direction, 1f);
                        _firedThisPress = true;
                        _localCharge = 0f;
                        SendCharge(0f);
                    }
                }
            }

            if (_rmbDown && Input.GetMouseButtonUp(1))
            {
                _rmbDown = false;
                if (!_firedThisPress && !IsEmpty)
                {
                    if (_rmbTime <= _tapTime) CmdToss(ray.origin, ray.direction);
                    else CmdShoot(ray.direction, _localCharge);
                }
                _localCharge = 0f;
                SendCharge(0f);
            }

            if (_debugRay) Debug.DrawRay(ray.origin, ray.direction * _range, Color.yellow);
        }

        /// <summary>Растирание специй по рёбрам. true — ЛКМ занята рёбрами (не черпать).</summary>
        private bool RubInput(Camera cam)
        {
            if (_stroke.Active)
            {
                if (!Input.GetMouseButton(0)) { _stroke.End(); return true; }
                if (_stroke.Tick(cam, out float st) && _stroke.Rack != null)
                    CmdRub(_stroke.Rack.NetworkObject, _stroke.Local, (byte)_stroke.Side, st);
                return true;
            }
            if (!Input.GetMouseButtonDown(0)) return false;
            if (_stroke.TryBegin(cam, _range, r => r.Pending(r.UpSide) > 0.01f, out RibsRack aimed, transform))
            {
                if (_log) Debug.Log("[Лопата] растираю специи — держи ЛКМ и води мышью по рёбрам", this);
                return true;
            }
            if (aimed != null)
            {
                if (_log && Time.time >= _rubHintAt)
                {
                    _rubHintAt = Time.time + 2f;
                    Debug.Log("[Лопата] на этой стороне рёбер нет специй — набери «Сухой руб», брось на рёбра (ПКМ), потом ЛКМ — растирать", this);
                }
                return true;
            }
            return false;
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdRub(NetworkObject rack, Vector3 local, byte side, float stroke)
        {
            if (rack == null) return;
            RibsRack r = rack.GetComponent<RibsRack>();
            if (r == null || (r.transform.position - transform.position).sqrMagnitude > 36f) return;
            r.ServerRubAt(local, side, Mathf.Min(stroke, 0.5f));
        }

        private void LateUpdate()
        {
            if (base.IsOwner) _look.Late(_stroke.Active, _stroke.Active);
        }

        /// <summary>Ближайшее под лучом — источник (IScoopSource), из которого можно взять?</summary>
        private NetworkObject FindSource(Ray ray, out Vector3 point)
        {
            point = default;
            int n = Physics.RaycastNonAlloc(ray, _hits, _range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            RaycastHit nearest = default;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.transform.IsChildOf(transform)) continue;
                if (h.collider.GetComponentInParent<PlayerInteractor>() != null) continue;
                if (h.distance >= best) continue;
                best = h.distance; nearest = h; any = true;
            }
            if (!any) return null;
            IScoopSource s = nearest.collider.GetComponentInParent<IScoopSource>();
            if (s == null || s.NetworkObject == null) return null;
            if (!s.PeekScoop(out ushort p, out FoodState st) || !CanTake(p, st)) return null;
            point = nearest.point;
            return s.NetworkObject;
        }

        /// <summary>Почему под прицелом нечего черпать — понятной фразой (смотрит лучом без маски).</summary>
        private string WhyNothing(Ray ray)
        {
            int n = Physics.RaycastNonAlloc(ray, _hits, _range, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            string why = null;
            FoodDatabase db = FoodDatabase.Instance;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.transform.IsChildOf(transform) || h.distance >= best) continue;
                IScoopSource src = h.collider.GetComponentInParent<IScoopSource>();
                FoodHeap heap = h.collider.GetComponentInParent<FoodHeap>();
                Cookware cw = h.collider.GetComponentInParent<Cookware>();
                if (heap == null && cw == null && src == null) continue;
                best = h.distance;
                string layer = LayerMask.LayerToName(h.collider.gameObject.layer);
                bool inMask = (_scoopMask.value & (1 << h.collider.gameObject.layer)) != 0;
                if (src != null)
                {
                    if (!src.PeekScoop(out ushort sp, out FoodState sst)) why = "мешок пустой (или не задан Product у Bulk Sack)";
                    else
                    {
                        ProductDef have = db != null ? db.Get(_loadProduct.Value) : null;
                        why = $"на лопате уже «{(have != null ? have.Title : "другое")}» — сначала выбрось (ПКМ)";
                    }
                }
                else if (!inMask) why = $"«{h.collider.name}» на слое «{layer}» — его нет в Scoop Mask ({LayerMaskNames(_scoopMask)}). Добавь слой или поставь Everything";
                else if (heap != null && !heap.IsSettled) why = "кучка ещё летит/не легла";
                else if (heap != null && !CanTake(heap.ProductId, heap.State))
                {
                    ProductDef have = db != null ? db.Get(_loadProduct.Value) : null;
                    why = $"на лопате уже «{(have != null ? have.Title : "другое")}» — сначала выбрось (ПКМ)";
                }
                else if (cw != null && cw.IsEmpty) why = "тара пустая";
                else why = "цель есть, но не подходит";
            }
            return why ?? $"под прицелом нечего черпать (дальность {_range} м, маска {LayerMaskNames(_scoopMask)}). Наведись на кучку, мешок или тару с содержимым";
        }

        private static string LayerMaskNames(LayerMask m)
        {
            if (m.value == ~0) return "Everything";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 32; i++)
                if ((m.value & (1 << i)) != 0) { if (sb.Length > 0) sb.Append(", "); sb.Append(LayerMask.LayerToName(i)); }
            return sb.ToString();
        }

        private void SendCharge(float c)
        {
            byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(c) * 255f);
            if (Mathf.Abs(b - _sentCharge) < 20 && b != 0 && b != 255) return;
            _sentCharge = b;
            CmdSetCharge(b);
        }

        private NetworkObject FindScoopTarget(Ray ray, out Vector3 point)
        {
            point = ray.origin + ray.direction * _range;
            // 1) Источник (мешок сахара, муки): первое, во что упёрся луч, — на любом слое.
            NetworkObject src = FindSource(ray, out Vector3 sp);
            if (src != null) { point = sp; return src; }
            int n = Physics.RaycastNonAlloc(ray, _hits, _range, _scoopMask, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            NetworkObject found = null;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.transform.IsChildOf(transform) || h.distance >= best) continue;

                FoodHeap heap = h.collider.GetComponentInParent<FoodHeap>();
                if (heap != null && heap.IsSettled && CanTake(heap.ProductId, heap.State))
                {
                    best = h.distance; found = heap.NetworkObject; point = h.point; continue;
                }
                Cookware cw = h.collider.GetComponentInParent<Cookware>();
                if (cw != null && !cw.IsEmpty)
                {
                    best = h.distance; found = cw.NetworkObject; point = h.point;
                    // Попали в стенку/коллайдер тары — ямку ставим на поверхность содержимого по лучу.
                    CookwareLiquidVisual lv = cw.GetComponentInChildren<CookwareLiquidVisual>();
                    if (lv != null && lv.TryGetSurfaceY(h.point, out float sy) && Mathf.Abs(ray.direction.y) > 0.05f)
                    {
                        float tt = (sy - ray.origin.y) / ray.direction.y;
                        if (tt > 0f && tt < _range * 1.5f) point = ray.origin + ray.direction * tt;
                    }
                }
            }
            return found;
        }

        private Vector3 AimPoint(Ray ray)
        {
            int n = Physics.RaycastNonAlloc(ray, _hits, _tossMaxDistance, _aimMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 p = ray.origin + ray.direction * _tossMaxDistance;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].transform.IsChildOf(transform) || _hits[i].distance >= best) continue;
                best = _hits[i].distance;
                p = _hits[i].point;
            }
            return p;
        }

        private bool CanTake(ushort product, FoodState state) =>
            IsEmpty || (_loadProduct.Value == product && _loadState.Value == (byte)state);

        // ───────────────────────── СЕРВЕР ─────────────────────────

        [ServerRpc(RequireOwnership = true)]
        private void CmdSetCharge(byte c) => _chargeNet.Value = c;

        [ServerRpc(RequireOwnership = true)]
        private void CmdScoop(NetworkObject target, Vector3 hitPoint)
        {
            if (target == null) return;
            // Далеко ли: тара может сидеть на чужом корне (ковш экскаватора — корень сетевого объекта
            // у кабины, за метры). Поэтому меряем до горловины тары / точки прицела, а не до корня.
            float lim = _range + 2f;
            Vector3 refPos = target.transform.position;
            Cookware nearCw = CookwareLookup.Of(target, hitPoint);
            if (nearCw != null) refPos = nearCw.MouthPoint;
            Vector3 me = transform.position;
            if ((refPos - me).sqrMagnitude > lim * lim && (hitPoint - me).sqrMagnitude > lim * lim)
            {
                if (_log) Debug.Log($"[Лопата] «{target.name}» слишком далеко ({Vector3.Distance(refPos, me):0.0} м, можно до {lim:0.0}) — подойди ближе", this);
                return;
            }

            int room = _capacity - _loadCount.Value;
            if (room <= 0) { if (_log) Debug.Log($"[Лопата] полная ({_loadCount.Value}/{_capacity}) — сначала выложи (ПКМ)", this); return; }

            // Источник (мешок): берём прямо из него, горка в горловине — только вид.
            IScoopSource src = target.GetComponent<IScoopSource>();
            if (src != null)
            {
                if (!src.PeekScoop(out ushort sp, out FoodState sst) || !CanTake(sp, sst)) return;
                int got = src.ServerScoop(room, out ushort product, out FoodState state, out byte cook, out byte clean);
                Load(product, state, got, cook, clean);
                return;
            }

            FoodHeap heap = target.GetComponent<FoodHeap>();
            if (heap != null)
            {
                if (!CanTake(heap.ProductId, heap.State)) return;
                ushort product = heap.ProductId;
                FoodState state = heap.State;
                byte cook = heap.Cook, clean = heap.Clean;
                int took = heap.ServerTake(room);
                Load(product, state, took, cook, clean);
                return;
            }

            Cookware cw = nearCw != null ? nearCw : CookwareLookup.Of(target, transform.position);
            if (cw != null)
            {
                // Из тары берём только то, что совпадает с уже набранным.
                int took = cw.ServerTakeTop(room, out ushort product, out FoodState state, out byte cook);
                if (took <= 0) { if (_log) Debug.Log($"[Лопата] в «{(cw.Def != null ? cw.Def.Title : cw.name)}» нечего взять сверху", this); return; }
                // Жидкость (молоко, вода, сливки) лопатой не черпается — вернуть. Сметана и масло — можно.
                FoodDatabase sdb = FoodDatabase.Instance;
                ProductDef took_d = sdb != null ? sdb.Get(product) : null;
                if (took_d != null && took_d.Solid)
                {
                    cw.ServerAddPinch(product, state, took, cook, false);
                    if (_log) Debug.Log($"[Лопата] «{took_d.Title}» застыло — мастерком не взять", this);
                    return;
                }
                if (FoodHeapLook.IsLiquid(sdb != null ? sdb.Get(product) : null, state))
                {
                    cw.ServerAddPinch(product, state, took, cook, false);
                    if (_log) Debug.Log("[Лопата] жидкость лопатой не черпается — возьми ведро или ковш", this);
                    return;
                }
                if (!CanTake(product, state))
                {
                    cw.ServerAddPinch(product, state, took, cook, false);   // не то — вернуть (не через перелив)
                    if (_log) Debug.Log("[Лопата] в лопате уже другое — сначала высыпь", this);
                    return;
                }
                Load(product, state, took, cook, 100);
                RpcScoopMark(target, hitPoint);
            }
        }

        /// <summary>След от мастерка в пасте (ямка с валиком) — у всех. Только вид.</summary>
        [ObserversRpc]
        private void RpcScoopMark(NetworkObject target, Vector3 point)
        {
            if (target == null) return;
            CookwareLiquidVisual lv = target.GetComponentInChildren<CookwareLiquidVisual>();
            if (lv != null) lv.Dent(point);
        }

        private void Load(ushort product, FoodState state, int count, byte cook, byte clean)
        {
            if (count <= 0) return;
            int before = _loadCount.Value;
            int total = before + count;
            _loadProduct.Value = product;
            _loadState.Value = (byte)state;
            _loadCook.Value = (byte)((_loadCook.Value * before + cook * count) / total);
            _loadClean.Value = (byte)((_loadClean.Value * before + clean * count) / Mathf.Max(1, total));
            _loadCount.Value = (byte)Mathf.Min(255, total);
            if (_log) Debug.Log($"[Лопата] зачерпнула +{count} · в лопате {_loadCount.Value}/{_capacity}", this);
        }

        /// <summary>
        /// Короткий бросок — ПО ЛУЧУ ИЗ КАМЕРЫ, без летящей кучки: что под прицелом,
        /// туда и попало мгновенно.
        ///   • тара (ведро, банка, кастрюля — любой её коллайдер) — содержимое сыплется
        ///     прямо в неё, ничего не сбивая; не влезло — остаётся на лопате;
        ///   • пол или что угодно другое — ложится кучкой в точке (к соседней такой же
        ///     подсыпается), кучку на лету уже не толкнуть и не уронить на ведро.
        /// Рогатка (ПКМ удерживать) по-прежнему стреляет физической кучкой.
        /// </summary>
        [ServerRpc(RequireOwnership = true)]
        private void CmdToss(Vector3 origin, Vector3 dir)
        {
            if (IsEmpty || dir.sqrMagnitude < 1e-6f) return;
            dir.Normalize();
            Vector3 from = LaunchPoint();
            Ray ray = new Ray(origin, dir);
            int n = Physics.RaycastNonAlloc(ray, _hits, _tossMaxDistance, _aimMask, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            RaycastHit hit = default;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.transform.IsChildOf(transform) || h.distance >= best) continue;
                if (h.collider.GetComponentInParent<PlayerInteractor>() != null) continue;
                // мелкие триггеры без смысла (зоны) пропускаем; триггер тары (Receive) — считаем
                if (h.collider.isTrigger && h.collider.GetComponentInParent<Cookware>() == null) continue;
                // кучки на полу — не преграда, бросок ложится рядом/в них
                best = h.distance; hit = h; any = true;
            }
            Vector3 point = any ? hit.point : origin + dir * _tossMaxDistance;
            Vector3 normal = any ? hit.normal : Vector3.up;

            ushort product = _loadProduct.Value;
            FoodState state = (FoodState)_loadState.Value;
            int count = _loadCount.Value;
            byte cook = _loadCook.Value, clean = _loadClean.Value;
            Color tint = FoodColors.Of(FoodDatabase.Instance != null ? FoodDatabase.Instance.Get(product) : null, state);

            // Рёбра (и всё, что принимает мастерок): специи — горкой на мясо
            IScoopReceiver rcv = any ? hit.collider.GetComponentInParent<IScoopReceiver>() : null;
            if (rcv != null)
            {
                int took = rcv.ServerReceiveScoop(product, state, count, point, normal, out string why);
                if (took <= 0)
                {
                    if (_log && !string.IsNullOrEmpty(why)) Debug.Log($"[Лопата] {why}", this);
                    return;
                }
                int left = Mathf.Max(0, count - took);
                _loadCount.Value = (byte)left;
                if (left <= 0) { _loadClean.Value = 100; _loadCook.Value = 0; }
                RpcTossFx(from, point, tint, false);
                if (_log) Debug.Log($"[Лопата] на рёбра: {took} порц.", this);
                return;
            }

            Cookware cw = any ? hit.collider.GetComponentInParent<Cookware>() : null;
            if (cw != null)
            {
                int put = cw.ServerAddContent(product, state, count, cook, false);
                if (put <= 0)
                {
                    if (_log) Debug.Log($"[Лопата] «{(cw.Def != null ? cw.Def.Title : cw.name)}» полная — не влезло", this);
                    return;
                }
                int rest = Mathf.Max(0, count - put);
                _loadCount.Value = (byte)Mathf.Min(rest, 255);
                if (rest <= 0) { _loadClean.Value = 100; _loadCook.Value = 0; }
                cw.ServerAgitate(0.1f);
                RpcTossFx(from, cw.MouthPoint, tint, true);
                if (_log) Debug.Log($"[Лопата] бросок в «{(cw.Def != null ? cw.Def.Title : cw.name)}»: {put} порц.", this);
                return;
            }

            // Кубик бисквита — снова кубиком, а не кучкой крошек
            if (SpongePiece.ServerDropAsPiece(product, state, count, cook, clean, point + normal * 0.02f, this))
            {
                _loadCount.Value = 0; _loadClean.Value = 100; _loadCook.Value = 0;
                RpcTossFx(from, point, tint, false);
                if (_log) Debug.Log($"[Лопата] кубик положен: {count} шт.", this);
                return;
            }

            // На пол/в стену: кучка в точке попадания.
            FoodHeap prefab = HeapPrefab;
            if (prefab == null)
            {
                Debug.LogError("[Лопата] не назначен Heap Prefab (и ни у одной тары нет Spill Heap Prefab) — перетащи truha", this);
                return;
            }
            Vector3 drop = point + normal * 0.02f;
            FoodHeap.ServerDropOnFloor(prefab, product, state, count, cook, clean, drop, 0.2f, ~0);
            _loadCount.Value = 0; _loadClean.Value = 100; _loadCook.Value = 0;
            RpcTossFx(from, drop, tint, false);
            if (_log) Debug.Log($"[Лопата] бросок на пол: {count} порц.", this);
        }

        /// <summary>След броска: струйка/облачко от лопаты к точке (вид, у всех).</summary>
        [ObserversRpc]
        private void RpcTossFx(Vector3 from, Vector3 to, Color color, bool intoCookware)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.01f) return;
            Vector3 dir = d / len;
            Color c2 = Color.Lerp(color, Color.white, 0.35f);
            // горсть летит по лучу: несколько пачек крупинок вдоль линии, мгновенно
            int steps = Mathf.Clamp(Mathf.RoundToInt(len / 0.25f), 2, 10);
            for (int i = 0; i < steps; i++)
            {
                Vector3 at = from + dir * (len * (i + 0.5f) / steps);
                CCRuntimeParticles.Burst(at, dir, color, c2, 4, CCRuntimeParticles.Look.Dust.Scaled(0.3f));
            }
            Vector3 up = intoCookware ? Vector3.up : Vector3.up;
            CCRuntimeParticles.Burst(to, up, color, c2, 14, CCRuntimeParticles.Look.Dust.Scaled(0.55f));
            if (intoCookware)
                foreach (Cookware cw in Cookware.All)
                {
                    if (cw == null || Vector3.Distance(cw.MouthPoint, to) > 0.05f) continue;
                    CookwareLiquidVisual lv = cw.GetComponentInChildren<CookwareLiquidVisual>();
                    if (lv != null) lv.Ripple(to);
                    break;
                }
        }

        /// <summary>Выстрел из «рогатки»: по направлению прицела с силой натяжения.</summary>
        [ServerRpc(RequireOwnership = true)]
        private void CmdShoot(Vector3 dir, float charge)
        {
            _chargeNet.Value = 0;
            if (IsEmpty) return;
            float speed = Mathf.Lerp(_shotSpeed.x, _shotSpeed.y, Mathf.Clamp01(charge));
            Vector3 v = dir.normalized * speed + Vector3.up * 0.8f;
            Throw(LaunchPoint(), v);
            if (_log) Debug.Log($"[Лопата] выстрел, сила {Mathf.RoundToInt(charge * 100f)}%", this);
        }

        private Vector3 LaunchPoint() => (_blade != null ? _blade.position : transform.position) + Vector3.up * 0.05f;

        private void Throw(Vector3 from, Vector3 velocity)
        {
            // кубик бисквита из рогатки — летит кубиком (на тарелку попадёт — тарелка проглотит)
            if (SpongePiece.TryGetPrefab(_loadProduct.Value, out GameObject piecePrefab))
            {
                FoodDatabase pdb = FoodDatabase.Instance;
                ProductDef pd = pdb != null ? pdb.Get(_loadProduct.Value) : null;
                for (int i = 0; i < _loadCount.Value; i++)
                {
                    GameObject go = Instantiate(piecePrefab, from + Vector3.up * (0.1f * i), Quaternion.identity);
                    FoodPiece fp = go.GetComponent<FoodPiece>();
                    if (fp != null) fp.ServerPreset(pd, (FoodState)_loadState.Value, _loadClean.Value, _loadCook.Value, 100);
                    base.Spawn(go);
                    Rigidbody prb = go.GetComponent<Rigidbody>();
                    if (prb != null) prb.linearVelocity = velocity;
                }
                _loadCount.Value = 0; _loadClean.Value = 100; _loadCook.Value = 0;
                return;
            }
            FoodHeap prefab = HeapPrefab;
            if (prefab == null)
            {
                Debug.LogError("[Лопата] не назначен Heap Prefab (и ни у одной тары нет Spill Heap Prefab) — перетащи truha", this);
                return;
            }
            FoodHeap heap = Instantiate(prefab, from, Quaternion.identity);
            heap.ServerPreset(_loadProduct.Value, (FoodState)_loadState.Value, _loadCount.Value,
                              _loadCook.Value, _loadClean.Value);
            base.Spawn(heap.gameObject);
            heap.ServerLaunch(velocity);

            _loadCount.Value = 0;
            _loadClean.Value = 100;
            _loadCook.Value = 0;
        }

        // ───────────────────────── РУКА ─────────────────────────

        public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
        {
            _chargeNet.Value = 0;
            _look.Release();
            base.ServerOnDrop(hand, tossDirection, force);
        }

        public override void UpdateHeldPose(Transform handAnchor)
        {
            _look.Pose(handAnchor);
            // Базовая поза в руке — от PickupItem (его Hold Offset), к ней — наше движение.
            base.UpdateHeldPose(handAnchor);
            if (_stroke.Active) { _stroke.Pose(transform, 0.04f); return; }   // растираешь — мастерок по мясу
            Vector3 pos = transform.position;
            Quaternion rot = transform.rotation;
            // Рогатка: лопата оттягивается назад и чуть вверх, дрожит на полном натяжении.
            if (_shownCharge > 0.001f)
            {
                pos += handAnchor.rotation * (Vector3.back * _pullBack + Vector3.up * _pullBack * 0.3f) * _shownCharge;
                if (_shownCharge > 0.85f) pos += Random.insideUnitSphere * 0.006f;
                rot *= Quaternion.Euler(-25f * _shownCharge, 0f, 0f);
            }
            transform.SetPositionAndRotation(pos, rot);
        }
    }

    /// <summary>
    /// ИСТОЧНИК, из которого черпают лопатой/мастерком: мешок сахара, муки, соли.
    /// Навёлся на любой его коллайдер — ЛКМ, набрал. Бесконечный или с запасом — решает он сам.
    /// </summary>
    public interface IScoopSource
    {
        NetworkObject NetworkObject { get; }
        /// <summary>Что даст (и есть ли что). Клиент и сервер.</summary>
        bool PeekScoop(out ushort product, out FoodState state);
        /// <summary>Взять до max порций. Только сервер.</summary>
        int ServerScoop(int max, out ushort product, out FoodState state, out byte cook, out byte clean);
    }
}
