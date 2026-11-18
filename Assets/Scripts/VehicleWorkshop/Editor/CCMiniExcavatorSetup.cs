#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using W = CCWorkshopEditor;

public static class CCMiniExcavatorSetup
{
    private static readonly float[] Reach = { .32f, .48f, .54f, .44f, .57f, .62f, .37f, .55f };

    [MenuItem("CatCooks/Vehicle Workshop/06 Create mini excavator")]
    public static void Create()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Выйди из Play Mode.");
        var selection = Selection.activeTransform;
        Vector3 position = selection != null ? selection.position + selection.right * 3.2f :
            SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

        GameObject root = W.BlueprintModel("MiniExcavator");
        try
        {
            var vehicle = W.Add<CCMiniExcavatorVehicle>(root);
            W.Tracks(vehicle, new Bounds(new Vector3(0f, .26f, 0f), new Vector3(1.6f, .52f, 2f)), 1550f);
            W.Set(vehicle, "_centerOfMass", new Vector3(0f, .32f, -.06f));
            vehicle.maxForwardSpeed = 3.3f;
            vehicle.maxReverseSpeed = 1.9f;
            vehicle.heavyTrackDrive = true;
            vehicle.tractionAcceleration = 2f;
            vehicle.pivotSpeedDegrees = 49f;
            W.Add<BoxCollider>(Part(root, "Chassis").gameObject);

            W.Set(vehicle, "_tower", Part(root, "Tower"));
            W.Set(vehicle, "_towerAxis", Vector3.up);
            W.Set(vehicle, "_boom", Part(root, "Boom"));
            W.Set(vehicle, "_arm", Part(root, "Stick"));
            W.Set(vehicle, "_bucket", Part(root, "Coupler"));
            W.Set(vehicle, "_boomLimit", new Vector2(-43f, 53f));
            W.Set(vehicle, "_armLimit", new Vector2(-55f, 65f));
            W.Set(vehicle, "_bucketLimit", new Vector2(-78f, 105f));
            W.Seat(vehicle, Part(root, "SeatPoint"), Part(root, "ExitPoint"), Part(root, "CameraPoint"));
            W.Add<CCVehicleHelp>(root).text =
                "F: сесть / выйти • Tab: езда / работа • WASD: езда • Space: тормоз\n" +
                "Работа: A/D башня • W/S стрела • Q/E рукоять • R/T наклон\n" +
                "1–8: насадки • G: взять / отпустить • Y: включить насадку";

            var tracks = W.Add<CCMiniTrackVisual>(root);
            tracks.leftTreads = Enumerable.Range(0, 26).Select(i => Part(root, $"TreadL{i:00}")).ToArray();
            tracks.rightTreads = Enumerable.Range(0, 26).Select(i => Part(root, $"TreadR{i:00}")).ToArray();

            vehicle.attachments = new CCMiniExcavatorAttachment[Reach.Length];
            for (int i = 0; i < Reach.Length; i++)
            {
                var kind = (CCMiniTool)i;
                string name = kind.ToString();
                Transform part = Part(root, name);
                var tool = W.Add<CCMiniExcavatorAttachment>(part.gameObject);
                tool.kind = kind;
                tool.reach = Reach[i];
                tool.tip = Part(root, name + "_Tip");
                tool.leftJaw = part.Find(name + "_Left");
                tool.rightJaw = part.Find(name + "_Right");
                tool.drivePart = part.Find(name + "_Drive");
                vehicle.attachments[i] = tool;
            }

            var bucket = vehicle.attachments[0];
            bucket.bucketRenderers = bucket.GetComponentsInChildren<Renderer>(true);
            vehicle.bucket = W.Container(bucket.gameObject, "MiniExcavatorBucket", new Vector3(0f, -.20f, .43f),
                bucket.transform.rotation, new Vector2(.33f, .31f), .18f, 18);
            bucket.bucketContainer = vehicle.bucket;
            bucket.bucketColliders = bucket.GetComponentsInChildren<Collider>(true);
            foreach (var collider in bucket.bucketColliders)
                collider.gameObject.layer = bucket.gameObject.layer;
            for (int i = 1; i < vehicle.attachments.Length; i++)
                vehicle.attachments[i].gameObject.SetActive(false);

            root.transform.position = position;
            W.SavePrototype(root, "MiniExcavator_Workshop");
        }
        catch
        {
            if (root != null) Undo.DestroyObjectImmediate(root);
            throw;
        }
    }

    private static Transform Part(GameObject root, string name)
    {
        Transform part = W.Find(root.transform, name);
        if (part == null) throw new InvalidOperationException("В модели мини-экскаватора нет " + name);
        return part;
    }
}
#endif
