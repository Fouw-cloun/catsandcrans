using System.Collections;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Базовый класс ЛЮБОГО предмета, с которым можно взаимодействовать.
/// Вся бизнес-логика выполняется на сервере (методы Server*). Наследники
/// переопределяют нужные методы — это и есть точка масштабирования: новый
/// предмет = новый класс-наследник, а ввод и рука не меняются.
///
/// Требует на объекте: NetworkObject, NetworkTransform (Client Authoritative),
/// Collider. Rigidbody — опционально (для физичных предметов).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public abstract class InteractableItem : NetworkBehaviour
{
    [Header("База")]
    [Tooltip("Можно ли взять предмет в руку")]
    [SerializeField] protected bool _canPickup = true;

    [Header("Физика приземления (анти-скольжение)")]
    [Tooltip("Трение о землю — выше = меньше скользит после падения/броска")]
    [SerializeField] private float _groundFriction = 1f;
    [Tooltip("Упругость — 0 = не отскакивает от пола")]
    [SerializeField] private float _bounciness = 0f;

    protected Rigidbody _rb;
    protected Collider _col;

    // Материал создаётся один раз на класс — дешёво для большого пула предметов.
    private static PhysicsMaterial _sharedGroundMaterial;

    /// <summary>Жёстко ли предмет следует за рукой (база — да; баланс — нет).</summary>
    public virtual bool FollowHand => true;

    /// <summary>Множитель скорости игрока, пока он держит предмет (1 = без штрафа).</summary>
    public virtual float CarrySpeedMultiplier => 1f;

    /// <summary>
    /// Если true — ЛКМ работает как "зажать-отпустить" (заряд силы до 3 сек),
    /// а не мгновенно по нажатию. PlayerInteractor сам копит заряд и вызывает
    /// ServerPrimaryUse только при отпускании, с Charge 0..1 в UseContext.
    /// База — false (мгновенное применение, как у инструментов).
    /// </summary>
    public virtual bool ChargedPrimaryUse => false;

    /// <summary>
    /// Разрешает нажимать F по МИРУ, не выпуская этот предмет из рук:
    /// кнопки техники, рычаги, части машин.
    ///
    /// Нужно инструментам, которые работают в связке с оборудованием — ключ на
    /// гидранте, кольцо шланга, дуло пылесоса. Раньше такие предметы были
    /// перечислены списком типов прямо в PlayerInteractor, и каждый новый
    /// приходилось туда дописывать, иначе он молча переставал жать кнопки.
    /// </summary>
    public virtual bool AllowInteractWhileHeld => false;

    public bool CanPickup => _canPickup;

    protected virtual void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();

        ApplyAntiSlideMaterial();
    }

    /// <summary>
    /// Назначает коллайдеру материал с высоким трением и без отскоков, чтобы
    /// брошенные/упавшие предметы не скользили и не прыгали по полу. Если на
    /// коллайдере уже стоит свой материал (Shared Material назначен в
    /// инспекторе) — не трогаем, чтобы не сломать ручную настройку.
    /// </summary>
    private void ApplyAntiSlideMaterial()
    {
        if (_col == null || _col.sharedMaterial != null) return;

        if (_sharedGroundMaterial == null)
        {
            _sharedGroundMaterial = new PhysicsMaterial("ItemAntiSlide")
            {
                dynamicFriction = _groundFriction,
                staticFriction = _groundFriction,
                bounciness = _bounciness,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }
        _col.sharedMaterial = _sharedGroundMaterial;
    }

    // ───────── ВЗАИМОДЕЙСТВИЕ (F с пустой рукой, смотрит на предмет) ─────────

    /// <summary>
    /// Вызывается на сервере, когда игрок жмёт F, целясь в предмет, с пустой
    /// рукой. База — попытка взять в руку. Техника переопределяет на «сесть».
    /// </summary>
    public virtual void ServerInteract(PlayerInteractor interactor)
    {
        if (!_canPickup) return;
        interactor.Hand.ServerTryHold(this);
    }

    // ───────── ВЗЯТЬ / БРОСИТЬ (на сервере) ─────────

    public virtual void ServerOnPickup(PlayerHand hand)
    {
        // Владение предметом — держателю: его клиент будет двигать предмет
        // локально без лага, а client-authoritative NetworkTransform
        // разошлёт позицию остальным.
        NetworkObject.GiveOwnership(hand.Owner);
        SetPhysicsHeld(true);
    }

    public virtual void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
    {
        SetPhysicsHeld(false);
        NetworkObject.RemoveOwnership(); // вернуть серверу

        // Толчок вперёд — ТОЛЬКО если force явно передан (заряженный ЛКМ-бросок
        // ингредиентов). Обычный G по умолчанию force=0 — предмет просто падает
        // под гравитацией, без полёта вверх/вперёд.
        if (force > 0f && _rb != null && !_rb.isKinematic)
            _rb.AddForce(tossDirection * force, ForceMode.VelocityChange);

        // Не даём предмету застрять/вытолкнуться из игрока, который его бросил:
        // на короткое время игнорируем столкновение именно с этим игроком.
        Collider ownerCol = hand.OwnerCollider;
        if (ownerCol != null) IgnoreCollisionWith(ownerCol, 0.4f);
    }

    // ───────── ПРИМЕНЕНИЕ (ЛКМ / ПКМ) ─────────

    public virtual void ServerPrimaryUse(UseContext ctx) { }
    public virtual void ServerSecondaryUse(UseContext ctx) { }

    // ───────── ПОЗА В РУКЕ (на клиенте-держателе каждый кадр) ─────────

    /// <summary>
    /// Как предмет располагается в руке. База — жёстко в точке кисти.
    /// Переопределяется балансными предметами (наклон/качание).
    /// </summary>
    /// <summary>v17.3: рука сама не даёт предмету пройти сквозь стены/стол/предметы (PlayerHand + HeldBlocker).
    /// false — у предмета своя логика (пила, горелки, отбойник, кувалда, вибратор, ведро на дужке, бидон).</summary>
    public virtual bool UseHeldBlocking => true;

    public virtual void UpdateHeldPose(Transform handAnchor)
    {
        transform.SetPositionAndRotation(handAnchor.position, handAnchor.rotation);
    }

    // ───────── ХЕЛПЕРЫ ─────────

    protected void SetPhysicsHeld(bool held)
    {
        if (_rb != null)
        {
            _rb.isKinematic = held;
            _rb.useGravity = !held;
            if (held) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
        }
        // Коллайдер выключаем в руке, чтобы предмет не толкал самого игрока.
        if (_col != null) _col.enabled = !held;
    }

    /// <summary>Временно игнорировать столкновение с указанным коллайдером
    /// (например, с игроком сразу после броска), затем включить обратно.</summary>
    protected void IgnoreCollisionWith(Collider other, float duration)
    {
        if (_col == null || other == null) return;
        Physics.IgnoreCollision(_col, other, true);
        if (duration > 0f) StartCoroutine(ReenableCollisionRoutine(other, duration));
        else Physics.IgnoreCollision(_col, other, false);
    }

    private IEnumerator ReenableCollisionRoutine(Collider other, float duration)
    {
        yield return new WaitForSeconds(duration);
        if (_col != null && other != null)
            Physics.IgnoreCollision(_col, other, false);
    }
}
