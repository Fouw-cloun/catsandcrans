using System.Collections.Generic;
using System.Reflection;
using System.Text;
using FishNet.Object;
using UnityEngine;

namespace CatCooks.Food.Supply
{
    /// <summary>v24.3: место блюда в ужине — на подносе у кота по одному каждого.</summary>
    public enum SupplyCourse : byte
    {
        [InspectorName("Горячее")] Hot = 0,
        [InspectorName("Десерт")] Dessert = 1,
        [InspectorName("Напиток")] Drink = 2,
    }

    /// <summary>В чём записано количество строки пресета.</summary>
    public enum SupplyUnit : byte
    {
        [InspectorName("Штуки (предметы)")] Pieces = 0,
        [InspectorName("Порции (дозы)")] Portions = 1,
        [InspectorName("Мастерки (×2 порции)")] Trowels = 2,
        [InspectorName("Лопаты (×4 порции)")] Shovels = 3,
        [InspectorName("Вёдра (×20 порций)")] Buckets = 4,
    }

    /// <summary>
    /// v24.2: ПРЕСЕТ ПОСТАВКИ ДЛЯ БЛЮДА.
    ///
    /// Сырьё записано «на Base Servings порций» (как в рецепте: на 24 макаруна — 8 яиц, 13 мастерков сахара…).
    /// Ставишь Portions — сколько порций блюда надо приготовить (30 макарун) — и пресет сам считает:
    ///   нужно = количество × Portions / Base Servings × (1 + запас), округление вверх;
    ///   дальше каталог решает, ЧЕМ это приедет:
    ///     • штучное (яйца, картошка, мясо) — столько штук (кусок на N порций — делится на N);
    ///     • источник (мешок, банка, солонка, бидон, пучок) — столько штук, сколько надо по ёмкости,
    ///       и в каждом РОВНО посчитанный запас (не бесконечный, если Limited Stock);
    ///     • нет префаба — вёдра с продуктом (по 20 порций, последнее — сколько осталось).
    ///   Потом всё делится на ящики по типам (мясо, молочка, бакалея…), крупное — на площадку,
    ///   ящики маркируются: тип + «для чего» (Макарон ×30).
    ///
    /// Инспектор пресета показывает расчёт сразу: что, сколько, чем, в какой ящик. В игре (хост) —
    /// кнопка «Привезти сейчас» или End (тестовый пресет в Supply Delivery).
    /// Создать: ПКМ → Create → CatCooks → Доставка → Пресет блюда, или CatCooks → Доставка → 4 (макарон ×30).
    /// </summary>
    [CreateAssetMenu(menuName = "CatCooks/Доставка/Пресет блюда", fileName = "SupplyPreset")]
    public sealed class SupplyPreset : ScriptableObject
    {
        [System.Serializable]
        public class Line
        {
            public ProductDef Product;
            [Tooltip("Количество на Base Servings порций блюда")]
            [Min(0f)] public float Amount = 1f;
            public SupplyUnit Unit = SupplyUnit.Portions;
            [Tooltip("Заметка: на что идёт (меренга, ганаш…)")]
            public string Note = "";
        }

        [Header("Блюдо")]
        public string Title = "Блюдо";
        [Tooltip("Место в ужине: горячее, десерт или напиток")]
        public SupplyCourse Course = SupplyCourse.Hot;
        [HideInInspector] public bool IsDessert;                                   // v24.2, оставлено для старых ассетов
        [Tooltip("Участвует в случайном ужине (по курсу)")]
        public bool InMenu = true;

        [Header("Сколько готовить")]
        [Tooltip("Сколько порций блюда привезти на уровень")]
        [Min(1)] public int Portions = 10;
        [Tooltip("На сколько порций записаны строки ниже")]
        [Min(1)] public int BaseServings = 1;
        [Tooltip("Запас на ошибки повара, %")]
        [Range(0, 100)] public int ReservePercent = 10;
        [Tooltip("Мешки, банки, бидоны и пучки приезжают с ровно посчитанным запасом (иначе — бесконечные)")]
        public bool LimitedStock = true;

        [Header("Сырьё на Base Servings порций")]
        public List<Line> Lines = new List<Line>();

        /// <summary>Сколько порций в единице.</summary>
        public static float PortionsPer(SupplyUnit u)
        {
            switch (u)
            {
                case SupplyUnit.Trowels: return 2f;
                case SupplyUnit.Shovels: return 4f;
                case SupplyUnit.Buckets: return 20f;
                default: return 1f;
            }
        }
    }

