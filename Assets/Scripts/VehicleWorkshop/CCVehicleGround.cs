using UnityEngine;

/// <summary>Suspension must not support itself on its own bucket, triggers or food.</summary>
public static class CCVehicleGround
{
    public static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    public static bool Cast(Transform root,Rigidbody body,Vector3 origin,Vector3 direction,float length,
        LayerMask mask,RaycastHit[] buffer,out RaycastHit result)
    {
        result=default;float best=float.PositiveInfinity;
        int n=Physics.RaycastNonAlloc(origin,direction,buffer,length,mask,QueryTriggerInteraction.Ignore);
        // Saturation is rare, but silently missing the road is worse than a temporary allocation.
        RaycastHit[] hits=n==buffer.Length?Physics.RaycastAll(origin,direction,length,mask,QueryTriggerInteraction.Ignore):buffer;
        if(hits!=buffer)n=hits.Length;
        for(int i=0;i<n;i++)
        {
            RaycastHit h=hits[i];Collider c=h.collider;
            if(c==null || c.transform.IsChildOf(root) || (body!=null && h.rigidbody==body))continue;
            if(Vector3.Dot(h.normal,-direction)<.35f)continue;
            if(c.GetComponentInParent<CatCooks.Food.FoodPiece>()!=null || c.GetComponentInParent<PlayerInteractor>()!=null)continue;
            if(h.distance<best){best=h.distance;result=h;}
        }
        return best<float.PositiveInfinity;
    }
}
