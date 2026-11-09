using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ГОЛОВКА → ДОЛЬКИ (чеснок).
    ///
    ///   1. Бьёшь целую головку кувалдой — она «Отбита» и чуть сплющивается
    ///      (правило продукта: Мытое/Грязное → Отбивать → Отбито; кувалда шлёт «Отбивать»).
    ///   2. Чистишь сплющенную шлифмашинкой — шелуха летит, а когда она «Чищеная»,
    ///      головка разваливается на зубчики (префабы Part Prefabs), сама пропадает.
    ///
    /// Чистить целую (не битую) головку нельзя: шлифмашинка просто скребёт, в консоли
    /// подсказка «сначала кувалдой» (выключается Need Flat First).
    ///
    /// Головка = столько порций, сколько из неё выйдет зубчиков (как луковица = 6 слайсов).
    ///
    /// Сеть: что делать, решает сервер (по состоянию FoodPiece); сплющивание видят все
    /// по одному байту (какая ось модели смотрела вверх, когда ударили).
    ///
    /// Вешается на головку чеснока рядом с FoodPiece. Для правил продукта:
    /// CatCooks → Острый соус → 1. Создать продукты и рецепт.
    /// </summary>
    [RequireComponent(typeof(FoodPiece))]
    public sealed class FoodSplitIntoParts : NetworkBehaviour, IFoodWorkHandler
    {
        [Header("Сплющивание кувалдой")]
        [Tooltip("В каком состоянии головка сплющена")]
        [SerializeField] private FoodState _flatState = FoodState.Pounded;
        [Tooltip("Что сплющивать. Пусто — первый меш")]
        [SerializeField] private Transform _model;
        [Tooltip("Насколько сплющить по вертикали (0.7 — на 30% ниже)")]
        [Range(0.3f, 1f)][SerializeField] private float _flatHeight = 0.68f;
        [Tooltip("Насколько раздаётся вширь")]
        [Range(1f, 1.6f)][SerializeField] private float _flatWiden = 1.15f;
        [Tooltip("Время «шмяка», с")]
        [Range(0.05f, 0.6f)][SerializeField] private float _flatTime = 0.2f;

        [Header("Чистка → зубчики")]
        [SerializeField] private FoodProcess _splitProcess = FoodProcess.Peel;
        [Tooltip("Головка в этом состоянии разваливается на части")]
        [SerializeField] private FoodState _splitState = FoodState.Peeled;
        [Tooltip("Чистить можно только сплющенную (сначала кувалдой)")]
        [SerializeField] private bool _needFlatFirst = true;
        [Tooltip("Префабы зубчика (FoodPiece). Несколько — случайный")]
        [SerializeField] private GameObject[] _partPrefabs;
        [Range(1, 20)][SerializeField] private int _partCount = 8;
        [Tooltip("Продукт зубчика (Зубчик чеснока). Пусто — какой стоит в префабе")]
        [SerializeField] private ProductDef _partProduct;
        [SerializeField] private FoodState _partState = FoodState.Peeled;
        [Tooltip("Разлёт зубчиков")]
        [SerializeField] private float _burst = 1.2f;
        [SerializeField] private Color _huskColor = new Color(0.97f, 0.94f, 0.86f);

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly SyncVar<byte> _flatAxis = new SyncVar<byte>(0);   // 0 — не сплющен, 1..3 — ось X/Y/Z

        private FoodPiece _food;
        private Vector3 _baseScale;
        private float _flatT = -1f;
        private bool _split, _countSet, _hinted;

        private void Awake()
        {
            _food = GetComponent<FoodPiece>();
            if (_model == null)
            {
                MeshRenderer mr = GetComponentInChildren<MeshRenderer>(true);
                _model = mr != null ? mr.transform : transform;
            }
            _baseScale = _model.localScale;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _flatAxis.OnChange += OnFlatChanged;
            ApplyFlat(1f);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _flatAxis.OnChange -= OnFlatChanged;
        }

        private void OnFlatChanged(byte prev, byte next, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            _flatT = prev == 0 && next != 0 ? 0f : -1f;
            if (_flatT < 0f) ApplyFlat(1f);
        }

        private void Update()
        {
            if (_flatT >= 0f)
            {
                _flatT += Time.deltaTime / _flatTime;
                ApplyFlat(Mathf.Min(1f, _flatT));
                if (_flatT >= 1f) _flatT = -1f;
            }
            if (!base.IsServerInitialized || _food == null || _food.IsConsumed) return;

            if (!_countSet) { _countSet = true; if (_food.Count < _partCount) _food.ServerSetCount(_partCount); }

            FoodState s = _food.State;
            if (s == _flatState && _flatAxis.Value == 0) _flatAxis.Value = UpAxis();
            if (s == _splitState && !_split) Split();
        }

        /// <summary>Какая ось модели сейчас ближе всего к вертикали — по ней и сплющиваем.</summary>
        private byte UpAxis()
        {
            float x = Mathf.Abs(Vector3.Dot(_model.right, Vector3.up));
            float y = Mathf.Abs(Vector3.Dot(_model.up, Vector3.up));
            float z = Mathf.Abs(Vector3.Dot(_model.forward, Vector3.up));
            return (byte)(x >= y && x >= z ? 1 : y >= z ? 2 : 3);
        }

        /// <summary>Сплющить: k 0..1 — ход «шмяка» с небольшим перехлёстом.</summary>
        private void ApplyFlat(float k)
        {
            if (_model == null) return;
            byte ax = _flatAxis.Value;
            if (ax == 0) { _model.localScale = _baseScale; return; }
            float e = k >= 1f ? 1f : 1f + Mathf.Sin(k * Mathf.PI) * 0.35f;      // перехлёст: сплющило сильнее и отпружинило
            float h = Mathf.Lerp(1f, _flatHeight, Mathf.Clamp01(k * 1.6f)) / Mathf.Lerp(1f, e, 0.3f);
            float w = Mathf.Lerp(1f, _flatWiden, Mathf.Clamp01(k * 1.6f)) * Mathf.Lerp(1f, e, 0.1f);
            Vector3 m = new Vector3(w, w, w);
            m[ax - 1] = h;
            _model.localScale = Vector3.Scale(_baseScale, m);
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        public bool ServerHandleWork(FoodPiece piece, FoodProcess process, float seconds, float quality01)
        {
            if (!_needFlatFirst || process != _splitProcess) return false;
            FoodState s = piece.State;
            if (s == _flatState || s == _splitState) return false;
            if (!_hinted && _log)
            {
                _hinted = true;
                Debug.Log($"[Чеснок] «{name}»: целую головку не почистить — сначала разбей кувалдой", this);
            }
            return true;      // работу «съели»: целая головка не чистится
        }

        public void ServerStopWork(FoodPiece piece) { _hinted = false; }

        [Server]
        private void Split()
        {
            _split = true;
            if (_partPrefabs == null || _partPrefabs.Length == 0)
            {
                Debug.LogError($"[Чеснок] «{name}»: не заданы Part Prefabs (зубчики) — головка остаётся", this);
                return;
            }
            Vector3 c = CenterOf();
            float size = SizeOf();
            int n = Mathf.Max(1, _partCount);
            for (int i = 0; i < n; i++)
            {
                GameObject prefab = _partPrefabs[Random.Range(0, _partPrefabs.Length)];
                if (prefab == null) continue;
                float a = (i + Random.Range(-0.2f, 0.2f)) / n * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 at = c + dir * size * 0.25f + Vector3.up * size * 0.15f;
                GameObject go = Instantiate(prefab, at, Quaternion.LookRotation(dir) * Quaternion.Euler(Random.Range(-20f, 20f), 0f, 0f));
                FoodPiece p = go.GetComponent<FoodPiece>();
                if (p != null) p.ServerPreset(_partProduct, _partState, _food.Clean, _food.Cook, _food.FormScore);
                else Debug.LogWarning($"[Чеснок] префаб «{prefab.name}» без FoodPiece — его не бросить в тару", prefab);
                base.Spawn(go);
                Rigidbody rb = go.GetComponent<Rigidbody>();
                if (rb != null) rb.AddForce((dir + Vector3.up * 0.8f).normalized * _burst, ForceMode.VelocityChange);
            }
            RpcHusk(c, size);
            if (_log) Debug.Log($"[Чеснок] «{name}» развалился на {n} зубчиков", this);
            _food.ServerTryConsume();
            base.NetworkObject.RemoveOwnership();
            base.Despawn(base.NetworkObject);
        }

        [ObserversRpc]
        private void RpcHusk(Vector3 at, float size)
        {
            float k = Mathf.Max(1f, size / 0.06f);
            CCRuntimeParticles.Burst(at, Vector3.up, _huskColor, Color.white, 26, CCRuntimeParticles.Look.Chips.Scaled(1.6f * k));
            CCRuntimeParticles.Burst(at, Vector3.up, _huskColor * 0.9f, _huskColor, 14, CCRuntimeParticles.Look.Dust.Scaled(1.2f * k));
        }

        private Vector3 CenterOf()
        {
            Renderer r = _model != null ? _model.GetComponent<Renderer>() : null;
            return r != null ? r.bounds.center : transform.position;
        }

        private float SizeOf()
        {
            Renderer r = _model != null ? _model.GetComponent<Renderer>() : null;
            if (r == null) return 0.06f;
            Vector3 s = r.bounds.size;
            return Mathf.Max(s.x, Mathf.Max(s.y, s.z));
        }
    }
}
