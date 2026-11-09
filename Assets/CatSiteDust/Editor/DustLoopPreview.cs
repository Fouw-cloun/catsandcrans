using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CatSite.Dust.Editor
{
    // Only a disposable editor copy is simulated. No preview settings enter the build.
    public sealed class DustLoopPreview : EditorWindow
    {
        [SerializeField] DustEffect source;
        [SerializeField] float speed=1, gap=.35f, motionSpeed=2;
        [SerializeField] bool varySeed=true;
        DustEffect preview;
        ParticleSystem[] systems;
        bool paused;
        double lastTick;
        float elapsed, cycle;
        uint seed=1234;
        [MenuItem("Tools/Cat Site Dust/Loop Preview")]
        public static void OpenWindow() { GetWindow<DustLoopPreview>("Dust Loop Preview"); }
        public static void Open(DustEffect effect)
        {
            var w=GetWindow<DustLoopPreview>("Dust Loop Preview");
            w.StopPreview();w.source=effect;w.StartPreview();w.Show();
        }
        void OnEnable()
        {
            EditorApplication.update+=Tick;
            EditorApplication.playModeStateChanged+=OnPlayMode;
            AssemblyReloadEvents.beforeAssemblyReload+=StopPreview;
            EditorSceneManager.sceneClosing+=OnSceneClosing;
        }
        void OnDisable()
        {
            StopPreview();EditorApplication.update-=Tick;
            EditorApplication.playModeStateChanged-=OnPlayMode;
            AssemblyReloadEvents.beforeAssemblyReload-=StopPreview;
            EditorSceneManager.sceneClosing-=OnSceneClosing;
        }
        void OnPlayMode(PlayModeStateChange state) { StopPreview(); }
        void OnSceneClosing(Scene scene,bool removingScene) { StopPreview(); }
        void OnGUI()
        {
            EditorGUILayout.HelpBox("Без Play Mode. Временная копия эффекта видна в Scene View; исходный префаб не изменяется. Закрытие окна удаляет копию.",MessageType.Info);
            EditorGUI.BeginChangeCheck();
            var next=(DustEffect)EditorGUILayout.ObjectField("Effect",source,typeof(DustEffect),true);
            if(EditorGUI.EndChangeCheck()){StopPreview();source=next;}
            speed=EditorGUILayout.Slider("Playback speed",speed,.1f,2);
            gap=EditorGUILayout.Slider("Pause between bursts",gap,0,3);
            motionSpeed=EditorGUILayout.Slider("Distance emission (m/s)",motionSpeed,0,8);
            varySeed=EditorGUILayout.Toggle("New variation each cycle",varySeed);
            using(new EditorGUI.DisabledScope(Application.isPlaying || !source))
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Start / Refresh"))StartPreview();
                if(GUILayout.Button(paused?"Resume":"Pause")){paused=!paused;lastTick=EditorApplication.timeSinceStartup;}
                if(GUILayout.Button("Stop"))StopPreview();
            }
            if(preview)
            {
                EditorGUILayout.LabelField("Particles",preview.ParticleCount.ToString());
                if(GUILayout.Button("Frame effect in Scene View") && SceneView.lastActiveSceneView)
                    SceneView.lastActiveSceneView.Frame(new Bounds(preview.transform.position,Vector3.one*4),false);
            }
            EditorGUILayout.HelpBox("Можно выбрать префаб из Project или экземпляр в вашей сцене. Для правок настроек частиц нажмите Start / Refresh. Материалы обновляются сразу. Все слои воспроизводятся вместе; Quality = Low по-прежнему исключает декоративные слои.",MessageType.None);
        }
        void StartPreview()
        {
            StopPreview();if(!source || Application.isPlaying)return;
            // Instantiate inactive to avoid a transient auto-play before initialization.
            var staging=new GameObject("Dust preview staging");
            staging.hideFlags=HideFlags.HideAndDontSave;staging.SetActive(false);
            var go=Object.Instantiate(source.gameObject,staging.transform);
            go.name="Dust temporary preview";
            foreach(var tr in go.GetComponentsInChildren<Transform>(true))tr.gameObject.hideFlags=HideFlags.HideAndDontSave;
            go.transform.SetParent(null,true);
            if(!EditorUtility.IsPersistent(source) && source.gameObject.scene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(go,source.gameObject.scene);
                go.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);
                go.transform.localScale=source.transform.lossyScale;
            }
            else
            {
                go.transform.position=SceneView.lastActiveSceneView?SceneView.lastActiveSceneView.pivot:Vector3.zero;
            }
            preview=go.GetComponent<DustEffect>();preview.playOnEnable=false;
            // Attached game adapters must not participate in an editor-only preview.
            foreach(var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))
                if(behaviour!=preview)behaviour.enabled=false;
            Object.DestroyImmediate(staging);go.SetActive(true);
            preview.Initialize();systems=preview.Systems;cycle=.25f;
            foreach(var ps in systems)
            {
                var main=ps.main;
                cycle=Mathf.Max(cycle,main.duration+main.startDelay.constantMax+main.startLifetime.constantMax);
            }
            paused=false;BeginCycle();lastTick=EditorApplication.timeSinceStartup;
        }
        void BeginCycle()
        {
            elapsed=0;if(varySeed)seed+=7919;
            preview.SimulatePreview(0,seed);
        }
        void Tick()
        {
            double now=EditorApplication.timeSinceStartup;
            if(!preview || paused || Application.isPlaying){lastTick=now;return;}
            if(!source){StopPreview();return;}
            double delta=now-lastTick;if(delta<1.0/60)return;lastTick=now;
            float dt=Mathf.Min((float)delta,.05f)*speed;
            elapsed+=dt;
            foreach(var ps in systems)
            {
                var e=ps.emission;var original=e.rateOverTime;var distance=e.rateOverDistance;
                // Stationary approximation for rate-over-distance; no scene transforms are moved.
                if(e.rateOverDistanceMultiplier>0)
                {e.rateOverTime=e.rateOverTimeMultiplier+e.rateOverDistanceMultiplier*motionSpeed;e.rateOverDistance=0;}
                ps.Simulate(dt,false,false,false);
                e.rateOverTime=original;e.rateOverDistance=distance;
            }
            if(!preview.isContinuous && elapsed>=cycle+gap)BeginCycle();
            SceneView.RepaintAll();Repaint();
        }
        void StopPreview()
        {
            if(preview)Object.DestroyImmediate(preview.gameObject);
            preview=null;systems=null;paused=false;SceneView.RepaintAll();
        }
    }
}
