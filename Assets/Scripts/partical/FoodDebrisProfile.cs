using UnityEngine;

/// <summary>
/// ПАЛИТРА ОДНОГО СОСТОЯНИЯ продукта.
///
/// Ошмёток — не один цвет. Когда болгарка идёт по грязной картошке, в воздух
/// летит СРАЗУ ТРИ вещества: сухая земля, кожура и мокрая мякоть под ней.
/// Один DebrisColor из IngredientDef описать это не может физически — поэтому
/// здесь их четыре, и смешиваются они по ходу обработки.
/// </summary>
[System.Serializable]
public struct FoodDebrisPalette
{
    [Tooltip("Как называется состояние. Только для удобства в инспекторе — " +
             "порядок в массиве должен совпадать с States в FoodFormDef.")]
    public string Label;

    [Tooltip("Сухая земля. Летит пылью и мелкой крошкой, тёмная и матовая.")]
    public Color Dirt;

    [Tooltip("Кожура. Летит длинными плоскими стружками.")]
    public Color Skin;

    [Tooltip("Мякоть под кожурой. Летит кусками, самая заметная фракция.")]
    public Color Flesh;

    [Tooltip("Сок. Летит каплями, полупрозрачный. Альфа здесь ВАЖНА — " +
             "именно она делает каплю каплей, а не жёлтым камешком.")]
    public Color Juice;

    [Tooltip("Сколько на продукте земли, 0..1. Управляет пылью и стружкой.\n\n" +
             "Грязная картошка = 1, мытая = 0. Между состояниями значение " +
             "плавно перетекает по прогрессу обработки, поэтому струя грязи " +
             "на глазах светлеет, пока моешь.")]
    [Range(0f, 1f)] public float DirtAmount;

    [Tooltip("Сколько в продукте сока, 0..1. Управляет каплями.\n\n" +
             "У чищеной картошки больше, чем у грязной: под кожурой мокро, " +
             "а снаружи сухо.")]
    [Range(0f, 1f)] public float JuiceAmount;

    /// <summary>Смесь двух палитр. Нужна, пока обработка идёт: продукт в этот
    /// момент наполовину грязный, и струя обязана быть наполовину грязной.</summary>
    public static FoodDebrisPalette Lerp(FoodDebrisPalette a, FoodDebrisPalette b, float t)
    {
        return new FoodDebrisPalette
        {
            Label      = t < 0.5f ? a.Label : b.Label,
            Dirt       = Color.Lerp(a.Dirt,  b.Dirt,  t),
            Skin       = Color.Lerp(a.Skin,  b.Skin,  t),
            Flesh      = Color.Lerp(a.Flesh, b.Flesh, t),
            Juice      = Color.Lerp(a.Juice, b.Juice, t),
            DirtAmount = Mathf.Lerp(a.DirtAmount, b.DirtAmount, t),
            JuiceAmount= Mathf.Lerp(a.JuiceAmount, b.JuiceAmount, t)
        };
    }
}

/// <summary>
/// ПРОФИЛЬ ОШМЁТКОВ ПРОДУКТА — палитра на каждое состояние формы.
///
/// ЗАЧЕМ ОТДЕЛЬНЫЙ АССЕТ, А НЕ ПОЛЯ В IngredientDef:
/// в IngredientDef уже лежат DebrisColor и DebrisSize, и на них завязаны все
/// пять инструментов. Добавить туда массив палитр значило бы тронуть ассет,
/// который читают и болгарка, и пила, и мойка, — а профиль нужен пока одной
/// картошке. Поэтому профиль ссылается на ингредиент, а не наоборот: нет
/// профиля — всё работает по-старому, от одного DebrisColor.
///
/// СОСТОЯНИЯ ИНДЕКСИРУЮТСЯ ТАК ЖЕ, как States в FoodFormDef: 0 — базовое.
/// Не хватило элементов — берётся последний, поэтому дописывать состояния в
/// форму можно, не трогая профиль.
/// </summary>
[CreateAssetMenu(menuName = "CatCooks/Профиль ошмётков", fileName = "Debris_")]
public class FoodDebrisProfile : ScriptableObject
{
    [Header("Кому принадлежит")]
    [Tooltip("Только для порядка в проекте — служба берёт профиль по ссылке " +
             "с FoodItem, а не поиском по этому полю.")]
    public IngredientDef Ingredient;

    [Header("Палитры по состояниям")]
    [Tooltip("Порядок обязан совпадать с States в FoodFormDef этого продукта.")]
    public FoodDebrisPalette[] States = new FoodDebrisPalette[0];

    [Header("Размеры, метры")]
    [Tooltip("Кусок мякоти. Самая крупная фракция.")]
    public float ChunkSize = 0.016f;

