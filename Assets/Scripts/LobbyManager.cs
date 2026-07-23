using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using Steamworks;
using UnityEngine;

/// <summary>
/// Сетевое состояние лобби. Живёт на сетевом объекте (NetworkObject) в сцене
/// 01b_Lobby. Хранит список участников и их готовность через SyncList, который
/// FishNet автоматически синхронизирует всем клиентам.
///
/// Старт игры (только хост) грузит игровую сцену через LoadGlobalScenes с
/// ReplaceOption.OnlineOnly — лобби заменяется игрой У ВСЕХ одновременно.
/// Это решает баг «запустилась только у хоста».
/// </summary>
public struct LobbyMember
{
    public int ClientId;
    public string Name;
    public bool Ready;
}

public class LobbyManager : NetworkBehaviour
{
    public static LobbyManager Instance { get; private set; }

    /// <summary>UI подписывается сюда, чтобы перерисоваться при любом изменении.</summary>
    public event Action OnLobbyChanged;

    [Tooltip("Имя игровой сцены (как в Build Settings)")]
    [SerializeField] private string _gameScene = "02_Game";

    private readonly SyncList<LobbyMember> _members = new();
    private bool _localReady;

    public IEnumerable<LobbyMember> Members => _members;
    public bool LocalReady => _localReady;
    public bool IsHost => base.IsServerInitialized;

    public bool AllReady()
    {
        if (_members.Count == 0) return false;
        foreach (LobbyMember m in _members)
            if (!m.Ready) return false;
        return true;
    }

    // ───────────────────────── СЕРВЕР ─────────────────────────

    public override void OnStartServer()
    {
        base.OnStartServer();
        base.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        if (base.ServerManager != null)
            base.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
    }

    private void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        // Клиент отключился — убираем из списка.
        if (args.ConnectionState != RemoteConnectionState.Stopped) return;
        for (int i = _members.Count - 1; i >= 0; i--)
            if (_members[i].ClientId == conn.ClientId)
                _members.RemoveAt(i);
    }

    // ───────────────────────── КЛИЕНТ ─────────────────────────

    public override void OnStartClient()
    {
        base.OnStartClient();
        Instance = this;
        _members.OnChange += OnMembersChanged;

        // Сообщаем серверу своё имя из Steam.
        CmdJoin(GetLocalName());
        OnLobbyChanged?.Invoke();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        _members.OnChange -= OnMembersChanged;
        if (Instance == this) Instance = null;
    }

    private void OnMembersChanged(SyncListOperation op, int index,
        LobbyMember oldItem, LobbyMember newItem, bool asServer)
    {
        // Обновляем UI только на клиентской стороне (на хосте колбэк
        // приходит дважды — пропускаем серверный вызов).
        if (asServer) return;
        OnLobbyChanged?.Invoke();
    }

    // ───────────────────────── RPC ─────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void CmdJoin(string name, NetworkConnection conn = null)
    {
        if (conn == null) return;
        // Уже в списке? (например, повторный вызов)
        for (int i = 0; i < _members.Count; i++)
            if (_members[i].ClientId == conn.ClientId) return;

        _members.Add(new LobbyMember
        {
            ClientId = conn.ClientId,
            Name = string.IsNullOrEmpty(name) ? ("Player " + conn.ClientId) : name,
            Ready = false
        });
    }

    [ServerRpc(RequireOwnership = false)]
    private void CmdSetReady(bool ready, NetworkConnection conn = null)
    {
        if (conn == null) return;
        for (int i = 0; i < _members.Count; i++)
        {
            if (_members[i].ClientId == conn.ClientId)
            {
                LobbyMember m = _members[i];
                m.Ready = ready;
                _members[i] = m; // переприсваивание триггерит синхронизацию
                return;
            }
        }
    }

    // ───────────────────────── ВЫЗОВЫ ИЗ UI ─────────────────────────

    /// <summary>Переключить свою готовность (вызывает любой клиент).</summary>
    public void ToggleReady()
    {
        _localReady = !_localReady;
        CmdSetReady(_localReady);
        OnLobbyChanged?.Invoke(); // мгновенно обновим подпись кнопки локально
    }

    /// <summary>Запустить игру. Сработает только у хоста и только если все готовы.</summary>
    public void StartGame()
    {
        if (!base.IsServerInitialized)
        {
            Debug.LogWarning("[LobbyManager] Запускать игру может только хост.");
            return;
        }
        if (!AllReady())
        {
            Debug.Log("[LobbyManager] Не все игроки готовы.");
            return;
        }

        // Заменяем сцену лобби на игровую У ВСЕХ клиентов сразу.
        // ReplaceOption.All (а не OnlineOnly) — так же, как в рабочем прямом
        // пути (StartSinglePlayer). При OnlineOnly клиенты не находили SceneId
        // объектов игровой сцены при переходе лобби→игра.
        SceneLoadData sld = new SceneLoadData(_gameScene)
        {
            ReplaceScenes = ReplaceOption.All
        };
        base.SceneManager.LoadGlobalScenes(sld);
    }

    private string GetLocalName()
    {
        if (SteamManager.Initialized)
            return SteamFriends.GetPersonaName();
        return "Player";
    }
}
