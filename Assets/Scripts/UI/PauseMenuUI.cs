using System;
using System.Collections.Generic;
using FishNet;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// МЕНЮ ПАУЗЫ — СЕКЦИОННЫЙ ПОДНОС. Металлический поднос с лунками, в каждой
/// лежит своё «блюдо»: кнопка своего цвета с иконкой еды. Настройки — такой же
/// поднос, но лунки шире и внутри них ползунки и переключатели.
///
/// ПРО КООПЕРАТИВ: Time.timeScale не трогается. Пауза одного игрока не может
/// останавливать мир — это сломает тики FishNet и заморозит игру напарнику.
/// Меню только забирает ввод у своего клиента через UiModal: кастрюля кипит,
/// время идёт, напарник работает. Отсюда «Перекур», а не «Пауза».
///
/// КУДА ВЕШАТЬ: любой объект игровой сцены (удобно — тот же, где GameClock).
/// Один на сцену, НЕ на игрока.
/// </summary>
[DisallowMultipleComponent]
public class PauseMenuUI : MonoBehaviour
{
    public static PauseMenuUI Instance { get; private set; }

    [Header("Сцены")]
    [Tooltip("Имя сцены главного меню — должно совпадать с GameBootstrap")]
    [SerializeField] private string _mainMenuScene = "01_MainMenu";

    [Header("Клавиша")]
    [SerializeField] private KeyCode _toggleKey = KeyCode.Escape;

    [Header("Затемнение")]
    [SerializeField] private Color _dimColor = new Color32(0x0B, 0x0C, 0x21, 0xC0);

    [Header("Цвета блюд")]
    [SerializeField] private Color _dishResume = new Color32(0xFF, 0xD8, 0x4D, 0xFF);
    [SerializeField] private Color _dishSettings = new Color32(0x8B, 0xD4, 0x4F, 0xFF);
    [SerializeField] private Color _dishLeave = new Color32(0xFF, 0xAF, 0x34, 0xFF);
    [SerializeField] private Color _dishQuit = new Color32(0xFE, 0x3B, 0x5B, 0xFF);
    [SerializeField] private Color _dishNeutral = new Color32(0xC0, 0x8B, 0x63, 0xFF);

    [Header("Текст")]
    [SerializeField] private Color _titleColor = new Color32(0xFF, 0xD8, 0x4D, 0xFF);
    [SerializeField] private Color _labelDark = new Color32(0x2A, 0x1C, 0x08, 0xFF);
    [SerializeField] private Color _labelLight = new Color32(0xFE, 0xF3, 0xD5, 0xFF);
    [SerializeField] private Color _valueColor = new Color32(0xFF, 0xD8, 0x4D, 0xFF);
    [SerializeField] private Color _sliderFillColor = new Color32(0x8B, 0xD4, 0x4F, 0xFF);

    // ───────── внутреннее ─────────

    private Canvas _canvas;
    private GameObject _rootPanel;
    private GameObject _mainPage;
    private GameObject _settingsPage;
    private GameObject _interfacePage;

    private bool _open;

    private enum Page { Main, Settings, Interface }
    private Page _page = Page.Main;

    private readonly List<Action> _refreshers = new List<Action>();

    public bool IsOpen => _open;

    private void Awake()
    {
        Instance = this;
        GameSettings.Load();
        UiLayout.Load();
    }

    private void Start()
    {
        Build();
        Close();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        // Смена сцены при открытом меню иначе оставит игрока без управления
        // с залипшим счётчиком модалок.
        if (_open) UiModal.Pop();
    }

    private void Update()
    {
        if (!Input.GetKeyDown(_toggleKey)) return;

        // ESC возвращает на уровень выше, а не захлопывает всё сразу:
        // размеры → настройки → главный поднос → закрыть.
        if (!_open) { Open(); return; }

        switch (_page)
        {
            case Page.Interface: ShowSettings(); break;
            case Page.Settings: ShowMain(); break;
            default: Close(); break;
        }
    }

