using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ГЛУБОКАЯ МИСКА ДЛЯ БОРЩА — подача собирается прямо в миске.
    ///
    /// ═══ ПОДАЧА ═══
    ///   1. Борщ — ведром из кастрюли (ЛКМ над миской): налить, чтобы миска была полной хотя бы наполовину.
    ///   2. Сметана — мастерком из ведра/банки: шмат по центру (белая шапка на борще).
    ///   3. Затирка — мастерком: шмат рядом со сметаной (розовато-белая, с зеленью).
    ///   4. Пампушка — брось в миску (или рядом): сама ляжет НА КРАЙ миски. Пампушка с чесноком — лучше.
    ///   Всё есть — миска ставит ЗВЁЗДЫ (1–5) и сыплет салют. Нести миску — F.
    ///
    /// Борщ, сметана, затирка — обычное содержимое Cookware (по сети как у любой тары). Пампушка — свой кусок,
    /// сервер держит его на крае (кинематика), Network Transform показывает всем. Взял — снова свободная.
    /// ═══ НАСТРОЙКА ═══ CatCooks → Борщ → 16 (миска в сцену), потом префабом руками.
    /// </summary>
    [RequireComponent(typeof(Cookware))]
    public sealed class BorschBowl : NetworkBehaviour
    {
        [Tooltip("Радиус края миски, м (мир). 0 — по жидкости")]
        [SerializeField] private float _rimRadius = 0f;
        [Tooltip("Высота края над опорой миски, м (мир). 0 — по жидкости")]
        [SerializeField] private float _rimHeight = 0f;
        [Tooltip("Доля полной миски борща для «полной порции»")]
        [Range(0.2f, 1f)][SerializeField] private float _soupFull = 0.5f;
        [Tooltip("Сколько порций сметаны / затирки на «шмат»")]
        [Min(1)][SerializeField] private int _dollop = 1;
        [SerializeField] private bool _stars = true;
        [Range(5, 80)][SerializeField] private int _starCount = 36;
        [SerializeField] private bool _log = true;

        private readonly SyncVar<byte> _starsNet = new SyncVar<byte>(0);
        private readonly SyncVar<bool> _bunNet = new SyncVar<bool>(false);
        private readonly SyncVar<bool> _bunGarlicNet = new SyncVar<bool>(false);

        private Cookware _cw;
        private CookwareLiquidVisual _lv;
        private int _soup, _cream, _zat;
        private Transform _creamT, _zatT;
        private FoodPiece _bun;
        private float _scanAt, _evalAt;
        private static GUIStyle _st;
        private static Mesh _blob;
        private Material _mCream, _mZat;

        public int Stars => _starsNet.Value;

        private void Awake()
        {
            _cw = GetComponent<Cookware>();
            _lv = GetComponentInChildren<CookwareLiquidVisual>(true);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            if (_cw != null) _cw.ContentsChanged += Recount;
            _starsNet.OnChange += OnStars;
            Recount();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            if (_cw != null) _cw.ContentsChanged -= Recount;
            _starsNet.OnChange -= OnStars;
        }

        private void OnStars(byte a, byte b, bool asServer)
        {
            if (asServer && base.IsClientInitialized) return;
            if (b > a && b > 0 && _stars) CelebrationStars.Burst(Rim().center + Vector3.up * 0.3f, Mathf.Max(0.5f, Rim().r), CelebrationStars.Gold, _starCount * b / 5);
        }

        private void Recount()
        {
            _soup = _cream = _zat = 0;
            if (_cw == null) return;
            FoodDatabase db = FoodDatabase.Instance;
            for (int i = 0; i < _cw.ViewCount; i++)
            {
                BagView v = _cw.GetView(i);
                if (v.Count <= 0) continue;
                ProductDef d = db != null ? db.Get(v.Product) : null;
                string t = d != null && !string.IsNullOrEmpty(d.Title) ? d.Title.Trim().ToLowerInvariant() : "";
                if (t == "борщ") _soup += v.Count;
                else if (t.Contains("сметан")) _cream += v.Count;
                else if (t.Contains("затирк")) _zat += v.Count;
            }
            _evalAt = Time.time + 0.3f;
        }

        // ───────────────────────── геометрия ─────────────────────────

        private struct RimInfo { public Vector3 center; public float r, y; }

        private RimInfo Rim()
        {
            var ri = new RimInfo();
            Transform a = _lv != null ? _lv.transform : transform;
            ri.center = a.position;
            ri.r = _rimRadius > 0f ? _rimRadius : (_lv != null ? _lv.RadiusAtRimWorld * 1.05f : 0.4f);
            ri.y = _rimHeight > 0f ? transform.position.y + _rimHeight : (_lv != null ? a.position.y + _lv.RimHeight * 1.08f : transform.position.y + 0.3f);
            return ri;
        }

        private float SurfaceY()
        {
            if (_lv == null) return transform.position.y;
            return _lv.transform.position.y + Mathf.Max(0.02f, _lv.LevelFraction) * _lv.RimHeight;
        }

        // ───────────────────────── сервер: пампушка на край, звёзды ─────────────────────────

        private void Update()
        {
            if (base.IsServerInitialized)
            {
                if (Time.time >= _scanAt) { _scanAt = Time.time + 0.25f; ServerBun(); }
                HoldBun();
                if (_evalAt > 0f && Time.time >= _evalAt) { _evalAt = 0f; ServerEval(); }
            }
            if (base.IsClientInitialized) Visuals();
        }

        private static bool IsBun(FoodPiece p)
        {
            ProductDef d = p != null ? p.Def : null;
            return d != null && !string.IsNullOrEmpty(d.Title) && d.Title.ToLowerInvariant().Contains("пампушк");
        }

        /// <summary>Пампушка рядом с миской (в ней или над ней) — на край. Взяли в руки — отпускаем.</summary>
        [Server]
        private void ServerBun()
        {
            if (_bun != null && (_bun.IsConsumed || _bun.IsHeld))
            {
                Rigidbody brb = _bun.GetComponent<Rigidbody>();
                if (brb != null && !_bun.IsConsumed) brb.isKinematic = false;
                _bun = null; _bunNet.Value = false; _bunGarlicNet.Value = false;
                _evalAt = Time.time + 0.1f;
            }
            if (_bun != null)
            {
                // намазали кистью уже на краю миски — пересчитать звёзды
                bool g = _bun.Def != null && !string.IsNullOrEmpty(_bun.Def.Title) && _bun.Def.Title.ToLowerInvariant().Contains("чесно");
                if (g != _bunGarlicNet.Value)
                {
                    _bunGarlicNet.Value = g;
                    if (_log) Debug.Log($"[Миска] «{name}»: пампушку на краю намазали чесночным маслом", this);
                    _evalAt = Time.time + 0.1f;
                }
                return;
            }
            RimInfo ri = Rim();
            Collider[] hits = Physics.OverlapSphere(ri.center + Vector3.up * (ri.y - ri.center.y) * 0.5f, ri.r * 1.3f, ~0, QueryTriggerInteraction.Ignore);
            foreach (Collider c in hits)
            {
                FoodPiece p = c.GetComponentInParent<FoodPiece>();
                if (p == null || p.IsHeld || p.IsConsumed || !IsBun(p)) continue;
                _bun = p;
                Rigidbody rb = p.GetComponent<Rigidbody>();
                if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.isKinematic = true; }
                bool garlic = p.Def != null && p.Def.Title.ToLowerInvariant().Contains("чесно");
                _bunNet.Value = true; _bunGarlicNet.Value = garlic;
                if (_log) Debug.Log($"[Миска] «{name}»: пампушка «{p.Def?.Title}» — на край миски", this);
                _evalAt = Time.time + 0.1f;
                return;
            }
        }

        /// <summary>Держим пампушку на крае (миску несут — пампушка едет с ней).</summary>
        [Server]
        private void HoldBun()
        {
            if (_bun == null) return;
            RimInfo ri = Rim();
            Vector3 side = transform.right; side.y = 0f;
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            side.Normalize();
            SphereCollider sc = _bun.GetComponent<SphereCollider>();
            float br = sc != null ? sc.radius * Mathf.Abs(_bun.transform.lossyScale.x) : 0.1f;
            Vector3 at = ri.center + side * (ri.r - br * 0.25f);
            at.y = ri.y + br * 0.55f;
            Rigidbody rb = _bun.GetComponent<Rigidbody>();
            if (rb != null && rb.isKinematic) rb.position = at;
            _bun.transform.position = at;
        }

        [Server]
        private void ServerEval()
        {
            int full = _cw.Def != null ? Mathf.Max(1, _cw.Def.Capacity) : 12;
            bool soup = _soup >= Mathf.CeilToInt(full * _soupFull);
            bool cream = _cream >= _dollop, zat = _zat >= _dollop, bun = _bunNet.Value;
            int stars = 0;
            if (_soup > 0)
            {
                stars = soup ? 2 : 1;
                if (cream) stars++;
                if (zat) stars++;
                if (bun) stars++;
                if (stars == 5 && !_bunGarlicNet.Value) stars = 4;        // пятая звезда — за пампушку с чесноком
                if (soup && cream && zat && bun && _bunGarlicNet.Value) stars = 5;
            }
            byte s = (byte)Mathf.Clamp(stars, 0, 5);
            if (s != _starsNet.Value)
            {
                _starsNet.Value = s;
                if (_log) Debug.Log($"[Миска] «{name}»: борщ {_soup}/{Mathf.CeilToInt(full * _soupFull)}, сметана {_cream}, затирка {_zat}, пампушка {(bun ? (_bunGarlicNet.Value ? "с чесноком" : "есть") : "нет")} → {s}★", this);
            }
        }

        // ───────────────────────── вид: шматы сметаны и затирки на борще ─────────────────────────

        private void Visuals()
        {
            float y = SurfaceY();
            RimInfo ri = Rim();
            float liquidR = _lv != null ? Mathf.Lerp(ri.r * 0.6f, ri.r * 0.95f, Mathf.Clamp01(_lv.LevelFraction)) : ri.r * 0.8f;
            Dollop(ref _creamT, "SourCream (auto)", _cream, new Color(0.98f, 0.97f, 0.93f), ref _mCream, ri.center + transform.forward * liquidR * 0.12f, y, liquidR * 0.32f, false);
            Dollop(ref _zatT, "Zatirka (auto)", _zat, new Color(0.95f, 0.86f, 0.80f), ref _mZat, ri.center - transform.forward * liquidR * 0.28f + transform.right * liquidR * 0.2f, y, liquidR * 0.24f, true);
        }

        private void Dollop(ref Transform t, string nm, int count, Color col, ref Material mat, Vector3 at, float y, float r, bool flecks)
        {
            if (count <= 0) { if (t != null) t.gameObject.SetActive(false); return; }
            if (t == null)
            {
                var go = new GameObject(nm) { hideFlags = HideFlags.DontSave };
                t = go.transform;
                go.AddComponent<MeshFilter>().sharedMesh = Blob(flecks);
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                if (mat == null)
                {
                    Shader sh = Shader.Find("CatCooks/Food Heap");
                    if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
                    mat = new Material(sh) { hideFlags = HideFlags.DontSave };
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.6f);
                    if (mat.HasProperty("_TexDetail")) mat.SetFloat("_TexDetail", 0f);
                    if (mat.HasProperty("_UseMeshUV")) mat.SetFloat("_UseMeshUV", 0f);
                }
                mr.sharedMaterial = mat;
            }
            t.gameObject.SetActive(true);
            float k = Mathf.Clamp(0.8f + 0.2f * count, 0.8f, 1.6f);
            t.position = new Vector3(at.x, y - r * 0.05f, at.z);
            t.rotation = Quaternion.Euler(0f, nm.Length * 37f, 0f);
            t.localScale = new Vector3(r * k, r * k * 0.55f, r * k);
        }

        /// <summary>Шмат: приплюснутый бугристый купол (единичный), с завитком сверху; затирка — с зелёными крапинками (цвет в вершинах).</summary>
        private static Mesh Blob(bool flecks)
        {
            const int La = 10, Lo = 24;
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            for (int i = 0; i <= La; i++)
            {
                float th = Mathf.PI * 0.5f * i / La;
                for (int j = 0; j <= Lo; j++)
                {
                    float ph = Mathf.PI * 2f * j / Lo;
                    float lump = 1f + 0.08f * Mathf.Sin(ph * 3f + th * 4f) + 0.05f * Mathf.Sin(ph * 5f - th * 2f);
                    float swirl = 1f + 0.25f * Mathf.Pow(Mathf.Cos(th), 6f) * Mathf.Sin(ph * 2f + th * 8f);
                    Vector3 p = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph) * lump, Mathf.Cos(th) * swirl, Mathf.Sin(th) * Mathf.Sin(ph) * lump);
                    v.Add(p);
                    bool fl = flecks && Mathf.Abs(Mathf.Sin(ph * 7f + th * 13f)) > 0.93f;
                    c.Add(fl ? new Color(0.30f, 0.55f, 0.20f) : Color.white);
                }
            }
            for (int i = 0; i < La; i++)
                for (int j = 0; j < Lo; j++)
                {
                    int a = i * (Lo + 1) + j, b = a + Lo + 1;
                    t.Add(a); t.Add(a + 1); t.Add(b); t.Add(a + 1); t.Add(b + 1); t.Add(b);
                }
            var m = new Mesh { name = flecks ? "ZatirkaBlob" : "CreamBlob", hideFlags = HideFlags.DontSave };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            // треугольники наружу
            Vector3[] vv = m.vertices; int[] tt = m.triangles;
            int q = (La / 2) * Lo * 6;
            Vector3 ctr = (vv[tt[q]] + vv[tt[q + 1]] + vv[tt[q + 2]]) / 3f;
            if (Vector3.Dot(Vector3.Cross(vv[tt[q + 1]] - vv[tt[q]], vv[tt[q + 2]] - vv[tt[q]]), ctr) < 0f)
            {
                for (int k = 0; k < tt.Length; k += 3) { int x = tt[k + 1]; tt[k + 1] = tt[k + 2]; tt[k + 2] = x; }
                m.triangles = tt; m.RecalculateNormals();
            }
            return m;
        }

        private void OnDestroy()
        {
            if (_creamT != null) Destroy(_creamT.gameObject);
            if (_zatT != null) Destroy(_zatT.gameObject);
        }

        // ───────────────────────── подсказка ─────────────────────────

        private void OnGUI()
        {
            Camera cam = Camera.main;
            if (cam == null || !CookwareAnalyzerHud.Visible) return;
            Vector3 c = Rim().center;
            if ((cam.transform.position - c).sqrMagnitude > 6f * 6f) return;
            Vector3 s = cam.WorldToScreenPoint(c + Vector3.up * 0.6f);
            if (s.z <= 0f) return;
            if (_st == null) _st = new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.MiddleCenter, richText = true };
            int full = _cw != null && _cw.Def != null ? Mathf.Max(1, _cw.Def.Capacity) : 12;
            int needSoup = Mathf.CeilToInt(full * _soupFull);
            string text = $"Борщ {Mark(_soup >= needSoup)} {_soup}/{needSoup} · сметана {Mark(_cream >= _dollop)} · затирка {Mark(_zat >= _dollop)} · пампушка {Mark(_bunNet.Value)}" +
                          (_bunNet.Value && !_bunGarlicNet.Value ? " (без чеснока)" : "") + (Stars > 0 ? $"   {new string('★', Stars)}" : "");
            Vector2 sz = _st.CalcSize(new GUIContent(text));
            GUI.Box(new Rect(s.x - sz.x * 0.5f - 6f, Screen.height - s.y - sz.y, sz.x + 12f, sz.y + 4f), text, _st);
        }

        private static string Mark(bool ok) => ok ? "<color=#5ad16a>✔</color>" : "<color=#ff6a5a>✖</color>";
    }
}
