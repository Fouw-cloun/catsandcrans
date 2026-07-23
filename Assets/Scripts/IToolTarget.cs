using FishNet.Connection;
using UnityEngine;

/// <summary>
/// Цель, по которой работает инструмент (доска для нарезки, гвоздь, заготовка).
/// Реализуй на любом объекте, который должен реагировать на инструмент.
/// </summary>
public interface IToolTarget
{
    /// <summary>Вызывается на сервере, когда по цели применили инструмент.</summary>
    void ApplyTool(ToolItem tool, NetworkConnection user);
}
