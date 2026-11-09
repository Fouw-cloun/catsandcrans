#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using DS = CatCooks.Food.CatCooksDessertSetup;
using M = CatCooks.Food.CookRecipeBook.Macaron;

namespace CatCooks.Food
{
    /// <summary>
    /// CatCooks → Макарон («Солёная каска»: шоколадные скорлупки с солёной карамелью):
    ///   1. Создать продукты и рецепты — мука миндальная, миндальная смесь, тесто для макарон (+ перемешанное),
    ///      карамель (+ горелая), солёная карамель, карамельный ганаш, скорлупка, макарон.
    ///   2. Массы в выделенную тару — как выглядят тесто, карамель и ганаш в вёдрах и кастрюле.
    ///   3. Грохот в сцену — строительное сито (рама, сетка, ручки) + компоненты.
    ///   4. Шприц в сцену — мешок для затирки швов со стальным носиком + компоненты.
    ///   5. Противень с кольцами в сцену — лист с обрубками трубы под решётку духовки + компоненты.
    ///   6. Скорлупка макарон — объект для префаба (сохрани руками, в Spawnable Prefabs, в Shell Prefab противня).
    ///   7. Проверить макарон — ✔/⚠/✖.
    /// Префабы не создаются. Модели — в Assets/Models/Dessert.
    /// </summary>
    public static class CatCooksMacaronSetup
    {
        // ───────────────────────── 1. ПРОДУКТЫ И РЕЦЕПТЫ ─────────────────────────

        [MenuItem("CatCooks/Макарон/1. Создать продукты и рецепты", priority = 120)]
        public static void CreateAll()
        {
            var log = new StringBuilder();
            string dir = DS.ProductsDir();
            ushort next = (ushort)(DS.MaxId() + 1);
            FoodGroupDef sub = DS.FindGroup("Group_SubRecipe") ?? DS.GroupOf("Бульон") ?? DS.GroupOf("Шоколадная масса") ?? DS.GroupOf("Меренга");
            FoodGroupDef bulk = DS.GroupOf("Мука пшеничная") ?? DS.GroupOf("Сахар") ?? sub;

            ProductDef mer = DS.Find("Меренга"), cocoa = DS.Find(M.Cocoa), sugar = DS.Find("Сахар");
            ProductDef butter = DS.Find("Сливочное масло"), salt = DS.Find(M.Salt), mass = DS.Find("Шоколадная масса");
            if (mer == null) log.AppendLine("✖ нет «Меренга» — CatCooks → Десерты → 1");
            if (mass == null) log.AppendLine("✖ нет «Шоколадная масса» — CatCooks → Десерты → 1");
            if (cocoa == null) log.AppendLine("✖ нет «Тёртое какао» — CatCooks → Шоколад → 1 (какао-бобы → обжарить → отбойник)");
            if (sugar == null) log.AppendLine("✖ нет «Сахар»");
            if (butter == null) log.AppendLine("✖ нет «Сливочное масло»");
            if (salt == null) log.AppendLine("✖ нет «Соль» — CatCooks → Хлеб → 1");

            ProductDef almond = DS.Ensure(M.AlmondFlour, bulk, new Color(0.93f, 0.86f, 0.72f), 0.02f, false, dir, ref next, log);
            ProductDef mix = DS.Ensure(M.AlmondMix, sub, new Color(0.95f, 0.91f, 0.82f), 0.02f, true, dir, ref next, log);
            ProductDef batter = DS.Ensure(M.Batter, sub, new Color(0.80f, 0.62f, 0.46f), 0.06f, true, dir, ref next, log);
            ProductDef over = DS.Ensure(M.BatterOver, sub, new Color(0.74f, 0.57f, 0.43f), 0.06f, true, dir, ref next, log);
            ProductDef caramel = DS.Ensure(M.Caramel, sub, new Color(0.80f, 0.48f, 0.16f), 0.06f, true, dir, ref next, log);
            ProductDef burnt = DS.Ensure(M.CaramelBurnt, sub, new Color(0.24f, 0.13f, 0.07f), 0.06f, true, dir, ref next, log);
            ProductDef salted = DS.Ensure(M.SaltCaramel, sub, new Color(0.84f, 0.56f, 0.26f), 0.06f, true, dir, ref next, log);
            ProductDef ganache = DS.Ensure(M.Ganache, sub, new Color(0.40f, 0.23f, 0.12f), 0.06f, true, dir, ref next, log);
            ProductDef shell = DS.Ensure(M.Shell, sub, new Color(0.78f, 0.58f, 0.40f), 0.08f, true, dir, ref next, log);
            ProductDef dish = DS.Ensure(M.Dish, sub, new Color(0.78f, 0.58f, 0.40f), 0.1f, true, dir, ref next, log);
            foreach (ProductDef p in new[] { almond, mix, batter, over, caramel, burnt, salted, ganache, shell, dish })
                if (p != null && p.DebrisColor.a < 0.01f) { p.DebrisColor = new Color(p.BaseColor.r, p.BaseColor.g, p.BaseColor.b, 1f); EditorUtility.SetDirty(p); }

            DS.EnsureDir(DS.RecipeDir);
            ProductDef paste = DS.Find("Шоколадная паста");
            if (mer != null && mix != null && (cocoa != null || paste != null) && batter != null)
                DS.SaveRecipe("Тесто для макарон", r => CookRecipeBook.MacaronBatter(r, mer, mix, cocoa, batter, paste), log);
            if (batter != null && over != null) DS.SaveRecipe("Тесто для макарон перемешанное", r => CookRecipeBook.MacaronBatterOver(r, batter, over), log);
            if (sugar != null && caramel != null) DS.SaveRecipe("Карамель", r => CookRecipeBook.Caramel(r, sugar, caramel), log);
            if (caramel != null && burnt != null) DS.SaveRecipe("Карамель горелая", r => CookRecipeBook.CaramelBurnt(r, caramel, burnt), log);
            if (caramel != null && butter != null && salt != null && salted != null) DS.SaveRecipe("Солёная карамель", r => CookRecipeBook.SaltedCaramel(r, caramel, butter, salt, salted), log);
            if (mass != null && salted != null && ganache != null) DS.SaveRecipe("Карамельный ганаш", r => CookRecipeBook.CaramelGanache(r, mass, salted, ganache), log);

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
            log.AppendLine("\nМука миндальная — нужен мешок (Bulk Sack) с этим продуктом, или тест: шестерёнка у грохота → «Тест: насыпать».");
            log.AppendLine("Дальше: 2 (массы в вёдра/кастрюлю), 3–6 (грохот, шприц, противень, скорлупка), 7 — проверка.");
            DS.Done("Макарон", log);
        }

