using UnityEngine;
namespace CatCooks.CraneLogistics
{
    [DisallowMultipleComponent]
    public sealed class CCZoneCraneRoute:MonoBehaviour
    {
        public CCSupplyCrane Crane;
        public CCWorkZone Source,Destination;
        [Tooltip("When non-empty, replaces Source for dispatch. Only these supplies belong to this route.")]
        public CCWorkZone[] Sources=new CCWorkZone[0];
        [Tooltip("When non-empty, replaces Destination. Lowest stock + incoming is served first.")]
        public CCWorkZone[] Destinations=new CCWorkZone[0];
        [Tooltip("Allow a one-unit trip when only one unit is available or fits.")]
        public bool AllowPartialLoad=true;
        public int SourceCount=>Sources!=null&&Sources.Length>0?Sources.Length:1;
        public int DestinationCount=>Destinations!=null&&Destinations.Length>0?Destinations.Length:1;
        public CCWorkZone SourceAt(int i)=>Sources!=null&&Sources.Length>0?Sources[i]:Source;
        public CCWorkZone DestinationAt(int i)=>Destinations!=null&&Destinations.Length>0?Destinations[i]:Destination;
        [Range(1,2)] public int UnitsPerTrip=2;
        [Min(0)] public float Cooldown=3;
        [Tooltip("Same non-empty group = one in-flight crane at a time in this territory.")]
        public string TrafficGroup="";
        [Tooltip("Only explicit crew requests; one combined route per crane is recommended.")]
        public bool DemandDriven=false;
        [System.NonSerialized] public int LastDemandCategory=-1;
        [System.NonSerialized] public CCDirectMountJob ActiveMount;
        public bool Enabled=true;
        [HideInInspector] public string Status="Not started";
        [System.NonSerialized] public bool InFlight;
        [System.NonSerialized] public bool SourcePicked;
        [System.NonSerialized] public bool SourceVisualTransferred;
        [System.NonSerialized] public string ActiveTrafficGroup;
        [System.NonSerialized] public int ReservedUnits;
        [System.NonSerialized] public uint CompletionBefore;
        [System.NonSerialized] public float NextTime;
        // Capture references at dispatch: editing a route during a trip cannot redirect its stock.
        [System.NonSerialized] public CCSupplyCrane ActiveCrane;
        [System.NonSerialized] public CCWorkZone ActiveSource,ActiveDestination;
        public CCWorkTerritory Owner=>GetComponentInParent<CCWorkTerritory>();
        public bool Resolve(out Vector3 a,out Vector3 b,out string error)
            =>ResolvePair(Source,Destination,out a,out b,out error);
        public bool ResolvePair(CCWorkZone source,CCWorkZone destination,out Vector3 a,out Vector3 b,out string error)
        {
            a=b=Vector3.zero;error="";CCWorkTerritory owner=Owner;
            if(owner==null||Crane==null||source==null||destination==null) error="Assign territory, crane and both zones.";
            else if(Crane.Territory!=owner||source.Owner!=owner||destination.Owner!=owner) error="Crane and both zones must belong to THIS territory.";
            else if(owner.transform.lossyScale!=Vector3.one||source.transform.lossyScale!=Vector3.one||destination.transform.lossyScale!=Vector3.one||Vector3.Dot(owner.transform.up,Vector3.up)<.999f||Vector3.Dot(source.transform.up,Vector3.up)<.999f||Vector3.Dot(destination.transform.up,Vector3.up)<.999f) error="Territory and zones must be upright with scale 1,1,1.";
            else if(source==destination||source.Kind!=CCZoneKind.Supply||!destination.AcceptsDelivery) error="Route must be Supply -> Receiving or Work.";
            else if(source.Resource==CCZoneResource.SteelColumn||source.Resource==CCZoneResource.SteelBeam) error="Steel uses direct installation requests, not Receiving zones.";
            else if(source.Resource!=destination.Resource) error="Resource types differ.";
            else if(!source.isActiveAndEnabled||!destination.isActiveAndEnabled) error="Zone is disabled.";
            else if(!source.Contains(source.DockPosition)||!destination.Contains(destination.DockPosition)||!source.Contains(source.StagingPosition)||!destination.Contains(destination.StagingPosition)) error="Dock and staging must be inside their zones (check height).";
            else if(Mathf.Abs(source.DockPosition.y-source.StagingPosition.y)>.25f||Mathf.Abs(destination.DockPosition.y-destination.StagingPosition.y)>.25f) error="Dock and staging must be on the same floor (height difference <= 0.25 m).";
            else if(!Crane.TrySolveDock(source.DockPosition,Crane.DockA,out a)||!Crane.TrySolveDock(destination.DockPosition,Crane.DockB,out b)) error="Dock unreachable: reposition zone/crane or adjust limits.";
            else if(Crane.TravelRope>Mathf.Min(a.z,b.z)||Crane.TravelRope<Crane.RopeRange.x) error="Invalid travel rope for these zones.";
            else if(owner.Excluded(source.DockPosition,Crane.LoadSize((int)source.Resource),Crane.LoadHeight((int)source.Resource))||owner.Excluded(destination.DockPosition,Crane.LoadSize((int)source.Resource),Crane.LoadHeight((int)source.Resource))) error="Dock intersects an exclusion zone.";
            return error.Length==0;
        }
    }
}
