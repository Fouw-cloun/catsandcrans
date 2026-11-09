using UnityEditor;
using UnityEngine;
namespace CatCooks.Builders.Editor
{
    [CustomEditor(typeof(CCBuilderExcavatorOperator))]
    public sealed class CCBuilderExcavatorInspector : UnityEditor.Editor
    {
        [System.Serializable] private sealed class Settings
        { public string Format="CatCooks-Step3-Poses";public Vector4 Scoop,Dump,Travel;public float Reach; }
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();CCBuilderExcavatorOperator op=(CCBuilderExcavatorOperator)target;
            ExcavatorVehicle vehicle=op.GetComponent<ExcavatorVehicle>();
            EditorGUILayout.HelpBox("Углы Vector4: башня, стрела, рукоять, ковш. Для ручной подгонки выключи оператора, запусти Host и управляй машиной игроком. Перед выходом из Play скопируй позы; после выхода вставь.",MessageType.Info);
            if(Application.isPlaying)
            {
                EditorGUILayout.LabelField("Состояние сервера",op.State.ToString());
                EditorGUILayout.Vector4Field("Текущая рука",vehicle.CurrentArmPose);
                using(new EditorGUI.DisabledScope(!vehicle.IsServerInitialized))
                {
                    if(GUILayout.Button("Текущая рука → Scoop Pose")) { Undo.RecordObject(op,"Scoop pose");op.ScoopPose=vehicle.CurrentArmPose; }
                    if(GUILayout.Button("Текущая рука → Dump Pose")) { Undo.RecordObject(op,"Dump pose");op.DumpPose=vehicle.CurrentArmPose; }
                    if(GUILayout.Button("Текущая рука → Travel Pose")) { Undo.RecordObject(op,"Travel pose");op.TravelPose=vehicle.CurrentArmPose; }
                }
            }
            if(GUILayout.Button("Копировать 3 позы и радиус"))
                EditorGUIUtility.systemCopyBuffer=JsonUtility.ToJson(new Settings { Scoop=op.ScoopPose,Dump=op.DumpPose,Travel=op.TravelPose,Reach=op.BucketReach });
            if(GUILayout.Button("Вставить сохранённые позы"))
            {
                try
                {
                    string json=EditorGUIUtility.systemCopyBuffer;
                    if(string.IsNullOrEmpty(json) || !json.Contains("CatCooks-Step3-Poses")) throw new System.FormatException();
                    Settings settings=JsonUtility.FromJson<Settings>(json);
                    if(settings==null || settings.Format!="CatCooks-Step3-Poses") throw new System.FormatException();
                    Undo.RecordObject(op,"Paste poses");op.ScoopPose=settings.Scoop;op.DumpPose=settings.Dump;op.TravelPose=settings.Travel;
                    op.BucketReach=Mathf.Max(.1f,settings.Reach);EditorUtility.SetDirty(op);
                }
                catch(System.Exception) { Debug.LogWarning("[Step3] В буфере нет сохранённых поз этого пакета."); }
            }
        }
    }
}
