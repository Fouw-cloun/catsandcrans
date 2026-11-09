using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ПРОДУКТ ИЗ СЛАЙСОВ — нарезка по одному куску.
    ///
    /// Замена старого SliceableFood для новой системы. Модель картошки состоит
    /// из слайсов (дочерние меши potato.001 … potato.006). Каждый распил:
    ///   • прячет крайний слайс на целой картошке (у всех игроков);
    ///   • спавнит на его месте отдельный предмет-кусок (FoodPiece),
    ///     в состоянии, которое даёт правило «текущее + Резать» (обычно «Кусочки»);
    ///   • кусок наследует чистоту, прожарку и форму целой картошки;
    ///   • у целой картошки становится на порцию меньше.
    /// На последнем распиле оставшиеся два слайса выпадают оба, а целая
    /// картошка исчезает.
    ///
    /// ═══ КТО РЕЖЕТ ═══
    ///
    /// 1. СТАРАЯ БЕНЗОПИЛА — без изменений. Она ищет любой ICuttable, а этот
    ///    компонент им является. Прицел, подача, крошки — всё её, как раньше.
    /// 2. Любой FoodTool / FoodImpactTool с процессом «Резать» — через
    ///    IFoodWorkHandler: FoodPiece отдаёт работу сюда, и слайс выпадает
    ///    каждые Seconds Per Slice секунд работы.
    ///
    /// Резать можно только то, что разрешает таблица: у овощей «Грязное + Резать»
    /// нет — сначала помой. Пила такую картошку просто не возьмёт в прицел
    /// (CanCut = false), а в логе будет подсказка.
    ///
    /// ═══ КАК НАСТРОИТЬ ═══
    ///
    /// На целую картошку (рядом с FoodPiece):
    ///   FoodSliceable
    ///     Slices       — пусто: соберёт сам, отсортирует вдоль длинной оси.
    ///     Piece Prefab — ОДИН префаб куска (FoodPiece + SlicePiece), см. ниже.
    ///   Шестерёнка компонента → «Собрать слайсы и заполнить префаб куска»:
    ///   порядок слайсов и их меши запишутся в SlicePiece префаба куска.
    ///
    /// Префаб куска «Картофель_слайс»:
    ///   NetworkObject, NetworkTransform (Client Authoritative), Rigidbody,
    ///   BoxCollider, MeshFilter + MeshRenderer (любой слайс),
    ///   FoodPiece (Product = Food_Картофель, Count = 1),
    ///   FoodStateVisual (Mesh Target ПУСТО — меш ставит SlicePiece),
    ///   SlicePiece (старый, без изменений).
    /// Префаб куска должен быть в Spawnable Prefabs у NetworkManager.
    /// </summary>
    [RequireComponent(typeof(FoodPiece))]
    public sealed class FoodSliceable : NetworkBehaviour, ICuttable, IFoodWorkHandler
    {
        [Header("Слайсы")]
        [Tooltip("Слайсы модели по порядку. ПУСТО — собрать из дочерних мешей и " +
                 "отсортировать вдоль длинной оси. Режется с конца списка.")]
        [SerializeField] private Transform[] _slices;

        [Tooltip("Префаб одного куска: FoodPiece + SlicePiece. Меш выбирается по индексу.")]
        [SerializeField] private GameObject _piecePrefab;

        [Tooltip("Свой префаб на слайс (необязательно). Пустой элемент — берётся Piece Prefab.")]
        [SerializeField] private GameObject[] _piecePrefabs;

        [Header("Работа")]
        [Tooltip("Каким процессом режется. Обычно «Резать».")]
        [SerializeField] private FoodProcess _process = FoodProcess.Cut;

        [Tooltip("Для FoodTool-зон: секунд работы на один слайс. Бензопила берёт своё время.")]
        [Min(0.05f)][SerializeField] private float _secondsPerSlice = 0.6f;

        [Tooltip("Держать ли картошку неподвижной, пока пила касается. " +
                 "Выключено: картошку обычно держит рука или она лежит на доске.")]
        [SerializeField] private bool _clampWhileCutting;

        [Header("Отделение")]
        [SerializeField] private float _pieceImpulse = 1.2f;
        [SerializeField] private float _pieceSpread = 0.35f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        // ───────── сеть ─────────
        private readonly SyncVar<byte> _cut = new SyncVar<byte>(0);
        private readonly SyncVar<bool> _clamped = new SyncVar<bool>(false);

        private FoodPiece _food;
        private Rigidbody _rb;
        private bool _wasKinematic;
        private float _work;               // сервер: накоплено секунд для FoodTool-зон
        private bool _warnedNotApplicable;

        public int SliceCount => _slices != null ? _slices.Length : 0;
        public int SlicesLeft => SliceCount - _cut.Value;

        // ───────── ICuttable (контракт старой бензопилы) ─────────

        /// <summary>Резать можно: слайсов больше одного И таблица разрешает
        /// «текущее состояние + Резать». Работает и на клиенте (прицел).</summary>
        public bool CanCut
        {
            get
            {
                if (SlicesLeft <= 1 || _food == null || _food.IsConsumed) return false;
                if (RuleAllows()) { _warnedNotApplicable = false; return true; }
                WarnNotApplicable();      // пила навелась на грязную — подсказать один раз
                return false;
            }
        }
        public float SecondsPerCut => 0f;                 // 0 = время задаёт пила
        public bool ClampWhileCutting => _clampWhileCutting;
        public string CutLabel => $"слайсов осталось {SlicesLeft}";
        public bool IsClamped => _clamped.Value;

        public Color CrumbColor
        {
            get
            {
                ProductDef d = _food != null ? _food.Def : null;
                return d != null ? d.CrumbColor : default;
            }
        }

        public Vector3 CutPoint()
        {
            if (_slices == null || SlicesLeft <= 0) return transform.position;
            Transform last = _slices[SlicesLeft - 1];
            return last != null ? last.position : transform.position;
        }

        /// <summary>Старая пила передаёт свой ProcessDef — он тут не нужен,
        /// процесс задан полем Process.</summary>
        public void ServerCutOne(ProcessDef ignored, float quality01) => ServerCutOne(quality01);

        // ───────────────────────── ЖИЗНЕННЫЙ ЦИКЛ ─────────────────────────

        private void Awake()
        {
            _food = GetComponent<FoodPiece>();
            _rb = GetComponent<Rigidbody>();
            if (_slices == null || _slices.Length == 0) _slices = CollectSorted(out _).ToArray();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Целая картошка = столько порций, сколько в ней слайсов.
            if (SliceCount > 1) _food.ServerSetCount(SliceCount);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _cut.OnChange += OnCutChanged;
            _clamped.OnChange += OnClampChanged;
            RefreshVisual();
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
            RefreshVisual();
        }

        private void RefreshVisual()
        {
            if (_slices == null) return;
            int left = SlicesLeft;
            for (int i = 0; i < _slices.Length; i++)
                if (_slices[i] != null) _slices[i].gameObject.SetActive(i < left);
        }

        // ───────────────────────── IFoodWorkHandler ─────────────────────────

        public bool ServerHandleWork(FoodPiece piece, FoodProcess process, float seconds, float quality01)
        {
            if (process != _process || SliceCount < 2) return false;   // не наше — пусть решает FoodPiece

            if (!CanCut) return true;                                    // наше, но сейчас нельзя

            _work += seconds;
            while (_work >= _secondsPerSlice && CanCut)
            {
                _work -= _secondsPerSlice;
                ServerCutOne(quality01);
                if (!base.IsSpawned) break;                              // последний распил убрал объект
            }
            return true;
        }

        public void ServerStopWork(FoodPiece piece) => _work = 0f;

        // ───────────────────────── РАСПИЛ ─────────────────────────

        [Server]
        public void ServerSetClamped(bool value)
        {
            if (_clamped.Value != value) _clamped.Value = value;
        }

        private void OnClampChanged(bool prev, bool next, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            if (_rb == null) return;
            if (next) { _wasKinematic = _rb.isKinematic; _rb.isKinematic = true; }
            else { _rb.isKinematic = _wasKinematic; _rb.WakeUp(); }
        }

        [Server]
        private void ServerCutOne(float quality01)
        {
            if (!CanCut) return;

            FoodDatabase db = FoodDatabase.Instance;
            if (db == null || !db.Table.TryGet(_food.ProductId, _food.State, _process, out FoodTransition t))
                return;

            int left = SlicesLeft;
            int index = left - 1;                                   // режем с хвоста
            SpawnPiece(index, t.To, quality01);

            if (left == 2)
            {
                // Последний распил: второй слайс тоже отдельным предметом,
                // целую картошку — убрать.
                SpawnPiece(0, t.To, quality01);
                if (_log) Debug.Log($"[Слайсы] «{name}» разрезана полностью", this);
                _food.ServerTryConsume();
                base.NetworkObject.RemoveOwnership();
                base.Despawn(base.NetworkObject);
                return;
            }

            _cut.Value = (byte)(_cut.Value + 1);
            _food.ServerSetCount(SlicesLeft);
            if (_log) Debug.Log($"[Слайсы] «{name}» — {CutLabel}", this);
        }

        [Server]
        private void SpawnPiece(int sliceIndex, FoodState pieceState, float quality01)
        {
            GameObject prefab = PrefabFor(sliceIndex);
            if (prefab == null)
            {
                Debug.LogError($"[Слайсы] «{name}»: не задан Piece Prefab для слайса {sliceIndex}", this);
                return;
            }

            Transform slice = _slices[sliceIndex];
            Vector3 at = slice != null ? slice.position : transform.position;
            Quaternion rot = slice != null ? slice.rotation : transform.rotation;

            GameObject go = Instantiate(prefab, at, rot);
            // Масштаб слайса в мире: у тебя картошка стоит со Scale 5, и кусок
            // без этого родился бы крошечным.
            if (slice != null) go.transform.localScale = slice.lossyScale;

            FoodPiece piece = go.GetComponent<FoodPiece>();
            if (piece == null)
            {
                Debug.LogError($"[Слайсы] на «{prefab.name}» нет FoodPiece", prefab);
                Destroy(go);
                return;
            }

            // Всё — ДО Spawn, чтобы клиенты получили готовые значения.
            byte form = (byte)Mathf.RoundToInt(_food.FormScore * Mathf.Clamp01(quality01 <= 0f ? 1f : quality01));
            piece.ServerPreset(_food.Def, pieceState, _food.Clean, _food.Cook, form);

            SlicePiece sp = go.GetComponent<SlicePiece>();
            if (sp != null) sp.ServerSetIndex((byte)sliceIndex);

            base.Spawn(go);

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 away = (at - transform.position);
                if (away.sqrMagnitude < 0.0001f) away = transform.up;
                Vector3 dir = (away.normalized + Random.insideUnitSphere * _pieceSpread + Vector3.up * 0.3f).normalized;
                rb.AddForce(dir * _pieceImpulse, ForceMode.Impulse);
            }
        }

        private GameObject PrefabFor(int i)
        {
            if (_piecePrefabs != null && i >= 0 && i < _piecePrefabs.Length && _piecePrefabs[i] != null)
                return _piecePrefabs[i];
            return _piecePrefab;
        }

        private bool RuleAllows()
        {
            FoodDatabase db = FoodDatabase.Instance;
            return db != null && db.CanApply(_food.ProductId, _food.State, _process);
        }

        private void WarnNotApplicable()
        {
            if (_warnedNotApplicable || !_log || _food == null) return;
            _warnedNotApplicable = true;
            Debug.Log($"[Слайсы] «{name}» в состоянии «{FoodStateInfo.NameOf(_food.State)}» " +
                      $"не режется: в группе нет правила «{FoodStateInfo.NameOf(_food.State)} + " +
                      $"{FoodStateInfo.NameOf(_process)}». Помой или почисти сначала.", this);
        }

        // ───────────────────────── СБОР СЛАЙСОВ ─────────────────────────

        private List<Transform> CollectSorted(out string axisName)
        {
            // Ищем глубоко: FBX обычно даёт промежуточный узел, слайсы — внуки.
            List<Transform> found = new List<Transform>();
            foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(true))
                if (r.transform != transform) found.Add(r.transform);

            axisName = "-";
            if (found.Count == 0)
            {
                Debug.LogError($"[Слайсы] на «{name}» нет дочерних мешей — назначь Slices вручную", this);
                return found;
            }

            Vector3 min = Center(found[0]), max = min;
            foreach (Transform t in found) { Vector3 c = Center(t); min = Vector3.Min(min, c); max = Vector3.Max(max, c); }
            Vector3 span = max - min;
            Vector3 axis;
            if (span.x >= span.y && span.x >= span.z) { axis = Vector3.right; axisName = "X"; }
            else if (span.y >= span.z) { axis = Vector3.up; axisName = "Y"; }
            else { axis = Vector3.forward; axisName = "Z"; }

            found.Sort((a, b) => Vector3.Dot(Center(a), axis).CompareTo(Vector3.Dot(Center(b), axis)));
            return found;
        }

        private Vector3 Center(Transform t)
        {
            MeshFilter mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                return transform.InverseTransformPoint(t.TransformPoint(mf.sharedMesh.bounds.center));
            return transform.InverseTransformPoint(t.position);
        }

