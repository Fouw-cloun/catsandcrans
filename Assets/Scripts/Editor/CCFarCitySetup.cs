// Editor-only, opt-in import. No runtime component or automatic scene mutation.
// Put this file in an Editor folder. Intended target: Unity 6 + URP.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class CCFarCitySetup
{
    [Serializable] class MeshRecord { public string name; public float[] bounds_min; public float[] bounds_max; }
    [Serializable] class Manifest { public MeshRecord[] meshes; }

    const string ShaderName = "CatCooks/Environment/Far City Atlas";
    const string MenuRoot = "Tools/CatCooks/Far City/";

    [MenuItem(MenuRoot + "1 - Build Prefabs from Selected Export")]
    static void BuildPrefabs()
    {
        string root = ExportRoot();
        if (root == null) return;
        Shader shader = Shader.Find(ShaderName);
        if (shader == null) { Debug.LogError("Install Unity/Shaders/CCFarCityAtlas.shader first (requires URP)."); return; }
        string atlasPath = root + "/Textures/CCFC_Atlas.png";
        var textureImporter = AssetImporter.GetAtPath(atlasPath) as TextureImporter;
        if (textureImporter == null) { Debug.LogError("Missing atlas: " + atlasPath); return; }
        string[] sources = AssetDatabase.FindAssets("t:Model", new[] { root + "/FBX" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(p).StartsWith("CCFC_", StringComparison.Ordinal))
            .OrderBy(p => p).ToArray();
        if (sources.Length == 0) { Debug.LogError("No CCFC FBX models. Run Generate_Cozy_Far_City.py in Blender first; OBJ examples alone are not enough for this importer."); return; }
        textureImporter.textureType = TextureImporterType.Default;
        textureImporter.sRGBTexture = true;
        textureImporter.alphaSource = TextureImporterAlphaSource.None;
        textureImporter.mipmapEnabled = true;
        textureImporter.wrapMode = TextureWrapMode.Clamp;
        textureImporter.filterMode = FilterMode.Trilinear;
        textureImporter.anisoLevel = 2;
        textureImporter.maxTextureSize = 2048;
        textureImporter.textureCompression = TextureImporterCompression.Compressed;
        textureImporter.isReadable = false;
        textureImporter.SaveAndReimport();
        var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
        EnsureFolder(root + "/Materials"); EnsureFolder(root + "/Prefabs");
        string materialPath = root + "/Materials/CCFC_SharedAtlas.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader); material.SetTexture("_BaseMap", atlas);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        // Existing material is preserved so artist colours survive subsequent imports.
        Manifest manifest = null;
        string manifestPath = root + "/Manifest.json";
        if (File.Exists(manifestPath)) manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
        int built = 0, failed = 0;
        foreach (string source in sources)
        {
            GameObject instance = null;
            var previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(source);
                importer.globalScale = 1;
                importer.useFileScale = true;
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importTangents = ModelImporterTangents.None;
                importer.meshCompression = ModelImporterMeshCompression.Off; // Preserve atlas seams.
                importer.isReadable = false;
                importer.addCollider = false;
                importer.generateSecondaryUV = false;
                importer.SaveAndReimport();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(source);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(model, previewScene);
                // Use a preview scene; the open gameplay scene is not modified.
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = Path.GetFileNameWithoutExtension(source);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                foreach (var old in instance.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(old);
                var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
                var lod0 = renderers.Where(r => InLOD(r.transform, instance.transform, "_LOD0")).Cast<Renderer>().ToArray();
                var lod1 = renderers.Where(r => InLOD(r.transform, instance.transform, "_LOD1")).Cast<Renderer>().ToArray();
                if (lod0.Length != 1 || lod1.Length != 1 || renderers.Length != 2)
                    throw new InvalidOperationException("Expected exactly one mesh per LOD; found " + lod0.Length + "/" + lod1.Length + ". Check FBX hierarchy.");
                var expected = manifest?.meshes?.FirstOrDefault(m => m.name == instance.name + "_LOD0");
                if (expected != null && expected.bounds_min?.Length == 3 && expected.bounds_max?.Length == 3)
                {
                    float height = expected.bounds_max[2] - expected.bounds_min[2];
                    float actual = lod0[0].bounds.size.y;
                    if (height > 0.01f && Mathf.Abs(actual / height - 1f) > 0.03f)
                        throw new InvalidOperationException("FBX scale/axis mismatch: expected height " + height + " m, imported " + actual + " m. Check Blender metric scale 1 and FBX Y-up export.");
                }
                foreach (var r in renderers)
                {
                    r.gameObject.SetActive(true); r.enabled = true;
                    r.sharedMaterial = material;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    r.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
                    // Do not opt into static batching: sector meshes are already combined.
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, 0);
                }
                var group = instance.AddComponent<LODGroup>();
                bool sector = instance.name.StartsWith("CCFC_Sector_", StringComparison.Ordinal);
                group.fadeMode = LODFadeMode.None;
                group.animateCrossFading = false;
                group.SetLODs(new[] { new LOD(sector ? 0.28f : 0.10f, lod0), new LOD(0.003f, lod1) });
                group.RecalculateBounds();
                string prefabPath = AssetDatabase.GenerateUniqueAssetPath(root + "/Prefabs/" + instance.name + ".prefab");
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath); built++;
            }
            catch (Exception ex) { Debug.LogError("CCFC: " + source + "\n" + ex.Message); failed++; }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("CCFC: built " + built + " prefabs; failed " + failed + ". Existing prefabs were preserved. Shared material: " + materialPath);
    }

    static bool InLOD(Transform child, Transform root, string suffix)
    {
        while (child != null && child != root)
        {
            if (child.name.EndsWith(suffix, StringComparison.Ordinal)) return true;
            child = child.parent;
        }
        return false;
    }

    [MenuItem(MenuRoot + "2 - Add Selected Sector Prefabs to Scene")]
    static void PlaceSectors()
    {
        // Explicit selection prevents mixing different exports or duplicate prefab versions.
        var prefabs = Selection.objects.OfType<GameObject>()
            .Where(g => g.name.StartsWith("CCFC_Sector_", StringComparison.Ordinal) && PrefabUtility.IsPartOfPrefabAsset(g))
            .ToArray();
        if (prefabs.Length == 0) { Debug.LogError("Select the 12 CCFC_Sector_* prefab assets in the Project window first."); return; }
        var parent = new GameObject("CCFC_FarCity"); Undo.RegisterCreatedObjectUndo(parent, "Place far city");
        foreach (var prefab in prefabs)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Place far city sector");
            instance.transform.SetParent(parent.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
        }
        Selection.activeGameObject = parent;
        Debug.Log("CCFC: placed " + prefabs.Length + " sectors at their shared origin. Move CCFC_FarCity to align the empty centre with gameplay.");
    }

    static string ExportRoot()
    {
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (string.IsNullOrEmpty(path)) { Debug.LogError("Select an imported CozyFarCity_* export folder in Project."); return null; }
        if (!AssetDatabase.IsValidFolder(path)) path = Path.GetDirectoryName(path).Replace('\\', '/');
        while (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal))
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path + "/Textures/CCFC_Atlas.png") != null && AssetDatabase.IsValidFolder(path + "/FBX")) return path;
            path = Path.GetDirectoryName(path)?.Replace('\\', '/');
        }
        Debug.LogError("Select an export folder containing Textures/CCFC_Atlas.png and FBX/."); return null;
    }
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
