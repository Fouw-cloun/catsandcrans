using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЖУРНАЛ ТАРЫ — «аналитик содержимого». Висит на каждой таре сам (сервер).
    ///
    /// Помнит:
    ///   • что сейчас лежит и сколько (порции + в вёдрах/мастерках/штуках);
    ///   • ПОРЯДОК: что положили первым, вторым, третьим;
    ///   • ОБРАБОТКИ: сколько секунд мешали, морозили, квасили;
    ///   • какой рецепт сейчас идёт, на каком шаге и с какой оценкой.
    ///
    /// Сам следит за содержимым (раз в 0.1 с): прибавилось — запись «добавлено»,
    /// убыло — «вылили/зачерпнули». Тара опустела (вылили всё ПКМ) — журнал
    /// обнуляется, можно начинать заново.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CookwareJournal : MonoBehaviour
    {
        public struct Line { public ushort Product; public int Count; public FoodState State; }

        private Cookware _cw;
        private readonly Dictionary<ushort, int> _counts = new Dictionary<ushort, int>();
        private readonly Dictionary<ushort, FoodState> _states = new Dictionary<ushort, FoodState>();
        private readonly List<ushort> _order = new List<ushort>();
        private readonly Dictionary<CookStepKind, float> _processed = new Dictionary<CookStepKind, float>();
        private float _poll;

        // ── рецепт, который идёт сейчас (ведёт CookRecipeBook) ──
        public CookRecipeDef Recipe;
        public int StepIndex;
        public float StepProgress;
        public int Batches;
        public float Score = 100f;
        public string Hint;
        public string LastResult;
        /// <summary>Что-то добавили после начала обработки — рецепт сбивается.</summary>
        public bool ChangedSinceStep;
        /// <summary>«Варить помешивая»: когда последний раз мешали (Time.time) и сколько секунд копятся комки.</summary>
        public float StirAt, Lumps;

        public Cookware Cookware => _cw;
        public IReadOnlyList<ushort> Order => _order;
        public bool IsEmpty => _counts.Count == 0;

        /// <summary>Журнал тары (создаётся сам). Только сервер.</summary>
        public static CookwareJournal Of(Cookware cw)
        {
            if (cw == null) return null;
            CookwareJournal j = cw.GetComponent<CookwareJournal>();
            if (j == null) { j = cw.gameObject.AddComponent<CookwareJournal>(); j._cw = cw; j.Sync(); }
            return j;
        }

        /// <summary>Есть ли журнал (для аналитика на клиенте-хосте; не создаёт).</summary>
        public static CookwareJournal Peek(Cookware cw) => cw != null ? cw.GetComponent<CookwareJournal>() : null;

        private void Awake() { if (_cw == null) _cw = GetComponent<Cookware>(); }

        private void Update()
        {
            _poll -= Time.deltaTime;
            if (_poll > 0f) return;
            _poll = 0.1f;
            Sync();
        }

        public int CountOf(ushort product) => _counts.TryGetValue(product, out int n) ? n : 0;
        public float Processed(CookStepKind k) => _processed.TryGetValue(k, out float s) ? s : 0f;

        public void LogProcess(CookStepKind k, float seconds)
        {
            _processed.TryGetValue(k, out float s);
            _processed[k] = s + seconds;
        }

        /// <summary>Перечитать содержимое тары и записать, что изменилось.</summary>
        public void Sync()
        {
            if (_cw == null) return;
            var now = new Dictionary<ushort, int>();
            for (int i = 0; i < _cw.ViewCount; i++)
            {
                BagView v = _cw.GetView(i);
                if (v.Count == 0) continue;
                now.TryGetValue(v.Product, out int c);
                now[v.Product] = c + v.Count;
                _states[v.Product] = v.State;
            }
            // прибавилось — в порядок добавления (первый раз) и «сбит шаг»
            foreach (var kv in now)
            {
                _counts.TryGetValue(kv.Key, out int was);
                if (kv.Value > was)
                {
                    if (!_order.Contains(kv.Key)) _order.Add(kv.Key);
                    if (Recipe != null && !Recipe.Uses(kv.Key)) ChangedSinceStep = true;
                    else if (Recipe != null && StepIndex > 0 && !IsIntermediate(kv.Key)) ChangedSinceStep = true;
                }
            }
            _counts.Clear();
            foreach (var kv in now) _counts[kv.Key] = kv.Value;
            _order.RemoveAll(p => !_counts.ContainsKey(p));

            if (_counts.Count == 0) ResetAll();      // вылили всё — чистый лист
        }

        private bool IsIntermediate(ushort p)
        {
            if (Recipe == null) return false;
            foreach (CookRecipeDef.Step s in Recipe.Steps)
                if (s.Intermediate != null && s.Intermediate.Id == p) return true;
            return Recipe.Result != null && Recipe.Result.Id == p;
        }

        public void ResetRecipe()
        {
            Recipe = null; StepIndex = 0; StepProgress = 0f; Batches = 0; Score = 100f; ChangedSinceStep = false; StirAt = 0f; Lumps = 0f;
        }

        public void ResetAll()
        {
            ResetRecipe();
            _order.Clear();
            _processed.Clear();
            _states.Clear();
            Hint = null;
            LastResult = null;        // вычерпали всё — метка «✔ готово» больше не висит
        }

        /// <summary>Строки для аналитика: порядок, количества в мерах, обработки, рецепт.</summary>
        public string Report()
        {
            var sb = new StringBuilder();
            FoodDatabase db = Object.FindAnyObjectByType<FoodDatabase>();
            int idx = 1;
            foreach (ushort p in _order)
            {
                ProductDef d = db != null ? db.Get(p) : null;
                FoodState st = _states.TryGetValue(p, out FoodState s) ? s : FoodState.None;
                int n = CountOf(p);
                sb.AppendLine($"{idx++}. {(d != null ? d.Title : "#" + p)} — {FoodUnits.Describe(n, FoodUnits.NaturalUnit(d, st))}");
            }
            if (_processed.Count > 0)
            {
                sb.Append("Обработки: ");
                foreach (var kv in _processed) sb.Append($"{KindName(kv.Key)} {kv.Value:0.#} с; ");
                sb.AppendLine();
            }
            if (Recipe != null)
            {
                string step = StepIndex < Recipe.Steps.Count
                    ? $"шаг {StepIndex + 1}/{Recipe.Steps.Count}: {KindName(Recipe.Steps[StepIndex].Kind)} {Mathf.Clamp01(StepProgress / Mathf.Max(0.1f, Recipe.Steps[StepIndex].Seconds)) * 100f:0}%"
                    : "готово";
                sb.AppendLine($"Рецепт: {Recipe.Title} ×{Batches} — {step}, оценка {Score:0}");
            }
            if (!string.IsNullOrEmpty(Hint)) sb.AppendLine("⚠ " + Hint);
            if (!string.IsNullOrEmpty(LastResult)) sb.AppendLine("✔ " + LastResult);
            return sb.ToString();
        }

        public static string KindName(CookStepKind k)
        {
            switch (k)
            {
                case CookStepKind.Mix: return "перемешать";
                case CookStepKind.Freeze: return "заморозить";
                case CookStepKind.Ferment: return "сквасить";
                case CookStepKind.Heat: return "варить";
                case CookStepKind.Rest: return "выдержать";
                case CookStepKind.Whip: return "взбить";
                case CookStepKind.Grind: return "измельчить отбойником";
                case CookStepKind.Roast: return "обжарить";
                case CookStepKind.Melt: return "растопить";
                case CookStepKind.Caramelize: return "карамелизовать";
                case CookStepKind.Scorch: return "пригорает";
                case CookStepKind.Simmer: return "уваривать";
                case CookStepKind.Fry: return "жарить";
                case CookStepKind.Stew: return "тушить";
                case CookStepKind.Burn: return "пригорает (сильный огонь)";
                case CookStepKind.SimmerStir: return "варить помешивая";
                case CookStepKind.Pound: return "отбить кувалдой";
                default: return k.ToString();
            }
        }
    }
}
