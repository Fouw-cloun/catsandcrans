using UnityEngine;

// В пространстве имён НАМЕРЕННО: в проекте ещё живёт старая система.
namespace CatCooks.Food.Tools
{
    /// <summary>
    /// ТОПЛИВО ДЛЯ КОСТРИЩА. Метка на доске (и на любом, что горит).
    /// Сама ничего не делает: кострище видит её в зоне приёма, забирает и
    /// кладёт в стопку.
    ///
    /// Вешать на префаб доски рядом с PickupItem (доску носят в руках).
    /// </summary>
    public sealed class BonfireFuel : MonoBehaviour
    {
        [Tooltip("Сколько досок в стопке даёт этот предмет. Доска — 1, кусок поддона — 2–3.")]
        [Range(1, 10)][SerializeField] private int _planks = 1;

        public int Planks => _stack != null ? _stack.Count : _planks;

        private PlankStackItem _stack;
        private void Awake() => _stack = GetComponent<PlankStackItem>();

        /// <summary>Кострище забрало часть стопки — оставить остаток. Сервер.</summary>
        public void ServerSetPlanks(int n)
        {
            if (_stack != null) _stack.ServerSetCount(n);
            else _planks = Mathf.Max(1, n);
        }

        /// <summary>Уже забрано кострищем (чтобы не посчитать дважды за кадр).</summary>
        public bool Consumed { get; set; }
    }
}
