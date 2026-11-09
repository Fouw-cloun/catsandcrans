using FishNet.Object;
using UnityEngine;

/// <summary>
/// СНЯТЬ КРЫШКУ ВЗГЛЯДОМ. Вешается на префаб ИГРОКА, рядом с PlayerInteractor.
///
/// Смотришь на кастрюлю, жмёшь клавишу — крышка снята, давление стравливается.
/// Ни брать посуду в руки, ни подходить вплотную не нужно.
///
/// ЗАЧЕМ ОТДЕЛЬНО ОТ PotItem: спасаться от разрыва надо быстро, а раскалённая
/// кастрюля под давлением — последнее, что хочется хватать руками. Плюс это
/// работает и со стационарными чанами, которые вообще не поднимаются.
///
/// Прицел считает КЛИЕНТ — сервер не знает наклон камеры. Тот же приём,
/// что в твоём UseContext.
/// </summary>
public class LidRemover : NetworkBehaviour
{
    [Header("Управление")]
    [Tooltip("Клавиша снятия крышки")]
    [SerializeField] private KeyCode _key = KeyCode.E;

    [Tooltip("Дальность, метры")]
    [SerializeField] private float _reach = 3f;

    [Tooltip("Слой посуды")]
    [SerializeField] private LayerMask _potMask = ~0;

    private Camera _cam;

    private void Update()
    {
        if (!base.IsOwner) return;
        if (!Input.GetKeyDown(_key)) return;

        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        if (!Physics.Raycast(_cam.transform.position, _cam.transform.forward,
                             out RaycastHit hit, _reach, _potMask,
                             QueryTriggerInteraction.Ignore))
        {
            Debug.Log("[Крышка] под прицелом ничего нет — проверь Pot Mask");
            return;
        }

        PotLidSocket socket = hit.collider.GetComponentInParent<PotLidSocket>();
        if (socket == null)
        {
            Debug.Log($"[Крышка] на «{hit.collider.name}» нет PotLidSocket");
            return;
        }

        if (!socket.HasLid)
        {
            Debug.Log("[Крышка] уже снята");
            return;
        }

        CmdTakeLid(socket.NetworkObject);
    }

    [ServerRpc(RequireOwnership = true)]
    private void CmdTakeLid(NetworkObject target)
    {
        if (target == null) return;

        // Проверка на вменяемость присланных данных: снимать крышку с кастрюли
        // на другом конце карты нельзя.
        if (Vector3.Distance(target.transform.position, transform.position) > _reach + 2f)
            return;

        PotLidSocket socket = target.GetComponent<PotLidSocket>();
        if (socket == null || !socket.HasLid) return;

        socket.ServerTakeLid();
        Debug.Log("[Крышка] снята руками, давление стравливается");
    }
}
