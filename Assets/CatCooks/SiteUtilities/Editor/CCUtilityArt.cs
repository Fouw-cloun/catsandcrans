using UnityEditor;
using UnityEngine;
using System.IO;

namespace CatCooks.SiteTools
{
    public static class CCUtilityArt
    {
        public const string Folder = "Assets/CatCooks/SiteUtilities/Generated";
        public static Vector3 V(float x,float y,float z) => new Vector3(x,y,z);
        public static Transform Node(Transform parent,string name,Vector3 position)
        {
            var t=new GameObject(name).transform;
            t.SetParent(parent,false);
            t.localPosition=position;
            return t;
        }
        public static Material Paint(string hex,bool glow=false)
        {
            var path=Folder+"/"+hex+(glow?"_Lit":"")+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat!=null) return mat;
            if(!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/CatCooks/SiteUtilities","Generated");
            ColorUtility.TryParseHtmlString("#"+hex,out var color);
            mat=new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color=color;
            if(glow){mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor",color*2.8f);}
            AssetDatabase.CreateAsset(mat,path);
            return mat;
        }
        public static Transform Shape(Transform parent,string name,PrimitiveType type,Vector3 pos,Vector3 size,string color,bool solid=false,bool glow=false)
        {
            var go=GameObject.CreatePrimitive(type);
            go.name=name;
            var t=go.transform;
            t.SetParent(parent,false);
            t.localPosition=pos;
            t.localScale=size;
            go.GetComponent<Renderer>().sharedMaterial=Paint(color,glow);
            if(!solid) Object.DestroyImmediate(go.GetComponent<Collider>());
            return t;
        }
        public static Transform Box(Transform p,string n,Vector3 pos,Vector3 size,string c,bool solid=false,bool glow=false)
            => Shape(p,n,PrimitiveType.Cube,pos,size,c,solid,glow);
        public static Transform Rod(Transform p,string n,Vector3 a,Vector3 b,float r,string c,bool solid=false)
        {
            var d=b-a;
            var t=Shape(p,n,PrimitiveType.Cylinder,(a+b)*.5f,V(2*r,d.magnitude*.5f,2*r),c,solid);
            t.localRotation=Quaternion.FromToRotation(Vector3.up,d.normalized);
            return t;
        }
        public static Transform Art(Transform parent,string path,Vector3 pos,float diameter)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(prefab==null)throw new System.InvalidOperationException("Импортируй модель "+path);
            var holder=Node(parent,prefab.name,pos);
            var obj=Object.Instantiate(prefab,holder,false);
            var bounds=new Bounds();bool first=true;
            foreach(var mf in obj.GetComponentsInChildren<MeshFilter>()){
                if(mf.sharedMesh==null)continue;
                var matrix=holder.worldToLocalMatrix*mf.transform.localToWorldMatrix;
                var box=mf.sharedMesh.bounds;
                for(int i=0;i<8;i++){
                    var local=box.center+Vector3.Scale(box.extents,V((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    var point=matrix.MultiplyPoint3x4(local);
                    if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);
                }
                var renderer=mf.GetComponent<Renderer>();
                var mats=renderer.sharedMaterials;
                for(int j=0;j<mats.Length;j++)
                    if(mats[j]!=null && mats[j].name.Length==6 && ColorUtility.TryParseHtmlString("#"+mats[j].name,out _))
                        mats[j]=Paint(mats[j].name);
                renderer.sharedMaterials=mats;
            }
            obj.transform.localPosition-=bounds.center;
            holder.localScale=Vector3.one*(diameter/Mathf.Max(.001f,bounds.size.x,bounds.size.y,bounds.size.z));
            return holder;
        }
        public static void Layer(Transform t)
        {
            int layer=LayerMask.NameToLayer("Interactable");
            if(layer>=0)t.gameObject.layer=layer;
        }
        public static void Set(Object component,string field,Object value)
        {
            var so=new SerializedObject(component);
            so.FindProperty(field).objectReferenceValue=value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        public static void Int(Object component,string field,int value)
        {
            var so=new SerializedObject(component);
            so.FindProperty(field).intValue=value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        public static void Finish(Transform root)
        {
            Undo.RegisterCreatedObjectUndo(root.gameObject,"Создать "+root.name);
            Selection.activeGameObject=root.gameObject;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        }
    }
}
