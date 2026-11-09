using UnityEngine;

// В пространстве имён НАМЕРЕННО: в проекте ещё живёт старая система.
namespace CatCooks.Food.Tools
{
    /// <summary>
    /// То, что можно бить кувалдой, кроме еды: поддоны, ящики, кирпичные стенки.
    /// Кувалда в момент удара находит это под прицелом и зовёт на сервере.
    /// </summary>
    public interface IHammerTarget
    {
        /// <summary>Удар кувалдой. point — куда попали, dir — направление удара.</summary>
        void ServerHammerHit(Vector3 point, Vector3 dir);
    }
}
