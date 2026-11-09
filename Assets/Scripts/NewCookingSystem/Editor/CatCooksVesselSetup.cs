#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЗАГОТОВКИ ТАР под ту посуду, что уже есть в проекте:
    /// кастрюля, казан, сковорода, духовка, вертел, ведро,
    /// банка и рюмка для бетона.
    ///
    /// ═══ ВМЕСТИМОСТЬ И ГИГАНТИЗМ ═══
    ///
    /// Масштаб задаётся ЗДЕСЬ, а не размером продуктов. Котёл на сорок порций
    /// и есть источник гигантизма: картошку в него опускают краном, потому что
    /// её надо сорок штук, а не потому что она большая.
    /// </summary>
    public static class CatCooksVesselSetup
    {
        private const string Dir = "Assets/CatCooks/Food/Vessels";

        [MenuItem("CatCooks/Еда/Создать тары", priority = 14)]
        public static void CreateAll()
        {
            EnsureDir(Dir);

            // ── КОТЛЫ: содержимое растворяется в общей массе ──
            Make("Cookware_Pot", "Кастрюля", CookwareKind.Pot, 40,
                 FoodProcess.Boil, true, true, true,
                 "Главный котёл. Варит всё, что бросили. Цвета смешиваются, " +
                 "отдельных морковок в супе не видно — но список внутри сохраняется.");

            Make("Cookware_Kazan", "Казан", CookwareKind.Pot, 30,
                 FoodProcess.Stew, true, true, true,
                 "Тушение: долго и с широким окном, прощает больше кастрюли. " +
                 "Он же держит фритюр, если понадобится.");

            // ── ПЛОСКИЕ: куски лежат раздельно ──
            Make("Cookware_Pan", "Сковорода", CookwareKind.Flat, 8,
                 FoodProcess.Fry, true, false, false,
                 "Куски видны каждый по отдельности, цвета НЕ смешиваются. " +
                 "Поэтому вместимость маленькая: восемь кусков на сковороде " +
                 "ещё читаются, сорок превратятся в кашу.");

            // ── ПЕЧЬ: греет то, что внутрь поставили ──
            Make("Cookware_Oven", "Духовка", CookwareKind.Oven, 12,
                 FoodProcess.Bake, true, false, false,
                 "Не проглатывает: внутрь ставят форму или продукт, и они " +
                 "остаются собой. Духовка только источник жара.");

            // ── ДЕРЖАТЕЛЬ ──
            Make("Cookware_Spit", "Вертел", CookwareKind.Holder, 6,
                 FoodProcess.Spit, true, false, false,
                 "ЕДИНСТВЕННАЯ тара, которая работает сама, пока игрок занят " +
                 "другим. Отсюда берётся параллельность: крутится мясо — " +
                 "месишь тесто.");

            // ── ХРАНЕНИЕ ──
            Make("Cookware_Bucket", "Ведро", CookwareKind.Storage, 30,
                 FoodProcess.None, false, false, false,
                 "Хранение и перенос. С содержимым не происходит ничего, " +
                 "предметы лежат физически и их можно обрабатывать прямо внутри.");

            // ── МЕРНЫЕ: заменяют систему массы ──
            Make("Cookware_ConcreteJar", "Банка для бетона", CookwareKind.Measure, 10,
                 FoodProcess.None, false, false, false,
                 "Мерная тара с рисками. Именно она заменяет граммы: игрок " +
                 "видит «3 деления», а не «412 г». Набрал столько делений — " +
                 "столько порций и выдаст.");

            Make("Cookware_ConcreteShot", "Рюмка для бетона", CookwareKind.Measure, 2,
                 FoodProcess.None, false, false, false,
                 "Мелкая дозировка: специи, закваска, уксус. Одна риска — " +
                 "одна порция. Для того, чего нужно чуть-чуть.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Еда] Создано 8 тар в " + Dir + "\n" +
                      "Повесь Cookware на объект посуды и укажи нужный ассет.");
            EditorUtility.DisplayDialog("Тары готовы",
                "Создано 8 описаний тары.\n\n" +
                "Дальше: на объект кастрюли вешаешь компонент Cookware, " +
                "в поле Def кладёшь Cookware_Pot, и добавляешь коллайдер " +
                "с галкой Is Trigger — через него еда попадает внутрь.",
                "Ясно");
        }

        private static void Make(string file, string title, CookwareKind kind, int cap,
                                 FoodProcess proc, bool needsHeat, bool absorbs,
                                 bool blends, string note)
        {
            string path = $"{Dir}/{file}.asset";
            CookwareDef v = AssetDatabase.LoadAssetAtPath<CookwareDef>(path);
            if (v == null)
            {
                v = ScriptableObject.CreateInstance<CookwareDef>();
                AssetDatabase.CreateAsset(v, path);
            }
            v.Title = title;
            v.Kind = kind;
            v.Capacity = cap;
            v.HeatProcess = proc;
            v.NeedsHeatSource = needsHeat;
            v.Absorbs = absorbs;
            v.BlendsColor = blends;
            v.PortionPerMark = kind == CookwareKind.Measure ? 1 : 0;
            EditorUtility.SetDirty(v);
        }

        private static void EnsureDir(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir)) return;
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
