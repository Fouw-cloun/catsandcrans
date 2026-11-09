#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food.EditorTools
{
    /// <summary>
    /// ДОПОЛНИТЬ ДАННЫЕ ПОД СРЕЗ 10 БЛЮД.
    ///
    /// Сверка таблицы CatCooks_Slice10 с группами показала: сырые продукты
    /// готовятся хорошо, но у десяти блюд рвутся цепочки — нет отбивания,
    /// фарш не формуется, у теста нет группы, у холода и горелки нет ни
    /// одного правила, нет тарелки, формы, морозилки и коптильни.
    ///
    /// Скрипт ДОБАВЛЯЕТ недостающее и ничего не удаляет и не перезаписывает:
    ///   • правило добавляется, только если пары «состояние + процесс» ещё нет;
    ///   • группа, продукт, тара создаются, только если ассета с таким именем нет.
    /// Запускать можно сколько угодно раз. Перед первым — коммит в git.
    ///
    /// Требует обновлённый FoodState.cs (Pound, Pipe, Pounded, Piped, Torched,
    /// Chilled, Frozen, Dried).
    ///
    /// Меню: Tools → CatCooks → Кулинария → Срез 10 блюд: дополнить данные
    /// </summary>
    public static class CatCooksSlice10Setup
    {
        private const string FoodRoot = "Assets/CatCooks/Food";
        private const FoodState U = FoodState.Undercooked;
        private const FoodState O = FoodState.Overcooked;
        private const FoodState B = FoodState.Burnt;
        private const FoodState N = FoodState.None;

        private static FoodRule R(FoodState from, FoodProcess p, FoodState to, float sec,
                                  float window = 0f, FoodState early = N, FoodState late = N)
            => new FoodRule { From = from, Process = p, To = to, Seconds = sec,
                              Window = window, TooEarly = early, TooLate = late };

        [MenuItem("Tools/CatCooks/Кулинария/Срез 10 блюд: дополнить данные")]
        public static void Run()
        {
            var log = new StringBuilder();

            // ── существующие группы ──
            AddRules("Group_Vegetables", log,
                R(FoodState.Washed, FoodProcess.Boil, FoodState.Boiled, 25f, 0.35f, U, O),   // в мундире (Робюшон)
                R(FoodState.Peeled, FoodProcess.Boil, FoodState.Boiled, 22f, 0.35f, U, O),   // чищеный целиком
                R(FoodState.Boiled, FoodProcess.Sift, FoodState.Puree, 3f));                 // протереть: шлифмашинка / грохот

            AddRules("Group_Meat", log,
                R(FoodState.Butchered, FoodProcess.Pound, FoodState.Pounded, 3f),            // кувалда
                R(FoodState.Pounded, FoodProcess.Bake, FoodState.Baked, 40f, 0.3f, U, O),     // мясо по-французски
                R(FoodState.Pounded, FoodProcess.Fry, FoodState.Fried, 15f, 0.25f, U, O),
                R(FoodState.Pieces, FoodProcess.Fry, FoodState.Fried, 12f, 0.25f, U, O),      // строганов: соломка
                R(FoodState.Pieces, FoodProcess.Stew, FoodState.Stewed, 40f, 0.4f, U, O),
                R(FoodState.Marinated, FoodProcess.Bake, FoodState.Baked, 60f, 0.3f, U, O),   // рёбра под фольгой
                R(FoodState.Baked, FoodProcess.Spit, FoodState.Spitted, 10f, 0.3f, N, B),     // рёбра: гриль-финиш
                R(FoodState.Marinated, FoodProcess.Smoke, FoodState.Smoked, 90f, 0.45f, U, O));// брискет

            AddRules("Group_Egg", log,
                R(FoodState.Whipped, FoodProcess.Torch, FoodState.Torched, 4f, 0.4f, N, B));  // меренга Аляски

            AddRules("Group_SubRecipe", log,
                R(FoodState.Ready, FoodProcess.Shape, FoodState.Shaped, 3f),                 // мороженое куполом
                R(FoodState.Shaped, FoodProcess.Freeze, FoodState.Frozen, 30f),
                R(FoodState.Ready, FoodProcess.Freeze, FoodState.Frozen, 30f),
                R(FoodState.Ready, FoodProcess.Chill, FoodState.Chilled, 20f));               // ледяное масло

            AddRules("Group_Bulk", log,
                R(FoodState.Dissolved, FoodProcess.Boil, FoodState.Melted, 15f, 0.3f, U, B));  // сахарный сироп

            AddRules("Group_Liquid", log,
                R(FoodState.Poured, FoodProcess.Boil, FoodState.Boiled, 10f, 0.4f, N, O));    // горячее молоко

            // ── новые группы ──
            FoodGroupDef mince = EnsureGroup("Group_Mince", "Фарш", FoodState.Minced,
                "Фарш как самостоятельная масса: отбить о миску, охладить, слепить, " +
                "обвалять, обжарить. Отдельно от «Мяса», потому что стартует уже фаршем.", log);
            AddRules(mince, log,
                R(FoodState.Minced, FoodProcess.Pound, FoodState.Pounded, 4f),
                R(FoodState.Minced, FoodProcess.Shape, FoodState.Shaped, 3f),
                R(FoodState.Pounded, FoodProcess.Chill, FoodState.Chilled, 20f),
                R(FoodState.Pounded, FoodProcess.Shape, FoodState.Shaped, 3f),
                R(FoodState.Chilled, FoodProcess.Shape, FoodState.Shaped, 3f),
                R(FoodState.Shaped, FoodProcess.Bread, FoodState.Breaded, 2f),
                R(FoodState.Breaded, FoodProcess.Fry, FoodState.Fried, 15f, 0.25f, U, O),
                R(FoodState.Shaped, FoodProcess.Fry, FoodState.Fried, 15f, 0.25f, U, O),
                R(FoodState.Overcooked, FoodProcess.Fry, FoodState.Burnt, 5f));

            FoodGroupDef dough = EnsureGroup("Group_Dough", "Тесто", FoodState.Kneaded,
                "Тесто после замеса: отлежаться, раскатать, нарезать или слепить, " +
                "отсадить шприцем, подсушить, сварить или испечь.", log);
            AddRules(dough, log,
                R(FoodState.Kneaded, FoodProcess.Rest, FoodState.Proofed, 30f),
                R(FoodState.Kneaded, FoodProcess.Roll, FoodState.Rolled, 4f),                // каток
                R(FoodState.Proofed, FoodProcess.Roll, FoodState.Rolled, 4f),
                R(FoodState.Rolled, FoodProcess.Cut, FoodState.Pieces, 2f),
                R(FoodState.Rolled, FoodProcess.Shape, FoodState.Shaped, 3f),               // пельмени
                R(FoodState.Kneaded, FoodProcess.Shape, FoodState.Shaped, 3f),              // жгуты ньокки
                R(FoodState.Shaped, FoodProcess.Cut, FoodState.Pieces, 2f),
                R(FoodState.Pieces, FoodProcess.Boil, FoodState.Boiled, 8f, 0.35f, U, O),    // ньокки
                R(FoodState.Shaped, FoodProcess.Boil, FoodState.Boiled, 10f, 0.35f, U, O),   // пельмени
                R(FoodState.Kneaded, FoodProcess.Pipe, FoodState.Piped, 2f),                // макарон: шприц
                R(FoodState.Piped, FoodProcess.Rest, FoodState.Dried, 20f),                 // матовая корочка
                R(FoodState.Dried, FoodProcess.Bake, FoodState.Baked, 14f, 0.25f, U, O),
                R(FoodState.Proofed, FoodProcess.Bake, FoodState.Baked, 40f, 0.3f, U, O),    // хлеб
                R(FoodState.Overcooked, FoodProcess.Bake, FoodState.Burnt, 8f));

            // ── продукты ──
            MoveProduct("Говяжий фарш", mince, log);
            EnsureProduct("Тесто для пельменей", dough, new Color(0.95f, 0.9f, 0.78f), log);
            EnsureProduct("Тесто для ньокки", dough, new Color(0.93f, 0.85f, 0.6f), log);
            EnsureProduct("Макаронное тесто", dough, new Color(0.96f, 0.8f, 0.85f), log);

            // ── тара ──
            EnsureCookware("Cookware_Plate", "Тарелка", CookwareKind.Storage, 6, FoodProcess.None, false, false, log);
            EnsureCookware("Cookware_Form", "Форма для запекания", CookwareKind.Flat, 12, FoodProcess.None, false, false, log);
            EnsureCookware("Cookware_Bowl", "Миска", CookwareKind.Pot, 20, FoodProcess.None, false, true, log);
            EnsureCookware("Cookware_Fridge", "Холодильник", CookwareKind.Oven, 10, FoodProcess.Chill, false, false, log);
            EnsureCookware("Cookware_Freezer", "Морозилка", CookwareKind.Oven, 10, FoodProcess.Freeze, false, false, log);
            EnsureCookware("Cookware_Smoker", "Коптильня", CookwareKind.Oven, 4, FoodProcess.Smoke, true, false, log);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Срез 10] Готово.\n" + (log.Length > 0 ? log.ToString() : "Всё уже было на месте."));
            Debug.Log("[Срез 10] Не забудь: FoodDatabase → собрать продукты из проекта заново " +
                      "(новые продукты получили новые Id).");
        }

        // ───────────────────────── ГРУППЫ ─────────────────────────

        private static void AddRules(string groupAsset, StringBuilder log, params FoodRule[] rules)
        {
            FoodGroupDef g = FindAsset<FoodGroupDef>(groupAsset);
            if (g == null) { log.AppendLine($"  ✘ нет группы {groupAsset} — пропущено"); return; }
            AddRules(g, log, rules);
        }

        private static void AddRules(FoodGroupDef g, StringBuilder log, params FoodRule[] rules)
        {
            int added = 0;
            Undo.RecordObject(g, "Срез 10: правила");
            foreach (FoodRule r in rules)
            {
                bool exists = g.Rules.Any(x => x.From == r.From && x.Process == r.Process);
                if (exists) continue;
                g.Rules.Add(r);
                added++;
                log.AppendLine($"  + {g.name}: {FoodStateInfo.NameOf(r.From)} + " +
                               $"{FoodStateInfo.NameOf(r.Process)} → {FoodStateInfo.NameOf(r.To)}");
            }
            if (added > 0) EditorUtility.SetDirty(g);
        }

        private static FoodGroupDef EnsureGroup(string assetName, string title, FoodState start,
                                                string note, StringBuilder log)
        {
            FoodGroupDef g = FindAsset<FoodGroupDef>(assetName);
            if (g != null) return g;

            g = ScriptableObject.CreateInstance<FoodGroupDef>();
            g.Title = title;
            g.StartState = start;
            g.Note = note;
            AssetDatabase.CreateAsset(g, $"{FoodRoot}/Groups/{assetName}.asset");
            log.AppendLine($"  ★ создана группа {assetName}");
            return g;
        }

        // ───────────────────────── ПРОДУКТЫ ─────────────────────────

        private static void MoveProduct(string title, FoodGroupDef group, StringBuilder log)
        {
            ProductDef p = AllAssets<ProductDef>().FirstOrDefault(x => x.Title == title);
            if (p == null) { log.AppendLine($"  ✘ нет продукта «{title}»"); return; }
            if (p.Group == group) return;
            Undo.RecordObject(p, "Срез 10: группа продукта");
            p.Group = group;
            EditorUtility.SetDirty(p);
            log.AppendLine($"  ↪ «{title}» перенесён в группу {group.name}");
        }

        private static void EnsureProduct(string title, FoodGroupDef group, Color color, StringBuilder log)
        {
            List<ProductDef> all = AllAssets<ProductDef>();
            if (all.Any(x => x.Title == title)) return;

            ushort nextId = (ushort)(all.Count == 0 ? 1 : all.Max(x => x.Id) + 1);
            ProductDef p = ScriptableObject.CreateInstance<ProductDef>();
            p.Id = nextId;
            p.Title = title;
            p.Group = group;
            p.BaseColor = color;
            p.MadeByRecipe = true;
            p.Size = 0.08f;

            string safe = title.Replace(' ', '_');
            AssetDatabase.CreateAsset(p, AssetDatabase.GenerateUniqueAssetPath($"{FoodRoot}/Products/Food_{safe}.asset"));
            log.AppendLine($"  ★ создан продукт «{title}» (Id {nextId}) — рецепт замеса заведи вручную");
        }

        // ───────────────────────── ТАРА ─────────────────────────

        private static void EnsureCookware(string assetName, string title, CookwareKind kind, int cap,
                                           FoodProcess heat, bool needsHeat, bool absorbs, StringBuilder log)
        {
            if (FindAsset<CookwareDef>(assetName) != null) return;

            CookwareDef d = ScriptableObject.CreateInstance<CookwareDef>();
            d.Title = title;
            d.Kind = kind;
            d.Capacity = cap;
            d.HeatProcess = heat;
            d.NeedsHeatSource = needsHeat;
            d.Absorbs = absorbs;
            d.BlendsColor = absorbs;
            AssetDatabase.CreateAsset(d, $"{FoodRoot}/Vessels/{assetName}.asset");
            log.AppendLine($"  ★ создана тара {assetName} ({title})");
        }

        // ───────────────────────── ПОИСК ─────────────────────────

        private static T FindAsset<T>(string assetName) where T : Object
        {
            foreach (string guid in AssetDatabase.FindAssets($"{assetName} t:{typeof(T).Name}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == assetName)
                    return AssetDatabase.LoadAssetAtPath<T>(path);
            }
            return null;
        }

        private static List<T> AllAssets<T>() where T : Object
            => AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                            .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
                            .Where(x => x != null).ToList();
    }
}
#endif