        // ───────────────────────── 2. МАССЫ В ТАРУ ─────────────────────────

        [MenuItem("CatCooks/Макарон/2. Массы в выделенную тару (вёдра, кастрюля)", priority = 121)]
        public static void AddMasses()
        {
            var log = new StringBuilder();
            var targets = new List<CookwareLiquidVisual>();
            foreach (GameObject go in Selection.gameObjects)
                foreach (CookwareLiquidVisual lv in go.GetComponentsInChildren<CookwareLiquidVisual>(true))
                    if (!targets.Contains(lv)) targets.Add(lv);
            if (targets.Count == 0) { EditorUtility.DisplayDialog("Макарон", "Выдели вёдра и кастрюлю (с LiquidAnchor) и нажми ещё раз. Банки без LiquidAnchor — сначала «Десерты → 2б».", "Ок"); return; }
            Color own = new Color(0, 0, 0, 0);
            var rows = new[]
            {
                new DS.RoleRow(M.AlmondMix, CookwareLiquidVisual.Role.Paste, own, "сыпучая смесь"),
                new DS.RoleRow(M.Batter, CookwareLiquidVisual.Role.Thick, own, "густое блестящее тесто"),
                new DS.RoleRow(M.BatterOver, CookwareLiquidVisual.Role.Thick, own, "жидковатое тесто"),
                new DS.RoleRow(M.Caramel, CookwareLiquidVisual.Role.Thick, own, "янтарная карамель"),
                new DS.RoleRow(M.CaramelBurnt, CookwareLiquidVisual.Role.Thick, own, "горелая карамель"),
                new DS.RoleRow(M.SaltCaramel, CookwareLiquidVisual.Role.Thick, own, "тягучая карамель"),
                new DS.RoleRow(M.Ganache, CookwareLiquidVisual.Role.Paste, own, "густой ганаш"),
                new DS.RoleRow("Сахар", CookwareLiquidVisual.Role.Paste, own, "сахар (плавится в карамель)"),
            };
            foreach (CookwareLiquidVisual lv in targets)
            {
                var so = new SerializedObject(lv);
                SerializedProperty arr = so.FindProperty("_products");
                log.AppendLine($"— {lv.transform.root.name}/{lv.name}");
                foreach (DS.RoleRow e in rows)
                {
                    ProductDef p = DS.Find(e.Title);
                    if (p == null) { log.AppendLine($"  ⚠ нет «{e.Title}» — сначала меню 1"); continue; }
                    DS.AddEntry(arr, p, (int)e.Role, e.Color, e.What, log);
                }
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(lv);
            }
            DS.Done("Макарон", log);
        }

        // ───────────────────────── размеры ─────────────────────────

        /// <summary>Противень под решётку духовки и радиус кольца.</summary>
        private static void TrayDims(out float L, out float W, out int nx, out int nz, out float rr, StringBuilder log)
        {
            L = 1.6f; W = 0.8f;
            BakeOven oven = Object.FindFirstObjectByType<BakeOven>();
            if (oven != null && oven.TryGetGrill(out Collider g))
            {
                Vector3 gs = g is BoxCollider gb ? Vector3.Scale(gb.size, g.transform.lossyScale) : g.bounds.size;
                float a = Mathf.Abs(gs.x), b = Mathf.Abs(gs.z);
                L = Mathf.Max(a, b) * 0.92f; W = Mathf.Min(a, b) * 0.88f;
                if (log != null) log.AppendLine($"Решётка «{oven.name}»: {Mathf.Max(a, b):0.00}×{Mathf.Min(a, b):0.00} м → противень {L:0.00}×{W:0.00} м");
            }
            else if (log != null) log.AppendLine($"Духовки с решёткой нет — противень {L:0.00}×{W:0.00} м");
            nz = W / L > 0.45f ? 3 : 2;
            float cell = W / nz;
            nx = Mathf.Clamp(Mathf.FloorToInt(L / cell), 2, 12);
            rr = cell * 0.5f * 0.7f;
        }

