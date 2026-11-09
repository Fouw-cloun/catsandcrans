using FishNet.Object;
using UnityEngine;

/// <summary>
/// ТО, ПО ЧЕМУ МОЖНО ЛЕЗТЬ. Зона лазания (LadderClimbZone) работает только
/// через этот интерфейс — поэтому одна и та же зона обслуживает и старую
/// стремянку (LadderItem), и раскладную лестницу (FoldingLadderItem).
/// </summary>
public interface IClimbable
{
    /// <summary>Можно ли этому игроку лезть прямо сейчас.</summary>
    bool CanClimb(NetworkObject player);

    /// <summary>Нижний конец лестницы в мире.</summary>
    Vector3 ClimbBase { get; }

    /// <summary>Куда лезть (единичный вектор от нижнего конца к верхнему).</summary>
    Vector3 ClimbAxis { get; }

    /// <summary>Длина лестницы, метры.</summary>
    float CurrentLength { get; }

    /// <summary>Наклон к горизонту: 90 — стоит, 0 — лежит.</summary>
    float AngleFromGround { get; }

    /// <summary>Кто-то висит на лестнице (не складывать, не поднимать).</summary>
    bool AnyoneClimbing { get; set; }
}
