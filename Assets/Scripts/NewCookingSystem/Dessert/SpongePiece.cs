using System.Collections.Generic;
using CatCooks.Food.Tools;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// КУБИК БИСКВИТА — отрезанный от пласта кусок.
    ///
    /// • Вид: тот же бисквит, что у пласта (меш строится сам) — корочка только с тех сторон,
    ///   где был край пласта, остальные стороны — свежий срез. Размер — как клетка пласта.
    /// • Мастерок: ЛКМ по кубику — кубик ложится на мастерок (как зачерпнуть из мешка).
    ///   ПКМ по тарелке — кубик на тарелке (DessertPlate соберёт на нём Аляску).
    ///   ПКМ в пол/на стол — кубик снова лежит кубиком, а не кучкой крошек.
    /// • Руками тоже можно: взял F, положил на тарелку.
    ///
    /// ═══ ТРЕБУЕТ ═══ FoodPiece, MeshFilter + MeshRenderer (Mat_SpongeChoc), BoxCollider,
    /// Rigidbody, NetworkObject, NetworkTransform. Префаб — в Spawnable Prefabs и в Piece Prefab пласта.
    /// </summary>
    [RequireComponent(typeof(FoodPiece))]
    public sealed class SpongePiece : NetworkBehaviour, IScoopSource
    {
        [Tooltip("Размер, если кубик положили в сцену руками (пласт ставит свой)")]
        [SerializeField] private Vector3 _size = new Vector3(0.6f, 0.3f, 0.6f);
        [Tooltip("Стороны с корочкой: 1 −X, 2 +X, 4 −Z, 8 +Z (15 — все)")]
        [SerializeField] private byte _crust = SpongeMesh.XNeg | SpongeMesh.ZNeg;
        [Tooltip("Можно ли взять мастерком (ЛКМ)")]
        [SerializeField] private bool _scoopable = true;
        [SerializeField] private bool _log = true;

        private readonly SyncVar<Vector3> _sizeNet = new SyncVar<Vector3>(Vector3.zero);
        private readonly SyncVar<byte> _crustNet = new SyncVar<byte>(0);

        private FoodPiece _food;
        private bool _taken;

        // ── какой префаб у какого продукта: мастерок кидает кубик обратно кубиком ──
        private static readonly Dictionary<ushort, GameObject> _prefabs = new Dictionary<ushort, GameObject>();

        public static void RegisterPrefab(ushort product, GameObject prefab)
        {
            if (product != 0 && prefab != null) _prefabs[product] = prefab;
        }

        public static bool TryGetPrefab(ushort product, out GameObject prefab)
        {
            if (!_prefabs.TryGetValue(product, out prefab) || prefab == null) return false;
            // v18.2: у вещества с профилем «мазок вместо кубика» (масло) на мастерке — красивая кучка,
            // и с мастерка на пол — кучка, а не кубик (сам брусок по-прежнему режется на кубики)
            FoodDatabase db = FoodDatabase.Instance;
            FoodSubstance sub = db != null ? FoodSubstance.Of(db.Get(product), FoodState.None) : null;
            if (sub != null && sub.OwnLook && sub.SmearNotPiece) { prefab = null; return false; }
            return true;
        }

        /// <summary>Размер из префаба (для вида на мастерке).</summary>
        public Vector3 DefaultSize => _size;

        public Vector3 Size => _sizeNet.Value.sqrMagnitude > 1e-8f ? _sizeNet.Value : _size;

        private void Awake()
        {
            _food = GetComponent<FoodPiece>();
            Refresh(_size, _crust);
        }

        /// <summary>До Spawn: размер клетки и стороны с корочкой.</summary>
        public void ServerSetup(Vector3 size, byte crust)
        {
            _size = size;
            _crust = crust;
            Refresh(size, crust);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _sizeNet.Value = _size;
            _crustNet.Value = _crust;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _sizeNet.OnChange += OnSize;
            _crustNet.OnChange += OnCrust;
            if (_sizeNet.Value.sqrMagnitude > 1e-8f) Refresh(_sizeNet.Value, _crustNet.Value);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _sizeNet.OnChange -= OnSize;
            _crustNet.OnChange -= OnCrust;
        }

        private void OnSize(Vector3 a, Vector3 b, bool asServer) => Refresh(b, _crustNet.Value);
        private void OnCrust(byte a, byte b, bool asServer) => Refresh(Size, b);

        private void Refresh(Vector3 size, byte crust)
        {
            if (size.sqrMagnitude < 1e-8f) return;
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = SpongeMesh.SharedPiece(size, crust);
            BoxCollider bc = GetComponent<BoxCollider>();
            if (bc != null) { bc.size = size * 0.98f; bc.center = Vector3.zero; }
        }

        // ───────────────────────── МАСТЕРОК ─────────────────────────

        public bool PeekScoop(out ushort product, out FoodState state)
        {
            product = _food != null ? _food.ProductId : (ushort)0;
            state = _food != null ? _food.State : FoodState.None;
            return _scoopable && _food != null && !_food.IsConsumed && !_food.IsHeld && product != 0;
        }

        public int ServerScoop(int max, out ushort product, out FoodState state, out byte cook, out byte clean)
        {
            product = 0; state = FoodState.None; cook = 0; clean = 100;
            if (_taken || !PeekScoop(out product, out state) || max <= 0) return 0;
            int n = Mathf.Min(max, _food.Count);
            if (n < _food.Count) return 0;                      // кубик целиком или никак
            cook = _food.Cook; clean = _food.Clean;
            if (!_food.ServerTryConsume()) return 0;
            _taken = true;
            if (_log) Debug.Log($"[Бисквит] «{name}» — на мастерке", this);
            base.NetworkObject.Despawn();
            return n;
        }

        /// <summary>Мастерок кинул кубик не в тарелку — снова кубик на этом месте. Сервер.</summary>
        public static bool ServerDropAsPiece(ushort product, FoodState state, int count, byte cook, byte clean, Vector3 at, NetworkBehaviour spawner)
        {
            if (!TryGetPrefab(product, out GameObject prefab) || spawner == null) return false;
            FoodDatabase db = FoodDatabase.Instance;
            ProductDef def = db != null ? db.Get(product) : null;
            for (int i = 0; i < Mathf.Max(1, count); i++)
            {
                SpongePiece src = prefab.GetComponent<SpongePiece>();
                float h = src != null ? src._size.y : 0.2f;
                GameObject go = Object.Instantiate(prefab, at + Vector3.up * (h * 0.6f + i * h * 1.05f), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                FoodPiece fp = go.GetComponent<FoodPiece>();
                if (fp != null) fp.ServerPreset(def, state, clean, cook, 100);
                FishNet.InstanceFinder.ServerManager.Spawn(go);
            }
            return true;
        }
    }
}
