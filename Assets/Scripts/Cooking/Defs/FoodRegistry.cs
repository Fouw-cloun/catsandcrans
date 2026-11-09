using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// РЕЕСТР. Единственное место, где ингредиенты и обработки получают числовые id.
///
/// Зачем: по сети летит ushort, а не ссылка на ScriptableObject. При двухстах
/// ингредиентах руками расставленные id гарантированно продублируются, и это
/// проявится как «в чане оказалась не та еда» — то есть максимально непонятно.
/// Реестр выдаёт id сам и никогда их не переиспользует.
///
/// РАЗМЕЩЕНИЕ: ассет должен лежать в Assets/Resources/FoodRegistry.asset —
/// иначе Load не найдёт его в билде.
/// </summary>
[CreateAssetMenu(menuName = "CatCooks/Food Registry", fileName = "FoodRegistry")]
public class FoodRegistry : ScriptableObject
{
    private const string ResourcePath = "FoodRegistry";

    [Tooltip("Все ингредиенты игры. Порядок не важен — id уже выданы.")]
    public List<IngredientDef> Ingredients = new List<IngredientDef>();

    [Tooltip("Все обработки игры.")]
    public List<ProcessDef> Processes = new List<ProcessDef>();

    [Tooltip("Следующий свободный id. Только растёт, старые не переиспользуются.")]
    public ushort NextIngredientId = 1;
    public ushort NextProcessId = 1;

    // ───────────────────────── ДОСТУП ─────────────────────────

    private static FoodRegistry _instance;
    private Dictionary<ushort, IngredientDef> _ingById;
    private Dictionary<ushort, ProcessDef> _procById;

    public static FoodRegistry Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = Resources.Load<FoodRegistry>(ResourcePath);
                if (_instance == null)
                    Debug.LogError($"[FoodRegistry] Не найден Resources/{ResourcePath}.asset");
                else
                    _instance.BuildIndex();
            }
            return _instance;
        }
    }

    public void BuildIndex()
    {
        _ingById = new Dictionary<ushort, IngredientDef>(Ingredients.Count);
        foreach (IngredientDef d in Ingredients)
            if (d != null && d.Id != 0) _ingById[d.Id] = d;

        _procById = new Dictionary<ushort, ProcessDef>(Processes.Count);
        foreach (ProcessDef p in Processes)
            if (p != null && p.Id != 0) _procById[p.Id] = p;
    }

    public IngredientDef GetIngredient(ushort id)
    {
        if (_ingById == null) BuildIndex();
        return _ingById.TryGetValue(id, out IngredientDef d) ? d : null;
    }

    public ProcessDef GetProcess(ushort id)
    {
        if (_procById == null) BuildIndex();
        return _procById.TryGetValue(id, out ProcessDef p) ? p : null;
    }

    /// <summary>Имя для UI. Не падает на неизвестном id — покажет заглушку.</summary>
    public static string NameOfIngredient(ushort id)
    {
        IngredientDef d = Instance != null ? Instance.GetIngredient(id) : null;
        return d != null ? d.DisplayName : $"?#{id}";
    }

    public static string NameOfProcess(ushort id)
    {
        if (id == 0) return "сырое";
        ProcessDef p = Instance != null ? Instance.GetProcess(id) : null;
        return p != null ? p.DisplayName : $"?#{id}";
    }
}
