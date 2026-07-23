using UnityEngine;

/// <summary>
/// Шасси автокрана (кабина руления). Колёсная физика (WheeledVehicle).
///   WASD — езда (только с ПОДНЯТЫМИ опорами), F — выйти.
///
/// В шасси можно сесть ВСЕГДА (и с поднятыми, и с опущенными опорами) — просто
/// с опущенными опорами машина не едет. Когда опоры опущены, физика колёс
/// выключается (WheelPhysicsActive=false), иначе подвеска растягивает/отрывает
/// колёса, пока корпус приподнят на опорах.
///
/// Одна из двух кабин на объекте крана (вторая — CraneOperatorSeat). За вход в
/// НУЖНУЮ кабину отвечает PlayerInteractor (по коллайдеру сиденья, на который
/// смотрит игрок), см. seatCollider ниже.
/// </summary>
public class MobileCraneChassis : WheeledVehicle
{
    [Header("Кран")]
    [SerializeField] private CraneState _craneState;

    // В шасси садимся всегда.
    protected override bool CanEnter(PlayerInteractor interactor) => true;

    // Ехать нельзя, если опоры выдвинуты.
    protected override bool CanDrive
        => _craneState == null || !_craneState.IsDeployed;

    // Физика колёс выключается при опущенных опорах.
    protected override bool WheelPhysicsActive
        => _craneState == null || !_craneState.IsDeployed;
}
