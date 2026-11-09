#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DS = CatCooks.Food.CatCooksDessertSetup;
using BS = CatCooks.Food.CatCooksBorschSetup;
using C = CatCooks.Food.CookRecipeBook.Cutlets;

namespace CatCooks.Food
{
    /// <summary>
    /// КОТЛЕТЫ, часть 1 (v0.1). Префабы не создаём: ассеты, правила, компоненты на выделенном.
    ///   1. Продукты и правила: Мякиш (хлеб без корки — шлифмашинкой), Хлебная масса, Фарш для котлет, Фарш отбитый,
    ///      Панировочные сухари (на потом); хлеб чистится шлифмашинкой; вид кучек.
    ///   2. Тара — выдели ведро/банку: её можно бить кувалдой («отбить фарш»).
    ///   3. Проверка.
    /// Хлеб для теста — клавиша 6 (Bonfire Test Keys), Shift+6 — другой вид.
    /// </summary>
    public static class CatCooksCutletsSetup
    {
        private const string Root = "CatCooks/Котлеты/";

        [MenuItem(Root + "1. Продукты и правила (мякиш, хлебная масса, фарш для котлет, отбитый)", priority = 300)]
        public static void MakeProducts()
        {
            var log = new StringBuilder();
            string dir = DS.ProductsDir();
            ushort next = (ushort)(DS.MaxId() + 1);
            FoodGroupDef breadG = BS.Or(BS.Or(DS.GroupOf("Буханка"), DS.GroupOf("Хлеб")), DS.FindGroup("Group_SubRecipe"));
            FoodGroupDef paste = BS.EnsureGroup("Group_Paste", "Пасты (уваренное)", "Густая масса: берётся мастерком, лежит гладкой кучкой.", FoodState.Puree, log);
            FoodGroupDef minceG = BS.Or(DS.GroupOf(C.BeefMince), paste);
            FoodGroupDef bulk = BS.Or(DS.GroupOf("Мука пшеничная"), DS.GroupOf("Соль"));

            log.AppendLine("\nПРОДУКТЫ:");
            ProductDef crumb = DS.Ensure(C.Crumb, breadG, new Color(0.97f, 0.93f, 0.82f), 0.27f, true, dir, ref next, log);
            ProductDef mass = DS.Ensure(C.BreadMass, paste, new Color(0.95f, 0.91f, 0.82f), 0.05f, true, dir, ref next, log);
            ProductDef mince = DS.Ensure(C.Mince, minceG, new Color(0.80f, 0.42f, 0.40f), 0.05f, true, dir, ref next, log);
            ProductDef pounded = DS.Ensure(C.Pounded, minceG, new Color(0.84f, 0.48f, 0.46f), 0.05f, true, dir, ref next, log);
            ProductDef crumbs = DS.Ensure(C.Crumbs, bulk, new Color(0.88f, 0.70f, 0.40f), 0.01f, false, dir, ref next, log);
            FoodGroupDef meatG = BS.Or(BS.Or(DS.GroupOf("Говядина"), DS.GroupOf("Свинина")), DS.FindGroup("Group_Meat"));
            ProductDef patty = DS.Ensure(C.Patty, meatG, new Color(0.82f, 0.45f, 0.42f), 0.1f, true, dir, ref next, log);
            foreach (ProductDef p in new[] { mass, mince, pounded })
            {
                if (p == null) continue;
                if (p.Solid) { p.Solid = false; EditorUtility.SetDirty(p); }
                if (p.DebrisColor.a < 0.01f) { Color c = p.BaseColor; c.a = 1f; p.DebrisColor = c; EditorUtility.SetDirty(p); }
            }
            if (crumbs != null && crumbs.DebrisColor.a < 0.01f) { crumbs.DebrisColor = new Color(0.88f, 0.70f, 0.40f, 1f); EditorUtility.SetDirty(crumbs); }

            log.AppendLine("\nПРАВИЛА (шлифмашинка по хлебу — «Чистить» → корка снимается):");
            int nb = 0;
            foreach (string t in new[] { "Буханка", "Багет", "Хлеб", "Батон" })
            {
                ProductDef b = DS.Find(t);
                if (b == null) continue;
                nb++;
                BS.RuleW(b, FoodState.Baked, FoodProcess.Peel, FoodState.Peeled, 4f, 0f, FoodState.None, FoodState.None, log);
                BS.RuleW(b, FoodState.Raw, FoodProcess.Peel, FoodState.Peeled, 4f, 0f, FoodState.None, FoodState.None, log);
            }
            if (nb == 0) log.AppendLine("✖ не нашёл хлеба («Буханка», «Багет») — CatCooks → Хлеб → 1");

            log.AppendLine("\nВИД КУЧЕК:");
            FoodHeapLook looks = EnsureLooks(log);
            if (looks != null)
            {
                Look(looks, mass, FoodHeapRenderer.HeapPreset.Puree, log);
                Look(looks, mince, FoodHeapRenderer.HeapPreset.Mince, log);
                Look(looks, pounded, FoodHeapRenderer.HeapPreset.Puree, log);        // отбитый — плотный гладкий, ровно по таре (не «плитой»)
                Look(looks, crumbs, FoodHeapRenderer.HeapPreset.Powder, log);
                EditorUtility.SetDirty(looks);
            }

            log.AppendLine("\nЧТО НУЖНО:");
            foreach (string t in new[] { C.BeefMince, C.PorkMince, "Молоко", "Лук репчатый", "Соль", "Перец чёрный" })
                log.AppendLine(DS.Find(t) != null ? $"✔ «{t}»" : $"✖ нет «{t}»" + (t == C.PorkMince ? " — тогда фарш только говяжий (4 мастерка)" : ""));

            AssetDatabase.SaveAssets();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null) { db.CollectFromProject(); EditorUtility.SetDirty(db); EditorSceneManager.MarkSceneDirty(db.gameObject.scene); log.AppendLine("\n✔ FoodDatabase пересобран — СОХРАНИ СЦЕНУ"); }
            CookRecipeBook.Reload();
            log.AppendLine("\nМАСТЕРКАМИ (±50%), молоко — наливать:");
            log.AppendLine("  0. Хлеб: клавиша 6 (Shift+6 — другой вид) → шлифмашинкой по батону ~4 с → «Мякиш» (светлый, без корки).");
            log.AppendLine($"  1. Хлебная масса (ведро/банка): мякиш целыми батонами (~{C.CrumbN:0} мастерка) + молоко налить → размокает {C.SoakSeconds:0} с → отбойником {C.GrindSeconds:0} с.");
            log.AppendLine($"  2. Фарш для котлет: говяжий фарш {C.BeefN:0} + свиной {C.PorkN:0} + хлебная масса {C.MassN:0} + лук тёртый {C.OnionN:0} + соль {C.SaltN:0} + перец {C.PepperN:0} → миксером {C.MixSeconds:0} с.");
            log.AppendLine($"  3. Отбить: кувалдой по ведру/банке {C.PoundSeconds:0} ударов → «Фарш отбитый» (завитки → гладкая плотная масса).");
            log.AppendLine("\nДальше: 2 (выдели ведро/банку для фарша), 3 (проверка).");
            DS.Done("Котлеты — продукты", log);
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
            log.AppendLine($"＋ {dir}/FoodHeapLooks.asset");
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

