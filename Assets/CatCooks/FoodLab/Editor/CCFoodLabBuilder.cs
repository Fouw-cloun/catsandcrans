using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatCooks.FoodLab.Editor
{
    public static class CCFoodLabBuilder
    {
        const string Root="Assets/CatCooks/FoodLab/Generated";
        [MenuItem("Tools/CatCooks/Food Lab/Create laboratory")]
        static void Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode){Debug.LogError("Выйди из Play Mode.");return;}
            Shader foodShader=Shader.Find("CatCooks/Food Lab/Toon Mass"),lit=Shader.Find("Universal Render Pipeline/Lit");
            if(foodShader==null||lit==null||ShaderUtil.ShaderHasError(foodShader))
            {Debug.LogError("Нужен URP и импорт папки FoodLab/Shaders без ошибок. Проверь Console.");return;}
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            Directory.CreateDirectory(Root);AssetDatabase.Refresh();
            string folder=AssetDatabase.GenerateUniqueAssetPath(Root+"/Laboratory");Directory.CreateDirectory(folder);Directory.CreateDirectory(folder+"/Profiles");Directory.CreateDirectory(folder+"/Meshes");AssetDatabase.Refresh();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.48f,.48f,.42f);RenderSettings.fog=false;
            var sun=new GameObject("Laboratory sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.25f;sun.color=new Color(1,.91f,.78f);sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(48,-35,0);RenderSettings.sun=sun;
            var cam=new GameObject("Food Lab Camera").AddComponent<Camera>();cam.tag="MainCamera";cam.transform.position=new Vector3(1.1f,3.1f,-4.8f);cam.transform.LookAt(new Vector3(.2f,.6f,0));cam.nearClipPlane=.03f;cam.farClipPlane=40;cam.backgroundColor=new Color(.16f,.22f,.23f);cam.clearFlags=CameraClearFlags.SolidColor;
            var listener=cam.gameObject.AddComponent<AudioListener>();
            var cameraRig=cam.gameObject.AddComponent<CCFoodLabCamera>();
            var world=new GameObject("CCFoodWorld — network root").AddComponent<CCFoodWorld>();
            world.domain=new Bounds(new Vector3(0,1.55f,0),new Vector3(6,2.6f,4));
            var profiles=new List<CCFoodProfile>();
            profiles.Add(Profile(folder,"01_Broth","Бульон",FoodBehaviour.Fluid,new Color(.93f,.62f,.19f),.15f,.04f,.01f,0,0,2.5f,.14f));
            profiles.Add(Profile(folder,"02_TomatoSauce","Томатный соус",FoodBehaviour.Fluid,new Color(.81f,.12f,.045f),8,.3f,.15f,0,0,2.5f,.24f));
            profiles.Add(Profile(folder,"03_Puree","Пюре",FoodBehaviour.Paste,new Color(.98f,.77f,.4f),10,.2f,.55f,.00018f,6,1.8f,.7f));
            profiles.Add(Profile(folder,"04_Chopped","Рубленые овощи",FoodBehaviour.Granular,new Color(.85f,.38f,.075f),.1f,.02f,.9f,0,0,2,.95f));
            profiles.Add(Profile(folder,"05_Batter","Жидкое тесто",FoodBehaviour.Fluid,new Color(.95f,.8f,.51f),15,.4f,.18f,0,0,2.5f,.4f));
            profiles.Add(Profile(folder,"06_DoughSoft","Мягкое тесто",FoodBehaviour.Dough,new Color(.88f,.66f,.37f),5,.2f,.6f,.000035f,3,2.4f,.7f));
            profiles.Add(Profile(folder,"07_DoughElastic","Упругое тесто",FoodBehaviour.Dough,new Color(.97f,.82f,.54f),2,.2f,.65f,.000008f,.08f,3.2f,.65f));
            profiles.Add(Profile(folder,"08_Concrete","Раствор с крошкой",FoodBehaviour.Paste,new Color(.48f,.5f,.44f),12,.2f,.8f,.00012f,5,1.7f,.95f));
            world.presets=profiles.ToArray();
            Material surfaceMat=Mat(folder,"Food_Surface",foodShader,Color.white);
            Material flecks=Mat(folder,"Food_Inclusions",lit,Color.white);flecks.enableInstancing=true;
            Material bubble=Mat(folder,"Food_Bubbles",lit,new Color(1,.9f,.66f));bubble.enableInstancing=true;
            Material wood=Mat(folder,"Table",lit,new Color(.43f,.29f,.19f));
            Material vessel=Mat(folder,"Ceramic",lit,new Color(.2f,.48f,.47f));
            Material metal=Mat(folder,"Pan",lit,new Color(.28f,.3f,.31f));
            var table=GameObject.CreatePrimitive(PrimitiveType.Cube);table.name="Table — analytic collision";table.transform.position=new Vector3(0,.5f,0);table.transform.localScale=new Vector3(5.8f,.1f,3.4f);table.GetComponent<Renderer>().sharedMaterial=wood;
            var tableBoundary=new GameObject("Table collision").AddComponent<CCFoodBoundary>();tableBoundary.transform.position=table.transform.position;tableBoundary.world=world;tableBoundary.boxSize=table.transform.localScale;
            world.Register(tableBoundary);
            var surface=new GameObject("Mass surface").AddComponent<CCFoodSurface>();surface.world=world;surface.material=surfaceMat;surface.inclusionMaterial=flecks;
            surface.transform.SetParent(world.transform,false);
            surface.inclusionMesh=PrimitiveMesh(PrimitiveType.Cube,folder+"/Meshes/Chunk.asset");
            Mesh sphere=PrimitiveMesh(PrimitiveType.Sphere,folder+"/Meshes/Bubble.asset");
            var spawns=new List<Transform>();var boil=new List<CCFoodBoil>();
            Vector3[] positions={new Vector3(-1.55f,.59f,.55f),new Vector3(-.55f,.59f,.55f),new Vector3(.65f,.59f,.55f),new Vector3(-1.05f,.59f,-.6f),new Vector3(1.55f,.59f,.55f),new Vector3(1.55f,.59f,-.55f)};
            for(int i=0;i<positions.Length;i++)
            {
                Transform vesselTransform=null;
                if(i!=3)
                {
                    float radius=i<2?.32f:i==2?.34f:.19f,height=i<2?.3f:i==2?.09f:.28f;
                    var go=new GameObject(i==2?"Pan":i<2?"Pot "+(i+1):"Jar "+(i-3));go.transform.position=positions[i];
                    Mesh m=MakeBowl(radius,height,.025f);AssetDatabase.CreateAsset(m,folder+"/Meshes/Container_"+i+".asset");
                    go.AddComponent<MeshFilter>().sharedMesh=m;go.AddComponent<MeshRenderer>().sharedMaterial=i==2?metal:vessel;
                    var bound=go.AddComponent<CCFoodBoundary>();bound.world=world;bound.shape=FoodBoundaryShape.Bowl;bound.radius=radius;bound.height=height;world.Register(bound);vesselTransform=go.transform;
                    var fx=go.AddComponent<CCFoodBoil>();fx.world=world;fx.station=i;fx.radius=radius;fx.bubbleMesh=sphere;fx.bubbleMaterial=bubble;boil.Add(fx);
                }
                var spawn=new GameObject("Spawn "+i).transform;spawn.position=positions[i]+Vector3.up*.06f;if(vesselTransform!=null)spawn.SetParent(vesselTransform,true);spawns.Add(spawn);
            }
            var controls=new GameObject("Food Lab controls").AddComponent<CCFoodLabControls>();controls.world=world;controls.surface=surface;controls.viewCamera=cam;controls.cameraRig=cameraRig;controls.spawnPoints=spawns.ToArray();controls.boiling=boil.ToArray();
            var marker=new GameObject("Tool radius").AddComponent<LineRenderer>();marker.loop=true;marker.positionCount=48;marker.widthMultiplier=.004f;marker.sharedMaterial=Mat(folder,"Tool_Cursor",Shader.Find("Universal Render Pipeline/Unlit"),new Color(.2f,.9f,.8f));controls.marker=marker;
            var shovel=new GameObject("Laboratory shovel — visual only").transform;
            var blade=GameObject.CreatePrimitive(PrimitiveType.Cube);blade.transform.SetParent(shovel,false);blade.transform.localScale=new Vector3(.24f,.015f,.2f);blade.GetComponent<Renderer>().sharedMaterial=metal;UnityEngine.Object.DestroyImmediate(blade.GetComponent<Collider>());
            var handle=GameObject.CreatePrimitive(PrimitiveType.Cube);handle.transform.SetParent(shovel,false);handle.transform.localPosition=new Vector3(0,0,-.25f);handle.transform.localScale=new Vector3(.025f,.025f,.35f);handle.GetComponent<Renderer>().sharedMaterial=wood;UnityEngine.Object.DestroyImmediate(handle.GetComponent<Collider>());controls.shovelVisual=shovel;
            PrefabUtility.SaveAsPrefabAssetAndConnect(world.gameObject,folder+"/CCFoodSystem.prefab",InteractionMode.AutomatedAction);
            EditorSceneManager.SaveScene(scene,folder+"/FoodLaboratory.unity");AssetDatabase.SaveAssets();Selection.activeGameObject=world.gameObject;
            Debug.Log("Food Lab создан: "+folder+"/FoodLaboratory.unity. Нажми Play и Добавить порцию. Это новый отдельный стенд.");
        }
        static CCFoodProfile Profile(string folder,string name,string title,FoodBehaviour behaviour,Color color,float visc,float cohesion,float friction,float compliance,float plastic,float tear,float rough)
        {
            var p=ScriptableObject.CreateInstance<CCFoodProfile>();p.name=name;p.displayName=title;p.behaviour=behaviour;p.body=color;p.viscosity=visc;p.cohesion=cohesion;p.friction=friction;p.compliance=compliance;p.plasticity=plastic;p.tearStretch=tear;p.roughness=rough;p.healContacts=behaviour==FoodBehaviour.Paste;
            p.inclusions=behaviour==FoodBehaviour.Granular?.85f:name.Contains("Concrete")?.55f:name.Contains("Sauce")?.2f:.06f;
            p.fleck=name.Contains("Concrete")?new Color(.3f,.31f,.27f):behaviour==FoodBehaviour.Granular?new Color(.2f,.42f,.1f):new Color(.32f,.38f,.08f);
            AssetDatabase.CreateAsset(p,folder+"/Profiles/"+name+".asset");return p;
        }
        static Material Mat(string folder,string name,Shader shader,Color color)
        {
            if(shader==null)throw new InvalidOperationException("Не найден shader для "+name);
            var m=new Material(shader){name=name,enableInstancing=true};m.SetColor("_BaseColor",color);if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.25f);AssetDatabase.CreateAsset(m,folder+"/"+name+".mat");return m;
        }
        static Mesh PrimitiveMesh(PrimitiveType type,string path)
        {var temp=GameObject.CreatePrimitive(type);var m=UnityEngine.Object.Instantiate(temp.GetComponent<MeshFilter>().sharedMesh);UnityEngine.Object.DestroyImmediate(temp);AssetDatabase.CreateAsset(m,path);return m;}
        static Mesh MakeBowl(float radius,float height,float wall)
        {
            var v=new List<Vector3>();var t=new List<int>();const int n=40;
            Action<Vector3,Vector3,Vector3,Vector3> quad=(a,b,c,d)=>{int start=v.Count;v.Add(a);v.Add(b);v.Add(c);v.Add(d);t.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});};
            for(int i=0;i<n;i++)
            {
                float a=i*Mathf.PI*2/n,b=(i+1)*Mathf.PI*2/n;
                Vector3 x=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)),y=new Vector3(Mathf.Cos(b),0,Mathf.Sin(b)),up=Vector3.up*height;
                quad(x*radius,y*radius,y*radius+up,x*radius+up); // inner wall faces cavity
                quad(y*(radius+wall),x*(radius+wall),x*(radius+wall)+up,y*(radius+wall)+up);
                quad(x*radius+up,y*radius+up,y*(radius+wall)+up,x*(radius+wall)+up);
                quad(Vector3.zero,y*radius,x*radius,Vector3.zero);
                quad(-Vector3.up*wall,x*(radius+wall)-Vector3.up*wall,y*(radius+wall)-Vector3.up*wall,-Vector3.up*wall);
            }
            var mesh=new Mesh{name="Analytic bowl visual"};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        [MenuItem("Tools/CatCooks/Food Lab/Enable FishNet adapter")]
        static void EnableFishNet()
        {
            bool found=false;foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())if(assembly.GetType("FishNet.Object.NetworkObject")!=null){found=true;break;}
            if(!found){Debug.LogError("FishNet не установлен. Локальная сцена работает без него.");return;}
            var target=UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            string symbols=PlayerSettings.GetScriptingDefineSymbols(target);
            if(!(";"+symbols+";").Contains(";CATCOOKS_FOOD_FISHNET;"))PlayerSettings.SetScriptingDefineSymbols(target,symbols+";CATCOOKS_FOOD_FISHNET");
            Debug.Log("После компиляции: Food Lab → Add network adapter to selected world.");
        }
        [MenuItem("Tools/CatCooks/Food Lab/Add network adapter to selected world")]
        static void AddNetwork()
        {
            #if CATCOOKS_FOOD_FISHNET
            var world=Selection.activeGameObject==null?null:Selection.activeGameObject.GetComponent<CCFoodWorld>();
            if(world==null){Debug.LogError("Выбери CCFoodWorld в Hierarchy.");return;}
            if(world.GetComponent<CCFoodFishNet>()==null)Undo.AddComponent<CCFoodFishNet>(world.gameObject);
            EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            #else
            Debug.LogError("Сначала Enable FishNet adapter.");
            #endif
        }
    }
}
