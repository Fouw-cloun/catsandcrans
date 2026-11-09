#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Food.EditorTools
{
    /// <summary>
    /// ПРОВЕРКА ПРОДУКТА ПЕРЕД ТЕСТОМ.
    ///
    /// Выдели картошку (в сцене или префаб в Project) и нажми
    /// CatCooks → Еда → Проверить выделенный продукт.
    ///
    /// Скрипт печатает в консоль список: что стоит, чего не хватает и что
    /// этот продукт сейчас умеет по таблице. Это быстрее, чем искать причину
    /// «почему не моется» в Play.
    /// </summary>
    public static class CatCooksProductCheck
    {
        [MenuItem("CatCooks/Еда/Проверить выделенный продукт")]
        private static void Run()
        {
            GameObject go = Selection.activeGameObject;
            if (go == null) { Debug.LogWarning("[Проверка] ничего не выделено"); return; }

            StringBuilder r = new StringBuilder();
            r.AppendLine($"═══ Проверка «{go.name}» ═══");

            bool ok = true;

            FoodPiece piece = go.GetComponent<FoodPiece>();
            if (piece == null)
            {
                Debug.LogError($"[Проверка] на «{go.name}» нет FoodPiece — это не продукт новой системы", go);
                return;
            }

            // ── компоненты ──
            ok &= Need(r, go.GetComponent<FishNet.Object.NetworkObject>() != null, "NetworkObject");
            ok &= Need(r, go.GetComponent<Rigidbody>() != null, "Rigidbody");

            Collider[] cols = go.GetComponentsInChildren<Collider>(true);
            bool solid = false;
            foreach (Collider c in cols) if (!c.isTrigger) solid = true;
            ok &= Need(r, solid, "обычный (не триггер) коллайдер");

            Warn(r, go.GetComponent<FoodStateVisual>() != null,
                 "FoodStateVisual — без него вид не меняется");

            r.AppendLine($"  · слой: {LayerMask.LayerToName(go.layer)}" +
                         " (он должен быть в масках инструментов: Food Mask у мойки и пилы)");

            // ── продукт ──
            SerializedObject so = new SerializedObject(piece);
            SerializedProperty prod = so.FindProperty("_product");
            ProductDef def = prod != null ? prod.objectReferenceValue as ProductDef : null;

            if (!Need(r, def != null, "в FoodPiece назначен Product"))
            {
                Debug.LogWarning(r.ToString(), go);
                return;
            }

            r.AppendLine($"  · продукт: «{def.Title}», Id {def.Id}");
            ok &= Need(r, def.Group != null, $"у «{def.Title}» задана группа");
            if (def.Group == null) { Debug.LogWarning(r.ToString(), go); return; }
            r.AppendLine($"  · группа: «{def.Group.Title}», старт «{FoodStateInfo.NameOf(def.Group.StartState)}», " +
                         $"правил {def.Group.Rules.Count}");

            // ── база ──
            FoodDatabase db = Object.FindAnyObjectByType<FoodDatabase>();
            if (db == null)
                r.AppendLine("  ✘ в открытой сцене НЕТ объекта с FoodDatabase. " +
                             "Создай пустой объект, повесь FoodDatabase и нажми в нём сбор продуктов, " +
                             "иначе таблица пустая и ни один процесс не сработает.");
            else
            {
                bool inDb = db.Get(def.Id) == def;
                ok &= Need(r, inDb, $"«{def.Title}» есть в списке FoodDatabase (иначе нажми сбор продуктов)");
            }

            // ── что умеет ──
            r.AppendLine("  · путь по состояниям от старта:");
            AppendChain(r, def, def.Group.StartState, 0, new HashSet<FoodState>());

            // ── внешность ──
            List<FoodState> states = new List<FoodState>();
            Collect(def, def.Group.StartState, states, 0);
            StringBuilder noLook = new StringBuilder();
            foreach (FoodState s in states)
                if (!def.TryGetLook(s, out _)) noLook.Append($"{FoodStateInfo.NameOf(s)}, ");
            if (noLook.Length > 0)
                r.AppendLine($"  ! в Looks нет материалов для: {noLook.ToString().TrimEnd(',', ' ')} — " +
                             "эти состояния будут выглядеть одинаково");

            // ── нарезка ──
            FoodSliceable slicer = go.GetComponent<FoodSliceable>();
            if (slicer != null)
            {
                SerializedObject sso = new SerializedObject(slicer);
                SerializedProperty slices = sso.FindProperty("_slices");
                SerializedProperty prefab = sso.FindProperty("_piecePrefab");
                int n = slices != null ? slices.arraySize : 0;
                ok &= Need(r, n > 1, $"FoodSliceable: собраны слайсы (сейчас {n}) — " +
                                     "жми «Собрать слайсы и заполнить префаб куска»");

                GameObject pf = prefab != null ? prefab.objectReferenceValue as GameObject : null;
                ok &= Need(r, pf != null, "FoodSliceable: назначен Piece Prefab");
                if (pf != null)
                {
                    ok &= Need(r, pf.GetComponent<FoodPiece>() != null, $"на куске «{pf.name}» есть FoodPiece");
                    ok &= Need(r, pf.GetComponent<FishNet.Object.NetworkObject>() != null,
                               $"на куске «{pf.name}» есть NetworkObject");
                    Warn(r, pf.GetComponent<SlicePiece>() != null,
                         $"на куске «{pf.name}» нет SlicePiece — все куски будут одной формы");
                    Warn(r, pf.transform.localScale == Vector3.one,
                         $"у куска «{pf.name}» Scale не 1 — размер куска задаёт нарезка");
                }
            }

            r.AppendLine(ok ? "ИТОГ: всё на месте, можно тестировать."
                            : "ИТОГ: есть незакрытые пункты выше.");
            if (ok) Debug.Log(r.ToString(), go); else Debug.LogWarning(r.ToString(), go);
        }

        private static bool Need(StringBuilder r, bool value, string what)
        {
            r.AppendLine((value ? "  ✔ " : "  ✘ ") + what);
            return value;
        }

        private static void Warn(StringBuilder r, bool value, string what)
        {
            if (!value) r.AppendLine("  ! " + what);
        }

        /// <summary>Напечатать дерево доступных переходов — что и чем можно сделать.</summary>
        private static void AppendChain(StringBuilder r, ProductDef def, FoodState from, int depth,
                                        HashSet<FoodState> seen)
        {
            if (depth > 4 || !seen.Add(from)) return;
            foreach (FoodRule rule in def.Group.Rules)
            {
                if (rule.From != from) continue;
                r.AppendLine($"      {new string(' ', depth * 3)}{FoodStateInfo.NameOf(rule.From)} + " +
                             $"{FoodStateInfo.NameOf(rule.Process)} → {FoodStateInfo.NameOf(rule.To)} " +
                             $"({rule.Seconds:0.#} с{(rule.Window > 0f ? ", окно" : "")})");
                AppendChain(r, def, rule.To, depth + 1, seen);
            }
        }

        private static void Collect(ProductDef def, FoodState from, List<FoodState> into, int depth)
        {
            if (depth > 4 || into.Contains(from)) return;
            into.Add(from);
            foreach (FoodRule rule in def.Group.Rules)
                if (rule.From == from) Collect(def, rule.To, into, depth + 1);
        }
    }
}
#endif
