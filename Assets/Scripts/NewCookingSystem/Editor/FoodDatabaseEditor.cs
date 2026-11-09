#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Инспектор реестра. Нужен ради одной кнопки: список продуктов надо чем-то
    /// заполнить, а руками перетаскивать 38 ассетов — не дело.
    ///
    /// Собирать надо ЗАРАНЕЕ, в редакторе: в собранной игре поиска по ассетам
    /// нет, и список должен быть уже проставлен в префабе или сцене.
    /// </summary>
    [CustomEditor(typeof(FoodDatabase))]
    public class FoodDatabaseEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Наполнение", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Список продуктов должен быть заполнен ДО сборки игры: " +
                "в билде поиска по ассетам нет.", MessageType.Info);

            if (GUILayout.Button("Собрать продукты из проекта", GUILayout.Height(30f)))
            {
                FoodDatabase db = (FoodDatabase)target;
                Undo.RecordObject(db, "Сбор продуктов");
                db.CollectFromProject();
                EditorUtility.SetDirty(db);
                Debug.Log("[Еда] Список продуктов собран. " +
                          "Теперь жми «Проверить прямо сейчас».");
            }

            if (GUILayout.Button("Проверить прямо сейчас", GUILayout.Height(24f)))
            {
                FoodDatabase db = (FoodDatabase)target;
                db.CollectFromProject();
                FoodTransitionTable t = new FoodTransitionTable();
                t.Build(CollectList(db));
                Debug.Log(t.Validate());
            }
        }

        private static System.Collections.Generic.List<ProductDef> CollectList(FoodDatabase db)
        {
            System.Collections.Generic.List<ProductDef> list =
                new System.Collections.Generic.List<ProductDef>();
            foreach (string guid in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef p = AssetDatabase.LoadAssetAtPath<ProductDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (p != null) list.Add(p);
            }
            return list;
        }
    }
}
#endif
