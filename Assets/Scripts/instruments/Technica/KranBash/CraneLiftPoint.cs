using UnityEngine;

/// <summary>Сферическая область захвата. Joint всегда крепится к центру этого Transform.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SphereCollider))]
public sealed class CraneLiftPoint : MonoBehaviour
{
    [Min(0.05f)] public float CaptureRadius = 0.35f;
    [Tooltip("Свободное качание по умолчанию. Включить для жёсткого крепления груза к крюку.")]
    public bool LockRotation;
    public CraneLoad Load => GetComponentInParent<CraneLoad>();
    public Vector3 WorldPoint => transform.position;
    public float WorldRadius => CaptureRadius; // метры мира, не локальный масштаб банки
    public int Index
    {
        get
        {
            CraneLoad load = Load;
            if (load == null) return -1;
            CraneLiftPoint[] points = load.Points;
            for (int i = 0; i < points.Length; i++) if (points[i] == this) return i;
            return -1;
        }
    }
    private void Reset() => RefreshSphere();
    private void OnValidate() => RefreshSphere();
    private void Awake() => RefreshSphere();
    private void RefreshSphere()
    {
        SphereCollider sphere = GetComponent<SphereCollider>();
        if (sphere == null) return;
        Vector3 s = transform.lossyScale;
        float scale = Mathf.Max(0.001f, Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))));
        sphere.isTrigger = true;
        sphere.center = Vector3.zero;
        sphere.radius = Mathf.Max(0.05f, CaptureRadius) / scale;
    }
    private void OnDrawGizmos()
    {
        Gizmos.color = Load != null && Load.IsAttached ? Color.yellow : new Color(0.2f, 0.9f, 0.7f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, WorldRadius);
        Gizmos.DrawSphere(transform.position, 0.035f);
    }
}