    // ───────── открытие/закрытие ─────────

    public void Open()
    {
        if (_open) return;
        _open = true;
        _canvas.enabled = true;
        _rootPanel.SetActive(true);
        ShowMain();
        UiModal.Push();
    }

    public void Close()
    {
        bool was = _open;
        _open = false;
        _page = Page.Main;
        if (_canvas != null) _canvas.enabled = false;
        if (_rootPanel != null) _rootPanel.SetActive(false);
        if (was) UiModal.Pop();
        GameSettings.Save();
        UiLayout.Save();
    }

    private void ShowMain() => GoTo(Page.Main);

    private void ShowSettings() => GoTo(Page.Settings);

    private void ShowInterface() => GoTo(Page.Interface);

    private void GoTo(Page p)
    {
        _page = p;
        _mainPage.SetActive(p == Page.Main);
        _settingsPage.SetActive(p == Page.Settings);
        _interfacePage.SetActive(p == Page.Interface);

        // Подтягиваем значения в ползунки: их могли изменить не через меню
        // (сброс к заводским, загрузка профиля).
        foreach (Action r in _refreshers) r();
    }

    // ───────── сборка ─────────

    private void Build()
    {
        // 200 — выше HUD (прицел 100, часы 90), меню всегда сверху.
        _canvas = UiKit.MakeCanvas("UI_PauseMenu", 200, true);
        EnsureEventSystem();

        _rootPanel = new GameObject("Root", typeof(RectTransform));
        var rootRt = _rootPanel.GetComponent<RectTransform>();
        rootRt.SetParent(_canvas.transform, false);
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;

        // Затемнение с raycastTarget = true глушит клики по миру.
        Image dim = UiKit.MakeImage(rootRt, "Dim", null, Vector2.zero, Vector2.zero, _dimColor);
        RectTransform dimRt = dim.rectTransform;
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;
        dim.raycastTarget = true;

        BuildMainPage(rootRt);
        BuildSettingsPage(rootRt);
        BuildInterfacePage(rootRt);
    }

    private void BuildMainPage(Transform parent)
    {
        const float w = 470f, h = 610f;

        _mainPage = new GameObject("MainPage", typeof(RectTransform));
        var page = _mainPage.GetComponent<RectTransform>();
        page.SetParent(parent, false);
        page.anchorMin = page.anchorMax = new Vector2(0.5f, 0.5f);
        page.sizeDelta = new Vector2(w, h);
        page.anchoredPosition = Vector2.zero;

        UiKit.MakeImage(page, "Tray", UiKit.TrayFrame,
            new Vector2(w, h), Vector2.zero, Color.white);

        // Верхняя лунка — «шильдик» подноса вместо блюда.
        UiKit.MakeImage(page, "TitleWell", UiKit.TrayWell,
            new Vector2(w - 96f, 92f), new Vector2(0f, h * 0.5f - 92f), Color.white);
        UiKit.MakeText(page, "Title", "ПЕРЕКУР", 34,
            new Vector2(w - 110f, 40f), new Vector2(0f, h * 0.5f - 80f), _titleColor);
        UiKit.MakeText(page, "Sub", "смена продолжается", 15,
            new Vector2(w - 110f, 24f), new Vector2(0f, h * 0.5f - 112f),
            UiKit.WithAlpha(_labelLight, 0.65f));

        float y = h * 0.5f - 210f;
        const float step = 112f;

        Dish(page, "Вернуться на смену", UiKit.FoodEgg, _dishResume, _labelDark,
            new Vector2(0f, y), w, Close);
        y -= step;

        Dish(page, "Настройки", UiKit.FoodSalad, _dishSettings, _labelDark,
            new Vector2(0f, y), w, ShowSettings);
        y -= step;

        Dish(page, "Выйти в меню", UiKit.FoodSoup, _dishLeave, _labelDark,
            new Vector2(0f, y), w, LeaveToMenu);
        y -= step;

        Dish(page, "Выход из игры", UiKit.FoodMeat, _dishQuit, _labelLight,
            new Vector2(0f, y), w, QuitGame);
    }

