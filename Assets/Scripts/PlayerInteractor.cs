using FishNet.Object;
using UnityEngine;

/// <summary>
/// Ввод взаимодействия на клиенте-владельце. Делает raycast из камеры, читает
/// клавиши и шлёт действия на сервер (вся логика — на сервере).
///
///   F   — взять предмет / взаимодействовать (сесть в технику)
///   G   — бросить предмет
///   ЛКМ — основное применение предмета в руке
///   ПКМ — второе применение предмета в руке
///
/// Живёт на префабе игрока рядом с PlayerHand и FirstPersonController.
/// </summary>
public class PlayerInteractor : NetworkBehaviour
{
    [Header("Прицел")]
    [Tooltip("Откуда целимся — обычно камера (CameraRoot/Main Camera)")]
    [SerializeField] private Transform _aimOrigin;
    [SerializeField] private float _reach = 3f;
    [Tooltip("Слои, по которым ищем предметы")]
    [SerializeField] private LayerMask _interactMask = ~0;

    [Header("Заряженный бросок (ЛКМ зажат)")]
    [Tooltip("Сколько секунд удержания ЛКМ даёт максимальную силу броска")]
    [SerializeField] private float _maxChargeSeconds = 3f;

    private PlayerHand _hand;
    public PlayerHand Hand => _hand;

    // Состояние заряда ЛКМ (только для предметов с ChargedPrimaryUse).
    private bool _charging;
    private float _chargeStartTime;

    /// <summary>0..1 — текущий заряд, для UI (полоска силы броска).</summary>
    public float ChargeFraction01 => _charging
        ? Mathf.Clamp01((Time.time - _chargeStartTime) / _maxChargeSeconds)
        : 0f;

    private void Awake()
    {
        _hand = GetComponent<PlayerHand>();
    }

    private void Update()
    {
        if (!base.IsOwner) return;

        if (Input.GetKeyDown(KeyCode.F)) TryInteract();
        if (Input.GetKeyDown(KeyCode.G)) TryDrop();

        HandleChargedPrimary();

        if (Input.GetMouseButtonDown(1)) TryUse(false, 1f);
    }

    // ───────── ЛКМ: мгновенно (инструмент) или заряд-отпускание (ингредиент) ─────────

    private void HandleChargedPrimary()
    {
        bool itemWantsCharge = _hand.HasItem && _hand.HeldItem.ChargedPrimaryUse;

        if (Input.GetMouseButtonDown(0))
        {
            if (itemWantsCharge) { _charging = true; _chargeStartTime = Time.time; }
            else TryUse(true, 1f); // мгновенное применение (инструмент)
        }

        // Если предмет сменился/выброшен/потерян, пока заряжали — сбросить.
        if (_charging && !itemWantsCharge) _charging = false;

        if (_charging && Input.GetMouseButtonUp(0))
        {
            float charge = ChargeFraction01;
            _charging = false;
            TryUse(true, charge); // бросок с накопленной силой
        }
    }

    // ───────── F: взять / взаимодействовать ─────────

    private void TryInteract()
    {
        // F работает с пустой рукой: подобрать предмет / сесть в технику /
        // нажать часть техники (рычаг).
        if (_hand.HasItem
    && !(_hand.HeldItem is WrenchItem)
    && !(_hand.HeldItem is HoseRingItem)) return;

        Transform origin = _aimOrigin != null ? _aimOrigin : transform;
        if (!Physics.Raycast(origin.position, origin.forward, out RaycastHit hit, _reach, _interactMask))
        {
            Debug.Log("[F] Луч никуда не попал");
            return;
        }
        Debug.Log($"[F] Попал в: {hit.collider.name}, слой: {LayerMask.LayerToName(hit.collider.gameObject.layer)}");


        // ВАЖЕН ПОРЯДОК: часть техники (рычаг) проверяем ПЕРВОЙ. Рычаг —
        // дочерний объект крана, а кран сам является InteractableItem, поэтому
        // GetComponentInParent<InteractableItem> "перехватил" бы рычаг и пытался
        // усадить игрока в кран вместо срабатывания рычага. Часть техники
        // специфичнее — у неё приоритет.
        // 1) Часть техники без своего NetworkObject (рычаг и т.п.).
        IVehiclePart part = hit.collider.GetComponentInParent<IVehiclePart>();
        if (part != null && part.OwningNetworkObject != null)
        {
            CmdInteractPart(part.OwningNetworkObject, part.PartId);
            return;
        }

        // 2) Иначе — самостоятельный предмет/техника (свой NetworkObject).
        InteractableItem item = hit.collider.GetComponentInParent<InteractableItem>();
        if (item != null)
        {
            // Если на объекте несколько кабин (автокран: шасси + кран), выбираем
            // ту, чей коллайдер сиденья совпал с задетым — чтобы сесть именно в
            // ту кабину, на которую смотрим, а не в первую попавшуюся.
            NetworkVehicle chosen = ResolveSeatByCollider(item, hit.collider);
            InteractableItem target = chosen != null ? chosen : item;
            CmdInteract(target.NetworkObject, target is NetworkVehicle nv ? nv.SeatIndexOnObject : -1);
            return;
        }
    }

