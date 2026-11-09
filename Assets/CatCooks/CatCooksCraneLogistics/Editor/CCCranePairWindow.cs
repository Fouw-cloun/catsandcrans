using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
namespace CatCooks.CraneLogistics.Editor
{
    public sealed class CCCranePairWindow:EditorWindow
    {
        [SerializeField] CCWorkTerritory _site;
        [SerializeField] CCSupplyCrane _a,_b;
        [SerializeField] CCWorkZone _sa,_da,_sb,_db;
        [SerializeField] bool _shared=false;
        string _result="";
        Vector2 _scroll;
        [MenuItem("Tools/CatCooks/Crane Logistics/Two-crane setup v3")]
        public static void Open(){GetWindow<CCCranePairWindow>("Two cranes v3").Show();}
        void OnGUI()
        {
            _scroll=EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.HelpBox("Настройка существующих автономных кранов. Геометрия не копируется. Назначьте разные Crane A/B и свои Source/Destination. Изменения только вне Play.",MessageType.Info);
            _site=(CCWorkTerritory)EditorGUILayout.ObjectField("Territory",_site,typeof(CCWorkTerritory),true);
            Pair("Кран 1",ref _a,ref _sa,ref _da);Pair("Кран 2",ref _b,ref _sb,ref _db);
            _shared=EditorGUILayout.Toggle("Общий коридор: по очереди",_shared);
            EditorGUILayout.HelpBox(_shared?"Оба маршрута получат TrafficGroup = Pair corridor. Одновременно назначается один рейс. Это не проверка столкновений стрел.":"Параллельная работа. Пользователь должен развести стрелы, платформы и пути по разным секторам.",MessageType.Info);
            using(new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if(GUILayout.Button("Проверить ссылки"))_result=Check();
                if(GUILayout.Button("Настроить два маршрута + включить оба крана"))Apply();
            }
            EditorGUILayout.HelpBox(_result.Length==0?"После применения сохраните сцену и запустите Host.":_result,MessageType.None);
            EditorGUILayout.EndScrollView();
        }
        static void Pair(string title,ref CCSupplyCrane c,ref CCWorkZone s,ref CCWorkZone d)
        {
            EditorGUILayout.Space();EditorGUILayout.LabelField(title,EditorStyles.boldLabel);
            c=(CCSupplyCrane)EditorGUILayout.ObjectField("Crane",c,typeof(CCSupplyCrane),true);
            s=(CCWorkZone)EditorGUILayout.ObjectField("Source (Supply)",s,typeof(CCWorkZone),true);
            d=(CCWorkZone)EditorGUILayout.ObjectField("Destination",d,typeof(CCWorkZone),true);
        }
        string Check()
        {
            if(_site==null||_a==null||_b==null||_sa==null||_da==null||_sb==null||_db==null)return "Заполните все семь ссылок.";
            if(_a==_b)return "Выбран один и тот же кран два раза.";
            if(EditorUtility.IsPersistent(_site)||EditorUtility.IsPersistent(_a)||EditorUtility.IsPersistent(_b))return "Нужны объекты сцены, не assets-префабы.";
            if(_a.transform.IsChildOf(_b.transform)||_b.transform.IsChildOf(_a.transform))return "Краны не должны быть вложены друг в друга.";
            foreach(var z in new[]{_sa,_da,_sb,_db})if(z.Owner!=_site)return "Все зоны должны принадлежать выбранной территории.";
            if(_sa.Kind!=CCZoneKind.Supply||_sb.Kind!=CCZoneKind.Supply||!_da.AcceptsDelivery||!_db.AcceptsDelivery)return "Нужны маршруты Supply -> Receiving/Work.";
            foreach(var z in new[]{_sa,_da,_sb,_db})
                if(!z.isActiveAndEnabled||!z.Contains(z.DockPosition)||!z.Contains(z.StagingPosition)||Mathf.Abs(z.DockPosition.y-z.StagingPosition.y)>.25f)return z.name+": включите зону и поместите Dock/Staging внутрь границ.";
            if(_sa.Resource!=_da.Resource||_sb.Resource!=_db.Resource)return "Resource не совпадает внутри одного из маршрутов.";
            foreach(var c in new[]{_a,_b})
            {
                if(c.Territory!=null&&c.Territory!=_site)return "Один кран принадлежит другой территории; сначала проверьте назначение вручную.";
                if(!c.Valid(out string error))return c.name+": "+error;
                int count=0;foreach(var r in _site.GetComponentsInChildren<CCZoneCraneRoute>(true))if(r.Owner==_site&&r.Crane==c)count++;
                if(count>1)return c.name+": найдено несколько маршрутов. Для первого парного теста оставьте один маршрут на кран.";
            }
            return "OK";
        }
        void Apply()
        {
            _result=Check();if(_result!="OK")return;
            // Verify actual dock reach before changing scene objects.
            if(!_a.TrySolveDock(_sa.DockPosition,_a.DockA,out var aa)||!_a.TrySolveDock(_da.DockPosition,_a.DockB,out var ab)||!_b.TrySolveDock(_sb.DockPosition,_b.DockA,out var ba)||!_b.TrySolveDock(_db.DockPosition,_b.DockB,out var bb))
            {_result="Один из Dock недостижим; изменение не применено.";return;}
            if(_a.TravelRope>Mathf.Min(aa.z,ab.z)||_b.TravelRope>Mathf.Min(ba.z,bb.z))
            {_result="Travel Rope слишком длинный для одной из новых точек. Настройте высоту до применения.";return;}
            Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Configure two crane routes");
            Configure(_a,_sa,_da,aa,ab);Configure(_b,_sb,_db,ba,bb);
            Undo.RecordObject(_site,"Enable territory");Undo.RecordObject(_site.gameObject,"Enable territory root");_site.enabled=true;_site.gameObject.SetActive(true);_site.Dispatch=true;_site.RefreshLocal();
            EditorUtility.SetDirty(_site);EditorSceneManager.MarkSceneDirty(_site.gameObject.scene);Undo.CollapseUndoOperations(undo);
            _result="Готово. Оба Run включены, StartDelay=0, маршруты зарегистрированы. Проверьте высоту/препятствия и сохраните сцену. Запасы не изменены.";
        }
        void Configure(CCSupplyCrane c,CCWorkZone source,CCWorkZone dest,Vector3 a,Vector3 b)
        {
            CCZoneCraneRoute route=null;foreach(var r in _site.GetComponentsInChildren<CCZoneCraneRoute>(true))if(r.Owner==_site&&r.Crane==c){route=r;break;}
            if(route==null){var g=new GameObject("Route - "+c.name);Undo.RegisterCreatedObjectUndo(g,"Create route");g.transform.SetParent(_site.transform,false);route=Undo.AddComponent<CCZoneCraneRoute>(g);}
            Undo.RecordObject(route,"Configure route");Undo.RecordObject(route.gameObject,"Enable route");route.gameObject.SetActive(true);route.enabled=true;route.Enabled=true;route.Crane=c;route.Source=source;route.Destination=dest;route.TrafficGroup=_shared?"Pair corridor":"";
            Undo.RecordObject(c,"Configure crane");Undo.RecordObject(c.gameObject,"Enable crane");c.gameObject.SetActive(true);c.enabled=true;c.Territory=_site;c.Run=true;c.StartDelay=0;c.DockA=a;c.DockB=b;
            c.StorageA=c.transform.InverseTransformDirection(source.StagingPosition-source.DockPosition);c.StorageB=c.transform.InverseTransformDirection(dest.StagingPosition-dest.DockPosition);
            EditorUtility.SetDirty(route);EditorUtility.SetDirty(c);EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        }
    }
}