    /// <summary>Строка расчёта: продукт, сколько нужно и чем приедет.</summary>
    public sealed class SupplyPlanLine
    {
        public ProductDef Product;
        public bool Pieces;                 // считаем штуками (иначе порциями)
        public float Need;                  // точно (с запасом)
        public int NeedRounded;             // округлено вверх
        public string Via = "";             // «мешок ×1 (24 порции)», «ведро ×2», «штук ×11»
        public int Items;                   // сколько предметов приедет
        public SupplyCategory Category;
        public bool Pallet;
        public string Notes = "";
        public readonly List<string> Dishes = new List<string>();   // для каких блюд (маркировка ящиков)
        public string Problem;              // не приедет
    }

    /// <summary>Расчёт поставки по пресетам: строки + предметы (уже с категорией, местом и запасом).</summary>
    public sealed class SupplyPlan
    {
        public readonly List<SupplyPlanLine> Lines = new List<SupplyPlanLine>();
        public readonly List<SupplyItem> Items = new List<SupplyItem>();
        public string Tag = "";             // маркировка ящиков: «Макарон ×30 · Борщ ×8»

        public string Report()
        {
            var sb = new StringBuilder();
            sb.Append($"для: {Tag}");
            foreach (SupplyPlanLine l in Lines)
                sb.Append($"\n  {(l.Problem == null ? "✔" : "✖")} {l.Product?.Title} — {l.NeedRounded} {(l.Pieces ? "шт." : "порц.")} → {(l.Problem ?? l.Via)}");
            return sb.ToString();
        }
    }

    public static class SupplyPlanner
    {
        /// <summary>
        /// Посчитать поставку для набора (пресет, порции). Одинаковые продукты разных пресетов складываются
        /// (порции к порциям, штуки к штукам) — и только потом переводятся в мешки, банки, вёдра.
        /// </summary>
        public static SupplyPlan Plan(IList<(SupplyPreset preset, int portions)> order, SupplyCatalog cat)
        {
            var plan = new SupplyPlan();
            var acc = new List<SupplyPlanLine>();
            var limited = new Dictionary<ProductDef, bool>();
            var tag = new StringBuilder();
            foreach (var (preset, portions) in order)
            {
                if (preset == null || portions <= 0) continue;
                if (tag.Length > 0) tag.Append(" · ");
                tag.Append($"{preset.Title} ×{portions}");
                float k = portions / (float)Mathf.Max(1, preset.BaseServings) * (1f + preset.ReservePercent / 100f);
                foreach (SupplyPreset.Line l in preset.Lines)
                {
                    if (l == null || l.Product == null || l.Amount <= 0f) continue;
                    bool pcs = l.Unit == SupplyUnit.Pieces;
                    float need = l.Amount * (pcs ? 1f : SupplyPreset.PortionsPer(l.Unit)) * k;
                    SupplyPlanLine pl = acc.Find(x => x.Product == l.Product && x.Pieces == pcs);
                    if (pl == null) { pl = new SupplyPlanLine { Product = l.Product, Pieces = pcs }; acc.Add(pl); }
                    pl.Need += need;
                    if (!string.IsNullOrEmpty(l.Note)) pl.Notes += (pl.Notes.Length > 0 ? ", " : "") + l.Note;
                    if (!pl.Dishes.Contains(preset.Title)) pl.Dishes.Add(preset.Title);
                    limited[l.Product] = (limited.TryGetValue(l.Product, out bool was) && was) || preset.LimitedStock;
                }
            }
            plan.Tag = tag.ToString();
            foreach (SupplyPlanLine pl in acc)
            {
                pl.NeedRounded = Mathf.Max(1, Mathf.CeilToInt(pl.Need - 0.01f));
                int from = plan.Items.Count;
                Convert(pl, cat, limited.TryGetValue(pl.Product, out bool lim) && lim, plan.Items);
                string dish = string.Join(" · ", pl.Dishes);
                for (int i = from; i < plan.Items.Count; i++) { SupplyItem it = plan.Items[i]; it.Dish = dish; plan.Items[i] = it; }
                plan.Lines.Add(pl);
            }
            return plan;
        }

