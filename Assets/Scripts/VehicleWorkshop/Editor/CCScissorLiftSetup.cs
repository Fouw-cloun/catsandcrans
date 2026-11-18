#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using W = CCWorkshopEditor;

public static class CCScissorLiftSetup
{
    [MenuItem("CatCooks/Vehicle Workshop/05 Create scissor lift")]
    public static void Create()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Выйди из Play Mode.");
        var selection = Selection.activeTransform;
        Vector3 position = selection != null ? selection.position + selection.right * 3f :
            SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        var root = W.BlueprintModel("ScissorLift");
        try
        {
            var lift = W.Add<CCScissorLiftVehicle>(root);
            var nodes = root.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Wheel_", StringComparison.Ordinal)).ToList();
            if (nodes.Count != 4) throw new InvalidOperationException("Модель должна содержать четыре колеса.");
            W.Wheels(lift, nodes, 1050);
            lift.maxForwardSpeed = 2.4f;
            lift.maxReverseSpeed = 1.2f;
            lift.tractionAcceleration = .8f;
            W.Set(lift, "_centerOfMass", new Vector3(0, .3f, 0));

            lift.platform = Part(root.transform, "Platform");
            lift.bars = Enumerable.Range(0, 12).Select(i => Part(root.transform, "ScissorBar" + i)).ToArray();
            lift.DrawPose(lift.minHeight);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (renderer.name == "Chassis" || renderer.name == "Deck" ||
                    renderer.name.StartsWith("Rail_", StringComparison.Ordinal) || renderer.name.StartsWith("Step_", StringComparison.Ordinal))
                    W.Add<BoxCollider>(renderer.gameObject);
            }

            var pads = W.Child(root.transform, "WorkshopWheelBumpStops", Vector3.zero);
            var material = SlideMaterial();
            for (int i = 0; i < lift.wheels.Length; i++)
            {
                Vector3 center = lift.wheels[i].anchor.localPosition - Vector3.up * (lift.restLength - .14f);
                var pad = W.Child(pads, "Pad" + i, center);
                var sphere = W.Add<SphereCollider>(pad.gameObject);
                sphere.radius = Mathf.Max(.08f, lift.wheelRadius - .07f);
                sphere.sharedMaterial = material;
            }

            W.Seat(lift, Part(root.transform, "SeatPoint"), Part(root.transform, "ExitPoint"), Part(root.transform, "CameraPoint"));
            var passenger = W.Add<CCLiftPassenger>(lift.platform.gameObject);
            passenger.halfDeck = lift.platformHalfSize;
            CCScissorLiftConsoleSetup.Configure(lift);
            root.transform.position = position;
            W.SavePrototype(root, "ScissorLift_Workshop");
        }
        catch
        {
            if (root != null) Undo.DestroyObjectImmediate(root);
            throw;
        }
    }

    private static Transform Part(Transform root, string name)
    {
        var t = W.Find(root, name);
        if (t == null) throw new InvalidOperationException("В модели нет " + name);
        return t;
    }

    private static PhysicsMaterial SlideMaterial()
    {
        Directory.CreateDirectory(W.Generated);
        AssetDatabase.Refresh();
        string path = W.Generated + "ScissorLiftWheelStop.physicMaterial";
        var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (mat != null) return mat;
        mat = new PhysicsMaterial("ScissorLiftWheelStop")
        {
            staticFriction = .025f,
            dynamicFriction = .025f,
            frictionCombine = PhysicsMaterialCombine.Minimum
        };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
#endif
