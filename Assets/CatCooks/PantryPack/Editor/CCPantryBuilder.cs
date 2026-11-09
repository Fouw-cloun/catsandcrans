using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatCooks.Pantry.Editor
{
    public sealed class CCPantryBuilder : EditorWindow
    {
        const string Root = "Assets/CatCooks/PantryPack";
        bool addBoxColliders;
        [Serializable] class ProductMesh { public string name, title; public Piece[] parts; }
        [Serializable] class Piece { public string name; public float[] pivot, vertices, normals, uv; public int[] triangles; }

        [MenuItem("Tools/CatCooks/Pantry/Build product prefabs")]
        static void Open() { GetWindow<CCPantryBuilder>("CatCooks Pantry"); }
        void OnGUI()
        {
            EditorGUILayout.LabelField("28 продуктов · 37 вариантов", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Создаёт меши, материалы и собранные префабы в новой папке Generated. Части остаются отдельными дочерними объектами. Требуется URP.", MessageType.Info);
            addBoxColliders = EditorGUILayout.Toggle("Простые Box Colliders", addBoxColliders);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Собрать модели и префабы"))
                {
                    try { Build(addBoxColliders); }
                    catch (Exception e) { Debug.LogException(e); EditorUtility.DisplayDialog("CatCooks Pantry", e.Message, "OK"); }
                    finally { EditorUtility.ClearProgressBar(); }
                }
            EditorGUILayout.LabelField("Папка результата", Root + "/Generated", EditorStyles.wordWrappedLabel);
        }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        static bool Finite(float x) { return !float.IsNaN(x) && !float.IsInfinity(x); }
        static void Floats(float[] values, string file)
        {
            if (values == null) throw new InvalidDataException("Missing array: " + file);
            foreach (float f in values) if (!Finite(f)) throw new InvalidDataException("Non-finite coordinate: " + file);
        }
        static void Validate(ProductMesh data, string file)
        {
            if (data == null || string.IsNullOrEmpty(data.name) || data.parts == null || data.parts.Length == 0)
                throw new InvalidDataException("Invalid product: " + file);
            foreach (Piece p in data.parts)
            {
                if (p == null || string.IsNullOrEmpty(p.name)) throw new InvalidDataException(file);
                Floats(p.pivot, file); Floats(p.vertices, file); Floats(p.normals, file); Floats(p.uv, file);
                int count = p.vertices.Length / 3;
                if (p.pivot.Length != 3 || count == 0 || p.vertices.Length % 3 != 0 || p.normals.Length != p.vertices.Length || p.uv.Length != count * 2 || p.triangles == null || p.triangles.Length == 0 || p.triangles.Length % 3 != 0)
                    throw new InvalidDataException("Invalid mesh arrays: " + file);
                foreach (int i in p.triangles) if (i < 0 || i >= count) throw new InvalidDataException("Invalid mesh index: " + file);
            }
        }
        static Vector3[] Vectors(float[] values)
        {
            var result = new Vector3[values.Length / 3];
            for (int i = 0; i < result.Length; i++) result[i] = new Vector3(values[3*i], values[3*i+1], values[3*i+2]);
            return result;
        }
        static void Build(bool colliders)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP/Lit не найден. Установи и настрой URP в проекте.");
            string source = Root + "/Products";
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException("Скопируй всю папку Assets/CatCooks/PantryPack из архива.");
            string[] files = Directory.GetFiles(source, "*.food.json", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            if (files.Length == 0) throw new InvalidDataException("Файлы *.food.json не найдены.");
            var data = new List<ProductMesh>();
            foreach (string file in files)
            {
                var item = JsonUtility.FromJson<ProductMesh>(File.ReadAllText(file)); Validate(item, file); data.Add(item);
                if (!File.Exists(Path.Combine(Path.GetDirectoryName(file), "Albedo.png"))) throw new FileNotFoundException("Не найден Albedo.png: " + file);
            }
            Folder(Root + "/Generated");
            string output = AssetDatabase.GenerateUniqueAssetPath(Root + "/Generated/Set_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Folder(output); var materials = new Dictionary<string, Material>();
            try
            {
                for (int a = 0; a < files.Length; a++)
                {
                    EditorUtility.DisplayProgressBar("CatCooks Pantry", data[a].title, (float)a / files.Length);
                    string srcFolder = Path.GetDirectoryName(files[a]).Replace('\\', '/');
                    string key = Path.GetFileName(srcFolder); string target = output + "/" + key;
                    Folder(target); Folder(target + "/Meshes"); Folder(target + "/Prefabs");
                    if (!materials.TryGetValue(key, out Material mat))
                    {
                        string texturePath = srcFolder + "/Albedo.png";
                        AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
                        var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                        if (importer == null) throw new InvalidDataException("Cannot import: " + texturePath);
                        importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true;
                        importer.alphaSource = TextureImporterAlphaSource.None; importer.mipmapEnabled = true;
                        importer.maxTextureSize = 1024; importer.wrapMode = TextureWrapMode.Clamp;
                        importer.filterMode = FilterMode.Bilinear; importer.textureCompression = TextureImporterCompression.Compressed;
                        importer.isReadable = false; importer.SaveAndReimport();
                        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                        if (texture == null) throw new InvalidDataException("Cannot load: " + texturePath);
                        mat = new Material(shader) { name = "CCF_" + key, enableInstancing = true };
                        mat.SetTexture("_BaseMap", texture); mat.SetColor("_BaseColor", Color.white);
                        mat.SetFloat("_Metallic", 0); mat.SetFloat("_Smoothness", .30f);
                        AssetDatabase.CreateAsset(mat, target + "/" + mat.name + ".mat"); materials.Add(key, mat);
                    }
                    var root = new GameObject(data[a].name);
                    try
                    {
                        for (int j = 0; j < data[a].parts.Length; j++)
                        {
                            Piece p = data[a].parts[j]; var mesh = new Mesh { name = p.name };
                            if (p.vertices.Length / 3 > 65535) mesh.indexFormat = IndexFormat.UInt32;
                            mesh.vertices = Vectors(p.vertices); mesh.normals = Vectors(p.normals);
                            var uv = new Vector2[p.uv.Length / 2];
                            for (int k = 0; k < uv.Length; k++) uv[k] = new Vector2(p.uv[k*2], p.uv[k*2+1]);
                            mesh.uv = uv; mesh.triangles = p.triangles; mesh.RecalculateBounds();
                            AssetDatabase.CreateAsset(mesh, target + "/Meshes/" + data[a].name + "_" + j + "_" + p.name + ".asset");
                            var go = new GameObject(p.name); go.transform.SetParent(root.transform, false);
                            go.transform.localPosition = new Vector3(p.pivot[0], p.pivot[1], p.pivot[2]);
                            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                            if (colliders) { var box = go.AddComponent<BoxCollider>(); box.center = mesh.bounds.center; box.size = mesh.bounds.size; }
                        }
                        string prefabPath = target + "/Prefabs/" + data[a].name + ".prefab";
                        if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null) throw new IOException("Cannot save prefab: " + prefabPath);
                    }
                    finally { DestroyImmediate(root); }
                }
                AssetDatabase.SaveAssets(); Selection.activeObject = AssetDatabase.LoadAssetAtPath<DefaultAsset>(output);
                EditorGUIUtility.PingObject(Selection.activeObject);
                Debug.Log("CatCooks Pantry: " + files.Length + " prefabs. " + output);
                EditorUtility.DisplayDialog("Готово", files.Length + " префабов созданы:\n" + output, "OK");
            }
            finally { EditorUtility.ClearProgressBar(); AssetDatabase.SaveAssets(); }
        }
    }
}
