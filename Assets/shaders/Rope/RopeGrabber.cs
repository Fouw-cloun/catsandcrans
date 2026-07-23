using FishNet.Object;
using UnityEngine;

/// <summary>
/// Вешается на игрока (объект с NetworkObject). Работает только у владельца (IsOwner).
/// По кнопке ищет ближайший узел ближайшей верёвки в радиусе руки и захватывает его,
/// затем каждый кадр шлёт серверу позицию руки. Отпускание — по отпусканию кнопки.
///
/// Сам захват исполняет сервер (в NetworkedRope), клиент только шлёт намерение + позицию.
/// </summary>
public class RopeGrabber : NetworkBehaviour
{
    [Header("Рука / точка захвата")]
    public Transform hand;                 // где «держим» верёвку (кисть/центр игрока)
    public float grabRadius = 0.6f;
    public KeyCode grabKey = KeyCode.E;
    public LayerMask ropeLayer = ~0;

    NetworkedRope _held;
    int _heldNode = -1;

    void Reset() => hand = transform;

    void Update()
    {
        if (!base.IsOwner) return;
        if (hand == null) hand = transform;

        if (Input.GetKeyDown(grabKey)) TryGrab();
        else if (Input.GetKey(grabKey) && _held != null) _held.MoveGrabServer(_heldNode, hand.position);
        else if (Input.GetKeyUp(grabKey)) Release();
    }

    void TryGrab()
    {
        // ищем коллайдеры верёвок рядом
        Collider[] hits = Physics.OverlapSphere(hand.position, grabRadius, ropeLayer);
        NetworkedRope best = null;
        int bestNode = -1;
        float bestDist = float.MaxValue;

        foreach (var c in hits)
        {
            var rope = c.GetComponentInParent<NetworkedRope>();
            if (rope == null) continue;

            int node = rope.FindNearestNode(hand.position, out float d);
            if (d < bestDist && d <= grabRadius)
            {
                bestDist = d; best = rope; bestNode = node;
            }
        }

        if (best != null)
        {
            _held = best;
            _heldNode = bestNode;
            _held.GrabServer(_heldNode, hand.position);
        }
    }

    void Release()
    {
        if (_held != null)
        {
            _held.ReleaseServer(_heldNode);
            _held = null;
            _heldNode = -1;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere((hand ? hand : transform).position, grabRadius);
    }
}
