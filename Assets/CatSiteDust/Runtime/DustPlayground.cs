using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatSite.Dust
{
    /// <summary>Demo only. OnGUI works without selecting the old/new Input System.</summary>
    public sealed class DustPlayground : MonoBehaviour
    {
        public DustEffect[] presets;
        public string[] titles;
        public Material stageTemplate;
        readonly List<Material> stageMaterials=new List<Material>();
        readonly List<DustEffect> stress=new List<DustEffect>();
        DustEffect current;
        Camera view;
        int selected=4;
        float elapsed,speed=1f,scale=1f,repeatAt=2.7f;
        bool playing=true,repeat=true;
        DustQuality quality=DustQuality.Standard;
        GUIStyle header,small,button;
        void Start()
        {
            if(presets==null || presets.Length==0) {Debug.LogError("Dust Playground: no preset references.",this);enabled=false;return;}
            BuildStage();Select(Mathf.Min(selected,presets.Length-1));
        }
        Material Mat(Color c)
        {
            var m=new Material(stageTemplate);m.SetColor("_BaseColor",c);stageMaterials.Add(m);return m;
        }
        GameObject Prop(string name,PrimitiveType type,Vector3 pos,Vector3 size,Material mat)
        {
            var g=GameObject.CreatePrimitive(type);g.name=name;g.transform.SetParent(transform,false);
            g.transform.localPosition=pos;g.transform.localScale=size;
            var renderer=g.GetComponent<Renderer>();renderer.sharedMaterial=mat;renderer.shadowCastingMode=ShadowCastingMode.Off;
            return g;
        }
        void BuildStage()
        {
            var go=new GameObject("Preview Camera");go.transform.SetParent(transform,false);view=go.AddComponent<Camera>();
            view.tag="MainCamera";view.transform.position=new Vector3(4.4f,3.5f,-6.2f);view.transform.LookAt(new Vector3(0,.6f,0));
            view.orthographic=true;view.orthographicSize=2.7f;view.clearFlags=CameraClearFlags.SolidColor;
            view.backgroundColor=new Color(.11f,.17f,.18f);view.nearClipPlane=.05f;view.farClipPlane=70;view.rect=new Rect(.28f,0,.72f,1);
            var lg=new GameObject("Preview Key");lg.transform.SetParent(transform,false);lg.transform.rotation=Quaternion.Euler(48,-35,0);
            var light=lg.AddComponent<Light>();light.type=LightType.Directional;light.color=new Color(1,.94f,.82f);light.intensity=1.2f;light.shadows=LightShadows.None;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.55f,.62f,.64f);
            var floor=Mat(new Color(.27f,.36f,.34f));var pad=Mat(new Color(.57f,.55f,.43f));
            var yellow=Mat(new Color(.96f,.64f,.19f));var teal=Mat(new Color(.18f,.39f,.39f));var cream=Mat(new Color(.89f,.82f,.65f));
            Prop("Floor",PrimitiveType.Cube,new Vector3(0,-.18f,0),new Vector3(30,.2f,30),floor);
            Prop("Dust stage",PrimitiveType.Cylinder,new Vector3(0,-.08f,0),new Vector3(3.3f,.08f,3.3f),pad);
            for(int i=0;i<6;i++)Prop("Edge marker",PrimitiveType.Cube,new Vector3(-1.4f+i*.56f,.005f,1.4f),new Vector3(.24f,.025f,.08f),yellow);
            Prop("Ingredient crate",PrimitiveType.Cube,new Vector3(-1.6f,.27f,.5f),new Vector3(.43f,.5f,.45f),teal);
            Prop("Crate rim",PrimitiveType.Cube,new Vector3(-1.6f,.55f,.5f),new Vector3(.49f,.06f,.49f),cream);
            Prop("Cooking pot",PrimitiveType.Cylinder,new Vector3(1.5f,.25f,.7f),new Vector3(.48f,.26f,.48f),teal);
            Prop("Pot lid",PrimitiveType.Sphere,new Vector3(1.5f,.52f,.7f),new Vector3(.52f,.09f,.52f),cream);
            Prop("Pot lid handle",PrimitiveType.Sphere,new Vector3(1.5f,.6f,.7f),new Vector3(.09f,.10f,.09f),yellow);
        }
        void Select(int index)
        {
            foreach(var fx in stress)if(fx)Destroy(fx.gameObject);stress.Clear();
            if(current)Destroy(current.gameObject);
            selected=index;current=Instantiate(presets[index],Vector3.zero,Quaternion.identity,transform);
            current.playOnEnable=false;
            if(current.name.Contains("Exhaust"))current.transform.SetPositionAndRotation(new Vector3(-.3f,.35f,0),Quaternion.Euler(0,0,-65));
            current.Configure(1,scale,Color.white,quality);Replay();
        }
        void Replay()
        {
            if(!current)return;elapsed=0;playing=true;current.Play(1234);
            foreach(var ps in current.Systems){var m=ps.main;m.simulationSpeed=speed;}
        }
        void Update()
        {
            if(view)view.rect=new Rect(Mathf.Max(265,Screen.width*.28f)/Screen.width,0,1-Mathf.Max(265,Screen.width*.28f)/Screen.width,1);
            if(!current || !playing)return;
            elapsed+=Time.deltaTime*speed;
            if(current.name.Contains("Wheel_Rolling"))current.transform.position=new Vector3(Mathf.Sin(elapsed*1.3f)*1.0f,0,0);
            if(!current.isContinuous && repeat && elapsed>repeatAt)Replay();
            for(int i=0;i<stress.Count;i++)
            {
                if(!stress[i])continue;
                if(current.name.Contains("Wheel_Rolling"))stress[i].transform.position=new Vector3((i%6-2.5f)*.7f+Mathf.Sin(elapsed*1.3f)*.35f,0,(i/6-1.5f)*.7f);
                if(!stress[i].IsAlive)stress[i].Play();
            }
        }
        void Styles()
        {
            if(header!=null)return;
            header=new GUIStyle(GUI.skin.label){fontSize=23,fontStyle=FontStyle.Bold,wordWrap=true};header.normal.textColor=new Color(.98f,.87f,.62f);
            small=new GUIStyle(GUI.skin.label){fontSize=12,wordWrap=true};small.normal.textColor=new Color(.78f,.83f,.80f);
            button=new GUIStyle(GUI.skin.button){fontSize=13,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(12,8,3,3)};
        }
        void OnGUI()
        {
            if(!current)return;Styles();
            float w=Mathf.Max(265,Screen.width*.28f);
            GUI.Box(new Rect(8,8,w-16,Screen.height-16),GUIContent.none);
            GUILayout.BeginArea(new Rect(20,18,w-40,Screen.height-36));
            GUILayout.Label("CAT SITE / DUST",header);
            GUILayout.Label("14 пресетов · URP · 1 unit = 1 метр",small);GUILayout.Space(10);
            for(int i=0;i<presets.Length;i++)
            {
                GUI.backgroundColor=i==selected?new Color(.85f,.64f,.27f):new Color(.43f,.49f,.46f);
                if(GUILayout.Button(titles!=null && i<titles.Length?titles[i]:presets[i].name,button,GUILayout.Height(Mathf.Clamp((Screen.height-340)/14f,19,30))))Select(i);
            }
            GUI.backgroundColor=Color.white;GUILayout.Space(8);
            GUILayout.Label("Качество",small);
            int q=GUILayout.SelectionGrid((int)quality,new[]{"Low","Standard","High"},3);
            if(q!=(int)quality){quality=(DustQuality)q;current.Configure(1,scale,Color.white,quality);Replay();}
            GUILayout.Label("Масштаб: "+scale.ToString("0.00"),small);
            float s=GUILayout.HorizontalSlider(scale,.4f,2.0f);
            if(Mathf.Abs(s-scale)>.015f){scale=s;current.Configure(1,scale,Color.white,quality);Replay();}
            GUILayout.Label("Скорость: "+speed.ToString("0.00")+"×",small);
            float sp=GUILayout.HorizontalSlider(speed,.1f,1.5f);
            if(Mathf.Abs(sp-speed)>.01f){speed=sp;foreach(var ps in current.Systems){var m=ps.main;m.simulationSpeed=speed;}}
            GUILayout.BeginHorizontal();if(GUILayout.Button("Повтор"))Replay();
            if(GUILayout.Button(playing?"Пауза":"Продолжить"))
            {playing=!playing;foreach(var ps in current.Systems){if(playing)ps.Play(false);else ps.Pause(false);}}
            GUILayout.EndHorizontal();repeat=GUILayout.Toggle(repeat,"Автоповтор разовых эффектов");
            if(GUILayout.Button(stress.Count==0?"Нагрузка: ещё 24 эффекта":"Убрать нагрузку"))
            {
                if(stress.Count>0){foreach(var fx in stress)if(fx)Destroy(fx.gameObject);stress.Clear();}
                else for(int i=0;i<24;i++)
                {
                    var fx=Instantiate(presets[selected],new Vector3((i%6-2.5f)*.7f,0,(i/6-1.5f)*.7f),Quaternion.identity,transform);
                    fx.Configure(.7f,.7f,Color.white,quality);fx.Play((uint)(100+i));stress.Add(fx);
                }
            }
            GUILayout.EndArea();
            GUI.Label(new Rect(w+22,24,Screen.width-w-44,45),titles!=null && selected<titles.Length?titles[selected]:presets[selected].name,header);
            int count=current.ParticleCount;foreach(var fx in stress)if(fx)count+=fx.ParticleCount;
            GUI.Label(new Rect(w+22,68,600,25),$"Частиц сейчас: {count}   ·   слоёв: {current.Systems.Length}   ·   seed 1234",small);
            if(current.isContinuous)
                GUI.Label(new Rect(w+22,Screen.height-55,Screen.width-w-44,40),current.name.Contains("Wheel")?"Колёсный источник движется. Выброшенная пыль остаётся в мире.":"Непрерывный эффект. Ось +Y префаба задаёт направление выброса.",small);
            else
            {
                GUI.Label(new Rect(w+22,Screen.height-64,180,25),"Время: "+elapsed.ToString("0.00")+" с",small);
                float t=GUI.HorizontalSlider(new Rect(w+22,Screen.height-34,Screen.width-w-44,20),Mathf.Clamp(elapsed,0,2.4f),0,2.4f);
                if(Mathf.Abs(t-Mathf.Clamp(elapsed,0,2.4f))>.025f){playing=false;elapsed=t;current.SimulatePreview(t);}
            }
        }
        void OnDestroy(){foreach(var m in stageMaterials)if(m)Destroy(m);}
    }
}
