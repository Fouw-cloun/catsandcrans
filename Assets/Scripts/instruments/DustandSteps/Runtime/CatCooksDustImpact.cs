using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
[AddComponentMenu("CatCooks/Dust and smoke/Material impact")]
public sealed class CatCooksDustImpact : MonoBehaviour
{
    public CatCooksDustSystem system;
    [Tooltip("Optional palette of the dropped material. Otherwise use the struck surface.")]
    public CatCooksDustProfile materialProfile;
    public LayerMask collisionMask = ~0;
    [Min(.1f)] public float minimumSpeed = 1;
    [Min(.02f)] public float cooldown = .18f;
    [Range(.1f,4)] public float effectScale = 1;
    public bool scaleFromBounds = true;
    public bool massAffectsStrength = true;
    public bool automaticCollisions = true;
    Rigidbody body;
    float nextTime;
    void Awake() => body=GetComponent<Rigidbody>();
    void OnEnable() { nextTime=0; if(body==null) body=GetComponent<Rigidbody>(); }
    void OnCollisionEnter(Collision collision)
    {
        if(!automaticCollisions || Time.time<nextTime || collision.contactCount==0) return;
        ContactPoint best=collision.GetContact(0); float speed=0;
        for(int i=0;i<collision.contactCount;i++)
        {
            var contact=collision.GetContact(i);
            float s=Mathf.Abs(Vector3.Dot(collision.relativeVelocity,contact.normal));
            if(s>speed) { speed=s; best=contact; }
        }
        Collider surface=best.otherCollider;
        if(surface==null || (collisionMask.value&(1<<surface.gameObject.layer))==0 || speed<minimumSpeed) return;
        var world=system!=null ? system : CatCooksDustSystem.Active; if(world==null) return;
        var profile=CatCooksDustUtility.Surface(surface,materialProfile,out float amount);
        if(materialProfile!=null) profile=materialProfile;
        float mass=massAffectsStrength && body!=null ? Mathf.Clamp(Mathf.Sqrt(body.mass/5),.5f,2) : 1;
        float strength=Mathf.Clamp(speed/3*mass,.4f,2.8f);
        float scale=effectScale;
        if(scaleFromBounds && best.thisCollider!=null)
        {
            Vector3 b=best.thisCollider.bounds.size;
            scale*=Mathf.Clamp(Mathf.Sqrt(Mathf.Max(b.x,Mathf.Max(b.y,b.z))),.35f,2.5f);
        }
        world.Emit(CatCooksDustSystem.Effect.Impact,best.point,best.normal,collision.relativeVelocity,strength,profile,scale,amount);
        nextTime=Time.time+Mathf.Max(.02f,cooldown);
    }
}
