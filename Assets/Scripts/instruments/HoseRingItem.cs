using FishNet.Component.Transforming;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// КОЛЬЦО ШЛАНГА. Предмет в руке. F на боковой слот гидранта (где была крышечка)
/// → присоединяется. Один конец шланга к гидранту, второй — брандспойт.
///
/// ПРАВИЛА:
///   - Присоединить можно ТОЛЬКО когда из этого выхода НЕ льёт вода (крышка на
///     месте ИЛИ верхний вентиль закрыт). Иначе напором бы сорвало.
///   - Снять (F по кольцу) → в руку. НО если сверху вода включена — при рывке
///     тебя ВЫШИБАЕТ струёй (как при откручивании крышки).
///   - Пока присоединено: сидит на СВОЕЙ точке слота (RingSeat выхода), физика/NT off.
///
/// СЕТЬ: _attachedOutlet — SyncVar (к какому выходу присоединено). У всех
/// клиентов кольцо садится на слот через OnChange.
/// </summary>
public class HoseRingItem : InteractableItem
{
    [Header("Положение в руке")]
    [SerializeField] private Vector3 _holdPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    [Header("Посадка на слот")]
    [SerializeField] private Vector3 _mountPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _mountEulerOffset = Vector3.zero;

    [Header("Вышибание при снятии под водой")]
    [Tooltip("Сила, с которой струя вышибает игрока при рывке кольца под напором")]
    [SerializeField] private float _blowPlayerForce = 8f;

    /// <summary>Выход, к которому присоединено кольцо (null = в руке/на земле).</summary>
    private readonly SyncVar<NetworkBehaviour> _attachedOutlet = new(null);

    public bool IsAttached => _attachedOutlet.Value != null;
    public HydrantNut AttachedOutlet => _attachedOutlet.Value as HydrantNut;

    /// <summary>В руке ли кольцо (для системы шланга).</summary>
    public bool IsHeld => _holdingHand != null;
    /// <summary>Rigidbody кольца (для подтягивания).</summary>
    public Rigidbody Body => _rb;

    private PlayerHand _holdingHand;

    private NetworkTransform _nt;

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
        _nt = GetComponent<NetworkTransform>();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _attachedOutlet.OnChange += OnAttachedChanged;
        if (_attachedOutlet.Value != null) ApplyAttached(true);
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _attachedOutlet.OnChange -= OnAttachedChanged;
    }

    private void OnAttachedChanged(NetworkBehaviour prev, NetworkBehaviour next, bool asServer)
        => ApplyAttached(next != null);

    private void ApplyAttached(bool attached)
    {
        if (_nt != null) _nt.enabled = !attached;
        if (attached)
        {
            if (_rb != null) { _rb.isKinematic = true; _rb.useGravity = false; }
            if (_col != null) _col.enabled = true; // чтобы можно было навести F для снятия
        }
    }

    // ───────── СЕРВЕР: ПРИСОЕДИНИТЬ / СНЯТЬ ─────────

    /// <summary>Присоединить кольцо к выходу (вызывает слот). Только если из
    /// выхода не льёт вода.</summary>
    public override void ServerOnPickup(PlayerHand hand)
    {
        _holdingHand = hand;
        base.ServerOnPickup(hand);
    }

    public override void ServerOnDrop(PlayerHand hand, Vector3 dir, float force = 0f)
    {
        _holdingHand = null;
        base.ServerOnDrop(hand, dir, force);
    }

    public void ServerAttachTo(HydrantNut outlet)
    {
        if (outlet != null && outlet.RingSeat == null)
        {
            Debug.LogError($"[Ring] У выхода '{outlet.name}' НЕ НАЗНАЧЕНЫ ни Ring Seat, " +
                           $"ни Wrench Seat — кольцу некуда садиться, оно повиснет в воздухе. " +
                           $"Создай пустышку в устье выхода и положи её в поле Ring Seat.", outlet);
        }

        _attachedOutlet.Value = outlet;
        _holdingHand = null;
        SetPhysicsHeld(true);
    }

    /// <summary>Снять кольцо. taker — рука снимающего. Если под водой — вышибает.</summary>
    public void ServerDetach(PlayerHand taker, bool underWater, Vector3 outletDir)
    {
        _attachedOutlet.Value = null;

        if (_nt != null) _nt.enabled = true;

        if (taker != null && !taker.HasItem)
        {
            taker.ServerTryHold(this);
            // Под водой — вышибаем ИГРОКА струёй.
            if (underWater) ServerBlowPlayer(taker, outletDir);
        }
        else
        {
            NetworkObject.RemoveOwnership();
            SetPhysicsHeld(false);
        }
    }

    /// <summary>Толкнуть игрока струёй (при снятии под напором).</summary>
    private void ServerBlowPlayer(PlayerHand hand, Vector3 outletDir)
    {
        FirstPersonController fpc = hand.GetComponentInParent<FirstPersonController>();
        if (fpc == null) return;
        // Толчок наружу от выхода + вверх — как при срыве крышки.
        Vector3 dir = (outletDir + Vector3.up * 0.4f).normalized;
        fpc.ServerApplyKnockback(dir * _blowPlayerForce);
    }

    // ───────── F ПО КОЛЬЦУ: СНЯТЬ ─────────

    public override void ServerInteract(PlayerInteractor interactor)
    {
        HydrantNut outlet = _attachedOutlet.Value as HydrantNut;
        if (outlet != null)
        {
            if (interactor == null || interactor.Hand == null || interactor.Hand.HasItem)
                return;
            outlet.ServerDetachRing(interactor.Hand);
            return;
        }
        base.ServerInteract(interactor);
    }

    // ───────── СИДИТ НА СЛОТЕ ─────────

    private void LateUpdate()
    {
        HydrantNut outlet = _attachedOutlet.Value as HydrantNut;
        // Садимся на СВОЮ точку кольца (RingSeat), а не на точку ключа.
        // Если точка не назначена — запасной вариант: сам объект выхода, чтобы
        // кольцо хотя бы прилипло к гидранту, а не висело там, где выпало.
        Transform a = outlet != null ? (outlet.RingSeat != null ? outlet.RingSeat : outlet.transform) : null;
        if (a == null) return;
        transform.SetPositionAndRotation(
            a.position + a.rotation * _mountPositionOffset,
            a.rotation * Quaternion.Euler(_mountEulerOffset));
    }

    public override void UpdateHeldPose(Transform handAnchor)
    {
        if (_attachedOutlet.Value != null) return;
        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);
        transform.SetPositionAndRotation(pos, rot);
    }
}
