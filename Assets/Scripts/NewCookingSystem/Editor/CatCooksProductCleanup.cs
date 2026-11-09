#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CatCooks.Food
{
    /// <summary>
    /// ЧИСТКА БАЗЫ ПРОДУКТОВ — CatCooks → Еда → Чистка: неиспользуемые продукты.
    ///
    /// Ищет, кто ссылается на каждый продукт (ProductDef):
    ///   • сцены, префабы, ассеты (рецепты, настройки, другие продукты) — читает файлы как текст,
    ///     открывать сцены не нужно;
    ///   • НЕ считаются «использованием» общие списки: Food Database (там все продукты подряд),
    ///     Cookware Liquid Visual (меню «Тара: принимать ВСЁ» прописало туда всё) и Cookware Heap Visual;
    ///   • код (*.cs) — если название продукта встречается в коде строкой (меню и рецепты ищут по названию).
    /// Три группы: НИГДЕ (отмечены сразу) · ТОЛЬКО В КОДЕ (не отмечены — реши сам) · ИСПОЛЬЗУЕТСЯ.
    /// Удаление — в корзину системы, перед этим резервная копия .unitypackage в папке проекта ProductBackups.
    /// После — Food Database пересобирается, пустые строки в LiquidAnchor открытых сцен убираются.
    /// </summary>
    public sealed class CatCooksProductCleanup : EditorWindow
    {
        [MenuItem("CatCooks/Еда/Чистка: неиспользуемые продукты", priority = 30)]
        public static void Open()
        {
            var w = GetWindow<CatCooksProductCleanup>("Чистка продуктов");
            w.minSize = new Vector2(760f, 420f);
            w.Scan();
        }

        private enum Kind { Nowhere = 0, OldCarcass = 1, CodeOnly = 2, Used = 3 }

        private sealed class Row
        {
            public ProductDef P;
            public string Path, Guid;
            public Kind Kind;
            public bool Mark;
            public readonly List<string> Uses = new List<string>();
            public bool InCode;
        }

        // Части туши из v2.2 — больше нигде не нужны (в v2.3 куски общие: Свинина/Говядина + несколько своих)
        private static readonly HashSet<string> OldCarcass = new HashSet<string>(new[]
        {
            "свиная шея", "свиная лопатка", "свиная грудка", "свиная корейка", "свиная грудинка", "свиной карбонад", "свиная вырезка",
            "свиной окорок", "свиное колено", "свиная ножка", "стейк из свиной шеи", "стейк из корейки", "стейк из карбонада", "медальон из вырезки",
            "говяжья шея", "говяжья лопатка", "толстый край", "говяжьи рёбра", "говяжья пашина", "тонкий край", "говяжья вырезка", "кострец",
            "говяжье бедро", "говяжье колено", "говяжья ножка", "стейк стриплойн", "филе-миньон", "рамп-стейк",
        });

        // Общие списки — ссылка оттуда не значит «нужен»
        private static readonly HashSet<string> RegistryClasses = new HashSet<string> { "FoodDatabase", "CookwareLiquidVisual", "CookwareHeapVisual" };

        private readonly List<Row> _rows = new List<Row>();
        private Vector2 _scroll;
        private bool _showUsed, _showCode = true;
        private bool _backup = true;
        private string _filter = "";
        private string _status = "";

        // ───────────────────────── поиск ─────────────────────────

        private void Scan()
        {
            _rows.Clear();
            var byGuid = new Dictionary<string, Row>();
            try
            {
                EditorUtility.DisplayProgressBar("Чистка продуктов", "продукты…", 0f);
                foreach (string g in AssetDatabase.FindAssets("t:ProductDef"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(g);
                    ProductDef p = AssetDatabase.LoadAssetAtPath<ProductDef>(path);
                    if (p == null) continue;
                    var r = new Row { P = p, Path = path, Guid = g };
                    _rows.Add(r);
                    byGuid[g] = r;
                }

                // 1) файлы сцен, префабов, ассетов — как текст (YAML)
                string[] files = AssetDatabase.GetAllAssetPaths()
                    .Where(a => a.StartsWith("Assets/") && (a.EndsWith(".unity") || a.EndsWith(".prefab") || a.EndsWith(".asset")))
                    .ToArray();
                var scriptName = new Dictionary<string, string>();
                var guidRx = new Regex(@"guid: ([0-9a-f]{32})");
                var scriptRx = new Regex(@"m_Script: \{fileID: \d+, guid: ([0-9a-f]{32})");
                for (int i = 0; i < files.Length; i++)
                {
                    string f = files[i];
                    if (i % 50 == 0) EditorUtility.DisplayProgressBar("Чистка продуктов", f, 0.05f + 0.8f * i / Mathf.Max(1, files.Length));
                    string full = Path.Combine(Directory.GetCurrentDirectory(), f);
                    if (!File.Exists(full)) continue;
                    string text;
                    try { text = File.ReadAllText(full); } catch { continue; }
                    string selfGuid = AssetDatabase.AssetPathToGUID(f);
                    if (!text.StartsWith("%YAML"))
                    {
                        // двоичный файл — грубо, по зависимостям
                        foreach (string dep in AssetDatabase.GetDependencies(f, false))
                        {
                            string dg = AssetDatabase.AssetPathToGUID(dep);
                            if (dg != selfGuid && byGuid.TryGetValue(dg, out Row rr)) AddUse(rr, $"{Short(f)} (двоичный)");
                        }
                        continue;
                    }
                    if (text.IndexOf("guid:", StringComparison.Ordinal) < 0) continue;
                    string[] docs = text.Split(new[] { "\n--- !u!" }, StringSplitOptions.None);
                    foreach (string doc in docs)
                    {
                        string cls = null;
                        Match sm = scriptRx.Match(doc);
                        if (sm.Success)
                        {
                            string sg = sm.Groups[1].Value;
                            if (!scriptName.TryGetValue(sg, out cls))
                            {
                                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(sg));
                                Type t = ms != null ? ms.GetClass() : null;
                                cls = t != null ? t.Name : (ms != null ? ms.name : "?");
                                scriptName[sg] = cls;
                            }
                            if (RegistryClasses.Contains(cls)) continue;
                        }
                        foreach (Match m in guidRx.Matches(doc))
                        {
                            string g = m.Groups[1].Value;
                            if (g == selfGuid || (sm.Success && g == sm.Groups[1].Value)) continue;
                            if (!byGuid.ContainsKey(g)) continue;
                            // префаб в сцене: правки списков (_products у Food Database / LiquidAnchor, _ignore) — не в счёт
                            if (!sm.Success)
                            {
                                int pp = doc.LastIndexOf("propertyPath:", m.Index, StringComparison.Ordinal);
                                if (pp >= 0)
                                {
                                    int eol = doc.IndexOf('\n', pp);
                                    string prop = doc.Substring(pp + 13, (eol > pp ? eol : doc.Length) - pp - 13).Trim();
                                    if (prop.StartsWith("_products") || prop.StartsWith("_ignore")) continue;
                                }
                            }
                            if (byGuid.TryGetValue(g, out Row rr)) AddUse(rr, cls != null && cls != "ProductDef" ? $"{Short(f)} ({cls})" : Short(f));
                        }
                    }
                }

                // 2) код — название строкой
                EditorUtility.DisplayProgressBar("Чистка продуктов", "код…", 0.9f);
                var literals = new HashSet<string>();
                var strRx = new Regex("\"((?:[^\"\\\\\\n]|\\\\.)*)\"");
                foreach (string cs in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
                {
                    if (cs.EndsWith("CatCooksProductCleanup.cs")) continue;
                    string text;
                    try { text = File.ReadAllText(cs); } catch { continue; }
                    foreach (Match m in strRx.Matches(text)) literals.Add(m.Groups[1].Value.Trim().ToLowerInvariant());
                }
                foreach (Row r in _rows)
                {
                    string t = (r.P.Title ?? "").Trim().ToLowerInvariant();
                    r.InCode = t.Length > 0 && (literals.Contains(t) || literals.Contains(t.Replace('ё', 'е')));
                    bool old = OldCarcass.Contains(t);
                    r.Kind = r.Uses.Count > 0 ? Kind.Used : old ? Kind.OldCarcass : r.InCode ? Kind.CodeOnly : Kind.Nowhere;
                    r.Mark = r.Kind == Kind.Nowhere || r.Kind == Kind.OldCarcass;
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            _rows.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : string.Compare(a.P.Title, b.P.Title, StringComparison.CurrentCulture));
            int n0 = _rows.Count(r => r.Kind == Kind.Nowhere), n1 = _rows.Count(r => r.Kind == Kind.OldCarcass), n2 = _rows.Count(r => r.Kind == Kind.CodeOnly);
            _status = $"Продуктов {_rows.Count}: нигде — {n0}, старые куски туши — {n1}, только в коде — {n2}, используются — {_rows.Count - n0 - n1 - n2}";
            Debug.Log("[Чистка продуктов] " + _status);
        }

        private static void AddUse(Row r, string where)
        {
            if (!r.Uses.Contains(where)) r.Uses.Add(where);
        }

        private static string Short(string path)
        {
            string n = Path.GetFileName(path);
            return n;
        }

        // ───────────────────────── окно ─────────────────────────

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Кто ссылается на продукт: сцены, префабы, рецепты, настройки (файлы читаются целиком, сцены открывать не надо). " +
                "Списки Food Database и LiquidAnchor (тара «принимать ВСЁ») не в счёт. «Только в коде» — название встречается в скриптах: " +
                "его может пересоздать меню, удаляй осознанно. Удаление — в корзину системы + резервная копия .unitypackage.", MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Найти заново", GUILayout.Width(120))) Scan();
            _showCode = GUILayout.Toggle(_showCode, "показать «только в коде»", GUILayout.Width(190));
            _showUsed = GUILayout.Toggle(_showUsed, "показать используемые", GUILayout.Width(170));
            GUILayout.Label("фильтр:", GUILayout.Width(48));
            _filter = GUILayout.TextField(_filter, GUILayout.Width(150));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(_status, EditorStyles.miniBoldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            Kind last = (Kind)(-1);
            foreach (Row r in _rows)
            {
                if (r.P == null) continue;
                if (r.Kind == Kind.Used && !_showUsed) continue;
                if (r.Kind == Kind.CodeOnly && !_showCode) continue;
                if (_filter.Length > 0 && (r.P.Title ?? "").IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (r.Kind != last)
                {
                    last = r.Kind;
                    string head = r.Kind == Kind.Nowhere ? "НИГДЕ НЕ ИСПОЛЬЗУЮТСЯ" : r.Kind == Kind.OldCarcass ? "СТАРЫЕ КУСКИ ТУШИ (v2.2) — больше не нужны"
                                : r.Kind == Kind.CodeOnly ? "ТОЛЬКО В КОДЕ (по названию) — реши сам" : "ИСПОЛЬЗУЮТСЯ";
                    GUILayout.Space(6);
                    EditorGUILayout.LabelField(head, EditorStyles.boldLabel);
                }
                EditorGUILayout.BeginHorizontal();
                GUI.enabled = r.Kind != Kind.Used;
                r.Mark = EditorGUILayout.Toggle(r.Mark && r.Kind != Kind.Used, GUILayout.Width(18));
                GUI.enabled = true;
                if (GUILayout.Button("▸", GUILayout.Width(22))) { EditorGUIUtility.PingObject(r.P); Selection.activeObject = r.P; }
                EditorGUILayout.LabelField($"{r.P.Title}", GUILayout.Width(230));
                EditorGUILayout.LabelField($"Id {r.P.Id}", GUILayout.Width(60));
                EditorGUILayout.LabelField(r.P.Group != null ? r.P.Group.name : "—", GUILayout.Width(120));
                string info = r.Uses.Count > 0 ? string.Join(", ", r.Uses.Take(3)) + (r.Uses.Count > 3 ? $" (+{r.Uses.Count - 3})" : "")
                            : r.InCode ? "название есть в коде" : "";
                EditorGUILayout.LabelField(info, EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            GUILayout.Space(4);
            int marked = _rows.Count(r => r.Mark && r.P != null && r.Kind != Kind.Used);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Снять все", GUILayout.Width(90))) foreach (Row r in _rows) r.Mark = false;
            if (GUILayout.Button("Отметить «нигде» и старые", GUILayout.Width(190))) foreach (Row r in _rows) r.Mark = r.Kind == Kind.Nowhere || r.Kind == Kind.OldCarcass;
            if (GUILayout.Button("Выделить отмеченные", GUILayout.Width(150)))
                Selection.objects = _rows.Where(r => r.Mark && r.P != null && r.Kind != Kind.Used).Select(r => (Object)r.P).ToArray();
            _backup = GUILayout.Toggle(_backup, "резервная копия", GUILayout.Width(130));
            GUI.enabled = marked > 0;
            GUI.color = new Color(1f, 0.75f, 0.7f);
            if (GUILayout.Button($"Удалить отмеченные ({marked})")) Delete();
            GUI.color = Color.white;
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        // ───────────────────────── удаление ─────────────────────────

        private void Delete()
        {
            List<Row> del = _rows.Where(r => r.Mark && r.P != null && r.Kind != Kind.Used).ToList();
            if (del.Count == 0) return;
            string names = string.Join(", ", del.Take(25).Select(r => r.P.Title)) + (del.Count > 25 ? $" … (ещё {del.Count - 25})" : "");
            if (!EditorUtility.DisplayDialog("Чистка продуктов", $"Удалить {del.Count} продуктов?\n\n{names}\n\n" +
                    (_backup ? "Перед удалением — резервная копия в папке ProductBackups (рядом с Assets). " : "") +
                    "Файлы уйдут в корзину системы.", "Удалить", "Отмена")) return;

            var log = new StringBuilder();
            if (_backup)
            {
                string dir = Path.Combine(Directory.GetCurrentDirectory(), "ProductBackups");
                Directory.CreateDirectory(dir);
                string pkg = Path.Combine(dir, $"Products_{DateTime.Now:yyyyMMdd_HHmmss}.unitypackage");
                AssetDatabase.ExportPackage(del.Select(r => r.Path).ToArray(), pkg, ExportPackageOptions.Default);
                log.AppendLine($"Резервная копия: {pkg}  (вернуть — Assets → Import Package → Custom Package)");
            }
            int ok = 0;
            foreach (Row r in del)
            {
                string title = r.P.Title;
                if (AssetDatabase.MoveAssetToTrash(r.Path)) { ok++; log.AppendLine($"－ {title}"); }
                else log.AppendLine($"✖ не удалось: {title} ({r.Path})");
            }
            AssetDatabase.Refresh();

            // Food Database — пересобрать; пустые строки в LiquidAnchor открытых сцен — убрать
            int dbs = 0, lvs = 0;
            foreach (FoodDatabase db in Object.FindObjectsByType<FoodDatabase>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                db.CollectFromProject();
                EditorUtility.SetDirty(db);
                dbs++;
            }
            foreach (CookwareLiquidVisual lv in Object.FindObjectsByType<CookwareLiquidVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(lv);
                SerializedProperty arr = so.FindProperty("_products");
                if (arr == null) continue;
                bool changed = false;
                for (int i = arr.arraySize - 1; i >= 0; i--)
                    if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("Product").objectReferenceValue == null) { arr.DeleteArrayElementAtIndex(i); changed = true; }
                if (changed) { so.ApplyModifiedProperties(); lvs++; }
            }
            for (int i = 0; i < EditorSceneManager.sceneCount; i++) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetSceneAt(i));
            AssetDatabase.SaveAssets();
            log.AppendLine($"\nУдалено: {ok}. Food Database пересобрана: {dbs}. LiquidAnchor почищено: {lvs}.");
            log.AppendLine("Сохрани сцену. Префабы тары с LiquidAnchor (не в сцене): пустые строки безвредны — или нажми «Тара: принимать ВСЁ» на них ещё раз.");
            Debug.Log("[Чистка продуктов]\n" + log);
            EditorUtility.DisplayDialog("Чистка продуктов", log.ToString(), "Ок");
            Scan();
        }
    }
}
#endif
