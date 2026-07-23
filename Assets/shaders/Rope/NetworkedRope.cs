using System.Collections.Generic;
using FishNet.Object;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;

/// <summary>
/// Сетевой слой поверх VerletRope (FishNet v4).
///
/// АРХИТЕКТУРА (важно — почему так):
///   Verlet недетерминирован между машинами, поэтому мы НЕ симулируем верёвку у всех.
///   Симулирует ТОЛЬКО сервер (в коопе это хост). Сервер стримит позиции узлов
///   наблюдателям по НЕНАДЁЖНОМУ каналу каждый тик. Клиенты не считают физику —
///   они интерполируют полученные снапшоты. Это даёт одинаковую верёвку у всех
///   и корректные коллизии/геймплей (верёвка «настоящая» только на сервере).
///
///   Захват: игрок шлёт ServerRpc (grab/move/release). Сервер пиннит нужный узел
///   к позиции руки. Права игрока на верёвку не требуются (RequireOwnership=false),
///   т.к. в коопе доверяем клиентам.
///
/// ОПТИМИЗАЦИЯ ТРАФИКА (см. sendEveryNTicks и опц. half-float в README):
///   24 узла * 12 байт = 288 б/тик. На 30 тиках это ~8.6 КБ/с на наблюдателя.
///   Для коопа (2-4 игрока, немного верёвок) — норм. Можно резать вдвое half-float'ами
///   или синхронизировать только якоря (см. коммент внизу файла).
/// </summary>
[RequireComponent(typeof(VerletRope))]
public class NetworkedRope : NetworkBehaviour
{
    [Header("Сеть")]
    [Tooltip("Отправлять снапшот раз в N тиков (1 = каждый тик).")]
    [Min(1)] public int sendEveryNTicks = 1;

    [Tooltip("Клиент рендерит с задержкой, чтобы интерполировать между снапшотами.")]
    public bool interpolate = true;
    [Range(0.03f, 0.3f)] public float interpolationDelay = 0.1f;

    VerletRope _rope;
    Vector3[] _sendBuffer;
    uint _tickCounter;

    // --- буфер снапшотов на клиенте ---
    struct Snapshot { public double localTime; public Vector3[] points; }
    readonly List<Snapshot> _snapshots = new List<Snapshot>();

    // --- состояние захвата (только на сервере) ---
    readonly Dictionary<int, Vector3> _grabTargets = new Dictionary<int, Vector3>();

    void Awake()
    {
        _rope = GetComponent<VerletRope>();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _rope.simulateLocally = false;   // в сети физику двигает только сервер (см. ниже)
        _sendBuffer = new Vector3[_rope.nodeCount];
        base.TimeManager.OnTick += OnTick;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        if (base.TimeManager != null)
            base.TimeManager.OnTick -= OnTick;
    }

    void OnTick()
    {
        if (!base.IsServerInitialized) return;

        // применяем цели захвата
        foreach (var kv in _grabTargets)
            _rope.Pin(kv.Key, kv.Value);

        float dt = (float)base.TimeManager.TickDelta;
        _rope.Simulate(dt);

        if (++_tickCounter % (uint)sendEveryNTicks == 0)
        {
            // если верёвку пересобрали с другим числом узлов — переаллоцируем буфер
            if (_sendBuffer == null || _sendBuffer.Length != _rope.Nodes.Count)
                _sendBuffer = new Vector3[_rope.Nodes.Count];

            _rope.WritePositions(_sendBuffer);
            SendPoints(_sendBuffer);
        }
    }

    // Ненадёжный канал: потерянный пакет не страшен, следующий тик всё исправит.
    [ObserversRpc(BufferLast = true)]
    void SendPoints(Vector3[] points, Channel channel = Channel.Unreliable)
    {
        // На хосте (server+client) сервер уже держит авторитетную симуляцию — не трогаем.
        if (base.IsServerInitialized) return;

        if (!interpolate)
        {
            _rope.ReadPositions(points);
            return;
        }

        _snapshots.Add(new Snapshot
        {
            localTime = Time.timeAsDouble,
            points = points
        });
        // держим буфер небольшим
        if (_snapshots.Count > 32) _snapshots.RemoveAt(0);
    }

    void Update()
    {
        if (base.IsServerInitialized || !interpolate) return;
        InterpolateClient();
    }

    void InterpolateClient()
    {
        if (_snapshots.Count == 0) return;

        double renderTime = Time.timeAsDouble - interpolationDelay;

        // ищем два снапшота, между которыми находится renderTime
        Snapshot older = _snapshots[0], newer = _snapshots[_snapshots.Count - 1];
        bool found = false;
        for (int i = 0; i < _snapshots.Count - 1; i++)
        {
            if (_snapshots[i].localTime <= renderTime && _snapshots[i + 1].localTime >= renderTime)
            {
                older = _snapshots[i];
                newer = _snapshots[i + 1];
                found = true;
                break;
            }
        }

        if (!found)
        {
            // renderTime за пределами буфера — берём самый свежий как есть
            _rope.ReadPositions(_snapshots[_snapshots.Count - 1].points);
            return;
        }

        double span = newer.localTime - older.localTime;
        float t = span > 1e-5 ? (float)((renderTime - older.localTime) / span) : 1f;

        var nodes = _rope.Nodes;
        int n = Mathf.Min(nodes.Count, Mathf.Min(older.points.Length, newer.points.Length));
        for (int i = 0; i < n; i++)
        {
            Vector3 p = Vector3.Lerp(older.points[i], newer.points[i], t);
            nodes[i].position = p;
            nodes[i].previous = p;
        }

        // выкидываем протухшие снапшоты
        while (_snapshots.Count > 2 && _snapshots[1].localTime < renderTime)
            _snapshots.RemoveAt(0);
    }

    // =======================================================================
    //  ПУБЛИЧНОЕ API ЗАХВАТА — вызывается с игрока (RopeGrabber)
    // =======================================================================

    [ServerRpc(RequireOwnership = false)]
    public void GrabServer(int nodeIndex, Vector3 handWorldPos, NetworkConnection conn = null)
    {
        if ((uint)nodeIndex >= _rope.Nodes.Count) return;
        _grabTargets[nodeIndex] = handWorldPos;
    }

    [ServerRpc(RequireOwnership = false)]
    public void MoveGrabServer(int nodeIndex, Vector3 handWorldPos, NetworkConnection conn = null)
    {
        if (_grabTargets.ContainsKey(nodeIndex))
            _grabTargets[nodeIndex] = handWorldPos;
    }

    [ServerRpc(RequireOwnership = false)]
    public void ReleaseServer(int nodeIndex, NetworkConnection conn = null)
    {
        if (_grabTargets.Remove(nodeIndex))
            _rope.Unpin(nodeIndex);
    }

    // Локальный helper для клиента: найти ближайший узел к точке (для прицела захвата).
    public int FindNearestNode(Vector3 worldPos, out float dist) => _rope.FindNearestNode(worldPos, out dist);

    // -----------------------------------------------------------------------
    //  АЛЬТЕРНАТИВА "ТОЛЬКО ЯКОРЯ" (экономия трафика в 10x, для декоративных верёвок):
    //  Вместо стрима всех узлов синхронизируйте 2 конца через SyncVar<Vector3>,
    //  а VerletRope запускайте ЛОКАЛЬНО на каждом клиенте, пиннив узел 0 и последний
    //  к этим SyncVar. Форма провиса совпадёт визуально почти идеально. Минус —
    //  коллизии/геймплей середины верёвки не будут строго одинаковыми у всех.
    // -----------------------------------------------------------------------
}
