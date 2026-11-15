using UnityEngine;

/// <summary>
/// v23.2 (FPS): рука локального игрока — из кэша. Раньше предметы (коробка гвоздей, черпак, стройка)
/// каждый кадр искали PlayerHand по всей сцене (FindObjectsByType) — у хоста это шло на каждом предмете.
/// Ищем не чаще раза в полсекунды и только пока руки нет.
/// </summary>
public static class CCHands
{
    private static PlayerHand _local;
    private static float _next;

    public static PlayerHand Local
    {
        get
        {
            if (_local != null && _local.IsOwner) return _local;
            if (Time.unscaledTime < _next) return null;
            _next = Time.unscaledTime + 0.5f;
            _local = null;
            foreach (PlayerHand h in Object.FindObjectsByType<PlayerHand>(FindObjectsSortMode.None))
                if (h != null && h.IsOwner) { _local = h; break; }
            return _local;
        }
    }

    /// <summary>Этот предмет сейчас в руке локального игрока.</summary>
    public static bool HeldByLocal(Object item)
    {
        PlayerHand h = Local;
        return h != null && item != null && (Object)h.HeldItem == item;
    }
}
