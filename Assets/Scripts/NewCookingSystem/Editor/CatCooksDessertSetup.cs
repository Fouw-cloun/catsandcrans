#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using D = CatCooks.Food.CookRecipeBook.Dessert;

namespace CatCooks.Food
{
    /// <summary>
    /// CatCooks → Десерты (Запечённая Аляска: бисквит, меренга, сборка на тарелке):
    ///   1. Создать продукты и рецепты — Яичная масса, Шоколадная масса, Бисквитное тесто, Бисквит,
    ///      Меренга, Меренга перебитая (+ Яичный белок, если нет); рецепты в Resources/CookRecipes.
    ///   2. Добавить массы в выделенную тару — ведро, кастрюля, опалубка: как выглядят и льются новые массы.
    ///   2б. Банки и ящики без жидкости — добавить LiquidAnchor (уровень жидкости) и массы.
    ///   3. Сделать опалубку в сцене — генерирует противень под решётку духовки и сразу настраивает.
    ///   3б. Настроить свою модель опалубки — то же на твоей модели.
    ///   4. Пласт бисквита + кубик — объекты для префабов (сохрани руками, добавь в Spawnable Prefabs).
    ///      Пласт — один бесшовный кусок, пилой режется на равные кубики там, куда смотришь.
    ///   5. Проверить десерты — ✔/⚠/✖, ничего не меняет.
    ///   6. Витрина Аляски — три стадии сборки (посмотреть вид).
    ///   7. Тарелка для Аляски — сделать в сцене / 7б — настроить свою модель: на ней собирается десерт.
    /// Префабы не создаются.
    /// </summary>
    public static class CatCooksDessertSetup
    {
        internal const string RecipeDir = "Assets/CatCooks/Food/Recipes/Resources/CookRecipes";
        internal const string DefDir = "Assets/CatCooks/Food/Cookware";
        internal const string ModelDir = "Assets/Models/Dessert";

        // ───────────────────────── 1. ПРОДУКТЫ И РЕЦЕПТЫ ─────────────────────────

        [MenuItem("CatCooks/Десерты/1. Создать продукты и рецепты (бисквит, меренга)", priority = 100)]
        public static void CreateAll()
        {
            var log = new StringBuilder();
            string dir = ProductsDir();
            ushort next = (ushort)(MaxId() + 1);
            FoodGroupDef sub = FindGroup("Group_SubRecipe") ?? GroupOf("Бульон") ?? GroupOf("Острый соус");
            FoodGroupDef eggG = FindGroup("Group_Egg") ?? GroupOf(D.Egg);

            ProductDef egg = Find(D.Egg), sugar = Find(D.Sugar), flour = Find(D.Flour);
            ProductDef choc = Find(D.Choc), paste = Find(D.ChocPaste), butter = Find(D.Butter);
            if (egg == null) log.AppendLine($"✖ нет «{D.Egg}» — CatCooks → Еда → Создать базу первого среза");
            if (sugar == null) log.AppendLine($"✖ нет «{D.Sugar}»");
            if (flour == null) log.AppendLine($"✖ нет «{D.Flour}» — CatCooks → Хлеб → 1");
            if (choc == null) log.AppendLine($"✖ нет «{D.Choc}» — CatCooks → Шоколад → 1");
            if (butter == null) log.AppendLine($"✖ нет «{D.Butter}»");
            ProductDef white = Ensure(D.White, eggG, new Color(0.96f, 0.95f, 0.9f), 0.03f, false, dir, ref next, log);

            ProductDef foam = Ensure(D.EggFoam, sub, new Color(0.98f, 0.88f, 0.56f), 0.06f, true, dir, ref next, log);
            ProductDef mass = Ensure(D.ChocMass, sub, new Color(0.30f, 0.17f, 0.10f), 0.06f, true, dir, ref next, log);
            ProductDef batter = Ensure(D.Batter, sub, new Color(0.36f, 0.22f, 0.15f), 0.06f, true, dir, ref next, log);
            ProductDef sponge = Ensure(D.Sponge, sub, new Color(0.30f, 0.18f, 0.11f), 0.3f, true, dir, ref next, log);
            ProductDef mer = Ensure(D.Meringue, sub, new Color(0.98f, 0.97f, 0.94f), 0.06f, true, dir, ref next, log);
            ProductDef merOver = Ensure(D.MeringueOver, sub, new Color(0.92f, 0.90f, 0.84f), 0.06f, true, dir, ref next, log);

            if (sponge != null)
            {
                if (sponge.DebrisColor.a < 0.01f) { sponge.DebrisColor = new Color(0.33f, 0.2f, 0.13f, 1f); EditorUtility.SetDirty(sponge); }
                // пилой: пласт → квадраты
                Rule(sponge, FoodState.Baked, FoodProcess.Cut, FoodState.Pieces, 1f, log);
            }
            foreach (ProductDef p in new[] { batter, mer, merOver, foam })
                if (p != null && p.DebrisColor.a < 0.01f) { p.DebrisColor = new Color(p.BaseColor.r, p.BaseColor.g, p.BaseColor.b, 1f); EditorUtility.SetDirty(p); }

            EnsureDir(RecipeDir);
            if (egg != null && sugar != null && foam != null) SaveRecipe("Яичная масса", r => CookRecipeBook.EggFoam(r, egg, sugar, foam), log);
            if (choc != null && butter != null && mass != null) SaveRecipe("Шоколадная масса", r => CookRecipeBook.ChocolateMass(r, choc, butter, mass), log);
            if (paste != null && butter != null && mass != null) SaveRecipe("Шоколадная масса из пасты", r => CookRecipeBook.ChocolateMass(r, paste, butter, mass, "Шоколадная масса из пасты"), log);
            if (foam != null && mass != null && flour != null && batter != null) SaveRecipe("Бисквитное тесто", r => CookRecipeBook.SpongeBatter(r, foam, mass, flour, batter, paste), log);
            if (white != null && sugar != null && mer != null) SaveRecipe("Меренга", r => CookRecipeBook.Meringue(r, white, sugar, mer), log);
            if (mer != null && merOver != null) SaveRecipe("Меренга перебитая", r => CookRecipeBook.MeringueOverwhipped(r, mer, merOver), log);

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
            log.AppendLine("\nДальше: выдели ведро, кастрюлю и опалубку → «2. Добавить массы в выделенную тару».");
            Done("Десерты", log);
        }

        // ───────────────────────── 2. МАССЫ В ТАРУ ─────────────────────────

        [MenuItem("CatCooks/Десерты/2. Добавить массы в выделенную тару (ведро, кастрюля, опалубка)", priority = 101)]
        public static void AddMasses()
        {
            var log = new StringBuilder();
            var targets = new List<CookwareLiquidVisual>();
            foreach (GameObject go in Selection.gameObjects)
                foreach (CookwareLiquidVisual lv in go.GetComponentsInChildren<CookwareLiquidVisual>(true))
                    if (!targets.Contains(lv)) targets.Add(lv);
            if (targets.Count == 0) { Dialog("Выдели ведро, кастрюлю, опалубку (или их LiquidAnchor) и нажми ещё раз."); return; }
            Color own = new Color(0, 0, 0, 0);
            var list = new[]
            {
                new RoleRow(D.Egg, CookwareLiquidVisual.Role.Liquid, new Color(0.98f, 0.84f, 0.42f, 1f), "жидкость (разбитые яйца)"),
                new RoleRow(D.White, CookwareLiquidVisual.Role.Liquid, new Color(0.95f, 0.94f, 0.88f, 1f), "жидкость"),
                new RoleRow(D.EggFoam, CookwareLiquidVisual.Role.Thick, own, "пышная масса"),
                new RoleRow(D.ChocMass, CookwareLiquidVisual.Role.Liquid, own, "жидкий шоколад"),
                new RoleRow(D.Batter, CookwareLiquidVisual.Role.Thick, own, "густое тесто, льётся"),
                new RoleRow(D.Meringue, CookwareLiquidVisual.Role.Thick, own, "пики"),
                new RoleRow(D.MeringueOver, CookwareLiquidVisual.Role.Thick, own, "осевшая масса"),
                new RoleRow(D.Choc, CookwareLiquidVisual.Role.Paste, own, "густая паста"),
                new RoleRow(D.ChocPaste, CookwareLiquidVisual.Role.Paste, own, "густая паста"),
                new RoleRow(D.Butter, CookwareLiquidVisual.Role.Paste, own, "густая паста"),
            };
            foreach (CookwareLiquidVisual lv in targets)
            {
                var so = new SerializedObject(lv);
                SerializedProperty arr = so.FindProperty("_products");
                log.AppendLine($"— {lv.transform.root.name}/{lv.name}");
                foreach (RoleRow e in list)
                {
                    ProductDef p = Find(e.Title);
                    if (p == null) { log.AppendLine($"  ⚠ нет «{e.Title}» — сначала меню 1"); continue; }
                    AddEntry(arr, p, (int)e.Role, e.Color, e.What, log);
                }
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(lv);
            }
            Done("Десерты", log);
        }

        // ───────────────────────── 2б. LIQUIDANCHOR ДЛЯ БАНОК ─────────────────────────

