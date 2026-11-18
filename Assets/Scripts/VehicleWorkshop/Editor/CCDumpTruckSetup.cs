#if UNITY_EDITOR
using System;
using System.Linq;
using CatCooks.Food;
using UnityEditor;
using UnityEngine;
using W = CCWorkshopEditor;

public static class CCDumpTruckSetup
{
    [MenuItem("CatCooks/Vehicle Workshop/04 Create dump truck")]
    public static void Create()
    {
        var active = Selection.activeTransform;
        Vector3 position = active != null ? active.position + Vector3.right * 5 :
            SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        var root = W.Model("DumpTruck_Source.fbx", "DumpTruck_Workshop");
        try
        {
            var wheels = W.TruckWheels(root.transform);
            if (wheels.Count != 6) throw new InvalidOperationException("В FBX самосвала ожидаются шесть колёс.");
            float diameter = wheels.Average(w => w.GetComponent<Renderer>().bounds.size.y);
            if (diameter < .001f) throw new InvalidOperationException("У колёс FBX неверный масштаб.");
            root.transform.GetChild(0).localScale *= 1.304f / diameter;

            var truck = W.Add<DumpTruckVehicle>(root);
            Setup(truck);
            root.transform.position = position;
            CCDumpTruckRideSetup.Apply(truck, false);
            W.SavePrototype(root, "DumpTruck_Workshop");
        }
        catch
        {
            if (root != null) Undo.DestroyObjectImmediate(root);
            throw;
        }
    }

    [MenuItem("CatCooks/Vehicle Workshop/04 Adapt selected dump truck")]
    public static void Adapt()
    {
        var selected = W.Selected();
        var truck = selected.GetComponentInParent<DumpTruckVehicle>();
        if (truck == null) throw new InvalidOperationException("Выдели корень машины с DumpTruckVehicle.");
        if (truck.tailgate != null && truck.GetComponent<CCDumpTruckDischarge>() != null)
        {
            W.Done(truck.gameObject, "Самосвал уже настроен. Параметры можно менять в Inspector.");
            return;
        }
        Setup(truck);
        CCDumpTruckRideSetup.Apply(truck, false);
        W.Done(truck.gameObject, "04: настроен кузов, задний борт и выгрузка.");
    }

    private static void Setup(DumpTruckVehicle truck)
    {
        var root = truck.transform;
        var gate = W.Find(root, "Cube.001");
        var meshBed = truck.Bed != null ? truck.Bed : gate != null ? gate.parent : null;
        if (meshBed == null || gate == null || !gate.IsChildOf(meshBed) || gate.GetComponent<MeshFilter>() == null)
            throw new InvalidOperationException("Нужен кузов Cube с задним бортом Cube.001.");
        var wheels = W.TruckWheels(root);
        if (wheels.Count < 4) throw new InvalidOperationException("Не найдены колёса fortire и tire.001–004.");
        var cargoCandidates = meshBed.GetComponentsInChildren<Cookware>(true);
        if (cargoCandidates.Length > 1)
            throw new InvalidOperationException("На кузове больше одной тары. Определи, какую оставить, перед установкой.");

        Bounds b = W.BoundsIn(root, meshBed.GetComponentsInChildren<Renderer>());
        Bounds g = W.BoundsIn(root, gate.GetComponentsInChildren<Renderer>());
        bool forwardPositive = b.center.z > g.center.z;
        float sign = forwardPositive ? 1 : -1;
        float floor = Mathf.Min(g.min.y, b.max.y - b.size.y * .25f);
        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Configure dump truck");
        var instance = PrefabUtility.GetOutermostPrefabInstanceRoot(gate.gameObject);
        if (instance != null)
        {
            if (!instance.transform.IsChildOf(root)) throw new InvalidOperationException("Открой отдельный экземпляр самосвала.");
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.UserAction);
        }

        if (truck.wheels == null || truck.wheels.Length == 0 || truck.wheels.Any(w => w == null || w.anchor == null))
        {
            W.Wheels(truck, wheels, 6500);
            if (W.Ref(truck, "_seatPoint") == null) W.TruckSeat(truck);
        }
        else
        {
            if (root.GetComponent<Rigidbody>() == null) W.Body(truck, 6500);
            var body = root.GetComponent<Rigidbody>();
            W.Set(truck, "_body", body);
            W.Set(truck, "_isStatic", false);
            if (W.Ref(truck, "_seatPoint") == null) W.TruckSeat(truck);
        }
        truck.stableSuspension = true;
        truck.maxForwardSpeed = 5.7f;
        truck.maxReverseSpeed = 2.3f;
        truck.tractionAcceleration = 1.1f;

        Transform hinge = W.Child(root, "WorkshopBedHinge", new Vector3(g.center.x, floor, g.center.z));
        Undo.SetTransformParent(meshBed, hinge, "Install rear bed hinge");
        Transform gateHinge = W.Child(hinge, "WorkshopGateHinge",
            hinge.InverseTransformPoint(root.TransformPoint(new Vector3(g.center.x, g.max.y, g.center.z))));
        Undo.SetTransformParent(gate, gateHinge, "Install upper tailgate hinge");
        W.Set(truck, "_bed", hinge);
        W.Set(truck, "_tiltSpeed", 11f);
        W.Set(truck, "_bedLimit", new Vector2(0, 55));
        truck.bedAxis = -sign * Vector3.right;
        truck.tailgate = gateHinge;
        truck.gateAxis = sign * Vector3.right;
        truck.gateOpenAngle = 100;
        truck.automaticGate = true;

