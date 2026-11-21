using CatCooks.Food.Tools;
using FishNet.Object;
using UnityEngine;

namespace CatCooks.Food.Supply
{
    /// <summary>
    /// v25.1: МЕШОК — ВЗЯТЬ, КИНУТЬ, ПОЙМАТЬ. Вешается рядом с BulkSack (меню «Доставка → 3» ставит сам на мешки папки).
    ///
    ///   F — взять мешок в руки (тяжёлый: идёшь медленнее).
    ///   ЛКМ (держать — сильнее) — кинуть. G — просто поставить.
    ///   Летящий мешок ЛОВИТ кот с пустыми руками, если мешок пролетает рядом (радиус Catch Radius) — сразу в руки.
    ///   Не поймали и мешок ударился быстрее Burst Speed — РВЁТСЯ: облако пыли, на полу кучка продукта,
    ///   в мешке становится меньше (Loss — доля оставшегося, но не меньше Min Loss порций). Мешок легчает.
    ///
    /// Кучку на полу можно собрать лопатой/мастерком — не всё потеряно, но грязное и время.
    ///
    /// v25.3: СЫПАТЬ ИЗ МЕШКА НА ГЛАЗ — держишь мешок, зажал ПКМ: мешок наклоняется, из горловины сыплется струя
    /// (Pour Rate порций/с) — в тару под струёй, мимо — кучкой на пол. Запас мешка конечный (доставка ставит
    /// ровно по меню), пустой мешок не сыплет. Сколько насыпал — столько и будет: «на глаз», без меню дозы.
    /// </summary>
    [RequireComponent(typeof(BulkSack))]
    public sealed class SupplySackThrow : PickupItem
    {
        [Header("Бросок")]
        [SerializeField] private float _minThrow = 3f;
        [SerializeField] private float _maxThrow = 11f;
        [Tooltip("Удар быстрее этого (м/с) — мешок рвётся")]
        [SerializeField] private float _burstSpeed = 6f;
        [Tooltip("Сколько от оставшегося высыпается при разрыве")]
        [Range(0f, 1f)][SerializeField] private float _loss = 0.3f;
        [Min(1)][SerializeField] private int _minLoss = 3;
        [Tooltip("Кот ловит летящий мешок, если тот пролетает ближе этого, м")]
        [SerializeField] private float _catchRadius = 1.4f;
        [Header("Сыпать (ПКМ)")]
        [Tooltip("Порций в секунду, пока держишь ПКМ")]
        [Min(0.2f)][SerializeField] private float _pourRate = 3f;
        [Tooltip("Наклон мешка при сыпании, градусы (вокруг красной оси X)")]
        [SerializeField] private float _pourTilt = 75f;
        [Tooltip("Тара ловит струю, если горловина тары ближе этого по горизонтали, м")]
        [SerializeField] private float _captureRadius = 0.7f;
        [SerializeField] private bool _log = true;

        private readonly FishNet.Object.Synchronizing.SyncVar<bool> _pouring = new FishNet.Object.Synchronizing.SyncVar<bool>(false);
        private readonly FishNet.Object.Synchronizing.SyncVar<int> _leftNet = new FishNet.Object.Synchronizing.SyncVar<int>(-1);
        private float _tilt, _pourAcc, _mouthH = -1f;
        private bool _rmbWas;
        private ParticleSystem _stream;
        private FoodHeap _floorHeap;

        private BulkSack _sack;
        private bool _flying;
        private float _thrownAt, _slowFor;
        private PlayerHand _thrower;
        private float _baseMass = -1f;
        private int _startLeft = -1;
        private readonly Collider[] _near = new Collider[16];

        public override bool ChargedPrimaryUse => true;
        public override float CarrySpeedMultiplier => 0.65f;

        protected override void Awake()
        {
            base.Awake();
            _sack = GetComponent<BulkSack>();
            _canPickup = true;
            if (_rb != null) _baseMass = _rb.mass;
        }

        /// <summary>ЛКМ с зарядом — бросок.</summary>
        public override void ServerPrimaryUse(UseContext ctx)
        {
            if (ctx.Hand == null || ctx.Hand.HeldItem != this) return;
            float force = Mathf.Lerp(_minThrow, _maxThrow, ctx.Charge);
            _thrower = ctx.Hand;
            ctx.Hand.ServerDrop((ctx.Direction + Vector3.up * 0.25f).normalized, force);
            _flying = true;
            _thrownAt = Time.time;
            _slowFor = 0f;
        }

