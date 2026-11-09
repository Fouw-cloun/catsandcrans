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
    /// ТАРА «ПРИНИМАЕТ ВСЁ» — одна кнопка для любой тары (ведро, кастрюля, ковш, банка, корыто, миска).
    ///
    /// Что делает с каждой выделенной тарой (Cookware на корне или выше/ниже по иерархии):
    ///   1. LiquidAnchor (жидкость и массы) — если нет, ставит как «Десерты → 2б» (замер полости).
    ///   2. HeapAnchor (горка: порошки, кусочки, специи) — если нет, ставит на дно по размеру LiquidAnchor.
    ///   3. В LiquidAnchor → Products прописывает ВСЕ продукты базы с ролью по названию/состоянию:
    ///        жидкое → «Жидкость», сливки → «Слой сверху», сливочное масло/шоколад → «Паста»,
    ///        тесто/меренга/соусы/мёд/глазурь → «Густая», мороженое → «Мороженое»,
    ///        полуфабрикаты-порошки (сухой руб, миндальная смесь) → «Паста».
    ///      Сыпучее (сахар, мука, соль, специи) и кусочки (мясо, овощи) в список НЕ идут —
    ///      их рисует горка HeapAnchor, а в жидкости — плавающие кусочки (суп).
    ///      Уже прописанное НЕ трогается (свои роли и цвета остаются).
    ///   4. Cookware: «Apply On Enter» — Pour и Scoop (налили/зачерпнули), «Cooks Recipes» — вкл.
    ///   5. Проверяет: Def, Absorbs (глотает ли кусочки), триггер приёма, Mouth — и пишет, что поправить.
    ///
    /// Префабы не создаёт и не меняет: тара в сцене — префаб? Overrides → Apply All.
    /// Нажимать можно сколько угодно раз: новые продукты добавятся, старые не дублируются.
    /// </summary>
    public static class CatCooksAnyCookwareSetup
    {
        [MenuItem("CatCooks/Еда/Тара: принимать ВСЁ (выдели тару)", priority = 15)]
        public static void SetupSelected()
        {
            var list = new List<Cookware>();
            foreach (GameObject sel in Selection.gameObjects)
            {
                Cookware cw = sel.GetComponentInParent<Cookware>();
                if (cw == null) cw = sel.GetComponentInChildren<Cookware>(true);
                if (cw != null && !list.Contains(cw)) list.Add(cw);
            }
            if (list.Count == 0) { EditorUtility.DisplayDialog("Тара", "Выдели тару в сцене (ведро, кастрюлю, ковш, банку…) — на ней должен быть Cookware.", "Ок"); return; }
            Run(list, "Тара — принимать всё");
        }

        [MenuItem("CatCooks/Еда/Тара: принимать ВСЁ — вся тара в сцене", priority = 16)]
        public static void SetupAll()
        {
            var list = new List<Cookware>();
            foreach (Cookware cw in Object.FindObjectsByType<Cookware>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (cw != null && cw.gameObject.scene.IsValid() && !IsOven(cw)) list.Add(cw);
            if (list.Count == 0) { EditorUtility.DisplayDialog("Тара", "В сцене нет Cookware.", "Ок"); return; }
            if (!EditorUtility.DisplayDialog("Тара", $"Настроить {list.Count} тар в сцене (духовки пропущу)?", "Да", "Отмена")) return;
            Run(list, "Тара — принимать всё (вся сцена)");
        }

        private static bool IsOven(Cookware cw)
        {
            string n = cw.name.ToLowerInvariant();
            return n.Contains("oven") || n.Contains("духов") || (cw.Def != null && cw.Def.Title.ToLowerInvariant().Contains("духов"));
        }

        private static void Run(List<Cookware> list, string title)
        {
            var log = new StringBuilder();
            List<ProductDef> all = AllProducts();
            log.AppendLine($"Продуктов в базе: {all.Count}\n");
            int ok = 0;
            foreach (Cookware cw in list)
            {
                if (EditorUtility.IsPersistent(cw)) { log.AppendLine($"— «{cw.name}»: это префаб в Project — перетащи в сцену и выдели там\n"); continue; }
                log.AppendLine($"══ «{cw.name}» ══");
                Undo.RegisterFullObjectHierarchyUndo(cw.gameObject, "Тара: принимать всё");
                CheckCookware(cw, log);

                CookwareLiquidVisual lv = cw.GetComponentInChildren<CookwareLiquidVisual>(true);
                if (lv == null)
                {
                    log.AppendLine("  LiquidAnchor нет — ставлю (замер полости, как «Десерты → 2б»):");
                    Object[] keep = Selection.objects;
                    Selection.objects = new Object[] { cw.gameObject };
                    DS.Quiet = true;
                    try { DS.AddLiquidAnchors(); }
                    finally { DS.Quiet = false; Selection.objects = keep; }
                    lv = cw.GetComponentInChildren<CookwareLiquidVisual>(true);
                    if (lv == null) { log.AppendLine("  ✖ LiquidAnchor не поставился (нет мешей?) — жидкость в этой таре не будет видна\n"); continue; }
                    log.AppendLine($"  ＋ LiquidAnchor «{lv.name}»");
                }
                else log.AppendLine($"  ✔ LiquidAnchor «{lv.name}»");

                EnsureHeap(cw, lv, log);
                FillRoles(lv, all, log);
                EditorUtility.SetDirty(cw.gameObject);
                ok++;
                log.AppendLine();
            }
            if (ok > 0) EditorSceneManager.MarkSceneDirty(list[0].gameObject.scene);
            log.AppendLine("ДАЛЬШЕ: тара — префаб? Overrides → Apply All. Сохрани сцену.\n" +
                           "Новые продукты появились позже — нажми ещё раз, добавятся только они.\n" +
                           "Роль не та (например, соус льётся, а должен лежать) — поправь Role у строки в LiquidAnchor → Products.");
            DS.Done(title, log);
        }

        // ───────── проверки Cookware ─────────

        private static void CheckCookware(Cookware cw, StringBuilder log)
        {
            if (cw.Def == null) log.AppendLine("  ✖ Cookware → Def пуст — тара не знает вместимость и нагрев. Поставь CookwareDef (ведро/кастрюля…)");
            else
            {
                log.AppendLine($"  Def «{cw.Def.Title}»: вместимость {cw.Def.Capacity} порц., нагрев {cw.Def.HeatProcess}");
                if (!cw.Def.Absorbs) log.AppendLine("  ⚠ Def → Absorbs выключен — брошенные кусочки (рёбра, овощи) НЕ проглотятся, жидкость и порошки — да. " +
                                                    "Нужно глотать — включи Absorbs в этом CookwareDef (он общий для всех тар с ним)");
            }
            var so = new SerializedObject(cw);
            SerializedProperty rec = so.FindProperty("_cooksRecipes");
            if (rec != null && !rec.boolValue) { rec.boolValue = true; log.AppendLine("  ＋ Cooks Recipes включён — рецепты в этой таре теперь ищутся"); }
            SerializedProperty en = so.FindProperty("_applyOnEnter");
            if (en != null)
            {
                bool pour = false, scoop = false;
                for (int i = 0; i < en.arraySize; i++)
                {
                    int v = en.GetArrayElementAtIndex(i).intValue;
                    if (v == (int)FoodProcess.Pour) pour = true;
                    if (v == (int)FoodProcess.Scoop) scoop = true;
                }
                if (!pour) { en.arraySize++; en.GetArrayElementAtIndex(en.arraySize - 1).intValue = (int)FoodProcess.Pour; log.AppendLine("  ＋ Apply On Enter: Pour (налитое)"); }
                if (!scoop) { en.arraySize++; en.GetArrayElementAtIndex(en.arraySize - 1).intValue = (int)FoodProcess.Scoop; log.AppendLine("  ＋ Apply On Enter: Scoop (зачерпнутое)"); }
            }
            so.ApplyModifiedProperties();

            bool trigger = false;
            foreach (Collider c in cw.GetComponentsInChildren<Collider>(true)) if (c.isTrigger) { trigger = true; break; }
            if (!trigger) log.AppendLine("  ⚠ нет коллайдера-триггера приёма — брошенное внутрь не засчитается. " +
                                         "Добавь Cookware Auto Setup → ⋮ «Подогнать и собрать» (сделает Receive) или свой Box Collider с Is Trigger внутри");
            SerializedProperty mouth = so.FindProperty("_mouth");
            if (mouth != null && mouth.objectReferenceValue == null)
                log.AppendLine("  · Mouth пуст — горловиной считается центр полости (обычно ок)");
        }

        // ───────── горка ─────────

        private static void EnsureHeap(Cookware cw, CookwareLiquidVisual lv, StringBuilder log)
        {
            CookwareHeapVisual hv = cw.GetComponentInChildren<CookwareHeapVisual>(true);
            if (hv != null) { log.AppendLine($"  ✔ HeapAnchor «{hv.name}» — порошки и кусочки горкой"); return; }
            var lso = new SerializedObject(lv);
            bool round = lso.FindProperty("_form") != null && lso.FindProperty("_form").intValue == (int)CookwareLiquidVisual.Form.Round;
            float rb = lso.FindProperty("_radiusBottom") != null ? lso.FindProperty("_radiusBottom").floatValue : 0.1f;
            Vector2 half = lso.FindProperty("_halfSize") != null ? lso.FindProperty("_halfSize").vector2Value : new Vector2(0.1f, 0.1f);
            float H = lso.FindProperty("_maxHeight") != null ? lso.FindProperty("_maxHeight").floatValue : 0.2f;

            Transform ha = DS.FindOrCreate(lv.transform.parent != null ? lv.transform.parent : cw.transform, "HeapAnchor");
            ha.localPosition = lv.transform.localPosition; ha.localRotation = lv.transform.localRotation; ha.localScale = Vector3.one;
            ha.gameObject.layer = cw.gameObject.layer;
            FoodHeapRenderer fr = DS.Need<FoodHeapRenderer>(ha.gameObject);
            if (ha.GetComponent<CookwareHeapVisual>() == null) Undo.AddComponent<CookwareHeapVisual>(ha.gameObject);
            var hso = new SerializedObject(fr);
            SetI(hso, "_shape", (int)FoodHeapRenderer.Shape.Fill);
            SetV2(hso, "_footprint", round ? new Vector2(rb * 0.8f, rb * 0.8f) : half * 0.9f);
            SetB(hso, "_fillRect", !round);
            SetF(hso, "_maxHeight", H * 0.7f);
            SetB(hso, "_preview", false);
            SetB(hso, "_worldUpright", false);
            SetB(hso, "_ignoreParentScale", true);
            hso.ApplyModifiedProperties();
            log.AppendLine($"  ＋ HeapAnchor на дне ({(round ? $"круг r={rb * 0.8f:0.00}" : $"{half.x * 1.8f:0.00}×{half.y * 1.8f:0.00}")} м, высота {H * 0.7f:0.00} м) — Food Heap Renderer + Cookware Heap Visual");
        }

        // ───────── роли продуктов ─────────

        private static readonly Color Own = new Color(0f, 0f, 0f, 0f);

        /// <summary>Роль по продукту. false — не в список (горка/кусочки).</summary>
        private static bool RoleOf(ProductDef p, out CookwareLiquidVisual.Role role, out Color color, out string why)
        {
            role = CookwareLiquidVisual.Role.Liquid; color = Own; why = "";
            string t = (p.Title ?? "").ToLowerInvariant().Trim();
            FoodVisual vis = FoodStateInfo.VisualOf(p.StartState);

            // точные — как в меню десертов, макарон и барбекю
            switch (t)
            {
                case "яйцо": color = new Color(0.98f, 0.84f, 0.42f, 1f); why = "жидкость (разбитые яйца)"; return true;
                case "яичный белок": color = new Color(0.95f, 0.94f, 0.88f, 1f); why = "жидкость (белок)"; return true;
                case "шоколадная масса": why = "жидкий шоколад"; return true;
                case "сливочное масло": role = CookwareLiquidVisual.Role.Paste; why = "масло — паста"; return true;
                case "шоколад": case "шоколадная паста": role = CookwareLiquidVisual.Role.Paste; why = "паста"; return true;
            }
            if (t.Contains("фарш") || t.Contains("кост") || t.Contains("туш")) return false;   // фарш, кости, туша — горкой/кусками
            if (t.Contains("смесь для морожен")) { why = "жидкая смесь"; return true; }
            if (t.Contains("морожен")) { role = CookwareLiquidVisual.Role.IceCream; why = "мороженое — шарики"; return true; }
            if (t.Contains("сливк")) { role = CookwareLiquidVisual.Role.TopLayer; why = "сливки — слой сверху"; return true; }
            if (FoodHeapLook.IsPowderName(p))
            {
                if (p.MadeByRecipe) { role = CookwareLiquidVisual.Role.Paste; why = "смесь-полуфабрикат — масса"; return true; }
                return false;   // сахар, мука, соль, специи — горкой
            }
            if (FoodHeapLook.IsThickName(p) || FoodHeapLook.IsMassName(p) || HasAny(t, ThickWords))
            { role = CookwareLiquidVisual.Role.Thick; why = "густое (мастерком)"; return true; }
            if (IsLiquidName(t)) { why = "жидкость"; return true; }
            if (p.Solid) { role = CookwareLiquidVisual.Role.Paste; why = "твёрдое — плотная масса"; return true; }
            if (FoodHeapLook.IsLiquid(p, p.StartState)) { why = "жидкость"; return true; }
            if (vis == FoodVisual.Mass) { role = CookwareLiquidVisual.Role.Thick; why = "масса"; return true; }
            if (p.MadeByRecipe && vis != FoodVisual.Pieces && vis != FoodVisual.Solid && vis != FoodVisual.Pile)
            { role = CookwareLiquidVisual.Role.Thick; why = "полуфабрикат — масса"; return true; }
            return false;       // кусочки, целое, сыпучее — горка/суп
        }

        private static readonly string[] LiquidWords =
        {
            "молок", "вода", "бульон", "уксус", "бурбон", "соус", "сироп", "масло", "кефир", "морс", "компот",
        };

        private static readonly string[] ThickWords = { "масса", "крем", "пюре", "джем", "повидл", "начинк" };

        private static bool HasAny(string t, string[] words)
        {
            foreach (string w in words) if (t.Contains(w)) return true;
            return false;
        }

        /// <summary>Жидкое по названию: «Моп-соус», «Яблочный уксус», «Бурбон»… (сок — только отдельным словом: не «песок»).</summary>
        private static bool IsLiquidName(string t)
        {
            if (t.StartsWith("сок") || t.Contains(" сок")) return true;
            foreach (string w in LiquidWords) if (t.Contains(w)) return true;
            return false;
        }

        private static void FillRoles(CookwareLiquidVisual lv, List<ProductDef> all, StringBuilder log)
        {
            var so = new SerializedObject(lv);
            SerializedProperty arr = so.FindProperty("_products");
            if (arr == null) { log.AppendLine("  ✖ у LiquidAnchor нет поля Products — старая версия Cookware Liquid Visual?"); return; }
            var have = new HashSet<ProductDef>();
            for (int i = 0; i < arr.arraySize; i++)
            {
                var p = arr.GetArrayElementAtIndex(i).FindPropertyRelative("Product").objectReferenceValue as ProductDef;
                if (p != null) have.Add(p);
            }
            int added = 0, kept = have.Count, heap = 0;
            var byRole = new Dictionary<CookwareLiquidVisual.Role, List<string>>();
            var heapNames = new List<string>();
            foreach (ProductDef p in all)
            {
                if (have.Contains(p)) continue;
                if (!RoleOf(p, out CookwareLiquidVisual.Role role, out Color col, out string why))
                {
                    heap++; if (heapNames.Count < 14) heapNames.Add(p.Title);
                    continue;
                }
                arr.arraySize++;
                SerializedProperty e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
                e.FindPropertyRelative("Product").objectReferenceValue = p;
                e.FindPropertyRelative("Role").intValue = (int)role;
                e.FindPropertyRelative("Color").colorValue = col;
                if (!byRole.TryGetValue(role, out List<string> l)) byRole[role] = l = new List<string>();
                l.Add(p.Title);
                added++;
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(lv);
            log.AppendLine($"  Products: было {kept}, добавлено {added}" + (added == 0 ? " — всё уже прописано" : ":"));
            foreach (KeyValuePair<CookwareLiquidVisual.Role, List<string>> kv in byRole)
                log.AppendLine($"    {RoleName(kv.Key)}: {string.Join(", ", kv.Value)}");
            if (heap > 0) log.AppendLine($"  горкой / кусочками ({heap}): {string.Join(", ", heapNames)}{(heap > heapNames.Count ? "…" : "")}");
        }

        private static string RoleName(CookwareLiquidVisual.Role r)
        {
            switch (r)
            {
                case CookwareLiquidVisual.Role.Liquid: return "Жидкость";
                case CookwareLiquidVisual.Role.TopLayer: return "Слой сверху";
                case CookwareLiquidVisual.Role.Floating: return "Плавающие комки";
                case CookwareLiquidVisual.Role.Paste: return "Паста";
                case CookwareLiquidVisual.Role.Thick: return "Густое";
                case CookwareLiquidVisual.Role.IceCream: return "Мороженое";
            }
            return r.ToString();
        }

        private static List<ProductDef> AllProducts()
        {
            var res = new List<ProductDef>();
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef d = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (d != null && !string.IsNullOrEmpty(d.Title) && !res.Contains(d)) res.Add(d);
            }
            res.Sort((a, b) => string.CompareOrdinal(a.Title, b.Title));
            return res;
        }

        private static void SetI(SerializedObject so, string n, int v) { SerializedProperty p = so.FindProperty(n); if (p != null) p.intValue = v; }
        private static void SetF(SerializedObject so, string n, float v) { SerializedProperty p = so.FindProperty(n); if (p != null) p.floatValue = v; }
        private static void SetB(SerializedObject so, string n, bool v) { SerializedProperty p = so.FindProperty(n); if (p != null) p.boolValue = v; }
        private static void SetV2(SerializedObject so, string n, Vector2 v) { SerializedProperty p = so.FindProperty(n); if (p != null) p.vector2Value = v; }
    }
}
#endif
