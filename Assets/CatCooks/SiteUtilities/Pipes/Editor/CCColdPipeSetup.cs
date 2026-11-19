using CatCooks.Level;
using CatCooks.Food;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static CatCooks.SiteTools.CCUtilityArt;

namespace CatCooks.SiteTools
{
    public static class CCColdPipeSetup
    {
        [MenuItem("CatCooks/Строительные сети/Подключить холодную воду на выбранном объекте")]
        static void UpgradeSelection()
        {
            if (Application.isPlaying || Selection.activeGameObject==null) return;
            var selected=Selection.activeGameObject;
            var run=selected.GetComponentInParent<CCPipeRun>();
            var house=selected.GetComponentInParent<CityModuleHouse>();
            if (run==null && house==null){Debug.LogWarning("Выбери Cat House (Modules) или Магистраль (CCPipeRun).");return;}
            Undo.IncrementCurrentGroup();
            int group=Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Подключить холодную воду");
            int count=0;
            if (run!=null){
                foreach (var fitting in run.GetComponentsInChildren<CCPipeFitting>(true))
                    if (fitting.GetComponentInParent<CCPipeRun>()==run && fitting.line==0 &&
                        Configure(fitting.transform,Vector3Int.zero)) count++;
            } else {
                house.Reindex();
                var built=house.transform.Find(CityModuleHouse.BuiltName);
                if (built!=null)
                    foreach (var cell in house.Cells){
                        if (!cell.HasPipes || cell.PartAt(0)==0 || cell.Locked) continue;
                        var network=built.Find($"C_{cell.X}_{cell.Y}_{cell.Z}/Трубная сеть");
                        var device=CCGridUtilityBuilder.FindFitting(network,cell,0);
                        if (device!=null && Configure(device,new Vector3Int(cell.X,cell.Y,cell.Z))) count++;
                    }
            }
            Undo.CollapseUndoOperations(group);
            if (count>0) EditorSceneManager.MarkSceneDirty(selected.scene);
            Debug.Log($"[Трубы] Обработано люков и вентилей холодной воды: {count}.",selected);
        }

        internal static bool Configure(Transform device,Vector3Int gridCell)
        {
            var mount=device.Find("Корпус арматуры");
            if (mount==null) return false;
            var hatch=device.GetComponent<CCInspectionHatch>();
            if (hatch!=null){
                Undo.RecordObject(hatch,"Холодный люк");
                hatch.gridCell=gridCell;
                if (hatch.coldJet==null) hatch.coldJet=BuildJet(mount);
                PrefabUtility.RecordPrefabInstancePropertyModifications(hatch);
                return true;
            }
            var wheel=mount.Find("Вентиль");
            if (wheel==null || !wheel.TryGetComponent(out HydrantNut nut)) return false;
            if (wheel.TryGetComponent(out HydrantNutPart old)) Undo.DestroyObjectImmediate(old);
            var valve=wheel.GetComponent<CCColdPipeValve>();
            if (valve==null) valve=Undo.AddComponent<CCColdPipeValve>(wheel.gameObject);
            Undo.RecordObject(valve,"Вентиль холодной воды");
            valve.nut=nut;
            valve.gridCell=gridCell;
            var data=new SerializedObject(nut);
            data.FindProperty("_startFullyOpen").boolValue=true;
            data.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(valve);
            return true;
        }

        static WaterJet BuildJet(Transform mount)
        {
            var outlet=Node(mount,"Струя из люка",Vector3.up*.42f).gameObject;
            outlet.SetActive(false);
            var jet=outlet.AddComponent<WaterJet>();
            var view=outlet.AddComponent<WaterJetLiquidStreamView>();
            // Reuse this scene's nozzle tuning, without linking its input, hose or splash object.
            foreach(var nozzle in Object.FindObjectsByType<NozzleGunItem>(FindObjectsInactive.Include,FindObjectsSortMode.None)){
                if (nozzle.gameObject.scene!=mount.gameObject.scene) continue;
                var sourceJet=new SerializedObject(nozzle).FindProperty("_jet").objectReferenceValue as WaterJet;
                if (sourceJet==null) sourceJet=nozzle.GetComponentInChildren<WaterJet>(true);
                if (sourceJet==null) continue;
                var sourceView=nozzle.GetComponentInChildren<WaterJetLiquidStreamView>(true);
                CopyFields(sourceJet,jet,"_maxLaunchSpeed","_minLaunchSpeed","_gravity","_maxRadius","_minRadius",
                    "_radiusProfile","_lengthSegments","_radialSegments","_maxFlightTime","_hitMask","_jetMaterial",
                    "_pushObjects","_maxPushForce","_pushThreshold","_pushRadius","_puddlePrefab","_puddleSpacing",
                    "_maxReach","_smallRadius","_largeRadius","_largeEvery","_puddleScatter","_maxSlopeAngle");
                if(sourceView!=null)
                    CopyFields(sourceView,view,"_color","_speedAtFull","_speedAtMin","_jetGravity","_widthAtFull","_cutoff","_splashPerSecond");
                var food=nozzle.GetComponentInChildren<FoodJetWasher>(true);
                if (food!=null && food.enabled){
                    var washer=outlet.AddComponent<FoodJetWasher>();
                    CopyFields(food,washer,"_washSpeed","_minFlow","_secondsToClean","_hitRadius","_foodMask","_washableMask","_scanInterval","_pushForce","_log");
                    Set(washer,"_jet",jet);
                }
                var sourceFill=nozzle.GetComponentInChildren<JetCookwareFiller>(true);
                if (sourceFill!=null && sourceFill.enabled){
                    var filler=Undo.AddComponent<JetCookwareFiller>(mount.parent.gameObject);
                    CopyFields(sourceFill,filler,"_water","_portionsPerSecond","_minFlow","_mouthRadius","_scanInterval","_log");
                    Set(filler,"_jet",jet);
                }
                var oldWasher=nozzle.GetComponentInChildren<WaterJetWasher>(true);
                if (oldWasher!=null && oldWasher.enabled){
                    var washer=Undo.AddComponent<WaterJetWasher>(mount.parent.gameObject);
                    CopyFields(oldWasher,washer,"_washProcess","_secondsToWash","_minFlow","_hitRadius","_foodMask","_scanInterval",
                        "_dirtyColor","_cleanColor","_dripsPerSecond","_dripSize","_dripGravity","_dripLifetime","_dripsCollide");
                    var options=new SerializedObject(washer);
                    options.FindProperty("_debugHud").boolValue=false;
                    options.ApplyModifiedPropertiesWithoutUndo();
                    Set(washer,"_jet",jet);
                }
                break;
            }
            Set(view,"_jet",jet);
            Undo.RegisterCreatedObjectUndo(outlet,"Добавить струю брандспойта");
            return jet;
        }

        static void CopyFields(Object source,Object destination,params string[] names)
        {
            var from=new SerializedObject(source);
            var to=new SerializedObject(destination);
            foreach(string field in names){
                var property=from.FindProperty(field);
                if (property!=null) to.CopyFromSerializedProperty(property);
            }
            to.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
