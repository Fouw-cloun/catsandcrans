using System.Collections.Generic;
using System.Text;
using CatCooks.Food.Supply;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DS = CatCooks.Food.CatCooksDessertSetup;

namespace CatCooks.Food.SupplyEditor
{
    /// <summary>
    /// v24.3: КЛЮКВА — продукт, модель «горсть ягод», префаб и запись в каталог доставки.
    /// Меню CatCooks → Доставка → 7.
    ///
    /// Горсть = 9 глянцевых ягод кучкой (6 внизу, 3 сверху), у каждой тёмная «чашечка» на макушке.
    /// Food Piece «Клюква», Count 5 порций, старт — Сырое. Правила: Сырое/Мытое → Толочь (отбойник) → Пюре.
    /// </summary>
    public static class CatCooksCranberrySetup
    {
        const string Root = "Assets/CatCooks/Supply";
        const string ModelDir = Root + "/Models", MatDir = Root + "/Materials", ProductDir = Root + "/Products";
        const string OldPrefabPath = ProductDir + "/Klyukva_FoodPiece.prefab";
        /// <summary>v24.9: префаб клюквы — в папке базовых префабов (каталог «→ 3» собирается оттуда).</summary>
        public static string PrefabPath
        {
            get
            {
                SupplyCatalog c = AssetDatabase.LoadAssetAtPath<SupplyCatalog>(Root + "/Resources/SupplyCatalog.asset");
                string f = c != null && !string.IsNullOrEmpty(c.BaseFolder) && AssetDatabase.IsValidFolder(c.BaseFolder.TrimEnd('/')) ? c.BaseFolder.TrimEnd('/') : ProductDir;
                return f + "/Klyukva.prefab";
            }
        }
        const int PortionsPerHandful = 5;
        const float Size = 5f;                                            // v25.1: горсть в 5 раз крупнее (игра с гигантизмом)

        [MenuItem("CatCooks/Доставка/5. Клюква: модель, префаб, в базу и каталог", priority = 5)]
        public static void Build()
        {
            var log = new StringBuilder("ДОСТАВКА → 5. Клюква\n");
            Dir(ModelDir); Dir(MatDir); Dir(ProductDir);
            // v24.9: старый префаб переносим в папку базовых (GUID и ссылки сохраняются)
            if (OldPrefabPath != PrefabPath && AssetDatabase.LoadAssetAtPath<GameObject>(OldPrefabPath) != null && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                string err = AssetDatabase.MoveAsset(OldPrefabPath, PrefabPath);
                log.AppendLine(string.IsNullOrEmpty(err) ? $"✎ перенёс {OldPrefabPath} → {PrefabPath}" : $"⚠ не перенёс старый префаб: {err}");
            }

            // 1. продукт
            ProductDef p = EnsureProduct(log);
            if (p == null) { Done(log); return; }
            DS.Rule(p, FoodState.Raw, FoodProcess.Mash, FoodState.Puree, 3f, log);
            DS.Rule(p, FoodState.Washed, FoodProcess.Mash, FoodState.Puree, 3f, log);
            DS.Rule(p, FoodState.Raw, FoodProcess.Wash, FoodState.Washed, 1f, log);

            // 2. меш и материалы
            Mesh mesh = SaveMesh(BuildHandful(), "Cranberry_Handful", log);
            Material berry = Mat("M_Cranberry", new Color(0.66f, 0.04f, 0.11f), true, log);
            Material calyx = Mat("M_Cranberry_Calyx", new Color(0.22f, 0.07f, 0.05f), false, log);

            // 3. префаб
            var go = new GameObject("Klyukva_FoodPiece");
            try
            {
                int food = LayerMask.NameToLayer("Food");
                if (food >= 0) go.layer = food;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = new[] { berry, calyx };
                var col = go.AddComponent<SphereCollider>();
                col.center = new Vector3(0f, 0.032f, 0f) * Size;
                col.radius = 0.048f * Size;
                Rigidbody rb = go.AddComponent<Rigidbody>();
                rb.mass = 0.15f * Size;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                DS.AddByName(go, "NetworkObject", log);
                DS.AddByName(go, "NetworkTransform", log);
                FoodPiece fp = go.AddComponent<FoodPiece>();
                var so = new SerializedObject(fp);
                SetObj(so, "_product", p);
                SetInt(so, "_count", PortionsPerHandful);
                SetInt(so, "_startState", (int)FoodState.Raw);
                so.ApplyModifiedPropertiesWithoutUndo();
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath, out bool ok);
                if (!ok || saved == null) { log.AppendLine($"✖ префаб не сохранился: {PrefabPath}"); Done(log); return; }
                Vector3 hs = SupplyDelivery.MeasurePrefab(saved);
                log.AppendLine($"＋ префаб {PrefabPath} — горсть {hs.x:0.00}×{hs.y:0.00}×{hs.z:0.00} м (×{Size:0}), {PortionsPerHandful} порций");

                // 4. каталог доставки
                SupplyCatalog cat = AssetDatabase.LoadAssetAtPath<SupplyCatalog>(Root + "/Resources/SupplyCatalog.asset");
                if (cat != null)
                {
                    cat.Entries.RemoveAll(e => e == null || e.Prefab == null || e.Product == p);
                    cat.Entries.Add(new SupplyCatalog.Entry
                    {
                        Product = p, Prefab = saved.GetComponent<NetworkObject>(), Source = false, OnPallet = false,
                        Size = SupplyDelivery.MeasurePrefab(saved),
                    });
                    EditorUtility.SetDirty(cat);
                    log.AppendLine("✔ в каталоге доставки: клюква едет горстями в ящике «ФРУКТЫ»");
                }
                else log.AppendLine("⚠ каталога нет — «Доставка → 1» и «→ 3», потом это меню ещё раз");
            }
            finally { Object.DestroyImmediate(go); }

