using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// МОДУЛЬ БЛИЖНЕГО СЛОЯ — собрать и рандомизировать каждый модуль слоя 1 отдельно, выбрать ключевые здания,
    /// заменить любой дом. Модуль выбирается кликом по его дому в сцене, по мини-карте или Ctrl+ЛКМ на карте застройки.
    ///   • «Случайно» — новый вариант базы (и случайные ключевые, если свои не выбраны);
    ///   • «Собрать» — тот же вариант с текущими ключевыми;
    ///   • Ключевые здания — галки (небоскрёбы, крупные / upper middle, уникальные и отмеченные «Ключевой»);
    ///   • Выделенный дом — заменить: ◀ ▶ по своей группе, из списка, случайный; повернуть на 90°.
    /// Меню: CatCooks → Застройка → Модуль ближнего слоя.
    /// </summary>
    public sealed class CityModuleWindow : EditorWindow
    {
        private CityBlockGenerator _g;
        private int _x = -1, _z = -1;
        private Vector2 _scroll;
        private int _swapGroup;          // 0 — своя группа, 1 — ключевые, 2 — база, 3 — все
        private int _swapIndex;

        [MenuItem("CatCooks/Застройка/Модуль ближнего слоя (окно)", priority = 939)]
        public static void OpenMenu() { Open(null, -1, -1); }

        public static void Open(CityBlockGenerator g, int x, int z)
        {
            CityModuleWindow w = GetWindow<CityModuleWindow>("Модуль ближнего слоя");
            w.minSize = new Vector2(330, 420);
            if (g != null) w._g = g;
            if (x >= 0) { w._x = x; w._z = z; }
            w.Show();
            w.Repaint();
        }

        private void OnSelectionChange()
        {
            if (Selection.activeGameObject != null)
            {
                int x, z;
                if (CityBlockGenerator.FindModule(Selection.activeGameObject.transform, out x, out z)) { _x = x; _z = z; }
            }
            Repaint();
        }

        private void OnGUI()
        {
            if (_g == null) _g = Object.FindFirstObjectByType<CityBlockGenerator>();
            if (_g == null) { EditorGUILayout.HelpBox("В сцене нет City Block Generator.", MessageType.Warning); return; }
            if (!_g.ManualLayer1)
                EditorGUILayout.HelpBox("На генераторе выключено «Ближний слой вручную» — «Застроить всё» перестроит и эти модули.", MessageType.Info);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            MiniMap();
            if (_x < 0 || _g.GetRole(_x, _z) != CityRole.Ring1)
            {
                EditorGUILayout.HelpBox("Выбери модуль слоя 1: кликни по любому его дому в сцене или по жёлтой клетке на мини-карте.", MessageType.None);
                BulkButtons();
                EditorGUILayout.EndScrollView();
                return;
            }

            bool exists = _g.ModuleExists(_x, _z);
            EditorGUILayout.LabelField($"Модуль {_x},{_z} — " + (exists ? "собран" : "пустой"), EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(1f, 0.9f, 0.5f);
                if (GUILayout.Button("Случайно", GUILayout.Height(30)))
                {
                    Undo.RecordObject(_g, "Модуль — случайно");
                    _g.RandomizeModule(_x, _z); Dirty(); SelectModule();
                }
                GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
                if (GUILayout.Button("Собрать", GUILayout.Height(30)))
                {
                    Undo.RecordObject(_g, "Модуль — собрать");
                    _g.GenerateModule(_x, _z, true); Dirty(); SelectModule();
                }
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Очистить", GUILayout.Height(30))) { _g.ClearModule(_x, _z); Dirty(); }
            }
            CityProps cp = _g.GetComponent<CityProps>();
            if (cp != null && cp.enabled && exists)
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Пропсы модуля заново")) { cp.GenerateModule(_x, _z); Dirty(); }
                    if (GUILayout.Button("Убрать пропсы модуля")) { cp.ClearModule(_x, _z); Dirty(); }
                }

            KeysUI();
            SwapUI();
            EditorGUILayout.Space(8);
            BulkButtons();
            EditorGUILayout.EndScrollView();
        }

        // ── мини-карта: жёлтые — слой 1 (тёмные — пустые), рамка — выбранный ──
        private void MiniMap()
        {
            int w = _g.Width, d = _g.Depth;
            float cell = Mathf.Clamp(Mathf.Floor((position.width - 40f) / w), 10f, 26f);
            Rect area = GUILayoutUtility.GetRect(cell * w, cell * d, GUILayout.ExpandWidth(false));
            area.x += Mathf.Max(0f, (position.width - 20f - cell * w) * 0.5f);
            area.width = cell * w; area.height = cell * d;
            Event e = Event.current;
            if (e.type == EventType.MouseDown && area.Contains(e.mousePosition))
            {
                int x = Mathf.Clamp((int)((e.mousePosition.x - area.x) / cell), 0, w - 1);
                int z = Mathf.Clamp(d - 1 - (int)((e.mousePosition.y - area.y) / cell), 0, d - 1);
                if (_g.GetRole(x, z) == CityRole.Ring1) { _x = x; _z = z; SelectModule(); }
                e.Use();
            }
            if (e.type != EventType.Repaint) return;
            EditorGUI.DrawRect(area, new Color(0.1f, 0.1f, 0.1f));
            for (int z = 0; z < d; z++)
                for (int x = 0; x < w; x++)
                {
                    Rect r = new Rect(area.x + x * cell + 1, area.y + (d - 1 - z) * cell + 1, cell - 2, cell - 2);
                    CityRole role = _g.GetRole(x, z);
                    Color c = CityBlockGenerator.RoleColor(role);
                    if (role == CityRole.Ring1 && !_g.ModuleExists(x, z)) c *= 0.5f;
                    EditorGUI.DrawRect(r, c);
                    if (x == _x && z == _z)
                    {
                        EditorGUI.DrawRect(new Rect(r.x - 1, r.y - 1, r.width + 2, 2), Color.white);
                        EditorGUI.DrawRect(new Rect(r.x - 1, r.yMax - 1, r.width + 2, 2), Color.white);
                        EditorGUI.DrawRect(new Rect(r.x - 1, r.y - 1, 2, r.height + 2), Color.white);
                        EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y - 1, 2, r.height + 2), Color.white);
                    }
                }
        }

        // ── ключевые здания модуля ──
        private void KeysUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Ключевые здания модуля", EditorStyles.boldLabel);
            ModulePlan plan = _g.GetPlan(_x, _z, false);
            bool rnd = plan == null || plan.RandomKeys;
            int picked = plan != null && plan.Keys != null ? plan.Keys.Count : 0;
            EditorGUI.BeginChangeCheck();
            bool nrnd = EditorGUILayout.ToggleLeft("Если ничего не выбрано — случайные ключевые (по настройкам слоя)", rnd);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_g, "Модуль — ключевые"); _g.GetPlan(_x, _z, true).RandomKeys = nrnd; Dirty(); }
            EditorGUILayout.LabelField(picked > 0 ? $"Выбрано: {picked} — ставятся именно они" : (rnd ? "Ничего не выбрано — ключевые случайные" : "Ничего не выбрано — модуль без ключевых, только база"), EditorStyles.miniLabel);

            List<BuildingEntry> keys = new List<BuildingEntry>();
            foreach (BuildingEntry e in _g.Layer1.Buildings) if (_g.IsKeyEntry(e)) keys.Add(e);
            if (keys.Count == 0) EditorGUILayout.HelpBox("В слое 1 нет ключевых: небоскрёбов, крупных / upper middle или отмеченных «Роль = Ключевой».", MessageType.Info);
            foreach (BuildingEntry e in keys)
            {
                bool on = plan != null && plan.Keys != null && plan.Keys.Contains(e.Prefab);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    bool non = EditorGUILayout.ToggleLeft(e.Prefab.name + "  · " + KindName(e) + (e.Unique ? " · уникальный" : ""), on);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_g, "Модуль — ключевые");
                        ModulePlan p = _g.GetPlan(_x, _z, true);
                        if (non) p.Keys.Add(e.Prefab); else p.Keys.Remove(e.Prefab);
                        Dirty();
                    }
                    if (on && GUILayout.Button("+1", GUILayout.Width(30))) { Undo.RecordObject(_g, "Модуль — ключевые"); _g.GetPlan(_x, _z, true).Keys.Add(e.Prefab); Dirty(); }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (picked > 0 && GUILayout.Button("Снять все")) { Undo.RecordObject(_g, "Модуль — ключевые"); _g.GetPlan(_x, _z, true).Keys.Clear(); Dirty(); }
                GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
                if (GUILayout.Button("Собрать с этими ключевыми")) { Undo.RecordObject(_g, "Модуль — собрать"); _g.GenerateModule(_x, _z, true); Dirty(); SelectModule(); }
                GUI.backgroundColor = Color.white;
            }
        }

        // ── замена выделенного дома ──
        private void SwapUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Выделенный дом", EditorStyles.boldLabel);
            GameObject pivot = SelectedPivot();
            if (pivot == null) { EditorGUILayout.LabelField("Кликни по дому этого модуля в сцене.", EditorStyles.miniLabel); return; }
            string cur = pivot.name.Substring(2);
            BuildingEntry curE = null;
            foreach (BuildingEntry e in _g.Layer1.Buildings) if (e != null && e.Prefab != null && e.Prefab.name == cur) { curE = e; break; }
            bool curKey = curE != null && _g.IsKeyEntry(curE);
            EditorGUILayout.LabelField(cur + (curE != null ? "  · " + KindName(curE) + (curKey ? " · ключевой" : " · база") : ""), EditorStyles.miniBoldLabel);

            _swapGroup = GUILayout.Toolbar(_swapGroup, new[] { "Такие же", "Ключевые", "База", "Все" });
            List<BuildingEntry> list = new List<BuildingEntry>();
            foreach (BuildingEntry e in _g.Layer1.Buildings)
            {
                if (e == null || e.Prefab == null || e.Weight <= 0f) continue;
                bool k = _g.IsKeyEntry(e);
                if (_swapGroup == 0 && (k != curKey)) continue;
                if (_swapGroup == 1 && !k) continue;
                if (_swapGroup == 2 && k) continue;
                list.Add(e);
            }
            if (list.Count == 0) { EditorGUILayout.LabelField("В этой группе нет домов.", EditorStyles.miniLabel); return; }
            string[] names = new string[list.Count];
            int at = 0;
            for (int i = 0; i < list.Count; i++) { names[i] = list[i].Prefab.name + "  · " + KindName(list[i]); if (list[i] == curE) at = i; }
            _swapIndex = Mathf.Clamp(_swapIndex, 0, list.Count - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("◀", GUILayout.Width(30))) Swap(pivot, list[(at - 1 + list.Count) % list.Count].Prefab);
                if (GUILayout.Button("▶", GUILayout.Width(30))) Swap(pivot, list[(at + 1) % list.Count].Prefab);
                _swapIndex = EditorGUILayout.Popup(_swapIndex, names);
                if (GUILayout.Button("Заменить", GUILayout.Width(80))) Swap(pivot, list[_swapIndex].Prefab);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Случайный из группы")) Swap(pivot, list[Random.Range(0, list.Count)].Prefab);
                if (GUILayout.Button("Повернуть 90°", GUILayout.Width(110)))
                {
                    Undo.RecordObject(pivot.transform, "Повернуть дом");
                    pivot.transform.localRotation *= Quaternion.Euler(0f, 90f, 0f);
                    Dirty();
                }
                if (GUILayout.Button("Убрать", GUILayout.Width(70))) { Undo.DestroyObjectImmediate(pivot); Dirty(); }
            }
            EditorGUILayout.LabelField("Замена — до следующего «Случайно»/«Собрать» этого модуля.", EditorStyles.miniLabel);
        }

        private void Swap(GameObject pivot, GameObject prefab)
        {
            Undo.RecordObject(_g, "Заменить дом");
            GameObject np = _g.ReplaceBuilding(pivot, prefab);
            if (np != null) Selection.activeGameObject = np;
            Dirty();
        }

        private GameObject SelectedPivot()
        {
            GameObject s = Selection.activeGameObject;
            if (s == null) return null;
            int x, z;
            if (!CityBlockGenerator.FindModule(s.transform, out x, out z) || x != _x || z != _z) return null;
            for (Transform t = s.transform; t != null; t = t.parent)
                if (t.name.StartsWith("B_") && t.parent != null && t.parent.name.StartsWith("Module_")) return t.gameObject;
            return null;
        }

        private void BulkButtons()
        {
            EditorGUILayout.LabelField("Весь ближний слой", EditorStyles.boldLabel);
            CityProps cpb = _g.GetComponent<CityProps>();
            if (cpb != null && cpb.enabled)
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Пропсы: расставить везде")) { cpb.GenerateAll(); Dirty(); }
                    if (GUILayout.Button("Пропсы: убрать везде")) { cpb.ClearLayer1(); Dirty(); }
                }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Собрать пустые модули"))
                {
                    Undo.RecordObject(_g, "Ближний слой");
                    int n = 0;
                    for (int z = 0; z < _g.Depth; z++)
                        for (int x = 0; x < _g.Width; x++)
                            if (_g.GetRole(x, z) == CityRole.Ring1 && !_g.ModuleExists(x, z)) { _g.GenerateModule(x, z, false); n++; }
                    Debug.Log("[Застройка] ближний слой: собрано пустых модулей " + n);
                    Dirty();
                }
                if (GUILayout.Button("Все случайно заново") &&
                    EditorUtility.DisplayDialog("Ближний слой", "Перестроить ВСЕ модули слоя 1 случайно? Ручные замены домов пропадут (выбранные ключевые останутся).", "Да", "Нет"))
                {
                    Undo.RecordObject(_g, "Ближний слой");
                    for (int z = 0; z < _g.Depth; z++)
                        for (int x = 0; x < _g.Width; x++)
                            if (_g.GetRole(x, z) == CityRole.Ring1) _g.RandomizeModule(x, z);
                    Dirty();
                }
            }
        }

        private void SelectModule()
        {
            Transform c = _g.Container();
            Transform m = c != null ? c.Find(CityBlockGenerator.ModuleName(_x, _z)) : null;
            if (m != null) { Selection.activeGameObject = m.gameObject; EditorGUIUtility.PingObject(m.gameObject); }
            Repaint();
        }

        private string KindName(BuildingEntry e)
        {
            BuildingKind k = e.Kind;
            if (k == BuildingKind.Auto)
            {
                Bounds b;
                Vector2 ms = _g.ModuleSize();
                if (TileGridGenerator.MeasurePrefab(e.Prefab, out b)) k = CityBlockGenerator.AutoKind(b, Mathf.Min(ms.x, ms.y));
            }
            switch (k)
            {
                case BuildingKind.Small: return "мелкий";
                case BuildingKind.Mid: return "средний";
                case BuildingKind.Large: return "крупный";
                case BuildingKind.Tower: return "небоскрёб";
                case BuildingKind.Row: return "рядовой";
            }
            return "?";
        }

        private void Dirty()
        {
            EditorUtility.SetDirty(_g);
            // в Play сцену помечать нельзя (InvalidOperationException → ломался и весь GUI окна: «Invalid GUILayout state»)
            if (!EditorApplication.isPlaying && _g.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(_g.gameObject.scene);
            Repaint();
        }
    }
}
