using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>Обработка в рецепте — что делают с тарой и чем.</summary>
    public enum CookStepKind : byte
    {
        [InspectorName("Перемешать (вибратор)")] Mix = 1,
        [InspectorName("Заморозить (азот)")] Freeze = 2,
        [InspectorName("Сквасить (закваска)")] Ferment = 3,
        [InspectorName("Варить (кипение на костре/плите)")] Heat = 4,
        [InspectorName("Выдержать (время)")] Rest = 5,
        [InspectorName("Взбить (вибратор)")] Whip = 6,
        [InspectorName("Измельчить (отбойник в таре)")] Grind = 7,
        [InspectorName("Обжарить (сухой нагрев на огне, без жидкости)")] Roast = 8,
        [InspectorName("Растопить (тёплая тара на огне, от 40 °C)")] Melt = 9,
        [InspectorName("Карамелизовать (сухой сахар на огне, от 160 °C)")] Caramelize = 10,
        [InspectorName("Пригорает (перегрев от 200 °C)")] Scorch = 11,
        [InspectorName("Уваривать (слабый огонь: костёр без раздува; сильный — пригорит)")] Simmer = 12,
        [InspectorName("Жарить (сковорода на огне, от 95 °C)")] Fry = 13,
        [InspectorName("Тушить (на огне, от 85 °C)")] Stew = 14,
        [InspectorName("Пригорает на сильном огне (уваривание)")] Burn = 15,
        [InspectorName("Варить помешивая (на огне; миксером мешать, иначе комки)")] SimmerStir = 16,
        [InspectorName("Отбить (кувалдой по таре: удары)")] Pound = 17,
    }

    /// <summary>Кто выполняет обработки рецепта.</summary>
    public enum CookDriver : byte
    {
        [InspectorName("Книга рецептов (миксер, азот, плита)")] Book = 0,
        [InspectorName("Вибратор (взбивание: молоко → сливки → масло)")] Vibrator = 1,
        [InspectorName("Закваска (FoodCulture: сметана)")] Culture = 2,
    }

    /// <summary>
    /// РЕЦЕПТ ТАРЫ: основа + ингредиенты (в вёдрах, мастерках, штуках) + обработки
    /// по порядку = результат.
    ///
    /// Пример — Мороженое (на 1 ведро тары):
    ///   Основа       Сливки, 1 ведро, «заполняет тару» (ведро полное сливок)
    ///   Ингредиенты  Сахар 1 мастерок, Яйцо 1 шт.
    ///   Обработки    1. Перемешать (вибратор) 6 с → «Смесь для мороженого»
    ///                2. Заморозить (азот) 2.5 с
    ///   Результат    Мороженое
    /// В банке (8 вёдер) — то же × 8: 8 вёдер сливок, 8 мастерков, 8 яиц.
    ///
    /// Тара сама ведёт журнал: что, сколько и в каком порядке положили, какие
    /// обработки были (CookwareJournal). Книга рецептов (CookRecipeBook) сверяет
    /// журнал с рецептами, когда инструмент работает с тарой.
    ///
    /// Создать: Create → CatCooks → Рецепт тары; положить в папку
    /// Resources/CookRecipes (подхватится сам).
    /// </summary>
    [CreateAssetMenu(menuName = "CatCooks/Рецепт тары", fileName = "Recipe_")]
    public sealed class CookRecipeDef : ScriptableObject
    {
        [Serializable]
        public struct Ingredient
        {
            public ProductDef Product;
            [Tooltip("Или вот это вместо (желток вместо яйца)")] public ProductDef Alt;
            [Tooltip("Сколько на 1 ведро тары")] [Min(0f)] public float Amount;
            public CookUnit Unit;
            [Tooltip("Для «Штук»: сколько порций в одной штуке. Целая морковь из 7 кусочков = 7, " +
                     "луковица на 6 слайсов = 6, кость = 1. 0 — одна порция")]
            [Min(0)] public int PortionsPerPiece;
            [Tooltip("Считать только мытое/чищеное/варёное — грязное и гнилое не в счёт")]
            public bool CleanOnly;
            [Tooltip("Считать только в этом состоянии (лук — «Тёртое»). None — в любом")]
            public FoodState State;
            [Tooltip("Перебор уваривается: больше нормы — можно (жидкое льётся как льётся: сливки, молоко), " +
                     "лишнее выкипит на огне в шаге «Уваривать» / «Варить» / «Варить помешивая», пока не останется норма")]
            public bool Reduces;

            /// <summary>Сколько порций в amount единиц этого ингредиента.</summary>
            public int ToPortions(float amount) =>
                Unit == CookUnit.Piece && PortionsPerPiece > 1
                    ? Mathf.Max(0, Mathf.RoundToInt(amount * PortionsPerPiece))
                    : FoodUnits.ToPortions(amount, Unit);

            /// <summary>«5 шт.», «2 мастерка» — с учётом порций в штуке.</summary>
            public string Describe(int portions)
            {
                if (Unit == CookUnit.Piece && PortionsPerPiece > 1)
                {
                    float v = portions / (float)PortionsPerPiece;
                    return (Mathf.Approximately(v, Mathf.Round(v)) ? Mathf.RoundToInt(v).ToString() : v.ToString("0.#")) + " шт.";
                }
                return FoodUnits.Describe(portions, Unit);
            }
        }

        [Serializable]
        public struct Step
        {
            public CookStepKind Kind;
            [Tooltip("Сколько секунд обработки")] [Min(0.1f)] public float Seconds;
            [Tooltip("Во что превращается всё после этого шага (пусто — остаётся как есть, тара просто помнит шаг)")]
            public ProductDef Intermediate;
            [Tooltip("Объём после шага: 1 — как было, 2 — вдвое пышнее (взбитые белки), 0.6 — осело. 0 = 1")]
            public float VolumeK;
        }

        public string Title = "Рецепт";
        [Tooltip("Кто делает обработки. Книга — сама ведёт шаги; Вибратор и Закваска делают своё (взбивание, сквашивание), " +
                 "а рецепт нужен для аналитика, подсказок, оценки и общей базы")]
        public CookDriver Driver = CookDriver.Book;

        [Header("Основа")]
        [Tooltip("Жидкая/главная основа (Сливки). Пусто — рецепт без основы")]
        public ProductDef Base;
        [Tooltip("Основа считается только в этом состоянии (картофель — только «Пюре»). Пусто (None) — в любом")]
        public FoodState BaseState = FoodState.None;
        [Min(0f)] public float BaseAmount = 1f;
        public CookUnit BaseUnit = CookUnit.Bucket;
        [Tooltip("Основа должна заполнять тару (ведро — полное сливок, банка — 8 вёдер). " +
                 "Тогда рецепт сам умножается на размер тары")]
        public bool BaseFillsContainer = true;
        [Tooltip("Сколько от полной тары считается «полной» (0.9 = 90%)")]
        [Range(0.5f, 1f)] public float BaseMinFill = 0.9f;
        [Tooltip("Количества — на ВСЮ тару, а не на ведро (бульон: 5 луковиц на кастрюлю). " +
                 "Тогда «Основа заполняет тару» значит: основа + всё остальное вместе заполняют тару " +
                 "(кастрюля с водой доверху, овощи вытеснили лишнюю воду)")]
        public bool PerContainer = false;
        [Tooltip("В результат превращается только основа (вода → бульон), ингредиенты остаются в таре " +
                 "(сварившиеся овощи и кости — вынуть). Выключено — всё по рецепту становится результатом")]
        public bool OnlyBaseBecomesResult = false;

        [Header("Ингредиенты (на 1 ведро тары)")]
        public List<Ingredient> Ingredients = new List<Ingredient>();
        [Tooltip("Важен порядок: класть основу и ингредиенты строго как в списке")]
        public bool OrderMatters = false;
        [Tooltip("Ничего лишнего в таре")]
        public bool OnlyThese = true;
        [Tooltip("Допуск по количеству (0 — только идеально, 0.15 = ±15%). На будущее — оценка блюда")]
        [Range(0f, 0.5f)] public float Tolerance = 0f;

        [Header("Обработки по порядку")]
        public List<Step> Steps = new List<Step>();

        [Header("Результат")]
        public ProductDef Result;
        [Tooltip("Это ОШИБКА повара (перебил меренгу): аналитик не подсказывает «сделай это»")]
        public bool IsMistake;

        /// <summary>Сколько «вёдер рецепта» в таре: по полной таре или по основе.</summary>
        public int BatchesFor(Cookware cw, int basePortions)
        {
            if (PerContainer) return 1;
            if (Base == null) return Mathf.Max(1, Mathf.RoundToInt(FoodUnits.BucketsIn(cw)));
            int perBatch = Mathf.Max(1, FoodUnits.ToPortions(BaseAmount, BaseUnit));
            if (BaseFillsContainer) return Mathf.Max(1, Mathf.RoundToInt(FoodUnits.BucketsIn(cw) * FoodUnits.Bucket / perBatch));
            return Mathf.Max(1, basePortions / perBatch);
        }

        public bool Uses(ushort product)
        {
            if (Base != null && Base.Id == product) return true;
            foreach (Ingredient i in Ingredients)
                if ((i.Product != null && i.Product.Id == product) || (i.Alt != null && i.Alt.Id == product)) return true;
            foreach (Step s in Steps)
                if (s.Intermediate != null && s.Intermediate.Id == product) return true;
            return false;
        }
    }
}
