using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CCMobileCraneWinchSetup
{
    private const string Folder = "Assets/VehicleWorkshop/Models/";
    private const string ModelPath = Folder + "WorkshopMobileHook.fbx";
    private const string Menu = "CatCooks/Vehicle Workshop/09C Установить крюк и лебёдку";
    private const string Hint = "R — поднять крюк • G — опустить";

    private static Transform Part(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        throw new InvalidOperationException("В модели отсутствует деталь «" + name + "».");
    }

    private static Material MaterialAsset(string id, Color color, float metallic)
    {
        string path = Folder + "Winch_" + id + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (!shader) throw new InvalidOperationException("Не найден шейдер для материалов лебёдки.");
        material = new Material(shader);
        material.color = color;
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .36f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Transform Child(Transform parent, string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, name);
        Undo.SetTransformParent(go.transform, parent, name);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    [MenuItem(Menu)]
    private static void Install()
    {
        var selected = Selection.activeTransform;
        var chassis = selected ? selected.GetComponentInParent<CCWorkshopCraneChassis>(true) : null;
        if (EditorApplication.isPlayingOrWillChangePlaymode || !chassis || EditorUtility.IsPersistent(chassis))
            throw new InvalidOperationException("Вне Play Mode выбери собранный Автокран Workshop в сцене.");
        Transform root = chassis.transform;
        var seat = chassis.GetComponent<CraneOperatorSeat>();
        if (!seat) throw new InvalidOperationException("У крана отсутствует CraneOperatorSeat.");
        if (chassis.GetComponent<CCMobileCraneWinch>())
        {
            Debug.Log("На этом автокране уже установлена лебёдка 09C.", chassis);
            return;
        }
        var seatData = new SerializedObject(seat);
        foreach (string field in new[] { "_hookJoint", "_rope", "_hookTransform" })
            if (seatData.FindProperty(field).objectReferenceValue)
                throw new InvalidOperationException("На этом экземпляре уже назначена другая подвеска крюка.");
        var tower = Part(root, "Поворотная платформа");
        var upper = Part(Part(tower, "Телескоп 2"), "crane block");
        var upperMesh = upper.GetComponent<MeshFilter>();
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (!asset || !upperMesh || !upperMesh.sharedMesh)
            throw new InvalidOperationException("Проверь импорт WorkshopMobileHook.fbx и меш верхнего блока.");
        var hookSteel = MaterialAsset("Steel", new Color(.29f, .31f, .32f), .45f);
        var blockPaint = MaterialAsset("Ochre", new Color(.9f, .52f, .09f), .15f);
        var cableSteel = MaterialAsset("Cable", new Color(.14f, .15f, .16f), .14f);

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Установить крюк и лебёдку");
        try
        {
            var winch = Undo.AddComponent<CCMobileCraneWinch>(root.gameObject);
            winch.upperBlock = Child(upper, "Выход троса");
            winch.upperBlock.localPosition = upperMesh.sharedMesh.bounds.center;
            winch.tower = tower;
            winch.hook = Child(root, "Подвес крюка");
            winch.hook.rotation = Quaternion.identity;
            var model = ((GameObject)PrefabUtility.InstantiatePrefab(asset, root.gameObject.scene)).transform;
            Undo.RegisterCreatedObjectUndo(model.gameObject, "Модель крюка");
            Undo.SetTransformParent(model, winch.hook, "Модель крюка");
            model.localPosition = Vector3.zero;
            model.localScale = Vector3.one;
            model.localRotation = Quaternion.identity;
            var eye = Part(model, "HangEye");
            Vector3 up = Part(model, "HangUp").position - eye.position;
            Vector3 forward = Part(model, "HangForward").position - eye.position;
            if (up.magnitude < .0001f || forward.magnitude < .0001f)
                throw new InvalidOperationException("Повреждены осевые метки крюка.");
            model.localScale /= up.magnitude;
            model.rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * model.rotation;
            model.position += winch.hook.position - eye.position;
            winch.eyeToBottom = winch.hook.position.y - Part(model, "HookBottom").position.y;
            if (Mathf.Abs(winch.eyeToBottom - .948f) > .02f)
                throw new InvalidOperationException("Не удалось восстановить исходный масштаб подвески.");
            winch.sheave = Part(model, "rope block");
            winch.sheaveAxis = winch.sheave.InverseTransformDirection(winch.hook.right).normalized;
            winch.sheaveRadius = Vector3.Distance(eye.position, winch.sheave.position);
            winch.restingSheaveRotation = winch.sheave.localRotation;
            PrefabUtility.RecordPrefabInstancePropertyModifications(model);
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                Undo.RecordObject(renderer, "Материал крюка");
                renderer.sharedMaterial = renderer.name == "hook base" ? blockPaint :
                    renderer.name == "rope block" ? cableSteel : hookSteel;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            winch.cable = Undo.AddComponent<LineRenderer>(Child(root, "Трос лебёдки").gameObject);
            winch.cable.useWorldSpace = false;
            winch.cable.positionCount = 2;
            winch.cable.startWidth = winch.cable.endWidth = .028f;
            winch.cable.sharedMaterial = cableSteel;
            winch.cable.textureMode = LineTextureMode.Stretch;
            winch.cable.numCapVertices = 2;
            seatData.FindProperty("_workshopWinch").objectReferenceValue = winch;
            seatData.FindProperty("_hookLimit").vector2Value = new Vector2(.3f, 18f);
            seatData.FindProperty("_hookSpeed").floatValue = 1.7f;
            seatData.ApplyModifiedProperties();
            var help = root.GetComponent<CCVehicleHelp>();
            if (help && !(help.text ?? "").Contains(Hint))
            {
                Undo.RecordObject(help, "Подсказка лебёдки");
                help.text += "\n" + Hint;
                PrefabUtility.RecordPrefabInstancePropertyModifications(help);
            }
            Physics.SyncTransforms();
            winch.Draw(2f);
            EditorUtility.SetDirty(winch);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Undo.CollapseUndoOperations(group);
            Debug.Log("Лебёдка установлена. Сохрани сцену.", root);
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            throw;
        }
    }
}
