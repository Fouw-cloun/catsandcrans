using UnityEngine;

namespace CatSite.Dust
{
    /// <summary>Optional Rigidbody collision adapter for dropped crates, tools and ingredients.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class DustImpactEmitter : MonoBehaviour
    {
        public DustPool pool;
        public DustEffect smallImpact,heavyImpact;
        [Tooltip("Only surfaces that should produce dust; exclude characters and trigger layers.")]
        public LayerMask surfaceMask=~0;
        public Color surfaceTint=Color.white;
        [Min(.01f)] public float minimumNormalSpeed=.75f;
        [Min(.1f)] public float heavySpeed=3.5f;
        [Min(0)] public float cooldown=.15f;
        [Range(.05f,3f)] public float scale=1f;
        [Range(0,1)] public float massInfluence=.25f;
        Rigidbody body;
        float previous=-100;
        void Awake(){body=GetComponent<Rigidbody>();}
        void OnCollisionEnter(Collision collision)
        {
            if(!pool || collision.contactCount==0 || Time.time-previous<cooldown || (surfaceMask.value & (1<<collision.gameObject.layer))==0)return;
            var contact=collision.GetContact(0);
            float speed=Mathf.Abs(Vector3.Dot(collision.relativeVelocity,contact.normal));
            if(speed<minimumNormalSpeed)return;
            float weight=Mathf.Clamp(speed*Mathf.Lerp(1,Mathf.Sqrt(Mathf.Max(.1f,body.mass)),massInfluence),0,12);
            previous=Time.time;
            pool.Spawn(weight>=heavySpeed?heavyImpact:smallImpact,contact.point+contact.normal*.025f,contact.normal,
                Mathf.Lerp(.6f,1.4f,weight/12f),scale*Mathf.Lerp(.75f,1.45f,weight/12f),surfaceTint);
        }
    }
}
