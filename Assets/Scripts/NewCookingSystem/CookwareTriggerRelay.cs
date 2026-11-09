using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ПЕРЕСЫЛКА ТРИГГЕРА ПРИЁМА В ТАРУ. Вешается на объект Receive (Cookware Auto Setup ставит сам).
    ///
    /// Зачем: Unity шлёт OnTriggerEnter объекту с триггером и объекту с Rigidbody, но не
    /// промежуточным. Ковш экскаватора — дочерний объект машины, Rigidbody у корня машины,
    /// и Cookware на ковше событий «в меня бросили продукт» не получил бы. Ведро/кастрюля
    /// с собственным Rigidbody получают их и так — тогда пересылка молчит (без двойного приёма).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class CookwareTriggerRelay : MonoBehaviour
    {
        [Tooltip("Тара. Пусто — ближайшая Cookware выше по иерархии")]
        [SerializeField] private Cookware _cookware;

        private Collider _col;

        private void Awake()
        {
            if (_cookware == null) _cookware = GetComponentInParent<Cookware>();
            _col = GetComponent<Collider>();
        }

        /// <summary>Нужна ли пересылка: Cookware не на этом объекте и не на объекте с Rigidbody этого триггера.</summary>
        private bool Needed
        {
            get
            {
                if (_cookware == null || _cookware.gameObject == gameObject) return false;
                Rigidbody rb = _col != null ? _col.attachedRigidbody : null;
                return rb == null || rb.gameObject != _cookware.gameObject;
            }
        }

        private void OnTriggerEnter(Collider other) { if (Needed) _cookware.RelayTriggerEnter(other); }
        private void OnTriggerStay(Collider other) { if (Needed) _cookware.RelayTriggerStay(other); }
        private void OnTriggerExit(Collider other) { if (Needed) _cookware.RelayTriggerExit(other); }
    }
}
