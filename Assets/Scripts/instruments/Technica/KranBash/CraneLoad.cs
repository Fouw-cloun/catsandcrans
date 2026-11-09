using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>На корень сетевого груза. Свободный груз считает сервер; взятый в руку следует PlayerHand.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject), typeof(Rigidbody))]
public sealed class CraneLoad : NetworkBehaviour
{
    public static readonly HashSet<CraneLoad> Active = new HashSet<CraneLoad>();
    [Tooltip("Скрипты, которые двигают груз: отключаются на время подвеса. Не добавляй NetworkTransform/CraneLoad.")]
    [SerializeField] private Behaviour[] _pauseWhileAttached;
    [SerializeField, Min(0f)] private float _linearDamping = 0.08f;
    [SerializeField, Min(0f)] private float _angularDamping = 0.35f;
    public struct AttachmentState
    {
        public bool Attached;
        public NetworkObject Hook;
    }
    private readonly SyncVar<AttachmentState> _attachment = new(new SyncTypeSettings(0f));
    private Rigidbody _body;
    private CraneLiftPoint[] _points;
    private ConfigurableJoint _joint;
    private CraneHook _serverHook;
    private bool[] _previousEnabled;
    private bool _paused;
    private CooperativeCarry _carry;
    private bool _carryEnabled;
    public bool IsAttached => _attachment.Value.Attached;
    public Rigidbody Body => _body;
    public CraneLiftPoint[] Points => _points ?? (_points = GetComponentsInChildren<CraneLiftPoint>(true));
    public CraneHook Hook => _attachment.Value.Hook != null ? _attachment.Value.Hook.GetComponent<CraneHook>() : null;

