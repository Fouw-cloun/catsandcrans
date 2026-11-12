using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// РАСКАЛЁННЫЙ МЕТАЛЛ: ковш экскаватора/бульдозера как маленькая кастрюля на горелке.
    ///
    /// Струя горелки (IHeatJet) упирается в ковш — в этом месте металл раскаляется
    /// ПЯТНОМ: тёмно-красный → красный → оранжевый → жёлтый. Не весь ковш, а то, куда бьёт
    /// пламя; тепло понемногу растекается по металлу и со временем остывает
    /// (пятно тускнеет и пропадает). Там, где внутри вода, металл краснеет, но
    /// не желтеет — вода забирает жар себе.
    ///
    /// Жар с той части, что касается содержимого (дно и стенки полости), идёт внутрь:
    /// ковш присылает таре температуру «огня», а CookwareHeat греет воду, кипятит,
    /// ведёт шаг рецепта «Варить» — всё как у кастрюли на костре.
    ///
    /// ВИД: поверх корпуса — копия меша чуть «надутая» по нормалям, с цветами вершин
    /// (шейдер CatCooks/Heat Glow, нет его — URP Particles/Unlit). Сама модель и её
    /// материалы не меняются.
    ///
    /// СЕТЬ: по сети ничего не идёт. Где горелка и горит ли она — уже есть у всех,
    /// каждый игрок считает пятно сам по одной формуле. Температуру содержимого
    /// считает сервер (CookwareHeat).
    ///
    /// ═══ КУДА ВЕШАТЬ ═══
    /// На корень ковша, рядом с Cookware и CookwareHeat. Модель ковша — Read/Write Enabled.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SurfaceHeatGlow : MonoBehaviour
    {
        [Header("Что светится")]
        [Tooltip("Меши корпуса ковша. Пусто — найдёт сам (без воды, горки, авто-объектов)")]
        [SerializeField] private Renderer[] _surfaces;
        [Tooltip("На сколько клеток нагрева делится ковш по длинной стороне. Больше — пятно точнее")]
        [Range(8, 40)][SerializeField] private int _resolution = 24;

        [Header("Нагрев от струи")]
        [Tooltip("Как быстро раскаляется место удара (доля шкалы в секунду при мощности 1)")]
        [SerializeField] private float _heatRate = 0.6f;
        [Tooltip("Размер пятна относительно радиуса пламени")]
        [SerializeField] private float _spotScale = 2.2f;
        [Tooltip("Наименьший размер пятна — доля размера ковша")]
        [Range(0.02f, 0.4f)][SerializeField] private float _minSpot = 0.09f;
        [Tooltip("Растекание тепла по металлу (1/с). Больше — пятно расползается шире")]
        [Range(0f, 3f)][SerializeField] private float _spread = 0.45f;
        [Tooltip("Остывание (1/с). Меньше — дольше светится, когда огонь убрали")]
        [Range(0.01f, 1f)][SerializeField] private float _cool = 0.09f;
        [Tooltip("Температура металла на полной шкале, °C (для подписи)")]
        [SerializeField] private float _maxC = 700f;

        [Header("Содержимое")]
        [Tooltip("Какой «огонь» получает содержимое при полностью раскалённом дне, °C (костёр ~200–300)")]
        [SerializeField] private float _contentFireC = 300f;
        [Tooltip("Какая доля дна полости должна раскалиться полностью, чтобы огонь был полным (0.18 = 18%)")]
        [Range(0.02f, 1f)][SerializeField] private float _fullCoverage = 0.18f;
        [Tooltip("Насколько далеко от полости металл ещё отдаёт жар внутрь (доля размера ковша)")]
        [Range(0f, 0.5f)][SerializeField] private float _contactMargin = 0.12f;
        [Tooltip("Жидкость внутри остужает металл: где вода — ковш краснеет, но не желтеет")]
        [SerializeField] private bool _liquidCools = true;
        [Range(0.2f, 1f)][SerializeField] private float _wetMaxHeat = 0.55f;

        [Header("Вид")]
        [Tooltip("С какой доли шкалы металл начинает краснеть")]
        [Range(0f, 0.5f)][SerializeField] private float _glowFrom = 0.1f;
        [SerializeField] private Color _dull = new Color(0.5f, 0.04f, 0.02f, 0.7f);
        [SerializeField] private Color _red = new Color(1f, 0.1f, 0.03f, 0.92f);
        [SerializeField] private Color _orange = new Color(1f, 0.48f, 0.06f, 1f);
        [SerializeField] private Color _yellow = new Color(1f, 0.93f, 0.55f, 1f);
        [Tooltip("Яркость (с Bloom светится), 1 — без пересвета")]
        [Range(1f, 6f)][SerializeField] private float _boost = 2.4f;
        [Tooltip("Мультяшные ступеньки цвета (0 — плавно)")]
        [Range(0f, 1f)][SerializeField] private float _toonBands = 0.7f;
        [Tooltip("Насколько оболочка свечения приподнята над металлом (доля размера ковша)")]
        [Range(0f, 0.02f)][SerializeField] private float _lift = 0.004f;
        [Tooltip("Языки пламени растекаются по металлу в месте удара")]
        [SerializeField] private bool _flameLick = true;
        [SerializeField] private float _lickPerSecond = 80f;
        [Tooltip("Сколько раз в секунду перекрашивать")]
        [Range(5f, 60f)][SerializeField] private float _updateRate = 20f;

        [Header("v14: равномерный нагрев, марево, лёгкий пар")]
        [Tooltip("Металл прогревается ровнее: тепло быстрее расходится от пятна (× к Spread, с подшагами — без «рябы»)")]
        [Range(1f, 8f)][SerializeField] private float _spreadBoost = 3f;
        [Tooltip("Весь ковш понемногу выравнивается к средней температуре (1/с): холодные края подтягиваются, пятно не «скачет»")]
        [Range(0f, 2f)][SerializeField] private float _evenness = 0.35f;
        [Tooltip("Пятно от струи мягче и шире (0 — как было, 1 — заметно шире)")]
        [Range(0f, 1f)][SerializeField] private float _softSpot = 0.5f;
        [Tooltip("Ступеньки цвета мягче (вместо Toon Bands). Меньше — ровнее покраснение")]
        [Range(0f, 1f)][SerializeField] private float _toonBandsV14 = 0.3f;
        [Tooltip("Марево (искажение воздуха) над горячим металлом")]
        [SerializeField] private bool _haze = true;
        [Tooltip("С какой доли шкалы нагрева идёт марево")]
        [Range(0f, 1f)][SerializeField] private float _hazeFrom = 0.12f;
        [Range(0f, 2f)][SerializeField] private float _hazeStrength = 1f;
        [Tooltip("Лёгкий пар/дымок от раскалённого металла")]
        [SerializeField] private bool _heatWisps = true;
        [Range(0f, 1f)][SerializeField] private float _wispsFrom = 0.3f;
        [SerializeField] private Color _wispColor = new Color(0.92f, 0.92f, 0.95f, 0.16f);

        [Header("Отладка")]
        [SerializeField] private bool _debugHud = true;
        [SerializeField] private bool _log = true;

        // ───────── сетка нагрева (оси корня, без масштаба) ─────────
        private int _nx, _ny, _nz;
        private Vector3 _min, _cell;
        private float[] _h, _tmp;
        private bool[] _solid, _contact;
        private int _contactCells;
        private float _sizeWorld = 1f;
        private float _scaleK = 1f;               // мир/локальные корня

        private sealed class Overlay
        {
            public Transform T;
            public Mesh Mesh;
            public int[] Base;          // индекс клетки (x,y,z) упакован
            public Vector3[] Frac;
            public Color32[] Colors;
            public MeshRenderer R;
        }
        private readonly List<Overlay> _overlays = new List<Overlay>();
        private static Material _mat;

        private Cookware _cw;
        private CookwareHeat _heat;
        private CookwareLiquidVisual _lv;
        private bool _ready;
        private float _updAcc;
        private float _maxHeat, _hotShare, _contentFire, _lastSetC = -1f;
        private int _jetsOn;
        private float _logAt;
        private ParticleSystem _lick;
        private float _lickAcc;
        private readonly RaycastHit[] _castHits = new RaycastHit[24];
        private bool _lickStylized;
        private int _hotIdx = -1;
        private float _meanHeat, _wispAcc;

        /// <summary>Самая горячая точка, 0..1.</summary>
        public float MaxHeat01 => _maxHeat;
        /// <summary>Самая горячая точка, °C.</summary>
        public float MaxSurfaceC => Mathf.Lerp(20f, _maxC, _maxHeat);
        /// <summary>«Огонь», который ковш отдаёт содержимому, °C (0 — не греет).</summary>
        public float ContentFireC => _contentFire;

        private void Awake()
        {
            _cw = GetComponent<Cookware>();
            _heat = GetComponent<CookwareHeat>();
            _lv = GetComponentInChildren<CookwareLiquidVisual>(true);
        }

        private void Start() => Build();

        private void OnDestroy()
        {
            foreach (Overlay o in _overlays)
                if (o.Mesh != null) SafeDestroy.Kill(o.Mesh);
            if (_lick != null) SafeDestroy.Kill(_lick.gameObject);
        }

        // ───────────────────────── СБОРКА ─────────────────────────

        private void Build()
        {
            if (_surfaces == null || _surfaces.Length == 0) _surfaces = AutoSurfaces();
            var meshes = new List<MeshFilter>();
            foreach (Renderer r in _surfaces)
            {
                if (r == null) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (!mf.sharedMesh.isReadable)
                {
                    Debug.LogWarning($"[Жар ковша] «{name}»: у модели «{mf.sharedMesh.name}» выключен Read/Write — " +
                                     "в импорте модели включи Read/Write Enabled, иначе свечения не будет", this);
                    continue;
                }
                meshes.Add(mf);
            }
            if (meshes.Count == 0)
            {
                Debug.LogWarning($"[Жар ковша] «{name}»: нет мешей корпуса (Surfaces пусто и сам не нашёл)", this);
                return;
            }

            // Габариты в осях корня.
            Matrix4x4 toRoot = transform.worldToLocalMatrix;
            bool any = false;
            Vector3 mn = Vector3.zero, mx = Vector3.zero;
            foreach (MeshFilter mf in meshes)
            {
                Mesh m = mf.sharedMesh;
                Matrix4x4 k = toRoot * mf.transform.localToWorldMatrix;
                foreach (Vector3 v in m.vertices)
                {
                    Vector3 p = k.MultiplyPoint3x4(v);
                    if (!any) { mn = mx = p; any = true; }
                    else { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
                }
            }
            Vector3 size = mx - mn;
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (longest < 1e-5f) return;
            Vector3 ls = transform.lossyScale;
            _scaleK = Mathf.Max(1e-5f, (Mathf.Abs(ls.x) + Mathf.Abs(ls.y) + Mathf.Abs(ls.z)) / 3f);
            _sizeWorld = longest * _scaleK;

            float cs = longest / _resolution;
            _nx = Mathf.Max(2, Mathf.CeilToInt(size.x / cs) + 1);
            _ny = Mathf.Max(2, Mathf.CeilToInt(size.y / cs) + 1);
            _nz = Mathf.Max(2, Mathf.CeilToInt(size.z / cs) + 1);
            _cell = new Vector3(Mathf.Max(size.x / (_nx - 1), 1e-5f), Mathf.Max(size.y / (_ny - 1), 1e-5f), Mathf.Max(size.z / (_nz - 1), 1e-5f));
            _min = mn;
            int n = _nx * _ny * _nz;
            _h = new float[n];
            _tmp = new float[n];
            _solid = new bool[n];
            _contact = new bool[n];

            // Где полость (жар отсюда идёт внутрь).
            Collider recv = _cw != null ? _cw.InsideVolume : null;
            Bounds cav = default(Bounds);
            bool hasCav = recv != null;
            if (hasCav) { cav = recv.bounds; cav.Expand(_contactMargin * _sizeWorld * 2f); }
            float mouthY = _cw != null ? _cw.MouthPoint.y : float.MaxValue;

            Material mat = GlowMaterial();
            foreach (MeshFilter mf in meshes)
            {
                Mesh m = mf.sharedMesh;
                Matrix4x4 k = toRoot * mf.transform.localToWorldMatrix;
                Vector3[] src = m.vertices;
                Vector3[] nrm = m.normals;
                bool hasN = nrm != null && nrm.Length == src.Length;
                Vector3 mls = mf.transform.lossyScale;
                float meshK = Mathf.Max(1e-5f, (Mathf.Abs(mls.x) + Mathf.Abs(mls.y) + Mathf.Abs(mls.z)) / 3f);
                float liftLocal = _lift * _sizeWorld / meshK;

                var o = new Overlay
                {
                    Base = new int[src.Length],
                    Frac = new Vector3[src.Length],
                    Colors = new Color32[src.Length],
                };
                var verts = new Vector3[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    verts[i] = hasN ? src[i] + nrm[i].normalized * liftLocal : src[i];
                    Vector3 p = k.MultiplyPoint3x4(src[i]);
                    Vector3 g = new Vector3((p.x - _min.x) / _cell.x, (p.y - _min.y) / _cell.y, (p.z - _min.z) / _cell.z);
                    int ix = Mathf.Clamp(Mathf.FloorToInt(g.x), 0, _nx - 2);
                    int iy = Mathf.Clamp(Mathf.FloorToInt(g.y), 0, _ny - 2);
                    int iz = Mathf.Clamp(Mathf.FloorToInt(g.z), 0, _nz - 2);
                    o.Base[i] = Idx(ix, iy, iz);
                    o.Frac[i] = new Vector3(Mathf.Clamp01(g.x - ix), Mathf.Clamp01(g.y - iy), Mathf.Clamp01(g.z - iz));

                    // Клетки вокруг вершины — металл.
                    for (int c = 0; c < 8; c++)
                    {
                        int j = Idx(ix + (c & 1), iy + ((c >> 1) & 1), iz + ((c >> 2) & 1));
                        _solid[j] = true;
                    }
                    // Касается ли содержимого.
                    Vector3 w = transform.TransformPoint(p);
                    bool touch = hasCav ? cav.Contains(w) : w.y < mouthY;
                    if (touch)
                    {
                        int j = Idx(Mathf.RoundToInt(g.x), Mathf.RoundToInt(g.y), Mathf.RoundToInt(g.z));
                        _contact[j] = true;
                    }
                }

                var mesh = new Mesh { name = m.name + " HeatGlow", hideFlags = HideFlags.DontSave };
                if (src.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = verts;
                if (hasN) mesh.normals = nrm;
                var tris = new List<int>();
                for (int s = 0; s < m.subMeshCount; s++) tris.AddRange(m.GetTriangles(s));
                mesh.SetTriangles(tris, 0);
                mesh.colors32 = o.Colors;
                mesh.bounds = m.bounds;
                mesh.MarkDynamic();

                var go = new GameObject("HeatGlow (auto)") { hideFlags = HideFlags.DontSave };
                go.layer = mf.gameObject.layer;
                go.transform.SetParent(mf.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.enabled = false;
                o.T = go.transform;
                o.Mesh = mesh;
                o.R = mr;
                _overlays.Add(o);
            }

            _contactCells = 0;
            for (int i = 0; i < _contact.Length; i++) if (_contact[i] && _solid[i]) _contactCells++;
            _ready = true;
            if (_log)
                Debug.Log($"[Жар ковша] «{name}»: сетка {_nx}×{_ny}×{_nz}, мешей {_overlays.Count}, " +
                          $"клеток у содержимого {_contactCells}" + (hasCav ? "" : " (нет Receive у Cookware — считаю всё ниже горловины)"), this);
        }

        private Renderer[] AutoSurfaces()
        {
            var list = new List<Renderer>();
            foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(true))
                if (PotBodyFilter.IsBody(r, transform) && r.GetComponentInParent<PotLidItem>() == null) list.Add(r);
            return list.ToArray();
        }

        private int Idx(int x, int y, int z) => (z * _ny + y) * _nx + x;

        private static Material GlowMaterial()
        {
            if (_mat != null) return _mat;
            Shader sh = Shader.Find("CatCooks/Heat Glow");
            if (sh != null)
            {
                _mat = new Material(sh) { name = "HeatGlow (auto)", hideFlags = HideFlags.DontSave };
                return _mat;
            }
            sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            _mat = new Material(sh) { name = "HeatGlow (auto, fallback)", hideFlags = HideFlags.DontSave };
            if (_mat.HasProperty("_Surface")) _mat.SetFloat("_Surface", 1f);
            if (_mat.HasProperty("_Blend")) _mat.SetFloat("_Blend", 0f);
            if (_mat.HasProperty("_SrcBlend")) _mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (_mat.HasProperty("_DstBlend")) _mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (_mat.HasProperty("_ZWrite")) _mat.SetFloat("_ZWrite", 0f);
            _mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 10;
            return _mat;
        }

        // ───────────────────────── КАДР ─────────────────────────

        // v17: для реакции игрока на жар (FireFlinch)
        public static readonly List<SurfaceHeatGlow> All = new List<SurfaceHeatGlow>();
        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() { All.Remove(this); }
        /// <summary>Самая горячая точка (мир). false — холодный.</summary>
        public bool TryHotPoint(out Vector3 p)
        {
            p = transform.position;
            if (!_ready || _hotIdx < 0 || _maxHeat <= 0.01f) return false;
            p = CellWorld(_hotIdx);
            return true;
        }
        /// <summary>Габарит металла (мир).</summary>
        public bool TryBounds(out Bounds b)
        {
            b = new Bounds(transform.position, Vector3.zero);
            bool any = false;
            if (_surfaces != null)
                foreach (Renderer r in _surfaces)
                {
                    if (r == null) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
            return any;
        }

        private void Update()
        {
            if (!_ready) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);

            HeatFromJets(dt);

            _updAcc += dt;
            float step = 1f / _updateRate;
            if (_updAcc >= step)
            {
                Diffuse(_updAcc);
                _updAcc = 0f;
                Repaint();
            }
            ServerFeedContents();
            HeatAir(dt);
        }

        private void HeatFromJets(float dt)
        {
            _jetsOn = 0;
            HeatJets.Prune();
            IReadOnlyList<IHeatJet> jets = HeatJets.All;
            for (int i = 0; i < jets.Count; i++)
            {
                IHeatJet j = jets[i];
                if (j == null || !j.JetActive) continue;
                if (!JetHit(j, out Vector3 point, out Vector3 normal, out float dist)) continue;
                _jetsOn++;
                float L = Mathf.Max(0.05f, j.JetLength);
                float k = j.JetPower * (1f - Mathf.SmoothStep(0.55f * L, L, dist));
                if (k <= 0f) continue;
                float rWorld = Mathf.Max(j.JetRadius * _spotScale, _minSpot * _sizeWorld);
                Deposit(transform.InverseTransformPoint(point), rWorld / _scaleK, _heatRate * k * dt);
                if (_flameLick) Lick(point, normal, j, k, dt);
            }
        }

        /// <summary>Куда упирается струя: первое препятствие на пути пламени — и это наш ковш.</summary>
        private bool JetHit(IHeatJet j, out Vector3 point, out Vector3 normal, out float dist)
        {
            point = normal = Vector3.zero; dist = 0f;
            Vector3 o = j.JetOrigin, d = j.JetDirection;
            if (d.sqrMagnitude < 1e-6f) return false;
            d.Normalize();
            float L = Mathf.Max(0.05f, j.JetLength);
            float r = Mathf.Max(0.005f, j.JetRadius * 0.4f);
            // Дальше от ковша, чем пламя + размер ковша, — даже не считаем.
            float far = L + _sizeWorld * 1.5f;
            if ((transform.position - o).sqrMagnitude > far * far) return false;

            int n = Physics.SphereCastNonAlloc(o, r, d, _castHits, L, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Collider bestC = null;
            RaycastHit bestH = default(RaycastHit);
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _castHits[i];
                Collider c = h.collider;
                if (c == null) continue;
                Transform root = j.JetRoot;
                if (root != null && c.transform.IsChildOf(root)) continue;
                if (c.GetComponentInParent<PlayerInteractor>() != null) continue;
                if (h.distance >= best) continue;
                best = h.distance; bestC = c; bestH = h;
            }
            if (bestC == null || !bestC.transform.IsChildOf(transform)) return false;
            if (best <= 0f)
            {
                // Сопло уже внутри коллайдера ковша — точка касания ближайшая.
                point = bestC.ClosestPoint(o);
                normal = -d;
                dist = 0f;
                return true;
            }
            point = bestH.point;
            normal = bestH.normal;
            dist = best;
            return true;
        }

        private void Deposit(Vector3 local, float radiusLocal, float amount)
        {
            if (amount <= 0f) return;
            float r = Mathf.Max(radiusLocal * (1f + 0.4f * _softSpot), Mathf.Min(_cell.x, Mathf.Min(_cell.y, _cell.z)) * 0.75f);
            int x0 = Mathf.Max(0, Mathf.FloorToInt((local.x - 2f * r - _min.x) / _cell.x));
            int x1 = Mathf.Min(_nx - 1, Mathf.CeilToInt((local.x + 2f * r - _min.x) / _cell.x));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((local.y - 2f * r - _min.y) / _cell.y));
            int y1 = Mathf.Min(_ny - 1, Mathf.CeilToInt((local.y + 2f * r - _min.y) / _cell.y));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((local.z - 2f * r - _min.z) / _cell.z));
            int z1 = Mathf.Min(_nz - 1, Mathf.CeilToInt((local.z + 2f * r - _min.z) / _cell.z));
            float inv = 1f / (r * r);
            bool wet = WetNow();
            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = Idx(x, y, z);
                if (!_solid[i]) continue;
                Vector3 c = new Vector3(_min.x + x * _cell.x, _min.y + y * _cell.y, _min.z + z * _cell.z);
                float d2 = (c - local).sqrMagnitude * inv;
                if (d2 > 4f) continue;
                float cap = wet && _contact[i] ? _wetMaxHeat : 1f;
                _h[i] = Mathf.Min(cap, _h[i] + amount * Mathf.Exp(-d2 * Mathf.Lerp(1.6f, 0.75f, _softSpot)));
            }
        }

        private bool WetNow() => _liquidCools && _lv != null && _lv.HasLiquid && _lv.CanPour;

        /// <summary>Тепло растекается по соседним клеткам металла и уходит в воздух.</summary>
        private void Diffuse(float dt)
        {
            // v14: ровнее — тепло расходится быстрее, подшагами (явная схема устойчива при шаге ≤ 0.5)
            float total = _spread * _spreadBoost * dt;
            int steps = Mathf.Clamp(Mathf.CeilToInt(total / 0.45f), 1, 8);
            for (int k = 0; k < steps; k++) DiffuseStep(dt / steps, total / steps, k == steps - 1);
            // выравнивание к средней по металлу
            if (_evenness > 0f && _meanHeat > 0.002f)
            {
                float e = Mathf.Clamp01(_evenness * dt);
                for (int i = 0; i < _h.Length; i++)
                    if (_solid[i]) _h[i] += (_meanHeat - _h[i]) * e * (_h[i] < _meanHeat ? 1f : 0.5f);
            }
        }

        private void DiffuseStep(float dt, float spreadA, bool last)
        {
            float a = Mathf.Clamp01(spreadA);
            float keep = Mathf.Exp(-_cool * dt);
            bool wet = WetNow();
            float max = 0f, sumAll = 0f;
            int hot = 0, solid = 0, hotIdx = -1;
            for (int z = 0; z < _nz; z++)
            for (int y = 0; y < _ny; y++)
            for (int x = 0; x < _nx; x++)
            {
                int i = Idx(x, y, z);
                if (!_solid[i]) { _tmp[i] = 0f; continue; }
                float sum = 0f; int cnt = 0;
                if (x > 0) { int j = i - 1; if (_solid[j]) { sum += _h[j]; cnt++; } }
                if (x < _nx - 1) { int j = i + 1; if (_solid[j]) { sum += _h[j]; cnt++; } }
                if (y > 0) { int j = i - _nx; if (_solid[j]) { sum += _h[j]; cnt++; } }
                if (y < _ny - 1) { int j = i + _nx; if (_solid[j]) { sum += _h[j]; cnt++; } }
                if (z > 0) { int j = i - _nx * _ny; if (_solid[j]) { sum += _h[j]; cnt++; } }
                if (z < _nz - 1) { int j = i + _nx * _ny; if (_solid[j]) { sum += _h[j]; cnt++; } }
                float v = _h[i];
                if (cnt > 0) v += a * (sum / cnt - v);
                v *= keep;
                if (wet && _contact[i]) v = Mathf.Min(v, _wetMaxHeat);
                if (v < 0.002f) v = 0f;
                _tmp[i] = v;
                solid++;
                sumAll += v;
                if (v > max) { max = v; hotIdx = i; }
                if (v > _glowFrom) hot++;
            }
            float[] t = _h; _h = _tmp; _tmp = t;
            if (!last) return;
            _maxHeat = max;
            _hotShare = solid > 0 ? hot / (float)solid : 0f;
            _meanHeat = solid > 0 ? sumAll / solid : 0f;
            _hotIdx = hotIdx;
        }

        // ───────────────────────── СОДЕРЖИМОЕ (сервер) ─────────────────────────

        private void ServerFeedContents()
        {
            if (_cw == null || !_cw.IsServerInitialized) return;
            float sum = 0f;
            bool wet = WetNow();
            float norm = wet ? _wetMaxHeat : 1f;
            for (int i = 0; i < _h.Length; i++)
                if (_contact[i] && _solid[i]) sum += _h[i];
            float e = _contactCells > 0 ? Mathf.Clamp01(sum / norm / (_contactCells * _fullCoverage)) : 0f;
            _contentFire = e > 0.02f ? Mathf.Lerp(20f, _contentFireC, e) : 0f;
            if (_contentFire <= 0f) { _lastSetC = -1f; return; }

            // Костёр (или ещё кто-то) тоже греет эту тару — берём большее.
            float other = _cw.ExternalTemperatureC;
            bool foreign = other > 0f && Mathf.Abs(other - _lastSetC) > 0.01f;
            float c = foreign ? Mathf.Max(other, _contentFire) : _contentFire;
            _cw.ServerSetExternalTemperature(c);
            _lastSetC = c;

            if (_log && Time.time >= _logAt)
            {
                _logAt = Time.time + 5f;
                Debug.Log($"[Жар ковша] «{name}»: металл до {MaxSurfaceC:0} °C, горячо {_hotShare * 100f:0}% площади, " +
                          $"внутрь {_contentFire:0} °C" + (_heat != null ? $", содержимое {_heat.TemperatureC:0} °C" : " (нет CookwareHeat — вода не греется!)"), this);
            }
        }

        // ───────────────────────── ВИД ─────────────────────────

        private void Repaint()
        {
            bool visible = _maxHeat > _glowFrom * 0.8f;
            for (int k = 0; k < _overlays.Count; k++)
            {
                Overlay o = _overlays[k];
                if (o.R == null) continue;
                if (o.R.enabled != visible) o.R.enabled = visible;
                if (!visible) continue;
                int sx = 1, sy = _nx, sz = _nx * _ny;
                for (int i = 0; i < o.Base.Length; i++)
                {
                    int b = o.Base[i];
                    Vector3 f = o.Frac[i];
                    float c00 = Mathf.Lerp(_h[b], _h[b + sx], f.x);
                    float c10 = Mathf.Lerp(_h[b + sy], _h[b + sy + sx], f.x);
                    float c01 = Mathf.Lerp(_h[b + sz], _h[b + sz + sx], f.x);
                    float c11 = Mathf.Lerp(_h[b + sz + sy], _h[b + sz + sy + sx], f.x);
                    float v = Mathf.Lerp(Mathf.Lerp(c00, c10, f.y), Mathf.Lerp(c01, c11, f.y), f.z);
                    o.Colors[i] = Ramp(v);
                }
                o.Mesh.colors32 = o.Colors;
            }
        }

        /// <summary>Цвет каления по шкале 0..1: тёмно-красный → красный → оранжевый → жёлтый.</summary>
        private Color32 Ramp(float v)
        {
            if (v <= _glowFrom) return new Color32(0, 0, 0, 0);
            float t = Mathf.InverseLerp(_glowFrom, 1f, v);
            // Мультяшные ступени: внутри ступени почти ровно, на границе — короткий переход.
            float bands = _toonBandsV14;
            if (bands > 0f)
            {
                float s = t * 4f, fl = Mathf.Floor(s), fr = s - fl;
                float edge = Mathf.Lerp(0.5f, 0.12f, bands);
                fr = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - edge, 1f, fr));
                float q = Mathf.Min(1f, (fl + fr) / 4f + 0.125f);          // середина ступени
                t = Mathf.Lerp(t, q, bands);
            }
            Color c;
            if (t < 0.33f) c = Color.Lerp(_dull, _red, t / 0.33f);
            else if (t < 0.7f) c = Color.Lerp(_red, _orange, (t - 0.33f) / 0.37f);
            else c = Color.Lerp(_orange, _yellow, (t - 0.7f) / 0.3f);
            // Пересвет: шейдер Heat Glow умножает на Boost сам; цвет вершины — в пределах 0..1.
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_glowFrom, _glowFrom + 0.08f, v));
            c.a *= fade;
            return c;
        }

        private void LateUpdate()
        {
            if (_mat != null && _mat.HasProperty("_Boost")) _mat.SetFloat("_Boost", _boost);
        }

        private void Lick(Vector3 point, Vector3 normal, IHeatJet j, float k, float dt)
        {
            if (_lick == null) _lick = CreateLick();
            if (_lick == null) return;
            _lickAcc += _lickPerSecond * k * dt;
            int n = Mathf.Min(16, Mathf.FloorToInt(_lickAcc));
            if (n <= 0) return;
            _lickAcc -= n;
            Vector3 nrm = normal.sqrMagnitude > 1e-6f ? normal.normalized : -j.JetDirection;
            Vector3 along = Vector3.ProjectOnPlane(j.JetDirection, nrm);
            float size = Mathf.Max(0.03f, j.JetRadius * 0.9f);
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                // Пламя расплющивается о металл и растекается во все стороны, больше — по ходу струи.
                Vector3 t = Vector3.ProjectOnPlane(Random.onUnitSphere, nrm).normalized;
                Vector3 v = (t * Random.Range(0.6f, 1.6f) + along * 1.2f) * Mathf.Max(0.6f, j.JetLength * 2.2f);
                v += Vector3.up * Random.Range(0.2f, 0.8f) * Mathf.Max(0.5f, j.JetLength);
                ep.position = point + nrm * size * 0.3f + t * size * 0.4f;
                ep.velocity = v;
                float c = Random.value;
                ep.startColor = c < 0.25f ? new Color(0.5f, 0.75f, 1f, 0.9f)
                              : c < 0.7f ? new Color(1f, 0.55f, 0.12f, 0.95f)
                                         : new Color(1f, 0.85f, 0.3f, 0.9f);
                ep.startSize = size * Random.Range(0.7f, 1.5f);
                ep.startLifetime = Random.Range(0.12f, 0.3f);
                if (_lickStylized)
                {
                    // v17: языки-капли (шейдер Stylized Fire): R — зерно, G — яркость, A — «износ»
                    ep.startColor = new Color(Random.value, Random.Range(0.6f, 1f), 0f, 1f);
                    ep.startSize = size * Random.Range(1.1f, 2f);
                    ep.startLifetime = Random.Range(0.2f, 0.35f);
                    ep.rotation = Random.Range(-25f, 25f);
                }
                _lick.Emit(ep, 1);
            }
        }

        private ParticleSystem CreateLick()
        {
            var go = new GameObject("HeatLick (auto)") { hideFlags = HideFlags.DontSave };
            go.SetActive(false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.gravityModifier = -0.25f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.4f));
            var vol = ps.limitVelocityOverLifetime;
            vol.enabled = true;
            vol.limit = 2f;
            vol.dampen = 0.25f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.6f, 0.4f), 1f) },
                      new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            // v17: языки пламени по металлу — как огонёк Zelda (шейдер Stylized Fire), без квадратиков
            Shader sf = Shader.Find("CatCooks/Stylized Fire");
            if (sf != null)
            {
                _lickStylized = true;
                r.sharedMaterial = new Material(sf) { name = "HeatLick Fire (auto)", hideFlags = HideFlags.DontSave };
                var g2 = new Gradient();
                g2.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                           new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
                col.color = g2;
                go.SetActive(true);
                ps.Play();
                return ps;
            }
            Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (s != null)
            {
                var m = new Material(s) { name = "HeatLick (auto)", hideFlags = HideFlags.DontSave };
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
                if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);
                if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                Texture2D tex = CCRuntimeParticles.SharedMaterial != null ? CCRuntimeParticles.SharedMaterial.mainTexture as Texture2D : null;
                if (tex != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                r.sharedMaterial = m;
            }
            go.SetActive(true);
            ps.Play();
            return ps;
        }

        // ───────────────────────── v14: МАРЕВО И ЛЁГКИЙ ПАР ─────────────────────────

        private Vector3 CellWorld(int i)
        {
            int x = i % _nx, y = (i / _nx) % _ny, z = i / (_nx * _ny);
            return transform.TransformPoint(new Vector3(_min.x + x * _cell.x, _min.y + y * _cell.y, _min.z + z * _cell.z));
        }

        private void HeatAir(float dt)
        {
            if (_hotIdx < 0 || _maxHeat <= 0.01f) return;
            Vector3 hot = CellWorld(_hotIdx);
            if (_haze && _maxHeat > _hazeFrom)
            {
                float k = Mathf.InverseLerp(_hazeFrom, 0.75f, _maxHeat) * _hazeStrength;
                float w = _sizeWorld * Mathf.Lerp(0.35f, 0.75f, Mathf.Clamp01(_hotShare * 3f));
                CCHeatFx.Haze(hot - Vector3.up * w * 0.1f, w, w * 1.8f, k);
                // весь ковш прогрелся — марево и над горловиной
                if (_meanHeat > _hazeFrom * 0.8f && _cw != null)
                {
                    float km = Mathf.InverseLerp(_hazeFrom * 0.8f, 0.5f, _meanHeat) * _hazeStrength;
                    CCHeatFx.Haze(_cw.MouthPoint, _sizeWorld * 0.8f, _sizeWorld * 1.5f, km * 0.8f);
                }
            }
            if (_heatWisps && _maxHeat > _wispsFrom)
            {
                float k = Mathf.InverseLerp(_wispsFrom, 1f, _maxHeat);
                _wispAcc += dt * (1.2f + 3f * k);
                if (_wispAcc >= 1f)
                {
                    _wispAcc -= 1f;
                    float s = _sizeWorld * Random.Range(0.12f, 0.22f);
                    Color c = _wispColor; c.a *= 0.5f + k;
                    CCHeatFx.Wisp(hot + Random.insideUnitSphere * _sizeWorld * 0.08f + Vector3.up * s * 0.3f,
                                  new Vector3(Random.Range(-0.05f, 0.05f), Random.Range(0.25f, 0.5f), Random.Range(-0.05f, 0.05f)) * Mathf.Sqrt(_sizeWorld / 0.5f),
                                  s, c, Random.Range(1f, 1.6f));
                }
            }
        }

        // ───────────────────────── ОТЛАДКА ─────────────────────────

        private void OnGUI()
        {
            if (!_debugHud || !_ready) return;
            if (_jetsOn == 0 && _maxHeat < _glowFrom) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 top = transform.position + Vector3.up * _sizeWorld * 0.6f;
            Vector3 sp = cam.WorldToScreenPoint(top);
            if (sp.z <= 0f || sp.z > _sizeWorld * 12f + 15f) return;
            string t = $"металл {MaxSurfaceC:0} °C ({_hotShare * 100f:0}% горячо)  ·  внутрь {_contentFire:0} °C" +
                       (_heat != null ? $"  ·  содержимое {_heat.TemperatureC:0} °C{(_heat.IsBoiling ? " КИПИТ" : "")}" : "") +
                       (_jetsOn > 0 ? $"  ·  струй {_jetsOn}" : "  ·  остывает");
            var st = new GUIStyle(GUI.skin.box) { fontSize = 13 };
            st.normal.textColor = Color.Lerp(new Color(1f, 0.8f, 0.4f), new Color(1f, 0.3f, 0.2f), _maxHeat);
            Vector2 size = st.CalcSize(new GUIContent(t));
            GUI.Box(new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y, size.x + 8f, size.y + 4f), t, st);
        }

        private void OnDrawGizmosSelected()
        {
            if (!_ready || _h == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            for (int z = 0; z < _nz; z++)
            for (int y = 0; y < _ny; y++)
            for (int x = 0; x < _nx; x++)
            {
                int i = Idx(x, y, z);
                if (!_solid[i]) continue;
                Vector3 c = new Vector3(_min.x + x * _cell.x, _min.y + y * _cell.y, _min.z + z * _cell.z);
                if (_h[i] > 0.05f) { Gizmos.color = Color.Lerp(Color.red, Color.yellow, _h[i]); Gizmos.DrawCube(c, _cell * 0.5f); }
                else if (_contact[i]) { Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.35f); Gizmos.DrawWireCube(c, _cell * 0.3f); }
            }
        }
    }
}
