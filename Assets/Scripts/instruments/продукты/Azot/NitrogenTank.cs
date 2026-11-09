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

        private readonly SyncVar<byte> _open = new SyncVar<byte>(0);
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
