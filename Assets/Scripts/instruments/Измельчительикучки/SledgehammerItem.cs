using FishNet.Object;
using UnityEngine;

// В пространстве имён НАМЕРЕННО: в проекте ещё живёт старая система со
// своими глобальными FoodState и FoodTransition.
namespace CatCooks.Food.Tools
{
    /// <summary>
    /// КУВАЛДА.
    ///
    /// Держишь ЛКМ — замах и удар, снова и снова, пока не отпустишь.
    /// В момент удара бьёт то, на что смотрит прицел (в пределах Range):
    ///
    ///   • у продукта есть правило для процесса кувалды («Разбивать», «Отбивать»):
    ///     орехи колются, чеснок давится, мясо отбивается — продукт переходит
    ///     в следующее состояние по таблице. Удары копятся: у ореха в правиле
    ///     2 секунды — нужно 2–3 удара.
    ///   • правила нет (картошка, овощи): продукт РАЗЛЕТАЕТСЯ вдребезги —
    ///     осколки кожуры и мякоти + брызги пюре, всё тает за пару секунд.
    ///     Сам продукт пропадает.
    ///   • мимо продукта — просто удар об пол: пыль.
    ///
    /// Решает сервер, эффект видят все.
    ///
    /// v13 — НЕ ПРОХОДИТ СКВОЗЬ: боёк каждый кадр проверяется на столкновение (пол, стол, тара,
    /// продукт, стены). Упёрся — кувалда останавливается в точке касания, удар засчитывается
    /// В МОМЕНТ КАСАНИЯ, и кувалда коротко отскакивает назад. Смотришь в пол — висит над полом.
    ///
    /// v13.1 — НА ПЛЕЧЕ (временно, до модели игрока): в покое кувалда лежит на правом плече
    /// бойком вверх-назад, замах — ещё дальше за спину, удар — дугой через верх вниз перед собой.
    /// Ориентация модели определяется сама (где боёк, где конец ручки — по мешу).
    ///
    /// ═══ ТРЕБУЕТ ═══
    /// NetworkObject, NetworkTransform (Client Authoritative), Rigidbody, Collider.
    /// Head — дочерний объект на бойке (оттуда пыль и эффекты). Можно не задавать.
    /// </summary>
    public class SledgehammerItem : PickupItem
    {
        public override bool UseHeldBlocking => false;     // v17.3: своя логика столкновений/позы
        [Header("Что делает")]
        [Tooltip("Процессы кувалды по порядку: первый, для которого у продукта есть правило, и сработает")]
        [SerializeField] private FoodProcess[] _processes = { FoodProcess.Crack, FoodProcess.Pound };
        [Tooltip("Секунд работы за один удар (сравни с секундами правила: у ореха «Разбивать» 2 с — 2 удара)")]
        [Min(0.05f)][SerializeField] private float _workPerHit = 1f;
        [Tooltip("Разбивать вдребезги продукты, для которых правила нет")]
        [SerializeField] private bool _smashOthers = true;

        [Header("Прицел")]
        [SerializeField] private float _range = 2.4f;
        [SerializeField] private LayerMask _foodMask = ~0;

        [Header("Замах")]
        [Tooltip("Замах вверх, с")]
        [SerializeField] private float _windup = 0.32f;
        [Tooltip("Удар вниз, с")]
        [SerializeField] private float _slam = 0.1f;
        [Tooltip("Возврат, с")]
        [SerializeField] private float _recover = 0.25f;
        [Tooltip("Угол замаха вверх, градусы")]
        [SerializeField] private float _windupAngle = 70f;
        [Tooltip("Угол удара вниз, градусы")]
        [SerializeField] private float _slamAngle = 45f;
        [Tooltip("Выпад вперёд при ударе, м")]
        [SerializeField] private float _lunge = 0.25f;

        [Header("Эффекты")]
        [SerializeField] private Transform _head;
        [Tooltip("Сколько осколков и брызг при разбивании")]
        [SerializeField] private int _shards = 18;
        [SerializeField] private int _drops = 10;
        [Tooltip("Материал осколков. Пусто — CatCooks/Food Heap без текстуры.")]
        [SerializeField] private Material _shardMaterial;


