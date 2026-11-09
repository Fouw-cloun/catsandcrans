#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using CatCooks.Food.Tools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DS = CatCooks.Food.CatCooksDessertSetup;

namespace CatCooks.Food
{
    /// <summary>
    /// CatCooks → Барбекю. Рёбра барбекю: специи, моп-соус, глазурь, клещи, пульверизатор, кисть, рубанок.
    ///   1. Продукты и рецепты.
    ///   2. Рёбра в сцену — пласт (8 рёбер) и одно ребро (для нарезки). Сохрани оба префабами сам.
    ///   3. Бутылки и банки в сцену: вода, яблочный сок, яблочный уксус, бурбон, вустерский соус, мёд,
    ///      соус барбекю, баночка сушёного чеснока. 3б — мешок коричневого сахара из выделенного мешка.
    ///   4–7. Инструменты: клещи, пульверизатор, кисть, рубанок — выдели свою модель (компоненты
    ///      на неё) или ничего не выделяй — сделаю простую модель «стройка».
    ///   8. Вид соусов в выделенной таре (вёдра, кастрюля, ковш).
    ///   9. Связать: одно ребро → пласту, пласт → туше (выдели тушу).
    ///   10. Проверка.
    /// Префабы меню не создаёт — только объекты в сцене, компоненты, меши, материалы, продукты.
    /// </summary>
    public static class CatCooksBBQSetup
    {
        private const string Dir = "Assets/Models/BBQ";

        // ───────────────────────── 1. ПРОДУКТЫ И РЕЦЕПТЫ ─────────────────────────

        [MenuItem("CatCooks/Барбекю/1. Продукты и рецепты", priority = 140)]
        public static void CreateAll()
        {
            var log = new StringBuilder();
            string dir = DS.ProductsDir();
            ushort next = (ushort)(DS.MaxId() + 1);
            FoodGroupDef sub = DS.FindGroup("Group_SubRecipe") ?? DS.GroupOf("Меренга") ?? DS.GroupOf("Бульон");
            FoodGroupDef meat = DS.GroupOf("Свинина") ?? DS.GroupOf("Говядина") ?? sub;
            FoodGroupDef spice = DS.GroupOf("Паприка") ?? DS.GroupOf("Перец чёрный") ?? sub;
            FoodGroupDef bulk = DS.GroupOf("Сахар") ?? spice;
            FoodGroupDef liquid = DS.GroupOf("Вода") ?? DS.GroupOf("Молоко") ?? sub;
            foreach (string need in new[] { "Соль", "Паприка", "Перец чёрный", "Вода" })
                if (DS.Find(need) == null) log.AppendLine($"✖ нет «{need}» — CatCooks → Еда → создать продукты");

            var made = new List<ProductDef>
            {
                DS.Ensure(BBQTitles.Ribs, meat, new Color(0.80f, 0.36f, 0.33f), 0.3f, false, dir, ref next, log),
                DS.Ensure(BBQTitles.Dish, sub, new Color(0.46f, 0.12f, 0.06f), 0.3f, true, dir, ref next, log),
                DS.Ensure(BBQTitles.Garlic, spice, new Color(0.93f, 0.88f, 0.72f), 0.01f, false, dir, ref next, log),
                DS.Ensure(BBQTitles.Rub, sub, new Color(0.62f, 0.22f, 0.10f), 0.01f, true, dir, ref next, log),
                DS.Ensure(BBQTitles.AppleJuice, liquid, new Color(0.93f, 0.72f, 0.30f), 0.1f, false, dir, ref next, log),
                DS.Ensure(BBQTitles.AppleVinegar, liquid, new Color(0.86f, 0.70f, 0.42f), 0.1f, false, dir, ref next, log),
                DS.Ensure(BBQTitles.Bourbon, liquid, new Color(0.72f, 0.38f, 0.12f), 0.1f, false, dir, ref next, log),
                DS.Ensure(BBQTitles.Mop, sub, new Color(0.82f, 0.58f, 0.30f), 0.1f, true, dir, ref next, log),
                DS.Ensure(BBQTitles.Sauce, liquid, new Color(0.52f, 0.14f, 0.07f), 0.06f, false, dir, ref next, log),
                DS.Ensure(BBQTitles.BrownSugar, bulk, new Color(0.62f, 0.42f, 0.22f), 0.01f, false, dir, ref next, log),
                DS.Ensure(BBQTitles.Honey, liquid, new Color(0.95f, 0.70f, 0.20f), 0.06f, false, dir, ref next, log),
                DS.Ensure(BBQTitles.Worcester, liquid, new Color(0.28f, 0.13f, 0.07f), 0.1f, false, dir, ref next, log),
                DS.Ensure(BBQTitles.Glaze, sub, new Color(0.44f, 0.10f, 0.05f), 0.06f, true, dir, ref next, log),
            };
            foreach (ProductDef p in made)
                if (p != null && p.DebrisColor.a < 0.01f) { p.DebrisColor = new Color(p.BaseColor.r, p.BaseColor.g, p.BaseColor.b, 1f); EditorUtility.SetDirty(p); }

            ProductDef salt = DS.Find(BBQTitles.Salt), paprika = DS.Find(BBQTitles.Paprika), pepper = DS.Find(BBQTitles.Pepper), water = DS.Find(BBQTitles.Water);
            ProductDef garlic = DS.Find(BBQTitles.Garlic), rub = DS.Find(BBQTitles.Rub), juice = DS.Find(BBQTitles.AppleJuice), vin = DS.Find(BBQTitles.AppleVinegar);
            ProductDef bourbon = DS.Find(BBQTitles.Bourbon), mop = DS.Find(BBQTitles.Mop), sauce = DS.Find(BBQTitles.Sauce), brown = DS.Find(BBQTitles.BrownSugar);
            ProductDef honey = DS.Find(BBQTitles.Honey), worc = DS.Find(BBQTitles.Worcester), glaze = DS.Find(BBQTitles.Glaze);
            DS.EnsureDir(DS.RecipeDir);
            if (salt != null && paprika != null && pepper != null && garlic != null && rub != null)
                DS.SaveRecipe("Сухой руб", r => CookRecipeBook.DryRub(r, salt, paprika, pepper, garlic, rub), log);
            if (water != null && juice != null && vin != null && bourbon != null && mop != null)
                DS.SaveRecipe("Моп-соус", r => CookRecipeBook.MopSauce(r, water, juice, vin, bourbon, mop), log);
            if (sauce != null && brown != null && honey != null && vin != null && worc != null && paprika != null && garlic != null && glaze != null)
                DS.SaveRecipe("Глазурь барбекю", r => CookRecipeBook.BBQGlaze(r, sauce, brown, honey, vin, worc, paprika, garlic, glaze), log);

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
            log.AppendLine("\nДальше: 2 — рёбра, 3 — бутылки и банки, 4–7 — инструменты, 8 — вид соусов в таре, 9 — связать с тушей, 10 — проверка.");
            DS.Done("Барбекю", log);
        }

        // ───────────────────────── 2. РЁБРА ─────────────────────────

        [MenuItem("CatCooks/Барбекю/2. Рёбра в сцену (пласт + одно ребро)", priority = 141)]
        public static void MakeRibs()
        {
            var log = new StringBuilder();
            ProductDef ribsP = DS.Find(BBQTitles.Ribs);
            if (ribsP == null) { EditorUtility.DisplayDialog("Барбекю", "Сначала «1. Продукты и рецепты».", "Ок"); return; }
            Vector3 dims = RibDims(log, out int count);
            Material mat = RibsMat(log);
            string shName = mat != null && mat.shader != null ? mat.shader.name : "—";
            if (shName != "CatCooks/Food Heap") log.AppendLine($"⚠ у Mat_Ribs шейдер «{shName}», нужен «CatCooks/Food Heap» — иначе цвета мяса (вершины) не видны, рёбра будут однотонные");
            else log.AppendLine("Mat_Ribs: шейдер CatCooks/Food Heap ✔ (мясо с прожилками жира — цвета в вершинах)");
            Vector3 at = At();
            GameObject rack = MakeRibsObject("RibsRack", count, dims, mat, ribsP, at, Mathf.Clamp(count * 0.4f, 1f, 6f) * Mathf.Sqrt(MeatCutSettings.WorldScale), log);
            GameObject one = MakeRibsObject("RibsSlice", 1, dims, mat, ribsP, at + Vector3.right * (count * dims.x * 0.5f + dims.x * 2f), 0.5f, log);
            Selection.objects = new Object[] { rack, one };
            EditorSceneManager.MarkSceneDirty(rack.scene);
            log.AppendLine("\nДАЛЬШЕ РУКАМИ:");
            log.AppendLine("1. Перетащи RibsSlice в Project → префаб RibsSlice. Перетащи RibsRack в Project → префаб RibsRack.");
            log.AppendLine("2. Оба — в Network Manager → Spawnable Prefabs.");
            log.AppendLine("3. CatCooks → Барбекю → 9 — свяжет RibsSlice с RibsRack (Slice Prefab) и пласт с тушей (выдели тушу).");
            log.AppendLine("   Или руками: RibsRack → Ribs Rack → Slice Prefab = RibsSlice; туша → Food Carcass → Ribs Prefab = RibsRack.");
            log.AppendLine("4. Один RibsRack можно оставить в сцене для тестов.");
            DS.Done("Рёбра", log);
        }

        /// <summary>Размер рёбер — по куску «рёбра» туши в сцене, иначе стандартный.</summary>
        private static Vector3 RibDims(StringBuilder log, out int count)
        {
            count = 8;
            float sc = MeatCutSettings.WorldScale;
            Vector3 d = new Vector3(0.07f, 0.32f, 0.06f) * sc;
            foreach (FoodCarcass c in Object.FindObjectsByType<FoodCarcass>(FindObjectsSortMode.None))
            {
                int part = c.EditorFindRibsPart();
                Transform t = c.EditorPart(part);
                Renderer r = t != null ? t.GetComponent<Renderer>() : null;
                if (r == null) continue;
                Vector3 s = r.bounds.size;
                float a = Mathf.Max(s.x, s.z), b = Mathf.Min(s.x, s.z);
                float W = Mathf.Clamp(a * 0.95f, 0.2f, 3f * sc), D = Mathf.Clamp(b * 0.9f, 0.12f, 1.5f * sc);
                d = new Vector3(W / count, D, Mathf.Clamp(s.y * 0.35f, 0.03f, 0.2f * sc));
                log.AppendLine($"Размер — по куску «{t.name}» туши «{c.name}»: {W:0.00}×{D:0.00} м, толщина {d.z:0.00} м");
                return d;
            }
            log.AppendLine($"Туши с куском «рёбра» в сцене нет — стандартный размер ×{sc:0.#}: {count * d.x:0.00}×{d.y:0.00} м");
            return d;
        }

