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
///
/// ─────────────────────────────────────────────────────────────────────────
/// ИСПРАВЛЕНО:
///
/// 1. ГЛАВНОЕ. Подписка на OnServerConnectionState стояла ПОСЛЕ проверки
///    SteamManager.Initialized. Без запущенного Steam метод выходил по return,
///    подписки не происходило — и «Одиночная игра» поднимала сервер, но сцену
///    не грузила. Игра вставала чёрным экраном без единой ошибки в консоли,
///    хотя одиночному режиму Steam вообще не нужен. Теперь всё, что касается
///    FishNet, настраивается до проверки Steam, а без Steam отваливается
///    только сетевая игра с друзьями.
///
/// 2. Disconnect() освобождает курсор через UiModal.Reset(). Иначе при выходе
///    из игры с открытым меню счётчик модальных окон оставался ненулевым, и в
///    главном меню курсор оставался в непонятном состоянии.
///
/// 3. Проверки на null у _networkManager: раньше пустое поле в инспекторе
///    роняло Start() с NullReferenceException без внятного объяснения.
///
/// 4. Disconnect() безопасен при повторном вызове. Он зовётся и из меню паузы,
///    и из главного меню, и из OnApplicationQuit — двойной StopConnection
///    сыпал предупреждениями FishNet.
/// ─────────────────────────────────────────────────────────────────────────
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

    /// <summary>Доступна ли сетевая игра. False, если Steam не поднялся —
    /// кнопки «Создать лобби» и «Пригласить» в меню стоит по этому флагу
    /// гасить, чтобы игрок не жал в пустоту.</summary>
    public bool SteamReady { get; private set; }

    private const string HostAddressKey = "HostAddress";
    public CSteamID CurrentLobbyID { get; private set; }

    private Callback<LobbyCreated_t> _lobbyCreated;
    private Callback<GameLobbyJoinRequested_t> _joinRequested;
    private Callback<LobbyEnter_t> _lobbyEntered;

    // Какую сцену грузить, как только поднимется сервер (пусто = ничего).
    private string _sceneOnServerStart;

    private bool _subscribed;

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
        if (_networkManager == null)
        {
            Debug.LogError("[SteamLobby] Не назначен NetworkManager. " +
                           "Перетащи его из bootstrap-сцены в поле Network Manager.");
            return;
        }

        // ── СНАЧАЛА FishNet ──
        // Подписка идёт ДО проверки Steam. Одиночная игра работает без Steam,
        // и если бы мы вышли раньше, сервер стартовал бы, а сцена не грузилась.
        _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
        _subscribed = true;

        // Dev-режим: в редакторе сразу поднимаем хост в указанном уровне.
        // Steam для этого тоже не нужен.
        if (WillDevSkip)
        {
            Debug.Log($"[SteamLobby] DEV: пропуск меню, старт хоста в '{_devScene}'.");
            _sceneOnServerStart = _devScene;
            _networkManager.ServerManager.StartConnection();
            _networkManager.ClientManager.StartConnection();
        }

        // ── ЗАТЕМ Steam ──
        if (!SteamManager.Initialized)
        {
            SteamReady = false;
            Debug.LogWarning("[SteamLobby] Steam НЕ инициализирован — сетевая игра " +
                             "недоступна, одиночная работает. Запущен ли Steam? " +
                             "Есть ли steam_appid.txt в корне проекта?");
            return;
        }

        SteamReady = true;

        _lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
        _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
        _lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
    }

    private void OnDestroy()
    {
        if (_subscribed && _networkManager != null)
        {
            _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            _subscribed = false;
        }
    }

    // ───────────────────────── ПУБЛИЧНЫЕ КНОПКИ ─────────────────────────

    /// <summary>Одиночная игра: локальный server+client, сразу в игровую сцену.
    /// Steam не требуется.</summary>
    public void StartSinglePlayer()
    {
        if (_networkManager == null) return;

        _sceneOnServerStart = _gameScene; // минуя лобби
        _networkManager.ServerManager.StartConnection();
        _networkManager.ClientManager.StartConnection();
    }

    /// <summary>Создать лобби (friends-only) и стать хостом -> сцена лобби.</summary>
    public void HostLobby()
    {
        if (!SteamReady)
        {
            Debug.LogError("[SteamLobby] Нельзя создать лобби: Steam не инициализирован.");
            return;
        }

        _sceneOnServerStart = _lobbyScene;
        SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, _maxPlayers);
    }

    /// <summary>Отключиться: останавливаем сеть и выходим из лобби.
    /// Безопасно звать повторно — зовётся и из меню паузы, и из главного меню.</summary>
    public void Disconnect()
    {
        // Возвращаем управление курсором в исходное состояние: если выход
        // случился из открытого меню, счётчик модалок остался бы ненулевым.
        UiModal.Reset();

        if (_networkManager != null)
        {
            if (_networkManager.IsServerStarted)
                _networkManager.ServerManager.StopConnection(true);
            if (_networkManager.IsClientStarted)
                _networkManager.ClientManager.StopConnection();
        }

        if (SteamReady && CurrentLobbyID.IsValid())
        {
            SteamMatchmaking.LeaveLobby(CurrentLobbyID);
            CurrentLobbyID = default;   // иначе второй вызов уйдёт в мёртвое лобби
        }

        _sceneOnServerStart = null;
    }

    // ───────────────────────── STEAM CALLBACKS ─────────────────────────

    private void OnLobbyCreated(LobbyCreated_t cb)
    {
        if (cb.m_eResult != EResult.k_EResultOK)
        {
            Debug.LogError("[SteamLobby] Не удалось создать лобби: " + cb.m_eResult);
            _sceneOnServerStart = null;   // иначе сцена загрузится «сама» позже
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
