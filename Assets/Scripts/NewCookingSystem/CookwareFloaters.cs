using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// КАК ВЫГЛЯДИТ ПРОДУКТ — запоминается у всех игроков, когда кусок появляется и когда исчезает (его проглотила тара).
    /// По этому виду CookwareFloaters рисует целые куски, плавающие в супе. Мясо туши (MeatCut, процедурное) не запоминается.
    /// </summary>
    public static class FoodLookCache
    {
        public struct Part { public Mesh Mesh; public Material[] Mats; public Vector3 Pos; public Quaternion Rot; public Vector3 Scale; }

        public sealed class Look
        {
            public Part[] Parts;
            public Vector3 RootScale;
            public int Count;            // порций в одном таком куске (луковица — 6)
            public float Size;           // габарит, м (мир)
        }

        private static readonly Dictionary<ushort, List<Look>> _map = new Dictionary<ushort, List<Look>>();

        public static void Note(FoodPiece p)
        {
            if (p == null || p is MeatCut) return;
            ProductDef d = p.Def;
            if (d == null) return;
            MeshFilter[] mfs = p.GetComponentsInChildren<MeshFilter>();
            if (mfs.Length == 0) return;
            if (!_map.TryGetValue(d.Id, out List<Look> list)) { list = new List<Look>(); _map[d.Id] = list; }
            Mesh first = null;
            foreach (MeshFilter mf in mfs) if (mf.sharedMesh != null) { first = mf.sharedMesh; break; }
            if (first == null) return;
            foreach (Look l in list) if (l.Parts.Length > 0 && l.Parts[0].Mesh == first) return;   // этот вид уже есть
            if (list.Count >= 6) return;
            Transform root = p.transform;
            var parts = new List<Part>();
            Bounds b = new Bounds(root.position, Vector3.zero); bool any = false;
            foreach (MeshFilter mf in mfs)
            {
                if (mf.sharedMesh == null) continue;
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled || (mf.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;
                Matrix4x4 rel = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                parts.Add(new Part { Mesh = mf.sharedMesh, Mats = mr.sharedMaterials, Pos = rel.GetColumn(3), Rot = rel.rotation, Scale = rel.lossyScale });
                if (!any) { b = mr.bounds; any = true; } else b.Encapsulate(mr.bounds);
            }
            if (parts.Count == 0) return;
            list.Add(new Look { Parts = parts.ToArray(), RootScale = root.lossyScale, Count = Mathf.Max(1, p.Count), Size = any ? b.size.magnitude : 0.2f });
        }

        public static Look Get(ushort product, int seed)
        {
            if (!_map.TryGetValue(product, out List<Look> list) || list.Count == 0) return null;
            return list[Mathf.Abs(seed) % list.Count];
        }
    }

    /// <summary>
    /// КУСКИ В СУПЕ: кости, голяшка, луковицы, морковь в кастрюле с водой/бульоном плавают — медленно кружат по кругу
    /// (как лодочки), покачиваются и поворачиваются; тяжёлое (кости, мясо) — ближе ко дну, овощи — у поверхности.
    /// Кипит — кружат быстрее и подпрыгивают.
    ///
    /// Ставить не надо: вешается сам на каждую жидкость тары (Cookware Liquid Visual) с галкой «Суп».
    /// У сковороды «Суп» выключен — там куски лежат собой. Чисто вид, у каждого игрока свой, по сети ничего.
    /// Вид куска — тот, каким он был до того, как его проглотила тара (FoodLookCache).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]          // после Cookware Liquid Visual — поверх его свойств поверхности (жирок бульона)
    public sealed class CookwareFloaters : MonoBehaviour
    {
        [Tooltip("Сколько кусков всего плавает, не больше")]
        [Range(1, 24)][SerializeField] private int _max = 14;
        [Tooltip("Скорость кружения (рад/с) в покое и при кипении")]
        [SerializeField] private Vector2 _swirl = new Vector2(0.12f, 0.55f);
        [Tooltip("Покачивание вверх-вниз, доля высоты жидкости")]
        [Range(0f, 0.2f)][SerializeField] private float _bob = 0.04f;
        [SerializeField] private bool _shadows = true;

        private static readonly FieldInfo SoupF = typeof(CookwareLiquidVisual).GetField("_soup", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo RbF = typeof(CookwareLiquidVisual).GetField("_radiusBottom", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo RtF = typeof(CookwareLiquidVisual).GetField("_radiusTop", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ProductsF = typeof(CookwareLiquidVisual).GetField("_products", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly int CreamColorId = Shader.PropertyToID("_CreamColor"), CreamCoverId = Shader.PropertyToID("_CreamCover"),
            CreamScaleId = Shader.PropertyToID("_CreamScale"), CreamFoldId = Shader.PropertyToID("_CreamFold"), CreamShadowId = Shader.PropertyToID("_CreamShadow"),
            CreamRimId = Shader.PropertyToID("_CreamRim"), GlintId = Shader.PropertyToID("_GlintStrength"), OpacityId = Shader.PropertyToID("_Opacity");
        private bool _entriesDone;
        private float _broth;             // доля бульона в жидкости 0..1 — жирок на поверхности
        private bool _red;                // это борщ: жир оранжевый (от зажарки)
        private Renderer _surface;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private sealed class Floater
        {
            public Transform T;
            public ushort Product;
            public FoodState State;
            public float Depth, Rad, Ang, Phase, Spin, Tilt, Half;
            public Renderer[] Rs;
        }

        private CookwareLiquidVisual _lv;
        private Cookware _cw;
        private readonly List<Floater> _fl = new List<Floater>();
        private string _sig = "";
        private float _nextCheck;
        private MaterialPropertyBlock _mpb;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            foreach (CookwareLiquidVisual lv in Object.FindObjectsByType<CookwareLiquidVisual>(FindObjectsSortMode.None))
                if (lv != null && lv.GetComponent<CookwareFloaters>() == null) lv.gameObject.AddComponent<CookwareFloaters>();
        }

        private void Awake()
        {
            _lv = GetComponent<CookwareLiquidVisual>();
            _cw = GetComponentInParent<Cookware>();
            _mpb = new MaterialPropertyBlock();
        }

        private void OnDisable() => Clear();
        private void OnDestroy() => Clear();

        private bool Soup => _lv != null && SoupF != null && (bool)SoupF.GetValue(_lv);

        private void Update()
        {
            if (_lv == null || _cw == null) return;
            if (Time.time >= _nextCheck) { _nextCheck = Time.time + 0.5f; Sync(); }
            if (_fl.Count == 0) return;

            float rim = Mathf.Max(0.01f, _lv.RimHeight);
            float level = Mathf.Max(0f, _lv.LevelFraction) * rim;
            float rb = RbF != null ? (float)RbF.GetValue(_lv) : _lv.RadiusAtRimWorld * 0.8f;
            float rt = RtF != null ? (float)RtF.GetValue(_lv) : _lv.RadiusAtRimWorld;
            float boil = Mathf.Clamp01(_cw.Agitation01 * 2.5f);
            float w = Mathf.Lerp(_swirl.x, _swirl.y, boil);
            Transform a = transform;
            float t = Time.time, dt = Time.deltaTime;
            foreach (Floater f in _fl)
            {
                if (f.T == null) continue;
                f.Ang += w * dt * (0.8f + 0.4f * Mathf.Sin(f.Phase));
                float bobY = Mathf.Sin(t * (1.3f + boil * 2f) + f.Phase) * _bob * level * (1f + boil * 1.5f);
                float y = Mathf.Clamp(level * f.Depth + bobY, f.Half * 0.6f, Mathf.Max(f.Half * 0.6f, level - f.Half * 0.3f));
                float wall = Mathf.Lerp(rb, rt, Mathf.Clamp01(y / rim)) - f.Half;
                float r = Mathf.Max(0f, wall) * f.Rad;
                Vector3 local = new Vector3(Mathf.Cos(f.Ang) * r, y, Mathf.Sin(f.Ang) * r);
                f.T.position = a.position + a.rotation * local;
                f.T.rotation = a.rotation * Quaternion.Euler(Mathf.Sin(t * 0.9f + f.Phase) * f.Tilt, f.Ang * Mathf.Rad2Deg * 0.6f + f.Spin * t, Mathf.Cos(t * 0.7f + f.Phase) * f.Tilt);
            }
        }

        /// <summary>Что плавает: целые куски в таре (не жидкость, не соль), если в таре есть жидкость.</summary>
        private void Sync()
        {
            if (!_entriesDone) EnsureBrothEntry();
            BrothShare();
            var want = new List<KeyValuePair<ushort, FoodState>>();
            var sb = new System.Text.StringBuilder();
            if (Soup && _lv.HasLiquid && _lv.LevelFraction > 0.08f)
            {
                FoodDatabase db = FoodDatabase.Instance;
                for (int i = 0; i < _cw.ViewCount && want.Count < _max; i++)
                {
                    BagView v = _cw.GetView(i);
                    if (v.Count <= 0 || _lv.Handles(v.Product)) continue;
                    ProductDef d = db != null ? db.Get(v.Product) : null;
                    if (d == null || FoodHeapLook.IsLiquid(d, v.State) || FoodUnits.IsAdditive(d, v.State)) continue;
                    FoodLookCache.Look look = FoodLookCache.Get(v.Product, 0);
                    if (look == null) continue;                                  // вид не знаем — его рисует горка
                    int n = Mathf.Clamp(Mathf.RoundToInt(v.Count / (float)look.Count), 1, 8);
                    for (int k = 0; k < n && want.Count < _max; k++) want.Add(new KeyValuePair<ushort, FoodState>(v.Product, v.State));
                    sb.Append(v.Product).Append(':').Append((int)v.State).Append('x').Append(n).Append(';');
                }
            }
            string sig = sb.ToString();
            if (sig == _sig) return;
            _sig = sig;
            Rebuild(want);
        }

        private void Rebuild(List<KeyValuePair<ushort, FoodState>> want)
        {
            // старые куски сохраняют своё место в круге — пересоздаём только вид
            var old = new List<Floater>(_fl);
            _fl.Clear();
            FoodDatabase db = FoodDatabase.Instance;
            for (int i = 0; i < want.Count; i++)
            {
                ushort pid = want[i].Key; FoodState st = want[i].Value;
                Floater prev = null;
                for (int j = 0; j < old.Count; j++) if (old[j] != null && old[j].Product == pid) { prev = old[j]; old[j] = null; break; }
                if (prev != null && prev.State == st) { _fl.Add(prev); continue; }
                FoodLookCache.Look look = FoodLookCache.Get(pid, i * 7 + 3);
                if (look == null) continue;
                ProductDef d = db != null ? db.Get(pid) : null;
                var f = new Floater { Product = pid, State = st };
                f.T = Build(look, d, st, out f.Rs);
                f.Half = look.Size * 0.35f;
                bool heavy = IsHeavy(d);
                float rnd = Hash(i * 13 + pid);
                f.Depth = heavy ? Mathf.Lerp(0.12f, 0.55f, rnd) : (rnd < 0.25f ? Mathf.Lerp(0.3f, 0.6f, rnd * 4f) : Mathf.Lerp(0.72f, 0.95f, rnd));
                f.Rad = Mathf.Lerp(0.15f, 0.8f, Hash(i * 31 + 7));
                f.Ang = prev != null ? prev.Ang : Hash(i * 17 + 1) * Mathf.PI * 2f;
                f.Phase = Hash(i * 5 + 11) * 6.28f;
                f.Spin = Mathf.Lerp(-25f, 25f, Hash(i * 3 + 2));
                f.Tilt = heavy ? Mathf.Lerp(5f, 20f, rnd) : Mathf.Lerp(10f, 35f, rnd);
                if (prev != null && prev.T != null) Destroy(prev.T.gameObject);
                _fl.Add(f);
            }
            foreach (Floater o in old) if (o != null && o.T != null) Destroy(o.T.gameObject);
        }

        private Transform Build(FoodLookCache.Look look, ProductDef d, FoodState st, out Renderer[] rs)
        {
            var root = new GameObject("SoupPiece (auto)") { hideFlags = HideFlags.DontSave };
            root.transform.localScale = look.RootScale;
            var list = new List<Renderer>();
            foreach (FoodLookCache.Part p in look.Parts)
            {
                var go = new GameObject("Part") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = p.Pos; go.transform.localRotation = p.Rot; go.transform.localScale = p.Scale;
                go.AddComponent<MeshFilter>().sharedMesh = p.Mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = p.Mats;
                mr.shadowCastingMode = _shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                // цвет по состоянию (опалено, варено…) — как у самого куска
                Material m0 = p.Mats != null && p.Mats.Length > 0 ? p.Mats[0] : null;
                Color c = Color.white;
                if (m0 != null) { if (m0.HasProperty(BaseColorId)) c = m0.GetColor(BaseColorId); else if (m0.HasProperty(ColorId)) c = m0.GetColor(ColorId); }
                Color tint = FoodStateVisual.HeatTint(c, st, d, 1f);
                if (tint != c)
                {
                    mr.GetPropertyBlock(_mpb);
                    _mpb.SetColor(BaseColorId, tint); _mpb.SetColor(ColorId, tint);
                    mr.SetPropertyBlock(_mpb);
                }
                list.Add(mr);
            }
            rs = list.ToArray();
            return root.transform;
        }

        private static bool IsHeavy(ProductDef d)
        {
            if (d == null || string.IsNullOrEmpty(d.Title)) return false;
            string t = d.Title.ToLowerInvariant();
            if (t.Contains("кост") || t.Contains("голяш") || t.Contains("мяс") || t.Contains("говяд") || t.Contains("свин") || t.Contains("рёбр") || t.Contains("ребр")) return true;
            return d.Group != null && d.Group.name.Contains("Meat");
        }

        private static float Hash(int n)
        {
            n = (n << 13) ^ n;
            return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483647f;
        }

        /// <summary>
        /// Бульон — жидкость этой тары, даже если «Тара: принимать ВСЁ» нажимали до того, как он появился
        /// (иначе после варки бульон «исчезал» — тара не знала, что его рисовать жидкостью).
        /// </summary>
        private void EnsureBrothEntry()
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null || ProductsF == null) return;
            _entriesDone = true;
            var arr = ProductsF.GetValue(_lv) as CookwareLiquidVisual.Entry[];
            var list = new List<CookwareLiquidVisual.Entry>();
            if (arr != null) list.AddRange(arr);
            bool added = false;
            for (int id = 1; id < 2048; id++)
            {
                ProductDef p = db.Get((ushort)id);
                if (p == null || string.IsNullOrEmpty(p.Title)) continue;
                string tl = p.Title.Trim().ToLowerInvariant();
                bool garlicOil = tl.Contains("масло") && tl.Contains("чесно");
                if (!tl.Contains("бульон") && tl != "борщ" && !garlicOil) continue;
                bool has = false;
                foreach (CookwareLiquidVisual.Entry e in list) if (e.Product == p) { has = true; break; }
                if (has) continue;
                list.Add(new CookwareLiquidVisual.Entry { Product = p, Role = CookwareLiquidVisual.Role.Liquid, Color = new Color(0f, 0f, 0f, 0f) });
                added = true;
            }
            if (added) ProductsF.SetValue(_lv, list.ToArray());
        }

        /// <summary>Сколько в жидкости бульона (по названию) — от этого жирок на поверхности.</summary>
        private void BrothShare()
        {
            FoodDatabase db = FoodDatabase.Instance;
            int broth = 0, all = 0, red = 0;
            for (int i = 0; i < _cw.ViewCount; i++)
            {
                BagView v = _cw.GetView(i);
                if (v.Count <= 0 || !_lv.TryGetRole(v.Product, out CookwareLiquidVisual.Role role) || role != CookwareLiquidVisual.Role.Liquid) continue;
                all += v.Count;
                ProductDef d = db != null ? db.Get(v.Product) : null;
                string tl = d != null && !string.IsNullOrEmpty(d.Title) ? d.Title.Trim().ToLowerInvariant() : "";
                if (tl.Contains("бульон")) broth += v.Count;
                else if (tl == "борщ") { broth += v.Count; red += v.Count; }
            }
            _broth = all > 0 ? broth / (float)all : 0f;
            _red = broth > 0 && red * 2 > broth;
        }

        /// <summary>Бульон: золотистая жидкость, по поверхности — тёмные капли жира (островки «слоя сверху»), блестит.</summary>
        private void LateUpdate()
        {
            if (_broth < 0.3f || _lv == null) return;
            if (_surface == null)
            {
                Transform s = _lv.transform.Find("LiquidSurface (auto)");
                if (s != null) _surface = s.GetComponent<Renderer>();
                if (_surface == null) return;
            }
            if (!_surface.enabled) return;
            _surface.GetPropertyBlock(_mpb);
            float k = Mathf.InverseLerp(0.3f, 0.8f, _broth);
            _mpb.SetColor(CreamColorId, _red ? new Color(0.92f, 0.42f, 0.10f, 1f) : new Color(0.62f, 0.40f, 0.12f, 1f));   // борщ — оранжевый жир, бульон — тёмно-золотой
            _mpb.SetFloat(CreamCoverId, Mathf.Lerp(0.12f, 0.34f, k));
            _mpb.SetFloat(CreamScaleId, 26f);                                       // много мелких капель
            _mpb.SetFloat(CreamFoldId, 0.15f);
            _mpb.SetFloat(CreamShadowId, 0.2f);
            _mpb.SetFloat(CreamRimId, 0.65f);                                       // светлая кромка капли — блестит
            _mpb.SetFloat(GlintId, 2.2f);
            _mpb.SetFloat(OpacityId, 0.86f);
            _surface.SetPropertyBlock(_mpb);
        }

        private void Clear()
        {
            foreach (Floater f in _fl) if (f.T != null) Destroy(f.T.gameObject);
            _fl.Clear();
            _sig = "";
        }
    }
}