        private static GameObject MakeRibsObject(string name, int ribs, Vector3 dims, Material mat, ProductDef product, Vector3 at, float mass, StringBuilder log)
        {
            GameObject go = FindInScene(name);
            bool fresh = go == null;
            if (fresh)
            {
                go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, name);
                go.transform.position = at;
            }
            else Undo.RegisterFullObjectHierarchyUndo(go, "Обновить " + name);
            int food = LayerMask.NameToLayer("Food");
            if (food >= 0) go.layer = food;
            RibsMesh rm = RibsMesh.Build(ribs, dims);
            rm.Mesh.colors = RibsRack.EditorRawColors(rm);
            rm.Mesh.hideFlags = HideFlags.None;
            Mesh preview = DS.SaveMesh(rm.Mesh, $"Ribs_{ribs}_Preview", log);
            (DS.Need<MeshFilter>(go)).sharedMesh = preview;
            (DS.Need<MeshRenderer>(go)).sharedMaterial = mat;
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
            DS.SetInt(so, "_count", ribs);
            SerializedProperty bris = so.FindProperty("_brisket");
            if (bris != null && bris.boolValue) { bris.boolValue = false; log.AppendLine($"⚠ у «{name}» стояла галка Brisket — снял (это рёбра)"); }
            so.ApplyModifiedProperties();
            rr.EditorSetup(ribs, dims, mat, null);
            if (PrefabUtility.IsPartOfPrefabInstance(go))
            {
                // Иначе Unity не считает это изменением экземпляра: в Play и в сохранённом префабе останется СТАРЫЙ размер
                foreach (Component c in go.GetComponents<Component>()) if (c != null) PrefabUtility.RecordPrefabInstancePropertyModifications(c);
                log.AppendLine($"⚠ «{name}» в сцене — экземпляр префаба. Новый размер записан как Overrides: нажми на нём Overrides → Apply All, " +
                               "иначе из туши и по сети будет выпадать префаб СТАРОГО размера (или удали префаб и объект и сделай заново)");
            }

            if (!fresh) { log.AppendLine($"✔ «{name}» уже был в сцене — обновлён на месте (меш, цвета, коллайдер; Slice Prefab и прочие ссылки не тронуты)"); return go; }
            log.AppendLine($"＋ «{name}»: {ribs} рёб., Ribs Rack (Food Piece «{product.Title}», Разделано), Box Collider, Rigidbody {mass:0.#} кг, сеть" +
                           (food >= 0 ? ", слой Food" : ""));
            return go;
        }

        private static Material RibsMat(StringBuilder log)
        {
            Texture2D tex = MeatTexture(log);
            return DS.FoodHeapMat("Mat_Ribs", tex, 6f, 0.15f, 0.9f, 0.25f, 0.004f, log);
        }

