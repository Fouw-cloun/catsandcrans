using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// РАБОЧИЕ СТОРОНЫ ДОМОВ — какие стороны модели с окнами. Для каждой стороны (+Z, +X, −Z, −X) считаем:
    /// площадь стёкол (материал стекла), иначе — площадь «цвета окон» в атласе (ключ _WinKey), иначе — детальность
    /// (сколько треугольников на метр фасада: рамы, подоконники). Рабочие — те, где заметно больше остальных.
    /// Результат пишется в BuildingEntry.Sides; поставленные руками (SidesManual) не трогаем.
    /// </summary>
    public static class CityFacadeTool
    {
        private static readonly string[] SideNames = { "+Z", "+X", "−Z", "−X" };
        private static readonly Color DefaultKey = new Color(0.486f, 0.565f, 0.565f);

        public static void DetectAll(CityBlockGenerator g, bool force)
        {
            List<BuildingEntry> todo = new List<BuildingEntry>();
            foreach (CityLayer l in new[] { g.Layer1, g.Layer2 })
                foreach (BuildingEntry e in l.Buildings)
                    if (e != null && e.Prefab != null && !e.SidesManual && (force || e.Sides == FacadeSides.None)) todo.Add(e);
            if (todo.Count == 0) return;

            // модели без Read/Write — включим на время замера, потом вернём
            List<ModelImporter> restore = new List<ModelImporter>();
            List<string> paths = new List<string>();
            foreach (BuildingEntry e in todo)
            {
                string p = AssetDatabase.GetAssetPath(e.Prefab);
                if (paths.Contains(p)) continue;
                paths.Add(p);
                ModelImporter mi = AssetImporter.GetAtPath(p) as ModelImporter;
                if (mi != null && !mi.isReadable) { mi.isReadable = true; mi.SaveAndReimport(); restore.Add(mi); }
            }
            Undo.RecordObject(g, "Рабочие стороны");
            System.Text.StringBuilder rep = new System.Text.StringBuilder();
            Dictionary<Texture, Color32[]> cache = new Dictionary<Texture, Color32[]>();
            int found = 0;
            try
            {
                for (int i = 0; i < todo.Count; i++)
                {
                    BuildingEntry e = todo[i];
                    EditorUtility.DisplayProgressBar("Рабочие стороны домов", e.Prefab.name, (float)i / todo.Count);
                    GameObject fresh = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(e.Prefab));
                    string info;
                    FacadeSides s = Detect(fresh != null ? fresh : e.Prefab, e.Material, cache, out info);
                    e.Sides = s;
                    if (s != (FacadeSides.Front | FacadeSides.Right | FacadeSides.Back | FacadeSides.Left)) found++;
                    rep.AppendLine($"  {e.Prefab.name}: {Describe(s)}   [{info}]");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                foreach (ModelImporter mi in restore) { mi.isReadable = false; mi.SaveAndReimport(); }
            }
            EditorUtility.SetDirty(g);
            Debug.Log($"[Застройка/стороны] моделей {todo.Count}, с глухими сторонами {found}. Ошибся — поставь руками Sides и галку SidesManual у дома\n" + rep, g);
        }

        public static string Describe(FacadeSides s)
        {
            if (s == FacadeSides.None) return "не определены";
            if ((int)s == 15) return "окна со всех сторон";
            List<string> n = new List<string>();
            for (int i = 0; i < 4; i++) if (((int)s & (1 << i)) != 0) n.Add(SideNames[i]);
            return "окна: " + string.Join(", ", n.ToArray());
        }

        public static FacadeSides Detect(GameObject prefab, Material body, Dictionary<Texture, Color32[]> cache, out string info)
        {
            float[] glass = new float[4], win = new float[4], area = new float[4];
            int[] tris = new int[4];
            Bounds b;
            if (!TileGridGenerator.MeasurePrefab(prefab, out b)) { info = "нет габарита"; return FacadeSides.None; }
            Matrix4x4 toRoot = Matrix4x4.Translate(-prefab.transform.position);
            foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (m == null || mr == null || !m.isReadable) continue;
                Matrix4x4 M = toRoot * mf.transform.localToWorldMatrix;
                Vector3[] v = m.vertices;
                Vector3[] nr = m.normals;
                Vector2[] uv = m.uv;
                Material[] mats = mr.sharedMaterials;
                for (int sm = 0; sm < m.subMeshCount; sm++)
                {
                    Material mat = sm < mats.Length ? mats[sm] : null;
                    bool isGlass = mat != null && CityBlockGenerator.IsGlassMat(mat);
                    Color32[] px = null; int tw = 0; Color key = DefaultKey; float tol = 0.12f;
                    Material texMat = mat != null && mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") != null ? mat : body;
                    if (!isGlass && texMat != null && uv != null && uv.Length == v.Length)
                    {
                        Texture t = texMat.HasProperty("_BaseMap") ? texMat.GetTexture("_BaseMap") : (texMat.HasProperty("_MainTex") ? texMat.GetTexture("_MainTex") : null);
                        if (t != null) { px = Pixels(t, cache); tw = 256; }
                        if (texMat.HasProperty("_WinKey")) key = texMat.GetColor("_WinKey");
                        if (texMat.HasProperty("_WinTol")) tol = Mathf.Max(0.06f, texMat.GetFloat("_WinTol"));
                    }
                    int[] ix = m.GetTriangles(sm);
                    for (int k = 0; k + 2 < ix.Length; k += 3)
                    {
                        int i0 = ix[k], i1 = ix[k + 1], i2 = ix[k + 2];
                        Vector3 a = M.MultiplyPoint3x4(v[i0]), bb = M.MultiplyPoint3x4(v[i1]), cc = M.MultiplyPoint3x4(v[i2]);
                        Vector3 n = nr != null && nr.Length == v.Length ? M.MultiplyVector(nr[i0] + nr[i1] + nr[i2]) : Vector3.Cross(bb - a, cc - a);
                        if (n.sqrMagnitude < 1e-12f) continue;
                        n.Normalize();
                        if (Mathf.Abs(n.y) > 0.5f) continue;                        // крыши, полы, козырьки
                        int side = Mathf.Abs(n.z) >= Mathf.Abs(n.x) ? (n.z > 0 ? 0 : 2) : (n.x > 0 ? 1 : 3);
                        Vector3 ce = (a + bb + cc) / 3f;
                        // только у внешней стены этой стороны (не боковины ниш)
                        float depth = side == 0 ? b.max.z - ce.z : side == 2 ? ce.z - b.min.z : side == 1 ? b.max.x - ce.x : ce.x - b.min.x;
                        float span = (side % 2 == 0) ? b.size.z : b.size.x;
                        if (depth > span * 0.3f) continue;
                        float ar = Vector3.Cross(bb - a, cc - a).magnitude * 0.5f;
                        area[side] += ar; tris[side]++;
                        if (isGlass) { glass[side] += ar; continue; }
                        if (px != null)
                        {
                            Vector2 tuv = (uv[i0] + uv[i1] + uv[i2]) / 3f;
                            int tx = Mathf.Clamp((int)(Mathf.Repeat(tuv.x, 1f) * tw), 0, tw - 1), ty = Mathf.Clamp((int)(Mathf.Repeat(tuv.y, 1f) * tw), 0, tw - 1);
                            Color32 p = px[ty * tw + tx];
                            float dr = Mathf.LinearToGammaSpace(p.r / 255f) - key.r, dg = Mathf.LinearToGammaSpace(p.g / 255f) - key.g, db = Mathf.LinearToGammaSpace(p.b / 255f) - key.b;
                            if (Mathf.Sqrt(dr * dr + dg * dg + db * db) < tol) win[side] += ar;
                        }
                    }
                }
            }
            float[] width = { b.size.x, b.size.z, b.size.x, b.size.z };
            float[] score = new float[4];
            string how;
            float gMax = Mathf.Max(Mathf.Max(glass[0], glass[1]), Mathf.Max(glass[2], glass[3]));
            float wMax = Mathf.Max(Mathf.Max(win[0], win[1]), Mathf.Max(win[2], win[3]));
            float aMax = Mathf.Max(Mathf.Max(area[0], area[1]), Mathf.Max(area[2], area[3]));
            if (gMax > aMax * 0.03f) { how = "стёкла"; for (int i = 0; i < 4; i++) score[i] = glass[i] / Mathf.Max(width[i], 1e-3f); }
            else if (wMax > aMax * 0.03f) { how = "окна в атласе"; for (int i = 0; i < 4; i++) score[i] = win[i] / Mathf.Max(width[i], 1e-3f); }
            else { how = "детальность"; for (int i = 0; i < 4; i++) score[i] = tris[i] / Mathf.Max(width[i], 1e-3f); }
            float mx = Mathf.Max(Mathf.Max(score[0], score[1]), Mathf.Max(score[2], score[3]));
            float mn = Mathf.Min(Mathf.Min(score[0], score[1]), Mathf.Min(score[2], score[3]));
            int mask = 0;
            if (mx <= 0f) mask = 15;
            else if (how == "детальность" && mx < mn * 1.6f) mask = 15;          // по детальности разницы нет — считаем все рабочими
            else for (int i = 0; i < 4; i++) if (score[i] >= mx * 0.4f) mask |= 1 << i;
            info = how + ": " + string.Format("+Z {0:0.#} · +X {1:0.#} · −Z {2:0.#} · −X {3:0.#}", score[0], score[1], score[2], score[3]);
            return (FacadeSides)mask;
        }

        private static Color32[] Pixels(Texture t, Dictionary<Texture, Color32[]> cache)
        {
            Color32[] px;
            if (cache.TryGetValue(t, out px)) return px;
            const int N = 256;
            RenderTexture rt = RenderTexture.GetTemporary(N, N, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture was = RenderTexture.active;
            Graphics.Blit(t, rt);
            RenderTexture.active = rt;
            Texture2D tmp = new Texture2D(N, N, TextureFormat.RGBA32, false, true);
            tmp.ReadPixels(new Rect(0, 0, N, N), 0, 0);
            tmp.Apply();
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            px = tmp.GetPixels32();
            Object.DestroyImmediate(tmp);
            cache[t] = px;
            return px;
        }
    }
}
