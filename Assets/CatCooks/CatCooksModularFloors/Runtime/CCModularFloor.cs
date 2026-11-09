using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Floors
{
    [DisallowMultipleComponent]
    public sealed class CCModularFloor : MonoBehaviour
    {
        public CCFloorLayout Layout;
        public CCFloorPalette Palette;
        [Min(3)] public float CellSize = 6;
        [Tooltip("0 uses Cell Size, preserving v1 layouts.")] [Min(0)] public float CellDepth;
        public float StepZ => CellDepth > 0 ? CellDepth : CellSize;
        public bool StructuralDetails;
        public bool ProfileBeams;
        public bool CastShadows = true;
        public bool MergeColumnColliders;
        public bool CollideWithBeams = true;
        [Min(2.5f)] public float StoreyHeight = 4;
        [Range(1, 5)] public int FrameStoreys = 3;
        [Range(.1f, .6f)] public float SlabThickness = .24f;
        [Range(.15f, .6f)] public float ColumnWidth = .32f;
        public bool GuardRails = true;
        public bool ShowPlanned = false;
        public bool ShowLabels = true;
        [HideInInspector] public GameObject GeneratedRoot;
        [HideInInspector] public CCFloorBake Bake;
        [HideInInspector] public List<CCFloorPiece> Pieces = new List<CCFloorPiece>();
        [HideInInspector] public string LastBuildSummary;
        // Data only. No Update, physics simulation, AI or network traffic.
    }
}