        /// <summary>
        /// Тара «для кучек» (банки, ящики) без уровня жидкости: создаёт LiquidAnchor — пустышку на дне полости
        /// (ось Y — вверх по миру, X/Z — вдоль стенок) с Cookware Liquid Visual нужной формы и размера,
        /// и прописывает массы десертов. Полость берётся из Inside Volume тары (если он похож на полость),
        /// иначе из габаритов модели. Круглая тара — форма «Круглая» и подгонка под стенки модели.
        /// </summary>
        [MenuItem("CatCooks/Десерты/2б. Банки и ящики без жидкости — добавить LiquidAnchor (выдели)", priority = 101)]
        public static void AddLiquidAnchors()
        {
            var log = new StringBuilder();
            var done = new HashSet<Cookware>();
            foreach (GameObject sel in Selection.gameObjects)
            {
                Cookware cw = sel.GetComponentInParent<Cookware>();
                if (cw == null) cw = sel.GetComponentInChildren<Cookware>(true);
                if (cw == null) { log.AppendLine($"— «{sel.name}»: нет Cookware — пропущен"); continue; }
                if (!done.Add(cw)) continue;
                if (EditorUtility.IsPersistent(cw)) { log.AppendLine($"— «{cw.name}»: это префаб в Project — перетащи в сцену"); continue; }
                CookwareLiquidVisual have = cw.GetComponentInChildren<CookwareLiquidVisual>(true);
                if (have != null) { log.AppendLine($"✔ «{cw.name}»: LiquidAnchor уже есть («{have.name}») — только массы:"); AddDessertRoles(have, log); continue; }
                if (!Cavity(cw, out Vector3 bottom, out Quaternion rot, out Vector2 half, out float height, out string from, log))
                { log.AppendLine($"✖ «{cw.name}»: не смог измерить полость — нет мешей"); continue; }

                bool round = IsRound(cw, rot, bottom, half, height);
                Undo.RegisterFullObjectHierarchyUndo(cw.gameObject, "LiquidAnchor");
                var go = new GameObject("LiquidAnchor");
                Undo.RegisterCreatedObjectUndo(go, "LiquidAnchor");
                go.transform.SetParent(cw.transform, false);
                go.transform.SetPositionAndRotation(bottom, rot);
                go.layer = cw.gameObject.layer;
                CookwareLiquidVisual lv = Undo.AddComponent<CookwareLiquidVisual>(go);
                var so = new SerializedObject(lv);
                SetObj(so, "_cookware", cw);
                SetF(so, "_maxHeight", Mathf.Max(0.02f, height * 0.92f));
                if (round)
                {
                    float r = Mathf.Min(half.x, half.y);
                    SetInt(so, "_form", (int)CookwareLiquidVisual.Form.Round);
                    SetF(so, "_radiusBottom", r * 0.95f);
                    SetF(so, "_radiusTop", r);
                }
                else
                {
                    SetInt(so, "_form", (int)CookwareLiquidVisual.Form.Box);
                    SetV2(so, "_halfSize", half);
                }
                so.ApplyModifiedProperties();
                if (round)
                {
                    var fit = typeof(CookwareLiquidVisual).GetMethod("FitToModelWalls", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (fit != null) { fit.Invoke(lv, null); log.AppendLine("  · круглая — подогнал под стенки модели"); }
                }
                log.AppendLine($"＋ «{cw.name}»: LiquidAnchor ({(round ? "круглая" : "прямоугольная")}) — полость по {from}: " +
                               (round ? $"радиус {Mathf.Min(half.x, half.y):0.00}" : $"{half.x * 2f:0.00}×{half.y * 2f:0.00}") + $" м, высота {height:0.00} м");
                AddDessertRoles(lv, log);
                EditorUtility.SetDirty(cw.gameObject);
            }
            if (done.Count > 0 && Selection.activeGameObject != null) EditorSceneManager.MarkSceneDirty(Selection.activeGameObject.scene);
            log.AppendLine("\nПроверь: выдели LiquidAnchor — голубой гизмо должен стоять внутри тары, на дне. Не так — поправь Half Size / Max Height руками.\n" +
                           "Префаб — Overrides → Apply All. Лить ИЗ банки (наклоном) можно, только если на ней Carried Container + Cookware Spill (как у ведра).");
            Done("LiquidAnchor", log);
        }

        private static void AddDessertRoles(CookwareLiquidVisual lv, StringBuilder log)
        {
            Color own = new Color(0, 0, 0, 0);
            var rows = new[]
            {
                new RoleRow("Вода", CookwareLiquidVisual.Role.Liquid, own, "жидкость"),
                new RoleRow(D.Egg, CookwareLiquidVisual.Role.Liquid, new Color(0.98f, 0.84f, 0.42f, 1f), "жидкость (разбитые яйца)"),
                new RoleRow(D.White, CookwareLiquidVisual.Role.Liquid, new Color(0.95f, 0.94f, 0.88f, 1f), "жидкость"),
                new RoleRow(D.EggFoam, CookwareLiquidVisual.Role.Thick, own, "пышная масса"),
                new RoleRow(D.ChocMass, CookwareLiquidVisual.Role.Liquid, own, "жидкий шоколад"),
                new RoleRow(D.Batter, CookwareLiquidVisual.Role.Thick, own, "густое тесто"),
                new RoleRow(D.Meringue, CookwareLiquidVisual.Role.Thick, own, "пики"),
                new RoleRow(D.MeringueOver, CookwareLiquidVisual.Role.Thick, own, "осевшая масса"),
            };
            var so = new SerializedObject(lv);
            SerializedProperty arr = so.FindProperty("_products");
            foreach (RoleRow e in rows)
            {
                ProductDef p = Find(e.Title);
                if (p == null) continue;
                AddEntry(arr, p, (int)e.Role, e.Color, e.What, log);
            }
            so.ApplyModifiedProperties();
        }

        /// <summary>
        /// Полость тары в мире: центр дна, поворот (Y — вверх по миру, X/Z — вдоль стенок), полуразмеры, высота.
        /// Inside Volume — если это коробка-полость (не тонкое дно), иначе габарит модели минус стенки.
        /// </summary>
        private static bool Cavity(Cookware cw, out Vector3 bottom, out Quaternion rot, out Vector2 half, out float height, out string from, StringBuilder log)
        {
            bottom = Vector3.zero; rot = Quaternion.identity; half = Vector2.zero; height = 0f; from = "";
            Transform root = cw.transform;
            // оси стенок: та ось корня, что ближе всего к горизонтали «вперёд»
            Vector3 fwd = Vector3.ProjectOnPlane(root.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(root.up, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(root.right, Vector3.up);
            rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            Quaternion inv = Quaternion.Inverse(rot);

            // габарит модели в этих осях
            bool any = false;
            Bounds mb = new Bounds(Vector3.zero, Vector3.zero);
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                string n = mf.gameObject.name;
                if (n.Contains("HeapAnchor") || n.Contains("LiquidAnchor") || n.Contains("(auto)")) continue;
                bool underHeap = false;
                for (Transform p = mf.transform; p != null && p != root; p = p.parent) if (p.name.Contains("HeapAnchor")) underHeap = true;
                if (underHeap) continue;
                Bounds b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 p = inv * (mf.transform.TransformPoint(c) - root.position);
                    if (!any) { mb = new Bounds(p, Vector3.zero); any = true; } else mb.Encapsulate(p);
                }
            }
            if (!any) return false;

            // Inside Volume, если это похоже на полость
            Collider iv = cw.InsideVolume;
            if (iv is BoxCollider bc)
            {
                Bounds ib = new Bounds(inv * (bc.transform.TransformPoint(bc.center) - root.position), Vector3.zero);
                Vector3 e = bc.size * 0.5f;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = bc.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                    ib.Encapsulate(inv * (bc.transform.TransformPoint(c) - root.position));
                }
                bool tallEnough = ib.size.y >= mb.size.y * 0.3f;
                bool wideEnough = ib.size.x >= mb.size.x * 0.4f && ib.size.z >= mb.size.z * 0.4f;
                if (tallEnough && wideEnough)
                {
                    float floorY = Mathf.Max(ib.min.y, mb.min.y + mb.size.y * 0.03f);
                    bottom = root.position + rot * new Vector3(ib.center.x, floorY, ib.center.z);
                    half = new Vector2(ib.extents.x * 0.98f, ib.extents.z * 0.98f);
                    height = Mathf.Min(ib.max.y, mb.max.y) - floorY;
                    from = "Inside Volume";
                    return height > 0.005f;
                }
                log.AppendLine($"  ℹ «{cw.name}»: Inside Volume похож на дно/кусок, а не на полость — меряю по модели");
            }
            // по модели: стенки ~6% ширины, дно ~4% высоты
            float wall = Mathf.Min(mb.size.x, mb.size.z) * 0.06f;
            float floor = mb.size.y * 0.04f;
            bottom = root.position + rot * new Vector3(mb.center.x, mb.min.y + floor, mb.center.z);
            half = new Vector2(Mathf.Max(0.01f, mb.extents.x - wall), Mathf.Max(0.01f, mb.extents.z - wall));
            height = mb.size.y - floor;
            from = "габариту модели";
            return true;
        }

        /// <summary>Круглая ли тара: у квадратной на середине высоты есть вершины в углах (|x|/a + |z|/b ≈ 2), у круглой — нет (≈ 1.4).</summary>
        private static bool IsRound(Cookware cw, Quaternion rot, Vector3 bottom, Vector2 half, float height)
        {
            Quaternion inv = Quaternion.Inverse(rot);
            float best = 0f; int n = 0;
            foreach (MeshFilter mf in cw.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.gameObject.name.Contains("HeapAnchor")) continue;
                Vector3[] vs;
                try { vs = mf.sharedMesh.vertices; } catch { continue; }
                if (vs == null) continue;
                int step = Mathf.Max(1, vs.Length / 4000);
                for (int i = 0; i < vs.Length; i += step)
                {
                    Vector3 p = inv * (mf.transform.TransformPoint(vs[i]) - bottom);
                    if (p.y < height * 0.25f || p.y > height * 0.75f) continue;
                    float k = Mathf.Abs(p.x) / Mathf.Max(1e-4f, half.x) + Mathf.Abs(p.z) / Mathf.Max(1e-4f, half.y);
                    if (k > best) best = k;
                    n++;
                }
            }
            if (n < 8) return false;               // не прочитать вершины — считаем прямоугольной
            return best < 1.7f;
        }

        internal struct RoleRow
        {
            public string Title; public CookwareLiquidVisual.Role Role; public Color Color; public string What;
            public RoleRow(string t, CookwareLiquidVisual.Role r, Color c, string w) { Title = t; Role = r; Color = c; What = w; }
        }

        // ───────────────────────── 3. СДЕЛАТЬ ОПАЛУБКУ ─────────────────────────

        /// <summary>
        /// Деревянная опалубка во всю решётку духовки: длинная и низкая, дно из досок, борта из двух досок,
        /// снизу три поперечных бруска, на торцах ручки-планки, по углам гвозди. Размер — под решётку
        /// духовки в сцене (95% длины, 93% ширины), высота борта ~7% ширины.
        /// </summary>
        [MenuItem("CatCooks/Десерты/3. Сделать опалубку в сцене (деревянная, во всю решётку)", priority = 102)]
        public static void MakeTray()
        {
            var log = new StringBuilder();
            ProductDef batter = Find(D.Batter), sponge = Find(D.Sponge);
            if (batter == null || sponge == null) { Dialog("Сначала «1. Создать продукты и рецепты»."); return; }

            float oL = 0.6f, oW = 0.4f;
            BakeOven oven = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<BakeOven>() : null;
            if (oven == null) oven = Object.FindFirstObjectByType<BakeOven>();
            if (oven != null && oven.TryGetGrill(out Collider g))
            {
                Vector3 gs = g is BoxCollider gb ? Vector3.Scale(gb.size, g.transform.lossyScale) : g.bounds.size;
                float a = Mathf.Abs(gs.x), b = Mathf.Abs(gs.z);
                oL = Mathf.Max(a, b) * 0.95f;
                oW = Mathf.Min(a, b) * 0.93f;
                log.AppendLine($"Решётка «{oven.name}»: {Mathf.Max(a, b):0.00}×{Mathf.Min(a, b):0.00} м → опалубка {oL:0.00}×{oW:0.00} м");
            }
            else log.AppendLine($"Духовки с решёткой (Grill) в сцене нет — опалубка {oL:0.00}×{oW:0.00} м. Сделай решётку (окно «Духовка-бочка») и нажми ещё раз");

            float t = Mathf.Clamp(oW * 0.022f, 0.012f, 0.06f);      // толщина доски борта
            float tf = t * 0.8f;                                      // толщина доски дна
            float bh = t * 0.8f;                                      // брусок снизу
            float H = Mathf.Clamp(oW * 0.07f, 0.04f, 0.3f);          // высота борта над дном
            float L = oL - 2f * t, W = oW - 2f * t;                   // внутри
            float y0 = bh;                                            // низ дна
            float yIn = y0 + tf;                                      // уровень дна внутри
            float wallH = tf + H;
            var rnd = new System.Random(12345);

            var mb = new BoxMesh();
            // бруски снизу (поперёк)
            foreach (float x in new[] { -0.38f * L, 0f, 0.38f * L })
                mb.Add(new Vector3(x, bh * 0.5f, 0f), new Vector3(t * 1.6f, bh, oW * 0.98f), 2, 0, rnd);
            // доски дна (вдоль), с щелями
            int n = Mathf.Clamp(Mathf.RoundToInt(W / Mathf.Max(0.08f, oW * 0.11f)), 4, 10);
            float gap = t * 0.15f, pw = (W - (n - 1) * gap) / n;
            for (int i = 0; i < n; i++)
            {
                float z = -W * 0.5f + pw * 0.5f + i * (pw + gap);
                float th = tf * (1f + ((float)rnd.NextDouble() * 2f - 1f) * 0.06f);
                mb.Add(new Vector3(0f, y0 + th * 0.5f, z), new Vector3(L, th, pw), 0, 0, rnd);
            }
            // борта: длинные — во всю длину, по две доски (верхняя чуть толще — виден шов)
            float h1 = wallH * 0.5f;
            for (int s = -1; s <= 1; s += 2)
            {
                float z = s * (W * 0.5f + t * 0.5f);
                mb.Add(new Vector3(0f, y0 + h1 * 0.5f, z), new Vector3(oL, h1, t), 0, 0, rnd);
                mb.Add(new Vector3(0f, y0 + h1 + h1 * 0.5f, z + s * t * 0.04f), new Vector3(oL * 0.998f, h1, t * 1.08f), 0, 0, rnd);
                float x = s * (L * 0.5f + t * 0.5f);
                mb.Add(new Vector3(x, y0 + h1 * 0.5f, 0f), new Vector3(t, h1, W), 2, 0, rnd);
                mb.Add(new Vector3(x + s * t * 0.04f, y0 + h1 + h1 * 0.5f, 0f), new Vector3(t * 1.08f, h1, W * 0.998f), 2, 0, rnd);
                // ручка-планка на торце
                mb.Add(new Vector3(s * (L * 0.5f + t + t * 0.55f), y0 + wallH * 0.62f, 0f), new Vector3(t * 1.1f, t * 0.9f, W * 0.55f), 2, 0, rnd);
            }
            // гвозди: на длинных бортах против торцов, по 2 на доску
            float nh = t * 0.32f, nd = Mathf.Max(0.002f, t * 0.12f);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    for (int row = 0; row < 2; row++)
                    {
                        float y = y0 + h1 * (row + 0.5f);
                        float zFace = sz * (W * 0.5f + t + (row == 1 ? t * 0.08f : 0f) + nd * 0.5f);
                        float x = sx * (L * 0.5f + t * 0.5f);
                        mb.Add(new Vector3(x, y - h1 * 0.18f, zFace), new Vector3(nh, nh, nd), 0, 1, rnd);
                        mb.Add(new Vector3(x, y + h1 * 0.18f, zFace), new Vector3(nh, nh, nd), 0, 1, rnd);
                    }
            Mesh mesh = mb.Build("BakeTray_Wood");
            EnsureDir(ModelDir);
            string mpath = AssetDatabase.GenerateUniqueAssetPath(ModelDir + "/BakeTray_Wood.asset");
            AssetDatabase.CreateAsset(mesh, mpath);

            Vector3 at = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            if (Physics.Raycast(at + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore)) at = hit.point;

            var go = new GameObject("BakeTray");
            Undo.RegisterCreatedObjectUndo(go, "Опалубка");
            Transform root = go.transform;
            root.position = at;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { WoodMat(log), NailMat(log) };
            log.AppendLine($"＋ «BakeTray»: деревянная, {oL:0.00}×{oW:0.00} м, борт {H:0.00} м, доска {t * 100f:0.#} см, дно — {n} досок (меш {mpath})");

            // коллайдеры: основание (бруски + дно) и 4 борта
            Transform cols = FindOrCreate(root, "TrayColliders");
            Box(cols, "Floor", new Vector3(0f, yIn * 0.5f, 0f), new Vector3(oL, yIn, oW));
            Box(cols, "WallF", new Vector3(0f, y0 + wallH * 0.5f, W * 0.5f + t * 0.5f), new Vector3(oL, wallH, t));
            Box(cols, "WallB", new Vector3(0f, y0 + wallH * 0.5f, -W * 0.5f - t * 0.5f), new Vector3(oL, wallH, t));
            Box(cols, "WallR", new Vector3(L * 0.5f + t * 0.5f, y0 + wallH * 0.5f, 0f), new Vector3(t, wallH, W));
            Box(cols, "WallL", new Vector3(-L * 0.5f - t * 0.5f, y0 + wallH * 0.5f, 0f), new Vector3(t, wallH, W));

            AddByName(go, "NetworkObject", log);
            AddByName(go, "NetworkTransform", log);
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Clamp(oL * oW * 6f, 2f, 60f);
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            Cookware cw = go.AddComponent<Cookware>();
            Transform mouth = FindOrCreate(root, "Mouth");
            mouth.localPosition = new Vector3(0f, y0 + wallH, 0f);
            Transform inside = FindOrCreate(root, "Inside");
            inside.localPosition = new Vector3(0f, yIn + H * 0.5f, 0f);
            BoxCollider ib = inside.gameObject.AddComponent<BoxCollider>();
            ib.isTrigger = true; ib.size = new Vector3(L * 0.97f, H, W * 0.97f);
            var cso = new SerializedObject(cw);
            SetObj(cso, "Def", EnsureDef(log));
            SetObj(cso, "_mouth", mouth);
            SetObj(cso, "_insideVolume", ib);
            cso.ApplyModifiedProperties();

            Transform anchor = FindOrCreate(root, "LiquidAnchor");
            anchor.localPosition = new Vector3(0f, yIn + 0.001f, 0f);
            CookwareLiquidVisual lv = anchor.gameObject.AddComponent<CookwareLiquidVisual>();
            var lso = new SerializedObject(lv);
            SetObj(lso, "_cookware", cw);
            SetInt(lso, "_form", (int)CookwareLiquidVisual.Form.Box);
            SetV2(lso, "_halfSize", new Vector2(L * 0.5f * 0.99f, W * 0.5f * 0.99f));
            SetF(lso, "_maxHeight", H * 0.55f);
            AddEntry(lso.FindProperty("_products"), batter, (int)CookwareLiquidVisual.Role.Thick, new Color(0, 0, 0, 0), "густое тесто", log);
            lso.ApplyModifiedProperties();

            GameObject f = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(f, "Fill");
            Object.DestroyImmediate(f.GetComponent<Collider>());
            f.name = "Fill";
            Transform fill = f.transform;
            fill.SetParent(root, false);
            float fh = H * 0.95f;
            fill.localPosition = new Vector3(0f, yIn + fh * 0.5f, 0f);
            fill.localScale = new Vector3(L * 0.985f, fh, W * 0.985f);
            f.GetComponent<MeshRenderer>().sharedMaterial = SpongeMat(log);

            BakeTray tray = go.AddComponent<BakeTray>();
            var tso = new SerializedObject(tray);
            SetObj(tso, "_batter", batter);
            SetObj(tso, "_result", sponge);
            SetObj(tso, "_fill", fill);
            SetF(tso, "_rawHeight", 0.5f);
            SetF(tso, "_placeReach", Mathf.Max(3f, oL * 0.8f));
            tso.ApplyModifiedProperties();
            log.AppendLine("＋ Cookware «Опалубка», Mouth, Inside, LiquidAnchor, Fill, Bake Tray, коллайдеры, сеть, Rigidbody");

            Selection.activeGameObject = go;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine("\nДАЛЬШЕ РУКАМИ:\n" +
                           "1. Не снимая выделения — «4. Пласт бисквита в сцену» → SpongeSlab сохрани префабом, в Spawnable Prefabs, " +
                           "в опалубке Bake Tray → Slab Prefab = SpongeSlab, SpongeSlab из сцены удали.\n" +
                           "2. Старую опалубку удали; BakeTray перетащи в Project → префаб BakeTray.\n" +
                           "Масштаб опалубки НЕ меняй — размер уже под решётку (иначе уровень теста не совпадёт с бортами).");
            Done("Опалубка", log);
        }

