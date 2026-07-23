using UnityEngine;
#if UNITY_EDITOR
using FishNet;
using FishNet.Managing;
using Steamworks;
#endif

/// <summary>
/// DEV-ТОЛЬКО логика, но САМ КЛАСС существует всегда (не обёрнут в #if целиком),
/// иначе объект в сцене с этим компонентом становится "битым" в билде и роняет
/// загрузку сцены. Поэтому под #if UNITY_EDITOR спрятано только ТЕЛО методов.
///
/// В билде компонент просто ничего не делает (Update пустой).
///
///   H — поднять ХОСТ (server + client) и загрузить игровую сцену.
///   J — подключиться КЛИЕНТОМ (для клона ParrelSync).
///
/// Use Localhost: ВКЛ для Tugboat (127.0.0.1), ВЫКЛ для FishySteamworks (SteamID).
/// </summary>
public class DevDirectConnect : MonoBehaviour
{
    [Tooltip("ВКЛ для Tugboat (localhost), ВЫКЛ для FishySteamworks (SteamID)")]
    [SerializeField] private bool _useLocalhost = true;
    [SerializeField] private string _localhostAddress = "127.0.0.1";

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.H)) HostSelf();
        if (Input.GetKeyDown(KeyCode.J)) JoinSelf();
#endif
        // В билде метод пустой — компонент существует, но бездействует.
    }

#if UNITY_EDITOR
    private void HostSelf()
    {
        if (SteamLobby.Instance == null)
        {
            Debug.LogError("[DevDirectConnect] Нет SteamLobby.Instance.");
            return;
        }
        Debug.Log("[DevDirectConnect] HOST: поднимаю сервер+клиент и гружу игру.");
        SteamLobby.Instance.StartSinglePlayer();
    }

    private void JoinSelf()
    {
        NetworkManager nm = InstanceFinder.NetworkManager;
        if (nm == null)
        {
            Debug.LogError("[DevDirectConnect] Нет NetworkManager.");
            return;
        }
        if (nm.IsServerStarted)
        {
            Debug.LogWarning("[DevDirectConnect] Этот редактор уже хост — J не нужен.");
            return;
        }

        string address;
        if (_useLocalhost)
        {
            address = _localhostAddress;
        }
        else
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError("[DevDirectConnect] Steam не инициализирован.");
                return;
            }
            address = SteamUser.GetSteamID().ToString();
        }

        Debug.Log($"[DevDirectConnect] JOIN: коннект клиентом на адрес {address}.");
        nm.TransportManager.Transport.SetClientAddress(address);
        nm.ClientManager.StartConnection();
    }
#endif
}
