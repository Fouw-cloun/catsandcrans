// ============================================================================
//  CatCooksShaderGUI.cs
//  Кастомный инспектор для CatCooks/Toon Food и CatCooks/Toon Props.
//  • Свойства разложены по сворачиваемым группам (иначе там каша из 90 полей).
//  • Сверху — выпадающий список пресетов: выбрал, нажал «Применить», увидел.
//  • Работает сразу с несколькими выделенными материалами.
//  Файл обязан лежать в папке с именем Editor/.
// ============================================================================
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CatCooks.EditorTools;

public class CatCooksShaderGUI : ShaderGUI
{
    private class Group
    {
        public string Title;
        public string[] Props;
        public bool DefaultOpen;

        public Group(string title, bool defaultOpen, params string[] props)
        {
            Title = title;
            DefaultOpen = defaultOpen;
            Props = props;
        }
    }

    // Порядок групп общий для обоих шейдеров. Отсутствующие свойства
    // молча пропускаются, пустые группы не рисуются вообще.
    private static readonly Group[] Groups =
    {
        new Group("Базовое", true,
            "_BaseMap", "_BaseColor", "_VertexColorTint",
            "_AlphaClip", "_Cutoff", "_Cull", "_SrcBlend", "_DstBlend", "_ZWrite"),

        new Group("Карты поверхности", false,
            "_UseNormalMap", "_BumpMap", "_BumpScale",
            "_MetallicMap", "_MetallicStrength", "_MetallicDiffuseDim",
            "_OcclusionMap", "_OcclusionStrength", "_OcclusionDirect"),

        new Group("Цветокоррекция", true,
            "_Exposure", "_Brightness", "_Gamma", "_Contrast", "_ContrastPivot",
            "_Saturation", "_Vibrance", "_HueShift",
            "_WBTemp", "_WBTint", "_ColorFilter",
            "_ShadowsColor", "_HighlightsColor", "_ToneSplit", "_GradeFinal"),

        new Group("Toon-освещение", true,
            "_ShadowThreshold", "_ShadowSoftness", "_ShadowTint", "_LightWrap",
            "_ShadowSaturation", "_TerminatorColor", "_TerminatorStrength", "_TerminatorWidth",
            "_ExtraBands", "_UseRamp", "_RampMap",
            "_ReceiveShadows", "_MainLightBoost", "_AdditionalLightStrength"),

        new Group("Заливка и окружающий свет", false,
            "_IndirectStrength", "_AmbientTint",
            "_TopLightColor", "_TopLightStrength",
            "_BounceColor", "_BounceStrength"),

        new Group("Подповерхностное свечение (SSS)", true,
            "_SssOn", "_SSSColor", "_SSSStrength", "_SSSDistortion",
            "_SSSPower", "_SSSAmbient", "_SssAoThickness"),

        new Group("Блики", true,
            "_SpecularOn", "_SpecColor2", "_SpecSize", "_SpecSoftness", "_SpecIntensity",
            "_SheenColor", "_SheenSize", "_SheenIntensity", "_SpecAdditional"),

        new Group("Глазурь", true,
            "_GlazeOn", "_GlazeColor", "_GlazePower", "_GlazeThreshold",
            "_GlazeSoftness", "_GlazeIntensity", "_GlazeLightMask"),

        new Group("Контровой свет (Rim)", false,
            "_RimOn", "_RimColor", "_RimPower", "_RimThreshold",
            "_RimSoftness", "_RimIntensity", "_RimLightMask"),

        new Group("Блёстки", false,
            "_SparkleOn", "_SparkleColor", "_SparkleDensity",
            "_SparkleAmount", "_SparkleSpeed", "_SparkleIntensity"),

        new Group("Корочка", false,
            "_CrustOn", "_CrustColor", "_CrustMode", "_CrustLevel",
            "_CrustThreshold", "_CrustSoftness", "_CrustAmount"),

        new Group("Соус и влажность", false,
            "_SauceOn", "_SauceColor", "_SauceLevel", "_SauceSoftness",
            "_SauceTintStrength", "_SauceWetness"),

        new Group("Грязь и пыль", false,
            "_GrimeOn", "_GrimeColor", "_GrimeStrength", "_GrimeThreshold", "_GrimeSoftness",
            "_DustColor", "_DustStrength", "_DustSharpness"),

        new Group("Потёртые рёбра", false,
            "_WearOn", "_WearColor", "_WearPower", "_WearAmount", "_WearMetal"),

        new Group("Отражение окружения", false,
            "_EnvOn", "_EnvMode", "_EnvCube",
            "_EnvSkyColor", "_EnvHorizonColor", "_EnvGroundColor",
            "_EnvSteps", "_EnvBlur", "_EnvStrength", "_EnvFresnel", "_EnvMetalMask"),

        new Group("Matcap", false,
            "_MatcapOn", "_MatcapMap", "_MatcapColor",
            "_MatcapStrength", "_MatcapBlend", "_MatcapFresnel"),

        new Group("Эмиссия", false,
            "_EmissionMap", "_EmissionColor", "_EmissionPulseSpeed", "_EmissionPulseMin"),

        new Group("Бесшовная сборка из кусочков", false,
            "_UseTriplanar", "_TriplanarSpace", "_TriplanarScale", "_TriplanarBlend",
            "_ShapeBlend", "_ShapeCenter"),

        new Group("Вертексная анимация", false,
            "_JiggleAmp", "_JiggleFreq", "_JiggleSpeed", "_JiggleBase", "_JiggleFalloff"),

        new Group("Контур", true,
            "_OutlineColor", "_OutlineWidth", "_OutlineMode", "_OutlineNormalSource",
            "_OutlineTextureBlend", "_OutlineFadeStart", "_OutlineFadeEnd",
            "_OutlineZBias", "_OutlineWobbleAmp", "_OutlineWobbleSpeed"),
    };

