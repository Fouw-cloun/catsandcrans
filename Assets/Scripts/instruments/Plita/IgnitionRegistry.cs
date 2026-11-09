using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// РЕЕСТР ИСТОЧНИКОВ ОГНЯ. Сюда записываются все горящие штуки: газовая
/// горелка, сварочная дуга, костёр — что угодно.
///
/// Зачем реестр, а не поиск по сцене: облака газа проверяют огонь каждый тик,
/// и перебирать всю сцену ради этого было бы расточительно. Здесь список
/// короткий — обычно один-два элемента.
///
/// Регистрируется ТОЛЬКО то, что реально горит прямо сейчас. Погасла горелка —
/// снялась из списка, и газ рядом с ней уже безопасен.
/// </summary>
public static class IgnitionRegistry
{
    private static readonly List<Transform> Sources = new List<Transform>();

    public static void Register(Transform t)
    {
        if (t != null && !Sources.Contains(t)) Sources.Add(t);
    }

    public static void Unregister(Transform t) => Sources.Remove(t);

    /// <summary>
    /// Есть ли огонь в радиусе. Заодно чистит мёртвые ссылки: объекты
    /// деспавнятся, а из списка сами не уходят.
    /// </summary>
    public static bool AnyNear(Vector3 point, float radius, out Vector3 where)
    {
        where = point;
        float sqr = radius * radius;

        for (int i = Sources.Count - 1; i >= 0; i--)
        {
            Transform t = Sources[i];
            if (t == null) { Sources.RemoveAt(i); continue; }

            if ((t.position - point).sqrMagnitude > sqr) continue;

            where = t.position;
            return true;
        }
        return false;
    }

    public static int Count => Sources.Count;
}
