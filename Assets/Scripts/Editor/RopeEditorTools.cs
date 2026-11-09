using UnityEditor;
using UnityEngine;
using System.IO;

// ДОЛЖЕН ЛЕЖАТЬ В ПАПКЕ "Editor". Меню Tools → Rope.
public static class RopeEditorTools
{
    // Создаёт слой "Rope" и настраивает Physics-матрицу так, чтобы верёвка не дралась сама с собой.
    [MenuItem("Tools/Rope/Setup 'Rope' Layer")]
    public static void SetupRopeLayer()
    {
        int layer = LayerMaskToIndex("Rope");
        if (layer < 0) layer = CreateLayer("Rope");
        if (layer < 0) { Debug.LogError("Не удалось создать слой Rope (все 32 слота заняты)."); return; }

        // Rope не сталкивается сам с собой
        Physics.IgnoreLayerCollision(layer, layer, true);
        Debug.Log($"Слой 'Rope' готов (index {layer}). Теперь: у VerletRope сними 'Rope' из collisionMask, " +
                  $"а в RopeColliders.colliderLayer поставь 'Rope'.");
    }

    // Генерирует 5 ассетов-пресетов материалов в Assets/RopeMaterials.
    [MenuItem("Tools/Rope/Create Material Presets")]
    public static void CreatePresets()
    {
        string dir = "Assets/RopeMaterials";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "RopeMaterials");

        foreach (VerletRope.RopeType t in System.Enum.GetValues(typeof(VerletRope.RopeType)))
        {
            if (t == VerletRope.RopeType.Custom) continue;
            string path = $"{dir}/Rope_{t}.asset";
            if (File.Exists(path)) continue;
            var mat = ScriptableObject.CreateInstance<RopeMaterial>();
            mat.type = t;
            mat.FillFromType();
            AssetDatabase.CreateAsset(mat, path);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Пресеты созданы в Assets/RopeMaterials (Wire/Hose/SteelCable/Chain/ClimbingRope).");
    }

    // Быстро собирает готовую верёвку на выделенном объекте.
    [MenuItem("Tools/Rope/Add Rope Components To Selection")]
    public static void AddComponents()
    {
        var go = Selection.activeGameObject;
        if (go == null) { Debug.LogWarning("Выдели объект в сцене."); return; }
        if (!go.GetComponent<VerletRope>())       go.AddComponent<VerletRope>();
        if (!go.GetComponent<RopeMeshRenderer>()) go.AddComponent<RopeMeshRenderer>();
        if (!go.GetComponent<RopeColliders>())     go.AddComponent<RopeColliders>();
        if (!go.GetComponent<RopeStyle>())         go.AddComponent<RopeStyle>();
        Debug.Log("Компоненты добавлены. Назначь startPoint/endPoint и RopeMaterial.");
    }

    // ---- helpers для работы со слоями ----
    static int LayerMaskToIndex(string name) => LayerMask.NameToLayer(name);

    static int CreateLayer(string name)
    {
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)   // 0-7 зарезервированы Unity
        {
            var sp = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(sp.stringValue))
            {
                sp.stringValue = name;
                tagManager.ApplyModifiedProperties();
                return i;
            }
        }
        return -1;
    }
}
