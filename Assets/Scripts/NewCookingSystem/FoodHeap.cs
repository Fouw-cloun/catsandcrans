using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// КУЧКА В МИРЕ — сетевая. Тёртая картошка, пюре, фарш без тары.
    ///
    /// Живёт в двух режимах:
    ///   ЛЕТИТ   — вылетела из измельчителя или брошена лопатой: обычное
    ///             физическое тело.
    ///   ЛЕЖИТ   — упала на пол: замирает, вырастает до своего объёма,
    ///             сливается с соседней такой же кучкой.
    ///
    /// Куда упала:
    ///   • в тару (любой её коллайдер или триггер приёма) — высыпается в неё
    ///     и исчезает; что не влезло — летит дальше;
    ///   • на такую же кучку — сливается с ней;
    ///   • на пол — ложится. Грязнеет (шкала Clean), учёт грязи — потом.
    ///
    /// По сети: продукт, состояние, порции, чистота, «лежит». Вид (кубики)
    /// каждый игрок строит сам из этих чисел — одинаково у всех.
    ///
    /// ═══ ПРЕФАБ «FoodHeap» ═══
    ///   NetworkObject
    ///   NetworkTransform  — Server Authoritative (НЕ Client)
    ///   Rigidbody         — Interpolate, Continuous Dynamic, масса 1
    ///   SphereCollider    — радиус небольшой (размер лежащей подгонится сам)
    ///   FoodHeap
    ///   └─ Visual (пустой дочерний) — FoodHeapRenderer, Shape = Mound
    ///   Layer = Food. Префаб — в Spawnable Prefabs.
    /// </summary>
    public sealed class FoodHeap : NetworkBehaviour
    {
        [Header("Вид")]
        [SerializeField] private FoodHeapRenderer _visual;

        [Header("Полёт")]
        [Tooltip("Сколько секунд может лететь. Упала за карту — исчезнет.")]
        [SerializeField] private float _maxFlightTime = 8f;
        [Tooltip("Радиус тела в полёте, м")]
        [SerializeField] private float _flightRadius = 0.08f;

        [Header("На полу")]
        [Tooltip("Насколько грязнеет, упав на пол, 0..100")]
        [Range(0, 100)][SerializeField] private int _floorDirt = 50;
        [Tooltip("Сливаться с такой же кучкой ближе этого (сверх радиусов), м")]
        [SerializeField] private float _mergeSlack = 0.15f;
        [SerializeField] private LayerMask _heapMask = ~0;
        [Tooltip("Жидкость (молоко с мастерка) на полу становится этой лужей. Пусто — лужа из любой Cookware Spill в сцене")]
        [SerializeField] private LiquidPuddle _puddlePrefab;
        [Tooltip("Через сколько секунд кучка на полу исчезает. 0 — лежит всегда")]
        [Min(0f)][SerializeField] private float _floorLifetime = 0f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        // ───────── сеть ─────────
        private readonly SyncVar<ushort> _product = new SyncVar<ushort>(0);
        private readonly SyncVar<byte> _state = new SyncVar<byte>(0);
        private readonly SyncVar<ushort> _count = new SyncVar<ushort>(0);
        private readonly SyncVar<byte> _cook = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _clean = new SyncVar<byte>(100);
        private readonly SyncVar<bool> _settled = new SyncVar<bool>(false);

        // ───────── сервер ─────────
        private bool _preset;
        private ushort _pProduct;
        private FoodState _pState;
        private int _pCount;
        private byte _pCook, _pClean = 100;
        private float _spawnTime;
        private bool _gone;
        private bool _pSettled;

        private Rigidbody _rb;
        private SphereCollider _sphere;

        public ushort ProductId => _product.Value;
        public FoodState State => (FoodState)_state.Value;
        public int Count => _count.Value;
        public byte Cook => _cook.Value;
        public byte Clean => _clean.Value;
        public bool IsSettled => _settled.Value;
        public ProductDef Def => FoodDatabase.Instance != null ? FoodDatabase.Instance.Get(_product.Value) : null;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _sphere = GetComponent<SphereCollider>();
            if (_visual == null) _visual = GetComponentInChildren<FoodHeapRenderer>(true);
        }

        /// <summary>Задать содержимое ДО Spawn. Без [Server]: объект ещё не в сети.
        /// settled = сразу лежит (кучка на полу под станком), без полёта.</summary>
        public void ServerPreset(ushort product, FoodState state, int count, byte cook, byte clean,
                                 bool settled = false)
        {
            _preset = true;
            _pProduct = product; _pState = state; _pCount = count; _pCook = cook; _pClean = clean;
            _pSettled = settled;
        }

        /// <summary>
        /// Положить порции на пол: досыпать в лежащую рядом такую же кучку или
        /// создать новую, сразу лежащую. Сервер. Для переполнения тары, станков и т.п.
        /// </summary>
        public static FoodHeap ServerDropOnFloor(FoodHeap prefab, ushort product, FoodState state, int portions,
                                                 byte cook, byte clean, Vector3 point, float mergeSlack, LayerMask heapMask)
        {
            if (portions <= 0) return null;
            // v18.1: большая кучка «собирает» всё рядом (как лужа): ищем шире, ближе к большой — легче влиться
            Collider[] near = Physics.OverlapSphere(point, 3f, heapMask, QueryTriggerInteraction.Collide);
            FoodHeap best = null;
            float bestD = float.MaxValue;
            foreach (Collider c in near)
            {
                FoodHeap h = c.GetComponentInParent<FoodHeap>();
                if (h == null || h._gone || !h.IsSettled || h.ProductId != product || h.State != state) continue;
                float d = FlatDist(h.transform.position, point);
                if (d > h.VisualRadius * 1.15f + mergeSlack || d >= bestD) continue;
                best = h; bestD = d;
            }
            if (best != null)
            {
                best.ServerAdd(portions, cook, clean);
                best.ServerMergeAround(heapMask);
                return best;
            }

            if (prefab == null) return null;
            FoodHeap heap = Instantiate(prefab, point, Quaternion.identity);
            heap.ServerPreset(product, state, portions, cook, clean, true);
            FishNet.InstanceFinder.ServerManager.Spawn(heap.gameObject);
            return heap;
        }

        private static float FlatDist(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// v18.1: кучка выросла — соседние такие же, которые она «накрыла», вливаются в неё
        /// (много мелких из ковша → одна большая). Сервер.
        /// </summary>
        [Server]
        public void ServerMergeAround(LayerMask heapMask)
        {
            if (_gone) return;
            float r = VisualRadius;
            Collider[] near = Physics.OverlapSphere(transform.position, r + 1.5f, heapMask, QueryTriggerInteraction.Collide);
            foreach (Collider c in near)
            {
                FoodHeap h = c.GetComponentInParent<FoodHeap>();
                if (h == null || h == this || h._gone || !h.IsSettled || !SameStuff(h)) continue;
                float d = FlatDist(h.transform.position, transform.position);
                if (d > (r + h.VisualRadius) * 0.9f) continue;
                ServerAdd(h._count.Value, h._cook.Value, h._clean.Value);
                if (_log) Debug.Log($"[Кучка] соседняя влилась в большую: {Count}", this);
                h.Vanish();
            }
        }

        /// <summary>Радиус лежащей горки, м — для поиска «рядом уже есть кучка».</summary>
        public float VisualRadius => _visual != null && _visual.Radius > 0f ? _visual.Radius : 0.15f;

        public override void OnStartServer()
        {
            base.OnStartServer();
            _spawnTime = Time.time;
            _settledAt = Time.time;
            if (_preset)
            {
                _product.Value = _pProduct;
                _state.Value = (byte)_pState;
                _count.Value = (ushort)Mathf.Clamp(_pCount, 0, ushort.MaxValue);
                _cook.Value = _pCook;
                _clean.Value = _pClean;
                if (_pSettled)
                {
                    _settled.Value = true;
                    ApplyBody(true);
                }
            }
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _count.OnChange += OnCountChanged;
            _settled.OnChange += OnSettledChanged;
            _state.OnChange += OnStateChanged;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _count.OnChange -= OnCountChanged;
            _settled.OnChange -= OnSettledChanged;
            _state.OnChange -= OnStateChanged;
        }

        private float _clientStartTime;

        public override void OnStartClient()
        {
            base.OnStartClient();
            _clientStartTime = Time.time;
            RefreshVisual();
            ApplyBody(_settled.Value);
        }

        private void OnCountChanged(ushort a, ushort b, bool asServer) => RefreshVisual();
        private void OnStateChanged(byte a, byte b, bool asServer) => RefreshVisual();
        private void OnSettledChanged(bool a, bool b, bool asServer)
        {
            ApplyBody(b);
            RefreshVisual();
            // Упала — живая кучка разлетается от удара и оседает (у каждого игрока).
            // Только если она ЛЕТЕЛА: кучка, созданная сразу лежащей (под станком),
            // и первое получение состояния при подключении — без разлёта.
            bool landedNow = b && !a && Time.time - _clientStartTime > 0.25f;
            if (landedNow && _visual != null && !(asServer && base.IsClientInitialized)) _visual.Splash();
        }

        /// <summary>Запустить в полёт. Сервер, после Spawn.</summary>
        [Server]
        public void ServerLaunch(Vector3 velocity)
        {
            if (_rb == null) return;
            _rb.isKinematic = false;
            _rb.linearVelocity = velocity;
            _rb.angularVelocity = Random.insideUnitSphere * 3f;
        }

        // ───────────────────────── ВЗЯТЬ / ДОБАВИТЬ ─────────────────────────

        /// <summary>Зачерпнуть лопатой. Возвращает, сколько взято. Пустая — исчезает.</summary>
        [Server]
        public int ServerTake(int max)
        {
            if (_gone || max <= 0) return 0;
            int take = Mathf.Min(max, _count.Value);
            _count.Value = (ushort)(_count.Value - take);
            if (_count.Value == 0) Vanish();
            return take;
        }

        [Server]
        public void ServerAdd(int count, byte cook, byte clean)
        {
            if (_gone || count <= 0) return;
            int total = _count.Value + count;
            _cook.Value = (byte)((_cook.Value * _count.Value + cook * count) / Mathf.Max(1, total));
            _clean.Value = (byte)((_clean.Value * _count.Value + clean * count) / Mathf.Max(1, total));
            _count.Value = (ushort)Mathf.Min(total, ushort.MaxValue);
        }

        // ───────────────────────── ПОЛЁТ И ПАДЕНИЕ ─────────────────────────

        private float _settledAt;

        private void Update()
        {
            if (base.IsServerInitialized && !_gone && _settled.Value && _floorLifetime > 0f
                && Time.time - _settledAt > _floorLifetime)
            {
                if (_log) Debug.Log("[Кучка] полежала на полу — исчезла", this);
                Vanish();
                return;
            }
            if (!base.IsServerInitialized || _gone || _settled.Value) return;
            if (Time.time - _spawnTime > _maxFlightTime)
            {
                if (_log) Debug.Log($"[Кучка] улетела в никуда — удалена", this);
                Vanish();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!base.IsServerInitialized || _gone || _settled.Value) return;
            Cookware cw = other.GetComponentInParent<Cookware>();
            if (cw != null) PourInto(cw);
        }

        private void OnCollisionEnter(Collision c)
        {
            if (!base.IsServerInitialized || _gone || _settled.Value) return;
            if (Time.time - _spawnTime < 0.05f) return;   // вылет из ствола — не посадка

            Cookware cw = c.collider.GetComponentInParent<Cookware>();
            if (cw != null) { PourInto(cw); if (_gone) return; }

            FoodHeap other = c.collider.GetComponentInParent<FoodHeap>();
            if (other != null && other != this && other.IsSettled && SameStuff(other))
            {
                other.ServerAdd(_count.Value, _cook.Value, _clean.Value);
                if (_log) Debug.Log($"[Кучка] слилась с соседней: {other.Count}", other);
                Vanish();
                return;
            }

            // Садимся, только если под нами что-то похожее на пол.
            ContactPoint cp = c.GetContact(0);
            if (cp.normal.y < 0.55f) return;
            Settle(cp.point);
        }

        private void PourInto(Cookware cw)
        {
            int added = cw.ServerAddContent(_product.Value, State, _count.Value, _cook.Value);
            if (added <= 0) return;
            _count.Value = (ushort)(_count.Value - added);
            if (_log) Debug.Log($"[Кучка] высыпалась в «{cw.Def?.Title}»: +{added}", cw);
            if (_count.Value == 0) Vanish();
        }

        private void Settle(Vector3 point)
        {
            // Жидкость (молоко с мастерка) на полу — не кучка, а лужа.
            if (FoodHeapLook.IsLiquid(Def, State))
            {
                LiquidPuddle pp = PuddlePrefab;
                if (pp != null) LiquidPuddle.ServerAddAt(pp, _product.Value, point, _count.Value);
                if (_log) Debug.Log($"[Кучка] жидкость на пол — лужа ({_count.Value} порц.)", this);
                Vanish();
                return;
            }

            // Упала на пол — грязнеет (учёт потом, шкала уже есть).
            _clean.Value = (byte)Mathf.Max(0, _clean.Value - _floorDirt);

            transform.SetPositionAndRotation(point, Quaternion.identity);

            // Рядом уже лежит такая же — в неё.
            float reach = (_visual != null ? _visual.Radius : 0.1f) + _mergeSlack;
            Collider[] near = Physics.OverlapSphere(point, reach + 0.3f, _heapMask, QueryTriggerInteraction.Collide);
            foreach (Collider col in near)
            {
                FoodHeap other = col.GetComponentInParent<FoodHeap>();
                if (other == null || other == this || !other.IsSettled || !SameStuff(other)) continue;
                float d = Vector3.Distance(other.transform.position, point);
                float otherR = other._visual != null ? other._visual.Radius : 0.1f;
                if (d > otherR + reach) continue;
                other.ServerAdd(_count.Value, _cook.Value, _clean.Value);
                if (_log) Debug.Log($"[Кучка] легла в соседнюю: {other.Count}", other);
                Vanish();
                return;
            }

            _settled.Value = true;
            _settledAt = Time.time;
            ApplyBody(true);
            if (_log) Debug.Log($"[Кучка] легла на пол: {Describe()}", this);
        }

        /// <summary>Лужа для жидкости: своя или любая из Cookware Spill в сцене.</summary>
        private LiquidPuddle PuddlePrefab
        {
            get
            {
                if (_puddlePrefab != null) return _puddlePrefab;
                if (_sharedPuddle == null)
                    foreach (CookwareSpill s in FindObjectsByType<CookwareSpill>(FindObjectsSortMode.None))
                        if (s.PuddlePrefab != null) { _sharedPuddle = s.PuddlePrefab; break; }
                return _sharedPuddle;
            }
        }
        private static LiquidPuddle _sharedPuddle;

        private bool SameStuff(FoodHeap o) => o._product.Value == _product.Value && o._state.Value == _state.Value;

        private void Vanish()
        {
            if (_gone) return;
            _gone = true;
            base.Despawn(base.NetworkObject);
        }

        // ───────────────────────── ТЕЛО И ВИД ─────────────────────────

        private void ApplyBody(bool settled)
        {
            if (_rb != null)
            {
                if (settled)
                {
                    if (!_rb.isKinematic) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
                    _rb.isKinematic = true;
                }
                else if (base.IsServerInitialized) _rb.isKinematic = false;
            }
            FitCollider();
        }

        private void FitCollider()
        {
            if (_sphere == null) return;
            if (_settled.Value && _visual != null && _visual.Radius > 0f)
            {
                // Лежащая: плоская «таблетка» под горкой, чтобы лопата попадала лучом.
                float r = _visual.Radius;
                _sphere.radius = r * 0.8f;
                _sphere.center = new Vector3(0f, Mathf.Max(_visual.Height * 0.5f, r * 0.3f) - r * 0.5f, 0f);
            }
            else
            {
                _sphere.radius = _flightRadius;
                _sphere.center = Vector3.zero;
            }
        }

        private void RefreshVisual()
        {
            if (_visual == null) return;
            // В полёте кучка — комок: рисуем её по центру тела, а не на «полу».
            _visual.transform.localPosition = _settled.Value ? Vector3.zero : Vector3.down * _flightRadius * 0.6f;
            int seed = base.IsSpawned ? base.ObjectId * 104729 + 7 : 1;
            if (_count.Value > 0) FoodHeapLook.Apply(_visual, Def, State);   // вид и материалы — по продукту и состоянию
            _visual.Show(_count.Value, FoodColors.Of(Def, State) * Mathf.Lerp(0.6f, 1f, _clean.Value / 100f), seed);
            FitCollider();
        }

        public string Describe()
        {
            ProductDef d = Def;
            return $"{(d != null ? d.Title : "?")} · {FoodStateInfo.NameOf(State)} · ×{Count} · чистота {Clean}";
        }
    }
}
