using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// СОУС В ТАРЕ — «Масло с шалфеем» (и горелое) в ковше/сковороде/кастрюле.
    /// Ставится САМ (GnocchiBoiler) на любую тару, где появился соус, — у всех игроков, без сети.
    ///
    /// Вид: золотистое ореховое масло слоем на дне (толщина — от количества), на нём плавают
    /// обжаренные листья шалфея, коричневые крупинки; на огне (от 90 °C) масло шкварчит — пузырьки.
    /// Горелое — тёмное, листья чёрные. Кучкой этот продукт в таре не рисуется.
    /// </summary>
    public sealed class GnocchiSauceVisual : MonoBehaviour
    {
        private Cookware _cw;
        private CookwareLiquidVisual _lv;
        private CookwareHeat _heat;
        private Transform _root, _pool;
        private readonly List<Transform> _leaves = new List<Transform>(), _flecks = new List<Transform>(), _bubbles = new List<Transform>();
        private readonly List<float> _bubT = new List<float>();
        private int _sauce, _burnt;
        private float _shownH;
        private static readonly string[] Titles = { CookRecipeBook.Gnocchi.SageButter, CookRecipeBook.Gnocchi.BurntButter };

        public static bool IsSauce(ProductDef d) => d != null && (d.Title == CookRecipeBook.Gnocchi.SageButter || d.Title == CookRecipeBook.Gnocchi.BurntButter);

        private void Awake()
        {
            _cw = GetComponent<Cookware>();
            _lv = GetComponentInChildren<CookwareLiquidVisual>(true);
            _heat = GetComponent<CookwareHeat>();
            _root = new GameObject("GnocchiSauce (auto)").transform;
            _root.SetParent(transform, false);
            _pool = Child("ButterPool", GnocchiLooks.Pool(GetInstanceID()), PoolMat(false));
            if (_cw != null) _cw.ContentsChanged += Recount;
            Recount();
        }

        private void OnDestroy()
        {
            if (_cw != null) _cw.ContentsChanged -= Recount;
            if (_root != null) Destroy(_root.gameObject);
        }

        private static Material PoolMat(bool burnt) => burnt ? GnocchiLooks.Mat("Butter burnt", GnocchiLooks.ButterBurnt, 0.6f)
                                                             : GnocchiLooks.Mat("Butter sage", GnocchiLooks.Butter, 0.93f);
        private static Material LeafMat(bool burnt) => burnt ? GnocchiLooks.Mat("Sage burnt", GnocchiLooks.SageBurnt, 0.3f)
                                                             : GnocchiLooks.Mat("Sage fried", GnocchiLooks.SageFried, 0.7f);
        private static Material FleckMat => GnocchiLooks.Mat("Butter fleck", GnocchiLooks.Fleck, 0.5f);
        private static Material BubbleMat => GnocchiLooks.Mat("Butter foam", new Color(1f, 0.9f, 0.6f), 0.95f);

        private void Recount()
        {
            _sauce = _burnt = 0;
            if (_cw == null) return;
            FoodDatabase db = FoodDatabase.Instance;
            for (int i = 0; i < _cw.ViewCount; i++)
            {
                BagView v = _cw.GetView(i);
                if (v.Count == 0) continue;
                ProductDef d = db != null ? db.Get(v.Product) : null;
                if (d == null) continue;
                if (d.Title == CookRecipeBook.Gnocchi.SageButter) _sauce += v.Count;
                else if (d.Title == CookRecipeBook.Gnocchi.BurntButter) _burnt += v.Count;
            }
            HeapIgnore.Add(gameObject, Titles);
            bool burnt = _burnt > _sauce;
            int all = _sauce + _burnt;
            _root.gameObject.SetActive(all > 0);
            if (all == 0) return;
            _pool.GetComponent<MeshRenderer>().sharedMaterial = PoolMat(burnt);
            Sync(_leaves, Mathf.Clamp(2 + all / 2, 3, 9), "SageLeaf", GnocchiLooks.SageLeaf, LeafMat(burnt));
            foreach (Transform t in _leaves) t.GetComponent<MeshRenderer>().sharedMaterial = LeafMat(burnt);
            Sync(_flecks, Mathf.Clamp(all * 3, 6, 24), "Fleck", GnocchiLooks.Crumb, FleckMat);
        }

        private void LateUpdate()
        {
            if (_cw == null || _sauce + _burnt == 0) return;
            // геометрия тары: LiquidAnchor (центр дна, радиус у края) или объём Inside
            Vector3 c; float R, bottom, depth;
            if (_lv != null)
            {
                c = _lv.transform.position; bottom = c.y;
                R = Mathf.Max(0.05f, _lv.RadiusAtRimWorld * 0.82f);
                depth = Mathf.Max(0.1f, _cw.MouthPoint.y - bottom);
            }
            else if (_cw.InsideVolume != null)
            {
                Bounds b = _cw.InsideVolume.bounds;
                c = b.center; bottom = b.min.y;
                R = Mathf.Max(0.05f, Mathf.Min(b.extents.x, b.extents.z) * 0.85f);
                depth = Mathf.Max(0.1f, b.size.y);
            }
            else { c = _cw.MouthPoint; bottom = c.y - 0.1f; R = 0.2f; depth = 0.2f; }
            // есть вода/жидкость — масло плавает пятном сверху
            if (_lv != null && _lv.Level > 0.01f && _lv.TryGetSurfaceY(c, out float sy)) bottom = sy - 0.004f;

            int all = _sauce + _burnt;
            float cap = Mathf.Max(1f, _cw.Capacity);
            float h = Mathf.Clamp(depth * all / cap, 0.012f, depth * 0.6f);              // слой масла
            _shownH = Mathf.MoveTowards(_shownH, h, Time.deltaTime * 0.2f);
            float spread = Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(all / 6f));
            float top = bottom + _shownH;

            float temp = _heat != null ? _heat.TemperatureC : 20f;
            bool sizzle = temp > 90f;
            float t = Time.time;

            Vector3 rootPos = new Vector3(c.x, bottom, c.z);
            _pool.position = rootPos;
            _pool.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            SetWorldScale(_pool, new Vector3(R * 2f * spread, _shownH, R * 2f * spread));

            float leafLen = Mathf.Clamp(R * 0.45f, 0.04f, GnocchiMesh.Length * 0.7f);
            for (int i = 0; i < _leaves.Count; i++)
            {
                Transform l = _leaves[i];
                float a = i * 2.399f + (sizzle ? t * 0.15f : 0f);
                float rr = Mathf.Sqrt((i + 0.6f) / _leaves.Count) * R * 0.7f * spread;
                float bob = sizzle ? Mathf.Sin(t * 5f + i) * 0.004f : 0f;
                l.position = new Vector3(c.x + Mathf.Cos(a) * rr, top + 0.003f + bob, c.z + Mathf.Sin(a) * rr);
                l.rotation = Quaternion.Euler(sizzle ? Mathf.Sin(t * 4f + i) * 6f : 0f, i * 73f + (sizzle ? t * 10f : 0f), 0f);
                SetWorldScale(l, Vector3.one * leafLen * (0.85f + 0.3f * ((i * 0.37f) % 1f)));
            }
            for (int i = 0; i < _flecks.Count; i++)
            {
                Transform f = _flecks[i];
                float a = i * 2.18f + 0.4f, rr = Mathf.Sqrt((i + 0.3f) / _flecks.Count) * R * 0.85f * spread;
                f.position = new Vector3(c.x + Mathf.Cos(a) * rr, top + 0.001f, c.z + Mathf.Sin(a) * rr);
                f.rotation = Quaternion.Euler(i * 31f, i * 57f, i * 13f);
                SetWorldScale(f, Vector3.one * leafLen * 0.08f * (0.6f + 0.8f * ((i * 0.53f) % 1f)));
            }
            // шкварчит: пузырьки появляются, растут и лопаются
            int nb = sizzle ? Mathf.Clamp(Mathf.RoundToInt((temp - 90f) / 12f) + 4, 4, 14) : 0;
            Sync(_bubbles, nb, "Foam", GnocchiLooks.Bead, BubbleMat);
            while (_bubT.Count < _bubbles.Count) _bubT.Add(Random.value);
            for (int i = 0; i < _bubbles.Count; i++)
            {
                if (i >= nb) continue;
                _bubT[i] += Time.deltaTime * (1.2f + 0.4f * (i % 3));
                if (_bubT[i] >= 1f) _bubT[i] -= 1f;
                float k = _bubT[i];
                float a = (i * 1.7f + Mathf.Floor(t * 0.9f + i * 0.31f) * 2.3f), rr = ((i * 0.618f + Mathf.Floor(t + i * 0.5f) * 0.37f) % 1f) * R * 0.8f * spread;
                Transform b = _bubbles[i];
                b.position = new Vector3(c.x + Mathf.Cos(a) * rr, top, c.z + Mathf.Sin(a) * rr);
                b.rotation = Quaternion.identity;
                float s = leafLen * 0.12f * Mathf.Sin(k * Mathf.PI);
                SetWorldScale(b, new Vector3(s, s * 0.6f, s));
            }
        }

        private Transform Child(string n, Mesh mesh, Material mat)
        {
            var go = new GameObject(n);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.SetParent(_root, false);
            return go.transform;
        }

        private void Sync(List<Transform> list, int n, string nm, Mesh mesh, Material mat)
        {
            while (list.Count < n) list.Add(Child(nm, mesh, mat));
            for (int i = 0; i < list.Count; i++) if (list[i] != null) list[i].gameObject.SetActive(i < n);
        }

        private static void SetWorldScale(Transform t, Vector3 world)
        {
            Vector3 ps = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(world.x / Mathf.Max(1e-4f, Mathf.Abs(ps.x)), world.y / Mathf.Max(1e-4f, Mathf.Abs(ps.y)), world.z / Mathf.Max(1e-4f, Mathf.Abs(ps.z)));
        }
    }

    /// <summary>Не рисовать продукты кучкой в таре: добавить в Ignore у Cookware Heap Visual (рефлексией, только вид).</summary>
    public static class HeapIgnore
    {
        private static FieldInfo _f;
        private static MethodInfo _refresh;

        public static void Add(GameObject root, string[] titles)
        {
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null || db.Table == null || root == null) return;
            var defs = new List<ProductDef>();
            for (int id = 1; id < 2048; id++)
            {
                ProductDef p = db.Table.GetProduct((ushort)id);
                if (p != null && System.Array.IndexOf(titles, p.Title) >= 0) defs.Add(p);
            }
            if (defs.Count == 0) return;
            if (_f == null) _f = typeof(CookwareHeapVisual).GetField("_ignore", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_refresh == null) _refresh = typeof(CookwareHeapVisual).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, System.Type.EmptyTypes, null);
            if (_f == null) return;
            foreach (CookwareHeapVisual hv in root.GetComponentsInChildren<CookwareHeapVisual>(true))
            {
                var arr = _f.GetValue(hv) as ProductDef[];
                var list = new List<ProductDef>(arr ?? new ProductDef[0]);
                bool changed = false;
                foreach (ProductDef d in defs) if (!list.Contains(d)) { list.Add(d); changed = true; }
                if (!changed) continue;
                _f.SetValue(hv, list.ToArray());
                if (_refresh != null) _refresh.Invoke(hv, null);
            }
        }
    }
}