    [Tooltip("Стружка кожуры. Длинная и плоская — здесь задаётся ДЛИНА.")]
    public float ShredSize = 0.030f;

    [Tooltip("Капля сока.")]
    public float DropletSize = 0.009f;

    [Tooltip("Пылинка земли.")]
    public float DustSize = 0.020f;

    [Header("Характер струи")]
    [Tooltip("Общий множитель густоты. Крути его, если в целом жидко или густо.")]
    [Range(0.1f, 4f)] public float Density = 1f;

    [Tooltip("Насколько сок липкий. Влияет на то, как долго капля ползёт по " +
             "стеклу камеры, прежде чем сорваться вниз.")]
    [Range(0f, 1f)] public float Stickiness = 0.6f;

    /// <summary>
    /// Палитра для конкретного состояния с учётом идущей обработки.
    ///
    /// Пока обработка копится, продукт ВИЗУАЛЬНО между двух состояний — так же
    /// плавно перетягивает материал FoodVisual. Струя обязана вести себя так
    /// же, иначе на грязной картошке до самого конца летела бы земля, а потом
    /// разом переключилась на чистую мякоть, и переход читался бы как рывок.
    /// </summary>
    public FoodDebrisPalette Resolve(byte state, byte progressTo, float progress01)
    {
        FoodDebrisPalette from = At(state);
        if (progress01 <= 0.001f || progressTo == state) return from;
        return FoodDebrisPalette.Lerp(from, At(progressTo), Mathf.Clamp01(progress01));
    }

    /// <summary>Палитра состояния. Выход за массив — последняя, а не ошибка:
    /// состояния в форму дописывают чаще, чем правят профиль.</summary>
    public FoodDebrisPalette At(byte index)
    {
        if (States == null || States.Length == 0) return Fallback();
        if (index == FoodFormDef.RottenIndex) return States[States.Length - 1];
        if (index >= States.Length) return States[States.Length - 1];
        return States[index];
    }

    /// <summary>Если профиль пустой — что-то серое, но видимое. Возвращать
    /// прозрачное нельзя: выглядело бы как «частицы не работают».</summary>
    private static FoodDebrisPalette Fallback()
    {
        return new FoodDebrisPalette
        {
            Label = "нет палитры",
            Dirt  = new Color(0.24f, 0.17f, 0.12f, 1f),
            Skin  = new Color(0.55f, 0.42f, 0.28f, 1f),
            Flesh = new Color(0.80f, 0.72f, 0.55f, 1f),
            Juice = new Color(0.90f, 0.88f, 0.72f, 0.45f),
            DirtAmount = 0.5f,
            JuiceAmount = 0.3f
        };
    }

    /// <summary>
    /// Профиль картошки с цветами, снятыми с её же текстур.
    /// Зовётся из окна создания — держать цифры в коде, а не в редакторе,
    /// удобнее: ассет можно пересоздать, если его случайно испортили.
    /// </summary>
    public static FoodDebrisPalette[] PotatoStates()
    {
        return new[]
        {
            // 0 — ГРЯЗНАЯ. Земли максимум, сока почти нет: снаружи сухо.
            new FoodDebrisPalette
            {
                Label = "грязная",
                Dirt  = new Color(0.235f, 0.169f, 0.122f, 1f),
                Skin  = new Color(0.710f, 0.408f, 0.047f, 1f),
                Flesh = new Color(0.867f, 0.584f, 0.110f, 1f),
                Juice = new Color(0.900f, 0.780f, 0.400f, 0.35f),
                DirtAmount = 1.0f,
                JuiceAmount = 0.15f
            },
            // 1 — МЫТАЯ. Земля смыта, кожура на месте, под ней уже мокро.
            new FoodDebrisPalette
            {
                Label = "мытая",
                Dirt  = new Color(0.380f, 0.290f, 0.200f, 1f),
                Skin  = new Color(0.780f, 0.500f, 0.090f, 1f),
                Flesh = new Color(1.000f, 0.861f, 0.437f, 1f),
                Juice = new Color(1.000f, 0.930f, 0.620f, 0.45f),
                DirtAmount = 0.15f,
                JuiceAmount = 0.55f
            },
            // 2 — ЧИЩЕНАЯ. Земли нет вовсе, зато мякоть открыта и течёт.
            new FoodDebrisPalette
            {
                Label = "чищеная",
                Dirt  = new Color(0.500f, 0.420f, 0.300f, 1f),
                Skin  = new Color(1.000f, 0.900f, 0.560f, 1f),
                Flesh = new Color(1.000f, 0.974f, 0.679f, 1f),
                Juice = new Color(1.000f, 1.000f, 0.843f, 0.55f),
                DirtAmount = 0.0f,
                JuiceAmount = 0.85f
            }
        };
    }
}
