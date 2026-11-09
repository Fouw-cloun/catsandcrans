using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace CatCooks.CraneLogistics.Editor
{
    public static class CCStockVisualSetup
    {
        [MenuItem("Tools/CatCooks/Crane Logistics/Create stock visuals for selected zone")]
        static void CreateSelected()
        {
            if(Application.isPlaying)return;
            CCWorkZone z=Selection.activeGameObject!=null?Selection.activeGameObject.GetComponentInParent<CCWorkZone>():null;
            if(z==null){Debug.LogWarning("Select a Supply, Receiving or Work zone.");return;}
            Create(z);
        }
        public static void Create(CCWorkZone zone)
        {
            if(Application.isPlaying||zone==null||EditorUtility.IsPersistent(zone))return;
            if(zone.Kind==CCZoneKind.Exclusion||zone.Kind==CCZoneKind.Rest){Debug.LogWarning("This zone does not store materials.",zone);return;}
            string root=PackageRoot();if(string.IsNullOrEmpty(root)){Debug.LogError("Cannot locate CCZoneStockVisual.cs in the project.");return;}
            string folder=root+"/Generated/StockV3";Folder(folder);
            GameObject prefab=Prefab(zone.Resource,root,folder);
            if(prefab==null)return;
            Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Create zone stock visuals");
            CCZoneStockVisual view=zone.GetComponent<CCZoneStockVisual>();if(view==null)view=Undo.AddComponent<CCZoneStockVisual>(zone.gameObject);
            // Never erase user geometry; rebuilding only our recorded generated root is explicit here.
            if(view.VisualRoot!=null)Undo.DestroyObjectImmediate(view.VisualRoot.gameObject);
            var go=new GameObject("Stock visuals - move beside staging, keep passage clear");Undo.RegisterCreatedObjectUndo(go,"Create stock root");go.transform.SetParent(zone.transform,false);
            go.transform.position=zone.StagingPosition+zone.transform.forward*3;go.transform.rotation=zone.transform.rotation;
            Undo.RecordObject(view,"Set stock visuals");view.Zone=zone;view.VisualRoot=go.transform;
            int slots=Mathf.Clamp(zone.Capacity,1,32);view.UnitsPerSlot=Mathf.Max(1,Mathf.CeilToInt(zone.Capacity/(float)slots));view.Slots=new GameObject[slots];
            int cols=Mathf.Min(4,slots);
            for(int i=0;i<slots;i++)
            {
                GameObject model=(GameObject)PrefabUtility.InstantiatePrefab(prefab,go.transform);Undo.RegisterCreatedObjectUndo(model,"Create stock slot");model.name="Batch "+(i+1);
                model.transform.localPosition=new Vector3((i%cols-(cols-1)*.5f)*1.4f,0,(i/cols)*1.1f);model.transform.localRotation=Quaternion.identity;view.Slots[i]=model;
            }
            view.PreviewInitial();EditorUtility.SetDirty(view);EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);Undo.CollapseUndoOperations(undo);Selection.activeGameObject=zone.gameObject;
            Debug.Log("Created visible stock. Move the Stock visuals child beside the walking path. One slot = "+view.UnitsPerSlot+" units. Generated prefab can also replace a Cargo child.",view);
        }
        static string PackageRoot()
        {
            foreach(string guid in AssetDatabase.FindAssets("CCZoneStockVisual t:MonoScript"))
            { string p=AssetDatabase.GUIDToAssetPath(guid);if(Path.GetFileName(p)=="CCZoneStockVisual.cs")return Path.GetDirectoryName(Path.GetDirectoryName(p)).Replace('\\','/'); }
            return null;
        }
        static void Folder(string path)
        { string[] pieces=path.Split('/');string current=pieces[0];for(int i=1;i<pieces.Length;i++){string next=current+"/"+pieces[i];if(!AssetDatabase.IsValidFolder(next))AssetDatabase.CreateFolder(current,pieces[i]);current=next;} }
        static GameObject Prefab(CCZoneResource resource,string root,string folder)
        {
            string path=folder+"/Batch_"+resource+".prefab";GameObject existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(existing!=null)return existing;
            string meshName=resource==CCZoneResource.Bricks?"CC_BrickPile":resource==CCZoneResource.Boards?"CC_PlankPile":resource==CCZoneResource.WindowFrames?"CC_WindowFrame":resource==CCZoneResource.Sand?"CC_SandPile_1":null;
            Material material=MaterialFor(resource,folder);GameObject rootObj=new GameObject("Batch_"+resource);
            try
            {
                GameObject source=meshName!=null?AssetDatabase.LoadAssetAtPath<GameObject>(root+"/Models_v3/"+meshName+".obj"):null;
                if(meshName!=null&&source==null){Debug.LogError("Missing Models_v3/"+meshName+".obj. Import Models_v3 from the full archive.");return null;}
                if(source!=null)
                {
                    foreach(MeshFilter f in source.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if(f.sharedMesh==null)continue;GameObject mesh=new GameObject(f.name);mesh.transform.SetParent(rootObj.transform,false);
                        // OBJ source files shipped here use metre units and Y-up; importer transform is retained.
                        mesh.transform.localPosition=source.transform.InverseTransformPoint(f.transform.position);
                        mesh.transform.localRotation=Quaternion.Inverse(source.transform.rotation)*f.transform.rotation;
                        mesh.transform.localScale=f.transform.lossyScale;
                        mesh.AddComponent<MeshFilter>().sharedMesh=f.sharedMesh;var mr=mesh.AddComponent<MeshRenderer>();mr.sharedMaterial=material;mr.shadowCastingMode=ShadowCastingMode.Off;
                    }
                    if(resource==CCZoneResource.Sand)rootObj.transform.localScale=Vector3.one*.35f;
                }
                else
                {
                    GameObject cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.transform.SetParent(rootObj.transform,false);cube.transform.localPosition=Vector3.up*.4f;cube.transform.localScale=Vector3.one*.8f;
                    Object.DestroyImmediate(cube.GetComponent<Collider>());var mr=cube.GetComponent<MeshRenderer>();mr.sharedMaterial=material;mr.shadowCastingMode=ShadowCastingMode.Off;
                }
                return PrefabUtility.SaveAsPrefabAsset(rootObj,path);
            }
            finally{Object.DestroyImmediate(rootObj);}
        }
        static Material MaterialFor(CCZoneResource resource,string folder)
        {
            string path=folder+"/"+resource+".mat";Material m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m!=null)return m;
            Shader shader=Shader.Find("Universal Render Pipeline/Unlit");if(shader==null)shader=Shader.Find("Unlit/Color");if(shader==null)shader=Shader.Find("Standard");
            Color color=resource==CCZoneResource.Bricks?new Color(.66f,.25f,.16f):resource==CCZoneResource.Boards?new Color(.63f,.43f,.22f):resource==CCZoneResource.Sand?new Color(.83f,.70f,.42f):resource==CCZoneResource.WindowFrames?new Color(.55f,.69f,.73f):new Color(.48f,.35f,.2f);
            m=new Material(shader){name=resource.ToString(),color=color,enableInstancing=true};AssetDatabase.CreateAsset(m,path);return m;
        }
    }
    [CustomEditor(typeof(CCZoneStockVisual))]
    public sealed class CCZoneStockVisualInspector:UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();var v=(CCZoneStockVisual)target;
            EditorGUILayout.HelpBox("Видимый ДОСТУПНЫЙ запас: при резервировании рейса уменьшается, при завершении доставки появляется. Нет физических предметов. Переместите VisualRoot сбоку от Staging; оставьте проход котам.",MessageType.Info);
            if(!Application.isPlaying&&GUILayout.Button("Preview Initial Stock"))v.PreviewInitial();
        }
    }
}
