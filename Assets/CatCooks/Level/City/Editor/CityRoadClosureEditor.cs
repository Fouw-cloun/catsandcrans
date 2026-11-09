using UnityEditor;
using UnityEngine;

namespace CatCooks.Level
{
    [CustomEditor(typeof(CityRoadClosure))]
    public sealed class CityRoadClosureEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            CityRoadClosure c = (CityRoadClosure)target;
            EditorGUILayout.HelpBox("Перекрёсток закрыт под кран. Поставь объект примерно в центр перекрёстка и нажми «Построить» — он сам прилипнет и всё перестроит " +
                                    "(передвинул на другой перекрёсток — снова «Построить»: на старом месте машины вернутся на круг). " +
                                    "«Построить»: конусы поперёк всех 4 въездов, барьеры в полоску, знаки; машины соседних модулей разворачиваются перед конусами. " +
                                    "Жёлтый квадрат — площадка под кран (весь перекрёсток).", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
                if (GUILayout.Button("Построить (прилипнуть и перестроить)", GUILayout.Height(28))) { c.Build(); Dirty(c); }
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Убрать конусы", GUILayout.Height(28))) { c.Clear(); Dirty(c); }
            }
            float hx, hz;
            c.HalfRoad(out hx, out hz);
            float m = c.Meter();
            EditorGUILayout.LabelField("Площадка под кран: " + (hx * 2f).ToString("0") + "×" + (hz * 2f).ToString("0") + " ед. (" + (hx * 2f / m).ToString("0") + "×" + (hz * 2f / m).ToString("0") + " м)", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);
            DrawDefaultInspector();
        }

        private static void Dirty(CityRoadClosure c)
        {
            EditorUtility.SetDirty(c);
            if (!EditorApplication.isPlaying && c.gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        }

        [MenuItem("CatCooks/Застройка/Город: перекрёсток под кран (конусы, знаки, разворот машин)", priority = 980)]
        private static void Create()
        {
            CityBlockGenerator g = Object.FindFirstObjectByType<CityBlockGenerator>();
            if (g == null) { EditorUtility.DisplayDialog("Перекрёсток под кран", "Нет генератора застройки (City) в сцене.", "Ок"); return; }
            Vector3 at = g.transform.position;
            if (Selection.activeTransform != null && Selection.activeTransform.GetComponent<CityRoadClosure>() == null) at = Selection.activeTransform.position;
            else if (SceneView.lastActiveSceneView != null) at = SceneView.lastActiveSceneView.pivot;
            GameObject go = new GameObject("CraneCrossing");
            Undo.RegisterCreatedObjectUndo(go, "Перекрёсток под кран");
            go.transform.position = at;
            CityRoadClosure c = go.AddComponent<CityRoadClosure>();
            c.City = g;
            c.Build();
            Selection.activeGameObject = go;
            Dirty(c);
        }
    }
}
