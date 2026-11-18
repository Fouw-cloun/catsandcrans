#if UNITY_EDITOR
using System;
using CatCooks.Food;
using UnityEditor;
using UnityEngine;
using W = CCWorkshopEditor;

public static class CCConcreteTruckRearSetup
{
    [MenuItem("CatCooks/Vehicle Workshop/03C Set up mixer levers and chute")]
    public static void InstallSelected()
    {
        var selected = W.Selected();
        var truck = selected.GetComponentInParent<CCConcreteTruckVehicle>();
        if (truck == null)
            throw new InvalidOperationException("Выдели корень бетоновоза, собранного через 03.");
        Setup(truck);
        W.Done(truck.gameObject, "03C: рычаги, заливная воронка и жёлоб подключены.");
    }

    internal static void Setup(CCConcreteTruckVehicle truck)
    {
        var root = truck.transform;
        var mix = Required(root, "lever");
        var discharge = Required(root, "lever.001");
        var chute = Required(root, "concrete slider");
        var drum = Required(root, "concrete container");
        Matrix4x4 frame = Calibration(root);
        // Coordinates in the supplied original FBX as measured in Blender.
        Vector3 inlet = frame.MultiplyPoint3x4(new Vector3(-.007f, -3.486f, 4.14f));
        Vector3 tip = frame.MultiplyPoint3x4(new Vector3(1.107f, -3.679f, 1.40f));
        float meters = frame.MultiplyVector(Vector3.forward).magnitude;
        if (meters < .001f) throw new InvalidOperationException("Некорректный масштаб модели бетоновоза.");

        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Configure mixer levers and chute");
        if (truck.chutePivot == null)
        {
            var instance = PrefabUtility.GetOutermostPrefabInstanceRoot(chute.gameObject);
            if (instance != null)
            {
                if (!instance.transform.IsChildOf(root))
                    throw new InvalidOperationException("Открой отдельный экземпляр бетоновоза для настройки жёлоба.");
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.UserAction);
            }

            Vector3 rear = Vector3.ProjectOnPlane(inlet - Required(root, "SEAT1").position, root.up);
            // The exact length direction comes from the source coordinate frame.
            Vector3 sourceRear = Vector3.ProjectOnPlane(-frame.MultiplyVector(Vector3.up), root.up);
            if (sourceRear.sqrMagnitude > .000001f) rear = sourceRear;
            rear.Normalize();
            Vector3 exitDirection = (tip - chute.position).normalized;
            Transform swivel = W.Child(root, "ChuteSwivel", root.InverseTransformPoint(chute.position));
            swivel.rotation = Quaternion.LookRotation(rear, root.up);
            Undo.SetTransformParent(chute, swivel, "Mount chute on vertical swivel");
            var lip = W.Child(chute, "ChuteOutlet", chute.InverseTransformPoint(tip));
            lip.rotation = Quaternion.LookRotation(exitDirection, root.up);
            float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(exitDirection, root.up), rear, root.up);
            swivel.rotation = Quaternion.AngleAxis(yaw, root.up) * swivel.rotation;
            truck.chutePivot = swivel;
            truck.chuteOutlet = lip;
        }

        truck.mixLever = mix;
        truck.dischargeLever = discharge;
        ConfigurePart(truck, mix, CCConcreteTruckRearPart.Action.Mix, meters);
        ConfigurePart(truck, discharge, CCConcreteTruckRearPart.Action.Discharge, meters);
        ConfigurePart(truck, chute, CCConcreteTruckRearPart.Action.AimChute, meters);

        if (truck.cookware == null)
        {
            var existing = W.Find(root, "DrumContents");
            var cookware = existing != null ? existing.GetComponent<Cookware>() : null;
            if (cookware == null)
            {
                Bounds b = W.BoundsIn(root, new[] { drum.GetComponent<Renderer>() });
                var storage = W.Child(root, "DrumContents", new Vector3(b.center.x, b.min.y + b.size.y * .13f, b.center.z));
                cookware = W.Container(storage.gameObject, "ConcreteDrum", Vector3.zero, root.rotation,
                    new Vector2(b.extents.x * .63f, b.extents.z * .60f), b.size.y * .43f, 320);
            }
            truck.cookware = cookware;
        }
        PlaceInlet(truck.cookware, root, inlet, meters);

        var oldSpill = truck.cookware.GetComponent<CookwareSpill>();
        if (oldSpill != null)
        {
            if (truck.puddlePrefab == null) truck.puddlePrefab = oldSpill.PuddlePrefab;
            Undo.RecordObject(oldSpill, "Drain through rear chute only");
            oldSpill.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(oldSpill);
        }
        if (truck.puddlePrefab == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Scripts/NewCookingSystem/MilkPuddle.prefab");
            if (prefab != null) truck.puddlePrefab = prefab.GetComponent<LiquidPuddle>();
        }

        var stream = W.Add<LiquidPourSource>(root.gameObject);
        W.Set(stream, "_mouth", truck.chuteOutlet);
        W.Set(stream, "_pourByTilt", false);
        W.Set(stream, "_forcePour", false);
        W.Set(stream, "_speed", truck.flowSpeed);
        W.Set(stream, "_width", .055f);
        truck.stream = stream;
        W.Add<CCVehicleHelp>(root.gameObject).text =
            "WASD: ехать • Space: тормоз • F: выйти • H: подсказка\n" +
            "Снаружи нажми F на рычаге смешивания, слива или на жёлобе.";
        PrefabUtility.RecordPrefabInstancePropertyModifications(truck);
        EditorUtility.SetDirty(truck);
    }

    private static void PlaceInlet(Cookware cookware, Transform root, Vector3 position, float meters)
    {
        Transform inlet = W.Child(cookware.transform, "HopperIntake", cookware.transform.InverseTransformPoint(position));
        Undo.RecordObject(inlet, "Align inlet to original hopper");
        inlet.rotation = root.rotation;
        var box = W.Add<BoxCollider>(inlet.gameObject);
        Undo.RecordObject(box, "Define hopper opening");
        box.isTrigger = true;
        box.enabled = true;
        box.size = SizeForWorld(inlet, new Vector3(.82f, .48f, .79f) * meters);
        int layer = LayerMask.NameToLayer("Cookware");
        if (layer >= 0) inlet.gameObject.layer = layer;
        var previous = cookware.InsideVolume;
        if (previous != null && previous != box)
        {
            Undo.RecordObject(previous, "Use hopper inlet");
            previous.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(previous);
        }
        var relay = W.Add<CookwareTriggerRelay>(inlet.gameObject);
        W.Set(relay, "_cookware", cookware);
        var mouth = W.Child(inlet, "HopperMouth", inlet.InverseTransformPoint(position + root.up * (.12f * meters)));
        W.Set(cookware, "_insideVolume", box);
        W.Set(cookware, "_mouth", mouth);
    }

    private static void ConfigurePart(CCConcreteTruckVehicle truck, Transform mesh, CCConcreteTruckRearPart.Action action, float meters)
    {
        var filter = mesh.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
            throw new InvalidOperationException("Нет меша детали " + mesh.name);
        Bounds b = filter.sharedMesh.bounds;
        var hit = W.Child(mesh, "WorkshopUse", b.center);
        var part = W.Add<CCConcreteTruckRearPart>(hit.gameObject);
        Undo.RecordObject(part, "Link concrete truck part");
        part.truck = truck;
        part.action = action;
        var box = W.Add<BoxCollider>(hit.gameObject);
        Undo.RecordObject(box, "Configure lever target");
        box.isTrigger = true;
        box.size = b.size + SizeForWorld(hit, Vector3.one * (.028f * meters));
        int layer = LayerMask.NameToLayer("Interactable");
        if (layer >= 0) hit.gameObject.layer = layer;
    }

    private static Vector3 SizeForWorld(Transform node, Vector3 size)
    {
        Vector3 scale = node.lossyScale;
        return new Vector3(size.x / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
            size.y / Mathf.Max(.0001f, Mathf.Abs(scale.y)), size.z / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
    }

    private static Matrix4x4 Calibration(Transform root)
    {
        Matrix4x4 source = Frame(new Vector3(-.7290567f, 2.6086276f, 2.2660396f),
            new Vector3(.7289636f, 2.6086276f, 2.2660396f),
            new Vector3(.6364176f, 1.2802802f, 1.7644185f),
            new Vector3(0, 1.5098813f, 1.4591768f));
        Matrix4x4 target = Frame(Required(root, "SEAT1").position, Required(root, "SEAT1.001").position,
            Required(root, "pult").position, Required(root, "base").position);
        if (Mathf.Abs(target.determinant) < 1e-12f)
            throw new InvalidOperationException("Не удалось совместить контрольные точки Concrete truck.fbx.");
        return target * source.inverse;
    }

    private static Matrix4x4 Frame(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Vector3 x = b - a, y = c - a, z = d - a;
        return new Matrix4x4(new Vector4(x.x, x.y, x.z, 0), new Vector4(y.x, y.y, y.z, 0),
            new Vector4(z.x, z.y, z.z, 0), new Vector4(a.x, a.y, a.z, 1));
    }

    private static Transform Required(Transform root, string name)
    {
        var part = W.Find(root, name);
        if (part == null) throw new InvalidOperationException("В бетоновозе нет детали " + name);
        return part;
    }
}
#endif
