#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// CatCooks → Шоколад:
    ///   1. Создать продукты и рецепты — Какао-боб (стручок), Какао-зёрна (белая гроздь), Обжаренные какао-зёрна,
    ///      Тёртое какао, Шоколадная паста, Шоколад; правила кувалды; застывание пасты; рецепты
    ///      «Тёртое какао» (отбойник) и «Шоколадная паста» (сахар + масло, перемешать).
    ///   2. Добавить пасту в выделенную тару — LiquidAnchor: паста, шоколад, масло — «Густая паста».
    ///   3. Проверить шоколад — ✔/⚠/✖, ничего не меняет.
    ///   4. Настроить выделенные (боб / зёрна) — заполняет поля CocoaPod / FoodRoastable / FoodPiece
    ///      на выделенных объектах (компоненты не добавляет).
    /// </summary>
    public static class CatCooksChocolateSetup
    {
        const string RecipeDir = "Assets/CatCooks/Food/Recipes/Resources/CookRecipes";
        const string Pod = "Какао-боб", Beans = "Какао-зёрна", Roasted = "Обжаренные какао-зёрна",
                     Ground = "Тёртое какао", Paste = "Шоколадная паста", Choc = "Шоколад";
        static readonly string[] OldTitles = { "Какао-боб (половинка)", "Какао-крупка", "Обжаренное какао" };

        [MenuItem("CatCooks/Шоколад/1. Создать продукты и рецепты", priority = 70)]
        public static void CreateAll()
        {
            var log = new StringBuilder();
            string dir = ProductsDir();
            ushort next = (ushort)(MaxId() + 1);
            FoodGroupDef veg = FindGroup("Group_Vegetables") ?? GroupOf("Лук репчатый");
            FoodGroupDef bulk = FindGroup("Group_Bulk") ?? GroupOf("Сахар");
            FoodGroupDef sub = FindGroup("Group_SubRecipe") ?? GroupOf("Бульон");

            ProductDef pod = Ensure(Pod, veg, new Color(0.62f, 0.36f, 0.16f), 0.26f, false, dir, ref next, log);
            ProductDef beans = Ensure(Beans, veg, new Color(0.93f, 0.9f, 0.82f), 0.1f, false, dir, ref next, log);
            ProductDef roasted = Ensure(Roasted, bulk, new Color(0.3f, 0.16f, 0.08f), 0.02f, false, dir, ref next, log);
            ProductDef ground = Ensure(Ground, bulk, new Color(0.33f, 0.19f, 0.11f), 0.01f, true, dir, ref next, log);
            ProductDef paste = Ensure(Paste, sub, new Color(0.36f, 0.19f, 0.1f), 0.06f, true, dir, ref next, log);
            ProductDef choc = Ensure(Choc, sub, new Color(0.27f, 0.15f, 0.09f), 0.06f, true, dir, ref next, log);
            ProductDef sugar = Find("Сахар"), butter = Find("Сливочное масло");
            if (sugar == null) log.AppendLine("✖ нет продукта «Сахар»");
            if (butter == null) log.AppendLine("✖ нет продукта «Сливочное масло»");

            if (pod != null)
            {
                if (pod.DebrisColor.a < 0.01f) { pod.DebrisColor = new Color(0.6f, 0.38f, 0.2f, 1f); EditorUtility.SetDirty(pod); }
                // кувалда: целый → «Отбито» (треснул). Битый ещё раз — тоже «Отбито»: удар съедает CocoaPod,
                // а без правила кувалда разнесла бы боб вдребезги.
                foreach (FoodState s in new[] { FoodState.Dirty, FoodState.Raw, FoodState.Washed, FoodState.Pounded })
                    Rule(pod, s, FoodProcess.Crack, FoodState.Pounded, 1f, log);
            }
            if (beans != null && beans.DebrisColor.a < 0.01f) { beans.DebrisColor = new Color(0.95f, 0.93f, 0.86f, 1f); EditorUtility.SetDirty(beans); }

            if (paste != null && choc != null)
            {
                if (paste.HardensInto != choc) { paste.HardensInto = choc; if (paste.HardenSeconds < 1f) paste.HardenSeconds = 90f; EditorUtility.SetDirty(paste); }
                if (!choc.Solid) { choc.Solid = true; EditorUtility.SetDirty(choc); }
                log.AppendLine($"✔ застывание: Шоколадная паста → Шоколад через {paste.HardenSeconds:0} с (поле Harden Seconds у пасты)");
            }

            EnsureDir(RecipeDir);
            if (roasted != null && ground != null)
                SaveRecipe("Тёртое какао", r => CookRecipeBook.CocoaGround(r, roasted, ground), log);
            if (ground != null && sugar != null && butter != null && paste != null)
                SaveRecipe("Шоколадная паста", r => CookRecipeBook.ChocolatePaste(r, ground, sugar, butter, paste), log);

            foreach (string t in OldTitles)
                if (Find(t) != null) log.AppendLine($"ℹ «{t}» — от старой версии, больше не нужен (можно удалить ассет и пересобрать FoodDatabase)");

            AssetDatabase.SaveAssets();
            CookRecipeBook.Reload();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null)
            {
                db.CollectFromProject();
                EditorUtility.SetDirty(db);
                EditorSceneManager.MarkSceneDirty(db.gameObject.scene);
                log.AppendLine("✔ FoodDatabase в сцене пересобран (сохрани сцену)");
            }
            else log.AppendLine("⚠ в открытой сцене нет FoodDatabase — нажми у него «Collect From Project»");
            Debug.Log("[Шоколад]\n" + log);
            EditorUtility.DisplayDialog("Шоколад", log.ToString(), "Ок");
        }

        [MenuItem("CatCooks/Шоколад/2. Добавить пасту в выделенную тару", priority = 71)]
        public static void AddPaste()
        {
            var log = new StringBuilder();
            var targets = new List<CookwareLiquidVisual>();
            foreach (GameObject go in Selection.gameObjects)
                foreach (CookwareLiquidVisual lv in go.GetComponentsInChildren<CookwareLiquidVisual>(true))
                    if (!targets.Contains(lv)) targets.Add(lv);
            if (targets.Count == 0) { EditorUtility.DisplayDialog("Шоколад", "Выдели ковш/ведро (или его LiquidAnchor) и нажми ещё раз.", "Ок"); return; }
            ProductDef paste = Find(Paste), choc = Find(Choc), butter = Find("Сливочное масло");
            foreach (CookwareLiquidVisual lv in targets)
            {
                var so = new SerializedObject(lv);
                SerializedProperty arr = so.FindProperty("_products");
                log.AppendLine($"— {lv.transform.root.name}/{lv.name}");
                if (paste != null) AddEntry(arr, paste, (int)CookwareLiquidVisual.Role.Paste, log);
                if (choc != null) AddEntry(arr, choc, (int)CookwareLiquidVisual.Role.Paste, log);
                if (butter != null) AddEntry(arr, butter, (int)CookwareLiquidVisual.Role.Paste, log);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(lv);
            }
            if (paste == null) log.AppendLine("⚠ сначала «1. Создать продукты и рецепты»");
            Debug.Log("[Шоколад]\n" + log);
            EditorUtility.DisplayDialog("Шоколад", log.ToString(), "Ок");
        }

        [MenuItem("CatCooks/Шоколад/4. Настроить выделенные (боб, зёрна)", priority = 73)]
        public static void SetupSelected()
        {
            var log = new StringBuilder();
            ProductDef pod = Find(Pod), beans = Find(Beans), roasted = Find(Roasted);
            if (pod == null || beans == null || roasted == null)
            { EditorUtility.DisplayDialog("Шоколад", "Сначала «1. Создать продукты и рецепты».", "Ок"); return; }
            int touched = 0;
            foreach (GameObject go in Selection.gameObjects)
            {
                CocoaPod cp = go.GetComponent<CocoaPod>();
                FoodRoastable fr = go.GetComponent<FoodRoastable>();
                FoodPiece fp = go.GetComponent<FoodPiece>();
                if (cp == null && fr == null) { log.AppendLine($"— «{go.name}»: нет CocoaPod / FoodRoastable — пропущен"); continue; }
                touched++;
                if (cp != null)
                {
                    var so = new SerializedObject(cp);
                    so.FindProperty("_pulpProduct").objectReferenceValue = beans;
                    so.FindProperty("_crackedState").intValue = (int)FoodState.Pounded;
                    SerializedProperty sh = so.FindProperty("_shells");
                    if (sh.arraySize == 0)
                    {
                        foreach (MeshRenderer mr in go.GetComponentsInChildren<MeshRenderer>(true))
                            if (mr.name.ToLowerInvariant().Contains("shell"))
                            {
                                sh.arraySize++;
                                sh.GetArrayElementAtIndex(sh.arraySize - 1).objectReferenceValue = mr.transform;
                            }
                    }
                    if (so.FindProperty("_pulp").objectReferenceValue == null)
                        foreach (MeshRenderer mr in go.GetComponentsInChildren<MeshRenderer>(true))
                            if (mr.name.ToLowerInvariant().Contains("pulp")) { so.FindProperty("_pulp").objectReferenceValue = mr.transform; break; }
                    so.ApplyModifiedProperties();
                    if (fp != null) SetPiece(fp, pod, FoodState.Raw);
                    log.AppendLine($"✔ «{go.name}»: Cocoa Pod — скорлупок {sh.arraySize}, Pulp Product = {Beans}" +
                                   (so.FindProperty("_pulpPrefab").objectReferenceValue == null ? "  ⚠ Pulp Prefab задай руками (префаб грозди)" : ""));
                }
                if (fr != null)
                {
                    var so = new SerializedObject(fr);
                    so.FindProperty("_roastedProduct").objectReferenceValue = roasted;
                    SerializedProperty rm = so.FindProperty("_roastedMaterial");
                    if (rm.objectReferenceValue == null)
                    {
                        Material made = MakeRoastedMaterial(go, so.FindProperty("_roastedColor").colorValue, log);
                        if (made != null) rm.objectReferenceValue = made;
                    }
                    so.ApplyModifiedProperties();
                    if (fp != null) SetPiece(fp, beans, FoodState.Raw);
                    log.AppendLine($"✔ «{go.name}»: Food Roastable — Roasted Product = {Roasted}");
                    if (go.GetComponent<FoodSoftBend>() == null)
                    {
                        Undo.AddComponent<FoodSoftBend>(go);
                        log.AppendLine($"＋ «{go.name}»: Food Soft Bend — мягко прогибается, когда несёшь и роняешь");
                    }
                }
                EditorUtility.SetDirty(go);
            }
            if (touched == 0) log.AppendLine("Выдели корень боба (с Cocoa Pod) и/или грозди (с Food Roastable).");
            else log.AppendLine("\nСохрани префабы (Overrides → Apply All).");
            Debug.Log("[Шоколад]\n" + log);
            EditorUtility.DisplayDialog("Шоколад", log.ToString(), "Ок");
        }

        /// <summary>Материал обжаренных: копия материала грозди, тёмно-коричневый, менее глянцевый.</summary>
        private static Material MakeRoastedMaterial(GameObject go, Color roastedColor, StringBuilder log)
        {
            Renderer r = go.GetComponentInChildren<Renderer>(true);
            Material src = r != null ? r.sharedMaterial : null;
            if (src == null) { log.AppendLine($"⚠ «{go.name}»: нет материала у меша — Roasted Material не создан (будет только цвет)"); return null; }
            const string dir = "Assets/Models/Chocolate";
            EnsureDir(dir);
            string path = dir + "/Mat_CocoaRoasted.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(src) { name = "Mat_CocoaRoasted" };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", roastedColor);
                if (m.HasProperty("_Color")) m.SetColor("_Color", roastedColor);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", Mathf.Min(m.GetFloat("_Smoothness"), 0.25f));
                AssetDatabase.CreateAsset(m, path);
                log.AppendLine($"＋ материал обжаренных: {path} (копия «{src.name}», тёмный) — можно поменять цвет/текстуру");
            }
            return m;
        }

        private static void SetPiece(FoodPiece fp, ProductDef p, FoodState st)
        {
            var so = new SerializedObject(fp);
            so.FindProperty("_product").objectReferenceValue = p;
            so.FindProperty("_startState").intValue = (int)st;
            so.ApplyModifiedProperties();
        }

        [MenuItem("CatCooks/Шоколад/3. Проверить шоколад", priority = 72)]
        public static void Check()
        {
            var sb = new StringBuilder();
            _sb = sb; _err = 0; _warn = 0;

            sb.AppendLine("■ Продукты");
            foreach (string t in new[] { Pod, Beans, Roasted, Ground, Paste, Choc, "Сахар", "Сливочное масло" })
            {
                ProductDef p = Find(t);
                if (p == null) Er($"нет «{t}» → «1. Создать продукты и рецепты»"); else Ok($"«{t}» Id {p.Id}");
            }
            ProductDef pod = Find(Pod), beans = Find(Beans), roasted = Find(Roasted), paste = Find(Paste);
            if (pod != null)
            {
                bool okRule = false, badRule = false;
                foreach (FoodRule r in pod.Overrides)
                    if (r.Process == FoodProcess.Crack) { if (r.To == FoodState.Pounded) okRule = true; else badRule = true; }
                if (!okRule) Er("у «Какао-боб» нет правила «Разбивать → Отбито» — меню 1");
                if (badRule) Wr("у «Какао-боб» осталось старое правило «Разбивать → Разбито» — меню 1 его заменит");
            }
            if (paste != null && paste.HardensInto == null) Wr("у «Шоколадная паста» не задано Hardens Into — не застынет");

            sb.AppendLine("\n■ Рецепты");
            foreach (string t in new[] { "Тёртое какао", "Шоколадная паста" })
            {
                CookRecipeDef found = null;
                foreach (string g in AssetDatabase.FindAssets("t:CookRecipeDef"))
                {
                    var r = AssetDatabase.LoadAssetAtPath<CookRecipeDef>(AssetDatabase.GUIDToAssetPath(g));
                    if (r != null && r.Title == t) found = r;
                }
                if (found == null) Wr($"нет ассета «{t}» — книга соберёт сама по названиям");
                else if (t == "Тёртое какао" && (found.Base == null || found.Base.Title != Roasted)) Er($"рецепт «{t}» старый (основа не «{Roasted}») — меню 1 перезапишет");
                else Ok($"рецепт «{t}»");
            }

            sb.AppendLine("\n■ Сцена (перетащи префабы в сцену, чтобы проверить их)");
            int pods = 0, roasts = 0;
            foreach (CocoaPod cp in Object.FindObjectsByType<CocoaPod>(FindObjectsSortMode.None))
            {
                pods++;
                int e0 = _err;
                var so = new SerializedObject(cp);
                int shells = so.FindProperty("_shells").arraySize;
                if (shells == 0)
                    foreach (MeshRenderer mr in cp.GetComponentsInChildren<MeshRenderer>(true))
                        if (mr.name.ToLowerInvariant().Contains("shell")) shells++;
                if (shells < 1) Er($"«{cp.name}»: нет скорлупок (Shells или дочерние с «Shell» в имени)");
                else if (shells == 1) Wr($"«{cp.name}»: одна скорлупка — отпадёт одна");
                var pre = so.FindProperty("_pulpPrefab").objectReferenceValue as GameObject;
                if (pre == null) Er($"«{cp.name}»: не задан Pulp Prefab (гроздь зёрен)");
                else
                {
                    if (pre.GetComponent<FoodRoastable>() == null) Er($"«{cp.name}»: у Pulp Prefab «{pre.name}» нет Food Roastable — не будет жариться");
                    if (pre.GetComponent<FoodPiece>() == null) Er($"«{cp.name}»: у Pulp Prefab «{pre.name}» нет Food Piece");
                    if (pre.GetComponent<FishNet.Object.NetworkObject>() == null) Er($"«{cp.name}»: у Pulp Prefab «{pre.name}» нет Network Object");
                }
                var pp = so.FindProperty("_pulpProduct").objectReferenceValue as ProductDef;
                if (pp != null && pp.Title != Beans) Wr($"«{cp.name}»: Pulp Product = «{pp.Title}», ожидался «{Beans}»");
                if ((FoodState)so.FindProperty("_crackedState").intValue != FoodState.Pounded) Wr($"«{cp.name}»: Cracked State не «Отбито» — правило кувалды ставит «Отбито»");
                FoodPiece fp = cp.GetComponent<FoodPiece>();
                var own = fp != null ? new SerializedObject(fp).FindProperty("_product").objectReferenceValue as ProductDef : null;
                if (own == null || own.Title != Pod) Er($"«{cp.name}»: Food Piece → Product должен быть «{Pod}»");
                if (cp.GetComponent<FoodSplitIntoParts>() != null) Er($"«{cp.name}»: убери Food Split Into Parts (старая схема)");
                if (cp.GetComponentInChildren<Collider>() == null) Er($"«{cp.name}»: нет коллайдера — кувалда и пила не попадут");
                foreach (MeshRenderer mr in cp.GetComponentsInChildren<MeshRenderer>(true))
                {
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null && !mf.sharedMesh.isReadable && mr.name.ToLowerInvariant().Contains("shell"))
                        Wr($"«{cp.name}/{mr.name}»: меш не Read/Write — трещины лягут по габаритам, не по форме");
                }
                if (_err == e0) Ok($"«{cp.name}»: какао-боб → кувалда → пила → гроздь «{(pre != null ? pre.name : "?")}»");
            }
            foreach (FoodRoastable fr in Object.FindObjectsByType<FoodRoastable>(FindObjectsSortMode.None))
            {
                roasts++;
                var so = new SerializedObject(fr);
                var rp = so.FindProperty("_roastedProduct").objectReferenceValue as ProductDef;
                if (rp == null) Er($"«{fr.name}»: Roasted Product пуст → «{Roasted}»");
                else if (rp.Title != Roasted) Wr($"«{fr.name}»: Roasted Product = «{rp.Title}»");
                else Ok($"«{fr.name}»: жарится в «{Roasted}»");
            }
            if (pods == 0) Wr("в сцене нет объекта с Cocoa Pod — перетащи префаб боба, чтобы проверить");
            if (roasts == 0) Wr("в сцене нет объекта с Food Roastable — перетащи префаб грозди, чтобы проверить");
            foreach (CookwareHeat h in Object.FindObjectsByType<CookwareHeat>(FindObjectsSortMode.None))
                Ok($"тара с нагревом: «{h.transform.root.name}»");

            int err = _err, warn = _warn;
            string head = err > 0 ? $"ОШИБОК {err}, предупреждений {warn}" : warn > 0 ? $"ошибок нет, предупреждений {warn}" : "всё в порядке";
            Debug.Log("[Шоколад — проверка] " + head + "\n" + sb);
            EditorUtility.DisplayDialog("Шоколад — проверка", head + "\n" + sb, "Ок");
        }


        private static void Rule(ProductDef p, FoodState from, FoodProcess proc, FoodState to, float sec, StringBuilder log)
        {
            for (int i = 0; i < p.Overrides.Count; i++)
            {
                FoodRule r = p.Overrides[i];
                if (r.From != from || r.Process != proc) continue;
                if (r.To == to) return;
                log.AppendLine($"↻ правило «{p.Title}»: {FoodStateInfo.NameOf(from)} → {FoodStateInfo.NameOf(proc)}: было {FoodStateInfo.NameOf(r.To)}, стало {FoodStateInfo.NameOf(to)}");
                r.To = to; r.Seconds = sec;
                p.Overrides[i] = r;
                EditorUtility.SetDirty(p);
                return;
            }
            p.Overrides.Add(new FoodRule { From = from, Process = proc, To = to, Seconds = sec });
            EditorUtility.SetDirty(p);
            log.AppendLine($"＋ правило «{p.Title}»: {FoodStateInfo.NameOf(from)} → {FoodStateInfo.NameOf(proc)} → {FoodStateInfo.NameOf(to)}");
        }

        // ───────────────────────── помощники ─────────────────────────

        private static StringBuilder _sb;
        private static int _err, _warn;
        private static void Ok(string t) => _sb.AppendLine("  ✔ " + t);
        private static void Wr(string t) { _warn++; _sb.AppendLine("  ⚠ " + t); }
        private static void Er(string t) { _err++; _sb.AppendLine("  ✖ " + t); }

        private static void SaveRecipe(string title, System.Func<CookRecipeDef, CookRecipeDef> fill, StringBuilder log)
        {
            string path = $"{RecipeDir}/Recipe_{title}.asset";
            CookRecipeDef r = AssetDatabase.LoadAssetAtPath<CookRecipeDef>(path);
            bool isNew = r == null;
            if (isNew) r = ScriptableObject.CreateInstance<CookRecipeDef>();
            fill(r);
            if (isNew) AssetDatabase.CreateAsset(r, path); else EditorUtility.SetDirty(r);
            log.AppendLine($"✔ рецепт «{title}» — {path}");
        }

        private static ProductDef Ensure(string title, FoodGroupDef group, Color color, float size, bool sub, string dir,
                                         ref ushort next, StringBuilder log)
        {
            ProductDef p = Find(title);
            if (p != null) { log.AppendLine($"✔ «{title}» уже есть (Id {p.Id})"); return p; }
            if (group == null) { log.AppendLine($"✖ «{title}»: не нашёл группу"); return null; }
            p = ScriptableObject.CreateInstance<ProductDef>();
            p.Id = next++;
            p.Title = title; p.Group = group; p.BaseColor = color; p.Size = size; p.MadeByRecipe = sub;
            string file = "Food_" + title.Replace(" ", "_").Replace("(", "").Replace(")", "") + ".asset";
            AssetDatabase.CreateAsset(p, $"{dir}/{file}");
            log.AppendLine($"＋ «{title}» (Id {p.Id})");
            return p;
        }

        private static bool HasRule(ProductDef p, FoodProcess proc)
        {
            foreach (FoodRule r in p.Overrides) if (r.Process == proc) return true;
            return false;
        }

        private static void AddEntry(SerializedProperty arr, ProductDef p, int role, StringBuilder log)
        {
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("Product").objectReferenceValue == p)
                { log.AppendLine($"  ✔ «{p.Title}» уже в Products"); return; }
            arr.arraySize++;
            SerializedProperty e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
            e.FindPropertyRelative("Product").objectReferenceValue = p;
            e.FindPropertyRelative("Role").intValue = role;
            e.FindPropertyRelative("Color").colorValue = new Color(0, 0, 0, 0);
            log.AppendLine($"  ＋ «{p.Title}» (густая паста)");
        }

        private static ProductDef Find(string title)
        {
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef d = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (d != null && d.Title == title) return d;
            }
            return null;
        }

        private static ushort MaxId()
        {
            ushort m = 0;
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef d = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (d != null && d.Id > m) m = d.Id;
            }
            return m;
        }

        private static string ProductsDir()
        {
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                ProductDef d = AssetDatabase.LoadAssetAtPath<ProductDef>(p);
                if (d != null && (d.Title == "Вода" || d.Title == "Сахар")) return System.IO.Path.GetDirectoryName(p).Replace('\\', '/');
            }
            const string dflt = "Assets/CatCooks/Food/Products";
            EnsureDir(dflt);
            return dflt;
        }

        private static FoodGroupDef FindGroup(string name)
        {
            foreach (string g in AssetDatabase.FindAssets("t:FoodGroupDef " + name))
            {
                FoodGroupDef d = AssetDatabase.LoadAssetAtPath<FoodGroupDef>(AssetDatabase.GUIDToAssetPath(g));
                if (d != null && d.name == name) return d;
            }
            return null;
        }

        private static FoodGroupDef GroupOf(string productTitle)
        {
            ProductDef p = Find(productTitle);
            return p != null ? p.Group : null;
        }

        private static void EnsureDir(string dir)
        {
            string[] parts = dir.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string nx = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(nx)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = nx;
            }
        }
    }
}
#endif
