using System.Collections.Generic;
using CatCooks.Level;
using FishNet.Object;
using UnityEngine;

namespace CatCooks.SiteTools
{
    [DisallowMultipleComponent]
    public sealed class CCColdPipeValve : MonoBehaviour, IVehiclePart
    {
        public HydrantNut nut;
        public CCPipeFluid fluid;
        public Vector3Int gridCell;
        [Min(.5f)] public float travelSeconds = 5f;
        public PlayerInteractor Operator { get; private set; }
        public NetworkObject OwningNetworkObject => nut.NetworkObject;
        public int PartId => 763;

        public void ServerUse(PlayerInteractor player)
        {
            if (nut == null || !nut.IsServerInitialized || player == null || Operator != null) return;
            if (player.ServerFocusPipeValve(this)) Operator = player;
        }

        public void Release(PlayerInteractor player)
        {
            if (Operator == player) Operator = null;
        }

        public static List<CCColdPipeValve> ConnectedTo(CCInspectionHatch hatch)
        {
            var valves = new List<CCColdPipeValve>();
            var run = hatch.GetComponentInParent<CCPipeRun>();
            if (run != null){
                foreach (var valve in run.GetComponentsInChildren<CCColdPipeValve>(true))
                    if (valve.fluid == hatch.fluid && valve.GetComponentInParent<CCPipeRun>() == run) valves.Add(valve);
                return valves;
            }
            var house = hatch.GetComponentInParent<CityModuleHouse>();
            if (house == null) return valves;
            var visited = new HashSet<Vector3Int>();
            var queue = new Queue<Vector3Int>();
            visited.Add(hatch.gridCell);
            queue.Enqueue(hatch.gridCell);
            var directions = new[]{Vector3Int.right,Vector3Int.left,Vector3Int.forward,Vector3Int.back,Vector3Int.up,Vector3Int.down};
            while (queue.Count > 0){
                var pos = queue.Dequeue();
                var cell = house.Find(pos.x,pos.y,pos.z);
                if (cell == null || !cell.HasPipes) continue;
                foreach (var dir in directions){
                    var next = pos + dir;
                    if (visited.Contains(next)) continue;
                    var neighbour = house.Find(next.x,next.y,next.z);
                    if (neighbour == null || !neighbour.HasPipes) continue;
                    if (dir.y != 0 && !(cell.VerticalPipe && neighbour.VerticalPipe)) continue;
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }
            foreach (var valve in house.GetComponentsInChildren<CCColdPipeValve>(true))
                if (valve.fluid == hatch.fluid && valve.GetComponentInParent<CCPipeRun>() == null && valve.GetComponentInParent<CityModuleHouse>() == house &&
                    visited.Contains(valve.gridCell)) valves.Add(valve);
            return valves;
        }
    }
}
