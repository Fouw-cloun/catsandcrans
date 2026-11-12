using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЛУЖА — пролитая жидкость (молоко, сливки, вода).
    ///
    ///   • РАЗМЕР ПО ОБЪЁМУ: площадь = объём ÷ толщина слоя. Вылил ведро —
    ///     большая лужа, стакан — маленькая. Объём порции и толщину задаёшь сам.
    ///   • РАСТЕКАНИЕ: новая лужа не появляется сразу во весь размер — плавно
    ///     растекается от точки падения, быстро в начале и медленно в конце.
    ///   • ШЕВЕЛЕНИЕ: край клякса и медленно «дышит», по поверхности идёт
    ///     лёгкая рябь (в шейдере), там, куда бьёт струя, — расходятся круги.
    ///   • ВЫСЫХАНИЕ: через Dry Delay начинает сохнуть — уменьшается и
    ///     бледнеет, потом исчезает совсем.
    ///   • МЯГКИЙ КРАЙ: полупрозрачная кромка (CatCooks/Liquid Surface), без
    ///     вырезанного диска.
    ///   • v4 — МУЛЬТЯШНЫЙ ВИД (Toon Look, по умолчанию): шейдер CatCooks/Milk Puddle (до v18.1 — Toon Puddle) —
    ///     чёткий край с тёмной обводкой, светлый бортик-мениск, плоская заливка,
    ///     белые блики-«бобы», круги от струи линиями, капли-спутники вокруг кляксы
    ///     и мокрый ореол на полу. Выключи — старый вид.
    ///   • v5 — СЛИЯНИЕ: лужа состоит из нескольких клякс (до 8). Пролил рядом —
    ///     к луже прирастает новая клякса, а не новая лужа. Две лужи одного продукта
    ///     коснулись — большая забирает малую, получается одно пятно с перемычкой.
    ///     Край стал спокойнее (Toon Edge Motion).
    ///
    /// По сети — продукт и объём; форму, рябь и круги каждый рисует сам.
    ///
    /// ═══ ПРЕФАБ «MilkPuddle» ═══
    /// Пустой объект: NetworkObject + LiquidPuddle. Больше ничего.
    /// </summary>
    public sealed class LiquidPuddle : NetworkBehaviour
    {
        [Header("Объём → размер")]
        [Tooltip("Объём одной порции, литров (как в таре: полное ведро = Capacity порций)")]
        [Min(0.01f)][SerializeField] private float _litresPerPortion = 1f;
        [Tooltip("Толщина слоя лужи, мм. Меньше — шире лужа при том же объёме.")]
        [Range(0.5f, 20f)][SerializeField] private float _thicknessMm = 3f;
        [Tooltip("Больше этого радиуса лужа не растёт, м")]
        [SerializeField] private float _maxRadius = 6f;

        [Header("Растекание")]
        [Tooltip("За сколько секунд растекается до полного размера")]
        [Min(0.1f)][SerializeField] private float _spreadSeconds = 2.5f;
        [Tooltip("Неровность края: 0 — круг, 0.4 — клякса")]
        [Range(0f, 0.6f)][SerializeField] private float _edgeNoise = 0.28f;
        [Tooltip("Как быстро край «дышит»")]
        [Range(0f, 1f)][SerializeField] private float _edgeBreath = 0.25f;
        [Tooltip("Ширина мягкой кромки, доля радиуса")]
        [Range(0.02f, 0.5f)][SerializeField] private float _softEdge = 0.18f;
        [SerializeField] private float _lift = 0.006f;

        [Header("Высыхание")]
        [Tooltip("Через сколько секунд после последнего пролива начинает сохнуть")]
        [SerializeField] private float _dryDelay = 25f;
        [Tooltip("Литров в секунду испаряется")]
        [SerializeField] private float _dryLitresPerSecond = 0.15f;
        [Tooltip("Любая лужа (даже ведро теста) высыхает не дольше чем за столько секунд после начала высыхания. 0 — без предела")]
        [Min(0f)][SerializeField] private float _dryAllSeconds = 30f;

        [Header("Вид")]
        [Tooltip("Пусто — CatCooks/Liquid Surface (или Milk Puddle, если включён Toon Look)")]
        [SerializeField] private Material _material;
        [Tooltip("v4: мультяшная лужа (CatCooks/Milk Puddle). Material выше при этом не используется")]
        [SerializeField] private bool _toonLook = true;
        [Tooltip("Капель-спутников вокруг кляксы")]
        [Range(0, 14)][SerializeField] private int _satellites = 7;

        [Header("v5: слияние и спокойный край")]
        [Tooltip("Как сильно шевелится край мультяшной лужи (0 — замер). Было 0.25 — слишком подвижно")]
        [Range(0f, 0.3f)][SerializeField] private float _toonEdgeMotion = 0.03f;
        [Tooltip("Лужи одного продукта, которые коснулись, сливаются в одну")]
        [SerializeField] private bool _merge = true;
        [Tooltip("Пролил ближе этого к краю лужи (м) — жидкость идёт в неё, а не в новую лужу")]
        [Range(0f, 1.5f)][SerializeField] private float _joinDistance = 0.5f;

        [Header("v8: лужи исчезают, меньше нагрузки")]
        [Tooltip("Лужа живёт не дольше этого (с) — потом сохнет, даже если в неё подливают. 0 — без предела")]
        [Min(0f)][SerializeField] private float _maxLife = 45f;
        [Tooltip("Сохнуть начинает не позже чем через столько секунд после последнего пролива")]
        [Min(0f)][SerializeField] private float _dryDelayMax = 10f;
        [Tooltip("Высохнуть полностью — за столько секунд (быстрее, чем Dry All Seconds)")]
        [Min(1f)][SerializeField] private float _dryTime = 12f;
        [Tooltip("Больше этого числа луж на сцене — самые старые сразу начинают сохнуть")]
        [Range(2, 60)][SerializeField] private int _maxPuddles = 12;
        [Tooltip("Как часто перестраивать форму спокойной лужи, с (растекается — чаще)")]
        [Range(0.05f, 1f)][SerializeField] private float _calmRebuild = 0.25f;

        [Header("v10.2: обтекает швы и дыры")]
        [Tooltip("Лужа проверяет пол под собой: швы, дыры, ступеньки и стоящие предметы обтекает — поверх не ложится")]
        [SerializeField] private bool _followFloor = true;
        [Tooltip("Перепад высоты пола, больше которого — «край» (шов, дыра, ступенька), м")]
        [Range(0.003f, 0.1f)][SerializeField] private float _floorStep = 0.015f;
        [Tooltip("Слои пола для проверки (пол, столы). Игроки и подвижные предметы пропускаются сами")]
        [SerializeField] private LayerMask _floorMask = ~0;
        [Tooltip("Шаг проверки пола, м (меньше — точнее по узким швам, но дороже)")]
        [Range(0.01f, 0.08f)][SerializeField] private float _floorCell = 0.03f;
        [Tooltip("v10.5: лужа остаётся на СВОЕЙ плитке (коллайдер пола под ней) — за щель на соседнюю плитку не льётся. " +
                 "Нужно, чтобы плитки были отдельными коллайдерами")]
        [SerializeField] private bool _stayOnTile = true;

        [Header("v11: поскользнуться на луже")]
        [Tooltip("Шанс поскользнуться при каждой проверке, пока ИДЁШЬ по луже (стоишь — не падаешь). 0.5 — для теста")]
        [Range(0f, 1f)][SerializeField] private float _slipChance = 0.5f;
        [Tooltip("Как часто проверять шанс, пока идёшь по луже, с")]
        [Range(0.1f, 3f)][SerializeField] private float _slipCheckEvery = 0.6f;
        [Tooltip("После того как встал — столько секунд не падаешь снова")]
        [Range(0f, 60f)][SerializeField] private float _slipCooldown = 10f;
        [Tooltip("Медленнее этого (м/с) — не поскальзываешься (крадёшься)")]
        [Range(0f, 5f)][SerializeField] private float _slipMinSpeed = 0.6f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly SyncVar<ushort> _product = new SyncVar<ushort>(0);
        /// <summary>Объём в сантилитрах (0.01 л) — ushort хватит до 655 л.</summary>
        private readonly SyncVar<ushort> _volumeCl = new SyncVar<ushort>(0);

        /// <summary>
        /// v5: кляксы лужи. x,y — смещение от корня лужи по X/Z, м; z — вес (литры);
        /// w — 1: появилась сразу (досталась от слитой лужи), 0: растекается из точки.
        /// </summary>
        private readonly SyncList<Vector4> _lobes = new SyncList<Vector4>();
        private const int MaxLobes = 8;
        private const int ToonSeg = 72;
        private readonly float[] _lobeR = new float[MaxLobes];
        private int _lobeSeen;
        private bool _growing, _absorbed;
        private float _mergeT;
        private readonly float[] _ray = new float[ToonSeg];
        private readonly float[] _rayTmp = new float[ToonSeg];

        private static readonly List<LiquidPuddle> All = new List<LiquidPuddle>();

        /// <summary>
        /// v12.2: префаб лужи «по умолчанию» — для тары без своего (банка, собранная AutoSetup).
        /// Его запоминает любой, у кого префаб назначен (бидон, ведро с Cookware Spill…).
        /// Без этого перелив из полной банки просто пропадал.
        /// </summary>
        public static LiquidPuddle DefaultPrefab { get; private set; }

        public static void RegisterDefault(LiquidPuddle prefab)
        {
            if (prefab != null && DefaultPrefab == null) DefaultPrefab = prefab;
        }
        private static Material _autoMat, _toonMat;
        private readonly List<Vector2> _u0 = new List<Vector2>();
        private readonly List<Vector2> _u1 = new List<Vector2>();
        private readonly List<Vector2> _u2 = new List<Vector2>();
        private bool _toon;
        private static readonly int DryId = Shader.PropertyToID("_Dry");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int RingsId = Shader.PropertyToID("_Rings");
        private static readonly int AgitationId = Shader.PropertyToID("_Agitation");

        private Mesh _mesh;
        private MeshRenderer _mr;
        private MaterialPropertyBlock _mpb;
        private float _radius, _spreadT;
        private float _lastPour, _volumeServer, _presetLitres, _dryFrom = -1f;
        private ushort _presetProduct;
        private float _rebuildT, _spawnTime, _builtR = -1f, _peakL, _lastL, _dryShown = 1f;
        private bool _dryingSeen;
        private bool _forceDry;
        private readonly Vector4[] _rings = { new Vector4(0, 0, 0, -1), new Vector4(0, 0, 0, -1),
                                              new Vector4(0, 0, 0, -1), new Vector4(0, 0, 0, -1) };
        private int _ringNext;
        private float _ringCool;
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Color32> _c = new List<Color32>();
        private readonly List<int> _t = new List<int>();

        public ushort ProductId => _product.Value;
        public float Litres => _volumeCl.Value / 100f;
        /// <summary>Радиус при полном растекании, м.</summary>
        public float TargetRadius
        {
            get
            {
                float area = Litres * 0.001f / Mathf.Max(1e-5f, _thicknessMm * 0.001f);
                return Mathf.Min(_maxRadius, Mathf.Sqrt(area / Mathf.PI));
            }
        }
        public float Radius => _radius;
        public float SlipChance => _slipChance;
        public float SlipCheckEvery => _slipCheckEvery;
        public float SlipCooldown => _slipCooldown;
        public float SlipMinSpeed => _slipMinSpeed;

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        /// <summary>
        /// Пролить порции: в лежащую рядом лужу того же продукта или новую.
        /// v5: «рядом» — ближе Join Distance к краю любой кляксы лужи; в луже
        /// жидкость прирастает кляксой в точке пролива.
        /// </summary>
        public static LiquidPuddle ServerAddAt(LiquidPuddle prefab, ushort product, Vector3 point, int portions,
                                               float mergeSlack = 0.3f)
        {
            if (portions <= 0) return null;
            LiquidPuddle best = null;
            float bestD = float.MaxValue;
            Collider tileHere = prefab != null && prefab._stayOnTile ? TileUnder(point, prefab._floorMask) : null;
            for (int i = 0; i < All.Count; i++)
            {
                LiquidPuddle p = All[i];
                if (p == null || !p.IsSpawned || p._absorbed || p._forceDry || p._product.Value != product) continue;
                if (tileHere != null && p.Tile != null && p.Tile != tileHere) continue;   // v10.5: другая плитка — своя лужа
                if (Mathf.Abs(p.transform.position.y - point.y) > 0.4f) continue;
                float d = p.EdgeDistance(point, true);
                if (d > Mathf.Max(mergeSlack, p._joinDistance) || d >= bestD) continue;
                best = p; bestD = d;
            }
            if (best != null) { best.ServerAddLitresAt(portions * best._litresPerPortion, point); return best; }
            if (prefab == null) return null;
            LiquidPuddle nw = Instantiate(prefab, point, Quaternion.identity);
            nw._presetProduct = product;
            nw._presetLitres = portions * nw._litresPerPortion;
            FishNet.InstanceFinder.ServerManager.Spawn(nw.gameObject);
            return nw;
        }

        public static LiquidPuddle FindNear(Vector3 point, float slack = 0.2f)
        {
            for (int i = 0; i < All.Count; i++)
            {
                LiquidPuddle p = All[i];
                if (p == null) continue;
                if (Mathf.Abs(p.transform.position.y - point.y) > 0.4f) continue;
                if (p.EdgeDistance(point, false) <= slack) return p;
            }
            return null;
        }

        /// <summary>
        /// v11.1: лужа под ногами (для скольжения). feet — точка ног; высоту сравниваем мягко (до 0.6 м),
        /// край — по видимому радиусу, а если сетка ещё не построена — по целевому.
        /// </summary>
        public static LiquidPuddle FindUnderFoot(Vector3 feet, float slack, out float edge)
        {
            edge = float.MaxValue;
            LiquidPuddle best = null;
            for (int i = 0; i < All.Count; i++)
            {
                LiquidPuddle p = All[i];
                if (p == null || !p.isActiveAndEnabled) continue;
                float dy = feet.y - p.transform.position.y;
                if (dy < -0.3f || dy > 0.6f) continue;
                float dv = p.EdgeDistance(feet, false);
                float dt = p.EdgeDistance(feet, true);
                float d = p._radius > 0.01f ? dv : dt;
                if (d < edge) { edge = d; best = p; }
            }
            return edge <= slack ? best : null;
        }

        // ── кляксы ──
        private int LobeN { get { return _lobes.Count > 0 ? Mathf.Min(_lobes.Count, MaxLobes) : 1; } }
        private Vector4 Lobe(int i) { return _lobes.Count > 0 ? _lobes[i] : new Vector4(0f, 0f, 1f, 0f); }

        private float SumW()
        {
            float s = 0f;
            int n = LobeN;
            for (int i = 0; i < n; i++) s += Mathf.Max(0f, Lobe(i).z);
            return Mathf.Max(1e-5f, s);
        }

        /// <summary>Радиус кляксы при полном растекании: доля площади по весу.</summary>
        private float LobeTarget(int i, float total, float sumW)
        {
            int n = LobeN;
            if (n <= 1) return total;
            float r = total * Mathf.Sqrt(Mathf.Max(0f, Lobe(i).z) / sumW) * 1.12f;   // кляксы перекрываются — чуть шире
            return Mathf.Min(total, r);
        }

        /// <summary>Расстояние от точки до края лужи по X/Z (меньше нуля — внутри).</summary>
        public float EdgeDistance(Vector3 point, bool target)
        {
            Vector3 o = point - transform.position;
            float total = TargetRadius, sumW = SumW();
            float best = float.MaxValue;
            int n = LobeN;
            for (int i = 0; i < n; i++)
            {
                Vector4 l = Lobe(i);
                float r = target ? LobeTarget(i, total, sumW) : _lobeR[i];
                float d = new Vector2(o.x - l.x, o.z - l.y).magnitude - r;
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>Веса клякс = текущие литры (после высыхания доли не врут).</summary>
        [Server]
        private void RescaleWeights()
        {
            if (_lobes.Count == 0 || _volumeServer <= 0.001f) return;
            float f = _volumeServer / SumW();
            if (Mathf.Abs(f - 1f) < 0.03f) return;
            for (int i = 0; i < _lobes.Count; i++) { Vector4 l = _lobes[i]; l.z *= f; _lobes[i] = l; }
        }

        [Server]
        private void ServerAddLitresAt(float litres, Vector3 point)
        {
            RescaleWeights();
            float total = TargetRadius, sumW = SumW();
            Vector3 o = point - transform.position;
            Vector2 q = new Vector2(o.x, o.z);
            int near = -1;
            float nearRel = float.MaxValue;
            for (int i = 0; i < _lobes.Count; i++)
            {
                Vector4 l = _lobes[i];
                float rel = (q - new Vector2(l.x, l.y)).magnitude / Mathf.Max(0.02f, LobeTarget(i, total, sumW));
                if (rel < nearRel) { nearRel = rel; near = i; }
            }
            if (near >= 0 && (nearRel < 0.75f || _lobes.Count >= MaxLobes))
            {
                // в существующую кляксу: она чуть сдвигается к месту пролива
                Vector4 l = _lobes[near];
                float k = litres / Mathf.Max(1e-4f, l.z + litres) * (nearRel < 0.75f ? 0.5f : 0.35f);
                Vector2 c = Vector2.Lerp(new Vector2(l.x, l.y), q, k);
                _lobes[near] = new Vector4(c.x, c.y, l.z + litres, l.w);
            }
            else _lobes.Add(new Vector4(q.x, q.y, litres, 0f));
            ServerAddLitres(litres);
        }

        /// <summary>Две ближайшие кляксы — в одну (когда их больше MaxLobes).</summary>
        [Server]
        private void CollapseNearestPair()
        {
            int bi = 0, bj = 1;
            float bd = float.MaxValue;
            for (int i = 0; i < _lobes.Count; i++)
            for (int j = i + 1; j < _lobes.Count; j++)
            {
                Vector4 a = _lobes[i], b = _lobes[j];
                float d = (new Vector2(a.x - b.x, a.y - b.y)).magnitude / Mathf.Max(1e-3f, Mathf.Sqrt(a.z) + Mathf.Sqrt(b.z));
                if (d < bd) { bd = d; bi = i; bj = j; }
            }
            Vector4 A = _lobes[bi], B = _lobes[bj];
            float w = Mathf.Max(1e-4f, A.z + B.z);
            Vector2 c = (new Vector2(A.x, A.y) * A.z + new Vector2(B.x, B.y) * B.z) / w;
            _lobes[bi] = new Vector4(c.x, c.y, A.z + B.z, 1f);
            _lobes.RemoveAt(bj);
        }

        /// <summary>Касаются ли лужи (по кляксам, с учётом того, насколько уже растеклись).</summary>
        private bool Touches(LiquidPuddle o)
        {
            float ta = TargetRadius, sa = SumW(), tb = o.TargetRadius, sb = o.SumW();
            float ka = SpreadGuess(), kb = o.SpreadGuess();
            Vector3 shift = o.transform.position - transform.position;
            int na = LobeN, nb = o.LobeN;
            for (int i = 0; i < na; i++)
            {
                Vector4 a = Lobe(i);
                float ra = LobeTarget(i, ta, sa) * ka;
                for (int j = 0; j < nb; j++)
                {
                    Vector4 b = o.Lobe(j);
                    float rb = o.LobeTarget(j, tb, sb) * kb;
                    float d = new Vector2(b.x + shift.x - a.x, b.y + shift.z - a.y).magnitude;
                    if (d < ra + rb + 0.02f) return true;
                }
            }
            return false;
        }

        /// <summary>Сервер не рисует лужу — грубо прикидываем, насколько она уже растеклась.</summary>
        private float SpreadGuess()
        {
            float t = Mathf.Clamp01((Time.time - _spawnTime) / Mathf.Max(0.1f, _spreadSeconds));
            return Mathf.Max(0.35f, 1f - Mathf.Pow(1f - t, 3f));
        }

        [Server]
        private void ServerAbsorb(LiquidPuddle o)
        {
            o._absorbed = true;
            RescaleWeights();
            o.RescaleWeights();
            Vector3 shift = o.transform.position - transform.position;
            int nb = o.LobeN;
            for (int j = 0; j < nb; j++)
            {
                Vector4 b = o.Lobe(j);
                float w = o._lobes.Count > 0 ? b.z : o._volumeServer;
                _lobes.Add(new Vector4(b.x + shift.x, b.y + shift.z, w, 1f));
            }
            while (_lobes.Count > MaxLobes) CollapseNearestPair();
            _volumeServer = Mathf.Min(655f, _volumeServer + o._volumeServer);
            _volumeCl.Value = (ushort)Mathf.RoundToInt(_volumeServer * 100f);
            if (o._dryFrom < 0f) { _dryFrom = -1f; _lastPour = Mathf.Max(_lastPour, o._lastPour); }
            if (_log) Debug.Log($"[Лужа] слились в одну: {Litres:0.##} л, клякс {_lobes.Count}", this);
            o.Despawn(o.NetworkObject);
        }

        /// <summary>v8: луж слишком много — самые старые сразу начинают сохнуть.</summary>
        [Server]
        private void ServerCrowdLimit()
        {
            int alive = 0;
            for (int i = 0; i < All.Count; i++) if (All[i] != null && All[i].IsSpawned && !All[i]._absorbed && !All[i]._forceDry) alive++;
            while (alive >= _maxPuddles)
            {
                LiquidPuddle oldest = null;
                for (int i = 0; i < All.Count; i++)
                {
                    LiquidPuddle q = All[i];
                    if (q == null || q == this || !q.IsSpawned || q._absorbed || q._forceDry) continue;
                    if (oldest == null || q._spawnTime < oldest._spawnTime) oldest = q;
                }
                if (oldest == null) break;
                oldest._forceDry = true;
                alive--;
            }
        }

        /// <summary>Грубый радиус лужи от корня (для быстрого отсева при слиянии).</summary>
        private float ExtentTarget()
        {
            float total = TargetRadius, sumW = SumW(), e = 0f;
            int n = LobeN;
            for (int i = 0; i < n; i++)
            {
                Vector4 l = Lobe(i);
                e = Mathf.Max(e, new Vector2(l.x, l.y).magnitude + LobeTarget(i, total, sumW));
            }
            return e;
        }

        [Server]
        private void ServerMergeCheck(float dt)
        {
            if (!_merge || _absorbed) return;
            _mergeT -= dt;
            if (_mergeT > 0f) return;
            _mergeT = 0.5f;
            float myE = ExtentTarget();
            for (int i = All.Count - 1; i >= 0; i--)
            {
                LiquidPuddle p = All[i];
                if (p == null || p == this || !p.IsSpawned || p._absorbed || p._product.Value != _product.Value) continue;
                if (Mathf.Abs(p.transform.position.y - transform.position.y) > 0.4f) continue;
                Vector3 dd = p.transform.position - transform.position;
                float far = myE + p.ExtentTarget() + 0.05f;
                if (dd.x * dd.x + dd.z * dd.z > far * far) continue;                 // v8: далеко — не считаем кляксы
                // забирает большая; при равных — та, что старше по ObjectId
                if (p._volumeServer > _volumeServer) continue;
                if (Mathf.Approximately(p._volumeServer, _volumeServer) && p.ObjectId < ObjectId) continue;
                if (Tile != null && p.Tile != null && Tile != p.Tile) continue;              // v10.5: через щель не сливаются
                if (!Touches(p)) continue;
                ServerAbsorb(p);
                return;
            }
        }

        [Server]
        private void ServerAddLitres(float litres)
        {
            _volumeServer = Mathf.Min(655f, _volumeServer + litres);
            _volumeCl.Value = (ushort)Mathf.RoundToInt(_volumeServer * 100f);
            _lastPour = Time.time;
            bool keepDrying = _forceDry || (_maxLife > 0f && Time.time - _spawnTime > _maxLife);
            if (keepDrying) _dryFrom = Mathf.Max(_dryFrom, _volumeServer);
            else _dryFrom = -1f;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (_presetProduct != 0) _product.Value = _presetProduct;
            _volumeServer = 0f;
            _spawnTime = Time.time;
            ServerCrowdLimit();
            _lobes.Clear();
            _lobes.Add(new Vector4(0f, 0f, Mathf.Max(0.01f, _presetLitres), 0f));
            ServerAddLitres(Mathf.Max(0.01f, _presetLitres));
            if (_log) Debug.Log($"[Лужа] новая, {Litres:0.##} л", this);
        }

        private void Update()
        {
            if (!base.IsServerInitialized || _absorbed) return;
            ServerMergeCheck(Time.deltaTime);
            if (!_absorbed) ServerDry(Time.deltaTime);
        }

        private void ServerDry(float dt)
        {
            bool tooOld = _maxLife > 0f && Time.time - _spawnTime > _maxLife;
            bool crowded = _forceDry;
            float delay = Mathf.Min(_dryDelay, _dryDelayMax > 0f ? _dryDelayMax : _dryDelay);
            if (!tooOld && !crowded && Time.time - _lastPour < delay) return;
            if (_dryFrom < 0f)
            {
                _dryFrom = _volumeServer;
                if (_log) Debug.Log($"[Лужа] сохнет: {Litres:0.##} л, исчезнет за ~{(_dryAllSeconds > 0f ? Mathf.Min(_dryAllSeconds, _dryFrom / Mathf.Max(0.001f, _dryLitresPerSecond)) : _dryFrom / Mathf.Max(0.001f, _dryLitresPerSecond)):0} с", this);
            }
            float rate = _dryLitresPerSecond;
            if (_dryAllSeconds > 0f) rate = Mathf.Max(rate, _dryFrom / _dryAllSeconds);
            rate = Mathf.Max(rate, _dryFrom / Mathf.Max(1f, _dryTime));                  // v8: быстрее
            _volumeServer -= rate * dt;
            if (_volumeServer <= 0.02f) { base.Despawn(base.NetworkObject); return; }
            ushort cl = (ushort)Mathf.RoundToInt(_volumeServer * 100f);
            if (Mathf.Abs(cl - _volumeCl.Value) >= 3) _volumeCl.Value = cl;
        }

        // ───────────────────────── ВИД (у всех) ─────────────────────────

        public override void OnStartClient()
        {
            base.OnStartClient();
            GameObject go = new GameObject("PuddleMesh (auto)") { hideFlags = HideFlags.DontSave, layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * _lift;
            _mesh = new Mesh { name = "Puddle", hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _mr = go.AddComponent<MeshRenderer>();
            Material toon = _toonLook ? ToonMaterial : null;
            _toon = toon != null;
            _mr.sharedMaterial = _toon ? toon : (_material != null ? _material : AutoMaterial);
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mpb = new MaterialPropertyBlock();
        }

        private static Material AutoMaterial
        {
            get
            {
                if (_autoMat != null) return _autoMat;
                Shader sh = Shader.Find("CatCooks/Liquid Surface");
                if (sh == null) sh = Shader.Find("CatCooks/Food Heap");
                _autoMat = new Material(sh) { name = "CC Puddle (auto)", hideFlags = HideFlags.DontSave };
                if (_autoMat.HasProperty("_Opacity")) _autoMat.SetFloat("_Opacity", 0.92f);
                if (_autoMat.HasProperty("_EdgeSoftness")) _autoMat.SetFloat("_EdgeSoftness", 0.9f);
                if (_autoMat.HasProperty("_RippleStrength")) _autoMat.SetFloat("_RippleStrength", 0.25f);
                return _autoMat;
            }
        }

        /// <summary>Для прогрева шейдеров (LiquidFx.Warmup).</summary>
        public static Material WarmupMaterial => ToonMaterial;

        private static Material ToonMaterial
        {
            get
            {
                if (_toonMat != null) return _toonMat;
                // v18.1: своё имя — «CatCooks/Toon Puddle» есть и у лужи-декора (trees): Shader.Find
                // брал её, и молоко на полу выглядело серой прозрачной водой
                Shader sh = Shader.Find("CatCooks/Milk Puddle");
                if (sh == null) { Debug.LogWarning("[Лужа] нет шейдера «CatCooks/Milk Puddle» — лужа будет простой"); return null; }
                _toonMat = new Material(sh) { name = "CC Puddle (toon)", hideFlags = HideFlags.DontSave };
                return _toonMat;
            }
        }

        /// <summary>Сюда бьёт струя — круг по поверхности (визуал).</summary>
        public void Ripple(Vector3 point)
        {
            if (_ringCool > 0f) return;
            _ringCool = 0.12f;
            _rings[_ringNext] = new Vector4(point.x, point.y, point.z, 0f);
            _ringNext = (_ringNext + 1) % _rings.Length;
        }

        /// <summary>Совместимость со старым вызовом (бидон): круг + пара капель.</summary>
        public void LocalRipple(Vector3 point, Color color) => Ripple(point);

        private void LateUpdate()
        {
            if (_mesh == null) return;
            float dt = Time.deltaTime;
            float target = TargetRadius;

            // Растекание: быстро в начале, медленно в конце; усыхание — плавно.
            if (_toon) UpdateLobes(dt, target);
            else
            {
                if (target > _radius)
                {
                    _spreadT = Mathf.Min(1f, _spreadT + dt / _spreadSeconds);
                    float k = 1f - Mathf.Pow(1f - _spreadT, 3f);
                    _radius = Mathf.Lerp(_radius, target, Mathf.Max(k * dt * 6f, 0.02f));
                }
                else
                {
                    _spreadT = 0f;
                    _radius = Mathf.MoveTowards(_radius, target, dt * 0.05f);
                }
                _growing = _spreadT < 1f && target > _radius + 0.01f;
                for (int i = 0; i < MaxLobes; i++) _lobeR[i] = i == 0 ? _radius : 0f;
            }

            // Форма — перестраиваем не каждый кадр: пока растекается — чаще (плавно), потом 10 раз в секунду.
            // v8: не видно — не строим; спокойная и не изменилась — не строим.
            _rebuildT -= dt;
            if (_rebuildT <= 0f)
            {
                bool changed = Mathf.Abs(_radius - _builtR) > 0.002f || _builtR < 0f || (_toon && _toonEdgeMotion > 0.05f);
                bool seen = _mr == null || _mr.isVisible || _builtR < 0f;
                _rebuildT = _growing ? 0.05f : _calmRebuild;
                if (changed && seen) { Rebuild(); _builtR = _radius; }
            }

            _ringCool -= dt;
            for (int i = 0; i < _rings.Length; i++)
                if (_rings[i].w >= 0f) { _rings[i].w += dt; if (_rings[i].w > 1.4f) _rings[i].w = -1f; }

            FoodDatabase db = FoodDatabase.Instance;
            ProductDef def = db != null ? db.Get(_product.Value) : null;
            Color col = def != null ? def.BaseColor : new Color(0.97f, 0.97f, 0.95f);
            // Высыхая, бледнеет и становится прозрачнее.
            // v9.2: высыхание плавное — доля от самого большого объёма, сглажена; бледнеет и тает всю сушку
            if (!_dryingSeen && Litres >= _peakL) _peakL = Litres;
            if (Litres < _peakL - 0.02f) _dryingSeen = true;
            if (Litres > _lastL + 0.05f) { _dryingSeen = false; _peakL = Mathf.Max(Litres, _peakL * 0.5f); }
            _lastL = Litres;
            float dryTarget = Mathf.Clamp01(Litres / Mathf.Max(0.01f, _peakL));
            _dryShown = Mathf.MoveTowards(_dryShown, dryTarget, dt * 0.6f);
            float dry = Mathf.Min(_dryShown, Mathf.Clamp01(Litres / Mathf.Max(0.01f, _litresPerPortion * 0.6f)));
            col.a = 1f;
            if (_toon) { _mpb.SetColor(BaseColorId, col); _mpb.SetFloat(DryId, dry); }
            else _mpb.SetColor(BaseColorId, Color.Lerp(col * 0.92f, col, dry));
            _mpb.SetVectorArray(RingsId, _rings);
            _mpb.SetFloat(AgitationId, _growing ? 0.5f : 0f);
            _mr.SetPropertyBlock(_mpb);
            _mr.enabled = _radius > 0.005f;
        }

        /// <summary>v5: у каждой кляксы свой радиус — новая растекается из точки пролива.</summary>
        private void UpdateLobes(float dt, float total)
        {
            int n = LobeN;
            float sumW = SumW();
            bool shrunk = _lobes.Count > 0 && _lobes.Count < _lobeSeen;     // кляксы схлопнулись — индексы сдвинулись
            float grow = 1f - Mathf.Exp(-dt * 3f / Mathf.Max(0.1f, _spreadSeconds));
            float r2 = 0f;
            _growing = false;
            for (int i = 0; i < n; i++)
            {
                float t = LobeTarget(i, total, sumW);
                if (shrunk) _lobeR[i] = t;
                else if (i >= _lobeSeen) _lobeR[i] = Lobe(i).w > 0.5f ? t * 0.85f : 0.01f;
                if (_lobeR[i] < t)
                {
                    _lobeR[i] = Mathf.MoveTowards(Mathf.Lerp(_lobeR[i], t, grow), t, dt * 0.02f);
                    if (t - _lobeR[i] > 0.01f) _growing = true;
                }
                else _lobeR[i] = Mathf.MoveTowards(Mathf.Lerp(_lobeR[i], t, 1f - Mathf.Exp(-dt * 0.9f)), t, dt * 0.01f);   // v9.2: сохнет — сжимается плавно
                r2 += _lobeR[i] * _lobeR[i];
            }
            for (int i = n; i < MaxLobes; i++) _lobeR[i] = 0f;
            _lobeSeen = _lobes.Count > 0 ? _lobes.Count : n;
            _radius = Mathf.Sqrt(r2);
        }

        private void Rebuild()
        {
            if (_toon) { RebuildToon(); return; }
            const int seg = 56;
            int seed = base.IsSpawned ? base.ObjectId : 1;
            float ph1 = (seed * 0.37f) % 6.28f, ph2 = (seed * 0.91f) % 6.28f, ph3 = (seed * 1.73f) % 6.28f;
            float breath = Time.time * _edgeBreath;
            float inner = 1f - _softEdge;

            _v.Clear(); _c.Clear(); _t.Clear();
            _v.Add(Vector3.zero); _c.Add(new Color32(255, 255, 255, 255));
            // Два кольца: внутреннее (полная непрозрачность) и внешнее (мягкая кромка, альфа 0).
            for (int ring = 0; ring < 2; ring++)
            {
                float rk = ring == 0 ? inner : 1f;
                byte alpha = ring == 0 ? (byte)255 : (byte)0;
                for (int i = 0; i < seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    float k = 1f + _edgeNoise * (0.55f * Mathf.Sin(a * 3f + ph1 + breath) +
                                                 0.30f * Mathf.Sin(a * 5f + ph2 - breath * 1.3f) +
                                                 0.15f * Mathf.Sin(a * 9f + ph3 + breath * 0.7f));
                    _v.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * _radius * k * rk);
                    _c.Add(new Color32(255, 255, 255, alpha));
                }
            }
            for (int i = 0; i < seg; i++)
            {
                int a0 = 1 + i, a1 = 1 + (i + 1) % seg;
                _t.Add(0); _t.Add(a1); _t.Add(a0);                           // центр → внутреннее
                int b0 = 1 + seg + i, b1 = 1 + seg + (i + 1) % seg;
                _t.Add(a0); _t.Add(a1); _t.Add(b0);                          // внутреннее → внешнее
                _t.Add(a1); _t.Add(b1); _t.Add(b0);
            }
            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        /// <summary>Край кляксы: множитель радиуса по углу (дышит).</summary>
        private float EdgeK(float a, float ph1, float ph2, float ph3, float breath)
        {
            return 1f + _edgeNoise * (0.55f * Mathf.Sin(a * 3f + ph1 + breath) +
                                      0.30f * Mathf.Sin(a * 5f + ph2 - breath * 1.3f) +
                                      0.15f * Mathf.Sin(a * 9f + ph3 + breath * 0.7f));
        }

        private void AddV(Vector3 p, float u, Vector2 own, byte alpha)
        {
            _v.Add(p); _c.Add(new Color32(255, 255, 255, alpha));
            _u0.Add(new Vector2(u, 0f)); _u1.Add(new Vector2(p.x, p.z)); _u2.Add(own);
        }

        /// <summary>
        /// v4/v5: мультяшная клякса. Край — объединение клякс (лучи из общего центра,
        /// сглажено по углу, поэтому между кляксами — мягкая перемычка).
        /// Кольца с долей до края (uv0.x): тело 0..1, ореол 1..1.25; капли-спутники вокруг.
        /// </summary>
        private void RebuildToon()
        {
            const int seg = ToonSeg;
            int seed = base.IsSpawned ? base.ObjectId : 1;
            float ph1 = (seed * 0.37f) % 6.28f, ph2 = (seed * 0.91f) % 6.28f, ph3 = (seed * 1.73f) % 6.28f;
            float breath = Time.time * _toonEdgeMotion;
            float[] rings = { 0.35f, 0.68f, 0.84f, 0.93f, 1f, 1.25f };

            // общий центр — по площади клякс
            int n = LobeN;
            Vector2 C = Vector2.zero;
            float aw = 0f, minR = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float r = _lobeR[i];
                if (r <= 0.003f) continue;
                Vector4 l = Lobe(i);
                C += new Vector2(l.x, l.y) * r * r;
                aw += r * r;
                minR = Mathf.Min(minR, r);
            }
            if (aw <= 1e-8f) { _mesh.Clear(); return; }
            C /= aw;

            // луч из центра до самого дальнего края клякс
            for (int k = 0; k < seg; k++)
            {
                float a = k / (float)seg * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float best = minR * 0.5f;
                for (int i = 0; i < n; i++)
                {
                    float r = _lobeR[i];
                    if (r <= 0.003f) continue;
                    Vector4 l = Lobe(i);
                    Vector2 pc = new Vector2(l.x, l.y) - C;
                    float along = Vector2.Dot(d, pc);
                    float perp2 = pc.sqrMagnitude - along * along;
                    float t;
                    if (perp2 <= r * r) t = along + Mathf.Sqrt(r * r - perp2);
                    else t = along - (Mathf.Sqrt(perp2) - r) * 1.5f;          // мимо — мягкое продолжение (перемычка)
                    if (t > best) best = t;
                }
                _ray[k] = best;
            }
            // сглаживание по углу: без «ступенек» на стыке клякс
            for (int pass = 0; pass < 3; pass++)
            {
                for (int k = 0; k < seg; k++)
                    _rayTmp[k] = 0.25f * _ray[(k + seg - 1) % seg] + 0.5f * _ray[k] + 0.25f * _ray[(k + 1) % seg];
                for (int k = 0; k < seg; k++) _ray[k] = Mathf.Max(_ray[k], _rayTmp[k] * 0.98f) * 0.5f + _rayTmp[k] * 0.5f;
            }

            Vector3 c3 = new Vector3(C.x, 0f, C.y);

            // v10.2: обтекание швов и дыр — луч не заходит за край пола; недостающая площадь
            // уходит в свободные стороны (лужа «растекается вбок», а не ложится поверх щели)
            bool limited = _followFloor && Application.isPlaying;
            if (limited)
            {
                float full = 0f, cut = 0f;
                float minT = Mathf.Max(0.01f, minR * 0.25f);
                for (int k = 0; k < seg; k++)
                {
                    float a = k / (float)seg * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float want = _ray[k] * 1.12f;
                    _free[k] = FreeAlong(c3, d, minT, want * 1.4f);
                    full += want * want;
                    cut += Mathf.Min(want, _free[k]) * Mathf.Min(want, _free[k]);
                }
                float grow = cut > 1e-6f ? Mathf.Clamp(Mathf.Sqrt(full / cut), 1f, 1.4f) : 1f;
                for (int k = 0; k < seg; k++)
                {
                    float want = _ray[k] * grow;
                    // край кляксы с «ореолом» 1.12 — тоже не за швом
                    _ray[k] = Mathf.Min(want, _free[k] / 1.12f);
                    _free[k] = Mathf.Max(0.005f, _free[k] - Mathf.Max(0.004f, _floorCell * 0.5f));   // запас до щели
                }
            }

            _v.Clear(); _c.Clear(); _t.Clear(); _u0.Clear(); _u1.Clear(); _u2.Clear();
            AddV(c3, 0f, Vector2.zero, 255);
            for (int ring = 0; ring < rings.Length; ring++)
            {
                float u = rings[ring];
                float rk = u <= 1f ? u : 1f + (u - 1f) * 0.5f;                // ореол — на 12% шире края
                for (int i = 0; i < seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    // v10.6: ограничение плиткой — ПОСЛЕ волн края (раньше край «дышал» до +28% и вылезал за щель)
                    float rr = _ray[i] * EdgeK(a, ph1, ph2, ph3, breath) * rk;
                    if (limited)
                    {
                        float lim = u <= 1f ? _free[i] * u : _free[i];             // тело — внутри, ореол — до самой щели
                        rr = Mathf.Min(rr, lim);
                    }
                    Vector3 off = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rr;
                    AddV(c3 + off, u, new Vector2(off.x, off.z), (byte)(u > 1f ? 0 : 255));
                }
            }
            for (int i = 0; i < seg; i++)
            {
                int a0 = 1 + i, a1 = 1 + (i + 1) % seg;
                _t.Add(0); _t.Add(a1); _t.Add(a0);
            }
            for (int ring = 1; ring < rings.Length; ring++)
            for (int i = 0; i < seg; i++)
            {
                int a0 = 1 + (ring - 1) * seg + i, a1 = 1 + (ring - 1) * seg + (i + 1) % seg;
                int b0 = 1 + ring * seg + i, b1 = 1 + ring * seg + (i + 1) % seg;
                _t.Add(a0); _t.Add(a1); _t.Add(b0);
                _t.Add(a1); _t.Add(b1); _t.Add(b0);
            }

            // Капли-спутники: брызги вокруг кляксы, у каждой свой бортик и блик.
            var rnd = new System.Random(seed * 7919 + 17);
            const int sseg = 14;
            float mean = Mathf.Sqrt(aw / Mathf.Max(1, n));
            for (int s = 0; s < _satellites; s++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float dist = Mathf.Lerp(1.1f, 1.45f, (float)rnd.NextDouble());
                float size = Mathf.Lerp(0.045f, 0.12f, (float)rnd.NextDouble());
                int ki = Mathf.RoundToInt(a / (Mathf.PI * 2f) * seg) % seg;
                float sd = _ray[ki] * EdgeK(a, ph1, ph2, ph3, breath) * dist;
                if (limited && sd > _free[ki] * 0.95f) continue;                    // v10.6: спутник за щелью — не рисуем
                Vector3 c = c3 + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * sd;
                float r = Mathf.Min(mean, _radius) * size;
                if (_followFloor && Application.isPlaying && !FloorOk(c.x, c.z)) continue;   // капля не висит над щелью
                int c0 = _v.Count;
                AddV(c + Vector3.up * 0.0004f, 0.45f, Vector2.zero, 255);
                for (int i = 0; i < sseg; i++)
                {
                    float b = i / (float)sseg * Mathf.PI * 2f;
                    float wob = 1f + 0.12f * Mathf.Sin(b * 3f + s);
                    Vector3 off = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * r * wob;
                    AddV(c + off + Vector3.up * 0.0004f, 1f, new Vector2(off.x, off.z), 255);
                }
                for (int i = 0; i < sseg; i++)
                {
                    _t.Add(c0); _t.Add(c0 + 1 + (i + 1) % sseg); _t.Add(c0 + 1 + i);
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetColors(_c);
            _mesh.SetUVs(0, _u0);
            _mesh.SetUVs(1, _u1);
            _mesh.SetUVs(2, _u2);
            _mesh.SetTriangles(_t, 0);
            // v8: нормали все вверх — без RecalculateNormals (шейдеру лужи нормали не нужны)
            while (_nUp.Count < _v.Count) _nUp.Add(Vector3.up);
            if (_nUp.Count > _v.Count) _nUp.RemoveRange(_v.Count, _nUp.Count - _v.Count);
            _mesh.SetNormals(_nUp);
            _mesh.RecalculateBounds();
        }

        private readonly List<Vector3> _nUp = new List<Vector3>();

        // ── v10.2: пол под лужей (кэш по клеткам: пол статичный — один луч на клетку) ──
        private readonly Dictionary<int, bool> _floorOk = new Dictionary<int, bool>();
        private readonly RaycastHit[] _floorHits = new RaycastHit[8];
        private readonly float[] _free = new float[ToonSeg];

        // ── v10.5: своя плитка ──
        private Collider _tile;
        private bool _tileLooked;
        private static readonly RaycastHit[] _tileHits = new RaycastHit[8];

        /// <summary>Коллайдер пола под точкой (игроки и подвижные предметы — не пол).</summary>
        private static Collider TileUnder(Vector3 p, LayerMask mask)
        {
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 0.3f, Vector3.down, _tileHits, 0.8f, mask, QueryTriggerInteraction.Ignore);
            Collider best = null;
            float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _tileHits[i];
                Rigidbody rb = h.collider.attachedRigidbody;
                if (rb != null && !rb.isKinematic) continue;
                if (h.collider is CharacterController) continue;
                if (h.collider.GetComponentInParent<PlayerInteractor>() != null) continue;
                if (h.collider.GetComponentInParent<LiquidPuddle>() != null) continue;
                if (h.distance < bd) { bd = h.distance; best = h.collider; }
            }
            return best;
        }

        /// <summary>Плитка, на которой лежит лужа (определяется один раз).</summary>
        public Collider Tile
        {
            get
            {
                if (!_tileLooked)
                {
                    _tileLooked = true;
                    _tile = _stayOnTile ? TileUnder(transform.position, _floorMask) : null;
                    if (_log && _stayOnTile) Debug.Log("[Лужа] плитка: " + (_tile != null ? "«" + _tile.name + "»" : "не найдена — без ограничения"), this);
                }
                return _tile;
            }
        }

        /// <summary>Можно ли луже лежать в этой точке (оси лужи): пол на той же высоте, без шва/дыры/предмета.</summary>
        private bool FloorOk(float lx, float lz)
        {
            float cell = Mathf.Max(0.01f, _floorCell);
            int cx = Mathf.FloorToInt(lx / cell), cz = Mathf.FloorToInt(lz / cell);
            int key = (cx & 0xffff) | (cz << 16);
            bool ok;
            if (_floorOk.TryGetValue(key, out ok)) return ok;
            Vector3 w = transform.position + new Vector3((cx + 0.5f) * cell, 0f, (cz + 0.5f) * cell);
            int n = Physics.RaycastNonAlloc(w + Vector3.up * 0.3f, Vector3.down, _floorHits, 0.6f, _floorMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            float y = 0f;
            Collider bc = null;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _floorHits[i];
                Rigidbody rb = h.collider.attachedRigidbody;
                if (rb != null && !rb.isKinematic) continue;                       // подвижные предметы — не пол
                if (h.collider is CharacterController) continue;                    // игроки
                if (h.collider.GetComponentInParent<PlayerInteractor>() != null) continue;
                if (h.distance < best) { best = h.distance; y = h.point.y; found = true; bc = h.collider; }
            }
            ok = found && Mathf.Abs(y - transform.position.y) <= _floorStep;
            // v10.5: за щелью — другая плитка (другой коллайдер) → туда не льётся
            Collider tile = Tile;
            if (ok && tile != null && bc != tile) ok = false;
            _floorOk[key] = ok;
            return ok;
        }

        /// <summary>Сколько можно пройти от точки c по направлению d до шва/дыры (не дальше maxT).</summary>
        private float FreeAlong(Vector3 c, Vector3 d, float minT, float maxT)
        {
            float step = Mathf.Max(0.008f, _floorCell * 0.7f);
            for (float t = minT; t <= maxT; t += step)
                if (!FloorOk(c.x + d.x * t, c.z + d.z * t)) return Mathf.Max(minT, t - step * 0.5f);
            return maxT;
        }

        private void OnDestroy()
        {
            if (_mesh != null) SafeDestroy.Kill(_mesh);
        }
    }
}
