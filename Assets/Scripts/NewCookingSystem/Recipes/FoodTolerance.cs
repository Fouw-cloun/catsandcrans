using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Строгость ингредиента — насколько точно надо попасть в количество (правила П2.3).
    /// Числа не менять: хранятся в ассетах рецептов.
    /// </summary>
    public enum FoodStrictness : byte
    {
        [InspectorName("Старый допуск рецепта (Tolerance)")] Legacy = 0,
        [InspectorName("Строгий: идеал ровно, допустимо ±1 доза или ±15%")] Strict = 1,
        [InspectorName("Обычный: идеал ±10%, допустимо ±35%")] Normal = 2,
        [InspectorName("Свободный: идеал ±25%, допустимо ±60%")] Loose = 3,
    }

    /// <summary>Обязательность ингредиента (П2.5).</summary>
    public enum FoodIngredientNeed : byte
    {
        [InspectorName("Обязательный: без него провал")] Required = 0,
        [InspectorName("Желательный: без него не выше «допустимо»")] Desired = 1,
    }

    /// <summary>С какой стороны ошибка «больнее»: там зона «допустимо» вдвое уже (П2.4).</summary>
    public enum FoodPainSide : byte
    {
        [InspectorName("Одинаково (мало и много)")] Both = 0,
        [InspectorName("Больнее МНОГО (соль, уксус, острое, дрожжи)")] More = 1,
        [InspectorName("Больнее МАЛО (лишнее уварится)")] Less = 2,
    }

    /// <summary>Зона попадания по количеству (П2.2).</summary>
    public enum FoodZone : byte
    {
        Ideal = 0,
        Acceptable = 1,
        Fail = 2,
    }

    /// <summary>
    /// ДОПУСКИ ПО КОЛИЧЕСТВУ — три зоны: идеал, допустимо, провал (документ
    /// «CatCooks — правила готовки», раздел 2).
    ///
    ///   Строгий   идеал ровно   допустимо ±max(1 доза, 15%)
    ///   Обычный   идеал ±10%    допустимо ±max(1 доза, 35%)
    ///   Свободный идеал ±25%    допустимо ±max(1 доза, 60%)
    ///
    /// На «больной» стороне зона «допустимо» вдвое уже (округление вверх, чтобы ±1 доза не пропадала).
    /// Ингредиент, который уваривается (Reduces), перебора не боится вовсе.
    /// Обязательного нет совсем — провал; желательного нет — «допустимо».
    /// </summary>
    public static class FoodTolerance
    {
        /// <summary>Качество компонента по зоне (П8.1): идеал 100, допустимо 90 (мелочь), провал 0.</summary>
        public static float QualityOf(FoodZone z)
        {
            switch (z)
            {
                case FoodZone.Ideal: return 100f;
                case FoodZone.Acceptable: return 90f;
                default: return 0f;
            }
        }

        /// <summary>Ширина идеала и «допустимо» (в порциях, в одну сторону) для нормы need.</summary>
        public static void Slack(FoodStrictness s, int need, out int ideal, out int acceptable)
        {
            float idealK, accK;
            switch (s)
            {
                case FoodStrictness.Strict: idealK = 0f; accK = 0.15f; break;
                case FoodStrictness.Loose: idealK = 0.25f; accK = 0.60f; break;
                default: idealK = 0.10f; accK = 0.35f; break;
            }
            ideal = Mathf.FloorToInt(need * idealK + 1e-4f);
            acceptable = Mathf.Max(FoodUnits.Dose, Mathf.RoundToInt(need * accK));
            if (acceptable < ideal) acceptable = ideal;
        }

        /// <summary>
        /// В какую зону попало количество. have/need — порции. reduces — лишнее уварится.
        /// sideMore — true, если отклонение в сторону «много» (для текста ошибки).
        /// </summary>
        public static FoodZone Evaluate(int have, int need, FoodStrictness s, FoodPainSide pain,
                                        FoodIngredientNeed req, bool reduces, out bool sideMore)
        {
            sideMore = have > need;
            if (need <= 0) return FoodZone.Ideal;
            if (have <= 0) return req == FoodIngredientNeed.Desired ? FoodZone.Acceptable : FoodZone.Fail;
            if (s == FoodStrictness.Legacy) s = FoodStrictness.Normal;

            int ideal, acc;
            Slack(s, need, out ideal, out acc);
            int dev = Mathf.Abs(have - need);
            if (sideMore && reduces) return FoodZone.Ideal;          // перебор выкипит до нормы

            bool painful = (sideMore && pain == FoodPainSide.More) || (!sideMore && pain == FoodPainSide.Less);
            if (painful) acc = Mathf.Max(ideal, (acc + 1) / 2);     // вдвое уже, с округлением вверх: ±1 доза не исчезает

            if (dev <= ideal) return FoodZone.Ideal;
            if (dev <= acc) return FoodZone.Acceptable;
            return FoodZone.Fail;
        }

        /// <summary>«мало (пресно)», «много» — короткое имя отклонения для подсказки.</summary>
        public static string ZoneWord(FoodZone z, bool sideMore)
        {
            switch (z)
            {
                case FoodZone.Ideal: return "в идеале";
                case FoodZone.Acceptable: return sideMore ? "чуть много (допустимо)" : "чуть мало (допустимо)";
                default: return sideMore ? "слишком много — провал" : "слишком мало — провал";
            }
        }
    }
}
