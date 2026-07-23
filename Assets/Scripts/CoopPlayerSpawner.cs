using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Спавнит игрока для каждого подключения, который становится «присутствующим»
/// в ЭТОЙ сцене. Работает и для одиночной игры (игровая сцена = стартовая),
/// и для перехода лобби→игра (сцена грузится позже, когда клиент уже подключён).
///
/// Это NetworkBehaviour — поэтому НЕ нужна ссылка на NetworkManager
/// (всё через base.SceneManager и base.Spawn). Решает твои вопросы №5 и №6:
///   - вешается на сетевой объект в игровой сцене 02_Game
///   - спавнер из примеров FishNet (PlayerSpawner) удали, оставь только этот
///
/// Объект, на котором висит скрипт, должен иметь компонент NetworkObject.
/// </summary>
public class CoopPlayerSpawner : NetworkBehaviour
{
    [Header("Спавн")]
    [SerializeField] private NetworkObject _playerPrefab;
    [SerializeField] private Transform[] _spawnPoints;

    private int _spawnIndex;

    public override void OnStartServer()
    {
        base.OnStartServer();
        // Событие срабатывает на сервере, когда подключение становится
        // присутствующим в какой-либо сцене (включая позже загруженные).
        base.SceneManager.OnClientPresenceChangeEnd += OnClientPresenceChangeEnd;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        if (base.SceneManager != null)
            base.SceneManager.OnClientPresenceChangeEnd -= OnClientPresenceChangeEnd;
    }

    private void OnClientPresenceChangeEnd(ClientPresenceChangeEventArgs args)
    {
        // Спавним только когда клиент ВОШЁЛ именно в нашу сцену.
        if (!args.Added) return;
        if (args.Scene != gameObject.scene) return;

        if (_playerPrefab == null)
        {
            Debug.LogError("[CoopPlayerSpawner] Не назначен Player prefab.");
            return;
        }

        SpawnFor(args.Connection);
    }

    private void SpawnFor(NetworkConnection conn)
    {
        Transform spawn = GetNextSpawnPoint();
        NetworkObject player = Instantiate(_playerPrefab, spawn.position, spawn.rotation);

        // base.Spawn с передачей conn делает клиента владельцем объекта (Owner).
        base.Spawn(player, conn);
    }

    private Transform GetNextSpawnPoint()
    {
        if (_spawnPoints == null || _spawnPoints.Length == 0) return transform;
        Transform t = _spawnPoints[_spawnIndex % _spawnPoints.Length];
        _spawnIndex++;
        return t;
    }
}
