using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatCooks.SiteTools
{
    // Client-only geometry. The centre jet owns flow, contacts and gameplay.
    public sealed class CCJetLayers : MonoBehaviour
    {
        const int Sheets=5, Steps=26, Columns=4, Circle=48;
        readonly float[] flightEnds=new float[Sheets];
        readonly List<Vector3> positions=new(1600);
        readonly List<Vector2> uv=new(1600), masks=new(1600);
        readonly List<Color32> colors=new(1600);
        readonly List<int> triangles=new(6000);
        CCPipeJetLook look;
        WaterJet jet;
        Mesh mesh;
        MeshRenderer surface;
        MaterialPropertyBlock block;
        Vector3 lastOrigin,lastDirection;
        float lastFlow=-2f, nextProbe, seed, nextFloorProbe, lastFootprint=-10f;
        Vector3 lastHit,lastNormal;
        readonly float[] supported=new float[Circle];
        public float ImpactRadius { get; private set; }

        public void Initialize(CCPipeJetLook owner,WaterJet source)
        {
            look=owner;jet=source;
            mesh=new Mesh{name="Брызги, пелена и листы воды"};
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            surface=gameObject.AddComponent<MeshRenderer>();
            surface.sharedMaterial=source.GetComponent<MeshRenderer>().sharedMaterial;
            surface.shadowCastingMode=ShadowCastingMode.Off;
            surface.receiveShadows=false;
            surface.sortingOrder=1;
            block=new MaterialPropertyBlock();
            seed=Mathf.Repeat(Vector3.Dot(transform.position,new Vector3(.731f,.163f,.387f)),9.71f);
        }

        public void Hide()
        {
            if(surface!=null)surface.enabled=false;
            lastFlow=-2f;lastFootprint=-10f;
        }

        public void Draw()
        {
            float flow=jet.Flow01, time=Time.time;
            Vector3 origin=jet.transform.position, axis=jet.transform.forward;
            bool probe=time>=nextProbe || Mathf.Abs(flow-lastFlow)>.065f ||
                (origin-lastOrigin).sqrMagnitude>.018f || Vector3.Dot(axis,lastDirection)<.995f;
            if(probe){
                nextProbe=time+.23f+.008f*seed;
                lastFlow=flow;lastOrigin=origin;lastDirection=axis;
            }
            positions.Clear(); uv.Clear(); masks.Clear(); colors.Clear(); triangles.Clear();
            Vector3 right=Vector3.Cross(axis,Vector3.up);
            if(right.sqrMagnitude<.002f)right=Vector3.Cross(axis,Vector3.right);
            right.Normalize();
            Vector3 across=Vector3.Cross(axis,right).normalized;
            for(int k=0;k<Sheets;k++) Sheet(k,origin,axis,right,across,flow,time,probe);
            if(jet.HasImpact) Contact(flow,time);
            else lastFootprint=-10f;
            mesh.Clear(false);
            mesh.SetVertices(positions);mesh.SetUVs(0,uv);mesh.SetUVs(1,masks);
            mesh.SetColors(colors);mesh.SetTriangles(triangles,0,false);
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            surface.enabled=true;
            block.SetFloat("_Intensity",flow);
            surface.SetPropertyBlock(block);
        }

        static Vector3 Ballistic(Vector3 start,Vector3 v,float g,float t)
            => start+v*t+Vector3.down*(.5f*g*t*t);

        void Sheet(int k,Vector3 origin,Vector3 axis,Vector3 right,Vector3 across,float flow,float time,bool probe)
        {
            float angle=seed+k*2.39996f;
            Vector3 radial=right*Mathf.Cos(angle)+across*Mathf.Sin(angle);
            float lip=look.RadiusAt(0,flow), fan=look.RadiusAt(2,flow)-lip;
            Vector3 start=origin+radial*lip*.52f;
            float speed=jet.LaunchSpeed;
            float ratio=.71f+.22f*Mathf.Repeat(k*.382f+seed*.076f,1f);
            Vector3 v=axis*(ratio*speed)+radial*(speed*.145f*fan);
            float g=jet.StreamGravity;
            if(probe){
                float limit=jet.FlightLimit;
                Vector3 prev=start;
                flightEnds[k]=limit;
                for(int q=1;q<=18;q++){
                    float t=limit*q/18f;
                    Vector3 p=Ballistic(start,v,g,t),delta=p-prev;
                    float len=delta.magnitude;
                    if(len>.0001f && Physics.Raycast(prev,delta/len,out var hit,len,jet.SurfaceMask,QueryTriggerInteraction.Ignore)){
                        flightEnds[k]=limit*(q-1+hit.distance/len)/18f;
                        break;
                    }
                    prev=p;
                }
            }
            float duration=flightEnds[k];
            if(duration<.015f)return;
            int first=positions.Count;
            Vector3 widthDir=Vector3.Cross(v,radial).normalized, previous=start;
            float metres=0;
            for(int j=0;j<=Steps;j++){
                float t=duration*j/Steps;
                Vector3 centre=Ballistic(start,v,g,t);
                metres+=Vector3.Distance(previous,centre);previous=centre;
                Vector3 tangent=(v+Vector3.down*(g*t)).normalized;
                widthDir=Vector3.ProjectOnPlane(widthDir,tangent).normalized;
                if(widthDir.sqrMagnitude<.01f)widthDir=Vector3.Cross(tangent,radial).normalized;
                Vector3 broad=Vector3.Cross(widthDir,tangent);
                float aging=Mathf.Clamp01(t*g/Mathf.Max(1f,v.y)*.66f);
                float phase=metres*2.8f-time*4.1f+angle;
                float envelope=look.RadiusAt(metres,flow);
                float width=envelope*Mathf.Lerp(.49f,.19f,aging)*(.78f+.22f*Mathf.Sin(phase));
                centre+=broad*(.11f*envelope*aging*Mathf.Sin(phase*.69f));
                for(int col=0;col<Columns;col++){
                    float u=(float)col/(Columns-1), flank=2f*u-1f;
                    Vector3 p=centre+widthDir*(flank*width)+broad*((1f-flank*flank)*width*.41f);
                    Vert(p,u,metres,aging,1,Mathf.Repeat(k*.183f+.17f,1f),1);
                }
                if(j>0)for(int col=0;col<Columns-1;col++) Quad(first+(j-1)*Columns+col,Columns);
            }
        }

        void Contact(float flow,float time)
        {
            Vector3 hit=jet.PathPointsWorld[jet.PathCount-1], n=jet.ImpactNormal.normalized;
            Vector3 x=Vector3.ProjectOnPlane(Vector3.right,n);
            if(x.sqrMagnitude<.01f)x=Vector3.ProjectOnPlane(Vector3.forward,n);
            x.Normalize();
            Vector3 z=Vector3.Cross(n,x);
            float width=look.RadiusAt(Vector3.Distance(jet.transform.position,hit),flow);
            ImpactRadius=Mathf.Max(look.RadiusAt(0,flow),width*.75f);
            float r=ImpactRadius;
            // Cache the available radius along each spoke. Stop foam at deck edges and openings.
            if(time>=nextFloorProbe || (hit-lastHit).sqrMagnitude>.025f || Vector3.Dot(n,lastNormal)<.98f ||
               Mathf.Abs(lastFootprint-r)>.12f){
                nextFloorProbe=time+.42f+.009f*seed;
                lastHit=hit;lastNormal=n;lastFootprint=r;
                for(int i=0;i<Circle;i++){
                    float a=i*(Mathf.PI*2f/Circle);
                    Vector3 radial=x*Mathf.Cos(a)+z*Mathf.Sin(a);
                    float max=r*2.53f;
                    supported[i]=0;
                    for(int j=1;j<=4;j++){
                        float d=max*j*.25f;
                        var origin=hit+radial*d+n*.14f;
                        if(!Physics.Raycast(origin,-n,out var floor,.27f,jet.SurfaceMask,QueryTriggerInteraction.Ignore) ||
                            Vector3.Dot(n,floor.normal)<.88f)break;
                        supported[i]=d;
                    }
                }
            }
            for(int band=0;band<5;band++){
                bool crown=band==0, pool=band==1;
                float pulse=Mathf.Repeat(time*(.71f+.12f*flow)+(band-2)/3f,1f);
                int first=positions.Count;
                for(int i=0;i<=Circle;i++){
                    float u=(float)i/Circle, a=u*Mathf.PI*2f;
                    Vector3 outward=x*Mathf.Cos(a)+z*Mathf.Sin(a);
                    float wobble=.064f*Mathf.Sin(5*a+seed)+.043f*Mathf.Cos(7*a-time*1.1f);
                    float lobes=.5f+.5f*Mathf.Sin(11*a+seed+1.1f*Mathf.Sin(3*a-time*3));
                    float crest=(.21f+.84f*lobes*lobes)*(.72f+.28f*Mathf.Sin(6*a+time*4));
                    for(int row=0;row<3;row++){
                        float v=row*.5f, radius,height,alpha;
                        if(crown){
                            radius=r*(.49f+v*(.76f+.18f*crest)+wobble);
                            height=.035f+r*v*v*crest*.88f;
                            alpha=1f-.28f*v;
                        }else if(pool){
                            radius=r*(.03f+v*(1.53f+wobble));
                            height=.023f+r*.028f*(1f-v);
                            alpha=(1f-v*v)*.87f;
                        }else{
                            float fade=Mathf.Sin(Mathf.PI*pulse);
                            radius=r*(.84f+1.51f*pulse+(.13f-.065f*pulse)*(v-.5f)+wobble);
                            height=.032f+r*.023f*fade;
                            alpha=fade*(1f-Mathf.Abs(2*v-1f))*.72f;
                        }
                        float reach=supported[i%Circle];
                        if(!crown){
                            alpha*=Mathf.Clamp01((reach-radius)/Mathf.Max(.035f,r*.16f));
                            radius=Mathf.Min(radius,reach);
                        }
                        Vert(hit+outward*radius+n*height,u,v,crest,crown?2:pool?3:4,pulse,alpha);
                    }
                    if(i>0)for(int row=0;row<2;row++) Quad(first+(i-1)*3+row,3);
                }
            }
        }

        void Vert(Vector3 world,float u,float v,float age,int layer,float seedValue,float alpha)
        {
            positions.Add(transform.InverseTransformPoint(world));uv.Add(new Vector2(u,v));
            masks.Add(new Vector2(age,layer));colors.Add(new Color(seedValue,1,1,alpha));
        }
        void Quad(int i,int stride)
        {
            triangles.Add(i);triangles.Add(i+1);triangles.Add(i+stride);
            triangles.Add(i+1);triangles.Add(i+stride+1);triangles.Add(i+stride);
        }
        void OnDestroy(){if(mesh!=null)Destroy(mesh);}
    }
}
