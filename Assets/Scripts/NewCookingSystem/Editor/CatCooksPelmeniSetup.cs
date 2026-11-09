#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DS = CatCooks.Food.CatCooksDessertSetup;

namespace CatCooks.Food
{
    /// <summary>
    /// ПЕЛЬМЕНИ, этап 1: тесто. Префабы НЕ создаём — только продукты, компоненты в сцене и проверки.
    ///   1. Продукты и рецепт теста — «Тесто для пельменей» (если нет), проверка воды/муки/яиц/соли, книга рецептов.
    ///   2. Ком теста из тары — DoughSheetDump на FoodDatabase: замес вываливается комом большого теста
    ///      (префаб — заготовка куска из меню Ньокки → 6, она уже в Spawnable Prefabs).
    ///   3. Проверить.
    /// Пробный ком без замеса — по-прежнему Ньокки → 4б.
    /// </summary>
    public static class CatCooksPelmeniSetup
    {
        private const string Root = "CatCooks/Пельмени/";

        // ───────────────────────── 1. ПРОДУКТЫ И РЕЦЕПТ ─────────────────────────

        [MenuItem(Root + "1. Продукты и рецепты (тесто, начинка)", priority = 200)]
        public static void MakeProducts()
        {
            var log = new StringBuilder();
            string dir = DS.ProductsDir();
            ushort next = (ushort)(DS.MaxId() + 1);
            FoodGroupDef dough = EnsureDoughGroup(log);
            // уже есть, но название чуть другое (пробел, регистр, ё) — поправить название, а не плодить второй продукт
            ProductDef loose = FindLoose(CookRecipeBook.Pelmeni.Dough);
            if (loose != null && loose.Title != CookRecipeBook.Pelmeni.Dough)
            {
                log.AppendLine($"✔ нашёл «{loose.Title}» ({AssetDatabase.GetAssetPath(loose)}) — название поправлено на «{CookRecipeBook.Pelmeni.Dough}»");
                loose.Title = CookRecipeBook.Pelmeni.Dough;
                EditorUtility.SetDirty(loose);
            }
            ProductDef d = DS.Ensure(CookRecipeBook.Pelmeni.Dough, dough, new Color(0.95f, 0.9f, 0.78f), 0.3f, true, dir, ref next, log);
            if (d != null && dough != null && d.Group != dough) { d.Group = dough; EditorUtility.SetDirty(d); log.AppendLine("  ✔ «Тесто для пельменей» → группа «Тесто» (Group_Dough)"); }
            if (d != null)
            {
                if (d.Group == null || d.Group.name != "Group_Dough") log.AppendLine($"⚠ «{d.Title}» в группе «{(d.Group != null ? d.Group.name : "—")}», а нужна Group_Dough (стартовое «Замешано»)");
                if (d.Solid) { d.Solid = false; EditorUtility.SetDirty(d); log.AppendLine($"  ✔ «{d.Title}»: снял Solid (берётся мастерком)"); }
            }
            // готовые пельмени (лопатой с пласта → кастрюля) и тесто для ньокков (замес из пюре)
            foreach (string t in new[] { PelmeniProduct.Title, "Тесто для ньокки" })
            {
                ProductDef x = DS.Ensure(t, dough, t == PelmeniProduct.Title ? new Color(0.96f, 0.93f, 0.84f) : new Color(0.97f, 0.84f, 0.48f), 0.12f, true, dir, ref next, log);
                if (x == null) continue;
                if (dough != null && x.Group != dough) { x.Group = dough; EditorUtility.SetDirty(x); log.AppendLine($"  ✔ «{t}» → группа «Тесто»"); }
                if (x.Solid) { x.Solid = false; EditorUtility.SetDirty(x); }
            }
            log.AppendLine(DS.Find("Картофель") != null ? "✔ «Картофель» есть (тесто ньокки — из варёной / пюре)" : "✖ нет «Картофель» — рецепт теста ньокки не соберётся");
            log.AppendLine();
            foreach (string t in new[] { CookRecipeBook.Pelmeni.Water, CookRecipeBook.Pelmeni.Flour, CookRecipeBook.Pelmeni.Egg, CookRecipeBook.Pelmeni.Salt })
                log.AppendLine(DS.Find(t) != null ? $"✔ «{t}» есть" : $"✖ нет продукта «{t}» — без него рецепт не соберётся");

            // ── начинка ──
            log.AppendLine("\nНАЧИНКА:");
            FoodGroupDef mince = EnsureMinceGroup(log);
            ProductDef fill = DS.Ensure(CookRecipeBook.Pelmeni.Filling, mince, new Color(0.66f, 0.42f, 0.36f), 0.1f, true, dir, ref next, log);
            if (fill != null && mince != null && fill.Group != mince) { fill.Group = mince; EditorUtility.SetDirty(fill); log.AppendLine("  ✔ «Фарш для пельменей» → группа «Фарш» (берётся мастерком как масса)"); }
            if (fill != null && fill.Solid) { fill.Solid = false; EditorUtility.SetDirty(fill); }
            FoodGroupDef herbs = DS.GroupOf("Шалфей") ?? DS.FindGroup("Group_Spices");
            ProductDef dill = DS.Ensure(CookRecipeBook.Pelmeni.Dill, herbs, new Color(0.32f, 0.55f, 0.22f), 0.05f, false, dir, ref next, log);
            if (dill != null && dill.DebrisColor.a < 0.01f) { dill.DebrisColor = new Color(0.32f, 0.55f, 0.22f, 1f); EditorUtility.SetDirty(dill); }
            // фарш из измельчителя — в группу «Фарш» (масса: берётся мастерком, не «целый кусок»)
            ProductDef beef = DS.Find(CookRecipeBook.Pelmeni.Mince);
            if (beef == null) beef = DS.Ensure(CookRecipeBook.Pelmeni.Mince, mince, new Color(0.70f, 0.28f, 0.25f), 0.08f, true, dir, ref next, log);
            ProductDef pork = DS.Ensure(CookRecipeBook.Pelmeni.PorkMince, mince, new Color(0.86f, 0.55f, 0.52f), 0.08f, true, dir, ref next, log);
            foreach (ProductDef m in new[] { beef, pork })
            {
                if (m == null || mince == null) continue;
                if (m.Group != mince) { log.AppendLine($"  ↪ «{m.Title}» → группа «Фарш» (была {(m.Group != null ? m.Group.name : "—")}) — теперь масса, мастерком"); m.Group = mince; EditorUtility.SetDirty(m); }
                if (m.Solid) { m.Solid = false; EditorUtility.SetDirty(m); }
            }
            log.AppendLine("✔ измельчитель: любой сырой кусок говядины → «Говяжий фарш», свинины → «Свиной фарш»");
            log.AppendLine(DS.Find(CookRecipeBook.Pelmeni.Onion) != null ? "✔ «Лук репчатый» есть" : "✖ нет «Лук репчатый»");
            log.AppendLine(DS.Find(CookRecipeBook.Pelmeni.Pepper) != null || DS.Find("Перец черный") != null ? "✔ «Перец чёрный» есть" : "✖ нет «Перец чёрный» (баночка специй)");
            AssetDatabase.SaveAssets();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null)
            {
                db.CollectFromProject();
                EditorUtility.SetDirty(db);
                EditorSceneManager.MarkSceneDirty(db.gameObject.scene);
                log.AppendLine("\n✔ FoodDatabase в сцене пересобран — СОХРАНИ СЦЕНУ");
            }
            else log.AppendLine("\n⚠ в открытой сцене нет FoodDatabase — нажми у него «Collect From Project»");
            CookRecipeBook.Reload();
            log.AppendLine("\nРЕЦЕПТ «Тесто для пельменей» (книга соберёт сама в Play, на 1 ведро воды = один ком):");
            log.AppendLine("  вода 1 ведро (ровно полное) + мука 12 мастерков (можно 9–15) + яйцо 2 шт. + соль 1 мастерок");
            log.AppendLine("  перемешать 10 с — бетономешалка (барабан крутится) или миксер в ведре → тугое тесто, 32 порции");
            log.AppendLine("\nРЕЦЕПТ «Тесто для ньокки» (партия на 3 картофелины, больше — кратно): картофель варёный или пюре 3 шт. + мука 4 мастерка (3–5) + яйцо 1 + соль 1 доза;");
            log.AppendLine("  отбойником пюре и миксером 8 с — или всё сразу в бетономешалку (варёную тоже), барабан 8 с.");
            log.AppendLine("\nРЕЦЕПТ «Фарш для пельменей» — на 1 мастерок лука (≈12 пельменей), больше лука — всё кратно:");
            log.AppendLine("  лук тёртый 1 мастерок + фарш 3 мастерка (говяжий, свиной или вперемешку) + укроп 2 дозы + соль 1 доза + перец 1 доза");
            log.AppendLine("  всё в одну тару (банка, бетономешалка, ведро) — измельчитель сыплет прямо в тару под выходом — миксером 5 с.");
            log.AppendLine("\nДальше: меню 2 (ком из тары), 4 (ведро), 5 (пучок укропа), 6 (плоскогубцы).");
            DS.Done("Пельмени — продукты и рецепт", log);
        }

