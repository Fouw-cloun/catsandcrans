using UnityEngine;

/// <summary>
/// Дуло пылесоса. Предмет в руке (подбор, поза в руке — как инструменты).
/// Пока без сосания. Шланг цепляется к дулу через VerletRope.startPoint —
/// см. настройку (дуло само ничего с верёвкой не делает, за него держит
/// startPoint шланга). Здесь только держание в руке.
///
/// Требует: NetworkObject, NetworkTransform (Client Authoritative), Collider,
/// Rigidbody (как у обычного предмета).
/// </summary>
public class NozzleItem : InteractableItem
{
    [Header("Положение в руке (регулировка под модель)")]
    [SerializeField] private Vector3 _holdPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
    }

    public override void UpdateHeldPose(Transform handAnchor)
    {
        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);
        transform.SetPositionAndRotation(pos, rot);
    }
}
