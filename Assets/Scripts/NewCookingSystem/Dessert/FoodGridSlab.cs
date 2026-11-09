using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>Пила может сказать, КУДА она целится (точка на продукте). Режущий выбирает кусок там.</summary>
    public interface ICuttableAim
    {
        /// <summary>Сервер: игрок целится сюда (мир).</summary>
        void ServerAimAt(Vector3 worldPoint);

        /// <summary>Любой клиент: какой кусок отрежется, если пилить в эту точку. corners — 8 углов рамки (мир):
        /// 0–3 низ, 4–7 верх. false — резать там нечего.</summary>
        bool AimBox(Vector3 worldPoint, Vector3[] corners);
    }

    /// <summary>
    /// ПЛАСТ, КОТОРЫЙ РЕЖЕТСЯ НА РАВНЫЕ КУБИКИ — бисквит из опалубки.
    ///
    /// Выглядит ОДНИМ куском: верх — сплошная корочка, швов между будущими кубиками нет.
    /// Пилишь — там, куда смотришь, от пласта отделяется кубик:
    ///   • на пласте остаётся ровный срез — светлый пористый мякиш (а край пласта — с корочкой);
    ///   • кубик не отлетает, а мягко отъезжает от пласта и ложится рядом;
    ///   • у кубика корочка только с тех сторон, где был край пласта, остальные — срез.
    /// Пласт всегда даёт ровно Count X × Count Z кубиков. Последний распил — оба оставшихся
    /// выпадают кубиками, пласт исчезает.
    ///
    /// Резать можно только с края: кубик, у которого есть свободная сторона (край пласта или
    /// уже отпиленный сосед). Если пила не говорит, куда целится (бензопила), режет с дальнего
    /// края по X.
    ///
    /// ═══ КТО РЕЖЕТ ═══
    ///   Ручная пила (SawFoodProcessor) — по прицелу; бензопила — ICuttable;
    ///   ножи/зоны FoodTool с процессом «Резать» — IFoodWorkHandler.
    ///   Нужна строчка таблицы «Бисквит: Запечённое + Резать → Кусочки» (Десерты → 1).
    ///
    /// ═══ НАСТРОЙКА (делает меню Десерты → 4) ═══
    ///   Корень: NetworkObject, NetworkTransform, Rigidbody, FoodPiece, этот компонент,
    ///   MeshFilter + MeshRenderer (Mat_SpongeChoc). Коллайдеры клеток создаются сами.
    ///   Piece Prefab — SpongeCube (FoodPiece + SpongePiece), в Spawnable Prefabs.
    /// </summary>
    [RequireComponent(typeof(FoodPiece))]
    public sealed class FoodGridSlab : NetworkBehaviour, ICuttable, ICuttableAim, IFoodWorkHandler
    {
        [Header("Сетка")]
        [Tooltip("Кубиков по X (вдоль) и по Z (поперёк)")]
        [Range(1, 30)][SerializeField] private int _countX = 10;
        [Range(1, 30)][SerializeField] private int _countZ = 3;
        [Tooltip("Размер одного кубика, в осях объекта (м при Scale 1): X, высота, Z")]
        [SerializeField] private Vector3 _cell = new Vector3(0.6f, 0.3f, 0.6f);
        [Tooltip("Подушка корочки сверху — доля высоты")]
        [Range(0f, 0.2f)][SerializeField] private float _topBulge = 0.05f;
        [Tooltip("Детализация верха: делений на кубик")]
        [Range(1, 4)][SerializeField] private int _detail = 2;

        [Header("Кубик")]
        [Tooltip("Префаб одного кубика: FoodPiece + SpongePiece (размер и корочки ставятся сами)")]
        [SerializeField] private GameObject _piecePrefab;
        [Tooltip("Как быстро отрезанный кубик отъезжает от пласта, м/с")]
        [SerializeField] private float _pieceSlide = 0.35f;

        [Header("Работа")]
        [SerializeField] private FoodProcess _process = FoodProcess.Cut;
        [Tooltip("Для ножей/зон FoodTool: секунд работы на кубик. Пила берёт своё время.")]
        [Min(0.05f)][SerializeField] private float _secondsPerSlice = 0.6f;
        [Tooltip("Держать пласт неподвижным, пока пилят")]
        [SerializeField] private bool _clampWhileCutting = true;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        // ───────── сеть ─────────
        private readonly SyncList<ushort> _removed = new SyncList<ushort>();
        private readonly SyncVar<ushort> _next = new SyncVar<ushort>(ushort.MaxValue);
        private readonly SyncVar<bool> _clamped = new SyncVar<bool>(false);

        private FoodPiece _food;
        private Rigidbody _rb;
        private bool _wasKinematic;
        private bool[] _present;
        private BoxCollider[] _cols;
        private Mesh _mesh;
        private float _work;
        private bool _warned;

        public int CellCount => _countX * _countZ;
        public int CellsLeft => CellCount - _removed.Count;
        public Vector3 CellSize => _cell;
        public int CountX => _countX;
        public int CountZ => _countZ;

        // ───────────────────────── ICuttable ─────────────────────────

        public bool CanCut
        {
            get
            {
                if (CellsLeft <= 1 || _food == null || _food.IsConsumed) return false;
                if (RuleAllows()) { _warned = false; return true; }
                if (!_warned && _log && _food != null)
                {
                    _warned = true;
                    Debug.Log($"[Пласт] «{name}» в состоянии «{FoodStateInfo.NameOf(_food.State)}» не режется: нет правила " +
                              $"«{FoodStateInfo.NameOf(_food.State)} + {FoodStateInfo.NameOf(_process)}» (Десерты → 1)", this);
                }
                return false;
            }
        }
        public float SecondsPerCut => 0f;
        public bool ClampWhileCutting => _clampWhileCutting;
        public string CutLabel => $"кубиков {CellsLeft} из {CellCount}";
        public bool IsClamped => _clamped.Value;
        public Color CrumbColor
        {
            get { ProductDef d = _food != null ? _food.Def : null; return d != null ? d.CrumbColor : SpongeMesh.CutCrumb; }
        }

        public Vector3 CutPoint()
        {
            int c = NextCell();
            if (c < 0) return transform.position;
            Vector3 lp = Center(c);
            // точка на верхнем крае свободной стороны — там летят крошки
            lp.y = _cell.y * 0.5f;
            return transform.TransformPoint(lp + FreeSideDir(c) * 0.45f);
        }

        public void ServerCutOne(ProcessDef ignored, float quality01) => ServerCut(quality01);

        // ───────────────────────── ЖИЗНЕННЫЙ ЦИКЛ ─────────────────────────

        private void Awake()
        {
            _food = GetComponent<FoodPiece>();
            _rb = GetComponent<Rigidbody>();
            _present = new bool[CellCount];
            for (int i = 0; i < _present.Length; i++) _present[i] = true;
            MakeColliders();
            Rebuild();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (CellCount > 1) _food.ServerSetCount(CellsLeft);
            if (_food.ProductId != 0 && _piecePrefab != null) SpongePiece.RegisterPrefab(_food.ProductId, _piecePrefab);
            if (_log) Debug.Log($"[Пласт] «{name}»: {_countX}×{_countZ} = {CellCount} кубиков по {_cell.x:0.00}×{_cell.y:0.00}×{_cell.z:0.00} м" +
                                (_piecePrefab == null ? " ⚠ Piece Prefab пуст — кубики не появятся" : ""), this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // мастерку на клиенте тоже нужно знать, что этот продукт — кубик
            if (_food.ProductId != 0 && _piecePrefab != null) SpongePiece.RegisterPrefab(_food.ProductId, _piecePrefab);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _removed.OnChange += OnRemovedChanged;
            _clamped.OnChange += OnClampChanged;
            ApplyRemoved();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _removed.OnChange -= OnRemovedChanged;
            _clamped.OnChange -= OnClampChanged;
        }

        private void OnDestroy()
        {
            if (_mesh != null) SafeDestroy.Kill(_mesh);
        }

        private void OnRemovedChanged(SyncListOperation op, int index, ushort oldItem, ushort newItem, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            ApplyRemoved();
        }

        private void ApplyRemoved()
        {
            for (int i = 0; i < _present.Length; i++) _present[i] = true;
            for (int i = 0; i < _removed.Count; i++) if (_removed[i] < _present.Length) _present[_removed[i]] = false;
            for (int i = 0; i < _cols.Length; i++) if (_cols[i] != null) _cols[i].enabled = _present[i];
            Rebuild();
        }

        private void Rebuild()
        {
            if (_mesh == null) { _mesh = new Mesh(); _mesh.hideFlags = HideFlags.DontSave; }
            SpongeMesh.BuildSlab(_mesh, _countX, _countZ, _cell, _present, _topBulge, _detail);
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = _mesh;
        }

        /// <summary>Коллайдер на каждую клетку (дети Cells/Cell_i): отпилил — пропал и коллайдер.
        /// Меню делает их заранее, в префабе, — тогда их видит Pickup Item (выключает в руках).</summary>
        private void MakeColliders()
        {
            foreach (Collider c in GetComponents<Collider>()) if (c is BoxCollider) c.enabled = false;
            Transform holder = transform.Find("Cells");
            _cols = new BoxCollider[CellCount];
            bool ok = holder != null && holder.childCount == CellCount;
            if (ok)
                for (int i = 0; i < CellCount; i++)
                {
                    Transform t = holder.Find("Cell_" + i);
                    _cols[i] = t != null ? t.GetComponent<BoxCollider>() : null;
                    if (_cols[i] == null) { ok = false; break; }
                    t.localPosition = Center(i);
                    _cols[i].size = _cell;
                    _cols[i].center = Vector3.zero;
                }
            if (ok) return;
            if (holder != null)
            {
                if (Application.isPlaying) Destroy(holder.gameObject); else DestroyImmediate(holder.gameObject);
                if (Application.isPlaying && _log) Debug.LogWarning($"[Пласт] «{name}»: Cells не совпадают с сеткой — пересоздаю (сделай пласт меню 4 заново)", this);
            }
            holder = new GameObject("Cells").transform;
            holder.SetParent(transform, false);
            holder.gameObject.layer = gameObject.layer;
            for (int i = 0; i < CellCount; i++)
            {
                var go = new GameObject("Cell_" + i);
                go.layer = gameObject.layer;
                go.transform.SetParent(holder, false);
                go.transform.localPosition = Center(i);
                BoxCollider bc = go.AddComponent<BoxCollider>();
                bc.size = _cell;
                _cols[i] = bc;
            }
        }

        /// <summary>Pickup Item при «положили» включает все свои коллайдеры — отпиленные гасим обратно.</summary>
        private void LateUpdate()
        {
            if (_cols == null || _present == null) return;
            for (int i = 0; i < _cols.Length; i++)
                if (_cols[i] != null && !_present[i] && _cols[i].enabled) _cols[i].enabled = false;
        }

        // ───────────────────────── ПРИЦЕЛ ─────────────────────────

        public void ServerAimAt(Vector3 worldPoint)
        {
            if (!base.IsServerInitialized) return;
            int best = CellAt(worldPoint);
            if (best >= 0 && _next.Value != best) _next.Value = (ushort)best;
        }

        /// <summary>Кубик под прицелом: ближайший к точке из тех, что можно отпилить (есть свободная сторона).
        /// Одинаково на сервере и у клиентов — рамка-подсказка показывает ровно тот кубик, что отрежется.</summary>
        public int CellAt(Vector3 worldPoint)
        {
            if (_present == null) return -1;
            Vector3 lp = transform.InverseTransformPoint(worldPoint);
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < CellCount; i++)
            {
                if (!Present(i) || !Exposed(i)) continue;
                Vector3 c = Center(i);
                float d = (c.x - lp.x) * (c.x - lp.x) + (c.z - lp.z) * (c.z - lp.z);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        public bool AimBox(Vector3 worldPoint, Vector3[] corners)
        {
            if (corners == null || corners.Length < 8 || CellsLeft <= 1) return false;
            int i = CellAt(worldPoint);
            if (i < 0) return false;
            Vector3 c = Center(i);
            float hx = _cell.x * 0.52f, hz = _cell.z * 0.52f;
            float y0 = -_cell.y * 0.5f - 0.004f, y1 = _cell.y * (0.5f + _topBulge) + 0.006f;
            for (int k = 0; k < 2; k++)
            {
                float y = k == 0 ? y0 : y1;
                corners[k * 4 + 0] = transform.TransformPoint(new Vector3(c.x - hx, y, c.z - hz));
                corners[k * 4 + 1] = transform.TransformPoint(new Vector3(c.x + hx, y, c.z - hz));
                corners[k * 4 + 2] = transform.TransformPoint(new Vector3(c.x + hx, y, c.z + hz));
                corners[k * 4 + 3] = transform.TransformPoint(new Vector3(c.x - hx, y, c.z + hz));
            }
            return true;
        }

        private int NextCell()
        {
            int n = _next.Value;
            if (n < CellCount && Present(n) && Exposed(n)) return n;
            // по умолчанию — с дальнего края по X
            for (int ix = _countX - 1; ix >= 0; ix--)
                for (int iz = _countZ - 1; iz >= 0; iz--)
                {
                    int i = ix + iz * _countX;
                    if (Present(i) && Exposed(i)) return i;
                }
            return -1;
        }

        private bool Present(int i) => i >= 0 && i < CellCount && (_present == null || _present[i]);

        private bool Exposed(int i)
        {
            int ix = i % _countX, iz = i / _countX;
            return !HasCell(ix - 1, iz) || !HasCell(ix + 1, iz) || !HasCell(ix, iz - 1) || !HasCell(ix, iz + 1);
        }

        private bool HasCell(int ix, int iz) => ix >= 0 && iz >= 0 && ix < _countX && iz < _countZ && Present(ix + iz * _countX);

        private Vector3 Center(int i) => SpongeMesh.CellCenter(_countX, _countZ, _cell, i % _countX, i / _countX);

        /// <summary>В какую сторону у клетки свободно (куда кубик отъедет).</summary>
        private Vector3 FreeSideDir(int i)
        {
            int ix = i % _countX, iz = i / _countX;
            Vector3 d = Vector3.zero;
            if (!HasCell(ix - 1, iz)) d.x -= _cell.x;
            if (!HasCell(ix + 1, iz)) d.x += _cell.x;
            if (!HasCell(ix, iz - 1)) d.z -= _cell.z;
            if (!HasCell(ix, iz + 1)) d.z += _cell.z;
            return d;
        }

        /// <summary>Какие стороны кубика — корочка (край пласта), остальные — срез.</summary>
        private byte CrustMask(int i)
        {
            int ix = i % _countX, iz = i / _countX;
            byte m = 0;
            if (ix == 0) m |= SpongeMesh.XNeg;
            if (ix == _countX - 1) m |= SpongeMesh.XPos;
            if (iz == 0) m |= SpongeMesh.ZNeg;
            if (iz == _countZ - 1) m |= SpongeMesh.ZPos;
            return m;
        }

        // ───────────────────────── IFoodWorkHandler ─────────────────────────

        public bool ServerHandleWork(FoodPiece piece, FoodProcess process, float seconds, float quality01)
        {
            if (process != _process || CellCount < 2) return false;
            if (!CanCut) return true;
            _work += seconds;
            while (_work >= _secondsPerSlice && CanCut)
            {
                _work -= _secondsPerSlice;
                ServerCut(quality01);
                if (!base.IsSpawned) break;
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
        private void ServerCut(float quality01)
        {
            if (!CanCut) return;
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null || !db.Table.TryGet(_food.ProductId, _food.State, _process, out FoodTransition t)) return;
            if (_piecePrefab == null) { Debug.LogError($"[Пласт] «{name}»: не задан Piece Prefab (SpongeCube)", this); return; }
            SpongePiece.RegisterPrefab(_food.ProductId, _piecePrefab);

            int cell = NextCell();
            if (cell < 0) return;
            byte mask = CrustMask(cell);
            Vector3 away = FreeSideDir(cell);
            _removed.Add((ushort)cell);
            _present[cell] = false;                                   // сервер знает сразу (выбор следующей)
            if (_cols[cell] != null) _cols[cell].enabled = false;      // кубик появится на этом месте
            SpawnPiece(cell, mask, away, t.To, quality01);
            _next.Value = ushort.MaxValue;

            if (CellsLeft == 1)
            {
                int last = -1;
                for (int i = 0; i < CellCount; i++) if (Present(i)) { last = i; break; }
                if (last >= 0) SpawnPiece(last, CrustMask(last), FreeSideDir(last), t.To, quality01);
                if (_log) Debug.Log($"[Пласт] «{name}» разрезан полностью — {CellCount} кубиков", this);
                _food.ServerTryConsume();
                base.NetworkObject.RemoveOwnership();
                base.Despawn(base.NetworkObject);
                return;
            }
            _food.ServerSetCount(CellsLeft);
            if (_log) Debug.Log($"[Пласт] «{name}» — отрезан кубик {cell % _countX + 1}×{cell / _countX + 1}, {CutLabel}", this);
        }

        [Server]
        private void SpawnPiece(int cell, byte crust, Vector3 awayLocal, FoodState state, float quality01)
        {
            Vector3 lp = Center(cell);
            Vector3 at = transform.TransformPoint(lp + Vector3.up * 0.002f);
            GameObject go = Instantiate(_piecePrefab, at, transform.rotation);
            Vector3 ls = transform.lossyScale;
            ls = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            FoodPiece piece = go.GetComponent<FoodPiece>();
            if (piece == null)
            {
                Debug.LogError($"[Пласт] на «{_piecePrefab.name}» нет FoodPiece", _piecePrefab);
                Destroy(go);
                return;
            }
            SpongePiece sp = go.GetComponent<SpongePiece>();
            if (sp != null)
            {
                go.transform.localScale = ls;                          // меш куска — в метрах пласта
                sp.ServerSetup(_cell, crust);
            }
            else go.transform.localScale = Vector3.Scale(_cell, ls);   // обычный единичный кубик

            byte form = (byte)Mathf.RoundToInt(_food.FormScore * Mathf.Clamp01(quality01 <= 0f ? 1f : quality01));
            piece.ServerPreset(_food.Def, state, _food.Clean, _food.Cook, form);
            base.Spawn(go);

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // мягко отъезжает в свободную сторону, без подброса
                Vector3 dir = transform.TransformDirection(awayLocal);
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-6f) dir = transform.right;
                rb.linearVelocity = dir.normalized * _pieceSlide + Vector3.up * 0.15f;
                rb.angularVelocity = Vector3.Cross(Vector3.up, dir.normalized) * 0.6f;
            }
        }

        private bool RuleAllows()
        {
            FoodDatabase db = FoodDatabase.Instance;
            return db != null && _food != null && db.CanApply(_food.ProductId, _food.State, _process);
        }

#if UNITY_EDITOR
        /// <summary>Для меню: задать сетку.</summary>
        public void EditorSetup(int countX, int countZ, Vector3 cell, GameObject piecePrefab)
        {
            _countX = Mathf.Clamp(countX, 1, 30); _countZ = Mathf.Clamp(countZ, 1, 30); _cell = cell;
            if (piecePrefab != null) _piecePrefab = piecePrefab;
            _present = null;
            MakeColliders();                         // клетки-коллайдеры в префаб
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
