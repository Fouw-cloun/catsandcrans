using FishNet.Object;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Люк на крышке духовки (часть без своего NetworkObject). Наводишь камеру на люк, F — открыть / закрыть.
    /// Нужен коллайдер на этом объекте. Ставится сам (CatCooks → Хлеб → Авто: духовка).
    /// </summary>
    public class OvenHatchPart : MonoBehaviour, IVehiclePart
    {
        [SerializeField] private int _partId = 7;

        private OvenVentHatch _hatch;
        private NetworkObject _owningNob;

        private void Awake()
        {
            _hatch = GetComponentInParent<OvenVentHatch>();
            _owningNob = GetComponentInParent<NetworkObject>();
            if (_hatch == null) Debug.LogError($"[Люк] «{name}»: Oven Hatch Part не под OvenVentHatch (корень крышки)", this);
        }

        public NetworkObject OwningNetworkObject => _owningNob;
        public int PartId => _partId;

        public void ServerUse(PlayerInteractor interactor)
        {
            if (_hatch != null) _hatch.ServerToggle();
        }
    }
}
