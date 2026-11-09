using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// СТРУЯ ЖАРА — горелка, кровельная горелка, паяльная лампа. Всё, что бьёт пламенем
    /// из сопла и нагревает то, во что упирается.
    ///
    /// Кто греется (ковш — SurfaceHeatGlow), сам опрашивает список горящих струй
    /// и решает, попали ли в него. Горелке не нужно знать ни про ковши, ни про еду.
    /// Состояние струи (горит, где сопло) уже есть у всех игроков — по сети его не гоняем.
    /// </summary>
    public interface IHeatJet
    {
        /// <summary>Горит прямо сейчас.</summary>
        bool JetActive { get; }
        /// <summary>Откуда бьёт пламя (сопло), мир.</summary>
        Vector3 JetOrigin { get; }
        /// <summary>Куда бьёт (единичный вектор), мир.</summary>
        Vector3 JetDirection { get; }
        /// <summary>Длина пламени, м.</summary>
        float JetLength { get; }
        /// <summary>Радиус пламени, м.</summary>
        float JetRadius { get; }
        /// <summary>Мощность: 1 — обычная ручная горелка, 2–3 — кровельная на полном газу.</summary>
        float JetPower { get; }
        /// <summary>Корень предмета (чтобы струя не «попадала» в саму горелку).</summary>
        Transform JetRoot { get; }
    }

    /// <summary>Список горящих струй. Горелка регистрируется, пока горит.</summary>
    public static class HeatJets
    {
        private static readonly List<IHeatJet> _all = new List<IHeatJet>();

        public static IReadOnlyList<IHeatJet> All => _all;

        public static void Register(IHeatJet jet)
        {
            if (jet != null && !_all.Contains(jet)) _all.Add(jet);
        }

        public static void Unregister(IHeatJet jet)
        {
            if (jet != null) _all.Remove(jet);
        }

        /// <summary>Выкинуть уничтоженные (смена сцены без OnDestroy).</summary>
        public static void Prune()
        {
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                IHeatJet j = _all[i];
                if (j == null || (j is Object o && o == null)) _all.RemoveAt(i);
            }
        }
    }
}
