using FishNet.Object;
using UnityEngine;

/// <summary>
/// ВРЕМЕННЫЙ отладочный инструмент. Вешается на префаб ИГРОКА рядом с
/// PlayerInteractor.
///
/// Позволяет проверить всю цепочку обработок, пока настоящие нож и болгарка
/// ещё не написаны: смотришь на продукт, жмёшь цифру — обработка применяется.
///
///   1 — обработка A (например «чистится»)
///   2 — обработка B (например «режется кубиком»)
///   3 — обработка C (свободный слот)
///   4 — сделать гнилым / вернуть обратно
///
/// В релиз не идёт. Когда появятся инструменты, они будут вызывать тот же
/// самый FoodItem.ServerApplyProcess — точка входа одна.
/// </summary>
public class FoodDebugTool : NetworkBehaviour
{
    [Header("Обработки на клавиши 1-3")]
    [SerializeField] private ProcessDef _slot1;
    [SerializeField] private ProcessDef _slot2;
    [SerializeField] private ProcessDef _slot3;

    [Header("Прицел")]
    [SerializeField] private float _reach = 3f;
    [SerializeField] private LayerMask _mask = ~0;

    [Tooltip("Качество, с которым применяется обработка. 1 = идеально.")]
    [Range(0f, 1f)][SerializeField] private float _quality = 1f;

    private Camera _cam;

    private void Update()
    {
        if (!base.IsOwner) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) TryUse(_slot1);
        else if (Input.GetKeyDown(KeyCode.Alpha2)) TryUse(_slot2);
        else if (Input.GetKeyDown(KeyCode.Alpha3)) TryUse(_slot3);
        else if (Input.GetKeyDown(KeyCode.Alpha4)) TryRot();
    }

    /// <summary>Прицел считает КЛИЕНТ — сервер не знает наклон камеры.
    /// Это ровно тот же приём, что у тебя в UseContext.</summary>
    private bool AimAtFood(out NetworkObject target)
    {
        target = null;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return false;

        Ray ray = new Ray(_cam.transform.position, _cam.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, _reach, _mask,
                             QueryTriggerInteraction.Ignore))
            return false;

        FoodItem item = hit.collider.GetComponentInParent<FoodItem>();
        if (item == null || !item.IsSpawned) return false;

        target = item.NetworkObject;
        return true;
    }

    private void TryUse(ProcessDef process)
    {
        if (process == null) return;
        // По сети шлём ID, а НЕ ссылку на ScriptableObject: FishNet не умеет
        // сериализовать ассеты в RPC — на клиенте пришёл бы null.
        if (AimAtFood(out NetworkObject target)) ServerApply(target, process.Id, _quality);
    }

    private void TryRot()
    {
        if (AimAtFood(out NetworkObject target)) ServerToggleRot(target);
    }

    [ServerRpc]
    private void ServerApply(NetworkObject target, ushort processId, float quality)
    {
        if (target == null) return;
        FoodItem item = target.GetComponent<FoodItem>();
        if (item == null) return;

        ProcessDef process = FoodRegistry.Instance != null
            ? FoodRegistry.Instance.GetProcess(processId) : null;
        if (process == null)
        {
            Debug.LogError($"[FoodDebugTool] Обработка id={processId} не найдена в реестре. " +
                           "Пересобери реестр: меню CatCooks -> Пересобрать реестр еды.");
            return;
        }

        bool ok = item.ServerApplyProcess(process, quality);
        if (!ok)
            Debug.Log($"[FoodDebugTool] {process.DisplayName} не применилась к " +
                      $"{item.name} — не выполнено условие перехода.");
    }

    [ServerRpc]
    private void ServerToggleRot(NetworkObject target)
    {
        if (target == null) return;
        FoodItem item = target.GetComponent<FoodItem>();
        if (item != null) item.ServerSetRotten();
    }
}
