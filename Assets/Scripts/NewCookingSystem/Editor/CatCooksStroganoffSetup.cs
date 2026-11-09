#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DS = CatCooks.Food.CatCooksDessertSetup;
using BS = CatCooks.Food.CatCooksBorschSetup;
using S = CatCooks.Food.CookRecipeBook.Stroganoff;

namespace CatCooks.Food
{
    /// <summary>
    /// БЕФСТРОГАНОВ — всё в одной сковороде. Префабы не создаём: ассеты, правила, компоненты на выделенном.
    ///   1. Продукты и правила: «Говядина соломкой» (говядину отбить кувалдой → измельчитель), «Говядина обжаренная»,
    ///      «Мясо с луком», «Бефстроганов», «Бефстроганов (сметана свернулась)»; вид кучек (соломка — брусочками).
    ///   2. Сковорода — выдели: еда в ней кусочками (соломка, лук полукольцами, соус), а не «блином».
    ///   3. Тарелка — выдели: подача и звёзды (бефстроганов + пюре + зелень), еда тоже кусочками.
    ///   4. Проверка.
    /// </summary>
    public static class CatCooksStroganoffSetup
    {
        private const string Root = "CatCooks/Бефстроганов/";

        // ───────────────────────── 1. ПРОДУКТЫ ─────────────────────────

