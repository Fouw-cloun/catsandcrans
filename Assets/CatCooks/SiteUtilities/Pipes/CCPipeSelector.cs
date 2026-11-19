using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace CatCooks.SiteTools
{
    public sealed class CCPipeSelector : NetworkBehaviour, IVehiclePart
    {
        public Transform handle;
        public HydrantNut valve;
        public GameObject openIndicator, closedIndicator;
        readonly SyncVar<bool> opened=new(false);
        public bool Open => opened.Value;
        public NetworkObject OwningNetworkObject => NetworkObject;
        public int PartId => 12;
        public void ServerUse(PlayerInteractor player)
        {
            if(IsServerInitialized && player!=null && Vector3.Distance(player.transform.position,handle.position)<4f)
            {
                opened.Value=!opened.Value;
                valve.ServerSetLever(opened.Value);
            }
        }
        void Update()
        {
            float angle=opened.Value?90f:0f;
            handle.localRotation=Quaternion.RotateTowards(handle.localRotation,Quaternion.Euler(0,angle,0),Time.deltaTime*240f);
            if(openIndicator.activeSelf!=opened.Value)openIndicator.SetActive(opened.Value);
            if(closedIndicator.activeSelf==opened.Value)closedIndicator.SetActive(!opened.Value);
        }
    }
}
