using FishNet.Object;
using UnityEngine;

/// <summary>
/// Кнопка включения базы пылесоса (часть без своего NetworkObject). Наводишь
/// камеру, F — включаешь/выключаешь компрессор. На кнопке должен быть коллайдер.
/// </summary>
public class VacuumButtonPart : MonoBehaviour, IVehiclePart
{
    [SerializeField] private int _partId = 0;

    private VacuumStation _station;
    private NetworkObject _owningNob;

    private void Awake()
    {
        _station = GetComponentInParent<VacuumStation>();
        _owningNob = GetComponentInParent<NetworkObject>();
    }

    public NetworkObject OwningNetworkObject => _owningNob;
    public int PartId => _partId;

    public void ServerUse(PlayerInteractor interactor)
    {
        if (_station != null) _station.ServerToggleCompressor();
    }
}
