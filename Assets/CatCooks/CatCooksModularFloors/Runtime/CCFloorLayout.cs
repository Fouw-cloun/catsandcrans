using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Floors
{
    public enum CCFloorState { Planned = 0, Built = 1, Missing = 2, Reserved = 3 }
    public enum CCFloorKind { Slab, Column, Beam, Rail, Stair, Debris, Wall, Window, Detail }

    [Serializable]
    public sealed class CCFloorCell
    {
        public int X, Z, Level;
        public CCFloorState State = CCFloorState.Built;
    }

    // Straight stairs rise along +Z. The destination is cell (X, Z + Length, FromLevel + 1).
    [Serializable]
    public sealed class CCFloorStair
    {
        public int X, Z, FromLevel;
        [Min(1)] public int Length = 2;
    }

    [CreateAssetMenu(menuName = "CatCooks/Floors/Layout")]
    public sealed class CCFloorLayout : ScriptableObject
    {
        public string Title;
        [TextArea] public string Description;
        [Range(4, 16)] public int Width = 8, Depth = 8;
        [Range(1, 4)] public int DeckLevels = 3;
        public List<CCFloorCell> Cells = new List<CCFloorCell>();
        public List<CCFloorStair> Stairs = new List<CCFloorStair>();
        public Vector2Int PlayerCell = new Vector2Int(6, 0);
        public Vector2Int LiftCell = new Vector2Int(4, 7);

        public CCFloorState Get(int x, int z, int level)
        {
            for (int i = 0; i < Cells.Count; i++)
            {
                var c = Cells[i];
                if (c.X == x && c.Z == z && c.Level == level) return c.State;
            }
            return CCFloorState.Planned;
        }
        public bool Built(int x, int z, int level)
        {
            return x >= 0 && z >= 0 && x < Width && z < Depth &&
                level >= 0 && level < DeckLevels && Get(x, z, level) == CCFloorState.Built;
        }
        public void Set(int x, int z, int level, CCFloorState state)
        {
            foreach (var c in Cells)
                if (c.X == x && c.Z == z && c.Level == level) { c.State = state; return; }
            Cells.Add(new CCFloorCell { X = x, Z = z, Level = level, State = state });
        }
    }

    [Serializable]
    public sealed class CCFloorPiece
    {
        public string Id;
        public CCFloorKind Kind;
        public CCFloorState State;
        public int Level;
        public bool Render = true, Collider = true;
        public Vector3 Position, Size;
        public Quaternion Rotation = Quaternion.identity;
    }
}
