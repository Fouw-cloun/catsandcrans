using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>Материалы и текстуры для входа в метро и неба: Assets/Generated/City/Air/.</summary>
    public static class CityAirAssets
    {
        public const string Folder = "Assets/Generated/City/Air";

        public static void MakeFolder()
        {
            string[] parts = Folder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        public static Material Mat(string name, string shader)
        {
            MakeFolder();
            string path = Folder + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            Shader sh = Shader.Find(shader);
            if (sh == null) { Debug.LogWarning("[Город/небо] нет шейдера " + shader); return null; }
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        public static void Metro(CityMetroEntrance e)
        {
            float m = e.Meter();
            e.Toon = Mat("CC_Metro_Toon", "CatCooks/City/Toon");
            e.Glow = Mat("CC_Metro_Glow", "CatCooks/City/Toon");
            e.Pit = Mat("CC_Metro_Pit", "CatCooks/City/Toon");
            e.HoleMark = Mat("CC_Metro_HoleMark", "CatCooks/City/Metro Hole");
            e.HoleClear = Mat("CC_Metro_HoleClear", "CatCooks/City/Metro Hole");
            if (e.Glow != null)
            {
                e.Glow.SetFloat("_Unlit", 1f);
                e.Glow.SetColor("_BaseColor", new Color(0.75f, 1f, 0.8f));
                e.Glow.SetColor("_EmissionColor", new Color(0.5f, 1.5f, 0.65f));
                EditorUtility.SetDirty(e.Glow);
            }
            if (e.Pit != null)
            {
                e.Pit.SetFloat("_PitDepth", e.Depth * m * 1.05f);
                e.Pit.SetFloat("_StencilRef", 64f);
                e.Pit.SetFloat("_StencilComp", 3f);                // Equal — только внутри проёма
                e.Pit.renderQueue = 2503;
                EditorUtility.SetDirty(e.Pit);
            }
            if (e.HoleMark != null)
            {
                e.HoleMark.SetFloat("_WriteFar", 0f); e.HoleMark.SetFloat("_ZTest", 4f); e.HoleMark.SetFloat("_ZWrite", 0f);
                e.HoleMark.SetFloat("_StencilComp", 8f); e.HoleMark.SetFloat("_StencilPass", 2f);
                e.HoleMark.renderQueue = 2501;
                EditorUtility.SetDirty(e.HoleMark);
            }
            if (e.HoleClear != null)
            {
                e.HoleClear.SetFloat("_WriteFar", 1f); e.HoleClear.SetFloat("_ZTest", 8f); e.HoleClear.SetFloat("_ZWrite", 1f);
                e.HoleClear.SetFloat("_StencilComp", 3f); e.HoleClear.SetFloat("_StencilPass", 0f);
                e.HoleClear.renderQueue = 2502;
                EditorUtility.SetDirty(e.HoleClear);
            }
            AssetDatabase.SaveAssets();
        }

        public static void Sky(CitySky s)
        {
            s.Toon = Mat("CC_Sky_Toon", "CatCooks/City/Toon");
            s.Banner = Mat("CC_Sky_Banner", "CatCooks/City/Toon");
            if (s.Banner != null)
            {
                Texture2D tex = BannerTexture(s.BannerText);
                s.Banner.SetTexture("_BaseMap", tex);
                s.Banner.SetFloat("_Unlit", 0.35f);
                EditorUtility.SetDirty(s.Banner);
            }
            s.Trail = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/CC_Sky_Trail.mat");
            if (s.Trail == null)
            {
                Shader sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (sh != null) { MakeFolder(); s.Trail = new Material(sh); AssetDatabase.CreateAsset(s.Trail, Folder + "/CC_Sky_Trail.mat"); }
            }
            AssetDatabase.SaveAssets();
        }

        // ───────────── баннер: надпись пиксельным шрифтом 5×7 ─────────────

        private static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
        {
            { 'A', new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" } },
            { 'B', new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." } },
            { 'C', new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." } },
            { 'D', new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." } },
            { 'E', new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" } },
            { 'F', new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." } },
            { 'G', new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####" } },
            { 'H', new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" } },
            { 'I', new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####" } },
            { 'J', new[] { "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.." } },
            { 'K', new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" } },
            { 'L', new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" } },
            { 'M', new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" } },
            { 'N', new[] { "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#" } },
            { 'O', new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." } },
            { 'P', new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." } },
            { 'Q', new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" } },
            { 'R', new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" } },
            { 'S', new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." } },
            { 'T', new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." } },
            { 'U', new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." } },
            { 'V', new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." } },
            { 'W', new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#" } },
            { 'X', new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" } },
            { 'Y', new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." } },
            { 'Z', new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" } },
            { '0', new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." } },
            { '1', new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." } },
            { '2', new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" } },
            { '3', new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." } },
            { '4', new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." } },
            { '5', new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." } },
            { '6', new[] { ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###." } },
            { '7', new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." } },
            { '8', new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." } },
            { '9', new[] { ".###.", "#...#", "#...#", ".####", "....#", "....#", ".###." } },
            { '!', new[] { "..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#.." } },
            { '?', new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.." } },
            { '.', new[] { ".....", ".....", ".....", ".....", ".....", ".....", "..#.." } },
            { ',', new[] { ".....", ".....", ".....", ".....", ".....", "..#..", ".#..." } },
            { '-', new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." } },
            { '\'', new[] { "..#..", "..#..", ".#...", ".....", ".....", ".....", "....." } },
            { '&', new[] { ".##..", "#..#.", "#.#..", ".#...", "#.#.#", "#..#.", ".##.#" } },
            { ' ', new[] { ".....", ".....", ".....", ".....", ".....", ".....", "....." } }
        };

        /// <summary>Баннер 1024×256: кремовый фон, красная рамка, надпись тёмно-красным пиксельным шрифтом по центру.</summary>
        public static Texture2D BannerTexture(string text)
        {
            MakeFolder();
            if (string.IsNullOrEmpty(text)) text = "CAT COOKS";
            text = text.ToUpperInvariant();
            const int W = 1024, H = 256;
            Color bg = new Color(1f, 0.93f, 0.72f), border = new Color(0.85f, 0.18f, 0.15f), ink = new Color(0.55f, 0.08f, 0.10f);
            Color[] px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    bool edge = x < 14 || x >= W - 14 || y < 14 || y >= H - 14;
                    px[y * W + x] = edge ? border : bg;
                }
            int n = text.Length;
            int cell = Mathf.Max(2, Mathf.Min((W - 80) / Mathf.Max(1, n * 6), (H - 70) / 7));
            int tw = n * 6 * cell - cell, x0 = (W - tw) / 2, y0 = (H - 7 * cell) / 2;
            for (int i = 0; i < n; i++)
            {
                string[] g;
                if (!Font.TryGetValue(text[i], out g)) g = Font[' '];
                for (int row = 0; row < 7; row++)
                    for (int col = 0; col < 5; col++)
                    {
                        if (g[row][col] != '#') continue;
                        int gx = x0 + (i * 6 + col) * cell, gy = y0 + (6 - row) * cell;
                        for (int yy = 0; yy < cell; yy++)
                            for (int xx = 0; xx < cell; xx++)
                            {
                                int px0 = gx + xx, py0 = gy + yy;
                                if (px0 >= 0 && px0 < W && py0 >= 0 && py0 < H) px[py0 * W + px0] = ink;
                            }
                    }
            }
            string path = Folder + "/CC_Sky_BannerText.asset";
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null || tex.width != W || tex.height != H)
            {
                if (tex != null) AssetDatabase.DeleteAsset(path);
                tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
                tex.SetPixels(px);
                tex.Apply(true);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                AssetDatabase.CreateAsset(tex, path);
            }
            else
            {
                tex.SetPixels(px);
                tex.Apply(true);
                EditorUtility.SetDirty(tex);
            }
            return tex;
        }
    }

    [CustomEditor(typeof(CityMetroEntrance))]
    public sealed class CityMetroEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            CityMetroEntrance e = (CityMetroEntrance)target;
            EditorGUILayout.HelpBox("Вход в метро: спуск по +Z объекта (синяя стрелка). Поставь на тротуар, поверни и «Собрать». " +
                                    "Яма видна только внутри проёма (трафарет) и уходит в темноту.", MessageType.None);
            GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
            if (GUILayout.Button("Собрать", GUILayout.Height(28))) { Undo.RegisterFullObjectHierarchyUndo(e.gameObject, "Метро"); CityAirAssets.Metro(e); e.Build(); EditorUtility.SetDirty(e); }
            GUI.backgroundColor = Color.white;
            DrawDefaultInspector();
        }

        [MenuItem("CatCooks/Застройка/Вход в метро")]
        private static void Menu()
        {
            Vector3 p = Vector3.zero;
            SceneView sv = SceneView.lastActiveSceneView;
            if (sv != null) p = sv.pivot;
            CityBlockGenerator g = Object.FindFirstObjectByType<CityBlockGenerator>();
            if (g != null) p.y = g.transform.position.y;
            GameObject go = new GameObject("Metro Entrance");
            Undo.RegisterCreatedObjectUndo(go, "Метро");
            go.transform.position = p;
            if (g != null) { go.transform.SetParent(g.transform, true); go.transform.rotation = g.transform.rotation; }
            CityMetroEntrance e = go.AddComponent<CityMetroEntrance>();
            CityAirAssets.Metro(e);
            e.Build();
            Selection.activeGameObject = go;
            Debug.Log("[Город/метро] вход поставлен в центр вида сцены — подвинь на тротуар (спуск по синей стрелке +Z), при смене размеров «Собрать»", go);
        }
    }

    [CustomEditor(typeof(CitySky))]
    public sealed class CitySkyEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            CitySky s = (CitySky)target;
            EditorGUILayout.HelpBox("Небо: лайнер, самолёты со следом, самолёт с баннером, дирижабль, шары, птицы. Летают в игре вокруг уровня, " +
                                    "на виду с этажа игрока (ViewHeight). «Собрать» — пересоздать (и надпись баннера).", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
                if (GUILayout.Button("Собрать", GUILayout.Height(28))) Build(s);
                GUI.backgroundColor = new Color(1f, 0.95f, 0.6f);
                if (GUILayout.Button("Новый вариант", GUILayout.Height(28))) { Undo.RecordObject(s, "Seed"); s.Seed++; Build(s); }
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Убрать", GUILayout.Height(28))) { s.Clear(); EditorUtility.SetDirty(s); }
            }
            DrawDefaultInspector();
        }

        private static void Build(CitySky s)
        {
            Undo.RegisterFullObjectHierarchyUndo(s.gameObject, "Небо");
            CityAirAssets.Sky(s);
            s.Build();
            EditorUtility.SetDirty(s);
            if (!UnityEditor.EditorApplication.isPlaying && s.gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(s.gameObject.scene);
            Debug.Log($"[Город/небо] собрано летающих: {s.Flyers.Count} · надпись «{s.BannerText}»", s);
        }

        [MenuItem("CatCooks/Застройка/Небо (самолёты, дирижабль, шары, птицы)")]
        private static void Menu()
        {
            CityBlockGenerator g = Object.FindFirstObjectByType<CityBlockGenerator>();
            CitySky s = Object.FindFirstObjectByType<CitySky>();
            if (s == null)
            {
                GameObject go = new GameObject("Sky (generated)");
                Undo.RegisterCreatedObjectUndo(go, "Небо");
                if (g != null) go.transform.SetParent(g.transform, false);
                s = go.AddComponent<CitySky>();
                s.City = g;
            }
            Build(s);
            Selection.activeGameObject = s.gameObject;
        }
    }
}
