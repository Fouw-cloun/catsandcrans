using UnityEngine;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
namespace CatCooks.CraneLogistics
{
    [DisallowMultipleComponent,RequireComponent(typeof(NetworkObject))]
    public sealed class CCWorkTerritory:NetworkBehaviour
    {
        public string TerritoryName="New floor";
        [Tooltip("Stops new assignments. Existing trips finish.")]
        public bool Dispatch=true;
        [Min(.1f)] public float DispatchInterval=.5f;
        [SerializeField] CCWorkZone[] _zones=new CCWorkZone[0];
        [SerializeField] CCZoneCraneRoute[] _routes=new CCZoneCraneRoute[0];
        public CCWorkZone[] Zones=>_zones;
        public CCZoneCraneRoute[] Routes=>_routes;
        public struct StockState { public int[] Stock, Incoming, DisplayStock; }
        readonly SyncVar<StockState> _stock=new SyncVar<StockState>(new SyncTypeSettings(.25f));
        int[] _counts,_incoming;
        float[] _refillAt;
        float _next;
        int _cursor;
        public uint DebugDispatchTicks { get; private set; }
        public float DebugLastDispatchAt { get; private set; }
        public bool DebugStartServerSeen { get; private set; }
        bool _dirty;
        sealed class Demand
        {
            public Component Owner; public CCZoneCraneRoute Route; public CCWorkZone Zone;
            public int Target; public ulong Ticket;
        }
        readonly List<Demand> _demands=new List<Demand>();
        ulong _nextTicket;
        uint _mountToken;
        readonly List<CCDirectMountJob> _mounts=new List<CCDirectMountJob>();
        public CCDirectMountJob ServerRequestMount(Component owner,CCZoneCraneRoute route,int step,CCZoneResource resource,Vector3 top,Quaternion rotation,Vector3 size)
        {
            if(NetworkObject==null||!IsServerInitialized||owner==null||route==null||route.Owner!=this||!route.DemandDriven)return null;
            if(resource!=CCZoneResource.SteelColumn&&resource!=CCZoneResource.SteelBeam)return null;
            for(int i=_mounts.Count-1;i>=0;i--)
            {
                var old=_mounts[i];if(old.Owner!=owner)continue;
                if(old.Step==step&&!old.Cancelled)return old;
                if(old.Assigned&&!old.Finished&&!old.Cancelled)return null;
                _mounts.RemoveAt(i);
            }
            if(size.x<=0||size.y<=0||size.z<=0)return null;
            if(++_mountToken==0)++_mountToken;
            var job=new CCDirectMountJob{Owner=owner,Route=route,Step=step,Resource=resource,Top=top,Rotation=rotation,Size=size,Token=_mountToken,Ticket=++_nextTicket};
            _mounts.Add(job);return job;
        }
        // Only the requesting crew can commit this exact job after both workers finish bolts.
        public bool ServerFastenMount(Component owner,CCDirectMountJob job)
        {
            if(!IsServerInitialized||job==null||owner==null||job.Owner!=owner||!job.Holding||!owner.gameObject.activeInHierarchy)return false;
            if(owner is Behaviour behaviour&&!behaviour.enabled)return false;
            if(!job.Route.ActiveCrane.ServerReleaseMount(this,job.Token))return false;
            job.Fastened=true;job.Status="Bolts secured; unhooking";return true;
        }
        public bool ResolveMount(CCZoneCraneRoute route,CCWorkZone source,CCDirectMountJob job,out Vector3 from,out Vector3 to,out string error)
        {
            from=to=Vector3.zero;error="";
            if(route==null||route.Owner!=this||route.Crane==null||route.Crane.Territory!=this||source==null||source.Owner!=this||job==null){error="Mount owner/source/crane missing";return false;}
            if(source.Kind!=CCZoneKind.Supply||source.Resource!=job.Resource||!source.isActiveAndEnabled){error="Mount source type/resource inactive or wrong";return false;}
            if(source.transform.lossyScale!=Vector3.one||Vector3.Dot(source.transform.up,Vector3.up)<.999f||!source.Contains(source.DockPosition)){error="Mount Supply: upright scale 1 and Dock inside zone required";return false;}
            Vector3 pickup=source.DockPosition+Vector3.up*job.Size.y;
            var c=route.Crane;
            // Evaluate BOTH points. A combined short-circuit error hid which end needed attention.
            bool pickupOK=c.TrySolveDock(pickup,c.DockA,out from);
            bool installationOK=c.TrySolveDock(job.Top,c.DockB,out to);
            if(!pickupOK||!installationOK)
            {
                error="DIRECT reach: "+(pickupOK?"PICKUP OK":MountReachFailure("PICKUP / lower Supply Dock: "+source.name,c,pickup,from));
                error+=" | "+(installationOK?"INSTALL OK":MountReachFailure("INSTALL / crew step "+(job.Step+1)+" "+(job.Owner!=null?job.Owner.name:"missing crew"),c,job.Top,to));
                return false;
            }
            if(c.TravelRope<c.RopeRange.x||c.TravelRope>Mathf.Min(from.z,to.z)){error="Travel rope invalid for direct mount";return false;}
            Vector2 footprint=CCSupplyCrane.WorldFootprint(job.Size,job.Rotation);
            if(Excluded(pickup-Vector3.up*job.Size.y,footprint,job.Size.y)||Excluded(job.Top-Vector3.up*job.Size.y,footprint,job.Size.y)){error="Direct load intersects exclusion zone";return false;}
            return true;
        }
        static string MountReachFailure(string label,CCSupplyCrane crane,Vector3 target,Vector3 pose)
        {
            if(crane.Tower==null||crane.Trolley==null||crane.RopeTop==null)return label+": missing Tower/Trolley/RopeTop";
            Vector3 calculated=crane.DeckAt(pose);
            float horizontal=Vector2.Distance(new Vector2(calculated.x,calculated.z),new Vector2(target.x,target.z));
            string reason="solver did not converge within 0.08 m; check geometry and target";
            if(pose.z<crane.RopeRange.x)reason="required rope is BELOW minimum (target too high for this solved pose)";
            else if(pose.z>crane.RopeRange.y)reason="required rope is ABOVE maximum (target too low for this solved pose)";
            else if(horizontal>=.08f&&Mathf.Abs(pose.y-crane.TrolleyRange.x)<.02f)reason="solver stopped at TROLLEY MIN; inspect target relative to jib travel";
            else if(horizontal>=.08f&&Mathf.Abs(pose.y-crane.TrolleyRange.y)<.02f)reason="solver stopped at TROLLEY MAX; inspect target relative to jib travel";
            return label+" FAILED: "+reason+"; requested top="+target.ToString("F3")+
                "; calculated top="+calculated.ToString("F3")+"; horizontal miss="+horizontal.ToString("F3")+" m"+
                "; solved yaw/trolley/rope="+pose.ToString("F3")+"; trolley range="+crane.TrolleyRange.ToString("F3")+
                "; rope range="+crane.RopeRange.ToString("F3");
        }
        bool TryStartMount(CCZoneCraneRoute route)
        {
            CCDirectMountJob selected=null;CCWorkZone supply=null;Vector3 a=default,b=default;
            float nearest=float.PositiveInfinity;
            foreach(var job in _mounts)
            {
                if(job.Route!=route||job.Assigned||job.Cancelled||job.Finished||job.Owner==null||!job.Owner.gameObject.activeInHierarchy)continue;
                if(job.Owner is Behaviour bh&&!bh.enabled)continue;
                if(job.Owner is ICCMountRequester requester&&!requester.MountRequestsEnabled){job.Status="Request paused by crew Run";continue;}
                job.Status="Waiting for matching, stocked and unlocked Supply";
                for(int i=0;i<route.SourceCount;i++)
                {
                    var z=route.SourceAt(i);int index=Index(z);
                    if(index<0||_counts[index]<1||Locked(z)||z.Resource!=job.Resource)continue;
                    if(!ResolveMount(route,z,job,out Vector3 pa,out Vector3 pb,out string error)){job.Status=error;continue;}
                    float distance=(z.DockPosition-route.Crane.DeckAt(route.Crane.Current.Pose)).sqrMagnitude;
                    if(selected!=null&&(job.Ticket>selected.Ticket||(job.Ticket==selected.Ticket&&distance>=nearest)))continue;
                    selected=job;supply=z;a=pa;b=pb;nearest=distance;
                }
            }
            if(selected==null)return false;
            if(!route.Crane.ServerStartMount(this,a,b,selected)){selected.Status="Crane rejected mount: payload profile / Run / reach";return false;}
            _counts[Index(supply)]--;route.ActiveMount=selected;route.ActiveSource=supply;route.ActiveDestination=null;route.ActiveCrane=route.Crane;
            route.ReservedUnits=1;route.InFlight=true;route.SourcePicked=false;route.SourceVisualTransferred=false;route.CompletionBefore=route.Crane.Current.CompletedTrips;
            route.ActiveTrafficGroup=route.TrafficGroup;route.LastDemandCategory=1;route.Status="Direct installation assigned";
            selected.Assigned=true;selected.Status=route.Status;_dirty=true;return true;
        }
        public string DemandReport()
        {
            var b=new System.Text.StringBuilder();
            foreach(var d in _demands)if(d.Owner!=null&&d.Route!=null&&d.Zone!=null&&d.Target>0)
                b.AppendLine(d.Owner.name+" | crane="+(d.Route.Crane!=null?d.Route.Crane.name:"NONE")+" | "+d.Zone.name+" | target="+d.Target+" stock="+Stock(d.Zone)+" incoming="+Incoming(d.Zone)+" ticket="+d.Ticket);
            foreach(var j in _mounts)if(j.Owner!=null&&!j.Cancelled&&!j.Finished)
                b.AppendLine("DIRECT #"+j.Token+" "+j.Owner.name+" step="+j.Step+" "+j.Resource+" target="+j.Top+" held="+j.Holding+" fastened="+j.Fastened+" detached="+j.Detached+" | "+j.Status);
            return b.ToString();
        }
        public bool ServerSetDemand(Component requester,CCZoneCraneRoute route,CCWorkZone zone,int target)
        {
            if(NetworkObject==null||!IsServerInitialized||requester==null)return false;
            Demand d=null;foreach(var item in _demands)if(item.Owner==requester){d=item;break;}
            if(target<=0){if(d!=null)d.Target=0;return true;}
            if(route==null||route.Owner!=this||zone==null||Index(zone)<0||!zone.AcceptsDelivery)return false;
            bool allowed=false;for(int i=0;i<route.DestinationCount;i++)if(route.DestinationAt(i)==zone)allowed=true;
            if(!allowed)return false;
            if(d==null){d=new Demand{Owner=requester};_demands.Add(d);}
            if(d.Target<=0||d.Zone!=zone||d.Route!=route)d.Ticket=++_nextTicket;
            d.Route=route;d.Zone=zone;d.Target=Mathf.Clamp(target,0,zone.Capacity);return true;
        }
        [ContextMenu("Refresh local zones and routes (Edit mode)")]
        public void RefreshLocal()
        {
            if(Application.isPlaying) return;
            var zones=new System.Collections.Generic.List<CCWorkZone>();
            foreach(CCWorkZone z in GetComponentsInChildren<CCWorkZone>(true)) if(z.Owner==this) zones.Add(z);
            _zones=zones.ToArray();
            var routes=new System.Collections.Generic.List<CCZoneCraneRoute>();
            foreach(CCZoneCraneRoute r in GetComponentsInChildren<CCZoneCraneRoute>(true)) if(r.Owner==this) routes.Add(r);
            _routes=routes.ToArray();
        }
        public override void OnStartServer()
        {
            base.OnStartServer();_mounts.Clear();_mountToken=0;_demands.Clear();_nextTicket=0;DebugStartServerSeen=true;DebugDispatchTicks=0;DebugLastDispatchAt=0;_counts=new int[_zones.Length];_incoming=new int[_zones.Length];_refillAt=new float[_zones.Length];
            for(int i=0;i<_zones.Length;i++) if(_zones[i]!=null&&_zones[i].Owner==this) _counts[i]=Mathf.Clamp(_zones[i].InitialStock,0,_zones[i].Capacity);
            foreach(CCZoneCraneRoute r in _routes) if(r!=null) { r.ActiveMount=null;r.LastDemandCategory=-1;r.InFlight=false;r.SourcePicked=false;r.SourceVisualTransferred=false;r.ActiveCrane=null;r.ReservedUnits=0;r.NextTime=0;r.Status="Waiting"; }
            _next=0;_cursor=0;Publish();
        }
        public override void OnStopServer()
        {
            foreach(CCZoneCraneRoute r in _routes) if(r!=null&&r.InFlight) Cancel(r);
            _counts=null;_incoming=null;base.OnStopServer();
        }
        int Index(CCWorkZone zone)
        { if(zone==null||zone.Owner!=this) return -1;for(int i=0;i<_zones.Length;i++) if(_zones[i]==zone) return i;return -1; }
        public int Stock(CCWorkZone z)
        { int i=Index(z);int[] data=!Application.isPlaying||NetworkObject==null?null:IsServerInitialized?_counts:_stock.Value.Stock;return data!=null&&i>=0&&i<data.Length?data[i]:0; }
        public int DisplayStock(CCWorkZone z)
        {
            int i=Index(z);if(i<0||!Application.isPlaying||NetworkObject==null)return 0;
            if(!IsServerInitialized){int[] data=_stock.Value.DisplayStock;return data!=null&&i<data.Length?data[i]:Stock(z);}
            return Stock(z)+VisibleReserved(z);
        }
        int VisibleReserved(CCWorkZone z)
        {
            int n=0;foreach(var r in _routes)if(r!=null&&r.InFlight&&!r.SourceVisualTransferred&&r.ActiveSource==z)n+=r.ReservedUnits;
            return n;
        }
        public int Incoming(CCWorkZone z)
        { int i=Index(z);int[] data=!Application.isPlaying||NetworkObject==null?null:IsServerInitialized?_incoming:_stock.Value.Incoming;return data!=null&&i>=0&&i<data.Length?data[i]:0; }
        public bool ServerConsume(CCWorkZone z,int amount)
        { int i=Index(z);if(!IsServerInitialized||_counts==null||i<0||amount<=0||_counts[i]<amount) return false;_counts[i]-=amount;_dirty=true;return true; }
        public bool ServerAddStock(CCWorkZone z,int amount)
        { int i=Index(z);if(!IsServerInitialized||_counts==null||i<0||amount<=0||amount>z.Capacity-_counts[i]-_incoming[i]-Outgoing(z)) return false;_counts[i]+=amount;_dirty=true;return true; }
        int Outgoing(CCWorkZone z)
        { int count=0;foreach(CCZoneCraneRoute r in _routes) if(r!=null&&r.InFlight&&r.ActiveSource==z) count+=r.ReservedUnits;return count; }
        bool Locked(CCWorkZone z)
        { foreach(CCZoneCraneRoute r in _routes) if(r!=null&&r.InFlight&&(r.ActiveSource==z||r.ActiveDestination==z)) return true;return false; }
        bool TrafficBusy(string group)
        { if(string.IsNullOrEmpty(group))return false;foreach(CCZoneCraneRoute r in _routes)if(r!=null&&r.InFlight&&r.ActiveTrafficGroup==group)return true;return false; }
        bool CraneBusy(CCSupplyCrane c)
        { foreach(CCZoneCraneRoute r in _routes) if(r!=null&&r.InFlight&&r.ActiveCrane==c) return true;return false; }
        public CCWorkZone FindExclusion(Vector3 deck,Vector2 size,float height)
        { foreach(CCWorkZone z in _zones) if(z!=null&&z.Owner==this&&z.isActiveAndEnabled&&z.Kind==CCZoneKind.Exclusion&&z.IntersectsLoad(deck,size,height)) return z;return null; }
        public bool Excluded(Vector3 deck,Vector2 size,float height) => FindExclusion(deck,size,height)!=null;
        void Update()
        {
            if(!IsServerInitialized||_counts==null||Time.time<_next) return;
            DebugDispatchTicks++;DebugLastDispatchAt=Time.unscaledTime;
            _next=Time.time+Mathf.Max(.1f,DispatchInterval);
            foreach(CCZoneCraneRoute r in _routes)
            {
                if(r==null||!r.InFlight) continue;
                CCSupplyCrane c=r.ActiveCrane;
                if(c!=null&&!r.SourceVisualTransferred&&c.Current.Ready&&(c.Current.Phase<=6||c.Current.Phase==11||c.Current.Phase>=13))
                {r.SourceVisualTransferred=true;_dirty=true;}
                if(c!=null&&!r.SourcePicked&&c.Current.Ready&&((c.Current.Phase>=1&&c.Current.Phase<=6)||c.Current.Phase==11||c.Current.Phase>=13))
                {
                    r.SourcePicked=true;int src=Index(r.ActiveSource);
                    if(src>=0&&r.ActiveSource.AutoRefillSupply&&r.ActiveSource.Kind==CCZoneKind.Supply)
                        _refillAt[src]=Time.time+Mathf.Max(.1f,r.ActiveSource.RefillDelay);
                }
                if(r.ActiveMount!=null&&c!=null)
                {
                    r.ActiveMount.Detached|=r.ActiveMount.Fastened&&c.Current.DirectDetached;
                    r.ActiveMount.Status=c.PhaseName;
                }
                if(c!=null&&c.Current.CompletedTrips!=r.CompletionBefore)
                {
                    int to=Index(r.ActiveDestination);if(to>=0) { _incoming[to]-=r.ReservedUnits;_counts[to]+=r.ReservedUnits; }
                    if(r.ActiveMount!=null){r.ActiveMount.Finished=true;r.ActiveMount.Detached=true;r.ActiveMount.Status="Installed; crane free";}
                    r.InFlight=false;r.ReservedUnits=0;r.Status="Delivered";r.NextTime=Time.time+r.Cooldown;_dirty=true;
                }
                else if(c==null||!c.isActiveAndEnabled||c.Territory!=this||!c.IsServerInitialized||r.ActiveSource==null||(r.ActiveMount==null?r.ActiveDestination==null:r.ActiveMount.Owner==null)) Cancel(r);
                else r.Status=c.PhaseName;
            }
            for(int i=0;i<_zones.Length;i++)
            {
                CCWorkZone z=_zones[i];
                if(z==null||!z.AutoRefillSupply||z.Kind!=CCZoneKind.Supply||!z.isActiveAndEnabled||_refillAt[i]<=0||Time.time<_refillAt[i])continue;
                int target=Mathf.Clamp(z.InitialStock,0,z.Capacity);
                int add=Mathf.Min(target-_counts[i],z.Capacity-_counts[i]-_incoming[i]-Outgoing(z));
                if(add>0){_counts[i]+=add;_dirty=true;}
                if(_counts[i]>=target)_refillAt[i]=0;
            }
            if(Dispatch&&_routes.Length>0)
            {
                // Round-robin prevents an early route from taking every free crane slot.
                for(int n=0;n<_routes.Length;n++)
                {
                    CCZoneCraneRoute r=_routes[(_cursor+n)%_routes.Length];
                    if(r==null||r.InFlight) continue;
                    if(r.Owner!=this){r.Status="Wrong territory owner";continue;}
                    if(!r.Enabled||!r.isActiveAndEnabled){r.Status="Route disabled / inactive parent";continue;}
                    if(Time.time<r.NextTime){r.Status="Cooldown";continue;}
                    string notReady=CraneWaitReason(r);
                    if(notReady.Length>0){r.Status=notReady;continue;}
                    if(TrafficBusy(r.TrafficGroup)) { r.Status="Shared corridor reserved: "+r.TrafficGroup;continue; }
                    if(r.DemandDriven&&r.LastDemandCategory!=1&&TryStartMount(r))continue;
                    if(!SelectDelivery(r,out CCWorkZone source,out CCWorkZone destination,out Vector3 a,out Vector3 b,out int amount,out string error))
                    {if(r.DemandDriven&&TryStartMount(r))continue;r.Status=error;
                        if(r.DemandDriven)foreach(var j in _mounts)if(j.Route==r&&!j.Assigned&&!j.Cancelled&&!j.Finished&&j.Owner!=null){r.Status="Waiting direct mount: "+j.Status;break;}
                        continue;}
                    int from=Index(source),to=Index(destination);
                    Vector3 sa=r.Crane.transform.InverseTransformDirection(source.StagingPosition-source.DockPosition);
                    Vector3 sb=r.Crane.transform.InverseTransformDirection(destination.StagingPosition-destination.DockPosition);
                    if(!r.Crane.ServerStartDelivery(this,a,b,sa,sb,amount,(int)source.Resource)) { r.Status="Crane rejected job / start delay";continue; }
                    r.ActiveMount=null;_counts[from]-=amount;_incoming[to]+=amount;r.SourcePicked=false;r.SourceVisualTransferred=false;r.ReservedUnits=amount;r.ActiveCrane=r.Crane;r.ActiveSource=source;r.ActiveDestination=destination;
                    r.CompletionBefore=r.Crane.Current.CompletedTrips;r.ActiveTrafficGroup=r.TrafficGroup;r.InFlight=true;r.Status="Assigned";_dirty=true;
                    if(r.DemandDriven)
                    {
                        r.LastDemandCategory=destination.Resource==CCZoneResource.Bricks?0:1;
                        foreach(var d in _demands)if(d.Route==r&&d.Zone==destination&&d.Target>0)d.Ticket=++_nextTicket;
                    }
                }
                _cursor=(_cursor+1)%_routes.Length;
            }
            if(_dirty) Publish();
        }
        string CraneWaitReason(CCZoneCraneRoute r)
        {
            CCSupplyCrane c=r.Crane;
            if(c==null)return "Missing Crane reference";
            if(!c.gameObject.activeInHierarchy)return "Crane inactive / inactive parent";
            if(!c.enabled)return "Crane component disabled";
            if(c.Territory!=this)return "Crane assigned to another/no territory";
            if(c.NetworkObject==null)return "Crane NetworkObject unavailable";
            if(!c.IsServerInitialized)return "Crane has not started on server (NetworkObject/spawn)";
            if(!string.IsNullOrEmpty(c.DebugStartupError))return "Crane startup failed: "+c.DebugStartupError;
            if(!c.Run)return "Crane Run OFF";
            if(!c.Current.Ready)return "Crane not Ready";
            if(!c.Current.Managed)return "Crane started without Territory; restart Host after assigning";
            if(CraneBusy(c)||!c.WaitingForJob)return "Crane busy: "+c.PhaseName;
            if(c.DebugDelayRemaining>0)return "Crane StartDelay";
            return "";
        }
        bool SelectDelivery(CCZoneCraneRoute r,out CCWorkZone source,out CCWorkZone destination,out Vector3 a,out Vector3 b,out int amount,out string reason)
        {
            if(!r.DemandDriven)return SelectPair(r,out source,out destination,out a,out b,out amount,out reason);
            source=destination=null;a=b=Vector3.zero;amount=0;reason="No pending crew demand";
            int bestRank=3;ulong bestTicket=ulong.MaxValue;float bestDistance=float.PositiveInfinity;
            Vector3 deck=r.Crane.DeckAt(r.Crane.Current.Pose);
            for(int k=_demands.Count-1;k>=0;k--)
            {
                Demand d=_demands[k];
                if(d.Owner==null){_demands.RemoveAt(k);continue;}
                if(d.Route!=r||d.Target<=0||!d.Owner.gameObject.activeInHierarchy||(d.Owner is Behaviour behaviour&&!behaviour.enabled))continue;
                CCWorkZone z=d.Zone;int to=Index(z);
                if(to<0||!z.isActiveAndEnabled||Locked(z))continue;
                bool listed=false;for(int i=0;i<r.DestinationCount;i++)if(r.DestinationAt(i)==z)listed=true;
                if(!listed)continue;
                int need=Mathf.Min(d.Target,z.Capacity)-_counts[to]-_incoming[to];if(need<=0)continue;
                int category=z.Resource==CCZoneResource.Bricks?0:1;
                int rank=category==r.LastDemandCategory?1:0;
                for(int i=0;i<r.SourceCount;i++)
                {
                    CCWorkZone supply=r.SourceAt(i);int from=Index(supply);
                    if(from<0||!supply.isActiveAndEnabled||supply.Resource!=z.Resource||Locked(supply))continue;
                    int trip=z.Resource==CCZoneResource.SteelBeam||z.Resource==CCZoneResource.SteelColumn?1:Mathf.Clamp(r.UnitsPerTrip,1,2);
                    int n=Mathf.Min(trip,Mathf.Min(need,_counts[from]));
                    if(n<=0||(!r.AllowPartialLoad&&n<trip)){reason="Demand waiting for full load / supply: "+z.name;continue;}
                    float distance=(supply.DockPosition-deck).sqrMagnitude;
                    if(rank>bestRank||(rank==bestRank&&(d.Ticket>bestTicket||(d.Ticket==bestTicket&&distance>=bestDistance))))continue;
                    if(!r.ResolvePair(supply,z,out Vector3 pa,out Vector3 pb,out string error)){reason=supply.name+" -> "+z.name+": "+error;continue;}
                    source=supply;destination=z;a=pa;b=pb;amount=n;bestRank=rank;bestTicket=d.Ticket;bestDistance=distance;
                }
            }
            if(source!=null)reason="Demand selected";
            return source!=null;
        }
        bool SelectPair(CCZoneCraneRoute r,out CCWorkZone source,out CCWorkZone destination,out Vector3 a,out Vector3 b,out int amount,out string reason)
        {
            source=destination=null;a=b=Vector3.zero;amount=0;
            int bestStock=int.MaxValue;float bestDistance=float.PositiveInfinity;
            string last="No points configured";
            Vector3 deck=r.Crane.DeckAt(r.Crane.Current.Pose);
            // No scene-wide search, no allocation of candidate lists. Only explicit per-crane points.
            for(int di=0;di<r.DestinationCount;di++)
            {
                CCWorkZone d=r.DestinationAt(di);int to=Index(d);
                if(to<0){last="Receiving point missing/unregistered; Refresh territory";continue;}
                if(!d.isActiveAndEnabled||!d.AcceptsDelivery){last=d.name+": receiving point disabled/wrong kind";continue;}
                if(Locked(d)){last=d.name+": zone reserved";continue;}
                int occupied=_counts[to]+_incoming[to],free=d.Capacity-occupied;
                if(free<=0){last=d.name+": destination full";continue;}
                for(int si=0;si<r.SourceCount;si++)
                {
                    CCWorkZone z=r.SourceAt(si);int from=Index(z);
                    if(from<0){last="Supply missing/unregistered; Refresh territory";continue;}
                    if(!z.isActiveAndEnabled||z.Kind!=CCZoneKind.Supply){last=z.name+": supply disabled/wrong kind";continue;}
                    if(z.Resource!=d.Resource){last=z.name+" -> "+d.name+": resource mismatch";continue;}
                    if(Locked(z)){last=z.name+": zone reserved";continue;}
                    int units=Mathf.Min(Mathf.Clamp(r.UnitsPerTrip,1,2),Mathf.Min(_counts[from],free));
                    if(units<=0){last=z.name+": supply empty";continue;}
                    if(!r.AllowPartialLoad&&units<Mathf.Clamp(r.UnitsPerTrip,1,2)){last="Not enough stock/space for a full batch";continue;}
                    float distance=(z.DockPosition-deck).sqrMagnitude;
                    if(occupied>bestStock||(occupied==bestStock&&distance>=bestDistance))continue;
                    if(!r.ResolvePair(z,d,out Vector3 pa,out Vector3 pb,out string error)){last=z.name+" -> "+d.name+": "+error;continue;}
                    source=z;destination=d;a=pa;b=pb;amount=units;bestStock=occupied;bestDistance=distance;
                }
            }
            reason=source!=null?"":"No eligible delivery. Last check: "+last;
            return source!=null;
        }
        void Cancel(CCZoneCraneRoute r)
        {
            if(r.ActiveCrane!=null) r.ActiveCrane.ServerCancelDelivery(this);
            int from=Index(r.ActiveSource),to=Index(r.ActiveDestination);
            if(_counts!=null&&from>=0&&(r.ActiveMount==null||!r.ActiveMount.Fastened)) _counts[from]+=r.ReservedUnits;
            if(_incoming!=null&&to>=0) _incoming[to]-=r.ReservedUnits;
            if(r.ActiveMount!=null){r.ActiveMount.Cancelled=true;r.ActiveMount.Detached=r.ActiveMount.Fastened;r.ActiveMount.Finished=r.ActiveMount.Fastened;r.ActiveMount.Status=r.ActiveMount.Fastened?"Secured before cancellation":"Cancelled; stock returned";}
            r.InFlight=false;r.ReservedUnits=0;r.Status="Cancelled (secured steel is not refunded)";_dirty=true;
        }
        void Publish()
        {
            int[] display=(int[])_counts.Clone();for(int i=0;i<_zones.Length;i++)if(_zones[i]!=null)display[i]+=VisibleReserved(_zones[i]);
            _stock.Value=new StockState{Stock=(int[])_counts.Clone(),Incoming=(int[])_incoming.Clone(),DisplayStock=display};_dirty=false;
        }
    }
}
