using UnityEngine;

[DisallowMultipleComponent, DefaultExecutionOrder(800)]
[AddComponentMenu("CatCooks/Dust and smoke/Player footsteps and landing")]
public sealed class CatCooksDustPlayer : MonoBehaviour
{
    public enum StepMode { Distance, AnimationEvents, Manual }
    public CatCooksDustSystem system;
    public CatCooksDustProfile fallbackProfile;
    public StepMode mode = StepMode.Distance;
    public Transform feet, leftFoot, rightFoot;
    [Tooltip("Root whose own colliders should be ignored; normally the player root.")]
    public Transform ignoreRoot;
    public LayerMask groundMask = ~0;
    public bool automaticJumpAndLanding = true;
    [Range(.15f,2)] public float walkStepDistance = .65f;
    [Range(.2f,2)] public float runStepDistance = .9f;
    [Min(.1f)] public float runSpeed = 4;
    [Min(0)] public float minimumMoveSpeed = .18f;
    [Min(.1f)] public float minimumLandingSpeed = 1.3f;
    [Range(.1f,4)] public float effectScale = 1;
    [Range(.02f,.3f)] public float groundTolerance = .10f;
    [Min(1)] public float teleportDistance = 4;
    readonly RaycastHit[] hits = new RaycastHit[32];
    CharacterController controller;
    CatCooksDustUtility.DistanceClock steps;
    Vector3 lastPosition, lastFeet, supportLocalFeet, velocity;
    Transform support;
    RaycastHit lastGround;
    bool initialised, grounded;
    float airTime, fallSpeed;
    int nextFoot;

    void Reset() { ignoreRoot=transform; controller=GetComponent<CharacterController>(); }
    void OnEnable() { controller=GetComponent<CharacterController>(); ResetTracking(); }
    public void ResetTracking()
    { initialised=false; grounded=false; airTime=fallSpeed=0; steps.Reset(); support=null; }
    Vector3 FeetPosition()
    {
        if (feet!=null) return feet.position;
        if (controller!=null)
            return transform.TransformPoint(controller.center-Vector3.up*controller.height*.5f);
        return transform.position; // For a different pivot assign a Feet transform.
    }
    bool Probe(Vector3 foot, out RaycastHit hit)
    {
        bool found=CatCooksDustUtility.Ground(foot+Vector3.up*.25f,Vector3.down,.25f+groundTolerance,
            groundMask,ignoreRoot!=null ? ignoreRoot : transform,hits,out hit);
        return found && hit.normal.y>.45f;
    }
    void LateUpdate()
    {
        if (mode==StepMode.Manual) return;
        float dt=Time.deltaTime;
        if (dt<=.00001f) return;
        Vector3 point=FeetPosition(); Vector3 position=transform.position;
        bool contact=Probe(point,out RaycastHit hit);
        if (!initialised || dt>.25f || !CatCooksDustUtility.Finite(position)
            || (position-lastPosition).sqrMagnitude>teleportDistance*teleportDistance)
        {
            ResetTracking(); initialised=true; grounded=contact;
            lastPosition=position; lastFeet=point;
            RememberGround(contact,hit,point); return;
        }
        Vector3 delta=position-lastPosition; lastPosition=position;
        // Remove movement carried by a translating or rotating platform.
        if (contact && grounded && support!=null && hit.collider.transform==support)
            delta-=support.TransformPoint(supportLocalFeet)-lastFeet;
        velocity=delta/dt;
        bool nowGrounded=contact && Vector3.Dot(velocity,hit.normal)<.8f;
        if (!nowGrounded)
        {
            if (grounded && automaticJumpAndLanding && velocity.y>.9f)
                Emit(CatCooksDustSystem.Effect.Jump,lastGround,1);
            airTime+=dt; fallSpeed=Mathf.Max(fallSpeed,-velocity.y); steps.Reset();
        }
        else
        {
            bool landed=!grounded && airTime>.075f && fallSpeed>=minimumLandingSpeed;
            if (landed && automaticJumpAndLanding)
                Emit(CatCooksDustSystem.Effect.Land,hit,Mathf.Lerp(.65f,2.5f,Mathf.InverseLerp(minimumLandingSpeed,10,fallSpeed)));
            float distance=Vector3.ProjectOnPlane(delta,hit.normal).magnitude;
            float speed=distance/dt;
            if (grounded && mode==StepMode.Distance && speed>=minimumMoveSpeed)
            {
                int n=steps.Advance(distance,speed>=runSpeed ? runStepDistance : walkStepDistance,3);
                for(int i=0;i<n;i++) Footstep(nextFoot++ & 1);
            }
            else if(speed<minimumMoveSpeed || landed) steps.Reset();
            airTime=fallSpeed=0;
        }
        grounded=nowGrounded; lastFeet=point; RememberGround(nowGrounded,hit,point);
    }
    void RememberGround(bool valid,RaycastHit hit,Vector3 point)
    {
        if (!valid || hit.collider==null) { support=null; return; }
        lastGround=hit; support=hit.collider.transform; supportLocalFeet=support.InverseTransformPoint(point);
    }
    void Emit(CatCooksDustSystem.Effect effect,RaycastHit hit,float strength)
    {
        var world=system!=null ? system : CatCooksDustSystem.Active;
        if (world==null || hit.collider==null) return;
        var profile=CatCooksDustUtility.Surface(hit.collider,fallbackProfile,out float amount);
        world.Emit(effect,hit.point,hit.normal,velocity,strength,profile,effectScale,amount);
    }
    // Animation event on the same GameObject: int argument 0 = left, 1 = right.
    public void Footstep(int foot)
    {
        Transform anchor=(foot&1)==0 ? leftFoot : rightFoot;
        Vector3 p=anchor!=null ? anchor.position : FeetPosition()+transform.right*((foot&1)==0 ? -.13f : .13f);
        if (!Probe(p,out RaycastHit hit) || Vector3.Dot(velocity,hit.normal)>.8f) return;
        float speed=Vector3.ProjectOnPlane(velocity,hit.normal).magnitude;
        Emit(speed>=runSpeed ? CatCooksDustSystem.Effect.Run : CatCooksDustSystem.Effect.Step,hit,1);
    }
    public void JumpDust()
    { if(Probe(FeetPosition(),out RaycastHit hit)) Emit(CatCooksDustSystem.Effect.Jump,hit,1); }
    public void LandingDust(float impactSpeed)
    {
        if(Probe(FeetPosition(),out RaycastHit hit))
            Emit(CatCooksDustSystem.Effect.Land,hit,Mathf.Clamp(impactSpeed/4,.5f,2.5f));
    }
}
