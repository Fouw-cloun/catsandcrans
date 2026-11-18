#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Linq;
using CatCooks.Food;
using W=CCWorkshopEditor;
public static class CCExcavatorSetup
{
    [MenuItem("CatCooks/Vehicle Workshop/01 Repair selected excavator")]
    public static void Repair()
    {
        var root=W.Selected();var v=root.GetComponent<ExcavatorVehicle>();
        if(v==null)throw new System.InvalidOperationException("Выдели корень с ExcavatorVehicle.");
        if(v.TowerPart==null || v.BoomPart==null || v.ArmPart==null || v.BucketPart==null)
            throw new System.InvalidOperationException("Сначала заполни Tower, Boom, Arm, Bucket в ExcavatorVehicle.");
        var tracks=root.GetComponentsInChildren<Renderer>().Where(r=>r.name=="track" || r.name=="track.003").ToArray();
        if(tracks.Length<2)throw new System.InvalidOperationException("Не найдены track и track.003; проверь модель.");
        W.Tracks(v,W.BoundsIn(root.transform,tracks),6000);
        var seat=W.Ref(v,"_seatPoint");var exit=W.Ref(v,"_exitPoint");
        var camera=(new SerializedObject(v)).FindProperty("_vehicleCamera").objectReferenceValue as GameObject;
        if(seat!=null && exit!=null && camera!=null)W.Seat(v,seat,exit,camera.transform);
        W.Add<CCVehicleHelp>(root);
        Pair(v,"boom base piston base","boom base piston tip",v.TowerPart,v.BoomPart,0);
        Pair(v,"boom arm piston base","boom arm piston tip",v.BoomPart,v.ArmPart,1);
        Pair(v,"bucket piston base","bucket piston tip",v.ArmPart,v.BucketPart,2);
        foreach(var cw in root.GetComponentsInChildren<Cookware>(true))W.Quiet(cw);
        W.Done(root,"Экскаватор: подвеска и поршни настроены. Компоненты и форма ковша сохранены. Проверь полный ход шарниров в тестовой сцене.");
    }
    static void Pair(ExcavatorVehicle v,string a,string b,Transform aLink,Transform bLink,int index)
    {
        Transform body=W.Find(v.transform,a),rod=W.Find(v.transform,b);
        if(body==null || rod==null){Debug.LogWarning("Нет пары поршня: "+a+" / "+b,v);return;}
        // Mesh origins in the supplied FBX are the end eyes. Each stays on its own rigid link.
        Undo.SetTransformParent(body,aLink,"Piston mount");Undo.SetTransformParent(rod,bLink,"Piston mount");
        var aMount=W.Child(aLink,"WorkshopMount_"+index+"_A",aLink.InverseTransformPoint(body.position));
        var bMount=W.Child(bLink,"WorkshopMount_"+index+"_B",bLink.InverseTransformPoint(rod.position));
        var all=v.GetComponents<PistonLookAt>();var p=index<all.Length?all[index]:Undo.AddComponent<PistonLookAt>(v.gameObject);
        Undo.RecordObject(p,"Bind piston");p.basePart=body;p.rodPart=rod;p.baseTarget=bMount;p.rodTarget=aMount;p.preserveBindPose=true;p.smooth=false;p.Rebind();
    }
}
#endif
