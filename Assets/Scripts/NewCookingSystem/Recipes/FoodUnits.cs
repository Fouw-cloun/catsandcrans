using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>Единица меры в рецептах.</summary>
    public enum CookUnit : byte
    {
        [InspectorName("Порция (внутренняя)")] Portion = 0,
        [InspectorName("Ведро (жидкое)")] Bucket = 1,
        [InspectorName("Мастерок (сыпучее)")] Trowel = 2,
        [InspectorName("Лопата = 2 мастерка")] Shovel = 3,
        [InspectorName("Штука (яйцо, кусок)")] Piece = 4,
        [InspectorName("Доза (закваска, щепотка)")] Dose = 5,
    }

    /// <summary>
    /// СИСТЕМА МЕР (v2, ноябрь 2026). Внутри тары всё лежит порциями; рецепты и
    /// игрок говорят дозами, мастерками, лопатами, штуками и вёдрами.
    ///
    ///   1 доза     = 1 порция            = ½ мастерка (банка ПКМ, щепотка)
    ///   1 мастерок = Portions Per Trowel = 2 порции (Capacity мастерка)
    ///   1 лопата   = 2 мастерка          = 4 порции (Capacity лопаты)
    ///   1 ведро    = Portions Per Bucket = 20 порций = 10 мастерков = 5 лопат
    ///
    /// ШТУКА в рецепте — это физический предмет (яйцо, луковица, кость). Сколько
    /// порций в нём, решает сам продукт (FoodPiece.Count) и поле «Порций в штуке»
    /// у ингредиента. Standard Piece (= лопата, 4 порции) — объём «обычной штуки»
    /// для подсказок и для новых продуктов, у которых своего размера ещё нет.
    ///
    /// Размер тары — в вёдрах (CookwareDef.Capacity = вёдра × 20): ведро 1,
    /// ковши экскаватора 2 и 4, малая бетономешалка 4, ковш 6, банка и рюмка
    /// бетона 8, большая бетономешалка 10, сковорода, вок и кастрюля по 32.
    /// Рецепт пишется НА 1 ВЕДРО основы и сам умножается на размер тары.
    ///
    /// До v2 ведро было 30 порций. Перевод тары, мастерков и рецептов — окно
    /// CatCooks → Кулинария → Новые меры. Свои числа — ассет
    /// Create → CatCooks → Меры («FoodUnits» в Resources).
    /// </summary>
    [CreateAssetMenu(menuName = "CatCooks/Меры (вёдра, мастерки)", fileName = "FoodUnits")]
    public sealed class FoodUnits : ScriptableObject
    {
        /// <summary>Версия системы мер. Рецепт с CookRecipeDef.MeasureVersion меньше — ещё не переведён.</summary>
        public const int Version = 2;
        /// <summary>Сколько порций было в ведре до v2 (для перевода старой тары).</summary>
        public const int OldBucket = 30;

        [Tooltip("Порций в ведре. v2: 20 (= 10 мастерков = 5 лопат). Было 30")]
        [Min(1)] public int PortionsPerBucket = 20;
        [Tooltip("Порций в мастерке. Доза = 1 порция = полмастерка, поэтому 2")]
        [Min(1)] public int PortionsPerTrowel = 2;
        [Tooltip("Объём «обычной штуки» в порциях (= лопата). Только для подсказок и новых продуктов")]
        [Min(1)] public int PortionsPerStandardPiece = 4;

        private static FoodUnits _cfg;
        private static bool _loaded;

        private static FoodUnits Cfg
        {
            get
            {
                if (!_loaded) { _loaded = true; _cfg = Resources.Load<FoodUnits>("FoodUnits"); }
                return _cfg;
            }
        }

        /// <summary>Перечитать ассет мер (после правки в редакторе).</summary>
        public static void Reload() { _loaded = false; _cfg = null; }

        public static int Bucket => Cfg != null ? Cfg.PortionsPerBucket : 20;
        public static int Trowel => Cfg != null ? Cfg.PortionsPerTrowel : 2;
        public static int Shovel => Trowel * 2;
        public static int Dose => 1;
        public static int StandardPiece => Cfg != null ? Cfg.PortionsPerStandardPiece : Shovel;

        public static int PortionsOf(CookUnit u)
        {
            switch (u)
            {
                case CookUnit.Bucket: return Bucket;
                case CookUnit.Trowel: return Trowel;
                case CookUnit.Shovel: return Shovel;
                case CookUnit.Dose: return Dose;
                default: return 1;      // порция; штука — порции решает продукт (см. Ingredient.ToPortions)
            }
        }

        /// <summary>
        /// «1 ведро 3 мастерка 1 доза» — разложить порции по крупным мерам (для подсказок и отчётов).
        /// liquid — жидкое (вёдра + мастерки), иначе лопаты + мастерки.
        /// </summary>
        public static string DescribeBest(int portions, bool liquid)
        {
            if (portions <= 0) return "0";
            var sb = new System.Text.StringBuilder();
            int left = portions;
            if (liquid && left >= Bucket)
            {
                int b = left / Bucket; left -= b * Bucket;
                sb.Append(b).Append(' ').Append(Plural(b, "ведро", "ведра", "вёдер"));
            }
            else if (!liquid && left >= Shovel)
            {
                int s = left / Shovel; left -= s * Shovel;
                sb.Append(s).Append(' ').Append(Plural(s, "лопата", "лопаты", "лопат"));
            }
            if (left >= Trowel)
            {
                int t = left / Trowel; left -= t * Trowel;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(t).Append(' ').Append(Plural(t, "мастерок", "мастерка", "мастерков"));
            }
            if (left > 0)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(left).Append(' ').Append(Plural(left, "доза", "дозы", "доз"));
            }
            return sb.ToString();
        }

        /// <summary>Порций в amount единиц.</summary>
        public static int ToPortions(float amount, CookUnit u) => Mathf.Max(0, Mathf.RoundToInt(amount * PortionsOf(u)));

        /// <summary>Размер тары в вёдрах (по паспортной вместимости, без запаса на перелив).</summary>
        public static float BucketsIn(Cookware cw)
        {
            if (cw == null || cw.Def == null) return 1f;
            return Mathf.Max(1f, cw.Def.Capacity / (float)Bucket);
        }

        /// <summary>
        /// ДОБАВКА — места в таре не занимает: сахар, мука, соль, специи, закваска, яйца.
        /// Сыплется/выливается и в ПОЛНУЮ тару (сверху полного ведра сливок) и никогда не
        /// «переливается через борт». Объёмные вещи (жидкости, сметана, куски) —
        /// по-прежнему считаются по вместимости.
        /// </summary>
        public static bool IsAdditive(ProductDef p, FoodState s)
        {
            if (p == null || string.IsNullOrEmpty(p.Title)) return false;
            string t = p.Title.ToLowerInvariant();
            if (t.Contains("яйц") || t.Contains("желток") || t.Contains("белок")) return true;
            if (t == "ньокки") return true;   // ньокки места «по воде» не занимают — кидаются и в полную кастрюлю
            if (t == "пельмени") return true; // пельмени — так же
            return FoodHeapLook.IsPowderName(p);
        }

        /// <summary>Чем мерить этот продукт: жидкость — вёдрами, сыпучее — мастерками, яйца — штуками.</summary>
        public static CookUnit NaturalUnit(ProductDef p, FoodState s)
        {
            if (p != null && !string.IsNullOrEmpty(p.Title))
            {
                string t = p.Title.ToLowerInvariant();
                if (t.Contains("яйц") || t.Contains("желток")) return CookUnit.Piece;
                if (t.Contains("закваск")) return CookUnit.Dose;
            }
            if (FoodHeapLook.IsPowderName(p)) return CookUnit.Trowel;
            if (FoodHeapLook.IsLiquid(p, s) || FoodHeapLook.IsMassName(p)) return CookUnit.Bucket;
            switch (FoodStateInfo.VisualOf(s))
            {
                case FoodVisual.Pile: return CookUnit.Trowel;
                case FoodVisual.Liquid: return CookUnit.Bucket;
                case FoodVisual.Mass: return CookUnit.Bucket;
                case FoodVisual.Solid:
                case FoodVisual.Pieces: return CookUnit.Piece;
            }
            return CookUnit.Portion;
        }

        /// <summary>«1 ведро», «3 мастерка», «2 шт.» — для подсказок и аналитика.</summary>
        public static string Describe(int portions, CookUnit u)
        {
            int per = PortionsOf(u);
            float v = portions / (float)per;
            string num = Mathf.Approximately(v, Mathf.Round(v)) ? Mathf.RoundToInt(v).ToString() : v.ToString("0.##");
            switch (u)
            {
                case CookUnit.Bucket: return num + " " + Plural(v, "ведро", "ведра", "вёдер");
                case CookUnit.Trowel: return num + " " + Plural(v, "мастерок", "мастерка", "мастерков");
                case CookUnit.Shovel: return num + " " + Plural(v, "лопата", "лопаты", "лопат");
                case CookUnit.Piece: return num + " шт.";
                case CookUnit.Dose: return num + " " + Plural(v, "доза", "дозы", "доз");
                default: return portions + " порц.";
            }
        }

        private static string Plural(float v, string one, string few, string many)
        {
            if (!Mathf.Approximately(v, Mathf.Round(v))) return few;
            int n = Mathf.Abs(Mathf.RoundToInt(v)) % 100;
            if (n >= 11 && n <= 14) return many;
            switch (n % 10)
            {
                case 1: return one;
                case 2: case 3: case 4: return few;
                default: return many;
            }
        }
    }
}
