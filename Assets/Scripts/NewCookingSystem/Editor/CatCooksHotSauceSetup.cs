#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// CatCooks → Острый соус:
    ///   1. Создать продукты и рецепт — недостающие продукты (Перец чили, Уксус, Зубчик чеснока,
    ///      Острая смесь; Соль/Чеснок/Острый соус берутся готовые), правила обработки
    ///      (чили режется бензопилой; чеснок: кувалда → «Отбито», шлифмашинка → «Чищеное»),
    ///      рецепт «Острый соус» в Resources/CookRecipes, пересборка FoodDatabase в сцене.
    ///   2. Добавить жидкости в выделенную тару — в LiquidAnchor выделенного ковша/кастрюли
    ///      роли: Вода, Уксус, Острая смесь — Жидкость; Острый соус — густой (как мёд).
    /// Только ассеты-данные: префабы и сцену (кроме FoodDatabase) не трогает.
    /// </summary>
    public static class CatCooksHotSauceSetup
    {
        const string RecipeDir = "Assets/CatCooks/Food/Recipes/Resources/CookRecipes";

        [MenuItem("CatCooks/Острый соус/1. Создать продукты и рецепт", priority = 60)]
        public static void CreateAll()
        {
            var log = new System.Text.StringBuilder();
            string dir = ProductsDir();
            ushort next = (ushort)(MaxId() + 1);

            FoodGroupDef veg = FindGroup("Group_Vegetables") ?? GroupOf("Лук репчатый");
            FoodGroupDef liq = FindGroup("Group_Liquid") ?? GroupOf("Вода");
            FoodGroupDef bulk = FindGroup("Group_Bulk") ?? GroupOf("Сахар");
            FoodGroupDef sub = FindGroup("Group_SubRecipe") ?? GroupOf("Бульон");

            ProductDef chili = Ensure("Перец чили", veg, new Color(0.86f, 0.09f, 0.05f), 0.08f, false, dir, ref next, log);
            ProductDef vinegar = Ensure("Уксус", liq, new Color(0.95f, 0.92f, 0.78f), 0.08f, false, dir, ref next, log);
            ProductDef salt = Ensure("Соль", bulk, new Color(0.97f, 0.97f, 0.95f), 0.01f, false, dir, ref next, log);
            ProductDef garlic = Ensure("Чеснок", veg, new Color(0.94f, 0.91f, 0.83f), 0.05f, false, dir, ref next, log);
            ProductDef clove = Ensure("Зубчик чеснока", veg, new Color(0.97f, 0.94f, 0.82f), 0.02f, false, dir, ref next, log);
            ProductDef mix = Ensure("Острая смесь", liq, new Color(0.93f, 0.33f, 0.12f), 0.08f, true, dir, ref next, log);
            ProductDef sauce = Ensure("Острый соус", sub, new Color(0.66f, 0.07f, 0.04f), 0.06f, true, dir, ref next, log);
            ProductDef water = Find("Вода");
            if (water == null) log.AppendLine("✖ нет продукта «Вода» — рецепт без основы не собрать");

            // Цвета ошмётков: шелуха чеснока белая, у чили — красные брызги.
            if (garlic != null && garlic.DebrisColor.a < 0.01f) { garlic.DebrisColor = new Color(0.98f, 0.95f, 0.88f, 1f); EditorUtility.SetDirty(garlic); }
            if (chili != null && chili.DebrisColor.a < 0.01f) { chili.DebrisColor = new Color(0.9f, 0.2f, 0.1f, 1f); EditorUtility.SetDirty(chili); }

            // ── правила ──
            if (chili != null)
            {
                Rule(chili, FoodState.Dirty, FoodProcess.Cut, FoodState.Pieces, 2f, log);
                Rule(chili, FoodState.Raw, FoodProcess.Cut, FoodState.Pieces, 2f, log);
            }
            if (garlic != null)
            {
                Rule(garlic, FoodState.Dirty, FoodProcess.Pound, FoodState.Pounded, 1.5f, log);
                Rule(garlic, FoodState.Washed, FoodProcess.Pound, FoodState.Pounded, 1.5f, log);
                Rule(garlic, FoodState.Raw, FoodProcess.Pound, FoodState.Pounded, 1.5f, log);
                // Повторный удар по сплющенной — ничего (иначе кувалда разбила бы её вдребезги).
                Rule(garlic, FoodState.Pounded, FoodProcess.Pound, FoodState.Pounded, 0f, log);
                Rule(garlic, FoodState.Pounded, FoodProcess.Peel, FoodState.Peeled, 2.5f, log);
            }

            // ── рецепт ──
            if (water != null && chili != null && vinegar != null && salt != null && clove != null && sauce != null)
            {
                EnsureDir(RecipeDir);
                string path = $"{RecipeDir}/Recipe_Острый соус.asset";
                CookRecipeDef r = AssetDatabase.LoadAssetAtPath<CookRecipeDef>(path);
                bool isNew = r == null;
                if (isNew) r = ScriptableObject.CreateInstance<CookRecipeDef>();
                CookRecipeBook.HotSauce(r, water, chili, vinegar, salt, clove, mix, sauce);
                if (isNew) AssetDatabase.CreateAsset(r, path); else EditorUtility.SetDirty(r);
                log.AppendLine($"✔ рецепт «Острый соус» — {path}");
                log.AppendLine("   на 1 ведро воды: чили 3 шт., уксус 2 дозы, соль 1 мастерок, чеснок 4 зубчика;");
                log.AppendLine("   1) отбойник 8 с → Острая смесь  2) кипеть 40 с → Острый соус  3) выдержать 10 с");
                Selection.activeObject = r;
            }

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
            else log.AppendLine("⚠ в открытой сцене нет FoodDatabase — у него нажми «Collect From Project» сам");

            Debug.Log("[Острый соус]\n" + log);
            EditorUtility.DisplayDialog("Острый соус", log.ToString(), "Ок");
        }

        [MenuItem("CatCooks/Острый соус/2. Добавить жидкости в выделенную тару", priority = 61)]
        public static void AddLiquids()
        {
            var log = new System.Text.StringBuilder();
            var targets = new List<CookwareLiquidVisual>();
            foreach (GameObject go in Selection.gameObjects)
                foreach (CookwareLiquidVisual lv in go.GetComponentsInChildren<CookwareLiquidVisual>(true))
                    if (!targets.Contains(lv)) targets.Add(lv);
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("Острый соус", "Выдели ковш (или его LiquidAnchor) в сцене и нажми ещё раз.", "Ок");
                return;
            }
            ProductDef water = Find("Вода"), vinegar = Find("Уксус"), mix = Find("Острая смесь"), sauce = Find("Острый соус");
            foreach (CookwareLiquidVisual lv in targets)
            {
                var so = new SerializedObject(lv);
                SerializedProperty arr = so.FindProperty("_products");
                log.AppendLine($"— {lv.transform.root.name}/{lv.name}");
                if (water != null) AddEntry(arr, water, (int)CookwareLiquidVisual.Role.Liquid, log);
                if (vinegar != null) AddEntry(arr, vinegar, (int)CookwareLiquidVisual.Role.Liquid, log);
                if (mix != null) AddEntry(arr, mix, (int)CookwareLiquidVisual.Role.Liquid, log);
                if (sauce != null) AddEntry(arr, sauce, (int)CookwareLiquidVisual.Role.Thick, log);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(lv);
            }
            // жёлтая кучка-превью в редакторе (HeapAnchor) — это не жидкость; выключаем, чтобы не путала
            foreach (GameObject go in Selection.gameObjects)
                foreach (FoodHeapRenderer fr in go.GetComponentsInChildren<FoodHeapRenderer>(true))
                {
                    var hso = new SerializedObject(fr);
                    SerializedProperty pv = hso.FindProperty("_preview");
                    SerializedProperty wu = hso.FindProperty("_worldUpright");
                    bool changed = false;
                    if (pv != null && pv.boolValue) { pv.boolValue = false; changed = true; log.AppendLine($"  ✔ {fr.transform.root.name}/{fr.name}: выключено жёлтое превью кучки (в игре кучка своего цвета)"); }
                    if (wu != null && wu.boolValue && fr.GetComponent<CookwareHeapVisual>() != null)
                    { wu.boolValue = false; changed = true; log.AppendLine($"  ✔ {fr.transform.root.name}/{fr.name}: горка лежит на дне тары (World Upright выкл) — не съезжает при наклоне ковша"); }
                    if (!changed) continue;
                    hso.ApplyModifiedProperties();
                    EditorUtility.SetDirty(fr);
                }
            if (vinegar == null || mix == null || sauce == null) log.AppendLine("⚠ сначала «1. Создать продукты и рецепт»");
            Debug.Log("[Острый соус]\n" + log);
            EditorUtility.DisplayDialog("Острый соус", log.ToString(), "Ок");
        }

        // ───────────────────────── помощники ─────────────────────────

        private static ProductDef Ensure(string title, FoodGroupDef group, Color color, float size, bool sub, string dir,
                                         ref ushort next, System.Text.StringBuilder log)
        {
            ProductDef p = Find(title);
            if (p != null) { log.AppendLine($"✔ «{title}» уже есть (Id {p.Id})"); return p; }
            if (group == null) { log.AppendLine($"✖ «{title}»: не нашёл группу — создай продукт руками"); return null; }
            p = ScriptableObject.CreateInstance<ProductDef>();
            p.Id = next++;
            p.Title = title;
            p.Group = group;
            p.BaseColor = color;
            p.Size = size;
            p.MadeByRecipe = sub;
            string file = "Food_" + title.Replace(" ", "_") + ".asset";
            AssetDatabase.CreateAsset(p, $"{dir}/{file}");
            log.AppendLine($"＋ «{title}» (Id {p.Id}) — {dir}/{file}");
            return p;
        }

        private static void Rule(ProductDef p, FoodState from, FoodProcess proc, FoodState to, float sec, System.Text.StringBuilder log)
        {
            foreach (FoodRule r in p.Overrides)
                if (r.From == from && r.Process == proc) return;
            p.Overrides.Add(new FoodRule { From = from, Process = proc, To = to, Seconds = sec });
            EditorUtility.SetDirty(p);
            log.AppendLine($"＋ правило «{p.Title}»: {FoodStateInfo.NameOf(from)} → {FoodStateInfo.NameOf(proc)} → {FoodStateInfo.NameOf(to)} ({sec:0.#} с)");
        }

        private static void AddEntry(SerializedProperty arr, ProductDef p, int role, System.Text.StringBuilder log)
        {
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("Product").objectReferenceValue == p)
                { log.AppendLine($"  ✔ «{p.Title}» уже в Products"); return; }
            arr.arraySize++;
            SerializedProperty e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
            e.FindPropertyRelative("Product").objectReferenceValue = p;
            e.FindPropertyRelative("Role").intValue = role;
            e.FindPropertyRelative("Color").colorValue = new Color(0, 0, 0, 0);
            log.AppendLine($"  ＋ «{p.Title}» ({(role == (int)CookwareLiquidVisual.Role.Thick ? "густой" : "жидкость")})");
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
                if (d != null && (d.Title == "Вода" || d.Title == "Сахар"))
                    return System.IO.Path.GetDirectoryName(p).Replace('\\', '/');
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
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
#endif
