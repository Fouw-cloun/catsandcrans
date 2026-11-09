using System.Collections.Generic;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace CatCooks.CraneLogistics.Editor
{
    public static class CCSupplyCraneSetup
    {
        const string Folder="Assets/CatCooksCraneLogistics/Generated";
        [MenuItem("Tools/CatCooks/Crane Logistics/Create from selected crane(s)")]
        static void Create()
        {
            if(Application.isPlaying) return;
            var sources=new List<MonoBehaviour>();
            foreach(GameObject selected in Selection.gameObjects)
                foreach(MonoBehaviour c in selected.GetComponentsInParent<MonoBehaviour>(true))
                    if(c!=null&&(c.GetType().Name=="TowerCraneVehicle"||c.GetType().Name=="CCBuilderAmbientCrane")&&!sources.Contains(c)) { sources.Add(c);break; }
            if(sources.Count==0) { Debug.LogError("Select scene roots with TowerCraneVehicle or CCBuilderAmbientCrane.");return; }
            EnsureFolder(Folder);
            Material steel=Mat("Steel",new Color(.21f,.28f,.32f)),wood=Mat("Wood",new Color(.7f,.44f,.22f)),yellow=Mat("SafetyYellow",new Color(1,.65f,.08f)),cat=Mat("Cat",new Color(.83f,.8f,.71f));
            var created=new List<Object>();
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Create crane logistics");
            foreach(MonoBehaviour source in sources)
            {
                if(EditorUtility.IsPersistent(source)) continue;
                SerializedObject so=new SerializedObject(source);
                bool ambient=source.GetType().Name=="CCBuilderAmbientCrane";
                Transform tower=Ref<Transform>(so,ambient?"Tower":"_tower"),trolley=Ref<Transform>(so,ambient?"Trolley":"_trolley"),top=Ref<Transform>(so,ambient?"RopeTop":"_ropeTopPoint");
                Component hc=Ref<Component>(so,ambient?"HookVisual":"_hook");
                if(hc==null) hc=Ref<Component>(so,"_hookJoint");
                if(hc==null) hc=Ref<Component>(so,"_hookTransform");
                if(tower==null||trolley==null||top==null||hc==null||!tower.IsChildOf(source.transform)||!trolley.IsChildOf(source.transform)||!top.IsChildOf(source.transform))
                { Debug.LogError("Missing Tower / Trolley / RopeTop / Hook references, or parts outside crane hierarchy.",source);continue; }
                if(source.GetComponentInChildren<SkinnedMeshRenderer>(true)!=null) { Debug.LogError("Use a static mesh crane; skinned geometry is not supported by this copier.",source);continue; }
                Transform root=Empty(source.name+" - Cat logistics",null);
                root.SetPositionAndRotation(source.transform.position,source.transform.rotation);
                var map=new Dictionary<Transform,Transform>();
                Transform visual=Copy(source.transform,root,map);visual.localPosition=Vector3.zero;visual.localRotation=Quaternion.identity;visual.localScale=source.transform.lossyScale;visual.gameObject.SetActive(true);
                if(!map.ContainsKey(hc.transform))
                { Transform extra=Copy(hc.transform,root,map);extra.SetPositionAndRotation(hc.transform.position,hc.transform.rotation);extra.localScale=hc.transform.lossyScale; }
                if(ambient) { Transform oldCargo=Ref<Transform>(so,"CargoVisual");if(oldCargo!=null&&map.ContainsKey(oldCargo)) map[oldCargo].gameObject.SetActive(false); }
                Transform hook=map[hc.transform];hook.SetParent(root,true);hook.gameObject.SetActive(true);
                Undo.AddComponent<NetworkObject>(root.gameObject);
                CCSupplyCrane c=Undo.AddComponent<CCSupplyCrane>(root.gameObject);
                c.Tower=map[tower];c.Trolley=map[trolley];c.RopeTop=map[top];c.HookVisual=hook;
                c.TowerRest=c.Tower.localRotation;c.TrolleyRest=c.Trolley.localPosition;c.HookRest=Quaternion.Inverse(root.rotation)*hook.rotation;
                c.TowerAxis=Vec(so,ambient?"TowerAxis":"_towerAxis",Vector3.up);c.TrolleyAxis=Vec(so,ambient?"TrolleyAxis":"_trolleyAxis",Vector3.right);
                c.TrolleyRange=Vec2(so,ambient?"TrolleyRange":"_trolleyLimit",new Vector2(2,25));
                if(!ambient) c.TrolleyRange-=Vector2.one*Vector3.Dot(trolley.localPosition,c.TrolleyAxis.normalized);
                Vector3 anchor=hc.transform.position;
                if(ambient) c.HookAnchorLocal=Vec(so,"HookAnchorLocal",Vector3.zero);
                else
                {
                    Transform ra=Ref<Transform>(new SerializedObject(hc),"_ropeAnchor");ConfigurableJoint joint=hc.GetComponent<ConfigurableJoint>();
                    if(ra!=null) anchor=ra.position;else if(joint!=null) anchor=hc.transform.TransformPoint(joint.anchor);
                    c.HookAnchorLocal=hc.transform.InverseTransformPoint(anchor);
                }
                c.RopeRange=new Vector2(.5f,150);c.TravelRope=1;
                float rope=Mathf.Clamp(Vector3.Distance(top.position,anchor),2,140);
                c.DockA=new Vector3(0,Mathf.Clamp(0,c.TrolleyRange.x,c.TrolleyRange.y),rope);
                c.DockB=new Vector3(65,Mathf.Clamp(3,c.TrolleyRange.x,c.TrolleyRange.y),Mathf.Max(2,rope-8));
                c.StartDelay=created.Count*8;
                c.Rope=Line("Main rope",root,steel,.035f);
                Transform platform=Empty("Platform - deck surface is local Y zero",root);c.Platform=platform;
                Block("Deck",platform,new Vector3(0,-.12f,0),new Vector3(4,.24f,3),steel);
                for(int i=0;i<5;i++) Block("Plank",platform,new Vector3(0,.015f,(i-2)*.57f),new Vector3(3.8f,.03f,.5f),wood);
                for(int i=0;i<4;i++)
                {
                    Vector3 corner=new Vector3(i%2==0?-1.85f:1.85f,.06f,i<2?-1.35f:1.35f);
                    c.Corners[i]=Empty("Attachment "+i,platform);c.Corners[i].localPosition=corner;
                    Block("Corner lug",c.Corners[i],Vector3.zero,new Vector3(.18f,.22f,.18f),yellow);
                    c.Slings[i]=Line("Sling "+i,root,steel,.025f);
                    c.SlingHooks[i]=Empty("Sling hook "+i,root);
                    // A three-part open hook follows the free end of each sling.
                    Block("Hook shank",c.SlingHooks[i],new Vector3(0,.22f,0),new Vector3(.055f,.28f,.055f),yellow);
                    Block("Hook toe",c.SlingHooks[i],new Vector3(.065f,.1f,0),new Vector3(.16f,.055f,.055f),yellow);
                    Block("Hook tip",c.SlingHooks[i],new Vector3(.13f,.16f,0),new Vector3(.055f,.12f,.055f),yellow);
                }
                    foreach(Transform sh in c.SlingHooks) Combine(sh);
                Combine(platform);
                BoxCollider collider=Undo.AddComponent<BoxCollider>(platform.gameObject);collider.center=new Vector3(0,-.12f,0);collider.size=new Vector3(4,.24f,3);
                Rigidbody body=Undo.AddComponent<Rigidbody>(platform.gameObject);body.isKinematic=true;body.useGravity=false;body.interpolation=RigidbodyInterpolation.Interpolate;
                for(int i=0;i<2;i++)
                {
                    c.Crew[i]=Cat("Rigger "+(i+1)+" - visual slot",root,cat,yellow);
                    Transform cargo=Empty("Cargo "+(i+1)+" - replace children with your model",root);c.Cargo[i]=cargo;
                    Block("Crate",cargo,new Vector3(0,.38f,0),new Vector3(.85f,.76f,.85f),wood);
                    for(int k=0;k<2;k++) Block("Steel strap",cargo,new Vector3((k==0?-.27f:.27f),.38f,0),new Vector3(.055f,.78f,.87f),steel);
                    Combine(cargo);
                }
                Transform seat=Ref<Transform>(so,ambient?"OperatorVisual":"_seatPoint");
                Transform parent=seat!=null&&map.ContainsKey(seat)?map[seat]:c.Tower;
                if(ambient&&seat!=null&&map.ContainsKey(seat)) map[seat].gameObject.SetActive(false);
                Transform op=Cat("Cabin operator - visual slot",ambient?c.Tower:parent,cat,yellow);
                if(seat!=null) op.position=seat.position;
                Vector3 scale=op.parent.lossyScale;op.localScale=new Vector3(1/Mathf.Max(.001f,Mathf.Abs(scale.x)),1/Mathf.Max(.001f,Mathf.Abs(scale.y)),1/Mathf.Max(.001f,Mathf.Abs(scale.z)));
                // Disable source instead of destroying it: one Undo restores the original setup.
                Undo.RecordObject(source.gameObject,"Disable original crane");source.gameObject.SetActive(false);
                if(!hc.transform.IsChildOf(source.transform)) { Undo.RecordObject(hc.gameObject,"Disable original hook");hc.gameObject.SetActive(false); }
                c.Preview(c.DockA);EditorUtility.SetDirty(c);created.Add(root.gameObject);
                EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            }
            Undo.CollapseUndoOperations(group);Selection.objects=created.ToArray();AssetDatabase.SaveAssets();
            Debug.Log("Crane logistics created, Run OFF. Configure docks, travel clearance and storage. Save scene before Host. Original crane disabled (Undo available).");
        }
        static T Ref<T>(SerializedObject s,string n) where T:Object { var p=s.FindProperty(n);return p==null?null:p.objectReferenceValue as T; }
        static Vector3 Vec(SerializedObject s,string n,Vector3 fallback) { var p=s.FindProperty(n);return p==null?fallback:p.vector3Value; }
        static Vector2 Vec2(SerializedObject s,string n,Vector2 fallback) { var p=s.FindProperty(n);return p==null?fallback:p.vector2Value; }
        static Transform Empty(string name,Transform parent)
        { GameObject g=new GameObject(name);Undo.RegisterCreatedObjectUndo(g,"Create crane part");g.transform.SetParent(parent,false);return g.transform; }
        static Transform Copy(Transform source,Transform parent,Dictionary<Transform,Transform> map)
        {
            Transform t=Empty(source.name,parent);map[source]=t;t.localPosition=source.localPosition;t.localRotation=source.localRotation;t.localScale=source.localScale;t.gameObject.SetActive(source.gameObject.activeSelf);
            MeshFilter mf=source.GetComponent<MeshFilter>();MeshRenderer mr=source.GetComponent<MeshRenderer>();bool skip=false;
            foreach(MonoBehaviour b in source.GetComponents<MonoBehaviour>()) if(b!=null&&b.GetType().Name=="CraneCableVisual") skip=true;
            if(!skip&&mf!=null&&mr!=null&&mr.enabled)
            { Undo.AddComponent<MeshFilter>(t.gameObject).sharedMesh=mf.sharedMesh;MeshRenderer r=Undo.AddComponent<MeshRenderer>(t.gameObject);r.sharedMaterials=mr.sharedMaterials;r.shadowCastingMode=ShadowCastingMode.Off; }
            foreach(Transform child in source) Copy(child,t,map);
            // Keep only the first LOD if the source has a LODGroup.
            LODGroup lod=source.GetComponent<LODGroup>();if(lod!=null)
            { LOD[] levels=lod.GetLODs();var keep=new HashSet<Renderer>();if(levels.Length>0) foreach(Renderer r in levels[0].renderers) if(r!=null) keep.Add(r);
              for(int i=1;i<levels.Length;i++) foreach(Renderer r in levels[i].renderers) if(r!=null&&!keep.Contains(r)&&map.ContainsKey(r.transform)) { MeshRenderer copy=map[r.transform].GetComponent<MeshRenderer>();if(copy!=null) copy.enabled=false; } }
            return t;
        }
        static Transform Block(string name,Transform p,Vector3 pos,Vector3 size,Material m,PrimitiveType type=PrimitiveType.Cube)
        {
            GameObject g=GameObject.CreatePrimitive(type);Undo.RegisterCreatedObjectUndo(g,"Create crane visual");g.name=name;g.transform.SetParent(p,false);g.transform.localPosition=pos;g.transform.localScale=size;
            Undo.DestroyObjectImmediate(g.GetComponent<Collider>());MeshRenderer r=g.GetComponent<MeshRenderer>();r.sharedMaterial=m;r.shadowCastingMode=ShadowCastingMode.Off;return g.transform;
        }
        static Transform Cat(string name,Transform parent,Material m,Material hat)
        {
            Transform t=Empty(name,parent);Block("Capsule",t,new Vector3(0,.42f,0),new Vector3(.4f,.42f,.4f),m,PrimitiveType.Capsule);
            Block("Helmet",t,new Vector3(0,.8f,0),new Vector3(.46f,.12f,.46f),hat);
            Block("Face direction",t,new Vector3(0,.64f,.21f),new Vector3(.18f,.05f,.03f),hat);Combine(t);return t;
        }
        static LineRenderer Line(string name,Transform root,Material m,float width)
        {
            LineRenderer l=Undo.AddComponent<LineRenderer>(Empty(name,root).gameObject);l.sharedMaterial=m;l.useWorldSpace=true;l.positionCount=2;l.startWidth=l.endWidth=width;l.numCapVertices=0;l.shadowCastingMode=ShadowCastingMode.Off;l.receiveShadows=false;return l;
        }
        static void EnsureFolder(string path)
        {
            string[] parts=path.Split('/');string current=parts[0];for(int i=1;i<parts.Length;i++) { string next=current+"/"+parts[i];if(!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current,parts[i]);current=next; }
        }
        static Material Mat(string name,Color color)
        {
            string path=Folder+"/"+name+".mat";Material m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m!=null) return m;
            Shader shader=Shader.Find("Universal Render Pipeline/Unlit");if(shader==null) shader=Shader.Find("Unlit/Color");if(shader==null) shader=Shader.Find("Standard");
            m=new Material(shader){name=name,color=color,enableInstancing=true};AssetDatabase.CreateAsset(m,path);return m;
        }
        // Combine generated static parts per shared material; preserve attachment transforms.
        static void Combine(Transform root)
        {
            var groups=new Dictionary<Material,List<CombineInstance>>();var originals=new List<MeshRenderer>();
            foreach(MeshFilter f in root.GetComponentsInChildren<MeshFilter>())
            { MeshRenderer r=f.GetComponent<MeshRenderer>();if(r==null||r.sharedMaterial==null||f.sharedMesh==null) continue;
              if(!groups.TryGetValue(r.sharedMaterial,out var list)) { list=new List<CombineInstance>();groups.Add(r.sharedMaterial,list); }
              list.Add(new CombineInstance{mesh=f.sharedMesh,transform=root.worldToLocalMatrix*f.transform.localToWorldMatrix});originals.Add(r); }
            foreach(var pair in groups)
            { Mesh mesh=new Mesh{name=root.name+" "+pair.Key.name};mesh.CombineMeshes(pair.Value.ToArray());
              string path=AssetDatabase.GenerateUniqueAssetPath(Folder+"/Combined.asset");AssetDatabase.CreateAsset(mesh,path);
              Transform t=Empty("Combined "+pair.Key.name,root);Undo.AddComponent<MeshFilter>(t.gameObject).sharedMesh=mesh;MeshRenderer r=Undo.AddComponent<MeshRenderer>(t.gameObject);r.sharedMaterial=pair.Key;r.shadowCastingMode=ShadowCastingMode.Off; }
            foreach(MeshRenderer r in originals) { Undo.DestroyObjectImmediate(r.GetComponent<MeshFilter>());Undo.DestroyObjectImmediate(r); }
        }
    }
    [CustomEditor(typeof(CCSupplyCrane))]
    public sealed class CCSupplyCraneInspector:UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();CCSupplyCrane c=(CCSupplyCrane)target;
            if(Application.isPlaying) { EditorGUILayout.LabelField("Status",c.PhaseName);Repaint();return; }
            EditorGUILayout.HelpBox("X: yaw degrees, Y: trolley LOCAL offset, Z: WORLD rope length. Platform top = RopeTop Y - rope - SlingHeight. Keep root scale 1. Source was disabled, not deleted.",MessageType.Info);
            if(!c.Valid(out string error)) EditorGUILayout.HelpBox(error,MessageType.Error);
            if(GUILayout.Button("Apply travel height from Highest Obstacle Marker"))
            {
                if(c.HighestObstacleMarker==null)Debug.LogWarning("Assign Highest Obstacle Marker at the top of the tallest obstacle on this route.",c);
                else
                {
                    float height=c.HighestObstacleMarker.position.y+Mathf.Max(.3f,c.TravelClearance);
                    if(c.TryTravelRopeForHeight(c.DockA,c.DockB,height,out float length,out string reason))
                    { Undo.RecordObject(c,"Set crane travel height");c.TravelRope=length;EditorUtility.SetDirty(c);EditorSceneManager.MarkSceneDirty(c.gameObject.scene);Debug.Log("Travel deck height >= "+height+" m; rope = "+length+" m. Verify full route and platform underside clearance.",c); }
                    else Debug.LogWarning(reason,c);
                }
            }
            if(GUILayout.Button("Preview dock A")) Preview(c,c.DockA);
            if(GUILayout.Button("Preview dock B")) Preview(c,c.DockB);
            if(GUILayout.Button("Preview travel above A")) Preview(c,new Vector3(c.DockA.x,c.DockA.y,c.TravelRope));
            if(GUILayout.Button("Fit dock A HEIGHT to selected floor Y (use marker field below)")) Fit(c,true);
            if(GUILayout.Button("Fit dock B HEIGHT to marker Y")) Fit(c,false);
            _heightMarker=(Transform)EditorGUILayout.ObjectField("Floor height marker",_heightMarker,typeof(Transform),true);
            EditorGUILayout.HelpBox("Height fitting changes only rope length. Set yaw / trolley for XZ alignment. No NavMesh needed for this dedicated crew. Do not add NetworkTransform to moving child visuals.",MessageType.None);
        }
        void OnSceneGUI()
        {
            CCSupplyCrane c=(CCSupplyCrane)target;
            if(Application.isPlaying||c.Tower==null||c.Trolley==null||c.RopeTop==null) return;
            DockHandle(c,true);DockHandle(c,false);
        }
        static void DockHandle(CCSupplyCrane c,bool a)
        {
            Vector3 pose=a?c.DockA:c.DockB, point=c.DeckAt(pose);
            Handles.color=a?Color.cyan:Color.yellow;Handles.Label(point+Vector3.up,a?"A — deck surface":"B — deck surface");
            EditorGUI.BeginChangeCheck();Vector3 desired=Handles.PositionHandle(point,Quaternion.identity);
            if(EditorGUI.EndChangeCheck())
            {
                Vector3 solved=pose;
                // Local numerical inverse kinematics: accommodates imported trolley axes and scale.
                for(int i=0;i<24;i++)
                {
                    Vector3 p=c.TopAt(solved),dx=c.TopAt(solved+new Vector3(.1f,0,0))-p,dy=c.TopAt(solved+new Vector3(0,.01f,0))-p;
                    dx/=.1f;dy/=.01f;Vector3 e=desired-p;float det=dx.x*dy.z-dy.x*dx.z;
                    if(Mathf.Abs(det)<.00001f) break;
                    float yaw=(e.x*dy.z-dy.x*e.z)/det,slide=(dx.x*e.z-e.x*dx.z)/det;
                    solved.x+=Mathf.Clamp(yaw,-15,15);solved.y=Mathf.Clamp(solved.y+Mathf.Clamp(slide,-2,2),c.TrolleyRange.x,c.TrolleyRange.y);
                    if(new Vector2(e.x,e.z).sqrMagnitude<.0001f) break;
                }
                solved.z=Mathf.Clamp(c.TopAt(solved).y-desired.y-c.SlingHeight,c.RopeRange.x,c.RopeRange.y);
                Undo.RecordObject(c,"Move crane dock");if(a)c.DockA=solved;else c.DockB=solved;EditorUtility.SetDirty(c);
                EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
            }
            Vector3 offset=a?c.StorageA:c.StorageB;Vector3 stock=c.DeckAt(a?c.DockA:c.DockB)+c.transform.TransformDirection(offset);
            Handles.color=Color.magenta;Handles.Label(stock+Vector3.up,a?"A — cargo / crew staging":"B — cargo / crew staging");
            EditorGUI.BeginChangeCheck();Vector3 next=Handles.PositionHandle(stock,Quaternion.identity);
            if(EditorGUI.EndChangeCheck())
            { Undo.RecordObject(c,"Move cargo staging");Vector3 local=c.transform.InverseTransformDirection(next-c.DeckAt(a?c.DockA:c.DockB));if(a)c.StorageA=local;else c.StorageB=local;EditorUtility.SetDirty(c);EditorSceneManager.MarkSceneDirty(c.gameObject.scene); }
        }
        Transform _heightMarker;
        void Fit(CCSupplyCrane c,bool a)
        {
            if(_heightMarker==null) { Debug.LogWarning("Assign Floor height marker first.");return; }
            Undo.RecordObject(c,"Fit dock height");Vector3 p=a?c.DockA:c.DockB;p.z=c.TopAt(p).y-_heightMarker.position.y-c.SlingHeight;
            if(a)c.DockA=p;else c.DockB=p;EditorUtility.SetDirty(c);Preview(c,p);
        }
        static void Preview(CCSupplyCrane c,Vector3 pose)
        {
            var objects=new List<Object>();foreach(Transform t in c.GetComponentsInChildren<Transform>(true)) objects.Add(t);
            Undo.RecordObjects(objects.ToArray(),"Preview crane pose");c.Preview(pose);EditorSceneManager.MarkSceneDirty(c.gameObject.scene);SceneView.RepaintAll();
        }
    }
}
