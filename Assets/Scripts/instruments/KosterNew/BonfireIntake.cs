using FishNet;
using UnityEngine;

// В пространстве имён НАМЕРЕННО: в проекте ещё живёт старая система.
namespace CatCooks.Food.Tools
{
    /// <summary>
    /// ЗОНА ПРИЁМА КОСТРИЩА. Триггер над ямой: всё с BonfireFuel, что в него
    /// упало (не в руках), уходит в стопку. Отдельный компонент, потому что
    /// Unity шлёт события триггера только объекту с коллайдером.
    ///
    /// Вешать на дочерний объект с BoxCollider (Is Trigger) над кострищем.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class BonfireIntake : MonoBehaviour
    {
        [SerializeField] private BonfirePit _pit;

        private void Awake()
        {
            if (_pit == null) _pit = GetComponentInParent<BonfirePit>();
            Collider c = GetComponent<Collider>();
            if (!c.isTrigger) Debug.LogWarning($"[Костёр] «{name}»: коллайдер приёма должен быть Is Trigger", this);
        }

        private void OnTriggerEnter(Collider other) => Feed(other);
        private void OnTriggerStay(Collider other) => Feed(other);

        private void Feed(Collider other)
        {
            if (_pit == null || !InstanceFinder.IsServerStarted) return;
            BonfireFuel f = other.GetComponentInParent<BonfireFuel>();
            // Доску в руках не берём: у PickupItem в руке коллайдеры выключены,
            // так что сюда она и не попадёт, — это страховка.
            if (f != null && !f.Consumed) _pit.ServerAccept(f);
        }
    }
}
