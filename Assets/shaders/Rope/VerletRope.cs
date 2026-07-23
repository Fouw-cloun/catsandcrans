using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Верёвка между двумя точками на Verlet-физике с кучей настроек:
/// натяжение, эластичность, провис, число звеньев, упругость, ветер, коллизии.
///
/// Два режима использования:
///   • КАБЕЛЬ  — endPoint задан, оба конца закреплены, между ними тянется провод.
///   • ВЕРЁВКА — endPoint пуст → свободно висящий конец (скалолазание/спелео),
///              за него можно цепляться (RopeGrabber).
///
/// simulateLocally = true → работает БЕЗ сети (просто Play).
/// В сети NetworkedRope выставит false и физику погонит только сервер (хост).
///
/// Настройки slack / tension / elasticity / stiffness / gravityScale читаются
/// КАЖДЫЙ шаг — крути ползунки в Play прямо на хосте и сразу видишь результат.
/// (nodeCount меняется только через Rebuild — он переаллоцирует цепочку.)
/// </summary>
[DisallowMultipleComponent]
public class VerletRope : MonoBehaviour
{
    public enum RopeType { Custom, Cable, ClimbingRope, Chain }

    [System.Serializable]
    public class Node
    {
        public Vector3 position;
        public Vector3 previous;
        public bool locked;
        public Node(Vector3 p) { position = p; previous = p; }
    }

    [Header("═══ Точки крепления ═══")]
    [Tooltip("Начало (узел 0). Пусто → позиция этого объекта.")]
    public Transform startPoint;
    [Tooltip("Конец (последний узел). Пусто → свободно висящий конец (верёвка).")]
    public Transform endPoint;
    public bool pinStart = true;
    public bool pinEnd = true;

    [Header("═══ Пресет ═══")]
    [Tooltip("Быстрая настройка под тип. Нажми 'Apply Preset' в контекстном меню компонента.")]
    public RopeType preset = RopeType.Custom;

    [Header("═══ Звенья / длина ═══")]
    [Tooltip("Число узлов. Звеньев = nodeCount - 1. Больше = плавнее и точнее коллизии.")]
    [Min(2)] public int nodeCount = 20;
    [Tooltip("Если пусто (нет endPoint) — длина свободно висящей верёвки.")]
    public float hangLength = 3f;
    [Tooltip("ON: длина фиксируется при Build (верёвка натягивается, если растащить концы).\nOFF: длина пересчитывается из расстояния каждый кадр (живой кабель).")]
    public bool constantLength = false;

    [Header("═══ Натяжение / провис ═══")]
    [Tooltip("Провис: добавляет длину сверх прямой. 0 = без запаса, 0.3 = заметный провис.")]
    [Range(0f, 2f)] public float slack = 0.15f;
    [Tooltip("Натяжение: тянет верёвку в струну. 0 = как есть, 1 = максимально натянуто.")]
    [Range(0f, 1f)] public float tension = 0f;
    [Tooltip("Эластичность (растяжимость под нагрузкой). 0 = нерастяжимая, 1 = резинка.")]
    [Range(0f, 1f)] public float elasticity = 0.1f;
    [Tooltip("Предел растяжения: во сколько раз макс. длина сегмента больше базовой (для резинки).")]
    [Range(1f, 3f)] public float stretchLimit = 1.5f;

    [Header("═══ Жёсткость / динамика ═══")]
    [Tooltip("Итерации солвера. Больше = жёстче и стабильнее, но дороже.")]
    [Range(1, 80)] public int stiffness = 30;
    [Tooltip("Затухание (сопротивление воздуха). 1 = без потерь, 0.95 = вязко.")]
    [Range(0.85f, 1f)] public float damping = 0.99f;
    [Tooltip("Множитель гравитации. <1 лёгкий кабель, >1 тяжёлая цепь.")]
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
    [Tooltip("Коллизия на каждом N-м узле (1 = все, 2 = каждый второй — быстрее).")]
    [Min(1)] public int collideEveryNNodes = 1;
    public LayerMask collisionMask = ~0;

    public List<Node> Nodes { get; private set; } = new List<Node>();
    public int LinkCount => Mathf.Max(0, Nodes.Count - 1);
    public bool simulateLocally = true;   // NetworkedRope выставит false

    float _fixedLength;                     // длина при constantLength
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

