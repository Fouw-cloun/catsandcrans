// ============================================================================
//  CatCooksPresets.cs
//  Библиотека готовых настроек для шейдеров CatCooks/Toon Food и Toon Props.
//  Кладётся в любую папку Editor/.
//  Используется двумя вещами:
//    • CatCooksShaderGUI      — выпадающий список пресетов в инспекторе материала
//    • CatCooksPresetGenerator — генерация .mat ассетов пачкой (меню Tools)
// ============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace CatCooks.EditorTools
{
    public class ShaderPreset
    {
        public string Name;
        public string Hint;
        public Dictionary<string, float> Floats = new Dictionary<string, float>();
        public Dictionary<string, Color> Colors = new Dictionary<string, Color>();

        public ShaderPreset(string name, string hint)
        {
            Name = name;
            Hint = hint;
        }

        public ShaderPreset F(string prop, float value)
        {
            Floats[prop] = value;
            return this;
        }

        public ShaderPreset C(string prop, float r, float g, float b, float a = 1f)
        {
            Colors[prop] = new Color(r, g, b, a);
            return this;
        }
    }

    public static class CatCooksPresets
    {
        public const string FoodShader = "CatCooks/Toon Food";
        public const string PropsShader = "CatCooks/Toon Props";

        // Пары «float-свойство → keyword». Нужны, чтобы после применения
        // пресета материал не остался с рассинхроненными ключевыми словами.
        public static readonly Dictionary<string, string> KeywordMap = new Dictionary<string, string>
        {
            { "_AlphaClip",     "_ALPHATEST_ON" },
            { "_UseNormalMap",  "_NORMALMAP"    },
            { "_UseRamp",       "_RAMP_ON"      },
            { "_MatcapOn",      "_MATCAP_ON"    },
            { "_EnvOn",         "_ENV_ON"       },
            { "_UseTriplanar",  "_TRIPLANAR"    },
        };

        public static List<ShaderPreset> ForShader(string shaderName)
        {
            if (shaderName == FoodShader) return Food;
            if (shaderName == PropsShader) return Props;
            return null;
        }

        // ====================================================================
        //  ЕДА
        // ====================================================================
        public static readonly List<ShaderPreset> Food = new List<ShaderPreset>
        {
            // --- 01 ---------------------------------------------------------
            new ShaderPreset("Овощ свежий", "Огурец, перец, салат. Упругий, влажный, чуть просвечивает.")
                .F("_Saturation", 1.30f).F("_Brightness", 1.06f).F("_Gamma", 0.96f)
                .F("_Contrast", 1.10f).F("_Vibrance", 0.35f).F("_WBTemp", 0.02f)
                .F("_ShadowThreshold", 0.42f).F("_ShadowSoftness", 0.07f).F("_LightWrap", 0.20f)
                .C("_ShadowTint", 0.55f, 0.68f, 0.58f)
                .F("_SssOn", 1).F("_SSSStrength", 0.70f).F("_SSSPower", 5f).F("_SSSDistortion", 0.35f)
                .C("_SSSColor", 0.60f, 1.00f, 0.35f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.20f).F("_SpecIntensity", 1.30f).F("_SpecSoftness", 0.03f)
                .F("_SheenIntensity", 0.30f)
                .F("_GlazeOn", 1).F("_GlazeIntensity", 0.55f).F("_GlazePower", 4.5f)
                .C("_GlazeColor", 0.85f, 1.00f, 0.85f)
                .F("_TopLightStrength", 0.20f)
                .F("_OutlineWidth", 1.1f).C("_OutlineColor", 0.12f, 0.22f, 0.10f),

            // --- 02 ---------------------------------------------------------
            new ShaderPreset("Фрукт спелый", "Яблоко, персик, ягода. Максимально сочный цвет и блик.")
                .F("_Saturation", 1.40f).F("_Brightness", 1.10f).F("_Gamma", 0.94f)
                .F("_Contrast", 1.14f).F("_Vibrance", 0.40f).F("_WBTemp", 0.12f)
                .F("_ShadowThreshold", 0.40f).F("_ShadowSoftness", 0.10f).F("_LightWrap", 0.30f)
                .C("_ShadowTint", 0.88f, 0.50f, 0.45f)
                .F("_SssOn", 1).F("_SSSStrength", 1.10f).F("_SSSPower", 4f).F("_SSSDistortion", 0.45f)
                .C("_SSSColor", 1.00f, 0.40f, 0.22f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.18f).F("_SpecIntensity", 1.60f).F("_SpecSoftness", 0.03f)
                .F("_SheenIntensity", 0.40f)
                .F("_GlazeOn", 1).F("_GlazeIntensity", 0.75f).F("_GlazePower", 3.5f)
                .C("_GlazeColor", 1.00f, 0.88f, 0.72f)
                .F("_TopLightStrength", 0.22f)
                .F("_OutlineWidth", 1.2f).C("_OutlineColor", 0.28f, 0.10f, 0.10f),

            // --- 03 ---------------------------------------------------------
            new ShaderPreset("Мясо сырое", "Стейк, фарш. Плотный красный, сильная просветка, мокрая плёнка.")
                .F("_Saturation", 1.22f).F("_Brightness", 1.00f).F("_Gamma", 1.02f)
                .F("_Contrast", 1.08f).F("_Vibrance", 0.20f).F("_WBTemp", 0.10f)
                .F("_ShadowThreshold", 0.45f).F("_ShadowSoftness", 0.12f).F("_LightWrap", 0.35f)
                .C("_ShadowTint", 0.70f, 0.34f, 0.34f)
                .F("_SssOn", 1).F("_SSSStrength", 1.60f).F("_SSSPower", 3f).F("_SSSDistortion", 0.55f)
                .F("_SssAoThickness", 1)
                .C("_SSSColor", 1.00f, 0.22f, 0.18f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.30f).F("_SpecIntensity", 1.20f).F("_SpecSoftness", 0.06f)
                .F("_SheenIntensity", 0.45f)
                .C("_SheenColor", 1.00f, 0.82f, 0.78f)
                .F("_GlazeOn", 1).F("_GlazeIntensity", 0.70f).F("_GlazePower", 3f)
                .C("_GlazeColor", 1.00f, 0.80f, 0.78f)
                .F("_TopLightStrength", 0.16f)
                .F("_OutlineWidth", 1.2f).C("_OutlineColor", 0.30f, 0.09f, 0.09f),

            // --- 04 ---------------------------------------------------------
            new ShaderPreset("Мясо жареное", "Стейк с корочкой. Тёплые тени, поджарка по полостям.")
                .F("_Saturation", 1.18f).F("_Brightness", 1.02f).F("_Gamma", 1.05f)
                .F("_Contrast", 1.16f).F("_Vibrance", 0.25f).F("_WBTemp", 0.22f)
                .F("_ShadowThreshold", 0.46f).F("_ShadowSoftness", 0.08f).F("_LightWrap", 0.18f)
                .C("_ShadowTint", 0.66f, 0.40f, 0.28f)
                .F("_SssOn", 1).F("_SSSStrength", 0.45f).F("_SSSPower", 6f)
                .C("_SSSColor", 1.00f, 0.35f, 0.15f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.26f).F("_SpecIntensity", 1.10f).F("_SpecSoftness", 0.05f)
                .F("_SheenIntensity", 0.30f)
                .F("_GlazeOn", 1).F("_GlazeIntensity", 0.60f).F("_GlazePower", 4f)
                .C("_GlazeColor", 1.00f, 0.78f, 0.55f)
                .F("_CrustOn", 1).F("_CrustMode", 0).F("_CrustAmount", 0.65f)
                .F("_CrustThreshold", 0.45f).F("_CrustSoftness", 0.22f)
                .C("_CrustColor", 0.55f, 0.28f, 0.12f)
                .F("_TopLightStrength", 0.18f)
                .F("_OutlineWidth", 1.3f).C("_OutlineColor", 0.24f, 0.11f, 0.05f),

            // --- 05 ---------------------------------------------------------
            new ShaderPreset("Выпечка и хлеб", "Матовая тёплая поверхность, минимум блеска, румяная корка.")
                .F("_Saturation", 1.12f).F("_Brightness", 1.06f).F("_Gamma", 0.98f)
                .F("_Contrast", 1.06f).F("_Vibrance", 0.30f).F("_WBTemp", 0.28f)
                .F("_ShadowThreshold", 0.44f).F("_ShadowSoftness", 0.14f).F("_LightWrap", 0.40f)
                .C("_ShadowTint", 0.82f, 0.60f, 0.42f)
                .F("_SssOn", 1).F("_SSSStrength", 0.55f).F("_SSSPower", 7f).F("_SSSAmbient", 0.20f)
                .C("_SSSColor", 1.00f, 0.62f, 0.30f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.55f).F("_SpecIntensity", 0.25f).F("_SpecSoftness", 0.14f)
                .F("_SheenIntensity", 0.20f)
                .F("_GlazeOn", 1).F("_GlazeIntensity", 0.22f).F("_GlazePower", 5f)
                .C("_GlazeColor", 1.00f, 0.82f, 0.60f)
                .F("_CrustOn", 1).F("_CrustMode", 0).F("_CrustAmount", 0.45f)
                .C("_CrustColor", 0.72f, 0.45f, 0.20f)
                .F("_TopLightStrength", 0.24f)
                .F("_OutlineWidth", 1.2f).C("_OutlineColor", 0.30f, 0.16f, 0.07f),

            // --- 06 ---------------------------------------------------------
            new ShaderPreset("Сыр", "Мягкий жёлтый, заметная просветка, полуматовый.")
                .F("_Saturation", 1.25f).F("_Brightness", 1.10f).F("_Gamma", 0.95f)
                .F("_Contrast", 1.05f).F("_Vibrance", 0.30f).F("_WBTemp", 0.20f)
                .F("_ShadowThreshold", 0.40f).F("_ShadowSoftness", 0.16f).F("_LightWrap", 0.45f)
                .C("_ShadowTint", 0.90f, 0.70f, 0.40f)
                .F("_SssOn", 1).F("_SSSStrength", 1.30f).F("_SSSPower", 3.5f).F("_SSSAmbient", 0.25f)
                .C("_SSSColor", 1.00f, 0.75f, 0.20f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.42f).F("_SpecIntensity", 0.55f).F("_SpecSoftness", 0.10f)
                .F("_SheenIntensity", 0.35f)
                .F("_GlazeOn", 1).F("_GlazeIntensity", 0.35f).F("_GlazePower", 5f)
                .C("_GlazeColor", 1.00f, 0.90f, 0.55f)
                .F("_TopLightStrength", 0.20f)
                .F("_OutlineWidth", 1.2f).C("_OutlineColor", 0.35f, 0.22f, 0.05f),

            // --- 07 ---------------------------------------------------------
            new ShaderPreset("Тесто", "Матовое, светлое, мягкий свет. Лёгкая дрожь при замесе.")
                .F("_Saturation", 1.00f).F("_Brightness", 1.10f).F("_Gamma", 0.92f)
                .F("_Contrast", 0.95f).F("_Vibrance", 0.20f).F("_WBTemp", 0.18f)
                .F("_ShadowThreshold", 0.38f).F("_ShadowSoftness", 0.20f).F("_LightWrap", 0.55f)
                .C("_ShadowTint", 0.86f, 0.76f, 0.66f)
                .F("_SssOn", 1).F("_SSSStrength", 0.80f).F("_SSSPower", 6f).F("_SSSAmbient", 0.30f)
                .C("_SSSColor", 1.00f, 0.80f, 0.62f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.65f).F("_SpecIntensity", 0.15f).F("_SpecSoftness", 0.20f)
                .F("_SheenIntensity", 0.18f)
                .F("_GlazeOn", 0)
                .F("_TopLightStrength", 0.26f)
                .F("_JiggleAmp", 0.012f).F("_JiggleSpeed", 4f).F("_JiggleFreq", 3f)
                .F("_OutlineWidth", 1.1f).C("_OutlineColor", 0.34f, 0.26f, 0.18f),

            // --- 08 ---------------------------------------------------------
            new ShaderPreset("Желе и пудинг", "Полупрозрачный, сильный SSS, глянец и настоящая дрожь.")
                .F("_Saturation", 1.35f).F("_Brightness", 1.12f).F("_Gamma", 0.90f)
                .F("_Contrast", 1.05f).F("_Vibrance", 0.35f).F("_WBTemp", 0.05f)
                .F("_ShadowThreshold", 0.35f).F("_ShadowSoftness", 0.22f).F("_LightWrap", 0.60f)
                .C("_ShadowTint", 0.85f, 0.55f, 0.70f)
                .F("_SssOn", 1).F("_SSSStrength", 2.20f).F("_SSSPower", 2.5f).F("_SSSDistortion", 0.60f)
                .F("_SSSAmbient", 0.35f)
                .C("_SSSColor", 1.00f, 0.35f, 0.55f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.15f).F("_SpecIntensity", 1.80f).F("_SpecSoftness", 0.02f)
                .F("_SheenIntensity", 0.50f)
                .F("_GlazeOn", 1).F("_GlazeIntensity", 1.10f).F("_GlazePower", 2.5f)
                .C("_GlazeColor", 1.00f, 0.90f, 0.95f)
                .F("_EnvOn", 1).F("_EnvStrength", 0.35f).F("_EnvFresnel", 3.5f).F("_EnvSteps", 0)
                .F("_TopLightStrength", 0.28f)
                .F("_JiggleAmp", 0.045f).F("_JiggleSpeed", 9f).F("_JiggleFreq", 5f).F("_JiggleFalloff", 1.2f)
                .F("_OutlineWidth", 1.0f).C("_OutlineColor", 0.35f, 0.10f, 0.28f),

            // --- 09 ---------------------------------------------------------
            new ShaderPreset("Суп и соус", "Жидкость в гигантской кастрюле. Широкий блик, натёк по краям.")
                .F("_Saturation", 1.25f).F("_Brightness", 1.04f).F("_Gamma", 1.00f)
                .F("_Contrast", 1.10f).F("_Vibrance", 0.25f).F("_WBTemp", 0.18f)
                .F("_ShadowThreshold", 0.40f).F("_ShadowSoftness", 0.15f).F("_LightWrap", 0.35f)
                .C("_ShadowTint", 0.72f, 0.44f, 0.30f)
                .F("_SssOn", 1).F("_SSSStrength", 0.60f).F("_SSSPower", 5f)
                .C("_SSSColor", 1.00f, 0.45f, 0.15f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.12f).F("_SpecIntensity", 2.00f).F("_SpecSoftness", 0.02f)
                .F("_SheenIntensity", 0.60f).F("_SheenSize", 0.9f)
                .F("_GlazeOn", 1).F("_GlazeIntensity", 1.00f).F("_GlazePower", 3f)
                .C("_GlazeColor", 1.00f, 0.92f, 0.80f)
                .F("_SauceOn", 1).F("_SauceLevel", 0.1f).F("_SauceSoftness", 0.25f)
                .F("_SauceTintStrength", 0.55f).F("_SauceWetness", 1.5f)
                .C("_SauceColor", 0.70f, 0.28f, 0.08f)
                .F("_EnvOn", 1).F("_EnvStrength", 0.45f).F("_EnvFresnel", 2.5f).F("_EnvSteps", 4)
                .F("_TopLightStrength", 0.24f)
                .F("_JiggleAmp", 0.008f).F("_JiggleSpeed", 3f)
                .F("_OutlineWidth", 0.9f).C("_OutlineColor", 0.28f, 0.14f, 0.06f),

            // --- 10 ---------------------------------------------------------
            new ShaderPreset("Крем и мороженое", "Холодные тени, мягкий свет, мелкие искры кристаллов.")
                .F("_Saturation", 1.10f).F("_Brightness", 1.14f).F("_Gamma", 0.90f)
                .F("_Contrast", 1.00f).F("_Vibrance", 0.30f).F("_WBTemp", -0.10f)
                .F("_ShadowThreshold", 0.36f).F("_ShadowSoftness", 0.22f).F("_LightWrap", 0.60f)
                .C("_ShadowTint", 0.72f, 0.76f, 0.92f)
                .F("_SssOn", 1).F("_SSSStrength", 1.10f).F("_SSSPower", 4f).F("_SSSAmbient", 0.30f)
                .C("_SSSColor", 0.85f, 0.90f, 1.00f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.45f).F("_SpecIntensity", 0.60f).F("_SpecSoftness", 0.12f)
                .F("_SheenIntensity", 0.45f)
                .C("_SheenColor", 0.90f, 0.95f, 1.00f)
                .F("_GlazeOn", 1).F("_GlazeIntensity", 0.40f).F("_GlazePower", 5f)
                .C("_GlazeColor", 0.90f, 0.95f, 1.00f)
                .F("_SparkleOn", 1).F("_SparkleDensity", 140f).F("_SparkleAmount", 0.10f)
                .F("_SparkleIntensity", 1.20f).F("_SparkleSpeed", 2.5f)
                .F("_TopLightStrength", 0.30f)
                .F("_OutlineWidth", 1.1f).C("_OutlineColor", 0.28f, 0.28f, 0.40f),

            // --- 11 ---------------------------------------------------------
            new ShaderPreset("Шоколад и глазурь", "Тёмный, зеркальный, густой. Отражение окружения.")
                .F("_Saturation", 1.15f).F("_Brightness", 0.98f).F("_Gamma", 1.10f)
                .F("_Contrast", 1.20f).F("_Vibrance", 0.15f).F("_WBTemp", 0.20f)
                .F("_ShadowThreshold", 0.48f).F("_ShadowSoftness", 0.10f).F("_LightWrap", 0.20f)
                .C("_ShadowTint", 0.48f, 0.30f, 0.22f)
                .F("_SssOn", 0)
                .F("_SpecularOn", 1).F("_SpecSize", 0.14f).F("_SpecIntensity", 2.20f).F("_SpecSoftness", 0.02f)
                .F("_SheenIntensity", 0.35f)
                .F("_GlazeOn", 1).F("_GlazeIntensity", 1.20f).F("_GlazePower", 2.5f)
                .C("_GlazeColor", 1.00f, 0.80f, 0.60f)
                .F("_EnvOn", 1).F("_EnvStrength", 0.70f).F("_EnvFresnel", 2f).F("_EnvSteps", 3)
                .F("_TopLightStrength", 0.20f)
                .F("_OutlineWidth", 1.2f).C("_OutlineColor", 0.14f, 0.06f, 0.03f),

            // --- 12 ---------------------------------------------------------
            new ShaderPreset("Леденец и сахар", "Кристаллический, яркий, много мелких искр.")
                .F("_Saturation", 1.45f).F("_Brightness", 1.14f).F("_Gamma", 0.88f)
                .F("_Contrast", 1.18f).F("_Vibrance", 0.45f).F("_WBTemp", 0.00f)
                .F("_ShadowThreshold", 0.40f).F("_ShadowSoftness", 0.06f).F("_LightWrap", 0.25f)
                .C("_ShadowTint", 0.80f, 0.60f, 0.90f)
                .F("_SssOn", 1).F("_SSSStrength", 1.40f).F("_SSSPower", 3f).F("_SSSAmbient", 0.30f)
                .C("_SSSColor", 1.00f, 0.55f, 0.85f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.10f).F("_SpecIntensity", 2.40f).F("_SpecSoftness", 0.015f)
                .F("_SheenIntensity", 0.30f)
                .F("_GlazeOn", 1).F("_GlazeIntensity", 1.30f).F("_GlazePower", 2f)
                .C("_GlazeColor", 1.00f, 0.95f, 1.00f)
                .F("_SparkleOn", 1).F("_SparkleDensity", 220f).F("_SparkleAmount", 0.18f)
                .F("_SparkleIntensity", 2.20f).F("_SparkleSpeed", 5f)
                .F("_EnvOn", 1).F("_EnvStrength", 0.40f).F("_EnvFresnel", 3f).F("_EnvSteps", 0)
                .F("_TopLightStrength", 0.24f)
                .F("_OutlineWidth", 1.0f).C("_OutlineColor", 0.30f, 0.12f, 0.34f),

            // --- 13 ---------------------------------------------------------
            new ShaderPreset("Фритюр и хрустящее", "Золотистая корка, множество мелких бликов на неровностях.")
                .F("_Saturation", 1.28f).F("_Brightness", 1.08f).F("_Gamma", 0.96f)
                .F("_Contrast", 1.22f).F("_Vibrance", 0.35f).F("_WBTemp", 0.30f)
                .F("_ShadowThreshold", 0.46f).F("_ShadowSoftness", 0.07f).F("_LightWrap", 0.15f)
                .C("_ShadowTint", 0.72f, 0.48f, 0.24f)
                .F("_SssOn", 1).F("_SSSStrength", 0.40f).F("_SSSPower", 8f)
                .C("_SSSColor", 1.00f, 0.55f, 0.15f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.22f).F("_SpecIntensity", 1.40f).F("_SpecSoftness", 0.04f)
                .F("_SheenIntensity", 0.35f)
                .F("_GlazeOn", 1).F("_GlazeIntensity", 0.70f).F("_GlazePower", 4f)
                .C("_GlazeColor", 1.00f, 0.80f, 0.45f)
                .F("_CrustOn", 1).F("_CrustMode", 0).F("_CrustAmount", 0.50f).F("_CrustThreshold", 0.50f)
                .C("_CrustColor", 0.80f, 0.52f, 0.18f)
                .F("_TopLightStrength", 0.22f)
                .F("_OutlineWidth", 1.3f).C("_OutlineColor", 0.30f, 0.16f, 0.04f),

            // --- 14 ---------------------------------------------------------
            new ShaderPreset("Рыба и морепродукты", "Холодная влажная поверхность, перламутровый отблеск.")
                .F("_Saturation", 1.20f).F("_Brightness", 1.06f).F("_Gamma", 0.96f)
                .F("_Contrast", 1.12f).F("_Vibrance", 0.30f).F("_WBTemp", -0.08f)
                .F("_ShadowThreshold", 0.42f).F("_ShadowSoftness", 0.10f).F("_LightWrap", 0.30f)
                .C("_ShadowTint", 0.62f, 0.70f, 0.85f)
                .F("_SssOn", 1).F("_SSSStrength", 0.85f).F("_SSSPower", 5f)
                .C("_SSSColor", 1.00f, 0.55f, 0.55f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.16f).F("_SpecIntensity", 1.80f).F("_SpecSoftness", 0.03f)
                .F("_SheenIntensity", 0.55f).F("_SheenSize", 0.85f)
                .C("_SheenColor", 0.80f, 0.95f, 1.00f)
                .F("_GlazeOn", 1).F("_GlazeIntensity", 0.90f).F("_GlazePower", 3f)
                .C("_GlazeColor", 0.85f, 0.95f, 1.00f)
                .F("_EnvOn", 1).F("_EnvStrength", 0.35f).F("_EnvFresnel", 3f).F("_EnvSteps", 0)
                .F("_TopLightStrength", 0.20f)
                .F("_OutlineWidth", 1.1f).C("_OutlineColor", 0.14f, 0.20f, 0.30f),
        };

        // ====================================================================
        //  ИНСТРУМЕНТЫ И РЕКВИЗИТ
        // ====================================================================
        public static readonly List<ShaderPreset> Props = new List<ShaderPreset>
        {
            // Правила v2.1 для ВСЕХ пресетов реквизита:
            //  • цветокоррекция в материале нейтральная (Saturation/Contrast/Gamma = 1, Vibrance = 0) —
            //    насыщенность задаёт текстура и общий Volume, а не каждый материал по-своему;
            //  • мягкая тень (Softness 0.2–0.35) + Light Wrap 0.3–0.4 — свет обтекает форму;
            //  • тёплые цветные тени (ShadowTint + ShadowSaturation), тёплая кромка света;
            //  • блики и rim — только там, где материал правда блестит (металл, эмаль, стекло);
            //  • контур тонкий (≈1 px) и цветной: тёмная версия цвета предмета.

            // --- 00 ---------------------------------------------------------
            new ShaderPreset("Мягкий стилизованный (база)", "Начни с него для любого пропса. Как в Shady Job: мягкий свет, цветные тени.")
                .F("_Saturation", 1.00f).F("_Brightness", 1.00f).F("_Gamma", 1.00f).F("_Contrast", 1.00f).F("_Vibrance", 0f).F("_WBTemp", 0f)
                .F("_MetallicStrength", 0f)
                .F("_ShadowThreshold", 0.45f).F("_ShadowSoftness", 0.28f).F("_LightWrap", 0.35f)
                .C("_ShadowTint", 0.80f, 0.68f, 0.76f).F("_ShadowSaturation", 0.35f)
                .C("_TerminatorColor", 1.00f, 0.62f, 0.42f).F("_TerminatorStrength", 0.25f).F("_TerminatorWidth", 0.18f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.60f).F("_SpecIntensity", 0.25f).F("_SpecSoftness", 0.20f)
                .F("_SheenIntensity", 0.06f)
                .F("_RimOn", 1).F("_RimIntensity", 0.20f).F("_RimPower", 4f)
                .F("_EnvOn", 0).F("_GrimeOn", 0).F("_WearOn", 0)
                .F("_UseNormalMap", 0)
                .F("_OutlineWidth", 1.0f).F("_OutlineTextureBlend", 0.8f).C("_OutlineColor", 0.16f, 0.10f, 0.10f),

            // --- 01 ---------------------------------------------------------
            new ShaderPreset("Сталь инструментальная", "Ключи, лопаты, ножи. Мягкий широкий блик, отражение в 3 ступени.")
                .F("_Saturation", 1.00f).F("_Brightness", 1.00f).F("_Gamma", 1.00f).F("_Contrast", 1.00f).F("_Vibrance", 0f).F("_WBTemp", 0f)
                .F("_MetallicStrength", 0.80f).F("_MetallicDiffuseDim", 0.45f)
                .F("_ShadowThreshold", 0.45f).F("_ShadowSoftness", 0.18f).F("_LightWrap", 0.25f)
                .C("_ShadowTint", 0.70f, 0.66f, 0.80f).F("_ShadowSaturation", 0.20f)
                .C("_TerminatorColor", 1.00f, 0.70f, 0.55f).F("_TerminatorStrength", 0.15f).F("_TerminatorWidth", 0.15f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.45f).F("_SpecIntensity", 0.70f).F("_SpecSoftness", 0.10f)
                .F("_SheenIntensity", 0.20f)
                .F("_RimOn", 1).F("_RimIntensity", 0.30f).F("_RimPower", 4f)
                .F("_EnvOn", 1).F("_EnvStrength", 0.55f).F("_EnvSteps", 3).F("_EnvFresnel", 2f)
                .C("_EnvSkyColor", 0.70f, 0.82f, 0.95f).C("_EnvHorizonColor", 1.00f, 0.92f, 0.80f).C("_EnvGroundColor", 0.45f, 0.36f, 0.28f)
                .F("_OutlineWidth", 1.0f).F("_OutlineTextureBlend", 0.8f).C("_OutlineColor", 0.12f, 0.12f, 0.16f),

            // --- 02 ---------------------------------------------------------
            new ShaderPreset("Крашеный металл (техника)", "Жёлтый экскаватор, корпуса, баллоны. Полуматовая краска, мягкие потёртости.")
                .F("_Saturation", 1.00f).F("_Brightness", 1.00f).F("_Gamma", 1.00f).F("_Contrast", 1.00f).F("_Vibrance", 0f).F("_WBTemp", 0f)
                .F("_MetallicStrength", 0.10f).F("_MetallicDiffuseDim", 0.20f)
                .F("_ShadowThreshold", 0.45f).F("_ShadowSoftness", 0.25f).F("_LightWrap", 0.32f)
                .C("_ShadowTint", 0.78f, 0.66f, 0.74f).F("_ShadowSaturation", 0.35f)
                .C("_TerminatorColor", 1.00f, 0.62f, 0.42f).F("_TerminatorStrength", 0.22f).F("_TerminatorWidth", 0.18f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.55f).F("_SpecIntensity", 0.35f).F("_SpecSoftness", 0.18f)
                .F("_SheenIntensity", 0.08f)
                .F("_RimOn", 1).F("_RimIntensity", 0.20f)
                .F("_WearOn", 1).F("_WearAmount", 0.20f).F("_WearPower", 6f).F("_WearMetal", 0.30f)
                .C("_WearColor", 0.85f, 0.80f, 0.72f)
                .F("_GrimeOn", 1).F("_GrimeStrength", 0.25f).F("_DustStrength", 0.08f)
                .C("_GrimeColor", 0.35f, 0.24f, 0.18f)
                .F("_EnvOn", 0)
                .F("_OutlineWidth", 1.0f).F("_OutlineTextureBlend", 0.8f).C("_OutlineColor", 0.16f, 0.10f, 0.06f),

            // --- 03 ---------------------------------------------------------
            new ShaderPreset("Ржавый металл", "Старые бочки, арматура, балки. Тусклый, тёплый, без бликов.")
                .F("_Saturation", 1.00f).F("_Brightness", 1.00f).F("_Gamma", 1.00f).F("_Contrast", 1.00f).F("_Vibrance", 0f).F("_WBTemp", 0f)
                .F("_MetallicStrength", 0.15f).F("_MetallicDiffuseDim", 0.20f)
                .F("_ShadowThreshold", 0.46f).F("_ShadowSoftness", 0.28f).F("_LightWrap", 0.35f)
                .C("_ShadowTint", 0.76f, 0.58f, 0.56f).F("_ShadowSaturation", 0.40f)
                .C("_TerminatorColor", 1.00f, 0.58f, 0.36f).F("_TerminatorStrength", 0.28f).F("_TerminatorWidth", 0.2f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.70f).F("_SpecIntensity", 0.15f).F("_SpecSoftness", 0.25f)
                .F("_SheenIntensity", 0.05f)
                .F("_RimOn", 1).F("_RimIntensity", 0.15f)
                .F("_GrimeOn", 1).F("_GrimeStrength", 0.40f).F("_DustStrength", 0.15f)
                .C("_GrimeColor", 0.38f, 0.20f, 0.12f).C("_DustColor", 0.80f, 0.66f, 0.50f)
                .F("_EnvOn", 0)
                .F("_OutlineWidth", 1.0f).F("_OutlineTextureBlend", 0.8f).C("_OutlineColor", 0.18f, 0.09f, 0.05f),

            // --- 04 ---------------------------------------------------------
            new ShaderPreset("Хром полированный", "Половник-гигант, миксер. Toon-металл: крупные ступени отражения, не зеркало.")
                .F("_Saturation", 1.00f).F("_Brightness", 1.00f).F("_Gamma", 1.00f).F("_Contrast", 1.00f).F("_Vibrance", 0f).F("_WBTemp", 0f)
                .F("_MetallicStrength", 1.00f).F("_MetallicDiffuseDim", 0.70f)
                .F("_ShadowThreshold", 0.48f).F("_ShadowSoftness", 0.12f).F("_LightWrap", 0.20f)
                .C("_ShadowTint", 0.66f, 0.66f, 0.82f).F("_ShadowSaturation", 0.15f)
                .F("_TerminatorStrength", 0.10f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.35f).F("_SpecIntensity", 1.20f).F("_SpecSoftness", 0.06f)
                .F("_SheenIntensity", 0.25f)
                .F("_RimOn", 1).F("_RimIntensity", 0.45f).F("_RimPower", 3f)
                .F("_EnvOn", 1).F("_EnvStrength", 1.00f).F("_EnvSteps", 3).F("_EnvFresnel", 1.2f).F("_EnvMetalMask", 1.0f)
                .C("_EnvSkyColor", 0.70f, 0.82f, 0.95f).C("_EnvHorizonColor", 1.00f, 0.92f, 0.80f).C("_EnvGroundColor", 0.45f, 0.36f, 0.28f)
                .F("_OutlineWidth", 1.0f).F("_OutlineTextureBlend", 0.6f).C("_OutlineColor", 0.10f, 0.12f, 0.18f),

            // --- 05 ---------------------------------------------------------
            new ShaderPreset("Медь и латунь", "Тёплый металл, кастрюли и трубы.")
                .F("_Saturation", 1.00f).F("_Brightness", 1.00f).F("_Gamma", 1.00f).F("_Contrast", 1.00f).F("_Vibrance", 0f).F("_WBTemp", 0f)
                .F("_MetallicStrength", 0.80f).F("_MetallicDiffuseDim", 0.45f)
                .F("_ShadowThreshold", 0.46f).F("_ShadowSoftness", 0.18f).F("_LightWrap", 0.28f)
                .C("_ShadowTint", 0.76f, 0.56f, 0.48f).F("_ShadowSaturation", 0.30f)
                .C("_TerminatorColor", 1.00f, 0.70f, 0.40f).F("_TerminatorStrength", 0.22f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.40f).F("_SpecIntensity", 0.90f).F("_SpecSoftness", 0.08f)
                .F("_SheenIntensity", 0.20f).C("_SpecColor2", 1.00f, 0.88f, 0.65f)
                .F("_RimOn", 1).F("_RimIntensity", 0.30f).C("_RimColor", 1.00f, 0.85f, 0.60f)
                .F("_EnvOn", 1).F("_EnvStrength", 0.70f).F("_EnvSteps", 3).F("_EnvFresnel", 1.8f)
                .C("_EnvHorizonColor", 1.00f, 0.85f, 0.60f)
                .F("_GrimeOn", 1).F("_GrimeStrength", 0.20f)
                .F("_OutlineWidth", 1.0f).F("_OutlineTextureBlend", 0.8f).C("_OutlineColor", 0.20f, 0.11f, 0.05f),

            // --- 06 ---------------------------------------------------------
            new ShaderPreset("Дерево", "Ручки инструментов, ящики, доски, поддоны. Матовое, тёплое, без бликов.")
                .F("_Saturation", 1.00f).F("_Brightness", 1.00f).F("_Gamma", 1.00f).F("_Contrast", 1.00f).F("_Vibrance", 0f).F("_WBTemp", 0f)
                .F("_MetallicStrength", 0.00f)
                .F("_ShadowThreshold", 0.44f).F("_ShadowSoftness", 0.30f).F("_LightWrap", 0.38f)
                .C("_ShadowTint", 0.78f, 0.62f, 0.62f).F("_ShadowSaturation", 0.35f)
                .C("_TerminatorColor", 1.00f, 0.62f, 0.40f).F("_TerminatorStrength", 0.25f).F("_TerminatorWidth", 0.2f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.75f).F("_SpecIntensity", 0.08f).F("_SpecSoftness", 0.25f)
                .F("_SheenIntensity", 0.03f)
                .F("_RimOn", 1).F("_RimIntensity", 0.12f)
                .F("_GrimeOn", 0).F("_DustStrength", 0.0f)
                .F("_EnvOn", 0).F("_UseNormalMap", 0)
                .F("_OutlineWidth", 0.9f).F("_OutlineTextureBlend", 0.85f).C("_OutlineColor", 0.20f, 0.12f, 0.06f),

            // --- 07 ---------------------------------------------------------
            new ShaderPreset("Пластик", "Каски, вёдра, бочки. Матовый крашеный пластик, мягкий широкий блик.")
                .F("_Saturation", 1.00f).F("_Brightness", 1.00f).F("_Gamma", 1.00f).F("_Contrast", 1.00f).F("_Vibrance", 0f).F("_WBTemp", 0f)
                .F("_MetallicStrength", 0.00f)
                .F("_ShadowThreshold", 0.44f).F("_ShadowSoftness", 0.28f).F("_LightWrap", 0.35f)
                .C("_ShadowTint", 0.78f, 0.70f, 0.82f).F("_ShadowSaturation", 0.30f)
                .C("_TerminatorColor", 1.00f, 0.66f, 0.50f).F("_TerminatorStrength", 0.20f).F("_TerminatorWidth", 0.18f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.55f).F("_SpecIntensity", 0.30f).F("_SpecSoftness", 0.20f)
                .F("_SheenIntensity", 0.06f)
                .F("_RimOn", 1).F("_RimIntensity", 0.18f).F("_RimPower", 4f)
                .F("_EnvOn", 0).F("_UseNormalMap", 0)
                .F("_OutlineWidth", 0.9f).F("_OutlineTextureBlend", 0.85f).C("_OutlineColor", 0.10f, 0.12f, 0.18f),

            // --- 08 ---------------------------------------------------------
            new ShaderPreset("Резина", "Шланги, шины, прокладки. Глубокий матовый тёмный, не чёрная дыра.")
                .F("_Saturation", 1.00f).F("_Brightness", 1.00f).F("_Gamma", 1.00f).F("_Contrast", 1.00f).F("_Vibrance", 0f).F("_WBTemp", 0f)
                .F("_MetallicStrength", 0.00f)
                .F("_ShadowThreshold", 0.46f).F("_ShadowSoftness", 0.30f).F("_LightWrap", 0.35f)
                .C("_ShadowTint", 0.70f, 0.64f, 0.72f).F("_ShadowSaturation", 0.25f)
                .F("_TerminatorStrength", 0.12f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.80f).F("_SpecIntensity", 0.12f).F("_SpecSoftness", 0.25f)
                .F("_SheenIntensity", 0.08f)
                .F("_RimOn", 1).F("_RimIntensity", 0.25f).F("_RimPower", 4f)
                .F("_EnvOn", 0)
                .F("_OutlineWidth", 1.0f).F("_OutlineTextureBlend", 0.6f).C("_OutlineColor", 0.06f, 0.06f, 0.08f),

            // --- 09 ---------------------------------------------------------
            new ShaderPreset("Эмаль и керамика", "Гигантские кастрюли, тарелки. Гладкий блеск, светлые тени.")
                .F("_Saturation", 1.00f).F("_Brightness", 1.00f).F("_Gamma", 1.00f).F("_Contrast", 1.00f).F("_Vibrance", 0f).F("_WBTemp", 0f)
                .F("_MetallicStrength", 0.00f)
                .F("_ShadowThreshold", 0.42f).F("_ShadowSoftness", 0.25f).F("_LightWrap", 0.35f)
                .C("_ShadowTint", 0.82f, 0.76f, 0.88f).F("_ShadowSaturation", 0.30f)
                .F("_TerminatorStrength", 0.18f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.35f).F("_SpecIntensity", 0.90f).F("_SpecSoftness", 0.06f)
                .F("_SheenIntensity", 0.15f)
                .F("_RimOn", 1).F("_RimIntensity", 0.30f).F("_RimPower", 3.5f)
                .F("_EnvOn", 1).F("_EnvStrength", 0.30f).F("_EnvSteps", 0).F("_EnvFresnel", 3f).F("_EnvMetalMask", 0f)
                .F("_OutlineWidth", 1.0f).F("_OutlineTextureBlend", 0.8f).C("_OutlineColor", 0.16f, 0.16f, 0.22f),

            // --- 10 ---------------------------------------------------------
            new ShaderPreset("Бетон и камень", "Плиты, блоки, мешки цемента. Полностью матовый, пыльный.")
                .F("_Saturation", 1.00f).F("_Brightness", 1.00f).F("_Gamma", 1.00f).F("_Contrast", 1.00f).F("_Vibrance", 0f).F("_WBTemp", 0f)
                .F("_MetallicStrength", 0.00f)
                .F("_ShadowThreshold", 0.46f).F("_ShadowSoftness", 0.32f).F("_LightWrap", 0.38f)
                .C("_ShadowTint", 0.78f, 0.70f, 0.74f).F("_ShadowSaturation", 0.30f)
                .F("_TerminatorStrength", 0.18f)
                .F("_SpecularOn", 0)
                .F("_RimOn", 1).F("_RimIntensity", 0.12f)
                .F("_GrimeOn", 1).F("_GrimeStrength", 0.30f).F("_DustStrength", 0.20f).F("_DustSharpness", 1.5f)
                .C("_GrimeColor", 0.40f, 0.30f, 0.24f)
                .F("_EnvOn", 0)
                .F("_OutlineWidth", 1.0f).F("_OutlineTextureBlend", 0.8f).C("_OutlineColor", 0.14f, 0.13f, 0.12f),

            // --- 11 ---------------------------------------------------------
            new ShaderPreset("Стекло и банки", "Прозрачные ёмкости. Сильный френель, минимум диффуза.")
                .F("_Saturation", 1.00f).F("_Brightness", 1.00f).F("_Gamma", 1.00f).F("_Contrast", 1.00f).F("_Vibrance", 0f).F("_WBTemp", 0f)
                .F("_MetallicStrength", 0.10f).F("_MetallicDiffuseDim", 0.50f)
                .F("_ShadowThreshold", 0.38f).F("_ShadowSoftness", 0.30f).F("_LightWrap", 0.45f)
                .C("_ShadowTint", 0.84f, 0.90f, 0.96f).F("_ShadowSaturation", 0.20f)
                .F("_TerminatorStrength", 0.0f)
                .F("_SpecularOn", 1).F("_SpecSize", 0.25f).F("_SpecIntensity", 1.40f).F("_SpecSoftness", 0.05f)
                .F("_SheenIntensity", 0.20f)
                .F("_RimOn", 1).F("_RimIntensity", 0.80f).F("_RimPower", 2.5f).F("_RimThreshold", 0.25f)
                .C("_RimColor", 0.85f, 0.95f, 1.00f)
                .F("_EnvOn", 1).F("_EnvStrength", 0.60f).F("_EnvSteps", 0).F("_EnvFresnel", 1.5f)
                .F("_OutlineWidth", 0.8f).F("_OutlineTextureBlend", 0.5f).C("_OutlineColor", 0.30f, 0.38f, 0.45f),

            // --- 12 ---------------------------------------------------------
            new ShaderPreset("Картон и мешковина", "Коробки, мешки. Совсем без блеска, тёплый и мягкий.")
                .F("_Saturation", 1.00f).F("_Brightness", 1.00f).F("_Gamma", 1.00f).F("_Contrast", 1.00f).F("_Vibrance", 0f).F("_WBTemp", 0f)
                .F("_MetallicStrength", 0.00f)
                .F("_ShadowThreshold", 0.42f).F("_ShadowSoftness", 0.35f).F("_LightWrap", 0.42f)
                .C("_ShadowTint", 0.80f, 0.66f, 0.62f).F("_ShadowSaturation", 0.35f)
                .C("_TerminatorColor", 1.00f, 0.62f, 0.42f).F("_TerminatorStrength", 0.25f)
                .F("_SpecularOn", 0)
                .F("_RimOn", 1).F("_RimIntensity", 0.12f)
                .F("_GrimeOn", 1).F("_GrimeStrength", 0.20f).F("_DustStrength", 0.12f)
                .F("_EnvOn", 0)
                .F("_OutlineWidth", 1.0f).F("_OutlineTextureBlend", 0.85f).C("_OutlineColor", 0.24f, 0.16f, 0.08f),
        };
    }
}
