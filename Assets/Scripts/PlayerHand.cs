using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Рука игрока — слот на один предмет. Живёт на префабе игрока.
/// Хранит держимый предмет в SyncVar, чтобы все клиенты знали, что у этого
/// игрока в руке. На клиенте-владельце каждый кадр ведёт предмет к точке кисти.
///
/// ─────────────────────────────────────────────────────────────────────────
/// ИСПРАВЛЕНО:
///
/// HasItem и HeldItem могли расходиться. HasItem смотрел прямо в SyncVar, а
/// HeldItem заполнялся только в колбэке OnChange. Между установкой значения и
/// приходом колбэка (а на клиенте, который подключился позже, — до первой
/// синхронизации) получалось HasItem == true при HeldItem == null.
///
/// Код по всему проекту написан в расчёте на то, что этого не бывает:
///     if (_hand.HasItem && !_hand.HeldItem.AllowInteractWhileHeld)
/// Такая строка при расхождении падает с NullReferenceException. Сейчас оба
/// свойства выводятся из одного места, и разойтись физически не могут.
/// ─────────────────────────────────────────────────────────────────────────
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

    // Кэш компонента, чтобы не звать GetComponent каждый кадр.
    private NetworkObject _cachedFor;
    private InteractableItem _cachedItem;

    public Transform HandAnchor => _handAnchor;

    /// <summary>Предмет в руке или null. Выводится напрямую из SyncVar, а не
    /// из колбэка: колбэк может ещё не успеть отработать, а обратиться к
    /// свойству код успевает в тот же кадр.</summary>
    public InteractableItem HeldItem
    {
        get
        {
            NetworkObject no = _heldItem.Value;
            if (no == null)
            {
                _cachedFor = null;
                _cachedItem = null;
                return null;
            }

            if (!ReferenceEquals(no, _cachedFor))
            {
                _cachedFor = no;
                _cachedItem = no.GetComponent<InteractableItem>();
            }
            return _cachedItem;
        }
    }

    /// <summary>Есть ли что-то в руке. Считается от HeldItem, поэтому
    /// «true при null» получиться не может.</summary>
    public bool HasItem => HeldItem != null;

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

    /// <summary>Колбэк остался только ради побочных эффектов — штрафа скорости.
    /// Само значение HeldItem он больше не выставляет.</summary>
    private void OnHeldChanged(NetworkObject prev, NetworkObject next, bool asServer)
    {
        // Сбрасываем кэш: объект сменился.
        _cachedFor = null;
        _cachedItem = null;

        // Штраф скорости при переноске (например, кастрюли) — только у владельца.
        if (!base.IsOwner || _controller == null) return;

        InteractableItem item = HeldItem;
        _controller.SpeedMultiplier = item != null ? item.CarrySpeedMultiplier : 1f;
    }

    private void Update()
    {
        // Предмет ведёт к руке только клиент-владелец (он же владелец предмета).
        if (!base.IsOwner) return;

        InteractableItem item = HeldItem;
        if (item == null) return;

        item.UpdateHeldPose(_handAnchor);
    }

    // ───────── СЕРВЕРНЫЕ МЕТОДЫ ─────────

    /// <summary>Попытаться взять предмет. Возвращает true при успехе.</summary>
    public bool ServerTryHold(InteractableItem item)
    {
        if (item == null) return false;
        CraneLoad craneLoad = item.GetComponentInParent<CraneLoad>();
        if (craneLoad != null && craneLoad.IsAttached) return false;
        CraneRider rider = GetComponent<CraneRider>();
        if (rider != null && rider.IsHanging) return false;
        if (_heldItem.Value != null) return false; // рука занята

        _heldItem.Value = item.NetworkObject;
        _cachedFor = item.NetworkObject;
        _cachedItem = item;

        item.ServerOnPickup(this);
        return true;
    }

    /// <summary>Бросить текущий предмет. Force=0 (по умолчанию, G) — предмет
    /// просто падает под гравитацией без полёта. Force>0 — толчок в направлении
    /// tossDirection (заряженный ЛКМ-бросок ингредиентов).</summary>
    public void ServerDrop(Vector3 tossDirection, float force = 0f)
    {
        if (_heldItem.Value == null) return;

        InteractableItem item = HeldItem;

        _heldItem.Value = null;
        _cachedFor = null;
        _cachedItem = null;

        if (item != null) item.ServerOnDrop(this, tossDirection, force);
    }
}
