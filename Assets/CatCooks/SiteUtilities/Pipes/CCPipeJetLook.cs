using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatCooks.SiteTools
{
    public enum CCPipeFluid { Cold, Hot, Sewage }

    [DisallowMultipleComponent, RequireComponent(typeof(WaterJet))]
    public sealed class CCPipeJetLook : MonoBehaviour
    {
        public CCPipeFluid fluid;
        public Material mist, drops;
        [Min(.02f)] public float mouthRadius = .27f;
        [Range(3f,30f)] public float spreadDegrees = 16f;
        [Min(.25f)] public float flareLength = 3.5f;

        WaterJet jet;
        CCInspectionHatch hatch;
        ParticleSystem haze, spray;
        CCJetLayers layers;
        ParticleSystem spume;
        float hazeBudget, sprayBudget, spumeBudget;
        Collider[] hits;
        HashSet<Rigidbody> pushed;

        public float RadiusAt(float metres,float flow)
        {
            float scale=Mathf.Max(.001f,Mathf.Abs(transform.lossyScale.x));
            float fan=Mathf.Tan(spreadDegrees*Mathf.Deg2Rad)*Mathf.Min(metres,flareLength*scale);
            return mouthRadius*scale*Mathf.Lerp(.27f,1f,flow)+fan*Mathf.Lerp(.12f,1f,flow);
        }

        void Awake()
        {
            jet=GetComponent<WaterJet>();
            hatch=GetComponentInParent<CCInspectionHatch>();
        }

        public void PushAlongCone(WaterJet source,float force)
        {
            if (hatch==null || !hatch.IsServerInitialized || source.PathCount<2) return;
            if (hits==null){hits=new Collider[48];pushed=new HashSet<Rigidbody>();}
            pushed.Clear();
            int end=source.PathCount-1;
            var points=source.PathPointsWorld;
            float metres=0f;
            for(int i=0;i<end;i+=2){
                int j=Mathf.Min(i+2,end);
                Vector3 a=points[i], b=points[j];
                for(int n=i+1;n<=j;n++) metres+=Vector3.Distance(points[n-1],points[n]);
                float radius=RadiusAt(metres,source.Flow01);
                int count=Physics.OverlapCapsuleNonAlloc(a,b,radius,hits,~0,QueryTriggerInteraction.Ignore);
                var contacts=hits;
                if(count==hits.Length){
                    contacts=Physics.OverlapCapsule(a,b,radius,~0,QueryTriggerInteraction.Ignore);
                    count=contacts.Length;
                }
                Vector3 direction=((b-a).normalized+Vector3.up*.20f).normalized;
                for(int k=0;k<count;k++){
                    var body=contacts[k].attachedRigidbody;
                    if(body==null || body.isKinematic || !pushed.Add(body))continue;
                    body.AddForce(direction*(force*source.Flow01),ForceMode.Acceleration);
                }
            }
        }

        ParticleSystem MakeCloud(string label,Material material,bool vapor)
        {
            var node=new GameObject(label);
            node.transform.SetParent(transform,false);
            var ps=node.AddComponent<ParticleSystem>();
            ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;
            main.loop=true;
            main.playOnAwake=false;
            main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.maxParticles=vapor?180:210;
            main.gravityModifier=vapor?0f:.48f;
            var emission=ps.emission; emission.enabled=false;
            var shape=ps.shape; shape.enabled=false;
            var size=ps.sizeOverLifetime; size.enabled=true;
            var growth=vapor?new AnimationCurve(new Keyframe(0,.4f),new Keyframe(.65f,1.25f),new Keyframe(1,1.7f)):
                AnimationCurve.Linear(0,1,1,.45f);
            size.size=new ParticleSystem.MinMaxCurve(1f,growth);
            var color=ps.colorOverLifetime; color.enabled=true;
            var gradient=new Gradient();
            gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.12f),new GradientAlphaKey(.75f,.55f),new GradientAlphaKey(0,1)});
            color.color=new ParticleSystem.MinMaxGradient(gradient);
            if(vapor){
                var noise=ps.noise;noise.enabled=true;noise.strength=.32f;noise.frequency=.74f;
                noise.scrollSpeed=.45f;noise.quality=ParticleSystemNoiseQuality.Low;
            }
            var renderer=node.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial=material;
            renderer.renderMode=ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder=3;
            renderer.minParticleSize=.001f;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            ps.Play();
            return ps;
        }

        void LateUpdate()
        {
            if (!jet.IsFlowing || jet.PathCount<2 || hatch==null || !hatch.IsClientInitialized){
                hazeBudget=sprayBudget=spumeBudget=0;
                if(layers!=null) layers.Hide();
                return;
            }
            if(layers==null){
                var node=new GameObject("Листы воды и зона удара");
                node.transform.SetParent(transform,false);
                layers=node.AddComponent<CCJetLayers>();
                layers.Initialize(this,jet);
                haze=MakeCloud("Дымка",mist,true);
                spray=MakeCloud("Летящие капли",drops,false);
                spume=MakeCloud("Брызги у поверхности",drops,false);
                var renderer=spume.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode=ParticleSystemRenderMode.Stretch;
                renderer.velocityScale=.065f; renderer.lengthScale=1.45f;
            }
            layers.Draw();
            float flow=jet.Flow01, dt=Mathf.Min(Time.deltaTime,.1f);
            bool hot=fluid==CCPipeFluid.Hot, foul=fluid==CCPipeFluid.Sewage;
            hazeBudget+=dt*flow*(hot?69f:foul?18f:32f);
            sprayBudget+=dt*flow*(foul?51f:99f);
            if(jet.HasImpact)spumeBudget+=dt*flow*(foul?55f:120f);
            else spumeBudget=0;
            while(hazeBudget>=1f){hazeBudget--;Emit(true,jet.HasImpact && Random.value<.38f);}
            while(sprayBudget>=1f){sprayBudget--;Emit(false,false);}
            while(spumeBudget>=1f){spumeBudget--;Emit(false,true);}
        }

        void Emit(bool vapor,bool impact)
        {
            bool hot=fluid==CCPipeFluid.Hot, foul=fluid==CCPipeFluid.Sewage;
            float flow=jet.Flow01;
            int last=jet.PathCount-1;
            var path=jet.PathPointsWorld;
            Vector3 position, velocity;
            float size,life;
            if(impact){
                Vector3 n=jet.ImpactNormal.normalized;
                Vector3 tangent=Vector3.ProjectOnPlane(Random.onUnitSphere,n).normalized;
                float radius=layers.ImpactRadius;
                position=path[last]+n*.045f+tangent*radius*Random.Range(.28f,1.05f);
                velocity=vapor?n*.45f+Vector3.up*(hot?1.6f:.55f)+tangent*.55f :
                    (n*Random.Range(2.6f,5.3f)+tangent*Random.Range(1.8f,4.7f))*Mathf.Lerp(.35f,1f,flow);
            }else{
                // Bias droplets towards the aerated crest. Leave the throat legible.
                float u=vapor?Random.value:Mathf.Lerp(.18f,1f,Random.value);
                float along=u*last;
                int i=Mathf.Min(Mathf.FloorToInt(along),last-1);
                Vector3 axis=(path[i+1]-path[i]).normalized;
                Vector3 radial=Vector3.Cross(axis,Random.onUnitSphere).normalized;
                position=Vector3.Lerp(path[i],path[i+1],along-i);
                float radius=RadiusAt(Vector3.Distance(position,path[0]),flow);
                position+=radial*radius*Random.Range(.53f,1.12f);
                velocity=vapor?Vector3.up*(hot?2.15f:foul?.75f:.35f)+radial*.6f:
                    axis*Mathf.Lerp(1.1f,4.4f,flow)+radial*Random.Range(.6f,2.25f);
            }
            Color tint;
            float variation=Random.value;
            if(vapor){
                tint=foul?Color.Lerp(new Color(.35f,.32f,.11f,.58f),new Color(.61f,.62f,.25f,.39f),variation):
                    hot?Color.Lerp(new Color(.86f,.87f,.83f,.56f),new Color(1,.99f,.95f,.72f),variation):
                    new Color(.76f,.92f,.99f,.45f);
                size=Random.Range(.44f,1.04f)*(hot?1.45f:foul?.99f:.78f);
                life=Random.Range(1.0f,1.8f)*(foul?1.5f:hot?1.35f:.78f);
            }else{
                tint=foul?Color.Lerp(new Color(.19f,.105f,.058f,.99f),new Color(.44f,.31f,.15f,.94f),variation):
                    Color.Lerp(new Color(.43f,.80f,.90f,.79f),new Color(.96f,.99f,1,.98f),variation);
                size=Random.Range(.045f,impact?.17f:.13f)*(foul?1.35f:1f);
                life=Random.Range(.28f,impact?.82f:.71f);
            }
            var particle=new ParticleSystem.EmitParams {
                position=position,velocity=velocity,startSize=size*Mathf.Lerp(.58f,1f,flow),
                startLifetime=life,startColor=tint,rotation=vapor && foul?Random.Range(-16f,16f):Random.Range(-180f,180f)
            };
            (vapor?haze:impact?spume:spray).Emit(particle,1);
        }

        void OnEnable()
        {
            if (haze != null) haze.Play(false);
            if (spray != null) spray.Play(false);
            if (spume != null) spume.Play(false);
        }

        void OnDisable()
        {
            hazeBudget=sprayBudget=spumeBudget=0;
            if(layers!=null) layers.Hide();
            if (haze != null) haze.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (spray != null) spray.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (spume != null) spume.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
