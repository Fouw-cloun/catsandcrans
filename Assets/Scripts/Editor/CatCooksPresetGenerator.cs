// ============================================================================
//  CatCooksPresetGenerator.cs
//  Меню: Tools ▸ CatCooks ▸ Создать материалы-пресеты
//  Раскладывает все варианты из CatCooksPresets в готовые .mat ассеты,
//  чтобы можно было просто перетаскивать их на объекты и сравнивать глазами.
//  Файл обязан лежать в папке с именем Editor/.
// ============================================================================
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using CatCooks.EditorTools;

public static class CatCooksPresetGenerator
{
    private const string RootFolder = "Assets/CatCooks/Materials/Presets";

    [MenuItem("Tools/CatCooks/Создать материалы-пресеты", false, 0)]
    public static void GenerateAll()
    {
        int created = Generate(CatCooksPresets.FoodShader, CatCooksPresets.Food, "Food", "CC_Food");
        created += Generate(CatCooksPresets.PropsShader, CatCooksPresets.Props, "Props", "CC_Props");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (created > 0)
        {
            var folder = AssetDatabase.LoadAssetAtPath<Object>(RootFolder);
            if (folder != null)
            {
                Selection.activeObject = folder;
                EditorGUIUtility.PingObject(folder);
            }
        }

        Debug.Log("[CatCooks] Материалов создано/обновлено: " + created + "  →  " + RootFolder);
    }

    [MenuItem("Tools/CatCooks/Синхронизировать keyword-ы у выделенных материалов", false, 20)]
    public static void SyncSelected()
    {
        int n = 0;
        foreach (var obj in Selection.objects)
        {
            var mat = obj as Material;
            if (mat == null) continue;
            CatCooksShaderGUI.SyncKeywords(mat);
            EditorUtility.SetDirty(mat);
            n++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[CatCooks] Обновлено материалов: " + n);
    }

    // ------------------------------------------------------------------
    private static int Generate(string shaderName, List<ShaderPreset> presets,
                                string subFolder, string prefix)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError("[CatCooks] Шейдер не найден: " + shaderName +
                           ". Проверь, что .shader файл лежит в проекте и компилируется без ошибок.");
            return 0;
        }

        string path = EnsureFolder(RootFolder + "/" + subFolder);
        int count = 0;

        for (int i = 0; i < presets.Count; i++)
        {
            var preset = presets[i];
            string fileName = string.Format("{0}_{1:00}_{2}.mat", prefix, i + 1, Slug(preset.Name));
            string assetPath = path + "/" + fileName;

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            bool isNew = mat == null;

            if (isNew)
            {
                mat = new Material(shader);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            mat.enableInstancing = true;
            CatCooksShaderGUI.ApplyPreset(mat, preset);

            if (isNew)
                AssetDatabase.CreateAsset(mat, assetPath);
            else
                EditorUtility.SetDirty(mat);

            count++;
        }

        return count;
    }

    private static string EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return path;

        string[] parts = path.Split('/');
        string current = parts[0]; // "Assets"

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }

        return current;
    }

    // Кириллица в латиницу — чтобы имена файлов были безопасными.
    private static readonly Dictionary<char, string> Translit = new Dictionary<char, string>
    {
        {'а',"a"},{'б',"b"},{'в',"v"},{'г',"g"},{'д',"d"},{'е',"e"},{'ё',"e"},
        {'ж',"zh"},{'з',"z"},{'и',"i"},{'й',"y"},{'к',"k"},{'л',"l"},{'м',"m"},
        {'н',"n"},{'о',"o"},{'п',"p"},{'р',"r"},{'с',"s"},{'т',"t"},{'у',"u"},
        {'ф',"f"},{'х',"h"},{'ц',"ts"},{'ч',"ch"},{'ш',"sh"},{'щ',"sch"},
        {'ъ',""},{'ы',"y"},{'ь',""},{'э',"e"},{'ю',"yu"},{'я',"ya"},
    };

    private static string Slug(string source)
    {
        var sb = new StringBuilder();
        bool capitalizeNext = true;

        foreach (char raw in source)
        {
            char c = char.ToLowerInvariant(raw);

            string piece = null;
            if (Translit.ContainsKey(c))
                piece = Translit[c];
            else if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                piece = c.ToString();

            if (piece == null)
            {
                capitalizeNext = true;   // пробелы, скобки и прочее — просто разделители
                continue;
            }

            if (piece.Length == 0)
                continue;

            if (capitalizeNext)
            {
                sb.Append(char.ToUpperInvariant(piece[0]));
                if (piece.Length > 1) sb.Append(piece.Substring(1));
                capitalizeNext = false;
            }
            else
            {
                sb.Append(piece);
            }
        }

        string result = sb.ToString();
        return string.IsNullOrEmpty(result) ? "Preset" : result;
    }
}
