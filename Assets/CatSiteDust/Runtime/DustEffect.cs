using System.Collections.Generic;
using UnityEngine;

namespace CatSite.Dust
{
    public enum DustQuality { Low, Standard, High }

    /// <summary>Controls authored child ParticleSystems. Does not allocate particles or materials per frame.</summary>
    [DisallowMultipleComponent]
    public sealed class DustEffect : MonoBehaviour
    {
        [Tooltip("Disable for effects managed by DustPool.")]
        public bool playOnEnable = true;
        public bool isContinuous;
        public DustQuality quality = DustQuality.Standard;
        [Range(.1f, 3f)] public float density = 1f;
        [Tooltip("Decorative layers omitted at Low quality.")]
        public ParticleSystem[] detailLayers;
        public Mesh[] puffVariants;
        ParticleSystem[] systems;
        ParticleSystemRenderer[] renderers;
        ParticleSystem.MinMaxGradient[] colors;
        float[] timeRates, distanceRates;
        ParticleSystem.Burst[][] bursts;
        bool[] detail;
        float intensity = 1f, emissionGain = 1f;
        Color tint = Color.white;
        Vector3 authoredScale;
        internal DustPool Owner;
        internal float SpawnedAt;
        static readonly List<ParticleSystemVertexStream> Streams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal,
            ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.StableRandomX
        };
        public ParticleSystem[] Systems { get { Initialize(); return systems; } }
        public bool IsAlive
        {
            get { Initialize(); for (int i=0;i<systems.Length;i++) if(systems[i].IsAlive(false)) return true; return false; }
        }
        void Awake() { Initialize(); }
        void OnEnable() { if (playOnEnable) Play(); }
        void OnDisable() { if (systems != null) Stop(true); }
        public void Initialize()
        {
            if(systems != null) return;
            authoredScale=transform.localScale;
            systems=GetComponentsInChildren<ParticleSystem>(true);
            int n=systems.Length;
            renderers=new ParticleSystemRenderer[n]; colors=new ParticleSystem.MinMaxGradient[n];
            timeRates=new float[n]; distanceRates=new float[n]; bursts=new ParticleSystem.Burst[n][];
            detail=new bool[n];
            for(int i=0;i<n;i++)
            {
                var ps=systems[i]; var e=ps.emission;
                var main=ps.main; main.simulationSpace=ParticleSystemSimulationSpace.World;
                main.scalingMode=ParticleSystemScalingMode.Hierarchy;
                main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
                colors[i]=ps.main.startColor; timeRates[i]=e.rateOverTimeMultiplier;
                distanceRates[i]=e.rateOverDistanceMultiplier;
                bursts[i]=new ParticleSystem.Burst[e.burstCount]; e.GetBursts(bursts[i]);
                renderers[i]=ps.GetComponent<ParticleSystemRenderer>();
                if(renderers[i])
                {
                    renderers[i].SetActiveVertexStreams(Streams);
                    // These custom shaders use CPU-batched particle vertices, not the particle instancing ABI.
                    renderers[i].enableGPUInstancing=false;
                    if(renderers[i].renderMode==ParticleSystemRenderMode.Mesh && puffVariants!=null && puffVariants.Length>0)
                        renderers[i].SetMeshes(puffVariants);
                }
                if(detailLayers != null) for(int j=0;j<detailLayers.Length;j++) if(detailLayers[j]==ps) detail[i]=true;
            }
        }
        public void Configure(float strength, float scale, Color color, DustQuality level)
        {
            Initialize(); intensity=Mathf.Clamp(strength,.1f,2f); tint=color; quality=level;
            transform.localScale=authoredScale*Mathf.Clamp(scale,.05f,8f);
            ApplySettings();
        }
        public void SetEmission(float value)
        {
            emissionGain=Mathf.Clamp01(value); Initialize(); ApplySettings();
        }
        void ApplySettings()
        {
            float q=quality==DustQuality.Low?.55f:quality==DustQuality.High?1.2f:1f;
            float amount=q*Mathf.Clamp(density,.1f,3f)*intensity;
            for(int i=0;i<systems.Length;i++)
            {
                bool enabledLayer=!(quality==DustQuality.Low && detail[i]);
                var main=systems[i].main;
                main.startColor=new ParticleSystem.MinMaxGradient(colors[i].colorMin*tint,colors[i].colorMax*tint);
                var e=systems[i].emission;
                e.enabled=enabledLayer;
                e.rateOverTimeMultiplier=timeRates[i]*amount*emissionGain;
                e.rateOverDistanceMultiplier=distanceRates[i]*amount*emissionGain;
                for(int j=0;j<bursts[i].Length;j++)
                {
                    var b=bursts[i][j];
                    b.count=new ParticleSystem.MinMaxCurve(Mathf.Max(1,Mathf.RoundToInt(b.count.constant*amount)));
                    e.SetBurst(j,b);
                }
                if(renderers[i]) renderers[i].enabled=enabledLayer;
                if(!enabledLayer) systems[i].Clear(false);
            }
        }
        public void Play(uint seed=0)
        {
            Initialize(); Stop(true); ApplySettings();
            for(int i=0;i<systems.Length;i++)
            {
                if(quality==DustQuality.Low && detail[i]) continue;
                systems[i].useAutoRandomSeed=seed==0;
                if(seed!=0) systems[i].randomSeed=seed+(uint)(i*7919);
                systems[i].Play(false);
            }
        }
        public void Stop(bool clear=false)
        {
            Initialize();
            foreach(var ps in systems) ps.Stop(false,clear?ParticleSystemStopBehavior.StopEmittingAndClear:ParticleSystemStopBehavior.StopEmitting);
        }
        public void SimulatePreview(float time,uint seed=1234)
        {
            Initialize(); Stop(true); ApplySettings();
            for(int i=0;i<systems.Length;i++)
            {
                if(quality==DustQuality.Low && detail[i]) continue;
                systems[i].useAutoRandomSeed=false; systems[i].randomSeed=seed+(uint)(i*7919);
                systems[i].Simulate(Mathf.Max(0,time),false,true,true);
            }
        }
        public int ParticleCount { get { Initialize(); int c=0; foreach(var ps in systems)c+=ps.particleCount; return c; } }
    }
}