#if UNITY_EDITOR
        [ContextMenu("Собрать слайсы и заполнить префаб куска")]
        private void EditorRebuild()
        {
            List<Transform> found = CollectSorted(out string axisName);
            if (found.Count == 0) return;
            _slices = found.ToArray();
            UnityEditor.EditorUtility.SetDirty(this);

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append($"[Слайсы] порядок вдоль оси {axisName} (режется с конца):");
            for (int i = 0; i < _slices.Length; i++) sb.Append($"\n   {i}: «{_slices[i].name}»");
            Debug.Log(sb.ToString(), this);

            if (_piecePrefab == null)
            {
                Debug.LogWarning("[Слайсы] Piece Prefab не задан — назначь префаб куска и нажми ещё раз", this);
                return;
            }
            SlicePiece sp = _piecePrefab.GetComponent<SlicePiece>();
            if (sp == null)
            {
                Debug.LogWarning($"[Слайсы] на «{_piecePrefab.name}» нет SlicePiece — добавь его", _piecePrefab);
                return;
            }
            Mesh[] meshes = new Mesh[_slices.Length];
            for (int i = 0; i < _slices.Length; i++)
            {
                MeshFilter mf = _slices[i].GetComponent<MeshFilter>();
                meshes[i] = mf != null ? mf.sharedMesh : null;
            }
            sp.EditorSetMeshes(meshes);
            UnityEditor.EditorUtility.SetDirty(sp);
            UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log($"[Слайсы] в «{_piecePrefab.name}» записано мешей: {meshes.Length}", this);
        }
#endif
    }
}
