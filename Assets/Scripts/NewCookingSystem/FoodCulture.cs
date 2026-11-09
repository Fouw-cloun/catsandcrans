using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>Что во что сквашивается закваской.</summary>
    [Serializable]
    public struct CultureRule
    {
        [Tooltip("Что сквашивается (Сливки, Молоко)")]
        public ProductDef From;
        [Tooltip("Во что превращается (Сметана)")]
        public ProductDef To;
        [Tooltip("Скорость относительно базовой: сливки 1, молоко 0.5 (дольше)")]
        [Range(0.1f, 3f)] public float Speed;
    }

    /// <summary>
    /// ЗАКВАСКА — СКВАШИВАНИЕ ПО ВРЕМЕНИ (сметана).
    ///
    /// Работает с ЛЮБОЙ тарой (Cookware) сама, на тару ничего вешать не надо.
    /// Один на сцену; если его нет — создаётся сам при старте игры с правилами
    /// по названиям продуктов: Закваска + Сливки → Сметана, Закваска + Молоко → Сметана (медленнее).
    ///
    /// ═══ ПРАВИЛА ═══
    ///   • Порядок неважен: посыпал пустое ведро и налил сливки — заквасится;
    ///     налил сливки и посыпал — тоже.
    ///   • Сухое ведро с закваской ждёт сколько угодно.
    ///   • Закваска попала туда, где нечего сквашивать (вода, суп) — через пару
    ///     секунд растворяется без толку. Вода остаётся водой.
    ///   • ДОЗА (одно нажатие ПКМ банкой) рассчитана на Capacity Per Dose порций
    ///     вместимости: ведру (30) нужна 1 доза — и сквашивается всё ведро, таре
    ///     на 60 — 2 дозы (одна сквасит половину). ПЕРЕБОР — бродит быстрее, но
    ///     сметана перекисает и портится (крупка, сыворотка). Закваска в уже
    ///     готовую сметану — тоже перебор.
    ///   • Сначала «бродит» (Brew Seconds — буклит, пузыри), потом партия
    ///     густеет (Set Seconds) — порции по очереди становятся сметаной.
    ///     Закваска уходит, когда партия готова.
    ///
    /// Сеть: считает только сервер (меняет содержимое Cookware, оно и так
    /// синхронизировано). Вид «бродит» каждый игрок вычисляет сам по
    /// содержимому — IsFermenting().
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("CatCooks/Еда/Food Culture (закваска)")]
    public sealed class FoodCulture : MonoBehaviour
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

        [Header("Закваска")]
        [Tooltip("Продукт-закваска. Пусто — найдётся по названию «Закваска»")]
        public ProductDef Starter;
        [Tooltip("Что во что сквашивается. Пусто — Сливки → Сметана по названиям")]
        public List<CultureRule> Rules = new List<CultureRule>();

        [Header("Допуск (недо- и перевзбитые сливки)")]
        [Tooltip("Эти продукты в небольшой доле тоже уходят в сметану: недовзбили — осталось " +
                 "немного молока, перевзбили — пошло немного масла. Пусто — Молоко и Сливочное масло")]
        public List<ProductDef> Tolerated = new List<ProductDef>();
        [Tooltip("Доля каждого из них от всей молочки, при которой ещё получается сметана (0.15 = 15%)")]
        [Range(0f, 0.5f)] public float Tolerance = 0.15f;

        [Header("Дозировка")]
        [Tooltip("Одна доза закваски — на столько порций вместимости тары. Ведро (30) — 1 доза, " +
                 "тара на 60 — 2 дозы, маленькая банка — тоже 1.")]
        [Min(1)] public int CapacityPerDose = 30;
        [Tooltip("Доз закваски на 1 ведро тары (банка на 8 вёдер — 8 доз). Рецепт «Сметана» говорит то же самое")]
        [Min(0.1f)] public float DosesPerBucket = 1f;
        [Tooltip("Перебор: каждая лишняя доза ускоряет брожение на столько (0.5 = +50%)")]
        [Range(0f, 2f)] public float OverdoseSpeedup = 0.5f;
        [Tooltip("Перебор закваски — сметана перекисает и портится (крупка, сыворотка)")]
        public bool OverdoseSpoils = true;
        [Header("Время")]
        [Tooltip("Брожение до загустения, с (буклит, пузыри)")]
        [Min(0f)] public float BrewSeconds = 15f;
        [Tooltip("За сколько секунд густеет вся партия")]
        [Min(1f)] public float SetSeconds = 30f;
        [Tooltip("Закваска там, где нечего сквашивать (вода), растворяется через, с")]
        [Min(0.1f)] public float DissolveSeconds = 3f;
        [Tooltip("Как часто сервер проверяет тары, с")]
        [Range(0.05f, 1f)] public float TickSeconds = 0.2f;

        [Header("Отладка")]
        public bool Log = true;

        private static FoodCulture _inst;

        public static FoodCulture Instance
        {
            get
            {
                if (_inst != null) return _inst;
                _inst = FindAnyObjectByType<FoodCulture>();
                if (_inst == null && Application.isPlaying)
                {
                    // Живёт между сценами: игра стартует со сцены-загрузчика и потом
                    // грузит 02_Game — объект без DontDestroyOnLoad пропадал вместе
                    // с первой сценой, и закваска молча переставала работать.
                    var go = new GameObject("FoodCulture (auto)");
                    DontDestroyOnLoad(go);
                    _inst = go.AddComponent<FoodCulture>();
                }
                return _inst;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() { _ = Instance; }

        private enum Phase : byte { Wait = 0, Brew = 1, Set = 2 }

        private sealed class State
        {
            public Phase Phase;
            public float T, Dissolve;
            public int Batch, DoneInBatch, Converted;
            public bool WarnedOverdose, WarnedTolerance;
        }

        private readonly Dictionary<Cookware, State> _states = new Dictionary<Cookware, State>();
        private readonly List<Cookware> _dead = new List<Cookware>();
        private float _acc;
        private bool _resolved;

        private void Awake()
        {
            if (_inst != null && _inst != this) { Destroy(this); return; }
            _inst = this;
        }

        // ───────────────────────── правила ─────────────────────────

        /// <summary>Заполнить пустые поля по названиям продуктов из FoodDatabase.</summary>
        private void Resolve()
        {
            if (_resolved) return;
            FoodDatabase db = SceneDb();
            if (db == null) return;
            _resolved = true;
            if (Starter == null) Starter = ByTitle(db, "закваск");
            if (Rules == null) Rules = new List<CultureRule>();
            if (Rules.Count == 0)
            {
                ProductDef sm = ByTitle(db, "сметан");
                ProductDef cream = ByTitle(db, "сливки");
                if (sm != null && cream != null) Rules.Add(new CultureRule { From = cream, To = sm, Speed = 1f });
            }
            if (Tolerated == null) Tolerated = new List<ProductDef>();
            if (Tolerated.Count == 0)
            {
                ProductDef milk = ByTitle(db, "молок");
                ProductDef butter = ByTitleExact(db, "сливочное масло");
                if (milk != null) Tolerated.Add(milk);
                if (butter != null) Tolerated.Add(butter);
            }
            if (Log)
                Debug.Log($"[Закваска] закваска: {(Starter != null ? Starter.Title : "НЕ НАЙДЕНА")}, правил: {Rules.Count}" +
                          (Rules.Count > 0 ? " (" + string.Join(", ", Rules.ConvertAll(r => $"{(r.From ? r.From.Title : "?")}→{(r.To ? r.To.Title : "?")}")) + ")" : ""), this);
            if (Starter == null || Rules.Count == 0) _resolved = false;    // база ещё не та — попробуем позже
        }

        private static ProductDef ByTitleExact(FoodDatabase db, string part)
        {
            FoodTransitionTable t = db.Table;
            if (t == null) return null;
            for (int id = 1; id < 2048; id++)
            {
                ProductDef p = t.GetProduct((ushort)id);
                if (p != null && !string.IsNullOrEmpty(p.Title) && p.Title.ToLowerInvariant().Contains(part)) return p;
            }
            return null;
        }

        private bool IsTolerated(ushort product)
        {
            if (Tolerated == null) return false;
            foreach (ProductDef p in Tolerated) if (p != null && p.Id == product) return true;
            return false;
        }

        private static ProductDef ByTitle(FoodDatabase db, string part)
        {
            FoodTransitionTable t = db.Table;
            if (t == null) return null;
            for (int id = 1; id < 2048; id++)
            {
                ProductDef p = t.GetProduct((ushort)id);
                if (p != null && !string.IsNullOrEmpty(p.Title)
                    && p.Title.ToLowerInvariant().Contains(part)
                    && !p.Title.ToLowerInvariant().Contains("масло"))   // «сливочное масло» — не сливки
                    return p;
            }
            return null;
        }

        /// <summary>Сколько доз нужно этой таре: по её вместимости.</summary>
        public int DosesFor(Cookware cw) => Mathf.Max(1, Mathf.RoundToInt(FoodUnits.BucketsIn(cw) * Mathf.Max(0.1f, DosesPerBucket)));

        /// <summary>Продукт сквашивается этой закваской?</summary>
        public bool IsCulturable(ushort product, out CultureRule rule)
        {
            Resolve();
            if (Rules != null)
                foreach (CultureRule r in Rules)
                    if (r.From != null && r.To != null && r.From.Id == product) { rule = r; return true; }
            rule = default;
            return false;
        }

        /// <summary>
        /// Бродит ли в таре закваска (для вида: пузыри, «дыхание» поверхности).
        /// Считается по содержимому, которое есть у всех игроков, — без сети.
        /// </summary>
        public static bool IsFermenting(Cookware cw)
        {
            FoodCulture fc = _inst != null ? _inst : FindAnyObjectByType<FoodCulture>();
            if (fc == null || cw == null) return false;
            fc.Resolve();
            if (fc.Starter == null) return false;
            bool starter = false, food = false;
            for (int i = 0; i < cw.ViewCount; i++)
            {
                BagView v = cw.GetView(i);
                if (v.Count == 0) continue;
                if (v.Product == fc.Starter.Id) starter = true;
                else if (fc.IsCulturable(v.Product, out _)) food = true;
            }
            return starter && food;
        }

        // ───────────────────────── сервер ─────────────────────────

        private void Update()
        {
            _acc += Time.deltaTime;
            if (_acc < TickSeconds) return;
            float dt = _acc;
            _acc = 0f;
            Resolve();
            if (Starter == null) return;

            foreach (Cookware cw in Cookware.All)
            {
                if (cw == null || !cw.IsServerInitialized) continue;
                TickCookware(cw, dt);
            }
            // забыть уничтоженные тары
            _dead.Clear();
            foreach (var kv in _states) if (kv.Key == null) _dead.Add(kv.Key);
            foreach (Cookware c in _dead) _states.Remove(c);
        }

        private void TickCookware(Cookware cw, float dt)
        {
            int starter = cw.CountOf(Starter.Id);
            if (starter <= 0) { _states.Remove(cw); return; }
            bool fresh = !_states.TryGetValue(cw, out State st);
            if (fresh) { st = new State(); _states[cw] = st; }

            // что есть в таре
            int culturable = 0, other = 0, tolerated = 0, worstN = 0;
            ushort worstP = 0;
            float speed = 0f;
            for (int i = 0; i < cw.ViewCount; i++)
            {
                BagView v = cw.GetView(i);
                if (v.Count == 0 || v.Product == Starter.Id) continue;
                if (IsCulturable(v.Product, out CultureRule r)) { culturable += v.Count; speed = Mathf.Max(speed, r.Speed); }
                else if (IsTolerated(v.Product))
                {
                    tolerated += v.Count;
                    int pc = cw.CountOf(v.Product);
                    if (pc > worstN) { worstN = pc; worstP = v.Product; }
                }
                else if (!IsResult(v.Product)) other += v.Count;
            }
            // Допуск: немного молока (недовзбили) или масла (перевзбили) уходит в сметану вместе со сливками.
            // Доля считается от всей молочки, включая уже готовую сметану, — она не «растёт» по ходу партии.
            int results = CountResults(cw, false);
            int dairyAll = culturable + tolerated + results;
            bool inTol = dairyAll > 0 && worstN <= Mathf.FloorToInt(dairyAll * Tolerance + 1e-4f);
            if (tolerated > 0)
            {
                if ((culturable > 0 || results > 0) && inTol) culturable += tolerated;   // всё ведро — одна масса
                else if (culturable > 0)
                {
                    if (!st.WarnedTolerance && Log)
                    {
                        st.WarnedTolerance = true;
                        FoodDatabase db = SceneDb();
                        ProductDef wp = db != null ? db.Get(worstP) : null;
                        Debug.Log($"[Закваска] «{cw.name}»: {(wp ? wp.Title : "лишнего")} {worstN * 100 / Mathf.Max(1, dairyAll)}% " +
                                  $"(допуск {Tolerance * 100f:0}%) — сначала доведи сливки вибратором. Закваска ждёт.", cw);
                    }
                    st.Phase = Phase.Wait;
                    return;
                }
                else other = 0;          // одно молоко/масло — закваска ждёт сливки
            }
            if (culturable > 0 && speed <= 0f) speed = 1f;

            int need = DosesFor(cw);
            if (fresh && Log)
                Debug.Log($"[Закваска] «{cw.name}»: доз {starter} (норма {need}), сквашивается {culturable}, прочего {other}" +
                          (culturable == 0 && other > 0 ? " — нечего сквашивать, растворится" : culturable == 0 ? " — ждёт молоко/сливки" : ""), cw);
            if (culturable == 0)
            {
                // Партия закончилась (всё уже сметана) — закваска своё отработала.
                if (st.Phase == Phase.Set) { Finish(cw, st, "партия готова"); return; }
                st.Phase = Phase.Wait;
                if (results > 0 && OverdoseSpoils)
                {
                    // Сыпанул ещё закваски в готовую сметану — перекисает.
                    st.Dissolve += dt;
                    if (st.Dissolve >= DissolveSeconds)
                    {
                        cw.ServerTake(Starter.Id, starter);
                        int n = Spoil(cw);
                        if (Log) Debug.Log($"[Закваска] «{cw.name}»: закваска в готовую сметану — перекисла ({n} порц. испорчено)", cw);
                        _states.Remove(cw);
                    }
                    return;
                }
                if (other > 0)
                {
                    // Вода и прочее: закваске тут нечего делать — растворяется.
                    st.Dissolve += dt;
                    if (st.Dissolve >= DissolveSeconds)
                    {
                        cw.ServerTake(Starter.Id, starter);
                        if (Log) Debug.Log($"[Закваска] «{cw.name}»: нечего сквашивать — закваска растворилась", cw);
                        _states.Remove(cw);
                    }
                }
                else st.Dissolve = 0f;   // сухая тара: ждём, пока нальют
                return;
            }
            st.Dissolve = 0f;

            // Норма доз сквашивает всю тару; меньше — часть. Больше нормы — быстрее, но испортится.
            int full = Mathf.Max(1, cw.Capacity);
            int budget = Mathf.CeilToInt(full * Mathf.Min(starter, need) / (float)need) - st.Converted;
            if (starter > need) speed *= 1f + OverdoseSpeedup * (starter - need);
            if (starter > need && !st.WarnedOverdose && Log)
            {
                st.WarnedOverdose = true;
                Debug.Log($"[Закваска] «{cw.name}»: ПЕРЕБОР — {starter} доз при норме {need}: бродит быстрее, сметана перекиснет", cw);
            }
            if (budget <= 0) { Finish(cw, st, "закваска кончилась"); return; }

            switch (st.Phase)
            {
                case Phase.Wait:
                    st.Phase = Phase.Brew;
                    st.T = 0f;
                    if (Log) Debug.Log($"[Закваска] «{cw.name}»: бродит ({culturable} порц., закваски {starter})", cw);
                    break;

                case Phase.Brew:
                    st.T += dt * speed;
                    Report(cw, st, 0f);
                    if (st.T >= BrewSeconds) StartBatch(st, culturable, budget);
                    break;

                case Phase.Set:
                    st.T += dt * speed;
                    Report(cw, st, 1f);
                    int target = Mathf.CeilToInt(st.Batch * Mathf.Clamp01(st.T / SetSeconds));
                    while (st.DoneInBatch < target && budget > 0)
                    {
                        if (!ConvertOne(cw)) break;
                        st.DoneInBatch++;
                        st.Converted++;
                        budget--;
                    }
                    if (st.T >= SetSeconds)
                    {
                        // Налили ещё сливок, а закваска осталась — следующая партия.
                        int left = CountCulturable(cw);
                        if (left > 0 && budget > 0) StartBatch(st, left, budget);
                        else Finish(cw, st, left > 0 ? "закваски не хватило на всё" : "партия готова");
                    }
                    break;
            }
        }

        /// <summary>Брожение и загустение — в журнал тары (аналитик: рецепт, шаг, проценты).</summary>
        private void Report(Cookware cw, State st, float setPhase)
        {
            if (Rules.Count == 0 || Rules[0].From == null || Rules[0].To == null) return;
            float total = Mathf.Max(0.1f, BrewSeconds + SetSeconds);
            float done = setPhase < 0.5f ? Mathf.Clamp01(st.T / Mathf.Max(0.1f, BrewSeconds)) * BrewSeconds
                                         : BrewSeconds + Mathf.Clamp01(st.T / Mathf.Max(0.1f, SetSeconds)) * SetSeconds;
            CookRecipeBook.ReportNative(cw, CookDriver.Culture, Rules[0].From, Rules[0].To, done / total);
        }

        private void StartBatch(State st, int culturable, int budget)
        {
            st.Phase = Phase.Set;
            st.T = 0f;
            st.Batch = Mathf.Min(culturable, budget);
            st.DoneInBatch = 0;
        }

        private void Finish(Cookware cw, State st, string why)
        {
            int s = cw.CountOf(Starter.Id);
            if (s > 0) cw.ServerTake(Starter.Id, s);
            bool over = OverdoseSpoils && s > DosesFor(cw);
            int spoiled = over ? Spoil(cw) : 0;
            if (Rules.Count > 0)
                CookRecipeBook.FinishNative(cw, CookDriver.Culture, Rules[0].From, Rules[0].To,
                    over ? $"{Rules[0].To.Title} ×{st.Converted}, перекисла (перебор закваски)"
                         : $"{Rules[0].To.Title} ×{st.Converted}, оценка {(s > DosesFor(cw) ? 60 : 100)}/100");
            if (Log) Debug.Log($"[Закваска] «{cw.name}»: {why}, сквашено {st.Converted} порц." +
                               (over ? $" ПЕРЕБОР закваски ({s} при норме {DosesFor(cw)}) — сметана перекисла, испорчено {spoiled}" : ""), cw);
            _states.Remove(cw);
        }

        /// <summary>Готовый результат (сметана) → «Испортилось»: крупка, сыворотка.</summary>
        private int Spoil(Cookware cw)
        {
            int n = 0;
            var snap = new List<BagView>();
            for (int i = 0; i < cw.ViewCount; i++) snap.Add(cw.GetView(i));
            foreach (BagView v in snap)
            {
                if (v.Count == 0 || v.State == FoodState.Spoiled || !IsResult(v.Product)) continue;
                int took = cw.ServerTake(v.Product, v.Count);
                if (took > 0) { cw.ServerAddPinch(v.Product, FoodState.Spoiled, took, 0, false); n += took; }
            }
            return n;
        }

        private int CountResults(Cookware cw, bool includeSpoiled)
        {
            int n = 0;
            for (int i = 0; i < cw.ViewCount; i++)
            {
                BagView v = cw.GetView(i);
                if (IsResult(v.Product) && (includeSpoiled || v.State != FoodState.Spoiled)) n += v.Count;
            }
            return n;
        }

        /// <summary>Одна порция: снять сквашиваемое (по порядку правил) и положить результат.</summary>
        private bool ConvertOne(Cookware cw)
        {
            foreach (CultureRule r in Rules)
            {
                if (r.From == null || r.To == null) continue;
                if (cw.CountOf(r.From.Id) <= 0) continue;
                if (cw.ServerTake(r.From.Id, 1) <= 0) continue;
                cw.ServerAddPinch(r.To.Id, r.To.StartState, 1, 0, false);   // замена 1:1 — влезает и в полную тару
                return true;
            }
            // Сливки кончились — то немногое молоко/масло, что в допуске, тоже в сметану.
            ProductDef to = Rules.Count > 0 ? Rules[0].To : null;
            if (to != null && Tolerated != null)
                foreach (ProductDef p in Tolerated)
                {
                    if (p == null || cw.CountOf(p.Id) <= 0) continue;
                    if (cw.ServerTake(p.Id, 1) <= 0) continue;
                    cw.ServerAddPinch(to.Id, to.StartState, 1, 0, false);
                    return true;
                }
            return false;
        }

        private int CountCulturable(Cookware cw)
        {
            int n = 0;
            foreach (CultureRule r in Rules) if (r.From != null) n += cw.CountOf(r.From.Id);
            if (n > 0 && Tolerated != null)
                foreach (ProductDef p in Tolerated) if (p != null) n += cw.CountOf(p.Id);
            return n;
        }

        private bool IsResult(ushort product)
        {
            foreach (CultureRule r in Rules) if (r.To != null && r.To.Id == product) return true;
            return false;
        }
    }
}
