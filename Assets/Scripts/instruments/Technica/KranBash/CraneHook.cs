using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>Отдельный сетевой крюк. NetworkTransform: Server Authoritative, Component Configuration Disabled.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject), typeof(Rigidbody), typeof(ConfigurableJoint))]
public sealed class CraneHook : NetworkBehaviour
{
    public static readonly HashSet<CraneHook> Active = new HashSet<CraneHook>();
    [Tooltip("Точка зацепа снизу крюка. Пусто — центр объекта")]
    [SerializeField] private Transform _latchPoint;
    [Tooltip("Выход троса сверху крюка. Пусто — anchor существующего Joint")]
    [SerializeField] private Transform _ropeAnchor;
    [Min(1f)][SerializeField] private float _maxLoadMass = 300f;
    [Min(0.1f)][SerializeField] private float _maxCaptureSpeed = 2f;
    [Min(0.1f)][SerializeField] private float _riderReach = 2f;
    [SerializeField] private bool _autoAttach;
    [Min(0f)][SerializeField] private float _linearDamping = 0.08f;
    [Min(0f)][SerializeField] private float _angularDamping = 0.5f;
    private readonly SyncVar<bool> _occupied = new(new SyncTypeSettings(0f));
    private Rigidbody _body, _topBody;
    private ConfigurableJoint _ropeJoint;
    private CraneLoad _load;
    private CraneRider _rider;
    private float _recaptureAt, _nextScan;
    public Rigidbody Body => _body;
    public float MaxLoadMass => _maxLoadMass;
    public float MaxCaptureSpeed => _maxCaptureSpeed;
    public bool Occupied => _occupied.Value;
    public Vector3 LatchWorld => _latchPoint != null ? _latchPoint.position : transform.position;
    public Vector3 RopeAnchorWorld => _ropeAnchor != null ? _ropeAnchor.position :
        transform.TransformPoint(_ropeJoint != null ? _ropeJoint.anchor : Vector3.zero);

    private void Awake()
    {
        _body = GetComponent<Rigidbody>();
        _ropeJoint = GetComponent<ConfigurableJoint>();
    }
    public override void OnStartNetwork()
    {
        base.OnStartNetwork(); Active.Add(this); RefreshPhysics();
    }
    public override void OnStartServer() { base.OnStartServer(); RefreshPhysics(); }
    public override void OnStartClient() { base.OnStartClient(); RefreshPhysics(); }
    private void RefreshPhysics()
    {
        if (!IsServerInitialized) _body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        _body.isKinematic = !IsServerInitialized;
        if (IsServerInitialized) _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _body.useGravity = IsServerInitialized;
        _body.interpolation = IsServerInitialized ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        _body.linearDamping = _linearDamping;
        _body.angularDamping = _angularDamping;
        _body.solverIterations = Mathf.Max(_body.solverIterations, 20);
        _body.solverVelocityIterations = Mathf.Max(_body.solverVelocityIterations, 10);
    }

