using UnityEngine;
namespace CatCooks.SiteTools
{
    [ExecuteAlways]
    public sealed class CCWireSpan : MonoBehaviour
    {
        public CCPowerPort a,b;
        public LineRenderer left,right;
        [Range(.01f,.22f)] public float sag=.065f;
        [Range(0,.7f)] public float wind=.18f;
        const int Segments=24;
        void OnEnable(){Register();Draw();}
        public void Register(){
            if(a!=null && !a.links.Contains(this))a.links.Add(this);
            if(b!=null && !b.links.Contains(this))b.links.Add(this);
        }
        void OnDisable(){if(a!=null)a.links.Remove(this);if(b!=null)b.links.Remove(this);}
        void LateUpdate()=>Draw();
        public void Draw(){
            if(a==null||b==null){if(left)left.enabled=false;if(right)right.enabled=false;return;}
            Strand(left,a.left,b.left);Strand(right,a.right,b.right);
        }
        void Strand(LineRenderer line,Transform start,Transform end){
            if(line==null)return;
            line.enabled=start!=null && end!=null && a.isActiveAndEnabled && b.isActiveAndEnabled;
            if(!line.enabled)return;
            Vector3 x=start.position,y=end.position;
            float distance=Vector3.Distance(x,y);
            Vector3 cross=Vector3.Cross(y-x,Vector3.up).normalized;
            float phase=Time.time*1.45f+x.x*.17f+x.z*.13f;
            var weather=CatCooksWindController.Instance;
            Vector3 air=weather!=null?weather.SampleWind((x+y)*.5f):cross;
            Vector3 sway=Vector3.ProjectOnPlane(air,(y-x).normalized);
            line.positionCount=Segments+1;
            for(int i=0;i<=Segments;i++){
                float t=i/(float)Segments;
                float wave=Application.isPlaying?Mathf.Sin(phase+t*2.5f)+.26f*Mathf.Sin(phase*2.1f-t):0;
                line.SetPosition(i,Vector3.Lerp(x,y,t)+4*t*(1-t)*(Vector3.down*(distance*sag)+sway*(wave*wind*Mathf.Min(1,distance*.18f))));
            }
        }
    }
}
