using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// v18: ТАБЛИЦА ВЕЩЕСТВ — какой профиль <see cref="FoodSubstance"/> у какого продукта.
    /// Один ассет на игру: Resources/FoodSubstances (грузится сам).
    ///
    /// Правила по порядку, первое совпадение побеждает:
    ///   1. Продукт (+ состояние)   — точное правило;
    ///   2. Слова в названии (+ состояние) — «тёртое какао», «сливочное масло»;
    ///   3. Только состояние          — «Фарш», «Пюре»…
    /// Нет совпадения — работают старые правила (FoodHeapLook, роли на таре).
    ///
    /// Создать: меню CatCooks → Вещества → 1. Создать профили и таблицу.
    /// </summary>
    [CreateAssetMenu(menuName = "CatCooks/Таблица веществ", fileName = "FoodSubstances")]
    public sealed class FoodSubstanceTable : ScriptableObject
    {
        public const string ResourceName = "FoodSubstances";

        [Serializable]
        public struct ByProduct
        {
            public ProductDef Product;
            [Tooltip("— нет — = любое состояние")] public FoodState State;
            public FoodSubstance Substance;
        }

        [Serializable]
        public struct ByName
        {
            [Tooltip("Правило включено")] public bool On;
            [Tooltip("Слова через запятую (без учёта регистра): «тёртое какао, какао-порошок»")]
            public string Words;
            [Tooltip("Не подходит, если в названии есть эти слова (через запятую)")]
            public string Except;
            [Tooltip("— нет — = любое состояние")] public FoodState State;
            [Tooltip("Не брать растопленное (Растоплено, Растворилось) — там своя жидкость")]
            public bool SkipLiquid;
            public FoodSubstance Substance;
        }

        [Serializable]
        public struct ByState
        {
            public FoodState State;
            public FoodSubstance Substance;
        }

        [Tooltip("Точные правила: продукт (+ состояние)")]
        public List<ByProduct> Products = new List<ByProduct>();
        [Tooltip("Правила по словам в названии продукта")]
        public List<ByName> Names = new List<ByName>();
        [Tooltip("Правила по состоянию (для всех продуктов)")]
        public List<ByState> States = new List<ByState>();

        [Header("Общее")]
        [Tooltip("Общий префаб кучки на полу (FoodHeap) — для тары, у которой не задан свой Spill Heap Prefab. " +
                 "Должен быть в Spawnable Prefabs FishNet")]
        public FoodHeap FloorHeapPrefab;
        [Tooltip("Сам повесить ScoopSubstanceView на лопаты и мастерки при запуске (ищет при загрузке сцены и ещё 2 раза " +
                 "в первые секунды, потом не ищет). Выкл — вешай компонент руками на каждую лопату")]
        public bool AutoAttachToScoops = true;
        [Tooltip("Писать в консоль, какой профиль выбран для продукта (по разу)")]
        public bool Log = true;

        // ───────────────────────── ПОИСК ─────────────────────────

        private static FoodSubstanceTable _inst;
        private static bool _loaded;
        private static readonly Dictionary<int, FoodSubstance> _cache = new Dictionary<int, FoodSubstance>();

        /// <summary>Таблица из Resources (или null, если её ещё не создали).</summary>
        public static FoodSubstanceTable Instance
        {
            get
            {
                if (!_loaded)
                {
                    _loaded = true;
                    _inst = Resources.Load<FoodSubstanceTable>(ResourceName);
                    if (_inst != null) Debug.Log($"[Вещества] таблица загружена: {_inst.Products.Count} по продукту, {_inst.Names.Count} по словам, {_inst.States.Count} по состоянию");
                }
                return _inst;
            }
        }

        private void OnValidate() { _cache.Clear(); }
        private void OnEnable() { _cache.Clear(); }

        /// <summary>Сбросить кэш (после правки таблицы в редакторе).</summary>
        public static void Reload() { _loaded = false; _inst = null; _cache.Clear(); }

        public static FoodSubstance Resolve(ProductDef p, FoodState s)
        {
            if (p == null) return null;
            FoodSubstanceTable t = Instance;
            if (t == null) return null;
            int key = ((int)p.Id << 8) | (int)s;
            if (_cache.TryGetValue(key, out FoodSubstance hit)) return hit;
            FoodSubstance found = t.Find(p, s);
            _cache[key] = found;
            if (t.Log && found != null) Debug.Log($"[Вещества] «{p.Title}» ({FoodStateInfo.NameOf(s)}) → профиль «{found.Title}»");
            return found;
        }

        private FoodSubstance Find(ProductDef p, FoodState s)
        {
            foreach (ByProduct r in Products)
                if (r.Substance != null && r.Product == p && (r.State == FoodState.None || r.State == s)) return r.Substance;

            string title = string.IsNullOrEmpty(p.Title) ? "" : p.Title.ToLowerInvariant();
            // v18.10: «жидкое» = только РАСТОПЛЕНО / растворилось. Состояния «В таре» и «Налито» — это просто
            // «лежит в ведре»: сливочное масло в ведре именно так и хранится. Раньше они тоже пропускались —
            // поэтому у масла в ведре и на мастерке профиль не находился и всё выглядело по-старому.
            bool liquid = s == FoodState.Melted || s == FoodState.Dissolved;
            foreach (ByName r in Names)
            {
                if (!r.On || r.Substance == null || string.IsNullOrEmpty(r.Words)) continue;
                if (r.State != FoodState.None && r.State != s) continue;
                if (r.SkipLiquid && liquid) continue;
                if (!AnyWord(title, r.Words)) continue;
                if (!string.IsNullOrEmpty(r.Except) && AnyWord(title, r.Except)) continue;
                return r.Substance;
            }

            foreach (ByState r in States)
                if (r.Substance != null && r.State == s) return r.Substance;
            return null;
        }

        private static bool AnyWord(string title, string words)
        {
            string[] w = words.ToLowerInvariant().Split(',');
            for (int i = 0; i < w.Length; i++)
            {
                string x = w[i].Trim();
                if (x.Length > 0 && title.Contains(x)) return true;
            }
            return false;
        }
    }
}
