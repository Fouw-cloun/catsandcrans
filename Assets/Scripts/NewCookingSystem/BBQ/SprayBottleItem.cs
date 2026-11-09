using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ПУЛЬВЕРИЗАТОР — брызгать на мясо, чтобы не сохло.
    ///   ЛКМ, глядя на тару с «Моп-соусом» (или водой), — заправить (до Capacity).
    ///   ЛКМ в любую другую сторону — пшик: облачко мороси; попал на рёбра — влажность +.
    /// Моп-соус лучше воды (вода — 70% и без бонуса к оценке).
    /// ═══ НА КОРНЕ ═══ NetworkObject · NetworkTransform · Rigidbody · коллайдер · этот компонент.
    /// Nozzle — дочерняя точка у сопла, СИНЯЯ ось (Z) — куда брызгает (меню ставит само).
    /// </summary>
    public sealed class SprayBottleItem : PickupItem
    {
        [SerializeField] private Transform _nozzle;
        [Tooltip("Сколько порций влезает")]
        [SerializeField] private float _capacity = 10f;
        [Tooltip("Порций на один пшик")]
        [SerializeField] private float _perSpray = 0.25f;
        [Tooltip("Дальность пшика, м")]
        [SerializeField] private float _range = 2.2f;
        [Tooltip("Ширина облачка у цели, м")]
        [SerializeField] private float _spread = 0.35f;
        [SerializeField] private float _cooldown = 0.25f;
        [SerializeField] private ProductDef _mop;
        [SerializeField] private ProductDef _water;
        [SerializeField] private bool _log = true;

        private readonly SyncVar<ushort> _product = new SyncVar<ushort>(0);
        private readonly SyncVar<ushort> _amount = new SyncVar<ushort>(0);   // сотые порции
        private float _next;
        private ParticleSystem _mist;

        public float Amount => _amount.Value / 100f;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            if (_nozzle == null) _nozzle = transform.Find("Nozzle");
        }

        private void Resolve()
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return;
            if (_mop == null) _mop = MacaronTitles.Find(db, BBQTitles.Mop);
            if (_water == null) _water = MacaronTitles.Find(db, BBQTitles.Water);
        }

        private bool Allowed(ushort p) => (_mop != null && p == _mop.Id) || (_water != null && p == _water.Id);

        public override void ServerPrimaryUse(UseContext ctx)
        {
            if (Time.time < _next) return;
            _next = Time.time + _cooldown;
            Resolve();
            Vector3 dir = ctx.Direction.sqrMagnitude > 1e-6f ? ctx.Direction.normalized : transform.forward;
            RaycastHit[] hits = Physics.RaycastAll(ctx.Origin, dir, _range + 0.6f, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            RibsRack rack = null; Cookware cw = null; Vector3 at = ctx.Origin + dir * _range;
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
                if (ctx.Hand != null && h.collider.transform.IsChildOf(ctx.Hand.transform.root)) continue;
                RibsRack r = h.collider.GetComponentInParent<RibsRack>();
                if (r != null) { rack = r; at = h.point; break; }
                Cookware c = h.collider.GetComponentInParent<Cookware>();
                if (c != null) { cw = c; at = h.point; break; }
                if (!h.collider.isTrigger) { at = h.point; break; }
            }

            // заправить из тары
            if (cw != null && rack == null && Fill(cw)) return;

            if (_amount.Value < 2) { if (_log) Debug.Log("[Пульверизатор] пусто — ЛКМ по ведру с моп-соусом: заправить", this); return; }
            float use = Mathf.Min(Amount, _perSpray);
            _amount.Value = (ushort)Mathf.Max(0, Mathf.RoundToInt((Amount - use) * 100f));
            bool mop = _mop != null && _product.Value == _mop.Id;
            // попали облачком: прямой луч или рёбра рядом с точкой
            if (rack == null)
                foreach (Collider c in Physics.OverlapSphere(at, _spread, ~0, QueryTriggerInteraction.Ignore))
                {
                    RibsRack r = c.GetComponentInParent<RibsRack>();
                    if (r != null) { rack = r; break; }
                }
            if (rack != null) rack.ServerSpray(use, mop);
            else if (_log) Debug.Log("[Пульверизатор] пшик мимо рёбер", this);
            FoodDatabase db = FoodDatabase.Instance;
            ProductDef d = db != null ? db.Get(_product.Value) : null;
            Color col = d != null ? d.BaseColor : new Color(0.8f, 0.6f, 0.4f);
            RpcMist(NozzlePos(), (at - NozzlePos()).normalized, Vector3.Distance(NozzlePos(), at), col);
            if (_amount.Value < 2) { _amount.Value = 0; _product.Value = 0; if (_log) Debug.Log("[Пульверизатор] закончился — заправь", this); }
        }

        private bool Fill(Cookware cw)
        {
            float room = _capacity - Amount;
            ushort pick = 0;
            for (int i = 0; i < cw.ViewCount; i++)
            {
                BagView v = cw.GetView(i);
                if (v.Count > 0 && Allowed(v.Product) && (_product.Value == 0 || _product.Value == v.Product || Amount < 0.02f)) { pick = v.Product; break; }
            }
            if (pick == 0)
            {
                if (_log) Debug.Log($"[Пульверизатор] в «{(cw.Def != null ? cw.Def.Title : cw.name)}» нет моп-соуса (или воды) — заправлять нечем", this);
                return true;     // смотрел на тару — не пшикаем
            }
            if (room < 0.5f) { if (_log) Debug.Log("[Пульверизатор] уже полный", this); return true; }
            int took = cw.ServerTake(pick, Mathf.FloorToInt(room));
            if (took <= 0) return true;
            if (_product.Value != pick) _product.Value = pick;
            _amount.Value = (ushort)Mathf.RoundToInt(Mathf.Min(_capacity, Amount + took) * 100f);
            if (_log) Debug.Log($"[Пульверизатор] заправлен: {Amount:0.#}/{_capacity:0} ({(_mop != null && pick == _mop.Id ? "моп-соус" : "вода")}) — ЛКМ по рёбрам: брызгать", this);
            return true;
        }

        private Vector3 NozzlePos() => _nozzle != null ? _nozzle.position : transform.position + transform.forward * 0.1f;

        [ObserversRpc]
        private void RpcMist(Vector3 from, Vector3 dir, float dist, Color color)
        {
            if (_mist == null) _mist = MakeMist();
            if (_mist == null) return;
            var ep = new ParticleSystem.EmitParams();
            color.a = 0.55f;
            for (int i = 0; i < 26; i++)
            {
                Vector3 jitter = Random.insideUnitSphere * 0.18f;
                ep.position = from;
                ep.velocity = (dir + jitter).normalized * Mathf.Max(1.5f, dist * Random.Range(1.6f, 2.4f));
                ep.startSize = Random.Range(0.02f, 0.05f);
                ep.startLifetime = Random.Range(0.35f, 0.6f);
                ep.startColor = Color.Lerp(color, Color.white, 0.4f);
                _mist.Emit(ep, 1);
            }
        }

        private ParticleSystem MakeMist()
        {
            var go = new GameObject("SprayMist (auto)");
            go.SetActive(false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            var m = ps.main;
            m.playOnAwake = false; m.loop = false; m.maxParticles = 400;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.gravityModifier = 0.3f;
            m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var sl = ps.sizeOverLifetime; sl.enabled = true;
            sl.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.8f)));
            var cl = ps.colorOverLifetime; cl.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            cl.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Shader shd = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            if (shd != null) r.sharedMaterial = new Material(shd) { name = "SprayMist (auto)" };
            go.SetActive(true);
            return ps;
        }

        private void OnGUI()
        {
            if (!base.IsOwner || Event.current.type != EventType.Repaint) return;
            if (Time.frameCount - _poseFrame > 2) return;
            FoodDatabase db = FoodDatabase.Instance;
            ProductDef d = db != null && _product.Value != 0 ? db.Get(_product.Value) : null;
            string s = Amount < 0.02f ? "пульверизатор пустой — ЛКМ по ведру с моп-соусом" : $"пульверизатор: {(d != null ? d.Title : "?")} {Amount:0.#}/{_capacity:0} · ЛКМ — пшик";
            GUI.Label(new Rect(Screen.width * 0.5f - 220f, Screen.height * 0.66f, 440f, 24f), s);
        }

        private int _poseFrame = -10;
        public override void UpdateHeldPose(Transform handAnchor)
        {
            _poseFrame = Time.frameCount;
            base.UpdateHeldPose(handAnchor);
        }

#if UNITY_EDITOR
        public void EditorSetup(Transform nozzle) { _nozzle = nozzle; UnityEditor.EditorUtility.SetDirty(this); }
#endif
    }
}