        private static float RingRadiusInScene()
        {
            MacaronTray t = Object.FindFirstObjectByType<MacaronTray>();
            if (t != null) return t.RingRadius * Mathf.Max(Mathf.Abs(t.transform.lossyScale.x), Mathf.Abs(t.transform.lossyScale.z));
            TrayDims(out _, out _, out _, out _, out float rr, null);
            return rr;
        }

        private static Vector3 At() => SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

        // ───────────────────────── 3. ГРОХОТ ─────────────────────────

        [MenuItem("CatCooks/Макарон/3. Грохот в сцену (строительное сито)", priority = 123)]
        public static void MakeSifter()
        {
            var log = new StringBuilder();
            float S;
            GameObject sel = Selection.activeGameObject;
            Cookware bucket = sel != null ? sel.GetComponentInParent<Cookware>() : null;
            if (bucket != null)
            {
                Bounds b = new Bounds(bucket.transform.position, Vector3.zero);
                foreach (Renderer r in bucket.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                S = Mathf.Max(b.size.x, b.size.z) * 1.3f;
                log.AppendLine($"Под ведро «{bucket.name}»: грохот {S:0.00} м");
            }
            else { S = RingRadiusInScene() * 5f; log.AppendLine($"Ведро не выделено — грохот {S:0.00} м (выдели ведро и нажми снова — подгоню под него)"); }

            float b0 = S * 0.08f, hF = S * 0.12f, w = S * 0.007f, meshY = hF * 0.3f;
            Color wood = new Color(0.80f, 0.60f, 0.37f, 0f), woodD = new Color(0.62f, 0.44f, 0.26f, 0f), steel = new Color(0.58f, 0.61f, 0.65f, 0f), yellow = new Color(0.99f, 0.76f, 0.12f, 0f);
            var vb = new VB();
            // рама
            vb.Box(new Vector3(0f, hF * 0.5f, S * 0.5f - b0 * 0.5f), new Vector3(S, hF, b0), wood);
            vb.Box(new Vector3(0f, hF * 0.5f, -S * 0.5f + b0 * 0.5f), new Vector3(S, hF, b0), wood);
            vb.Box(new Vector3(S * 0.5f - b0 * 0.5f, hF * 0.5f, 0f), new Vector3(b0, hF, S - 2f * b0), woodD);
            vb.Box(new Vector3(-S * 0.5f + b0 * 0.5f, hF * 0.5f, 0f), new Vector3(b0, hF, S - 2f * b0), woodD);
            // жёлтая полоса по верху рамы (стройка)
            vb.Box(new Vector3(0f, hF + w, S * 0.5f - b0 * 0.5f), new Vector3(S * 0.999f, w * 2f, b0 * 0.6f), yellow);
            vb.Box(new Vector3(0f, hF + w, -S * 0.5f + b0 * 0.5f), new Vector3(S * 0.999f, w * 2f, b0 * 0.6f), yellow);
            // сетка
            int n = 14;
            float inner = S - 2f * b0;
            for (int i = 1; i < n; i++)
            {
                float k = -inner * 0.5f + inner * i / n;
                vb.Box(new Vector3(0f, meshY, k), new Vector3(inner, w, w), steel);
                vb.Box(new Vector3(k, meshY + w * 0.6f, 0f), new Vector3(w, w, inner), steel);
            }
            // ручки
            for (int s = -1; s <= 1; s += 2)
            {
                vb.Box(new Vector3(s * (S * 0.5f + S * 0.08f), hF * 0.55f, 0f), new Vector3(S * 0.16f, b0 * 0.55f, b0 * 1.2f), woodD);
                vb.Box(new Vector3(s * (S * 0.5f + S * 0.15f), hF * 0.55f, 0f), new Vector3(b0 * 0.5f, b0 * 0.7f, S * 0.22f), wood);
            }
            Mesh mesh = DS.SaveMesh(vb.Build("Sifter_Mesh"), "Sifter_Mesh", log);

            var go = new GameObject("Sifter");
            Undo.RegisterCreatedObjectUndo(go, "Sifter");
            go.transform.position = At();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = DS.PlateMat(log);
            // коллайдеры рамы и сетки
            Transform cols = DS.FindOrCreate(go.transform, "SifterColliders");
            ColBox(cols, "FrameN", new Vector3(0f, hF * 0.5f, S * 0.5f - b0 * 0.5f), new Vector3(S, hF, b0));
            ColBox(cols, "FrameS", new Vector3(0f, hF * 0.5f, -S * 0.5f + b0 * 0.5f), new Vector3(S, hF, b0));
            ColBox(cols, "FrameE", new Vector3(S * 0.5f - b0 * 0.5f, hF * 0.5f, 0f), new Vector3(b0, hF, S - 2f * b0));
            ColBox(cols, "FrameW", new Vector3(-S * 0.5f + b0 * 0.5f, hF * 0.5f, 0f), new Vector3(b0, hF, S - 2f * b0));
            ColBox(cols, "MeshPlate", new Vector3(0f, meshY, 0f), new Vector3(inner, w * 2f, inner));
            Transform meshT = DS.FindOrCreate(go.transform, "Mesh");
            meshT.localPosition = new Vector3(0f, meshY - w, 0f);
            Transform inside = DS.FindOrCreate(go.transform, "Inside");
            inside.localPosition = new Vector3(0f, meshY + hF * 0.6f, 0f);
            BoxCollider ib = DS.Need<BoxCollider>(inside.gameObject);
            ib.isTrigger = true; ib.size = new Vector3(inner, hF * 1.2f, inner);
            Transform mouth = DS.FindOrCreate(go.transform, "Mouth");
            mouth.localPosition = new Vector3(0f, hF, 0f);
            // горка на сетке
            Transform pile = DS.FindOrCreate(go.transform, "Pile");
            pile.localPosition = new Vector3(0f, meshY + w, 0f);
            pile.localScale = new Vector3(inner * 0.4f, hF * 0.9f, inner * 0.4f);
            DS.Need<MeshFilter>(pile.gameObject).sharedMesh = PuckAsset(log);
            DS.Need<MeshRenderer>(pile.gameObject).sharedMaterial = MacaronMat(log);
            pile.gameObject.SetActive(false);

            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = 2f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            Cookware cw = go.AddComponent<Cookware>();
            var cso = new SerializedObject(cw);
            DS.SetObj(cso, "Def", SifterDef(log));
            SerializedProperty cr = cso.FindProperty("_cooksRecipes"); if (cr != null) cr.boolValue = false;
            DS.SetObj(cso, "_insideVolume", ib);
            DS.SetObj(cso, "_mouth", mouth);
            cso.ApplyModifiedProperties();
            go.AddComponent<SifterItem>();
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine("＋ «Sifter»: рама с жёлтой полосой, стальная сетка, ручки; Cookware «Грохот», Inside, Pile, Sifter Item, сеть, Rigidbody");
            log.AppendLine("\nДАЛЬШЕ: перетащи Sifter в Project → префаб (если нужен в Spawnable Prefabs) или оставь в сцене.\n" +
                           "Как пользоваться: мастерком (ПКМ) кидаешь в грохот муку миндальную и сахар → F взять → держи над ведром → ЛКМ и тряси мышью.");
            DS.Done("Грохот", log);
        }

        private static CookwareDef SifterDef(StringBuilder log)
        {
            string path = $"{DS.DefDir}/Cookware_Sifter.asset";
            CookwareDef d = AssetDatabase.LoadAssetAtPath<CookwareDef>(path);
            if (d != null) return d;
            DS.EnsureDir(DS.DefDir);
            d = ScriptableObject.CreateInstance<CookwareDef>();
            d.Title = "Грохот"; d.Kind = CookwareKind.Storage; d.Capacity = 16;
            d.HeatProcess = FoodProcess.None; d.NeedsHeatSource = true; d.Absorbs = false; d.BlendsColor = false;
            AssetDatabase.CreateAsset(d, path);
            log.AppendLine($"  ＋ тара {path}: «Грохот», 16 порций, не глотает");
            return d;
        }

        private static void ColBox(Transform h, string n, Vector3 c, Vector3 s)
        {
            Transform x = DS.FindOrCreate(h, n);
            x.localPosition = c; x.localRotation = Quaternion.identity; x.localScale = Vector3.one;
            DS.Need<BoxCollider>(x.gameObject).size = s;
        }

        // ───────────────────────── 4. ШПРИЦ ─────────────────────────

        [MenuItem("CatCooks/Макарон/4. Шприц в сцену (мешок для затирки)", priority = 124)]
        public static void MakeBag()
        {
            var log = new StringBuilder();
            float L = RingRadiusInScene() * 4f;
            Color canvas = new Color(0.80f, 0.74f, 0.62f, 0.3f), band = new Color(0.97f, 0.55f, 0.15f, 0f), steel = new Color(0.62f, 0.65f, 0.69f, 0f), tape = new Color(0.90f, 0.30f, 0.15f, 0f);
            // мешок: профиль от макушки наружу, вниз, к носику
            var prof = new List<Vector2>
            {
                new Vector2(0f, 1f), new Vector2(0.08f, 0.99f), new Vector2(0.17f, 0.94f), new Vector2(0.21f, 0.84f),
                new Vector2(0.21f, 0.72f), new Vector2(0.18f, 0.52f), new Vector2(0.13f, 0.34f), new Vector2(0.08f, 0.2f),
                new Vector2(0.055f, 0.15f), new Vector2(0.055f, 0.15f), new Vector2(0f, 0.15f),
            };
            for (int i = 0; i < prof.Count; i++) prof[i] *= L;
            Mesh bodyRaw = MacaronMesh.Lathe(null, prof, 36,
                (i, th, p) => p.y > L * 0.8f && p.y < L * 0.88f ? band : canvas,
                (i, th, p) =>
                {
                    if (i == 0 || i >= prof.Count - 3) return p;
                    float fold = 1f + 0.045f * Mathf.Sin(th * 7f + p.y * 3f / L);   // складки ткани
                    return new Vector3(p.x * fold, p.y, p.z * fold);
                }, "PipingBag_Body");
            Mesh body = DS.SaveMesh(bodyRaw, "PipingBag_Body", log);
            var nprof = new List<Vector2>
            {
                new Vector2(0f, 0.17f), new Vector2(0.062f, 0.17f), new Vector2(0.062f, 0.17f),
                new Vector2(0.062f, 0.13f), new Vector2(0.018f, 0.0f), new Vector2(0.018f, 0f), new Vector2(0f, 0f),
            };
            for (int i = 0; i < nprof.Count; i++) nprof[i] *= L;
            Mesh noz = DS.SaveMesh(MacaronMesh.Lathe(null, nprof, 24, (i, th, p) => steel, null, "PipingBag_Nozzle"), "PipingBag_Nozzle", log);
            Mesh tie = DS.SaveMesh(MacaronMesh.Tube(0.03f * L, 0.065f * L, 0.05f * L, tape, tape, 20), "PipingBag_Tie", log);
            Mesh stream = DS.SaveMesh(StreamMesh(), "PipingBag_Stream", log);
            Material mat = DS.PlateMat(log);

            var go = new GameObject("PipingBag");
            Undo.RegisterCreatedObjectUndo(go, "PipingBag");
            go.transform.position = At();
            Transform bodyT = Child(go.transform, "Body", body, mat, Vector3.zero);
            Child(bodyT, "Tie", tie, mat, new Vector3(0f, 0.96f * L, 0f));
            Child(go.transform, "NozzleMesh", noz, mat, Vector3.zero);
            Transform tip = DS.FindOrCreate(go.transform, "Nozzle");
            tip.localPosition = Vector3.zero;
            Transform st = Child(go.transform, "Stream", stream, MacaronMat(log), Vector3.zero);
            st.localScale = new Vector3(L * 0.018f, L * 0.018f, 1f);
            st.gameObject.SetActive(false);
            BoxCollider bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.44f * L, L, 0.44f * L); bc.center = new Vector3(0f, 0.5f * L, 0f);
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.8f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            PipingBagItem bag = go.AddComponent<PipingBagItem>();
            bag.EditorSetup(bodyT, tip, st);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"＋ «PipingBag» длиной {L:0.00} м: брезентовый мешок с оранжевой полосой, стальной носик, завязка; Body/Nozzle/Stream, Piping Bag Item, сеть");
            log.AppendLine("\nДержать удобно носиком вниз: подстрой Hold Position/Euler Offset у Piping Bag Item (как у других предметов).");
            DS.Done("Шприц", log);
        }

