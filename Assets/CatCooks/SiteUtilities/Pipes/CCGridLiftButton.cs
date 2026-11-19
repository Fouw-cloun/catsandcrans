using FishNet.Object;
using UnityEngine;
namespace CatCooks.SiteTools
{
    public sealed class CCGridLiftButton : MonoBehaviour,IVehiclePart
    {
        public CCGridLift lift;
        public NetworkObject station;
        public int stop;
        public NetworkObject OwningNetworkObject=>station;
        public int PartId=>470+stop;
        public void ServerUse(PlayerInteractor user)
        {
            if(user!=null && lift!=null && (user.transform.position-transform.position).sqrMagnitude<16)
                lift.Request(stop);
        }
    }
}
