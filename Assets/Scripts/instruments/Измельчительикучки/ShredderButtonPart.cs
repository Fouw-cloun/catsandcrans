using FishNet.Object;
using UnityEngine;

// В пространстве имён НАМЕРЕННО: в проекте ещё живёт старая система со
// своими глобальными FoodState и FoodTransition. Из глобального
// пространства компилятор взял бы их вместо новых (CatCooks.Food).
namespace CatCooks.Food.Tools
{
    /// <summary>
    /// КНОПКА ИЗМЕЛЬЧИТЕЛЯ. Навёлся, нажал F — станок включился или выключился.
    /// Работает через тот же механизм, что кнопки пылесоса и миксера (IVehiclePart).
    ///
    /// Зелёная: Turn On = да.  Красная: Turn On = нет.
    /// У каждой кнопки станка свой Part Id (0, 1).
    ///
    /// ═══ КУДА УТАПЛИВАЕТСЯ ═══
    ///
    /// Направление задаётся в осях САМОЙ кнопки (как стрелки в режиме Local):
    ///   (0, -1, 0)      — вниз по зелёной оси
    ///   (1, -1, 0)      — по диагонали: вниз по зелёной и вдоль красной, 45°
    ///   (0, -1, 1)      — вниз и вдоль синей
    /// Длину считать не нужно — направление нормализуется само.
    ///
    /// Глубина — в НАСТОЯЩИХ метрах мира: масштаб модели (Scale 5, 100 у FBX)
    /// на неё не влияет. Прошлая версия считала глубину в единицах родителя —
    /// отсюда кнопка «улетала» на другой край станка.
    ///
    /// Выдели кнопку в сцене: жёлтая линия показывает путь нажатия,
    /// шарик на конце — где будет кнопка в нажатом виде.
    /// </summary>
    public class ShredderButtonPart : MonoBehaviour, IVehiclePart
    {
        [Tooltip("Да — зелёная (включить). Нет — красная (выключить).")]
        [SerializeField] private bool _turnOn = true;
        [Tooltip("Уникальный номер части внутри станка: 0, 1…")]
        [SerializeField] private int _partId;

        [Header("Нажатие (визуал)")]
        [Tooltip("Куда утапливается, в осях самой кнопки. (1,-1,0) — диагональ 45° " +
                 "вниз по зелёной и вдоль красной.")]
        [SerializeField] private Vector3 _pressDirection = new Vector3(0f, -1f, 0f);
        [Tooltip("На сколько утапливается, в метрах мира")]
        [Min(0f)][SerializeField] private float _pressDepth = 0.02f;
        [Tooltip("Скорость нажатия/отжатия")]
        [SerializeField] private float _pressSpeed = 12f;

        private ShredderStation _station;
        private NetworkObject _owningNob;
        private Vector3 _restLocalPos;
        private Quaternion _restLocalRot;
        private bool _captured;
        private float _pressT;

        private void Awake()
        {
            _station = GetComponentInParent<ShredderStation>();
            _owningNob = GetComponentInParent<NetworkObject>();
            Capture();
        }

        private void Capture()
        {
            _restLocalPos = transform.localPosition;
            _restLocalRot = transform.localRotation;
            _captured = true;
        }

        public NetworkObject OwningNetworkObject => _owningNob;
        public int PartId => _partId;

        public void ServerUse(PlayerInteractor interactor)
        {
            if (_station != null) _station.ServerSetOn(_turnOn);
        }

        private void Update()
        {
            if (_station == null || !_captured) return;
            // Кнопка «горит» — утоплена, пока её состояние совпадает со станком.
            float target = _station.IsOn == _turnOn ? 1f : 0f;
            if (Mathf.Approximately(_pressT, target)) return;
            _pressT = Mathf.MoveTowards(_pressT, target, Time.deltaTime * _pressSpeed);
            transform.position = PressedWorld(_pressT);
        }

        /// <summary>Позиция кнопки в мире при нажатии t (0 — отжата, 1 — нажата).</summary>
        private Vector3 PressedWorld(float t)
        {
            Transform parent = transform.parent;
            Vector3 restWorld = parent != null ? parent.TransformPoint(_restLocalPos) : _restLocalPos;
            Quaternion restRot = parent != null ? parent.rotation * _restLocalRot : _restLocalRot;
            Vector3 dir = _pressDirection.sqrMagnitude > 1e-6f ? _pressDirection.normalized : Vector3.down;
            return restWorld + restRot * dir * (_pressDepth * t);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) Capture();
            Vector3 a = PressedWorld(0f), b = PressedWorld(1f);
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 1f);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawSphere(b, Mathf.Max(0.004f, _pressDepth * 0.25f));
        }
#endif
    }
}
