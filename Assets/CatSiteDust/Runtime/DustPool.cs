using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatSite.Dust
{
    /// <summary>Bounded pool for bursts only. Saturation drops cosmetic requests instead of growing.</summary>
    [DisallowMultipleComponent]
    public sealed class DustPool : MonoBehaviour
    {
        [Serializable] public struct Entry { public DustEffect prefab; [Range(1,64)] public int capacity; }
        public Entry[] entries;
        [Range(1,256)] public int maxActive=40;
        public DustQuality quality=DustQuality.Standard;
        public Camera distanceCamera;
        [Min(0)] public float maxDistance=35f;
        public bool distanceCulling=true;
        sealed class Bucket { public readonly Stack<DustEffect> free=new Stack<DustEffect>(); }
        readonly Dictionary<DustEffect,Bucket> buckets=new Dictionary<DustEffect,Bucket>();
        readonly Dictionary<DustEffect,Bucket> owners=new Dictionary<DustEffect,Bucket>();
        readonly List<DustEffect> active=new List<DustEffect>(64);
        bool ready;
        public int ActiveCount=>active.Count;
        public int DroppedRequests { get; private set; }
        void Awake() { Prewarm(); }
        public void Prewarm()
        {
            if(ready) return; ready=true;
            if(!distanceCamera)distanceCamera=Camera.main;
            var staging=new GameObject("Inactive staging"); staging.transform.SetParent(transform,false); staging.SetActive(false);
            if(entries!=null) foreach(var entry in entries)
            {
                if(!entry.prefab || entry.prefab.isContinuous || buckets.ContainsKey(entry.prefab))continue;
                var bucket=new Bucket(); buckets.Add(entry.prefab,bucket);
                for(int i=0;i<Mathf.Clamp(entry.capacity,1,64);i++)
                {
                    var fx=Instantiate(entry.prefab,staging.transform);
                    fx.playOnEnable=false; fx.gameObject.SetActive(false); fx.transform.SetParent(transform,false);
                    fx.Initialize(); fx.Owner=this; bucket.free.Push(fx); owners.Add(fx,bucket);
                }
            }
            Destroy(staging);
        }
        public DustEffect Spawn(DustEffect prefab,Vector3 position,Vector3 normal,float strength=1f,float scale=1f)
        { return Spawn(prefab,position,normal,strength,scale,Color.white); }
        public DustEffect Spawn(DustEffect prefab,Vector3 position,Vector3 normal,float strength,float scale,Color tint)
        {
            Prewarm();
            if(!prefab || prefab.isContinuous || !buckets.TryGetValue(prefab,out var bucket))return null;
            if(active.Count>=maxActive || bucket.free.Count==0 ||
                (distanceCulling && distanceCamera && maxDistance>0 && (distanceCamera.transform.position-position).sqrMagnitude>maxDistance*maxDistance))
            { DroppedRequests++; return null; }
            var fx=bucket.free.Pop();
            if(normal.sqrMagnitude<.001f)normal=Vector3.up;
            fx.transform.SetPositionAndRotation(position,Quaternion.FromToRotation(Vector3.up,normal.normalized));
            fx.Configure(strength,scale,tint,quality); fx.SpawnedAt=Time.time;
            fx.gameObject.SetActive(true); fx.Play(); active.Add(fx); return fx;
        }
        void Update()
        {
            for(int i=active.Count-1;i>=0;i--)
            {
                var fx=active[i];
                if(fx && (Time.time-fx.SpawnedAt<.05f || fx.IsAlive))continue;
                if(fx) {fx.gameObject.SetActive(false); owners[fx].free.Push(fx);}
                int last=active.Count-1; active[i]=active[last]; active.RemoveAt(last);
            }
        }
        void OnDisable()
        {
            foreach(var fx in active) if(fx){fx.gameObject.SetActive(false); owners[fx].free.Push(fx);}
            active.Clear();
        }
    }
}
