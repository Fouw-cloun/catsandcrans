using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCooks.Floors.Editor
{
    public sealed class CCSiteWindow : EditorWindow
    {
        CCConstructionSite site;
        int variant;
        string message;
        bool failed;
        Vector2 scroll;
        [MenuItem("Tools/CatCooks/Construction Site v2")]
        public static void Open() => GetWindow<CCSiteWindow>("Construction Site v2");
        void OnEnable()
        {
            minSize = new Vector2(430, 540);
            if (Selection.activeGameObject) site = Selection.activeGameObject.GetComponentInParent<CCConstructionSite>();
        }
        void Run(Action action)
        {
            try { action(); message = "Готово. Сохрани сцену."; failed = false; }
            catch (Exception e) { message = e.Message; failed = true; Debug.LogException(e); }
        }
        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Двор → первый этаж → фасад → рабочая площадка", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("По умолчанию 104 × 84 м, девять пустых этажей. Ширина, глубина и высоты регулируются отдельно. Изменения применяются только кнопкой пересборки.", MessageType.Info);
            variant = EditorGUILayout.Popup("Вариант верхнего этажа", variant, CCFloorSetup.Titles);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Создать новое здание 104 × 84 м", GUILayout.Height(30))) Run(() => site = Create(null, variant));
                if (GUILayout.Button("Обстроить выбранный CC_ModularFloor снизу")) Run(() =>
                {
                    var floor = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<CCModularFloor>() : null;
                    if (!floor) throw new InvalidOperationException("Выдели корень старого этажа CC_ModularFloor в Hierarchy.");
                    site = Create(floor, variant);
                });
                site = (CCConstructionSite)EditorGUILayout.ObjectField("Корень здания", site, typeof(CCConstructionSite), true);
                if (site)
                {
                    var so = new SerializedObject(site); so.Update();
                    EditorGUILayout.PropertyField(so.FindProperty("Width"), new GUIContent("Ширина X, м"));
                    EditorGUILayout.PropertyField(so.FindProperty("Depth"), new GUIContent("Глубина Z, м"));
                    EditorGUILayout.PropertyField(so.FindProperty("GroundFloorHeight"), new GUIContent("Высота первого этажа"));
                    EditorGUILayout.PropertyField(so.FindProperty("EmptyFloors"), new GUIContent("Пустых этажей"));
                    EditorGUILayout.PropertyField(so.FindProperty("EmptyFloorHeight"), new GUIContent("Высота пустого этажа"));
                    EditorGUILayout.PropertyField(so.FindProperty("YardDepth"), new GUIContent("Глубина двора"));
                    EditorGUILayout.PropertyField(so.FindProperty("TopStoreyHeight"), new GUIContent("Высота яруса надстройки"));
                    EditorGUILayout.PropertyField(so.FindProperty("TopFrameStoreys"), new GUIContent("Ярусов надстройки"));
                    so.ApplyModifiedProperties();
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("52 × 42")) Size(52, 42);
                    if (GUILayout.Button("78 × 63")) Size(78, 63);
                    if (GUILayout.Button("104 × 84")) Size(104, 84);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.HelpBox($"Поверхность рабочего этажа: Y +{site.WorkdeckHeight:0.##} м от корня. Верх каркаса: +{site.WorkdeckHeight + site.TopFrameStoreys * site.TopStoreyHeight:0.##} м.", MessageType.None);
                    if (GUILayout.Button("Назначить выбранную схему верха")) Run(() =>
                    {
                        CCFloorSetup.EnsureAssets(); Undo.RecordObject(site, "Select top layout");
                        site.TopLayout = CCFloorSetup.Layout(variant); EditorUtility.SetDirty(site);
                    });
                    if (GUILayout.Button("ПЕРЕСОБРАТЬ ЗДАНИЕ И ВЕРХНИЙ ЭТАЖ", GUILayout.Height(34))) Run(() => CCSiteBuilder.Rebuild(site));
                    if (GUILayout.Button("Выбрать корень: все настройки в Inspector")) Selection.activeGameObject = site.gameObject;
                    if (GUILayout.Button("Выделить рабочую площадку") && site.Workdeck)
                    { Selection.activeGameObject = site.Workdeck.gameObject; SceneView.lastActiveSceneView?.FrameSelected(); }
                    if (!string.IsNullOrEmpty(site.LastBuildSummary)) EditorGUILayout.HelpBox(site.LastBuildSummary, MessageType.Info);
                }
            }
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, failed ? MessageType.Error : MessageType.Info);
            EditorGUILayout.EndScrollView();
        }
        void Size(float width, float depth)
        { Undo.RecordObject(site, "Set building footprint"); site.Width = width; site.Depth = depth; EditorUtility.SetDirty(site); }

        public static CCConstructionSite Create(CCModularFloor existing, int variant)
        {
            if (Application.isPlaying || PrefabStageUtility.GetCurrentPrefabStage() != null) throw new InvalidOperationException("Выйди из Play / Prefab Mode.");
            if (existing && (PrefabUtility.IsPartOfPrefabInstance(existing) || EditorUtility.IsPersistent(existing) || existing.GetComponentInParent<CCConstructionSite>()))
                throw new InvalidOperationException("Нужен отдельный этаж сцены, ещё не входящий в CCConstructionSite.");
            if (existing && (!existing.Layout || (existing.transform.lossyScale - Vector3.one).sqrMagnitude > .0001f || Vector3.Dot(existing.transform.up, Vector3.up) < .9999f))
                throw new InvalidOperationException("У этажа нужен Layout, Scale 1 и только поворот по Y.");
            CCFloorSetup.EnsureAssets();
            var root = new GameObject("CC_ConstructionSite");
            var s = root.AddComponent<CCConstructionSite>();
            var content = new GameObject("GroundUserContent"); content.transform.SetParent(root.transform, false); s.GroundUserContent = content.transform;
            Undo.RegisterCreatedObjectUndo(root, "Create construction site root");
            if (existing)
            {
                if (!existing.Layout) throw new InvalidOperationException("У выбранного этажа не назначен Layout.");
                s.Width = existing.CellSize * existing.Layout.Width; s.Depth = existing.StepZ * existing.Layout.Depth;
                root.transform.position = existing.transform.position - Vector3.up * s.WorkdeckHeight;
                root.transform.rotation = existing.transform.rotation;
                root.layer = existing.gameObject.layer;
                Undo.SetTransformParent(existing.transform, root.transform, "Move floor into construction site");
                s.Workdeck = existing; s.TopLayout = existing.Layout;
            }
            else
            {
                root.transform.position = Selection.activeTransform ? Selection.activeTransform.position : Vector3.zero;
                var go = new GameObject("Workdeck"); go.transform.SetParent(root.transform, false);
                s.Workdeck = go.AddComponent<CCModularFloor>(); s.TopLayout = CCFloorSetup.Layout(variant);
                new GameObject("UserContent").transform.SetParent(go.transform, false);
                Undo.RegisterCreatedObjectUndo(go, "Create upper deck");
            }
            EditorUtility.SetDirty(s);
            CCSiteBuilder.Rebuild(s); Selection.activeGameObject = root; return s;
        }
    }

    [CustomEditor(typeof(CCConstructionSite))]
    public sealed class CCConstructionSiteEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var site = (CCConstructionSite)target;
            EditorGUILayout.HelpBox($"Верхний рабочий пол: {site.WorkdeckHeight:0.##} м над землёй. Root Y — уровень двора. Пользовательские объекты ставь в GroundUserContent или Workdeck/UserContent.", MessageType.Info);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
                if (GUILayout.Button("Пересобрать всё здание"))
                    try { CCSiteBuilder.Rebuild(site); } catch (Exception e) { Debug.LogException(e, site); }
            if (!string.IsNullOrEmpty(site.LastBuildSummary)) EditorGUILayout.HelpBox(site.LastBuildSummary, MessageType.None);
        }
        void OnSceneGUI()
        {
            var site = (CCConstructionSite)target;
            using (new Handles.DrawingScope(site.transform.localToWorldMatrix))
                foreach (var socket in site.Sockets) Handles.Label(socket.LocalPosition + Vector3.up * .4f, socket.Id);
        }
    }
}
