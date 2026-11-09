using UnityEngine;
using UnityEngine.Rendering;
namespace CatCooks.Builders
{
    // One runtime writer for existing sky shader, without modifying material assets.
    public sealed class CCBuilderDaylight : MonoBehaviour
    {
        public SkyController Sky;
        public Light Sun;
        public MonoBehaviour[] PauseWhileActive=new MonoBehaviour[0];
        [Range(0,360)] public float Azimuth=40;
        [Min(0)] public float DayIntensity=1.3f;
        public bool RefreshEnvironment;
        [Min(2)] public float EnvironmentInterval=5;
        private Material _oldSky,_runtime;
        private Color _skySun,_skyTop,_skyHorizon,_skyGround,_skyLit,_skyShadow;
        private float _skyIntensity,_skyElevation,_skyAzimuth,_skyDark,_skyOvercast;
        private bool[] _enabled;
        private bool _skyEnabled,_active,_oldFog;
        private float _next,_nextGI,_oldIntensity,_oldDarkness;
        private Texture _oldCookie;
        private Color _oldSun,_ambientSky,_ambientEquator,_ambientGround;
        private Quaternion _oldRotation;
        private AmbientMode _oldAmbient;
        private void Start() { Begin(); }
        private void OnEnable() { if(Application.isPlaying && _active==false && _enabled!=null) Begin(); }
        private void Begin()
        {
            if(_active || !Application.isPlaying) return;
            _active=true;
            _oldSky=RenderSettings.skybox;_oldFog=RenderSettings.fog;
            _oldAmbient=RenderSettings.ambientMode;_ambientSky=RenderSettings.ambientSkyColor;
            _ambientEquator=RenderSettings.ambientEquatorColor;_ambientGround=RenderSettings.ambientGroundColor;
            _oldDarkness=Shader.GetGlobalFloat("_GlobalCloudDarkness");
            if(Sun==null && Sky!=null) Sun=Sky.sun;
            if(Sun!=null) { _oldSun=Sun.color;_oldIntensity=Sun.intensity;_oldRotation=Sun.transform.rotation;_oldCookie=Sun.cookie;Sun.cookie=null; }
            Material source=Sky!=null && Sky.skyMaterial!=null?Sky.skyMaterial:_oldSky;
            if(source!=null) { _runtime=new Material(source);RenderSettings.skybox=_runtime; }
            if(PauseWhileActive==null) PauseWhileActive=new MonoBehaviour[0];
            _enabled=new bool[PauseWhileActive.Length];
            for(int i=0;i<_enabled.Length;i++) if(PauseWhileActive[i]!=null && PauseWhileActive[i]!=this && PauseWhileActive[i]!=Sky)
            { _enabled[i]=PauseWhileActive[i].enabled;PauseWhileActive[i].enabled=false; }
            if(Sky!=null)
            {
                _skyEnabled=Sky.enabled;Sky.enabled=false;
                _skySun=Sky.sunColor;_skyTop=Sky.zenithColor;_skyHorizon=Sky.horizonColor;_skyGround=Sky.groundColor;
                _skyLit=Sky.cloudLitColor;_skyShadow=Sky.cloudShadowColor;_skyIntensity=Sky.sunIntensity;
                _skyElevation=Sky.sunElevation;_skyAzimuth=Sky.sunAzimuth;_skyDark=Sky.cloudDarkness;_skyOvercast=Sky.skyOvercast;
            }
            RenderSettings.fog=false;Shader.SetGlobalFloat("_GlobalCloudDarkness",0);
        }
        private void Update()
        {
            if(!_active || GameClock.Instance==null || Time.unscaledTime<_next) return;
            _next=Time.unscaledTime+.1f;
            float hour=GameClock.Instance.Hours;
            float elevation=Mathf.Sin((hour-6)*Mathf.PI/12);
            float day=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.1f,.3f,elevation));
            float warm=1-Mathf.Clamp01(Mathf.Abs(elevation)*4);
            Color sun=Color.Lerp(new Color(1,.94f,.8f),new Color(1,.48f,.22f),warm);
            Color top=Color.Lerp(new Color(.012f,.02f,.065f),new Color(.35f,.6f,.92f),day);
            Color horizon=Color.Lerp(new Color(.035f,.04f,.09f),Color.Lerp(new Color(.82f,.9f,.97f),new Color(1,.58f,.38f),warm),day);
            if(Sun!=null) { Sun.transform.rotation=Quaternion.Euler((hour-6)*15,Azimuth,0);Sun.color=sun;Sun.intensity=DayIntensity*day; }
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=Color.Lerp(new Color(.045f,.06f,.12f),new Color(.45f,.53f,.66f),day);
            RenderSettings.ambientEquatorColor=Color.Lerp(new Color(.025f,.035f,.075f),new Color(.3f,.33f,.38f),day);
            RenderSettings.ambientGroundColor=Color.Lerp(new Color(.015f,.02f,.045f),new Color(.18f,.17f,.16f),day);
            if(Sky!=null)
            {
                Sky.sunColor=sun;Sky.sunIntensity=DayIntensity*day;Sky.sunElevation=(hour-6)*15;Sky.sunAzimuth=Azimuth;
                Sky.zenithColor=top;Sky.horizonColor=horizon;Sky.groundColor=RenderSettings.ambientGroundColor;
                Sky.cloudLitColor=Color.Lerp(new Color(.06f,.075f,.14f),Color.white,day);
                Sky.cloudShadowColor=Color.Lerp(new Color(.025f,.03f,.07f),new Color(.72f,.78f,.9f),day);
                Sky.cloudDarkness=0;Sky.skyOvercast=0;
            }
            if(_runtime!=null)
            {
                ColorProp("_ZenithColor",top);ColorProp("_SkyTint",top);ColorProp("_HorizonColor",horizon);
                ColorProp("_GroundColor",RenderSettings.ambientGroundColor);ColorProp("_SunColor",sun);
                ColorProp("_CloudColor",Color.Lerp(new Color(.06f,.075f,.14f),Color.white,day));
                ColorProp("_CloudShadowColor",Color.Lerp(new Color(.025f,.03f,.07f),new Color(.72f,.78f,.9f),day));
                FloatProp("_SunIntensity",DayIntensity*day);FloatProp("_CloudDarkness",0);FloatProp("_Overcast",0);
                if(Sun!=null && _runtime.HasProperty("_SunDir")) _runtime.SetVector("_SunDir",-Sun.transform.forward);
            }
            if(RefreshEnvironment && Time.unscaledTime>=_nextGI)
            { _nextGI=Time.unscaledTime+Mathf.Max(2,EnvironmentInterval);DynamicGI.UpdateEnvironment(); }
        }
        private void ColorProp(string name,Color value) { if(_runtime.HasProperty(name)) _runtime.SetColor(name,value); }
        private void FloatProp(string name,float value) { if(_runtime.HasProperty(name)) _runtime.SetFloat(name,value); }
        private void OnDisable()
        {
            if(!_active) return;_active=false;
            RenderSettings.skybox=_oldSky;RenderSettings.fog=_oldFog;RenderSettings.ambientMode=_oldAmbient;
            RenderSettings.ambientSkyColor=_ambientSky;RenderSettings.ambientEquatorColor=_ambientEquator;RenderSettings.ambientGroundColor=_ambientGround;
            Shader.SetGlobalFloat("_GlobalCloudDarkness",_oldDarkness);
            if(Sun!=null) { Sun.color=_oldSun;Sun.intensity=_oldIntensity;Sun.transform.rotation=_oldRotation;Sun.cookie=_oldCookie; }
            for(int i=0;i<_enabled.Length && i<PauseWhileActive.Length;i++)
                if(PauseWhileActive[i]!=null && PauseWhileActive[i]!=this && PauseWhileActive[i]!=Sky) PauseWhileActive[i].enabled=_enabled[i];
            if(Sky!=null)
            {
                Sky.sunColor=_skySun;Sky.sunIntensity=_skyIntensity;Sky.sunElevation=_skyElevation;Sky.sunAzimuth=_skyAzimuth;
                Sky.zenithColor=_skyTop;Sky.horizonColor=_skyHorizon;Sky.groundColor=_skyGround;Sky.cloudLitColor=_skyLit;
                Sky.cloudShadowColor=_skyShadow;Sky.cloudDarkness=_skyDark;Sky.skyOvercast=_skyOvercast;Sky.enabled=_skyEnabled;
            }
            if(_runtime!=null) Destroy(_runtime);
        }
    }
}
