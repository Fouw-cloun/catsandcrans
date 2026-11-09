using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// ЧИСТКА ПАПКИ ATMOSPHERE. Лишнее (старые отдельные облака, горы, диорама горизонта, мастера первичной
    /// сборки, старое небо, неиспользуемые пресеты/материалы/текстуры) переносится в Atmosphere/_Unused —
    /// GUID и ссылки сохраняются, всё возвращается одной кнопкой. Компоненты этих систем убираются из открытых сцен.
    /// Группы замкнуты: после проверки игры папку _Unused можно удалить целиком — проект соберётся.
    /// Меню: CatCooks/Застройка/Атмосфера: чистка папки.
    /// </summary>
    public class CityAtmosphereCleanup : EditorWindow
    {
        private sealed class Group
        {
            public string Title, Why;
            public string[] Files;          // пути от корня Atmosphere (папка — целиком)
            public string[] Components;     // типы компонентов — убрать из сцены
            public string Needs;            // группа, без которой эту нельзя убрать
            public bool On;
        }

        private List<Group> _groups;
        private bool _unusedAssets = true, _keepMy = true;
        private List<string> _preview = new List<string>();
        private Vector2 _scroll;
        private string _root;

        [MenuItem("CatCooks/Застройка/Атмосфера: чистка папки", priority = 960)]
        public static void Open() { GetWindow<CityAtmosphereCleanup>(true, "Атмосфера: чистка").Init(); }

        private void Init()
        {
            _root = FindRoot();
            _groups = new List<Group>
            {
                new Group { Title = "Старые отдельные облака (Clouds)", On = true,
                    Why = "убраны — будут новые; небо рисует Sky Toon",
                    Files = new[] { "Runtime/Sky/CatCooksClouds.cs", "Shaders/CatCooks_Clouds.shader", "Materials/M_Clouds.mat" },
                    Components = new[] { "CatCooksClouds" } },
                new Group { Title = "Дымка гор (Mountain Haze)", On = true,
                    Why = "гор в городе нет",
                    Files = new[] { "Runtime/Horizon/CatCooksMountainHaze.cs" },
                    Components = new[] { "CatCooksMountainHaze" } },
                new Group { Title = "Диорама горизонта (кольца облаков, стаи)", On = true, Needs = "Мастера первичной сборки",
                    Why = "горизонт — дальние пояса города и небо",
                    Files = new[] { "Runtime/Horizon/DioramaRig.cs", "Shaders/CatCooks_DioramaRing.shader", "Shaders/CatCooks_DioramaFlock.shader",
                                    "Materials/Horizon", "Textures/Diorama_Bird.png", "Textures/Diorama_Clouds.png", "Textures/Diorama_Clouds_Puffs.png",
                                    "Textures/Diorama_Clouds_Soft.png", "Textures/Diorama_Clouds_Wisps.png", "Textures/Diorama_Debris.png", "Textures/Diorama_Dust.png" },
                    Components = new[] { "DioramaRig" } },
                new Group { Title = "Мастера первичной сборки", On = true,
                    Why = "«Собрать сцену», «Единая система», «Проверить сцену», «Точка привязки», фабрика пресетов, генератор Volume — сцена уже собрана, пресеты делают наши инструменты",
                    Files = new[] { "Editor/CatCooksAtmosphereUnify.cs", "Editor/CatCooksAtmosphereUnify.Light.cs", "Editor/CatCooksAtmosphereSetup.cs",
                                    "Editor/CatCooksAtmosphereDoctor.cs", "Editor/CatCooksAtmosphereAnchor.cs", "Editor/CatCooksPresetFactory.cs", "Editor/CatCooksVolumeGenerator.cs" },
                    Components = new string[0] },
                new Group { Title = "Старое небо (Stylized Sky)", On = true,
                    Why = "небо — Sky Toon (запасной — City V2 Art Sky остаётся)",
                    Files = new[] { "Shaders/CatCooks_StylizedSky.shader", "Materials/M_Sky.mat" },
                    Components = new string[0] },
            };
        }

        private void OnGUI()
        {
            if (_groups == null) Init();
            if (string.IsNullOrEmpty(_root)) { EditorGUILayout.HelpBox("Папка Atmosphere не найдена (ищу по CatCooksMoodDirector.cs).", MessageType.Error); return; }
            EditorGUILayout.HelpBox("Atmosphere: " + _root + "\nЛишнее переносится в " + _root + "/_Unused (GUID сохраняются, «Вернуть всё» — назад). " +
                                    "Компоненты убираются из открытых сцен. Проверь игру — потом папку _Unused можно удалить.", MessageType.Info);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (Group g in _groups)
            {
                EditorGUILayout.BeginVertical("box");
                bool was = g.On;
                g.On = EditorGUILayout.ToggleLeft(g.Title, g.On, EditorStyles.boldLabel);
                if (g.On && !was && g.Needs != null) Get(g.Needs).On = true;
                if (!g.On && was) foreach (Group o in _groups) if (o.Needs == g.Title) o.On = false;
                EditorGUILayout.LabelField(g.Why + (g.Needs != null ? "  (вместе с «" + g.Needs + "»)" : ""), EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.BeginVertical("box");
            _unusedAssets = EditorGUILayout.ToggleLeft("Неиспользуемые пресеты, материалы, текстуры", _unusedAssets, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Presets / Materials / Textures, на которые не ссылается ни сцена из Build Settings, ни открытая сцена, ни префабы и ассеты проекта.", EditorStyles.wordWrappedMiniLabel);
            _keepMy = EditorGUILayout.ToggleLeft("не трогать Presets/My", _keepMy);
            if (GUILayout.Button("Показать список")) _preview = UnusedAssets();
            foreach (string s in _preview) EditorGUILayout.LabelField("  " + s.Substring(_root.Length + 1), EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();

            GUILayout.Space(6);
            if (GUILayout.Button("Перенести отмеченное в _Unused", GUILayout.Height(30))) Run();
            if (GUILayout.Button("Вернуть всё из _Unused")) Restore();
        }

        private Group Get(string title) { foreach (Group g in _groups) if (g.Title == title) return g; return null; }

        // ─────────────────────────────────────────── перенос
        private void Run()
        {
            // 1. компоненты из открытых сцен
            int comps = 0;
            foreach (Group g in _groups)
            {
                if (!g.On) continue;
                foreach (string tn in g.Components) comps += RemoveComponents(tn);
            }
            if (comps > 0)
            {
                Debug.Log("[Атмосфера/чистка] убрано компонентов из сцены: " + comps);
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                { Debug.LogWarning("[Атмосфера/чистка] сцены не сохранены — перенос отменён"); return; }
            }
            // 2. что ещё используется сценами — не трогаем
            HashSet<string> used = SceneDeps();
            List<string> files = new List<string>();
            foreach (Group g in _groups)
            {
                if (!g.On) continue;
                foreach (string f in g.Files)
                {
                    string p = _root + "/" + f;
                    if (!AssetDatabase.IsValidFolder(p) && AssetDatabase.LoadMainAssetAtPath(p) == null) continue;
                    if (used.Contains(p) && !p.EndsWith(".cs")) { Debug.LogWarning("[Атмосфера/чистка] " + f + " используется в сцене — оставлен"); continue; }
                    files.Add(p);
                }
            }
            if (_unusedAssets) files.AddRange(UnusedAssets());
            int moved = 0;
            List<string> log = new List<string>();
            foreach (string p in files)
            {
                string rel = p.Substring(_root.Length + 1);
                string dst = _root + "/_Unused/" + rel;
                MakeFolder(Path.GetDirectoryName(dst).Replace('\\', '/'));
                // уже лежит в _Unused (архив вернул файл на место) — старую копию заменяем, иначе два одинаковых класса
                if (AssetDatabase.LoadMainAssetAtPath(dst) != null) AssetDatabase.DeleteAsset(dst);
                string err = AssetDatabase.MoveAsset(p, dst);
                if (string.IsNullOrEmpty(err)) { moved++; log.Add(p + "|" + dst); }
                else Debug.LogWarning("[Атмосфера/чистка] " + rel + ": " + err);
            }
            string man = _root + "/_Unused/moved.txt";
            if (log.Count > 0) File.AppendAllText(man, string.Join("\n", log.ToArray()) + "\n");
            AssetDatabase.Refresh();
            Debug.Log("[Атмосфера/чистка] перенесено в _Unused: " + moved + ". Проверь игру; всё ок — удали папку " + _root + "/_Unused");
            _preview.Clear();
        }

        private void Restore()
        {
            string man = _root + "/_Unused/moved.txt";
            if (!File.Exists(man)) { Debug.Log("[Атмосфера/чистка] нечего возвращать"); return; }
            string[] lines = File.ReadAllLines(man);
            int n = 0;
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string[] kv = lines[i].Split('|');
                if (kv.Length != 2) continue;
                MakeFolder(Path.GetDirectoryName(kv[0]).Replace('\\', '/'));
                if (string.IsNullOrEmpty(AssetDatabase.MoveAsset(kv[1], kv[0]))) n++;
            }
            File.Delete(man);
            AssetDatabase.Refresh();
            Debug.Log("[Атмосфера/чистка] возвращено: " + n + " (компоненты в сцене — Ctrl+Z или добавить заново)");
        }

        private static int RemoveComponents(string typeName)
        {
            System.Type t = null;
            foreach (System.Reflection.Assembly a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                t = a.GetType(typeName, false);
                if (t != null) break;
            }
            if (t == null || !typeof(Component).IsAssignableFrom(t)) return 0;
            int n = 0;
            foreach (Object o in Object.FindObjectsByType(t, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Component c = o as Component;
                if (c == null) continue;
                GameObject go = c.gameObject;
                Undo.DestroyObjectImmediate(c);
                n++;
                if (go != null && go.transform.childCount == 0 && go.GetComponents<Component>().Length == 1) Undo.DestroyObjectImmediate(go);
                if (!UnityEditor.EditorApplication.isPlaying && go != null) EditorSceneManager.MarkSceneDirty(go.scene);
            }
            return n;
        }

        // ─────────────────────────────────────────── что используется
        private static HashSet<string> SceneDeps()
        {
            List<string> roots = new List<string>();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes) if (s.enabled && !string.IsNullOrEmpty(s.path)) roots.Add(s.path);
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                string p = EditorSceneManager.GetSceneAt(i).path;
                if (!string.IsNullOrEmpty(p) && !roots.Contains(p)) roots.Add(p);
            }
            return new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(), true));
        }

        private List<string> UnusedAssets()
        {
            HashSet<string> used = SceneDeps();
            // ассеты проекта вне Atmosphere: префабы, материалы, пресеты, сцены
            List<string> roots = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab t:Material t:ScriptableObject t:Scene", new[] { "Assets" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.StartsWith(_root + "/")) continue;
                roots.Add(p);
            }
            // скрипты Atmosphere (их ссылки по умолчанию — материалы, текстуры)
            foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { _root }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!p.Contains("/_Unused/") && !InGroups(p)) roots.Add(p);
            }
            foreach (string d in AssetDatabase.GetDependencies(roots.ToArray(), true)) used.Add(d);
            // зависимости уже используемых ассетов Atmosphere (стиль → палитра и т.д.)
            foreach (string d in AssetDatabase.GetDependencies(new List<string>(used).ToArray(), true)) used.Add(d);

            List<string> res = new List<string>();
            foreach (string sub in new[] { "Presets", "Materials", "Textures" })
            {
                string folder = _root + "/" + sub;
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                foreach (string guid in AssetDatabase.FindAssets("", new[] { folder }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetDatabase.IsValidFolder(p) || p.Contains("/_Unused/")) continue;
                    if (_keepMy && p.StartsWith(_root + "/Presets/My/")) continue;
                    string ext = Path.GetExtension(p).ToLowerInvariant();
                    if (ext != ".asset" && ext != ".mat" && ext != ".png" && ext != ".tga" && ext != ".psd" && ext != ".jpg" && ext != ".exr") continue;
                    if (used.Contains(p) || InGroups(p) || res.Contains(p)) continue;
                    res.Add(p);
                }
            }
            res.Sort();
            return res;
        }

        private bool InGroups(string p)
        {
            foreach (Group g in _groups)
                foreach (string f in g.Files)
                {
                    string q = _root + "/" + f;
                    if (p == q || p.StartsWith(q + "/")) return true;
                }
            return false;
        }

        private static string FindRoot()
        {
            foreach (string guid in AssetDatabase.FindAssets("CatCooksMoodDirector t:MonoScript"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!p.EndsWith("/CatCooksMoodDirector.cs") || p.Contains("/_Unused/")) continue;
                string d = Path.GetDirectoryName(p).Replace('\\', '/');          // …/Runtime/Core
                d = Path.GetDirectoryName(Path.GetDirectoryName(d)).Replace('\\', '/');
                return d;
            }
            return null;
        }

        private static void MakeFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            MakeFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
