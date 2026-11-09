using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>
    /// БАНКА С ЖИДКОСТЬЮ ПОРЦИЯМИ (уксус). Как баночка закваски, только льётся струёй.
    ///
    ///   ПКМ — одна порция: рука вытягивает банку над тарой под прицелом, наклоняет,
    ///         льётся короткая струя (своя, цветом продукта), банка возвращается.
    ///         Одно нажатие — одна доза (Portions Per Dose).
    ///   F/G — взять/положить, ЛКМ — бросить (как продукт).
    ///
    /// Куда попало: тара под прицелом (если рука до неё дотянулась), иначе — куда долетит
    /// струя (ближайшая горловина), иначе мимо (в консоли «мимо тары»).
    ///
    /// Сеть: сервер решает, сколько и куда налито; струю и наклон рисует каждый сам по флагу.
    /// Требует: NetworkObject, NetworkTransform, Rigidbody, Collider. Spout — горлышко
    /// (синяя ось наружу). Нет — создастся сам сверху банки.
    /// </summary>
    public class LiquidDoseJarItem : PickupItem
    {
        [Header("Что льёт")]
        [Tooltip("Продукт: Food_Уксус (или масло, соевый соус...)")]
        [SerializeField] private ProductDef _product;
        [Min(1)][SerializeField] private int _capacity = 30;
        [Tooltip("Бесконечная — для тестов")]
        [SerializeField] private bool _infinite = true;
        [Tooltip("Порций за одно нажатие ПКМ")]
        [Min(1)][SerializeField] private int _portionsPerDose = 1;
        [Tooltip("Сколько длится доза целиком (наклон, струя, возврат), с")]
        [Range(0.6f, 3f)][SerializeField] private float _doseSeconds = 1.3f;

        [Header("Рука")]
        [SerializeField] private float _reach = 2.4f;
        [Tooltip("Высота горлышка над горловиной тары, м")]
        [SerializeField] private float _aboveMouth = 0.3f;
        [Tooltip("Мимо тары: банка перед камерой на таком расстоянии, м")]
        [SerializeField] private float _freeDistance = 0.8f;
        [SerializeField] private float _tiltSpeed = 4f;
        [SerializeField] private LayerMask _aimMask = ~0;
        [Tooltip("Наклон горлышка вперёд при наливании (0 — строго вниз)")]
        [Range(0f, 1f)][SerializeField] private float _forwardLean = 0.35f;

        [Header("Струя")]
        [Tooltip("Горлышко: синяя ось (Z) смотрит из банки наружу")]
        [SerializeField] private Transform _spout;
        [SerializeField] private float _streamWidth = 0.02f;
        [SerializeField] private float _pourSpeed = 0.9f;
        [Tooltip("Цвет струи. Прозрачный — Base Color продукта")]
        [SerializeField] private Color _color = new Color(0f, 0f, 0f, 0f);
        [Tooltip("Характер струи. Пусто — по продукту (уксус — как вода)")]
        [SerializeField] private LiquidPourProfile _profile;
        [SerializeField] private float _captureRadius = 0.5f;
        [SerializeField] private LayerMask _surfaceMask = ~0;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly SyncVar<ushort> _left = new SyncVar<ushort>(0);
        private readonly SyncVar<bool> _pouring = new SyncVar<bool>(false);
        private readonly SyncVar<NetworkObject> _target = new SyncVar<NetworkObject>();

        private Cookware _aimCw, _doseCw;
        private float _tilt, _doseT, _startT;
        private bool _delivered, _was;
        private LiquidStreamRibbon _stream;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private bool HasLiquid => _infinite || _left.Value > 0;
        private Transform Spout => _spout != null ? _spout : transform;
        private Color StreamColor => _color.a > 0.01f ? _color : (_product != null ? _product.BaseColor : new Color(0.95f, 0.92f, 0.75f));

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            if (_spout == null) _spout = MakeSpout();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _left.Value = (ushort)Mathf.Clamp(_capacity, 0, ushort.MaxValue);
        }

        private void OnDestroy()
        {
            if (_stream != null) Destroy(_stream.gameObject);
        }

        // ───────────────────────── КАДР ─────────────────────────

        private void Update()
        {
            if (base.IsOwner) OwnerInput();
            bool p = _pouring.Value;
            if (p && !_was) _startT = Time.time;
            _was = p;
            _tilt = Mathf.MoveTowards(_tilt, p ? 1f : 0f, Time.deltaTime * _tiltSpeed);
            if (base.IsServerInitialized) ServerDose(Time.deltaTime);
            UpdateStream();
        }

        private void OwnerInput()
        {
            if (!_pouring.Value) _aimCw = AimCookware();
            if (Input.GetMouseButtonDown(1) && !_pouring.Value)
            {
                _doseCw = _aimCw;
                CmdDose(_doseCw != null ? _doseCw.NetworkObject : null);
            }
        }

        private Cookware AimCookware()
        {
            Camera cam = Camera.main;
            if (cam == null) return null;
            int n = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, _hits, _reach, _aimMask,
                                            QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            Cookware res = null;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].transform.IsChildOf(transform)) continue;
                if (_hits[i].collider.GetComponentInParent<PlayerInteractor>() != null) continue;
                Cookware cw = _hits[i].collider.GetComponentInParent<Cookware>();
                if (cw == null || _hits[i].distance >= best) continue;
                best = _hits[i].distance; res = cw;
            }
            return res;
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdDose(NetworkObject target)
        {
            if (_pouring.Value || !HasLiquid || _product == null) return;
            _target.Value = target;
            _pouring.Value = true;
            _doseT = 0f;
            _delivered = false;
        }

        public override void OnOwnershipServer(FishNet.Connection.NetworkConnection prevOwner)
        {
            base.OnOwnershipServer(prevOwner);
            if (_pouring.Value) _pouring.Value = false;
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        private void ServerDose(float dt)
        {
            if (!_pouring.Value) return;
            _doseT += dt;
            if (!_delivered && _doseT >= _doseSeconds * 0.75f)
            {
                _delivered = true;
                int n = 0;
                for (int i = 0; i < _portionsPerDose && HasLiquid; i++)
                {
                    n++;
                    if (!_infinite && _left.Value > 0) _left.Value = (ushort)(_left.Value - 1);
                }
                Deliver(n);
            }
            if (_doseT >= _doseSeconds) _pouring.Value = false;
        }

        private void Deliver(int portions)
        {
            if (portions <= 0) return;
            Cookware cw = null;
            if (_target.Value != null)
            {
                Cookware t = CookwareLookup.Of(_target.Value, Spout.position);
                if (t != null && Vector3.Distance(t.MouthPoint, Spout.position) < Mathf.Max(1.5f, _aboveMouth * 4f)) cw = t;
            }
            if (cw == null && Trace(out Vector3 point, out Cookware direct))
                cw = direct != null ? direct : NearestCookware(point);
            if (cw == null)
            {
                if (_log) Debug.Log($"[Банка] {_product.Title} мимо тары", this);
                return;
            }
            cw.ServerAddPinch(_product.Id, _product.StartState, portions, 0);
            if (_log) Debug.Log($"[Банка] +{portions} {_product.Title} в «{(cw.Def != null ? cw.Def.Title : cw.name)}» " +
                                $"(в таре уже {cw.CountOf(_product.Id)})", this);
        }

        private bool Trace(out Vector3 point, out Cookware direct)
        {
            direct = null;
            Vector3 pos = Spout.position;
            Vector3 vel = Spout.forward * _pourSpeed;
            const float dt = 0.02f;
            for (int i = 0; i < 150; i++)
            {
                vel += Physics.gravity * dt;
                Vector3 next = pos + vel * dt;
                Vector3 seg = next - pos;
                float len = seg.magnitude;
                if (len > 1e-5f)
                {
                    int n = Physics.RaycastNonAlloc(pos, seg / len, _hits, len, _surfaceMask, QueryTriggerInteraction.Collide);
                    float bd = float.MaxValue; bool found = false; RaycastHit bh = default;
                    for (int k = 0; k < n; k++)
                    {
                        RaycastHit h = _hits[k];
                        if (h.transform.IsChildOf(transform)) continue;
                        if (h.collider.GetComponentInParent<PlayerInteractor>() != null) continue;
                        if (h.collider.isTrigger && h.collider.GetComponentInParent<Cookware>() == null) continue;
                        if (h.distance < bd) { bd = h.distance; bh = h; found = true; }
                    }
                    if (found)
                    {
                        point = bh.point;
                        direct = bh.collider.GetComponentInParent<Cookware>();
                        return true;
                    }
                }
                pos = next;
            }
            point = pos;
            return false;
        }

        private Cookware NearestCookware(Vector3 point)
        {
            Cookware best = null;
            float bestD = _captureRadius;
            foreach (Cookware c in Cookware.All)
            {
                if (c == null) continue;
                Vector3 m = c.MouthPoint;
                if (point.y > m.y + 0.2f) continue;
                float d = new Vector2(m.x - point.x, m.z - point.z).magnitude;
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        // ───────────────────────── ВИД ─────────────────────────

        private void UpdateStream()
        {
            float t = Time.time - _startT;
            bool on = _pouring.Value && _tilt > 0.85f && t > _doseSeconds * 0.3f && t < _doseSeconds * 0.85f && HasLiquid;
            if (!on && _stream == null) return;
            if (_stream == null)
            {
                _stream = LiquidStreamRibbon.Create(transform);
                _stream.Profile = _profile != null ? _profile : LiquidPourProfile.ForProduct(_product, StreamColor);
            }
            Transform s = Spout;
            _stream.Tick(on, s.position, s.forward * _pourSpeed, _streamWidth, StreamColor, transform, 30f);
        }

        public override void UpdateHeldPose(Transform handAnchor)
        {
            base.UpdateHeldPose(handAnchor);
            if (_tilt <= 0.001f) return;
            float k = Mathf.SmoothStep(0f, 1f, _tilt);
            Camera cam = Camera.main;
            Vector3 camFwd = cam != null ? cam.transform.forward : transform.forward;
            Cookware aimCw = _pouring.Value ? (_doseCw != null ? _doseCw : TargetCw()) : _aimCw;
            Vector3 aimPos;
            if (aimCw != null) aimPos = aimCw.MouthPoint + Vector3.up * _aboveMouth;
            else if (cam != null) aimPos = cam.transform.position + camFwd * _freeDistance + Vector3.down * 0.15f;
            else aimPos = transform.position;

            Vector3 flat = Vector3.ProjectOnPlane(camFwd, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();
            Vector3 downDir = (Vector3.down + flat * _forwardLean).normalized;
            Quaternion turn = Quaternion.FromToRotation(Spout.forward, downDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, turn * transform.rotation, k);
            transform.position += (aimPos - Spout.position) * k;
        }

        private Cookware TargetCw() => CookwareLookup.Of(_target.Value, transform.position);

        private Transform MakeSpout()
        {
            bool any = false; Bounds b = default;
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            var go = new GameObject("Spout (auto)");
            go.transform.SetParent(transform, false);
            Vector3 top = any ? new Vector3(b.center.x, b.max.y, b.center.z) : transform.position + Vector3.up * 0.1f;
            go.transform.SetPositionAndRotation(top, Quaternion.LookRotation(transform.up, -transform.forward));
            return go.transform;
        }

        private void OnGUI()
        {
            if (!base.IsOwner || _product == null || _pouring.Value) return;
            Cookware cw = _aimCw;
            string t = cw != null
                ? $"ПКМ — налить {_product.Title} в «{(cw.Def != null ? cw.Def.Title : cw.name)}» (там уже {cw.CountOf(_product.Id)})"
                : $"ПКМ — налить {_product.Title} (наведись на тару)";
            if (!_infinite) t += $"  · в банке {_left.Value}";
            var st = new GUIStyle(GUI.skin.box) { fontSize = 14 };
            Vector2 size = st.CalcSize(new GUIContent(t));
            GUI.Box(new Rect(Screen.width * 0.5f - size.x * 0.5f - 6f, Screen.height * 0.5f + 34f, size.x + 12f, 24f), t, st);
        }
    }
}
