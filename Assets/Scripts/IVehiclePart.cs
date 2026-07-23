using FishNet.Object;

/// <summary>
/// Интерактивная ЧАСТЬ техники, у которой НЕТ своего NetworkObject (рычаг,
/// кнопка, тумблер на корпусе). Сетевую идентичность ей даёт родительская
/// машина: PlayerInteractor находит часть по raycast, поднимается к её
/// NetworkObject (OwningNetworkObject) и передаёт серверу, что взаимодействовали
/// именно с этой частью. Сервер вызывает ServerUse() на части.
///
/// Так часть может быть дочерней к машине без вложенного NetworkObject
/// (который ломает спавн — ошибка nested spawn / ComponentIndex 1).
/// </summary>
public interface IVehiclePart
{
    /// <summary>NetworkObject машины-владельца (берётся из родителя).</summary>
    NetworkObject OwningNetworkObject { get; }

    /// <summary>Уникальный id части в пределах машины (если частей несколько).</summary>
    int PartId { get; }

    /// <summary>Вызывается на сервере, когда игрок применил F к этой части.</summary>
    void ServerUse(PlayerInteractor interactor);
}
