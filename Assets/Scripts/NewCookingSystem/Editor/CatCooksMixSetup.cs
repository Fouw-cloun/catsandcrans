#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// CatCooks → Еда → Создать «Смесь для мороженого».
    /// Создаёт продукт (группа «Жидкое», кремовый цвет, новый Id), пересобирает
    /// список продуктов FoodDatabase в открытой сцене и, если выделен LiquidAnchor
    /// ведра, добавляет туда смесь (роль Жидкость) и мороженое (роль Мороженое).
    /// </summary>
    public static class CatCooksMixSetup
    {
        const string Title = "Смесь для мороженого";

        [MenuItem("CatCooks/Еда/Создать «Смесь для мороженого»", priority = 40)]
        public static void Create()
        {
            var log = new System.Text.StringBuilder();
            ProductDef mix = Find(Title);
            if (mix == null)
            {
                ushort maxId = 0;
                string dir = "Assets/CatCooks/Food/Products";
                foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    ProductDef d = AssetDatabase.LoadAssetAtPath<ProductDef>(p);
                    if (d == null) continue;
                    if (d.Id > maxId) maxId = d.Id;
                    if (d.Title == "Сливки") dir = System.IO.Path.GetDirectoryName(p).Replace('\\', '/');
                }
                mix = ScriptableObject.CreateInstance<ProductDef>();
                mix.Id = (ushort)(maxId + 1);
                mix.Title = Title;
                mix.BaseColor = new Color(0.98f, 0.93f, 0.74f);
                mix.Group = FindGroup("Group_Liquid");
                AssetDatabase.CreateAsset(mix, $"{dir}/Food_Смесь_для_мороженого.asset");
                log.AppendLine($"＋ продукт «{Title}» (Id {mix.Id}) — {dir}");
            }
            else log.AppendLine($"✔ продукт «{Title}» уже есть (Id {mix.Id})");

            FoodDatabase db = Object.FindFirstObjectByType<FoodDatabase>();
            if (db != null)
            {
                db.CollectFromProject();
                EditorUtility.SetDirty(db);
                EditorSceneManager.MarkSceneDirty(db.gameObject.scene);
                log.AppendLine("✔ FoodDatabase в сцене пересобран");
            }
            else log.AppendLine("⚠ в открытой сцене нет FoodDatabase — нажми у него «Collect From Project» сам");

            // Выделен LiquidAnchor — добавить смесь и мороженое.
            CookwareLiquidVisual lv = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInChildren<CookwareLiquidVisual>() : null;
            if (lv != null)
            {
                ProductDef ice = Find("Мороженое");
                var so = new SerializedObject(lv);
                SerializedProperty arr = so.FindProperty("_products");
                AddEntry(arr, mix, (int)CookwareLiquidVisual.Role.Liquid, log);
                if (ice != null) AddEntry(arr, ice, (int)CookwareLiquidVisual.Role.IceCream, log);
                so.ApplyModifiedProperties();
            }
            else log.AppendLine("· выдели LiquidAnchor ведра и нажми ещё раз — добавлю смесь и мороженое в Products");

            AssetDatabase.SaveAssets();
            Debug.Log("[Смесь]\n" + log);
            EditorUtility.DisplayDialog("Смесь для мороженого", log.ToString(), "Ок");
        }

        private static void AddEntry(SerializedProperty arr, ProductDef p, int role, System.Text.StringBuilder log)
        {
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("Product").objectReferenceValue == p)
                { log.AppendLine($"✔ «{p.Title}» уже в Products"); return; }
            arr.arraySize++;
            SerializedProperty e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
            e.FindPropertyRelative("Product").objectReferenceValue = p;
            e.FindPropertyRelative("Role").intValue = role;
            e.FindPropertyRelative("Color").colorValue = new Color(0, 0, 0, 0);
            log.AppendLine($"＋ «{p.Title}» в Products LiquidAnchor");
        }

        private static ProductDef Find(string title)
        {
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef d = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (d != null && d.Title == title) return d;
            }
            return null;
        }

        private static FoodGroupDef FindGroup(string name)
        {
            foreach (string g in AssetDatabase.FindAssets("t:FoodGroupDef " + name))
            {
                FoodGroupDef d = AssetDatabase.LoadAssetAtPath<FoodGroupDef>(AssetDatabase.GUIDToAssetPath(g));
                if (d != null && d.name == name) return d;
            }
            return null;
        }
    }
}
#endif