        // ───────────────────────── 2. ТАРА ПОД КУВАЛДУ ─────────────────────────

        [MenuItem(Root + "2. Тара: бить кувалдой — отбить фарш (выдели ведро/банку)", priority = 301)]
        public static void SetupPoundTara()
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
            if (list.Count == 0) { EditorUtility.DisplayDialog("Котлеты", "Выдели ведро или банку (объект с Cookware) — в сцене или префаб.", "Ок"); return; }
            foreach (Cookware cw in list)
            {
                GameObject go = cw.gameObject;
                Undo.RegisterFullObjectHierarchyUndo(go, "Котлеты");
                DS.Need<CookwarePoundTarget>(go);
                EditorUtility.SetDirty(go);
                bool repair = false;
                foreach (MonoBehaviour m in go.GetComponentsInChildren<MonoBehaviour>(true)) if (m != null && m.GetType().Name == "PotHammerRepair") { repair = true; break; }
                log.AppendLine($"✔ «{go.name}»: кувалдой по таре — шаг «Отбить»" + (EditorUtility.IsPersistent(go) ? " (префаб)" : " (в сцене; копия префаба — Overrides → Apply)") +
                               (repair ? "\n  ⚠ на ней есть Pot Hammer Repair (кувалда чинит кастрюлю) — удары могут уходить в ремонт. Лучше ведро или банка" : ""));
            }
            if (list[0].gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(list[0].gameObject.scene);
            log.AppendLine("\nФарш для котлет в этой таре → кувалдой по таре 8 ударов → «Фарш отбитый». Содержимое вздрагивает от каждого удара.");
            DS.Done("Котлеты — тара под кувалду", log);
        }