        /// <summary>Волокна мяса 256×256: продольные нити, мелкие поры, бесшовно. Серая — цвет даёт меш.</summary>
        private static Texture2D MeatTexture(StringBuilder log)
        {
            string path = Dir + "/Tex_RibsMeat.png";
            Texture2D have = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (have != null) return have;
            const int N = 256;
            var px = new Color32[N * N];
            var rnd = new System.Random(17);
            float[] ph = new float[8]; for (int i = 0; i < ph.Length; i++) ph[i] = (float)rnd.NextDouble() * 6.28f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    float warp = 0.04f * Mathf.Sin((v * 3f + u) * 6.283f + ph[0]) + 0.02f * Mathf.Sin((u * 5f) * 6.283f + ph[1]);
                    float fib = Mathf.Abs(Mathf.Sin((u + warp) * 6.283f * 36f + ph[2]));            // нити
                    float fib2 = Mathf.Abs(Mathf.Sin((u * 1.0f + warp * 1.7f) * 6.283f * 13f + ph[3]));
                    float pore = Mathf.PerlinNoise(u * 40f + 3.3f, v * 40f + 7.1f);
                    float g = 0.7f + 0.18f * fib + 0.08f * fib2 - 0.12f * Mathf.SmoothStep(0.62f, 0.8f, pore);
                    byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(g) * 255f);
                    px[y * N + x] = new Color32(b, b, b, 255);
                }
            var t = new Texture2D(N, N, TextureFormat.RGB24, true);
            t.SetPixels32(px); t.Apply();
            DS.EnsureDir(Dir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), path), t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            log.AppendLine($"  ＋ текстура {path} (волокна мяса)");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ───────────────────────── 3. БУТЫЛКИ И БАНКИ ─────────────────────────

        internal enum Icon { Apple, AppleDrop, Barrel, Drop, Honeycomb, Flame, Garlic, Leaf, Sunflower, Thyme, Nutmeg }   // Leaf, Sunflower — борщ; Thyme, Nutmeg — телятина по-орловски

        internal sealed class Bottle
        {
            public string Name, Product, Line1, Line2; public int Dose, Segs; public bool Jar, Shaker;
            public Color Body, Cap, LabelBg, LabelFg, LabelAccent; public float H, R, Neck; public Icon Icon;
        }

        private static Bottle[] BottleList() => new[]
        {
            new Bottle { Name = "Bottle_AppleJuice", Product = BBQTitles.AppleJuice, Dose = 5, Segs = 22, H = 0.32f, R = 0.07f, Neck = 0.025f,
                         Body = new Color(0.97f, 0.76f, 0.24f), Cap = new Color(0.30f, 0.66f, 0.22f),
                         LabelBg = new Color(0.98f, 0.95f, 0.85f), LabelFg = new Color(0.20f, 0.45f, 0.12f), LabelAccent = new Color(0.85f, 0.15f, 0.12f),
                         Icon = Icon.Apple, Line1 = "ЯБЛОЧНЫЙ", Line2 = "СОК" },
            new Bottle { Name = "Bottle_AppleVinegar", Product = BBQTitles.AppleVinegar, Dose = 1, Segs = 18, H = 0.36f, R = 0.045f, Neck = 0.016f,
                         Body = new Color(0.86f, 0.66f, 0.36f), Cap = new Color(0.62f, 0.44f, 0.24f),
                         LabelBg = Color.white, LabelFg = new Color(0.55f, 0.12f, 0.10f), LabelAccent = new Color(0.30f, 0.55f, 0.85f),
                         Icon = Icon.AppleDrop, Line1 = "ЯБЛОЧНЫЙ", Line2 = "УКСУС" },
            new Bottle { Name = "Bottle_Bourbon", Product = BBQTitles.Bourbon, Dose = 1, Segs = 4, H = 0.34f, R = 0.075f, Neck = 0.022f,
                         Body = new Color(0.72f, 0.36f, 0.10f), Cap = new Color(0.72f, 0.06f, 0.05f),
                         LabelBg = new Color(0.10f, 0.08f, 0.07f), LabelFg = new Color(0.98f, 0.80f, 0.35f), LabelAccent = new Color(0.62f, 0.38f, 0.18f),
                         Icon = Icon.Barrel, Line1 = "БУРБОН", Line2 = "KENTUCKY" },
            new Bottle { Name = "Bottle_Worcester", Product = BBQTitles.Worcester, Dose = 1, Segs = 16, H = 0.28f, R = 0.038f, Neck = 0.013f,
                         Body = new Color(0.22f, 0.10f, 0.06f), Cap = new Color(0.95f, 0.55f, 0.10f),
                         LabelBg = new Color(0.98f, 0.62f, 0.14f), LabelFg = new Color(0.10f, 0.06f, 0.05f), LabelAccent = new Color(0.25f, 0.10f, 0.06f),
                         Icon = Icon.Drop, Line1 = "ВУСТЕРСКИЙ", Line2 = "СОУС" },
            new Bottle { Name = "Jar_Honey", Product = BBQTitles.Honey, Dose = 1, Segs = 22, H = 0.18f, R = 0.085f, Neck = 0.065f, Jar = true,
                         Body = new Color(0.96f, 0.68f, 0.14f), Cap = new Color(0.98f, 0.86f, 0.28f),
                         LabelBg = new Color(0.20f, 0.12f, 0.05f), LabelFg = new Color(0.99f, 0.82f, 0.25f), LabelAccent = new Color(0.98f, 0.70f, 0.12f),
                         Icon = Icon.Honeycomb, Line1 = "МЁД", Line2 = "ЦВЕТОЧНЫЙ" },
            new Bottle { Name = "Jar_BBQSauce", Product = BBQTitles.Sauce, Dose = 5, Segs = 22, H = 0.22f, R = 0.085f, Neck = 0.066f, Jar = true,
                         Body = new Color(0.48f, 0.10f, 0.05f), Cap = new Color(0.12f, 0.10f, 0.09f),
                         LabelBg = new Color(0.98f, 0.78f, 0.12f), LabelFg = new Color(0.45f, 0.06f, 0.04f), LabelAccent = new Color(0.95f, 0.30f, 0.06f),
                         Icon = Icon.Flame, Line1 = "СОУС", Line2 = "БАРБЕКЮ" },
            new Bottle { Name = "Bottle_Mustard", Product = BBQTitles.Mustard, Dose = 1, Segs = 16, H = 0.24f, R = 0.045f, Neck = 0.016f,
                         Body = new Color(0.92f, 0.72f, 0.12f), Cap = new Color(0.80f, 0.12f, 0.08f),
                         LabelBg = new Color(0.99f, 0.96f, 0.85f), LabelFg = new Color(0.62f, 0.38f, 0.02f), LabelAccent = new Color(0.30f, 0.45f, 0.15f),
                         Icon = Icon.Drop, Line1 = "ГОРЧИЦА", Line2 = "ДЛЯ МЯСА" },
            new Bottle { Name = "Shaker_Garlic", Product = BBQTitles.Garlic, Dose = 1, Segs = 18, H = 0.15f, R = 0.045f, Neck = 0.042f, Jar = true, Shaker = true,
                         Body = new Color(0.96f, 0.94f, 0.88f), Cap = new Color(0.72f, 0.73f, 0.76f),
                         LabelBg = new Color(0.55f, 0.36f, 0.68f), LabelFg = Color.white, LabelAccent = new Color(0.98f, 0.96f, 0.90f),
                         Icon = Icon.Garlic, Line1 = "ЧЕСНОК", Line2 = "СУШЁНЫЙ" },
        };

        [MenuItem("CatCooks/Барбекю/3. Бутылки и банки в сцену (с этикетками)", priority = 142)]
        public static void MakeBottles()
        {
            var log = new StringBuilder();
            if (DS.Find(BBQTitles.AppleJuice) == null) { EditorUtility.DisplayDialog("Барбекю", "Сначала «1. Продукты и рецепты».", "Ок"); return; }
            // вода — из крана, бутылка не нужна
            foreach (LiquidDoseJarItem j in Object.FindObjectsByType<LiquidDoseJarItem>(FindObjectsSortMode.None))
                if (j != null && j.name == "Bottle_Water") { log.AppendLine("－ «Bottle_Water» убрана — вода из крана"); Undo.DestroyObjectImmediate(j.gameObject); }
            Vector3 at = At();
            Material mat = DS.PlateMat(log);
            int k = 0;
            var made = new List<Object>();
            foreach (Bottle b in BottleList())
            {
                ProductDef p = DS.Find(b.Product);
                if (p == null) { log.AppendLine($"⚠ нет «{b.Product}» — {(b.Product == BBQTitles.Mustard ? "Брискет → 1" : "Барбекю → 1")}"); continue; }
                GameObject go = FindInScene(b.Name);
                bool fresh = go == null;
                if (fresh)
                {
                    go = new GameObject(b.Name);
                    Undo.RegisterCreatedObjectUndo(go, b.Name);
                    go.transform.position = at + Vector3.right * (k * 0.25f);
                }
                k++;
                BuildBottle(go, b, mat, log);
                if (b.Shaker)
                {
                    PowderShakerItem ps = DS.Need<PowderShakerItem>(go);
                    var so = new SerializedObject(ps);
                    DS.SetObj(so, "_product", p);
                    DS.SetObj(so, "_mouth", go.transform.Find("Spout"));
                    SerializedProperty inf = so.FindProperty("_infinite"); if (inf != null) inf.boolValue = true;
                    so.ApplyModifiedProperties();
                }
                else
                {
                    LiquidDoseJarItem jar = DS.Need<LiquidDoseJarItem>(go);
                    var so = new SerializedObject(jar);
                    DS.SetObj(so, "_product", p);
                    DS.SetInt(so, "_portionsPerDose", b.Dose);
                    DS.SetObj(so, "_spout", go.transform.Find("Spout"));
                    SerializedProperty inf = so.FindProperty("_infinite"); if (inf != null) inf.boolValue = true;
                    so.ApplyModifiedProperties();
                }
                log.AppendLine($"{(fresh ? "＋" : "✔ обновлён")} «{go.name}» — {b.Product} ({b.Line1} {b.Line2}), ПКМ: {b.Dose} {(b.Shaker ? "доза" : "порц.")}");
                made.Add(go);
            }
            Selection.objects = made.ToArray();
            if (made.Count > 0) EditorSceneManager.MarkSceneDirty(((GameObject)made[0]).scene);
            log.AppendLine("\nЭтикетки — текстуры Label_*.png в Assets/Models/BBQ (надпись и значок). Повторный запуск обновляет бутылки на месте.");
            log.AppendLine("Соль, паприка, перец — твои баночки (Powder Shaker Item). Коричневый сахар — 3б. Сохрани префабами, если нужно.");
            DS.Done("Бутылки и банки", log);
        }

        private static GameObject FindInScene(string name)
        {
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && t.parent == null && t.name == name && t.gameObject.scene.IsValid()) return t.gameObject;
            return null;
        }

        /// <summary>Тело, крышка, значок сверху, этикетка (своя текстура с надписью), Spout, коллайдер, физика, сеть.</summary>
        internal static void BuildBottle(GameObject go, Bottle b, Material mat, StringBuilder log)
        {
            var mb = new MB();
            float H = b.H, R = b.R, n = b.Neck;
            List<Vector2> prof = b.Jar
                ? new List<Vector2> { new Vector2(0f, 0f), new Vector2(R * 0.92f, 0f), new Vector2(R, H * 0.06f), new Vector2(R, H * 0.82f), new Vector2(R * 0.9f, H * 0.88f), new Vector2(n, H * 0.9f) }
                : new List<Vector2> { new Vector2(0f, 0f), new Vector2(R * 0.92f, 0f), new Vector2(R, H * 0.05f), new Vector2(R, H * 0.6f), new Vector2(R * 0.85f, H * 0.7f), new Vector2(n * 1.3f, H * 0.8f), new Vector2(n, H * 0.84f), new Vector2(n, H * 0.92f) };
            Matrix4x4 bodyM = b.Segs == 4 ? Matrix4x4.Rotate(Quaternion.Euler(0f, 45f, 0f)) : Matrix4x4.identity;
            mb.Lathe(bodyM, prof, b.Segs, y => b.Body, true, false);
            float cy = prof[prof.Count - 1].y;
            float cr = b.Jar ? n * 1.1f : n * 1.18f, ch = b.Jar ? H * 0.1f : H * 0.08f;
            mb.Lathe(Matrix4x4.identity, new List<Vector2> { new Vector2(0f, cy), new Vector2(cr, cy), new Vector2(cr, cy + ch), new Vector2(cr * 0.92f, cy + ch * 1.08f), new Vector2(0f, cy + ch * 1.08f) },
                     Mathf.Max(12, b.Segs == 4 ? 16 : b.Segs), y => b.Cap, true, false);
            float top = cy + ch * 1.08f;
            // значок сверху — видно издалека
            switch (b.Icon)
            {
                case Icon.Apple:
                case Icon.AppleDrop:
                    {
                        float ar = n * 0.95f;
                        mb.Lathe(Matrix4x4.Translate(new Vector3(0f, top, 0f)), Sphere(ar, 0.9f), 12, y => new Color(0.86f, 0.14f, 0.12f), false, false);
                        mb.Box(Matrix4x4.Translate(new Vector3(0f, top + ar * 1.9f, 0f)), new Vector3(ar * 0.15f, ar * 0.5f, ar * 0.15f), new Color(0.40f, 0.26f, 0.12f));
                        mb.Box(Matrix4x4.Translate(new Vector3(ar * 0.35f, top + ar * 1.85f, 0f)) * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, -35f)), new Vector3(ar * 0.6f, ar * 0.12f, ar * 0.35f), new Color(0.30f, 0.66f, 0.22f));
                        break;
                    }
                case Icon.Barrel:   // сургуч: подтёки
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * 1.57f + 0.4f;
                        mb.Box(Matrix4x4.Translate(new Vector3(Mathf.Cos(a) * cr, cy - ch * (0.4f + 0.25f * (i % 2)), Mathf.Sin(a) * cr)), new Vector3(cr * 0.35f, ch * 1.4f, cr * 0.35f), b.Cap);
                    }
                    break;
                case Icon.Honeycomb:   // ложка-веретено для мёда на крышке
                    {
                        mb.Box(Matrix4x4.Translate(new Vector3(0f, top + n * 0.5f, 0f)) * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 70f)), new Vector3(n * 0.12f, n * 1.6f, n * 0.12f), new Color(0.80f, 0.58f, 0.33f));
                        for (int i = 0; i < 3; i++)
                            mb.Lathe(Matrix4x4.Translate(new Vector3(-n * 0.55f - i * n * 0.12f, top + n * 0.25f + i * n * 0.04f, 0f)) * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 70f)),
                                     new List<Vector2> { new Vector2(0f, -n * 0.05f), new Vector2(n * 0.16f, -n * 0.05f), new Vector2(n * 0.16f, n * 0.05f), new Vector2(0f, n * 0.05f) }, 10, y => new Color(0.86f, 0.62f, 0.35f), true, true);
                        break;
                    }
                case Icon.Flame:   // язычки огня на крышке
                    for (int i = 0; i < 3; i++)
                    {
                        float x = (i - 1) * n * 0.45f, hh = n * (i == 1 ? 0.9f : 0.6f);
                        mb.Lathe(Matrix4x4.Translate(new Vector3(x, top, 0f)), new List<Vector2> { new Vector2(0f, 0f), new Vector2(n * 0.2f, hh * 0.25f), new Vector2(n * 0.14f, hh * 0.65f), new Vector2(0f, hh) }, 8,
                                 y => Color.Lerp(new Color(0.98f, 0.35f, 0.05f), new Color(1f, 0.85f, 0.2f), y), true, false);
                    }
                    break;
                case Icon.Leaf:     // дырочки посыпалки + два лавровых листика сверху
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i / 7f * 6.283f, rr = i == 0 ? 0f : cr * 0.5f;
                        mb.Box(Matrix4x4.Translate(new Vector3(Mathf.Cos(a) * rr, top + 0.0005f, Mathf.Sin(a) * rr)), new Vector3(cr * 0.14f, 0.002f, cr * 0.14f), new Color(0.15f, 0.15f, 0.16f));
                    }
                    for (int i = -1; i <= 1; i += 2)
                        mb.Box(Matrix4x4.Translate(new Vector3(i * cr * 0.3f, top + 0.003f, cr * 0.55f)) * Matrix4x4.Rotate(Quaternion.Euler(0f, i * 30f, 0f)),
                               new Vector3(cr * 0.28f, 0.003f, cr * 0.7f), new Color(0.40f, 0.52f, 0.22f));
                    break;
                case Icon.Thyme:    // дырочки посыпалки + веточка тимьяна сверху
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i / 7f * 6.283f, rr = i == 0 ? 0f : cr * 0.5f;
                        mb.Box(Matrix4x4.Translate(new Vector3(Mathf.Cos(a) * rr, top + 0.0005f, Mathf.Sin(a) * rr)), new Vector3(cr * 0.14f, 0.002f, cr * 0.14f), new Color(0.15f, 0.15f, 0.16f));
                    }
                    mb.Box(Matrix4x4.Translate(new Vector3(0f, top + 0.003f, cr * 0.5f)), new Vector3(cr * 0.06f, 0.003f, cr * 0.9f), new Color(0.45f, 0.36f, 0.22f));
                    for (int i = 0; i < 4; i++)
                        for (int sd = -1; sd <= 1; sd += 2)
                            mb.Box(Matrix4x4.Translate(new Vector3(sd * cr * 0.1f, top + 0.004f, cr * (0.15f + i * 0.2f))), new Vector3(cr * 0.12f, 0.003f, cr * 0.08f), new Color(0.38f, 0.50f, 0.30f));
                    break;
                case Icon.Nutmeg:   // дырочки посыпалки + орешек сверху
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i / 7f * 6.283f, rr = i == 0 ? 0f : cr * 0.5f;
                        mb.Box(Matrix4x4.Translate(new Vector3(Mathf.Cos(a) * rr, top + 0.0005f, Mathf.Sin(a) * rr)), new Vector3(cr * 0.14f, 0.002f, cr * 0.14f), new Color(0.15f, 0.15f, 0.16f));
                    }
                    mb.Lathe(Matrix4x4.Translate(new Vector3(cr * 0.55f, top, cr * 0.55f)), Sphere(cr * 0.22f, 1.25f), 10, y => new Color(0.50f, 0.32f, 0.18f), false, false);
                    break;
                case Icon.Garlic:   // дырочки посыпалки + головка чеснока
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i / 7f * 6.283f, rr = i == 0 ? 0f : cr * 0.5f;
                        mb.Box(Matrix4x4.Translate(new Vector3(Mathf.Cos(a) * rr, top + 0.0005f, Mathf.Sin(a) * rr)), new Vector3(cr * 0.14f, 0.002f, cr * 0.14f), new Color(0.15f, 0.15f, 0.16f));
                    }
                    break;
            }
            Mesh m = DS.SaveMesh(mb.Build(b.Name + "_Mesh"), b.Name + "_Mesh", log);
            MeshFilter mf = DS.Need<MeshFilter>(go);
            mf.sharedMesh = m;
            MeshRenderer mr = DS.Need<MeshRenderer>(go);
            mr.sharedMaterial = mat;
            BoxCollider bc = DS.Need<BoxCollider>(go);
            bc.center = m.bounds.center; bc.size = m.bounds.size;
            Rigidbody rb = DS.Need<Rigidbody>(go);
            rb.mass = 0.6f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            Transform sp = DS.FindOrCreate(go.transform, "Spout");
            sp.localPosition = new Vector3(0f, cy + ch, 0f);
            sp.localRotation = Quaternion.Euler(-90f, 0f, 0f);         // синяя ось — вверх из горлышка

            // этикетка
            float y0 = H * (b.Jar ? 0.2f : 0.17f), y1 = H * (b.Jar ? 0.74f : 0.55f);
            Texture2D tex = LabelTexture(b, log);
            Material lm = LabelMat(b.Name, tex, log);
            Transform lt = DS.FindOrCreate(go.transform, "Label");
            lt.localPosition = Vector3.zero; lt.localRotation = Quaternion.identity; lt.localScale = Vector3.one;
            Mesh lmesh = b.Segs == 4 ? FlatLabel(R * 0.7071f + 0.0015f, R * 0.7071f * 1.7f, y0, y1) : BandLabel(R * 1.012f, y0, y1, 230f);
            lmesh = DS.SaveMesh(lmesh, b.Name + "_LabelMesh", log);
            (DS.Need<MeshFilter>(lt.gameObject)).sharedMesh = lmesh;
            (DS.Need<MeshRenderer>(lt.gameObject)).sharedMaterial = lm;
            lt.gameObject.layer = go.layer;
        }

        private static List<Vector2> Sphere(float r, float squash)
        {
            var l = new List<Vector2>();
            for (int i = 0; i <= 8; i++) { float a = -Mathf.PI * 0.5f + Mathf.PI * i / 8f; l.Add(new Vector2(Mathf.Cos(a) * r, (Mathf.Sin(a) + 1f) * r * squash)); }
            return l;
        }

        /// <summary>Этикетка-полоса вокруг бутылки (дуга спереди, по +Z). UV: u — по дуге, v — вверх.</summary>
        private static Mesh BandLabel(float r, float y0, float y1, float arcDeg)
        {
            const int seg = 24;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            // снаружи (смотришь с +Z) u растёт вправо, т.е. к −X — иначе надпись зеркальная
            float a0 = Mathf.Deg2Rad * (90f - arcDeg * 0.5f), a1 = Mathf.Deg2Rad * (90f + arcDeg * 0.5f);
            for (int i = 0; i <= seg; i++)
            {
                float k = i / (float)seg, a = Mathf.Lerp(a0, a1, k);
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                v.Add(d + Vector3.up * y0); uv.Add(new Vector2(k, 0f));
                v.Add(d + Vector3.up * y1); uv.Add(new Vector2(k, 1f));
            }
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2, b = a + 2;
                float am = Mathf.Lerp(a0, a1, (i + 0.5f) / seg);
                Vector3 outw = new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am));
                AddTri(v, t, a, b, a + 1, outw); AddTri(v, t, b, b + 1, a + 1, outw);
            }
            var m = new Mesh { name = "Label" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        private static Mesh FlatLabel(float z, float w, float y0, float y1)
        {
            var v = new List<Vector3> { new Vector3(-w * 0.5f, y0, z), new Vector3(w * 0.5f, y0, z), new Vector3(w * 0.5f, y1, z), new Vector3(-w * 0.5f, y1, z) };
            var uv = new List<Vector2> { new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            var t = new List<int>();
            AddTri(v, t, 0, 1, 2, Vector3.forward); AddTri(v, t, 0, 2, 3, Vector3.forward);
            var m = new Mesh { name = "Label" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        private static void AddTri(List<Vector3> v, List<int> t, int a, int b, int c, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(n, outward) < 0f) { int x = b; b = c; c = x; }
            t.Add(a); t.Add(b); t.Add(c);
        }

        private static Material LabelMat(string name, Texture2D tex, StringBuilder log)
        {
            string path = Dir + "/Mat_Label_" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool fresh = m == null;
            if (fresh)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m = new Material(sh) { name = "Mat_Label_" + name };
            }
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
            if (fresh) { DS.EnsureDir(Dir); AssetDatabase.CreateAsset(m, path); log.AppendLine($"  ＋ материал {path}"); }
            else EditorUtility.SetDirty(m);
            return m;
        }

        // ───────── этикетка: фон, значок, надпись (шрифт LegacyRuntime — с кириллицей) ─────────

        private const int LW = 512, LH = 256;

        private static Texture2D LabelTexture(Bottle b, StringBuilder log)
        {
            var px = new Color[LW * LH];
            for (int i = 0; i < px.Length; i++) px[i] = b.LabelBg;
            // полосы сверху и снизу
            FillRect(px, 0, LH - 22, LW, 22, b.LabelAccent);
            FillRect(px, 0, 0, LW, 22, b.LabelAccent);
            FillRect(px, 0, LH - 30, LW, 4, Color.Lerp(b.LabelAccent, Color.black, 0.3f));
            FillRect(px, 0, 26, LW, 4, Color.Lerp(b.LabelAccent, Color.black, 0.3f));
            // значок слева и справа (видно с любой стороны дуги)
            DrawIcon(px, b.Icon, 78, 128, 62, b);
            DrawIcon(px, b.Icon, LW - 78, 128, 62, b);
            // надпись по центру
            bool ok = DrawText(px, b.Line1, new RectInt(150, 120, 212, 92), b.LabelFg, log);
            if (!string.IsNullOrEmpty(b.Line2)) ok &= DrawText(px, b.Line2, new RectInt(160, 44, 192, 70), Color.Lerp(b.LabelFg, b.LabelAccent, 0.35f), log);
            if (!ok) log.AppendLine($"  ⚠ надпись «{b.Line1} {b.Line2}» не нарисовалась (шрифт) — значок и цвета есть");
            string path = Dir + "/Label_" + b.Name + ".png";
            var tex = new Texture2D(LW, LH, TextureFormat.RGBA32, true);
            tex.SetPixels(px); tex.Apply();
            DS.EnsureDir(Dir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void FillRect(Color[] px, int x, int y, int w, int h, Color c)
        {
            for (int j = Mathf.Max(0, y); j < Mathf.Min(LH, y + h); j++)
                for (int i = Mathf.Max(0, x); i < Mathf.Min(LW, x + w); i++) px[j * LW + i] = c;
        }

        /// <summary>Плавное наложение цвета по маске 0..1.</summary>
        private static void Blend(Color[] px, int x, int y, Color c, float a)
        {
            if (x < 0 || y < 0 || x >= LW || y >= LH || a <= 0f) return;
            int i = y * LW + x;
            px[i] = Color.Lerp(px[i], c, Mathf.Clamp01(a) * c.a);
        }

        private delegate float Shape(float x, float y);    // расстояние со знаком: < 0 — внутри

        private static void DrawShape(Color[] px, int cx, int cy, int r, Shape sdf, Color c)
        {
            for (int y = cy - r * 2; y <= cy + r * 2; y++)
                for (int x = cx - r * 2; x <= cx + r * 2; x++)
                {
                    float d = sdf(x - cx, y - cy);
                    Blend(px, x, y, c, Mathf.Clamp01(0.5f - d));
                }
        }

        private static float Circle(float x, float y, float r) => Mathf.Sqrt(x * x + y * y) - r;
        private static float Box(float x, float y, float hx, float hy, float round)
        {
            float qx = Mathf.Abs(x) - hx + round, qy = Mathf.Abs(y) - hy + round;
            return Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - round;
        }
        private static float Drop(float x, float y, float r)
        {
            // капля: круг снизу + острый верх
            float c = Circle(x, y + r * 0.25f, r * 0.75f);
            float k = Mathf.Abs(x) * 1.25f + (y - r * 0.9f) * 0.85f;   // конус вверх
            return Mathf.Min(c, Mathf.Max(k, -(y + r * 0.25f)));   // конус — только над центром круга
        }

        private static void DrawIcon(Color[] px, Icon icon, int cx, int cy, int r, Bottle b)
        {
            Color dark = new Color(0.12f, 0.08f, 0.06f);
            switch (icon)
            {
                case Icon.Apple:
                    DrawShape(px, cx, cy, r, (x, y) => Mathf.Min(Circle(x - r * 0.28f, y + r * 0.05f, r * 0.62f), Circle(x + r * 0.28f, y + r * 0.05f, r * 0.62f)), new Color(0.86f, 0.12f, 0.10f));
                    DrawShape(px, cx, cy, r, (x, y) => Circle(x - r * 0.32f, y - r * 0.22f, r * 0.14f), new Color(1f, 1f, 1f, 0.75f));
                    DrawShape(px, cx, cy, r, (x, y) => Box(x, y - r * 0.68f, r * 0.06f, r * 0.2f, 0f), new Color(0.40f, 0.25f, 0.12f));
                    DrawShape(px, cx, cy, r, (x, y) => Circle((x - r * 0.28f) * 0.7f, (y - r * 0.78f) * 1.6f, r * 0.2f), new Color(0.30f, 0.66f, 0.22f));
                    break;
                case Icon.AppleDrop:
                    DrawShape(px, cx - r / 4, cy, r, (x, y) => Mathf.Min(Circle(x - r * 0.22f, y, r * 0.5f), Circle(x + r * 0.22f, y, r * 0.5f)), new Color(0.86f, 0.12f, 0.10f));
                    DrawShape(px, cx - r / 4, cy, r, (x, y) => Box(x, y - r * 0.55f, r * 0.05f, r * 0.16f, 0f), new Color(0.40f, 0.25f, 0.12f));
                    DrawShape(px, cx + r / 2, cy - r / 3, r / 2, (x, y) => Drop(x, y, r * 0.42f), b.LabelAccent);
                    break;
                case Icon.Barrel:
                    DrawShape(px, cx, cy, r, (x, y) => Box(x * (1f + 0.25f * (y / r) * (y / r)), y, r * 0.62f, r * 0.82f, r * 0.2f), new Color(0.62f, 0.36f, 0.16f));
                    for (int i = -1; i <= 1; i++)
                    {
                        int yy = i * r / 2;
                        DrawShape(px, cx, cy + yy, r, (x, y) => Box(x, y, r * (0.7f - Mathf.Abs(i) * 0.08f), r * 0.07f, 0f), new Color(0.28f, 0.28f, 0.30f));
                    }
                    for (int i = -1; i <= 1; i += 2) DrawShape(px, cx + i * r / 4, cy, r, (x, y) => Box(x, y, r * 0.02f, r * 0.75f, 0f), new Color(0.45f, 0.25f, 0.10f));
                    break;
                case Icon.Drop:
                    DrawShape(px, cx, cy, r, (x, y) => Drop(x, y, r * 0.78f), dark);
                    DrawShape(px, cx, cy, r, (x, y) => Circle(x + r * 0.2f, y + r * 0.05f, r * 0.13f), new Color(1f, 1f, 1f, 0.6f));
                    break;
                case Icon.Honeycomb:
                    for (int q = -1; q <= 1; q++)
                        for (int w = -1; w <= 1; w++)
                        {
                            if (Mathf.Abs(q + w) > 1) continue;
                            float hx = (q + w * 0.5f) * r * 0.62f, hy = w * r * 0.54f;
                            int ix = cx + Mathf.RoundToInt(hx), iy = cy + Mathf.RoundToInt(hy);
                            DrawShape(px, ix, iy, r / 2, (x, y) => Hex(x, y, r * 0.3f), b.LabelAccent);
                            DrawShape(px, ix, iy, r / 2, (x, y) => Hex(x, y, r * 0.22f), Color.Lerp(b.LabelAccent, new Color(1f, 0.9f, 0.4f), 0.5f));
                        }
                    break;
                case Icon.Flame:
                    DrawShape(px, cx, cy - r / 6, r, (x, y) => Drop(x, y, r * 0.82f), new Color(0.92f, 0.18f, 0.05f));
                    DrawShape(px, cx, cy - r / 3, r, (x, y) => Drop(x, y, r * 0.5f), new Color(0.99f, 0.60f, 0.10f));
                    DrawShape(px, cx, cy - r / 2, r, (x, y) => Drop(x, y, r * 0.25f), new Color(1f, 0.92f, 0.45f));
                    break;
                case Icon.Leaf:       // лавровый лист: вытянутый овал + прожилка
                    DrawShape(px, cx, cy, r, (x, y) => Circle(x * 2.1f, y * 0.95f, r * 0.85f) / 2.1f, new Color(0.36f, 0.50f, 0.20f));
                    DrawShape(px, cx, cy, r, (x, y) => Box(x, y, r * 0.025f, r * 0.75f, 0f), new Color(0.72f, 0.80f, 0.45f));
                    for (int i = -2; i <= 2; i++)
                    {
                        int yy = i * r / 4;
                        DrawShape(px, cx, cy + yy, r, (x, y) => Box(x, y - Mathf.Abs(x) * 0.6f, r * 0.24f, r * 0.012f, 0f), new Color(0.62f, 0.72f, 0.38f));
                    }
                    break;
                case Icon.Sunflower:  // подсолнух: лепестки кругом и тёмная серединка
                    for (int i = 0; i < 12; i++)
                    {
                        float a = i / 12f * 6.283f;
                        int px0 = cx + Mathf.RoundToInt(Mathf.Cos(a) * r * 0.55f), py0 = cy + Mathf.RoundToInt(Mathf.Sin(a) * r * 0.55f);
                        float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                        DrawShape(px, px0, py0, r / 2, (x, y) => Circle((x * ca + y * sa) * 0.8f, (-x * sa + y * ca) * 2.2f, r * 0.2f), new Color(0.99f, 0.80f, 0.10f));
                    }
                    DrawShape(px, cx, cy, r, (x, y) => Circle(x, y, r * 0.38f), new Color(0.35f, 0.20f, 0.08f));
                    DrawShape(px, cx, cy, r, (x, y) => Circle(x - r * 0.1f, y - r * 0.1f, r * 0.08f), new Color(0.55f, 0.36f, 0.16f));
                    break;
                case Icon.Thyme:      // веточка: стебель и парные листики
                    DrawShape(px, cx, cy, r, (x, y) => Box(x + y * 0.15f, y, r * 0.03f, r * 0.8f, 0f), new Color(0.45f, 0.34f, 0.20f));
                    for (int i = -3; i <= 3; i++)
                        for (int sd = -1; sd <= 1; sd += 2)
                        {
                            int lx = cx + sd * r / 5 - i * r / 40, ly = cy + i * r / 4;
                            float ang = sd * 0.6f;
                            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                            DrawShape(px, lx, ly, r / 2, (x, y) => Circle((x * ca + y * sa) * 1.0f, (-x * sa + y * ca) * 2.0f, r * 0.13f), new Color(0.40f, 0.56f, 0.30f));
                        }
                    break;
                case Icon.Nutmeg:     // мускатный орех: овал с сетчатым узором
                    DrawShape(px, cx, cy, r, (x, y) => Circle(x, y * 1.25f, r * 0.7f), new Color(0.52f, 0.33f, 0.18f));
                    for (int i = -2; i <= 2; i++)
                        DrawShape(px, cx + i * r / 4, cy, r, (x, y) => Box(x + Mathf.Sin(y * 0.08f) * r * 0.05f, y, r * 0.02f, r * 0.5f, 0f), new Color(0.72f, 0.52f, 0.30f));
                    DrawShape(px, cx, cy, r, (x, y) => Circle(x + r * 0.25f, y * 1.25f - r * 0.25f, r * 0.12f), new Color(1f, 1f, 1f, 0.35f));
                    break;
                case Icon.Garlic:
                    DrawShape(px, cx, cy - r / 8, r, (x, y) => Mathf.Min(Circle(x, y + r * 0.15f, r * 0.6f), Drop(x, y, r * 0.75f)), new Color(0.98f, 0.96f, 0.90f));
                    for (int i = -1; i <= 1; i++) DrawShape(px, cx + i * r / 4, cy - r / 6, r, (x, y) => Box(x, y, r * 0.02f, r * 0.42f, 0f), new Color(0.75f, 0.65f, 0.75f));
                    DrawShape(px, cx, cy - r * 3 / 4, r, (x, y) => Box(x, y, r * 0.35f, r * 0.05f, r * 0.04f), new Color(0.65f, 0.55f, 0.45f));
                    break;
            }
        }

        private static float Hex(float x, float y, float r)
        {
            x = Mathf.Abs(x); y = Mathf.Abs(y);
            return Mathf.Max(x * 0.866f + y * 0.5f, y) - r;
        }

        /// <summary>Надпись шрифтом движка (LegacyRuntime.ttf, кириллица) в прямоугольник этикетки — по центру, вписать.</summary>
        private static bool DrawText(Color[] px, string text, RectInt area, Color col, StringBuilder log)
        {
            if (string.IsNullOrEmpty(text)) return true;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) return false;
            const int size = 72;
            font.RequestCharactersInTexture(text, size, FontStyle.Bold);
            Texture src = font.material != null ? font.material.mainTexture : null;
            if (src == null) return false;
            // атлас шрифта не читается напрямую — копия через RenderTexture
            RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var atlas = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            atlas.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            atlas.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color[] ap = atlas.GetPixels();
            int aw = atlas.width, ah = atlas.height;
            Object.DestroyImmediate(atlas);
            // атлас Alpha8: покрытие в альфе; если альфа после копии пропала — берём красный канал
            float amin = 1f, amax = 0f;
            for (int i = 0; i < ap.Length; i += 7) { amin = Mathf.Min(amin, ap[i].a); amax = Mathf.Max(amax, ap[i].a); }
            bool useAlpha = amax - amin > 0.1f;

            // строка в буфер на родном размере
            int width = 0;
            var infos = new List<CharacterInfo>();
            foreach (char ch in text)
            {
                if (!font.GetCharacterInfo(ch, out CharacterInfo ci, size, FontStyle.Bold)) { infos.Add(default(CharacterInfo)); width += size / 3; continue; }
                infos.Add(ci); width += ci.advance;
            }
            int bh = Mathf.RoundToInt(size * 1.3f), base0 = Mathf.RoundToInt(size * 0.28f);
            if (width <= 0) return false;
            var buf = new float[width * bh];
            int pen = 0;
            for (int k = 0; k < infos.Count; k++)
            {
                CharacterInfo ci = infos[k];
                int gw = ci.maxX - ci.minX, gh = ci.maxY - ci.minY;
                if (gw > 0 && gh > 0)
                    for (int gy = 0; gy < gh; gy++)
                        for (int gx = 0; gx < gw; gx++)
                        {
                            float fx = (gx + 0.5f) / gw, fy = (gy + 0.5f) / gh;
                            Vector2 uv = ci.uvBottomLeft + (ci.uvBottomRight - ci.uvBottomLeft) * fx + (ci.uvTopLeft - ci.uvBottomLeft) * fy;
                            int ax = Mathf.Clamp(Mathf.FloorToInt(uv.x * aw), 0, aw - 1), ay = Mathf.Clamp(Mathf.FloorToInt(uv.y * ah), 0, ah - 1);
                            Color a = ap[ay * aw + ax];
                            float cov = useAlpha ? a.a : a.r;
                            int x = pen + ci.minX + gx, y = base0 + ci.minY + gy;
                            if (x >= 0 && y >= 0 && x < width && y < bh) buf[y * width + x] = Mathf.Max(buf[y * width + x], cov);
                        }
                pen += ci.advance > 0 ? ci.advance : size / 3;
            }
            // вписать в прямоугольник по центру
            float scale = Mathf.Min(area.width / (float)width, area.height / (float)bh);
            int ow = Mathf.RoundToInt(width * scale), oh = Mathf.RoundToInt(bh * scale);
            int ox = area.x + (area.width - ow) / 2, oy = area.y + (area.height - oh) / 2;
            Color shadow = new Color(0f, 0f, 0f, 0.35f);
            for (int pass = 0; pass < 2; pass++)
                for (int y = 0; y < oh; y++)
                    for (int x = 0; x < ow; x++)
                    {
                        float sx = x / scale, sy = y / scale;
                        int ix = Mathf.Clamp((int)sx, 0, width - 1), iy = Mathf.Clamp((int)sy, 0, bh - 1);
                        int ix1 = Mathf.Min(ix + 1, width - 1), iy1 = Mathf.Min(iy + 1, bh - 1);
                        float tx = sx - ix, ty = sy - iy;
                        float v = Mathf.Lerp(Mathf.Lerp(buf[iy * width + ix], buf[iy * width + ix1], tx), Mathf.Lerp(buf[iy1 * width + ix], buf[iy1 * width + ix1], tx), ty);
                        if (pass == 0) Blend(px, ox + x + 2, oy + y - 2, shadow, v);
                        else Blend(px, ox + x, oy + y, col, v);
                    }
            return true;
        }

        [MenuItem("CatCooks/Барбекю/3б. Мешок коричневого сахара (выдели мешок сахара)", priority = 143)]
        public static void BrownSugarSack()
        {
            var log = new StringBuilder();
            ProductDef brown = DS.Find(BBQTitles.BrownSugar);
            GameObject sel = Selection.activeGameObject;
            BulkSack sack = sel != null ? sel.GetComponentInParent<BulkSack>() : null;
            if (brown == null || sack == null) { EditorUtility.DisplayDialog("Барбекю", "Выдели в сцене мешок сахара (Bulk Sack). Продукты — меню 1.", "Ок"); return; }
            GameObject copy = Object.Instantiate(sack.gameObject, sack.transform.position + sack.transform.right * 0.8f, sack.transform.rotation);
            copy.name = "BrownSugarSack";
            Undo.RegisterCreatedObjectUndo(copy, "BrownSugarSack");
            BulkSack bs = copy.GetComponent<BulkSack>();
            var so = new SerializedObject(bs);
            DS.SetObj(so, "_product", brown);
            so.ApplyModifiedProperties();
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>())
            {
                if (r.GetComponentInParent<FoodHeap>() != null) continue;
                var mpbMat = new Material(r.sharedMaterial) { name = "Mat_BrownSugarSack" };
                Color c = new Color(0.72f, 0.52f, 0.30f);
                if (mpbMat.HasProperty("_BaseColor")) mpbMat.SetColor("_BaseColor", Color.Lerp(mpbMat.GetColor("_BaseColor"), c, 0.55f));
                else if (mpbMat.HasProperty("_Color")) mpbMat.SetColor("_Color", Color.Lerp(mpbMat.color, c, 0.55f));
                DS.EnsureDir(Dir);
                string mp = AssetDatabase.GenerateUniqueAssetPath(Dir + "/Mat_BrownSugarSack.mat");
                AssetDatabase.CreateAsset(mpbMat, mp);
                r.sharedMaterial = mpbMat;
                break;
            }
            Selection.activeGameObject = copy;
            EditorSceneManager.MarkSceneDirty(copy.scene);
            log.AppendLine($"＋ «BrownSugarSack» — копия «{sack.name}», Product = {BBQTitles.BrownSugar}, мешок подкрашен");
            log.AppendLine("Проверь у копии Bulk Sack → Mouth: должен указывать на её ребёнка (не на оригинал).");
            DS.Done("Коричневый сахар", log);
        }

        // ───────────────────────── 4–7. ИНСТРУМЕНТЫ ─────────────────────────

        [MenuItem("CatCooks/Барбекю/4. Клещи (выдели модель или сделаю)", priority = 144)]
        public static void MakeTongs()
        {
            var log = new StringBuilder();
            GameObject go = Selection.activeGameObject;
            Transform jawA = null, jawB = null;
            if (go == null)
            {
                go = new GameObject("Tongs");
                Undo.RegisterCreatedObjectUndo(go, "Tongs");
                go.transform.position = At();
                Material mat = DS.PlateMat(log);
                Color steel = new Color(0.66f, 0.69f, 0.73f), grip = new Color(0.95f, 0.45f, 0.10f);
                const float L = 0.42f;
                for (int s = -1; s <= 1; s += 2)
                {
                    var arm = new GameObject(s < 0 ? "JawA" : "JawB");
                    arm.transform.SetParent(go.transform, false);
                    arm.transform.localRotation = Quaternion.Euler(0f, s * 5f, 0f);
                    var mb = new MB();
                    mb.Box(Matrix4x4.Translate(new Vector3(0f, 0f, L * 0.5f)), new Vector3(0.012f, 0.02f, L), steel);
                    mb.Box(Matrix4x4.Translate(new Vector3(0f, 0f, L * 0.22f)), new Vector3(0.02f, 0.03f, L * 0.36f), grip);
                    mb.Box(Matrix4x4.Translate(new Vector3(-s * 0.008f, 0f, L * 0.98f)), new Vector3(0.022f, 0.035f, 0.04f), steel);   // губка
                    Mesh m = DS.SaveMesh(mb.Build("Tongs_" + arm.name), "Tongs_" + arm.name, log);
                    arm.AddComponent<MeshFilter>().sharedMesh = m;
                    arm.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    if (s < 0) jawA = arm.transform; else jawB = arm.transform;
                }
                Transform tip = DS.FindOrCreate(go.transform, "Tip");
                tip.localPosition = new Vector3(0f, 0f, L);
                BoxCollider bc = go.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, 0f, L * 0.5f); bc.size = new Vector3(0.09f, 0.04f, L);
                log.AppendLine("＋ «Tongs» — стальные клещи с оранжевыми ручками (JawA/JawB, Tip)");
            }
            Transform t = go.transform.Find("Tip");
            if (t == null) { t = DS.FindOrCreate(go.transform, "Tip"); t.position = FarEnd(go); log.AppendLine("  ＋ Tip — на дальнем конце модели (подвинь между губками)"); }
            ToolBase(go, 0.6f, log);
            TongsItem ti = DS.Need<TongsItem>(go);
            ti.EditorSetup(t);
            if (jawA != null || jawB != null)
            {
                var so = new SerializedObject(ti);
                DS.SetObj(so, "_jawA", jawA); DS.SetObj(so, "_jawB", jawB);
                so.ApplyModifiedProperties();
            }
            Finish(go, "Клещи", "ЛКМ по рёбрам — перевернуть; ПКМ — взять/отпустить; взятые ЛКМ — поставить на решётку/стол", log);
        }

        [MenuItem("CatCooks/Барбекю/5. Пульверизатор (выдели модель или сделаю)", priority = 145)]
        public static void MakeSpray()
        {
            var log = new StringBuilder();
            GameObject go = Selection.activeGameObject;
            if (go == null)
            {
                go = new GameObject("SprayBottle");
                Undo.RegisterCreatedObjectUndo(go, "SprayBottle");
                go.transform.position = At();
                var mb = new MB();
                Color body = new Color(0.80f, 0.90f, 0.95f), head = new Color(0.98f, 0.55f, 0.10f), black = new Color(0.12f, 0.12f, 0.13f);
                mb.Lathe(Matrix4x4.identity, new List<Vector2> { new Vector2(0f, 0f), new Vector2(0.05f, 0f), new Vector2(0.052f, 0.02f), new Vector2(0.05f, 0.2f), new Vector2(0.03f, 0.23f), new Vector2(0.018f, 0.25f) }, 18, y => y > 0.06f && y < 0.15f ? new Color(0.2f, 0.65f, 0.3f) : body, true, true);
                mb.Box(Matrix4x4.Translate(new Vector3(0f, 0.27f, 0.015f)), new Vector3(0.04f, 0.05f, 0.08f), head);
                mb.Box(Matrix4x4.Translate(new Vector3(0f, 0.28f, 0.065f)), new Vector3(0.018f, 0.018f, 0.025f), black);
                mb.Box(Matrix4x4.Translate(new Vector3(0f, 0.225f, 0.045f)) * Matrix4x4.Rotate(Quaternion.Euler(20f, 0f, 0f)), new Vector3(0.016f, 0.05f, 0.012f), head);   // курок
                Mesh m = DS.SaveMesh(mb.Build("SprayBottle_Mesh"), "SprayBottle_Mesh", log);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                go.AddComponent<MeshRenderer>().sharedMaterial = DS.PlateMat(log);
                Transform nz = DS.FindOrCreate(go.transform, "Nozzle");
                nz.localPosition = new Vector3(0f, 0.28f, 0.08f); nz.localRotation = Quaternion.identity;
                log.AppendLine("＋ «SprayBottle» — бутылка с оранжевой головкой, Nozzle смотрит вперёд (синяя ось)");
            }
            Transform n = go.transform.Find("Nozzle");
            if (n == null)
            {
                n = DS.FindOrCreate(go.transform, "Nozzle");
                Bounds b = DS.LocalBounds(go.transform);
                n.localPosition = new Vector3(b.center.x, b.max.y - b.size.y * 0.08f, b.max.z);
                n.localRotation = Quaternion.identity;
                log.AppendLine("  ＋ Nozzle — сверху спереди модели, синяя ось вперёд (подвинь к соплу)");
            }
            ToolBase(go, 0.5f, log);
            SprayBottleItem sb = DS.Need<SprayBottleItem>(go);
            sb.EditorSetup(n);
            Finish(go, "Пульверизатор", "ЛКМ по ведру с моп-соусом — заправить; ЛКМ по рёбрам — пшик", log);
        }

        [MenuItem("CatCooks/Барбекю/6. Кисть (выдели модель или сделаю строительную)", priority = 146)]
        public static void MakeBrush()
        {
            var log = new StringBuilder();
            GameObject go = Selection.activeGameObject;
            Renderer bristles = null;
            if (go == null)
            {
                go = new GameObject("BastingBrush");
                Undo.RegisterCreatedObjectUndo(go, "BastingBrush");
                go.transform.position = At();
                Material mat = DS.PlateMat(log);
                var mb = new MB();
                Color wood = new Color(0.80f, 0.58f, 0.33f), metal = new Color(0.72f, 0.74f, 0.78f), red = new Color(0.85f, 0.18f, 0.12f);
                mb.Box(Matrix4x4.Translate(new Vector3(0f, 0.17f, 0f)), new Vector3(0.03f, 0.2f, 0.024f), wood);
                mb.Box(Matrix4x4.Translate(new Vector3(0f, 0.25f, 0f)), new Vector3(0.032f, 0.03f, 0.026f), red);
                mb.Box(Matrix4x4.Translate(new Vector3(0f, 0.06f, 0f)), new Vector3(0.11f, 0.03f, 0.03f), metal);
                Mesh m = DS.SaveMesh(mb.Build("Brush_Handle"), "Brush_Handle", log);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                var br = new GameObject("Bristles");
                br.transform.SetParent(go.transform, false);
                var mb2 = new MB();
                mb2.Box(Matrix4x4.Translate(new Vector3(0f, 0.015f, 0f)), new Vector3(0.105f, 0.065f, 0.026f), new Color(0.88f, 0.80f, 0.62f));
                Mesh bm = DS.SaveMesh(mb2.Build("Brush_Bristles"), "Brush_Bristles", log);
                br.AddComponent<MeshFilter>().sharedMesh = bm;
                bristles = br.AddComponent<MeshRenderer>();
                bristles.sharedMaterial = mat;
                log.AppendLine("＋ «BastingBrush» — толстая строительная кисть (Bristles — щетина, краснеет в соусе)");
            }
            if (bristles == null)
            {
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                {
                    string nm = r.name.ToLowerInvariant();
                    if (nm.Contains("bristle") || nm.Contains("щет") || nm.Contains("ворс")) { bristles = r; break; }
                }
                if (bristles == null) { bristles = go.GetComponentInChildren<Renderer>(); log.AppendLine("  ⚠ щетину не нашёл по имени — Bristles = весь рендер (поставь руками ребёнка-щетину)"); }
            }
            ToolBase(go, 0.4f, log);
            BastingBrushItem bb = DS.Need<BastingBrushItem>(go);
            bb.EditorSetup(bristles);
            Finish(go, "Кисть", "ЛКМ по таре с глазурью — макнуть; ЛКМ зажать на рёбрах и водить мышью — мазать", log);
        }

        [MenuItem("CatCooks/Барбекю/7. Электрорубанок (выдели модель или сделаю)", priority = 147)]
        public static void MakePlaner()
        {
            var log = new StringBuilder();
            GameObject go = Selection.activeGameObject;
            Transform blade = null;
            if (go == null)
            {
                go = new GameObject("ElectricPlaner");
                Undo.RegisterCreatedObjectUndo(go, "ElectricPlaner");
                go.transform.position = At();
                Material mat = DS.PlateMat(log);
                var mb = new MB();
                Color orange = new Color(0.98f, 0.52f, 0.08f), black = new Color(0.12f, 0.12f, 0.13f), steel = new Color(0.68f, 0.70f, 0.74f);
                mb.Box(Matrix4x4.Translate(new Vector3(0f, 0.012f, 0f)), new Vector3(0.09f, 0.024f, 0.28f), steel);          // подошва
                mb.Box(Matrix4x4.Translate(new Vector3(0f, 0.065f, -0.01f)), new Vector3(0.085f, 0.08f, 0.22f), orange);       // корпус
                mb.Box(Matrix4x4.Translate(new Vector3(0f, 0.13f, -0.06f)), new Vector3(0.04f, 0.05f, 0.14f), black);          // ручка
                mb.Box(Matrix4x4.Translate(new Vector3(0f, 0.11f, 0.09f)), new Vector3(0.05f, 0.06f, 0.04f), black);           // передняя ручка
                mb.Box(Matrix4x4.Translate(new Vector3(0.048f, 0.07f, 0.03f)), new Vector3(0.012f, 0.03f, 0.06f), black);      // выброс стружки
                Mesh m = DS.SaveMesh(mb.Build("Planer_Body"), "Planer_Body", log);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                var bl = new GameObject("Blade");
                bl.transform.SetParent(go.transform, false);
                bl.transform.localPosition = new Vector3(0f, 0.022f, 0.02f);
                var mb2 = new MB();
                mb2.Lathe(Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 90f)), new List<Vector2> { new Vector2(0f, -0.04f), new Vector2(0.018f, -0.04f), new Vector2(0.018f, 0.04f), new Vector2(0f, 0.04f) }, 6, y => steel, true, true);
                Mesh dm = DS.SaveMesh(mb2.Build("Planer_Drum"), "Planer_Drum", log);
                bl.AddComponent<MeshFilter>().sharedMesh = dm;
                bl.AddComponent<MeshRenderer>().sharedMaterial = mat;
                blade = bl.transform;
                log.AppendLine("＋ «ElectricPlaner» — оранжевый рубанок со стальной подошвой (Blade — барабан, крутится)");
            }
            if (blade == null)
                foreach (Transform c in go.GetComponentsInChildren<Transform>(true))
                {
                    string nm = c.name.ToLowerInvariant();
                    if (nm.Contains("blade") || nm.Contains("drum") || nm.Contains("нож") || nm.Contains("барабан")) { blade = c; break; }
                }
            ToolBase(go, 2.5f, log);
            PlanerItem pi = DS.Need<PlanerItem>(go);
            pi.EditorSetup(blade);
            Finish(go, "Электрорубанок", "ЛКМ зажать на сырых рёбрах и водить мышью — снять плёнку", log);
        }

        /// <summary>Общее для инструментов: Rigidbody, коллайдер, сеть.</summary>
        private static void ToolBase(GameObject go, float mass, StringBuilder log)
        {
            if (go.GetComponentInChildren<Collider>() == null)
            {
                BoxCollider bc = Undo.AddComponent<BoxCollider>(go);
                Bounds b = DS.LocalBounds(go.transform);
                bc.center = b.center; bc.size = b.size;
                log.AppendLine("  ＋ Box Collider по модели");
            }
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null) { rb = Undo.AddComponent<Rigidbody>(go); rb.mass = mass; rb.interpolation = RigidbodyInterpolation.Interpolate; log.AppendLine($"  ＋ Rigidbody {mass:0.#} кг"); }
            DS.AddByName(go, "NetworkObject", log);
            DS.AddByName(go, "NetworkTransform", log);
            foreach (PickupItem other in go.GetComponents<PickupItem>())
                if (!(other is TongsItem) && !(other is SprayBottleItem) && !(other is BastingBrushItem) && !(other is PlanerItem))
                    log.AppendLine($"  ⚠ на «{go.name}» уже есть {other.GetType().Name} — удали его, инструмент сам умеет брать в руку");
        }

        private static void Finish(GameObject go, string title, string how, StringBuilder log)
        {
            Selection.activeGameObject = go;
            EditorUtility.SetDirty(go);
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"✔ «{go.name}»: {title} настроен. {how}.");
            log.AppendLine("Слой — как у остальных инструментов (с детьми). Держать криво — Hold Position/Euler Offset. Сохрани префабом, если нужен.");
            DS.Done(title, log);
        }

        private static Vector3 FarEnd(GameObject go)
        {
            Bounds b = new Bounds(go.transform.position, Vector3.zero);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            Vector3 s = b.size;
            Vector3 axis = s.x >= s.y && s.x >= s.z ? Vector3.right : s.y >= s.z ? Vector3.up : Vector3.forward;
            Vector3 c1 = b.center + Vector3.Scale(axis, b.extents), c2 = b.center - Vector3.Scale(axis, b.extents);
            return (c1 - go.transform.position).sqrMagnitude > (c2 - go.transform.position).sqrMagnitude ? c1 : c2;
        }

        // ───────────────────────── 8. ВИД СОУСОВ В ТАРЕ ─────────────────────────

        [MenuItem("CatCooks/Барбекю/8. Соусы и специи в выделенной таре (вёдра, кастрюля, ковш)", priority = 148)]
        public static void AddMasses()
        {
            var log = new StringBuilder();
            var targets = new List<CookwareLiquidVisual>();
            foreach (GameObject go in Selection.gameObjects)
                foreach (CookwareLiquidVisual lv in go.GetComponentsInChildren<CookwareLiquidVisual>(true))
                    if (!targets.Contains(lv)) targets.Add(lv);
            if (targets.Count == 0) { EditorUtility.DisplayDialog("Барбекю", "Выдели вёдра, кастрюлю, ковш (с LiquidAnchor). Без LiquidAnchor — сначала «Десерты → 2б».", "Ок"); return; }
            Color own = new Color(0, 0, 0, 0);
            var rows = new[]
            {
                new DS.RoleRow(BBQTitles.Mop, CookwareLiquidVisual.Role.Liquid, own, "моп-соус"),
                new DS.RoleRow(BBQTitles.AppleJuice, CookwareLiquidVisual.Role.Liquid, own, "яблочный сок"),
                new DS.RoleRow(BBQTitles.AppleVinegar, CookwareLiquidVisual.Role.Liquid, own, "яблочный уксус"),
                new DS.RoleRow(BBQTitles.Bourbon, CookwareLiquidVisual.Role.Liquid, own, "бурбон"),
                new DS.RoleRow(BBQTitles.Worcester, CookwareLiquidVisual.Role.Liquid, own, "вустерский соус"),
                new DS.RoleRow(BBQTitles.Honey, CookwareLiquidVisual.Role.Thick, own, "мёд"),
                new DS.RoleRow(BBQTitles.Sauce, CookwareLiquidVisual.Role.Thick, own, "соус барбекю"),
                new DS.RoleRow(BBQTitles.Glaze, CookwareLiquidVisual.Role.Thick, own, "блестящая глазурь"),
                new DS.RoleRow(BBQTitles.Rub, CookwareLiquidVisual.Role.Paste, own, "сухой руб"),
                new DS.RoleRow(BBQTitles.Garlic, CookwareLiquidVisual.Role.Paste, own, "сушёный чеснок"),
                new DS.RoleRow(BBQTitles.BrownSugar, CookwareLiquidVisual.Role.Paste, own, "коричневый сахар"),
                new DS.RoleRow(BBQTitles.Paprika, CookwareLiquidVisual.Role.Paste, own, "паприка"),
                new DS.RoleRow(BBQTitles.Pepper, CookwareLiquidVisual.Role.Paste, own, "перец"),
                new DS.RoleRow(BBQTitles.Salt, CookwareLiquidVisual.Role.Paste, own, "соль"),
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
            DS.Done("Барбекю — тара", log);
        }

        // ───────────────────────── 9. СВЯЗАТЬ ─────────────────────────

        [MenuItem("CatCooks/Барбекю/9. Связать префабы рёбер и тушу (выдели тушу)", priority = 149)]
        public static void Link()
        {
            var log = new StringBuilder();
            GameObject rackP = null, sliceP = null;
            foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
            {
                GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                RibsRack r = p != null ? p.GetComponent<RibsRack>() : null;
                if (r == null || r.IsBrisket) continue;   // брискет — свой (Брискет → 5)
                int n = new SerializedObject(r).FindProperty("_ribs").intValue;
                if (n <= 1) { if (sliceP == null || p.name == "RibsSlice") sliceP = p; }
                else if (rackP == null || p.name == "RibsRack") rackP = p;
            }
            if (rackP == null) log.AppendLine("✖ нет префаба пласта рёбер (Ribs Rack, Ribs > 1) — меню 2 и сохрани RibsRack в Project");
            if (sliceP == null) log.AppendLine("✖ нет префаба одного ребра (Ribs Rack, Ribs = 1) — меню 2 и сохрани RibsSlice в Project");
            if (rackP != null && sliceP != null)
            {
                RibsRack rr = rackP.GetComponent<RibsRack>();
                var so = new SerializedObject(rr);
                DS.SetObj(so, "_slicePrefab", sliceP);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(rackP);
                log.AppendLine($"✔ «{rackP.name}» → Slice Prefab = «{sliceP.name}» (нарезка пилой)");
                // и пласты в сцене
                foreach (RibsRack s in Object.FindObjectsByType<RibsRack>(FindObjectsSortMode.None))
                {
                    if (s.IsBrisket) continue;
                    var o = new SerializedObject(s);
                    if (o.FindProperty("_ribs").intValue <= 1) continue;
                    DS.SetObj(o, "_slicePrefab", sliceP);
                    o.ApplyModifiedProperties();
                    log.AppendLine($"  ✔ в сцене «{s.name}» → Slice Prefab = «{sliceP.name}»");
                }
            }
            GameObject sel = Selection.activeGameObject;
            FoodCarcass c = sel != null ? sel.GetComponentInParent<FoodCarcass>() : null;
            if (c == null) c = Object.FindFirstObjectByType<FoodCarcass>();
            if (c != null && rackP != null)
            {
                int part = c.EditorFindRibsPart();
                c.EditorSetRibs(rackP, part);
                Transform pt = c.EditorPart(part);
                log.AppendLine(part >= 0 ? $"✔ туша «{c.name}»: кусок «{(pt != null ? pt.name : "?")}» (№{part}) → выпадает «{rackP.name}»"
                                         : $"⚠ туша «{c.name}»: части с «rib»/«ребр» в имени не нашёл — поставь Food Carcass → Ribs Part (номер куска) руками");
                if (PrefabUtility.IsPartOfPrefabInstance(c)) log.AppendLine("  туша — префаб: Overrides → Apply All");
                EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
            }
            else if (c == null) log.AppendLine("⚠ туши (Food Carcass) в сцене нет — рёбра можно положить в сцену готовым RibsRack");
            AssetDatabase.SaveAssets();
            DS.Done("Барбекю — связать", log);
        }

        // ───────────────────────── 10. ПРОВЕРКА ─────────────────────────

        [MenuItem("CatCooks/Барбекю/10. Проверить барбекю", priority = 150)]
        public static void Check()
        {
            _sb = new StringBuilder(); _err = 0; _warn = 0;
            StringBuilder sb = _sb;
            sb.AppendLine("Продукты:");
            foreach (string t in new[] { BBQTitles.Ribs, BBQTitles.Dish, BBQTitles.Garlic, BBQTitles.Rub, BBQTitles.AppleJuice, BBQTitles.AppleVinegar, BBQTitles.Bourbon,
                                         BBQTitles.Mop, BBQTitles.Sauce, BBQTitles.BrownSugar, BBQTitles.Honey, BBQTitles.Worcester, BBQTitles.Glaze,
                                         BBQTitles.Salt, BBQTitles.Paprika, BBQTitles.Pepper, BBQTitles.Water })
                if (DS.Find(t) != null) Ok(t); else Er($"нет «{t}» — меню 1");
            sb.AppendLine("Рецепты:");
            foreach (string t in new[] { "Сухой руб", "Моп-соус", "Глазурь барбекю" })
                if (AssetDatabase.LoadAssetAtPath<CookRecipeDef>($"{DS.RecipeDir}/Recipe_{t}.asset") != null) Ok(t); else Er($"нет рецепта «{t}» — меню 1");
            sb.AppendLine("Сцена:");
            RibsRack[] racks = Object.FindObjectsByType<RibsRack>(FindObjectsSortMode.None);
            if (racks.Length == 0) Wr("рёбер в сцене нет — меню 2 (или туша с рёбрами)");
            foreach (RibsRack r in racks)
            {
                var o = new SerializedObject(r);
                int n = o.FindProperty("_ribs").intValue;
                if (n > 1 && o.FindProperty("_slicePrefab").objectReferenceValue == null) Wr($"«{r.name}»: Slice Prefab пуст — пилой не нарезать (меню 9)");
                else Ok($"рёбра «{r.name}» ({n})");
            }
            // префабы пласта и ребра — одного размера (ребро = ширина пласта / число рёбер)
            {
                GameObject rp = null, sp = null;
                foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
                {
                    GameObject pg = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                    RibsRack pr = pg != null ? pg.GetComponent<RibsRack>() : null;
                    if (pr == null || pr.IsBrisket) continue;
                    int n = new SerializedObject(pr).FindProperty("_ribs").intValue;
                    if (n > 1 && (rp == null || pg.name == "RibsRack")) rp = pg;
                    if (n == 1 && (sp == null || pg.name == "RibsSlice")) sp = pg;
                }
                if (rp != null && sp != null)
                {
                    float a = new SerializedObject(rp.GetComponent<RibsRack>()).FindProperty("_ribWidth").floatValue;
                    float b = new SerializedObject(sp.GetComponent<RibsRack>()).FindProperty("_ribWidth").floatValue;
                    if (Mathf.Abs(a - b) < a * 0.05f) Ok($"префабы «{rp.name}» и «{sp.name}» одного размера (ребро {a * 100f:0} см)");
                    else Er($"префабы разного размера: ребро у «{rp.name}» {a * 100f:0} см, у «{sp.name}» {b * 100f:0} см — удали оба префаба и объекты, меню 2, перетащи заново, меню 9");
                }
            }
            if (Object.FindFirstObjectByType<TongsItem>() == null) Wr("нет клещей — меню 4"); else Ok("клещи");
            if (Object.FindFirstObjectByType<SprayBottleItem>() == null) Wr("нет пульверизатора — меню 5"); else Ok("пульверизатор");
            if (Object.FindFirstObjectByType<BastingBrushItem>() == null) Wr("нет кисти — меню 6"); else Ok("кисть");
            if (Object.FindFirstObjectByType<PlanerItem>() == null) Wr("нет рубанка — меню 7"); else Ok("рубанок");
            var have = new HashSet<string>();
            foreach (LiquidDoseJarItem j in Object.FindObjectsByType<LiquidDoseJarItem>(FindObjectsSortMode.None))
            {
                var p = new SerializedObject(j).FindProperty("_product").objectReferenceValue as ProductDef;
                if (p != null) have.Add(p.Title);
            }
            foreach (PowderShakerItem j in Object.FindObjectsByType<PowderShakerItem>(FindObjectsSortMode.None))
            {
                var p = new SerializedObject(j).FindProperty("_product").objectReferenceValue as ProductDef;
                if (p != null) have.Add(p.Title);
            }
            foreach (BulkSack s in Object.FindObjectsByType<BulkSack>(FindObjectsSortMode.None))
            {
                var p = new SerializedObject(s).FindProperty("_product").objectReferenceValue as ProductDef;
                if (p != null) have.Add(p.Title);
            }
            foreach (string t in new[] { BBQTitles.AppleJuice, BBQTitles.AppleVinegar, BBQTitles.Bourbon, BBQTitles.Worcester, BBQTitles.Honey, BBQTitles.Sauce,
                                         BBQTitles.Garlic, BBQTitles.Salt, BBQTitles.Paprika, BBQTitles.Pepper, BBQTitles.BrownSugar })
                if (have.Contains(t)) Ok($"источник «{t}»"); else Wr($"нет баночки/мешка «{t}» — меню 3 / 3б (соль, перец, паприка — свои баночки)");
            Ok("вода — из крана (бутылка не нужна)");
            foreach (LiquidDoseJarItem j in Object.FindObjectsByType<LiquidDoseJarItem>(FindObjectsSortMode.None))
                if (j.name == "Bottle_Water") Wr("в сцене осталась «Bottle_Water» — не нужна, меню 3 её уберёт (или удали)");
            foreach (Bottle b in BottleList())
            {
                GameObject go = FindInScene(b.Name);
                if (go != null && go.transform.Find("Label") == null) Wr($"«{b.Name}» без этикетки — нажми меню 3 ещё раз (обновит на месте)");
            }
            FoodCarcass c = Object.FindFirstObjectByType<FoodCarcass>();
            if (c != null)
            {
                var o = new SerializedObject(c);
                if (o.FindProperty("_ribsPrefab").objectReferenceValue == null) Wr($"туша «{c.name}»: рёбра выпадут старым куском — меню 9");
                else Ok($"туша «{c.name}» → рёбра барбекю");
            }
            if (Object.FindFirstObjectByType<BakeOven>() == null) Wr("нет духовки-бочки");
            sb.Insert(0, _err == 0 ? (_warn == 0 ? "ВСЁ ГОТОВО ✔\n\n" : $"Можно играть, замечаний: {_warn}\n\n") : $"Ошибок: {_err}, замечаний: {_warn}\n\n");
            DS.Done("Барбекю — проверка", sb);
        }

        private static StringBuilder _sb;
        private static int _err, _warn;
        private static void Ok(string t) => _sb.AppendLine("  ✔ " + t);
        private static void Wr(string t) { _warn++; _sb.AppendLine("  ⚠ " + t); }
        private static void Er(string t) { _err++; _sb.AppendLine("  ✖ " + t); }

        // ───────────────────────── общее ─────────────────────────

        private static Vector3 At() => SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

        /// <summary>Сборщик меша с цветом в вершинах: коробки и тела вращения, треугольники — всегда наружу.</summary>
        private sealed class MB
        {
            private readonly List<Vector3> _v = new List<Vector3>();
            private readonly List<Color> _c = new List<Color>();
            private readonly List<int> _t = new List<int>();

            public void Box(Matrix4x4 m, Vector3 size, Color col)
            {
                Vector3 h = size * 0.5f;
                Vector3[] ax = { Vector3.right, Vector3.up, Vector3.forward };
                Vector3 c0 = m.MultiplyPoint3x4(Vector3.zero);
                for (int i = 0; i < 3; i++)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector3 N = ax[i] * s, U = ax[(i + 1) % 3], V = ax[(i + 2) % 3];
                        float hn = h[i], hu = h[(i + 1) % 3], hv = h[(i + 2) % 3];
                        Vector3 o = N * hn;
                        int b = _v.Count;
                        foreach (Vector3 p in new[] { o - U * hu - V * hv, o + U * hu - V * hv, o + U * hu + V * hv, o - U * hu + V * hv })
                        { _v.Add(m.MultiplyPoint3x4(p)); _c.Add(col); }
                        Vector3 nw = m.MultiplyVector(N);
                        Tri(b, b + 1, b + 2, nw); Tri(b, b + 2, b + 3, nw);
                    }
            }

            /// <summary>Тело вращения: профиль (радиус, высота) снизу вверх вокруг Y. colorAt(y). Крышки снизу/сверху — по желанию.</summary>
            public void Lathe(Matrix4x4 m, List<Vector2> prof, int segs, System.Func<float, Color> colorAt, bool capBottom, bool capTop)
            {
                int start = _v.Count;
                for (int i = 0; i < prof.Count; i++)
                    for (int s = 0; s <= segs; s++)
                    {
                        float a = s / (float)segs * Mathf.PI * 2f;
                        Vector3 p = new Vector3(Mathf.Cos(a) * prof[i].x, prof[i].y, Mathf.Sin(a) * prof[i].x);
                        _v.Add(m.MultiplyPoint3x4(p)); _c.Add(colorAt(prof[i].y));
                    }
                for (int i = 0; i < prof.Count - 1; i++)
                    for (int s = 0; s < segs; s++)
                    {
                        int a = start + i * (segs + 1) + s, b = a + segs + 1;
                        float ang = (s + 0.5f) / segs * Mathf.PI * 2f;
                        Vector3 outw = m.MultiplyVector(new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)));
                        float dr = prof[i + 1].x - prof[i].x, dy = prof[i + 1].y - prof[i].y;
                        if (Mathf.Abs(dy) < 1e-5f) outw = m.MultiplyVector(dr < 0f ? Vector3.up : Vector3.down);
                        Tri(a, a + 1, b + 1, outw); Tri(a, b + 1, b, outw);
                    }
                if (capBottom) Cap(m, prof[0], segs, colorAt, Vector3.down);
                if (capTop) Cap(m, prof[prof.Count - 1], segs, colorAt, Vector3.up);
            }

            private void Cap(Matrix4x4 m, Vector2 ring, int segs, System.Func<float, Color> colorAt, Vector3 dir)
            {
                if (ring.x < 1e-5f) return;
                int c = _v.Count;
                _v.Add(m.MultiplyPoint3x4(new Vector3(0f, ring.y, 0f))); _c.Add(colorAt(ring.y));
                for (int s = 0; s <= segs; s++)
                {
                    float a = s / (float)segs * Mathf.PI * 2f;
                    _v.Add(m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * ring.x, ring.y, Mathf.Sin(a) * ring.x))); _c.Add(colorAt(ring.y));
                }
                Vector3 n = m.MultiplyVector(dir);
                for (int s = 0; s < segs; s++) Tri(c, c + 1 + s, c + 2 + s, n);
            }

            private void Tri(int a, int b, int c, Vector3 outward)
            {
                Vector3 n = Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]);
                if (Vector3.Dot(n, outward) < 0f) { int t = b; b = c; c = t; }
                _t.Add(a); _t.Add(b); _t.Add(c);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_v); mesh.SetColors(_c); mesh.SetTriangles(_t, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
#endif
