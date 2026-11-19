using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
namespace CatCooks.SiteTools
{
    public sealed class CCPowerPanel : NetworkBehaviour
    {
        public bool source;
        public Transform door;
        public Transform[] levers;
        public Renderer[] indicators;
        public Material[] glowing;
        public Material dark;
        public CCPowerPort port;
        // bit 0 = door, bit 1 = input, bit 2 = output
        readonly SyncVar<int> state=new(6);
        public bool ClosedCircuit=>(state.Value&2)!=0 && (!source || (state.Value&4)!=0);
        public void Operate(int bit){
            if(!IsServerInitialized || bit<0 || bit>(source?2:1))return;
            if(bit>0 && (state.Value&1)==0)return;
            state.Value^=1<<bit;
        }
        void Update(){
            int v=state.Value;
            door.localRotation=Quaternion.RotateTowards(door.localRotation,Quaternion.Euler(0,(v&1)!=0?-108f:0,0),240f*Time.deltaTime);
            for(int i=0;i<levers.Length;i++)levers[i].localRotation=Quaternion.Euler((v&(2<<i))!=0?-24:24,0,0);
            for(int i=0;i<indicators.Length;i++){
                bool on=(i==0 || i==3)?!ClosedCircuit:i==1?(v&1)!=0:port.Powered;
                var m=on?glowing[i]:dark;
                if(indicators[i].sharedMaterial!=m)indicators[i].sharedMaterial=m;
            }
        }
    }
}