        /// <summary>Нужное количество → предметы: штуки / источники с запасом / вёдра.</summary>
        static void Convert(SupplyPlanLine pl, SupplyCatalog cat, bool limited, List<SupplyItem> items)
        {
            ProductDef p = pl.Product;
            pl.Category = cat != null ? cat.CategoryOf(p) : SupplyCategories.Of(p);
            Vector3 inner = cat != null ? cat.CrateInner : SupplyCrate.DefaultInner;
            bool Pal(SupplyCatalog.Entry en, Vector3 sz) => (en != null && en.OnPallet) || !SupplyCrate.FitsEmpty(sz, inner);
            List<SupplyCatalog.Entry> es = cat != null ? cat.For(p) : new List<SupplyCatalog.Entry>();
            SupplyCatalog.Entry src = es.Find(e => e.Source), piece = es.Find(e => !e.Source);
            int need = pl.NeedRounded;

            // v24.9: штучное в «счётном» источнике (лоток яиц) — считаем как источник: лоток на N штук
            bool countSrc = src != null && IsCountSource(src.Prefab);
            // штуками — любой штучный префаб; источник тоже годится (одна банка = «одна штука»)
            if (pl.Pieces && !countSrc)
            {
                SupplyCatalog.Entry e = piece ?? src;
                NetworkObject pf = e != null ? e.Prefab : SupplyCatalog.FindPiecePrefab(p);
                if (pf == null) { pl.Problem = "нет префаба — штуками не привезти (поставь порции, приедет ведро)"; return; }
                Vector3 s = e != null ? e.Size : SupplyDelivery.MeasurePrefab(pf.gameObject);
                bool pal = Pal(e, s);
                for (int i = 0; i < need; i++) items.Add(new SupplyItem { Prefab = pf, Product = p, Size = s, Category = pl.Category, Pallet = pal, Stock = -1 });
                pl.Items = need; pl.Pallet = pal;
                pl.Via = $"{pf.name} ×{need}";
                return;
            }

            // порциями: источник с ёмкостью
            if (src != null)
            {
                int cap = Mathf.Max(1, CapacityOf(src.Prefab));
                int n = limited ? Mathf.CeilToInt(need / (float)cap) : 1;
                bool pal = Pal(src, src.Size);
                int left = need;
                for (int i = 0; i < n; i++)
                {
                    int st = limited ? cap : -1;                       // v25.4: мешки, банки, бидоны едут ПОЛНЫМИ (было — ровно сколько нужно, мешок пустел за секунды)
                    left -= cap;
                    items.Add(new SupplyItem { Prefab = src.Prefab, Product = p, Size = src.Size, Category = pl.Category, Pallet = pal, Stock = st });
                }
                pl.Items = n; pl.Pallet = pal;
                pl.Via = limited ? $"{KindName(src.Prefab)} ×{n} (полн{(n > 1 ? "ые" : "ый")}, по {cap} порц.; нужно {need})" : $"{KindName(src.Prefab)} ×1 (бесконечный)";
                return;
            }

            // порциями: куски (кусок на N порций)
            NetworkObject pp = piece != null ? piece.Prefab : SupplyCatalog.FindPiecePrefab(p);
            if (pp != null)
            {
                int per = Mathf.Max(1, piece != null && piece.PortionsPerItem > 0 ? piece.PortionsPerItem : PiecePortions(pp));   // v24.8
                int n = Mathf.CeilToInt(need / (float)per);
                Vector3 s = piece != null ? piece.Size : SupplyDelivery.MeasurePrefab(pp.gameObject);
                bool pal = Pal(piece, s);
                for (int i = 0; i < n; i++) items.Add(new SupplyItem { Prefab = pp, Product = p, Size = s, Category = pl.Category, Pallet = pal, Stock = -1 });
                pl.Items = n; pl.Pallet = pal;
                pl.Via = per > 1 ? $"{pp.name} ×{n} (по {per} порц.)" : $"{pp.name} ×{n}";
                return;
            }

            // вёдра
            if (cat != null && cat.BucketPrefab != null)
            {
                int per = Mathf.Max(1, cat.BucketPortions);
                int n = Mathf.CeilToInt(need / (float)per);
                Vector3 s = SupplyDelivery.MeasurePrefab(cat.BucketPrefab.gameObject);
                int left = need;
                for (int i = 0; i < n; i++)
                {
                    items.Add(new SupplyItem { Prefab = cat.BucketPrefab, Product = p, Bucket = true, Portions = Mathf.Min(per, left), Size = s, Category = pl.Category, Pallet = Pal(null, s), Stock = -1 });
                    left -= per;
                }
                pl.Items = n; pl.Pallet = Pal(null, s);
                pl.Via = $"⚠ ведро ×{n} (своего префаба нет)";
                return;
            }
            pl.Problem = "НЕ ПРИЕДЕТ: нет префаба и ведра в каталоге";
        }

