using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using FishNet.Object;
namespace CatCooks.RandomConstruction.Editor
{
    public sealed class CCRandomConstructionSetup:EditorWindow
    {
        Transform _origin;
        GameObject _oldGeometry;
        CCBuildSettings _settings=CCBuildSettings.Default;
        int _seed=1729;bool _random=true;
        string _message="";
        [MenuItem("Tools/CatCooks/Random Construction/Create four-level site")]
        public static void Open(){GetWindow<CCRandomConstructionSetup>("Random construction").Show();}
        void OnGUI()
        {
            EditorGUILayout.HelpBox("Зональная стройка v2: открытая площадка, рабочие острова, мосты и два высоких фрагмента. Y корня = поверхность игрового пола. Безопасная четверть постоянная; новое состояние при запуске Host. Старые бригады/краны автоматически не подключаются.",MessageType.Info);
            _origin=(Transform)EditorGUILayout.ObjectField("Game floor origin",_origin,typeof(Transform),true);
            _oldGeometry=(GameObject)EditorGUILayout.ObjectField("Old generated geometry (optional)",_oldGeometry,typeof(GameObject),true);
            _settings.Width=EditorGUILayout.IntSlider("Cells X (even)",_settings.Width,4,16);_settings.Depth=EditorGUILayout.IntSlider("Cells Z (even)",_settings.Depth,4,16);
            _settings.Bay=EditorGUILayout.Slider("Square bay metres",_settings.Bay,4,20);_settings.Height=_settings.Bay;
            _settings.SafeCorner=EditorGUILayout.Popup("Safe quarter",_settings.SafeCorner,new[]{"0: X min / Z min","1: X max / Z min","2: X min / Z max","3: X max / Z max"});
            _settings.BridgeWidth=EditorGUILayout.Slider("Bridge width, metres",_settings.BridgeWidth<=0?2.4f:_settings.BridgeWidth,1.2f,4);
            _settings.JumpGap=EditorGUILayout.Slider("Optional jump gap, metres",_settings.JumpGap<=0?1.2f:_settings.JumpGap,.4f,2.5f);
            _settings.DisableJumpShortcut=EditorGUILayout.Toggle("Disable jump shortcut",_settings.DisableJumpShortcut);
            _seed=EditorGUILayout.IntField("Preview / fixed seed",_seed);_random=EditorGUILayout.Toggle("New seed each Host",_random);
            var s=_settings.Sanitized();EditorGUILayout.LabelField("Footprint",(s.Width*s.Bay)+" x "+(s.Depth*s.Bay)+" m; storey "+s.Height+" m");
            using(new EditorGUI.DisabledScope(Application.isPlaying))if(GUILayout.Button("Create site + models + preview"))
            {try{Create();}catch(Exception e){_message=e.Message;Debug.LogException(e);}}
            if(_message.Length>0)EditorGUILayout.HelpBox(_message,MessageType.Info);
        }
        void Create()
        {
            if(_origin==null)throw new InvalidOperationException("Назначь Empty у угла игрового этажа. Его Y — уровень пола.");
            if(_oldGeometry!=null&&_oldGeometry.GetComponentInChildren<NetworkObject>(true)!=null)throw new InvalidOperationException("Выбирай только старую геометрию Generated_Floor, без NetworkObject котов, игрока и кранов.");
            if(Vector3.Dot(_origin.up,Vector3.up)<.9999f)throw new InvalidOperationException("Маркер можно поворачивать только по Y.");
            var material=CCRandomConstructionAssets.Material();CCRandomConstructionAssets.Models(material);
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Create random construction");
            var go=new GameObject("RANDOM CONSTRUCTION — game floor Y zero");Undo.RegisterCreatedObjectUndo(go,"Create construction");go.transform.SetPositionAndRotation(_origin.position,_origin.rotation);
            Undo.AddComponent<NetworkObject>(go);var site=Undo.AddComponent<CCRandomConstruction>(go);
            site.Settings=_settings.Sanitized();site.PreviewSeed=_seed;site.RandomizeOnHost=_random;site.AtlasMaterial=material;
            if(_oldGeometry!=null){site.ReplacedGeometry=new[]{_oldGeometry};Undo.RecordObject(_oldGeometry,"Disable selected old geometry");}
            CCRandomConstructionAssets.Preview(site);
            EditorUtility.SetDirty(site);EditorSceneManager.MarkSceneDirty(go.scene);Undo.CollapseUndoOperations(group);Selection.activeGameObject=go;
            _message="Создано. Расставь оборудование в зелёной четверти. Назначь PlayerSpawn своему спавнеру, сохрани сцену. Новые типы работ пока только в графе задач; старые бригады сюда не переключены.";
        }
    }
    [CustomEditor(typeof(CCRandomConstruction))]
    public sealed class CCRandomConstructionInspector:UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();var s=(CCRandomConstruction)target;
            EditorGUILayout.HelpBox("Zones v2. После обновления нажми Rebuild preview (same seed). Корень и привязки сохраняются. Зелёный — основная площадка, оранжевый — острова, голубой — верхние фрагменты; мосты голубые, прыжковый путь розовый. Пустоты и открытый центр не заполняются будущими заданиями.",MessageType.Info);
            if(!Application.isPlaying)
            {
                if(GUILayout.Button("Rebuild preview (same seed)"))TryPreview(s,false);
                if(GUILayout.Button("New random preview seed"))TryPreview(s,true);
                if(GUILayout.Button("Validate generated dependencies + safe quarter"))
                {var g=new CCBuildGraph(s.Settings,s.PreviewSeed);string error=g.Validate(g.InitialState());if(error.Length==0)Debug.Log("Graph valid. Zonal voids and sky reserved; continuous bridges connect all landings to the safe quarter. Check player passage and jump distance in scene.",s);else Debug.LogError(error,s);}
                if(GUILayout.Button("Create / find 20 model prefabs")){var m=CCRandomConstructionAssets.Material();CCRandomConstructionAssets.Models(m);EditorUtility.FocusProjectWindow();Selection.activeObject=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(CCRandomConstructionAssets.Root+"/Generated/Models");}
            }
            if(GUILayout.Button("Print next work tasks / access requirements"))Debug.Log(s.WorkReport(),s);
            if(Application.isPlaying)EditorGUILayout.HelpBox(s.Status,MessageType.None);
        }
        static void TryPreview(CCRandomConstruction s,bool random)
        {
            try{Undo.RecordObject(s,"Change construction seed");if(random)s.PreviewSeed=Guid.NewGuid().GetHashCode();CCRandomConstructionAssets.Preview(s);}catch(Exception e){Debug.LogException(e,s);}
        }
    }
    public static class CCRandomConstructionAssets
    {
        public static string Root
        {
            get{foreach(string guid in AssetDatabase.FindAssets("CCRandomConstructionSetup t:MonoScript")){string p=AssetDatabase.GUIDToAssetPath(guid);if(p.EndsWith("/Editor/CCRandomConstructionSetup.cs"))return p.Substring(0,p.Length-"/Editor/CCRandomConstructionSetup.cs".Length);}throw new InvalidOperationException("Cannot find package root");}
        }
        static void Folder(string path)
        {if(AssetDatabase.IsValidFolder(path))return;int k=path.LastIndexOf('/');Folder(path.Substring(0,k));AssetDatabase.CreateFolder(path.Substring(0,k),path.Substring(k+1));}
        public static Material Material()
        {
            string folder=Root+"/Generated";Folder(folder);string path=folder+"/ConstructionAtlas.mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat!=null)return mat;
            string texturePath=Root+"/Art/ConstructionAtlas.png";
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);if(texture==null)throw new InvalidOperationException("Missing ConstructionAtlas.png");
            var importer=AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if(importer!=null){importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;importer.maxTextureSize=512;importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);}
            var pipeline=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            string shaderName=pipeline==null?"Standard":pipeline.GetType().Name.Contains("HDRender")?"HDRP/Lit":"Universal Render Pipeline/Lit";
            var shader=Shader.Find(shaderName);if(shader==null)throw new InvalidOperationException("Shader unavailable: "+shaderName);
            mat=new Material(shader){name="ConstructionAtlas",enableInstancing=true};
            foreach(string prop in new[]{"_BaseMap","_MainTex","_BaseColorMap"})if(mat.HasProperty(prop))mat.SetTexture(prop,texture);
            foreach(string prop in new[]{"_BaseColor","_Color"})if(mat.HasProperty(prop))mat.SetColor(prop,Color.white);
            foreach(string prop in new[]{"_Smoothness","_Glossiness"})if(mat.HasProperty(prop))mat.SetFloat(prop,.15f);
            AssetDatabase.CreateAsset(mat,path);return mat;
        }
        public static void Models(Material material)
        {
            string folder=Root+"/Generated/Models";Folder(folder);
            foreach(string name in CCBuildGeometry.ModelNames)
            {
                string path=folder+"/"+name+".prefab";if(AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null)continue;
                var data=CCBuildGeometry.Sample(name);Mesh mesh=data.Visual.ToMesh(name);string meshPath=folder+"/"+name+".asset";
                if(AssetDatabase.LoadMainAssetAtPath(meshPath)!=null){UnityEngine.Object.DestroyImmediate(mesh);mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);}else AssetDatabase.CreateAsset(mesh,meshPath);
                var go=new GameObject(name);try
                {
                    go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                    PrefabUtility.SaveAsPrefabAsset(go,path);
                }finally{UnityEngine.Object.DestroyImmediate(go);}
            }
            AssetDatabase.SaveAssets();
        }
        public static void Preview(CCRandomConstruction site)
        {
            if(Application.isPlaying)throw new InvalidOperationException("Rebuild only outside Play");
            if(!site.Valid(out string why))throw new InvalidOperationException(why);
            string folder=Root+"/Generated/Previews";Folder(folder);
            var bake=ScriptableObject.CreateInstance<CCBuildPreviewBake>();bake.Description="Seed "+site.PreviewSeed;
            string path=folder+"/Preview_"+Guid.NewGuid().ToString("N").Substring(0,12)+".asset";AssetDatabase.CreateAsset(bake,path);
            Undo.RecordObject(site,"Rebuild preview");GameObject old=site.Generated;site.Generated=null;
            foreach(var t in new[]{site.PlayerSpawn,site.KitchenAnchor,site.ReceivingA,site.ReceivingB,site.CrewStaging,site.LiftStop})if(t!=null)Undo.RecordObject(t,"Move stable anchor");
            foreach(var g in site.ReplacedGeometry)if(g!=null)Undo.RecordObject(g,"Disable old geometry");
            try
            {
                site.BuildPreview();
                foreach(var f in site.Generated.GetComponentsInChildren<MeshFilter>())if(f.sharedMesh!=null)AssetDatabase.AddObjectToAsset(f.sharedMesh,bake);
                foreach(var c in site.Generated.GetComponentsInChildren<MeshCollider>())if(c.sharedMesh!=null)AssetDatabase.AddObjectToAsset(c.sharedMesh,bake);
                site.PreviewBake=bake;Undo.RegisterCreatedObjectUndo(site.Generated,"New preview");if(old!=null)Undo.DestroyObjectImmediate(old);
                EditorUtility.SetDirty(bake);EditorUtility.SetDirty(site);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(site.gameObject.scene);
            }
            catch{if(site.Generated!=null)UnityEngine.Object.DestroyImmediate(site.Generated);site.Generated=old;AssetDatabase.DeleteAsset(path);throw;}
        }
    }
}
