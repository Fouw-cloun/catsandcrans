using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCooks.Food.EditorTools
{
    /// <summary>
    /// v23.3: ГОРКА ВНУТРИ ФОРМЫ ТАРЫ. У некоторых тар (ковш) HeapAnchor стоит ниже дна или его Footprint шире дна —
    /// добавленное (масло, крошка) торчит из-под тары. Меню ставит HeapAnchor туда же, где дно жидкости
    /// (Cookware Liquid Visual — её форма уже подогнана под тару), и делает Footprint / Max Height / Flare Top по этой форме.
    /// Работает на выделенных тарах, а если ничего не выделено — на всех тарах открытой сцены. Префабы не создаёт.
    /// </summary>
    public static class CatCooksHeapFit
    {
        [MenuItem("CatCooks/Тара/Горку — внутрь формы тары (выделенные или все)", priority = 60)]
        public static void Fit()
        {
            var log = new StringBuilder("[Тара] горка по форме тары:\n");
            Cookware[] list;
            var sel = Selection.GetFiltered<Cookware>(SelectionMode.Editable | SelectionMode.Deep);
            list = sel != null && sel.Length > 0 ? sel : Object.FindObjectsByType<Cookware>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int n = 0;
            foreach (Cookware cw in list)
            {
                if (cw == null || EditorUtility.IsPersistent(cw)) continue;
                CookwareHeapVisual hv = cw.GetComponentInChildren<CookwareHeapVisual>(true);
                CookwareLiquidVisual lv = cw.GetComponentInChildren<CookwareLiquidVisual>(true);
                if (hv == null || lv == null) continue;
                FoodHeapRenderer hr = hv.GetComponent<FoodHeapRenderer>();
                if (hr == null) continue;
                Vector2 bottom = lv.BottomHalfSizeWorld, rim = lv.RimHalfSizeWorld;
                float height = lv.RimHeightWorld;
                if (bottom.x <= 1e-4f || bottom.y <= 1e-4f || height <= 1e-4f) { log.AppendLine($"  ! «{cw.name}»: у жидкости нет формы (запусти разок Play или Cookware Auto Setup)"); continue; }

                Undo.RecordObject(hv.transform, "Горка по форме тары");
                Undo.RecordObject(hr, "Горка по форме тары");
                Transform a = hv.transform;
                Vector3 was = a.position;
                a.SetPositionAndRotation(lv.transform.position + lv.transform.up * 0.002f * Mathf.Max(1f, height), lv.transform.rotation);

                var so = new SerializedObject(hr);
                bool world = so.FindProperty("_ignoreParentScale") != null && so.FindProperty("_ignoreParentScale").boolValue;
                Vector3 ls = a.lossyScale;
                Vector2 toUnits = world ? Vector2.one : new Vector2(1f / Mathf.Max(1e-5f, ls.x), 1f / Mathf.Max(1e-5f, ls.z));
                float toH = world ? 1f : 1f / Mathf.Max(1e-5f, ls.y);
                Vector2 fp = Vector2.Scale(bottom * 0.88f, toUnits);
                SetV2(so, "_footprint", fp);
                SetF(so, "_maxHeight", height * 0.95f * toH);
                float flare = Mathf.Clamp(Mathf.Min(rim.x / Mathf.Max(1e-4f, bottom.x), rim.y / Mathf.Max(1e-4f, bottom.y)), 0.5f, 2.5f);
                SetF(so, "_flareTop", flare);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(hr);
                if (cw.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(cw.gameObject.scene);
                log.AppendLine($"  ✔ «{cw.name}»: HeapAnchor сдвинут на {(a.position - was).magnitude:0.000} м, Footprint {fp.x:0.###}×{fp.y:0.###}, " +
                               $"Max Height {height * 0.95f * toH:0.###}, Flare Top {flare:0.##}");
                n++;
            }
            if (n == 0) log.AppendLine("  • тар с HeapAnchor и LiquidAnchor не найдено");
            log.AppendLine("  Сохрани сцену (Ctrl+S). Если стало хуже — Ctrl+Z.");
            Debug.Log(log.ToString());
        }

        static void SetF(SerializedObject so, string name, float v) { SerializedProperty p = so.FindProperty(name); if (p != null) p.floatValue = v; }
        static void SetV2(SerializedObject so, string name, Vector2 v) { SerializedProperty p = so.FindProperty(name); if (p != null) p.vector2Value = v; }
    }
}
