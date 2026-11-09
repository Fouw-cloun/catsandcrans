using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatCooks.Floors.Editor
{
    public static class CCFloorBuilder
    {
        public static string Root
        {
            get
            {
                string script = AssetDatabase.GUIDToAssetPath("f9f1484eef0c58ddac9d48d32883720a");
                if (string.IsNullOrEmpty(script)) return "Assets/CatCooksModularFloors";
                return System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(script)).Replace('\\', '/');
            }
        }
        sealed class Batch
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();
            public Material Material;
            public Vector3 Origin;
        }
        static readonly Vector3[] Corners = {
            new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f), new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
            new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f), new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f) };
        static readonly int[,] Faces = { {0,3,2,1}, {4,5,6,7}, {0,4,7,3}, {1,2,6,5}, {0,1,5,4}, {3,7,6,2} };

        public static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/'); string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        public static void Rebuild(CCModularFloor f)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Пересборка разрешена только вне Play.");
            if (EditorUtility.IsPersistent(f) || PrefabUtility.IsPartOfPrefabInstance(f) || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Используй отдельный корень в обычной сцене, не внутри prefab instance / Prefab Mode.");
            if (f.GeneratedRoot != null && f.GeneratedRoot.transform.parent != f.transform)
                throw new InvalidOperationException("Generated Root не принадлежит этому корню.");
            var errors = CCFloorPlanner.Validate(f);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            CheckVisual(f.Palette.Slab); CheckVisual(f.Palette.Column); CheckVisual(f.Palette.Beam);
            var pieces = CCFloorPlanner.Create(f);
            var ids = new HashSet<string>();
            foreach (var p in pieces) if (!ids.Add(p.Id)) throw new InvalidOperationException("Повтор ID: " + p.Id);
            EnsureFolder(Root + "/Generated/Bakes");
            string path = AssetDatabase.GenerateUniqueAssetPath(Root + "/Generated/Bakes/Floor_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".asset");
            var bake = ScriptableObject.CreateInstance<CCFloorBake>();
            bake.LayoutName = f.Layout.name; bake.BuiltAt = DateTime.Now.ToString("s");
            GameObject temp = null;
            bool committed = false;
            try
            {
                AssetDatabase.CreateAsset(bake, path);
                temp = new GameObject("Generated_Floor");
                temp.transform.SetParent(f.transform, false);
                temp.layer = f.gameObject.layer;
                temp.SetActive(false);
                var batches = new Dictionary<string, Batch>();
                var collisionGroups = new Dictionary<string, Transform>();
                int colliderCount = 0, customCount = 0;
                foreach (var p in ColliderPieces(f, pieces))
                {
                    string chunk = p.Level + "_" + Chunk(p.Position.x, f.CellSize, f.Layout.Width) + "_" + Chunk(p.Position.z, f.StepZ, f.Layout.Depth);
                    if (p.Collider)
                    {
                        // Same-orientation static boxes share a transform; no Rigidbody or per-module behaviour.
                        string key = chunk + "_" + p.Rotation.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "_" +
                            p.Rotation.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "_" + p.Rotation.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "_" + p.Rotation.w.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                        if (!collisionGroups.TryGetValue(key, out Transform target))
                        {
                            var go = new GameObject("Collision_" + collisionGroups.Count);
                            go.layer = f.gameObject.layer; go.transform.SetParent(temp.transform, false);
                            go.transform.localRotation = p.Rotation; target = go.transform;
                            collisionGroups.Add(key, target);
                        }
                        var box = target.gameObject.AddComponent<BoxCollider>();
                        box.center = Quaternion.Inverse(p.Rotation) * p.Position; box.size = p.Size;
                        colliderCount++;
                    }
                }
                foreach (var p in pieces)
                {
                    if (p.State != CCFloorState.Built) continue;
                    string chunk = p.Level + "_" + Chunk(p.Position.x, f.CellSize, f.Layout.Width) + "_" + Chunk(p.Position.z, f.StepZ, f.Layout.Depth);
                    if (!p.Render) continue;
                    var visual = f.Palette.Visual(p.Kind);
                    if (visual != null && visual.Prefab != null)
                    {
                        BuildVisual(visual, p, temp.transform, f.gameObject.layer); customCount++;
                        continue;
                    }
                    Material mat = f.Palette.MaterialFor(p.Kind);
                    string batchKey = chunk + "_" + mat.GetInstanceID();
                    if (!batches.TryGetValue(batchKey, out Batch batch))
                    {
                        batch = new Batch { Material = mat, Origin = new Vector3(
                            Chunk(p.Position.x, f.CellSize, f.Layout.Width) * f.CellSize * 4, p.Level * f.StoreyHeight,
                            Chunk(p.Position.z, f.StepZ, f.Layout.Depth) * f.StepZ * 4) };
                        batches.Add(batchKey, batch);
                    }
                    Rect uv = f.Palette.AtlasMaterial ? CCSiteAtlas.ForKind(p.Kind) : new Rect(0, 0, 1, 1);
                    if (f.ProfileBeams && p.Kind == CCFloorKind.Beam) AddProfile(batch, p, uv);
                    else AddBox(batch, p, uv);
                }
                int meshIndex = 0;
                foreach (var entry in batches)
                {
                    var b = entry.Value;
                    var mesh = new Mesh { name = "FloorChunk_" + meshIndex++, indexFormat = b.Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                    mesh.SetVertices(b.Vertices); mesh.SetNormals(b.Normals); mesh.SetUVs(0, b.UV); mesh.SetTriangles(b.Triangles, 0); mesh.RecalculateBounds();
                    AssetDatabase.AddObjectToAsset(mesh, bake);
                    var go = new GameObject(mesh.name);
                    go.layer = f.gameObject.layer; go.transform.SetParent(temp.transform, false); go.transform.localPosition = b.Origin;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = b.Material;
                    renderer.shadowCastingMode = f.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    // Chunked meshes already batch the generated modules. No runtime StaticBatchingUtility allocation.
                }
                CreateMarkers(f, temp.transform);
                EditorUtility.SetDirty(bake); AssetDatabase.SaveAssets();

                // Replace only our generated branch, after a successful build. UserContent stays untouched.
                Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Rebuild CatCooks floor");
                temp.SetActive(true);
                Undo.RegisterCreatedObjectUndo(temp, "Create floor geometry");
                Undo.RegisterCompleteObjectUndo(f, "Update floor data");
                if (f.GeneratedRoot != null)
                {
                    if (f.GeneratedRoot.transform.parent != f.transform) throw new InvalidOperationException("Generated Root не принадлежит этому корню.");
                    Undo.DestroyObjectImmediate(f.GeneratedRoot);
                }
                f.GeneratedRoot = temp; f.Bake = bake; f.Pieces = pieces;
                f.LastBuildSummary = $"{f.Layout.Title} • {batches.Count} мешей + {customCount} своих модулей • {colliderCount} BoxCollider • {pieces.Count} записей";
                temp.SetActive(true);
                EditorUtility.SetDirty(f); EditorSceneManager.MarkSceneDirty(f.gameObject.scene);
                Undo.CollapseUndoOperations(group); committed = true;
                Selection.activeGameObject = f.gameObject;
            }
            finally
            {
                if (!committed)
                {
                    if (temp != null) UnityEngine.Object.DestroyImmediate(temp);
                    AssetDatabase.DeleteAsset(path);
                }
            }
        }

        static int Chunk(float position, float step, int cells) => Mathf.Clamp(Mathf.FloorToInt(position / (step * 4)), 0, (cells - 1) / 4);

        public static List<CCFloorPiece> ColliderPieces(CCModularFloor floor, List<CCFloorPiece> pieces)
        {
            var result = new List<CCFloorPiece>();
            var columns = new Dictionary<Vector2, CCFloorPiece>();
            foreach (var p in pieces)
            {
                if (p.State != CCFloorState.Built || !p.Collider || (!floor.CollideWithBeams && p.Kind == CCFloorKind.Beam)) continue;
                if (!floor.MergeColumnColliders || p.Kind != CCFloorKind.Column) { result.Add(p); continue; }
                var key = new Vector2(p.Position.x, p.Position.z);
                if (!columns.TryGetValue(key, out CCFloorPiece merged))
                {
                    merged = new CCFloorPiece { Id = p.Id + "/mergedCollision", Kind = p.Kind, State = p.State, Level = p.Level,
                        Position = p.Position, Size = p.Size, Rotation = p.Rotation, Render = false };
                    columns.Add(key, merged); result.Add(merged);
                }
                else
                {
                    float bottom = Mathf.Min(merged.Position.y - merged.Size.y * .5f, p.Position.y - p.Size.y * .5f);
                    float top = Mathf.Max(merged.Position.y + merged.Size.y * .5f, p.Position.y + p.Size.y * .5f);
                    merged.Position.y = (top + bottom) * .5f; merged.Size.y = top - bottom;
                }
            }
            return result;
        }

        static void CheckVisual(CCFloorVisual visual)
        {
            if (visual == null || visual.Prefab == null) return;
            if (!EditorUtility.IsPersistent(visual.Prefab)) throw new InvalidOperationException("В Palette нужен prefab/model из Project, не объект сцены.");
            if (visual.Prefab.GetComponentInChildren<LODGroup>(true) || visual.Prefab.GetComponentInChildren<SkinnedMeshRenderer>(true))
                throw new InvalidOperationException("Свой модуль должен быть статическим без LODGroup/SkinnedMeshRenderer. Выдели один LOD в отдельный prefab.");
            if (visual.Prefab.GetComponentsInChildren<MeshFilter>(true).Length == 0) throw new InvalidOperationException("У своего модуля нет MeshFilter.");
        }
        static void BuildVisual(CCFloorVisual visual, CCFloorPiece p, Transform parent, int layer)
        {
            var container = new GameObject(p.Id); container.layer = layer; container.transform.SetParent(parent, false);
            container.transform.localPosition = p.Position; container.transform.localRotation = p.Rotation;
            var fit = new GameObject("Fit").transform; fit.SetParent(container.transform, false);
            var correction = new GameObject("Orientation").transform; correction.SetParent(fit, false);
            correction.localRotation = Quaternion.Euler(visual.RotationCorrection);
            CopyVisual(visual.Prefab.transform, correction, true, layer);
            bool found = false; Bounds bounds = new Bounds();
            foreach (var filter in container.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var b = filter.sharedMesh.bounds;
                Matrix4x4 m = container.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (var corner in Corners)
                {
                    Vector3 v = m.MultiplyPoint3x4(b.center + Vector3.Scale(corner * 2, b.extents));
                    if (!found) { bounds = new Bounds(v, Vector3.zero); found = true; } else bounds.Encapsulate(v);
                }
            }
            if (!found || bounds.size.x < .0001f || bounds.size.y < .0001f || bounds.size.z < .0001f)
                throw new InvalidOperationException("Свой модуль должен иметь объёмный меш: " + visual.Prefab.name);
            fit.localScale = new Vector3(p.Size.x / bounds.size.x, p.Size.y / bounds.size.y, p.Size.z / bounds.size.z);
            fit.localPosition = -Vector3.Scale(bounds.center, fit.localScale);
        }
        static void CopyVisual(Transform source, Transform parent, bool root, int layer)
        {
            if (!source.gameObject.activeSelf) return;
            var go = new GameObject(source.name); go.layer = layer; go.transform.SetParent(parent, false);
            go.transform.localPosition = root ? Vector3.zero : source.localPosition;
            go.transform.localRotation = source.localRotation; go.transform.localScale = source.localScale;
            var filter = source.GetComponent<MeshFilter>(); var renderer = source.GetComponent<MeshRenderer>();
            if (filter && renderer && renderer.enabled && filter.sharedMesh)
            {
                go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var copy = go.AddComponent<MeshRenderer>(); copy.sharedMaterials = renderer.sharedMaterials;
                copy.shadowCastingMode = renderer.shadowCastingMode; copy.receiveShadows = renderer.receiveShadows;
            }
            foreach (Transform child in source) CopyVisual(child, go.transform, false, layer);
        }
        static void AddProfile(Batch b, CCFloorPiece p, Rect uv)
        {
            for (int part = -1; part <= 1; part++)
            {
                var q = new CCFloorPiece { Position = p.Position, Rotation = p.Rotation, Size = p.Size };
                if (part == 0) q.Size = new Vector3(p.Size.x, p.Size.y * .6f, p.Size.z * .24f);
                else { q.Size.y = p.Size.y * .2f; q.Position += p.Rotation * Vector3.up * (part * p.Size.y * .4f); }
                AddBox(b, q, uv);
            }
        }
        static void AddBox(Batch b, CCFloorPiece p, Rect uv)
        {
            for (int face = 0; face < 6; face++)
            {
                int start = b.Vertices.Count;
                Vector3 v0 = Vector3.Scale(Corners[Faces[face, 0]], p.Size);
                Vector3 v1 = Vector3.Scale(Corners[Faces[face, 1]], p.Size);
                Vector3 v2 = Vector3.Scale(Corners[Faces[face, 2]], p.Size);
                Vector3 normal = p.Rotation * Vector3.Cross(v1 - v0, v2 - v0).normalized;
                for (int i = 0; i < 4; i++)
                {
                    b.Vertices.Add(p.Position - b.Origin + p.Rotation * Vector3.Scale(Corners[Faces[face, i]], p.Size));
                    b.Normals.Add(normal); b.UV.Add(new Vector2(i == 1 || i == 2 ? uv.xMax : uv.xMin, i >= 2 ? uv.yMax : uv.yMin));
                }
                b.Triangles.Add(start); b.Triangles.Add(start + 1); b.Triangles.Add(start + 2);
                b.Triangles.Add(start); b.Triangles.Add(start + 2); b.Triangles.Add(start + 3);
            }
        }
        static void CreateMarkers(CCModularFloor f, Transform parent)
        {
            var markers = new GameObject("ReferenceMarkers").transform; markers.SetParent(parent, false);
            Marker("PlayerArea", f.Layout.PlayerCell, f, markers);
            Marker("Lift_ReferenceOnly", f.Layout.LiftCell, f, markers);
            Marker("Crane_A_ReferenceOnly", new Vector2Int(-1, 0), f, markers);
            Marker("Crane_B_ReferenceOnly", new Vector2Int(f.Layout.Width, f.Layout.Depth - 1), f, markers);
        }
        static void Marker(string name, Vector2Int cell, CCModularFloor f, Transform parent)
        {
            var marker = new GameObject(name); marker.transform.SetParent(parent, false);
            marker.transform.localPosition = new Vector3((cell.x + .5f) * f.CellSize, 0, (cell.y + .5f) * f.StepZ);
        }
    }
}
