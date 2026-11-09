// ============================================================================
//  CatCooks/Toon Props  v2.1  —  toon-шейдер для инструментов, техники,
//  посуды и строительного реквизита (Unity 6 / URP)
// ----------------------------------------------------------------------------
//  v2.1 (стилизованный мягкий свет, как в Shady Job / Tiny Eden):
//   • ИСПРАВЛЕНО: Light Wrap раньше считался после обрезки NdotL в 0, поэтому
//     вся теневая сторона получала одинаковую «плоскую» яркость. Теперь wrap
//     честно обтекает форму.
//   • НОВОЕ: «Насыщенность теней» — тени цветные, а не серые (как в анимации).
//   • НОВОЕ: «Тёплая кромка света» — тёплая полоска на границе света и тени.
//   • ИСПРАВЛЕНО: контур «Tint By Base Map» — теперь тёмная насыщенная версия
//     цвета предмета, а не чёрно-фиолетовая линия.
//   • Значения по умолчанию — мягкий стиль: тень мягкая, wrap 0.35, тёплая
//     тень, бликов и rim почти нет, контур тонкий и цветной.
//     (На уже созданных материалах значения не меняются — нажми пресет.)
//  Что нового по сравнению с v1:
//   • Тот же цвето-грейдинг, что и в Toon Food (экспозиция, гамма, контраст,
//     насыщенность, vibrance, hue, баланс белого, тонировка теней/светов) —
//     чтобы еда и инструменты жили в одной цветовой палитре.
//   • Ramp-текстура освещения + Light Wrap.
//   • Стилизованные отражения: процедурное «небо» (sky/horizon/ground с
//     постеризацией) ИЛИ кубмапа. Главный рычаг для мультяшного металла.
//   • Matcap с режимами Add / Multiply / Screen.
//   • Grime — грязь/копоть по полостям AO (стройка!) и Dust — пыль сверху.
//   • Edge Wear — потёртая краска / блик по рёбрам через френель.
//   • Двойной спекуляр: острый блик + широкий металлический отблеск.
//   • Улучшенный контур: пиксельный/мировой режим, затухание по дистанции,
//     сглаженные нормали, тонировка текстурой, wobble.
//   • GPU Instancing, SSAO, alpha clip, double-sided, Cull/Blend/ZWrite,
//     спекуляр от доп. источников, пульсирующая эмиссия (индикаторы техники).
// ============================================================================
Shader "CatCooks/Toon Props"
{
    Properties
    {
        [Header(Base)]
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor ("Base Tint", Color) = (1,1,1,1)
        _VertexColorTint ("Vertex Color Tint", Range(0,1)) = 0

        [Space(6)]
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 1

        [Header(Surface Maps)]
        [Toggle(_NORMALMAP)] _UseNormalMap ("Use Normal Map", Float) = 0
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 2)) = 1
        _MetallicMap ("Metallic Map (R)", 2D) = "black" {}
        _MetallicStrength ("Metallic Strength", Range(0, 1)) = 1
        _MetallicDiffuseDim ("Metallic Darkens Diffuse", Range(0, 1)) = 0.55
        _OcclusionMap ("AO Map (G)", 2D) = "white" {}
        _OcclusionStrength ("AO Strength", Range(0, 1)) = 1
        _OcclusionDirect ("AO Affects Direct Light", Range(0, 1)) = 0.35

        [Header(Color Grading)]
        _Exposure ("Exposure", Range(0, 3)) = 1
        _Brightness ("Brightness", Range(0.5, 1.6)) = 1.0
        _Gamma ("Gamma", Range(0.5, 1.6)) = 1.0
        _Contrast ("Contrast", Range(0, 2)) = 1.0
        _ContrastPivot ("Contrast Pivot", Range(0, 1)) = 0.5
        _Saturation ("Saturation", Range(0, 2)) = 1.0
        _Vibrance ("Vibrance", Range(0, 1)) = 0
        _HueShift ("Hue Shift", Range(-0.5, 0.5)) = 0
        _WBTemp ("White Balance Temperature", Range(-1, 1)) = 0
        _WBTint ("White Balance Tint", Range(-1, 1)) = 0
        _ColorFilter ("Color Filter", Color) = (1,1,1,1)
        _ShadowsColor ("Grade: Shadows Tint", Color) = (1,1,1,1)
        _HighlightsColor ("Grade: Highlights Tint", Color) = (1,1,1,1)
        _ToneSplit ("Grade: Shadow/Highlight Split", Range(0, 1)) = 0.5
        [ToggleUI] _GradeFinal ("Grade Final Color (instead of albedo)", Float) = 0

        [Header(Toon Lighting)]
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.45
        _ShadowSoftness ("Shadow Softness", Range(0.001, 0.5)) = 0.25
        _ShadowTint ("Shadow Tint", Color) = (0.80, 0.68, 0.76, 1)
        _LightWrap ("Light Wrap", Range(0, 1)) = 0.35
        _ShadowSaturation ("Насыщенность теней (цветные тени)", Range(0, 1)) = 0.35
        _TerminatorColor ("Тёплая кромка света: цвет", Color) = (1.0, 0.62, 0.42, 1)
        _TerminatorStrength ("Тёплая кромка света: сила", Range(0, 1)) = 0.25
        _TerminatorWidth ("Тёплая кромка света: ширина", Range(0.02, 0.5)) = 0.18
        _ExtraBands ("Extra Light Bands (0 = off)", Range(0, 4)) = 0
        [Toggle(_RAMP_ON)] _UseRamp ("Use Light Ramp", Float) = 0
        _RampMap ("Light Ramp (X = NdotL)", 2D) = "white" {}
        [ToggleUI] _ReceiveShadows ("Receive Shadows", Float) = 1
        _MainLightBoost ("Main Light Boost", Range(0, 3)) = 1
        // Диапазон поднят с 0..2 до 0..40. Двойки физически не хватало:
        // при затухании 1/36 на шести метрах она давала 5% яркости.
        _AdditionalLightStrength ("Доп. свет: усиление", Range(0, 40)) = 12
        _AdditionalLightFocus ("Доп. свет: концентрация пятна", Range(0.5, 4)) = 1.4

        [Header(Ambient and Fill)]
        _IndirectStrength ("Ambient (SH) Strength", Range(0, 2)) = 1
        _AmbientTint ("Ambient Tint", Color) = (1,1,1,1)
        _TopLightColor ("Top Light Color", Color) = (1, 0.97, 0.92, 1)
        _TopLightStrength ("Top Light Strength", Range(0, 1)) = 0.1
        _BounceColor ("Bounce Light Color (from below)", Color) = (0.6, 0.6, 0.7, 1)
        _BounceStrength ("Bounce Strength", Range(0, 1)) = 0.05

        [Header(Toon Specular)]
        [ToggleUI] _SpecularOn ("Enable Specular", Float) = 1
        _SpecColor2 ("Specular Color", Color) = (1, 1, 1, 1)
        _SpecSize ("Specular Size", Range(0, 1)) = 0.55
        _SpecSoftness ("Specular Edge Softness", Range(0.001, 0.5)) = 0.05
        _SpecIntensity ("Specular Intensity", Range(0, 3)) = 0.35
        _SheenColor ("Wide Sheen Color", Color) = (1, 1, 1, 1)
        _SheenSize ("Wide Sheen Size", Range(0, 1)) = 0.85
        _SheenIntensity ("Wide Sheen Intensity", Range(0, 2)) = 0.08
        _SpecAdditional ("Spec From Additional Lights", Range(0, 1)) = 0.6

        [Header(Rim Light)]
        [ToggleUI] _RimOn ("Enable Rim", Float) = 1
        _RimColor ("Rim Color", Color) = (1, 0.95, 0.85, 1)
        _RimPower ("Rim Power", Range(0.5, 10)) = 3
        _RimThreshold ("Rim Threshold", Range(0, 1)) = 0.35
        _RimSoftness ("Rim Edge Softness", Range(0.001, 0.5)) = 0.08
        _RimIntensity ("Rim Intensity", Range(0, 3)) = 0.25
        _RimLightMask ("Rim Only On Lit Side", Range(0, 1)) = 0.6

        [Header(Environment Reflection   toon metal)]
        [Toggle(_ENV_ON)] _EnvOn ("Enable Env Reflection", Float) = 0
        [Enum(ProceduralSky,0,Cubemap,1)] _EnvMode ("Env Mode", Float) = 0
        _EnvCube ("Env Cubemap", Cube) = "" {}
        _EnvSkyColor ("Env Sky", Color) = (0.65, 0.8, 1.0, 1)
        _EnvHorizonColor ("Env Horizon", Color) = (1.0, 0.95, 0.85, 1)
        _EnvGroundColor ("Env Ground", Color) = (0.28, 0.26, 0.24, 1)
        _EnvSteps ("Env Posterize Steps (0 = off)", Range(0, 12)) = 3
        _EnvBlur ("Env Blur (cubemap mip)", Range(0, 1)) = 0.25
        _EnvStrength ("Env Strength", Range(0, 3)) = 0.7
        _EnvFresnel ("Env Fresnel", Range(0, 8)) = 2
        _EnvMetalMask ("Env Follows Metallic", Range(0, 1)) = 1

        [Header(Matcap)]
        [Toggle(_MATCAP_ON)] _MatcapOn ("Enable Matcap", Float) = 0
        _MatcapMap ("Matcap", 2D) = "black" {}
        _MatcapColor ("Matcap Tint", Color) = (1,1,1,1)
        _MatcapStrength ("Matcap Strength", Range(0, 3)) = 0.7
        [Enum(Add,0,Multiply,1,Screen,2)] _MatcapBlend ("Matcap Blend", Float) = 0
        _MatcapFresnel ("Matcap Fresnel Mask", Range(0, 1)) = 0

        [Header(Grime and Dust)]
        [ToggleUI] _GrimeOn ("Enable Grime", Float) = 0
        _GrimeColor ("Grime Color", Color) = (0.22, 0.19, 0.16, 1)
        _GrimeStrength ("Grime In Cavities", Range(0, 1)) = 0.5
        _GrimeThreshold ("Grime Threshold", Range(0, 1)) = 0.5
        _GrimeSoftness ("Grime Softness", Range(0.001, 0.5)) = 0.25
        _DustColor ("Dust Color (top facing)", Color) = (0.72, 0.68, 0.58, 1)
        _DustStrength ("Dust Strength", Range(0, 1)) = 0
        _DustSharpness ("Dust Sharpness", Range(0.5, 8)) = 2

        [Header(Edge Wear)]
        [ToggleUI] _WearOn ("Enable Edge Wear", Float) = 0
        _WearColor ("Wear Color", Color) = (0.85, 0.83, 0.8, 1)
        _WearPower ("Wear Power", Range(0.5, 10)) = 4
        _WearAmount ("Wear Amount", Range(0, 1)) = 0.35
        _WearMetal ("Wear Adds Metallic", Range(0, 1)) = 0.5

        [Header(Emission)]
        _EmissionMap ("Emission Map", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        _EmissionPulseSpeed ("Emission Pulse Speed (0 = off)", Range(0, 12)) = 0
        _EmissionPulseMin ("Emission Pulse Min", Range(0, 1)) = 0.4

        [Header(Vertex Wobble)]
        _JiggleAmp ("Wobble Amplitude", Range(0, 0.5)) = 0
        _JiggleFreq ("Wobble Frequency", Range(0, 20)) = 4
        _JiggleSpeed ("Wobble Speed", Range(0, 20)) = 6
        _JiggleBase ("Wobble Base Height (OS Y)", Range(-5, 5)) = 0
        _JiggleFalloff ("Wobble Height Falloff", Range(0.01, 5)) = 1

        [Header(Outline)]
        _OutlineColor ("Outline Color", Color) = (0.12, 0.08, 0.1, 1)
        _OutlineWidth ("Outline Width", Range(0, 8)) = 1.0
        [Enum(Pixels,0,World,1)] _OutlineMode ("Outline Width Mode", Float) = 0
        [Enum(MeshNormal,0,VertexColorRGB,1,ObjectPosition,2)] _OutlineNormalSource ("Outline Normal Source", Float) = 0
        _OutlineTextureBlend ("Outline Tint By Base Map", Range(0, 1)) = 0.8
        _OutlineFadeStart ("Outline Fade Start (m)", Float) = 25
        _OutlineFadeEnd ("Outline Fade End (m)", Float) = 60
        _OutlineZBias ("Outline Z Bias", Range(0, 0.01)) = 0
        _OutlineWobbleAmp ("Outline Wobble (hand drawn)", Range(0, 1)) = 0
        _OutlineWobbleSpeed ("Outline Wobble Speed", Range(0, 30)) = 8
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }

        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half   _VertexColorTint;
            half   _Cutoff;

            half   _BumpScale;
            half   _MetallicStrength;
            half   _MetallicDiffuseDim;
            half   _OcclusionStrength;
            half   _OcclusionDirect;

            half   _Exposure;
            half   _Brightness;
            half   _Gamma;
            half   _Contrast;
            half   _ContrastPivot;
            half   _Saturation;
            half   _Vibrance;
            half   _HueShift;
            half   _WBTemp;
            half   _WBTint;
            half4  _ColorFilter;
            half4  _ShadowsColor;
            half4  _HighlightsColor;
            half   _ToneSplit;
            half   _GradeFinal;

            half   _ShadowThreshold;
            half   _ShadowSoftness;
            half4  _ShadowTint;
            half   _LightWrap;
            half   _ShadowSaturation;
            half4  _TerminatorColor;
            half   _TerminatorStrength;
            half   _TerminatorWidth;
            half   _ExtraBands;
            half   _ReceiveShadows;
            half   _MainLightBoost;
            half   _AdditionalLightStrength;
            half   _AdditionalLightFocus;

            half   _IndirectStrength;
            half4  _AmbientTint;
            half4  _TopLightColor;
            half   _TopLightStrength;
            half4  _BounceColor;
            half   _BounceStrength;

            half   _SpecularOn;
            half4  _SpecColor2;
            half   _SpecSize;
            half   _SpecSoftness;
            half   _SpecIntensity;
            half4  _SheenColor;
            half   _SheenSize;
            half   _SheenIntensity;
            half   _SpecAdditional;

            half   _RimOn;
            half4  _RimColor;
            half   _RimPower;
            half   _RimThreshold;
            half   _RimSoftness;
            half   _RimIntensity;
            half   _RimLightMask;

            half   _EnvMode;
            half4  _EnvSkyColor;
            half4  _EnvHorizonColor;
            half4  _EnvGroundColor;
            half   _EnvSteps;
            half   _EnvBlur;
            half   _EnvStrength;
            half   _EnvFresnel;
            half   _EnvMetalMask;

            half4  _MatcapColor;
            half   _MatcapStrength;
            half   _MatcapBlend;
            half   _MatcapFresnel;

            half   _GrimeOn;
            half4  _GrimeColor;
            half   _GrimeStrength;
            half   _GrimeThreshold;
            half   _GrimeSoftness;
            half4  _DustColor;
            half   _DustStrength;
            half   _DustSharpness;

            half   _WearOn;
            half4  _WearColor;
            half   _WearPower;
            half   _WearAmount;
            half   _WearMetal;

            half4  _EmissionColor;
            half   _EmissionPulseSpeed;
            half   _EmissionPulseMin;

            half   _JiggleAmp;
            half   _JiggleFreq;
            half   _JiggleSpeed;
            half   _JiggleBase;
            half   _JiggleFalloff;

            half4  _OutlineColor;
            half   _OutlineWidth;
            half   _OutlineMode;
            half   _OutlineNormalSource;
            half   _OutlineTextureBlend;
            float  _OutlineFadeStart;
            float  _OutlineFadeEnd;
            half   _OutlineZBias;
            half   _OutlineWobbleAmp;
            half   _OutlineWobbleSpeed;
        CBUFFER_END

        TEXTURE2D(_BaseMap);      SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);      SAMPLER(sampler_BumpMap);
        TEXTURE2D(_MetallicMap);  SAMPLER(sampler_MetallicMap);
        TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
        TEXTURE2D(_EmissionMap);  SAMPLER(sampler_EmissionMap);
        TEXTURE2D(_RampMap);      SAMPLER(sampler_RampMap);
        TEXTURE2D(_MatcapMap);    SAMPLER(sampler_MatcapMap);
        TEXTURECUBE(_EnvCube);    SAMPLER(sampler_EnvCube);

        half CC_Luma(half3 c)
        {
            return dot(c, half3(0.2126h, 0.7152h, 0.0722h));
        }

        half3 CC_RgbToHsv(half3 c)
        {
            half4 K = half4(0.0h, -1.0h / 3.0h, 2.0h / 3.0h, -1.0h);
            half4 p = lerp(half4(c.bg, K.wz), half4(c.gb, K.xy), step(c.b, c.g));
            half4 q = lerp(half4(p.xyw, c.r), half4(c.r, p.yzx), step(p.x, c.r));
            half d = q.x - min(q.w, q.y);
            half e = 1.0e-4h;
            return half3(abs(q.z + (q.w - q.y) / (6.0h * d + e)), d / (q.x + e), q.x);
        }

        half3 CC_HsvToRgb(half3 c)
        {
            half4 K = half4(1.0h, 2.0h / 3.0h, 1.0h / 3.0h, 3.0h);
            half3 p = abs(frac(c.xxx + K.xyz) * 6.0h - K.www);
            return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
        }

        half3 CC_Grade(half3 c)
        {
            c = max(c, 0.0h) * _Exposure;

            half3 wb = half3(1.0h + _WBTemp * 0.35h,
                             1.0h + _WBTint * 0.20h,
                             1.0h - _WBTemp * 0.35h);
            c *= wb;

            c = pow(max(c, 1.0e-5h), _Gamma);
            c = (c - _ContrastPivot) * _Contrast + _ContrastPivot;
            c = max(c, 0.0h);

            half l = CC_Luma(c);
            c = lerp(l.xxx, c, _Saturation);

            half mx = max(c.r, max(c.g, c.b));
            half mn = min(c.r, min(c.g, c.b));
            half sat = saturate(mx - mn);
            c = lerp(CC_Luma(c).xxx, c, 1.0h + _Vibrance * (1.0h - sat));

            if (abs(_HueShift) > 0.0005h)
            {
                half3 hsv = CC_RgbToHsv(max(c, 0.0h));
                hsv.x = frac(hsv.x + _HueShift + 1.0h);
                c = CC_HsvToRgb(hsv);
            }

            c *= _ColorFilter.rgb * _Brightness;

            half lm = saturate(CC_Luma(c));
            half split = smoothstep(saturate(_ToneSplit - 0.3h), saturate(_ToneSplit + 0.3h), lm);
            c *= lerp(_ShadowsColor.rgb, _HighlightsColor.rgb, split);

            return max(c, 0.0h);
        }

        float3 CC_VertexAnim(float3 positionOS, float3 normalOS)
        {
            if (_JiggleAmp <= 0.0001h)
                return positionOS;

            float3 objPos = TransformObjectToWorld(float3(0, 0, 0));
            float phase = _Time.y * _JiggleSpeed
                        + (objPos.x + objPos.z) * 1.7
                        + (positionOS.x + positionOS.z) * _JiggleFreq;

            float wave = sin(phase) * 0.6 + sin(phase * 1.73 + 1.3) * 0.4;
            float mask = saturate((positionOS.y - _JiggleBase) * _JiggleFalloff);

            return positionOS + normalOS * wave * _JiggleAmp * mask;
        }

        half3 CC_SampleEnv(half3 reflDir)
        {
            #if defined(_ENV_ON)
            if (_EnvMode >= 0.5h)
            {
                return SAMPLE_TEXTURECUBE_LOD(_EnvCube, sampler_EnvCube, reflDir, _EnvBlur * 6.0h).rgb;
            }
            #endif

            half y = reflDir.y;
            if (_EnvSteps >= 1.0h)
            {
                half s = floor(_EnvSteps + 0.5h);
                y = floor(y * s) / s;
            }
            half3 c = lerp(_EnvGroundColor.rgb, _EnvHorizonColor.rgb, smoothstep(-0.45h, 0.0h, y));
            c = lerp(c, _EnvSkyColor.rgb, smoothstep(0.0h, 0.45h, y));
            return c;
        }
        ENDHLSL

        // ==================================================================
        //  FORWARD LIT
        // ==================================================================
        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _RAMP_ON
            #pragma shader_feature_local_fragment _MATCAP_ON
            #pragma shader_feature_local_fragment _ENV_ON

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #ifndef LIGHT_LOOP_BEGIN
            #define LIGHT_LOOP_BEGIN(count) for (uint lightIndex = 0u; lightIndex < count; ++lightIndex) {
            #define LIGHT_LOOP_END }
            #endif

            // ================================================================
            //  ФОРМИРОВАНИЕ ЗАТУХАНИЯ ДОП. ИСТОЧНИКОВ
            //
            //  Точечный и конусный свет затухают по ОБРАТНОМУ КВАДРАТУ. У этой
            //  кривой узкое яркое ядро и очень длинный слабый хвост: на шести
            //  метрах остаётся 1/36 яркости, на двенадцати — 1/144. Поэтому
            //  «просто умножить на силу» не работает: чтобы вытянуть хвост до
            //  видимого уровня, ядро приходится задирать до четырёхзначных
            //  чисел, и оно уходит в белое пятно.
            //
            //  Здесь кривая ФОРМИРУЕТСЯ:
            //    усиление  — поднимает весь хвост до рабочего уровня;
            //    saturate  — срезает пересвет, ядро становится плоским;
            //    концентрация — задаёт резкость края получившегося пятна.
            //
            //  Итог: широкое ровное пятно с внятной границей вместо точки
            //  света с невидимым ореолом. Концентрация 1 даёт мягкий размытый
            //  круг, 2.5 — чёткий с обрывом.
            // ================================================================
            half CC_ShapeAtten(half atten)
            {
                return pow(saturate(atten * _AdditionalLightStrength),
                           max(0.5h, _AdditionalLightFocus));
            }


            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 tangentWS  : TEXCOORD3;
                float3 positionOS : TEXCOORD4;
                float4 color      : TEXCOORD5;
                half   fogFactor  : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 posOS = CC_VertexAnim(IN.positionOS.xyz, IN.normalOS);

                VertexPositionInputs pos = GetVertexPositionInputs(posOS);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalWS   = nrm.normalWS;
                OUT.tangentWS  = float4(nrm.tangentWS, IN.tangentOS.w * GetOddNegativeScale());
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.color      = IN.color;
                OUT.fogFactor  = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            // ndl — СЫРОЙ dot(N, L) в диапазоне -1..1 (не обрезанный), иначе wrap не работает.
            half CC_Wrap(half ndl)
            {
                return saturate((ndl + _LightWrap) / (1.0h + _LightWrap));
            }

            half CC_ToonStep(half d)
            {
                half lit = smoothstep(_ShadowThreshold - _ShadowSoftness,
                                      _ShadowThreshold + _ShadowSoftness, d);
                if (_ExtraBands >= 0.999h)
                {
                    half bands = floor(_ExtraBands + 0.5h);
                    half b = saturate(floor(saturate(d) * (bands + 1.0h)) / bands);
                    lit = min(lit, b);
                }
                return lit;
            }

            half3 CC_LightRamp(half d, out half litOut)
            {
                #if defined(_RAMP_ON)
                    half3 ramp = SAMPLE_TEXTURE2D(_RampMap, sampler_RampMap, float2(saturate(d), 0.5)).rgb;
                    litOut = CC_Luma(ramp);
                    return ramp * lerp(_ShadowTint.rgb, half3(1,1,1), litOut);
                #else
                    half lit = CC_ToonStep(d);
                    litOut = lit;
                    return lerp(_ShadowTint.rgb, half3(1,1,1), lit);
                #endif
            }

            half4 Frag(Varyings IN, FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                // ---------------- карты ----------------
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                baseTex.rgb = lerp(baseTex.rgb, baseTex.rgb * IN.color.rgb, _VertexColorTint);

                #if defined(_ALPHATEST_ON)
                    clip(baseTex.a - _Cutoff);
                #endif

                half metallic  = SAMPLE_TEXTURE2D(_MetallicMap, sampler_MetallicMap, IN.uv).r * _MetallicStrength;
                half aoTex     = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, IN.uv).g;
                half occlusion = lerp(1.0h, aoTex, _OcclusionStrength);

                // ---------------- нормаль ----------------
                float sgn = IN.tangentWS.w;
                float3 bitangent = sgn * cross(IN.normalWS, IN.tangentWS.xyz);
                half3 normalWS;
                #if defined(_NORMALMAP)
                    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                    normalWS = normalize(TransformTangentToWorld(normalTS,
                                   half3x3(IN.tangentWS.xyz, bitangent, IN.normalWS)));
                #else
                    normalWS = normalize(IN.normalWS);
                #endif

                half facing = IS_FRONT_VFACE(cullFace, 1.0h, -1.0h);
                normalWS *= facing;

                half3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                half  fresnel   = 1.0h - saturate(dot(normalWS, viewDirWS));

                // ---------------- альбедо + грейдинг ----------------
                half3 albedo = baseTex.rgb;
                if (_GradeFinal < 0.5h)
                    albedo = CC_Grade(albedo);

                // грязь в полостях + пыль сверху
                if (_GrimeOn > 0.5h)
                {
                    half cav = smoothstep(_GrimeThreshold - _GrimeSoftness,
                                          _GrimeThreshold + _GrimeSoftness, 1.0h - aoTex);
                    albedo = lerp(albedo, _GrimeColor.rgb, cav * _GrimeStrength);

                    half up = pow(saturate(normalWS.y), _DustSharpness);
                    albedo = lerp(albedo, _DustColor.rgb, up * _DustStrength);
                }

                // потёртые рёбра (краска слезла — виден металл)
                if (_WearOn > 0.5h)
                {
                    half wear = pow(fresnel, _WearPower) * _WearAmount;
                    albedo = lerp(albedo, _WearColor.rgb, wear);
                    metallic = saturate(metallic + wear * _WearMetal);
                }

                // ---------------- SSAO ----------------
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    float2 ssUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                    AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(ssUV);
                    occlusion = min(occlusion, aoFactor.indirectAmbientOcclusion);
                #endif

                half directOcclusion = lerp(1.0h, occlusion, _OcclusionDirect);

                // металл гасит диффуз
                half3 diffAlbedo = albedo * lerp(1.0h, 1.0h - _MetallicDiffuseDim, metallic);

                // ---------------- главный свет ----------------
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half shadowAtten = lerp(1.0h, mainLight.shadowAttenuation, _ReceiveShadows);

                half ndl   = CC_Wrap(dot(normalWS, mainLight.direction));
                half mainD = ndl * shadowAtten;

                half litMain;
                half3 rampCol = CC_LightRamp(mainD, litMain);
                half3 mainDiffuse = rampCol * mainLight.color * _MainLightBoost * directOcclusion;

                // ---------------- ambient / fill ----------------
                half3 ambient = SampleSH(normalWS) * _IndirectStrength * _AmbientTint.rgb;
                half3 topLight = saturate(normalWS.y) * _TopLightColor.rgb * _TopLightStrength;
                half3 bounce   = saturate(-normalWS.y) * _BounceColor.rgb * _BounceStrength;

                // ---------------- доп. источники ----------------
                half3 addLight = half3(0, 0, 0);
                half3 addSpec  = half3(0, 0, 0);
                #if defined(_ADDITIONAL_LIGHTS) || defined(USE_FORWARD_PLUS)
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = IN.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light l = GetAdditionalLight(lightIndex, IN.positionWS, half4(1, 1, 1, 1));
                        half lShadow = lerp(1.0h, l.shadowAttenuation, _ReceiveShadows);
                        half ld = CC_Wrap(dot(normalWS, l.direction)) * lShadow;

                        half litAdd;
                        half3 lRamp = CC_LightRamp(ld, litAdd);
                        half att = CC_ShapeAtten(l.distanceAttenuation);
                        addLight += lRamp * l.color * att;

                        if (_SpecularOn > 0.5h && _SpecAdditional > 0.001h)
                        {
                            half3 hL = normalize(l.direction + viewDirWS);
                            half ndhL = saturate(dot(normalWS, hL));
                            half sL = pow(ndhL, exp2((1.0h - _SpecSize) * 9.0h + 1.0h));
                            sL = smoothstep(0.5h - _SpecSoftness, 0.5h + _SpecSoftness, sL);
                            addSpec += sL * _SpecColor2.rgb * l.color * att
                                     * _SpecIntensity * _SpecAdditional * (1.0h + metallic * 2.0h) * litAdd;
                        }
                    LIGHT_LOOP_END
                }
                #endif

                // ---------------- сборка ----------------
                // Цветные тени: в тени альбедо насыщеннее (в анимации тень — это цвет, а не серый).
                half3 shadowAlbedo = max(lerp(CC_Luma(diffAlbedo).xxx, diffAlbedo, 1.0h + _ShadowSaturation), 0.0h);
                half3 shadedAlbedo = lerp(shadowAlbedo, diffAlbedo, litMain);
                half3 color = shadedAlbedo * (mainDiffuse + addLight + (ambient + topLight + bounce) * occlusion);

                // Тёплая кромка на границе света и тени (терминатор).
                if (_TerminatorStrength > 0.001h)
                {
                    half term = saturate(1.0h - abs(mainD - _ShadowThreshold) / max(_TerminatorWidth, 0.001h));
                    term *= term;
                    color += diffAlbedo * _TerminatorColor.rgb * (term * _TerminatorStrength)
                           * mainLight.color * _MainLightBoost * directOcclusion;
                }

                // ---------------- спекуляр ----------------
                if (_SpecularOn > 0.5h)
                {
                    half3 H = normalize(mainLight.direction + viewDirWS);
                    half ndh = saturate(dot(normalWS, H));

                    half specRaw = pow(ndh, exp2((1.0h - _SpecSize) * 9.0h + 1.0h));
                    half spec = smoothstep(0.5h - _SpecSoftness, 0.5h + _SpecSoftness, specRaw);
                    half3 specTint = lerp(_SpecColor2.rgb, _SpecColor2.rgb * albedo * 2.0h, metallic);
                    half specBoost = 1.0h + metallic * 2.0h;
                    color += spec * specTint * _SpecIntensity * specBoost
                           * litMain * mainLight.color * directOcclusion;

                    // широкий отблеск — «полированный металл»
                    if (_SheenIntensity > 0.001h)
                    {
                        half sheenRaw = pow(ndh, exp2((1.0h - _SheenSize) * 6.0h + 0.5h));
                        half sheen = smoothstep(0.15h, 0.85h, sheenRaw);
                        color += sheen * _SheenColor.rgb * _SheenIntensity * (1.0h + metallic)
                               * litMain * mainLight.color * directOcclusion;
                    }

                    color += addSpec * directOcclusion;
                }

                // ---------------- rim ----------------
                if (_RimOn > 0.5h && _RimIntensity > 0.001h)
                {
                    half rimF = pow(fresnel, _RimPower);
                    half rim = smoothstep(_RimThreshold - _RimSoftness,
                                          _RimThreshold + _RimSoftness, rimF);
                    half rimMask = lerp(1.0h, litMain, _RimLightMask);
                    color += rim * rimMask * _RimColor.rgb * _RimIntensity * occlusion;
                }

                // ---------------- отражение окружения ----------------
                #if defined(_ENV_ON)
                {
                    half3 refl = reflect(-viewDirWS, normalWS);
                    half3 env = CC_SampleEnv(refl) * _EnvStrength;
                    half envMask = saturate(pow(fresnel, _EnvFresnel) + lerp(0.0h, metallic, _EnvMetalMask));
                    // на металле отражение окрашивается альбедо
                    env *= lerp(half3(1,1,1), albedo * 1.5h, metallic);
                    color += env * envMask * occlusion;
                }
                #endif

                // ---------------- matcap ----------------
                #if defined(_MATCAP_ON)
                {
                    half3 nVS = mul((float3x3)UNITY_MATRIX_V, normalWS);
                    float2 mUV = nVS.xy * 0.5h + 0.5h;
                    half3 mc = SAMPLE_TEXTURE2D(_MatcapMap, sampler_MatcapMap, mUV).rgb
                             * _MatcapColor.rgb * _MatcapStrength;
                    half mMask = lerp(1.0h, fresnel, _MatcapFresnel) * occlusion;

                    if (_MatcapBlend < 0.5h)
                        color += mc * mMask;
                    else if (_MatcapBlend < 1.5h)
                        color = lerp(color, color * mc, mMask);
                    else
                        color = lerp(color, 1.0h - (1.0h - color) * (1.0h - saturate(mc)), mMask);
                }
                #endif

                // ---------------- эмиссия ----------------
                half pulse = 1.0h;
                if (_EmissionPulseSpeed > 0.001h)
                    pulse = lerp(_EmissionPulseMin, 1.0h, sin(_Time.y * _EmissionPulseSpeed) * 0.5h + 0.5h);
                color += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, IN.uv).rgb
                       * _EmissionColor.rgb * pulse;

                // ---------------- финальный грейдинг ----------------
                if (_GradeFinal > 0.5h)
                    color = CC_Grade(color);

                color = MixFog(color, IN.fogFactor);
                return half4(color, baseTex.a);
            }
            ENDHLSL
        }

        // ==================================================================
        //  OUTLINE
        // ==================================================================
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite [_ZWrite]
            ZTest LEqual
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half   fogFactor  : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings OutlineVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

                float3 posOS = CC_VertexAnim(IN.positionOS.xyz, IN.normalOS);

                // ═══ НОРМАЛЬ ОБОЛОЧКИ ═══
                //
                // normalize нулевого вектора даёт NaN. Дальше NaN попадает в
                // позицию вершины, и треугольник растягивается на весь экран —
                // тот же чёрный клин, только по другой причине.
                //
                // Нулевой вектор здесь получается легко: серый вертексный цвет
                // (0.5, 0.5, 0.5) после разворота в минус-плюс даёт ровно ноль,
                // а вершина в самом центре модели даёт ноль в режиме
                // ObjectPosition. И то и другое встречается в обычных мешах.
                float3 nRaw;

                if (_OutlineNormalSource < 0.5)
                    nRaw = IN.normalOS;
                else if (_OutlineNormalSource < 1.5)
                    nRaw = IN.color.rgb * 2.0 - 1.0;
                else
                    nRaw = IN.positionOS.xyz;

                float nLen2 = dot(nRaw, nRaw);
                float3 nOS = (nLen2 > 1e-8) ? nRaw * rsqrt(nLen2) : float3(0.0, 1.0, 0.0);

                float width = _OutlineWidth;

                if (_OutlineWobbleAmp > 0.0001)
                {
                    float w = sin(_Time.y * _OutlineWobbleSpeed
                                + IN.positionOS.x * 13.0 + IN.positionOS.y * 7.3);
                    width *= 1.0 + w * _OutlineWobbleAmp;
                }

                float3 posWS = TransformObjectToWorld(posOS);
                float dist = distance(posWS, GetCameraPositionWS());
                float fadeEnd = max(_OutlineFadeEnd, _OutlineFadeStart + 0.01);
                // ЗАТУХАНИЕ. Долю запоминаем: она понадобится ещё раз ниже.
                float fade = saturate(1.0 - smoothstep(_OutlineFadeStart, fadeEnd, dist));
                width *= fade;

                if (_OutlineMode >= 0.5)
                    posOS += nOS * width * 0.01;

                float4 posCS = TransformObjectToHClip(posOS);

                if (_OutlineMode < 0.5)
                {
                    float3 nWS = TransformObjectToWorldNormal(nOS);
                    float3 nVS = TransformWorldToViewDir(nWS);
                    // Нормаль, смотрящая точно в камеру, даёт нулевую проекцию
                    // на экран. normalize от неё — снова NaN.
                    float2 nxy = nVS.xy;
                    float nxyLen2 = dot(nxy, nxy);
                    nxy = (nxyLen2 > 1e-8) ? nxy * rsqrt(nxyLen2) : float2(0.0, 0.0);

                    float2 offset = nxy * width * 2.0 / _ScreenParams.xy;
                    posCS.xy += offset * posCS.w;
                }

                // ═══ ПОГАСШИЙ КОНТУР ПРЯЧЕМ ГЛУБИНОЙ ═══
                //
                // Здесь раньше стояло выбрасывание вершины: если контур
                // погас по дальности, вершине присваивалось w = -1.
                //
                // Так делать нельзя, и это и есть источник чёрных полос.
                // Условие проверяется У КАЖДОЙ ВЕРШИНЫ ОТДЕЛЬНО, а дальность
                // у вершин одного треугольника разная. На длинном заборе или
                // вытянутой балке одна вершина оказывается за границей
                // затухания, а две другие нет — и треугольник получает одну
                // вершину с w = -1 и две нормальные.
                //
                // Растеризатор такой треугольник не выбрасывает. Он
                // интерполирует его ЧЕРЕЗ ПЛОСКОСТЬ w = 0, а там деление на w
                // уходит в бесконечность. Треугольник растягивается на
                // пол-экрана — теми самыми огромными чёрными клиньями цвета
                // контура, расходящимися от предмета.
                //
                // Поэтому гасим иначе: ширина плавно уходит в ноль (оболочка
                // ложится на саму модель), а сверху уводим её ЗА поверхность
                // по глубине. Тест глубины отсекает её сам, и ни одна вершина
                // никуда не улетает.
                float bias = _OutlineZBias - (1.0 - fade) * 0.02;

                #if UNITY_REVERSED_Z
                    posCS.z += bias * posCS.w;
                #else
                    posCS.z -= bias * posCS.w;
                #endif
                OUT.positionCS = posCS;
                OUT.fogFactor = ComputeFogFactor(posCS.z);
                return OUT;
            }

            half4 OutlineFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                #if defined(_ALPHATEST_ON)
                    clip(baseTex.a - _Cutoff);
                #endif

                // Контур = тёмная и более насыщенная версия цвета предмета (а не чёрная линия).
                half3 fromBase = baseTex.rgb * baseTex.rgb * 0.55h;
                half3 c = lerp(_OutlineColor.rgb, fromBase, _OutlineTextureBlend);

                c = MixFog(c, IN.fogFactor);
                return half4(c, baseTex.a);
            }
            ENDHLSL
        }

        // ==================================================================
        //  SHADOW CASTER
        // ==================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posOS = CC_VertexAnim(IN.positionOS.xyz, IN.normalOS);
                float3 positionWS = TransformObjectToWorld(posOS);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionCS = positionCS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 ShadowFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                #if defined(_ALPHATEST_ON)
                    half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _BaseColor.a;
                    clip(a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // ==================================================================
        //  DEPTH ONLY
        // ==================================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posOS = CC_VertexAnim(IN.positionOS.xyz, IN.normalOS);
                OUT.positionCS = TransformObjectToHClip(posOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 DepthFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                #if defined(_ALPHATEST_ON)
                    half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _BaseColor.a;
                    clip(a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // ==================================================================
        //  DEPTH NORMALS
        // ==================================================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 tangentWS  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthNormalsVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posOS = CC_VertexAnim(IN.positionOS.xyz, IN.normalOS);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionCS = TransformObjectToHClip(posOS);
                OUT.normalWS = nrm.normalWS;
                OUT.tangentWS = float4(nrm.tangentWS, IN.tangentOS.w * GetOddNegativeScale());
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 DepthNormalsFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                #if defined(_ALPHATEST_ON)
                    half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _BaseColor.a;
                    clip(a - _Cutoff);
                #endif

                float3 normalWS = normalize(IN.normalWS);
                #if defined(_NORMALMAP)
                    float sgn = IN.tangentWS.w;
                    float3 bitangent = sgn * cross(IN.normalWS, IN.tangentWS.xyz);
                    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                    normalWS = normalize(TransformTangentToWorld(normalTS,
                                   half3x3(IN.tangentWS.xyz, bitangent, IN.normalWS)));
                #endif

                return half4(normalWS, 0);
            }
            ENDHLSL
        }
    }

    FallBack Off
    CustomEditor "CatCooksShaderGUI"
}
