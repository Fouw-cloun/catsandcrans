using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCooks.Level
{
    /// <summary>
    /// Инспектор генератора сетки: поля по форме, карта клеток (для «Своей» — рисовать мышью), кнопки.
    /// Меню CatCooks → Сетка плиток → Создать генератор: объект в сцене (перед камерой сцены), выделенные в Project
    /// модели/префабы — сразу в варианты. Префабы меню не создаёт — сохраняй сам, когда понравится.
    /// </summary>
    [CustomEditor(typeof(TileGridGenerator))]
    public sealed class TileGridGeneratorEditor : Editor
    {
        private bool _painting, _paintValue;
        private bool _auto = true;
        private const string AutoKey = "CatCooks.TileGrid.Auto";

        private void OnEnable() { _auto = EditorPrefs.GetBool(AutoKey, true); }

        public override void OnInspectorGUI()
        {
            TileGridGenerator g = (TileGridGenerator)target;
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Variants"), new GUIContent("Модели (варианты)"), true);
            Prop("AutoSize", "Размер по модели");
            if (!g.AutoSize) Prop("ManualSize", "Размер клетки, м");
            Prop("Gap", "Зазор, м");

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Форма", EditorStyles.boldLabel);
            Prop("Shape", "Форма");
            Prop("Width", "Клеток по X");
            Prop("Depth", "Клеток по Z");
            TileGridShape sh = g.Shape;
            if (sh == TileGridShape.Ring || sh == TileGridShape.Frame) Prop("Thickness", "Толщина, клеток");
            if (sh == TileGridShape.Cross || sh == TileGridShape.LShape || sh == TileGridShape.UShape) Prop("Arm", "Ширина плеча, клеток");
            if (sh == TileGridShape.TextureMask)
            {
                Prop("MaskTexture", "Картинка-маска");
                Prop("MaskThreshold", "Порог");
            }
            Prop("Invert", "Наоборот (дырка)");

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Раскладка", EditorStyles.boldLabel);
            Prop("CenterPivot", "Центр в объекте");
            Prop("SnapToGround", "Низом на землю");
            Prop("RandomRotate", "Крутить на 90°");
            using (new EditorGUILayout.HorizontalScope())
            {
                Prop("Seed", "Узор (seed)");
                if (GUILayout.Button("Случ.", GUILayout.Width(48))) { serializedObject.FindProperty("Seed").intValue = Random.Range(1, 99999); }
            }
            Prop("MarkStatic", "Static");

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Объединение", EditorStyles.boldLabel);
            Prop("CombinedCollider", "MeshCollider");
            Prop("MeshFolder", "Папка мешей");
            Prop("Log", "Логи");
            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();

            // ── карта клеток ──
            EditorGUILayout.Space(8);
            Vector2 cs = g.CellSize();
            int on = g.CountCells();
            EditorGUILayout.LabelField($"Клетка {cs.x:0.###} × {cs.y:0.###} м · плиток {on} из {g.Width * g.Depth} · площадь {g.Width * cs.x:0.#} × {g.Depth * cs.y:0.#} м", EditorStyles.miniLabel);
            bool painted = DrawGrid(g);

            if (g.Shape == TileGridShape.Custom)
            {
                EditorGUILayout.HelpBox("Своя форма: ЛКМ по клеткам — рисовать / стирать (тяни мышью).", MessageType.None);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Залить")) { Undo.RecordObject(g, "Сетка"); g.MaskFill(true); painted = true; }
                    if (GUILayout.Button("Стереть")) { Undo.RecordObject(g, "Сетка"); g.MaskFill(false); painted = true; }
                    if (GUILayout.Button("Инвертировать")) { Undo.RecordObject(g, "Сетка"); g.MaskInvert(); painted = true; }
                }
            }
            else if (GUILayout.Button("Сделать своей (дорисовать мышью)"))
            {
                Undo.RecordObject(g, "Сетка"); g.BakeShapeToMask(); painted = true;
            }

            // ── кнопки ──
            EditorGUILayout.Space(8);
            bool autoNow = EditorGUILayout.ToggleLeft("Перестраивать сразу при изменениях", _auto);
            if (autoNow != _auto) { _auto = autoNow; EditorPrefs.SetBool(AutoKey, _auto); }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
                if (GUILayout.Button("Сгенерировать", GUILayout.Height(28))) Build(g);
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Очистить", GUILayout.Height(28))) { g.Clear(); Dirty(g); }
            }
            if (GUILayout.Button("Объединить в один меш (+ сохранить меш ассетом)")) { g.Combine(); Dirty(g); }
            if (g.Container() == null)
                EditorGUILayout.HelpBox("Жёлтые клетки в сцене — где будут плитки. Нажми «Сгенерировать».", MessageType.Info);

            if ((changed || painted))
            {
                EditorUtility.SetDirty(g);
                SceneView.RepaintAll();
                if (_auto && g.Container() != null && !HasCombined(g)) Build(g);
            }
        }

        private static bool HasCombined(TileGridGenerator g)
        {
            Transform c = g.Container();
            if (c == null) return false;
            for (int i = 0; i < c.childCount; i++) if (c.GetChild(i).name.StartsWith("Combined_")) return true;
            return false;
        }

        private static void Build(TileGridGenerator g)
        {
            g.Generate();
            Dirty(g);
        }

        private static void Dirty(TileGridGenerator g)
        {
            EditorUtility.SetDirty(g);
            if (g.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(g.gameObject.scene);
        }

        private void Prop(string field, string label)
        {
            SerializedProperty p = serializedObject.FindProperty(field);
            if (p != null) EditorGUILayout.PropertyField(p, new GUIContent(label, p.tooltip));
        }

        /// <summary>Карта: Z вверх (как вид сверху). Для «Своей» — рисование мышью. true — что-то нарисовали.</summary>
        private bool DrawGrid(TileGridGenerator g)
        {
            int w = g.Width, d = g.Depth;
            float avail = EditorGUIUtility.currentViewWidth - 40f;
            float cell = Mathf.Clamp(Mathf.Floor(avail / w), 3f, 18f);
            float maxH = 360f;
            if (cell * d > maxH) cell = Mathf.Max(3f, Mathf.Floor(maxH / d));
            Rect area = GUILayoutUtility.GetRect(cell * w, cell * d, GUILayout.ExpandWidth(false));
            area.x += Mathf.Max(0f, (avail - cell * w) * 0.5f);
            area.width = cell * w; area.height = cell * d;
            if (w * d > 40000) { EditorGUI.HelpBox(area, "слишком много клеток для карты", MessageType.Warning); return false; }

            bool custom = g.Shape == TileGridShape.Custom;
            Event e = Event.current;
            bool painted = false;
            if (custom && area.Contains(e.mousePosition) && e.button == 0 && (e.type == EventType.MouseDown || (e.type == EventType.MouseDrag && _painting)))
            {
                int x = Mathf.Clamp((int)((e.mousePosition.x - area.x) / cell), 0, w - 1);
                int z = Mathf.Clamp(d - 1 - (int)((e.mousePosition.y - area.y) / cell), 0, d - 1);
                if (e.type == EventType.MouseDown) { _painting = true; _paintValue = !g.MaskGet(x, z); Undo.RecordObject(g, "Сетка — рисование"); }
                if (g.MaskGet(x, z) != _paintValue) { g.MaskSet(x, z, _paintValue); painted = true; }
                e.Use();
            }
            if (e.type == EventType.MouseUp) _painting = false;

            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(area, new Color(0.12f, 0.12f, 0.12f));
                Color onC = custom ? new Color(1f, 0.78f, 0.25f) : new Color(0.95f, 0.75f, 0.3f);
                Color offC = new Color(0.22f, 0.22f, 0.22f);
                float pad = cell >= 6f ? 1f : 0f;
                for (int z = 0; z < d; z++)
                    for (int x = 0; x < w; x++)
                    {
                        Rect r = new Rect(area.x + x * cell + pad, area.y + (d - 1 - z) * cell + pad, cell - pad * 2f, cell - pad * 2f);
                        EditorGUI.DrawRect(r, g.CellOn(x, z) ? onC : offC);
                    }
            }
            return painted;
        }

        // ───────────────────────── МЕНЮ ─────────────────────────

        [MenuItem("CatCooks/Сетка плиток/Создать генератор (выдели модели в Project)", priority = 900)]
        public static void CreateGenerator()
        {
            GameObject go = new GameObject("TileGrid");
            Undo.RegisterCreatedObjectUndo(go, "Сетка плиток");
            SceneView sv = SceneView.lastActiveSceneView;
            if (sv != null && sv.camera != null)
            {
                Ray ray = new Ray(sv.camera.transform.position, sv.camera.transform.forward);
                RaycastHit hit;
                go.transform.position = Physics.Raycast(ray, out hit, 500f) ? hit.point : sv.pivot;
            }
            TileGridGenerator g = go.AddComponent<TileGridGenerator>();
            int added = 0;
            foreach (Object o in Selection.objects)
            {
                GameObject p = o as GameObject;
                if (p == null || !EditorUtility.IsPersistent(p)) continue;
                g.Variants.Add(new TileVariant { Prefab = p, Weight = 1f });
                added++;
            }
            Selection.activeGameObject = go;
            if (added > 0) g.Generate();
            if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log(added > 0
                ? $"[Сетка плиток] создан «{go.name}» с {added} моделями — меняй форму в инспекторе"
                : "[Сетка плиток] создан «TileGrid» — перетащи модель плитки в «Модели (варианты)» и нажми «Сгенерировать»", go);
        }

        [MenuItem("CatCooks/Сетка плиток/Добавить генератор на выделенный объект", priority = 901)]
        public static void AddToSelected()
        {
            GameObject go = Selection.activeGameObject;
            if (go == null || EditorUtility.IsPersistent(go)) { EditorUtility.DisplayDialog("Сетка плиток", "Выдели объект в сцене.", "Ок"); return; }
            if (go.GetComponent<TileGridGenerator>() == null) Undo.AddComponent<TileGridGenerator>(go);
        }
    }
}
