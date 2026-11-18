#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using W = CCWorkshopEditor;

public static class CCDumpTruckRideSetup
{
    private const float Sag = .14f;
    private const float PadClearance = .085f;

    [MenuItem("CatCooks/Vehicle Workshop/04A Repair dump truck ride and tailgate")]
    public static void Repair()
    {
        var selected = W.Selected();
        var truck = selected.GetComponentInParent<DumpTruckVehicle>();
        if (truck == null || truck.tailgate == null)
            throw new InvalidOperationException("Выдели корень самосвала, настроенного через 04.");
        string result = Apply(truck, true);
        W.Done(truck.gameObject, "04A: ограничители хода и борт настроены. " + result);
    }

    internal static string Apply(DumpTruckVehicle truck, bool alignToFloor)
    {
        var root = truck.transform;
        var body = root.GetComponent<Rigidbody>();
        if (body == null)
            throw new InvalidOperationException("На корне нет Rigidbody.");
        CCDumpTruckFeedbackSetup.Configure(truck);
        var wheels = new List<WheeledVehicle.Wheel>();
        foreach (var wheel in truck.wheels)
            if (wheel != null && wheel.anchor != null) wheels.Add(wheel);
        if (wheels.Count < 4 || truck.wheelRadius < .15f || truck.wheelRadius > 1.5f)
            throw new InvalidOperationException("Проверь опоры и радиус колёс. Ожидаемый радиус исходной модели — около 0,65 м.");

        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "Repair dump truck suspension");
        Undo.RecordObject(truck, "Tune truck suspension");
        W.Set(truck, "_body", body);
        W.Set(truck, "_isStatic", false);
        truck.automaticGate = true;
        truck.stableSuspension = true;
        truck.springStrength = body.mass * Physics.gravity.magnitude / (wheels.Count * Sag);
        truck.springDamper = 1.75f * Mathf.Sqrt(truck.springStrength * body.mass / wheels.Count);

        var group = W.Child(root, "WorkshopWheelBumpStops", Vector3.zero);
        var material = PadMaterial();
        float radius = Mathf.Max(.1f, truck.wheelRadius - PadClearance);
        for (int i = 0; i < wheels.Count; i++)
        {
            Vector3 center = NeutralCenter(truck, wheels[i]);
            var pad = W.Child(group, "Pad_" + i, group.InverseTransformPoint(center));
            var collider = W.Add<SphereCollider>(pad.gameObject);
            Undo.RecordObject(collider, "Limit suspension bottoming");
            Vector3 worldScale = pad.lossyScale;
            float scale = Mathf.Max(Mathf.Abs(worldScale.x), Mathf.Abs(worldScale.y), Mathf.Abs(worldScale.z));
            collider.radius = radius / Mathf.Max(.0001f, scale);
            collider.sharedMaterial = material;
            collider.isTrigger = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
        }

        var help = W.Add<CCVehicleHelp>(root.gameObject);
        Undo.RecordObject(help, "Update dump truck controls");
        help.text = "WASD: ехать • Space: тормоз • Q/E: поднять/опустить кузов\n" +
            "Борт свободно отклоняется при подъёме • F: выйти • H: подсказка";

        string result = alignToFloor ? LiftOutOfFloor(truck, body, wheels) : "";
        PrefabUtility.RecordPrefabInstancePropertyModifications(truck);
        EditorUtility.SetDirty(truck);
        AssetDatabase.SaveAssets();
        return result;
    }

    private static Vector3 NeutralCenter(DumpTruckVehicle truck, WheeledVehicle.Wheel wheel) =>
        wheel.anchor.position - truck.transform.up * (truck.restLength - Sag);

    private static string LiftOutOfFloor(DumpTruckVehicle truck, Rigidbody body, List<WheeledVehicle.Wheel> wheels)
    {
        Physics.SyncTransforms();
        var buffer = new RaycastHit[64];
        var corrections = new List<float>();
        int floorLayers = 0;
        foreach (var wheel in wheels)
        {
            Vector3 center = NeutralCenter(truck, wheel);
            if (!CCVehicleGround.Cast(truck.transform, body, center + Vector3.up * .7f, Vector3.down,
                    2.8f, ~0, buffer, out RaycastHit hit)) continue;
            var surfaceBody = hit.collider.attachedRigidbody;
            if ((surfaceBody != null && !surfaceBody.isKinematic) ||
                Vector3.Dot(hit.normal, Vector3.up) < .85f) continue;
            corrections.Add(hit.point.y - (center.y - truck.wheelRadius));
            floorLayers |= 1 << hit.collider.gameObject.layer;
        }
        if (corrections.Count < 4)
            return "Не найден ровный пол под четырьмя колёсами. Поставь машину на свободную площадку.";
        corrections.Sort();
        if (corrections[corrections.Count - 1] - corrections[0] > .28f)
            return "Под колёсами перепад высоты. Проверь машину на ровной площадке.";
        truck.groundMask = truck.groundMask.value | floorLayers;
        float shift = corrections[corrections.Count - 1] + .07f;
        if (shift > .001f && shift < 1.25f)
        {
            Undo.RecordObject(truck.transform, "Lift wheels above floor");
            truck.transform.position += Vector3.up * shift;
            PrefabUtility.RecordPrefabInstancePropertyModifications(truck.transform);
            return $"Машина поднята на {shift:F2} м над найденным полом.";
        }
        return "Слой пола добавлен к Ground Mask. Проверь положение машины над настилом.";
    }

    private static PhysicsMaterial PadMaterial()
    {
        Directory.CreateDirectory(W.Generated);
        AssetDatabase.Refresh();
        string path = W.Generated + "DumpTruckWheelPad.physicMaterial";
        var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (material != null) return material;
        material = new PhysicsMaterial("DumpTruckWheelPad")
        {
            dynamicFriction = .02f,
            staticFriction = .02f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounciness = 0
        };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
#endif
