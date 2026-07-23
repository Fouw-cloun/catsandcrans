using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Transporting;
using Steamworks;
using UnityEngine;

/// <summary>
/// Центральный скрипт сетевого слоя. Живёт в 00_Bootstrap, помечен
/// DontDestroyOnLoad. Отвечает за:
///   - создание / вход в Steam-лобби (SteamMatchmaking)
///   - старт FishNet server / client
///   - проброс адреса хоста в транспорт (FishySteamworks)
///   - загрузку нужной сцены при старте сервера:
///       * хост-мультиплеер -> сцена лобби 01b_Lobby
///       * одиночная игра    -> сразу игровая сцена 02_Game
///
/// ВАЖНО: Steam должен быть запущен, а steam_appid.txt (AppID 480 для
/// разработки) должен лежать в корне проекта.
/// </summary>
public class SteamLobby : MonoBehaviour
{
    public static SteamLobby Instance { get; private set; }

    [Header("Ссылки")]
    [Tooltip("Перетащи сюда NetworkManager из bootstrap-сцены")]
    [SerializeField] private NetworkManager _networkManager;

    [Header("Сцены")]
    [Tooltip("Сцена лобби (грузится при создании мультиплеерного лобби)")]
    [SerializeField] private string _lobbyScene = "01b_Lobby";
    [Tooltip("Игровая сцена (грузится напрямую в одиночной игре)")]
    [SerializeField] private string _gameScene = "02_Game";

    [Header("Настройки")]
    [SerializeField] private int _maxPlayers = 4;

    [Header("Dev (только редактор)")]
    [Tooltip("Если включено — в редакторе сразу поднять хост в Dev Scene, минуя меню")]
    [SerializeField] private bool _devSkipToGame = false;
    [Tooltip("Какой уровень грузить при dev-старте")]
    [SerializeField] private string _devScene = "02_Game";

    /// <summary>True, если сейчас сработает dev-пропуск меню (для GameBootstrap).</summary>
    public bool WillDevSkip => _devSkipToGame && Application.isEditor;

    private const string HostAddressKey = "HostAddress";
    public CSteamID CurrentLobbyID { get; private set; }

    private Callback<LobbyCreated_t> _lobbyCreated;
    private Callback<GameLobbyJoinRequested_t> _joinRequested;
    private Callback<LobbyEnter_t> _lobbyEntered;

    // Какую сцену грузить, как только поднимется сервер (пусто = ничего).
    private string _sceneOnServerStart;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("[SteamLobby] Steam НЕ инициализирован. " +
                           "Запущен ли Steam? Есть ли steam_appid.txt в корне проекта?");
            return;
        }

        _lobbyCreated  = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
        _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
        _lobbyEntered  = Callback<LobbyEnter_t>.Create(OnLobbyEntered);

        _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;

        // Dev-режим: в редакторе сразу поднимаем хост в указанном уровне.
        // Делаем это здесь (а не в GameBootstrap), чтобы гарантированно успеть
        // подписаться на OnServerConnectionState ДО старта сервера.
        if (WillDevSkip)
        {
            Debug.Log($"[SteamLobby] DEV: пропуск меню, старт хоста в '{_devScene}'.");
            _sceneOnServerStart = _devScene;
            _networkManager.ServerManager.StartConnection();
            _networkManager.ClientManager.StartConnection();
        }
    }

    private void OnDestroy()
    {
        if (_networkManager != null)
            _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
    }

    // ───────────────────────── ПУБЛИЧНЫЕ КНОПКИ ─────────────────────────

    /// <summary>Одиночная игра: локальный server+client, сразу в игровую сцену.</summary>
    public void StartSinglePlayer()
    {
        _sceneOnServerStart = _gameScene; // минуя лобби
        _networkManager.ServerManager.StartConnection();
        _networkManager.ClientManager.StartConnection();
    }

    /// <summary>Создать лобби (friends-only) и стать хостом -> сцена лобби.</summary>
    public void HostLobby()
    {
        _sceneOnServerStart = _lobbyScene;
        SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, _maxPlayers);
    }

    /// <summary>Отключиться: останавливаем сеть и выходим из лобби.</summary>
    public void Disconnect()
    {
        if (_networkManager.IsServerStarted) _networkManager.ServerManager.StopConnection(true);
        if (_networkManager.IsClientStarted) _networkManager.ClientManager.StopConnection();
        if (CurrentLobbyID.IsValid()) SteamMatchmaking.LeaveLobby(CurrentLobbyID);
    }

    // ───────────────────────── STEAM CALLBACKS ─────────────────────────

    private void OnLobbyCreated(LobbyCreated_t cb)
    {
        if (cb.m_eResult != EResult.k_EResultOK)
        {
            Debug.LogError("[SteamLobby] Не удалось создать лобби: " + cb.m_eResult);
            return;
        }

        CurrentLobbyID = new CSteamID(cb.m_ulSteamIDLobby);

        _networkManager.ServerManager.StartConnection();
        _networkManager.ClientManager.StartConnection();

        SteamMatchmaking.SetLobbyData(
            CurrentLobbyID, HostAddressKey, SteamUser.GetSteamID().ToString());
    }

    private void OnJoinRequested(GameLobbyJoinRequested_t cb)
    {
        // Друг принял инвайт / нажал "Join Game" в оверлее Steam.
        SteamMatchmaking.JoinLobby(cb.m_steamIDLobby);
    }

    private void OnLobbyEntered(LobbyEnter_t cb)
    {
        CurrentLobbyID = new CSteamID(cb.m_ulSteamIDLobby);

        // Хост уже поднял сервер в OnLobbyCreated — повторно не стартуем.
        if (_networkManager.IsServerStarted) return;

        string hostAddress = SteamMatchmaking.GetLobbyData(CurrentLobbyID, HostAddressKey);
        if (string.IsNullOrEmpty(hostAddress))
        {
            Debug.LogError("[SteamLobby] В лобби нет HostAddress.");
            return;
        }

        // Присоединяющийся клиент сцену лобби загрузит сам — она global,
        // FishNet синхронизирует её всем новым клиентам автоматически.
        _networkManager.TransportManager.Transport.SetClientAddress(hostAddress);
        _networkManager.ClientManager.StartConnection();
    }

    // ───────────────────────── FISHNET СЕРВЕР ─────────────────────────

    private void OnServerConnectionState(ServerConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started &&
            !string.IsNullOrEmpty(_sceneOnServerStart))
        {
            string scene = _sceneOnServerStart;
            _sceneOnServerStart = null;
            LoadGlobalScene(scene);
        }
    }

    private void LoadGlobalScene(string sceneName)
    {
        // Global Scene грузится на сервере И на каждом текущем/будущем клиенте.
        // ReplaceOption.All ВЫГРУЖАЕТ предыдущую сцену (главное меню), включая
        // сцены, загруженные обычным Unity — иначе меню осталось бы висеть
        // поверх лобби с дублирующим Canvas и EventSystem, и клики проваливались
        // бы на кнопки меню за спиной.
        SceneLoadData sld = new SceneLoadData(sceneName)
        {
            ReplaceScenes = ReplaceOption.All
        };
        _networkManager.SceneManager.LoadGlobalScenes(sld);
    }
}
