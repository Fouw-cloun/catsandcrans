using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FishNet.Object;
namespace CatCooks.CraneLogistics.Editor
{
    // Editor-only observer: continues working when the crane / FishNet has not started.
    public sealed class CCCraneDebugWindow:EditorWindow
    {
        [SerializeField] CCSupplyCrane _crane;
        [SerializeField] CCZoneCraneRoute _route;
        [SerializeField] bool _console=true,_drawBox=true;
        [SerializeField] string _lastPlayReport="";
        readonly Queue<string> _events=new Queue<string>();
        readonly Queue<string> _errors=new Queue<string>();
        string _report="",_summary="Выберите кран и маршрут.",_key="",_reach="Не проверялось";
        Vector2 _scroll;
        double _next,_lastProgress;
        bool _hasMotion,_capturePlay,_samplingFailed;
        readonly HashSet<string> _readNotes=new HashSet<string>();
        struct NetInfo { public bool Server,Client;public string Description; }
        NetInfo Net(NetworkBehaviour b)
        {
            // FishNet behaviour getters may dereference an Awake-cached NetworkObject.
            // In Edit mode we must not call them at all.
            if(!EditorApplication.isPlaying) return new NetInfo{Description="Edit mode: network not queried"};
            if(b==null) return new NetInfo{Description="Missing component"};
            try
            {
                if(b.NetworkObject==null) return new NetInfo{Description="NetworkObject cache unavailable (before Awake / teardown)"};
                return new NetInfo{Server=b.IsServerInitialized,Client=b.IsClientInitialized,Description="Network getters available"};
            }
            catch(NullReferenceException)
            { _readNotes.Add("Network getters not ready: "+PathOf(b));return new NetInfo{Description="FishNet not initialized / tearing down"}; }
            catch(MissingReferenceException)
            { _readNotes.Add("Object removed during network read");return new NetInfo{Description="Object removed"}; }
        }
        bool Server(NetworkBehaviour b)=>Net(b).Server;
        bool Client(NetworkBehaviour b)=>Net(b).Client;
        CCSupplyCrane.State StateOf(CCSupplyCrane c)
        {
            NetInfo n=Net(c);if(!n.Server&&!n.Client)return default;
            try{return c.Current;}
            catch(NullReferenceException){_readNotes.Add("Crane state unavailable during initialization");return default;}
            catch(MissingReferenceException){_readNotes.Add("Crane removed during state read");return default;}
        }
        bool Waiting(CCSupplyCrane c)
        { var p=StateOf(c);return Server(c)&&p.Ready&&p.Managed&&p.Phase==7; }
        static readonly string[] PhaseNames={"Loading","Rigging / boarding","Hoisting","Slewing","Lowering","Unrigging","Unloading","Waiting for territory job","Empty hoist","Empty slew","Empty lowering","Crew returning to deck","Crew approaching supply"};
        string Phase(CCSupplyCrane c)
        { var p=StateOf(c);return !p.Ready?"State unavailable / not Ready":p.Blocked?"Blocked":PhaseNames[Mathf.Clamp(p.Phase,0,12)]; }
        int StockOf(CCWorkTerritory t,CCWorkZone z,bool incoming=false)
        {
            if(z==null||t==null)return 0;
            if(!EditorApplication.isPlaying)return incoming?0:z.InitialStock;
            NetInfo n=Net(t);if(!n.Server&&!n.Client)return 0;
            try{return incoming?t.Incoming(z):t.Stock(z);}
            catch(NullReferenceException){_readNotes.Add("Stock state unavailable during initialization");return 0;}
            catch(MissingReferenceException){_readNotes.Add("Zone removed during stock read");return 0;}
        }
        static CCZoneCraneRoute[] Routes(CCWorkTerritory t)=>t!=null&&t.Routes!=null?t.Routes:Array.Empty<CCZoneCraneRoute>();
        static CCWorkZone[] Zones(CCWorkTerritory t)=>t!=null&&t.Zones!=null?t.Zones:Array.Empty<CCWorkZone>();
        CCSupplyCrane.State _previous;
        [MenuItem("Tools/CatCooks/Crane Debug/Open single-crane debugger")]
        static void Open()
        { var w=GetWindow<CCCraneDebugWindow>("Crane Debug");w.minSize=new Vector2(520,440);w.FromSelection();w.Show(); }
        void OnEnable()
        {
            EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=PlayChanged;
            Application.logMessageReceived+=LogReceived;SceneView.duringSceneGui+=DrawScene;
            _capturePlay=EditorApplication.isPlaying;_next=0;_lastProgress=EditorApplication.timeSinceStartup;
        }
        void OnDisable()
        {
            EditorApplication.update-=Tick;EditorApplication.playModeStateChanged-=PlayChanged;
            Application.logMessageReceived-=LogReceived;SceneView.duringSceneGui-=DrawScene;
        }
        void PlayChanged(PlayModeStateChange state)
        {
            Event("Unity: "+state);_next=0;
            if(state==PlayModeStateChange.ExitingPlayMode) { _capturePlay=false; }
            if(state==PlayModeStateChange.EnteredPlayMode) { _capturePlay=true;_key="";_hasMotion=false;_lastProgress=EditorApplication.timeSinceStartup; }
        }
        void LogReceived(string condition,string stack,LogType type)
        {
            if(condition.StartsWith("[CC Crane Debug]",StringComparison.Ordinal)) return;
            bool error=type==LogType.Error||type==LogType.Exception||type==LogType.Assert;
            bool relevant=condition.Contains("Crane logistics")||stack.Contains("CatCooks.CraneLogistics");
            if(!error&&!relevant) return;
            string entry=DateTime.Now.ToString("HH:mm:ss")+" "+type+": "+condition+"\n"+stack;
            if(entry.Length>5000) entry=entry.Substring(0,5000)+"\n[truncated]";
            if(_errors.Count>=20) _errors.Dequeue();_errors.Enqueue(entry);
        }
        void Event(string line)
        { if(_events.Count>=120) _events.Dequeue();_events.Enqueue(DateTime.Now.ToString("HH:mm:ss")+" "+line); }
        void FromSelection()
        {
            GameObject g=Selection.activeGameObject;if(g==null)return;
            CCZoneCraneRoute route=g.GetComponentInParent<CCZoneCraneRoute>();
            if(route!=null) { _route=route;_crane=route.Crane; }
            else
            {
                CCSupplyCrane c=g.GetComponentInParent<CCSupplyCrane>();if(c!=null) _crane=c;
                FindRoute();
            }
            _key="";_hasMotion=false;_samplingFailed=false;_next=0;
        }
        void FindRoute()
        {
            if(_crane==null||_crane.Territory==null) return;
            foreach(CCZoneCraneRoute r in _crane.Territory.GetComponentsInChildren<CCZoneCraneRoute>(true))
                if(r.Owner==_crane.Territory&&r.Crane==_crane) { _route=r;return; }
        }
        void Tick()
        { if(EditorApplication.timeSinceStartup<_next)return;_next=EditorApplication.timeSinceStartup+.5;Sample();Repaint(); }
        static string PathOf(UnityEngine.Object obj)
        {
            if(obj==null)return "<MISSING / NONE>";
            Transform t=obj is Component c?c.transform:obj is GameObject g?g.transform:null;
            if(t==null)return obj.name;
            string s=t.name;while(t.parent!=null){t=t.parent;s=t.name+"/"+s;}return s;
        }
        static bool Registered<T>(T[] list,T item) where T:UnityEngine.Object
        { if(item==null||list==null)return false;foreach(T v in list)if(v==item)return true;return false; }
        string Diagnose()
        {
            CCSupplyCrane c=_crane;CCZoneCraneRoute r=_route;
            if(c==null)return "НЕТ КРАНА: назначьте CCSupplyCrane в поле Crane.";
            if(!c.Valid(out string invalid))return "ОШИБКА КРАНА: "+invalid+" — смотрите список ссылок ниже.";
            if(!c.gameObject.activeInHierarchy)return "КОРЕНЬ КРАНА ВЫКЛЮЧЕН (либо выключен его родитель).";
            if(!c.enabled)return "КОМПОНЕНТ CCSupplyCrane ВЫКЛЮЧЕН.";
            if(r==null)return "НЕТ МАРШРУТА: назначьте Route. Кнопка Find route ищет только в территории крана.";
            if(r.Crane!=c)return "В МАРШРУТЕ НАЗНАЧЕН ДРУГОЙ КРАН.";
            CCWorkTerritory t=c.Territory;
            if(t==null)return "НЕТ TERRITORY у крана: он настроен на старый цикл, а не задания зон.";
            if(r.Owner!=t)return "МАРШРУТ НАХОДИТСЯ В ДРУГОЙ ТЕРРИТОРИИ.";
            if(!Registered(Routes(t),r))return "МАРШРУТ НЕ ЗАРЕГИСТРИРОВАН: вне Play нажмите Refresh у территории.";
            bool multi=(r.Sources!=null&&r.Sources.Length>0)||(r.Destinations!=null&&r.Destinations.Length>0);
            if(!multi)
            {
            if(r.Source==null||r.Destination==null)return "НЕ НАЗНАЧЕН SOURCE ИЛИ DESTINATION.";
            if(!Registered(Zones(t),r.Source)||!Registered(Zones(t),r.Destination))return "ЗОНЫ НЕ ЗАРЕГИСТРИРОВАНЫ: проверьте принадлежность и Refresh.";
            if(r.Source.Owner!=t||r.Destination.Owner!=t)return "ЗОНЫ ПРИНАДЛЕЖАТ ДРУГОЙ ТЕРРИТОРИИ.";
            if(!r.Source.isActiveAndEnabled||!r.Destination.isActiveAndEnabled)return "ОДНА ИЗ ЗОН ВЫКЛЮЧЕНА.";
            if(r.Source.Kind!=CCZoneKind.Supply||!r.Destination.AcceptsDelivery)return "ТИПЫ ЗОН: нужен Supply -> Receiving или Work.";
            if(r.Source.Resource!=r.Destination.Resource)return "RESOURCE У ИСТОЧНИКА И ПРИЁМНИКА НЕ СОВПАДАЕТ.";
            }
            if(!t.isActiveAndEnabled)return "ТЕРРИТОРИЯ ВЫКЛЮЧЕНА.";
            if(!EditorApplication.isPlaying)return "РЕЖИМ РЕДАКТОРА. Ссылки проверены; нажмите Check route, затем сохраните сцену и Play -> Host.";
            if(EditorApplication.isPaused)return "UNITY НА ПАУЗЕ.";
            if(Time.timeScale<=0)return "TIME.SCALE = 0: игровая симуляция остановлена.";
            if(!Server(c))
                return Server(t)?"СЕРВЕР ТЕРРИТОРИИ РАБОТАЕТ, НО КРАН НЕ ИНИЦИАЛИЗИРОВАН В СЕТИ.":"НЕТ СЕРВЕРНОЙ ИНИЦИАЛИЗАЦИИ: запустите Host; если он уже работает — проверьте сетевую загрузку сцены/объектов. На удалённом клиенте снимайте отчёт на Host.";
            if(!Server(t))return "КРАН НА СЕРВЕРЕ, НО ТЕРРИТОРИЯ НЕ ИНИЦИАЛИЗИРОВАНА В СЕТИ.";
            if(!StateOf(c).Ready)return "КРАН НЕ READY. Ошибка запуска: "+c.DebugStartupError;
            if(!StateOf(c).Managed)return "КРАН ЗАПУЩЕН БЕЗ ТЕРРИТОРИИ. Назначайте Territory вне Play, затем перезапустите.";
            if(!c.Run)return "RUN ВЫКЛЮЧЕН: кран не движется.";
            if(c.DebugDelayRemaining>0)return "START DELAY: ожидается завершение стартовой задержки.";
            if(StateOf(c).Blocked)return "ПЛАТФОРМА ЗАБЛОКИРОВАНА: "+c.DebugBlockReason+" | "+PathOf(c.DebugBlocker);
            if(Waiting(c))
            {
                if(!r.isActiveAndEnabled||!r.Enabled)return "МАРШРУТ ВЫКЛЮЧЕН.";
                if(!t.Dispatch)return "DISPATCH ВЫКЛЮЧЕН: новые задания не выдаются.";
                if(Time.time<r.NextTime)return "COOLDOWN: пауза между рейсами.";
                return "ОЖИДАНИЕ ЗАДАНИЯ: "+r.Status;
            }
            if(EditorApplication.timeSinceStartup-_lastProgress>10)return "НЕТ ИЗМЕНЕНИЙ ПОЗЫ/ПРОГРЕССА БОЛЕЕ 10 СЕКУНД. Смотрите Fixed ticks и ошибки ниже.";
            return "ВЫПОЛНЯЕТСЯ: "+Phase(c);
        }
        void Sample()
        {
            if(_samplingFailed)return;
            try{SampleCore();}
            catch(Exception error)
            {
                // Fail once; retain the previous report, never spam EditorApplication.update.
                _samplingFailed=true;
                _summary="ОШИБКА СБОРЩИКА. Автоопрос остановлен; скопируйте отчёт. Refresh report повторит попытку.";
                _report="COLLECTOR ERROR: "+error+"\n\nPREVIOUS REPORT\n"+_report;
                if(_capturePlay)_lastPlayReport=_report;
                Debug.LogError("[CC Crane Debug] "+_summary+"\n"+error);
            }
        }
        void SampleCore()
        {
            _readNotes.Clear();
            if(_crane!=null&&EditorApplication.isPlaying)
            {
                var p=StateOf(_crane);
                if(!_hasMotion||p.Phase!=_previous.Phase||p.CompletedTrips!=_previous.CompletedTrips||(p.Pose-_previous.Pose).sqrMagnitude>1e-10f||Mathf.Abs(p.Progress-_previous.Progress)>1e-7f)
                { _lastProgress=EditorApplication.timeSinceStartup;_previous=p;_hasMotion=true; }
            }
            _summary=Diagnose();
            if(_summary!=_key)
            { _key=_summary;Event(_summary);if(_console&&EditorApplication.isPlaying)Debug.Log("[CC Crane Debug] "+_summary,_crane); }
            _report=Report();
            if(EditorApplication.isPlaying&&_capturePlay)_lastPlayReport=_report;
        }
        string Report()
        {
            var b=new StringBuilder(8000);b.AppendLine("CATCOOKS CRANE DEBUG v5 direct mount — "+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            b.AppendLine("Unity "+Application.unityVersion+" | FishNet assembly "+typeof(NetworkObject).Assembly.GetName().Version);
            b.AppendLine("Play="+EditorApplication.isPlaying+" Paused="+EditorApplication.isPaused+" timeScale="+Time.timeScale+" fixedDelta="+Time.fixedDeltaTime);
            b.AppendLine("RESULT: "+_summary);b.AppendLine("Last manual reach check: "+_reach);
            var c=_crane;var r=_route;
            if(c!=null)
            {
                b.AppendLine("\nCRANE: "+PathOf(c));b.AppendLine("Scene="+c.gameObject.scene.path+" sceneLoaded="+c.gameObject.scene.isLoaded+" dirty="+c.gameObject.scene.isDirty);
                b.AppendLine("Active="+c.gameObject.activeInHierarchy+" Enabled="+c.enabled+" Run="+c.Run+" server="+Server(c)+" client="+Client(c)+" StartServerSeen="+c.DebugStartServerSeen);
                b.AppendLine("Network lifecycle="+Net(c).Description);
                b.AppendLine("StartupError="+c.DebugStartupError+" Fixed ticks="+c.DebugFixedSteps+" lastFixedUnscaled="+c.DebugLastFixedAt+" unscaledNow="+Time.unscaledTime);
                var p=StateOf(c);b.AppendLine("Ready="+p.Ready+" Managed="+p.Managed+" PhaseSeconds="+p.PhaseSeconds+" Phase="+p.Phase+" ("+Phase(c)+") Progress="+p.Progress.ToString("F4")+" Trips="+p.CompletedTrips+" Blocked="+p.Blocked);
                b.AppendLine("DirectMount="+p.DirectMount+" token="+p.MountToken+" detached="+p.DirectDetached+" size="+p.MountSize);
            b.AppendLine("Pose="+p.Pose.ToString("F3")+" DelayRemaining="+c.DebugDelayRemaining+" StartDelay="+c.StartDelay);
                b.AppendLine("DockA="+c.DockA.ToString("F3")+" DockB="+c.DockB.ToString("F3")+" travelRope="+c.TravelRope+" slingHeight="+c.SlingHeight);
                b.AppendLine("Trolley limits="+c.TrolleyRange+" Rope limits="+c.RopeRange+" speeds yaw/trolley/hoist="+c.RotationSpeed+"/"+c.TrolleySpeed+"/"+c.HoistSpeed);
                b.AppendLine("Root world scale="+c.transform.lossyScale+" up="+c.transform.up);
                Ref(b,"Tower",c.Tower);Ref(b,"Trolley",c.Trolley);Ref(b,"RopeTop",c.RopeTop);Ref(b,"HookVisual",c.HookVisual);Ref(b,"Platform",c.Platform);Ref(b,"Territory",c.Territory);
                Refs(b,"Crew",c.Crew);Refs(b,"Cargo",c.Cargo);Refs(b,"Corners",c.Corners);
                if(c.Platform!=null) { Rigidbody rb=c.Platform.GetComponent<Rigidbody>();b.AppendLine("Platform Rigidbody="+(rb!=null)+" kinematic="+(rb!=null&&rb.isKinematic)+" position="+c.Platform.position.ToString("F3")); }
                b.AppendLine("ObstacleCheck="+c.CheckObstacles+" mask="+c.ObstacleMask.value+" CargoClearance="+c.CargoClearance);
                b.AppendLine("Last obstacle reason="+c.DebugBlockReason+" object="+PathOf(c.DebugBlocker));
                if(c.DebugBlocker is Component blocker&&blocker!=null)b.AppendLine("Blocker component="+blocker.GetType().Name+" layer="+blocker.gameObject.layer+" ("+LayerMask.LayerToName(blocker.gameObject.layer)+")");
                b.AppendLine("Last query available="+c.DebugHasQuery+" centre="+c.DebugQueryCenter+" halfSize="+c.DebugQueryHalfSize+" rotation="+c.DebugQueryRotation);
                CCWorkTerritory t=c.Territory;
                if(t!=null)
                {
                    b.AppendLine("\nTERRITORY: "+PathOf(t)+" Active="+t.isActiveAndEnabled+" Dispatch="+t.Dispatch+" server="+Server(t)+" client="+Client(t));
                    b.AppendLine("Territory network lifecycle="+Net(t).Description);
                    b.AppendLine("CREW DEMANDS:\n"+t.DemandReport());
                    b.AppendLine("Dispatch ticks="+t.DebugDispatchTicks+" lastDispatchUnscaled="+t.DebugLastDispatchAt+" registeredRoutes="+Routes(t).Length+" zones="+Zones(t).Length);
                    foreach(CCZoneCraneRoute other in Routes(t)) if(other!=null)b.AppendLine("Registered route: "+PathOf(other)+" enabled="+other.Enabled+" active="+other.isActiveAndEnabled+" crane="+PathOf(other.Crane)+" InFlight="+other.InFlight+" TrafficGroup="+other.TrafficGroup+" Status="+other.Status);
                    foreach(CCWorkZone z in Zones(t)) if(z!=null)
                    { b.AppendLine("Zone: "+PathOf(z)+" kind="+z.Kind+" resource="+z.Resource+" active="+z.isActiveAndEnabled+" stock="+StockOf(t,z)+" initial="+z.InitialStock+" incoming="+StockOf(t,z,true)+" capacity="+z.Capacity);b.AppendLine("  Dock="+z.DockPosition.ToString("F3")+" Staging="+z.StagingPosition.ToString("F3")+" size="+z.Size+" scale="+z.transform.lossyScale); }
                }
            }
            b.AppendLine("\nROUTE: "+PathOf(r));
            if(r!=null)
            { Ref(b,"Crane",r.Crane);Ref(b,"Owner",r.Owner);Ref(b,"Source",r.Source);Ref(b,"Destination",r.Destination);Ref(b,"ActiveSource",r.ActiveSource);Ref(b,"ActiveDestination",r.ActiveDestination);
                for(int i=0;i<r.SourceCount;i++)Ref(b,"Configured supply["+i+"]",r.SourceAt(i));
                for(int i=0;i<r.DestinationCount;i++)Ref(b,"Configured receiving["+i+"]",r.DestinationAt(i));b.AppendLine("Status="+r.Status+" Enabled="+r.Enabled+" active="+r.isActiveAndEnabled+" InFlight="+r.InFlight+" TrafficGroup="+r.TrafficGroup+" reserved="+r.ReservedUnits+" units="+r.UnitsPerTrip+" cooldownRemaining="+Mathf.Max(0,r.NextTime-Time.time)); }
            b.AppendLine("\nNETWORK READ NOTES (unavailable values are not a confirmed stopped Host):");foreach(string note in _readNotes)b.AppendLine(note);
            b.AppendLine("\nSTATE HISTORY (up to 120 changes):");foreach(string line in _events)b.AppendLine(line);
            b.AppendLine("\nCONSOLE ERRORS / CRANE MESSAGES captured while this window was open (up to 20):");foreach(string line in _errors)b.AppendLine(line);
            return b.ToString();
        }
        static void Ref(StringBuilder b,string name,UnityEngine.Object value)
        { b.AppendLine(name+"="+PathOf(value));if(value is Component c&&c!=null)b.AppendLine("  active="+c.gameObject.activeInHierarchy); }
        static void Refs(StringBuilder b,string name,Transform[] items)
        { if(items==null){b.AppendLine(name+"=<NULL ARRAY>");return;}b.AppendLine(name+" count="+items.Length);for(int i=0;i<items.Length;i++)Ref(b,name+"["+i+"]",items[i]); }
        void OnGUI()
        {
            EditorGUILayout.HelpBox("Отладка одного крана. Откройте ДО Play, назначьте Crane и Route. Окно наблюдает; Host само не запускает и препятствия не отключает.",MessageType.Info);
            EditorGUI.BeginChangeCheck();_crane=(CCSupplyCrane)EditorGUILayout.ObjectField("Crane",_crane,typeof(CCSupplyCrane),true);_route=(CCZoneCraneRoute)EditorGUILayout.ObjectField("Route",_route,typeof(CCZoneCraneRoute),true);
            if(EditorGUI.EndChangeCheck()){_key="";_hasMotion=false;_reach="Не проверялось для выбранной пары";_samplingFailed=false;_next=0;}
            EditorGUILayout.BeginHorizontal();if(GUILayout.Button("Use selection"))FromSelection();if(GUILayout.Button("Find route")){FindRoute();_next=0;}if(GUILayout.Button("Refresh report")){_samplingFailed=false;Sample();}EditorGUILayout.EndHorizontal();
            _console=EditorGUILayout.Toggle("Log status changes",_console);_drawBox=EditorGUILayout.Toggle("Show last obstacle box",_drawBox);
            EditorGUILayout.HelpBox(_summary,MessageType.Info);
            using(new EditorGUI.DisabledScope(EditorApplication.isPlaying||_crane==null||_route==null))
            {
                if(GUILayout.Button("Check route reachability (Edit mode)"))
                { bool ok=_route.Resolve(out Vector3 a,out Vector3 b,out string error);_reach=ok?"OK: A="+a.ToString("F3")+" B="+b.ToString("F3"):error;Event("Reach: "+_reach);Sample(); }
                if(GUILayout.Button("Prepare ONLY this crane + route (Undo)"))PrepareSingle();
            }
            EditorGUILayout.BeginHorizontal();
            using(new EditorGUI.DisabledScope(_crane==null||_crane.DebugBlocker==null))
            if(GUILayout.Button("Select blocking object")){Selection.activeObject=_crane.DebugBlocker;EditorGUIUtility.PingObject(_crane.DebugBlocker);}
            if(GUILayout.Button("Copy report")){Sample();EditorGUIUtility.systemCopyBuffer=_report;ShowNotification(new GUIContent("Отчёт скопирован"));}
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            using(new EditorGUI.DisabledScope(string.IsNullOrEmpty(_lastPlayReport)))
            if(GUILayout.Button("Copy LAST PLAY report")){EditorGUIUtility.systemCopyBuffer=_lastPlayReport;ShowNotification(new GUIContent("Отчёт последнего Play скопирован"));}
            if(GUILayout.Button("Save report .txt"))
            { string path=EditorUtility.SaveFilePanel("Save crane report","","CraneDebug.txt","txt");if(!string.IsNullOrEmpty(path)){Sample();File.WriteAllText(path,_report+"\n\nLAST PLAY SNAPSHOT\n"+_lastPlayReport,Encoding.UTF8);} }
            EditorGUILayout.EndHorizontal();
            _scroll=EditorGUILayout.BeginScrollView(_scroll);EditorGUILayout.TextArea(_report,GUILayout.ExpandHeight(true),GUILayout.MinHeight(250));EditorGUILayout.EndScrollView();
        }
        void PrepareSingle()
        {
            CCWorkTerritory t=_crane.Territory;
            if(t==null||_route.Owner!=t||_route.Crane!=_crane){ShowNotification(new GUIContent("Сначала согласуйте Crane / Route / Territory"));return;}
            if(EditorUtility.IsPersistent(_crane)||!_crane.gameObject.scene.IsValid()){ShowNotification(new GUIContent("Выберите сценовый кран"));return;}
            // Never disable an ancestor of the selected crane/territory; nested rigs need manual repair.
            var all=Resources.FindObjectsOfTypeAll<CCSupplyCrane>();
            foreach(var other in all)if(other!=null&&other!=_crane&&other.Territory==t&&!EditorUtility.IsPersistent(other)&&other.gameObject.scene.IsValid())
                if(_crane.transform.IsChildOf(other.transform)||t.transform.IsChildOf(other.transform)){ShowNotification(new GUIContent("Краны вложены друг в друга — сначала исправьте иерархию"));return;}
            Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Prepare single crane debug");
            Undo.RecordObject(t,"Prepare territory");t.RefreshLocal();t.Dispatch=true;t.enabled=true;
            Undo.RecordObject(t.gameObject,"Enable territory");t.gameObject.SetActive(true);
            foreach(var other in all)if(other!=null&&other!=_crane&&other.Territory==t&&!EditorUtility.IsPersistent(other)&&other.gameObject.scene.IsValid())
            { Undo.RecordObject(other,"Pause other crane");Undo.RecordObject(other.gameObject,"Disable other crane");other.Run=false;other.gameObject.SetActive(false);EditorUtility.SetDirty(other);EditorSceneManager.MarkSceneDirty(other.gameObject.scene); }
            foreach(CCZoneCraneRoute r in Routes(t))if(r!=null){Undo.RecordObject(r,"Select only debug route");r.Enabled=r==_route;EditorUtility.SetDirty(r);}
            Undo.RecordObject(_crane,"Prepare selected crane");Undo.RecordObject(_crane.gameObject,"Enable selected crane");_crane.gameObject.SetActive(true);_crane.enabled=true;_crane.Run=true;_crane.StartDelay=0;
            Undo.RecordObject(_route,"Enable selected route");Undo.RecordObject(_route.gameObject,"Enable selected route");_route.gameObject.SetActive(true);_route.enabled=true;
            EditorUtility.SetDirty(_crane);EditorUtility.SetDirty(t);EditorSceneManager.MarkSceneDirty(_crane.gameObject.scene);EditorSceneManager.MarkSceneDirty(t.gameObject.scene);Undo.CollapseUndoOperations(undo);
            Event("Single-crane mode prepared. No objects deleted, no stock/geometry edits. SAVE scene before Host. Ctrl+Z can undo preparation.");Sample();
        }
        void DrawScene(SceneView view)
        {
            if(!_drawBox||_crane==null||!EditorApplication.isPlaying||!_crane.DebugHasQuery)return;
            Matrix4x4 old=Handles.matrix;Color color=Handles.color;
            Handles.matrix=Matrix4x4.TRS(_crane.DebugQueryCenter,_crane.DebugQueryRotation,Vector3.one);Handles.color=StateOf(_crane).Blocked?Color.red:Color.cyan;Handles.DrawWireCube(Vector3.zero,_crane.DebugQueryHalfSize*2);Handles.matrix=old;
            Handles.Label(_crane.DebugQueryCenter,"Last load obstacle query: "+_crane.DebugBlockReason);Handles.color=color;
        }
    }
}