        public override void ServerOnPickup(PlayerHand hand)
        {
            base.ServerOnPickup(hand);
            _flying = false;
        }

        // ───────────────────────── СЫПАТЬ (ПКМ) ─────────────────────────

        public override void UpdateHeldPose(Transform handAnchor)
        {
            base.UpdateHeldPose(handAnchor);
            if (_tilt > 0.001f) transform.rotation = transform.rotation * Quaternion.Euler(_pourTilt * _tilt, 0f, 0f);
        }

        private void Update()
        {
            _tilt = Mathf.MoveTowards(_tilt, _pouring.Value ? 1f : 0f, Time.deltaTime * 3f);
            if (IsOwner && IsClientInitialized)
            {
                bool rmb = Input.GetMouseButton(1) && !UiModal.AnyOpen;
                if (rmb != _rmbWas) { _rmbWas = rmb; CmdPour(rmb); }
            }
            if (IsServerInitialized) ServerPourTick();
            UpdateStream();
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdPour(bool on)
        {
            bool held = NetworkObject.Owner != null && NetworkObject.Owner.IsValid;
            _pouring.Value = on && held;
            if (!_pouring.Value) _pourAcc = 0f;
        }

        public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
        {
            _pouring.Value = false;
            base.ServerOnDrop(hand, tossDirection, force);
        }

        /// <summary>Горловина: верх мешка (в его осях) — при наклоне уходит вперёд.</summary>
        private Vector3 Mouth
        {
            get
            {
                if (_mouthH < 0f)
                {
                    float top = 0f; bool any = false;
                    foreach (Renderer r in GetComponentsInChildren<Renderer>())
                    {
                        if (r is ParticleSystemRenderer) continue;
                        Bounds b = r.bounds;
                        float t = transform.InverseTransformPoint(b.center + Vector3.up * b.extents.y).y;
                        if (!any || t > top) { top = t; any = true; }
                    }
                    _mouthH = any ? top : 0.5f;
                }
                return transform.TransformPoint(new Vector3(0f, _mouthH, 0f));
            }
        }

        private void ServerPourTick()
        {
            if (_sack != null && _leftNet.Value != Left()) _leftNet.Value = Left();
            if (!_pouring.Value || _tilt < 0.6f || _sack == null) return;
            if (!_sack.PeekScoop(out _, out _)) { _pouring.Value = false; return; }            // пустой — не сыплет
            _pourAcc += Time.deltaTime * _pourRate;
            while (_pourAcc >= 1f)
            {
                _pourAcc -= 1f;
                int n = _sack.ServerScoop(1, out ushort product, out FoodState state, out byte cook, out byte clean);
                if (n <= 0) { _pouring.Value = false; return; }
                DropPortion(product, state, cook, clean);
            }
        }

        private readonly RaycastHit[] _hits = new RaycastHit[12];

        /// <summary>Порция падает из горловины вниз: в тару (или рядом с её горлом) — туда, мимо — в кучку на полу.</summary>
        private void DropPortion(ushort product, FoodState state, byte cook, byte clean)
        {
            Vector3 from = Mouth;
            int n = Physics.RaycastNonAlloc(from, Vector3.down, _hits, 6f, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue; RaycastHit hit = default; bool got = false;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(transform)) continue;
                if (_hits[i].collider.GetComponentInParent<PlayerHand>() != null) continue;
                if (_hits[i].distance < best) { best = _hits[i].distance; hit = _hits[i]; got = true; }
            }
            Vector3 point = got ? hit.point : from + Vector3.down * 2f;
            Cookware cw = got ? hit.collider.GetComponentInParent<Cookware>() : null;
            if (cw == null)
            {
                float bd = _captureRadius;
                foreach (Cookware c in Cookware.All)
                {
                    if (c == null) continue;
                    Vector3 m = c.MouthPoint;
                    if (from.y < m.y || point.y > m.y + 0.2f) continue;
                    float d = new Vector2(m.x - from.x, m.z - from.z).magnitude;
                    if (d < bd) { bd = d; cw = c; }
                }
            }
            if (cw != null)
            {
                cw.ServerAddContent(product, state, 1, cook);
                cw.ServerAgitate(0.2f);
                return;
            }
            // мимо — на пол, в одну кучку (новая, если отошли)
            if (_floorHeap != null && (!_floorHeap.IsSpawned || (_floorHeap.transform.position - point).sqrMagnitude > 0.6f * 0.6f)) _floorHeap = null;
            if (_floorHeap != null) { _floorHeap.ServerAdd(1, cook, (byte)Mathf.Min(clean, 70)); return; }
            FoodSubstanceTable t = FoodSubstanceTable.Instance;
            if (t == null || t.FloorHeapPrefab == null) return;
            _floorHeap = Instantiate(t.FloorHeapPrefab, point + Vector3.up * 0.03f, Quaternion.identity);
            _floorHeap.ServerPreset(product, state, 1, cook, (byte)Mathf.Min(clean, 70), true);
            Spawn(_floorHeap.gameObject);
        }