        // ───────────────────────── 4. ЗАГОТОВКА КОТЛЕТЫ ─────────────────────────

        [MenuItem(Root + "4. Котлета — заготовка в сцену (потом сохранить префабом)", priority = 303)]
        public static void MakePattyPiece()
        {
            var log = new StringBuilder();
            ProductDef patty = DS.Find(C.Patty);
            if (patty == null) { EditorUtility.DisplayDialog("Котлеты", "Сначала меню 1 (продукт «Котлета»).", "Ок"); return; }
            Vector3 at = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            GameObject go = BS.SceneRoot("Cutlet_Piece", at);
            int food = LayerMask.NameToLayer("Food");
            if (food >= 0) go.layer = food;
            BS.Get<FishNet.Object.NetworkObject>(go);
            BS.Get<FishNet.Component.Transforming.NetworkTransform>(go);
            BS.ClientAuthNT(go, log);
            Rigidbody rb = BS.Get<Rigidbody>(go);
            rb.mass = 0.6f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            BoxCollider bc = BS.Get<BoxCollider>(go);
            float S = Mathf.Max(0.25f, MeatCutSettings.WorldScale) / 4f;
            bc.center = new Vector3(0f, 0.3f * S, 0f); bc.size = new Vector3(1.2f * S, 0.6f * S, 1.2f * S);
            FoodPiece fp = BS.Get<FoodPiece>(go);
            var so = new SerializedObject(fp);
            DS.SetObj(so, "_product", patty);
            SerializedProperty stp = so.FindProperty("_startState"); if (stp != null) stp.intValue = (int)FoodState.Raw;
            DS.SetInt(so, "_count", 4);
            so.ApplyModifiedPropertiesWithoutUndo();
            CutletPatty cp = BS.Get<CutletPatty>(go);
            cp.EditorSetMaterial(DS.PlateMat(log));
            EditorUtility.SetDirty(cp);
            BS.Get<Suckable>(go);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine("✔ «Cutlet_Piece» в сцене: Food Piece «Котлета» (сырое, 4 порц.) + Cutlet Patty (свой меш: комок → котлета), физика, сеть.");
            log.AppendLine("\nДАЛЬШЕ РУКАМИ:");
            log.AppendLine("  1. Перетащи Cutlet_Piece из сцены в окно Project — префаб.");
            log.AppendLine("  2. Добавь его в Spawnable Prefabs (NetworkManager → DefaultPrefabObjects), если FishNet не добавил сам.");
            log.AppendLine("  3. Выдели префаб в Project → Котлеты → 5. Объект в сцене потом удали.");
            DS.Done("Котлеты — заготовка котлеты", log);
        }