        private static Transform Child(Transform parent, string n, Mesh mesh, Material mat, Vector3 pos)
        {
            Transform t = DS.FindOrCreate(parent, n);
            t.localPosition = pos; t.localRotation = Quaternion.identity;
            DS.Need<MeshFilter>(t.gameObject).sharedMesh = mesh;
            DS.Need<MeshRenderer>(t.gameObject).sharedMaterial = mat;
            return t;
        }

        /// <summary>Струйка: цилиндр радиус 1, вдоль +Z от 0 до 1.</summary>
        private static Mesh StreamMesh()
        {
            var prof = new List<Vector2> { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 0f) };
            Mesh m = MacaronMesh.Lathe(null, prof, 10, (i, th, p) => Color.white, null, "PipingBag_Stream");
            // ось вращения Y → Z
            Vector3[] v = m.vertices; Vector3[] n = m.normals;
            for (int i = 0; i < v.Length; i++) { v[i] = new Vector3(v[i].x, -v[i].z, v[i].y); n[i] = new Vector3(n[i].x, -n[i].z, n[i].y); }
            m.vertices = v; m.normals = n; m.RecalculateBounds();
            return m;
        }

        // ───────────────────────── 5. ПРОТИВЕНЬ С КОЛЬЦАМИ ─────────────────────────

        [MenuItem("CatCooks/Макарон/5. Противень с кольцами в сцену (под решётку духовки)", priority = 125)]
        public static void MakeTray()
        {
            var log = new StringBuilder();
            TrayDims(out float L, out float W, out int nx, out int nz, out float rr, log);
            float th = Mathf.Clamp(W * 0.02f, 0.006f, 0.06f), lip = th * 1.8f;
            float ro = rr * 1.14f, hR = rr * 0.32f;
            Color steel = new Color(0.64f, 0.67f, 0.71f, 0f), steelD = new Color(0.48f, 0.51f, 0.55f, 0f);
            Color pipe = new Color(0.56f, 0.60f, 0.66f, 0f), yellow = new Color(0.99f, 0.76f, 0.12f, 0f);
            var vb = new VB();
            vb.Box(new Vector3(0f, th * 0.5f, 0f), new Vector3(L, th, W), steel);
            // бортик
            vb.Box(new Vector3(0f, th + lip * 0.5f, W * 0.5f - th * 0.5f), new Vector3(L, lip, th), steelD);
            vb.Box(new Vector3(0f, th + lip * 0.5f, -W * 0.5f + th * 0.5f), new Vector3(L, lip, th), steelD);
            vb.Box(new Vector3(L * 0.5f - th * 0.5f, th + lip * 0.5f, 0f), new Vector3(th, lip, W - 2f * th), steelD);
            vb.Box(new Vector3(-L * 0.5f + th * 0.5f, th + lip * 0.5f, 0f), new Vector3(th, lip, W - 2f * th), steelD);
            // ручки-скобы на торцах
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * (L * 0.5f + W * 0.06f);
                vb.Box(new Vector3(x, th + lip * 0.6f, 0f), new Vector3(th * 1.6f, th * 1.6f, W * 0.4f), steelD);
                vb.Box(new Vector3(s * (L * 0.5f + W * 0.03f), th + lip * 0.6f, W * 0.2f), new Vector3(W * 0.06f, th * 1.6f, th * 1.6f), steelD);
                vb.Box(new Vector3(s * (L * 0.5f + W * 0.03f), th + lip * 0.6f, -W * 0.2f), new Vector3(W * 0.06f, th * 1.6f, th * 1.6f), steelD);
            }
            // кольца — обрубки трубы, торцы покрашены жёлтым
            Mesh tube = MacaronMesh.Tube(rr, ro, hR, pipe, yellow, 40);
            var rings = new Vector3[nx * nz];
            float cx = L / nx, cz = W / nz;
            for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                {
                    Vector3 c = new Vector3(-L * 0.5f + cx * (ix + 0.5f), th, -W * 0.5f + cz * (iz + 0.5f));
                    rings[ix * nz + iz] = c;
                    vb.Append(tube, Matrix4x4.Translate(c));
                }
            Object.DestroyImmediate(tube);
            Mesh mesh = DS.SaveMesh(vb.Build("MacaronTray_Mesh"), "MacaronTray_Mesh", log);

            var go = new GameObject("MacaronTray");
            Undo.RegisterCreatedObjectUndo(go, "MacaronTray");
            go.transform.position = At() + Vector3.up * 0.2f;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = DS.PlateMat(log);
            BoxCollider bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(L, th, W); bc.center = new Vector3(0f, th * 0.5f, 0f);
            Transform cols = DS.FindOrCreate(go.transform, "TrayColliders");
            ColBox(cols, "LipN", new Vector3(0f, th + lip * 0.5f, W * 0.5f - th * 0.5f), new Vector3(L, lip, th));
            ColBox(cols, "LipS", new Vector3(0f, th + lip * 0.5f, -W * 0.5f + th * 0.5f), new Vector3(L, lip, th));
            ColBox(cols, "LipE", new Vector3(L * 0.5f - th * 0.5f, th + lip * 0.5f, 0f), new Vector3(th, lip, W));
            ColBox(cols, "LipW", new Vector3(-L * 0.5f + th * 0.5f, th + lip * 0.5f, 0f), new Vector3(th, lip, W));
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Clamp(L * W * 4f, 1f, 30f); rb.interpolation = RigidbodyInterpolation.Interpolate;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            MacaronTray tray = go.AddComponent<MacaronTray>();
            GameObject shellPrefab = null;
            foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
            {
                GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                if (p != null && p.GetComponent<MacaronShell>() != null) { shellPrefab = p; break; }
            }
            tray.EditorSetup(rings, rr, hR, MacaronMat(log), shellPrefab);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"＋ «MacaronTray» {L:0.00}×{W:0.00} м: {nx}×{nz} = {rings.Length} колец Ø{rr * 2f:0.00} м (обрубки трубы, жёлтые торцы)");
            log.AppendLine(shellPrefab != null ? $"  ✔ Shell Prefab = «{shellPrefab.name}»" : "  ⚠ Shell Prefab пуст — сделай скорлупку (меню 6), сохрани префабом и перетащи в Macaron Tray → Shell Prefab");
            log.AppendLine("\nДАЛЬШЕ: перетащи MacaronTray в Project → префаб (и в Spawnable Prefabs, если спавнишь) или оставь в сцене.");
            DS.Done("Противень с кольцами", log);
        }

        // ───────────────────────── 6. СКОРЛУПКА ─────────────────────────

        [MenuItem("CatCooks/Макарон/6. Скорлупка макарон (объект для префаба)", priority = 126)]
        public static void MakeShell()
        {
            var log = new StringBuilder();
            ProductDef shell = DS.Find(M.Shell), dish = DS.Find(M.Dish);
            if (shell == null) { EditorUtility.DisplayDialog("Макарон", "Сначала «1. Создать продукты и рецепты».", "Ок"); return; }
            float rr = RingRadiusInScene();
            Mesh m = Object.Instantiate(MacaronMesh.Shell(false));
            Vector3[] v = m.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = new Vector3(v[i].x * rr, v[i].y * rr * 0.78f, v[i].z * rr);
            m.vertices = v; m.RecalculateNormals(); m.RecalculateBounds();
            Mesh mesh = DS.SaveMesh(m, "MacaronShell_Mesh", log);
            Material mat = MacaronMat(log);

            var go = new GameObject("MacaronShell");
            Undo.RegisterCreatedObjectUndo(go, "MacaronShell");
            go.transform.position = At() + Vector3.up * 0.3f;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;          // для вида в редакторе; в игре вид — у детей
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            BoxCollider bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(rr * 2f, rr * 0.5f, rr * 2f); bc.center = new Vector3(0f, rr * 0.25f, 0f);
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.2f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            MacaronShell ms = go.AddComponent<MacaronShell>();
            var so = new SerializedObject(ms);
            DS.SetObj(so, "_product", shell);
            DS.SetInt(so, "_startState", (int)FoodState.Baked);
            so.ApplyModifiedProperties();
            ms.EditorSetup(dish, rr, mat);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"＋ «MacaronShell» Ø{rr * 2f:0.00} м: Macaron Shell (это и Food Piece) «{M.Shell}» → собранный «{M.Dish}», Box Collider, Rigidbody, сеть");
            log.AppendLine("\nДАЛЬШЕ РУКАМИ:\n1. Перетащи MacaronShell в Project → префаб MacaronShell.\n2. Добавь его в Spawnable Prefabs.\n" +
                           "3. Macaron Tray → Shell Prefab = MacaronShell.\n4. Удали MacaronShell из сцены.");
            DS.Done("Скорлупка макарон", log);
        }

        // ───────────────────────── 7. ПРОВЕРКА ─────────────────────────

        private static StringBuilder _sb;
        private static int _err, _warn;
        private static void Ok(string t) => _sb.AppendLine("  ✔ " + t);
        private static void Wr(string t) { _warn++; _sb.AppendLine("  ⚠ " + t); }
        private static void Er(string t) { _err++; _sb.AppendLine("  ✖ " + t); }

        [MenuItem("CatCooks/Макарон/7. Проверить макарон", priority = 127)]
        public static void Check()
        {
            _sb = new StringBuilder(); _err = 0; _warn = 0;
            _sb.AppendLine("■ Продукты");
            foreach (string t in new[] { M.AlmondFlour, M.AlmondMix, "Меренга", M.Cocoa, M.Batter, M.BatterOver, "Сахар", M.Caramel, M.CaramelBurnt,
                                         "Сливочное масло", M.Salt, M.SaltCaramel, "Шоколадная масса", M.Ganache, M.Shell, M.Dish })
            {
                ProductDef p = DS.Find(t);
                if (p == null) Er($"нет «{t}»"); else Ok($"«{t}» Id {p.Id}");
            }
            _sb.AppendLine("\n■ Рецепты");
            foreach (string t in new[] { "Тесто для макарон", "Тесто для макарон перемешанное", "Карамель", "Карамель горелая", "Солёная карамель", "Карамельный ганаш" })
            {
                bool found = false;
                foreach (string g in AssetDatabase.FindAssets("t:CookRecipeDef"))
                {
                    var r = AssetDatabase.LoadAssetAtPath<CookRecipeDef>(AssetDatabase.GUIDToAssetPath(g));
                    if (r != null && r.Title == t) found = true;
                }
                if (found) Ok($"рецепт «{t}»"); else Wr($"нет ассета «{t}» — меню 1 (книга соберёт по названиям, если продукты есть)");
            }

            _sb.AppendLine("\n■ Инструменты в сцене");
            SifterItem[] sifters = Object.FindObjectsByType<SifterItem>(FindObjectsSortMode.None);
            if (sifters.Length == 0) Wr("нет грохота — меню 3");
            foreach (SifterItem s in sifters)
            {
                Cookware cw = s.GetComponent<Cookware>();
                if (cw == null || cw.Def == null) Er($"грохот «{s.name}»: нет Cookware/Def"); else Ok($"грохот «{s.name}»");
            }
            PipingBagItem[] bags = Object.FindObjectsByType<PipingBagItem>(FindObjectsSortMode.None);
            if (bags.Length == 0) Wr("нет шприца — меню 4");
            foreach (PipingBagItem b in bags)
            {
                var so = new SerializedObject(b);
                if (so.FindProperty("_nozzle").objectReferenceValue == null && b.transform.Find("Nozzle") == null) Wr($"шприц «{b.name}»: нет Nozzle — струйки не видно");
                Ok($"шприц «{b.name}»");
            }
            MacaronTray[] trays = Object.FindObjectsByType<MacaronTray>(FindObjectsSortMode.None);
            if (trays.Length == 0) Wr("нет противня с кольцами — меню 5");
            foreach (MacaronTray t in trays)
            {
                int e0 = _err;
                var so = new SerializedObject(t);
                var sp = so.FindProperty("_shellPrefab").objectReferenceValue as GameObject;
                if (t.RingCount == 0) Er($"противень «{t.name}»: нет колец — меню 5 заново");
                if (sp == null) Er($"противень «{t.name}»: Shell Prefab пуст — меню 6, сохрани префабом, перетащи");
                else
                {
                    if (sp.GetComponent<MacaronShell>() == null) Er($"Shell Prefab «{sp.name}»: нет Macaron Shell");
                    if (sp.GetComponent<FishNet.Object.NetworkObject>() == null) Er($"Shell Prefab «{sp.name}»: нет Network Object");
                }
                if (so.FindProperty("_batterMat").objectReferenceValue == null) Wr($"противень «{t.name}»: нет материала теста (Batter Mat)");
                if (_err == e0) Ok($"противень «{t.name}»: {t.RingCount} колец" + (sp != null ? $", скорлупки «{sp.name}» (не забудь Spawnable Prefabs)" : ""));
            }
            int heats = Object.FindObjectsByType<CookwareHeat>(FindObjectsSortMode.None).Length;
            if (heats == 0) Wr("нет кастрюли с Cookware Heat — карамель не сварить"); else Ok($"тара с нагревом: {heats} (карамель — в кастрюле на костре)");
            if (Object.FindObjectsByType<MixerItem>(FindObjectsSortMode.None).Length == 0) Wr("нет миксера — тесто, карамель, ганаш не перемешать");
            if (Object.FindObjectsByType<CatCooks.Food.Tools.ScoopItem>(FindObjectsSortMode.None).Length == 0) Wr("нет мастерка — муку и сахар в грохот не кинуть");
            if (Object.FindObjectsByType<BakeOven>(FindObjectsSortMode.None).Length == 0) Wr("нет духовки");

            string head = _err > 0 ? $"ОШИБОК {_err}, предупреждений {_warn}" : _warn > 0 ? $"ошибок нет, предупреждений {_warn}" : "всё в порядке";
            Debug.Log("[Макарон — проверка] " + head + "\n" + _sb);
            EditorUtility.DisplayDialog("Макарон — проверка", head + "\n" + _sb, "Ок");
        }

        // ───────────────────────── материалы и меши ─────────────────────────

        internal static Material MacaronMat(StringBuilder log)
        {
            return DS.FoodHeapMat("Mat_Macaron", DS.MeringueFoamTexture(log), 5f, 0.3f, 0.95f, 0.25f, 0.006f, log);
        }

        private static Mesh PuckAsset(StringBuilder log)
        {
            string path = DS.ModelDir + "/Pile_Mesh.asset";
            Mesh have = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (have != null) return have;
            return DS.SaveMesh(Object.Instantiate(MacaronMesh.Puck()), "Pile_Mesh", log);
        }

        /// <summary>Сборщик меша с цветом в вершинах: коробки (плоские грани) и готовые меши.</summary>
        private sealed class VB
        {
            private readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>();
            private readonly List<Color> _c = new List<Color>();
            private readonly List<int> _t = new List<int>();
            private static readonly Vector3[] Ax = { Vector3.right, Vector3.up, Vector3.forward };

            public void Box(Vector3 c, Vector3 size, Color col)
            {
                Vector3 h = size * 0.5f;
                for (int i = 0; i < 3; i++)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector3 N = Ax[i] * s, U = Ax[(i + 1) % 3], V = Vector3.Cross(N, U);
                        float hn = h[i], hu = h[(i + 1) % 3];
                        float hv = Mathf.Abs(Vector3.Dot(h, new Vector3(Mathf.Abs(V.x), Mathf.Abs(V.y), Mathf.Abs(V.z))));
                        Vector3 o = c + N * hn;
                        int b = _v.Count;
                        _v.Add(o - U * hu - V * hv); _v.Add(o + U * hu - V * hv); _v.Add(o + U * hu + V * hv); _v.Add(o - U * hu + V * hv);
                        for (int k = 0; k < 4; k++) { _n.Add(N); _c.Add(col); }
                        // p00, p10 (+U), p11, p01 (+V): Cross(U, V) = N — наружу
                        _t.Add(b); _t.Add(b + 1); _t.Add(b + 3);
                        _t.Add(b + 1); _t.Add(b + 2); _t.Add(b + 3);
                    }
            }

            public void Append(Mesh m, Matrix4x4 mtx)
            {
                int b = _v.Count;
                Vector3[] v = m.vertices, n = m.normals;
                Color[] c = m.colors;
                for (int i = 0; i < v.Length; i++)
                {
                    _v.Add(mtx.MultiplyPoint3x4(v[i]));
                    _n.Add(mtx.MultiplyVector(n.Length > i ? n[i] : Vector3.up).normalized);
                    _c.Add(c.Length > i ? c[i] : Color.white);
                }
                foreach (int i in m.triangles) _t.Add(b + i);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                if (_v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(_v); m.SetNormals(_n); m.SetColors(_c); m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
#endif
