using UnityEngine;

/// <summary>
/// Одиннадцать групп обработок — ровно те, что ты выписал в документе.
/// Группа нужна там, где конкретный способ неважен: ёмкость умеет или не умеет
/// целую группу (сковорода — HighHeat, чан — LiquidHeat).
/// </summary>
public enum ProcessGroup
{
    LiquidHeat,     // варится, кипятится, вываривается
    SurfaceStrip,   // чистится, зачищается, шелушится
    Grating,        // трётся на тёрке
    Braising,       // тушится, брезируется, томится, припускается
    Cutting,        // режется, шинкуется, рубится, рвётся на волокна
    HighHeat,       // пассеруется, обжаривается, прокаливается
    Pressing,       // давится, мнётся, разминается
    Grinding,       // дробится, измельчается, мясорубка
    Cryo,           // замораживается, колется
    Washing,        // замачивается, промывается
    MixPrep         // просеивается, раскатывается, замешивается, эмульгируется
}

/// <summary>Метка инструмента — какой железкой это делается.</summary>
public enum ToolTag
{
    Knife, Grinder, Chainsaw, Hammer, Sledge, Jackhammer, Drill, Mixer,
    Press, Sieve, Torch, Shovel, Tongs, Trowel, Vibroplate, Hands
}

/// <summary>
/// Справочник одной обработки. Одна строка из твоего «чистится / режется
/// кубиком / варится» = один такой ассет.
/// </summary>
[CreateAssetMenu(menuName = "CatCooks/Process", fileName = "Proc_")]
public class ProcessDef : ScriptableObject
{
    [Header("Идентификация")]
    [Tooltip("Выдаётся реестром автоматически, руками НЕ трогать.")]
    [SerializeField] private ushort _id;
    public ushort Id => _id;

    [Tooltip("Как называется: «режется кубиком»")]
    public string DisplayName = "";

    public ProcessGroup Group = ProcessGroup.Cutting;

    [Header("Инструмент")]
    [Tooltip("Чем это можно делать. Бензопила вместо ножа — тоже вариант, " +
             "просто качество будет другое.")]
    public ToolTag[] AcceptedTools;

    [Header("Эффект")]
    [Tooltip("Сколько массы остаётся. 0.85 = 15% ушло в шелуху и обрезки.")]
    [Range(0.05f, 1f)] public float MassYield = 1f;

    [Tooltip("Как обработка меняет вкус на 1 кг. Прокаливание специй " +
             "усиливает остроту, вымачивание вымывает соль (отрицательные значения).")]
    public TasteVector TasteDeltaPerKg;

#if UNITY_EDITOR
    public void EditorAssignId(ushort id) => _id = id;
#endif
}
