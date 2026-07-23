using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Крышка бокового выхода гидранта. Отпадает при полном откручивании, лежит на
/// земле, ПОДБИРАЕТСЯ (F) и возвращается на свой выход.
///
/// ВОЗВРАТ: держишь крышку в руке и ПОДНОСИШЬ близко к своему выходу — она сама
/// встаёт на место (гайка снова закручена с нуля). Механика НЕ одноразовая:
/// открутил → отпало → подобрал → вернул → снова можно откручивать.
///
/// Требует: NetworkObject, NetworkTransform (Client Authoritative), Collider,
/// Rigidbody (Is Kinematic СНЯТО). Префаб — в Spawnable Prefabs у NetworkManager!
/// </summary>
public class HydrantCapItem : InteractableItem
{
    [Header("Положение в руке")]
    [SerializeField] private Vector3 _holdPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    [Header("Возврат на выход")]
    [Tooltip("С какого расстояния крышка сама встаёт на своё место")]
    [SerializeField] private float _snapDistance = 0.8f;

    /// <summary>Гайка, с которой крышка слетела (туда же и вернётся).</summary>
    private readonly SyncVar<NetworkBehaviour> _homeNut = new(null);

    private PlayerHand _holdingHand;

    private HydrantNut Home => _homeNut.Value as HydrantNut;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
    }

    /// <summary>Сервер запоминает, с какой гайки слетела крышка.</summary>
    public void ServerSetHomeNut(HydrantNut nut)
    {
        _homeNut.Value = nut;
    }

    // ───────── ВЗЯТЬ / БРОСИТЬ ─────────

    public override void ServerOnPickup(PlayerHand hand)
    {
        _holdingHand = hand;
        base.ServerOnPickup(hand);
    }

    public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
    {
        _holdingHand = null;
        base.ServerOnDrop(hand, tossDirection, force);
    }

    // ───────── СЕРВЕР: авто-возврат на выход ─────────

    private void Update()
    {
        if (!base.IsServerInitialized || _holdingHand == null) return;

        HydrantNut nut = Home;
        if (nut == null || !nut.IsOutletOpen) return; // выход уже закрыт или дома нет

        // Позиция выхода — по пивоту гайки.
        Transform target = nut.NutPivot != null ? nut.NutPivot : nut.transform;
        if (Vector3.Distance(transform.position, target.position) > _snapDistance) return;

        // Крышка встаёт на место: выкидываем из руки, гайка снова закручена,
        // предмет-крышку убираем (визуал крышки на гайке включится обратно).
        PlayerHand hand = _holdingHand;
        _holdingHand = null;
        hand.ServerDrop(Vector3.zero, 0f);

        nut.ServerCapReturned();
        base.Despawn();
    }

    // ───────── ПОЗА В РУКЕ ─────────

    public override void UpdateHeldPose(Transform handAnchor)
    {
        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);
        transform.SetPositionAndRotation(pos, rot);
    }
}
