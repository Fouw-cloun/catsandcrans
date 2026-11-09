// Enable CATCOOKS_FOOD_FISHNET only when FishNet is installed in the project.
#if CATCOOKS_FOOD_FISHNET
using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace CatCooks.FoodLab
{
    [RequireComponent(typeof(CCFoodWorld),typeof(NetworkObject))]
    public sealed class CCFoodFishNet : NetworkBehaviour
    {
        [Range(2,15)] public float snapshotsPerSecond=8;
        [Tooltip("Laboratory commands only. Production should validate player reach, held tool and ingredient ownership.")]
        public bool allowRemoteLabCommands=true;
        CCFoodWorld world;float nextSnapshot;uint sequence;
        readonly FoodSnapshotAssembler assembler=new FoodSnapshotAssembler();
        readonly Dictionary<int,float> lastMotion=new Dictionary<int,float>(),lastSpawn=new Dictionary<int,float>();
        public int bytesPerSnapshot {get;private set;}
        void Awake()
        {world=GetComponent<CCFoodWorld>();world.networkControlled=true;world.simulate=false;world.CommandRequested+=Request;}
        void OnDestroy(){if(world!=null)world.CommandRequested-=Request;}
        public override void OnStartNetwork()
        {base.OnStartNetwork();assembler.Reset();world.Initialize();world.networkControlled=true;world.simulate=IsServerInitialized;}
        public override void OnStartServer()
        {base.OnStartServer();world.Initialize();world.simulate=true;nextSnapshot=0;sequence=0;}
        public override void OnStopServer()
        {world.simulate=false;base.OnStopServer();}
        public override void OnStopNetwork()
        {base.OnStopNetwork();assembler.Reset();world.simulate=false;world.Solver.Clear();lastMotion.Clear();lastSpawn.Clear();}
        void Request(FoodCommand command)
        {
            if(IsServerInitialized){world.Execute(command,0);return;}
            if(IsClientInitialized)SendCommand(command);
        }
        [ServerRpc(RequireOwnership=false)]
        void SendCommand(FoodCommand command,NetworkConnection sender=null)
        {
            if(sender==null||!allowRemoteLabCommands||command.kind==FoodCommandKind.Clear)return;
            int owner=sender.ClientId+1;float now=Time.unscaledTime;
            if(command.kind==FoodCommandKind.Spawn)
            {
                if(lastSpawn.TryGetValue(owner,out float last)&&now-last<.5f)return;
                lastSpawn[owner]=now;command.grams=Mathf.Clamp(command.grams,1,7000);
            }
            else if(command.kind==FoodCommandKind.Drag||command.kind==FoodCommandKind.Stir||command.kind==FoodCommandKind.Push)
            {
                if(lastMotion.TryGetValue(owner,out float last)&&now-last<.025f)return;
                lastMotion[owner]=now;
            }
            world.Execute(command,owner);
        }
        void Update()
        {
            if(!IsServerInitialized||Time.unscaledTime<nextSnapshot)return;
            nextSnapshot=Time.unscaledTime+1/snapshotsPerSecond;
            byte[] data=CCFoodSnapshot.Encode(world);bytesPerSnapshot=data.Length;
            const int size=800;int chunks=(data.Length+size-1)/size;sequence++;
            for(int i=0;i<chunks;i++)
            {
                int length=Math.Min(size,data.Length-i*size);byte[] chunk=new byte[length];Buffer.BlockCopy(data,i*size,chunk,0,length);
                Snapshot(sequence,i,chunks,chunk);
            }
        }
        // Reliable bounded chunks. No BufferLast: buffering only the LAST chunk would break late joins.
        // Every snapshot is complete, so a new observer obtains the whole state on the next interval.
        [ObserversRpc(BufferLast=false)]
        void Snapshot(uint seq,int index,int count,byte[] bytes)
        {
            if(IsServerInitialized)return;
            byte[] full=assembler.Push(seq,index,count,bytes);if(full==null)return;
            if(!CCFoodSnapshot.Apply(world,full,out string error))world.status="Сеть: "+error;
            else world.status="Слепок "+seq+": "+full.Length+" байт";
        }
    }
}
#endif
