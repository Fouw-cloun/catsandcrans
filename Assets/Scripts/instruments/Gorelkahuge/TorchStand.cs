using UnityEngine;

/// <summary>
/// ПОСТАВИТЬ ГОРЕЛКУ: включённую горелку кладут так, чтобы пламя било туда, куда
/// смотрел игрок (под ковш, в бок кастрюли). Горелка ложится на пол «на подставку»
/// и замирает (кинематика), пламя остаётся гореть.
///
/// Общий помощник для ручной горелки (BlowTorchItem) и кровельной (RoofingTorchItem).
/// Только сервер; позиция расходится по NetworkTransform.
/// </summary>
public static class TorchStand
{
    private static readonly RaycastHit[] _hits = new RaycastHit[24];

    /// <summary>
    /// Поставить горелку item так, чтобы сопло nozzle било в то, на что смотрит игрок.
    /// eye/look — откуда и куда смотрит игрок; reach — докуда ищем цель, м;
    /// flame — длина пламени, м. false — цели нет (горелку тогда просто роняют).
    /// </summary>
    public static bool ServerPlace(Transform item, Transform nozzle, Vector3 eye, Vector3 look, float reach, float flame,
                                   out Vector3 target)
    {
        target = Vector3.zero;
        if (item == null || nozzle == null || look.sqrMagnitude < 1e-6f) return false;
        look.Normalize();
        if (!Cast(eye, look, reach, item, out RaycastHit hit)) return false;
        target = hit.point;

        Vector3 flat = Vector3.ProjectOnPlane(look, Vector3.up);
        if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(item.forward, Vector3.up);
        if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
        flat.Normalize();

        // Опора — пол перед целью, со стороны игрока: горелка лежит там и смотрит на цель.
        float back = Mathf.Max(0.25f, flame * 0.9f);
        Vector3 probe = target - flat * back + Vector3.up * Mathf.Max(1.5f, back * 2f);
        Vector3 foot = target - flat * back;
        if (Cast(probe, Vector3.down, Mathf.Max(6f, back * 6f), item, out RaycastHit floor)) foot = floor.point;

        // Сопло — так, чтобы самая жаркая часть пламени (последняя треть) упиралась в цель.
        Vector3 from = foot + Vector3.up * 0.05f;
        Vector3 aim = target - from;
        if (aim.sqrMagnitude < 1e-4f) aim = flat;
        aim.Normalize();
        Vector3 nozzleAt = target - aim * flame * 0.55f;

        // Повернуть предмет: ось сопла (Z) — на цель.
        Quaternion nozzleLocal = Quaternion.Inverse(item.rotation) * nozzle.rotation;
        Vector3 upHint = Mathf.Abs(Vector3.Dot(aim, Vector3.up)) > 0.95f ? flat : Vector3.up;
        item.rotation = Quaternion.LookRotation(aim, upHint) * Quaternion.Inverse(nozzleLocal);
        item.position += nozzleAt - nozzle.position;

        // Не проваливаться в пол: низ горелки — на уровень опоры.
        bool any = false;
        Bounds b = default;
        foreach (Renderer r in item.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || !r.enabled) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (any && b.min.y < foot.y) item.position += Vector3.up * (foot.y - b.min.y + 0.01f);

        Rigidbody rb = item.GetComponent<Rigidbody>();
        if (rb != null)
        {
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        Collider c = item.GetComponent<Collider>();
        if (c != null) c.enabled = true;
        return true;
    }

    private static bool Cast(Vector3 from, Vector3 dir, float dist, Transform skip, out RaycastHit best)
    {
        best = default;
        int n = Physics.RaycastNonAlloc(from, dir, _hits, dist, ~0, QueryTriggerInteraction.Ignore);
        float bd = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = _hits[i];
            if (h.collider == null || h.transform.IsChildOf(skip)) continue;
            if (h.collider.GetComponentInParent<PlayerInteractor>() != null) continue;
            if (h.distance >= bd) continue;
            bd = h.distance; best = h; found = true;
        }
        return found;
    }

    /// <summary>Глаза игрока, который держит руку: камера, если есть, иначе сама рука.</summary>
    public static Vector3 EyeOf(PlayerHand hand)
    {
        if (hand == null) return Vector3.zero;
        Camera cam = hand.GetComponentInChildren<Camera>(true);
        if (cam == null) cam = hand.GetComponentInParent<FirstPersonController>() != null
            ? hand.GetComponentInParent<FirstPersonController>().GetComponentInChildren<Camera>(true) : null;
        return cam != null ? cam.transform.position : hand.transform.position + Vector3.up * 0.6f;
    }
}
