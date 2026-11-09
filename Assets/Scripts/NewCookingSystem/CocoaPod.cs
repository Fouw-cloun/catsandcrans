using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// КАКАО-БОБ (стручок): две скорлупки + белая гроздь зёрен внутри.
    ///
    ///   1. Кувалда — боб трескается (правило продукта: Сырое/Мытое/Грязное → Разбивать → Отбито):
    ///      чуть сплющивается со «шмяком», по скорлупе проступают трещины (наклейка поверх модели —
    ///      тонкая полоска, повторяющая поверхность). Бить ещё раз — бесполезно, вдребезги не разлетится.
    ///   2. Бензопила по треснувшему — скорлупки отваливаются (падают, лежат и исчезают),
    ///      остаётся гроздь зёрен: сетевой префаб Pulp Prefab (Какао-зёрна, FoodRoastable).
    ///      Целый (не битый) боб пила не берёт.
    ///
    /// Вешается на КОРЕНЬ боба рядом с FoodPiece. Скорлупки и гроздь — дочерние меши:
    /// пусто в полях — найдутся по именам («Shell» — скорлупки, «Pulp» — гроздь).
    ///
    /// Сеть: что делать, решает сервер по состоянию FoodPiece. Трещины у всех одинаковые
    /// (зерно — Id объекта), сплющивание — по одному байту, раскрытие — один RPC;
    /// отпавшие скорлупки — чисто визуальные у каждого (по сети не ходят).
    /// </summary>
    [RequireComponent(typeof(FoodPiece))]
    public sealed class CocoaPod : NetworkBehaviour, ICuttable, IFoodWorkHandler
    {
        [Header("Модель")]
        [Tooltip("Скорлупки (половинки стручка). Пусто — дочерние с «Shell» в имени")]
        [SerializeField] private Transform[] _shells;
        [Tooltip("Белая гроздь зёрен внутри. Пусто — дочерний с «Pulp» в имени")]
        [SerializeField] private Transform _pulp;

        [Header("Удар кувалдой")]
        [Tooltip("Состояние «треснул» (правило продукта ставит его кувалдой)")]
        [SerializeField] private FoodState _crackedState = FoodState.Pounded;
        [Tooltip("Насколько сплющить по вертикали (0.88 — на 12% ниже)")]
        [Range(0.5f, 1f)][SerializeField] private float _flatHeight = 0.88f;
        [Tooltip("Насколько раздаётся вширь")]
        [Range(1f, 1.3f)][SerializeField] private float _flatWiden = 1.05f;
        [Range(0.05f, 0.6f)][SerializeField] private float _flatTime = 0.2f;

        [Header("Трещины (наклейка поверх)")]
        [Tooltip("Материал трещин. Пусто — копия материала скорлупы, почти чёрная")]
        [SerializeField] private Material _crackMaterial;
        [SerializeField] private Color _crackColor = new Color(0.07f, 0.035f, 0.02f, 1f);
        [Tooltip("Наибольшая ширина трещины (в самом толстом месте), доля длины боба")]
        [Range(0.005f, 0.06f)][SerializeField] private float _crackWidth = 0.022f;
        [Tooltip("Ответвлений на каждой скорлупке")]
        [Range(0, 8)][SerializeField] private int _branches = 3;

        [Header("Пила → зёрна")]
        [Tooltip("Сетевой префаб грозди зёрен (FoodPiece «Какао-зёрна» + FoodRoastable)")]
        [SerializeField] private GameObject _pulpPrefab;
        [Tooltip("Продукт грозди. Пусто — какой стоит в префабе")]
        [SerializeField] private ProductDef _pulpProduct;
        [SerializeField] private FoodState _pulpState = FoodState.Raw;
        [Tooltip("Сколько порций в грозди (= на одну партию рецепта «Тёртое какао» нужно 12)")]
        [Range(1, 60)][SerializeField] private int _pulpPortions = 12;
        [Tooltip("Гроздь появляется того же размера, что и внутренности боба (боб в сцене со Scale 5 — и гроздь станет такой же). " +
                 "Выкл — ровно в масштабе своего префаба")]
        [SerializeField] private bool _pulpMatchSize = true;
        [Tooltip("Сколько секунд пилить")]
        [Min(0.1f)][SerializeField] private float _sawSeconds = 1.2f;

        [Header("Скорлупки после пилы")]
        [Tooltip("Сколько лежат, с (потом уменьшаются и пропадают)")]
        [Min(0.5f)][SerializeField] private float _shellLifetime = 6f;
        [SerializeField] private float _shellKick = 1.2f;
        [Tooltip("Слой отпавших скорлупок (2 = Ignore Raycast: руками не берутся)")]
        [Range(0, 31)][SerializeField] private int _shellLayer = 2;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly SyncVar<byte> _flatAxis = new SyncVar<byte>(0);     // 0 — не сплющен, 1..3 — ось X/Y/Z корня
        private readonly SyncVar<bool> _clamped = new SyncVar<bool>(false);

        private FoodPiece _food;
        private Rigidbody _rb;
        private bool _wasKinematic;
        private Transform _pivot;
        private Vector3 _center;                 // центр боба в осях корня
        private Vector3 _seamN, _longAx, _wideAx; // оси в корне
        private float _len = 0.26f;
        private float _halfL = 0.13f, _halfW = 0.06f, _halfN = 0.06f;
        private readonly List<Matrix4x4> _rootFromShell = new List<Matrix4x4>();
        private readonly List<Vector3> _shellCenters = new List<Vector3>();
        private float _flatT = -1f;
        private bool _cracksBuilt, _opened, _hinted;
        private float _despawnAt = -1f;
        private readonly List<GameObject> _cracks = new List<GameObject>();

        // ───────────────────────── ЖИЗНЬ ─────────────────────────

        private void Awake()
        {
            _food = GetComponent<FoodPiece>();
            _rb = GetComponent<Rigidbody>();
            FindParts();
            BuildPivot();
            MeasureRest();
        }

        private void FindParts()
        {
            var shells = new List<Transform>();
            if (_shells != null) foreach (Transform t in _shells) if (t != null && !shells.Contains(t)) shells.Add(t);
            foreach (MeshRenderer mr in GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = mr.name.ToLowerInvariant();
                if (_shells == null || _shells.Length == 0)
                    if (n.Contains("shell") && !shells.Contains(mr.transform)) shells.Add(mr.transform);
                if (_pulp == null && n.Contains("pulp")) _pulp = mr.transform;
            }
            _shells = shells.ToArray();
            if (_shells.Length == 0)
                Debug.LogError($"[Какао-боб] «{name}»: не нашёл скорлупки — задай Shells (дочерние меши половинок)", this);
        }

        /// <summary>Скорлупки и гроздь — под общую «ось сплющивания» в центре боба.</summary>
        private void BuildPivot()
        {
            if (_shells.Length == 0) return;
            Bounds b = RootBounds();
            _pivot = new GameObject("PodModel (auto)").transform;
            _pivot.SetParent(transform, false);
            _pivot.localPosition = b.center;
            _pivot.localRotation = Quaternion.identity;
            _pivot.localScale = Vector3.one;
            foreach (Transform s in _shells) if (s != null && s != transform) s.SetParent(_pivot, true);
            if (_pulp != null && _pulp != transform) _pulp.SetParent(_pivot, true);
        }

        /// <summary>Центр, шов (между скорлупками), длинная ось — в осях корня, в покое.</summary>
        private void MeasureRest()
        {
            _rootFromShell.Clear();
            if (_shells.Length == 0) return;
            Matrix4x4 w2r = transform.worldToLocalMatrix;
            var centers = new List<Vector3>();
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (Transform s in _shells)
            {
                Matrix4x4 m = w2r * s.localToWorldMatrix;
                _rootFromShell.Add(m);
                Mesh mesh = MeshOf(s);
                Vector3 c = Vector3.zero; int n = 0;
                if (mesh != null && mesh.isReadable)
                {
                    foreach (Vector3 v in mesh.vertices)
                    {
                        Vector3 p = m.MultiplyPoint3x4(v);
                        c += p; n++;
                        min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                    }
                }
                else if (mesh != null)
                {
                    Bounds mb = mesh.bounds;
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 v = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                        Vector3 p = m.MultiplyPoint3x4(v);
                        c += p; n++;
                        min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                    }
                }
                centers.Add(n > 0 ? c / n : m.MultiplyPoint3x4(Vector3.zero));
            }
            _shellCenters.Clear();
            _shellCenters.AddRange(centers);
            _center = (min + max) * 0.5f;
            Vector3 size = max - min;

            // шов: от одной скорлупки к другой; одна скорлупка — самая тонкая сторона
            if (centers.Count >= 2 && (centers[1] - centers[0]).sqrMagnitude > 1e-8f) _seamN = (centers[1] - centers[0]).normalized;
            else _seamN = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;

            // длинная ось — самая длинная сторона, не совпадающая со швом
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            float best = -1f;
            foreach (Vector3 a in axes)
            {
                if (Mathf.Abs(Vector3.Dot(a, _seamN)) > 0.7f) continue;
                float e = Mathf.Abs(Vector3.Dot(size, a));
                if (e > best) { best = e; _longAx = a; }
            }
            _longAx = (_longAx - _seamN * Vector3.Dot(_longAx, _seamN)).normalized;
            _wideAx = Vector3.Cross(_seamN, _longAx).normalized;
            _len = Mathf.Max(0.01f, best);
            _halfL = _len * 0.5f;
            _halfW = Mathf.Max(0.005f, Mathf.Abs(Vector3.Dot(size, _wideAx)) * 0.5f);
            _halfN = Mathf.Max(0.005f, Mathf.Abs(Vector3.Dot(size, _seamN)) * 0.5f);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _flatAxis.OnChange += OnFlatChanged;
            _clamped.OnChange += OnClampChanged;
            ApplyFlat(1f);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _flatAxis.OnChange -= OnFlatChanged;
            _clamped.OnChange -= OnClampChanged;
        }

        private void OnFlatChanged(byte prev, byte next, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            _flatT = prev == 0 && next != 0 ? 0f : -1f;
            if (_flatT < 0f) ApplyFlat(1f);
        }

        private void OnClampChanged(bool prev, bool next, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            if (_rb == null) return;
            if (next) { _wasKinematic = _rb.isKinematic; _rb.isKinematic = true; }
            else { _rb.isKinematic = _wasKinematic; _rb.WakeUp(); }
        }

        private void Update()
        {
            if (_flatT >= 0f)
            {
                _flatT += Time.deltaTime / _flatTime;
                ApplyFlat(Mathf.Min(1f, _flatT));
                if (_flatT >= 1f) _flatT = -1f;
            }

            bool cracked = _food != null && _food.State == _crackedState;
            if (cracked && !_cracksBuilt && !_opened) BuildCracks();

            if (!base.IsServerInitialized || _food == null) return;
            if (_despawnAt > 0f)
            {
                if (Time.time >= _despawnAt) { _despawnAt = -1f; base.NetworkObject.RemoveOwnership(); base.Despawn(base.NetworkObject); }
                return;
            }
            if (_food.IsConsumed) return;
            if (cracked && _flatAxis.Value == 0)
            {
                _flatAxis.Value = UpAxis();
                if (_log) Debug.Log($"[Какао-боб] «{name}»: треснул — теперь бензопилой", this);
            }
        }

        // ───────────────────────── КУВАЛДА ─────────────────────────

        public bool ServerHandleWork(FoodPiece piece, FoodProcess process, float seconds, float quality01)
        {
            if (piece.State != _crackedState) return false;     // целый — работает правило (трескается)
            if (process != FoodProcess.Crack && process != FoodProcess.Pound) return false;
            if (_log && !_hinted) { _hinted = true; Debug.Log($"[Какао-боб] «{name}»: уже треснул — пили бензопилой", this); }
            return true;                                         // работу «съели»: вдребезги не разбивается
        }

        public void ServerStopWork(FoodPiece piece) { _hinted = false; }

        // ───────────────────────── ПИЛА ─────────────────────────

        public bool CanCut => !_opened && _food != null && !_food.IsConsumed && _food.State == _crackedState;
        public float SecondsPerCut => _sawSeconds;
        public bool ClampWhileCutting => false;
        public string CutLabel => _food != null && _food.State == _crackedState ? "какао-боб: раскрыть" : "какао-боб: сначала кувалдой";
        public Color CrumbColor
        {
            get
            {
                ProductDef d = _food != null ? _food.Def : null;
                return d != null ? d.CrumbColor : new Color(0.55f, 0.33f, 0.2f);
            }
        }
        public Vector3 CutPoint() => transform.TransformPoint(_center);

        [Server]
        public void ServerSetClamped(bool value) { if (_clamped.Value != value) _clamped.Value = value; }

        [Server]
        public void ServerCutOne(ProcessDef cutProcess, float quality01)
        {
            if (!CanCut) return;
            _opened = true;

            Vector3 at = _pulp != null ? _pulp.position : transform.TransformPoint(_center);
            Quaternion rot = _pulp != null ? _pulp.rotation : transform.rotation;
            if (_pulpPrefab == null)
                Debug.LogError($"[Какао-боб] «{name}»: не задан Pulp Prefab (гроздь зёрен) — зёрна не появятся", this);
            else
            {
                GameObject go = Instantiate(_pulpPrefab, at, rot);
                if (_pulpMatchSize) MatchSize(go);
                FoodPiece piece = go.GetComponent<FoodPiece>();
                if (piece != null) piece.ServerPreset(_pulpProduct, _pulpState, _food.Clean, 0, _food.FormScore);
                else Debug.LogWarning($"[Какао-боб] префаб «{_pulpPrefab.name}» без FoodPiece — его не бросить в тару", _pulpPrefab);
                base.Spawn(go);
                if (piece != null) piece.ServerSetCount(_pulpPortions);
                Rigidbody rb = go.GetComponent<Rigidbody>();
                if (rb != null) rb.AddForce(Vector3.up * 0.8f, ForceMode.VelocityChange);
            }
            RpcOpen();
            if (_log) Debug.Log($"[Какао-боб] «{name}»: распилен — скорлупа отпала, зёрна ×{_pulpPortions}", this);
            _food.ServerTryConsume();
            _despawnAt = Time.time + 0.3f;           // RPC успевает дойти, потом деспавн
        }

        /// <summary>Подогнать масштаб заспавненной грозди под внутренности боба (по размеру меша в мире).</summary>
        private void MatchSize(GameObject go)
        {
            if (_pulp == null) return;
            Mesh inner = MeshOf(_pulp);
            MeshFilter mf = go.GetComponentInChildren<MeshFilter>(true);
            if (inner == null || mf == null || mf.sharedMesh == null) return;
            float want = Vector3.Scale(inner.bounds.size, Abs(_pulp.lossyScale)).magnitude;
            float have = Vector3.Scale(mf.sharedMesh.bounds.size, Abs(mf.transform.lossyScale)).magnitude;
            if (want < 1e-5f || have < 1e-5f) return;
            float k = want / have;
            if (Mathf.Abs(k - 1f) < 0.02f) return;
            go.transform.localScale *= k;
            if (_log) Debug.Log($"[Какао-боб] гроздь «{go.name}» ×{k:0.##} по размеру внутренностей (масштаб {go.transform.localScale.x:0.##})", this);
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        [ObserversRpc]
        private void RpcOpen()
        {
            _opened = true;
            DropShells();
        }

        /// <summary>Скорлупки — копиями в мир: падают, лежат и исчезают. Оригинал прячется.</summary>
        private void DropShells()
        {
            Vector3 seamW = transform.TransformDirection(_seamN).normalized;
            Vector3 c = transform.TransformPoint(_center);
            foreach (Transform s in _shells)
            {
                if (s == null) continue;
                GameObject copy = Instantiate(s.gameObject, s.position, s.rotation);
                copy.name = s.name + " (скорлупа)";
                copy.transform.localScale = s.lossyScale;
                SetLayer(copy.transform, _shellLayer);
                foreach (MeshCollider mc in copy.GetComponentsInChildren<MeshCollider>(true)) mc.convex = true;
                if (copy.GetComponentInChildren<Collider>() == null)
                {
                    Mesh m = MeshOf(copy.transform);
                    if (m != null && m.isReadable) { MeshCollider mc = copy.AddComponent<MeshCollider>(); mc.sharedMesh = m; mc.convex = true; }
                    else copy.AddComponent<BoxCollider>();
                }
                Rigidbody rb = copy.GetComponent<Rigidbody>();
                if (rb == null) rb = copy.AddComponent<Rigidbody>();
                rb.mass = 0.1f;
                rb.isKinematic = false;
                Vector3 side = Vector3.Dot(s.position - c, seamW) >= 0f ? seamW : -seamW;
                rb.AddForce((side + Vector3.up * 0.6f + Random.insideUnitSphere * 0.2f).normalized * _shellKick, ForceMode.VelocityChange);
                rb.AddTorque(Random.insideUnitSphere * 3f, ForceMode.VelocityChange);
                copy.AddComponent<CocoaShellFade>().Begin(_shellLifetime);
            }
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            Color crumb = CrumbColor;
            CCRuntimeParticles.Burst(c, Vector3.up, crumb, crumb * 0.8f, 18, CCRuntimeParticles.Look.Chips.Scaled(Mathf.Max(1f, _len * transform.lossyScale.x / 0.1f)));
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        // ───────────────────────── СПЛЮЩИВАНИЕ ─────────────────────────

        /// <summary>Какая ось корня сейчас ближе к вертикали — по ней сплющиваем.</summary>
        private byte UpAxis()
        {
            float x = Mathf.Abs(Vector3.Dot(transform.right, Vector3.up));
            float y = Mathf.Abs(Vector3.Dot(transform.up, Vector3.up));
            float z = Mathf.Abs(Vector3.Dot(transform.forward, Vector3.up));
            return (byte)(x >= y && x >= z ? 1 : y >= z ? 2 : 3);
        }

        private void ApplyFlat(float k)
        {
            if (_pivot == null) return;
            byte ax = _flatAxis.Value;
            if (ax == 0) { _pivot.localScale = Vector3.one; return; }
            float e = k >= 1f ? 1f : 1f + Mathf.Sin(k * Mathf.PI) * 0.35f;
            float h = Mathf.Lerp(1f, _flatHeight, Mathf.Clamp01(k * 1.6f)) / Mathf.Lerp(1f, e, 0.3f);
            float w = Mathf.Lerp(1f, _flatWiden, Mathf.Clamp01(k * 1.6f)) * Mathf.Lerp(1f, e, 0.1f);
            Vector3 m = new Vector3(w, w, w);
            m[ax - 1] = h;
            _pivot.localScale = m;
        }

        // ───────────────────────── ТРЕЩИНЫ ─────────────────────────

        /// <summary>Трещина на развёртке скорлупки: точки (t — вдоль боба, w — поперёк, −1..1) и ширина в каждой (0..1).</summary>
        private sealed class CrackPath
        {
            public readonly List<Vector2> P = new List<Vector2>();
            public readonly List<float> W = new List<float>();
        }

        private static float Rand(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        /// <summary>
        /// Мультяшная трещина: ломаная с плавными изгибами и редкими резкими изломами,
        /// толщина «гуляет» (вздутия), к концам — острые иголки. Ответвления тоньше
        /// и тоже заостряются, у некоторых — своя веточка.
        /// </summary>
        private static CrackPath Walk(System.Random r, Vector2 start, float heading, float length, float wStart, float wEnd, bool taperStart, float step)
        {
            var c = new CrackPath();
            int n = Mathf.Max(3, Mathf.CeilToInt(length / step));
            Vector2 p = start;
            float dev = 0f, devVel = 0f, kink = 0f;
            float bump = Rand(r, 0f, 6.28f), bumpF = Rand(r, 1.5f, 3f);
            for (int i = 0; i <= n; i++)
            {
                float u = i / (float)n;
                // толщина: от wStart к wEnd, вздутия, к концу (и к началу, если надо) — иголкой
                float tipEnd = Mathf.Pow(Mathf.Clamp01((1f - u) / 0.25f), 0.8f);
                float tipStart = taperStart ? Mathf.Pow(Mathf.Clamp01(u / 0.2f), 0.8f) : 1f;
                float body = Mathf.Lerp(wStart, wEnd, u) * (0.78f + 0.3f * Mathf.Sin(bump + u * bumpF * 6.28f));
                c.P.Add(p); c.W.Add(Mathf.Max(0f, body * tipEnd * tipStart));
                // направление: держится общего курса, плавно виляет, иногда резкий излом (который потом выпрямляется)
                devVel = devVel * 0.75f + Rand(r, -0.2f, 0.2f) - dev * 0.08f;
                dev = Mathf.Clamp(dev + devVel, -0.85f, 0.85f);
                if (r.NextDouble() < 0.1) kink = Rand(r, -0.8f, 0.8f);
                kink *= 0.55f;
                float h = heading + dev + kink;
                p += new Vector2(Mathf.Cos(h), Mathf.Sin(h)) * step;
                p.y = Mathf.Clamp(p.y, -0.8f, 0.8f);
                p.x = Mathf.Clamp(p.x, -0.93f, 0.93f);
            }
            return c;
        }

        private void BuildCracks()
        {
            _cracksBuilt = true;
            if (_shells.Length == 0 || _rootFromShell.Count != _shells.Length) return;
            int seed = base.NetworkObject != null ? base.NetworkObject.ObjectId * 7919 + 17 : 17;
            var rnd = new System.Random(seed);
            const float step = 0.035f;          // шаг вдоль трещины (в долях полуоси): часто, чтобы лечь в рёбра
            for (int i = 0; i < _shells.Length; i++)
            {
                Transform s = _shells[i];
                if (s == null) continue;
                Mesh mesh = MeshOf(s);
                if (mesh == null) continue;
                Matrix4x4 r2s = _rootFromShell[i].inverse;
                Vector3[] verts = mesh.isReadable ? mesh.vertices : null;
                int[] tris = mesh.isReadable ? mesh.triangles : null;
                // какой стороной шва эта скорлупка
                Vector3 sc = i < _shellCenters.Count ? _shellCenters[i] : _rootFromShell[i].MultiplyPoint3x4(mesh.bounds.center);
                float side = Vector3.Dot(sc - _center, _seamN) >= 0f ? 1f : -1f;
                if (Mathf.Abs(Vector3.Dot(sc - _center, _seamN)) < 1e-5f) side = i == 0 ? -1f : 1f;

                var paths = new List<CrackPath>();
                // главная: от одного конца боба к другому, толстая посередине
                float dirSign = rnd.NextDouble() < 0.5 ? 1f : -1f;
                Vector2 st = new Vector2(-0.78f * dirSign, Rand(rnd, -0.25f, 0.25f));
                CrackPath main = Walk(rnd, st, dirSign > 0 ? Rand(rnd, -0.2f, 0.2f) : Mathf.PI + Rand(rnd, -0.2f, 0.2f),
                                      1.5f, 1f, 0.9f, true, step);
                paths.Add(main);
                // ответвления: от главной в стороны, тоньше, остриём
                for (int b = 0; b < _branches; b++)
                {
                    int at = Mathf.Clamp(Mathf.RoundToInt(main.P.Count * Rand(rnd, 0.2f, 0.8f)), 1, main.P.Count - 2);
                    Vector2 d = main.P[at + 1] - main.P[at - 1];
                    float h = Mathf.Atan2(d.y, d.x) + (b % 2 == 0 ? -1f : 1f) * Rand(rnd, 0.9f, 1.5f);   // поочерёдно в разные стороны
                    float w0 = main.W[at] * Rand(rnd, 0.55f, 0.75f);
                    CrackPath br = Walk(rnd, main.P[at], h, Rand(rnd, 0.35f, 0.6f), w0, w0 * 0.6f, false, step);
                    paths.Add(br);
                    if (rnd.NextDouble() < 0.55 && br.P.Count > 6)        // веточка от ветки
                    {
                        int k = br.P.Count / 2;
                        Vector2 d2 = br.P[k + 1] - br.P[k - 1];
                        float h2 = Mathf.Atan2(d2.y, d2.x) + (rnd.NextDouble() < 0.5 ? -1f : 1f) * Rand(rnd, 0.6f, 1.1f);
                        paths.Add(Walk(rnd, br.P[k], h2, Rand(rnd, 0.15f, 0.28f), br.W[k] * 0.7f, br.W[k] * 0.4f, false, step));
                    }
                }
                // иногда — отдельная маленькая трещинка с одной веточкой (как на картинке справа)
                if (rnd.NextDouble() < 0.6)
                {
                    Vector2 st2 = new Vector2(Rand(rnd, -0.5f, 0.5f), Rand(rnd, -0.55f, 0.55f));
                    CrackPath small = Walk(rnd, st2, Rand(rnd, 0f, 6.28f), Rand(rnd, 0.35f, 0.55f), 0.7f, 0.6f, true, step);
                    paths.Add(small);
                    int k = small.P.Count / 3;
                    if (k > 0 && k < small.P.Count - 1)
                    {
                        Vector2 d3 = small.P[k + 1] - small.P[k - 1];
                        paths.Add(Walk(rnd, small.P[k], Mathf.Atan2(d3.y, d3.x) + Rand(rnd, 0.7f, 1.2f), 0.18f, small.W[k] * 0.7f, small.W[k] * 0.4f, false, step));
                    }
                }

                var V = new List<Vector3>(); var Nn = new List<Vector3>(); var T = new List<int>();
                foreach (CrackPath path in paths)
                {
                    var pts = new List<Vector3>(); var nrm = new List<Vector3>(); var wid = new List<float>();
                    for (int k = 0; k < path.P.Count; k++)
                    {
                        Vector2 q = path.P[k];
                        float up = Mathf.Sqrt(Mathf.Max(0.02f, 1f - q.x * q.x - q.y * q.y));
                        Vector3 dirRoot = _longAx * q.x + _wideAx * q.y + _seamN * side * up;
                        if (Snap(dirRoot, r2s, verts, tris, mesh, out Vector3 pS, out Vector3 nS)) { pts.Add(pS); nrm.Add(nS); wid.Add(path.W[k]); }
                    }
                    AddRibbon(pts, nrm, wid, r2s, V, Nn, T);
                }
                if (T.Count == 0) continue;

                var cm = new Mesh { name = "CocoaCrack" };
                cm.SetVertices(V); cm.SetNormals(Nn); cm.SetTriangles(T, 0);
                cm.RecalculateBounds();
                var go = new GameObject("Crack (auto)");
                go.transform.SetParent(s, false);
                go.AddComponent<MeshFilter>().sharedMesh = cm;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = CrackMaterial(s);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _cracks.Add(go);
            }
        }

        /// <summary>
        /// Точку направления (в осях корня, от центра боба) — на поверхность скорлупки:
        /// луч из центра, дальнее пересечение с треугольниками (наружная сторона). Вывод — в осях скорлупки.
        /// </summary>
        private bool Snap(Vector3 dirRoot, Matrix4x4 r2s, Vector3[] verts, int[] tris, Mesh mesh, out Vector3 p, out Vector3 n)
        {
            // направление в «нормированных» осях боба → в метры корня (полуоси)
            Vector3 d = _longAx * Vector3.Dot(dirRoot, _longAx) * _halfL
                      + _wideAx * Vector3.Dot(dirRoot, _wideAx) * _halfW
                      + _seamN * Vector3.Dot(dirRoot, _seamN) * _halfN;
            Vector3 o = r2s.MultiplyPoint3x4(_center);
            Vector3 dir = r2s.MultiplyVector(d);
            p = Vector3.zero; n = Vector3.up;
            if (verts != null && tris != null)
            {
                float bestT = -1f; Vector3 bestN = Vector3.zero;
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                    if (RayTri(o, dir, a, b, c, out float t) && t > bestT)
                    {
                        bestT = t;
                        bestN = Vector3.Cross(b - a, c - a);
                    }
                }
                if (bestT > 0f)
                {
                    p = o + dir * bestT;
                    n = bestN.normalized;
                    if (Vector3.Dot(n, p - o) < 0f) n = -n;
                    return true;
                }
                return false;
            }
            // меш не читается — на эллипсоид габаритов
            Bounds mb = mesh.bounds;
            Vector3 e = dir.normalized;
            Vector3 ext = mb.extents;
            float inv = Mathf.Sqrt(Sq(e.x / Mathf.Max(1e-4f, ext.x)) + Sq(e.y / Mathf.Max(1e-4f, ext.y)) + Sq(e.z / Mathf.Max(1e-4f, ext.z)));
            if (inv < 1e-6f) return false;
            p = mb.center + e / inv;
            n = new Vector3((p.x - mb.center.x) / Sq(ext.x), (p.y - mb.center.y) / Sq(ext.y), (p.z - mb.center.z) / Sq(ext.z)).normalized;
            return true;
        }

        private static float Sq(float v) => v * v;

        private static bool RayTri(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 pv = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, pv);
            if (Mathf.Abs(det) < 1e-12f) return false;
            float inv = 1f / det;
            Vector3 tv = o - a;
            float u = Vector3.Dot(tv, pv) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 0f;
        }

        /// <summary>Полоска по точкам (оси скорлупки): ширина в каждой точке (0..1 от Crack Width), приподнята над поверхностью.</summary>
        private void AddRibbon(List<Vector3> pts, List<Vector3> nrm, List<float> wid, Matrix4x4 r2s, List<Vector3> V, List<Vector3> N, List<int> T)
        {
            if (pts.Count < 2) return;
            // масштаб «метр корня → единица скорлупки» (средний)
            float s2l = (r2s.MultiplyVector(Vector3.right).magnitude + r2s.MultiplyVector(Vector3.up).magnitude + r2s.MultiplyVector(Vector3.forward).magnitude) / 3f;
            float w = _crackWidth * _len * s2l;
            float lift = 0.006f * _len * s2l;
            // нормали сгладить (меш гранёный — иначе полоска «скачет» по граням)
            var sm = new Vector3[pts.Count];
            for (int i = 0; i < pts.Count; i++)
                sm[i] = (nrm[Mathf.Max(i - 1, 0)] + nrm[i] * 2f + nrm[Mathf.Min(i + 1, pts.Count - 1)]).normalized;
            int baseI = V.Count;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 tng = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 nn = sm[i];
                Vector3 sideV = Vector3.Cross(nn, tng).normalized;
                float ww = w * wid[i] * 0.5f;
                Vector3 c = pts[i] + nn * lift;
                V.Add(c - sideV * ww); N.Add(nn);
                V.Add(c + sideV * ww); N.Add(nn);
            }
            for (int i = 0; i < pts.Count - 1; i++)
            {
                int a = baseI + i * 2, b = a + 1, c = a + 2, d = a + 3;
                Tri(V, T, a, c, b, N[a]);
                Tri(V, T, b, c, d, N[b]);
            }
        }

        /// <summary>Лицом наружу: в Unity лицевая — по часовой, если смотреть снаружи.</summary>
        private static void Tri(List<Vector3> V, List<int> T, int a, int b, int c, Vector3 n)
        {
            Vector3 cr = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
            if (Vector3.Dot(cr, n) < 0f) { int x = b; b = c; c = x; }
            T.Add(a); T.Add(b); T.Add(c);
        }

        private Material CrackMaterial(Transform shell)
        {
            if (_crackMaterial != null) return _crackMaterial;
            Renderer r = shell.GetComponent<Renderer>();
            Material src = r != null ? r.sharedMaterial : null;
            if (src == null) return null;
            var m = new Material(src) { name = "CocoaCrack (auto)" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", _crackColor);
            if (m.HasProperty("_Color")) m.SetColor("_Color", _crackColor);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            _crackMaterial = m;
            return m;
        }

        private Bounds RootBounds()
        {
            Matrix4x4 w2r = transform.worldToLocalMatrix;
            bool any = false;
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            foreach (Transform s in _shells)
            {
                Mesh mesh = MeshOf(s);
                if (mesh == null) continue;
                Matrix4x4 m = w2r * s.localToWorldMatrix;
                Bounds mb = mesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 v = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(v);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        private static Mesh MeshOf(Transform t)
        {
            MeshFilter mf = t != null ? t.GetComponent<MeshFilter>() : null;
            return mf != null ? mf.sharedMesh : null;
        }

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.yellow; Gizmos.DrawLine(_center, _center + _longAx * _len * 0.5f);
            Gizmos.color = Color.cyan; Gizmos.DrawLine(_center, _center + _seamN * _len * 0.3f);
        }
    }

    /// <summary>Отпавшая скорлупка: лежит, потом уменьшается и исчезает. Только визуал, по сети не ходит.</summary>
    public sealed class CocoaShellFade : MonoBehaviour
    {
        private float _t, _life;
        private Vector3 _scale;
        public void Begin(float life) { _life = Mathf.Max(0.5f, life); _scale = transform.localScale; }
        private void Update()
        {
            _t += Time.deltaTime;
            float shrink = 1f;
            if (_t > _life - 1f) shrink = Mathf.Clamp01(_life - _t);
            transform.localScale = _scale * shrink;
            if (_t >= _life) Destroy(gameObject);
        }
    }
}
