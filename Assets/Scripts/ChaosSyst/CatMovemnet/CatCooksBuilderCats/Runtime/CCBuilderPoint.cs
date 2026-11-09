using System;
using UnityEngine;
namespace CatCooks.Builders
{
    [Flags] public enum CCBuilderPlace { Work = 1, Meeting = 2, Rest = 4, Any = 7 }
    public enum CCBuilderActivity : byte { Idle, Walking, Gathering, Working, Chatting, Hopping, Resting, Stranded, Digging, Hammering, Carried, Fallen, GettingUp, Collecting, Sleeping, Operating }
    public sealed class CCBuilderPoint : MonoBehaviour
    {
        public CCBuilderPlace Kind = CCBuilderPlace.Any;
        [Min(.6f)] public float GroupRadius = 1.2f;
        public Vector3 Slot(int slot, int count)
        {
            float angle = slot * Mathf.PI * 2f / Mathf.Max(1, count);
            return transform.position + transform.rotation * new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * GroupRadius;
        }
        private void OnDrawGizmos()
        {
            Gizmos.color = (Kind & CCBuilderPlace.Work) != 0 ? new Color(1,.65f,.15f,.65f) : new Color(.25f,.8f,.8f,.65f);
            Gizmos.DrawWireSphere(transform.position, .16f);
            for (int i=0;i<4;i++) Gizmos.DrawWireSphere(Slot(i,4), .22f);
        }
    }
}
