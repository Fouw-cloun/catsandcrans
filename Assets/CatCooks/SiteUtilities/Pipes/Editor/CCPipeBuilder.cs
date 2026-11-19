using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FishNet.Object;
using static CatCooks.SiteTools.CCUtilityArt;

namespace CatCooks.SiteTools
{
    public static class CCPipeBuilder
    {
        internal static readonly string[] Colors={"367FAB","C05037","515F4E"};
        internal static readonly float[] Radii={.405f,.51f,.705f};
        const string Steel="56616A", Bolt="ABB8B9", Yellow="DF9D3E";
        // Parallel transport keeps lines distinct on vertical runs and on successive bends.
        internal static void Rebuild(CCPipeRun run)
        {
            if(run.geometry!=null) Undo.DestroyObjectImmediate(run.geometry.gameObject);
            var g=Node(run.transform,"Три магистрали",Vector3.zero);
            Undo.RegisterCreatedObjectUndo(g.gameObject,"Пересобрать трубы");
            Undo.RecordObject(run,"Трубы");
            run.geometry=g;
            var p=run.points;
            if(p.Count<2)return;
            float spacing=Mathf.Max(1.52f,run.spacing);
            var samples=new List<Vector3>{p[0]};
            var indices=new List<int>{0};
            for(int i=1;i<p.Count-1;i++){
                Vector3 incoming=p[i]-p[i-1], outgoing=p[i+1]-p[i];
                float trim=Mathf.Min(Mathf.Min(incoming.magnitude,outgoing.magnitude)*.44f,spacing*2.3f);
                Vector3 a=p[i]-incoming.normalized*trim,b=p[i]+outgoing.normalized*trim;
                for(int j=0;j<=8;j++){
                    float u=j/8f;
                    samples.Add(Vector3.Lerp(Vector3.Lerp(a,p[i],u),Vector3.Lerp(p[i],b,u),u));
                    if(j==4)indices.Add(samples.Count-1);
                }
            }
            indices.Add(samples.Count);samples.Add(p[p.Count-1]);
            Vector3 previous=(samples[1]-samples[0]).normalized;
            Vector3 side=Vector3.Cross(Vector3.up,previous);
            if(side.sqrMagnitude<.001f)side=Vector3.right;
            side.Normalize();
            var centers=new Vector3[samples.Count,3];
            var tangents=new Vector3[samples.Count];
            for(int i=0;i<samples.Count;i++){
                var tangent=(samples[Mathf.Min(i+1,samples.Count-1)]-samples[Mathf.Max(i-1,0)]).normalized;
                side=Quaternion.FromToRotation(previous,tangent)*side;previous=tangent;tangents[i]=tangent;
                for(int k=0;k<3;k++){
                    var c=samples[i]+side*((k-1)*spacing);
                    centers[i,k]=c;
                    if(i>0)Rod(g,"Секция",centers[i-1,k],c,Radii[k],Colors[k],true);
                    bool flange=i==0 || i==samples.Count-1;
                    if(flange)Rod(g,"Фланец",c-tangent*.045f,c+tangent*.045f,Radii[k]+.045f,Steel);
                    else Shape(g,"Отвод",PrimitiveType.Sphere,c,Vector3.one*2*Radii[k],Colors[k]);
                }
            }
            foreach(var fitting in run.GetComponentsInChildren<CCPipeFitting>(true)){
                if(fitting.node<0 || fitting.node>=indices.Count || fitting.line<0 || fitting.line>=3)continue;
                int i=indices[fitting.node];
                Undo.RecordObject(fitting.transform,"Переместить арматуру");
                // Raise legacy fitting internals to the new pipe surface on their first rebuild.
                var mount=fitting.transform.Find("Корпус арматуры");
                if(mount==null){
                    int kind=fitting.GetComponent<CCInspectionHatch>()!=null?2:fitting.GetComponentsInChildren<HydrantNut>(true).Length>1?1:0;
                    var rebuilt=BuildGridFitting(run.transform,fitting.line,kind);
                    var anchor=rebuilt.gameObject.AddComponent<CCPipeFitting>();
                    anchor.node=fitting.node;anchor.line=fitting.line;
                    anchor.mountingAngles=fitting.mountingAngles;
                    OrientMount(rebuilt,anchor.line,anchor.mountingAngles);
                    rebuilt.localPosition=centers[i,anchor.line];
                    rebuilt.localRotation=Quaternion.FromToRotation(Vector3.forward,tangents[i]);
                    Undo.RegisterCreatedObjectUndo(rebuilt.gameObject,"Обновить арматуру");
                    Undo.DestroyObjectImmediate(fitting.gameObject);
                    continue;
                }
                if(fitting.line==0) CCColdPipeSetup.Configure(fitting.transform,Vector3Int.zero);
                OrientMount(fitting.transform,fitting.line,fitting.mountingAngles,true);
                fitting.transform.localPosition=centers[i,fitting.line];
                fitting.transform.localRotation=Quaternion.FromToRotation(Vector3.forward,tangents[i]);
            }
            EditorUtility.SetDirty(run);
        }
        internal static Vector2 AngleControls(Vector2 angles)
        {
            angles.x = EditorGUILayout.Slider("Вокруг трубы, °", angles.x, -180f, 180f);
            angles.y = EditorGUILayout.Slider("Разворот детали, °", angles.y, -180f, 180f);
            if (GUILayout.Button("Сбросить углы")) return Vector2.zero;
            return angles;
        }

