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
        public byte Recipe;
        public Vector3 Pos;          // точка на полу (центр низа формы)
        public float Yaw;
        public byte Boards;          // уложено досок
        public ushort Nails;         // битовая маска: где стоят гвозди
        public uint Hits;            // по 2 бита на гвоздь: сколько раз ударили (0..3)
        public byte Stage;           // 0 доски, 1 гвозди, 2 молоток, 3 готово
    }

    /// <summary>
    /// v22: ПРОСТАЯ СТРОЙКА. Пока одна постройка — ФОРМА ДЛЯ БИСКВИТА (опалубка).
    ///
    ///   1. C — маленькое меню построек. Выбрал «Форма для бисквита» — перед тобой призрачный шаблон.
    ///      Колесо мыши — повернуть, ЛКМ — поставить, ПКМ/Esc — отмена. Шаблон без физики, сквозь него ходят.
    ///   2. ДОСКИ: бросай в шаблон доски от поддонов (те же, что для костра; стопка — тоже). Каждая доска
    ///      по одной красиво влетает на своё место каркаса: 6 досок дна + 4 борта = 10.
    ///   3. ГВОЗДИ: на каркасе светятся точки (по две в каждом углу и по одной в середине длинных бортов).
    ///      Берёшь КОРОБКУ ГВОЗДЕЙ (NailBoxItem), ЛКМ по точке — гвоздь встаёт, торчит.
    ///   4. МОЛОТОК (маленький, HammerItem): ЛКМ по торчащему гвоздю — 3 удара на гвоздь, он уходит в доску.
    ///   5. Все гвозди забиты — шаблон исчезает, на его месте появляется настоящая опалубка (тара):
    ///      в неё заливают бисквитное тесто.
    ///
    /// Стоит в сцене одним объектом (меню CatCooks → Стройка → 1). Formwork Prefab — префаб опалубки
    /// (меню само найдёт префаб с BakeTray). Сеть: состояние строек — SyncList, видно всем и опоздавшим.
    /// </summary>
    public sealed class BuildManager : NetworkBehaviour
    {
        public static BuildManager Instance { get; private set; }

        [Header("Постройки")]
        [Tooltip("Префаб опалубки (NetworkObject + Cookware + BakeTray) — появляется, когда форма достроена. " +
                 "Должен быть в Spawnable Prefabs FishNet")]
        [SerializeField] private NetworkObject _formworkPrefab;
        [Tooltip("Откуда взять вид шаблона, если префаба нет (опалубка в сцене). Пусто — найдёт сам")]
        [SerializeField] private GameObject _formworkLook;

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

        // ── шаблон (меряется один раз по префабу/опалубке) ──
        private bool _shapeReady;
        private readonly List<Mesh> _lookMeshes = new List<Mesh>();
        private readonly List<Matrix4x4> _lookMats = new List<Matrix4x4>();
        private Material _woodMat, _nailMat;
        private Bounds _local;                 // габарит формы в осях корня опалубки
        public Bounds LocalBounds { get { EnsureShape(); return _local; } }
        public int BoardsNeeded => _boardsNeeded;
        public int HitsPerNail => _hitsPerNail;
        public Color GhostColor => _ghostColor;
        public Color PointColor => _pointColor;
        public Material WoodMat { get { EnsureShape(); return _woodMat; } }
        public Material NailMat { get { EnsureShape(); return _nailMat; } }

        // ── клиент ──
        private readonly Dictionary<int, BuildSiteView> _views = new Dictionary<int, BuildSiteView>();
        private bool _menuOpen, _placing;
        private BuildSiteView _preview;
        private float _previewYaw, _hammerCd;
        private Vector3 _previewPos;
        private bool _previewOk;

        private void Awake() { Instance = this; }
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var v in _views.Values) if (v != null) Destroy(v.gameObject);
            if (_preview != null) Destroy(_preview.gameObject);
        }

        // ───────────────────────── форма ─────────────────────────

        private void EnsureShape()
        {
            if (_shapeReady) return;
            _shapeReady = true;
            GameObject src = _formworkPrefab != null ? _formworkPrefab.gameObject : _formworkLook;
            if (src == null)
            {
                BakeTray bt = FindAnyObjectByType<BakeTray>();
                if (bt != null) src = bt.gameObject;
            }
            bool any = false;
            if (src != null)
            {
                Matrix4x4 toRoot = src.transform.worldToLocalMatrix;
                foreach (MeshFilter mf in src.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf == null || mf.sharedMesh == null) continue;
                    if (mf.GetComponentInParent<CookwareLiquidVisual>() != null) continue;
                    if (mf.gameObject.name == "Fill") continue;
                    MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null) continue;
                    Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                    _lookMeshes.Add(mf.sharedMesh);
                    _lookMats.Add(m);
                    Bounds b = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 p = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                        if (!any) { _local = new Bounds(p, Vector3.zero); any = true; } else _local.Encapsulate(p);
                    }
                    Material[] ms = mr.sharedMaterials;
                    if (_woodMat == null && ms.Length > 0) _woodMat = ms[0];
                    if (_nailMat == null && ms.Length > 1) _nailMat = ms[1];
                }
                // масштаб (и у префаба — поворот) корня: шаблон в метрах мира, повёрнут только по Y стройки
                Matrix4x4 rootM = _formworkPrefab != null
                    ? Matrix4x4.TRS(Vector3.zero, src.transform.rotation, src.transform.lossyScale)
                    : Matrix4x4.Scale(src.transform.lossyScale);
                any = false;
                for (int i = 0; i < _lookMats.Count; i++)
                {
                    _lookMats[i] = rootM * _lookMats[i];
                    Bounds b = _lookMeshes[i].bounds;
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 p = _lookMats[i].MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1)));
                        if (!any) { _local = new Bounds(p, Vector3.zero); any = true; } else _local.Encapsulate(p);
                    }
                }
            }
            if (!any)
            {
                _local = new Bounds(new Vector3(0f, 0.045f, 0f), new Vector3(0.6f, 0.09f, 0.4f));
                if (_log) Debug.LogWarning("[Стройка] нет префаба опалубки и опалубки в сцене — шаблон по размерам по умолчанию 0.6×0.4 м", this);
            }
            if (_woodMat == null) _woodMat = MakeMat(new Color(0.72f, 0.55f, 0.34f));
            if (_nailMat == null) _nailMat = MakeMat(new Color(0.55f, 0.57f, 0.6f));
            if (_log) Debug.Log($"[Стройка] форма для бисквита: {_local.size.x:0.00}×{_local.size.z:0.00}×{_local.size.y:0.00} м, " +
                                $"вид — {(src != null ? src.name : "по умолчанию")}, префаб опалубки {(_formworkPrefab != null ? "есть" : "НЕТ (в конце опалубка не появится)")}", this);
        }

        private static Material MakeMat(Color c)
        {
            Shader sh = Shader.Find("CatCooks/Toon Props");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(sh) { hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            return m;
        }

        public int LookCount { get { EnsureShape(); return _lookMeshes.Count; } }
        public Mesh LookMesh(int i) => _lookMeshes[i];
        public Matrix4x4 LookMatrix(int i) => _lookMats[i];

        /// <summary>Мир ← оси стройки: корень формы стоит так, чтобы низ габарита был на полу в точке pos.</summary>
        public Matrix4x4 SiteMatrix(Vector3 pos, float yaw)
        {
            EnsureShape();
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            Vector3 root = pos - r * new Vector3(_local.center.x, _local.min.y, _local.center.z);
            return Matrix4x4.TRS(root, r, Vector3.one);
        }

        // ───────────────────────── геометрия каркаса ─────────────────────────

        /// <summary>Доска №i: центр, размер (в осях формы). 0..5 — дно вдоль длины, 6..9 — борта.</summary>
        public void BoardSlot(int i, out Vector3 center, out Vector3 size)
        {
            EnsureShape();
            Bounds b = _local;
            float L = b.size.x, W = b.size.z, H = b.size.y;
            float t = Mathf.Clamp(H * 0.22f, 0.006f, W * 0.08f);          // толщина доски
            int bottom = Mathf.Max(1, _boardsNeeded - 4);
            if (i < bottom)
            {
                float w = W / bottom;
                center = new Vector3(b.center.x, b.min.y + t * 0.5f, b.min.z + w * (i + 0.5f));
                size = new Vector3(L, t, w * 0.96f);
                return;
            }
            int k = i - bottom;
            float h = H - t;
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
        public void NailSlot(int i, out Vector3 p, out Vector3 dir)
        {
            EnsureShape();
            Bounds b = _local;
            float t = Mathf.Clamp(b.size.y * 0.22f, 0.006f, b.size.z * 0.08f);
            float y = b.min.y + b.size.y * 0.62f;
            if (i < 8)
            {
                int corner = i / 2;                                  // (−x,−z) (+x,−z) (+x,+z) (−x,+z)
                bool px = corner == 1 || corner == 2, pz = corner >= 2;
                float x = px ? b.max.x : b.min.x, z = pz ? b.max.z : b.min.z;
                float sx = px ? -1f : 1f, sz = pz ? -1f : 1f;        // внутрь
                if (i % 2 == 0)
                {   // сквозь торец — в конец длинного борта
                    p = new Vector3(x, y, z + sz * t * 0.5f);
                    dir = new Vector3(sx, 0f, 0f);
                }
                else
                {   // сквозь длинный борт — в торец
                    p = new Vector3(x + sx * t * 0.5f, y, z);
                    dir = new Vector3(0f, 0f, sz);
                }
                return;
            }
            bool far = i == 9;
            p = new Vector3(b.center.x, b.min.y + t * 0.5f, far ? b.max.z : b.min.z);
            dir = new Vector3(0f, 0f, far ? -1f : 1f);
        }

        public float NailLength { get { EnsureShape(); return Mathf.Clamp(_local.size.y * 0.55f, 0.01f, 0.12f); } }

        // ───────────────────────── сеть ─────────────────────────

        public override void OnStartClient()
        {
            base.OnStartClient();
            EnsureShape();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            EnsureShape();
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdPlace(byte recipe, Vector3 pos, float yaw)
        {
            var s = new BuildSiteState { Id = _nextId++, Recipe = recipe, Pos = pos, Yaw = yaw, Stage = 0 };
            _sites.Add(s);
            if (_log) Debug.Log($"[Стройка] шаблон формы для бисквита поставлен (№{s.Id}) — неси {_boardsNeeded} досок", this);
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdCancel(int id)
        {
            int i = IndexOf(id);
            if (i >= 0 && _sites[i].Boards == 0) { _sites.RemoveAt(i); if (_log) Debug.Log($"[Стройка] шаблон №{id} убран", this); }
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdHit(int id, int nail)
        {
            ServerHitNail(id, nail);
        }

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
            if (s.Stage != 1 || (s.Nails & (1 << nail)) != 0) return false;
            s.Nails = (ushort)(s.Nails | (1 << nail));
            int placed = 0;
            for (int k = 0; k < NailCount; k++) if ((s.Nails & (1 << k)) != 0) placed++;
            if (placed >= NailCount)
            {
                s.Stage = 2;
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
            if (s.Stage != 2 && s.Stage != 1) return;
            if ((s.Nails & (1 << nail)) == 0) return;
            int h = HitsOf(s, nail);
            if (h >= _hitsPerNail) return;
            h++;
            s.Hits = (s.Hits & ~(3u << (nail * 2))) | ((uint)h << (nail * 2));
            bool all = s.Stage == 2;
            for (int k = 0; k < NailCount && all; k++) if (HitsOf(s, k) < _hitsPerNail) all = false;
            if (all) s.Stage = 3;
            _sites[i] = s;
            if (all) ServerFinish(i);
        }

        public static int HitsOf(BuildSiteState s, int nail) => (int)((s.Hits >> (nail * 2)) & 3u);

        [Server]
        private void ServerFinish(int i)
        {
            BuildSiteState s = _sites[i];
            if (_formworkPrefab != null)
            {
                Matrix4x4 m = SiteMatrix(s.Pos, s.Yaw);
                Vector3 pos = m.GetColumn(3);
                Quaternion rot = Quaternion.Euler(0f, s.Yaw, 0f) * _formworkPrefab.transform.rotation;   // как у шаблона
                NetworkObject nob = Instantiate(_formworkPrefab, pos + Vector3.up * 0.005f, rot);
                base.Spawn(nob.gameObject);
                if (_log) Debug.Log($"[Стройка] №{s.Id}: форма для бисквита готова — заливай тесто", this);
            }
            else if (_log) Debug.LogWarning("[Стройка] форма достроена, но Formwork Prefab пуст — опалубка не появилась. Сделай опалубку префабом (инструкция) и нажми меню CatCooks → Стройка → 1", this);
            _sites.RemoveAt(i);
        }

        // ───────────────────────── сервер: доски ─────────────────────────

        private readonly Collider[] _overlap = new Collider[32];

        private void FixedUpdate()
        {
            if (!base.IsServerInitialized || _sites.Count == 0) return;
            _absorbTick -= Time.fixedDeltaTime;
            if (_absorbTick > 0f) return;
            _absorbTick = 0.2f;
            for (int i = 0; i < _sites.Count; i++)
            {
                BuildSiteState s = _sites[i];
                if (s.Stage != 0) continue;
                Matrix4x4 m = SiteMatrix(s.Pos, s.Yaw);
                Vector3 c = m.MultiplyPoint3x4(_local.center + Vector3.up * _local.size.y * 0.5f);
                Vector3 half = new Vector3(_local.extents.x + 0.05f, _local.extents.y + 0.35f, _local.extents.z + 0.05f);
                int n = Physics.OverlapBoxNonAlloc(c, half, _overlap, Quaternion.Euler(0f, s.Yaw, 0f), ~0, QueryTriggerInteraction.Ignore);
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
                if (!changed) continue;
                if (s.Boards >= _boardsNeeded)
                {
                    s.Stage = 1;
                    if (_log) Debug.Log($"[Стройка] №{s.Id}: каркас собран — теперь гвозди: коробка гвоздей, ЛКМ по светящимся точкам", this);
                }
                else if (_log) Debug.Log($"[Стройка] №{s.Id}: досок {s.Boards}/{_boardsNeeded}", this);
                _sites[i] = s;
            }
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
                    v = BuildSiteView.Create(this, false);
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
            if (_menuOpen && (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))) StartPlacing();
            if (_menuOpen && Input.GetKeyDown(KeyCode.Escape)) _menuOpen = false;
        }

        private void StartPlacing()
        {
            _menuOpen = false;
            _placing = true;
            if (_preview == null) _preview = BuildSiteView.Create(this, true);
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
                CmdPlace(0, _previewPos, _previewYaw);
                StopPlacing();
            }
        }

        /// <summary>Клиент: ближайшая к лучу точка гвоздя. wantPlaced — только поставленные и недобитые.</summary>
        public bool AimNail(Ray ray, bool wantPlaced, out int siteId, out int nail)
        {
            siteId = 0; nail = -1;
            float best = float.MaxValue;
            float rad = Mathf.Max(0.03f, LocalBounds.size.y * 0.6f);
            for (int i = 0; i < _sites.Count; i++)
            {
                BuildSiteState s = _sites[i];
                if (wantPlaced ? (s.Stage != 2 && s.Stage != 1) : s.Stage != 1) continue;
                Matrix4x4 m = SiteMatrix(s.Pos, s.Yaw);
                for (int k = 0; k < NailCount; k++)
                {
                    bool placed = (s.Nails & (1 << k)) != 0;
                    if (wantPlaced != placed) continue;
                    if (wantPlaced && HitsOf(s, k) >= _hitsPerNail) continue;
                    Vector3 p, d;
                    NailSlot(k, out p, out d);
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

        private void OnGUI()
        {
            if (!base.IsClientInitialized) return;
            GUIStyle box = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.UpperLeft, wordWrap = true };
            if (_menuOpen)
            {
                Rect r = new Rect(Screen.width * 0.5f - 170f, Screen.height * 0.5f - 70f, 340f, 120f);
                GUI.Box(r, "ПОСТРОЙКИ  (" + _menuKey + " — закрыть)\n\n" +
                           $"[1]  Форма для бисквита\n      {_boardsNeeded} досок · {NailCount} гвоздей · молоток", box);
                if (GUI.Button(new Rect(r.x + 10f, r.y + 50f, r.width - 20f, 50f), GUIContent.none, GUIStyle.none)) StartPlacing();
            }
            else if (_placing)
            {
                GUI.Box(new Rect(Screen.width * 0.5f - 210f, Screen.height - 80f, 420f, 28f),
                        _previewOk ? "ЛКМ — поставить · колесо — повернуть · ПКМ/Esc — отмена" : "Наведись на пол (ближе " + _placeRange.ToString("0") + " м)", box);
            }
        }
    }
}
