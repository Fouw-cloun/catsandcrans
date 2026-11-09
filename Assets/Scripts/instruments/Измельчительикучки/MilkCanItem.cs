using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// В пространстве имён НАМЕРЕННО: в проекте ещё живёт старая система.
namespace CatCooks.Food.Tools
{
    /// <summary>
    /// БИДОН С МОЛОКОМ.
    ///
    /// Держишь ПКМ — бидон наклоняется, из носика льётся струя. Лить можно где
    /// угодно:
    ///   • струя падает в тару (или рядом с горловиной тары в пределах
    ///     Capture Radius) — молоко идёт в ближайшую тару;
    ///   • тары нет — на полу растёт лужа (LiquidPuddle): брызги, рябь, блик.
    ///
    /// Струя честно вылетает из носика по его синей оси (Z) со скоростью Pour
    /// Speed и падает по дуге. Куда упала — решает и сервер (молоко), и каждый
    /// игрок (брызги) одинаково.
    ///
    /// Если бидон наклоняется не в ту сторону — поменяй знак Tilt Angle или ось Tilt Axis.
    ///
    /// v2 (струи Pour v1): характер струи — LiquidPourProfile (пусто — молоко по цвету);
    /// горло бидона широкое — струя может идти «языком» шириной Neck Width; пока
    /// бидон доклоняется, струя нарастает, а не появляется сразу; пустой бидон не
    /// льёт и визуально. Сервер считает дугу той же скоростью, что и струя-меш.
    ///
    /// ═══ ТРЕБУЕТ ═══
    /// NetworkObject, NetworkTransform (Client Authoritative), Rigidbody (масса 15–25), Collider.
    /// Spout — дочерний объект у носика, СИНЯЯ ось (Z) смотрит из носика наружу.
    /// </summary>
    public class MilkCanItem : PickupItem
    {
        [Header("Что льёт")]
        [SerializeField] private ProductDef _milk;
        [Min(1)][SerializeField] private int _capacity = 40;
        [Tooltip("Бесконечный — для тестов")]
        [SerializeField] private bool _infinite = true;
        [Tooltip("Порций в секунду")]
        [Min(0.1f)][SerializeField] private float _pourRate = 2f;

        [Header("Наклон")]
        [Tooltip("Вокруг какой оси бидона наклоняется (в осях бидона). Обычно X — красная.")]
        [SerializeField] private Vector3 _tiltAxis = Vector3.right;
        [Tooltip("Угол наклона, градусы. Льёт назад, на себя — поставь со знаком минус.")]
        [SerializeField] private float _tiltAngle = 65f;
        [Tooltip("Скорость наклона")]
        [SerializeField] private float _tiltSpeed = 3f;

        [Header("Куда льётся")]
        [Tooltip("Носик: СИНЯЯ ось (Z) смотрит из носика наружу")]
        [SerializeField] private Transform _spout;
        [Tooltip("Скорость струи из носика, м/с")]
        [SerializeField] private float _pourSpeed = 1.6f;
        [Tooltip("Тара, горловина которой ближе этого к месту падения струи, м, — ловит молоко")]
        [SerializeField] private float _captureRadius = 0.6f;
        [Tooltip("Обо что струя разбивается (пол, стол, тара)")]
        [SerializeField] private LayerMask _surfaceMask = ~0;
        [Tooltip("Префаб лужи (NetworkObject + LiquidPuddle). Пусто — на пол не льётся.")]
        [SerializeField] private LiquidPuddle _puddlePrefab;

        [Header("Вид")]
        [Tooltip("Цвет струи берётся из продукта (Base Color у Food_Молоко). Выключи — будет Milk Color ниже")]
        [SerializeField] private bool _colorFromProduct = true;
        [Tooltip("Свой цвет струи — только если Color From Product выключен")]
        [SerializeField] private Color _milkColor = new Color(0.97f, 0.97f, 0.94f);
        [Tooltip("Толщина струи у носика, м")]
        [SerializeField] private float _streamWidth = 0.05f;
        [Tooltip("Брызг в секунду там, куда падает струя")]
        [SerializeField] private float _splashPerSecond = 50f;
        [Tooltip("Принудительный характер струи. Обычно ПУСТО — берётся по продукту (молоко → Milk)")]
        [SerializeField] private LiquidPourProfile _pourProfile;
        [Tooltip("Ширина «языка» с края горла, м. 0 — круглая струя. Бидон: 0.04–0.07")]
        [Range(0f, 0.2f)][SerializeField] private float _neckWidth = 0.05f;
        [Tooltip("С какой доли наклона (0..1) начинает литься. Выше — струя нарастает до полной")]
        [Range(0.2f, 0.95f)][SerializeField] private float _startTilt = 0.6f;


        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly SyncVar<ushort> _left = new SyncVar<ushort>(0);
        private readonly SyncVar<bool> _pouring = new SyncVar<bool>(false);

