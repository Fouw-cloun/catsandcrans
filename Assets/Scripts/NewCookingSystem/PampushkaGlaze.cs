using FishNet;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ПАМПУШКА С ЧЕСНОКОМ — вид: блестящий слой масла и зелёные крапинки сверху, когда пампушку намазали кистью чесночным маслом
    /// (Basting Brush: макнуть в «Масло чесночное с зеленью», водить по пампушкам).
    /// Soak In Oil ✔ — по-старому: пропитывается, лёжа в таре с маслом.
    /// Вешается на префаб пампушки (меню Борщ → 14). Работает на сервере, по сети — через продукт FoodPiece.
    /// Сверху появляются зелёные крапинки (у всех игроков).
    /// </summary>
    [RequireComponent(typeof(FoodPiece))]
    public sealed class PampushkaGlaze : MonoBehaviour
    {
        [Tooltip("Во что превращается. Пусто — «Пампушка с чесноком» по названию")]
        [SerializeField] private ProductDef _glazedProduct;
        [Tooltip("Сколько секунд лежать в чесночном масле")]
        [Min(0.2f)][SerializeField] private float _seconds = 2f;
        [Tooltip("Пропитываться, просто лёжа в таре с чесночным маслом (выкл — только кистью, как задумано)")]
        [SerializeField] private bool _soakInOil = false;
        [SerializeField] private bool _log = true;

        private FoodPiece _food;
        private float _soak, _checkAt;
        private Transform _herbs;

        private void Awake() => _food = GetComponent<FoodPiece>();

        private ProductDef Glazed()
        {
            if (_glazedProduct != null) return _glazedProduct;
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return null;
            for (int id = 1; id < 2048; id++)
            {
                ProductDef p = db.Get((ushort)id);
                if (p != null && !string.IsNullOrEmpty(p.Title) && p.Title.Trim().ToLowerInvariant() == "пампушка с чесноком") { _glazedProduct = p; return p; }
            }
            return null;
        }

        private static bool IsGarlicOil(ProductDef d)
        {
            if (d == null || string.IsNullOrEmpty(d.Title)) return false;
            string t = d.Title.ToLowerInvariant();
            return t.Contains("масло") && t.Contains("чесно");
        }

        private void Update()
        {
            ShowHerbs();
            if (!_soakInOil || !InstanceFinder.IsServerStarted || _food == null || _food.IsConsumed || _food.IsHeld) return;
            if (Time.time < _checkAt) return;
            float dt = 0.25f;
            _checkAt = Time.time + dt;
            ProductDef d = _food.Def;
            if (d == null || string.IsNullOrEmpty(d.Title)) return;
            string t = d.Title.ToLowerInvariant();
            if (!t.Contains("пампушк") || t.Contains("чесно")) return;
            Vector3 p = transform.position;
            foreach (Cookware cw in Cookware.All)
            {
                if (cw == null) continue;
                Collider iv = cw.InsideVolume;
                if (!(cw.IsInside(p) || (iv != null && iv.bounds.Contains(p)))) continue;
                FoodDatabase db = FoodDatabase.Instance;
                ushort oil = 0;
                for (int i = 0; i < cw.ViewCount && oil == 0; i++)
                {
                    BagView v = cw.GetView(i);
                    if (v.Count > 0 && IsGarlicOil(db != null ? db.Get(v.Product) : null)) oil = v.Product;
                }
                if (oil == 0) { _soak = 0f; return; }
                _soak += dt;
                if (_soak < _seconds) return;
                ProductDef g = Glazed();
                if (g == null) { if (_log) Debug.LogWarning("[Пампушка] нет продукта «Пампушка с чесноком» — меню Борщ → 13", this); _soak = -999f; return; }
                cw.ServerTake(oil, 1);
                _food.ServerSetProductState(g, _food.State);
                if (_log) Debug.Log($"[Пампушка] «{name}» пропиталась чесночным маслом — «{g.Title}»", this);
                return;
            }
            _soak = 0f;
        }

        private Transform _coat;
        private MeshFilter _doughMf;

        /// <summary>
        /// Пампушка с чесноком: сверху блестящий слой масла (по форме самой булки) и зелёные крапинки укропа.
        /// Вид у всех — по продукту. Намазать можно где угодно: на решётке, на столе, на краю миски.
        /// </summary>
        private void ShowHerbs()
        {
            bool glazed = _food != null && _food.Def != null && !string.IsNullOrEmpty(_food.Def.Title) && _food.Def.Title.ToLowerInvariant().Contains("чесно");
            if (!glazed)
            {
                if (_herbs != null) _herbs.gameObject.SetActive(false);
                if (_coat != null) _coat.gameObject.SetActive(false);
                return;
            }
            if (_doughMf == null)
            {
                Transform m = transform.Find("DoughModel (auto)");
                if (m != null) _doughMf = m.GetComponent<MeshFilter>();
            }
            if (_coat == null && _doughMf != null && _doughMf.sharedMesh != null) BuildCoat();
            if (_herbs == null)
            {
                var go = new GameObject("Herbs (auto)") { hideFlags = HideFlags.DontSave };
                _herbs = go.transform;
                _herbs.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = Specks();
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Unlit/Color");
                var mat = new Material(sh) { hideFlags = HideFlags.DontSave };
                Color c = new Color(0.22f, 0.5f, 0.15f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _herbs.gameObject.SetActive(true);
            if (_coat != null) _coat.gameObject.SetActive(true);

            // крапинки — по настоящему верху булки (меш теста оседает, коллайдер меньше)
            Renderer dr = _doughMf != null ? _doughMf.GetComponent<Renderer>() : null;
            if (dr != null)
            {
                Bounds b = dr.bounds;
                Vector3 e = b.extents * 1.02f;
                Vector3 ls = transform.lossyScale;
                _herbs.position = new Vector3(b.center.x, b.max.y - e.y, b.center.z);
                _herbs.rotation = Quaternion.identity;
                _herbs.localScale = new Vector3(e.x / Mathf.Max(1e-4f, Mathf.Abs(ls.x)), e.y / Mathf.Max(1e-4f, Mathf.Abs(ls.y)), e.z / Mathf.Max(1e-4f, Mathf.Abs(ls.z)));
            }
            else
            {
                SphereCollider sc = GetComponent<SphereCollider>();
                float r = sc != null ? sc.radius : 0.1f;
                _herbs.localPosition = Vector3.zero;
                _herbs.localScale = Vector3.one * r * 1.01f;
                _herbs.rotation = Quaternion.identity;
            }
        }

        /// <summary>Слой масла: тот же меш, что у булки (следует за ней), чуть больше, полупрозрачный и блестящий.</summary>
        private void BuildCoat()
        {
            var go = new GameObject("OilCoat (auto)") { hideFlags = HideFlags.DontSave };
            go.layer = _doughMf.gameObject.layer;
            _coat = go.transform;
            _coat.SetParent(_doughMf.transform, false);
            _coat.localScale = Vector3.one * 1.015f;
            go.AddComponent<MeshFilter>().sharedMesh = _doughMf.sharedMesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.sharedMaterial = OilMaterial();
        }

        private static Material _oilMat;

        private static Material OilMaterial()
        {
            if (_oilMat != null) return _oilMat;
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh) { name = "GarlicOilCoat (auto)", hideFlags = HideFlags.DontSave };
            Color c = new Color(0.98f, 0.82f, 0.32f, 0.38f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.92f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.92f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            // прозрачный (URP Lit: Surface = Transparent, Blend = Alpha)
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _oilMat = m;
            return m;
        }

        private static Mesh _specks;

        /// <summary>Мелкие плоские крапинки на верхней полусфере единичного радиуса.</summary>
        private static Mesh Specks()
        {
            if (_specks != null) return _specks;
            var v = new System.Collections.Generic.List<Vector3>(); var t = new System.Collections.Generic.List<int>();
            var rnd = new System.Random(7);
            for (int k = 0; k < 40; k++)
            {
                float th = (float)rnd.NextDouble() * 1.1f, ph = (float)rnd.NextDouble() * 6.283f;
                Vector3 n = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                Vector3 u = Vector3.Cross(n, Vector3.right).normalized; if (u.sqrMagnitude < 0.01f) u = Vector3.forward;
                Vector3 w = Vector3.Cross(n, u);
                float s = 0.05f + (float)rnd.NextDouble() * 0.05f;
                int i = v.Count;
                v.Add(n + (u + w) * s); v.Add(n + (u - w) * s); v.Add(n + (-u - w) * s); v.Add(n + (-u + w) * s);
                t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
                t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2);
            }
            _specks = new Mesh { name = "HerbSpecks", hideFlags = HideFlags.DontSave };
            _specks.SetVertices(v); _specks.SetTriangles(t, 0);
            _specks.RecalculateNormals(); _specks.RecalculateBounds();
            return _specks;
        }
    }
}
