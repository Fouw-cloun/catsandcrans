using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>
    /// v23: ВЕДРО-ЧЕРПАК. Красное конусное пожарное ведро с проволочной дужкой, к боку приварена
    /// арматура — ручка. Это «ложка» для жидкостей: объём как у мастерка (Capacity порций).
    ///
    /// ЛКМ по таре — пустое: зачерпнуть жидкость (ту, которой в таре больше всего);
    ///                полное: вылить в тару.
    /// ЛКМ мимо тары — полное: вылить на пол (лужа).
    /// ПКМ — дегустация (пока заглушка: пишет, что внутри).
    ///
    /// Модель рисуется сама при запуске, если у объекта нет своих мешей. Пивот — у хвата арматуры.
    /// Создать: меню CatCooks → Инструменты → Ведро-черпак в сцену.
    /// </summary>
    public sealed class LadleItem : PickupItem, IScoopPoseTool
    {
        /// <summary>v23.4: чаша ведра — для рабочей позы над тарой (зависает над содержимым под прицелом).</summary>
        public Transform ScoopPoint => _bucket;

        [Header("Черпак")]
        [Tooltip("Сколько порций влезает (мастерок ≈ 4)")]
        [Range(1, 40)][SerializeField] private int _capacity = 4;
        [Tooltip("Дальность ЛКМ, м")]
        [Range(0.5f, 8f)][SerializeField] private float _range = 3.5f;
        [Range(0.05f, 1f)][SerializeField] private float _interval = 0.35f;
        [SerializeField] private bool _hint = true;
        [SerializeField] private bool _log = true;

        [Header("Модель (авто)")]
        [SerializeField] private Color _bucketColor = new Color(0.82f, 0.08f, 0.06f);
        [SerializeField] private Color _rebarColor = new Color(0.32f, 0.29f, 0.27f);
        [Tooltip("Радиус верха ведра, м")]
        [SerializeField] private float _topRadius = 0.085f;
        [Tooltip("Высота конуса, м")]
        [SerializeField] private float _height = 0.15f;
        [Tooltip("Длина арматуры-ручки, м")]
        [SerializeField] private float _handleLength = 0.42f;
        [Tooltip("v23.1: во сколько раз крупнее (модель и коллайдеры). 1 — как в v23")]
        [Range(0.5f, 8f)][SerializeField] private float _sizeK = 4f;

        private readonly SyncVar<ushort> _product = new SyncVar<ushort>(0);
        private readonly SyncVar<byte> _state = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _cook = new SyncVar<byte>(0);
        private readonly SyncVar<int> _count = new SyncVar<int>(0);
        private readonly SyncVar<Color> _color = new SyncVar<Color>(new Color(0.9f, 0.9f, 0.9f, 1f));

        private Transform _bucket;          // ось ведра (верх — +Y)
        private Transform _liquid;          // диск жидкости внутри
        private Material _liquidMat;
        private float _shown, _wobble, _next;
        private string _status = "";
        private string _tasteMsg = "";
        private float _tasteUntil;
        private readonly RaycastHit[] _hits = new RaycastHit[24];

        public int Count => _count.Value;
        public bool IsEmpty => _count.Value <= 0;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            bool auto = GetComponentInChildren<MeshRenderer>() == null;
            if (auto) BuildModel();
            if (!Mathf.Approximately(_sizeK, 1f))
            {
                // v23.1: крупнее ×Size K — авто-модель и коллайдеры; своя модель — масштабом объекта
                if (auto)
                {
                    Transform model = transform.Find("Model (auto)");
                    if (model != null) model.localScale = Vector3.one * _sizeK;
                    foreach (BoxCollider bc in GetComponents<BoxCollider>()) { bc.size *= _sizeK; bc.center *= _sizeK; }
                }
                else transform.localScale *= _sizeK;
            }
        }

        // ───────────────────────── КЛИЕНТ ─────────────────────────

        private void Update()
        {
            UpdateLiquidView(Time.deltaTime);
            if (!base.IsOwner || !IsHeldByLocal()) { _status = ""; return; }
            Camera cam = Camera.main;
            if (cam == null) return;
            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            Vector3 point;
            NetworkObject target;
            Cookware cw = AimCookware(ray, out target, out point);
            if (cw != null) _status = IsEmpty ? (HasLiquid(cw) ? "ЛКМ — зачерпнуть" : "тут нечего черпать (только жидкое)") : "ЛКМ — вылить в тару";
            else _status = IsEmpty ? "наведись на тару с жидкостью" : "ЛКМ — вылить на пол";

            if (Input.GetMouseButtonDown(1)) Taste();
            if (Time.time < _next || !Input.GetMouseButtonDown(0)) return;
            _next = Time.time + _interval;
            if (cw == null) { point = FloorPoint(ray); target = null; }
            CmdUse(target, point, ray.direction);
        }

        private bool IsHeldByLocal()
        {
            PlayerHand h = GetComponentInParent<PlayerHand>();
            if (h != null) return h.IsOwner && h.HeldItem == this;
            return CCHands.HeldByLocal(this);                                    // v23.2 (FPS): без поиска по сцене каждый кадр
        }

        private Cookware AimCookware(Ray ray, out NetworkObject target, out Vector3 point)
        {
            target = null; point = ray.origin + ray.direction * _range;
            int n = Physics.RaycastNonAlloc(ray, _hits, _range, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            Cookware found = null;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (h.transform.IsChildOf(transform) || h.distance >= best) continue;
                Cookware cw = h.collider.GetComponentInParent<Cookware>();
                if (cw == null)
                {
                    if (!h.collider.isTrigger) { best = h.distance; found = null; }   // стена перед тарой
                    continue;
                }
                best = h.distance; found = cw; point = h.point;
                target = cw.NetworkObject;
            }
            return found;
        }

        private Vector3 FloorPoint(Ray ray)
        {
            int n = Physics.RaycastNonAlloc(ray, _hits, _range + 2f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 p = ray.origin + ray.direction * 1.2f;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].transform.IsChildOf(transform) || _hits[i].distance >= best) continue;
                best = _hits[i].distance; p = _hits[i].point; any = true;
            }
            if (!any)
            {
                RaycastHit d;
                if (Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out d, 4f, ~0, QueryTriggerInteraction.Ignore)) p = d.point;
            }
            return p;
        }

        private static bool HasLiquid(Cookware cw)
        {
            ushort id; FoodState st; byte ck; int c;
            return DominantLiquid(cw, out id, out st, out ck, out c);
        }

        private void Taste()
        {
            FoodDatabase db = FoodDatabase.Instance;
            ProductDef d = db != null && !IsEmpty ? db.Get(_product.Value) : null;
            _tasteMsg = IsEmpty ? "Пусто — нечего пробовать" : $"Пробуешь: {(d != null ? d.Title : "что-то")} ({_count.Value}/{_capacity}). Дегустация скоро будет!";
            _tasteUntil = Time.time + 2.5f;
            if (_log) Debug.Log($"[Черпак] дегустация (заглушка): {_tasteMsg}", this);
        }

        private void OnGUI()
        {
            if (!_hint) return;
            GUIStyle st = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            if (!string.IsNullOrEmpty(_status))
            {
                FoodDatabase db = FoodDatabase.Instance;
                ProductDef d = db != null && !IsEmpty ? db.Get(_product.Value) : null;
                string load = IsEmpty ? "пусто" : $"{(d != null ? d.Title : "?")} {_count.Value}/{_capacity}";
                GUI.Box(new Rect(Screen.width * 0.5f - 230f, Screen.height - 110f, 460f, 28f), $"Черпак: {load} · {_status} · ПКМ — попробовать", st);
            }
            if (Time.time < _tasteUntil)
                GUI.Box(new Rect(Screen.width * 0.5f - 230f, Screen.height - 146f, 460f, 28f), _tasteMsg, st);
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        /// <summary>Жидкость, которой в таре больше всего.</summary>
        private static bool DominantLiquid(Cookware cw, out ushort product, out FoodState state, out byte cook, out int count)
        {
            product = 0; state = FoodState.None; cook = 0; count = 0;
            if (cw == null) return false;
            FoodDatabase db = FoodDatabase.Instance;
            CookwareLiquidVisual lv = cw.GetComponentInChildren<CookwareLiquidVisual>();
            for (int i = 0; i < cw.ViewCount; i++)
            {
                BagView v = cw.GetView(i);
                if (v.Count == 0 || v.Count <= count) continue;
                bool liquid;
                CookwareLiquidVisual.Role role;
                if (lv != null && lv.TryGetRole(v.Product, out role))
                    liquid = role == CookwareLiquidVisual.Role.Liquid || role == CookwareLiquidVisual.Role.TopLayer;
                else liquid = FoodHeapLook.IsLiquid(db != null ? db.Get(v.Product) : null, v.State);
                if (!liquid) continue;
                product = v.Product; state = v.State; cook = v.Cook; count = v.Count;
            }
            return count > 0;
        }

        [ServerRpc(RequireOwnership = true)]
        private void CmdUse(NetworkObject target, Vector3 point, Vector3 dir)
        {
            Vector3 me = transform.position;
            if ((point - me).sqrMagnitude > (_range + 2f) * (_range + 2f)) return;
            Cookware cw = target != null ? CookwareLookup.Of(target, point) : null;

            if (cw != null && IsEmpty)
            {
                ushort id; FoodState st; byte ck; int have;
                if (!DominantLiquid(cw, out id, out st, out ck, out have)) { if (_log) Debug.Log("[Черпак] в таре нет жидкого — черпаю только жидкости", this); return; }
                int took = cw.ServerTake(id, Mathf.Min(_capacity, have));
                if (took <= 0) return;
                CookwareLiquidVisual lv = cw.GetComponentInChildren<CookwareLiquidVisual>();
                _product.Value = id; _state.Value = (byte)st; _cook.Value = ck; _count.Value = took;
                _color.Value = lv != null ? lv.LiquidColor : ColorOf(id);
                RpcFx(point, false, _color.Value);
                if (_log) Debug.Log($"[Черпак] зачерпнул {took} из «{cw.name}»", this);
                return;
            }
            if (IsEmpty) return;

            ushort p = _product.Value; FoodState s = (FoodState)_state.Value; byte c = _cook.Value;
            if (cw != null)
            {
                int put = cw.ServerAddContent(p, s, _count.Value, c, false);
                if (put <= 0) { if (_log) Debug.Log("[Черпак] тара полная", this); return; }
                _count.Value -= put;
                cw.ServerAgitate(0.15f);
                RpcFx(point, true, _color.Value);
                if (_log) Debug.Log($"[Черпак] вылил {put} в «{cw.name}»", this);
            }
            else
            {
                LiquidPuddle.ServerAddAt(LiquidPuddle.DefaultPrefab, p, point, _count.Value);
                _count.Value = 0;
                RpcFx(point, true, _color.Value);
                if (_log) Debug.Log("[Черпак] вылил на пол", this);
            }
            if (_count.Value <= 0) { _count.Value = 0; _product.Value = 0; }
        }

        private static Color ColorOf(ushort id)
        {
            FoodDatabase db = FoodDatabase.Instance;
            ProductDef d = db != null ? db.Get(id) : null;
            return d != null ? d.BaseColor : Color.white;
        }

        [ObserversRpc]
        private void RpcFx(Vector3 point, bool pour, Color c)
        {
            _wobble = 1f;
            Vector3 lip = _bucket != null ? _bucket.position + _bucket.up * (_height * 0.5f * _bucket.lossyScale.y) : transform.position;
            if (pour)
            {
                // струйка капель от края ведра до цели
                for (int i = 0; i < 14; i++)
                {
                    float k = i / 13f;
                    Vector3 at = Vector3.Lerp(lip, point, k * 0.35f);
                    Vector3 vel = (point - at) * 1.6f + Random.insideUnitSphere * 0.15f;
                    LiquidFx.Drop(at, vel, 0.012f * _sizeK, c, 0.7f);
                }
                LiquidFx.Splash(point, Vector3.up, c, 8, 0.018f * _sizeK, 1.1f);
            }
            else LiquidFx.Splash(point, Vector3.up, c, 5, 0.015f * _sizeK, 0.8f);
            Cookware cw = null;
            foreach (Cookware x in Cookware.All)
                if (x != null && (x.MouthPoint - point).sqrMagnitude < 0.6f * 0.6f) { cw = x; break; }
            CookwareLiquidVisual lv = cw != null ? cw.GetComponentInChildren<CookwareLiquidVisual>() : null;
            if (lv != null) lv.Ripple(point);
        }

        // ───────────────────────── ВИД ─────────────────────────

        private void UpdateLiquidView(float dt)
        {
            if (_liquid == null) return;
            float f = Mathf.Clamp01(_count.Value / (float)Mathf.Max(1, _capacity));
            _shown = Mathf.MoveTowards(_shown, f, dt * 2.5f);
            bool show = _shown > 0.01f;
            if (_liquid.gameObject.activeSelf != show) _liquid.gameObject.SetActive(show);
            if (!show) return;
            // конус: объём ∝ h³ → уровень h = H·f^(1/3), радиус зеркала ∝ h
            float h = _height * Mathf.Pow(_shown, 1f / 3f) * 0.92f;
            float r = _topRadius * (h / _height) * 0.97f;
            // поверхность горизонтальна в мире, чуть качается
            _wobble = Mathf.MoveTowards(_wobble, 0f, dt * 1.5f);
            Vector3 up = Vector3.up + new Vector3(Mathf.Sin(Time.time * 9f), 0f, Mathf.Cos(Time.time * 7f)) * 0.08f * _wobble;
            _liquid.position = _bucket.TransformPoint(new Vector3(0f, -_height * 0.5f + h, 0f));
            _liquid.rotation = Quaternion.FromToRotation(Vector3.up, up.normalized);
            Vector3 ls = _bucket.lossyScale;
            _liquid.localScale = Vector3.one;
            Vector3 cur = _liquid.lossyScale;
            float want = r * ls.x;
            _liquid.localScale = new Vector3(want / Mathf.Max(1e-5f, cur.x), 1f, want / Mathf.Max(1e-5f, cur.z));
            Color c = _color.Value; c.a = 1f;
            if (_liquidMat != null)
            {
                if (_liquidMat.HasProperty("_BaseColor")) _liquidMat.SetColor("_BaseColor", c); else _liquidMat.color = c;
            }
        }

        /// <summary>
        /// Ведро: конус (остриём вниз, открыт сверху) + валик по краю + проволочная дужка,
        /// сбоку приварена рифлёная арматура. Пивот — на конце арматуры (хват), арматура вдоль +Z.
        /// </summary>
        private void BuildModel()
        {
            Transform root = new GameObject("Model (auto)").transform;
            root.SetParent(transform, false);
            Shader sh = Shader.Find("CatCooks/Toon Props");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            Material red = MakeMat(sh, "FireBucket (auto)", _bucketColor, 0.45f);
            Material rebar = MakeMat(sh, "Rebar (auto)", _rebarColor, 0.25f);
            Material wire = MakeMat(sh, "Wire (auto)", new Color(0.55f, 0.56f, 0.58f), 0.6f);
            Material inner = MakeMat(sh, "FireBucketIn (auto)", _bucketColor * 0.55f + new Color(0f, 0f, 0f, 1f), 0.3f);

            float R = _topRadius, H = _height;
            // ведро — центр ведра на расстоянии handleLength по +Z
            _bucket = new GameObject("Bucket").transform;
            _bucket.SetParent(root, false);
            _bucket.localPosition = new Vector3(0f, 0.02f, _handleLength + R);

            Mesh outer = ConeMesh(R, H, 24, false);
            Mesh inMesh = ConeMesh(R * 0.96f, H * 0.97f, 24, true);
            Part(_bucket, "Cone", outer, red, new Vector3(0f, -H * 0.5f, 0f), Quaternion.identity, Vector3.one);
            Part(_bucket, "ConeInside", inMesh, inner, new Vector3(0f, -H * 0.5f + H * 0.03f, 0f), Quaternion.identity, Vector3.one);
            // валик по краю
            Part(_bucket, "Rim", TorusMesh(R, R * 0.06f, 28, 8), red, new Vector3(0f, H * 0.5f, 0f), Quaternion.identity, Vector3.one);
            // дужка: полукольцо проволоки над ведром (поперёк ручки)
            Part(_bucket, "Bail", ArcMesh(R * 1.02f, 0.0035f, 20, 6), wire, new Vector3(0f, H * 0.5f, 0f), Quaternion.Euler(0f, 0f, 0f), Vector3.one);
            // ушки дужки
            Part(_bucket, "Ear L", TorusMesh(0.008f, 0.0025f, 10, 5), red, new Vector3(-R * 1.0f, H * 0.5f - 0.01f, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one);
            Part(_bucket, "Ear R", TorusMesh(0.008f, 0.0025f, 10, 5), red, new Vector3(R * 1.0f, H * 0.5f - 0.01f, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one);

            // арматура: рифлёный пруток от хвата до бока ведра, у ведра — сварной шов
            float rr = 0.0075f;
            float len = _handleLength + 0.01f;
            Transform bar = new GameObject("Rebar").transform;
            bar.SetParent(root, false);
            bar.localPosition = new Vector3(0f, 0.02f, len * 0.5f);
            bar.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Part(bar, "Rod", CylMesh(rr, len, 10), rebar, Vector3.zero, Quaternion.identity, Vector3.one);
            int ribs = Mathf.Max(6, Mathf.RoundToInt(len / 0.022f));
            Mesh rib = TorusMesh(rr * 1.05f, rr * 0.28f, 10, 4);
            for (int i = 0; i < ribs; i++)
            {
                float y = -len * 0.5f + (i + 0.5f) * len / ribs;
                Part(bar, "Rib", rib, rebar, new Vector3(0f, y, 0f), Quaternion.Euler(0f, 0f, 14f), Vector3.one);
            }
            // сварка: бугристые капли на стыке с ведром (вертикальный приваренный кусок)
            Transform weld = new GameObject("Weld").transform;
            weld.SetParent(root, false);
            weld.localPosition = new Vector3(0f, 0.02f, _handleLength + 0.004f);
            Part(weld, "Strip", CylMesh(rr * 0.9f, H * 0.7f, 8), rebar, Vector3.zero, Quaternion.identity, Vector3.one);
            Mesh blob = SphereMesh(rr * 1.5f, 8, 6);
            for (int i = 0; i < 5; i++)
                Part(weld, "Bead", blob, rebar, new Vector3(Mathf.Sin(i * 2.1f) * 0.002f, -H * 0.28f + i * H * 0.14f, 0.002f), Quaternion.Euler(i * 40f, i * 70f, 0f), new Vector3(1f, 0.7f, 1f));

            // жидкость внутри
            _liquid = new GameObject("Liquid").transform;
            _liquid.SetParent(_bucket, false);
            _liquidMat = MakeMat(sh, "LadleLiquid (auto)", Color.white, 0.85f);
            MeshFilter lf = _liquid.gameObject.AddComponent<MeshFilter>();
            lf.sharedMesh = DiscMesh(1f, 24);
            MeshRenderer lr = _liquid.gameObject.AddComponent<MeshRenderer>();
            lr.sharedMaterial = _liquidMat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _liquid.gameObject.SetActive(false);

            // коллайдеры, если их нет
            if (GetComponentInChildren<Collider>() == null)
            {
                BoxCollider hb = gameObject.AddComponent<BoxCollider>();
                hb.center = new Vector3(0f, 0.02f, _handleLength * 0.5f);
                hb.size = new Vector3(0.03f, 0.03f, _handleLength);
                BoxCollider bb = gameObject.AddComponent<BoxCollider>();
                bb.center = _bucket.localPosition;
                bb.size = new Vector3(R * 1.7f, H, R * 1.7f);
            }
        }

        private static Material MakeMat(Shader sh, string name, Color c, float smooth)
        {
            Material m = new Material(sh) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            return m;
        }

        private static void Part(Transform parent, string name, Mesh mesh, Material m, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            GameObject g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = rot;
            g.transform.localScale = scale;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = m;
        }

        // ── процедурные меши ──

        /// <summary>Конус остриём вниз: вершина в y=0, край радиуса r на высоте h. inside — нормали внутрь.</summary>
        private static Mesh ConeMesh(float r, float h, int seg, bool inside)
        {
            var v = new Vector3[(seg + 1) * 2];
            var n = new Vector3[v.Length];
            var t = new int[seg * 3];
            float slope = r / h;
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 nn = new Vector3(d.x, -slope, d.z).normalized;
                if (inside) nn = -nn;
                v[i * 2] = Vector3.zero; n[i * 2] = nn;
                v[i * 2 + 1] = d * r + Vector3.up * h; n[i * 2 + 1] = nn;
            }
            for (int i = 0; i < seg; i++)
            {
                int a0 = i * 2, a1 = i * 2 + 1, b1 = (i + 1) * 2 + 1;
                if (inside) { t[i * 3] = a0; t[i * 3 + 1] = b1; t[i * 3 + 2] = a1; }
                else { t[i * 3] = a0; t[i * 3 + 1] = a1; t[i * 3 + 2] = b1; }
            }
            var m = new Mesh { name = inside ? "ConeIn" : "Cone" };
            m.vertices = v; m.normals = n; m.triangles = t;
            m.RecalculateBounds();
            return m;
        }

        private static Mesh TorusMesh(float R, float r, int seg, int side)
        {
            return SweepMesh(R, r, seg, side, Mathf.PI * 2f, "Torus");
        }

        /// <summary>Полукольцо (дужка) в плоскости XY, концы на ±R по X.</summary>
        private static Mesh ArcMesh(float R, float r, int seg, int side)
        {
            return SweepMesh(R, r, seg, side, Mathf.PI, "Arc", true);
        }

        private static Mesh SweepMesh(float R, float r, int seg, int side, float span, string name, bool vertical = false)
        {
            var v = new Vector3[(seg + 1) * (side + 1)];
            var n = new Vector3[v.Length];
            var t = new int[seg * side * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * span;
                Vector3 c = vertical ? new Vector3(Mathf.Cos(a) * R, Mathf.Sin(a) * R, 0f) : new Vector3(Mathf.Cos(a) * R, 0f, Mathf.Sin(a) * R);
                Vector3 outD = c.normalized;
                Vector3 axis = vertical ? Vector3.forward : Vector3.up;
                for (int j = 0; j <= side; j++)
                {
                    float b = j / (float)side * Mathf.PI * 2f;
                    Vector3 nn = outD * Mathf.Cos(b) + axis * Mathf.Sin(b);
                    v[i * (side + 1) + j] = c + nn * r;
                    n[i * (side + 1) + j] = nn;
                }
            }
            int k = 0;
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < side; j++)
                {
                    int a0 = i * (side + 1) + j, a1 = a0 + 1, b0 = a0 + side + 1, b1 = b0 + 1;
                    if (vertical) { t[k++] = a0; t[k++] = b0; t[k++] = a1; t[k++] = a1; t[k++] = b0; t[k++] = b1; }
                    else { t[k++] = a0; t[k++] = a1; t[k++] = b0; t[k++] = a1; t[k++] = b1; t[k++] = b0; }
                }
            var m = new Mesh { name = name };
            m.vertices = v; m.normals = n; m.triangles = t;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Цилиндр вдоль Y, центр в 0.</summary>
        private static Mesh CylMesh(float r, float len, int seg)
        {
            var v = new Vector3[(seg + 1) * 2];
            var n = new Vector3[v.Length];
            var t = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[i * 2] = d * r + Vector3.down * len * 0.5f; n[i * 2] = d;
                v[i * 2 + 1] = d * r + Vector3.up * len * 0.5f; n[i * 2 + 1] = d;
            }
            for (int i = 0; i < seg; i++)
            {
                int a0 = i * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                t[i * 6] = a0; t[i * 6 + 1] = a1; t[i * 6 + 2] = b0;
                t[i * 6 + 3] = a1; t[i * 6 + 4] = b1; t[i * 6 + 5] = b0;
            }
            var m = new Mesh { name = "Rod" };
            m.vertices = v; m.normals = n; m.triangles = t;
            m.RecalculateBounds();
            return m;
        }

        private static Mesh SphereMesh(float r, int seg, int rings)
        {
            var v = new Vector3[(seg + 1) * (rings + 1)];
            var n = new Vector3[v.Length];
            var t = new int[seg * rings * 6];
            for (int j = 0; j <= rings; j++)
            {
                float p = j / (float)rings * Mathf.PI;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Sin(p) * Mathf.Cos(a), Mathf.Cos(p), Mathf.Sin(p) * Mathf.Sin(a));
                    v[j * (seg + 1) + i] = d * r; n[j * (seg + 1) + i] = d;
                }
            }
            int k = 0;
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a0 = j * (seg + 1) + i, a1 = a0 + 1, b0 = a0 + seg + 1, b1 = b0 + 1;
                    t[k++] = a0; t[k++] = a1; t[k++] = b0;
                    t[k++] = a1; t[k++] = b1; t[k++] = b0;
                }
            var m = new Mesh { name = "Blob" };
            m.vertices = v; m.normals = n; m.triangles = t;
            m.RecalculateBounds();
            return m;
        }

        private static Mesh DiscMesh(float r, int seg)
        {
            var v = new Vector3[seg + 1];
            var n = new Vector3[seg + 1];
            var t = new int[seg * 3];
            v[0] = Vector3.zero; n[0] = Vector3.up;
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                v[i + 1] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r); n[i + 1] = Vector3.up;
                t[i * 3] = 0; t[i * 3 + 1] = (i + 1) % seg + 1; t[i * 3 + 2] = i + 1;
            }
            var m = new Mesh { name = "LadleLiquid" };
            m.vertices = v; m.normals = n; m.triangles = t;
            m.RecalculateBounds();
            return m;
        }
    }
}
