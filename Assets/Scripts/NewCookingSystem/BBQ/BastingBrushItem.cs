using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// КИСТЬ (толстая строительная) — мазать рёбра соусом.
    ///   ЛКМ по таре с «Глазурью барбекю» (ведро, кастрюля, ковш) — макнуть: кисть набирает соус.
    ///   ЛКМ зажать на рёбрах и водить мышью — мажешь (камера стоит). Кончился — макни снова.
    ///   Простой «Соус барбекю» из банки тоже мажется, но оценка ниже, чем за свою глазурь.
    ///   ПАМПУШКИ (борщ): макни в «Масло чесночное с зеленью» → ЛКМ зажать на пампушках и водить —
    ///   каждая, по которой прошёл, становится «Пампушка с чесноком» (½ порции масла на штуку).
    /// ═══ НА КОРНЕ ═══ NetworkObject · NetworkTransform · Rigidbody · коллайдер · этот компонент.
    /// Bristles — дочерний рендер щетины (краснеет, когда в соусе).
    /// </summary>
    public sealed class BastingBrushItem : PickupItem
    {
        [SerializeField] private Renderer _bristles;
        [Tooltip("Сколько порций соуса набирает за раз")]
        [SerializeField] private float _load = 3f;
        [SerializeField] private float _reach = 3f;
        [SerializeField] private ProductDef _glaze;
        [SerializeField] private ProductDef _sauce;
        [Tooltip("Горчица — для брискета (до специй)")]
        [SerializeField] private ProductDef _mustard;
        [Tooltip("Чесночное масло — мазать пампушки. Пусто — «Масло чесночное с зеленью» по названию")]
        [SerializeField] private ProductDef _garlicOil;
        [Tooltip("Во что превращается пампушка. Пусто — «Пампушка с чесноком» по названию")]
        [SerializeField] private ProductDef _bunGlazed;
        [SerializeField] private bool _log = true;

        private readonly SyncVar<ushort> _product = new SyncVar<ushort>(0);
        private readonly SyncVar<ushort> _amount = new SyncVar<ushort>(0);   // сотые

        private readonly HeldToolLook _look = new HeldToolLook();
        private readonly RackStroke _stroke = new RackStroke();
        private MaterialPropertyBlock _mpb;
        private Color _dryColor = new Color(0.85f, 0.78f, 0.62f);
        private bool _colorRead;

        public float Amount => _amount.Value / 100f;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            if (_bristles == null) { Transform b = transform.Find("Bristles"); if (b != null) _bristles = b.GetComponent<Renderer>(); }
        }

        private void Resolve()
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return;
            if (_glaze == null) _glaze = MacaronTitles.Find(db, BBQTitles.Glaze);
            if (_sauce == null) _sauce = MacaronTitles.Find(db, BBQTitles.Sauce);
            if (_mustard == null) _mustard = MacaronTitles.Find(db, BBQTitles.Mustard);
            if (_garlicOil == null) _garlicOil = MacaronTitles.Find(db, "Масло чесночное с зеленью");
            if (_bunGlazed == null) _bunGlazed = MacaronTitles.Find(db, "Пампушка с чесноком");
        }

        private bool _dabbing;
        private float _dabAt;

        private static bool IsBun(FoodPiece p)
        {
            ProductDef d = p != null ? p.Def : null;
            return d != null && !string.IsNullOrEmpty(d.Title) && d.Title.ToLowerInvariant().Contains("пампушк");
        }

        private void Update()
        {
            if (!base.IsOwner || !_look.Held) { if (_stroke.Active) _stroke.End(); _dabbing = false; return; }
            Camera cam = Camera.main;
            if (cam == null) return;
            // пампушки: ЛКМ зажата — мажем каждую под прицелом
            if (_dabbing)
            {
                if (!Input.GetMouseButton(0)) { _dabbing = false; return; }
                if (Time.time >= _dabAt && ToolRay.Cast(cam, _reach, transform, out RaycastHit dh, QueryTriggerInteraction.Ignore))
                {
                    _dabAt = Time.time + 0.12f;
                    FoodPiece bp = dh.collider.GetComponentInParent<FoodPiece>();
                    if (IsBun(bp)) CmdGlaze(bp.NetworkObject);
                }
                return;
            }
            if (_stroke.Active)
            {
                if (!Input.GetMouseButton(0)) { _stroke.End(); return; }
                if (_stroke.Tick(cam, out float st) && _stroke.Rack != null)
                    CmdPaint(_stroke.Rack.NetworkObject, _stroke.Local, (byte)_stroke.Side, st);
                return;
            }
            if (!Input.GetMouseButtonDown(0)) return;
            if (_stroke.TryBegin(cam, _reach, null, out RibsRack aimed, transform))
            {
                if (Amount < 0.02f) { _stroke.End(); if (_log) Debug.Log("[Кисть] сухая — макни в глазурь (ЛКМ по ведру/кастрюле)", this); }
                return;
            }
            if (aimed != null) return;
            if (ToolRay.Cast(cam, _reach, transform, out RaycastHit bh, QueryTriggerInteraction.Ignore))
            {
                FoodPiece bp = bh.collider.GetComponentInParent<FoodPiece>();
                DoughBlob raw = bp != null ? bp.GetComponent<DoughBlob>() : null;
                if (raw != null && raw.IsPiece && !raw.IsBaked && _garlicOil != null && _product.Value == _garlicOil.Id)
                {
                    if (_log) Debug.Log("[Кисть] это ещё тесто — сначала испеки пампушки, потом мажь (на решётке, на столе или в миске — где угодно)", this);
                    return;
                }
                if (IsBun(bp))
                {
                    if (Amount < 0.02f) { if (_log) Debug.Log("[Кисть] сухая — макни в чесночное масло (ЛКМ по таре)", this); return; }
                    _dabbing = true; _dabAt = 0f;
                    return;
                }
            }
            if (ToolRay.Cast(cam, _reach, transform, out RaycastHit hit, QueryTriggerInteraction.Collide))
            {
                Cookware cw = hit.collider.GetComponentInParent<Cookware>();
                if (cw != null) CmdDip(cw.NetworkObject);
            }
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdDip(NetworkObject target)
        {
            if (target == null) return;
            Cookware cw = target.GetComponent<Cookware>();
            if (cw == null) cw = target.GetComponentInChildren<Cookware>();
            if (cw == null) return;
            Resolve();
            ushort pick = 0;
            if (_glaze != null && cw.CountOf(_glaze.Id) > 0) pick = _glaze.Id;
            else if (_sauce != null && cw.CountOf(_sauce.Id) > 0) pick = _sauce.Id;
            else if (_mustard != null && cw.CountOf(_mustard.Id) > 0) pick = _mustard.Id;
            else if (_garlicOil != null && cw.CountOf(_garlicOil.Id) > 0) pick = _garlicOil.Id;
            if (pick == 0) { if (_log) Debug.Log($"[Кисть] в «{(cw.Def != null ? cw.Def.Title : cw.name)}» нет глазури, соуса барбекю, горчицы или чесночного масла", this); return; }
            if (_product.Value != 0 && _product.Value != pick && Amount > 0.05f) { if (_log) Debug.Log("[Кисть] на кисти другой соус — сначала домажь", this); return; }
            float room = _load - Amount;
            if (room < 0.5f) { if (_log) Debug.Log("[Кисть] и так полная", this); return; }
            int took = cw.ServerTake(pick, Mathf.Max(1, Mathf.FloorToInt(room)));
            if (took <= 0) return;
            _product.Value = pick;
            _amount.Value = (ushort)Mathf.RoundToInt(Mathf.Min(_load, Amount + took) * 100f);
            string what = _glaze != null && pick == _glaze.Id ? "Глазурь барбекю" : _mustard != null && pick == _mustard.Id ? "Горчица"
                        : _garlicOil != null && pick == _garlicOil.Id ? "Масло чесночное (ЛКМ по пампушкам и води)" : "Соус барбекю";
            if (_log) Debug.Log($"[Кисть] макнул: {Amount:0.0} порц. «{what}» — ЛКМ по мясу и води мышью", this);
        }

        /// <summary>Мазнуть пампушку чесночным маслом: «Пампушка» → «Пампушка с чесноком». Сервер.</summary>
        [ServerRpc(RequireOwnership = true)]
        private void CmdGlaze(NetworkObject target)
        {
            if (target == null) return;
            FoodPiece p = target.GetComponent<FoodPiece>();
            if (p == null || p.IsConsumed || !IsBun(p) || p.Def.Title.ToLowerInvariant().Contains("чесно")) return;
            if ((p.transform.position - transform.position).sqrMagnitude > (_reach + 2f) * (_reach + 2f)) return;
            Resolve();
            if (_garlicOil == null || _product.Value != _garlicOil.Id || Amount < 0.02f)
            {
                if (_log) Debug.Log("[Кисть] на кисти нет чесночного масла — макни в «Масло чесночное с зеленью»", this);
                return;
            }
            if (_bunGlazed == null) { if (_log) Debug.LogWarning("[Кисть] нет продукта «Пампушка с чесноком» — Борщ → 13", this); return; }
            p.ServerSetProductState(_bunGlazed, p.State);
            _amount.Value = (ushort)Mathf.Max(0, Mathf.RoundToInt((Amount - 0.5f) * 100f));
            if (_amount.Value < 2) { _amount.Value = 0; _product.Value = 0; }
            if (_log) Debug.Log($"[Кисть] пампушка намазана чесночным маслом — «{_bunGlazed.Title}»" + (_amount.Value == 0 ? " · масло кончилось, макни ещё" : $" · на кисти {Amount:0.0}"), this);
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdPaint(NetworkObject rack, Vector3 local, byte side, float stroke)
        {
            if (rack == null || Amount < 0.02f) return;
            RibsRack r = rack.GetComponent<RibsRack>();
            if (r == null || (r.transform.position - transform.position).sqrMagnitude > 36f) return;
            float used = r.ServerPaintAt(local, side, Mathf.Min(stroke, 0.5f), Amount, _product.Value);
            if (used <= 0f) return;
            _amount.Value = (ushort)Mathf.Max(0, Mathf.RoundToInt((Amount - used) * 100f));
            if (_amount.Value < 2) { _amount.Value = 0; _product.Value = 0; if (_log) Debug.Log("[Кисть] соус кончился — макни ещё", this); }
        }

        public override void UpdateHeldPose(Transform handAnchor)
        {
            _look.Pose(handAnchor);
            base.UpdateHeldPose(handAnchor);
            if (_stroke.Active) _stroke.Pose(transform, 0.06f);
        }

        private void LateUpdate()
        {
            if (base.IsOwner) _look.Late(_stroke.Active, _stroke.Active);
            TintBristles();
        }

        public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
        {
            _look.Release();
            base.ServerOnDrop(hand, tossDirection, force);
        }

        private void TintBristles()
        {
            if (_bristles == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            if (!_colorRead && _bristles.sharedMaterial != null)
            {
                _colorRead = true;
                Material m = _bristles.sharedMaterial;
                if (m.HasProperty("_BaseColor")) _dryColor = m.GetColor("_BaseColor");
                else if (m.HasProperty("_Color")) _dryColor = m.GetColor("_Color");
            }
            FoodDatabase db = FoodDatabase.Instance;
            ProductDef d = db != null && _product.Value != 0 ? db.Get(_product.Value) : null;
            float k = Mathf.Clamp01(Amount / Mathf.Max(0.1f, _load) * 1.5f);
            Color c = Color.Lerp(_dryColor, d != null ? d.BaseColor : _dryColor, k);
            _bristles.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            _bristles.SetPropertyBlock(_mpb);
        }

        private void OnGUI()
        {
            if (!base.IsOwner || !_look.Held || Event.current.type != EventType.Repaint) return;
            string s = Amount < 0.02f ? "кисть сухая — ЛКМ по таре с глазурью: макнуть" : $"кисть в соусе {Amount:0.#}/{_load:0} · ЛКМ по рёбрам и води мышью";
            GUI.Label(new Rect(Screen.width * 0.5f - 220f, Screen.height * 0.66f, 440f, 24f), s);
        }

#if UNITY_EDITOR
        public void EditorSetup(Renderer bristles) { _bristles = bristles; UnityEditor.EditorUtility.SetDirty(this); }
#endif
    }
}
