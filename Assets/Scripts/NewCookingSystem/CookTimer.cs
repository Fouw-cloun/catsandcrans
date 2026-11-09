using System;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>Что случилось за шаг таймера.</summary>
    public enum CookEvent : byte
    {
        /// <summary>Ничего нового: идёт набор времени или точность едет в окне.</summary>
        None = 0,
        /// <summary>Переход без окна завершён (помыли, нарезали).</summary>
        Done = 1,
        /// <summary>Нижняя граница окна: продукт стал готовым, точность начала расти.</summary>
        Ready = 2,
        /// <summary>Верхняя граница окна пройдена: переготовлено.</summary>
        Spoiled = 3,
        /// <summary>Сняли раньше нижней границы: недоготовлено.</summary>
        Undercooked = 4,
        /// <summary>Процесс к продукту неприменим.</summary>
        NotApplicable = 5,
    }

    /// <summary>
    /// ТАЙМЕР ОДНОЙ ОБРАБОТКИ. Общий для куска на столе (FoodPiece) и для
    /// позиции в таре (BagEntry), чтобы логика окна жила в ОДНОМ месте.
    ///
    /// ═══ ЧТО ИСПРАВЛЕНО ═══
    ///
    /// В прошлой версии и FoodPiece, и ContentBag искали правило по ТЕКУЩЕМУ
    /// состоянию. На нижней границе окна «Кусочки» становились «Варёным», и
    /// следующий кадр искал правило «Варёное + Варить». Такого правила в
    /// данных нет, поэтому:
    ///   • точность не росла к идеалу — прожарка всегда 0;
    ///   • переварить было невозможно — ветка TooLate не выполнялась никогда.
    ///
    /// Теперь таймер помнит ИСХОДНОЕ состояние (Origin), с которого начался
    /// процесс, и правило всегда ищется по нему. Состояние продукта при этом
    /// честно меняется на «Варёное» в момент готовности — для визуала, рецептов
    /// и подсказок продукт уже готов, а таймер продолжает вести окно.
    /// </summary>
    [Serializable]
    public struct CookTimer
    {
        [Tooltip("Состояние, с которого начался процесс. Ключ поиска правила.")]
        public FoodState Origin;

        [Tooltip("Какой процесс идёт. None — таймер стоит.")]
        public FoodProcess Process;

        [Tooltip("Сколько секунд процесс уже воздействует")]
        public float Spent;

        [Tooltip("Продукт уже готов, но процесс продолжается: точность меняется")]
        public bool InWindow;

        public bool IsRunning => Process != FoodProcess.None;

        public void Clear()
        {
            Origin = FoodState.None;
            Process = FoodProcess.None;
            Spent = 0f;
            InWindow = false;
        }

        /// <summary>
        /// Шаг обработки.
        ///
        ///   0 .. ReadyAt   сырое, состояние не меняется
        ///   ReadyAt        state = To, событие Ready, точность начинает расти
        ///   Seconds        идеал, точность 1
        ///   .. SpoilAt     точность падает
        ///   после SpoilAt  state = TooLate, событие Spoiled
        ///
        /// accuracy — текущая точность 0..1 (для прожарки). Для переходов без
        /// окна всегда 1.
        /// </summary>
        public static CookEvent Tick(ref CookTimer timer, ref FoodState state, ushort product,
                                     FoodProcess proc, float dt, FoodTransitionTable table,
                                     out float accuracy)
        {
            accuracy = 0f;
            if (table == null || proc == FoodProcess.None) return CookEvent.NotApplicable;

            // Сменился процесс или состояние поменяли снаружи (помыли прямо
            // посреди варки) — начинаем заново от текущего состояния.
            if (timer.Process != proc || !IsConsistent(ref timer, state, product, table))
            {
                timer.Origin = state;
                timer.Process = proc;
                timer.Spent = 0f;
                timer.InWindow = false;
            }

            if (!table.TryGet(product, timer.Origin, proc, out FoodTransition t))
            {
                timer.Clear();
                return CookEvent.NotApplicable;
            }

            timer.Spent += dt;

            // ── без окна: просто набрать время ──
            if (!t.HasWindow)
            {
                if (timer.Spent < t.Seconds) { accuracy = 0f; return CookEvent.None; }
                state = t.To;
                accuracy = 1f;
                timer.Clear();
                return CookEvent.Done;
            }

            // ── с окном ──
            if (!timer.InWindow)
            {
                if (timer.Spent < t.ReadyAt) return CookEvent.None;
                timer.InWindow = true;
                state = t.To;
                accuracy = t.Accuracy(timer.Spent);
                return CookEvent.Ready;
            }

            accuracy = t.Accuracy(timer.Spent);

            if (timer.Spent > t.SpoilAt && t.TooLate != FoodState.None)
            {
                state = t.TooLate;
                accuracy = 0f;
                timer.Clear();
                return CookEvent.Spoiled;
            }

            // Окно без «пере» (TooLate = None): держим на границе, чтобы
            // таймер не рос бесконечно и не переполнял float за часы игры.
            if (timer.Spent > t.SpoilAt) timer.Spent = t.SpoilAt;
            return CookEvent.None;
        }

        /// <summary>
        /// Процесс прекращён: вынули из-под инструмента, сняли со сковороды.
        ///
        /// Если продукт уже в окне — фиксируем набранную точность.
        /// Если ещё не дошёл, но держали заметно долго — недоготовлено.
        /// Подержали совсем чуть-чуть — считаем, что не начинали.
        ///
        /// Для ТАРЫ это не зовётся при снятии с огня: там позиции помнят своё
        /// время и доварятся, когда кастрюлю вернут на плиту.
        /// </summary>
        public static CookEvent Stop(ref CookTimer timer, ref FoodState state, ushort product,
                                     FoodTransitionTable table, out float accuracy)
        {
            accuracy = 0f;
            if (!timer.IsRunning || table == null) { timer.Clear(); return CookEvent.None; }

            if (!table.TryGet(product, timer.Origin, timer.Process, out FoodTransition t) ||
                !t.HasWindow)
            {
                timer.Clear();
                return CookEvent.None;
            }

            if (timer.InWindow)
            {
                accuracy = t.Accuracy(timer.Spent);
                timer.Clear();
                return CookEvent.None;                // готово, точность зафиксирована
            }

            if (timer.Spent < t.ReadyAt * 0.4f || t.TooEarly == FoodState.None)
            {
                timer.Clear();
                return CookEvent.None;
            }

            state = t.TooEarly;
            timer.Clear();
            return CookEvent.Undercooked;
        }

        /// <summary>Прогресс до готовности 0..1 — для полосок и свечения.
        /// В окне всегда 1.</summary>
        public static float Progress01(in CookTimer timer, ushort product, FoodTransitionTable table)
        {
            if (!timer.IsRunning || table == null) return 0f;
            if (timer.InWindow) return 1f;
            if (!table.TryGet(product, timer.Origin, timer.Process, out FoodTransition t)) return 0f;
            float target = t.HasWindow ? t.ReadyAt : t.Seconds;
            if (target <= 0.001f) return 1f;
            return Mathf.Clamp01(timer.Spent / target);
        }

        /// <summary>Позиция в окне −1..+1: −1 нижняя граница, 0 идеал, +1 верхняя.
        /// Для будильника над тарой. Вне окна — NaN.</summary>
        public static float WindowPosition(in CookTimer timer, ushort product, FoodTransitionTable table)
        {
            if (!timer.InWindow || table == null) return float.NaN;
            if (!table.TryGet(product, timer.Origin, timer.Process, out FoodTransition t) ||
                !t.HasWindow) return float.NaN;
            float half = t.Seconds * t.Window;
            if (half <= 0.0001f) return 0f;
            return Mathf.Clamp((timer.Spent - t.Seconds) / half, -1f, 1f);
        }

        /// <summary>Совпадает ли текущее состояние с тем, чего ждёт таймер.
        /// До окна — исходное, в окне — результат перехода.</summary>
        private static bool IsConsistent(ref CookTimer timer, FoodState state, ushort product,
                                         FoodTransitionTable table)
        {
            if (!timer.IsRunning) return false;
            if (!timer.InWindow) return state == timer.Origin;
            return table.TryGet(product, timer.Origin, timer.Process, out FoodTransition t)
                   && state == t.To;
        }
    }
}