        /// <summary>Струя сыпучего (вид, у всех): частицы из горловины вниз цвета продукта.</summary>
        private void UpdateStream()
        {
            bool on = _pouring.Value && _tilt > 0.6f && _leftNet.Value != 0;
            if (on && _stream == null && !Application.isBatchMode)
            {
                var go = new GameObject("Sack Stream (auto)") { hideFlags = HideFlags.DontSave };
                _stream = go.AddComponent<ParticleSystem>();
                _stream.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = _stream.main;
                main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = 1.2f; main.startSpeed = 0.4f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
                main.gravityModifier = 1.2f; main.maxParticles = 600;
                Color c = new Color(0.95f, 0.93f, 0.88f);
                if (_sack != null && _sack.PeekScoop(out ushort pid, out _) && FoodDatabase.Instance != null)
                {
                    ProductDef pd = FoodDatabase.Instance.Get(pid);
                    if (pd != null) c = pd.BaseColor;
                }
                main.startColor = c;
                var em = _stream.emission; em.rateOverTime = 140f;
                var sh = _stream.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.12f;
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);                // круг горизонтально
                var col = _stream.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World;
                col.bounce = 0.05f; col.lifetimeLoss = 0.6f; col.quality = ParticleSystemCollisionQuality.Low;
                var r = go.GetComponent<ParticleSystemRenderer>();
                Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (s != null) r.sharedMaterial = new Material(s) { color = Color.white, hideFlags = HideFlags.DontSave };
            }
            if (_stream == null) return;
            _stream.transform.position = Mouth;
            if (on && !_stream.isEmitting) _stream.Play();
            else if (!on && _stream.isEmitting) _stream.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            if (_stream != null) Destroy(_stream.gameObject);
        }

        private static GUIStyle _hint;

