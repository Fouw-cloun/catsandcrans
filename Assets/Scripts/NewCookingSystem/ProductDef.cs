using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Как выглядит продукт в одном состоянии.
    /// Пустое поле = не менять: материал без меша — только перекраска.
    ///
    /// КУСОЧКИ — это НЕ кучка. У картошки из слайсов каждый отрезанный слайс
    /// остаётся своим мешем (его ставит SlicePiece). Поэтому для «Кусочков»
    /// задавай только материал среза, а Mesh оставляй пустым.
    /// </summary>
    [Serializable]
    public struct StateLook
    {
        public FoodState State;
        [Tooltip("Материал этого состояния. Пусто — остаётся прежний, но \n" +
                 "подкрашивается к BurntColor, если состояние испорченное.")]
        public Material Material;
        [Tooltip("В какой слот материалов ставить. 0 — первый.\n\n" +
                 "Если у модели два материала (кожура + мякоть), кожура обычно в слоте 0: " +
                 "мытьё и чистка меняют только её, мякоть на срезе остаётся.\n" +
                 "-1 — заменить ВСЕ слоты этим материалом.")]
        public int Slot;
        [Tooltip("Свой меш (чищеная картошка другой формы). Пусто — меш не меняется. " +
                 "Для слайсов и кусочков оставляй пустым.")]
        public Mesh Mesh;
    }

    /// <summary>
    /// ПРОДУКТ.
    ///
    /// Один ScriptableObject — один файл с тем же именем. (Именно это правило
    /// нарушено в CatCooksLookData.cs, и поэтому там отвалились пресеты погоды.)
    ///
    /// ═══ ЧТО ИЗМЕНИЛОСЬ ПРИ ПЕРЕНОСЕ ═══
    ///
    /// Убраны LegacyForm и LegacyIngredient. Идея «старый FoodFormDef даёт
    /// материалы по состояниям» не работала: он ждёт индексы 0..N своего графа,
    /// а новый StateIndex — это число из enum (Варёное = 40). Такой индекс
    /// молча откатывался в 0, и материал не менялся никогда.
    ///
    /// Вместо этого — список Looks: состояние → материал/меш. Читает его
    /// FoodStateVisual.
    ///
    /// Ни в одном из 38 ассетов продуктов Legacy-поля заполнены не были,
    /// поэтому ничего не теряется. GUID скрипта сохранён (.meta из старого
    /// проекта), ассеты открываются без переназначения.
    /// </summary>
    [CreateAssetMenu(menuName = "CatCooks/Продукт", fileName = "Food_")]
    public class ProductDef : ScriptableObject
    {
        [Header("Опознание")]
        [Tooltip("По сети летит ЭТО ЧИСЛО, а не ссылка на ассет. " +
                 "Назначается один раз и больше не меняется никогда.")]
        public ushort Id;

        public string Title = "Картофель";

        [Header("Принадлежность")]
        [Tooltip("Отсюда продукт получает все свои переходы")]
        public FoodGroupDef Group;

        [Header("Исключения из правил группы")]
        [Tooltip("Правила, которые ПЕРЕКРЫВАЮТ групповые для этого продукта.\n\n" +
                 "Если тут больше трёх-четырёх строк — скорее всего, продукт " +
                 "не в той группе.")]
        public List<FoodRule> Overrides = new List<FoodRule>();

        [Header("Внешность")]
        [Tooltip("Базовый цвет. Из него получаются цвета кучки, массы и ошмётков.")]
        public Color BaseColor = new Color(0.8f, 0.72f, 0.55f);

        [Tooltip("Во что красится при переготовке")]
        public Color BurntColor = new Color(0.22f, 0.16f, 0.11f);

        [Tooltip("Цвет крошек и брызг при обработке. Прозрачный — берётся BaseColor.")]
        public Color DebrisColor = new Color(0f, 0f, 0f, 0f);

        [Tooltip("Меш для класса «целое»")]
        public Mesh SolidMesh;

        [Tooltip("Меши кусков для класса «куски»")]
        public Mesh[] PieceMeshes;

        [Tooltip("Характерный размер в метрах: картофелина 0.05, кочан 0.15.")]
        public float Size = 0.05f;

        [Tooltip("Внешность по состояниям. Не обязано покрывать все: " +
                 "отсутствующее состояние оставляет вид как есть.")]
        public List<StateLook> Looks = new List<StateLook>();

        [Header("Полуфабрикат")]
        [Tooltip("Не покупается, а готовится по своему рецепту.")]
        public bool MadeByRecipe;

        [Header("Застывание (в таре)")]
        [Tooltip("Во что сам превращается в таре со временем (шоколадная паста → шоколад). Пусто — не меняется")]
        public ProductDef HardensInto;
        [Tooltip("Через сколько секунд в таре застывает")]
        [Min(1f)] public float HardenSeconds = 90f;
        [Tooltip("Твёрдое: мастерком/лопатой из тары не черпается (застывший шоколад)")]
        public bool Solid;

        public FoodState StartState =>
            Group != null ? Group.StartState : FoodState.Raw;

        /// <summary>Цвет ошмётков с запасным вариантом.</summary>
        public Color CrumbColor => DebrisColor.a > 0.01f ? DebrisColor : BaseColor;

        /// <summary>Внешность состояния. false — такого в списке нет.</summary>
        public bool TryGetLook(FoodState state, out StateLook look)
        {
            for (int i = 0; i < Looks.Count; i++)
            {
                if (Looks[i].State == state) { look = Looks[i]; return true; }
            }
            look = default;
            return false;
        }

        /// <summary>Правило перехода: сперва исключения продукта, потом группы.</summary>
        public bool TryGetRule(FoodState from, FoodProcess proc, out FoodRule rule)
        {
            for (int i = 0; i < Overrides.Count; i++)
            {
                if (Overrides[i].From == from && Overrides[i].Process == proc)
                {
                    rule = Overrides[i];
                    return true;
                }
            }
            if (Group != null) return Group.TryGet(from, proc, out rule);
            rule = default;
            return false;
        }
    }
}
