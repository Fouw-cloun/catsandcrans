using System.Reflection;
using FishNet.Object;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// РУЧКА ВЕДРА (ДУЖКА) — болтается сама. Только вид, у каждого игрока свой.
    ///
    ///   • ведро стоит — ручка падает на бок и лежит на кромке; толкнули, понесли
    ///     тележкой, стукнули — подпрыгивает и брякает о край (упругий отскок);
    ///   • ведро в руке — ручка поднята вверх: за неё держат (CarriedContainer
    ///     вешает ведро за точку хвата — Grip);
    ///   • раскачка от рывков ведра — как у маятника: ускорение тары «сдувает» ручку.
    ///
    /// ═══ КАК ВЕШАТЬ ═══
    /// На корень ведра. Pivot — пустой объект на оси петель (обе «ушки» дужки),
    /// его локальная ось X — ось поворота. Дужка (меш) — дочерний объект Pivot.
    /// В модели дужка ПОДНЯТА вверх (угол 0). Настраивает окно
    /// CatCooks → Кулинария → Ведро: заменить модель (кнопка «Настроить ручку»).
    /// </summary>
    [DefaultExecutionOrder(50)]     // после позы ведра в руке (CarriedContainer: -30)
    public sealed class BucketHandle : MonoBehaviour
    {
        [Tooltip("Ось петель: пустой объект, его ось X — ось поворота дужки. Дужка — его дочерний объект")]
        [SerializeField] private Transform _pivot;
        [Tooltip("Длина дужки от оси до хвата (верх дужки), м. 0 — посчитать по мешу")]
        [Min(0f)][SerializeField] private float _length = 0f;
        [Tooltip("Насколько дужка может отклониться в каждую сторону, градусы (дальше — кромка ведра)")]
        [SerializeField] private Vector2 _limits = new Vector2(-88f, 88f);
        [Tooltip("Затухание качания (больше — быстрее успокаивается)")]
        [Range(0f, 10f)][SerializeField] private float _damping = 1.6f;
        [Tooltip("Отскок от кромки: 0 — глухо ложится, 1 — прыгает как резиновая")]
        [Range(0f, 0.9f)][SerializeField] private float _bounce = 0.38f;
        [Tooltip("Насколько сильно рывки ведра раскачивают дужку")]
        [Range(0f, 3f)][SerializeField] private float _shake = 1.2f;
        [Tooltip("Лёгкий дребезг у кромки при ходьбе (дужка «брякает»), градусы")]
        [Range(0f, 10f)][SerializeField] private float _rattle = 3f;
        [Tooltip("За сколько секунд дужка поднимается вверх, когда ведро берут")]
        [Range(0.02f, 0.5f)][SerializeField] private float _liftTime = 0.09f;

        private Quaternion _rest;
        private bool _restSaved;
        private float _ang, _vel;
        private Vector3 _lastPos, _velW, _velPrevW;
        private bool _tracked;
        private PickupItem _item;
        // ВАЖНО: не тип SyncVar — FishNet запрещает SyncVar-поля вне NetworkBehaviour.
        // Держим как object и читаем свойство Value через отражение.
        private object _holder;
        private PropertyInfo _holderValue;
        private bool _holderLooked;
        private float _rattleT;

        /// <summary>Дужка поднята (держат) — точка хвата в осях корня ведра (верх дужки при угле 0).</summary>
        public Vector3 GripLocal
        {
            get
            {
                if (_pivot == null) return Vector3.up * 0.8f;
                Vector3 pivotRoot = transform.InverseTransformPoint(_pivot.position);
                Vector3 upRoot = transform.InverseTransformDirection(_pivot.parent != null
                    ? _pivot.parent.TransformDirection(RestRotation * Vector3.up) : RestRotation * Vector3.up);
                return pivotRoot + upRoot.normalized * Length;
            }
        }

        public bool Ready => _pivot != null;
        public Transform Pivot => _pivot;
        private Quaternion RestRotation { get { SaveRest(); return _rest; } }
        private float Length => _length > 0.01f ? _length : 0.25f;

        /// <summary>Для окна настройки.</summary>
        public void Setup(Transform pivot, float length)
        {
            _pivot = pivot;
            _length = length;
            _restSaved = false;
            SaveRest();
        }

        private void SaveRest()
        {
            if (_restSaved || _pivot == null) return;
            _rest = _pivot.localRotation;
            _restSaved = true;
        }

        private void Awake()
        {
            _item = GetComponent<PickupItem>();
            SaveRest();
            // Ведро стоит — дужка лежит на боку (сторона — случайно, но одинаково у всех по имени).
            _ang = (name.GetHashCode() & 1) == 0 ? _limits.y : _limits.x;
        }

        private bool Held
        {
            get
            {
                if (_item == null) return false;
                if (!_holderLooked)
                {
                    _holderLooked = true;
                    FieldInfo f = typeof(PickupItem).GetField("_holder", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null) _holder = f.GetValue(_item);
                    if (_holder != null) _holderValue = _holder.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                }
                if (_holder == null || _holderValue == null) return false;
                return _holderValue.GetValue(_holder) as NetworkObject != null;
            }
        }

        private void LateUpdate()
        {
            if (_pivot == null) return;
            float dt = Mathf.Clamp(Time.deltaTime, 1e-4f, 0.05f);

            // ускорение петель (сглажено — без дрожи от кадров)
            Vector3 p = _pivot.position;
            if (!_tracked) { _lastPos = p; _velW = _velPrevW = Vector3.zero; _tracked = true; }
            Vector3 raw = (p - _lastPos) / dt;
            _lastPos = p;
            if (raw.sqrMagnitude > 400f) raw = _velW;
            _velW = Vector3.Lerp(_velW, raw, 1f - Mathf.Exp(-dt / 0.04f));
            Vector3 acc = Vector3.ClampMagnitude((_velW - _velPrevW) / dt, 30f);
            _velPrevW = _velW;

            Transform par = _pivot.parent != null ? _pivot.parent : transform;
            Vector3 axis = par.TransformDirection(RestRotation * Vector3.right).normalized;
            Vector3 up0 = par.TransformDirection(RestRotation * Vector3.up).normalized;

            if (Held)
            {
                // держат за дужку — она поднята; лёгкая дрожь от рывков
                _ang = Mathf.SmoothDamp(_ang, 0f, ref _vel, _liftTime, 2000f, dt);
            }
            else
            {
                // маятник вокруг оси петель: «тяжесть» = сила тяжести минус ускорение ведра
                Vector3 dir = Quaternion.AngleAxis(_ang, axis) * up0;
                Vector3 geff = Vector3.down * 9.81f - acc * _shake;
                float L = Length;
                int steps = Mathf.Clamp(Mathf.CeilToInt(dt * 240f), 1, 8);
                float h = dt / steps;
                for (int i = 0; i < steps; i++)
                {
                    dir = Quaternion.AngleAxis(_ang, axis) * up0;
                    float accA = Vector3.Dot(geff, Vector3.Cross(axis, dir)) / L * Mathf.Rad2Deg;
                    _vel += (accA - _vel * _damping) * h;
                    _ang += _vel * h;
                    if (_ang > _limits.y) { _ang = _limits.y; if (_vel > 0f) _vel = -_vel * _bounce; }
                    if (_ang < _limits.x) { _ang = _limits.x; if (_vel < 0f) _vel = -_vel * _bounce; }
                }
                // дребезг: ведро трясётся, дужка лежит на кромке — «брякает»
                float shakeK = Mathf.Clamp01(acc.magnitude / 6f);
                if (_rattle > 0f && shakeK > 0.15f && (_ang >= _limits.y - 0.5f || _ang <= _limits.x + 0.5f))
                {
                    _rattleT -= dt;
                    if (_rattleT <= 0f)
                    {
                        _rattleT = Random.Range(0.06f, 0.16f);
                        float kick = _rattle * shakeK * Random.Range(4f, 9f);
                        _vel += _ang > 0f ? -kick : kick;
                    }
                }
            }
            _pivot.localRotation = RestRotation * Quaternion.AngleAxis(_ang, Vector3.right);
        }

        private void OnDrawGizmosSelected()
        {
            if (_pivot == null) return;
            Transform par = _pivot.parent != null ? _pivot.parent : transform;
            Quaternion r = _restSaved ? _rest : _pivot.localRotation;
            Vector3 axis = par.TransformDirection(r * Vector3.right);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(_pivot.position - axis * 0.2f, _pivot.position + axis * 0.2f);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.TransformPoint(GripLocal), 0.02f);
        }
    }
}
