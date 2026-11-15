using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CatCooks.Food.Tools;

namespace CatCooks.Food.EditorTools
{
    /// <summary>
    /// v23.1: МУКА — свой профиль вещества (мягкая белая «пудра» кучкой: в таре, на полу, на мастерке)
    /// и мешки сыпучего «как у сахара» (сеть, физика, слой, стоят на полу).
    /// Префабов не создаёт и не меняет: ассеты профиля/материала и объекты открытой сцены.
    /// </summary>
    public static class CatCooksFlourSetup
    {
        const string Dir = "Assets/CatCooks/Food/Substances";
        const string TablePath = Dir + "/Resources/FoodSubstances.asset";
        const string FlourPath = Dir + "/Substance_Flour.asset";
        const string FlourMatPath = Dir + "/Substance_Flour.mat";

        [MenuItem("CatCooks/Вещества/3. Мука: кучка-пудра + мешки как у сахара", priority = 92)]
        public static void Run()
        {
            var log = new StringBuilder("[Мука] настройка:\n");
            FoodSubstance flour = EnsureFlour(log);
            if (flour != null) AddRules(flour, log);
            FixSacks(log);
            Debug.Log(log.ToString());
        }

        // ───────────────────────── ПРОФИЛЬ ─────────────────────────

        static FoodSubstance EnsureFlour(StringBuilder log)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) { log.AppendLine($"  ✖ нет папки {Dir} — сначала CatCooks → Вещества → 1"); return null; }
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(FlourMatPath);
            if (mat == null)
            {
                Shader sh = Shader.Find("CatCooks/Powder Heap");
                if (sh == null) { log.AppendLine("  ✖ нет шейдера «CatCooks/Powder Heap»"); return null; }
                mat = new Material(sh) { name = "Substance_Flour" };
                AssetDatabase.CreateAsset(mat, FlourMatPath);
                log.AppendLine($"  ✔ материал {FlourMatPath}");
            }
            // мука: мягкие крупные бугорки, почти без тёмных щелей, тонкое зерно, светлая холодная тень
            Undo.RecordObject(mat, "Материал муки");
            SetF(mat, "_ClumpScale", 16f);
            SetF(mat, "_SmallScale", 48f);
            SetF(mat, "_ClumpAmt", 0.42f);
            SetF(mat, "_Crevice", 0.22f);
            SetF(mat, "_GrainScale", 300f);
            SetF(mat, "_GrainAmt", 0.05f);
            SetF(mat, "_Ambient", 1.05f);
            if (mat.HasProperty("_ShadowTint")) mat.SetColor("_ShadowTint", new Color(0.82f, 0.81f, 0.88f, 1f));
            EditorUtility.SetDirty(mat);

