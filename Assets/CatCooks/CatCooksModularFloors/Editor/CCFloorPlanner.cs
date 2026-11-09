using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Floors.Editor
{
    public static class CCFloorPlanner
    {
        public static List<string> Validate(CCModularFloor floor)
        {
            var errors = new List<string>();
            var a = floor.Layout;
            if (a == null) { errors.Add("Выбери Layout."); return errors; }
            if (a.Width < 4 || a.Width > 16 || a.Depth < 4 || a.Depth > 16 || a.DeckLevels < 1 || a.DeckLevels > 4)
                errors.Add("Размер сетки: 4–16, уровней настила: 1–4.");
            if (floor.CellSize < 3 || floor.StepZ < 3 || floor.StoreyHeight < 2.5f || floor.SlabThickness <= 0 || floor.ColumnWidth <= 0)
                errors.Add("Недопустимые размеры модулей.");
            if (floor.FrameStoreys < a.DeckLevels || floor.FrameStoreys > 5)
                errors.Add("Frame Storeys должен быть не меньше Deck Levels и не больше 5.");
            if ((floor.transform.lossyScale - Vector3.one).sqrMagnitude > .0001f)
                errors.Add("Масштаб корня и его родителей должен быть (1,1,1). Меняй Cell Size / Storey Height.");
            if (Vector3.Dot(floor.transform.up, Vector3.up) < .9999f)
                errors.Add("Корень можно вращать только вокруг Y.");
            if (floor.Palette == null || !floor.Palette.Concrete || !floor.Palette.Steel || !floor.Palette.Safety || !floor.Palette.Rubble)
                errors.Add("Нужна Palette с четырьмя материалами.");
            var seen = new HashSet<Vector3Int>();
            foreach (var c in a.Cells)
            {
                var k = new Vector3Int(c.X, c.Level, c.Z);
                if (!seen.Add(k)) errors.Add("Повтор клетки: " + k);
                if (c.X < 0 || c.X >= a.Width || c.Z < 0 || c.Z >= a.Depth || c.Level < 0 || c.Level >= a.DeckLevels)
                    errors.Add("Клетка за пределами сетки: " + k);
            }
            if (!a.Built(a.PlayerCell.x, a.PlayerCell.y, 0)) errors.Add("Player Cell должен стоять на готовой нижней плите.");
            if (!a.Built(a.LiftCell.x, a.LiftCell.y, 0)) errors.Add("Lift Cell должен стоять на готовой нижней плите.");
            var stairCells = new HashSet<Vector3Int>();
            foreach (var s in a.Stairs)
            {
                if (s.Length < 1 || s.FromLevel < 0 || s.FromLevel + 1 >= a.DeckLevels || s.Z < 1 ||
                    s.X < 0 || s.X >= a.Width || s.Z + s.Length >= a.Depth)
                { errors.Add("Лестница за пределами сетки или без площадок."); continue; }
                if (Mathf.Atan2(floor.StoreyHeight, s.Length * floor.StepZ) * Mathf.Rad2Deg > 35)
                    errors.Add("Лестница круче 35 градусов. Увеличь Length или Cell Size.");
                if (!a.Built(s.X, s.Z - 1, s.FromLevel) || !a.Built(s.X, s.Z + s.Length, s.FromLevel + 1))
                    errors.Add("У лестницы нет нижней или верхней площадки.");
                for (int z = s.Z; z < s.Z + s.Length; z++)
                {
                    if (!a.Built(s.X, z, s.FromLevel)) errors.Add("Под лестницей должна быть готовая нижняя плита.");
                    if (a.Get(s.X, z, s.FromLevel + 1) != CCFloorState.Reserved)
                        errors.Add("Над лестницей поставь Reserved: проём нельзя заполнять плитой.");
                    if (!stairCells.Add(new Vector3Int(s.X, s.FromLevel, z))) errors.Add("Лестницы пересекаются.");
                }
            }
            if (errors.Count > 0) return errors;
            // Walkability graph: deck neighbours plus explicit stair connections.
            var visited = new HashSet<Vector3Int>();
            var queue = new Queue<Vector3Int>();
            var start = new Vector3Int(a.PlayerCell.x, 0, a.PlayerCell.y);
            queue.Enqueue(start); visited.Add(start);
            var dirs = new[] { Vector3Int.right, Vector3Int.left, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var d in dirs)
                {
                    var q = p + d;
                    if (a.Built(q.x, q.z, q.y) && visited.Add(q)) queue.Enqueue(q);
                }
                foreach (var s in a.Stairs)
                {
                    var lo = new Vector3Int(s.X, s.FromLevel, s.Z - 1);
                    var hi = new Vector3Int(s.X, s.FromLevel + 1, s.Z + s.Length);
                    if (p == lo && visited.Add(hi)) queue.Enqueue(hi);
                    if (p == hi && visited.Add(lo)) queue.Enqueue(lo);
                }
            }
            int unreachable = 0;
            foreach (var c in a.Cells)
                if (c.State == CCFloorState.Built && !visited.Contains(new Vector3Int(c.X, c.Level, c.Z))) unreachable++;
            if (unreachable > 0) errors.Add("Недоступных готовых плит: " + unreachable + ". Добавь соединение/лестницу.");
            return errors;
        }

        public static List<CCFloorPiece> Create(CCModularFloor f)
        {
            var a = f.Layout;
            var pieces = new List<CCFloorPiece>();
            float c = f.CellSize, dz = f.StepZ, h = f.StoreyHeight, t = f.SlabThickness, w = f.ColumnWidth;
            Action<string, CCFloorKind, CCFloorState, int, Vector3, Vector3, Quaternion, bool, bool> add =
                (id, kind, state, level, pos, size, rot, render, collider) => pieces.Add(new CCFloorPiece
                { Id = id, Kind = kind, State = state, Level = level, Position = pos, Size = size, Rotation = rot, Render = render, Collider = collider });
            var identity = Quaternion.identity;
            for (int l = 0; l < a.DeckLevels; l++)
                for (int z = 0; z < a.Depth; z++)
                    for (int x = 0; x < a.Width; x++)
                    {
                        var state = a.Get(x, z, l);
                        add($"slab/{l}/{x}/{z}", CCFloorKind.Slab, state, l,
                            new Vector3((x + .5f) * c, l * h - t * .5f, (z + .5f) * dz), new Vector3(c, t, dz), identity, true, true);
                        if (state == CCFloorState.Missing)
                        {
                            // Static visual remnants below the gap. Never an invisible walking floor.
                            add($"debris/{l}/{x}/{z}", CCFloorKind.Debris, CCFloorState.Built, l,
                                new Vector3((x + .35f) * c, l * h - 1.6f, (z + .4f) * dz), new Vector3(c * .38f, t, dz * .48f),
                                Quaternion.Euler(18, x * 17 + z * 23, -24), true, false);
                        }
                    }
            for (int l = 0; l < f.FrameStoreys; l++)
            {
                for (int z = 0; z <= a.Depth; z++)
                    for (int x = 0; x <= a.Width; x++)
                    {
                        bool outer = x == 0 || z == 0 || x == a.Width || z == a.Depth;
                        bool built = outer || SupportsAbove(a, x, z, l);
                        add($"column/{l}/{x}/{z}", CCFloorKind.Column, built ? CCFloorState.Built : CCFloorState.Planned, l,
                            new Vector3(x * c, (l + .5f) * h - t * .5f, z * dz), new Vector3(w, h - t, w), identity, true, true);
                    }
                for (int z = 0; z <= a.Depth; z++)
                    for (int x = 0; x < a.Width; x++)
                    {
                        bool built = z == 0 || z == a.Depth || DeckAboveEdge(a, x, z, l, false);
                        add($"beamX/{l}/{x}/{z}", CCFloorKind.Beam, built ? CCFloorState.Built : CCFloorState.Planned, l,
                            new Vector3((x + .5f) * c, (l + 1) * h - t - w * .5f, z * dz), new Vector3(c, w, w), identity, true, true);
                    }
                for (int z = 0; z < a.Depth; z++)
                    for (int x = 0; x <= a.Width; x++)
                    {
                        bool built = x == 0 || x == a.Width || DeckAboveEdge(a, x, z, l, true);
                        add($"beamZ/{l}/{x}/{z}", CCFloorKind.Beam, built ? CCFloorState.Built : CCFloorState.Planned, l,
                            new Vector3(x * c, (l + 1) * h - t - w * .5f, (z + .5f) * dz), new Vector3(dz, w, w), Quaternion.Euler(0, 90, 0), true, true);
                    }
                // Logical facade slots only: lower wall band + window frame above it.
                for (int side = 0; side < 4; side++)
                {
                    int count = side < 2 ? a.Width : a.Depth;
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 p = side < 2 ? new Vector3((i + .5f) * c, l * h, side == 0 ? 0 : a.Depth * dz) :
                            new Vector3(side == 2 ? 0 : a.Width * c, l * h, (i + .5f) * dz);
                        Quaternion r = side < 2 ? identity : Quaternion.Euler(0, 90, 0);
                        add($"wall/{l}/{side}/{i}", CCFloorKind.Wall, CCFloorState.Planned, l, p + Vector3.up * .5f,
                            new Vector3((side < 2 ? c : dz) - w, 1, .18f), r, true, true);
                        float windowHeight = Mathf.Max(.5f, h - 1 - t - w);
                        add($"window/{l}/{side}/{i}", CCFloorKind.Window, CCFloorState.Planned, l, p + Vector3.up * (1 + windowHeight * .5f),
                            new Vector3((side < 2 ? c : dz) - w, windowHeight, .12f), r, true, true);
                    }
                }
            }
            int stairIndex = 0;
            foreach (var s in a.Stairs)
            {
                string id = "stairs/" + stairIndex++;
                float length = s.Length * dz, width = Mathf.Min(2.4f, c * .6f);
                Vector3 lo = new Vector3((s.X + .5f) * c, s.FromLevel * h, s.Z * dz);
                Vector3 delta = new Vector3(0, h, length);
                Quaternion rot = Quaternion.FromToRotation(Vector3.forward, delta.normalized);
                add(id + "/collision", CCFloorKind.Stair, CCFloorState.Built, s.FromLevel,
                    lo + delta * .5f - rot * Vector3.up * .06f, new Vector3(width, .12f, delta.magnitude), rot, false, true);
                int steps = Mathf.CeilToInt(h / .18f);
                for (int i = 0; i < steps; i++)
                {
                    float rise = h * (i + 1) / steps;
                    add(id + "/step/" + i, CCFloorKind.Stair, CCFloorState.Built, s.FromLevel,
                        lo + new Vector3(0, rise - .06f, length * (i + .5f) / steps), new Vector3(width, .12f, length / steps), identity, true, false);
                }
                if (f.GuardRails)
                    for (int side = -1; side <= 1; side += 2)
                        add(id + "/handrail/" + side, CCFloorKind.Rail, CCFloorState.Built, s.FromLevel,
                            lo + delta * .5f + new Vector3(side * width * .5f, .95f, 0), new Vector3(.08f, .08f, delta.magnitude), rot, true, false);
                if (f.GuardRails)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        // Continuous side collider follows the slope; open at both ends.
                        add(id + "/sideCollision/" + side, CCFloorKind.Rail, CCFloorState.Built, s.FromLevel,
                            lo + delta * .5f + new Vector3(side * width * .5f, .48f, 0), new Vector3(.08f, .96f, delta.magnitude), rot, false, true);
                        for (int i = 0; i <= steps; i += 2)
                            add(id + "/post/" + side + "/" + i, CCFloorKind.Rail, CCFloorState.Built, s.FromLevel,
                                lo + delta * (i / (float)steps) + new Vector3(side * width * .5f, .475f, 0), new Vector3(.07f, .95f, .07f), identity, true, false);
                    }
            }
            if (f.GuardRails) AddDeckRails(f, pieces);
            if (f.StructuralDetails) AddStructure(f, pieces);
            return pieces;
        }

        static void AddStructure(CCModularFloor f, List<CCFloorPiece> pieces)
        {
            int initialCount = pieces.Count;
            for (int i = 0; i < initialCount; i++)
            {
                var p = pieces[i];
                if (p.Kind != CCFloorKind.Column || p.State != CCFloorState.Built) continue;
                float w = f.ColumnWidth;
                pieces.Add(new CCFloorPiece { Id = p.Id + "/cap", Kind = CCFloorKind.Detail,
                    State = CCFloorState.Built, Level = p.Level, Collider = false,
                    Position = p.Position + Vector3.up * (p.Size.y * .5f - .06f), Size = new Vector3(w * 1.6f, .12f, w * 1.6f) });
                if (p.Level == 0)
                    pieces.Add(new CCFloorPiece { Id = p.Id + "/foot", Kind = CCFloorKind.Detail,
                        State = CCFloorState.Built, Level = 0, Collider = false,
                        Position = new Vector3(p.Position.x, .05f, p.Position.z), Size = new Vector3(w * 1.9f, .1f, w * 1.9f) });
            }
            // Corner bays are braced; the central view remains open.
            for (int l = 0; l < f.FrameStoreys; l++)
                for (int side = 0; side < 4; side++)
                {
                    Vector3 start, end;
                    float y = l * f.StoreyHeight + .18f, top = (l + 1) * f.StoreyHeight - f.SlabThickness - .2f;
                    if (side < 2)
                    {
                        float z = side == 0 ? 0 : f.Layout.Depth * f.StepZ;
                        start = new Vector3(.18f, y, z); end = new Vector3(f.CellSize - .18f, top, z);
                    }
                    else
                    {
                        float x = side == 2 ? 0 : f.Layout.Width * f.CellSize;
                        start = new Vector3(x, y, (f.Layout.Depth - 1) * f.StepZ + .18f);
                        end = new Vector3(x, top, f.Layout.Depth * f.StepZ - .18f);
                    }
                    Vector3 delta = end - start;
                    pieces.Add(new CCFloorPiece { Id = $"brace/{l}/{side}", Kind = CCFloorKind.Detail,
                        State = CCFloorState.Built, Level = l, Collider = false, Position = (start + end) * .5f,
                        Size = new Vector3(delta.magnitude, .14f, .14f), Rotation = Quaternion.FromToRotation(Vector3.right, delta.normalized) });
                }
        }

        static bool SupportsAbove(CCFloorLayout a, int x, int z, int level)
        {
            for (int l = level + 1; l < a.DeckLevels; l++)
                if (a.Built(x, z, l) || a.Built(x - 1, z, l) || a.Built(x, z - 1, l) || a.Built(x - 1, z - 1, l)) return true;
            return false;
        }
        static bool DeckAboveEdge(CCFloorLayout a, int x, int z, int level, bool alongZ)
        {
            // Intermediate beams also brace columns that support a higher deck.
            for (int l = level + 1; l < a.DeckLevels; l++)
                if (a.Built(x, z, l) || (alongZ ? a.Built(x - 1, z, l) : a.Built(x, z - 1, l))) return true;
            return false;
        }
        static void AddDeckRails(CCModularFloor f, List<CCFloorPiece> pieces)
        {
            var a = f.Layout; float c = f.CellSize, dz = f.StepZ;
            var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
            foreach (var cell in a.Cells)
            {
                if (cell.State != CCFloorState.Built) continue;
                foreach (var d in dirs)
                {
                    if (a.Built(cell.X + d.x, cell.Z + d.y, cell.Level)) continue;
                    bool stairOpening = false;
                    foreach (var s in a.Stairs)
                        if (cell.X == s.X &&
                            ((cell.Level == s.FromLevel + 1 && cell.Z == s.Z + s.Length && d.y == -1) ||
                             (cell.Level == s.FromLevel && cell.Z == s.Z - 1 && d.y == 1))) stairOpening = true;
                    Vector3 center = new Vector3((cell.X + .5f + d.x * .5f) * c, cell.Level * f.StoreyHeight,
                        (cell.Z + .5f + d.y * .5f) * dz);
                    Quaternion rot = d.x == 0 ? Quaternion.identity : Quaternion.Euler(0, 90, 0);
                    string id = $"edge/{cell.Level}/{cell.X}/{cell.Z}/{d.x}/{d.y}";
                    if (stairOpening)
                    {
                        // Keep protection on both sides of the stair mouth, not across it.
                        float halfOpening = Mathf.Min(2.4f, c * .6f) * .5f + .06f;
                        float segment = c * .5f - halfOpening;
                        for (int side = -1; side <= 1; side += 2)
                            Rail(pieces, id + "/wing/" + side, cell.Level,
                                center + rot * Vector3.right * (side * (halfOpening + segment * .5f)), segment, rot);
                    }
                    else Rail(pieces, id, cell.Level, center, d.x == 0 ? c : dz, rot);
                }
            }
        }
        static void Rail(List<CCFloorPiece> p, string id, int l, Vector3 center, float length, Quaternion rotation)
        {
            p.Add(new CCFloorPiece { Id = id + "/collision", Kind = CCFloorKind.Rail, State = CCFloorState.Built, Level = l,
                Position = center + Vector3.up * .52f, Size = new Vector3(length, 1.04f, .09f), Rotation = rotation, Render = false });
            for (int i = 0; i < 2; i++)
                p.Add(new CCFloorPiece { Id = id + "/bar/" + i, Kind = CCFloorKind.Rail, State = CCFloorState.Built, Level = l,
                    Position = center + Vector3.up * (i == 0 ? .5f : 1), Size = new Vector3(length, .065f, .065f), Rotation = rotation, Collider = false });
            for (int i = -1; i <= 1; i += 2)
                p.Add(new CCFloorPiece { Id = id + "/post/" + i, Kind = CCFloorKind.Rail, State = CCFloorState.Built, Level = l,
                    Position = center + rotation * new Vector3(i * (length * .5f - .04f), .52f, 0), Size = new Vector3(.07f, 1.04f, .07f), Rotation = rotation, Collider = false });
        }
    }
}
