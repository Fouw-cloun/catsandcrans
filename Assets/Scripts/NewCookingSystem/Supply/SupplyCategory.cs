using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food.Supply
{
    /// <summary>Тип ящика поставки — по нему знак на ящике, цвет полосы и надпись.</summary>
    public enum SupplyCategory : byte
    {
        Meat = 0,       // МЯСО
        Vegetables = 1, // ОВОЩИ
        Fruits = 2,     // ФРУКТЫ
        Spices = 3,     // СПЕЦИИ
        Seafood = 4,    // МОРЕПРОДУКТЫ
        Dairy = 5,      // МОЛОЧКА (и яйца)
        Grocery = 6,    // БАКАЛЕЯ (мешки, бутылки, банки, хлеб)
    }

    /// <summary>
    /// v24.0: ТИПЫ ЯЩИКОВ ПОСТАВКИ — название, цвет и знак (пиктограмма рисуется кодом в текстуру, ассеты не нужны).
    /// Продукт → тип: сначала по названию (точные слова), потом по группе продукта (FoodGroupDef.Title).
    /// </summary>
    public static class SupplyCategories
    {
        public const int Count = 7;

        public static string Title(SupplyCategory c)
        {
            switch (c)
            {
                case SupplyCategory.Meat: return "МЯСО";
                case SupplyCategory.Vegetables: return "ОВОЩИ";
                case SupplyCategory.Fruits: return "ФРУКТЫ";
                case SupplyCategory.Spices: return "СПЕЦИИ";
                case SupplyCategory.Seafood: return "МОРЕПРОДУКТЫ";
                case SupplyCategory.Dairy: return "МОЛОЧКА";
                default: return "БАКАЛЕЯ";
            }
        }

        public static Color Tint(SupplyCategory c)
        {
            switch (c)
            {
                case SupplyCategory.Meat: return new Color(0.80f, 0.17f, 0.15f);
                case SupplyCategory.Vegetables: return new Color(0.24f, 0.62f, 0.22f);
                case SupplyCategory.Fruits: return new Color(0.97f, 0.56f, 0.10f);
                case SupplyCategory.Spices: return new Color(0.58f, 0.30f, 0.66f);
                case SupplyCategory.Seafood: return new Color(0.10f, 0.33f, 0.66f);
                case SupplyCategory.Dairy: return new Color(0.45f, 0.74f, 0.97f);
                default: return new Color(0.66f, 0.52f, 0.30f);
            }
        }

        // ───────────────────────── продукт → тип ─────────────────────────

        private static readonly (string word, SupplyCategory cat)[] Words =
        {
            // морепродукты и рыба — раньше мяса (группа у них общая «Мясо и рыба»)
            ("рыб", SupplyCategory.Seafood), ("сибас", SupplyCategory.Seafood), ("треск", SupplyCategory.Seafood), ("лосос", SupplyCategory.Seafood),
            ("креветк", SupplyCategory.Seafood), ("кальмар", SupplyCategory.Seafood), ("мидии", SupplyCategory.Seafood), ("краб", SupplyCategory.Seafood),
            ("водоросл", SupplyCategory.Seafood), ("моллюск", SupplyCategory.Seafood),
            // фрукты
            ("яблок", SupplyCategory.Fruits), ("лимон", SupplyCategory.Fruits), ("апельсин", SupplyCategory.Fruits), ("груш", SupplyCategory.Fruits),
            ("банан", SupplyCategory.Fruits), ("персик", SupplyCategory.Fruits), ("ягод", SupplyCategory.Fruits), ("малин", SupplyCategory.Fruits),
            ("клубник", SupplyCategory.Fruits), ("клюкв", SupplyCategory.Fruits), ("манго", SupplyCategory.Fruits), ("вишн", SupplyCategory.Fruits), ("виноград", SupplyCategory.Fruits), ("лайм", SupplyCategory.Fruits),
            // молочка и яйца
            ("молоко", SupplyCategory.Dairy), ("сливки", SupplyCategory.Dairy), ("сметан", SupplyCategory.Dairy), ("сливочное масло", SupplyCategory.Dairy),
            ("сыр", SupplyCategory.Dairy), ("творог", SupplyCategory.Dairy), ("йогурт", SupplyCategory.Dairy), ("кефир", SupplyCategory.Dairy),
            ("яйц", SupplyCategory.Dairy), ("яичн", SupplyCategory.Dairy), ("закваск", SupplyCategory.Dairy),
            // мясо
            ("рёбр", SupplyCategory.Meat), ("ребр", SupplyCategory.Meat), ("брискет", SupplyCategory.Meat), ("кост", SupplyCategory.Meat),
            ("голяшк", SupplyCategory.Meat), ("корейк", SupplyCategory.Meat), ("говядин", SupplyCategory.Meat), ("свинин", SupplyCategory.Meat),
            ("телятин", SupplyCategory.Meat), ("сало", SupplyCategory.Meat), ("фарш", SupplyCategory.Meat), ("утк", SupplyCategory.Meat),
            ("кур", SupplyCategory.Meat), ("бекон", SupplyCategory.Meat), ("стейк", SupplyCategory.Meat), ("рулька", SupplyCategory.Meat),
            // специи и зелень (до овощей: «чеснок сушёный» — специя, «зубчик чеснока» — овощ)
            ("сушён", SupplyCategory.Spices), ("сушен", SupplyCategory.Spices), ("перец чёрн", SupplyCategory.Spices), ("перец черн", SupplyCategory.Spices),
            ("паприк", SupplyCategory.Spices), ("лавров", SupplyCategory.Spices), ("тимьян", SupplyCategory.Spices), ("шалфе", SupplyCategory.Spices),
            ("укроп", SupplyCategory.Spices), ("розмарин", SupplyCategory.Spices), ("базилик", SupplyCategory.Spices), ("мускат", SupplyCategory.Spices),
            ("ванил", SupplyCategory.Spices), ("корица", SupplyCategory.Spices), ("соль", SupplyCategory.Spices), ("фенхел", SupplyCategory.Spices),
            ("петрушк", SupplyCategory.Spices), ("кинз", SupplyCategory.Spices),
            // овощи и грибы
            ("картоф", SupplyCategory.Vegetables), ("морков", SupplyCategory.Vegetables), ("лук", SupplyCategory.Vegetables), ("свёкл", SupplyCategory.Vegetables),
            ("свекл", SupplyCategory.Vegetables), ("капуст", SupplyCategory.Vegetables), ("помидор", SupplyCategory.Vegetables), ("томат", SupplyCategory.Vegetables),
            ("чеснок", SupplyCategory.Vegetables), ("шампиньон", SupplyCategory.Vegetables), ("гриб", SupplyCategory.Vegetables), ("огур", SupplyCategory.Vegetables),
            ("перец чили", SupplyCategory.Vegetables), ("баклажан", SupplyCategory.Vegetables), ("кабач", SupplyCategory.Vegetables),
        };

        private static readonly Dictionary<ushort, SupplyCategory> _cache = new Dictionary<ushort, SupplyCategory>();

        /// <summary>Тип ящика для продукта. «Томатная паста», «соус», «уксус» и всё в бутылках/мешках — бакалея.</summary>
        public static SupplyCategory Of(ProductDef p)
        {
            if (p == null) return SupplyCategory.Grocery;
            if (_cache.TryGetValue(p.Id, out SupplyCategory c)) return c;
            c = Resolve(p);
            _cache[p.Id] = c;
            return c;
        }

        private static SupplyCategory Resolve(ProductDef p)
        {
            string t = (p.Title ?? "").ToLowerInvariant();
            // жидкое, соусы и пасты в банках — бакалея (даже «томатная паста», «яблочный сок», «масло подсолнечное»)
            if (t.Contains("паст") || t.Contains("соус") || t.Contains("уксус") || t.Contains(" сок") || t.EndsWith("сок") ||
                t.Contains("масло подсолн") || t.Contains("бурбон") || t.Contains("мёд") || t == "мед" || t.Contains("горчиц") ||
                t.Contains("сахар") || t.Contains("мука") || t.Contains("дрожж") || t.Contains("крахмал") || t.Contains("хлеб") ||
                t.Contains("буханк") || t.Contains("багет") || t.Contains("сухар"))
                return SupplyCategory.Grocery;
            foreach ((string w, SupplyCategory cat) in Words)
                if (t.Contains(w)) return cat;
            string g = p.Group != null ? (p.Group.Title ?? "").ToLowerInvariant() : "";
            if (g.Contains("мяс") || g.Contains("фарш")) return SupplyCategory.Meat;
            if (g.Contains("овощ")) return SupplyCategory.Vegetables;
            if (g.Contains("специ")) return SupplyCategory.Spices;
            if (g.Contains("сыр") || g.Contains("яйц")) return SupplyCategory.Dairy;
            return SupplyCategory.Grocery;
        }

        // ───────────────────────── знак (пиктограмма) ─────────────────────────

        private static readonly Texture2D[] _emblems = new Texture2D[Count];

        /// <summary>Знак типа: круглый жетон цвета типа с белым кантом и белой пиктограммой; вне круга — прозрачно.</summary>
        public static Texture2D Emblem(SupplyCategory c)
        {
            int i = (int)c;
            if (_emblems[i] != null) return _emblems[i];
            const int N = 256;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "CC Supply Emblem " + Title(c), wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            Color bg = Tint(c), white = new Color(1f, 0.98f, 0.94f), ink = new Color(0.12f, 0.1f, 0.09f);
            var px = new Color32[N * N];
            float aa = 2.2f / N;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / N * 2f - 1f, (y + 0.5f) / N * 2f - 1f);
                    float r = p.magnitude;
                    float disc = Mathf.Clamp01((0.98f - r) / aa + 0.5f);         // весь жетон
                    float rimOut = Mathf.Clamp01((0.98f - r) / aa + 0.5f), rimIn = Mathf.Clamp01((r - 0.86f) / aa + 0.5f);
                    float inkRing = Mathf.Clamp01((0.98f - r) / aa + 0.5f) * Mathf.Clamp01((r - 0.93f) / aa + 0.5f);
                    float icon = Mathf.Clamp01(-Icon(c, p / 0.78f) * 0.78f / aa + 0.5f) * Mathf.Clamp01((0.84f - r) / aa + 0.5f);
                    Color col = bg;
                    col = UnityEngine.Color.Lerp(col, white, rimOut * rimIn);
                    col = UnityEngine.Color.Lerp(col, ink, inkRing);
                    col = UnityEngine.Color.Lerp(col, white, icon);
                    col.a = disc;
                    px[y * N + x] = col;
                }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            _emblems[i] = tex;
            return tex;
        }

        // SDF пиктограмм в квадрате −1..1 (меньше нуля — внутри)
        private static float Icon(SupplyCategory c, Vector2 p)
        {
            switch (c)
            {
                case SupplyCategory.Meat:
                {
                    // окорок: мясная часть + кость с двумя «шишками»
                    float meat = Ellipse(Rot(p - new Vector2(-0.18f, 0.12f), 35f), new Vector2(0.55f, 0.42f));
                    float bone = Capsule(p, new Vector2(0.18f, -0.18f), new Vector2(0.55f, -0.55f), 0.09f);
                    float k1 = Circle(p - new Vector2(0.52f, -0.68f), 0.12f), k2 = Circle(p - new Vector2(0.68f, -0.52f), 0.12f);
                    float d = Mathf.Min(meat, Mathf.Min(bone, Mathf.Min(k1, k2)));
                    float fat = Ellipse(Rot(p - new Vector2(-0.3f, 0.22f), 35f), new Vector2(0.22f, 0.12f));   // прожилка-дырка
                    return Mathf.Max(d, -fat);
                }
                case SupplyCategory.Vegetables:
                {
                    // морковь по диагонали + три листа
                    Vector2 q = Rot(p, -40f);
                    float body = Cone(q - new Vector2(0f, -0.15f), 0.27f, 0.04f, 0.9f);
                    float l1 = Ellipse(q - new Vector2(0f, 0.5f), new Vector2(0.08f, 0.22f));
                    float l2 = Ellipse(Rot(q - new Vector2(-0.16f, 0.44f), 35f), new Vector2(0.07f, 0.2f));
                    float l3 = Ellipse(Rot(q - new Vector2(0.16f, 0.44f), -35f), new Vector2(0.07f, 0.2f));
                    float d = Mathf.Min(body, Mathf.Min(l1, Mathf.Min(l2, l3)));
                    float cut1 = Box(q - new Vector2(-0.09f, 0.08f), new Vector2(0.11f, 0.02f)), cut2 = Box(q - new Vector2(0.06f, -0.22f), new Vector2(0.09f, 0.02f));
                    return Mathf.Max(d, -Mathf.Min(cut1, cut2));
                }
                case SupplyCategory.Fruits:
                {
                    // яблоко: два круга + черенок + лист
                    float a = Mathf.Min(Circle(p - new Vector2(-0.2f, -0.08f), 0.5f), Circle(p - new Vector2(0.2f, -0.08f), 0.5f));
                    float notch = Circle(p - new Vector2(0f, 0.5f), 0.13f);
                    a = Mathf.Max(a, -notch);
                    float stem = Capsule(p, new Vector2(0f, 0.32f), new Vector2(0.06f, 0.68f), 0.045f);
                    float leaf = Ellipse(Rot(p - new Vector2(0.28f, 0.62f), -35f), new Vector2(0.2f, 0.09f));
                    return Mathf.Min(a, Mathf.Min(stem, leaf));
                }
                case SupplyCategory.Spices:
                {
                    // лист с прожилкой + три крупинки
                    float leaf = Ellipse(Rot(p - new Vector2(-0.08f, 0.12f), 40f), new Vector2(0.62f, 0.3f));
                    float vein = Capsule(Rot(p - new Vector2(-0.08f, 0.12f), 40f), new Vector2(-0.5f, 0f), new Vector2(0.45f, 0f), 0.025f);
                    float stem = Capsule(p, new Vector2(-0.45f, -0.25f), new Vector2(-0.62f, -0.45f), 0.04f);
                    float g1 = Circle(p - new Vector2(0.35f, -0.45f), 0.08f), g2 = Circle(p - new Vector2(0.58f, -0.3f), 0.07f), g3 = Circle(p - new Vector2(0.15f, -0.65f), 0.07f);
                    float d = Mathf.Max(leaf, -vein);
                    return Mathf.Min(d, Mathf.Min(stem, Mathf.Min(g1, Mathf.Min(g2, g3))));
                }
                case SupplyCategory.Seafood:
                {
                    // рыба: тело-эллипс, хвост-треугольник, глаз-дырка, жабра
                    float body = Ellipse(p - new Vector2(-0.12f, 0f), new Vector2(0.58f, 0.32f));
                    float tail = Triangle(p, new Vector2(0.38f, 0f), new Vector2(0.82f, 0.36f), new Vector2(0.82f, -0.36f));
                    float eye = Circle(p - new Vector2(-0.45f, 0.08f), 0.07f);
                    float gill = Capsule(p, new Vector2(-0.25f, 0.18f), new Vector2(-0.22f, -0.16f), 0.025f);
                    float d = Mathf.Min(body, tail);
                    return Mathf.Max(d, -Mathf.Min(eye, gill));
                }
                case SupplyCategory.Dairy:
                {
                    // бутылка молока + яйцо рядом
                    float bottle = RoundBox(p - new Vector2(-0.18f, -0.2f), new Vector2(0.26f, 0.42f), 0.1f);
                    float neck = Box(p - new Vector2(-0.18f, 0.32f), new Vector2(0.12f, 0.16f));
                    float cap = Box(p - new Vector2(-0.18f, 0.52f), new Vector2(0.14f, 0.05f));
                    float label = Box(p - new Vector2(-0.18f, -0.18f), new Vector2(0.18f, 0.1f));
                    float b = Mathf.Max(Mathf.Min(bottle, Mathf.Min(neck, cap)), -label);
                    float egg = Ellipse(p - new Vector2(0.45f, -0.38f), new Vector2(0.2f, 0.26f));
                    return Mathf.Min(b, egg);
                }
                default:
                {
                    // мешок: перевязанный верх + колос на мешке
                    float sack = RoundBox(p - new Vector2(0f, -0.18f), new Vector2(0.48f, 0.5f), 0.22f);
                    float neck = Box(p - new Vector2(0f, 0.42f), new Vector2(0.16f, 0.1f));
                    float top = Ellipse(p - new Vector2(0f, 0.62f), new Vector2(0.3f, 0.14f));
                    float d = Mathf.Min(sack, Mathf.Min(neck, top));
                    // колос — вырезан
                    float stalk = Capsule(p, new Vector2(0f, -0.55f), new Vector2(0f, 0.08f), 0.025f);
                    float ears = 9f;
                    for (int k = 0; k < 3; k++)
                    {
                        float y = -0.28f + k * 0.15f;
                        ears = Mathf.Min(ears, Ellipse(Rot(p - new Vector2(-0.09f, y), -35f), new Vector2(0.08f, 0.045f)));
                        ears = Mathf.Min(ears, Ellipse(Rot(p - new Vector2(0.09f, y), 35f), new Vector2(0.08f, 0.045f)));
                    }
                    ears = Mathf.Min(ears, Ellipse(p - new Vector2(0f, 0.12f), new Vector2(0.045f, 0.09f)));
                    return Mathf.Max(d, -Mathf.Min(stalk, ears));
                }
            }
        }

        private static Vector2 Rot(Vector2 p, float deg) { float a = deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a); return new Vector2(c * p.x + s * p.y, -s * p.x + c * p.y); }
        private static float Circle(Vector2 p, float r) => p.magnitude - r;
        private static float Ellipse(Vector2 p, Vector2 ab) { float k = new Vector2(p.x / ab.x, p.y / ab.y).magnitude; return (k - 1f) * Mathf.Min(ab.x, ab.y); }
        private static float Box(Vector2 p, Vector2 b) { Vector2 d = new Vector2(Mathf.Abs(p.x) - b.x, Mathf.Abs(p.y) - b.y); return Vector2.Max(d, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f); }
        private static float RoundBox(Vector2 p, Vector2 b, float r) => Box(p, b - new Vector2(r, r)) - r;
        private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(1e-6f, Vector2.Dot(ba, ba)));
            return (pa - ba * h).magnitude - r;
        }
        /// <summary>Сужающийся конус вдоль Y: радиус r0 сверху (y = +h/2) → r1 снизу (y = −h/2).</summary>
        private static float Cone(Vector2 p, float r0, float r1, float h)
        {
            float t = Mathf.Clamp01((p.y + h * 0.5f) / h);
            float r = Mathf.Lerp(r1, r0, t);
            float side = Mathf.Abs(p.x) - r;
            float cap = Mathf.Abs(p.y) - h * 0.5f;
            return Mathf.Max(side, cap);
        }
        private static float Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float e0 = Edge(p, a, b), e1 = Edge(p, b, c), e2 = Edge(p, c, a);
            bool ccw = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) > 0f;
            float s = ccw ? 1f : -1f;
            return Mathf.Max(s * e0, Mathf.Max(s * e1, s * e2));
        }
        private static float Edge(Vector2 p, Vector2 a, Vector2 b) { Vector2 e = b - a; Vector2 n = new Vector2(e.y, -e.x).normalized; return Vector2.Dot(p - a, n); }
    }
}