        [Header("v13: не проходит сквозь пол и предметы")]
        [Tooltip("Боёк упирается в пол, стол, тару, продукт — дальше не идёт")]
        [SerializeField] private bool _solidHead = true;
        [Tooltip("Радиус бойка для проверки, м")]
        [Range(0.02f, 0.3f)][SerializeField] private float _headRadius = 0.09f;
        [Tooltip("Обо что упирается")]
        [SerializeField] private LayerMask _blockMask = ~0;
        [Tooltip("Отскок после удара, градусы")]
        [Range(0f, 30f)][SerializeField] private float _recoilAngle = 10f;

        [Header("v13.1: на плече")]
        [Tooltip("Держать на плече и бить дугой из-за спины (временно, до модели игрока). Выкл — старая поза")]
        [SerializeField] private bool _shoulderCarry = true;
        [Tooltip("Где рука с кувалдой в покое (от руки-якоря: вправо, вверх, вперёд), м")]
        [SerializeField] private Vector3 _shoulderOffset = new Vector3(0.32f, -0.12f, 0.12f);
        [Tooltip("Где рука в момент удара (вправо, вверх, вперёд), м")]
        [SerializeField] private Vector3 _strikeOffset = new Vector3(0.08f, -0.4f, 0.55f);
        [Tooltip("Угол дуги (0 — боёк вверх, 90 — вперёд, 140 — вниз перед собой): покой, замах, удар")]
        [SerializeField] private Vector3 _arcAngles = new Vector3(-40f, -80f, 135f);
        [Tooltip("Наклон вбок (кувалда лежит на плече под углом), градусы")]
        [Range(-45f, 45f)][SerializeField] private float _shoulderRoll = -12f;

        [Header("v17.5: бьёт в точку прицела")]
        [Tooltip("Удар идёт туда, куда смотришь: поворот к цели, угол дуги и вынос руки подбираются так, чтобы боёк пришёл в точку прицела")]
        [SerializeField] private bool _aimStrike = true;
        [Tooltip("Дальше этого (от камеры) кувалда не достаёт — бьёт в воздух перед собой")]
        [SerializeField] private float _reach = 2f;
        [Tooltip("Насколько рука может вытянуться/подтянуться к цели в ударе, м")]
        [Range(0f, 0.8f)][SerializeField] private float _maxLunge = 0.45f;
        [Tooltip("Множитель пыли при ударе по продукту (какао-боб)")]
        [Range(0.5f, 4f)][SerializeField] private float _foodDust = 2f;
        [Tooltip("v18: разлёт пыли при ударе по продукту (какао-боб): 1 — как по полу, 0.5 — вдвое меньше")]
        [Range(0.1f, 2f)][SerializeField] private float _foodSpread = 0.5f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private float _swingT = -1f;          // −1 — не машем
        private bool _hitDone;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private float SwingLength => _windup + _slam + _recover;

        // v13: боёк
        private Vector3 _headLocal, _gripLocal;
        private bool _headInit;
        private float _prevArc = float.NaN;
        private float _prevPitch, _prevLunge, _recoilT = -1f;
        private bool _contact;
        private readonly Collider[] _blockHits = new Collider[16];
        // v17.5: точка прицела удара
        private Vector3 _aimPt;
        private bool _aimValid;

