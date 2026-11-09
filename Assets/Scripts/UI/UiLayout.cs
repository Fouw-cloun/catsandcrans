using System;
using UnityEngine;

/// <summary>
/// РАЗМЕРЫ ЭЛЕМЕНТОВ ИНТЕРФЕЙСА. Локальные настройки игрока: по сети не
/// гоняются, у каждого участника кооператива свои, хранятся в PlayerPrefs.
///
/// Любое изменение поднимает событие Changed, на которое подписаны прицел и
/// часы. Поэтому ползунки в меню работают вживую: тянешь — элемент меняется
/// прямо под курсором, а не после перезапуска. Пересобирать канвас при этом
/// не надо, меняются только размеры существующих RectTransform.
///
/// Файл кладётся рядом с UiKit.cs, в Assets/Scripts/UI/.
/// </summary>
public static class UiLayout
{
    // ───────── значения по умолчанию ─────────

    public const float DefCrosshairSize = 46f;
    public const float DefCrosshairAlpha = 0.72f;
    public const float DefClockWidth = 240f;
    public const float DefClockHeight = 144f;
    public const float DefClockOffsetY = -6f;

    // Границы ползунков. Верхние взяты так, чтобы даже на максимуме элемент
    // не занимал больше пятой части экрана по высоте.
    public const float MinCrosshairSize = 22f;
    public const float MaxCrosshairSize = 110f;
    public const float MinClockWidth = 120f;
    public const float MaxClockWidth = 460f;
    public const float MinClockHeight = 64f;
    public const float MaxClockHeight = 280f;
    public const float MinClockOffsetY = -60f;
    public const float MaxClockOffsetY = 40f;

    // ───────── ключи PlayerPrefs ─────────

    private const string KSize = "cc_ui_crosshair_size";
    private const string KAlpha = "cc_ui_crosshair_alpha";
    private const string KClockW = "cc_ui_clock_w";
    private const string KClockH = "cc_ui_clock_h";
    private const string KClockY = "cc_ui_clock_y";

    // ───────── значения ─────────

    /// <summary>Ширина лапки-прицела, пиксели при 1080p.
    /// Высота считается от неё по пропорции спрайта.</summary>
    public static float CrosshairSize { get; private set; } = DefCrosshairSize;

    /// <summary>Прозрачность лапки в состоянии покоя.</summary>
    public static float CrosshairAlpha { get; private set; } = DefCrosshairAlpha;

    /// <summary>Ширина корпуса часов.</summary>
    public static float ClockWidth { get; private set; } = DefClockWidth;

    /// <summary>Высота корпуса часов. Независима от ширины — корпус можно
    /// растягивать и сплющивать. Циферблат при этом остаётся круглым:
    /// его диаметр берётся как меньшее из ограничений по ширине и высоте.</summary>
    public static float ClockHeight { get; private set; } = DefClockHeight;

    /// <summary>Смещение часов от верхней грани. Отрицательное утапливает
    /// корпус за край экрана.</summary>
    public static float ClockOffsetY { get; private set; } = DefClockOffsetY;

    /// <summary>Поднимается при любом изменении. Прицел и часы подписаны
    /// и перестраивают свою вёрстку на месте.</summary>
    public static event Action Changed;

    private static bool _loaded;

    // ───────── загрузка и сохранение ─────────

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        CrosshairSize = PlayerPrefs.GetFloat(KSize, DefCrosshairSize);
        CrosshairAlpha = PlayerPrefs.GetFloat(KAlpha, DefCrosshairAlpha);
        ClockWidth = PlayerPrefs.GetFloat(KClockW, DefClockWidth);
        ClockHeight = PlayerPrefs.GetFloat(KClockH, DefClockHeight);
        ClockOffsetY = PlayerPrefs.GetFloat(KClockY, DefClockOffsetY);