        foreach (Collider collider in meshBed.GetComponents<Collider>())
        {
            if (collider.isTrigger) continue;
            Undo.RecordObject(collider, "Remove solid fill from cargo bed");
            collider.enabled = false;
        }

        Cookware cookware;
        if (cargoCandidates.Length == 1) cookware = cargoCandidates[0];
        else
        {
            Vector3 center = new Vector3(b.center.x, floor + b.size.y * .021f, b.center.z);
            Transform volume = W.Child(hinge, "BedContents", hinge.InverseTransformPoint(root.TransformPoint(center)));
            Quaternion frame = Quaternion.LookRotation(root.forward * sign, root.up);
            float width = root.TransformVector(Vector3.right * b.extents.x).magnitude;
            float length = root.TransformVector(Vector3.forward * b.extents.z).magnitude;
            float depth = root.TransformVector(Vector3.up * (g.max.y - floor)).magnitude * .67f;
            cookware = W.Container(volume.gameObject, "DumpTruckBed", Vector3.zero, frame,
                new Vector2(width * .90f, length * .92f), Mathf.Max(.35f, depth), 640);
        }
        W.Quiet(cookware);
        var spill = cookware.GetComponent<CookwareSpill>();
        LiquidPuddle puddle = spill != null ? spill.PuddlePrefab : null;
        if (spill != null)
        {
            Undo.RecordObject(spill, "Drain only through rear gate");
            spill.enabled = false;
        }
        if (puddle == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Scripts/NewCookingSystem/MilkPuddle.prefab");
            if (prefab != null) puddle = prefab.GetComponent<LiquidPuddle>();
        }
        AttachRearCollision(cookware, gate, gateHinge, root, g.center.z, b.size.z);

        Transform outlet = W.Child(hinge, "RearOutlet",
            hinge.InverseTransformPoint(root.TransformPoint(new Vector3(g.center.x, floor + b.size.y * .07f, g.center.z))));
        var discharge = W.Add<CCDumpTruckDischarge>(root.gameObject);
        discharge.truck = truck;
        discharge.cookware = cookware;
        discharge.outlet = outlet;
        discharge.puddlePrefab = puddle;
        outlet.localRotation = Quaternion.LookRotation(-sign * Vector3.forward, Vector3.up);
        var stream = W.Add<LiquidPourSource>(root.gameObject);
        W.Set(stream, "_mouth", outlet);
        W.Set(stream, "_pourByTilt", false);
        W.Set(stream, "_forcePour", false);
        W.Set(stream, "_speed", .55f);
        W.Set(stream, "_width", .085f);
        discharge.stream = stream;

        var cargo = W.Add<CCDumpTruckCargo>(root.gameObject);
        cargo.truck = truck;
        cargo.cookware = cookware;
        ConfigureHydraulics(root, hinge);
        W.Add<CCVehicleHelp>(root.gameObject).text =
            "WASD: ехать • Space: тормоз • Q/E: поднять/опустить кузов\n" +
            "Борт свободно отклоняется при подъёме • F: выйти • H: подсказка";
        EditorUtility.SetDirty(truck);
    }

    private static void AttachRearCollision(Cookware cookware, Transform gate, Transform gateHinge, Transform root,
        float rear, float bedLength)
    {
        bool moved = false;
        var walls = cookware.transform.Find("AutoColliders");
        if (walls != null)
        {
            foreach (var collider in walls.GetComponentsInChildren<BoxCollider>())
            {
                var t = collider.transform;
                if (!t.name.StartsWith("Z+", StringComparison.Ordinal) &&
                    !t.name.StartsWith("Z-", StringComparison.Ordinal)) continue;
                float z = root.InverseTransformPoint(t.position).z;
                if (Mathf.Abs(z - rear) > bedLength * .20f) continue;
                Undo.SetTransformParent(t, gateHinge, "Open cargo wall with tailgate");
                moved = true;
            }
        }
        if (!moved && gate.GetComponent<Collider>() == null)
        {
            var collider = W.Add<BoxCollider>(gate.gameObject);
            Bounds bounds = gate.GetComponent<MeshFilter>().sharedMesh.bounds;
            collider.center = bounds.center;
            collider.size = bounds.size;
        }
    }

    private static void ConfigureHydraulics(Transform root, Transform hinge)
    {
        string[] names = { "Cylinder.001", "Cylinder.002", "Cylinder.003", "Cylinder.004", "Cylinder.005", "Cylinder" };
        Transform[] sections = names.Select(name => W.Find(root, name)).ToArray();
        if (sections.Any(t => t == null)) return;
        foreach (var script in root.GetComponentsInChildren<TelescopingCylinder>(true))
        {
            Undo.RecordObject(script, "Use workshop telescopic motion");
            script.enabled = false;
        }
        var bottom = W.Child(root, "WorkshopRamBase", root.InverseTransformPoint(sections[0].position));
        var top = W.Child(hinge, "WorkshopRamTip", hinge.InverseTransformPoint(sections[5].position));
        foreach (var section in sections) Undo.SetTransformParent(section, root, "Separate ram stages");
        var ram = W.Add<CCDumpTruckHydraulics>(root.gameObject);
        ram.bottom = bottom;
        ram.top = top;
        ram.sections = sections;
    }
}
#endif
