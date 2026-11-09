using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatCooks.Floors.Editor
{
    public static class CCSiteAtlas
    {
        public static Rect Tile(int index)
        {
            const float pad = .007f;
            return new Rect((index % 4) * .25f + pad, (index / 4) * .25f + pad, .25f - 2 * pad, .25f - 2 * pad);
        }
        public static Rect Facade(bool side) => new Rect(side ? .503f : .003f, .503f, .494f, .494f);
        public static Rect ForKind(CCFloorKind kind)
        {
            switch (kind)
            {
                case CCFloorKind.Slab: case CCFloorKind.Stair: return Tile(0);
                case CCFloorKind.Rail: return Tile(2);
                case CCFloorKind.Debris: return Tile(3);
                default: return Tile(1);
            }
        }
        public static Material Create(CCConstructionSite site, string folder, CCFloorBake bake)
        {
            int n = site.AtlasResolution;
            var tex = new Texture2D(n, n, TextureFormat.RGB24, true, false);
            try
            {
                var pixels = new Color32[n * n];
                var colors = new[] { new Color32(171,160,128,255), new Color32(65,83,61,255), new Color32(233,171,45,255),
                    new Color32(108,99,82,255), new Color32(79,88,91,255), new Color32(156,103,76,255),
                    new Color32(213,209,185,255), new Color32(46,66,72,255) };
                for (int y = 0; y < n / 2; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int tile = (y / (n / 4)) * 4 + x / (n / 4);
                        var color = colors[tile];
                        int noise = ((x * 19 + y * 43 + (x ^ y) * 7) % 7) - 3;
                        color.r = (byte)Mathf.Clamp(color.r + noise, 0, 255); color.g = (byte)Mathf.Clamp(color.g + noise, 0, 255);
                        color.b = (byte)Mathf.Clamp(color.b + noise, 0, 255); pixels[y * n + x] = color;
                    }
                PaintFacade(pixels, n, 0, Mathf.Clamp(Mathf.RoundToInt(site.Width / 4), 8, 48), Mathf.Max(1, site.EmptyFloors));
                PaintFacade(pixels, n, n / 2, Mathf.Clamp(Mathf.RoundToInt(site.Depth / 4), 8, 48), Mathf.Max(1, site.EmptyFloors));
                tex.SetPixels32(pixels); tex.Apply(false, false);
                string path = folder + "/SiteAtlas.png";
                File.WriteAllBytes(path, tex.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true; importer.alphaSource = TextureImporterAlphaSource.None;
                importer.mipmapEnabled = true; importer.isReadable = false; importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 2; importer.maxTextureSize = n;
                importer.textureCompression = TextureImporterCompression.Compressed; importer.SaveAndReimport();
                var pipeline = GraphicsSettings.currentRenderPipeline;
                string shaderName = pipeline == null ? "Standard" : pipeline.GetType().Name.Contains("HDRender") ? "HDRP/Lit" : "Universal Render Pipeline/Lit";
                var shader = Shader.Find(shaderName);
                if (!shader) throw new System.InvalidOperationException("Не найден " + shaderName);
                var mat = new Material(shader) { name = "SiteAtlas_Opaque", enableInstancing = true };
                var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", imported);
                if (mat.HasProperty("_BaseColorMap")) mat.SetTexture("_BaseColorMap", imported);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", imported);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", .12f);
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", .12f);
                AssetDatabase.AddObjectToAsset(mat, bake); return mat;
            }
            finally { Object.DestroyImmediate(tex); }
        }
        static void PaintFacade(Color32[] p, int n, int offsetX, int columns, int rows)
        {
            int size = n / 2, y0 = n / 2;
            Fill(p, n, offsetX, y0, size, size, new Color32(160,151,131,255));
            float cw = (size - 8f) / columns, ch = (size - 8f) / rows;
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < columns; col++)
                {
                    int x = offsetX + 4 + Mathf.RoundToInt(col * cw), y = y0 + 4 + Mathf.RoundToInt(row * ch);
                    int ww = Mathf.Max(3, Mathf.RoundToInt(cw * .62f)), hh = Mathf.Max(5, Mathf.RoundToInt(ch * .62f));
                    int wx = x + Mathf.RoundToInt(cw * .19f), wy = y + Mathf.RoundToInt(ch * .22f);
                    Fill(p, n, x, y, Mathf.CeilToInt(cw), Mathf.Max(1, Mathf.RoundToInt(ch * .055f)), new Color32(116,113,101,255));
                    Fill(p, n, wx - 1, wy - 1, ww + 2, hh + 2, new Color32(220,209,177,255));
                    bool warm = (row * 7 + col * 11) % 13 == 0;
                    var glass = warm ? new Color32(179,163,109,255) : new Color32(72,108,117,255);
                    Fill(p, n, wx, wy, ww, hh, glass);
                    Fill(p, n, wx, wy, ww, Mathf.Max(1, hh / 3), new Color32((byte)(glass.r * .68f),(byte)(glass.g * .68f),(byte)(glass.b * .68f),255));
                    Fill(p, n, wx + ww / 2, wy, 1, hh, new Color32(191,190,163,255));
                    Fill(p, n, wx, wy + hh - 2, ww, 1, new Color32(139,168,167,255));
                }
        }
        static void Fill(Color32[] p, int n, int x, int y, int w, int h, Color32 color)
        {
            for (int yy = Mathf.Max(y, 0); yy < Mathf.Min(n, y + h); yy++)
                for (int xx = Mathf.Max(x, 0); xx < Mathf.Min(n, x + w); xx++) p[yy * n + xx] = color;
        }
    }
}
