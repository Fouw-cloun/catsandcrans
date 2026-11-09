using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Верёвка на Verlet-физике. Стабилизированный солвер + concave-safe коллизия.
///
/// Правки против ДЁРГАНЬЯ:
///   • изгиб решается фиксированным числом итераций, а не stiffness раз (не раскачивает);
///   • кламп скорости узла за шаг (нет взрывов при резких настройках);
///   • контактный скин + трение применяется только при реальном проникновении.
/// Правки против ПРОХОДА СКВОЗЬ:
///   • анти-туннелинг SphereCast (работает по вогнутым MeshCollider уровня);
///   • депенетрация + фолбэк-рейкаст, если узел уже внутри.
///
/// Форму считает СЕРВЕР (NetworkedRope); клиенты интерполируют.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class VerletRope : MonoBehaviour
{
    public enum RopeType { Custom, Wire, Hose, SteelCable, Chain, ClimbingRope }

    [System.Serializable]
    public class Node
    {
        public Vector3 position;
        public Vector3 previous;
        public bool locked;
        public Node(Vector3 p) { position = p; previous = p; }
    }

    [Header("═══ Точки крепления ═══")]
    public Transform startPoint;
    public Transform endPoint;
    public bool pinStart = true;
    public bool pinEnd = true;

    [Header("═══ Звенья / длина ═══")]
    [Min(2)] public int nodeCount = 20;
    public float hangLength = 3f;
    public bool constantLength = false;

    [Header("═══ Натяжение / провис ═══")]
    [Range(0f, 2f)] public float slack = 0.15f;
    [Range(0f, 1f)] public float tension = 0f;
    [Range(0f, 1f)] public float elasticity = 0.1f;
    [Range(1f, 3f)] public float stretchLimit = 1.5f;

    [Header("═══ Жёсткость / динамика ═══")]
    [Range(1, 80)] public int stiffness = 30;
    [Tooltip("Жёсткость на изгиб. 0 = тряпка, 1 = почти прут.")]
    [Range(0f, 1f)] public float bendStiffness = 0.25f;
    [Tooltip("Итераций изгиба за шаг (2-4). Больше НЕ значит стабильнее.")]
    [Range(1, 6)] public int bendIterations = 2;
    [Range(0.85f, 1f)] public float damping = 0.99f;
    [Range(0f, 4f)] public float gravityScale = 1f;
    public float gravity = -9.81f;

    [Header("═══ Ветер (опц.) ═══")]
    public bool useWind = false;
    public Vector3 windDirection = new Vector3(1f, 0f, 0f);
    [Range(0f, 5f)] public float windStrength = 1f;
    [Range(0f, 5f)] public float windTurbulence = 1f;

    [Header("═══ Коллизии с миром ═══")]
    public bool collide = true;
    public float ropeRadius = 0.05f;
    [Min(1)] public int collideEveryNNodes = 1;
    [Tooltip("Свип из прошлой позиции → не проходит сквозь тонкие/быстрые объекты и вогнутые меши.")]
    public bool preventTunneling = true;
    [Tooltip("Контактный зазор — гасит дрожь на поверхности.")]
    [Range(0f, 0.05f)] public float contactSkin = 0.01f;
    [Range(0f, 1f)] public float surfaceFriction = 0.5f;
    [Range(0f, 1f)] public float bounciness = 0f;
    public LayerMask collisionMask = ~0;

    [Header("═══ Толчок объектов (опц., сервер) ═══")]
    public bool pushRigidbodies = false;
    [Range(0f, 20f)] public float pushForce = 3f;

    public List<Node> Nodes { get; private set; } = new List<Node>();
    public int LinkCount => Mathf.Max(0, Nodes.Count - 1);
    public bool simulateLocally = true;

    float _fixedLength;
    static readonly Collider[] _hitBuffer = new Collider[8];
    Vector3 StartPos => startPoint ? startPoint.position : transform.position;

    void Awake() => Build();
    void OnEnable() { if (Nodes.Count == 0) Build(); }

    [ContextMenu("Rebuild Rope")]
    public void Build()
    {
        Nodes.Clear();
        Vector3 a = StartPos;
        Vector3 b = endPoint ? endPoint.position : a + Vector3.down * hangLength;
        _fixedLength = ComputeTargetLength(Vector3.Distance(a, b));
        int cnt = Mathf.Max(2, nodeCount);
        for (int i = 0; i < cnt; i++)
        {
            float t = (float)i / (cnt - 1);
            Nodes.Add(new Node(Vector3.Lerp(a, b, t)));
        }
    }

    float ComputeTargetLength(float straightDist)
    {
        float len = (endPoint != null) ? straightDist * (1f + slack) : hangLength * (1f + slack);
        len *= (1f - tension * 0.9f);
        return Mathf.Max(len, 0.01f);
    }

    void FixedUpdate()
    {
        if (Application.isPlaying && simulateLocally) Simulate(Time.fixedDeltaTime);
    }

    public void Simulate(float dt)
    {
        if (Nodes.Count == 0) Build();
        int n = Nodes.Count;

        float straight = Vector3.Distance(StartPos, endPoint ? endPoint.position : StartPos + Vector3.down * hangLength);
        float total = constantLength ? _fixedLength : ComputeTargetLength(straight);
        float segLen = total / (n - 1);
        float maxSeg = segLen * stretchLimit;
        float maxMove = Mathf.Max(ropeRadius, segLen * 0.9f);   // кламп: не больше сегмента за шаг

        // концы
        if (pinStart) { Nodes[0].position = StartPos; Nodes[0].locked = true; }
        else Nodes[0].locked = false;
        if (endPoint != null && pinEnd) { Nodes[n - 1].position = endPoint.position; Nodes[n - 1].locked = true; }
        else Nodes[n - 1].locked = false;

        // интеграция + кламп скорости
        Vector3 g = new Vector3(0f, gravity * gravityScale, 0f) * (dt * dt);
        for (int i = 0; i < n; i++)
        {
            Node node = Nodes[i];
            if (node.locked) { node.previous = node.position; continue; }

            Vector3 vel = (node.position - node.previous) * damping;
            float vlen = vel.magnitude;
            if (vlen > maxMove) vel *= maxMove / vlen;      // анти-взрыв
            node.previous = node.position;
            node.position += vel + g;

            if (useWind)
            {
                float turb = Mathf.PerlinNoise(Time.time * windTurbulence, i * 0.3f) - 0.5f;
                node.position += windDirection.normalized * (windStrength * (0.5f + turb)) * (dt * dt);
            }
        }

        // расстояние
        float correction = Mathf.Lerp(1f, 0.15f, elasticity);
        for (int k = 0; k < stiffness; k++)
            SolveDistance(segLen, maxSeg, correction);

        // изгиб — фиксированное число итераций (не раскачивает)
        if (bendStiffness > 0f)
            for (int k = 0; k < bendIterations; k++)
                SolveBending();

        if (collide) ResolveCollisions();
    }

    void SolveDistance(float segLen, float maxSeg, float correction)
    {
        for (int i = 0; i < Nodes.Count - 1; i++)
        {
            Node a = Nodes[i], b = Nodes[i + 1];
            Vector3 delta = b.position - a.position;
            float dist = delta.magnitude;
            if (dist < 1e-6f) continue;

            float target = Mathf.Min(segLen, maxSeg);
            float applied = (dist - target) * correction;
            if (dist > maxSeg) applied = dist - maxSeg;

            Vector3 offset = delta * (applied / dist);
            if (a.locked && b.locked) continue;
            if (a.locked)      b.position -= offset;
            else if (b.locked) a.position += offset;
            else { a.position += offset * 0.5f; b.position -= offset * 0.5f; }
        }
    }

    void SolveBending()
    {
        float s = bendStiffness * 0.5f;
        for (int i = 1; i < Nodes.Count - 1; i++)
        {
            Node node = Nodes[i];
            if (node.locked) continue;
            Vector3 mid = (Nodes[i - 1].position + Nodes[i + 1].position) * 0.5f;
            node.position += (mid - node.position) * s;
        }
    }

    void ResolveCollisions()
    {
        float r = ropeRadius;
        float rs = r + contactSkin;

        for (int i = 0; i < Nodes.Count; i += collideEveryNNodes)
        {
            Node node = Nodes[i];
            if (node.locked) continue;

            // 1) АНТИ-ТУННЕЛИНГ (concave-safe: SphereCast работает по вогнутым мешам)
            if (preventTunneling)
            {
                Vector3 move = node.position - node.previous;
                float moveLen = move.magnitude;
                if (moveLen > 1e-4f &&
                    Physics.SphereCast(node.previous, r, move / moveLen,
                        out RaycastHit sweep, moveLen, collisionMask, QueryTriggerInteraction.Ignore))
                {
                    node.position = sweep.point + sweep.normal * rs;
                    ApplyContact(node, sweep.normal);
                    continue;
                }
            }

            // 2) ДЕПЕНЕТРАЦИЯ (примитивы/convex через ClosestPoint)
            int count = Physics.OverlapSphereNonAlloc(
                node.position, rs, _hitBuffer, collisionMask, QueryTriggerInteraction.Ignore);

            for (int h = 0; h < count; h++)
            {
                Collider col = _hitBuffer[h];
                Vector3 closest = col.ClosestPoint(node.position);
                Vector3 dir = node.position - closest;
                float d = dir.magnitude;

                if (d < 1e-5f)
                {
                    // узел внутри (вогнутый меш) → фолбэк: ищем поверхность рейкастом наружу
                    if (RecoverInside(node, col, r)) { }
                    continue;
                }
                if (d >= rs) continue;

                Vector3 nrm = dir / d;
                node.position += nrm * (rs - d);
                ApplyContact(node, nrm);

                if (pushRigidbodies && col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic)
                    col.attachedRigidbody.AddForceAtPosition(-nrm * pushForce, closest, ForceMode.Force);
            }
        }
    }

    // Трение + отскок через коррекцию previous
    void ApplyContact(Node node, Vector3 normal)
    {
        Vector3 vel = node.position - node.previous;
        Vector3 vn = Vector3.Dot(vel, normal) * normal;
        Vector3 vt = vel - vn;
        Vector3 newVel = vt * (1f - surfaceFriction) - vn * bounciness;
        node.previous = node.position - newVel;
    }

    // Узел оказался внутри коллайдера — толкаем к прошлой (свободной) позиции
    bool RecoverInside(Node node, Collider col, float r)
    {
        Vector3 toPrev = node.previous - node.position;
        float len = toPrev.magnitude;
        if (len < 1e-4f) { node.position = node.previous; return true; }
        if (Physics.Raycast(node.previous, -toPrev / len, out RaycastHit hit, len + r, collisionMask, QueryTriggerInteraction.Ignore))
        {
            node.position = hit.point + hit.normal * (r + contactSkin);
            ApplyContact(node, hit.normal);
            return true;
        }
        node.position = node.previous;   // просто откат
        return true;
    }

    // ---- API захвата / сети (сигнатуры не менять) ----
    public void Pin(int index, Vector3 worldPos)
    {
        if ((uint)index >= Nodes.Count) return;
        Nodes[index].position = worldPos; Nodes[index].locked = true;
    }
    public void Unpin(int index)
    {
        if ((uint)index >= Nodes.Count) return;
        if (index == 0 && pinStart) return;
        if (index == Nodes.Count - 1 && endPoint != null && pinEnd) return;
        Nodes[index].locked = false;
    }
    public int FindNearestNode(Vector3 worldPos, out float distance)
    {
        int best = -1; distance = float.MaxValue;
        for (int i = 0; i < Nodes.Count; i++)
        {
            float d = (Nodes[i].position - worldPos).sqrMagnitude;
            if (d < distance) { distance = d; best = i; }
        }
        distance = Mathf.Sqrt(Mathf.Max(distance, 0f));
        return best;
    }
    public void WritePositions(Vector3[] target)
    {
        for (int i = 0; i < Nodes.Count && i < target.Length; i++) target[i] = Nodes[i].position;
    }
    public void ReadPositions(Vector3[] source)
    {
        if (Nodes.Count != source.Length) return;
        for (int i = 0; i < source.Length; i++)
        { Nodes[i].position = source[i]; Nodes[i].previous = source[i]; }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying && Nodes != null && Nodes.Count != nodeCount && nodeCount >= 2)
            Build();
    }
#endif

    void OnDrawGizmos()
    {
        if (startPoint == null && endPoint == null) return;
        Gizmos.color = Color.cyan;
        Vector3 a = StartPos;
        Vector3 b = endPoint ? endPoint.position : a + Vector3.down * hangLength;
        Gizmos.DrawLine(a, b);
        Gizmos.DrawWireSphere(a, 0.06f);
        Gizmos.DrawWireSphere(b, 0.06f);
    }
}