        // ───────────────────────── 2. КОМ ИЗ ТАРЫ ─────────────────────────

        [MenuItem(Root + "2. Ком теста из тары (бетономешалка, ведро)", priority = 201)]
        public static void SetupDump()
        {
            var log = new StringBuilder();
            GameObject pf = PickBallPrefab(log);
            if (pf == null)
            {
                log.AppendLine("✖ Нет префаба кома. Сделай заготовку куска теста: CatCooks → Ньокки → 6 → перетащи в Project (префаб) →");
                log.AppendLine("   Network Manager → Spawnable Prefabs → выдели префаб в Project и нажми это меню ещё раз.");
                DS.Done("Пельмени — ком теста из тары", log);
                return;
            }
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            GameObject host;
            if (db != null) host = db.gameObject;
            else
            {
                host = GameObject.Find("DoughSheetDump");
                if (host == null) { host = new GameObject("DoughSheetDump"); Undo.RegisterCreatedObjectUndo(host, "Ком теста из тары"); log.AppendLine("＋ «DoughSheetDump» в сцене (FoodDatabase не нашёл)"); }
            }
            bool had = host.GetComponent<DoughSheetDump>() != null;
            DoughSheetDump dump = DS.Need<DoughSheetDump>(host);
            dump.EditorSetup(pf);
            log.AppendLine($"{(had ? "✔ обновлён" : "＋")} Dough Sheet Dump на «{host.name}»: префаб кома «{pf.name}», обычный ком = {dump.EditorPortionsPerBall} порц. (одна партия рецепта)");
            // шар в руки: тот же, что у бетономешалки (FoodPiece + DoughBlob)
            GameObject carry = null;
            foreach (MixerDough m in Object.FindObjectsByType<MixerDough>(FindObjectsSortMode.None))
                if (m != null && m.BallPrefab != null && m.BallPrefab.GetComponent<FoodPiece>() != null) { carry = m.BallPrefab; break; }
            if (carry == null)
                foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
                {
                    GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                    if (go != null && go.GetComponent<DoughBlob>() != null && go.GetComponent<FoodPiece>() != null) { carry = go; break; }
                }
            if (carry != null) { dump.EditorSetCarry(carry); log.AppendLine($"✔ шар в руки: «{carry.name}» (как у хлебного теста) — тесто вываливается шаром, бери F, бросай ЛКМ — где лёг, там большое тесто"); }
            else log.AppendLine("⚠ шара в руки нет (нет префаба с Food Piece + Dough Blob — Хлеб → бетономешалка) — тесто сразу ляжет комом большого теста");
            bool net = HasType(pf, "NetworkObject");
            log.AppendLine(net ? "✔ у префаба есть Network Object" : "✖ у префаба НЕТ Network Object — сделай заготовку меню Ньокки → 6 заново");
            log.AppendLine("  ⚠ проверь, что он в Network Manager → Spawnable Prefabs (для нарезки ньокков он уже нужен был)");

            var mixers = Object.FindObjectsByType<MixerDough>(FindObjectsSortMode.None);
            log.AppendLine(mixers.Length > 0 ? $"\n✔ бетономешалка с тестом: {mixers.Length} шт. — пельменное тесто вывалится комом при повороте бочки"
                                             : "\n⚠ бетономешалки с Mixer Dough в сцене нет — месить можно миксером в ведре");
            log.AppendLine("\nКАК ВЫВАЛИТЬ:");
            log.AppendLine("  • бетономешалка: барабан выключить → бочку ручкой повернуть → ком из горловины (по партии за раз);");
            log.AppendLine("  • ведро: наклонить сильно (как высыпать остатки) → всё тесто выходит одним комом в сторону горловины.");
            log.AppendLine("  Подстели РУЛОН под горловину — на голом полу тесто сразу грязное.");
            EditorSceneManager.MarkSceneDirty(host.scene);
            DS.Done("Пельмени — ком теста из тары", log);
        }

