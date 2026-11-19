using FishNet.Object;
using UnityEngine;
namespace CatCooks.SiteTools
{
    public sealed class CCPanelControl : MonoBehaviour,IVehiclePart
    {
        public CCPowerPanel panel;
        public int bit;
        public NetworkObject OwningNetworkObject=>panel.NetworkObject;
        public int PartId=>610+bit;
        public void ServerUse(PlayerInteractor player){
            if(player!=null && (player.transform.position-transform.position).sqrMagnitude<16f)panel.Operate(bit);
        }
    }
}
