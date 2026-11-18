#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using W = CCWorkshopEditor;

public static class CCBulldozerCleanRigSetup
{
    private const string ModelPath = W.Models + "Bulldozer_CleanRig.fbx";
    private const float Mass = 4200f;
    private const float Sag = .15f;

    [MenuItem("CatCooks/Vehicle Workshop/02F Create clean bulldozer")]
    public static void Create()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Выйди из Play Mode.");
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (asset == null) throw new InvalidOperationException("Unity не нашла модель по пути " + ModelPath);

        var selected = Selection.activeGameObject;
        var previous = selected != null ? selected.GetComponentInParent<TrackedVehicle>() : null;
        var excavator = previous as ExcavatorVehicle;
        if (excavator == null)
            foreach (var candidate in UnityEngine.Object.FindObjectsByType<ExcavatorVehicle>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == SceneManager.GetActiveScene()) { excavator = candidate; break; }

        Vector3 position = previous != null ? previous.transform.position + previous.transform.right * 5f :
            SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create clean bulldozer");
        var root = new GameObject("Bulldozer_Workshop_Clean");
        Undo.RegisterCreatedObjectUndo(root, "Create clean bulldozer");
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            Undo.RegisterCreatedObjectUndo(model, "Create clean bulldozer model");
            model.transform.SetParent(root.transform, false);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.UserAction);

            float importedUnit = NormalizeModel(model.transform);
            var shell = Part(model.transform, "tracks");
            if (shell.GetComponent<MeshFilter>() == null || shell.GetComponent<MeshRenderer>() == null ||
                model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0)
                throw new InvalidOperationException("Ожидалась модель с единым мешем гусениц без костей.");
            Bounds beltBounds = W.BoundsIn(root.transform, new[] { shell.GetComponent<MeshRenderer>() });
            if (Mathf.Abs(beltBounds.size.x - 3.83f) > .35f ||
                Mathf.Abs(beltBounds.size.y - 1f) > .22f ||
                Mathf.Abs(beltBounds.size.z - 4.19f) > .35f)
                throw new InvalidOperationException(
                    $"Размеры ходовой после нормализации {beltBounds.size:F2} м. " +
                    "Ожидалось около 3.83 × 1.00 × 4.19 м. Проверь, что используется Bulldozer_CleanRig.fbx из 02F.");

            var shapeA = ContactBounds(model.transform, 0);
            var shapeB = ContactBounds(model.transform, 1);
            var left = shapeA.center.x < shapeB.center.x ? shapeA : shapeB;
            var right = shapeA.center.x < shapeB.center.x ? shapeB : shapeA;
            float groundY = Mathf.Min(left.min.y, right.min.y);
            if (right.center.x - left.center.x < .2f || left.size.z < 1f || right.size.z < 1f)
                throw new InvalidOperationException("Проверь масштаб и оси импорта Bulldozer_CleanRig.fbx.");

            var drive = W.Add<TrackedVehicle>(root);
            W.Body(drive, Mass);
            drive.groundMask = excavator != null ? excavator.groundMask : (LayerMask)~0;
            drive.stableSuspension = true;
            drive.heavyTrackDrive = true;
            drive.restLength = .5f;
            drive.trackRadius = .24f;
            drive.springStrength = Mass * Physics.gravity.magnitude / (8f * Sag);
            drive.springDamper = 1.7f * Mathf.Sqrt(drive.springStrength * Mass / 8f);
            drive.maxForwardSpeed = 3.7f;
            drive.maxReverseSpeed = 2.3f;
            drive.tractionAcceleration = 2.1f;
            drive.pivotSpeedDegrees = 36f;

            var points = W.Child(root.transform, "WorkshopDozerSuspension", Vector3.zero);
            drive.leftTrackPoints = Supports(points, left, "L", drive);
            drive.rightTrackPoints = Supports(points, right, "R", drive);
            W.Set(drive, "_centerOfMass", new Vector3((left.center.x + right.center.x) * .5f,
                groundY + .5f, (left.center.z + right.center.z) * .5f));

            Bounds hull = W.BoundsIn(root.transform, Part(model.transform, "hull").GetComponentsInChildren<Renderer>());
            AddBox(root, hull.center, Vector3.Scale(hull.size, new Vector3(.90f, .86f, .88f)));
            AddTrackBox(root, left);
            AddTrackBox(root, right);

            Vector3 seatPos = Part(model.transform, "SEAT1").position;
            var seat = W.Child(root.transform, "WorkshopDozerSeat", seatPos);
            var exit = W.Child(root.transform, "WorkshopDozerExit",
                new Vector3(left.min.x - .65f, groundY + .15f, seatPos.z));
            var camera = W.Child(seat, "WorkshopDozerCamera", Vector3.up * .72f);
            W.Seat(drive, seat, exit, camera);

            SetupBlade(root.transform, model.transform, drive);
            W.Add<CCVehicleHelp>(root).text =
                "WASD: ехать • Space: тормоз • Q/E: поднять/опустить ковш\nR/T: наклонить ковш • F: выйти • H: подсказка";
            root.transform.position = position;
            W.Done(root, $"02F: бульдозер создан. Масштаб импорта был ×{importedUnit:F4}; " +
                $"размер ходовой {beltBounds.size:F2} м. Поставь машину на ровную площадку и сохрани сцену.");
            Undo.CollapseUndoOperations(group);
        }
        catch
        {
            if (root != null) Undo.DestroyObjectImmediate(root);
            throw;
        }
    }

    private static float NormalizeModel(Transform model)
    {
        var origin = Part(model, "WorkshopModelOrigin");
        var frontMarker = Part(model, "WorkshopModelFront");
        var upMarker = Part(model, "WorkshopModelUp");
        Vector3 forward = frontMarker.position - origin.position;
        Vector3 up = upMarker.position - origin.position;
        float forwardMeters = forward.magnitude;
        float upMeters = up.magnitude;
        float unit = (forwardMeters + upMeters) * .5f;
        // The FBX landmarks are exactly one meter apart in source coordinates.
        if (unit < .000001f || Mathf.Abs(forwardMeters - upMeters) > unit * .02f ||
            Mathf.Abs(Vector3.Dot(forward.normalized, up.normalized)) > .02f)
            throw new InvalidOperationException("Маркеры FBX искажены. Проверь равномерность масштаба модели.");

        model.localScale /= unit;
        model.rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * model.rotation;
        model.position -= origin.position;
        return unit;
    }

    private static Bounds ContactBounds(Transform model, int n)
    {
        var a = Part(model, "ContactLow" + n).position;
        var b = Part(model, "ContactHigh" + n).position;
        var bounds = new Bounds();
        bounds.SetMinMax(Vector3.Min(a, b), Vector3.Max(a, b));
        return bounds;
    }

    private static Transform[] Supports(Transform holder, Bounds track, string side, TrackedVehicle drive)
    {
        var points = new Transform[4];
        for (int i = 0; i < 4; i++)
            points[i] = W.Child(holder, side + i, new Vector3(track.center.x,
                track.min.y + drive.restLength + drive.trackRadius - Sag,
                Mathf.Lerp(track.min.z, track.max.z, .13f + .246667f * i)));
        return points;
    }

    private static void AddBox(GameObject root, Vector3 center, Vector3 size)
    {
        var box = Undo.AddComponent<BoxCollider>(root);
        box.center = center;
        box.size = size;
    }

    private static void AddTrackBox(GameObject root, Bounds track)
    {
        // Keep hard colliders inside the belts so their underside does not take
        // the weight away from the raycast springs on a flat surface.
        float bottom = track.min.y + .08f;
        float top = track.max.y - .08f;
        AddBox(root, new Vector3(track.center.x, (bottom + top) * .5f, track.center.z),
            new Vector3(track.size.x * .90f, top - bottom, track.size.z * .90f));
    }

    private static void SetupBlade(Transform root, Transform model, TrackedVehicle drive)
    {
        var hands = Part(model, "hands");
        var bucket = Part(model, "shield");
        var lift = W.Child(root, "WorkshopDozerLift", root.InverseTransformPoint(hands.position));
        var tilt = W.Child(lift, "WorkshopDozerTilt", lift.InverseTransformPoint(bucket.position));
        Undo.SetTransformParent(hands, lift, "Mount lift arms");
        Undo.SetTransformParent(bucket, tilt, "Mount compact blade");

        Vector3 floor = Part(bucket, "CCBucket_Floor").position;
        Vector3 forward = (Part(bucket, "CCBucket_FloorFront").position - floor).normalized;
        Vector3 right = (Part(bucket, "CCBucket_Right").position - floor).normalized;
        Vector3 rim = Part(bucket, "CCBucket_Rim").position;
        Vector3 up = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(rim - floor, forward), right).normalized;
        float depth = Vector3.Dot(rim - floor, up);
        var frame = Quaternion.LookRotation(forward, up);
        var bottomHalf = new Vector2(Vector3.Distance(floor, Part(bucket, "CCBucket_Right").position),
            Vector3.Distance(floor, Part(bucket, "CCBucket_FloorFront").position));
        W.Container(bucket.gameObject, "BulldozerBucket", bucket.InverseTransformPoint(floor), frame, bottomHalf, depth, 120);
        var cavity = bucket.GetComponent<CatCooks.Food.CookwareCavity>();
        W.Set(cavity, "_topHalf", new Vector2(bottomHalf.x, Vector3.Distance(rim, Part(bucket, "CCBucket_RimFront").position)));
        W.Set(cavity, "_topShift", new Vector2(0, Vector3.Dot(rim - floor, forward)));
        W.Set(cavity, "_wall", depth * .275f);
        W.Set(cavity, "_bands", 2);
        cavity.EditorBuild();

        var blade = W.Add<CCBulldozerBlade>(root.gameObject);
        blade.vehicle = drive;
        blade.liftPivot = lift;
        blade.tiltPivot = tilt;
        blade.cylinders = new[]
        {
            Cylinder(root, tilt, Part(model, "piston main 2"), Part(model, "piston secondary part 2"), "Left"),
            Cylinder(root, tilt, Part(model, "piston main1"), Part(model, "piston secondary part 1"), "Right")
        };
        EditorUtility.SetDirty(blade);
    }

    private static CCBulldozerBlade.Cylinder Cylinder(Transform root, Transform tilt, Transform barrel, Transform rod, string side)
    {
        var basePin = W.Child(root, "Dozer" + side + "BasePin", root.InverseTransformPoint(barrel.position));
        var tipPin = W.Child(tilt, "Dozer" + side + "TipPin", tilt.InverseTransformPoint(rod.position));
        Undo.SetTransformParent(barrel, root, "Mount cylinder");
        Undo.SetTransformParent(rod, tilt, "Mount piston rod");
        var cylinder = new CCBulldozerBlade.Cylinder { barrel = barrel, rod = rod, basePin = basePin, tipPin = tipPin };
        cylinder.Bind();
        return cylinder;
    }

    private static Transform Part(Transform root, string name)
    {
        var part = W.Find(root, name);
        if (part == null) throw new InvalidOperationException("В модели не найдена деталь " + name);
        return part;
    }
}
#endif