    private void Awake()
    {
        _body = GetComponent<Rigidbody>();
        _carry = GetComponent<CooperativeCarry>();
        _points = GetComponentsInChildren<CraneLiftPoint>(true);
    }
    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        Active.Add(this);
        _attachment.OnChange += HookChanged;
        RefreshState();
    }
    public override void OnStartClient() { base.OnStartClient(); RefreshState(); }
    public override void OnStartServer() { base.OnStartServer(); RefreshState(); }
    public override void OnOwnershipClient(NetworkConnection previous) { base.OnOwnershipClient(previous); RefreshState(); }
    public override void OnOwnershipServer(NetworkConnection previous) { base.OnOwnershipServer(previous); RefreshState(); }
    private void HookChanged(AttachmentState prev, AttachmentState next, bool asServer) => RefreshState();

    private void RefreshState()
    {
        if (_body == null) return;
        bool simulate = IsServerInitialized && (IsAttached || Owner == null || !Owner.IsValid);
        if (!simulate) _body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        _body.isKinematic = !simulate;
        if (simulate) _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _body.useGravity = simulate;
        _body.interpolation = simulate ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        _body.solverIterations = Mathf.Max(_body.solverIterations, 16);
        _body.solverVelocityIterations = Mathf.Max(_body.solverVelocityIterations, 8);
        _body.linearDamping = _linearDamping;
        _body.angularDamping = _angularDamping;
        if (IsAttached == _paused) return;
        if (IsAttached)
        {
            _previousEnabled = new bool[_pauseWhileAttached == null ? 0 : _pauseWhileAttached.Length];
            for (int i = 0; i < _previousEnabled.Length; i++)
            {
                Behaviour b = _pauseWhileAttached[i];
                if (b == null || b == this || b == _carry || b is FishNet.Component.Transforming.NetworkTransform) continue;
                _previousEnabled[i] = b.enabled;
                b.enabled = false;
            }
            if (_carry != null) { _carryEnabled = _carry.enabled; _carry.enabled = false; }
        }
        else RestoreBehaviours();
        _paused = IsAttached;
    }

    public bool CanAttach(CraneHook hook)
    {
        return isActiveAndEnabled && IsSpawned && !IsAttached && _body != null &&
            (!_body.isKinematic || FindDock() != null) && (Owner == null || !Owner.IsValid) &&
            (_carry == null || (!_carry.IsCarried && !_carry.NeedsMore)) &&
            hook != null && hook.gameObject.scene == gameObject.scene && _body.mass <= hook.MaxLoadMass;
    }

    private CatCooks.Food.CookwareDock FindDock()
    {
        foreach (CatCooks.Food.CookwareDock dock in FindObjectsByType<CatCooks.Food.CookwareDock>(FindObjectsSortMode.None))
            if (dock.HasCraneClaim(_body)) return dock;
        return null;
    }

    public bool ServerAttach(CraneHook hook, CraneLiftPoint point)
    {
        if (!IsServerInitialized || point == null || point.Load != this || !CanAttach(hook)) return false;
        if (Vector3.Distance(hook.LatchWorld, point.WorldPoint) > point.WorldRadius) return false;
        if ((_body.GetPointVelocity(point.WorldPoint) - hook.Body.GetPointVelocity(hook.LatchWorld)).magnitude > hook.MaxCaptureSpeed) return false;
        Vector3 gap = point.WorldPoint - hook.LatchWorld;
        if (gap.sqrMagnitude > 0.0001f)
            foreach (RaycastHit hit in Physics.RaycastAll(hook.LatchWorld, gap.normalized, gap.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(hook.transform)) return false;
        CatCooks.Food.CookwareDock dock = FindDock();
        if (dock != null) dock.ReleaseForCrane(_body);
        _serverHook = hook;
        _attachment.Value = new AttachmentState { Attached = true, Hook = hook.NetworkObject };
        RefreshState();
        _joint = gameObject.AddComponent<ConfigurableJoint>();
        _joint.autoConfigureConnectedAnchor = false;
        _joint.connectedBody = hook.Body;
        _joint.anchor = transform.InverseTransformPoint(point.WorldPoint);
        _joint.connectedAnchor = hook.transform.InverseTransformPoint(hook.LatchWorld);
        _joint.xMotion = _joint.yMotion = _joint.zMotion = ConfigurableJointMotion.Locked;
        _joint.angularXMotion = _joint.angularYMotion = _joint.angularZMotion =
            point.LockRotation ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Free;
        _joint.enableCollision = false;
        _joint.projectionMode = JointProjectionMode.PositionAndRotation;
        _joint.projectionDistance = 0.08f;
        _joint.projectionAngle = 15f;
        _joint.breakForce = Mathf.Infinity;
        _joint.breakTorque = Mathf.Infinity;
        _body.WakeUp();
        return true;
    }

    public void ServerDetach()
    {
        if (!IsServerInitialized) return;
        ReleaseJoint();
        _attachment.Value = new AttachmentState();
        _serverHook = null;
        RefreshState(); // скорость сохраняется: груз продолжает лететь/падать
    }
    private void ReleaseJoint()
    {
        if (_joint == null) return;
        _joint.xMotion = _joint.yMotion = _joint.zMotion = ConfigurableJointMotion.Free;
        _joint.angularXMotion = _joint.angularYMotion = _joint.angularZMotion = ConfigurableJointMotion.Free;
        Destroy(_joint);
        _joint = null;
    }
    private void FixedUpdate()
    {
        if (IsServerInitialized && IsAttached && (_serverHook == null || !_serverHook.IsSpawned || _joint == null))
            ServerDetach();
    }
    private void RestoreBehaviours()
    {
        if (_previousEnabled != null && _pauseWhileAttached != null)
            for (int i = 0; i < Mathf.Min(_previousEnabled.Length, _pauseWhileAttached.Length); i++)
            {
                Behaviour b = _pauseWhileAttached[i];
                if (b != null && b != this && b != _carry && !(b is FishNet.Component.Transforming.NetworkTransform)) b.enabled = _previousEnabled[i];
            }
        if (_carry != null && _paused) _carry.enabled = _carryEnabled;
    }
    public override void OnStopServer()
    {
        if (_serverHook != null) _serverHook.ServerDetach();
        _serverHook = null;
        ReleaseJoint();
        base.OnStopServer();
    }
    public override void OnStopNetwork()
    {
        Active.Remove(this);
        if (IsServerInitialized && _serverHook != null) _serverHook.ServerDetach();
        ReleaseJoint();
        if (_paused) RestoreBehaviours();
        _paused = false;
        _attachment.OnChange -= HookChanged;
        base.OnStopNetwork();
    }
}
