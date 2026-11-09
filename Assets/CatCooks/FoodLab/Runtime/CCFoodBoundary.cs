using UnityEngine;

namespace CatCooks.FoodLab
{
    public enum FoodBoundaryShape { SolidBox, Bowl }
    // Analytic, moving collision geometry. Ordinary Unity colliders do NOT enter this solver.
    public sealed class CCFoodBoundary : MonoBehaviour
    {
        public CCFoodWorld world;
        public FoodBoundaryShape shape;
        public Vector3 boxSize = Vector3.one;
        public float radius = .25f, height = .25f, wall = .025f;
        [Range(0, 1)] public float friction = .4f;
        Matrix4x4 inverse, matrix, previous;
        bool initialized;
        float step;
        void OnEnable() { if (world != null) world.Register(this); }
        void OnDisable() { if (world != null) world.Unregister(this); }
        public void Prepare(float dt)
        {
            previous = initialized ? matrix : transform.localToWorldMatrix;
            matrix = transform.localToWorldMatrix; inverse = matrix.inverse;
            initialized = true; step = Mathf.Max(dt, .0001f);
        }
        public bool Project(ref Vector3 p, float particleRadius, out Vector3 normal, out Vector3 velocity)
        {
            normal = velocity = Vector3.zero;
            Vector3 scale = transform.lossyScale;
            // Uniform positive scale only. Builder creates unit-scale boundaries.
            float s = Mathf.Max(.0001f, Mathf.Abs(scale.x));
            Vector3 q = inverse.MultiplyPoint3x4(p), before = q;
            float r = particleRadius / s;
            if (shape == FoodBoundaryShape.SolidBox)
            {
                Vector3 half = boxSize * .5f + Vector3.one * r;
                Vector3 d = half - new Vector3(Mathf.Abs(q.x), Mathf.Abs(q.y), Mathf.Abs(q.z));
                if (d.x <= 0 || d.y <= 0 || d.z <= 0) return false;
                if (d.x < d.y && d.x < d.z) q.x = (q.x < 0 ? -1 : 1) * half.x;
                else if (d.y < d.z) q.y = (q.y < 0 ? -1 : 1) * half.y;
                else q.z = (q.z < 0 ? -1 : 1) * half.z;
            }
            else
            {
                float radial = new Vector2(q.x, q.z).magnitude;
                // Finite solid base: underside and top both collide.
                if (radial < radius + wall + r && q.y > -wall-r && q.y < r)
                    q.y = q.y < -wall*.5f ? -wall-r : r;
                // Finite cylindrical wall: interior, exterior and the top rim.
                if (q.y >= -wall && q.y < height+r && radial > radius-r && radial < radius+wall+r)
                {
                    float inside = radial-(radius-r), outside = radius+wall+r-radial, top = height+r-q.y;
                    if (top < inside && top < outside) q.y = height+r;
                    else
                    {
                        float target = inside < outside ? radius-r : radius+wall+r;
                        if (radial > .00001f) { q.x *= target/radial; q.z *= target/radial; }
                    }
                }
                if ((q-before).sqrMagnitude < 1e-14f) return false;
            }
            Vector3 corrected = matrix.MultiplyPoint3x4(q);
            normal = (corrected-p).normalized;
            velocity = Vector3.ClampMagnitude((corrected-previous.MultiplyPoint3x4(q))/step, 5);
            p = corrected; return true;
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan; Gizmos.matrix = transform.localToWorldMatrix;
            if (shape == FoodBoundaryShape.SolidBox) Gizmos.DrawWireCube(Vector3.zero, boxSize);
            else Gizmos.DrawWireCube(new Vector3(0,height*.5f,0),new Vector3(radius*2,height,radius*2));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
