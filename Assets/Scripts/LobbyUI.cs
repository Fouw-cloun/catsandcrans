using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI лобби (TextMeshPro). Живёт в сцене 01b_Lobby на Canvas. Показывает список
/// игроков с отметкой готовности, кнопку «Готов/Не готов» и кнопку «Старт»
/// (только хосту, активна когда все готовы).
///
/// LobbyManager — сетевой объект, появляется не мгновенно, поэтому
/// привязываемся к нему в Update, как только Instance станет доступен.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    [Header("Ссылки на UI")]
    [Tooltip("Текстовое поле со списком игроков (TMP)")]
    [SerializeField] private TMP_Text _playerListText;
    [SerializeField] private Button _readyButton;
    [Tooltip("Подпись ВНУТРИ кнопки готовности (TMP, дочерний объект кнопки)")]
    [SerializeField] private TMP_Text _readyButtonLabel;
    [SerializeField] private Button _startButton;

    private bool _bound;

    private void Update()
    {
        if (_bound) return;
        if (LobbyManager.Instance == null) return;
        Bind();
    }

    private void Bind()
    {
        _bound = true;

        LobbyManager.Instance.OnLobbyChanged += Refresh;

        if (_readyButton != null)
            _readyButton.onClick.AddListener(() => LobbyManager.Instance.ToggleReady());
        if (_startButton != null)
            _startButton.onClick.AddListener(() => LobbyManager.Instance.StartGame());

        Refresh();
    }

    private void OnDisable()
    {
        if (LobbyManager.Instance != null)
            LobbyManager.Instance.OnLobbyChanged -= Refresh;
        _bound = false;
    }

    private void Refresh()
    {
        LobbyManager lm = LobbyManager.Instance;
        if (lm == null) return;

        // Список игроков.
        if (_playerListText != null)
        {
            StringBuilder sb = new StringBuilder();
            foreach (LobbyMember m in lm.Members)
                sb.AppendLine((m.Ready ? "<color=#4CAF50>[готов]</color>      "
                                       : "<color=#999999>[не готов]</color> ") + m.Name);
            _playerListText.text = sb.ToString();
        }

        // Подпись кнопки готовности.
        if (_readyButtonLabel != null)
            _readyButtonLabel.text = lm.LocalReady ? "Не готов" : "Готов";

        // Кнопка «Старт» — только хосту, активна когда все готовы.
        if (_startButton != null)
        {
            _startButton.gameObject.SetActive(lm.IsHost);
            _startButton.interactable = lm.AllReady();
        }
    }
}
