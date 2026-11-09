using CatCooks.Food;
using FishNet;
using UnityEngine;

// В пространстве имён НАМЕРЕННО: в проекте ещё живёт старая система со
// своими глобальными FoodState и FoodTransition. Из глобального
// пространства компилятор взял бы их вместо новых (CatCooks.Food).
namespace CatCooks.Food.Tools
{
    /// <summary>
    /// ВОРОНКА ИЗМЕЛЬЧИТЕЛЯ. Триггер внутри воронки передаёт станку всё, что
    /// в него упало. Отдельный компонент, потому что Unity шлёт OnTriggerEnter
    /// только объекту с коллайдером, а коллайдер воронки — дочерний.
    ///
    /// Вешать на дочерний объект с BoxCollider (Is Trigger) внутри раструба.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ShredderHopper : MonoBehaviour
    {
        [SerializeField] private ShredderStation _station;

        private void Awake()
        {
            if (_station == null) _station = GetComponentInParent<ShredderStation>();
            Collider c = GetComponent<Collider>();
            if (!c.isTrigger)
                Debug.LogWarning($"[Воронка] «{name}»: коллайдер должен быть Is Trigger", this);
        }

        private void OnTriggerEnter(Collider other) => Feed(other);

        // Продукт мог влететь, пока его держали, и отпущен уже внутри —
        // второго Enter не будет, ловим на Stay.
        private void OnTriggerStay(Collider other) => Feed(other);

        private void Feed(Collider other)
        {
            if (_station == null || !InstanceFinder.IsServerStarted) return;
            FoodPiece p = other.GetComponentInParent<FoodPiece>();
            if (p != null && !p.IsHeld && !p.IsConsumed) _station.ServerAccept(p);
        }
    }
}
