using System.Threading.Tasks;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// КУСОК МЯСА с новой туши: лопатка, грудинка (брискет), корейка, рёбра, окорок, нога (рулька / голяшка), стейк…
    ///
    /// Один общий префаб на ВСЕ части и стейки обоих видов — форму кусок строит сам (CarcassModel)
    /// по виду, части и ломтю. Вид кусок получает при отпиле с туши (или из полей ниже — если поставлен в сцену сам).
    ///
    /// ═══ ЧТО С НИМ ДЕЛАТЬ ═══
    ///   • РУЧНАЯ ПИЛА: рамка у края куска — ЛКМ пилить — отпадает ломоть: у корейки — стейк (у быка «Стейк рибай»),
    ///     у остальных — кусок того же мяса (у ноги — поперёк кости, как для холодца и оссобуко).
    ///   • ЭЛЕКТРОРУБАНОК: ЛКМ на сыром куске и води ВЗГЛЯДОМ — снимается шкура (свинина) или жир и
    ///     плёнка (говядина), под ними — чистое мясо. Подсказка над куском: «плёнка N%».
    ///   • Дальше — как обычное мясо: в измельчитель, на огонь, в тару (продукт свой у каждой части).
    ///
    /// ═══ НА КОРНЕ ПРЕФАБА ═══ Mesh Filter · Mesh Renderer (Mat_Meat) · Box Collider · Rigidbody ·
    /// Network Object · Network Transform · этот компонент. Делает меню «Новые туши → 4».
    /// </summary>
    public sealed class MeatCut : FoodPiece, ICuttable, ICuttableAim
    {
        [Header("Кусок (если поставлен в сцену сам, не с туши)")]
        [SerializeField] private MeatSpecies _species = MeatSpecies.Pork;
        [SerializeField] private MeatPart _part = MeatPart.LoinL;

        [Header("Рубанок")]
        [Tooltip("Сколько плёнки снимает метр «проведённого» рубанка (1 — вся в радиусе)")]
        [SerializeField] private float _cleanPerMeter = 6f;
        [Tooltip("Радиус рубанка, м")]
        [SerializeField] private float _cleanRadius = 0.07f;
        [Tooltip("Плотность мяса, кг/м³ — масса куска по объёму")]
        [SerializeField] private float _density = 1050f;
        [SerializeField] private bool _logCut = true;

        private readonly SyncVar<byte> _sp = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _pt = new SyncVar<byte>(0);
        private readonly SyncVar<short> _a = new SyncVar<short>(CarcassModel.NoSlab);
        private readonly SyncVar<short> _b = new SyncVar<short>(CarcassModel.NoSlab);
        private readonly SyncVar<Vector3> _org = new SyncVar<Vector3>(Vector3.zero);   // точка опоры (оси туши)
        private readonly SyncVar<float> _scl = new SyncVar<float>(0f);                // масштаб куска в мире (как у туши); 0 — не задан
        private readonly SyncList<byte> _mem = new SyncList<byte>();      // плёнка по клеткам 255 → снято 0

        private MeshFilter _mf;
        private BoxCollider _box;
        private Mesh _mesh;
        private MeatMesh _mm;
        private Task<MeatMesh> _pending;
        private long _pendingKey = long.MinValue, _shownKey = long.MinValue;
        private bool _dirtyPaint;
        private bool[] _usedCells;
        private Vector3[] _cellSurf;     // центр плёнки клетки на поверхности (оси туши) — для «снизу/сверху»
        private float[] _cellK;          // временное: сила рубанка по клеткам
        private bool _aimHi = true;

        // до спавна
        private bool _cutPreset;
        private MeatSpecies _pSp; private MeatPart _pPt; private short _pA = CarcassModel.NoSlab, _pB = CarcassModel.NoSlab; private byte[] _pMem;
        private float _pScl;
        private bool _sclWarned;

        public MeatSpecies Species => (MeatSpecies)_sp.Value;
        public MeatPart Part => (MeatPart)_pt.Value;
        public bool IsSlab => _a.Value != CarcassModel.NoSlab && _b.Value != CarcassModel.NoSlab;
        /// <summary>Точка опоры куска в осях туши (центр его коробки при появлении) — меш строится вокруг неё.
        /// Остаток после отпила её не меняет: кусок просто становится короче, а не «прыгает».</summary>
        public Vector3 Origin => _org.Value;

        /// <summary>Меш построен для текущего ломтя (можно пилить дальше).</summary>
        public bool MeshReady => _mm != null && _pending == null && _shownKey == KeyOf(Species, Part, _a.Value, _b.Value);

        /// <summary>Доля плёнки/шкуры, что ещё осталась (0..1).</summary>
        public float Membrane01
        {
            get
            {
                if (_usedCells == null || _mem.Count == 0) return 1f;
                float s = 0f; int n = 0;
                for (int i = 0; i < _mem.Count && i < _usedCells.Length; i++) if (_usedCells[i]) { s += _mem[i]; n++; }
                return n > 0 ? s / (255f * n) : 0f;
            }
        }

        public bool IsCookedState
        {
            get
            {
                int s = (int)State;
                return s >= (int)FoodState.Boiled && s <= (int)FoodState.Torched;
            }
        }

        /// <summary>До Spawn: какой кусок (с туши или ломоть). mem — плёнка (null — вся на месте).</summary>
        public void PresetCut(MeatSpecies sp, MeatPart part, short a, short b, byte[] mem, float scale = 0f)
        {
            _cutPreset = true; _pSp = sp; _pPt = part; _pA = a; _pB = b; _pMem = mem; _pScl = scale;
            if (scale > 0f) ApplyScale(scale);
            _species = sp; _part = part;
            ShowBase();
        }

        // ───────────────────────── жизнь ─────────────────────────

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            _mf = GetComponent<MeshFilter>(); if (_mf == null) _mf = gameObject.AddComponent<MeshFilter>();
            if (GetComponent<MeshRenderer>() == null)
            {
                MeshRenderer mr = gameObject.AddComponent<MeshRenderer>();
                MeatCutSettings st = MeatCutSettings.Instance;
                if (st != null) mr.sharedMaterial = st.Material;
            }
            _rb = GetComponent<Rigidbody>();
            _box = GetComponent<BoxCollider>();
            MeatPhysics.Setup(_rb);
            MeatPhysics.NoScaleSync(gameObject);
            ShowBase();
        }

        private void FixedUpdate()
        {
            if (!base.IsServerInitialized) return;
            if (!_clamped) WorkHoldTick();
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
            MeatSpecies sp = _cutPreset ? _pSp : _species;
            MeatPart pt = _cutPreset ? _pPt : _part;
            _sp.Value = (byte)sp; _pt.Value = (byte)pt;
            _a.Value = _cutPreset ? _pA : CarcassModel.NoSlab;
            _b.Value = _cutPreset ? _pB : CarcassModel.NoSlab;
            _org.Value = CarcassModel.BoxCenter(sp, pt, _a.Value, _b.Value);
            float scl = _cutPreset && _pScl > 0f ? _pScl : Mathf.Abs(transform.lossyScale.x);
            _scl.Value = scl;
            ApplyScale(scl);
            int n = CarcassModel.CellN * CarcassModel.CellN * CarcassModel.CellN;
            _mem.Clear();
            for (int i = 0; i < n; i++) _mem.Add(_cutPreset && _pMem != null && i < _pMem.Length ? _pMem[i] : (byte)255);
            if (_logCut) Debug.Log($"[Мясо] «{name}»: {CarcassModel.Nice(sp, pt)}" + (IsSlab ? $", ломоть {(_b.Value - _a.Value) / 10f * scl:0.0} см" : "") +
                                   $", масштаб ×{scl:0.##} — продукт «{(Def != null ? Def.Title : "?")}»", this);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _sp.OnChange += OnByte; _pt.OnChange += OnByte;
            _a.OnChange += OnShort; _b.OnChange += OnShort;
            _org.OnChange += OnOrg;
            _scl.OnChange += OnScl;
            _mem.OnChange += OnMem;
            StateChanged += OnState;
            if (_scl.Value > 0f) ApplyScale(_scl.Value);
            Rebuild();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _sp.OnChange -= OnByte; _pt.OnChange -= OnByte;
            _a.OnChange -= OnShort; _b.OnChange -= OnShort;
            _org.OnChange -= OnOrg;
            _scl.OnChange -= OnScl;
            _mem.OnChange -= OnMem;
            StateChanged -= OnState;
        }

        private void OnByte(byte a, byte b, bool s) => Rebuild();
        private void OnShort(short a, short b, bool s) => Rebuild();
        private void OnOrg(Vector3 a, Vector3 b, bool s) { _shownKey = long.MinValue; Rebuild(); }
        private void OnMem(SyncListOperation op, int i, byte a, byte b, bool s) => _dirtyPaint = true;
        private void OnScl(float a, float b, bool s) { if (b > 0f) ApplyScale(b); }

        /// <summary>Масштаб куска в мире = масштаб туши (меш в осях туши, «настоящие» метры × Scale).</summary>
        public float WorldScale => _scl.Value > 0f ? _scl.Value : (_cutPreset && _pScl > 0f ? _pScl : Mathf.Abs(transform.lossyScale.x));

        private void ApplyScale(float s)
        {
            float cur = Mathf.Abs(transform.lossyScale.x);
            if (s <= 0f || cur < 1e-5f || Mathf.Abs(cur - s) < s * 0.01f) return;
            transform.localScale = transform.localScale * (s / cur);
        }

        /// <summary>Кто-то (сеть, подбор) сбросил масштаб — вернуть и сказать один раз.</summary>
        private void KeepScale()
        {
            float s = _scl.Value;
            if (s <= 0f) return;
            float cur = Mathf.Abs(transform.lossyScale.x);
            if (Mathf.Abs(cur - s) < s * 0.01f) return;
            if (!_sclWarned) { _sclWarned = true; Debug.LogWarning($"[Мясо] «{name}»: масштаб сбросился ×{cur:0.##} → возвращаю ×{s:0.##} (Network Transform → выключи Synchronize Scale, если повторяется)", this); }
            ApplyScale(s);
        }
        private void OnState(FoodState a, FoodState b) => _dirtyPaint = true;

        private void OnDestroy()
        {
            if (_mesh != null) SafeDestroy.Kill(_mesh);
        }

        // ───────────────────────── меш ─────────────────────────

        private static long KeyOf(MeatSpecies sp, MeatPart p, short a, short b)
            => ((long)(byte)sp << 48) | ((long)(byte)p << 40) | ((long)(ushort)a << 16) | (ushort)b;

        /// <summary>Сразу показать готовый меш части (из настроек), пока строится свой.</summary>
        private void ShowBase()
        {
            if (_mf == null) return;
            MeatSpecies sp = _cutPreset ? _pSp : _species; MeatPart pt = _cutPreset ? _pPt : _part;
            short a = _cutPreset ? _pA : CarcassModel.NoSlab, b = _cutPreset ? _pB : CarcassModel.NoSlab;
            if (a != CarcassModel.NoSlab) { if (_mesh == null) _mf.sharedMesh = null; return; }   // ломоть — покажется, когда построится
            MeatCutSettings st = MeatCutSettings.Instance;
            Mesh m = st != null ? st.BaseMesh(sp, pt) : null;
            if (m != null && _mesh == null) { _mf.sharedMesh = m; FitCollider(m.bounds); }
        }

        private void Rebuild()
        {
            MeatSpecies sp = Species; MeatPart pt = Part; short a = _a.Value, b = _b.Value;
            long key = KeyOf(sp, pt, a, b);
            if (key == _shownKey || key == _pendingKey) return;
            if (CarcassModel.TryCached(sp, pt, a, b, out MeatMesh have)) { Apply(have, key); return; }
            _pendingKey = key;
            // пока строится — готовый меш части из настроек (у ломтя — ничего: доли секунды)
            MeatCutSettings st = MeatCutSettings.Instance;
            Mesh baseMesh = a == CarcassModel.NoSlab && st != null ? st.BaseMesh(sp, pt) : null;
            if (_mesh == null) { _mf.sharedMesh = baseMesh; if (baseMesh != null) FitCollider(baseMesh.bounds); }
            // строим в фоне: большой кусок — доли секунды, игра не замирает
            _pending = Task.Run(() => CarcassModel.Build(sp, pt, a, b));
        }

        private void Update()
        {
            if (_pending != null && _pending.IsCompleted)
            {
                Task<MeatMesh> t = _pending; long key = _pendingKey;
                _pending = null; _pendingKey = long.MinValue;
                if (t.IsFaulted) Debug.LogError($"[Мясо] «{name}»: меш не построился — {t.Exception?.GetBaseException().Message}", this);
                else if (key == KeyOf(Species, Part, _a.Value, _b.Value)) Apply(t.Result, key);
                else Rebuild();
            }
            if (_dirtyPaint && _mm != null) { _dirtyPaint = false; Repaint(); }
            KeepScale();
            HudProbe();
        }

        private void Apply(MeatMesh m, long key)
        {
            _shownKey = key;
            if (m == null) { Debug.LogWarning($"[Мясо] «{name}»: пустой кусок ({CarcassModel.Nice(Species, Part)})", this); return; }
            _mm = m;
            _usedCells = new bool[CarcassModel.CellN * CarcassModel.CellN * CarcassModel.CellN];
            for (int i = 0; i < m.Cell.Length; i++) if (m.Mask[i] > 0f) _usedCells[m.Cell[i]] = true;
            _cellSurf = new Vector3[_usedCells.Length];
            var cnt = new int[_usedCells.Length];
            for (int i = 0; i < m.Cell.Length; i++)
            {
                if (m.Mask[i] <= 0f) continue;
                _cellSurf[m.Cell[i]] += m.V[i] + m.Origin; cnt[m.Cell[i]]++;
            }
            for (int i = 0; i < cnt.Length; i++) if (cnt[i] > 0) _cellSurf[i] /= cnt[i];
            if (_mesh == null) _mesh = new Mesh { name = CarcassModel.Id(Species, Part) + " (auto)" };
            Cook01(out float cooked, out float burnt);
            _shownShrink = Shrink();
            CarcassModel.Fill(_mesh, m, Origin, MemArr(), cooked, burnt, _shownShrink);
            _mf.sharedMesh = _mesh;
            FitCollider(_mesh.bounds);
            if (base.IsServerInitialized && _rb != null) _rb.mass = Mathf.Clamp(m.Volume * _density, 0.1f, 60f);
        }

        private Color[] _colTmp;
        private byte[] _memArr;

        /// <summary>Снимок плёнки (null — пока нет данных: вся плёнка на месте).</summary>
        private byte[] MemArr()
        {
            if (_mem.Count == 0) return null;
            if (_memArr == null || _memArr.Length != _mem.Count) _memArr = new byte[_mem.Count];
            for (int i = 0; i < _memArr.Length; i++) _memArr[i] = _mem[i];
            return _memArr;
        }

        private void Repaint()
        {
            if (_mesh == null || _mm == null) return;
            Cook01(out float cooked, out float burnt);
            // приготовилось — кусок усох: меш заново (к центру), коллайдер по нему
            float sh = Shrink();
            if (Mathf.Abs(sh - _shownShrink) > 0.001f)
            {
                _shownShrink = sh;
                CarcassModel.Fill(_mesh, _mm, Origin, MemArr(), cooked, burnt, sh);
                FitCollider(_mesh.bounds);
                if (_logCut) Debug.Log($"[Мясо] «{name}»: {FoodStateInfo.NameOf(State)} — усадка {Mathf.RoundToInt((1f - sh) * 100f)}%", this);
                return;
            }
            _colTmp = CarcassModel.Paint(_mm, MemArr(), cooked, burnt, _colTmp);
            _mesh.colors = _colTmp;
        }

        private float _shownShrink = 1f;

        /// <summary>Усадка мяса по состоянию: жареное/запечённое −8%, варёное −5%, опалённое −3%, сгоревшее −12%.</summary>
        private float Shrink()
        {
            switch (State)
            {
                case FoodState.Boiled: case FoodState.Stewed: return 0.95f;
                case FoodState.Fried: case FoodState.Baked: case FoodState.Spitted: case FoodState.Smoked: return 0.92f;
                case FoodState.Torched: return 0.97f;
                case FoodState.Undercooked: return 0.96f;
                case FoodState.Overcooked: return 0.9f;
                case FoodState.Burnt: return 0.88f;
                default: return 1f;
            }
        }

        private void Cook01(out float cooked, out float burnt)
        {
            cooked = 0f; burnt = 0f;
            switch (State)
            {
                case FoodState.Boiled: case FoodState.Stewed: cooked = 0.55f; break;
                case FoodState.Fried: case FoodState.Baked: case FoodState.Spitted: case FoodState.Smoked: case FoodState.Torched: cooked = 0.9f; break;
                case FoodState.Undercooked: cooked = 0.45f; break;
                case FoodState.Overcooked: cooked = 1f; burnt = 0.3f; break;
                case FoodState.Burnt: cooked = 1f; burnt = 0.85f; break;
            }
        }

        private void FitCollider(Bounds b)
        {
            if (_box == null) return;
            _box.center = b.center;
            _box.size = Vector3.Max(b.size, Vector3.one * 0.01f);
        }

        // ───────────────────────── рубанок ─────────────────────────

        /// <summary>Рубанок прошёл по точке (мир) на stroke метров. Сервер. false — нельзя (готовое мясо).</summary>
        [Server]
        public bool ServerCleanAt(Vector3 world, float stroke, out string hint)
        {
            hint = null;
            if (IsCookedState) { hint = "рубанком — только сырое мясо"; return false; }
            if (_mem.Count == 0 || stroke <= 0f) return false;
            ServerWorkHold();
            Vector3 c = Origin + transform.InverseTransformPoint(world);
            float dec = Mathf.Clamp(stroke * _cleanPerMeter, 0f, 1f) * 255f;
            float R = _cleanRadius * 1.6f;
            int spotsWas = Spots(out _);
            if (_cellK == null || _cellK.Length != _mem.Count) _cellK = new float[_mem.Count];
            System.Array.Clear(_cellK, 0, _cellK.Length);
            if (_mm != null && _mm.V != null)
            {
                // по ПОВЕРХНОСТИ: клетка чистится, если рубанок прошёл рядом с её плёнкой (а не с центром клетки —
                // у круглых кусков центр угловой клетки далеко внутри, и там оставались «вечные» пятна)
                float R2 = R * R;
                Vector3[] V = _mm.V; Vector3 o = _mm.Origin;
                for (int v = 0; v < V.Length; v++)
                {
                    if (_mm.Mask[v] <= 0f) continue;
                    float d2 = (V[v] + o - c).sqrMagnitude;
                    if (d2 > R2) continue;
                    int cell = _mm.Cell[v];
                    if (cell >= _cellK.Length) continue;
                    float k = Mathf.Clamp01(1.2f - Mathf.Sqrt(d2) / R);
                    if (k > _cellK[cell]) _cellK[cell] = k;
                }
            }
            else
                for (int i = 0; i < _mem.Count; i++)
                {
                    float d = Vector3.Distance(CarcassModel.CellCenter(Species, Part, i), c);
                    if (d <= R) _cellK[i] = Mathf.Clamp01(1.2f - d / R);
                }
            bool any = false;
            for (int i = 0; i < _mem.Count; i++)
            {
                if (_cellK[i] <= 0f) continue;
                if (_usedCells != null && i < _usedCells.Length && !_usedCells[i]) continue;
                int v = _mem[i];
                int nv = Mathf.Max(0, v - Mathf.CeilToInt(dec * _cellK[i]));
                if (nv < 20) nv = 0;   // остатки меньше 8% — снято
                if (nv != v) { _mem[i] = (byte)nv; any = true; }
            }
            int spots = Spots(out int below);
            if (spots == 0) hint = "зачищено — красота";
            else if (below == spots) hint = $"сверху чисто — пятна снизу ({spots}), переверни кусок";
            if (any && _logCut)
            {
                if (spots == 0 && spotsWas > 0) Debug.Log($"[Мясо] «{name}»: {CarcassModel.Nice(Species, Part)} — зачищено полностью", this);
                else if (spots > 0 && spots != spotsWas && spots <= 3)
                    Debug.Log($"[Мясо] «{name}»: осталось пятен плёнки {spots}" + (below > 0 ? $" (снизу {below})" : ""), this);
            }
            return any;
        }

        /// <summary>Сколько клеток плёнки ещё заметно (> 15%). below — из них снизу (лицом к полу).</summary>
        public int Spots(out int below)
        {
            below = 0;
            if (_usedCells == null || _mem.Count == 0) return 0;
            int n = 0;
            Vector3 mid = _box != null ? _box.bounds.center : transform.position;
            for (int i = 0; i < _mem.Count && i < _usedCells.Length; i++)
            {
                if (!_usedCells[i] || _mem[i] <= 38) continue;
                n++;
                if (_cellSurf != null && transform.TransformPoint(_cellSurf[i] - Origin).y < mid.y - 0.01f) below++;
            }
            return n;
        }

        /// <summary>Зачищено, % (100 — плёнки нет).</summary>
        public float Cleaned01 => 1f - Membrane01;

        // ───────────────────────── пила ─────────────────────────

        private float Thickness => CarcassModel.SliceThickness(Species, Part);
        private int Axis => CarcassModel.SliceAxis(Part);
        private float Lo => _mm != null ? _mm.AxisLo : 0f;
        private float Hi => _mm != null ? _mm.AxisHi : 0f;

        public bool CanCut => MeshReady && !IsHeld && CarcassModel.Sliceable(Part) && Hi - Lo > Thickness * 1.6f;
        public float SecondsPerCut => 0f;
        public bool ClampWhileCutting => true;
        public string CutLabel => $"{CarcassModel.Nice(Species, Part)} · режет там, куда смотришь";
        public Color CrumbColor => Def != null ? Def.BaseColor : new Color(0.7f, 0.25f, 0.22f);

        private float AxisOf(Vector3 world)
        {
            Vector3 c = Origin + transform.InverseTransformPoint(world);
            return Axis == 0 ? c.x : c.y;
        }

        private float _aimAxis = float.NaN;

        public void ServerAimAt(Vector3 worldPoint)
        {
            _aimAxis = AxisOf(worldPoint);
            _aimHi = _aimAxis >= (Lo + Hi) * 0.5f;
        }

        /// <summary>Где пройдёт рез — ТАМ, КУДА СМОТРИШЬ (не тоньше полломтя от края). Нет прицела — ломоть с края.</summary>
        private float CutAt(float aim, bool hi)
        {
            float t = Thickness;
            if (float.IsNaN(aim)) return hi ? Hi - t : Lo + t;
            float lo = Lo + t * 0.5f, up = Hi - t * 0.5f;
            if (up <= lo) return (Lo + Hi) * 0.5f;
            return Mathf.Clamp(aim, lo, up);
        }

        /// <summary>Отпиливаемая часть: от реза до того края, куда целишься: [s0, s1] по оси нарезки (оси туши).</summary>
        private void SliceRange(bool hi, out float s0, out float s1) => SliceRange(hi, _aimAxis, out s0, out s1);

        private void SliceRange(bool hi, float aim, out float s0, out float s1)
        {
            float c = CutAt(aim, hi);
            if (hi) { s0 = c; s1 = Hi + 0.02f; }
            else { s0 = Lo - 0.02f; s1 = c; }
        }

        public Vector3 CutPoint()
        {
            SliceRange(_aimHi, out float s0, out float s1);
            float cut = _aimHi ? s0 : s1;
            Vector3 c = _mm != null ? _mm.Origin : Origin;
            if (Axis == 0) c.x = cut; else c.y = cut;
            return transform.TransformPoint(c - Origin);
        }

        public bool AimBox(Vector3 worldPoint, Vector3[] corners)
        {
            if (corners == null || corners.Length < 8 || !CanCut) return false;
            float ax = AxisOf(worldPoint);
            bool hi = ax >= (Lo + Hi) * 0.5f;
            SliceRange(hi, ax, out float s0, out float s1);
            s0 = Mathf.Max(s0, Lo); s1 = Mathf.Min(s1, Hi);
            Vector3 mn = _mm.Min + _mm.Origin, mx = _mm.Max + _mm.Origin;
            if (Axis == 0) { mn.x = s0; mx.x = s1; } else { mn.y = s0; mx.y = s1; }
            Vector3 o = Origin;
            for (int k = 0; k < 2; k++)
            {
                float y = k == 0 ? mn.y : mx.y;
                corners[k * 4 + 0] = transform.TransformPoint(new Vector3(mn.x, y, mn.z) - o);
                corners[k * 4 + 1] = transform.TransformPoint(new Vector3(mx.x, y, mn.z) - o);
                corners[k * 4 + 2] = transform.TransformPoint(new Vector3(mx.x, y, mx.z) - o);
                corners[k * 4 + 3] = transform.TransformPoint(new Vector3(mn.x, y, mx.z) - o);
            }
            return true;
        }

        private bool _clamped, _clampWas;

        [Server]
        public void ServerSetClamped(bool value)
        {
            if (_rb == null || _clamped == value) return;
            _clamped = value;
            if (value) { _clampWas = _rb.isKinematic; _rb.isKinematic = true; }
            else { _rb.isKinematic = _clampWas; _rb.WakeUp(); }
        }

        public void ServerCutOne(ProcessDef cutProcess, float quality01)
        {
            if (!base.IsServerInitialized || !CanCut) return;
            MeatCutSettings st = MeatCutSettings.Instance;
            GameObject prefab = st != null ? st.CutPrefab : null;
            if (prefab == null) { Debug.LogError("[Мясо] нет префаба куска — MeatCutSettings → Cut Prefab (Новые туши → 6)", this); return; }
            bool hi = _aimHi;
            SliceRange(hi, out float s0, out float s1);
            short sa = CarcassModel.ToMm(s0), sb = CarcassModel.ToMm(s1);
            float cut = hi ? s0 : s1;
            short ra = hi ? CarcassModel.ToMm(Lo - 0.02f) : CarcassModel.ToMm(cut);
            short rb = hi ? CarcassModel.ToMm(cut) : CarcassModel.ToMm(Hi + 0.02f);
            MeatSpecies sp = Species; MeatPart pt = Part;

            Vector3 sliceOrigin = CarcassModel.BoxCenter(sp, pt, sa, sb);
            Vector3 at = transform.TransformPoint(sliceOrigin - Origin);
            GameObject go = Instantiate(prefab, at, transform.rotation);
            MeatCut slice = go.GetComponent<MeatCut>();
            if (slice == null) { Debug.LogError($"[Мясо] на префабе «{prefab.name}» нет Meat Cut", prefab); Destroy(go); return; }
            var mem = new byte[_mem.Count];
            for (int i = 0; i < mem.Length; i++) mem[i] = _mem[i];
            slice.PresetCut(sp, pt, sa, sb, mem, WorldScale);
            ProductDef sliceProduct = st.SliceProduct(sp, pt);
            slice.ServerPreset(sliceProduct != null ? sliceProduct : Def, State, Clean, Cook, FormScore);
            base.Spawn(go);
            Rigidbody srb = go.GetComponent<Rigidbody>();
            Vector3 away = (Axis == 0 ? transform.right : transform.up) * (hi ? 1f : -1f);
            if (srb != null) srb.AddForce((away + Vector3.up * 0.4f) * 0.5f, ForceMode.VelocityChange);

            // остаток — тот же кусок, короче (точка опоры та же — не двигается)
            _a.Value = ra; _b.Value = rb;
            Rebuild();
            if (_logCut) Debug.Log($"[Мясо] «{name}»: отпилен кусок {Mathf.Abs(Mathf.Min(s1, Hi) - Mathf.Max(s0, Lo)) * WorldScale * 100f:0} см (рез там, куда смотрел) — «{(sliceProduct != null ? sliceProduct.Title : Def?.Title)}»", this);
        }

        // ───────────────────────── подсказка ─────────────────────────

        private static int _hudFrame = -1;
        private static MeatCut _hud;

        private void HudProbe()
        {
            if (_hudFrame == Time.frameCount) return;
            _hudFrame = Time.frameCount;
            _hud = null;
            Camera cam = Camera.main;
            if (cam == null) return;
            float reach = Mathf.Clamp(3f * MeatCutSettings.WorldScale * 0.5f, 3f, 10f);   // куски крупные (масштаб мяса)
            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit h, reach, ~0, QueryTriggerInteraction.Ignore) && h.collider != null)
                _hud = h.collider.GetComponentInParent<MeatCut>();
        }

        private static GUIStyle _style;

        private void OnGUI()
        {
            if (_hud != this || Event.current.type != EventType.Repaint) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, richText = true };
            string nice = CarcassModel.Nice(Species, Part);
            string title = Def != null && !nice.StartsWith(Def.Title) ? $"{nice} — {Def.Title}" : nice;
            string cut = CarcassModel.Sliceable(Part) ? (CanCut ? " · пила: режет там, куда смотришь" : " · пилить нечего") : "";
            var r = new Rect(Screen.width * 0.5f - 300f, Screen.height * 0.58f, 600f, 24f);
            Shadowed(r, $"<b>{title}</b>{cut}");
            if (IsCookedState) return;
            // вторая строка: зачистка
            int spots = Spots(out int below);
            float pct = Cleaned01 * 100f;
            string line;
            if (_usedCells == null) line = "зачистка: …";
            else if (spots == 0) line = "<color=#7CFC7C>зачищено 100% ✔</color>";
            else
            {
                string col = pct >= 80f ? "#FFE36E" : "#FF9A5A";
                line = $"<color={col}>зачищено {Mathf.Min(pct, 99f):0}%</color> · пятен плёнки: {spots}" +
                       (below > 0 ? (below == spots ? " — все СНИЗУ, переверни кусок" : $" (снизу {below})") : "") +
                       " · рубанок: ЛКМ держать и водить";
            }
            Shadowed(new Rect(r.x, r.y + 22f, r.width, r.height), line);
            // полоска
            var bar = new Rect(Screen.width * 0.5f - 90f, r.y + 46f, 180f, 6f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = spots == 0 ? new Color(0.45f, 0.95f, 0.45f) : new Color(1f, 0.75f, 0.3f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(Cleaned01), bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private static void Shadowed(Rect r, string text)
        {
            string plain = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", "");
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), plain, _style);
            GUI.color = Color.white;
            GUI.Label(r, text, _style);
        }

