using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.Floors
{
    [Serializable]
    public sealed class CCSiteSocket
    {
        public string Id;
        public Vector3 LocalPosition;
        public Vector3 Forward = Vector3.forward;
    }

    [DisallowMultipleComponent]
    public sealed class CCConstructionSite : MonoBehaviour
    {
        [Header("Размеры в метрах, независимо по двум осям")]
        [Min(32)] public float Width = 104;
        [Min(32)] public float Depth = 84;
        [Header("Двор, первый этаж, визуальная середина")]
        [Min(4)] public float GroundFloorHeight = 6;
        [Range(0, 30)] public int EmptyFloors = 9;
        [Min(2.8f)] public float EmptyFloorHeight = 4.2f;
        [Min(12)] public float YardDepth = 30;
        [Min(3)] public float YardSideMargin = 10;
        [Min(8)] public float DrivewayLength = 20;
        [Header("Верхняя рабочая площадка")]
        public CCModularFloor Workdeck;
        public CCFloorLayout TopLayout;
        public bool FitStairsToScale = true;
        [Tooltip("Optional persistent palette supplying only custom slab/column/beam prefabs.")]
        public CCFloorPalette TopModuleOverrides;
        [Range(3, 5)] public int TopFrameStoreys = 4;
        [Min(3)] public float TopStoreyHeight = 5.5f;
        [Range(.25f, .85f)] public float TopColumnWidth = .65f;
        public bool ProfileBeams = true;
        public bool StructuralDetails = true;
        [Header("Графика")]
        [Tooltip("1024 recommended; 2048 for closer facade shots.")]
        public int AtlasResolution = 1024;
        public bool CastWorkdeckShadows;
        public bool BeamCollisions;
        public bool FacadeTrims = true;
        [Range(.02f, .4f)] public float TrimCullScreenHeight = .12f;
        [HideInInspector] public GameObject GeneratedBuilding;
        [HideInInspector] public Transform GroundUserContent;
        [HideInInspector] public CCFloorBake BuildingBake;
        [HideInInspector] public List<CCSiteSocket> Sockets = new List<CCSiteSocket>();
        [HideInInspector] public string LastBuildSummary;
        public float WorkdeckHeight => GroundFloorHeight + EmptyFloors * EmptyFloorHeight;

        public bool TryGetSocket(string id, out Vector3 worldPosition)
        {
            foreach (var socket in Sockets)
                if (socket.Id == id) { worldPosition = transform.TransformPoint(socket.LocalPosition); return true; }
            worldPosition = transform.position; return false;
        }
        // Static authoring component. No Update, polling, network messages or runtime mesh generation.
    }
}
