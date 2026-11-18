using UnityEngine;

/// <summary>Aligns the visible cylinder and rod along the line between their hinge pins.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(160)]
public sealed class CCBucketCylinder : MonoBehaviour
{
    [SerializeField] private Transform barrel;
    [SerializeField] private Transform rod;
    [SerializeField] private Transform barrelPin;
    [SerializeField] private Transform rodPin;
    [Tooltip("Local mesh axis, directed from the barrel pin toward the rod pin.")]
    [SerializeField] private Vector3 barrelAxis = Vector3.forward;
    [Tooltip("Local mesh axis, directed from the rod pin toward the barrel pin.")]
    [SerializeField] private Vector3 rodAxis = Vector3.forward;
    [SerializeField, HideInInspector] private Quaternion barrelRest;
    [SerializeField, HideInInspector] private Quaternion rodRest;

    public void Bind(Transform bodyMesh, Transform rodMesh, Transform bodyMount, Transform rodMount,
        Vector3 bodyMeshAxis, Vector3 rodMeshAxis)
    {
        barrel = bodyMesh;
        rod = rodMesh;
        barrelPin = bodyMount;
        rodPin = rodMount;
        barrelAxis = bodyMeshAxis;
        rodAxis = rodMeshAxis;
        barrelRest = barrel.localRotation;
        rodRest = rod.localRotation;
    }

    private void LateUpdate()
    {
        if (barrel == null || rod == null || barrelPin == null || rodPin == null) return;
        Vector3 line = rodPin.position - barrelPin.position;
        if (line.sqrMagnitude < .000001f) return;
        Align(barrel, barrelPin.position, line, barrelRest, barrelAxis);
        Align(rod, rodPin.position, -line, rodRest, rodAxis);
    }

    private static void Align(Transform part, Vector3 pin, Vector3 worldLine, Quaternion rest, Vector3 meshAxis)
    {
        var parent = part.parent;
        Vector3 direction = parent == null ? worldLine : parent.InverseTransformVector(worldLine);
        Vector3 restDirection = rest * Vector3.Scale(part.localScale, meshAxis);
        if (restDirection.sqrMagnitude == 0 || direction.sqrMagnitude == 0) return;
        part.SetPositionAndRotation(pin, part.rotation);
        part.localRotation = Quaternion.FromToRotation(restDirection.normalized, direction.normalized) * rest;
    }
}
