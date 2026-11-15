using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.Food.Tools
{
    /// <summary>
    /// АЗОТНЫЙ БАЛЛОН. Тяжёлый: носить можно (F), но медленно (Carry Speed у Pickup Item).
    ///
    /// Вентиль-колесо открывают ПИСТОЛЕТОМ: держишь азотный пистолет, наводишься
    /// на баллон и жмёшь R — колесо прокручивается, азот идёт в шланг. Ещё раз R —
    /// закрыть. Пока вентиль открыт, из-под колеса понемногу сочится морозный пар.
    ///
    /// ═══ ТРЕБУЕТ ═══
    /// NetworkObject, NetworkTransform (Client Authoritative), Rigidbody (масса 40–60), Collider.
    /// Valve Wheel — меш колеса (крутится), Outlet — точка, откуда выходит шланг.
    /// В Pickup Item: Carry Speed 0.4–0.5.
    /// </summary>
    public class NitrogenTank : PickupItem
    {
        [Header("Вентиль")]
        [Tooltip("Меш колеса вентиля — крутится при открытии. Пусто — не крутится")]
        [SerializeField] private Transform _valveWheel;
        [Tooltip("Ось вращения колеса (в его осях)")]
        [SerializeField] private Vector3 _wheelAxis = Vector3.forward;
        [Tooltip("На сколько градусов проворачивается от закрытого до открытого")]
        [SerializeField] private float _valveTurn = 720f;
        [Tooltip("За сколько секунд открывается полностью")]
        [Min(0.1f)][SerializeField] private float _valveSeconds = 1.2f;

        [Header("Азот")]
        [Tooltip("Откуда выходит шланг (и пар при открытии). Пусто — верх баллона")]
        [SerializeField] private Transform _outlet;
        [Tooltip("Пар из-под колеса, пока вентиль открыт")]
        [SerializeField] private bool _hissFx = true;

        [Header("v19.5: редуктор (как у баллона кровельной горелки)")]
        [Tooltip("Давление 1…5 при старте. Колесо мыши с пистолетом в руке — меняет: струя длиннее и холоднее")]
        [Range(1, 5)][SerializeField] private int _startPressure = 3;
        [Tooltip("Стрелка манометра (крутится по давлению). Пусто — нет")]
        [SerializeField] private Transform _needle;
        [Tooltip("Ось стрелки (в её осях)")]
        [SerializeField] private Vector3 _needleAxis = Vector3.forward;
        [Tooltip("Ход стрелки от 1 до 5, градусы")]
        [SerializeField] private float _needleSweep = 220f;

        private readonly SyncVar<byte> _open = new SyncVar<byte>(0);
        private readonly SyncVar<byte> _pressure = new SyncVar<byte>(3);
        private Quaternion _needleBase;
        private bool _hasNeedle;
        private float _needleShown = -1f;

        /// <summary>Давление 1…5.</summary>
        public int Pressure => Mathf.Clamp(_pressure.Value, 1, 5);
        /// <summary>Множитель струи: 1 → 0.6, 3 → 1, 5 → 1.45.</summary>
        public float PressureK => Pressure <= 3 ? Mathf.Lerp(0.6f, 1f, (Pressure - 1) / 2f) : Mathf.Lerp(1f, 1.45f, (Pressure - 3) / 2f);

        public override void OnStartServer()
        {
            base.OnStartServer();
            _pressure.Value = (byte)Mathf.Clamp(_startPressure, 1, 5);
        }

        [Server]
        public void ServerAddPressure(int delta)
        {
            int p = Mathf.Clamp(Pressure + delta, 1, 5);
            if (p == Pressure) return;
            _pressure.Value = (byte)p;
            Debug.Log($"[Азот] давление {p}/5", this);
        }
        private float _open01, _target, _hissT;
        private Quaternion _wheelBase;
        private bool _hasWheel;

        public float Open01 => _open.Value / 255f;
        public bool IsOpen => _open.Value > 200;
        public Transform Outlet => _outlet != null ? _outlet : transform;

        protected override void Awake()
        {
            base.Awake();
            _canPickup = true;
            if (_valveWheel != null) { _wheelBase = _valveWheel.localRotation; _hasWheel = true; }
            if (_needle != null) { _needleBase = _needle.localRotation; _hasNeedle = true; }
        }

        [Server]
        public void ServerToggleValve()
        {
            _target = _target > 0.5f ? 0f : 1f;
            Debug.Log($"[Азот] вентиль {(_target > 0.5f ? "открывают" : "закрывают")}", this);
        }

        private void Update()
        {
            if (base.IsServerInitialized)
            {
                _open01 = Mathf.MoveTowards(_open01, _target, Time.deltaTime / _valveSeconds);
                byte o = (byte)Mathf.RoundToInt(_open01 * 255f);
                if (o != _open.Value && (o == 0 || o == 255 || Mathf.Abs(o - _open.Value) >= 8)) _open.Value = o;
            }
            if (_hasWheel)
            {
                Vector3 ax = _wheelAxis.sqrMagnitude > 1e-6f ? _wheelAxis.normalized : Vector3.forward;
                _valveWheel.localRotation = _wheelBase * Quaternion.AngleAxis(Open01 * _valveTurn, ax);
            }
            if (_hasNeedle)
            {
                float want = (Pressure - 1) / 4f * (0.35f + 0.65f * Open01);
                _needleShown = _needleShown < 0f ? want : Mathf.Lerp(_needleShown, want, 1f - Mathf.Exp(-Time.deltaTime * 6f));
                Vector3 nax = _needleAxis.sqrMagnitude > 1e-6f ? _needleAxis.normalized : Vector3.forward;
                _needle.localRotation = _needleBase * Quaternion.AngleAxis(_needleShown * _needleSweep, nax);
            }
            if (_hissFx && Open01 > 0.05f)
            {
                _hissT -= Time.deltaTime;
                if (_hissT <= 0f)
                {
                    _hissT = Random.Range(0.25f, 0.6f);
                    NitroFx.Vapor(Outlet.position, Outlet.up * 0.3f + Random.insideUnitSphere * 0.1f, 0.35f * Open01);
                }
            }
        }
    }
}
