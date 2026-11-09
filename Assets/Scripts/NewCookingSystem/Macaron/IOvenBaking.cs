namespace CatCooks.Food
{
    /// <summary>
    /// То, что само печётся в духовке-бочке (противень макарон). Духовка считает такие вещи
    /// «пекущимися» — от них копится давление (закрыто и люк закрыт — пар и взрыв, как у хлеба).
    /// </summary>
    public interface IOvenBaking
    {
        /// <summary>Сервер: прямо сейчас печётся.</summary>
        bool IsBakingNow { get; }
    }
}