        /// <summary>Выделенный префаб куска теста, иначе — Piece Prefab теста в сцене, иначе — любой такой префаб в проекте.</summary>
        private static GameObject PickBallPrefab(StringBuilder log)
        {
            GameObject sel = Selection.activeObject as GameObject;
            if (IsBallPrefab(sel)) { log.AppendLine($"✔ взял выделенный префаб «{sel.name}»"); return sel; }
            foreach (DoughSheet d in Object.FindObjectsByType<DoughSheet>(FindObjectsSortMode.None))
                if (!d.IsPieceTemplate && IsBallPrefab(d.PiecePrefab)) { log.AppendLine($"✔ взял префаб куска у теста «{d.name}»: «{d.PiecePrefab.name}»"); return d.PiecePrefab; }
            foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
            {
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                if (IsBallPrefab(go)) { log.AppendLine($"✔ нашёл в проекте префаб куска теста «{go.name}» ({AssetDatabase.GetAssetPath(go)})"); return go; }
            }
            return null;
        }

        private static bool IsBallPrefab(GameObject go)
        {
            if (go == null || !EditorUtility.IsPersistent(go)) return false;
            DoughSheet ds = go.GetComponent<DoughSheet>();
            return ds != null && ds.IsPieceTemplate;
        }

        // ───────────────────────── 2б. ПРОБНЫЙ КОМ ─────────────────────────

        [MenuItem(Root + "2б. Пробный ком пельменного теста в сцену (без замеса)", priority = 201)]
        public static void TestBall() => CatCooksGnocchiSetup.MakeDoughPelmeni();

        // ───────────────────────── 4. ВЕДРО-ВЫРУБКА ─────────────────────────

