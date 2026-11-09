using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace CatCooks.CityV2.Editor
{
    public static class CCCityV2RoofBaker
    {
        [MenuItem("Tools/CatCooks/City V2/Bake Selected Roof Decoration Root")]
        static void Bake()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) {Debug.LogWarning("Выйди из Play Mode.");return;}
            var root=Selection.activeGameObject;
            if(root==null || EditorUtility.IsPersistent(root)) {Debug.LogWarning("Выбери отдельный корень только декораций крыши в Hierarchy.");return;}
            try
            {
                // Require a decoration-only root; do not bake game behaviour or physics away.
                foreach(var c in root.GetComponentsInChildren<Component>(true))
                    if(c==null || !(c is Transform || c is MeshFilter || c is MeshRenderer || c is LODGroup))
                        throw new InvalidOperationException("В корне есть компоненты кроме геометрии/LOD. Выбери отдельную группу декораций без скриптов и коллайдеров.");
                var visible=root.GetComponentsInChildren<MeshRenderer>(false).Where(r=>r.enabled).ToArray();
                if(visible.Length==0) throw new InvalidOperationException("Нет активных декораций.");
                var materials=visible.SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
                if(materials.Length!=1 || materials[0]==null || materials[0].shader.name!="CatCooks/CityV2/Atlas")
                    throw new InvalidOperationException("У всех деталей должен быть один и тот же shared material CityV2/Atlas. При импорте Rooftop Kit включи Reuse selected city material.");
                var lists=new[]{new List<CombineInstance>(),new List<CombineInstance>()};
                var grouped=new HashSet<Renderer>();
                foreach(var group in root.GetComponentsInChildren<LODGroup>(false))
                {
                    if(!group.enabled) throw new InvalidOperationException("В группе есть выключенный LODGroup.");
                    var lods=group.GetLODs();if(lods.Length!=2) throw new InvalidOperationException("Ожидаются группы с двумя LOD.");
                    for(int l=0;l<2;l++) foreach(var r in lods[l].renderers)
                    {
                        if(r==null || !r.gameObject.activeInHierarchy || !r.enabled) continue;
                        if(!grouped.Add(r)) throw new InvalidOperationException("Один renderer указан в нескольких LOD: "+r.name);
                        if(!visible.Contains(r)) throw new InvalidOperationException("LOD ссылается на объект вне выбранного корня.");
                        lists[l].Add(Part(root.transform,r));
                    }
                }
                foreach(var r in visible) if(!grouped.Contains(r))
                {var part=Part(root.transform,r);lists[0].Add(part);lists[1].Add(part);}
                if(lists.Any(l=>l.Count==0)) throw new InvalidOperationException("Один из LOD пуст.");
                string parent="Assets/CatCooks/CityV2/Generated";Directory.CreateDirectory(parent);AssetDatabase.Refresh();
                string folder=AssetDatabase.GenerateUniqueAssetPath(parent+"/RoofBatch");Directory.CreateDirectory(folder);AssetDatabase.Refresh();
                GameObject combined=null;
                try
                {
                    combined=new GameObject(root.name+"_Baked");
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(combined,root.scene);
                    combined.transform.SetParent(root.transform.parent,false);
                    combined.transform.localPosition=root.transform.localPosition;combined.transform.localRotation=root.transform.localRotation;combined.transform.localScale=root.transform.localScale;
                    var output=new LOD[2];
                    for(int l=0;l<2;l++)
                    {
                        var mesh=new Mesh{name=combined.name+"_LOD"+l,indexFormat=IndexFormat.UInt32};
                        mesh.CombineMeshes(lists[l].ToArray(),true,true,false);mesh.RecalculateBounds();
                        AssetDatabase.CreateAsset(mesh,folder+"/LOD"+l+".asset");
                        var go=new GameObject("LOD"+l);go.transform.SetParent(combined.transform,false);
                        go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=materials[0];
                        renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
                        output[l]=new LOD(l==0?.12f:.002f,new Renderer[]{renderer});
                    }
                    var lodGroup=combined.AddComponent<LODGroup>();lodGroup.fadeMode=LODFadeMode.None;lodGroup.SetLODs(output);lodGroup.RecalculateBounds();
                    PrefabUtility.SaveAsPrefabAsset(combined,folder+"/RoofBatch.prefab");
                    Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Bake roof decoration");
                    Undo.RegisterCreatedObjectUndo(combined,"Create roof batch");Undo.RecordObject(root,"Disable source decorations");root.SetActive(false);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(root);EditorSceneManager.MarkSceneDirty(root.scene);AssetDatabase.SaveAssets();Selection.activeGameObject=combined;
                    Debug.Log("Roof batch: один renderer на LOD, один материал. Исходная группа выключена, не удалена. Объединяй по крыше/кварталу, не весь горизонт в один меш.");
                }
                catch {if(combined!=null) UnityEngine.Object.DestroyImmediate(combined);throw;}
            }
            catch(Exception e) {Debug.LogError("City V2 Roof Bake: "+e.Message);}
        }
        static CombineInstance Part(Transform root,Renderer renderer)
        {
            var filter=renderer.GetComponent<MeshFilter>();var mesh=filter!=null?filter.sharedMesh:null;
            if(mesh==null || !mesh.isReadable || mesh.subMeshCount!=1 || renderer.sharedMaterials.Length!=1)
                throw new InvalidOperationException("Нужен читаемый односекционный меш: "+renderer.name+". Готовый JSON-импорт City V2 это поддерживает.");
            Matrix4x4 matrix=root.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
            if(matrix.determinant<=0) throw new InvalidOperationException("Отрицательный/нулевой масштаб детали: "+renderer.name+". Используй положительный масштаб.");
            return new CombineInstance{mesh=mesh,subMeshIndex=0,transform=matrix};
        }
    }
}
