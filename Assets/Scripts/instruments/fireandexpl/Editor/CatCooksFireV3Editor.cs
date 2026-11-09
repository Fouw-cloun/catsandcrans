using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CatCooksFireV3))]
public sealed class CatCooksFireV3Editor : Editor
{
    public override void OnInspectorGUI()
    {
        var fire = (CatCooksFireV3)target;
        EditorGUILayout.HelpBox("Footprint — площадь костра в локальных метрах. Для расширения кострища меняйте её: система добавит источники. Transform Scale равномерно увеличивает уже существующий эффект.", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("0.5 м")) Preset(fire, .5f, .35f, .30f);
            if (GUILayout.Button("2 × 2 м")) Preset(fire, 2, 1.4f, .55f);
            if (GUILayout.Button("4 × 4 м")) Preset(fire, 4, 2.8f, .60f);
            if (GUILayout.Button("Столб"))
            {
                Preset(fire, .8f, 2.5f, .4f);
                fire.automaticHeight = false; fire.flameHeight = 2.5f;
                EditorUtility.SetDirty(fire);
            }
        }
        DrawDefaultInspector();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Источников / живых частиц", $"{fire.SourceCount} / {fire.LiveParticleCount}");
        if (fire.enableHeatHaze)
            EditorGUILayout.HelpBox("Для марева включите Opaque Texture и Depth Texture в URP Asset и не отключайте их на камере. Марево слегка искажает непрозрачную сцену внутри объёма огня; это не полноэкранный blur.", MessageType.Info);
        Vector3 s = fire.transform.lossyScale;
        if (s.x <= 0 || s.y <= 0 || s.z <= 0 || Mathf.Abs(s.x - s.y) > .001f || Mathf.Abs(s.x - s.z) > .001f)
            EditorGUILayout.HelpBox("Для предсказуемого силуэта используйте положительный равномерный масштаб объекта и родителей. Пропорции кострища задавайте через Footprint и Flame Height.", MessageType.Warning);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button(fire.IsPreviewing ? "Перезапустить preview" : "Preview в сцене")) fire.StartPreview();
                if (GUILayout.Button("Убрать preview")) fire.StopPreview();
            }
        }
        using (new EditorGUI.DisabledScope(!fire.IsBuilt))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Всплеск искр")) fire.BurstSparks();
            if (GUILayout.Button("Клуб дыма")) fire.PuffSmoke();
        }
        if (GUILayout.Button("Добавить ручной источник")) AddSource(fire);
        if (GUILayout.Button("Проверить геометрию и импорты")) CatCooksFireV3Validation.Run();
    }

    static void Preset(CatCooksFireV3 f, float size, float height, float spacing)
    {
        Undo.RecordObject(f, "Fire preset");
        f.layout = CatCooksFireV3.Layout.Ellipse;
        f.SetFootprint(size, size); f.automaticHeight = true; f.heightToWidth = height / size;
        f.flameHeight = height; f.sourceSpacing = spacing;
        EditorUtility.SetDirty(f);
    }
    static void AddSource(CatCooksFireV3 f)
    {
        Undo.RecordObject(f, "Add manual fire source");
        int n = f.manualSources == null ? 0 : f.manualSources.Length;
        var go = new GameObject("Fire source " + (n + 1));
        Undo.RegisterCreatedObjectUndo(go, "Add manual fire source");
        Undo.SetTransformParent(go.transform, f.transform, "Parent fire source");
        go.transform.localPosition = new Vector3((n % 3 - 1) * .45f, 0, n / 3 * .45f);
        go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
        Array.Resize(ref f.manualSources, n + 1);
        f.manualSources[n] = new CatCooksFireV3.FireSource { anchor = go.transform };
        f.layout = CatCooksFireV3.Layout.Manual;
        EditorUtility.SetDirty(f);
        Selection.activeGameObject = go;
    }

    [MenuItem("GameObject/CatCooks/Fire V3 (2 metres)", false, 10)]
    public static void Create(MenuCommand command)
    {
        var go = new GameObject("CatCooks Fire V3 — 2m");
        GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create CatCooks Fire V3");
        var f = Undo.AddComponent<CatCooksFireV3>(go);
        f.bodyShader = Shader.Find("CatCooks/Fire V3/Body");
        f.particlesShader = Shader.Find("CatCooks/Fire V3/Particles");
        f.heatShader = Shader.Find("CatCooks/Fire V3/Heat");
        EditorUtility.SetDirty(f); Selection.activeGameObject = go;
        if (!Application.isPlaying) f.StartPreview();
    }
}

