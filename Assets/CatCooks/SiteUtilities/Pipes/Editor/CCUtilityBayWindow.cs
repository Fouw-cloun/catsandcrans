using CatCooks.Level;
using CatCooks.Floors;
using UnityEngine;
using UnityEditor;
using static CatCooks.SiteTools.CCUtilityArt;

namespace CatCooks.SiteTools
{
    public sealed class CCUtilityBayWindow : EditorWindow
    {
        CityModuleHouse house;
        CityFloorGenerator floor;
        CCModularFloor deck;
        Vector3Int cell;
        float pitch=6,height=4;
        int depth=1;
        [MenuItem("CatCooks/Строительные сети/Модули и трубы")]
        static void Open()=>GetWindow<CCUtilityBayWindow>("Инженерная ячейка");
        void OnGUI(){
            house=(CityModuleHouse)EditorGUILayout.ObjectField("Дом etali",house,typeof(CityModuleHouse),true);
            if(house==null)floor=(CityFloorGenerator)EditorGUILayout.ObjectField("Генератор этажа",floor,typeof(CityFloorGenerator),true);
            if(house==null && floor==null)deck=(CCModularFloor)EditorGUILayout.ObjectField("Рабочий настил",deck,typeof(CCModularFloor),true);
            cell=EditorGUILayout.Vector3IntField("Клетка X / этаж Y / Z",cell);
            if(house==null && floor==null && deck==null){
                pitch=Mathf.Max(3,EditorGUILayout.FloatField("Шаг, м",pitch));
                height=Mathf.Max(2,EditorGUILayout.FloatField("Высота этажа, м",height));
            }

            if(house==null)depth=EditorGUILayout.IntSlider("Глубина шахты, этажей",depth,1,6);
            EditorGUILayout.HelpBox("Для дома укажи верхний этаж лифта. Шахта займёт один этаж вниз. У генератора этажа вручную открой плиту. Автономная ячейка появится в центре вида Scene.",MessageType.Info);
            using(new EditorGUI.DisabledScope(Application.isPlaying)){
                if(GUILayout.Button(house==null?"Создать крановую шахту":"Поставить крытый лифт"))Build(true);
                if(GUILayout.Button(house==null?"Создать эстакаду с тремя трубами":"Поставить трубную комнату"))Build(false);
                if(GUILayout.Button("Начать отдельную трассу")){
                    var t=Node(null,"Магистраль",SceneView.lastActiveSceneView!=null?SceneView.lastActiveSceneView.pivot:Vector3.zero);
                    var r=t.gameObject.AddComponent<CCPipeRun>();r.points.Add(Vector3.zero);r.points.Add(Vector3.forward*3);
                    CCPipeBuilder.Rebuild(r);Finish(t);
                }
            }
        }
        void Build(bool shaft){
            float w=pitch,h=height,length=pitch;
            Transform parent=null;
            Vector3 local=SceneView.lastActiveSceneView!=null?SceneView.lastActiveSceneView.pivot:Vector3.zero;
            string id="Инженерная ячейка "+cell;
            if(house!=null){
                var kind=shaft?CityModuleHouse.CellKind.Lift:CityModuleHouse.CellKind.Riser;
                if(!house.CanPaint(cell.x,cell.y,cell.z,kind)){
                    ShowNotification(new GUIContent("Клетка заблокирована или пересекает другой инженерный узел."));return;
                }
                CityModuleHouseEditor.AutoAssign(house);
                if(house.BulkModule==null){ShowNotification(new GUIContent("Назначь Bulk Module у дома."));return;}
                Undo.RecordObject(house,"Инженерная комната");
                house.Add(cell.x,cell.y,cell.z,kind);
                CityModuleHouseEditor.RebuildRegion(house,cell.x,cell.x,cell.y,cell.y,cell.z,cell.z);
                EditorUtility.SetDirty(house);return;
            }else if(floor!=null){
                length=w=floor.Pitch;h=floor.Height;parent=floor.transform;
                floor.MakePlan();
                if(!floor.Has(cell.x,cell.z)){ShowNotification(new GUIContent("Клетка за границей плана"));return;}
                local=parent.InverseTransformPoint(floor.CellPos(cell.x,cell.z,cell.y)+Vector3.up*floor.SlabTop);
            }else if(deck!=null){
                var layout=deck.Layout;
                if(layout==null || cell.x<0 || cell.z<0 || cell.y<0 || cell.x>=layout.Width || cell.z>=layout.Depth || cell.y>=layout.DeckLevels){
                    ShowNotification(new GUIContent("Выбери ячейку в пределах рабочего настила"));return;
                }
                if(layout.Built(cell.x,cell.z,cell.y)){ShowNotification(new GUIContent("Открой эту плиту в плане настила и пересобери этаж"));return;}
                w=deck.CellSize;length=deck.StepZ;h=deck.StoreyHeight;parent=deck.transform;
                local=V((cell.x+.5f)*w,cell.y*h,(cell.z+.5f)*length);
            }
            if(parent!=null && parent.Find(id)!=null){ShowNotification(new GUIContent("Эта ячейка уже занята"));return;}
            var root=Node(parent,id,local);
            string metal="596573", yellow="DB9B39", dark="38444E";
            float e=w*.5f-.14f,zEdge=length*.5f-.14f;
            foreach(float side in new[]{-1f,1f}){
                Box(root,"Борт",V(side*e,-.16f,0),V(.23f,.34f,length),metal,true);
                Box(root,"Поперечина",V(0,-.16f,side*zEdge),V(w,.34f,.23f),metal,true);
                Box(root,"Сервисный настил",V(side*(e-.23f),0,0),V(.49f,.09f,length-.3f),dark,true);
                for(int i=0;i<6;i++)Box(root,"Противоскользящий риф",V(side*(e-.23f),.052f,Mathf.Lerp(-zEdge,zEdge,i/5f)),V(.44f,.018f,.06f),yellow);
            }
            if(shaft){
                float d=h*depth;
                foreach(float x in new[]{-e,e})foreach(float z in new[]{-zEdge,zEdge})
                    Box(root,"Стойка шахты",V(x,-d*.5f,z),V(.18f,d,.18f),metal,true);
                for(int j=1;j<=depth;j++){
                    float y=-h*j;
                    foreach(float s in new[]{-1f,1f}){
                        Box(root,"Пояс",V(s*e,y,0),V(.14f,.2f,length),dark);
                        Box(root,"Пояс",V(0,y,s*zEdge),V(w,.2f,.14f),dark);
                        Rod(root,"Раскос",V(-e,y,s*zEdge),V(e,y+h,s*zEdge),.06f,metal);
                    }
                }
                foreach(float x in new[]{-.28f,.28f})Rod(root,"Тетива",V(x,-d,-zEdge+.2f),V(x,.65f,-zEdge+.2f),.045f,yellow);
                for(float y=-d+.25f;y<.7f;y+=.32f)Rod(root,"Ступень",V(-.28f,y,-zEdge+.2f),V(.28f,y,-zEdge+.2f),.03f,metal);
                float gantry=Mathf.Min(3.8f,h*.76f);
                foreach(float x in new[]{-e,e})Box(root,"Опора портала",V(x,gantry*.5f,0),V(.22f,gantry,.3f),yellow,true);
                Box(root,"Мост крана",V(0,gantry,0),V(w,.31f,.34f),yellow);
                Box(root,"Тельфер",V(0,gantry-.24f,0),V(.66f,.44f,.5f),dark);
                Rod(root,"Трос",V(0,gantry-.4f,0),V(0,-h*.32f,0),.025f,metal);
                Art(root,"Assets/CatCooks/SiteUtilities/Pipes/Models/HoistHook.fbx",V(0,-h*.32f-.21f,0),.49f);
            } else {
                for(int i=0;i<3;i++)Box(root,"Траверса",V(0,-.43f,Mathf.Lerp(-zEdge,zEdge,i*.5f)),V(w,.17f,.18f),metal,true);
                var run=Node(root,"Магистраль",Vector3.zero).gameObject.AddComponent<CCPipeRun>();
                run.points.Add(V(0,-.11f,-length*.5f));run.points.Add(V(0,-.11f,length*.5f));
                CCPipeBuilder.Rebuild(run);
            }
            Finish(root);
        }
    }