        Clamp();
    }

    /// <summary>Значения из PlayerPrefs могли остаться от старой версии с
    /// другими границами — приводим в допустимый диапазон.</summary>
    private static void Clamp()
    {
        CrosshairSize = Mathf.Clamp(CrosshairSize, MinCrosshairSize, MaxCrosshairSize);
        CrosshairAlpha = Mathf.Clamp(CrosshairAlpha, 0.15f, 1f);
        ClockWidth = Mathf.Clamp(ClockWidth, MinClockWidth, MaxClockWidth);
        ClockHeight = Mathf.Clamp(ClockHeight, MinClockHeight, MaxClockHeight);
        ClockOffsetY = Mathf.Clamp(ClockOffsetY, MinClockOffsetY, MaxClockOffsetY);
    }

    public static void SetCrosshairSize(float v)
    {
        CrosshairSize = Mathf.Clamp(v, MinCrosshairSize, MaxCrosshairSize);
        PlayerPrefs.SetFloat(KSize, CrosshairSize);
        Raise();
    }

    public static void SetCrosshairAlpha(float v)
    {
        CrosshairAlpha = Mathf.Clamp(v, 0.15f, 1f);
        PlayerPrefs.SetFloat(KAlpha, CrosshairAlpha);
        Raise();
    }

    public static void SetClockWidth(float v)
    {
        ClockWidth = Mathf.Clamp(v, MinClockWidth, MaxClockWidth);
        PlayerPrefs.SetFloat(KClockW, ClockWidth);
        Raise();
    }

    public static void SetClockHeight(float v)
    {
        ClockHeight = Mathf.Clamp(v, MinClockHeight, MaxClockHeight);
        PlayerPrefs.SetFloat(KClockH, ClockHeight);
        Raise();
    }

    public static void SetClockOffsetY(float v)
    {
        ClockOffsetY = Mathf.Clamp(v, MinClockOffsetY, MaxClockOffsetY);
        PlayerPrefs.SetFloat(KClockY, ClockOffsetY);
        Raise();
    }

    /// <summary>Вернуть всё к заводским значениям. Полезно, когда игрок
    /// растянул часы на пол-экрана и не помнит, что менял.</summary>
    public static void ResetAll()
    {
        CrosshairSize = DefCrosshairSize;
        CrosshairAlpha = DefCrosshairAlpha;
        ClockWidth = DefClockWidth;
        ClockHeight = DefClockHeight;
        ClockOffsetY = DefClockOffsetY;

        PlayerPrefs.SetFloat(KSize, CrosshairSize);
        PlayerPrefs.SetFloat(KAlpha, CrosshairAlpha);
        PlayerPrefs.SetFloat(KClockW, ClockWidth);
        PlayerPrefs.SetFloat(KClockH, ClockHeight);
        PlayerPrefs.SetFloat(KClockY, ClockOffsetY);

        Raise();
    }

    /// <summary>Пропорции корпуса часов в исходном PNG (900 × 540): вырез под
    /// циферблат имеет диаметр 384 и центр на 44.4% высоты. Отсюда и берутся
    /// коэффициенты — если перерисуешь картинку, править надо здесь.</summary>
    public const float ClockRecessByWidth = 384f / 900f;   // 0.4267
    public const float ClockRecessByHeight = 384f / 540f;  // 0.7111
    public const float ClockRecessCenterY = 240f / 540f;   // 0.4444

    /// <summary>Диаметр циферблата при заданном размере корпуса. Берётся
    /// меньшее из двух ограничений, поэтому при растягивании корпуса тарелка
    /// остаётся круглой и не вылезает за вырез.</summary>
    public static float ClockFaceDiameter(float w, float h)
    {
        return Mathf.Min(w * ClockRecessByWidth, h * ClockRecessByHeight);
    }

    private static void Raise()
    {
        Changed?.Invoke();
    }

    public static void Save() => PlayerPrefs.Save();

    /// <summary>Сброс статики при входе в Play Mode. Нужен, если в редакторе
    /// выключен Domain Reload: иначе подписчики Changed от прошлого запуска
    /// остаются висеть на уничтоженных объектах.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Changed = null;
        _loaded = false;
    }
}
