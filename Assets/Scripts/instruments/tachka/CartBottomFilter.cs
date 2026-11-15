using UnityEngine;
using CatCooks.Food;

/// <summary>
/// v19.6: КАКИЕ МЕШИ СЧИТАТЬ «НИЗОМ» ТАЧКИ.
/// PushCart (посадочная высота) и CartRig (низ модели) мерят низ по всем Renderer в иерархии.
/// Когда тачка стала тарой, в ней появились меши жидкости и горки (LiquidAnchor, HeapAnchor),
/// частицы, кучки — их границы бывают где угодно (меш ещё не собран, лежит в нуле мира) —
/// «низ» уезжал вниз на метры, и тачку ставили высоко: она взлетала при посадке, тряслась и скользила.
/// Считаем только настоящие меши самой тачки.
/// </summary>
public static class CartBottomFilter
{
    public static bool Counts(Renderer r)
    {
        if (r == null) return false;
        if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) return false;      // частицы, линии, следы — нет
        if (r.GetComponentInParent<CookwareLiquidVisual>() != null) return false;     // жидкость тары
        if (r.GetComponentInParent<FoodHeapRenderer>() != null) return false;         // горка/кучка
        if (r.GetComponentInParent<CookwareHeapVisual>() != null) return false;
        Transform t = r.transform;
        while (t != null)
        {
            string n = t.name;
            if (n == "LiquidAnchor" || n == "HeapAnchor" || n == "SubstanceHeap (auto)" || n.StartsWith("EggSplat") || n.StartsWith("YolkFly")) return false;
            t = t.parent;
        }
        return true;
    }
}
