using System.Collections.Generic;
using System.Text;
using CatCooks.Food.Supply;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCooks.Food.SupplyEditor
{
    /// <summary>
    /// v24.2: инспектор пресета поставки — сырьё, порции и СРАЗУ расчёт: сколько чего, чем приедет
    /// (мешок/банка с запасом, штуки, вёдра), в какие ящики разложится. Кнопки: привезти сейчас (игра, хост),
    /// сделать тестовым (End), добавить в меню уровня.
    /// </summary>
    [CustomEditor(typeof(SupplyPreset))]
    public sealed class SupplyPresetEditor : Editor
    {
        static GUIStyle _wrap, _head, _bad;
        bool _showItems;

        public override void OnInspectorGUI()
        {
            if (_wrap == null)
            {
                _wrap = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };
                _head = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
                _bad = new GUIStyle(_wrap);
                _bad.normal.textColor = new Color(1f, 0.45f, 0.4f);
            }
            var preset = (SupplyPreset)target;

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Title"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Course"), new GUIContent("Место в ужине"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("InMenu"));
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Сколько готовить", _head);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Portions"), new GUIContent("Порций блюда"));
                foreach (int n in new[] { 10, 20, 30, 50 })
                    if (GUILayout.Button(n.ToString(), EditorStyles.miniButton, GUILayout.Width(34))) serializedObject.FindProperty("Portions").intValue = n;
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("BaseServings"), new GUIContent("Строки записаны на (порций)"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ReservePercent"), new GUIContent("Запас на ошибки, %"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("LimitedStock"), new GUIContent("Запас мешков/банок по расчёту"));
            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Lines"), new GUIContent("Сырьё на базовые порции"), true);
            serializedObject.ApplyModifiedProperties();

            // ── расчёт ──
            EditorGUILayout.Space(8);
            SupplyCatalog cat = SupplyCatalog.Instance;
            EditorGUILayout.LabelField($"РАСЧЁТ на {preset.Portions} порц. (×{preset.Portions / (float)Mathf.Max(1, preset.BaseServings):0.##}, запас {preset.ReservePercent}%)", _head);
            if (cat == null) EditorGUILayout.HelpBox("Нет каталога (Resources/SupplyCatalog) — CatCooks → Доставка → 1 и 2. Без него расчёт не знает, чем привезти.", MessageType.Warning);
            SupplyPlan plan = SupplyPlanner.Plan(new List<(SupplyPreset, int)> { (preset, preset.Portions) }, cat);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (plan.Lines.Count == 0) EditorGUILayout.LabelField("строк нет", _wrap);
                foreach (SupplyPlanLine l in plan.Lines)
                {
                    string where = l.Problem != null ? "" : l.Pallet ? "  <color=#9aa>· на площадке</color>" : $"  <color=#9aa>· ящик {SupplyCategories.Title(l.Category)}</color>";
                    string need = l.Pieces ? $"{l.NeedRounded} шт." : $"{l.NeedRounded} порц. ({l.Need / 2f:0.#} маст.)";
                    string note = string.IsNullOrEmpty(l.Notes) ? "" : $"\n      <size=10><color=#888>{l.Notes}</color></size>";
                    if (l.Problem != null) EditorGUILayout.LabelField($"✖ <b>{l.Product?.Title}</b> — {need} → {l.Problem}{note}", _bad);
                    else EditorGUILayout.LabelField($"✔ <b>{l.Product?.Title}</b> — {need} → {l.Via}{where}{note}", _wrap);
                }
            }

            var pallet = new List<SupplyItem>();
            var crates = SupplyDelivery.PackCrates(plan.Items, 120, pallet, cat != null ? cat.CrateKinds() : null);
            EditorGUILayout.LabelField($"ЯЩИКИ: {crates.Count}   ·   на площадке: {pallet.Count}   ·   всего предметов: {plan.Items.Count}", _head);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"маркировка: <b>для: {plan.Tag}</b>", _wrap);
                for (int i = 0; i < crates.Count; i++)
                    EditorGUILayout.LabelField($"▪ {SupplyCategories.Title(crates[i].cat)} №{i + 1} ({(crates[i].kind.Prefab != null ? crates[i].kind.Prefab.name : "ящик")}): {Manifest(crates[i].items)}", _wrap);
                if (pallet.Count > 0) EditorGUILayout.LabelField($"▪ ПЛОЩАДКА: {Manifest(pallet)}", _wrap);
            }

            // ── кнопки ──
            EditorGUILayout.Space(6);
            SupplyDelivery sd = SupplyDelivery.Instance != null ? SupplyDelivery.Instance : Object.FindFirstObjectByType<SupplyDelivery>(FindObjectsInactive.Include);
            using (new EditorGUI.DisabledScope(!Application.isPlaying || sd == null || !sd.IsServerInitialized))
            {
                if (GUILayout.Button(Application.isPlaying ? "▶ ПРИВЕЗТИ СЕЙЧАС (к себе)" : "▶ Привезти сейчас — только в игре (хост)", GUILayout.Height(28)))
                    sd.ServerDeliverPresets(new List<(SupplyPreset, int)> { (preset, preset.Portions) }, SupplyDelivery.LocalPlayer());
            }
            using (new EditorGUI.DisabledScope(sd == null || Application.isPlaying))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Тестовый в сцене (End)")) SetOnDelivery(sd, "_testPreset", preset, false);
                if (GUILayout.Button("Добавить в меню уровня")) SetOnDelivery(sd, "_presets", preset, true);
            }
            if (sd == null) EditorGUILayout.HelpBox("В сцене нет Supply Delivery — CatCooks → Доставка → 3.", MessageType.Info);
        }

        static string Manifest(List<SupplyItem> items)
        {
            var order = new List<string>();
            var n = new Dictionary<string, int>();
            foreach (SupplyItem it in items)
            {
                string t = it.Product != null ? it.Product.Title : "?";
                if (it.Bucket) t += $" (ведро {it.Portions})";
                else if (it.Stock > 0) t += $" ({it.Stock} порц.)";
                if (!n.ContainsKey(t)) { n[t] = 0; order.Add(t); }
                n[t]++;
            }
            var sb = new StringBuilder();
            foreach (string t in order) { if (sb.Length > 0) sb.Append(" · "); sb.Append(n[t] > 1 ? $"{t} ×{n[t]}" : t); }
            return sb.ToString();
        }

        internal static void SetOnDelivery(SupplyDelivery sd, string field, SupplyPreset preset, bool list)
        {
            if (sd == null || preset == null) return;
            var so = new SerializedObject(sd);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) return;
            if (list)
            {
                for (int i = 0; i < p.arraySize; i++) if (p.GetArrayElementAtIndex(i).objectReferenceValue == preset) return;
                p.arraySize++;
                p.GetArrayElementAtIndex(p.arraySize - 1).objectReferenceValue = preset;
            }
            else p.objectReferenceValue = preset;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(sd.gameObject.scene);
            Debug.Log($"[Доставка] «{preset.Title}» → Supply Delivery.{field}. Сохрани сцену.", sd);
        }
    }
}
