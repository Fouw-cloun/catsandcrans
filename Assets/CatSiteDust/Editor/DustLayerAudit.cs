using UnityEngine;
using UnityEditor;
namespace CatSite.Dust.Editor
{
 public static class DustLayerAudit
 {
  [MenuItem("Tools/Cat Site Dust/Audit Every Layer")]
  public static void Audit()
  {
   int checkedLayers=0,failures=0;
   foreach(string id in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/CatSiteDust/Prefabs"}))
   {
    var asset=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(id));
    var go=Object.Instantiate(asset);go.hideFlags=HideFlags.HideAndDontSave;
    try
    {
     var fx=go.GetComponent<DustEffect>();fx.Initialize();fx.Stop(true);
     foreach(var ps in fx.Systems)
     {
      checkedLayers++;var r=ps.GetComponent<ParticleSystemRenderer>();var e=ps.emission;
      var oldRate=e.rateOverTime;
      // A stationary distance emitter is expected to be empty; provide motion-equivalent rate for this test only.
      if(e.rateOverTimeMultiplier==0 && e.rateOverDistanceMultiplier>0)e.rateOverTime=e.rateOverDistanceMultiplier*2;
      ps.Simulate(.4f,false,true,true);e.rateOverTime=oldRate;
      bool valid=ps.particleCount>0 && r && r.enabled && r.sharedMaterial && r.sharedMaterial.shader;
      if(valid && ShaderUtil.ShaderHasError(r.sharedMaterial.shader))valid=false;
      if(valid && r.renderMode==ParticleSystemRenderMode.Mesh)valid=r.mesh && r.mesh.vertexCount>0;
      if(!valid)failures++;
      Debug.Log($"Dust layer {(valid?"PASS":"FAIL")}: {asset.name}/{ps.name}; particles={ps.particleCount}; mode={ps.main.startColor.mode}",asset);
     }
    }
    finally{Object.DestroyImmediate(go);}
   }
   Debug.Log($"Dust layer audit: {checkedLayers} layers, {failures} failures. Simulation/resource check only; inspect pixels in your scene.");
  }
 }
}
