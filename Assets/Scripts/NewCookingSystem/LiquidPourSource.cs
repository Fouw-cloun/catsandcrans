using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// ЛЬЁТ СТРУЮ из чего угодно: бутылка, кувшин, лейка, чайник, противень, мешок.
    /// Только визуал (переливание порций делает своя логика, например CookwareSpill).
    ///
    /// Как пользоваться:
    ///   1) повесь на предмет, создай дочерний пустой объект «Mouth» на носике или краю,
    ///      синяя стрелка (forward) — куда льётся, красная (right) — вдоль края;
    ///   2) Shape = Spout — круглая струя из носика; Lip — «простыня» с края шириной Lip Width;
    ///   3) по умолчанию льётся сам, когда предмет наклоняют (Start Tilt..Full Tilt),
    ///      или управляй из кода: Flow = 0..1 (Pour By Tilt выключи).
    /// </summary>
    public sealed class LiquidPourSource : MonoBehaviour
    {
        public enum Shape { Spout, Lip }

        [SerializeField] private Transform _mouth;
        [SerializeField] private Shape _shape = Shape.Spout;
        [Tooltip("Толщина струи у носика, м")]
        [SerializeField] private float _width = 0.025f;
        [Tooltip("Ширина слива с края (Shape = Lip), м")]
        [SerializeField] private float _lipWidth = 0.18f;
        [Tooltip("Скорость вылета, м/с")]
        [SerializeField] private float _speed = 0.8f;
        [SerializeField] private Color _color = new Color(0.35f, 0.75f, 0.85f);
        [Tooltip("Характер жидкости. Пусто — по цвету")]
        [SerializeField] private LiquidPourProfile _profile;

        [Header("Когда льётся")]
        [SerializeField] private bool _pourByTilt = true;
        [Tooltip("Наклон (от вертикали), с которого начинает литься / льётся в полную силу, °")]
        [SerializeField] private float _startTilt = 70f;
        [SerializeField] private float _fullTilt = 120f;
        [Tooltip("Лить всегда (для проверки вида)")]
        [SerializeField] private bool _forcePour;

        private LiquidStreamRibbon _stream;

        /// <summary>Сила струи 0..1, если Pour By Tilt выключен.</summary>
        public float Flow { get; set; }
        public Color LiquidColor { get => _color; set => _color = value; }
        public LiquidPourProfile Profile { get => _profile; set => _profile = value; }

        public void Setup(Transform mouth, Shape shape, float width, float lipWidth, float speed, Color color,
                          LiquidPourProfile profile, bool force)
        {
            _mouth = mouth; _shape = shape; _width = width; _lipWidth = lipWidth; _speed = speed;
            _color = color; _profile = profile; _forcePour = force; _pourByTilt = !force;
        }

        private void Update()
        {
            Transform m = _mouth != null ? _mouth : transform;
            float flow;
            if (_forcePour) flow = 1f;
            else if (_pourByTilt)
            {
                float tilt = Vector3.Angle(transform.up, Vector3.up);
                flow = Mathf.InverseLerp(_startTilt, Mathf.Max(_startTilt + 1f, _fullTilt), tilt);
            }
            else flow = Mathf.Clamp01(Flow);

            bool on = flow > 0.02f;
            if (!on && _stream == null) return;
            if (_stream == null) _stream = LiquidStreamRibbon.Create(transform);

            float width = _width * (0.35f + 0.65f * Mathf.Sqrt(flow));
            Vector3 vel = m.forward * (_speed * (0.6f + 0.6f * flow));
            _stream.Profile = _profile;
            if (_shape == Shape.Lip) _stream.SetLip(m.right, Mathf.Max(width, _lipWidth * Mathf.Sqrt(flow)));
            _stream.Tick(on, m.position, vel, width, _color, transform, 50f * flow);
        }

        private void OnDisable()
        {
            if (_stream != null) Destroy(_stream.gameObject);
            _stream = null;
        }

        private void OnDrawGizmosSelected()
        {
            Transform m = _mouth != null ? _mouth : transform;
            Gizmos.color = new Color(0.3f, 0.8f, 1f);
            Gizmos.DrawRay(m.position, m.forward * 0.25f);
            if (_shape == Shape.Lip)
            {
                Vector3 r = m.right * _lipWidth * 0.5f;
                Gizmos.DrawLine(m.position - r, m.position + r);
            }
        }
    }
}
