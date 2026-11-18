using FishNet.Object;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CCScissorLiftPanel : MonoBehaviour, IVehiclePart
{
    public CCScissorLiftVehicle lift;
    public bool recovery;
    public int slot;

    public NetworkObject OwningNetworkObject => lift != null ? lift.NetworkObject : null;
    public int PartId => 520 + slot;

    public void ServerUse(PlayerInteractor interactor)
    {
        if (lift == null || interactor == null || !lift.IsServerInitialized) return;
        if (recovery)
        {
            if ((interactor.transform.position - transform.position).sqrMagnitude <= 3.35f * 3.35f)
                lift.ServerTryRightLift();
        }
        else
        {
            lift.ServerUseConsole(interactor);
        }
    }
}
