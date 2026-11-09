using UnityEngine;

/// <summary>
/// ЭТОТ КОМПОНЕНТ ВЕШАЕТСЯ НА ПРЕФАБ ПРОДУКТА — на картошку, рядом с FoodItem.
/// Больше из системы ошмётков на еду не вешается ничего.
///
/// Остальные два MonoBehaviour системы (CatCooksFoodDebris и
/// CatCooksLensSplatter) создают себя сами при первом обращении и живут между
/// сценами — их не нужно ни на что вешать вообще.
///
/// ПОЧЕМУ КОМПОНЕНТ, А НЕ ПОЛЕ В IngredientDef: IngredientDef читают все пять
/// инструментов и реестр. Добавить туда поле ради одной картошки — значит
/// тронуть ассет, от которого зависит вся еда в игре. Компонент же вешается
/// на одну картошку, и если что-то пойдёт не так, страдает только она.
///
/// ПОЧЕМУ ОТДЕЛЬНЫМ ФАЙЛОМ: Unity ищет MonoBehaviour по имени файла. Лежи этот
/// класс внутри CatCooksFoodDebris.cs, добавить его через Add Component было бы
/// нельзя — редактор ответил бы «script class cannot be found».
/// </summary>
[DisallowMultipleComponent]
public sealed class FoodDebrisProfileBinder : MonoBehaviour
{
    [Tooltip("Профиль ошмётков этого продукта.\n\n" +
             "Пусто = старое поведение: один цвет из DebrisColor в IngredientDef. " +
             "Так что забыть заполнить не страшно — ничего не сломается, просто " +
             "струя будет однотонной, как раньше.")]
    public FoodDebrisProfile Profile;
}
