using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ОДНО ПРАВИЛО ПЕРЕХОДА. Ребро графа.
    ///
    /// Читается так: «продукт в состоянии From, обработанный процессом Process
    /// в течение Best секунд, становится To».
    /// </summary>
    [Serializable]
    public struct FoodRule
    {
        [Tooltip("Из какого состояния")]
        public FoodState From;

        [Tooltip("Каким процессом. Инструмент знает только свой процесс и " +
                 "больше ничего — вся логика здесь.")]
        public FoodProcess Process;

        [Tooltip("В какое состояние переходит при попадании в окно времени")]
        public FoodState To;

        [Tooltip("Сколько секунд нужно для перехода. Ноль = мгновенно, " +
                 "без окна качества: помыть нельзя «слишком сильно».")]
        public float Seconds;

        [Tooltip("Ширина окна в долях от Seconds. 0.35 значит, что попадание " +
                 "в ±35% считается точным.\n\n" +
                 "Ноль = окна нет, перейдёт как только наберётся время.")]
        [Range(0f, 1f)] public float Window;

        [Tooltip("Куда свалится при недоборе времени. Обычно Undercooked.\n" +
                 "None = не переходит вовсе, просто ждёт дальше.")]
        public FoodState TooEarly;

        [Tooltip("Куда свалится при переборе. Обычно Overcooked, потом Burnt.\n" +
                 "None = пересидеть нельзя, состояние стабильно.")]
        public FoodState TooLate;

        /// <summary>Есть ли у правила окно качества. Если нет — это подготовка
        /// вроде мытья, где «перестараться» бессмысленно.</summary>
        public bool HasWindow => Window > 0.001f && Seconds > 0.001f;
    }

    /// <summary>
    /// ГРУППА ПРОДУКТОВ — носитель правил.
    ///
    /// ═══ ГЛАВНАЯ ИДЕЯ ВСЕЙ БАЗЫ ═══
    ///
    /// Переходы принадлежат ГРУППЕ, а не продукту. Правило «овощ + котёл =
    /// варёный овощ» написано один раз, и его получают морковь, картошка,
    /// свёкла и ещё три десятка продуктов.
    ///
    /// По нашим подсчётам: поштучно на продукт вышло бы 223 пары и около 446
    /// переходов. Через группы — 58 правил на десять ассетов. Разница в восемь
    /// раз, и она же означает, что новый продукт стоит ОДНОЙ строчки: указал
    /// группу и всё.
    ///
    /// Если для нового продукта приходится дописывать переходы — значит правила
    /// уехали не туда, и надо чинить группу, а не заводить исключение.
    /// </summary>
    [CreateAssetMenu(menuName = "CatCooks/Группа продуктов", fileName = "Group_")]
    public class FoodGroupDef : ScriptableObject
    {
        [Header("Что это за группа")]
        public string Title = "Овощи и фрукты";

        [TextArea(2, 4)]
        [Tooltip("Для людей: чем эта группа отличается и почему выделена")]
        public string Note = "";

        [Header("Начало пути")]
        [Tooltip("Состояние, в котором продукт появляется на кухне. " +
                 "С него начинается любой путь по графу.")]
        public FoodState StartState = FoodState.Raw;

        [Header("Правила переходов")]
        [Tooltip("Рёбра графа. Из них при старте собирается общая таблица.")]
        public List<FoodRule> Rules = new List<FoodRule>();

        /// <summary>Найти правило. Возвращает false, если такого перехода нет —
        /// значит инструмент к этому продукту в этом состоянии неприменим, и
        /// это нормальная ситуация, а не ошибка.</summary>
        public bool TryGet(FoodState from, FoodProcess proc, out FoodRule rule)
        {
            for (int i = 0; i < Rules.Count; i++)
            {
                if (Rules[i].From == from && Rules[i].Process == proc)
                {
                    rule = Rules[i];
                    return true;
                }
            }
            rule = default;
            return false;
        }
    }
}
