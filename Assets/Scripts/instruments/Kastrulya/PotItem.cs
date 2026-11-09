using UnityEngine;

/// <summary>
/// ПЕРЕНОСИМАЯ ПОСУДА. Вешается на кастрюлю ВМЕСТО голого NetworkObject —
/// это она делает кастрюлю предметом, который можно взять в руки.
///
///   F   — взять / положить (база)
///   ЛКМ — поставить на конфорку, на которую смотришь
///   ПКМ — СНЯТЬ КРЫШКУ. Это и есть спасение от разрыва: без крышки
///         давление не копится и тут же стравливается.
///   G   — бросить (база)
///
/// ПОЧЕМУ ОТДЕЛЬНЫЙ КОМПОНЕНТ, А НЕ НАСЛЕДНИК У CookingVessel:
///   ёмкостью может быть и стационарный чан, и барабан бетономешалки — им
///   поднимаемость не нужна и даже вредна. Поэтому CookingVessel остаётся
///   композицией, а «предметность» добавляется вот этим компонентом только
///   той посуде, которую реально носят.
///
/// Полная кастрюля тяжелее: скорость игрока падает вместе с массой содержимого.
/// </summary>
public class PotItem : InteractableItem
{
    [Header("Положение в руке")]
    [SerializeField] private Vector3 _holdPositionOffset = new Vector3(0f, -0.1f, 0.45f);
    [SerializeField] private Vector3 _holdEulerOffset = Vector3.zero;

    [Header("Постановка на плиту")]
    [Tooltip("Дальность, с которой можно поставить, метры")]
    [SerializeField] private float _placeReach = 3f;

    [Tooltip("Слои плиты. Можно оставить всё: код всё равно проверяет " +
             "наличие StoveBurner.")]
    [SerializeField] private LayerMask _stoveMask = ~0;

    [Header("Вес содержимого")]
    [Tooltip("Скорость игрока с ПУСТОЙ посудой")]
    [Range(0.2f, 1f)][SerializeField] private float _emptySpeed = 1f;

    [Tooltip("Скорость с ПОЛНОЙ. Полный чан супа таскать тяжело.")]
    [Range(0.1f, 1f)][SerializeField] private float _fullSpeed = 0.55f;

    private CookingVessel _vessel;
    private PotLidSocket _lid;

    /// <summary>
    /// Чем полнее посуда, тем медленнее идёшь. Читается каждый раз при взятии
    /// предмета, поэтому достаточно вернуть значение здесь.
    /// </summary>
    public override float CarrySpeedMultiplier
    {
        get
        {
            if (_vessel == null || _vessel.Def == null || _vessel.Def.CapacityG <= 0)
                return _emptySpeed;

            float load = Mathf.Clamp01(
                (float)(_vessel.Def.CapacityG - _vessel.FreeSpaceG) / _vessel.Def.CapacityG);

            return Mathf.Lerp(_emptySpeed, _fullSpeed, load);
        }
    }

    protected override void Awake()
    {
        base.Awake();
        _canPickup = true;
        _vessel = GetComponent<CookingVessel>();
        _lid = GetComponent<PotLidSocket>();
    }

    /// <summary>
    /// ПКМ — снять крышку. Работает и когда кастрюля в руках, и когда стоит
    /// на столе: спасаться от разрыва надо быстро, и заставлять сперва
    /// поднимать раскалённую кастрюлю было бы издевательством.
    /// </summary>
    public override void ServerSecondaryUse(UseContext ctx)
    {
        if (_lid == null || !_lid.HasLid) return;
        _lid.ServerTakeLid();
    }

    /// <summary>
    /// ЛКМ — поставить кастрюлю на конфорку, на которую смотришь.
    ///
    /// Ставим через саму конфорку: она знает, где у неё тарелка, и выравнивает
    /// посуду ровно. Просто бросить кастрюлю сверху не годится — она плюхнется
    /// и укатится, тем более с кипятком.
    /// </summary>
    public override void ServerPrimaryUse(UseContext ctx)
    {
        if (!Physics.Raycast(ctx.Origin, ctx.Direction, out RaycastHit hit, _placeReach,
                             _stoveMask, QueryTriggerInteraction.Ignore))
            return;

        StoveBurner burner = hit.collider.GetComponentInParent<StoveBurner>();
        if (burner == null) return;

        if (!burner.IsFree)
        {
            Debug.Log("[Плита] конфорка занята", this);
            return;
        }

        // Из руки отпускаем ДО постановки: пока кастрюля в руке, её позу
        // каждый кадр перебивает PlayerHand, и она тут же уехала бы обратно.
        if (ctx.Hand != null && ctx.Hand.HeldItem == this)
            ctx.Hand.ServerDrop(Vector3.zero, 0f);

        if (_vessel != null) burner.ServerPlace(_vessel);
    }

    /// <summary>
    /// Поза в руке с оффсетом. Базовая реализация ставит предмет ЖЁСТКО в точку
    /// кисти и про оффсеты не знает — их применяет каждый предмет сам, как это
    /// сделано в болгарке и бензопиле. Без этого метода поля ниже просто
    /// лежали бы в инспекторе и ни на что не влияли.
    ///
    /// Оффсет позиции — в системе координат руки, поворот — относительно неё.
    /// </summary>
    public override void UpdateHeldPose(Transform handAnchor)
    {
        Vector3 pos = handAnchor.position + handAnchor.rotation * _holdPositionOffset;
        Quaternion rot = handAnchor.rotation * Quaternion.Euler(_holdEulerOffset);
        transform.SetPositionAndRotation(pos, rot);
    }
}
