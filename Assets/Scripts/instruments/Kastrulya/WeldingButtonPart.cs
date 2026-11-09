using FishNet.Object;
using UnityEngine;

/// <summary>
/// Кнопка включения сварочного аппарата — часть без своего NetworkObject.
/// Наводишь камеру, F — включаешь и выключаешь. На кнопке нужен коллайдер.
///
/// Работает и с держателем в руке: WeldingTorchItem объявляет
/// AllowInteractWhileHeld, поэтому F по кнопке проходит, не выпуская предмет.
/// </summary>
public class WeldingButtonPart : MonoBehaviour, IVehiclePart
{
    [Tooltip("Номер части. НЕ должен совпадать с Body Part Id на станции, " +
             "иначе сервер найдёт не ту часть.")]
    [SerializeField] private int _partId = 0;

    private WeldingStation _station;
    private NetworkObject _owningNob;

    private void Awake()
    {
        _station = GetComponentInParent<WeldingStation>();
        _owningNob = GetComponentInParent<NetworkObject>();

        if (_station == null)
        {
            Debug.LogError($"[Кнопка] «{name}»: выше по иерархии нет WeldingStation", this);
            return;
        }

        if (GetComponent<Collider>() == null)
            Debug.LogError($"[Кнопка] «{name}»: нет коллайдера — луч по ней " +
                           "не попадёт, и нажать будет нельзя", this);

        // Кнопка НА ТОМ ЖЕ объекте, что и станция — тогда луч в любую часть
        // корпуса находит её через GetComponentInParent, и включается всё
        // подряд. Кнопка обязана быть ОТДЕЛЬНЫМ дочерним объектом со своим
        // коллайдером — иначе её нельзя отличить от корпуса.
        if (_station.gameObject == gameObject)
            Debug.LogError($"[Кнопка] «{name}» висит на ТОМ ЖЕ объекте, что и " +
                           "WeldingStation. Из-за этого включается нажатие по всему " +
                           "корпусу. Вынеси кнопку в дочерний объект со своим " +
                           "коллайдером.", this);
    }

    public NetworkObject OwningNetworkObject => _owningNob;
    public int PartId => _partId;

    public void ServerUse(PlayerInteractor interactor)
    {
        if (_station == null) return;
        _station.ServerTogglePower();
    }
}
