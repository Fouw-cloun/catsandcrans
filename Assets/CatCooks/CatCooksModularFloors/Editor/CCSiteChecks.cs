using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CatCooks.Floors.Editor
{
    public static class CCSiteChecks
    {
        [MenuItem("Tools/CatCooks/Check Construction Site v2")]
        public static void Run()
        {
            CCFloorSetup.EnsureAssets();
            var go = new GameObject("SiteValidation") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var site = go.AddComponent<CCConstructionSite>();
                var deck = new GameObject("Workdeck"); deck.transform.SetParent(go.transform, false);
                var f = deck.AddComponent<CCModularFloor>(); site.Workdeck = f;
                f.Palette = AssetDatabase.LoadAssetAtPath<CCFloorPalette>(CCFloorSetup.Presets + "/DefaultPalette.asset");
                var sizes = new[] { new Vector2(52, 42), new Vector2(78, 63), new Vector2(104, 84) };
                foreach (var size in sizes)
                    for (int variant = 0; variant < 5; variant++)
                    {
                        site.Width = size.x; site.Depth = size.y; site.TopLayout = CCFloorSetup.Layout(variant);
                        CCSiteBuilder.Validate(site);
                        var layout = UnityEngine.Object.Instantiate(site.TopLayout);
                        try
                        {
                            CCSiteBuilder.FitStairs(site, layout);
                            f.Layout = layout; f.CellSize = size.x / layout.Width; f.CellDepth = size.y / layout.Depth;
                            f.StoreyHeight = site.TopStoreyHeight; f.FrameStoreys = site.TopFrameStoreys;
                            f.ColumnWidth = site.TopColumnWidth; f.StructuralDetails = true; f.MergeColumnColliders = true; f.CollideWithBeams = false;
                            var errors = CCFloorPlanner.Validate(f);
                            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
                            var pieces = CCFloorPlanner.Create(f); var ids = new HashSet<string>();
                            int columns = 0;
                            foreach (var p in pieces)
                            {
                                if (!ids.Add(p.Id)) throw new InvalidOperationException("Duplicate piece ID: " + p.Id);
                                if (p.Size.x <= 0 || p.Size.y <= 0 || p.Size.z <= 0) throw new InvalidOperationException("Invalid size: " + p.Id);
                                if (p.Kind == CCFloorKind.Column && p.State == CCFloorState.Built &&
                                    (Mathf.Approximately(p.Position.x,0) || Mathf.Approximately(p.Position.x,size.x) || Mathf.Approximately(p.Position.z,0) || Mathf.Approximately(p.Position.z,size.y))) columns++;
                            }
                            if (columns != 32 * 4) throw new InvalidOperationException("Incomplete perimeter.");
                            var colliders = CCFloorBuilder.ColliderPieces(f, pieces);
                            foreach (var p in colliders) if (p.Kind == CCFloorKind.Beam) throw new InvalidOperationException("Beam collision optimization failed.");
                            Debug.Log($"Site check OK: {size.x} × {size.y}, {site.TopLayout.Title}; pieces {pieces.Count}, colliders {colliders.Count}.");
                        }
                        finally { UnityEngine.Object.DestroyImmediate(layout); }
                    }
                Debug.Log("15 схем/размеров проверены. Проверка рендеринга, прохода игрока, Undo и сохранения сцены выполняется отдельно в проекте.");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
