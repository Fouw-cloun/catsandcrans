using FishNet.Object;
using UnityEngine;

/// <summary>
/// Цель для F на гайке (часть без своего NetworkObject). Наводишь камеру на гайку,
/// жмёшь F с ключом в руке → ключ садится на эту гайку.
///
/// Вешается на объект с коллайдером РЯДОМ с гайкой (не под крутящимся пивотом,
/// иначе цель будет уезжать вместе с гайкой).
///
/// У каждой из 4 гаек — СВОЙ Part Id (0,1,2,3) и своя ссылка на HydrantNut.
/// </summary>
public class HydrantNutPart : MonoBehaviour, IVehiclePart
{
    [Tooltip("Уникальный id части в пределах гидранта (у 4 гаек — разные!)")]
    [SerializeField] private int _partId = 0;
    [Tooltip("Гайка, которой управляет эта цель")]
    [SerializeField] private HydrantNut _nut;

    private NetworkObject _owningNob;

    private void Awake()
    {
        if (_nut == null) _nut = GetComponentInParent<HydrantNut>();
        _owningNob = GetComponentInParent<NetworkObject>();

        ValidateSetup();
    }

    /// <summary>Самопроверка: ловим типовые ошибки настройки, из-за которых ключ
    /// садится всегда на ОДНУ И ТУ ЖЕ гайку.</summary>
    private void ValidateSetup()
    {
        if (_nut == null)
        {
            Debug.LogError($"[NutPart] '{name}': не назначена гайка (поле Nut)!", this);
            return;
        }

        if (_owningNob == null)
        {
            Debug.LogError($"[NutPart] '{name}': нет NetworkObject в родителях!", this);
            return;
        }

        // ГЛАВНАЯ ГРАБЛЯ: одинаковые Part Id у разных частей. Сервер ищет часть по
        // PartId и всегда находит ПЕРВУЮ — поэтому ключ садится не туда, куда целишься.
        foreach (HydrantNutPart other in _owningNob.GetComponentsInChildren<HydrantNutPart>(true))
        {
            if (other == this) continue;
            if (other.PartId == _partId)
            {
                Debug.LogError(
                    $"[NutPart] ДУБЛЬ Part Id = {_partId} у '{name}' и '{other.name}'! " +
                    $"Сервер найдёт ПЕРВУЮ часть, и ключ будет садиться всегда на неё. " +
                    $"Поставь у 4 гаек РАЗНЫЕ Part Id (0,1,2,3).", this);
            }
        }

        // Вторая грабля: две цели указывают на одну и ту же гайку.
        foreach (HydrantNutPart other in _owningNob.GetComponentsInChildren<HydrantNutPart>(true))
        {
            if (other == this) continue;
            if (other.Nut == _nut)
            {
                Debug.LogError(
                    $"[NutPart] '{name}' и '{other.name}' ссылаются на ОДНУ гайку " +
                    $"'{_nut.name}'! У каждой цели должна быть своя гайка.", this);
            }
        }
    }

    public NetworkObject OwningNetworkObject => _owningNob;
    public int PartId => _partId;

    /// <summary>Гайка, которой управляет эта цель (читает HydrantNut).</summary>
    public HydrantNut Nut => _nut;

    public void ServerUse(PlayerInteractor interactor)
    {
        if (_nut != null) _nut.ServerMountWrench(interactor);
    }
}
