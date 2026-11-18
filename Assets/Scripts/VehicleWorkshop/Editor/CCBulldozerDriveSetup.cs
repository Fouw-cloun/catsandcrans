#if UNITY_EDITOR
using System;
using CatCooks.Food;
using FishNet.Object;
using UnityEditor;
using UnityEngine;
using W = CCWorkshopEditor;

public static class CCBulldozerDriveSetup
{
    [MenuItem("CatCooks/Vehicle Workshop/02C Set up bulldozer drive and blade")]
    public static void Install()
    {
        var selected = W.Selected();
        var shield = selected.name == "shield" ? selected.transform : W.Find(selected.transform, "shield");
        if (shield == null) throw new InvalidOperationException("Выдели корень бульдозера или shield.");
        var network = shield.GetComponentInParent<NetworkObject>();
        if (network == null || network.transform == shield)
            throw new InvalidOperationException("Добавь NetworkObject на родительский корень бульдозера.");
        var root = network.transform;
        var existingBlade = root.GetComponent<CCBulldozerBlade>();
        if (existingBlade != null && existingBlade.liftPivot != null && existingBlade.tiltPivot != null)
        {
            Debug.Log("02C уже настроен. Пределы и скорость меняются в CCBulldozerBlade.", root);
            return;
        }

        var currentDrive = root.GetComponent<NetworkVehicle>();
        if (currentDrive != null && currentDrive.GetType() != typeof(TrackedVehicle))
            throw new InvalidOperationException("На корне уже другой контроллер машины. 02C рассчитан на бульдозер с обычным TrackedVehicle или без контроллера.");
        var hands = Required(root, "hands");
        var hull = Required(root, "hull");
        var seatMesh = Required(root, "SEAT1");
        var trackA = Required(root, "track");
        var trackB = Required(root, "track.003");
        var barrelLeft = Required(root, "piston main 2");
        var barrelRight = Required(root, "piston main1");
        var rodLeft = Required(root, "piston secondary part 2");
        var rodRight = Required(root, "piston secondary part 1");

        Vector3 up = Vector3.up;
        Vector3 front = Vector3.ProjectOnPlane(shield.position - hands.position, up).normalized;
        if (front.sqrMagnitude < .5f) throw new InvalidOperationException("Не удалось определить перед машины.");
        Quaternion chassisFrame = Quaternion.LookRotation(front, up);
        var instance = PrefabUtility.GetOutermostPrefabInstanceRoot(shield.gameObject);
        if (instance != null && !instance.transform.IsChildOf(root))
            throw new InvalidOperationException("Бульдозер вложен в более крупный prefab. Сначала открой отдельный экземпляр бульдозера в сцене.");
        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Set up bulldozer");
        if (instance != null)
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.UserAction);

        if (currentDrive == null && root.GetComponent<Collider>() == null &&
            (Vector3.Dot(root.forward, front) < .98f || Vector3.Dot(root.up, up) < .98f))
            AlignRoot(root, chassisFrame);
        if (Vector3.Dot(root.forward, front) < .8f || Vector3.Dot(root.up, up) < .8f)
            Debug.LogWarning("Направление шасси отличается от осей корня. Проверь W на тестовой площадке.", root);

        var drive = currentDrive as TrackedVehicle;
        if (drive == null) drive = W.Add<TrackedVehicle>(root.gameObject);
        if (!drive.stableSuspension || drive.leftTrackPoints == null || drive.leftTrackPoints.Length == 0 ||
            drive.rightTrackPoints == null || drive.rightTrackPoints.Length == 0)
            SetupDrive(drive, hull, seatMesh, trackA, trackB);

        var lift = WorldChild(root, "WorkshopDozerLift", hands.position, chassisFrame);
        var tilt = WorldChild(lift, "WorkshopDozerTilt", shield.position, chassisFrame);
        Undo.SetTransformParent(hands, lift, "Bulldozer lift arms");
        Undo.SetTransformParent(shield, tilt, "Bulldozer blade tilt");

