using FishNet.Connection;
using UnityEngine;

/// <summary>
/// Контекст применения предмета (ЛКМ/ПКМ). Передаётся в методы предмета на
/// сервере, чтобы предмет знал, кто применяет и куда целится игрок.
/// Прицел (origin/direction) считается на клиенте-владельце и передаётся в RPC,
/// потому что сервер не знает наклон камеры игрока.
/// </summary>
public struct UseContext
{
    public NetworkConnection User;   // кто применяет
    public Vector3 Origin;           // точка прицела (позиция камеры)
    public Vector3 Direction;        // направление взгляда

    /// <summary>0..1 — сила удержания ЛКМ (только для предметов с ChargedPrimaryUse).
    /// Для обычных мгновенных применений всегда 1.</summary>
    public float Charge;

    /// <summary>Рука применяющего — собирается на сервере, по сети НЕ передаётся.
    /// Нужна предметам, которым требуется отсоединиться от руки (бросок).</summary>
    public PlayerHand Hand;

    public UseContext(NetworkConnection user, Vector3 origin, Vector3 direction, float charge, PlayerHand hand)
    {
        User = user;
        Origin = origin;
        Direction = direction;
        Charge = charge;
        Hand = hand;
    }
}