    private void BuildSettingsPage(Transform parent)
    {
        const float w = 660f, h = 820f;

        _settingsPage = new GameObject("SettingsPage", typeof(RectTransform));
        var page = _settingsPage.GetComponent<RectTransform>();
        page.SetParent(parent, false);
        page.anchorMin = page.anchorMax = new Vector2(0.5f, 0.5f);
        page.sizeDelta = new Vector2(w, h);
        page.anchoredPosition = Vector2.zero;

        UiKit.MakeImage(page, "Tray", UiKit.TrayFrame,
            new Vector2(w, h), Vector2.zero, Color.white);

        UiKit.MakeImage(page, "TitleWell", UiKit.TrayWell,
            new Vector2(w - 96f, 76f), new Vector2(0f, h * 0.5f - 84f), Color.white);
        UiKit.MakeText(page, "Title", "НАСТРОЙКИ", 30,
            new Vector2(w - 110f, 38f), new Vector2(0f, h * 0.5f - 84f), _titleColor);

        float y = h * 0.5f - 168f;
        const float step = 70f;

        SliderRow(page, "Чувствительность мыши", UiKit.FoodCarrot,
            new Vector2(0f, y), w, 0.2f, 8f,
            () => GameSettings.MouseSensitivity, GameSettings.SetSensitivity, "0.0");
        y -= step;

        SliderRow(page, "Общая громкость", UiKit.FoodSoup,
            new Vector2(0f, y), w, 0f, 1f,
            () => GameSettings.MasterVolume, GameSettings.SetMaster, "P0");
        y -= step;

        SliderRow(page, "Музыка", UiKit.FoodBread,
            new Vector2(0f, y), w, 0f, 1f,
            () => GameSettings.MusicVolume, GameSettings.SetMusic, "P0");
        y -= step;

        SliderRow(page, "Звуки", UiKit.FoodEgg,
            new Vector2(0f, y), w, 0f, 1f,
            () => GameSettings.SfxVolume, GameSettings.SetSfx, "P0");
        y -= step;

        OptionRow(page, "Качество графики", UiKit.FoodMeat, new Vector2(0f, y), w,
            () => QualitySettings.names[GameSettings.Quality],
            d => GameSettings.SetQuality(GameSettings.Quality + d));
        y -= step;

        OptionRow(page, "Полный экран", UiKit.FoodSalad, new Vector2(0f, y), w,
            () => GameSettings.Fullscreen ? "вкл" : "выкл",
            d => GameSettings.SetFullscreen(!GameSettings.Fullscreen));
        y -= step;

        OptionRow(page, "Вертикальная синхронизация", UiKit.FoodCarrot,
            new Vector2(0f, y), w,
            () => GameSettings.VSync ? "вкл" : "выкл",
            d => GameSettings.SetVSync(!GameSettings.VSync));

        Dish(page, "Размеры интерфейса", UiKit.FoodBread, _dishSettings, _labelDark,
            new Vector2(0f, -h * 0.5f + 154f), w, ShowInterface);

        Dish(page, "Назад к подносу", UiKit.FoodEgg, _dishResume, _labelDark,
            new Vector2(0f, -h * 0.5f + 62f), w * 0.72f, ShowMain);
    }

