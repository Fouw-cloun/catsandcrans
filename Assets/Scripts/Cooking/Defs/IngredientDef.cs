using UnityEngine;

/// <summary>Категории из твоего списка ингредиентов.</summary>
public enum IngredientCategory
{
    Meat, Fish, Vegetable, Herb, Grain, Legume, Dairy, Egg, Fruit,
    Oil, Sauce, Spice, Sweetener, Extra, Liquid,
    /// <summary>Полуфабрикат, сделанный игроком: ру, бешамель, софрито, сметана.</summary>
    Crafted
}

/// <summary>Штука или сыпучее/жидкое.</summary>
public enum IngredientForm
{
    /// <summary>Свёкла, картошка, куриное бедро — отдельный предмет в мире.</summary>
    Piece,
    /// <summary>Соль, мука, вода, масло — течёт массой из дозатора.</summary>
    Bulk
}

/// <summary>
/// Справочник одного продукта. Один ассет = одна строка из твоего списка.
///
/// Результаты рецептов (ру, бешамель, софрито, сметана, панир) — ТОЖЕ такие
/// ассеты, с Category = Crafted. Поэтому компонентная готовка и композитное
/// блюдо обслуживаются одной системой, а не двумя.
/// </summary>
[CreateAssetMenu(menuName = "CatCooks/Ingredient", fileName = "Ing_")]
public class IngredientDef : ScriptableObject
{
    [Header("Идентификация")]
    [Tooltip("Стабильный числовой id — именно он летит по сети. Выдаётся " +
             "реестром автоматически, руками НЕ трогать.")]
    [SerializeField] private ushort _id;
    public ushort Id => _id;

    [Tooltip("Как называется в UI: «Свёкла»")]
    public string DisplayName = "";

    public IngredientCategory Category = IngredientCategory.Vegetable;
    public IngredientForm Form = IngredientForm.Piece;

    [Header("Физика")]
    [Tooltip("Масса одной штуки со склада, ГРАММЫ (для Form = Piece)")]
    public int UnitMassG = 300;

    [Tooltip("Префаб предмета в мире (для Form = Piece)")]
    public GameObject WorldPrefab;

    [Header("Вкус")]
    [Tooltip("Вклад во вкус на 1 КГ продукта. Для соли Salt большой, " +
             "для воды всё нули — она только разбавляет массой.")]
    public TasteVector TastePerKg;

    [Header("Ошмётки при обработке")]
    [Tooltip("Цвет кожуры/стружки этого продукта. Одна система частиц на все " +
             "овощи, цвет подставляется отсюда — поэтому у картошки летит " +
             "коричневое, у свёклы бордовое.")]
    public Color DebrisColor = new Color(0.40f, 0.29f, 0.18f);

    [Tooltip("Размер ошмётка, метры. У капусты крупнее, у моркови мельче.")]
    public float DebrisSize = 0.02f;

    [Header("Обработка")]
    [Tooltip("Что с этим продуктом вообще можно делать. Нужно для подсказок " +
             "и чтобы нельзя было сварить соль.")]
    public ProcessDef[] AllowedProcesses;

#if UNITY_EDITOR
    /// <summary>Только для реестра — он единственный, кто выдаёт id.</summary>
    public void EditorAssignId(ushort id) => _id = id;
#endif
}
