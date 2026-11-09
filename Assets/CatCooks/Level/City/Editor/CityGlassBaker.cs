using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// РАЗМЕТКА СТЁКОЛ: каждый связный кусок стекла в меше — отдельное окно. В копию меша пишем:
    ///   UV3 = координаты внутри окна (0..1 по ширине вдоль стены и по высоте) — для бликов и тени рамы;
    ///   UV4 = (случайное окна, ширина, 1 = размечено, высота) — ширина/высота в единицах меша.
    /// Копии — ассеты Assets/Generated/City/GlassMeshes/…, связи — в CityGlassMeshes.asset.
    /// </summary>
    public static class CityGlassBaker
    {
        public const string RegistryPath = "Assets/Generated/City/CityGlassMeshes.asset";
        public const string MeshDir = "Assets/Generated/City/GlassMeshes";

        public static CityGlassMeshes Registry()
        {
            CityGlassMeshes r = AssetDatabase.LoadAssetAtPath<CityGlassMeshes>(RegistryPath);
            if (r != null) return r;
            Folder(MeshDir);
            r = ScriptableObject.CreateInstance<CityGlassMeshes>();
            AssetDatabase.CreateAsset(r, RegistryPath);
            return r;
        }

        private static void Folder(string folder)
        {
            string[] parts = folder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        public static bool IsGlassRenderer(Renderer r)
        {
            string nm = r.gameObject.name.ToLowerInvariant();
            if (nm.StartsWith("glass") || nm.Contains("стекл") || nm.Contains("window")) return true;
            foreach (Material m in r.sharedMaterials) if (CityBlockGenerator.IsGlassMat(m)) return true;
            return false;
        }

        /// <summary>Есть ли у модели стёкла (тогда импорт делаем читаемым перед разметкой).</summary>
        public static bool HasGlass(GameObject model)
        {
            foreach (MeshRenderer r in model.GetComponentsInChildren<MeshRenderer>(true)) if (IsGlassRenderer(r)) return true;
            return false;
        }

        /// <summary>Разметить все стёкла модели. Возвращает число размеченных мешей.</summary>
        public static int BakeModel(GameObject model, CityGlassMeshes reg)
        {
            int n = 0;
            foreach (MeshRenderer r in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!IsGlassRenderer(r)) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (reg.IsBaked(mf.sharedMesh)) continue;
                Vector3 up = mf.transform.InverseTransformDirection(Vector3.up).normalized;   // «вверх» в осях меша
                // стены этой модели — чтобы порезать «стекло за стеной» по проёмам (дома из middle)
                List<CityGlassOpenings.Wall> walls = new List<CityGlassOpenings.Wall>();
                foreach (MeshFilter wf in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (wf == mf || wf.sharedMesh == null || !wf.sharedMesh.isReadable) continue;
                    MeshRenderer wr = wf.GetComponent<MeshRenderer>();
                    if (wr != null && IsGlassRenderer(wr)) continue;
                    CityGlassOpenings.Wall w = new CityGlassOpenings.Wall();
                    w.V = wf.sharedMesh.vertices; w.T = wf.sharedMesh.triangles;
                    w.ToGlass = mf.transform.worldToLocalMatrix * wf.transform.localToWorldMatrix;
                    walls.Add(w);
                }
                Mesh baked = Bake(mf.sharedMesh, up, model.name, walls);
                if (baked == null) continue;
                reg.Set(mf.sharedMesh, baked);
                n++;
            }
            if (n > 0) { EditorUtility.SetDirty(reg); AssetDatabase.SaveAssets(); }
            return n;
        }

        private static int Find(int[] p, int x)
        {
            while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; }
            return x;
        }

        public static Mesh Bake(Mesh src, Vector3 up, string owner) { return Bake(src, up, owner, null); }

        public static Mesh Bake(Mesh src, Vector3 up, string owner, List<CityGlassOpenings.Wall> walls)
        {
            if (src.vertices == null || src.vertices.Length == 0) { Debug.LogWarning($"[Застройка/стёкла] «{owner}/{src.name}»: меш не читается — включи Read/Write в импорте"); return null; }
            // имя файла — по GUID исходного FBX: у моделей-копий (1layer/middle и 2layer) одинаковые имена,
            // раньше они перезаписывали друг другу размеченный меш — часть домов оставалась без разметки («кубики»)
            string srcPath = AssetDatabase.GetAssetPath(src);
            string tag = string.IsNullOrEmpty(srcPath) ? "" : "_" + AssetDatabase.AssetPathToGUID(srcPath).Substring(0, 8);
            string srcName = src.name;
            int cutPlanes, cutWins;
            Mesh cut = CityGlassOpenings.Split(src, up, walls, out cutPlanes, out cutWins);
            if (cut != null)
            {
                Debug.Log($"[Застройка/стёкла] «{owner}/{src.name}»: стекло за стеной — порезано по проёмам: листов {cutPlanes}, окон {cutWins}");
                src = cut;
            }
            Vector3[] v = src.vertices;
            if (v == null || v.Length == 0) { Debug.LogWarning($"[Застройка/стёкла] «{owner}/{src.name}»: меш не читается — включи Read/Write в импорте"); return null; }
            Vector3[] nrm = src.normals;
            int vc = v.Length;

            // склеиваем вершины по позиции (у Blender-моделей швы нормалей/UV режут вершины)
            float size = Mathf.Max(src.bounds.size.magnitude, 1e-3f);
            float q = size * 1e-5f;
            Dictionary<Vector3Int, int> weld = new Dictionary<Vector3Int, int>();
            int[] wid = new int[vc];
            for (int i = 0; i < vc; i++)
            {
                Vector3Int k = new Vector3Int(Mathf.RoundToInt(v[i].x / q), Mathf.RoundToInt(v[i].y / q), Mathf.RoundToInt(v[i].z / q));
                int id;
                if (!weld.TryGetValue(k, out id)) { id = weld.Count; weld[k] = id; }
                wid[i] = id;
            }
            int[] parent = new int[weld.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                int[] t = src.GetTriangles(s);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    int a = Find(parent, wid[t[i]]), b = Find(parent, wid[t[i + 1]]), c = Find(parent, wid[t[i + 2]]);
                    if (a != b) parent[b] = a;
                    a = Find(parent, a);
                    if (a != c) parent[Find(parent, c)] = a;
                }
            }

            // острова: средняя нормаль, оси стены, габарит
            Dictionary<int, int> islandOf = new Dictionary<int, int>();
            List<Vector3> nSum = new List<Vector3>();
            int[] isl = new int[vc];
            for (int i = 0; i < vc; i++)
            {
                int root = Find(parent, wid[i]);
                int k;
                if (!islandOf.TryGetValue(root, out k)) { k = nSum.Count; islandOf[root] = k; nSum.Add(Vector3.zero); }
                isl[i] = k;
                if (nrm != null && nrm.Length == vc) nSum[k] += nrm[i];
            }
            int ni = nSum.Count;
            Vector3[] tAx = new Vector3[ni];
            float[] minA = new float[ni], maxA = new float[ni], minB = new float[ni], maxB = new float[ni];
            Vector3[] center = new Vector3[ni];
            int[] cnt = new int[ni];
            for (int k = 0; k < ni; k++)
            {
                Vector3 n = nSum[k];
                Vector3 nh = n - up * Vector3.Dot(n, up);
                if (nh.sqrMagnitude < 1e-6f) nh = Vector3.Cross(up, Mathf.Abs(up.x) < 0.9f ? Vector3.right : Vector3.forward);
                nh.Normalize();
                tAx[k] = Vector3.Cross(up, nh).normalized;
                minA[k] = minB[k] = float.MaxValue; maxA[k] = maxB[k] = float.MinValue;
            }
            for (int i = 0; i < vc; i++)
            {
                int k = isl[i];
                float a = Vector3.Dot(v[i], tAx[k]), b = Vector3.Dot(v[i], up);
                if (a < minA[k]) minA[k] = a; if (a > maxA[k]) maxA[k] = a;
                if (b < minB[k]) minB[k] = b; if (b > maxB[k]) maxB[k] = b;
                center[k] += v[i]; cnt[k]++;
            }
            // ОКНО = НЕСКОЛЬКО СТЁКОЛ (v3): куски стекла в одной плоскости, разделённые только тонким переплётом,
            // склеиваем в одно окно — ночью оно горит целиком, а не «половинкой» или клетками.
            int[] win = ClusterWindows(ni, tAx, nSum, up, center, cnt, minA, maxA, minB, maxB);
            Vector3[] wSum = new Vector3[ni];
            int[] wCnt = new int[ni];
            for (int k = 0; k < ni; k++) { wSum[win[k]] += center[k]; wCnt[win[k]] += cnt[k]; }
            int windows = 0;
            for (int k = 0; k < ni; k++) if (win[k] == k) windows++;

            List<Vector2> uv3 = new List<Vector2>(vc);
            List<Vector4> uv4 = new List<Vector4>(vc);
            float[] rnd = new float[ni];
            for (int k = 0; k < ni; k++)
            {
                int wk = win[k];
                Vector3 c = wSum[wk] / Mathf.Max(1, wCnt[wk]);
                int h = Mathf.RoundToInt(c.x * 73.1f) * 73856093 ^ Mathf.RoundToInt(c.y * 91.7f) * 19349663 ^ Mathf.RoundToInt(c.z * 57.3f) * 83492791;
                rnd[k] = 0.01f + 0.98f * ((h & 0x7fffffff) % 10007) / 10007f;      // > 0: шейдер отличает от «нет разметки»
            }
            // окно или сплошной стеклянный фасад (небоскрёб): по размеру куска относительно всего стекла меша.
            // 3 — окно (горит целиком), 2 — фасад (шейдер делит на окна сеткой). Раньше решал порог в метрах
            // (Big Island) — крупные окна домов middle попадали в «фасад» и горели кубиками.
            float meshH = 0f;
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < vc; i++) { float b = Vector3.Dot(v[i], up); if (b < lo) lo = b; if (b > hi) hi = b; }
                meshH = Mathf.Max(1e-5f, hi - lo);
            }
            float[] kind = new float[ni];
            for (int k = 0; k < ni; k++)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < vc; i++) { float a = Vector3.Dot(v[i], tAx[k]); if (a < lo) lo = a; if (a > hi) hi = a; }
                float meshW = Mathf.Max(1e-5f, hi - lo);
                bool facade = (maxA[k] - minA[k]) > 0.45f * meshW && (maxB[k] - minB[k]) > 0.3f * meshH;
                kind[k] = facade ? 2f : 3f;
            }
            for (int i = 0; i < vc; i++)
            {
                int k = isl[i];
                float w = Mathf.Max(1e-5f, maxA[k] - minA[k]), hgt = Mathf.Max(1e-5f, maxB[k] - minB[k]);
                float a = Vector3.Dot(v[i], tAx[k]), b = Vector3.Dot(v[i], up);
                uv3.Add(new Vector2((a - minA[k]) / w, (b - minB[k]) / hgt));
                uv4.Add(new Vector4(rnd[k], w, kind[k], hgt));
            }

            Mesh m = Object.Instantiate(src);
            m.name = src.name + "_win";
            m.SetUVs(2, uv3);
            m.SetUVs(3, uv4);
            m.name = srcName + "_win";
            string file = MeshDir + "/" + Safe(owner + "_" + srcName) + tag + "_win.asset";
            Folder(MeshDir);
            Mesh old = AssetDatabase.LoadAssetAtPath<Mesh>(file);
            if (old != null)
            {
                // перезаписываем на месте — GUID тот же, ссылки домов в сцене не рвутся
                EditorUtility.CopySerialized(m, old);
                old.name = m.name;
                EditorUtility.SetDirty(old);
                Object.DestroyImmediate(m);
                m = old;
            }
            else AssetDatabase.CreateAsset(m, file);
            Debug.Log($"[Застройка/стёкла] «{owner}/{srcName}»: кусков стекла {ni}, окон {windows} → {file}");
            return m;
        }

        /// <summary>
        /// Склейка кусков стекла в окна: одна ориентация, одна плоскость, прямоугольники почти касаются
        /// (зазор меньше ~45% меньшей стороны стекла — это переплёт, а не простенок). Окно не растёт больше
        /// 4 стёкол по любой оси — чтобы не склеить весь фасад цепочкой. Возвращает корень окна для каждого куска.
        /// </summary>
        private static int[] ClusterWindows(int ni, Vector3[] tAx, List<Vector3> nSum, Vector3 up, Vector3[] center, int[] cnt,
                                            float[] minA, float[] maxA, float[] minB, float[] maxB)
        {
            int[] p = new int[ni];
            float[] cA0 = new float[ni], cA1 = new float[ni], cB0 = new float[ni], cB1 = new float[ni], cMax = new float[ni];
            float[] d = new float[ni];
            for (int k = 0; k < ni; k++)
            {
                p[k] = k;
                cA0[k] = minA[k]; cA1[k] = maxA[k]; cB0[k] = minB[k]; cB1[k] = maxB[k];
                cMax[k] = Mathf.Max(maxA[k] - minA[k], maxB[k] - minB[k]);
                Vector3 nh = Vector3.Cross(tAx[k], up);
                d[k] = Vector3.Dot(center[k] / Mathf.Max(1, cnt[k]), nh);
            }
            if (ni > 3000) return p;   // огромный меш — без склейки
            for (int i = 0; i < ni; i++)
                for (int j = i + 1; j < ni; j++)
                {
                    if (Vector3.Dot(tAx[i], tAx[j]) < 0.95f) continue;
                    float mi = Mathf.Min(Mathf.Min(maxA[i] - minA[i], maxB[i] - minB[i]), Mathf.Min(maxA[j] - minA[j], maxB[j] - minB[j]));
                    float gap = Mathf.Max(mi * 0.45f, 1e-4f);
                    if (Mathf.Abs(d[i] - d[j]) > gap * 1.5f) continue;
                    if (minA[i] - gap > maxA[j] || minA[j] - gap > maxA[i]) continue;
                    if (minB[i] - gap > maxB[j] || minB[j] - gap > maxB[i]) continue;
                    int a = Find(p, i), b = Find(p, j);
                    if (a == b) continue;
                    float a0 = Mathf.Min(cA0[a], cA0[b]), a1 = Mathf.Max(cA1[a], cA1[b]);
                    float b0 = Mathf.Min(cB0[a], cB0[b]), b1 = Mathf.Max(cB1[a], cB1[b]);
                    float lim = 4f * Mathf.Max(cMax[a], cMax[b]);
                    if (a1 - a0 > lim || b1 - b0 > lim) continue;
                    p[b] = a;
                    cA0[a] = a0; cA1[a] = a1; cB0[a] = b0; cB1[a] = b1; cMax[a] = Mathf.Max(cMax[a], cMax[b]);
                }
            int[] r = new int[ni];
            for (int k = 0; k < ni; k++) r[k] = Find(p, k);
            return r;
        }

        private static string Safe(string s)
        {
            char[] bad = System.IO.Path.GetInvalidFileNameChars();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char ch in s)
            {
                bool ok = ch != ' ';
                foreach (char b in bad) if (b == ch) { ok = false; break; }
                sb.Append(ok ? ch : '_');
            }
            return sb.ToString();
        }
    }
}
