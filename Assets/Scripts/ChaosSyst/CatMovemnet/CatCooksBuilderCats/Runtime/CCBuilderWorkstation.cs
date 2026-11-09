using UnityEngine;

namespace CatCooks.Builders
{
    public enum CCBuilderJob { Dig, Hammer, Deliver }
    public enum CCBuilderItemKind { Shovel, Hammer, Brick, Plank, WindowFrame }

    [DisallowMultipleComponent]
    public sealed class CCBuilderWorkstation : MonoBehaviour
    {
        public CCBuilderJob Job;
        public CCBuilderMaterialRoute Route;
        [Range(0,1)] public int RouteEnd;
        public CCBuilderSandPile SandSource, SandTarget;
        [Min(.1f)] public float SandPerScoop=2;
        [Min(.5f)] public float ScoopInterval=2;
        public bool HasSand => SandSource!=null && SandTarget!=null && SandSource!=SandTarget &&
            SandSource.isActiveAndEnabled && SandTarget.isActiveAndEnabled && SandSource.Amount>.01f && SandTarget.Free>.01f;
        public CCBuilderItemKind MaterialKind = CCBuilderItemKind.Brick;
        [Range(1,4)] public int Capacity = 4;
        [Min(.5f)] public float ApproachRadius = 1.2f;
        [Tooltip("Необязательно: точки СТОЯНИЯ ног котов, на уровне пола. До 4.")]
        public Transform[] WorkSlots = new Transform[0];
        public Transform LookAtPoint;
        public Transform DeliveryPoint;
        public Vector2 WorkSeconds = new Vector2(7,12);
        [Tooltip("0: доставленные материалы остаются. >0: муляж возвращается на склад через N секунд.")]
        [Min(0)] public float RestockSeconds = 45f;
        private readonly CCBuilderCat[] _owners = new CCBuilderCat[4];
        private int _deliveryCount;
        public int CompletedJobs { get; private set; }
        public int SlotCount => WorkSlots != null && WorkSlots.Length > 0
            ? Mathf.Min(Mathf.Clamp(Capacity,1,4),WorkSlots.Length) : Mathf.Clamp(Capacity,1,4);
        public CCBuilderItemKind NeededItem => Job==CCBuilderJob.Dig ? CCBuilderItemKind.Shovel :
            Job==CCBuilderJob.Hammer ? CCBuilderItemKind.Hammer : MaterialKind;
        public Vector3 Focus => LookAtPoint!=null ? LookAtPoint.position : transform.position+Vector3.up*.3f;
        public Vector3 SlotPosition(int slot)
        {
            if (WorkSlots!=null && slot<WorkSlots.Length && WorkSlots[slot]!=null) return WorkSlots[slot].position;
            float angle=slot*Mathf.PI*2f/SlotCount;
            return transform.position+transform.rotation*new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*ApproachRadius;
        }
        public int FreeSlot()
        {
            if (!isActiveAndEnabled) return -1;
            for(int i=0;i<SlotCount;i++) if(_owners[i]==null || !_owners[i].IsServerInitialized) return i;
            return -1;
        }
        public bool Reserve(CCBuilderCat cat,int slot)
        {
            if(cat==null || !cat.IsServerInitialized || !isActiveAndEnabled || slot<0 || slot>=SlotCount) return false;
            if(_owners[slot]!=null && _owners[slot]!=cat && _owners[slot].IsServerInitialized) return false;
            _owners[slot]=cat;return true;
        }
        public bool Owns(CCBuilderCat cat,int slot) => slot>=0 && slot<SlotCount && _owners[slot]==cat && isActiveAndEnabled;
        public void Release(CCBuilderCat cat)
        { for(int i=0;i<_owners.Length;i++) if(_owners[i]==cat) _owners[i]=null; }
        public void Complete(CCBuilderCat cat) { if(cat!=null && cat.IsServerInitialized) CompletedJobs++; }
        public Vector3 NextDeliveryPosition()
        {
            Transform point=DeliveryPoint!=null ? DeliveryPoint : transform;
            int n=_deliveryCount++%12;
            return point.position+point.rotation*new Vector3((n%3-1)*.48f,0,(n/3)*.45f);
        }
        private void OnDisable() { for(int i=0;i<_owners.Length;i++) _owners[i]=null; }
        private void OnDrawGizmosSelected()
        {
            Gizmos.color=Job==CCBuilderJob.Deliver ? Color.yellow : Color.green;
            Gizmos.DrawWireSphere(transform.position,.25f);
            for(int i=0;i<SlotCount;i++) { Gizmos.DrawWireSphere(SlotPosition(i),.23f);Gizmos.DrawLine(SlotPosition(i),Focus); }
        }
    }
}
