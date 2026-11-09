using UnityEditor;
using UnityEngine;

namespace CatCooks.AtmosphereTools
{
    /// <summary>
    /// Инспектор пульта: стиль и погода выбираются ВЫПАДАЮЩИМ СПИСКОМ
    /// с названиями, а не индексом.
    ///
    /// Смысл прост: помнить, что «07» это разрыв грозы, невозможно, а
    /// подбор сочетаний — это десятки переключений подряд. Список с
    /// названиями и стрелки «предыдущий/следующий» превращают перебор
    /// из чтения таблицы в пару кликов.
    /// </summary>
    [CustomEditor(typeof(CatCooksMoodDirector))]
    public class CatCooksMoodDirectorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var d = (CatCooksMoodDirector)target;

            EditorGUILayout.Space(4);
            DrawPicker(d);
            EditorGUILayout.Space(6);

            // Дальше обычный инспектор со всеми полями, кроме индексов —
            // их уже нарисовали сверху в удобном виде.
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script",
                                    "styleIndex", "weatherIndex");
            serializedObject.ApplyModifiedProperties();
        }

        void DrawPicker(CatCooksMoodDirector d)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("ВЫБОР", EditorStyles.boldLabel);

            int newStyle = Row(d, "Стиль", d.styleIndex, d.styles.Count,
                               i => i < d.styles.Count && d.styles[i] != null
                                    ? d.styles[i].title : "(пусто)");
            if (newStyle != d.styleIndex)
            {
                Undo.RecordObject(d, "Style");
                d.SetStyle(newStyle);
                MarkDirty(d);
            }

            int newWeather = Row(d, "Погода", d.weatherIndex, d.weathers.Count,
                                 i => i < d.weathers.Count && d.weathers[i] != null
                                      ? d.weathers[i].title : "(пусто)");
            if (newWeather != d.weatherIndex)
            {
                Undo.RecordObject(d, "Weather");
                d.SetWeather(newWeather);
                MarkDirty(d);
            }

            // Заметка из пресета — чтобы не лазить в ассет за напоминанием,
            // чем эта погода вообще отличается.
            string note = null;
            if (d.weatherIndex < d.weathers.Count && d.weathers[d.weatherIndex] != null)
                note = d.weathers[d.weatherIndex].note;
            if (!string.IsNullOrEmpty(note))
                EditorGUILayout.HelpBox(note, MessageType.None);

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Применить сейчас")) { d.Apply(); MarkDirty(d); }
            if (GUILayout.Button("Забрать в ручные"))
            {
                Undo.RecordObject(d, "Pull");
                d.PullCurrentIntoOverrides();
                MarkDirty(d);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        static int Row(Object o, string label, int index, int count,
                       System.Func<int, string> nameOf)
        {
            if (count == 0)
            {
                EditorGUILayout.LabelField(label, "список пуст");
                return index;
            }

            var names = new string[count];
            for (int i = 0; i < count; i++) names[i] = $"{i:00} · {nameOf(i)}";

            EditorGUILayout.BeginHorizontal();
            int result = EditorGUILayout.Popup(label, Mathf.Clamp(index, 0, count - 1), names);
            if (GUILayout.Button("◀", GUILayout.Width(26)))
                result = (result - 1 + count) % count;
            if (GUILayout.Button("▶", GUILayout.Width(26)))
                result = (result + 1) % count;
            EditorGUILayout.EndHorizontal();

            return result;
        }

        static void MarkDirty(Object o)
        {
            EditorUtility.SetDirty(o);
            if (!Application.isPlaying) SceneView.RepaintAll();
        }
    }
}