        // ───────────────────────── 5. НАЗНАЧИТЬ ПРЕФАБ ─────────────────────────

        [MenuItem(Root + "5. Назначить префаб котлеты (выдели префаб Cutlet_Piece в Project)", priority = 304)]
        public static void AssignPatty()
        {
            var log = new StringBuilder();
            GameObject prefab = null;
            foreach (Object o in Selection.objects)
            {
                GameObject g = o as GameObject;
                if (g != null && EditorUtility.IsPersistent(g) && g.GetComponent<CutletPatty>() != null) { prefab = g; break; }
            }
            if (prefab == null) { EditorUtility.DisplayDialog("Котлеты", "Выдели в окне Project префаб котлеты (Cutlet_Piece с Cutlet Patty) — сначала меню 4 и перетащить из сцены в Project.", "Ок"); return; }
            CutletSettings st = null;
            foreach (string g in AssetDatabase.FindAssets("t:CutletSettings"))
            {
                st = AssetDatabase.LoadAssetAtPath<CutletSettings>(AssetDatabase.GUIDToAssetPath(g));
                if (st != null) break;
            }
            if (st == null)
            {
                const string dir = "Assets/CatCooks/Resources";
                DS.EnsureDir(dir);
                st = ScriptableObject.CreateInstance<CutletSettings>();
                AssetDatabase.CreateAsset(st, dir + "/CutletSettings.asset");
                log.AppendLine($"＋ {dir}/CutletSettings.asset");
            }
            Undo.RecordObject(st, "Котлеты");
            st.PattyPrefab = prefab;
            EditorUtility.SetDirty(st);
            AssetDatabase.SaveAssets();
            log.AppendLine($"✔ кучка отбитого фарша на столе → «{prefab.name}» (комок, от {st.MinHeapPortions} порц., до {st.MaxPortions})");
            if (prefab.GetComponent<FishNet.Object.NetworkObject>() == null) log.AppendLine("  ✖ у префаба нет Network Object");
            log.AppendLine("Проверь, что префаб в Spawnable Prefabs.");
            DS.Done("Котлеты — префаб котлеты", log);
        }

        // ───────────────────────── 6. МЕШОК СУХАРЕЙ ─────────────────────────

        [MenuItem(Root + "6. Мешок панировочных сухарей — в сцену", priority = 305)]
        public static void MakeSack()
        {
            var log = new StringBuilder();
            ProductDef crumbs = DS.Find(C.Crumbs);
            if (crumbs == null) { EditorUtility.DisplayDialog("Котлеты", "Сначала меню 1 (продукт «Панировочные сухари»).", "Ок"); return; }
            FoodHeap heapPrefab = FindHeapPrefab(log);
            Vector3 at = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            float S = Mathf.Max(0.25f, MeatCutSettings.WorldScale) / 4f;
            float H = 1.2f * S, R = 0.42f * S;
            GameObject go = BS.SceneRoot("Sack_Breadcrumbs", at);
            Mesh m = SaveMesh(SackMesh(H, R), "Sack_Breadcrumbs_Mesh", log);
            DS.Need<MeshFilter>(go).sharedMesh = m;
            DS.Need<MeshRenderer>(go).sharedMaterials = new[] { DS.PlateMat(log) };
            foreach (Collider c in go.GetComponents<Collider>()) if (!(c is BoxCollider)) Object.DestroyImmediate(c);
            BoxCollider bc = DS.Need<BoxCollider>(go); bc.center = m.bounds.center; bc.size = m.bounds.size;
            BS.Get<FishNet.Object.NetworkObject>(go);
            Transform mouth = DS.FindOrCreate(go.transform, "Mouth");
            mouth.localPosition = new Vector3(0f, H * 0.93f, 0f); mouth.localRotation = Quaternion.identity;
            System.Type sackType = null;
            foreach (System.Reflection.Assembly asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                sackType = asm.GetType("CatCooks.Food.Tools.BulkSack");
                if (sackType != null) break;
            }
            if (sackType == null) { log.AppendLine("✖ не нашёл компонент Bulk Sack (мешок сахара) — повесь его руками"); }
            else
            {
                Component bs = go.GetComponent(sackType);
                if (bs == null) bs = go.AddComponent(sackType);
                var so = new SerializedObject(bs);
                DS.SetObj(so, "_product", crumbs);
                SerializedProperty st = so.FindProperty("_state"); if (st != null) st.intValue = (int)FoodState.Scooped;
                SerializedProperty inf = so.FindProperty("_infinite"); if (inf != null) inf.boolValue = true;
                DS.SetInt(so, "_keep", 12);
                DS.SetObj(so, "_mouth", mouth);
                if (heapPrefab != null) DS.SetObj(so, "_heapPrefab", heapPrefab);
                so.ApplyModifiedProperties();
                log.AppendLine($"✔ Bulk Sack: «{crumbs.Title}», бесконечный, горка в горловине" + (heapPrefab != null ? $" (кучка — «{heapPrefab.name}»)" : " — ✖ не нашёл префаб кучки: укажи Heap Prefab руками (тот же, что у мешка сахара/измельчителя)"));
            }
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            log.AppendLine($"✔ «Sack_Breadcrumbs» — мешковина в синюю полоску, этикетка, горловина подвёрнута, сверху сухари. Высота {H * 100f:0} см.");
            log.AppendLine("\nМастерком: ЛКМ по мешку — зачерпнуть сухари; ПКМ — бросить на котлету (или в ведро).");
            log.AppendLine("Повторный запуск обновляет мешок на месте. Сохрани префабом, если нужно больше мешков.");
            DS.Done("Котлеты — мешок сухарей", log);
        }

