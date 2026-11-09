using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(CatCooksDustEmitter))]
[DefaultExecutionOrder(900)]
[AddComponentMenu("CatCooks/Dust and smoke/Vehicle exhaust")]
public sealed class CatCooksVehicleExhaust : MonoBehaviour
{
    public enum LoadSource { ObservedMovement, Manual }
    [Header("Engine")]
    public bool engineRunning=true;
    public LoadSource loadSource;
    [Range(0,1)] public float engineLoad;
    [Tooltip("Root of the vehicle. If empty, nearest Rigidbody or this transform's root is used.")]
    public Transform vehicleRoot;
    [Min(.5f)] public float fullLoadSpeed=8;
    [Min(.5f)] public float accelerationForFullLoad=3;
    [Range(.1f,2)] public float responseTime=.35f;
    [Header("Idle and acceleration")]
    [Range(.2f,20)] public float idleEventsPerSecond=2;
    [Range(.2f,20)] public float loadedEventsPerSecond=7;
    [Range(.1f,2)] public float idleAmount=.65f;
    [Range(.1f,3)] public float loadedAmount=1.5f;
    [Range(0,.4f)] public float pulseStrength=.16f;
    [Range(.5f,12)] public float pulseFrequency=3;
    [Range(0,1)] public float inheritVehicleVelocity=.8f;
    [Min(.5f)] public float teleportDistance=4;
    public float SmoothedLoad => smoothedLoad;
    CatCooksDustEmitter emitter;
    Transform tracked;
    Vector3 previousPosition;
    float previousSpeed, smoothedLoad, carry, phase;
    bool tracking, wasRunning;

    void Reset()
    {
        var e=GetComponent<CatCooksDustEmitter>();
        if(e!=null) { e.effect=CatCooksDustSystem.Effect.Exhaust; e.playOnEnable=false; }
    }
    void OnEnable() { emitter=GetComponent<CatCooksDustEmitter>(); ResetTracking(); wasRunning=false; }
    void OnDisable() { carry=0; tracking=false; }
    public void ResetTracking() { tracking=false; tracked=null; carry=0; previousSpeed=0; smoothedLoad=0; phase=0; }
    public void SetEngineState(bool running,float load)
    { engineRunning=running; engineLoad=CatCooksDustUtility.Safe(load,0,1,0); loadSource=LoadSource.Manual; }
    public float EventRate(float load,float seconds)
    {
        load=CatCooksDustUtility.Safe(load,0,1,0);
        float pulse=1+Mathf.Sin(seconds*Mathf.PI*2*Mathf.Clamp(pulseFrequency,.5f,12))*Mathf.Clamp(pulseStrength,0,.4f);
        return Mathf.Clamp(Mathf.Lerp(idleEventsPerSecond,loadedEventsPerSecond,load)*pulse,.2f,20);
    }
    void LateUpdate()
    {
        if(emitter==null || !emitter.isActiveAndEnabled) return;
        Transform root=vehicleRoot;
        if(root==null) { var body=GetComponentInParent<Rigidbody>(); root=body!=null ? body.transform : transform.root; }
        Vector3 position=root.position, velocity=Vector3.zero;
        float dt=Time.deltaTime;
        bool valid=CatCooksDustUtility.Finite(position) && dt>0 && dt<=.25f;
        bool reset=!tracking || tracked!=root || !valid;
        if(!reset && (position-previousPosition).sqrMagnitude>Mathf.Pow(Mathf.Max(.5f,teleportDistance),2)) reset=true;
        if(reset)
        { carry=0; previousSpeed=0; tracking=valid; tracked=root; smoothedLoad=0; }
        else velocity=(position-previousPosition)/dt;
        previousPosition=position;
        if(!valid) return;
        float speed=Vector3.ProjectOnPlane(velocity,Vector3.up).magnitude;
        // Only positive speed change raises load; a hard brake should not create a throttle burst.
        float acceleration=reset ? 0 : Mathf.Max(0,(speed-previousSpeed)/dt); previousSpeed=speed;
        float target=loadSource==LoadSource.Manual ? CatCooksDustUtility.Safe(engineLoad,0,1,0) :
            Mathf.Clamp01(speed/Mathf.Max(.5f,fullLoadSpeed)*.65f+acceleration/Mathf.Max(.5f,accelerationForFullLoad)*.6f);
        smoothedLoad=Mathf.Lerp(smoothedLoad,target,1-Mathf.Exp(-dt/Mathf.Clamp(responseTime,.1f,2)));
        if(!engineRunning) { carry=0; wasRunning=false; return; }
        var world=emitter.system!=null ? emitter.system : CatCooksDustSystem.Active;
        if(world==null) { carry=0; return; }
        phase+=Mathf.Min(dt,.15f);
        if(!wasRunning) { carry=1; wasRunning=true; }
        if(reset && tracking) velocity=Vector3.zero;
        carry+=Mathf.Min(dt,.15f)*EventRate(smoothedLoad,phase);
        int available=Mathf.FloorToInt(carry); carry-=available;
        for(int i=0;i<Mathf.Min(3,available);i++)
            EmitPreview(world,transform.position,transform.rotation,smoothedLoad,phase,
                Vector3.ClampMagnitude(velocity,40)*Mathf.Clamp01(inheritVehicleVelocity));
    }
    // Shared visual recipe used by both the vehicle and the isolated inspector preview.
    public void EmitPreview(CatCooksDustSystem world,Vector3 point,Quaternion rotation,float load,float seconds,
        Vector3 inheritedVelocity=default(Vector3))
    {
        var e=emitter!=null ? emitter : GetComponent<CatCooksDustEmitter>(); if(e==null) return;
        load=CatCooksDustUtility.Safe(load,0,1,0);
        float amount=Mathf.Clamp(Mathf.Lerp(idleAmount,loadedAmount,load),.1f,3);
        float size=Mathf.Lerp(.9f,1.1f,load); // Load mainly changes density, not sudden giant clouds.
        e.EmitInto(world,point,rotation,amount,size,inheritedVelocity);
    }
    void OnDrawGizmosSelected()
    {
        Gizmos.color=new Color(.3f,1,.6f); Vector3 p=transform.position, d=transform.up;
        Gizmos.DrawRay(p,d*.5f); Gizmos.DrawWireSphere(p,.035f);
    }
}
