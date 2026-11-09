using System;
using UnityEditor;
using UnityEngine;

// Each inspector owns its preview scene. Gameplay emitters and scene particles are never reused.
public sealed class CatCooksDustPreview : IDisposable
{
    PreviewRenderUtility render;
    GameObject root;
    CatCooksDustSystem world;
    double lastTime;
    float nextEvent, elapsed;
    Vector2 orbit = new Vector2(25,25);
    float zoom=1;
    bool playing=true, repeat=true;
    public float speed=1;
    public float exhaustLoad=.35f;
    public CatCooksDustSystem.Effect profileEffect=CatCooksDustSystem.Effect.Burst;
    public bool Playing => playing;
    public void Restart() { elapsed=0; nextEvent=0; lastTime=EditorApplication.timeSinceStartup; if(world!=null) world.ClearParticles(); }
    public void Toolbar()
    {
        using(new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            playing=GUILayout.Toggle(playing,"▶ Play",EditorStyles.toolbarButton,GUILayout.Width(65));
            bool repeatNext=GUILayout.Toggle(repeat,"Повтор",EditorStyles.toolbarButton,GUILayout.Width(65));
            if(repeatNext && !repeat && float.IsPositiveInfinity(nextEvent)) nextEvent=elapsed;
            repeat=repeatNext;
            if(GUILayout.Button("Сначала",EditorStyles.toolbarButton,GUILayout.Width(65))) { Restart(); playing=true; }
            GUILayout.Label("Скорость",GUILayout.Width(60)); speed=GUILayout.HorizontalSlider(speed,.15f,1.5f,GUILayout.Width(65));
        }
    }
    void Ensure()
    {
        if(render!=null) return;
        render=new PreviewRenderUtility();
        render.camera.clearFlags=CameraClearFlags.SolidColor;
        render.camera.backgroundColor=new Color(.12f,.15f,.19f);
        render.camera.nearClipPlane=.03f; render.camera.farClipPlane=200;
        render.camera.fieldOfView=35;
        root=new GameObject("CCD isolated preview") { hideFlags=HideFlags.HideAndDontSave };
        root.SetActive(false);
        render.AddSingleGO(root);
        world=root.AddComponent<CatCooksDustSystem>();
        world.isolatedPreview=true; world.maxParticles=512; world.wind=new Vector3(.07f,0,.02f);
        world.puffShader=Shader.Find("CatCooks/Dust and Smoke/Toon Puff");
        root.SetActive(true); world.BeginIsolatedPreview();
        Restart();
    }
    public void Draw(Rect rect, CatCooksDustEmitter emitter, CatCooksDustProfile profile)
    {
        if(rect.width<10 || rect.height<10) return;
        Ensure();
        Event e=Event.current;
        if(rect.Contains(e.mousePosition))
        {
            if(e.type==EventType.MouseDrag && e.button==0)
            { orbit.x+=e.delta.x*.6f; orbit.y=Mathf.Clamp(orbit.y+e.delta.y*.45f,-5,85); e.Use(); }
            if(e.type==EventType.ScrollWheel) { zoom=Mathf.Clamp(zoom*Mathf.Exp(e.delta.y*.055f),.2f,5); e.Use(); }
        }
        if(e.type!=EventType.Repaint) return;
        double now=EditorApplication.timeSinceStartup;
        float dt=Mathf.Clamp((float)(now-lastTime),0,.1f)*speed; lastTime=now;
        if(playing)
        {
            if(elapsed>=nextEvent)
            {
                var exhaust=emitter==null ? null : emitter.GetComponent<CatCooksVehicleExhaust>();
                if(exhaust!=null && exhaust.enabled) exhaust.EmitPreview(world,Vector3.zero,Quaternion.identity,exhaustLoad,elapsed);
                else if(emitter!=null) emitter.EmitInto(world,Vector3.zero,Quaternion.identity);
                else world.Emit(profileEffect,Vector3.zero,Vector3.up,Vector3.zero,1,profile);
                bool driven=exhaust!=null && exhaust.enabled;
                bool continuous=driven || (emitter!=null && emitter.loop);
                float period=driven ? 1/exhaust.EventRate(exhaustLoad,elapsed) : continuous ? 1/Mathf.Clamp(emitter.eventsPerSecond,.2f,20) : 4.5f;
                if(emitter!=null && !continuous && emitter.layers!=null)
                    foreach(var layer in emitter.layers) if(layer!=null && layer.enabled) period=Mathf.Max(period,layer.delay+3.5f);
                nextEvent=repeat ? elapsed+period : float.PositiveInfinity;
            }
            world.SimulateIsolatedPreview(dt); elapsed+=dt;
        }
        float scale=emitter!=null ? Mathf.Clamp(emitter.scale,.1f,8) : 1;
        float area=emitter!=null ? Mathf.Max(emitter.footprint.x,emitter.footprint.y)*scale : 0;
        float distance=Mathf.Max(3.5f*scale,area*1.8f+scale*2)*zoom;
        Vector3 focus=new Vector3(0,scale*.65f,0);
        var rotation=Quaternion.Euler(orbit.y,orbit.x,0);
        render.camera.transform.SetPositionAndRotation(focus+rotation*new Vector3(0,0,-distance),rotation);
        render.BeginPreview(rect,GUIStyle.none);
        render.camera.aspect=rect.width/rect.height;
        // Explicit SRP path: the supplied shader is URP, not built-in rendering.
        render.Render(true);
        GUI.DrawTexture(rect,render.EndPreview(),ScaleMode.StretchToFill,false);
        GUI.Label(new Rect(rect.x+8,rect.y+6,rect.width-16,22),$"{world.LiveCount} частиц · ЛКМ: вращать · колесо: приблизить",EditorStyles.whiteMiniLabel);
    }
    public void Dispose()
    {
        if(world!=null) world.StopPreview();
        if(root!=null) UnityEngine.Object.DestroyImmediate(root);
        if(render!=null) render.Cleanup();
        render=null; root=null; world=null;
    }
}

