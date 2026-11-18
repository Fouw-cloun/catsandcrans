#if UNITY_EDITOR
using System;
using System.IO;
using CatCooks.Food;
using UnityEditor;
using UnityEngine;
using W = CCWorkshopEditor;

public static class CCDumpTruckCleanSetup
{
    private const string ModelPath = W.Models + "DumpTruck_Clean.fbx";
    private const float Mass = 6500f;
    private const float Sag = .14f;
    private static readonly string[] WheelNames =
        { "fortire left", "fortire right", "tire.001", "tire.002", "tire.003", "tire.004" };

    [MenuItem("CatCooks/Vehicle Workshop/04E Create clean dump truck")]
    public static void Create()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Выйди из Play Mode.");
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (asset == null) throw new InvalidOperationException("Unity не импортировала " + ModelPath);
        var active = Selection.activeTransform;
        Vector3 position = active != null ? active.position + active.right * 5f :
            SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create clean dump truck");
        var root = new GameObject("DumpTruck_Workshop_Clean");
        Undo.RegisterCreatedObjectUndo(root, "Create clean dump truck");
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            Undo.RegisterCreatedObjectUndo(model, "Create dump truck artwork");
            model.transform.SetParent(root.transform, false);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.UserAction);
            NormalizeModel(model.transform);

            var wheels = new Transform[6];
            var hubs = new Vector3[6];
            float radius = 0;
            for (int i = 0; i < wheels.Length; i++)
            {
                wheels[i] = Part(model.transform, WheelNames[i]);
                hubs[i] = Mark(model.transform, "Hub" + i);
                var renderer = wheels[i].GetComponent<MeshRenderer>();
                if (renderer == null || Vector3.Distance(renderer.bounds.center, hubs[i]) > .08f)
                    throw new InvalidOperationException("Метка и геометрия не совпали для " + WheelNames[i] + ".");
                radius += renderer.bounds.extents.y / wheels.Length;
            }
            ValidateGeometry(model.transform, hubs, radius);

            var truck = W.Add<DumpTruckVehicle>(root);
            W.Body(truck, Mass);
            ConfigureWheels(truck, wheels, hubs, radius);
            ConfigureSeatAndBody(truck, model.transform, hubs, radius);
            ConfigureBed(truck, model.transform);
            W.Add<CCVehicleHelp>(root).text =
                "WASD: ехать • Space: тормоз • Q/E: поднять/опустить кузов\n" +
                "Борт открывается при подъёме • F: войти/выйти • H: подсказка";
            root.transform.position = position;
            W.SavePrototype(root, "DumpTruck_Workshop_Clean");
            Debug.Log($"04E: 6 колёс, радиус {radius:F2} м. Задние мосты позади переднего и кабины.", root);
            Undo.CollapseUndoOperations(group);
        }
        catch
        {
            if (root != null) Undo.DestroyObjectImmediate(root);
            throw;
        }
    }

    private static void NormalizeModel(Transform model)
    {
        var origin = Part(model, "CCRig_Origin");
        Vector3 forward = Mark(model, "Forward") - origin.position;
        Vector3 up = Mark(model, "Up") - origin.position;
        float unit = (forward.magnitude + up.magnitude) * .5f;
        if (unit < .000001f || Mathf.Abs(forward.magnitude - up.magnitude) > unit * .02f)
            throw new InvalidOperationException("В FBX неверный масштаб осевых меток.");
        model.localScale /= unit;
        model.rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * model.rotation;
        model.position -= origin.position;
    }

    private static void ValidateGeometry(Transform model, Vector3[] hubs, float radius)
    {
        float front = (hubs[0].z + hubs[1].z) * .5f;
        float rear = (hubs[4].z + hubs[5].z) * .5f;
        float floor = hubs[0].y;
        for (int i = 0; i < hubs.Length; i++)
        {
            if (Mathf.Abs(hubs[i].y - floor) > .08f || (i >= 2 && hubs[i].z >= front - 1f))
                throw new InvalidOperationException("Оси колёс не совпали с шасси.");
        }
        if (Mathf.Abs(radius - .652f) > .05f || Mathf.Abs(front - rear - 4.706f) > .12f ||
            Mark(model, "GatePivot").z >= rear || Part(model, "SEAT1").position.z <= rear)
            throw new InvalidOperationException("Неверное положение кабины, борта или колёс в DumpTruck_Clean.fbx.");
    }

    private static void ConfigureWheels(DumpTruckVehicle truck, Transform[] meshes, Vector3[] hubs, float radius)
    {
        var root = truck.transform;
        truck.stableSuspension = true;
        truck.groundMask = ~0;
        truck.restLength = .42f;
        truck.wheelRadius = radius;
        truck.springStrength = Mass * Physics.gravity.magnitude / (6 * Sag);
        truck.springDamper = 1.75f * Mathf.Sqrt(truck.springStrength * Mass / 6);
        truck.maxForwardSpeed = 5.7f;
        truck.maxReverseSpeed = 2.3f;
        truck.tractionAcceleration = 1.1f;

        var anchors = W.Child(root, "WheelAnchors", Vector3.zero);
        var visuals = W.Child(root, "WheelVisuals", Vector3.zero);
        var stops = W.Child(root, "WheelBumpStops", Vector3.zero);
        var material = SlideMaterial();
        truck.wheels = new WheeledVehicle.Wheel[6];
        truck.rearWheelVisuals = new Transform[4];
        for (int i = 0; i < meshes.Length; i++)
        {
            var hub = hubs[i];
            var anchor = W.Child(anchors, "Anchor_" + i, hub + Vector3.up * (truck.restLength - Sag));
            var visual = W.Child(visuals, "Wheel_" + i, hub);
            Undo.SetTransformParent(meshes[i], visual, "Mount FBX tire at its marked center");
            if (Vector3.Distance(meshes[i].GetComponent<Renderer>().bounds.center, hub) > .08f)
                throw new InvalidOperationException("При привязке визуала сместилось колесо " + meshes[i].name);
            foreach (var collider in meshes[i].GetComponentsInChildren<Collider>(true))
            {
                Undo.RecordObject(collider, "Separate visual tires from physics");
                collider.enabled = false;
            }
            truck.wheels[i] = new WheeledVehicle.Wheel
            {
                anchor = anchor,
                visual = visual,
                steering = i < 2,
                motorized = true,
                spinAxisLocal = Vector3.right
            };
            if (i >= 2) truck.rearWheelVisuals[i - 2] = visual;

            var node = W.Child(stops, "Pad_" + i, hub);
            var pad = Undo.AddComponent<SphereCollider>(node.gameObject);
            pad.radius = radius - .085f;
            pad.sharedMaterial = material;
        }
        W.Set(truck, "_centerOfMass", new Vector3(0, .78f, (hubs[0].z + hubs[5].z) * .5f));
    }

    private static void ConfigureSeatAndBody(DumpTruckVehicle truck, Transform model, Vector3[] hubs, float radius)
    {
        var root = truck.transform;
        Vector3 pos = Part(model, "SEAT1").position;
        var seat = W.Child(root, "SeatPoint", pos);
        var exit = W.Child(root, "ExitPoint", new Vector3(pos.x < 0 ? -2.15f : 2.15f, hubs[0].y - radius + .15f, pos.z));
        var camera = W.Child(seat, "CameraPoint", Vector3.up * .8f);
        W.Seat(truck, seat, exit, camera);

        var chassis = Part(model, "base");
        Bounds hull = W.BoundsIn(root, new[] { chassis.GetComponent<Renderer>() });
        var box = Undo.AddComponent<BoxCollider>(root.gameObject);
        box.center = hull.center;
        box.size = Vector3.Scale(hull.size, new Vector3(.87f, .82f, .89f));
        box.sharedMaterial = SlideMaterial();
    }

    private static void ConfigureBed(DumpTruckVehicle truck, Transform model)
    {
        var root = truck.transform;
        var bed = Part(model, "Cube");
        var gate = Part(model, "Cube.001");
        var bedHinge = W.Child(root, "WorkshopBedHinge", Mark(model, "BedPivot"));
        var gateHinge = W.Child(bedHinge, "WorkshopGateHinge",
            bedHinge.InverseTransformPoint(Mark(model, "GatePivot")));
        Undo.SetTransformParent(bed, bedHinge, "Mount the bed on the rear hinge");
        Undo.SetTransformParent(gate, gateHinge, "Mount the tailgate on the upper hinge");

        W.Set(truck, "_bed", bedHinge);
        W.Set(truck, "_tiltSpeed", 11f);
        W.Set(truck, "_bedLimit", new Vector2(0, 55));
        truck.bedAxis = Vector3.left;
        truck.tailgate = gateHinge;
        truck.gateAxis = Vector3.right;
        truck.automaticGate = true;

        Vector3 floor = Mark(model, "CargoFloor");
        Vector3 side = Mark(model, "CargoSide") - floor;
        Vector3 front = Mark(model, "CargoFront") - floor;
        float depth = (Mark(model, "CargoTop") - floor).magnitude;
        var volume = W.Child(bedHinge, "BedContents", bedHinge.InverseTransformPoint(floor));
        var cargo = W.Container(volume.gameObject, "DumpTruckBed", Vector3.zero, root.rotation,
            new Vector2(side.magnitude, front.magnitude), depth, 640);

        var walls = cargo.transform.Find("AutoColliders");
        if (walls != null)
        {
            var colliders = walls.GetComponentsInChildren<BoxCollider>();
            foreach (var collider in colliders)
                if (collider.name.StartsWith("Z-", StringComparison.Ordinal))
                    Undo.SetTransformParent(collider.transform, gateHinge, "Make rear collision follow tailgate");
        }

        var spill = cargo.GetComponent<CookwareSpill>();
        LiquidPuddle puddle = spill != null ? spill.PuddlePrefab : null;
        if (spill != null) spill.enabled = false;
        if (puddle == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Scripts/NewCookingSystem/MilkPuddle.prefab");
            if (prefab != null) puddle = prefab.GetComponent<LiquidPuddle>();
        }
        var outlet = W.Child(bedHinge, "RearOutlet", bedHinge.InverseTransformPoint(Mark(model, "Outlet")));
        outlet.localRotation = Quaternion.Euler(0, 180, 0);
        var stream = W.Add<LiquidPourSource>(root.gameObject);
        W.Set(stream, "_mouth", outlet);
        W.Set(stream, "_pourByTilt", false);
        W.Set(stream, "_forcePour", false);
        W.Set(stream, "_speed", .55f);
        W.Set(stream, "_width", .085f);

        var discharge = W.Add<CCDumpTruckDischarge>(root.gameObject);
        discharge.truck = truck;
        discharge.cookware = cargo;
        discharge.outlet = outlet;
        discharge.puddlePrefab = puddle;
        discharge.stream = stream;
        var damping = W.Add<CCDumpTruckCargo>(root.gameObject);
        damping.truck = truck;
        damping.cookware = cargo;

        string[] names = { "Cylinder.001", "Cylinder.002", "Cylinder.003", "Cylinder.004", "Cylinder.005", "Cylinder" };
        var sections = new Transform[names.Length];
        for (int i = 0; i < sections.Length; i++) sections[i] = Part(model, names[i]);
        var ram = W.Add<CCDumpTruckHydraulics>(root.gameObject);
        ram.bottom = W.Child(root, "WorkshopRamBase", sections[0].position);
        ram.top = W.Child(bedHinge, "WorkshopRamTip", bedHinge.InverseTransformPoint(sections[5].position));
        foreach (var section in sections) Undo.SetTransformParent(section, root, "Mount hydraulic stage");
        ram.sections = sections;
    }

    private static PhysicsMaterial SlideMaterial()
    {
        Directory.CreateDirectory(W.Generated);
        AssetDatabase.Refresh();
        string path = W.Generated + "DumpTruckCleanSlide.physicMaterial";
        var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (material != null) return material;
        material = new PhysicsMaterial("DumpTruckCleanSlide")
        {
            staticFriction = .03f,
            dynamicFriction = .03f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounciness = 0
        };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Vector3 Mark(Transform model, string name) => Part(model, "CCRig_" + name).position;

    private static Transform Part(Transform root, string name)
    {
        var part = W.Find(root, name);
        if (part == null) throw new InvalidOperationException("В чистом FBX не найдена деталь " + name);
        return part;
    }
}
#endif
