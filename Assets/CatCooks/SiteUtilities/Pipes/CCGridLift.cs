using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.SiteTools
{
    public sealed class CCGridLift : NetworkBehaviour
    {
        [Min(1)] public float storey=4;
        [Min(.1f)] public float speed=1.5f;
        public Transform platform;
        readonly SyncVar<float> height=new(0f);
        readonly SyncVar<bool> down=new(false);
        public void Request(int stop)
        {
            if(IsServerInitialized) down.Value=stop==2?!down.Value:stop==1;
        }
        public override void OnStartClient()
        {
            base.OnStartClient();
            platform.localPosition=Vector3.up*height.Value;
        }
        void Update()
        {
            float step=speed*Time.deltaTime;
            if(IsServerInitialized)
                height.Value=Mathf.MoveTowards(height.Value,down.Value?-storey:0f,step);
            if(IsServerInitialized || IsClientInitialized)
            {
                float y=IsServerInitialized?height.Value:Mathf.MoveTowards(platform.localPosition.y,height.Value,step*1.35f);
                platform.localPosition=Vector3.up*y;
            }
        }
    }
}