        private bool AimPoint(out Vector3 p)
        {
            p = Vector3.zero;
            Camera cam = Camera.main;
            if (cam == null) return false;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int n = Physics.RaycastNonAlloc(ray, _hits, _reach, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].transform.IsChildOf(transform) || _hits[i].distance >= best) continue;
                if (_hits[i].collider.GetComponentInParent<CharacterController>() != null) continue;
                best = _hits[i].distance; p = _hits[i].point;
            }
            return best < float.MaxValue;
        }

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
        }

        // ───────────────────────── ВЛАДЕЛЕЦ ─────────────────────────

        private void Update()
        {
            if (_swingT >= 0f)
            {
                _swingT += Time.deltaTime;
                if (base.IsOwner && _swingT < _windup) { Vector3 ap; if (AimPoint(out ap)) { _aimPt = ap; _aimValid = true; } }
                if (base.IsOwner && !_hitDone && _swingT >= _windup + _slam)
                {
                    _hitDone = true;
                    Strike();
                }
                if (_swingT >= SwingLength) _swingT = -1f;
            }

            if (!base.IsOwner) return;
            // Держишь ЛКМ — следующий замах сразу после предыдущего.
            if (_swingT < 0f && Input.GetMouseButton(0))
            {
                StartSwing();
                CmdSwing();
            }
        }

        private void StartSwing()
        {
            if (base.IsOwner) _aimValid = AimPoint(out _aimPt);
            _swingT = 0f;
            _hitDone = false;
            _contact = false;
            _recoilT = -1f;
        }

        /// <summary>Где боёк в осях кувалды: Head или дальний конец мешей.</summary>
        private void InitHead()
        {
            _headInit = true;
            bool any = false;
            Bounds lb = new Bounds(Vector3.zero, Vector3.zero);
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                Bounds wb = r.bounds;
                Vector3 c = wb.center, e = wb.extents;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                    Vector3 l = transform.InverseTransformPoint(corner);
                    if (!any) { lb = new Bounds(l, Vector3.zero); any = true; } else lb.Encapsulate(l);
                }
            }
            if (!any)
            {
                _headLocal = _head != null ? transform.InverseTransformPoint(_head.position) : Vector3.forward * 0.6f;
                _gripLocal = -_headLocal * 0.6f;
                return;
            }
            // самая длинная ось — рукоять; боёк — на дальнем от ручки (точки объекта) конце
            Vector3 ext = lb.extents;
            int ax = ext.x >= ext.y && ext.x >= ext.z ? 0 : ext.y >= ext.z ? 1 : 2;
            Vector3 dir = ax == 0 ? Vector3.right : ax == 1 ? Vector3.up : Vector3.forward;
            float cAlong = Vector3.Dot(lb.center, dir);
            float sign = cAlong >= 0f ? 1f : -1f;
            float extA = Mathf.Abs(Vector3.Dot(ext, dir));
            if (_head != null)
            {
                _headLocal = transform.InverseTransformPoint(_head.position);
                sign = Vector3.Dot(_headLocal - lb.center, dir) >= 0f ? 1f : -1f;
            }
            else _headLocal = lb.center + dir * (sign * extA * 0.85f);
            _gripLocal = lb.center - dir * (sign * extA * 0.8f);                     // конец ручки — за него держат
        }

        /// <summary>Боёк в этой позе во что-то упёрся?</summary>
        private bool HeadBlocked(Vector3 pos, Quaternion rot, out Vector3 contact)
        {
            contact = Vector3.zero;
            Vector3 hp = pos + rot * Vector3.Scale(_headLocal, transform.lossyScale);
            int n = Physics.OverlapSphereNonAlloc(hp, _headRadius, _blockHits, _blockMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = _blockHits[i];
                if (c == null || c.transform.IsChildOf(transform)) continue;
                if (c is CharacterController || c.GetComponentInParent<PlayerInteractor>() != null) continue;
                MeshCollider mc = c as MeshCollider;
                contact = mc != null && !mc.convex ? hp : c.ClosestPoint(hp);
                return true;
            }
            return false;
        }

        /// <summary>Момент удара: что под прицелом.</summary>
        private void Strike()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            float range = _aimStrike ? Mathf.Min(_range, _reach + 0.15f) : _range;     // v17.5: бьёт только докуда достаёт
            int n = Physics.RaycastNonAlloc(ray, _hits, range, ~0, QueryTriggerInteraction.Ignore);

            float best = float.MaxValue;
            NetworkObject target = null;
            Vector3 point = ray.origin + ray.direction * range;
            bool hitAnything = false;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.transform.IsChildOf(transform) || h.distance >= best) continue;
                best = h.distance;
                point = h.point;
                hitAnything = true;
                FoodPiece p = ((1 << h.collider.gameObject.layer) & _foodMask) != 0
                    ? h.collider.GetComponentInParent<FoodPiece>() : null;
                target = p != null && !p.IsHeld ? p.NetworkObject : null;
                // Не еда — может, поддон или ящик (IHammerTarget)?
                if (target == null)
                {
                    IHammerTarget ht = h.collider.GetComponentInParent<IHammerTarget>();
                    if (ht is Component comp) target = comp.GetComponentInParent<NetworkObject>();
                }
            }
            if (hitAnything) CmdHit(target, point);
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        [ServerRpc(RequireOwnership = true)]
        private void CmdSwing() => RpcSwing();

        [ObserversRpc(ExcludeOwner = true)]
        private void RpcSwing() => StartSwing();

        [ServerRpc(RequireOwnership = true)]
        private void CmdHit(NetworkObject target, Vector3 point)
        {
            if ((point - transform.position).sqrMagnitude > (_range + 2f) * (_range + 2f)) return;

            // Поддон, ящик и т.п. — у него своя реакция на удар.
            IHammerTarget hammerTarget = target != null ? target.GetComponentInChildren<IHammerTarget>() : null;
            if (hammerTarget != null && target.GetComponent<FoodPiece>() == null)
            {
                Vector3 dir = (point - transform.position).normalized;
                hammerTarget.ServerHammerHit(point, dir);
                return;
            }

            FoodPiece piece = target != null ? target.GetComponent<FoodPiece>() : null;
            if (piece == null || piece.IsConsumed || piece.IsHeld)
            {
                RpcImpact(point, new Color(0.8f, 0.72f, 0.55f), 0.3f, 1f, 1f);
                return;
            }

            ProductDef def = piece.Def;
            Color flesh = def != null ? def.BaseColor : Color.white;
            // v17.5: пыль по продукту — гуще и из середины (между точкой удара и центром)
            float psize = SizeOf(piece);
            Vector3 fxAt = Vector3.Lerp(point, CentreOf(piece), 0.65f);
            point = fxAt;

            // 1. Есть правило — работа по таблице.
            foreach (FoodProcess proc in _processes)
            {
                if (!piece.CanApplyNow(proc)) continue;
                // Таймер продукта копится между ударами: не останавливаем работу,
                // иначе накопленное обнулилось бы (у правил без окна Stop сбрасывает время).
                bool changed = piece.ServerAddWork(proc, _workPerHit, 1f);
                if (_log) Debug.Log($"[Кувалда] {piece}{(changed ? " — готово" : "")}", piece);
                RpcImpact(point, flesh, Mathf.Max(0.2f, psize * 1.4f), _foodDust, _foodSpread);
                return;
            }

            // 2. Правила нет — вдребезги.
            if (!_smashOthers)
            {
                RpcImpact(point, flesh, Mathf.Max(0.2f, psize * 1.4f), _foodDust, _foodSpread);
                return;
            }
            Color skin = FoodColors.Of(def, piece.State);
            float size = SizeOf(piece);
            Vector3 centre = piece.transform.position;
            if (_log) Debug.Log($"[Кувалда] {piece} — вдребезги", piece);
            if (!piece.ServerTryConsume()) return;
            RpcShatter(centre, size, skin, flesh);
            piece.NetworkObject.Despawn();
        }

        private static Vector3 CentreOf(FoodPiece p)
        {
            Renderer[] rs = p.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return p.transform.position;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b.center;
        }

        private static float SizeOf(FoodPiece p)
        {
            Renderer[] rs = p.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return 0.1f;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        }

        // ───────────────────────── ВСЕМ: ЭФФЕКТЫ ─────────────────────────

        [ObserversRpc]
        private void RpcShatter(Vector3 centre, float size, Color skin, Color flesh)
        {
            FoodShatterFx.Play(centre, size, skin, flesh, _shards, _drops, gameObject.layer, _shardMaterial);
        }

        [ObserversRpc]
        private void RpcImpact(Vector3 point, Color color, float size, float amount, float spread)
        {
            // v17.4–17.5: облачка пыли и объёмные ошмётки разной формы вместо квадратиков
            CCPowderFx.Hit(point, Vector3.up, color, size, amount, spread);  // v18: мультяшный «пых», по продукту — вдвое уже
        }

        // ───────────────────────── РУКА ─────────────────────────

        /// <summary>
        /// v13.1: поза «на плече». Дуга — в плоскости «вверх–вперёд» руки: угол 0 — боёк вверх,
        /// 90 — вперёд, 135 — вниз перед собой, −40 — за плечом. Конец ручки — в руке.
        /// </summary>
        private void ShoulderPose(Transform hand)
        {
            if (!_headInit) InitHead();
            Vector3 R = hand.right, U = hand.up, F = hand.forward;
            // горизонтальные оси — чтобы удар шёл в пол перед собой, а не «за камерой»
            Vector3 Fh = Vector3.ProjectOnPlane(F, Vector3.up);
            if (Fh.sqrMagnitude < 1e-4f) Fh = Vector3.ProjectOnPlane(-U, Vector3.up);
            Fh.Normalize();
            Vector3 Rh = Vector3.Cross(Vector3.up, Fh).normalized;

            float rest = _arcAngles.x, back = _arcAngles.y, hit = _arcAngles.z;
            float arc = rest, handK = 0f;
            _lungeAdd = Vector3.zero;
            if (_aimStrike && _aimValid && _swingT >= 0f)
            {
                // v17.5: удар в точку прицела — повернуться к ней, подобрать угол дуги и вынос руки
                Vector3 toT = Vector3.ProjectOnPlane(_aimPt - hand.position, Vector3.up);
                float turn = Mathf.Clamp01(_swingT / Mathf.Max(0.05f, _windup));
                if (toT.sqrMagnitude > 1e-4f) Fh = Vector3.Slerp(Fh, toT.normalized, turn).normalized;
                Rh = Vector3.Cross(Vector3.up, Fh).normalized;
                Vector3 handStrike = hand.position + hand.right * _strikeOffset.x + Vector3.up * _strikeOffset.y + Fh * _strikeOffset.z;
                Vector3 D = _aimPt - handStrike;
                float L = (Vector3.Scale(_headLocal - _gripLocal, transform.lossyScale)).magnitude;
                float a = Mathf.Atan2(Vector3.Dot(D, Fh), Vector3.Dot(D, Vector3.up)) * Mathf.Rad2Deg;
                hit = Mathf.Clamp(a, 60f, 175f);
                float dl = Mathf.Clamp(D.magnitude - L, -_maxLunge * 0.5f, _maxLunge);
                _lungeAdd = D.sqrMagnitude > 1e-6f ? D.normalized * dl : Vector3.zero;
            }
            if (_swingT >= 0f)
            {
                if (_swingT < _windup)
                {
                    float k = Mathf.SmoothStep(0f, 1f, _swingT / _windup);
                    arc = Mathf.Lerp(rest, back, k); handK = -0.15f * k;
                }
                else if (_swingT < _windup + _slam)
                {
                    float k = (_swingT - _windup) / _slam;
                    k = k * k;                                               // разгон к удару
                    arc = Mathf.Lerp(back, hit, k); handK = Mathf.Lerp(-0.15f, 1f, k);
                }
                else
                {
                    float k = Mathf.SmoothStep(0f, 1f, (_swingT - _windup - _slam) / _recover);
                    arc = Mathf.Lerp(hit, rest, k); handK = Mathf.Lerp(1f, 0f, k);
                }
            }
            if (_recoilT >= 0f)
            {
                _recoilT += Time.deltaTime;
                float rk = Mathf.Clamp01(_recoilT / 0.12f);
                arc -= _recoilAngle * Mathf.Sin(rk * Mathf.PI);
                if (rk >= 1f) _recoilT = -1f;
            }

            // боёк не проходит сквозь: от прошлого кадра к нужной дуге — последняя свободная
            if (float.IsNaN(_prevArc)) _prevArc = arc;
            if (_solidHead)
            {
                Vector3 c0;
                if (PoseBlocked(hand, Rh, Fh, arc, handK, out c0))
                {
                    if (!PoseBlocked(hand, Rh, Fh, _prevArc, handK, out c0))
                    {
                        float lo = 0f, hi = 1f;
                        for (int i = 0; i < 7; i++)
                        {
                            float m = 0.5f * (lo + hi);
                            if (PoseBlocked(hand, Rh, Fh, Mathf.Lerp(_prevArc, arc, m), handK, out c0)) hi = m; else lo = m;
                        }
                        arc = Mathf.Lerp(_prevArc, arc, lo);
                    }
                    else arc = Mathf.Min(_prevArc, arc);                     // уже упёрся — не глубже
                    bool slamming = _swingT >= _windup * 0.9f && _swingT < _windup + _slam + _recover * 0.5f;
                    if (slamming && !_contact)
                    {
                        _contact = true;
                        _recoilT = 0f;
                        if (base.IsOwner && !_hitDone) { _hitDone = true; Strike(); }
                        if (_swingT < _windup + _slam) _swingT = _windup + _slam;
                    }
                }
            }
            _prevArc = arc;

            Vector3 pos; Quaternion rot;
            ShoulderPoseAt(hand, Rh, Fh, arc, handK, out pos, out rot);
            transform.SetPositionAndRotation(pos, rot);
        }

        private void ShoulderPoseAt(Transform hand, Vector3 Rh, Vector3 Fh, float arcDeg, float handK, out Vector3 pos, out Quaternion rot)
        {
            float a = arcDeg * Mathf.Deg2Rad;
            Vector3 dir = Vector3.up * Mathf.Cos(a) + Fh * Mathf.Sin(a);              // куда смотрит боёк от ручки
            Vector3 side = Quaternion.AngleAxis(_shoulderRoll, dir) * Rh;            // ось «вбок» кувалды
            Vector3 hd = (_headLocal - _gripLocal);
            if (hd.sqrMagnitude < 1e-8f) hd = Vector3.forward;
            hd.Normalize();
            // боковая ось модели — любая, перпендикулярная рукояти (самая «плоская» из осей)
            Vector3 sl = Mathf.Abs(hd.x) < 0.9f ? Vector3.right : Vector3.up;
            sl = Vector3.ProjectOnPlane(sl, hd).normalized;
            Vector3 sideW = Vector3.ProjectOnPlane(side, dir);
            if (sideW.sqrMagnitude < 1e-6f) sideW = Rh;
            rot = Quaternion.LookRotation(dir, sideW.normalized) * Quaternion.Inverse(Quaternion.LookRotation(hd, sl));
            Vector3 handPos = hand.position
                + hand.right * Mathf.LerpUnclamped(_shoulderOffset.x, _strikeOffset.x, Mathf.Max(0f, handK))
                + Vector3.up * Mathf.LerpUnclamped(_shoulderOffset.y, _strikeOffset.y, Mathf.Max(0f, handK))
                + Fh * Mathf.LerpUnclamped(_shoulderOffset.z, _strikeOffset.z, handK);
            pos = handPos - rot * Vector3.Scale(_gripLocal, transform.lossyScale) + _lungeAdd * Mathf.Max(0f, handK);
        }

        private Vector3 _lungeAdd;

        private bool PoseBlocked(Transform hand, Vector3 Rh, Vector3 Fh, float arc, float handK, out Vector3 contact)
        {
            Vector3 p; Quaternion r;
            ShoulderPoseAt(hand, Rh, Fh, arc, handK, out p, out r);
            return HeadBlocked(p, r, out contact);
        }

        public override void UpdateHeldPose(Transform handAnchor)
        {
            if (_shoulderCarry) { ShoulderPose(handAnchor); return; }
            // Базовая поза в руке — от PickupItem (его Hold Offset), к ней — наше движение.
            base.UpdateHeldPose(handAnchor);
            Vector3 pos = transform.position;
            Quaternion rot = transform.rotation;
            float pitch = 0f, lunge = 0f;

            if (_swingT >= 0f)
            {
                // Замах вверх (плавно) → удар вниз (резко) → возврат.
                if (_swingT < _windup)
                {
                    float k = Mathf.SmoothStep(0f, 1f, _swingT / _windup);
                    pitch = -_windupAngle * k; lunge = -_lunge * 0.3f * k;
                }
                else if (_swingT < _windup + _slam)
                {
                    float k = (_swingT - _windup) / _slam;
                    k = k * k;                                           // разгон к удару
                    pitch = Mathf.Lerp(-_windupAngle, _slamAngle, k); lunge = Mathf.Lerp(-_lunge * 0.3f, _lunge, k);
                }
                else
                {
                    float k = Mathf.SmoothStep(0f, 1f, (_swingT - _windup - _slam) / _recover);
                    pitch = Mathf.Lerp(_slamAngle, 0f, k); lunge = Mathf.Lerp(_lunge, 0f, k);
                }
            }

            // v13: отскок после касания
            if (_recoilT >= 0f)
            {
                _recoilT += Time.deltaTime;
                float rk = Mathf.Clamp01(_recoilT / 0.12f);
                pitch -= _recoilAngle * Mathf.Sin(rk * Mathf.PI);
                if (rk >= 1f) _recoilT = -1f;
            }

            // v13: боёк не проходит сквозь — ищем последнюю свободную позу между прошлым кадром и нужной
            if (_solidHead)
            {
                if (!_headInit) InitHead();
                Vector3 contact;
                Quaternion want = rot * Quaternion.Euler(pitch, 0f, 0f);
                if (HeadBlocked(pos + handAnchor.forward * lunge, want, out contact))
                {
                    float lo = 0f, hi = 1f;                                   // lo — свободно (прошлый кадр)
                    Quaternion r0 = rot * Quaternion.Euler(_prevPitch, 0f, 0f);
                    Vector3 c0;
                    if (HeadBlocked(pos + handAnchor.forward * _prevLunge, r0, out c0))
                    {
                        // уже упёрся (смотришь в пол) — приподнять боёк, пока не станет свободно
                        float lift = 0f;
                        for (int i = 0; i < 8; i++)
                        {
                            lift -= 8f;
                            if (!HeadBlocked(pos, rot * Quaternion.Euler(Mathf.Min(_prevPitch, pitch) + lift, 0f, 0f), out c0)) break;
                        }
                        pitch = Mathf.Min(_prevPitch, pitch) + lift;
                        lunge = 0f;
                    }
                    else
                    {
                        for (int i = 0; i < 7; i++)
                        {
                            float m = 0.5f * (lo + hi);
                            float pm = Mathf.Lerp(_prevPitch, pitch, m), lm = Mathf.Lerp(_prevLunge, lunge, m);
                            if (HeadBlocked(pos + handAnchor.forward * lm, rot * Quaternion.Euler(pm, 0f, 0f), out c0)) hi = m; else lo = m;
                        }
                        pitch = Mathf.Lerp(_prevPitch, pitch, lo);
                        lunge = Mathf.Lerp(_prevLunge, lunge, lo);
                    }
                    // касание во время удара — удар засчитан сейчас, кувалда отскакивает
                    bool slamming = _swingT >= _windup * 0.9f && _swingT < _windup + _slam + _recover * 0.5f;
                    if (slamming && !_contact)
                    {
                        _contact = true;
                        _recoilT = 0f;
                        if (base.IsOwner && !_hitDone) { _hitDone = true; Strike(); }
                        if (_swingT < _windup + _slam) _swingT = _windup + _slam;   // дальше — сразу возврат
                    }
                }
            }
            _prevPitch = pitch; _prevLunge = lunge;
            rot *= Quaternion.Euler(pitch, 0f, 0f);
            pos += handAnchor.forward * lunge;
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
