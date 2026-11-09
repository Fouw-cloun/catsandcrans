using UnityEngine;

public static class CatCooksDustUtility
{
    public static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
    public static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    public static float Safe(float v, float min, float max, float fallback)
        => Finite(v) ? Mathf.Clamp(v, min, max) : fallback;
    public static Vector3 Normal(Vector3 n) => Finite(n) && n.sqrMagnitude > .0001f ? n.normalized : Vector3.up;
    public static CatCooksDustProfile Surface(Collider c, CatCooksDustProfile fallback, out float amount)
    {
        var s = c == null ? null : c.GetComponentInParent<CatCooksDustSurface>();
        amount = s == null ? 1 : Mathf.Clamp(s.amount, 0, 2);
        return s != null && s.profile != null ? s.profile : fallback;
    }

    // Each receiver owns its hit buffer. RaycastNonAlloc results are not distance-sorted.
    public static bool Ground(Vector3 origin, Vector3 direction, float length, LayerMask mask,
        Transform ignoreRoot, RaycastHit[] hits, out RaycastHit nearest)
    {
        nearest = default;
        int count = Physics.RaycastNonAlloc(origin, direction, hits, length, mask, QueryTriggerInteraction.Ignore);
        RaycastHit[] candidates=hits;
        // In a dense material pile a full NonAlloc buffer may omit the nearest hit.
        // Pay for a full query only in that exceptional case, then apply the same filter.
        if(count==hits.Length)
        { candidates=Physics.RaycastAll(origin,direction,length,mask,QueryTriggerInteraction.Ignore); count=candidates.Length; }
        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = candidates[i];
            if (hit.collider == null || (ignoreRoot != null && hit.collider.transform.IsChildOf(ignoreRoot))) continue;
            if (hit.distance < best) { nearest = hit; best = hit.distance; }
        }
        return best < float.PositiveInfinity;
    }

    // Pure distance clock, also used by the Editor regression checks.
    public struct DistanceClock
    {
        public float remainder;
        public int Advance(float distance, float spacing, int limit)
        {
            if (!Finite(distance) || distance <= 0) return 0;
            spacing = Safe(spacing,.05f,100,.5f);
            distance = Mathf.Min(distance,10000);
            remainder += distance;
            int available = Mathf.FloorToInt((remainder+spacing*1e-5f) / spacing);
            remainder = Mathf.Max(0,remainder-available*spacing); // Never replay a backlog after a stall.
            return Mathf.Min(Mathf.Max(0, limit), available);
        }
        public void Reset() => remainder = 0;
    }
}
