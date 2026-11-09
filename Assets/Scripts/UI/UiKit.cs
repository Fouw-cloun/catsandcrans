using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ОБЩАЯ БАЗА ИНТЕРФЕЙСА: палитра, загрузка спрайтов и хелперы сборки uGUI.
///
/// Спрайты лежат в Assets/Resources/UI/ и грузятся как Texture2D, а Sprite
/// собирается в коде. Так 9-slice бордеры задаются здесь, в одном месте, и
/// не нужно вручную размечать каждую картинку в Sprite Editor — при
/// перегенерации ассетов ничего не слетает.
/// </summary>
public static class UiKit
{
    // ───────────── ПАЛИТРА ─────────────

    public static readonly Color FurShadow = Hex("AD4100");
    public static readonly Color FurBase = Hex("FFAF34");
    public static readonly Color FurLight = Hex("FEF6AD");

    public static readonly Color HelmetShadow = Hex("B56B00");
    public static readonly Color HelmetBase = Hex("FFD84D");
    public static readonly Color HelmetLight = Hex("FFFAAE");

    public static readonly Color VestShadow = Hex("007D07");
    public static readonly Color VestBase = Hex("8BD44F");
    public static readonly Color VestLight = Hex("EEFFB1");

    public static readonly Color DenimShadow = Hex("004785");
    public static readonly Color DenimBase = Hex("50A8D8");
    public static readonly Color DenimLight = Hex("AAE6FE");

    public static readonly Color CreamShadow = Hex("A69176");
    public static readonly Color CreamBase = Hex("FEF3D5");
    public static readonly Color CreamLight = Hex("FFFAE7");

    public static readonly Color WoodShadow = Hex("6A2C13");
    public static readonly Color WoodBase = Hex("C08B63");
    public static readonly Color WoodLight = Hex("EDCEAF");

    public static readonly Color SteelShadow = Hex("3E4A62");
    public static readonly Color SteelBase = Hex("8EA3B8");
    public static readonly Color SteelLight = Hex("D1E1EE");

    public static readonly Color DangerShadow = Hex("930034");
    public static readonly Color DangerBase = Hex("FE3B5B");
    public static readonly Color DangerLight = Hex("FFA998");

    public static readonly Color Ink = Hex("0B0C21");
    public static readonly Color InkMid = Hex("5A5571");

    public static Color Hex(string hex)
    {
        return ColorUtility.TryParseHtmlString(hex[0] == '#' ? hex : "#" + hex,
            out Color c) ? c : Color.magenta;
    }

