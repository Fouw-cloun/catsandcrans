using UnityEditor;
using UnityEngine;

namespace CatCooks.Food.EditorTools
{
    /// <summary>
    /// v19.3: слой «HeldOverlay» для предмета в руке (рисуется поверх мира, как оружие в шутере).
    /// Добавляется сам при загрузке редактора в первый свободный пользовательский слой (8–31).
    /// Меню: CatCooks → Рука → Слой HeldOverlay.
    /// </summary>
    [InitializeOnLoad]
    public static class CatCooksHeldLayerSetup
    {
        static CatCooksHeldLayerSetup()
        {
            EditorApplication.delayCall += () => Ensure(false);
        }

        [MenuItem("CatCooks/Рука/Слой HeldOverlay (предмет поверх мира)")]
        public static void Menu() { Ensure(true); }

        private static void Ensure(bool verbose)
        {
            if (LayerMask.NameToLayer(HeldViewModel.LayerName) >= 0)
            {
                if (verbose) Debug.Log($"[В руке] слой «{HeldViewModel.LayerName}» уже есть: №{LayerMask.NameToLayer(HeldViewModel.LayerName)}");
                return;
            }
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            SerializedProperty layers = so.FindProperty("layers");
            if (layers == null) return;
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty p = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(p.stringValue)) continue;
                p.stringValue = HeldViewModel.LayerName;
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                Debug.Log($"[В руке] добавлен слой «{HeldViewModel.LayerName}» (№{i}) — предмет в руке рисуется поверх мира, когда рядом нет тары. " +
                          "Столкновения у слоя не важны: коллайдеры предмета в руке выключены.");
                return;
            }
            Debug.LogWarning($"[В руке] нет свободного слоя для «{HeldViewModel.LayerName}» (8–31 заняты) — освободи один в Tags and Layers");
        }
    }
}
