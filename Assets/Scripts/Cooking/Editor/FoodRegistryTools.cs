#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Тулза реестра. Меню: CatCooks -> Пересобрать реестр еды.
///
/// Находит ВСЕ IngredientDef и ProcessDef в проекте, добавляет новые в реестр
/// и выдаёт им id. Существующие id не трогает и не переиспользует — иначе
/// сохранённые уровни и префабы начнут ссылаться не туда.
///
/// Лежит в папке Editor — обязательно. UnityEditor в рантайм-сборку не входит.
/// </summary>
public static class FoodRegistryTools
{
    [MenuItem("CatCooks/Пересобрать реестр еды")]
    public static void Rebuild()
    {
        FoodRegistry reg = LoadRegistry();
        if (reg == null) return;

        int addedIng = CollectIngredients(reg);
        int addedPrc = CollectProcesses(reg);

        reg.BuildIndex();
        EditorUtility.SetDirty(reg);
        AssetDatabase.SaveAssets();

        Debug.Log($"[FoodRegistry] Готово. Ингредиентов: {reg.Ingredients.Count} " +
                  $"(+{addedIng}), обработок: {reg.Processes.Count} (+{addedPrc}).");

        Validate();
    }

    [MenuItem("CatCooks/Проверить реестр еды")]
    public static void Validate()
    {
        FoodRegistry reg = LoadRegistry();
        if (reg == null) return;

        int problems = 0;
        HashSet<ushort> seen = new HashSet<ushort>();

        foreach (IngredientDef d in reg.Ingredients)
        {
            if (d == null) { Debug.LogError("[FoodRegistry] Пустая строка в Ingredients"); problems++; continue; }
            if (d.Id == 0) { Debug.LogError($"[FoodRegistry] Без id: {d.name}", d); problems++; }
            else if (!seen.Add(d.Id)) { Debug.LogError($"[FoodRegistry] ДУБЛЬ id {d.Id}: {d.name}", d); problems++; }
            if (string.IsNullOrWhiteSpace(d.DisplayName))
                Debug.LogWarning($"[FoodRegistry] Пустое DisplayName: {d.name}", d);
            if (d.Form == IngredientForm.Piece && d.UnitMassG <= 0)
            { Debug.LogError($"[FoodRegistry] UnitMassG <= 0: {d.name}", d); problems++; }
        }

        seen.Clear();
        foreach (ProcessDef p in reg.Processes)
        {
            if (p == null) { Debug.LogError("[FoodRegistry] Пустая строка в Processes"); problems++; continue; }
            if (p.Id == 0) { Debug.LogError($"[FoodRegistry] Без id: {p.name}", p); problems++; }
            else if (!seen.Add(p.Id)) { Debug.LogError($"[FoodRegistry] ДУБЛЬ id {p.Id}: {p.name}", p); problems++; }
        }

        if (problems == 0) Debug.Log("[FoodRegistry] Проблем не найдено.");
        else Debug.LogError($"[FoodRegistry] Проблем: {problems}");

    }

    // ───────────────────────── ВНУТРЕННЕЕ ─────────────────────────

    private static FoodRegistry LoadRegistry()
    {
        FoodRegistry reg = Resources.Load<FoodRegistry>("FoodRegistry");
        if (reg == null)
            Debug.LogError("[FoodRegistry] Создай ассет: ПКМ -> Create -> CatCooks -> " +
                           "Food Registry, и положи его в Assets/Resources/FoodRegistry.asset");
        return reg;
    }

    private static int CollectIngredients(FoodRegistry reg)
    {
        int added = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:IngredientDef"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            IngredientDef d = AssetDatabase.LoadAssetAtPath<IngredientDef>(path);
            if (d == null || reg.Ingredients.Contains(d)) continue;

            reg.Ingredients.Add(d);
            added++;
            if (d.Id == 0)
            {
                d.EditorAssignId(reg.NextIngredientId++);
                EditorUtility.SetDirty(d);
            }
        }
        return added;
    }

    private static int CollectProcesses(FoodRegistry reg)
    {
        int added = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:ProcessDef"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ProcessDef p = AssetDatabase.LoadAssetAtPath<ProcessDef>(path);
            if (p == null || reg.Processes.Contains(p)) continue;

            reg.Processes.Add(p);
            added++;
            if (p.Id == 0)
            {
                p.EditorAssignId(reg.NextProcessId++);
                EditorUtility.SetDirty(p);
            }
        }
        return added;
    }
}
#endif