    public static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }

    // ───────────── СПРАЙТЫ ─────────────

    private const string Root = "UI/";
    private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
    private static bool _warned;

    /// <summary>Грузит текстуру из Resources/UI и оборачивает в Sprite с
    /// нужным 9-slice бордером. Результат кэшируется на весь запуск.</summary>
    public static Sprite Load(string file, Vector4 border = default)
    {
        if (Cache.TryGetValue(file, out Sprite cached)) return cached;

        Texture2D tex = Resources.Load<Texture2D>(Root + file);
        if (tex == null)
        {
            if (!_warned)
            {
                _warned = true;
                Debug.LogError($"[UiKit] Не найдено Resources/{Root}{file}. " +
                               "Проверь, что PNG лежат в Assets/Resources/UI/ " +
                               "и импортированы как Texture Type: Default или Sprite.");
            }
            return null;
        }

        Sprite s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, border);
        s.name = file;
        Cache[file] = s;
        return s;
    }

    // Именованные спрайты. Бордеры подобраны под сгенерированные PNG —
    // менять их нужно только если перерисуешь сами картинки.

    // Лапа: на каждое состояние своя пара картинок. _fill — белый силуэт
    // с серыми подушечками, его красит код. _line — контур и обводки
    // подушечек, кладётся поверх и цвет не меняет.
    public static Sprite PawIdleFill => Load("paw_idle_fill");
    public static Sprite PawIdleLine => Load("paw_idle_line");
    public static Sprite PawOpenFill => Load("paw_open_fill");
    public static Sprite PawOpenLine => Load("paw_open_line");
    public static Sprite PawGrabFill => Load("paw_grab_fill");
    public static Sprite PawGrabLine => Load("paw_grab_line");

    public static Sprite ClockHousing => Load("clock_housing");
    public static Sprite ClockFace => Load("clock_face");
    public static Sprite HandSpoon => Load("clock_hand_spoon");
    public static Sprite HandFork => Load("clock_hand_fork");
    public static Sprite ClockCap => Load("clock_cap");

    public static Sprite TrayFrame => Load("tray_frame", new Vector4(54, 54, 54, 54));
    public static Sprite TrayWell => Load("tray_well", new Vector4(34, 34, 34, 34));
    public static Sprite Dish => Load("dish", new Vector4(34, 34, 34, 34));
    public static Sprite SliderTrack => Load("slider_track", new Vector4(22, 22, 22, 22));
    public static Sprite SliderFill => Load("slider_fill", new Vector4(22, 22, 22, 22));
    public static Sprite SliderKnob => Load("slider_knob");
    public static Sprite RoundButton => Load("round_button");

    public static Sprite FoodSalad => Load("food_salad");
    public static Sprite FoodMeat => Load("food_meat");
    public static Sprite FoodEgg => Load("food_egg");
    public static Sprite FoodSoup => Load("food_soup");
    public static Sprite FoodBread => Load("food_bread");
    public static Sprite FoodCarrot => Load("food_carrot");

    // ───────────── СБОРКА uGUI ─────────────

    /// <summary>Canvas с заведомо верными настройками.
    /// interactive = false → без GraphicRaycaster: HUD не кликают, а лишний
    /// рейкастер перебирает графику каждый кадр впустую.</summary>
    public static Canvas MakeCanvas(string name, int sortingOrder, bool interactive)
    {
        var go = new GameObject(name);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        if (interactive) go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    public static RectTransform MakeRect(Transform parent, string name,
        Vector2 size, Vector2 pos, Vector2? anchor = null, Vector2? pivot = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor ?? new Vector2(0.5f, 0.5f);
        rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return rt;
    }

    public static Image MakeImage(Transform parent, string name, Sprite sprite,
        Vector2 size, Vector2 pos, Color color,
        Vector2? anchor = null, Vector2? pivot = null)
    {
        RectTransform rt = MakeRect(parent, name, size, pos, anchor, pivot);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        if (sprite != null && sprite.border.sqrMagnitude > 0f)
            img.type = Image.Type.Sliced;
        return img;
    }

    private static Font _font;

    /// <summary>Встроенный шрифт. В Unity 6 старый Arial.ttf убран, актуальное
    /// имя — LegacyRuntime.ttf. Кириллицу поддерживает.</summary>
    public static Font Font()
    {
        if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return _font;
    }

    public static Text MakeText(Transform parent, string name, string content,
        int fontSize, Vector2 size, Vector2 pos, Color color,
        TextAnchor align = TextAnchor.MiddleCenter, bool shadow = true)
    {
        RectTransform rt = MakeRect(parent, name, size, pos);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = Font();
        t.fontSize = fontSize;
        t.fontStyle = FontStyle.Bold;
        t.text = content;
        t.color = color;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;

        // Без тени текст теряется на песке и жёлтой технике.
        if (shadow)
        {
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0.04f, 0.05f, 0.13f, 0.8f);
            sh.effectDistance = new Vector2(1.6f, -1.6f);
        }
        return t;
    }
}

/// <summary>
/// ШЛЮЗ МОДАЛЬНЫХ ОКОН. Пока открыто окно, игрок не должен двигаться, крутить
/// головой и применять предмет по ЛКМ — иначе клик по кнопке «Выход» заодно
/// ударит кувалдой.
///
/// Счётчик, а не bool: окна вкладываются (пауза → настройки), и закрытие
/// внутреннего не должно возвращать управление раньше времени.
///
/// ДЛЯ КООПЕРАТИВА: здесь нет Time.timeScale = 0. Пауза одного игрока не может
/// останавливать мир — это сломает тики FishNet и заморозит игру остальным.
/// </summary>
public static class UiModal
{
    private static int _open;