        /// <summary>Префаб кучки FoodHeap: у мешка сахара в сцене, иначе у измельчителя.</summary>
        private static FoodHeap FindHeapPrefab(StringBuilder log)
        {
            foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null) continue;
                string tn = mb.GetType().Name;
                string[] fields = tn == "BulkSack" ? new[] { "_heapPrefab" } : tn == "ShredderStation" ? new[] { "_gratedPrefab", "_cubesPrefab", "_sticksPrefab", "_pureePrefab" } : null;
                if (fields == null || mb.gameObject.name == "Sack_Breadcrumbs") continue;
                var so = new SerializedObject(mb);
                foreach (string f in fields)
                {
                    SerializedProperty p = so.FindProperty(f);
                    FoodHeap h = p != null ? p.objectReferenceValue as FoodHeap : null;
                    if (h != null) return h;
                }
            }
            return null;
        }

        private static Mesh SaveMesh(Mesh m, string name, StringBuilder log)
        {
            const string dir = "Assets/CatCooks/Food/Cutlets";
            string path = dir + "/" + name + ".asset";
            DS.EnsureDir(dir);
            m.name = name;
            Mesh have = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (have != null) { EditorUtility.CopySerialized(m, have); Object.DestroyImmediate(m); EditorUtility.SetDirty(have); return have; }
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"  ＋ меш {path}");
            return m;
        }

        /// <summary>
        /// Мешок: тело вращения — скруглённое дно, пузатый бок со складками, сужение у горловины, подвёрнутый наружу край;
        /// мешковина с синими полосками, спереди этикетка (бежевая, с тёмной полосой), сверху — сухари.
        /// </summary>
        private static Mesh SackMesh(float H, float R)
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            Color burlap = new Color(0.80f, 0.68f, 0.48f), burlapD = new Color(0.70f, 0.58f, 0.40f), stripe = new Color(0.25f, 0.38f, 0.62f),
                  label = new Color(0.96f, 0.92f, 0.80f), labelBand = new Color(0.72f, 0.32f, 0.16f), crumb = new Color(0.90f, 0.70f, 0.40f);
            const int Seg = 36;
            // профиль (радиус-доля, высота-доля)
            float[,] prof = { { 0f, 0f }, { 0.75f, 0f }, { 0.95f, 0.06f }, { 1.0f, 0.25f }, { 0.98f, 0.55f }, { 0.9f, 0.75f }, { 0.78f, 0.86f }, { 0.74f, 0.9f }, { 0.86f, 0.92f }, { 0.88f, 0.97f }, { 0.76f, 0.98f } };
            int pc = prof.GetLength(0);
            int b0 = v.Count;
            for (int i = 0; i < pc; i++)
                for (int j = 0; j <= Seg; j++)
                {
                    float a = j / (float)Seg * Mathf.PI * 2f;
                    float fold = 1f + 0.035f * Mathf.Sin(a * 7f + i * 1.3f) * (i > 1 && i < pc - 3 ? 1f : 0.3f);   // складки
                    float r = prof[i, 0] * R * fold, y = prof[i, 1] * H;
                    v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                    float yk = prof[i, 1];
                    bool front = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 270f)) < 38f;            // этикетка спереди (−Z)
                    Color col = Color.Lerp(burlap, burlapD, 0.5f + 0.5f * Mathf.Sin(a * 40f + yk * 60f));
                    if (Mathf.Abs(yk - 0.2f) < 0.025f || Mathf.Abs(yk - 0.66f) < 0.025f) col = stripe;
                    if (front && yk > 0.3f && yk < 0.6f) col = Mathf.Abs(yk - 0.45f) < 0.04f ? labelBand : label;
                    if (i >= pc - 3) col = burlapD;                                                       // подвёрнутый край
                    c.Add(col);
                }
            for (int i = 0; i < pc - 1; i++)
                for (int j = 0; j < Seg; j++)
                {
                    int a = b0 + i * (Seg + 1) + j, b = a + Seg + 1;
                    Vector3 mid = (v[a] + v[b + 1]) * 0.5f;
                    Vector3 nn = new Vector3(mid.x, 0f, mid.z);
                    if (i == 0) nn = Vector3.down;
                    BS.Tri(v, t, a, b, a + 1, nn); BS.Tri(v, t, a + 1, b, b + 1, nn);
                }
            // сухари сверху — горка в горловине
            int ctr = v.Count;
            v.Add(new Vector3(0f, H * 0.96f, 0f)); c.Add(crumb);
            int r0 = v.Count;
            for (int j = 0; j < Seg; j++)
            {
                float a = j / (float)Seg * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a) * R * 0.74f, H * 0.9f, Mathf.Sin(a) * R * 0.74f));
                c.Add(Color.Lerp(crumb, crumb * 0.85f, (j % 3) / 2f));
            }
            for (int j = 0; j < Seg; j++) BS.Tri(v, t, ctr, r0 + j, r0 + (j + 1) % Seg, Vector3.up);
            var m = new Mesh();
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // ───────────────────────── 7. ТАРЕЛКА ─────────────────────────

        [MenuItem(Root + "7. Тарелка — подача и звёзды (выдели тарелку)", priority = 306)]
        public static void SetupPlate()
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
            if (list.Count == 0) { EditorUtility.DisplayDialog("Котлеты", "Выдели тарелку (объект с Cookware) — в сцене или префаб.", "Ок"); return; }
            foreach (Cookware cw in list)
            {
                Undo.RegisterFullObjectHierarchyUndo(cw.gameObject, "Котлеты");
                DS.Need<CutletPlate>(cw.gameObject);
                EditorUtility.SetDirty(cw.gameObject);
                log.AppendLine($"✔ «{cw.name}»: тарелка котлет — звёзды над тарелкой" + (EditorUtility.IsPersistent(cw.gameObject) ? " (префаб)" : " (в сцене; копия префаба — Overrides → Apply)"));
            }
            if (list[0].gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(list[0].gameObject.scene);
            log.AppendLine("\nНа тарелку: жареную котлету (лежит собой, тарелка её не «съедает») + пюре мастерком (от 1 мастерка).");
            log.AppendLine("Звёзды: жареная · золотая с двух сторон · в панировке · на сливочном и подсолнечном масле · пюре.");
            DS.Done("Котлеты — тарелка", log);
        }

        // ───────────────────────── 3. ПРОВЕРКА ─────────────────────────

        [MenuItem(Root + "3. Проверка", priority = 302)]
        public static void Check()
        {
            var log = new StringBuilder();
            log.AppendLine("ПРОДУКТЫ:");
            foreach (string t in new[] { C.Crumb, C.BreadMass, C.Mince, C.Pounded, C.Crumbs })
                log.AppendLine(DS.Find(t) != null ? $"✔ «{t}»" : $"✖ нет «{t}» — меню 1");
            foreach (string t in new[] { C.BeefMince, C.PorkMince, "Молоко", "Лук репчатый", "Соль", "Перец чёрный" })
                log.AppendLine(DS.Find(t) != null ? $"✔ «{t}»" : $"✖ нет «{t}»");
            foreach (string t in new[] { "Буханка", "Багет" })
            {
                ProductDef b = DS.Find(t);
                if (b == null) continue;
                bool ok = false;
                foreach (FoodRule r in b.Overrides) if (r.Process == FoodProcess.Peel && r.To == FoodState.Peeled) { ok = true; break; }
                log.AppendLine(ok ? $"✔ «{t}» чистится шлифмашинкой" : $"✖ «{t}» — нет правила «Чистить» (меню 1)");
            }
            log.AppendLine("\nВ СЦЕНЕ:");
            log.AppendLine(Object.FindObjectsByType<CookwarePoundTarget>(FindObjectsSortMode.None).Length > 0 ? "✔ есть тара под кувалду" : "✖ нет тары под кувалду — выдели ведро/банку → меню 2");
            log.AppendLine(Object.FindObjectsByType<BreadMold>(FindObjectsSortMode.None).Length > 0 ? "✔ формы для хлеба есть — клавиша 6 возьмёт хлеб из них" : "⚠ форм для хлеба нет — укажи префабы хлеба в Bonfire Test Keys → Bread Prefabs");
            log.AppendLine(Object.FindFirstObjectByType<Tools.BonfireTestKeys>() != null ? "✔ Bonfire Test Keys в сцене (клавиша 6 — хлеб)" : "✖ нет Bonfire Test Keys — клавиша 6 не работает");
            bool sander = false;
            foreach (MonoBehaviour m in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)) if (m != null && m.GetType().Name == "SanderItem") { sander = true; break; }
            log.AppendLine(sander ? "✔ шлифмашинка в сцене" : "⚠ шлифмашинки нет — корку снимать нечем");
            CutletSettings cs = CutletSettings.Instance;
            log.AppendLine(cs != null && cs.PattyPrefab != null ? $"✔ префаб котлеты «{cs.PattyPrefab.name}» (кучка фарша → комок)" : "✖ нет префаба котлеты — меню 4 → сохранить префаб → меню 5");
            log.AppendLine(DS.Find(C.Patty) != null ? "✔ продукт «Котлета»" : "✖ нет «Котлета» — меню 1");
            log.AppendLine(Object.FindObjectsByType<CutletPlate>(FindObjectsSortMode.None).Length > 0 ? "✔ тарелка котлет есть" : "⚠ нет тарелки котлет — выдели тарелку → меню 7");
            bool butter = DS.Find("Масло сливочное") != null || DS.Find("Сливочное масло") != null;
            log.AppendLine(butter ? "✔ сливочное масло есть" : "⚠ не нашёл «Масло сливочное» — котлета поймёт любое название со словом «сливочн»");
            log.AppendLine("\nВ Play в консоли: «[Рецепты] в книге: … Хлебная масса, Фарш для котлет, Фарш отбитый».");
            DS.Done("Котлеты — проверка", log);
        }
    }
}
#endif
