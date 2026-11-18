using FishNet.Object;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CCConcreteTruckRearPart : MonoBehaviour, IVehiclePart
{
    public enum Action { Mix, Discharge, AimChute }
    public CCConcreteTruckVehicle truck;
    public Action action;
    public NetworkObject OwningNetworkObject => truck != null ? truck.NetworkObject : null;
    public int PartId => 310 + (int)action;

    public void ServerUse(PlayerInteractor interactor)
    {
        if (truck == null || interactor == null) return;
        var hit = GetComponent<Collider>();
        Vector3 closest = hit != null ? hit.ClosestPoint(interactor.transform.position) : transform.position;
        if ((closest - interactor.transform.position).sqrMagnitude > 3.8f * 3.8f) return;
        truck.ServerUseRearPart(action);
    }
}
