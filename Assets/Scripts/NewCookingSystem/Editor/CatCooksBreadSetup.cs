#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CatCooks.Food.Tools;

namespace CatCooks.Food
{
    /// <summary>
    /// CatCooks → Хлеб:
    ///   1. Создать продукты, рецепт и материалы — Мука пшеничная, Дрожжи (если нет), Тесто дрожжевое,
    ///      Багет, Буханка; правило «Замешано → Дать подойти → Подошло»; рецепт «Тесто дрожжевое»;
    ///      материалы теста и корочки из текстур в Assets/Models/Bread.
    ///   2. Добавить тесто в выделенную тару (бетономешалка) — тесто видно в бочке густой массой.
    ///   3. Проверить хлеб — ✔/⚠/✖ по всему, ничего не меняет.
    ///   4. Настроить выделенные — заполняет поля и материалы на выделенных объектах
    ///      (шар теста, формы, хлеб, мешалка, баночка дрожжей, мешок муки). Компоненты-«картинки»
    ///      (Food Hot Steam) добавляет сам, сетевые — нет.
    /// </summary>
    public static class CatCooksBreadSetup
    {
        const string RecipeDir = "Assets/CatCooks/Food/Recipes/Resources/CookRecipes";
        const string ModelDir = "Assets/Models/Bread";
        const string Flour = "Мука пшеничная", Yeast = "Дрожжи", Salt = "Соль", Water = "Вода",
                     Dough = "Тесто дрожжевое", Baguette = "Багет", Loaf = "Буханка", Bun = "Булка";

        [MenuItem("CatCooks/Хлеб/1. Создать продукты, рецепт и материалы", priority = 80)]
        public static void CreateAll()
        {
            var log = new StringBuilder();
            string dir = ProductsDir();
            ushort next = (ushort)(MaxId() + 1);
            FoodGroupDef bulk = FindGroup("Group_Bulk") ?? GroupOf("Сахар");
            FoodGroupDef sub = FindGroup("Group_SubRecipe") ?? GroupOf("Бульон");
            FoodGroupDef doughG = FindGroup("Group_Dough") ?? sub;

            ProductDef flour = Ensure(Flour, bulk, new Color(0.95f, 0.92f, 0.84f), 0.01f, false, dir, ref next, log);
            ProductDef yeast = Ensure(Yeast, bulk, new Color(0.86f, 0.8f, 0.64f), 0.01f, false, dir, ref next, log);
            ProductDef salt = Find(Salt), water = Find(Water);
            if (salt == null) log.AppendLine("✖ нет продукта «Соль» (CatCooks → Острый соус → 1 создаёт её)");
            if (water == null) log.AppendLine("✖ нет продукта «Вода»");
            ProductDef dough = Ensure(Dough, doughG, new Color(0.96f, 0.9f, 0.76f), 0.06f, true, dir, ref next, log);
            ProductDef bag = Ensure(Baguette, sub, new Color(0.85f, 0.6f, 0.3f), 0.6f, true, dir, ref next, log);
            ProductDef loaf = Ensure(Loaf, sub, new Color(0.8f, 0.55f, 0.28f), 0.27f, true, dir, ref next, log);
            ProductDef bun = Ensure(Bun, sub, new Color(0.84f, 0.57f, 0.27f), 0.08f, true, dir, ref next, log);   // кусок теста на решётке

            if (dough != null)
            {
                Rule(dough, FoodState.Kneaded, FoodProcess.Rest, FoodState.Proofed, 40f, log);
                if (dough.DebrisColor.a < 0.01f) { dough.DebrisColor = new Color(0.97f, 0.92f, 0.8f, 1f); EditorUtility.SetDirty(dough); }
            }
            foreach (ProductDef b in new[] { bag, loaf, bun })
                if (b != null && b.DebrisColor.a < 0.01f) { b.DebrisColor = new Color(0.9f, 0.75f, 0.5f, 1f); EditorUtility.SetDirty(b); }

            EnsureDir(RecipeDir);
            if (water != null && flour != null && yeast != null && salt != null && dough != null)
            {
                string path = $"{RecipeDir}/Recipe_{Dough}.asset";
                CookRecipeDef r = AssetDatabase.LoadAssetAtPath<CookRecipeDef>(path);
                bool isNew = r == null;
                if (isNew) r = ScriptableObject.CreateInstance<CookRecipeDef>();
                CookRecipeBook.YeastDough(r, water, flour, yeast, salt, dough);
                if (isNew) AssetDatabase.CreateAsset(r, path); else EditorUtility.SetDirty(r);
                log.AppendLine($"✔ рецепт «{Dough}»: вода 1 ведро + мука 8 мастерков + дрожжи 1 доза + соль 1 мастерок, мешать 10 с");
            }

            // материалы
            EnsureDir(ModelDir);
            // цвет — в материале, текстура — светлая «фактура» (генерируется, если нет своей)
            MakeMat("Mat_Dough", "Tex_Dough.png", new Color(0.97f, 0.92f, 0.80f), 0.35f, log, 0);
            MakeMat("Mat_DoughProofed", "Tex_DoughBubbles.png", new Color(0.96f, 0.90f, 0.76f), 0.3f, log, 1);
            MakeMat("Mat_BaguetteCrust", "Tex_BaguetteCrust.png", new Color(0.80f, 0.50f, 0.24f), 0.25f, log, 2);
            MakeMat("Mat_LoafCrust", "Tex_LoafCrust.png", new Color(0.76f, 0.47f, 0.22f), 0.25f, log, 3);

            AssetDatabase.SaveAssets();
            CookRecipeBook.Reload();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null)
            {
                db.CollectFromProject();
                EditorUtility.SetDirty(db);
                EditorSceneManager.MarkSceneDirty(db.gameObject.scene);
                log.AppendLine("✔ FoodDatabase в сцене пересобран (сохрани сцену)");
            }
            else log.AppendLine("⚠ в открытой сцене нет FoodDatabase — нажми у него «Collect From Project»");
            Debug.Log("[Хлеб]\n" + log);
            EditorUtility.DisplayDialog("Хлеб", log.ToString(), "Ок");
        }

