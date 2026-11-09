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
    /// СИСТЕМА МЕР. Внутри тары всё лежит порциями; рецепты и игрок говорят
    /// вёдрами, мастерками, лопатами и штуками.
    ///
    ///   1 ведро    = Portions Per Bucket порций жидкости (= вместимость ведра, 30)
    ///   1 мастерок = Portions Per Trowel порций сыпучего (= Capacity мастерка, 2)
    ///   1 лопата   = 2 мастерка (= Capacity лопаты, 4)
    ///   1 штука    = 1 порция (яйцо)
    ///
    /// Размер тары — в вёдрах: ведро 1, банка 8 (Capacity 240), дальше кастрюля,
    /// сковорода, казан. Рецепт пишется НА 1 ВЕДРО и сам умножается на размер тары:
    /// в банке мороженого — 8 вёдер сливок, 8 мастерков сахара, 8 яиц.
    ///
    /// Свои числа — ассет Create → CatCooks → Меры («FoodUnits» в Resources).
    /// </summary>
    [CreateAssetMenu(menuName = "CatCooks/Меры (вёдра, мастерки)", fileName = "FoodUnits")]
    public sealed class FoodUnits : ScriptableObject
    {
        [Min(1)] public int PortionsPerBucket = 30;
        [Min(1)] public int PortionsPerTrowel = 2;

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

        public static int Bucket => Cfg != null ? Cfg.PortionsPerBucket : 30;
        public static int Trowel => Cfg != null ? Cfg.PortionsPerTrowel : 2;
        public static int Shovel => Trowel * 2;

        public static int PortionsOf(CookUnit u)
        {
            switch (u)
            {
                case CookUnit.Bucket: return Bucket;
                case CookUnit.Trowel: return Trowel;
                case CookUnit.Shovel: return Shovel;
                default: return 1;
            }
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
