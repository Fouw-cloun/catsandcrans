using UnityEngine;

public static class CraneMath
{
    public static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    public static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);

    // Если исходная сцена вне пределов, разрешаем плавно вернуться, не телепортируя тело.
    public static float StepIntoRange(float value, float delta, Vector2 range)
    {
        float low = Mathf.Min(range.x, range.y), high = Mathf.Max(range.x, range.y);
        if (value < low) return delta > 0f ? Mathf.Min(high, value + delta) : value;
        if (value > high) return delta < 0f ? Mathf.Max(low, value + delta) : value;
        return Mathf.Clamp(value + delta, low, high);
    }
}
