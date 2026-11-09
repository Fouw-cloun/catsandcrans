using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatSite.Dust.Editor
{
    [CustomEditor(typeof(DustEffect))]
    public sealed class DustEffectEditor : UnityEditor.Editor
    {
        float previewTime;
        int selectedLayer;
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();var fx=(DustEffect)target;
            EditorGUILayout.Space();
            if(!Application.isPlaying && GUILayout.Button("Loop Preview — all layers"))DustLoopPreview.Open(fx);
            if(EditorUtility.IsPersistent(fx))
                EditorGUILayout.HelpBox("Перетащите префаб в сцену или нажмите Loop Preview — all layers.",MessageType.Info);
            else
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    if(GUILayout.Button("Replay"))
                    {
                        if(Application.isPlaying)fx.Play();else{previewTime=.18f;fx.SimulatePreview(previewTime);SceneView.RepaintAll();}
                    }
                    if(GUILayout.Button("Clear")){fx.Stop(true);previewTime=0;SceneView.RepaintAll();}
                }
                if(!Application.isPlaying)
                {
                    var layers=fx.Systems;
                    string[] names=new string[layers.Length];
                    for(int k=0;k<layers.Length;k++)names[k]=layers[k].name;
                    selectedLayer=EditorGUILayout.Popup("Isolate layer",selectedLayer,names);
                    if(GUILayout.Button("Preview isolated layer (0.4s)"))
                    {
                        fx.Stop(true);
                        for(int k=0;k<layers.Length;k++)
                        {
                            var renderer=layers[k].GetComponent<ParticleSystemRenderer>();
                            if(renderer)renderer.enabled=k==selectedLayer;
                        }
                        var layer=layers[selectedLayer];var emission=layer.emission;
                        var original=emission.rateOverTime;
                        if(emission.rateOverTimeMultiplier==0 && emission.rateOverDistanceMultiplier>0)
                            emission.rateOverTime=emission.rateOverDistanceMultiplier*2;
                        layer.Simulate(.4f,false,true,true);emission.rateOverTime=original;
                        SceneView.RepaintAll();
                    }
                    float t=EditorGUILayout.Slider("Preview seconds",previewTime,0,2.4f);
                    if(Mathf.Abs(t-previewTime)>.001f){previewTime=t;fx.SimulatePreview(t);SceneView.RepaintAll();}
                    if(fx.isContinuous)EditorGUILayout.HelpBox("Rate over Distance требует движения источника. В Loop Preview задайте Distance emission (m/s).",MessageType.Info);
                }
            }
        }
    }
    public static class DustTools
    {
        const string Root="Assets/CatSiteDust";
        [MenuItem("Tools/Cat Site Dust/Select Prefab Folder")]
        public static void Open()
        {
            Selection.activeObject=AssetDatabase.LoadAssetAtPath<DefaultAsset>(Root+"/Prefabs");
        }
        [MenuItem("Tools/Cat Site Dust/Validate Package")]
        public static void Validate()
        {
            int errors=0,checkedCount=0;
            if(GraphicsSettings.currentRenderPipeline==null || !GraphicsSettings.currentRenderPipeline.GetType().Name.Contains("Universal"))
            {Debug.LogError("Cat Site Dust requires URP. Assign a URP asset in Graphics/Quality settings.");errors++;}
            var ids=AssetDatabase.FindAssets("t:Prefab",new[]{Root+"/Prefabs"});
            foreach(var id in ids)
            {
                string path=AssetDatabase.GUIDToAssetPath(id);
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!prefab){errors++;continue;}
                var instance=UnityEngine.Object.Instantiate(prefab);instance.hideFlags=HideFlags.HideAndDontSave;
                try
                {
                    var fx=instance.GetComponent<DustEffect>();
                    if(!fx)throw new Exception("DustEffect missing");
                    fx.Initialize();
                    foreach(var ps in fx.Systems)
                    {
                        var r=ps.GetComponent<ParticleSystemRenderer>();
                        if(!r || !r.sharedMaterial || !r.sharedMaterial.shader)throw new Exception("Missing material/shader on "+ps.name);
                        if(ShaderUtil.ShaderHasError(r.sharedMaterial.shader))throw new Exception("Shader compile error: "+r.sharedMaterial.shader.name);
                        if(r.renderMode==ParticleSystemRenderMode.Mesh && (!r.mesh || r.mesh.vertexCount<3))throw new Exception("Mesh import failed on "+ps.name);
                        if(ps.main.loop!=fx.isContinuous)throw new Exception("Loop flag mismatch on "+ps.name);
                        if(ps.main.startLifetime.constantMax<=0)throw new Exception("Invalid lifetime");
                        if(ps.main.maxParticles>128)throw new Exception("Unexpected particle cap");
                    }
                    fx.SimulatePreview(.25f);
                    if(!fx.isContinuous && fx.ParticleCount==0)throw new Exception("Burst produced zero particles at 0.25s");
                    if(!fx.isContinuous)
                    {
                        fx.SimulatePreview(5f);
                        if(fx.IsAlive)throw new Exception("One-shot still alive after 5s");
                    }
                    checkedCount++;
                }
                catch(Exception e){errors++;Debug.LogError(path+": "+e.Message,prefab);}
                finally{UnityEngine.Object.DestroyImmediate(instance);}
            }
            Debug.Log($"Cat Site Dust validation: {checkedCount}/{ids.Length} presets passed; {errors} errors. This is an import/simulation check, not a GPU benchmark.");
            if(Application.isBatchMode && errors>0)throw new Exception("Cat Site Dust validation failed");
        }
        [MenuItem("Tools/Cat Site Dust/Create Pool In Scene")]
        public static void CreatePool()
        {
            var ids=AssetDatabase.FindAssets("t:Prefab",new[]{Root+"/Prefabs"});var entries=new List<DustPool.Entry>();
            foreach(var id in ids)
            {
                var go=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(id));var fx=go?go.GetComponent<DustEffect>():null;
                if(fx && !fx.isContinuous)entries.Add(new DustPool.Entry{prefab=fx,capacity=6});
            }
            var root=new GameObject("Cat Site Dust Pool");Undo.RegisterCreatedObjectUndo(root,"Create Dust Pool");
            var pool=root.AddComponent<DustPool>();pool.entries=entries.ToArray();Selection.activeGameObject=root;
            EditorSceneManager.MarkSceneDirty(root.scene);
        }
    }
}
