using System;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>Что происходит с кастрюлей прямо сейчас (новая система).</summary>
    public enum PotCondition : byte
    {
        Cold = 0,       // холодная
        Warm = 1,       // греется
        Boiling = 2,    // кипит
        Overheat = 3,   // давление под крышкой: трясётся, шипит, крышка дребезжит
        Ruptured = 4,   // разорвана — заварить дыры и выправить молотком
    }

    /// <summary>
    /// «ТЕЛО» КАСТРЮЛИ: температура, давление, разрыв, ремонт.
    ///
    /// Через этот интерфейс работают крышка (PotLidSocket), дыры и сварка (PotDamage),
    /// вмятины и молоток (PotHammerRepair, HammerItem), пар и тряска (PotEffects),
    /// градусники (PotThermometer3D, WorldThermometer).
    ///
    /// Новая кастрюля — CookwarePressure (на Cookware + CookwareHeat).
    /// Старая — PotThermal (тоже реализует, чтобы старые кастрюли не сломались,
    /// пока их не удалили).
    /// </summary>
    public interface IPotBody
    {
        /// <summary>Температура, °C.</summary>
        float TemperatureC { get; }
        /// <summary>Температура в долях шкалы градусника 0..1.</summary>
        float Temp01 { get; }
        /// <summary>Давление 0..1 (1 — разрыв).</summary>
        float Pressure01 { get; }
        PotCondition Condition { get; }
        bool IsRuptured { get; }
        /// <summary>Рвануло (зовётся у всех: сервер и клиенты).</summary>
        event Action Ruptured;
        /// <summary>Инструмент что-то починил — проверить, цела ли теперь кастрюля целиком. Сервер.</summary>
        void ServerCheckFullyRepaired();
    }

    public static class PotBodyUtil
    {
        /// <summary>IPotBody на объекте, в детях или в родителях. null — нет.</summary>
        public static IPotBody Find(Component c)
        {
            if (c == null) return null;
            IPotBody b = c.GetComponent<IPotBody>();
            if (b == null) b = c.GetComponentInChildren<IPotBody>();
            if (b == null) b = c.GetComponentInParent<IPotBody>();
            return b;
        }
    }
}
