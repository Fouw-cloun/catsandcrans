using FishNet.Object;
using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>
    /// МЕШОК С СЫПУЧИМ (сахар, мука, соль). В горловине мешка всегда лежит
    /// горка продукта — обычная кучка FoodHeap, как у картошки из измельчителя.
    ///
    /// Лопатой или мастерком: навёлся на МЕШОК (любой его коллайдер, любой слой) — ЛКМ, зачерпнул;
    /// ПКМ — кинул на пол (ляжет кучкой) или в тару. Мешок досыпает горку сам,
    /// поэтому он бесконечный (или на Supply порций, если Infinite снят).
    ///
    /// ═══ КАК ВЕШАТЬ ═══
    /// Корень мешка: NetworkObject + этот компонент (мешок может стоять
    /// статично; если его можно носить — горка ездит за ним).
    /// Mouth — дочерний пустой объект в горловине мешка, там лежит горка.
    /// Heap Prefab — ТОТ ЖЕ префаб FoodHeap, что у лопаты/измельчителя.
    /// </summary>
    public sealed class BulkSack : NetworkBehaviour, IScoopSource
    {
        private readonly FishNet.Object.Synchronizing.SyncVar<int> _leftNet = new FishNet.Object.Synchronizing.SyncVar<int>(0);

        // ── IScoopSource: лопата берёт прямо из мешка, на любой его коллайдер ──
        public bool PeekScoop(out ushort product, out FoodState state)
        {
            product = _product != null ? _product.Id : (ushort)0;
            state = _state;
            return _product != null && (_infinite || _leftNet.Value > 0);
        }

        public int ServerScoop(int max, out ushort product, out FoodState state, out byte cook, out byte clean)
        {
            product = _product != null ? _product.Id : (ushort)0;
            state = _state; cook = 0; clean = 100;
            if (_product == null || max <= 0) return 0;
            int n = _infinite ? max : Mathf.Min(max, _left);
            if (!_infinite) { _left -= n; _leftNet.Value = _left; }
            if (_log && n > 0) Debug.Log($"[Мешок] зачерпнули {n} «{_product.Title}»", this);
            return n;
        }
        [Header("Что в мешке")]
        [Tooltip("Food_Сахар (или мука, соль)")]
        [SerializeField] private ProductDef _product;
        [Tooltip("Состояние кучки. Для рецептов — «Сыпучее/Зачерпнуто»")]
        [SerializeField] private FoodState _state = FoodState.Scooped;
        [Tooltip("Сколько порций держать в горке")]
        [Range(2, 60)][SerializeField] private int _keep = 18;
        [Tooltip("Бесконечный мешок")]
        [SerializeField] private bool _infinite = true;
        [Tooltip("Запас, если не бесконечный, порций")]
        [Min(1)][SerializeField] private int _supply = 200;
        [Tooltip("Как быстро досыпается горка, порций/с")]
        [SerializeField] private float _refillPerSecond = 8f;

        [Header("Горка")]
        [Tooltip("v19: рисовать горку в горловине мешка. Выкл (по умолчанию) — мешок просто болванка: " +
                 "навёлся мастерком/лопатой, ЛКМ — набрал. Горка с физикой могла толкать мешок и всё вокруг")]
        [SerializeField] private bool _showHeap = false;
        [Tooltip("Префаб кучки FoodHeap (тот же, что у лопаты)")]
        [SerializeField] private FoodHeap _heapPrefab;
        [Tooltip("Горловина мешка — где лежит горка")]
        [SerializeField] private Transform _mouth;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private FoodHeap _heap;

        /// <summary>v19: свой префаб кучки, а не задан — общий из таблицы веществ (FoodSubstances → Floor Heap Prefab).</summary>
        private FoodHeap HeapPrefab
        {
            get
            {
                if (_heapPrefab != null) return _heapPrefab;
                FoodSubstanceTable t = FoodSubstanceTable.Instance;
                return t != null ? t.FloorHeapPrefab : null;
            }
        }

        private float _acc, _check;
        private int _left;

        private Vector3 MouthPos => _mouth != null ? _mouth.position : transform.position + Vector3.up * 0.5f;

        public override void OnStartServer()
        {
            base.OnStartServer();
            _left = _supply;
            _leftNet.Value = _left;
            SpawnHeap();
        }

        private void Update()
        {
            if (!_showHeap || !base.IsServerInitialized || _product == null || HeapPrefab == null) return;
            float dt = Time.deltaTime;
            _check -= dt;
            if (_check > 0f) return;
            _check = 0.2f;

            // Горку зачерпнули целиком — она исчезла. Новая.
            if (_heap == null || !_heap.IsSpawned) { _heap = null; SpawnHeap(); return; }

            // Горка ездит за мешком (если мешок носят).
            if ((_heap.transform.position - MouthPos).sqrMagnitude > 0.0004f)
                _heap.transform.position = MouthPos;

            // Досыпать до Keep.
            int need = _keep - _heap.Count;
            if (need <= 0) { _acc = 0f; return; }
            _acc += 0.2f * _refillPerSecond;
            int add = Mathf.Min(need, Mathf.FloorToInt(_acc));
            if (!_infinite) add = Mathf.Min(add, _left);
            if (add <= 0) return;
            _acc -= add;
            if (!_infinite) _left -= add;
            _heap.ServerAdd(add, 0, 100);
        }

        private void SpawnHeap()
        {
            if (!_showHeap) return;                     // v19: мешок — болванка, горку не рисуем
            if (_product == null || HeapPrefab == null)
            {
                if (_log) Debug.LogWarning($"[Мешок] «{name}»: не задан Product или Heap Prefab", this);
                return;
            }
            int n = _infinite ? _keep : Mathf.Min(_keep, _left);
            if (n <= 0) return;
            if (!_infinite) _left -= n;
            _heap = Instantiate(HeapPrefab, MouthPos, Quaternion.identity);
            _heap.ServerPreset(_product.Id, _state, n, 0, 100, true);   // сразу лежит, чистая
            base.Spawn(_heap.gameObject);
            if (_log) Debug.Log($"[Мешок] горка «{_product.Title}» ({n} порц.)", this);
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (_heap != null && _heap.IsSpawned) base.Despawn(_heap.gameObject);
        }
    }
}
