#if UNITY_EDITOR
using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// CatCooks → Хлеб → 5. Авто: формы и хлеб (выдели формы и модели хлеба в сцене).
    ///
    /// ФОРМА (Mold_Baguette / Mold_Loaf, или всё, где есть Bread Mold / «mold» в имени):
    ///   • Bread Mold (если нет), Rigidbody, NetworkObject, NetworkTransform;
    ///   • дочерний «Fill» — модель хлеба (Bread_Baguette / Bread_Loaf из Assets/Models/Bread) ровно в форме,
    ///     материал корочки;
    ///   • триггер внутри формы (ловит кусок теста) и коллайдер корпуса, если его нет;
    ///   • продукт, материал теста; Bread Prefab — если префаб хлеба уже есть в проекте.
    /// ХЛЕБ (модель Bread_Baguette / Bread_Loaf в сцене):
    ///   • NetworkObject, NetworkTransform, Rigidbody, Box Collider, слой Food, Food Piece (Багет/Буханка, Запечённое),
    ///     корочка, Food Hot Steam.
    ///   Потом перетащи его в Project → префаб BreadBaguette / BreadLoaf → Spawnable Prefabs,
    ///   и нажми меню ещё раз на формах — Bread Prefab подставится сам.
    /// Префабы не создаёт.
    /// </summary>
    public static class CatCooksBreadMoldSetup
    {
        const string ModelDir = "Assets/Models/Bread";

        [MenuItem("CatCooks/Хлеб/5. Авто: формы и хлеб (выдели формы и модели хлеба)", priority = 84)]
        public static void Run()
        {
            var log = new StringBuilder();
            if (Selection.gameObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("Хлеб", "Выдели в сцене формы (Mold_Baguette, Mold_Loaf) и/или модели хлеба (Bread_Baguette, Bread_Loaf).", "Ок");
                return;
            }
            ProductDef bag = FindProduct("Багет"), loaf = FindProduct("Буханка");
            if (bag == null || loaf == null) { EditorUtility.DisplayDialog("Хлеб", "Сначала «CatCooks → Хлеб → 1. Создать продукты, рецепт и материалы».", "Ок"); return; }

            foreach (GameObject go in Selection.gameObjects)
            {
                string n = go.name.ToLowerInvariant();
                bool isBag = n.Contains("bag") || n.Contains("багет");
                bool isLoaf = n.Contains("loaf") || n.Contains("бух") || n.Contains("brick");
                bool isMold = go.GetComponent<BreadMold>() != null || n.Contains("mold") || n.Contains("форм");
                if (!isBag && !isLoaf)
                {
                    log.AppendLine($"— «{go.name}»: не понял, багет или буханка (в имени должно быть Baguette или Loaf) — пропускаю");
                    continue;
                }
                ProductDef p = isBag ? bag : loaf;
                if (isMold) SetupMold(go, isBag, p, log);
                else SetupBread(go, isBag, p, log);
                EditorUtility.SetDirty(go);
            }
            log.AppendLine("\nСохрани сцену / префабы (Overrides → Apply All).");
            Debug.Log("[Хлеб — авто: формы и хлеб]\n" + log);
            EditorUtility.DisplayDialog("Хлеб", log.ToString(), "Ок");
        }

        // ───────────────────────── ФОРМА ─────────────────────────

        private static void SetupMold(GameObject go, bool isBag, ProductDef product, StringBuilder log)
        {
            log.AppendLine($"— форма «{go.name}» ({product.Title})");
            BreadMold mold = go.GetComponent<BreadMold>();
            if (mold == null) { mold = Undo.AddComponent<BreadMold>(go); log.AppendLine("  ＋ Bread Mold"); }
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null) { rb = Undo.AddComponent<Rigidbody>(go); rb.mass = 1.5f; log.AppendLine("  ＋ Rigidbody (1.5 кг)"); }
            AddByName(go, "NetworkObject", log);
            AddByName(go, "NetworkTransform", log);
            SetLayer(go, "Interactable", log);

            var so = new SerializedObject(mold);
            Transform fill = so.FindProperty("_fill").objectReferenceValue as Transform;
            if (fill == null) fill = FindDeep(go.transform, "Fill");
            Material crust = AssetDatabase.LoadAssetAtPath<Material>($"{ModelDir}/{(isBag ? "Mat_BaguetteCrust" : "Mat_LoafCrust")}.mat");
            if (fill == null)
            {
                string model = isBag ? "Bread_Baguette" : "Bread_Loaf";
                GameObject asset = LoadModel(model);
                if (asset == null) log.AppendLine($"  ✖ не нашёл модель {model} (папка {ModelDir}) — Fill не создан");
                else
                {
                    Transform parent = ModelRoot(go.transform);
                    GameObject inst = PrefabUtility.InstantiatePrefab(asset, parent) as GameObject;
                    if (inst == null) inst = UnityEngine.Object.Instantiate(asset, parent);
                    Undo.RegisterCreatedObjectUndo(inst, "Fill");
                    inst.name = "Fill";
                    inst.transform.localPosition = isBag ? new Vector3(0f, 0.022f, 0f) : new Vector3(0f, 0.004f, 0f);
                    inst.transform.localRotation = Quaternion.identity;
                    inst.transform.localScale = Vector3.one;
                    foreach (Collider c in inst.GetComponentsInChildren<Collider>(true)) Undo.DestroyObjectImmediate(c);
                    fill = inst.transform;
                    log.AppendLine($"  ＋ Fill — модель {model} внутри формы (под «{parent.name}»)");
                }
            }
            else log.AppendLine($"  ✔ Fill: «{fill.name}»");
            if (fill != null && crust != null)
                foreach (Renderer r in fill.GetComponentsInChildren<Renderer>(true)) { Undo.RecordObject(r, "crust"); r.sharedMaterial = crust; }

            // триггер внутри формы и корпус
            if (fill != null)
            {
                Bounds fb = LocalBounds(go.transform, fill, null);
                bool hasTrigger = false, hasBody = false;
                foreach (Collider c in go.GetComponents<Collider>()) { if (c.isTrigger) hasTrigger = true; else hasBody = true; }
                foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                    if (!c.isTrigger && !c.transform.IsChildOf(fill)) hasBody = true;
                if (!hasTrigger)
                {
                    BoxCollider t = Undo.AddComponent<BoxCollider>(go);
                    t.isTrigger = true;
                    Vector3 size = new Vector3(fb.size.x * 1.15f, fb.size.y * 2f, fb.size.z * 1.05f);
                    t.center = new Vector3(fb.center.x, fb.min.y + size.y * 0.5f, fb.center.z);
                    t.size = size;
                    log.AppendLine("  ＋ триггер внутри формы (ловит кусок теста)");
                }
                else log.AppendLine("  ✔ триггер есть");
                if (!hasBody)
                {
                    Bounds mb = LocalBounds(go.transform, go.transform, fill);
                    BoxCollider b = Undo.AddComponent<BoxCollider>(go);
                    b.center = new Vector3(mb.center.x, mb.min.y + mb.size.y * 0.2f, mb.center.z);
                    b.size = new Vector3(mb.size.x, mb.size.y * 0.4f, mb.size.z);
                    log.AppendLine("  ＋ коллайдер корпуса (низ формы — на нём стоит)");
                }
            }

            so.FindProperty("_fill").objectReferenceValue = fill;
            so.FindProperty("_breadProduct").objectReferenceValue = product;
            Material dough = AssetDatabase.LoadAssetAtPath<Material>($"{ModelDir}/Mat_Dough.mat");
            if (dough != null) so.FindProperty("_doughMaterial").objectReferenceValue = dough;
            SerializedProperty bp = so.FindProperty("_breadPrefab");
            if (bp.objectReferenceValue == null)
            {
                GameObject prefab = FindBreadPrefab(product);
                if (prefab != null) { bp.objectReferenceValue = prefab; log.AppendLine($"  ✔ Bread Prefab = «{prefab.name}»"); }
                else log.AppendLine($"  ⚠ префаба хлеба «{product.Title}» ещё нет: выдели модель {(isBag ? "Bread_Baguette" : "Bread_Loaf")} в сцене → это же меню → " +
                                    $"перетащи в Project (префаб {(isBag ? "BreadBaguette" : "BreadLoaf")}) → Spawnable Prefabs → нажми меню ещё раз на форме");
            }
            else log.AppendLine($"  ✔ Bread Prefab = «{bp.objectReferenceValue.name}»");
            so.ApplyModifiedProperties();
        }

        // ───────────────────────── ХЛЕБ ─────────────────────────

        private static void SetupBread(GameObject go, bool isBag, ProductDef product, StringBuilder log)
        {
            log.AppendLine($"— хлеб «{go.name}» ({product.Title})");
            AddByName(go, "NetworkObject", log);
            AddByName(go, "NetworkTransform", log);
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null) { rb = Undo.AddComponent<Rigidbody>(go); rb.mass = 0.5f; log.AppendLine("  ＋ Rigidbody (0.5 кг)"); }
            if (go.GetComponentInChildren<Collider>(true) == null)
            {
                Bounds b = LocalBounds(go.transform, go.transform, null);
                BoxCollider c = Undo.AddComponent<BoxCollider>(go);
                c.center = b.center; c.size = b.size;
                log.AppendLine("  ＋ Box Collider по хлебу");
            }
            SetLayer(go, "Food", log);
            FoodPiece fp = go.GetComponent<FoodPiece>();
            if (fp == null) { fp = Undo.AddComponent<FoodPiece>(go); log.AppendLine("  ＋ Food Piece"); }
            var so = new SerializedObject(fp);
            so.FindProperty("_product").objectReferenceValue = product;
            so.FindProperty("_startState").intValue = (int)FoodState.Baked;
            so.ApplyModifiedProperties();
            log.AppendLine($"  ✔ продукт «{product.Title}», состояние Запечённое");
            Material crust = AssetDatabase.LoadAssetAtPath<Material>($"{ModelDir}/{(isBag ? "Mat_BaguetteCrust" : "Mat_LoafCrust")}.mat");
            if (crust != null)
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) { Undo.RecordObject(r, "crust"); r.sharedMaterial = crust; }
            if (go.GetComponent<FoodHotSteam>() == null) { Undo.AddComponent<FoodHotSteam>(go); log.AppendLine("  ＋ Food Hot Steam (пар от свежего хлеба)"); }
            log.AppendLine($"  → перетащи «{go.name}» в Project, назови {(isBag ? "BreadBaguette" : "BreadLoaf")} (латиницей, без пробелов) → NetworkManager → Spawnable Prefabs");
        }

        // ───────────────────────── помощники ─────────────────────────

        private static ProductDef FindProduct(string title)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef p = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (p != null && string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }

        private static GameObject LoadModel(string name)
        {
            GameObject g = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{name}.obj");
            if (g != null) return g;
            foreach (string guid in AssetDatabase.FindAssets(name + " t:Model"))
            {
                g = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (g != null) return g;
            }
            return null;
        }

        /// <summary>Префаб хлеба этого продукта: Food Piece с этим продуктом и без Bread Mold.</summary>
        private static GameObject FindBreadPrefab(ProductDef product)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                GameObject g = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (g == null || g.GetComponent<BreadMold>() != null) continue;
                FoodPiece fp = g.GetComponent<FoodPiece>();
                if (fp == null) continue;
                var so = new SerializedObject(fp);
                if (so.FindProperty("_product").objectReferenceValue == product) return g;
            }
            return null;
        }

        /// <summary>Куда класть Fill: к самой модели формы (родитель её мешей), чтобы координаты совпали.</summary>
        private static Transform ModelRoot(Transform root)
        {
            Renderer best = null;
            float vol = -1f;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || (r.gameObject.hideFlags & HideFlags.DontSave) != 0) continue;
                Vector3 s = r.bounds.size;
                float v = s.x * s.y * s.z;
                if (v > vol) { vol = v; best = r; }
            }
            if (best == null || best.transform == root) return root;
            Transform p = best.transform.parent;
            return p != null && (p == root || p.IsChildOf(root)) ? p : root;
        }

        /// <summary>Габариты мешей under (кроме skip) в осях space.</summary>
        private static Bounds LocalBounds(Transform space, Transform under, Transform skip)
        {
            bool any = false;
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            foreach (Renderer r in under.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (skip != null && r.transform.IsChildOf(skip)) continue;
                Bounds wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 l = space.InverseTransformPoint(c);
                    if (!any) { b = new Bounds(l, Vector3.zero); any = true; } else b.Encapsulate(l);
                }
            }
            if (!any) b = new Bounds(Vector3.zero, Vector3.one * 0.1f);
            return b;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t != root && t.name == name) return t;
            return null;
        }

        private static void SetLayer(GameObject go, string layer, StringBuilder log)
        {
            int l = LayerMask.NameToLayer(layer);
            if (l < 0 || go.layer == l) return;
            Undo.RecordObject(go, "layer");
            go.layer = l;
            log.AppendLine($"  ✔ слой → {layer}");
        }

        /// <summary>Добавить компонент FishNet по имени типа, если его нет.</summary>
        private static void AddByName(GameObject go, string typeName, StringBuilder log)
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
