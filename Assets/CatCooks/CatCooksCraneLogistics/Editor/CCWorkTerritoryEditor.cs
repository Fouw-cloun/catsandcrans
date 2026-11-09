using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using FishNet.Object;
namespace CatCooks.CraneLogistics.Editor
{
    public static class CCWorkTerritorySetup
    {
        [MenuItem("Tools/CatCooks/New Territory/1 - Create independent territory")]
        static void Create()
        {
            if(Application.isPlaying) return;
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Create independent territory");
            GameObject g=new GameObject("NEW TERRITORY - move to your new floor");Undo.RegisterCreatedObjectUndo(g,"Create territory");
            if(Selection.activeTransform!=null) g.transform.position=Selection.activeTransform.position;
            else if(SceneView.lastActiveSceneView!=null) g.transform.position=SceneView.lastActiveSceneView.pivot;
            Undo.AddComponent<NetworkObject>(g);CCWorkTerritory site=Undo.AddComponent<CCWorkTerritory>(g);
            site.Dispatch=false;
            Add(site,CCZoneKind.Supply,new Vector3(-12,0,0));
            Add(site,CCZoneKind.Receiving,new Vector3(0,0,0));
            Add(site,CCZoneKind.Work,new Vector3(12,0,0));
            Add(site,CCZoneKind.Rest,new Vector3(0,0,12));
            CCWorkZone forbidden=Add(site,CCZoneKind.Exclusion,new Vector3(12,0,12));forbidden.Size=new Vector3(6,8,6);
            site.RefreshLocal();EditorUtility.SetDirty(site);EditorSceneManager.MarkSceneDirty(g.scene);Undo.CollapseUndoOperations(group);Selection.activeGameObject=g;
        }
        public static CCWorkZone Add(CCWorkTerritory site,CCZoneKind kind,Vector3 position)
        {
            GameObject g=new GameObject(kind+" zone");Undo.RegisterCreatedObjectUndo(g,"Create zone");g.transform.SetParent(site.transform,false);g.transform.localPosition=position;
            CCWorkZone z=Undo.AddComponent<CCWorkZone>(g);z.Kind=kind;z.Label=kind.ToString();z.InitialStock=kind==CCZoneKind.Supply?20:0;
            if(kind!=CCZoneKind.Exclusion&&kind!=CCZoneKind.Rest)
            { z.Dock=Point("Dock - platform centre at floor height",g.transform,new Vector3(-2,0,0));z.Staging=Point("Staging - cargo and crew",g.transform,new Vector3(2,0,0)); }
            if(kind==CCZoneKind.Work)
            { z.WorkPoints=new Transform[4];for(int i=0;i<4;i++) z.WorkPoints[i]=Point("Work slot "+(i+1),g.transform,new Vector3((i%2==0?-1:1)*2,0,(i<2?-1:1)*2)); }
            return z;
        }
        static Transform Point(string name,Transform parent,Vector3 position)
        { GameObject g=new GameObject(name);Undo.RegisterCreatedObjectUndo(g,"Create zone point");g.transform.SetParent(parent,false);g.transform.localPosition=position;return g.transform; }
    }
    [CustomEditor(typeof(CCWorkTerritory))]
    public sealed class CCWorkTerritoryInspector:UnityEditor.Editor
    {
        CCSupplyCrane _crane;
        CCZoneKind _kind;
        public override void OnInspectorGUI()
        {
            CCWorkTerritory s=(CCWorkTerritory)target;DrawDefaultInspector();
            EditorGUILayout.HelpBox("Only child zones/routes of THIS root are registered. No old builder director, floor generator or global search. Keep root upright, scale 1. Set up outside Play.",MessageType.Info);
            if(!Application.isPlaying)
            {
                if(GUILayout.Button("Refresh THIS territory zones / routes")) { Undo.RecordObject(s,"Refresh territory");s.RefreshLocal();Dirty(s); }
                _kind=(CCZoneKind)EditorGUILayout.EnumPopup("New zone type",_kind);
                if(GUILayout.Button("Add zone here")) { CCWorkZone z=CCWorkTerritorySetup.Add(s,_kind,Vector3.zero);Undo.RecordObject(s,"Register zone");s.RefreshLocal();Dirty(s);Selection.activeGameObject=z.gameObject; }
                _crane=(CCSupplyCrane)EditorGUILayout.ObjectField("Crane for new route",_crane,typeof(CCSupplyCrane),true);
                using(new EditorGUI.DisabledScope(_crane==null))
                if(GUILayout.Button("Assign this crane + create route"))
                {
                    if(_crane.Territory!=null&&_crane.Territory!=s) { Debug.LogError("Crane already belongs to another territory. Clear that assignment explicitly first.",_crane);return; }
                    Undo.RecordObject(_crane,"Assign crane territory");_crane.Territory=s;_crane.Run=false;
                    GameObject g=new GameObject("Route - "+_crane.name);Undo.RegisterCreatedObjectUndo(g,"Create route");g.transform.SetParent(s.transform,false);
                    CCZoneCraneRoute r=Undo.AddComponent<CCZoneCraneRoute>(g);r.Crane=_crane;
                    foreach(CCWorkZone z in s.Zones) if(z!=null) { if(r.Source==null&&z.Kind==CCZoneKind.Supply)r.Source=z;if(r.Destination==null&&z.AcceptsDelivery)r.Destination=z; }
                    Undo.RecordObject(s,"Register route");s.RefreshLocal();Dirty(s);Dirty(_crane);Selection.activeGameObject=g;
                }
            }
            EditorGUILayout.Space();EditorGUILayout.LabelField("Local stock (units / reserved incoming)",EditorStyles.boldLabel);
            foreach(CCWorkZone z in s.Zones) if(z!=null&&z.Kind!=CCZoneKind.Exclusion&&z.Kind!=CCZoneKind.Rest)
            {
                int count=Application.isPlaying?s.Stock(z):z.InitialStock;
                EditorGUILayout.LabelField(z.Label,count+" / "+z.Capacity+"   incoming "+(Application.isPlaying?s.Incoming(z):0));
                if(Application.isPlaying&&s.NetworkObject!=null&&s.IsServerInitialized)
                {
                    EditorGUILayout.BeginHorizontal();
                    if(GUILayout.Button("Add 2: "+z.Label)&&!s.ServerAddStock(z,2)) Debug.LogWarning("Cannot add: capacity or reservation.",s);
                    if(GUILayout.Button("Consume 2: "+z.Label)&&!s.ServerConsume(z,2)) Debug.LogWarning("Not enough stock.",s);
                    EditorGUILayout.EndHorizontal();
                }
            }
            foreach(CCZoneCraneRoute r in s.Routes) if(r!=null) EditorGUILayout.LabelField(r.name,r.Status);
            if(Application.isPlaying){EditorGUILayout.HelpBox(s.DemandReport(),MessageType.None);Repaint();}
        }
        static void Dirty(Component c) { EditorUtility.SetDirty(c);EditorSceneManager.MarkSceneDirty(c.gameObject.scene); }
    }
    [CustomEditor(typeof(CCWorkZone))]
    public sealed class CCWorkZoneInspector:UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();CCWorkZone z=(CCWorkZone)target;
            if(z.Owner==null) EditorGUILayout.HelpBox("Place this zone below a CCWorkTerritory root.",MessageType.Error);
            EditorGUILayout.HelpBox("Size changes zone boundaries only, not level geometry. Zone origin is floor Y. Dock = platform centre; Staging = cargo/crew on same floor. Work slots are authoring points for a later worker controller.",MessageType.Info);
        }
        void OnSceneGUI()
        {
            CCWorkZone z=(CCWorkZone)target;
            if(Application.isPlaying)return;
            Handles.color=z.Tint;Vector3 centre=z.transform.position+z.transform.up*z.Size.y*.5f;
            Handles.Label(z.transform.position+Vector3.up*(z.Size.y+.2f),z.Label+" / "+z.Kind);
            EditorGUI.BeginChangeCheck();Vector3 size=Handles.ScaleHandle(z.Size,centre,z.transform.rotation,HandleUtility.GetHandleSize(centre));
            if(EditorGUI.EndChangeCheck()) { Undo.RecordObject(z,"Resize work zone");z.Size=new Vector3(Mathf.Max(.5f,size.x),Mathf.Max(.1f,size.y),Mathf.Max(.5f,size.z));EditorUtility.SetDirty(z); }
            Handle(z.Dock,"DOCK");Handle(z.Staging,"STAGING");
            if(z.WorkPoints!=null) foreach(Transform p in z.WorkPoints) Handle(p,"WORK");
        }
        static void Handle(Transform t,string label)
        {
            if(t==null) return;Handles.Label(t.position+Vector3.up*.3f,label);EditorGUI.BeginChangeCheck();Vector3 p=Handles.PositionHandle(t.position,Quaternion.identity);
            if(EditorGUI.EndChangeCheck()) { Undo.RecordObject(t,"Move zone point");t.position=p;EditorUtility.SetDirty(t); }
        }
    }
    [CustomEditor(typeof(CCZoneCraneRoute))]
    public sealed class CCZoneCraneRouteInspector:UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();CCZoneCraneRoute r=(CCZoneCraneRoute)target;
            if(Application.isPlaying) {
                EditorGUILayout.LabelField("Status",r.Status);
                if(r.InFlight)EditorGUILayout.HelpBox("Active: "+(r.ActiveSource!=null?r.ActiveSource.name:"?")+" -> "+(r.ActiveMount!=null?"DIRECT #"+r.ActiveMount.Token+" "+r.ActiveMount.Top:(r.ActiveDestination!=null?r.ActiveDestination.name:"?"))+" / units "+r.ReservedUnits,MessageType.Info);
                Repaint();return;
            }
            EditorGUILayout.HelpBox("v5: Steel uses direct installation targets on Frame crew, never Receiving. Validate ALL direct targets there. Sources/Destinations are explicit per-crane lists. Non-empty lists replace the single fields for dispatch. Source/Destination below are also the initial dock/preview pair. Traffic Group must be empty for independent parallel work.",MessageType.Info);
            if(GUILayout.Button("Validate ALL configured point pairs"))
            {
                int valid=0;
                for(int si=0;si<r.SourceCount;si++)for(int di=0;di<r.DestinationCount;di++)
                {
                    var source=r.SourceAt(si);var dest=r.DestinationAt(di);
                    if(source!=null&&dest!=null&&source.Resource!=dest.Resource)continue;
                    if(r.ResolvePair(source,dest,out var pa,out var pb,out string why))valid++;
                    else Debug.LogWarning("[Crane points] "+(source!=null?source.name:"NULL")+" -> "+(dest!=null?dest.name:"NULL")+": "+why,r);
                }
                Debug.Log("[Crane points] Reachable matching pairs: "+valid+". Check travel clearance for ALL paths separately.",r);
            }
            if(GUILayout.Button("Enable THIS route and crane for parallel work (Edit)"))
            {
                if(r.Crane==null||r.Owner==null){Debug.LogError("Assign Crane and put route under territory.",r);return;}
                if(r.Crane.Territory!=null&&r.Crane.Territory!=r.Owner){Debug.LogError("Crane belongs to another territory.",r);return;}
                Undo.RecordObjects(new Object[]{r,r.gameObject,r.Crane,r.Crane.gameObject,r.Owner},"Enable crane route");
                r.enabled=true;r.Enabled=true;r.gameObject.SetActive(true);r.TrafficGroup="";
                r.Crane.enabled=true;r.Crane.Run=true;r.Crane.gameObject.SetActive(true);r.Crane.Territory=r.Owner;r.Crane.StartDelay=0;
                r.Owner.Dispatch=true;r.Owner.RefreshLocal();
                EditorUtility.SetDirty(r);EditorUtility.SetDirty(r.Crane);EditorUtility.SetDirty(r.Owner);
                EditorSceneManager.MarkSceneDirty(r.gameObject.scene);EditorSceneManager.MarkSceneDirty(r.Crane.gameObject.scene);
                if(!r.gameObject.activeInHierarchy||!r.Crane.gameObject.activeInHierarchy)Debug.LogWarning("A parent is inactive: enable that parent manually.",r);
            }
            bool ok=r.Resolve(out Vector3 a,out Vector3 b,out string error);
            EditorGUILayout.HelpBox(ok?"Both docks reachable. Check travel path / beams separately.":error,ok?MessageType.Info:MessageType.Warning);
            using(new EditorGUI.DisabledScope(!ok))
            {
                if(GUILayout.Button("Fit crane docks to these zones"))
                {
                    CCSupplyCrane c=r.Crane;Undo.RecordObject(c,"Fit zone route");c.DockA=a;c.DockB=b;
                    c.StorageA=c.transform.InverseTransformDirection(r.Source.StagingPosition-r.Source.DockPosition);c.StorageB=c.transform.InverseTransformDirection(r.Destination.StagingPosition-r.Destination.DockPosition);
                    EditorUtility.SetDirty(c);EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
                }
                if(GUILayout.Button("Preview pickup")) Preview(r.Crane,a);
                if(GUILayout.Button("Preview delivery")) Preview(r.Crane,b);
            }
            EditorGUILayout.HelpBox("Enable crane Run AND territory Dispatch after setup. Source decreases at dispatch, destination increases after completed unloading. No automatic use by old builder cats.",MessageType.None);
        }
        static void Preview(CCSupplyCrane c,Vector3 pose)
        {
            Transform[] parts=c.GetComponentsInChildren<Transform>(true);Object[] objects=new Object[parts.Length];
            for(int i=0;i<parts.Length;i++) objects[i]=parts[i];
            Undo.RecordObjects(objects,"Preview zone dock");c.Preview(pose);EditorSceneManager.MarkSceneDirty(c.gameObject.scene);SceneView.RepaintAll();
        }
    }
    public static class CCZoneLabels
    {
        [DrawGizmo(GizmoType.NonSelected|GizmoType.Selected)]
        static void Draw(CCWorkZone z,GizmoType type)
        { Handles.color=z.Tint;Handles.Label(z.transform.position+Vector3.up*.15f,z.Label+" ["+z.Kind+"]"); }
    }
}
