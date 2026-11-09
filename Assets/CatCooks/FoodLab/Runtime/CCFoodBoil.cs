using UnityEngine;
using UnityEngine.Rendering;

namespace CatCooks.FoodLab
{
    // Cosmetic only. Recipe/vessel integration controls amount on each client independently.
    public sealed class CCFoodBoil : MonoBehaviour
    {
        public CCFoodWorld world;
        public int station;
        public float radius=.23f;
        [Range(0,1)] public float amount;
        public Mesh bubbleMesh;
        public Material bubbleMaterial;
        readonly Matrix4x4[] matrices=new Matrix4x4[32];
        void LateUpdate()
        {
            if(amount<=0||world==null||world.Profiles==null||bubbleMesh==null||bubbleMaterial==null||SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)return;
            int count=0;
            foreach(var p in world.Solver.particles)
            {
                Vector3 local=transform.InverseTransformPoint(p.display);
                if(local.y<0||local.y>.4f||new Vector2(local.x,local.z).magnitude>radius||CCFoodSurface.Hash(p.id)>.2f)continue;
                var mode=world.Profiles[p.profile].behaviour;if(mode!=FoodBehaviour.Fluid&&mode!=FoodBehaviour.Paste)continue;
                float phase=Mathf.Repeat(Time.time*(1+amount)+CCFoodSurface.Hash(p.id*19)*8,1);
                float size=Mathf.Sin(phase*Mathf.PI)*world.particleSpacing*.7f*amount;
                matrices[count++]=Matrix4x4.TRS(p.display+Vector3.up*(world.particleSpacing*.45f+phase*.013f),Quaternion.identity,new Vector3(size,size*.45f,size));
                if(count==32)break;
            }
            if(count>0)Graphics.DrawMeshInstanced(bubbleMesh,0,bubbleMaterial,matrices,count,null,ShadowCastingMode.Off,false,gameObject.layer);
        }
    }
}