        private static Material WoodMat(StringBuilder log)
        {
            string path = ModelDir + "/Mat_TrayWood.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            EnsureDir(ModelDir);
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh) { name = "Mat_TrayWood" };
            Texture2D tex = WoodTexture(log);
            if (tex != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.18f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.18f);
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"＋ материал {path} (дерево)");
            return m;
        }

        private static Material NailMat(StringBuilder log)
        {
            string path = ModelDir + "/Mat_Nail.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            EnsureDir(ModelDir);
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh) { name = "Mat_Nail" };
            Color c = new Color(0.24f, 0.24f, 0.26f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.9f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.45f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>Текстура досок 512×256: волокна вдоль U, тёмные прожилки, пара сучков. Бесшовная.</summary>
        private static Texture2D WoodTexture(StringBuilder log)
        {
            string path = ModelDir + "/Tex_TrayWood.png";
            Texture2D have = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (have != null) return have;
            const int NX = 512, NY = 256;
            Color light = new Color(0.78f, 0.58f, 0.36f), dark = new Color(0.52f, 0.34f, 0.19f);
            var px = new Color32[NX * NY];
            var rnd = new System.Random(777);
            var kx = new float[3]; var ky = new float[3];
            for (int i = 0; i < 3; i++) { kx[i] = (float)rnd.NextDouble() * NX; ky[i] = (float)rnd.NextDouble() * NY; }
            for (int y = 0; y < NY; y++)
                for (int x = 0; x < NX; x++)
                {
                    float u = x / (float)NX, v = y / (float)NY;
                    // волокна: полосы вдоль U, изогнутые шумом; плюс сучки — завихрение полос вокруг точки
                    float warp = TileN(u, v, 2f, 8f) * 3.2f;
                    float knot = 0f;
                    for (int i = 0; i < 3; i++)
                    {
                        float dx = Mathf.Abs(x - kx[i]); dx = Mathf.Min(dx, NX - dx);
                        float dy = Mathf.Abs(y - ky[i]); dy = Mathf.Min(dy, NY - dy);
                        float d = Mathf.Sqrt(dx * dx * 0.12f + dy * dy) / 14f;
                        knot += Mathf.Exp(-d * d) * 2.5f;
                    }
                    float s = Mathf.Sin((v * 26f + warp + knot) * Mathf.PI * 2f) * 0.5f + 0.5f;
                    float line = Mathf.SmoothStep(0.72f, 1f, s);
                    float blot = TileN(u, v, 1f, 3f);
                    float k = Mathf.Clamp01(0.55f * line + 0.25f * blot + 0.2f * Mathf.Clamp01(knot * 0.5f));
                    px[y * NX + x] = Color.Lerp(light, dark, k);
                }
            var tex = new Texture2D(NX, NY, TextureFormat.RGB24, true);
            tex.SetPixels32(px); tex.Apply();
            EnsureDir(ModelDir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            log.AppendLine($"＋ текстура {path}");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Бесшовный шум: смесь четырёх сдвинутых выборок Перлина.</summary>
        private static float TileN(float u, float v, float fu, float fv)
        {
            float a = Mathf.PerlinNoise(u * fu * 4f + 3.1f, v * fv + 7.3f), b = Mathf.PerlinNoise((u - 1f) * fu * 4f + 3.1f, v * fv + 7.3f);
            float c = Mathf.PerlinNoise(u * fu * 4f + 3.1f, (v - 1f) * fv + 7.3f), d = Mathf.PerlinNoise((u - 1f) * fu * 4f + 3.1f, (v - 1f) * fv + 7.3f);
            return a * (1 - u) * (1 - v) + b * u * (1 - v) + c * (1 - u) * v + d * u * v;
        }

        /// <summary>Меш из коробок: плоские нормали, волокна дерева вдоль заданной оси, два материала (дерево, гвозди).</summary>
        private sealed class BoxMesh
        {
            private readonly List<Vector3> _v = new List<Vector3>();
            private readonly List<Vector3> _n = new List<Vector3>();
            private readonly List<Vector2> _uv = new List<Vector2>();
            private readonly List<int>[] _t = { new List<int>(), new List<int>() };
            private static readonly Vector3[] Ax = { Vector3.right, Vector3.up, Vector3.forward };

            /// <param name="grain">ось волокон: 0 — X, 2 — Z</param>
            /// <param name="sub">0 — дерево, 1 — гвозди</param>
            public void Add(Vector3 c, Vector3 size, int grain, int sub, System.Random rnd)
            {
                Vector3 h = size * 0.5f;
                Vector3 G = Ax[grain];
                float ou = (float)rnd.NextDouble() * 7f, ov = (float)rnd.NextDouble() * 3f;   // у каждой доски свой кусок текстуры
                float len = Mathf.Max(0.05f, Mathf.Abs(Vector3.Dot(size, G)));
                for (int i = 0; i < 3; i++)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector3 N = Ax[i] * s;
                        Vector3 U = Ax[(i + 1) % 3];
                        Vector3 V = Vector3.Cross(U, N);
                        float hn = h[i], hu = h[(i + 1) % 3];
                        float hv = Mathf.Abs(Vector3.Dot(h, new Vector3(Mathf.Abs(V.x), Mathf.Abs(V.y), Mathf.Abs(V.z))));
                        Vector3 o = c + N * hn;
                        int b = _v.Count;
                        Vector3[] p = { o - U * hu - V * hv, o - U * hu + V * hv, o + U * hu + V * hv, o + U * hu - V * hv };
                        bool alongU = Mathf.Abs(Vector3.Dot(U, G)) > 0.5f;
                        foreach (Vector3 q in p)
                        {
                            _v.Add(q); _n.Add(N);
                            Vector3 lq = q - c;
                            float a = alongU ? Vector3.Dot(lq, U) : Vector3.Dot(lq, V);
                            float bb = alongU ? Vector3.Dot(lq, V) : Vector3.Dot(lq, U);
                            // вдоль волокон — одна текстура на ~1.5 длины доски, поперёк — плотнее
                            _uv.Add(new Vector2(a / (len * 1.5f) + ou, bb / Mathf.Max(0.02f, len * 0.12f) * 0.25f + ov));
                        }
                        List<int> t = _t[Mathf.Clamp(sub, 0, 1)];
                        t.Add(b); t.Add(b + 1); t.Add(b + 2);
                        t.Add(b); t.Add(b + 2); t.Add(b + 3);
                    }
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                if (_v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(_v); m.SetNormals(_n); m.SetUVs(0, _uv);
                m.subMeshCount = 2;
                m.SetTriangles(_t[0], 0); m.SetTriangles(_t[1], 1);
                m.RecalculateBounds();
                m.RecalculateTangents();
                return m;
            }
        }

        // ───────────────────────── 3. ОПАЛУБКА ─────────────────────────

        [MenuItem("CatCooks/Десерты/3б. Настроить свою модель опалубки (выдели её)", priority = 103)]
        public static void SetupTray()
        {
            var log = new StringBuilder();
            if (Selection.gameObjects.Length == 0) { Dialog("Выдели КОРЕНЬ опалубки (противень с бортиками) в сцене."); return; }
            ProductDef batter = Find(D.Batter), sponge = Find(D.Sponge);
            if (batter == null || sponge == null) { Dialog("Сначала «1. Создать продукты и рецепты»."); return; }
            foreach (GameObject go in Selection.gameObjects)
            {
                if (EditorUtility.IsPersistent(go)) { log.AppendLine($"— «{go.name}»: это префаб в Project — перетащи в сцену и настрой там"); continue; }
                Undo.RegisterFullObjectHierarchyUndo(go, "Опалубка");
                Transform root = go.transform;
                log.AppendLine($"ОПАЛУБКА «{go.name}»");

                // ничего «предметного» кроме Bake Tray (он сам берётся в руки)
                foreach (string tn in new[] { "CarriedContainer", "PickupItem", "CookwareHeat" })
                    foreach (Component c in go.GetComponents<Component>())
                        if (c != null && c.GetType().Name == tn)
                        { Undo.DestroyObjectImmediate(c); log.AppendLine($"  － убран {tn} (Bake Tray берётся в руки сам, греет духовка)"); }

                AddByName(go, "NetworkObject", log);
                AddByName(go, "NetworkTransform", log);
                Rigidbody rb = Need<Rigidbody>(go);
                if (rb.mass < 1f) rb.mass = 3f;

                Bounds b = LocalBounds(root);
                if (b.size == Vector3.zero) { log.AppendLine("  ✖ нет мешей — нечего мерить"); continue; }

                // коллайдеры: дно и бортики, если нет своих обычных
                bool solid = false;
                foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                {
                    if (c.isTrigger) continue;
                    if (c is MeshCollider mc && !mc.convex) { log.AppendLine($"  ⚠ «{c.name}»: Mesh Collider без Convex — с Rigidbody не работает, выключаю"); Undo.RecordObject(c, "off"); c.enabled = false; continue; }
                    if (c.enabled) solid = true;
                }
                if (!solid) { TrayBoxes(root, b, log); }

                // Cookware
                Cookware cw = Need<Cookware>(go);
                var cso = new SerializedObject(cw);
                if (cso.FindProperty("Def").objectReferenceValue == null)
                    cso.FindProperty("Def").objectReferenceValue = EnsureDef(log);
                if (cso.FindProperty("_mouth").objectReferenceValue == null)
                {
                    Transform mouth = FindOrCreate(root, "Mouth");
                    mouth.localPosition = new Vector3(b.center.x, b.max.y, b.center.z); mouth.localRotation = Quaternion.identity;
                    cso.FindProperty("_mouth").objectReferenceValue = mouth;
                    log.AppendLine("  ＋ Mouth — верх по центру");
                }
                if (cso.FindProperty("_insideVolume").objectReferenceValue == null)
                {
                    Transform inside = FindOrCreate(root, "Inside");
                    inside.localPosition = b.center; inside.localRotation = Quaternion.identity; inside.localScale = Vector3.one;
                    BoxCollider bc = Need<BoxCollider>(inside.gameObject);
                    bc.isTrigger = true; bc.center = Vector3.zero;
                    bc.size = new Vector3(b.size.x * 0.92f, b.size.y, b.size.z * 0.92f);
                    cso.FindProperty("_insideVolume").objectReferenceValue = bc;
                    log.AppendLine("  ＋ Inside — триггер полости (Is Trigger)");
                }
                cso.ApplyModifiedProperties();

                // уровень теста
                CookwareLiquidVisual lv = go.GetComponentInChildren<CookwareLiquidVisual>(true);
                if (lv == null)
                {
                    Transform anchor = FindOrCreate(root, "LiquidAnchor");
                    anchor.localPosition = new Vector3(b.center.x, b.min.y + b.size.y * 0.08f, b.center.z);
                    anchor.localRotation = Quaternion.identity;
                    lv = Undo.AddComponent<CookwareLiquidVisual>(anchor.gameObject);
                    Vector3 ls = root.lossyScale;
                    var lso = new SerializedObject(lv);
                    SetObj(lso, "_cookware", cw);
                    SetInt(lso, "_form", (int)CookwareLiquidVisual.Form.Box);
                    SetV2(lso, "_halfSize", new Vector2(b.extents.x * Mathf.Abs(ls.x) * 0.9f, b.extents.z * Mathf.Abs(ls.z) * 0.9f));
                    SetF(lso, "_maxHeight", Mathf.Max(0.02f, b.size.y * Mathf.Abs(ls.y) * 0.85f));
                    lso.ApplyModifiedProperties();
                    log.AppendLine("  ＋ LiquidAnchor (Cookware Liquid Visual, прямоугольная) — проверь размер: ⋮ → «Подогнать под стенки модели»");
                }
                {
                    var lso = new SerializedObject(lv);
                    AddEntry(lso.FindProperty("_products"), batter, (int)CookwareLiquidVisual.Role.Thick, new Color(0, 0, 0, 0), "густое тесто", log);
                    lso.ApplyModifiedProperties();
                }

                // Fill — пласт бисквита в опалубке
                Transform fill = root.Find("Fill");
                if (fill == null)
                {
                    GameObject f = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Undo.RegisterCreatedObjectUndo(f, "Fill");
                    Object.DestroyImmediate(f.GetComponent<Collider>());
                    f.name = "Fill";
                    fill = f.transform;
                    fill.SetParent(root, false);
                    float h = b.size.y * 0.8f;
                    fill.localPosition = new Vector3(b.center.x, b.min.y + b.size.y * 0.08f + h * 0.5f, b.center.z);
                    fill.localRotation = Quaternion.identity;
                    fill.localScale = new Vector3(b.size.x * 0.9f, h, b.size.z * 0.9f);
                    f.GetComponent<MeshRenderer>().sharedMaterial = SpongeMat(log);
                    log.AppendLine("  ＋ Fill — пласт бисквита (куб под размер полости, материал Mat_Sponge). Можно заменить своим мешем: низ — у дна");
                }

                // Bake Tray
                BakeTray tray = Need<BakeTray>(go);
                var tso = new SerializedObject(tray);
                SetObj(tso, "_batter", batter);
                SetObj(tso, "_result", sponge);
                SetObj(tso, "_fill", fill);
                tso.ApplyModifiedProperties();
                bool slab = tso.FindProperty("_slabPrefab").objectReferenceValue != null;
                log.AppendLine("  ✔ Bake Tray: тесто → бисквит, Fill" + (slab ? ", Slab Prefab есть" : "  ⚠ Slab Prefab — меню 4, сохрани префабом и перетащи сюда"));
                log.AppendLine("  ℹ поставь опалубку ровно → Bake Tray ⋮ → «Запомнить позу»");
                EditorUtility.SetDirty(go);
            }
            if (Selection.activeGameObject != null) EditorSceneManager.MarkSceneDirty(Selection.activeGameObject.scene);
            log.AppendLine("\nСохрани опалубку префабом (имя латиницей, например BakeTray) и добавь в Spawnable Prefabs, если спавнишь.");
            Done("Опалубка", log);
        }

        // ───────────────────────── 3в. ПОД МАСШТАБ ─────────────────────────

        /// <summary>
        /// Опалубку растянули (Scale) — уровень теста задан в метрах и сам не растягивается.
        /// Пересчитывает его по триггеру полости «Inside» (он растянулся вместе с опалубкой).
        /// </summary>
        [MenuItem("CatCooks/Десерты/3в. Опалубка: подогнать под масштаб (выдели её)", priority = 103)]
        public static void FitTrayToScale()
        {
            var log = new StringBuilder();
            int done = 0;
            foreach (GameObject sel in Selection.gameObjects)
            {
                BakeTray tray = sel.GetComponentInParent<BakeTray>();
                if (tray == null) { log.AppendLine($"— «{sel.name}»: нет Bake Tray"); continue; }
                Cookware cw = tray.GetComponent<Cookware>();
                Collider inside = cw != null ? cw.InsideVolume : null;
                if (inside == null) { Transform it = tray.transform.Find("Inside"); inside = it != null ? it.GetComponent<Collider>() : null; }
                CookwareLiquidVisual lv = tray.GetComponentInChildren<CookwareLiquidVisual>(true);
                if (inside == null || lv == null) { log.AppendLine($"✖ «{tray.name}»: нет Inside (триггер полости) или LiquidAnchor — меню 3б"); continue; }
                Vector3 size;
                if (inside is BoxCollider bc) size = Vector3.Scale(bc.size, inside.transform.lossyScale);
                else size = inside.bounds.size;
                size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                var lso = new SerializedObject(lv);
                Undo.RecordObject(lv, "Под масштаб");
                SetV2(lso, "_halfSize", new Vector2(size.x * 0.5f, size.z * 0.5f));
                SetF(lso, "_maxHeight", Mathf.Max(0.02f, size.y * 0.55f));
                lso.ApplyModifiedProperties();
                var tso = new SerializedObject(tray);
                Undo.RecordObject(tray, "Под масштаб");
                SetF(tso, "_placeReach", Mathf.Max(3f, Mathf.Max(size.x, size.z) * 0.8f));
                tso.ApplyModifiedProperties();
                Rigidbody rb = tray.GetComponent<Rigidbody>();
                if (rb != null) { Undo.RecordObject(rb, "масса"); rb.mass = Mathf.Clamp(size.x * size.z * 6f, 2f, 80f); }
                EditorUtility.SetDirty(tray.gameObject);
                done++;
                log.AppendLine($"✔ «{tray.name}»: полость {size.x:0.00}×{size.z:0.00} м, глубина {size.y:0.00} м → уровень теста {size.x:0.00}×{size.z:0.00}, высота до {size.y * 0.55f:0.00} м; дальность постановки {Mathf.Max(3f, Mathf.Max(size.x, size.z) * 0.8f):0.0} м");
            }
            if (done > 0)
            {
                EditorSceneManager.MarkSceneDirty(Selection.activeGameObject.scene);
                log.AppendLine("\nПласт бисквита сделай заново под новый размер: «4. Пласт бисквита в сцену» (старый SpongeSlab удали/замени).\nОпалубка — префаб: Overrides → Apply All.");
            }
            else log.AppendLine("Выдели опалубку (объект с Bake Tray).");
            Done("Опалубка под масштаб", log);
        }

        // ───────────────────────── 4. ПЛАСТ ИЗ КУБИКОВ ─────────────────────────

        /// <summary>
        /// Пласт бисквита (FoodGridSlab): один бесшовный кусок, который пилой режется на равные кубики
        /// там, куда смотришь. Рядом — «SpongeCube»: объект для префаба кубика (SpongePiece).
        /// </summary>
        [MenuItem("CatCooks/Десерты/4. Пласт бисквита (режется на кубики) + кубик (выдели опалубку)", priority = 104)]
        public static void MakeSlab()
        {
            var log = new StringBuilder();
            ProductDef sponge = Find(D.Sponge);
            if (sponge == null) { Dialog("Сначала «1. Создать продукты и рецепты»."); return; }
            GameObject sel = Selection.activeGameObject;
            BakeTray tray = sel != null ? sel.GetComponentInParent<BakeTray>() : null;
            Transform fill = tray != null ? tray.transform.Find("Fill") : null;
            Vector3 size = new Vector3(1.2f, 0.14f, 0.6f);
            if (fill != null)
            {
                Vector3 ls = fill.lossyScale;
                size = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
                log.AppendLine($"Размер — по Fill опалубки «{tray.name}»: {size.x:0.00}×{size.y:0.00}×{size.z:0.00} м");
            }
            else log.AppendLine("⚠ опалубка не выделена (или нет Fill) — пласт по умолчанию 1.2×0.14×0.6 м");

            int pick = EditorUtility.DisplayDialogComplex("Пласт бисквита",
                $"Сколько кубиков поперёк пласта?\nВдоль — сколько получится квадратных.\n\nПласт {size.x:0.00}×{size.z:0.00} м",
                "3 поперёк", "2 поперёк", "4 поперёк");
            int across = pick == 0 ? 3 : pick == 2 ? 4 : 2;
            bool alongX = size.x >= size.z;
            float L = alongX ? size.x : size.z, W = alongX ? size.z : size.x, h = Mathf.Max(0.01f, size.y);
            int along = Mathf.Clamp(Mathf.RoundToInt(L / (W / across)), 2, 30);
            int nx = alongX ? along : across, nz = alongX ? across : along;
            Vector3 cell = new Vector3(size.x / nx, h, size.z / nz);

            Material mat = SpongeChocMat(log);
            FitTexScale(mat, 2.4f / Mathf.Max(0.05f, Mathf.Min(cell.x, cell.z)), log);
            Mesh slabMesh = SaveMesh(SpongeMesh.BuildSlab(null, nx, nz, cell, null), "SpongeSlab_Mesh", log);
            Mesh pieceMesh = SaveMesh(SpongeMesh.BuildPiece(null, cell, SpongeMesh.XNeg | SpongeMesh.ZNeg), "SpongeCube_Piece", log);
            Vector3 pos = (tray != null ? tray.transform.position : SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero)
                          + Vector3.up * (h + 0.6f);
            Quaternion rot = tray != null ? Quaternion.Euler(0f, tray.transform.eulerAngles.y, 0f) : Quaternion.identity;

            // ── пласт ──
            var go = new GameObject("SpongeSlab");
            Undo.RegisterCreatedObjectUndo(go, "SpongeSlab");
            go.transform.SetPositionAndRotation(pos, rot);
            go.AddComponent<MeshFilter>().sharedMesh = slabMesh;          // в игре меш строится сам
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            AddByName(go, "NetworkObject", log);
            AddByName(go, "NetworkTransform", log);
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Clamp(size.x * size.z * h * 40f, 1f, 40f);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            FoodPiece fp = go.AddComponent<FoodPiece>();
            var so = new SerializedObject(fp);
            SetObj(so, "_product", sponge);
            SetInt(so, "_startState", (int)FoodState.Baked);
            so.ApplyModifiedProperties();
            FoodGridSlab slab = go.AddComponent<FoodGridSlab>();
            slab.EditorSetup(nx, nz, cell, null);
            log.AppendLine($"＋ «SpongeSlab»: {nx}×{nz} = {nx * nz} кубиков по {cell.x:0.00}×{cell.y:0.00}×{cell.z:0.00} м — один бесшовный кусок");
            log.AppendLine("   Food Piece «Бисквит» (Запечённое), Food Grid Slab, Rigidbody, сеть. Коллайдеры клеток создаются в игре.");

            // ── кубик ──
            var piece = new GameObject("SpongeCube");
            Undo.RegisterCreatedObjectUndo(piece, "SpongeCube");
            piece.transform.position = pos + rot * Vector3.forward * (size.z * 0.5f + cell.z * 1.5f);
            piece.transform.rotation = rot;
            piece.AddComponent<MeshFilter>().sharedMesh = pieceMesh;
            piece.AddComponent<MeshRenderer>().sharedMaterial = mat;
            BoxCollider bc = piece.AddComponent<BoxCollider>();
            bc.size = cell * 0.98f;
            AddByName(piece, "NetworkObject", log);
            AddByName(piece, "NetworkTransform", log);
            Rigidbody prb = piece.AddComponent<Rigidbody>();
            prb.mass = Mathf.Clamp(rb.mass / (nx * nz), 0.2f, 5f);
            prb.interpolation = RigidbodyInterpolation.Interpolate;
            FoodPiece pfp = piece.AddComponent<FoodPiece>();
            var pso = new SerializedObject(pfp);
            SetObj(pso, "_product", sponge);
            SetInt(pso, "_startState", (int)FoodState.Pieces);
            pso.ApplyModifiedProperties();
            SpongePiece sp = piece.AddComponent<SpongePiece>();
            var spo = new SerializedObject(sp);
            SetV3(spo, "_size", cell);
            spo.ApplyModifiedProperties();
            log.AppendLine("＋ «SpongeCube»: кубик (Food Piece «Бисквит» Кусочки + Sponge Piece — корочки и размер ставит пласт; берётся мастерком)");

            SpongeFillTexture(fill, L / W, log);

            Selection.objects = new Object[] { go, piece };
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine("\nДАЛЬШЕ РУКАМИ (префабы не создаю):\n" +
                           "1. SpongeCube → перетащи в Project → префаб SpongeCube.\n" +
                           "2. SpongeSlab → Food Grid Slab → Piece Prefab = префаб SpongeCube.\n" +
                           "3. SpongeSlab → в Project → префаб SpongeSlab (старый замени).\n" +
                           "4. Оба префаба — в Spawnable Prefabs у NetworkManager.\n" +
                           "5. Опалубка → Bake Tray → Slab Prefab = SpongeSlab. Apply All.\n" +
                           "6. SpongeSlab и SpongeCube из сцены удали.\n" +
                           "Слой: если у еды свой слой (Food) — поставь его обоим (с детьми), иначе пила/мастерок не увидят.");
            Done("Пласт бисквита", log);
        }

        internal static Mesh SaveMesh(Mesh m, string name, StringBuilder log)
        {
            string path = ModelDir + "/" + name + ".asset";
            EnsureDir(ModelDir);
            m.name = name;
            m.hideFlags = HideFlags.None;
            Mesh have = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (have != null)
            {
                EditorUtility.CopySerialized(m, have);
                Object.DestroyImmediate(m);
                EditorUtility.SetDirty(have);
                log.AppendLine($"  ✔ меш {path} обновлён");
                return have;
            }
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"  ＋ меш {path}");
            return m;
        }

        private static void FitTexScale(Material m, float scale, StringBuilder log)
        {
            if (m == null || !m.HasProperty("_TexScale")) return;
            if (Mathf.Abs(m.GetFloat("_TexScale") - scale) < 0.01f) return;
            m.SetFloat("_TexScale", scale);
            EditorUtility.SetDirty(m);
            log.AppendLine($"  ✔ {m.name}: плотность пор {scale:0.0} на метр (под размер кубика)");
        }

        internal static void SetV3(SerializedObject so, string n, Vector3 v) { SerializedProperty p = so.FindProperty(n); if (p != null) p.vector3Value = v; }

        /// <summary>Размер кубика: из пласта в сцене, из префаба SpongeSlab, из опалубки — или по умолчанию.</summary>
        private static Vector3 CellSize(out string from)
        {
            FoodGridSlab s = Object.FindFirstObjectByType<FoodGridSlab>();
            if (s == null)
                foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
                {
                    GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                    if (p != null && (s = p.GetComponent<FoodGridSlab>()) != null) break;
                }
            if (s != null) { from = $"пласт «{s.name}»"; return s.CellSize; }
            BakeTray t = Object.FindFirstObjectByType<BakeTray>();
            Transform fill = t != null ? t.transform.Find("Fill") : null;
            if (fill != null)
            {
                Vector3 ls = fill.lossyScale;
                float w = Mathf.Min(Mathf.Abs(ls.x), Mathf.Abs(ls.z)) / 3f;
                from = $"опалубка «{t.name}» (3 поперёк)";
                return new Vector3(w, Mathf.Abs(ls.y), w);
            }
            from = "по умолчанию";
            return new Vector3(0.6f, 0.3f, 0.6f);
        }

        // ───────────────────────── 6. ВИТРИНА ─────────────────────────

        /// <summary>Четыре стадии: меренгу кинули, оплывает, закрывает бисквит, готовый купол подрумянен.</summary>
        [MenuItem("CatCooks/Десерты/6. Витрина Аляски (вид сборки)", priority = 106)]
        public static void MakePreview()
        {
            var log = new StringBuilder();
            Vector3 cell = CellSize(out string from);
            Material sm = SpongeChocMat(log), mm = MeringueMat(log), im = IceMat(log);
            FitTexScale(mm, 1.8f / Mathf.Max(0.05f, Mathf.Min(cell.x, cell.z) * 0.5f), log);
            Vector3 at = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            var root = new GameObject("AlaskaPreview");
            Undo.RegisterCreatedObjectUndo(root, "AlaskaPreview");
            root.transform.position = at;
            float half = Mathf.Min(cell.x, cell.z) * 0.5f;
            float iceR = half * 0.78f, iceH = iceR * 1.6f;          // шарик чуть сплюснутый (как Ice Round 0.8 у тарелки)
            string[] names = { "1_JustThrown", "2_Flowing", "3_Covering", "4_Toasted" };
            float[] spread = { 0f, 0.4f, 0.75f, 1f }, toast = { 0f, 0f, 0f, 0.45f };
            for (int i = 0; i < 4; i++)
            {
                var item = new GameObject(names[i]);
                item.transform.SetParent(root.transform, false);
                item.transform.localPosition = new Vector3((i - 1.5f) * cell.x * 2.6f, 0f, 0f);
                Part(item.transform, "Sponge", SpongeMesh.BuildPiece(null, cell, SpongeMesh.XNeg | SpongeMesh.ZPos), sm, new Vector3(0f, cell.y * 0.5f, 0f), Vector3.one);
                Part(item.transform, "IceCream", SpongeMesh.IceScoop(), im, new Vector3(0f, cell.y, 0f), new Vector3(iceR, iceH, iceR));
                var mg = new GameObject("Meringue");
                mg.transform.SetParent(item.transform, false);
                mg.AddComponent<MeshFilter>();
                mg.AddComponent<MeshRenderer>().sharedMaterial = mm;
                MeringueCap cap = mg.AddComponent<MeringueCap>();
                DessertPlate.ConfigureCap(cap, cell, true, iceR, iceH);
                cap.Spread = spread[i];
                cap.Seed = i + 3;
                cap.Toast = toast[i];
            }
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
            log.AppendLine($"Кубик {cell.x:0.00}×{cell.y:0.00}×{cell.z:0.00} м ({from}).");
            log.AppendLine("1 — меренгу только кинули на мороженое; 2 — оплывает (Spread 0.4); 3 — переваливает через бисквит (0.75); 4 — купол-улей, подрумянен.");
            log.AppendLine("\nВыдели Meringue → Meringue Cap: Spread — растекание, Toast — подпал, Turns / Ridge Depth — жгут.\nAlaskaPreview — только посмотреть, потом удали.");
            Done("Витрина Аляски", log);
        }

        private static void Part(Transform parent, string n, Mesh mesh, Material m, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject(n);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            if (n == "IceCream")
            {
                ProductDef ice = Find("Мороженое");
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", ice != null ? new Color(ice.BaseColor.r, ice.BaseColor.g, ice.BaseColor.b, 1f) : new Color(0.95f, 0.92f, 0.85f));
                r.SetPropertyBlock(mpb);
            }
        }

        // ───────────────────────── 7. ТАРЕЛКА ─────────────────────────

        [MenuItem("CatCooks/Десерты/7. Тарелка для Аляски — сделать в сцене (стиль «стройка»)", priority = 107)]
        public static void MakePlate()
        {
            var log = new StringBuilder();
            Vector3 cell = CellSize(out string from);
            float R = Mathf.Max(cell.x, cell.z) * 1.5f;
            GameObject go = PlateObject("DessertPlate", R, log);
            log.AppendLine($"＋ «DessertPlate»: тарелка Ø{R * 2f:0.00} м под кубик {cell.x:0.00}×{cell.z:0.00} ({from})");
            SetupPlate(go, R, PlateFloor * R, cell, log);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine("\nДАЛЬШЕ: перетащи DessertPlate в Project → префаб DessertPlate → в Spawnable Prefabs. Поставь пару тарелок на стол.");
            Done("Тарелка для Аляски", log);
        }

        [MenuItem("CatCooks/Десерты/7в. Тарелка «стройка» — просто тарелка (для любых блюд)", priority = 107)]
        public static void MakeUniversalPlate()
        {
            var log = new StringBuilder();
            int pick = EditorUtility.DisplayDialogComplex("Тарелка «стройка»", "Какого размера тарелку сделать?\n(Под кубик бисквита — меню 7.)", "Средняя Ø1.6 м", "Маленькая Ø1 м", "Большая Ø2.4 м");
            float R = pick == 0 ? 0.8f : pick == 2 ? 1.2f : 0.5f;
            GameObject go = PlateObject("PlateBuild", R, log);
            Rigidbody rb = Need<Rigidbody>(go);
            rb.mass = 1f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            AddByName(go, "NetworkObject", log);
            AddByName(go, "NetworkTransform", log);
            if (go.GetComponent<PickupItem>() == null) { Undo.AddComponent<PickupItem>(go); log.AppendLine("  ＋ Pickup Item — нести (F)"); }
            Transform anchor = FindOrCreate(go.transform, "FoodAnchor");
            anchor.localPosition = new Vector3(0f, PlateFloor * R, 0f);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"＋ «PlateBuild» Ø{R * 2f:0.00} м: модель, коллайдеры, Rigidbody, сеть, Pickup Item. FoodAnchor — центр дна.");
            log.AppendLine("\nПод Аляску её можно доделать: выдели → 7б. Или сохрани как есть префабом (в Spawnable Prefabs).");
            Done("Тарелка «стройка»", log);
        }

        /// <summary>Объект тарелки «стройка» в сцене: меш (ассет), материал, коллайдеры.</summary>
        private static GameObject PlateObject(string name, float R, StringBuilder log)
        {
            Vector3 at = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.position = at;
            Mesh m = SaveMesh(PlateMesh(R), $"PlateBuild_R{Mathf.RoundToInt(R * 100f)}_Mesh", log);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = PlateMat(log);
            PlateColliders(go, R, log);
            return go;
        }

        [MenuItem("CatCooks/Десерты/7б. Тарелка для Аляски — настроить свою модель (выдели)", priority = 107)]
        public static void SetupOwnPlate()
        {
            var log = new StringBuilder();
            GameObject sel = Selection.activeGameObject;
            if (sel == null) { Dialog("Выдели корень своей тарелки в сцене."); return; }
            Bounds b = LocalBounds(sel.transform);
            Vector3 ls = sel.transform.lossyScale;
            float sx = Mathf.Abs(ls.x), sz = Mathf.Abs(ls.z), sy = Mathf.Abs(ls.y);
            // размер в метрах в осях корня (корень мог быть повёрнут −90 X, как из Blender)
            Vector3 w = sel.transform.TransformVector(b.size);
            float R = Mathf.Max(Mathf.Abs(w.x), Mathf.Abs(w.z)) * 0.5f;
            Vector3 cell = CellSize(out string from);
            if (sel.GetComponent<Collider>() == null && sel.GetComponentInChildren<Collider>() == null)
            {
                MeshCollider mc = Undo.AddComponent<MeshCollider>(sel);
                mc.convex = true;
                log.AppendLine("  ＋ Mesh Collider (выпуклый)");
            }
            log.AppendLine($"Тарелка «{sel.name}»: Ø{R * 2f:0.00} м; кубик {cell.x:0.00}×{cell.z:0.00} ({from})");
            if (Mathf.Max(cell.x, cell.z) * 0.75f > R) log.AppendLine("⚠ кубик широковат для тарелки — меренга свесится за край (увеличь тарелку или режь пласт мельче)");
            // поверхность — чуть выше низа (дно тарелки)
            Vector3 lowW = sel.transform.TransformPoint(new Vector3(b.center.x, b.min.y, b.center.z));
            Vector3 hiW = sel.transform.TransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
            float lift = Mathf.Abs(hiW.y - lowW.y) * 0.3f;
            SetupPlate(sel, R, lift, cell, log, Mathf.Min(lowW.y, hiW.y));
            EditorSceneManager.MarkSceneDirty(sel.scene);
            log.AppendLine("\nDessertAnchor стоит в центре на дне — подвинь, если десерт висит или утонул. Префаб — Apply All.");
            Done("Тарелка для Аляски", log);
        }

        /// <summary>Компоненты тарелки: Rigidbody, сеть, Pickup Item, Cookware (глотает, без рецептов), Inside, Mouth, DessertAnchor, DessertPlate.</summary>
        private static void SetupPlate(GameObject go, float R, float surfaceY, Vector3 cell, StringBuilder log, float worldBottom = float.NaN)
        {
            Rigidbody rb = Need<Rigidbody>(go);
            rb.mass = Mathf.Max(rb.mass, 1f);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            AddByName(go, "NetworkObject", log);
            AddByName(go, "NetworkTransform", log);
            if (go.GetComponent<PickupItem>() == null) { Undo.AddComponent<PickupItem>(go); log.AppendLine("  ＋ Pickup Item — нести тарелку (F)"); }
            Cookware cw = Need<Cookware>(go);
            var cso = new SerializedObject(cw);
            SetObj(cso, "Def", PlateDef(log));
            SerializedProperty cr = cso.FindProperty("_cooksRecipes");
            if (cr != null) cr.boolValue = false;

            // узлы — в метрах мира, по центру тарелки
            Vector3 c = go.transform.position;
            float bottom = float.IsNaN(worldBottom) ? c.y : worldBottom;
            Transform anchor = FindOrCreate(go.transform, "DessertAnchor");
            anchor.position = new Vector3(c.x, bottom + surfaceY, c.z);
            anchor.rotation = Quaternion.Euler(0f, go.transform.eulerAngles.y, 0f);
            Transform mouth = FindOrCreate(go.transform, "Mouth");
            mouth.position = anchor.position + Vector3.up * cell.y;
            Transform inside = FindOrCreate(go.transform, "Inside");
            inside.position = anchor.position + Vector3.up * (cell.y * 0.9f);
            inside.rotation = anchor.rotation;
            BoxCollider ib = Need<BoxCollider>(inside.gameObject);
            ib.isTrigger = true;
            Vector3 ps = inside.lossyScale;
            ib.size = new Vector3(R * 1.5f / Mathf.Max(1e-4f, Mathf.Abs(ps.x)), cell.y * 1.8f / Mathf.Max(1e-4f, Mathf.Abs(ps.y)), R * 1.5f / Mathf.Max(1e-4f, Mathf.Abs(ps.z)));
            ib.center = Vector3.zero;
            SetObj(cso, "_insideVolume", ib);
            SetObj(cso, "_mouth", mouth);
            cso.ApplyModifiedProperties();
            log.AppendLine("  ＋ Cookware «Тарелка для десерта» (глотает кубик, без рецептов), Inside (триггер), Mouth, DessertAnchor");

            DessertPlate dp = Need<DessertPlate>(go);
            Material im = IceMat(log), mm = MeringueMat(log), sm = SpongeChocMat(log);
            FitTexScale(mm, 1.8f / Mathf.Max(0.05f, Mathf.Min(cell.x, cell.z) * 0.5f), log);
            dp.EditorSetup(anchor, cell, sm, im, mm);
            log.AppendLine("  ＋ Dessert Plate: бисквит → мороженое → меренга стекает куполом → горелка");
        }

        private static CookwareDef PlateDef(StringBuilder log)
        {
            string path = $"{DefDir}/Cookware_DessertPlate.asset";
            CookwareDef d = AssetDatabase.LoadAssetAtPath<CookwareDef>(path);
            if (d != null) return d;
            EnsureDir(DefDir);
            d = ScriptableObject.CreateInstance<CookwareDef>();
            d.Title = "Тарелка для десерта"; d.Kind = CookwareKind.Storage; d.Capacity = 10;
            d.HeatProcess = FoodProcess.None; d.NeedsHeatSource = true; d.Absorbs = true; d.BlendsColor = false;
            AssetDatabase.CreateAsset(d, path);
            log.AppendLine($"  ＋ тара {path}: «Тарелка для десерта», глотает кусочки");
            return d;
        }

        private static Material IceMat(StringBuilder log)
        {
            return FoodHeapMat("Mat_IceCream", MeringueFoamTexture(log), 5f, 0.7f, 0.94f, 0.3f, 0.005f, log);
        }

        internal static Material PlateMat(StringBuilder log)
        {
            string path = ModelDir + "/Mat_PlateBuild.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            EnsureDir(ModelDir);
            Shader sh = Shader.Find("CatCooks/Food Heap");
            if (sh == null) { sh = Shader.Find("Universal Render Pipeline/Lit"); log.AppendLine("  ⚠ нет шейдера CatCooks/Food Heap — тарелка на URP/Lit, полосы (цвет вершин) не видны"); }
            m = new Material(sh) { name = "Mat_PlateBuild" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_GlintStrength")) { m.SetFloat("_GlintStrength", 0.6f); m.SetFloat("_GlintSize", 0.95f); }
            if (m.HasProperty("_TexDetail")) m.SetFloat("_TexDetail", 0f);
            if (m.HasProperty("_Saturation")) m.SetFloat("_Saturation", 1.05f);
            if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", 0.006f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.7f);
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"  ＋ материал {path} (мультяшный свет, цвета — в вершинах)");
            return m;
        }

        // ── тарелка «стройка» ──
        // профиль (r, y — доли радиуса, зона): 0 — кремовое дно, 1 — жёлто-чёрная лента, 2 — стальной борт, 3 — низ
        private static readonly Vector3[] PlateProf =
        {
            new Vector3(0f, 0.034f, 0), new Vector3(0.56f, 0.034f, 0), new Vector3(0.63f, 0.044f, 0), new Vector3(0.69f, 0.07f, 0),
            new Vector3(0.72f, 0.088f, 1), new Vector3(0.755f, 0.096f, 1), new Vector3(0.79f, 0.1f, 1), new Vector3(0.825f, 0.1f, 1),
            new Vector3(0.86f, 0.098f, 1), new Vector3(0.885f, 0.094f, 1), new Vector3(0.9f, 0.096f, 2), new Vector3(0.93f, 0.112f, 2),
            new Vector3(0.97f, 0.118f, 2), new Vector3(0.995f, 0.108f, 2), new Vector3(1f, 0.085f, 2), new Vector3(0.99f, 0.06f, 2),
            new Vector3(0.96f, 0.048f, 3), new Vector3(0.75f, 0.03f, 3), new Vector3(0.62f, 0.016f, 3), new Vector3(0.6f, 0f, 3),
            new Vector3(0.5f, 0f, 3), new Vector3(0.48f, 0.012f, 3), new Vector3(0f, 0.012f, 3),
        };
        /// <summary>Высота дна тарелки (где стоит десерт), доля радиуса.</summary>
        private const float PlateFloor = 0.034f;

        private static readonly Color PlCream = new Color(0.96f, 0.93f, 0.86f, 0f), PlYellow = new Color(0.99f, 0.76f, 0.12f, 0f),
                                      PlDark = new Color(0.16f, 0.15f, 0.17f, 0f), PlSteel = new Color(0.58f, 0.61f, 0.65f, 0f),
                                      PlUnder = new Color(0.36f, 0.38f, 0.42f, 0f), PlBolt = new Color(0.44f, 0.46f, 0.5f, 0f),
                                      PlBoltTop = new Color(0.66f, 0.68f, 0.72f, 0f);

        /// <summary>
        /// Мультяшная тарелка «стройка»: кремовое дно, широкий борт с жёлто-чёрной сигнальной лентой
        /// (косые полосы), стальной край-«шестерня» с 8 болтами, тёмный низ с ножкой.
        /// Каждая плитка — свой цвет (полосы чёткие), нормали — гладкие по профилю.
        /// </summary>
        private static Mesh PlateMesh(float R)
        {
            const int segs = 288;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            int rows = PlateProf.Length;
            // гладкие нормали профиля (в плоскости r-y)
            var pn = new Vector2[rows];
            for (int i = 0; i < rows; i++)
            {
                Vector2 p0 = Prof2(Mathf.Max(0, i - 1)), p1 = Prof2(Mathf.Min(rows - 1, i + 1));
                Vector2 tg = (p1 - p0).normalized;
                pn[i] = new Vector2(-tg.y, tg.x);          // верх (наружу по r) → вверх
            }
            for (int j = 0; j < segs; j++)
            {
                float t0 = j / (float)segs * Mathf.PI * 2f, t1 = (j + 1) / (float)segs * Mathf.PI * 2f, tc = (t0 + t1) * 0.5f;
                for (int i = 0; i < rows - 1; i++)
                {
                    Vector3 A = PlateProf[i], B = PlateProf[i + 1];
                    int zone = (int)(A.z != B.z && B.z != 3 ? B.z : A.z);
                    float rc = (A.x + B.x) * 0.5f;
                    Color col;
                    if (zone == 0) col = PlCream;
                    else if (zone == 1) col = Mathf.Repeat(tc / (Mathf.PI * 2f) * 18f + rc * 2.2f, 1f) < 0.5f ? PlYellow : PlDark;
                    else if (zone == 2) col = PlSteel;
                    else col = PlUnder;
                    int b0 = v.Count;
                    // a=(i,t0) a2=(i,t1) b=(i+1,t0) b2=(i+1,t1); Cross(a2−a, b−a) = вверх у верха
                    AddPlateV(v, n, c, A, pn[i], t0, R, col);
                    AddPlateV(v, n, c, A, pn[i], t1, R, col);
                    AddPlateV(v, n, c, B, pn[i + 1], t0, R, col);
                    AddPlateV(v, n, c, B, pn[i + 1], t1, R, col);
                    t.Add(b0); t.Add(b0 + 1); t.Add(b0 + 2);
                    t.Add(b0 + 2); t.Add(b0 + 1); t.Add(b0 + 3);
                }
            }
            // болты на стальном борту
            for (int k = 0; k < 8; k++)
            {
                float th = (k + 0.5f) / 8f * Mathf.PI * 2f;
                float g = Gear(th, 0.95f);
                Vector3 ctr = new Vector3(0.95f * g * R * Mathf.Cos(th), 0.112f * R, 0.95f * g * R * Mathf.Sin(th));
                Bolt(v, n, c, t, ctr, 0.032f * R, 0.026f * R, th);
            }
            var m = new Mesh { name = "PlateBuild_Mesh" };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        private static Vector2 Prof2(int i) => new Vector2(PlateProf[i].x, PlateProf[i].y);

        /// <summary>Край-«шестерня»: стальной борт чуть волной (10 зубцов).</summary>
        private static float Gear(float th, float r) => r >= 0.9f ? 1f + 0.012f * Mathf.Cos(10f * th) : 1f;

        private static void AddPlateV(List<Vector3> v, List<Vector3> n, List<Color> c, Vector3 p, Vector2 nrm, float th, float R, Color col)
        {
            float g = Gear(th, p.x);
            float cs = Mathf.Cos(th), sn = Mathf.Sin(th);
            v.Add(new Vector3(p.x * g * R * cs, p.y * R, p.x * g * R * sn));
            n.Add(new Vector3(nrm.x * cs, nrm.y, nrm.x * sn).normalized);
            c.Add(col);
        }

        /// <summary>Болт: шестигранник с фаской и плоской шляпкой.</summary>
        private static void Bolt(List<Vector3> v, List<Vector3> n, List<Color> c, List<int> t, Vector3 ctr, float r, float h, float rot)
        {
            var bot = new Vector3[6]; var mid = new Vector3[6]; var top = new Vector3[6];
            for (int k = 0; k < 6; k++)
            {
                float a = k / 6f * Mathf.PI * 2f + rot;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                bot[k] = ctr + d * r;
                mid[k] = ctr + d * r + Vector3.up * h;
                top[k] = ctr + d * (r * 0.78f) + Vector3.up * (h * 1.3f);
            }
            for (int k = 0; k < 6; k++)
            {
                int k2 = (k + 1) % 6;
                Vector3 out1 = ((bot[k] + bot[k2]) * 0.5f - ctr); out1.y = 0f; out1.Normalize();
                // бок: p00=низ k, p10=верх k (вверх), p01=низ k+1 (по кругу) — Cross(вверх, по кругу) = наружу
                PQuad(v, n, c, t, bot[k], mid[k], mid[k2], bot[k2], out1, PlBolt);
                Vector3 nf = (out1 + Vector3.up * 1.2f).normalized;
                PQuad(v, n, c, t, mid[k], top[k], top[k2], mid[k2], nf, PlBolt);
                // шляпка: (центр, k+1, k) — нормаль вверх
                int b0 = v.Count;
                Vector3 cTop = ctr + Vector3.up * (h * 1.3f);
                v.Add(cTop); v.Add(top[k2]); v.Add(top[k]);
                for (int q = 0; q < 3; q++) { n.Add(Vector3.up); c.Add(PlBoltTop); }
                t.Add(b0); t.Add(b0 + 1); t.Add(b0 + 2);
            }
        }

        /// <summary>Плитка: p00→p10 — ось U, p00→p01 — ось V, Cross(U,V) = наружу.</summary>
        private static void PQuad(List<Vector3> v, List<Vector3> n, List<Color> c, List<int> t, Vector3 p00, Vector3 p10, Vector3 p11, Vector3 p01, Vector3 nrm, Color col)
        {
            int b = v.Count;
            v.Add(p00); v.Add(p10); v.Add(p11); v.Add(p01);
            for (int q = 0; q < 4; q++) { n.Add(nrm); c.Add(col); }
            t.Add(b); t.Add(b + 1); t.Add(b + 3);
            t.Add(b + 1); t.Add(b + 2); t.Add(b + 3);
        }

        /// <summary>Коллайдеры тарелки: плоское дно + 12 брусков по борту (Rigidbody не любит невыпуклые меши).</summary>
        private static void PlateColliders(GameObject go, float R, StringBuilder log)
        {
            Transform h = FindOrCreate(go.transform, "PlateColliders");
            h.localPosition = Vector3.zero; h.localRotation = Quaternion.identity; h.localScale = Vector3.one;
            Transform bt = FindOrCreate(h, "Base");
            bt.localPosition = new Vector3(0f, PlateFloor * R * 0.5f, 0f);
            bt.localRotation = Quaternion.identity;
            Need<BoxCollider>(bt.gameObject).size = new Vector3(R * 1.36f, PlateFloor * R, R * 1.36f);
            for (int k = 0; k < 12; k++)
            {
                float a = k / 12f * 360f;
                Transform rim = FindOrCreate(h, "Rim_" + k);
                rim.localRotation = Quaternion.Euler(0f, -a, 0f);
                rim.localPosition = rim.localRotation * new Vector3(0.84f * R, 0.06f * R, 0f);
                Need<BoxCollider>(rim.gameObject).size = new Vector3(0.3f * R, 0.12f * R, 2f * Mathf.PI * 0.84f * R / 12f * 1.08f);
            }
            log.AppendLine("  ＋ PlateColliders — дно и 12 брусков по борту");
        }

        private static Material SpongeChocMat(StringBuilder log)
        {
            return FoodHeapMat("Mat_SpongeChoc", SpongeCrumbTexture(log), 2.2f, 0.06f, 0.97f, 0.12f, 0.008f, log);
        }

        private static Material MeringueMat(StringBuilder log)
        {
            return FoodHeapMat("Mat_Meringue", MeringueFoamTexture(log), 7f, 0.55f, 0.95f, 0.35f, 0.006f, log);
        }

        /// <summary>Материал на шейдере проекта «CatCooks/Food Heap» (цвет из вершин, трипланар-узор, контур).
        /// Уже есть — не трогаю (твои правки остаются).</summary>
        internal static Material FoodHeapMat(string name, Texture2D tex, float texScale, float glint, float glintSize, float rim, float outline, StringBuilder log)
        {
            string path = ModelDir + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            EnsureDir(ModelDir);
            Shader sh = Shader.Find("CatCooks/Food Heap");
            bool heap = sh != null;
            if (!heap) { sh = Shader.Find("Universal Render Pipeline/Lit"); log.AppendLine($"  ⚠ нет шейдера CatCooks/Food Heap — {name} на URP/Lit (цвет из вершин не виден)"); }
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh) { name = name };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (heap)
            {
                m.SetFloat("_TexScale", texScale);
                m.SetFloat("_TexDetail", 1f);
                m.SetFloat("_GlintStrength", glint);
                m.SetFloat("_GlintSize", glintSize);
                m.SetFloat("_Rim", rim);
                m.SetFloat("_OutlineWidth", outline);
                m.SetFloat("_UseMeshUV", 0f);
            }
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"  ＋ материал {path}");
            return m;
        }

        /// <summary>Мякиш шоколадного бисквита 256×256: крупные и мелкие поры, бесшовно. Серая — цвет даёт меш.</summary>
        private static Texture2D SpongeCrumbTexture(StringBuilder log)
        {
            return PoreTexture("Tex_SpongeCrumb", 0.86f, 0.14f, 14, 0.42f, 0.5f, 34, 0.38f, 0.65f, 11, log);
        }

        /// <summary>Пена меренги: почти белая, мелкие пузырьки и мягкие разводы.</summary>
        internal static Texture2D MeringueFoamTexture(StringBuilder log)
        {
            return PoreTexture("Tex_MeringueFoam", 0.95f, 0.06f, 22, 0.3f, 0.86f, 48, 0.3f, 0.92f, 23, log);
        }

        /// <param name="baseV">светлота основы</param><param name="noiseV">разброс основы</param>
        /// <param name="cellsA">крупные поры: ячеек на сторону</param><param name="rA">радиус, доля ячейки</param><param name="darkA">дно поры</param>
        private static Texture2D PoreTexture(string name, float baseV, float noiseV, int cellsA, float rA, float darkA, int cellsB, float rB, float darkB, int seed, StringBuilder log)
        {
            string path = ModelDir + "/" + name + ".png";
            Texture2D have = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (have != null) return have;
            const int N = 256;
            var px = new Color32[N * N];
            float[] layerA = PoreLayer(N, cellsA, rA, darkA, seed);
            float[] layerB = PoreLayer(N, cellsB, rB, darkB, seed * 7 + 3);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, w = y / (float)N;
                    float g = baseV + (TileN(u, w, 2f, 6f) - 0.5f) * 2f * noiseV;
                    g *= layerA[y * N + x] * layerB[y * N + x];
                    byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(g) * 255f);
                    px[y * N + x] = new Color32(b, b, b, 255);
                }
            var tex = new Texture2D(N, N, TextureFormat.RGB24, true);
            tex.SetPixels32(px); tex.Apply();
            EnsureDir(ModelDir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            log.AppendLine($"  ＋ текстура {path}");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Слой пор (множитель яркости): в каждой ячейке пора со случайным центром и размером,
        /// тёмное дно, светлый край. Соседи берутся по кругу — бесшовно.</summary>
        private static float[] PoreLayer(int N, int cells, float rK, float dark, int seed)
        {
            var rnd = new System.Random(seed);
            var cx = new float[cells * cells]; var cy = new float[cells * cells]; var cr = new float[cells * cells];
            for (int i = 0; i < cells * cells; i++)
            {
                cx[i] = (float)rnd.NextDouble(); cy[i] = (float)rnd.NextDouble();
                cr[i] = rK * (0.45f + (float)rnd.NextDouble() * 0.75f);
            }
            var res = new float[N * N];
            float cell = N / (float)cells;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = x / cell, fy = y / cell;
                    int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
                    float k = 1f, rim = 1f;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int gx = ix + dx, gy = iy + dy;
                            int wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells;
                            int id = wy * cells + wx;
                            float px = gx + cx[id], py = gy + cy[id];
                            float d = Mathf.Sqrt((fx - px) * (fx - px) + (fy - py) * (fy - py)) / cr[id];
                            if (d < 1f) k = Mathf.Min(k, Mathf.Lerp(dark, 1f, d * d * d));
                            else if (d < 1.25f) rim = Mathf.Max(rim, 1f + 0.06f * (1.25f - d) * 4f);   // светлый бортик поры
                        }
                    res[y * N + x] = k < 0.999f ? k : rim;
                }
            return res;
        }

        /// <summary>Fill в опалубке (куб на Mat_Sponge, URP Lit) — та же текстура мякиша, плитка по форме пласта.</summary>
        private static void SpongeFillTexture(Transform fill, float aspect, StringBuilder log)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(ModelDir + "/Mat_Sponge.mat");
            if (m == null || !m.HasProperty("_BaseMap")) return;
            if (m.GetTexture("_BaseMap") != null) return;
            m.SetTexture("_BaseMap", SpongeCrumbTexture(log));
            m.SetTextureScale("_BaseMap", new Vector2(Mathf.Clamp(aspect, 1f, 8f) * 4f, 4f));
            EditorUtility.SetDirty(m);
            log.AppendLine("  ✔ Mat_Sponge (пласт в опалубке) — текстура мякиша");
        }

        // ───────────────────────── 5. ПРОВЕРКА ─────────────────────────

        [MenuItem("CatCooks/Десерты/5. Проверить десерты", priority = 105)]
        public static void Check()
        {
            var sb = new StringBuilder();
            _sb = sb; _err = 0; _warn = 0;
            sb.AppendLine("■ Продукты");
            foreach (string t in new[] { D.Egg, D.White, D.Sugar, D.Flour, D.Choc, D.Butter, D.EggFoam, D.ChocMass, D.Batter, D.Sponge, D.Meringue, D.MeringueOver })
            {
                ProductDef p = Find(t);
                if (p == null) Er($"нет «{t}»"); else Ok($"«{t}» Id {p.Id}");
            }
            sb.AppendLine("\n■ Рецепты (ассеты; нет — книга соберёт сама по названиям)");
            foreach (string t in new[] { "Яичная масса", "Шоколадная масса", "Бисквитное тесто", "Меренга", "Меренга перебитая" })
            {
                bool found = false;
                foreach (string g in AssetDatabase.FindAssets("t:CookRecipeDef"))
                {
                    var r = AssetDatabase.LoadAssetAtPath<CookRecipeDef>(AssetDatabase.GUIDToAssetPath(g));
                    if (r != null && r.Title == t) found = true;
                }
                if (found) Ok($"рецепт «{t}»"); else Wr($"нет ассета «{t}» — меню 1");
            }

            sb.AppendLine("\n■ Сцена");
            int mixers = 0;
            foreach (MixerItem m in Object.FindObjectsByType<MixerItem>(FindObjectsSortMode.None))
            {
                mixers++;
                var so = new SerializedObject(m);
                SerializedProperty k = so.FindProperty("_kitchenMode");
                if (k != null && !k.boolValue) Wr($"миксер «{m.name}»: Kitchen Mode выключен — взбивать не будет");
                else Ok($"миксер «{m.name}» — взбивает и мешает в таре");
            }
            if (mixers == 0) Wr("в сцене нет миксера (Mixer Item) — взбивать нечем");

            int roles = 0;
            ProductDef batter = Find(D.Batter), mer = Find(D.Meringue);
            foreach (CookwareLiquidVisual lv in Object.FindObjectsByType<CookwareLiquidVisual>(FindObjectsSortMode.None))
            {
                if (batter == null || !lv.TryGetRole(batter.Id, out _)) continue;
                roles++;
                Ok($"тара «{lv.transform.root.name}»: новые массы видны" + (mer != null && lv.TryGetRole(mer.Id, out _) ? "" : " (кроме меренги — меню 2)"));
            }
            if (roles == 0) Wr("ни одна тара не знает новых масс — выдели ведро/кастрюлю/опалубку → меню 2 (без этого массы не видно и тесто не льётся)");

            int trays = 0;
            foreach (BakeTray t in Object.FindObjectsByType<BakeTray>(FindObjectsSortMode.None))
            {
                trays++;
                int e0 = _err;
                var so = new SerializedObject(t);
                if (so.FindProperty("_batter").objectReferenceValue == null) Er($"опалубка «{t.name}»: не задан Batter");
                if (so.FindProperty("_result").objectReferenceValue == null) Er($"опалубка «{t.name}»: не задан Result");
                var slab = so.FindProperty("_slabPrefab").objectReferenceValue as GameObject;
                if (slab == null) Wr($"опалубка «{t.name}»: нет Slab Prefab — испечётся, но не достать (меню 4)");
                else
                {
                    if (slab.GetComponent<FoodPiece>() == null) Er($"Slab Prefab «{slab.name}»: нет Food Piece");
                    if (slab.GetComponent<FishNet.Object.NetworkObject>() == null) Er($"Slab Prefab «{slab.name}»: нет Network Object");
                    FoodGridSlab gs = slab.GetComponent<FoodGridSlab>();
                    if (gs == null) Wr($"Slab Prefab «{slab.name}»: старый пласт (без Food Grid Slab) — меню 4 заново, чтобы резался на кубики бесшовно");
                    else
                    {
                        var gso = new SerializedObject(gs);
                        var pp = gso.FindProperty("_piecePrefab").objectReferenceValue as GameObject;
                        if (pp == null) Er($"Slab Prefab «{slab.name}»: Food Grid Slab → Piece Prefab пуст (= префаб SpongeCube)");
                        else if (pp.GetComponent<FoodPiece>() == null || pp.GetComponent<FishNet.Object.NetworkObject>() == null) Er($"кубик «{pp.name}»: нужен Food Piece и Network Object");
                        else
                        {
                            if (pp.GetComponent<SpongePiece>() == null) Wr($"кубик «{pp.name}»: нет Sponge Piece — мастерком не взять, корочки не те");
                            Ok($"пласт «{slab.name}»: {gs.CountX}×{gs.CountZ} = {gs.CellCount} кубиков → «{pp.name}» (не забудь его в Spawnable Prefabs)");
                        }
                    }
                }
                Cookware cw = t.GetComponent<Cookware>();
                if (cw == null) Er($"опалубка «{t.name}»: нет Cookware — тесто не налить");
                else if (cw.Def == null) Er($"опалубка «{t.name}»: у Cookware нет Def");
                if (t.GetComponent<CookwareHeat>() != null) Wr($"опалубка «{t.name}»: убери Cookware Heat");
                if (t.GetComponent<CarriedContainer>() != null) Er($"опалубка «{t.name}»: убери Carried Container (Bake Tray сам предмет)");
                if (so.FindProperty("_fill").objectReferenceValue == null && t.transform.Find("Fill") == null) Wr($"опалубка «{t.name}»: нет Fill — пласт не видно");
                if (_err == e0) Ok($"опалубка «{t.name}»");
            }
            if (trays == 0) Wr("в сцене нет опалубки (Bake Tray) — меню 3 (сделать) или 3б (своя модель)");
            int ovens = Object.FindObjectsByType<BakeOven>(FindObjectsSortMode.None).Length;
            if (ovens == 0) Wr("в сцене нет духовки (Bake Oven)"); else Ok($"духовок: {ovens}");

            sb.AppendLine("\n■ Сборка Аляски");
            ProductDef ice = Find("Мороженое");
            if (ice == null) Er("нет продукта «Мороженое»"); else Ok("«Мороженое» есть");
            int plates = 0;
            foreach (DessertPlate dp in Object.FindObjectsByType<DessertPlate>(FindObjectsSortMode.None))
            {
                plates++;
                int e0 = _err;
                Cookware pc = dp.GetComponent<Cookware>();
                if (pc == null || pc.Def == null) Er($"тарелка «{dp.name}»: нет Cookware/Def");
                else if (!pc.Def.Absorbs) Wr($"тарелка «{dp.name}»: Def не глотает — кубик руками не положить (мастерком можно)");
                if (pc != null && pc.InsideVolume == null) Wr($"тарелка «{dp.name}»: нет Inside — меню 7б");
                if (dp.GetComponent<PickupItem>() == null) Wr($"тарелка «{dp.name}»: нет Pickup Item — не унести");
                var dso = new SerializedObject(dp);
                if (dso.FindProperty("_meringueMat").objectReferenceValue == null) Wr($"тарелка «{dp.name}»: нет материала меренги — меню 7б");
                if (_err == e0) Ok($"тарелка «{dp.name}»");
            }
            if (plates == 0) Wr("в сцене нет тарелки для Аляски — меню 7 (сделать) или 7б (своя модель)");
            int scoops = 0;
            foreach (CatCooks.Food.Tools.ScoopItem s in Object.FindObjectsByType<CatCooks.Food.Tools.ScoopItem>(FindObjectsSortMode.None)) scoops++;
            if (scoops == 0) Wr("в сцене нет мастерка/лопаты (Scoop Item) — нечем класть мороженое и меренгу"); else Ok($"мастерков/лопат: {scoops}");

            int err = _err, warn = _warn;
            string head = err > 0 ? $"ОШИБОК {err}, предупреждений {warn}" : warn > 0 ? $"ошибок нет, предупреждений {warn}" : "всё в порядке";
            Debug.Log("[Десерты — проверка] " + head + "\n" + sb);
            EditorUtility.DisplayDialog("Десерты — проверка", head + "\n" + sb, "Ок");
        }

        // ───────────────────────── помощники ─────────────────────────

        private static StringBuilder _sb;
        private static int _err, _warn;
        private static void Ok(string t) => _sb.AppendLine("  ✔ " + t);
        private static void Wr(string t) { _warn++; _sb.AppendLine("  ⚠ " + t); }
        private static void Er(string t) { _err++; _sb.AppendLine("  ✖ " + t); }

        private static void Dialog(string t) => EditorUtility.DisplayDialog("Десерты", t, "Ок");

        /// <summary>true — Done только пишет в консоль, без окна (когда меню зовёт другое меню).</summary>
        internal static bool Quiet;

        internal static void Done(string title, StringBuilder log)
        {
            Debug.Log($"[{title}]\n" + log);
            if (Quiet) return;
            EditorUtility.DisplayDialog(title, log.ToString(), "Ок");
        }

        private static void TrayBoxes(Transform root, Bounds b, StringBuilder log)
        {
            Transform h = FindOrCreate(root, "TrayColliders");
            h.localPosition = Vector3.zero; h.localRotation = Quaternion.identity; h.localScale = Vector3.one;
            float t = Mathf.Max(0.005f, Mathf.Min(b.size.x, b.size.z) * 0.04f);
            Box(h, "Floor", new Vector3(b.center.x, b.min.y + t * 0.5f, b.center.z), new Vector3(b.size.x, t, b.size.z));
            Box(h, "WallL", new Vector3(b.min.x + t * 0.5f, b.center.y, b.center.z), new Vector3(t, b.size.y, b.size.z));
            Box(h, "WallR", new Vector3(b.max.x - t * 0.5f, b.center.y, b.center.z), new Vector3(t, b.size.y, b.size.z));
            Box(h, "WallB", new Vector3(b.center.x, b.center.y, b.min.z + t * 0.5f), new Vector3(b.size.x, b.size.y, t));
            Box(h, "WallF", new Vector3(b.center.x, b.center.y, b.max.z - t * 0.5f), new Vector3(b.size.x, b.size.y, t));
            log.AppendLine("  ＋ TrayColliders — дно и 4 бортика (Box Collider)");
        }

        private static void Box(Transform h, string n, Vector3 c, Vector3 s)
        {
            Transform x = FindOrCreate(h, n);
            x.localPosition = c; x.localRotation = Quaternion.identity; x.localScale = Vector3.one;
            Need<BoxCollider>(x.gameObject).size = s;
        }

        private static Material SpongeMat(StringBuilder log)
        {
            string path = ModelDir + "/Mat_Sponge.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            EnsureDir(ModelDir);
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh) { name = "Mat_Sponge" };
            Color c = new Color(0.30f, 0.18f, 0.11f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"  ＋ материал {path} (шоколадный бисквит)");
            return m;
        }

        private static CookwareDef EnsureDef(StringBuilder log)
        {
            string path = $"{DefDir}/Cookware_Formwork.asset";
            CookwareDef d = AssetDatabase.LoadAssetAtPath<CookwareDef>(path);
            if (d != null) return d;
            EnsureDir(DefDir);
            d = ScriptableObject.CreateInstance<CookwareDef>();
            d.Title = "Опалубка"; d.Kind = CookwareKind.Storage; d.Capacity = FoodUnits.Bucket;
            d.HeatProcess = FoodProcess.None; d.NeedsHeatSource = true; d.Absorbs = false; d.BlendsColor = true;
            AssetDatabase.CreateAsset(d, path);
            log.AppendLine($"  ＋ тара {path}: «Опалубка», 1 ведро, ничего не глотает");
            return d;
        }

        internal static void SaveRecipe(string title, Func<CookRecipeDef, CookRecipeDef> fill, StringBuilder log)
        {
            string path = $"{RecipeDir}/Recipe_{title}.asset";
            CookRecipeDef r = AssetDatabase.LoadAssetAtPath<CookRecipeDef>(path);
            bool isNew = r == null;
            if (isNew) r = ScriptableObject.CreateInstance<CookRecipeDef>();
            fill(r);
            if (isNew) AssetDatabase.CreateAsset(r, path); else EditorUtility.SetDirty(r);
            log.AppendLine($"✔ рецепт «{title}»");
        }

        internal static ProductDef Ensure(string title, FoodGroupDef group, Color color, float size, bool sub, string dir, ref ushort next, StringBuilder log)
        {
            ProductDef p = Find(title);
            if (p != null) { log.AppendLine($"✔ «{title}» уже есть (Id {p.Id})"); return p; }
            if (group == null) { log.AppendLine($"✖ «{title}»: не нашёл группу"); return null; }
            p = ScriptableObject.CreateInstance<ProductDef>();
            p.Id = next++;
            p.Title = title; p.Group = group; p.BaseColor = color; p.Size = size; p.MadeByRecipe = sub;
            string file = "Food_" + title.Replace(" ", "_") + ".asset";
            AssetDatabase.CreateAsset(p, $"{dir}/{file}");
            log.AppendLine($"＋ «{title}» (Id {p.Id})");
            return p;
        }

        internal static void Rule(ProductDef p, FoodState from, FoodProcess proc, FoodState to, float sec, StringBuilder log)
        {
            for (int i = 0; i < p.Overrides.Count; i++)
            {
                FoodRule r = p.Overrides[i];
                if (r.From != from || r.Process != proc) continue;
                if (r.To == to) return;
                r.To = to; r.Seconds = sec; p.Overrides[i] = r; EditorUtility.SetDirty(p);
                return;
            }
            p.Overrides.Add(new FoodRule { From = from, Process = proc, To = to, Seconds = sec });
            EditorUtility.SetDirty(p);
            log.AppendLine($"＋ правило «{p.Title}»: {FoodStateInfo.NameOf(from)} → {FoodStateInfo.NameOf(proc)} → {FoodStateInfo.NameOf(to)}");
        }

        internal static void AddEntry(SerializedProperty arr, ProductDef p, int role, Color color, string what, StringBuilder log)
        {
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("Product").objectReferenceValue == p)
                { log.AppendLine($"  ✔ «{p.Title}» уже есть"); return; }
            arr.arraySize++;
            SerializedProperty e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
            e.FindPropertyRelative("Product").objectReferenceValue = p;
            e.FindPropertyRelative("Role").intValue = role;
            e.FindPropertyRelative("Color").colorValue = color;
            log.AppendLine($"  ＋ «{p.Title}» — {what}");
        }

        internal static void SetObj(SerializedObject so, string n, Object v) { SerializedProperty p = so.FindProperty(n); if (p != null) p.objectReferenceValue = v; }
        internal static void SetInt(SerializedObject so, string n, int v) { SerializedProperty p = so.FindProperty(n); if (p != null) p.intValue = v; }
        internal static void SetF(SerializedObject so, string n, float v) { SerializedProperty p = so.FindProperty(n); if (p != null) p.floatValue = v; }
        internal static void SetV2(SerializedObject so, string n, Vector2 v) { SerializedProperty p = so.FindProperty(n); if (p != null) p.vector2Value = v; }

        internal static T Need<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            if (c == null) c = Undo.AddComponent<T>(go);
            return c;
        }

        internal static Transform FindOrCreate(Transform parent, string name)
        {
            Transform t = parent.Find(name);
            if (t != null) return t;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>Габарит мешей в осях корня (без Fill, LiquidAnchor и служебного).</summary>
        internal static Bounds LocalBounds(Transform root)
        {
            bool any = false;
            Bounds res = new Bounds(Vector3.zero, Vector3.zero);
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                bool skip = false;
                for (Transform p = mf.transform; p != null && p != root; p = p.parent)
                    if (p.name == "Fill" || p.name == "LiquidAnchor") { skip = true; break; }
                if (skip) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z);
                    Vector3 p = root.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!any) { res = new Bounds(p, Vector3.zero); any = true; } else res.Encapsulate(p);
                }
            }
            return res;
        }

        /// <summary>Добавить компонент по имени типа (FishNet NetworkObject / NetworkTransform), если его нет.</summary>
        internal static void AddByName(GameObject go, string typeName, StringBuilder log)
        {
            foreach (Component c in go.GetComponents<Component>()) if (c != null && c.GetType().Name == typeName) return;
            Type t = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
                foreach (Type x in types)
                    if (x != null && x.Name == typeName && typeof(Component).IsAssignableFrom(x) && !x.IsAbstract && x.Namespace != null && x.Namespace.StartsWith("FishNet")) { t = x; break; }
                if (t != null) break;
            }
            if (t == null) { log.AppendLine($"  ⚠ не нашёл тип {typeName} — добавь руками на «{go.name}»"); return; }
            Undo.AddComponent(go, t);
            log.AppendLine($"  ＋ {typeName}");
        }

        internal static ProductDef Find(string title)
        {
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef d = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (d != null && d.Title == title) return d;
            }
            return null;
        }

        internal static ushort MaxId()
        {
            ushort m = 0;
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef d = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (d != null && d.Id > m) m = d.Id;
            }
            return m;
        }

        internal static string ProductsDir()
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

        internal static FoodGroupDef FindGroup(string name)
        {
            foreach (string g in AssetDatabase.FindAssets("t:FoodGroupDef " + name))
            {
                FoodGroupDef d = AssetDatabase.LoadAssetAtPath<FoodGroupDef>(AssetDatabase.GUIDToAssetPath(g));
                if (d != null && d.name == name) return d;
            }
            return null;
        }

        internal static FoodGroupDef GroupOf(string productTitle)
        {
            ProductDef p = Find(productTitle);
            return p != null ? p.Group : null;
        }

        internal static void EnsureDir(string dir)
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