        internal static void OrientMount(Transform fitting, int line, Vector2 angles, bool recordUndo=false)
        {
            var mount = fitting.Find("Корпус арматуры");
            if (fitting.name=="Муфта" && mount.Find("Прижим")==null) ClampEar(mount,recordUndo);
            if (recordUndo) Undo.RecordObject(mount, "Поворот арматуры");
            var roll = Quaternion.AngleAxis(angles.x, Vector3.forward);
            mount.localPosition = roll * (Vector3.up * (Radii[line] - .08f));
            mount.localRotation = roll * Quaternion.AngleAxis(angles.y, Vector3.up);
            if (recordUndo) PrefabUtility.RecordPrefabInstancePropertyModifications(mount);
        }

        static void ClampEar(Transform mount,bool recordUndo)
        {
            var ear=Node(mount,"Прижим",V(0,.19f,0));
            Box(ear,"Колодка",Vector3.zero,V(.24f,.105f,.27f),Steel);
            foreach(float x in new[]{-.071f,.071f})
                Rod(ear,"Болт",V(x,.045f,0),V(x,.08f,0),.035f,Bolt);
            if(recordUndo) Undo.RegisterCreatedObjectUndo(ear.gameObject,"Прижим муфты");
        }

        internal static void Wheel(Transform parent,Vector3 at,float radius,string color)
        {
            Art(parent,"Assets/CatCooks/SiteUtilities/Pipes/Models/ServiceHandwheel.fbx",at,radius*2);
        }
        static HydrantNut Control(Transform root,string name,Vector3 at,int id,bool outlet,HydrantNut valve=null)
        {
            var n=Node(root,name,at);
            var joint=Node(n,"Шпиндель",Vector3.zero);
            var seat=Node(n,"Посадка",outlet?Vector3.up*.18f:Vector3.up*.08f);
            var nut=n.gameObject.AddComponent<HydrantNut>();
            Int(nut,"_kind",outlet?(int)HydrantNut.NutKind.SideOutlet:(int)HydrantNut.NutKind.TopValve);
            Set(nut,"_nutPivot",joint);Set(nut,"_wrenchSeat",seat);Set(nut,"_ringSeat",seat);
            if(valve!=null)Set(nut,"_topValve",valve);
            if(outlet){
                seat.localRotation=Quaternion.Euler(-90,0,0);
                var capPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/cap.prefab");
                if(capPrefab!=null && capPrefab.GetComponent<HydrantCapItem>()!=null)Set(nut,"_capPrefab",capPrefab);
                Set(nut,"_capSpawnPoint",seat);
                var cap=Rod(joint,"Заглушка",Vector3.zero,Vector3.up*.12f,.125f,Bolt);
                Set(nut,"_capVisual",cap.gameObject);
            }else {
                Wheel(joint,Vector3.zero,.32f,Yellow);
                Rod(joint,"Гайка",Vector3.zero,Vector3.up*.075f,.05f,Bolt);
            }
            var col=n.gameObject.AddComponent<BoxCollider>();col.size=outlet?V(.30f,.28f,.30f):V(.63f,.11f,.63f);
            Layer(n);var part=n.gameObject.AddComponent<HydrantNutPart>();Int(part,"_partId",id);Set(part,"_nut",nut);
            return nut;
        }
        internal static void Fitting(CCPipeRun run,int index,int line,int kind)
        {
            var previous=run.GetComponentsInChildren<CCPipeFitting>(true);
            if(kind<0){
                foreach(var old in previous)if(old.node==index && old.line==line)Undo.DestroyObjectImmediate(old.gameObject);
                return;
            }
            var f=BuildGridFitting(run.transform,line,kind);
            var anchor=f.gameObject.AddComponent<CCPipeFitting>();anchor.node=index;anchor.line=line;
            foreach(var old in previous)
                if(old.node==index && old.line==line){anchor.mountingAngles=old.mountingAngles;break;}
            Undo.RegisterCreatedObjectUndo(f.gameObject,"Заменить арматуру");
            foreach(var old in previous)if(old.node==index && old.line==line)Undo.DestroyObjectImmediate(old.gameObject);
            Rebuild(run);
        }