    /// <summary>Страница размеров. Все ползунки применяются вживую: UiLayout
    /// поднимает событие, на которое подписаны прицел и часы, и те меняют свою
    /// вёрстку прямо под курсором. Меню при этом остаётся открытым, поэтому
    /// результат виден сразу — подгонять размер вслепую не приходится.</summary>
    private void BuildInterfacePage(Transform parent)
    {
        const float w = 660f, h = 680f;

        _interfacePage = new GameObject("InterfacePage", typeof(RectTransform));
        var page = _interfacePage.GetComponent<RectTransform>();
        page.SetParent(parent, false);
        page.anchorMin = page.anchorMax = new Vector2(0.5f, 0.5f);
        page.sizeDelta = new Vector2(w, h);
        page.anchoredPosition = Vector2.zero;

        UiKit.MakeImage(page, "Tray", UiKit.TrayFrame,
            new Vector2(w, h), Vector2.zero, Color.white);

        UiKit.MakeImage(page, "TitleWell", UiKit.TrayWell,
            new Vector2(w - 96f, 76f), new Vector2(0f, h * 0.5f - 84f), Color.white);
        UiKit.MakeText(page, "Title", "РАЗМЕРЫ", 30,
            new Vector2(w - 110f, 38f), new Vector2(0f, h * 0.5f - 84f), _titleColor);

        float y = h * 0.5f - 172f;
        const float step = 70f;

        SliderRow(page, "Размер прицела", UiKit.FoodCarrot, new Vector2(0f, y), w,
            UiLayout.MinCrosshairSize, UiLayout.MaxCrosshairSize,
            () => UiLayout.CrosshairSize, UiLayout.SetCrosshairSize, "0");
        y -= step;

        SliderRow(page, "Прозрачность прицела", UiKit.FoodEgg, new Vector2(0f, y), w,
            0.15f, 1f,
            () => UiLayout.CrosshairAlpha, UiLayout.SetCrosshairAlpha, "P0");
        y -= step;

        SliderRow(page, "Ширина часов", UiKit.FoodBread, new Vector2(0f, y), w,
            UiLayout.MinClockWidth, UiLayout.MaxClockWidth,
            () => UiLayout.ClockWidth, UiLayout.SetClockWidth, "0");
        y -= step;

        SliderRow(page, "Высота часов", UiKit.FoodSoup, new Vector2(0f, y), w,
            UiLayout.MinClockHeight, UiLayout.MaxClockHeight,
            () => UiLayout.ClockHeight, UiLayout.SetClockHeight, "0");
        y -= step;

        SliderRow(page, "Сдвиг часов по вертикали", UiKit.FoodSalad,
            new Vector2(0f, y), w,
            UiLayout.MinClockOffsetY, UiLayout.MaxClockOffsetY,
            () => UiLayout.ClockOffsetY, UiLayout.SetClockOffsetY, "0");

        Dish(page, "Сбросить к заводским", UiKit.FoodMeat, _dishLeave, _labelDark,
            new Vector2(0f, -h * 0.5f + 154f), w, ResetLayout);

        Dish(page, "Назад к настройкам", UiKit.FoodEgg, _dishResume, _labelDark,
            new Vector2(0f, -h * 0.5f + 62f), w * 0.72f, ShowSettings);
    }

    /// <summary>Сброс размеров. После него ползунки надо перечитать вручную —
    /// значения поменялись мимо них.</summary>
    private void ResetLayout()
    {
        UiLayout.ResetAll();
        foreach (Action r in _refreshers) r();
    }

    // ───────── кирпичики ─────────