            // 5. база продуктов
            AssetDatabase.SaveAssets();
            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null)
            {
                db.CollectFromProject();
                EditorUtility.SetDirty(db);
                EditorSceneManager.MarkSceneDirty(db.gameObject.scene);
                log.AppendLine("✔ FoodDatabase пересобран — СОХРАНИ СЦЕНУ");
            }
            else log.AppendLine("⚠ в сцене нет FoodDatabase — у него «Collect From Project»");
            log.AppendLine("\nПрефаб должен попасть в Spawnable Prefabs (DefaultPrefabObjects) — FishNet обычно добавляет сам.");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Done(log);
        }

        /// <summary>Продукт «Клюква» (группа — как у овощей/фруктов). Нужен и меню «Ужин дня».</summary>
        internal static ProductDef EnsureProduct(StringBuilder log)
        {
            ProductDef p = DS.Find("Клюква");
            if (p != null) return p;
            FoodGroupDef g = DS.GroupOf("Помидор") ?? DS.GroupOf("Свёкла") ?? DS.GroupOf("Морковь");
            ushort next = (ushort)(DS.MaxId() + 1);
            p = DS.Ensure("Клюква", g, new Color(0.72f, 0.06f, 0.14f), 0.015f, false, DS.ProductsDir(), ref next, log);
            AssetDatabase.SaveAssets();
            return p;
        }

        // ───────────────────────── модель ─────────────────────────

        static Mesh BuildHandful()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
            var berry = new List<int>(); var dark = new List<int>();
            var rnd = new System.Random(17);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

            var centers = new List<(Vector3 c, float r)>();
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI * 2f / 6f + R(-0.2f, 0.2f);
                float r = R(0.017f, 0.020f);
                centers.Add((new Vector3(Mathf.Cos(a) * 0.028f, r * 0.9f, Mathf.Sin(a) * 0.028f), r));
            }
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f + 0.5f + R(-0.2f, 0.2f);
                float r = R(0.017f, 0.020f);
                centers.Add((new Vector3(Mathf.Cos(a) * 0.012f, 0.044f + R(0f, 0.004f), Mathf.Sin(a) * 0.012f), r));
            }

            foreach (var (c, r) in centers)
            {
                // ягода — чуть сплюснутая сфера, ось «макушки» слегка наклонена
                Vector3 up = (Vector3.up + new Vector3(R(-0.5f, 0.5f), 0f, R(-0.5f, 0.5f))).normalized;
                Quaternion q = Quaternion.FromToRotation(Vector3.up, up);
                const int seg = 12, ring = 9;
                int b0 = v.Count;
                for (int y = 0; y <= ring; y++)
                {
                    float t = y / (float)ring, th = t * Mathf.PI;
                    for (int x = 0; x <= seg; x++)
                    {
                        float ph = x / (float)seg * Mathf.PI * 2f;
                        var local = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th) * 0.88f, Mathf.Sin(th) * Mathf.Sin(ph));
                        v.Add(c + q * (local * r));
                        n.Add((q * new Vector3(local.x, local.y / 0.88f, local.z)).normalized);
                        uv.Add(new Vector2(x / (float)seg, 1f - t));
                    }
                }
                for (int y = 0; y < ring; y++)
                    for (int x = 0; x < seg; x++)
                    {
                        int a = b0 + y * (seg + 1) + x, b = a + seg + 1;
                        berry.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                    }
                // «чашечка» — тёмная пятиконечная звёздочка на макушке
                Vector3 top = c + q * (Vector3.up * r * 0.88f) + up * 0.0008f;
                Vector3 t1 = Vector3.Cross(up, Vector3.forward).sqrMagnitude > 1e-4f ? Vector3.Cross(up, Vector3.forward).normalized : Vector3.right;
                Vector3 t2 = Vector3.Cross(up, t1);
                int s0 = v.Count;
                v.Add(top); n.Add(up); uv.Add(new Vector2(0.5f, 0.5f));
                for (int k = 0; k < 10; k++)
                {
                    float ang = k * Mathf.PI / 5f, rr = (k % 2 == 0 ? 0.38f : 0.15f) * r;
                    v.Add(top + (t1 * Mathf.Cos(ang) + t2 * Mathf.Sin(ang)) * rr); n.Add(up); uv.Add(new Vector2(0.5f, 0.5f));
                }
                for (int k = 0; k < 10; k++)
                {
                    dark.AddRange(new[] { s0, s0 + 1 + (k + 1) % 10, s0 + 1 + k });
                    dark.AddRange(new[] { s0, s0 + 1 + k, s0 + 1 + (k + 1) % 10 });   // с обеих сторон — обход не важен
                }
            }
            var m = new Mesh { name = "Cranberry_Handful" };
            for (int i = 0; i < v.Count; i++) v[i] *= Size;
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
            m.subMeshCount = 2;
            m.SetTriangles(berry, 0); m.SetTriangles(dark, 1);
            m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        // ───────────────────────── помощники ─────────────────────────

        static Mesh SaveMesh(Mesh m, string name, StringBuilder log)
        {
            string path = ModelDir + "/" + name + ".asset";
            Mesh have = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (have != null) { EditorUtility.CopySerialized(m, have); Object.DestroyImmediate(m); EditorUtility.SetDirty(have); return have; }
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"＋ меш {path}");
            return m;
        }

        static Material Mat(string name, Color c, bool glossy, StringBuilder log)
        {
            string path = MatDir + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            Shader sh = Shader.Find("CatCooks/Toon Props");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            m = new Material(sh) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (glossy)
            {
                if (m.HasProperty("_SpecIntensity")) m.SetFloat("_SpecIntensity", 1.2f);
                if (m.HasProperty("_SpecSize")) m.SetFloat("_SpecSize", 0.35f);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.75f);
            }
            AssetDatabase.CreateAsset(m, path);
            log.AppendLine($"＋ материал {path}");
            return m;
        }

        static void SetObj(SerializedObject so, string f, Object v) { SerializedProperty p = so.FindProperty(f); if (p != null) p.objectReferenceValue = v; }
        static void SetInt(SerializedObject so, string f, int v)
        {
            SerializedProperty p = so.FindProperty(f);
            if (p == null) return;
            p.intValue = v;
        }

        static void Dir(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            Dir(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        static void Done(StringBuilder log)
        {
            Debug.Log(log.ToString());
            EditorUtility.DisplayDialog("CatCooks — Клюква", log.Length > 2500 ? log.ToString(0, 2500) + "\n…" : log.ToString(), "OK");
        }
    }
}
