#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЗАПОЛНЕНИЕ БАЗЫ ОДНОЙ КНОПКОЙ.
    ///
    /// Создаёт группы с правилами и продукты первого среза — 10 блюд,
    /// 29 продуктов. Модели НЕ НУЖНЫ: ассеты создаются с пустыми полями меша,
    /// система собирается и проверяется без единого полигона. Меши и цвета
    /// подцепляются потом, они ни на что в логике не влияют.
    ///
    /// Цифры правил взяты из разбора 126 рецептов, а не из головы.
    /// </summary>
    public static class CatCooksFoodSetup
    {
        private const string Root = "Assets/CatCooks/Food";
        private const string GroupsDir = Root + "/Groups";
        private const string ProductsDir = Root + "/Products";

        [MenuItem("CatCooks/Еда/Создать базу первого среза", priority = 10)]
        public static void CreateAll()
        {
            EnsureDir(GroupsDir);
            EnsureDir(ProductsDir);

            Dictionary<string, FoodGroupDef> groups = new Dictionary<string, FoodGroupDef>();

            groups["Овощи"] = MakeGroup("Group_Vegetables", "Овощи и фрукты",
                "Самая богатая группа: 22 состояния. Всё, что моется, чистится, " +
                "режется и трётся.",
                FoodState.Dirty, VegetableRules());

            groups["Мясо"] = MakeGroup("Group_Meat", "Мясо и рыба",
                "Туша приходит целиком и разделывается пилой на части под блюдо.",
                FoodState.Carcass, MeatRules());

            groups["Специи"] = MakeGroup("Group_Spices", "Специи",
                "Состояний готовки нет. Добавляется и исчезает, обновляя смесь.",
                FoodState.InContainer, SpiceRules());

            groups["Бакалея"] = MakeGroup("Group_Bulk", "Бакалея сыпучая",
                "Мешок, черпается лопатой. Уходит в смесь или в бетономешалку.",
                FoodState.InSack, BulkRules());

            groups["Крупы"] = MakeGroup("Group_Grain", "Крупы",
                "Единственная сыпучая группа с нагревом: кучка набухает и меняет цвет.",
                FoodState.InSack, GrainRules());

            groups["Яйцо"] = MakeGroup("Group_Egg", "Яйцо",
                "Своя группа из-за отделения белка от желтка.",
                FoodState.InShell, EggRules());

            groups["Жидкое"] = MakeGroup("Group_Liquid", "Жидкое из тары",
                "Бутылка или банка. Льётся и обновляет состояние смеси.",
                FoodState.InContainer, LiquidRules());

            groups["Сыр"] = MakeGroup("Group_Cheese", "Сыр",
                "Сорта разные, поведение одно: головка, тереть, растопить.",
                FoodState.Raw, CheeseRules());

            groups["Полуфабрикат"] = MakeGroup("Group_SubRecipe", "Полуфабрикат",
                "Не покупается. Появляется только как результат своего рецепта.",
                FoodState.Ready, SubRules());

            // ── продукты первого среза ──
            ushort id = 1;
            foreach (var p in SliceProducts())
                MakeProduct(ref id, p.title, groups[p.group], p.color, p.size, p.sub);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Еда] База создана: {groups.Count} групп, {id - 1} продуктов.\n" +
                      "Модели не назначены — так и задумано. Проверь таблицу через " +
                      "CatCooks → Еда → Проверить таблицу.");
            EditorUtility.DisplayDialog("Готово",
                $"Создано {groups.Count} групп и {id - 1} продуктов.\n\n" +
                "Меши и цвета можно назначить завтра — система работает без них.",
                "Ясно");
        }

        // ───────────────────────── ПРАВИЛА ─────────────────────────

        private static FoodRule R(FoodState from, FoodProcess proc, FoodState to,
                                  float sec = 0f, float win = 0f,
                                  FoodState early = FoodState.None,
                                  FoodState late = FoodState.None)
        {
            return new FoodRule
            {
                From = from, Process = proc, To = to,
                Seconds = sec, Window = win, TooEarly = early, TooLate = late
            };
        }

        /// <summary>Овощи: мыть, чистить, резать, тереть, толочь и четыре нагрева.
        /// Подготовка мгновенная и без окна — «перемыть» нельзя.</summary>
        private static List<FoodRule> VegetableRules()
        {
            return new List<FoodRule>
            {
                R(FoodState.Dirty,  FoodProcess.Wash,  FoodState.Washed, 1.5f),
                R(FoodState.Washed, FoodProcess.Peel,  FoodState.Peeled, 2.5f),
                R(FoodState.Dirty,  FoodProcess.Peel,  FoodState.Peeled, 3.5f),

                R(FoodState.Peeled, FoodProcess.Cut,   FoodState.Pieces, 2f),
                R(FoodState.Washed, FoodProcess.Cut,   FoodState.Pieces, 2f),
                R(FoodState.Peeled, FoodProcess.Grate, FoodState.Grated, 2.5f),
                R(FoodState.Peeled, FoodProcess.Mash,  FoodState.Puree,  3f),
                R(FoodState.Boiled, FoodProcess.Mash,  FoodState.Puree,  2f),

                // нагрев: окно есть, недо и пере заданы
                R(FoodState.Pieces, FoodProcess.Boil, FoodState.Boiled, 20f, 0.35f,
                  FoodState.Undercooked, FoodState.Overcooked),
                R(FoodState.Pieces, FoodProcess.Fry,  FoodState.Fried,  14f, 0.30f,
                  FoodState.Undercooked, FoodState.Overcooked),
                R(FoodState.Pieces, FoodProcess.Bake, FoodState.Baked,  35f, 0.30f,
                  FoodState.Undercooked, FoodState.Overcooked),
                R(FoodState.Pieces, FoodProcess.Stew, FoodState.Stewed, 45f, 0.40f,
                  FoodState.Undercooked, FoodState.Overcooked),

                // из пере — в уголь, и это смешно
                R(FoodState.Overcooked, FoodProcess.Fry,  FoodState.Burnt, 8f),
                R(FoodState.Overcooked, FoodProcess.Bake, FoodState.Burnt, 12f),
            };
        }

        private static List<FoodRule> MeatRules()
        {
            return new List<FoodRule>
            {
                R(FoodState.Carcass,   FoodProcess.Butcher,  FoodState.Butchered, 5f),
                R(FoodState.Butchered, FoodProcess.Cut,      FoodState.Pieces,    2.5f),
                R(FoodState.Butchered, FoodProcess.Mince,    FoodState.Minced,    4f),
                R(FoodState.Butchered, FoodProcess.Marinate, FoodState.Marinated, 30f),
                R(FoodState.Butchered, FoodProcess.Bread,    FoodState.Breaded,   2f),

                R(FoodState.Butchered, FoodProcess.Boil,  FoodState.Boiled,  60f, 0.30f,
                  FoodState.Undercooked, FoodState.Overcooked),
                R(FoodState.Butchered, FoodProcess.Fry,   FoodState.Fried,   18f, 0.25f,
                  FoodState.Undercooked, FoodState.Overcooked),
                R(FoodState.Butchered, FoodProcess.Bake,  FoodState.Baked,   50f, 0.30f,
                  FoodState.Undercooked, FoodState.Overcooked),
                R(FoodState.Butchered, FoodProcess.Stew,  FoodState.Stewed,  90f, 0.40f,
                  FoodState.Undercooked, FoodState.Overcooked),
                R(FoodState.Marinated, FoodProcess.Spit,  FoodState.Spitted, 40f, 0.30f,
                  FoodState.Undercooked, FoodState.Overcooked),
                R(FoodState.Butchered, FoodProcess.Smoke, FoodState.Smoked, 180f, 0.45f,
                  FoodState.Undercooked, FoodState.Overcooked),

                R(FoodState.Overcooked, FoodProcess.Fry, FoodState.Burnt, 10f),
            };
        }

        private static List<FoodRule> SpiceRules()
        {
            return new List<FoodRule>
            {
                R(FoodState.InContainer, FoodProcess.Scoop, FoodState.Dust, 0.5f),
                R(FoodState.InContainer, FoodProcess.Grate, FoodState.Dust, 1.5f),
                R(FoodState.Dust,        FoodProcess.Mix,   FoodState.Dissolved, 0.3f),
            };
        }

        private static List<FoodRule> BulkRules()
        {
            return new List<FoodRule>
            {
                R(FoodState.InSack,  FoodProcess.Scoop, FoodState.Scooped, 0.8f),
                R(FoodState.Scooped, FoodProcess.Sift,  FoodState.PileDry, 2f),
                R(FoodState.Scooped, FoodProcess.Mix,   FoodState.Dissolved, 0.5f),
                R(FoodState.PileDry, FoodProcess.Mix,   FoodState.Dissolved, 0.5f),
            };
        }

        private static List<FoodRule> GrainRules()
        {
            return new List<FoodRule>
            {
                R(FoodState.InSack,  FoodProcess.Scoop, FoodState.PileDry, 0.8f),
                R(FoodState.PileDry, FoodProcess.Boil,  FoodState.PileSwollen, 25f, 0.35f,
                  FoodState.Undercooked, FoodState.Overcooked),
            };
        }

        private static List<FoodRule> EggRules()
        {
            return new List<FoodRule>
            {
                R(FoodState.InShell, FoodProcess.Crack,    FoodState.Cracked, 0.5f),
                R(FoodState.Cracked, FoodProcess.Separate, FoodState.White,   1.5f),
                R(FoodState.Cracked, FoodProcess.Whip,     FoodState.Whipped, 4f),
                R(FoodState.White,   FoodProcess.Whip,     FoodState.Whipped, 6f),
                R(FoodState.InShell, FoodProcess.Boil,     FoodState.Boiled,  8f, 0.35f,
                  FoodState.Undercooked, FoodState.Overcooked),
                R(FoodState.Cracked, FoodProcess.Fry,      FoodState.Fried,   6f, 0.30f,
                  FoodState.Undercooked, FoodState.Overcooked),
                R(FoodState.Boiled,  FoodProcess.Peel,     FoodState.Peeled,  2f),
                R(FoodState.Peeled,  FoodProcess.Cut,      FoodState.Pieces,  1.5f),
            };
        }

        private static List<FoodRule> LiquidRules()
        {
            return new List<FoodRule>
            {
                R(FoodState.InContainer, FoodProcess.Pour, FoodState.Poured, 1f),
                R(FoodState.Poured,      FoodProcess.Mix,  FoodState.Dissolved, 0.5f),
            };
        }

        private static List<FoodRule> CheeseRules()
        {
            return new List<FoodRule>
            {
                R(FoodState.Raw,    FoodProcess.Grate, FoodState.Grated, 2.5f),
                R(FoodState.Raw,    FoodProcess.Cut,   FoodState.Pieces, 1.5f),
                R(FoodState.Grated, FoodProcess.Bake,  FoodState.Melted, 8f, 0.35f,
                  FoodState.Undercooked, FoodState.Burnt),
            };
        }

        private static List<FoodRule> SubRules()
        {
            // Полуфабрикат приходит готовым. Дальше его можно только лить,
            // мешать и греть — как обычный компонент.
            return new List<FoodRule>
            {
                R(FoodState.Ready, FoodProcess.Pour, FoodState.Poured, 1f),
                R(FoodState.Ready, FoodProcess.Mix,  FoodState.Dissolved, 0.5f),
                R(FoodState.Ready, FoodProcess.Cut,  FoodState.Pieces, 2f),
            };
        }

        // ───────────────────────── ПРОДУКТЫ СРЕЗА ─────────────────────────

        private struct P
        {
            public string title, group; public Color color; public float size; public bool sub;
            public P(string t, string g, float r, float gr, float b, float s, bool sb = false)
            { title = t; group = g; color = new Color(r, gr, b); size = s; sub = sb; }
        }

        /// <summary>29 продуктов первого среза. Цвета приблизительные — они
        /// нужны только чтобы в сцене было видно разницу, художник заменит.</summary>
        private static List<P> SliceProducts()
        {
            return new List<P>
            {
                new P("Картофель",        "Овощи",  0.85f, 0.72f, 0.42f, 0.05f),
                new P("Лук репчатый",     "Овощи",  0.90f, 0.82f, 0.60f, 0.05f),
                new P("Чеснок",           "Овощи",  0.92f, 0.90f, 0.82f, 0.02f),
                new P("Помидор",          "Овощи",  0.80f, 0.20f, 0.14f, 0.05f),

                new P("Говядина",         "Мясо",   0.62f, 0.22f, 0.20f, 0.20f),
                new P("Свинина",          "Мясо",   0.80f, 0.45f, 0.42f, 0.20f),

                new P("Соль",             "Бакалея", 0.96f, 0.96f, 0.94f, 0.01f),
                new P("Сахар",            "Бакалея", 0.95f, 0.93f, 0.88f, 0.01f),
                new P("Мука пшеничная",   "Бакалея", 0.93f, 0.89f, 0.78f, 0.01f),
                new P("Мука миндальная",  "Бакалея", 0.90f, 0.84f, 0.70f, 0.01f),

                new P("Перец чёрный",     "Специи", 0.22f, 0.18f, 0.15f, 0.01f),
                new P("Паприка",          "Специи", 0.75f, 0.25f, 0.12f, 0.01f),
                new P("Шалфей",           "Специи", 0.55f, 0.62f, 0.45f, 0.02f),
                new P("Ваниль",           "Специи", 0.45f, 0.33f, 0.22f, 0.02f),

                new P("Панировочные сухари", "Крупы", 0.80f, 0.65f, 0.42f, 0.01f),

                new P("Яйцо",             "Яйцо",   0.94f, 0.88f, 0.76f, 0.03f),
                new P("Яичный белок",     "Яйцо",   0.96f, 0.95f, 0.90f, 0.03f),

                new P("Молоко",           "Жидкое", 0.97f, 0.96f, 0.92f, 0.10f),
                new P("Вода",             "Жидкое", 0.70f, 0.85f, 0.92f, 0.10f),
                new P("Горчица",          "Жидкое", 0.85f, 0.72f, 0.25f, 0.06f),

                new P("Сыр твёрдый",      "Сыр",    0.93f, 0.80f, 0.42f, 0.10f),

                new P("Сливочное масло",  "Полуфабрикат", 0.96f, 0.90f, 0.62f, 0.06f, true),
                new P("Сметана",          "Полуфабрикат", 0.97f, 0.96f, 0.93f, 0.08f, true),
                new P("Говяжий фарш",     "Полуфабрикат", 0.70f, 0.28f, 0.25f, 0.08f, true),
                new P("Хлеб",             "Полуфабрикат", 0.78f, 0.60f, 0.38f, 0.12f, true),
                new P("Бульон",           "Полуфабрикат", 0.80f, 0.62f, 0.30f, 0.10f, true),
                new P("Острый соус",      "Полуфабрикат", 0.72f, 0.18f, 0.10f, 0.06f, true),
                new P("Шоколад",          "Полуфабрикат", 0.30f, 0.18f, 0.12f, 0.06f, true),
                new P("Мороженое",        "Полуфабрикат", 0.95f, 0.92f, 0.85f, 0.08f, true),
            };
        }

        // ───────────────────────── СОЗДАНИЕ АССЕТОВ ─────────────────────────

        private static FoodGroupDef MakeGroup(string file, string title, string note,
                                              FoodState start, List<FoodRule> rules)
        {
            string path = $"{GroupsDir}/{file}.asset";
            FoodGroupDef g = AssetDatabase.LoadAssetAtPath<FoodGroupDef>(path);
            if (g == null)
            {
                g = ScriptableObject.CreateInstance<FoodGroupDef>();
                AssetDatabase.CreateAsset(g, path);
            }
            g.Title = title;
            g.Note = note;
            g.StartState = start;
            g.Rules = rules;
            EditorUtility.SetDirty(g);
            return g;
        }

        private static void MakeProduct(ref ushort id, string title, FoodGroupDef group,
                                        Color color, float size, bool sub)
        {
            string safe = title.Replace(" ", "_");
            string path = $"{ProductsDir}/Food_{safe}.asset";
            ProductDef p = AssetDatabase.LoadAssetAtPath<ProductDef>(path);
            if (p == null)
            {
                p = ScriptableObject.CreateInstance<ProductDef>();
                AssetDatabase.CreateAsset(p, path);
            }
            // Id назначается один раз и больше не трогается: по нему летит сеть.
            if (p.Id == 0) p.Id = id;
            id++;

            p.Title = title;
            p.Group = group;
            p.BaseColor = color;
            p.Size = size;
            p.MadeByRecipe = sub;
            EditorUtility.SetDirty(p);
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

        // ───────────────────────── ПРОВЕРКА ─────────────────────────

        [MenuItem("CatCooks/Еда/Проверить таблицу", priority = 11)]
        public static void CheckTable()
        {
            List<ProductDef> products = new List<ProductDef>();
            foreach (string guid in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef p = AssetDatabase.LoadAssetAtPath<ProductDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (p != null) products.Add(p);
            }

            FoodTransitionTable table = new FoodTransitionTable();
            table.Build(products);
            Debug.Log(table.Validate());
        }
    }
}
#endif
