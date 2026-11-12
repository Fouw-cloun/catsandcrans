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
    ///   2. Бензопила по треснувшему — v13.2–13.3: веди пилой по бобу как хочешь, за пилой остаётся
    ///      тёмный пропил (как трещина, лежит по поверхности). Обведи боб примерно КРУГОМ —
    ///      вдоль или поперёк, на Ring Need (70%) — и он разваливается: скорлупки отпадают
    ///      (лежат и исчезают), остаётся гроздь зёрен: сетевой префаб Pulp Prefab.
    ///      Целый (не битый) боб пила не берёт. Ring Saw выкл — как раньше: держи пилу на бобе.
    ///
    /// Вешается на КОРЕНЬ боба рядом с FoodPiece. Скорлупки и гроздь — дочерние меши:
    /// пусто в полях — найдутся по именам («Shell» — скорлупки, «Pulp» — гроздь).
    ///
    /// Сеть: что делать, решает сервер по состоянию FoodPiece. Трещины у всех одинаковые
    /// (зерно — Id объекта), сплющивание — по одному байту, раскрытие — один RPC;
    /// отпавшие скорлупки — чисто визуальные у каждого (по сети не ходят).
    /// </summary>
    [RequireComponent(typeof(FoodPiece))]
    public sealed class CocoaPod : NetworkBehaviour, IPartCuttable, IFoodWorkHandler, ISawOwnFx
    {
        [Header("Модель")]
        [Tooltip("Скорлупки (половинки стручка). Пусто — дочерние с «Shell» в имени")]
        [SerializeField] private Transform[] _shells;
        [Tooltip("Белая гроздь зёрен внутри. Пусто — дочерний с «Pulp» в имени")]
        [SerializeField] private Transform _pulp;

        [Header("Удар кувалдой")]
        [Tooltip("Состояние «треснул» (правило продукта ставит его кувалдой)")]
        [SerializeField] private FoodState _crackedState = FoodState.Pounded;
        [Tooltip("v13.3: лёгкое сплющивание при ударе (со «шмяком»). Выкл — форма не меняется, только трещины")]
        [SerializeField] private bool _squashSoft = true;
        [Tooltip("Сила сплющивания: 0 — нет, 1 — полностью Flat Height / Flat Widen. 0.45 — легко")]
        [Range(0f, 1f)][SerializeField] private float _squashAmount = 0.45f;
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

        [Header("v13.2: пилить кругом вокруг боба")]
        [Tooltip("Вкл — веди пилой по бобу, остаётся пропил; обвёл кругом на Ring Need — развалился. Выкл — как раньше (Saw Seconds в любой точке)")]
        [SerializeField] private bool _ringSaw = true;
        [Tooltip("v17.3: какую долю круга надо пропилить, чтобы боб развалился (0.5 — половина круга)")]
        [Range(0.1f, 1f)][SerializeField] private float _breakAt = 0.5f;
        [Tooltip("Насколько криво можно вести: ширина пояса вокруг круга (0.5 — пропил может гулять на полтолщины боба)")]
        [Range(0.15f, 0.9f)][SerializeField] private float _ringBand = 0.5f;
        [Tooltip("Секунд на каждый «шаг» пропила (у пилы на сервере не меньше 0.6)")]
        [Range(0.6f, 2f)][SerializeField] private float _ringStepSeconds = 0.6f;
        [Tooltip("Пропил соединяется с прошлым, если пилу повёл не дальше этого угла (градусы вокруг центра боба). Дальше — новый штрих")]
        [Range(20f, 120f)][SerializeField] private float _ringMaxJoin = 75f;
        [Tooltip("Ширина пропила (доля Crack Width)")]
        [Range(0.3f, 3f)][SerializeField] private float _cutWidth = 1.2f;
        [Tooltip("v13.3: эффекты при резьбе — щепки скорлупы, белые крошки мякоти, пыль, дрожь боба")]
        [SerializeField] private bool _cutFx = true;
        [Range(0f, 3f)][SerializeField] private float _cutFxAmount = 1f;
        [SerializeField] private Color _pulpSpeckColor = new Color(0.97f, 0.94f, 0.84f, 1f);
        [Tooltip("v13.4: цвет свежего среза по краям пропила (светлая «живая» скорлупа под коркой)")]
        [SerializeField] private Color _cutFreshColor = new Color(0.93f, 0.8f, 0.58f, 1f);
        [Tooltip("v13.4: размер ошмётков (доля длины боба)")]
        [Range(0.02f, 0.2f)][SerializeField] private float _chunkSize = 0.07f;

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
        // v13.2: пропил. Точки — направления из центра боба в нормированных осях (длина, ширина, шов), w=1 — начало штриха
        private readonly SyncList<Vector4> _trail = new SyncList<Vector4>();
        private readonly SyncVar<byte> _ringPct = new SyncVar<byte>(0);

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
            TickTrailVisual();
            if (_pend.Count > 0 && Time.time - _recFlushAt >= 0.1f) FlushPath();
            TickShake();
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
        public float SecondsPerCut => _ringSaw ? _ringStepSeconds + 0.1f : _sawSeconds;     // v13.3: +0.1 — сервер пилы не отбрасывает рез по джиттеру
        public bool ClampWhileCutting => false;
        public string CutLabel
        {
            get
            {
                if (_food == null || _food.State != _crackedState) return "какао-боб: сначала кувалдой";
                if (!_ringSaw) return "какао-боб: раскрыть";
                return $"какао-боб: пропил по кругу {_ringPct.Value}% из {_breakAt * 100f:0}% — обведи пилой вокруг боба";
            }
        }
        public Color CrumbColor
        {
            get
            {
                ProductDef d = _food != null ? _food.Def : null;
                return d != null ? d.CrumbColor : new Color(0.55f, 0.33f, 0.2f);
            }
        }
        public Vector3 CutPoint()
        {
            if (_ringSaw && _trail.Count > 0)
            {
                Vector4 q = _trail[_trail.Count - 1];
                return transform.TransformPoint(EllipsoidPoint(new Vector3(q.x, q.y, q.z)));
            }
            return transform.TransformPoint(_center);
        }

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

        // ───────────────────────── v13.2: ПИЛА КРУГОМ ─────────────────────────
        //
        // Точка касания пилы → направление из центра боба в нормированных осях q = (длина, ширина, шов),
        // |q| = 1. Сервер отмечает три «пояса»: вокруг длинной оси (поперёк боба), вокруг шва и вокруг
        // ширины (вдоль боба, в двух плоскостях). Точка попадает в пояс оси k, если |q[k]| ≤ Ring Band,
        // и закрашивает ячейку угла вокруг этой оси. Любой пояс закрашен на Ring Need — развалился.

        private const int RingBins = 36;
        private readonly ulong[] _rings = new ulong[3];        // только сервер
        private Vector3 _lastQ;
        private float _lastCutAt = -10f;
        private const int TrailMax = 900;

        // визуал пропила (у каждого свой, из _trail)
        private sealed class CutRun
        {
            public readonly List<Vector3> P = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<float> W = new List<float>();
            public readonly List<Vector2> R = new List<Vector2>();   // v13.4: случайные числа точки (у всех одинаковые)
        }
        private List<CutRun>[] _runs;            // по скорлупкам
        private CutRun[] _openRun;               // текущий (продолжающийся) кусок на каждой скорлупке
        private Mesh[] _cutMesh;
        private int _trailBuilt;
        private Vector3 _trailPrev;
        private bool _trailHasPrev;
        private int _trailSample;
        private Vector3[][] _shellVerts;
        private int[][] _shellTris;

        private Vector3 QAt(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world) - _center;
            Vector3 q = new Vector3(Vector3.Dot(local, _longAx) / _halfL, Vector3.Dot(local, _wideAx) / _halfW, Vector3.Dot(local, _seamN) / _halfN);
            return q.sqrMagnitude < 1e-8f ? new Vector3(0f, 0f, 1f) : q.normalized;
        }

        private Vector3 EllipsoidPoint(Vector3 q)
        {
            return _center + _longAx * (q.x * _halfL) + _wideAx * (q.y * _halfW) + _seamN * (q.z * _halfN);
        }

        private void Mark(Vector3 q)
        {
            for (int k = 0; k < 3; k++)
            {
                if (Mathf.Abs(q[k]) > _ringBand) continue;
                float a = Mathf.Atan2(q[(k + 2) % 3], q[(k + 1) % 3]);
                int bin = Mathf.Clamp(Mathf.FloorToInt((a + Mathf.PI) / (2f * Mathf.PI) * RingBins), 0, RingBins - 1);
                _rings[k] |= 1UL << bin;
            }
        }

        private float BestRing(out int axis)
        {
            float best = 0f; axis = 0;
            for (int k = 0; k < 3; k++)
            {
                int c = 0;
                for (int b = 0; b < RingBins; b++) if ((_rings[k] & (1UL << b)) != 0) c++;
                float f = c / (float)RingBins;
                if (f > best) { best = f; axis = k; }
            }
            return best;
        }

        public int PartAt(Collider hit, Vector3 point)
        {
            if (hit == null || !hit.transform.IsChildOf(transform)) return -1;
            return 0;
        }

        public bool CanCutPart(int part, Vector3 point, out string why)
        {
            why = "";
            if (!CanCut)
            {
                why = _opened ? "уже раскрыт" : "сначала разбей кувалдой";
                return false;
            }
            if (part < 0) { why = "веди пилой по бобу"; return false; }
            // v13.3: пила спрашивает это у владельца КАЖДЫЙ кадр — значит, здесь видно, где пила сейчас.
            // Пишем путь пилы и пачками шлём серверу (раньше рез был только раз в 0.6 с — места пропускались)
            if (_ringSaw && base.IsClientInitialized) RecordLocal(point);
            return true;
        }

        // ── путь пилы у владельца ──
        private readonly List<Vector4> _pend = new List<Vector4>();
        private Vector3 _recLast;
        private float _recLastAt = -10f, _recFlushAt;

        private void RecordLocal(Vector3 point)
        {
            Vector3 q = QAt(point);
            bool newStroke = Time.time - _recLastAt > 0.35f;
            if (!newStroke && Vector3.Angle(_recLast, q) < 1.5f) { _recLastAt = Time.time; return; }
            _pend.Add(new Vector4(q.x, q.y, q.z, newStroke ? 1f : 0f));
            _recLast = q;
            _recLastAt = Time.time;
            if (_pend.Count >= 16 || Time.time - _recFlushAt >= 0.1f) FlushPath();
        }

        private void FlushPath()
        {
            _recFlushAt = Time.time;
            if (_pend.Count == 0) return;
            Vector4[] arr = _pend.ToArray();
            _pend.Clear();
            CmdSawPath(arr);
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdSawPath(Vector4[] qs)
        {
            if (!_ringSaw || !CanCut || qs == null) return;
            int n = Mathf.Min(qs.Length, 32);
            for (int i = 0; i < n; i++)
            {
                Vector3 q = new Vector3(qs[i].x, qs[i].y, qs[i].z);
                if (q.sqrMagnitude < 0.25f || q.sqrMagnitude > 4f) continue;   // мусор
                ApplyQ(q.normalized, qs[i].w > 0.5f);
            }
            CheckRingDone(_lastProc, 1f);
        }

        private ProcessDef _lastProc;

        /// <summary>Сервер: одна точка пути пилы — закрасить пояса, продолжить/начать штрих пропила.</summary>
        [Server]
        private void ApplyQ(Vector3 q, bool newStroke)
        {
            float ang = Vector3.Angle(_lastQ, q);
            bool join = !newStroke && Time.time - _lastCutAt < 0.6f && ang <= _ringMaxJoin;
            if (join)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(ang / 4f));
                for (int i = 1; i <= steps; i++) Mark(Vector3.Slerp(_lastQ, q, i / (float)steps));
            }
            else Mark(q);
            if (!join || ang >= 2.5f)
            {
                if (_trail.Count < TrailMax) _trail.Add(new Vector4(q.x, q.y, q.z, join ? 0f : 1f));
                _lastQ = q;
            }
            _lastCutAt = Time.time;
        }

        [Server]
        private void CheckRingDone(ProcessDef cutProcess, float quality01)
        {
            if (!CanCut) return;
            int axis;
            float best = BestRing(out axis);
            byte pct = (byte)Mathf.Clamp(Mathf.RoundToInt(best * 100f), 0, 100);
            if (pct != _ringPct.Value)
            {
                if (_log && pct / 10 != _ringPct.Value / 10)
                    Debug.Log($"[Какао-боб] «{name}»: пропил по кругу {pct}% (нужно {_breakAt * 100f:0}%, " +
                              (axis == 0 ? "поперёк" : "вдоль") + ")", this);
                _ringPct.Value = pct;
            }
            if (best >= _breakAt - 1e-4f) ServerCutOne(cutProcess, quality01);
        }

        [Server]
        public void ServerCutPart(int part, Vector3 point, ProcessDef cutProcess, float quality01)
        {
            if (!_ringSaw) { ServerCutOne(cutProcess, quality01); return; }
            if (!CanCut) return;
            _lastProc = cutProcess;
            ApplyQ(QAt(point), false);           // запасной путь: основной — CmdSawPath
            CheckRingDone(cutProcess, quality01);
        }

        [Server]
        public void ServerSetFocus(int part) { }

        /// <summary>Достроить пропил по новым точкам из _trail (только добавленным — дорого снапать).</summary>
        private void TickTrailVisual()
        {
            if (!_ringSaw || _opened || _shells.Length == 0 || _rootFromShell.Count != _shells.Length) return;
            int count = _trail.Count;
            if (count <= _trailBuilt) return;
            if (_runs == null) InitTrailVisual();

            int seed = base.NetworkObject != null ? base.NetworkObject.ObjectId : 0;
            bool fx = _cutFx && count - _trailBuilt <= 24;          // зашёл в игру позже — старый пропил без эффектов
            for (int i = _trailBuilt; i < count; i++)
            {
                Vector4 v = _trail[i];
                Vector3 q = new Vector3(v.x, v.y, v.z);
                if (fx) CutFx(q);
                bool start = v.w > 0.5f || !_trailHasPrev;
                if (start)
                {
                    for (int s = 0; s < _openRun.Length; s++) _openRun[s] = null;
                    AddSample(q, seed, true);
                }
                else
                {
                    float ang = Vector3.Angle(_trailPrev, q);
                    int steps = Mathf.Max(1, Mathf.CeilToInt(ang / 3f));
                    for (int k = 1; k <= steps; k++) AddSample(Vector3.Slerp(_trailPrev, q, k / (float)steps), seed, false);
                }
                _trailPrev = q;
                _trailHasPrev = true;
            }
            _trailBuilt = count;
            RebuildCutMeshes();
        }

        private void InitTrailVisual()
        {
            int n = _shells.Length;
            _runs = new List<CutRun>[n];
            _openRun = new CutRun[n];
            _cutMesh = new Mesh[n];
            _shellVerts = new Vector3[n][];
            _shellTris = new int[n][];
            for (int i = 0; i < n; i++)
            {
                _runs[i] = new List<CutRun>();
                Mesh mesh = MeshOf(_shells[i]);
                if (mesh != null && mesh.isReadable) { _shellVerts[i] = mesh.vertices; _shellTris[i] = mesh.triangles; }
                if (_shells[i] == null) continue;
                var go = new GameObject("SawCut (auto)");
                go.transform.SetParent(_shells[i], false);
                go.layer = _shells[i].gameObject.layer;
                _cutMesh[i] = new Mesh { name = "CocoaSawCut" };
                go.AddComponent<MeshFilter>().sharedMesh = _cutMesh[i];
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new Material[] { CutFreshMaterial(_shells[i]), CrackMaterial(_shells[i]) };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _cracks.Add(go);
            }
        }

        /// <summary>Одна точка пропила: чуть «рваная» (как трещина) и прижатая к своей скорлупке.</summary>
        private static float Hash(int seed, int i, float salt)
        {
            float v = Mathf.Sin(seed * 12.9898f + i * 78.233f + salt * 37.719f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        private void AddSample(Vector3 q, int seed, bool strokeStart)
        {
            _trailSample++;
            // v13.4: путь пилы — чуть рваный (резкие мелкие сдвиги, не плавная волна маркера)
            Vector3 tng = _trailHasPrev && !strokeStart ? (q - _trailPrev) : Vector3.zero;
            Vector3 perp = tng.sqrMagnitude > 1e-10f ? Vector3.Cross(tng.normalized, q).normalized : Vector3.zero;
            float nz = Hash(seed, _trailSample, 0.1f) - 0.5f;
            Vector3 qj = (q + perp * nz * 0.018f).normalized;
            float w = strokeStart ? 0.25f : 1f;
            Vector2 rr = new Vector2(Hash(seed, _trailSample, 1.3f), Hash(seed, _trailSample, 2.7f));

            Vector3 dirRoot = _longAx * qj.x + _wideAx * qj.y + _seamN * qj.z;
            for (int i = 0; i < _shells.Length; i++)
            {
                if (_shells[i] == null) continue;
                // скорлупка своей стороны шва (у самого шва — обе)
                Vector3 sc = i < _shellCenters.Count ? _shellCenters[i] - _center : Vector3.zero;
                float side = Vector3.Dot(sc, _seamN);
                if (Mathf.Abs(side) > 1e-5f && qj.z * Mathf.Sign(side) < -0.06f) { _openRun[i] = null; continue; }
                Mesh mesh = MeshOf(_shells[i]);
                if (mesh == null) continue;
                Matrix4x4 r2s = _rootFromShell[i].inverse;
                Vector3 pS, nS;
                if (!Snap(dirRoot, r2s, _shellVerts[i], _shellTris[i], mesh, out pS, out nS)) { _openRun[i] = null; continue; }
                if (_openRun[i] == null) { _openRun[i] = new CutRun(); _runs[i].Add(_openRun[i]); }
                _openRun[i].P.Add(pS); _openRun[i].N.Add(nS); _openRun[i].W.Add(w); _openRun[i].R.Add(rr);
            }
        }

        /// <summary>
        /// v13.4: ПРОПИЛ, а не маркер. Два слоя:
        ///   • светлый «свежий срез» — рваные края с зубцами (каждая сторона своя, через точку то шире, то уже);
        ///   • тёмная узкая щель в середине, концы — острыми иглами;
        ///   • от щели иногда отходят короткие острые трещинки-занозы.
        /// </summary>
        private void RebuildCutMeshes()
        {
            for (int i = 0; i < _shells.Length; i++)
            {
                if (_cutMesh[i] == null) continue;
                var V = new List<Vector3>(); var Nn = new List<Vector3>();
                var Tl = new List<int>(); var Td = new List<int>();
                Matrix4x4 r2s = _rootFromShell[i].inverse;
                float s2l = (r2s.MultiplyVector(Vector3.right).magnitude + r2s.MultiplyVector(Vector3.up).magnitude + r2s.MultiplyVector(Vector3.forward).magnitude) / 3f;
                float wb = _crackWidth * _len * s2l * _cutWidth;
                float lift = 0.006f * _len * s2l;
                foreach (CutRun run in _runs[i]) BuildKerf(run, wb, lift, V, Nn, Tl, Td);
                _cutMesh[i].Clear();
                if (Tl.Count == 0 && Td.Count == 0) continue;
                _cutMesh[i].SetVertices(V); _cutMesh[i].SetNormals(Nn);
                _cutMesh[i].subMeshCount = 2;
                _cutMesh[i].SetTriangles(Tl, 0);
                _cutMesh[i].SetTriangles(Td, 1);
                _cutMesh[i].RecalculateBounds();
            }
        }

        private void BuildKerf(CutRun run, float wb, float lift, List<Vector3> V, List<Vector3> N, List<int> Tl, List<int> Td)
        {
            int n = run.P.Count;
            if (n < 2) return;
            var sm = new Vector3[n];
            var tg = new Vector3[n];
            for (int k = 0; k < n; k++)
            {
                sm[k] = (run.N[Mathf.Max(k - 1, 0)] + run.N[k] * 2f + run.N[Mathf.Min(k + 1, n - 1)]).normalized;
                Vector3 d = run.P[Mathf.Min(k + 1, n - 1)] - run.P[Mathf.Max(k - 1, 0)];
                tg[k] = d.sqrMagnitude > 1e-12f ? d.normalized : Vector3.forward;
            }
            int baseL = V.Count;
            // светлый край: 2 вершины на точку
            for (int k = 0; k < n; k++)
            {
                Vector2 r = run.R[k];
                float tip = Mathf.Clamp01(Mathf.Min(k / 2.5f, (n - 1 - k) / 2.5f));
                float zig = (k & 1) == 0 ? 1f : 0.55f;                    // зубцы
                float wl = wb * (0.55f + 0.9f * r.x) * zig * tip * run.W[k];
                float wr = wb * (0.55f + 0.9f * r.y) * (1.55f - zig) * tip * run.W[k];
                Vector3 side = Vector3.Cross(sm[k], tg[k]).normalized;
                Vector3 c = run.P[k] + sm[k] * lift;
                V.Add(c - side * wl); N.Add(sm[k]);
                V.Add(c + side * wr); N.Add(sm[k]);
            }
            for (int k = 0; k < n - 1; k++)
            {
                int a = baseL + k * 2;
                Tri(V, Tl, a, a + 2, a + 1, N[a]);
                Tri(V, Tl, a + 1, a + 2, a + 3, N[a + 1]);
            }
            // тёмная щель: узкая, концы иглой, чуть выше
            int baseD = V.Count;
            for (int k = 0; k < n; k++)
            {
                Vector2 r = run.R[k];
                float tip = Mathf.Pow(Mathf.Clamp01(Mathf.Min(k / 3f, (n - 1 - k) / 3f)), 1.5f);
                float w = wb * (0.16f + 0.18f * r.y) * tip * run.W[k];
                Vector3 side = Vector3.Cross(sm[k], tg[k]).normalized;
                Vector3 c = run.P[k] + sm[k] * (lift * 1.8f);
                float skew = (r.x - 0.5f) * w * 0.8f;                       // щель гуляет внутри среза
                V.Add(c - side * (w - skew)); N.Add(sm[k]);
                V.Add(c + side * (w + skew)); N.Add(sm[k]);
            }
            for (int k = 0; k < n - 1; k++)
            {
                int a = baseD + k * 2;
                Tri(V, Td, a, a + 2, a + 1, N[a]);
                Tri(V, Td, a + 1, a + 2, a + 3, N[a + 1]);
            }
            // занозы: короткие острые трещинки от щели в стороны
            for (int k = 2; k < n - 2; k++)
            {
                Vector2 r = run.R[k];
                if (r.x < 0.93f) continue;                                    // редко
                float sideSign = r.y > 0.5f ? 1f : -1f;
                Vector3 side = Vector3.Cross(sm[k], tg[k]).normalized * sideSign;
                Vector3 c = run.P[k] + sm[k] * (lift * 1.9f);
                float len = wb * (0.9f + 1.6f * Mathf.Abs(r.y - 0.5f) * 2f);
                float half = wb * 0.16f;
                float along = (k & 2) == 0 ? 1f : -1f;                        // уходит наискось, как скол от зуба
                Vector3 a0 = c - tg[k] * half;
                Vector3 a1 = c + tg[k] * half;
                Vector3 mid = c + side * len * 0.35f + tg[k] * along * len * 0.4f + sm[k] * lift * 0.2f;
                Vector3 tipP = c + side * len * 0.6f + tg[k] * along * len * 0.85f;
                int b = V.Count;
                V.Add(a0); N.Add(sm[k]);
                V.Add(a1); N.Add(sm[k]);
                V.Add(mid - tg[k] * half * 0.35f); N.Add(sm[k]);
                V.Add(mid + tg[k] * half * 0.35f); N.Add(sm[k]);
                V.Add(tipP); N.Add(sm[k]);
                Tri(V, Td, b, b + 1, b + 2, sm[k]);
                Tri(V, Td, b + 1, b + 3, b + 2, sm[k]);
                Tri(V, Td, b + 2, b + 3, b + 4, sm[k]);
            }
        }

        private Material _freshMat;

        private Material CutFreshMaterial(Transform shell)
        {
            if (_freshMat != null) return _freshMat;
            Renderer r = shell.GetComponent<Renderer>();
            Material src = r != null ? r.sharedMaterial : null;
            if (src == null) return CrackMaterial(shell);
            _freshMat = new Material(src) { name = "CocoaCutFresh (auto)" };
            if (_freshMat.HasProperty("_BaseColor")) _freshMat.SetColor("_BaseColor", _cutFreshColor);
            if (_freshMat.HasProperty("_Color")) _freshMat.SetColor("_Color", _cutFreshColor);
            if (_freshMat.HasProperty("_BaseMap")) _freshMat.SetTexture("_BaseMap", null);
            if (_freshMat.HasProperty("_MainTex")) _freshMat.SetTexture("_MainTex", null);
            if (_freshMat.HasProperty("_Smoothness")) _freshMat.SetFloat("_Smoothness", 0.25f);
            return _freshMat;
        }

        // ── эффекты резьбы (v13.4: объёмные ошмётки разной формы, а не квадратики) ──
        private ParticleSystem _fxShell, _fxPulp, _fxDust;
        private Material _fxShellMat, _fxPulpMat, _fxDustMat;
        private float _shakeT = -1f;

        private float WorldLen() => Mathf.Max(0.02f, transform.TransformVector(_longAx * _len).magnitude);

        private void EnsureFx()
        {
            if (_fxShell != null) return;
            Renderer sr = _shells.Length > 0 && _shells[0] != null ? _shells[0].GetComponent<Renderer>() : null;
            Renderer pr = _pulp != null ? _pulp.GetComponent<Renderer>() : null;
            _fxShellMat = sr != null && sr.sharedMaterial != null ? new Material(sr.sharedMaterial) { name = "CocoaChunk (auto)" } : new Material(CCRuntimeParticles.SharedMaterial);
            if (pr != null && pr.sharedMaterial != null) _fxPulpMat = new Material(pr.sharedMaterial) { name = "CocoaPulpBit (auto)" };
            else
            {
                _fxPulpMat = new Material(_fxShellMat) { name = "CocoaPulpBit (auto)" };
                if (_fxPulpMat.HasProperty("_BaseColor")) _fxPulpMat.SetColor("_BaseColor", _pulpSpeckColor);
                if (_fxPulpMat.HasProperty("_Color")) _fxPulpMat.SetColor("_Color", _pulpSpeckColor);
                if (_fxPulpMat.HasProperty("_BaseMap")) _fxPulpMat.SetTexture("_BaseMap", null);
            }
            _fxShellMat.enableInstancing = true;
            _fxPulpMat.enableInstancing = true;
            float wl = WorldLen();
            _fxShell = CocoaFx.MeshSystem("CC Cocoa Chunks", _fxShellMat, CocoaFx.ShellMeshes(), wl * _chunkSize);
            _fxPulp = CocoaFx.MeshSystem("CC Cocoa Pulp Bits", _fxPulpMat, CocoaFx.PulpMeshes(), wl * _chunkSize * 0.7f);
            _fxDustMat = CocoaFx.PuffMaterial();
            _fxDust = CocoaFx.PuffSystem("CC Cocoa Dust", _fxDustMat);
        }

        private void CutFx(Vector3 q)
        {
            if (Random.value > 0.85f * Mathf.Min(1f, _cutFxAmount + 0.2f)) { _shakeT = 0f; return; }
            EnsureFx();
            Vector3 at = transform.TransformPoint(EllipsoidPoint(q));
            Vector3 nrm = transform.TransformDirection(_longAx * (q.x / _halfL) + _wideAx * (q.y / _halfW) + _seamN * (q.z / _halfN)).normalized;
            float wl = WorldLen();
            float sp = Mathf.Sqrt(wl / 0.26f);
            float a = _cutFxAmount;
            if (Random.value < 0.42f * a) EmitChunk(_fxShell, at, nrm, sp, wl * _chunkSize * Random.Range(0.6f, 1.3f), 1f);
            if (Random.value < 0.3f * a) EmitChunk(_fxPulp, at, nrm, sp * 0.8f, wl * _chunkSize * Random.Range(0.4f, 0.8f), 0.8f);
            if (Random.value < 0.22f * a) EmitPuff(at, nrm, sp, wl);
            _shakeT = 0f;
        }

        private static void EmitChunk(ParticleSystem ps, Vector3 at, Vector3 nrm, float sp, float size, float kick)
        {
            var ep = new ParticleSystem.EmitParams();
            ep.position = at + nrm * size * 0.5f;
            ep.velocity = (nrm * Random.Range(1.3f, 2.8f) + Random.insideUnitSphere * 1.1f + Vector3.up * Random.Range(0.6f, 1.6f)) * sp * kick;
            ep.startSize = size;
            ep.rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
            ep.startLifetime = Random.Range(1.6f, 2.6f);
            ps.Emit(ep, 1);
        }

        private void EmitPuff(Vector3 at, Vector3 nrm, float sp, float wl)
        {
            var ep = new ParticleSystem.EmitParams();
            ep.position = at + nrm * wl * 0.03f;
            ep.velocity = (nrm * Random.Range(0.25f, 0.6f) + Vector3.up * 0.25f + Random.insideUnitSphere * 0.15f) * sp;
            ep.startSize = wl * Random.Range(0.12f, 0.22f);
            Color c = Color.Lerp(CrumbColor, Color.white, 0.6f); c.a = 0.45f;
            ep.startColor = c;
            ep.rotation = Random.Range(0f, 360f);
            ep.startLifetime = Random.Range(0.5f, 0.9f);
            _fxDust.Emit(ep, 1);
        }

        /// <summary>Раскрылся — горсть ошмётков и мякоти разом.</summary>
        private void OpenBurst()
        {
            if (!_cutFx) return;
            EnsureFx();
            Vector3 c = transform.TransformPoint(_center);
            float wl = WorldLen();
            float sp = Mathf.Sqrt(wl / 0.26f) * 0.7f;
            int shells = Mathf.RoundToInt(7 * _cutFxAmount), pulp = Mathf.RoundToInt(6 * _cutFxAmount), puffs = Mathf.RoundToInt(4 * _cutFxAmount);
            for (int i = 0; i < shells; i++) { Vector3 d = Random.onUnitSphere; d.y = Mathf.Abs(d.y); EmitChunk(_fxShell, c + d * wl * 0.15f, d, sp, wl * _chunkSize * Random.Range(0.7f, 1.4f), 1f); }
            for (int i = 0; i < pulp; i++) { Vector3 d = Random.onUnitSphere; d.y = Mathf.Abs(d.y); EmitChunk(_fxPulp, c + d * wl * 0.1f, d, sp, wl * _chunkSize * Random.Range(0.5f, 0.9f), 0.8f); }
            for (int i = 0; i < puffs; i++) EmitPuff(c + Random.insideUnitSphere * wl * 0.15f, Random.onUnitSphere, sp, wl * 1.4f);
        }

        /// <summary>Боб подрагивает под пилой (только модель, физика и сеть не трогаются).</summary>
        private void TickShake()
        {
            if (_pivot == null || _shakeT < 0f) return;
            _shakeT += Time.deltaTime;
            const float dur = 0.22f;
            if (_shakeT >= dur || _opened)
            {
                _shakeT = -1f;
                _pivot.localRotation = Quaternion.identity;
                return;
            }
            float a = (1f - _shakeT / dur) * 2.2f * Mathf.Min(1f, _cutFxAmount);
            float t = Time.time * 47f;
            _pivot.localRotation = Quaternion.Euler(Mathf.Sin(t) * a, Mathf.Sin(t * 1.37f + 1f) * a * 0.6f, Mathf.Sin(t * 0.83f + 2f) * a);
        }

        private void OnDestroy()
        {
            if (_cutMesh != null) foreach (Mesh m in _cutMesh) if (m != null) CCHeatFx.Kill(m);
            // ошмётки долетают и сами исчезают, потом — убрать системы и материалы
            if (_fxShell != null) CCHeatFx.Kill(_fxShell.gameObject, 3f);
            if (_fxPulp != null) CCHeatFx.Kill(_fxPulp.gameObject, 3f);
            if (_fxDust != null) CCHeatFx.Kill(_fxDust.gameObject, 3f);
            if (_fxShellMat != null) CCHeatFx.Kill(_fxShellMat, 3.1f);
            if (_fxPulpMat != null) CCHeatFx.Kill(_fxPulpMat, 3.1f);
            if (_fxDustMat != null) CCHeatFx.Kill(_fxDustMat, 3.1f);
            if (_freshMat != null) CCHeatFx.Kill(_freshMat, 3.1f);
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
            OpenBurst();
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
            // v17: квадратных крошек больше нет — ошмётки скорлупы и мякоти летят из OpenBurst (сетки CocoaFx)
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
            if (ax == 0 || !_squashSoft || _squashAmount <= 0f) { _pivot.localScale = Vector3.one; return; }
            float fh = Mathf.Lerp(1f, _flatHeight, _squashAmount), fw = Mathf.Lerp(1f, _flatWiden, _squashAmount);
            float e = k >= 1f ? 1f : 1f + Mathf.Sin(k * Mathf.PI) * 0.35f * _squashAmount;
            float h = Mathf.Lerp(1f, fh, Mathf.Clamp01(k * 1.6f)) / Mathf.Lerp(1f, e, 0.3f);
            float w = Mathf.Lerp(1f, fw, Mathf.Clamp01(k * 1.6f)) * Mathf.Lerp(1f, e, 0.1f);
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

    /// <summary>v13.4: пила не сыплет свои квадратные крошки — цель рисует эффекты сама.</summary>
    public interface ISawOwnFx { }

    /// <summary>v13.4: ошмётки какао-боба — сетки разной формы и системы частиц для них.</summary>
    public static class CocoaFx
    {
        private static Mesh[] _shell, _pulp;
        private static Texture2D _puffTex;

        /// <summary>Скорлупа: рваные выгнутые пластинки (5–8 углов, разная толщина) и длинные щепки.</summary>
        public static Mesh[] ShellMeshes()
        {
            if (_shell != null && _shell[0] != null) return _shell;
            var r = new System.Random(4242);
            _shell = new Mesh[7];
            for (int i = 0; i < 5; i++) _shell[i] = Shard(r, 5 + i % 4, 1f, 1f, 0.12f + 0.06f * (i % 3), 0.25f);
            _shell[5] = Shard(r, 5, 1.9f, 0.32f, 0.12f, 0.1f);       // щепка
            _shell[6] = Shard(r, 4, 1.6f, 0.45f, 0.16f, 0.15f);
            return _shell;
        }

        /// <summary>Мякоть: мягкие бугристые комочки.</summary>
        public static Mesh[] PulpMeshes()
        {
            if (_pulp != null && _pulp[0] != null) return _pulp;
            var r = new System.Random(777);
            _pulp = new Mesh[3];
            for (int i = 0; i < 3; i++) _pulp[i] = Blob(r, 0.75f + 0.1f * i);
            return _pulp;
        }

        private static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        private static Mesh Shard(System.Random r, int corners, float sx, float sy, float thick, float bend)
        {
            var ring = new Vector3[corners];
            float a0 = R(r, 0f, 6.28f);
            for (int i = 0; i < corners; i++)
            {
                float a = a0 + (i + R(r, -0.3f, 0.3f)) / corners * Mathf.PI * 2f;
                float rad = R(r, 0.45f, 1f);
                float x = Mathf.Cos(a) * rad * sx * 0.5f, y = Mathf.Sin(a) * rad * sy * 0.5f;
                ring[i] = new Vector3(x, y, -bend * (x * x + y * y));
            }
            var V = new List<Vector3>(); var T = new List<int>();
            Vector3 c = Vector3.zero;
            foreach (Vector3 p in ring) c += p;
            c /= corners;
            Vector3 off = new Vector3(0f, 0f, -thick);
            for (int i = 0; i < corners; i++)
            {
                Vector3 a = ring[i], b = ring[(i + 1) % corners];
                // лицевая сторона в Unity — та, куда смотрит cross(b−a, c−a): всё наружу
                FlatTri(V, T, c, a, b);                         // верх (+Z)
                FlatTri(V, T, c + off, b + off, a + off);       // низ (−Z)
                FlatTri(V, T, a, b + off, b);                   // бок
                FlatTri(V, T, a, a + off, b + off);
            }
            return Build("CocoaShard", V, T, false);
        }

        private static Mesh Blob(System.Random r, float squash)
        {
            // октаэдр, разбитый на 4 по каждой грани, точки — на неровную сферу
            Vector3[] o = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            int[] f = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
            var V = new List<Vector3>(); var T = new List<int>();
            var noise = new float[5];
            for (int i = 0; i < noise.Length; i++) noise[i] = R(r, 0f, 100f);
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = o[f[i]], b = o[f[i + 1]], c = o[f[i + 2]];
                Vector3 ab = (a + b).normalized, bc = (b + c).normalized, ca = (c + a).normalized;
                Vector3[] tri = { a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca };
                for (int k = 0; k < tri.Length; k += 3)
                    FlatTri(V, T, Bump(tri[k], noise, squash), Bump(tri[k + 1], noise, squash), Bump(tri[k + 2], noise, squash));
            }
            return Build("CocoaPulpBit", V, T, true);
        }

        private static Vector3 Bump(Vector3 d, float[] n, float squash)
        {
            float k = 0.42f + 0.1f * Mathf.Sin(d.x * 3.1f + n[0]) * Mathf.Cos(d.y * 2.7f + n[1]) + 0.06f * Mathf.Sin(d.z * 4.3f + n[2]);
            Vector3 p = d * k;
            p.y *= squash;
            return p;
        }

        private static void FlatTri(List<Vector3> V, List<int> T, Vector3 a, Vector3 b, Vector3 c)
        {
            int i = V.Count;
            V.Add(a); V.Add(b); V.Add(c);
            T.Add(i); T.Add(i + 1); T.Add(i + 2);
        }

        private static Mesh Build(string name, List<Vector3> V, List<int> T, bool smooth)
        {
            var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            m.SetVertices(V);
            m.SetTriangles(T, 0);
            m.RecalculateNormals();
            if (smooth)
            {
                // мягкие нормали: усреднить по совпадающим точкам
                Vector3[] nv = m.normals;
                var acc = new Dictionary<Vector3, Vector3>();
                for (int i = 0; i < V.Count; i++) { Vector3 s; acc.TryGetValue(V[i], out s); acc[V[i]] = s + nv[i]; }
                for (int i = 0; i < V.Count; i++) nv[i] = acc[V[i]].normalized;
                m.normals = nv;
            }
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Система объёмных ошмётков: падают, кувыркаются (пока летят), отскакивают, лежат и тают.</summary>
        public static ParticleSystem MeshSystem(string name, Material mat, Mesh[] meshes, float size)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.SetActive(false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule m = ps.main;
            m.loop = true; m.playOnAwake = true; m.duration = 1f;
            m.startLifetime = 2f;
            m.startSpeed = 0f;
            m.startSize = size;
            m.startRotation3D = true;
            m.gravityModifier = 1.1f;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.scalingMode = ParticleSystemScalingMode.Shape;
            m.maxParticles = 160;
            ParticleSystem.EmissionModule e = ps.emission; e.enabled = true; e.rateOverTime = 0f;
            ParticleSystem.ShapeModule sh = ps.shape; sh.enabled = false;

            // кувыркаются, пока летят; на столе — замирают (вращение от скорости)
            ParticleSystem.RotationBySpeedModule rs = ps.rotationBySpeed;
            rs.enabled = true;
            rs.separateAxes = true;
            var up = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
            rs.x = new ParticleSystem.MinMaxCurve(14f, up);
            rs.y = new ParticleSystem.MinMaxCurve(-9f, up);
            rs.z = new ParticleSystem.MinMaxCurve(11f, up);
            rs.range = new Vector2(0f, 2.5f);

            ParticleSystem.SizeOverLifetimeModule sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.78f, 1f), new Keyframe(1f, 0f)));

            ParticleSystem.CollisionModule c = ps.collision;
            c.enabled = true;
            c.type = ParticleSystemCollisionType.World;
            c.mode = ParticleSystemCollisionMode.Collision3D;
            c.quality = ParticleSystemCollisionQuality.Medium;
            c.dampen = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
            c.bounce = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            c.lifetimeLoss = 0f;
            c.radiusScale = 0.45f;
            c.collidesWith = ~(1 << 2);           // не о Ignore Raycast

            ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Mesh;
            pr.SetMeshes(meshes);
            pr.alignment = ParticleSystemRenderSpace.World;
            pr.sharedMaterial = mat;
            pr.enableGPUInstancing = true;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            pr.receiveShadows = true;
            go.SetActive(true);
            ps.Play();
            return ps;
        }

        /// <summary>Мягкий клубок пыли (круглая размытая текстура, растёт и тает).</summary>
        public static ParticleSystem PuffSystem(string name, Material mat)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.SetActive(false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule m = ps.main;
            m.loop = true; m.playOnAwake = true; m.duration = 1f;
            m.startSpeed = 0f;
            m.gravityModifier = -0.03f;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.scalingMode = ParticleSystemScalingMode.Shape;
            m.maxParticles = 60;
            ParticleSystem.EmissionModule e = ps.emission; e.enabled = true; e.rateOverTime = 0f;
            ParticleSystem.ShapeModule sh = ps.shape; sh.enabled = false;
            ParticleSystem.SizeOverLifetimeModule sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.35f, 1f), new Keyframe(1f, 1.5f)));
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            ParticleSystem.LimitVelocityOverLifetimeModule lv = ps.limitVelocityOverLifetime;
            lv.enabled = true; lv.drag = 2.5f; lv.limit = 100f;
            ParticleSystem.RotationOverLifetimeModule ro = ps.rotationOverLifetime;
            ro.enabled = true; ro.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
            ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.sharedMaterial = mat;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(true);
            ps.Play();
            return ps;
        }

        public static Material PuffMaterial()
        {
            if (_puffTex == null)
            {
                const int S = 64;
                _puffTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = "CocoaPuff", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                var px = new Color32[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                        float ang = Mathf.Atan2(dy, dx);
                        // клубок: неровный край из трёх «горбов» + мягкий спад
                        float edge = 0.78f + 0.1f * Mathf.Sin(ang * 3f + 0.7f) + 0.06f * Mathf.Sin(ang * 5f + 2.1f);
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / edge;
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a * (3f - 2f * a);
                        px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                _puffTex.SetPixels32(px);
                _puffTex.Apply();
            }
            var m = new Material(CCRuntimeParticles.SharedMaterial) { name = "CocoaPuff (auto)" };
            m.mainTexture = _puffTex;
            return m;
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