        private void OnGUI()
        {
            if (!IsOwner || !(NetworkObject.Owner != null && NetworkObject.Owner.IsValid)) return;
            string left = _leftNet.Value < 0 ? "∞" : _leftNet.Value.ToString();
            string t = _leftNet.Value == 0 ? "Мешок ПУСТОЙ   ·   ЛКМ — кинуть, G — поставить"
                : $"Мешок: {left} порц.   ·   ПКМ (держать) — сыпать на глаз   ·   ЛКМ — кинуть (держать — дальше)   ·   G — поставить";
            if (_hint == null) { _hint = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true }; _hint.normal.textColor = Color.white; }
            float w = Mathf.Min(760f, Screen.width - 40f);
            GUI.Box(new Rect((Screen.width - w) * 0.5f, Screen.height - 110f, w, 44f), t, _hint);
        }

        private void FixedUpdate()
        {
            if (!_flying || !IsServerInitialized || _rb == null) return;
            // поймать: кот с пустыми руками рядом (не тот, кто кинул, первые 0.4 с)
            int n = Physics.OverlapSphereNonAlloc(_rb.worldCenterOfMass, _catchRadius, _near, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                PlayerHand hand = _near[i].GetComponentInParent<PlayerHand>();
                if (hand == null || hand.HasItem) continue;
                if (hand == _thrower && Time.time - _thrownAt < 0.4f) continue;
                _flying = false;
                if (hand.ServerTryHold(this))
                {
                    if (_log) Debug.Log($"[Мешок] «{name}» поймали!", this);
                    return;
                }
            }
            // лёг и не движется — полёт закончен
            if (_rb.linearVelocity.sqrMagnitude < 0.25f) _slowFor += Time.fixedDeltaTime; else _slowFor = 0f;
            if (_slowFor > 0.4f || Time.time - _thrownAt > 6f) _flying = false;
        }

        private void OnCollisionEnter(Collision c)
        {
            if (!_flying || !IsServerInitialized) return;
            if (Time.time - _thrownAt < 0.15f) return;                            // ещё вылетает из рук
            if (c.collider.GetComponentInParent<PlayerHand>() != null) return;     // в кота — ловля решает FixedUpdate
            if (c.relativeVelocity.magnitude < _burstSpeed) return;
            _flying = false;
            Burst(c.contactCount > 0 ? c.GetContact(0).point : transform.position);
        }

        [Server]
        private void Burst(Vector3 at)
        {
            if (_sack == null || !_sack.PeekScoop(out ushort product, out FoodState state))
            {
                PuffRpc(at, new Color(0.85f, 0.8f, 0.7f));
                return;
            }
            // сколько высыпется: доля оставшегося (у бесконечного — Min Loss)
            int lost = _sack.ServerScoop(Mathf.Max(_minLoss, Mathf.CeilToInt(Left() * _loss)), out product, out state, out byte cook, out byte clean);
            if (lost > 0)
            {
                FoodSubstanceTable t = FoodSubstanceTable.Instance;
                FoodHeap pf = t != null ? t.FloorHeapPrefab : null;
                if (pf != null)
                {
                    Vector3 p = at;
                    if (Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out RaycastHit h, 3f, ~0, QueryTriggerInteraction.Ignore)) p = h.point;
                    FoodHeap heap = Instantiate(pf, p + Vector3.up * 0.05f, Quaternion.identity);
                    heap.ServerPreset(product, state, lost, cook, (byte)Mathf.Min(clean, (byte)70), true);   // с пола — уже не чистое
                    Spawn(heap.gameObject);
                }
            }
            // легче на столько, сколько высыпалось
            int left = Left();
            if (_rb != null && _baseMass > 0f && _startLeft > 0 && left >= 0)
                _rb.mass = Mathf.Max(_baseMass * 0.25f, _baseMass * left / (float)_startLeft);
            ProductDef pd = FoodDatabase.Instance != null ? FoodDatabase.Instance.Get(product) : null;
            PuffRpc(at, pd != null ? pd.CrumbColor : new Color(0.92f, 0.9f, 0.85f));
            if (_log) Debug.Log($"[Мешок] «{name}» ПОРВАЛСЯ: высыпалось {lost} порц.{(left >= 0 ? $", осталось {left}" : "")}", this);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            // запас мешка на старте (BulkSack мог ещё не выставить _left — берём настройку Supply)
            var inf = typeof(BulkSack).GetField("_infinite", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var sup = typeof(BulkSack).GetField("_supply", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            bool infinite = inf != null && _sack != null && (bool)inf.GetValue(_sack);
            _startLeft = !infinite && sup != null && _sack != null ? (int)sup.GetValue(_sack) : -1;
        }

        /// <summary>Сколько осталось (−1 — бесконечный).</summary>
        private int Left()
        {
            if (_sack == null) return -1;
            var f = typeof(BulkSack).GetField("_infinite", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (f != null && (bool)f.GetValue(_sack)) return -1;
            var l = typeof(BulkSack).GetField("_left", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return l != null ? (int)l.GetValue(_sack) : -1;
        }

        [ObserversRpc(RunLocally = true)]
        private void PuffRpc(Vector3 at, Color color)
        {
            if (Application.isBatchMode) return;
            var go = new GameObject("Sack Puff");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.4f; main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            main.startColor = new Color(color.r, color.g, color.b, 0.85f);
            main.gravityModifier = 0.25f;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.4f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                      new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            Shader sh2 = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh2 != null) r.sharedMaterial = new Material(sh2) { color = Color.white };
            ps.Play();
        }
    }
}
