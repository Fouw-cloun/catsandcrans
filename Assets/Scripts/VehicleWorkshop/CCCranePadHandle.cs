using FishNet.Object;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CCCranePadHandle : MonoBehaviour, IVehiclePart
{
    public CCWorkshopCraneChassis chassis;
    public bool deploy;
    public Vector3 hingeAxis = Vector3.right;

    public NetworkObject OwningNetworkObject => chassis ? chassis.NetworkObject : null;
    public int PartId => deploy ? 901 : 902;
    private Quaternion rest;

    private void Awake() => rest = transform.localRotation;

    public void ServerUse(PlayerInteractor player)
    {
        if (!chassis || !player || !chassis.IsServerInitialized || !chassis.state) return;
        if (chassis.HasDriver || chassis.operatorSeat.HasDriver || chassis.state.IsDeployed == deploy) return;
        // PlayerInteractor validates the owning machine. Validate this handle's reach too.
        float reach = player.Reach + 1.2f;
        if ((player.transform.position-transform.position).sqrMagnitude > reach*reach) return;
        chassis.state.ServerToggle();
    }

    private void Update()
    {
        if (!chassis || !chassis.state) return;
        float angle = chassis.state.IsDeployed == deploy ? 22f : 0f;
        transform.localRotation = Quaternion.RotateTowards(transform.localRotation,
            rest * Quaternion.AngleAxis(angle,hingeAxis), 110f * Time.deltaTime);
    }
}
