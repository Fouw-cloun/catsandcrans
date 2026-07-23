using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Рука игрока — слот на один предмет. Живёт на префабе игрока.
/// Хранит держимый предмет в SyncVar, чтобы все клиенты знали, что у этого
/// игрока в руке. На клиенте-владельце каждый кадр ведёт предмет к точке кисти.
/// </summary>
public class PlayerHand : NetworkBehaviour
{
    [Header("Ссылки")]
    [Tooltip("Точка кисти — куда крепится предмет. Сделай дочерней к CameraRoot.")]
    [SerializeField] private Transform _handAnchor;
    [Tooltip("Контроллер игрока (для штрафа скорости при переноске тяжёлого)")]
    [SerializeField] private FirstPersonController _controller;
    [Tooltip("Коллайдер тела игрока (обычно CharacterController) — нужен, чтобы " +
             "брошенный предмет временно не сталкивался со своим же хозяином")]
    [SerializeField] private Collider _ownerCollider;

    // Текущий предмет в руке (синхронизируется всем).
    private readonly SyncVar<NetworkObject> _heldItem = new(null);

    public Transform HandAnchor => _handAnchor;
    public InteractableItem HeldItem { get; private set; }
    public bool HasItem => _heldItem.Value != null;

    /// <summary>Коллайдер тела игрока. Если не назначен в инспекторе — пробуем
    /// взять CharacterController автоматически (он сам является Collider).</summary>
    public Collider OwnerCollider
    {
        get
        {
            if (_ownerCollider == null) _ownerCollider = GetComponent<CharacterController>();
            return _ownerCollider;
        }
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _heldItem.OnChange += OnHeldChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _heldItem.OnChange -= OnHeldChanged;
    }

    private void OnHeldChanged(NetworkObject prev, NetworkObject next, bool asServer)
    {
        HeldItem = next != null ? next.GetComponent<InteractableItem>() : null;

        // Штраф скорости при переноске (например, кастрюли) — только у владельца.
        if (base.IsOwner && _controller != null)
            _controller.SpeedMultiplier = HeldItem != null ? HeldItem.CarrySpeedMultiplier : 1f;
    }

    private void Update()
    {
        // Предмет ведёт к руке только клиент-владелец (он же владелец предмета).
        if (!base.IsOwner || HeldItem == null) return;
        HeldItem.UpdateHeldPose(_handAnchor);
    }

    // ───────── СЕРВЕРНЫЕ МЕТОДЫ ─────────

    /// <summary>Попытаться взять предмет. Возвращает true при успехе.</summary>
    public bool ServerTryHold(InteractableItem item)
    {
        if (_heldItem.Value != null) return false; // рука занята
        _heldItem.Value = item.NetworkObject;
        item.ServerOnPickup(this);
        return true;
    }

    /// <summary>Бросить текущий предмет. Force=0 (по умолчанию, G) — предмет
    /// просто падает под гравитацией без полёта. Force>0 — толчок в направлении
    /// tossDirection (заряженный ЛКМ-бросок ингредиентов).</summary>
    public void ServerDrop(Vector3 tossDirection, float force = 0f)
    {
        if (_heldItem.Value == null) return;
        InteractableItem item = _heldItem.Value.GetComponent<InteractableItem>();
        _heldItem.Value = null;
        if (item != null) item.ServerOnDrop(this, tossDirection, force);
    }
}
