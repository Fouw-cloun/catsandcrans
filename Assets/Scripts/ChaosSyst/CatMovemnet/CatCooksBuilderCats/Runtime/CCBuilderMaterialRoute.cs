using UnityEngine;
namespace CatCooks.Builders
{
    public sealed class CCBuilderMaterialRoute : MonoBehaviour
    {
        public Transform PointA,PointB;
        [Min(.2f)] public float StackSpacing=.55f;
        [Min(.1f)] public float PickupDelay=1.5f;
        public bool Ready => isActiveAndEnabled && PointA!=null && PointB!=null;
        public Vector3 Location(int end,int slot)
        {
            Transform p=end==0?PointA:PointB;if(p==null) return transform.position;
            return p.position+p.rotation*new Vector3((slot%3-1)*StackSpacing,0,(slot/3)*StackSpacing);
        }
    }
}
