#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ПОЛУФАБРИКАТЫ: продукты, рецепты и проверка графа.
    ///
    /// Восемь полуфабрикатов первого среза тянут за собой девять продуктов,
    /// которых в срезе не было: сливки, дрожжи, закваска, кости, морковь,
    /// чили, уксус, какао-боб, яичный желток. Плюс сами сливки оказались
    /// полуфабрикатом из молока — то есть появился второй уровень.
    ///
    /// Глубина крафта вышла до трёх: шоколад ← сливочное масло ← сливки ← молоко.
    /// Циклов нет, и проверка ниже это стережёт.
    /// </summary>
    public static class CatCooksSubRecipes
    {
        private const string Root = "Assets/CatCooks/Food";
        private const string ProductsDir = Root + "/Products";
        private const string RecipesDir = Root + "/Recipes";

        [MenuItem("CatCooks/Еда/Создать полуфабрикаты и рецепты", priority = 12)]
        public static void CreateAll()
        {
            EnsureDir(RecipesDir);

            // ── 1. девять продуктов, которых не хватало ──
            ushort id = NextFreeId();
            AddProduct(ref id, "Сливки",           "Group_SubRecipe", 0.97f, 0.95f, 0.88f, 0.08f, true);
            AddProduct(ref id, "Дрожжи",           "Group_Bulk",      0.86f, 0.80f, 0.64f, 0.01f, false);
            AddProduct(ref id, "Закваска",         "Group_Liquid",    0.95f, 0.94f, 0.90f, 0.04f, false);
            AddProduct(ref id, "Кости для бульона","Group_Meat",      0.90f, 0.86f, 0.76f, 0.15f, false);
            AddProduct(ref id, "Морковь",          "Group_Vegetables",0.88f, 0.48f, 0.16f, 0.04f, false);
            AddProduct(ref id, "Перец чили",       "Group_Vegetables",0.78f, 0.14f, 0.10f, 0.03f, false);
            AddProduct(ref id, "Уксус",            "Group_Liquid",    0.86f, 0.80f, 0.60f, 0.06f, false);
            AddProduct(ref id, "Какао-боб",        "Group_Grain",     0.38f, 0.24f, 0.16f, 0.02f, false);
            AddProduct(ref id, "Яичный желток",    "Group_Egg",       0.96f, 0.76f, 0.20f, 0.02f, false);

            AssetDatabase.SaveAssets();

            // ── 2. рецепты, в порядке готовности ──
            // Порядок посчитан топологически: то, что ни от кого не зависит, первым.
            MakeRecipe("Recipe_Bulion", "Бульон", FoodProcess.Boil, 120f, "Бульон", 2,
                "Вываривается из костей. Основа для супов — без него борщ не соберётся.",
                N("Кости для бульона", FoodState.Butchered, 1),
                N("Вода", FoodState.Poured, 2),
                N("Лук репчатый", FoodState.Peeled, 1),
                N("Морковь", FoodState.Peeled, 1));

            MakeRecipe("Recipe_Farsh", "Говяжий фарш", FoodProcess.Mince, 8f, "Говяжий фарш", 1,
                "Измельчитель. Единственный полуфабрикат без нагрева.",
                N("Говядина", FoodState.Butchered, 1));

            MakeRecipe("Recipe_HotSauce", "Острый соус", FoodProcess.Boil, 40f, "Острый соус", 1,
                "Чили пробивается и проваривается с уксусом.",
                N("Перец чили", FoodState.Pieces, 2),
                N("Уксус", FoodState.Poured, 1),
                N("Соль", FoodState.Scooped, 1),
                N("Чеснок", FoodState.Peeled, 1));

            MakeRecipe("Recipe_Slivki", "Сливки", FoodProcess.Chill, 60f, "Сливки", 1,
                "Отстаиваются из молока. ВТОРОЙ УРОВЕНЬ: сами идут в масло, " +
                "сметану и мороженое.",
                N("Молоко", FoodState.Poured, 3));

            MakeRecipe("Recipe_Hleb", "Хлеб", FoodProcess.Bake, 90f, "Хлеб", 2,
                "Замес, расстойка, духовка. Из него потом сухари.",
                N("Мука пшеничная", FoodState.Scooped, 3),
                N("Вода", FoodState.Poured, 1),
                N("Дрожжи", FoodState.Scooped, 1),
                N("Соль", FoodState.Scooped, 1));

            MakeRecipe("Recipe_Morozhenoe", "Мороженое", FoodProcess.Freeze, 120f, "Мороженое", 2,
                "Заварная основа, потом заморозка с помешиванием.",
                N("Сливки", FoodState.Poured, 2),
                N("Молоко", FoodState.Poured, 1),
                N("Яичный желток", FoodState.Yolk, 2),
                N("Сахар", FoodState.Scooped, 1));

            MakeRecipe("Recipe_Maslo", "Сливочное масло", FoodProcess.Whip, 45f, "Сливочное масло", 1,
                "Сливки взбиваются до отделения пахты.",
                N("Сливки", FoodState.Poured, 3));

            MakeRecipe("Recipe_Smetana", "Сметана", FoodProcess.Chill, 180f, "Сметана", 2,
                "Сливки с закваской, долгая выдержка.",
                N("Сливки", FoodState.Poured, 2),
                N("Закваска", FoodState.Poured, 1));

            MakeRecipe("Recipe_Shokolad", "Шоколад", FoodProcess.Mix, 60f, "Шоколад", 1,
                "САМЫЙ ГЛУБОКИЙ: бобы обжариваются, мелются и вымешиваются с маслом. " +
                "Через масло тянет за собой сливки и молоко — три уровня.",
                N("Какао-боб", FoodState.PileDry, 3),
                N("Сахар", FoodState.Scooped, 1),
                N("Сливочное масло", FoodState.Ready, 1));

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Еда] Полуфабрикаты созданы: 9 продуктов, 9 рецептов.\n" +
                      "Проверь граф: CatCooks → Еда → Проверить рецепты.");
            EditorUtility.DisplayDialog("Готово",
                "Добавлено 9 продуктов и 9 рецептов.\n\n" +
                "Продуктов в срезе стало 38. Глубина крафта — до трёх уровней.\n" +
                "Модели по-прежнему не нужны.", "Ясно");
        }

        // ───────────────────────── ПРОВЕРКА ГРАФА ─────────────────────────

        /// <summary>
        /// Ищет циклы и считает глубину. Гонять после каждой правки рецептов:
        /// один случайно закольцованный рецепт подвесит подсказки намертво,
        /// а найти его вручную среди полутора сотен невозможно.
        /// </summary>
        [MenuItem("CatCooks/Еда/Проверить рецепты", priority = 13)]
        public static void CheckRecipes()
        {
            List<FoodRecipeDef> all = LoadAll<FoodRecipeDef>();
            Dictionary<ProductDef, FoodRecipeDef> madeBy = new Dictionary<ProductDef, FoodRecipeDef>();

            foreach (FoodRecipeDef r in all)
            {
                if (!r.IsSubRecipe) continue;
                if (madeBy.ContainsKey(r.ResultProduct))
                {
                    Debug.LogWarning($"[Еда] «{r.ResultProduct.Title}» делается двумя " +
                                     $"рецептами: «{madeBy[r.ResultProduct].Title}» и «{r.Title}». " +
                                     "Подбор возьмёт первый попавшийся.", r);
                    continue;
                }
                madeBy[r.ResultProduct] = r;
            }

            StringBuilder sb = new StringBuilder();
            int maxDepth = 0, cycles = 0;

            foreach (FoodRecipeDef r in all)
            {
                HashSet<FoodRecipeDef> path = new HashSet<FoodRecipeDef>();
                int d = Depth(r, madeBy, path, out bool cycle);
                if (cycle)
                {
                    sb.AppendLine($"ЦИКЛ · «{r.Title}» зависит сам от себя. " +
                                  "Подсказки зависнут.");
                    cycles++;
                }
                else if (d > maxDepth) maxDepth = d;
            }

            foreach (FoodRecipeDef r in all)
                foreach (FoodNeed n in r.Needs)
                    if (n.Product == null)
                        sb.AppendLine($"ПУСТО · у «{r.Title}» требование без продукта");
                    else if (n.Product.MadeByRecipe && !madeBy.ContainsKey(n.Product))
                        sb.AppendLine($"НЕТ РЕЦЕПТА · «{r.Title}» требует " +
                                      $"«{n.Product.Title}», он помечен как полуфабрикат, " +
                                      "но рецепта для него нет — достать неоткуда");

            string head = $"[Еда] Рецептов {all.Count}, из них полуфабрикатов {madeBy.Count}. " +
                          $"Глубина крафта {maxDepth}. Циклов {cycles}.";
            if (sb.Length > 0) Debug.LogWarning(head + "\n" + sb);
            else Debug.Log(head + "  Всё сходится.");
        }

        private static int Depth(FoodRecipeDef r, Dictionary<ProductDef, FoodRecipeDef> madeBy,
                                 HashSet<FoodRecipeDef> path, out bool cycle)
        {
            cycle = false;
            if (!path.Add(r)) { cycle = true; return 0; }

            int best = 0;
            foreach (FoodNeed n in r.Needs)
            {
                if (n.Product == null) continue;
                if (!madeBy.TryGetValue(n.Product, out FoodRecipeDef sub)) continue;
                int d = Depth(sub, madeBy, path, out bool c);
                if (c) { cycle = true; path.Remove(r); return 0; }
                if (d + 1 > best) best = d + 1;
            }
            path.Remove(r);
            return best;
        }

        // ───────────────────────── МЕЛОЧИ ─────────────────────────

        private struct NeedSpec { public string product; public FoodState state; public int count; }

        private static NeedSpec N(string product, FoodState state, int count)
        {
            return new NeedSpec { product = product, state = state, count = count };
        }

        private static void MakeRecipe(string file, string title, FoodProcess proc,
                                       float sec, string result, int resultCount,
                                       string note, params NeedSpec[] needs)
        {
            string path = $"{RecipesDir}/{file}.asset";
            FoodRecipeDef r = AssetDatabase.LoadAssetAtPath<FoodRecipeDef>(path);
            if (r == null)
            {
                r = ScriptableObject.CreateInstance<FoodRecipeDef>();
                AssetDatabase.CreateAsset(r, path);
            }
            r.Title = title;
            r.Note = note;
            r.Process = proc;
            r.Seconds = sec;
            r.ResultProduct = FindProduct(result);
            r.ResultCount = resultCount;
            r.Needs = new List<FoodNeed>();
            foreach (NeedSpec n in needs)
            {
                ProductDef p = FindProduct(n.product);
                if (p == null)
                {
                    Debug.LogWarning($"[Еда] Рецепт «{title}»: продукт «{n.product}» не найден. " +
                                     "Сперва создай базу первого среза.");
                    continue;
                }
                r.Needs.Add(new FoodNeed { Product = p, State = n.state, Count = n.count });
            }
            EditorUtility.SetDirty(r);
        }

        private static void AddProduct(ref ushort id, string title, string groupFile,
                                       float cr, float cg, float cb, float size, bool sub)
        {
            string safe = title.Replace(" ", "_");
            string path = $"{ProductsDir}/Food_{safe}.asset";
            ProductDef p = AssetDatabase.LoadAssetAtPath<ProductDef>(path);
            if (p == null)
            {
                p = ScriptableObject.CreateInstance<ProductDef>();
                AssetDatabase.CreateAsset(p, path);
            }
            if (p.Id == 0) { p.Id = id; id++; }
            p.Title = title;
            p.BaseColor = new Color(cr, cg, cb);
            p.Size = size;
            p.MadeByRecipe = sub;
            if (p.Group == null)
                p.Group = AssetDatabase.LoadAssetAtPath<FoodGroupDef>(
                    $"{Root}/Groups/{groupFile}.asset");
            EditorUtility.SetDirty(p);
        }

        private static ProductDef FindProduct(string title)
        {
            foreach (ProductDef p in LoadAll<ProductDef>())
                if (p.Title == title) return p;
            return null;
        }

        /// <summary>Следующий свободный Id. Занятые не переиспользуются:
        /// по Id летит сеть, и повторное использование склеит разные продукты
        /// в сохранёнках.</summary>
        private static ushort NextFreeId()
        {
            ushort max = 0;
            foreach (ProductDef p in LoadAll<ProductDef>())
                if (p.Id > max) max = p.Id;
            return (ushort)(max + 1);
        }

        private static List<T> LoadAll<T>() where T : Object
        {
            List<T> list = new List<T>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                T a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (a != null) list.Add(a);
            }
            return list;
        }

        private static void EnsureDir(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir)) return;
            string[] parts = dir.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
#endif
