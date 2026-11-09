using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЧИСТАЯ ПОВЕРХНОСТЬ для еды: подстилка (рулонная гидроизоляция), стол, доска.
    /// Еда, упавшая на объект с этим компонентом (или на его детей), не пачкается, как на полу.
    /// Ставится автоматически на полосы рулона; на свой стол можно повесить руками.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FoodCleanSurface : MonoBehaviour
    {
        [Tooltip("Подсказка в логе еды: «упало на …»")]
        public string Title = "подстилка";
    }
}