            FoodSubstance s = AssetDatabase.LoadAssetAtPath<FoodSubstance>(FlourPath);
            bool fresh = s == null;
            if (fresh) s = ScriptableObject.CreateInstance<FoodSubstance>();
            else Undo.RecordObject(s, "Профиль муки");
            s.Title = "Мука";
            s.Look = SubstanceLook.Powder;
            s.FallbackPreset = FoodHeapRenderer.HeapPreset.Powder;
            s.UseProductColor = true;
            s.Brightness = 1.05f;
            s.Material = mat;
            s.InCookware = true;
            s.CookwareRelief = 0.8f;
            s.FloorSlope = 0.55f;
            s.Wonky = 0.35f;
            s.BladeHeight = 1.15f;
            s.Curl = 0f;
            s.Jiggle = 0.04f;
            s.DustOnScoop = true;
            s.DustAmount = 1.6f;
            s.EdgeDrips = true;
            s.DripShake = 20f;
            s.DripSize = 0.06f;
            s.Steam = false;
            s.Flow = SubstanceFlow.Spills;
            s.StickToCookware = false;
            if (fresh) { AssetDatabase.CreateAsset(s, FlourPath); log.AppendLine($"  ✔ профиль {FlourPath}"); }
            else { EditorUtility.SetDirty(s); log.AppendLine("  • профиль «Мука» уже был — значения обновлены"); }
            return s;
        }

        static void SetF(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }

        static void AddRules(FoodSubstance flour, StringBuilder log)
        {
            FoodSubstanceTable table = AssetDatabase.LoadAssetAtPath<FoodSubstanceTable>(TablePath);
            if (table == null) { log.AppendLine($"  ✖ нет таблицы {TablePath} — сначала CatCooks → Вещества → 1"); return; }
            Undo.RecordObject(table, "Правила муки");
            int added = 0;
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef p = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (p == null || string.IsNullOrEmpty(p.Title)) continue;
                string t = p.Title.ToLowerInvariant();
                if (!t.Contains("мук") || t.Contains("тесто") || t.Contains("масл") || t.Contains("паст")) continue;
                bool has = false;
                foreach (FoodSubstanceTable.ByProduct r in table.Products) if (r.Product == p) { has = true; break; }
                if (has) continue;
                table.Products.Add(new FoodSubstanceTable.ByProduct { Product = p, State = FoodState.None, Substance = flour });
                log.AppendLine($"  ✔ правило: продукт «{p.Title}» → Мука");
                added++;
            }
            const string words = "мука, мук";
            bool hasName = false;
            foreach (FoodSubstanceTable.ByName r in table.Names) if (r.Words == words) { hasName = true; break; }
            if (!hasName)
            {
                table.Names.Add(new FoodSubstanceTable.ByName { On = true, Words = words, Except = "тесто, масл, паст", State = FoodState.None, SkipLiquid = true, Substance = flour });
                log.AppendLine("  ✔ правило по словам: «мука» → Мука");
            }
            if (added == 0 && hasName) log.AppendLine("  • правила для муки уже были");
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            FoodSubstanceTable.Reload();
        }

        // ───────────────────────── МЕШКИ ─────────────────────────

        static void FixSacks(StringBuilder log)
        {
            BulkSack[] sacks = Object.FindObjectsByType<BulkSack>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (sacks.Length == 0) { log.AppendLine("  • мешков (BulkSack) в открытой сцене нет"); return; }
            // образец — мешок с физикой (сахар), иначе любой с Rigidbody
            BulkSack refSack = null;
            foreach (BulkSack b in sacks)
            {
                if (b.GetComponent<Rigidbody>() == null) continue;
                if (refSack == null || b.name.ToLowerInvariant().Contains("сахар")) refSack = b;
            }
            Rigidbody refRb = refSack != null ? refSack.GetComponent<Rigidbody>() : null;
            int layer = refSack != null ? refSack.gameObject.layer : LayerMask.NameToLayer("Interactable");
            System.Type nt = System.Type.GetType("FishNet.Component.Transforming.NetworkTransform, FishNet.Runtime");
            log.AppendLine($"  мешков в сцене: {sacks.Length}; образец: {(refSack != null ? "«" + refSack.name + "»" : "нет (беру настройки по умолчанию)")}");

            foreach (BulkSack b in sacks)
            {
                GameObject go = b.gameObject;
                var fixes = new StringBuilder();
                Undo.RegisterFullObjectHierarchyUndo(go, "Мешок как у сахара");
                if (layer >= 0)
                    foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                        if (t.gameObject.layer != layer) { t.gameObject.layer = layer; fixes.Append(" слой;"); break; }
                if (layer >= 0) foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

                if (nt != null && go.GetComponent(nt) == null)
                {
                    Component c = Undo.AddComponent(go, nt);
                    var so = new SerializedObject(c);
                    SerializedProperty ca = so.FindProperty("_clientAuthoritative");
                    if (ca != null) ca.boolValue = true;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    fixes.Append(" NetworkTransform;");
                }
                Rigidbody rb = go.GetComponent<Rigidbody>();
                if (rb == null)
                {
                    rb = Undo.AddComponent<Rigidbody>(go);
                    if (refRb != null)
                    {
                        rb.mass = refRb.mass; rb.linearDamping = refRb.linearDamping; rb.angularDamping = refRb.angularDamping;
                        rb.interpolation = refRb.interpolation; rb.collisionDetectionMode = refRb.collisionDetectionMode;
                    }
                    else rb.mass = 1f;
                    fixes.Append(" Rigidbody;");
                }
                // коллайдер: нужен хоть один твёрдый в иерархии (у муки он был на дочернем «Bag» — оставляем)
                bool solid = false;
                foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) { solid = true; break; }
                if (!solid)
                {
                    Bounds lb;
                    if (LocalBounds(go.transform, out lb))
                    {
                        BoxCollider bc = Undo.AddComponent<BoxCollider>(go);
                        bc.center = lb.center; bc.size = lb.size;
                        fixes.Append(" коллайдер;");
                    }
                }
                // поставить на пол (низ мешка — на поверхность под ним)
                Bounds wb;
                if (WorldBounds(go.transform, out wb))
                {
                    float best = float.MaxValue; float floorY = float.NaN;
                    foreach (RaycastHit h in Physics.RaycastAll(new Vector3(wb.center.x, wb.max.y + 0.5f, wb.center.z), Vector3.down, wb.size.y + 50f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (h.transform.IsChildOf(go.transform)) continue;
                        if (h.point.y > wb.center.y) continue;                     // не крыша над мешком
                        float d = wb.min.y - h.point.y;
                        if (Mathf.Abs(d) < best) { best = Mathf.Abs(d); floorY = h.point.y; }
                    }
                    if (!float.IsNaN(floorY))
                    {
                        float dy = floorY + 0.002f - wb.min.y;
                        if (Mathf.Abs(dy) > 0.003f) { go.transform.position += Vector3.up * dy; fixes.Append($" на пол ({dy:+0.000;-0.000} м);"); }
                    }
                }
                EditorUtility.SetDirty(go);
                log.AppendLine(fixes.Length > 0 ? $"  ✔ «{b.name}»:{fixes}" : $"  • «{b.name}» — уже как надо");
                if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
            }
            log.AppendLine("  Сохрани сцену (Ctrl+S).");
        }

        static bool WorldBounds(Transform root, out Bounds b)
        {
            b = default(Bounds);
            bool any = false;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        static bool LocalBounds(Transform root, out Bounds b)
        {
            b = default(Bounds);
            bool any = false;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = root.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return any;
        }
    }
}
