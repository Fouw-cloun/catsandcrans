#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ДИАГНОСТИКА. Отвечает на вопрос «почему ноль».
    ///
    /// Показывает, что реально лежит в проекте: сколько ассетов каждого типа,
    /// в каких папках, и не разъехались ли пути. Гадать не надо — видно сразу.
    /// </summary>
    public static class CatCooksFoodDiagnostics
    {
        [MenuItem("CatCooks/Еда/ДИАГНОСТИКА — почему пусто", priority = 0)]
        public static void Run()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("═══ ДИАГНОСТИКА БАЗЫ ЕДЫ ═══\n");

            // 1. существуют ли папки, куда пишут кнопки
            string[] dirs =
            {
                "Assets/CatCooks",
                "Assets/CatCooks/Food",
                "Assets/CatCooks/Food/Groups",
                "Assets/CatCooks/Food/Products",
                "Assets/CatCooks/Food/Recipes",
                "Assets/CatCooks/Food/Vessels",
            };
            sb.AppendLine("ПАПКИ, куда пишут кнопки создания:");
            foreach (string d in dirs)
                sb.AppendLine($"   {(AssetDatabase.IsValidFolder(d) ? "ЕСТЬ   " : "НЕТ    ")}{d}");

            // 2. сколько ассетов каждого типа во ВСЁМ проекте
            sb.AppendLine("\nАССЕТЫ ПО ВСЕМУ ПРОЕКТУ (поиск по типу):");
            Count<ProductDef>(sb, "ProductDef");
            Count<FoodGroupDef>(sb, "FoodGroupDef");
            Count<FoodRecipeDef>(sb, "FoodRecipeDef");
            Count<CookwareDef>(sb, "CookwareDef");

            // 3. если продукты есть — где именно и всё ли у них заполнено
            string[] guids = AssetDatabase.FindAssets("t:ProductDef");
            if (guids.Length > 0)
            {
                sb.AppendLine("\nГДЕ ЛЕЖАТ ПРОДУКТЫ (первые 10):");
                for (int i = 0; i < guids.Length && i < 10; i++)
                    sb.AppendLine("   " + AssetDatabase.GUIDToAssetPath(guids[i]));

                int noGroup = 0, zeroId = 0;
                HashSet<ushort> ids = new HashSet<ushort>();
                int dupId = 0;
                foreach (string g in guids)
                {
                    ProductDef p = AssetDatabase.LoadAssetAtPath<ProductDef>(
                        AssetDatabase.GUIDToAssetPath(g));
                    if (p == null) continue;
                    if (p.Group == null) noGroup++;
                    if (p.Id == 0) zeroId++;
                    else if (!ids.Add(p.Id)) dupId++;
                }
                sb.AppendLine($"\nСОСТОЯНИЕ ПРОДУКТОВ:");
                sb.AppendLine($"   без группы: {noGroup}   (у таких не будет переходов)");
                sb.AppendLine($"   с Id = 0:   {zeroId}    (Id обязателен, по нему летит сеть)");
                sb.AppendLine($"   дубли Id:   {dupId}");
            }
            else
            {
                sb.AppendLine("\nПРОДУКТОВ НЕТ ВООБЩЕ. Причины по убыванию вероятности:");
                sb.AppendLine("   1. Кнопка «Создать базу первого среза» не нажималась");
                sb.AppendLine("      или упала с ошибкой — проверь консоль выше.");
                sb.AppendLine("   2. Ассеты создались, но Unity их не проиндексировала:");
                sb.AppendLine("      правый клик в окне Project → Reimport All.");
                sb.AppendLine("   3. Скрипты лежат в Assembly Definition, который не видит");
                sb.AppendLine("      UnityEditor — тогда кнопки вообще не появились бы в меню.");
            }

            // 4. проверка самого меню
            sb.AppendLine("\nЕСЛИ ПУСТО — нажми по порядку:");
            sb.AppendLine("   CatCooks → Еда → Создать базу первого среза");
            sb.AppendLine("   CatCooks → Еда → Создать полуфабрикаты и рецепты");
            sb.AppendLine("   CatCooks → Еда → Создать тары");
            sb.AppendLine("   и снова эту диагностику.");

            Debug.Log(sb.ToString());
        }

        private static void Count<T>(StringBuilder sb, string typeName) where T : Object
        {
            string[] g = AssetDatabase.FindAssets("t:" + typeName);
            sb.AppendLine($"   {typeName,-16} {g.Length,4} шт");
        }

        /// <summary>
        /// Аварийный путь: создать один продукт вручную и убедиться, что
        /// создание ассетов вообще работает. Если и это не сработает —
        /// проблема не в моём коде, а в правах или в состоянии проекта.
        /// </summary>
        [MenuItem("CatCooks/Еда/ДИАГНОСТИКА — создать один тестовый продукт", priority = 2)]
        public static void MakeOne()
        {
            const string dir = "Assets/CatCooks/Food/Products";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                if (!AssetDatabase.IsValidFolder("Assets/CatCooks"))
                    AssetDatabase.CreateFolder("Assets", "CatCooks");
                if (!AssetDatabase.IsValidFolder("Assets/CatCooks/Food"))
                    AssetDatabase.CreateFolder("Assets/CatCooks", "Food");
                AssetDatabase.CreateFolder("Assets/CatCooks/Food", "Products");
            }

            ProductDef p = ScriptableObject.CreateInstance<ProductDef>();
            p.Id = 999;
            p.Title = "ТЕСТ";
            AssetDatabase.CreateAsset(p, dir + "/Food_TEST.asset");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int found = AssetDatabase.FindAssets("t:ProductDef").Length;
            Debug.Log($"[Диагностика] Создан тестовый продукт. " +
                      $"Поиск по проекту теперь находит {found} шт.\n" +
                      (found > 0
                        ? "Создание и поиск работают. Значит кнопка базы не отработала — "
                          + "нажми её и смотри консоль на ошибки."
                        : "Создание работает, а поиск нет — сделай Reimport All."));
            Selection.activeObject = p;
            EditorGUIUtility.PingObject(p);
        }
    }
}
#endif
