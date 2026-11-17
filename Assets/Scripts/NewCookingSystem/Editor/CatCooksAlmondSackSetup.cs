#if UNITY_EDITOR
using System;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using DS = CatCooks.Food.CatCooksDessertSetup;
using M = CatCooks.Food.CookRecipeBook.Macaron;
using CatCooks.Food.Tools;

namespace CatCooks.Food
{
    /// <summary>
    /// v23.53: CatCooks → Макарон → 3б. Мешок миндальной муки в сцену.
    /// Копия рабочего мешка сыпучего из сцены (мука или сахар: сеть, физика, слой уже настроены),
    /// продукт — «Мука миндальная», ткань чуть теплее (свой материал-копия Mat_SackAlmond).
    /// Мастерком из мешка: ЛКМ — зачерпнул, ПКМ — в грохот. Мешок бесконечный (как у сахара). Префабы не создаются.
    /// </summary>
    public static class CatCooksAlmondSackSetup
    {
        private static readonly Color Almond = new Color(0.93f, 0.84f, 0.66f, 1f);

        [MenuItem("CatCooks/Макарон/3б. Мешок миндальной муки в сцену", priority = 123)]
        public static void Make()
        {
            var log = new StringBuilder();
            ProductDef almond = DS.Find(M.AlmondFlour);
            if (almond == null)
            {
                EditorUtility.DisplayDialog("Мешок миндальной муки", "Нет продукта «" + M.AlmondFlour + "» — сначала CatCooks → Макарон → 1", "OK");
                return;
            }

            // образец — мешок муки, иначе сахара, иначе любой мешок сыпучего с физикой
            BulkSack refSack = null;
            int score = -1;
            foreach (BulkSack b in Object.FindObjectsByType<BulkSack>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var so0 = new SerializedObject(b);
                ProductDef p = so0.FindProperty("_product").objectReferenceValue as ProductDef;
                if (p == almond) continue;
                string t = p != null && p.Title != null ? p.Title.ToLowerInvariant() : b.name.ToLowerInvariant();
                int sc = (b.GetComponent<Rigidbody>() != null ? 1 : 0) + (t.Contains("мук") ? 4 : t.Contains("сахар") ? 2 : 0);
                if (sc > score) { score = sc; refSack = b; }
            }
            if (refSack == null)
            {
                EditorUtility.DisplayDialog("Мешок миндальной муки", "В сцене нет ни одного мешка сыпучего (Bulk Sack) — образца. Поставь мешок сахара или муки и нажми ещё раз.", "OK");
                return;
            }

            GameObject go = Object.Instantiate(refSack.gameObject);
            go.name = "Мешок миндальной муки";
            Undo.RegisterCreatedObjectUndo(go, "Мешок миндальной муки");
            go.transform.SetParent(refSack.transform.parent, true);
            Vector3 side = refSack.transform.right;
            Renderer rr = refSack.GetComponentInChildren<Renderer>();
            float w = rr != null ? Mathf.Max(rr.bounds.size.x, rr.bounds.size.z) : 0.6f;
            go.transform.position = refSack.transform.position + side * (w * 1.3f);
            log.AppendLine($"＋ «{go.name}» — копия «{refSack.name}» (сеть, физика, слой как у него), рядом с ним");

            BulkSack sack = go.GetComponent<BulkSack>();
            var so = new SerializedObject(sack);
            so.FindProperty("_product").objectReferenceValue = almond;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"  продукт: «{almond.Title}» (мука — кучкой-пудрой, как у муки)");

            // старая горка образца (если её сохранили в сцене) — убрать, мешок насыплет свою
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                Transform c = go.transform.GetChild(i);
                if (c.GetComponent<FishNet.Object.NetworkObject>() != null) { Object.DestroyImmediate(c.gameObject); }
            }

            // ткань теплее: материал-копия
            Renderer big = null; float bigV = -1f;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer)) continue;
                Vector3 s = r.bounds.size;
                float v = s.x * s.y * s.z;
                if (v > bigV) { bigV = v; big = r; }
            }
            if (big != null && big.sharedMaterial != null)
            {
                string path = DS.ModelDir + "/Mat_SackAlmond.mat";
                Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    DS.EnsureDir(DS.ModelDir);
                    m = new Material(big.sharedMaterial) { name = "Mat_SackAlmond" };
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.Lerp(m.GetColor("_BaseColor"), Almond, 0.35f));
                    else if (m.HasProperty("_Color")) m.SetColor("_Color", Color.Lerp(m.GetColor("_Color"), Almond, 0.35f));
                    AssetDatabase.CreateAsset(m, path);
                    log.AppendLine($"  ✔ материал {path} (ткань теплее, миндальный оттенок)");
                }
                Material[] ms = big.sharedMaterials;
                ms[0] = m;
                big.sharedMaterials = ms;
            }

            FishNet.Object.NetworkObject nob = go.GetComponent<FishNet.Object.NetworkObject>();
            if (nob != null)
            {
                nob.SetSceneId(0);                      // копия получила SceneId образца — сбрасываем и выдаём новый
                EditorUtility.SetDirty(nob);
                AssignSceneId(nob, log);
            }
            EditorUtility.SetDirty(go);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            AssetDatabase.SaveAssets();
            log.AppendLine("\nКАК: мастерком ЛКМ по мешку — зачерпнул, ПКМ — в грохот. В грохот — мука миндальная и сахар поровну, " +
                           "потом взять грохот (F) над ведром, зажать ЛКМ и трясти мышью → «Миндальная смесь».");
            log.AppendLine("Сохрани сцену (Ctrl+S).");
            Debug.Log("[Макарон] Мешок миндальной муки\n" + log, go);
            EditorUtility.DisplayDialog("Мешок миндальной муки", log.ToString(), "OK");
        }

        private static void AssignSceneId(FishNet.Object.NetworkObject nob, StringBuilder log)
        {
            const System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public |
                                                      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
            try
            {
                System.Reflection.MethodInfo create = null;
                foreach (System.Reflection.MethodInfo mi in typeof(FishNet.Object.NetworkObject).GetMethods(bf))
                    if (mi.Name == "CreateSceneId" && mi.IsStatic && mi.GetParameters().Length == 3) { create = mi; break; }
                if (create != null) create.Invoke(null, new object[] { nob.gameObject.scene, false, 0 });
                System.Reflection.MethodInfo rs = typeof(FishNet.Object.NetworkObject).GetMethod("ReserializeEditorSetValues", bf);
                if (rs != null) rs.Invoke(nob, new object[] { true, false });
                log.AppendLine(nob.IsSceneObject ? "  ✔ сетевой id сцены выдан"
                                                 : "  ⚠ сетевой id не выдан — сохрани сцену и запусти Tools → Fish-Networking → Utility → Reserialize NetworkObjects");
            }
            catch (Exception e)
            {
                log.AppendLine("  ⚠ не смог выдать сетевой id (" + e.GetType().Name + ") — сохрани сцену и запусти Tools → Fish-Networking → Utility → Reserialize NetworkObjects");
            }
        }
    }
}
#endif
