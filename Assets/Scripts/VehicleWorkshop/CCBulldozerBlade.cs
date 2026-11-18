using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(160)]
public sealed class CCBulldozerBlade : NetworkBehaviour
{
    [System.Serializable]
    public sealed class Cylinder
    {
        public Transform barrel;
        public Transform rod;
        public Transform basePin;
        public Transform tipPin;
        public Quaternion barrelRest;
        public Quaternion rodRest;
        public Vector3 barrelLine;
        public Vector3 rodLine;

        public void Bind()
        {
            barrelRest = barrel.localRotation;
            rodRest = rod.localRotation;
            // The original model is aligned at rest. Keep the imported roll.
            barrelLine = barrel.parent.InverseTransformVector(tipPin.position - basePin.position).normalized;
            rodLine = rod.parent.InverseTransformVector(basePin.position - tipPin.position).normalized;
        }

        public void Apply()
        {
            if (barrel == null || rod == null || basePin == null || tipPin == null) return;
            Vector3 line = tipPin.position - basePin.position;
            if (line.sqrMagnitude < .000001f) return;
            Aim(barrel, basePin.position, line, barrelRest, barrelLine);
            Aim(rod, tipPin.position, -line, rodRest, rodLine);
        }

        private static void Aim(Transform part, Vector3 pin, Vector3 line, Quaternion rest, Vector3 reference)
        {
            part.position = pin;
            Vector3 direction = part.parent.InverseTransformVector(line).normalized;
            part.localRotation = Quaternion.FromToRotation(reference, direction) * rest;
        }
    }

    public NetworkVehicle vehicle;
    public Transform liftPivot;
    public Transform tiltPivot;
    public Cylinder[] cylinders;
    public Vector2 liftLimits = new Vector2(-3f, 14f);
    public Vector2 tiltLimits = new Vector2(-20f, 22f);
    [Min(1)] public float liftSpeed = 20f;
    [Min(1)] public float tiltSpeed = 29f;
    private readonly SyncVar<Vector2> _pose = new(Vector2.zero);
    private Vector2 _localPose, _displayPose;
    private Quaternion _liftRest, _tiltRest;
    private bool _wasDriver;
    private float _sendAt;
    private Vector2 _lastSent;

    private void Awake()
    {
        if (liftPivot != null) _liftRest = liftPivot.localRotation;
        if (tiltPivot != null) _tiltRest = tiltPivot.localRotation;
    }

    private void LateUpdate()
    {
        if ((!IsClientInitialized && !IsServerInitialized) || vehicle == null || liftPivot == null || tiltPivot == null) return;
        bool driver = vehicle.LocalDriver && IsOwner;
        if (driver && !_wasDriver)
        {
            _localPose = _pose.Value;
            _lastSent = _localPose;
            _sendAt = 0;
        }
        _wasDriver = driver;

        if (driver)
        {
            if (!UiModal.AnyOpen)
            {
                float dt = Mathf.Min(.05f, Time.deltaTime);
                int lift = Press(KeyCode.Q) - Press(KeyCode.E);
                int tilt = Press(KeyCode.R) - Press(KeyCode.T);
                _localPose.x = Mathf.Clamp(_localPose.x + lift * liftSpeed * dt, liftLimits.x, liftLimits.y);
                _localPose.y = Mathf.Clamp(_localPose.y + tilt * tiltSpeed * dt, tiltLimits.x, tiltLimits.y);
            }
            if (Time.unscaledTime >= _sendAt && _localPose != _lastSent)
            {
                _sendAt = Time.unscaledTime + .05f;
                _lastSent = _localPose;
                SetPose(_localPose);
            }
        }

        _displayPose = driver ? _localPose : Vector2.MoveTowards(_displayPose, _pose.Value, 100f * Time.deltaTime);
        liftPivot.localRotation = _liftRest * Quaternion.AngleAxis(-_displayPose.x, Vector3.right);
        tiltPivot.localRotation = _tiltRest * Quaternion.AngleAxis(-_displayPose.y, Vector3.right);
        if (cylinders != null)
            foreach (var cylinder in cylinders)
                cylinder?.Apply();
    }

    private static int Press(KeyCode key) => Input.GetKey(key) ? 1 : 0;

    [ServerRpc]
    private void SetPose(Vector2 value)
    {
        if (float.IsNaN(value.x) || float.IsInfinity(value.x) || float.IsNaN(value.y) || float.IsInfinity(value.y)) return;
        _pose.Value = new Vector2(Mathf.Clamp(value.x, liftLimits.x, liftLimits.y),
            Mathf.Clamp(value.y, tiltLimits.x, tiltLimits.y));
    }
}
