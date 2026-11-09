using FishNet;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ОБРАБОТКА УДАРОМ ИЛИ ТРЕНИЕМ. Для того, что не «включается», а работает
    /// самим движением: кувалда отбивает, каток раскатывает, край кастрюли
    /// разбивает яйцо.
    ///
    /// FoodTool тут не подходит: у кувалды нет состояния «работает», есть
    /// удар. Поэтому считаем физический контакт.
    ///
    ///   Hit — каждый удар быстрее MinSpeed даёт WorkPerHit секунд работы,
    ///         умноженных на силу удара. Кувалда, край кастрюли (яйцо).
    ///   Rub — пока касается и движется, работа = время × скорость.
    ///         Каток, скалка, пестик.
    ///
    /// ═══ КАК ВЕШАТЬ ═══
    ///
    /// На объект с ОБЫЧНЫМ (не триггер) коллайдером рабочей части:
    ///   бойку кувалды, валу катка, ободку кастрюли. Коллизии считает сервер.
    ///   Если коллайдер на дочернем объекте, а Rigidbody на корне — вешай
    ///   сюда же, на объект с коллайдером: Unity шлёт OnCollision и ему.
    ///
    /// Пример: разбить яйцо о кастрюлю — на ободок кастрюли FoodImpactTool,
    ///   Process = Разбивать, Mode = Hit, MinSpeed = 1.2. Кинул яйцо в край —
    ///   оно «Разбито» и дальше ведёт себя как жидкость.
    /// </summary>
    public sealed class FoodImpactTool : MonoBehaviour
    {
        public enum Mode : byte { Hit = 0, Rub = 1 }

        public FoodProcess Process = FoodProcess.Pound;
        public Mode WorkMode = Mode.Hit;

        [Header("Удар")]
        [Tooltip("Слабее этого удар не считается, м/с")]
        [Min(0f)] public float MinSpeed = 2f;
        [Tooltip("Сколько секунд работы даёт удар на скорости FullSpeed")]
        [Min(0f)] public float WorkPerHit = 1f;
        [Tooltip("Скорость, на которой удар считается полным, м/с")]
        [Min(0.1f)] public float FullSpeed = 6f;

        [Header("Трение")]
        [Tooltip("Скорость движения, на которой трение даёт полную работу, м/с")]
        [Min(0.05f)] public float RubFullSpeed = 1f;

        [Header("Отладка")]
        [SerializeField] private bool _log = true;

        private static bool IsServer => InstanceFinder.IsServerStarted;

        private void OnCollisionEnter(Collision c)
        {
            if (!IsServer || WorkMode != Mode.Hit) return;
            FoodPiece p = c.collider.GetComponentInParent<FoodPiece>();
            if (p == null || !p.CanApplyNow(Process)) return;

            float speed = c.relativeVelocity.magnitude;
            if (speed < MinSpeed) return;

            float power = Mathf.Clamp01(speed / FullSpeed);
            // Удар мгновенный: сразу фиксируем, иначе таймер ждал бы следующего.
            bool changed = p.ServerAddWork(Process, WorkPerHit * power, power);
            if (changed) p.ServerStopWork();
            if (_log) Debug.Log($"[{name}] удар {speed:0.0} м/с → {p}", this);
        }

        private void OnCollisionStay(Collision c)
        {
            if (!IsServer || WorkMode != Mode.Rub) return;
            FoodPiece p = c.collider.GetComponentInParent<FoodPiece>();
            if (p == null || !p.CanApplyNow(Process)) return;

            float speed = c.relativeVelocity.magnitude;
            float power = Mathf.Clamp01(speed / RubFullSpeed);
            if (power < 0.05f) return;

            if (p.ServerAddWork(Process, Time.fixedDeltaTime * power, power) && _log)
                Debug.Log($"[{name}] → {p}", this);
        }

        private void OnCollisionExit(Collision c)
        {
            if (!IsServer || WorkMode != Mode.Rub) return;
            FoodPiece p = c.collider.GetComponentInParent<FoodPiece>();
            if (p != null) p.ServerStopWork();
        }
    }
}