        [MenuItem(Root + "4. Ведро-вырубка (выдели ведро в сцене)", priority = 203)]
        public static void SetupBucketCutter()
        {
            var log = new StringBuilder();
            GameObject[] sel = Selection.gameObjects;
            float ideal = 0.4f * Mathf.Max(0.25f, MeatCutSettings.WorldScale) / 4f;
            int k = 0;
            foreach (GameObject go0 in sel)
            {
                if (go0 == null || EditorUtility.IsPersistent(go0)) continue;
                Cookware cw = go0.GetComponentInParent<Cookware>();
                if (cw == null) { log.AppendLine($"✖ «{go0.name}»: не тара (нет Cookware) — выдели ведро"); continue; }
                GameObject go = cw.gameObject;
                bool had = go.GetComponent<CatCooks.Food.Tools.BucketDoughCutter>() != null;
                var cut = DS.Need<CatCooks.Food.Tools.BucketDoughCutter>(go);
                float r = MeasureRim(go);
                cut.EditorSetup(r);
                k++;
                float rel = r / ideal;
                string verdict = rel < 0.8f ? "мелковато — пельмени выйдут маленькие (минус к оценке)" : rel > 1.25f ? "крупновато — пельмени выйдут большие (минус к оценке)" : "в самый раз ✔";
                log.AppendLine($"{(had ? "✔ обновлён" : "＋")} «{go.name}»: Bucket Dough Cutter, кромка R = {r:0.00} м → кружок Ø {r * 200f:0} см (норма Ø {ideal * 200f:0} см) — {verdict}");
                if (go.GetComponent<CarriedContainer>() == null) log.AppendLine("  ⚠ нет Carried Container — ведро не берётся в руки как тара");
                if (!HasType(go, "NetworkObject")) log.AppendLine("  ✖ нет Network Object!");
                if (PrefabUtility.IsPartOfPrefabInstance(go)) log.AppendLine("  ⚠ это префаб: Overrides → Apply All (иначе у заспавненных вёдер компонента не будет)");
                EditorSceneManager.MarkSceneDirty(go.scene);
            }
            if (k == 0) log.Insert(0, "✖ Выдели в сцене ведро (тару с Cookware), потом это меню.\n\n");
            log.AppendLine("\nКАК: раскатай пельменный пласт до 4–5.5 см → возьми ПУСТОЕ ведро → смотри на пласт (зелёное кольцо) →");
            log.AppendLine("ЛКМ ДЕРЖАТЬ — ведро прижалось к тесту → поворачивай мышью (обзор) — ведро проворачивается → на 90° — кружок вырезан.");
            log.AppendLine("Потом начинка мастерком и плоскогубцы (меню 6).");
            log.AppendLine("Радиус не тот — поле Radius у Bucket Dough Cutter (0 — сам).");
            DS.Done("Пельмени — ведро-вырубка", log);
        }

