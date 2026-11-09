using UnityEngine;
namespace CatCooks.CraneLogistics
{
    // Server-only transaction. Geometry is copied at request time; moving nodes in Play is unsupported.
    public sealed class CCDirectMountJob
    {
        public Component Owner { get; internal set; }
        public CCZoneCraneRoute Route { get; internal set; }
        public int Step { get; internal set; }
        public uint Token { get; internal set; }
        public CCZoneResource Resource { get; internal set; }
        public Vector3 Top { get; internal set; }
        public Quaternion Rotation { get; internal set; }
        public Vector3 Size { get; internal set; }
        public bool Assigned { get; internal set; }
        public bool Fastened { get; internal set; }
        public bool Detached { get; internal set; }
        public bool Finished { get; internal set; }
        public bool Cancelled { get; internal set; }
        public string Status { get; internal set; } = "Queued";
        internal ulong Ticket;
        public bool Holding => Assigned&&!Cancelled&&!Fastened&&Route!=null&&Route.ActiveMount==this&&
            Route.ActiveCrane!=null&&Route.ActiveCrane.Current.MountToken==Token&&Route.ActiveCrane.Current.Phase==13;
    }
}

namespace CatCooks.CraneLogistics { public interface ICCMountRequester { bool MountRequestsEnabled { get; } } }
