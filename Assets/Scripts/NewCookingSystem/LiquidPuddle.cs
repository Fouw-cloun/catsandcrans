using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЛУЖА — пролитая жидкость (молоко, сливки, вода).
    ///
    ///   • РАЗМЕР ПО ОБЪЁМУ: площадь = объём ÷ толщина слоя. Вылил ведро —
    ///     большая лужа, стакан — маленькая. Объём порции и толщину задаёшь сам.
    ///   • РАСТЕКАНИЕ: новая лужа не появляется сразу во весь размер — плавно
    ///     растекается от точки падения, быстро в начале и медленно в конце.
    ///   • ШЕВЕЛЕНИЕ: край клякса и медленно «дышит», по поверхности идёт
    ///     лёгкая рябь (в шейдере), там, куда бьёт струя, — расходятся круги.
    ///   • ВЫСЫХАНИЕ: через Dry Delay начинает сохнуть — уменьшается и
    ///     бледнеет, потом исчезает совсем.
    ///   • МЯГКИЙ КРАЙ: полупрозрачная кромка (CatCooks/Liquid Surface), без
    ///     вырезанного диска.
    ///
    /// По сети — продукт и объём; форму, рябь и круги каждый рисует сам.
    ///
    /// ═══ ПРЕФАБ «MilkPuddle» ═══
    /// Пустой объект: NetworkObject + LiquidPuddle. Больше ничего.
    /// </summary>
    public sealed class LiquidPuddle : NetworkBehaviour
    {
        [Header("Объём → размер")]
        [Tooltip("Объём одной порции, литров (как в таре: полное ведро = Capacity порций)")]
        [Min(0.01f)][SerializeField] private float _litresPerPortion = 1f;
        [Tooltip("Толщина слоя лужи, мм. Меньше — шире лужа при том же объёме.")]
        [Range(0.5f, 20f)][SerializeField] private float _thicknessMm = 3f;
        [Tooltip("Больше этого радиуса лужа не растёт, м")]
        [SerializeField] private float _maxRadius = 6f;

        [Header("Растекание")]
        [Tooltip("За сколько секунд растекается до полного размера")]
        [Min(0.1f)][SerializeField] private float _spreadSeconds = 2.5f;
        [Tooltip("Неровность края: 0 — круг, 0.4 — клякса")]
        [Range(0f, 0.6f)][SerializeField] private float _edgeNoise = 0.28f;
        [Tooltip("Как быстро край «дышит»")]
        [Range(0f, 1f)][SerializeField] private float _edgeBreath = 0.25f;
        [Tooltip("Ширина мягкой кромки, доля радиуса")]
        [Range(0.02f, 0.5f)][SerializeField] private float _softEdge = 0.18f;
        [SerializeField] private float _lift = 0.006f;

        [Header("Высыхание")]
        [Tooltip("Через сколько секунд после последнего пролива начинает сохнуть")]
        [SerializeField] private float _dryDelay = 25f;
        [Tooltip("Литров в секунду испаряется")]
        [SerializeField] private float _dryLitresPerSecond = 0.15f;
        [Tooltip("Любая лужа (даже ведро теста) высыхает не дольше чем за столько секунд после начала высыхания. 0 — без предела")]
        [Min(0f)][SerializeField] private float _dryAllSeconds = 30f;

        [Header("Вид")]
        [Tooltip("Пусто — CatCooks/Liquid Surface")]
        [SerializeField] private Material _material;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly SyncVar<ushort> _product = new SyncVar<ushort>(0);
        /// <summary>Объём в сантилитрах (0.01 л) — ushort хватит до 655 л.</summary>
        private readonly SyncVar<ushort> _volumeCl = new SyncVar<ushort>(0);

        private static readonly List<LiquidPuddle> All = new List<LiquidPuddle>();
        private static Material _autoMat;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int RingsId = Shader.PropertyToID("_Rings");
        private static readonly int AgitationId = Shader.PropertyToID("_Agitation");

        private Mesh _mesh;
        private MeshRenderer _mr;
        private MaterialPropertyBlock _mpb;
        private float _radius, _spreadT;
        private float _lastPour, _volumeServer, _presetLitres, _dryFrom = -1f;
        private ushort _presetProduct;
        private float _rebuildT;
        private readonly Vector4[] _rings = { new Vector4(0, 0, 0, -1), new Vector4(0, 0, 0, -1),
                                              new Vector4(0, 0, 0, -1), new Vector4(0, 0, 0, -1) };
        private int _ringNext;
        private float _ringCool;
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Color32> _c = new List<Color32>();
        private readonly List<int> _t = new List<int>();

        public ushort ProductId => _product.Value;
        public float Litres => _volumeCl.Value / 100f;
        /// <summary>Радиус при полном растекании, м.</summary>
        public float TargetRadius
        {
            get
            {
                float area = Litres * 0.001f / Mathf.Max(1e-5f, _thicknessMm * 0.001f);
                return Mathf.Min(_maxRadius, Mathf.Sqrt(area / Mathf.PI));
            }
        }
        public float Radius => _radius;

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        /// <summary>Пролить порции: в лежащую рядом лужу того же продукта или новую.</summary>
        public static LiquidPuddle ServerAddAt(LiquidPuddle prefab, ushort product, Vector3 point, int portions,
                                               float mergeSlack = 0.3f)
        {
            if (portions <= 0) return null;
            LiquidPuddle best = null;
            float bestD = float.MaxValue;
            foreach (LiquidPuddle p in All)
            {
                if (p == null || !p.IsSpawned || p._product.Value != product) continue;
                Vector3 d = p.transform.position - point;
                if (Mathf.Abs(d.y) > 0.4f) continue;
                float h = new Vector2(d.x, d.z).magnitude;
                if (h > p.TargetRadius + mergeSlack || h >= bestD) continue;
                best = p; bestD = h;
            }
            if (best != null) { best.ServerAddLitres(portions * best._litresPerPortion); return best; }
            if (prefab == null) return null;
            LiquidPuddle nw = Instantiate(prefab, point, Quaternion.identity);
            nw._presetProduct = product;
            nw._presetLitres = portions * nw._litresPerPortion;
            FishNet.InstanceFinder.ServerManager.Spawn(nw.gameObject);
            return nw;
        }

        public static LiquidPuddle FindNear(Vector3 point, float slack = 0.2f)
        {
            foreach (LiquidPuddle p in All)
            {
                if (p == null) continue;
                Vector3 d = p.transform.position - point;
                if (Mathf.Abs(d.y) > 0.4f) continue;
                if (new Vector2(d.x, d.z).magnitude <= p._radius + slack) return p;
            }
            return null;
        }

        [Server]
        private void ServerAddLitres(float litres)
        {
            _volumeServer = Mathf.Min(655f, _volumeServer + litres);
            _volumeCl.Value = (ushort)Mathf.RoundToInt(_volumeServer * 100f);
            _lastPour = Time.time;
            _dryFrom = -1f;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (_presetProduct != 0) _product.Value = _presetProduct;
            _volumeServer = 0f;
            ServerAddLitres(Mathf.Max(0.01f, _presetLitres));
            if (_log) Debug.Log($"[Лужа] новая, {Litres:0.##} л", this);
        }

        private void Update()
        {
            if (base.IsServerInitialized) ServerDry(Time.deltaTime);
        }

        private void ServerDry(float dt)
        {
            if (Time.time - _lastPour < _dryDelay) return;
            if (_dryFrom < 0f)
            {
                _dryFrom = _volumeServer;
                if (_log) Debug.Log($"[Лужа] сохнет: {Litres:0.##} л, исчезнет за ~{(_dryAllSeconds > 0f ? Mathf.Min(_dryAllSeconds, _dryFrom / Mathf.Max(0.001f, _dryLitresPerSecond)) : _dryFrom / Mathf.Max(0.001f, _dryLitresPerSecond)):0} с", this);
            }
            float rate = _dryLitresPerSecond;
            if (_dryAllSeconds > 0f) rate = Mathf.Max(rate, _dryFrom / _dryAllSeconds);
            _volumeServer -= rate * dt;
            if (_volumeServer <= 0.02f) { base.Despawn(base.NetworkObject); return; }
            ushort cl = (ushort)Mathf.RoundToInt(_volumeServer * 100f);
            if (Mathf.Abs(cl - _volumeCl.Value) >= 3) _volumeCl.Value = cl;
        }

        // ───────────────────────── ВИД (у всех) ─────────────────────────

        public override void OnStartClient()
        {
            base.OnStartClient();
            GameObject go = new GameObject("PuddleMesh (auto)") { hideFlags = HideFlags.DontSave, layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * _lift;
            _mesh = new Mesh { name = "Puddle", hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _mr = go.AddComponent<MeshRenderer>();
            _mr.sharedMaterial = _material != null ? _material : AutoMaterial;
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mpb = new MaterialPropertyBlock();
        }

        private static Material AutoMaterial
        {
            get
            {
                if (_autoMat != null) return _autoMat;
                Shader sh = Shader.Find("CatCooks/Liquid Surface");
                if (sh == null) sh = Shader.Find("CatCooks/Food Heap");
                _autoMat = new Material(sh) { name = "CC Puddle (auto)", hideFlags = HideFlags.DontSave };
                if (_autoMat.HasProperty("_Opacity")) _autoMat.SetFloat("_Opacity", 0.92f);
                if (_autoMat.HasProperty("_EdgeSoftness")) _autoMat.SetFloat("_EdgeSoftness", 0.9f);
                if (_autoMat.HasProperty("_RippleStrength")) _autoMat.SetFloat("_RippleStrength", 0.25f);
                return _autoMat;
            }
        }

        /// <summary>Сюда бьёт струя — круг по поверхности (визуал).</summary>
        public void Ripple(Vector3 point)
        {
            if (_ringCool > 0f) return;
            _ringCool = 0.12f;
            _rings[_ringNext] = new Vector4(point.x, point.y, point.z, 0f);
            _ringNext = (_ringNext + 1) % _rings.Length;
        }

        /// <summary>Совместимость со старым вызовом (бидон): круг + пара капель.</summary>
        public void LocalRipple(Vector3 point, Color color) => Ripple(point);

        private void LateUpdate()
        {
            if (_mesh == null) return;
            float dt = Time.deltaTime;
            float target = TargetRadius;

            // Растекание: быстро в начале, медленно в конце; усыхание — плавно.
            if (target > _radius)
            {
                _spreadT = Mathf.Min(1f, _spreadT + dt / _spreadSeconds);
                float k = 1f - Mathf.Pow(1f - _spreadT, 3f);
                _radius = Mathf.Lerp(_radius, target, Mathf.Max(k * dt * 6f, 0.02f));
            }
            else
            {
                _spreadT = 0f;
                _radius = Mathf.MoveTowards(_radius, target, dt * 0.05f);
            }

            // Форма «дышит» — перестраиваем не каждый кадр (10 раз в секунду хватает глазу).
            _rebuildT -= dt;
            if (_rebuildT <= 0f) { _rebuildT = 0.1f; Rebuild(); }

            _ringCool -= dt;
            for (int i = 0; i < _rings.Length; i++)
                if (_rings[i].w >= 0f) { _rings[i].w += dt; if (_rings[i].w > 1.4f) _rings[i].w = -1f; }

            FoodDatabase db = FoodDatabase.Instance;
            ProductDef def = db != null ? db.Get(_product.Value) : null;
            Color col = def != null ? def.BaseColor : new Color(0.97f, 0.97f, 0.95f);
            // Высыхая, бледнеет и становится прозрачнее.
            float dry = Mathf.Clamp01(Litres / Mathf.Max(0.01f, _litresPerPortion * 2f));
            col.a = 1f;
            _mpb.SetColor(BaseColorId, Color.Lerp(col * 0.92f, col, dry));
            _mpb.SetVectorArray(RingsId, _rings);
            _mpb.SetFloat(AgitationId, _spreadT < 1f && target > _radius + 0.01f ? 0.5f : 0f);
            _mr.SetPropertyBlock(_mpb);
            _mr.enabled = _radius > 0.005f;
        }

        private void Rebuild()
        {
            const int seg = 56;
            int seed = base.IsSpawned ? base.ObjectId : 1;
            float ph1 = (seed * 0.37f) % 6.28f, ph2 = (seed * 0.91f) % 6.28f, ph3 = (seed * 1.73f) % 6.28f;
            float breath = Time.time * _edgeBreath;
            float inner = 1f - _softEdge;

            _v.Clear(); _c.Clear(); _t.Clear();
            _v.Add(Vector3.zero); _c.Add(new Color32(255, 255, 255, 255));
            // Два кольца: внутреннее (полная непрозрачность) и внешнее (мягкая кромка, альфа 0).
            for (int ring = 0; ring < 2; ring++)
            {
                float rk = ring == 0 ? inner : 1f;
                byte alpha = ring == 0 ? (byte)255 : (byte)0;
                for (int i = 0; i < seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    float k = 1f + _edgeNoise * (0.55f * Mathf.Sin(a * 3f + ph1 + breath) +
                                                 0.30f * Mathf.Sin(a * 5f + ph2 - breath * 1.3f) +
                                                 0.15f * Mathf.Sin(a * 9f + ph3 + breath * 0.7f));
                    _v.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * _radius * k * rk);
                    _c.Add(new Color32(255, 255, 255, alpha));
                }
            }
            for (int i = 0; i < seg; i++)
            {
                int a0 = 1 + i, a1 = 1 + (i + 1) % seg;
                _t.Add(0); _t.Add(a1); _t.Add(a0);                           // центр → внутреннее
                int b0 = 1 + seg + i, b1 = 1 + seg + (i + 1) % seg;
                _t.Add(a0); _t.Add(a1); _t.Add(b0);                          // внутреннее → внешнее
                _t.Add(a1); _t.Add(b1); _t.Add(b0);
            }
            _mesh.Clear();
            _mesh.SetVertices(_v);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (_mesh != null) SafeDestroy.Kill(_mesh);
        }
    }
}
