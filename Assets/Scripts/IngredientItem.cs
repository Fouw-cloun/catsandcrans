using UnityEngine;

/// <summary>
/// Ингредиент — простейший переносимый предмет (овощ, заготовка).
///
///   F/G — взять / бросить (как у любого предмета, с гравитацией при падении).
///   ЛКМ — ОСНОВНОЕ применение: ЗАРЯЖЕННЫЙ БРОСОК.
///         Зажми ЛКМ — идёт накопление силы (0..3 сек = 0..100% силы),
///         отпусти — ингредиент летит вперёд с накопленной силой.
///         Реализовано через ChargedPrimaryUse=true: PlayerInteractor сам
///         копит заряд и вызывает ServerPrimaryUse только при отпускании.
///
/// Требует Rigidbody на префабе — без него предмет не падает и не летит
/// (см. InteractableItem.SetPhysicsHeld и ServerOnDrop).
/// </summary>
public class IngredientItem : InteractableItem
{
    [Header("Ингредиент")]
    [Tooltip("Тип ингредиента — пригодится для рецептов позже")]
    [SerializeField] private string _ingredientId = "potato";

    [Header("Бросок (ЛКМ зажат-отпущен)")]
    [Tooltip("Сила броска при ЛКМ без удержания (charge ~ 0)")]
    [SerializeField] private float _minThrowForce = 3f;
    [Tooltip("Сила броска при полном удержании 3 сек (charge = 1)")]
    [SerializeField] private float _maxThrowForce = 16f;

    public string IngredientId => _ingredientId;

    // ЛКМ у ингредиента — это бросок, не мгновенное применение.
    public override bool ChargedPrimaryUse => true;

    public override void ServerPrimaryUse(UseContext ctx)
    {
        // Предмет мог уже потеряться/смениться, пока копился заряд — подстрахуемся.
        if (ctx.Hand == null || ctx.Hand.HeldItem != this) return;

        float force = Mathf.Lerp(_minThrowForce, _maxThrowForce, ctx.Charge);

        // Переиспользуем обычный пайплайн броска (снятие владения, физика,
        // гравитация), просто с силой по заряду вместо стандартной слабой.
        ctx.Hand.ServerDrop(ctx.Direction, force);
    }

    // Пример: ПКМ — выбросить с эффектом (заглушка под будущую логику).
    public override void ServerSecondaryUse(UseContext ctx)
    {
        // Здесь позже: положить в кастрюлю/на станцию, если целишься в неё.
        Debug.Log($"[Ingredient:{_ingredientId}] ПКМ — пока ничего.");
    }
}
