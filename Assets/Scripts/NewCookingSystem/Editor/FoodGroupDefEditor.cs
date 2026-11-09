#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// РЕДАКТОР ГРУППЫ. Правила показаны строками «откуда → чем → куда»,
    /// а не безымянным списком структур, в котором ничего не разобрать.
    ///
    /// Внизу — карта достижимости: видно, куда из стартового состояния можно
    /// добраться, а куда нет. Если продукт где-то застревает, это заметно сразу,
    /// а не через неделю, когда на нём построят рецепт.
    /// </summary>
    [CustomEditor(typeof(FoodGroupDef))]
    public class FoodGroupDefEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            FoodGroupDef g = (FoodGroupDef)target;
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("Title"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Note"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("StartState"),
                new GUIContent("Начальное состояние",
                    "С него начинается путь любого продукта этой группы"));

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"Правила перехода — {g.Rules.Count} шт",
                EditorStyles.boldLabel);

            SerializedProperty rules = serializedObject.FindProperty("Rules");
            int remove = -1;

            for (int i = 0; i < rules.arraySize; i++)
            {
                SerializedProperty el = rules.GetArrayElementAtIndex(i);
                SerializedProperty from = el.FindPropertyRelative("From");
                SerializedProperty proc = el.FindPropertyRelative("Process");
                SerializedProperty to = el.FindPropertyRelative("To");
                SerializedProperty sec = el.FindPropertyRelative("Seconds");
                SerializedProperty win = el.FindPropertyRelative("Window");
                SerializedProperty early = el.FindPropertyRelative("TooEarly");
                SerializedProperty late = el.FindPropertyRelative("TooLate");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // строка «откуда → чем → куда»
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(from, GUIContent.none);
                EditorGUILayout.LabelField("→", GUILayout.Width(16f));
                EditorGUILayout.PropertyField(proc, GUIContent.none);
                EditorGUILayout.LabelField("→", GUILayout.Width(16f));
                EditorGUILayout.PropertyField(to, GUIContent.none);
                if (GUILayout.Button("×", GUILayout.Width(24f))) remove = i;
                EditorGUILayout.EndHorizontal();

                // время и окно
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("секунд", GUILayout.Width(52f));
                sec.floatValue = EditorGUILayout.FloatField(sec.floatValue, GUILayout.Width(52f));
                EditorGUILayout.LabelField("окно", GUILayout.Width(38f));
                win.floatValue = EditorGUILayout.Slider(win.floatValue, 0f, 1f);
                EditorGUILayout.EndHorizontal();

                bool hasWindow = win.floatValue > 0.001f && sec.floatValue > 0.001f;
                if (hasWindow)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("недо", GUILayout.Width(52f));
                    EditorGUILayout.PropertyField(early, GUIContent.none);
                    EditorGUILayout.LabelField("пере", GUILayout.Width(38f));
                    EditorGUILayout.PropertyField(late, GUIContent.none);
                    EditorGUILayout.EndHorizontal();

                    if ((FoodState)late.intValue == FoodState.None)
                        EditorGUILayout.HelpBox(
                            "Окно задано, а «пере» — нет. Пересидеть невозможно, " +
                            "значит окно ни на что не влияет.", MessageType.Warning);

                    float lo = sec.floatValue * (1f - win.floatValue);
                    float hi = sec.floatValue * (1f + win.floatValue);
                    EditorGUILayout.LabelField(
                        $"попадание с {lo:0.#} по {hi:0.#} сек", EditorStyles.miniLabel);
                }
                else if (sec.floatValue > 0.001f)
                {
                    EditorGUILayout.LabelField(
                        "без окна: перестараться нельзя", EditorStyles.miniLabel);
                }

                EditorGUILayout.EndVertical();
            }

            if (remove >= 0) rules.DeleteArrayElementAtIndex(remove);

            if (GUILayout.Button("Добавить правило"))
                rules.InsertArrayElementAtIndex(rules.arraySize);

            if (serializedObject.ApplyModifiedProperties())
                FoodRecipeDefEditor.ClearCache();   // список состояний в рецептах устарел

            EditorGUILayout.Space(10f);
            DrawReach(g);
        }

        /// <summary>
        /// Карта достижимости из стартового состояния. Главный инструмент
        /// отладки данных: сразу видно и тупики, и недостижимое.
        /// </summary>
        private void DrawReach(FoodGroupDef g)
        {
            EditorGUILayout.LabelField("Куда можно добраться", EditorStyles.boldLabel);

            HashSet<FoodState> seen = new HashSet<FoodState> { g.StartState };
            Queue<FoodState> q = new Queue<FoodState>();
            q.Enqueue(g.StartState);
            List<string> lines = new List<string>();

            while (q.Count > 0)
            {
                FoodState cur = q.Dequeue();
                foreach (FoodRule r in g.Rules)
                {
                    if (r.From != cur || r.To == FoodState.None) continue;
                    if (!seen.Add(r.To)) continue;
                    q.Enqueue(r.To);
                    lines.Add($"{FoodStateInfo.NameOf(cur)} → " +
                              $"{FoodStateInfo.NameOf(r.To)}  ({FoodStateInfo.NameOf(r.Process)})");
                }
            }

            EditorGUILayout.HelpBox(
                $"Достижимо состояний: {seen.Count}\n" + string.Join("\n", lines),
                MessageType.None);

            // правила, которые никогда не сработают
            List<string> dead = new List<string>();
            foreach (FoodRule r in g.Rules)
                if (r.From != FoodState.None && !seen.Contains(r.From))
                    dead.Add($"{FoodStateInfo.NameOf(r.From)} → {FoodStateInfo.NameOf(r.To)}");

            if (dead.Count > 0)
                EditorGUILayout.HelpBox(
                    "МЁРТВЫЕ ПРАВИЛА — в это состояние никак не попасть:\n· " +
                    string.Join("\n· ", dead), MessageType.Warning);

            // тупики
            List<string> stuck = new List<string>();
            foreach (FoodState s in seen)
            {
                if (FoodStateInfo.IsSpoiled(s) || s == FoodState.Dissolved) continue;
                bool onward = false;
                foreach (FoodRule r in g.Rules)
                    if (r.From == s) { onward = true; break; }
                if (!onward) stuck.Add(FoodStateInfo.NameOf(s));
            }
            if (stuck.Count > 0)
                EditorGUILayout.HelpBox(
                    "Тупики (нормально, если это конечные состояния): " +
                    string.Join(", ", stuck), MessageType.None);
        }
    }
}
#endif
