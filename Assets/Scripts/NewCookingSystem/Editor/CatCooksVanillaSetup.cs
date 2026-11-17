#if UNITY_EDITOR
using System;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CatCooks.Food
{
    /// <summary>
    /// v23.28/v23.31: CatCooks → Десерты → Ваниль.
    ///   • продукт «Ваниль» (если нет);
    ///   • рецепт «Мороженое» пересохраняется С ВАНИЛЬЮ: сливки + сахар + желток/яйцо + 1 стручок ванили → перемешать → азот;
    ///   • НОВЫЙ стручок в сцене у камеры — ОДИН объект: сетка и материал из PantryPack 29_Vanilla прямо на нём,
    ///     Rigidbody, Box Collider, NetworkObject, NetworkTransform, слой Food, Food Piece «Ваниль».
    ///     Кинул в ведро — стручок «уходит» в смесь (как яйцо), мешаешь — растворяется вместе со всем.
    /// Префабы не создаются.
    /// </summary>
    public static class CatCooksVanillaSetup
    {
        private const string Title = "Ваниль";

        [MenuItem("CatCooks/Десерты/8. Ваниль: продукт, новый стручок (одним объектом), мороженое с ванилью", priority = 120)]
        public static void Setup()
        {
            var log = new StringBuilder();
            string dir = CatCooksDessertSetup.ProductsDir();
            ushort next = (ushort)(CatCooksDessertSetup.MaxId() + 1);
            FoodGroupDef spice = CatCooksDessertSetup.GroupOf("Соль") ?? CatCooksDessertSetup.GroupOf("Сахар") ?? CatCooksDessertSetup.GroupOf("Перец молотый");
            ProductDef van = CatCooksDessertSetup.Ensure(Title, spice, new Color(0.24f, 0.14f, 0.07f), 0.15f, false, dir, ref next, log);

            // ── рецепт мороженого с ванилью ──
            ProductDef cream = CatCooksDessertSetup.Find("Сливки"), sugar = CatCooksDessertSetup.Find("Сахар");
            ProductDef yolk = CatCooksDessertSetup.Find("Яичный желток"), egg = CatCooksDessertSetup.Find("Яйцо");
            ProductDef ice = CatCooksDessertSetup.Find("Мороженое"), white = CatCooksDessertSetup.Find("Яичный белок");
            ProductDef milk = CatCooksDessertSetup.Find("Молоко");
            if (cream != null && sugar != null && (yolk != null || egg != null) && ice != null)
            {
                CatCooksDessertSetup.EnsureDir(CatCooksDessertSetup.RecipeDir);
                CatCooksDessertSetup.SaveRecipe("Мороженое", r => CookRecipeBook.IceCream(r, cream, sugar, yolk, egg, ice, null, white, milk, van), log);
                log.AppendLine("  сливки ≥ ¼ ведра + сахар 1–2 мастерка + 1–3 желтка/яйца + 1 стручок ванили → перемешать 6 с → азот 2,5 с");
            }
            else log.AppendLine("✖ рецепт мороженого не пересохранён: нет «Сливки», «Сахар», «Яичный желток»/«Яйцо» или «Мороженое»");

            // ── v23.31: НОВЫЙ стручок — ОДИН объект: моделька (сетка + материал) прямо на нём, без детей ──
            if (van != null)
            {
                // v23.33: стручки уже стоят — только добавляем им «погружение в тару», новый не создаём
                int had = 0;
                foreach (FoodPiece f in Object.FindObjectsByType<FoodPiece>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var fso = new SerializedObject(f);
                    SerializedProperty fp0 = fso.FindProperty("_product");
                    if (fp0 == null || fp0.objectReferenceValue != van) continue;
                    had++;
                    if (f.GetComponent<SinkIntoCookware>() == null)
                    {
                        Undo.AddComponent<SinkIntoCookware>(f.gameObject);
                        log.AppendLine($"  ＋ «{f.name}»: Sink Into Cookware — погружается в любую тару");
                    }
                    else log.AppendLine($"  ✔ «{f.name}»: уже погружается в тару");
                }
                if (had == 0)
                {
                    GameObject pod = MakePod(van, log);
                    if (pod != null) Selection.activeGameObject = pod;
                }
                else log.AppendLine($"✔ стручков в сцене: {had} — новый не создаю (нужен ещё — Ctrl+D)");
            }

            AssetDatabase.SaveAssets();
            CookRecipeBook.Reload();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null)
            {
                db.CollectFromProject();
                EditorUtility.SetDirty(db);
                EditorSceneManager.MarkSceneDirty(db.gameObject.scene);
                log.AppendLine("✔ FoodDatabase в сцене пересобран");
            }
            else log.AppendLine("⚠ в открытой сцене нет FoodDatabase — нажми у него «Collect From Project»");
            log.AppendLine("\nСохрани сцену. Стручков нужно больше — дублируй VanillaPod в сцене (Ctrl+D).");
            CatCooksDessertSetup.Done("Ваниль", log);
        }

        /// <summary>Сетка и материал стручка из PantryPack (29_Vanilla/Prefabs/Whole → её моделька).</summary>
        private static bool FindPodLook(out Mesh mesh, out Material[] mats)
        {
            mesh = null; mats = null;
            foreach (string g in AssetDatabase.FindAssets("Whole t:Prefab"))
            {
                string p = AssetDatabase.GUIDToAssetPath(g).Replace('\\', '/');
                if (!p.Contains("/29_Vanilla/")) continue;
                GameObject pf = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (pf == null) continue;
                MeshFilter mf = pf.GetComponentInChildren<MeshFilter>(true);
                MeshRenderer mr = pf.GetComponentInChildren<MeshRenderer>(true);
                if (mf == null || mf.sharedMesh == null) continue;
                mesh = mf.sharedMesh;
                mats = mr != null ? mr.sharedMaterials : null;
                return true;
            }
            foreach (string g in AssetDatabase.FindAssets("t:Mesh"))
            {
                string p = AssetDatabase.GUIDToAssetPath(g).Replace('\\', '/');
                if (!p.Contains("/29_Vanilla/")) continue;
                mesh = AssetDatabase.LoadAssetAtPath<Mesh>(p);
                if (mesh != null) break;
            }
            foreach (string g in AssetDatabase.FindAssets("CCF_29_Vanilla t:Material"))
            {
                Material m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
                if (m != null) { mats = new[] { m }; break; }
            }
            return mesh != null;
        }

        /// <summary>v23.31: стручок одним объектом: сетка, материал, Rigidbody, Box Collider, NetworkObject, NetworkTransform, Food Piece. Сцена, не префаб.</summary>
        private static GameObject MakePod(ProductDef van, StringBuilder log)
        {
            if (!FindPodLook(out Mesh mesh, out Material[] mats))
            {
                log.AppendLine("✖ не нашёл модельку стручка (PantryPack/…/29_Vanilla) — стручок не создан");
                return null;
            }
            // v23.32: берём за основу РАБОЧИЙ продукт из сцены (картошка, лук…) — у него сеть и физика точно настроены
            // и он подбирается. Копию чистим: без детей и лишних компонентов, ставим сетку стручка и продукт «Ваниль».
            GameObject go = CloneTemplate(log);
            bool fresh = go == null;
            if (fresh) go = new GameObject("VanillaPod");
            go.name = "VanillaPod";
            Undo.RegisterCreatedObjectUndo(go, "VanillaPod");
            SceneView sv = SceneView.lastActiveSceneView;
            go.transform.SetParent(null, true);
            go.transform.SetPositionAndRotation((sv != null ? sv.pivot : Vector3.zero) + Vector3.up * 0.3f, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            MeshFilter mfp = go.GetComponent<MeshFilter>();
            if (mfp == null) mfp = go.AddComponent<MeshFilter>();
            mfp.sharedMesh = mesh;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr == null) mr = go.AddComponent<MeshRenderer>();
            if (mats != null && mats.Length > 0) mr.sharedMaterials = mats;
            int l = LayerMask.NameToLayer("Food");
            if (l >= 0) go.layer = l;

            // коллайдер по сетке, но не тоньше 4 см (тонкий лёгкий предмет физика выталкивает — «летает»)
            Bounds b = mesh.bounds;
            float len = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float minT = Mathf.Max(0.04f, len * 0.22f);
            BoxCollider bc = go.GetComponent<BoxCollider>();
            if (bc == null) bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = false;
            bc.center = b.center;
            bc.size = new Vector3(Mathf.Max(minT, b.size.x), Mathf.Max(minT, b.size.y), Mathf.Max(minT, b.size.z));

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.mass = 0.3f;
            rb.linearDamping = 0.3f;
            rb.angularDamping = 0.8f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.maxDepenetrationVelocity = 1.5f;

            AddFishNet(go, "NetworkObject", log);
            AddFishNet(go, "NetworkTransform", log);
            FoodPiece fp = go.GetComponent<FoodPiece>();
            if (fp == null) fp = go.AddComponent<FoodPiece>();
            var so = new SerializedObject(fp);
            if (go.GetComponent<SinkIntoCookware>() == null) go.AddComponent<SinkIntoCookware>();   // v23.33: погружается в любую тару
            SerializedProperty sp = so.FindProperty("_startState");
            if (sp != null) sp.intValue = (int)FoodState.None;
            SerializedProperty pp = so.FindProperty("_product");
            if (pp != null) pp.objectReferenceValue = van;
            SerializedProperty cp = so.FindProperty("_count");
            if (cp != null) cp.intValue = 1;
            so.ApplyModifiedProperties();
            // копия получила тот же SceneId, что у образца — сбрасываем, FishNet выдаст новый (при сохранении сцены)
            FishNet.Object.NetworkObject nob = go.GetComponent<FishNet.Object.NetworkObject>();
            if (nob != null)
            {
                nob.SetSceneId(0);
                EditorUtility.SetDirty(nob);
                AssignSceneId(nob, log);
            }
            EditorUtility.SetDirty(go);

            log.AppendLine($"＋ стручок «VanillaPod» — ОДИН объект (моделька на нём же), у камеры сцены" + (fresh ? "" : " (основа — рабочий продукт из сцены)"));
            log.AppendLine($"  коллайдер {bc.size.x:0.###}×{bc.size.y:0.###}×{bc.size.z:0.###} м, масса 0,3, слой Food, Food Piece «{van.Title}» 1 шт.");
            return go;
        }

        /// <summary>
        /// v23.32: выдать SceneId сразу (FishNet делает это сам в OnValidate, но не чаще раза в 0,25 с — новый объект мог
        /// остаться без id, тогда сервер его не «спавнит» и предмет НЕ ПОДБИРАЕТСЯ). Внутренние методы FishNet — через reflection.
        /// </summary>
        private static void AssignSceneId(FishNet.Object.NetworkObject nob, StringBuilder log)
        {
            const System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public |
                                                      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
            try
            {
                System.Reflection.MethodInfo create = null;
                foreach (System.Reflection.MethodInfo m in typeof(FishNet.Object.NetworkObject).GetMethods(bf))
                    if (m.Name == "CreateSceneId" && m.IsStatic && m.GetParameters().Length == 3) { create = m; break; }
                if (create != null)
                {
                    object[] args = { nob.gameObject.scene, false, 0 };
                    create.Invoke(null, args);
                }
                System.Reflection.MethodInfo rs = typeof(FishNet.Object.NetworkObject).GetMethod("ReserializeEditorSetValues", bf);
                if (rs != null) rs.Invoke(nob, new object[] { true, false });
                bool ok = nob.IsSceneObject;
                log.AppendLine(ok ? "  ✔ сетевой id сцены выдан (сервер будет его спавнить — подбирается)"
                                  : "  ⚠ сетевой id не выдан — сохрани сцену и запусти Tools → Fish-Networking → Utility → Reserialize NetworkObjects");
            }
            catch (Exception e)
            {
                log.AppendLine("  ⚠ не смог выдать сетевой id (" + e.GetType().Name + ") — сохрани сцену и запусти Tools → Fish-Networking → Utility → Reserialize NetworkObjects");
            }
        }

        /// <summary>v23.32: копия рабочего продукта сцены (Food Piece с Rigidbody и коллайдером на корне), очищенная до основы.</summary>
        private static GameObject CloneTemplate(StringBuilder log)
        {
            FoodPiece best = null; int bestN = int.MaxValue;
            foreach (FoodPiece f in Object.FindObjectsByType<FoodPiece>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (f == null || f.name.StartsWith("VanillaPod")) continue;
                if (f.GetComponent<Rigidbody>() == null || f.GetComponent<Collider>() == null) continue;
                if (f.GetComponentsInChildren<FishNet.Object.NetworkObject>(true).Length > 1) continue;
                int n = f.GetComponents<Component>().Length + f.transform.childCount * 3;
                if (n < bestN) { bestN = n; best = f; }
            }
            if (best == null) { log.AppendLine("⚠ в сцене нет рабочего продукта-образца — стручок собран с нуля"); return null; }
            GameObject go = Object.Instantiate(best.gameObject);
            log.AppendLine($"  основа: копия «{best.name}»");
            for (int i = go.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            // всё лишнее — долой (несколько проходов: одни компоненты требуют другие)
            for (int pass = 0; pass < 4; pass++)
                foreach (Component c in go.GetComponents<Component>())
                {
                    if (c == null || c is Transform || c is Rigidbody || c is FoodPiece || c is MeshFilter || c is MeshRenderer) continue;
                    string tn = c.GetType().Name;
                    if (tn == "NetworkObject" || tn == "NetworkTransform" || tn == "FoodStateVisual") continue;
                    try { Object.DestroyImmediate(c); } catch (Exception) { }
                }
            return go;
        }

        private static void AddFishNet(GameObject go, string typeName, StringBuilder log)
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
            if (t == null) { log.AppendLine($"  ⚠ не нашёл {typeName} — добавь руками"); return; }
            Undo.AddComponent(go, t);
            log.AppendLine($"  ＋ {typeName}");
        }
    }
}
#endif
