using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Раскладывает кинематические CapsuleCollider'ы вдоль сегментов верёвки и двигает их
/// каждый FixedUpdate за узлами. Благодаря этому ДРУГИЕ объекты (игрок, ящики, рагдоллы)
/// сталкиваются с верёвкой — сама VerletRope этого не делает (она только отталкивается от мира).
///
/// Дёшево: коллайдеры кинематические, не симулируются, только позиционируются.
/// Работает и на сервере, и на клиенте — просто едет за Nodes (они синхронизированы).
/// Для перфа можно ставить collider на каждый N-й сегмент.
/// </summary>
[RequireComponent(typeof(VerletRope))]
public class RopeColliders : MonoBehaviour
{
    [Tooltip("Ставить коллайдер на каждый N-й сегмент (1 = все, 2 = каждый второй).")]
    [Min(1)] public int colliderEveryNSegments = 1;
    [Tooltip("Множитель радиуса коллайдера относительно ropeRadius верёвки.")]
    public float radiusScale = 1f;
    [Tooltip("Слой для коллайдеров сегментов (создай слой 'Rope' и исключи его из collisionMask верёвки!).")]
    public int colliderLayer = 0;
    [Tooltip("Материал физики (трение/отскок) для сегментов — опц.")]
    public PhysicsMaterial physicMaterial;

    VerletRope _rope;
    readonly List<CapsuleCollider> _caps = new List<CapsuleCollider>();
    Transform _root;

    void Awake()
    {
        _rope = GetComponent<VerletRope>();
        var go = new GameObject("RopeColliders");
        _root = go.transform;
        _root.SetParent(transform, false);
    }

    void FixedUpdate()
    {
        var nodes = _rope.Nodes;
        if (nodes == null || nodes.Count < 2) return;

        int step = Mathf.Max(1, colliderEveryNSegments);
        int needed = 0;
        for (int i = 0; i < nodes.Count - 1; i += step) needed++;

        EnsurePool(needed);

        float r = _rope.ropeRadius * radiusScale;
        int idx = 0;
        for (int i = 0; i < nodes.Count - 1; i += step)
        {
            Vector3 a = nodes[i].position;
            Vector3 b = nodes[Mathf.Min(i + step, nodes.Count - 1)].position;
            PlaceCapsule(_caps[idx++], a, b, r);
        }
    }

    void PlaceCapsule(CapsuleCollider cap, Vector3 a, Vector3 b, float radius)
    {
        Vector3 mid = (a + b) * 0.5f;
        Vector3 dir = b - a;
        float len = dir.magnitude;

        cap.transform.position = mid;
        if (len > 1e-4f)
            cap.transform.rotation = Quaternion.LookRotation(dir / len);

        cap.direction = 2;                 // ось Z (LookRotation смотрит вдоль Z)
        cap.radius = radius;
        cap.height = Mathf.Max(len + radius * 2f, radius * 2f);
        cap.center = Vector3.zero;
    }

    void EnsurePool(int needed)
    {
        while (_caps.Count < needed)
        {
            var go = new GameObject("seg");
            go.layer = colliderLayer;
            go.transform.SetParent(_root, false);
            var cap = go.AddComponent<CapsuleCollider>();
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            if (physicMaterial != null) cap.material = physicMaterial;
            _caps.Add(cap);
        }
        for (int i = 0; i < _caps.Count; i++)
            _caps[i].gameObject.SetActive(i < needed);
    }
}