        static float MaxSide(Vector3 s) => Mathf.Max(s.x, Mathf.Max(s.y, s.z));

        /// <summary>Источник, из которого берут ШТУКИ (лоток яиц): штучная строка меню едет в нём.</summary>
        public static bool IsCountSource(NetworkObject pf) => pf != null && pf.GetComponent<SupplyEggTray>() != null;

        static string KindName(NetworkObject pf)
        {
            if (pf == null) return "?";
            foreach (MonoBehaviour mb in pf.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                switch (mb.GetType().Name)
                {
                    case "BulkSack": return "мешок";
                    case "LiquidDoseJarItem": return "банка";
                    case "PowderShakerItem": return "банка-солонка";
                    case "MilkCanItem": return "бидон";
                    case "HerbBunchItem": return "пучок";
                    case "SupplyEggTray": return "ящик яиц";
                }
            }
            return pf.name;
        }

        // ───────────── запас источников (приватные поля — через отражение) ─────────────

        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        static FieldInfo Field(object o, string n)
        {
            for (System.Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(n, F | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }

        static int GetInt(object o, string n, int def) { FieldInfo f = Field(o, n); return f != null && f.FieldType == typeof(int) ? (int)f.GetValue(o) : def; }
        static void Set(object o, string n, object v) { FieldInfo f = Field(o, n); if (f != null && f.FieldType == v.GetType()) f.SetValue(o, v); }

        /// <summary>Сколько порций в одном источнике (мешок, банка, бидон, пучок) по его настройкам.</summary>
        public static int CapacityOf(NetworkObject pf)
        {
            if (pf == null) return 1;
            foreach (MonoBehaviour mb in pf.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                switch (mb.GetType().Name)
                {
                    case "BulkSack": return GetInt(mb, "_supply", 200);
                    case "LiquidDoseJarItem":
                    case "PowderShakerItem": return GetInt(mb, "_capacity", 30) * Mathf.Max(1, GetInt(mb, "_portionsPerDose", 1));
                    case "MilkCanItem": return GetInt(mb, "_capacity", 40);
                    case "HerbBunchItem": return GetInt(mb, "_doses", 12) * Mathf.Max(1, GetInt(mb, "_portionsPerDose", 1));
                    case "SupplyEggTray": return GetInt(mb, "_capacity", 30);
                }
            }
            return 1;
        }

        /// <summary>Порций в одном куске (Food Piece → Count).</summary>
        public static int PiecePortions(NetworkObject pf)
        {
            FoodPiece fp = pf != null ? pf.GetComponent<FoodPiece>() : null;
            return fp != null ? Mathf.Max(1, GetInt(fp, "_count", 1)) : 1;
        }

        /// <summary>Сервер, ДО Spawn: источник получает ровно portions порций и становится не бесконечным.</summary>
        public static void ApplyStock(GameObject go, int portions)
        {
            if (go == null || portions <= 0) return;
            foreach (MonoBehaviour mb in go.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                switch (mb.GetType().Name)
                {
                    case "BulkSack":
                        Set(mb, "_infinite", false); Set(mb, "_supply", portions); break;
                    case "LiquidDoseJarItem":
                    case "PowderShakerItem":
                        Set(mb, "_infinite", false);
                        Set(mb, "_capacity", Mathf.Max(1, Mathf.CeilToInt(portions / (float)Mathf.Max(1, GetInt(mb, "_portionsPerDose", 1))))); break;
                    case "MilkCanItem":
                        Set(mb, "_infinite", false); Set(mb, "_capacity", portions); break;
                    case "SupplyEggTray":
                        Set(mb, "_infinite", false); Set(mb, "_capacity", portions); break;
                    case "HerbBunchItem":
                        Set(mb, "_infinite", false);
                        Set(mb, "_doses", Mathf.Max(1, Mathf.CeilToInt(portions / (float)Mathf.Max(1, GetInt(mb, "_portionsPerDose", 1))))); break;
                }
            }
        }
    }
}
