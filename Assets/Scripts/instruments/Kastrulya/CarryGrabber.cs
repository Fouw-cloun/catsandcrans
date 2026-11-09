using FishNet.Object;
using UnityEngine;

/// <summary>
/// ВЗЯТЬСЯ ЗА ТЯЖЁЛУЮ ПОСУДУ. Вешается на префаб ИГРОКА рядом с PlayerInteractor.
///
/// Смотришь на кастрюлю, жмёшь клавишу — взялся. Жмёшь ещё раз или отходишь —
/// отпустил. Руки при этом остаются свободными: посуда не занимает слот руки,
/// её несут за края.
///
/// Клавиша та же, что у подбора предметов, и это не конфликт: PlayerInteractor
/// ищет InteractableItem, а на совместной посуде его нет — только
/// CooperativeCarry. Пересечься они не могут.
///
/// Прицел считает КЛИЕНТ — сервер не знает наклон камеры.
/// </summary>
public class CarryGrabber : NetworkBehaviour
{
    [Header("Управление")]
    [Tooltip("Взяться и отпустить")]
    [SerializeField] private KeyCode _key = KeyCode.F;

    [Tooltip("ПОСТАВИТЬ РОВНО: кастрюля плавно опускается перед тобой на землю, " +
             "выравнивается и отпускается. Просто отпустить (той же клавишей " +
             "взятия) — уронить с высоты переноски.")]
    [SerializeField] private KeyCode _placeKey = KeyCode.R;

    [Tooltip("Дальность, метры")]
    [SerializeField] private float _reach = 2.5f;

    [Tooltip("Слой посуды")]
    [SerializeField] private LayerMask _potMask = ~0;

    private Camera _cam;
    private CooperativeCarry _holding;

    private void Update()
    {
        if (!base.IsOwner) return;

        // Отошёл — сервер отцепит сам, а мы забываем ссылку, чтобы следующее
        // нажатие снова бралось, а не пыталось отпустить.
        if (_holding != null && !_holding.Holds(base.NetworkObject)) _holding = null;

        // Поставить ровно — работает, только когда что-то несёшь.
        if (Input.GetKeyDown(_placeKey) && _holding != null)
        {
            // Смотрим на конфорку — ставим прямо на неё, а не на пол рядом.
            if (_cam != null &&
                Physics.Raycast(_cam.transform.position, _cam.transform.forward,
                                out RaycastHit stoveHit, _reach + 1f, _potMask,
                                QueryTriggerInteraction.Ignore))
            {
                StoveBurner burner = stoveHit.collider.GetComponentInParent<StoveBurner>();
                if (burner != null)
                {
                    CmdPlaceOnStove(_holding.NetworkObject, burner.NetworkObject);
                    _holding = null;
                    return;
                }
            }

            CmdPlace(_holding.NetworkObject);
            _holding = null;
            return;
        }

        if (!Input.GetKeyDown(_key)) return;

        if (_holding != null)
        {
            CmdRelease(_holding.NetworkObject);
            _holding = null;
            return;
        }

        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        if (!Physics.Raycast(_cam.transform.position, _cam.transform.forward,
                             out RaycastHit hit, _reach, _potMask,
                             QueryTriggerInteraction.Ignore))
            return;

        // Попали в КНОПКУ или другую часть техники — не хватаем, пусть
        // сработает нажатие. Иначе кнопка на переносимой коробке была бы
        // недоступна: F по ней всегда поднимал бы саму коробку.
        if (hit.collider.GetComponentInParent<IVehiclePart>() != null) return;

        CooperativeCarry carry = hit.collider.GetComponentInParent<CooperativeCarry>();
        if (carry == null) return;

        if (carry.Count >= carry.Required && !carry.Holds(base.NetworkObject))
        {
            Debug.Log("[Перенос] мест больше нет");
            return;
        }

        _holding = carry;
        CmdGrab(carry.NetworkObject);
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdGrab(NetworkObject target)
    {
        if (target == null) return;

        // Проверка на вменяемость присланных данных: хвататься за кастрюлю
        // на другом конце карты нельзя.
        if (Vector3.Distance(target.transform.position, transform.position) > _reach + 2f)
            return;

        CooperativeCarry carry = target.GetComponent<CooperativeCarry>();
        if (carry != null) carry.ServerGrab(base.NetworkObject);
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdPlace(NetworkObject target)
    {
        if (target == null) return;
        CooperativeCarry carry = target.GetComponent<CooperativeCarry>();
        if (carry == null || !carry.Holds(base.NetworkObject)) return;
        carry.ServerPlaceDown();
    }

    /// <summary>
    /// Поставить ношу прямо на конфорку. Все носильщики отцепляются, а
    /// конфорка выравнивает посуду сама.
    /// </summary>
    [ServerRpc(RequireOwnership = true)]
    private void CmdPlaceOnStove(NetworkObject target, NetworkObject stove)
    {
        if (target == null || stove == null) return;

        CooperativeCarry carry = target.GetComponent<CooperativeCarry>();
        if (carry == null || !carry.Holds(base.NetworkObject)) return;

        StoveBurner burner = stove.GetComponent<StoveBurner>();
        CookingVessel vessel = target.GetComponent<CookingVessel>();

        if (burner == null || vessel == null || !burner.IsFree)
        {
            carry.ServerPlaceDown();          // конфорка занята — ставим на пол
            return;
        }

        carry.ServerReleaseAll();
        burner.ServerPlace(vessel);
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdRelease(NetworkObject target)
    {
        if (target == null) return;
        CooperativeCarry carry = target.GetComponent<CooperativeCarry>();
        if (carry != null) carry.ServerRelease(base.NetworkObject);
    }
}
