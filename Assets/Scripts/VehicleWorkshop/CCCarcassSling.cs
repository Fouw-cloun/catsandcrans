using UnityEngine;

[DisallowMultipleComponent]
public sealed class CCCarcassSling : MonoBehaviour
{
    public CraneLiftPoint point;
    public Material webbing;
    public Vector3[] frontWrap, rearWrap;

    public CraneLoad Load => point.Load;
    private LineRenderer front, rear;
    private CraneLoad load;

    [SerializeField, Min(0f), Tooltip("Запас зоны захвата вокруг туши в метрах.")]
    private float capturePadding = .45f;

    // Store the spine in the carcass frame. The query volume is not a physics
    // collider: it must not affect tools, the player's movement or the food.
    private Vector3 startLocal, endLocal;
    private float coreRadius;
    private bool fitted;
    public float Radius => coreRadius + Mathf.Max(0f, capturePadding);

    private void Awake()
    {
        load = GetComponent<CraneLoad>();
        FitCapsule();
    }

    private void FitCapsule()
    {
        fitted = false;
        // Work near the origin even for construction sites far out in world space.
        var corners = new System.Collections.Generic.List<Vector3>();
        Quaternion inv = Quaternion.Inverse(transform.rotation);
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
            Bounds b = renderer.localBounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                corners.Add(inv * (renderer.transform.TransformPoint(p) - transform.position));
            }
        }
        if (corners.Count == 0) return;

        Vector3 mean = Vector3.zero;
        foreach (Vector3 v in corners) mean += v;
        mean /= corners.Count;
        Vector3 xx = Vector3.zero, yy = Vector3.zero, zz = Vector3.zero;
        Vector3 axis = Vector3.zero;
        foreach (Vector3 v in corners)
        {
            Vector3 d = v - mean;
            xx += d * d.x; yy += d * d.y; zz += d * d.z;
            if (d.sqrMagnitude > axis.sqrMagnitude) axis = d;
        }
        if (axis.sqrMagnitude < .000001f) return;
        axis.Normalize();
        // Principal length direction, including carcasses imported standing up.
        for (int i = 0; i < 12; i++)
        {
            Vector3 next = xx * axis.x + yy * axis.y + zz * axis.z;
            if (next.sqrMagnitude < .00000001f) break;
            axis = next.normalized;
        }

        float lo = float.PositiveInfinity, hi = float.NegativeInfinity, radiusSq = 0f;
        foreach (Vector3 v in corners)
        {
            Vector3 d = v - mean;
            float axial = Vector3.Dot(d, axis);
            lo = Mathf.Min(lo, axial);
            hi = Mathf.Max(hi, axial);
            radiusSq = Mathf.Max(radiusSq, (d - axis * axial).sqrMagnitude);
        }
        startLocal = transform.InverseTransformVector(transform.rotation * (mean + lo * axis));
        endLocal = transform.InverseTransformVector(transform.rotation * (mean + hi * axis));
        coreRadius = Mathf.Max(.1f, Mathf.Sqrt(radiusSq));
        fitted = true;
    }

    private static Vector3 ClosestSpine(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        if (d.sqrMagnitude < .000001f) return a;
        return a + d * Mathf.Clamp01(Vector3.Dot(p - a, d) / d.sqrMagnitude);
    }

    public bool Contains(Vector3 world, out Vector3 spine, out float distance)
    {
        spine = ClosestSpine(world, transform.TransformPoint(startLocal), transform.TransformPoint(endLocal));
        distance = Vector3.Distance(world, spine);
        return fitted && isActiveAndEnabled && distance <= Radius + .003f;
    }

    // First contact with the finite cylinder or either round cap. The eye
    // can already be inside the volume when the player is holding the hook.
    public bool RayEntry(Ray ray, float reach, out float distance)
    {
        distance = float.PositiveInfinity;
        if (!fitted || !isActiveAndEnabled) return false;
        Vector3 a = transform.TransformPoint(startLocal), b = transform.TransformPoint(endLocal);
        float r = Radius;
        if ((ClosestSpine(ray.origin, a, b) - ray.origin).sqrMagnitude <= r * r)
        {
            distance = 0f;
            return true;
        }
        Vector3 n = (b - a).normalized, m = ray.origin - a, dir = ray.direction.normalized;
        float length = Vector3.Distance(a, b);
        float md = Vector3.Dot(m, n), nd = Vector3.Dot(dir, n);
        Vector3 v = dir - nd * n, u = m - md * n;
        float aa = v.sqrMagnitude, bb = Vector3.Dot(u, v);
        float disc = bb * bb - aa * (u.sqrMagnitude - r * r);
        if (aa > .000001f && disc >= 0f)
        {
            float t = (-bb - Mathf.Sqrt(disc)) / aa;
            float along = md + nd * t;
            if (t >= 0f && along >= 0f && along <= length) distance = t;
        }
        distance = Mathf.Min(distance, Mathf.Min(CapEntry(m, dir, r), CapEntry(ray.origin - b, dir, r)));
        return distance <= reach;
    }

    private static float CapEntry(Vector3 m, Vector3 dir, float r)
    {
        float b = Vector3.Dot(m, dir);
        float disc = b * b - (m.sqrMagnitude - r * r);
        if (b >= 0f || disc < 0f) return float.PositiveInfinity;
        return Mathf.Max(0f, -b - Mathf.Sqrt(disc));
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) FitCapsule();
        if (!fitted) return;
        Vector3 a = transform.TransformPoint(startLocal), b = transform.TransformPoint(endLocal);
        Vector3 n = (b - a).normalized;
        Vector3 side = Vector3.Cross(n, Mathf.Abs(n.y) > .9f ? Vector3.right : Vector3.up).normalized;
        Vector3 up = Vector3.Cross(n, side);
        using (new UnityEditor.Handles.DrawingScope(new Color(.15f, 1f, .65f)))
        {
            UnityEditor.Handles.DrawWireDisc(a, n, Radius);
            UnityEditor.Handles.DrawWireDisc(b, n, Radius);
            foreach (var edge in new[] { side, -side, up, -up })
                UnityEditor.Handles.DrawLine(a + edge * Radius, b + edge * Radius);
            foreach (var tangent in new[] { side, up })
            {
                Vector3 normal = Vector3.Cross(n, tangent);
                UnityEditor.Handles.DrawWireArc(a, normal, tangent, 180f, Radius);
                UnityEditor.Handles.DrawWireArc(b, normal, -tangent, 180f, Radius);
            }
        }
    }
#endif

    private void LateUpdate()
    {
        var hook = load && load.IsAttached ? load.Hook : null;
        if (!hook)
        {
            if (front) front.enabled = rear.enabled = false;
            return;
        }
        if (!front)
        {
            front = MakeBand("Передняя стропа", frontWrap);
            rear = MakeBand("Задняя стропа", rearWrap);
        }
        Vector3 eye = transform.InverseTransformPoint(hook.LatchWorld);
        CloseBand(front, eye);
        CloseBand(rear, eye);
    }

    private LineRenderer MakeBand(string label, Vector3[] points)
    {
        var child = new GameObject(label);
        child.transform.SetParent(transform, false);
        var line = child.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.sharedMaterial = webbing;
        line.startWidth = line.endWidth = .055f;
        line.numCornerVertices = 3;
        line.positionCount = points.Length;
        line.SetPositions(points);
        return line;
    }

    private static void CloseBand(LineRenderer band, Vector3 eye)
    {
        band.enabled = true;
        band.SetPosition(0, eye);
        band.SetPosition(band.positionCount - 1, eye);
    }

    private void OnDisable()
    {
        if (front) front.enabled = rear.enabled = false;
    }
}
