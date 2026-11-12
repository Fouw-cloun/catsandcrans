using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЧТО ЭТО ЗА КУЧКА — единое правило для ВСЕХ, кто рисует или двигает еду:
    /// HeapAnchor тары, кучка на полу (FoodHeap), полотно лопаты/мастерка,
    /// измельчитель, перелив через край, бросок.
    ///
    /// Раньше любая кучка без явного правила рисовалась пресетом, выставленным
    /// на ней в инспекторе, — а там у всех стоит картошка (и текстура картошки).
    /// Теперь вид считается из СОСТОЯНИЯ продукта (FoodStateInfo.VisualOf — тот же
    /// класс, по которому игра решает «что этим можно взять»), а картошкин пресет
    /// остаётся только для овощей.
    ///
    ///   Порошок   закваска, сахар, мука, соль, специи; «Зачерпнуто», «Пыль», «В мешке»
    ///   Фарш      «Фарш» — свой рельеф (короткие завитки) и своя текстура, цвет продукта
    ///   Пюре      «Пюре»
    ///   Масса     сметана, масло, тесто, взбитое — гладкий наплыв
    ///   Кубики    «Кусочки», «Разделано» (мясо частями)
    ///   Жидкость  «Налито», «В таре», «Растоплено», яйцо — не кучка вообще
    ///   Своё      тёртые овощи — как настроено на кучке (режимы измельчителя)
    ///
    /// Свои правила: ассет Create → CatCooks → Вид кучек («FoodHeapLooks» в Resources).
    ///
    /// v18: сначала спрашивается таблица ВЕЩЕСТВ (FoodSubstance, Resources/FoodSubstances):
    /// если у продукта есть профиль — вид, материал и поведение (льётся/сыплется) берутся из него.
    /// Этот класс остаётся прослойкой — старый код, который его зовёт, работает как раньше.
    /// </summary>
    [CreateAssetMenu(menuName = "CatCooks/Вид кучек", fileName = "FoodHeapLooks")]
    public sealed class FoodHeapLook : ScriptableObject
    {
        [Serializable]
        public struct ByProduct
        {
            public ProductDef Product;
            [Tooltip("Любое состояние — None")] public FoodState State;
            public FoodHeapRenderer.HeapPreset Look;
        }

        [Serializable]
        public struct ByState
        {
            public FoodState State;
            public FoodHeapRenderer.HeapPreset Look;
        }

        [Tooltip("Свои правила для продуктов (важнее правил по состоянию)")]
        public List<ByProduct> Products = new List<ByProduct>();
        [Tooltip("Свои правила для состояний")]
        public List<ByState> States = new List<ByState>();

        private static FoodHeapLook _table;
        private static bool _loaded;

        private static readonly string[] PowderNames =
        {
            "закваск", "сахар", "мук", "соль", "перец молот", "паприк", "ванил", "какао", "специ", "крахмал", "дрожж", "сода",
            "миндальная смесь", "сухой руб", "чеснок суш", "руб для брискета", "горчичная паста",   // паста для брискета — кучкой, как руб
        };
        private static readonly string[] MinceNames = { "фарш" };
        private static readonly string[] ThickNames = { "паст", "меренг", "ганаш", "карамел", "соус барбекю", "глазурь", "мёд" };   // густое: берут мастерком, не льют
        private static readonly string[] MassNames = { "сметан", "морожен", "сливочное масло", "тесто", "творог", "масло с шалфеем", "масло горелое" };   // соус к ньокки — на мастерке/лопате гладкой каплей

        private static bool HasName(ProductDef p, string[] names, bool skipButter)
        {
            if (p == null || string.IsNullOrEmpty(p.Title)) return false;
            string t = p.Title.ToLowerInvariant();
            foreach (string n in names)
                if (t.Contains(n) && !(skipButter && t.Contains("масло"))) return true;
            return false;
        }

        public static bool IsPowderName(ProductDef p) => HasName(p, PowderNames, true);
        public static bool IsMassName(ProductDef p) => HasName(p, MassNames, false);
        public static bool IsThickName(ProductDef p) => HasName(p, ThickNames, false);

        private static bool IsPotato(ProductDef p)
            => p != null && !string.IsNullOrEmpty(p.Title) && p.Title.ToLowerInvariant().Contains("картоф");

        private static bool IsVegetable(ProductDef p)
            => p != null && p.Group != null && p.Group.name.IndexOf("Vegetable", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Жидкость (молоко, вода, сливки, растопленное, яйцо), а не кучка?
        /// Порошки и массы по названию — не жидкость, даже если группа «Жидкое» (закваска).
        /// </summary>
        public static bool IsLiquid(ProductDef p, FoodState state)
        {
            FoodSubstance sub = FoodSubstance.Of(p, state);                 // v18: профиль решает «льётся или нет»
            if (sub != null) return sub.Flow == SubstanceFlow.Pours;
            if (IsPowderName(p) || IsMassName(p)) return false;
            // Застывшее (шоколад) — не жидкость, даже «налитое» в таре. Густые пасты (шоколадная паста)
            // и меренга — тоже не жидкость: их берут мастерком, а не льют.
            if (p != null && (p.Solid || HasName(p, ThickNames, false))) return false;
            return FoodStateInfo.VisualOf(state) == FoodVisual.Liquid;
        }

        /// <summary>
        /// Применить вид к рендереру кучки: пресет + материалы (овощам — их текстура,
        /// остальным — без картофельного узора, фаршу — свой). Звать перед Show.
        /// </summary>
        public static void Apply(FoodHeapRenderer r, ProductDef p, FoodState s)
        {
            if (r == null) return;
            // Текстуры кучек нарисованы под картошку — оставляем их ТОЛЬКО картошке.
            // Морковь, лук и прочее — те же формы (режимы измельчителя), но без картофельного узора, своим цветом.
            r.SetLook(Resolve(p, s), IsPotato(p));
            r.SetSubstance(FoodSubstance.Of(p, s));                          // v18: масло, порошок — своим видом
            r.SetSpoiled(FoodStateInfo.IsSpoiled(s));                        // v19: испорченная масса — видно
        }

        /// <summary>Вид кучки для продукта в состоянии. Custom — оставить как настроено на кучке.</summary>
        public static FoodHeapRenderer.HeapPreset Resolve(ProductDef p, FoodState state)
        {
            if (p != null && p.Title == GnocchiProduct.Title) return FoodHeapRenderer.HeapPreset.Gnocchi;   // рифлёные ньокки, а не кубики
            FoodSubstance sub = FoodSubstance.Of(p, state);                 // v18: профиль вещества важнее слов
            if (sub != null && sub.FallbackPreset != FoodHeapRenderer.HeapPreset.Custom) return sub.FallbackPreset;
            if (!_loaded) { _loaded = true; _table = Resources.Load<FoodHeapLook>("FoodHeapLooks"); }
            if (_table != null)
            {
                foreach (ByProduct r in _table.Products)
                    if (r.Product == p && (r.State == FoodState.None || r.State == state)) return r.Look;
                foreach (ByState r in _table.States)
                    if (r.State == state) return r.Look;
            }

            // Картофель целый (сырой, мытый, чищеный, варёный) — клубнями, а не стружкой. Пюре / тёртый — ниже, как раньше.
            if (IsPotato(p))
            {
                FoodVisual pv = FoodStateInfo.VisualOf(state);
                if (pv == FoodVisual.Solid || pv == FoodVisual.None) return FoodHeapRenderer.HeapPreset.Whole;
            }
            if (IsPowderName(p)) return FoodHeapRenderer.HeapPreset.Powder;
            if (state == FoodState.Minced || HasName(p, MinceNames, false)) return FoodHeapRenderer.HeapPreset.Mince;
            if (IsMassName(p)) return FoodHeapRenderer.HeapPreset.Dollop;

            switch (FoodStateInfo.VisualOf(state))
            {
                case FoodVisual.Liquid: return FoodHeapRenderer.HeapPreset.Liquid;
                case FoodVisual.Pieces: return FoodHeapRenderer.HeapPreset.Cubes;
                case FoodVisual.Mass:
                    return state == FoodState.Puree ? FoodHeapRenderer.HeapPreset.Puree : FoodHeapRenderer.HeapPreset.Dollop;
                case FoodVisual.Pile:
                    // тёртые овощи — режим измельчителя (бруски/кубики/стружка/пюре) решает сама кучка
                    if (state == FoodState.Grated) return IsVegetable(p) ? FoodHeapRenderer.HeapPreset.Custom
                                                                          : FoodHeapRenderer.HeapPreset.GratedStrands;
                    return FoodHeapRenderer.HeapPreset.Powder;
            }
            // Целое/прочее: овощи — как настроено (картошка), остальное — нейтральные кусочки без картофельной текстуры.
            return IsVegetable(p) ? FoodHeapRenderer.HeapPreset.Custom : FoodHeapRenderer.HeapPreset.Cubes;
        }
    }
}
