using UnityEngine;

/// <summary>
/// Многомерный вкус блюда.
///
/// ВАЖНО: величины АБСОЛЮТНЫЕ (условные единицы на всю массу), а НЕ «уровни».
/// Воспринимаемая солёность = Salt / масса в кг. Из этого бесплатно следует:
///   - долил воды  -> масса выросла, концентрация упала -> стало пресно;
///   - выпарил     -> масса упала, концентрация выросла -> стало насыщеннее;
///   - слил две ёмкости -> вкусы просто СКЛАДЫВАЮТСЯ, без всяких усреднений.
///
/// Если хранить «уровень 0..1», на каждый из этих случаев придётся писать
/// отдельный костыль. Поэтому — абсолют.
/// </summary>
[System.Serializable]
public struct TasteVector
{
    public float Salt;    // солёность
    public float Heat;    // острота
    public float Acid;    // кислота (уксус, лимон, ткемали, рассол)
    public float Sweet;   // сладость
    public float Fat;     // жирность (сметана, ру, курдюк, сливки)

    public static readonly TasteVector Zero = new TasteVector();

    public static TasteVector operator +(TasteVector a, TasteVector b) => new TasteVector
    {
        Salt  = a.Salt  + b.Salt,
        Heat  = a.Heat  + b.Heat,
        Acid  = a.Acid  + b.Acid,
        Sweet = a.Sweet + b.Sweet,
        Fat   = a.Fat   + b.Fat
    };

    public static TasteVector operator *(TasteVector a, float k) => new TasteVector
    {
        Salt  = a.Salt  * k,
        Heat  = a.Heat  * k,
        Acid  = a.Acid  * k,
        Sweet = a.Sweet * k,
        Fat   = a.Fat   * k
    };

    /// <summary>
    /// Концентрация — то, что реально «чувствуется на язык».
    /// Именно это значение потом сравнивается с целевым коридором рецепта.
    /// </summary>
    public TasteVector PerKg(int massG)
    {
        if (massG <= 0) return Zero;
        return this * (1000f / massG);
    }

    public override string ToString() =>
        $"S{Salt:0.#} H{Heat:0.#} A{Acid:0.#} Sw{Sweet:0.#} F{Fat:0.#}";
}