        for (int i = 0; i < nodeCount; i++)
        {
            float t = (float)i / (nodeCount - 1);
            Nodes.Add(new Node(Vector3.Lerp(a, b, t)));
        }
    }

    [ContextMenu("Apply Preset")]
    public void ApplyPreset()
    {
        switch (preset)
        {
            case RopeType.Cable:
                nodeCount = 14; slack = 0.1f; tension = 0.15f; elasticity = 0.05f;
                stiffness = 45; gravityScale = 0.6f; damping = 0.98f;
                collide = false; constantLength = false; break;
            case RopeType.ClimbingRope:
                nodeCount = 28; slack = 0.25f; tension = 0f; elasticity = 0.15f;
                stiffness = 25; gravityScale = 1f; damping = 0.99f;
                collide = true; collideEveryNNodes = 1; constantLength = true; break;
            case RopeType.Chain:
                nodeCount = 20; slack = 0.05f; tension = 0f; elasticity = 0f;
                stiffness = 60; gravityScale = 1.6f; damping = 0.97f;
                collide = true; constantLength = true; break;
        }
        Build();
    }

    float ComputeTargetLength(float straightDist)
    {
        float len = (endPoint != null) ? straightDist * (1f + slack) : hangLength * (1f + slack);
        len *= (1f - tension * 0.9f);                  // натяжение укорачивает → в струну
        return Mathf.Max(len, 0.01f);
    }

    void FixedUpdate()
    {
        if (simulateLocally) Simulate(Time.fixedDeltaTime);
    }

    /// <summary>Один шаг физики. Локально (FixedUpdate) или на сервере (сетевой тик).</summary>
    public void Simulate(float dt)
    {
        if (Nodes.Count == 0) Build();
        int n = Nodes.Count;

        // сегмент считаем каждый шаг → ползунки натяжения/провиса живые
        float straight = Vector3.Distance(StartPos, endPoint ? endPoint.position : StartPos + Vector3.down * hangLength);
        float total = constantLength ? _fixedLength : ComputeTargetLength(straight);
        float segLen = total / (n - 1);
        float maxSeg = segLen * stretchLimit;

        // концы едут за трансформами
        if (pinStart) { Nodes[0].position = StartPos; Nodes[0].locked = true; }
        else Nodes[0].locked = false;
        if (endPoint != null && pinEnd) { Nodes[n - 1].position = endPoint.position; Nodes[n - 1].locked = true; }
        else if (endPoint == null) Nodes[n - 1].locked = false;

        // интеграция
        Vector3 g = new Vector3(0f, gravity * gravityScale, 0f) * (dt * dt);
        for (int i = 0; i < n; i++)
        {
            Node node = Nodes[i];
            if (node.locked) { node.previous = node.position; continue; }

            Vector3 vel = (node.position - node.previous) * damping;
            node.previous = node.position;
            node.position += vel + g;

            if (useWind)
            {
                float turb = Mathf.PerlinNoise(Time.time * windTurbulence, i * 0.3f) - 0.5f;
                node.position += windDirection.normalized * (windStrength * (0.5f + turb)) * (dt * dt);
            }
        }

        // констрейнты (эластичность = частичная коррекция)
        float correction = Mathf.Lerp(1f, 0.15f, elasticity);
        for (int k = 0; k < stiffness; k++)
            SolveConstraints(segLen, maxSeg, correction);

        if (collide) ResolveCollisions();
    }

    void SolveConstraints(float segLen, float maxSeg, float correction)
    {
        for (int i = 0; i < Nodes.Count - 1; i++)
        {
            Node a = Nodes[i];
            Node b = Nodes[i + 1];
            Vector3 delta = b.position - a.position;
            float dist = delta.magnitude;
            if (dist < 1e-6f) continue;

            // жёсткий предел растяжения — даже резинка не рвётся в бесконечность
            float target = Mathf.Min(segLen, maxSeg);
            float error = dist - target;

            // мягкая коррекция для эластичности, но клампим по stretchLimit
            float applied = error * correction;
            if (dist > maxSeg) applied = dist - maxSeg;  // за пределом тянем жёстко

            Vector3 offset = delta * (applied / dist);
            if (a.locked && b.locked) continue;
            if (a.locked)      b.position -= offset;
            else if (b.locked) a.position += offset;
            else { a.position += offset * 0.5f; b.position -= offset * 0.5f; }
        }
    }

    void ResolveCollisions()
    {
        for (int i = 0; i < Nodes.Count; i += collideEveryNNodes)
        {
            Node node = Nodes[i];
            if (node.locked) continue;

            int count = Physics.OverlapSphereNonAlloc(
                node.position, ropeRadius, _hitBuffer, collisionMask, QueryTriggerInteraction.Ignore);
            for (int h = 0; h < count; h++)
            {
                Vector3 closest = _hitBuffer[h].ClosestPoint(node.position);
                Vector3 dir = node.position - closest;
                float d = dir.magnitude;
                if (d < ropeRadius && d > 1e-6f)
                    node.position += dir / d * (ropeRadius - d);
            }
        }
    }

    // ---- API захвата / сети (не менять сигнатуры — используются NetworkedRope/RopeGrabber) ----
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
        // живой rebuild в редакторе при смене числа узлов
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