        private float _pourAcc;
        private float _tilt;
        private bool _rmbWas;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _left.Value = (ushort)Mathf.Clamp(_capacity, 0, ushort.MaxValue);
        }

        private void Update()
        {
            if (base.IsOwner) OwnerInput();
            _tilt = Mathf.MoveTowards(_tilt, _pouring.Value ? 1f : 0f, Time.deltaTime * _tiltSpeed);
            TrackSpoutVelocity();
            if (base.IsServerInitialized) ServerPour();
            UpdateVisual();
        }

        private void OwnerInput()
        {
            bool rmb = Input.GetMouseButton(1);
            if (rmb != _rmbWas) { _rmbWas = rmb; CmdPour(rmb); }
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdPour(bool on)
        {
            if (_pouring.Value != on) _pouring.Value = on;
            _pourAcc = 0f;
        }

        public override void OnOwnershipServer(FishNet.Connection.NetworkConnection prevOwner)
        {
            base.OnOwnershipServer(prevOwner);
            if (_pouring.Value) _pouring.Value = false;      // бросили — перестал лить
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        private void ServerPour()
        {
            if (!_pouring.Value || _milk == null || _tilt < _startTilt) return;
            if (!HasMilk) return;

            _pourAcc += Time.deltaTime * _pourRate * Flow01;
            while (_pourAcc >= 1f)
            {
                _pourAcc -= 1f;
                Deliver();
                if (!_infinite && _left.Value > 0) _left.Value = (ushort)(_left.Value - 1);
            }
        }

        private void Deliver()
        {
            if (!Trace(out Vector3 point, out Cookware direct)) return;
            Cookware cw = direct != null ? direct : NearestCookware(point);
            if (cw != null)
            {
                cw.ServerAddContent(_milk.Id, _milk.StartState, 1, 0);
                cw.ServerAgitate(0.35f);
                if (_log && cw.Def != null) Debug.Log($"[Бидон] +1 молоко в «{cw.Def.Title}»", this);
                return;
            }
            if (_puddlePrefab != null)
            {
                LiquidPuddle.ServerAddAt(_puddlePrefab, _milk.Id, point, 1);
                if (_log) Debug.Log("[Бидон] +1 молоко на пол", this);
            }
        }

        // ───────────────────────── КУДА ПАДАЕТ СТРУЯ ─────────────────────────

        /// <summary>Проследить дугу струи от носика. Попала в тару — вернуть её.</summary>
        // Скорость носика (игрок идёт с бидоном) — струя-меш её наследует, сервер тоже.
        private Vector3 _spoutLast, _spoutVel;
        private bool _spoutHas;

        private void TrackSpoutVelocity()
        {
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            Vector3 p = (_spout != null ? _spout : transform).position;
            if (_spoutHas)
            {
                Vector3 v = (p - _spoutLast) / dt;
                if (v.sqrMagnitude > 100f) v = _spoutVel;                // телепорт
                _spoutVel = Vector3.Lerp(_spoutVel, v, 1f - Mathf.Exp(-dt / 0.05f));
            }
            _spoutLast = p; _spoutHas = true;
        }

        private bool HasMilk => _infinite || _left.Value > 0;

        /// <summary>0..1 — насколько бидон докланился после точки, где начинает литься.</summary>
        private float Flow01 => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_startTilt, 1f, _tilt) * 0.7f + 0.3f);

        /// <summary>Цвет струи: из продукта или свой.</summary>
        private Color StreamColor => _colorFromProduct && _milk != null ? _milk.BaseColor : _milkColor;

        /// <summary>Характер струи: принудительный или по продукту.</summary>
        private LiquidPourProfile Profile
            => _pourProfile != null ? _pourProfile : LiquidPourProfile.ForProduct(_milk, StreamColor);

        /// <summary>Скорость вылета из носика — та же, что получает струя-меш (с вязкостью).</summary>
        private Vector3 SpoutVelocity(Transform s) => s.forward * (_pourSpeed * Mathf.Lerp(0.6f, 1f, Flow01));

