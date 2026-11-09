using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace CatCooks.PaintedBackdrop.Editor
{
 public sealed class CCPaintedBackdropWindow : EditorWindow
 {
  const string Root="Assets/CatCooks/PaintedBackdrop";
  const string ShaderName="CatCooks/Painted Backdrop/Square City";
  [SerializeField] GameObject city;
  [SerializeField] Vector3 centre;
  [SerializeField] float side=1200,bottom=-25,height=220;
  [SerializeField] bool apron=true;
  [SerializeField] float innerSide=800;
  [SerializeField] Material material;
  string status="Назначь корень дальнего города; сначала Подогнать, затем Создать. Пояс неподвижен и не следует за камерой.";
  [MenuItem("Tools/CatCooks/Painted Backdrop")]
  static void Open(){GetWindow<CCPaintedBackdropWindow>("Painted Backdrop");}
  void OnGUI()
  {
   EditorGUILayout.HelpBox("Квадратный внешний пояс: 4 стороны, 1 материал. Выше рисованных крыш виден настоящий skybox. Внутренние 3D-дома закрывают задник по глубине.",MessageType.Info);
   city=(GameObject)EditorGUILayout.ObjectField("Far city root",city,typeof(GameObject),true);
   if(GUILayout.Button("Подогнать размер за существующий город")) Run(Fit);
   centre=EditorGUILayout.Vector3Field("Centre XZ (Y ignored)",centre);
   side=Mathf.Max(10,EditorGUILayout.FloatField("Outer square side (m)",side));
   bottom=EditorGUILayout.FloatField("Bottom world Y",bottom);
   height=Mathf.Max(1,EditorGUILayout.FloatField("Panel height (m)",height));
   apron=EditorGUILayout.Toggle("Ground apron outside city",apron);
   if(apron)innerSide=Mathf.Clamp(EditorGUILayout.FloatField("Apron inner opening (m)",innerSide),1,side-1);
   if(GUILayout.Button("Создать новый пояс и сохранить prefab")) Run(Create);
   material=(Material)EditorGUILayout.ObjectField("Backdrop material",material,typeof(Material),false);
   if(material!=null && material.shader!=null && material.shader.name==ShaderName)
   {
    Slider("_Brightness","Brightness",.2f,2);Slider("_Saturation","Saturation",0,1.5f);
    Slider("_DetailSoftness","Texture softness",0,2);Slider("_AirBlend","Extra painted air",0,1);
    Slider("_WeatherInfluence","Follow scene lighting",0,1);
   }
   EditorGUILayout.HelpBox(status,MessageType.Info);
  }
  void Slider(string prop,string label,float min,float max)
  {
   float before=material.GetFloat(prop),value=EditorGUILayout.Slider(label,before,min,max);
   if(before!=value){Undo.RecordObject(material,"Tune painted backdrop");material.SetFloat(prop,value);EditorUtility.SetDirty(material);SceneView.RepaintAll();}
  }
  void Run(Action action)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode){status="Выйди из Play Mode.";return;}
   try{action();AssetDatabase.SaveAssets();SceneView.RepaintAll();}catch(Exception e){status=e.Message;Debug.LogError("Painted backdrop: "+e.Message);}
  }
  void Fit()
  {
   if(city==null || EditorUtility.IsPersistent(city))throw new InvalidOperationException("Нужен корень города из Hierarchy.");
   var rs=city.GetComponentsInChildren<MeshRenderer>(true);
   if(rs.Length==0)throw new InvalidOperationException("В корне нет мешей.");
   var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
   float size=Mathf.Max(b.size.x,b.size.z);centre=new Vector3(b.center.x,0,b.center.z);
   side=Mathf.Max(10,size*1.35f);innerSide=size*1.01f;
   bottom=b.min.y-Mathf.Max(5,b.size.y*.1f);height=Mathf.Max(20,b.size.y*1.35f);
   status="Размеры взяты в мировых метрах, с учётом масштаба города. Пояс снаружи его общего XZ bounds. При высоком полёте камеры плоские стены будут заметны: подбирай высоту по игровому ракурсу.";
  }
  void Create()
  {
   var shader=Shader.Find(ShaderName);if(shader==null)throw new InvalidOperationException("Не найден shader Painted Backdrop. Импортируй папку Assets из архива.");
   string path=Root+"/Textures/CC_PaintedSkyline.png";
   var ti=AssetImporter.GetAtPath(path) as TextureImporter;if(ti==null)throw new InvalidOperationException("Нет текстуры "+path);
   ti.textureType=TextureImporterType.Default;ti.sRGBTexture=true;ti.alphaSource=TextureImporterAlphaSource.FromInput;ti.alphaIsTransparency=true;
   ti.mipmapEnabled=true;ti.wrapModeU=TextureWrapMode.Repeat;ti.wrapModeV=TextureWrapMode.Clamp;ti.filterMode=FilterMode.Trilinear;ti.anisoLevel=1;
   ti.maxTextureSize=4096;ti.textureCompression=TextureImporterCompression.Compressed;ti.isReadable=false;ti.SaveAndReimport();
   Directory.CreateDirectory(Root+"/Generated");AssetDatabase.Refresh();
   string folder=AssetDatabase.GenerateUniqueAssetPath(Root+"/Generated/Belt");Directory.CreateDirectory(folder);AssetDatabase.Refresh();
   var mat=new Material(shader){name="PaintedSkyline"};mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));AssetDatabase.CreateAsset(mat,folder+"/PaintedSkyline.mat");
   GameObject go=null;
   try
   {
    go=new GameObject("CC_PaintedSkyline_Belt");
    if(city!=null && city.scene.IsValid())UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,city.scene);
    go.transform.position=new Vector3(centre.x,bottom,centre.z);
    float h=side*.5f;
    var points=new[]{new Vector3(-h,0,-h),new Vector3(h,0,-h),new Vector3(h,0,h),new Vector3(-h,0,h)};
    for(int i=0;i<4;i++)
    {
     Vector3 a=points[i],b=points[(i+1)%4];float u=i*.25f,v=(i+1)*.25f;
     var mesh=new Mesh{name="Wall_"+i};mesh.vertices=new[]{a,b,b+Vector3.up*height,a+Vector3.up*height};
     mesh.uv=new[]{new Vector2(u,0),new Vector2(v,0),new Vector2(v,1),new Vector2(u,1)};mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateNormals();mesh.RecalculateBounds();
     AssetDatabase.CreateAsset(mesh,folder+"/Wall_"+i+".asset");Add(go,"Wall_"+i,mesh,mat);
    }
    if(apron)
    {
     var mesh=new Mesh{name="GroundApron"};var vertices=new Vector3[16];var uv=new Vector2[16];var indices=new int[24];
     float ratio=Mathf.Clamp(innerSide/side,.001f,.999f);
     for(int i=0;i<4;i++)
     {
      int n=i*4;Vector3 a=points[i],b=points[(i+1)%4];
      vertices[n]=a;vertices[n+1]=b;vertices[n+2]=b*ratio;vertices[n+3]=a*ratio;
      for(int j=0;j<4;j++)uv[n+j]=new Vector2(.5f,.01f);
      int k=i*6;indices[k]=n;indices[k+1]=n+1;indices[k+2]=n+2;indices[k+3]=n;indices[k+4]=n+2;indices[k+5]=n+3;
     }
     mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=indices;mesh.RecalculateNormals();mesh.RecalculateBounds();
     AssetDatabase.CreateAsset(mesh,folder+"/GroundApron.asset");Add(go,"GroundApron",mesh,mat);
    }
    PrefabUtility.SaveAsPrefabAsset(go,folder+"/PaintedSkyline.prefab");Undo.RegisterCreatedObjectUndo(go,"Create painted city belt");EditorSceneManager.MarkSceneDirty(go.scene);
    material=mat;Selection.activeGameObject=go;
    float need=side*1.4143f+height;
    status="Создан пояс: "+(apron?"16 треугольников / 5 Renderer":"8 треугольников / 4 Renderer")+". Подвигай/масштабируй общий корень или отдельные Wall. Для повторной сборки отключи предыдущий пояс. Проверь Far Clip игровой камеры: дистанция до дальнего угла должна помещаться в него; ориентир для камеры внутри пояса до "+Mathf.CeilToInt(need)+" м.";
   }
   catch{if(go!=null)DestroyImmediate(go);throw;}
  }
  static void Add(GameObject root,string name,Mesh mesh,Material material)
  {
   var o=new GameObject(name);o.transform.SetParent(root.transform,false);o.AddComponent<MeshFilter>().sharedMesh=mesh;
   var r=o.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;
  }
 }
}
