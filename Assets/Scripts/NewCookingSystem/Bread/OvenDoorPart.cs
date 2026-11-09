using FishNet.Object;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// УСТАРЕЛО (v1): верхняя половина духовки теперь снимается как крышка (PotLidItem), а не откидывается.
    /// Компонент оставлен, чтобы старые объекты не ломались, — убери его. Люк на крышке — OvenVentHatch + OvenHatchPart.
    /// </summary>
    [System.Obsolete("Крышка духовки теперь снимается как крышка кастрюли — убери Oven Door Part")]
    public class OvenDoorPart : MonoBehaviour, IVehiclePart
    {
        [SerializeField] private int _partId = 0;
        private NetworkObject _owningNob;
        private void Awake() { _owningNob = GetComponentInParent<NetworkObject>(); }
        public NetworkObject OwningNetworkObject => _owningNob;
        public int PartId => _partId;
        public void ServerUse(PlayerInteractor interactor)
        {
            Debug.Log($"[Духовка] «{name}»: Oven Door Part устарел — крышка снимается F, люк на ней открывается F", this);
        }
    }
}
