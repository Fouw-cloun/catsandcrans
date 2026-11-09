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
///
/// ─────────────────────────────────────────────────────────────────────────
/// ИЗМЕНЕНО ПОД UI:
///
/// 1. Пока открыто модальное окно (UiModal.AnyOpen), ввод не читается. Без
///    этого клик по кнопке «Выход из игры» в меню одновременно уходил бы
///    в CmdUse и бил кувалдой по миру за спиной.
///
/// 2. Появились публичные AimOrigin / Reach / InteractMask. Прицел-лапка
///    берёт параметры луча отсюда, а не держит свою копию в инспекторе.
///    Раньше при изменении _reach в одном месте прицел начинал врать:
///    подсвечивал объект, до которого F уже не дотягивается.
///
/// 3. Отладочные Debug.Log при каждом нажатии F убраны под галку
///    _debugRaycast. В сетевой игре они засоряли консоль так, что реальные
///    предупреждения FishNet в ней терялись.
/// ─────────────────────────────────────────────────────────────────────────
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

    [Header("Отладка")]
    [Tooltip("Писать в консоль, во что попал луч при нажатии F")]
    [SerializeField] private bool _debugRaycast;

    private PlayerHand _hand;
    public PlayerHand Hand => _hand;

    // ───────── параметры луча наружу ─────────
    // Прицел-лапка читает их, чтобы гарантированно целиться туда же, куда F.

    /// <summary>Точка и направление прицеливания. Никогда не null.</summary>
    public Transform AimOrigin => _aimOrigin != null ? _aimOrigin : transform;

    /// <summary>Дальность взаимодействия.</summary>
    public float Reach => _reach;

    /// <summary>Слои, по которым ищем предметы.</summary>
    public LayerMask InteractMask => _interactMask;

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

        // Открыто меню — мир не трогаем. Иначе клик по кнопке в интерфейсе
        // уходит ещё и в применение предмета.
        if (UiModal.AnyOpen)
        {
            // Заряд сбрасываем: игрок отпустит ЛКМ уже над кнопкой меню, и
            // без сброса при закрытии меню улетел бы бросок на полную силу.
            _charging = false;
            return;
        }

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
        //
        // Исключение — инструменты, которые работают в связке с оборудованием:
        // ключ на гидранте, кольцо шланга, дуло пылесоса. Раньше они были
        // перечислены здесь списком типов, и каждый новый приходилось дописывать
        // вручную, иначе он молча переставал жать кнопки. Теперь предмет
        // объявляет это сам через AllowInteractWhileHeld.
        if (_hand.HasItem && !_hand.HeldItem.AllowInteractWhileHeld) return;

        Transform origin = AimOrigin;
        if (!Physics.Raycast(origin.position, origin.forward, out RaycastHit hit,
                             _reach, _interactMask))
        {
            if (_debugRaycast) Debug.Log("[F] Луч никуда не попал");
            return;
        }

        if (_debugRaycast)
        {
            Debug.Log($"[F] Попал в: {hit.collider.name}, слой: " +
                      LayerMask.LayerToName(hit.collider.gameObject.layer));
        }

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
        CmdDrop(AimOrigin.forward);
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
        Transform origin = AimOrigin;
        CmdUse(primary, origin.position, origin.forward, charge);
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
