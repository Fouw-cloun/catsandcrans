using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatCooks.Floors.Editor
{
    public static class CCFloorSetup
    {
        public static readonly string[] Names = { "01_OpenFrame", "02_L_Mezzanine", "03_WingsAndBridge", "04_Terraces", "05_CollapsedBay" };
        public static readonly string[] Titles = { "Открытый каркас", "Г-образный настил", "Крылья и мост", "Террасы", "Обвалившийся участок" };
        public static string Presets => CCFloorBuilder.Root + "/Presets";

        public static void EnsureAssets()
        {
            CCFloorBuilder.EnsureFolder(Presets);
            CCFloorBuilder.EnsureFolder(CCFloorBuilder.Root + "/Materials");
            var palette = AssetDatabase.LoadAssetAtPath<CCFloorPalette>(Presets + "/DefaultPalette.asset");
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance<CCFloorPalette>();
                palette.Concrete = Material("Concrete", new Color(.65f, .59f, .43f));
                palette.Steel = Material("Steel", new Color(.19f, .29f, .18f));
                palette.Safety = Material("Safety", new Color(.98f, .63f, .08f));
                palette.Rubble = Material("Rubble", new Color(.39f, .37f, .32f));
                AssetDatabase.CreateAsset(palette, Presets + "/DefaultPalette.asset");
            }
            for (int i = 0; i < Names.Length; i++)
                if (AssetDatabase.LoadAssetAtPath<CCFloorLayout>(Presets + "/" + Names[i] + ".asset") == null)
                {
                    var a = ScriptableObject.CreateInstance<CCFloorLayout>(); Fill(a, i);
                    AssetDatabase.CreateAsset(a, Presets + "/" + Names[i] + ".asset");
                }
            AssetDatabase.SaveAssets();
        }
        public static CCFloorLayout Layout(int i) => AssetDatabase.LoadAssetAtPath<CCFloorLayout>(Presets + "/" + Names[i] + ".asset");
        public static void Fill(CCFloorLayout a, int variant)
        {
            a.Title = Titles[variant]; a.Width = a.Depth = 8; a.DeckLevels = 3;
            a.Cells.Clear(); a.Stairs.Clear();
            for (int l = 0; l < 3; l++)
                for (int z = 0; z < 8; z++)
                    for (int x = 0; x < 8; x++)
                    {
                        bool built = l == 0;
                        if (l == 1)
                        {
                            if (variant == 1) built = x < 2 || z >= 6;
                            if (variant == 2) built = x < 2 || x >= 6 || z == 6;
                            if (variant == 3) built = z >= 4;
                            if (variant == 4) built = z >= 6;
                        }
                        if (l == 2 && variant == 3) built = z >= 6;
                        bool missing = variant == 4 && l == 0 && x >= 3 && x <= 4 && z >= 3 && z <= 4;
                        a.Set(x, z, l, missing ? CCFloorState.Missing : built ? CCFloorState.Built : CCFloorState.Planned);
                    }
            if (variant == 1 || variant == 2 || variant == 4)
                a.Stairs.Add(new CCFloorStair { X = 2, Z = 4, FromLevel = 0, Length = 2 });
            if (variant == 3)
            {
                a.Stairs.Add(new CCFloorStair { X = 2, Z = 2, FromLevel = 0, Length = 2 });
                a.Stairs.Add(new CCFloorStair { X = 5, Z = 5, FromLevel = 1, Length = 1 });
            }
            foreach (var stair in a.Stairs)
                for (int z = stair.Z; z < stair.Z + stair.Length; z++)
                    a.Set(stair.X, z, stair.FromLevel + 1, CCFloorState.Reserved);
            a.PlayerCell = new Vector2Int(6, 0); a.LiftCell = new Vector2Int(4, 7);
            string[] descriptions = {
                "Сплошной нижний этаж. Внешний каркас готов; внутренние колонны, балки и верхние плиты запланированы.",
                "Готовые верхние плиты вдоль двух сторон. Открытый центр и лестница на Г-образную площадку.",
                "Два верхних крыла соединены мостом. Под мостом остаётся проход нижнего уровня.",
                "Три высоты настила 0 / H / 2H. Две лестницы соединяют ступенчатые площадки.",
                "В нижнем настиле отсутствуют четыре центральные плиты. Есть обход, ограждение и верхняя площадка." };
            a.Description = descriptions[variant];
        }
        static Material Material(string name, Color color)
        {
            string path = CCFloorBuilder.Root + "/Materials/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path); if (existing) return existing;
            var pipeline = GraphicsSettings.currentRenderPipeline;
            string shaderName = pipeline == null ? "Standard" : pipeline.GetType().Name.Contains("HDRender") ? "HDRP/Lit" : "Universal Render Pipeline/Lit";
            Shader shader = Shader.Find(shaderName);
            if (!shader) throw new System.InvalidOperationException("Не найден шейдер " + shaderName + ". Создай Palette со своими материалами.");
            var mat = new Material(shader) { name = name, enableInstancing = true };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", .2f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", .2f);
            AssetDatabase.CreateAsset(mat, path); return mat;
        }
        public static CCModularFloor CreateFloor(int variant)
        {
            if (Application.isPlaying || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new System.InvalidOperationException("Выйди из Play / Prefab Mode.");
            EnsureAssets();
            Vector3 origin = Selection.activeTransform ? Selection.activeTransform.position : Vector3.zero;
            var root = new GameObject("CC_ModularFloor"); root.transform.position = origin;
            var floor = root.AddComponent<CCModularFloor>(); floor.Layout = Layout(variant);
            floor.Palette = AssetDatabase.LoadAssetAtPath<CCFloorPalette>(Presets + "/DefaultPalette.asset");
            new GameObject("UserContent").transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(root, "Create CatCooks floor root");
            CCFloorBuilder.Rebuild(floor);
            Selection.activeGameObject = root;
            return floor;
        }
    }
}
