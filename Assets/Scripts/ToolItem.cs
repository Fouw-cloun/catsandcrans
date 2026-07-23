using UnityEngine;

/// <summary>
/// Инструмент (нож, молоток). ЛКМ — применить по тому, на что смотрит игрок:
/// сервер делает raycast по прицелу из UseContext и, если попал в IToolTarget,
/// вызывает его ApplyTool. Так один инструмент работает с любыми целями.
/// </summary>
public class ToolItem : InteractableItem
{
    [Header("Инструмент")]
    [Tooltip("Тип инструмента — цель сама решит, как реагировать")]
    [SerializeField] private string _toolId = "knife";
    [SerializeField] private float _useRange = 2.5f;
    [SerializeField] private LayerMask _targetMask = ~0;

    public string ToolId => _toolId;

    public override void ServerPrimaryUse(UseContext ctx)
    {
        // Луч из прицела игрока (его прислал клиент-владелец).
        if (Physics.Raycast(ctx.Origin, ctx.Direction, out RaycastHit hit, _useRange, _targetMask))
        {
            IToolTarget target = hit.collider.GetComponentInParent<IToolTarget>();
            if (target != null)
            {
                target.ApplyTool(this, ctx.User);
                return;
            }
        }
        Debug.Log($"[Tool:{_toolId}] ЛКМ — цели нет.");
    }
}
