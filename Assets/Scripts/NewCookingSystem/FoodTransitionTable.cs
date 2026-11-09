using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Готовый переход, разрешённый для конкретного продукта.
    ///
    /// ═══ КАК РАБОТАЕТ ОКНО ВРЕМЕНИ ═══
    ///
    /// Seconds — идеальный момент, Window — допуск в долях от него:
    ///     нижняя граница = Seconds × (1 − Window)   продукт стал готовым
    ///     идеал          = Seconds                  точность 100
    ///     верхняя        = Seconds × (1 + Window)   дальше портится
    ///
    /// Переход в готовое состояние происходит на НИЖНЕЙ границе — как только
    /// продукт стал съедобным. Дальше он остаётся готовым, но точность падает
    /// по мере удаления от идеала, а за верхней границей портится.
    ///
    /// В прошлой версии переход срабатывал на ВЕРХНЕЙ границе, из-за чего
    /// игрок, который просто ждал, всегда получал точность ноль, а идеал был
    /// недостижим в принципе. Это была главная ошибка системы.
    /// </summary>
    public struct FoodTransition
    {
        public ushort Product;
        public FoodState From;
        public FoodProcess Process;
        public FoodState To;
        public float Seconds;
        public float Window;
        public FoodState TooEarly;
        public FoodState TooLate;

        public bool HasWindow => Window > 0.001f && Seconds > 0.001f;

        /// <summary>Момент, когда продукт становится готовым.</summary>
        public float ReadyAt => HasWindow ? Seconds * (1f - Window) : Seconds;

        /// <summary>Момент, после которого продукт испорчен.</summary>
        public float SpoilAt => HasWindow ? Seconds * (1f + Window) : float.MaxValue;

        /// <summary>Точность попадания: 1 в идеале, 0 на границах окна.
        /// Идёт прямо в шкалу «прожарка».</summary>
        public float Accuracy(float spent)
        {
            if (!HasWindow) return 1f;
            float half = Seconds * Window;
            float d = Mathf.Abs(spent - Seconds);
            if (d >= half) return 0f;
            return 1f - d / half;
        }

        /// <summary>Что получится, если снять прямо сейчас.</summary>
        public FoodState StateAt(float spent)
        {
            if (!HasWindow) return spent >= Seconds ? To : From;
            if (spent < ReadyAt) return TooEarly == FoodState.None ? From : TooEarly;
            if (spent > SpoilAt) return TooLate == FoodState.None ? To : TooLate;
            return To;
        }
    }

    /// <summary>
    /// ТАБЛИЦА ПЕРЕХОДОВ. Строится один раз при старте, дальше только читается.
    /// Ключ — один int: (продукт 16 бит) | (состояние 8) | (процесс 8).
    /// </summary>
    public sealed class FoodTransitionTable
    {
        private readonly Dictionary<int, FoodTransition> _map = new Dictionary<int, FoodTransition>(1024);
        private readonly Dictionary<ushort, ProductDef> _products = new Dictionary<ushort, ProductDef>(256);

        /// <summary>Процессы, реально встреченные при сборке. Перебирать
        /// 255 значений подряд ради поиска тупиков — расточительно.</summary>
        private readonly List<FoodProcess> _usedProcesses = new List<FoodProcess>();

        public int Count => _map.Count;
        public int ProductCount => _products.Count;

        private static int Key(ushort product, FoodState from, FoodProcess proc)
            => (product << 16) | ((byte)from << 8) | (byte)proc;

        public void Build(IList<ProductDef> products)
        {
            _map.Clear();
            _products.Clear();
            _usedProcesses.Clear();
            if (products == null) return;

            HashSet<FoodProcess> procs = new HashSet<FoodProcess>();

            for (int i = 0; i < products.Count; i++)
            {
                ProductDef p = products[i];
                if (p == null) continue;

                if (_products.ContainsKey(p.Id))
                {
                    Debug.LogError($"[Еда] ДВА ПРОДУКТА С Id={p.Id}: " +
                                   $"«{_products[p.Id].Title}» и «{p.Title}». " +
                                   "Id летит по сети числом, дубликат сломает синхронизацию.", p);
                    continue;
                }
                _products.Add(p.Id, p);

                if (p.Group != null)
                    foreach (FoodRule r in p.Group.Rules) Put(p, r, procs);
                foreach (FoodRule r in p.Overrides) Put(p, r, procs);
            }
            _usedProcesses.AddRange(procs);
        }

        private void Put(ProductDef p, FoodRule r, HashSet<FoodProcess> procs)
        {
            if (r.From == FoodState.None || r.Process == FoodProcess.None) return;
            procs.Add(r.Process);
            _map[Key(p.Id, r.From, r.Process)] = new FoodTransition
            {
                Product = p.Id, From = r.From, Process = r.Process, To = r.To,
                Seconds = r.Seconds, Window = r.Window,
                TooEarly = r.TooEarly, TooLate = r.TooLate
            };
        }

        /// <summary>Главный запрос. false — не ошибка: инструмент здесь
        /// неприменим, шлифмашинкой бульон не почистить.</summary>
        public bool TryGet(ushort product, FoodState from, FoodProcess proc, out FoodTransition t)
            => _map.TryGetValue(Key(product, from, proc), out t);

        public ProductDef GetProduct(ushort id)
        {
            _products.TryGetValue(id, out ProductDef p);
            return p;
        }

        /// <summary>Процессы, применимые к продукту сейчас — для подсветки
        /// тех инструментов, которые что-то сделают.</summary>
        public void CollectProcesses(ushort product, FoodState from, List<FoodProcess> into)
        {
            into.Clear();
            for (int i = 0; i < _usedProcesses.Count; i++)
                if (_map.ContainsKey(Key(product, from, _usedProcesses[i])))
                    into.Add(_usedProcesses[i]);
        }

        // ───────────────────────── ПРОВЕРКА ─────────────────────────

        /// <summary>
        /// Самопроверка данных.
        ///
        /// ═══ ПОЧЕМУ ТЕПЕРЬ ТИХАЯ ═══
        ///
        /// Прошлая версия выдавала 88 предупреждений про тупики, и за ними
        /// нельзя было разглядеть настоящую беду. Но тупик — это норма:
        /// варёная картошка и есть конец пути. Теперь предупреждение только
        /// про НЕОЖИДАННЫЕ тупики. Готовое, испорченное и растворившееся молчат.
        /// </summary>
        public string Validate()
        {
            StringBuilder sb = new StringBuilder();
            int errors = 0, warns = 0;

            foreach (KeyValuePair<ushort, ProductDef> kv in _products)
            {
                ProductDef p = kv.Value;
                if (p.Group == null)
                {
                    sb.AppendLine($"ОШИБКА · «{p.Title}» без группы — переходов не будет");
                    errors++;
                    continue;
                }
                if (!HasAnyExit(p.Id, p.StartState))
                {
                    sb.AppendLine($"ОШИБКА · «{p.Title}»: из «{FoodStateInfo.NameOf(p.StartState)}» " +
                                  "нет ни одного перехода — продукт нельзя тронуть");
                    errors++;
                }
            }

            HashSet<int> seen = new HashSet<int>();
            foreach (KeyValuePair<int, FoodTransition> kv in _map)
            {
                FoodTransition t = kv.Value;

                if (t.HasWindow && t.TooLate == FoodState.None)
                {
                    ProductDef p = GetProduct(t.Product);
                    sb.AppendLine($"ВНИМАНИЕ · «{p?.Title}»: у перехода в " +
                                  $"«{FoodStateInfo.NameOf(t.To)}» есть окно, но нет «пере» — " +
                                  "испортить невозможно, окно не работает");
                    warns++;
                }

                if (!seen.Add((t.Product << 16) | (byte)t.To)) continue;
                if (IsExpectedEnd(t.To)) continue;
                if (HasAnyExit(t.Product, t.To)) continue;

                ProductDef pd = GetProduct(t.Product);
                sb.AppendLine($"ВНИМАНИЕ · «{pd?.Title}»: из «{FoodStateInfo.NameOf(t.To)}» " +
                              "дальше некуда, хотя это не конечное состояние");
                warns++;
            }

            string head = $"[Еда] Таблица: {_map.Count} переходов, {_products.Count} продуктов, " +
                          $"{_usedProcesses.Count} процессов. Ошибок {errors}, предупреждений {warns}.";
            return head + (sb.Length > 0 ? "\n" + sb : "  Всё сходится.");
        }

        private bool HasAnyExit(ushort product, FoodState from)
        {
            for (int i = 0; i < _usedProcesses.Count; i++)
                if (_map.ContainsKey(Key(product, from, _usedProcesses[i]))) return true;
            return false;
        }

        /// <summary>Состояния, для которых тупик ожидаем и правилен.</summary>
        private static bool IsExpectedEnd(FoodState s)
        {
            if (FoodStateInfo.IsThermal(s)) return true;     // варёное и есть конец
            switch (s)
            {
                case FoodState.Dissolved:
                case FoodState.Ready:
                case FoodState.Poured:
                case FoodState.Puree:
                case FoodState.Minced:
                case FoodState.Grated:
                case FoodState.Whipped:
                case FoodState.Pieces:
                case FoodState.Melted:
                case FoodState.PileSwollen:
                case FoodState.Dust:
                    return true;
                default:
                    return false;
            }
        }
    }
}
