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
    /// ═══ ТРЕБУЕТ ═══
    /// NetworkObject, NetworkTransform (Client Authoritative), Rigidbody, Collider.
    /// Head — дочерний объект на бойке (оттуда пыль и эффекты). Можно не задавать.
    /// </summary>
    public class SledgehammerItem : PickupItem
    {
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


        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private float _swingT = -1f;          // −1 — не машем
        private bool _hitDone;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private float SwingLength => _windup + _slam + _recover;

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
            _swingT = 0f;
            _hitDone = false;
        }

        /// <summary>Момент удара: что под прицелом.</summary>
        private void Strike()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int n = Physics.RaycastNonAlloc(ray, _hits, _range, ~0, QueryTriggerInteraction.Ignore);

            float best = float.MaxValue;
            NetworkObject target = null;
            Vector3 point = ray.origin + ray.direction * _range;
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
                RpcImpact(point, new Color(0.8f, 0.72f, 0.55f));
                return;
            }

            ProductDef def = piece.Def;
            Color flesh = def != null ? def.BaseColor : Color.white;

            // 1. Есть правило — работа по таблице.
            foreach (FoodProcess proc in _processes)
            {
                if (!piece.CanApplyNow(proc)) continue;
                // Таймер продукта копится между ударами: не останавливаем работу,
                // иначе накопленное обнулилось бы (у правил без окна Stop сбрасывает время).
                bool changed = piece.ServerAddWork(proc, _workPerHit, 1f);
                if (_log) Debug.Log($"[Кувалда] {piece}{(changed ? " — готово" : "")}", piece);
                RpcImpact(point, flesh);
                return;
            }

            // 2. Правила нет — вдребезги.
            if (!_smashOthers)
            {
                RpcImpact(point, flesh);
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
        private void RpcImpact(Vector3 point, Color color)
        {
            CCRuntimeParticles.Burst(point, Vector3.up, color, Color.white, 14, CCRuntimeParticles.Look.Dust.Scaled(3f));
            CCRuntimeParticles.Burst(point, Vector3.up, color * 0.9f, color, 8, CCRuntimeParticles.Look.Chips.Scaled(2f));
        }

        // ───────────────────────── РУКА ─────────────────────────

        public override void UpdateHeldPose(Transform handAnchor)
        {
            // Базовая поза в руке — от PickupItem (его Hold Offset), к ней — наше движение.
            base.UpdateHeldPose(handAnchor);
            Vector3 pos = transform.position;
            Quaternion rot = transform.rotation;

            if (_swingT >= 0f)
            {
                // Замах вверх (плавно) → удар вниз (резко) → возврат.
                float pitch, lunge;
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
                rot *= Quaternion.Euler(pitch, 0f, 0f);
                pos += handAnchor.forward * lunge;
            }
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
