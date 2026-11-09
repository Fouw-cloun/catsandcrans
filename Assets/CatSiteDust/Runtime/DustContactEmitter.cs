using UnityEngine;

namespace CatSite.Dust
{
    /// <summary>Animation events and explicit gameplay hooks. No automatic duplicate collision/animation triggering.</summary>
    public sealed class DustContactEmitter : MonoBehaviour
    {
        public DustPool pool;
        public DustEffect walk,run,takeoff,landLight,landHeavy,toolHit;
        public Transform leftFoot,rightFoot;
        [Tooltip("Optional probe at foot level for jump/landing events.")]
        public Transform groundProbe;
        public LayerMask groundMask=~0;
        [Min(.01f)] public float probeDistance=.3f;
        [Range(.05f,3f)] public float scale=1f;
        public Color surfaceTint=Color.white;
        [Min(0)] public float perFootCooldown=.065f;
        public float heavyLandingSpeed=5f;
        float leftAt=-100,rightAt=-100;
        public void StepLeft(){ Step(leftFoot,false,ref leftAt); }
        public void StepRight(){ Step(rightFoot,false,ref rightAt); }
        public void RunLeft(){ Step(leftFoot,true,ref leftAt); }
        public void RunRight(){ Step(rightFoot,true,ref rightAt); }
        void Step(Transform foot,bool running,ref float previous)
        {
            if(Time.time-previous<perFootCooldown || !pool)return;
            var p=foot?foot.position:transform.position;
            if(!Probe(p,out var hit))return;
            previous=Time.time; pool.Spawn(running?run:walk,hit.point+hit.normal*.025f,hit.normal,1,scale,surfaceTint);
        }
        bool Probe(Vector3 point,out RaycastHit hit)
        { return Physics.Raycast(point+Vector3.up*.08f,Vector3.down,out hit,probeDistance+.08f,groundMask,QueryTriggerInteraction.Ignore); }
        public void Takeoff()
        {
            if(pool && Probe(groundProbe?groundProbe.position:transform.position,out var hit))pool.Spawn(takeoff,hit.point+hit.normal*.025f,hit.normal,1,scale,surfaceTint);
        }
        /// <param name="downwardSpeed">Positive impact speed supplied BEFORE the controller resets vertical velocity.</param>
        public void Land(float downwardSpeed)
        {
            if(!pool || downwardSpeed<.5f || !Probe(groundProbe?groundProbe.position:transform.position,out var hit))return;
            float weight=Mathf.InverseLerp(.5f,10f,downwardSpeed);
            pool.Spawn(downwardSpeed>=heavyLandingSpeed?landHeavy:landLight,hit.point+hit.normal*.025f,hit.normal,
                Mathf.Lerp(.6f,1.25f,weight),scale*Mathf.Lerp(.8f,1.35f,weight),surfaceTint);
        }
        public void Hit(Vector3 point,Vector3 normal,float strength=1f)
        { if(pool)pool.Spawn(toolHit,point+normal*.025f,normal,strength,scale,surfaceTint); }
    }
}
