using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЕДА В ТАРЕ — КУСОЧКАМИ, А НЕ «БЛИНОМ». Обычная горка тары рисует всё одной массой среднего цвета.
    /// Этот компонент для своих продуктов рисует настоящие кусочки (у каждого игрока сам, по синхронизированному
    /// содержимому тары — по сети ничего не шлёт):
    ///   • «Говядина соломкой» — брусочки сырого мяса (красные, с прожилками);
    ///   • «Говядина обжаренная» — те же брусочки с румяной корочкой;
    ///   • «Мясо с луком» — румяная соломка + золотистые полукольца лука;
    ///   • «Бефстроганов» — соломка и лук в розовато-сливочном соусе (гладкая глянцевая поверхность, кусочки торчат);
    ///   • «Бефстроганов (сметана свернулась)» — соус сероватый с белыми хлопьями;
    ///   • «Лук репчатый» тёртый — белые полупрозрачные полукольца.
    /// Эти продукты убираются из обычной горки (меню ставит их в Ignore у CookwareHeapVisual), остальное — как было.
    /// Размер — по CookwareFillTuner (сковорода), иначе по габариту тары (тарелка).
    /// Ставит меню CatCooks → Бефстроганов → 2 (сковорода) и 3 (тарелка).
    /// </summary>
    [RequireComponent(typeof(Cookware))]
    public sealed class CookwareFoodDecor : MonoBehaviour
    {
        [Tooltip("Материал с цветом в вершинах (мультяшный материал игры). Пусто — URP Particles/Unlit")]
        [SerializeField] private Material _material;
        [Tooltip("Размер кусочков (доля радиуса дна)")]
        [Range(0.05f, 0.5f)][SerializeField] private float _pieceSize = 0.26f;
        [Tooltip("Брусочков мяса на порцию")]
        [Range(1, 10)][SerializeField] private int _stripsPerPortion = 5;
        [Tooltip("Полуколец лука на порцию")]
        [Range(1, 10)][SerializeField] private int _ringsPerPortion = 4;
        [Tooltip("Тарелка без FillTuner: радиус по габариту × это")]
        [Range(0.2f, 1f)][SerializeField] private float _plateRadiusK = 0.55f;

        private enum Kind { None, StripsRaw, StripsFried, BeefOnion, Sauce, Curdled, OnionRaw }

        private Cookware _cw;
        private CookwareFillTuner _tuner;
        private Transform _root;
        private Mesh _mesh;
        private MeshRenderer _mr;
        private int _key = -1;
        private float _at;
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Color> _c = new List<Color>();
        private readonly List<int> _t = new List<int>();

        private void Awake()
        {
            _cw = GetComponent<Cookware>();
            _tuner = GetComponent<CookwareFillTuner>();
        }

        /// <summary>Какие продукты рисует декор (для меню: убрать их из обычной горки).</summary>
        public static bool Handles(ProductDef d, FoodState s) => KindOf(d, s) != Kind.None;

        private static Kind KindOf(ProductDef d, FoodState s)
        {
            if (d == null || string.IsNullOrEmpty(d.Title)) return Kind.None;
            string t = d.Title.Trim().ToLowerInvariant();
            if (t == "говядина соломкой") return s == FoodState.Fried ? Kind.StripsFried : Kind.StripsRaw;
            if (t == "говядина обжаренная") return Kind.StripsFried;
            if (t == "мясо с луком") return Kind.BeefOnion;
            if (t == "бефстроганов") return Kind.Sauce;
            if (t.StartsWith("бефстроганов (")) return Kind.Curdled;
            if (t == "лук репчатый" && (s == FoodState.Grated || s == FoodState.Pieces)) return Kind.OnionRaw;
            return Kind.None;
        }

        private void LateUpdate()
        {
            if (_cw == null || Time.time < _at) return;
            _at = Time.time + 0.2f;
            FoodDatabase db = FoodDatabase.Instance;
            if (db == null) return;
            int raw = 0, fried = 0, mix = 0, sauce = 0, curd = 0, onion = 0, total = 0;
            for (int i = 0; i < _cw.ViewCount; i++)
            {
                BagView v = _cw.GetView(i);
                if (v.Count <= 0) continue;
                total += v.Count;
                switch (KindOf(db.Get(v.Product), v.State))
                {
                    case Kind.StripsRaw: raw += v.Count; break;
                    case Kind.StripsFried: fried += v.Count; break;
                    case Kind.BeefOnion: mix += v.Count; break;
                    case Kind.Sauce: sauce += v.Count; break;
                    case Kind.Curdled: curd += v.Count; break;
                    case Kind.OnionRaw: onion += v.Count; break;
                }
            }
            int key = raw | (fried << 6) | (mix << 12) | (sauce << 18) | (curd << 24) ^ (onion * 7919) ^ (total * 31);
            if (key == _key) return;
            _key = key;
            Build(raw, fried, mix, sauce, curd, onion, total);
        }

        // ───────────────────────── построение ─────────────────────────

        private void Geometry(out Vector3 bottom, out float rBottom, out float rTop, out float height)
        {
            if (_tuner != null)
            {
                float s = Mathf.Max(1e-4f, Mathf.Abs(transform.lossyScale.x));
                bottom = transform.InverseTransformPoint(_tuner.HeapCenterWorld);
                rBottom = _tuner.HeapBottomR / s; rTop = _tuner.HeapTopR / s; height = _tuner.HeapHeight / s;
                return;
            }
            // тарелка: по габариту видимых мешей (локально)
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || (mf.gameObject.hideFlags & HideFlags.DontSave) != 0 || mf.transform == _root) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 p = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                    Vector3 lp = transform.InverseTransformPoint(mf.transform.TransformPoint(p));
                    if (!any) { b = new Bounds(lp, Vector3.zero); any = true; } else b.Encapsulate(lp);
                }
            }
            if (!any) b = new Bounds(Vector3.up * 0.02f, new Vector3(0.3f, 0.04f, 0.3f));
            float r = Mathf.Min(b.extents.x, b.extents.z) * _plateRadiusK;
            bottom = new Vector3(b.center.x, b.min.y + b.size.y * 0.35f, b.center.z);
            rBottom = r; rTop = r * 1.15f; height = Mathf.Max(b.size.y * 0.8f, r * 0.25f);
        }

        private void Build(int raw, int fried, int mix, int sauce, int curd, int onion, int total)
        {
            int all = raw + fried + mix + sauce + curd + onion;
            if (all <= 0) { if (_root != null) _root.gameObject.SetActive(false); return; }
            if (_root == null)
            {
                var go = new GameObject("FoodDecor (auto)") { hideFlags = HideFlags.DontSave };
                go.layer = gameObject.layer;
                _root = go.transform; _root.SetParent(transform, false);
                _mesh = new Mesh { name = "FoodDecor", hideFlags = HideFlags.DontSave };
                _mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _mr = go.AddComponent<MeshRenderer>();
                _mr.sharedMaterial = _material != null ? _material : FallbackMat();
            }
            _root.gameObject.SetActive(true);
            Geometry(out Vector3 bottom, out float rB, out float rT, out float H);
            _v.Clear(); _c.Clear(); _t.Clear();
            var rnd = new System.Random(GetInstanceID() * 31 + all);
            float size = rB * _pieceSize;
            int cap = _cw.Def != null ? Mathf.Max(1, _cw.Def.Capacity) : 20;
            float fill = Mathf.Clamp01(total / (float)cap);
            bool wet = sauce + curd > 0;
            float level = H * Mathf.Lerp(0.18f, 0.85f, fill);

            // соус — гладкий слой по уровню, кусочки торчат из него
            if (wet)
            {
                Color sc = curd > sauce ? new Color(0.78f, 0.70f, 0.62f) : new Color(0.86f, 0.58f, 0.44f);
                SauceDisc(bottom, Mathf.Lerp(rB, rT, level / Mathf.Max(1e-4f, H)) * 0.985f, level, sc, rnd);
                if (curd > 0)
                    for (int i = 0; i < Mathf.Min(80, curd * 4); i++)
                    {
                        Vector3 p = Scatter(bottom, rB * 0.9f, rnd); p.y = bottom.y + level + 0.002f;
                        Fleck(p, size * 0.12f, new Color(0.98f, 0.97f, 0.93f), rnd);
                    }
            }

            int strips = Mathf.Min(220, raw * _stripsPerPortion + fried * _stripsPerPortion + Mathf.CeilToInt((mix + sauce + curd) * _stripsPerPortion * 0.8f));
            int rings = Mathf.Min(160, onion * _ringsPerPortion + Mathf.CeilToInt((mix + sauce + curd) * _ringsPerPortion * 0.5f));
            int friedShare = fried + mix + sauce + curd;
            float pieceArea = size * size * 0.35f;
            int perLayer = Mathf.Max(4, Mathf.FloorToInt(Mathf.PI * rB * rB * 0.8f / pieceArea));
            int idx = 0;
            for (int i = 0; i < strips; i++, idx++)
            {
                bool isFried = rnd.NextDouble() < friedShare / (float)Mathf.Max(1, friedShare + raw);
                Vector3 p = Place(bottom, rB, rT, H, size, idx, perLayer, wet, level, rnd);
                Strip(p, size, isFried, wet, rnd);
            }
            for (int i = 0; i < rings; i++, idx++)
            {
                bool golden = onion == 0 || rnd.NextDouble() < (mix + sauce + curd) / (float)Mathf.Max(1, mix + sauce + curd + onion);
                Vector3 p = Place(bottom, rB, rT, H, size, idx, perLayer, wet, level, rnd);
                Ring(p, size * 0.55f, golden, wet, rnd);
            }

            _mesh.Clear();
            if (_v.Count > 65000) _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _mesh.SetVertices(_v); _mesh.SetColors(_c); _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateNormals(); _mesh.RecalculateBounds();
        }

        private static Vector3 Scatter(Vector3 c, float r, System.Random rnd)
        {
            float a = (float)rnd.NextDouble() * Mathf.PI * 2f, d = Mathf.Sqrt((float)rnd.NextDouble()) * r;
            return c + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
        }

        /// <summary>Где лежит кусочек: сухое — слоями от дна (горкой к центру); в соусе — вокруг уровня соуса.</summary>
        private static Vector3 Place(Vector3 bottom, float rB, float rT, float H, float size, int idx, int perLayer, bool wet, float level, System.Random rnd)
        {
            int layer = idx / perLayer;
            float y = wet ? level + ((float)rnd.NextDouble() - 0.4f) * size * 0.35f : size * 0.12f + layer * size * 0.2f;
            y = Mathf.Clamp(y, size * 0.08f, H * 1.25f);
            float rr = Mathf.Lerp(rB, rT, Mathf.Clamp01(y / Mathf.Max(1e-4f, H))) * 0.9f - size * 0.3f;
            if (!wet) rr *= Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(layer / 4f));      // выше — ближе к центру (горка)
            Vector3 p = Scatter(bottom, Mathf.Max(size * 0.2f, rr), rnd);
            p.y = bottom.y + y;
            return p;
        }

        /// <summary>Брусочек мяса: вытянутый, чуть скошенный; жареный — румяный верх и края, светлее внутри.</summary>
        private void Strip(Vector3 p, float size, bool fried, bool wet, System.Random rnd)
        {
            float l = size * (0.8f + 0.4f * (float)rnd.NextDouble()), w = size * 0.3f, h = size * 0.22f;
            Quaternion q = Quaternion.Euler(((float)rnd.NextDouble() - 0.5f) * 30f, (float)rnd.NextDouble() * 360f, ((float)rnd.NextDouble() - 0.5f) * 20f);
            float v = 0.88f + 0.24f * (float)rnd.NextDouble();
            Color top, side;
            if (!fried) { top = new Color(0.78f, 0.22f, 0.22f) * v; side = new Color(0.86f, 0.40f, 0.40f) * v; }
            else { top = new Color(0.42f, 0.22f, 0.12f) * v; side = new Color(0.58f, 0.34f, 0.22f) * v; }
            if (wet) { top = Color.Lerp(top, new Color(0.80f, 0.52f, 0.40f), 0.25f); side = Color.Lerp(side, new Color(0.84f, 0.56f, 0.44f), 0.3f); }
            Box(p, q, new Vector3(l, h, w), top, side);
            // прожилка у сырого — светлая полоска сверху
            if (!fried && rnd.NextDouble() < 0.5)
                Box(p + q * new Vector3(((float)rnd.NextDouble() - 0.5f) * l * 0.5f, h * 0.51f, 0f), q, new Vector3(l * 0.12f, h * 0.02f, w * 1.01f), new Color(0.95f, 0.85f, 0.80f), new Color(0.95f, 0.85f, 0.80f));
        }

        /// <summary>Полукольцо лука: дуга 180°, сечение-ромбик; сырой — белый с зеленцой, жареный — золотистый с подпалинами.</summary>
        private void Ring(Vector3 p, float r, bool golden, bool wet, System.Random rnd)
        {
            const int Seg = 8;
            float th = r * 0.2f;
            Quaternion q = Quaternion.Euler(((float)rnd.NextDouble() - 0.5f) * 20f, (float)rnd.NextDouble() * 360f, 0f);
            float rr = r * (0.75f + 0.5f * (float)rnd.NextDouble());
            Color a = golden ? new Color(0.86f, 0.62f, 0.26f) : new Color(0.96f, 0.95f, 0.86f);
            Color b = golden ? new Color(0.62f, 0.38f, 0.14f) : new Color(0.86f, 0.90f, 0.74f);
            if (wet) { a = Color.Lerp(a, new Color(0.86f, 0.60f, 0.46f), 0.3f); b = Color.Lerp(b, new Color(0.80f, 0.55f, 0.42f), 0.3f); }
            int s0 = _v.Count;
            for (int i = 0; i <= Seg; i++)
            {
                float ang = Mathf.PI * i / Seg;
                Vector3 c = new Vector3(Mathf.Cos(ang) * rr, 0f, Mathf.Sin(ang) * rr);
                Vector3 outD = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                Color col = Color.Lerp(a, b, Mathf.Abs(Mathf.Sin(ang * 3f + rr)) * 0.5f);
                _v.Add(p + q * (c + outD * th)); _c.Add(col);
                _v.Add(p + q * (c + Vector3.up * th * 0.6f)); _c.Add(col);
                _v.Add(p + q * (c - outD * th)); _c.Add(col * 0.95f);
                _v.Add(p + q * (c - Vector3.up * th * 0.6f)); _c.Add(col * 0.9f);
            }
            for (int i = 0; i < Seg; i++)
                for (int k = 0; k < 4; k++)
                {
                    int a0 = s0 + i * 4 + k, a1 = s0 + i * 4 + (k + 1) % 4, b0 = a0 + 4, b1 = a1 + 4;
                    Vector3 mid = (_v[a0] + _v[b1]) * 0.5f;
                    float ang = Mathf.PI * (i + 0.5f) / Seg;
                    Vector3 axis = p + q * new Vector3(Mathf.Cos(ang) * rr, 0f, Mathf.Sin(ang) * rr);
                    Tri(a0, b0, a1, mid - axis); Tri(a1, b0, b1, mid - axis);
                }
        }

        private void Fleck(Vector3 p, float s, Color col, System.Random rnd)
        {
            float a = (float)rnd.NextDouble() * 6.28f;
            Vector3 u = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * s, w = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * s * 0.7f;
            int i = _v.Count;
            _v.Add(p - u - w); _v.Add(p + u - w); _v.Add(p + u + w); _v.Add(p - u + w);
            for (int k = 0; k < 4; k++) _c.Add(col);
            Tri(i, i + 1, i + 2, Vector3.up); Tri(i, i + 2, i + 3, Vector3.up);
        }

        /// <summary>Соус: диск с мягкой волной и светлыми бликами-разводами (вершинный цвет).</summary>
        private void SauceDisc(Vector3 bottom, float r, float level, Color col, System.Random rnd)
        {
            const int Rings = 6, Seg = 36;
            int ctr = _v.Count;
            _v.Add(bottom + Vector3.up * level); _c.Add(col);
            float ph = (float)rnd.NextDouble() * 6.28f;
            for (int k = 1; k <= Rings; k++)
                for (int j = 0; j < Seg; j++)
                {
                    float a = j / (float)Seg * Mathf.PI * 2f, d = r * k / Rings;
                    float wave = Mathf.Sin(a * 3f + ph + k) * Mathf.Cos(k * 1.7f + a * 2f) * r * 0.012f;
                    _v.Add(bottom + new Vector3(Mathf.Cos(a) * d, level + wave, Mathf.Sin(a) * d));
                    float swirl = Mathf.Sin(a * 2f + k * 0.9f + ph) * 0.5f + 0.5f;
                    _c.Add(Color.Lerp(col, col * 1.12f, swirl * 0.6f));
                }
            for (int j = 0; j < Seg; j++) Tri(ctr, ctr + 1 + j, ctr + 1 + (j + 1) % Seg, Vector3.up);
            for (int k = 1; k < Rings; k++)
                for (int j = 0; j < Seg; j++)
                {
                    int a0 = ctr + 1 + (k - 1) * Seg + j, a1 = ctr + 1 + (k - 1) * Seg + (j + 1) % Seg;
                    int b0 = a0 + Seg, b1 = a1 + Seg;
                    Tri(a0, b0, a1, Vector3.up); Tri(a1, b0, b1, Vector3.up);
                }
        }

        private void Box(Vector3 c, Quaternion q, Vector3 size, Color top, Color side)
        {
            Vector3 h = size * 0.5f;
            for (int ax = 0; ax < 3; ax++)
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    Vector3 n = Vector3.zero; n[ax] = sg;
                    Vector3 u = Vector3.zero, w = Vector3.zero;
                    u[(ax + 1) % 3] = 1f; w[(ax + 2) % 3] = 1f;
                    Vector3 cc = Vector3.Scale(n, h), uu = Vector3.Scale(u, h), ww = Vector3.Scale(w, h);
                    Color col = ax == 1 ? (sg > 0 ? top : side * 0.85f) : side;
                    int i = _v.Count;
                    _v.Add(c + q * (cc - uu - ww)); _v.Add(c + q * (cc + uu - ww)); _v.Add(c + q * (cc + uu + ww)); _v.Add(c + q * (cc - uu + ww));
                    for (int k = 0; k < 4; k++) _c.Add(col);
                    Tri(i, i + 1, i + 2, q * n); Tri(i, i + 2, i + 3, q * n);
                }
        }

        private void Tri(int a, int b, int c, Vector3 nn)
        {
            Vector3 fn = Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]);
            if (Vector3.Dot(fn, nn) >= 0f) { _t.Add(a); _t.Add(b); _t.Add(c); } else { _t.Add(a); _t.Add(c); _t.Add(b); }
        }

        private static Material _fallback;
        private static Material FallbackMat()
        {
            if (_fallback != null) return _fallback;
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Particles/Standard Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            _fallback = new Material(sh) { name = "FoodDecor (auto)", hideFlags = HideFlags.DontSave };
            return _fallback;
        }

#if UNITY_EDITOR
        public void EditorSetMaterial(Material m) { _material = m; }
#endif

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
