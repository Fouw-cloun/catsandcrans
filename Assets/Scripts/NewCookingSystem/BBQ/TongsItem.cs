using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// КЛЕЩИ — горячее руками не взять, клещами можно.
    ///   ЛКМ по рёбрам — перевернуть (любые, и горячие).
    ///   ПКМ по рёбрам — взять (повиснут на клещах); ещё раз ПКМ — отпустить.
    ///   Взятые: ЛКМ — поставить ровно (смотришь на решётку духовки или на стол).
    /// ═══ НА КОРНЕ ═══ NetworkObject · NetworkTransform · Rigidbody · коллайдер · этот компонент.
    /// Tip — дочерняя точка между губками (меню ставит на дальний конец модели).
    /// Jaw A / Jaw B — по желанию: губки, сходятся, когда что-то держишь.
    /// </summary>
    public sealed class TongsItem : PickupItem
    {
        [SerializeField] private Transform _tip;
        [Tooltip("Губки (по желанию) — сходятся (поворот вокруг своей Y), когда держишь")]
        [SerializeField] private Transform _jawA, _jawB;
        [SerializeField] private float _jawClose = 5f;
        [SerializeField] private float _reach = 3f;
        [SerializeField] private bool _log = true;

        private readonly SyncVar<NetworkObject> _holding = new SyncVar<NetworkObject>(null);
        private Quaternion _jawARest, _jawBRest;
        private float _jaw;

        public Transform Tip => _tip != null ? _tip : transform;
        public bool Holding => _holding.Value != null;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            if (_tip == null) _tip = transform.Find("Tip");
            if (_jawA != null) _jawARest = _jawA.localRotation;
            if (_jawB != null) _jawBRest = _jawB.localRotation;
        }

        private RibsRack AimRack(UseContext ctx)
        {
            Vector3 dir = ctx.Direction.sqrMagnitude > 1e-6f ? ctx.Direction.normalized : transform.forward;
            RaycastHit[] hits = Physics.RaycastAll(ctx.Origin, dir, _reach, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; RibsRack res = null;
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null || h.collider.transform.IsChildOf(transform) || h.distance >= best) continue;
                RibsRack r = h.collider.GetComponentInParent<RibsRack>();
                if (r == null) continue;
                best = h.distance; res = r;
            }
            return res;
        }

        /// <summary>ЛКМ: перевернуть рёбра / поставить взятые.</summary>
        public override void ServerPrimaryUse(UseContext ctx)
        {
            RibsRack held = HeldRack();
            if (held != null)
            {
                if (GrillPlacement.Find(ctx, held.transform, _reach, out GrillPlacement.Spot spot))
                {
                    held.ServerRelease();
                    _holding.Value = null;
                    held.PlaceFlat(spot);
                    if (_log) Debug.Log($"[Клещи] поставил «{held.name}»" + (spot.Oven != null ? $" на решётку «{spot.Oven.name}»" : ""), this);
                }
                else if (_log) Debug.Log("[Клещи] некуда поставить — смотри на решётку духовки или на стол (ПКМ — просто отпустить)", this);
                return;
            }
            RibsRack r = AimRack(ctx);
            if (r == null) { if (_log) Debug.Log("[Клещи] ЛКМ по рёбрам — перевернуть, ПКМ — взять", this); return; }
            r.ServerFlip("клещами");
        }

        /// <summary>ПКМ: взять / отпустить.</summary>
        public override void ServerSecondaryUse(UseContext ctx)
        {
            RibsRack held = HeldRack();
            if (held != null)
            {
                held.ServerRelease();
                _holding.Value = null;
                if (_log) Debug.Log($"[Клещи] отпустил «{held.name}»", this);
                return;
            }
            RibsRack r = AimRack(ctx);
            if (r == null) { if (_log) Debug.Log("[Клещи] ПКМ по рёбрам — взять (горячие тоже)", this); return; }
            if (r.ServerGrab(base.NetworkObject)) _holding.Value = r.NetworkObject;
        }

        private RibsRack HeldRack()
        {
            NetworkObject h = _holding.Value;
            if (h == null) return null;
            RibsRack r = h.GetComponent<RibsRack>();
            if (r == null || r.GrabbedBy != base.NetworkObject) { _holding.Value = null; return null; }
            return r;
        }

        public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
        {
            RibsRack held = HeldRack();
            if (held != null) { held.ServerRelease(); _holding.Value = null; }
            base.ServerOnDrop(hand, tossDirection, force);
        }

        private void LateUpdate()
        {
            _jaw = Mathf.MoveTowards(_jaw, Holding ? 1f : 0f, Time.deltaTime * 6f);
            if (_jawA != null) _jawA.localRotation = _jawARest * Quaternion.Euler(0f, _jawClose * _jaw, 0f);
            if (_jawB != null) _jawB.localRotation = _jawBRest * Quaternion.Euler(0f, -_jawClose * _jaw, 0f);
        }

#if UNITY_EDITOR
        public void EditorSetup(Transform tip) { _tip = tip; UnityEditor.EditorUtility.SetDirty(this); }
#endif
    }
}
