using UnityEngine;

/// <summary>
/// КАКИЕ МЕШИ — КОРПУС КАСТРЮЛИ. Нужен, когда в Pot Damage (Shell) или в
/// Deformable Mesh (Targets) поле оставили пустым и компонент собирает всё из детей.
///
/// Без фильтра туда попадали вода (LiquidAnchor), горка, градусник, пар и т. п.:
/// дыры «перекрашивали» воду и градусник шейдером повреждений (градусник пропадал,
/// вода становилась плоской), а деформация мяла сам градусник.
/// </summary>
public static class PotBodyFilter
{
    public static bool IsBody(Component r, Transform root)
    {
        if (r == null) return false;
        if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) return false;
        if (r.GetComponentInParent<CatCooks.Food.CookwareLiquidVisual>(true) != null) return false;
        if (r.GetComponentInParent<CatCooks.Food.CookwareHeapVisual>(true) != null) return false;
        if (r.GetComponentInParent<CatCooks.Food.FoodHeapRenderer>(true) != null) return false;
        if (r.GetComponentInParent<CatCooks.Food.BoilFx>(true) != null) return false;
        if (r.GetComponentInParent<PotLidItem>(true) != null) return false;
        if ((r.gameObject.hideFlags & HideFlags.DontSave) != 0) return false;

        // Градусник, авто-объекты и прочее — по имени в цепочке родителей до корня.
        for (Transform t = r.transform; t != null && t != root; t = t.parent)
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("termomet") || n.Contains("thermomet") || n.Contains("градус") ||
                n.Contains("(auto)") || n.Contains("liquid") || n.Contains("heap") ||
                n.Contains("climbzone") || n.Contains("steam"))
                return false;
        }

        // Материал градусника/воды — тоже не корпус.
        Renderer rr = r as Renderer;
        if (rr == null) rr = r.GetComponent<Renderer>();
        if (rr != null)
            foreach (Material m in rr.sharedMaterials)
            {
                if (m == null || m.shader == null) continue;
                string s = m.shader.name.ToLowerInvariant();
                if (s.Contains("thermo") || s.Contains("liquid") || s.Contains("particle") || s.Contains("water")) return false;
            }
        return true;
    }
}
