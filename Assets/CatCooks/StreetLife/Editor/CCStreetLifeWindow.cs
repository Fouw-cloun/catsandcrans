using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace CatCooks.StreetLife.Editor
{
 public sealed class CCStreetLifeWindow : EditorWindow
 {
  [Serializable] sealed class MeshData { public string name; public float[] positions,uv,normals; public int[] triangles; }
  [Serializable] sealed class PairData { public int version; public string name; public MeshData[] lods; }
  const string Root="Assets/CatCooks/StreetLife";
  [SerializeField] DefaultAsset source;
  [SerializeField] string lastBuild;
  [SerializeField] Vector3 placement;
  [SerializeField] bool architectureColliders=true;
  string status="33 модели: 10 машин, 20 пропов, 2 башни и рабочая площадка. У каждой есть Near + Far. Готовые исходники включены; Blender для сборки не нужен.";
  [MenuItem("Tools/CatCooks/Street Life")]
  static void Open(){GetWindow<CCStreetLifeWindow>("Street Life");}
  void OnEnable(){if(source==null)source=AssetDatabase.LoadAssetAtPath<DefaultAsset>(Root+"/Source");}
  void OnGUI()
  {
   EditorGUILayout.HelpBox(status,MessageType.Info);
   source=(DefaultAsset)EditorGUILayout.ObjectField("Source folder",source,typeof(DefaultAsset),false);
   architectureColliders=EditorGUILayout.Toggle("Static architecture colliders",architectureColliders);
   if(GUILayout.Button("1. Собрать префабы Near + Far"))Run(Build);
   placement=EditorGUILayout.Vector3Field("Test layout world position",placement);
   using(new EditorGUI.DisabledScope(string.IsNullOrEmpty(lastBuild)))
   {
    if(GUILayout.Button("2. Поставить тестовый квартал"))Run(Place);
    if(GUILayout.Button("Открыть папку префабов"))Selection.activeObject=AssetDatabase.LoadAssetAtPath<DefaultAsset>(lastBuild+"/Prefabs");
   }
   EditorGUILayout.HelpBox("Near: один Renderer на видимый LOD, тени включены. Far: простой меш без теней, коллизий и LODGroup. Материал общий URP/Lit; существующий свет, небо и погода не меняются.",MessageType.None);
   if(GUILayout.Button("Выбранные Near: зафиксировать LOD0 для проверки света"))Run(()=>Force(0));
   if(GUILayout.Button("Выбранные Near: вернуть автоматический LOD"))Run(()=>Force(-1));
  }
  void Run(Action action)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode){status="Выйди из Play Mode.";return;}
   try{action();AssetDatabase.SaveAssets();SceneView.RepaintAll();}
   catch(Exception e){status=e.Message;Debug.LogError("Street Life: "+e.Message);}
  }
  static void Folder(string p){Directory.CreateDirectory(p);AssetDatabase.Refresh();}
  void Build()
  {
   string src=AssetDatabase.GetAssetPath(source);
   if(!AssetDatabase.IsValidFolder(src)||!Directory.Exists(src+"/UnityMeshes"))throw new InvalidOperationException("Выбери Source из комплекта или папку нового экспорта Blender с Textures и UnityMeshes.");
   var shader=Shader.Find("Universal Render Pipeline/Lit");if(shader==null)throw new InvalidOperationException("Нужен проект с Universal Render Pipeline и URP/Lit.");
   var files=Directory.GetFiles(src+"/UnityMeshes","CCSL_*.json").OrderBy(p=>p).ToArray();
   if(files.Length==0)throw new InvalidOperationException("Нет CCSL mesh sources.");
   string atlasPath=src+"/Textures/CCSL_Atlas.png";var ti=AssetImporter.GetAtPath(atlasPath) as TextureImporter;
   if(ti==null)throw new InvalidOperationException("Не найден Textures/CCSL_Atlas.png.");
   ti.textureType=TextureImporterType.Default;ti.sRGBTexture=true;ti.alphaSource=TextureImporterAlphaSource.None;
   ti.mipmapEnabled=true;ti.wrapMode=TextureWrapMode.Clamp;ti.filterMode=FilterMode.Trilinear;ti.anisoLevel=4;ti.maxTextureSize=2048;
   ti.textureCompression=TextureImporterCompression.Compressed;ti.isReadable=false;ti.SaveAndReimport();
   Folder(Root+"/Generated");string dest=AssetDatabase.GenerateUniqueAssetPath(Root+"/Generated/StreetSet");
   foreach(string sub in new[]{"","/Meshes","/Prefabs","/Prefabs/Near","/Prefabs/Far"})Folder(dest+sub);
   var mat=new Material(shader){name="CCSL_SharedAtlas",enableInstancing=true};mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath));
   mat.SetColor("_BaseColor",Color.white);mat.SetFloat("_Metallic",0);mat.SetFloat("_Smoothness",.25f);
   AssetDatabase.CreateAsset(mat,dest+"/CCSL_SharedAtlas.mat");int count=0;
   try
   {
    foreach(string file in files)
    {
     EditorUtility.DisplayProgressBar("Street Life",Path.GetFileName(file),(float)count/files.Length);
     var data=JsonUtility.FromJson<PairData>(File.ReadAllText(file));
     if(data==null||data.version!=2||data.name!=Path.GetFileNameWithoutExtension(file)||data.lods==null||data.lods.Length!=2)
      throw new InvalidOperationException("Неверный mesh source: "+file);
     var meshes=data.lods.Select(MakeMesh).ToArray();
     for(int i=0;i<2;i++)AssetDatabase.CreateAsset(meshes[i],dest+"/Meshes/"+data.name+"_LOD"+i+".asset");
     bool arch=data.name.Contains("_Architecture_");
     var preview=EditorSceneManager.NewPreviewScene();
     GameObject near=null,far=null;
     try
     {
      near=new GameObject(data.name);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(near,preview);
      var lods=new LOD[2];
      for(int l=0;l<2;l++)
      {
       var child=new GameObject("LOD"+l);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(child,preview);child.transform.SetParent(near.transform,false);
       var r=AddRenderer(child,meshes[l],mat,l==0);
       lods[l]=new LOD(l==0?(arch?.18f:.06f):(arch?.0005f:.0015f),new Renderer[]{r});
      }
      var group=near.AddComponent<LODGroup>();group.fadeMode=LODFadeMode.None;group.SetLODs(lods);group.RecalculateBounds();
      if(arch&&architectureColliders){var c=near.AddComponent<MeshCollider>();c.sharedMesh=meshes[0];c.convex=false;}
      PrefabUtility.SaveAsPrefabAsset(near,dest+"/Prefabs/Near/"+data.name+".prefab");
      far=new GameObject(data.name+"_Far");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(far,preview);AddRenderer(far,meshes[1],mat,false);
      PrefabUtility.SaveAsPrefabAsset(far,dest+"/Prefabs/Far/"+data.name+"_Far.prefab");count++;
     }
     finally{if(near!=null)DestroyImmediate(near);if(far!=null)DestroyImmediate(far);EditorSceneManager.ClosePreviewScene(preview);}
    }
   }
   finally{EditorUtility.ClearProgressBar();}
   lastBuild=dest;status="Готово: "+count+" Near и "+count+" Far. Все используют один material asset. Повторная сборка создаст отдельный новый набор. Старые префабы не перезаписаны.";
   Selection.activeObject=AssetDatabase.LoadAssetAtPath<DefaultAsset>(dest+"/Prefabs");
  }
  static MeshRenderer AddRenderer(GameObject obj,Mesh mesh,Material mat,bool near)
  {
   obj.AddComponent<MeshFilter>().sharedMesh=mesh;var r=obj.AddComponent<MeshRenderer>();r.sharedMaterial=mat;
   r.shadowCastingMode=near?ShadowCastingMode.On:ShadowCastingMode.Off;r.receiveShadows=near;
   r.lightProbeUsage=near?LightProbeUsage.BlendProbes:LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;
   return r;
  }
  static Mesh MakeMesh(MeshData d)
  {
   if(d==null||d.positions==null||d.positions.Length==0||d.positions.Length%3!=0||d.normals==null||d.uv==null||d.triangles==null)
    throw new InvalidOperationException("Неполный mesh source.");
   int count=d.positions.Length/3;
   if(d.normals.Length!=count*3||d.uv.Length!=count*2||d.triangles.Length==0||d.triangles.Length%3!=0)
    throw new InvalidOperationException("Неверные размеры массивов: "+d.name);
   foreach(float f in d.positions.Concat(d.normals).Concat(d.uv))if(float.IsNaN(f)||float.IsInfinity(f))throw new InvalidOperationException("Нечисловые координаты: "+d.name);
   if(d.triangles.Any(i=>i<0||i>=count))throw new InvalidOperationException("Неверный индекс: "+d.name);
   var v=new Vector3[count];var n=new Vector3[count];var uv=new Vector2[count];
   for(int i=0;i<count;i++){v[i]=new Vector3(d.positions[i*3],d.positions[i*3+1],d.positions[i*3+2]);n[i]=new Vector3(d.normals[i*3],d.normals[i*3+1],d.normals[i*3+2]);uv[i]=new Vector2(d.uv[i*2],d.uv[i*2+1]);}
   var mesh=new Mesh{name=d.name,indexFormat=count>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.vertices=v;mesh.normals=n;mesh.uv=uv;mesh.triangles=d.triangles;mesh.RecalculateBounds();return mesh;
  }
  void Place()
  {
   string folder=lastBuild+"/Prefabs/Near";
   var paths=AssetDatabase.FindAssets("t:Prefab",new[]{folder}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p=>p).ToArray();
   if(paths.Length==0)throw new InvalidOperationException("Сначала собери комплект.");
   var root=new GameObject("CCSL_LightTest_Block");root.transform.position=placement;Undo.RegisterCreatedObjectUndo(root,"Place street test block");
   int car=0,prop=0;
   foreach(string path in paths)
   {
    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);var obj=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root.transform);Undo.RegisterCreatedObjectUndo(obj,"Place test model");
    if(obj.name.Contains("_Vehicle_")){obj.transform.localPosition=new Vector3((car%5-2)*10,0,-29-(car/5)*14);car++;}
    else if(obj.name.Contains("_Prop_")){obj.transform.localPosition=new Vector3(prop<10?-62:62,0,-44+(prop%10)*10);prop++;}
    else if(obj.name.Contains("HarborDeco"))obj.transform.localPosition=new Vector3(-43,0,32);
    else if(obj.name.Contains("PrismExchange"))obj.transform.localPosition=new Vector3(43,0,40);
    else obj.transform.localPosition=Vector3.zero;
    if(obj.name.Contains("_Architecture_"))obj.GetComponent<LODGroup>().ForceLOD(0);
   }
   // A single opaque ground mesh for the test assembly, using the same concrete tile.
   var ground=new GameObject("TestGround");ground.transform.SetParent(root.transform,false);
   var mesh=new Mesh{name="CCSL_TestGround"};mesh.vertices=new[]{new Vector3(-72,-.06f,-54),new Vector3(-72,-.06f,76),new Vector3(72,-.06f,76),new Vector3(72,-.06f,-54)};
   mesh.triangles=new[]{0,1,2,0,2,3};Vector2 swatch=new Vector2(1.5f/8f,1-.5f/8f);mesh.uv=Enumerable.Repeat(swatch,4).ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();
   string meshPath=AssetDatabase.GenerateUniqueAssetPath(lastBuild+"/Meshes/TestGround.asset");AssetDatabase.CreateAsset(mesh,meshPath);
   var mat=AssetDatabase.LoadAssetAtPath<Material>(lastBuild+"/CCSL_SharedAtlas.mat");AddRenderer(ground,mesh,mat,true);
   var floor=ground.AddComponent<BoxCollider>();floor.center=new Vector3(0,-.16f,11);floor.size=new Vector3(144,.2f,130);
   EditorSceneManager.MarkSceneDirty(root.scene);Selection.activeGameObject=root;
   status="Поставлен тестовый квартал. Свет/погода не изменены, игрок и камера не создаются. У трёх архитектурных моделей LOD0 зафиксирован для текущей проверки. В обычных сохранённых Near-префабах LOD автоматический. Верх рабочей площадки = Y корня квартала.";
  }
  static void Force(int lod)
  {
   int count=0;
   foreach(var obj in Selection.gameObjects)foreach(var group in obj.GetComponentsInChildren<LODGroup>(true)){group.ForceLOD(lod);count++;}
   Debug.Log("Street Life: LOD override "+lod+" для "+count+" групп. Это временный режим проверки текущей сессии, не настройка сохранённого префаба.");
  }
 }
}
