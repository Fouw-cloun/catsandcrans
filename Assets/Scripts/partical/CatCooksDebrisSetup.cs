#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Создание профилей ошмётков одной кнопкой.
///
/// ЗАЧЕМ КНОПКА, А НЕ РУЧНОЕ ЗАПОЛНЕНИЕ: в профиле картошки девять цветов,
/// снятых пипеткой с её же текстур. Занести их руками — двадцать минут и три
/// опечатки, а проверить опечатку нечем: неверный цвет выглядит как «просто
/// такой оттенок». Цифры лежат в FoodDebrisProfile.PotatoStates(), и кнопка
/// просто переносит их в ассет.
///
/// ПЕРЕСОЗДАНИЕ БЕЗОПАСНО: если ассет уже есть, кнопка спросит и перезапишет
/// только палитры, не трогая размеры и густоту, которые ты мог настроить.
/// </summary>
public static class CatCooksDebrisSetup
{
    private const string Dir = "Assets/CatCooks/Food/Debris";

    [MenuItem("CatCooks/Ошмётки/Создать профиль: картошка", priority = 200)]
    public static void CreatePotato()
    {
        FoodDebrisProfile p = LoadOrCreate("Debris_Potato");

        bool hadStates = p.States != null && p.States.Length > 0;
        if (hadStates &&
            !EditorUtility.DisplayDialog(
                "Профиль картошки уже есть",
                "Перезаписать палитры цветами из текстур?\n\n" +
                "Размеры, густота и липкость останутся как настроены.",
                "Перезаписать", "Отмена"))
            return;

        p.States = FoodDebrisProfile.PotatoStates();

        // Размеры трогаем только у нового ассета — иначе затёрли бы настройку.
        if (!hadStates)
        {
            p.ChunkSize = 0.016f;
            p.ShredSize = 0.030f;
            p.DropletSize = 0.009f;
            p.DustSize = 0.020f;
            p.Density = 1f;
            p.Stickiness = 0.65f;
        }

        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
        Selection.activeObject = p;
        EditorGUIUtility.PingObject(p);

        Debug.Log("[Ошмётки] Профиль картошки готов: 3 состояния " +
                  "(грязная, мытая, чищеная). Повесь FoodDebrisProfileBinder на " +
                  "префаб картошки и укажи этот ассет.", p);
    }

    /// <summary>
    /// Проверка развёртки: показывает, что реально увидит игрок, до того как
    /// заводить инструмент. Без неё настройка палитры — это «поправил цифру,
    /// запустил игру, взял болгарку, подошёл к картошке, посмотрел».
    /// </summary>
    [MenuItem("CatCooks/Ошмётки/Где что настраивается", priority = 201)]
    public static void Explain()
    {
        EditorUtility.DisplayDialog(
            "Ошмётки — что где лежит",
            "ПАЛИТРА (цвет земли, кожуры, мякоти, сока)\n" +
            "   ассет профиля, поле States, по одной палитре на состояние формы\n\n" +
            "ГУСТОТА И СКОРОСТЬ РАЗЛЁТА\n" +
            "   объект «CatCooks Food Debris (auto)» в сцене во время игры\n\n" +
            "КЛЯКСЫ НА ЭКРАНЕ\n" +
            "   объект «CatCooks Lens Splatter (auto)», клавиша V протирает\n\n" +
            "ЧЕЙ ПРОФИЛЬ БРАТЬ\n" +
            "   компонент FoodDebrisProfileBinder на префабе продукта.\n" +
            "   Не повесил — работает старое поведение по DebrisColor.",
            "Понятно");
    }

    private static FoodDebrisProfile LoadOrCreate(string name)
    {
        string path = $"{Dir}/{name}.asset";
        FoodDebrisProfile p = AssetDatabase.LoadAssetAtPath<FoodDebrisProfile>(path);
        if (p != null) return p;

        EnsureDir(Dir);
        p = ScriptableObject.CreateInstance<FoodDebrisProfile>();
        AssetDatabase.CreateAsset(p, path);
        return p;
    }

    /// <summary>Создаёт всю цепочку папок. CreateFolder умеет только по одной
    /// за раз и молча возвращает пустую строку, если родителя нет.</summary>
    private static void EnsureDir(string dir)
    {
        if (AssetDatabase.IsValidFolder(dir)) return;

        string[] parts = dir.Split('/');
        string cur = parts[0];                     // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}
#endif
