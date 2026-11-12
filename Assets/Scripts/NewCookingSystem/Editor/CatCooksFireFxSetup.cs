#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v14: CatCooks → Огонь и жар:
    ///   1. Включить Opaque Texture (для марева) — во всех URP Asset проекта.
    ///   2. Добавить шейдеры в сборку — Toon Flame, Heat Haze, Stylized Fire, Burn Dissolve в Always Included Shaders.
    ///   3. Горение: добавить на выделенные — FoodBurnable на выделенные продукты (сцена или префабы в Project).
    ///   4. Горение: какао (боб и зёрна) во всём проекте — найти префабы с CocoaPod / FoodRoastable и добавить.
    /// Префабы не создаёт.
    /// </summary>
    public static class CatCooksFireFxSetup
    {
        [MenuItem("CatCooks/Огонь и жар/1. Включить Opaque Texture (для марева)", priority = 85)]
        public static void EnableOpaque()
        {
            var log = new StringBuilder("[Жар] Opaque Texture в URP:\n");
            int total = 0;
            foreach (string g in AssetDatabase.FindAssets("t:RenderPipelineAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                Object a = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (a == null) continue;
                System.Reflection.PropertyInfo pi = a.GetType().GetProperty("supportsCameraOpaqueTexture");
                if (pi == null || !pi.CanWrite) continue;
                total++;
                if ((bool)pi.GetValue(a, null)) { log.AppendLine("  ✔ " + path + " — уже включено"); continue; }
                Undo.RecordObject(a, "Opaque Texture");
                pi.SetValue(a, true, null);
                EditorUtility.SetDirty(a);
                log.AppendLine("  ＋ " + path + " — включено");
            }
            if (total == 0) log.AppendLine("  ✖ URP Asset не найден — включи вручную: URP Asset → Rendering → Opaque Texture");
            AssetDatabase.SaveAssets();
            log.AppendLine("Проверь камеру игрока: Camera → Rendering → Opaque Texture = Use Pipeline Settings (или On).");
            Debug.Log(log.ToString());
        }

        [MenuItem("CatCooks/Огонь и жар/2. Добавить шейдеры в сборку (пламя, марево, горение)", priority = 86)]
        public static void IncludeShaders()
        {
            string[] names = { "CatCooks/Toon Flame", "CatCooks/Heat Haze", "CatCooks/Stylized Fire", "CatCooks/Burn Dissolve", "CatCooks/Powder Heap", "CatCooks/Butter", "CatCooks/Toon Dust", "CatCooks/Milk Puddle", "CatCooks/Sugar", "CatCooks/Toon Food" };
            Object gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            if (gs == null) { Debug.LogWarning("[Жар] Не нашёл ProjectSettings/GraphicsSettings.asset"); return; }
            var so = new SerializedObject(gs);
            SerializedProperty arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null) { Debug.LogWarning("[Жар] В Graphics Settings нет Always Included Shaders"); return; }
            var log = new StringBuilder("[Жар] Шейдеры в сборке:\n");
            foreach (string n in names)
            {
                Shader sh = Shader.Find(n);
                if (sh == null) { log.AppendLine("  ✖ «" + n + "» не найден — файл шейдера на месте?"); continue; }
                bool has = false;
                for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) { has = true; break; }
                if (has) { log.AppendLine("  ✔ «" + n + "» уже есть"); continue; }
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
                log.AppendLine("  ＋ «" + n + "» добавлен");
            }
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }

        [MenuItem("CatCooks/Огонь и жар/3. Горение: добавить на выделенные продукты", priority = 87)]
        public static void AddBurnSelected()
        {
            var log = new StringBuilder("[Горение] FoodBurnable на выделенные:\n");
            if (Selection.gameObjects.Length == 0) { EditorUtility.DisplayDialog("Горение", "Выдели продукты (в сцене или префабы в Project) и нажми ещё раз.", "Ок"); return; }
            foreach (GameObject go in Selection.gameObjects) AddTo(go, log);
            AssetDatabase.SaveAssets();
            log.AppendLine("Сохрани сцену (Ctrl+S). Префабы из Project сохранены сразу.");
            Debug.Log(log.ToString());
        }

        [MenuItem("CatCooks/Огонь и жар/4. Горение: какао (боб и зёрна) во всём проекте", priority = 88)]
        public static void AddBurnCocoa()
        {
            var log = new StringBuilder("[Горение] какао-префабы:\n");
            int n = 0;
            foreach (string g in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                if (go.GetComponent<CocoaPod>() == null && go.GetComponent<FoodRoastable>() == null) continue;
                AddTo(go, log);
                n++;
            }
            if (n == 0) log.AppendLine("  ✖ не нашёл префабов с CocoaPod / FoodRoastable — выдели их вручную и меню 3");
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }

        private static void AddTo(GameObject go, StringBuilder log)
        {
            if (go.GetComponent<FoodPiece>() == null) { log.AppendLine("  ✖ «" + go.name + "»: нет FoodPiece — это не продукт"); return; }
            if (go.GetComponent<FoodBurnable>() != null) { log.AppendLine("  ✔ «" + go.name + "»: уже есть"); return; }
            if (PrefabUtility.IsPartOfPrefabAsset(go))
            {
                string path = AssetDatabase.GetAssetPath(go);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                if (root.GetComponent<FoodBurnable>() == null) root.AddComponent<FoodBurnable>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                log.AppendLine("  ＋ «" + go.name + "» (префаб " + path + ")");
                return;
            }
            Undo.AddComponent<FoodBurnable>(go);
            EditorUtility.SetDirty(go);
            log.AppendLine("  ＋ «" + go.name + "» (в сцене" + (PrefabUtility.IsPartOfPrefabInstance(go) ? " — это экземпляр префаба: Overrides → Apply" : "") + ")");
        }
    }
}
#endif