        private bool Trace(out Vector3 point, out Cookware direct)
        {
            direct = null;
            Transform s = _spout != null ? _spout : transform;
            Vector3 pos = s.position;
            Vector3 vel = SpoutVelocity(s) * Profile.SpeedScale + _spoutVel * 0.85f;
            const float dt = 0.02f;
            for (int i = 0; i < 150; i++)
            {
                Vector3 next = pos + vel * dt + 0.5f * Physics.gravity * dt * dt;
                vel += Physics.gravity * dt;
                Vector3 seg = next - pos;
                float len = seg.magnitude;
                if (len > 1e-5f && NearestHit(pos, seg / len, len, out RaycastHit h))
                {
                    point = h.point;
                    direct = h.collider.GetComponentInParent<Cookware>();
                    return true;
                }
                pos = next;
            }
            point = pos;
            return false;
        }

        private bool NearestHit(Vector3 from, Vector3 dir, float len, out RaycastHit best)
        {
            int n = Physics.RaycastNonAlloc(from, dir, _hits, len, _surfaceMask, QueryTriggerInteraction.Collide);
            best = default;
            float bestD = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.transform.IsChildOf(transform)) continue;                            // сам бидон
                if (h.collider.GetComponentInParent<PlayerInteractor>() != null) continue;   // игрок
                if (h.collider.isTrigger && h.collider.GetComponentInParent<Cookware>() == null) continue;
                if (h.collider.GetComponentInParent<LiquidPuddle>() != null) continue;
                if (h.distance >= bestD) continue;
                bestD = h.distance; best = h; found = true;
            }
            return found;
        }

        /// <summary>Ближайшая тара, горловина которой рядом с местом падения.</summary>
        private Cookware NearestCookware(Vector3 point)
        {
            Cookware best = null;
            float bestD = _captureRadius;
            foreach (Cookware c in Cookware.All)
            {
                if (c == null) continue;
                Vector3 m = c.MouthPoint;
                if (point.y > m.y + 0.2f) continue;                                // струя упала выше тары — мимо
                float d = new Vector2(m.x - point.x, m.z - point.z).magnitude;
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        // ───────────────────────── ВИД (у всех) ─────────────────────────

        private LiquidStreamRibbon _ribbon;

        private void UpdateVisual()
        {
            bool on = _pouring.Value && _tilt >= _startTilt && HasMilk;
            if (on && _ribbon == null) _ribbon = LiquidStreamRibbon.Create(transform);
            if (_ribbon == null) return;

            Transform s = _spout != null ? _spout : transform;
            float f = on ? Flow01 : 0f;
            float width = _streamWidth * Mathf.Lerp(0.45f, 1f, f);
            // Широкое горло: «язык» с края, поперёк — красная ось носика.
            _ribbon.Profile = Profile;
            if (_neckWidth > 0f) _ribbon.SetLip(s.right, Mathf.Max(width, _neckWidth * f));
            // Струя-меш из носика по той же дуге, что и расчёт сервера (вязкость ribbon учтёт сам).
            _ribbon.Tick(on, s.position, SpoutVelocity(s), width, StreamColor, transform, _splashPerSecond * f);
            if (!on || !_ribbon.Landed) return;

            // Куда упала: круги на поверхности тары или на луже.
            Vector3 p = _ribbon.LandingPoint;
            Cookware c = NearestCookware(p);
            CookwareLiquidVisual lv = c != null ? c.GetComponentInChildren<CookwareLiquidVisual>() : null;
            if (lv != null) lv.Ripple(p);
            else
            {
                LiquidPuddle near = LiquidPuddle.FindNear(p);
                if (near != null) near.Ripple(p);
            }
        }

        private void OnDestroy()
        {
            if (_ribbon != null) Destroy(_ribbon.gameObject);
        }

        public override void UpdateHeldPose(Transform handAnchor)
        {
            // Базовая поза в руке — от PickupItem (его Hold Offset), к ней — наше движение.
            base.UpdateHeldPose(handAnchor);
            Vector3 pos = transform.position;
            Quaternion rot = transform.rotation;
            if (_tilt > 0.001f)
            {
                Vector3 axis = _tiltAxis.sqrMagnitude > 1e-6f ? _tiltAxis.normalized : Vector3.right;
                rot *= Quaternion.AngleAxis(_tiltAngle * Mathf.SmoothStep(0f, 1f, _tilt), axis);
            }
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
