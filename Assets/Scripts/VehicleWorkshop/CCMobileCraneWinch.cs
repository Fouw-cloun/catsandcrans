using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Визуальная подвеска автокрана. Длину задаёт сетевое место оператора.</summary>
[DisallowMultipleComponent]
public class CCMobileCraneWinch : MonoBehaviour
{
    public const float ParkedLength = .38f;
    public CraneHook physicalHook;
    public CraneCableVisual braidedCable;
    public float Length => requestedLength;
    public Transform upperBlock;
    public Transform tower;
    public Transform hook;
    public LineRenderer cable;
    public Transform sheave;
    public Quaternion restingSheaveRotation;
    public Vector3 sheaveAxis;
    public float sheaveRadius;
    public float eyeToBottom;
    [SerializeField] private LayerMask floorLayers = ~0;

    private readonly RaycastHit[] hits = new RaycastHit[64];
    private float requestedLength = ParkedLength;

    private void FixedUpdate()
    {
        if (physicalHook && physicalHook.IsServerInitialized && upperBlock)
            physicalHook.ServerSetSuspension(upperBlock.position, requestedLength);
    }

    public void RequestLength(float metres) => requestedLength = Mathf.Max(0f, metres);

    // The ray covers the whole hook as well as the visible cable.
    public float Clearance(float metres)
    {
        if (physicalHook || !upperBlock) return metres;
        float padding = eyeToBottom + .025f;
        var physicsScene = gameObject.scene.GetPhysicsScene();
        int count = physicsScene.Raycast(upperBlock.position, Vector3.down, hits,
            metres + padding, floorLayers, QueryTriggerInteraction.Ignore);
        float available = metres;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.normal.y <= .1f || hit.transform.IsChildOf(transform)) continue;
            available = Mathf.Min(available, Mathf.Max(0f, hit.distance - padding));
        }
        return available;
    }

    private void LateUpdate() => Draw(requestedLength);

    public void Draw(float metres)
    {
        if (physicalHook)
        {
            if (braidedCable) braidedCable.Length = metres;
            SpinSheave(metres);
            return;
        }
        if (!upperBlock || !tower || !hook || !cable) return;
        float length = Clearance(metres);
        Vector3 top = upperBlock.position;
        Vector3 bottom = top + Vector3.down * length;
        Vector3 forward = Vector3.ProjectOnPlane(tower.forward, Vector3.up);
        hook.position = bottom;
        if (forward.sqrMagnitude > .0001f)
            hook.rotation = Quaternion.LookRotation(forward, Vector3.up);

        // Cable points follow the truck's interpolated root in the same coordinate space.
        cable.SetPosition(0, cable.transform.InverseTransformPoint(top));
        cable.SetPosition(1, cable.transform.InverseTransformPoint(bottom));
        SpinSheave(length);
    }

    private void SpinSheave(float length)
    {
        if (sheave && sheaveRadius > .001f)
        {
            float degrees = (2f - length) / sheaveRadius * Mathf.Rad2Deg;
            sheave.localRotation = restingSheaveRotation * Quaternion.AngleAxis(degrees, sheaveAxis);
        }
    }
}
