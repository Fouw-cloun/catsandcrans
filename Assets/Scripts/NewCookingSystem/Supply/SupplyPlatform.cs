using UnityEngine;

namespace CatCooks.Food.Supply
{
    /// <summary>
    /// v24.0: ПЛОЩАДКА ПОСТАВКИ — большой стальной поддон, на который ставят ящики и крупное (мешки, вёдра, туши).
    /// Пока ставится сразу на этаж; потом её будет поднимать кран (по углам — проушины под стропы).
    /// Ось площадки: X — длина, Z — ширина, низ — y = 0, верх настила — DeckHeight.
    /// Зоны (в метрах от центра): слева ящики рядами, справа — крупное. Собирает меню CatCooks → Доставка → 1.
    /// </summary>
    public sealed class SupplyPlatform : MonoBehaviour
    {
        public Vector2 DeckSize = new Vector2(10f, 7f);   // v24.5: больше — под ящики трёх размеров и погрузчик
        public float DeckHeight = 0.22f;
        [Tooltip("Зона ящиков (x, z, ширина по X, глубина по Z)")]
        public Rect CrateZone = new Rect(-4.8f, -3.3f, 7.0f, 6.6f);
        [Tooltip("Зона крупного: мешки, вёдра, туши")]
        public Rect PalletZone = new Rect(2.4f, -3.3f, 2.4f, 6.6f);

        /// <summary>Точка на верхе настила (x, z — в метрах площадки).</summary>
        public Vector3 Top(float x, float z) => transform.TransformPoint(new Vector3(x, DeckHeight, z));

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            Gizmos.DrawWireCube(new Vector3(CrateZone.center.x, DeckHeight + 0.3f, CrateZone.center.y), new Vector3(CrateZone.width, 0.6f, CrateZone.height));
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireCube(new Vector3(PalletZone.center.x, DeckHeight + 0.3f, PalletZone.center.y), new Vector3(PalletZone.width, 0.6f, PalletZone.height));
        }
    }
}
