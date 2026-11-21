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
        [MenuItem("CatCooks/Строительные сети/Обновить струи трёх труб")]
        static void UpgradeSelection()
        {
            if (Application.isPlaying || Selection.activeGameObject==null) return;
            var selected=Selection.activeGameObject;
            var run=selected.GetComponentInParent<CCPipeRun>();
            var house=selected.GetComponentInParent<CityModuleHouse>();
            if (run==null && house==null){Debug.LogWarning("Выбери Cat House (Modules) или Магистраль (CCPipeRun).");return;}
            Undo.IncrementCurrentGroup();
            int group=Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Обновить струи труб");
            int count=0;
            if (run!=null){
                foreach (var fitting in run.GetComponentsInChildren<CCPipeFitting>(true))
                    if (fitting.GetComponentInParent<CCPipeRun>()==run &&
                        Configure(fitting.transform,Vector3Int.zero,fitting.line)) count++;
            } else {
                house.Reindex();
                var built=house.transform.Find(CityModuleHouse.BuiltName);
                if (built!=null)
                    foreach (var cell in house.Cells){
                        if (!cell.HasPipes || cell.Locked) continue;
                        var network=built.Find($"C_{cell.X}_{cell.Y}_{cell.Z}/Трубная сеть");
                        for (int line=0;line<3;line++){
                            if (cell.PartAt(line)==0) continue;
                            var device=CCGridUtilityBuilder.FindFitting(network,cell,line);
                            if (device!=null && Configure(device,new Vector3Int(cell.X,cell.Y,cell.Z),line)) count++;
                        }
                    }
            }
            Undo.CollapseUndoOperations(group);
            if (count>0) EditorSceneManager.MarkSceneDirty(selected.scene);
            Debug.Log($"[Трубы] Обработано люков и вентилей: {count}.",selected);
        }

        internal static bool Configure(Transform device,Vector3Int gridCell,int line)
        {
            if ((uint)line>2) return false;
            var fluid=(CCPipeFluid)line;
            var mount=device.Find("Корпус арматуры");
            if (mount==null) return false;
            var hatch=device.GetComponent<CCInspectionHatch>();
            if (hatch!=null){
                Undo.RecordObject(hatch,"Вид выброса из люка");
                hatch.gridCell=gridCell;
                hatch.fluid=fluid;
                if (hatch.pipeJet==null) hatch.pipeJet=BuildJet(mount,fluid);
                ConfigureLook(hatch.pipeJet,fluid);
                PrefabUtility.RecordPrefabInstancePropertyModifications(hatch);
                return true;
            }
            var wheel=mount.Find("Вентиль");
            if (wheel==null || !wheel.TryGetComponent(out HydrantNut nut)) return false;
            if (wheel.TryGetComponent(out HydrantNutPart old)) Undo.DestroyObjectImmediate(old);
            var valve=wheel.GetComponent<CCColdPipeValve>();
            if (valve==null) valve=Undo.AddComponent<CCColdPipeValve>(wheel.gameObject);
            Undo.RecordObject(valve,"Вентиль трубной линии");
            valve.fluid=fluid;
            valve.nut=nut;
            valve.gridCell=gridCell;
            var data=new SerializedObject(nut);
            data.FindProperty("_startFullyOpen").boolValue=true;
            data.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(valve);
            return true;
        }

        static WaterJet BuildJet(Transform mount,CCPipeFluid fluid)
        {
            var outlet=Node(mount,"Струя из люка",Vector3.up*.42f).gameObject;
            outlet.SetActive(false);
            var jet=outlet.AddComponent<WaterJet>();

            // Reuse this scene's nozzle tuning, without linking its input, hose or splash object.
            foreach(var nozzle in Object.FindObjectsByType<NozzleGunItem>(FindObjectsInactive.Include,FindObjectsSortMode.None)){
                if (nozzle.gameObject.scene!=mount.gameObject.scene) continue;
                var sourceJet=new SerializedObject(nozzle).FindProperty("_jet").objectReferenceValue as WaterJet;
                if (sourceJet==null) sourceJet=nozzle.GetComponentInChildren<WaterJet>(true);
                if (sourceJet==null) continue;

                CopyFields(sourceJet,jet,"_maxLaunchSpeed","_minLaunchSpeed","_gravity","_maxRadius","_minRadius",
                    "_radiusProfile","_lengthSegments","_radialSegments","_maxFlightTime","_hitMask","_jetMaterial",
                    "_pushObjects","_maxPushForce","_pushThreshold","_pushRadius","_puddlePrefab","_puddleSpacing",
                    "_maxReach","_smallRadius","_largeRadius","_largeEvery","_puddleScatter","_maxSlopeAngle");
                if (fluid==CCPipeFluid.Sewage) break;
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

            Undo.RegisterCreatedObjectUndo(outlet,"Добавить струю брандспойта");
            return jet;
        }

        static Material JetMaterial(string name)
        {
            string path="Assets/CatCooks/SiteUtilities/Pipes/JetArt/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material==null) throw new System.InvalidOperationException("В архиве 07E нужен материал "+path);
            return material;
        }

        static void ConfigureLook(WaterJet jet,CCPipeFluid fluid)
        {
            // Old 07D ribbons hide the mesh in LateUpdate. Switch to the volumetric pipe renderer.
            if (jet.TryGetComponent<WaterJetLiquidStreamView>(out var ribbon) && ribbon.enabled){
                Undo.RecordObject(ribbon,"Вид струи");
                ribbon.enabled=false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(ribbon);
            }
            var look=jet.GetComponent<CCPipeJetLook>();
            if (look==null) look=Undo.AddComponent<CCPipeJetLook>(jet.gameObject);
            Undo.RecordObject(look,"Эффекты трубы");
            look.fluid=fluid;
            look.mist=JetMaterial(fluid==CCPipeFluid.Sewage ? "Odor" : "Vapor");
            look.drops=JetMaterial(fluid==CCPipeFluid.Cold ? "Ice" : "Droplets");
            var data=new SerializedObject(jet);
            data.FindProperty("_jetMaterial").objectReferenceValue=JetMaterial(fluid.ToString());
            data.FindProperty("_lengthSegments").intValue=40;
            data.FindProperty("_radialSegments").intValue=16;
            data.FindProperty("_pushObjects").boolValue=true;
            var force=data.FindProperty("_maxPushForce");
            force.floatValue=Mathf.Max(force.floatValue,28f);
            var speed=data.FindProperty("_maxLaunchSpeed");
            speed.floatValue=Mathf.Max(speed.floatValue,15f);
            if (fluid==CCPipeFluid.Sewage) data.FindProperty("_puddlePrefab").objectReferenceValue=null;
            data.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(look);
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
