using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
namespace CatCooks.Builders
{
    [RequireComponent(typeof(NetworkObject)),DisallowMultipleComponent]
    public sealed class CCBuilderSandPile : NetworkBehaviour
    {
        public Transform Visual;
        [Min(0)] public float InitialAmount=120;
        [Min(1)] public float Capacity=240,ModelAmount=120;
        [Min(.05f)] public float ResizeSeconds=.4f;
        private readonly SyncVar<float> _amount=new(new SyncTypeSettings(.1f));
        private Vector3 _baseScale;
        private float _shown=-1;
        public float Amount => Mathf.Max(0,_amount.Value);
        public float Free => Mathf.Max(0,Capacity-Amount);
        private void Awake() { if(Visual!=null) _baseScale=Visual.localScale; }
        public override void OnStartServer() { base.OnStartServer();_amount.Value=Mathf.Clamp(InitialAmount,0,Mathf.Max(1,Capacity)); }
        public float TransferTo(CCBuilderSandPile target,float requested)
        {
            if(!IsServerInitialized || !isActiveAndEnabled || target==null || target==this || !target.IsServerInitialized ||
                !target.isActiveAndEnabled || float.IsNaN(requested) || float.IsInfinity(requested)) return 0;
            float moved=Mathf.Min(Mathf.Max(0,requested),Mathf.Min(Amount,target.Free));
            if(moved<=0) return 0;
            _amount.Value=Amount-moved;target._amount.Value=target.Amount+moved;return moved;
        }
        private void Update()
        {
            if(Visual==null || (!IsClientInitialized && !IsServerInitialized)) return;
            float target=Mathf.Pow(Amount/Mathf.Max(1,ModelAmount),1f/3f);
            if(_shown==target) return;
            _shown=_shown<0?target:Mathf.MoveTowards(_shown,target,Time.deltaTime/Mathf.Max(.05f,ResizeSeconds));
            Visual.gameObject.SetActive(_shown>.001f);Visual.localScale=_baseScale*_shown;
        }
        public override void OnStopNetwork() { _shown=-1;if(Visual!=null) { Visual.gameObject.SetActive(true);Visual.localScale=_baseScale; }base.OnStopNetwork(); }
    }
}
