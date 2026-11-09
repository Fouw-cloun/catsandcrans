using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Floors.Editor
{
    public static class CCFloorChecks
    {
        [MenuItem("Tools/CatCooks/Check Five Floor Layouts")]
        public static void Run()
        {
            CCFloorSetup.EnsureAssets();
            var go = new GameObject("FloorValidation") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var f = go.AddComponent<CCModularFloor>();
                f.Palette = AssetDatabase.LoadAssetAtPath<CCFloorPalette>(CCFloorSetup.Presets + "/DefaultPalette.asset");
                for (int i = 0; i < 5; i++)
                {
                    f.Layout = CCFloorSetup.Layout(i);
                    var errors = CCFloorPlanner.Validate(f);
                    if (errors.Count != 0) throw new InvalidOperationException(f.Layout.name + ": " + string.Join("; ", errors));
                    var pieces = CCFloorPlanner.Create(f);
                    var ids = new HashSet<string>(); int columns = 0;
                    foreach (var p in pieces)
                    {
                        if (!ids.Add(p.Id)) throw new InvalidOperationException("Повтор ID: " + p.Id);
                        if (p.Size.x <= 0 || p.Size.y <= 0 || p.Size.z <= 0) throw new InvalidOperationException("Нулевой размер: " + p.Id);
                        if (p.Kind == CCFloorKind.Column && p.State == CCFloorState.Built &&
                            (Mathf.Approximately(p.Position.x, 0) || Mathf.Approximately(p.Position.z, 0) ||
                             Mathf.Approximately(p.Position.x, f.Layout.Width * f.CellSize) || Mathf.Approximately(p.Position.z, f.Layout.Depth * f.StepZ))) columns++;
                    }
                    int expected = (2 * f.Layout.Width + 2 * f.Layout.Depth) * f.FrameStoreys;
                    if (columns != expected) throw new InvalidOperationException("Неполный внешний каркас: " + f.Layout.name);
                    Debug.Log("Floor check OK: " + f.Layout.Title + ", " + pieces.Count + " logical pieces, " + columns + " perimeter columns.");
                }
                Debug.Log("Пять схем прошли проверку данных и связности. Это не заменяет проверку ходьбы, Undo и сохранения сцены в Play Mode.");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
