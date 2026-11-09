using FishNet.Object;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Откидная крышка духовки как «часть» (без своего NetworkObject): смотришь на крышку, F — открыть / закрыть.
    /// Вешается на меш крышки (нужен коллайдер). Сам находит Oven Hinged Lid в родителях.
    /// </summary>
    public class OvenLidPart : MonoBehaviour, IVehiclePart
    {
        [Tooltip("Номер части — уникальный внутри духовки (люк — 7)")]
        [SerializeField] private int _partId = 8;

        private OvenHingedLid _lid;
        private NetworkObject _owningNob;

        private void Awake()
        {
            _lid = GetComponentInParent<OvenHingedLid>();
            _owningNob = GetComponentInParent<NetworkObject>();
            if (_lid == null) Debug.LogError($"[Крышка духовки] «{name}»: Oven Lid Part не под Oven Hinged Lid (корень духовки)", this);
        }

        public NetworkObject OwningNetworkObject => _owningNob;
        public int PartId => _partId;

        public void ServerUse(PlayerInteractor interactor)
        {
            if (_lid != null) _lid.ServerToggle();
        }
    }
}
