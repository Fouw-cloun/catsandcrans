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
    /// БРИСКЕТ ГОРЯЧЕГО КОПЧЕНИЯ — меню «CatCooks → Брискет».
    ///   1. Продукты (Брискет, Руб для брискета, Горчица, Брискет горячего копчения) и рецепт руба.
    ///   2. Брискет в сцену — кусок (8 ломтиков) и один ломтик (Ribs Rack с галкой Brisket).
    ///   3. Рулон пенофола (выдели свою модель или сделаю).
    ///   4. Бутылка горчицы (обновит и остальные бутылки барбекю на месте).
    ///   5. Связать: брискет → ломтик (пила), говяжья туша → брискет.
    ///   6. Проверка.
    /// Префабы не создаёт — объекты в сцене и ассеты.
    /// </summary>
    public static class CatCooksBrisketSetup
    {
        private const string Root = "CatCooks/Брискет/";
        private static readonly Vector3 Dims0 = new Vector3(0.05f, 0.26f, 0.075f);   // ломтик, ширина куска, толщина — настоящий размер
        private static Vector3 Dims => Dims0 * MeatCutSettings.WorldScale;              // × масштаб мяса (MeatCutSettings → Scale)
        private const int Slices = 8;

        // ───────────────────────── 1 ─────────────────────────

        [MenuItem(Root + "1. Продукты и рецепт «Руб для брискета»", priority = 160)]
        public static void MakeProducts()
        {
            var log = new StringBuilder();
            string dir = DS.ProductsDir();
            ushort next = (ushort)(DS.MaxId() + 1);
            FoodGroupDef sub = DS.FindGroup("Group_SubRecipe") ?? DS.GroupOf("Сухой руб") ?? DS.GroupOf("Меренга");
            FoodGroupDef beef = DS.GroupOf("Говядина") ?? DS.GroupOf("Брискет") ?? sub;
            FoodGroupDef liquid = DS.GroupOf("Вода") ?? DS.GroupOf("Яблочный уксус") ?? sub;
            foreach (string need in new[] { BBQTitles.Pepper, BBQTitles.Salt, BBQTitles.BrownSugar, BBQTitles.Mop })
                if (DS.Find(need) == null) log.AppendLine($"✖ нет «{need}» — {(need == BBQTitles.BrownSugar || need == BBQTitles.Mop ? "Барбекю → 1" : "Еда → Создать продукты")}");
            ProductDef brisket = DS.Ensure(BBQTitles.Brisket, beef, new Color(0.70f, 0.20f, 0.18f), 0.25f, false, dir, ref next, log);
            ProductDef rub = DS.Ensure(BBQTitles.BrisketRub, sub, new Color(0.30f, 0.24f, 0.19f), 0.01f, true, dir, ref next, log);
            ProductDef mustard = DS.Ensure(BBQTitles.Mustard, liquid, new Color(0.88f, 0.68f, 0.12f), 0.05f, false, dir, ref next, log);
            ProductDef dish = DS.Ensure(BBQTitles.BrisketDish, sub, new Color(0.24f, 0.12f, 0.08f), 0.25f, true, dir, ref next, log);
            ProductDef paste = DS.Ensure(BBQTitles.BrisketPaste, sub, new Color(0.56f, 0.38f, 0.13f), 0.02f, true, dir, ref next, log);
            foreach (ProductDef p in new[] { brisket, rub, mustard, dish, paste })
                if (p != null && p.DebrisColor.a < 0.01f) { p.DebrisColor = new Color(p.BaseColor.r, p.BaseColor.g, p.BaseColor.b, 1f); EditorUtility.SetDirty(p); }
            ProductDef pepper = DS.Find(BBQTitles.Pepper), salt = DS.Find(BBQTitles.Salt), brown = DS.Find(BBQTitles.BrownSugar);
            if (pepper != null && salt != null && brown != null && rub != null)
                DS.SaveRecipe("Руб для брискета", r => CookRecipeBook.BrisketRub(r, pepper, salt, brown, rub), log);
            else log.AppendLine("⚠ рецепт «Руб для брискета» не создан — нет перца, соли или коричневого сахара");
            if (mustard != null && pepper != null && salt != null && brown != null && paste != null)
                DS.SaveRecipe("Горчичная паста для брискета", r => CookRecipeBook.BrisketPaste(r, mustard, pepper, salt, brown, paste), log);
            AssetDatabase.SaveAssets();
            CookRecipeBook.Reload();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null)
            {
                db.CollectFromProject();
                EditorUtility.SetDirty(db);
                EditorSceneManager.MarkSceneDirty(db.gameObject.scene);
                log.AppendLine("✔ FoodDatabase в сцене пересобран — новые продукты (паста) теперь в базе. СОХРАНИ СЦЕНУ");
            }
            else log.AppendLine("⚠ в открытой сцене нет FoodDatabase — выдели его в своей сцене и нажми «Собрать продукты из проекта»");
            log.AppendLine("\nРецепт «Горчичная паста для брискета» (ГЛАВНЫЙ): горчица 2 нажатия + перец 2 дозы + соль 2 дозы + коричневый сахар 1 мастерок → миксер 3 с. Мажут мастерком.");
            log.AppendLine("Рецепт «Руб для брискета» (без горчицы, −10): перец чёрный 2 дозы + соль 2 дозы + коричневый сахар 1 мастерок → миксер 3 с.");
            log.AppendLine("Тары с пастой и рубом — «Еда → Тара: принимать ВСЁ» ещё раз.");
            DS.Done("Брискет — продукты", log);
        }

        // ───────────────────────── 2 ─────────────────────────

        [MenuItem(Root + "2. Брискет в сцену (кусок + ломтик)", priority = 161)]
        public static void MakeBrisket()
        {
            var log = new StringBuilder();
            ProductDef p = DS.Find(BBQTitles.Brisket);
            if (p == null) { EditorUtility.DisplayDialog("Брискет", "Сначала «1. Продукты».", "Ок"); return; }
            Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/BBQ/Mat_Ribs.mat");
            if (mat == null) { mat = DS.FoodHeapMat("Mat_Brisket", null, 6f, 0.15f, 0.9f, 0.25f, 0.004f, log); log.AppendLine("⚠ нет Mat_Ribs (Барбекю → 2) — сделал Mat_Brisket"); }
            Vector3 at = At();
            GameObject main = MakeObject("Brisket", Slices, p, mat, at, 3f * Mathf.Sqrt(MeatCutSettings.WorldScale), log);
            log.AppendLine($"Масштаб ×{MeatCutSettings.WorldScale:0.#}: брискет {Slices * Dims.x:0.00}×{Dims.y:0.00} м, толщина {Dims.z:0.00} м");
            GameObject one = MakeObject("BrisketSlice", 1, p, mat, at + Vector3.right * (Slices * Dims.x * 0.5f + 0.15f * MeatCutSettings.WorldScale), 0.4f, log);
            Selection.objects = new Object[] { main, one };
            EditorSceneManager.MarkSceneDirty(main.scene);
            log.AppendLine("\nДАЛЬШЕ РУКАМИ:");
            log.AppendLine("1. Перетащи BrisketSlice в Project → префаб BrisketSlice. Перетащи Brisket в Project → префаб Brisket.");
            log.AppendLine("2. Оба — в Network Manager → Spawnable Prefabs.");
            log.AppendLine("3. Брискет → 5 — свяжет ломтик с куском и говяжью тушу с брискетом.");
            log.AppendLine("4. Один Brisket можно оставить в сцене для теста.");
            DS.Done("Брискет", log);
        }

        private static GameObject MakeObject(string name, int n, ProductDef product, Material mat, Vector3 at, float mass, StringBuilder log)
        {
            GameObject go = FindRoot(name);
            bool fresh = go == null;
            if (fresh) { go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, name); go.transform.position = at; }
            else Undo.RegisterFullObjectHierarchyUndo(go, "Брискет");
            int food = LayerMask.NameToLayer("Food");
            if (food >= 0) go.layer = food;
            RibsMesh rm = RibsMesh.Build(n, Dims, 7, true);
            rm.Mesh.colors = RibsRack.EditorBrisketColors(rm);
            rm.Mesh.hideFlags = HideFlags.None;
            Mesh preview = DS.SaveMesh(rm.Mesh, $"Brisket_{n}_Preview", log);
            DS.Need<MeshFilter>(go).sharedMesh = preview;
            DS.Need<MeshRenderer>(go).sharedMaterial = mat;
            BoxCollider bc = DS.Need<BoxCollider>(go);
            bc.center = preview.bounds.center; bc.size = preview.bounds.size;
            Rigidbody rb = DS.Need<Rigidbody>(go);
            rb.mass = mass; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            RibsRack rr = DS.Need<RibsRack>(go);
            var so = new SerializedObject(rr);
            DS.SetObj(so, "_product", product);
            DS.SetInt(so, "_startState", (int)FoodState.Butchered);
            DS.SetInt(so, "_count", n);
            SerializedProperty b = so.FindProperty("_brisket"); if (b != null) b.boolValue = true;
            so.ApplyModifiedProperties();
            rr.EditorSetup(n, Dims, mat, null);
            rr.EditorSetBrisket(true);
            if (PrefabUtility.IsPartOfPrefabInstance(go))
            {
                // Иначе Unity не считает это изменением экземпляра: в Play и в сохранённом префабе останется СТАРЫЙ размер
                foreach (Component c in go.GetComponents<Component>()) if (c != null) PrefabUtility.RecordPrefabInstancePropertyModifications(c);
                log.AppendLine($"⚠ «{name}» в сцене — экземпляр префаба. Новый размер записан как Overrides: нажми на нём Overrides → Apply All, " +
                               "иначе из туши и по сети будет выпадать префаб СТАРОГО размера (или удали префаб и объект и сделай заново)");
            }

            log.AppendLine($"{(fresh ? "＋" : "✔ обновлён")} «{name}»: {n} ломт., Ribs Rack (галка Brisket ✔, продукт «{product.Title}», Разделано), Box Collider, Rigidbody {mass:0.0} кг, сеть" + (food >= 0 ? ", слой Food" : ""));
            return go;
        }

        // ───────────────────────── 3 ─────────────────────────

        [MenuItem(Root + "3. Рулон пенофола (выдели модель или сделаю)", priority = 162)]
        public static void MakeRoll()
        {
            var log = new StringBuilder();
            GameObject go = Selection.activeGameObject;
            bool own = go != null && !EditorUtility.IsPersistent(go) && go.GetComponent<RibsRack>() == null;
            Transform spin = null;
            if (!own)
            {
                go = FindRoot("PenofolRoll");
                if (go == null) { go = new GameObject("PenofolRoll"); Undo.RegisterCreatedObjectUndo(go, "PenofolRoll"); go.transform.position = At() + Vector3.up * 0.2f; }
                Transform roll = DS.FindOrCreate(go.transform, "Roll");
                roll.localPosition = Vector3.zero; roll.localRotation = Quaternion.Euler(0f, 0f, 90f); roll.localScale = Vector3.one;
                Mesh m = DS.SaveMesh(RollMesh(), "PenofolRoll_Mesh", log);
                DS.Need<MeshFilter>(roll.gameObject).sharedMesh = m;
                DS.Need<MeshRenderer>(roll.gameObject).sharedMaterial = DS.PlateMat(log);
                BoxCollider bc = DS.Need<BoxCollider>(go);
                bc.center = Vector3.zero; bc.size = new Vector3(0.46f, 0.15f, 0.15f);
                spin = roll;
                log.AppendLine("＋ «PenofolRoll» — рулон фольгированного пенофола (серебро снаружи, картонная втулка)");
            }
            else
            {
                if (go.GetComponentInChildren<Collider>() == null) { Undo.AddComponent<BoxCollider>(go); log.AppendLine("  ＋ Box Collider"); }
                spin = go.transform.Find("Roll");
                log.AppendLine($"✔ на твою модель «{go.name}»" + (spin == null ? " (ребёнка «Roll» нет — крутиться не будет, это не страшно)" : ""));
            }
            Rigidbody rb = DS.Need<Rigidbody>(go);
            rb.mass = 1f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            FoilRollItem fr = DS.Need<FoilRollItem>(go);
            fr.EditorSetup(spin);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine("\nСлой — как у мастерка (вместе с детьми). Лишний Pickup Item на модели — удали.");
            log.AppendLine("Сохрани префабом (перетащи в Project) и добавь в Spawnable Prefabs, если нужен префаб.");
            DS.Done("Рулон пенофола", log);
        }

        /// <summary>Рулон вдоль Y: серебряные витки, торцы со спиралью, картонная втулка.</summary>
        private static Mesh RollMesh()
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            const int seg = 28; const float R = 0.07f, r = 0.022f, H = 0.44f;
            Color silver = new Color(0.82f, 0.84f, 0.87f), silver2 = new Color(0.70f, 0.72f, 0.76f), card = new Color(0.62f, 0.48f, 0.32f), foam = new Color(0.90f, 0.90f, 0.88f);
            System.Action<float, float, float, float, Color, Color, bool> tube = (r0, r1, y0, y1, c0, c1, inward) =>
            {
                int b0 = v.Count;
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Color cc = (s % 4 < 2) ? c0 : c1;
                    v.Add(d * r0 + Vector3.up * y0); c.Add(cc);
                    v.Add(d * r1 + Vector3.up * y1); c.Add(cc);
                }
                for (int s = 0; s < seg; s++)
                {
                    int a = b0 + s * 2, b = a + 2;
                    float am = (s + 0.5f) / seg * Mathf.PI * 2f;
                    Vector3 o = new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am)) * (inward ? -1f : 1f);
                    Tri(v, t, a, b, b + 1, o); Tri(v, t, a, b + 1, a + 1, o);
                }
            };
            System.Action<float, bool> cap = (y, top) =>
            {
                // кольцо между втулкой и наружным витком — светлые витки пенофола
                int rings = 5;
                for (int k = 0; k < rings; k++)
                {
                    float ra = Mathf.Lerp(r, R, k / (float)rings), rb = Mathf.Lerp(r, R, (k + 1) / (float)rings);
                    int b0 = v.Count;
                    Color cc = k % 2 == 0 ? foam : silver2;
                    for (int s = 0; s <= seg; s++)
                    {
                        float a = s / (float)seg * Mathf.PI * 2f;
                        Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        v.Add(d * ra + Vector3.up * y); c.Add(cc);
                        v.Add(d * rb + Vector3.up * y); c.Add(cc);
                    }
                    Vector3 o = top ? Vector3.up : Vector3.down;
                    for (int s = 0; s < seg; s++)
                    {
                        int a = b0 + s * 2, b = a + 2;
                        Tri(v, t, a, b, b + 1, o); Tri(v, t, a, b + 1, a + 1, o);
                    }
                }
            };
            tube(R, R, -H * 0.5f, H * 0.5f, silver, silver2, false);
            tube(r, r, -H * 0.5f - 0.005f, H * 0.5f + 0.005f, card, card * 0.9f, true);
            cap(H * 0.5f, true); cap(-H * 0.5f, false);
            // свисающий язычок пенофола
            int f0 = v.Count;
            v.Add(new Vector3(R, -H * 0.45f, 0f)); v.Add(new Vector3(R, H * 0.45f, 0f));
            v.Add(new Vector3(R + 0.02f, H * 0.45f, -0.09f)); v.Add(new Vector3(R + 0.02f, -H * 0.45f, -0.09f));
            for (int i = 0; i < 4; i++) c.Add(silver);
            Tri(v, t, f0, f0 + 1, f0 + 2, Vector3.right); Tri(v, t, f0, f0 + 2, f0 + 3, Vector3.right);
            Tri(v, t, f0, f0 + 2, f0 + 1, Vector3.left); Tri(v, t, f0, f0 + 3, f0 + 2, Vector3.left);
            var m = new Mesh { name = "PenofolRoll_Mesh" };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private static void Tri(List<Vector3> v, List<int> t, int a, int b, int c, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(n, outward) < 0f) { int x = b; b = c; c = x; }
            t.Add(a); t.Add(b); t.Add(c);
        }

        // ───────────────────────── 4 ─────────────────────────

        [MenuItem(Root + "4. Бутылка горчицы (и остальные бутылки — обновить)", priority = 163)]
        public static void MakeMustard() => CatCooksBBQSetup.MakeBottles();

        // ───────────────────────── 5 ─────────────────────────

        [MenuItem(Root + "5. Связать: брискет → ломтик, говяжья туша → брискет", priority = 164)]
        public static void Link()
        {
            var log = new StringBuilder();
            GameObject main = null, slice = null;
            foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
            {
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                RibsRack rr = go != null ? go.GetComponent<RibsRack>() : null;
                if (rr == null) continue;
                var so = new SerializedObject(rr);
                SerializedProperty b = so.FindProperty("_brisket");
                if (b == null || !b.boolValue) continue;
                int n = so.FindProperty("_ribs").intValue;
                if (n > 1 && (main == null || go.name == "Brisket")) main = go;
                if (n == 1 && (slice == null || go.name == "BrisketSlice")) slice = go;
            }
            if (main == null) log.AppendLine("✖ нет префаба Brisket (Ribs Rack, Brisket ✔, ломтиков > 1) — меню 2, перетащи в Project");
            if (slice == null) log.AppendLine("✖ нет префаба BrisketSlice — меню 2, перетащи в Project");
            if (main != null && slice != null)
            {
                var so = new SerializedObject(main.GetComponent<RibsRack>());
                DS.SetObj(so, "_slicePrefab", slice);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(main);
                log.AppendLine($"✔ {main.name} → Slice Prefab = {slice.name} (пилой — ломтик)");
            }
            // брискет в сцене (тестовый) — тоже связать
            foreach (RibsRack r in Object.FindObjectsByType<RibsRack>(FindObjectsSortMode.None))
            {
                if (!r.IsBrisket || slice == null) continue;
                var so = new SerializedObject(r);
                if (so.FindProperty("_ribs").intValue <= 1) continue;
                Undo.RecordObject(r, "Брискет");
                DS.SetObj(so, "_slicePrefab", slice);
                so.ApplyModifiedProperties();
                log.AppendLine($"✔ «{r.name}» в сцене → Slice Prefab = {slice.name}");
            }
            int n2 = 0;
            foreach (FoodCarcass c in Object.FindObjectsByType<FoodCarcass>(FindObjectsSortMode.None))
            {
                MeatPartTag tag = c.GetComponentInChildren<MeatPartTag>(true);
                if (tag == null || tag.Species != MeatSpecies.Beef) continue;
                Undo.RecordObject(c, "Брискет");
                c.EditorSetBrisket(main);
                n2++;
                log.AppendLine($"✔ туша «{c.name}»: низ (брискет) → {(main != null ? main.name : "—")}");
                if (PrefabUtility.IsPartOfPrefabInstance(c)) log.AppendLine("   туша — префаб: Overrides → Apply All");
            }
            if (n2 == 0) log.AppendLine("⚠ говяжьей туши в сцене нет — Мясо → Новые туши → 3");
            AssetDatabase.SaveAssets();
            log.AppendLine("\nПроверь: Brisket и BrisketSlice — в Network Manager → Spawnable Prefabs. Сохрани сцену.");
            DS.Done("Брискет — связать", log);
        }

        // ───────────────────────── 6 ─────────────────────────

        [MenuItem(Root + "6. Проверить брискет", priority = 165)]
        public static void Check()
        {
            var sb = new StringBuilder();
            int err = 0, warn = 0;
            foreach (string t in new[] { BBQTitles.Brisket, BBQTitles.BrisketPaste, BBQTitles.BrisketRub, BBQTitles.Mustard, BBQTitles.BrisketDish, BBQTitles.Pepper, BBQTitles.Salt, BBQTitles.BrownSugar, BBQTitles.Mop })
                if (DS.Find(t) != null) sb.AppendLine("  ✔ " + t); else { sb.AppendLine($"  ✖ нет «{t}» — Брискет → 1 / Барбекю → 1"); err++; }
            if (AssetDatabase.LoadAssetAtPath<CookRecipeDef>($"{DS.RecipeDir}/Recipe_Руб для брискета.asset") == null) { sb.AppendLine("  ✖ нет рецепта «Руб для брискета» — меню 1"); err++; }
            else sb.AppendLine("  ✔ рецепт «Руб для брискета»");
            if (AssetDatabase.LoadAssetAtPath<CookRecipeDef>($"{DS.RecipeDir}/Recipe_Горчичная паста для брискета.asset") == null) { sb.AppendLine("  ✖ нет рецепта «Горчичная паста для брискета» — меню 1"); err++; }
            else sb.AppendLine("  ✔ рецепт «Горчичная паста для брискета»");
            {
                FoodDatabase fdb = Object.FindFirstObjectByType<FoodDatabase>();
                ProductDef pp = DS.Find(BBQTitles.BrisketPaste);
                if (fdb != null && pp != null)
                {
                    fdb.CollectFromProject();
                    bool inDb = fdb.Get(pp.Id) == pp;
                    if (inDb) sb.AppendLine("  ✔ паста в FoodDatabase");
                    else { sb.AppendLine("  ✖ паста НЕ в FoodDatabase — выдели FoodDatabase → «Собрать продукты из проекта», сохрани сцену"); err++; }
                }
            }
            // префабы — того же размера, что даёт меню 2 сейчас (масштаб мяса)
            foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
            {
                GameObject pg = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                RibsRack pr = pg != null ? pg.GetComponent<RibsRack>() : null;
                if (pr == null || !pr.IsBrisket) continue;
                var po = new SerializedObject(pr);
                float w = po.FindProperty("_ribWidth").floatValue, l = po.FindProperty("_boneLength").floatValue;
                bool same = Mathf.Abs(w - Dims.x) < Dims.x * 0.05f && Mathf.Abs(l - Dims.y) < Dims.y * 0.05f;
                if (same) sb.AppendLine($"  ✔ префаб «{pg.name}»: ломтик {w * 100f:0} см, ширина {l:0.00} м (×{MeatCutSettings.WorldScale:0.#})");
                else { sb.AppendLine($"  ✖ префаб «{pg.name}» СТАРОГО размера: ломтик {w * 100f:0} см, ширина {l:0.00} м, а надо {Dims.x * 100f:0} см / {Dims.y:0.00} м — удали префаб и объект в сцене, меню 2, перетащи заново, меню 5"); err++; }
            }
            bool hasB = false;
            foreach (RibsRack r in Object.FindObjectsByType<RibsRack>(FindObjectsSortMode.None))
            {
                if (!r.IsBrisket) continue;
                hasB = true;
                var so = new SerializedObject(r);
                int n = so.FindProperty("_ribs").intValue;
                if (n > 1 && so.FindProperty("_slicePrefab").objectReferenceValue == null) { sb.AppendLine($"  ⚠ «{r.name}»: Slice Prefab пуст — пилой не нарезать (меню 5)"); warn++; }
                else sb.AppendLine($"  ✔ брискет «{r.name}» ({n} ломт.)");
            }
            if (!hasB) { sb.AppendLine("  ⚠ брискета в сцене нет — меню 2 (или говяжья туша)"); warn++; }
            if (Object.FindFirstObjectByType<FoilRollItem>() == null) { sb.AppendLine("  ⚠ нет рулона пенофола — меню 3"); warn++; } else sb.AppendLine("  ✔ рулон пенофола");
            if (Object.FindFirstObjectByType<PlanerItem>() == null) { sb.AppendLine("  ⚠ нет рубанка — Барбекю → 7"); warn++; }
            if (Object.FindFirstObjectByType<BastingBrushItem>() == null) { sb.AppendLine("  ⚠ нет кисти — Барбекю → 6"); warn++; }
            if (Object.FindFirstObjectByType<TongsItem>() == null) { sb.AppendLine("  ⚠ нет клещей — Барбекю → 4"); warn++; }
            if (Object.FindFirstObjectByType<SprayBottleItem>() == null) { sb.AppendLine("  ⚠ нет пульверизатора — Барбекю → 5"); warn++; }
            bool mustardBottle = false;
            foreach (Tools.LiquidDoseJarItem j in Object.FindObjectsByType<Tools.LiquidDoseJarItem>(FindObjectsSortMode.None))
            {
                var p = new SerializedObject(j).FindProperty("_product").objectReferenceValue as ProductDef;
                if (p != null && p.Title == BBQTitles.Mustard) mustardBottle = true;
            }
            if (!mustardBottle) { sb.AppendLine("  ⚠ нет бутылки горчицы — меню 4"); warn++; } else sb.AppendLine("  ✔ бутылка горчицы");
            foreach (FoodCarcass c in Object.FindObjectsByType<FoodCarcass>(FindObjectsSortMode.None))
            {
                MeatPartTag tag = c.GetComponentInChildren<MeatPartTag>(true);
                if (tag == null || tag.Species != MeatSpecies.Beef) continue;
                bool ok = new SerializedObject(c).FindProperty("_brisketPrefab").objectReferenceValue != null;
                sb.AppendLine(ok ? $"  ✔ туша «{c.name}» → брискет" : $"  ⚠ туша «{c.name}»: брискет выпадет обычным куском — меню 5");
                if (!ok) warn++;
            }
            sb.Insert(0, err == 0 ? (warn == 0 ? "ВСЁ ГОТОВО ✔\n\n" : $"Можно готовить, замечаний: {warn}\n\n") : $"Ошибок: {err}, замечаний: {warn}\n\n");
            DS.Done("Брискет — проверка", sb);
        }

        private static Vector3 At() => SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

        private static GameObject FindRoot(string name)
        {
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && t.parent == null && t.name == name && t.gameObject.scene.IsValid()) return t.gameObject;
            return null;
        }
    }
}
#endif
