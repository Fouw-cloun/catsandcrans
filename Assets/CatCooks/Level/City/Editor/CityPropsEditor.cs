using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Level
{
    [CustomEditor(typeof(CityProps))]
    public sealed class CityPropsEditor : Editor
    {
        private Object _folder;
        private bool _sub = true;

        public override void OnInspectorGUI()
        {
            CityProps p = (CityProps)target;
            serializedObject.Update();
            EditorGUILayout.HelpBox("Пропсы и машины — только ближний слой (модули «слой 1»). Размер подгоняется сам: метры × единиц в метре. " +
                                    "Тип каждого пропса определяется по имени — проверь список ниже, поправь и поставь Manual.", MessageType.None);

            // ── папка пропсов: только из неё ──
            EditorGUILayout.LabelField("Папка пропсов (ставится только то, что в ней)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                Object nf = EditorGUILayout.ObjectField(p.SourceFolder, typeof(DefaultAsset), false);
                bool ns = GUILayout.Toggle(p.SourceSubfolders, "с подпапками", GUILayout.Width(100));
                if (EditorGUI.EndChangeCheck())
                {
                    if (nf != null && !AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(nf))) nf = null;
                    Undo.RecordObject(p, "Папка пропсов");
                    p.SourceFolder = nf; p.SourceSubfolders = ns;
                    EditorUtility.SetDirty(p);
                    if (nf != null) FromFolder(p);
                }
                GUI.enabled = p.SourceFolder != null;
                if (GUILayout.Button("Обновить", GUILayout.Width(80))) FromFolder(p);
                GUI.enabled = true;
            }
            if (p.SourceFolder != null)
                EditorGUILayout.HelpBox("Ставится ТОЛЬКО содержимое папки. Перед каждой расстановкой список сверяется с папкой сам: добавил модель в папку — появится, удалил — пропадёт. " +
                                        "Тип (фонарь, дерево, знак…) — по имени модели; не определился — выбери Kind в списке и поставь Manual. Чего нет в папке — того не будет (нет машин — нет машин).", MessageType.Info);

            // ── загрузка ──
            EditorGUILayout.LabelField("Загрузка пропсов (без папки — вручную)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _folder = EditorGUILayout.ObjectField(_folder, typeof(DefaultAsset), false);
                _sub = GUILayout.Toggle(_sub, "с подпапками", GUILayout.Width(100));
                GUI.enabled = _folder != null;
                if (GUILayout.Button("Добавить из папки", GUILayout.Width(130))) Load(p, AssetDatabase.GetAssetPath(_folder), _sub);
                GUI.enabled = true;
            }
            Rect drop = GUILayoutUtility.GetRect(0, 34, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "Перетащи сюда папки props / street props или модели", EditorStyles.helpBox);
            DropZone(p, drop);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Определить типы заново (кроме Manual)"))
                {
                    Undo.RecordObject(p, "Типы пропсов");
                    foreach (PropEntry e in p.Props) CityProps.Guess(e);
                    EditorUtility.SetDirty(p);
                }
                if (GUILayout.Button("Очистить список", GUILayout.Width(120))) { Undo.RecordObject(p, "Пропсы"); p.Props.Clear(); EditorUtility.SetDirty(p); }
            }
            GUI.backgroundColor = new Color(0.6f, 0.85f, 1f);
            if (GUILayout.Button("Атласы пропсов (найти текстуры рядом с моделями и наложить)", GUILayout.Height(22))) Atlases(p, true);
            GUI.backgroundColor = Color.white;
            Summary(p);

            // ── расстановка ──
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
                if (GUILayout.Button("Расставить", GUILayout.Height(28))) { p.GenerateAll(); Dirty(p); }
                GUI.backgroundColor = new Color(1f, 0.95f, 0.6f);
                if (GUILayout.Button("Новый вариант", GUILayout.Height(28))) { Undo.RecordObject(p, "Seed"); p.Seed++; p.GenerateAll(); Dirty(p); }
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Убрать", GUILayout.Height(28))) { p.ClearAll(); Dirty(p); }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(1f, 0.85f, 0.55f);
                if (GUILayout.Button("Только улица (фонари, деревья… — кодом)", GUILayout.Height(22)))
                {
                    Undo.RecordObject(p, "Пропсы — только улица"); p.StreetOnlyPreset(); EditorUtility.SetDirty(p);
                }
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Убрать с ближнего слоя", GUILayout.Height(22))) { p.ClearLayer1(); Dirty(p); }
            }
            // ── добавить к стоящему ──
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Добавить к уже стоящему (ничего не убирая)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Солнечные панели рядами на крыши и вывески на фасады вокруг центра (Extras Center, пусто — центр уровня). " +
                                    "Частота — Panel Roof Chance / Sign Facade Chance ниже. Уже стоящее не убирается и не перекрывается; «Расставить» добавленное не трогает.", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
                if (GUILayout.Button("Добавить панели и вывески", GUILayout.Height(24)))
                {
                    int pn, sn; string why;
                    int mods = p.AddExtras(out pn, out sn, out why);
                    Debug.Log("[Застройка/пропсы] добавлено к стоящему: солнечных панелей " + pn + ", вывесок " + sn + " (модулей " + mods + ")" + why, p);
                    Dirty(p);
                }
                GUI.backgroundColor = new Color(1f, 0.95f, 0.6f);
                if (GUILayout.Button("Ещё вариант", GUILayout.Height(24)))
                {
                    Undo.RecordObject(p, "Пропсы"); p.ExtrasSeed++;
                    int pn, sn; string why;
                    p.AddExtras(out pn, out sn, out why);
                    Debug.Log("[Застройка/пропсы] добавлено ещё: панелей " + pn + ", вывесок " + sn + why, p);
                    Dirty(p);
                }
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Убрать добавленное", GUILayout.Height(24)))
                {
                    int n = p.ClearExtras();
                    Debug.Log("[Застройка/пропсы] убрано добавленное (панели и вывески) в модулях: " + n, p);
                    Dirty(p);
                }
            }
            if (p.LastModules > 0)
                EditorGUILayout.LabelField($"Последняя расстановка: модулей {p.LastModules}, пропсов {p.LastProps}, машин в движении {p.LastCars} · {p.Meter():0.##} ед./м", EditorStyles.miniLabel);
            else
                EditorGUILayout.LabelField($"Единиц в метре сейчас: {p.Meter():0.##}", EditorStyles.miniLabel);
            EditorGUILayout.Space(6);
            DrawPropertiesExcluding(serializedObject, "m_Script", "SourceFolder", "SourceSubfolders");
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Атласы пропсов — как у домов: ищем материалы/текстуры в папках моделей (и выше, со всеми подпапками общей папки),
        /// подбираем по словам в путях (props ↔ props, street props ↔ street), белые материалы FBX переназначаем в импорте.
        /// Шейдер — тёплый шейдер домов города (без окон), чтобы пропсы были в одной гамме с домами.
        /// </summary>
        public static void Atlases(CityProps p, bool log)
        {
            List<GameObject> models = new List<GameObject>();
            foreach (PropEntry e in p.Props) if (e != null && e.Prefab != null && !models.Contains(e.Prefab)) models.Add(e.Prefab);
            if (models.Count == 0) return;
            // общая папка всех пропсов (например, 1layer) — там ищем со всеми подпапками
            string common = null;
            foreach (GameObject m in models)
            {
                string d = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(m)).Replace('\\', '/');
                if (common == null) { common = d; continue; }
                while (!string.IsNullOrEmpty(common) && !(d + "/").StartsWith(common + "/")) common = System.IO.Path.GetDirectoryName(common).Replace('\\', '/');
            }
            if (string.IsNullOrEmpty(common) || common == "Assets") common = null;
            Dictionary<GameObject, Material> chosen;
            int n = CityAtlasTool.Apply(models, common, "пропсы", log, null, true, false, out chosen);
            // уже расставленные: сбросить подмены материалов, чтобы подтянулся импорт
            int fixedN = 0;
            if (p.City != null && p.City.Container() != null && p.RoofMaterial == null && p.StreetMaterial == null)
                foreach (Transform t in p.City.Container().GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != CityProps.PropsName && t.name != CityProps.TrafficName) continue;
                    foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                        if (PrefabUtility.IsPartOfPrefabInstance(r)) { PrefabUtility.RevertObjectOverride(r, InteractionMode.AutomatedAction); fixedN++; }
                }
            if (log) Debug.Log($"[Застройка/пропсы] атласы: моделей с новым материалом {n} из {models.Count}, папка поиска «{common ?? "папки моделей"}», обновлено в сцене {fixedN}. " +
                               (chosen.Count == 0 ? "Текстур рядом не нашёл — положи атласы props / street props в папку с моделями или укажи RoofMaterial/StreetMaterial." : ""), p);
        }

        private static void FromFolder(CityProps p)
        {
            Undo.RecordObject(p, "Пропсы из папки");
            int a, r; List<string> u;
            p.SyncFolder(true, out a, out r, out u);
            if (a == 0 && r == 0)
                Debug.Log("[Застройка/пропсы] папка: без изменений, в списке " + p.Props.Count + (u.Count > 0 ? ". Тип не определён: " + string.Join(", ", u.ToArray()) : ""), p);
            if (a > 0) Atlases(p, true);
            EditorUtility.SetDirty(p);
        }

        private static void Summary(CityProps p)
        {
            Dictionary<PropKind, int> n = new Dictionary<PropKind, int>();
            foreach (PropEntry e in p.Props)
            {
                if (e == null || e.Prefab == null) continue;
                int v; n.TryGetValue(e.Kind, out v); n[e.Kind] = v + 1;
            }
            List<string> parts = new List<string>();
            foreach (KeyValuePair<PropKind, int> kv in n) parts.Add(kv.Key + " " + kv.Value);
            EditorGUILayout.LabelField("В списке: " + (parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "пусто"), EditorStyles.wordWrappedMiniLabel);
        }

        private static void DropZone(CityProps p, Rect r)
        {
            Event ev = Event.current;
            if (!r.Contains(ev.mousePosition)) return;
            if (ev.type == EventType.DragUpdated) { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; ev.Use(); }
            else if (ev.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (Object o in DragAndDrop.objectReferences)
                {
                    string path = AssetDatabase.GetAssetPath(o);
                    if (AssetDatabase.IsValidFolder(path)) Load(p, path, true);
                    else { GameObject go = o as GameObject; if (go != null) Add(p, go); }
                }
                EditorUtility.SetDirty(p);
                ev.Use();
            }
        }

        private static void Load(CityProps p, string folder, bool sub)
        {
            if (string.IsNullOrEmpty(folder)) return;
            Undo.RecordObject(p, "Пропсы из папки");
            int added = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!sub && System.IO.Path.GetDirectoryName(path).Replace('\\', '/') != folder) continue;
                GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null && Add(p, go)) added++;
            }
            EditorUtility.SetDirty(p);
            Debug.Log($"[Застройка/пропсы] из {folder}: добавлено {added}, всего {p.Props.Count}. Типы определены по имени — проверь в списке", p);
            if (added > 0) Atlases(p, true);
        }

        private static bool Add(CityProps p, GameObject go)
        {
            foreach (PropEntry e in p.Props) if (e != null && e.Prefab == go) return false;
            PropEntry ne = new PropEntry { Prefab = go };
            CityProps.Guess(ne);
            p.Props.Add(ne);
            return true;
        }

        private static void Dirty(CityProps p)
        {
            EditorUtility.SetDirty(p);
            if (!UnityEditor.EditorApplication.isPlaying && p.gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
        }

        [MenuItem("CatCooks/Застройка/Пропсы и машины (ближний слой)")]
        private static void Menu()
        {
            CityBlockGenerator g = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<CityBlockGenerator>() : null;
            if (g == null) g = Object.FindFirstObjectByType<CityBlockGenerator>();
            if (g == null) { EditorUtility.DisplayDialog("Пропсы", "Нет генератора застройки (City) в сцене.", "Ок"); return; }
            CityProps p = g.GetComponent<CityProps>();
            if (p == null) { p = Undo.AddComponent<CityProps>(g.gameObject); p.City = g; }
            Selection.activeGameObject = g.gameObject;
            EditorGUIUtility.PingObject(p);
            Debug.Log("[Застройка/пропсы] компонент «Пропсы и машины» на City. Перетащи папки props и street props в его окно, потом «Расставить»", p);
        }
    }
}
