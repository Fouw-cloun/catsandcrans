using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Метка куска НОВОЙ туши (меню «CatCooks → Мясо → Новые туши»): какой вид и какая часть.
    /// Висит на дочернем объекте туши. Туша по ней понимает, какой кусок выпадет (Meat Cut).
    /// Руками не трогать — ставит меню.
    /// </summary>
    public sealed class MeatPartTag : MonoBehaviour
    {
        public MeatSpecies Species;
        public MeatPart Part;
    }
}