    public void ServerSetSuspension(Vector3 top, float length)
    {
        if (!IsServerInitialized || !CraneMath.Finite(top) || !CraneMath.Finite(length)) return;
        if (_topBody == null)
        {
            GameObject anchor = new GameObject(name + "_ServerRopeAnchor");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(anchor, gameObject.scene);
            anchor.transform.position = top;
            _topBody = anchor.AddComponent<Rigidbody>();
            _topBody.isKinematic = true;
            _topBody.useGravity = false;
            _ropeJoint.autoConfigureConnectedAnchor = false;
            _ropeJoint.connectedBody = _topBody;
            _ropeJoint.connectedAnchor = Vector3.zero;
            _ropeJoint.configuredInWorldSpace = false;
            _ropeJoint.swapBodies = false;
            if (_ropeAnchor != null) _ropeJoint.anchor = transform.InverseTransformPoint(_ropeAnchor.position);
            _ropeJoint.xMotion = _ropeJoint.yMotion = _ropeJoint.zMotion = ConfigurableJointMotion.Limited;
            _ropeJoint.angularXMotion = _ropeJoint.angularYMotion = _ropeJoint.angularZMotion = ConfigurableJointMotion.Free;
            _ropeJoint.linearLimitSpring = new SoftJointLimitSpring { spring = 0f, damper = 0f };
            _ropeJoint.xDrive = _ropeJoint.yDrive = _ropeJoint.zDrive = new JointDrive();
            _ropeJoint.angularXDrive = _ropeJoint.angularYZDrive = _ropeJoint.slerpDrive = new JointDrive();
            _ropeJoint.projectionMode = JointProjectionMode.PositionAndRotation;
            _ropeJoint.projectionDistance = 0.12f;
            _ropeJoint.enableCollision = false;
            _ropeJoint.breakForce = _ropeJoint.breakTorque = Mathf.Infinity;
        }
        _topBody.MovePosition(top);
        SoftJointLimit limit = _ropeJoint.linearLimit;
        limit.limit = Mathf.Max(0.1f, length);
        limit.bounciness = 0f;
        limit.contactDistance = 0.01f;
        _ropeJoint.linearLimit = limit;
    }

    public bool ServerAttach(CraneLiftPoint point)
    {
        if (!IsServerInitialized || Occupied || Time.unscaledTime < _recaptureAt || point == null || !point.isActiveAndEnabled) return false;
        CraneLoad load = point.Load;
        if (load == null || !load.ServerAttach(this, point)) return false;
        _load = load;
        _occupied.Value = true;
        return true;
    }
    public bool ServerAttachRider(CraneRider rider)
    {
        if (!IsServerInitialized || Occupied || rider == null || rider.gameObject.scene != gameObject.scene || Time.unscaledTime < _recaptureAt) return false;
        if (Vector3.Distance(LatchWorld, rider.HarnessWorld) > _riderReach || rider.Mass > _maxLoadMass) return false;
        if (!rider.ServerHang(this)) return false;
        _rider = rider;
        _occupied.Value = true;
        return true;
    }
    public void ServerDetach()
    {
        if (!IsServerInitialized) return;
        CraneLoad load = _load; CraneRider rider = _rider;
        _load = null; _rider = null;
        _occupied.Value = false;
        _recaptureAt = Time.unscaledTime + 1f;
        if (load != null) load.ServerDetach();
        if (rider != null) rider.ServerRelease();
    }
    public void ServerToggleNearest()
    {
        if (!IsServerInitialized) return;
        if (Occupied) ServerDetach(); else AttachNearest();
    }
    private void AttachNearest()
    {
        if (Time.unscaledTime < _recaptureAt) return;
        CraneLiftPoint best = null; float closest = float.MaxValue;
        foreach (CraneLoad load in CraneLoad.Active)
        {
            if (load == null || !load.CanAttach(this)) continue;
            foreach (CraneLiftPoint point in load.Points)
            {
                if (point == null || !point.isActiveAndEnabled) continue;
                float d = Vector3.Distance(LatchWorld, point.WorldPoint);
                if (d <= point.WorldRadius && d < closest) { closest = d; best = point; }
            }
        }
        if (best != null) ServerAttach(best);
    }
    private void FixedUpdate()
    {
        if (!IsServerInitialized) return;
        if ((_load != null && (!_load.IsSpawned || !_load.IsAttached)) ||
            (_rider != null && (!_rider.IsSpawned || !_rider.IsHanging)) ||
            (Occupied && _load == null && _rider == null)) ServerDetach();
        if (_autoAttach && !Occupied && Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 0.1f;
            AttachNearest();
        }
    }
    public override void OnStopServer()
    {
        ServerDetach();
        if (_topBody != null) Destroy(_topBody.gameObject);
        _topBody = null;
        base.OnStopServer();
    }
    public override void OnStopNetwork() { Active.Remove(this); base.OnStopNetwork(); }
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(LatchWorld, 0.12f);
    }
}
