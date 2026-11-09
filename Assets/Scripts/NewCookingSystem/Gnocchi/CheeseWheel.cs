using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ГОЛОВКА СЫРА — пилится ПИЛОЙ или БЕНЗОПИЛОЙ на секторы (дольки).
    ///
    /// Пилишь головку — от неё отваливается сектор (по кругу, один за другим), головка остаётся
    /// «тортом с вырезом». Последний распил — оба оставшихся сектора выпадают дольками, головки нет.
    /// Каждая долька — обычный Food Piece «Сыр твёрдый» (сырое, Portions порций): в руку (F) —
    /// и в измельчитель → тёртый сыр.
    ///
    /// Вид — процедурный меш (сам считает вырез), корка — своим материалом. Меш, коллайдер и вырез —
    /// у всех одинаковые (SyncVar «сколько отпилено»).
    ///
    /// ═══ ТРЕБУЕТ ═══ NetworkObject, Rigidbody, MeshFilter, MeshRenderer, MeshCollider (выпуклый).
    /// Wedge Prefab — префаб дольки (меню Ньокки → 10б делает заготовку, 10д назначает префаб).
    /// ═══ НАСТРОЙКА ═══ CatCooks → Ньокки → 10г.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class CheeseWheel : NetworkBehaviour, ICuttable
    {
        [Header("Головка")]
        [Tooltip("Радиус, м (как у дольки — меню ставит само)")]
        [SerializeField] private float _radius = 0.5f;
        [Tooltip("Высота, м")]
        [SerializeField] private float _height = 0.32f;
        [Range(2, 12)][SerializeField] private int _sectors = 6;

        [Header("Долька")]
        [Tooltip("Префаб дольки: Food Piece «Сыр твёрдый» с мешем-сектором (меню 10б → префаб → 10д)")]
        [SerializeField] private GameObject _wedgePrefab;
        [SerializeField] private ProductDef _product;
        [Tooltip("Порций сыра в одной дольке (тёртого из неё выйдет столько же)")]
        [Range(1, 20)][SerializeField] private int _portions = 3;

        [Header("Распил")]
        [Tooltip("Сколько пилить один сектор, с")]
        [SerializeField] private float _secondsPerCut = 1.2f;
        [SerializeField] private float _impulse = 0.6f;

        [Header("Вид")]
        [SerializeField] private Material _cheeseMat;
        [SerializeField] private Material _rindMat;
        [SerializeField] private bool _log = true;

        private readonly SyncVar<byte> _cut = new SyncVar<byte>(0);
        private readonly SyncVar<bool> _clamped = new SyncVar<bool>(false);

        private Mesh _mesh;
        private MeshCollider _mc;
        private Rigidbody _rb;
        private bool _wasKinematic;
        private int _shown = -1;

        public int Left => Mathf.Max(0, _sectors - _cut.Value);
        private float Step => 360f / Mathf.Max(2, _sectors);

        // ── ICuttable ──
        public bool CanCut => Left > 0 && _wedgePrefab != null;
        public float SecondsPerCut => _secondsPerCut;
        public bool ClampWhileCutting => true;
        public string CutLabel => $"головка сыра: долек {Left} из {_sectors}";
        public Color CrumbColor => _product != null ? _product.BaseColor : new Color(0.97f, 0.85f, 0.45f);

        /// <summary>Пропил — по шву следующего сектора, на середине радиуса, сверху.</summary>
        public Vector3 CutPoint() => transform.TransformPoint(Dir(_cut.Value * Step) * _radius * 0.55f + Vector3.up * _height);

        public void ServerSetClamped(bool value) { if (base.IsServerInitialized && _clamped.Value != value) _clamped.Value = value; }

        public void ServerCutOne(ProcessDef cutProcess, float quality01)
        {
            if (!base.IsServerInitialized || !CanCut) return;
            int k = _cut.Value;
            int drop = Left <= 2 ? Left : 1;            // последний распил — выпадают обе оставшиеся дольки
            for (int i = 0; i < drop; i++) SpawnWedge(k + i);
            _cut.Value = (byte)Mathf.Min(_sectors, k + drop);
            if (_log) Debug.Log($"[Сыр] «{name}»: отпилен сектор — {CutLabel}", this);
            if (Left <= 0)
            {
                if (_log) Debug.Log($"[Сыр] «{name}»: головка распилена целиком — {_sectors} долек", this);
                base.NetworkObject.RemoveOwnership();
                base.Despawn(base.NetworkObject);
            }
        }

        private void SpawnWedge(int k)
        {
            float mid = (k + 0.5f) * Step;
            Vector3 outDir = transform.TransformDirection(Dir(mid));
            Vector3 at = transform.TransformPoint(Vector3.up * (_height * 0.5f)) + outDir * 0.04f;
            Quaternion rot = transform.rotation * Quaternion.Euler(0f, mid, 0f);
            GameObject go = Instantiate(_wedgePrefab, at, rot);
            FoodPiece fp = go.GetComponent<FoodPiece>();
            if (fp != null) fp.ServerPreset(_product, FoodState.Raw, 100, 0, 100);
            else Debug.LogWarning($"[Сыр] «{name}»: у префаба дольки нет Food Piece — в измельчитель не пойдёт", this);
            base.Spawn(go);
            if (fp != null) fp.ServerSetCount(_portions);
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.AddForce((outDir + Vector3.up * 0.35f).normalized * _impulse, ForceMode.Impulse);
        }

        // ── жизнь ──
        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _mc = GetComponent<MeshCollider>();
            Rebuild();
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _cut.OnChange += OnCut;
            _clamped.OnChange += OnClamp;
            Rebuild();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _cut.OnChange -= OnCut;
            _clamped.OnChange -= OnClamp;
        }

        private void OnDestroy() { if (_mesh != null) Destroy(_mesh); }

        private void OnCut(byte a, byte b, bool asServer) { if (asServer && base.IsClientInitialized) return; Rebuild(); }

        private void OnClamp(bool a, bool b, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            if (_rb == null) return;
            if (b) { _wasKinematic = _rb.isKinematic; _rb.isKinematic = true; }
            else { _rb.isKinematic = _wasKinematic; _rb.WakeUp(); }
        }

        /// <summary>Меш головки с вырезом: оставшиеся секторы [cut·step .. 360°].</summary>
        private void Rebuild()
        {
            int left = Left;
            if (left == _shown && _mesh != null) return;
            _shown = left;
            if (left <= 0) return;
            if (_mesh == null) _mesh = new Mesh { name = "CheeseWheel (runtime)" };
            float a0 = _cut.Value * Step, a1 = 360f;
            Fill(_mesh, _radius, _height, a0, a1, false);
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            MeshRenderer mr = GetComponent<MeshRenderer>();
            if (_cheeseMat != null) mr.sharedMaterials = new[] { _cheeseMat, _rindMat != null ? _rindMat : _cheeseMat };
            if (_mc != null) { _mc.sharedMesh = null; _mc.convex = true; _mc.sharedMesh = _mesh; }
            if (_rb != null && left < _sectors) _rb.mass = Mathf.Max(0.5f, _rb.mass * left / (left + 1f));
        }

        private static Vector3 Dir(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)); }

        /// <summary>
        /// Сектор цилиндра от a0 до a1 градусов (0° — +Z, по часовой сверху). Подмеш 0 — мякиш (верх, низ, срезы),
        /// подмеш 1 — корка (дуга). centered: Y от −H/2 до H/2 (долька), иначе от 0 до H (головка на земле).
        /// Нормали — явные, обход граней — по ним (корректно и для выреза больше 180°).
        /// </summary>
        public static void Fill(Mesh m, float R, float H, float a0, float a1, bool centered)
        {
            float y0 = centered ? -H * 0.5f : 0f, y1 = y0 + H;
            int seg = Mathf.Max(2, Mathf.CeilToInt((a1 - a0) / 7.5f));
            var b = new MeshBuild();
            bool full = a1 - a0 >= 359.9f;

            // верх и низ — веер от центра
            for (int side = 0; side < 2; side++)
            {
                float y = side == 0 ? y1 : y0;
                Vector3 nr = side == 0 ? Vector3.up : Vector3.down;
                int c = b.V(new Vector3(0f, y, 0f), nr, new Vector2(0.5f, 0.5f));
                int first = b.Count;
                for (int i = 0; i <= seg; i++)
                {
                    Vector3 d = Dir(Mathf.Lerp(a0, a1, i / (float)seg));
                    b.V(d * R + Vector3.up * y, nr, new Vector2(0.5f + d.x * 0.5f, 0.5f + d.z * 0.5f));
                }
                for (int i = 0; i < seg; i++) b.Tri(b.Inner, c, first + i, first + i + 1);
            }
            // корка — дуга
            int arc = b.Count;
            for (int i = 0; i <= seg; i++)
            {
                float t = i / (float)seg;
                Vector3 d = Dir(Mathf.Lerp(a0, a1, t));
                b.V(d * R + Vector3.up * y0, d, new Vector2(t * (a1 - a0) / 60f, 0f));
                b.V(d * R + Vector3.up * y1, d, new Vector2(t * (a1 - a0) / 60f, 1f));
            }
            for (int i = 0; i < seg; i++)
            {
                int p = arc + i * 2;
                b.Tri(b.Rind, p, p + 1, p + 2); b.Tri(b.Rind, p + 1, p + 3, p + 2);
            }
            // срезы (если не целый круг)
            if (!full)
            {
                for (int s = 0; s < 2; s++)
                {
                    float a = s == 0 ? a0 : a1;
                    Vector3 d = Dir(a);
                    Vector3 tang = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, -Mathf.Sin(a * Mathf.Deg2Rad));
                    Vector3 nr = s == 0 ? -tang : tang;
                    int q0 = b.V(new Vector3(0f, y0, 0f), nr, new Vector2(0f, 0f));
                    int q1 = b.V(new Vector3(0f, y1, 0f), nr, new Vector2(0f, 1f));
                    int q2 = b.V(d * R + Vector3.up * y1, nr, new Vector2(1f, 1f));
                    int q3 = b.V(d * R + Vector3.up * y0, nr, new Vector2(1f, 0f));
                    b.Tri(b.Inner, q0, q1, q2); b.Tri(b.Inner, q0, q2, q3);
                }
            }
            m.Clear();
            m.SetVertices(b.Pos); m.SetNormals(b.Nrm); m.SetUVs(0, b.Uv);
            m.subMeshCount = 2;
            m.SetTriangles(b.Inner, 0); m.SetTriangles(b.Rind, 1);
            m.RecalculateBounds();
        }

        /// <summary>Сборщик меша: вершины с явными нормалями, обход треугольника — по нормали.</summary>
        private sealed class MeshBuild
        {
            public readonly List<Vector3> Pos = new List<Vector3>(), Nrm = new List<Vector3>();
            public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<int> Inner = new List<int>(), Rind = new List<int>();
            public int Count => Pos.Count;
            public int V(Vector3 p, Vector3 n, Vector2 u) { Pos.Add(p); Nrm.Add(n); Uv.Add(u); return Pos.Count - 1; }
            public void Tri(List<int> t, int a, int b, int c)
            {
                Vector3 nn = Vector3.Cross(Pos[b] - Pos[a], Pos[c] - Pos[a]);
                if (Vector3.Dot(nn, Nrm[a] + Nrm[b] + Nrm[c]) < 0f) { t.Add(a); t.Add(c); t.Add(b); }
                else { t.Add(a); t.Add(b); t.Add(c); }
            }
        }

#if UNITY_EDITOR
        public void EditorSetup(GameObject wedgePrefab, ProductDef product, float radius, float height, Material cheese, Material rind)
        {
            UnityEditor.Undo.RecordObject(this, "Головка сыра");
            if (wedgePrefab != null) _wedgePrefab = wedgePrefab;
            if (product != null) _product = product;
            if (radius > 0f) _radius = radius;
            if (height > 0f) _height = height;
            if (cheese != null) _cheeseMat = cheese;
            if (rind != null) _rindMat = rind;
            UnityEditor.EditorUtility.SetDirty(this);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this)) UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }

        public GameObject WedgePrefab => _wedgePrefab;
#endif
    }
}
