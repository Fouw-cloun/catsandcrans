using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
namespace CatCooks.Builders
{
    // A bundle owns all pieces; only state transitions are synchronized, never per-brick physics.
    [DefaultExecutionOrder(200),RequireComponent(typeof(CCBuilderWorkItem))]
    public sealed class CCBuilderBrickSpill : NetworkBehaviour
    {
        public Transform[] Bricks=new Transform[4];
        public struct SpillState
        {
            public bool Active,Settled;
            public uint Sequence;
            public int Collected;
            public Vector3 Start,P0,P1,P2,P3;
        }
        private readonly SyncVar<SpillState> _spill=new(new SyncTypeSettings(0f));
        private readonly Vector3[] _local=new Vector3[4];
        private readonly Quaternion[] _rot=new Quaternion[4];
        private CCBuilderWorkItem _item;
        private uint _seen;
        private float _since;
        private bool _wasActive;
        public int Count => Mathf.Min(Bricks==null?0:Bricks.Length,Mathf.Clamp(_item.BundleCount,1,4));
        private void Awake()
        {
            _item=GetComponent<CCBuilderWorkItem>();
            for(int i=0;i<4 && i<Bricks.Length;i++) if(Bricks[i]!=null) { _local[i]=Bricks[i].localPosition;_rot[i]=Bricks[i].localRotation; }
            Restore();
        }
        public void Scatter(CCBuilderCat cat)
        {
            if(!IsServerInitialized || cat==null || !_item.HeldBy(cat)) return;
            Vector3 origin=cat.LastGroundPosition;
            SpillState s=new SpillState { Active=true,Sequence=_spill.Value.Sequence+1,Start=transform.position };
            s.P0=cat.ScatterGround(origin+cat.transform.forward*.7f+cat.transform.right*.5f);
            s.P1=cat.ScatterGround(origin+cat.transform.forward*1.1f-cat.transform.right*.6f);
            s.P2=cat.ScatterGround(origin-cat.transform.forward*.6f+cat.transform.right*.8f);
            s.P3=cat.ScatterGround(origin-cat.transform.forward*.9f-cat.transform.right*.7f);
            _spill.Value=s;
        }
        public Vector3 Ground(int index)
        { SpillState s=_spill.Value;return index==0?s.P0:index==1?s.P1:index==2?s.P2:s.P3; }
        public void Settle() { if(IsServerInitialized) { SpillState s=_spill.Value;s.Settled=true;_spill.Value=s; } }
        public void Collect(int count) { if(IsServerInitialized) { SpillState s=_spill.Value;s.Collected=Mathf.Clamp(count,0,Count);_spill.Value=s; } }
        public void Finish() { if(IsServerInitialized) { SpillState s=_spill.Value;s.Active=false;_spill.Value=s;Restore(); } }
        private void Restore()
        {
            for(int i=0;i<4 && i<Bricks.Length;i++) if(Bricks[i]!=null)
            { Bricks[i].gameObject.SetActive(i<Count);Bricks[i].localPosition=_local[i];Bricks[i].localRotation=_rot[i]; }
        }
        private void LateUpdate()
        {
            if(!IsServerInitialized && !IsClientInitialized) return;
            SpillState s=_spill.Value;
            if(!s.Active) { if(_wasActive) Restore();_wasActive=false;return; }
            if(_seen!=s.Sequence) { _seen=s.Sequence;_since=Time.time; }
            _wasActive=true;
            float t=s.Settled?1:Mathf.Clamp01((Time.time-_since)/.65f);
            for(int i=0;i<Count;i++) if(Bricks[i]!=null)
            {
                if(i<s.Collected) { Bricks[i].localPosition=_local[i];Bricks[i].localRotation=_rot[i];continue; }
                Vector3 end=Ground(i);
                Bricks[i].SetPositionAndRotation(Vector3.Lerp(s.Start,end,t)+Vector3.up*(Mathf.Sin(t*Mathf.PI)*.35f),Quaternion.Euler(0,31+i*73,0));
            }
        }
        public override void OnStopNetwork() { Restore();_wasActive=false;_seen=0;base.OnStopNetwork(); }
    }
}