    [CustomEditor(typeof(CCPipeRun))]
    public sealed class CCPipeInspector : Editor
    {
        int index,line,kind;Vector3 direction=Vector3.forward;float step=3;
        public override void OnInspectorGUI(){
            var r=(CCPipeRun)target;
            if(Application.isPlaying){EditorGUILayout.LabelField("Редактирование в Edit Mode");return;}
            if(r.points.Count<2){EditorGUILayout.HelpBox("Создай трассу через окно модулей.",MessageType.Info);return;}
            index=EditorGUILayout.IntSlider("Узел арматуры",index,0,r.points.Count-1);
            direction=EditorGUILayout.Vector3Field("Направление XYZ",direction);
            step=Mathf.Max(1.5f,EditorGUILayout.FloatField("Длина секции",step));
            if(GUILayout.Button("Продлить от последнего узла") && direction.sqrMagnitude>.001f){
                int n=r.points.Count;
                if(Vector3.Dot((r.points[n-1]-r.points[n-2]).normalized,direction.normalized)<-.98f){Debug.LogWarning("Для разворота добавь промежуточное колено.");return;}
                Undo.RecordObject(r,"Продлить трассу");r.points.Add(r.points[n-1]+direction.normalized*step);index=n;CCPipeBuilder.Rebuild(r);
            }
            if(r.points.Count>2 && GUILayout.Button("Убрать последнюю секцию")){
                int last=r.points.Count-1;
                foreach(var f in r.GetComponentsInChildren<CCPipeFitting>())if(f.node==last){Debug.LogWarning("Сначала перенеси или удали арматуру этого узла.");return;}
                Undo.RecordObject(r,"Укоротить трассу");r.points.RemoveAt(last);index=last-1;CCPipeBuilder.Rebuild(r);
            }
            line=EditorGUILayout.Popup("Линия",line,new[]{"Холодная","Горячая","Канализация"});
            kind=EditorGUILayout.Popup("Арматура",kind,new[]{"Задвижка","Вентиль и замок шланга","Ревизионный люк","Рычажный переключатель","Обратный клапан","Соединительная муфта"});
            if(GUILayout.Button("Поставить / заменить в узле"))CCPipeBuilder.Fitting(r,index,line,kind);
            if(GUILayout.Button("Убрать арматуру с этой линии"))CCPipeBuilder.Fitting(r,index,line,-1);
            foreach (var part in r.GetComponentsInChildren<CCPipeFitting>(true)){
                if (part.node!=index || part.line!=line) continue;
                EditorGUILayout.LabelField("Поворот детали",EditorStyles.boldLabel);
                var angles=CCPipeBuilder.AngleControls(part.mountingAngles);
                if (angles!=part.mountingAngles){
                    Undo.RecordObject(part,"Поворот арматуры");
                    part.mountingAngles=angles;
                    EditorUtility.SetDirty(part);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(part);
                    if (part.transform.Find("Корпус арматуры")!=null)
                        CCPipeBuilder.OrientMount(part.transform,line,angles,true);
                    else CCPipeBuilder.Rebuild(r);
                    SceneView.RepaintAll();
                }
                break;
            }
            if(GUILayout.Button("Обновить диаметр трассы"))CCPipeBuilder.Rebuild(r);
        }
        void OnSceneGUI(){
            if(Application.isPlaying)return;
            var r=(CCPipeRun)target;
            if(index<0||index>=r.points.Count)return;
            Vector3 old=r.transform.TransformPoint(r.points[index]);
            Handles.Label(old,"Узел "+index);
            EditorGUI.BeginChangeCheck();
            Vector3 next=r.transform.InverseTransformPoint(Handles.PositionHandle(old,r.transform.rotation));
            if(!EditorGUI.EndChangeCheck())return;
            for(int i=0;i<r.points.Count;i++)if(i!=index && (r.points[i]-next).sqrMagnitude<.8f)return;
            var original=r.points[index];Undo.RecordObject(r,"Сдвинуть узел");r.points[index]=next;
            for(int i=1;i<r.points.Count-1;i++)if(Vector3.Dot((r.points[i]-r.points[i-1]).normalized,(r.points[i+1]-r.points[i]).normalized)<-.98f){r.points[index]=original;return;}
            CCPipeBuilder.Rebuild(r);
        }
    }
}
