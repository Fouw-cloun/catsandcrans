using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ШПРИЦ — строительный мешок для затирки швов со стальным носиком. Кондитерский мешок по-нашему.
    ///
    ///   ЛКМ (держать) по таре с массой — НАБРАТЬ (тесто для макарон, ганаш, крем…): мешок раздувается.
    ///   ЛКМ (держать) по кольцу противня — ДАВИТЬ тесто в кольцо: над кольцом % — попади в 85–112%.
    ///   ЛКМ (держать) по скорлупке — ДАВИТЬ начинку: скорлупка переворачивается, растёт шапочка ганаша.
    ///   ПКМ по таре — выдавить всё обратно.
    /// Носик сам смотрит туда, куда давишь; струйка видна всем.
    ///
    /// ═══ НА КОРНЕ ═══ NetworkObject · NetworkTransform · Rigidbody · Box Collider · этот компонент.
    /// Дети (делает меню «Макарон → 4»): Body — мешок (раздувается), Nozzle — кончик носика,
    /// Stream — струйка (цилиндр от носика вдоль +Z, длина 1), сначала скрыт.
    /// </summary>
    public sealed class PipingBagItem : PickupItem
    {
        [Header("Мешок")]
        [Tooltip("Сколько порций влезает")]
        [SerializeField] private float _capacity = 10f;
        [Tooltip("Набирает, порций в секунду")]
        [SerializeField] private float _fillRate = 4f;
        [Tooltip("Давит, порций в секунду (одно кольцо = 1 порция)")]
        [SerializeField] private float _pipeRate = 0.8f;
        [Tooltip("Дальность от камеры, м")]
        [SerializeField] private float _reach = 3f;

        [Header("Вид")]
        [SerializeField] private Transform _body;
        [SerializeField] private Transform _nozzle;
        [SerializeField] private Transform _stream;
        [Tooltip("Насколько худеет пустой мешок")]
        [Range(0.2f, 1f)][SerializeField] private float _emptyThin = 0.5f;
        [SerializeField] private bool _log = true;

        // ── сеть ──
        private readonly SyncVar<ushort> _product = new SyncVar<ushort>(0);
        private readonly SyncVar<ushort> _amount = new SyncVar<ushort>(0);          // сотые порции
        private readonly SyncVar<bool> _piping = new SyncVar<bool>(false);
        private readonly SyncVar<Vector3> _pipeAt = new SyncVar<Vector3>(Vector3.zero);

        /// <summary>Куда смотрит локальный игрок со шприцем (для подсказки над кольцом).</summary>
        public static MacaronTray LocalAimTray;
        public static int LocalAimRing = -1;
        /// <summary>Скорлупка, на которую смотрит локальный игрок со шприцем (подсказка «ганаш N%»).</summary>
        public static MacaronShell LocalAimShell;

        private readonly HeldToolLook _look = new HeldToolLook();
        private Vector3 _bodyScale;
        private float _sendAt, _sendDt, _hintAt;
        private bool _lmbWas;
        private Vector3 _aimPoint;
        private MaterialPropertyBlock _mpb;
        private Renderer _streamR;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        public float Amount => _amount.Value / 100f;
        public bool IsEmpty => _amount.Value == 0;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            if (_body == null) _body = transform.Find("Body");
            if (_nozzle == null) _nozzle = transform.Find("Nozzle");
            if (_stream == null) _stream = transform.Find("Stream");
            if (_body != null) _bodyScale = _body.localScale;
            if (_stream != null) { _streamR = _stream.GetComponentInChildren<Renderer>(true); _stream.gameObject.SetActive(false); }
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _amount.OnChange += OnAmount;
            RefreshBody();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _amount.OnChange -= OnAmount;
            if (LocalAimTray != null && base.IsOwner) { LocalAimTray = null; LocalAimRing = -1; }
            if (LocalAimShell != null && base.IsOwner) LocalAimShell = null;
        }

        private void OnAmount(ushort a, ushort b, bool asServer) => RefreshBody();

        // ───────────────────────── ВЛАДЕЛЕЦ ─────────────────────────

        private enum Aim { None, Fill, Ring, Shell }

        private void Update()
        {
            if (!base.IsOwner || !_look.Held)
            {
                if (base.IsOwner && _lmbWas) { _lmbWas = false; CmdStop(); }
                if (base.IsOwner && LocalAimTray != null) { LocalAimTray = null; LocalAimRing = -1; }
                if (base.IsOwner && LocalAimShell != null) LocalAimShell = null;
                return;
            }
            Camera cam = Camera.main;
            if (cam == null) return;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Aim kind = Target(ray, out NetworkObject target, out int ring, out Vector3 point);
            LocalAimTray = kind == Aim.Ring && target != null ? target.GetComponent<MacaronTray>() : null;
            LocalAimRing = kind == Aim.Ring ? ring : -1;
            LocalAimShell = kind == Aim.Shell && target != null ? target.GetComponent<MacaronShell>() : null;
            _aimPoint = point;

            bool lmb = Input.GetMouseButton(0);
            if (lmb)
            {
                _sendDt += Time.deltaTime;
                if (Time.time >= _sendAt && kind != Aim.None && target != null)
                {
                    _sendAt = Time.time + 0.1f;
                    float dt = Mathf.Min(_sendDt, 0.25f);
                    _sendDt = 0f;
                    if (kind == Aim.Fill) CmdFill(target, dt);
                    else if (kind == Aim.Ring) CmdPipeRing(target, ring, dt, point);
                    else if (kind == Aim.Shell) CmdPipeShell(target, dt, point);
                }
                else if (kind == Aim.None && Time.time >= _sendAt) { _sendAt = Time.time + 0.5f; _sendDt = 0f; if (_lmbWas) CmdStop(); }
            }
            else if (_lmbWas) { _sendDt = 0f; CmdStop(); }
            _lmbWas = lmb;

            if (Input.GetMouseButtonDown(1) && !IsEmpty && kind == Aim.Fill && target != null) CmdDump(target);
        }

        /// <summary>Что под прицелом: кольцо противня, скорлупка, тара с массой.</summary>
        private Aim Target(Ray ray, out NetworkObject target, out int ring, out Vector3 point)
        {
            target = null; ring = -1; point = ray.origin + ray.direction * _reach;
            int n = Physics.RaycastNonAlloc(ray, _hits, _reach, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            Aim kind = Aim.None;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform) || h.distance >= best) continue;
                if (h.collider.GetComponentInParent<PlayerInteractor>() != null) continue;
                MacaronTray tray = h.collider.GetComponentInParent<MacaronTray>();
                if (tray != null && !h.collider.isTrigger)
                {
                    int r = tray.RingAt(h.point);
                    best = h.distance; point = h.point;
                    if (r >= 0) { kind = Aim.Ring; target = tray.NetworkObject; ring = r; } else kind = Aim.None;
                    continue;
                }
                MacaronShell sh = h.collider.GetComponentInParent<MacaronShell>();
                if (sh != null && !h.collider.isTrigger)
                {
                    best = h.distance; point = h.point; kind = Aim.Shell; target = sh.NetworkObject;
                    continue;
                }
                Cookware cw = h.collider.GetComponentInParent<Cookware>();
                if (cw != null)
                {
                    best = h.distance; point = h.point; kind = Aim.Fill; target = cw.NetworkObject;
                    continue;
                }
                if (!h.collider.isTrigger) { best = h.distance; point = h.point; kind = Aim.None; target = null; }
            }
            return kind;
        }

        private void LateUpdate()
        {
            _look.Late(false, false);
            UpdateStream();
        }

        public override void UpdateHeldPose(Transform handAnchor)
        {
            _look.Pose(handAnchor);
            base.UpdateHeldPose(handAnchor);
            if (!base.IsOwner || !_lmbWas || _nozzle == null) return;
            // носиком к цели: мешок наклоняется туда, куда давишь
            Vector3 down = (_nozzle.position - transform.position);
            Vector3 to = _aimPoint - _nozzle.position;
            if (down.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f) return;
            Quaternion turn = Quaternion.FromToRotation(down.normalized, to.normalized);
            transform.rotation = Quaternion.Slerp(Quaternion.identity, turn, 0.6f) * transform.rotation;
        }

        public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
        {
            _piping.Value = false;
            _look.Release();
            base.ServerOnDrop(hand, tossDirection, force);
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        private bool Near(NetworkObject t) =>
            t != null && (t.transform.position - transform.position).sqrMagnitude < Mathf.Pow(_reach * 2f + 2f, 2f);

        [ServerRpc(RequireOwnership = true)]
        private void CmdFill(NetworkObject target, float dt)
        {
            if (!Near(target)) return;
            Cookware cw = target.GetComponent<Cookware>();
            if (cw == null) cw = target.GetComponentInChildren<Cookware>();
            if (cw == null || cw.IsEmpty) { Hint("тара пустая"); return; }
            float room = _capacity - Amount;
            if (room < 0.01f) { Hint("шприц полный — дави в кольца или на скорлупки"); return; }
            int want = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(room, _fillRate * dt + _fillCarry)), 0, 99);
            _fillCarry += _fillRate * dt;
            if (want <= 0) return;
            _fillCarry = 0f;
            int took = cw.ServerTakeTop(want, out ushort p, out FoodState st, out byte cook);
            if (took <= 0) return;
            FoodDatabase db = FoodDatabase.Instance;
            ProductDef d = db != null ? db.Get(p) : null;
            if ((d != null && (d.Solid || FoodUnits.IsAdditive(d, st))) || (!IsEmpty && p != _product.Value))
            {
                cw.ServerAddPinch(p, st, took, cook, false);
                Hint(!IsEmpty && p != _product.Value ? $"в шприце уже «{Title(_product.Value)}» — сначала выдави (ПКМ по таре)" : $"«{(d != null ? d.Title : "это")}» шприцем не набрать");
                return;
            }
            _product.Value = p;
            _amount.Value = (ushort)Mathf.Clamp(Mathf.RoundToInt((Amount + took) * 100f), 0, 65000);
            if (_log && (Amount >= _capacity - 0.01f || took >= 2)) Debug.Log($"[Шприц] набрал «{Title(p)}»: {Amount:0.#}/{_capacity:0}", this);
        }
        private float _fillCarry;

        [ServerRpc(RequireOwnership = true)]
        private void CmdPipeRing(NetworkObject target, int ring, float dt, Vector3 at)
        {
            if (!Near(target) || IsEmpty) { if (IsEmpty) Hint("шприц пустой — набери тесто (ЛКМ по ведру)"); StopVisual(); return; }
            MacaronTray tray = target.GetComponent<MacaronTray>();
            if (tray == null) return;
            float used = tray.ServerPipe(ring, _product.Value, Mathf.Min(Amount, _pipeRate * dt));
            Spend(used, tray.RingWorld(ring));
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdPipeShell(NetworkObject target, float dt, Vector3 at)
        {
            if (!Near(target) || IsEmpty) { if (IsEmpty) Hint("шприц пустой — набери ганаш"); StopVisual(); return; }
            MacaronShell sh = target.GetComponent<MacaronShell>();
            if (sh == null) return;
            float used = sh.ServerAddFilling(_product.Value, Mathf.Min(Amount, _pipeRate * 1.2f * dt), out string why);
            if (used <= 0f && why != null) Hint(why);
            Spend(used, sh.transform.position + Vector3.up * sh.Radius * 0.6f);
        }

        private void Spend(float used, Vector3 at)
        {
            if (used <= 0f) { StopVisual(); return; }
            _amount.Value = (ushort)Mathf.Clamp(Mathf.RoundToInt((Amount - used) * 100f), 0, 65000);
            if (_amount.Value < 2) { _amount.Value = 0; _product.Value = 0; if (_log) Debug.Log("[Шприц] пустой", this); }
            if (!_piping.Value) _piping.Value = true;
            if ((_pipeAt.Value - at).sqrMagnitude > 1e-4f) _pipeAt.Value = at;
        }

        private void StopVisual() { if (_piping.Value) _piping.Value = false; }

        [ServerRpc(RequireOwnership = true)]
        private void CmdStop() => StopVisual();

        [ServerRpc(RequireOwnership = true)]
        private void CmdDump(NetworkObject target)
        {
            if (!Near(target) || IsEmpty) return;
            Cookware cw = target.GetComponent<Cookware>();
            if (cw == null) cw = target.GetComponentInChildren<Cookware>();
            if (cw == null) return;
            int n = Mathf.FloorToInt(Amount + 0.01f);
            if (n > 0) cw.ServerAddContent(_product.Value, StartStateOf(_product.Value), n, 0);
            if (_log) Debug.Log($"[Шприц] выдавил обратно {n} порц. «{Title(_product.Value)}»", this);
            _amount.Value = 0; _product.Value = 0;
        }

        private static FoodState StartStateOf(ushort p)
        {
            FoodDatabase db = FoodDatabase.Instance;
            ProductDef d = db != null ? db.Get(p) : null;
            return d != null ? d.StartState : FoodState.Raw;
        }

        private static string Title(ushort p)
        {
            FoodDatabase db = FoodDatabase.Instance;
            ProductDef d = db != null ? db.Get(p) : null;
            return d != null ? d.Title : "?";
        }

        private void Hint(string text)
        {
            if (!_log || Time.time < _hintAt) return;
            _hintAt = Time.time + 3f;
            Debug.Log($"[Шприц] {text}", this);
        }

        // ───────────────────────── ВИД ─────────────────────────

        private void RefreshBody()
        {
            if (_body == null) return;
            float k = Mathf.Clamp01(Amount / Mathf.Max(0.1f, _capacity));
            float thin = Mathf.Lerp(_emptyThin, 1f, Mathf.Sqrt(k));
            _body.localScale = new Vector3(_bodyScale.x * thin, _bodyScale.y, _bodyScale.z * thin);
        }

        private void UpdateStream()
        {
            if (_stream == null || _nozzle == null) return;
            bool on = _piping.Value && !IsEmpty;
            if (_stream.gameObject.activeSelf != on) _stream.gameObject.SetActive(on);
            if (!on) return;
            Vector3 a = _nozzle.position, b = _pipeAt.Value;
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            _stream.position = a;
            _stream.rotation = Quaternion.LookRotation(d / len);
            Vector3 ps = _stream.parent != null ? _stream.parent.lossyScale : Vector3.one;
            float w = Mathf.Max(0.004f, _stream.localScale.x);
            _stream.localScale = new Vector3(w, w, len / Mathf.Max(1e-4f, Mathf.Abs(ps.z)));
            if (_streamR != null)
            {
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                FoodDatabase db = FoodDatabase.Instance;
                ProductDef pd = db != null ? db.Get(_product.Value) : null;
                _streamR.GetPropertyBlock(_mpb);
                Color c = pd != null ? pd.BaseColor : new Color(0.8f, 0.6f, 0.45f); c.a = 1f;
                _mpb.SetColor("_BaseColor", c);
                _streamR.SetPropertyBlock(_mpb);
            }
        }

        private void OnGUI()
        {
            if (!base.IsOwner || !_look.Held) return;
            string s = IsEmpty ? "шприц пустой — ЛКМ по ведру: набрать" : $"шприц: {Title(_product.Value)} {Amount:0.#}/{_capacity:0} · ЛКМ — давить · ПКМ по таре — выдавить";
            GUI.Label(new Rect(Screen.width * 0.5f - 220f, Screen.height * 0.66f, 440f, 24f), s);
        }

#if UNITY_EDITOR
        public void EditorSetup(Transform body, Transform nozzle, Transform stream)
        {
            _body = body; _nozzle = nozzle; _stream = stream;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