        internal static Transform BuildGridFitting(Transform parent,int line,int kind)
        {
            var f=Node(parent,new[]{"Задвижка","Замок шланга","Ревизионный люк","Переключатель","Обратный клапан","Муфта"}[kind],Vector3.zero);
            float r=Radii[line];
            Rod(f,"Седло",V(0,0,-.19f),V(0,0,.19f),r+.045f,Steel);
            var mount=Node(f,"Корпус арматуры",Vector3.up*(r-.08f));
            if(kind==5 || kind==4){
                float length=kind==5?.18f:.36f;
                foreach(float z in new[]{-length,length})
                    Rod(f,"Фланец",V(0,0,z-.04f),V(0,0,z+.04f),r+.09f,Bolt);
                Rod(f,"Корпус",V(0,0,-length),V(0,0,length),r+.065f,kind==5?Steel:Yellow);
                if(kind==5) ClampEar(mount,false);
                if(kind==4){
                    Box(mount,"Стрелка потока",V(0,.16f,0),V(.06f,.035f,.31f),Steel);
                    foreach(int sign in new[]{-1,1})
                        Rod(mount,"Направление",V(0,.16f,.16f),V(sign*.10f,.16f,.03f),.035f,Steel);
                }
                return f;
            }
            if(kind==3){
                f.gameObject.AddComponent<NetworkObject>();
                var selector=f.gameObject.AddComponent<CCPipeSelector>();
                var top=f.gameObject.AddComponent<HydrantNut>();
                selector.valve=top;
                Rod(mount,"Шток",V(0,.04f,0),V(0,.40f,0),.10f,Steel);
                selector.handle=Node(mount,"Ось рычага",V(0,.42f,0));
                var lever=Box(selector.handle,"Рычаг",V(0,0,.22f),V(.12f,.11f,.60f),Yellow,true);Layer(lever);
                selector.openIndicator=Box(mount,"Открыто",V(.14f,.25f,.09f),V(.08f,.08f,.08f),"54BA70",false,true).gameObject;
                selector.closedIndicator=Box(mount,"Закрыто",V(.14f,.25f,-.09f),V(.08f,.08f,.08f),"D84E43",false,true).gameObject;
                selector.openIndicator.SetActive(false);
                Rod(mount,"Патрубок",V(0,.18f,0),V(.47f,.18f,0),.12f,Steel);
                var outlet=Control(mount,"Штуцер",V(.47f,.18f,0),1,true,top);
                outlet.transform.localRotation=Quaternion.Euler(0,0,-90);
                return f;
            }
            Rod(mount,"Патрубок",Vector3.zero,V(0,.4f,0),.10f,Colors[line]);
            if(kind==2){
                f.gameObject.AddComponent<NetworkObject>();
                var hatch=f.gameObject.AddComponent<CCInspectionHatch>();
                var hinge=Node(mount,"Шарнир люка",V(0,.37f,-.31f));hatch.hinge=hinge;
                var lid=Rod(hinge,"Крышка",V(0,-.02f,.31f),V(0,.05f,.31f),.33f,Steel,true);Layer(lid);
                Rod(mount,"Дно ревизии",V(0,.23f,0),V(0,.24f,0),.27f,"253132");
                for(int j=0;j<24;j++){
                    float a=j*Mathf.PI/12,b=(j+1)*Mathf.PI/12;
                    Rod(mount,"Буртик",V(.30f*Mathf.Cos(a),.34f,.30f*Mathf.Sin(a)),V(.30f*Mathf.Cos(b),.34f,.30f*Mathf.Sin(b)),.031f,Steel);
                }
                for(int i=0;i<8;i++){float a=i*Mathf.PI/4;var v=V(Mathf.Cos(a)*.27f,.05f,.31f+Mathf.Sin(a)*.27f);Rod(hinge,"Болт",v,v+Vector3.up*.033f,.024f,Bolt);}
                Box(hinge,"Ручка",V(0,.10f,.31f),V(.21f,.045f,.05f),Yellow);
            }else{
                f.gameObject.AddComponent<NetworkObject>();
                var valve=Control(mount,"Вентиль",V(0,.44f,0),0,false);
                if(kind==1){
                    Rod(mount,"Ответвление",V(0,.22f,0),V(.44f,.22f,0),.095f,Steel);
                    var outlet=Control(mount,"Штуцер",V(.44f,.22f,0),1,true,valve);
                    outlet.transform.localRotation=Quaternion.Euler(0,0,-90);
                }
            }
            if (line==0 && parent.GetComponentInParent<CCPipeRun>()!=null)
                CCColdPipeSetup.Configure(f,Vector3Int.zero);
            return f;
        }
    }
}
