using UnityEngine;
using UnityEngine.SceneManagement;

// The chassis alone owns the compound body. Boom, telescope, ram and feet are visual children.
public sealed class CCWorkshopCraneChassis : MobileCraneChassis
{
    public CraneState state;
    public CraneOperatorSeat operatorSeat;
    public Transform[] feet;
    public Transform barrel, rod, barrelPin, rodPin;
    private Vector3[] footRest;
    private Quaternion barrelRest, rodRest;
    private Vector3 barrelAxis, rodAxis;
    private RigidbodyConstraints freeConstraints;
    private readonly RaycastHit[] hits=new RaycastHit[32];
    private bool locked;
    private Vector3 lastVisualPosition, lastVisualForward;
    private bool visualPoseReady;
    protected override void Awake()
    {
        base.Awake();
        freeConstraints=_body.constraints;
        footRest=new Vector3[feet.Length];
        for(int i=0;i<feet.Length;i++) footRest[i]=feet[i].localPosition;
        Vector3 axis=(rodPin.position-barrelPin.position).normalized;
        barrelRest=barrel.localRotation; rodRest=rod.localRotation;
        barrelAxis=barrel.parent.InverseTransformDirection(axis);
        rodAxis=rod.parent.InverseTransformDirection(axis);
    }

    protected override bool CanEnter(PlayerInteractor player) => !operatorSeat.HasDriver;
    protected override bool WheelPhysicsActive => !state.IsDeployed || !locked;

    protected override bool WheelGroundCast(Vector3 origin,Vector3 dir,float length,out RaycastHit hit)
    {
        var scene=gameObject.scene.GetPhysicsScene();
        int n=scene.Raycast(origin,dir,hits,length,groundMask,QueryTriggerInteraction.Ignore);
        hit=default;
        if(n==hits.Length) return false;
        float closest=float.PositiveInfinity;
        for(int i=0;i<n;i++)
        {
            var h=hits[i];
            if(h.transform.IsChildOf(transform)||h.distance>=closest||Vector3.Dot(h.normal,-dir)<.50f) continue;
            if(h.collider.GetComponentInParent<CharacterController>()) continue;
            if(h.rigidbody && !h.rigidbody.isKinematic) continue;
            closest=h.distance; hit=h;
        }
        return hit.collider!=null;
    }

    private bool FootDepth(int i,out float depth)
    {
        Transform foot=feet[i];
        Vector3 start=foot.parent.TransformPoint(footRest[i])+transform.up*.12f;
        if(WheelGroundCast(start,-transform.up,1.02f,out var hit))
        {
            depth=Mathf.Max(0f,hit.distance-.12f);
            return true;
        }
        depth=0f; return false;
    }
    private bool PadsSupported()
    {
        if(feet.Length!=4 || transform.up.y<.92f) return false;
        for(int i=0;i<feet.Length;i++) if(!FootDepth(i,out _)) return false;
        return true;
    }
    private bool TiresSupported()
    {
        int count=0;
        foreach(var w in wheels)
            if(WheelGroundCast(w.anchor.position,-transform.up,restLength+wheelRadius-.045f,out _)) count++;
        return count>=4;
    }
    public bool CanDeploy() => _body && _body.linearVelocity.sqrMagnitude<.16f &&
        _body.angularVelocity.sqrMagnitude<.09f && PadsSupported();

    protected override void VehicleFixedUpdate()
    {
        bool operating=state.IsDeployed;
        bool drive=IsDriving && Mathf.Abs(Input.GetAxisRaw("Vertical"))>.01f;
        bool slow=_body.linearVelocity.sqrMagnitude<.0324f && _body.angularVelocity.sqrMagnitude<.04f;
        bool hold=operating ? PadsSupported() : !drive && slow && TiresSupported();
        if(hold!=locked)
        {
            locked=hold;
            if(hold){_body.linearVelocity=Vector3.zero; _body.angularVelocity=Vector3.zero;}
            _body.constraints=hold?RigidbodyConstraints.FreezeAll:freeConstraints;
            if(!hold) _body.WakeUp();
        }
        if(!locked)
        {
            base.VehicleFixedUpdate();
            Vector3 horizontal=Vector3.ProjectOnPlane(_body.linearVelocity,transform.up);
            float cap=Vector3.Dot(horizontal,transform.forward)<0f ? 2.7f : 5.6f;
            if(horizontal.sqrMagnitude>cap*cap) _body.linearVelocity+=horizontal.normalized*cap-horizontal;
        }
        if(locked && !operating)
            foreach(var w in wheels)
                if(w.visual && WheelGroundCast(w.anchor.position,-transform.up,restLength+wheelRadius+.10f,out var ground))
                    w.visual.position=ground.point+transform.up*wheelRadius;
        if(IsDriving && Input.GetKey(KeyCode.Space) && !locked)
            _body.AddForce(-Vector3.ClampMagnitude(Vector3.ProjectOnPlane(_body.linearVelocity,transform.up)/Time.fixedDeltaTime,8f),ForceMode.Acceleration);
    }

    protected override void ApplyVisuals()
    {
        if(HasMoveAuthority) base.ApplyVisuals();
        else
        {
            float dt=Mathf.Max(.001f,Time.deltaTime);
            Vector3 delta=transform.position-lastVisualPosition;
            float travel=visualPoseReady && delta.sqrMagnitude<4f ? Vector3.Dot(delta,transform.forward) : 0f;
            float yaw=visualPoseReady ? Vector3.SignedAngle(lastVisualForward,transform.forward,transform.up)*Mathf.Deg2Rad : 0f;
            float steer=Mathf.Abs(travel)>.003f ? Mathf.Clamp(Mathf.Atan(4.1f*yaw/travel)*Mathf.Rad2Deg,-maxSteerAngle,maxSteerAngle) : 0f;
            foreach(var w in wheels)
            {
                if(!w.visual) continue;
                w.spin+=travel*Mathf.Rad2Deg/wheelRadius;
                w.steerAngle=Mathf.MoveTowards(w.steerAngle,w.steering?steer:0f,90f*dt);
                w.visual.localRotation=Quaternion.AngleAxis(w.steerAngle,Vector3.up)*Quaternion.AngleAxis(w.spin,Vector3.right);
                if(WheelGroundCast(w.anchor.position,-transform.up,restLength+wheelRadius+.1f,out var h))
                    w.visual.position=h.point+transform.up*wheelRadius;
            }
        }
        lastVisualPosition=transform.position;lastVisualForward=transform.forward;visualPoseReady=true;
        for(int i=0;i<feet.Length;i++)
        {
            float depth=0;
            if(state.IsDeployed) FootDepth(i,out depth);
            feet[i].localPosition=Vector3.MoveTowards(feet[i].localPosition,
                footRest[i]-Vector3.up*depth,Time.deltaTime*.9f);
        }
    }

    private void LateUpdate()
    {
        Vector3 axis=(rodPin.position-barrelPin.position).normalized;
        if(axis.sqrMagnitude<.5f) return;
        barrel.localRotation=Quaternion.FromToRotation(barrelAxis,barrel.parent.InverseTransformDirection(axis))*barrelRest;
        rod.localRotation=Quaternion.FromToRotation(rodAxis,rod.parent.InverseTransformDirection(axis))*rodRest;
        // Projected metal lengths measured in the supplied FBX. Keep 10 cm of rod inside the barrel.
        float distance=Vector3.Distance(barrelPin.position,rodPin.position);
        rod.localScale=new Vector3(1f,1f,Mathf.Max(1f,(distance-2.285f+.10f)/2.074f));
    }
}
