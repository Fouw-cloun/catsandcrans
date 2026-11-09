using UnityEngine;
namespace CatCooks.CraneLogistics
{
    public enum CCZoneKind { Supply, Receiving, Work, Rest, Exclusion }
    public enum CCZoneResource { Crates, Bricks, Boards, Sand, WindowFrames, SteelColumn, SteelBeam }
    [DisallowMultipleComponent]
    public sealed class CCWorkZone:MonoBehaviour
    {
        public string Label="New zone";
        public CCZoneKind Kind;
        public CCZoneResource Resource;
        [Tooltip("Local box: bottom is zone Y zero; do not scale the root.")]
        public Vector3 Size=new Vector3(8,3,8);
        [Min(0)] public int InitialStock=0;
        [Min(1)] public int Capacity=20;
        [Header("Optional renewable supply (server)")]
        public bool AutoRefillSupply=false;
        [Min(.1f)] public float RefillDelay=5;
        public Transform Dock, Staging;
        public Transform[] WorkPoints=new Transform[0];
        public CCWorkTerritory Owner => GetComponentInParent<CCWorkTerritory>();
        public Vector3 DockPosition => Dock!=null?Dock.position:transform.position;
        public Vector3 StagingPosition => Staging!=null?Staging.position:transform.position+transform.right*4;
        public bool AcceptsDelivery => Kind==CCZoneKind.Receiving||Kind==CCZoneKind.Work;
        public Color Tint
        {
            get
            {
                switch(Kind) { case CCZoneKind.Supply:return new Color(.2f,.65f,1);case CCZoneKind.Receiving:return new Color(1,.75f,.1f);case CCZoneKind.Work:return new Color(.2f,1,.4f);case CCZoneKind.Rest:return new Color(.65f,.4f,1);default:return new Color(1,.2f,.2f); }
            }
        }
        public bool Contains(Vector3 p)
        {
            Vector3 q=transform.InverseTransformPoint(p);
            return Mathf.Abs(q.x)<=Size.x*.5f&&Mathf.Abs(q.z)<=Size.z*.5f&&q.y>=-.15f&&q.y<=Size.y;
        }
        // Conservative XZ overlap, independent of platform heading; no physics collider needed.
        public bool IntersectsLoad(Vector3 deck,Vector2 size,float height)
        {
            Vector3 q=transform.InverseTransformPoint(deck);float radius=size.magnitude*.5f;
            return q.y+height>=0&&q.y<=Size.y&&Mathf.Abs(q.x)<=Size.x*.5f+radius&&Mathf.Abs(q.z)<=Size.z*.5f+radius;
        }
        void OnValidate() { Size=new Vector3(Mathf.Max(.5f,Size.x),Mathf.Max(.1f,Size.y),Mathf.Max(.5f,Size.z));Capacity=Mathf.Max(1,Capacity);InitialStock=Mathf.Clamp(InitialStock,0,Capacity); }
        void OnDrawGizmos()
        {
            Matrix4x4 old=Gizmos.matrix;Gizmos.matrix=transform.localToWorldMatrix;
            Color c=Tint;c.a=.07f;Gizmos.color=c;Gizmos.DrawCube(Vector3.up*Size.y*.5f,Size);
            c.a=.8f;Gizmos.color=c;Gizmos.DrawWireCube(Vector3.up*Size.y*.5f,Size);Gizmos.matrix=old;
            if(Kind!=CCZoneKind.Exclusion&&Kind!=CCZoneKind.Rest) { Gizmos.DrawWireSphere(DockPosition,.3f);Gizmos.DrawLine(DockPosition,StagingPosition); }
            if(WorkPoints!=null) foreach(Transform p in WorkPoints) if(p!=null) Gizmos.DrawWireSphere(p.position,.2f);
        }
    }
}
