#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DS = CatCooks.Food.CatCooksDessertSetup;

namespace CatCooks.Food
{
    /// <summary>
    /// НОВЫЕ ТУШИ — свинья и бык без головы из кода (CarcassModel), разрезанные по настоящим отрубам.
    ///
    ///   1. Продукты частей и стейков + Mat_Meat + MeatCutSettings (Resources).
    ///   2. Свиная туша в сцену.            3. Говяжья туша в сцену.
    ///   4. Заготовка общего куска «MeatCut» — сохрани её префабом руками (ОДИН на всё).
    ///   5. Полка: все части по отдельности (свинина / говядина) — по желанию, если нужны префабы частей.
    ///   6. Связать: префаб MeatCut → настройки и туши; рёбра свиньи → пласт барбекю; кости.
    ///   7. Проверка.
    /// Префабы меню не создаёт — только объекты в сцене и ассеты (меши, материал, текстура, настройки).
    /// Повторный запуск обновляет то же самое на месте.
    /// </summary>
    public static class CatCooksCarcassSetup
    {
        private const string MeshDir = "Assets/Models/Meat";
        private const string SettingsDir = "Assets/CatCooks/Food/Meat/Resources";
        private const string SettingsPath = SettingsDir + "/MeatCutSettings.asset";
        private const string Root = "CatCooks/Мясо/Новые туши/";

        // ───────────────────────── 1. ПРОДУКТЫ ─────────────────────────

        [MenuItem(Root + "1. Продукты частей и стейков + настройки", priority = 20)]
        public static void MakeProducts()
        {
            var log = new StringBuilder();
            MeatCutSettings st = Settings(log);
            string dir = DS.ProductsDir();
            ushort next = (ushort)(DS.MaxId() + 1);
            FoodGroupDef porkG = DS.GroupOf("Свинина"), beefG = DS.GroupOf("Говядина");
            if (porkG == null && beefG == null) { porkG = DS.GroupOf("Рёбра"); log.AppendLine("⚠ нет «Свинина»/«Говядина» — группа взята у «Рёбра». Лучше сначала CatCooks → Еда → Создать продукты"); }
            if (porkG == null) porkG = beefG; if (beefG == null) beefG = porkG;
            st.PorkKinds = new ProductDef[CarcassModel.KindCount]; st.PorkSlices = new ProductDef[CarcassModel.KindCount];
            st.BeefKinds = new ProductDef[CarcassModel.KindCount]; st.BeefSlices = new ProductDef[CarcassModel.KindCount];
            log.AppendLine("СВИНИНА:");
            Fill(MeatSpecies.Pork, porkG, new Color(0.84f, 0.47f, 0.44f), st.PorkKinds, st.PorkSlices, dir, ref next, log);
            log.AppendLine("\nГОВЯДИНА:");
            Fill(MeatSpecies.Beef, beefG, new Color(0.62f, 0.14f, 0.12f), st.BeefKinds, st.BeefSlices, dir, ref next, log);
            st.Material = MeatMat(log);
            EditorUtility.SetDirty(st);
            AssetDatabase.SaveAssets();
            log.AppendLine($"\nНастройки: {SettingsPath}");
            log.AppendLine("Сохрани сцену (пересоберётся FoodDatabase) и жми 2 / 3 — туши.");
            DS.Done("Новые туши — продукты", log);
        }

        private static void Fill(MeatSpecies sp, FoodGroupDef g, Color col, ProductDef[] kinds, ProductDef[] slices, string dir, ref ushort next, StringBuilder log)
        {
            for (int k = 0; k < CarcassModel.KindCount; k++)
            {
                string t = CarcassModel.KindTitle(sp, k);
                kinds[k] = DS.Ensure(t, g, col, 0.2f, false, dir, ref next, log);
                string s = CarcassModel.KindSliceTitle(sp, k);
                slices[k] = s != null ? DS.Ensure(s, g, col, 0.12f, false, dir, ref next, log) : null;
            }
        }

        private static MeatCutSettings Settings(StringBuilder log)
        {
            MeatCutSettings st = AssetDatabase.LoadAssetAtPath<MeatCutSettings>(SettingsPath);
            if (st != null) return st;
            DS.EnsureDir(SettingsDir);
            st = ScriptableObject.CreateInstance<MeatCutSettings>();
            AssetDatabase.CreateAsset(st, SettingsPath);
            log.AppendLine($"＋ настройки {SettingsPath}");
            return st;
        }

