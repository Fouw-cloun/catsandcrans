using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>
    /// Яйцо. Берёшь в руку (F), смотришь на тару и жмёшь ПКМ — рука подносит
    /// яйцо к горловине и разбивает его ПРЯМО НАД ТАРОЙ. Смотришь мимо тары —
    /// разбивается там, где держишь (на пол). Размер скорлупы и содержимого
    /// берётся с модели яйца сам (Auto Size).
    ///
    /// Скорлупа раскалывается на две половинки и разлетается, из яйца вытекает
    /// сырое яйцо — тягучая струйка белка и желток. Куда падает:
    ///   • в тару (или рядом с горловиной в пределах Capture Radius) — в таре
    ///     прибавляется Content (по умолчанию Яйцо, «Разбито»);
    ///   • на пол — лужица (Puddle Prefab, если задан).
    /// Само яйцо после этого исчезает.
    ///
    /// Сеть: разбивает сервер (он и решает, куда попало). Скорлупа, струйка и
    /// желток — вид, каждый игрок рисует сам по одному сообщению.
    ///
    /// ═══ ТРЕБУЕТ ═══
    /// NetworkObject, NetworkTransform (Client Authoritative), Rigidbody, Collider, модель яйца.
    /// Половинки скорлупы: можно дать свои модели (Shell Top / Shell Bottom),
    /// иначе построятся сами из материала яйца.
    /// </summary>
    public class EggItem : PickupItem
    {
        [Header("Что вытекает")]
        [Tooltip("Продукт, который попадает в тару. Food_Яйцо (или Food_Яичный_желток)")]
        [SerializeField] private ProductDef _content;
        [Tooltip("Состояние в таре. Для яйца — «Яйцо/Разбито», для желтка — «Яйцо/Желток»")]
        [SerializeField] private FoodState _contentState = FoodState.Cracked;
        [Min(1)][SerializeField] private int _portions = 1;

        [Header("Куда падает")]
        [Tooltip("Тара, горловина которой ближе этого к месту падения, ловит яйцо, м")]
        [SerializeField] private float _captureRadius = 0.45f;
        [SerializeField] private LayerMask _surfaceMask = ~0;
        [Tooltip("Лужица на полу (NetworkObject + LiquidPuddle). Пусто — на полу ничего не остаётся")]
        [SerializeField] private LiquidPuddle _puddlePrefab;

        [Header("Вид")]
        [Tooltip("Модели половинок скорлупы. Пусто — построятся сами")]
        [SerializeField] private GameObject _shellTop;
        [SerializeField] private GameObject _shellBottom;
        [Tooltip("Цвет скорлупы после разбивания")]
        [SerializeField] private Color _shellColor = new Color(0.97f, 0.96f, 0.93f);
        [Tooltip("Скорлупа в материале модели яйца (с её текстурой). Выключено — чистый цвет Shell Color")]
        [SerializeField] private bool _shellUsesEggMaterial = false;
        [SerializeField] private Color _whiteColor = new Color(0.97f, 0.96f, 0.86f);
        [SerializeField] private Color _yolkColor = new Color(1f, 0.72f, 0.12f);
        [Tooltip("Размер скорлупы и содержимого — по модели яйца (габариты меша с учётом масштаба)")]
        [SerializeField] private bool _autoSize = true;
        [Tooltip("Размер яйца, м — если Auto Size выключен")]
        [SerializeField] private float _eggSize = 0.06f;
        [Tooltip("Множитель содержимого (белок, желток) относительно размера яйца")]
        [Range(0.3f, 3f)][SerializeField] private float _contentScale = 1f;

        [Header("Куда разбивать")]
        [Tooltip("Докуда тянется рука к таре, м")]
        [SerializeField] private float _reach = 2.2f;
        [Tooltip("На какой высоте над горловиной разбивается, м")]
        [SerializeField] private float _aboveMouth = 0.15f;
        [Tooltip("За сколько секунд рука подносит яйцо к таре")]
        [Range(0.05f, 1f)][SerializeField] private float _reachSeconds = 0.25f;
        [SerializeField] private LayerMask _aimMask = ~0;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private readonly SyncVar<bool> _cracked = new SyncVar<bool>(false);
        private bool _rmbWas, _sent, _reaching;
        private float _reachT;
        private Cookware _aimCw, _reachCw;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
        }

        private void Update()
        {
            if (!base.IsOwner || _cracked.Value || _sent) return;
            if (!_reaching) _aimCw = AimCookware();
            bool rmb = Input.GetMouseButton(1);
            if (rmb && !_rmbWas && !_reaching)
            {
                if (_aimCw != null) { _reaching = true; _reachT = 0f; _reachCw = _aimCw; }   // поднести к таре
                else { _sent = true; CmdCrack(null); }                                       // разбить где держишь
            }
            _rmbWas = rmb;
            if (_reaching)
            {
                _reachT += Time.deltaTime / _reachSeconds;
                if (_reachT >= 1f)
                {
                    _reaching = false;
                    _sent = true;
                    CmdCrack(_reachCw != null ? _reachCw.NetworkObject : null);
                }
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

        public override void UpdateHeldPose(Transform handAnchor)
        {
            base.UpdateHeldPose(handAnchor);
            if (!_reaching || _reachCw == null) return;
            // Рука подносит яйцо к горловине тары.
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_reachT));
            Vector3 target = _reachCw.MouthPoint + Vector3.up * (_aboveMouth + EggSize * 0.5f);
            transform.position = Vector3.Lerp(transform.position, target, k);
        }

        private void OnGUI()
        {
            if (!base.IsOwner || _cracked.Value || _sent || _reaching || _aimCw == null) return;
            string t = $"ПКМ — разбить яйцо в «{(_aimCw.Def != null ? _aimCw.Def.Title : _aimCw.name)}»";
            var st = new GUIStyle(GUI.skin.box) { fontSize = 14 };
            Vector2 size = st.CalcSize(new GUIContent(t));
            GUI.Box(new Rect(Screen.width * 0.5f - size.x * 0.5f - 6f, Screen.height * 0.5f + 34f, size.x + 12f, 24f), t, st);
        }

        // ── размер по модели ──
        private MeshFilter ModelMesh
        {
            get
            {
                foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) return mf;
                return null;
            }
        }

        /// <summary>Габариты модели в метрах мира (по осям меша).</summary>
        private bool ModelBox(out Vector3 center, out Vector3 size, out Quaternion rot)
        {
            MeshFilter mf = ModelMesh;
            if (!_autoSize || mf == null)
            {
                center = transform.position; rot = transform.rotation;
                size = new Vector3(_eggSize * 0.8f, _eggSize, _eggSize * 0.8f);
                return false;
            }
            Bounds b = mf.sharedMesh.bounds;
            center = mf.transform.TransformPoint(b.center);
            Vector3 ls = mf.transform.lossyScale;
            size = new Vector3(Mathf.Abs(b.size.x * ls.x), Mathf.Abs(b.size.y * ls.y), Mathf.Abs(b.size.z * ls.z));
            rot = mf.transform.rotation;
            return true;
        }

        /// <summary>Длина яйца, м (самая длинная сторона модели).</summary>
        private float EggSize
        {
            get
            {
                ModelBox(out _, out Vector3 sz, out _);
                return Mathf.Max(0.01f, Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z)));
            }
        }

        // ───────────────────────── СЕРВЕР ─────────────────────────

        [ServerRpc(RequireOwnership = true)]
        private void CmdCrack(NetworkObject target)
        {
            if (_cracked.Value) return;
            _cracked.Value = true;

            Vector3 from = transform.position;
            Cookware cw = null;
            Vector3 point;
            bool hit;
            Cookware aimed = CookwareLookup.Of(target, transform.position);
            if (aimed != null && Vector3.Distance(aimed.MouthPoint, from) < _reach + 1.5f)
            {
                // Разбили над тарой под прицелом — всё в неё. Точка падения — поверхность содержимого.
                cw = aimed;
                point = new Vector3(from.x, aimed.MouthPoint.y, from.z);
                CookwareLiquidVisual lv = aimed.GetComponentInChildren<CookwareLiquidVisual>();
                if (lv != null && lv.HasLiquid && lv.TryGetSurfaceY(point, out float sy)) point.y = sy;
                else point.y = aimed.MouthPoint.y - 0.15f;
                hit = true;
            }
            else
            {
                hit = Trace(from, out point, out Cookware direct);
                cw = hit ? (direct != null ? direct : NearestCookware(point)) : null;
            }
            bool intoCookware = false;
            if (cw != null && _content != null)
            {
                cw.ServerAddContent(_content.Id, _contentState, _portions, 0);
                cw.ServerAgitate(0.25f);
                intoCookware = true;
                if (_log && cw.Def != null) Debug.Log($"[Яйцо] разбито в «{cw.Def.Title}»", this);
            }
            else if (hit && _puddlePrefab != null && _content != null)
            {
                LiquidPuddle.ServerAddAt(_puddlePrefab, _content.Id, point, 1);
                if (_log) Debug.Log("[Яйцо] разбито на пол", this);
            }
            else if (_log) Debug.Log("[Яйцо] разбито мимо", this);

            RpcCrack(from, transform.rotation, hit ? point : from + Vector3.down * 2f, intoCookware);

            // Отпустить из руки и убрать — скорлупа уже летит у всех как вид.
            PlayerHand hand = FindHolder();
            if (hand != null) hand.ServerDrop(Vector3.zero);
            Invoke(nameof(ServerRemove), 0.15f);
        }

        private PlayerHand FindHolder()
        {
            foreach (PlayerHand h in FindObjectsByType<PlayerHand>(FindObjectsSortMode.None))
                if (h != null && h.HeldItem == this) return h;
            return null;
        }

        private void ServerRemove()
        {
            if (base.IsSpawned) base.Despawn(gameObject);
        }

        private bool Trace(Vector3 from, out Vector3 point, out Cookware direct)
        {
            direct = null;
            Vector3 pos = from, vel = Vector3.down * 0.3f;
            const float dt = 0.02f;
            for (int i = 0; i < 150; i++)
            {
                vel += Physics.gravity * dt;
                Vector3 next = pos + vel * dt;
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
                if (h.transform.IsChildOf(transform)) continue;
                if (h.collider.GetComponentInParent<PlayerInteractor>() != null) continue;
                if (h.collider.isTrigger && h.collider.GetComponentInParent<Cookware>() == null) continue;
                if (h.collider.GetComponentInParent<LiquidPuddle>() != null) continue;
                if (h.distance >= bestD) continue;
                bestD = h.distance; best = h; found = true;
            }
            return found;
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

        // ───────────────────────── ВИД (у всех) ─────────────────────────

        [ObserversRpc(RunLocally = false)]
        private void RpcCrack(Vector3 at, Quaternion rot, Vector3 landing, bool intoCookware)
        {
            // Модель яйца прячем сразу — дальше живут скорлупа и содержимое.
            foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;

            // Габариты модели: половинки скорлупы ровно её размера, содержимое — по ней же.
            ModelBox(out Vector3 c, out Vector3 size, out Quaternion mrot);
            float s = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            // длинная ось яйца → «верх» половинок
            Vector3 longAxis = size.y >= size.x && size.y >= size.z ? mrot * Vector3.up
                             : size.x >= size.z ? mrot * Vector3.right : mrot * Vector3.forward;
            if (Vector3.Dot(longAxis, Vector3.up) < 0f) longAxis = -longAxis;
            float across = (size.x + size.y + size.z - s) * 0.5f;        // средний поперечник
            Vector3 halfScale = new Vector3(across, s, across);
            Quaternion up = Quaternion.FromToRotation(Vector3.up, longAxis);
            Material shellMat = FindShellMaterial();
            Vector3 side = up * Vector3.right;
            SpawnShell(_shellTop, c, up * Quaternion.Euler(180f, 0f, 0f), halfScale, side * 0.6f + Vector3.up * 0.9f, shellMat);
            SpawnShell(_shellBottom, c, up, halfScale, -side * 0.6f + Vector3.up * 0.5f, shellMat);
            // хруст: крошки скорлупы (размер — по яйцу)
            CCRuntimeParticles.Burst(c, Vector3.up, _shellColor, Color.Lerp(_shellColor, Color.white, 0.4f), 10,
                                     CCRuntimeParticles.Look.Chips.Scaled(Mathf.Clamp(s / 0.06f, 0.3f, 8f) * 0.35f));
            at = c;
            s *= _contentScale;

            // Содержимое: тягучий белок струйкой + желток каплей.
            var go = new GameObject("EggGoo (fx)");
            go.transform.position = at;
            go.AddComponent<EggGooFx>().Init(at, landing, _whiteColor, _yolkColor, s, intoCookware);
        }

        private static Material _whiteShell;

        /// <summary>Материал скорлупы: по умолчанию свой, однотонный Shell Color (белый).</summary>
        private Material FindShellMaterial()
        {
            if (_shellUsesEggMaterial)
            {
                Renderer r = GetComponentInChildren<Renderer>(true);
                if (r != null && r.sharedMaterial != null) return r.sharedMaterial;
            }
            if (_whiteShell == null)
            {
                Shader sh = Shader.Find("CatCooks/Toon Props");
                if (sh == null) sh = Shader.Find("CatCooks/Toon Food");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
                _whiteShell = new Material(sh) { name = "EggShell (auto)", hideFlags = HideFlags.DontSave };
            }
            if (_whiteShell.HasProperty("_BaseColor")) _whiteShell.SetColor("_BaseColor", _shellColor);
            if (_whiteShell.HasProperty("_Color")) _whiteShell.SetColor("_Color", _shellColor);
            if (_whiteShell.HasProperty("_BaseMap")) _whiteShell.SetTexture("_BaseMap", null);
            if (_whiteShell.HasProperty("_MainTex")) _whiteShell.SetTexture("_MainTex", null);
            if (_whiteShell.HasProperty("_Smoothness")) _whiteShell.SetFloat("_Smoothness", 0.35f);
            return _whiteShell;
        }

        /// <summary>Половинка скорлупы: чашечка размером ровно с модель яйца (scale = габариты).</summary>
        private void SpawnShell(GameObject model, Vector3 pos, Quaternion rot, Vector3 scale, Vector3 kick, Material mat)
        {
            GameObject g;
            float s = Mathf.Max(scale.x, scale.y);
            if (model != null) g = Instantiate(model, pos, rot);
            else
            {
                g = new GameObject("EggShell (fx)");
                g.transform.SetPositionAndRotation(pos, rot);
                g.AddComponent<MeshFilter>().sharedMesh = EggGooFx.HalfShellMesh;
                var mr = g.AddComponent<MeshRenderer>();
                if (mat != null) mr.sharedMaterial = mat;
                else
                {
                    Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                    mr.sharedMaterial = new Material(sh) { color = _shellColor };
                }
                // меш чашечки: радиус 0.5, высота 0.5 → масштаб = габариты яйца (высота половинки = половина яйца)
                g.transform.localScale = scale;
                var col = g.AddComponent<SphereCollider>();
                col.radius = 0.35f;
            }
            var rb = g.GetComponent<Rigidbody>();
            if (rb == null) rb = g.AddComponent<Rigidbody>();
            rb.mass = Mathf.Clamp(s * 0.2f, 0.01f, 0.5f);
            float kickK = Mathf.Clamp(s / 0.06f, 1f, 4f);               // большое яйцо — половинки разлетаются шире
            rb.linearVelocity = kick * kickK + Random.insideUnitSphere * 0.3f;
            rb.angularVelocity = Random.insideUnitSphere * 12f;
            Destroy(g, 5f);
        }
    }

    /// <summary>
    /// Вид сырого яйца: белок тянется струйкой (вязкая струя), желток падает каплей,
    /// на месте падения — шлепок. Живёт пару секунд и удаляет себя.
    /// </summary>
    public sealed class EggGooFx : MonoBehaviour
    {
        private Vector3 _from, _land, _yolkPos, _yolkVel;
        private Color _white, _yolk;
        private float _size, _t;
        private bool _into, _yolkDone;
        private LiquidStreamRibbon _ribbon;
        private Transform _yolkT;
        private static LiquidPourProfile _gooProfile;

        private static Mesh _half, _sphere;

        /// <summary>Полусфера (чашечка скорлупы), радиус 0.5, срез — плоскость Y=0, открыта сверху.</summary>
        public static Mesh HalfShellMesh
        {
            get
            {
                if (_half != null) return _half;
                const int seg = 18, rings = 7;
                var v = new System.Collections.Generic.List<Vector3>();
                var tri = new System.Collections.Generic.List<int>();
                for (int r = 0; r <= rings; r++)
                {
                    float a = r / (float)rings * Mathf.PI * 0.5f;            // 0 — срез, π/2 — макушка
                    float y = -Mathf.Sin(a) * 0.5f, rad = Mathf.Cos(a) * 0.5f;
                    for (int s = 0; s <= seg; s++)
                    {
                        float b = s / (float)seg * Mathf.PI * 2f;
                        // зубчатый край скола
                        float jag = r == 0 ? (s % 2 == 0 ? 0.06f : -0.02f) : 0f;
                        v.Add(new Vector3(Mathf.Cos(b) * rad, y + jag, Mathf.Sin(b) * rad));
                    }
                }
                for (int r = 0; r < rings; r++)
                    for (int s = 0; s < seg; s++)
                    {
                        int i0 = r * (seg + 1) + s, i1 = i0 + 1, i2 = i0 + seg + 1, i3 = i2 + 1;
                        // наружу и внутрь — скорлупа видна с обеих сторон
                        tri.Add(i0); tri.Add(i2); tri.Add(i1); tri.Add(i1); tri.Add(i2); tri.Add(i3);
                        tri.Add(i0); tri.Add(i1); tri.Add(i2); tri.Add(i1); tri.Add(i3); tri.Add(i2);
                    }
                _half = new Mesh { name = "EggHalfShell" };
                _half.SetVertices(v);
                _half.SetTriangles(tri, 0);
                _half.RecalculateNormals();
                _half.RecalculateBounds();
                return _half;
            }
        }

        private static Mesh Sphere
        {
            get
            {
                if (_sphere != null) return _sphere;
                GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _sphere = g.GetComponent<MeshFilter>().sharedMesh;
                Destroy(g);
                return _sphere;
            }
        }

        public void Init(Vector3 from, Vector3 land, Color white, Color yolk, float size, bool intoCookware)
        {
            _from = from; _land = land; _white = white; _yolk = yolk; _size = size; _into = intoCookware;
            _ribbon = LiquidStreamRibbon.Create(transform);
            if (_gooProfile == null)
            {
                // Белок: вязкий, прозрачнее молока, тянется и рвётся каплями.
                _gooProfile = ScriptableObject.CreateInstance<LiquidPourProfile>();
                _gooProfile.name = "Egg goo (auto)";
                _gooProfile.hideFlags = HideFlags.DontSave;
                _gooProfile.Viscosity = 0.75f; _gooProfile.Opacity = 0.75f; _gooProfile.FlowSpeed = 0.8f;
                _gooProfile.Wobble = 0.12f; _gooProfile.StreakAmount = 0.35f; _gooProfile.Necking = 0.6f;
                _gooProfile.BreakUp = 0.8f; _gooProfile.SplashSize = 1.6f; _gooProfile.DropsPerSecond = 2f;
                _gooProfile.LipFoam = 0.02f; _gooProfile.BottomFoam = 0.05f; _gooProfile.FoamPatch = 2f; _gooProfile.Gloss = 1.6f;
            }
            _ribbon.Profile = _gooProfile;

            var y = new GameObject("Yolk (fx)");
            _yolkT = y.transform;
            _yolkT.SetParent(transform, false);
            y.AddComponent<MeshFilter>().sharedMesh = Sphere;
            var mr = y.AddComponent<MeshRenderer>();
            Shader sh = Shader.Find("CatCooks/Toon Food");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(sh) { name = "Yolk (auto)" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", yolk); else m.color = yolk;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.85f);
            mr.sharedMaterial = m;
            _yolkT.localScale = Vector3.one * size * 0.5f;
            _yolkPos = from + Vector3.down * size * 0.2f;
            _yolkVel = Vector3.down * 0.2f;
            Destroy(gameObject, 3f);
        }

        private void OnDestroy()
        {
            if (_ribbon != null) Destroy(_ribbon.gameObject);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _t += dt;
            // белок течёт первые ~0.5 с, тонкой тягучей струйкой
            bool flow = _t < 0.55f;
            float w = _size * Mathf.Lerp(0.55f, 0.15f, Mathf.Clamp01(_t / 0.55f));
            if (_ribbon != null) _ribbon.Tick(flow, _from, Vector3.down * 0.25f, w, _white, null, 25f);

            if (_yolkDone || _yolkT == null) return;
            // желток выходит чуть позже белка и падает
            if (_t < 0.12f) { _yolkT.position = _yolkPos; return; }
            _yolkVel += Physics.gravity * dt;
            _yolkPos += _yolkVel * dt;
            // в полёте чуть вытягивается
            float stretch = Mathf.Clamp(1f + _yolkVel.magnitude * 0.04f, 1f, 1.35f);
            _yolkT.localScale = new Vector3(1f / Mathf.Sqrt(stretch), stretch, 1f / Mathf.Sqrt(stretch)) * _size * 0.5f;
            _yolkT.position = _yolkPos;
            if (_yolkPos.y <= _land.y + _size * 0.1f)
            {
                _yolkDone = true;
                LiquidFx.ImpactSplash(_land + Vector3.up * 0.003f, Vector3.up, _yolk, 0.5f, 0.6f);
                if (_into)
                {
                    Cookware cw = null;
                    float best = 0.6f;
                    foreach (Cookware c in Cookware.All)
                    {
                        if (c == null) continue;
                        float d = Vector3.Distance(c.MouthPoint, _land);
                        if (d < best) { best = d; cw = c; }
                    }
                    CookwareLiquidVisual lv = cw != null ? cw.GetComponentInChildren<CookwareLiquidVisual>() : null;
                    if (lv != null) lv.Ripple(_land);
                    Destroy(_yolkT.gameObject);
                }
                else
                {
                    // на полу желток расплющивается куполком и остаётся до исчезновения эффекта
                    _yolkT.position = _land + Vector3.up * _size * 0.08f;
                    _yolkT.localScale = new Vector3(_size * 0.55f, _size * 0.18f, _size * 0.55f);
                }
            }
        }
    }
}