    /// <summary>Лунка + блюдо внутри неё + иконка еды. Это и есть кнопка.</summary>
    private Button Dish(Transform parent, string label, Sprite icon,
        Color color, Color textColor, Vector2 pos, float trayWidth, Action onClick)
    {
        float wellW = trayWidth - 96f;
        RectTransform well = UiKit.MakeRect(parent, "Well_" + label,
            new Vector2(wellW, 88f), pos);
        UiKit.MakeImage(well, "Recess", UiKit.TrayWell,
            new Vector2(wellW, 88f), Vector2.zero, Color.white);

        RectTransform btn = UiKit.MakeRect(well, "Dish", new Vector2(wellW - 14f, 74f),
            new Vector2(0f, 2f));

        var img = btn.gameObject.AddComponent<Image>();
        img.sprite = UiKit.Dish;
        img.type = Image.Type.Sliced;
        img.color = color;
        img.raycastTarget = true;

        var button = btn.gameObject.AddComponent<Button>();
        button.targetGraphic = img;

        // Заметная реакция: в полноэкранном меню мелкий сдвиг оттенка не читается.
        ColorBlock cb = button.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
        cb.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        cb.selectedColor = Color.white;
        cb.fadeDuration = 0.08f;
        button.colors = cb;
        button.onClick.AddListener(() => onClick());

        if (icon != null)
        {
            UiKit.MakeImage(btn, "Icon", icon, new Vector2(52f, 52f),
                new Vector2(-wellW * 0.5f + 46f, 0f), Color.white);
        }

        UiKit.MakeText(btn, "Label", label, 21,
            new Vector2(wellW - 130f, 32f), new Vector2(28f, 0f), textColor,
            TextAnchor.MiddleLeft, false);

        return button;
    }

    private void SliderRow(Transform parent, string label, Sprite icon, Vector2 pos,
        float trayWidth, float min, float max, Func<float> get, Action<float> set,
        string format)
    {
        float wellW = trayWidth - 96f;
        RectTransform well = UiKit.MakeRect(parent, "Row_" + label,
            new Vector2(wellW, 58f), pos);
        UiKit.MakeImage(well, "Recess", UiKit.TrayWell,
            new Vector2(wellW, 58f), Vector2.zero, Color.white);

        UiKit.MakeImage(well, "Icon", icon, new Vector2(34f, 34f),
            new Vector2(-wellW * 0.5f + 32f, 0f), Color.white);

        UiKit.MakeText(well, "Label", label, 17,
            new Vector2(230f, 26f), new Vector2(-wellW * 0.5f + 178f, 0f),
            _labelLight, TextAnchor.MiddleLeft, false);

        Text value = UiKit.MakeText(well, "Value", "", 17,
            new Vector2(64f, 26f), new Vector2(wellW * 0.5f - 44f, 0f),
            _valueColor, TextAnchor.MiddleRight, false);

        const float trackW = 190f;
        RectTransform sRt = UiKit.MakeRect(well, "Slider",
            new Vector2(trackW, 26f), new Vector2(wellW * 0.5f - 190f, 0f));
        var slider = sRt.gameObject.AddComponent<Slider>();

        Image back = UiKit.MakeImage(sRt, "Track", UiKit.SliderTrack,
            new Vector2(trackW, 20f), Vector2.zero, Color.white);
        back.raycastTarget = true;

        // Slider требует именно такую вложенность Fill Area → Fill,
        // иначе заливка не тянется за значением.
        RectTransform fillArea = UiKit.MakeRect(sRt, "FillArea",
            new Vector2(trackW - 8f, 16f), Vector2.zero);
        Image fill = UiKit.MakeImage(fillArea, "Fill", UiKit.SliderFill,
            new Vector2(trackW - 8f, 16f), Vector2.zero, _sliderFillColor);
        RectTransform fillRt = fill.rectTransform;
        fillRt.anchorMin = new Vector2(0f, 0.5f);
        fillRt.anchorMax = new Vector2(1f, 0.5f);
        fillRt.sizeDelta = new Vector2(0f, 16f);

        RectTransform handleArea = UiKit.MakeRect(sRt, "HandleArea",
            new Vector2(trackW - 26f, 30f), Vector2.zero);
        Image handle = UiKit.MakeImage(handleArea, "Knob", UiKit.SliderKnob,
            new Vector2(30f, 30f), Vector2.zero, Color.white);
        handle.raycastTarget = true;

        slider.direction = Slider.Direction.LeftToRight;
        slider.fillRect = fillRt;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(get());
        value.text = get().ToString(format);

        slider.onValueChanged.AddListener(v =>
        {
            set(v);
            value.text = v.ToString(format);
        });

        _refreshers.Add(() =>
        {
            slider.SetValueWithoutNotify(get());
            value.text = get().ToString(format);
        });
    }

