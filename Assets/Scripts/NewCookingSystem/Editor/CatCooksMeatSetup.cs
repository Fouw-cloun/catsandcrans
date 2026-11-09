#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using FishNet.Component.Transforming;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// CatCooks → Мясо → Настроить выделенную тушу.
    ///
    /// Выдели тушу (Carcass_6_Parts — в сцене или префаб в Project) и нажми.
    /// Сделает всё:
    ///   • на корне: NetworkObject, NetworkTransform, Rigidbody (60 кг), FoodPiece
    ///     (Говядина или Свинина — по имени/пути: Pork/Свин → свинина), «Туша целиком»;
    ///   • FoodCarcass: части = дочерние меши (у каждой свой BoxCollider);
    ///   • префаб куска  Assets/CatCooks/Food/Meat/{Beef|Pork}_CarcassPiece.prefab
    ///     (FoodPiece + SlicePiece с мешами частей — кусок выглядит как отпиленная часть);
    ///   • префаб кости  Assets/CatCooks/Food/Meat/Bone_ForBroth.prefab
    ///     (свой меш кости, FoodPiece «Кости для бульона», «Разделано» — как в рецепте бульона).
    /// Повторный запуск обновляет то же самое. Новые префабы проверь в NetworkManager →
    /// Spawnable Prefabs (FishNet обычно добавляет сам).
    /// </summary>
    public static class CatCooksMeatSetup
    {
        const string Dir = "Assets/CatCooks/Food/Meat";

        [MenuItem("CatCooks/Мясо/Настроить выделенную тушу", priority = 1)]
        public static void SetupSelected()
        {
            if (Selection.gameObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("Мясо", "Выдели тушу (Carcass_6_Parts) в сцене или префаб в Project.", "Ок");
                return;
            }
            EnsureFolder();
            var log = new StringBuilder();
            GameObject bone = EnsureBonePrefab(log);
            foreach (GameObject go in Selection.gameObjects)
            {
                if (EditorUtility.IsPersistent(go))
                {
                    string path = AssetDatabase.GetAssetPath(go);
                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    try { if (SetupOne(root, path, bone, log)) PrefabUtility.SaveAsPrefabAsset(root, path); }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                else
                {
                    Undo.RegisterFullObjectHierarchyUndo(go, "Мясо: туша");
                    SetupOne(go, go.scene.path + "/" + go.name, bone, log);
                    EditorUtility.SetDirty(go);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Мясо]\n" + log);
            EditorUtility.DisplayDialog("Мясо — туша", log.ToString(), "Ок");
        }

        [MenuItem("CatCooks/Мясо/Префабы костей из выделенного набора", priority = 2)]
        public static void BonesFromSet()
        {
            GameObject set = Selection.activeGameObject;
            if (set == null) { EditorUtility.DisplayDialog("Мясо", "Выдели набор костей (Soup_Set_6_Parts) в сцене.", "Ок"); return; }
            EnsureFolder();
            ProductDef bones = Product("Кости для бульона");
            if (bones == null) { EditorUtility.DisplayDialog("Мясо", "Нет продукта «Кости для бульона».", "Ок"); return; }
            int layer = FoodLayer(set.layer);
            var log = new StringBuilder();
            int n = 0;
            foreach (MeshFilter mf in set.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                string path = $"{Dir}/Bone_{Safe(mf.name)}.prefab";
                var go = new GameObject("Bone_" + mf.name) { layer = layer };
                try
                {
                    go.transform.localScale = mf.transform.lossyScale;          // размер как в наборе
                    go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    go.AddComponent<MeshRenderer>().sharedMaterials = mr != null ? mr.sharedMaterials : new Material[0];
                    var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; mc.convex = true;
                    var rb = go.AddComponent<Rigidbody>(); rb.mass = 0.5f; rb.interpolation = RigidbodyInterpolation.Interpolate;
                    go.AddComponent<NetworkObject>();
                    go.AddComponent<NetworkTransform>();
                    FoodPiece fp = go.AddComponent<FoodPiece>();
                    var so = new SerializedObject(fp);
                    Set(so, "_product", bones);
                    SetEnum(so, "_startState", (int)FoodState.Butchered);
                    SetInt(so, "_count", 1);
                    so.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(go, path);
                    log.AppendLine($"＋ {path}");
                    n++;
                }
                finally { Object.DestroyImmediate(go); }
            }
            AssetDatabase.SaveAssets();
            // сразу раздать набор всем тушам в сцене
            GameObject[] all = BoneSet();
            foreach (FoodCarcass c in Object.FindObjectsByType<FoodCarcass>(FindObjectsSortMode.None))
            {
                c.EditorSetup(c.EditorCollectParts(), null, null, all);
                EditorUtility.SetDirty(c);
            }
            log.AppendLine($"Костей: {n}. Набор отдан всем тушам в сцене (выпадает 1–2 на каждый кусок).");
            Debug.Log("[Мясо]\n" + log);
            EditorUtility.DisplayDialog("Мясо — кости", log.ToString(), "Ок");
        }

        private static string Safe(string s)
        {
            foreach (char ch in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
            return s.Replace(' ', '_');
        }

        /// <summary>Все Bone_*.prefab из папки мяса (кроме запасной одиночной).</summary>
        private static GameObject[] BoneSet()
        {
            var list = new List<GameObject>();
            foreach (string g in AssetDatabase.FindAssets("Bone_ t:Prefab", new[] { Dir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (p.EndsWith("Bone_ForBroth.prefab")) continue;
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null && go.GetComponent<FoodPiece>() != null) list.Add(go);
            }
            return list.ToArray();
        }

        /// <summary>Слой для еды: «Food», если есть, иначе слой источника.</summary>
        private static int FoodLayer(int fallback)
        {
            int l = LayerMask.NameToLayer("Food");
            return l >= 0 ? l : fallback;
        }

        [MenuItem("CatCooks/Мясо/Создать префаб кости (запасной)", priority = 3)]
        public static void BoneOnly()
        {
            EnsureFolder();
            var log = new StringBuilder();
            EnsureBonePrefab(log);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Мясо — кость", log.ToString(), "Ок");
        }

        // ───────────────────────── туша ─────────────────────────

        private static bool SetupOne(GameObject root, string where, GameObject bone, StringBuilder log)
        {
            // Какое мясо: 1) уже выставлено руками на FoodPiece — не трогаем;
            // 2) по пути меша/материала частей (…/14_PorkCarcass/…, CCF_15_BeefCarcass); 3) по имени.
            ProductDef meat = null;
            FoodPiece existing = root.GetComponent<FoodPiece>();
            if (existing != null)
            {
                var eso = new SerializedObject(existing);
                ProductDef cur = eso.FindProperty("_product")?.objectReferenceValue as ProductDef;
                if (cur != null && (cur.Title == "Говядина" || cur.Title == "Свинина")) meat = cur;
            }
            bool pork;
            if (meat != null) pork = meat.Title == "Свинина";
            else
            {
                string src = SourceHint(root) + " " + root.name + " " + where;
                src = src.ToLowerInvariant();
                pork = src.Contains("pork") || src.Contains("свин") || src.Contains("pig");
                meat = Product(pork ? "Свинина" : "Говядина");
            }
            if (meat == null) { log.AppendLine($"✖ «{root.name}»: нет продукта {(pork ? "Свинина" : "Говядина")}"); return false; }

            Get<NetworkObject>(root);
            Get<NetworkTransform>(root);
            Rigidbody rb = Get<Rigidbody>(root);
            if (rb.mass < 20f) rb.mass = 60f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            FoodCarcass carcass = Get<FoodCarcass>(root);       // тянет FoodPiece (RequireComponent)
            FoodPiece food = Get<FoodPiece>(root);
            Transform[] parts = carcass.EditorCollectParts();
            var so = new SerializedObject(food);
            Set(so, "_product", meat);
            SetEnum(so, "_startState", (int)FoodState.Carcass);
            SetInt(so, "_count", Mathf.Clamp(parts.Length, 1, 255));
            so.ApplyModifiedPropertiesWithoutUndo();

            // у каждой части — свой коллайдер (по нему пила понимает, какой кусок пилят)
            int added = 0;
            foreach (Transform p in parts)
                if (p.GetComponent<Collider>() == null) { p.gameObject.AddComponent<BoxCollider>(); added++; }

            GameObject piece = EnsurePiecePrefab(parts, pork, meat, root.layer, log);
            GameObject[] set = BoneSet();
            carcass.EditorSetup(parts, piece, bone, set);
            if (set.Length == 0) log.AppendLine("   · набора костей нет — выпадет запасная кость. Сделай набор: выдели Soup_Set_6_Parts → «Префабы костей из выделенного набора»");
            EditorUtility.SetDirty(carcass);
            log.AppendLine($"✔ «{root.name}»: {(pork ? "СВИНИНА" : "ГОВЯДИНА")}, частей {parts.Length}" +
                           (added > 0 ? $", добавлено коллайдеров {added}" : ""));
            foreach (Transform p in parts) log.AppendLine($"     · {p.name}");
            return true;
        }

        /// <summary>Путь меша/материала первой части — там видно, свинья это или корова.</summary>
        private static string SourceHint(GameObject root)
        {
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                string a = mf.sharedMesh != null ? AssetDatabase.GetAssetPath(mf.sharedMesh) : "";
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                string b = mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.name + " " + AssetDatabase.GetAssetPath(mr.sharedMaterial) : "";
                if (a.Length + b.Length > 0) return a + " " + b;
            }
            return "";
        }

        private static GameObject EnsurePiecePrefab(Transform[] parts, bool pork, ProductDef meat, int layer, StringBuilder log)
        {
            string path = $"{Dir}/{(pork ? "Pork" : "Beef")}_CarcassPiece.prefab";
            var meshes = new Mesh[parts.Length];
            Material mat = null;
            for (int i = 0; i < parts.Length; i++)
            {
                MeshFilter mf = parts[i].GetComponent<MeshFilter>();
                meshes[i] = mf != null ? mf.sharedMesh : null;
                if (mat == null) { var mr = parts[i].GetComponent<MeshRenderer>(); if (mr != null) mat = mr.sharedMaterial; }
            }
            var go = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path)) { layer = FoodLayer(layer) };
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = meshes.Length > 0 ? meshes[0] : null;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                go.AddComponent<BoxCollider>();
                var rb = go.AddComponent<Rigidbody>(); rb.mass = 6f; rb.interpolation = RigidbodyInterpolation.Interpolate;
                go.AddComponent<NetworkObject>();
                go.AddComponent<NetworkTransform>();
                FoodPiece fp = go.AddComponent<FoodPiece>();
                var so = new SerializedObject(fp);
                Set(so, "_product", meat);
                SetEnum(so, "_startState", (int)FoodState.Butchered);
                SetInt(so, "_count", 1);
                so.ApplyModifiedPropertiesWithoutUndo();
                SlicePiece sp = go.AddComponent<SlicePiece>();
                sp.EditorSetMeshes(meshes);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(go, path);
                log.AppendLine($"＋ префаб куска: {path}");
                return saved;
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ───────────────────────── кость ─────────────────────────

        private static GameObject EnsureBonePrefab(StringBuilder log)
        {
            string path = $"{Dir}/Bone_ForBroth.prefab";
            ProductDef bones = Product("Кости для бульона");
            if (bones == null) { log.AppendLine("✖ нет продукта «Кости для бульона» — кость не создана"); return AssetDatabase.LoadAssetAtPath<GameObject>(path); }

            string meshPath = $"{Dir}/Bone_Mesh.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null) { mesh = BuildBoneMesh(); AssetDatabase.CreateAsset(mesh, meshPath); }

            string matPath = $"{Dir}/M_Bone.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                Shader sh = Shader.Find("CatCooks/Toon Props");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
                mat = new Material(sh) { name = "M_Bone" };
                Color c = new Color(0.93f, 0.89f, 0.8f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
                AssetDatabase.CreateAsset(mat, matPath);
            }

            var go = new GameObject("Bone_ForBroth") { layer = FoodLayer(0) };
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                var col = go.AddComponent<CapsuleCollider>();
                col.direction = 0; col.radius = 0.045f; col.height = 0.34f;
                var rb = go.AddComponent<Rigidbody>(); rb.mass = 0.6f; rb.interpolation = RigidbodyInterpolation.Interpolate;
                go.AddComponent<NetworkObject>();
                go.AddComponent<NetworkTransform>();
                FoodPiece fp = go.AddComponent<FoodPiece>();
                var so = new SerializedObject(fp);
                Set(so, "_product", bones);
                SetEnum(so, "_startState", (int)FoodState.Butchered);    // рецепт бульона: «Кости, Разделано»
                SetInt(so, "_count", 1);
                so.ApplyModifiedPropertiesWithoutUndo();
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(go, path);
                log.AppendLine($"＋ префаб кости: {path}");
                return saved;
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>Кость: стержень и по два мыщелка на концах. Длина ~0.34 м вдоль X.</summary>
        private static Mesh BuildBoneMesh()
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            // стержень (утолщается к концам)
            {
                int start = v.Count, seg = 12, rings = 10;
                for (int i = 0; i <= rings; i++)
                {
                    float x = Mathf.Lerp(-0.13f, 0.13f, i / (float)rings);
                    float r = 0.022f + Mathf.Pow(Mathf.Abs(x) / 0.13f, 3f) * 0.014f;
                    for (int j = 0; j <= seg; j++)
                    {
                        float b = 2f * Mathf.PI * j / seg;
                        v.Add(new Vector3(x, Mathf.Cos(b) * r, Mathf.Sin(b) * r));
                    }
                }
                for (int i = 0; i < rings; i++)
                    for (int j = 0; j < seg; j++)
                    {
                        int a0 = start + i * (seg + 1) + j, a1 = a0 + 1, b0 = a0 + seg + 1, b1 = b0 + 1;
                        t.Add(a0); t.Add(a1); t.Add(b0); t.Add(a1); t.Add(b1); t.Add(b0);
                    }
            }
            Sphere(v, t, new Vector3(-0.15f, 0.018f, 0f), 0.034f);
            Sphere(v, t, new Vector3(-0.15f, -0.018f, 0f), 0.03f);
            Sphere(v, t, new Vector3(0.15f, 0f, 0.018f), 0.032f);
            Sphere(v, t, new Vector3(0.15f, 0f, -0.018f), 0.032f);
            var m = new Mesh { name = "Bone_Mesh" };
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

    private static void Sphere(List<Vector3> v, List<int> t, Vector3 c, float r)
        {
            int start = v.Count, seg = 12, rings = 8;
            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.PI * i / rings;
                for (int j = 0; j <= seg; j++)
                {
                    float b = 2f * Mathf.PI * j / seg;
                    v.Add(c + new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b)) * r);
                }
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < seg; j++)
                {
                    int a0 = start + i * (seg + 1) + j, a1 = a0 + 1, b0 = a0 + seg + 1, b1 = b0 + 1;
                    t.Add(a0); t.Add(b0); t.Add(a1); t.Add(a1); t.Add(b0); t.Add(b1);
                }
        }

        // ───────────────────────── служебное ─────────────────────────

        private static T Get<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static ProductDef Product(string title)
        {
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef p = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (p != null && p.Title == title) return p;
            }
            return null;
        }

        private static void Set(SerializedObject so, string prop, Object v) { var p = so.FindProperty(prop); if (p != null) p.objectReferenceValue = v; }
        private static void SetInt(SerializedObject so, string prop, int v) { var p = so.FindProperty(prop); if (p != null) p.intValue = v; }
        private static void SetEnum(SerializedObject so, string prop, int v) { var p = so.FindProperty(prop); if (p != null) p.intValue = v; }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/CatCooks/Food")) AssetDatabase.CreateFolder("Assets/CatCooks", "Food");
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/CatCooks/Food", "Meat");
        }
    }
}
#endif
