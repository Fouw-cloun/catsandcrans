using UnityEngine;
using UnityEngine.AI;
namespace CatCooks.Builders
{
    // Baked in the Editor, registered once at runtime. No runtime mesh rebuilding.
    [DefaultExecutionOrder(-500)]
    public sealed class CCBuilderNavArea : MonoBehaviour
    {
        public NavMeshData Data;
        public int AgentTypeId;
        public Vector3 Center = new Vector3(0,2,0);
        public Vector3 Size = new Vector3(32,12,32);
        public LayerMask GeometryMask = ~0;
        [Tooltip("Необязательно: брать геометрию только из этой иерархии.")]
        public Transform GeometryRoot;
        [Min(.1f)] public float AgentRadius = .24f;
        [Min(.3f)] public float AgentHeight = .95f;
        private NavMeshDataInstance _instance;
        public Bounds WorldBounds => new Bounds(transform.position + Center, Size);
        private void OnEnable()
        {
            if (Application.isPlaying && Data != null && !_instance.valid) _instance = NavMesh.AddNavMeshData(Data);
        }
        private void OnDisable() { if (_instance.valid) _instance.Remove(); }
        private void OnDrawGizmosSelected() { Gizmos.color = Color.cyan; Gizmos.DrawWireCube(WorldBounds.center, WorldBounds.size); }
    }
}