    public static bool AnyOpen => _open > 0;

    public static void Push()
    {
        _open++;
        Apply();
    }

    public static void Pop()
    {
        _open = Mathf.Max(0, _open - 1);
        Apply();
    }

    public static void Reset()
    {
        _open = 0;
        Apply();
    }

    private static void Apply()
    {
        bool free = _open > 0;
        Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = free;
    }
}

/// <summary>
/// НАСТРОЙКИ ИГРОКА. Локальные: по сети не гоняются, у каждого участника
/// кооператива свои. Хранятся в PlayerPrefs.
/// </summary>
public static class GameSettings
{
    private const string KSens = "cc_sens";
    private const string KMaster = "cc_vol_master";
    private const string KMusic = "cc_vol_music";
    private const string KSfx = "cc_vol_sfx";
    private const string KQuality = "cc_quality";
    private const string KFull = "cc_fullscreen";
    private const string KVSync = "cc_vsync";

    public static float MouseSensitivity { get; private set; } = 2f;
    public static float MasterVolume { get; private set; } = 1f;
    public static float MusicVolume { get; private set; } = 0.7f;
    public static float SfxVolume { get; private set; } = 1f;

    private static bool _loaded;

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        MouseSensitivity = PlayerPrefs.GetFloat(KSens, 2f);
        MasterVolume = PlayerPrefs.GetFloat(KMaster, 1f);
        MusicVolume = PlayerPrefs.GetFloat(KMusic, 0.7f);
        SfxVolume = PlayerPrefs.GetFloat(KSfx, 1f);

        int q = PlayerPrefs.GetInt(KQuality, QualitySettings.GetQualityLevel());
        QualitySettings.SetQualityLevel(
            Mathf.Clamp(q, 0, QualitySettings.names.Length - 1), true);

        Screen.fullScreen = PlayerPrefs.GetInt(KFull, Screen.fullScreen ? 1 : 0) == 1;
        QualitySettings.vSyncCount = PlayerPrefs.GetInt(KVSync, 1);

        AudioListener.volume = MasterVolume;
    }

    public static void SetSensitivity(float v)
    {
        MouseSensitivity = Mathf.Clamp(v, 0.2f, 8f);
        PlayerPrefs.SetFloat(KSens, MouseSensitivity);
    }

    public static void SetMaster(float v)
    {
        MasterVolume = Mathf.Clamp01(v);
        PlayerPrefs.SetFloat(KMaster, MasterVolume);
        AudioListener.volume = MasterVolume;
    }

    public static void SetMusic(float v)
    {
        MusicVolume = Mathf.Clamp01(v);
        PlayerPrefs.SetFloat(KMusic, MusicVolume);
    }

    public static void SetSfx(float v)
    {
        SfxVolume = Mathf.Clamp01(v);
        PlayerPrefs.SetFloat(KSfx, SfxVolume);
    }

    public static int Quality => QualitySettings.GetQualityLevel();

    public static void SetQuality(int level)
    {
        level = Mathf.Clamp(level, 0, QualitySettings.names.Length - 1);
        QualitySettings.SetQualityLevel(level, true);
        PlayerPrefs.SetInt(KQuality, level);
    }

    public static bool Fullscreen => Screen.fullScreen;

    public static void SetFullscreen(bool on)
    {
        Screen.fullScreen = on;
        PlayerPrefs.SetInt(KFull, on ? 1 : 0);
    }

    public static bool VSync => QualitySettings.vSyncCount > 0;

    public static void SetVSync(bool on)
    {
        QualitySettings.vSyncCount = on ? 1 : 0;
        PlayerPrefs.SetInt(KVSync, on ? 1 : 0);
    }

    public static void Save() => PlayerPrefs.Save();
}
