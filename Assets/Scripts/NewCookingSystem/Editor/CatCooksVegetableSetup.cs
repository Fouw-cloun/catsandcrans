#if UNITY_EDITOR
using System.Text;
using FishNet.Component.Transforming;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// CatCooks → Овощи → Настроить выделенные.
    /// Только вешает компоненты на выделенные в сцене морковки и луковицы.
    /// Ничего не создаёт (ни префабов, ни продуктов, ни папок) и ничего не переименовывает.
    /// Продукт ищется среди существующих по названию; нет — поле «Product» оставляется пустым.
    /// </summary>
    public static class CatCooksVegetableSetup
    {
        [MenuItem("CatCooks/Овощи/Настроить выделенные")]
        public static void SetupSelected()
        {
            if (Selection.gameObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("Овощи", "Выдели в сцене морковки и луковицы.", "Ок");
                return;
            }
            var log = new StringBuilder();
            foreach (GameObject go in Selection.gameObjects)
            {
                Undo.RegisterFullObjectHierarchyUndo(go, "Овощи");
                string n = go.name.ToLowerInvariant();
                MeshFilter anyMesh = go.GetComponentInChildren<MeshFilter>(true);
                if (anyMesh != null && anyMesh.sharedMesh != null)
                    n += " " + AssetDatabase.GetAssetPath(anyMesh.sharedMesh).ToLowerInvariant();

                if (n.Contains("морков") || n.Contains("carrot")) Carrot(go, log);
                else if (n.Contains("лук") || n.Contains("onion")) Onion(go, log);
                else log.AppendLine($"· «{go.name}»: не понял, морковь или лук — пропущен");
                EditorUtility.SetDirty(go);
            }
            Debug.Log("[Овощи]\n" + log);
            EditorUtility.DisplayDialog("Овощи", log.ToString(), "Ок");
        }

        private static void Common(GameObject go, ProductDef product, float mass)
        {
            int food = LayerMask.NameToLayer("Food");
            if (food >= 0)
            {
                go.layer = food;
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = food;
            }
            Get<NetworkObject>(go);
            Get<NetworkTransform>(go);
            Rigidbody rb = Get<Rigidbody>(go);
            if (rb.mass < 0.01f || rb.mass > 20f) rb.mass = mass;
            FoodPiece fp = Get<FoodPiece>(go);
            if (product != null)
            {
                var so = new SerializedObject(fp);
                SerializedProperty p = so.FindProperty("_product");
                if (p != null) p.objectReferenceValue = product;
                SerializedProperty st = so.FindProperty("_startState");
                if (st != null) st.intValue = (int)FoodState.Dirty;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            FoodStateVisual vis = Get<FoodStateVisual>(go);
            if (vis.EditorMeshTarget == null) vis.EditorMeshTarget = go.GetComponentInChildren<MeshFilter>(true);
            Get<Suckable>(go);
            if (go.GetComponentInChildren<Collider>() == null) go.AddComponent<CapsuleCollider>();
        }

        private static void Carrot(GameObject go, StringBuilder log)
        {
            ProductDef p = Product("Морковь");
            Common(go, p, 0.15f);
            int parts = 0;
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
                if (mf.transform != go.transform) parts++;
            if (parts >= 2) Get<FoodSliceable>(go);
            log.AppendLine($"✔ «{go.name}»: морковь" + (p == null ? " (продукт «Морковь» не найден — укажи руками)" : "")
                           + (parts >= 2 ? ", FoodSliceable добавлен (префаб куска укажи руками)" : ", цельная — без FoodSliceable"));
        }

        private static void Onion(GameObject go, StringBuilder log)
        {
            ProductDef p = Product("Лук репчатый");
            Common(go, p, 0.12f);
            FoodWholeSlicer ws = Get<FoodWholeSlicer>(go);
            MeshFilter mf = go.GetComponentInChildren<MeshFilter>(true);
            if (mf != null) ws.EditorSetup(null, mf.transform, 0);
            log.AppendLine($"✔ «{go.name}»: лук, FoodWholeSlicer добавлен (Slice Prefabs укажи руками)"
                           + (p == null ? "; продукт не найден — укажи руками" : ""));
        }

        private static ProductDef Product(string title)
        {
            foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
            {
                ProductDef p = AssetDatabase.LoadAssetAtPath<ProductDef>(AssetDatabase.GUIDToAssetPath(g));
                if (p != null && p.Title == title) return p;
            }
            return null;
        }

        private static T Get<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}
#endif
