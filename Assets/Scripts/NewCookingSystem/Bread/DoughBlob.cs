using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ТЕСТО-ШАР (и кусок теста после шпателя) — мягкий, как тесто из мультика.
    ///
    /// ВИД (у каждого игрока свой, по сети не ходит):
    ///   • меш строится сам (шар), размер — от числа порций (кусок после шпателя меньше);
    ///   • лежит — понемногу оседает и растекается снизу; несёшь — подтягивается;
    ///   • трясётся и пружинит от рывков и падений, с боков свисают мягкие подтёки.
    ///
    /// РАССТОЙКА: брось в банку (любая тара; тесто не растворяется, лежит куском) —
    /// со временем поднимается: растёт, по поверхности вздуваются пузыри, материал меняется
    /// на «пузырчатый» (Proofed Material). Готово — «Подошло» (можно делить шпателем).
    /// На огне (горячая тара) не подходит.
    ///
    /// ШПАТЕЛЬ (DoughSpatulaItem) делит подошедший шар на Parts равных кусков (по умолчанию 4):
    /// каждый удар отщипывает кусок, шар уменьшается; последний остаток — сам кусок.
    /// Куски кладутся в формы для хлеба (BreadMold).
    ///
    /// Вешается на корень префаба теста рядом с FoodPiece; на корне — Sphere Collider.
    /// </summary>
    [RequireComponent(typeof(FoodPiece))]
    public sealed class DoughBlob : NetworkBehaviour, IFoodPutHandler, IFoodSecondaryHandler
    {
        [Header("Вид")]
        [Tooltip("Материал теста")]
        [SerializeField] private Material _material;
        [Tooltip("Материал подошедшего теста (пузырьки). Пусто — тот же")]
        [SerializeField] private Material _proofedMaterial;
        [Tooltip("Радиус шара, м, при Ref Portions порций (кусок меньше — по объёму)")]
        [SerializeField] private float _refRadius = 0.16f;
        [Min(1)][SerializeField] private int _refPortions = 48;
        [Tooltip("Во сколько раз больше: 1 — шар 0.32 м, 4.5 — шар ~1.4 м (под большую бетономешалку)")]
        [Range(0.5f, 8f)][SerializeField] private float _sizeScale = 4.5f;
        [Tooltip("Кусок после шпателя: доля от «честного по объёму» размера. 1 — по объёму; 0.3 — маленький, влезает в обычную форму")]
        [Range(0.1f, 1f)][SerializeField] private float _pieceScale = 1f;
        [Tooltip("Подогнать радиус Sphere Collider на корне под размер теста")]
        [SerializeField] private bool _fitCollider = true;

        [Header("Мягкость")]
        [Tooltip("Насколько оседает лёжа (0.2 — на 20% ниже, шире снизу)")]
        [Range(0f, 0.5f)][SerializeField] private float _sag = 0.2f;
        [Tooltip("За сколько секунд оседает")]
        [Min(0.2f)][SerializeField] private float _spreadSeconds = 6f;
        [Tooltip("Насколько сильно трясётся от рывков")]
        [Range(0f, 0.1f)][SerializeField] private float _jiggle = 0.03f;
        [Range(5f, 300f)][SerializeField] private float _stiffness = 70f;
        [Range(0.5f, 30f)][SerializeField] private float _damping = 4.5f;
        [Tooltip("Подтёков с боков")]
        [Range(0, 8)][SerializeField] private int _drips = 4;
        [Tooltip("Длина подтёков, доля радиуса")]
        [Range(0f, 0.6f)][SerializeField] private float _dripLength = 0.28f;

        [Header("Расстойка")]
        [Tooltip("Сколько секунд подходит в таре")]
        [Min(1f)][SerializeField] private float _proofSeconds = 40f;
        [Tooltip("Насколько вырастает (0.45 — радиус +45%)")]
        [Range(0f, 1f)][SerializeField] private float _proofGrow = 0.45f;
        [Tooltip("Подходит только в таре (банке)")]
        [SerializeField] private bool _needContainer = true;
        [Tooltip("Горячее этой температуры тары — не подходит, °C")]
        [SerializeField] private float _maxTempC = 60f;
        [Range(0, 60)][SerializeField] private int _bubbles = 22;

        [Header("Шпатель")]
        [Tooltip("На сколько равных кусков делится шар")]
        [Range(2, 8)][SerializeField] private int _parts = 4;

        [Header("Выпечка (на решётке в духовке)")]
        [Tooltip("Во что превращается, когда испеклось. Пусто — тот же продукт, состояние «Запечённое»")]
        [SerializeField] private ProductDef _bakedProduct;
        [Tooltip("Материал корочки. Пусто — тесто подрумянивается цветом Crust Color")]
        [SerializeField] private Material _crustMaterial;
        [SerializeField] private Color _crustColor = new Color(0.86f, 0.55f, 0.24f);
        [Tooltip("С какой температуры духовки печётся, °C")]
        [SerializeField] private float _bakeFromC = 140f;
        [Tooltip("Сколько секунд печётся (при закрытой духовке)")]
        [Min(1f)][SerializeField] private float _bakeSeconds = 40f;
        [Tooltip("Насколько подрастает в духовке")]
        [Range(0f, 0.6f)][SerializeField] private float _bakeRise = 0.18f;

        [Header("Укладка на решётку (ПКМ с куском в руке)")]
        [Tooltip("ПКМ по решётке духовки — кусок кладётся ровно, вплотную к соседкам")]
        [SerializeField] private bool _placeOnGrill = true;
        [Tooltip("Дальность укладки, м")]
        [SerializeField] private float _placeRange = 4f;
        [Tooltip("Зазор между соседками при укладке, доля суммы радиусов (1.03 — почти вплотную)")]
        [Range(1f, 1.3f)][SerializeField] private float _placeGap = 1.03f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly SyncVar<byte> _proof = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _bake = new SyncVar<byte>(0);
        private float _bakeProgress;
        private bool _bakingNow, _crustSwap;
        private int _bakeQuarter = -1;
        private MaterialPropertyBlock _crustMpb;
        private bool _crustStartSet;
        private Color _crustStart, _crustEnd;
        private readonly SyncVar<bool> _isPiece = new SyncVar<bool>(false);
        private readonly SyncVar<byte> _pieceCount = new SyncVar<byte>(0);   // порций в одном куске (после первого удара шпателем)
        private readonly SyncVar<float> _scaleNet = new SyncVar<float>(0f);  // размер куска от шпателя (0 — Piece Scale из префаба)
        private Vector3 _holdAt;
        private float _holdUntil;
        private BakeOven _placedOven;               // лежит на решётке этой духовки (кинематика, не катается)
        private Vector3 _placedOvenPos;
        private BakeOven _checkOven;
        private readonly RaycastHit[] _rayHits = new RaycastHit[24];
        private readonly Collider[] _overlap = new Collider[24];

        private FoodPiece _food;
        private SphereCollider _sphere;
        private Transform _modelT;
        private Mesh _mesh;
        private MeshRenderer _mr;
        private Vector3[] _dir;           // единичные направления вершин шара
        private Vector3[] _work;
        private Vector3[] _dripDir;       // азимуты подтёков (в плоскости, единичные)
        private float[] _dripLen, _dripPhase, _dripElev;
        private Vector3[] _bubbleDir;
        private float[] _bubbleSize;
        private float _spread, _stillTime;
        private Vector3 _j, _jVel, _prevPos, _prevVel, _acc;
        private float _progress;
        private int _lastQuarter = -1;
        private float _hintAt;
        private bool _proofedSwap;
        private readonly List<Vector3> _nrm = new List<Vector3>();
        private const int Seg = 48, Rings = 30;
        private float _idleAcc;
        private byte _lastProofShown = 255;
        private byte _lastBakeShown = 255;
        private bool _bakedFrozen;

        public bool IsProofed => _proof.Value >= 255;
        public float Bake01 => _bake.Value / 255f;
        public bool IsBaked => _bake.Value >= 255;
        /// <summary>Печётся прямо сейчас (для духовки — счёт и давление). Сервер.</summary>
        public bool IsBakingNow => base.IsServerInitialized && _bakingNow;
        public float Proof01 => _proof.Value / 255f;
        public bool IsPiece => _isPiece.Value;
        public int Parts => _parts;

        // ───────────────────────── ЖИЗНЬ ─────────────────────────

        private void Awake()
        {
            _food = GetComponent<FoodPiece>();
            _sphere = GetComponent<SphereCollider>();
            BuildMesh();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _progress = _proof.Value / 255f;
        }

        private void BuildMesh()
        {
            var go = new GameObject("DoughModel (auto)");
            go.layer = gameObject.layer;
            _modelT = go.transform;
            _modelT.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            _mr = go.AddComponent<MeshRenderer>();
            _mr.sharedMaterial = _material;
            // если на корне был свой меш (заглушка для редактора) — прячем
            MeshRenderer own = GetComponent<MeshRenderer>();
            if (own != null) own.enabled = false;

            const int seg = Seg, rings = Rings;
            var dirs = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float th = Mathf.PI * r / rings;
                for (int s = 0; s <= seg; s++)
                {
                    float ph = 2f * Mathf.PI * s / seg;
                    dirs.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)));
                    uv.Add(new Vector2(s / (float)seg, 1f - r / (float)rings));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + seg + 1;
                    // лицевая сторона наружу (Unity: по часовой, если смотреть снаружи)
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
                }
            _dir = dirs.ToArray();
            _work = new Vector3[_dir.Length];
            _mesh = new Mesh { name = "Dough (auto)" };
            _mesh.MarkDynamic();
            _mesh.vertices = _dir;
            _mesh.uv = uv.ToArray();
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            go.GetComponent<MeshFilter>().sharedMesh = _mesh;

            var rnd = new System.Random(GetInstanceID() ^ 0x5eed);
            _dripDir = new Vector3[_drips]; _dripLen = new float[_drips]; _dripPhase = new float[_drips]; _dripElev = new float[_drips];
            for (int i = 0; i < _drips; i++)
            {
                float a = (i + (float)rnd.NextDouble() * 0.6f) / Mathf.Max(1, _drips) * Mathf.PI * 2f;
                _dripDir[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                _dripLen[i] = 0.6f + 0.4f * (float)rnd.NextDouble();
                _dripPhase[i] = (float)rnd.NextDouble() * 6.28f;
                _dripElev[i] = Mathf.Lerp(0.15f, 0.45f, (float)rnd.NextDouble());   // насколько ниже экватора
            }
            _bubbleDir = new Vector3[_bubbles]; _bubbleSize = new float[_bubbles];
            for (int i = 0; i < _bubbles; i++)
            {
                Vector3 d = new Vector3((float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1);
                _bubbleDir[i] = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.up;
                _bubbleSize[i] = Mathf.Lerp(0.2f, 0.4f, (float)rnd.NextDouble());
            }
            _prevPos = transform.position;
        }

        /// <summary>
        /// Корочка: плавно «тесто → золотистое → корочка» (покраска материала теста),
        /// в самом конце — материал корочки (если задан). Без скачков цвета.
        /// </summary>
        private void ApplyCrust()
        {
            float k = Bake01;
            if (k <= 0.001f || _mr == null) return;
            if (!_crustStartSet)
            {
                _crustStartSet = true;
                _crustStart = BakeColor.BaseOf(_mr.sharedMaterial);
                Color cc = BakeColor.BaseOf(_crustMaterial);
                _crustEnd = _crustMaterial != null && cc.r + cc.g + cc.b < 2.7f ? cc : _crustColor;
            }
            bool done = k >= 0.985f || IsBaked;
            if (done && _crustMaterial != null)
            {
                if (!_crustSwap) { _crustSwap = true; _mr.sharedMaterial = _crustMaterial; }
                BakeColor.Tint(_mr, ref _crustMpb, null);
                return;
            }
            BakeColor.Tint(_mr, ref _crustMpb, BakeColor.Ramp(_crustStart, BakeColor.DefaultGold, _crustEnd, k));
        }

        private float Radius()
        {
            float baseR = BaseRadius();
            float r = baseR * (1f + _proofGrow * Smooth(Proof01)) * (1f + _bakeRise * Smooth(Bake01));
            // пампушки рядом: растут, пока не упрутся в соседку — друг в друга не влезают (делят зазор по размеру)
            if (r > baseR && _neighborCap > 0f) r = Mathf.Max(baseR, Mathf.Min(r, _neighborCap));
            return r;
        }

        /// <summary>Радиус без расстойки и выпечки.</summary>
        private float BaseRadius()
        {
            int n = _food != null ? Mathf.Max(1, _food.Count) : _refPortions;
            float ps = _scaleNet.Value > 0.01f ? _scaleNet.Value : _pieceScale;
            return RadiusFor(n) * (_isPiece.Value ? ps : 1f);
        }

        private bool _stuck;

        /// <summary>Испеклась вплотную к соседке — слиплись кучкой (стоит на месте). Шпатель отделяет по одной.</summary>
        public bool IsStuck => _stuck;

        [Server]
        private void ServerStickToNeighbours()
        {
            float my = Radius();
            Vector3 p = _modelT != null ? _modelT.position : transform.position;
            int n = 0;
            for (int i = 0; i < _allBlobs.Count; i++)
            {
                DoughBlob o = _allBlobs[i];
                if (o == null || o == this || o._food == null || o._food.IsConsumed || o._food.IsHeld) continue;
                Vector3 op = o._modelT != null ? o._modelT.position : o.transform.position;
                if (Vector3.Distance(p, op) <= (my + o.Radius()) * 1.12f) n++;
            }
            if (n == 0) return;
            _stuck = true;
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.isKinematic = true; }
            if (_log) Debug.Log($"[Тесто] «{name}»: испеклась вплотную к соседкам ({n}) — слиплись кучкой; шпатель ЛКМ — отделить", this);
        }

        /// <summary>Шпатель: отделить испечённую от кучки — отскакивает в сторону. Сервер.</summary>
        [Server]
        public bool ServerUnstick(Vector3 dir, out string why)
        {
            why = null;
            if (!_stuck) { why = "не слиплась — бери руками"; return false; }
            _stuck = false;
            _placedOven = null; _holdUntil = 0f;
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = false; rb.WakeUp(); rb.AddForce((dir.normalized + Vector3.up * 0.6f).normalized * 1.2f, ForceMode.VelocityChange); }
            if (_log) Debug.Log($"[Тесто] «{name}»: отделили от кучки", this);
            return true;
        }

        private static readonly List<DoughBlob> _allBlobs = new List<DoughBlob>();
        private float _neighborCap, _capShown = -1f, _capAt;

        private void OnEnable() { if (!_allBlobs.Contains(this)) _allBlobs.Add(this); }
        private void OnDisable() => _allBlobs.Remove(this);

        /// <summary>Сколько можно вырасти, чтобы не влезть в соседний кусок теста (0 — соседей нет).</summary>
        private void UpdateNeighborCap()
        {
            if (Time.time < _capAt) return;
            _capAt = Time.time + 0.2f;
            float my = BaseRadius();
            Vector3 p = _modelT != null ? _modelT.position : transform.position;
            float cap = float.MaxValue;
            for (int i = 0; i < _allBlobs.Count; i++)
            {
                DoughBlob o = _allBlobs[i];
                if (o == null || o == this || o._food == null || o._food.IsConsumed || o._food.IsHeld) continue;
                float ob = o.BaseRadius();
                Vector3 op = o._modelT != null ? o._modelT.position : o.transform.position;
                float d = Vector3.Distance(p, op);
                if (d > (my + ob) * 2.5f) continue;
                cap = Mathf.Min(cap, d * my / Mathf.Max(1e-4f, my + ob) * 1.02f);
            }
            _neighborCap = cap < float.MaxValue ? cap : 0f;
        }

        /// <summary>Радиус шара из n порций (без расстойки), м. Для мешалки — откуда выкидывать шар.</summary>
        public float RadiusFor(int portions) =>
            _refRadius * _sizeScale * Mathf.Pow(Mathf.Max(1, portions) / (float)_refPortions, 1f / 3f);

        private static float Smooth(float k) => k * k * (3f - 2f * k);

        private void Update()
        {
            if (base.IsServerInitialized) { ServerHoldPlaced(); ServerBake(Time.deltaTime); ServerTick(Time.deltaTime); }
        }

        /// <summary>Лежит на решётке в горячей духовке — печётся в булку.</summary>
        private void ServerBake(float dt)
        {
            _bakingNow = false;
            if (_food == null || _food.IsConsumed || _food.IsHeld || IsBaked || dt <= 0f) return;
            Vector3 p = _modelT != null ? _modelT.position : transform.position;
            BakeOven oven = BakeOven.Find(p);
            if (oven == null || oven.TemperatureC < _bakeFromC) return;
            _bakingNow = true;
            float speed = Mathf.Clamp(oven.TemperatureC / 180f, 0.6f, 1.5f) * oven.BakeSpeed;
            _bakeProgress = Mathf.Min(1f, Mathf.Max(_bakeProgress, Bake01) + dt * speed / _bakeSeconds);
            byte b = (byte)Mathf.RoundToInt(_bakeProgress * 255f);
            if (b != _bake.Value) _bake.Value = b;
            int q = Mathf.FloorToInt(_bakeProgress * 4f);
            if (_log && q != _bakeQuarter && q > 0 && q < 4) Debug.Log($"[Тесто] «{name}» печётся в духовке: {q * 25}% ({oven.TemperatureC:0} °C)", this);
            _bakeQuarter = q;
            if (_bakeProgress >= 1f)
            {
                ServerStickToNeighbours();
                _food.ServerSetProductState(_bakedProduct, FoodState.Baked);
                if (_log) Debug.Log($"[Тесто] «{name}»: ИСПЕКЛОСЬ — {(_bakedProduct != null ? _bakedProduct.Title : "булка")}" +
                                    (IsProofed ? "" : " (не подошло — вышло плоским)"), this);
            }
        }

        private void LateUpdate()
        {
            UpdateNeighborCap();
            if (Mathf.Abs(_capShown - _neighborCap) > 1e-4f) { _capShown = _neighborCap; _idleAcc = 1f; }   // сосед подвинулся — перестроить
            Deform(Time.deltaTime);
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        private void ServerTick(float dt)
        {
            if (_food == null || _food.IsConsumed || IsProofed || _bake.Value > 0 || dt <= 0f) return;
            Cookware cw = ContainerNow(_modelT != null ? _modelT.position : transform.position, Radius());
            bool ok = !_food.IsHeld && (!_needContainer || cw != null);
            if (!ok && !_food.IsHeld && _log && Time.time >= _hintAt)
            {
                _hintAt = Time.time + 6f;
                Debug.Log($"[Тесто] «{name}»: не в таре — положи в банку/ведро/корыто (шар должен касаться полости тары), " +
                          "или выключи Need Container", this);
            }
            if (ok && cw != null)
            {
                CookwareHeat h = cw.GetComponent<CookwareHeat>();
                if (h != null && h.TemperatureC > _maxTempC)
                {
                    ok = false;
                    if (_log && Time.time >= _hintAt) { _hintAt = Time.time + 5f; Debug.Log($"[Тесто] «{name}»: в «{cw.name}» {h.TemperatureC:0} °C — горячо, не подходит", this); }
                }
            }
            if (!ok) return;
            _progress = Mathf.Min(1f, _progress + dt / _proofSeconds);
            _food.ServerAddWork(FoodProcess.Rest, dt);          // правило «Замешано → Дать подойти → Подошло» (если есть)
            byte b = (byte)Mathf.RoundToInt(_progress * 255f);
            if (b != _proof.Value) _proof.Value = b;
            int q = Mathf.FloorToInt(_progress * 4f);
            if (_log && q != _lastQuarter)
            {
                _lastQuarter = q;
                if (q > 0 && q < 4) Debug.Log($"[Тесто] «{name}» подходит в «{(cw != null ? cw.name : "—")}»: {q * 25}%", this);
                else if (q >= 4) Debug.Log($"[Тесто] «{name}»: ПОДОШЛО — дели шпателем на {_parts} куска", this);
            }
        }

        /// <summary>Тесто в таре не растворяется — лежит куском (и подходит).</summary>
        public bool ServerHandlePut(Cookware cw, out bool put)
        {
            put = false;
            return true;
        }

        private Cookware _boxCw;
        private float _boxAt;

        /// <summary>Тара под шаром — пересчёт 2 раза в секунду (габариты тары считать каждый кадр незачем).</summary>
        private Cookware ContainerNow(Vector3 p, float r)
        {
            if (Time.time < _boxAt) return _boxCw;
            _boxAt = Time.time + 0.5f;
            _boxCw = FindCookware(p, r);
            return _boxCw;
        }

        /// <summary>
        /// Тара, в которой лежит шар:
        ///   • шар касается полости тары (Inside Volume) — или
        ///   • шар лежит В габаритах тары: центр над тарой по горизонтали, низ шара ниже её края
        ///     (большой шар в мелком корыте/банке торчит наружу, но лежит в ней).
        /// Бетономешалка не в счёт — в ней тесто месят, а не держат.
        /// </summary>
        private static Cookware FindCookware(Vector3 p, float radius)
        {
            Cookware best = null;
            float bestD = float.MaxValue;
            foreach (Cookware cw in Cookware.All)
            {
                if (cw == null || cw.GetComponent<MixerDough>() != null) continue;
                if (cw.IsInside(p)) return cw;
                Collider iv = cw.InsideVolume;
                if (iv != null && iv.enabled)
                {
                    if (iv.bounds.Contains(p)) return cw;
                    if ((iv.ClosestPoint(p) - p).sqrMagnitude <= radius * radius) return cw;
                }
                if (!MeshBounds(cw, out Bounds b)) continue;
                if (p.x < b.min.x || p.x > b.max.x || p.z < b.min.z || p.z > b.max.z) continue;
                if (p.y - radius * 0.7f > b.max.y || p.y < b.min.y) continue;
                float d = new Vector2(p.x - b.center.x, p.z - b.center.z).sqrMagnitude;
                if (d < bestD) { bestD = d; best = cw; }
            }
            return best;
        }

        private static bool MeshBounds(Cookware cw, out Bounds b)
        {
            b = default(Bounds);
            bool any = false;
            foreach (MeshRenderer r in cw.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled || (r.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        [Server]
        public void ServerSetProofed()
        {
            _progress = 1f;
            _proof.Value = 255;
        }

        [Server]
        public void ServerMarkPiece() { if (!_isPiece.Value) _isPiece.Value = true; }

        /// <summary>Размер куска (доля «честного по объёму»). Шпатель задаёт пампушкам. Сервер.</summary>
        [Server]
        public void ServerSetPieceScale(float scale) { _scaleNet.Value = Mathf.Clamp(scale, 0.05f, 1f); }

        /// <summary>
        /// Шпатель ПКМ: отщипнуть МАЛЕНЬКИЙ шарик — пампушку (Bun Portions порций), префабом пампушки.
        /// Можно и от целого шара, и от куска после ЛКМ. Тесто должно подойти.
        /// </summary>
        [Server]
        public bool ServerCutBun(GameObject bunPrefab, Vector3 dir, int bunPortions, out string why) =>
            ServerCutBun(bunPrefab, dir, bunPortions, 0f, out why);

        /// <summary>То же, bunScale — размер шарика (0 — как в префабе).</summary>
        [Server]
        public bool ServerCutBun(GameObject bunPrefab, Vector3 dir, int bunPortions, float bunScale, out string why)
        {
            why = null;
            if (_food == null || _food.IsConsumed) { why = "уже нет"; return false; }
            if (IsBaked) return ServerUnstick(dir, out why);
            if (!IsProofed) { why = $"ещё не подошло ({Proof01 * 100f:0}%) — подержи в банке"; return false; }
            if (_bake.Value > 0) { why = "уже печётся"; return false; }
            if (bunPrefab == null) { why = "у шпателя не задан Bun Prefab (префаб пампушки) — Борщ → 14/15"; return false; }
            int piece = Mathf.Max(1, bunPortions);
            if (_food.Count < piece * 2) { why = $"мало теста на пампушку (осталось {_food.Count} порц.) — этот кусок и есть пампушка, клади в духовку"; return false; }
            float r = Radius();
            Vector3 at = (_modelT != null ? _modelT.position : transform.position) + dir.normalized * r * 1.1f + Vector3.up * r * 0.3f;
            GameObject go = Instantiate(bunPrefab, at, Quaternion.identity);
            FoodPiece fp = go.GetComponent<FoodPiece>();
            if (fp != null) fp.ServerPreset(_food.Def, _food.State, _food.Clean, _food.Cook, _food.FormScore);
            base.Spawn(go);
            if (fp != null) fp.ServerSetCount(piece);
            DoughBlob db = go.GetComponent<DoughBlob>();
            if (db != null) { db.ServerSetProofed(); db.ServerMarkPiece(); if (bunScale > 0.01f) db.ServerSetPieceScale(bunScale); }
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.AddForce((dir.normalized + Vector3.up * 0.5f).normalized * 0.6f, ForceMode.VelocityChange);
            int left = _food.Count - piece;
            _food.ServerSetCount(left);
            if (_log) Debug.Log($"[Тесто] «{name}»: отщипнули ПАМПУШКУ ({piece} порц.), осталось {left}", this);
            return true;
        }

        /// <summary>
        /// Шпатель: отщипнуть один кусок. Возвращает текст для лога (null — не вышло, why — почему).
        /// </summary>
        [Server]
        public bool ServerCutPiece(GameObject piecePrefab, Vector3 dir, out string why)
        {
            why = null;
            if (_food == null || _food.IsConsumed) { why = "уже нет"; return false; }
            if (IsBaked) return ServerUnstick(dir, out why);                   // испечённые пампушки — отделить от кучки
            if (_isPiece.Value) { why = "это уже кусок — клади в форму"; return false; }
            if (!IsProofed) { why = $"ещё не подошло ({Proof01 * 100f:0}%) — подержи в банке"; return false; }
            if (_pieceCount.Value == 0) _pieceCount.Value = (byte)Mathf.Clamp(Mathf.RoundToInt(_food.Count / (float)_parts), 1, 255);
            int piece = _pieceCount.Value;
            if (_food.Count < piece * 2) { ServerMarkPiece(); why = "остался последний кусок"; return false; }

            GameObject prefab = piecePrefab != null ? piecePrefab : null;
            if (prefab == null) { why = "у шпателя не задан Piece Prefab (префаб теста)"; return false; }
            float r = Radius();
            Vector3 at = (_modelT != null ? _modelT.position : transform.position) + dir.normalized * r * 1.1f + Vector3.up * r * 0.3f;
            GameObject go = Instantiate(prefab, at, Quaternion.identity);
            FoodPiece fp = go.GetComponent<FoodPiece>();
            if (fp != null) fp.ServerPreset(_food.Def, _food.State, _food.Clean, _food.Cook, _food.FormScore);
            base.Spawn(go);
            if (fp != null) fp.ServerSetCount(piece);
            DoughBlob db = go.GetComponent<DoughBlob>();
            if (db != null) { db.ServerSetProofed(); db.ServerMarkPiece(); }
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.AddForce((dir.normalized + Vector3.up * 0.5f).normalized * 1.2f, ForceMode.VelocityChange);

            int left = _food.Count - piece;
            _food.ServerSetCount(left);
            if (left < piece * 2) ServerMarkPiece();
            if (_log) Debug.Log($"[Тесто] «{name}»: отщипнули кусок ({piece} порц.), осталось {left}" + (_isPiece.Value ? " — это последний кусок" : ""), this);
            return true;
        }

        // ───────────────────────── УКЛАДКА НА РЕШЁТКУ ─────────────────────────

        /// <summary>
        /// ПКМ с куском теста в руке, прицел на решётку духовки (или на тесто, что уже лежит на ней):
        /// кусок кладётся ровно — вплотную к ближайшей соседке, в сторону прицела.
        /// Далеко от всех — ложится, куда смотришь (новая кучка). ЛКМ — бросок как обычно.
        /// Решётку ищем по её плоскости (луч взгляда ∩ верх решётки), а не по попаданию в коллайдер —
        /// дверца, стенки и прутья не мешают.
        /// </summary>
        public bool ServerHandleSecondary(FoodPiece piece, UseContext ctx)
        {
            if (!_placeOnGrill || !_isPiece.Value || IsBaked || ctx.Hand == null) return false;
            Vector3 dir = ctx.Direction.sqrMagnitude > 1e-6f ? ctx.Direction.normalized : Vector3.forward;
            float r = Radius();
            float colR = _sphere != null ? _sphere.radius : r;

            // 1) смотрим прямо на тесто, что лежит на решётке, — целимся в него
            BakeOven oven = null;
            Collider grill = null;
            Vector3 aimPoint = Vector3.zero;
            bool aimed = false;
            int n = Physics.RaycastNonAlloc(ctx.Origin, dir, _rayHits, _placeRange, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _rayHits[i];
                if (h.collider == null || h.transform.IsChildOf(transform) || h.distance >= best) continue;
                DoughBlob ob = h.collider.GetComponentInParent<DoughBlob>();
                if (ob == null || ob == this) continue;
                BakeOven o = BakeOven.Find(ob.transform.position);
                if (o == null || !o.TryGetGrill(out Collider g)) continue;
                best = h.distance; oven = o; grill = g; aimPoint = h.point; aimed = true;
            }

            // 2) иначе — луч до плоскости верха решётки любой духовки, точка внутри решётки
            if (!aimed)
            {
                float bestT = float.MaxValue;
                for (int i = 0; i < BakeOven.All.Count; i++)
                {
                    BakeOven o = BakeOven.All[i];
                    if (o == null || !o.TryGetGrill(out Collider g)) continue;
                    if (Mathf.Abs(dir.y) < 1e-3f) continue;
                    float t = (g.bounds.max.y - ctx.Origin.y) / dir.y;
                    if (t <= 0f || t > _placeRange || t >= bestT) continue;
                    Vector3 p = ctx.Origin + dir * t;
                    Bounds gb0 = g.bounds;
                    float pad = r * 1.5f;
                    if (p.x < gb0.min.x - pad || p.x > gb0.max.x + pad || p.z < gb0.min.z - pad || p.z > gb0.max.z + pad) continue;
                    bestT = t; oven = o; grill = g; aimPoint = p; aimed = true;
                }
            }
            if (!aimed)
            {
                if (_log) Debug.Log($"[Тесто] «{name}»: ПКМ — не вижу решётки духовки (смотри на решётку, дверца открыта, ближе {_placeRange:0.#} м)", this);
                return false;
            }

            float top = grill.bounds.max.y;
            Bounds gb = grill.bounds, cb = oven.ChamberBounds;
            float minX = Mathf.Max(gb.min.x, cb.min.x) + r, maxX = Mathf.Min(gb.max.x, cb.max.x) - r;
            float minZ = Mathf.Max(gb.min.z, cb.min.z) + r, maxZ = Mathf.Min(gb.max.z, cb.max.z) - r;
            if (minX > maxX || minZ > maxZ)
            {
                // решётка уже куска — кладём в центр
                minX = maxX = gb.center.x; minZ = maxZ = gb.center.z;
            }
            float y = top + colR + 0.005f;
            Vector3 aim = new Vector3(Mathf.Clamp(aimPoint.x, minX, maxX), y, Mathf.Clamp(aimPoint.z, minZ, maxZ));
            _checkOven = oven;

            // соседки на этой решётке
            var near = new List<DoughBlob>();
            for (int i = 0; i < _allBlobs.Count; i++)
            {
                DoughBlob o = _allBlobs[i];
                if (o == null || o == this || o._food == null || o._food.IsConsumed || o._food.IsHeld) continue;
                Vector3 op = o.transform.position;
                if (BakeOven.Find(op) != oven) continue;
                if (Mathf.Abs(op.y - top) > o.Radius() * 2.5f + 0.1f) continue;
                near.Add(o);
            }

            Vector3 target = Vector3.zero;
            bool found = false;
            float bestD = float.MaxValue;
            bool farFromAll = true;
            for (int i = 0; i < near.Count; i++)
            {
                Vector3 op = near[i].transform.position;
                float d = new Vector2(op.x - aim.x, op.z - aim.z).magnitude;
                if (d < (r + near[i].Radius()) * 3f) { farFromAll = false; break; }
            }
            if (farFromAll && FreeAt(aim, r, colR, near, minX, maxX, minZ, maxZ)) { target = aim; found = true; }
            else
            {
                // 12 мест вокруг каждой соседки — берём свободное, ближайшее к прицелу
                for (int i = 0; i < near.Count; i++)
                {
                    Vector3 op = near[i].transform.position;
                    float dist = (r + near[i].Radius()) * _placeGap;
                    for (int k = 0; k < 12; k++)
                    {
                        float a = k * Mathf.PI / 6f;
                        Vector3 c = new Vector3(op.x + Mathf.Cos(a) * dist, y, op.z + Mathf.Sin(a) * dist);
                        float dd = (c - aim).sqrMagnitude;
                        if (dd >= bestD) continue;
                        if (!FreeAt(c, r, colR, near, minX, maxX, minZ, maxZ)) continue;
                        bestD = dd; target = c; found = true;
                    }
                }
                if (!found && FreeAt(aim, r, colR, near, minX, maxX, minZ, maxZ)) { target = aim; found = true; }
            }
            if (!found) { if (_log) Debug.Log($"[Тесто] «{name}»: на решётке нет места рядом с прицелом", this); return true; }

            ctx.Hand.ServerDrop(dir, 0f);
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.isKinematic = true; }
            transform.SetPositionAndRotation(target, Quaternion.identity);
            if (rb != null) { rb.position = target; rb.rotation = Quaternion.identity; }
            _holdAt = target;
            _holdUntil = Time.time + 1.0f;              // поздние пакеты позиции от клиента не утащат кусок
            _placedOven = oven;
            _placedOvenPos = oven.transform.position;
            _capAt = 0f; _boxAt = 0f;
            if (_log) Debug.Log($"[Тесто] «{name}»: уложили на решётку «{oven.name}»" + (near.Count > 0 ? $" вплотную к соседкам ({near.Count} на решётке)" : ""), this);
            return true;
        }

        /// <summary>Место свободно: в пределах решётки, не влезает в соседок и ни во что твёрдое.</summary>
        private bool FreeAt(Vector3 c, float r, float colR, List<DoughBlob> near, float minX, float maxX, float minZ, float maxZ)
        {
            if (c.x < minX - 1e-4f || c.x > maxX + 1e-4f || c.z < minZ - 1e-4f || c.z > maxZ + 1e-4f) return false;
            for (int i = 0; i < near.Count; i++)
            {
                Vector3 op = near[i].transform.position;
                float need = (r + near[i].Radius()) * (_placeGap - 0.01f);
                if (new Vector2(op.x - c.x, op.z - c.z).magnitude < need) return false;
            }
            int n = Physics.OverlapSphereNonAlloc(c + Vector3.up * 0.01f, colR * 0.9f, _overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider o = _overlap[i];
                if (o == null || o.transform.IsChildOf(transform)) continue;
                if (o.GetComponentInParent<DoughBlob>() != null) continue;     // соседок уже проверили по радиусу
                if (_checkOven != null && o.GetComponentInParent<BakeOven>() == _checkOven) continue;   // сама духовка: стенки уже учли рамкой решётки
                if (o.GetComponentInParent<PlayerInteractor>() != null) continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Уложенный на решётку кусок стоит на месте (кинематика): первую секунду держим точку,
        /// дальше — пока не возьмут в руку, не отделят шпателем или не сдвинут духовку (тогда — обычная физика).
        /// </summary>
        private void ServerHoldPlaced()
        {
            if (_placedOven == null) return;
            Rigidbody rb = GetComponent<Rigidbody>();
            bool release = _food == null || _food.IsHeld || _food.IsConsumed
                           || (_placedOven.transform.position - _placedOvenPos).sqrMagnitude > 1e-4f;
            if (release)
            {
                _placedOven = null; _holdUntil = 0f;
                if (rb != null && !_stuck && (_food == null || !_food.IsHeld)) { rb.isKinematic = false; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                return;
            }
            if (rb != null && !rb.isKinematic) rb.isKinematic = true;
            if (Time.time < _holdUntil && (transform.position - _holdAt).sqrMagnitude > 1e-6f)
            {
                transform.position = _holdAt;
                if (rb != null) rb.position = _holdAt;
            }
        }

        // ───────────────────────── ПОДСКАЗКА ВЫПЕЧКИ ─────────────────────────

        [Header("Подсказка выпечки")]
        [Tooltip("Над духовкой — табличка: температура, % и сколько ещё ждать; над каждым куском — полоска")]
        [SerializeField] private bool _bakeHud = true;

        private static GUIStyle _hudStyle;
        private static int _hudFrame = -1;
        private static readonly List<BakeOven> _hudOvens = new List<BakeOven>();

        /// <summary>Скорость выпечки в этой духовке сейчас (как на сервере). 0 — холодно.</summary>
        private float BakeSpeedIn(BakeOven oven)
        {
            if (oven == null || oven.TemperatureC < _bakeFromC) return 0f;
            return Mathf.Clamp(oven.TemperatureC / 180f, 0.6f, 1.5f) * oven.BakeSpeed;
        }

        private void OnGUI()
        {
            if (!_bakeHud || Event.current.type != EventType.Repaint) return;
            if (_food == null || _food.IsConsumed || _food.IsHeld) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 pos = transform.position;
            if ((pos - cam.transform.position).sqrMagnitude > 12f * 12f) return;
            BakeOven oven = BakeOven.Find(pos);
            if (oven == null) return;
            if (_hudFrame != Time.frameCount) { _hudFrame = Time.frameCount; _hudOvens.Clear(); }
            if (_hudStyle == null)
                _hudStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 13, wordWrap = false, richText = true };

            // полоска над куском
            Vector3 sp = cam.WorldToScreenPoint(pos + Vector3.up * (Radius() + 0.06f));
            if (sp.z > 0f)
            {
                float w = 44f, h = 6f;
                Rect r = new Rect(sp.x - w * 0.5f, Screen.height - sp.y - h, w, h);
                Bar(r, Bake01, IsBaked ? new Color(0.35f, 0.85f, 0.3f) : new Color(0.95f, 0.6f, 0.2f));
            }

            // табличка над духовкой — одна на духовку
            if (_hudOvens.Contains(oven)) return;
            _hudOvens.Add(oven);
            int count = 0, done = 0;
            float slowest = 1f;
            for (int i = 0; i < _allBlobs.Count; i++)
            {
                DoughBlob o = _allBlobs[i];
                if (o == null || o._food == null || o._food.IsConsumed || o._food.IsHeld) continue;
                if (BakeOven.Find(o.transform.position) != oven) continue;
                count++;
                if (o.IsBaked) done++;
                else slowest = Mathf.Min(slowest, o.Bake01);
            }
            if (count == 0) return;
            float speed = BakeSpeedIn(oven);
            string text;
            float prog = done == count ? 1f : slowest;
            if (done == count) text = $"<color=#7CFC6A>Готово ✔</color> испеклось {count} шт. — доставай";
            else if (speed <= 0f)
                text = $"<color=#FF8A6A>Холодно: {oven.TemperatureC:0} °C</color>, нужно от {_bakeFromC:0} °C — поставь духовку на огонь (O), раздуй костёр · {done}/{count} готово";
            else
            {
                float eta = (1f - slowest) * _bakeSeconds / Mathf.Max(0.01f, speed);
                text = $"Печётся: {Mathf.RoundToInt(slowest * 100f)}% · ещё ~{Mathf.CeilToInt(eta)} с · {oven.TemperatureC:0} °C · {done}/{count} готово";
                if (oven.DoorOpen) text += "\n<color=#FFD36A>дверца открыта — печётся медленнее (L — закрыть)</color>";
            }
            Bounds cb = oven.ChamberBounds;
            Vector3 top = new Vector3(cb.center.x, cb.max.y + 0.35f, cb.center.z);
            Vector3 ts = cam.WorldToScreenPoint(top);
            if (ts.z <= 0f) return;
            Vector2 size = _hudStyle.CalcSize(new GUIContent(text));
            float bw = Mathf.Max(size.x + 12f, 180f);
            Rect box = new Rect(ts.x - bw * 0.5f, Screen.height - ts.y - size.y - 16f, bw, size.y + 16f);
            GUI.Box(box, text, _hudStyle);
            Bar(new Rect(box.x + 6f, box.yMax - 9f, box.width - 12f, 6f), prog, done == count ? new Color(0.35f, 0.85f, 0.3f) : new Color(0.95f, 0.6f, 0.2f));
        }

        private static void Bar(Rect r, float k, Color fill)
        {
            Color was = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(k), r.height), Texture2D.whiteTexture);
            GUI.color = was;
        }

        // ───────────────────────── ВИД ─────────────────────────

        private void Deform(float dt)
        {
            if (_mesh == null || dt <= 1e-5f) return;
            float R = Radius();
            float proof = Smooth(Proof01);

            // материал подошедшего
            if (!_proofedSwap && _proofedMaterial != null && Proof01 >= 0.35f) { _proofedSwap = true; _mr.sharedMaterial = _proofedMaterial; }
            ApplyCrust();

            // движение → тряска и оседание
            Vector3 pos = transform.position;
            Vector3 vel = (pos - _prevPos) / dt;
            Vector3 acc = (vel - _prevVel) / dt;
            _prevPos = pos; _prevVel = vel;
            if (vel.sqrMagnitude > 900f) { acc = Vector3.zero; vel = Vector3.zero; }    // телепорт / спавн
            _acc = Vector3.Lerp(_acc, acc, 1f - Mathf.Exp(-dt * 25f));
            bool moving = vel.sqrMagnitude > 0.02f;
            _stillTime = moving ? 0f : _stillTime + dt;
            float spreadTarget = _stillTime > 0.4f ? 1f : 0.3f;
            if (Bake01 > 0.02f) spreadTarget = _spread;          // в духовке форма застывает — в руке не подтягивается
            float rate = spreadTarget > _spread ? 1f / _spreadSeconds : 1f / 0.6f;
            _spread = Mathf.MoveTowards(_spread, spreadTarget, rate * dt);

            // испеклась — твёрдая булка: не трясётся, подтёки втягиваются, не «дышит»
            float firm = Smooth(Bake01);
            Vector3 accL = transform.InverseTransformDirection(_acc);
            Vector3 target = Vector3.ClampMagnitude(-accL * _jiggle * (1f - firm), 0.35f);
            int n = Mathf.Clamp(Mathf.CeilToInt(dt / 0.008f), 1, 8);
            float h = dt / n;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = (target - _j) * _stiffness - _jVel * _damping;
                _jVel += a * h; _j += _jVel * h;
            }
            if (_j.sqrMagnitude > 0.16f) _j = _j.normalized * 0.4f;

            // успокоилось — пересчитываем меш редко (подтёки «дышат» медленно)
            bool calm = !moving && Mathf.Abs(_spread - spreadTarget) < 1e-3f && _j.sqrMagnitude < 1e-8f && _jVel.sqrMagnitude < 1e-6f && _proof.Value == _lastProofShown && _bake.Value == _lastBakeShown;
            _lastProofShown = _proof.Value;
            _lastBakeShown = _bake.Value;
            if (calm && IsBaked && _bakedFrozen) return;          // испечённая и спокойная — меш не трогаем вовсе
            _bakedFrozen = calm && IsBaked;
            if (calm)
            {
                _idleAcc += dt;
                if (_idleAcc < 0.15f) return;
                _idleAcc = 0f;
            }

            Vector3 up = transform.InverseTransformDirection(Vector3.up).normalized;
            Vector3 down = -up;
            Vector3 ax = Vector3.ProjectOnPlane(Vector3.right, up);
            if (ax.sqrMagnitude < 1e-4f) ax = Vector3.ProjectOnPlane(Vector3.forward, up);
            ax.Normalize();
            Vector3 az = Vector3.Cross(up, ax);
            float jm = _j.magnitude;
            Vector3 jd = jm > 1e-5f ? _j / jm : Vector3.zero;
            float sag = _sag * _spread;
            float time = Time.time;

            // подтёки: направления — ниже экватора, свисают вниз
            int dn = _dripDir.Length;
            var dripW = new Vector3[dn]; var dripL = new float[dn];
            for (int k = 0; k < dn; k++)
            {
                Vector3 hd = (ax * _dripDir[k].x + az * _dripDir[k].z).normalized;
                dripW[k] = (hd * Mathf.Cos(_dripElev[k]) + down * Mathf.Sin(_dripElev[k])).normalized;
                dripL[k] = _dripLength * _dripLen[k] * (0.55f + 0.45f * _spread) * (1f + 0.15f * (1f - firm) * Mathf.Sin(time * 0.8f + _dripPhase[k])) * (1f - firm);
            }

            for (int i = 0; i < _dir.Length; i++)
            {
                Vector3 d = _dir[i];
                float r = R;
                // пузыри подошедшего теста
                if (proof > 0.01f)
                    for (int b = 0; b < _bubbleDir.Length; b++)
                    {
                        float c = Vector3.Dot(d, _bubbleDir[b]);
                        float ang = 1f - c;
                        float s = _bubbleSize[b] * _bubbleSize[b] * 0.5f;
                        if (ang < s) { float t = 1f - ang / s; t = t * t * (3f - 2f * t); r += R * 0.055f * proof * t; }
                    }
                Vector3 p = d * r;
                // оседание: дно на месте, верх ниже, низ шире
                float hgt = Vector3.Dot(p, up);
                Vector3 horiz = p - up * hgt;
                float hn = (hgt / r + 1f) * 0.5f;                      // 0 — низ, 1 — верх
                float newH = (hgt + r) * (1f - sag) - r;
                horiz *= 1f + sag * 0.9f * (1f - hn) + sag * 0.25f;
                p = horiz + up * newH;
                // подтёки
                for (int k = 0; k < dn; k++)
                {
                    float c = Vector3.Dot(d, dripW[k]);
                    if (c < 0.82f) continue;
                    float w = (c - 0.82f) / 0.18f;
                    w = w * w * (3f - 2f * w);
                    p += (down * 0.8f + dripW[k] * 0.2f) * (dripL[k] * r * w);
                }
                // тряска: растяжение вдоль рывка, сжатие поперёк
                if (jm > 1e-4f)
                {
                    float along = Vector3.Dot(p, jd);
                    p += jd * along * jm - (p - jd * along) * jm * 0.5f;
                    p += jd * Mathf.Sin(time * 22f + along * 30f) * jm * r * 0.05f * (1f - firm);   // мелкая дрожь
                }
                _work[i] = p;
            }
            _mesh.vertices = _work;
            _mesh.RecalculateNormals();
            SmoothSeams();
            _mesh.RecalculateBounds();

            if (_fitCollider && _sphere != null)
            {
                float want = R * (1f - sag * 0.5f);
                if (Mathf.Abs(_sphere.radius - want) > want * 0.03f) _sphere.radius = want;
            }
        }

        /// <summary>Шов развёртки и полюса — общие нормали, иначе на шаре видна «складка».</summary>
        private void SmoothSeams()
        {
            _mesh.GetNormals(_nrm);
            if (_nrm.Count != _dir.Length) return;
            int row = Seg + 1;
            for (int r = 1; r < Rings; r++)
            {
                int a = r * row, b = a + Seg;
                Vector3 n = (_nrm[a] + _nrm[b]).normalized;
                _nrm[a] = n; _nrm[b] = n;
            }
            for (int pole = 0; pole < 2; pole++)
            {
                int start = pole == 0 ? 0 : Rings * row;
                Vector3 sum = Vector3.zero;
                for (int k = 0; k < row; k++) sum += _nrm[start + k];
                sum = sum.normalized;
                for (int k = 0; k < row; k++) _nrm[start + k] = sum;
            }
            _mesh.SetNormals(_nrm);
        }

        private void OnDestroy()
        {
            if (_mesh != null) SafeDestroy.Kill(_mesh);
        }
    }
}