    /// <summary>Строка «‹ значение ›». Взята вместо Dropdown и Toggle: те тянут
    /// шаблоны и лишнюю сборку, а стрелки одинаково работают мышью и геймпадом.</summary>
    private void OptionRow(Transform parent, string label, Sprite icon, Vector2 pos,
        float trayWidth, Func<string> get, Action<int> step)
    {
        float wellW = trayWidth - 96f;
        RectTransform well = UiKit.MakeRect(parent, "Row_" + label,
            new Vector2(wellW, 58f), pos);
        UiKit.MakeImage(well, "Recess", UiKit.TrayWell,
            new Vector2(wellW, 58f), Vector2.zero, Color.white);

        UiKit.MakeImage(well, "Icon", icon, new Vector2(34f, 34f),
            new Vector2(-wellW * 0.5f + 32f, 0f), Color.white);

        UiKit.MakeText(well, "Label", label, 17,
            new Vector2(250f, 26f), new Vector2(-wellW * 0.5f + 188f, 0f),
            _labelLight, TextAnchor.MiddleLeft, false);

        Text value = UiKit.MakeText(well, "Value", get(), 17,
            new Vector2(140f, 26f), new Vector2(wellW * 0.5f - 118f, 0f),
            _valueColor, TextAnchor.MiddleCenter, false);

        Action refresh = () => value.text = get();
        _refreshers.Add(refresh);

        Arrow(well, "‹", new Vector2(wellW * 0.5f - 212f, 0f),
            () => { step(-1); refresh(); });
        Arrow(well, "›", new Vector2(wellW * 0.5f - 30f, 0f),
            () => { step(1); refresh(); });
    }

    private void Arrow(Transform parent, string glyph, Vector2 pos, Action onClick)
    {
        RectTransform rt = UiKit.MakeRect(parent, "Arrow" + glyph,
            new Vector2(38f, 38f), pos);

        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = UiKit.RoundButton;
        img.color = _dishNeutral;
        img.raycastTarget = true;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());

        UiKit.MakeText(rt, "Glyph", glyph, 24,
            new Vector2(38f, 38f), Vector2.zero, _labelDark,
            TextAnchor.MiddleCenter, false);
    }

    private static void EnsureEventSystem()
    {
        var existing = UnityEngine.Object
            .FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
        if (existing != null) return;

        var go = new GameObject("EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.EventSystems.StandaloneInputModule));
        DontDestroyOnLoad(go);
    }

    // ───────── выход ─────────

    /// <summary>Выйти в главное меню. Хост при этом рвёт сессию всем —
    /// ожидаемо, но диалог подтверждения тут стоит добавить.</summary>
    private void LeaveToMenu()
    {
        Close();
        UiModal.Reset();

        // Идём через SteamLobby: там уже разобрано, как корректно гасить
        // и сервер, и клиента. Дублировать эту логику не нужно.
        if (SteamLobby.Instance != null)
        {
            SteamLobby.Instance.Disconnect();
        }
        else if (InstanceFinder.NetworkManager != null)
        {
            if (InstanceFinder.NetworkManager.IsServerStarted)
                InstanceFinder.NetworkManager.ServerManager.StopConnection(true);
            if (InstanceFinder.NetworkManager.IsClientStarted)
                InstanceFinder.NetworkManager.ClientManager.StopConnection();
        }

        SceneManager.LoadScene(_mainMenuScene, LoadSceneMode.Single);
    }

    private void QuitGame()
    {
        GameSettings.Save();
        if (SteamLobby.Instance != null) SteamLobby.Instance.Disconnect();

        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
