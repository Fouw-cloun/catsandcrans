using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ОРИЕНТИР С ПОДСВЕТКОЙ (космическая башня и т. п.): верхние окна горят всегда (ниже — частично, как у небоскрёбов),
    /// сферы на модели тускло светятся своим цветом + мягкий ореол (ночью сильнее, днём едва-едва).
    /// Сферы ищутся в меше сами: отдельные куски, почти круглые, 1–15% высоты здания.
    /// Вешается на корень дома (B_...) — генератор ставит сам, если у дома в списке включено «Landmark Glow»;
    /// на готовые — меню «Космическая башня: подсветка». Модели нужен Read/Write (меню включает сам).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class CityLandmarkGlow : MonoBehaviour
    {
        public const string GlowName = "Landmark Glow (generated)";

        [Header("Окна")]
        [Tooltip("Верхние окна горят всегда (доля высоты сверху), ниже — частично")]
        public bool TopWindowsLit = true;
        [Range(0.05f, 1f)] public float TopShare = 0.35f;

        [Header("Сферы")]
        public bool SphereGlow = true;
        [Tooltip("Свечение поверхности сфер (своим цветом)")]
        [Range(0f, 3f)] public float Glow = 0.6f;
        [Tooltip("Мягкий ореол вокруг сфер")]
        [Range(0f, 2f)] public float Halo = 0.35f;
        [Tooltip("Днём (доля от ночного)")]
        [Range(0f, 1f)] public float DayGlow = 0.15f;
        [Tooltip("Размер сфер — доля высоты здания (от–до)")]
        public Vector2 SphereSize = new Vector2(0.01f, 0.15f);
        [Tooltip("Насколько круглые: соотношение сторон габарита не больше")]
        [Range(1f, 2f)] public float Roundness = 1.35f;
        public Material GlowMaterial;

        [Tooltip("Сколько сфер нашлось (только чтение)")]
        public int Spheres;

        private void OnEnable() { Apply(); }

        /// <summary>Пересобрать: окна (через CityOfficeTower) и свечение сфер.</summary>
        [ContextMenu("Пересобрать подсветку")]
        public void Apply()
        {
            // окна: верх весь + частично
            CityOfficeTower ot = GetComponent<CityOfficeTower>();
            if (TopWindowsLit)
            {
                if (ot == null) ot = gameObject.AddComponent<CityOfficeTower>();
                ot.Mode = CityOfficeTower.NightMode.TopAllPartial;
                ot.BandShare = TopShare;
                ot.TopInset = 0f;
                ot.Apply();
            }
            BuildSpheres();
        }

        private void BuildSpheres()
        {
            Transform old = transform.Find(GlowName);
            if (old != null) { if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject); }
            Spheres = 0;
            if (!SphereGlow) return;
            if (GlowMaterial == null)
            {
                Shader sh = Shader.Find("CatCooks/City/Landmark Glow");
                if (sh == null) return;
                GlowMaterial = new Material(sh) { name = "Landmark Glow (auto)", hideFlags = HideFlags.DontSave };
            }
            float height = BuildingHeight();
            if (height <= 0f) return;

            foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null || mf.name == GlowName || mf.transform.parent != null && mf.transform.parent.name == GlowName) continue;
                if (!mesh.isReadable && Application.isPlaying)
                {
                    Debug.LogWarning("[Ориентир] «" + mesh.name + "»: нет Read/Write — сферы не найти. Меню «Космическая башня: подсветка» включит его.", this);
                    continue;
                }
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                Texture tex = null;
                if (mr != null && mr.sharedMaterial != null)
                {
                    if (mr.sharedMaterial.HasProperty("_BaseMap")) tex = mr.sharedMaterial.GetTexture("_BaseMap");
                    if (tex == null && mr.sharedMaterial.HasProperty("_MainTex")) tex = mr.sharedMaterial.GetTexture("_MainTex");
                }
                Mesh glow = SphereMesh(mesh, mf.transform, height);
                if (glow == null) continue;
                GameObject g = new GameObject(GlowName);
                g.hideFlags = HideFlags.DontSave;
                g.transform.SetParent(mf.transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = glow;
                MeshRenderer gr = g.AddComponent<MeshRenderer>();
                gr.sharedMaterial = GlowMaterial;
                gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                gr.receiveShadows = false;
                MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                if (tex != null) mpb.SetTexture("_BaseMap", tex);
                mpb.SetFloat("_Glow", Glow);
                mpb.SetFloat("_Halo", Halo);
                mpb.SetFloat("_DayGlow", DayGlow);
                gr.SetPropertyBlock(mpb);
            }
            if (!Application.isPlaying)
                Debug.Log("[Ориентир] «" + name + "»: светящихся сфер найдено " + Spheres + (Spheres == 0 ? " — проверь Read/Write модели или Sphere Size / Roundness" : ""), this);
        }

        private float BuildingHeight()
        {
            Renderer[] rs = GetComponentsInChildren<Renderer>(true);
            bool any = false; Bounds b = new Bounds();
            foreach (Renderer r in rs)
            {
                if (r.transform.name == GlowName) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any ? b.size.y : 0f;
        }

        /// <summary>Отдельные куски меша, похожие на сферы → копия их треугольников (свечение) + квад-ореол на каждую.</summary>
        private Mesh SphereMesh(Mesh src, Transform t, float height)
        {
            Vector3[] v = src.vertices;
            Vector3[] nr = src.normals;
            Vector2[] uv = src.uv;
            int[] tri = src.triangles;
            if (v.Length == 0 || tri.Length == 0) return null;
            // слить вершины с одной позицией (швы UV) и объединить куски
            Dictionary<Vector3Int, int> weld = new Dictionary<Vector3Int, int>();
            int[] wid = new int[v.Length];
            float q = 1e4f;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3Int k = new Vector3Int(Mathf.RoundToInt(v[i].x * q), Mathf.RoundToInt(v[i].y * q), Mathf.RoundToInt(v[i].z * q));
                int id;
                if (!weld.TryGetValue(k, out id)) { id = weld.Count; weld[k] = id; }
                wid[i] = id;
            }
            int[] parent = new int[weld.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            for (int i = 0; i < tri.Length; i += 3)
            {
                Union(parent, wid[tri[i]], wid[tri[i + 1]]);
                Union(parent, wid[tri[i]], wid[tri[i + 2]]);
            }
            // габарит каждого куска в мире (масштаб модели), центр и средний UV
            Dictionary<int, int> compIndex = new Dictionary<int, int>();
            List<Vector3> mn = new List<Vector3>(), mx = new List<Vector3>();
            List<Vector2> uvSum = new List<Vector2>();
            List<int> cnt = new List<int>();
            int[] compOf = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                int root = Find(parent, wid[i]);
                int ci;
                if (!compIndex.TryGetValue(root, out ci)) { ci = mn.Count; compIndex[root] = ci; mn.Add(v[i]); mx.Add(v[i]); uvSum.Add(Vector2.zero); cnt.Add(0); }
                compOf[i] = ci;
                mn[ci] = Vector3.Min(mn[ci], v[i]); mx[ci] = Vector3.Max(mx[ci], v[i]);
                if (uv != null && uv.Length == v.Length) uvSum[ci] += uv[i];
                cnt[ci]++;
            }
            bool[] isSphere = new bool[mn.Count];
            Vector3 ls = t.lossyScale;
            for (int ci = 0; ci < mn.Count; ci++)
            {
                Vector3 sz = Vector3.Scale(mx[ci] - mn[ci], new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
                float a = Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z)), b = Mathf.Min(sz.x, Mathf.Min(sz.y, sz.z));
                isSphere[ci] = cnt[ci] >= 20 && b > 1e-5f && a / b <= Roundness && a >= SphereSize.x * height && a <= SphereSize.y * height;
            }
            // собрать меш: треугольники сфер (свечение, uv2.x = 0) + квад-ореол (uv2 = 1, радиус)
            List<Vector3> ov = new List<Vector3>(); List<Vector3> on = new List<Vector3>();
            List<Vector2> ouv = new List<Vector2>(), ouv1 = new List<Vector2>(), ouv2 = new List<Vector2>();
            List<int> ot = new List<int>();
            Dictionary<int, int> remap = new Dictionary<int, int>();
            for (int i = 0; i < tri.Length; i += 3)
            {
                int ci = compOf[tri[i]];
                if (!isSphere[ci]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int vi = tri[i + k];
                    int ni;
                    if (!remap.TryGetValue(vi, out ni))
                    {
                        ni = ov.Count; remap[vi] = ni;
                        Vector3 n = nr != null && nr.Length == v.Length ? nr[vi] : Vector3.up;
                        float r = (mx[ci] - mn[ci]).magnitude * 0.5f;
                        ov.Add(v[vi] + n * r * 0.02f);                          // чуть над поверхностью — без мерцания
                        on.Add(n);
                        ouv.Add(uv != null && uv.Length == v.Length ? uv[vi] : Vector2.zero);
                        ouv1.Add(Vector2.zero); ouv2.Add(Vector2.zero);
                    }
                    ot.Add(ni);
                }
            }
            int spheres = 0;
            for (int ci = 0; ci < mn.Count; ci++)
            {
                if (!isSphere[ci]) continue;
                spheres++;
                Vector3 c = (mn[ci] + mx[ci]) * 0.5f;
                float r = Mathf.Max(mx[ci].x - mn[ci].x, Mathf.Max(mx[ci].y - mn[ci].y, mx[ci].z - mn[ci].z)) * 0.5f;
                Vector2 au = cnt[ci] > 0 ? uvSum[ci] / cnt[ci] : Vector2.zero;
                int b0 = ov.Count;
                Vector2[] k4 = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
                for (int k = 0; k < 4; k++)
                {
                    ov.Add(c); on.Add(Vector3.up); ouv.Add(au); ouv1.Add(k4[k]); ouv2.Add(new Vector2(1f, r * 2.4f));
                }
                ot.Add(b0); ot.Add(b0 + 1); ot.Add(b0 + 2); ot.Add(b0); ot.Add(b0 + 2); ot.Add(b0 + 3);
            }
            if (spheres == 0) return null;
            Spheres += spheres;
            Mesh m = new Mesh();
            m.name = src.name + " (сферы)";
            m.hideFlags = HideFlags.DontSave;
            if (ov.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(ov); m.SetNormals(on);
            m.SetUVs(0, ouv); m.SetUVs(1, ouv1); m.SetUVs(2, ouv2);
            m.SetTriangles(ot, 0);
            m.RecalculateBounds();
            Bounds bb = m.bounds; bb.Expand(height * 0.1f); m.bounds = bb;
            return m;
        }

        private static int Find(int[] p, int a) { while (p[a] != a) { p[a] = p[p[a]]; a = p[a]; } return a; }
        private static void Union(int[] p, int a, int b) { a = Find(p, a); b = Find(p, b); if (a != b) p[b] = a; }
    }
}
