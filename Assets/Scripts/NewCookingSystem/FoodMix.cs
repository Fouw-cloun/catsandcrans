using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// СМЕШИВАНИЕ ВИБРАТОРОМ ПО РЕЦЕПТУ С КОЛИЧЕСТВАМИ.
    ///
    /// Вибратор в таре: если содержимое совпадает с рецептом смеси — он не
    /// взбивает сливки в масло, а МЕШАЕТ всё вместе. Прогресс в процентах всей
    /// массы (Mix Seconds работы). На 100% всё содержимое становится одним
    /// продуктом-смесью. Дальше смесь идёт по своей цепочке (смесь для мороженого
    /// → азот → мороженое).
    ///
    /// ═══ ЕДИНИЦЫ ═══
    /// Порция — это то, чем всё меряется в таре.
    ///   1 мастерок = 2 порции сыпучего (мастерок Capacity 2)
    ///   1 лопата   = 4 порции (2 мастерка) — поставь лопате Capacity 4
    ///   1 яйцо     = 1 порция
    ///
    /// ═══ РЕЦЕПТ ПО УМОЛЧАНИЮ (идеальный) ═══
    /// «Смесь для мороженого» на ведро: Сахар РОВНО 2 (1 мастерок) + Яйцо РОВНО 1 +
    /// Сливки не меньше 4. Ничего лишнего. Нужен продукт «Смесь для мороженого»
    /// (создаётся кнопкой CatCooks → Еда → Создать «Смесь для мороженого»).
    ///
    /// Свои рецепты — добавь компонент на FoodDatabase и заполни Recipes.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CatCooks/Еда/Food Mix (смеси вибратором)")]
    public sealed class FoodMix : MonoBehaviour
    {
        /// <summary>
        /// База продуктов СЦЕНЫ, без автосоздания. FoodDatabase.Instance, не найдя базу
        /// (сцена-загрузчик), создаёт пустую «(auto)» с DontDestroyOnLoad — и потом
        /// настоящая база игровой сцены уничтожала себя как дубликат. Менеджеры живут
        /// между сценами, поэтому им — только поиск.
        /// </summary>
        private static FoodDatabase SceneDb()
        {
            FoodDatabase db = FindAnyObjectByType<FoodDatabase>();
            return db != null && db.Table != null ? db : null;
        }

        [Serializable]
        public struct Need
        {
            public ProductDef Product;
            [Tooltip("Или вот это вместо (желток вместо яйца)")] public ProductDef Alt;
            [Tooltip("Не меньше")] [Min(0)] public int Min;
            [Tooltip("Не больше (0 — без предела). Для «ровно» поставь Min = Max")] [Min(0)] public int Max;
        }

        [Serializable]
        public class Recipe
        {
            public string Title = "Смесь";
            public ProductDef Result;
            public List<Need> Needs = new List<Need>();
            [Tooltip("Чужое в таре запрещено (идеальный вариант)")] public bool OnlyThese = true;
            [Tooltip("Секунд работы вибратора на всю массу")] [Min(0.5f)] public float MixSeconds = 6f;
        }

        public List<Recipe> Recipes = new List<Recipe>();
        public bool Log = true;

        private static FoodMix _inst;
        public static FoodMix Instance
        {
            get
            {
                if (_inst != null) return _inst;
                _inst = FindAnyObjectByType<FoodMix>();
                if (_inst == null && Application.isPlaying)
                {
                    var go = new GameObject("FoodMix (auto)");
                    DontDestroyOnLoad(go);
                    _inst = go.AddComponent<FoodMix>();
                }
                return _inst;
            }
        }

        // Устарело: смеси теперь — шаги рецептов тары (CookRecipeDef + CookRecipeBook).
        // Класс оставлен, чтобы не ломать ссылки; сам больше не создаётся.

        private void Awake()
        {
            if (_inst != null && _inst != this) { Destroy(this); return; }
            _inst = this;
        }

        private sealed class Progress { public float P, Last; public int Shown; public Recipe R; }
        private readonly Dictionary<Cookware, Progress> _progress = new Dictionary<Cookware, Progress>();
        private readonly Dictionary<Cookware, string> _hinted = new Dictionary<Cookware, string>();
        private bool _resolved;

        // ───────────────────────── рецепты по умолчанию ─────────────────────────

        private void Resolve()
        {
            if (_resolved) return;
            FoodDatabase db = SceneDb();
            if (db == null) return;
            _resolved = true;
            if (Recipes == null) Recipes = new List<Recipe>();
            if (Recipes.Count == 0)
            {
                ProductDef mix = ByTitle(db, "смесь для морож");
                ProductDef sugar = ByTitle(db, "сахар"), egg = ByTitle(db, "яйцо"), yolk = ByTitle(db, "желток");
                ProductDef cream = ByTitle(db, "сливки");
                if (mix != null && sugar != null && cream != null && (egg != null || yolk != null))
                {
                    var r = new Recipe { Title = "Смесь для мороженого", Result = mix, OnlyThese = true, MixSeconds = 6f };
                    r.Needs.Add(new Need { Product = sugar, Min = 2, Max = 2 });                               // 1 мастерок
                    r.Needs.Add(new Need { Product = egg != null ? egg : yolk, Alt = egg != null ? yolk : null, Min = 1, Max = 1 });
                    r.Needs.Add(new Need { Product = cream, Min = 4, Max = 0 });                               // сколько есть
                    Recipes.Add(r);
                }
                else if (Log && mix == null)
                    Debug.Log("[Смесь] нет продукта «Смесь для мороженого» — меню CatCooks → Еда → Создать «Смесь для мороженого»", this);
            }
            if (Log) Debug.Log($"[Смесь] рецептов: {Recipes.Count}", this);
        }

        private static ProductDef ByTitle(FoodDatabase db, string part)
        {
            FoodTransitionTable t = db.Table;
            if (t == null) return null;
            for (int id = 1; id < 2048; id++)
            {
                ProductDef p = t.GetProduct((ushort)id);
                if (p == null || string.IsNullOrEmpty(p.Title)) continue;
                string s = p.Title.ToLowerInvariant();
                if (!s.Contains(part)) continue;
                if (part == "сливки" && s.Contains("масло")) continue;
                if (part == "яйцо" && (s.Contains("желток") || s.Contains("белок"))) continue;
                return p;
            }
            return null;
        }

        // ───────────────────────── сервер: вибратор мешает ─────────────────────────

        /// <summary>
        /// Вибратор работает в таре. true — это смесь по рецепту, вибратор мешает её
        /// (и не должен взбивать сливки в масло). Только сервер.
        /// </summary>
        public bool ServerMix(Cookware cw)
        {
            if (cw == null || !cw.IsServerInitialized) return false;
            Resolve();
            Recipe r = Match(cw, out string why);
            if (r == null)
            {
                if (why != null) Hint(cw, why);
                _progress.Remove(cw);
                return false;
            }
            if (!_progress.TryGetValue(cw, out Progress p) || p.R != r) { p = new Progress { R = r, Last = Time.time }; _progress[cw] = p; }
            float dt = Mathf.Clamp(Time.time - p.Last, 0f, 0.3f);
            p.Last = Time.time;
            p.P += dt / Mathf.Max(0.5f, r.MixSeconds);
            int pct = Mathf.Clamp(Mathf.FloorToInt(p.P * 4f) * 25, 0, 100);
            if (pct > p.Shown && Log) { p.Shown = pct; if (pct < 100) Debug.Log($"[Смесь] «{cw.name}»: {r.Title} — {pct}%", cw); }
            if (p.P < 1f) return true;

            // Готово: всё, что по рецепту, — в один продукт-смесь, порций столько же.
            int total = 0;
            foreach (Need n in r.Needs)
            {
                if (n.Product != null) total += cw.ServerTake(n.Product.Id, cw.CountOf(n.Product.Id));
                if (n.Alt != null) total += cw.ServerTake(n.Alt.Id, cw.CountOf(n.Alt.Id));
            }
            cw.ServerAddPinch(r.Result.Id, r.Result.StartState, Mathf.Max(1, total), 0, false);
            _progress.Remove(cw);
            if (Log) Debug.Log($"[Смесь] «{cw.name}»: {r.Title} — 100%, готово ({total} порц.)", cw);
            return true;
        }

        private Recipe Match(Cookware cw, out string why)
        {
            why = null;
            Recipe best = null;
            foreach (Recipe r in Recipes)
            {
                if (r == null || r.Result == null || r.Needs.Count == 0) continue;
                bool any = false, ok = true;
                var sb = new StringBuilder();
                foreach (Need n in r.Needs)
                {
                    int have = (n.Product != null ? cw.CountOf(n.Product.Id) : 0) + (n.Alt != null ? cw.CountOf(n.Alt.Id) : 0);
                    if (have > 0) any = true;
                    string name = n.Product != null ? n.Product.Title : "?";
                    if (have < n.Min) { ok = false; sb.Append($"{name}: есть {have}, нужно {Range(n)}; "); }
                    else if (n.Max > 0 && have > n.Max) { ok = false; sb.Append($"{name}: есть {have}, нужно {Range(n)} — перебор; "); }
                }
                if (ok && r.OnlyThese)
                    for (int i = 0; i < cw.ViewCount; i++)
                    {
                        BagView v = cw.GetView(i);
                        if (v.Count == 0 || InRecipe(r, v.Product)) continue;
                        ok = false;
                        FoodDatabase db = SceneDb();
                        ProductDef d = db != null ? db.Get(v.Product) : null;
                        sb.Append($"лишнее: {(d != null ? d.Title : "?")} ×{v.Count}; ");
                    }
                if (ok) return r;
                // подсказку даём, только если в таре уже есть что-то кроме основы (сахар или яйцо положили)
                if (any && CountRecipeKinds(cw, r) >= 2 && best == null) why = $"{r.Title}: {sb}";
            }
            return best;
        }

        private static bool InRecipe(Recipe r, ushort product)
        {
            foreach (Need n in r.Needs)
                if ((n.Product != null && n.Product.Id == product) || (n.Alt != null && n.Alt.Id == product)) return true;
            return false;
        }

        private static int CountRecipeKinds(Cookware cw, Recipe r)
        {
            int k = 0;
            foreach (Need n in r.Needs)
                if ((n.Product != null && cw.CountOf(n.Product.Id) > 0) || (n.Alt != null && cw.CountOf(n.Alt.Id) > 0)) k++;
            return k;
        }

        private static string Range(Need n)
            => n.Max > 0 && n.Max == n.Min ? $"ровно {n.Min}" : n.Max > 0 ? $"{n.Min}–{n.Max}" : $"не меньше {n.Min}";

        private void Hint(Cookware cw, string why)
        {
            if (!Log) return;
            if (_hinted.TryGetValue(cw, out string last) && last == why) return;
            _hinted[cw] = why;
            Debug.Log($"[Смесь] «{cw.name}»: не по рецепту — {why}(1 мастерок = 2 порции)", cw);
        }
    }
}
