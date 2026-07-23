using FishNet.Object;
using UnityEngine;

/// <summary>
/// Кнопка бетономешалки (часть без своего NetworkObject). Наводишь камеру, F —
/// пуск или стоп барабана. Сделай ДВЕ штуки: зелёную (Turn On = вкл) и красную
/// (Turn On = выкл), с РАЗНЫМИ Part Id.
///
/// Нужен коллайдер на объекте кнопки (в слое, по которому бьёт F-рейкаст).
/// </summary>
public class MixerButtonPart : MonoBehaviour, IVehiclePart
{
    [Tooltip("Уникальный id части в пределах мешалки (у зелёной и красной — разные)")]
    [SerializeField] private int _partId = 0;
    [Tooltip("ВКЛ = зелёная (пуск барабана). ВЫКЛ = красная (стоп).")]
    [SerializeField] private bool _turnOn = true;

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
        if (_mixer != null) _mixer.ServerSetSpinning(_turnOn);
    }
}