    /// <summary>Среди всех кабин объекта ищет ту, чей SeatCollider = задетый
    /// коллайдер (или задетый коллайдер — потомок SeatCollider в иерархии).
    /// Если совпадений нет — null (используется дефолт).</summary>
    private NetworkVehicle ResolveSeatByCollider(InteractableItem item, Collider hitCol)
    {
        NetworkVehicle[] all = item.NetworkObject.GetComponentsInChildren<NetworkVehicle>(true);

        // Сначала точное совпадение.
        foreach (NetworkVehicle v in all)
            if (v.SeatCollider != null && v.SeatCollider == hitCol)
                return v;

        // Затем — если задетый коллайдер лежит ВНУТРИ иерархии SeatCollider
        // (например меш кабины — дочерний объект коллайдера сиденья).
        foreach (NetworkVehicle v in all)
            if (v.SeatCollider != null && hitCol.transform.IsChildOf(v.SeatCollider.transform))
                return v;

        // И наоборот — если SeatCollider дочерний задетому (крупный коллайдер).
        foreach (NetworkVehicle v in all)
            if (v.SeatCollider != null && v.SeatCollider.transform.IsChildOf(hitCol.transform)
                && all.Length > 1)
                return v;

        return null;
    }

    [ServerRpc]
    private void CmdInteract(NetworkObject target, int seatIndex)
    {
        if (target == null) return;

        // Если указана конкретная кабина (seatIndex >= 0) — вызываем её.
        if (seatIndex >= 0)
        {
            NetworkVehicle[] seats = target.GetComponentsInChildren<NetworkVehicle>(true);
            foreach (NetworkVehicle v in seats)
            {
                if (v.SeatIndexOnObject != seatIndex) continue;
                if (Vector3.Distance(transform.position, target.transform.position) > _reach + 4f)
                    return;
                v.ServerInteract(this);
                return;
            }
        }

        // Иначе — обычная одиночная техника/предмет.
        InteractableItem item = target.GetComponent<InteractableItem>();
        if (item == null) return;

        if (Vector3.Distance(transform.position, target.transform.position) > _reach + 1.5f)
            return;

        item.ServerInteract(this);
    }

    [ServerRpc]
    private void CmdInteractPart(NetworkObject owner, int partId)
    {
        if (owner == null) return;

        // Валидация дистанции до машины-владельца.
        if (Vector3.Distance(transform.position, owner.transform.position) > _reach + 4f)
            return;

        // Находим нужную часть среди частей машины по PartId и применяем.
        foreach (IVehiclePart part in owner.GetComponentsInChildren<IVehiclePart>(true))
        {
            if (part.PartId == partId)
            {
                part.ServerUse(this);
                return;
            }
        }
    }

    // ───────── G: бросить ─────────

    private void TryDrop()
    {
        if (!_hand.HasItem) return;
        Vector3 dir = _aimOrigin != null ? _aimOrigin.forward : transform.forward;
        CmdDrop(dir);
    }

    [ServerRpc]
    private void CmdDrop(Vector3 tossDirection)
    {
        _hand.ServerDrop(tossDirection);
    }

    // ───────── ЛКМ / ПКМ: применение ─────────

    private void TryUse(bool primary, float charge)
    {
        if (!_hand.HasItem) return;
        Vector3 origin = _aimOrigin != null ? _aimOrigin.position : transform.position;
        Vector3 dir = _aimOrigin != null ? _aimOrigin.forward : transform.forward;
        CmdUse(primary, origin, dir, charge);
    }

    [ServerRpc]
    private void CmdUse(bool primary, Vector3 origin, Vector3 direction, float charge)
    {
        InteractableItem item = _hand.HeldItem;
        if (item == null) return;

        UseContext ctx = new UseContext(base.Owner, origin, direction, charge, _hand);
        if (primary) item.ServerPrimaryUse(ctx);
        else item.ServerSecondaryUse(ctx);
    }
}
