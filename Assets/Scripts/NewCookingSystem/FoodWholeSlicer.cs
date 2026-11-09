using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// РЕЗКА ЦЕЛОЙ МОДЕЛИ НА СЛАЙСЫ (лук). В отличие от FoodSliceable, модель не
    /// собрана из кусочков: она цельная, а слайсы — отдельные префабы.
    ///
    /// Режешь (пилой, ножом — любым ICuttable-инструментом) — с конца отваливается
    /// слайс (случайный из Slice Prefabs, если их несколько), сама луковица
    /// укорачивается на эту толщину. Последний слайс — луковица исчезает.
    /// Режется только то, что по правилам можно резать (лук: Мытое/Чищеное →
    /// Резать → Кусочки), слайсы — в этом состоянии.
    ///
    /// Ось резки — самая длинная сторона модели (или задай Axis).
    /// Настраивается кнопкой CatCooks → Овощи → Настроить выделенные.
    /// </summary>
    [RequireComponent(typeof(FoodPiece))]
    public sealed class FoodWholeSlicer : NetworkBehaviour, ICuttable
    {
        [Header("Слайсы")]
        [Tooltip("Префабы слайса (FoodPiece). Несколько — берётся случайный")]
        [SerializeField] private GameObject[] _slicePrefabs;
        [Tooltip("Продукт слайса. Пусто — тот же, что у целого (лук → лук). Какао-боб (половинка) → Какао-крупка")]
        [SerializeField] private ProductDef _sliceProduct;
        [Range(1, 20)][SerializeField] private int _sliceCount = 6;
        [Tooltip("Слайс того же размера, что и луковица (масштаб префаба × масштаб луковицы). " +
                 "Выключено — слайс появляется ровно в размере своего префаба.")]
        [SerializeField] private bool _scaleLikeOnion = false;

        [Header("Модель")]
        [Tooltip("Что укорачивать. Пусто — первый MeshRenderer")]
        [SerializeField] private Transform _model;
        [Tooltip("Ось резки в осях модели. Ноль — самая длинная сторона")]
        [SerializeField] private Vector3 _axis = Vector3.zero;

        [Header("Работа")]
        [SerializeField] private FoodProcess _process = FoodProcess.Cut;
        [Tooltip("Резать только чищеную (грязную и мытую — нельзя, сначала почистить)")]
        [SerializeField] private bool _onlyPeeled = true;
        [Min(0.05f)][SerializeField] private float _secondsPerSlice = 0.5f;
        [SerializeField] private float _sliceImpulse = 0.8f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly SyncVar<byte> _cut = new SyncVar<byte>(0);
        private readonly SyncVar<bool> _clamped = new SyncVar<bool>(false);

        private FoodPiece _food;
        private Rigidbody _rb;
        private bool _wasKinematic;
        private Vector3 _baseScale, _basePos, _axisL, _baseLossy;
        private float _halfLen;
        private bool _ready;
        private Mesh _cachedMesh;
        private bool _countSet;

        public int Left => Mathf.Max(0, _sliceCount - _cut.Value);

        // ── ICuttable ──
        public bool CanCut => Left > 0 && _food != null && !_food.IsConsumed
                              && (!_onlyPeeled || _food.State == FoodState.Peeled) && RuleAllows();
        public float SecondsPerCut => _secondsPerSlice;
        public bool ClampWhileCutting => false;
        public string CutLabel => $"слайсов осталось {Left}";
        public Color CrumbColor => _food != null && _food.Def != null ? _food.Def.BaseColor : Color.white;
        public Vector3 CutPoint() => EndPoint();

        [Server]
        public void ServerSetClamped(bool value) { if (_clamped.Value != value) _clamped.Value = value; }

        [Server]
        public void ServerCutOne(ProcessDef cutProcess, float quality01)
        {
            if (!CanCut) return;
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null || !db.Table.TryGet(_food.ProductId, _food.State, _process, out FoodTransition t)) return;

            Vector3 at = EndPoint();
            GameObject prefab = PickPrefab();
            if (prefab == null) { Debug.LogError($"[Слайсы] «{name}»: не заданы Slice Prefabs", this); return; }
            GameObject go = Instantiate(prefab, at, (_model != null ? _model : transform).rotation);
            if (_scaleLikeOnion) go.transform.localScale = Vector3.Scale(go.transform.localScale, ScaleOf());
            FoodPiece piece = go.GetComponent<FoodPiece>();
            if (piece != null) piece.ServerPreset(_sliceProduct != null ? _sliceProduct : _food.Def, t.To, _food.Clean, _food.Cook, _food.FormScore);
            base.Spawn(go);
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.AddForce((AxisWorld() * 0.6f + Vector3.up * 0.4f + Random.insideUnitSphere * 0.3f).normalized * _sliceImpulse, ForceMode.Impulse);

            _cut.Value = (byte)(_cut.Value + 1);
            if (_log) Debug.Log($"[Слайсы] «{name}»: отрезан слайс, {CutLabel}", this);
            if (Left <= 0)
            {
                _food.ServerTryConsume();
                base.NetworkObject.RemoveOwnership();
                base.Despawn(base.NetworkObject);
                return;
            }
            _food.ServerSetCount(Mathf.Max(1, Left));
        }

        // ── жизнь ──
        private void Awake()
        {
            _food = GetComponent<FoodPiece>();
            _rb = GetComponent<Rigidbody>();
            if (_model == null)
            {
                MeshRenderer mr = GetComponentInChildren<MeshRenderer>(true);
                _model = mr != null ? mr.transform : transform;
            }
            CacheModel();
        }

        private void CacheModel()
        {
            _baseScale = _model.localScale;
            _baseLossy = _model.lossyScale;
            _basePos = _model.localPosition;
            RefreshBounds(true);
            _ready = true;
        }

        /// <summary>Ось и длина — от ТЕКУЩЕГО меша: при чистке меш меняется на «чищеный».</summary>
        private void RefreshBounds(bool force = false)
        {
            MeshFilter mf = _model != null ? _model.GetComponent<MeshFilter>() : null;
            Mesh cur = mf != null ? mf.sharedMesh : null;
            if (!force && cur == _cachedMesh) return;
            _cachedMesh = cur;
            Bounds b = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.1f);
            if (_axis.sqrMagnitude > 1e-6f) _axisL = _axis.normalized;
            else
            {
                Vector3 s = b.size;
                _axisL = s.x >= s.y && s.x >= s.z ? Vector3.right : s.y >= s.z ? Vector3.up : Vector3.forward;
            }
            _halfLen = Mathf.Abs(Vector3.Dot(b.extents, _axisL));
        }

        /// <summary>
        /// Целая луковица = столько порций, сколько из неё выйдет слайсов (6). Тогда в кастрюле
        /// и в рецепте целая и нарезанная луковица весят одинаково.
        /// </summary>
        private void Update()
        {
            if (_countSet || !base.IsServerInitialized || _food == null) return;
            _countSet = true;
            if (_cut.Value == 0 && _food.Count < _sliceCount) _food.ServerSetCount(_sliceCount);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _cut.OnChange += OnCutChanged;
            _clamped.OnChange += OnClampChanged;
            ApplyShrink();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _cut.OnChange -= OnCutChanged;
            _clamped.OnChange -= OnClampChanged;
        }

        private void OnCutChanged(byte prev, byte next, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            ApplyShrink();
        }

        private void OnClampChanged(bool prev, bool next, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            if (_rb == null) return;
            if (next) { _wasKinematic = _rb.isKinematic; _rb.isKinematic = true; }
            else { _rb.isKinematic = _wasKinematic; _rb.WakeUp(); }
        }

        /// <summary>Луковица укорачивается с конца: масштаб по оси и сдвиг к оставшейся части.</summary>
        private void ApplyShrink()
        {
            if (!_ready || _model == null) return;
            RefreshBounds();
            float k = Mathf.Clamp01(Left / (float)Mathf.Max(1, _sliceCount));
            if (k <= 0f) return;
            Vector3 sc = _baseScale;
            Vector3 ax = new Vector3(Mathf.Abs(_axisL.x), Mathf.Abs(_axisL.y), Mathf.Abs(_axisL.z));
            sc = Vector3.Scale(sc, Vector3.one - ax * (1f - k));
            _model.localScale = sc;
            // отрезанное — с «+» конца оси; остаток прижимается к «−» концу
            Vector3 shiftModel = -_axisL * _halfLen * (1f - k);
            _model.localPosition = _basePos + _model.localRotation * Vector3.Scale(shiftModel, _baseScale);
        }

        private Vector3 AxisWorld() => (_model != null ? _model : transform).TransformDirection(_axisL).normalized;

        private Vector3 EndPoint()
        {
            if (_model == null) return transform.position;
            RefreshBounds();
            MeshFilter mf = _model.GetComponent<MeshFilter>();
            Vector3 c = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.center : Vector3.zero;
            return _model.TransformPoint(c + _axisL * _halfLen);
        }

        /// <summary>Масштаб слайса — как у целой модели (до укорачивания), иначе слайсы сплющит.</summary>
        private Vector3 ScaleOf() => _ready ? _baseLossy : transform.lossyScale;

        private GameObject PickPrefab()
        {
            if (_slicePrefabs == null || _slicePrefabs.Length == 0) return null;
            for (int tries = 0; tries < 6; tries++)
            {
                GameObject g = _slicePrefabs[Random.Range(0, _slicePrefabs.Length)];
                if (g != null) return g;
            }
            return null;
        }

        private bool RuleAllows()
        {
            FoodDatabase db = FoodDatabase.Instance;
            return db != null && db.CanApply(_food.ProductId, _food.State, _process);
        }

#if UNITY_EDITOR
        public void EditorSetup(GameObject[] slices, Transform model, int count)
        {
            if (slices != null) _slicePrefabs = slices;
            if (model != null) _model = model;
            if (count > 0) _sliceCount = count;
        }
#endif
    }
}
