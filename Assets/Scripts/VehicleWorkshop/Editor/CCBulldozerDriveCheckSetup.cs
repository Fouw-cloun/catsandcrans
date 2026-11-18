#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using W = CCWorkshopEditor;

public static class CCBulldozerDriveCheckSetup
{
    [MenuItem("CatCooks/Vehicle Workshop/02G Check and tune bulldozer drive")]
    public static void Apply()
    {
        var root = W.Selected();
        var drive = root.GetComponent<TrackedVehicle>();
        var body = root.GetComponent<Rigidbody>();
        if (drive == null || body == null || root.GetComponent<CCBulldozerBlade>() == null)
            throw new InvalidOperationException("Выдели корень настроенного бульдозера.");

        Undo.RecordObject(drive, "Bind bulldozer drive");
        W.Set(drive, "_body", body);
        W.Set(drive, "_isStatic", false);
        Undo.RecordObject(body, "Allow chassis translation");
        body.constraints &= ~RigidbodyConstraints.FreezePosition;
        drive.stableSuspension = true;

        // ItemAntiSlide is intended for items that rest on the world. In 02F
        // both suspension and traction are ray-driven, and hard colliders only
        // handle impacts or bottoming out. Keep their friction low.
        Directory.CreateDirectory(W.Generated);
        AssetDatabase.Refresh();
        const string materialPath = W.Generated + "BulldozerChassisSlide.physicMaterial";
        var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(materialPath);
        if (material == null)
        {
            material = new PhysicsMaterial("BulldozerChassisSlide")
            {
                dynamicFriction = .03f,
                staticFriction = .03f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0
            };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        foreach (var box in root.GetComponents<BoxCollider>())
        {
            if (box.isTrigger) continue;
            Undo.RecordObject(box, "Set friction for raycast-driven chassis");
            box.sharedMaterial = material;
            PrefabUtility.RecordPrefabInstancePropertyModifications(box);
        }

        // Learn the layer from the actual stationary surface directly under the
        // installed vehicle. Preserve masks that already hit that surface.
        Physics.SyncTransforms();
        int extra = UnlistedFloorLayers(drive, body, drive.leftTrackPoints)
            | UnlistedFloorLayers(drive, body, drive.rightTrackPoints);
        if (extra != 0) drive.groundMask = drive.groundMask.value | extra;

        var check = W.Add<CCBulldozerDriveCheck>(root);
        Undo.RecordObject(check, "Attach bulldozer drive HUD");
        check.drive = drive;
        PrefabUtility.RecordPrefabInstancePropertyModifications(check);
        PrefabUtility.RecordPrefabInstancePropertyModifications(drive);
        PrefabUtility.RecordPrefabInstancePropertyModifications(body);
        W.Done(root, $"02G: диагностика подключена. Дополнительные слои ближайшего пола: {extra}. Сядь в машину и проверь HUD с зажатой W.");
    }

    private static int UnlistedFloorLayers(TrackedVehicle drive, Rigidbody body, Transform[] points)
    {
        if (points == null) return 0;
        var hits = new RaycastHit[64];
        int layers = 0;
        foreach (var point in points)
        {
            if (point == null) continue;
            if (!CCVehicleGround.Cast(drive.transform, body, point.position, -drive.transform.up,
                    drive.restLength + drive.trackRadius + .30f, ~0, hits, out RaycastHit hit)) continue;
            if (hit.collider.attachedRigidbody != null) continue;
            int mask = 1 << hit.collider.gameObject.layer;
            if ((drive.groundMask.value & mask) == 0) layers |= mask;
        }
        return layers;
    }
}
#endif
