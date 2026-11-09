namespace CatCooks.Food
{
    /// <summary>
    /// СОДЕРЖИМОЕ ТАРЫ ГЛАЗАМИ ФИЗИЧЕСКОГО СЛОЯ.
    ///
    /// Старый кастрюльный пласт (PotThermal, PotSpill, PotLiquidVisual,
    /// PotStatusHUD, PotItem) держал ссылку на CookingVessel и брал у него
    /// всего несколько вещей: пусто ли, насколько заполнено, очистить, отлить.
    /// Этот интерфейс и есть ровно эти вещи — без граммов, в порциях.
    ///
    /// При переносе пласта в каждом скрипте меняется одна строка:
    ///     CookingVessel _vessel      →  IVesselContents _vessel
    ///     GetComponent&lt;CookingVessel&gt;() → GetComponent&lt;IVesselContents&gt;()
    /// и граммы заменяются долями (см. таблицу в 01_СПИСОК_ПЕРЕНОСА.md).
    /// </summary>
    public interface IVesselContents
    {
        /// <summary>Внутри ничего нет. Пустая кастрюля не кипит и не давит крышку.</summary>
        bool IsEmpty { get; }

        /// <summary>Заполненность 0..1. Заменяет (CapacityG − FreeSpaceG) / CapacityG.</summary>
        float Fill01 { get; }

        /// <summary>Вместимость в порциях.</summary>
        int Capacity { get; }

        /// <summary>Сколько порций ещё влезет. Заменяет FreeSpaceG.</summary>
        int FreeSpace { get; }

        /// <summary>Всё выплеснулось (разрыв, опрокидывание). Только на сервере.</summary>
        void ServerClear();

        /// <summary>Отлить долю содержимого 0..1 (утечка, выливание).
        /// Возвращает, сколько порций реально ушло. Только на сервере.</summary>
        int ServerSpill(float fraction01);
    }

    /// <summary>
    /// ИСТОЧНИК ТЕМПЕРАТУРЫ для тары. Реализует PotThermal после переноса:
    ///     public class PotThermal : NetworkBehaviour, ITemperatureSource
    ///     public float TemperatureC => _tempC.Value;
    ///
    /// Пока его нет, тара греется флагом HeatOn, как раньше.
    /// </summary>
    public interface ITemperatureSource
    {
        float TemperatureC { get; }
    }
}
