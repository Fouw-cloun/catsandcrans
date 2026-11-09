using UnityEngine;

namespace CatSite.Dust
{
    /// <summary>Attach to a world-oriented wheel contact / exhaust anchor; +Y is the puff emission direction.</summary>
    [RequireComponent(typeof(DustEffect))]
    public sealed class DustContinuousEmitter : MonoBehaviour
    {
        public bool requireGround=true;
        public Camera distanceCamera;
        [Min(0)] public float maxDistance=35f;
        public LayerMask groundMask=~0;
        [Min(.01f)] public float probeDistance=.45f;
        [Min(0)] public float minSpeed=.15f;
        [Min(.1f)] public float fullSpeed=5f;
        [Range(0,1)] public float throttle=1f;
        [Min(.5f)] public float teleportDistance=4f;
        DustEffect fx;
        Vector3 previous;
        float nextProbe,lastGain=-1;
        bool grounded=true;
        void OnEnable(){fx=GetComponent<DustEffect>();previous=transform.position;nextProbe=0;lastGain=-1;if(!distanceCamera)distanceCamera=Camera.main;}
        void Update()
        {
            Vector3 p=transform.position;float distance=Vector3.Distance(p,previous);previous=p;
            bool far=distanceCamera && maxDistance>0 && (distanceCamera.transform.position-p).sqrMagnitude>maxDistance*maxDistance;
            if(far){if(lastGain!=0){fx.SetEmission(0);lastGain=0;}return;}
            if(distance>teleportDistance){fx.Stop(true);fx.Play();fx.SetEmission(0);lastGain=0;return;}
            if(Time.time>=nextProbe)
            {
                grounded=!requireGround || Physics.Raycast(p+Vector3.up*.1f,Vector3.down,probeDistance+.1f,groundMask,QueryTriggerInteraction.Ignore);
                nextProbe=Time.time+.05f;
            }
            float speed=distance/Mathf.Max(Time.deltaTime,.0001f);
            float gain=grounded?throttle:0;
            if(requireGround)gain*=speed<minSpeed?0:Mathf.Lerp(.35f,1f,Mathf.InverseLerp(minSpeed,Mathf.Max(minSpeed+.01f,fullSpeed),speed));
            if(Mathf.Abs(gain-lastGain)>.02f || (gain==0 && lastGain!=0)) {fx.SetEmission(gain);lastGain=gain;}
        }
    }
}
