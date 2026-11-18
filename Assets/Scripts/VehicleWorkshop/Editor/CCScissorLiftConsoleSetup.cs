#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using W = CCWorkshopEditor;

public static class CCScissorLiftConsoleSetup
{
    [MenuItem("CatCooks/Vehicle Workshop/05A Set up standing lift controls")]
    public static void Install()
    {
        GameObject selected = W.Selected();
        var lift = selected.GetComponentInParent<CCScissorLiftVehicle>();
        if (lift == null)
            throw new InvalidOperationException("Выдели подъёмник из архива 05 в Hierarchy.");

        Undo.RegisterFullObjectHierarchyUndo(lift.gameObject, "Standing lift controls");
        Configure(lift);
        W.Done(lift.gameObject, "Подъёмник 05A настроен. Сохрани сцену.");
    }

    internal static void Configure(CCScissorLiftVehicle lift)
    {
        Transform root = lift.transform;
        Transform console = lift.platform != null ? W.Find(lift.platform, "ConsolePanel") : null;
        if (console == null)
            throw new InvalidOperationException("На площадке подъёмника отсутствует ConsolePanel.");

        int layer = LayerMask.NameToLayer("Interactable");
        if (layer < 0)
            throw new InvalidOperationException("В проекте отсутствует слой Interactable.");

        Transform oldSeat = W.Find(root, "WorkshopSeatHit");
        if (oldSeat != null && oldSeat.TryGetComponent(out BoxCollider oldHit))
            oldHit.enabled = false;

        Transform consoleUse = W.Child(console, "WorkshopConsoleUse", Vector3.zero);
        BoxCollider hit = SetupPart(consoleUse.gameObject, lift, layer, 0);
        hit.size = new Vector3(1.2f, 1.4f, 1.18f);
        W.Set(lift, "console", console);
        W.Set(lift, "_seatCollider", hit);

        var blue = RecoveryMaterial();
        SetupRecovery(root, lift, layer, "WorkshopRecoveryLeft", -.80f, 1, blue);
        SetupRecovery(root, lift, layer, "WorkshopRecoveryRight", .80f, 2, blue);
        var help = W.Add<CCVehicleHelp>(lift.gameObject);
        help.text = "F у пульта: взять управление / выйти • WASD: ехать • Space: тормоз\n" +
            "Q/E: поднять/опустить • R: поднять упавший подъёмник • снаружи F по синей кнопке • H: подсказка";
        PrefabUtility.RecordPrefabInstancePropertyModifications(help);
        PrefabUtility.RecordPrefabInstancePropertyModifications(lift);
        if (oldSeat != null && oldSeat.TryGetComponent(out BoxCollider seatCollider))
            PrefabUtility.RecordPrefabInstancePropertyModifications(seatCollider);
    }

    private static BoxCollider SetupPart(GameObject go, CCScissorLiftVehicle lift, int layer, int slot)
    {
        go.layer = layer;
        var part = W.Add<CCScissorLiftPanel>(go);
        W.Set(part, "lift", lift);
        W.Set(part, "slot", slot);
        W.Set(part, "recovery", slot != 0);
        var box = W.Add<BoxCollider>(go);
        box.enabled = true;
        box.isTrigger = true;
        return box;
    }

    private static void SetupRecovery(Transform root, CCScissorLiftVehicle lift, int layer,
        string name, float x, int slot, Material blue)
    {
        Transform button = root.Find(name);
        if (button == null)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, name);
            button = go.transform;
            button.SetParent(root, false);
        }
        button.localPosition = new Vector3(x, .38f, -.13f);
        button.localScale = new Vector3(.10f, .17f, .23f);
        button.GetComponent<Renderer>().sharedMaterial = blue;
        SetupPart(button.gameObject, lift, layer, slot).size = new Vector3(1.1f, 1.3f, 1.25f);
    }

    private static Material RecoveryMaterial()
    {
        Directory.CreateDirectory(W.Generated);
        AssetDatabase.Refresh();
        string path = W.Generated + "ScissorLift_RecoveryBlue.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        material = new Material(shader) { color = new Color(.045f, .48f, 1f) };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
#endif
