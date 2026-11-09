#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using CatCooks.Food.Tools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DS = CatCooks.Food.CatCooksDessertSetup;
using BS = CatCooks.Food.CatCooksBorschSetup;
using O = CatCooks.Food.CookRecipeBook.Orloff;

namespace CatCooks.Food
{
    /// <summary>
    /// ТЕЛЯТИНА ПО-ОРЛОВСКИ, шаг 1. Префабы НЕ создаём — только ассеты (продукты, правила, меши), объекты в сцене
    /// и компоненты на выделенном. Сохранить префабом — руками.
    ///   1. Продукты и правила: шампиньоны, тимьян, мускатный орех, телячья корейка, дюксель, лук томлёный / зарумяненный, субиз;
    ///      правила: шампиньоны и сыр — в измельчитель.
    ///   2. Шампиньон — модель в сцену (грязный → помыть → в измельчитель).
    ///   3. Баночки тимьяна и мускатного ореха — в сцену.
    ///   4. Телячья корейка — модель в сцену (потом сохранить префабом).
    ///   5. Туши: ронять корейку префабом (выдели префаб в Project).
    ///   6. Проверка.
    /// </summary>
    public static class CatCooksOrloffSetup
    {
        private const string Root = "CatCooks/Орлов/";
        private const string ModelDir = "Assets/CatCooks/Food/Orloff";

        // ───────────────────────── 1. ПРОДУКТЫ ─────────────────────────

        [MenuItem(Root + "1. Продукты и правила (шампиньоны, тимьян, мускат, корейка, дюксель, субиз)", priority = 260)]
        public static void MakeProducts()
        {
            var log = new StringBuilder();
            string dir = DS.ProductsDir();
            ushort next = (ushort)(DS.MaxId() + 1);
            FoodGroupDef veg = BS.Or(DS.GroupOf("Морковь"), DS.FindGroup("Group_Vegetables"));
            FoodGroupDef spice = BS.Or(DS.GroupOf("Перец чёрный"), DS.FindGroup("Group_Spices"));
            FoodGroupDef meat = BS.Or(BS.Or(DS.GroupOf("Говядина"), DS.GroupOf("Свинина")), DS.FindGroup("Group_Meat"));
            FoodGroupDef paste = BS.EnsureGroup("Group_Paste", "Пасты (уваренное)", "Густая масса: берётся мастерком, лежит гладкой кучкой.", FoodState.Puree, log);
            FoodGroupDef saute = BS.EnsureGroup("Group_Saute", "Зажарка (сковорода)", "Жареные тёртые овощи — густой массой: видна горкой, берётся мастерком.", FoodState.Puree, log);

            log.AppendLine("\nПРОДУКТЫ:");
            ProductDef mush = DS.Ensure(O.Mushroom, veg, new Color(0.93f, 0.88f, 0.80f), 0.08f, false, dir, ref next, log);
            ProductDef thyme = DS.Ensure(O.Thyme, spice, new Color(0.40f, 0.52f, 0.32f), 0.02f, false, dir, ref next, log);
            ProductDef nut = DS.Ensure(O.Nutmeg, spice, new Color(0.55f, 0.36f, 0.20f), 0.02f, false, dir, ref next, log);
            ProductDef loin = DS.Ensure(O.VealLoin, meat, new Color(0.80f, 0.38f, 0.36f), 0.3f, false, dir, ref next, log);
            ProductDef dux = DS.Ensure(O.Duxelles, paste, new Color(0.30f, 0.20f, 0.12f), 0.06f, true, dir, ref next, log);
            ProductDef sweat = DS.Ensure(O.OnionSweated, saute, new Color(0.95f, 0.88f, 0.62f), 0.06f, true, dir, ref next, log);
            ProductDef brown = DS.Ensure(O.OnionBrowned, saute, new Color(0.70f, 0.45f, 0.18f), 0.06f, true, dir, ref next, log);
            ProductDef soub = DS.Ensure(O.Soubise, paste, new Color(0.98f, 0.95f, 0.86f), 0.06f, true, dir, ref next, log);
            foreach (ProductDef p in new[] { dux, sweat, brown, soub })
            {
                if (p == null) continue;
                if (p.Solid) { p.Solid = false; EditorUtility.SetDirty(p); }
                if (p.DebrisColor.a < 0.01f) { Color c = p.BaseColor; c.a = 1f; p.DebrisColor = c; EditorUtility.SetDirty(p); }
            }
            if (dux != null && paste != null && dux.Group != paste) { dux.Group = paste; EditorUtility.SetDirty(dux); }
            if (soub != null && paste != null && soub.Group != paste) { soub.Group = paste; EditorUtility.SetDirty(soub); }
            if (mush != null && mush.DebrisColor.a < 0.01f) { mush.DebrisColor = new Color(0.85f, 0.78f, 0.68f, 1f); EditorUtility.SetDirty(mush); }
            if (thyme != null && thyme.DebrisColor.a < 0.01f) { thyme.DebrisColor = new Color(0.40f, 0.52f, 0.32f, 1f); EditorUtility.SetDirty(thyme); }
            if (nut != null && nut.DebrisColor.a < 0.01f) { nut.DebrisColor = new Color(0.55f, 0.36f, 0.20f, 1f); EditorUtility.SetDirty(nut); }

            log.AppendLine("\nПРАВИЛА:");
            if (mush != null)
                foreach (FoodState st in new[] { FoodState.Washed, FoodState.Peeled, FoodState.Raw })
                {
                    BS.RuleW(mush, st, FoodProcess.Grate, FoodState.Grated, 2f, 0f, FoodState.None, FoodState.None, log);
                    BS.RuleW(mush, st, FoodProcess.Mince, FoodState.Grated, 2f, 0f, FoodState.None, FoodState.None, log);
                }
            ProductDef cheese = DS.Find(O.Cheese);
            if (cheese != null)
                foreach (FoodState st in new[] { FoodState.Raw, FoodState.Washed, FoodState.Pieces })
                {
                    BS.RuleW(cheese, st, FoodProcess.Grate, FoodState.Grated, 2f, 0f, FoodState.None, FoodState.None, log);
                    BS.RuleW(cheese, st, FoodProcess.Mince, FoodState.Grated, 2f, 0f, FoodState.None, FoodState.None, log);
                }
            else log.AppendLine("⚠ нет «Сыр твёрдый» — понадобится для соуса морне (шаг 2)");

            log.AppendLine("\nЧТО УЖЕ ЕСТЬ:");
            foreach (string t in new[] { "Лук репчатый", O.Butter, O.Cream, "Соль", "Масло подсолнечное", "Морковь", "Лавровый лист", "Бульон для борща", "Мука пшеничная", "Молоко", "Яйцо" })
                log.AppendLine(DS.Find(t) != null ? $"✔ «{t}»" : $"✖ нет «{t}»" + (t == O.Cream ? " — сливки из молока (вибратор)" : t == O.Butter ? " — сливочное масло из сливок" : ""));

            AssetDatabase.SaveAssets();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null)
            {
                db.CollectFromProject();
                EditorUtility.SetDirty(db);
                EditorSceneManager.MarkSceneDirty(db.gameObject.scene);
                log.AppendLine("\n✔ FoodDatabase пересобран — СОХРАНИ СЦЕНУ");
            }
            else log.AppendLine("\n⚠ в открытой сцене нет FoodDatabase — нажми у него «Collect From Project»");
            CookRecipeBook.Reload();
            log.AppendLine("\nВСЁ МАСТЕРКАМИ (допуск ±50%):");
            log.AppendLine($"ДЮКСЕЛЬ (сковорода): грибы рубленые {O.DuxMushroom:0} мастерка (2 гриба) + лук тёртый {O.DuxOnion:0} + сливочное масло {O.DuxButter:0} + соль {O.DuxSalt:0} + тимьян {O.DuxThyme:0}");
            log.AppendLine($"  → на огонь, жарить {O.DuxSeconds:0} с → «Дюксель».");
            log.AppendLine($"ЛУК ТОМЛЁНЫЙ: лук тёртый {O.SoubOnion:0} мастерка (1 луковица) + сливочное масло {O.SoubButter:0} → СЛАБЫЙ огонь (без раздува) {O.SweatSeconds:0} с (раздул — «Лук зарумяненный»).");
            log.AppendLine($"СУБИЗ: лук томлёный {O.SoubSweated:0} мастерка + сливки НАЛИТЬ (от {O.SoubCream:0} мастерка-объёма, лишнее выкипит) → слабый огонь {O.SoubReduceSeconds:0} с → миксером {O.SoubMixSeconds:0} с.");
            log.AppendLine("\nДальше: 2 (шампиньон), 3 (тимьян и мускат), 4 → префаб → 5 (корейка с туши), 6 (проверка).");
            DS.Done("Орлов — продукты и правила", log);
        }

