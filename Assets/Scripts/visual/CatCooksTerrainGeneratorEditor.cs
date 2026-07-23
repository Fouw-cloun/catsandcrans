using UnityEditor;
using UnityEngine;

// ============================================================================
//  Редактор генератора территории CatCooks.
//  ВАЖНО: этот файл должен лежать в папке с именем "Editor"
//  (например Assets/Scripts/Editor/CatCooksTerrainGeneratorEditor.cs)
// ============================================================================
[CustomEditor(typeof(CatCooksTerrainGenerator))]
public class CatCooksTerrainGeneratorEditor : Editor
{
    private double lastEditTime;
    private bool pendingGenerate;
    private const double RegenDelay = 0.15; // сек после последнего движения ползунка

    public override void OnInspectorGUI()
    {
        var gen = (CatCooksTerrainGenerator)target;

        EditorGUILayout.HelpBox(
            "Планировка: горы — запад и север (полка-«десна» под модели), " +
            "дорога вдоль западных гор, лес и холмы восточнее, река с озёрами на юге.\n" +
            "Живой предпросмотр: включи Auto Update и двигай ползунки.",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        bool changed = EditorGUI.EndChangeCheck();

        EditorGUILayout.Space(8);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
            if (GUILayout.Button("⟳  Сгенерировать", GUILayout.Height(32)))
                Regenerate(gen);
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("🎲 Новый сид", GUILayout.Height(32), GUILayout.Width(110)))
            {
                Undo.RecordObject(gen, "New Terrain Seed");
                gen.seed = Random.Range(0, 999999);
                EditorUtility.SetDirty(gen);
                Regenerate(gen);
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Убрать деревья"))
            {
                var td = gen.GetComponent<Terrain>().terrainData;
                td.treeInstances = new TreeInstance[0];
            }
            if (GUILayout.Button("Пресет: старт"))
            {
                Undo.RecordObject(gen, "Terrain Preset");
                ApplyStarterPreset(gen);
                EditorUtility.SetDirty(gen);
                Regenerate(gen);
            }
        }

        // живой предпросмотр с лёгким дебаунсом, чтобы не пересчитывать
        // на каждый пиксель движения ползунка
        if (changed && gen.autoUpdate)
        {
            lastEditTime = EditorApplication.timeSinceStartup;
            if (!pendingGenerate)
            {
                pendingGenerate = true;
                EditorApplication.update += DebouncedUpdate;
            }
        }
    }

    void DebouncedUpdate()
    {
        if (EditorApplication.timeSinceStartup - lastEditTime < RegenDelay) return;
        EditorApplication.update -= DebouncedUpdate;
        pendingGenerate = false;
        if (target != null) Regenerate((CatCooksTerrainGenerator)target);
    }

    void Regenerate(CatCooksTerrainGenerator gen)
    {
        try { gen.Generate(); }
        catch (System.Exception e) { Debug.LogError($"[TerrainGen] {e.Message}\n{e.StackTrace}"); }
        SceneView.RepaintAll();
    }

    static void ApplyStarterPreset(CatCooksTerrainGenerator g)
    {
        g.baseHeight = 8f;  g.noiseScale = 2.2f; g.octaves = 5;
        g.persistence = 0.45f; g.lacunarity = 2.1f;
        g.hillAmplitude = 9f; g.domainWarp = 0.6f; g.hillZoneBoost = 0.8f;
        g.shelfWestWidth = 0.14f; g.shelfNorthWidth = 0.14f; g.shelfHeight = 22f;
        g.shelfEdgeNoise = 0.05f; g.shelfSlopeSoftness = 0.09f; g.shelfRoughness = 3f;
        g.roadEnabled = true; g.roadPosition = 0.24f; g.roadWidth = 0.03f;
        g.roadShoulder = 0.05f; g.roadCurviness = 0.05f; g.roadCurveScale = 1.6f;
        g.riverEnabled = true; g.riverPosition = 0.13f; g.riverWidth = 0.035f;
        g.riverDepth = 4.5f; g.bankSoftness = 0.05f;
        g.meanderAmount = 0.06f; g.meanderScale = 2.2f;
        g.lakeCount = 2; g.lakeDepth = 6f; g.waterLevel = 5.5f;
        g.pitCount = 14; g.pitDepth = 1.2f; g.smoothPasses = 1;
    }
}
