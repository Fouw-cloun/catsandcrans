using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>Сюда можно бросить мастерком (рёбра — специи). Сервер. Возвращает, сколько взято; 0 — why.</summary>
    public interface IScoopReceiver
    {
        int ServerReceiveScoop(ushort product, FoodState state, int count, Vector3 point, Vector3 normal, out string why);
    }

    /// <summary>
    /// РЁБРА БАРБЕКЮ — пласт свиных рёбер (или одно ребро после нарезки). Модель своя (RibsMesh).
    ///
    /// ═══ КАК ГОТОВЯТ ═══
    ///   1. Чистка (по желанию, за оценку): ЭЛЕКТРОРУБАНОК — зажал ЛКМ и водишь мышью по мясу:
    ///      белая плёнка и жир уходят, проступают кости. Только сырые.
    ///   2. Специи: «Сухой руб» (соль + паприка + перец + сушёный чеснок, миксером) — МАСТЕРКОМ:
    ///      ПКМ — бросил горку на рёбра, ЛКМ зажал и водишь мышью — растираешь. С двух сторон:
    ///      R (смотришь на рёбра) — перевернуть руками, пока сырые/холодные.
    ///   3. Духовка (ЛКМ рёбрами в руке — встают на решётку). С 95 °C печётся (~70 с), сохнет —
    ///      ПУЛЬВЕРИЗАТОР с моп-соусом: ЛКМ — брызнуть. Горячие — только КЛЕЩАМИ:
    ///      ЛКМ — перевернуть, ПКМ — взять / отпустить, взятые ЛКМ — поставить на решётку/стол.
    ///   4. Глазурь: КИСТЬ — макнул в «Глазурь барбекю» (ЛКМ по таре), ЛКМ по рёбрам и водишь мышью;
    ///      перевернул, вторую сторону; ещё ~10 с в духовке — лак схватился → ГОТОВО (звёздочки).
    ///   Нарезка — ручной пилой в любой момент (одно ребро с края). Сырыми — сохнут быстрее (−оценка).
    ///
    /// ═══ НА КОРНЕ ═══ NetworkObject · NetworkTransform · Rigidbody · Box Collider · MeshFilter · MeshRenderer ·
    /// этот компонент (он же Food Piece). Делает меню CatCooks → Барбекю → 2 (рёбра и одно ребро).
    ///
    /// ═══ РЕЖИМ «БРИСКЕТ» (галка Brisket, меню CatCooks → Брискет → 2) ═══
    ///   1. Рубанок — срезать жир сверху, оставив тонкий слой (~30%).  2. Кисть с горчицей (по желанию).
    ///   3. Мастерком «Руб для брискета» (перец + соль + коричневый сахар) — обе стороны.
    ///   4. Обжарка: решётка, духовка от 200 °C — корочка снизу; клещами перевернуть — вторая сторона.
    ///   5. Рулон пенофола: ЛКМ держать — «лодочка» (верх открыт). Томить 110–130 °C, брызгать моп-соусом.
    ///   6. Корочка почти чёрная — рулоном ещё раз: завернуть полностью, доходит до готовности.
    ///   7. Вынуть клещами, отдых в пенофоле (вне духовки), ПКМ рулоном — развернуть → ГОТОВО.
    ///   8. Нарезать ручной пилой ПОПЕРЁК волокон (ломтики с края).
    /// </summary>
    public sealed class RibsRack : FoodPiece, ICuttable, ICuttableAim, IFoodWorkHandler, IScoopReceiver
    {
        [Header("Рёбра")]
        [Range(1, 16)][SerializeField] private int _ribs = 8;
        [Tooltip("Ширина одного ребра, м")]
        [SerializeField] private float _ribWidth = 0.07f;
        [Tooltip("Длина кости (поперёк пласта), м")]
        [SerializeField] private float _boneLength = 0.32f;
        [Tooltip("Толщина мяса у толстого края, м")]
        [SerializeField] private float _thickness = 0.06f;
        [Tooltip("Префаб ОДНОГО ребра (тот же компонент, Ribs = 1) — выпадает при нарезке пилой")]
        [SerializeField] private GameObject _slicePrefab;

        [Header("Продукты (пусто — по названию)")]
        [SerializeField] private ProductDef _dish;
        [SerializeField] private ProductDef _rub;
        [SerializeField] private ProductDef _glaze;
        [SerializeField] private ProductDef _sauce;
        [SerializeField] private ProductDef _mop;

        [Header("Запекание")]
        [Tooltip("С какой температуры рёбра печётся, °C")]
        [SerializeField] private float _cookFromC = 95f;
        [Tooltip("Секунд до готовности при ~170 °C")]
        [SerializeField] private float _bakeSeconds = 70f;
        [Tooltip("Секунд до полного высыхания при ~170 °C (если не брызгать)")]
        [SerializeField] private float _drySeconds = 80f;
        [Tooltip("Выше — подгорает")]
        [SerializeField] private float _burnAboveC = 245f;
        [Tooltip("Одно ребро (нарезали до духовки): печётся быстрее и сохнет сильнее")]
        [SerializeField] private float _sliceDryK = 1.7f;
        [SerializeField] private float _sliceBakeK = 1.4f;
        [Tooltip("Сколько секунд в духовке после глазури, чтобы лак схватился")]
        [SerializeField] private float _glazeSetSeconds = 10f;
        [Tooltip("Влажность от одного пшика моп-соусом (вода — 70%)")]
        [Range(0.02f, 0.6f)][SerializeField] private float _sprayMoist = 0.18f;
        [Tooltip("Горячее этого руками не взять и не перевернуть — клещи")]
        [SerializeField] private float _hotC = 50f;
        [SerializeField] private float _heatRate = 0.05f;
        [SerializeField] private float _coolRate = 0.025f;

        [Header("Специи, соус, чистка")]
        [Tooltip("Порций руба на одну сторону (мастерок = 2)")]
        [SerializeField] private float _rubPerSide = 2f;
        [Tooltip("Порций соуса на одну сторону")]
        [SerializeField] private float _glazePerSide = 2f;
        [Tooltip("Сколько покрытия даёт метр движения мышью по клетке")]
        [SerializeField] private float _strokeK = 3.5f;

        [Header("Руки")]
        [Tooltip("Смотришь на рёбра — перевернуть руками (только холодные)")]
        [SerializeField] private KeyCode _flipKey = KeyCode.R;
        [SerializeField] private float _reach = 3f;

        [Header("Мазки (вид)")]
        [Tooltip("Руб и соус рисуются мазками по текстуре — как краской: зернистая шапка специй, блестящий соус. Выкл — только подкраска вершин")]
        [SerializeField] private bool _paintStrokes = true;
        [Tooltip("Материал слоя мазков: прозрачный URP Lit. Пусто — сделает сам (в редакторе работает; для билда лучше задать)")]
        [SerializeField] private Material _paintMaterial;
        [Tooltip("Насыщенность мазка за проход (0.3 — бледно, 0.8 — сразу густо)")]
        [Range(0.1f, 1.2f)][SerializeField] private float _strokeAmount = 0.55f;

        [Header("Вид")]
        [SerializeField] private Material _material;
        [SerializeField] private Color _rawMeat = new Color(0.74f, 0.24f, 0.22f);
        [SerializeField] private Color _cookedMeat = new Color(0.58f, 0.31f, 0.17f);
        [SerializeField] private Color _overMeat = new Color(0.34f, 0.18f, 0.10f);
        [SerializeField] private Color _dryMeat = new Color(0.36f, 0.24f, 0.18f);
        [SerializeField] private Color _fat = new Color(0.94f, 0.86f, 0.80f);
        [SerializeField] private Color _fatCooked = new Color(0.86f, 0.66f, 0.40f);
        [SerializeField] private Color _bone = new Color(0.93f, 0.89f, 0.78f);
        [SerializeField] private Color _rubColor = new Color(0.62f, 0.20f, 0.08f);
        [SerializeField] private Color _glazeColor = new Color(0.46f, 0.10f, 0.05f);
        [SerializeField] private Color _glazeSetColor = new Color(0.33f, 0.08f, 0.04f);
        [SerializeField] private Color _char = new Color(0.07f, 0.05f, 0.04f);

        [Header("Брискет (вместо рёбер)")]
        [Tooltip("Этот кусок — брискет: свой рецепт (обжарка, пенофол, корочка, отдых), без костей")]
        [SerializeField] private bool _brisket;
        [SerializeField] private ProductDef _bRub;
        [Tooltip("Горчичная паста (горчица + руб): ею мажут брискет мастерком — горчица обязательна")]
        [SerializeField] private ProductDef _bPaste;
        [SerializeField] private Color _pasteColor = new Color(0.56f, 0.38f, 0.13f);
        [Tooltip("Брискет без горчицы (ни пасты, ни кисти с горчицей) — минус")]
        [SerializeField] private float _noMustardPenalty = 10f;
        [SerializeField] private ProductDef _mustard;
        [SerializeField] private ProductDef _bDish;
        [Tooltip("С какой температуры обжаривается корочка снизу, °C")]
        [SerializeField] private float _searC = 200f;
        [Tooltip("Секунд обжарки одной стороны")]
        [SerializeField] private float _searSeconds = 8f;
        [Tooltip("Секунд до готовности при ~120 °C")]
        [SerializeField] private float _bBakeSeconds = 90f;
        [Tooltip("Секунд до почти чёрной корочки (лодочка, 110–130 °C)")]
        [SerializeField] private float _barkSeconds = 45f;
        [Tooltip("Выше — для томления слишком жарко, °C")]
        [SerializeField] private float _lowMaxC = 150f;
        [Tooltip("Секунд отдыха в пенофоле вне духовки")]
        [SerializeField] private float _restSeconds = 10f;
        [Tooltip("Брискет: до скольки рубанок срезает жир сверху в КАЖДОМ месте (0.3 = тонкий слой ~30%, ровно по всему куску)")]
        [Range(0f, 0.6f)][SerializeField] private float _fatFloor = 0.3f;
        [Tooltip("Порций руба на сторону брискета (мастерок = 2)")]
        [SerializeField] private float _bRubPerSide = 3f;
        [SerializeField] private Color _bRubColor = new Color(0.30f, 0.22f, 0.16f);
        [SerializeField] private Color _mustardColor = new Color(0.86f, 0.66f, 0.12f);
        [SerializeField] private Color _searColor = new Color(0.40f, 0.20f, 0.10f);
        [SerializeField] private Color _barkColor = new Color(0.10f, 0.07f, 0.05f);

        [Header("Копчение (дым пылесосом в люк духовки)")]
        [Tooltip("Секунд в полной дымом духовке до «копчёный 100%»")]
        [SerializeField] private float _smokeSeconds = 12f;
        [Tooltip("Сколько баллов даёт полное копчение")]
        [SerializeField] private float _smokeBonus = 6f;
        [Tooltip("Без копчения (дымок < 90%) — сколько баллов снять максимум. 0 — копчение по желанию")]
        [SerializeField] private float _noSmokePenalty = 10f;
        [Tooltip("Полный бак пылесоса, выдутый ПРЯМО на мясо, даёт столько копчения (1 = 100%)")]
        [SerializeField] private float _directSmoke = 1.2f;
        [SerializeField] private Color _smokeTint = new Color(0.42f, 0.25f, 0.15f);

        [Header("Подсказка и звёздочки")]
        [SerializeField] private bool _showHud = true;
        [Tooltip("Жёлтая рамка «что отрежется» у ручной пилы")]
        [SerializeField] private bool _sawFrame = false;
        [SerializeField] private float _hudDistance = 4f;
        [SerializeField] private bool _stars = true;
        [Range(5, 60)][SerializeField] private int _starCount = 22;
        [SerializeField] private bool _logRibs = true;

        public const byte FlagSlicedRaw = 1, FlagFinished = 2, FlagPaste = 4;
        public bool UsedPaste => (_flags.Value & FlagPaste) != 0;
        private const int Rows = RibsMesh.Rows;

        // ── сеть ──
        private readonly SyncVar<byte> _n = new SyncVar<byte>(0);
        private readonly SyncVar<Vector3> _dims = new SyncVar<Vector3>(Vector3.zero);
        private readonly SyncList<byte> _mem = new SyncList<byte>();     // плёнка/жир 255 → снято 0
        private readonly SyncList<byte> _rubL = new SyncList<byte>();    // специи 0..255
        private readonly SyncList<byte> _glz = new SyncList<byte>();     // соус 0..255
        private readonly SyncVar<byte> _pend0 = new SyncVar<byte>(0), _pend1 = new SyncVar<byte>(0);   // горка руба: порции ×40
        private readonly SyncVar<byte> _done = new SyncVar<byte>(0);     // 0..255 = 0..1.5
        private readonly SyncVar<byte> _moist = new SyncVar<byte>(255);
        private readonly SyncVar<byte> _temp = new SyncVar<byte>(20);
        private readonly SyncVar<byte> _charN = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _set = new SyncVar<byte>(0);      // лак схватился 0..255
        private readonly SyncVar<byte> _flags = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _score = new SyncVar<byte>(0);
        private readonly SyncVar<NetworkObject> _grab = new SyncVar<NetworkObject>(null);
        // брискет
        private readonly SyncVar<byte> _sear0 = new SyncVar<byte>(0), _sear1 = new SyncVar<byte>(0);   // корочка обжарки 0..255 = 0..1.5
        private readonly SyncVar<byte> _wrap = new SyncVar<byte>(0);     // 0 — нет, 1 — лодочка, 2 — закрыт
        private readonly SyncVar<byte> _bark = new SyncVar<byte>(0);     // корочка томления 0..255 = 0..1.5
        private readonly SyncVar<byte> _rest = new SyncVar<byte>(0);     // отдых 0..255
        private readonly SyncVar<byte> _smokeN = new SyncVar<byte>(0);   // копчение 0..255

        // ── сервер ──
        private float _fDone, _fMoist = 1f, _fTemp = 20f, _fChar, _fSet, _fMinMoist = 1f;
        private float _glazeQSum, _glazeQW, _mops, _waters;
        private int _pasteN, _dryN;
        private byte _pScore;
        private float _hintAt, _sendAt;
        private float _fSear0, _fSear1, _fBark, _fRest, _barkAtWrap = -1f;
        private bool _lateBoat, _searWarned, _doneLogged, _restLogged, _barkLogged, _smokeLogged, _smokeHinted;
        private float _fSmoke;
        private int _logStep = -1;
        private float _aimX;
        private bool _wasKinematic;
        private bool _presetRibs;
        private int _pN; private Vector3 _pDims;
        private byte[] _pMem, _pRub, _pGlz;
        private float _pDone, _pMoist, _pTemp, _pChar, _pSet, _pMin; private byte _pFlags;
        // _rb — общий из InteractableItem (protected), своё поле с тем же именем Unity не любит

        // ── вид ──
        private MeshFilter _mf;
        private MeshRenderer _mr;
        private BoxCollider _box;
        private RibsMesh _rm;
        private Color[] _cols;
        private bool _dirty = true;
        private float _paintAt;
        private Transform _pile;
        private RackPaintLayer _rubPaint, _glzPaint;
        private readonly Vector3[] _strokeLast = new Vector3[4];
        private readonly float[] _strokeLastT = new float[4];
        private float _paintStrokeAt = -10f;
        private MeshRenderer _pileR;
        private MaterialPropertyBlock _mpb;
        private static GUIStyle _hud;

        // ───────────────────────── свойства ─────────────────────────

        public int Ribs => _n.Value > 0 ? _n.Value : _ribs;
        public Vector3 Dims => _dims.Value.sqrMagnitude > 1e-8f ? _dims.Value : new Vector3(_ribWidth, _boneLength, _thickness);
        public float Done01 => _done.Value / 255f * 1.5f;
        public float Moist01 => _moist.Value / 255f;
        public float TempC => _temp.Value;
        public bool Finished => (_flags.Value & FlagFinished) != 0;
        public bool IsHot => _temp.Value >= _hotC;
        public bool IsCooking => Done01 > 0.03f;
        public int Score => _score.Value;
        public NetworkObject GrabbedBy => _grab.Value;
        /// <summary>Габарит модели в осях рёбер (для «водить по мясу»).</summary>
        public Bounds LocalBounds => _rm != null && _rm.Mesh != null ? _rm.Mesh.bounds : new Bounds(Vector3.zero, new Vector3(Ribs * Dims.x, Dims.z, Dims.y));
        private int Cells => 2 * Rows * Ribs;
        private int Idx(int side, int row, int col) => (side * Rows + row) * Ribs + col;

        /// <summary>Какая сторона рёбер сейчас смотрит вверх: 0 — мясная, 1 — костяная.</summary>
        public int UpSide => Vector3.Dot(transform.up, Vector3.up) >= 0f ? 0 : 1;

        public float SideAvg(SyncList<byte> l, int side)
        {
            if (l == null || l.Count < Cells) return 0f;
            int s = 0, n = Rows * Ribs;
            for (int r = 0; r < Rows; r++) for (int c = 0; c < Ribs; c++) s += l[Idx(side, r, c)];
            return s / (255f * n);
        }
        public float SideMax(SyncList<byte> l, int side)
        {
            if (l == null || l.Count < Cells) return 0f;
            int m = 0;
            for (int r = 0; r < Rows; r++) for (int c = 0; c < Ribs; c++) m = Mathf.Max(m, l[Idx(side, r, c)]);
            return m / 255f;
        }
        /// <summary>Сколько жира рубанок оставит в клетке сверху: тонкий слой, но НЕ ровный — пятнами 50–150% от Fat Floor
        /// (по номеру клетки, одинаково у всех), чтобы выглядело как настоящий подрезанный жир, а не «под линейку».</summary>
        private float FloorOf(int cell)
        {
            if (_fatFloor <= 0f) return 0f;
            float h = Mathf.Abs(Mathf.Sin(cell * 12.9898f + 78.233f) * 43758.5453f) % 1f;
            return Mathf.Clamp(_fatFloor * (0.5f + h), 0f, 0.6f);
        }

        /// <summary>Сколько ещё можно срезать сверху в самом толстом месте (0 — везде уже тонкий слой).</summary>
        public float FatExcess
        {
            get
            {
                if (_mem.Count < Cells) return 1f;
                float m = 0f;
                for (int r = 0; r < Rows; r++) for (int c = 0; c < Ribs; c++)
                {
                    int i = Idx(0, r, c);
                    m = Mathf.Max(m, _mem[i] / 255f - FloorOf(i));
                }
                return m;
            }
        }

        /// <summary>Жир сверху подрезан везде (нигде не осталось толстого места).</summary>
        public bool FatEven => FatExcess <= 0.1f;
        public float Membrane01 => (SideAvg(_mem, 0) + SideAvg(_mem, 1)) * 0.5f;
        public float Rub01(int side) => SideAvg(_rubL, side);
        public float Glaze01(int side) => SideAvg(_glz, side);
        public float Pending(int side) => (side == 0 ? _pend0.Value : _pend1.Value) / 40f;

        public bool IsBrisket => _brisket;
        /// <summary>Пенофол: 0 — нет, 1 — лодочка (верх открыт), 2 — завёрнут полностью.</summary>
        public int Wrap => _wrap.Value;
        public float Sear01(int side) => (side == 0 ? _sear0.Value : _sear1.Value) / 255f * 1.5f;
        public float Bark01 => _bark.Value / 255f * 1.5f;
        public float Rest01 => _rest.Value / 255f;
        /// <summary>Сколько прокоптилось (дым пылесосом в люк духовки) 0..1.</summary>
        public float Smoke01 => _smokeN.Value / 255f;
        /// <summary>Жир сверху (брискет) — сколько осталось 0..1.</summary>
        public float FatCap01 => SideAvg(_mem, 0);
        private float RubPerSide => _brisket ? _bRubPerSide : _rubPerSide;
        private string Who => _brisket ? "Брискет" : "Рёбра";

        // ───────────────────────── жизнь ─────────────────────────

        protected override void Awake()
        {
            base.Awake();
            _rb = GetComponent<Rigidbody>();
            _mf = GetComponent<MeshFilter>(); if (_mf == null) _mf = gameObject.AddComponent<MeshFilter>();
            _mr = GetComponent<MeshRenderer>(); if (_mr == null) _mr = gameObject.AddComponent<MeshRenderer>();
            if (_material != null) _mr.sharedMaterial = _material;
            _box = GetComponent<BoxCollider>();
            MeatPhysics.Setup(_rb);
            Rebuild(_ribs, new Vector3(_ribWidth, _boneLength, _thickness));
        }

        private void FixedUpdate()
        {
            if (!base.IsServerInitialized) return;
            if (_grab.Value == null) WorkHoldTick();
            MeatPhysics.Calm(_rb);
        }

        // ── пока по мясу водят инструментом — оно стоит (кинематика), ничто не толкает и не «взрывает» ──
        private float _workUntil;
        private bool _workOn, _workKin;

        [Server]
        public void ServerWorkHold()
        {
            if (_rb == null || IsHeld) return;
            _workUntil = Time.time + 0.5f;
            if (_workOn) return;
            _workOn = true; _workKin = _rb.isKinematic;
            if (!_rb.isKinematic) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; _rb.isKinematic = true; }
        }

        private void WorkHoldTick()
        {
            if (!_workOn || Time.time < _workUntil) return;
            _workOn = false;
            if (_rb == null || IsHeld) return;      // взяли в руки — кинематикой рулит рука
            _rb.isKinematic = _workKin;
            if (!_workKin) _rb.WakeUp();
        }


        public override void OnStartServer()
        {
            base.OnStartServer();
            Resolve();
            if (_presetRibs)
            {
                _n.Value = (byte)Mathf.Clamp(_pN, 1, 16);
                _dims.Value = _pDims;
                Fill(_mem, _pMem); Fill(_rubL, _pRub); Fill(_glz, _pGlz);
                _fDone = _pDone; _fMoist = _pMoist; _fTemp = _pTemp; _fChar = _pChar; _fSet = _pSet; _fMinMoist = _pMin;
                _flags.Value = _pFlags;
                if (_pScore > 0) _score.Value = _pScore;
            }
            else
            {
                _n.Value = (byte)Mathf.Clamp(_ribs, 1, 16);
                _dims.Value = new Vector3(_ribWidth, _boneLength, _thickness);
                int n = 2 * Rows * _ribs;
                var m = new byte[n]; var z = new byte[n];
                for (int i = 0; i < n; i++) m[i] = 255;
                Fill(_mem, m); Fill(_rubL, z); Fill(_glz, (byte[])z.Clone());
            }
            Push();
            ServerSetCount(Ribs);
            if (_logRibs) Debug.Log($"[Рёбра] «{name}»: {Ribs} рёбер {Dims.x:0.00}×{Dims.y:0.00} м" +
                                   (_slicePrefab == null && Ribs > 1 ? " ⚠ Slice Prefab пуст — пилой не нарезать" : "") +
                                   (_rub == null ? " ⚠ нет продукта «Сухой руб» — Барбекю → 1" : ""), this);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _n.OnChange += OnShape; _dims.OnChange += OnDims;
            _mem.OnChange += OnList; _rubL.OnChange += OnList; _glz.OnChange += OnList;
            _done.OnChange += OnByte; _moist.OnChange += OnByte; _charN.OnChange += OnByte; _set.OnChange += OnByte;
            _pend0.OnChange += OnByte; _pend1.OnChange += OnByte; _flags.OnChange += OnByte;
            _sear0.OnChange += OnByte; _sear1.OnChange += OnByte; _bark.OnChange += OnByte; _wrap.OnChange += OnWrap;
            _smokeN.OnChange += OnByte;
            Rebuild(Ribs, Dims);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _n.OnChange -= OnShape; _dims.OnChange -= OnDims;
            _mem.OnChange -= OnList; _rubL.OnChange -= OnList; _glz.OnChange -= OnList;
            _done.OnChange -= OnByte; _moist.OnChange -= OnByte; _charN.OnChange -= OnByte; _set.OnChange -= OnByte;
            _pend0.OnChange -= OnByte; _pend1.OnChange -= OnByte; _flags.OnChange -= OnByte;
            _sear0.OnChange -= OnByte; _sear1.OnChange -= OnByte; _bark.OnChange -= OnByte; _wrap.OnChange -= OnWrap;
            _smokeN.OnChange -= OnByte;
        }

        private void OnWrap(byte a, byte b, bool s) { _dirty = true; _foilT = 0f; }

        private void OnShape(byte a, byte b, bool s) => Rebuild(Ribs, Dims);
        private void OnDims(Vector3 a, Vector3 b, bool s) => Rebuild(Ribs, Dims);
        private void OnList(SyncListOperation op, int i, byte a, byte b, bool s) => _dirty = true;
        private void OnByte(byte a, byte b, bool s) => _dirty = true;

        private void OnDestroy()
        {
            if (_rm != null && _rm.Mesh != null) SafeDestroy.Kill(_rm.Mesh);
            if (_rubPaint != null) { _rubPaint.DestroyPile(); _rubPaint.Destroy(); }
            if (_glzPaint != null) _glzPaint.Destroy();
        }

        private void Resolve()
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return;
            if (_dish == null) _dish = MacaronTitles.Find(db, BBQTitles.Dish);
            if (_rub == null) _rub = MacaronTitles.Find(db, BBQTitles.Rub);
            if (_glaze == null) _glaze = MacaronTitles.Find(db, BBQTitles.Glaze);
            if (_sauce == null) _sauce = MacaronTitles.Find(db, BBQTitles.Sauce);
            if (_mop == null) _mop = MacaronTitles.Find(db, BBQTitles.Mop);
            if (_bRub == null) _bRub = MacaronTitles.Find(db, BBQTitles.BrisketRub);
            if (_bPaste == null) _bPaste = MacaronTitles.Find(db, BBQTitles.BrisketPaste);
            if (_mustard == null) _mustard = MacaronTitles.Find(db, BBQTitles.Mustard);
            if (_bDish == null) _bDish = MacaronTitles.Find(db, BBQTitles.BrisketDish);
        }

        private static void Fill(SyncList<byte> l, byte[] a)
        {
            l.Clear();
            if (a != null) for (int i = 0; i < a.Length; i++) l.Add(a[i]);
        }

        private static byte B(float v01) => (byte)Mathf.Clamp(Mathf.RoundToInt(v01 * 255f), 0, 255);

        /// <summary>Сервер: поплавки → байты сети.</summary>
        private void Push()
        {
            byte d = B(_fDone / 1.5f); if (d != _done.Value) _done.Value = d;
            byte m = B(_fMoist); if (m != _moist.Value) _moist.Value = m;
            byte t = (byte)Mathf.Clamp(Mathf.RoundToInt(_fTemp), 0, 255); if (t != _temp.Value) _temp.Value = t;
            byte c = B(_fChar); if (c != _charN.Value) _charN.Value = c;
            byte s = B(_fSet); if (s != _set.Value) _set.Value = s;
            byte sm = B(_fSmoke); if (sm != _smokeN.Value) _smokeN.Value = sm;
            if (!_brisket) return;
            byte a = B(_fSear0 / 1.5f); if (a != _sear0.Value) _sear0.Value = a;
            byte b = B(_fSear1 / 1.5f); if (b != _sear1.Value) _sear1.Value = b;
            byte k = B(_fBark / 1.5f); if (k != _bark.Value) _bark.Value = k;
            byte r = B(_fRest); if (r != _rest.Value) _rest.Value = r;
        }

        // ───────────────────────── СЕРВЕР: печь, сохнуть, гореть ─────────────────────────

        private void Update()
        {
            if (base.IsServerInitialized) ServerTick(Time.deltaTime);
            if (base.IsClientInitialized) ClientInput();
        }

        private void ServerTick(float dt)
        {
            if (dt <= 0f) return;
            FollowTongs();
            BakeOven oven = FindOven();
            OvenLog(oven);
            float target = oven != null ? oven.TemperatureC : 20f;
            SmokeTick(dt, oven);
            if (_brisket) { BrisketTick(dt, oven, target); return; }
            float single = Ribs <= 1 ? 1.6f : 1f;
            float k = (target > _fTemp ? _heatRate : _coolRate) * single;
            _fTemp = Mathf.Lerp(_fTemp, target, 1f - Mathf.Exp(-k * dt));

            if (_fTemp >= _cookFromC)
            {
                float speed = Mathf.Clamp(_fTemp / 170f, 0.45f, 1.6f) * (oven != null ? oven.BakeSpeed : 0.5f);
                float bakeK = Ribs <= 1 ? _sliceBakeK : 1f;
                _fDone = Mathf.Min(1.5f, _fDone + dt * speed * bakeK / Mathf.Max(5f, _bakeSeconds));
                float dryK = Ribs <= 1 ? _sliceDryK : 1f;
                _fMoist = Mathf.Max(0f, _fMoist - dt * speed * dryK / Mathf.Max(5f, _drySeconds));
                if (_fDone > 0.2f) _fMinMoist = Mathf.Min(_fMinMoist, _fMoist);
                if (_fTemp > _burnAboveC || _fDone > 1.3f)
                    _fChar = Mathf.Min(1f, _fChar + dt * (0.02f + Mathf.Max(0f, _fTemp - _burnAboveC) * 0.002f + Mathf.Max(0f, _fDone - 1.3f) * 0.1f));
                // глазурь схватывается в жару
                float g = (Glaze01(0) + Glaze01(1)) * 0.5f;
                if (g > 0.05f && _fTemp >= 110f) _fSet = Mathf.Min(1f, _fSet + dt / Mathf.Max(1f, _glazeSetSeconds) * Mathf.Clamp01(g * 1.6f));
                ServerLogProgress();
                if (_fMoist < 0.35f) Hint($"рёбра СОХНУТ (влажность {_fMoist * 100f:0}%) — брызни моп-соусом из пульверизатора (ЛКМ)");
                if (_fTemp > _burnAboveC) Hint($"{_fTemp:0} °C — слишком жарко, рёбра подгорают!");
            }
            if (!Finished) TryFinish();
            Push();
        }

        /// <summary>Когда дым ложится лучше всего. Рёбра: на середине готовки (30–85%), до глазури.
        /// Брискет: после обжарки (в лодочке / открытым). Сырое или уже в соусе — вполсилы.</summary>
        public float SmokeWeight
        {
            get
            {
                if (_brisket)
                {
                    if (_wrap.Value == 2) return 0f;
                    return (Sear01(0) > 0.6f || Sear01(1) > 0.6f || _wrap.Value == 1) ? 1f : 0.5f;
                }
                float g = (Glaze01(0) + Glaze01(1)) * 0.5f;
                if (g > 0.3f) return 0.4f;
                return _fDone >= 0.25f && _fDone <= 0.85f ? 1f : 0.5f;
            }
        }

        /// <summary>Пылесос выдувает дым ПРЯМО на мясо (в духовке или на столе). amount — доля бака. Сервер.</summary>
        [Server]
        public bool ServerSmokeDirect(float amount, out string why)
        {
            why = null;
            if (Finished) { why = "уже готово"; return false; }
            float w = SmokeWeight;
            if (w <= 0f) { why = "в закрытом пенофоле дым не проходит — открой (ПКМ рулоном) или копти в лодочке"; return false; }
            float was = _fSmoke;
            _fSmoke = Mathf.Min(1f, _fSmoke + amount * _directSmoke * w);
            _smokeDirectAt = Time.time;
            if (_logRibs)
            {
                if (!_smokeLogged && _fSmoke >= 1f) { _smokeLogged = true; Debug.Log($"[{Who}] «{name}»: прокоптились — дымок 100% (+{_smokeBonus:0} к оценке)", this); }
                else if (was < 0.01f) Debug.Log($"[{Who}] «{name}»: коптится — дым прямо на мясо" + (w < 1f ? (_brisket ? " (сырой — ложится вполсилы, лучше после обжарки)" : " (лучше всего на 30–85% прожарки, до соуса)") : ""), this);
            }
            Push();
            return true;
        }

        private float _smokeDirectAt = -10f;
        public bool SmokingNow => Time.time - _smokeDirectAt < 0.5f;

        /// <summary>Копчение: в духовке с дымом (пылесосом через люк). Брискет в закрытом пенофоле не коптится.</summary>
        private void SmokeTick(float dt, BakeOven oven)
        {
            if (oven == null || Finished) return;
            bool cooking = _fTemp >= _cookFromC;
            float smoke = oven.Smoke01;
            if (smoke > 0.02f && !(_brisket && _wrap.Value == 2))
            {
                float was = _fSmoke;
                _fSmoke = Mathf.Min(1f, _fSmoke + dt * smoke * SmokeWeight / Mathf.Max(1f, _smokeSeconds));
                if (_logRibs && !_smokeLogged && _fSmoke >= 1f) { _smokeLogged = true; Debug.Log($"[{Who}] «{name}»: прокоптились — дымок 100% (+{_smokeBonus:0} к оценке)", this); }
                else if (_logRibs && was < 0.01f && _fSmoke >= 0.01f) Debug.Log($"[{Who}] «{name}»: коптится — дым в духовке {smoke * 100f:0}%", this);
            }
            else if (cooking && !_smokeHinted && _fSmoke < 0.05f && _fDone > 0.3f)
            {
                _smokeHinted = true;
                if (_logRibs) Debug.Log($"[{Who}] «{name}»: ПОРА КОПТИТЬ — пылесосом набери дым у костра и выдувай прямо на мясо (или в люк духовки)", this);
            }
        }

        // ───────────────────────── БРИСКЕТ: обжарка, пенофол, корочка, отдых ─────────────────────────

        private void BrisketTick(float dt, BakeOven oven, float target)
        {
            float k = (target > _fTemp ? _heatRate : _coolRate) * (_wrap.Value == 2 ? 0.7f : 1f);
            _fTemp = Mathf.Lerp(_fTemp, target, 1f - Mathf.Exp(-k * dt));
            if (_fTemp >= _cookFromC)
            {
                float speed = Mathf.Clamp(_fTemp / 120f, 0.5f, 1.6f) * (oven != null ? oven.BakeSpeed : 0.5f);
                // корочка обжарки — снизу, на сильном жару, пока не в пенофоле
                if (_wrap.Value == 0 && oven != null && _fTemp >= _searC)
                {
                    int down = 1 - UpSide;
                    float sv = down == 0 ? _fSear0 : _fSear1;
                    sv = Mathf.Min(1.5f, sv + dt / Mathf.Max(1f, _searSeconds));
                    if (down == 0) _fSear0 = sv; else _fSear1 = sv;
                    if (sv > 1.25f) { _fChar = Mathf.Min(1f, _fChar + dt * 0.04f); Hint("эта сторона пережаривается — переверни клещами (ЛКМ)"); }
                    else if (sv >= 1f && Sear01(1 - down) < 1f) Hint("снизу корочка есть — переверни клещами (ЛКМ), обжарь вторую сторону");
                    else if (sv >= 1f) Hint("обжарен с двух сторон — заверни лодочкой: рулон пенофола, ЛКМ держать");
                }
                float doneK = _wrap.Value == 2 ? 1.35f : 1f;
                _fDone = Mathf.Min(1.5f, _fDone + dt * speed * doneK / Mathf.Max(5f, _bBakeSeconds));
                float dryK = _wrap.Value == 0 ? 1.6f : _wrap.Value == 1 ? 0.7f : 0f;
                _fMoist = Mathf.Max(0f, _fMoist - dt * speed * dryK / Mathf.Max(5f, _drySeconds));
                if (_wrap.Value == 2) _fMoist = Mathf.Min(1f, _fMoist + dt * 0.01f);
                if (_fDone > 0.2f) _fMinMoist = Mathf.Min(_fMinMoist, _fMoist);
                if (_wrap.Value == 0 && _fDone > 0.4f)
                {
                    if (!_lateBoat && _logRibs) Debug.Log($"[Брискет] «{name}»: долго без пенофола — сохнет! Заверни лодочкой (рулон, ЛКМ держать)", this);
                    _lateBoat = true;
                    Hint("без пенофола брискет сохнет — заверни лодочкой (рулон, ЛКМ держать)");
                }
                // корочка томления — в лодочке, сверху открыто, на спокойном жару
                if (_wrap.Value == 1)
                {
                    float rub = Mathf.Max(0.3f, (Rub01(0) + Rub01(1)) * 0.5f);
                    _fBark = Mathf.Min(1.5f, _fBark + dt * speed * rub / Mathf.Max(5f, _barkSeconds));
                    if (_fTemp > _lowMaxC) Hint($"{_fTemp:0} °C — для томления жарко, держи 110–130 °C");
                    if (_fBark >= 1f && !_barkLogged) { _barkLogged = true; if (_logRibs) Debug.Log($"[Брискет] «{name}»: корочка почти чёрная — заверни ПОЛНОСТЬЮ (рулон пенофола, ЛКМ держать ещё раз)", this); }
                    if (_fBark >= 1f) Hint("корочка готова — заверни полностью: рулон пенофола, ЛКМ держать");
                    if (_fBark > 1.3f) _fChar = Mathf.Min(1f, _fChar + dt * 0.03f);
                }
                if (_fTemp > _burnAboveC || _fDone > 1.3f)
                    _fChar = Mathf.Min(1f, _fChar + dt * (0.02f + Mathf.Max(0f, _fTemp - _burnAboveC) * 0.002f + Mathf.Max(0f, _fDone - 1.3f) * 0.1f));
                if (_fMoist < 0.35f && _wrap.Value < 2) Hint($"брискет СОХНЕТ (влажность {_fMoist * 100f:0}%) — брызни моп-соусом (пульверизатор, ЛКМ)");
                if (_fDone >= 1f && !_doneLogged)
                {
                    _doneLogged = true;
                    if (_logRibs) Debug.Log($"[Брискет] «{name}»: ГОТОВ внутри — вынимай клещами (ПКМ), пусть отдохнёт в пенофоле вне духовки {_restSeconds:0} с", this);
                }
                BrisketLogProgress();
            }
            // отдых — вне духовки, завёрнутый, готовый
            if (oven == null && _wrap.Value == 2 && _fDone >= 1f && _fRest < 1f)
            {
                _fRest = Mathf.Min(1f, _fRest + dt / Mathf.Max(1f, _restSeconds));
                if (_fRest >= 1f && !_restLogged) { _restLogged = true; if (_logRibs) Debug.Log($"[Брискет] «{name}»: отдохнул — разверни (ПКМ рулоном пенофола) и нарезай ручной пилой ПОПЕРЁК волокон", this); }
            }
            if (!Finished) TryFinish();
            Push();
        }

        private void BrisketLogProgress()
        {
            if (!_logRibs) return;
            int q = Mathf.FloorToInt(Mathf.Min(_fDone, 1.25f) * 4f);
            if (q == _logStep) return;
            _logStep = q;
            string w = _wrap.Value == 0 ? "без пенофола" : _wrap.Value == 1 ? "лодочка" : "завёрнут";
            if (q > 0 && q < 4) Debug.Log($"[Брискет] «{name}»: готовность {q * 25}% ({_fTemp:0} °C, {w}, корочка {_fBark * 100f:0}%, влажность {_fMoist * 100f:0}%)", this);
            else if (q >= 5) Debug.Log($"[Брискет] «{name}»: ПЕРЕДЕРЖАЛИ — {_fDone * 100f:0}%, вынимай", this);
        }

        /// <summary>Рулон пенофола: следующий слой (0 → лодочка → закрыт). Сервер. false — нельзя (hint — почему).</summary>
        [Server]
        public bool ServerWrap(out string hint)
        {
            hint = null;
            if (!_brisket) { hint = "пенофолом заворачивают брискет"; return false; }
            if (Finished) { hint = "брискет готов — режь"; return false; }
            if (IsHeld) { hint = "положи брискет"; return false; }
            int w = _wrap.Value;
            if (w >= 2) { hint = "уже завёрнут полностью — ПКМ рулоном: развернуть"; return false; }
            if (w == 0)
            {
                if (Sear01(0) < 0.8f || Sear01(1) < 0.8f)
                    hint = $"завернул без обжарки (корочка {Sear01(0) * 100f:0}% / {Sear01(1) * 100f:0}%) — оценка ниже. Лучше: решётка, от {_searC:0} °C, обе стороны";
                _wrap.Value = 1;
                if (_logRibs) Debug.Log($"[Брискет] «{name}»: в пенофоле ЛОДОЧКОЙ (верх открыт) — томи 110–130 °C, брызгай моп-соусом, жди почти чёрную корочку", this);
            }
            else
            {
                _barkAtWrap = _fBark;
                if (_fBark < 0.7f) hint = $"завернул рано — корочка {_fBark * 100f:0}% (лучше почти чёрная, от 100%)";
                _wrap.Value = 2;
                if (_logRibs) Debug.Log($"[Брискет] «{name}»: завёрнут ПОЛНОСТЬЮ — доходит до готовности (корочка {_fBark * 100f:0}%)", this);
            }
            _dirty = true;
            return true;
        }

        /// <summary>ПКМ рулоном — развернуть. Сервер.</summary>
        [Server]
        public bool ServerUnwrap(out string hint)
        {
            hint = null;
            if (!_brisket || _wrap.Value == 0) { hint = "не завёрнут"; return false; }
            if (_fDone < 1f) hint = $"ещё не готов ({_fDone * 100f:0}%) — в пенофоле доходит быстрее";
            else if (_fRest < 1f) hint = $"не отдохнул ({_fRest * 100f:0}%) — подержи завёрнутым вне духовки";
            _wrap.Value = 0;
            if (_logRibs) Debug.Log($"[Брискет] «{name}»: развернули" + (hint != null ? " — " + hint : " — режь ручной пилой поперёк волокон"), this);
            _dirty = true;
            return true;
        }

        private bool _glazeCue;

        private void ServerLogProgress()
        {
            if (!_logRibs) return;
            if (!_brisket && !_glazeCue && _fDone >= 0.6f && (Glaze01(0) < 0.6f || Glaze01(1) < 0.6f))
            {
                _glazeCue = true;
                Debug.Log($"[Рёбра] «{name}»: прожарка 60% — ПОРА МАЗАТЬ ГЛАЗУРЬЮ: кисть (макни в кастрюлю), открой духовку, обе стороны", this);
            }
            int q = Mathf.FloorToInt(Mathf.Min(_fDone, 1.25f) * 4f);
            if (q == _logStep) return;
            _logStep = q;
            if (q > 0 && q < 4) Debug.Log($"[Рёбра] «{name}»: прожарка {q * 25}% ({_fTemp:0} °C, влажность {_fMoist * 100f:0}%)", this);
            else if (q == 4) Debug.Log($"[Рёбра] «{name}»: ПРОЖАРЕНЫ — теперь кисть с глазурью: обе стороны, и ещё ~{_glazeSetSeconds:0} с в духовке", this);
            else if (q >= 5) Debug.Log($"[Рёбра] «{name}»: ПЕРЕДЕРЖАЛИ — {_fDone * 100f:0}%, сохнут и подгорают, вынимай", this);
        }

        private void TryFinish()
        {
            if (_brisket) { TryFinishBrisket(); return; }
            if (_fDone < 1f || Glaze01(0) < 0.6f || Glaze01(1) < 0.6f || _fSet < 1f) return;
            _flags.Value = (byte)(_flags.Value | FlagFinished);
            int sc = ComputeScore(out string why);
            _score.Value = (byte)sc;
            Resolve();
            if (_dish != null) ServerSetProductState(_dish, FoodState.Baked);
            if (_logRibs) Debug.Log($"[Рёбра] «{name}»: ГОТОВО — Рёбра барбекю ★ оценка {sc}/100 ({why})" +
                                   (_dish == null ? " ⚠ нет продукта «Рёбра барбекю» — Барбекю → 1" : ""), this);
            RpcStars((byte)sc);
        }

        private void TryFinishBrisket()
        {
            if (_fDone < 1f || _fRest < 1f || _wrap.Value != 0) return;
            _flags.Value = (byte)(_flags.Value | FlagFinished);
            int sc = ComputeScore(out string why);
            _score.Value = (byte)sc;
            Resolve();
            if (_bDish != null) ServerSetProductState(_bDish, FoodState.Smoked);
            if (_logRibs) Debug.Log($"[Брискет] «{name}»: ГОТОВО — Брискет горячего копчения ★ оценка {sc}/100 ({why}). Режь ручной пилой поперёк волокон" +
                                   (_bDish == null ? " ⚠ нет продукта «Брискет горячего копчения» — Брискет → 1" : ""), this);
            RpcStars((byte)sc);
        }

        private int BrisketScore(out string why)
        {
            float score = 100f;
            var sb = new System.Text.StringBuilder();
            float fat = FatCap01;
            if (fat > 0.55f) { float p = 12f; score -= p; sb.Append($"жир не подрезан ({fat * 100f:0}%) −{p:0}; "); }
            else if (fat < 0.1f) { float p = 8f; score -= p; sb.Append("срезали весь жир — суховат −8; "); }
            else if (FatExcess > 0.3f) { score -= 4f; sb.Append("остались толстые куски жира −4; "); }
            float rub = (Rub01(0) + Rub01(1)) * 0.5f; if (rub < 0.95f) { float p = 22f * (1f - rub); score -= p; sb.Append($"специи {rub * 100f:0}% −{p:0}; "); }
            float sear = (Mathf.Min(1f, Sear01(0)) + Mathf.Min(1f, Sear01(1))) * 0.5f; if (sear < 0.9f) { float p = 14f * (1f - sear); score -= p; sb.Append($"обжарка {sear * 100f:0}% −{p:0}; "); }
            float bark = _barkAtWrap >= 0f ? _barkAtWrap : _fBark;
            if (bark < 0.9f) { float p = 16f * (0.9f - bark) / 0.9f; score -= p; sb.Append($"корочка {bark * 100f:0}% −{p:0}; "); }
            if (_lateBoat) { score -= 8f; sb.Append("долго без пенофола −8; "); }
            float d = _fDone;
            if (d < 0.95f) { float p = (0.95f - d) * 120f; score -= p; sb.Append($"недоготовлен −{p:0}; "); }
            if (d > 1.25f) { float p = (d - 1.25f) * 80f; score -= p; sb.Append($"передержан −{p:0}; "); }
            if (_fMinMoist < 0.5f) { float p = (0.5f - _fMinMoist) * 60f; score -= p; sb.Append($"сох (мин. {_fMinMoist * 100f:0}%) −{p:0}; "); }
            if (_fChar > 0.05f) { float p = _fChar * 45f; score -= p; sb.Append($"подгорел −{p:0}; "); }
            if ((_flags.Value & FlagSlicedRaw) != 0) { score -= 10f; sb.Append("резали сырым −10; "); }
            bool mustard = (Glaze01(0) + Glaze01(1)) * 0.5f >= 0.5f || (_pasteN > 0 && _pasteN >= _dryN);
            if (mustard) { score += 3f; sb.Append(_pasteN > 0 ? "горчичная паста +3; " : "горчица +3; "); }
            else if (_noMustardPenalty > 0f) { score -= _noMustardPenalty; sb.Append($"без горчицы −{_noMustardPenalty:0}; "); }
            if (_mops > 0f && _waters <= 0f) { score += 3f; sb.Append("моп-соус +3; "); }
            if (_fSmoke >= 0.9f) { score += _smokeBonus; sb.Append($"копчение +{_smokeBonus:0}; "); }
            else if (_noSmokePenalty > 0f) { float p = _noSmokePenalty * (0.9f - _fSmoke) / 0.9f; score -= p; sb.Append($"дымок {_fSmoke * 100f:0}% −{p:0}; "); }
            why = sb.Length > 0 ? sb.ToString().TrimEnd(' ', ';') : "всё идеально";
            return Mathf.Clamp(Mathf.RoundToInt(score), 5, 100);
        }

        public int ComputeScore(out string why)
        {
            if (_brisket) return BrisketScore(out why);
            float score = 100f;
            var sb = new System.Text.StringBuilder();
            float mem = Membrane01; if (mem > 0.2f) { float p = 15f * mem; score -= p; sb.Append($"плёнка −{p:0}; "); }
            float rub = (Rub01(0) + Rub01(1)) * 0.5f; if (rub < 0.95f) { float p = 25f * (1f - rub); score -= p; sb.Append($"специи {rub * 100f:0}% −{p:0}; "); }
            float d = _fDone;
            if (d < 0.95f) { float p = (0.95f - d) * 120f; score -= p; sb.Append($"недожарены −{p:0}; "); }
            if (d > 1.2f) { float p = (d - 1.2f) * 80f; score -= p; sb.Append($"передержаны −{p:0}; "); }
            if (_fMinMoist < 0.5f) { float p = (0.5f - _fMinMoist) * 60f; score -= p; sb.Append($"сохли (мин. {_fMinMoist * 100f:0}%) −{p:0}; "); }
            float g = (Glaze01(0) + Glaze01(1)) * 0.5f; if (g < 0.95f) { float p = 20f * (1f - g); score -= p; sb.Append($"соус {g * 100f:0}% −{p:0}; "); }
            float gq = _glazeQW > 0f ? _glazeQSum / _glazeQW : 1f; if (gq < 0.99f) { float p = (1f - gq) * 50f; score -= p; sb.Append($"соус из банки, не глазурь −{p:0}; "); }
            if (_fChar > 0.05f) { float p = _fChar * 45f; score -= p; sb.Append($"подгорели −{p:0}; "); }
            if ((_flags.Value & FlagSlicedRaw) != 0) { score -= 10f; sb.Append("нарезали сырыми −10; "); }
            if (_mops > 0f && _waters <= 0f) { score += 3f; sb.Append("моп-соус +3; "); }
            if (_fSmoke >= 0.9f) { score += _smokeBonus; sb.Append($"копчение +{_smokeBonus:0}; "); }
            else if (_noSmokePenalty > 0f) { float p = _noSmokePenalty * (0.9f - _fSmoke) / 0.9f; score -= p; sb.Append($"дымок {_fSmoke * 100f:0}% −{p:0}; "); }
            why = sb.Length > 0 ? sb.ToString().TrimEnd(' ', ';') : "всё идеально";
            return Mathf.Clamp(Mathf.RoundToInt(score), 5, 100);
        }

        [ObserversRpc]
        private void RpcStars(byte score)
        {
            if (!_stars) return;
            Bounds b = _mr != null ? _mr.bounds : new Bounds(transform.position, Vector3.one * 0.3f);
            int n = Mathf.RoundToInt(_starCount * Mathf.Lerp(0.4f, 1.2f, score / 100f));
            Color c = score >= 70 ? CelebrationStars.Gold : Color.Lerp(CelebrationStars.Gold, new Color(0.75f, 0.75f, 0.78f), 0.6f);
            CelebrationStars.Burst(new Vector3(b.center.x, b.max.y, b.center.z), Mathf.Clamp(b.extents.magnitude / 0.35f, 0.4f, 2f), c, n);
        }

        /// <summary>Духовка, в которой лежат рёбра: корень, середина модели, полость с запасом.</summary>
        private BakeOven FindOven()
        {
            if (_grab.Value != null) return null;
            BakeOven o = BakeOven.Find(transform.position);
            if (o != null) return o;
            if (_mr != null && (o = BakeOven.Find(_mr.bounds.center)) != null) return o;
            // большое мясо (×масштаб): центр пласта над полостью по XZ, по высоте — от дна полости до её верха + полтолщины пласта
            Bounds rb = _mr != null ? _mr.bounds : new Bounds(transform.position, Vector3.one * 0.1f);
            Vector3 c = rb.center;
            foreach (BakeOven ov in BakeOven.All)
            {
                if (ov == null) continue;
                Bounds cb = ov.CavityBounds;
                if (cb.size.sqrMagnitude < 1e-6f) continue;
                float pad = 0.05f + cb.size.x * 0.02f;
                if (c.x < cb.min.x - pad || c.x > cb.max.x + pad || c.z < cb.min.z - pad || c.z > cb.max.z + pad) continue;
                if (c.y < cb.min.y - 0.1f || c.y > cb.max.y + rb.extents.y + pad) continue;
                return ov;
            }
            return null;
        }

        private BakeOven _ovenWas;
        private float _ovenWhyAt;

        /// <summary>Сервер: сказать в лог, когда рёбра попали в духовку / вынули; рядом, но «не внутри» — почему.</summary>
        private void OvenLog(BakeOven oven)
        {
            if (!_logRibs) return;
            if (oven != _ovenWas)
            {
                if (oven != null) Debug.Log($"[{Who}] «{name}»: в духовке «{oven.name}» ✔ — {oven.TemperatureC:0} °C (печь с {_cookFromC:0} °C)", this);
                else if (_ovenWas != null) Debug.Log($"[{Who}] «{name}»: вынули из духовки «{_ovenWas.name}»", this);
                _ovenWas = oven;
            }
            if (oven != null || Time.time < _ovenWhyAt) return;
            _ovenWhyAt = Time.time + 4f;
            Bounds rb = _mr != null ? _mr.bounds : new Bounds(transform.position, Vector3.one * 0.1f);
            foreach (BakeOven ov in BakeOven.All)
            {
                if (ov == null) continue;
                Bounds cb = ov.CavityBounds;
                float d = Vector3.Distance(cb.ClosestPoint(rb.center), rb.center);
                if (d > 3f) continue;
                Debug.LogWarning($"[{Who}] «{name}»: рядом с духовкой «{ov.name}» ({d:0.00} м), но НЕ внутри — не печётся. " +
                                 $"Центр пласта {rb.center:F2}, полость {cb.min:F2}…{cb.max:F2}" +
                                 (_grab.Value != null ? " · в клещах" : "") + (IsHeld ? " · в руке" : "") +
                                 " · положи на решётку внутри (или полость Chamber у духовки мала)", this);
                return;
            }
        }

        // ── духовка отдаёт «Запекать» всем продуктам — рёбра пекутся сами (выше) ──
        public bool ServerHandleWork(FoodPiece piece, FoodProcess process, float seconds, float quality01) => process == FoodProcess.Bake;
        public void ServerStopWork(FoodPiece piece) { }

        // ───────────────────────── СПЕЦИИ (мастерок) ─────────────────────────

        public int ServerReceiveScoop(ushort product, FoodState state, int count, Vector3 point, Vector3 normal, out string why)
        {
            why = null;
            bool paste = false;
            Resolve();
            if (Finished || _fDone > 0.15f) { why = _brisket ? "брискет уже готовится — специи до духовки" : "рёбра уже запекаются — специи кладут до духовки"; return 0; }
            if (_brisket)
            {
                if (_wrap.Value > 0) { why = "брискет в пенофоле"; return 0; }
                ProductDef got = FoodDatabase.Instance != null ? FoodDatabase.Instance.Get(product) : null;
                string gt = got != null && got.Title != null ? got.Title.ToLowerInvariant() : "";
                paste = (_bPaste != null && product == _bPaste.Id) || (gt.Contains("горчичн") && gt.Contains("паст"));
                bool dry = (_bRub != null && product == _bRub.Id) || gt.Contains("руб для брискета");
                if (!paste && !dry && _logRibs) Debug.Log($"[Брискет] «{name}»: мастерком принесли «{(got != null ? got.Title : "?")}» — на брискет нужна «Горчичная паста для брискета» (сварилась ли смесь? Брискет → 1, Брискет → 6)", this);
                if (!paste && !dry) { why = "на брискет — «Горчичная паста для брискета»: горчица 2 + перец 2 + соль 2 + коричневый сахар 1 мастерок, миксером"; return 0; }

            }
            else if (_rub == null || product != _rub.Id) { why = "на рёбра — «Сухой руб»: соль + паприка + перец + сушёный чеснок, перемешать миксером"; return 0; }
            int side = transform.InverseTransformDirection(normal).y >= 0f ? 0 : 1;
            float pend = Pending(side);
            float room = Mathf.Max(0f, RubPerSide * 2f - pend);
            int take = Mathf.Min(count, Mathf.FloorToInt(room + 0.01f));
            if (take <= 0) { why = "на этой стороне специй уже горкой — разотри (ЛКМ мастерком, води мышью)"; return 0; }
            if (_brisket)
            {
                if (paste) { _pasteN += take; _flags.Value = (byte)(_flags.Value | FlagPaste); }
                else { _dryN += take; if (_logRibs) Debug.Log($"[Брискет] «{name}»: сухой руб без горчицы — в оценке −{_noMustardPenalty:0}. Лучше «Горчичная паста для брискета»", this); }
            }
            SetPending(side, pend + take);
            if (_logRibs) Debug.Log($"[{Who}] «{name}»: руб на {SideName(side)} сторону {take} порц. — разотри: ЛКМ мастерком и води мышью", this);
            return take;
        }

        private void SetPending(int side, float v)
        {
            byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 40f), 0, 255);
            if (side == 0) { if (_pend0.Value != b) _pend0.Value = b; }
            else if (_pend1.Value != b) _pend1.Value = b;
        }

        /// <summary>Растирать руб: точка (в осях рёбер), сторона, сколько провёл мышью (м). Сервер.</summary>
        [Server]
        public void ServerRubAt(Vector3 local, int side, float stroke)
        {
            float pend = Pending(side);
            if (pend <= 0.001f) { Hint(_brisket ? "на этой стороне нет специй — брось «Руб для брискета» мастерком (ПКМ)" : "на этой стороне нет специй — брось «Сухой руб» мастерком (ПКМ)"); return; }
            // горчица — специи липнут лучше
            ServerWorkHold();
            float mult = _brisket && (Glaze01(side) >= 0.5f || UsedPaste) ? 1.35f : 1f;
            float used = Paint(_rubL, local, side, stroke * mult, pend / RubPerSide);
            float left = pend - used * RubPerSide;
            // сторона уже вся в специях — излишек горки втирается гуще (шапка), горка уходит
            if (Rub01(side) >= 0.97f && left > 0f) { left = Mathf.Max(0f, left - stroke * 6f); used = Mathf.Max(used, 0.001f); }
            SetPending(side, left);
            if (used > 0f) StrokeFx(0, side, local);
            if (Rub01(side) >= 0.95f && _logRibs && Time.time >= _hintAt) Hint($"{SideName(side)} сторона в специях — переверни (R) и вторую");
        }

        private string SideName(int side) => _brisket ? (side == 0 ? "жировая (верх)" : "мясная (низ)") : (side == 0 ? "мясная" : "костяная");

        /// <summary>Глазурь кистью (у брискета — горчица). Возвращает, сколько порций ушло. Сервер.</summary>
        [Server]
        public float ServerPaintAt(Vector3 local, int side, float stroke, float load, ushort product)
        {
            Resolve();
            ServerWorkHold();
            if (_brisket)
            {
                if (_mustard == null || product != _mustard.Id) { Hint("брискет мажут ГОРЧИЦЕЙ (по желанию, до специй) — макни кисть в горчицу"); return 0f; }
                if (_fDone > 0.05f) { Hint("горчицу — на сырой брискет, до специй"); return 0f; }
                if (Rub01(side) > 0.5f) { Hint("эта сторона уже в специях — горчицу кладут до них"); return 0f; }
                float u = Paint(_glz, local, side, stroke, load) ;
                if (u > 0f) StrokeFx(1, side, local);
                if (Glaze01(side) >= 0.9f && Glaze01(1 - side) < 0.5f) Hint("эта сторона в горчице — переверни (R) и вторую, потом специи мастерком");
                return u;
            }
            if (_fDone < 0.6f) { Hint("соусом мажут почти готовые рёбра (прожарка от 60%) — сначала запеки"); return 0f; }
            float used = Paint(_glz, local, side, stroke, load / _glazePerSide) * _glazePerSide;
            if (used > 0f)
            {
                StrokeFx(1, side, local);
                float q = _glaze != null && product == _glaze.Id ? 1f : 0.7f;
                _glazeQSum += q * used; _glazeQW += used;
                if (Glaze01(side) >= 0.6f && Glaze01(1 - side) < 0.6f) Hint("эта сторона в соусе — переверни клещами и вторую");
            }
            return used;
        }

        /// <summary>Рубанок: снимает плёнку. Только сырые. Сервер.</summary>
        [Server]
        public bool ServerCleanAt(Vector3 local, int side, float stroke)
        {
            if (_fDone > 0.05f) { Hint(_brisket ? "рубанком — только сырой брискет" : "рубанком — только сырые рёбра"); return false; }
            if (_brisket && _wrap.Value > 0) { Hint("брискет в пенофоле"); return false; }
            ServerWorkHold();
            bool fatTop = _brisket && side == 0;
            if (fatTop && FatExcess <= 0.02f) { Hint("жир сверху — тонкий слой ✔, больше не срезаем. Переверни (R) — плёнка снизу"); return false; }
            Erase(_mem, local, side, stroke, fatTop);
            if (_brisket)
            {
                if (fatTop && FatEven) Hint($"жир сверху ~{FatCap01 * 100f:0}% ровно — отлично. Переверни (R) — сними плёнку снизу");
            }
            else if (Membrane01 < 0.08f) Hint("плёнка снята — красота");
            return true;
        }

        /// <summary>Пшик пульверизатором. mop — моп-соус (иначе вода). Сервер.</summary>
        [Server]
        public void ServerSpray(float portions, bool mop)
        {
            float k = mop ? 1f : 0.7f;
            float was = _fMoist;
            _fMoist = Mathf.Min(1f, _fMoist + _sprayMoist * k * Mathf.Clamp(portions / 0.25f, 0.3f, 2f));
            _fTemp = Mathf.Max(20f, _fTemp - 2f);
            if (mop) _mops += portions; else _waters += portions;
            Push();
            if (_logRibs) Debug.Log($"[Рёбра] «{name}»: пшик {(mop ? "моп-соусом" : "водой")} — влажность {was * 100f:0}% → {_fMoist * 100f:0}%", this);
        }

        /// <summary>Покрыть клетки вокруг точки (движение мышью = stroke, м). budget — сколько «сторон» хватит. Возвращает долю стороны.</summary>
        private float Paint(SyncList<byte> l, Vector3 local, int side, float stroke, float budget)
        {
            if (l.Count < Cells || stroke <= 0f || budget <= 0f) return 0f;
            float used = 0f;
            int cells = Rows * Ribs;
            Vector3 d = Dims;
            float R = Mathf.Max(d.x * 0.9f, d.y / Rows * 0.7f) * 1.2f;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Ribs; c++)
                {
                    float f = Falloff(local, r, c, R);
                    if (f <= 0f) continue;
                    int i = Idx(side, r, c);
                    float v = l[i] / 255f;
                    float add = Mathf.Min(1f - v, stroke * _strokeK * f);
                    add = Mathf.Min(add, (budget - used) * cells);
                    if (add <= 0f) continue;
                    l[i] = B(v + add);
                    used += add / cells;
                    if (used >= budget) return budget;
                }
            return used;
        }

        /// <summary>Снять плёнку/жир вокруг точки, но не ниже floor (брискет: тонкий слой жира остаётся ровно везде).</summary>
        private void Erase(SyncList<byte> l, Vector3 local, int side, float stroke, bool fatTop = false)
        {
            if (l.Count < Cells || stroke <= 0f) return;
            Vector3 d = Dims;
            float R = Mathf.Max(d.x * 0.9f, d.y / Rows * 0.7f) * 1.25f;   // пошире — края и уголки тоже достаются
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Ribs; c++)
                {
                    float f = Falloff(local, r, c, R);
                    if (f <= 0f) continue;
                    int i = Idx(side, r, c);
                    float v = l[i] / 255f;
                    float floor = fatTop ? FloorOf(i) : 0f;
                    if (v <= floor) continue;
                    float nv = Mathf.Max(floor, v - stroke * _strokeK * 1.2f * f);
                    if (B(nv) != l[i]) l[i] = B(nv);
                }
        }

        private float Falloff(Vector3 local, int row, int col, float R)
        {
            Vector3 d = Dims;
            float W = Ribs * d.x;
            float cx = -W * 0.5f + d.x * (col + 0.5f), cz = -d.y * 0.5f + d.y * (row + 0.5f) / Rows;
            float dx = local.x - cx, dz = local.z - cz;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            return dist >= R ? 0f : 1f - dist / R;
        }

        // ───────────────────────── ПЕРЕВОРОТ, РУКИ, КЛЕЩИ ─────────────────────────

        /// <summary>Перевернуть пласт (мясная ↔ костяная сторона). Сервер.</summary>
        [Server]
        public void ServerFlip(string who)
        {
            if (_brisket && _wrap.Value > 0) { Hint("в пенофоле не переворачивают"); return; }
            Quaternion rot = Quaternion.AngleAxis(180f, transform.right) * transform.rotation;
            float lowNow = LowestY(transform.rotation, transform.position);
            float lowNew = LowestY(rot, transform.position);
            Vector3 pos = transform.position + Vector3.up * (lowNow - lowNew + 0.004f);
            if (_rb != null && !_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero;
                _rb.position = pos; _rb.rotation = rot;
            }
            transform.SetPositionAndRotation(pos, rot);
            if (_logRibs) Debug.Log($"[Рёбра] «{name}»: перевернули ({who}) — вверху {(UpSide == 0 ? "мясная" : "костяная")} сторона", this);
        }

        private float LowestY(Quaternion rot, Vector3 pos)
        {
            Bounds lb = _rm != null && _rm.Mesh != null ? _rm.Mesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.1f);
            Vector3 s = transform.lossyScale;
            float low = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = new Vector3((i & 1) == 0 ? lb.min.x : lb.max.x, (i & 2) == 0 ? lb.min.y : lb.max.y, (i & 4) == 0 ? lb.min.z : lb.max.z);
                low = Mathf.Min(low, (pos + rot * Vector3.Scale(c, s)).y);
            }
            return low;
        }

        /// <summary>Руками (R): только холодные.</summary>
        [ServerRpc(RequireOwnership = false)]
        private void CmdHandFlip()
        {
            if (IsHeld || _grab.Value != null) return;
            // брискет в пенофоле: R — развернуть руками (рулон в руки брать не нужно)
            if (_brisket && _wrap.Value > 0) { ServerUnwrap(out _); return; }
            if (IsHot) Hint($"горячие ({TempC:0} °C) — аккуратно (можно и клещами: ЛКМ)");
            ServerFlip("руками");
        }

        /// <summary>F пустой рукой: горячие — не взять.</summary>
        public override void ServerInteract(PlayerInteractor interactor)
        {
            if (_grab.Value != null) return;
            // можно взять в любой момент: готовка (прожарка, соус, дымок) сохраняется, в духовке — продолжится, когда снова нагреется
            if (IsHot) Hint($"горячие ({TempC:0} °C) — взял в руки, прожарка {Done01 * 100f:0}% сохранится; вернёшь в духовку — продолжат печься");
            base.ServerInteract(interactor);
        }

        /// <summary>ЛКМ рёбрами в руке: на решётку духовки / на стол ровно; мимо — бросок.</summary>
        public override void ServerPrimaryUse(UseContext ctx)
        {
            if (ctx.Hand != null && ctx.Hand.HeldItem == this && GrillPlacement.Find(ctx, transform, _reach, out GrillPlacement.Spot spot))
            {
                ctx.Hand.ServerDrop(Vector3.zero, 0f);
                PlaceFlat(spot);
                return;
            }
            base.ServerPrimaryUse(ctx);
        }

        [Server]
        public void PlaceFlat(GrillPlacement.Spot spot)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(transform.right, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.right;
            Vector3 up = UpSide == 0 ? Vector3.up : Vector3.down;
            // локальная X — вдоль; вверх — та сторона, что сейчас вверху
            Quaternion rot = Quaternion.LookRotation(Vector3.Cross(fwd.normalized, up), up);
            rot = GrillPlacement.FitRot(transform, rot, spot);
            GrillPlacement.PlaceAt(transform, _rb, rot, spot, 0.004f);
            if (_logRibs) Debug.Log(spot.Oven != null ? $"[Рёбра] «{name}» на решётке «{spot.Oven.name}» — закрой (L) и пеки (от {_cookFromC:0} °C)" : $"[Рёбра] «{name}» положены ровно", this);
        }

        /// <summary>Клещи взяли. Сервер.</summary>
        [Server]
        public bool ServerGrab(NetworkObject tongs)
        {
            if (tongs == null || IsHeld) return false;
            if (_grab.Value != null && _grab.Value != tongs) return false;
            if (_rb != null) { _wasKinematic = _rb.isKinematic; _rb.isKinematic = true; }
            base.NetworkObject.RemoveOwnership();
            _grab.Value = tongs;
            if (_logRibs) Debug.Log($"[Рёбра] «{name}»: взяли клещами — ЛКМ клещами поставить на решётку/стол, ПКМ — отпустить", this);
            return true;
        }

        [Server]
        public void ServerRelease()
        {
            if (_grab.Value == null) return;
            _grab.Value = null;
            if (_rb != null) { _rb.isKinematic = _wasKinematic; _rb.WakeUp(); }
        }

        private void FollowTongs()
        {
            NetworkObject g = _grab.Value;
            if (g == null) return;
            TongsItem t = g.GetComponent<TongsItem>();
            if (t == null || !g.IsSpawned) { ServerRelease(); return; }
            Vector3 tip = t.Tip.position;
            Bounds lb = _rm != null && _rm.Mesh != null ? _rm.Mesh.bounds : new Bounds();
            Vector3 hang = transform.rotation * Vector3.Scale(new Vector3(lb.center.x, lb.max.y, lb.min.z + lb.size.z * 0.15f), transform.lossyScale);
            Vector3 pos = Vector3.Lerp(transform.position, tip - hang, 0.6f);
            if (_rb != null) _rb.position = pos;
            transform.position = pos;
        }

        // ───────────────────────── НАРЕЗКА (пила) ─────────────────────────

        public bool CanCut => Ribs > 1 && !IsHeld && _grab.Value == null && (!_brisket || _wrap.Value == 0);
        public float SecondsPerCut => 0f;
        public bool ClampWhileCutting => true;
        public string CutLabel => $"рёбер {Ribs}";
        public Color CrumbColor => Color.Lerp(_rawMeat, _cookedMeat, Mathf.Clamp01(Done01));

        public Vector3 CutPoint()
        {
            int e = AimEnd();
            return transform.TransformPoint(RibCenter(e) + Vector3.up * Dims.z * 0.5f);
        }

        public void ServerAimAt(Vector3 worldPoint) => _aimX = transform.InverseTransformPoint(worldPoint).x;

        private int AimEnd() => _aimX >= 0f ? Ribs - 1 : 0;

        private Vector3 RibCenter(int col)
        {
            Vector3 d = Dims;
            return new Vector3(-Ribs * d.x * 0.5f + d.x * (col + 0.5f), 0f, 0f);
        }

        public bool AimBox(Vector3 worldPoint, Vector3[] corners)
        {
            if (!_sawFrame) return false;   // жёлтая рамка пилы — выключена (поле Saw Frame)
            if (corners == null || corners.Length < 8 || Ribs <= 1) return false;
            float x = transform.InverseTransformPoint(worldPoint).x;
            int e = x >= 0f ? Ribs - 1 : 0;
            Vector3 c = RibCenter(e), d = Dims;
            float hx = d.x * 0.52f, hz = d.y * 0.56f, y0 = -d.z * 0.7f, y1 = d.z * 0.7f;
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

        private bool _clamped, _clampWas;

        [Server]
        public void ServerSetClamped(bool value)
        {
            if (_rb == null || _grab.Value != null || _clamped == value) return;
            _clamped = value;
            if (value) { _clampWas = _rb.isKinematic; _rb.isKinematic = true; }
            else { _rb.isKinematic = _clampWas; _rb.WakeUp(); }
        }

        public void ServerCutOne(ProcessDef cutProcess, float quality01)
        {
            if (!base.IsServerInitialized || !CanCut) return;
            if (_slicePrefab == null) { Debug.LogError($"[Рёбра] «{name}»: не задан Slice Prefab (одно ребро) — CatCooks → Барбекю → 2", this); return; }
            int e = AimEnd();
            int N = Ribs;
            Vector3 d = Dims;
            Vector3 sliceAt = transform.TransformPoint(RibCenter(e));
            bool raw = _fDone < 0.8f;

            // одно ребро — со своей частью клеток и всеми шкалами
            GameObject go = Instantiate(_slicePrefab, sliceAt, transform.rotation);
            RibsRack sl = go.GetComponent<RibsRack>();
            if (sl == null) { Debug.LogError($"[Рёбра] на Slice Prefab «{_slicePrefab.name}» нет Ribs Rack", _slicePrefab); Destroy(go); return; }
            byte flags = (byte)(_flags.Value | (raw ? FlagSlicedRaw : 0));
            sl.PresetFrom(1, d, Column(_mem, e), Column(_rubL, e), Column(_glz, e), _fDone, _fMoist, _fTemp, _fChar, _fSet, _fMinMoist, flags);
            if (_brisket) sl.PresetBrisket(_fSear0, _fSear1, _fBark, _fRest, _barkAtWrap, _lateBoat);
            sl.PresetSmoke(_fSmoke);
            sl.PresetStats((byte)Score, _mops, _waters, _glazeQSum, _glazeQW, _pasteN, _dryN);
            sl.ServerPreset(Def, State, Clean, Cook, FormScore);
            base.Spawn(go);
            Rigidbody srb = go.GetComponent<Rigidbody>();
            if (srb != null) srb.AddForce((transform.right * (e == 0 ? -1f : 1f) + Vector3.up * 0.5f) * 0.6f, ForceMode.VelocityChange);

            // пласт стал на одно ребро короче — сдвигаем, чтобы остальные остались на месте
            _mem.Clear(); _rubL.Clear(); _glz.Clear();
            byte[] m = Without(_memTmp, e, N), r = Without(_rubTmp, e, N), g = Without(_glzTmp, e, N);
            Fill(_mem, m); Fill(_rubL, r); Fill(_glz, g);
            Vector3 shift = transform.right * ((e == 0 ? 1f : -1f) * d.x * 0.5f * Mathf.Abs(transform.lossyScale.x));
            if (_rb != null) _rb.position += shift;
            transform.position += shift;
            if (raw) _flags.Value = (byte)(_flags.Value | FlagSlicedRaw);
            _n.Value = (byte)(N - 1);
            ServerSetCount(Ribs);
            if (_logRibs) Debug.Log(_brisket ? $"[Брискет] «{name}»: отрезан ломтик — осталось {Ribs}" + (raw ? " (сырым — −10 к оценке)" : "")
                                             : $"[Рёбра] «{name}»: отрезано ребро — осталось {Ribs}" + (raw ? " (сырыми — будут суше, −10 к оценке)" : ""), this);
        }

        private byte[] _memTmp, _rubTmp, _glzTmp;

        private byte[] Column(SyncList<byte> l, int col)
        {
            // заодно снимок всех клеток до удаления (для Without)
            int N = Ribs;
            byte[] snap = new byte[l.Count];
            for (int i = 0; i < snap.Length; i++) snap[i] = l[i];
            if (l == _mem) _memTmp = snap; else if (l == _rubL) _rubTmp = snap; else _glzTmp = snap;
            var res = new byte[2 * Rows];
            for (int s = 0; s < 2; s++) for (int r = 0; r < Rows; r++)
                { int i = (s * Rows + r) * N + col; res[s * Rows + r] = i < snap.Length ? snap[i] : (byte)0; }
            return res;
        }

        private static byte[] Without(byte[] snap, int col, int N)
        {
            int M = N - 1;
            var res = new byte[2 * Rows * M];
            for (int s = 0; s < 2; s++) for (int r = 0; r < Rows; r++)
                {
                    int k = 0;
                    for (int c = 0; c < N; c++)
                    {
                        if (c == col) continue;
                        int i = (s * Rows + r) * N + c;
                        res[(s * Rows + r) * M + k] = snap != null && i < snap.Length ? snap[i] : (byte)0;
                        k++;
                    }
                }
            return res;
        }

        /// <summary>До Spawn: копчение для ломтика/ребра.</summary>
        public void PresetSmoke(float smoke) { _fSmoke = smoke; _smokeLogged = smoke >= 1f; _smokeHinted = true; }

        /// <summary>До Spawn: оценка и «как готовили» — у отрезанного ребра/ломтика та же ценность, что у целого.</summary>
        public void PresetStats(byte score, float mops, float waters, float gqSum, float gqW, int paste, int dry)
        {
            _pScore = score; _mops = mops; _waters = waters; _glazeQSum = gqSum; _glazeQW = gqW; _pasteN = paste; _dryN = dry;
        }

        /// <summary>До Spawn: шкалы брискета для ломтика.</summary>
        public void PresetBrisket(float sear0, float sear1, float bark, float rest, float barkAtWrap, bool lateBoat)
        {
            _fSear0 = sear0; _fSear1 = sear1; _fBark = bark; _fRest = rest; _barkAtWrap = barkAtWrap; _lateBoat = lateBoat;
            _doneLogged = _fDone >= 1f; _restLogged = rest >= 1f; _barkLogged = bark >= 1f;
        }

        /// <summary>До Spawn: одно ребро с частью клеток пласта.</summary>
        public void PresetFrom(int ribs, Vector3 dims, byte[] mem, byte[] rub, byte[] glz, float done, float moist, float temp, float ch, float set, float minMoist, byte flags)
        {
            _presetRibs = true;
            _pN = ribs; _pDims = dims; _pMem = mem; _pRub = rub; _pGlz = glz;
            _pDone = done; _pMoist = moist; _pTemp = temp; _pChar = ch; _pSet = set; _pMin = minMoist; _pFlags = flags;
            Rebuild(ribs, dims);
        }

        // ───────────────────────── КЛИЕНТ: R — перевернуть руками ─────────────────────────

        private void ClientInput()
        {
            if (_flipKey == KeyCode.None || !Input.GetKeyDown(_flipKey) || UiModal.AnyOpen) return;
            if (!LookedAt(out _)) return;
            CmdHandFlip();
        }

        /// <summary>Локальная камера смотрит на эти рёбра (в пределах Reach).</summary>
        public bool LookedAt(out RaycastHit hit)
        {
            hit = default(RaycastHit);
            Camera cam = Camera.main;
            if (cam == null) return false;
            if (!ToolRay.Cast(cam, _reach, null, out hit)) return false;
            return hit.collider != null && hit.collider.GetComponentInParent<RibsRack>() == this;
        }

        // ───────────────────────── ВИД ─────────────────────────

        private void Rebuild(int ribs, Vector3 dims)
        {
            if (_mf == null) return;
            if (_rm != null && _rm.Ribs == ribs && (_rm.Dims - dims).sqrMagnitude < 1e-10f) return;
            if (_rm != null && _rm.Mesh != null) Destroy(_rm.Mesh);
            _rm = RibsMesh.Build(ribs, dims, 7 + GetInstanceID() % 5, _brisket);
            _mf.sharedMesh = _rm.Mesh;
            _cols = new Color[_rm.Mesh.vertexCount];
            if (_box != null)
            {
                Bounds b = _rm.Mesh.bounds;
                _box.center = b.center;
                _box.size = new Vector3(b.size.x, Mathf.Max(b.size.y, dims.z * 0.6f), b.size.z);
            }
            if (_paintStrokes && Application.isPlaying) BuildPaint(dims);
            _dirty = true;
        }

        // ───────────────────────── МАЗКИ (вид) ─────────────────────────

        private void BuildPaint(Vector3 dims)
        {
            if (_rubPaint == null) _rubPaint = new RackPaintLayer(RackPaintLayer.Kind.Rub, "RubLayer");
            if (_glzPaint == null) _glzPaint = new RackPaintLayer(RackPaintLayer.Kind.Glaze, _brisket ? "MustardLayer" : "GlazeLayer");
            float lift = Mathf.Max(0.0008f, dims.z * 0.025f);
            // у брискета горчица ПОД специями, у рёбер соус ПОВЕРХ руба
            _rubPaint.Build(transform, _rm, _paintMaterial, _brisket ? lift * 2f : lift, _brisket ? 2 : 1);
            _glzPaint.Build(transform, _rm, _paintMaterial, _brisket ? lift : lift * 2f, _brisket ? 1 : 2);
            _paintStrokeAt = -10f;   // сразу дорисовать по клеткам
        }

        /// <summary>Сервер: мазок прошёл — всем нарисовать (layer 0 — руб, 1 — соус/горчица).</summary>
        [Server]
        private void StrokeFx(int layer, int side, Vector3 local)
        {
            if (!_paintStrokes) return;
            int k = layer * 2 + side;
            Vector3 from = Time.time - _strokeLastT[k] < 0.35f ? _strokeLast[k] : local;
            _strokeLast[k] = local; _strokeLastT[k] = Time.time;
            RpcStroke((byte)layer, (byte)side, from, local);
        }

        [ObserversRpc]
        private void RpcStroke(byte layer, byte side, Vector3 a, Vector3 b)
        {
            RackPaintLayer l = layer == 0 ? _rubPaint : _glzPaint;
            if (l == null || !l.Ready) return;
            Vector3 d = Dims;
            float R = Mathf.Max(d.x * 0.9f, d.y / Rows * 0.7f) * 0.8f;
            int seed = Mathf.Abs(Mathf.RoundToInt(b.x * 997f + b.z * 613f)) & 1023;
            l.Stroke(side, a, b, R, _strokeAmount * (layer == 0 ? 1f : 0.85f), seed);
            _paintStrokeAt = Time.time;
        }

        private float CellOf(SyncList<byte> l, int side, int row, int col)
        {
            int i = Idx(side, row, col);
            return l != null && i < l.Count ? l[i] / 255f : 0f;
        }

        /// <summary>Цвет, «запечённость» и досинхронизация слоёв по клеткам. Из Repaint.</summary>
        private void PaintLook(float cook, float set, float ch, float bark)
        {
            if (_rubPaint == null || !_rubPaint.Ready) return;
            Color rc = _brisket ? (UsedPaste ? _pasteColor : _bRubColor) : _rubColor;
            float rubCook = Mathf.Clamp01(Mathf.Max(cook * 0.7f, bark) + ch * 0.6f);
            _rubPaint.SetLook(rc, rubCook, 0.18f + 0.12f * Moist01);
            Color gc = _brisket ? _mustardColor : Color.Lerp(_glazeColor, _glazeSetColor, set);
            float gCook = _brisket ? Mathf.Clamp01(cook * 0.8f + ch * 0.6f) : Mathf.Clamp01(set * 0.25f + ch * 0.7f);
            _glzPaint.SetLook(gc, gCook, _brisket ? 0.45f : Mathf.Lerp(0.6f, 0.9f, set));
            // зашёл позже / тест-меню / отрезали ребро — дорисовать по клеткам (не во время мазков, чтобы не было «квадратов»)
            if (Time.time - _paintStrokeAt > 1.5f && _rubL.Count >= Cells && _glz.Count >= Cells)
            {
                for (int sd = 0; sd < 2; sd++)
                {
                    int side = sd;
                    _rubPaint.FromCells((a, r, c) => CellOf(_rubL, a, r, c), side);
                    _glzPaint.FromCells((a, r, c) => CellOf(_glz, a, r, c), side);
                }
            }
        }

        private void LateUpdate()
        {
            if (_rm == null) return;
            if (_dirty && Time.time >= _paintAt) { _paintAt = Time.time + 0.08f; _dirty = false; Repaint(); }
            UpdatePile();
            if (_brisket) UpdateFoil();
            if (_rubPaint != null) _rubPaint.Flush();
            if (_glzPaint != null) _glzPaint.Flush();
        }

        // ───────────────────────── ПЕНОФОЛ (вид) ─────────────────────────

        private Transform _foil;
        private MeshFilter _foilMf;
        private MeshRenderer _foilR;
        private int _foilShown = -1;
        private float _foilT = 1f;
        private static Material _foilMat;

        /// <summary>Фольгированный пенофол: лодочка (дно и бортики) или закрытый «конвертик». Растёт, когда заворачивают.</summary>
        private void UpdateFoil()
        {
            int w = _wrap.Value;
            if (w == 0 && _foil == null) return;
            if (_foil == null)
            {
                var go = new GameObject("Foil (auto)");
                go.layer = gameObject.layer;
                _foil = go.transform;
                _foil.SetParent(transform, false);
                _foilMf = go.AddComponent<MeshFilter>();
                _foilR = go.AddComponent<MeshRenderer>();
                _foilR.sharedMaterial = FoilMaterial();
            }
            if (w != _foilShown)
            {
                _foilShown = w;
                if (_foilMf.sharedMesh != null) Destroy(_foilMf.sharedMesh);
                _foilMf.sharedMesh = w > 0 ? FoilMesh(LocalBounds, w == 2, GetInstanceID()) : null;
            }
            _foilR.enabled = w > 0;
            if (w == 0) return;
            _foilT = Mathf.MoveTowards(_foilT, 1f, Time.deltaTime * 2.2f);
            float k = Mathf.SmoothStep(0f, 1f, _foilT);
            // лодочка — открытым верхом к небу, как бы ни лежал кусок (в пенофоле не переворачивают)
            Quaternion rot = UpSide == 0 ? Quaternion.identity : Quaternion.Euler(180f, 0f, 0f);
            float sy = Mathf.Lerp(0.25f, 1f, k);
            _foil.localRotation = rot;
            _foil.localScale = new Vector3(1f, sy, 1f);
            _foil.localPosition = rot * new Vector3(0f, LocalBounds.min.y * (1f - sy), 0f);
        }

        private static Material FoilMaterial()
        {
            if (_foilMat != null) return _foilMat;
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            _foilMat = new Material(sh) { name = "Penofol (auto)" };
            Color c = new Color(0.82f, 0.84f, 0.86f);
            if (_foilMat.HasProperty("_BaseColor")) _foilMat.SetColor("_BaseColor", c);
            if (_foilMat.HasProperty("_Color")) _foilMat.SetColor("_Color", c);
            if (_foilMat.HasProperty("_Metallic")) _foilMat.SetFloat("_Metallic", 0.85f);
            if (_foilMat.HasProperty("_Smoothness")) _foilMat.SetFloat("_Smoothness", 0.62f);
            if (_foilMat.HasProperty("_Glossiness")) _foilMat.SetFloat("_Glossiness", 0.62f);
            if (_foilMat.HasProperty("_Cull")) _foilMat.SetFloat("_Cull", 0f);            // двусторонний — лодочку видно и изнутри
            return _foilMat;
        }

        /// <summary>Коробочка вокруг куска с мятыми гранями. full — закрыта; иначе дно + бортики на 70% высоты (верх открыт).</summary>
        private static Mesh FoilMesh(Bounds b, bool full, int seed)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            Vector3 mn = b.min - new Vector3(0.012f, 0.006f, 0.012f), mx = b.max + new Vector3(0.012f, full ? 0.012f : 0f, 0.012f);
            if (!full) mx.y = mn.y + (mx.y - mn.y) * 0.72f;
            const int n = 10;
            System.Action<Vector3, Vector3, Vector3, Vector3, bool> face = (o, du, dv, nrm, rim) =>
            {
                int b0 = v.Count;
                for (int j = 0; j <= n; j++)
                    for (int i = 0; i <= n; i++)
                    {
                        float u = i / (float)n, w = j / (float)n;
                        Vector3 p = o + du * u + dv * w;
                        float h = Mathf.Sin(p.x * 97f + seed) * Mathf.Sin(p.z * 83f + p.y * 61f) * 0.0035f;
                        if (rim && j == n) p.y += Mathf.Sin(p.x * 41f + p.z * 37f + seed) * 0.008f;   // мятый край лодочки
                        v.Add(p + nrm * h);
                    }
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        int a = b0 + j * (n + 1) + i, c = a + n + 2;
                        int bb = a + 1, d = a + n + 1;
                        Vector3 cr = Vector3.Cross(v[bb] - v[a], v[c] - v[a]);
                        if (Vector3.Dot(cr, nrm) >= 0f) { t.Add(a); t.Add(bb); t.Add(c); t.Add(a); t.Add(c); t.Add(d); }
                        else { t.Add(a); t.Add(c); t.Add(bb); t.Add(a); t.Add(d); t.Add(c); }
                    }
            };
            Vector3 X = new Vector3(mx.x - mn.x, 0f, 0f), Y = new Vector3(0f, mx.y - mn.y, 0f), Z = new Vector3(0f, 0f, mx.z - mn.z);
            face(mn, X, Z, Vector3.down, false);                                    // дно
            face(mn, X, Y, Vector3.back, !full);                                    // −Z
            face(mn + Z, X, Y, Vector3.forward, !full);                             // +Z
            face(mn, Z, Y, Vector3.left, !full);                                    // −X
            face(mn + X, Z, Y, Vector3.right, !full);                               // +X
            if (full) face(mn + Y, X, Z, Vector3.up, false);                        // крышка
            var m = new Mesh { name = full ? "Penofol_Full (auto)" : "Penofol_Boat (auto)" };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private float L(SyncList<byte> l, int i, float dflt) => l != null && i < l.Count ? l[i] / 255f : dflt;

        private void Repaint()
        {
            int nv = _rm.Mesh.vertexCount;
            if (_cols == null || _cols.Length != nv) _cols = new Color[nv];
            float done = Done01, moist = Moist01, ch = _charN.Value / 255f, set = _set.Value / 255f;
            float cook = Mathf.Clamp01(done);
            Color meat = done < 1f ? Color.Lerp(_rawMeat, _cookedMeat, Mathf.Pow(cook, 0.8f)) : Color.Lerp(_cookedMeat, _overMeat, Mathf.Clamp01((done - 1f) / 0.4f));
            float dry = (1f - moist) * Mathf.Clamp01(done * 1.5f);
            meat = Color.Lerp(meat, _dryMeat, dry * 0.6f);
            Color fatC = Color.Lerp(_fat, _fatCooked, cook);
            Color rubC = Color.Lerp(_rubColor, _rubColor * 0.6f, cook);
            Color glzC = Color.Lerp(_glazeColor, _glazeSetColor, set);
            Color boneC = Color.Lerp(_bone, new Color(0.78f, 0.62f, 0.42f), cook);
            float gAvg = 0f;
            int N = Ribs;
            float sear0 = Sear01(0), sear1 = Sear01(1), bark = Bark01, smokeV = Smoke01;
            bool painted = _rubPaint != null && _rubPaint.Ready;
            float pk = painted ? 0.3f : 1f;   // мазки рисуют слой сверху — вершины только чуть подкрашиваем (тень под специями)
            PaintLook(cook, set, ch, _brisket ? bark : 0f);
            bool ok = _mem.Count >= Cells && _rubL.Count >= Cells && _glz.Count >= Cells;
            for (int v = 0; v < nv; v++)
            {
                byte s = _rm.Side[v];
                Color c;
                float detail = 0.7f;
                if (s == 255)
                {
                    c = Color.Lerp(boneC, _char, ch * 0.7f);
                    detail = 0.25f;
                }
                else if (_brisket)
                {
                    int i = (s * Rows + _rm.Row[v]) * N + Mathf.Min(_rm.Col[v], N - 1);
                    float mem = ok ? L(_mem, i, 1f) : 1f, rub = ok ? L(_rubL, i, 0f) : 0f, must = ok ? L(_glz, i, 0f) : 0f;
                    float edge = _rm.Edge[v], sp = _rm.Speck[v];
                    Color m = Color.Lerp(meat, _overMeat, edge * cook * 0.25f);
                    // жир — только сверху, пятнами (крапинки вершин); снизу и на боках плёнка — тонкая, не белая
                    float fatK = s == 0 ? Mathf.Clamp01(mem * (0.65f + 0.7f * sp)) * 0.9f : mem * (edge > 0.5f ? 0.12f : 0.3f);
                    c = Color.Lerp(m, fatC, _rm.Fat[v] * fatK);
                    c = Color.Lerp(c, _mustardColor, must * 0.75f * (1f - rub * 0.7f) * pk);
                    Color rb0 = UsedPaste ? _pasteColor : _bRubColor;
                    Color rubB = Color.Lerp(rb0, rb0 * 0.7f, cook);
                    c = Color.Lerp(c, rubB, rub * (0.5f + 0.5f * sp) * 0.8f * pk);
                    if (!painted && sp > 0.78f) c = Color.Lerp(c, new Color(0.06f, 0.05f, 0.04f), rub * 0.8f);   // крупный перец
                    if (!painted && sp < 0.1f) c = Color.Lerp(c, new Color(0.95f, 0.93f, 0.88f), rub * 0.5f);    // соль
                    float sear = s == 0 ? sear0 : sear1;
                    c = Color.Lerp(c, _searColor, Mathf.Clamp01(sear) * 0.55f);
                    float bk = Mathf.Clamp01(bark) * (s == 0 ? 1f : 0.45f) * (0.35f + 0.65f * Mathf.Max(rub, 0.3f));
                    c = Color.Lerp(c, _barkColor, bk * 0.9f);
                    c = Color.Lerp(c, _char, Mathf.Clamp01(ch * (0.35f + 0.65f * sp + edge * 0.3f)));
                    detail = Mathf.Lerp(0.75f, 0.35f, bk);
                    gAvg += must * (1f - rub) * (1f - bk);
                }
                else
                {
                    int i = (s * Rows + _rm.Row[v]) * N + Mathf.Min(_rm.Col[v], N - 1);
                    float mem = ok ? L(_mem, i, 1f) : 1f, rub = ok ? L(_rubL, i, 0f) : 0f, glz = ok ? L(_glz, i, 0f) : 0f;
                    float edge = _rm.Edge[v];
                    Color m = Color.Lerp(meat, _overMeat, edge * cook * 0.25f);
                    c = Color.Lerp(m, fatC, _rm.Fat[v] * mem * (s == 1 ? 0.6f : 0.8f));   // снизу плёнка полупрозрачная
                    // снизу без плёнки — проступают кости
                    if (s == 1) c = Color.Lerp(c, boneC, _rm.BoneLine[v] * (1f - mem) * 0.45f);
                    float sp = _rm.Speck[v];
                    c = Color.Lerp(c, rubC, rub * (0.55f + 0.45f * sp) * 0.85f * pk);
                    if (!painted && sp > 0.82f) c = Color.Lerp(c, new Color(0.08f, 0.06f, 0.05f), rub * 0.7f);   // перец
                    c = Color.Lerp(c, glzC, glz * 0.9f * pk);
                    c = Color.Lerp(c, _char, Mathf.Clamp01(ch * (0.35f + 0.65f * sp + edge * 0.3f)));
                    detail = Mathf.Lerp(0.75f, 0.25f, glz);
                    gAvg += glz;
                }
                if (s != 255 && smokeV > 0f) c = Color.Lerp(c, _smokeTint, smokeV * 0.22f);    // дымок — чуть темнее и теплее
                c.a = detail;
                _cols[v] = c;
            }
            _rm.Mesh.colors = _cols;
            if (_mr != null)
            {
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                _mr.GetPropertyBlock(_mpb);
                float g = nv > 0 ? gAvg / nv : 0f;
                _mpb.SetFloat("_GlintStrength", Mathf.Lerp(0.06f, 0.6f, Mathf.Clamp01(g * 2.2f)) * (1f - set * 0.25f));
                _mr.SetPropertyBlock(_mpb);
            }
        }

        /// <summary>Горка руба на верхней стороне, пока не растёрли.</summary>
        private bool _pileUsesTex;

        private void UpdatePile()
        {
            float p = Pending(UpSide);
            bool show = p > 0.02f && !IsHeld;
            if (_pile == null)
            {
                if (!show) return;
                var go = new GameObject("RubPile (auto)");
                go.layer = gameObject.layer;
                _pile = go.transform;
                _pile.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = RackPaintLayer.Mound();
                _pileR = go.AddComponent<MeshRenderer>();
                Material pm = _rubPaint != null ? _rubPaint.PileMaterial(_mr != null ? _mr.sharedMaterial : null) : null;
                _pileUsesTex = pm != null;
                _pileR.sharedMaterial = pm != null ? pm : (_mr != null ? _mr.sharedMaterial : null);
            }
            if (!_pileUsesTex && _rubPaint != null)
            {
                Material pm2 = _rubPaint.PileMaterial(_mr != null ? _mr.sharedMaterial : null);
                if (pm2 != null) { _pileR.sharedMaterial = pm2; _pileUsesTex = true; _pileR.SetPropertyBlock(null); }
            }
            else if (_pileUsesTex && show && _rubPaint != null) _rubPaint.PileMaterial(null);   // цвет горки — как у смеси (паста / руб)
            if (_pileR.enabled != show) _pileR.enabled = show;
            if (!show) return;
            Vector3 d = Dims;
            float k = Mathf.Sqrt(Mathf.Clamp01(p / (RubPerSide * 2f)));
            float r = Mathf.Min(Ribs * d.x, d.y) * 0.22f * (0.5f + k);
            int up = UpSide;
            _pile.localPosition = new Vector3(0f, (up == 0 ? 1f : -1f) * d.z * 0.55f, -d.y * 0.1f);
            _pile.localRotation = up == 0 ? Quaternion.identity : Quaternion.Euler(180f, 0f, 0f);
            _pile.localScale = new Vector3(r, r * 0.45f, r);
            if (_pileUsesTex) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _pileR.GetPropertyBlock(_mpb);
            Color rc = _brisket ? _bRubColor : _rubColor; rc.a = 1f;
            _mpb.SetColor("_BaseColor", rc);
            _pileR.SetPropertyBlock(_mpb);
        }

        // ───────────────────────── ПОДСКАЗКИ ─────────────────────────

        private void Hint(string text)
        {
            if (!_logRibs || Time.time < _hintAt) return;
            _hintAt = Time.time + 4f;
            Debug.Log($"[{Who}] «{name}»: {text}", this);
        }

        public string StageText()
        {
            if (_brisket) return BrisketStage();
            if (Finished) return $"★ ГОТОВО — Рёбра барбекю · оценка {Score}/100";
            float d = Done01;
            string temp = $"{TempC:0} °C";
            BakeOven ovn = OvenNow();
            if (d < 0.03f && ovn != null)
                return TempC < _cookFromC
                    ? $"в духовке ✔ · греются {TempC:0} °C → печь начнёт с {_cookFromC:0} °C (духовка {ovn.TemperatureC:0} °C, надо 150–180)"
                    : $"в духовке ✔ · печётся · {temp}";
            if (d < 0.03f)
            {
                string mem = Membrane01 > 0.15f ? $"плёнка {Membrane01 * 100f:0}% (рубанок)" : "плёнка снята";
                string up = Pending(UpSide) > 0.02f ? " · ЛКМ мастерком — растирай" : "";
                return $"сырые · {mem} · специи: мясо {Rub01(0) * 100f:0}% / кость {Rub01(1) * 100f:0}%{up} · R — перевернуть";
            }
            string moist = Moist01 < 0.5f ? $" · ВЛАЖНОСТЬ {Moist01 * 100f:0}% — брызни!" : $" · влажность {Moist01 * 100f:0}%";
            string where = ovn != null ? "" : " · ВНЕ ДУХОВКИ";
            if (d < 0.25f) return $"прожарка {Mathf.Min(d, 1f) * 100f:0}% · пеки до ~30%, потом КОПЧЕНИЕ · {temp}{moist}{where}";
            if (d < 0.6f && Smoke01 < 0.9f)
                return $"прожарка {d * 100f:0}% · КОПТИ: пылесос — дым у костра → выдувай на рёбра · дымок {Smoke01 * 100f:0}% · {temp}{moist}{where}";
            if (d < 0.6f) return $"прожарка {Mathf.Min(d, 1f) * 100f:0}% · дымок ✔ · глазурью — с 60% · {temp}{moist}{where}";
            if (Glaze01(0) < 0.6f || Glaze01(1) < 0.6f)
                return $"прожарка {Mathf.Min(d, 1.5f) * 100f:0}% · ПОРА МАЗАТЬ ГЛАЗУРЬЮ — кисть, обе стороны (мясо {Glaze01(0) * 100f:0}% / кость {Glaze01(1) * 100f:0}%) · {temp}{moist}{where}";
            string glaze = $" · соус: мясо {Glaze01(0) * 100f:0}% / кость {Glaze01(1) * 100f:0}%";
            string set = Glaze01(0) >= 0.6f && Glaze01(1) >= 0.6f ? $" · лак {_set.Value / 255f * 100f:0}%" : "";
            return $"прожарка {Mathf.Min(d, 1.5f) * 100f:0}% · {temp}{moist} · дымок {Smoke01 * 100f:0}%{glaze}{set}";
        }

        private string BrisketStage()
        {
            if (Finished) return $"★ ГОТОВО — Брискет горячего копчения · оценка {Score}/100 · режь пилой поперёк волокон";
            float d = Done01;
            string w = Wrap == 0 ? "без пенофола" : Wrap == 1 ? "пенофол: лодочка" : "пенофол: закрыт";
            BakeOven ovn = OvenNow();
            if (d < 0.03f && Sear01(0) < 0.02f && Sear01(1) < 0.02f && ovn != null)
                return ovn.TemperatureC < _searC
                    ? $"в духовке ✔ · {TempC:0} °C · духовка {ovn.TemperatureC:0} °C — для обжарки разогрей до {_searC:0} °C"
                    : $"в духовке ✔ · греется {TempC:0} °C → корочка пойдёт с {_searC:0} °C";
            if (d < 0.03f && Sear01(0) < 0.02f && Sear01(1) < 0.02f)
            {
                string up = Pending(UpSide) > 0.02f ? " · ЛКМ мастерком — растирай" : "";
                float fat = FatCap01, fatMax = SideMax(_mem, 0);
                string fatS = FatEven ? $"жир сверху ~{fat * 100f:0}% — тонкий слой ✔, переверни (R)"
                            : fat > 0.55f ? $"жир сверху {fat * 100f:0}% — срежь рубанком до ~{_fatFloor * 100f:0}%"
                                          : $"жир сверху {fat * 100f:0}% — остались толстые места (до {fatMax * 100f:0}%), пройди рубанком по белому";
                float film = SideAvg(_mem, 1);
                string filmS = film > 0.1f ? $"плёнка снизу {film * 100f:0}%" : "плёнка снизу ✔";
                string mus = UsedPaste ? "паста" : (Glaze01(0) + Glaze01(1)) * 0.5f >= 0.5f ? "горчица ✔ · руб" : "горчичная паста";
                return $"сырой брискет · {fatS} · {filmS} · {mus} {Rub01(0) * 100f:0}/{Rub01(1) * 100f:0}% (мастерком){up} · R — перевернуть";
            }
            string temp = $"{TempC:0} °C" + (ovn == null && Rest01 <= 0f && d < 1f ? " · ВНЕ ДУХОВКИ" : "");
            string moist = Wrap < 2 ? (Moist01 < 0.5f ? $" · ВЛАЖНОСТЬ {Moist01 * 100f:0}% — брызни!" : $" · влажность {Moist01 * 100f:0}%") : "";
            if (Wrap == 0 && d < 0.3f)
                return $"обжарка: верх {Mathf.Min(1f, Sear01(0)) * 100f:0}% / низ {Mathf.Min(1f, Sear01(1)) * 100f:0}% (от {_searC:0} °C) · {temp} · {w}{moist}";
            if (d >= 1f && Wrap == 2) return Rest01 >= 1f ? "готов и отдохнул · R — развернуть" : $"готов · отдых {Rest01 * 100f:0}% (вне духовки, в пенофоле) · {temp}";
            if (d >= 1f && Rest01 >= 1f && Wrap == 0) return "готов и отдохнул";
            string smokeCue = Wrap == 1 && Smoke01 < 0.9f ? " · КОПТИ: пылесос — дым у костра → на брискет" : "";
            return $"готовность {Mathf.Min(d, 1.5f) * 100f:0}% · корочка {Bark01 * 100f:0}% · {w} · {temp}{moist} · дымок {Smoke01 * 100f:0}%{smokeCue}";
        }

        private BakeOven _ovenNow;
        private float _ovenNowAt = -1f;

        /// <summary>В какой духовке рёбра сейчас (для подсказки; раз в 0.25 с).</summary>
        private BakeOven OvenNow()
        {
            if (Time.time >= _ovenNowAt) { _ovenNowAt = Time.time + 0.25f; _ovenNow = FindOven(); }
            return _ovenNow;
        }

        private void OnGUI()
        {
            if (!_showHud || Event.current.type != EventType.Repaint) return;
            Camera cam = Camera.main;
            if (cam == null || _mr == null) return;
            Vector3 to = _mr.bounds.center - cam.transform.position;
            float far = Mathf.Max(_hudDistance, 2f * MeatCutSettings.WorldScale);
            bool look = to.magnitude <= far && Vector3.Angle(cam.transform.forward, to) <= 28f;
            BakeOven ovn = OvenNow();
            if (!look && ovn != null && ToolRay.Cast(cam, far + 4f, null, out RaycastHit oh) && oh.collider != null && oh.collider.GetComponentInParent<BakeOven>() == ovn)
                look = true;   // смотришь на духовку (крышка закрыта) — видно, что там с рёбрами
            if (!look) return;
            Vector3 sp = cam.WorldToScreenPoint(new Vector3(_mr.bounds.center.x, _mr.bounds.max.y + 0.12f, _mr.bounds.center.z));
            if (sp.z <= 0f) return;
            string s = StageText();
            if (_hud == null) _hud = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            Color c = Finished ? new Color(1f, 0.85f, 0.25f) : Moist01 < 0.5f && IsCooking ? new Color(1f, 0.55f, 0.3f) : Color.white;
            Rect rc = new Rect(sp.x - 260f, Screen.height - sp.y - 44f, 520f, 40f);
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(rc, Texture2D.whiteTexture); GUI.color = old;
            _hud.normal.textColor = c;
            GUI.Label(rc, s, _hud);
            if ((IsCooking || ovn != null) && !Finished)
            {
                float w = 260f, x0 = sp.x - w * 0.5f, y0 = rc.yMax + 2f;
                GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(new Rect(x0 - 1, y0 - 1, w + 2, 10), Texture2D.whiteTexture);
                GUI.color = Done01 >= 0.6f && !_brisket ? new Color(0.45f, 0.95f, 0.45f) : new Color(1f, 0.65f, 0.25f);
                GUI.DrawTexture(new Rect(x0, y0, w * Mathf.Clamp01(Done01), 8), Texture2D.whiteTexture);
                if (!_brisket) { GUI.color = Color.white; GUI.DrawTexture(new Rect(x0 + w * 0.6f - 1f, y0 - 3f, 2f, 14f), Texture2D.whiteTexture); }   // метка «мазать с 60%»
                GUI.color = old;
            }
        }

        // ───────────────────────── ТЕСТ ─────────────────────────

        [ContextMenu("Тест: почистить, натереть и запечь до глазури (Play, хост)")]
        private void TestToGlaze()
        {
            if (!NetTest.ServerReady(this, "Рёбра")) return;
            for (int i = 0; i < _mem.Count; i++) { _mem[i] = 0; _rubL[i] = 255; }
            _pend0.Value = 0; _pend1.Value = 0;
            _fDone = 1f; _fMoist = 0.8f; _fTemp = 150f; _fMinMoist = 0.8f;
            Push();
            Debug.Log($"[Рёбра] «{name}»: тест — чистые, в специях, прожарены. Дальше: кисть с глазурью, обе стороны", this);
        }

        [ContextMenu("Тест: готово сразу")]
        private void TestFinish()
        {
            if (!NetTest.ServerReady(this, "Рёбра")) return;
            for (int i = 0; i < _mem.Count; i++) { _mem[i] = 0; _rubL[i] = 255; _glz[i] = 255; }
            _fDone = 1.05f; _fMoist = 0.8f; _fMinMoist = 0.8f; _fSet = 1f; _fTemp = 60f; _glazeQSum = 1f; _glazeQW = 1f;
            Push();
            TryFinish();
        }

        [ContextMenu("Брискет-тест: подрезан, в горчице и специях, обжарен (дальше — пенофол)")]
        private void TestBrisketSeared()
        {
            if (!_brisket || !NetTest.ServerReady(this, "Брискет")) return;
            for (int i = 0; i < _mem.Count; i++) { _mem[i] = 80; _rubL[i] = 255; _glz[i] = 200; }
            _pend0.Value = 0; _pend1.Value = 0;
            _fSear0 = 1f; _fSear1 = 1f; _fDone = 0.15f; _fTemp = 160f;
            Push();
            Debug.Log($"[Брискет] «{name}»: тест — подрезан, в специях, обжарен. Дальше: рулон пенофола (ЛКМ держать) — лодочка, томить 110–130 °C", this);
        }

        [ContextMenu("Брискет-тест: корочка готова (дальше — завернуть полностью)")]
        private void TestBrisketBark()
        {
            if (!_brisket || !NetTest.ServerReady(this, "Брискет")) return;
            TestBrisketSeared();
            _wrap.Value = 1; _fBark = 1f; _fDone = 0.7f; _fMoist = 0.7f; _fMinMoist = 0.7f; _fTemp = 125f;
            Push();
            Debug.Log($"[Брискет] «{name}»: тест — в лодочке, корочка 100%. Дальше: рулон ещё раз — закрыть, дождаться готовности, отдых", this);
        }

