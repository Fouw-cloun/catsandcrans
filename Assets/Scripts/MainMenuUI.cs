using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Живёт в сцене 01_MainMenu на объекте Canvas (или отдельном UI-менеджере).
/// Просто пробрасывает нажатия кнопок в SteamLobby.
///
/// Присоединение к игре в этом базовом варианте идёт через инвайт Steam:
/// хост создаёт friends-only лобби и зовёт друга через оверлей (Shift+Tab →
/// Friends → правый клик → Invite to Game). Друг принимает — срабатывает
/// OnJoinRequested в SteamLobby. Отдельная кнопка "Join" не нужна для теста.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Header("Кнопки")]
    [SerializeField] private Button _singlePlayerButton;
    [SerializeField] private Button _hostButton;
    [SerializeField] private Button _quitButton;

    private void Awake()
    {
        if (_singlePlayerButton != null) _singlePlayerButton.onClick.AddListener(OnSinglePlayer);
        if (_hostButton != null)         _hostButton.onClick.AddListener(OnHost);
        if (_quitButton != null)         _quitButton.onClick.AddListener(OnQuit);
    }

    private void OnSinglePlayer()
    {
        if (SteamLobby.Instance != null) SteamLobby.Instance.StartSinglePlayer();
    }

    private void OnHost()
    {
        if (SteamLobby.Instance != null) SteamLobby.Instance.HostLobby();
    }

    private void OnQuit()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
