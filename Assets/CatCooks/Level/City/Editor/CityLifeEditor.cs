using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ЖИЗНЬ ГОРОДА — меню CatCooks → Застройка: голуби на крышах, бельё на верёвках, свет пропсов (фонари, щиты, антенны).
    /// Свет пропсов подключается сам: редактор следит за иерархией — поставил фонарь/щит/антенну из списка руками
    /// или их расставил генератор — свет появляется сам (выключается в меню «…авто: выкл»).
    /// </summary>
    [InitializeOnLoad]
    public static class CityLifeEditor
    {
        public const string Folder = "Assets/Generated/City/Life";
        private const string AutoKey = "CatCooks.PropGlowAuto";
        private static bool _queued;
        private static double _lastScan;

        static CityLifeEditor()
        {
            EditorApplication.hierarchyChanged += OnHierarchy;
        }

        public static bool Auto { get { return EditorPrefs.GetBool(AutoKey, true); } set { EditorPrefs.SetBool(AutoKey, value); } }

        private static void OnHierarchy()
        {
            if (!Auto || EditorApplication.isPlayingOrWillChangePlaymode || _queued) return;
            _queued = true;
            EditorApplication.delayCall += AutoScan;
        }

        private static void AutoScan()
        {
            _queued = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.timeSinceStartup - _lastScan < 0.4) { _queued = true; EditorApplication.delayCall += AutoScan; return; }
            _lastScan = EditorApplication.timeSinceStartup;
            int n = CityPropGlow.AttachAll(null);
            if (n > 0)
            {
                Material mat = Mat("CC_PropGlow", "CatCooks/City/Prop Glow");
                if (mat != null)
                    foreach (CityPropGlow g in Object.FindObjectsByType<CityPropGlow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                        if (g.GlowMaterial == null || g.GlowMaterial.hideFlags != HideFlags.None) { g.GlowMaterial = mat; g.Rebuild(); }
            }
            if (n > 0)
            {
                FixReadable();
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
                Debug.Log("[Город/свет] подключено к свету пропсов: " + n);
            }
        }

        private static void MakeFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Generated")) AssetDatabase.CreateFolder("Assets", "Generated");
            if (!AssetDatabase.IsValidFolder("Assets/Generated/City")) AssetDatabase.CreateFolder("Assets/Generated", "City");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Generated/City", "Life");
        }

        private static Material Mat(string name, string shader)
        {
            MakeFolder();
            string path = Folder + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            Shader sh = Shader.Find(shader);
            if (sh == null) { Debug.LogWarning("[Город] нет шейдера " + shader + " — подожди компиляцию и запусти снова"); return null; }
            m = new Material(sh) { enableInstancing = true };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ══ свет пропсов ══
        [MenuItem("CatCooks/Застройка/Город: свет пропсов — подключить (фонари, щиты, антенны)", priority = 985)]
        public static void GlowAll()
        {
            Material mat = Mat("CC_PropGlow", "CatCooks/City/Prop Glow");
            int added = CityPropGlow.AttachAll(mat);
            int fixedN = FixReadable();
            int total = 0;
            foreach (CityPropGlow g in Object.FindObjectsByType<CityPropGlow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mat != null) g.GlowMaterial = mat;
                g.Rebuild();
                EditorUtility.SetDirty(g);
                total++;
            }
            if (!EditorApplication.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[Город/свет] свет пропсов: новых " + added + ", всего " + total + (fixedN > 0 ? ", моделям включён Read/Write: " + fixedN : "") +
                      ". Авто-подключение того, что ставишь руками: " + (Auto ? "ВКЛ" : "выкл") + ".");
        }

        [MenuItem("CatCooks/Застройка/Город: свет пропсов — авто для новых: вкл или выкл", priority = 986)]
        private static void ToggleAuto()
        {
            Auto = !Auto;
            Debug.Log("[Город/свет] авто-подключение света к новым фонарям/щитам/антеннам: " + (Auto ? "ВКЛ" : "выкл"));
        }

        /// <summary>Моделям из списка включить Read/Write (нужно, чтобы найти лампу/доску/верх антенны по форме). Собранные по габаритам — пересобрать.</summary>
        private static int FixReadable()
        {
            HashSet<string> paths = new HashSet<string>();
            List<CityPropGlow> approx = new List<CityPropGlow>();
            foreach (CityPropGlow g in Object.FindObjectsByType<CityPropGlow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!g.Approx) continue;
                approx.Add(g);
                foreach (MeshFilter mf in g.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || mf.sharedMesh.isReadable) continue;
                    string p = AssetDatabase.GetAssetPath(mf.sharedMesh);
                    if (!string.IsNullOrEmpty(p)) paths.Add(p);
                }
            }
            int n = 0;
            foreach (string p in paths)
            {
                ModelImporter mi = AssetImporter.GetAtPath(p) as ModelImporter;
                if (mi == null || mi.isReadable) continue;
                mi.isReadable = true;
                mi.SaveAndReimport();
                n++;
            }
            if (n > 0) foreach (CityPropGlow g in approx) if (g != null) g.Rebuild();
            return n;
        }

        // ══ голуби ══
        [MenuItem("CatCooks/Застройка/Город: голуби на крышах", priority = 987)]
        public static void Pigeons()
        {
            CityPigeons p = Object.FindFirstObjectByType<CityPigeons>();
            if (p == null)
            {
                GameObject go = new GameObject("CityPigeons");
                Undo.RegisterCreatedObjectUndo(go, "Голуби");
                if (SceneView.lastActiveSceneView != null) go.transform.position = SceneView.lastActiveSceneView.pivot;   // живут вокруг себя
                p = go.AddComponent<CityPigeons>();
            }
            Material m = Mat("CC_Pigeon", "CatCooks/City/Sway Toon");
            if (m != null)
            {
                m.SetVector("_SwayAxis", new Vector4(0f, 1f, 0f, 0f));
                m.SetFloat("_SwaySpeed", 16f);
                m.SetFloat("_SwayAmp", 0.32f);
                m.SetFloat("_SwayRipple", 0f);
                m.SetFloat("_WindK", 0f);
                EditorUtility.SetDirty(m);
            }
            Undo.RecordObject(p, "Голуби");
            p.PigeonMaterial = m;
            EditorUtility.SetDirty(p);
            Selection.activeGameObject = p.gameObject;
            AssetDatabase.SaveAssets();
            Debug.Log("[Город/голуби] голуби готовы — появятся в игре на крышах вокруг объекта CityPigeons (голубой круг — радиус; поставь его в центр уровня). Стаек " + p.Flocks + ". Настройки — на объекте CityPigeons", p);
        }

        // ══ бельё ══
        [MenuItem("CatCooks/Застройка/Город: бельё на верёвках над дорогами", priority = 988)]
        public static void Laundry()
        {
            CityLaundry l = Object.FindFirstObjectByType<CityLaundry>();
            if (l == null)
            {
                GameObject go = new GameObject("CityLaundry");
                Undo.RegisterCreatedObjectUndo(go, "Бельё");
                if (SceneView.lastActiveSceneView != null) go.transform.position = SceneView.lastActiveSceneView.pivot;   // строится вокруг себя
                l = go.AddComponent<CityLaundry>();
            }
            LaundryMat(l);
            l.Build();
            Selection.activeGameObject = l.gameObject;
            if (!EditorApplication.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(l.gameObject.scene);
        }

        public static void LaundryMat(CityLaundry l)
        {
            Material m = Mat("CC_Laundry", "CatCooks/City/Sway Toon");
            float M = CityPropGlow.CityMeter();
            if (m != null)
            {
                m.SetVector("_SwayAxis", new Vector4(0f, 0f, 1f, 0f));
                m.SetFloat("_SwaySpeed", 1.8f);
                m.SetFloat("_SwayAmp", 0.22f * M);
                m.SetFloat("_SwayRipple", 0.35f / Mathf.Max(0.01f, M));
                m.SetFloat("_WindK", 1.5f);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssets();
            }
            Undo.RecordObject(l, "Бельё");
            l.LaundryMaterial = m;
            EditorUtility.SetDirty(l);
        }
    }

    [CustomEditor(typeof(CityLaundry))]
    public sealed class CityLaundryEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            CityLaundry l = (CityLaundry)target;
            EditorGUILayout.HelpBox("Бельё на верёвках над дорогами между домами соседних модулей. Строится ВОКРУГ ЭТОГО ОБЪЕКТА (розовый круг — радиус): поставь его в центр уровня и нажми «Построить». Ближние дороги — первыми.", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
                if (GUILayout.Button("Построить", GUILayout.Height(26))) { CityLifeEditor.LaundryMat(l); l.Build(); Dirty(l); }
                GUI.backgroundColor = new Color(1f, 0.95f, 0.6f);
                if (GUILayout.Button("Новый вариант", GUILayout.Height(26))) { Undo.RecordObject(l, "Бельё"); l.Seed++; CityLifeEditor.LaundryMat(l); l.Build(); Dirty(l); }
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Убрать", GUILayout.Height(26))) { l.Clear(); Dirty(l); }
            }
            EditorGUILayout.Space(4);
            DrawDefaultInspector();
        }

        private static void Dirty(CityLaundry l)
        {
            EditorUtility.SetDirty(l);
            if (!EditorApplication.isPlaying && l.gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(l.gameObject.scene);
        }
    }
}
