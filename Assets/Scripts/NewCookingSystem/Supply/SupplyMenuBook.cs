using System.Collections.Generic;

namespace CatCooks.Food.Supply
{
    /// <summary>Обед или ужин.</summary>
    public enum MealSlot : byte { Lunch = 0, Dinner = 1 }

    /// <summary>Одна позиция поставки: продукт (по названию, как в базе) и сколько ПРЕДМЕТОВ привезти.</summary>
    public readonly struct SupplyLine
    {
        public readonly string Product;
        public readonly int Count;
        public SupplyLine(string product, int count) { Product = product; Count = count; }
    }

    /// <summary>Блюдо меню: название и сырьё, которое привозят на этаж (полуфабрикаты игроки делают сами).</summary>
    public sealed class SupplyDish
    {
        public readonly string Title;
        public readonly SupplyLine[] Lines;
        public SupplyDish(string title, params SupplyLine[] lines) { Title = title; Lines = lines; }
    }

    /// <summary>
    /// v24.0: МЕНЮ УРОВНЯ — основные блюда, которые уже готовятся в игре (без десертов).
    /// На уровень случайно выбираются два разных: одно на обед, другое на ужин.
    ///
    /// Количество — ПРЕДМЕТЫ, которые приедут: штуки (картофелина, кусок мяса), а мешок, банка, бидон, пучок —
    /// это 1 (источник: из него черпают/сыплют/льют сколько надо). Жидкое и пасты без своего префаба
    /// приезжают ВЁДРАМИ с содержимым: «1» = одно полное ведро (см. SupplyCatalog.BucketPortions).
    /// Если обед и ужин просят одно и то же: штучное складывается, источники (мешки, банки) — берётся больше из двух.
    /// </summary>
    public static class SupplyMenuBook
    {
        static SupplyLine L(string p, int n = 1) => new SupplyLine(p, n);

        public static readonly SupplyDish[] Dishes =
        {
            new SupplyDish("Борщ",
                L("Кости для бульона", 4), L("Говяжья голяшка", 1), L("Свёкла", 3), L("Капуста белокочанная", 1), L("Картофель", 6),
                L("Морковь", 2), L("Лук репчатый", 3), L("Помидор", 2), L("Томатная паста"), L("Лавровый лист"),
                L("Сало", 1), L("Масло подсолнечное"), L("Уксус"), L("Соль"), L("Зубчик чеснока", 4), L("Укроп"), L("Сметана"),
                L("Мука пшеничная"), L("Дрожжи")),

            new SupplyDish("Котлеты с пюре",
                L("Говядина", 2), L("Свинина", 1), L("Буханка", 1), L("Молоко"), L("Лук репчатый", 2), L("Соль"), L("Перец чёрный"),
                L("Картофель", 6), L("Сливочное масло"), L("Масло подсолнечное")),

            new SupplyDish("Бефстроганов",
                L("Говядина", 2), L("Лук репчатый", 3), L("Масло подсолнечное"), L("Сливочное масло"), L("Мука пшеничная"),
                L("Сметана"), L("Томатная паста"), L("Соль"), L("Перец чёрный")),

            new SupplyDish("Телятина по-орловски",
                L("Телячья корейка", 1), L("Шампиньоны", 8), L("Лук репчатый", 4), L("Сливочное масло"), L("Масло подсолнечное"),
                L("Тимьян"), L("Сливки"), L("Мука пшеничная"), L("Молоко"), L("Сыр твёрдый", 1), L("Мускатный орех"), L("Соль")),

            new SupplyDish("Пельмени",
                L("Мука пшеничная"), L("Яйцо", 4), L("Соль"), L("Говядина", 1), L("Свинина", 1), L("Лук репчатый", 2),
                L("Перец чёрный"), L("Укроп"), L("Сметана")),

            new SupplyDish("Ньокки с маслом и шалфеем",
                L("Картофель", 6), L("Мука пшеничная"), L("Яйцо", 2), L("Соль"), L("Сливочное масло"), L("Шалфей"), L("Сыр твёрдый", 1)),

            new SupplyDish("Рёбра барбекю",
                L("Рёбра", 1), L("Соль"), L("Паприка"), L("Перец чёрный"), L("Чеснок сушёный"),
                L("Яблочный сок"), L("Яблочный уксус"), L("Бурбон"),
                L("Соус барбекю"), L("Коричневый сахар"), L("Мёд"), L("Вустерский соус")),

            new SupplyDish("Брискет горячего копчения",
                L("Брискет", 1), L("Перец чёрный"), L("Соль"), L("Коричневый сахар"), L("Горчица"),
                L("Яблочный сок"), L("Яблочный уксус"), L("Бурбон")),
        };

        /// <summary>Индекс блюда по названию (−1 — нет).</summary>
        public static int IndexOf(string title)
        {
            for (int i = 0; i < Dishes.Length; i++) if (Dishes[i].Title == title) return i;
            return -1;
        }

        /// <summary>
        /// Сложить сырьё двух блюд: штучное — сумма, источники (мешок, банка, бидон, пучок, ведро) — больше из двух.
        /// isSource — у продукта приезжает источник, а не штука (решает каталог).
        /// </summary>
        public static List<SupplyLine> Merge(SupplyDish a, SupplyDish b, System.Func<string, bool> isSource)
        {
            var order = new List<string>();
            var sum = new Dictionary<string, int>();
            void Add(SupplyDish d)
            {
                if (d == null) return;
                var seen = new Dictionary<string, int>();
                foreach (SupplyLine l in d.Lines)
                {
                    if (string.IsNullOrEmpty(l.Product) || l.Count <= 0) continue;
                    seen.TryGetValue(l.Product, out int s0);
                    seen[l.Product] = s0 + l.Count;
                }
                foreach (var kv in seen)
                {
                    if (!sum.TryGetValue(kv.Key, out int have)) { order.Add(kv.Key); sum[kv.Key] = kv.Value; continue; }
                    sum[kv.Key] = isSource != null && isSource(kv.Key) ? System.Math.Max(have, kv.Value) : have + kv.Value;
                }
            }
            Add(a); Add(b);
            var res = new List<SupplyLine>(order.Count);
            foreach (string p in order) res.Add(new SupplyLine(p, sum[p]));
            return res;
        }
    }
}
