using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЗАМОРОЗКА АЗОТОМ.
    ///
    /// Азотный пистолет поливает тару паром → тара копит «холод». Набрала
    /// Freeze Seconds — содержимое замерзает:
    ///   • это мороженое по рецепту (сливки + сахар + яйцо, молоко по желанию) —
    ///     всё это становится МОРОЖЕНЫМ (держится, не тает);
    ///   • что-то случайное — просто ЗАМЕРЗАЕТ (лёд, иней, не льётся) и через
    ///     Thaw Seconds оттаивает обратно в то, чем было.
    ///
    /// Работает с любой тарой сам; один на сцену, создаётся сам. Продукты
    /// находит по названиям: Мороженое, Сливки, Сахар, Яйцо / Яичный желток, Молоко.
    /// Сеть: считает только сервер (меняет содержимое Cookware).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CatCooks/Еда/Food Freeze (заморозка азотом)")]
    public sealed class FoodFreeze : MonoBehaviour
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
            [Tooltip("Что нужно")] public ProductDef Product;
            [Tooltip("Или вот это вместо (например, желток вместо яйца)")] public ProductDef Alt;
            [Min(1)] public int Count;
        }

        [Header("Мороженое")]
        [Tooltip("Результат. Пусто — «Мороженое» по названию")]
        public ProductDef IceCream;
        [Tooltip("Обязательное. Пусто — Сливки ×2, Сахар ×1, Яйцо (или желток) ×1")]
        public List<Need> Needs = new List<Need>();
        [Tooltip("Входит в мороженое, но не обязательно. Пусто — Молоко, Ваниль")]
        public List<ProductDef> Extras = new List<ProductDef>();

        [Header("Время")]
        [Tooltip("Сколько секунд поливать азотом, чтобы замёрзло")]
        [Min(0.2f)] public float FreezeSeconds = 2.5f;
        [Tooltip("Холод уходит, если перестал поливать, ед./с")]
        [Min(0f)] public float ChillDecay = 0.8f;
        [Tooltip("Случайная смесь оттаивает через, с")]
        [Min(1f)] public float ThawSeconds = 40f;

        [Header("Отладка")]
        public bool Log = true;

        private static FoodFreeze _inst;

        public static FoodFreeze Instance
        {
            get
            {
                if (_inst != null) return _inst;
                _inst = FindAnyObjectByType<FoodFreeze>();
                if (_inst == null && Application.isPlaying)
                {
                    var go = new GameObject("FoodFreeze (auto)");
                    DontDestroyOnLoad(go);                       // переживает смену сцены (загрузчик → 02_Game)
                    _inst = go.AddComponent<FoodFreeze>();
                }
                return _inst;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() { _ = Instance; }

        private sealed class Frozen
        {
            public float ThawAt;
            public readonly Dictionary<ushort, FoodState> Was = new Dictionary<ushort, FoodState>();
        }

        private readonly Dictionary<Cookware, float> _chill = new Dictionary<Cookware, float>();
        private readonly Dictionary<Cookware, float> _lastHit = new Dictionary<Cookware, float>();
        private readonly Dictionary<Cookware, Frozen> _frozen = new Dictionary<Cookware, Frozen>();
        private readonly List<Cookware> _tmp = new List<Cookware>();
        private bool _resolved, _mixMode;
        private readonly Dictionary<Cookware, int> _chillShown = new Dictionary<Cookware, int>();

        private void Awake()
        {
            if (_inst != null && _inst != this) { Destroy(this); return; }
            _inst = this;
        }

        /// <summary>Холод 0..1 для тары (только сервер) — для подсказок.</summary>
        public float Chill01(Cookware cw) => _chill.TryGetValue(cw, out float c) ? Mathf.Clamp01(c / FreezeSeconds) : 0f;

        /// <summary>Поливают азотом. Сервер, каждый кадр, пока тара в струе.</summary>
        public void ServerChill(Cookware cw, float seconds)
        {
            if (cw == null || !cw.IsServerInitialized) return;
            // Шаг рецепта «Заморозить» (смесь → мороженое) — ведёт книга рецептов.
            if (CookRecipeBook.ServerProcess(cw, CookStepKind.Freeze, seconds)) return;
            Resolve();
            _chill.TryGetValue(cw, out float c);
            c += seconds;
            _lastHit[cw] = Time.time;
            int pct = Mathf.Clamp(Mathf.FloorToInt(c / FreezeSeconds * 4f) * 25, 0, 100);
            _chillShown.TryGetValue(cw, out int shown);
            if (Log && pct > shown && pct < 100) Debug.Log($"[Азот] «{cw.name}»: холод {pct}%", cw);
            _chillShown[cw] = c >= FreezeSeconds ? 0 : Mathf.Max(shown, pct);
            if (c >= FreezeSeconds)
            {
                c = 0f;
                Freeze(cw);
            }
            _chill[cw] = c;
        }

        // ───────────────────────── правила ─────────────────────────

        private void Resolve()
        {
            if (_resolved) return;
            FoodDatabase db = SceneDb();
            if (db == null) return;
            _resolved = true;
            if (IceCream == null) IceCream = ByTitle(db, "морожен");
            if (Needs == null) Needs = new List<Need>();
            // Новая цепочка: сливки + сахар + яйцо → вибратор → «Смесь для мороженого» → азот → мороженое.
            ProductDef mix = ByTitle(db, "смесь для морож");
            if (Needs.Count == 0 && mix != null)
            {
                Needs.Add(new Need { Product = mix, Count = 1 });
                if (Extras == null) Extras = new List<ProductDef>();
                _mixMode = true;
            }
            if (Needs.Count == 0)
            {
                ProductDef cream = ByTitle(db, "сливки"), sugar = ByTitle(db, "сахар");
                ProductDef egg = ByTitle(db, "яйцо"), yolk = ByTitle(db, "желток");
                if (cream != null) Needs.Add(new Need { Product = cream, Count = 2 });
                if (sugar != null) Needs.Add(new Need { Product = sugar, Count = 1 });
                if (egg != null || yolk != null) Needs.Add(new Need { Product = egg != null ? egg : yolk, Alt = egg != null ? yolk : null, Count = 1 });
            }
            if (Extras == null) Extras = new List<ProductDef>();
            if (Extras.Count == 0 && !_mixMode)
            {
                ProductDef milk = ByTitle(db, "молок"), van = ByTitle(db, "ванил");
                if (milk != null) Extras.Add(milk);
                if (van != null) Extras.Add(van);
            }
            if (Log)
                Debug.Log($"[Азот] мороженое: {(IceCream != null ? IceCream.Title : "НЕ НАЙДЕНО")}, обязательных: {Needs.Count}, доп.: {Extras.Count}", this);
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

        private string WhyNot(Cookware cw)
        {
            if (IceCream == null) return "нет продукта «Мороженое»";
            var sb = new System.Text.StringBuilder();
            foreach (Need n in Needs)
            {
                int have = (n.Product != null ? cw.CountOf(n.Product.Id) : 0) + (n.Alt != null ? cw.CountOf(n.Alt.Id) : 0);
                if (have < Mathf.Max(1, n.Count)) sb.Append($"нет «{(n.Product != null ? n.Product.Title : "?")}» (нужно {n.Count}); ");
            }
            if (_mixMode) sb.Append("сначала: сливки + сахар 2 (1 мастерок) + яйцо 1 → перемешай вибратором");
            return sb.ToString();
        }

        private bool IsIceCreamRecipe(Cookware cw)
        {
            if (IceCream == null || Needs.Count == 0) return false;
            foreach (Need n in Needs)
            {
                int have = (n.Product != null ? cw.CountOf(n.Product.Id) : 0) + (n.Alt != null ? cw.CountOf(n.Alt.Id) : 0);
                if (have < Mathf.Max(1, n.Count)) return false;
            }
            return true;
        }

        // ───────────────────────── заморозка ─────────────────────────

        private void Freeze(Cookware cw)
        {
            if (IsIceCreamRecipe(cw))
            {
                // Всё, что идёт в мороженое, — в мороженое. Порций столько же.
                int total = 0;
                foreach (Need n in Needs)
                {
                    if (n.Product != null) total += cw.ServerTake(n.Product.Id, cw.CountOf(n.Product.Id));
                    if (n.Alt != null) total += cw.ServerTake(n.Alt.Id, cw.CountOf(n.Alt.Id));
                }
                foreach (ProductDef e in Extras)
                    if (e != null) total += cw.ServerTake(e.Id, cw.CountOf(e.Id));
                cw.ServerAddPinch(IceCream.Id, IceCream.StartState, Mathf.Max(1, total), 0);
                _frozen.Remove(cw);
                if (Log) Debug.Log($"[Азот] «{cw.name}»: МОРОЖЕНОЕ, {total} порц.", cw);
                return;
            }

            if (Log) Debug.Log($"[Азот] «{cw.name}»: не мороженое — {WhyNot(cw)}", cw);
            // Случайная смесь: всё, кроме мороженого, замерзает, потом оттает.
            if (!_frozen.TryGetValue(cw, out Frozen fr)) { fr = new Frozen(); _frozen[cw] = fr; }
            fr.ThawAt = Time.time + ThawSeconds;

            var snapshot = new List<BagView>();
            for (int i = 0; i < cw.ViewCount; i++) snapshot.Add(cw.GetView(i));
            int n0 = 0;
            foreach (BagView v in snapshot)
            {
                if (v.Count == 0 || v.State == FoodState.Frozen) continue;
                if (IceCream != null && v.Product == IceCream.Id) continue;
                if (!fr.Was.ContainsKey(v.Product)) fr.Was[v.Product] = v.State;
                int took = cw.ServerTake(v.Product, v.Count);
                if (took > 0) { cw.ServerAddPinch(v.Product, FoodState.Frozen, took, 0); n0 += took; }
            }
            if (Log) Debug.Log(n0 > 0 ? $"[Азот] «{cw.name}»: не мороженое — замёрзло ({n0} порц.), оттает через {ThawSeconds:0} с"
                                      : $"[Азот] «{cw.name}»: уже замёрзло — продлено", cw);
        }

        private void Thaw(Cookware cw, Frozen fr)
        {
            var snapshot = new List<BagView>();
            for (int i = 0; i < cw.ViewCount; i++) snapshot.Add(cw.GetView(i));
            int n0 = 0;
            foreach (BagView v in snapshot)
            {
                if (v.Count == 0 || v.State != FoodState.Frozen) continue;
                FoodState back;
                if (!fr.Was.TryGetValue(v.Product, out back))
                {
                    FoodDatabase db = SceneDb();
                    ProductDef d = db != null ? db.Get(v.Product) : null;
                    back = d != null ? d.StartState : FoodState.Raw;
                }
                int took = cw.ServerTake(v.Product, v.Count);
                if (took > 0) { cw.ServerAddPinch(v.Product, back, took, 0); n0 += took; }
            }
            if (Log) Debug.Log($"[Азот] «{cw.name}»: оттаяло ({n0} порц.)", cw);
        }

        private void Update()
        {
            if (_chill.Count == 0 && _frozen.Count == 0) return;
            float now = Time.time, dt = Time.deltaTime;

            // холод уходит, если не поливают
            _tmp.Clear();
            foreach (var kv in _chill) _tmp.Add(kv.Key);
            foreach (Cookware cw in _tmp)
            {
                if (cw == null) { _chill.Remove(cw); _lastHit.Remove(cw); continue; }
                if (_lastHit.TryGetValue(cw, out float t) && now - t < 0.15f) continue;
                float c = _chill[cw] - dt * ChillDecay;
                if (c <= 0f) { _chill.Remove(cw); _lastHit.Remove(cw); }
                else _chill[cw] = c;
            }

            // оттаивание
            _tmp.Clear();
            foreach (var kv in _frozen) _tmp.Add(kv.Key);
            foreach (Cookware cw in _tmp)
            {
                if (cw == null) { _frozen.Remove(cw); continue; }
                Frozen fr = _frozen[cw];
                if (now < fr.ThawAt) continue;
                if (cw.IsServerInitialized) Thaw(cw, fr);
                _frozen.Remove(cw);
            }
        }
    }
}
