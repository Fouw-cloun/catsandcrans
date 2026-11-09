using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace CatCooks.CityV2.Editor
{
    public sealed class CCCityV2Window : EditorWindow
    {
        [Serializable] sealed class MeshData { public string name; public float[] positions, uv, normals; public int[] triangles; }
        [Serializable] sealed class PairData { public int version; public string name; public MeshData[] lods; }
        const string AtlasShader="CatCooks/CityV2/Atlas";
        const string Generated="Assets/CatCooks/CityV2/Generated";
        [SerializeField] DefaultAsset source;
        [SerializeField] GameObject city;
        [SerializeField] Material material;
        [SerializeField] bool reuseMaterial=true;
        [SerializeField] string lastBuild;
        string status="Источник: папка с Textures и UnityMeshes. В архиве уже есть ReadySource. Blender для импорта этого набора не требуется.";
        Vector2 scroll;
        [MenuItem("Tools/CatCooks/City V2/City and Colour")]
        static void Open() { GetWindow<CCCityV2Window>("City V2"); }
        void OnGUI()
        {
            scroll=EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("Дальний город: 1 материал на сектор, 2 LOD, детали уже встроены. Не ставь каталог отдельных домов поверх готовых секторов.",MessageType.Info);
            source=(DefaultAsset)EditorGUILayout.ObjectField("Source folder",source,typeof(DefaultAsset),false);
            reuseMaterial=EditorGUILayout.Toggle("Reuse selected city material",reuseMaterial);
            if(GUILayout.Button("1. Собрать материалы и префабы")) Run(Build);
            using(new EditorGUI.DisabledScope(string.IsNullOrEmpty(lastBuild)))
                if(GUILayout.Button("2. Поставить новый комплект секторов")) Run(Place);
            EditorGUILayout.Space();
            city=(GameObject)EditorGUILayout.ObjectField("Existing city root",city,typeof(GameObject),true);
            material=(Material)EditorGUILayout.ObjectField("City shared material",material,typeof(Material),false);
            if(GUILayout.Button("Назначить материал известным атласам выбранного города")) Run(Assign);
            if(GUILayout.Button("Вернуть исходные материалы города")) Run(Restore);
            if(material!=null && material.shader!=null && material.shader.name==AtlasShader)
            {
                EditorGUILayout.HelpBox("Сначала цвет и свет. Local haze = 0, если работает погодный fullscreen-туман. Mip softness смягчает только текстуру, не силуэт и не весь кадр.",MessageType.Info);
                Slider("_AlbedoGain","Albedo gain",.5f,1.8f);
                Slider("_Saturation","Saturation",0,1.5f);
                Slider("_SunGain","Sun response",0,2);
                Slider("_FillGain","Ambient fill",0,2);
                Slider("_HazeAmount","Local haze amount",0,1);
                Number("_HazeStart","Local haze start (m)",0);
                Number("_HazeEnd","Local haze end (m)",material.GetFloat("_HazeStart")+1);
                Slider("_MipSoftness","Distant texture softness",0,1);
                Number("_SoftStart","Softness start (m)",0);
                Number("_SoftEnd","Softness end (m)",material.GetFloat("_SoftStart")+1);
                if(GUILayout.Button("Все настройки материала")) Selection.activeObject=material;
            }
            EditorGUILayout.HelpBox(status,MessageType.Info);
            EditorGUILayout.EndScrollView();
        }
        void Slider(string property,string label,float min,float max)
        {
            float old=material.GetFloat(property),value=EditorGUILayout.Slider(label,old,min,max);
            if(value!=old) {Undo.RecordObject(material,"Tune city material");material.SetFloat(property,value);EditorUtility.SetDirty(material);SceneView.RepaintAll();}
        }
        void Number(string property,string label,float min)
        {
            float old=material.GetFloat(property),value=Mathf.Max(min,EditorGUILayout.FloatField(label,old));
            if(value!=old) {Undo.RecordObject(material,"Tune city distance");material.SetFloat(property,value);EditorUtility.SetDirty(material);SceneView.RepaintAll();}
        }
        void Run(Action action)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) {status="Выйди из Play Mode.";return;}
            try {action();AssetDatabase.SaveAssets();SceneView.RepaintAll();}
            catch(Exception e) {status=e.Message;Debug.LogError("City V2: "+e.Message);}
        }
        void Build()
        {
            string root=AssetDatabase.GetAssetPath(source);
            if(!AssetDatabase.IsValidFolder(root) || !Directory.Exists(root+"/UnityMeshes"))
                throw new InvalidOperationException("Выбери в Source folder папку ReadySource либо новый экспорт Blender, содержащий UnityMeshes и Textures.");
            var shader=Shader.Find(AtlasShader);
            if(shader==null) throw new InvalidOperationException("Не найден CityV2/Atlas. Импортируй папку Shaders и исправь ошибки компиляции в Console.");
            var files=Directory.GetFiles(root+"/UnityMeshes","CCFC_*.json").OrderBy(p=>p).ToArray();
            if(files.Length==0) throw new InvalidOperationException("Папка UnityMeshes пуста.");
            Folder(Generated);
            string build=AssetDatabase.GenerateUniqueAssetPath(Generated+"/City");Folder(build);Folder(build+"/Meshes");Folder(build+"/Prefabs");Folder(build+"/Materials");
            Material shared=reuseMaterial && material!=null && material.shader!=null && material.shader.name==AtlasShader?material:null;
            if(shared==null) foreach(string texturePath in Directory.GetFiles(root+"/Textures","CCFC_Atlas*.png").OrderBy(p=>p))
            {
                string path=texturePath.Replace('\\','/');var ti=AssetImporter.GetAtPath(path) as TextureImporter;
                if(ti==null) continue;
                ti.textureType=TextureImporterType.Default;ti.sRGBTexture=true;ti.alphaSource=TextureImporterAlphaSource.None;
                ti.mipmapEnabled=true;ti.wrapMode=TextureWrapMode.Clamp;ti.filterMode=FilterMode.Trilinear;ti.anisoLevel=2;
                ti.maxTextureSize=2048;ti.textureCompression=TextureImporterCompression.Compressed;ti.isReadable=false;ti.SaveAndReimport();
                var m=new Material(shader){name=Path.GetFileNameWithoutExtension(path)};
                m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                AssetDatabase.CreateAsset(m,build+"/Materials/"+m.name+".mat");
                if(Path.GetFileName(path)=="CCFC_Atlas.png") shared=m;
            }
            if(shared==null) throw new InvalidOperationException("Не найден Textures/CCFC_Atlas.png.");
            int built=0;
            try
            {
                foreach(string file in files)
                {
                    EditorUtility.DisplayProgressBar("City V2",Path.GetFileName(file),(float)built/files.Length);
                    var data=JsonUtility.FromJson<PairData>(File.ReadAllText(file));
                    if(data==null || data.version!=2 || data.lods==null || data.lods.Length!=2 || data.name!=Path.GetFileNameWithoutExtension(file))
                        throw new InvalidOperationException("Неверный формат mesh source: "+file);
                    var preview=EditorSceneManager.NewPreviewScene();GameObject obj=null;
                    try
                    {
                        obj=new GameObject(data.name);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj,preview);
                        var lods=new LOD[2];
                        for(int i=0;i<2;i++)
                        {
                            var md=data.lods[i];Mesh mesh=CreateMesh(md);
                            AssetDatabase.CreateAsset(mesh,build+"/Meshes/"+data.name+"_LOD"+i+".asset");
                            var child=new GameObject(data.name+"_LOD"+i);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(child,preview);child.transform.SetParent(obj.transform,false);
                            child.AddComponent<MeshFilter>().sharedMesh=mesh;
                            var r=child.AddComponent<MeshRenderer>();r.sharedMaterial=shared;
                            r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
                            r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;
                            r.motionVectorGenerationMode=MotionVectorGenerationMode.Camera;
                            lods[i]=new LOD(i==0?(data.name.StartsWith("CCFC_Sector_")?.28f:.08f):.002f,new Renderer[]{r});
                        }
                        var group=obj.AddComponent<LODGroup>();group.fadeMode=LODFadeMode.None;group.SetLODs(lods);group.RecalculateBounds();
                        PrefabUtility.SaveAsPrefabAsset(obj,build+"/Prefabs/"+data.name+".prefab");built++;
                    }
                    finally {if(obj!=null) DestroyImmediate(obj);EditorSceneManager.ClosePreviewScene(preview);}
                }
            }
            finally {EditorUtility.ClearProgressBar();}
            lastBuild=build;material=shared;
            status="Готово: "+built+" префабов. "+build+". Кнопка 2 ставит только секторы. Отдельные детали находятся в Prefabs/CCFC_Roof_*. Старые экспорты не заменены.";
            Selection.activeObject=AssetDatabase.LoadAssetAtPath<DefaultAsset>(build);
        }
        static Mesh CreateMesh(MeshData d)
        {
            if(d==null || d.positions==null || d.positions.Length%3!=0 || d.uv==null || d.normals==null || d.triangles==null)
                throw new InvalidOperationException("Неполные данные меша.");
            int count=d.positions.Length/3;
            if(count==0 || d.uv.Length!=count*2 || d.normals.Length!=count*3 || d.triangles.Length%3!=0)
                throw new InvalidOperationException("Неверные размеры массивов: "+d.name);
            var v=new Vector3[count];var n=new Vector3[count];var uv=new Vector2[count];
            for(int i=0;i<count;i++)
            {
                for(int j=0;j<3;j++)
                    if(float.IsNaN(d.positions[i*3+j]) || float.IsInfinity(d.positions[i*3+j]) || float.IsNaN(d.normals[i*3+j]) || float.IsInfinity(d.normals[i*3+j]))
                        throw new InvalidOperationException("Нечисловые координаты/нормали: "+d.name);
                v[i]=new Vector3(d.positions[i*3],d.positions[i*3+1],d.positions[i*3+2]);
                n[i]=new Vector3(d.normals[i*3],d.normals[i*3+1],d.normals[i*3+2]);
                uv[i]=new Vector2(d.uv[i*2],d.uv[i*2+1]);
            }
            if(d.triangles.Any(i=>i<0 || i>=count)) throw new InvalidOperationException("Индекс вне меша: "+d.name);
            var mesh=new Mesh{name=d.name,indexFormat=count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            mesh.vertices=v;mesh.normals=n;mesh.uv=uv;mesh.triangles=d.triangles;mesh.RecalculateBounds();
            return mesh; // Keep CPU data for native .asset serialization; no runtime mesh building.
        }
        void Place()
        {
            var paths=AssetDatabase.FindAssets("t:Prefab",new[]{lastBuild+"/Prefabs"}).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p=>Path.GetFileName(p).StartsWith("CCFC_Sector_")).OrderBy(p=>p).ToArray();
            if(paths.Length==0) throw new InvalidOperationException("В последней сборке нет секторов.");
            var parent=new GameObject("CCFC_CityV2");Undo.RegisterCreatedObjectUndo(parent,"Place City V2");
            foreach(var path in paths)
            {
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                Undo.RegisterCreatedObjectUndo(instance,"Place City V2 sector");instance.transform.SetParent(parent.transform,false);
            }
            city=parent;Selection.activeGameObject=parent;
            status="Поставлено "+paths.Length+" секторов. Отключи старый город для сравнения; совмести центр и масштаб нового корня со старым. Меши уже имеют общие координаты.";
        }
        static bool Known(Material m)
        {
            if(m==null || m.shader==null) return false;
            string s=m.shader.name;
            if(m.renderQueue>2500 || m.IsKeywordEnabled("_ALPHATEST_ON")) return false;
            if(s=="Universal Render Pipeline/Lit" && ((m.GetTextureScale("_BaseMap")-Vector2.one).sqrMagnitude>0.00000001f || m.GetTextureOffset("_BaseMap").sqrMagnitude>0.00000001f)) return false;
            return s==AtlasShader || s=="CatCooks/ClearCity/Sunlit Atlas" || s=="CatCooks/Environment/Far City Atlas" || s=="CatCooks/CozyLook/Backdrop Atlas" ||
                (s=="Universal Render Pipeline/Lit" && m.name.StartsWith("CCFC_Atlas",StringComparison.OrdinalIgnoreCase) && m.HasProperty("_Surface") && m.GetFloat("_Surface")<.5f && !m.IsKeywordEnabled("_ALPHATEST_ON") && m.GetFloat("_AlphaClip")<.5f);
        }
        void Assign()
        {
            if(city==null || EditorUtility.IsPersistent(city) || material==null || material.shader.name!=AtlasShader)
                throw new InvalidOperationException("Нужны City root из Hierarchy и созданный материал CityV2/Atlas.");
            var renderers=city.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.sharedMaterials.Any(Known)).ToArray();
            if(renderers.Length==0) throw new InvalidOperationException("В корне нет известных атласных материалов.");
            var backup=city.GetComponent<CCCityV2MaterialBackup>();
            if(backup==null) backup=Undo.AddComponent<CCCityV2MaterialBackup>(city);
            Undo.RecordObject(backup,"City material backup");int slots=0;
            foreach(var r in renderers)
            {
                var mats=r.sharedMaterials;var link=backup.bindings.FirstOrDefault(b=>b.renderer==r);
                if(link==null) {link=new CCCityV2MaterialBackup.Binding{renderer=r,before=(Material[])mats.Clone()};backup.bindings.Add(link);}
                for(int i=0;i<mats.Length;i++) if(Known(mats[i])) {mats[i]=material;slots++;}
                link.after=(Material[])mats.Clone();Undo.RecordObject(r,"Assign City V2 atlas");r.sharedMaterials=mats;
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            }
            EditorUtility.SetDirty(backup);PrefabUtility.RecordPrefabInstancePropertyModifications(backup);EditorSceneManager.MarkSceneDirty(city.scene);
            status="Назначен общий материал: "+slots+" slots. Исходные ссылки сохранены на корне. Сохрани сцену.";
        }
        void Restore()
        {
            var backup=city!=null?city.GetComponent<CCCityV2MaterialBackup>():null;if(backup==null) return;
            foreach(var b in backup.bindings)
            {
                if(b.renderer==null || b.before==null || b.after==null) continue;
                var mats=b.renderer.sharedMaterials;
                for(int i=0;i<mats.Length && i<b.before.Length && i<b.after.Length;i++) if(mats[i]==b.after[i]) mats[i]=b.before[i];
                Undo.RecordObject(b.renderer,"Restore city atlas");b.renderer.sharedMaterials=mats;PrefabUtility.RecordPrefabInstancePropertyModifications(b.renderer);
            }
            Undo.DestroyObjectImmediate(backup);EditorSceneManager.MarkSceneDirty(city.scene);status="Исходные материалы восстановлены.";
        }
        static void Folder(string path)
        {
            if(AssetDatabase.IsValidFolder(path)) return;
            string parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
    }
}
