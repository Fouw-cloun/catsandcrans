using System;
using UnityEngine;

[DisallowMultipleComponent, DefaultExecutionOrder(850)]
[AddComponentMenu("CatCooks/Dust and smoke/Wheel trails")]
public sealed class CatCooksDustWheels : MonoBehaviour
{
    [Serializable] public sealed class Wheel
    {
        [Tooltip("Wheel centre or suspension anchor. Set probeDistance to the actual ground reach.")]
        public Transform point;
        public WheelCollider wheelCollider;
        [Min(.05f)] public float probeDistance = .5f;
        [Range(0,2)] public float extraSlip;
    }
    public CatCooksDustSystem system;
    public CatCooksDustProfile fallbackProfile;
    public Transform ignoreRoot;
    public LayerMask groundMask = ~0;
    public Wheel[] wheels=Array.Empty<Wheel>();
    [Range(.1f,2)] public float metresPerPuff=.4f;
    [Min(.05f)] public float minimumSpeed=.3f;
    [Range(.1f,4)] public float effectScale=1;
    [Min(1)] public float teleportDistance=5;
    sealed class State
    {
        public bool initialised, grounded;
        public Vector3 lastPoint, lastContact, supportLocalPoint;
        public Transform support;
        public CatCooksDustUtility.DistanceClock clock;
    }
    State[] states;
    readonly RaycastHit[] hits=new RaycastHit[32];
    void Reset() => ignoreRoot=transform;
    void OnEnable() => ResetTracking();
    public void ResetTracking()
    {
        states=new State[wheels==null ? 0 : wheels.Length];
        for(int i=0;i<states.Length;i++) states[i]=new State();
    }
    public void SetSlip(int index,float slip)
    { if(wheels!=null && index>=0 && index<wheels.Length && wheels[index]!=null) wheels[index].extraSlip=CatCooksDustUtility.Safe(slip,0,2,0); }
    void LateUpdate()
    {
        if(wheels==null) return;
        if(states==null || states.Length!=wheels.Length) ResetTracking();
        float dt=Time.deltaTime; if(dt<=.00001f) return;
        var world=system!=null ? system : CatCooksDustSystem.Active;
        if(world==null) return;
        for(int i=0;i<wheels.Length;i++)
        {
            Wheel w=wheels[i]; State s=states[i];
            if(w==null || (w.point==null && w.wheelCollider==null)) continue;
            Vector3 point=w.point!=null ? w.point.position : w.wheelCollider.transform.TransformPoint(w.wheelCollider.center);
            bool grounded=false; Vector3 contact=point, normal=Vector3.up; Collider surface=null;
            float slip=Mathf.Clamp(w.extraSlip,0,2);
            if(w.wheelCollider!=null)
            {
                grounded=w.wheelCollider.GetGroundHit(out WheelHit hit);
                if(grounded) { contact=hit.point; normal=hit.normal; surface=hit.collider; slip+=Mathf.Abs(hit.sidewaysSlip)+Mathf.Abs(hit.forwardSlip); }
            }
            else
            {
                grounded=CatCooksDustUtility.Ground(point+Vector3.up*.08f,Vector3.down,
                    Mathf.Max(.05f,w.probeDistance)+.08f,groundMask,ignoreRoot!=null ? ignoreRoot : transform,hits,out RaycastHit hit);
                if(grounded) { contact=hit.point; normal=hit.normal; surface=hit.collider; }
            }
            grounded=grounded && surface!=null && normal.y>.25f && (groundMask.value&(1<<surface.gameObject.layer))!=0;
            if(!s.initialised || dt>.25f || (point-s.lastPoint).sqrMagnitude>teleportDistance*teleportDistance)
            { s.initialised=true; s.clock.Reset(); Remember(s,point,contact,surface,grounded); continue; }
            Vector3 delta=point-s.lastPoint;
            if(grounded && s.grounded && s.support!=null && s.support==surface.transform)
                delta-=s.support.TransformPoint(s.supportLocalPoint)-s.lastPoint;
            Vector3 planar=Vector3.ProjectOnPlane(delta,normal); float speed=planar.magnitude/dt;
            if(grounded && s.grounded && (speed>=minimumSpeed || slip>.15f))
            {
                var profile=CatCooksDustUtility.Surface(surface,fallbackProfile,out float amount);
                if(amount>0)
                {
                    float slipDistance=Mathf.Clamp(slip,0,2)*dt*1.4f;
                    int n=s.clock.Advance(planar.magnitude+slipDistance,metresPerPuff,2);
                    for(int k=0;k<n;k++)
                    {
                        Vector3 at=Vector3.Lerp(s.lastContact,contact,(k+1f)/n);
                        world.Emit(slip>.35f ? CatCooksDustSystem.Effect.Skid : CatCooksDustSystem.Effect.Wheel,
                            at,normal,planar/dt,Mathf.Clamp(.65f+speed*.045f+slip*.35f,.65f,2),profile,effectScale,amount);
                    }
                }
                else s.clock.Reset();
            }
            else s.clock.Reset();
            Remember(s,point,contact,surface,grounded);
        }
    }
    static void Remember(State s,Vector3 point,Vector3 contact,Collider collider,bool grounded)
    {
        s.lastPoint=point; s.lastContact=contact; s.grounded=grounded;
        s.support=grounded && collider!=null ? collider.transform : null;
        if(s.support!=null) s.supportLocalPoint=s.support.InverseTransformPoint(point);
    }
}
