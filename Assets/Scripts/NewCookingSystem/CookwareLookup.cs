using FishNet.Object;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Тара по сетевому объекту. Обычно Cookware — на корне (ведро, кастрюля), но у машин
    /// (ковш экскаватора) NetworkObject — на всей машине, а Cookware — на дочернем ковше.
    /// Раньше инструменты смотрели только корень и «не видели» ковш: отбойник не долбил,
    /// ведро не черпало. Теперь — корень, а нет там — ближайшая тара среди детей.
    /// </summary>
    public static class CookwareLookup
    {
        public static Cookware Of(NetworkObject nob, Vector3 near)
        {
            if (nob == null) return null;
            Cookware c = nob.GetComponent<Cookware>();
            if (c != null) return c;
            float best = float.MaxValue;
            foreach (Cookware x in nob.GetComponentsInChildren<Cookware>(true))
            {
                if (x == null) continue;
                float d = (x.MouthPoint - near).sqrMagnitude;
                if (d < best) { best = d; c = x; }
            }
            return c;
        }
    }
}
