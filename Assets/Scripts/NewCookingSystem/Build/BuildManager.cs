using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>Одна стройка: где стоит и на каком этапе. Синхронизируется всем (SyncList).</summary>
    public struct BuildSiteState
    {
        public int Id;
        public byte Recipe;          // 0 — форма для бисквита, 1 — противень для макарон (v23.52)
        public Vector3 Pos;          // точка на полу (центр низа формы)
        public float Yaw;
        public byte Boards;          // уложено досок
        public ushort Nails;         // битовая маска: где стоят гвозди
        public uint Hits;            // по 2 бита на гвоздь: сколько раз ударили (0..3)
        public byte Stage;           // 0 доски, 4 кольца (только противень), 1 гвозди, 2 молоток, 3 готово
        public byte Rings;           // v23.52: вставлено колец (обрубков трубы)
    }

    /// <summary>
    /// v22: ПРОСТАЯ СТРОЙКА. v23.52: две постройки.
    ///
    ///   [1] ФОРМА ДЛЯ БИСКВИТА (опалубка): 10 досок → 10 гвоздей → молоток → опалубка.
    ///   [2] ПРОТИВЕНЬ ДЛЯ МАКАРОН: 10 досок (дно и борта) → КОЛЬЦА (обрубки трубы) → 10 гвоздей → молоток → противень с кольцами.
    ///       Кольца: трубу (меню «Стройка → 4») режешь пилой или бензопилой — каждый распил отваливает кольцо.
    ///       Кольцо бросаешь в шаблон — оно по одному встаёт на светящееся место.
    ///
    ///   1. C — меню построек, 1 / 2 — выбрать. Перед тобой призрачный шаблон. Колесо мыши — повернуть, ЛКМ — поставить,
    ///      ПКМ/Esc — отмена. Шаблон без физики, сквозь него ходят.
    ///   2. ДОСКИ: бросай в шаблон доски от поддонов (те же, что для костра; стопка — тоже). Каждая доска по одной влетает на место.
    ///   3. КОЛЬЦА (только противень): бросай кольца из трубы — встают по одному в свои гнёзда.
    ///   4. ГВОЗДИ: на каркасе светятся точки. Коробка гвоздей (NailBoxItem), ЛКМ по точке — гвоздь встаёт, торчит.
    ///   5. МОЛОТОК (HammerItem): ЛКМ по торчащему гвоздю — 3 удара на гвоздь.
    ///   6. Все гвозди забиты — шаблон исчезает, появляется настоящая форма (префаб).
    ///
    /// Стоит в сцене одним объектом (меню CatCooks → Стройка → 1). Префабы опалубки и противня меню находит само.
    /// Сеть: состояние строек — SyncList, видно всем и опоздавшим.
    /// </summary>
    public sealed class BuildManager : NetworkBehaviour
    {
        public static BuildManager Instance { get; private set; }

        public const byte RecipeFormwork = 0, RecipeMacaronTray = 1;
        public const int RecipeCount = 2;
        public const byte StageBoards = 0, StageNails = 1, StageHammer = 2, StageDone = 3, StageRings = 4;

        [Header("Постройки")]
        [Tooltip("Префаб опалубки (NetworkObject + Cookware + BakeTray) — появляется, когда форма достроена. " +
                 "Должен быть в Spawnable Prefabs FishNet")]
        [SerializeField] private NetworkObject _formworkPrefab;
        [Tooltip("Откуда взять вид шаблона, если префаба нет (опалубка в сцене). Пусто — найдёт сам")]
        [SerializeField] private GameObject _formworkLook;
        [Tooltip("v23.52: префаб противня с кольцами (NetworkObject + MacaronTray). Должен быть в Spawnable Prefabs FishNet")]
        [SerializeField] private NetworkObject _macaronTrayPrefab;
        [Tooltip("v23.52: откуда взять вид шаблона противня, если префаба нет (противень в сцене). Пусто — найдёт сам")]
        [SerializeField] private GameObject _macaronTrayLook;

        [Header("Правила")]
        [SerializeField] private KeyCode _menuKey = KeyCode.C;
        [Range(1, 20)][SerializeField] private int _boardsNeeded = 10;
        [Range(1, 3)][SerializeField] private int _hitsPerNail = 3;
        [Tooltip("Насколько далеко от игрока можно ставить шаблон, м")]
        [Range(1f, 15f)][SerializeField] private float _placeRange = 6f;
        [Tooltip("Дальность «тыкнуть» в точку гвоздя, м")]
        [Range(0.5f, 6f)][SerializeField] private float _nailReach = 3f;

        [Header("Вид")]
        [SerializeField] private Color _ghostColor = new Color(0.45f, 0.8f, 1f, 0.28f);
        [SerializeField] private Color _pointColor = new Color(1f, 0.85f, 0.25f, 1f);
        [SerializeField] private bool _log = true;

        private readonly SyncList<BuildSiteState> _sites = new SyncList<BuildSiteState>();
        private int _nextId = 1;
        private float _absorbTick;

        // ── шаблоны (меряются один раз по префабу/образцу) ──
        private sealed class Shape
        {
            public bool Ready;
            public string Title;
            public readonly List<Mesh> Meshes = new List<Mesh>();
            public readonly List<Matrix4x4> Mats = new List<Matrix4x4>();
            public Material Wood, Nail, Look;
            public Bounds Local;                    // габарит формы в осях корня
            public Vector3[] Rings = new Vector3[0];  // центры колец на дне (оси формы)
            public float RingRi, RingRo, RingH;
            public Mesh RingMesh;
        }
        private readonly Shape[] _shapes = { new Shape(), new Shape() };

        // ── клиент ──
        private readonly Dictionary<int, BuildSiteView> _views = new Dictionary<int, BuildSiteView>();
        private bool _menuOpen, _placing;
        private byte _placeRecipe;
        private BuildSiteView _preview;
        private byte _previewRecipe = 255;
        private float _previewYaw, _hammerCd;
        private Vector3 _previewPos;
        private bool _previewOk;

        public int BoardsNeeded => _boardsNeeded;
        public int HitsPerNail => _hitsPerNail;
        public Color GhostColor => _ghostColor;
        public Color PointColor => _pointColor;

        private void Awake() { Instance = this; }
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var v in _views.Values) if (v != null) Destroy(v.gameObject);
            if (_preview != null) Destroy(_preview.gameObject);
        }

        // ───────────────────────── шаблоны ─────────────────────────

        private static int R(int recipe) => recipe == RecipeMacaronTray ? 1 : 0;

        public static string TitleOf(int recipe) => recipe == RecipeMacaronTray ? "Противень для макарон" : "Форма для бисквита";

        private NetworkObject PrefabOf(int recipe) => recipe == RecipeMacaronTray ? _macaronTrayPrefab : _formworkPrefab;

        private Shape EnsureShape(int recipe)
        {
            Shape sh = _shapes[R(recipe)];
            if (sh.Ready) return sh;
            sh.Ready = true;
            sh.Title = TitleOf(recipe);
            bool tray = recipe == RecipeMacaronTray;
            NetworkObject prefab = PrefabOf(recipe);
            GameObject src = prefab != null ? prefab.gameObject : (tray ? _macaronTrayLook : _formworkLook);
            if (src == null)
            {
                if (tray) { MacaronTray mt = FindAnyObjectByType<MacaronTray>(); if (mt != null) src = mt.gameObject; }
                else { BakeTray bt = FindAnyObjectByType<BakeTray>(); if (bt != null) src = bt.gameObject; }
            }
            bool any = false;
            if (src != null)
            {
                Matrix4x4 toRoot = src.transform.worldToLocalMatrix;
                foreach (MeshFilter mf in src.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf == null || mf.sharedMesh == null) continue;
                    if (mf.GetComponentInParent<CookwareLiquidVisual>() != null) continue;
                    if (mf.gameObject.name == "Fill" || mf.gameObject.name.EndsWith("(auto)")) continue;
                    MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null) continue;
                    Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                    sh.Meshes.Add(mf.sharedMesh);
                    sh.Mats.Add(m);
                    Material[] ms = mr.sharedMaterials;
                    if (sh.Look == null && ms.Length > 0) sh.Look = ms[0];
                    if (!tray)
                    {
                        if (sh.Wood == null && ms.Length > 0) sh.Wood = ms[0];
                        if (sh.Nail == null && ms.Length > 1) sh.Nail = ms[1];
                    }
                }
                // масштаб (и у префаба — поворот) корня: шаблон в метрах мира, повёрнут только по Y стройки
                Matrix4x4 rootM = prefab != null
                    ? Matrix4x4.TRS(Vector3.zero, src.transform.rotation, src.transform.lossyScale)
                    : Matrix4x4.Scale(src.transform.lossyScale);
                for (int i = 0; i < sh.Mats.Count; i++)
                {
                    sh.Mats[i] = rootM * sh.Mats[i];
                    Bounds b = sh.Meshes[i].bounds;
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 p = sh.Mats[i].MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1)));
                        if (!any) { sh.Local = new Bounds(p, Vector3.zero); any = true; } else sh.Local.Encapsulate(p);
                    }
                }
                // v23.52: кольца противня — по самому противню (центры колец и радиус)
                if (tray)
                {
                    MacaronTray mt = src.GetComponent<MacaronTray>();
                    if (mt != null && mt.RingCount > 0)
                    {
                        float sc = Mathf.Abs(src.transform.lossyScale.x);
                        sh.RingRi = mt.RingRadius * sc;
                        sh.RingRo = sh.RingRi * 1.14f;
                        sh.RingH = sh.RingRi * 0.32f;
                        sh.Rings = new Vector3[mt.RingCount];
                        for (int i = 0; i < mt.RingCount; i++)
                        {
                            Vector3 lp = toRoot.MultiplyPoint3x4(mt.RingWorld(i));
                            sh.Rings[i] = rootM.MultiplyPoint3x4(lp);
                        }
                    }
                }
            }
            if (!any)
            {
                sh.Local = tray ? new Bounds(new Vector3(0f, 0.06f, 0f), new Vector3(1.6f, 0.12f, 0.8f))
                                : new Bounds(new Vector3(0f, 0.045f, 0f), new Vector3(0.6f, 0.09f, 0.4f));
                if (_log) Debug.LogWarning($"[Стройка] {sh.Title}: нет префаба и образца в сцене — шаблон по размерам по умолчанию", this);
            }
            if (tray && sh.Rings.Length == 0)
            {
                // нет противня-образца — 6 × 2 кольца по габариту
                Bounds b = sh.Local;
                int nx = 6, nz = 2;
                float cx = b.size.x / nx, cz = b.size.z / nz;
                sh.RingRi = Mathf.Min(cx, cz) * 0.5f * 0.7f; sh.RingRo = sh.RingRi * 1.14f; sh.RingH = sh.RingRi * 0.32f;
                sh.Rings = new Vector3[nx * nz];
                for (int ix = 0; ix < nx; ix++)
                    for (int iz = 0; iz < nz; iz++)
                        sh.Rings[ix * nz + iz] = new Vector3(b.min.x + cx * (ix + 0.5f), b.min.y, b.min.z + cz * (iz + 0.5f));
            }
            if (tray)
            {
                // кольца стоят на досках дна
                float t = BoardThickness(sh.Local);
                for (int i = 0; i < sh.Rings.Length; i++) sh.Rings[i].y = sh.Local.min.y + t;
                sh.RingMesh = MacaronMesh.Tube(sh.RingRi, sh.RingRo, sh.RingH, PipeItem.PipeColor, PipeItem.CutColor, 32);
                sh.RingMesh.hideFlags = HideFlags.DontSave;
            }
            if (sh.Wood == null) sh.Wood = MakeMat(new Color(0.72f, 0.55f, 0.34f));
            if (sh.Nail == null) sh.Nail = MakeMat(new Color(0.55f, 0.57f, 0.6f));
            if (sh.Look == null) sh.Look = sh.Wood;
            if (_log) Debug.Log($"[Стройка] {sh.Title}: {sh.Local.size.x:0.00}×{sh.Local.size.z:0.00}×{sh.Local.size.y:0.00} м" +
                                (tray ? $", колец {sh.Rings.Length} Ø{sh.RingRo * 2f:0.00} м" : "") +
                                $", вид — {(src != null ? src.name : "по умолчанию")}, префаб {(prefab != null ? "есть" : "НЕТ (в конце форма не появится)")}", this);
            return sh;
        }

        private static float BoardThickness(Bounds b) => Mathf.Clamp(b.size.y * 0.22f, 0.006f, b.size.z * 0.08f);

        private static Material MakeMat(Color c)
        {
            Shader sh = Shader.Find("CatCooks/Toon Props");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(sh) { hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            return m;
        }

        public Bounds LocalBounds(int recipe) => EnsureShape(recipe).Local;
        public int LookCount(int recipe) => EnsureShape(recipe).Meshes.Count;
        public Mesh LookMesh(int recipe, int i) => EnsureShape(recipe).Meshes[i];
        public Matrix4x4 LookMatrix(int recipe, int i) => EnsureShape(recipe).Mats[i];
        public Material WoodMat(int recipe) => EnsureShape(recipe).Wood;
        public Material NailMat(int recipe) => EnsureShape(recipe).Nail;
        /// <summary>v23.52: материал колец (как у противня-образца — цвет в вершинах).</summary>
        public Material RingMat(int recipe) => EnsureShape(recipe).Look;
        public int RingsNeeded(int recipe) => recipe == RecipeMacaronTray ? EnsureShape(recipe).Rings.Length : 0;
        public Mesh RingMesh(int recipe) => EnsureShape(recipe).RingMesh;
        public Vector3 RingSlot(int recipe, int i) { Shape sh = EnsureShape(recipe); return i >= 0 && i < sh.Rings.Length ? sh.Rings[i] : sh.Local.center; }
        public float RingHeight(int recipe) => EnsureShape(recipe).RingH;

        /// <summary>Мир ← оси стройки: корень формы стоит так, чтобы низ габарита был на полу в точке pos.</summary>
        public Matrix4x4 SiteMatrix(int recipe, Vector3 pos, float yaw)
        {
            Bounds l = EnsureShape(recipe).Local;
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            Vector3 root = pos - r * new Vector3(l.center.x, l.min.y, l.center.z);
            return Matrix4x4.TRS(root, r, Vector3.one);
        }

        // ───────────────────────── геометрия каркаса ─────────────────────────

        /// <summary>Доска №i: центр, размер (в осях формы). Сначала дно вдоль длины, потом 4 борта.</summary>
        public void BoardSlot(int recipe, int i, out Vector3 center, out Vector3 size)
        {
            Bounds b = EnsureShape(recipe).Local;
            float L = b.size.x, W = b.size.z, H = b.size.y;
            float t = BoardThickness(b);
            int bottom = Mathf.Max(1, _boardsNeeded - 4);
            if (i < bottom)
            {
                float w = W / bottom;
                center = new Vector3(b.center.x, b.min.y + t * 0.5f, b.min.z + w * (i + 0.5f));
                size = new Vector3(L, t, w * 0.96f);
                return;
            }
            int k = i - bottom;
            // противень — низкие бортики (как у листа), форма для бисквита — во всю высоту
            float h = recipe == RecipeMacaronTray ? Mathf.Max(t, Mathf.Min(H - t, EnsureShape(recipe).RingH * 0.8f + t)) : H - t;
            switch (k)
            {
                case 0: center = new Vector3(b.center.x, b.min.y + t + h * 0.5f, b.min.z + t * 0.5f); size = new Vector3(L, h, t); break;
                case 1: center = new Vector3(b.center.x, b.min.y + t + h * 0.5f, b.max.z - t * 0.5f); size = new Vector3(L, h, t); break;
                case 2: center = new Vector3(b.min.x + t * 0.5f, b.min.y + t + h * 0.5f, b.center.z); size = new Vector3(t, h, W - t * 2f); break;
                default: center = new Vector3(b.max.x - t * 0.5f, b.min.y + t + h * 0.5f, b.center.z); size = new Vector3(t, h, W - t * 2f); break;
            }
        }

        public const int NailCount = 10;

        /// <summary>
        /// Гвоздь №i: точка на наружной стороне борта (оси формы) и направление забивания (внутрь).
        /// 0..7 — по два в каждом углу (один в торец, один в длинный борт), 8..9 — середина длинных бортов, в дно.
        /// </summary>
        public void NailSlot(int recipe, int i, out Vector3 p, out Vector3 dir)
        {
            Bounds b = EnsureShape(recipe).Local;
            float t = BoardThickness(b);
            Vector3 bc, bs;
            BoardSlot(recipe, _boardsNeeded - 4, out bc, out bs);              // борт — по его высоте
            float y = recipe == RecipeMacaronTray ? bc.y : b.min.y + b.size.y * 0.62f;
            if (i < 8)
            {
                int corner = i / 2;                                  // (−x,−z) (+x,−z) (+x,+z) (−x,+z)
                bool px = corner == 1 || corner == 2, pz = corner >= 2;
                float x = px ? b.max.x : b.min.x, z = pz ? b.max.z : b.min.z;
                float sx = px ? -1f : 1f, sz = pz ? -1f : 1f;        // внутрь
                if (i % 2 == 0) { p = new Vector3(x, y, z + sz * t * 0.5f); dir = new Vector3(sx, 0f, 0f); }
                else { p = new Vector3(x + sx * t * 0.5f, y, z); dir = new Vector3(0f, 0f, sz); }
                return;
            }
            bool far = i == 9;
            p = new Vector3(b.center.x, b.min.y + t * 0.5f, far ? b.max.z : b.min.z);
            dir = new Vector3(0f, 0f, far ? -1f : 1f);
        }

        public float NailLength(int recipe)
        {
            Bounds b = EnsureShape(recipe).Local;
            float h = recipe == RecipeMacaronTray ? BoardThickness(b) * 4f : b.size.y;
            return Mathf.Clamp(h * 0.55f, 0.01f, 0.12f);
        }

        // ───────────────────────── сеть ─────────────────────────

        public override void OnStartClient() { base.OnStartClient(); EnsureShape(0); EnsureShape(1); }
        public override void OnStartServer() { base.OnStartServer(); EnsureShape(0); EnsureShape(1); }

        [ServerRpc(RequireOwnership = false)]
        private void CmdPlace(byte recipe, Vector3 pos, float yaw)
        {
            if (recipe >= RecipeCount) return;
            var s = new BuildSiteState { Id = _nextId++, Recipe = recipe, Pos = pos, Yaw = yaw, Stage = StageBoards };
            _sites.Add(s);
            if (_log) Debug.Log($"[Стройка] шаблон «{TitleOf(recipe)}» поставлен (№{s.Id}) — неси {_boardsNeeded} досок", this);
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdCancel(int id)
        {
            int i = IndexOf(id);
            if (i >= 0 && _sites[i].Boards == 0) { _sites.RemoveAt(i); if (_log) Debug.Log($"[Стройка] шаблон №{id} убран", this); }
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdHit(int id, int nail) { ServerHitNail(id, nail); }

        private int IndexOf(int id)
        {
            for (int i = 0; i < _sites.Count; i++) if (_sites[i].Id == id) return i;
            return -1;
        }

        /// <summary>Сервер: поставить гвоздь (коробка гвоздей).</summary>
        [Server]
        public bool ServerPlaceNail(int id, int nail)
        {
            int i = IndexOf(id);
            if (i < 0 || nail < 0 || nail >= NailCount) return false;
            BuildSiteState s = _sites[i];
            if (s.Stage != StageNails || (s.Nails & (1 << nail)) != 0) return false;
            s.Nails = (ushort)(s.Nails | (1 << nail));
            int placed = 0;
            for (int k = 0; k < NailCount; k++) if ((s.Nails & (1 << k)) != 0) placed++;
            if (placed >= NailCount)
            {
                s.Stage = StageHammer;
                if (_log) Debug.Log($"[Стройка] №{id}: все гвозди стоят — бери молоток и забивай ({_hitsPerNail} удара на гвоздь)", this);
            }
            _sites[i] = s;
            return true;
        }

        [Server]
        private void ServerHitNail(int id, int nail)
        {
            int i = IndexOf(id);
            if (i < 0 || nail < 0 || nail >= NailCount) return;
            BuildSiteState s = _sites[i];
            if (s.Stage != StageHammer && s.Stage != StageNails) return;
            if ((s.Nails & (1 << nail)) == 0) return;
            int h = HitsOf(s, nail);
            if (h >= _hitsPerNail) return;
            h++;
            s.Hits = (s.Hits & ~(3u << (nail * 2))) | ((uint)h << (nail * 2));
            bool all = s.Stage == StageHammer;
            for (int k = 0; k < NailCount && all; k++) if (HitsOf(s, k) < _hitsPerNail) all = false;
            if (all) s.Stage = StageDone;
            _sites[i] = s;
            if (all) ServerFinish(i);
        }

        public static int HitsOf(BuildSiteState s, int nail) => (int)((s.Hits >> (nail * 2)) & 3u);

        [Server]
        private void ServerFinish(int i)
        {
            BuildSiteState s = _sites[i];
            NetworkObject prefab = PrefabOf(s.Recipe);
            if (prefab != null)
            {
                Matrix4x4 m = SiteMatrix(s.Recipe, s.Pos, s.Yaw);
                Vector3 pos = m.GetColumn(3);
                Quaternion rot = Quaternion.Euler(0f, s.Yaw, 0f) * prefab.transform.rotation;   // как у шаблона
                NetworkObject nob = Instantiate(prefab, pos + Vector3.up * 0.005f, rot);
                base.Spawn(nob.gameObject);
                if (_log) Debug.Log($"[Стройка] №{s.Id}: «{TitleOf(s.Recipe)}» готова" + (s.Recipe == RecipeMacaronTray ? " — отсаживай тесто шприцем в кольца" : " — заливай тесто"), this);
            }
            else if (_log) Debug.LogWarning($"[Стройка] «{TitleOf(s.Recipe)}» достроена, но префаб пуст — форма не появилась. Сделай её префабом и нажми меню CatCooks → Стройка → 1", this);
            _sites.RemoveAt(i);
        }

        // ───────────────────────── сервер: доски и кольца ─────────────────────────

        private readonly Collider[] _overlap = new Collider[48];

        private void FixedUpdate()
        {
            if (!base.IsServerInitialized || _sites.Count == 0) return;
            _absorbTick -= Time.fixedDeltaTime;
            if (_absorbTick > 0f) return;
            _absorbTick = 0.2f;
            for (int i = 0; i < _sites.Count; i++)
            {
                BuildSiteState s = _sites[i];
                if (s.Stage != StageBoards && s.Stage != StageRings) continue;
                Bounds l = EnsureShape(s.Recipe).Local;
                Matrix4x4 m = SiteMatrix(s.Recipe, s.Pos, s.Yaw);
                Vector3 c = m.MultiplyPoint3x4(l.center + Vector3.up * l.size.y * 0.5f);
                Vector3 half = new Vector3(l.extents.x + 0.05f, l.extents.y + 0.35f, l.extents.z + 0.05f);
                int n = Physics.OverlapBoxNonAlloc(c, half, _overlap, Quaternion.Euler(0f, s.Yaw, 0f), ~0, QueryTriggerInteraction.Ignore);
                bool changed = s.Stage == StageBoards ? AbsorbBoards(ref s, n) : AbsorbRings(ref s, n);
                if (!changed) continue;
                _sites[i] = s;
            }
        }

        private bool AbsorbBoards(ref BuildSiteState s, int n)
        {
            bool changed = false;
            for (int k = 0; k < n && s.Boards < _boardsNeeded; k++)
            {
                Collider col = _overlap[k];
                if (col == null) continue;
                BonfireFuel fuel = col.GetComponentInParent<BonfireFuel>();
                if (fuel == null || fuel.Consumed) continue;
                int have = Mathf.Max(1, fuel.Planks);
                int take = Mathf.Min(have, _boardsNeeded - s.Boards);
                s.Boards = (byte)(s.Boards + take);
                changed = true;
                if (take < have) fuel.ServerSetPlanks(have - take);
                else
                {
                    fuel.Consumed = true;
                    NetworkObject nob = fuel.GetComponentInParent<NetworkObject>();
                    if (nob != null && nob.IsSpawned) nob.Despawn(); else Destroy(fuel.gameObject);
                }
            }
            if (!changed) return false;
            if (s.Boards >= _boardsNeeded)
            {
                if (s.Recipe == RecipeMacaronTray && RingsNeeded(s.Recipe) > 0)
                {
                    s.Stage = StageRings;
                    if (_log) Debug.Log($"[Стройка] №{s.Id}: каркас противня собран — теперь КОЛЬЦА: режь трубу пилой и бросай кольца в шаблон ({RingsNeeded(s.Recipe)} шт.)", this);
                }
                else
                {
                    s.Stage = StageNails;
                    if (_log) Debug.Log($"[Стройка] №{s.Id}: каркас собран — теперь гвозди: коробка гвоздей, ЛКМ по светящимся точкам", this);
                }
            }
            else if (_log) Debug.Log($"[Стройка] №{s.Id}: досок {s.Boards}/{_boardsNeeded}", this);
            return true;
        }

        private bool AbsorbRings(ref BuildSiteState s, int n)
        {
            int need = RingsNeeded(s.Recipe);
            bool changed = false;
            for (int k = 0; k < n && s.Rings < need; k++)
            {
                Collider col = _overlap[k];
                if (col == null) continue;
                PipeRing ring = col.GetComponentInParent<PipeRing>();
                if (ring == null || ring.Consumed || ring.IsHeldNow) continue;
                ring.Consumed = true;
                s.Rings++;
                changed = true;
                NetworkObject nob = ring.GetComponentInParent<NetworkObject>();
                if (nob != null && nob.IsSpawned) nob.Despawn(); else Destroy(ring.gameObject);
            }
            if (!changed) return false;
            if (s.Rings >= need)
            {
                s.Stage = StageNails;
                if (_log) Debug.Log($"[Стройка] №{s.Id}: все {need} колец на месте — теперь гвозди: коробка гвоздей, ЛКМ по светящимся точкам", this);
            }
            else if (_log) Debug.Log($"[Стройка] №{s.Id}: колец {s.Rings}/{need}", this);
            return true;
        }

        // ───────────────────────── клиент ─────────────────────────

        private void Update()
        {
            SyncViews();
            if (!base.IsClientInitialized) return;
            InputMenu();
            if (_placing) UpdatePreview();
            HammerInput();
        }

        private void SyncViews()
        {
            var alive = new HashSet<int>();
            for (int i = 0; i < _sites.Count; i++)
            {
                BuildSiteState s = _sites[i];
                alive.Add(s.Id);
                BuildSiteView v;
                if (!_views.TryGetValue(s.Id, out v) || v == null)
                {
                    v = BuildSiteView.Create(this, s.Recipe, false);
                    _views[s.Id] = v;
                }
                v.Apply(s);
            }
            if (_views.Count == alive.Count) return;
            var dead = new List<int>();
            foreach (var kv in _views) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (int id in dead)
            {
                if (_views[id] != null) _views[id].FinishAndDestroy();
                _views.Remove(id);
            }
        }

        private static Transform Cam => Camera.main != null ? Camera.main.transform : null;

        private void InputMenu()
        {
            if (Input.GetKeyDown(_menuKey)) { _menuOpen = !_menuOpen; if (_placing) StopPlacing(); }
            if (_menuOpen && (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))) StartPlacing(RecipeFormwork);
            if (_menuOpen && (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))) StartPlacing(RecipeMacaronTray);
            if (_menuOpen && Input.GetKeyDown(KeyCode.Escape)) _menuOpen = false;
        }

        private void StartPlacing(byte recipe)
        {
            _menuOpen = false;
            _placing = true;
            _placeRecipe = recipe;
            if (_preview != null && _previewRecipe != recipe) { Destroy(_preview.gameObject); _preview = null; }
            if (_preview == null) { _preview = BuildSiteView.Create(this, recipe, true); _previewRecipe = recipe; }
            Transform c = Cam;
            _previewYaw = c != null ? c.eulerAngles.y : 0f;
        }

        private void StopPlacing()
        {
            _placing = false;
            if (_preview != null) _preview.gameObject.SetActive(false);
        }

        private void UpdatePreview()
        {
            Transform c = Cam;
            if (c == null) return;
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) { StopPlacing(); return; }
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f) _previewYaw += wheel > 0f ? 15f : -15f;
            _previewOk = false;
            RaycastHit[] hits = Physics.RaycastAll(c.position, c.forward, _placeRange, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (RaycastHit h in hits)
            {
                if (h.collider.GetComponentInParent<FirstPersonController>() != null) continue;
                if (h.normal.y < 0.7f || h.distance >= best) continue;
                Rigidbody rb = h.collider.attachedRigidbody;
                if (rb != null && !rb.isKinematic) continue;
                best = h.distance; _previewPos = h.point; _previewOk = true;
            }
            _preview.gameObject.SetActive(_previewOk);
            if (_previewOk) _preview.ApplyPreview(_previewPos, _previewYaw);
            if (_previewOk && Input.GetMouseButtonDown(0))
            {
                CmdPlace(_placeRecipe, _previewPos, _previewYaw);
                StopPlacing();
            }
        }

        /// <summary>Клиент: ближайшая к лучу точка гвоздя. wantPlaced — только поставленные и недобитые.</summary>
        public bool AimNail(Ray ray, bool wantPlaced, out int siteId, out int nail)
        {
            siteId = 0; nail = -1;
            float best = float.MaxValue;
            for (int i = 0; i < _sites.Count; i++)
            {
                BuildSiteState s = _sites[i];
                if (wantPlaced ? (s.Stage != StageHammer && s.Stage != StageNails) : s.Stage != StageNails) continue;
                float rad = Mathf.Max(0.03f, NailLength(s.Recipe) * 1.1f);
                Matrix4x4 m = SiteMatrix(s.Recipe, s.Pos, s.Yaw);
                for (int k = 0; k < NailCount; k++)
                {
                    bool placed = (s.Nails & (1 << k)) != 0;
                    if (wantPlaced != placed) continue;
                    if (wantPlaced && HitsOf(s, k) >= _hitsPerNail) continue;
                    Vector3 p, d;
                    NailSlot(s.Recipe, k, out p, out d);
                    Vector3 w = m.MultiplyPoint3x4(p);
                    Vector3 to = w - ray.origin;
                    float along = Vector3.Dot(to, ray.direction);
                    if (along < 0f || along > _nailReach) continue;
                    float off = (to - ray.direction * along).magnitude;
                    if (off > rad + along * 0.03f) continue;
                    float score = off + along * 0.01f;
                    if (score < best) { best = score; siteId = s.Id; nail = k; }
                }
            }
            return nail >= 0;
        }

        private PlayerHand _hand;
        private PlayerHand LocalHand
        {
            get
            {
                if (_hand != null && _hand.IsOwner) return _hand;
                _hand = CCHands.Local;                                            // v23.2 (FPS): из кэша
                return _hand;
            }
        }

        /// <summary>Молоток в руке + ЛКМ по торчащему гвоздю — удар (молоток машет сам, как всегда).</summary>
        private void HammerInput()
        {
            _hammerCd -= Time.deltaTime;
            if (_hammerCd > 0f || !Input.GetMouseButtonDown(0)) return;
            PlayerHand hand = LocalHand;
            if (hand == null || !(hand.HeldItem is HammerItem)) return;
            Transform c = Cam;
            if (c == null) return;
            int id, nail;
            if (!AimNail(new Ray(c.position, c.forward), true, out id, out nail)) return;
            _hammerCd = 0.3f;
            CmdHit(id, nail);
        }

        private static GUIStyle _box;

        private void OnGUI()
        {
            if (!base.IsClientInitialized) return;
            if (_box == null) _box = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.UpperLeft, wordWrap = true };
            if (_menuOpen)
            {
                Rect r = new Rect(Screen.width * 0.5f - 190f, Screen.height * 0.5f - 90f, 380f, 170f);
                GUI.Box(r, "ПОСТРОЙКИ  (" + _menuKey + " — закрыть)\n\n" +
                           $"[1]  Форма для бисквита\n      {_boardsNeeded} досок · {NailCount} гвоздей · молоток\n\n" +
                           $"[2]  Противень для макарон\n      {_boardsNeeded} досок · {RingsNeeded(RecipeMacaronTray)} колец из трубы (пила) · {NailCount} гвоздей · молоток", _box);
                if (GUI.Button(new Rect(r.x + 10f, r.y + 40f, r.width - 20f, 50f), GUIContent.none, GUIStyle.none)) StartPlacing(RecipeFormwork);
                if (GUI.Button(new Rect(r.x + 10f, r.y + 95f, r.width - 20f, 60f), GUIContent.none, GUIStyle.none)) StartPlacing(RecipeMacaronTray);
            }
            else if (_placing)
            {
                GUI.Box(new Rect(Screen.width * 0.5f - 230f, Screen.height - 80f, 460f, 28f),
                        _previewOk ? $"«{TitleOf(_placeRecipe)}»: ЛКМ — поставить · колесо — повернуть · ПКМ/Esc — отмена"
                                   : "Наведись на пол (ближе " + _placeRange.ToString("0") + " м)", _box);
            }
        }
    }
}
