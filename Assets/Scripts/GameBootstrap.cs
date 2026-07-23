using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Живёт в сцене 00_Bootstrap. Запускается самым первым при старте игры.
/// Менеджеры (NetworkManager, SteamManager, SteamLobby) уже лежат в этой
/// сцене и помечены DontDestroyOnLoad, поэтому они переживут загрузку меню.
/// Задача этого скрипта — просто передать управление в главное меню.
///
/// В Build Settings сцена 00_Bootstrap ДОЛЖНА стоять под индексом 0.
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    [Tooltip("Имя сцены главного меню (как в Build Settings)")]
    [SerializeField] private string _mainMenuScene = "01_MainMenu";

    private void Start()
    {
        // Если включён dev-пропуск меню (в редакторе) — SteamLobby сам поднимет
        // хост в нужном уровне, меню грузить не надо.
        if (SteamLobby.Instance != null && SteamLobby.Instance.WillDevSkip)
            return;

        // LoadSceneMode.Single выгрузит bootstrap-сцену, но менеджеры с
        // DontDestroyOnLoad останутся живы в отдельной DDOL-сцене.
        SceneManager.LoadScene(_mainMenuScene, LoadSceneMode.Single);
    }
}
