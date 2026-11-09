using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// В пространстве имён НАМЕРЕННО: в проекте ещё живёт старая система.
namespace CatCooks.Food.Tools
{
    /// <summary>
    /// ДОСКА, КОТОРАЯ СОБИРАЕТСЯ В СТОПКУ В РУКАХ.
    ///
    ///   • в руке доска, ЛКМ по другой доске на полу — она добавляется в стопку
    ///     (до Max Stack), в руках видно, как стопка растёт;
    ///   • ПКМ — снять одну доску со стопки и положить перед собой;
    ///   • бросил стопку в кострище — уходят все доски разом (если не влезли все,
    ///     остаток остаётся стопкой).
    /// Чем выше стопка, тем медленнее ходишь.
    ///
    /// ═══ ПРЕФАБ ДОСКИ ═══
    /// Вместо Pickup Item: Plank Stack Item. Рядом Bonfire Fuel, NetworkObject,
    /// NetworkTransform (Client Authoritative), Rigidbody, коллайдер.
    /// Plank Prefab — этот же префаб (для ПКМ «снять доску»).
    /// </summary>
    public sealed class PlankStackItem : PickupItem
    {
        [Header("Стопка")]
        [Range(1, 20)][SerializeField] private int _maxStack = 8;
        [Tooltip("Префаб одной доски — для «снять доску» (ПКМ). Обычно этот же префаб.")]
        [SerializeField] private NetworkObject _plankPrefab;
        [Tooltip("Дальность «подобрать доску в стопку», м")]
        [SerializeField] private float _collectRange = 2.6f;
        [Tooltip("Шаг стопки по высоте, м (≈ толщина доски в мире)")]
        [SerializeField] private float _stackStep = 0.035f;
        [Tooltip("Замедление: скорость при полной стопке (при одной доске — Carry Speed)")]
        [Range(0.2f, 1f)][SerializeField] private float _fullStackSpeed = 0.55f;
        [SerializeField] private bool _log = true;

        [Tooltip("Упала стопка на пол — рассыпается на отдельные доски")]
        [SerializeField] private bool _scatterOnFloor = true;

        private readonly SyncVar<byte> _count = new SyncVar<byte>(1);
        private bool _flying;
        private float _landedAt = -1f;
        private Rigidbody _body;
        private readonly List<GameObject> _extra = new List<GameObject>();
        private readonly RaycastHit[] _hits = new RaycastHit[12];
        private MeshFilter _srcFilter;
        private MeshRenderer _srcRenderer;

        public int Count => _count.Value;
        public override float CarrySpeedMultiplier =>
            base.CarrySpeedMultiplier * Mathf.Lerp(1f, _fullStackSpeed, (_count.Value - 1) / Mathf.Max(1f, _maxStack - 1f));

        protected override void Awake()
        {
            base.Awake();
            _srcFilter = GetComponentInChildren<MeshFilter>();
            _body = GetComponent<Rigidbody>();
            _srcRenderer = _srcFilter != null ? _srcFilter.GetComponent<MeshRenderer>() : null;
        }

        [Server]
        public void ServerSetCount(int n) => _count.Value = (byte)Mathf.Clamp(n, 1, 255);

        // ── бросок (G): не пролетать сквозь пол; в кострище — в стопку; на полу — рассыпаться ──

        public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
        {
            base.ServerOnDrop(hand, tossDirection, force);
            // Тонкая доска на скорости перескакивает пол за один шаг физики — считаем непрерывно.
            if (_body != null) _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _flying = true;
            _landedAt = -1f;
        }

        private void OnCollisionEnter(Collision c)
        {
            if (!base.IsServerInitialized || !_flying) return;
            if (c.collider.GetComponentInParent<PlankStackItem>() != null) return;   // о другую доску — летим дальше
            _flying = false;
            _landedAt = Time.time;
        }

        private void Update()
        {
            if (!base.IsServerInitialized) return;

            // Летит мимо Intake, но над кострищем — всё равно в стопку.
            if (_flying || _landedAt > 0f)
            {
                BonfirePit pit = BonfirePit.FindCatching(transform.position);
                BonfireFuel fuel = GetComponent<BonfireFuel>();
                if (pit != null && fuel != null && !fuel.Consumed) { pit.ServerAccept(fuel); return; }
            }

            if (_landedAt > 0f && Time.time - _landedAt > 0.15f)
            {
                _landedAt = -1f;
                if (_scatterOnFloor && _count.Value > 1) Scatter();
            }

            // Улеглась — обратно обычная проверка столкновений (дешевле).
            if (!_flying && _body != null && !_body.isKinematic &&
                _body.collisionDetectionMode != CollisionDetectionMode.Discrete && _body.linearVelocity.sqrMagnitude < 0.01f)
                _body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }

        /// <summary>Стопка упала на пол — рассыпается: каждая доска отдельно, рядом.</summary>
        [Server]
        private void Scatter()
        {
            if (_plankPrefab == null) return;
            int extra = _count.Value - 1;
            _count.Value = 1;
            for (int i = 0; i < extra; i++)
            {
                Vector3 at = transform.position + Vector3.up * (0.05f + i * 0.04f) +
                             new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f));
                NetworkObject nob = Instantiate(_plankPrefab, at, transform.rotation * Quaternion.Euler(0f, Random.Range(-40f, 40f), 0f));
                base.Spawn(nob.gameObject);
                PlankStackItem p = nob.GetComponent<PlankStackItem>();
                if (p != null) p.ServerSetCount(1);
                Rigidbody rb = nob.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                    rb.linearVelocity = new Vector3(Random.Range(-1f, 1f), 0.8f, Random.Range(-1f, 1f));
                    rb.angularVelocity = Random.insideUnitSphere * 3f;
                }
            }
            if (_log) Debug.Log($"[Доски] стопка рассыпалась на {extra + 1}", this);
        }

        // ЛКМ: подобрать доску в стопку.
        public override void ServerPrimaryUse(UseContext ctx)
        {
            if (_count.Value >= _maxStack) { if (_log) Debug.Log("[Доски] стопка полная", this); return; }
            int n = Physics.RaycastNonAlloc(ctx.Origin, ctx.Direction, _hits, _collectRange, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            PlankStackItem other = null;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].distance >= best) continue;
                PlankStackItem p = _hits[i].collider.GetComponentInParent<PlankStackItem>();
                if (p == null || p == this || p.IsHeldNow()) continue;
                best = _hits[i].distance; other = p;
            }
            if (other == null) return;
            int take = Mathf.Min(other.Count, _maxStack - _count.Value);
            _count.Value = (byte)(_count.Value + take);
            if (take >= other.Count) other.NetworkObject.Despawn();
            else other.ServerSetCount(other.Count - take);
            if (_log) Debug.Log($"[Доски] в стопке {_count.Value}", this);
        }

        // ПКМ: снять одну доску и положить перед собой.
        public override void ServerSecondaryUse(UseContext ctx)
        {
            if (_count.Value <= 1 || _plankPrefab == null) return;
            _count.Value = (byte)(_count.Value - 1);
            Vector3 at = ctx.Origin + ctx.Direction * 1.2f;
            NetworkObject nob = Instantiate(_plankPrefab, at, Quaternion.LookRotation(ctx.Direction) * Quaternion.Euler(0f, 90f, 0f));
            base.Spawn(nob.gameObject);
            PlankStackItem p = nob.GetComponent<PlankStackItem>();
            if (p != null) p.ServerSetCount(1);
            Rigidbody rb = nob.GetComponent<Rigidbody>();
            if (rb != null) rb.linearVelocity = ctx.Direction * 1.5f;
        }

        private bool IsHeldNow()
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            return rb != null && rb.isKinematic;
        }

        // ── вид: лишние доски стопкой над основной (у всех) ──
        private void LateUpdate()
        {
            int want = Mathf.Max(0, _count.Value - 1);
            if (_srcFilter == null || _srcRenderer == null) return;
            while (_extra.Count > want) { Destroy(_extra[_extra.Count - 1]); _extra.RemoveAt(_extra.Count - 1); }
            while (_extra.Count < want)
            {
                int i = _extra.Count + 1;
                GameObject go = new GameObject("PlankInStack (auto)") { hideFlags = HideFlags.DontSave, layer = gameObject.layer };
                go.transform.SetParent(_srcFilter.transform, false);
                // Шаг — в мире, вдоль «верха» доски; лёгкий разворот, чтобы стопка не была идеальной.
                Vector3 upLocal = _srcFilter.transform.InverseTransformDirection(transform.up);
                float s = Mathf.Max(1e-5f, _srcFilter.transform.lossyScale.y);
                go.transform.localPosition = upLocal.normalized * (_stackStep * i / s);
                System.Random r = new System.Random(i * 131);
                go.transform.localRotation = Quaternion.AngleAxis(((float)r.NextDouble() - 0.5f) * 10f, upLocal);
                go.AddComponent<MeshFilter>().sharedMesh = _srcFilter.sharedMesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = _srcRenderer.sharedMaterials;
                _extra.Add(go);
            }
        }

        private void OnDestroy()
        {
            foreach (GameObject g in _extra) if (g != null) Destroy(g);
        }
    }
}
