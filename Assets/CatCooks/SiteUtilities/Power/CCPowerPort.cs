using System.Collections.Generic;
using UnityEngine;
namespace CatCooks.SiteTools
{
    public sealed class CCPowerPort : MonoBehaviour
    {
        public Transform left,right;
        public CCPowerPanel panel;
        public Light[] beams;
        public Renderer[] lenses;
        public Material dark, luminous;
        public readonly List<CCWireSpan> links=new List<CCWireSpan>();
        readonly HashSet<CCPowerPort> visited=new HashSet<CCPowerPort>();
        readonly Queue<CCPowerPort> queue=new Queue<CCPowerPort>();
        float next;bool initialized,on;
        public bool Powered {get;private set;}
        void Update(){
            if(Time.unscaledTime<next)return;
            next=Time.unscaledTime+.18f;
            Powered=HasPower();
            if(initialized && on==Powered)return;
            initialized=true;on=Powered;
            if(beams!=null)foreach(var l in beams)if(l!=null)l.enabled=on;
            if(lenses!=null)foreach(var r in lenses)if(r!=null)r.sharedMaterial=on?luminous:dark;
        }
        bool HasPower(){
            visited.Clear();queue.Clear();queue.Enqueue(this);visited.Add(this);
            while(queue.Count>0){
                var p=queue.Dequeue();
                if(p.panel!=null){
                    if(!p.panel.ClosedCircuit)continue;
                    if(p.panel.source)return true;
                }
                foreach(var link in p.links){
                    if(link==null || !link.isActiveAndEnabled)continue;
                    var other=link.a==p?link.b:link.a;
                    if(other!=null && other.isActiveAndEnabled && visited.Add(other))queue.Enqueue(other);
                }
            }
            return false;
        }
    }
}
