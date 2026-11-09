using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CatCooks.Floors.Editor
{
    public static class CCSiteBuilder
    {
        public static string SiteBakes => CCFloorBuilder.Root + "/Generated/SiteBakes";
        public static void Validate(CCConstructionSite s)
        {
            if (Application.isPlaying || PrefabStageUtility.GetCurrentPrefabStage() != null || PrefabUtility.IsPartOfPrefabInstance(s))
                throw new InvalidOperationException("Пересборка доступна в обычной сцене вне Play / Prefab Mode.");
            if (!s.Workdeck || s.Workdeck.transform.parent != s.transform) throw new InvalidOperationException("Workdeck должен быть непосредственным дочерним объектом здания.");
            if (!s.TopLayout) throw new InvalidOperationException("Выбери Top Layout.");
            if (s.Width < 32 || s.Depth < 32 || s.Width > 300 || s.Depth > 300 || s.GroundFloorHeight < 4 || s.GroundFloorHeight > 12 ||
                s.EmptyFloors < 0 || s.EmptyFloors > 30 || s.EmptyFloorHeight < 2.8f || s.EmptyFloorHeight > 8 || s.YardDepth < 12 || s.YardSideMargin < 3 || s.DrivewayLength < 8)
                throw new InvalidOperationException("Размеры: ширина/глубина 32–300, первый этаж 4–12, пустые этажи 0–30 по 2.8–8 м; двор от 12 м, боковой отступ от 3 м, подъезд от 8 м.");
            if (s.TopFrameStoreys < s.TopLayout.DeckLevels || s.TopFrameStoreys > 5 || s.TopStoreyHeight < 3 || s.TopStoreyHeight > 8 || s.TopColumnWidth < .25f || s.TopColumnWidth > .85f)
                throw new InvalidOperationException("Проверь высоту/число ярусов и толщину колонн верхнего каркаса.");
            if (s.AtlasResolution != 1024 && s.AtlasResolution != 2048) throw new InvalidOperationException("Atlas Resolution: 1024 или 2048.");
            if ((s.transform.lossyScale - Vector3.one).sqrMagnitude > .0001f || Vector3.Dot(s.transform.up, Vector3.up) < .9999f)
                throw new InvalidOperationException("Scale корня и родителей = 1. Разрешён только поворот вокруг Y.");
            if (s.GeneratedBuilding && s.GeneratedBuilding.transform.parent != s.transform) throw new InvalidOperationException("Generated Building принадлежит другому корню.");
        }
        public static void Rebuild(CCConstructionSite s)
        {
            Validate(s);
            CCFloorBuilder.EnsureFolder(SiteBakes);
            string folder = SiteBakes + "/Site_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            CCFloorBuilder.EnsureFolder(folder);
            string bakePath = folder + "/Building.asset";
            var bake = ScriptableObject.CreateInstance<CCFloorBake>();
            GameObject temp = null;
            bool committed = false;
            int undoGroup = -1;
            CCFloorBake newFloorBake = null;
            try
            {
                bake.LayoutName = s.TopLayout.name; bake.BuiltAt = DateTime.Now.ToString("s");
                AssetDatabase.CreateAsset(bake, bakePath);
                var material = CCSiteAtlas.Create(s, folder, bake);
                var palette = ScriptableObject.CreateInstance<CCFloorPalette>(); palette.name = "Workdeck_AtlasPalette";
                palette.AtlasMaterial = palette.Concrete = palette.Steel = palette.Safety = palette.Rubble = material;
                // Optional original module overrides survive from the existing deck palette.
                var sourcePalette = s.TopModuleOverrides ? s.TopModuleOverrides : s.Workdeck.Palette;
                if (sourcePalette)
                {
                    palette.Slab = sourcePalette.Slab; palette.Column = sourcePalette.Column; palette.Beam = sourcePalette.Beam;
                }
                AssetDatabase.AddObjectToAsset(palette, bake);
                var layout = UnityEngine.Object.Instantiate(s.TopLayout); layout.name = s.TopLayout.name + "_Fitted";
                FitStairs(s, layout); AssetDatabase.AddObjectToAsset(layout, bake);
                temp = new GameObject("Generated_Building"); temp.layer = s.gameObject.layer;
                temp.transform.SetParent(s.transform, false); temp.SetActive(false);
                List<CCSiteSocket> sockets;
                string summary = BuildEnvelope(s, temp.transform, bake, material, out sockets);
                EditorUtility.SetDirty(bake); AssetDatabase.SaveAssets();

                Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Rebuild construction site");
                Undo.RegisterCompleteObjectUndo(s, "Site data");
                Undo.RegisterCompleteObjectUndo(s.Workdeck, "Workdeck dimensions");
                Undo.RecordObject(s.Workdeck.transform, "Workdeck height");
                s.Workdeck.Layout = layout; s.Workdeck.Palette = palette;
                s.Workdeck.CellSize = s.Width / layout.Width; s.Workdeck.CellDepth = s.Depth / layout.Depth;
                s.Workdeck.StoreyHeight = s.TopStoreyHeight; s.Workdeck.FrameStoreys = s.TopFrameStoreys;
                s.Workdeck.ColumnWidth = s.TopColumnWidth; s.Workdeck.SlabThickness = .4f;
                s.Workdeck.StructuralDetails = s.StructuralDetails; s.Workdeck.ProfileBeams = s.ProfileBeams;
                s.Workdeck.CastShadows = s.CastWorkdeckShadows;
                s.Workdeck.MergeColumnColliders = true; s.Workdeck.CollideWithBeams = s.BeamCollisions;
                s.Workdeck.transform.localPosition = Vector3.up * s.WorkdeckHeight;
                s.Workdeck.transform.localRotation = Quaternion.identity; s.Workdeck.transform.localScale = Vector3.one;
                Undo.RecordObject(s.Workdeck.gameObject, "Workdeck layer"); s.Workdeck.gameObject.layer = s.gameObject.layer;
                CCFloorBuilder.Rebuild(s.Workdeck); newFloorBake = s.Workdeck.Bake;
                temp.SetActive(true); Undo.RegisterCreatedObjectUndo(temp, "Building envelope");
                if (s.GeneratedBuilding) Undo.DestroyObjectImmediate(s.GeneratedBuilding);
                s.GeneratedBuilding = temp; s.BuildingBake = bake; s.Sockets = sockets;
                s.LastBuildSummary = summary + $" • рабочий пол Y={s.WorkdeckHeight:0.##} м • атлас {s.AtlasResolution}², стандартные части: 1 материал";
                EditorUtility.SetDirty(s); EditorUtility.SetDirty(s.Workdeck);
                EditorSceneManager.MarkSceneDirty(s.gameObject.scene);
                Undo.CollapseUndoOperations(undoGroup); committed = true;
                Selection.activeGameObject = s.gameObject;
            }
            finally
            {
                if (!committed)
                {
                    if (undoGroup >= 0) { Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(undoGroup); }
                    if (temp) UnityEngine.Object.DestroyImmediate(temp);
                    if (newFloorBake) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(newFloorBake));
                    AssetDatabase.DeleteAsset(folder);
                }
            }
        }
        public static void FitStairs(CCConstructionSite s, CCFloorLayout layout)
        {
            if (!s.FitStairsToScale) return;
            float stepZ = s.Depth / layout.Depth;
            int required = Mathf.CeilToInt(s.TopStoreyHeight / (Mathf.Tan(35 * Mathf.Deg2Rad) * stepZ));
            foreach (var stair in layout.Stairs)
            {
                if (stair.Length >= required) continue;
                int end = stair.Z + stair.Length, start = end - required;
                if (start < 1) throw new InvalidOperationException("Не хватает места для пологой лестницы. Увеличь Depth или уменьши Top Storey Height.");
                for (int z = start; z < stair.Z; z++)
                    if (layout.Built(stair.X, z, stair.FromLevel + 1))
                        throw new InvalidOperationException("Удлинение лестницы затронет готовую верхнюю плиту. Перенеси лестницу в своей копии Layout.");
                stair.Z = start; stair.Length = required;
                for (int z = start - 1; z < end; z++) layout.Set(stair.X, z, stair.FromLevel, CCFloorState.Built);
                for (int z = start; z < end; z++) layout.Set(stair.X, z, stair.FromLevel + 1, CCFloorState.Reserved);
            }
        }
        static string BuildEnvelope(CCConstructionSite s, Transform root, CCFloorBake bake, Material mat, out List<CCSiteSocket> sockets)
        {
            float w = s.Width, d = s.Depth, gh = s.GroundFloorHeight, top = s.WorkdeckHeight;
            var ground = new CCSiteMesh(); var yard = new CCSiteMesh(); var shell = new CCSiteMesh(); var trim = new CCSiteMesh();
            var collision = new GameObject("GroundAndYard_Collision"); collision.layer = s.gameObject.layer; collision.transform.SetParent(root, false);
            int colliderCount = 0;
            Action<CCSiteMesh, Vector3, Vector3, int, bool> box = (mesh, center, size, tile, solid) =>
            {
                mesh.Box(center, size, CCSiteAtlas.Tile(tile));
                if (solid) { var col = collision.AddComponent<BoxCollider>(); col.center = center; col.size = size; colliderCount++; }
            };
            // Ground floor: a walkable shell with a real pedestrian entrance and loading opening.
            box(ground, new Vector3(w * .5f, -.225f, d * .5f), new Vector3(w, .45f, d), 0, true);
            if (s.EmptyFloors > 0)
                box(ground, new Vector3(w * .5f, gh - .225f, d * .5f), new Vector3(w, .45f, d), 0, true);
            box(ground, new Vector3(.15f, gh * .5f, d * .5f), new Vector3(.3f, gh, d), 5, true);
            box(ground, new Vector3(w - .15f, gh * .5f, d * .5f), new Vector3(.3f, gh, d), 5, true);
            box(ground, new Vector3(w * .5f, gh * .5f, d - .15f), new Vector3(w, gh, .3f), 5, true);
            float entryX = w * .22f, cargoX = w * .72f;
            float entryW = 4, cargoW = 8, entryH = 3.2f, cargoH = Mathf.Min(4.8f, gh - .5f);
            float[] left = { 0, entryX + entryW * .5f, cargoX + cargoW * .5f };
            float[] right = { entryX - entryW * .5f, cargoX - cargoW * .5f, w };
            for (int i = 0; i < 3; i++) box(ground, new Vector3((left[i] + right[i]) * .5f, gh * .5f, .15f), new Vector3(right[i] - left[i], gh, .3f), 5, true);
            box(ground, new Vector3(entryX, (gh + entryH) * .5f, .15f), new Vector3(entryW, gh - entryH, .3f), 6, true);
            box(ground, new Vector3(cargoX, (gh + cargoH) * .5f, .15f), new Vector3(cargoW, gh - cargoH, .3f), 6, true);
            // Entry canopy and loading canopy; no glass/transparent materials or lights.
            box(ground, new Vector3(entryX, entryH + .15f, -.9f), new Vector3(6, .22f, 2.4f), 1, true);
            box(ground, new Vector3(cargoX, cargoH + .15f, -1.4f), new Vector3(10, .22f, 3.4f), 1, true);
            for (int i = -1; i <= 1; i += 2)
            {
                box(ground, new Vector3(entryX + i * 2.15f, entryH * .5f, -.12f), new Vector3(.22f, entryH, .4f), 6, true);
                box(ground, new Vector3(cargoX + i * 4.15f, cargoH * .5f, -.12f), new Vector3(.22f, cargoH, .4f), 2, true);
            }
            // Yard and direct truck approach. All walking surfaces meet at y=0.
            float margin = s.YardSideMargin, yd = s.YardDepth, gate = 14;
            box(yard, new Vector3(w * .5f, -.15f, -yd * .5f), new Vector3(w + margin * 2, .3f, yd), 4, true);
            box(yard, new Vector3(cargoX, -.15f, -yd - s.DrivewayLength * .5f), new Vector3(12, .3f, s.DrivewayLength), 4, true);
            box(yard, new Vector3(-margin, .35f, -yd * .5f), new Vector3(.3f, .7f, yd), 0, true);
            box(yard, new Vector3(w + margin, .35f, -yd * .5f), new Vector3(.3f, .7f, yd), 0, true);
            float gateLeft = cargoX - gate * .5f, gateRight = cargoX + gate * .5f;
            box(yard, new Vector3((gateLeft - margin) * .5f, .35f, -yd), new Vector3(gateLeft + margin, .7f, .3f), 0, true);
            box(yard, new Vector3((gateRight + w + margin) * .5f, .35f, -yd), new Vector3(w + margin - gateRight, .7f, .3f), 0, true);
            // Painted loading bays and pedestrian path, merged into the yard mesh (no extra renderer/collider).
            for (int i = 0; i < 3; i++)
            {
                float x = cargoX - 10 + i * 10;
                box(yard, new Vector3(x, .012f, -9), new Vector3(.12f, .015f, 12), 2, false);
            }
            box(yard, new Vector3(cargoX, .012f, -15), new Vector3(20, .015f, .12f), 2, false);
            box(yard, new Vector3(entryX, .012f, -yd * .5f), new Vector3(2.4f, .015f, yd - .5f), 6, false);
            // Nine fake floors are just FOUR QUADS. Window rows are painted into the atlas.
            if (s.EmptyFloors > 0)
            {
                float facadeTop = top - .4f; // Stop at the underside of the work slab: no coplanar overlap.
                shell.Quad(new Vector3(w, gh, 0), new Vector3(0, gh, 0), new Vector3(0, facadeTop, 0), new Vector3(w, facadeTop, 0), CCSiteAtlas.Facade(false));
                shell.Quad(new Vector3(0, gh, d), new Vector3(w, gh, d), new Vector3(w, facadeTop, d), new Vector3(0, facadeTop, d), CCSiteAtlas.Facade(false));
                shell.Quad(new Vector3(0, gh, 0), new Vector3(0, gh, d), new Vector3(0, facadeTop, d), new Vector3(0, facadeTop, 0), CCSiteAtlas.Facade(true));
                shell.Quad(new Vector3(w, gh, d), new Vector3(w, gh, 0), new Vector3(w, facadeTop, 0), new Vector3(w, facadeTop, d), CCSiteAtlas.Facade(true));
                if (s.FacadeTrims)
                {
                    for (int row = 0; row <= s.EmptyFloors; row++)
                    {
                        float y = gh + row * s.EmptyFloorHeight;
                        box(trim, new Vector3(w * .5f, y, -.06f), new Vector3(w + .2f, .18f, .18f), 6, false);
                        box(trim, new Vector3(w * .5f, y, d + .06f), new Vector3(w + .2f, .18f, .18f), 6, false);
                        box(trim, new Vector3(-.06f, y, d * .5f), new Vector3(.18f, .18f, d), 6, false);
                        box(trim, new Vector3(w + .06f, y, d * .5f), new Vector3(.18f, .18f, d), 6, false);
                    }
                }
            }
            ground.Save("GroundFloor", root, mat, bake, s.gameObject.layer);
            yard.Save("DeliveryYard", root, mat, bake, s.gameObject.layer);
            shell.Save("NineFloors_Facade", root, mat, bake, s.gameObject.layer);
            var trimRenderer = trim.Save("FacadeTrims", root, mat, bake, s.gameObject.layer);
            if (trimRenderer)
            {
                var lod = trimRenderer.gameObject.AddComponent<LODGroup>(); lod.fadeMode = LODFadeMode.None;
                lod.SetLODs(new[] { new LOD(s.TrimCullScreenHeight, new Renderer[] { trimRenderer }) }); lod.RecalculateBounds();
            }
            sockets = new List<CCSiteSocket> {
                new CCSiteSocket { Id = "truck.approach", LocalPosition = new Vector3(cargoX, 0, -yd - s.DrivewayLength + 2) },
                new CCSiteSocket { Id = "truck.unload", LocalPosition = new Vector3(cargoX, 0, -9) },
                new CCSiteSocket { Id = "food.drop", LocalPosition = new Vector3(cargoX + 6, 0, -4) },
                new CCSiteSocket { Id = "entrance.ground", LocalPosition = new Vector3(entryX, 0, 1.5f) },
                new CCSiteSocket { Id = "lift.bottom", LocalPosition = new Vector3(w * .5f, 0, d - 5) },
                new CCSiteSocket { Id = "lift.top", LocalPosition = new Vector3(w * .5f, top, d - 5) },
                new CCSiteSocket { Id = "work.player", LocalPosition = new Vector3(w * .8f, top, d * .12f) }
            };
            var markers = new GameObject("ReferenceSockets").transform; markers.SetParent(root, false);
            foreach (var socket in sockets) { var marker = new GameObject(socket.Id); marker.transform.SetParent(markers, false); marker.transform.localPosition = socket.LocalPosition; }
            return $"Оболочка: {shell.TriangleCount} трис; первый этаж + двор: {ground.TriangleCount + yard.TriangleCount}; отделка: {trim.TriangleCount} • низ: {colliderCount} BoxCollider";
        }
    }
}
