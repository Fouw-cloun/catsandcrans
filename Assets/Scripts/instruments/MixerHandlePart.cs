using FishNet.Object;
using UnityEngine;

/// <summary>
/// Ручка бетономешалки (часть без своего NetworkObject). Наводишь камеру, жмёшь F
/// — захватываешь ручку; дальше, ПОКА держишь F, движение камеры влево/вправо
/// поворачивает бочку (это читает уже сам ConcreteMixer у грабера). Отпустил F —
/// ручка освобождается.
///
/// Нужен коллайдер на объекте ручки (в слое F-рейкаста). Part Id — отличный от
/// кнопок.
/// </summary>
public class MixerHandlePart : MonoBehaviour, IVehiclePart
{
    [Tooltip("Уникальный id части (отличный от кнопок)")]
    [SerializeField] private int _partId = 2;

    private ConcreteMixer _mixer;
    private NetworkObject _owningNob;

    private void Awake()
    {
        _mixer = GetComponentInParent<ConcreteMixer>();
        _owningNob = GetComponentInParent<NetworkObject>();
    }

    public NetworkObject OwningNetworkObject => _owningNob;
    public int PartId => _partId;

    public void ServerUse(PlayerInteractor interactor)
    {
        if (_mixer != null) _mixer.ServerGrab(interactor);
    }
}