        [MenuItem("CatCooks/Хлеб/2. Добавить тесто в выделенную тару (бетономешалка)", priority = 81)]
        public static void AddDoughLook()
        {
            var log = new StringBuilder();
            ProductDef dough = Find(Dough);
            if (dough == null) { EditorUtility.DisplayDialog("Хлеб", "Сначала «1. Создать продукты…».", "Ок"); return; }
            int n = 0;
            foreach (GameObject go in Selection.gameObjects)
                foreach (CookwareLiquidVisual lv in go.GetComponentsInChildren<CookwareLiquidVisual>(true))
                {
                    var so = new SerializedObject(lv);
                    SerializedProperty arr = so.FindProperty("_products");
                    bool has = false;
                    for (int i = 0; i < arr.arraySize; i++)
                        if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("Product").objectReferenceValue == dough) has = true;
                    if (!has)
                    {
                        arr.arraySize++;
                        SerializedProperty e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
                        e.FindPropertyRelative("Product").objectReferenceValue = dough;
                        e.FindPropertyRelative("Role").intValue = (int)CookwareLiquidVisual.Role.Paste;
                        e.FindPropertyRelative("Color").colorValue = new Color(0, 0, 0, 0);
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(lv);
                    }
                    n++;
                    log.AppendLine($"{(has ? "✔" : "＋")} {lv.transform.root.name}/{lv.name}: «{Dough}» — густая масса");
                }
            if (n == 0) log.AppendLine("Выдели бетономешалку (с CookwareLiquidVisual внутри бочки). Нет LiquidAnchor — тесто просто не видно в бочке, на работу не влияет.");
            Debug.Log("[Хлеб]\n" + log);
            EditorUtility.DisplayDialog("Хлеб", log.ToString(), "Ок");
        }

        [MenuItem("CatCooks/Хлеб/4. Настроить выделенные", priority = 83)]
        public static void SetupSelected()
        {
            var log = new StringBuilder();
            ProductDef dough = Find(Dough), bag = Find(Baguette), loaf = Find(Loaf), bun = Find(Bun), yeast = Find(Yeast), flour = Find(Flour);
            Material mDough = Mat("Mat_Dough"), mProof = Mat("Mat_DoughProofed"), mBag = Mat("Mat_BaguetteCrust"), mLoaf = Mat("Mat_LoafCrust");
            if (dough == null || mDough == null) { EditorUtility.DisplayDialog("Хлеб", "Сначала «1. Создать продукты, рецепт и материалы».", "Ок"); return; }
            foreach (GameObject go in Selection.gameObjects)
            {
                bool any = false;
                string lname = go.name.ToLowerInvariant();
                bool isBag = lname.Contains("bag") || lname.Contains("багет");
                bool isLoaf = lname.Contains("loaf") || lname.Contains("bread_l") || lname.Contains("бух") || lname.Contains("brick");

                DoughBlob db = go.GetComponent<DoughBlob>();
                if (db != null)
                {
                    var so = new SerializedObject(db);
                    so.FindProperty("_material").objectReferenceValue = mDough;
                    so.FindProperty("_proofedMaterial").objectReferenceValue = mProof;
                    SerializedProperty bp = so.FindProperty("_bakedProduct");
                    if (bp != null && bp.objectReferenceValue == null && bun != null) bp.objectReferenceValue = bun;
                    SerializedProperty cm = so.FindProperty("_crustMaterial");
                    if (cm != null && cm.objectReferenceValue == null && mLoaf != null) cm.objectReferenceValue = mLoaf;
                    so.ApplyModifiedProperties();
                    SetPiece(go, dough, FoodState.Kneaded);
                    if (go.GetComponent<SphereCollider>() == null) log.AppendLine($"⚠ «{go.name}»: на корне нужен Sphere Collider (добавь руками)");
                    log.AppendLine($"✔ «{go.name}»: тесто — материалы, продукт «{Dough}», на решётке печётся в «{(bun != null ? Bun : "⚠ нет «Булка» — нажми меню 1")}» (корочка Mat_LoafCrust)"); any = true;
                }
                BreadMold mold = go.GetComponent<BreadMold>();
                if (mold != null)
                {
                    var so = new SerializedObject(mold);
                    so.FindProperty("_doughMaterial").objectReferenceValue = mDough;
                    ProductDef p = isBag ? bag : isLoaf ? loaf : null;
                    Material crust = isBag ? mBag : isLoaf ? mLoaf : null;
                    if (p != null) so.FindProperty("_breadProduct").objectReferenceValue = p;
                    so.ApplyModifiedProperties();
                    Transform fill = so.FindProperty("_fill").objectReferenceValue as Transform;
                    if (fill == null) fill = go.transform.Find("Fill");
                    if (fill != null && crust != null)
                        foreach (Renderer r in fill.GetComponentsInChildren<Renderer>(true)) { Undo.RecordObject(r, "crust"); r.sharedMaterial = crust; EditorUtility.SetDirty(r); }
                    log.AppendLine($"✔ «{go.name}»: форма — {(p != null ? p.Title : "⚠ не понял, багет или буханка (в имени Baguette / Loaf) — задай Bread Product руками")}" +
                                   (fill == null ? "  ⚠ нет дочернего Fill (меш хлеба внутри)" : "") +
                                   (so.FindProperty("_breadPrefab").objectReferenceValue == null ? "  ⚠ Bread Prefab задай руками" : ""));
                    any = true;
                }
                FoodPiece fp = go.GetComponent<FoodPiece>();
                if (fp != null && db == null && (isBag || isLoaf))
                {
                    SetPiece(go, isBag ? bag : loaf, FoodState.Baked);
                    Material crust = isBag ? mBag : mLoaf;
                    foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) { Undo.RecordObject(r, "crust"); r.sharedMaterial = crust; EditorUtility.SetDirty(r); }
                    if (go.GetComponent<FoodHotSteam>() == null) { Undo.AddComponent<FoodHotSteam>(go); log.AppendLine($"＋ «{go.name}»: Food Hot Steam (пар от свежего хлеба)"); }
                    log.AppendLine($"✔ «{go.name}»: хлеб «{(isBag ? Baguette : Loaf)}», материал корочки"); any = true;
                }
                MixerDough md = go.GetComponent<MixerDough>();
                if (md != null)
                {
                    var so = new SerializedObject(md);
                    so.FindProperty("_dough").objectReferenceValue = dough;
                    so.ApplyModifiedProperties();
                    log.AppendLine($"✔ «{go.name}»: мешалка — тесто «{Dough}»" + (so.FindProperty("_ballPrefab").objectReferenceValue == null ? "  ⚠ Ball Prefab задай руками" : "")); any = true;
                }
                PowderShakerItem ps = go.GetComponent<PowderShakerItem>();
                if (ps != null && yeast != null && (lname.Contains("yeast") || lname.Contains("дрож")))
                {
                    var so = new SerializedObject(ps);
                    so.FindProperty("_product").objectReferenceValue = yeast;
                    so.ApplyModifiedProperties();
                    log.AppendLine($"✔ «{go.name}»: баночка — «{Yeast}»"); any = true;
                }
                foreach (MonoBehaviour mb in go.GetComponents<MonoBehaviour>())
                {
                    if (mb == null || mb.GetType().Name != "BulkSack" || flour == null) continue;
                    if (!(lname.Contains("flour") || lname.Contains("мук"))) continue;
                    var so = new SerializedObject(mb);
                    SerializedProperty pp = so.FindProperty("_product");
                    if (pp != null) { pp.objectReferenceValue = flour; so.ApplyModifiedProperties(); log.AppendLine($"✔ «{go.name}»: мешок — «{Flour}»"); any = true; }
                }
                if (!any) log.AppendLine($"— «{go.name}»: нечего настраивать (нет DoughBlob / BreadMold / хлеба / MixerDough / баночки / мешка)");
                EditorUtility.SetDirty(go);
            }
            log.AppendLine("\nСохрани префабы (Overrides → Apply All).");
            Debug.Log("[Хлеб]\n" + log);
            EditorUtility.DisplayDialog("Хлеб", log.ToString(), "Ок");
        }

        [MenuItem("CatCooks/Хлеб/3. Проверить хлеб", priority = 82)]
        public static void Check()
        {
            var sb = new StringBuilder();
            _sb = sb; _err = 0; _warn = 0;
            sb.AppendLine("■ Продукты");
            foreach (string t in new[] { Flour, Yeast, Salt, Water, Dough, Baguette, Loaf })
            {
                ProductDef p = Find(t);
                if (p == null) Er($"нет «{t}» → меню 1"); else Ok($"«{t}» Id {p.Id}");
            }
            ProductDef dough = Find(Dough);
            if (dough != null)
            {
                bool rule = false;
                foreach (FoodRule r in dough.Overrides) if (r.Process == FoodProcess.Rest && r.To == FoodState.Proofed) rule = true;
                if (!rule) Wr("у теста нет правила «Дать подойти → Подошло» (подходит и без него, но в F4 не видно «подошло»)");
            }
            sb.AppendLine("\n■ Рецепт");
            bool found = false;
            foreach (string g in AssetDatabase.FindAssets("t:CookRecipeDef"))
            {
                var r = AssetDatabase.LoadAssetAtPath<CookRecipeDef>(AssetDatabase.GUIDToAssetPath(g));
                if (r != null && r.Title == Dough) found = true;
            }
            if (found) Ok($"рецепт «{Dough}»"); else Wr($"нет ассета «{Dough}» — книга соберёт сама по названиям");
            foreach (string m in new[] { "Mat_Dough", "Mat_DoughProofed", "Mat_BaguetteCrust", "Mat_LoafCrust" })
                if (Mat(m) == null) Wr($"нет материала {m} → меню 1 (нужны текстуры в {ModelDir})");

            sb.AppendLine("\n■ Сцена (перетащи префабы в сцену, чтобы проверить их)");
            foreach (MixerDough md in Object.FindObjectsByType<MixerDough>(FindObjectsSortMode.None))
            {
                var so = new SerializedObject(md);
                if (md.GetComponent<ConcreteMixer>() == null) Er($"«{md.name}»: Mixer Dough не на корне бетономешалки (нет ConcreteMixer рядом)");
                Cookware cw = md.GetComponent<Cookware>();
                if (cw == null || cw.Def == null) Er($"«{md.name}»: нет Cookware / Cookware Def");
                if (cw != null && cw.InsideVolume == null) Er($"«{md.name}»: у Cookware нет Inside Volume (триггер внутри бочки)");
                if (so.FindProperty("_ballPrefab").objectReferenceValue == null) Er($"«{md.name}»: не задан Ball Prefab (шар теста)");
                Transform mouth = so.FindProperty("_mouth").objectReferenceValue as Transform;
                if (mouth == null) foreach (Transform t in md.GetComponentsInChildren<Transform>(true)) if (t.name == "Mouth") mouth = t;
                if (mouth == null) Er($"«{md.name}»: нет Mouth (горловина, синяя ось наружу)");
                else Ok($"«{md.name}»: бетономешалка-тесто, горловина «{mouth.name}»");
            }
            foreach (DoughBlob d in Object.FindObjectsByType<DoughBlob>(FindObjectsSortMode.None))
            {
                var so = new SerializedObject(d);
                if (so.FindProperty("_material").objectReferenceValue == null) Wr($"«{d.name}»: нет материала теста → меню 4");
                if (d.GetComponent<SphereCollider>() == null) Er($"«{d.name}»: нужен Sphere Collider на корне");
                if (d.GetComponent<FishNet.Object.NetworkObject>() == null) Er($"«{d.name}»: нет Network Object");
                else Ok($"«{d.name}»: шар теста");
            }
            foreach (DoughSpatulaItem s in Object.FindObjectsByType<DoughSpatulaItem>(FindObjectsSortMode.None))
            {
                var so = new SerializedObject(s);
                if (so.FindProperty("_piecePrefab").objectReferenceValue == null) Er($"«{s.name}»: шпатель без Piece Prefab (префаб теста)");
                else Ok($"«{s.name}»: шпатель");
            }
            foreach (BreadMold m in Object.FindObjectsByType<BreadMold>(FindObjectsSortMode.None))
            {
                var so = new SerializedObject(m);
                if (so.FindProperty("_breadProduct").objectReferenceValue == null) Er($"«{m.name}»: не задан Bread Product");
                if (so.FindProperty("_breadPrefab").objectReferenceValue == null) Er($"«{m.name}»: не задан Bread Prefab");
                Transform fill = so.FindProperty("_fill").objectReferenceValue as Transform ?? m.transform.Find("Fill");
                if (fill == null) Er($"«{m.name}»: нет дочернего Fill (меш хлеба внутри формы)");
                bool trig = false, solid = false;
                foreach (Collider c in m.GetComponentsInChildren<Collider>(true)) { if (c.isTrigger) trig = true; else solid = true; }
                if (!trig) Er($"«{m.name}»: нет триггер-коллайдера внутри формы (ловит кусок теста)");
                if (!solid) Er($"«{m.name}»: нет обычного коллайдера (форма провалится)");
                if (trig && solid && fill != null) Ok($"«{m.name}»: форма");
            }
            foreach (BakeOven o in Object.FindObjectsByType<BakeOven>(FindObjectsSortMode.None))
            {
                var so = new SerializedObject(o);
                Collider ch = so.FindProperty("_chamber").objectReferenceValue as Collider;
                if (ch == null) Er($"«{o.name}»: не задан Chamber (полость) — внутри ничего не печётся");
                else if (!ch.isTrigger) Er($"«{o.name}»: Chamber должен быть Is Trigger");
                foreach (Component c in o.GetComponents<Component>())
                    if (c != null && (c is Cookware || c is CookwareHeat || c is CookwarePressure || c is CarriedContainer || c is PotLidSocket))
                        Wr($"«{o.name}»: на духовке {c.GetType().Name} — она больше не тара, убери (меню «Духовка: подключить компоненты» уберёт само)");
                OvenHingedLid hl = o.GetComponentInChildren<OvenHingedLid>(true);
                if (hl == null) Wr($"«{o.name}»: нет Oven Hinged Lid — крышка не откидывается");
                else if (new SerializedObject(hl).FindProperty("_hinge").objectReferenceValue == null) Er($"«{o.name}»: Oven Hinged Lid → Hinge не задан (LidHinge)");
                if (o.GetComponentInChildren<OvenLidPart>(true) == null) Wr($"«{o.name}»: нет Oven Lid Part на меше крышки — F по крышке не сработает (клавиша L работает)");
                OvenVentHatch vh = o.GetComponentInChildren<OvenVentHatch>(true);
                if (vh == null) Wr($"«{o.name}»: нет Oven Vent Hatch (люк) — пар уходит только при открытой крышке");
                bool grate = false;
                if (ch != null)
                    foreach (Collider c in o.GetComponentsInChildren<Collider>(true))
                        if (!c.isTrigger && c != ch && ch.bounds.Contains(c.bounds.center)) { grate = true; break; }
                if (!grate) Wr($"«{o.name}»: внутри Chamber нет обычного коллайдера — решётки/дна нет, всё провалится");
                Ok($"«{o.name}»: духовка");
            }
            foreach (PowderShakerItem ps in Object.FindObjectsByType<PowderShakerItem>(FindObjectsSortMode.None))
            {
                var p = new SerializedObject(ps).FindProperty("_product").objectReferenceValue as ProductDef;
                if (p != null && p.Title == Yeast) Ok($"«{ps.name}»: баночка дрожжей");
            }
            int err = _err, warn = _warn;
            string head = err > 0 ? $"ОШИБОК {err}, предупреждений {warn}" : warn > 0 ? $"ошибок нет, предупреждений {warn}" : "всё в порядке";
            Debug.Log("[Хлеб — проверка] " + head + "\n" + sb);
            EditorUtility.DisplayDialog("Хлеб — проверка", head + "\n" + sb, "Ок");
        }

        // ───────────────────────── помощники ─────────────────────────

        private static StringBuilder _sb;
        private static int _err, _warn;
        private static void Ok(string t) => _sb.AppendLine("  ✔ " + t);
        private static void Wr(string t) { _warn++; _sb.AppendLine("  ⚠ " + t); }
        private static void Er(string t) { _err++; _sb.AppendLine("  ✖ " + t); }

        private static void SetPiece(GameObject go, ProductDef p, FoodState st)
        {
            FoodPiece fp = go.GetComponent<FoodPiece>();
            if (fp == null || p == null) return;
            var so = new SerializedObject(fp);
            so.FindProperty("_product").objectReferenceValue = p;
            so.FindProperty("_startState").intValue = (int)st;
            so.ApplyModifiedProperties();
        }

        private static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{ModelDir}/{name}.mat");

        private static void MakeMat(string name, string tex, Color color, float smooth, StringBuilder log, int kind)
        {
            string path = $"{ModelDir}/{name}.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelDir}/{tex}");
            if (t == null) t = MakeTexture(tex, kind, log);
            if (m == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                m = new Material(sh) { name = name };
                AssetDatabase.CreateAsset(m, path);
                log.AppendLine($"＋ материал {path}");
            }
            string texProp = m.HasProperty("_BaseMap") ? "_BaseMap" : m.HasProperty("_MainTex") ? "_MainTex" : null;
            Texture cur = texProp != null ? m.GetTexture(texProp) : null;
            if (cur != null && cur != t)
            {
                log.AppendLine($"✔ материал {name}: своя текстура «{cur.name}» — не трогаю (цвет тоже)");
                return;
            }
            if (t != null && texProp != null)
            {
                m.SetTexture(texProp, t);
                if (texProp == "_BaseMap" && m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            EditorUtility.SetDirty(m);
            log.AppendLine($"✔ материал {name}: цвет {ColorUtility.ToHtmlStringRGB(color)}, фактура {tex}");
        }

        /// <summary>
        /// Светлая бесшовная «фактура» 256×256 (цвет даёт материал):
        /// 0 — тесто, 1 — тесто с пузырьками, 2 — корочка багета (косые надрезы), 3 — корочка буханки (пятна).
        /// </summary>
        private static Texture2D MakeTexture(string file, int kind, StringBuilder log)
        {
            const int N = 256;
            var rnd = new System.Random(file.Length * 7919 + kind * 104729);
            float ox = rnd.Next(1, 900), oy = rnd.Next(1, 900);
            var px = new Color32[N * N];
            // пузырьки / пятна — случайные кружки (с переносом через край — бесшовно)
            int spots = kind == 1 ? 70 : kind == 3 ? 40 : kind == 0 ? 120 : 0;
            var sx = new float[spots]; var sy = new float[spots]; var sr = new float[spots];
            for (int i = 0; i < spots; i++)
            {
                sx[i] = (float)rnd.NextDouble() * N; sy[i] = (float)rnd.NextDouble() * N;
                sr[i] = kind == 1 ? 2f + (float)rnd.NextDouble() * 6f : kind == 3 ? 8f + (float)rnd.NextDouble() * 18f : 0.6f + (float)rnd.NextDouble() * 1.2f;
            }
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float n = Tile(x, y, N, ox, oy, 4f) * 0.6f + Tile(x, y, N, ox * 2f, oy * 2f, 12f) * 0.4f;   // 0..1
                    float v;
                    switch (kind)
                    {
                        case 0: v = 0.92f + 0.08f * n; break;
                        case 1: v = 0.9f + 0.08f * n; break;
                        case 2:
                            float score = Mathf.Abs(Mathf.Sin((x + y * 0.55f) / N * Mathf.PI * 6f));
                            v = 0.74f + 0.16f * n + 0.12f * Mathf.SmoothStep(0.85f, 1f, score);   // светлые косые надрезы
                            break;
                        default: v = 0.72f + 0.26f * n; break;
                    }
                    for (int i = 0; i < spots; i++)
                    {
                        float dx = Mathf.Abs(x - sx[i]); dx = Mathf.Min(dx, N - dx);
                        float dy = Mathf.Abs(y - sy[i]); dy = Mathf.Min(dy, N - dy);
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / sr[i];
                        if (d >= 1.2f) continue;
                        if (kind == 1) v *= d < 0.8f ? 0.93f + 0.05f * d : 0.9f;          // пузырёк: тёмный ободок
                        else if (kind == 3) v *= Mathf.Lerp(0.88f, 1f, Mathf.Clamp01(d));  // румяное пятно
                        else v *= d < 1f ? 0.94f : 1f;                                      // мучная крапинка
                    }
                    v = Mathf.Clamp01(v);
                    px[y * N + x] = new Color(v, v * 0.985f, v * 0.96f);
                }
            var tex = new Texture2D(N, N, TextureFormat.RGB24, true);
            tex.SetPixels32(px);
            tex.Apply();
            string path = $"{ModelDir}/{file}";
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            log.AppendLine($"＋ текстура {path} (сгенерирована)");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Бесшовный шум Перлина: смесь четырёх сдвинутых выборок.</summary>
        private static float Tile(int x, int y, int n, float ox, float oy, float freq)
        {
            float u = x / (float)n, v = y / (float)n;
            return Pn(x, y, n, ox, oy, freq) * (1 - u) * (1 - v) + Pn(x - n, y, n, ox, oy, freq) * u * (1 - v)
                 + Pn(x, y - n, n, ox, oy, freq) * (1 - u) * v + Pn(x - n, y - n, n, ox, oy, freq) * u * v;
        }

        private static float Pn(float a, float b, int n, float ox, float oy, float freq)
            => Mathf.PerlinNoise(ox + a / n * freq, oy + b / n * freq);

        private static ProductDef Ensure(string title, FoodGroupDef group, Color color, float size, bool sub, string dir,
                                         ref ushort next, StringBuilder log)
        {
            ProductDef p = Find(title);
            if (p != null) { log.AppendLine($"✔ «{title}» уже есть (Id {p.Id})"); return p; }
            if (group == null) { log.AppendLine($"✖ «{title}»: не нашёл группу"); return null; }
            p = ScriptableObject.CreateInstance<ProductDef>();
            p.Id = next++;
            p.Title = title; p.Group = group; p.BaseColor = color; p.Size = size; p.MadeByRecipe = sub;
            string file = "Food_" + title.Replace(" ", "_").Replace("(", "").Replace(")", "") + ".asset";
            AssetDatabase.CreateAsset(p, $"{dir}/{file}");
            log.AppendLine($"＋ «{title}» (Id {p.Id})");
            return p;
        }

        private static void Rule(ProductDef p, FoodState from, FoodProcess proc, FoodState to, float sec, StringBuilder log)
        {
            for (int i = 0; i < p.Overrides.Count; i++)
            {
                FoodRule r = p.Overrides[i];
                if (r.From != from || r.Process != proc) continue;
                if (r.To == to) return;
                r.To = to; r.Seconds = sec; p.Overrides[i] = r; EditorUtility.SetDirty(p);
                log.AppendLine($"↻ правило «{p.Title}»: {FoodStateInfo.NameOf(from)} → {FoodStateInfo.NameOf(proc)} → {FoodStateInfo.NameOf(to)}");
                return;
            }
            p.Overrides.Add(new FoodRule { From = from, Process = proc, To = to, Seconds = sec });
            EditorUtility.SetDirty(p);
            log.AppendLine($"＋ правило «{p.Title}»: {FoodStateInfo.NameOf(from)} → {FoodStateInfo.NameOf(proc)} → {FoodStateInfo.NameOf(to)}");
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

        private static ushort MaxId()
        {
            ushort m = 0;
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef d = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (d != null && d.Id > m) m = d.Id;
            }
            return m;
        }

        private static string ProductsDir()
        {
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                ProductDef d = AssetDatabase.LoadAssetAtPath<ProductDef>(p);
                if (d != null && (d.Title == "Вода" || d.Title == "Сахар")) return System.IO.Path.GetDirectoryName(p).Replace('\\', '/');
            }
            const string dflt = "Assets/CatCooks/Food/Products";
            EnsureDir(dflt);
            return dflt;
        }

        private static FoodGroupDef FindGroup(string name)
        {
            foreach (string g in AssetDatabase.FindAssets("t:FoodGroupDef " + name))
            {
                FoodGroupDef d = AssetDatabase.LoadAssetAtPath<FoodGroupDef>(AssetDatabase.GUIDToAssetPath(g));
                if (d != null && d.name == name) return d;
            }
            return null;
        }

        private static FoodGroupDef GroupOf(string productTitle)
        {
            ProductDef p = Find(productTitle);
            return p != null ? p.Group : null;
        }

        private static void EnsureDir(string dir)
        {
            string[] parts = dir.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string nx = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(nx)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = nx;
            }
        }
    }
}
#endif
