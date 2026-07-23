#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Заставляет редактор ВСЕГДА входить в Play Mode со сцены 00_Bootstrap,
/// какая бы сцена ни была открыта. Благодаря этому можно держать открытым
/// рабочий уровень, нажать Play — и игра корректно поднимет менеджеры,
/// инициализирует Steam и (если включён dev-флаг в SteamLobby) сразу запустит
/// локальный хост в нужном уровне. Игрок заспавнится штатно, камера и
/// управление заработают.
///
/// Лежит в папке Assets/Scripts/Editor/ (обязательно в папке с именем Editor).
/// Если путь к bootstrap-сцене другой — поправь BootstrapScenePath.
/// </summary>
[InitializeOnLoad]
public static class PlayFromBootstrap
{
    // ПОПРАВЬ путь под свой проект, если сцена лежит в другом месте.
    private const string BootstrapScenePath = "Assets/Scenes/00_Bootstrap.unity";

    static PlayFromBootstrap()
    {
        SceneAsset bootstrap = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapScenePath);
        if (bootstrap != null)
            EditorSceneManager.playModeStartScene = bootstrap;
        else
            Debug.LogWarning($"[PlayFromBootstrap] Не найдена сцена по пути {BootstrapScenePath}. " +
                             "Поправь BootstrapScenePath в скрипте.");
    }
}
#endif