    private int _presetIndex;

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        Material material = materialEditor.target as Material;
        if (material == null)
        {
            base.OnGUI(materialEditor, properties);
            return;
        }

        DrawPresetBar(materialEditor, material);
        EditorGUILayout.Space(6);

        var lookup = new Dictionary<string, MaterialProperty>();
        foreach (var p in properties)
        {
            if (!lookup.ContainsKey(p.name))
                lookup.Add(p.name, p);
        }

        var drawn = new HashSet<string>();

        foreach (var group in Groups)
        {
            var found = new List<MaterialProperty>();
            foreach (var name in group.Props)
            {
                MaterialProperty prop;
                if (lookup.TryGetValue(name, out prop))
                {
                    found.Add(prop);
                    drawn.Add(name);
                }
            }

            if (found.Count == 0)
                continue;

            string key = "CatCooks." + material.shader.name + "." + group.Title;
            bool open = EditorPrefs.GetBool(key, group.DefaultOpen);

            bool newOpen = DrawFoldoutHeader(group.Title, open);
            if (newOpen != open)
                EditorPrefs.SetBool(key, newOpen);

            if (newOpen)
            {
                EditorGUI.indentLevel++;
                foreach (var prop in found)
                    materialEditor.ShaderProperty(prop, prop.displayName);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4);
            }
        }

        // всё, что не попало ни в одну группу (например, добавленные позже свойства)
        var leftovers = new List<MaterialProperty>();
        foreach (var p in properties)
        {
            if (drawn.Contains(p.name))
                continue;
            if ((p.propertyFlags & UnityEngine.Rendering.ShaderPropertyFlags.HideInInspector) != 0)
                continue;
            leftovers.Add(p);
        }

        if (leftovers.Count > 0)
        {
            if (DrawFoldoutHeader("Прочее", true))
            {
                EditorGUI.indentLevel++;
                foreach (var p in leftovers)
                    materialEditor.ShaderProperty(p, p.displayName);
                EditorGUI.indentLevel--;
            }
        }

        EditorGUILayout.Space(8);
        materialEditor.RenderQueueField();
        materialEditor.EnableInstancingField();
        materialEditor.DoubleSidedGIField();
    }

    // ------------------------------------------------------------------
    private static bool DrawFoldoutHeader(string title, bool open)
    {
        var style = new GUIStyle(EditorStyles.foldout);
        style.fontStyle = FontStyle.Bold;
        return EditorGUILayout.Foldout(open, title, true, style);
    }

    // ------------------------------------------------------------------
    private void DrawPresetBar(MaterialEditor materialEditor, Material material)
    {
        var presets = CatCooksPresets.ForShader(material.shader.name);
        if (presets == null || presets.Count == 0)
            return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Пресеты", EditorStyles.boldLabel);

        var names = new string[presets.Count];
        for (int i = 0; i < presets.Count; i++)
            names[i] = presets[i].Name;

        _presetIndex = Mathf.Clamp(_presetIndex, 0, presets.Count - 1);
        _presetIndex = EditorGUILayout.Popup("Вариант", _presetIndex, names);

        EditorGUILayout.LabelField(presets[_presetIndex].Hint, EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Применить к материалу", GUILayout.Height(24)))
        {
            foreach (var target in materialEditor.targets)
            {
                var mat = target as Material;
                if (mat == null) continue;
                Undo.RecordObject(mat, "Apply CatCooks Preset");
                ApplyPreset(mat, presets[_presetIndex]);
                EditorUtility.SetDirty(mat);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField(
            "Пресет меняет только настройки освещения и цвета. Текстуры и оттенок Base Tint не трогаются.",
            EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.EndVertical();
    }

    // ------------------------------------------------------------------
    public static void ApplyPreset(Material mat, ShaderPreset preset)
    {
        foreach (var kv in preset.Floats)
        {
            if (mat.HasProperty(kv.Key))
                mat.SetFloat(kv.Key, kv.Value);
        }

        foreach (var kv in preset.Colors)
        {
            if (mat.HasProperty(kv.Key))
                mat.SetColor(kv.Key, kv.Value);
        }

        SyncKeywords(mat);
    }

    // Приводит keyword-ы в соответствие со значениями toggle-свойств.
    public static void SyncKeywords(Material mat)
    {
        foreach (var kv in CatCooksPresets.KeywordMap)
        {
            if (!mat.HasProperty(kv.Key))
                continue;

            if (mat.GetFloat(kv.Key) > 0.5f)
                mat.EnableKeyword(kv.Value);
            else
                mat.DisableKeyword(kv.Value);
        }
    }
}