        [MenuItem(Root + "1. Продукты и правила (соломка, обжаренная, с луком, бефстроганов)", priority = 290)]
        public static void MakeProducts()
        {
            var log = new StringBuilder();
            string dir = DS.ProductsDir();
            ushort next = (ushort)(DS.MaxId() + 1);
            FoodGroupDef meat = BS.Or(BS.Or(DS.GroupOf("Говядина"), DS.GroupOf("Свинина")), DS.FindGroup("Group_Meat"));
            FoodGroupDef saute = BS.EnsureGroup("Group_Saute", "Зажарка (сковорода)", "Жареные тёртые овощи — густой массой: видна горкой, берётся мастерком.", FoodState.Puree, log);
            FoodGroupDef paste = BS.EnsureGroup("Group_Paste", "Пасты (уваренное)", "Густая масса: берётся мастерком, лежит гладкой кучкой.", FoodState.Puree, log);

            log.AppendLine("\nПРОДУКТЫ:");
            ProductDef strips = DS.Ensure(S.Strips, meat, new Color(0.78f, 0.24f, 0.24f), 0.05f, true, dir, ref next, log);
            ProductDef fried = DS.Ensure(S.BeefFried, saute, new Color(0.46f, 0.26f, 0.15f), 0.05f, true, dir, ref next, log);
            ProductDef mix = DS.Ensure(S.BeefOnion, saute, new Color(0.56f, 0.36f, 0.20f), 0.05f, true, dir, ref next, log);
            ProductDef dish = DS.Ensure(S.Dish, paste, new Color(0.80f, 0.54f, 0.40f), 0.05f, true, dir, ref next, log);
            ProductDef curd = DS.Ensure(S.Curdled, paste, new Color(0.72f, 0.64f, 0.56f), 0.05f, true, dir, ref next, log);
            foreach (ProductDef p in new[] { strips, fried, mix, dish, curd })
            {
                if (p == null) continue;
                if (p.Solid) { p.Solid = false; EditorUtility.SetDirty(p); }
                if (p.DebrisColor.a < 0.01f) { Color c = p.BaseColor; c.a = 1f; p.DebrisColor = c; EditorUtility.SetDirty(p); }
            }
            if (dish != null && paste != null && dish.Group != paste) { dish.Group = paste; EditorUtility.SetDirty(dish); }

            // говядина: кувалдой отбить (2 удара) → «Отбито»; отбитая в измельчитель → соломка
            log.AppendLine("\nПРАВИЛА (кувалда по говядине — «Отбито»):");
            var beef = new List<ProductDef>();
            ProductDef b0 = DS.Find("Говядина"); if (b0 != null) beef.Add(b0);
            MeatCutSettings st = BS.FindSettings();
            if (st != null && st.BeefKinds != null)
                foreach (ProductDef p in st.BeefKinds)
                {
                    if (p == null || beef.Contains(p) || string.IsNullOrEmpty(p.Title)) continue;
                    string t = p.Title.ToLowerInvariant();
                    if (t.Contains("брискет") || t.Contains("голяш") || t.Contains("кост")) continue;     // у них свои блюда
                    beef.Add(p);
                }
            foreach (ProductDef p in beef)
            {
                foreach (FoodState fs in new[] { FoodState.Butchered, FoodState.Raw, FoodState.Washed, FoodState.Pieces })
                    BS.RuleW(p, fs, FoodProcess.Pound, FoodState.Pounded, 2f, 0f, FoodState.None, FoodState.None, log);
                BS.RuleW(p, FoodState.Pounded, FoodProcess.Pound, FoodState.Pounded, 0.1f, 0f, FoodState.None, FoodState.None, log);   // лишние удары не разбивают
            }
            if (beef.Count == 0) log.AppendLine("✖ не нашёл «Говядина» — Мясо → Новые туши → 1");

            // вид кучек на полу / в мастерке: соломка — брусочками
            log.AppendLine("\nВИД КУЧЕК:");
            FoodHeapLook looks = EnsureLooks(log);
            if (looks != null)
            {
                Look(looks, strips, FoodHeapRenderer.HeapPreset.SoftSticks, log);
                Look(looks, fried, FoodHeapRenderer.HeapPreset.SoftSticks, log);
                Look(looks, mix, FoodHeapRenderer.HeapPreset.SoftSticks, log);
                Look(looks, dish, FoodHeapRenderer.HeapPreset.Dollop, log);
                Look(looks, curd, FoodHeapRenderer.HeapPreset.Dollop, log);
                EditorUtility.SetDirty(looks);
            }

            log.AppendLine("\nЧТО НУЖНО (уже есть?):");
            foreach (string t in new[] { "Масло подсолнечное", "Сливочное масло", "Соль", "Перец чёрный", "Лук репчатый", "Мука пшеничная", "Сметана", "Томатная паста", "Вода" })
                log.AppendLine(DS.Find(t) != null ? $"✔ «{t}»" : $"✖ нет «{t}»" + (t == "Томатная паста" ? " — Борщ → 7" : t == "Масло подсолнечное" ? " — Борщ → 2" : ""));
            log.AppendLine(FindLike("пюре") != null ? $"✔ гарнир — «{FindLike("пюре").Title}»" : "· пюре: картофель, сваренный и размятый (состояние «Пюре»)");

            AssetDatabase.SaveAssets();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null) { db.CollectFromProject(); EditorUtility.SetDirty(db); EditorSceneManager.MarkSceneDirty(db.gameObject.scene); log.AppendLine("\n✔ FoodDatabase пересобран — СОХРАНИ СЦЕНУ"); }
            CookRecipeBook.Reload();
            log.AppendLine("\nВСЁ В ОДНОЙ СКОВОРОДЕ, мастерками (±50%), жидкое — наливать:");
            log.AppendLine("  0. Говядину (кусок туши) — кувалдой 2 удара → «Отбито» → в измельчитель → «Говядина соломкой».");
            log.AppendLine($"  1. Соломка {S.BeefN:0} + масло подсолнечное {S.OilN:0} + соль {S.SaltN:0} + перец {S.PepperN:0} → жарить {S.FrySeconds:0} с (можно раздуть) → «Говядина обжаренная».");
            log.AppendLine($"  2. + лук тёртый {S.OnionN:0} (1 луковица) + сливочное масло {S.ButterN:0} + мука {S.FlourN:0} → жарить {S.OnionSeconds:0} с → «Мясо с луком».");
            log.AppendLine($"  3. + сметана {S.CreamN:0} + томатная паста {S.PasteN:0} + вода/бульон налить (от {S.WaterN:0}) → огонь БЕЗ раздува {S.SimmerSeconds:0} с → «Бефстроганов».");
            log.AppendLine("     Раздул на третьем шаге — «сметана свернулась» (ошибка).");
            log.AppendLine("  4. Мастерком на тарелку + пюре + зелень → звёзды.");
            log.AppendLine("\nДальше: 2 (выдели сковороду), 3 (выдели тарелку), 4 (проверка).");
            DS.Done("Бефстроганов — продукты", log);
        }