public static class CatCooksFireV3Validation
{
    [MenuItem("Tools/CatCooks/Validate Fire V3")]
    public static void Run()
    {
        try
        {
            var sources = new List<CatCooksFireGeometry.Source>();
            float[] sizes = { .1f, .5f, 2, 4, 8, 32 };
            int[] budgets = { 1, 4, 16, 64, 128 };
            int layouts = 0;
            foreach (float x in sizes) foreach (float z in sizes) foreach (int budget in budgets)
            for (int ellipse = 0; ellipse < 2; ellipse++)
            {
                CatCooksFireGeometry.Grid(new Vector2(x, z), .55f, budget, ellipse == 1, 1.4f, 137, sources);
                Check(sources.Count > 0 && sources.Count <= budget, "Source budget");
                foreach (var s in sources)
                {
                    Check(Finite(s.position) && Finite(new Vector3(s.radius.x, s.height, s.radius.y)), "Finite layout");
                    Check(s.height > 0 && s.radius.x > 0 && s.radius.y > 0, "Positive dimensions");
                    Check(Mathf.Abs(s.position.x) + s.radius.x <= x * .5f + .0001f, "X source envelope");
                    Check(Mathf.Abs(s.position.z) + s.radius.y <= z * .5f + .0001f, "Z source envelope");
                }
                layouts++;
            }
            CatCooksFireGeometry.Grid(new Vector2(2, 2), .55f, 64, true, 1.4f, 137, sources);
            Check(sources.Count == 16, "2m default must have 16 sources");
            ValidateTemplate(CatCooksFireGeometry.MakeTemplate(16, 16, true));
            ValidateTemplate(CatCooksFireGeometry.MakeTemplate(7, 8, true));
            ValidateTemplate(CatCooksFireGeometry.MakeTemplate(6, 9, false));
            ValidateTemplate(CatCooksFireGeometry.MakeTemplate(2, 4, false));
            foreach (string suffix in new[] { "Body", "Particles", "Heat" })
            {
                Shader shader = Shader.Find("CatCooks/Fire V3/" + suffix);
                Check(shader != null, "Missing shader " + suffix);
                Check(shader.isSupported, "Unsupported shader " + suffix);
                Check(!ShaderUtil.ShaderHasError(shader), "Shader import error " + suffix);
            }
            Debug.Log($"CatCooks Fire V3: PASS — {layouts} layouts, 4 mesh templates, shader import/support checks. Rendered appearance and player builds still require a scene test.");
        }
        catch (Exception e) { Debug.LogError("CatCooks Fire V3 validation FAILED: " + e.Message); }
    }
    static void ValidateTemplate(CatCooksFireGeometry.Template t)
    {
        double signedVolume = 0;
        for (int i = 0; i < t.vertices.Length; i++)
        {
            Check(Finite(t.vertices[i]) && Finite(t.normals[i]), "Non-finite vertex/normal");
            Check(t.normals[i].sqrMagnitude > .8f, "Missing normal");
            Check(t.vertices[i].y >= -.00001f && t.vertices[i].y <= 1.00001f, "Normalised height");
        }
        for (int i = 0; i < t.triangles.Length; i += 3)
        {
            Vector3 a = t.vertices[t.triangles[i]], b = t.vertices[t.triangles[i+1]], c = t.vertices[t.triangles[i+2]];
            Check(Vector3.Cross(b-a, c-a).sqrMagnitude > 1e-12f, "Degenerate submitted triangle");
            signedVolume += Vector3.Dot(a, Vector3.Cross(b,c)) / 6.0;
        }
        Check(signedVolume > 0, "Inverted winding or zero volume");
    }
    static bool Finite(Vector3 v)
        => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
        && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
