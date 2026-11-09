#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// CatCooks → Рецепты:
    ///   • Создать базовые рецепты — Сливки, Сливочное масло, Сметана, Мороженое
    ///     (ассеты CookRecipeDef в Resources/CookRecipes; правятся в инспекторе).
    ///   • Создать рецепт бульона — вода доверху + 5 луковиц + 5 морковок + 10 костей, варить.
    ///   • Создать меры — ассет FoodUnits в Resources (ведро = 30 порций, мастерок = 2).
    /// Дальше рецепты правятся прямо в инспекторе ассета.
    /// </summary>
    public static class CatCooksRecipeSetup
    {
        const string Dir = "Assets/CatCooks/Food/Recipes/Resources/CookRecipes";
        const string UnitsDir = "Assets/CatCooks/Food/Recipes/Resources";

        [MenuItem("CatCooks/Рецепты/Создать базовые рецепты (сливки, масло, сметана, мороженое)", priority = 1)]
        public static void Basics()
        {
            EnsureDir(Dir);
            ProductDef milk = Find("Молоко"), cream = Find("Сливки"), butter = Find("Сливочное масло");
            ProductDef sour = Find("Сметана"), starter = Find("Закваска");
            ProductDef sugar = Find("Сахар"), egg = Find("Яйцо"), yolk = Find("Яичный желток");
            ProductDef ice = Find("Мороженое"), mix = Find("Смесь для мороженого");
            var log = new System.Text.StringBuilder();

            if (milk != null && cream != null)
            {
                CookRecipeDef r = Get("Сливки");
                Base(r, "Сливки", CookDriver.Vibrator, milk, cream);
                r.Ingredients.Clear();
                Steps(r, new CookRecipeDef.Step { Kind = CookStepKind.Whip, Seconds = 45f });
                Save(r, "Сливки", log);
            }
            else log.AppendLine("✖ Сливки: нет Молоко/Сливки");

            if (cream != null && butter != null)
            {
                CookRecipeDef r = Get("Сливочное масло");
                Base(r, "Сливочное масло", CookDriver.Vibrator, cream, butter);
                r.Ingredients.Clear();
                Steps(r, new CookRecipeDef.Step { Kind = CookStepKind.Whip, Seconds = 30f });
                Save(r, "Сливочное масло", log);
            }
            else log.AppendLine("✖ Масло: нет Сливки/Сливочное масло");

            if (cream != null && sour != null && starter != null)
            {
                CookRecipeDef r = Get("Сметана");
                Base(r, "Сметана", CookDriver.Culture, cream, sour);
                r.Tolerance = 0.15f;
                r.BaseMinFill = 0.85f;
                r.Ingredients.Clear();
                r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = starter, Amount = 1f, Unit = CookUnit.Dose });
                Steps(r, new CookRecipeDef.Step { Kind = CookStepKind.Ferment, Seconds = 45f });
                Save(r, "Сметана", log);
            }
            else log.AppendLine("✖ Сметана: нет Сливки/Сметана/Закваска");

            if (cream != null && sugar != null && egg != null && ice != null)
            {
                CookRecipeDef r = Get("Мороженое");
                Base(r, "Мороженое", CookDriver.Book, cream, ice);
                r.Ingredients.Clear();
                r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = sugar, Amount = 1f, Unit = CookUnit.Trowel });
                r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = egg, Alt = yolk, Amount = 1f, Unit = CookUnit.Piece });
                Steps(r, new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = 6f, Intermediate = mix },
                         new CookRecipeDef.Step { Kind = CookStepKind.Freeze, Seconds = 2.5f });
                Save(r, "Мороженое", log);
                if (mix == null) log.AppendLine("  · «Смеси для мороженого» нет — после замеса тара просто помнит шаг (CatCooks → Еда → Создать «Смесь для мороженого»)");
            }
            else log.AppendLine("✖ Мороженое: нет Сливки/Сахар/Яйцо/Мороженое");

            AssetDatabase.SaveAssets();
            CookRecipeBook.Reload();
            Debug.Log("[Рецепты]\n" + log);
            EditorUtility.DisplayDialog("Рецепты", log.ToString(), "Ок");
        }

        [MenuItem("CatCooks/Рецепты/Создать рецепт бульона", priority = 3)]
        public static void BrothRecipe()
        {
            EnsureDir(Dir);
            var log = new System.Text.StringBuilder();
            ProductDef water = Find("Вода"), onion = Find("Лук репчатый"), onionRed = Find("Лук красный");
            ProductDef carrot = Find("Морковь"), bones = Find("Кости для бульона"), broth = Find("Бульон");
            if (water != null && onion != null && carrot != null && bones != null && broth != null)
            {
                CookRecipeDef r = Get("Бульон");
                CookRecipeBook.Broth(r, water, onion, onionRed, carrot, bones, broth);
                Save(r, "Бульон", log);
                if (onionRed == null) log.AppendLine("  · «Лук красный» не найден — пока только репчатый (Alt у лука задашь потом)");
                AssetDatabase.SaveAssets();
                CookRecipeBook.Reload();
                Selection.activeObject = r;
            }
            else
            {
                log.AppendLine("✖ Нет продукта: " +
                               (water == null ? "«Вода» " : "") + (onion == null ? "«Лук репчатый» " : "") +
                               (carrot == null ? "«Морковь» " : "") + (bones == null ? "«Кости для бульона» " : "") +
                               (broth == null ? "«Бульон»" : ""));
            }
            Debug.Log("[Рецепты]\n" + log);
            EditorUtility.DisplayDialog("Рецепт бульона", log.ToString(), "Ок");
        }

        private static CookRecipeDef Get(string title)
        {
            string path = $"{Dir}/Recipe_{title}.asset";
            CookRecipeDef r = AssetDatabase.LoadAssetAtPath<CookRecipeDef>(path);
            return r != null ? r : ScriptableObject.CreateInstance<CookRecipeDef>();
        }

        private static void Base(CookRecipeDef r, string title, CookDriver driver, ProductDef from, ProductDef result)
        {
            r.Title = title; r.Driver = driver;
            r.Base = from; r.BaseAmount = 1f; r.BaseUnit = CookUnit.Bucket; r.BaseFillsContainer = true; r.BaseMinFill = 0.9f;
            r.OrderMatters = false; r.OnlyThese = true; r.Tolerance = 0f;
            r.Result = result;
        }

        private static void Steps(CookRecipeDef r, params CookRecipeDef.Step[] steps)
        {
            r.Steps.Clear();
            r.Steps.AddRange(steps);
        }

        private static void Save(CookRecipeDef r, string title, System.Text.StringBuilder log)
        {
            string path = $"{Dir}/Recipe_{title}.asset";
            if (AssetDatabase.LoadAssetAtPath<CookRecipeDef>(path) == null) AssetDatabase.CreateAsset(r, path);
            else EditorUtility.SetDirty(r);
            log.AppendLine($"✔ {title} — {path}");
        }

        [MenuItem("CatCooks/Рецепты/Создать меры (вёдра, мастерки)", priority = 2)]
        public static void Units()
        {
            EnsureDir(UnitsDir);
            string path = $"{UnitsDir}/FoodUnits.asset";
            if (AssetDatabase.LoadAssetAtPath<FoodUnits>(path) == null)
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<FoodUnits>(), path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<FoodUnits>(path);
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
