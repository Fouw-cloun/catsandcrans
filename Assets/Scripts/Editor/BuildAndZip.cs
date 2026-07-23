#if UNITY_EDITOR
using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Окно: Build and Zip. Открывается через меню Tools → Build and Zip.
/// Одной кнопкой:
///   1. Собирает билд в папку Builds/vX.X.X/
///   2. Кладёт steam_appid.txt рядом с .exe
///   3. Архивирует всю папку в Builds/catsandcrans_vX.X.X.zip
/// </summary>
public class BuildAndZip : EditorWindow
{
    private string _version = "0.1.0";
    private string _steamAppId = "480";
    private string _exeName = "catsandcrans";
    private bool _autoZip = true;
    private bool _openFolder = true;
    private Vector2 _scroll;

    [MenuItem("Tools/Build and Zip")]
    public static void Open()
    {
        var w = GetWindow<BuildAndZip>("Build & Zip");
        w.minSize = new Vector2(380, 280);
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Сборка и архивация билда", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        _exeName    = EditorGUILayout.TextField("Имя .exe",          _exeName);
        _version    = EditorGUILayout.TextField("Версия (X.X.X)",    _version);
        _steamAppId = EditorGUILayout.TextField("steam_appid.txt",   _steamAppId);

        EditorGUILayout.Space(4);
        _autoZip    = EditorGUILayout.Toggle("Архивировать после сборки", _autoZip);
        _openFolder = EditorGUILayout.Toggle("Открыть папку после",       _openFolder);

        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox(
            $"Билд:   Builds/v{_version}/\n" +
            $"Архив:  Builds/{_exeName}_v{_version}.zip",
            MessageType.None);
        EditorGUILayout.Space(8);

        GUI.backgroundColor = new Color(0.4f, 0.85f, 0.5f);
        if (GUILayout.Button("▶  Build" + (_autoZip ? " + Zip" : ""), GUILayout.Height(36)))
        {
            GUI.backgroundColor = Color.white;
            string buildDir = BuildDir();
            Run(buildDir, ZipPath(buildDir));
        }
        GUI.backgroundColor = Color.white;

        string existingBuildDir = BuildDir();
        if (_autoZip && Directory.Exists(existingBuildDir))
        {
            EditorGUILayout.Space(4);
            if (GUILayout.Button("Только Zip (уже собранный билд)"))
                ZipFolder(existingBuildDir, ZipPath(existingBuildDir));
        }

        EditorGUILayout.EndScrollView();
    }

    private string BuildDir() => Path.GetFullPath(
        Path.Combine(Application.dataPath, "..", "Builds", $"v{_version}"));

    private string ZipPath(string buildDir) => Path.GetFullPath(
        Path.Combine(Path.GetDirectoryName(buildDir)!,
            $"{_exeName}_v{_version}.zip"));

    private void Run(string buildDir, string zipPath)
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        if (scenes.Length == 0)
        {
            EditorUtility.DisplayDialog("Ошибка", "В Build Settings нет сцен.", "OK");
            return;
        }

        string[] scenePaths = Array.ConvertAll(scenes, s => s.path);
        string exePath = Path.Combine(buildDir, _exeName + ".exe");

        if (Directory.Exists(buildDir)) Directory.Delete(buildDir, true);
        Directory.CreateDirectory(buildDir);

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes          = scenePaths,
            locationPathName = exePath,
            target          = BuildTarget.StandaloneWindows64,
            options         = BuildOptions.None
        });

        if (report.summary.result != BuildResult.Succeeded)
        {
            EditorUtility.DisplayDialog("Ошибка сборки",
                $"Build failed: {report.summary.result}", "OK");
            return;
        }

        // steam_appid.txt — обязательно рядом с .exe
        File.WriteAllText(Path.Combine(buildDir, "steam_appid.txt"), _steamAppId);

        // version.txt — метка-пломба, чтобы сверяться у кого какая версия
        File.WriteAllText(Path.Combine(buildDir, "version.txt"),
            $"{_exeName} v{_version} | built {DateTime.Now:yyyy-MM-dd HH:mm}");

        Debug.Log($"[BuildAndZip] Билд v{_version} собран → {buildDir}");

        if (_autoZip) ZipFolder(buildDir, zipPath);

        if (_openFolder)
            EditorUtility.RevealInFinder(Path.GetDirectoryName(zipPath) + "/");

        EditorUtility.DisplayDialog("Готово",
            $"v{_version} собран.\n" +
            (_autoZip ? $"Архив: {Path.GetFileName(zipPath)}" : "Архив не создавался."),
            "OK");
    }

    private void ZipFolder(string sourceDir, string zipPath)
    {
        if (!Directory.Exists(sourceDir))
        {
            Debug.LogError($"[BuildAndZip] Папка билда не найдена: {sourceDir}");
            return;
        }

        if (File.Exists(zipPath)) File.Delete(zipPath);

        // Внутри архива папка называется v{version}/ — так при распаковке
        // получается аккуратная отдельная папка, а не россыпь файлов.
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        string parentDir = Path.GetDirectoryName(sourceDir)!;
        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(parentDir, file).Replace('\\', '/');
            zip.CreateEntryFromFile(file, relative);
        }

        long mb = new FileInfo(zipPath).Length / 1024 / 1024;
        Debug.Log($"[BuildAndZip] Архив готов: {Path.GetFileName(zipPath)} ({mb} МБ)");
    }
}
#endif