        private static Material MeatMat(StringBuilder log)
        {
            string path = MeshDir + "/Mat_Meat.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool fresh = m == null;
            DS.EnsureDir(MeshDir);
            Texture2D tex = FiberTexture(log);
            Shader sh = Shader.Find("CatCooks/Food Heap");
            bool heap = sh != null;
            if (!heap) { sh = Shader.Find("Universal Render Pipeline/Lit"); log.AppendLine("⚠ нет шейдера CatCooks/Food Heap — Mat_Meat на URP/Lit, цвета мяса (вершины) видны не будут"); }
            if (fresh) m = new Material(sh) { name = "Mat_Meat" };
            else if (heap && m.shader != sh) m.shader = sh;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (heap)
            {
                // мультяшно: волокна еле видны, мягкий ободок, тонкая обводка
                m.SetFloat("_TexScale", 6f);
                m.SetFloat("_TexDetail", 0.5f);
                m.SetFloat("_GlintStrength", 0.1f);
                m.SetFloat("_GlintSize", 0.9f);
                m.SetFloat("_Rim", 0.22f);
                m.SetFloat("_OutlineWidth", 0.003f);
                m.SetFloat("_UseMeshUV", 0f);
            }
            if (fresh) { AssetDatabase.CreateAsset(m, path); log.AppendLine($"＋ материал {path} (CatCooks/Food Heap, мультяшный)"); }
            else { EditorUtility.SetDirty(m); log.AppendLine($"✔ материал {path} обновлён (мультяшный)"); }
            return m;
        }

