using System;
using UnityEngine;

[AddComponentMenu("CatCooks/Dust and smoke/Event or continuous emitter")]
public sealed class CatCooksDustEmitter : MonoBehaviour
{
    [Serializable]
    public sealed class Layer
    {
        public string label = "Accent";
        public bool enabled = true;
        public CatCooksDustSystem.Effect effect = CatCooksDustSystem.Effect.Smoke;
        public CatCooksDustProfile profile;
        public Color tint = Color.white;
        public Vector3 offset = new Vector3(0,.1f,0);
        [Range(.05f,4)] public float scale = .5f;
        [Range(0,3)] public float amount = .5f;
        [Range(0,3)] public float delay = .12f;
    }
    [Header("Main effect")]
    public CatCooksDustSystem system;
    public CatCooksDustProfile profile;
    public CatCooksDustSystem.Effect effect=CatCooksDustSystem.Effect.Burst;
    [Range(.1f,8)] public float scale=1;
    [Range(.1f,3)] public float strength=1;
    [Range(0,3)] public float amount=1;
    [Tooltip("Multiplies the profile palette. White keeps its original colours.")]
    public Color tint=Color.white;
    [Header("Source area — metres at Scale 1")]
    [Tooltip("XZ footprint. Zero emits at a point. Size is controlled by Scale, not Transform.")]
    public Vector2 footprint;
    [Range(1,12)] public int sources=1;
    [Tooltip("Distributes the amount between sources instead of multiplying it.")]
    public bool distributeAmount=true;
    public Vector3 initialVelocity;
    [Header("Additional layers — independent palette, size, timing")]
    public Layer[] layers = Array.Empty<Layer>();
    [Header("Playback")]
    public bool playOnEnable;
    public bool loop;
    [Range(.2f,20)] public float eventsPerSecond=2;
    float carry;
    bool DrivenByExhaust
    {
        get { var driver=GetComponent<CatCooksVehicleExhaust>(); return driver!=null && driver.isActiveAndEnabled; }
    }
    void OnEnable() { carry=0; if(playOnEnable && !DrivenByExhaust && Application.IsPlaying(gameObject)) PlayEffect(); }
    void Update()
    {
        if(!loop || DrivenByExhaust) return;
        carry+=Mathf.Min(Time.deltaTime,.15f)*Mathf.Clamp(eventsPerSecond,.2f,20);
        int n=Mathf.Min(3,Mathf.FloorToInt(carry)); carry-=n;
        for(int i=0;i<n;i++) PlayEffect();
    }
    public void PlayEffect()
    {
        var world=system!=null ? system : CatCooksDustSystem.Active;
        if(world!=null) EmitInto(world,transform.position,transform.rotation);
    }
    // Also used by the isolated editor preview. No clone of this component is needed.
    public void EmitInto(CatCooksDustSystem world, Vector3 position, Quaternion rotation,
        float amountMultiplier=1, float scaleMultiplier=1, Vector3 inheritedVelocity=default(Vector3))
    {
        if(world==null) return;
        float s=Mathf.Clamp(CatCooksDustUtility.Safe(scale,.1f,8,1)*CatCooksDustUtility.Safe(scaleMultiplier,.25f,2,1),.1f,8);
        float quantity=CatCooksDustUtility.Safe(amount,0,3,1)*CatCooksDustUtility.Safe(amountMultiplier,0,3,1);
        int n=Mathf.Clamp(sources,1,12);
        Vector2 area=new Vector2(CatCooksDustUtility.Safe(footprint.x,0,20,0),CatCooksDustUtility.Safe(footprint.y,0,20,0));
        Vector3 normal=rotation*Vector3.up;
        Vector3 velocity=rotation*initialVelocity + (CatCooksDustUtility.Finite(inheritedVelocity) ? Vector3.ClampMagnitude(inheritedVelocity,40) : Vector3.zero);
        // Golden-angle disk: stable layout with no per-frame source reshuffling.
        for(int i=0;i<n;i++)
        {
            float radius=n==1 ? 0 : Mathf.Sqrt((i+.5f)/n)*.5f;
            float angle=i*2.39996323f;
            Vector3 offset=new Vector3(Mathf.Cos(angle)*radius*area.x,0,Mathf.Sin(angle)*radius*area.y)*s;
            Vector3 point=position+rotation*offset;
            float a=quantity/(distributeAmount ? n : 1);
            world.Emit(effect,point,normal,velocity,strength,profile,s,a,tint);
            if(layers==null) continue;
            for(int k=0;k<Mathf.Min(layers.Length,8);k++)
            {
                Layer layer=layers[k]; if(layer==null || !layer.enabled) continue;
                Vector3 layerOffset=CatCooksDustUtility.Finite(layer.offset) ? Vector3.ClampMagnitude(layer.offset,10) : Vector3.zero;
                world.Emit(layer.effect,point+rotation*layerOffset*s,normal,velocity,strength,
                    layer.profile!=null ? layer.profile : profile,
                    s*CatCooksDustUtility.Safe(layer.scale,.05f,4,1),
                    a*CatCooksDustUtility.Safe(layer.amount,0,3,0),tint*layer.tint,layer.delay);
            }
        }
    }
}