        // ───────────────────────── 2. ШАМПИНЬОН ─────────────────────────

        [MenuItem(Root + "2. Шампиньон — модель в сцену", priority = 261)]
        public static void MakeMushroom()
        {
            var log = new StringBuilder();
            if (DS.Find(O.Mushroom) == null) { EditorUtility.DisplayDialog("Орлов", "Сначала меню 1 (продукты).", "Ок"); return; }
            Vector3 at = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            float S = Mathf.Max(0.25f, MeatCutSettings.WorldScale) / 4f;
            float capR = 0.17f * S, stemR = 0.06f * S, stemH = 0.13f * S, capH = 0.12f * S;
            Mesh m = SaveMesh(MushroomMesh(capR, capH, stemR, stemH), "Champignon_Mesh", log);
            GameObject go = BS.SceneObj("Champignon_01", at);
            go.GetComponent<MeshFilter>().sharedMesh = m;
            go.GetComponent<MeshRenderer>().sharedMaterials = new[] { DS.PlateMat(log) };
            foreach (Collider c in go.GetComponents<Collider>()) if (!(c is MeshCollider)) Object.DestroyImmediate(c);
            MeshCollider mc = BS.Get<MeshCollider>(go); mc.sharedMesh = m; mc.convex = true;
            BS.SetupVeg(go, O.Mushroom, 3, false, log);
            Rigidbody rb = go.GetComponent<Rigidbody>(); if (rb != null) rb.mass = 0.5f;
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"\n✔ «Champignon_01» — шляпка Ø{capR * 200f:0} см, 3 порции. Грязный (темнее, с крошками) → помыть водой → в измельчитель → «рубленые» (горкой).");
            log.AppendLine("Испортился — темнеет сам (подкраска). Нужно больше — дублируй (Ctrl+D) или сохрани префабом и добавь в Spawnable Prefabs.");
            log.AppendLine("Повторный запуск обновит меш на месте.");
            DS.Done("Орлов — шампиньон", log);
        }

        /// <summary>
        /// Шампиньон: тело вращения — ножка (кремово-белая), пластинки под шляпкой (розовато-коричневые),
        /// шляпка-купол (белая, к макушке чуть бежевая), край шляпки завёрнут. Цвет — в вершинах (мультяшный материал игры).
        /// </summary>
        private static Mesh MushroomMesh(float capR, float capH, float stemR, float stemH)
        {
            Color stem = new Color(0.95f, 0.92f, 0.85f), gill = new Color(0.62f, 0.45f, 0.38f), rim = new Color(0.96f, 0.93f, 0.88f),
                  cap = new Color(0.98f, 0.96f, 0.92f), crown = new Color(0.86f, 0.78f, 0.66f);
            var prof = new List<Vector2>(); var col = new List<Color>();
            // ножка: низ → бок (чуть шире книзу, юбочка-кольцо у шляпки)
            prof.Add(new Vector2(0f, 0f)); col.Add(stem);
            prof.Add(new Vector2(stemR * 1.05f, 0f)); col.Add(stem);
            prof.Add(new Vector2(stemR * 1.1f, stemH * 0.08f)); col.Add(stem);
            prof.Add(new Vector2(stemR * 0.95f, stemH * 0.6f)); col.Add(stem);
            prof.Add(new Vector2(stemR * 1.15f, stemH * 0.88f)); col.Add(stem);       // кольцо (остаток плёнки)
            prof.Add(new Vector2(stemR * 0.9f, stemH)); col.Add(gill);
            // пластинки снизу шляпки — к краю
            prof.Add(new Vector2(capR * 0.55f, stemH + capH * 0.08f)); col.Add(gill);
            prof.Add(new Vector2(capR * 0.88f, stemH + capH * 0.12f)); col.Add(gill);
            // завёрнутый край
            prof.Add(new Vector2(capR * 0.95f, stemH + capH * 0.05f)); col.Add(rim);
            prof.Add(new Vector2(capR, stemH + capH * 0.2f)); col.Add(rim);
            // купол
            const int dome = 8;
            for (int i = 1; i <= dome; i++)
            {
                float a = i / (float)dome * Mathf.PI * 0.5f;
                float r = capR * Mathf.Cos(a), y = stemH + capH * 0.2f + capH * 0.8f * Mathf.Sin(a);
                if (i == dome) r = 0f;
                prof.Add(new Vector2(r, y)); col.Add(Color.Lerp(cap, crown, Mathf.Pow(i / (float)dome, 3f)));
            }
            return Lathe(prof, col, 28);
        }

        /// <summary>Тело вращения по профилю (r, y) снизу вверх: у каждого отрезка свои вершины (цвет отрезка), нормали — гладкие по профилю.</summary>
        private static Mesh Lathe(List<Vector2> prof, List<Color> col, int seg)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            int pc = prof.Count;
            var pn = new Vector2[pc];
            for (int i = 0; i < pc; i++)
            {
                Vector2 sum = Vector2.zero;
                if (i > 0) { Vector2 d = prof[i] - prof[i - 1]; sum += new Vector2(d.y, -d.x).normalized; }
                if (i < pc - 1) { Vector2 d = prof[i + 1] - prof[i]; sum += new Vector2(d.y, -d.x).normalized; }
                pn[i] = sum.sqrMagnitude > 1e-8f ? sum.normalized : Vector2.up;
            }
            for (int k = 0; k < pc - 1; k++)
            {
                Color cc = col[k + 1];
                int b0 = v.Count;
                for (int j = 0; j <= seg; j++)
                {
                    float a = j / (float)seg * Mathf.PI * 2f;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    for (int e = 0; e < 2; e++)
                    {
                        Vector2 p = prof[k + e], q = pn[k + e];
                        v.Add(new Vector3(p.x * ca, p.y, p.x * sa));
                        n.Add(new Vector3(q.x * ca, q.y, q.x * sa).normalized);
                        c.Add(cc);
                    }
                }
                for (int j = 0; j < seg; j++)
                {
                    int a0 = b0 + j * 2, a1 = a0 + 1, c0 = a0 + 2, c1 = a0 + 3;
                    Vector3 nn = n[a0] + n[c1];
                    BS.Tri(v, t, a0, c0, a1, nn);
                    BS.Tri(v, t, a1, c0, c1, nn);
                }
            }
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        // ───────────────────────── 3. ТИМЬЯН И МУСКАТ ─────────────────────────

        [MenuItem(Root + "3. Баночки тимьяна и мускатного ореха — в сцену", priority = 262)]
        public static void MakeShakers()
        {
            var log = new StringBuilder();
            ProductDef thyme = DS.Find(O.Thyme), nut = DS.Find(O.Nutmeg);
            if (thyme == null || nut == null) { EditorUtility.DisplayDialog("Орлов", "Сначала меню 1 (продукты).", "Ок"); return; }
            Vector3 at = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            Material mat = DS.PlateMat(log);
            var made = new List<Object>();
            var shakers = new[]
            {
                new CatCooksBBQSetup.Bottle
                {
                    Name = "Shaker_Thyme", Product = O.Thyme, Dose = 1, Segs = 18, H = 0.15f, R = 0.045f, Neck = 0.042f, Jar = true, Shaker = true,
                    Body = new Color(0.86f, 0.90f, 0.82f), Cap = new Color(0.32f, 0.44f, 0.26f),
                    LabelBg = new Color(0.26f, 0.36f, 0.20f), LabelFg = Color.white, LabelAccent = new Color(0.70f, 0.80f, 0.55f),
                    Icon = CatCooksBBQSetup.Icon.Thyme, Line1 = "ТИМЬЯН", Line2 = "СУШЁНЫЙ"
                },
                new CatCooksBBQSetup.Bottle
                {
                    Name = "Shaker_Nutmeg", Product = O.Nutmeg, Dose = 1, Segs = 18, H = 0.13f, R = 0.04f, Neck = 0.038f, Jar = true, Shaker = true,
                    Body = new Color(0.92f, 0.86f, 0.76f), Cap = new Color(0.45f, 0.28f, 0.15f),
                    LabelBg = new Color(0.40f, 0.24f, 0.12f), LabelFg = new Color(1f, 0.92f, 0.78f), LabelAccent = new Color(0.80f, 0.60f, 0.35f),
                    Icon = CatCooksBBQSetup.Icon.Nutmeg, Line1 = "МУСКАТНЫЙ", Line2 = "ОРЕХ"
                }
            };
            for (int i = 0; i < shakers.Length; i++)
            {
                CatCooksBBQSetup.Bottle b = shakers[i];
                GameObject go = BS.SceneRoot(b.Name, at + Vector3.right * (i * 0.25f));
                CatCooksBBQSetup.BuildBottle(go, b, mat, log);
                PowderShakerItem ps = DS.Need<PowderShakerItem>(go);
                var so = new SerializedObject(ps);
                DS.SetObj(so, "_product", DS.Find(b.Product)); DS.SetObj(so, "_mouth", go.transform.Find("Spout"));
                SerializedProperty inf = so.FindProperty("_infinite"); if (inf != null) inf.boolValue = true;
                so.ApplyModifiedProperties();
                BS.ClientAuthNT(go, log);
                log.AppendLine($"✔ «{go.name}» — {b.Product}, ПКМ над тарой: 1 доза");
                made.Add(go);
            }
            Selection.objects = made.ToArray();
            EditorSceneManager.MarkSceneDirty(((GameObject)made[0]).scene);
            log.AppendLine("\nТимьян — в дюксель (1 доза), мускат — в соус морне (шаг 2). Повторный запуск обновляет баночки на месте. Сохрани префабами, если нужно.");
            DS.Done("Орлов — тимьян и мускат", log);
        }

        // ───────────────────────── 4. ТЕЛЯЧЬЯ КОРЕЙКА ─────────────────────────

        [MenuItem(Root + "4. Телячья корейка — модель в сцену (потом сохранить префабом)", priority = 263)]
        public static void MakeLoin()
        {
            var log = new StringBuilder();
            ProductDef loin = DS.Find(O.VealLoin);
            if (loin == null) { EditorUtility.DisplayDialog("Орлов", "Сначала меню 1 (продукты).", "Ок"); return; }
            Vector3 at = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            float WS = Mathf.Max(0.25f, MeatCutSettings.WorldScale);
            float L = 0.26f * WS, W = 0.085f * WS, H = 0.075f * WS;
            Mesh m = SaveMesh(LoinMesh(L, W, H), "Veal_Loin_Mesh", log);
            GameObject go = BS.SceneObj("Veal_Loin", at);
            go.GetComponent<MeshFilter>().sharedMesh = m;
            go.GetComponent<MeshRenderer>().sharedMaterials = new[] { DS.PlateMat(log) };
            foreach (Collider c in go.GetComponents<Collider>()) if (!(c is BoxCollider)) Object.DestroyImmediate(c);
            BoxCollider bc = BS.Get<BoxCollider>(go); bc.center = m.bounds.center; bc.size = m.bounds.size;
            BS.MeatPiece(go, loin, 3f, log);
            FoodPiece fp = go.GetComponent<FoodPiece>();
            var so = new SerializedObject(fp); DS.SetInt(so, "_count", 8); so.ApplyModifiedPropertiesWithoutUndo();
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"✔ «Veal_Loin» — корейка {L * 100f:0}×{W * 100f:0}×{H * 100f:0} см, 8 порций (потом — 6–8 медальонов): розовое мясо, жировая шапочка сверху, срезы с мраморностью");
            log.AppendLine("\nДАЛЬШЕ РУКАМИ:");
            log.AppendLine("  1. Перетащи Veal_Loin из сцены в окно Project — префаб.");
            log.AppendLine("  2. Проверь, что он в Spawnable Prefabs (NetworkManager → DefaultPrefabObjects).");
            log.AppendLine("  3. Выдели префаб в Project → Орлов → 5 — говяжья туша начнёт ронять корейку целым куском (левый толстый край).");
            DS.Done("Орлов — телячья корейка", log);
        }

        /// <summary>
        /// Корейка: брусок-«батон» вдоль X — сечение скруглённое (плоское дно, купол сверху), сверху жировая шапочка,
        /// к концам чуть сужается. Торцы — срез: мясо с мраморными прожилками, жир сверху полосой.
        /// </summary>
        private static Mesh LoinMesh(float L, float W, float H)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            Color meat = new Color(0.82f, 0.36f, 0.34f), meatDark = new Color(0.70f, 0.26f, 0.25f), fat = new Color(0.97f, 0.92f, 0.82f),
                  marble = new Color(0.93f, 0.74f, 0.68f), side = new Color(0.76f, 0.32f, 0.30f);
            const int Ring = 28, Len = 10;
            const float FatFrom = 0.86f;   // жировая шапочка — только по верху
            var ring = new Vector2[Ring];
            for (int i = 0; i < Ring; i++)
            {
                float a = i / (float)Ring * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                float z = Mathf.Sign(ca) * Mathf.Pow(Mathf.Abs(ca), 0.7f) * W * 0.5f;
                float y = sa >= 0f ? H * 0.42f + sa * H * 0.58f : H * 0.42f + Mathf.Sign(sa) * Mathf.Pow(Mathf.Abs(sa), 0.45f) * H * 0.42f;
                ring[i] = new Vector2(z, y);
            }
            // бок: сетка Len × Ring, вершины общие — нормали гладкие
            int b0 = v.Count;
            for (int k = 0; k <= Len; k++)
            {
                float u = k / (float)Len;
                float x = (u - 0.5f) * L;
                float taper = 1f - 0.12f * Mathf.Pow(Mathf.Abs(u - 0.5f) * 2f, 3f);
                for (int i = 0; i <= Ring; i++)
                {
                    Vector2 p = ring[i % Ring];
                    float wob = 1f + 0.03f * Mathf.Sin(x * 13f / Mathf.Max(0.01f, L) + i * 0.9f);
                    Vector3 q = new Vector3(x, p.y * taper * wob, p.x * taper * wob);
                    v.Add(q); n.Add(Vector3.zero);
                    c.Add(p.y > H * FatFrom ? fat : Color.Lerp(side, meat, 0.5f + 0.5f * Mathf.Sin(i * 1.3f + k)));
                }
            }
            for (int k = 0; k < Len; k++)
                for (int i = 0; i < Ring; i++)
                {
                    int a = b0 + k * (Ring + 1) + i, b = a + 1, a2 = a + Ring + 1, b2 = a2 + 1;
                    Vector3 mid = (v[a] + v[b2]) * 0.5f;
                    Vector3 outN = new Vector3(0f, mid.y - H * 0.45f, mid.z);
                    BS.Tri(v, t, a, a2, b, outN);
                    BS.Tri(v, t, b, a2, b2, outN);
                }
            // торцы — срез: веер, полосы мраморности, жир сверху
            for (int end = 0; end < 2; end++)
            {
                float x = end == 0 ? -L * 0.5f : L * 0.5f;
                int k = end == 0 ? 0 : Len;
                Vector3 nn = end == 0 ? Vector3.left : Vector3.right;
                int ctr = v.Count;
                v.Add(new Vector3(x, H * 0.4f * 0.88f, 0f)); n.Add(nn); c.Add(meatDark);
                int r0 = v.Count;
                for (int i = 0; i < Ring; i++)
                {
                    Vector3 p = v[b0 + k * (Ring + 1) + i];
                    v.Add(p); n.Add(nn);
                    c.Add(p.y > H * FatFrom * 0.88f ? fat : meat);
                }
                // промежуточное кольцо: мраморные прожилки
                int r1 = v.Count;
                for (int i = 0; i < Ring; i++)
                {
                    Vector3 p = Vector3.Lerp(v[ctr], v[r0 + i], 0.55f);
                    v.Add(p); n.Add(nn);
                    c.Add(Mathf.Sin(i * 2.7f) > 0.6f ? marble : meatDark);
                }
                for (int i = 0; i < Ring; i++)
                {
                    int i2 = (i + 1) % Ring;
                    BS.Tri(v, t, ctr, r1 + i, r1 + i2, nn);
                    BS.Tri(v, t, r1 + i, r0 + i, r0 + i2, nn);
                    BS.Tri(v, t, r1 + i, r0 + i2, r1 + i2, nn);
                }
            }
            var m = new Mesh();
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateNormals();
            // торцы — плоские (RecalculateNormals их не смешивает с боком: вершины свои)
            m.RecalculateBounds();
            return m;
        }

        // ───────────────────────── 5. ТУШИ: КОРЕЙКА ─────────────────────────

        [MenuItem(Root + "5. Туши: ронять телячью корейку (выдели префаб Veal_Loin в Project)", priority = 264)]
        public static void AssignLoin()
        {
            var log = new StringBuilder();
            MeatCutSettings st = BS.FindSettings();
            if (st == null) { EditorUtility.DisplayDialog("Орлов", "Нет MeatCutSettings — Мясо → Новые туши → 1.", "Ок"); return; }
            GameObject loin = null;
            foreach (Object o in Selection.objects)
            {
                GameObject go = o as GameObject;
                if (go == null || !EditorUtility.IsPersistent(go)) continue;
                FoodPiece fp = go.GetComponent<FoodPiece>();
                string title = "";
                if (fp != null) { ProductDef d = new SerializedObject(fp).FindProperty("_product").objectReferenceValue as ProductDef; if (d != null) title = d.Title; }
                string nm = go.name.ToLowerInvariant();
                if (title == O.VealLoin || nm.Contains("loin") || nm.Contains("корейк")) { loin = go; break; }
            }
            if (loin == null)
            {
                EditorUtility.DisplayDialog("Орлов", "Выдели в окне Project префаб Veal_Loin (сначала меню 4 и перетащить из сцены в Project).", "Ок");
                return;
            }
            Undo.RecordObject(st, "Телячья корейка");
            st.VealLoinPrefab = loin;
            EditorUtility.SetDirty(st);
            AssetDatabase.SaveAssets();
            log.AppendLine($"✔ говяжий толстый край → «{loin.name}» целым куском" + (st.VealLoinLeftOnly ? " (только левый; правый — как раньше, стейки рибай)" : " (оба)"));
            if (loin.GetComponent<FishNet.Object.NetworkObject>() == null) log.AppendLine($"  ✖ у «{loin.name}» нет Network Object — выпадать не будет");
            log.AppendLine($"\nНастройки: {AssetDatabase.GetAssetPath(st)}. Оба края корейкой — сними галку Veal Loin Left Only.");
            log.AppendLine("Убрать — очисти поле Veal Loin Prefab. Проверь, что префаб в Spawnable Prefabs.");
            DS.Done("Орлов — корейка с туши", log);
        }

        // ───────────────────────── 7. ШАГ 2: СОУС МОРНЕ ─────────────────────────

        [MenuItem(Root + "7. Шаг 2: ру, бешамель, соус морне (продукты)", priority = 266)]
        public static void MakeStep2()
        {
            var log = new StringBuilder();
            string dir = DS.ProductsDir();
            ushort next = (ushort)(DS.MaxId() + 1);
            FoodGroupDef paste = BS.EnsureGroup("Group_Paste", "Пасты (уваренное)", "Густая масса: берётся мастерком, лежит гладкой кучкой.", FoodState.Puree, log);
            log.AppendLine("\nПРОДУКТЫ:");
            ProductDef roux = DS.Ensure(O.Roux, paste, new Color(0.90f, 0.76f, 0.46f), 0.06f, true, dir, ref next, log);
            ProductDef bech = DS.Ensure(O.Bechamel, paste, new Color(0.98f, 0.96f, 0.90f), 0.06f, true, dir, ref next, log);
            ProductDef lumpy = DS.Ensure(O.BechamelLumpy, paste, new Color(0.90f, 0.86f, 0.74f), 0.06f, true, dir, ref next, log);
            ProductDef morn = DS.Ensure(O.Mornay, paste, new Color(0.98f, 0.88f, 0.58f), 0.06f, true, dir, ref next, log);
            foreach (ProductDef p in new[] { roux, bech, lumpy, morn })
            {
                if (p == null) continue;
                if (p.Solid) { p.Solid = false; EditorUtility.SetDirty(p); }
                if (paste != null && p.Group != paste) { p.Group = paste; EditorUtility.SetDirty(p); }
                if (p.DebrisColor.a < 0.01f) { Color c = p.BaseColor; c.a = 1f; p.DebrisColor = c; EditorUtility.SetDirty(p); }
            }
            ProductDef cheese = DS.Find(O.Cheese);
            if (cheese != null)
                foreach (FoodState st in new[] { FoodState.Raw, FoodState.Washed, FoodState.Pieces })
                {
                    BS.RuleW(cheese, st, FoodProcess.Grate, FoodState.Grated, 2f, 0f, FoodState.None, FoodState.None, log);
                    BS.RuleW(cheese, st, FoodProcess.Mince, FoodState.Grated, 2f, 0f, FoodState.None, FoodState.None, log);
                }
            log.AppendLine("\nЧТО НУЖНО:");
            foreach (string t in new[] { O.Butter, O.Flour, O.Milk, O.Cheese, O.Nutmeg })
                log.AppendLine(DS.Find(t) != null ? $"✔ «{t}»" : $"✖ нет «{t}»" + (t == O.Nutmeg ? " — меню 1" : ""));

            AssetDatabase.SaveAssets();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null) { db.CollectFromProject(); EditorUtility.SetDirty(db); EditorSceneManager.MarkSceneDirty(db.gameObject.scene); log.AppendLine("\n✔ FoodDatabase пересобран — СОХРАНИ СЦЕНУ"); }
            CookRecipeBook.Reload();
            log.AppendLine("\nВСЁ МАСТЕРКАМИ, жидкое — налить (больше нормы можно: лишнее выкипит на огне):");
            log.AppendLine($"РУ (сковорода): сливочное масло {O.RouxButter:0} + мука {O.RouxFlour:0} → жарить {O.RouxSeconds:0} с.");
            log.AppendLine($"БЕШАМЕЛЬ (та же сковорода): ру {O.BechRoux:0} + молоко налить (от {O.BechMilk:0} мастерков) → на огне ВАРИТЬ ПОМЕШИВАЯ {O.BechSeconds:0} с:");
            log.AppendLine($"  миксером в таре хотя бы раз в {CookRecipeBook.StirGrace:0} с. Не мешал ещё {CookRecipeBook.LumpSeconds:0} с — «{O.BechamelLumpy}» (ошибка).");
            log.AppendLine($"СОУС МОРНЕ: бешамель {O.MornBech:0} + сыр тёртый {O.MornCheese:0} + мускат {O.MornNutmeg:0} → миксером {O.MornSeconds:0} с.");
            log.AppendLine("\nСУБИЗ теперь: лук томлёный 2 + сливки НАЛИТЬ (сколько угодно от 1 мастерка-объёма) → слабый огонь: лишнее выкипит, уварить 8 с → миксером 6 с.");
            DS.Done("Орлов — шаг 2", log);
        }

        // ───────────────────────── 8. ШАГ 3: ТЕЛЯТИНА ─────────────────────────

        [MenuItem(Root + "8. Шаги 3–7: телятина, сборка, духовка, подача (выдели префаб Veal_Loin)", priority = 267)]
        public static void MakeStep3()
        {
            var log = new StringBuilder();
            string dir = DS.ProductsDir();
            ushort next = (ushort)(DS.MaxId() + 1);
            ProductDef loin = DS.Find(O.VealLoin);
            if (loin == null) { EditorUtility.DisplayDialog("Орлов", "Нет «Телячья корейка» — сначала меню 1.", "Ок"); return; }
            FoodGroupDef meat = loin.Group != null ? loin.Group : BS.Or(DS.GroupOf("Говядина"), DS.FindGroup("Group_Meat"));
            log.AppendLine("ПРОДУКТЫ:");
            ProductDef stewed = DS.Ensure(O.VealStewed, meat, new Color(0.52f, 0.30f, 0.20f), 0.3f, true, dir, ref next, log);
            if (stewed != null && stewed.Solid != loin.Solid) { stewed.Solid = loin.Solid; EditorUtility.SetDirty(stewed); }

            log.AppendLine("\nПРАВИЛА КОРЕЙКИ (окно ±40–50%):");
            BS.RuleW(loin, FoodState.Butchered, FoodProcess.Fry, FoodState.Fried, O.SearSeconds, 0.4f, FoodState.Undercooked, FoodState.Overcooked, log);
            BS.RuleW(loin, FoodState.Raw, FoodProcess.Fry, FoodState.Fried, O.SearSeconds, 0.4f, FoodState.Undercooked, FoodState.Overcooked, log);
            BS.RuleW(loin, FoodState.Fried, FoodProcess.Boil, FoodState.Stewed, O.BraiseSeconds, 0.5f, FoodState.Undercooked, FoodState.Overcooked, log);
            BS.RuleW(loin, FoodState.Undercooked, FoodProcess.Boil, FoodState.Stewed, O.BraiseSeconds * 0.5f, 0.5f, FoodState.None, FoodState.Overcooked, log);
            BS.RuleW(loin, FoodState.Overcooked, FoodProcess.Fry, FoodState.Burnt, 15f, 0f, FoodState.None, FoodState.None, log);

            // компонент — на выделенные префабы/объекты корейки, иначе на Veal_Loin в сцене
            log.AppendLine("\nКОМПОНЕНТ Veal Loin Cook (ведёт шаги, подсказка над куском, отдых):");
            var targets = new List<GameObject>();
            foreach (Object o in Selection.objects)
            {
                GameObject go = o as GameObject;
                if (go == null) continue;
                FoodPiece fp = go.GetComponent<FoodPiece>();
                if (fp == null) continue;
                ProductDef d = new SerializedObject(fp).FindProperty("_product").objectReferenceValue as ProductDef;
                if (d == loin || go.name.ToLowerInvariant().Contains("loin")) targets.Add(go);
            }
            if (targets.Count == 0) { MeatCutSettings mst = BS.FindSettings(); if (mst != null && mst.VealLoinPrefab != null) targets.Add(mst.VealLoinPrefab); }
            if (targets.Count == 0) { GameObject sc = GameObject.Find("Veal_Loin"); if (sc != null) targets.Add(sc); }
            foreach (GameObject go in targets)
            {
                if (EditorUtility.IsPersistent(go))
                {
                    // префаб в Project — правим через содержимое префаба и сохраняем (так FishNet пересоберёт список NetworkBehaviour)
                    string path = AssetDatabase.GetAssetPath(go);
                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    bool had = SetupLoin(root, stewed);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    PrefabUtility.UnloadPrefabContents(root);
                    log.AppendLine($"{(had ? "✔ обновлён" : "＋")} префаб «{go.name}»: Veal Loin Cook (шаг 3) + Orloff Loaf (шаги 4–7)");
                }
                else
                {
                    bool had = SetupLoin(go, stewed);
                    EditorUtility.SetDirty(go);
                    log.AppendLine($"{(had ? "✔ обновлён" : "＋")} «{go.name}» в сцене (если это копия префаба: Overrides → Apply)");
                }
            }
            if (targets.Count == 0) log.AppendLine("✖ не нашёл корейку: выдели префаб Veal_Loin в Project (или объект в сцене) и запусти ещё раз");

            AssetDatabase.SaveAssets();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null) { db.CollectFromProject(); EditorUtility.SetDirty(db); EditorSceneManager.MarkSceneDirty(db.gameObject.scene); log.AppendLine("\n✔ FoodDatabase пересобран — СОХРАНИ СЦЕНУ"); }
            CookRecipeBook.Reload();
            log.AppendLine($"\nТЕЛЯТИНА: корейку в сковороду на огонь — обжарить ~{O.SearSeconds:0} с ({O.SearSeconds * 0.6f:0}–{O.SearSeconds * 1.4f:0}) до корочки;");
            log.AppendLine($"  → в кастрюлю-суп (Борщ → 11) с бульоном (или водой), можно морковь, лук, лавр → НАКРЫТЬ КРЫШКОЙ → тушить ~{O.BraiseSeconds:0} с ({O.BraiseSeconds * 0.5f:0}–{O.BraiseSeconds * 1.5f:0});");
            log.AppendLine($"  → вынуть, положить (не в тару) — отдыхает {O.RestSeconds:0} с → «{O.VealStewed}». Над куском — подсказка, что делать.");
            log.AppendLine("СБОРКА: пилой 5 разрезов (6 медальонов) → мастерком в щели дюксель, потом субиз (5+5 порц.) → сверху соус морне 1–3 мастерка + сыр тёртый 1 →");
            log.AppendLine("  духовка от 140 °C, ПКМ по решётке, ~25 с (корочка) → на тарелку / блюдо / доску — «Подано» и звёзды.");
            log.AppendLine("\nЕсли в Play ругается на NetworkBehaviour у корейки: Fish-Networking → Utility → Reserialize NetworkObjects (или открой префаб и сохрани).");
            DS.Done("Орлов — шаг 3", log);
        }

        private static bool SetupLoin(GameObject go, ProductDef stewed)
        {
            bool had = go.GetComponent<VealLoinCook>() != null && go.GetComponent<OrloffLoaf>() != null;
            VealLoinCook vc = DS.Need<VealLoinCook>(go);
            var so = new SerializedObject(vc);
            DS.SetObj(so, "_restedProduct", stewed);
            DS.SetF(so, "_restSeconds", O.RestSeconds);
            so.ApplyModifiedPropertiesWithoutUndo();
            DS.Need<OrloffLoaf>(go);
            return had;
        }

        // ───────────────────────── 6. ПРОВЕРКА ─────────────────────────

        [MenuItem(Root + "6. Проверка", priority = 265)]
        public static void Check()
        {
            var log = new StringBuilder();
            log.AppendLine("ПРОДУКТЫ:");
            foreach (string t in new[] { O.Mushroom, O.Thyme, O.Nutmeg, O.VealLoin, O.Duxelles, O.OnionSweated, O.OnionBrowned, O.Soubise })
                log.AppendLine(DS.Find(t) != null ? $"✔ «{t}»" : $"✖ нет «{t}» — меню 1");
            foreach (string t in new[] { O.Roux, O.Bechamel, O.BechamelLumpy, O.Mornay })
                log.AppendLine(DS.Find(t) != null ? $"✔ «{t}»" : $"✖ нет «{t}» — меню 7");
            log.AppendLine(DS.Find(O.VealStewed) != null ? $"✔ «{O.VealStewed}»" : $"✖ нет «{O.VealStewed}» — меню 8");
            foreach (string t in new[] { "Лук репчатый", O.Butter, O.Cream, "Соль", O.Cheese })
                log.AppendLine(DS.Find(t) != null ? $"✔ «{t}»" : $"✖ нет «{t}»");
            ProductDef mush = DS.Find(O.Mushroom);
            if (mush != null)
            {
                bool gr = false;
                foreach (FoodRule r in mush.Overrides) if ((r.Process == FoodProcess.Grate || r.Process == FoodProcess.Mince) && r.To == FoodState.Grated) { gr = true; break; }
                log.AppendLine(gr ? "✔ шампиньоны рубятся в измельчителе" : "✖ шампиньоны — нет правила измельчителя (меню 1)");
            }
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            log.AppendLine(db == null ? "✖ в сцене нет FoodDatabase" : "✔ FoodDatabase в сцене (после меню 1 — сохрани сцену)");

            log.AppendLine("\nВ СЦЕНЕ:");
            log.AppendLine(GameObject.Find("Champignon_01") != null ? "✔ Champignon_01" : "· шампиньона нет — меню 2");
            log.AppendLine(GameObject.Find("Shaker_Thyme") != null ? "✔ Shaker_Thyme" : "· тимьяна нет — меню 3");
            log.AppendLine(GameObject.Find("Shaker_Nutmeg") != null ? "✔ Shaker_Nutmeg" : "· муската нет — меню 3");

            log.AppendLine("\nКОРЕЙКА:");
            MeatCutSettings st = BS.FindSettings();
            if (st == null) log.AppendLine("✖ нет MeatCutSettings");
            else log.AppendLine(st.VealLoinPrefab != null ? $"✔ туша говядины роняет «{st.VealLoinPrefab.name}»" + (st.VealLoinLeftOnly ? " (левый край)" : " (оба края)")
                                                          : "· корейка — обычным куском (меню 4 → префаб → 5)");
            if (st != null && st.VealLoinPrefab != null)
                log.AppendLine(st.VealLoinPrefab.GetComponent<VealLoinCook>() != null && st.VealLoinPrefab.GetComponent<OrloffLoaf>() != null
                    ? "✔ у префаба корейки есть Veal Loin Cook и Orloff Loaf (шаги 3–7)" : "✖ у префаба корейки нет Veal Loin Cook / Orloff Loaf — выдели префаб → меню 8");
            log.AppendLine("\nВ Play в консоли: «[Рецепты] в книге: … Дюксель, Лук томлёный, Лук зарумяненный, Субиз, Ру, Бешамель, Соус морне».");
            DS.Done("Орлов — проверка", log);
        }

        // ───────────────────────── общее ─────────────────────────

        private static Mesh SaveMesh(Mesh m, string name, StringBuilder log)
        {
            string path = ModelDir + "/" + name + ".asset";
            DS.EnsureDir(ModelDir);
            m.name = name;
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
    }
}
#endif