[CustomEditor(typeof(CatCooksDustEmitter))]
public sealed class CatCooksDustEmitterEditor : Editor
{
    CatCooksDustPreview preview;
    bool editProfile;
    void OnDisable() { preview?.Dispose(); preview=null; }
    public override bool RequiresConstantRepaint() => preview!=null && preview.Playing;
    public override void OnInspectorGUI()
    {
        var emitter=(CatCooksDustEmitter)target;
        EditorGUI.BeginChangeCheck(); DrawDefaultInspector();
        bool changed=EditorGUI.EndChangeCheck();
        EditorGUILayout.HelpBox("Tint умножает палитру профиля. Layers добавляют независимые клубы и крошки в этот же эффект. Scale меняет размер; Amount — количество; Sources распределяет эффект по Footprint.",MessageType.Info);
        if(emitter.profile!=null)
        {
            if(GUILayout.Button("Создать личную копию профиля для этого эмиттера"))
            {
                string original=AssetDatabase.GetAssetPath(emitter.profile);
                string folder=string.IsNullOrEmpty(original) ? "Assets" : System.IO.Path.GetDirectoryName(original).Replace('\\','/');
                string path=AssetDatabase.GenerateUniqueAssetPath(folder+"/"+emitter.profile.name+"_Custom.asset");
                var copy=Instantiate(emitter.profile); AssetDatabase.CreateAsset(copy,path);
                Undo.RecordObject(emitter,"Assign custom dust profile"); emitter.profile=copy;
                if(PrefabUtility.IsPartOfPrefabInstance(emitter)) PrefabUtility.RecordPrefabInstancePropertyModifications(emitter);
                EditorUtility.SetDirty(emitter); changed=true;
            }
            editProfile=EditorGUILayout.Foldout(editProfile,"Настроить профиль здесь (общий для всех его пользователей)",true);
            if(editProfile)
            {
                var so=new SerializedObject(emitter.profile); so.Update();
                var property=so.GetIterator(); bool enter=true;
                while(property.NextVisible(enter))
                {
                    enter=false; if(property.name=="m_Script") continue;
                    EditorGUILayout.PropertyField(property,true);
                }
                changed|=so.ApplyModifiedProperties();
            }
        }
        if(GUILayout.Button("Открыть большой предпросмотр")) CatCooksDustPreviewWindow.Open(emitter);
        if(preview==null) preview=new CatCooksDustPreview();
        if(changed) preview.Restart();
        if(emitter.GetComponent<CatCooksVehicleExhaust>()!=null) preview.exhaustLoad=EditorGUILayout.Slider("Preview: нагрузка двигателя",preview.exhaustLoad,0,1);
        preview.Toolbar(); preview.Draw(GUILayoutUtility.GetRect(100,270,GUILayout.ExpandWidth(true)),emitter,null);
        if(Application.isPlaying && !EditorUtility.IsPersistent(emitter) && GUILayout.Button("Испустить в игровой сцене")) emitter.PlayEffect();
    }
}

