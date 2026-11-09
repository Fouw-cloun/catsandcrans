using UnityEditor;
using UnityEngine;

// ЭТОТ СКРИПТ ДОЛЖЕН ЛЕЖАТЬ В ПАПКЕ "Editor" (иначе билд не соберётся).
// Добавляет кнопки в инспектор RopeStyle, чтобы применять материал в один клик.
[CustomEditor(typeof(RopeStyle))]
public class RopeStyleEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var style = (RopeStyle)target;

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Apply Material", GUILayout.Height(28)))
            {
                style.Apply();
                EditorUtility.SetDirty(style);
                MarkSceneDirty();
            }
            if (GUILayout.Button("Rebuild Rope", GUILayout.Height(28)))
            {
                style.GetComponent<VerletRope>().Build();
                MarkSceneDirty();
            }
        }

        if (style.material == null)
            EditorGUILayout.HelpBox("Назначь RopeMaterial. Создать: Project → Create → Rope → Rope Material, затем 'Fill From Type'.", MessageType.Info);
    }

    static void MarkSceneDirty()
    {
        if (!Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }
}
