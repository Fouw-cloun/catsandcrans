using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// АТЛАСЫ ДЛЯ МОДЕЛЕЙ — белые дома (FBX не нашёл свои текстуры) получают атлас из своей папки.
    ///
    /// Где ищем, для каждой модели:
    ///   • материалы (.mat) и текстуры в папке слоя (со всеми подпапками) и в папке самой модели и выше;
    ///   • текстуры группируются в наборы по имени: «little buildings_DefaultMaterial_AlbedoTransparency» +
    ///     «…_MetallicSmoothness» + «…_Normal» — один набор; для набора без .mat создаётся материал
    ///     «<набор> (atlas).mat» рядом с текстурами (URP Lit, если есть, иначе Standard).
    /// Как выбираем: по словам в путях и именах — «little» к little, «middle» к middle, папка skyscrapers к текстурам
    ///   из skyscrapers; рядом лежащий (та же папка) — плюс. Ничего не совпало — общий атлас (plane / atlas / main),
    ///   иначе единственный.
    /// Как применяем: в импорте FBX (Remap материалов, как вкладка Materials → On Demand Remap) — модель становится
    ///   цветной везде: в Project, в сцене, в уже расставленных домах. Перекрашиваются только БЕЛЫЕ материалы
    ///   (без текстуры), свои рабочие материалы модели не трогаем.
    /// </summary>
    public static class CityAtlasTool
    {
        private sealed class Cand
        {
            public Material Mat;           // готовый материал (или созданный)
            public string Dir;             // папка
            public string Key;             // слова для сравнения
            public string Base, MS, Normal, Metal; // для набора текстур без материала
            public string Name;
        }

        private static readonly string[] Generic =
        {
            "tex", "texture", "textures", "blend", "defaultmaterial", "default", "material", "materials", "new", "basecolor", "base",
            "color", "albedo", "albedotransparency", "transparency", "metallic", "metallicsmoothness", "smoothness", "roughness",
            "normal", "height", "ao", "png", "jpg", "tga", "mat", "fbx", "assets", "models", "model", "atlas", "the", "and", "near", "radius"
        };

        public const string GlassPath = "Assets/Generated/City/CC_WindowGlass.mat";

        /// <summary>Подобрать и применить атласы моделям слоя + стекло. Возвращает, скольким моделям применено.</summary>
        public static int Apply(CityBlockGenerator g, CityLayer l, bool log)
        {
            List<GameObject> models = new List<GameObject>();
            foreach (BuildingEntry e in l.Buildings) if (e != null && e.Prefab != null && !models.Contains(e.Prefab)) models.Add(e.Prefab);
            foreach (GameObject o in l.RoofProps) if (o != null && !models.Contains(o)) models.Add(o);
            foreach (GameObject o in l.StreetProps) if (o != null && !models.Contains(o)) models.Add(o);
            foreach (GameObject o in l.Cars) if (o != null && !models.Contains(o)) models.Add(o);
            string root = l.SourceFolder != null ? AssetDatabase.GetAssetPath(l.SourceFolder) : null;
            if (g.GlassMaterial == null) { Undo.RecordObject(g, "Стекло"); g.GlassMaterial = EnsureGlass(); }
            if (g.GlassMeshes == null) { Undo.RecordObject(g, "Стекло"); g.GlassMeshes = CityGlassBaker.Registry(); }
            Dictionary<GameObject, Material> chosen;
            int n = Apply(models, root, l.Name, log, g.GlassMaterial, l.UseCityShader, l.AtlasWindows, out chosen);
            // разметка стёкол: каждый кусок стекла — своё окно
            int bakedN = 0;
            foreach (GameObject m in models)
            {
                string mp = AssetDatabase.GetAssetPath(m);
                GameObject fresh = AssetDatabase.LoadAssetAtPath<GameObject>(mp);
                if (fresh != null && CityGlassBaker.HasGlass(fresh)) bakedN += CityGlassBaker.BakeModel(fresh, g.GlassMeshes);
            }
            if (log && bakedN > 0) Debug.Log($"[Застройка/стёкла] «{l.Name}»: размечено стёкол {bakedN}");
            // материал стен — в записи дома: генератор поставит его на части без материала (у некоторых FBX их нет)
            Undo.RecordObject(g, "Атласы");
            foreach (BuildingEntry e in l.Buildings)
            {
                Material m;
                if (e != null && e.Prefab != null && chosen.TryGetValue(e.Prefab, out m)) e.Material = m;
            }
            EditorUtility.SetDirty(g);
            // уже расставленные дома — стёкла и стены без материала поправить сразу
            Transform c = g.Container();
            if (c != null)
            {
                int fixedN = 0;
                foreach (BuildingEntry e in l.Buildings)
                {
                    if (e == null || e.Prefab == null) continue;
                    foreach (Transform t in c.GetComponentsInChildren<Transform>(true))
                    {
                        if (!t.name.StartsWith("B_")) continue;
                        if (t.childCount == 0) continue;
                        GameObject inst = t.GetChild(0).gameObject;
                        if (PrefabUtility.GetCorrespondingObjectFromSource(inst) != e.Prefab) continue;
                        // сбросить старые подмены материалов/мешей (v1.5 ошибочно красил стены небоскрёбов стеклом)
                        foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
                            if (PrefabUtility.IsPartOfPrefabInstance(r)) PrefabUtility.RevertObjectOverride(r, InteractionMode.AutomatedAction);
                        foreach (MeshFilter mf in inst.GetComponentsInChildren<MeshFilter>(true))
                            if (PrefabUtility.IsPartOfPrefabInstance(mf)) PrefabUtility.RevertObjectOverride(mf, InteractionMode.AutomatedAction);
                        CityBlockGenerator.ApplyMaterials(inst, e.Material, g.GlassMaterial, g.GlassMeshes);
                        fixedN++;
                    }
                }
                if (log && fixedN > 0) Debug.Log($"[Застройка/атласы] «{l.Name}»: обновлено расставленных домов — {fixedN}");
            }
            return n;
        }

        /// <summary>
        /// ПОЧИНИТЬ ОКНА — для всех домов обоих слоёв: стёкла FBX (материал …UV_GRID / glass, части glass…) → материал
        /// стекла в импорте; разметка стёкол; у «(city win)» — цвет стекла из атласа подбирается сам; все расставленные
        /// дома: сброс старых подмен и заново стекло/стены. В консоль — по каждой модели, сколько стёкол.
        /// </summary>
        public static void FixAll(CityBlockGenerator g)
        {
            Undo.RecordObject(g, "Окна");
            if (g.GlassMaterial == null) g.GlassMaterial = EnsureGlass();
            if (g.GlassMeshes == null) g.GlassMeshes = CityGlassBaker.Registry();
            Material glass = g.GlassMaterial;
            if (glass == null) return;
            List<BuildingEntry> all = new List<BuildingEntry>();
            all.AddRange(g.Layer1.Buildings); all.AddRange(g.Layer2.Buildings);
            List<GameObject> models = new List<GameObject>();
            foreach (BuildingEntry e in all) if (e != null && e.Prefab != null && !models.Contains(e.Prefab)) models.Add(e.Prefab);

            System.Text.StringBuilder rep = new System.Text.StringBuilder();
            int fixedModels = 0;
            foreach (GameObject m in models)
            {
                string path = AssetDatabase.GetAssetPath(m);
                ModelImporter mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) continue;
                List<string> names = new List<string>();
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path)) { Material em = o as Material; if (em != null && !names.Contains(em.name)) names.Add(em.name); }
                foreach (KeyValuePair<AssetImporter.SourceAssetIdentifier, Object> kv in mi.GetExternalObjectMap())
                    if (kv.Key.type == typeof(Material) && !names.Contains(kv.Key.name)) names.Add(kv.Key.name);
                bool changed = false;
                Dictionary<AssetImporter.SourceAssetIdentifier, Object> ext = mi.GetExternalObjectMap();
                foreach (string nm in names)
                {
                    if (!IsGlassName(nm)) continue;
                    AssetImporter.SourceAssetIdentifier id = new AssetImporter.SourceAssetIdentifier(typeof(Material), nm);
                    Object cur;
                    if (ext.TryGetValue(id, out cur) && cur == glass) continue;
                    mi.AddRemap(id, glass); changed = true;
                }
                bool ownGlass = CityGlassBaker.HasGlass(m);
                if (ownGlass && !mi.isReadable) { mi.isReadable = true; changed = true; }
                // у модели свои стёкла → атлас «(city win)» с ней нельзя: он красит серые стены как окна (дом «весь в стекле»)
                int unwin = 0;
                if (ownGlass)
                {
                    List<KeyValuePair<AssetImporter.SourceAssetIdentifier, Object>> pairs = new List<KeyValuePair<AssetImporter.SourceAssetIdentifier, Object>>(mi.GetExternalObjectMap());
                    foreach (KeyValuePair<AssetImporter.SourceAssetIdentifier, Object> kv in pairs)
                    {
                        Material wm = kv.Value as Material;
                        if (kv.Key.type != typeof(Material) || !IsWinMat(wm)) continue;
                        Material nw = NoWin(wm);
                        if (nw == null) continue;
                        mi.AddRemap(kv.Key, nw); changed = true; unwin++;
                    }
                    foreach (BuildingEntry e in all)
                        if (e != null && e.Prefab == m && IsWinMat(e.Material)) { e.Material = NoWin(e.Material); unwin++; }
                }
                if (changed) { EditorUtility.SetDirty(mi); mi.SaveAndReimport(); fixedModels++; }
                GameObject fresh = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                int gl = 0;
                List<string> parts = new List<string>();
                foreach (MeshRenderer r in fresh.GetComponentsInChildren<MeshRenderer>(true))
                    if (CityGlassBaker.IsGlassRenderer(r)) { gl++; parts.Add(r.name); }
                CityGlassBaker.BakeModel(fresh, g.GlassMeshes);
                rep.AppendLine(gl > 0 ? $"  {m.name}: стёкол {gl} ({string.Join(", ", parts.ToArray())})" + (changed ? " — исправлено" : "") + (unwin > 0 ? $", атлас без «окон в атласе» ({unwin})" : "")
                                      : $"  {m.name}: отдельных стёкол нет — окна в атласе (галка «Окна нарисованы в атласе» у слоя)");
            }
            // цвет стекла для «(city win)» — из самого атласа
            List<Material> wins = new List<Material>();
            foreach (BuildingEntry e in all)
            {
                if (e == null || e.Prefab == null || CityGlassBaker.HasGlass(e.Prefab)) continue;   // у кого свои стёкла — ключ не нужен
                List<Material> ms = new List<Material>();
                if (e.Material != null) ms.Add(e.Material);
                foreach (Renderer r in AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(e.Prefab)).GetComponentsInChildren<Renderer>(true)) ms.AddRange(r.sharedMaterials);
                foreach (Material mm in ms)
                    if (mm != null && mm.shader != null && mm.shader.name == "CatCooks/City/Building" && mm.IsKeywordEnabled("_ATLAS_WINDOWS") && !wins.Contains(mm)) wins.Add(mm);
            }
            foreach (Material wm in wins) AutoWinKey(wm, rep);
            // расставленные дома
            int inst = 0;
            Transform c = g.Container();
            if (c != null)
            {
                foreach (Transform t in c.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.StartsWith("B_") || t.childCount == 0) continue;
                    for (int ci = 0; ci < t.childCount; ci++)      // все этажи «башенки» из мелких, не только первый
                    {
                    GameObject go = t.GetChild(ci).gameObject;
                    GameObject src = PrefabUtility.GetCorrespondingObjectFromSource(go);
                    BuildingEntry be = null;
                    foreach (BuildingEntry e in all) if (e != null && e.Prefab == src) { be = e; break; }
                    if (src == null) continue;
                    foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                        if (PrefabUtility.IsPartOfPrefabInstance(r)) PrefabUtility.RevertObjectOverride(r, InteractionMode.AutomatedAction);
                    foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
                        if (PrefabUtility.IsPartOfPrefabInstance(mf)) PrefabUtility.RevertObjectOverride(mf, InteractionMode.AutomatedAction);
                    CityBlockGenerator.ApplyMaterials(go, be != null ? be.Material : null, glass, g.GlassMeshes);
                    inst++;
                    }
                }
            }
            // проверка: стёкла в сцене без разметки (горели бы «кубиками») — доразметить прямо с экземпляра
            int fixedNow = 0;
            List<string> bad = new List<string>();
            if (c != null)
            {
                foreach (MeshRenderer r in c.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!CityGlassBaker.IsGlassRenderer(r)) continue;
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null || g.GlassMeshes.IsBaked(mf.sharedMesh)) continue;
                    Mesh bm = g.GlassMeshes.Get(mf.sharedMesh);
                    if (bm == null && mf.sharedMesh.isReadable)
                    {
                        Transform root = r.transform;
                        while (root.parent != null && !root.parent.name.StartsWith("B_")) root = root.parent;
                        List<CityGlassOpenings.Wall> walls = new List<CityGlassOpenings.Wall>();
                        foreach (MeshFilter wf in root.GetComponentsInChildren<MeshFilter>(true))
                        {
                            if (wf == mf || wf.sharedMesh == null || !wf.sharedMesh.isReadable) continue;
                            MeshRenderer wr = wf.GetComponent<MeshRenderer>();
                            if (wr != null && CityGlassBaker.IsGlassRenderer(wr)) continue;
                            CityGlassOpenings.Wall w = new CityGlassOpenings.Wall();
                            w.V = wf.sharedMesh.vertices; w.T = wf.sharedMesh.triangles;
                            w.ToGlass = mf.transform.worldToLocalMatrix * wf.transform.localToWorldMatrix;
                            walls.Add(w);
                        }
                        Vector3 up = mf.transform.InverseTransformDirection(Vector3.up).normalized;
                        bm = CityGlassBaker.Bake(mf.sharedMesh, up, root.name, walls);
                        if (bm != null) { g.GlassMeshes.Set(mf.sharedMesh, bm); EditorUtility.SetDirty(g.GlassMeshes); }
                    }
                    if (bm != null) { Undo.RecordObject(mf, "Стекло"); mf.sharedMesh = bm; fixedNow++; }
                    else { string key = mf.sharedMesh.name + " (" + r.name + ")"; if (!bad.Contains(key)) bad.Add(key); }
                }
            }
            if (fixedNow > 0) rep.AppendLine($"  стёкол в сцене доразмечено: {fixedNow}");
            if (bad.Count > 0) Debug.LogWarning("[Застройка/окна] стёкла без разметки (меш не читается — включи Read/Write у модели): " + string.Join(", ", bad.ToArray()), g);
            EditorUtility.SetDirty(g);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Застройка/окна] починка: моделей {models.Count} (импорт исправлен у {fixedModels}), домов в сцене обновлено {inst}\n" + rep, g);
        }

        /// <summary>Цвет стекла в атласе: самый частый холодный серо-голубой цвет текстуры → _WinKey.</summary>
        private static void AutoWinKey(Material m, System.Text.StringBuilder rep)
        {
            Texture t = m.GetTexture("_BaseMap");
            if (t == null) return;
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
            Color32[] px = tmp.GetPixels32();
            Object.DestroyImmediate(tmp);
            Dictionary<int, int> hist = new Dictionary<int, int>();
            Dictionary<int, Vector3> sum = new Dictionary<int, Vector3>();
            foreach (Color32 p in px)
            {
                // в гамму (как в инспекторе), квант 16
                float r = Mathf.LinearToGammaSpace(p.r / 255f), gg = Mathf.LinearToGammaSpace(p.g / 255f), b = Mathf.LinearToGammaSpace(p.b / 255f);
                float mx = Mathf.Max(r, Mathf.Max(gg, b)), mn = Mathf.Min(r, Mathf.Min(gg, b));
                if (b < r - 0.02f || gg < r - 0.03f) continue;                     // холодный: синева ≥ красного
                if (mx - mn > 0.3f || mx < 0.25f || mx > 0.92f) continue;          // серо-голубой, не белый и не чёрный
                int key = ((int)(r * 15f) << 8) | ((int)(gg * 15f) << 4) | (int)(b * 15f);
                int cnt; hist.TryGetValue(key, out cnt); hist[key] = cnt + 1;
                Vector3 sm; sum.TryGetValue(key, out sm); sum[key] = sm + new Vector3(r, gg, b);
            }
            int best = -1, bc = 0;
            foreach (KeyValuePair<int, int> kv in hist) if (kv.Value > bc) { bc = kv.Value; best = kv.Key; }
            if (best < 0 || bc < N * N / 200) { rep.AppendLine($"  {m.name}: цвет стекла в атласе не нашёл — задай «Цвет стекла» руками"); return; }
            if (bc > N * N * 0.4f) { rep.AppendLine($"  {m.name}: серо-голубого {bc * 100f / (N * N):0.#}% атласа — это стены, а не окна; ключ не меняю"); return; }
            Vector3 avg = sum[best] / bc;
            Undo.RecordObject(m, "Цвет стекла");
            m.SetColor("_WinKey", new Color(avg.x, avg.y, avg.z, 1f));
            EditorUtility.SetDirty(m);
            rep.AppendLine($"  {m.name}: цвет стекла в атласе = ({avg.x:0.00}, {avg.y:0.00}, {avg.z:0.00}), {bc * 100f / (N * N):0.#}% атласа");
        }

        private static bool IsWinMat(Material m)
        {
            return m != null && m.shader != null && m.shader.name == "CatCooks/City/Building" && m.IsKeywordEnabled("_ATLAS_WINDOWS");
        }

        /// <summary>Близнец «(city)» без окон в атласе для «(city win)»: тот же атлас и настройки, ключ выключен.</summary>
        private static Material NoWin(Material win)
        {
            string p = AssetDatabase.GetAssetPath(win);
            if (string.IsNullOrEmpty(p)) return null;
            string np = p.EndsWith(" (city win).mat") ? p.Substring(0, p.Length - " (city win).mat".Length) + " (city).mat"
                                                      : p.Substring(0, p.Length - 4) + " (no win).mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(np);
            if (m == null)
            {
                m = new Material(win);
                m.DisableKeyword("_ATLAS_WINDOWS");
                if (m.HasProperty("_AtlasWindows")) m.SetFloat("_AtlasWindows", 0f);
                AssetDatabase.CreateAsset(m, np);
                Debug.Log($"[Застройка/окна] создан {np} — атлас без окон для домов со своими стёклами", m);
            }
            else if (m.IsKeywordEnabled("_ATLAS_WINDOWS"))
            {
                m.DisableKeyword("_ATLAS_WINDOWS");
                if (m.HasProperty("_AtlasWindows")) m.SetFloat("_AtlasWindows", 0f);
                EditorUtility.SetDirty(m);
            }
            return m;
        }

        /// <summary>Материал стекла (один на весь город): Assets/Generated/City/CC_WindowGlass.mat.</summary>
        public static Material EnsureGlass()
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(GlassPath);
            if (m != null) return m;
            Shader sh = Shader.Find("CatCooks/City/Window Glass");
            if (sh == null) { Debug.LogWarning("[Застройка/атласы] нет шейдера CatCooks/City/Window Glass — положи Shaders из архива"); return null; }
            MakeFolder("Assets/Generated/City");
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, GlassPath);
            Debug.Log($"[Застройка/атласы] создан материал стекла {GlassPath}");
            return m;
        }

        private static void MakeFolder(string folder)
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

        private static bool IsGlassName(string n)
        {
            n = n.ToLowerInvariant();
            return n.Contains("uv_grid") || n.Contains("glass") || n.Contains("стекл");    // «checker» — нет: атлас небоскрёбов TT_checker_map_gravity
        }

        private static bool IsCityShader(Material m)
        {
            return m != null && m.shader != null && m.shader.name.StartsWith("CatCooks/City/");
        }

        public static int Apply(List<GameObject> models, string layerRoot, string label, bool log, Material glass, bool cityShader, bool atlasWindows,
                                out Dictionary<GameObject, Material> chosen)
        {
            chosen = new Dictionary<GameObject, Material>();
            // кандидаты: папка слоя + папки моделей и их родители
            List<string> dirs = new List<string>();
            if (!string.IsNullOrEmpty(layerRoot) && AssetDatabase.IsValidFolder(layerRoot)) dirs.Add(layerRoot);
            foreach (GameObject m in models)
            {
                string p = AssetDatabase.GetAssetPath(m);
                if (string.IsNullOrEmpty(p)) continue;
                string d = Dir(p);
                if (!dirs.Contains(d)) dirs.Add(d);
                string up = Dir(d);
                if (!string.IsNullOrEmpty(up) && up != "Assets" && !dirs.Contains(up)) dirs.Add(up);
            }
            List<Cand> cands = Collect(dirs, layerRoot);
            if (cands.Count == 0 && log)
                Debug.LogWarning($"[Застройка/атласы] «{label}»: рядом с моделями нет ни текстур, ни материалов — положи атласы в папку слоя (или её подпапки). Стёкла всё равно поставлю.");

            int applied = 0;
            List<string> lines = new List<string>();
            string layerName = !string.IsNullOrEmpty(layerRoot) ? System.IO.Path.GetFileName(layerRoot).ToLowerInvariant() : "";
            foreach (GameObject m in models)
            {
                string path = AssetDatabase.GetAssetPath(m);
                ModelImporter mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) continue;                                         // префаб, не FBX — пропускаем
                Material mat = null;
                Cand best = cands.Count > 0 ? Choose(cands, path, layerName) : null;
                if (best != null)
                {
                    mat = Ensure(best);
                    if (mat != null && cityShader) mat = EnsureCity(best, mat, atlasWindows && !CityGlassBaker.HasGlass(m));   // есть свои стёкла — окна в атласе не трогаем
                }
                if (mat != null) chosen[m] = mat;
                int n = 0, ng = 0;
                // встроенные материалы модели: стекло (checker/glass) → стекло; белые → атлас
                Object[] subs = AssetDatabase.LoadAllAssetsAtPath(path);
                List<string> names = new List<string>();
                foreach (Object o in subs)
                {
                    Material em = o as Material;
                    if (em == null || names.Contains(em.name)) continue;
                    names.Add(em.name);
                    AssetImporter.SourceAssetIdentifier id = new AssetImporter.SourceAssetIdentifier(typeof(Material), em.name);
                    if (IsGlassName(em.name)) { if (glass != null) { mi.AddRemap(id, glass); ng++; } continue; }
                    if (HasTexture(em) || mat == null) continue;                  // у модели свой рабочий материал — не трогаем
                    mi.AddRemap(id, mat);
                    n++;
                }
                // уже переназначенные раньше: белые или старые атласы (не на шейдере города) — заменить
                List<KeyValuePair<AssetImporter.SourceAssetIdentifier, Object>> ext = new List<KeyValuePair<AssetImporter.SourceAssetIdentifier, Object>>(mi.GetExternalObjectMap());
                foreach (KeyValuePair<AssetImporter.SourceAssetIdentifier, Object> kv in ext)
                {
                    if (kv.Key.type != typeof(Material)) continue;
                    Material ex = kv.Value as Material;
                    if (IsGlassName(kv.Key.name)) { if (glass != null && ex != glass) { mi.AddRemap(kv.Key, glass); ng++; } continue; }
                    if (mat == null || ex == mat) continue;
                    bool white = ex == null || !HasTexture(ex);
                    bool oldAtlas = cityShader && ex != null && !IsCityShader(ex);
                    if (!white && !oldAtlas) continue;
                    mi.AddRemap(kv.Key, mat);
                    n++;
                }
                bool needRead = CityGlassBaker.HasGlass(m) && !mi.isReadable;
                if (needRead) mi.isReadable = true;                                   // разметке стёкол нужно читать меш
                if (n + ng == 0 && !needRead) continue;
                EditorUtility.SetDirty(mi);
                mi.SaveAndReimport();
                applied++;
                lines.Add($"{m.name} → {(mat != null ? mat.name : "—")}" + (ng > 0 ? " + стекло" : ""));
            }
            AssetDatabase.SaveAssets();
            if (log) Debug.Log($"[Застройка/атласы] «{label}»: применено к {applied} моделям из {models.Count} (наборов атласов {cands.Count})" +
                               (lines.Count > 0 ? "\n" + string.Join("\n", lines.ToArray()) : "") +
                               (applied < models.Count ? "\nОстальные уже настроены или это не FBX." : ""));
            return applied;
        }

        /// <summary>Материал на шейдере города с текстурой атласа: «<имя> (city).mat» рядом с атласом.</summary>
        private static Material EnsureCity(Cand c, Material src, bool atlasWindows)
        {
            if (IsCityShader(src)) return src;
            Shader sh = Shader.Find("CatCooks/City/Building");
            if (sh == null) { Debug.LogWarning("[Застройка/атласы] нет шейдера CatCooks/City/Building — оставляю обычный материал"); return src; }
            string dir = Dir(AssetDatabase.GetAssetPath(src));
            if (string.IsNullOrEmpty(dir)) dir = c.Dir;
            string nm = c.Mat != null && c.Mat.name.StartsWith("New Material") && !string.IsNullOrEmpty(c.Base) ? c.Name : src.name.Replace(" (atlas)", "");
            if (c.Mat != null && c.Mat.name.StartsWith("New Material"))
            {
                Texture tb = src.HasProperty("_BaseMap") && src.GetTexture("_BaseMap") != null ? src.GetTexture("_BaseMap") : src.GetTexture("_MainTex");
                if (tb != null) { string tn = tb.name; int k = tn.IndexOf("_DefaultMaterial", System.StringComparison.OrdinalIgnoreCase); nm = k > 0 ? tn.Substring(0, k) : tn; }
            }
            string path = dir + "/" + Safe(nm) + (atlasWindows ? " (city win).mat" : " (city).mat");
            Material ex = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (ex != null) return ex;
            Material m = new Material(sh);
            Texture t = src.HasProperty("_BaseMap") && src.GetTexture("_BaseMap") != null ? src.GetTexture("_BaseMap") : (src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null);
            m.SetTexture("_BaseMap", t);
            if (src.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", src.GetTextureScale("_BaseMap"));
            m.SetFloat("_AtlasWindows", atlasWindows ? 1f : 0f);
            if (atlasWindows) m.EnableKeyword("_ATLAS_WINDOWS"); else m.DisableKeyword("_ATLAS_WINDOWS");
            AssetDatabase.CreateAsset(m, path);
            Debug.Log($"[Застройка/атласы] создан материал города {path}" + (atlasWindows ? " (окна из атласа ночью светятся)" : ""));
            return m;
        }

        private static string Dir(string p)
        {
            string d = System.IO.Path.GetDirectoryName(p);
            return string.IsNullOrEmpty(d) ? "" : d.Replace('\\', '/');
        }

        private static bool HasTexture(Material m)
        {
            if (m == null) return false;
            if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) return true;
            if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null) return true;
            return false;
        }

        /// <summary>Материалы и наборы текстур в папках (папка слоя — с подпапками, остальные — только сами).</summary>
        private static List<Cand> Collect(List<string> dirs, string layerRoot)
        {
            List<Cand> list = new List<Cand>();
            HashSet<string> seen = new HashSet<string>();
            Dictionary<string, Cand> sets = new Dictionary<string, Cand>();
            foreach (string d in dirs)
            {
                bool deep = d == layerRoot;
                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { d }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (!deep && Dir(p) != d) continue;
                    if (!seen.Add(p)) continue;
                    if (!p.EndsWith(".mat")) continue;                              // встроенные в FBX — не кандидаты
                    Material m = AssetDatabase.LoadAssetAtPath<Material>(p);
                    if (m == null || !HasTexture(m) || IsCityShader(m)) continue;          // свои (city/стекло) — не кандидаты
                    Texture t = m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null ? m.GetTexture("_BaseMap") : m.GetTexture("_MainTex");
                    string tp = t != null ? AssetDatabase.GetAssetPath(t) : "";
                    list.Add(new Cand { Mat = m, Dir = Dir(p), Name = m.name, Key = (p + " " + tp).ToLowerInvariant() });
                }
                foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { d }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (!deep && Dir(p) != d) continue;
                    if (!seen.Add(p)) continue;
                    string file = System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
                    string ch;
                    string prefix = SplitChannel(file, out ch);
                    if (ch == null) continue;
                    string key = Dir(p) + "|" + prefix;
                    Cand c;
                    if (!sets.TryGetValue(key, out c))
                    {
                        string nm = System.IO.Path.GetFileNameWithoutExtension(p);
                        nm = nm.Length > prefix.Length ? nm.Substring(0, prefix.Length).TrimEnd('_', ' ', '-') : nm;
                        if (string.IsNullOrEmpty(nm)) nm = System.IO.Path.GetFileName(Dir(p));
                        c = new Cand { Dir = Dir(p), Name = nm, Key = p.ToLowerInvariant() };
                        sets[key] = c;
                    }
                    if (ch == "base") c.Base = p;
                    else if (ch == "ms") c.MS = p;
                    else if (ch == "normal") c.Normal = p;
                    else if (ch == "metal") c.Metal = p;
                }
            }
            // набор текстур, у которого уже есть .mat с этим альбедо, — не дублируем
            foreach (Cand s in sets.Values)
            {
                if (string.IsNullOrEmpty(s.Base)) continue;
                bool covered = false;
                foreach (Cand c in list) if (c.Mat != null && c.Key.Contains(s.Base.ToLowerInvariant())) { covered = true; break; }
                if (!covered) list.Add(s);
            }
            return list;
        }

        /// <summary>Имя без канала: «little buildings_defaultmaterial_albedotransparency» → «little buildings», канал «base».</summary>
        private static string SplitChannel(string f, out string ch)
        {
            ch = null;
            string[][] map =
            {
                new[] { "metallicsmoothness", "ms" }, new[] { "albedotransparency", "base" }, new[] { "basecolor", "base" }, new[] { "base_color", "base" },
                new[] { "albedo", "base" }, new[] { "diffuse", "base" }, new[] { "_color", "base" }, new[] { "normal", "normal" }, new[] { "metallic", "metal" }
            };
            for (int i = 0; i < map.Length; i++)
            {
                int at = f.LastIndexOf(map[i][0], System.StringComparison.Ordinal);
                if (at < 0) continue;
                ch = map[i][1];
                string pre = f.Substring(0, at);
                int dm = pre.LastIndexOf("defaultmaterial", System.StringComparison.Ordinal);
                if (dm >= 0) pre = pre.Substring(0, dm);
                return pre.TrimEnd('_', ' ', '-', '.');
            }
            return f;
        }

        private static List<string> Words(string s, string layerName)
        {
            List<string> r = new List<string>();
            System.Text.StringBuilder cur = new System.Text.StringBuilder();
            string low = s.ToLowerInvariant() + " ";
            for (int i = 0; i < low.Length; i++)
            {
                char ch = low[i];
                if (char.IsLetter(ch)) { cur.Append(ch); continue; }
                if (cur.Length >= 3)
                {
                    string w = cur.ToString();
                    bool gen = w == layerName;
                    for (int k = 0; k < Generic.Length && !gen; k++) if (Generic[k] == w) gen = true;
                    if (!gen && !r.Contains(w)) r.Add(w);
                }
                cur.Length = 0;
            }
            return r;
        }

        private static bool Has(List<string> words, string w)
        {
            for (int i = 0; i < words.Count; i++)
                if (words[i] == w || words[i].StartsWith(w) || w.StartsWith(words[i])) return true;   // skyscraper / skyscrapers
            return false;
        }

        private static Cand Choose(List<Cand> cands, string modelPath, string layerName)
        {
            if (cands.Count == 1) return cands[0];
            List<string> mw = Words(modelPath, layerName);
            string md = Dir(modelPath);
            // слова кандидатов; общие для всех (Assets, имя проекта, папка города) — не различают, выкидываем
            List<List<string>> cws = new List<List<string>>();
            for (int i = 0; i < cands.Count; i++) cws.Add(Words(cands[i].Key, layerName));
            List<string> shared = new List<string>(cws[0]);
            for (int i = 1; i < cws.Count; i++) shared.RemoveAll(w => !cws[i].Contains(w));
            Cand best = null; float bestScore = -1f;
            Cand common = null;
            for (int i = 0; i < cands.Count; i++)
            {
                Cand c = cands[i];
                float sc = 0f;
                for (int k = 0; k < cws[i].Count; k++)
                    if (!shared.Contains(cws[i][k]) && Has(mw, cws[i][k])) sc += 1f;
                if (c.Dir == md) sc += 0.5f;
                else if (md.StartsWith(c.Dir + "/")) sc += 0.25f;
                if (sc > bestScore) { bestScore = sc; best = c; }
                string lk = c.Key;
                if (common == null && (lk.Contains("plane") || lk.Contains("atlas") || lk.Contains("main") || lk.Contains("common"))) common = c;
            }
            if (bestScore < 1f && common != null) return common;      // ни одно слово не совпало — общий атлас
            return best;
        }

        /// <summary>Готовый материал или созданный из набора текстур (рядом с текстурами).</summary>
        private static Material Ensure(Cand c)
        {
            if (c.Mat != null) return c.Mat;
            if (string.IsNullOrEmpty(c.Base)) return null;
            string path = c.Dir + "/" + Safe(c.Name) + " (atlas).mat";
            Material ex = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (ex != null) { c.Mat = ex; return ex; }
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            bool urp = sh != null;
            if (sh == null) sh = Shader.Find("Standard");
            Material m = new Material(sh);
            Texture2D bt = AssetDatabase.LoadAssetAtPath<Texture2D>(c.Base);
            if (urp) { m.SetTexture("_BaseMap", bt); m.SetColor("_BaseColor", Color.white); }
            m.SetTexture("_MainTex", bt);
            m.color = Color.white;
            if (!string.IsNullOrEmpty(c.MS))
            {
                m.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(c.MS));
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 1f);
                if (m.HasProperty("_GlossMapScale")) m.SetFloat("_GlossMapScale", 1f);
            }
            else
            {
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.25f);
            }
            if (!string.IsNullOrEmpty(c.Normal))
            {
                TextureImporter ti = AssetImporter.GetAtPath(c.Normal) as TextureImporter;
                if (ti != null && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(c.Normal));
                m.EnableKeyword("_NORMALMAP");
            }
            AssetDatabase.CreateAsset(m, path);
            Debug.Log($"[Застройка/атласы] создан материал {path}");
            c.Mat = m;
            return m;
        }

        private static string Safe(string s)
        {
            char[] bad = System.IO.Path.GetInvalidFileNameChars();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char ch in s)
            {
                bool ok = true;
                foreach (char b in bad) if (b == ch) { ok = false; break; }
                sb.Append(ok ? ch : '_');
            }
            return sb.ToString();
        }
    }
}
