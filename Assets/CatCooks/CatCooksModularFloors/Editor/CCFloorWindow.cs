using System;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Floors.Editor
{
    public sealed class CCFloorWindow : EditorWindow
    {
        CCModularFloor floor;
        CCFloorLayout layout;
        int variant, level;
        CCFloorState brush = CCFloorState.Built;
        Vector2 scroll;
        string message;
        bool failed;

        [MenuItem("Tools/CatCooks/Modular Floors")]
        public static void Open() => GetWindow<CCFloorWindow>("CatCooks Floors");
        void OnEnable()
        {
            minSize = new Vector2(430, 580);
            if (Selection.activeGameObject) floor = Selection.activeGameObject.GetComponentInParent<CCModularFloor>();
            if (floor) layout = floor.Layout;
        }
        void Run(Action action)
        {
            try { action(); message = "Готово. Сохрани сцену (Ctrl+S)."; failed = false; }
            catch (Exception e) { message = e.Message; failed = true; Debug.LogException(e); }
            Repaint();
        }
        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Модульные этажи • без котов", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Сетка Layout; шаг X = Cell Size, Z = Cell Depth (0 = как X). Верх плиты уровня 0 совпадает с Y корня. Выбор схемы сам по себе не пересобирает сцену.", MessageType.Info);
            variant = EditorGUILayout.Popup("Вариант", variant, CCFloorSetup.Titles);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Создать новый тестовый этаж")) Run(() => { floor = CCFloorSetup.CreateFloor(variant); layout = floor.Layout; });
                floor = (CCModularFloor)EditorGUILayout.ObjectField("Корень этажа", floor, typeof(CCModularFloor), true);
                if (GUILayout.Button("Назначить вариант выбранному корню")) Run(() =>
                {
                    if (!floor) throw new InvalidOperationException("Выбери корень этажа.");
                    CCFloorSetup.EnsureAssets(); Undo.RecordObject(floor, "Choose floor layout");
                    floor.Layout = layout = CCFloorSetup.Layout(variant); EditorUtility.SetDirty(floor);
                });
                using (new EditorGUI.DisabledScope(!floor))
                {
                    if (GUILayout.Button("ПЕРЕСОБРАТЬ ВЫБРАННЫЙ ЭТАЖ", GUILayout.Height(32))) Run(() => CCFloorBuilder.Rebuild(floor));
                    if (GUILayout.Button("Проверить схему без пересборки")) Run(() =>
                    {
                        var errors = CCFloorPlanner.Validate(floor);
                        if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
                    });
                    if (GUILayout.Button("Выбрать корень в сцене") && floor) Selection.activeGameObject = floor.gameObject;
                }
                EditorGUILayout.Space();
                layout = (CCFloorLayout)EditorGUILayout.ObjectField("Редактировать Layout", layout, typeof(CCFloorLayout), false);
                if (layout)
                {
                    EditorGUILayout.HelpBox(layout.Description ?? "", MessageType.None);
                    if (floor && floor.Layout != layout) EditorGUILayout.HelpBox("Редактируемый Layout отличается от Layout корня. Для применения назначь этот asset в компонент CCModularFloor.", MessageType.Warning);
                    if (GUILayout.Button("Создать свою копию Layout и назначить корню")) Run(() =>
                    {
                        string source = AssetDatabase.GetAssetPath(layout);
                        string dest = AssetDatabase.GenerateUniqueAssetPath(CCFloorSetup.Presets + "/CustomLayout.asset");
                        if (!AssetDatabase.CopyAsset(source, dest)) throw new InvalidOperationException("Не удалось скопировать Layout.");
                        layout = AssetDatabase.LoadAssetAtPath<CCFloorLayout>(dest);
                        if (floor) { Undo.RecordObject(floor, "Assign custom layout"); floor.Layout = layout; EditorUtility.SetDirty(floor); }
                    });
                    level = EditorGUILayout.IntSlider("Уровень настила", level, 0, Mathf.Max(0, layout.DeckLevels - 1));
                    brush = (CCFloorState)EditorGUILayout.EnumPopup("Кисть", brush);
                    EditorGUILayout.LabelField("Нажми клетку: зелёный — готово, серый — план, красный — разрушено, синий — резерв.", EditorStyles.wordWrappedLabel);
                    DrawGrid();
                    EditorGUILayout.LabelField("Лестницы, маркеры и размеры сетки редактируются в Inspector самого Layout. После изменений нажми «Пересобрать».", EditorStyles.wordWrappedLabel);
                    if (GUILayout.Button("Открыть Layout в Inspector")) Selection.activeObject = layout;
                }
            }
            if (floor && !string.IsNullOrEmpty(floor.LastBuildSummary)) EditorGUILayout.HelpBox(floor.LastBuildSummary, MessageType.None);
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, failed ? MessageType.Error : MessageType.Info);
            EditorGUILayout.EndScrollView();
        }
        void DrawGrid()
        {
            if (layout.Width < 1 || layout.Depth < 1 || layout.Width > 16 || layout.Depth > 16) return;
            float size = Mathf.Min(36, (position.width - 65) / layout.Width);
            EditorGUILayout.LabelField("↑ +Z / дальняя сторона", EditorStyles.miniLabel);
            for (int z = layout.Depth - 1; z >= 0; z--)
            {
                EditorGUILayout.BeginHorizontal(); GUILayout.Label(z.ToString(), GUILayout.Width(20));
                for (int x = 0; x < layout.Width; x++)
                {
                    Color previous = GUI.backgroundColor;
                    var state = layout.Get(x, z, level);
                    GUI.backgroundColor = state == CCFloorState.Built ? new Color(.4f, .8f, .52f) : state == CCFloorState.Missing ? new Color(1, .4f, .35f) : state == CCFloorState.Reserved ? new Color(.35f, .55f, .9f) : Color.gray;
                    string label = "";
                    foreach (var s in layout.Stairs)
                        if (s.X == x && s.FromLevel == level && z >= s.Z && z < s.Z + s.Length) label = "↑";
                    if (level == 0 && layout.PlayerCell == new Vector2Int(x, z)) label = "P";
                    if (level == 0 && layout.LiftCell == new Vector2Int(x, z)) label = "L";
                    if (GUILayout.Button(label, GUILayout.Width(size), GUILayout.Height(size)))
                    {
                        Undo.RecordObject(layout, "Paint floor cell"); layout.Set(x, z, level, brush); EditorUtility.SetDirty(layout);
                    }
                    GUI.backgroundColor = previous;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.LabelField("X: 0 … " + (layout.Width - 1) + " →   P — игрок, L — место лифта, ↑ — лестница", EditorStyles.miniLabel);
        }
    }

    [CustomEditor(typeof(CCModularFloor))]
    public sealed class CCModularFloorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var f = (CCModularFloor)target;
            EditorGUILayout.HelpBox("Изменения применяются кнопкой. Generated_Floor пересоздаётся; свои объекты ставь в UserContent. Ctrl+Z отменяет пересборку.", MessageType.Info);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Пересобрать этаж"))
                    try { CCFloorBuilder.Rebuild(f); } catch (Exception e) { Debug.LogException(e, f); }
                if (GUILayout.Button("Открыть редактор пяти схем")) CCFloorWindow.Open();
            }
            if (!string.IsNullOrEmpty(f.LastBuildSummary)) EditorGUILayout.HelpBox(f.LastBuildSummary, MessageType.None);
        }
        void OnSceneGUI()
        {
            var f = (CCModularFloor)target;
            if (!f.Layout) return;
            using (new Handles.DrawingScope(f.transform.localToWorldMatrix))
            {
                if (f.ShowPlanned)
                    foreach (var p in f.Pieces)
                    {
                        if (p.State == CCFloorState.Built || p.Kind == CCFloorKind.Wall || p.Kind == CCFloorKind.Window) continue;
                        Handles.color = p.State == CCFloorState.Missing ? new Color(1, .3f, .2f, .8f) : new Color(.25f, .8f, 1, .22f);
                        using (new Handles.DrawingScope(f.transform.localToWorldMatrix * Matrix4x4.TRS(p.Position, p.Rotation, Vector3.one)))
                            Handles.DrawWireCube(Vector3.zero, p.Size);
                    }
                if (f.ShowLabels && f.GeneratedRoot)
                {
                    var markers = f.GeneratedRoot.transform.Find("ReferenceMarkers");
                    if (markers) foreach (Transform marker in markers)
                        Handles.Label(f.transform.InverseTransformPoint(marker.position) + Vector3.up * .4f, marker.name);
                }
            }
        }
    }
}
