using UnityEditor;
using UnityEngine;

// ============================================================================
//  Редактор пульта CatCooksLookPanel.
//  ВАЖНО: файл должен лежать в папке "Editor"
//  (например Assets/Scripts/Editor/CatCooksLookPanelEditor.cs)
// ============================================================================
[CustomEditor(typeof(CatCooksLookPanel))]
public class CatCooksLookPanelEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var p = (CatCooksLookPanel)target;

        EditorGUILayout.HelpBox(
            "Пульт визуала. Выбери Style / Weather / Combo ниже и применяй кнопками.\n" +
            "Ползунки-коррекции работают вживую поверх любого пресета и сохраняются при его смене.",
            MessageType.Info);

        if (p.director == null)
            EditorGUILayout.HelpBox("CatCooksWorldDirector не найден в сцене — пресеты не сработают.", MessageType.Warning);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.backgroundColor = new Color(0.65f, 1f, 0.65f);
            if (GUILayout.Button("Применить СТИЛЬ", GUILayout.Height(28)))
                p.ApplyStyle();
            if (GUILayout.Button("Применить ПОГОДУ", GUILayout.Height(28)))
                p.ApplyWeatherPick();
            GUI.backgroundColor = new Color(1f, 0.88f, 0.5f);
            if (GUILayout.Button("ОБА", GUILayout.Height(28), GUILayout.Width(64)))
                p.ApplyBoth();
            GUI.backgroundColor = Color.white;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.backgroundColor = new Color(0.72f, 0.84f, 1f);
            if (GUILayout.Button("Применить КОМБО ★", GUILayout.Height(24)))
                p.ApplyCombo();
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("Сбросить коррекции", GUILayout.Height(24), GUILayout.Width(160)))
            {
                Undo.RecordObject(p, "Reset Look Corrections");
                p.ResetCorrections();
                EditorUtility.SetDirty(p);
            }
        }

        EditorGUILayout.Space(6);

        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck())
            SceneView.RepaintAll();
    }
}