        /// <summary>Радиус кромки ведра: верхний пояс вершин модели (последние 15% по высоте), м.</summary>
        private static float MeasureRim(GameObject go)
        {
            Bounds b = DS.LocalBounds(go.transform);
            float sc = Mathf.Abs(go.transform.lossyScale.x) > 1e-4f ? Mathf.Abs(go.transform.lossyScale.x) : 1f;
            float top = b.max.y - b.size.y * 0.15f, rmax = 0f;
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                bool skip = false;
                for (Transform p = mf.transform; p != null && p != go.transform; p = p.parent) if (p.name == "Fill" || p.name == "LiquidAnchor") { skip = true; break; }
                if (skip) continue;
                foreach (Vector3 v in mf.sharedMesh.vertices)
                {
                    Vector3 l = go.transform.InverseTransformPoint(mf.transform.TransformPoint(v));
                    if (l.y < top) continue;
                    float d = new Vector2(l.x - b.center.x, l.z - b.center.z).magnitude;
                    if (d > rmax) rmax = d;
                }
            }
            if (rmax <= 1e-4f) rmax = Mathf.Max(b.extents.x, b.extents.z);
            return rmax * sc;
        }

        // ───────────────────────── 5. ПУЧОК УКРОПА ─────────────────────────

        [MenuItem(Root + "5. Пучок укропа — настроить (выдели модель пучка)", priority = 204)]
        public static void SetupDillBunch()
        {
            var log = new StringBuilder();
            ProductDef dill = DS.Find(CookRecipeBook.Pelmeni.Dill);
            GameObject go = Selection.activeGameObject;
            if (go == null || dill == null) { DS.Done("Пельмени — пучок укропа", new StringBuilder(dill == null ? "✖ Нет продукта «Укроп» — сначала меню 1." : "✖ Выдели корень модели пучка (в сцене или префаб в Project).")); return; }
            foreach (PickupItem other in go.GetComponents<PickupItem>())
            {
                if (other is CatCooks.Food.Tools.HerbBunchItem) continue;
                if (other.GetType() == typeof(PickupItem)) { log.AppendLine("－ простой Pickup Item убран — его заменяет Herb Bunch Item"); Undo.DestroyObjectImmediate(other); }
                else log.AppendLine($"⚠ на пучке уже «{other.GetType().Name}» — убери руками");
            }
            Bounds lb = DS.LocalBounds(go.transform);
            if (go.GetComponentInChildren<Collider>() == null)
            {
                BoxCollider bc = Undo.AddComponent<BoxCollider>(go);
                bc.center = lb.center; bc.size = Vector3.Max(lb.size, Vector3.one * 0.02f);
                log.AppendLine("  ＋ Box Collider по модели");
            }
            Rigidbody rb = DS.Need<Rigidbody>(go);
            rb.mass = Mathf.Clamp(rb.mass, 0.2f, 1f); rb.interpolation = RigidbodyInterpolation.Interpolate;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            ClientAuthNT(go, log);
            Transform top = go.transform.Find("Top");
            if (top == null)
            {
                top = DS.FindOrCreate(go.transform, "Top");
                top.localPosition = new Vector3(lb.center.x, Mathf.Lerp(lb.center.y, lb.max.y, 0.8f), lb.center.z);
                log.AppendLine("  ＋ Top — верх пучка (подвинь к листьям, если нужно)");
            }
            var hb = DS.Need<CatCooks.Food.Tools.HerbBunchItem>(go);
            hb.EditorSetup(dill, top);
            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"＋ «{go.name}»: Herb Bunch Item — «Укроп», 1 доза за рывок, бесконечный");
            log.AppendLine("\nВ ИГРЕ: взять пучок (F) → смотришь в миску с фаршем → ПКМ — рывок, укроп падает (+1 доза). Для начинки — 2 дозы.");
            if (PrefabUtility.IsPartOfPrefabInstance(go)) log.AppendLine("Это экземпляр префаба — Overrides → Apply All.");
            DS.Done("Пельмени — пучок укропа", log);
        }

        // ───────────────────────── 6. ПЛОСКОГУБЦЫ ─────────────────────────

        [MenuItem(Root + "6. Плоскогубцы (выдели модель или сделаю простые)", priority = 205)]
        public static void SetupPliers()
        {
            var log = new StringBuilder();
            GameObject go = Selection.activeGameObject;
            if (go != null && EditorUtility.IsPersistent(go)) go = null;
            if (go != null && go.GetComponent<Cookware>() != null) go = null;               // ведро выделено по привычке — не трогаем
            if (go == null)
            {
                go = new GameObject("Pliers");
                Undo.RegisterCreatedObjectUndo(go, "Плоскогубцы");
                go.transform.position = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
                BuildSimplePliers(go.transform, log);
            }
            else Undo.RegisterFullObjectHierarchyUndo(go, "Плоскогубцы");
            foreach (PickupItem other in go.GetComponents<PickupItem>())
            {
                if (other is CatCooks.Food.Tools.PliersItem) continue;
                if (other.GetType() == typeof(PickupItem)) { log.AppendLine("－ простой Pickup Item убран — его заменяет Pliers Item"); Undo.DestroyObjectImmediate(other); }
                else log.AppendLine($"⚠ на модели уже «{other.GetType().Name}» — убери руками (два предмета на одном объекте)");
            }
            Bounds lb = DS.LocalBounds(go.transform);
            if (go.GetComponentInChildren<Collider>() == null)
            {
                BoxCollider bc = Undo.AddComponent<BoxCollider>(go);
                bc.center = lb.center; bc.size = Vector3.Max(lb.size, Vector3.one * 0.03f);
                log.AppendLine("  ＋ Box Collider по модели");
            }
            Rigidbody rb = DS.Need<Rigidbody>(go);
            rb.mass = Mathf.Clamp(rb.mass, 0.5f, 3f); rb.interpolation = RigidbodyInterpolation.Interpolate;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            ClientAuthNT(go, log);
            DS.Need<CatCooks.Food.Tools.PliersItem>(go);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
            log.AppendLine($"＋ «{go.name}»: Pliers Item");
            log.AppendLine("\nКАК (по кружку с начинкой):");
            log.AppendLine("  1. ЛКМ ДЕРЖАТЬ, глядя на кружок — он складывается пополам (шов к тебе).");
            log.AppendLine("  2. Не отпуская — веди прицел по шву (дуге края) от конца до конца: внизу полоска ███░░.");
            log.AppendLine("  3. Отпусти — шов зажат (нужно ≥75% края). Мало — зажми ещё раз и доведи.");
            log.AppendLine("  4. ПКМ — концы загибаются «ушком», пельмень готов.");
            log.AppendLine("\nДАЛЬШЕ РУКАМИ: перетащи в Project — префаб → Network Manager → Spawnable Prefabs (если спавнишь по сети).");
            DS.Done("Пельмени — плоскогубцы", log);
        }

        /// <summary>Простые плоскогубцы из кубиков (две ручки, губки), если своей модели нет. Без коллайдеров у деталей.</summary>
        private static void BuildSimplePliers(Transform root, StringBuilder log)
        {
            float k = Mathf.Max(0.25f, MeatCutSettings.WorldScale) / 4f * 2f;            // крупный мир — крупный инструмент
            Material red = MatAsset("Mat_PliersGrip", new Color(0.78f, 0.12f, 0.1f), 0.3f, log);
            Material steel = MatAsset("Mat_PliersSteel", new Color(0.62f, 0.64f, 0.66f), 0.75f, log);
            Part(root, "GripL", new Vector3(-0.025f, 0f, -0.12f) * k, new Vector3(0f, 6f, 0f), new Vector3(0.03f, 0.025f, 0.2f) * k, red);
            Part(root, "GripR", new Vector3(0.025f, 0f, -0.12f) * k, new Vector3(0f, -6f, 0f), new Vector3(0.03f, 0.025f, 0.2f) * k, red);
            Part(root, "Pivot", new Vector3(0f, 0f, 0.0f) * k, Vector3.zero, new Vector3(0.05f, 0.03f, 0.04f) * k, steel);
            Part(root, "JawL", new Vector3(-0.008f, 0f, 0.06f) * k, new Vector3(0f, -3f, 0f), new Vector3(0.016f, 0.022f, 0.09f) * k, steel);
            Part(root, "JawR", new Vector3(0.008f, 0f, 0.06f) * k, new Vector3(0f, 3f, 0f), new Vector3(0.016f, 0.022f, 0.09f) * k, steel);
            log.AppendLine("＋ «Pliers» — простые плоскогубцы из брусков (своя модель лучше: выдели её и нажми меню снова)");
        }

        private static void Part(Transform root, string n, Vector3 pos, Vector3 euler, Vector3 size, Material m)
        {
            GameObject p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(p, n);
            p.name = n;
            Object.DestroyImmediate(p.GetComponent<Collider>());
            p.transform.SetParent(root, false);
            p.transform.localPosition = pos; p.transform.localEulerAngles = euler; p.transform.localScale = size;
            p.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        private static Material MatAsset(string name, Color c, float smooth, StringBuilder log)
        {
            const string dir = "Assets/CatCooks/Food/Models/Pelmeni";
            string path = dir + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            DS.EnsureDir(dir);
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"  ＋ {path}");
            return m;
        }

        private static void ClientAuthNT(GameObject go, StringBuilder log)
        {
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null || c.GetType().Name != "NetworkTransform") continue;
                var so = new SerializedObject(c);
                SerializedProperty a = so.FindProperty("_clientAuthoritative"); if (a != null) a.boolValue = true;
                SerializedProperty sd = so.FindProperty("_sendToOwner"); if (sd != null) sd.boolValue = true;
                so.ApplyModifiedProperties();
                log.AppendLine("  ✔ Network Transform: Client Authoritative");
            }
        }

        /// <summary>Group_Mince — «Фарш», стартовое «Фарш» (как в «Срез 10 блюд»). Нет — создаём рядом с другими группами.</summary>
        private static FoodGroupDef EnsureMinceGroup(StringBuilder log)
        {
            FoodGroupDef g = DS.FindGroup("Group_Mince");
            if (g != null) { log.AppendLine($"✔ группа «Фарш» есть"); return g; }
            FoodGroupDef dough = DS.FindGroup("Group_Dough");
            string dir = dough != null ? System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(dough)).Replace('\\', '/') : "Assets/CatCooks/Food/Groups";
            DS.EnsureDir(dir);
            g = ScriptableObject.CreateInstance<FoodGroupDef>();
            g.Title = "Фарш"; g.StartState = FoodState.Minced;
            g.Note = "Фарш как самостоятельная масса: начинка, котлеты.";
            AssetDatabase.CreateAsset(g, $"{dir}/Group_Mince.asset");
            log.AppendLine($"＋ группа «Фарш» ({dir}/Group_Mince.asset), стартовое «Фарш»");
            return g;
        }

        // ───────────────────────── 3. ПРОВЕРКА ─────────────────────────

        [MenuItem(Root + "3. Проверить (тесто)", priority = 202)]
        public static void Check()
        {
            var sb = new StringBuilder();
            int err = 0;
            ProductDef d = DS.Find(CookRecipeBook.Pelmeni.Dough);
            if (d == null)
            {
                ProductDef loose = FindLoose(CookRecipeBook.Pelmeni.Dough);
                if (loose != null) sb.AppendLine($"✖ продукт есть, но название не точное: «{loose.Title}» ({AssetDatabase.GetAssetPath(loose)}) — меню 1 поправит");
                else sb.AppendLine("✖ нет ПРОДУКТА «Тесто для пельменей» (ассет ProductDef; ком в сцене из 4б — это не он) — нажми меню 1, оно создаст");
                string similar = Similar("пельмен");
                if (similar.Length > 0) sb.AppendLine("   похожие продукты в проекте: " + similar);
                err++;
            }
            else
            {
                bool grp = d.Group != null && d.Group.name == "Group_Dough";
                sb.AppendLine($"{(grp ? "✔" : "⚠")} «{d.Title}» (Id {d.Id}), группа {(d.Group != null ? d.Group.name : "—")}{(grp ? "" : " — нужна Group_Dough")}");
            }
            foreach (string t in new[] { CookRecipeBook.Pelmeni.Water, CookRecipeBook.Pelmeni.Flour, CookRecipeBook.Pelmeni.Egg, CookRecipeBook.Pelmeni.Salt })
                if (DS.Find(t) == null) { sb.AppendLine($"✖ нет продукта «{t}» (рецепт теста)"); err++; }
            if (d != null) sb.AppendLine("✔ вода, мука, яйцо, соль — рецепт «Тесто для пельменей» соберётся в Play (лог «[Рецепты] в книге: … Тесто для пельменей»)");

            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db == null) { sb.AppendLine("✖ в сцене нет FoodDatabase"); err++; }

            DoughSheetDump dump = Object.FindFirstObjectByType<DoughSheetDump>();
            GameObject pf = dump != null ? dump.EditorBallPrefab : null;
            if (dump == null)
            {
                GameObject fallback = null;
                foreach (DoughSheet s in Object.FindObjectsByType<DoughSheet>(FindObjectsSortMode.None)) if (!s.IsPieceTemplate && s.PiecePrefab != null) { fallback = s.PiecePrefab; break; }
                sb.AppendLine(fallback != null ? $"⚠ нет Dough Sheet Dump — меню 2. Пока ком возьмёт префаб куска у теста в сцене («{fallback.name}»), но лучше нажать 2"
                                               : "✖ нет Dough Sheet Dump — меню 2 (без него замес высыплется кучкой)");
                if (fallback == null) err++;
            }
            else if (!IsBallPrefab(pf)) { sb.AppendLine($"✖ Dough Sheet Dump на «{dump.name}»: префаб кома не задан или не тот — меню 2"); err++; }
            else sb.AppendLine($"{(HasType(pf, "NetworkObject") ? "✔" : "✖")} ком из тары: префаб «{pf.name}», обычный ком = {dump.EditorPortionsPerBall} порц.{(HasType(pf, "NetworkObject") ? "" : " — у префаба нет Network Object!")}");
            if (dump != null) sb.AppendLine(dump.EditorCarryPrefab != null ? $"✔ шар теста в руки: «{dump.EditorCarryPrefab.name}»" : "⚠ шар теста в руки не задан — меню 2 (иначе тесто сразу ложится комом, в руки не взять)");

            var mixers = Object.FindObjectsByType<MixerDough>(FindObjectsSortMode.None);
            int mixItems = 0;
            foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)) if (mb != null && mb.GetType().Name == "MixerItem") mixItems++;
            if (mixers.Length == 0 && mixItems == 0) { sb.AppendLine("✖ нечем месить: нет ни бетономешалки (Mixer Dough), ни миксера (MixerItem) в сцене"); err++; }
            else sb.AppendLine($"✔ чем месить: бетономешалка {mixers.Length} шт., миксер {mixItems} шт.");

            sb.AppendLine(Object.FindObjectsByType<WaterproofRollItem>(FindObjectsSortMode.None).Length > 0 ? "✔ рулон-подстилка есть" : "⚠ рулона нет — Ньокки → 2 (без подстилки тесто грязное)");
            sb.AppendLine(Object.FindObjectsByType<RoadRollerVehicle>(FindObjectsSortMode.None).Length > 0 ? "✔ каток есть" : "⚠ катка нет — Ньокки → 1 (раскатать до 4–5.5 см)");
            sb.AppendLine(Object.FindObjectsByType<CatCooks.Food.Tools.ToolFlourCoat>(FindObjectsSortMode.None).Length > 0 ? "✔ мука на кувалде (Tool Flour Coat)" : "⚠ на кувалде нет Tool Flour Coat — Ньокки → 5");
            var cutters = Object.FindObjectsByType<CatCooks.Food.Tools.BucketDoughCutter>(FindObjectsSortMode.None);
            if (cutters.Length == 0) sb.AppendLine("⚠ ведра-вырубки нет — выдели ведро → меню 4 (кружки для пельменей)");
            foreach (var c in cutters) sb.AppendLine($"✔ ведро-вырубка «{c.name}»: R = {(c.EditorRadius > 0.01f ? c.EditorRadius.ToString("0.00") + " м" : "сам")}");
            if (DS.Find(PelmeniProduct.Title) == null) { sb.AppendLine("✖ нет продукта «Пельмени» — меню 1 (без него лопата не берёт пельмени с пласта)"); err++; }
            else sb.AppendLine("✔ «Пельмени» есть — лопатой / мастерком с пласта");
            sb.AppendLine(DS.Find("Тесто для ньокки") != null ? "✔ «Тесто для ньокки» есть (замес из пюре)" : "⚠ нет «Тесто для ньокки» — меню 1");
            // начинка
            if (DS.Find(CookRecipeBook.Pelmeni.Filling) == null) { sb.AppendLine("✖ нет продукта «Фарш для пельменей» — меню 1"); err++; }
            else sb.AppendLine("✔ «Фарш для пельменей» есть");
            if (DS.Find(CookRecipeBook.Pelmeni.Mince) == null && DS.Find(CookRecipeBook.Pelmeni.PorkMince) == null) sb.AppendLine("⚠ нет «Говяжий фарш» — рецепт начинки не соберётся (меню 1)");
            else
            {
                ProductDef bm = DS.Find(CookRecipeBook.Pelmeni.Mince);
                if (bm != null && (bm.Group == null || bm.Group.name != "Group_Mince")) sb.AppendLine($"⚠ «Говяжий фарш» в группе {(bm.Group != null ? bm.Group.name : "—")} — меню 1 перенесёт в «Фарш» (иначе куча фарша — «целым куском»)");
            }
            // туша — откуда мясо
            MeatCutSettings mcs = Resources.Load<MeatCutSettings>("MeatCutSettings");
            if (mcs == null) sb.AppendLine("⚠ туша: нет настроек кусков — CatCooks → Мясо → Новые туши → 1");
            else if (mcs.CutPrefab == null) sb.AppendLine("✖ туша: Cut Prefab пуст — режутся только рёбра и брискет! Мясо → Новые туши → 4 (заготовка) → префаб → Spawnable Prefabs → 6");
            else sb.AppendLine($"✔ туша: куски выпадают префабом «{mcs.CutPrefab.name}» (в Spawnable Prefabs?)");
            if (DS.Find(CookRecipeBook.Pelmeni.Onion) == null) sb.AppendLine("⚠ нет «Лук репчатый» — рецепт начинки не соберётся");
            if (DS.Find(CookRecipeBook.Pelmeni.Dill) == null && DS.Find("Петрушка") == null && DS.Find("Шалфей") == null) sb.AppendLine("⚠ нет зелени (Укроп) — меню 1");
            if (DS.Find(CookRecipeBook.Pelmeni.Pepper) == null && DS.Find("Перец черный") == null) sb.AppendLine("⚠ нет «Перец чёрный» — рецепт начинки не соберётся");
            bool dillBunch = false;
            foreach (var hb in Object.FindObjectsByType<CatCooks.Food.Tools.HerbBunchItem>(FindObjectsSortMode.None))
            {
                Object pr = new SerializedObject(hb).FindProperty("_product") is SerializedProperty sp ? sp.objectReferenceValue : null;
                if (pr is ProductDef pd && pd.Title == CookRecipeBook.Pelmeni.Dill) dillBunch = true;
            }
            sb.AppendLine(dillBunch ? "✔ пучок укропа есть" : "⚠ пучка укропа нет — выдели модель пучка → меню 5 (или в рецепт пойдёт петрушка/шалфей)");
            sb.AppendLine(Object.FindObjectsByType<CatCooks.Food.Tools.PliersItem>(FindObjectsSortMode.None).Length > 0 ? "✔ плоскогубцы есть" : "✖ плоскогубцев нет — меню 6 (лепка пельменей)");
            if (Object.FindObjectsByType<CatCooks.Food.Tools.PliersItem>(FindObjectsSortMode.None).Length == 0) err++;
            sb.AppendLine("\nПробный ком без замеса — меню 2б (или Ньокки → 4б).");
            sb.Insert(0, err == 0 ? "ВСЁ ГОТОВО ✔\n\n" : $"Ошибок: {err}\n\n");
            DS.Done("Пельмени — проверка", sb);
        }

        // ───────────────────────── группа «Тесто» ─────────────────────────

        private static FoodRule R(FoodState from, FoodProcess p, FoodState to, float sec, float window = 0f,
                                  FoodState early = FoodState.None, FoodState late = FoodState.None)
            => new FoodRule { From = from, Process = p, To = to, Seconds = sec, Window = window, TooEarly = early, TooLate = late };

        /// <summary>
        /// Group_Dough — как в «Срез 10 блюд» (Tools → CatCooks → Кулинария), то же имя ассета и те же правила теста,
        /// чтобы потом тот скрипт её узнал. Нет — создаём рядом с другими группами. Есть — дописываем недостающие правила.
        /// </summary>
        private static FoodGroupDef EnsureDoughGroup(StringBuilder log)
        {
            FoodGroupDef g = DS.FindGroup("Group_Dough");
            if (g == null)
            {
                string dir = null;
                foreach (string n in new[] { "Group_SubRecipe", "Group_Bulk", "Group_Liquid", "Group_Vegetables" })
                {
                    FoodGroupDef other = DS.FindGroup(n);
                    if (other != null) { dir = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(other)).Replace('\\', '/'); break; }
                }
                if (string.IsNullOrEmpty(dir)) { dir = "Assets/CatCooks/Food/Groups"; DS.EnsureDir(dir); }
                g = ScriptableObject.CreateInstance<FoodGroupDef>();
                g.Title = "Тесто";
                g.StartState = FoodState.Kneaded;
                g.Note = "Тесто после замеса: отлежаться, раскатать, нарезать или слепить, сварить или испечь.";
                AssetDatabase.CreateAsset(g, $"{dir}/Group_Dough.asset");
                log.AppendLine($"＋ группа «Тесто» ({dir}/Group_Dough.asset), стартовое состояние «Замешано»");
            }
            else log.AppendLine($"✔ группа «Тесто» есть ({AssetDatabase.GetAssetPath(g)})");
            if (g.StartState != FoodState.Kneaded) log.AppendLine($"  ⚠ у Group_Dough стартовое «{FoodStateInfo.NameOf(g.StartState)}», ожидалось «Замешано» — не трогаю, проверь руками");

            FoodState U = FoodState.Undercooked, O = FoodState.Overcooked;
            FoodRule[] rules =
            {
                R(FoodState.Kneaded, FoodProcess.Rest, FoodState.Proofed, 30f),
                R(FoodState.Kneaded, FoodProcess.Roll, FoodState.Rolled, 4f),                 // каток
                R(FoodState.Proofed, FoodProcess.Roll, FoodState.Rolled, 4f),
                R(FoodState.Rolled, FoodProcess.Cut, FoodState.Pieces, 2f),
                R(FoodState.Rolled, FoodProcess.Shape, FoodState.Shaped, 3f),                // пельмени: лепка
                R(FoodState.Kneaded, FoodProcess.Shape, FoodState.Shaped, 3f),
                R(FoodState.Shaped, FoodProcess.Cut, FoodState.Pieces, 2f),
                R(FoodState.Pieces, FoodProcess.Boil, FoodState.Boiled, 8f, 0.35f, U, O),
                R(FoodState.Shaped, FoodProcess.Boil, FoodState.Boiled, 10f, 0.35f, U, O),    // пельмени: варка
            };
            int added = 0;
            Undo.RecordObject(g, "Группа Тесто: правила");
            foreach (FoodRule r in rules)
            {
                bool exists = false;
                foreach (FoodRule x in g.Rules) if (x.From == r.From && x.Process == r.Process) { exists = true; break; }
                if (exists) continue;
                g.Rules.Add(r); added++;
            }
            if (added > 0) { EditorUtility.SetDirty(g); log.AppendLine($"  ＋ правил теста: {added} (замешано → раскатать → слепить → сварить)"); }
            return g;
        }

        /// <summary>Название без мелочей: пробелы по краям и двойные, регистр, ё → е.</summary>
        private static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Trim().ToLowerInvariant().Replace('ё', 'е').Replace(' ', ' ');
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s;
        }

        private static ProductDef FindLoose(string title)
        {
            string want = Norm(title);
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef p = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (p != null && Norm(p.Title) == want) return p;
            }
            return null;
        }

        /// <summary>Продукты, в названии которых есть part: «Название» (группа, путь) через запятую.</summary>
        private static string Similar(string part)
        {
            var sb = new StringBuilder();
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                ProductDef p = AssetDatabase.LoadAssetAtPath<ProductDef>(path);
                if (p == null || !Norm(p.Title).Contains(part)) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append($"«{p.Title}» ({(p.Group != null ? p.Group.name : "без группы")}, {path})");
            }
            return sb.ToString();
        }

        private static bool HasType(GameObject go, string n)
        {
            foreach (Component c in go.GetComponents<Component>()) if (c != null && c.GetType().Name == n) return true;
            return false;
        }
    }
}
#endif
