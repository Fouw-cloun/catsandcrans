using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>То, что можно долбить шпателем (кроме шара хлебного теста): залипший валец, застрявшая кувалда…
    /// Шпатель бьёт в точку; сервер выбирает ближайшую цель из DoughStick.Spatulable.</summary>
    public interface ISpatulaTarget
    {
        /// <summary>Насколько точка удара близко к тому, что надо долбить (м). float.MaxValue — нечего долбить.</summary>
        float SpatulaDistance(Vector3 point);
        /// <summary>Удар шпателем на сервере. false + why — нечего тут рубить.</summary>
        bool ServerSpatulaHit(Vector3 point, Vector3 dir, out string why);
    }

    /// <summary>Мука, налипшее тесто, тяжи — общие мелочи для инструментов.</summary>
    public static class DoughStick
    {
        public static readonly List<ISpatulaTarget> Spatulable = new List<ISpatulaTarget>();

        /// <summary>Сервер: ближайшая цель для шпателя около точки (не дальше maxDist).</summary>
        public static ISpatulaTarget NearestSpatulable(Vector3 p, float maxDist)
        {
            ISpatulaTarget best = null; float bd = maxDist;
            foreach (ISpatulaTarget t in Spatulable)
            {
                if (t == null || (t is Object o && o == null)) continue;
                float d = t.SpatulaDistance(p);
                if (d < bd) { bd = d; best = t; }
            }
            return best;
        }

        public static bool IsFlour(ushort product)
        {
            FoodDatabase db = FoodDatabase.Instance;
            ProductDef p = db != null ? db.Get(product) : null;
            return p != null && !string.IsNullOrEmpty(p.Title) && p.Title.ToLowerInvariant().StartsWith("мука");
        }

        public static readonly Color DoughColor = new Color(0.97f, 0.94f, 0.86f);
        public static readonly Color FlourColor = new Color(0.98f, 0.97f, 0.94f);

        private static Material _lumpMat;
        /// <summary>Материал налипшего теста (URP Lit, цвет теста).</summary>
        public static Material LumpMaterial()
        {
            if (_lumpMat != null) return _lumpMat;
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            _lumpMat = new Material(s) { name = "DoughLump (runtime)" };
            if (_lumpMat.HasProperty("_BaseColor")) _lumpMat.SetColor("_BaseColor", DoughColor);
            if (_lumpMat.HasProperty("_Color")) _lumpMat.SetColor("_Color", DoughColor);
            if (_lumpMat.HasProperty("_Smoothness")) _lumpMat.SetFloat("_Smoothness", 0.3f);
            return _lumpMat;
        }

        /// <summary>Фигура БЕЗ коллайдера (коллайдер убираем сразу — DestroyImmediate).</summary>
        public static Transform MakeLump(Transform parent, string name, PrimitiveType type)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.GetComponent<MeshRenderer>().sharedMaterial = LumpMaterial();
            go.transform.SetParent(parent, false);
            go.SetActive(false);
            return go.transform;
        }

        /// <summary>Белёсый налёт муки на рендерерах (MPB). coat 0..1.</summary>
        public static void Tint(Renderer[] rs, Color[] baseCols, float coat, MaterialPropertyBlock mpb)
        {
            if (rs == null) return;
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null) continue;
                rs[i].GetPropertyBlock(mpb);
                Color c = Color.Lerp(baseCols[i], FlourColor, coat * 0.75f);
                mpb.SetColor("_BaseColor", c); mpb.SetColor("_Color", c);
                rs[i].SetPropertyBlock(mpb);
            }
        }

        public static Color[] BaseColors(Renderer[] rs)
        {
            var cols = new Color[rs.Length];
            for (int i = 0; i < rs.Length; i++)
            {
                Material m = rs[i] != null ? rs[i].sharedMaterial : null;
                cols[i] = m == null ? Color.white : m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            }
            return cols;
        }

        /// <summary>Расстояние от точки до отрезка ab.</summary>
        public static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a; float l2 = ab.sqrMagnitude;
            float t = l2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / l2) : 0f;
            return (p - (a + ab * t)).magnitude;
        }
    }

    /// <summary>
    /// ТЯЖ ТЕСТА: мягкая «жвачка» от пласта к инструменту. У пласта — плавный бугорок, посередине тянется и тоньшает
    /// (объём тот же), у инструмента — комок. Длинный провисает. Меш свой, в мировых координатах, без коллайдера.
    /// </summary>
    public sealed class DoughStrand
    {
        private const int Rings = 16, Seg = 14;
        private readonly GameObject _go;
        private readonly Mesh _mesh;
        private readonly Vector3[] _v = new Vector3[Rings * Seg + 2];   // + центры крышек (низ, верх)
        private readonly int[] _tri;

        public DoughStrand(string name)
        {
            _go = new GameObject(name);
            _mesh = new Mesh { name = name };
            _mesh.MarkDynamic();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _go.AddComponent<MeshRenderer>().sharedMaterial = DoughStick.LumpMaterial();
            // наружу лицом (в Unity лицевая — по часовой, если смотреть снаружи); с крышками — не «полворонки»
            _tri = new int[(Rings - 1) * Seg * 6 + Seg * 6];
            int k = 0, B = Rings * Seg, T = B + 1;
            for (int r = 0; r < Rings - 1; r++)
                for (int s = 0; s < Seg; s++)
                {
                    int a = r * Seg + s, b = r * Seg + (s + 1) % Seg, c = a + Seg, d = b + Seg;
                    _tri[k++] = a; _tri[k++] = b; _tri[k++] = c;
                    _tri[k++] = b; _tri[k++] = d; _tri[k++] = c;
                }
            for (int s = 0; s < Seg; s++)
            {
                int s1 = (s + 1) % Seg, top = (Rings - 1) * Seg;
                _tri[k++] = top + s; _tri[k++] = top + s1; _tri[k++] = T;   // верх — купол-комок у инструмента
                _tri[k++] = B; _tri[k++] = s1; _tri[k++] = s;               // низ (в тесте)
            }
            _go.SetActive(false);
        }

        public void Hide() { if (_go != null && _go.activeSelf) _go.SetActive(false); }
        public void Destroy() { if (_go != null) Object.Destroy(_go); if (_mesh != null) Object.Destroy(_mesh); }

        /// <summary>a — у теста (на поверхности), b — у инструмента. r0 — толщина в покое, rest — длина без растяжки.
        /// wide — во сколько раз сечение шире вбок (лента у вальца: 4–6, круглый тяж: 1). side — направление «вбок» для ленты.</summary>
        public void Set(Vector3 a, Vector3 b, float r0, float rest, float wide, Vector3 side)
        {
            if (_go == null) return;
            if (!_go.activeSelf) _go.SetActive(true);
            Vector3 ab = b - a; float L = Mathf.Max(0.01f, ab.magnitude);
            Vector3 dir = ab / L;
            float horiz = new Vector2(ab.x, ab.z).magnitude / L;
            float sag = Mathf.Max(0f, L - rest) * 0.18f * horiz;                      // длинный — провисает
            float neck = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(0.05f, rest) / L), 0.22f, 1f); // тянется — тоньшает
            float rMid = r0 * neck, rBase = r0 * 2.4f, rTop = r0 * 1.35f;
            if (side.sqrMagnitude < 1e-6f || Mathf.Abs(Vector3.Dot(side.normalized, dir)) > 0.95f) side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            // путь — кривая: от теста сначала ВВЕРХ (основание лежит на пласте ровно и не крутится), потом к инструменту
            Vector3 p1 = a + Vector3.up * Mathf.Min(L * 0.4f, 0.35f);
            for (int r = 0; r < Rings; r++)
            {
                float t = r / (float)(Rings - 1), u = 1f - t;
                Vector3 c = u * u * a + 2f * u * t * p1 + t * t * b + Vector3.down * (sag * 4f * t * (1f - t));
                if (r == 0) c += Vector3.down * r0 * 0.6f;                              // основание утоплено в пласт
                float rad = rMid + (rBase - rMid) * Mathf.Exp(-t * 9f) + (rTop - rMid) * Mathf.Exp(-(1f - t) * 12f);
                Vector3 tan = 2f * u * (p1 - a) + 2f * t * (b - p1);
                tan = tan.sqrMagnitude > 1e-8f ? tan.normalized : Vector3.up;
                Vector3 sx = Vector3.ProjectOnPlane(side, tan);
                if (sx.sqrMagnitude < 1e-6f) sx = Vector3.ProjectOnPlane(Vector3.forward, tan);
                sx.Normalize();
                Vector3 sy = Vector3.Cross(tan, sx).normalized;
                float w = Mathf.Lerp(1f, wide, Mathf.Clamp01(Mathf.Min(t * 3f, (1f - t) * 3f) + 0.35f));
                for (int s = 0; s < Seg; s++)
                {
                    float ang = s / (float)Seg * Mathf.PI * 2f;
                    float wob = 1f + 0.06f * Mathf.Sin(ang * 3f + t * 7f);              // чуть неровный, живой
                    _v[r * Seg + s] = c + (sx * (Mathf.Cos(ang) * w) + sy * Mathf.Sin(ang)) * rad * wob;
                }
            }
            _v[Rings * Seg] = a + Vector3.down * r0 * 1.2f;
            Vector3 endTan = (b - p1).sqrMagnitude > 1e-8f ? (b - p1).normalized : dir;
            _v[Rings * Seg + 1] = b + endTan * rTop * 0.8f;
            _mesh.vertices = _v;
            if (_mesh.triangles.Length != _tri.Length) _mesh.triangles = _tri;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }
    }

    /// <summary>
    /// МУКА НА ИНСТРУМЕНТЕ (кувалда). Положи кувалду, возьми мастерком муку и кинь на неё (ПКМ) — ляжет налётом.
    /// Каждый удар по тесту тратит муку. Без муки кувалда ЗАСТРЕВАЕТ бойком в тесте:
    ///   • взять можно, но от теста к бойку тянется тяж — отойдёшь, и тесто тянет назад (вместе с игроком);
    ///   • бросишь — кувалду «примагнитит» обратно в тесто, она там пружинит;
    ///   • выдолбить — шпателем ЛКМ по месту, где она сидит (или по кувалде), 3 удара.
    /// ═══ НАСТРОЙКА ═══ CatCooks → Ньокки → 5. Мука на инструмент (выдели кувалду). Шпатели не липнут.
    /// </summary>
    public sealed class ToolFlourCoat : NetworkBehaviour, IScoopReceiver, ISpatulaTarget
    {
        [SerializeField] private string _title = "кувалда";
        [Tooltip("Боёк — им кувалда застревает в тесте. Пусто — центр инструмента")]
        [SerializeField] private Transform _stickPoint;
        [Tooltip("Мука за одну порцию мастерка (1 = полностью)")]
        [Range(0.1f, 1f)][SerializeField] private float _perPortion = 0.6f;
        [Tooltip("Мука на один удар по тесту")]
        [Range(0.02f, 0.5f)][SerializeField] private float _perHit = 0.15f;
        [Tooltip("Сколько ударов шпателем, чтобы выдолбить застрявшую кувалду")]
        [Range(1, 8)][SerializeField] private int _chipsToFree = 3;
        [Tooltip("Насколько боёк уходит в тесто, м")]
        [SerializeField] private float _sinkDepth = 0.12f;
        [Header("Тяж теста")]
        [Tooltip("Тяж свободно тянется до этой длины, м — дальше тянет игрока назад")]
        [SerializeField] private float _slack = 0.6f;
        [Tooltip("Сила, с которой тяж тянет игрока назад (на метр растяжки)")]
        [SerializeField] private float _pull = 16f;
        [Tooltip("Толщина тяжа, м")]
        [SerializeField] private float _strandRadius = 0.07f;
        [SerializeField] private float _springK = 70f;
        [SerializeField] private float _springDamp = 5f;
        [SerializeField] private bool _log = true;

        public static readonly List<ToolFlourCoat> All = new List<ToolFlourCoat>();

        private readonly SyncVar<byte> _coat = new SyncVar<byte>(0);            // 0..200 → 0..1
        private readonly SyncVar<bool> _stuckIn = new SyncVar<bool>(false);
        private readonly SyncVar<Vector3> _pinPos = new SyncVar<Vector3>(Vector3.zero);
        private readonly SyncVar<Quaternion> _pinRot = new SyncVar<Quaternion>(Quaternion.identity);
        private readonly SyncVar<Vector3> _anchor = new SyncVar<Vector3>(Vector3.zero);  // где боёк сидит в тесте (поверхность)
        private readonly SyncVar<byte> _chips = new SyncVar<byte>(0);

        private Renderer[] _rs;
        private Color[] _base;
        private MaterialPropertyBlock _mpb;
        private DoughStrand _strand;
        private Rigidbody _rb;
        private Collider _solid;
        private bool _pinnedLocal, _wasKin, _wasGrav, _prevHeld;
        private float _s, _v;                          // пружинка (м) у лежащей застрявшей
        private float _retT = 1f; private Vector3 _retPos; private Quaternion _retRot;   // «примагнитило» обратно
        private float _lookAt, _handsAt; private bool _looking;
        private static PlayerHand[] _hands = new PlayerHand[0];

        public string Title => _title;
        public float Coat01 => _coat.Value / 200f;
        public bool IsStuck => _stuckIn.Value;
        public Transform StickPoint => _stickPoint != null ? _stickPoint : transform;
        /// <summary>В руке у кого-то (у всех: в руке коллайдеры предмета выключены).</summary>
        public bool IsHeldNow => _solid != null ? !_solid.enabled : (_rb != null && _rb.isKinematic && !_stuckIn.Value);

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            foreach (Collider c in GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) { _solid = c; break; }
        }
        private void OnEnable() { if (!All.Contains(this)) All.Add(this); if (!DoughStick.Spatulable.Contains(this)) DoughStick.Spatulable.Add(this); }
        private void OnDisable() { All.Remove(this); DoughStick.Spatulable.Remove(this); }

        public override void OnStartClient()
        {
            base.OnStartClient();
            _coat.OnChange += (a, b, s) => RefreshLook();
            RefreshLook();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (_log) Debug.Log($"[Мука] «{name}» ({_title}): мука {Coat01 * 100f:0}% · кинь муку мастерком на инструмент, иначе застрянет в тесте", this);
        }

        /// <summary>Чем бьют в этой точке: инструмент в руке рядом (или лежащий незастрявший).</summary>
        public static ToolFlourCoat Nearest(Vector3 p, float maxDist)
        {
            ToolFlourCoat best = null; float bd = maxDist * maxDist; bool bestHeld = false;
            foreach (ToolFlourCoat t in All)
            {
                if (t == null) continue;
                bool held = t.IsHeldNow;
                if (t.IsStuck && !held) continue;
                float d = (t.StickPoint.position - p).sqrMagnitude;
                if (d > maxDist * maxDist) continue;
                if (best == null || (held && !bestHeld) || (held == bestHeld && d < bd)) { best = t; bd = d; bestHeld = held; }
            }
            return best;
        }

        /// <summary>Удар по тесту: true — мука была (потратили), false — голая, застрянет.</summary>
        [Server]
        public bool ServerUseHit()
        {
            float c = Coat01;
            if (c <= 0.001f) return false;
            c = Mathf.Max(0f, c - _perHit);
            _coat.Value = (byte)Mathf.RoundToInt(c * 200f);
            if (c <= 0.001f && _log) Debug.Log($"[Мука] «{name}»: на «{_title}» кончилась мука — следующий удар ЗАСТРЯНЕТ в тесте", this);
            return true;
        }

        /// <summary>Голой кувалдой по тесту: боёк застрял в точке удара (в руке остаётся, но привязан тяжем).</summary>
        [Server]
        public void ServerStickInto(Vector3 point)
        {
            if (_stuckIn.Value) return;
            Vector3 target = point - Vector3.up * _sinkDepth;
            _pinRot.Value = transform.rotation;
            _pinPos.Value = transform.position + (target - StickPoint.position);
            _anchor.Value = point;
            _chips.Value = (byte)Mathf.Clamp(_chipsToFree, 1, 8);
            _stuckIn.Value = true;
            RpcTug(1f);
            if (_log) Debug.Log($"[Мука] «{name}»: «{_title}» без муки — ЗАСТРЯЛА в тесте! Тесто держит тяжем: отойдёшь — потянет назад. Выдолби шпателем (ЛКМ по месту) ×{_chips.Value}, потом посыпь мукой", this);
        }

        public int ServerReceiveScoop(ushort product, FoodState state, int count, Vector3 point, Vector3 normal, out string why)
        {
            why = null;
            if (!DoughStick.IsFlour(product)) { why = $"на «{_title}» — только мука (чтобы не застревала в тесте)"; return 0; }
            if (Coat01 >= 0.99f) { why = $"«{_title}» уже в муке"; return 0; }
            float c = Mathf.Min(1f, Coat01 + _perPortion);
            _coat.Value = (byte)Mathf.RoundToInt(c * 200f);
            RpcDust(StickPoint.position, 1f);
            if (_log) Debug.Log($"[Мука] «{name}»: посыпали «{_title}» — мука {c * 100f:0}%" + (_stuckIn.Value ? " (застрявшую мука не вынет — долби шпателем)" : ""), this);
            return 1;
        }

        public float SpatulaDistance(Vector3 point)
        {
            if (!_stuckIn.Value) return float.MaxValue;
            return Mathf.Min(DoughStick.SegDist(point, _anchor.Value, StickPoint.position), (point - transform.position).magnitude);
        }

        public bool ServerSpatulaHit(Vector3 point, Vector3 dir, out string why)
        {
            why = null;
            if (!_stuckIn.Value) { why = $"«{_title}» не застряла — долбить нечего"; return false; }
            int left = Mathf.Max(0, _chips.Value - 1);
            _chips.Value = (byte)left;
            RpcDust(_anchor.Value + Vector3.up * 0.05f, 0.7f);
            if (left > 0)
            {
                RpcTug(0.5f);
                if (_log) Debug.Log($"[Мука] «{name}»: долбим тесто вокруг «{_title}» — ещё {left}", this);
                return true;
            }
            _stuckIn.Value = false;
            if (!IsHeldNow && _rb != null) { _rb.isKinematic = false; _rb.useGravity = true; }
            RpcTug(0.9f);
            if (_log) Debug.Log($"[Мука] «{name}»: «{_title}» выдолбили — свободна. Посыпь мукой, прежде чем бить снова", this);
            return true;
        }

        [ServerRpc(RequireOwnership = false)]
        private void CmdTug() { if (_stuckIn.Value) RpcTug(1f); }

        [ObserversRpc]
        private void RpcTug(float k) { _v += 2.2f * k; }

        [ObserversRpc]
        private void RpcDust(Vector3 at, float k)
        {
            CCRuntimeParticles.Burst(at, Vector3.up, DoughStick.FlourColor, new Color(0.9f, 0.88f, 0.82f), Mathf.RoundToInt(16 * k), CCRuntimeParticles.Look.Dust.Scaled(1.5f));
        }

        private void Update()
        {
            if (!_stuckIn.Value || IsHeldNow) return;
            if (Input.GetKeyDown(KeyCode.Mouse1) && _looking) CmdTug();     // ПКМ по лежащей — дёрнуть (пружинит)
        }

        private void LateUpdate()
        {
            bool stuck = _stuckIn.Value, held = IsHeldNow;
            if (!stuck)
            {
                if (_pinnedLocal)
                {
                    _pinnedLocal = false; _s = 0f; _v = 0f;
                    if (_rb != null && !base.IsServerInitialized && !held) { _rb.isKinematic = _wasKin; _rb.useGravity = _wasGrav; }
                }
                _strand?.Hide();
                _prevHeld = held;
                return;
            }
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (held)
            {
                _pinnedLocal = false;
                PullHolder(dt);
            }
            else
            {
                if (!_pinnedLocal && _rb != null) { _wasKin = _rb.isKinematic; _wasGrav = _rb.useGravity; _pinnedLocal = true; }
                if (_prevHeld) { _retT = 0f; _retPos = transform.position; _retRot = transform.rotation; }   // бросили — примагнитило назад
                _v += (-_springK * _s - _springDamp * _v) * dt;
                _s = Mathf.Clamp(_s + _v * dt, -0.03f, 0.6f);
                if (_rb != null) { _rb.isKinematic = true; _rb.useGravity = false; }
                Vector3 p = _pinPos.Value + Vector3.up * _s; Quaternion q = _pinRot.Value;
                if (_retT < 1f)
                {
                    _retT = Mathf.Min(1f, _retT + dt / 0.35f);
                    float e = 1f + 2.2f * Mathf.Pow(_retT - 1f, 3f) + 1.2f * Mathf.Pow(_retT - 1f, 2f);   // easeOutBack — с перелётом
                    p = Vector3.LerpUnclamped(_retPos, p, e); q = Quaternion.SlerpUnclamped(_retRot, q, Mathf.Clamp01(e));
                }
                transform.SetPositionAndRotation(p, q);
            }
            _prevHeld = held;
            if (_strand == null) _strand = new DoughStrand("DoughStrand_" + name);
            Vector3 head = StickPoint.position, a = _anchor.Value;
            _strand.Set(a, head + Vector3.down * 0.03f, _strandRadius, Mathf.Max(0.05f, _sinkDepth + 0.05f), 1f, Vector3.zero);
        }

        /// <summary>У держателя (свой игрок): дальше Slack — тяж тянет игрока назад к тесту.</summary>
        private void PullHolder(float dt)
        {
            if (!base.IsOwner) return;
            if (Time.time >= _handsAt) { _handsAt = Time.time + 1f; _hands = Object.FindObjectsByType<PlayerHand>(FindObjectsSortMode.None); }
            PlayerHand me = null;
            foreach (PlayerHand h in _hands) if (h != null && h.IsOwner && h.HeldItem != null && h.HeldItem.gameObject == gameObject) { me = h; break; }
            if (me == null) return;
            FirstPersonController fpc = me.GetComponent<FirstPersonController>();
            if (fpc == null) return;
            Vector3 d = _anchor.Value - StickPoint.position;
            float dist = d.magnitude;
            if (dist <= _slack) return;
            Vector3 dir = new Vector3(d.x, 0f, d.z).normalized;
            float k = (dist - _slack) * _pull * (dist > _slack * 4f ? 2.5f : 1f);
            fpc.AddPush(dir * k * dt);
        }

        private void OnDestroy() { _strand?.Destroy(); }

        private void RefreshLook()
        {
            if (_rs == null)
            {
                var list = new List<Renderer>();
                foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) if (r is MeshRenderer || r is SkinnedMeshRenderer) list.Add(r);
                _rs = list.ToArray(); _base = DoughStick.BaseColors(_rs); _mpb = new MaterialPropertyBlock();
            }
            DoughStick.Tint(_rs, _base, Coat01, _mpb);
        }

        private void OnGUI()
        {
            string t = null;
            bool held = IsHeldNow;
            if (_stuckIn.Value)
            {
                if (Time.time >= _lookAt)
                {
                    _lookAt = Time.time + 0.2f;
                    Camera cam = Camera.main;
                    _looking = cam != null && Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out RaycastHit h, 5f, ~0, QueryTriggerInteraction.Ignore)
                               && (h.collider.transform.IsChildOf(transform) || SpatulaDistance(h.point) < 0.8f);
                }
                if (held && base.IsOwner) t = $"<color=#FF8A5A>«{_title}» застряла — тесто держит тяжем! Брось (G), возьми шпатель и выдолби (ЛКМ по месту) ×{_chips.Value}</color>";
                else if (_looking) t = $"<color=#FF8A5A>«{_title}» застряла в тесте — выдолби шпателем (ЛКМ) ещё {_chips.Value}</color>";
            }
            else if (held && base.IsOwner)
                t = Coat01 <= 0.001f ? $"<color=#FF8A5A>«{_title}» без муки — застрянет в тесте! Положи и кинь муку мастерком</color>" : $"«{_title}»: мука {Coat01 * 100f:0}%";
            if (t == null) return;
            var st = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, Screen.height - 120, Screen.width, 24), t, st);
        }

        public void EditorSetup(string title, Transform stick) { _title = title; _stickPoint = stick; }
    }
}
