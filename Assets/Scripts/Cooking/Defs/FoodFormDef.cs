using UnityEngine;

/// <summary>
/// СОСТОЯНИЕ — узел графа. Меш не меняется, меняется только материал.
/// Грязная картошка, чистая, чищеная — это три состояния одной формы.
/// </summary>
[System.Serializable]
public struct FoodState
{
    [Tooltip("Название: «грязная», «чищеная»")]
    public string Label;

    [Tooltip("Материал этого состояния")]
    public Material Material;

    [Tooltip("ЭТО СОСТОЯНИЕ — УЖЕ НЕ ПРЕДМЕТ, А МАССА.\n\n" +
             "Дойдя до него, предмет растворяется в ёмкости, где лежит: его " +
             "масса и журнал уходят во фракцию, сам объект деспавнится. " +
             "Именно так картошка становится пюре — пюре не бывает отдельным " +
             "предметом с мешем, у него нет формы, только консистенция.\n\n" +
             "Если ёмкости рядом нет, предмет остаётся в этом состоянии " +
             "лежать как есть — растворяться некуда.")]
    public bool DissolvesIntoVessel;

    [Tooltip("Положение узла в редакторе графа. Руками не трогать — " +
             "двигается мышкой в окне «Редактор форм еды».")]
    public Vector2 EditorPos;
}

/// <summary>
/// ПЕРЕХОД — ребро графа. «Из состояния A по обработке P в состояние B».
///
/// Направление задаётся явно, поэтому необратимость получается сама собой:
/// нарисовал стрелку только вперёд — назад пути нет. А если нужен возврат
/// (уронил чищеную в грязь), просто рисуешь ещё одну стрелку обратно.
/// </summary>
[System.Serializable]
public struct FoodTransition
{
    [Tooltip("Индекс состояния-источника")]
    public int From;

    [Tooltip("Обработка-триггер")]
    public ProcessDef Process;

    [Tooltip("Индекс состояния-приёмника")]
    public int To;

    [Tooltip("Секунд на плавную смену материала. 0 = мгновенно. " +
             "Плавно работает, только если у обоих материалов ОДИН шейдер.")]
    public float BlendSeconds;

    [Tooltip("ВО СКОЛЬКО РАЗ БЫСТРЕЕ ИДЁТ ИМЕННО ЭТОТ ПЕРЕХОД.\n\n" +
             "Скорость — свойство ребра, а не инструмента: одним и тем же " +
             "молотком кусочки разбиваются быстрее целой картофелины, и " +
             "разница живёт в графе, а не в настройках молотка. Иначе " +
             "пришлось бы держать в инструменте таблицу «что про что знаю», " +
             "и каждый новый продукт правил бы чужой скрипт.\n\n" +
             "0 или 1 = обычная скорость, 2 = вдвое быстрее, 0.5 = вдвое дольше.\n\n" +
             "НОЛЬ РАВЕН ЕДИНИЦЕ намеренно. Unity сидит на C# 9, где структуре " +
             "нельзя задать значение поля по умолчанию, — а все рёбра, " +
             "нарисованные до появления этого поля, сериализованы с нулём. " +
             "Считать ноль остановкой значило бы молча сломать весь готовый " +
             "граф, поэтому ноль трактуется как «обычно».")]
    [Range(0f, 5f)] public float WorkMultiplier;
}

/// <summary>
/// ФОРМА ПРОДУКТА — один меш и все его состояния.
///
/// «Целая картошка» — это форма. Внутри неё состояния: грязная, чистая,
/// чищеная. Меш у всех один, разница только в материале.
///
/// КАК ЭТО РАБОТАЕТ:
///   States[0]   — базовое состояние. Продукт таким спавнится.
///   States[1..] — остальные, достижимы только по переходам.
///   Transitions — рёбра. Нет ребра — обработка не сменит вид.
///   Rotten      — испорченное. Вне графа: ставится флагом, а не обработкой.
///
/// Граф строится мышкой в окне CatCooks -> Редактор форм еды.
///
/// Переходы МЕЖДУ формами (целая -> нарезанная, там меняется меш и объект
/// пересоздаётся) появятся следующим шагом. Сейчас форма одна.
/// </summary>
[CreateAssetMenu(menuName = "CatCooks/Food Form", fileName = "Form_")]
public class FoodFormDef : ScriptableObject
{
    public const byte RottenIndex = 255;

    [Tooltip("Название: «целая картошка»")]
    public string Label = "";

    [Tooltip("Какому продукту принадлежит форма")]
    public IngredientDef Ingredient;

    [Tooltip("Состояния. ПЕРВОЕ (индекс 0) — базовое, продукт таким спавнится.")]
    public FoodState[] States = new FoodState[0];

    [Tooltip("Рёбра графа. Редактируются в окне редактора форм.")]
    public FoodTransition[] Transitions = new FoodTransition[0];

    [Header("Вне графа")]
    [Tooltip("Испорченное. Ставится флагом ServerSetRotten, обработкой недостижимо. " +
             "Условия порчи (шанс при спавне, время без холодильника) — позже.")]
    public FoodState Rotten;

    /// <summary>
    /// Куда ведёт обработка из текущего состояния.
    /// Возвращает -1, если такого ребра нет — тогда вид не меняется, но
    /// обработка всё равно запишется в журнал.
    /// </summary>
    public int FindTransition(byte fromState, ProcessDef process, out float blendSeconds)
        => FindTransition(fromState, process, out blendSeconds, out _);

    /// <summary>
    /// То же, плюс множитель скорости ребра. Отдельной перегрузкой, а не
    /// правкой существующей: старую зовут из нескольких мест, и менять им
    /// подпись ради одного нового значения — лишний повод что-то сломать.
    /// </summary>
    public int FindTransition(byte fromState, ProcessDef process,
                              out float blendSeconds, out float workMultiplier)
    {
        blendSeconds = 0f;
        workMultiplier = 1f;
        if (process == null || Transitions == null) return -1;

        foreach (FoodTransition t in Transitions)
        {
            if (t.From != fromState || t.Process != process) continue;
            if (t.To < 0 || t.To >= States.Length) continue;
            blendSeconds = t.BlendSeconds;

            // Ноль означает «поле не заполняли», а не «работа стоит»: рёбра,
            // нарисованные до появления множителя, сериализованы с нулём и
            // обязаны продолжать работать как раньше.
            workMultiplier = t.WorkMultiplier > 0.01f ? t.WorkMultiplier : 1f;
            return t.To;
        }
        return -1;
    }

    /// <summary>Растворяется ли предмет, дойдя до этого состояния.</summary>
    public bool DissolvesAt(byte index)
    {
        if (index == RottenIndex || States == null || index >= States.Length) return false;
        return States[index].DissolvesIntoVessel;
    }

    /// <summary>Материал состояния. Неизвестный индекс = базовое состояние.</summary>
    public Material GetMaterial(byte index)
    {
        if (index == RottenIndex) return Rotten.Material;
        if (States == null || States.Length == 0) return null;
        if (index >= States.Length) index = 0;
        return States[index].Material;
    }

    public string GetLabel(byte index)
    {
        if (index == RottenIndex) return string.IsNullOrEmpty(Rotten.Label) ? "гнилое" : Rotten.Label;
        if (States == null || index >= States.Length) return "?";
        return States[index].Label;
    }
}