#if UNITY_EDITOR
        public void EditorSetup(MeatSpecies sp, MeatPart part)
        {
            _species = sp; _part = part;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }

    /// <summary>
    /// Крупное мясо (×масштаб) не «взрывается»: брошенный/упавший кусок, застрявший в соседнем коллайдере,
    /// Unity выталкивает с огромной скоростью — копит «энергию» и улетает. Ограничиваем вытал­кивание и скорость.
    /// </summary>
    public static class MeatPhysics
    {
        public const float MaxSpeed = 14f, MaxSpin = 12f;

        public static void Setup(Rigidbody rb)
        {
            if (rb == null) return;
            rb.maxDepenetrationVelocity = 1.5f;
            rb.maxAngularVelocity = MaxSpin;
        }

        /// <summary>Каждый FixedUpdate на сервере.</summary>
        public static void Calm(Rigidbody rb)
        {
            if (rb == null || rb.isKinematic) return;
            Vector3 v = rb.linearVelocity;
            if (v.sqrMagnitude > MaxSpeed * MaxSpeed) rb.linearVelocity = v.normalized * MaxSpeed;
        }

        /// <summary>Network Transform не синхронизирует масштаб (кусок сам знает свой масштаб) — иначе дёргает коллайдер.</summary>
        public static void NoScaleSync(GameObject go)
        {
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null || c.GetType().Name != "NetworkTransform") continue;
                var m = c.GetType().GetMethod("SetSynchronizeScale", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (m != null) { try { m.Invoke(c, new object[] { false }); } catch { } continue; }
                var f = c.GetType().GetField("_synchronizeScale", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null && f.FieldType == typeof(bool)) f.SetValue(c, false);
            }
        }
    }
}
