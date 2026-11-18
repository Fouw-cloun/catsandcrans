using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(180)]
public sealed class CCLiftPassenger : MonoBehaviour
{
    public Vector2 halfDeck = new(.81f, 1.11f);
    public float deckTop = .08f;
    private Vector3 _lastPosition;
    private Quaternion _lastRotation;
    private bool _ready;
    private CharacterController _skipController;
    private int _skipFrame;
    private readonly Collider[] _overlaps = new Collider[40];
    private readonly HashSet<CharacterController> _carried = new();

    private void OnEnable() => _ready = false;

    public void SkipTransfer(CharacterController controller)
    {
        _skipController = controller;
        _skipFrame = Time.frameCount;
    }

    private void LateUpdate()
    {
        Vector3 pos = transform.position;
        Quaternion rotation = transform.rotation;
        if (_ready && ((pos - _lastPosition).sqrMagnitude > .00000001f || Quaternion.Angle(rotation, _lastRotation) > .001f))
        {
            if ((pos - _lastPosition).sqrMagnitude < 1f) Carry(pos, rotation);
        }
        _lastPosition = pos;
        _lastRotation = rotation;
        _ready = true;
    }

    private void Carry(Vector3 pos, Quaternion rotation)
    {
        Vector3 center = _lastPosition + _lastRotation * Vector3.up * .72f;
        int count = Physics.OverlapBoxNonAlloc(center, new Vector3(halfDeck.x + .1f, .93f, halfDeck.y + .1f),
            _overlaps, _lastRotation, ~0, QueryTriggerInteraction.Ignore);
        _carried.Clear();
        for (int i = 0; i < count; i++)
        {
            var cc = _overlaps[i] as CharacterController;
            if (cc == null || !cc.enabled || (cc == _skipController && Time.frameCount == _skipFrame) ||
                !_carried.Add(cc)) continue;
            var player = cc.GetComponent<FirstPersonController>();
            if (player == null || !player.isActiveAndEnabled || !player.IsOwner) continue;
            Vector3 feet = cc.bounds.center;
            feet.y = cc.bounds.min.y;
            Vector3 previous = Quaternion.Inverse(_lastRotation) * (feet - _lastPosition);
            if (Mathf.Abs(previous.x) > halfDeck.x || Mathf.Abs(previous.z) > halfDeck.y ||
                previous.y < deckTop - .14f || previous.y > deckTop + .18f || cc.velocity.y > 1.8f)
                continue;
            Vector3 destination = pos + rotation * previous;
            cc.Move(destination - feet);
        }
    }
}