        private static ProductDef FindLike(string part)
        {
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef d = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (d != null && !string.IsNullOrEmpty(d.Title) && d.Title.ToLowerInvariant().Contains(part)) return d;
            }
            return null;
        }

        private static FoodHeapLook EnsureLooks(StringBuilder log)
        {
            foreach (string g in AssetDatabase.FindAssets("t:FoodHeapLook"))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                FoodHeapLook l = AssetDatabase.LoadAssetAtPath<FoodHeapLook>(path);
                if (l == null) continue;
                if (!path.Contains("/Resources/")) log.AppendLine($"⚠ {path} — не в папке Resources, игра его не прочтёт");
                return l;
            }
            const string dir = "Assets/CatCooks/Resources";
            DS.EnsureDir(dir);
            var a = ScriptableObject.CreateInstance<FoodHeapLook>();
            AssetDatabase.CreateAsset(a, dir + "/FoodHeapLooks.asset");
            log.AppendLine($"＋ {dir}/FoodHeapLooks.asset (вид кучек по продуктам)");
            return a;
        }

        private static void Look(FoodHeapLook l, ProductDef p, FoodHeapRenderer.HeapPreset look, StringBuilder log)
        {
            if (p == null) return;
            for (int i = 0; i < l.Products.Count; i++)
                if (l.Products[i].Product == p)
                {
                    if (l.Products[i].Look != look) { var e = l.Products[i]; e.Look = look; e.State = FoodState.None; l.Products[i] = e; }
                    log.AppendLine($"✔ «{p.Title}» — {look}");
                    return;
                }
            l.Products.Add(new FoodHeapLook.ByProduct { Product = p, State = FoodState.None, Look = look });
            log.AppendLine($"＋ «{p.Title}» — {look}");
        }

        // ───────────────────────── 2/3. СКОВОРОДА И ТАРЕЛКА ─────────────────────────

        [MenuItem(Root + "2. Сковорода — еда кусочками (выдели сковороду)", priority = 291)]
        public static void SetupPan() => SetupSelected(false);

        [MenuItem(Root + "3. Тарелка — подача и звёзды (выдели тарелку)", priority = 292)]
        public static void SetupPlate() => SetupSelected(true);

        private static void SetupSelected(bool plate)
        {
            var log = new StringBuilder();
            var list = new List<Cookware>();
            foreach (GameObject g in Selection.gameObjects)
            {
                if (g == null) continue;
                Cookware cw = g.GetComponentInParent<Cookware>();
                if (cw == null) cw = g.GetComponentInChildren<Cookware>();
                if (cw != null && !list.Contains(cw)) list.Add(cw);
            }
            if (list.Count == 0) { EditorUtility.DisplayDialog("Бефстроганов", plate ? "Выдели тарелку (объект с Cookware) — в сцене или префаб." : "Выдели сковороду (объект с Cookware) — в сцене или префаб.", "Ок"); return; }
            Material mat = DS.PlateMat(log);
            var handled = new List<ProductDef>();
            foreach (string t in new[] { S.Strips, S.BeefFried, S.BeefOnion, S.Dish, S.Curdled, "Лук репчатый" }) { ProductDef p = DS.Find(t); if (p != null) handled.Add(p); }
            if (handled.Count < 5) log.AppendLine("⚠ не все продукты бефстроганова есть — сначала меню 1");
            foreach (Cookware cw in list)
            {
                GameObject go = cw.gameObject;
                Undo.RegisterFullObjectHierarchyUndo(go, "Бефстроганов");
                CookwareFoodDecor dec = DS.Need<CookwareFoodDecor>(go);
                dec.EditorSetMaterial(mat);
                EditorUtility.SetDirty(dec);
                int hv = 0;
                foreach (CookwareHeapVisual h in go.GetComponentsInChildren<CookwareHeapVisual>(true))
                {
                    var so = new SerializedObject(h);
                    SerializedProperty arr = so.FindProperty("_ignore");
                    if (arr == null) continue;
                    foreach (ProductDef p in handled)
                    {
                        bool has = false;
                        for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == p) { has = true; break; }
                        if (has) continue;
                        arr.arraySize++;
                        arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = p;
                    }
                    so.ApplyModifiedProperties();
                    hv++;
                }
                if (plate) { StroganoffPlate sp = DS.Need<StroganoffPlate>(go); EditorUtility.SetDirty(sp); }
                if (!plate && cw.GetComponent<CookwareFillTuner>() == null)
                    log.AppendLine($"⚠ «{go.name}»: нет Cookware Fill Tuner — размер кусочков возьмётся по габариту (лучше: Борщ → 1 для сковороды)");
                if (!plate && cw.Def != null && cw.Def.Capacity < 40)
                    log.AppendLine($"⚠ «{go.name}»: вместимость {cw.Def.Capacity} — бефстроганов целиком (~16 порций + вода) может не влезть; Борщ → 7 поднимет сковороду до 40");
                log.AppendLine($"✔ «{go.name}»: еда кусочками (Cookware Food Decor)" + (plate ? " + Stroganoff Plate (звёзды)" : "") +
                               $", из обычной горки убраны продукты бефстроганова (горок: {hv})" + (EditorUtility.IsPersistent(go) ? " — префаб" : " — в сцене (копия префаба: Overrides → Apply)"));
                EditorUtility.SetDirty(go);
            }
            if (list.Count > 0 && list[0].gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(list[0].gameObject.scene);
            log.AppendLine(plate ? "\nВ тарелку мастерком: бефстроганов (от 2 мастерков — полная порция), пюре (от 1 мастерка), зелень. Над тарелкой — звёзды."
                                 : "\nВ сковороде: сырая соломка — красные брусочки, обжаренная — румяные, с луком — плюс золотистые полукольца, бефстроганов — в розоватом соусе.");
            DS.Done(plate ? "Бефстроганов — тарелка" : "Бефстроганов — сковорода", log);
        }

        // ───────────────────────── 4. ПРОВЕРКА ─────────────────────────

        [MenuItem(Root + "4. Проверка", priority = 293)]
        public static void Check()
        {
            var log = new StringBuilder();
            log.AppendLine("ПРОДУКТЫ:");
            foreach (string t in new[] { S.Strips, S.BeefFried, S.BeefOnion, S.Dish, S.Curdled })
                log.AppendLine(DS.Find(t) != null ? $"✔ «{t}»" : $"✖ нет «{t}» — меню 1");
            foreach (string t in new[] { "Масло подсолнечное", "Сливочное масло", "Соль", "Перец чёрный", "Лук репчатый", "Мука пшеничная", "Сметана", "Томатная паста", "Вода" })
                log.AppendLine(DS.Find(t) != null ? $"✔ «{t}»" : $"✖ нет «{t}»");
            ProductDef beef = DS.Find("Говядина");
            if (beef != null)
            {
                bool pound = false;
                foreach (FoodRule r in beef.Overrides) if (r.Process == FoodProcess.Pound && r.To == FoodState.Pounded) { pound = true; break; }
                log.AppendLine(pound ? "✔ говядина отбивается кувалдой" : "✖ говядина — нет правила кувалды (меню 1)");
            }
            log.AppendLine("\nВ СЦЕНЕ:");
            int pans = 0, plates = 0;
            foreach (CookwareFoodDecor d in Object.FindObjectsByType<CookwareFoodDecor>(FindObjectsSortMode.None))
            {
                if (d.GetComponent<StroganoffPlate>() != null) plates++; else pans++;
            }
            log.AppendLine(pans > 0 ? $"✔ сковород с едой кусочками: {pans}" : "✖ ни одной сковороды — выдели сковороду → меню 2");
            log.AppendLine(plates > 0 ? $"✔ тарелок бефстроганова: {plates}" : "✖ ни одной тарелки — выдели тарелку → меню 3");
            bool shred = false;
            foreach (MonoBehaviour m in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)) if (m != null && m.GetType().Name == "ShredderStation") { shred = true; break; }
            log.AppendLine(shred ? "✔ измельчитель в сцене" : "⚠ измельчителя нет — соломку делать нечем");
            log.AppendLine("\nВ Play в консоли: «[Рецепты] в книге: … Говядина обжаренная, Мясо с луком, Бефстроганов, …».");
            DS.Done("Бефстроганов — проверка", log);
        }
    }
}
#endif
