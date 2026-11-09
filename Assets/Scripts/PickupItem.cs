using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// ПРОСТОЙ ПРЕДМЕТ. Берётся в руку, носится, бросается — и всё.
///
/// ═══ ЗАЧЕМ ОН НУЖЕН ═══
///
/// InteractableItem — АБСТРАКТНЫЙ класс. В Add Component его нет, и повесить на
/// объект нельзя: он только база. Все предметы в проекте — его наследники под
/// конкретную железку: ChainsawItem, HammerItem, PotItem, WrenchItem. Каждый
/// что-то умеет по ЛКМ.
///
/// А вещей, которые НИЧЕГО не умеют, в игре полно: палено, четвертинка, доска,
/// кирпич, мешок. Раньше под каждую пришлось бы заводить пустой класс-наследник
/// или, что хуже, вешать неподходящий ToolItem и мириться с его лучом по
/// IToolTarget на каждый клик. Этот класс закрывает дыру один раз.
///
/// ═══ ПОЧЕМУ ВЕДЁТ КОЛЛАЙДЕРЫ САМ ═══
///
/// База ищет коллайдер строчкой GetComponent&lt;Collider&gt;() НА КОРНЕ. У составного
/// предмета — палена из четырёх четвертинок, лестницы из двух секций — корень
/// пустой, там null, и тогда молча отваливаются сразу три вещи: материал против
/// скольжения, гашение коллайдеров в руке (предмет распихивает своего же
/// носильщика) и защита бросившего от собственного броска.
///
/// Поэтому здесь свой список ВСЕХ нетриггерных коллайдеров иерархии. Для
/// обычного предмета с одним коллайдером на корне поведение ровно то же, что и
/// у базы, — просто перестаёт зависеть от того, где этот коллайдер лежит.
///
/// ГАСИМ ПО SyncVar, А НЕ В ServerOnPickup. ServerOnPickup выполняется только на
/// сервере, а коллизия считается у каждого клиента своя: удалённый игрок
/// продолжал бы толкать сам себя предметом, который держит. OnChange приходит
/// всем.
///
/// Требует: NetworkObject, NetworkTransform (Client Authoritative), Rigidbody,
/// хотя бы один Collider где-нибудь в иерархии.
/// </summary>
public class PickupItem : InteractableItem
{
    [Header("Положение в руке")]
    [Tooltip("Сдвиг относительно кисти — подгонка под модель")]
    [SerializeField] private Vector3 _holdPositionOffset = Vector3.zero;

    [Tooltip("Доворот относительно кисти, градусы")]
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    [Header("Вес")]
    [Tooltip("Множитель скорости, пока предмет в руках. 1 — без штрафа.\n\n" +
             "Ставь меньше единицы для громоздкого: палено, мешок, доска.")]
    [Range(0.2f, 1f)][SerializeField] private float _carrySpeed = 1f;

    /// <summary>Кто держит. Нужен, чтобы погасить коллайдеры у ВСЕХ, а не
    /// только на сервере.</summary>
    private readonly SyncVar<NetworkObject> _holder = new(null);

    private Collider[] _solid;
    private static PhysicsMaterial _grip;

    public override float CarrySpeedMultiplier => _carrySpeed;

    protected override void Awake()
    {
        base.Awake();
        CacheSolidColliders();
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _holder.OnChange += OnHolderChanged;
        SetSolid(_holder.Value == null);
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        _holder.OnChange -= OnHolderChanged;
    }

    public override void ServerOnPickup(PlayerHand hand)
    {
        base.ServerOnPickup(hand);
        _holder.Value = hand.NetworkObject;
    }

    public override void ServerOnDrop(PlayerHand hand, Vector3 tossDirection, float force = 0f)
    {
        base.ServerOnDrop(hand, tossDirection, force);
        _holder.Value = null;
    }

    public override void UpdateHeldPose(Transform handAnchor)
    {
        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);
        transform.SetPositionAndRotation(pos, rot);
    }

    // ───────────────────────── СОСТАВНОЕ ТЕЛО ─────────────────────────

    private void CacheSolidColliders()
    {
        Collider[] all = GetComponentsInChildren<Collider>(true);
        List<Collider> solid = new List<Collider>(all.Length);

        foreach (Collider c in all)
        {
            if (c == null || c.isTrigger) continue;
            solid.Add(c);
        }
        _solid = solid.ToArray();

        if (_solid.Length == 0)
        {
            Debug.LogWarning($"[Предмет] «{name}»: ни одного обычного коллайдера " +
                             "в иерархии — предмет провалится сквозь землю и его " +
                             "нельзя будет подобрать.", this);
            return;
        }

        if (_grip == null)
        {
            _grip = new PhysicsMaterial("ItemGrip")
            {
                dynamicFriction = 1f,
                staticFriction = 1f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }

        // Ручную настройку не перебиваем: материал в инспекторе положили осознанно.
        foreach (Collider c in _solid)
            if (c.sharedMaterial == null) c.sharedMaterial = _grip;
    }

    private void SetSolid(bool on)
    {
        if (_solid == null) return;
        foreach (Collider c in _solid)
            if (c != null) c.enabled = on;
    }

    private void OnHolderChanged(NetworkObject prev, NetworkObject next, bool asServer)
    {
        // У ХОСТА OnChange приходит дважды: asServer = true, потом false.
        if (asServer && base.IsClientInitialized) return;

        SetSolid(next == null);

        if (next == null && prev != null) StartCoroutine(IgnoreDropperRoutine(prev));
    }

    /// <summary>Пару кадров не сталкиваемся с тем, кто отпустил, — иначе предмет
    /// выталкивает его из-под себя в момент броска.</summary>
    private IEnumerator IgnoreDropperRoutine(NetworkObject dropper)
    {
        if (dropper == null || _solid == null) yield break;

        Collider body = dropper.GetComponent<CharacterController>();
        if (body == null) body = dropper.GetComponentInChildren<Collider>();
        if (body == null) yield break;

        foreach (Collider c in _solid)
            if (c != null) Physics.IgnoreCollision(c, body, true);

        yield return new WaitForSeconds(0.4f);

        foreach (Collider c in _solid)
            if (c != null && body != null) Physics.IgnoreCollision(c, body, false);
    }
}
