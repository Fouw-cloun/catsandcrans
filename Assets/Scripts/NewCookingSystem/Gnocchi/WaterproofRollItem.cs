using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>Полоса раскатанной гидроизоляции (данные: начало на полу, направление, длина, ширина).</summary>
    [System.Serializable]
    public struct SheetStrip
    {
        public Vector3 A;      // начало, точка на полу (низ полосы)
        public Vector3 Dir;    // куда раскатана (горизонтально, единичный)
        public float Len;
        public float Width;
    }

    /// <summary>
    /// РУЛОН ГИДРОИЗОЛЯЦИИ — подстилка под большое тесто (ньокки, пельмени), чтобы не на грязный пол.
    ///
    ///   В РУКЕ:   ЛКМ — бросить рулон на пол туда, куда смотришь. Он ложится поперёк взгляда,
    ///             и ты сразу его катишь: идёшь вперёд — полотно разматывается за рулоном.
    ///             Бросил рядом с уже раскатанной полосой — новая встаёт ВСТЫК сбоку, как обои.
    ///   КАТИШЬ:   иди вперёд — разматывается (назад не сматывается).
    ///             ПКМ держать — отрезать (полотно остаётся лежать), F — отпустить рулон (покатишь потом, F по нему).
    ///   ЛЕЖИТ:    F — взять в руки снова.
    /// На полотне еда не пачкается (FoodCleanSurface). Полосы живут, пока жив рулон.
    /// ═══ НА КОРНЕ ═══ NetworkObject · NetworkTransform · Rigidbody · Capsule Collider (ось X) · этот компонент.
    /// Roll — ребёнок с мешем рулона (ось вдоль X): худеет, пока разматываешь.
    /// </summary>
    public sealed class WaterproofRollItem : PickupItem
    {
        [Header("Рулон")]
        [Tooltip("Ширина полотна (= длина рулона по оси X), м")]
        [SerializeField] private float _width = 2f;
        [Tooltip("Сколько метров полотна в рулоне")]
        [SerializeField] private float _totalLength = 30f;
        [Tooltip("Радиус полного рулона, м")]
        [SerializeField] private float _outerRadius = 0.28f;
        [Tooltip("Радиус втулки (пустой рулон), м")]
        [SerializeField] private float _coreRadius = 0.06f;
        [Tooltip("Меш рулона (ребёнок, ось вдоль X). Пусто — ребёнок «Roll»")]
        [SerializeField] private Transform _roll;

        [Header("Раскатка")]
        [Tooltip("Рулон катится впереди игрока на столько, м")]
        [SerializeField] private float _pushAhead = 0.9f;
        [Tooltip("Ушёл от рулона в сторону дальше этого — отпустил, м")]
        [SerializeField] private float _maxAside = 2.5f;
        [Tooltip("Бросок рядом с полосой (не дальше этого от её края, м) — встаёт встык, как обои")]
        [SerializeField] private float _snapDistance = 1.2f;
        [Tooltip("Нахлёст встык, м")]
        [SerializeField] private float _overlap = 0.01f;
        [Tooltip("Секунд держать ПКМ, чтобы отрезать")]
        [SerializeField] private float _cutHold = 0.7f;
        [Tooltip("Дальность броска рулона, м")]
        [SerializeField] private float _layReach = 6f;

        [Header("Полотно (вид)")]
        [SerializeField] private float _thickness = 0.006f;
        [Tooltip("Материал полотна. Пусто — сделает сам (тёмно-серый с посыпкой)")]
        [SerializeField] private Material _sheetMaterial;
        [SerializeField] private bool _log = true;

        public static readonly List<WaterproofRollItem> All = new List<WaterproofRollItem>();

        private readonly SyncList<SheetStrip> _strips = new SyncList<SheetStrip>();
        private readonly SyncVar<bool> _laid = new SyncVar<bool>(false);
        private readonly SyncVar<bool> _active = new SyncVar<bool>(false);
        private readonly SyncVar<Vector3> _aStart = new SyncVar<Vector3>(Vector3.zero);
        private readonly SyncVar<Vector3> _aDir = new SyncVar<Vector3>(Vector3.forward);
        private readonly SyncVar<float> _aLen = new SyncVar<float>(0f);
        private readonly SyncVar<float> _used = new SyncVar<float>(0f);
        private readonly SyncVar<NetworkObject> _pusher = new SyncVar<NetworkObject>(null);

        private Transform _sheetRoot;
        private readonly List<Transform> _sheetViews = new List<Transform>();
        private Transform _activeView;
        private Collider[] _cols;
        private CapsuleCollider _capsule;
        private float _cutT, _spin;
        private Quaternion _rollBase;
        private Vector3 _rollScale0 = Vector3.one;
        private bool _rollBaseSet;
        private static Mesh _unitBox;
        private static Material _autoMat;

        public float Remaining => Mathf.Max(0f, _totalLength - _used.Value - (_active.Value ? _aLen.Value : 0f));
        public float Radius => Mathf.Sqrt(_coreRadius * _coreRadius + (_outerRadius * _outerRadius - _coreRadius * _coreRadius) * Mathf.Clamp01(Remaining / Mathf.Max(0.01f, _totalLength)));
        public bool IsPushing => _pusher.Value != null;

        // ───────────────────────── жизнь ─────────────────────────

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            if (_roll == null) _roll = transform.Find("Roll");
            _cols = GetComponentsInChildren<Collider>(true);
            _capsule = GetComponent<CapsuleCollider>();
        }

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _strips.OnChange += OnStrips;
            _active.OnChange += OnActiveB;
            _aLen.OnChange += OnLen;
            _pusher.OnChange += OnPusher;
            RebuildSheets();
            ApplySolid();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _strips.OnChange -= OnStrips;
            _active.OnChange -= OnActiveB;
            _aLen.OnChange -= OnLen;
            _pusher.OnChange -= OnPusher;
        }

        private void OnDestroy()
        {
            if (_sheetRoot != null) SafeDestroy.Kill(_sheetRoot.gameObject);
        }

        private void OnStrips(SyncListOperation op, int i, SheetStrip a, SheetStrip b, bool s) => RebuildSheets();
        private void OnActiveB(bool a, bool b, bool s) => RebuildSheets();
        private void OnLen(float a, float b, bool s) => UpdateActiveView();
        private void OnPusher(NetworkObject a, NetworkObject b, bool s) => ApplySolid();

        /// <summary>Катят — рулон не мешает идти (коллайдеры выключены); лежит — твёрдый.</summary>
        private void ApplySolid()
        {
            if (_cols == null) return;
            bool on = _pusher.Value == null;
            foreach (Collider c in _cols) if (c != null && !c.isTrigger) c.enabled = on && !IsHeldNow;
        }

        private bool IsHeldNow => _rb != null && _rb.isKinematic && !_laid.Value;

        // ───────────────────────── в руке: бросить и катить ─────────────────────────

        public override void ServerPrimaryUse(UseContext ctx)
        {
            if (_laid.Value) return;
            if (Remaining < 0.2f) { Say("рулон пустой — осталась одна втулка"); return; }
            if (!FloorHit(ctx.Origin, ctx.Direction, out RaycastHit hit)) { Say("брось на пол — смотри под ноги, ближе"); return; }
            Vector3 dir = Vector3.ProjectOnPlane(ctx.Direction, Vector3.up);
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            dir.Normalize();
            Vector3 a = hit.point;
            bool snapped = FindNeighbor(a, dir, out Vector3 sa, out Vector3 sd, out string who);
            if (snapped) { a = sa; dir = sd; }

            PlayerHand hand = ctx.Hand;
            if (hand != null) hand.ServerDrop(Vector3.zero, 0f);
            Lay(a, dir);
            _active.Value = true; _aStart.Value = a; _aDir.Value = dir; _aLen.Value = 0f;
            if (hand != null) _pusher.Value = hand.NetworkObject;
            ApplySolid();
            Say(snapped ? $"рулон встал встык к полосе {who} — катай вперёд" : "рулон на полу — иди вперёд, он разматывается. ПКМ держать — отрезать, F — отпустить");
        }

        private void Lay(Vector3 floor, Vector3 dir)
        {
            _laid.Value = true;
            if (_rb != null) { _rb.isKinematic = true; _rb.useGravity = false; _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
            PlaceRoll(floor, dir);
        }

        private void PlaceRoll(Vector3 floor, Vector3 dir)
        {
            Vector3 pos = floor + Vector3.up * (Radius + _thickness);
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
            if (_rb != null) { _rb.position = pos; _rb.rotation = rot; }
            transform.SetPositionAndRotation(pos, rot);
        }

        private bool FloorHit(Vector3 origin, Vector3 dir, out RaycastHit best)
        {
            best = new RaycastHit();
            RaycastHit[] hits = Physics.RaycastAll(origin, dir, _layReach, ~0, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue; bool any = false;
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
                if (h.collider.GetComponentInParent<PlayerInteractor>() != null) continue;
                if (h.normal.y < 0.6f) continue;               // только пол / плоское
                if (h.distance < bd) { bd = h.distance; best = h; any = true; }
            }
            return any;
        }

        /// <summary>Рядом с этой точкой есть полоса — встать встык к её боку (как обои), параллельно.</summary>
        private bool FindNeighbor(Vector3 p, Vector3 dir, out Vector3 start, out Vector3 newDir, out string who)
        {
            start = p; newDir = dir; who = null;
            float bestScore = float.MaxValue; bool found = false;
            foreach (WaterproofRollItem r in All)
            {
                if (r == null) continue;
                int n = r._strips.Count + (r._active.Value && r != this ? 1 : 0);
                for (int i = 0; i < n; i++)
                {
                    SheetStrip s = i < r._strips.Count ? r._strips[i] : r.ActiveStrip;
                    if (s.Len < 0.2f) continue;
                    Vector3 side = Vector3.Cross(Vector3.up, s.Dir);
                    Vector3 d = p - s.A;
                    float u = Vector3.Dot(d, s.Dir), v = Vector3.Dot(d, side);
                    if (u < -s.Width || u > s.Len + s.Width) continue;
                    float av = Mathf.Abs(v);
                    if (av < s.Width * 0.25f) continue;                                    // на самой полосе — кладём поверх, без стыковки
                    if (av > s.Width * 0.5f + _width + _snapDistance) continue;
                    if (Mathf.Abs(Vector3.Dot(dir, s.Dir)) < 0.6f) continue;               // поперёк — не стыкуем
                    float score = Mathf.Abs(av - (s.Width * 0.5f + _width * 0.5f));
                    if (score >= bestScore) continue;
                    bestScore = score; found = true;
                    bool same = Vector3.Dot(dir, s.Dir) >= 0f;
                    float sign = v >= 0f ? 1f : -1f;
                    float off = sign * (s.Width * 0.5f + _width * 0.5f - _overlap);
                    newDir = same ? s.Dir : -s.Dir;
                    start = (same ? s.A : s.A + s.Dir * s.Len) + side * off + Vector3.up * 0.0015f;   // чуть выше — без мерцания на нахлёсте
                    who = $"«{r.name}» №{i + 1}";
                }
            }
            return found;
        }

        private SheetStrip ActiveStrip => new SheetStrip { A = _aStart.Value, Dir = _aDir.Value, Len = _aLen.Value, Width = _width };

        // ───────────────────────── F: катить / отпустить / взять ─────────────────────────

        public override void ServerInteract(PlayerInteractor interactor)
        {
            if (_laid.Value && _active.Value)
            {
                if (_pusher.Value == null) { _pusher.Value = interactor.NetworkObject; Say("катишь — иди вперёд. ПКМ держать — отрезать"); }
                else if (_pusher.Value == interactor.NetworkObject) { _pusher.Value = null; Say("отпустил рулон — F, чтобы катить дальше"); }
                ApplySolid();
                return;
            }
            if (_laid.Value) { _laid.Value = false; if (_rb != null) { _rb.isKinematic = false; _rb.useGravity = true; } }
            base.ServerInteract(interactor);
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdRelease(NetworkConnection sender = null)
        {
            if (_pusher.Value == null || _pusher.Value.Owner != sender) return;
            _pusher.Value = null;
            ApplySolid();
            Say("отпустил рулон — F по нему, чтобы катить дальше");
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdCut(NetworkConnection sender = null)
        {
            if (!_active.Value) return;
            if (_pusher.Value != null && _pusher.Value.Owner != sender) return;
            ServerCut();
        }

        [Server]
        private void ServerCut()
        {
            float len = _aLen.Value;
            if (len >= 0.1f)
            {
                _strips.Add(ActiveStrip);
                _used.Value = _used.Value + len;
                Say($"отрезал полосу {len:0.0} м × {_width:0.0} м — осталось в рулоне {Remaining:0.0} м");
            }
            else Say("отрезать нечего — сначала раскатай");
            _active.Value = false; _aLen.Value = 0f; _pusher.Value = null;
            ApplySolid();
        }

        // ───────────────────────── каждый кадр ─────────────────────────

        private void Update()
        {
            if (base.IsServerInitialized) ServerTick();
            ClientInput();
            RollVisual();
        }

        [Server]
        private void ServerTick()
        {
            if (!_laid.Value || !_active.Value) return;
            NetworkObject p = _pusher.Value;
            if (p == null) { PlaceRoll(_aStart.Value + _aDir.Value * _aLen.Value, _aDir.Value); return; }
            Vector3 dir = _aDir.Value, a = _aStart.Value;
            Vector3 d = p.transform.position - a;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            if (Mathf.Abs(Vector3.Dot(d, side)) > _width * 0.5f + _maxAside) { _pusher.Value = null; ApplySolid(); Say("ушёл от рулона — отпустил. F по нему, чтобы катить дальше"); return; }
            float t = Vector3.Dot(d, dir) + _pushAhead;
            float max = _aLen.Value + Remaining;
            float len = Mathf.Clamp(Mathf.Max(_aLen.Value, t), 0f, max);
            if (len > _aLen.Value + 0.005f) _aLen.Value = len;
            PlaceRoll(a + dir * _aLen.Value, dir);
            if (Remaining <= 0.01f) { Say("рулон кончился — полоса отрезана"); ServerCut(); }
        }

        private void ClientInput()
        {
            NetworkObject p = _pusher.Value;
            bool mine = p != null && p.IsOwner;
            bool look = !mine && _laid.Value && _active.Value && LookedAt();
            if ((mine || look) && Input.GetMouseButton(1) && !UiModal.AnyOpen)
            {
                _cutT += Time.deltaTime;
                if (_cutT >= _cutHold) { _cutT = -10f; CmdCut(); }
            }
            else if (!Input.GetMouseButton(1)) _cutT = 0f;
            if (mine && Input.GetKeyDown(KeyCode.F) && !UiModal.AnyOpen) CmdRelease();
        }

        private bool LookedAt()
        {
            Camera cam = Camera.main;
            if (cam == null) return false;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit h, 5f, ~0, QueryTriggerInteraction.Ignore)) return false;
            return h.collider != null && h.collider.transform.IsChildOf(transform);
        }

        /// <summary>Рулон худеет и крутится, пока разматывается.</summary>
        private void RollVisual()
        {
            float r = Radius;
            if (_roll != null)
            {
                if (!_rollBaseSet) { _rollBase = _roll.localRotation; _rollScale0 = _roll.localScale; _rollBaseSet = true; }
                float k = r / Mathf.Max(0.001f, _outerRadius);
                _roll.localScale = Vector3.Scale(_rollScale0, new Vector3(1f, k, k));
                float unrolled = _used.Value + (_active.Value ? _aLen.Value : 0f);
                _spin = unrolled / Mathf.Max(0.02f, r) * Mathf.Rad2Deg;
                _roll.localRotation = Quaternion.AngleAxis(_spin, Vector3.right) * _rollBase;
            }
            if (_capsule != null) _capsule.radius = r;
        }

        // ───────────────────────── полотно (вид) ─────────────────────────

        private void RebuildSheets()
        {
            if (_sheetRoot == null)
            {
                _sheetRoot = new GameObject($"WaterproofSheets «{name}» (auto)").transform;
            }
            int need = _strips.Count;
            while (_sheetViews.Count < need) _sheetViews.Add(MakeView($"Strip {_sheetViews.Count + 1}"));
            for (int i = 0; i < _sheetViews.Count; i++)
            {
                bool on = i < need;
                if (_sheetViews[i].gameObject.activeSelf != on) _sheetViews[i].gameObject.SetActive(on);
                if (on) Fit(_sheetViews[i], _strips[i]);
            }
            if (_activeView == null) _activeView = MakeView("Strip (раскатывается)");
            UpdateActiveView();
        }

        private void UpdateActiveView()
        {
            if (_activeView == null) return;
            bool on = _active.Value && _aLen.Value > 0.02f;
            if (_activeView.gameObject.activeSelf != on) _activeView.gameObject.SetActive(on);
            if (on) Fit(_activeView, ActiveStrip);
        }

        private Transform MakeView(string n)
        {
            var go = new GameObject(n);
            go.transform.SetParent(_sheetRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = UnitBox();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _sheetMaterial != null ? _sheetMaterial : AutoMaterial();
            go.AddComponent<BoxCollider>();
            go.AddComponent<FoodCleanSurface>().Title = "гидроизоляция";
            return go.transform;
        }

        private static readonly int StId = Shader.PropertyToID("_BaseMap_ST"), MainStId = Shader.PropertyToID("_MainTex_ST");
        private MaterialPropertyBlock _mpb;

        private void Fit(Transform t, SheetStrip s)
        {
            Vector3 dir = s.Dir.sqrMagnitude > 1e-4f ? s.Dir.normalized : Vector3.forward;
            t.SetPositionAndRotation(s.A + dir * (s.Len * 0.5f) + Vector3.up * (_thickness * 0.5f), Quaternion.LookRotation(dir, Vector3.up));
            t.localScale = new Vector3(s.Width, _thickness, Mathf.Max(0.01f, s.Len));
            MeshRenderer mr = t.GetComponent<MeshRenderer>();
            if (mr == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            mr.GetPropertyBlock(_mpb);
            Vector4 st = new Vector4(1f, Mathf.Max(0.05f, s.Len / Mathf.Max(0.1f, s.Width)), 0f, 0f);
            _mpb.SetVector(StId, st); _mpb.SetVector(MainStId, st);
            mr.SetPropertyBlock(_mpb);
        }

        /// <summary>Куб 1×1×1 с UV сверху: U — поперёк, V — вдоль (тайлинг по длине через _BaseMap_ST).</summary>
        private static Mesh UnitBox()
        {
            if (_unitBox != null) return _unitBox;
            GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _unitBox = Object.Instantiate(tmp.GetComponent<MeshFilter>().sharedMesh);
            _unitBox.name = "WaterproofStrip (auto)";
            SafeDestroy.Kill(tmp);
            return _unitBox;
        }

        public static Material AutoMaterial()
        {
            if (_autoMat != null) return _autoMat;
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            _autoMat = new Material(sh) { name = "Waterproof (auto)" };
            Texture2D tex = SheetTexture();
            _autoMat.SetTexture("_BaseMap", tex); _autoMat.mainTexture = tex;
            _autoMat.SetColor("_BaseColor", Color.white);
            _autoMat.SetFloat("_Smoothness", 0.25f);
            return _autoMat;
        }

        /// <summary>Тёмно-серое полотно с мелкой посыпкой, по краю — светлая кромка с плёнкой (под нахлёст).</summary>
        public static Texture2D SheetTexture()
        {
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "Waterproof (auto)", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            var rnd = new System.Random(7);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float g = 0.17f + (float)rnd.NextDouble() * 0.05f;
                    if (rnd.NextDouble() < 0.08) g += 0.12f;                     // посыпка
                    float u = x / (float)(N - 1);
                    Color c = new Color(g, g, g * 1.05f);
                    if (u > 0.93f) c = Color.Lerp(c, new Color(0.55f, 0.57f, 0.6f), 0.7f);     // кромка-плёнка
                    if (u < 0.015f || (u > 0.925f && u < 0.935f)) c *= 0.7f;
                    px[y * N + x] = c;
                }
            tex.SetPixels32(px); tex.Apply(true);
            return tex;
        }

        // ───────────────────────── подсказка ─────────────────────────

        private static GUIStyle _st;

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            NetworkObject p = _pusher.Value;
            bool mine = p != null && p.IsOwner;
            string text = null;
            if (mine) text = $"Рулон: размотано {_aLen.Value:0.0} м · в рулоне {Remaining:0.0} м · иди вперёд — разматывается · ПКМ держать — отрезать · F — отпустить";
            else if (_laid.Value && LookedAt()) text = _active.Value ? $"Рулон: размотано {_aLen.Value:0.0} м · F — катить · ПКМ держать — отрезать" : $"Рулон гидроизоляции · {Remaining:0.0} м · F — взять";
            else if (IsHeldLocally()) text = $"Рулон гидроизоляции ({Remaining:0.0} м) · ЛКМ — бросить на пол и раскатывать · рядом с полосой — встанет встык";
            if (text == null) return;
            if (_st == null) _st = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            Rect r = new Rect(Screen.width * 0.5f - 330f, Screen.height * 0.72f, 660f, 44f);
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old;
            GUI.Label(r, text, _st);
            if (_cutT > 0f)
            {
                var bar = new Rect(Screen.width * 0.5f - 100f, r.yMax + 4f, 200f, 8f);
                GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(bar, Texture2D.whiteTexture);
                GUI.color = new Color(1f, 0.8f, 0.3f); GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(_cutT / _cutHold), bar.height), Texture2D.whiteTexture);
                GUI.color = old;
            }
        }

        private bool IsHeldLocally() => !_laid.Value && base.IsOwner && _rb != null && _rb.isKinematic;

        private void Say(string text)
        {
            if (_log) Debug.Log($"[Рулон] «{name}»: {text}", this);
        }

#if UNITY_EDITOR
        public void EditorSetup(float width, float length, float outer, float core, Transform roll, Material mat)
        {
            UnityEditor.Undo.RecordObject(this, "Рулон");
            _width = width; _totalLength = length; _outerRadius = outer; _coreRadius = core; _roll = roll; _sheetMaterial = mat;
            UnityEditor.EditorUtility.SetDirty(this);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this)) UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }
#endif
    }
}