        /// <summary>Волокна мяса 256×256, серые, бесшовные — цвет даёт меш.</summary>
        private static Texture2D FiberTexture(StringBuilder log)
        {
            string path = MeshDir + "/Tex_MeatFibers.png";
            Texture2D have = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (have != null) return have;
            const int N = 256;
            var px = new Color32[N * N];
            var rnd = new System.Random(23);
            float[] ph = new float[6]; for (int i = 0; i < ph.Length; i++) ph[i] = (float)rnd.NextDouble() * 6.28f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    float warp = 0.035f * Mathf.Sin((v * 3f + u) * 6.283f + ph[0]) + 0.02f * Mathf.Sin(u * 4f * 6.283f + ph[1]);
                    float fib = Mathf.Abs(Mathf.Sin((u + warp) * 6.283f * 40f + ph[2]));
                    float fib2 = Mathf.Abs(Mathf.Sin((u + warp * 1.6f) * 6.283f * 11f + ph[3]));
                    float pore = Mathf.PerlinNoise(u * 36f + 3.3f, v * 36f + 7.1f);
                    float g = 0.74f + 0.15f * fib + 0.07f * fib2 - 0.08f * Mathf.Clamp01((pore - 0.6f) * 5f);
                    byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(g) * 255f);
                    px[y * N + x] = new Color32(b, b, b, 255);
                }
            var t = new Texture2D(N, N, TextureFormat.RGB24, true);
            t.SetPixels32(px); t.Apply();
            DS.EnsureDir(MeshDir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), path), t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            log.AppendLine($"＋ текстура {path}");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ───────────────────────── 2/3. ТУШИ ─────────────────────────

        [MenuItem(Root + "2. Свиная туша в сцену", priority = 21)]
        public static void MakePork() => MakeCarcass(MeatSpecies.Pork);

        [MenuItem(Root + "3. Говяжья туша в сцену", priority = 22)]
        public static void MakeBeef() => MakeCarcass(MeatSpecies.Beef);

        /// <summary>Меши всех частей вида — ассеты Assets/Models/Meat/Meat_*.asset (и в настройки).</summary>
        private static Mesh[] BuildMeshes(MeatSpecies sp, MeatCutSettings st, StringBuilder log)
        {
            var res = new Mesh[CarcassModel.PartCount];
            int done = 0, total = 0;
            for (int i = 0; i < CarcassModel.PartCount; i++) if (CarcassModel.Exists(sp, (MeatPart)i)) total++;
            try
            {
                for (int i = 0; i < CarcassModel.PartCount; i++)
                {
                    var p = (MeatPart)i;
                    if (!CarcassModel.Exists(sp, p)) continue;
                    EditorUtility.DisplayProgressBar("Туша", $"{CarcassModel.Nice(sp, p)} ({done + 1}/{total})", done / (float)total);
                    MeatMesh mm = CarcassModel.Build(sp, p);
                    done++;
                    if (mm == null) { log.AppendLine($"  ⚠ {CarcassModel.Id(sp, p)}: пусто"); continue; }
                    Mesh m = CarcassModel.ToMesh(mm, "Meat_" + CarcassModel.Id(sp, p), CarcassModel.BoxCenter(sp, p));
                    res[i] = SaveMesh(m);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            Mesh[] arr = sp == MeatSpecies.Beef ? st.BeefMeshes : st.PorkMeshes;
            if (arr == null || arr.Length != CarcassModel.PartCount) arr = new Mesh[CarcassModel.PartCount];
            for (int i = 0; i < arr.Length; i++) arr[i] = res[i];
            if (sp == MeatSpecies.Beef) st.BeefMeshes = arr; else st.PorkMeshes = arr;
            EditorUtility.SetDirty(st);
            log.AppendLine($"Меши частей: {done} шт. → {MeshDir}/Meat_{(sp == MeatSpecies.Beef ? "Beef" : "Pork")}_*.asset");
            return res;
        }

        private static Mesh SaveMesh(Mesh m)
        {
            DS.EnsureDir(MeshDir);
            string path = MeshDir + "/" + m.name + ".asset";
            Mesh have = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (have != null) { EditorUtility.CopySerialized(m, have); Object.DestroyImmediate(m); EditorUtility.SetDirty(have); return have; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static void MakeCarcass(MeatSpecies sp)
        {
            var log = new StringBuilder();
            MeatCutSettings st = Settings(log);
            string beefOrPork = sp == MeatSpecies.Beef ? "Говядина" : "Свинина";
            ProductDef whole = DS.Find(beefOrPork);
            if (whole == null) { EditorUtility.DisplayDialog("Туша", $"Нет продукта «{beefOrPork}» — CatCooks → Еда → Создать продукты.", "Ок"); return; }
            if (st.Product(sp, MeatPart.LoinL) == null) log.AppendLine("⚠ нет продуктов частей — сначала «1. Продукты частей и стейков»");
            st.Material = MeatMat(log);
            Mesh[] meshes = BuildMeshes(sp, st, log);

            string name = sp == MeatSpecies.Beef ? "Carcass_Beef" : "Carcass_Pork";
            GameObject go = FindRoot(name);
            bool fresh = go == null;
            if (fresh)
            {
                go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, name);
                go.transform.position = At();
            }
            else Undo.RegisterFullObjectHierarchyUndo(go, "Туша");
            go.transform.localScale = Vector3.one * st.Scale;   // куски при отпиле берут этот же масштаб
            int food = LayerMask.NameToLayer("Food");
            if (food >= 0) go.layer = food;

            // старые куски (раскладка v2.2 — 28/30 частей) убрать
            var keep = new HashSet<string>();
            for (int i = 0; i < CarcassModel.PartCount; i++) keep.Add(CarcassModel.Id(sp, (MeatPart)i));
            for (int c = go.transform.childCount - 1; c >= 0; c--)
            {
                Transform ch = go.transform.GetChild(c);
                if (ch.GetComponent<MeatPartTag>() != null && !keep.Contains(ch.name)) { log.AppendLine($"  － старый кусок «{ch.name}» убран"); Undo.DestroyObjectImmediate(ch.gameObject); }
            }
            var parts = new List<Transform>();
            for (int i = 0; i < CarcassModel.PartCount; i++)
            {
                var p = (MeatPart)i;
                if (meshes[i] == null) continue;
                Transform t = DS.FindOrCreate(go.transform, CarcassModel.Id(sp, p));
                t.localPosition = CarcassModel.BoxCenter(sp, p); t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
                t.gameObject.layer = go.layer;
                DS.Need<MeshFilter>(t.gameObject).sharedMesh = meshes[i];
                DS.Need<MeshRenderer>(t.gameObject).sharedMaterial = st.Material;
                BoxCollider bc = DS.Need<BoxCollider>(t.gameObject);
                bc.center = meshes[i].bounds.center; bc.size = meshes[i].bounds.size;
                MeatPartTag tag = DS.Need<MeatPartTag>(t.gameObject);
                tag.Species = sp; tag.Part = p;
                EditorUtility.SetDirty(tag);
                parts.Add(t);
            }

            Rigidbody rb = DS.Need<Rigidbody>(go);
            rb.mass = sp == MeatSpecies.Beef ? 250f : 80f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            FoodPiece fp = go.GetComponent<FoodPiece>();
            if (fp == null) fp = Undo.AddComponent<FoodPiece>(go);
            var so = new SerializedObject(fp);
            DS.SetObj(so, "_product", whole);
            DS.SetInt(so, "_startState", (int)FoodState.Carcass);
            DS.SetInt(so, "_count", parts.Count);
            so.ApplyModifiedProperties();
            FoodCarcass fc = DS.Need<FoodCarcass>(go);
            GameObject[] bones = BoneSet();
            fc.EditorSetup(parts.ToArray(), null, null, bones);
            fc.EditorSetCut(st.CutPrefab);
            fc.EditorSetRibs(sp == MeatSpecies.Pork ? RibsPrefab() : null, -1);
            var fso = new SerializedObject(fc);
            DS.SetF(fso, "_edgeWidth", (sp == MeatSpecies.Beef ? 0.12f : 0.08f) * st.Scale);
            fso.ApplyModifiedProperties();
            EditorUtility.SetDirty(fc);
            EditorUtility.SetDirty(st);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);

            Bounds wb = new Bounds(go.transform.position, Vector3.zero);
            foreach (Renderer rr in go.GetComponentsInChildren<Renderer>()) wb.Encapsulate(rr.bounds);
            log.AppendLine($"Масштаб ×{st.Scale:0.0#} (Resources/MeatCutSettings → Scale): туша {wb.size.x:0.0#}×{wb.size.y:0.0#}×{wb.size.z:0.0#} м");
            log.AppendLine($"\n{(fresh ? "＋" : "✔ обновлена")} «{name}»: {parts.Count} частей, Rigidbody {rb.mass:0} кг, Food Piece «{beefOrPork}» (Туша целиком), Food Carcass");
            foreach (Transform t in parts)
            {
                MeatPartTag tag = t.GetComponent<MeatPartTag>();
                log.AppendLine($"   · {t.name} — {CarcassModel.Nice(sp, tag.Part)}");
            }
            log.AppendLine(bones.Length > 0 ? $"Кости: {bones.Length} префабов" : "⚠ костей нет — Мясо → «Префабы костей из выделенного набора»");
            log.AppendLine(st.CutPrefab != null ? $"Префаб куска: {st.CutPrefab.name} ✔" : "⚠ нет префаба куска — меню 4, сохрани префабом, потом 6");
            if (sp == MeatSpecies.Pork) log.AppendLine(RibsPrefab() != null ? "Рёбра → пласт барбекю (RibsRack) ✔" : "Рёбра: нет префаба RibsRack — выпадут обычным куском (Барбекю → 2, потом здесь 6)");
            log.AppendLine("\nДАЛЬШЕ: перетащи тушу в Project — будет префаб туши (если нужен). Network Manager → Spawnable Prefabs.");
            DS.Done(sp == MeatSpecies.Beef ? "Говяжья туша" : "Свиная туша", log);
        }

        // ───────────────────────── 4. ЗАГОТОВКА КУСКА ─────────────────────────

        [MenuItem(Root + "4. Заготовка общего куска MeatCut (сохрани префабом)", priority = 23)]
        public static void MakeCutTemplate()
        {
            var log = new StringBuilder();
            MeatCutSettings st = Settings(log);
            if (st.Material == null) st.Material = MeatMat(log);
            GameObject go = FindRoot("MeatCut");
            bool fresh = go == null;
            if (fresh)
            {
                go = new GameObject("MeatCut");
                Undo.RegisterCreatedObjectUndo(go, "MeatCut");
                go.transform.position = At() + Vector3.up * 0.3f;
            }
            SetupCut(go, MeatSpecies.Pork, MeatPart.LoinL, st, log);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"{(fresh ? "＋" : "✔")} «MeatCut» — общий кусок (в сцене выглядит как свиной карбонад).");
            log.AppendLine("\nДАЛЬШЕ РУКАМИ:");
            log.AppendLine("1. Перетащи MeatCut в Project → префаб MeatCut.");
            log.AppendLine("2. Network Manager → Spawnable Prefabs → добавь MeatCut.");
            log.AppendLine("3. Удали MeatCut из сцены (или оставь — это просто кусок карбонада).");
            log.AppendLine("4. Новые туши → 6 — привяжет префаб к настройкам и тушам.");
            DS.Done("MeatCut", log);
        }

        private static void SetupCut(GameObject go, MeatSpecies sp, MeatPart part, MeatCutSettings st, StringBuilder log)
        {
            int food = LayerMask.NameToLayer("Food");
            if (food >= 0) go.layer = food;
            Mesh mesh = st.BaseMesh(sp, part);
            if (mesh == null)
            {
                MeatMesh mm = CarcassModel.Build(sp, part);
                if (mm != null) mesh = SaveMesh(CarcassModel.ToMesh(mm, "Meat_" + CarcassModel.Id(sp, part), CarcassModel.BoxCenter(sp, part)));
            }
            DS.Need<MeshFilter>(go).sharedMesh = mesh;
            DS.Need<MeshRenderer>(go).sharedMaterial = st.Material;
            BoxCollider bc = DS.Need<BoxCollider>(go);
            if (mesh != null) { bc.center = mesh.bounds.center; bc.size = mesh.bounds.size; }
            Rigidbody rb = DS.Need<Rigidbody>(go);
            rb.mass = 1f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            MeatCut mc = DS.Need<MeatCut>(go);
            var so = new SerializedObject(mc);
            DS.SetObj(so, "_product", st.Product(sp, part));
            DS.SetInt(so, "_startState", (int)FoodState.Butchered);
            DS.SetInt(so, "_count", 1);
            so.ApplyModifiedProperties();
            mc.EditorSetup(sp, part);
        }

        // ───────────────────────── 5. ПОЛКА ─────────────────────────

        [MenuItem(Root + "5а. Полка: все части свинины по отдельности", priority = 24)]
        public static void ShelfPork() => Shelf(MeatSpecies.Pork);

        [MenuItem(Root + "5б. Полка: все части говядины по отдельности", priority = 25)]
        public static void ShelfBeef() => Shelf(MeatSpecies.Beef);

        private static void Shelf(MeatSpecies sp)
        {
            var log = new StringBuilder();
            MeatCutSettings st = Settings(log);
            if (st.Material == null) st.Material = MeatMat(log);
            if (st.BaseMesh(sp, MeatPart.LoinL) == null) BuildMeshes(sp, st, log);
            string name = sp == MeatSpecies.Beef ? "MeatShelf_Beef" : "MeatShelf_Pork";
            GameObject shelf = FindRoot(name);
            if (shelf == null)
            {
                shelf = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(shelf, name);
                shelf.transform.position = At() + Vector3.right * 2.5f * st.Scale;
            }
            shelf.transform.localScale = Vector3.one * st.Scale;
            float step = sp == MeatSpecies.Beef ? 0.75f : 0.5f;
            int col = 0, row = 0, n = 0;
            for (int i = 0; i < CarcassModel.PartCount; i++)
            {
                var p = (MeatPart)i;
                if (!CarcassModel.Exists(sp, p)) continue;
                Transform t = DS.FindOrCreate(shelf.transform, CarcassModel.Id(sp, p));
                t.localPosition = new Vector3(col * step, 0.15f, row * step);
                t.localRotation = Quaternion.identity;
                SetupCut(t.gameObject, sp, p, st, log);
                log.AppendLine($"  · {t.name} — {CarcassModel.Nice(sp, p)} → «{(st.Product(sp, p) != null ? st.Product(sp, p).Title : "нет продукта — меню 1")}»");
                n++;
                if (++col >= 6) { col = 0; row++; }
            }
            Selection.activeGameObject = shelf;
            EditorSceneManager.MarkSceneDirty(shelf.scene);
            log.AppendLine($"\n«{name}»: {n} кусков (каждый — Meat Cut со своей частью и продуктом).");
            log.AppendLine("Нужны префабы частей — перетащи нужные дочерние объекты в Project (можно все разом), добавь в Spawnable Prefabs.");
            log.AppendLine("Для туши префабы частей НЕ нужны: она сама выдаёт куски общим MeatCut. Полку потом можно удалить.");
            DS.Done("Полка частей", log);
        }

        // ───────────────────────── 6. СВЯЗАТЬ ─────────────────────────

        [MenuItem(Root + "6. Связать префаб MeatCut, рёбра и кости с тушами", priority = 26)]
        public static void Link()
        {
            var log = new StringBuilder();
            MeatCutSettings st = Settings(log);
            GameObject cut = null;
            if (Selection.activeObject is GameObject sel && EditorUtility.IsPersistent(sel) && sel.GetComponent<MeatCut>() != null) cut = sel;
            if (cut == null) cut = FindPrefab<MeatCut>(go => go.name == "MeatCut") ?? FindPrefab<MeatCut>(go => true);
            if (cut == null) { log.AppendLine("✖ нет префаба с Meat Cut — меню 4, перетащи «MeatCut» в Project, потом снова 6"); }
            else { st.CutPrefab = cut; EditorUtility.SetDirty(st); log.AppendLine($"✔ Cut Prefab = {AssetDatabase.GetAssetPath(cut)}"); }
            GameObject ribs = RibsPrefab();
            GameObject[] bones = BoneSet();
            int n = 0;
            foreach (FoodCarcass c in Object.FindObjectsByType<FoodCarcass>(FindObjectsSortMode.None))
            {
                MeatPartTag tag = c.GetComponentInChildren<MeatPartTag>(true);
                if (tag == null) continue;
                Undo.RecordObject(c, "Связать тушу");
                if (cut != null) c.EditorSetCut(cut);
                c.EditorSetRibs(tag.Species == MeatSpecies.Pork ? ribs : null, -1);
                if (bones.Length > 0) c.EditorSetup(c.EditorCollectParts(), null, null, bones);
                n++;
                log.AppendLine($"✔ «{c.name}»: кусок {(cut != null ? cut.name : "—")}, " +
                               (tag.Species == MeatSpecies.Pork ? (ribs != null ? $"рёбра → {ribs.name}" : "рёбра — кусками (нет RibsRack)") : "рёбра — кусками «Говяжьи рёбра»") +
                               $", костей {bones.Length}");
                if (PrefabUtility.IsPartOfPrefabInstance(c)) log.AppendLine("   туша — префаб: Overrides → Apply All");
            }
            if (n == 0) log.AppendLine("⚠ новых туш в сцене нет — меню 2 / 3");
            AssetDatabase.SaveAssets();
            log.AppendLine("\nПроверь: MeatCut — в Network Manager → Spawnable Prefabs. Сохрани сцену.");
            DS.Done("Новые туши — связать", log);
        }

        // ───────────────────────── 7. ПРОВЕРКА ─────────────────────────

        [MenuItem(Root + "7. Проверить", priority = 27)]
        public static void Check()
        {
            var sb = new StringBuilder();
            int err = 0, warn = 0;
            MeatCutSettings st = AssetDatabase.LoadAssetAtPath<MeatCutSettings>(SettingsPath);
            if (st == null) { sb.AppendLine("✖ нет настроек — меню 1"); err++; }
            else
            {
                foreach (MeatSpecies sp in new[] { MeatSpecies.Pork, MeatSpecies.Beef })
                {
                    int miss = 0;
                    for (int k = 0; k < CarcassModel.KindCount; k++)
                    {
                        ProductDef[] a = sp == MeatSpecies.Beef ? st.BeefKinds : st.PorkKinds;
                        if (a == null || k >= a.Length || a[k] == null) miss++;
                    }
                    if (miss > 0) { sb.AppendLine($"✖ {sp}: нет {miss} продуктов частей — меню 1"); err++; } else sb.AppendLine($"✔ {sp}: продукты частей");
                    if (st.BaseMesh(sp, MeatPart.LoinL) == null) { sb.AppendLine($"⚠ {sp}: мешей частей нет — меню {(sp == MeatSpecies.Pork ? 2 : 3)}"); warn++; }
                }
                if (st.CutPrefab == null) { sb.AppendLine("✖ Cut Prefab пуст — меню 4, сохрани префаб, меню 6"); err++; }
                else sb.AppendLine($"✔ Cut Prefab {st.CutPrefab.name}");
                if (st.Material == null) { sb.AppendLine("⚠ нет Mat_Meat — меню 1"); warn++; }
                else if (st.Material.shader == null || st.Material.shader.name != "CatCooks/Food Heap") { sb.AppendLine($"⚠ Mat_Meat на «{st.Material.shader?.name}» — нужен CatCooks/Food Heap (иначе однотонно)"); warn++; }
                else sb.AppendLine("✔ Mat_Meat — CatCooks/Food Heap");
            }
            foreach (FoodCarcass c in Object.FindObjectsByType<FoodCarcass>(FindObjectsSortMode.None))
            {
                MeatPartTag tag = c.GetComponentInChildren<MeatPartTag>(true);
                if (tag == null) continue;
                var so = new SerializedObject(c);
                bool cut = so.FindProperty("_cutPrefab").objectReferenceValue != null || (st != null && st.CutPrefab != null);
                int bones = so.FindProperty("_bonePrefabs").arraySize;
                sb.AppendLine($"{(cut ? "✔" : "✖")} туша «{c.name}»: частей {c.PartCount}, кусок {(cut ? "есть" : "НЕТ — меню 6")}, костей {bones}");
                if (tag.Species == MeatSpecies.Pork)
                {
                    GameObject rp = so.FindProperty("_ribsPrefab").objectReferenceValue as GameObject;
                    RibsRack rr = rp != null ? rp.GetComponent<RibsRack>() : null;
                    if (rp == null) { sb.AppendLine("   ⚠ рёбра выпадут обычным куском — Ribs Prefab пуст (меню 6)"); warn++; }
                    else if (rr == null || rr.IsBrisket) { sb.AppendLine($"   ✖ Ribs Prefab = «{rp.name}» — это не пласт рёбер{(rr != null ? " (брискет!)" : "")} — меню 6"); err++; }
                    else sb.AppendLine($"   ✔ рёбра → пласт «{rp.name}»");
                }
                if (!cut) err++;
                if (bones == 0) warn++;
            }
            sb.Insert(0, err == 0 ? (warn == 0 ? "ВСЁ ГОТОВО ✔\n\n" : $"Можно резать, замечаний: {warn}\n\n") : $"Ошибок: {err}, замечаний: {warn}\n\n");
            DS.Done("Новые туши — проверка", sb);
        }

        // ───────────────────────── помощники ─────────────────────────

        private static Vector3 At() => SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

        private static GameObject FindRoot(string name)
        {
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && t.parent == null && t.name == name && t.gameObject.scene.IsValid()) return t.gameObject;
            return null;
        }

        private static GameObject FindPrefab<T>(System.Func<GameObject, bool> ok) where T : Component
        {
            foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
            {
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                if (go != null && go.GetComponent<T>() != null && ok(go)) return go;
            }
            return null;
        }

        /// <summary>Пласт рёбер барбекю (RibsRack, рёбер больше одного).</summary>
        private static GameObject RibsPrefab()
        {
            // Пласт рёбер: Ribs > 1 и НЕ брискет (у брискета тоже 8 «рёбер»-ломтиков). Имя RibsRack — в приоритете.
            System.Func<GameObject, bool> ok = go =>
            {
                RibsRack r = go.GetComponent<RibsRack>();
                if (r == null || r.IsBrisket) return false;
                SerializedProperty n = new SerializedObject(r).FindProperty("_ribs");
                return n != null && n.intValue > 1;
            };
            return FindPrefab<RibsRack>(go => go.name == "RibsRack" && ok(go)) ?? FindPrefab<RibsRack>(ok);
        }

        /// <summary>Набор костей Bone_*.prefab (Мясо → «Префабы костей из выделенного набора»).</summary>
        private static GameObject[] BoneSet()
        {
            var list = new List<GameObject>();
            foreach (string g in AssetDatabase.FindAssets("Bone_ t:Prefab"))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (p.EndsWith("Bone_ForBroth.prefab")) continue;
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null && go.GetComponent<FoodPiece>() != null) list.Add(go);
            }
            if (list.Count == 0)
            {
                GameObject one = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CatCooks/Food/Meat/Bone_ForBroth.prefab");
                if (one != null) list.Add(one);
            }
            return list.ToArray();
        }
    }
}
#endif
