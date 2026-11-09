#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// ГЕНЕРАТОР UI-ИЕРАРХИИ В СЦЕНУ. Меню «CatCooks → UI».
///
/// Собирает прицел, часы и меню как обычные объекты сцены, чтобы их можно было
/// посмотреть, померить и подвигать руками. Потом правки читаются обратно в
/// настройки, а сама ветка удаляется — при запуске игры она исчезает сама.
///
/// ─────────────────────────────────────────────────────────────────────────
/// ПОЧЕМУ ЧЕРЕЗ РЕФЛЕКСИЮ, А НЕ ВТОРОЙ СБОРЩИК
///
/// Соблазн был написать здесь свою сборку иерархии. Так делать нельзя: копия
/// разъедется с рабочим кодом на первой же правке, и в редакторе ты будешь
/// двигать одно, а в игре увидишь другое. Это хуже, чем не иметь инструмента
/// вовсе, потому что ошибка не выглядит как ошибка.
///
/// Поэтому инструмент дёргает ТЕ ЖЕ приватные Build() из CatPawCrosshair,
/// GameClockUI и PauseMenuUI. Иерархия в сцене гарантированно совпадает с
/// игровой, потому что это буквально один и тот же код.
///
/// Плата за это — рефлексия и зависимость от имён Build и _canvas. Если
/// переименуешь их, инструмент скажет об этом внятной ошибкой, а не сломается
/// молча. Обычный публичный метод был бы честнее, но тогда в рантайм-классы
/// пришлось бы добавлять API, нужный только редактору.
/// ─────────────────────────────────────────────────────────────────────────
///
/// Файл обязан лежать в папке с именем Editor:
///   Assets/Scripts/UI/Editor/UiSceneBuilder.cs
/// Иначе проект не соберётся — UnityEditor в сборку не попадает.
/// </summary>
public static class UiSceneBuilder
{
    private const string RootName = "~UI_PREVIEW";

    private const string MenuBuild = "CatCooks/UI/Собрать UI в сцене %#u";
    private const string MenuClear = "CatCooks/UI/Удалить превью";
    private const string MenuRead = "CatCooks/UI/Считать правки обратно";
    private const string MenuMain = "CatCooks/UI/Показать: главный поднос";
    private const string MenuSettings = "CatCooks/UI/Показать: настройки";
    private const string MenuInterface = "CatCooks/UI/Показать: размеры";

    // ───────────────────────── СБОРКА ─────────────────────────