        var blade = W.Add<CCBulldozerBlade>(root.gameObject);
        Undo.RecordObject(blade, "Bind bulldozer blade");
        blade.vehicle = drive;
        blade.liftPivot = lift;
        blade.tiltPivot = tilt;
        blade.cylinders = new[]
        {
            Link(root, tilt, barrelLeft, rodLeft, "Left"),
            Link(root, tilt, barrelRight, rodRight, "Right")
        };
        foreach (var old in root.GetComponentsInChildren<PistonLookAt>(true))
            if (old.basePart == barrelLeft || old.basePart == barrelRight || old.rodPart == rodLeft || old.rodPart == rodRight)
            {
                Undo.RecordObject(old, "Disable previous bulldozer piston driver");
                old.enabled = false;
            }
        W.Add<CCVehicleHelp>(root.gameObject).text =
            "WASD: ехать • Space: тормоз • Q/E: поднять/опустить ковш\nR/T: наклонить ковш • F: выйти • H: подсказка";
        EditorUtility.SetDirty(blade);
        W.Done(root.gameObject, "02C: езда, подъём, наклон и оба поршня настроены. Сохрани сцену.");
    }

    private static void AlignRoot(Transform root, Quaternion rotation)
    {
        int count = root.childCount;
        var positions = new Vector3[count];
        var rotations = new Quaternion[count];
        for (int i = 0; i < count; i++)
        {
            positions[i] = root.GetChild(i).position;
            rotations[i] = root.GetChild(i).rotation;
        }
        root.rotation = rotation;
        for (int i = 0; i < count; i++) root.GetChild(i).SetPositionAndRotation(positions[i], rotations[i]);
    }

    private static void SetupDrive(TrackedVehicle drive, Transform hull, Transform seatMesh, Transform trackA, Transform trackB)
    {
        var root = drive.transform;
        const float mass = 4200;
        const float sag = .15f;
        W.Body(drive, mass);
        drive.restLength = .5f;
        drive.trackRadius = .24f;
        drive.springStrength = mass * Physics.gravity.magnitude / (8 * sag);
        drive.springDamper = 1.7f * Mathf.Sqrt(drive.springStrength * mass / 8);
        drive.stableSuspension = true;
        drive.maxForwardSpeed = 3.7f;
        drive.maxReverseSpeed = 2.3f;
        drive.tractionAcceleration = 2.1f;
        drive.heavyTrackDrive = true;
        drive.pivotSpeedDegrees = 36f;
        drive.groundMask = ~0;

        Bounds a = W.BoundsIn(root, trackA.GetComponentsInChildren<Renderer>());
        Bounds b = W.BoundsIn(root, trackB.GetComponentsInChildren<Renderer>());
        Bounds left = a.center.x < b.center.x ? a : b;
        Bounds right = a.center.x < b.center.x ? b : a;
        var holder = W.Child(root, "WorkshopDozerSuspension", Vector3.zero);
        drive.leftTrackPoints = Points(drive, holder, left, "L", sag);
        drive.rightTrackPoints = Points(drive, holder, right, "R", sag);
        float floorY = Mathf.Min(left.min.y, right.min.y);
        W.Set(drive, "_centerOfMass", new Vector3((left.center.x + right.center.x) * .5f,
            floorY + LocalMeters(root, Vector3.up * .5f).y, (left.center.z + right.center.z) * .5f));

        if (root.GetComponentsInChildren<Collider>().Length == 0 ||
            hull.GetComponentInChildren<Collider>() == null && root.GetComponent<Collider>() == null)
        {
            Bounds body = W.BoundsIn(root, hull.GetComponentsInChildren<Renderer>());
            var chassis = W.Child(root, "WorkshopDozerChassis", body.center);
            W.Add<BoxCollider>(chassis.gameObject).size = new Vector3(body.size.x * .9f, body.size.y * .85f, body.size.z * .85f);
            int layer = LayerMask.NameToLayer("Interactable");
            if (layer >= 0) chassis.gameObject.layer = layer;
        }

        var seat = WorldChild(root, "WorkshopDozerSeat", seatMesh.position, root.rotation);
        var exit = WorldChild(root, "WorkshopDozerExit",
            root.TransformPoint(new Vector3(left.min.x, floorY, left.center.z)) - root.right * .65f + root.up * .14f, root.rotation);
        var camera = WorldChild(seat, "WorkshopDozerCamera", seat.position + root.up * .72f, root.rotation);
        W.Seat(drive, seat, exit, camera);
    }

    private static Transform[] Points(TrackedVehicle drive, Transform holder, Bounds track, string side, float sag)
    {
        float height = LocalMeters(holder, drive.transform.up * (drive.restLength + drive.trackRadius - sag)).y;
        var points = new Transform[4];
        for (int i = 0; i < points.Length; i++)
            points[i] = W.Child(holder, side + i, new Vector3(track.center.x, track.min.y + height,
                Mathf.Lerp(track.min.z, track.max.z, .13f + i * .246667f)));
        return points;
    }

    private static Vector3 LocalMeters(Transform frame, Vector3 vector) => frame.InverseTransformVector(vector);

    private static CCBulldozerBlade.Cylinder Link(Transform root, Transform tilt, Transform barrel, Transform rod, string side)
    {
        var basePin = WorldChild(root, "Dozer" + side + "BasePin", barrel.position, root.rotation);
        var tipPin = WorldChild(tilt, "Dozer" + side + "TipPin", rod.position, tilt.rotation);
        Undo.SetTransformParent(barrel, root, "Bulldozer cylinder");
        Undo.SetTransformParent(rod, tilt, "Bulldozer piston rod");
        var cylinder = new CCBulldozerBlade.Cylinder { barrel = barrel, rod = rod, basePin = basePin, tipPin = tipPin };
        cylinder.Bind();
        return cylinder;
    }

    private static Transform WorldChild(Transform parent, string name, Vector3 position, Quaternion rotation)
    {
        var child = W.Child(parent, name, parent.InverseTransformPoint(position));
        child.rotation = rotation;
        return child;
    }

    private static Transform Required(Transform parent, string name)
    {
        var part = W.Find(parent, name);
        if (part == null) throw new InvalidOperationException("В модели не найдена деталь " + name);
        return part;
    }
}
#endif
