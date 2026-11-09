#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// CatCooks → Острый соус → 3. Проверить всё для соуса.
    /// Ничего не меняет — только список: что настроено, чего не хватает и где.
    /// Выделен ковш — проверяет его подробно; иначе ищет все ковши (SurfaceHeatGlow) в сцене.
    /// </summary>
    public static class CatCooksHotSauceDoctor
    {
        private static StringBuilder _sb;
        private static int _err, _warn;

        [MenuItem("CatCooks/Острый соус/3. Проверить всё для соуса", priority = 62)]
        public static void Run()
        {
            _sb = new StringBuilder(); _err = 0; _warn = 0;

            // ── продукты и рецепт ──
            Head("Продукты");
            string[] need = { "Вода", "Перец чили", "Уксус", "Соль", "Чеснок", "Зубчик чеснока", "Острая смесь", "Острый соус" };
            foreach (string t in need)
            {
                ProductDef p = FindProduct(t);
                if (p == null) Err($"нет продукта «{t}» → CatCooks → Острый соус → 1. Создать продукты и рецепт");
                else Ok($"«{t}» Id {p.Id}" + (p.Group == null ? " — НЕТ ГРУППЫ" : ""));
            }
            ProductDef garlic = FindProduct("Чеснок");
            if (garlic != null)
            {
                bool pound = HasRule(garlic, FoodProcess.Pound), peel = HasRuleFrom(garlic, FoodState.Pounded, FoodProcess.Peel);
                if (!pound || !peel) Err("у «Чеснок» нет правил кувалда→Отбито / шлифмашинка Отбито→Чищеное → нажми «1. Создать продукты и рецепт»");
            }
            Head("Рецепт");
            bool recipe = false;
            foreach (string g in AssetDatabase.FindAssets("t:CookRecipeDef"))
            {
                var r = AssetDatabase.LoadAssetAtPath<CookRecipeDef>(AssetDatabase.GUIDToAssetPath(g));
                if (r == null || r.Title != "Острый соус") continue;
                recipe = true;
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!path.Contains("/Resources/CookRecipes/")) Warn($"рецепт лежит не в Resources/CookRecipes ({path}) — книга его не увидит");
                else Ok($"рецепт «Острый соус» — шагов {r.Steps.Count}: " + string.Join(" → ", r.Steps.ConvertAll(s => CookwareJournal.KindName(s.Kind) + $" {s.Seconds:0} с")));
            }
            if (!recipe) Warn("ассета рецепта нет — книга соберёт рецепт сама по названиям продуктов (если все продукты есть)");
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db == null) Err("в сцене нет FoodDatabase");

            // ── ковши ──
            Head("Ковши (тара на горелке)");
            var sel = Selection.activeGameObject;
            SurfaceHeatGlow[] glows = sel != null ? sel.GetComponentsInChildren<SurfaceHeatGlow>(true)
                                                  : Object.FindObjectsByType<SurfaceHeatGlow>(FindObjectsSortMode.None);
            if (glows.Length == 0 && sel != null && sel.GetComponent<Cookware>() != null)
                Err($"«{sel.name}»: нет Surface Heat Glow — горелка не будет греть этот ковш");
            if (glows.Length == 0 && sel == null) Warn("в сцене нет ни одного ковша с Surface Heat Glow");
            foreach (SurfaceHeatGlow g in glows) CheckBucket(g.gameObject);

            // ── горелки ──
            Head("Горелки");
            var blow = Object.FindObjectsByType<BlowTorchItem>(FindObjectsSortMode.None);
            var roof = Object.FindObjectsByType<RoofingTorchItem>(FindObjectsSortMode.None);
            Ok($"ручных горелок {blow.Length}, кровельных {roof.Length}");
            foreach (RoofingTorchItem t in roof)
            {
                var so = new SerializedObject(t);
                var cyl = so.FindProperty("_cylinder");
                bool any = Object.FindObjectsByType<TorchGasCylinder>(FindObjectsSortMode.None).Length > 0;
                if ((cyl == null || cyl.objectReferenceValue == null) && !any) Err($"«{t.name}»: нет баллона (TorchGasCylinder) в сцене — гореть не будет");
                if (so.FindProperty("_nozzle").objectReferenceValue == null && !HasChild(t.transform, "bell"))
                    Warn($"«{t.name}»: нет Nozzle и нет части Bell — пламя пойдёт из центра; создай пустой объект на кромке стакана");
                CheckNet(t.gameObject);
            }
            foreach (TorchGasCylinder c in Object.FindObjectsByType<TorchGasCylinder>(FindObjectsSortMode.None))
            {
                if (!HasChild(c.transform, "reducer") && !HasChild(c.transform, "редуктор")) Warn($"«{c.name}»: нет дочернего редуктора (GasReducer) — шланг крепится к вентилю");
                CheckNet(c.gameObject);
            }

            // ── инструменты и ингредиенты ──
            Head("Отбойник, чеснок, чили, уксус");
            var jacks = Object.FindObjectsByType<JackhammerItem>(FindObjectsSortMode.None);
            foreach (JackhammerItem j in jacks)
                if (j.GetComponent<JackhammerGrinder>() == null) Err($"«{j.name}»: нет Jackhammer Grinder — отбойник не измельчит в таре");
                else Ok($"«{j.name}»: Jackhammer Grinder есть");
            if (jacks.Length == 0) Warn("отбойника в сцене нет");
            foreach (FoodSplitIntoParts s in Object.FindObjectsByType<FoodSplitIntoParts>(FindObjectsSortMode.None))
            {
                var so = new SerializedObject(s);
                var arr = so.FindProperty("_partPrefabs");
                if (arr == null || arr.arraySize == 0) Err($"«{s.name}»: Food Split Into Parts без Part Prefabs (зубчиков)");
                else Ok($"«{s.name}»: зубчиков {so.FindProperty("_partCount").intValue}");
            }
            foreach (FoodWholeSlicer w in Object.FindObjectsByType<FoodWholeSlicer>(FindObjectsSortMode.None))
            {
                FoodPiece fp = w.GetComponent<FoodPiece>();
                var pso = fp != null ? new SerializedObject(fp) : null;
                var prod = pso != null ? pso.FindProperty("_product").objectReferenceValue as ProductDef : null;
                if (prod == null || !prod.Title.ToLowerInvariant().Contains("чили")) continue;
                var so = new SerializedObject(w);
                if (so.FindProperty("_onlyPeeled").boolValue) Err($"«{w.name}» (чили): в Food Whole Slicer включён Only Peeled — чили не чистят, выключи");
                var arr = so.FindProperty("_slicePrefabs");
                if (arr == null || arr.arraySize == 0) Err($"«{w.name}» (чили): нет Slice Prefabs (колечко чили)");
                else Ok($"«{w.name}» (чили): колец {so.FindProperty("_sliceCount").intValue}");
            }
            foreach (CatCooks.Food.Tools.LiquidDoseJarItem jar in Object.FindObjectsByType<CatCooks.Food.Tools.LiquidDoseJarItem>(FindObjectsSortMode.None))
            {
                var so = new SerializedObject(jar);
                var prod = so.FindProperty("_product").objectReferenceValue as ProductDef;
                if (prod == null) Err($"«{jar.name}»: у банки не задан Product (Уксус)");
                else Ok($"«{jar.name}»: льёт «{prod.Title}»");
                CheckNet(jar.gameObject);
            }

            string head = _err > 0 ? $"ОШИБОК {_err}, предупреждений {_warn}" : _warn > 0 ? $"ошибок нет, предупреждений {_warn}" : "всё в порядке";
            string text = head + "\n" + _sb;
            Debug.Log("[Острый соус — проверка]\n" + text);
            EditorUtility.DisplayDialog("Острый соус — проверка", text.Length > 3500 ? text.Substring(0, 3500) + "\n… (полностью — в консоли)" : text, "Ок");
        }

        private static void CheckBucket(GameObject go)
        {
            Cookware cw = go.GetComponent<Cookware>();
            string n = go.name;
            if (cw == null) { Err($"«{n}»: Surface Heat Glow есть, а Cookware нет"); return; }
            if (cw.Def == null) Err($"«{n}»: у Cookware не задан Def (вместимость)");
            else Ok($"«{n}»: вместимость {cw.Def.Capacity} порц. (~{cw.Def.Capacity / (float)FoodUnits.Bucket:0.#} ведра)");
            if (go.GetComponent<CookwareHeat>() == null) Err($"«{n}»: нет Cookware Heat — металл будет красным, а вода не нагреется");
            if (cw.InsideVolume == null) Err($"«{n}»: у Cookware пустой Inside Volume — Cookware Cavity → «Найти и собрать»");
            var cavity = go.GetComponent<CookwareCavity>();
            var auto = go.GetComponent<CookwareAutoSetup>();
            if (cavity != null)
            {
                Ok($"«{n}»: Cookware Cavity — {(cavity.Form == CookwareCavity.Shape.Cone ? "рюмка" : "ковш")}, объём {cavity.Volume * 1000f:0} л");
                if (auto != null) Warn($"«{n}»: висят и Cookware Cavity, и Cookware Auto Setup — жми кнопки только у Cavity (Auto Setup можно удалить)");
            }
            else if (auto == null) Warn($"«{n}»: нет Cookware Cavity — полость не подогнана по форме (добавь Cookware Cavity → «Найти и собрать»)");
            else Warn($"«{n}»: полость собрана Auto Setup; для ковша/рюмки лучше Cookware Cavity (ручки в сцене)");
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !PotBodyFilter.IsBody(mf, go.transform)) continue;
                if (!mf.sharedMesh.isReadable) Err($"«{n}/{mf.name}»: модель без Read/Write — свечения не будет (Import Settings → Read/Write)");
            }
            CookwareLiquidVisual lv = go.GetComponentInChildren<CookwareLiquidVisual>(true);
            if (lv == null) Err($"«{n}»: нет LiquidAnchor (Cookware Liquid Visual) — воды не видно");
            else
            {
                var so = new SerializedObject(lv);
                var arr = so.FindProperty("_products");
                string[] want = { "Вода", "Уксус", "Острая смесь", "Острый соус" };
                foreach (string w in want)
                {
                    bool found = false;
                    for (int i = 0; arr != null && i < arr.arraySize; i++)
                    {
                        var p = arr.GetArrayElementAtIndex(i).FindPropertyRelative("Product").objectReferenceValue as ProductDef;
                        if (p != null && p.Title == w) found = true;
                    }
                    if (!found) Err($"«{n}»: в LiquidAnchor → Products нет «{w}» → выдели ковш, CatCooks → Острый соус → 2. Добавить жидкости");
                }
                var prof = so.FindProperty("_boxProfile");
                if (prof != null && prof.arraySize < 2) Warn($"«{n}»: у жидкости нет профиля формы ковша — Cookware Cavity → «2. Собрать»");
            }
            // Ковш — часть машины (экскаватор/бульдозер)?
            Rigidbody parentRb = go.transform.parent != null ? go.transform.parent.GetComponentInParent<Rigidbody>() : null;
            bool vehicle = parentRb != null;
            if (vehicle)
            {
                Ok($"«{n}» — часть техники «{parentRb.name}» (Rigidbody у машины)");
                if (go.GetComponent<FishNet.Object.NetworkObject>() != null) Err($"«{n}»: на ковше свой Network Object — убери, сеть ковша идёт через Network Object машины");
                if (go.GetComponent<Rigidbody>() != null) Err($"«{n}»: на ковше свой Rigidbody — убери, ковш двигает рука экскаватора");
                if (go.GetComponent("NetworkTransform") != null) Err($"«{n}»: на ковше Network Transform — убери (поворот ковша уже синхронит машина)");
                if (go.GetComponent<CarriedContainer>() != null || go.GetComponent<PickupItem>() != null) Err($"«{n}»: ковш машины нельзя брать в руки — убери Carried Container / Pickup Item");
                if (parentRb.GetComponentInParent<FishNet.Object.NetworkObject>() == null) Err($"«{n}»: у машины нет Network Object");
            }
            else CheckNet(go);

            // Триггер приёма: пересылка в Cookware нужна, если Rigidbody не на самом ковше.
            if (cw.InsideVolume != null)
            {
                Rigidbody arb = cw.InsideVolume.attachedRigidbody;
                bool needRelay = arb == null || arb.gameObject != go;
                if (needRelay && cw.InsideVolume.GetComponent<CookwareTriggerRelay>() == null)
                    Err($"«{n}»: на Receive нет Cookware Trigger Relay — брошенные продукты не попадут в ковш (Cookware Cavity → «2. Собрать» ещё раз)");
                int food = LayerMask.NameToLayer("Food");
                if (food >= 0)
                {
                    if (Physics.GetIgnoreLayerCollision(cw.InsideVolume.gameObject.layer, food))
                        Err($"«{n}»: слой Receive «{LayerMask.LayerToName(cw.InsideVolume.gameObject.layer)}» не пересекается с Food (Project Settings → Physics) — продукты не примет");
                    Transform cols = go.transform.Find("AutoColliders");
                    if (cols != null && Physics.GetIgnoreLayerCollision(cols.gameObject.layer, food))
                        Err($"«{n}»: слой AutoColliders не сталкивается с Food — продукты провалятся сквозь дно");
                }
            }
        }

        private static void CheckNet(GameObject go)
        {
            if (go.GetComponent<FishNet.Object.NetworkObject>() == null) Err($"«{go.name}»: нет Network Object");
            if (go.GetComponent<Rigidbody>() == null) Warn($"«{go.name}»: нет Rigidbody");
            if (go.GetComponentInChildren<Collider>() == null) Err($"«{go.name}»: нет коллайдера — не взять и струя не попадёт");
        }

        private static bool HasChild(Transform t, string key)
        {
            foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
                if (c.name.ToLowerInvariant().Contains(key)) return true;
            return false;
        }

        private static bool HasRule(ProductDef p, FoodProcess proc)
        {
            foreach (FoodRule r in p.Overrides) if (r.Process == proc) return true;
            return false;
        }

        private static bool HasRuleFrom(ProductDef p, FoodState from, FoodProcess proc)
        {
            foreach (FoodRule r in p.Overrides) if (r.From == from && r.Process == proc) return true;
            return false;
        }

        private static ProductDef FindProduct(string title)
        {
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef d = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (d != null && d.Title == title) return d;
            }
            return null;
        }

        private static void Head(string t) => _sb.AppendLine($"\n■ {t}");
        private static void Ok(string t) => _sb.AppendLine("  ✔ " + t);
        private static void Warn(string t) { _warn++; _sb.AppendLine("  ⚠ " + t); }
        private static void Err(string t) { _err++; _sb.AppendLine("  ✖ " + t); }
    }
}
#endif