[CustomEditor(typeof(CatCooksDustProfile))]
public sealed class CatCooksDustProfileEditor : Editor
{
    CatCooksDustPreview preview;
    void OnDisable() { preview?.Dispose(); preview=null; }
    public override bool RequiresConstantRepaint() => preview!=null && preview.Playing;
    public override void OnInspectorGUI()
    {
        if(preview==null) preview=new CatCooksDustPreview();
        EditorGUI.BeginChangeCheck(); DrawDefaultInspector();
        preview.profileEffect=(CatCooksDustSystem.Effect)EditorGUILayout.EnumPopup("Событие для пробы",preview.profileEffect);
        if(EditorGUI.EndChangeCheck()) preview.Restart();
        if(GUILayout.Button("Открыть большой предпросмотр")) CatCooksDustPreviewWindow.Open(target);
        preview.Toolbar(); preview.Draw(GUILayoutUtility.GetRect(100,250,GUILayout.ExpandWidth(true)),null,(CatCooksDustProfile)target);
    }
}

public sealed class CatCooksDustPreviewWindow : EditorWindow
{
    [SerializeField] UnityEngine.Object source;
    CatCooksDustPreview preview;
    public static void Open(UnityEngine.Object source)
    {
        var window=GetWindow<CatCooksDustPreviewWindow>("Dust & Smoke Preview");
        window.source=source; window.preview?.Restart(); window.Show();
    }
    void OnDisable() { preview?.Dispose(); preview=null; }
    void Update() { if(preview!=null && preview.Playing) Repaint(); }
    void OnGUI()
    {
        var next=EditorGUILayout.ObjectField("Эмиттер или профиль",source,typeof(UnityEngine.Object),true);
        if(next!=source) { source=next; preview?.Restart(); }
        var emitter=source as CatCooksDustEmitter;
        if(source is CatCooksVehicleExhaust exhaust) emitter=exhaust.GetComponent<CatCooksDustEmitter>();
        if(source is GameObject go) emitter=go.GetComponent<CatCooksDustEmitter>();
        var profile=source as CatCooksDustProfile;
        if(emitter==null && profile==null) { EditorGUILayout.HelpBox("Выбери prefab с CatCooksDustEmitter либо профиль.",MessageType.Info); return; }
        if(preview==null) preview=new CatCooksDustPreview();
        if(emitter==null) preview.profileEffect=(CatCooksDustSystem.Effect)EditorGUILayout.EnumPopup("Событие",preview.profileEffect);
        if(emitter!=null && emitter.GetComponent<CatCooksVehicleExhaust>()!=null) preview.exhaustLoad=EditorGUILayout.Slider("Нагрузка двигателя",preview.exhaustLoad,0,1);
        preview.Toolbar();
        Rect r=GUILayoutUtility.GetRect(100,100,GUILayout.ExpandWidth(true),GUILayout.ExpandHeight(true));
        preview.Draw(r,emitter,profile);
    }
}

[CustomEditor(typeof(CatCooksVehicleExhaust))]
public sealed class CatCooksVehicleExhaustEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("Поставь prefab у среза трубы: зелёная ось Y направлена наружу. Vehicle Root — корень техники. Observed Movement оценивает нагрузку по движению; точный газ и включение двигателя передавай через SetEngineState. Палитра и слои на соседнем CatCooksDustEmitter.",MessageType.Info);
        if(GUILayout.Button("Предпросмотр выхлопа с настройкой нагрузки")) CatCooksDustPreviewWindow.Open(target);
    }
}