    [MenuItem(MenuBuild, false, 0)]
    public static void BuildAll()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Нельзя в Play Mode",
                "Останови игру: в Play Mode интерфейс строят сами скрипты, " +
                "и превью с ними столкнётся.", "Понятно");
            return;
        }

        ClearAll();

        // EventSystem создаём заранее: PauseMenuUI.Build() ищет его сам и, если
        // не находит, зовёт DontDestroyOnLoad — в редакторе это даёт warning.
        EnsureEventSystem();

        UiLayout.Load();
        GameSettings.Load();

        GameObject root = new GameObject(RootName);
        root.AddComponent<UiPreviewRoot>();
        Undo.RegisterCreatedObjectUndo(root, "Собрать UI");

        int built = 0;
        built += Attach(root, BuildComponent<CatPawCrosshair>()) ? 1 : 0;
        built += Attach(root, BuildComponent<GameClockUI>()) ? 1 : 0;

        Canvas menu = BuildComponent<PauseMenuUI>();
        if (Attach(root, menu))
        {
            built++;
            ShowPage(menu, "MainPage");
        }

        if (built == 0)
        {
            Debug.LogError("[UiSceneBuilder] Не собралось ничего. Самая частая " +
                           "причина — спрайты не лежат в Assets/Resources/UI/. " +
                           "Смотри ошибки выше.");
            Object.DestroyImmediate(root);
            return;
        }

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(root.scene);

        Debug.Log($"[UiSceneBuilder] Собрано канвасов: {built}. Двигай что нужно, " +
                  "потом CatCooks → UI → Считать правки обратно.", root);
    }

    /// <summary>Создаёт временный объект, вешает компонент и зовёт его Build().
    /// Возвращает построенный канвас.</summary>
    private static Canvas BuildComponent<T>() where T : Component
    {
        var temp = new GameObject("~temp_" + typeof(T).Name);
        temp.hideFlags = HideFlags.HideAndDontSave;

        Canvas canvas = null;

        try
        {
            // Прицел и часы — NetworkBehaviour, им нужен NetworkObject рядом,
            // иначе FishNet начнёт ругаться при добавлении компонента.
            if (typeof(FishNet.Object.NetworkBehaviour).IsAssignableFrom(typeof(T)))
                temp.AddComponent<FishNet.Object.NetworkObject>();

            T comp = temp.AddComponent<T>();

            MethodInfo build = typeof(T).GetMethod("Build",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (build == null)
            {
                Debug.LogError($"[UiSceneBuilder] В {typeof(T).Name} не найден метод " +
                               "Build(). Его переименовали? Инструмент зовёт именно его, " +
                               "чтобы иерархия в сцене совпадала с игровой.");
                return null;
            }

            build.Invoke(comp, null);

            FieldInfo canvasField = typeof(T).GetField("_canvas",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (canvasField == null)
            {
                Debug.LogError($"[UiSceneBuilder] В {typeof(T).Name} не найдено поле _canvas.");
                return null;
            }

            canvas = canvasField.GetValue(comp) as Canvas;

            if (canvas == null)
            {
                Debug.LogWarning($"[UiSceneBuilder] {typeof(T).Name} не создал канвас. " +
                                 "Скорее всего не загрузились спрайты из Resources/UI.");
            }
        }
        finally
        {
            // Временный носитель больше не нужен: канвас он создал отдельным
            // объектом в корне сцены, и тот останется жить.
            Object.DestroyImmediate(temp);
        }

        return canvas;
    }

    private static bool Attach(GameObject root, Canvas canvas)
    {
        if (canvas == null) return false;

        canvas.transform.SetParent(root.transform, false);

        // Канвас в Play Mode прячется через enabled — в превью он должен быть виден.
        canvas.enabled = true;
        canvas.gameObject.SetActive(true);
        return true;
    }

    // ───────────────────────── ОЧИСТКА ─────────────────────────

    [MenuItem(MenuClear, false, 1)]
    public static void ClearAll()
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null) return;

        Undo.DestroyObjectImmediate(root);
    }

    // ───────────────────────── ЧТЕНИЕ ПРАВОК ─────────────────────────

    /// <summary>Считывает размеры, которые ты подвигал в сцене, и кладёт их в
    /// UiLayout (то есть в PlayerPrefs). После этого превью можно удалять —
    /// игра возьмёт новые значения.</summary>
    [MenuItem(MenuRead, false, 2)]
    public static void ReadBack()
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            Debug.LogWarning("[UiSceneBuilder] Превью в сцене нет — считывать нечего.");
            return;
        }

        UiLayout.Load();
        int changed = 0;

        // ── Прицел ──
        var paw = FindRect(root, "HUD_PawCrosshair/Paw");
        if (paw != null)
        {
            UiLayout.SetCrosshairSize(paw.sizeDelta.x);
            changed++;

            var idle = paw.Find("PoseIdle");
            var group = idle != null ? idle.GetComponent<CanvasGroup>() : null;
            if (group != null)
            {
                UiLayout.SetCrosshairAlpha(group.alpha);
                changed++;
            }
        }

        // ── Часы ──
        var clock = FindRect(root, "HUD_Clock/Clock");
        if (clock != null)
        {
            UiLayout.SetClockWidth(clock.sizeDelta.x);
            UiLayout.SetClockHeight(clock.sizeDelta.y);
            UiLayout.SetClockOffsetY(clock.anchoredPosition.y);
            changed += 3;

            // Внутреннюю разметку циферблата подсказываем в консоль: она живёт
            // константами в GameClockUI, автоматически туда не запишешь.
            var face = FindRect(root, "HUD_Clock/Clock/Face");
            if (face != null && clock.sizeDelta.y > 1f)
            {
                float rel = face.anchoredPosition.y / clock.sizeDelta.y;
                if (Mathf.Abs(rel - 0.0556f) > 0.002f)
                {
                    Debug.Log($"[UiSceneBuilder] Циферблат сдвинут. Если это надолго, " +
                              $"поставь в GameClockUI: FaceOffsetY = {rel:0.0000}f");
                }
            }
        }

        UiLayout.Save();

        Debug.Log($"[UiSceneBuilder] Считано значений: {changed}. " +
                  "Сохранено в PlayerPrefs, игра подхватит при следующем запуске.");
    }

    // ───────────────────────── ПЕРЕКЛЮЧЕНИЕ СТРАНИЦ ─────────────────────────

    [MenuItem(MenuMain, false, 20)]
    private static void ShowMain() => SwitchPage("MainPage");

    [MenuItem(MenuSettings, false, 21)]
    private static void ShowSettings() => SwitchPage("SettingsPage");

    [MenuItem(MenuInterface, false, 22)]
    private static void ShowInterface() => SwitchPage("InterfacePage");

    private static void SwitchPage(string page)
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null) return;

        Transform menu = root.transform.Find("UI_PauseMenu");
        if (menu == null) return;

        ShowPage(menu.GetComponent<Canvas>(), page);
        EditorSceneManager.MarkSceneDirty(root.scene);
    }

    /// <summary>Все три страницы меню строятся сразу и лежат друг на друге.
    /// Чтобы править конкретную, остальные надо выключить.</summary>
    private static void ShowPage(Canvas menuCanvas, string page)
    {
        if (menuCanvas == null) return;

        Transform rootPanel = menuCanvas.transform.Find("Root");
        if (rootPanel == null) return;

        rootPanel.gameObject.SetActive(true);

        foreach (string name in new[] { "MainPage", "SettingsPage", "InterfacePage" })
        {
            Transform p = rootPanel.Find(name);
            if (p != null) p.gameObject.SetActive(name == page);
        }
    }

    // ───────────────────────── МЕЛОЧИ ─────────────────────────

    private static RectTransform FindRect(GameObject root, string path)
    {
        Transform t = root.transform.Find(path);
        if (t == null)
        {
            Debug.LogWarning($"[UiSceneBuilder] Не найден путь {path}. " +
                             "Иерархию переименовали или собрали не всё.");
            return null;
        }
        return t as RectTransform;
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;

        var go = new GameObject("EventSystem",
            typeof(EventSystem), typeof(StandaloneInputModule));
        Undo.RegisterCreatedObjectUndo(go, "EventSystem");
    }
}
#endif
