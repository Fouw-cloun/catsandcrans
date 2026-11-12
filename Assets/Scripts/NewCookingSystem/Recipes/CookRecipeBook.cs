using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// КНИГА РЕЦЕПТОВ ТАРЫ — сверяет журнал тары с рецептами и ведёт обработки.
    ///
    /// Инструменты сообщают «я делаю X с этой тарой»:
    ///   вибратор → Перемешать, азот → Заморозить (дальше — плита, закваска...).
    /// Книга отвечает: «это шаг рецепта» (true — инструмент свою обычную работу не
    /// делает: не взбивает сливки в масло, не морозит просто так) или «нет» (false —
    /// инструмент работает как обычно).
    ///
    /// Шаг рецепта засчитывается, только если в таре ровно то, что нужно:
    ///   • основа заполняет тару (если так в рецепте) — ведро полное сливок;
    ///   • ингредиенты — в количестве «на ведро × размер тары»;
    ///   • ничего лишнего (если OnlyThese), порядок — если OrderMatters;
    ///   • обработки — строго по очереди.
    /// Не так — в консоли и в аналитике пишется, ЧЕГО не хватает или что лишнее.
    ///
    /// Рецепты: все CookRecipeDef из Resources/CookRecipes. Нет ни одного — книга
    /// сама собирает «Мороженое» по названиям продуктов.
    /// </summary>
    public static class CookRecipeBook
    {
        private static List<CookRecipeDef> _recipes;
        private static readonly Dictionary<(Cookware, CookStepKind), float> _last = new Dictionary<(Cookware, CookStepKind), float>();
        public static bool Log = true;

        public static IReadOnlyList<CookRecipeDef> Recipes { get { Ensure(); return _recipes; } }

        private static float _nextTry;

        private static void Ensure()
        {
            if (_recipes != null && _recipes.Count > 0) return;
            if (_recipes != null && Time.time < _nextTry) return;     // пусто — не дёргать каждый кадр
            _nextTry = Time.time + 3f;
            _recipes = new List<CookRecipeDef>(Resources.LoadAll<CookRecipeDef>("CookRecipes"));
            if (_recipes.Count == 0) _recipes.AddRange(BuildDefaults());
            // Острый соус — добавляем сами, даже если в Resources уже лежат другие рецепты
            // (ассета соуса нет — работает этот, по названиям продуктов).
            if (!HasTitle(_recipes, "Острый соус"))
            {
                CookRecipeDef hs = BuildHotSauceDefault();
                if (hs != null) _recipes.Add(hs);
            }
            // Шоколад — так же: нет ассетов, работает по названиям продуктов.
            BuildChocolateDefaults(_recipes);
            // Хлеб: тесто в бетономешалке.
            BuildBreadDefaults(_recipes);
            // Десерты: шоколадная масса, яичная масса, бисквитное тесто, меренга.
            BuildDessertDefaults(_recipes);
            // Макарон «Солёная каска»: тесто, карамель, солёная карамель, ганаш.
            BuildMacaronDefaults(_recipes);
            // Рёбра барбекю: сухой руб, моп-соус, глазурь.
            BuildBBQDefaults(_recipes);
            // Ньокки: соус — масло с шалфеем (и пригоревшее).
            BuildGnocchiDefaults(_recipes);
            // Пельмени: тесто (замес в бетономешалке или ведре).
            BuildPelmeniDefaults(_recipes);
            // Борщ: бульон из опалённых костей и голяшки (старый «Бульон» остаётся — основа соусов).
            BuildBorschDefaults(_recipes);
            // Телятина по-орловски: дюксель, лук томлёный, субиз (дальше — морне, мясо, сборка).
            BuildOrloffDefaults(_recipes);
            // Бефстроганов: всё в одной сковороде — мясо, потом лук с мукой, потом сметана с пастой и вода.
            BuildStroganoffDefaults(_recipes);
            // Котлеты: хлебная масса (мякиш + молоко), фарш для котлет, отбитый фарш.
            BuildCutletDefaults(_recipes);
            if (Log) Debug.Log($"[Рецепты] в книге: {_recipes.Count}" + (_recipes.Count > 0 ? " (" + string.Join(", ", _recipes.ConvertAll(r => r.Title)) + ")" : ""));
        }

        /// <summary>
        /// Инструмент работает с тарой. seconds — сколько времени этой обработки прошло
        /// (если не знаешь — передай 0, книга сама посчитает по времени между вызовами).
        /// true — это шаг рецепта, обычную работу инструмент не делает. Только сервер.
        /// </summary>
        public static bool ServerProcess(Cookware cw, CookStepKind kind, float seconds = 0f)
        {
            if (cw == null || !cw.IsServerInitialized) return false;
            Ensure();
            if (_recipes.Count == 0) return false;
            CookwareJournal j = CookwareJournal.Of(cw);
            j.Sync();

            float dt = seconds;
            if (dt <= 0f)
            {
                var key = (cw, kind);
                float now = Time.time;
                dt = _last.TryGetValue(key, out float t) ? Mathf.Clamp(now - t, 0f, 0.3f) : 0f;
                _last[key] = now;
            }
            j.LogProcess(kind, dt);

            // v19: смесь по пропорциям (шоколадная паста): перебор → испорчено, досыпал → исправилось
            if (kind == CookStepKind.Mix && FoodBlend.ServerMix(cw, dt)) return true;

            // «Варить помешивая»: миксер в таре — это помешивание (комки уходят), а не свой шаг
            if ((kind == CookStepKind.Mix || kind == CookStepKind.Whip) && j.Recipe != null && j.StepIndex < j.Recipe.Steps.Count &&
                j.Recipe.Steps[j.StepIndex].Kind == CookStepKind.SimmerStir)
            {
                if (Log && Time.time - j.StirAt > 1.5f) Debug.Log($"[Рецепты] «{cw.name}»: {j.Recipe.Title} — помешали", cw);
                j.StirAt = Time.time;
                j.Lumps = Mathf.Max(0f, j.Lumps - dt * 2f);
                return true;
            }

            // Идёт рецепт — держится ли он ещё?
            if (j.Recipe != null)
            {
                string stageWhy = null;
                bool stageOk = !j.ChangedSinceStep && StageValid(cw, j, out stageWhy);
                if (!stageOk)
                {
                    j.Hint = $"{j.Recipe.Title} сбился: " + (j.ChangedSinceStep ? "добавили что-то после обработки" : stageWhy);
                    if (Log) Debug.Log($"[Рецепты] «{cw.name}»: {j.Hint}", cw);
                    j.ResetRecipe();
                }
            }

            // Не идёт — подобрать рецепт, у которого ПЕРВЫЙ шаг — эта обработка.
            if (j.Recipe == null)
            {
                string bestWhy = null;
                foreach (CookRecipeDef r in _recipes)
                {
                    if (r == null || r.Driver != CookDriver.Book || r.Steps.Count == 0 || r.Result == null || r.Steps[0].Kind != kind) continue;
                    if (CheckIngredients(cw, j, r, out int batches, out float score, out string why))
                    {
                        j.Recipe = r; j.StepIndex = 0; j.StepProgress = 0f; j.Batches = batches; j.Score = score;
                        j.ChangedSinceStep = false; j.Hint = null; j.StirAt = Time.time; j.Lumps = 0f;
                        if (Log) Debug.Log($"[Рецепты] «{cw.name}»: {r.Title} ×{batches} — начали «{CookwareJournal.KindName(kind)}», оценка {score:0}", cw);
                        break;
                    }
                    if (why != null && bestWhy == null) bestWhy = $"{r.Title}: {why}";
                }
                if (j.Recipe == null)
                {
                    if (bestWhy != null && bestWhy != j.Hint)
                    {
                        j.Hint = bestWhy;
                        if (Log) Debug.Log($"[Рецепты] «{cw.name}»: не по рецепту — {bestWhy}", cw);
                    }
                    return false;
                }
            }

            // Шаг рецепта — та ли это обработка?
            if (j.StepIndex >= j.Recipe.Steps.Count || j.Recipe.Steps[j.StepIndex].Kind != kind) return false;
            CookRecipeDef.Step step = j.Recipe.Steps[j.StepIndex];
            // перебор жидкого (сливки, молоко) сначала выкипает до нормы — шаг стоит
            if ((kind == CookStepKind.Simmer || kind == CookStepKind.Heat || kind == CookStepKind.SimmerStir || kind == CookStepKind.Rest) && ReduceExcess(cw, j, dt)) return true;
            // не мешают — копятся комки
            if (kind == CookStepKind.SimmerStir && !StirOk(cw, j, dt)) return true;
            int before = Pct(j.StepProgress, step.Seconds);
            j.StepProgress += dt;
            int after = Pct(j.StepProgress, step.Seconds);
            if (Log && after > before && after < 100)
                Debug.Log($"[Рецепты] «{cw.name}»: {j.Recipe.Title} — {CookwareJournal.KindName(kind)} {after}%", cw);
            if (j.StepProgress >= step.Seconds) CompleteStep(cw, j);
            return true;
        }

        /// <summary>
        /// Инструмент умеет несколько обработок (миксер — взбить и перемешать): выбрать ту,
        /// которая нужна рецепту в этой таре, и провести её. Идёт рецепт — его текущий шаг;
        /// нет — рецепт, который сходится по составу; ничего не сходится — тот, к которому
        /// ближе всего (чтобы подсказка была про него). Сервер.
        /// </summary>
        public static bool ServerProcessAny(Cookware cw, float seconds, params CookStepKind[] kinds)
        {
            if (cw == null || !cw.IsServerInitialized || kinds == null || kinds.Length == 0) return false;
            Ensure();
            if (_recipes.Count == 0) return ServerProcess(cw, kinds[0], seconds);
            // v19: в таре смесь по пропорциям — это «перемешать»
            if (System.Array.IndexOf(kinds, CookStepKind.Mix) >= 0 && FoodBlend.Applies(cw)) return ServerProcess(cw, CookStepKind.Mix, seconds);
            CookwareJournal j = CookwareJournal.Of(cw);
            j.Sync();
            CookStepKind pick = kinds[0];
            bool found = false;
            if (j.Recipe != null && j.StepIndex < j.Recipe.Steps.Count)
            {
                CookStepKind cur = j.Recipe.Steps[j.StepIndex].Kind;
                if (System.Array.IndexOf(kinds, cur) >= 0) { pick = cur; found = true; }
            }
            if (!found)
            {
                int bestClose = 0;
                foreach (CookStepKind k in kinds)
                {
                    foreach (CookRecipeDef r in _recipes)
                    {
                        if (r == null || r.Driver != CookDriver.Book || r.Steps.Count == 0 || r.Result == null || r.Steps[0].Kind != k) continue;
                        if (CheckIngredients(cw, j, r, out _, out _, out _)) { pick = k; found = true; break; }
                        int close = Closeness(j, r);
                        if (close > bestClose) { bestClose = close; pick = k; }
                    }
                    if (found) break;
                }
            }
            return ServerProcess(cw, pick, seconds);
        }

        /// <summary>Сколько компонентов рецепта уже лежит в таре.</summary>
        private static int Closeness(CookwareJournal j, CookRecipeDef r)
        {
            int n = 0;
            if (r.Base != null && j.CountOf(r.Base.Id) > 0) n++;
            foreach (CookRecipeDef.Ingredient i in r.Ingredients)
                if (i.Product != null && (j.CountOf(i.Product.Id) > 0 || (i.Alt != null && j.CountOf(i.Alt.Id) > 0))) n++;
            return n;
        }

        /// <summary>
        /// Есть ли смысл вести обработку kind в этой таре: идёт рецепт с таким шагом, или в таре лежит
        /// основа рецепта, который с него начинается. Чтобы нагрев не писал «растопить» любой воде. Сервер.
        /// </summary>
        public static bool Wants(Cookware cw, CookStepKind kind)
        {
            if (cw == null) return false;
            Ensure();
            CookwareJournal j = CookwareJournal.Peek(cw);
            if (j != null && j.Recipe != null && j.StepIndex < j.Recipe.Steps.Count && j.Recipe.Steps[j.StepIndex].Kind == kind) return true;
            foreach (CookRecipeDef r in _recipes)
            {
                if (r == null || r.Driver != CookDriver.Book || r.Steps.Count == 0 || r.Steps[0].Kind != kind || r.Base == null) continue;
                if (cw.CountOf(r.Base.Id) > 0) return true;
            }
            return false;
        }

        /// <summary>
        /// Огонь слишком сильный для уваривания (томатная паста): идущее уваривание сбивается, а то, что
        /// в таре, начинает пригорать (рецепт-ошибка с шагом «Пригорает на сильном огне»). Зовёт CookwareHeat. Сервер.
        /// </summary>
        public static bool ServerOverheat(Cookware cw, float seconds)
        {
            if (cw == null || !cw.IsServerInitialized) return false;
            Ensure();
            CookwareJournal j = CookwareJournal.Of(cw);
            if (j.Recipe != null && j.StepIndex < j.Recipe.Steps.Count && j.Recipe.Steps[j.StepIndex].Kind == CookStepKind.Simmer)
            {
                j.Hint = $"{j.Recipe.Title} сбился: огонь слишком сильный — не раздувай";
                if (Log) Debug.Log($"[Рецепты] «{cw.name}»: {j.Hint}", cw);
                j.ResetRecipe();
            }
            return Wants(cw, CookStepKind.Burn) && ServerProcess(cw, CookStepKind.Burn, seconds);
        }

        private static readonly Dictionary<Cookware, float> _reduceAcc = new Dictionary<Cookware, float>();
        private static readonly Dictionary<Cookware, float> _reduceLogAt = new Dictionary<Cookware, float>();
        /// <summary>Сколько порций лишнего жидкого выкипает в секунду.</summary>
        public const float ReducePerSecond = 4f;
        /// <summary>«Варить помешивая»: сколько секунд можно не мешать; сколько секунд без помешивания — комки.</summary>
        public const float StirGrace = 4f, LumpSeconds = 6f;

        /// <summary>Перебор «уваривающихся» ингредиентов (сливки, молоко) выкипает до нормы. true — ещё уваривается (шаг ждёт).</summary>
        private static bool ReduceExcess(Cookware cw, CookwareJournal j, float dt)
        {
            CookRecipeDef r = j.Recipe;
            if (r == null || j.StepIndex > 0 && r.Steps[j.StepIndex - 1].Intermediate != null) return false;   // уже другой продукт — нечего уваривать
            bool any = false;
            foreach (CookRecipeDef.Ingredient ing in r.Ingredients)
            {
                if (!ing.Reduces || ing.Product == null) continue;
                int have = Have(cw, j, ing, out _);
                int need = Mathf.Max(1, ing.ToPortions(ing.Amount * Mathf.Max(1, j.Batches)));
                int extra = have - need;
                if (extra <= 0) continue;
                any = true;
                _reduceAcc.TryGetValue(cw, out float acc);
                acc += dt * ReducePerSecond;
                int n = Mathf.Min(extra, Mathf.FloorToInt(acc));
                if (n > 0)
                {
                    acc -= n;
                    int took = cw.ServerTake(ing.Product.Id, n);
                    if (took < n && ing.Alt != null) took += cw.ServerTake(ing.Alt.Id, n - took);
                    extra -= took;
                }
                _reduceAcc[cw] = acc;
                j.Hint = $"{r.Title}: уваривается лишнее — {ing.Product.Title}, ещё {ing.Describe(Mathf.Max(0, extra))}";
                _reduceLogAt.TryGetValue(cw, out float at);
                if (Log && Time.time >= at) { _reduceLogAt[cw] = Time.time + 2f; Debug.Log($"[Рецепты] «{cw.name}»: {j.Hint}", cw); }
                break;
            }
            if (!any) _reduceAcc.Remove(cw);
            return any;
        }

        /// <summary>«Варить помешивая»: мешали недавно — шаг идёт; нет — копятся комки, дольше LumpSeconds — «… с комками». false — шаг стоит.</summary>
        private static bool StirOk(Cookware cw, CookwareJournal j, float dt)
        {
            if (Time.time - j.StirAt <= StirGrace) return true;
            j.Lumps += dt;
            int pct = Mathf.Clamp(Mathf.RoundToInt(j.Lumps / LumpSeconds * 100f), 0, 100);
            j.Hint = $"{j.Recipe.Title}: ПОМЕШАЙ миксером! комки {pct}%";
            _reduceLogAt.TryGetValue(cw, out float at);
            if (Log && Time.time >= at) { _reduceLogAt[cw] = Time.time + 2f; Debug.Log($"[Рецепты] «{cw.name}»: {j.Hint}", cw); }
            if (j.Lumps < LumpSeconds) return false;
            Lumpify(cw, j);
            return false;
        }

        /// <summary>Не мешали — всё в таре по рецепту становится «&lt;результат&gt; с комками» (ошибка повара).</summary>
        private static void Lumpify(Cookware cw, CookwareJournal j)
        {
            CookRecipeDef r = j.Recipe;
            FoodDatabase db = Object.FindAnyObjectByType<FoodDatabase>();
            ProductDef bad = db != null && r.Result != null ? FindExact(db, r.Result.Title.Trim().ToLowerInvariant() + " с комками") : null;
            if (bad == null)
            {
                j.Hint = $"{r.Title} сбился: не мешали — комки";
                if (Log) Debug.Log($"[Рецепты] «{cw.name}»: {j.Hint} (нет продукта «{(r.Result != null ? r.Result.Title : "?")} с комками»)", cw);
                j.ResetRecipe();
                return;
            }
            int total = 0;
            var snap = new List<BagView>();
            for (int i = 0; i < cw.ViewCount; i++) snap.Add(cw.GetView(i));
            foreach (BagView v in snap)
                if (v.Count > 0 && r.Uses(v.Product)) total += cw.ServerTake(v.Product, v.Count);
            if (total > 0) cw.ServerAddPinch(bad.Id, bad.StartState, Mathf.Max(1, Mathf.RoundToInt(total * 0.8f)), 0, false);
            j.LastResult = $"{bad.Title} — не мешали";
            j.Hint = j.LastResult;
            if (Log) Debug.Log($"[Рецепты] «{cw.name}»: ОШИБКА — {bad.Title}: не мешали дольше {StirGrace + LumpSeconds:0} с", cw);
            j.ResetRecipe();
        }

        private static int Pct(float p, float s) => Mathf.Clamp(Mathf.FloorToInt(p / Mathf.Max(0.1f, s) * 4f) * 25, 0, 100);

        // ───────────────────────── шаги ─────────────────────────

        private static void CompleteStep(Cookware cw, CookwareJournal j)
        {
            CookRecipeDef r = j.Recipe;
            CookRecipeDef.Step step = r.Steps[j.StepIndex];
            bool last = j.StepIndex >= r.Steps.Count - 1;
            ProductDef into = last ? r.Result : step.Intermediate;
            if (into != null)
            {
                // Всё по рецепту — в один продукт, порций столько же (объём сохраняется).
                int total = 0;
                var snap = new List<BagView>();
                for (int i = 0; i < cw.ViewCount; i++) snap.Add(cw.GetView(i));
                bool onlyBase = last && r.OnlyBaseBecomesResult && r.Base != null;
                foreach (BagView v in snap)
                {
                    if (v.Count <= 0 || v.Product == into.Id) continue;
                    if (onlyBase ? v.Product != r.Base.Id : !r.Uses(v.Product)) continue;
                    total += cw.ServerTake(v.Product, v.Count);
                }
                // объём после шага: взбитое пышнее (×2), перебитое оседает (×0.6)
                float vk = step.VolumeK > 0.01f ? step.VolumeK : 1f;
                int outN = Mathf.Max(total > 0 ? 1 : 0, Mathf.RoundToInt(total * vk));
                if (outN > 0) cw.ServerAddPinch(into.Id, into.StartState, outN, 0, false);
                if (Log && total > 0 && !Mathf.Approximately(vk, 1f))
                    Debug.Log($"[Рецепты] «{cw.name}»: объём {total} → {outN} порц. (×{vk:0.##})", cw);
            }
            if (Log) Debug.Log($"[Рецепты] «{cw.name}»: {r.Title} — {CookwareJournal.KindName(step.Kind)} 100%" +
                               (into != null ? $" → {into.Title}" : ""), cw);
            j.StepIndex++;
            j.StepProgress = 0f;
            j.Sync();
            j.ChangedSinceStep = false;
            if (last)
            {
                j.LastResult = $"{r.Result.Title} ×{j.Batches}, оценка {j.Score:0}/100";
                if (Log) Debug.Log($"[Рецепты] «{cw.name}»: ГОТОВО — {j.LastResult}", cw);
                j.ResetRecipe();
            }
        }

        /// <summary>Содержимое всё ещё соответствует текущей стадии рецепта?</summary>
        private static bool StageValid(Cookware cw, CookwareJournal j, out string why)
        {
            why = null;
            CookRecipeDef r = j.Recipe;
            // последний сделанный шаг с промежуточным продуктом — теперь в таре он
            ProductDef inter = null;
            for (int i = j.StepIndex - 1; i >= 0; i--)
                if (r.Steps[i].Intermediate != null) { inter = r.Steps[i].Intermediate; break; }
            if (inter != null)
            {
                if (j.CountOf(inter.Id) <= 0) { why = $"нет «{inter.Title}»"; return false; }
                if (r.OnlyThese)
                    for (int i = 0; i < cw.ViewCount; i++)
                    {
                        BagView v = cw.GetView(i);
                        if (v.Count > 0 && v.Product != inter.Id) { why = "в смеси лишнее"; return false; }
                    }
                return true;
            }
            return CheckIngredients(cw, j, r, out _, out _, out why);
        }

        // ───────────────────────── проверка состава ─────────────────────────

        /// <summary>В таре ровно то, что нужно рецепту (с допуском)? why — что не так.</summary>
        public static bool CheckIngredients(Cookware cw, CookwareJournal j, CookRecipeDef r, out int batches, out float score, out string why)
        {
            batches = 1; score = 100f; why = null;
            var sb = new StringBuilder();
            FoodDatabase db = Object.FindAnyObjectByType<FoodDatabase>();
            int kinds = 0;
            float devSum = 0f; int devN = 0;

            int baseHave = r.Base != null ? BaseHave(cw, j, r) : 0;
            batches = r.BatchesFor(cw, baseHave);
            if (r.Base != null)
            {
                if (baseHave > 0) kinds++;
                // «Полная тара» — от её собственной вместимости (без запаса на перелив).
                int full = cw.Def != null ? cw.Def.Capacity : FoodUnits.Bucket;
                if (r.PerContainer && r.BaseFillsContainer)
                {
                    // основа + всё остальное вместе — полная тара
                    int needTotal = Mathf.CeilToInt(full * r.BaseMinFill);
                    int total = cw.TotalCount;
                    if (baseHave <= 0) sb.Append($"нет основы: {r.Base.Title}; ");
                    else if (total < needTotal)
                        sb.Append($"{r.Base.Title}: долить до полной тары (ещё {FoodUnits.Describe(needTotal - total, r.BaseUnit)}); ");
                }
                else
                {
                    int need = r.BaseFillsContainer
                        ? Mathf.CeilToInt(full * r.BaseMinFill)
                        : FoodUnits.ToPortions(r.BaseAmount, r.BaseUnit);
                    if (baseHave < need)
                        sb.Append($"{r.Base.Title}: {FoodUnits.Describe(baseHave, r.BaseUnit)}, нужно " +
                                  (r.BaseFillsContainer ? "полную тару" : FoodUnits.Describe(need, r.BaseUnit)) + "; ");
                }
            }

            foreach (CookRecipeDef.Ingredient ing in r.Ingredients)
            {
                if (ing.Product == null) continue;
                int have = Have(cw, j, ing, out int dirty);
                if (have > 0 || dirty > 0) kinds++;
                int need = ing.ToPortions(ing.Amount * batches);
                int slack = Mathf.FloorToInt(need * r.Tolerance + 1e-4f);
                if (need > 0) { devSum += Mathf.Abs(have - need) / (float)need; devN++; }
                if (dirty > 0)
                    sb.Append($"{ing.Product.Title}: {ing.Describe(dirty)} грязные/гнилые — не в счёт; ");
                if (ing.Reduces && have > need) { if (need > 0) devSum -= Mathf.Abs(have - need) / (float)need; }   // перебор уварится — не в минус
                if (have < need - slack)
                    sb.Append($"{ing.Product.Title}: {ing.Describe(have)}, нужно {ing.Describe(need)}" + (ing.Reduces ? " (больше — можно, лишнее уварится)" : "") + "; ");
                else if (have > need + slack && !ing.Reduces)
                    sb.Append($"{ing.Product.Title}: {ing.Describe(have)} — перебор, нужно {ing.Describe(need)}; ");
            }

            if (r.OnlyThese)
                for (int i = 0; i < cw.ViewCount; i++)
                {
                    BagView v = cw.GetView(i);
                    if (v.Count == 0 || r.Uses(v.Product)) continue;
                    ProductDef d = db != null ? db.Get(v.Product) : null;
                    sb.Append($"лишнее: {(d != null ? d.Title : "?")}; ");
                }

            if (r.OrderMatters && !OrderOk(j, r, out string ow)) sb.Append(ow + "; ");

            score = Mathf.Clamp(100f * (1f - (devN > 0 ? devSum / devN : 0f)), 0f, 100f);
            if (sb.Length == 0) return true;
            // подсказку даём, когда начали собирать именно этот рецепт (хотя бы 2 его компонента в таре)
            if (kinds >= 2) why = sb.ToString() + $"(тара вмещает {FoodUnits.Describe(Mathf.RoundToInt(FoodUnits.BucketsIn(cw) * FoodUnits.Bucket), CookUnit.Bucket)})";
            return false;
        }

        /// <summary>
        /// Сколько основы в таре: если у рецепта задано состояние основы — только в нём.
        /// «Пюре» засчитывает и «Сварено» / «Переварено»: варёная картошка в бетономешалке разминается при замесе.
        /// </summary>
        private static int BaseHave(Cookware cw, CookwareJournal j, CookRecipeDef r)
        {
            if (r.BaseState == FoodState.None) return j.CountOf(r.Base.Id);
            int n = 0;
            for (int i = 0; i < cw.ViewCount; i++)
            {
                BagView v = cw.GetView(i);
                if (v.Product == r.Base.Id && BaseStateOk(r.BaseState, v.State)) n += v.Count;
            }
            return n;
        }

        private static bool BaseStateOk(FoodState want, FoodState have)
        {
            if (have == want) return true;
            if (want == FoodState.Puree) return have == FoodState.Boiled || have == FoodState.Overcooked;
            return false;
        }

        /// <summary>Сколько порций ингредиента в таре (с заменой Alt). CleanOnly — без грязных и гнилых (их — в dirty).</summary>
        private static int Have(Cookware cw, CookwareJournal j, CookRecipeDef.Ingredient ing, out int dirty)
        {
            dirty = 0;
            // куски, что плавают в кастрюле-супе собой (не проглочены), — тоже в счёт
            int loose = cw.LooseCount(ing.Product.Id, ing.Alt != null ? ing.Alt.Id : (ushort)0, ing.State, ing.CleanOnly, out int looseDirty);
            dirty += looseDirty;
            if (!ing.CleanOnly && ing.State == FoodState.None)
                return j.CountOf(ing.Product.Id) + (ing.Alt != null ? j.CountOf(ing.Alt.Id) : 0) + loose;
            int have = loose;
            for (int i = 0; i < cw.ViewCount; i++)
            {
                BagView v = cw.GetView(i);
                if (v.Count == 0) continue;
                if (v.Product != ing.Product.Id && (ing.Alt == null || v.Product != ing.Alt.Id)) continue;
                // нужное состояние (лук — тёртый): Alt (сало вместо масла) — в любом
                if (ing.State != FoodState.None && v.Product == ing.Product.Id && !BaseStateOk(ing.State, v.State)) continue;
                if (ing.CleanOnly && (v.State == FoodState.Dirty || FoodStateInfo.IsSpoiled(v.State))) dirty += v.Count;
                else have += v.Count;
            }
            return have;
        }

        /// <summary>Есть ли в книге рецепт с таким названием (старый FoodRecipeDef с тем же именем тогда не варится).</summary>
        public static bool HasBookRecipe(string title)
        {
            if (string.IsNullOrEmpty(title)) return false;
            Ensure();
            foreach (CookRecipeDef r in _recipes)
                if (r != null && r.Title == title) return true;
            return false;
        }

        private static bool OrderOk(CookwareJournal j, CookRecipeDef r, out string why)
        {
            why = null;
            var wanted = new List<ushort>();
            if (r.Base != null) wanted.Add(r.Base.Id);
            foreach (CookRecipeDef.Ingredient i in r.Ingredients) if (i.Product != null) wanted.Add(i.Product.Id);
            int last = -1;
            foreach (ushort p in wanted)
            {
                int at = -1;
                for (int k = 0; k < j.Order.Count; k++) if (j.Order[k] == p) { at = k; break; }
                if (at < 0) continue;
                if (at < last) { why = "не тот порядок добавления"; return false; }
                last = at;
            }
            return true;
        }

        // ───────────────────────── мороженое по умолчанию ─────────────────────────

        /// <summary>Базовые рецепты по названиям продуктов — пока нет ассетов (меню CatCooks → Рецепты → Создать базовые).</summary>
        private static List<CookRecipeDef> BuildDefaults()
        {
            var list = new List<CookRecipeDef>();
            FoodDatabase db = Object.FindAnyObjectByType<FoodDatabase>();
            if (db == null || db.Table == null) return list;
            ProductDef milk = Find(db, "молок"), cream = Find(db, "сливки"), butter = Find(db, "сливочное масло");
            ProductDef sour = Find(db, "сметан"), starter = Find(db, "закваск");
            ProductDef sugar = Find(db, "сахар"), egg = Find(db, "яйцо"), yolk = Find(db, "желток");
            ProductDef ice = Find(db, "морожен"), mix = Find(db, "смесь для морож");
            ProductDef water = Find(db, "вода"), onion = Find(db, "лук репч"), onionRed = Find(db, "лук красн");
            ProductDef carrot = Find(db, "морков"), bones = Find(db, "кости"), broth = FindExact(db, "бульон");
            if (water != null && onion != null && carrot != null && bones != null && broth != null)
            {
                CookRecipeDef br = ScriptableObject.CreateInstance<CookRecipeDef>();
                br.name = "Recipe_Бульон (auto)";
                br.hideFlags = HideFlags.DontSave;
                list.Add(Broth(br, water, onion, onionRed, carrot, bones, broth));
            }

            if (milk != null && cream != null)
                list.Add(NewRecipe("Сливки", CookDriver.Vibrator, milk, cream, CookStepKind.Whip, 45f, null));
            if (cream != null && butter != null)
                list.Add(NewRecipe("Сливочное масло", CookDriver.Vibrator, cream, butter, CookStepKind.Whip, 30f, null));
            if (cream != null && sour != null && starter != null)
            {
                CookRecipeDef r = NewRecipe("Сметана", CookDriver.Culture, cream, sour, CookStepKind.Ferment, 45f, null);
                r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = starter, Amount = 1f, Unit = CookUnit.Dose });
                r.Tolerance = 0.15f;
                list.Add(r);
            }
            if (cream != null && sugar != null && (egg != null || yolk != null) && ice != null)
            {
                CookRecipeDef r = NewRecipe("Мороженое", CookDriver.Book, cream, ice, CookStepKind.Mix, 6f, mix);
                r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = sugar, Amount = 1f, Unit = CookUnit.Trowel });
                r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = egg != null ? egg : yolk, Alt = egg != null ? yolk : null, Amount = 1f, Unit = CookUnit.Piece });
                r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Freeze, Seconds = 2.5f });
                list.Add(r);
            }
            return list;
        }

        /// <summary>
        /// БУЛЬОН на кастрюлю: вода доверху + 5 луковиц + 5 морковок + 10 костей (мытые/чищеные),
        /// варить на кипении 90 с → вода становится бульоном, овощи и кости остаются (вынуть).
        /// </summary>
        public static CookRecipeDef Broth(CookRecipeDef r, ProductDef water, ProductDef onion, ProductDef onionRed,
                                          ProductDef carrot, ProductDef bones, ProductDef broth)
        {
            r.Title = "Бульон"; r.Driver = CookDriver.Book;
            r.Base = water; r.BaseAmount = 1f; r.BaseUnit = CookUnit.Bucket;
            r.BaseFillsContainer = true; r.BaseMinFill = 0.85f;
            r.PerContainer = true; r.OnlyBaseBecomesResult = true;
            r.OrderMatters = false; r.OnlyThese = false; r.Tolerance = 0.2f;
            r.Ingredients.Clear();
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = onion, Alt = onionRed, Amount = 5f, Unit = CookUnit.Piece, PortionsPerPiece = 6, CleanOnly = true });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = carrot, Amount = 5f, Unit = CookUnit.Piece, PortionsPerPiece = 7, CleanOnly = true });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = bones, Amount = 10f, Unit = CookUnit.Piece, PortionsPerPiece = 1 });
            r.Steps.Clear();
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Heat, Seconds = 90f });
            r.Result = broth;
            return r;
        }

        private static CookRecipeDef NewRecipe(string title, CookDriver driver, ProductDef from, ProductDef result,
                                               CookStepKind step, float seconds, ProductDef intermediate)
        {
            var r = ScriptableObject.CreateInstance<CookRecipeDef>();
            r.name = "Recipe_" + title + " (auto)";
            r.hideFlags = HideFlags.DontSave;
            r.Title = title; r.Driver = driver;
            r.Base = from; r.BaseAmount = 1f; r.BaseUnit = CookUnit.Bucket; r.BaseFillsContainer = true;
            r.Steps.Add(new CookRecipeDef.Step { Kind = step, Seconds = seconds, Intermediate = intermediate });
            r.Result = result;
            return r;
        }

        // ───────────────────────── «родные» процессы (вибратор, закваска) ─────────────────────────

        /// <summary>
        /// Вибратор или закваска ведут свою обработку; здесь она попадает в журнал тары —
        /// аналитик видит рецепт, шаг и проценты. progress01 — доля всей обработки. Сервер.
        /// </summary>
        public static void ReportNative(Cookware cw, CookDriver driver, ProductDef from, ProductDef to, float progress01)
        {
            if (cw == null || from == null || to == null) return;
            Ensure();
            CookRecipeDef r = FindNative(driver, from, to);
            if (r == null) return;
            CookwareJournal j = CookwareJournal.Of(cw);
            if (j.Recipe != r) { j.ResetRecipe(); j.Recipe = r; j.Batches = r.BatchesFor(cw, j.CountOf(from.Id)); j.Hint = null; }
            j.StepIndex = 0;
            float secs = r.Steps.Count > 0 ? r.Steps[0].Seconds : 1f;
            j.StepProgress = Mathf.Clamp01(progress01) * secs;
            j.ChangedSinceStep = false;
        }

        /// <summary>Родная обработка закончилась: результат — в журнал, рецепт сбрасывается. Сервер.</summary>
        public static void FinishNative(Cookware cw, CookDriver driver, ProductDef from, ProductDef to, string text)
        {
            if (cw == null) return;
            CookwareJournal j = CookwareJournal.Peek(cw);
            if (j == null) return;
            Ensure();
            CookRecipeDef r = from != null && to != null ? FindNative(driver, from, to) : null;
            if (r != null && j.Recipe != null && j.Recipe != r) return;
            j.LastResult = string.IsNullOrEmpty(text) ? (to != null ? to.Title : "готово") : text;
            j.ResetRecipe();
        }

        private static CookRecipeDef FindNative(CookDriver driver, ProductDef from, ProductDef to)
        {
            foreach (CookRecipeDef r in _recipes)
                if (r != null && r.Driver == driver && r.Base == from && r.Result == to) return r;
            return null;
        }

        // ───────────────────────── подсказка «что дальше» ─────────────────────────

        /// <summary>
        /// Что за рецепт собирается в таре и чего ему не хватает: основа, ингредиенты в мерах,
        /// потом какая обработка и чем. null — ничего похожего. Клиент и сервер (по журналу).
        /// </summary>
        public static string Suggest(Cookware cw, CookwareJournal j)
        {
            if (cw == null || j == null || j.IsEmpty || j.Recipe != null) return null;
            Ensure();
            // Рецепты с самым полным набором компонентов в таре; при ничьей (одни сливки — это и
            // масло, и сметана, и мороженое) показываем до трёх вариантов.
            var ties = new List<CookRecipeDef>();
            int bestKinds = 0;
            foreach (CookRecipeDef r in _recipes)
            {
                if (r == null || r.IsMistake || r.Base == null || j.CountOf(r.Base.Id) <= 0) continue;
                if (r.Result != null && cw.CountOf(r.Result.Id) > 0 && r.OnlyBaseBecomesResult) continue;   // бульон уже сварен — подсказываем следующее (борщ)
                int kinds = 1;
                foreach (CookRecipeDef.Ingredient ing in r.Ingredients)
                    if (ing.Product != null && (j.CountOf(ing.Product.Id) > 0 || (ing.Alt != null && j.CountOf(ing.Alt.Id) > 0))) kinds++;
                if (kinds > bestKinds) { ties.Clear(); bestKinds = kinds; }
                if (kinds == bestKinds) ties.Add(r);
            }
            if (ties.Count == 0) return null;
            var all = new StringBuilder();
            for (int k = 0; k < Mathf.Min(3, ties.Count); k++)
            {
                if (k > 0) all.Append("\n➜ ");
                all.Append(SuggestFor(cw, j, ties[k]));
            }
            return all.ToString();
        }

        private static string SuggestFor(Cookware cw, CookwareJournal j, CookRecipeDef best)
        {
            var sb = new StringBuilder();
            int full = cw.Def != null ? cw.Def.Capacity : FoodUnits.Bucket;
            int baseHave = BaseHave(cw, j, best);
            int baseNeed = best.BaseFillsContainer ? Mathf.CeilToInt(full * best.BaseMinFill) : FoodUnits.ToPortions(best.BaseAmount, best.BaseUnit);
            int batches = best.BatchesFor(cw, baseHave);
            bool ok = true;
            if (best.PerContainer && best.BaseFillsContainer)
            {
                int total = cw.TotalCount;
                if (total < baseNeed)
                {
                    ok = false;
                    sb.Append($"{best.Base.Title}: долить до полной тары (ещё {FoodUnits.Describe(baseNeed - total, best.BaseUnit)}); ");
                }
            }
            else if (baseHave < baseNeed)
            {
                ok = false;
                sb.Append($"{best.Base.Title}: {FoodUnits.Describe(baseHave, best.BaseUnit)} из {FoodUnits.Describe(baseNeed, best.BaseUnit)}; ");
            }
            foreach (CookRecipeDef.Ingredient ing in best.Ingredients)
            {
                if (ing.Product == null) continue;
                int have = Have(cw, j, ing, out int dirty);
                int need = ing.ToPortions(ing.Amount * batches);
                int slack = Mathf.FloorToInt(need * best.Tolerance + 1e-4f);
                if (dirty > 0) { ok = false; sb.Append($"{ing.Product.Title}: {ing.Describe(dirty)} грязные — сначала помыть/почистить; "); }
                if (have < need - slack) { ok = false; sb.Append($"ещё {ing.Product.Title}: {ing.Describe(need - have)}; "); }
                else if (have > need + slack && ing.Reduces) sb.Append($"{ing.Product.Title}: лишние {ing.Describe(have - need)} уварятся на огне; ");
                else if (have > need + slack) { ok = false; sb.Append($"{ing.Product.Title}: перебор на {ing.Describe(have - need)}; "); }
            }
            string todo = "";
            if (ok && best.Steps.Count > 0)
                todo = " → теперь: " + StepTool(best.Steps[0].Kind, best.Driver);
            return $"Рецепт «{best.Title}»: " + (sb.Length > 0 ? sb.ToString() : "всё есть") + todo;
        }

        public static string StepTool(CookStepKind k, CookDriver d)
        {
            switch (k)
            {
                case CookStepKind.Mix: return "перемешать миксером";
                case CookStepKind.Whip: return d == CookDriver.Vibrator ? "взбить вибратором" : "взбить миксером (венчик в таре, ЛКМ)";
                case CookStepKind.Freeze: return "заморозить азотом";
                case CookStepKind.Ferment: return "ждать, пока сквасится";
                case CookStepKind.Heat: return "варить: поставить на огонь (костёр, горелка под ковш), должно кипеть";
                case CookStepKind.Grind: return "измельчить: отбойник носиком в тару, зажать ЛКМ";
                case CookStepKind.Rest: return "выдержать: просто подождать (можно снять с огня)";
                case CookStepKind.Roast: return "обжарить: тару без воды на огонь (костёр, горелка под ковш)";
                case CookStepKind.Melt: return "растопить: тару на огонь, греть от 40 °C (кипятить не нужно)";
                case CookStepKind.Caramelize: return "карамелизовать: сахар в кастрюле на огонь, раздуть до 160 °C, держать до янтарного — и СНЯТЬ (от 200 °C пригорит)";
                case CookStepKind.Scorch: return "снять с огня — пригорает";
                case CookStepKind.Simmer: return "уваривать: на огонь, но НЕ раздувать (костёр без шин, огонь до ~175 °C) — на сильном огне пригорит";
                case CookStepKind.Fry: return "жарить: сковороду на огонь (F9), от 95 °C";
                case CookStepKind.Stew: return "тушить: держать на огне, от 85 °C";
                case CookStepKind.Burn: return "огонь слишком сильный — пригорает! не раздувай, убери доски";
                case CookStepKind.SimmerStir: return "варить помешивая: на огонь и МЕШАТЬ миксером (ЛКМ в таре) хотя бы раз в 4 с — иначе комки";
                case CookStepKind.Pound: return "отбить: КУВАЛДОЙ по таре (ведру, банке) — удар за ударом";
                default: return "выдержать";
            }
        }

        private static ProductDef FindExact(FoodDatabase db, string title)
        {
            for (int id = 1; id < 2048; id++)
            {
                ProductDef p = db.Table.GetProduct((ushort)id);
                if (p != null && !string.IsNullOrEmpty(p.Title) && p.Title.Trim().ToLowerInvariant() == title) return p;
            }
            return null;
        }

        private static ProductDef Find(FoodDatabase db, string part)
        {
            for (int id = 1; id < 2048; id++)
            {
                ProductDef p = db.Table.GetProduct((ushort)id);
                if (p == null || string.IsNullOrEmpty(p.Title)) continue;
                string s = p.Title.ToLowerInvariant();
                if (!s.Contains(part)) continue;
                if (part == "сливки" && s.Contains("масло")) continue;
                if (part == "яйцо" && (s.Contains("желток") || s.Contains("белок"))) continue;
                return p;
            }
            return null;
        }

        private static bool HasTitle(List<CookRecipeDef> list, string title)
        {
            foreach (CookRecipeDef r in list) if (r != null && r.Title == title) return true;
            return false;
        }

        private static CookRecipeDef BuildHotSauceDefault()
        {
            FoodDatabase db = Object.FindAnyObjectByType<FoodDatabase>();
            if (db == null || db.Table == null) return null;
            ProductDef water = FindExact(db, "вода"), chili = Find(db, "перец чили"), vinegar = Find(db, "уксус");
            ProductDef salt = FindExact(db, "соль"), clove = Find(db, "зубчик"), mix = Find(db, "острая смесь");
            ProductDef sauce = FindExact(db, "острый соус");
            if (water == null || chili == null || vinegar == null || salt == null || clove == null || sauce == null) return null;
            CookRecipeDef r = ScriptableObject.CreateInstance<CookRecipeDef>();
            r.name = "Recipe_Острый соус (auto)";
            r.hideFlags = HideFlags.DontSave;
            return HotSauce(r, water, chili, vinegar, salt, clove, mix, sauce);
        }

        /// <summary>
        /// ОСТРЫЙ СОУС (на 1 ведро воды; 2 ведра воды — всего вдвое):
        ///   вода 1 ведро + перец чили 3 шт. (кольцами или целиком, по 8 колец) + уксус 2 дозы
        ///   + соль 1 мастерок + чеснок 4 зубчика;
        ///   1. измельчить отбойником 8 с → «Острая смесь»;
        ///   2. варить на кипении 40 с → загустевает в «Острый соус» (густой, как мёд);
        ///   3. выдержать 10 с → готово.
        /// </summary>
        public static CookRecipeDef HotSauce(CookRecipeDef r, ProductDef water, ProductDef chili, ProductDef vinegar,
                                             ProductDef salt, ProductDef clove, ProductDef mix, ProductDef sauce)
        {
            r.Title = "Острый соус"; r.Driver = CookDriver.Book;
            r.Base = water; r.BaseAmount = 1f; r.BaseUnit = CookUnit.Bucket;
            r.BaseFillsContainer = false; r.BaseMinFill = 0.9f;
            r.PerContainer = false; r.OnlyBaseBecomesResult = false;
            r.OrderMatters = false; r.OnlyThese = true; r.Tolerance = 0.25f;
            r.Ingredients.Clear();
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = chili, Amount = 3f, Unit = CookUnit.Piece, PortionsPerPiece = 8, CleanOnly = false });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = vinegar, Amount = 2f, Unit = CookUnit.Dose });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = salt, Amount = 1f, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = clove, Amount = 4f, Unit = CookUnit.Piece, PortionsPerPiece = 1 });
            r.Steps.Clear();
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Grind, Seconds = 8f, Intermediate = mix });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Heat, Seconds = 40f, Intermediate = sauce });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Rest, Seconds = 10f });
            r.Result = sauce;
            return r;
        }

        private static void BuildChocolateDefaults(List<CookRecipeDef> list)
        {
            FoodDatabase db = Object.FindAnyObjectByType<FoodDatabase>();
            if (db == null || db.Table == null) return;
            ProductDef roasted = FindExact(db, "обжаренные какао-зёрна") ?? FindExact(db, "обжаренные какао-зерна");
            ProductDef ground = FindExact(db, "тёртое какао") ?? FindExact(db, "тертое какао");
            ProductDef sugar = Find(db, "сахар");
            ProductDef butter = Find(db, "сливочное масло"), paste = FindExact(db, "шоколадная паста");
            if (!HasTitle(list, "Тёртое какао") && roasted != null && ground != null)
            {
                CookRecipeDef r = ScriptableObject.CreateInstance<CookRecipeDef>();
                r.name = "Recipe_Тёртое какао (auto)"; r.hideFlags = HideFlags.DontSave;
                list.Add(CocoaGround(r, roasted, ground));
            }
            if (!HasTitle(list, "Шоколадная паста") && ground != null && sugar != null && butter != null && paste != null)
            {
                CookRecipeDef r = ScriptableObject.CreateInstance<CookRecipeDef>();
                r.name = "Recipe_Шоколадная паста (auto)"; r.hideFlags = HideFlags.DontSave;
                list.Add(ChocolatePaste(r, ground, sugar, butter, paste));
            }
        }

        /// <summary>
        /// ТЁРТОЕ КАКАО (на 12 порций обжаренных какао-зёрен = одна гроздь из одного боба; 24 — всё вдвое):
        /// зёрна жарятся КУСКОМ (FoodRoastable), обжаренные сами уходят в тару порциями;
        /// измельчить отбойником 8 с → «Тёртое какао» (пыль).
        /// </summary>
        public static CookRecipeDef CocoaGround(CookRecipeDef r, ProductDef roasted, ProductDef ground)
        {
            r.Title = "Тёртое какао"; r.Driver = CookDriver.Book;
            r.Base = roasted; r.BaseAmount = 12f; r.BaseUnit = CookUnit.Portion;
            r.BaseFillsContainer = false; r.BaseMinFill = 0.9f;
            r.PerContainer = false; r.OnlyBaseBecomesResult = false;
            r.OrderMatters = false; r.OnlyThese = true; r.Tolerance = 0.25f;
            r.Ingredients.Clear();
            r.Steps.Clear();
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Grind, Seconds = 8f });
            r.Result = ground;
            return r;
        }

        /// <summary>
        /// ШОКОЛАДНАЯ ПАСТА (на 12 порций тёртого какао): сахар 1 мастерок + сливочное масло 1 мастерок,
        /// перемешать (миксер или отбойник) 6 с → «Шоколадная паста». Дальше паста сама застывает в «Шоколад»
        /// (ProductDef → Hardens Into), пока не застыла — переложи мастерком.
        /// </summary>
        public static CookRecipeDef ChocolatePaste(CookRecipeDef r, ProductDef ground, ProductDef sugar, ProductDef butter, ProductDef paste)
        {
            r.Title = "Шоколадная паста"; r.Driver = CookDriver.Book;
            r.Base = ground; r.BaseAmount = 12f; r.BaseUnit = CookUnit.Portion;
            r.BaseFillsContainer = false; r.BaseMinFill = 0.9f;
            r.PerContainer = false; r.OnlyBaseBecomesResult = false;
            r.OrderMatters = false; r.OnlyThese = true; r.Tolerance = 0.25f;
            r.Ingredients.Clear();
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = sugar, Amount = 1f, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = butter, Amount = 1f, Unit = CookUnit.Trowel });
            r.Steps.Clear();
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = 6f });
            r.Result = paste;
            return r;
        }

        private static void BuildBreadDefaults(List<CookRecipeDef> list)
        {
            if (HasTitle(list, "Тесто дрожжевое")) return;
            FoodDatabase db = Object.FindAnyObjectByType<FoodDatabase>();
            if (db == null || db.Table == null) return;
            ProductDef water = FindExact(db, "вода"), flour = FindExact(db, "мука пшеничная") ?? Find(db, "мука");
            ProductDef yeast = FindExact(db, "дрожжи"), salt = FindExact(db, "соль"), dough = FindExact(db, "тесто дрожжевое");
            if (water == null || flour == null || yeast == null || salt == null || dough == null) return;
            CookRecipeDef r = ScriptableObject.CreateInstance<CookRecipeDef>();
            r.name = "Recipe_Тесто дрожжевое (auto)"; r.hideFlags = HideFlags.DontSave;
            list.Add(YeastDough(r, water, flour, yeast, salt, dough));
        }

        /// <summary>
        /// ТЕСТО ДРОЖЖЕВОЕ (на 1 ведро воды = один шар теста; 2 ведра — всё вдвое, два шара):
        ///   вода 1 ведро + мука 8 мастерков + дрожжи 1 доза (баночка ПКМ) + соль 1 мастерок;
        ///   перемешать (бетономешалка крутится) 10 с → «Тесто дрожжевое».
        /// </summary>
        public static CookRecipeDef YeastDough(CookRecipeDef r, ProductDef water, ProductDef flour, ProductDef yeast, ProductDef salt, ProductDef dough)
        {
            r.Title = "Тесто дрожжевое"; r.Driver = CookDriver.Book;
            r.Base = water; r.BaseAmount = 1f; r.BaseUnit = CookUnit.Bucket;
            r.BaseFillsContainer = false; r.BaseMinFill = 0.9f;
            r.PerContainer = false; r.OnlyBaseBecomesResult = false;
            r.OrderMatters = false; r.OnlyThese = true; r.Tolerance = 0.25f;
            r.Ingredients.Clear();
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = flour, Amount = 8f, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = yeast, Amount = 1f, Unit = CookUnit.Dose });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = salt, Amount = 1f, Unit = CookUnit.Trowel });
            r.Steps.Clear();
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = 10f });
            r.Result = dough;
            return r;
        }

        // ───────────────────────── десерты (Запечённая Аляска) ─────────────────────────

        /// <summary>Названия продуктов десертов — одни и те же в книге, меню и проверке.</summary>
        public static class Dessert
        {
            public const string Egg = "Яйцо", White = "Яичный белок", Sugar = "Сахар", Flour = "Мука пшеничная",
                                Choc = "Шоколад", ChocPaste = "Шоколадная паста", Butter = "Сливочное масло",
                                EggFoam = "Яичная масса", ChocMass = "Шоколадная масса", Batter = "Бисквитное тесто",
                                Sponge = "Бисквит", Meringue = "Меренга", MeringueOver = "Меренга перебитая";
        }

        private static void BuildDessertDefaults(List<CookRecipeDef> list)
        {
            FoodDatabase db = Object.FindAnyObjectByType<FoodDatabase>();
            if (db == null || db.Table == null) return;
            ProductDef egg = FindExact(db, "яйцо"), white = FindExact(db, "яичный белок"), sugar = FindExact(db, "сахар");
            ProductDef flour = FindExact(db, "мука пшеничная"), choc = FindExact(db, "шоколад"), chocPaste = FindExact(db, "шоколадная паста");
            ProductDef butter = FindExact(db, "сливочное масло"), foam = FindExact(db, "яичная масса"), mass = FindExact(db, "шоколадная масса");
            ProductDef batter = FindExact(db, "бисквитное тесто"), mer = FindExact(db, "меренга"), merOver = FindExact(db, "меренга перебитая");
            if (egg != null && sugar != null && foam != null) AddAuto(list, "Яичная масса", r => EggFoam(r, egg, sugar, foam));
            if (choc != null && butter != null && mass != null) AddAuto(list, "Шоколадная масса", r => ChocolateMass(r, choc, butter, mass));
            if (chocPaste != null && butter != null && mass != null) AddAuto(list, "Шоколадная масса из пасты", r => ChocolateMass(r, chocPaste, butter, mass, "Шоколадная масса из пасты"));
            if (foam != null && (mass != null || chocPaste != null) && flour != null && batter != null) AddAuto(list, "Бисквитное тесто", r => SpongeBatter(r, foam, mass, flour, batter, chocPaste));
            if (white != null && sugar != null && mer != null) AddAuto(list, "Меренга", r => Meringue(r, white, sugar, mer));
            if (mer != null && merOver != null) AddAuto(list, "Меренга перебитая", r => MeringueOverwhipped(r, mer, merOver));
        }

        private static void AddAuto(List<CookRecipeDef> list, string title, System.Func<CookRecipeDef, CookRecipeDef> fill)
        {
            if (HasTitle(list, title)) return;
            CookRecipeDef r = ScriptableObject.CreateInstance<CookRecipeDef>();
            r.name = "Recipe_" + title + " (auto)"; r.hideFlags = HideFlags.DontSave;
            list.Add(fill(r));
        }

        private static CookRecipeDef Plain(CookRecipeDef r, string title, ProductDef baseP, float amount, CookUnit unit, float tolerance)
        {
            r.Title = title; r.Driver = CookDriver.Book;
            r.Base = baseP; r.BaseAmount = amount; r.BaseUnit = unit;
            r.BaseFillsContainer = false; r.BaseMinFill = 0.9f;
            r.PerContainer = false; r.OnlyBaseBecomesResult = false;
            r.OrderMatters = false; r.OnlyThese = true; r.Tolerance = tolerance;
            r.Ingredients.Clear(); r.Steps.Clear();
            return r;
        }

        /// <summary>
        /// ЯИЧНАЯ МАССА (основа бисквита): яйца 6 шт. + сахар 2 мастерка — взбить миксером 8 с →
        /// пышная светлая масса, объём ×1.5 (10 порций → 15 = полведра). 12 яиц — всё вдвое.
        /// </summary>
        public static CookRecipeDef EggFoam(CookRecipeDef r, ProductDef egg, ProductDef sugar, ProductDef foam)
        {
            Plain(r, "Яичная масса", egg, 6f, CookUnit.Piece, 0.2f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = sugar, Amount = 2f, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Whip, Seconds = 8f, VolumeK = 1.5f });
            r.Result = foam;
            return r;
        }

        /// <summary>
        /// ШОКОЛАДНАЯ МАССА: шоколад 3 мастерка + сливочное масло 1 мастерок — растопить на огне
        /// (от 40 °C) 6 с → жидкий шоколад с маслом (8 порций). Из пасты — так же.
        /// </summary>
        public static CookRecipeDef ChocolateMass(CookRecipeDef r, ProductDef choc, ProductDef butter, ProductDef mass, string title = "Шоколадная масса")
        {
            Plain(r, title, choc, 3f, CookUnit.Trowel, 0.3f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = butter, Amount = 1f, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Melt, Seconds = 6f });
            r.Result = mass;
            return r;
        }

        /// <summary>
        /// БИСКВИТНОЕ ТЕСТО (на одну опалубку = 1 ведро теста): яичная масса полведра + шоколад полведра
        /// (жидкая шоколадная паста из ковша — один цикл, 16 порций; или шоколадная масса из кастрюли)
        /// + мука 3 мастерка — перемешать 5 с (бетономешалка крутится или миксер) → 1 ведро жидкого теста
        /// (при смешивании пена оседает, ×0.83). Ведро яичной массы + ведро шоколада + 6 мастерков муки → 2 ведра.
        /// </summary>
        public static CookRecipeDef SpongeBatter(CookRecipeDef r, ProductDef foam, ProductDef mass, ProductDef flour, ProductDef batter, ProductDef paste = null)
        {
            Plain(r, "Бисквитное тесто", foam, 0.5f, CookUnit.Bucket, 0.3f);
            ProductDef choc = mass != null ? mass : paste;
            ProductDef alt = mass != null ? paste : null;
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = choc, Alt = alt, Amount = 0.5f, Unit = CookUnit.Bucket });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = flour, Amount = 3f, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = 5f, VolumeK = 0.83f });
            r.Result = batter;
            return r;
        }

        /// <summary>
        /// МЕРЕНГА: яичный белок 4 шт. + сахар 2 мастерка — взбить миксером 10 с → твёрдые пики,
        /// объём ×2.5 (8 порций → 20). Взбивать дальше — перебьётся (см. «Меренга перебитая»).
        /// </summary>
        public static CookRecipeDef Meringue(CookRecipeDef r, ProductDef white, ProductDef sugar, ProductDef mer)
        {
            Plain(r, "Меренга", white, 4f, CookUnit.Piece, 0.25f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = sugar, Amount = 2f, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Whip, Seconds = 10f, VolumeK = 2.5f });
            r.Result = mer;
            return r;
        }

        /// <summary>ПЕРЕБИЛ: готовую меренгу взбивать ещё 8 с — крупинки, оседает (объём ×0.6). Ошибка повара.</summary>
        public static CookRecipeDef MeringueOverwhipped(CookRecipeDef r, ProductDef mer, ProductDef over)
        {
            Plain(r, "Меренга перебитая", mer, 1f, CookUnit.Portion, 0f);
            r.IsMistake = true;
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Whip, Seconds = 8f, VolumeK = 0.6f });
            r.Result = over;
            return r;
        }

        // ───────────────────────── макарон «Солёная каска» ─────────────────────────

        /// <summary>Названия продуктов макарон — одни и те же в книге, инструментах, меню и проверке.</summary>
        public static class Macaron
        {
            public const string AlmondFlour = "Мука миндальная", AlmondMix = "Миндальная смесь", Cocoa = "Тёртое какао",
                                Batter = "Тесто для макарон", BatterOver = "Тесто для макарон перемешанное",
                                Caramel = "Карамель", CaramelBurnt = "Карамель горелая", SaltCaramel = "Солёная карамель",
                                Salt = "Соль", Cream = "Сливки", Ganache = "Карамельный ганаш",
                                Shell = "Скорлупка макарон", Dish = "Макарон «Солёная каска»";
        }

        private static void BuildMacaronDefaults(List<CookRecipeDef> list)
        {
            FoodDatabase db = Object.FindAnyObjectByType<FoodDatabase>();
            if (db == null || db.Table == null) return;
            ProductDef mer = FindExact(db, "меренга"), mix = FindExact(db, "миндальная смесь"), cocoa = FindExact(db, "тёртое какао") ?? FindExact(db, "тертое какао");
            ProductDef batter = FindExact(db, "тесто для макарон"), over = FindExact(db, "тесто для макарон перемешанное");
            ProductDef sugar = FindExact(db, "сахар"), caramel = FindExact(db, "карамель"), burnt = FindExact(db, "карамель горелая");
            ProductDef butter = FindExact(db, "сливочное масло"), salt = FindExact(db, "соль"), salted = FindExact(db, "солёная карамель") ?? FindExact(db, "соленая карамель");
            ProductDef mass = FindExact(db, "шоколадная масса"), ganache = FindExact(db, "карамельный ганаш");
            ProductDef paste = FindExact(db, "шоколадная паста");
            if (mer != null && mix != null && (cocoa != null || paste != null) && batter != null) AddAuto(list, "Тесто для макарон", r => MacaronBatter(r, mer, mix, cocoa, batter, paste));
            if (batter != null && over != null) AddAuto(list, "Тесто для макарон перемешанное", r => MacaronBatterOver(r, batter, over));
            if (sugar != null && caramel != null) AddAuto(list, "Карамель", r => Caramel(r, sugar, caramel));
            if (caramel != null && burnt != null) AddAuto(list, "Карамель горелая", r => CaramelBurnt(r, caramel, burnt));
            if (caramel != null && butter != null && salt != null && salted != null) AddAuto(list, "Солёная карамель", r => SaltedCaramel(r, caramel, butter, salt, salted));
            if (mass != null && salted != null && ganache != null) AddAuto(list, "Карамельный ганаш", r => CaramelGanache(r, mass, salted, ganache));
        }

        /// <summary>
        /// ТЕСТО ДЛЯ МАКАРОН (макаронаж): меренга 10 порций (полведра) + миндальная смесь 4 мастерка (из грохота)
        /// + тёртое какао 1 мастерок (или 1 мастерок шоколадной пасты из ковша) — перемешать миксером 5 с → густая блестящая «лента» (объём ×0.8 — меренга
        /// оседает). ~16 порций = 16 скорлупок. Мешать дальше — перемешается (растечётся блинами).
        /// </summary>
        public static CookRecipeDef MacaronBatter(CookRecipeDef r, ProductDef meringue, ProductDef mix, ProductDef cocoa, ProductDef batter, ProductDef paste = null)
        {
            Plain(r, "Тесто для макарон", meringue, 10f, CookUnit.Portion, 0.3f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = mix, Amount = 4f, Unit = CookUnit.Trowel });
            ProductDef choc = cocoa != null ? cocoa : paste;
            ProductDef alt = cocoa != null ? paste : null;
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = choc, Alt = alt, Amount = 1f, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = 5f, VolumeK = 0.8f });
            r.Result = batter;
            return r;
        }

        /// <summary>ПЕРЕМЕШАЛ: готовое тесто мешать ещё 4 с — жидкое, скорлупки растекутся блинами. Ошибка повара.</summary>
        public static CookRecipeDef MacaronBatterOver(CookRecipeDef r, ProductDef batter, ProductDef over)
        {
            Plain(r, "Тесто для макарон перемешанное", batter, 1f, CookUnit.Portion, 0f);
            r.IsMistake = true;
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = 4f, VolumeK = 0.9f });
            r.Result = over;
            return r;
        }

        /// <summary>
        /// КАРАМЕЛЬ: сахар 3 мастерка в сухой кастрюле на огонь — с 160 °C карамелизуется 4 с → янтарная карамель.
        /// Костёр без воздуха даёт ~140 °C — РАЗДУЙ. Готово — сними с огня: от 200 °C через 5 с пригорит.
        /// </summary>
        public static CookRecipeDef Caramel(CookRecipeDef r, ProductDef sugar, ProductDef caramel)
        {
            Plain(r, "Карамель", sugar, 3f, CookUnit.Trowel, 0.34f);
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Caramelize, Seconds = 4f });
            r.Result = caramel;
            return r;
        }

        /// <summary>ПРИГОРЕЛА: карамель держали от 200 °C 5 с — горькая, чёрная. Ошибка повара.</summary>
        public static CookRecipeDef CaramelBurnt(CookRecipeDef r, ProductDef caramel, ProductDef burnt)
        {
            Plain(r, "Карамель горелая", caramel, 1f, CookUnit.Portion, 0f);
            r.IsMistake = true;
            r.OnlyThese = false;
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Scorch, Seconds = 5f });
            r.Result = burnt;
            return r;
        }

        /// <summary>
        /// СОЛЁНАЯ КАРАМЕЛЬ: карамель (6 порций, сняли с огня) + сливочное масло 1 мастерок + соль 1 мастерок —
        /// перемешать миксером 4 с → тягучая солёная карамель.
        /// </summary>
        public static CookRecipeDef SaltedCaramel(CookRecipeDef r, ProductDef caramel, ProductDef butter, ProductDef salt, ProductDef salted)
        {
            Plain(r, "Солёная карамель", caramel, 6f, CookUnit.Portion, 0.34f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = butter, Amount = 1f, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = salt, Amount = 1f, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = 4f });
            r.Result = salted;
            return r;
        }

        /// <summary>
        /// КАРАМЕЛЬНЫЙ ГАНАШ (начинка): шоколадная масса 4 мастерка + солёная карамель 2 мастерка —
        /// перемешать миксером 4 с → густой ганаш (~12 порций = начинка на 12 макарон). Набирается шприцем.
        /// </summary>
        public static CookRecipeDef CaramelGanache(CookRecipeDef r, ProductDef mass, ProductDef salted, ProductDef ganache)
        {
            Plain(r, "Карамельный ганаш", mass, 4f, CookUnit.Trowel, 0.3f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = salted, Amount = 2f, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = 4f });
            r.Result = ganache;
            return r;
        }

        // ───────────────────────── рёбра барбекю ─────────────────────────

        // ───────────────────────── ньокки: соус ─────────────────────────

        /// <summary>Названия продуктов ньокков — одни и те же в книге, тарелке и меню.</summary>
        public static class Gnocchi
        {
            public const string Butter = "Сливочное масло", Sage = "Шалфей", SageButter = "Масло с шалфеем",
                                BurntButter = "Масло горелое", Cheese = "Сыр твёрдый", Dish = "Ньокки с маслом и шалфеем";
        }

        private static void BuildGnocchiDefaults(List<CookRecipeDef> list)
        {
            FoodDatabase db = Object.FindAnyObjectByType<FoodDatabase>();
            if (db == null || db.Table == null) return;
            ProductDef butter = FindExact(db, "сливочное масло"), sage = FindExact(db, "шалфей");
            ProductDef sauce = FindExact(db, "масло с шалфеем"), burnt = FindExact(db, "масло горелое");
            if (butter != null && sage != null && sauce != null) AddAuto(list, Gnocchi.SageButter, r => SageButter(r, butter, sage, sauce));
            if (sauce != null && burnt != null) AddAuto(list, Gnocchi.BurntButter, r => SageButterBurnt(r, sauce, burnt));
        }

        /// <summary>
        /// МАСЛО С ШАЛФЕЕМ (соус к ньокки): сливочное масло 2 мастерка (своё — из сливок вибратором) + шалфей 2 дозы (пучок — рвать над ковшом) —
        /// в ковше/сковороде на огне растопить 8 с (от 40 °C) → золотистое ореховое масло с хрустящими листьями.
        /// </summary>
        public static CookRecipeDef SageButter(CookRecipeDef r, ProductDef butter, ProductDef sage, ProductDef sauce)
        {
            Plain(r, Gnocchi.SageButter, butter, 2f, CookUnit.Trowel, 0.5f);      // ±50%: масла 1–3 мастерка, шалфея 1–3 дозы
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = sage, Amount = 2f, Unit = CookUnit.Dose });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Melt, Seconds = 8f });
            r.Result = sauce;
            return r;
        }

        /// <summary>ПРИГОРЕЛО: масло с шалфеем держали от 200 °C 6 с — чёрное, горькое. Ошибка повара.</summary>
        public static CookRecipeDef SageButterBurnt(CookRecipeDef r, ProductDef sauce, ProductDef burnt)
        {
            Plain(r, Gnocchi.BurntButter, sauce, 1f, CookUnit.Portion, 0f);
            r.IsMistake = true;
            r.OnlyThese = false;
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Scorch, Seconds = 6f });
            r.Result = burnt;
            return r;
        }

        // ───────────────────────── пельмени ─────────────────────────

        /// <summary>Названия продуктов пельменей — одни и те же в книге, меню и проверке.</summary>
        public static class Pelmeni
        {
            public const string Dough = "Тесто для пельменей", Water = "Вода", Flour = "Мука пшеничная", Egg = "Яйцо", Salt = "Соль";
            public const string Filling = "Фарш для пельменей", Mince = "Говяжий фарш", PorkMince = "Свиной фарш", Onion = "Лук репчатый",
                                Dill = "Укроп", Pepper = "Перец чёрный";
            /// <summary>Сколько порций теста даёт одна партия (1 ведро воды): (30 + 24 + 2 + 2) × 0.55.</summary>
            public const int DoughPerBatch = 32;
        }

        // ───────────────────────── борщ ─────────────────────────

        /// <summary>Названия продуктов борща — одни и те же в книге, меню и проверке.</summary>
        public static class Borsch
        {
            public const string Broth = "Бульон для борща", Water = "Вода", Bones = "Кости для бульона", Shank = "Говяжья голяшка",
                                Onion = "Лук репчатый", OnionRed = "Лук красный", Carrot = "Морковь", Bay = "Лавровый лист",
                                Beet = "Свёкла", Cabbage = "Капуста белокочанная", Oil = "Масло подсолнечное", Lard = "Сало";
            public const string Tomato = "Помидор", Paste = "Томатная паста", PasteBurnt = "Томатная паста пригоревшая",
                                Saute = "Пассеровка", BeetStewed = "Свёкла тушёная", Zazharka = "Зажарка для борща",
                                Vinegar = "Уксус", AppleVinegar = "Яблочный уксус";
            public const string Soup = "Борщ", Potato = "Картофель", Salt = "Соль";
            public const string Garlic = "Зубчик чеснока", GarlicDry = "Чеснок сушёный", Dill = "Укроп", GarlicOil = "Масло чесночное с зеленью",
                                Zatirka = "Затирка", Bun = "Пампушка", BunGarlic = "Пампушка с чесноком", SourCream = "Сметана";
            /// <summary>Шаг 5: сборка борща — на кастрюлю бульона.</summary>
            public const int SoupPotato = 6, SoupCabbage = 12, SoupZazharka = 4;   // капуста — кочан целиком (12 порц.), ±50%
            public const float SoupSeconds = 40f;
            /// <summary>Шаг 4: время шагов, с.</summary>
            public const float PasteSeconds = 60f, SauteSeconds = 20f, BeetSeconds = 30f, ZazharkaSeconds = 20f, BurnSeconds = 6f;
            /// <summary>Сколько всего на кастрюлю (PerContainer): кости, голяшка, лук, морковь, лавр (дозы).</summary>
            public const int BonesN = 4, ShankN = 4, OnionN = 2, CarrotN = 2, BayDoses = 2;   // с 2 ног: 6 шайб + 2 кости — сходится (±50%: кости 2–6, голяшка 2–6)
            public const float BoilSeconds = 90f;
        }

        private static void BuildBorschDefaults(List<CookRecipeDef> list)
        {
            FoodDatabase db = Object.FindAnyObjectByType<FoodDatabase>();
            if (db == null || db.Table == null) return;
            BuildBorschStep4(list, db);
            BuildBorschStep5(list, db);
            BuildBorschStep6(list, db);
            ProductDef broth = FindExact(db, "бульон для борща");
            if (broth == null) return;                                   // меню Борщ → 2 ещё не нажимали
            ProductDef water = FindExact(db, "вода"), bones = FindExact(db, "кости для бульона");
            ProductDef shank = FindExact(db, "говяжья голяшка"), onion = FindExact(db, "лук репчатый") ?? Find(db, "лук репч");
            ProductDef onionRed = FindExact(db, "лук красный") ?? Find(db, "лук красн"), carrot = FindExact(db, "морковь");
            ProductDef bay = FindExact(db, "лавровый лист");
            if (water != null && bones != null && shank != null && onion != null && carrot != null && bay != null)
                AddAuto(list, Borsch.Broth, r => BorschBroth(r, water, bones, shank, onion, onionRed, carrot, bay, broth));
            else if (Log)
                Debug.LogWarning("[Рецепты] «Бульон для борща» не собран — нет продукта: " + (water == null ? "Вода " : "") + (bones == null ? "Кости для бульона " : "") +
                                 (shank == null ? "Говяжья голяшка " : "") + (onion == null ? "Лук репчатый " : "") + (carrot == null ? "Морковь " : "") + (bay == null ? "Лавровый лист" : ""));
        }

        // ───────────────────────── КОТЛЕТЫ ─────────────────────────

        public static class Cutlets
        {
            public const string Crumb = "Мякиш", BreadMass = "Хлебная масса", Mince = "Фарш для котлет", Pounded = "Фарш отбитый",
                                Crumbs = "Панировочные сухари", BeefMince = "Говяжий фарш", PorkMince = "Свиной фарш", Patty = "Котлета";
            // всё мастерками (±50%); молоко — наливать, лишнее впитается
            /// <summary>1. Хлебная масса: мякиш 2 + молоко от 1 — размокнуть 8 с → отбойником 5 с.</summary>
            public const float CrumbN = 2f, MilkN = 1f, SoakSeconds = 8f, GrindSeconds = 5f;
            /// <summary>2. Фарш для котлет: говяжий 2 + свиной 2 + хлебная масса 1 + лук тёртый 1 + соль 1 + перец 1 — миксером 6 с.</summary>
            public const float BeefN = 2f, PorkN = 2f, MassN = 1f, OnionN = 1f, SaltN = 1f, PepperN = 1f, MixSeconds = 6f;
            /// <summary>3. Отбить: кувалдой по таре — 8 ударов (1 удар = 1 с работы).</summary>
            public const float PoundBase = 2f, PoundSeconds = 8f;
        }

        private static void BuildCutletDefaults(List<CookRecipeDef> list)
        {
            FoodDatabase db = Object.FindAnyObjectByType<FoodDatabase>();
            if (db == null || db.Table == null) return;
            ProductDef mince = FindExact(db, "фарш для котлет");
            if (mince == null) return;                                       // меню Котлеты → 1 ещё не нажимали
            ProductDef crumb = FindExact(db, "мякиш"), mass = FindExact(db, "хлебная масса"), pounded = FindExact(db, "фарш отбитый");
            ProductDef milk = FindExact(db, "молоко"), beef = FindExact(db, "говяжий фарш"), pork = FindExact(db, "свиной фарш");
            ProductDef onion = FindExact(db, "лук репчатый") ?? Find(db, "лук репч"), salt = FindExact(db, "соль");
            ProductDef pepper = FindExact(db, "перец чёрный") ?? FindExact(db, "перец черный") ?? Find(db, "перец");
            if (crumb != null && milk != null && mass != null) AddAuto(list, Cutlets.BreadMass, r => CutBreadMass(r, crumb, milk, mass));
            if (beef != null && mass != null && onion != null && salt != null && pepper != null)
                AddAuto(list, Cutlets.Mince, r => CutMince(r, beef, pork, mass, onion, salt, pepper, mince));
            if (pounded != null) AddAuto(list, Cutlets.Pounded, r => CutPound(r, mince, pounded));
            if (Log && (crumb == null || milk == null || beef == null || onion == null || salt == null || pepper == null))
                Debug.LogWarning("[Рецепты] котлеты — собраны не все, нет продукта: " + (crumb == null ? "Мякиш " : "") + (milk == null ? "Молоко " : "") +
                                 (beef == null ? "Говяжий фарш " : "") + (onion == null ? "Лук репчатый " : "") + (salt == null ? "Соль " : "") + (pepper == null ? "Перец чёрный" : ""));
        }

        /// <summary>1. ХЛЕБНАЯ МАССА — ведро/банка: мякиш (целыми батонами без корки) 2 мастерка + молоко налить (от 1) → размокнуть 8 с (лишнее молоко впитается) → отбойником 5 с.</summary>
        public static CookRecipeDef CutBreadMass(CookRecipeDef r, ProductDef crumb, ProductDef milk, ProductDef result)
        {
            Plain(r, Cutlets.BreadMass, crumb, Cutlets.CrumbN, CookUnit.Trowel, 0.5f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = milk, Amount = Cutlets.MilkN, Unit = CookUnit.Trowel, Reduces = true });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Rest, Seconds = Cutlets.SoakSeconds });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Grind, Seconds = Cutlets.GrindSeconds, VolumeK = 0.8f });
            r.Result = result;
            return r;
        }

        /// <summary>2. ФАРШ ДЛЯ КОТЛЕТ — ведро/банка: говяжий фарш 2 + свиной 2 (нет свиного — говяжий 4) + хлебная масса 1 + лук тёртый 1 + соль 1 + перец 1 → миксером 6 с.</summary>
        public static CookRecipeDef CutMince(CookRecipeDef r, ProductDef beef, ProductDef pork, ProductDef mass, ProductDef onion, ProductDef salt, ProductDef pepper, ProductDef result)
        {
            Plain(r, Cutlets.Mince, beef, pork != null ? Cutlets.BeefN : Cutlets.BeefN + Cutlets.PorkN, CookUnit.Trowel, 0.5f);
            if (pork != null) r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = pork, Amount = Cutlets.PorkN, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = mass, Amount = Cutlets.MassN, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = onion, Amount = Cutlets.OnionN, Unit = CookUnit.Trowel, State = FoodState.Grated });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = salt, Amount = Cutlets.SaltN, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = pepper, Amount = Cutlets.PepperN, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = Cutlets.MixSeconds });
            r.Result = result;
            return r;
        }

        /// <summary>3. ФАРШ ОТБИТЫЙ — в той же таре: кувалдой по ведру/банке 8 ударов → плотный, гладкий, липкий (×0.9).</summary>
        public static CookRecipeDef CutPound(CookRecipeDef r, ProductDef mince, ProductDef result)
        {
            Plain(r, Cutlets.Pounded, mince, Cutlets.PoundBase, CookUnit.Trowel, 0.5f);
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Pound, Seconds = Cutlets.PoundSeconds, VolumeK = 0.9f });
            r.Result = result;
            return r;
        }

        // ───────────────────────── БЕФСТРОГАНОВ ─────────────────────────

        public static class Stroganoff
        {
            public const string Strips = "Говядина соломкой", BeefFried = "Говядина обжаренная", BeefOnion = "Мясо с луком",
                                Dish = "Бефстроганов", Curdled = "Бефстроганов (сметана свернулась)";
            // всё мастерками, допуск ±50%; жидкое — наливать (лишнее уварится)
            /// <summary>1. Мясо: соломка 4 + масло подсолнечное 1 + соль 1 + перец 1 — жарить 15 с на сильном огне.</summary>
            public const float BeefN = 4f, OilN = 1f, SaltN = 1f, PepperN = 1f, FrySeconds = 15f;
            /// <summary>2. Лук: к мясу — лук тёртый 3 (1 луковица) + сливочное масло 1 + мука 1 — жарить 15 с.</summary>
            public const float FriedN = 3f, OnionN = 3f, ButterN = 1f, FlourN = 1f, OnionSeconds = 15f;
            /// <summary>3. Соус: + сметана 2 + томатная паста 1 + вода/бульон налить (от 2) — томить без раздува 20 с.</summary>
            public const float MixN = 7f, CreamN = 2f, PasteN = 1f, WaterN = 2f, SimmerSeconds = 20f, CurdleSeconds = 6f;
        }

        private static void BuildStroganoffDefaults(List<CookRecipeDef> list)
        {
            FoodDatabase db = Object.FindAnyObjectByType<FoodDatabase>();
            if (db == null || db.Table == null) return;
            ProductDef dish = FindExact(db, "бефстроганов");
            if (dish == null) return;                                       // меню Бефстроганов → 1 ещё не нажимали
            ProductDef strips = FindExact(db, "говядина соломкой"), fried = FindExact(db, "говядина обжаренная"), mix = FindExact(db, "мясо с луком");
            ProductDef curd = FindExact(db, "бефстроганов (сметана свернулась)");
            ProductDef oil = FindExact(db, "масло подсолнечное"), butter = FindExact(db, "сливочное масло"), salt = FindExact(db, "соль");
            ProductDef pepper = FindExact(db, "перец чёрный") ?? FindExact(db, "перец черный") ?? Find(db, "перец");
            ProductDef onion = FindExact(db, "лук репчатый") ?? Find(db, "лук репч"), flour = FindExact(db, "мука пшеничная") ?? Find(db, "мука");
            ProductDef cream = FindExact(db, "сметана"), paste = FindExact(db, "томатная паста");
            ProductDef water = FindExact(db, "вода"), broth = FindExact(db, "бульон для борща") ?? FindExact(db, "бульон");
            if (water == null) { water = broth; broth = null; }
            if (strips != null && fried != null && oil != null && salt != null && pepper != null)
                AddAuto(list, Stroganoff.BeefFried, r => StrogBeef(r, strips, oil, butter, salt, pepper, fried));
            if (fried != null && onion != null && butter != null && flour != null && mix != null)
                AddAuto(list, Stroganoff.BeefOnion, r => StrogOnion(r, fried, onion, butter, flour, mix));
            if (mix != null && cream != null && paste != null && water != null)
            {
                AddAuto(list, Stroganoff.Dish, r => StrogSauce(r, mix, cream, paste, water, broth, dish, false));
                if (curd != null) AddAuto(list, Stroganoff.Curdled, r => StrogSauce(r, mix, cream, paste, water, broth, curd, true));
            }
            if (Log && (strips == null || oil == null || salt == null || pepper == null || onion == null || butter == null || flour == null || cream == null || paste == null || water == null))
                Debug.LogWarning("[Рецепты] бефстроганов — собран не весь, нет продукта: " + (strips == null ? "Говядина соломкой " : "") + (oil == null ? "Масло подсолнечное " : "") +
                                 (salt == null ? "Соль " : "") + (pepper == null ? "Перец чёрный " : "") + (onion == null ? "Лук репчатый " : "") + (butter == null ? "Сливочное масло " : "") +
                                 (flour == null ? "Мука пшеничная " : "") + (cream == null ? "Сметана " : "") + (paste == null ? "Томатная паста " : "") + (water == null ? "Вода" : ""));
        }

        /// <summary>1. ГОВЯДИНА ОБЖАРЕННАЯ — сковорода, сильный огонь: соломка 4 + масло подсолнечное 1 (или сливочное) + соль 1 + перец 1 → жарить 15 с (×0.85).</summary>
        public static CookRecipeDef StrogBeef(CookRecipeDef r, ProductDef strips, ProductDef oil, ProductDef butter, ProductDef salt, ProductDef pepper, ProductDef result)
        {
            Plain(r, Stroganoff.BeefFried, strips, Stroganoff.BeefN, CookUnit.Trowel, 0.5f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = oil, Alt = butter, Amount = Stroganoff.OilN, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = salt, Amount = Stroganoff.SaltN, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = pepper, Amount = Stroganoff.PepperN, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Fry, Seconds = Stroganoff.FrySeconds, VolumeK = 0.85f });
            r.Result = result;
            return r;
        }

        /// <summary>2. МЯСО С ЛУКОМ — в ту же сковороду к мясу: лук тёртый 3 + сливочное масло 1 + мука 1 → жарить 15 с.</summary>
        public static CookRecipeDef StrogOnion(CookRecipeDef r, ProductDef fried, ProductDef onion, ProductDef butter, ProductDef flour, ProductDef result)
        {
            Plain(r, Stroganoff.BeefOnion, fried, Stroganoff.FriedN, CookUnit.Trowel, 0.5f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = onion, Amount = Stroganoff.OnionN, Unit = CookUnit.Trowel, State = FoodState.Grated });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = butter, Amount = Stroganoff.ButterN, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = flour, Amount = Stroganoff.FlourN, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Fry, Seconds = Stroganoff.OnionSeconds, VolumeK = 0.9f });
            r.Result = result;
            return r;
        }

        /// <summary>
        /// 3. БЕФСТРОГАНОВ — там же: мясо с луком (всё, ~7 мастерков) + сметана 2 + томатная паста 1 + вода или бульон НАЛИТЬ (от 2, лишнее уварится)
        ///   → огонь БЕЗ раздува: томить 20 с → «Бефстроганов». Раздул — сметана сворачивается (ошибка).
        /// </summary>
        public static CookRecipeDef StrogSauce(CookRecipeDef r, ProductDef mix, ProductDef cream, ProductDef paste, ProductDef water, ProductDef broth, ProductDef result, bool curdled)
        {
            Plain(r, curdled ? Stroganoff.Curdled : Stroganoff.Dish, mix, Stroganoff.MixN, CookUnit.Trowel, 0.5f);
            r.IsMistake = curdled;
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = cream, Amount = Stroganoff.CreamN, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = paste, Amount = Stroganoff.PasteN, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = water, Alt = broth, Amount = Stroganoff.WaterN, Unit = CookUnit.Trowel, Reduces = true });
            r.Steps.Add(new CookRecipeDef.Step { Kind = curdled ? CookStepKind.Burn : CookStepKind.Simmer, Seconds = curdled ? Stroganoff.CurdleSeconds : Stroganoff.SimmerSeconds, VolumeK = 0.9f });
            r.Result = result;
            return r;
        }

        // ───────────────────────── ТЕЛЯТИНА ПО-ОРЛОВСКИ ─────────────────────────

        public static class Orloff
        {
            public const string Mushroom = "Шампиньоны", Thyme = "Тимьян", Nutmeg = "Мускатный орех", VealLoin = "Телячья корейка",
                                Duxelles = "Дюксель", OnionSweated = "Лук томлёный", OnionBrowned = "Лук зарумяненный", Soubise = "Субиз",
                                Butter = "Сливочное масло", Cream = "Сливки", Cheese = "Сыр твёрдый";
            // ВСЁ В МАСТЕРКАХ (1 мастерок = 2 порции). Допуск ±50%: «1 мастерок» = от полмастерка до полутора.
            /// <summary>Дюксель: грибы рубленые 3 мастерка (2 шампиньона) + лук тёртый 1 + масло 1 + соль 1 + тимьян 1.</summary>
            public const float DuxMushroom = 3f, DuxOnion = 1f, DuxButter = 1f, DuxSalt = 1f, DuxThyme = 1f;
            public const float DuxSeconds = 25f;
            /// <summary>Субиз: лук тёртый 3 мастерка (1 луковица) + масло 1 — томить 30 с; потом лук томлёный 2 + сливки 1 — миксером 6 с.</summary>
            public const float SoubOnion = 3f, SoubButter = 1f, SoubSweated = 2f, SoubCream = 1f;
            public const float SweatSeconds = 30f, SoubReduceSeconds = 8f, SoubMixSeconds = 6f, BrownSeconds = 6f;
            /// <summary>Шаг 2. Ру: масло 1 + мука 1 мастерок — жарить 8 с. Бешамель: ру 2 + молоко от 3 (лишнее уварится) — варить помешивая 20 с.
            /// Морне: бешамель 3 + сыр тёртый 1 + мускат 1 — миксером 4 с.</summary>
            public const string Roux = "Ру", Bechamel = "Бешамель", BechamelLumpy = "Бешамель с комками", Mornay = "Соус морне",
                                Flour = "Мука пшеничная", Milk = "Молоко";
            public const float RouxButter = 1f, RouxFlour = 1f, RouxSeconds = 8f;
            public const float BechRoux = 2f, BechMilk = 3f, BechSeconds = 20f;
            public const float MornBech = 3f, MornCheese = 1f, MornNutmeg = 1f, MornSeconds = 4f;
            /// <summary>Шаг 3. Корейку: обжарить в сковороде ~20 с → тушить в кастрюле (бульон, под крышкой) ~60 с → отдохнуть 10 с → «Телятина тушёная».</summary>
            public const string VealStewed = "Телятина тушёная";
            public const float SearSeconds = 20f, BraiseSeconds = 60f, RestSeconds = 10f;
        }

        private static void BuildOrloffDefaults(List<CookRecipeDef> list)
        {
            FoodDatabase db = Object.FindAnyObjectByType<FoodDatabase>();
            if (db == null || db.Table == null) return;
            ProductDef dux = FindExact(db, "дюксель");
            if (dux == null) return;                                       // меню Орлов → 1 ещё не нажимали
            ProductDef mush = FindExact(db, "шампиньоны"), onion = FindExact(db, "лук репчатый") ?? Find(db, "лук репч");
            ProductDef butter = FindExact(db, "сливочное масло"), oil = FindExact(db, "масло подсолнечное");
            ProductDef salt = FindExact(db, "соль"), thyme = FindExact(db, "тимьян");
            ProductDef sweat = FindExact(db, "лук томлёный"), brown = FindExact(db, "лук зарумяненный"), soub = FindExact(db, "субиз");
            ProductDef cream = FindExact(db, "сливки") ?? Find(db, "сливки");
            if (mush != null && onion != null && butter != null && salt != null && thyme != null)
                AddAuto(list, Orloff.Duxelles, r => Duxelles(r, mush, onion, butter, oil, salt, thyme, dux));
            if (onion != null && butter != null && sweat != null)
            {
                AddAuto(list, Orloff.OnionSweated, r => OnionSweated(r, onion, butter, sweat));
                if (brown != null) AddAuto(list, Orloff.OnionBrowned, r => OnionBrowned(r, onion, butter, brown));
            }
            if (sweat != null && cream != null && soub != null)
                AddAuto(list, Orloff.Soubise, r => Soubise(r, sweat, cream, soub));
            // шаг 2: ру → бешамель → морне
            ProductDef roux = FindExact(db, "ру"), bech = FindExact(db, "бешамель"), morn = FindExact(db, "соус морне");
            ProductDef flour = FindExact(db, "мука пшеничная") ?? Find(db, "мука"), milk = FindExact(db, "молоко");
            ProductDef cheese = FindExact(db, "сыр твёрдый") ?? Find(db, "сыр"), nut = FindExact(db, "мускатный орех");
            if (butter != null && flour != null && roux != null) AddAuto(list, Orloff.Roux, r => Roux(r, butter, flour, roux));
            if (roux != null && milk != null && bech != null) AddAuto(list, Orloff.Bechamel, r => Bechamel(r, roux, milk, bech));
            if (bech != null && cheese != null && nut != null && morn != null) AddAuto(list, Orloff.Mornay, r => Mornay(r, bech, cheese, nut, morn));
            if (Log && roux != null && (flour == null || milk == null || cheese == null || nut == null))
                Debug.LogWarning("[Рецепты] соус морне — собран не весь, нет продукта: " + (flour == null ? "Мука пшеничная " : "") + (milk == null ? "Молоко " : "") +
                                 (cheese == null ? "Сыр твёрдый " : "") + (nut == null ? "Мускатный орех" : ""));
            if (Log && (mush == null || onion == null || butter == null || salt == null || thyme == null || cream == null))
                Debug.LogWarning("[Рецепты] телятина по-орловски — собраны не все полуфабрикаты, нет продукта: " + (mush == null ? "Шампиньоны " : "") + (onion == null ? "Лук репчатый " : "") +
                                 (butter == null ? "Сливочное масло " : "") + (salt == null ? "Соль " : "") + (thyme == null ? "Тимьян " : "") + (cream == null ? "Сливки" : ""));
        }

        /// <summary>
        /// ДЮКСЕЛЬ — в сковороде, всё мастерками: шампиньоны РУБЛЕНЫЕ (мытые → измельчитель) 3 мастерка (2 гриба)
        ///   + лук ТЁРТЫЙ 1 + сливочное масло 1 (или масло подсолнечное) + соль 1 + тимьян 1
        ///   → на огонь, ЖАРИТЬ 25 с, пока не уйдёт влага → «Дюксель» (×0.6). Допуск ±50%.
        /// </summary>
        public static CookRecipeDef Duxelles(CookRecipeDef r, ProductDef mush, ProductDef onion, ProductDef butter, ProductDef oil, ProductDef salt, ProductDef thyme, ProductDef result)
        {
            Plain(r, Orloff.Duxelles, mush, Orloff.DuxMushroom, CookUnit.Trowel, 0.5f);
            r.BaseState = FoodState.Grated;
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = onion, Amount = Orloff.DuxOnion, Unit = CookUnit.Trowel, State = FoodState.Grated });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = butter, Alt = oil, Amount = Orloff.DuxButter, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = salt, Amount = Orloff.DuxSalt, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = thyme, Amount = Orloff.DuxThyme, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Fry, Seconds = Orloff.DuxSeconds, VolumeK = 0.6f });
            r.Result = result;
            return r;
        }

        /// <summary>
        /// ЛУК ТОМЛЁНЫЙ (субиз, этап 1) — сковорода или кастрюля: лук ТЁРТЫЙ 3 мастерка (1 луковица) + сливочное масло 1 мастерок →
        ///   на СЛАБОМ огне (костёр без раздува, до ~175 °C) томить 30 с, НЕ румяня → «Лук томлёный» (×0.8).
        /// Раздул огонь — лук румянится: «Лук зарумяненный» (ошибка: субиз должен быть белым).
        /// </summary>
        public static CookRecipeDef OnionSweated(CookRecipeDef r, ProductDef onion, ProductDef butter, ProductDef result)
        {
            Plain(r, Orloff.OnionSweated, onion, Orloff.SoubOnion, CookUnit.Trowel, 0.5f);
            r.BaseState = FoodState.Grated;
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = butter, Amount = Orloff.SoubButter, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Simmer, Seconds = Orloff.SweatSeconds, VolumeK = 0.8f });
            r.Result = result;
            return r;
        }

        /// <summary>Ошибка повара: лук с маслом на сильном огне 6 с — зарумянился (для субиза не годится).</summary>
        public static CookRecipeDef OnionBrowned(CookRecipeDef r, ProductDef onion, ProductDef butter, ProductDef result)
        {
            Plain(r, Orloff.OnionBrowned, onion, Orloff.SoubOnion, CookUnit.Trowel, 0.5f);
            r.BaseState = FoodState.Grated;
            r.IsMistake = true;
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = butter, Amount = Orloff.SoubButter, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Burn, Seconds = Orloff.BrownSeconds, VolumeK = 0.8f });
            r.Result = result;
            return r;
        }

        /// <summary>
        /// СУБИЗ (луковое пюре, белое): лук томлёный 2 мастерка (всё из этапа 1, ~2.5) + сливки — НАЛИТЬ (от 1 мастерка-объёма; больше — можно)
        ///   → на СЛАБЫЙ огонь: лишние сливки выкипают до нормы, потом уваривать 8 с → МИКСЕРОМ 6 с → «Субиз».
        /// </summary>
        public static CookRecipeDef Soubise(CookRecipeDef r, ProductDef sweat, ProductDef cream, ProductDef result)
        {
            Plain(r, Orloff.Soubise, sweat, Orloff.SoubSweated, CookUnit.Trowel, 0.5f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = cream, Amount = Orloff.SoubCream, Unit = CookUnit.Trowel, Reduces = true });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Simmer, Seconds = Orloff.SoubReduceSeconds });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = Orloff.SoubMixSeconds });
            r.Result = result;
            return r;
        }

        /// <summary>РУ (основа соуса) — в сковороде: сливочное масло 1 мастерок + мука 1 мастерок → жарить 8 с → «Ру».</summary>
        public static CookRecipeDef Roux(CookRecipeDef r, ProductDef butter, ProductDef flour, ProductDef result)
        {
            Plain(r, Orloff.Roux, butter, Orloff.RouxButter, CookUnit.Trowel, 0.5f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = flour, Amount = Orloff.RouxFlour, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Fry, Seconds = Orloff.RouxSeconds });
            r.Result = result;
            return r;
        }

        /// <summary>
        /// БЕШАМЕЛЬ — в той же сковороде: ру 2 мастерка (всё) + молоко НАЛИТЬ (от 3 мастерков-объёма; больше — можно, уварится)
        ///   → на огне ВАРИТЬ ПОМЕШИВАЯ 20 с: миксером в таре хотя бы раз в 4 с. Не мешали ещё 6 с — «Бешамель с комками» (ошибка). ×0.8.
        /// </summary>
        public static CookRecipeDef Bechamel(CookRecipeDef r, ProductDef roux, ProductDef milk, ProductDef result)
        {
            Plain(r, Orloff.Bechamel, roux, Orloff.BechRoux, CookUnit.Trowel, 0.5f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = milk, Amount = Orloff.BechMilk, Unit = CookUnit.Trowel, Reduces = true });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.SimmerStir, Seconds = Orloff.BechSeconds, VolumeK = 0.8f });
            r.Result = result;
            return r;
        }

        /// <summary>СОУС МОРНЕ: бешамель 3 мастерка + сыр тёртый 1 + мускатный орех 1 → миксером 4 с → «Соус морне». Можно снять с огня.</summary>
        public static CookRecipeDef Mornay(CookRecipeDef r, ProductDef bech, ProductDef cheese, ProductDef nut, ProductDef result)
        {
            Plain(r, Orloff.Mornay, bech, Orloff.MornBech, CookUnit.Trowel, 0.5f);
            r.OnlyThese = true;
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = cheese, Amount = Orloff.MornCheese, Unit = CookUnit.Trowel, State = FoodState.Grated });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = nut, Amount = Orloff.MornNutmeg, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = Orloff.MornSeconds });
            r.Result = result;
            return r;
        }

        /// <summary>Помидор: «Помидор», «Помидоры», «Томат»… — но не паста.</summary>
        private static ProductDef FindTomato(FoodDatabase db)
        {
            ProductDef t = FindExact(db, "помидор");
            if (t != null) return t;
            for (int id = 1; id < 2048; id++)
            {
                ProductDef p = db.Table.GetProduct((ushort)id);
                if (p == null || string.IsNullOrEmpty(p.Title)) continue;
                string s = p.Title.ToLowerInvariant();
                if ((s.Contains("помидор") || s.Contains("томат")) && !s.Contains("паст") && !s.Contains("соус") && !s.Contains("сок")) return p;
            }
            return null;
        }

        /// <summary>Шаг 6: масло чесночное с зеленью (для пампушек) и затирка.</summary>
        private static void BuildBorschStep6(List<CookRecipeDef> list, FoodDatabase db)
        {
            ProductDef oil = FindExact(db, "масло подсолнечное"), clove = FindExact(db, "зубчик чеснока") ?? Find(db, "зубчик");
            ProductDef dill = FindExact(db, "укроп"), parsley = FindExact(db, "петрушка"), salt = FindExact(db, "соль"), lard = FindExact(db, "сало");
            ProductDef gOil = FindExact(db, "масло чесночное с зеленью"), zat = FindExact(db, "затирка");
            ProductDef herb = dill != null ? dill : parsley, herbAlt = dill != null ? parsley : null;
            if (oil != null && clove != null && herb != null && gOil != null)
                AddAuto(list, Borsch.GarlicOil, r => GarlicOil(r, oil, clove, herb, herbAlt, gOil));
            if (lard != null && clove != null && salt != null && herb != null && zat != null)
            {
                AddAuto(list, Borsch.Zatirka, r => Zatirka(r, lard, clove, salt, herb, herbAlt, zat, CookStepKind.Grind, Borsch.Zatirka));
                AddAuto(list, Borsch.Zatirka + " (миксером)", r => Zatirka(r, lard, clove, salt, herb, herbAlt, zat, CookStepKind.Mix, Borsch.Zatirka + " (миксером)"));
            }
            if (Log && (gOil != null || zat != null) && (clove == null || herb == null || oil == null || lard == null))
                Debug.LogWarning("[Рецепты] чесночное масло / затирка собраны не все — нет продукта: " + (clove == null ? "Зубчик чеснока " : "") + (herb == null ? "Укроп " : "") +
                                 (oil == null ? "Масло подсолнечное " : "") + (lard == null ? "Сало" : ""));
        }

        /// <summary>
        /// МАСЛО ЧЕСНОЧНОЕ С ЗЕЛЕНЬЮ (мазать пампушки кистью): масло подсолнечное 1 нажатие бутылки (2 порции)
        ///   + чеснок 2 зубчика (как выпали из головки или через измельчитель) + укроп 2 дозы (1–3) — миксером 4 с.
        /// </summary>
        public static CookRecipeDef GarlicOil(CookRecipeDef r, ProductDef oil, ProductDef clove, ProductDef herb, ProductDef herbAlt, ProductDef result)
        {
            Plain(r, Borsch.GarlicOil, oil, 2f, CookUnit.Portion, 0.5f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = clove, Amount = 2f, Unit = CookUnit.Portion });   // зубчики — целые (из головки) или измельчённые
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = herb, Alt = herbAlt, Amount = 2f, Unit = CookUnit.Portion });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = 4f });
            r.Result = result;
            return r;
        }

        /// <summary>
        /// ЗАТИРКА (на подачу борща), на 1 пласт сала (больше сала — всё кратно):
        ///   сало ИЗМЕЛЬЧЁННОЕ (пласт → измельчитель) + чеснок 4–5 зубчиков (чищеные, целые или измельчённые)
        ///   + соль 1 мастерок (или 1–3 дозы) + укроп 2 дозы — в банку/ведро и ОТБОЙНИКОМ 6 с (или МИКСЕРОМ 6 с) → «Затирка».
        /// </summary>
        public static CookRecipeDef Zatirka(CookRecipeDef r, ProductDef lard, ProductDef clove, ProductDef salt, ProductDef herb, ProductDef herbAlt, ProductDef result,
                                            CookStepKind step, string title)
        {
            Plain(r, title, lard, 1f, CookUnit.Portion, 0.5f);
            r.BaseState = FoodState.Grated;
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = clove, Amount = 5f, Unit = CookUnit.Portion });   // зубчики из головки — как выпали
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = salt, Amount = 2f, Unit = CookUnit.Portion });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = herb, Alt = herbAlt, Amount = 2f, Unit = CookUnit.Portion });
            r.Steps.Add(new CookRecipeDef.Step { Kind = step, Seconds = 6f, VolumeK = 0.8f });
            r.Result = result;
            return r;
        }

        /// <summary>Шаг 5: сборка борща в кастрюле с бульоном.</summary>
        private static void BuildBorschStep5(List<CookRecipeDef> list, FoodDatabase db)
        {
            ProductDef soup = FindExact(db, "борщ");
            if (soup == null) return;
            ProductDef broth = FindExact(db, "бульон для борща"), potato = FindExact(db, "картофель");
            ProductDef cabbage = FindExact(db, "капуста белокочанная") ?? Find(db, "капуст"), zaz = FindExact(db, "зажарка для борща");
            ProductDef salt = FindExact(db, "соль");
            if (broth != null && potato != null && cabbage != null && zaz != null && salt != null)
                AddAuto(list, Borsch.Soup, r => BorschSoup(r, broth, potato, cabbage, zaz, salt, soup));
            else if (Log)
                Debug.LogWarning("[Рецепты] «Борщ» не собран — нет продукта: " + (broth == null ? "Бульон для борща " : "") + (potato == null ? "Картофель " : "") +
                                 (cabbage == null ? "Капуста белокочанная " : "") + (zaz == null ? "Зажарка для борща " : "") + (salt == null ? "Соль" : ""));
        }

        /// <summary>
        /// БОРЩ — в кастрюле с «Бульоном для борща» (кости и овощи из бульона лучше вынуть, но не мешают):
        ///   картофель 6 порций (≈1 картофелина: целая, кусками или кубиками из измельчителя — любая, кроме грязной)
        ///   + капуста ТЁРТАЯ 12 порций (кочан из измельчителя) + зажарка для борща 4 порции (2 мастерка из сковороды)
        ///   + соль 1 мастерок (или 1–3 дозы баночкой); кипятить 40 с → «Борщ» (бульон и всё положенное — в борщ).
        /// Целые куски картошки в кастрюле-супе плавают собой и остаются кусками в борще. Допуск ±50%.
        /// </summary>
        public static CookRecipeDef BorschSoup(CookRecipeDef r, ProductDef broth, ProductDef potato, ProductDef cabbage, ProductDef zaz, ProductDef salt, ProductDef soup)
        {
            Plain(r, Borsch.Soup, broth, 10f, CookUnit.Portion, 0.5f);
            r.PerContainer = true; r.OnlyThese = false;            // лавровый лист из бульона, кости — не мешают
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = potato, Amount = Borsch.SoupPotato, Unit = CookUnit.Portion, CleanOnly = false });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = cabbage, Amount = Borsch.SoupCabbage, Unit = CookUnit.Portion, State = FoodState.Grated });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = zaz, Amount = Borsch.SoupZazharka, Unit = CookUnit.Portion });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = salt, Amount = 1f, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Heat, Seconds = Borsch.SoupSeconds });
            r.Result = soup;
            return r;
        }

        /// <summary>Шаг 4 борща: томатная паста (уваривание), цепочка зажарки в сковороде.</summary>
        private static void BuildBorschStep4(List<CookRecipeDef> list, FoodDatabase db)
        {
            ProductDef tomato = FindTomato(db), paste = FindExact(db, "томатная паста"), pasteBurnt = FindExact(db, "томатная паста пригоревшая");
            ProductDef saute = FindExact(db, "пассеровка"), beetSt = FindExact(db, "свёкла тушёная"), zaz = FindExact(db, "зажарка для борща");
            ProductDef oil = FindExact(db, "масло подсолнечное"), lard = FindExact(db, "сало");
            ProductDef onion = FindExact(db, "лук репчатый") ?? Find(db, "лук репч"), carrot = FindExact(db, "морковь");
            ProductDef beet = FindExact(db, "свёкла") ?? FindExact(db, "свекла");
            ProductDef vin = FindExact(db, "уксус"), vinA = FindExact(db, "яблочный уксус");
            if (vin == null) { vin = vinA; vinA = null; }
            if (tomato != null && paste != null)
            {
                AddAuto(list, Borsch.Paste, r => TomatoPaste(r, tomato, paste));
                if (pasteBurnt != null)
                {
                    AddAuto(list, Borsch.PasteBurnt, r => PasteBurnt(r, tomato, FoodState.Grated, pasteBurnt, Borsch.PasteBurnt));
                    AddAuto(list, Borsch.PasteBurnt + " (готовая)", r => PasteBurnt(r, paste, FoodState.None, pasteBurnt, Borsch.PasteBurnt + " (готовая)"));
                }
            }
            if (oil != null && onion != null && carrot != null && saute != null)
                AddAuto(list, Borsch.Saute, r => Saute(r, oil, lard, onion, carrot, saute));
            if (saute != null && beet != null && vin != null && beetSt != null)
                AddAuto(list, Borsch.BeetStewed, r => BeetStewed(r, saute, beet, vin, vinA, beetSt));
            if (beetSt != null && paste != null && zaz != null)
                AddAuto(list, Borsch.Zazharka, r => Zazharka(r, beetSt, paste, zaz));
            if (Log && zaz != null && (tomato == null || oil == null || beet == null || vin == null))
                Debug.LogWarning("[Рецепты] зажарка борща собрана не вся — нет продукта: " + (tomato == null ? "Помидор " : "") + (oil == null ? "Масло подсолнечное " : "") +
                                 (beet == null ? "Свёкла " : "") + (vin == null ? "Уксус" : ""));
        }

        /// <summary>
        /// ТОМАТНАЯ ПАСТА: помидоры (мытые) → измельчитель → тёртые — в сковороду (или казан), сколько угодно;
        /// на огонь и УВАРИВАТЬ 60 с на слабом огне: костёр без раздува (под тарой до ~175 °C).
        /// Раздул — уваривание сбивается, тёртые помидоры пригорают («Томатная паста пригоревшая»).
        /// Объём ×0.5 (уварилась вдвое). Ничего лишнего. Дальше — мастерком в ведро, тару освободить под зажарку.
        /// </summary>
        public static CookRecipeDef TomatoPaste(CookRecipeDef r, ProductDef tomato, ProductDef paste)
        {
            Plain(r, Borsch.Paste, tomato, 1f, CookUnit.Portion, 0.5f);
            r.BaseState = FoodState.Grated;
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Simmer, Seconds = Borsch.PasteSeconds, VolumeK = 0.5f });
            r.Result = paste;
            return r;
        }

        /// <summary>Ошибка повара: помидоры / паста на сильном огне 6 с — пригорело.</summary>
        public static CookRecipeDef PasteBurnt(CookRecipeDef r, ProductDef from, FoodState state, ProductDef burnt, string title)
        {
            Plain(r, title, from, 1f, CookUnit.Portion, 0.5f);
            r.BaseState = state;
            r.IsMistake = true;
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Burn, Seconds = Borsch.BurnSeconds });
            r.Result = burnt;
            return r;
        }

        /// <summary>
        /// ПАССЕРОВКА (зажарка, этап 1) — на 1 тёртую луковицу (6 порций), больше лука — всё кратно:
        ///   лук ТЁРТЫЙ 1 шт. + морковь ТЁРТАЯ 1 шт. (измельчитель сыплет прямо в сковороду под выходом)
        ///   + масло подсолнечное 1 нажатие бутылки (2 порции; или сало 1–3 ломтика вместо масла);
        ///   сковороду на огонь — жарить 20 с → «Пассеровка» (×0.8). Основа — лук: по нему считаются партии.
        /// </summary>
        public static CookRecipeDef Saute(CookRecipeDef r, ProductDef oil, ProductDef lard, ProductDef onion, ProductDef carrot, ProductDef saute)
        {
            Plain(r, Borsch.Saute, onion, 6f, CookUnit.Portion, 0.5f);
            r.BaseState = FoodState.Grated;
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = carrot, Amount = 1f, Unit = CookUnit.Piece, PortionsPerPiece = 7, State = FoodState.Grated });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = oil, Alt = lard, Amount = 2f, Unit = CookUnit.Portion });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Fry, Seconds = Borsch.SauteSeconds, VolumeK = 0.8f });
            r.Result = saute;
            return r;
        }

        /// <summary>
        /// СВЁКЛА ТУШЁНАЯ (зажарка, этап 2) — в ту же сковороду к пассеровке:
        ///   свёкла ТЁРТАЯ (запечённая → измельчитель) 1 шт. + уксус 2 нажатия (обычный или яблочный, 1–3) → тушить 30 с.
        /// </summary>
        public static CookRecipeDef BeetStewed(CookRecipeDef r, ProductDef saute, ProductDef beet, ProductDef vin, ProductDef vinAlt, ProductDef result)
        {
            Plain(r, Borsch.BeetStewed, saute, 10f, CookUnit.Portion, 0.5f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = beet, Amount = 1f, Unit = CookUnit.Piece, PortionsPerPiece = 6, State = FoodState.Grated });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = vin, Alt = vinAlt, Amount = 2f, Unit = CookUnit.Portion });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Stew, Seconds = Borsch.BeetSeconds, VolumeK = 0.9f });
            r.Result = result;
            return r;
        }

        /// <summary>
        /// ЗАЖАРКА ДЛЯ БОРЩА (этап 3) — в ту же сковороду к тушёной свёкле:
        ///   томатная паста 2 мастерка (1–3) → тушить 20 с → «Зажарка для борща». Дальше — в кастрюлю с бульоном (шаг 5).
        /// </summary>
        public static CookRecipeDef Zazharka(CookRecipeDef r, ProductDef beetSt, ProductDef paste, ProductDef zaz)
        {
            Plain(r, Borsch.Zazharka, beetSt, 14f, CookUnit.Portion, 0.5f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = paste, Amount = 2f, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Stew, Seconds = Borsch.ZazharkaSeconds });
            r.Result = zaz;
            return r;
        }

        /// <summary>
        /// БУЛЬОН ДЛЯ БОРЩА — на кастрюлю (количества на всю тару, не на ведро):
        ///   кости 4 шт. + голяшка 4 шайбы — сначала ОПАЛИТЬ горелкой (до «Опалено», не до угля);
        ///   лук 2 шт. и морковь 2 шт. (мытые/чищеные) — тоже опалить горелкой;
        ///   всё в кастрюлю, лавровый лист 2 дозы (баночка), воды доверху (≥80% тары вместе со всем);
        ///   кипятить 90 с → вода становится «Бульоном для борща», кости/голяшка/овощи остаются (вынуть сачком/лопатой).
        /// Сырые (не опалённые) тоже сварятся — бульон будет хуже (оценка блюда — позже). Сгоревшие — не в счёт.
        /// Допуск ±50%: костей 2–6, голяшки 2–6, лука и моркови 1–3, лавра 1–3 дозы. Ничего лишнего (соль — в сам борщ).
        /// Старый «Бульон» (5 луковиц, 5 морковок, 10 костей) не трогаем — он основа соусов.
        /// </summary>
        public static CookRecipeDef BorschBroth(CookRecipeDef r, ProductDef water, ProductDef bones, ProductDef shank, ProductDef onion, ProductDef onionRed,
                                                ProductDef carrot, ProductDef bay, ProductDef broth)
        {
            r.Title = Borsch.Broth; r.Driver = CookDriver.Book;
            r.Base = water; r.BaseAmount = 1f; r.BaseUnit = CookUnit.Bucket; r.BaseState = FoodState.None;
            r.BaseFillsContainer = true; r.BaseMinFill = 0.6f;          // воды на 60%+ тары — останется место под борщ
            r.PerContainer = true; r.OnlyBaseBecomesResult = true;
            r.OrderMatters = false; r.OnlyThese = true; r.Tolerance = 0.5f;
            r.Ingredients.Clear();
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = bones, Amount = Borsch.BonesN, Unit = CookUnit.Piece, PortionsPerPiece = 1, CleanOnly = true });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = shank, Amount = Borsch.ShankN, Unit = CookUnit.Piece, PortionsPerPiece = 1, CleanOnly = true });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = onion, Alt = onionRed, Amount = Borsch.OnionN, Unit = CookUnit.Piece, PortionsPerPiece = 6, CleanOnly = true });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = carrot, Amount = Borsch.CarrotN, Unit = CookUnit.Piece, PortionsPerPiece = 7, CleanOnly = true });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = bay, Amount = Borsch.BayDoses, Unit = CookUnit.Dose });
            r.Steps.Clear();
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Heat, Seconds = Borsch.BoilSeconds, VolumeK = 0.7f });   // выкипело на треть — место под картошку, капусту, зажарку
            r.Result = broth;
            return r;
        }

        private static void BuildPelmeniDefaults(List<CookRecipeDef> list)
        {
            FoodDatabase db = Object.FindAnyObjectByType<FoodDatabase>();
            if (db == null || db.Table == null) return;
            ProductDef water = FindExact(db, "вода"), flour = FindExact(db, "мука пшеничная");
            ProductDef egg = FindExact(db, "яйцо"), salt = FindExact(db, "соль"), dough = FindExact(db, "тесто для пельменей");
            BuildPelmeniFilling(list, db);
            ProductDef potato = FindExact(db, "картофель"), gDough = FindExact(db, "тесто для ньокки");
            if (potato != null && flour != null && egg != null && salt != null && gDough != null)
                AddAuto(list, "Тесто для ньокки", r => GnocchiDough(r, potato, flour, egg, salt, gDough));
            if (water != null && flour != null && egg != null && salt != null && dough != null)
                AddAuto(list, Pelmeni.Dough, r => PelmeniDough(r, water, flour, egg, salt, dough));
            else if (Log && dough != null)
                Debug.LogWarning("[Рецепты] «Тесто для пельменей» не собран — нет продукта: " +
                                 (water == null ? "Вода " : "") + (flour == null ? "Мука пшеничная " : "") + (egg == null ? "Яйцо " : "") + (salt == null ? "Соль" : ""));
        }

        /// <summary>
        /// ТЕСТО ДЛЯ НЬОККИ — партия на 3 картофелины (18 порций), больше картошки — всё кратно:
        ///   картофель варёный или пюре 3 шт. + мука 4 мастерка (2–6) + яйцо 1 шт. + соль 1 мастерок (или 1–3 дозы баночкой);
        ///   варить чищеную в кастрюле → поштучно в тару → отбойником пюре → муку, яйцо, соль → миксером 8 с;
        ///   или всё сразу в бетономешалку (варёную картошку тоже) — барабан 8 с. Сырая картошка не считается.
        ///   → «Тесто для ньокки» (×0.75). Дальше: вывалить шаром → в руки → на подстилку → кувалда, каток, шпатель.
        /// </summary>
        public static CookRecipeDef GnocchiDough(CookRecipeDef r, ProductDef potato, ProductDef flour, ProductDef egg, ProductDef salt, ProductDef dough)
        {
            Plain(r, "Тесто для ньокки", potato, 18f, CookUnit.Portion, 0.5f);       // ±50%: соль 1 мастерок или 1–3 дозы, мука 2–6 мастерков
            r.BaseState = FoodState.Puree;                                            // пюре или варёная
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = flour, Amount = 4f, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = egg, Amount = 1f, Unit = CookUnit.Piece });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = salt, Amount = 1f, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = 8f, VolumeK = 0.75f });
            r.Result = dough;
            return r;
        }

        private static void BuildPelmeniFilling(List<CookRecipeDef> list, FoodDatabase db)
        {
            ProductDef fill = FindExact(db, "фарш для пельменей");
            if (fill == null) return;
            ProductDef beef = FindExact(db, "говяжий фарш"), pork = FindExact(db, "свиной фарш");
            ProductDef onion = FindExact(db, "лук репчатый") ?? Find(db, "лук репч");
            ProductDef dill = FindExact(db, "укроп"), parsley = FindExact(db, "петрушка"), sage = FindExact(db, "шалфей");
            ProductDef salt = FindExact(db, "соль"), pepper = FindExact(db, "перец чёрный") ?? FindExact(db, "перец черный");
            ProductDef meat = beef != null ? beef : pork, meatAlt = beef != null ? pork : null;
            ProductDef herb = dill != null ? dill : (parsley != null ? parsley : sage);
            ProductDef herbAlt = dill != null ? (parsley != null ? parsley : sage) : (parsley != null ? sage : null);
            if (meat != null && onion != null && herb != null && salt != null && pepper != null)
                AddAuto(list, Pelmeni.Filling, r => PelmeniFilling(r, meat, meatAlt, onion, herb, herbAlt, salt, pepper, fill));
            else if (Log)
                Debug.LogWarning("[Рецепты] «Фарш для пельменей» не собран — нет продукта: " + (meat == null ? "Говяжий фарш " : "") + (onion == null ? "Лук репчатый " : "") +
                                 (herb == null ? "Укроп " : "") + (salt == null ? "Соль " : "") + (pepper == null ? "Перец чёрный" : ""));
        }

        /// <summary>
        /// ФАРШ ДЛЯ ПЕЛЬМЕНЕЙ — на 1 мастерок лука (≈12 пельменей); больше лука — всё кратно больше (банка, бетономешалка):
        ///   лук репчатый 1 мастерок (тёртый, из измельчителя) + фарш 3 мастерка (говяжий, свиной или смешанный — из измельчителя)
        ///   + укроп 2 дозы (пучок; или петрушка/шалфей) + соль 1 мастерок (или 1–3 дозы) + перец чёрный 1 доза — перемешать (миксер / бетономешалка) 5 с
        ///   → «Фарш для пельменей». Мастерком — по порции в кружок.
        /// Основа — лук: по нему считается, на сколько партий заложено (фарш у основы не может быть двумя продуктами сразу).
        /// </summary>
        public static CookRecipeDef PelmeniFilling(CookRecipeDef r, ProductDef meat, ProductDef meatAlt, ProductDef onion, ProductDef herb, ProductDef herbAlt,
                                                   ProductDef salt, ProductDef pepper, ProductDef fill)
        {
            Plain(r, Pelmeni.Filling, onion, 1f, CookUnit.Trowel, 0.5f);           // ±50%: соль 1 мастерок или 1–3 дозы
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = meat, Alt = meatAlt, Amount = 3f, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = herb, Alt = herbAlt, Amount = 2f, Unit = CookUnit.Dose });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = salt, Amount = 1f, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = pepper, Amount = 1f, Unit = CookUnit.Dose });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = 5f });
            r.Result = fill;
            return r;
        }

        /// <summary>
        /// ТЕСТО ДЛЯ ПЕЛЬМЕНЕЙ (на 1 ведро воды = один ком; 2 ведра — всё вдвое, два кома):
        ///   вода 1 ведро + мука 12 мастерков (±3) + яйцо 2 шт. + соль 1 мастерок;
        ///   перемешать 10 с (бетономешалка крутится, или миксер в ведре) → тугое «Тесто для пельменей».
        ///   Мука впитывает воду — объём ×0.55 (58 → 32 порции, ведро теста).
        /// Дальше: вывалить комом на подстилку (повернуть бочку / сильно наклонить ведро) → кувалда, каток.
        /// От дрожжевого отличается: больше муки, яйца, без дрожжей.
        /// </summary>
        public static CookRecipeDef PelmeniDough(CookRecipeDef r, ProductDef water, ProductDef flour, ProductDef egg, ProductDef salt, ProductDef dough)
        {
            Plain(r, Pelmeni.Dough, water, 1f, CookUnit.Bucket, 0.25f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = flour, Amount = 12f, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = egg, Amount = 2f, Unit = CookUnit.Piece });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = salt, Amount = 1f, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = 10f, VolumeK = 0.55f });
            r.Result = dough;
            return r;
        }

        private static void BuildBBQDefaults(List<CookRecipeDef> list)
        {
            FoodDatabase db = Object.FindAnyObjectByType<FoodDatabase>();
            if (db == null || db.Table == null) return;
            ProductDef salt = FindExact(db, "соль"), paprika = FindExact(db, "паприка"), pepper = FindExact(db, "перец чёрный") ?? FindExact(db, "перец черный");
            ProductDef garlic = FindExact(db, "чеснок сушёный") ?? FindExact(db, "чеснок сушеный"), rub = FindExact(db, "сухой руб");
            ProductDef water = FindExact(db, "вода"), juice = FindExact(db, "яблочный сок"), vinegar = FindExact(db, "яблочный уксус");
            ProductDef bourbon = FindExact(db, "бурбон"), mop = FindExact(db, "моп-соус");
            ProductDef sauce = FindExact(db, "соус барбекю"), brown = FindExact(db, "коричневый сахар"), honey = FindExact(db, "мёд") ?? FindExact(db, "мед");
            ProductDef worc = FindExact(db, "вустерский соус"), glaze = FindExact(db, "глазурь барбекю");
            if (salt != null && paprika != null && pepper != null && garlic != null && rub != null)
                AddAuto(list, "Сухой руб", r => DryRub(r, salt, paprika, pepper, garlic, rub));
            if (water != null && juice != null && vinegar != null && bourbon != null && mop != null)
                AddAuto(list, "Моп-соус", r => MopSauce(r, water, juice, vinegar, bourbon, mop));
            if (sauce != null && brown != null && honey != null && vinegar != null && worc != null && paprika != null && garlic != null && glaze != null)
                AddAuto(list, "Глазурь барбекю", r => BBQGlaze(r, sauce, brown, honey, vinegar, worc, paprika, garlic, glaze));
            ProductDef bRub = FindExact(db, "руб для брискета");
            if (pepper != null && salt != null && brown != null && bRub != null)
                AddAuto(list, "Руб для брискета", r => BrisketRub(r, pepper, salt, brown, bRub));
            ProductDef mustard = FindExact(db, "горчица"), paste = FindExact(db, "горчичная паста для брискета");
            if (mustard != null && pepper != null && salt != null && brown != null && paste != null)
                AddAuto(list, "Горчичная паста для брискета", r => BrisketPaste(r, mustard, pepper, salt, brown, paste));
        }

        /// <summary>
        /// ГОРЧИЧНАЯ ПАСТА ДЛЯ БРИСКЕТА — горчица + техасский руб в одном: горчица 2 порции (бутылка — 2 нажатия) +
        /// перец чёрный 2 дозы + соль 2 дозы + коричневый сахар 1 мастерок — миксером 3 с → густая паста,
        /// её мастерком на брискет (ПКМ — горка, ЛКМ — растирать). Горчица держит специи и даёт корочку.
        /// </summary>
        public static CookRecipeDef BrisketPaste(CookRecipeDef r, ProductDef mustard, ProductDef pepper, ProductDef salt, ProductDef brown, ProductDef paste)
        {
            Plain(r, "Горчичная паста для брискета", mustard, 2f, CookUnit.Portion, 0.5f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = pepper, Amount = 2f, Unit = CookUnit.Dose });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = salt, Amount = 2f, Unit = CookUnit.Dose });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = brown, Amount = 1f, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = 3f });
            r.Result = paste;
            return r;
        }

        /// <summary>
        /// РУБ ДЛЯ БРИСКЕТА (техасский): перец чёрный + соль + коричневый (тростниковый) сахар ПОРОВНУ —
        /// перец 2 дозы, соль 2 дозы, сахар 1 мастерок (= 2) — миксером 3 с → 6 порций (по 3 на сторону брискета).
        /// </summary>
        public static CookRecipeDef BrisketRub(CookRecipeDef r, ProductDef pepper, ProductDef salt, ProductDef brown, ProductDef rub)
        {
            Plain(r, "Руб для брискета", pepper, 2f, CookUnit.Dose, 0.5f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = salt, Amount = 2f, Unit = CookUnit.Dose });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = brown, Amount = 1f, Unit = CookUnit.Trowel });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = 3f });
            r.Result = rub;
            return r;
        }

        /// <summary>
        /// СУХОЙ РУБ (специи для рёбер): паприка 2 дозы + соль 2 + перец чёрный 2 + чеснок сушёный 2
        /// (баночки — ПКМ, одна доза) — перемешать миксером 3 с → 8 порций (4 мастерка = обе стороны пласта).
        /// </summary>
        public static CookRecipeDef DryRub(CookRecipeDef r, ProductDef salt, ProductDef paprika, ProductDef pepper, ProductDef garlic, ProductDef rub)
        {
            Plain(r, "Сухой руб", paprika, 2f, CookUnit.Dose, 0.5f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = salt, Amount = 2f, Unit = CookUnit.Dose });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = pepper, Amount = 2f, Unit = CookUnit.Dose });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = garlic, Amount = 2f, Unit = CookUnit.Dose });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = 3f });
            r.Result = rub;
            return r;
        }

        /// <summary>
        /// МОП-СОУС (брызгать рёбра в духовке): вода 10 порций (бутыль — 2 нажатия) + яблочный сок 5 (1 нажатие)
        /// + яблочный уксус 1 + бурбон 1 — перемешать миксером 3 с → 17 порций. Заправляют пульверизатор.
        /// </summary>
        public static CookRecipeDef MopSauce(CookRecipeDef r, ProductDef water, ProductDef juice, ProductDef vinegar, ProductDef bourbon, ProductDef mop)
        {
            Plain(r, "Моп-соус", water, 10f, CookUnit.Portion, 0.3f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = juice, Amount = 5f, Unit = CookUnit.Portion });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = vinegar, Amount = 1f, Unit = CookUnit.Dose });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = bourbon, Amount = 1f, Unit = CookUnit.Dose });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = 3f });
            r.Result = mop;
            return r;
        }

        /// <summary>
        /// ГЛАЗУРЬ БАРБЕКЮ (мазать рёбра кистью): соус барбекю 10 порций (банка — 2 нажатия) + коричневый сахар
        /// 1 мастерок + мёд 2 дозы + яблочный уксус 1 + вустерский соус 1 + паприка 1 + чеснок сушёный 1 —
        /// на огне (ковш, кастрюля; от 40 °C) прогреть 5 с, потом перемешать миксером 4 с → ~19 порций.
        /// </summary>
        public static CookRecipeDef BBQGlaze(CookRecipeDef r, ProductDef sauce, ProductDef brown, ProductDef honey, ProductDef vinegar,
                                             ProductDef worc, ProductDef paprika, ProductDef garlic, ProductDef glaze)
        {
            Plain(r, "Глазурь барбекю", sauce, 10f, CookUnit.Portion, 0.3f);
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = brown, Amount = 1f, Unit = CookUnit.Trowel });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = honey, Amount = 2f, Unit = CookUnit.Dose });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = vinegar, Amount = 1f, Unit = CookUnit.Dose });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = worc, Amount = 1f, Unit = CookUnit.Dose });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = paprika, Amount = 1f, Unit = CookUnit.Dose });
            r.Ingredients.Add(new CookRecipeDef.Ingredient { Product = garlic, Amount = 1f, Unit = CookUnit.Dose });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Melt, Seconds = 5f });
            r.Steps.Add(new CookRecipeDef.Step { Kind = CookStepKind.Mix, Seconds = 4f });
            r.Result = glaze;
            return r;
        }

        /// <summary>Забыть загруженные рецепты (после правки ассетов в редакторе).</summary>
        public static void Reload() { _recipes = null; }
    }
}
