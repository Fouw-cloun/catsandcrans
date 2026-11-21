using UnityEngine;
using UnityEngine.SceneManagement;

// Positions are evaluated in the physics scene that owns the vehicle.
internal static class CCVehicleExitFloor
{
    internal static Quaternion Upright(Transform player)
    {
        Vector3 forward=Vector3.ProjectOnPlane(player.forward,Vector3.up);
        return Quaternion.LookRotation(forward.sqrMagnitude>.001f?forward:Vector3.forward);
    }

    internal static bool TryFind(NetworkVehicle seat, Transform mount, Transform marker,
        CharacterController cc, out Vector3 position)
    {
        position = default;
        if (!cc || !seat.NetworkObject) return false;
        var root = seat.NetworkObject.transform;
        var world = root.gameObject.scene.GetPhysicsScene();
        if(!world.IsValid()) return false;
        Physics.SyncTransforms();
        Vector3 scale = cc.transform.lossyScale;
        float radius = Mathf.Max(.05f, cc.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));
        float height = Mathf.Max(radius * 2f, cc.height * Mathf.Abs(scale.y));
        float lift = Mathf.Clamp(cc.skinWidth * Mathf.Abs(scale.y) + .035f, .065f, .22f);
        Vector3 offset = Upright(cc.transform) * Vector3.Scale(cc.center,scale);
        Vector3 anchor = mount ? mount.position : root.position;
        Vector3 side = Vector3.ProjectOnPlane(root.right, Vector3.up).normalized;
        if (side.sqrMagnitude < .01f) side=Vector3.right;
        Vector3 front = Vector3.Cross(side,Vector3.up);
        float road = root.position.y;
        // A chassis pivot can be at axle height or at the cab. Estimate street level
        // from the physical hull, and keep lower storeys outside the search window.
        bool hasHull=false;
        foreach(var c in root.GetComponentsInChildren<Collider>())
        {
            if (!c.enabled || c.isTrigger || c is WheelCollider) continue;
            road=hasHull ? Mathf.Min(road,c.bounds.min.y) : c.bounds.min.y;
            hasHull=true;
        }
        var rays = new RaycastHit[48];
        var overlaps = new Collider[64];
        float slopeCos=Mathf.Cos(Mathf.Clamp(cc.slopeLimit,0f,65f)*Mathf.Deg2Rad);
        int layer=cc.gameObject.layer;
        int mask=0;
        for(int i=0;i<32;i++) if(!Physics.GetIgnoreLayerCollision(layer,i)) mask |= 1<<i;

        bool FindFloor(Vector3 candidate,out RaycastHit ground)
        {
            Vector3 top=new Vector3(candidate.x,road+1.35f,candidate.z);
            int count=world.Raycast(top,Vector3.down,rays,2.6f,mask,QueryTriggerInteraction.Ignore);
            ground=default;
            if(count==rays.Length) return false;
            float closest=float.PositiveInfinity;
            for(int i=0;i<count;i++)
            {
                var h=rays[i]; var tr=h.collider.transform;
                if(tr.IsChildOf(root)||tr.IsChildOf(cc.transform)||h.distance>=closest) continue;
                closest=h.distance; ground=h;
            }
            return ground.collider && ground.normal.y>=slopeCos &&
                (!ground.rigidbody || ground.rigidbody.isKinematic) &&
                !ground.collider.GetComponentInParent<CharacterController>();
        }
        bool Candidate(Vector3 point,out Vector3 pose)
        {
            pose=default;
            if(!FindFloor(point,out var floor)) return false;
            // Check the footprint across tile edges and holes. A foot on a narrow beam
            // is insufficient for the player's upright capsule.
            for(int k=0;k<4;k++)
            {
                Vector3 d=k==0?side:k==1?-side:k==2?front:-front;
                if(!FindFloor(point+d*radius*.72f,out var rim) ||
                    Mathf.Abs(rim.point.y-floor.point.y)>radius*.72f+.12f) return false;
            }
            float slopeLift=radius*(1f/Mathf.Max(.4f,floor.normal.y)-1f);
            Vector3 feet=floor.point+Vector3.up*(lift+slopeLift);
            int n=world.OverlapCapsule(feet+Vector3.up*radius,feet+Vector3.up*(height-radius),
                radius,overlaps,mask,QueryTriggerInteraction.Ignore);
            if(n==overlaps.Length) return false;
            for(int i=0;i<n;i++)
                if(!overlaps[i].transform.IsChildOf(cc.transform)) return false;
            pose=feet+Vector3.up*(height*.5f)-offset;
            return true;
        }
        if(marker && Candidate(marker.position,out position)) return true;
        Vector3 first=marker ? Vector3.ProjectOnPlane(marker.position-anchor,Vector3.up).normalized : -side;
        if(first.sqrMagnitude<.01f) first=-side;
        for(int ring=0;ring<5;ring++)
        for(int i=0;i<12;i++)
        {
            Vector3 dir=Quaternion.AngleAxis(i*30f,Vector3.up)*first;
            if(Candidate(anchor+dir*(1.25f+ring*.85f),out position)) return true;
        }
        return false;
    }
}
