using FishNet.Object;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// РУЛОН ПЕНОФОЛА (фольгированный утеплитель со стройки) — вместо фольги для брискета.
    ///   ЛКМ ДЕРЖАТЬ на брискете ~1.5 с — заворачиваешь (полоска прогресса):
    ///     1-й раз — «лодочка»: дно и бортики, верх открыт (томится и коптится, корочка растёт);
    ///     2-й раз — полностью (когда корочка почти чёрная): доходит до готовности, отдыхает.
    ///   ПКМ по брискету — развернуть (после отдыха — и резать).
    /// ═══ НА КОРНЕ ═══ NetworkObject · NetworkTransform · Rigidbody · коллайдер · этот компонент.
    /// Модель — любая (меню CatCooks → Брискет → 3 сделает рулон, если ничего не выделено).
    /// </summary>
    public sealed class FoilRollItem : PickupItem
    {
        [SerializeField] private float _reach = 3f;
        [Tooltip("Сколько секунд держать ЛКМ, чтобы завернуть")]
        [SerializeField] private float _holdSeconds = 1.5f;
        [Tooltip("Рулон (дочерний) — крутится, пока заворачиваешь")]
        [SerializeField] private Transform _spin;
        [SerializeField] private bool _log = true;

        private readonly HeldToolLook _look = new HeldToolLook();
        private RibsRack _target;
        private float _t;
        private bool _waitRelease;
        private Vector3 _aimPoint;
        private static GUIStyle _style;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            if (_spin == null) _spin = transform.Find("Roll");
        }

        private RibsRack Aim(Camera cam, out RaycastHit hit)
        {
            hit = default(RaycastHit);
            if (cam == null || !ToolRay.Cast(cam, _reach, transform, out hit)) return null;
            if (hit.collider == null) return null;
            return hit.collider.GetComponentInParent<RibsRack>();
        }

        private void Update()
        {
            if (!base.IsOwner || !_look.Held) { _target = null; _t = 0f; return; }
            Camera cam = Camera.main;
            if (cam == null) return;
            if (_waitRelease) { if (!Input.GetMouseButton(0)) _waitRelease = false; return; }

            if (Input.GetMouseButtonDown(1))
            {
                RibsRack r = Aim(cam, out _);
                if (r != null && r.IsBrisket) CmdUnwrap(r.NetworkObject);
                else if (_log) Debug.Log("[Пенофол] ПКМ по брискету — развернуть", this);
                return;
            }

            if (Input.GetMouseButton(0))
            {
                RibsRack r = Aim(cam, out RaycastHit h);
                if (r == null || !r.IsBrisket)
                {
                    if (Input.GetMouseButtonDown(0) && _log) Debug.Log("[Пенофол] держи ЛКМ на брискете — завернуть (1-й раз лодочкой, 2-й — полностью)", this);
                    _target = null; _t = 0f;
                    return;
                }
                if (r != _target) { _target = r; _t = 0f; }
                _aimPoint = h.point;
                _t += Time.deltaTime;
                if (_t >= _holdSeconds)
                {
                    CmdWrap(r.NetworkObject);
                    _t = 0f; _target = null; _waitRelease = true;
                }
            }
            else { _target = null; _t = 0f; }
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdWrap(NetworkObject rack)
        {
            if (rack == null) return;
            RibsRack r = rack.GetComponent<RibsRack>();
            if (r == null || (r.transform.position - transform.position).sqrMagnitude > 36f) return;
            bool ok = r.ServerWrap(out string hint);
            if (_log && hint != null) Debug.Log("[Пенофол] " + hint, this);
            if (_log && ok) Debug.Log($"[Пенофол] «{r.name}»: {(r.Wrap == 1 ? "лодочка — верх открыт" : "завёрнут полностью")}", this);
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdUnwrap(NetworkObject rack)
        {
            if (rack == null) return;
            RibsRack r = rack.GetComponent<RibsRack>();
            if (r == null || (r.transform.position - transform.position).sqrMagnitude > 36f) return;
            r.ServerUnwrap(out string hint);
            if (_log && hint != null) Debug.Log("[Пенофол] " + hint, this);
        }

        public override void UpdateHeldPose(Transform handAnchor)
        {
            _look.Pose(handAnchor);
            base.UpdateHeldPose(handAnchor);
            if (_target != null && base.IsOwner)
            {
                // рулон тянется к куску и катится — «заворачиваю»
                transform.position = Vector3.Lerp(transform.position, _aimPoint + Vector3.up * 0.06f, 0.5f);
                if (_spin != null) _spin.Rotate(Vector3.up, 540f * Time.deltaTime, Space.Self);
            }
        }

        private void LateUpdate()
        {
            if (base.IsOwner) _look.Late(false, false);
        }

        public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
        {
            _look.Release();
            base.ServerOnDrop(hand, tossDirection, force);
        }

        private void OnGUI()
        {
            if (!base.IsOwner || !_look.Held || Event.current.type != EventType.Repaint) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold };
            float cx = Screen.width * 0.5f, y = Screen.height * 0.66f;
            string line = "пенофол · ЛКМ держать на брискете — завернуть (лодочка → полностью) · ПКМ — развернуть";
            GUI.Label(new Rect(cx - 360f, y, 720f, 24f), line, _style);
            if (_target == null) return;
            float k = Mathf.Clamp01(_t / Mathf.Max(0.1f, _holdSeconds));
            string what = _target.Wrap == 0 ? "заворачиваю лодочкой…" : _target.Wrap == 1 ? "заворачиваю полностью…" : "уже завёрнут";
            GUI.Label(new Rect(cx - 200f, y + 26f, 400f, 22f), what, _style);
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(new Rect(cx - 121f, y + 50f, 242f, 12f), Texture2D.whiteTexture);
            GUI.color = new Color(0.8f, 0.85f, 0.9f); GUI.DrawTexture(new Rect(cx - 120f, y + 51f, 240f * k, 10f), Texture2D.whiteTexture);
            GUI.color = old;
        }

#if UNITY_EDITOR
        public void EditorSetup(Transform spin) { _spin = spin; UnityEditor.EditorUtility.SetDirty(this); }
#endif
    }
}