#if UNITY_EDITOR
        public void EditorSetBrisket(bool on) { UnityEditor.Undo.RecordObject(this, "Брискет"); _brisket = on; EditorRecord(); }

        private void EditorRecord()
        {
            UnityEditor.EditorUtility.SetDirty(this);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this)) UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }

        /// <summary>Для меню: сырой брискет (жир сверху, мясо снизу).</summary>
        public static Color[] EditorBrisketColors(RibsMesh rm)
        {
            var cols = new Color[rm.Mesh.vertexCount];
            Color raw = new Color(0.74f, 0.24f, 0.22f), fat = new Color(0.94f, 0.86f, 0.80f);
            for (int v = 0; v < cols.Length; v++)
            {
                Color c = Color.Lerp(raw, fat, rm.Fat[v] * (rm.Side[v] == 0 ? 0.9f : 0.5f));
                c.a = 0.7f; cols[v] = c;
            }
            return cols;
        }

        public void EditorSetup(int ribs, Vector3 dims, Material mat, GameObject slicePrefab)
        {
            UnityEditor.Undo.RecordObject(this, "Рёбра");
            _ribs = ribs; _ribWidth = dims.x; _boneLength = dims.y; _thickness = dims.z; _material = mat;
            if (slicePrefab != null) _slicePrefab = slicePrefab;
            EditorRecord();
        }

        /// <summary>Для меню: цвета сырых рёбер в вершинах (вид в редакторе).</summary>
        public static Color[] EditorRawColors(RibsMesh rm)
        {
            var cols = new Color[rm.Mesh.vertexCount];
            Color raw = new Color(0.74f, 0.24f, 0.22f), fat = new Color(0.94f, 0.86f, 0.80f), bone = new Color(0.93f, 0.89f, 0.78f);
            for (int v = 0; v < cols.Length; v++)
            {
                Color c = rm.Side[v] == 255 ? bone : Color.Lerp(raw, fat, rm.Fat[v] * (rm.Side[v] == 1 ? 0.6f : 0.8f));
                c.a = 0.7f; cols[v] = c;
            }
            return cols;
        }

#endif
    }

    /// <summary>Названия продуктов барбекю — одни и те же в коде, книге и меню.</summary>
    public static class BBQTitles
    {
        public const string Ribs = "Рёбра", Dish = "Рёбра барбекю", Garlic = "Чеснок сушёный", Rub = "Сухой руб",
                            AppleJuice = "Яблочный сок", AppleVinegar = "Яблочный уксус", Bourbon = "Бурбон", Mop = "Моп-соус",
                            Sauce = "Соус барбекю", BrownSugar = "Коричневый сахар", Honey = "Мёд", Worcester = "Вустерский соус",
                            Glaze = "Глазурь барбекю", Water = "Вода", Salt = "Соль", Pepper = "Перец чёрный", Paprika = "Паприка",
                            Brisket = "Брискет", BrisketRub = "Руб для брискета", Mustard = "Горчица", BrisketDish = "Брискет горячего копчения",
                            BrisketPaste = "Горчичная паста для брискета";
    }
}
