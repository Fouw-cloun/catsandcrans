// ============================================================================
//  CatCooks/Toon Props  —  стилизованный toon-шейдер для предметов (URP)
//  Карты: Base, Normal, Metallic (R), AO. ПК/консоли.
//  Возможности: ступенчатое toon-освещение с мягкостью и доп. полосами,
//  цветная тень, toon-спекуляр с металликом, rim-light, эмиссия,
//  контурная обводка (outline), поддержка доп. источников света и теней URP.
// ============================================================================
Shader "CatCooks/Toon Props"
{
    Properties
    {
        [Header(Base)]
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor ("Base Tint", Color) = (1,1,1,1)

        [Header(Surface Maps)]
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 2)) = 1
        _MetallicMap ("Metallic Map (R)", 2D) = "black" {}
        _MetallicStrength ("Metallic Strength", Range(0, 1)) = 1
        _MetallicDiffuseDim ("Metallic Darkens Diffuse", Range(0, 1)) = 0.55
        _OcclusionMap ("AO Map", 2D) = "white" {}
        _OcclusionStrength ("AO Strength", Range(0, 1)) = 1

        [Header(Toon Lighting)]
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.45
        _ShadowSoftness ("Shadow Softness", Range(0.001, 0.5)) = 0.06
        _ShadowTint ("Shadow Tint", Color) = (0.62, 0.62, 0.78, 1)
        _ExtraBands ("Extra Light Bands (0 = off)", Range(0, 4)) = 0
        _IndirectStrength ("Ambient (SH) Strength", Range(0, 2)) = 1

        [Header(Toon Specular)]
        [Toggle(_SPECULAR_ON)] _SpecularOn ("Enable Specular", Float) = 1
        _SpecColor2 ("Specular Color", Color) = (1, 1, 1, 1)
        _SpecSize ("Specular Size", Range(0, 1)) = 0.55
        _SpecSoftness ("Specular Edge Softness", Range(0.001, 0.5)) = 0.05
        _SpecIntensity ("Specular Intensity", Range(0, 3)) = 0.8

        [Header(Rim Light)]
        [Toggle(_RIM_ON)] _RimOn ("Enable Rim", Float) = 1
        _RimColor ("Rim Color", Color) = (1, 0.95, 0.85, 1)
        _RimPower ("Rim Power", Range(0.5, 10)) = 3
        _RimThreshold ("Rim Threshold", Range(0, 1)) = 0.35
        _RimSoftness ("Rim Edge Softness", Range(0.001, 0.5)) = 0.08
        _RimLightMask ("Rim Only On Lit Side", Range(0, 1)) = 0.6
        _RimIntensity ("Rim Intensity", Range(0, 3)) = 0.7

        [Header(Emission)]
        _EmissionMap ("Emission Map", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)

        [Header(Outline)]
        _OutlineColor ("Outline Color", Color) = (0.12, 0.08, 0.1, 1)
        _OutlineWidth ("Outline Width (px)", Range(0, 8)) = 1.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half   _BumpScale;
            half   _MetallicStrength;
            half   _MetallicDiffuseDim;
            half   _OcclusionStrength;
            half   _ShadowThreshold;
            half   _ShadowSoftness;
            half4  _ShadowTint;
            half   _ExtraBands;
            half   _IndirectStrength;
            half4  _SpecColor2;
            half   _SpecSize;
            half   _SpecSoftness;
            half   _SpecIntensity;
            half4  _RimColor;
            half   _RimPower;
            half   _RimThreshold;
            half   _RimSoftness;
            half   _RimLightMask;
            half   _RimIntensity;
            half4  _EmissionColor;
            half4  _OutlineColor;
            half   _OutlineWidth;
        CBUFFER_END

        TEXTURE2D(_BaseMap);      SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);      SAMPLER(sampler_BumpMap);
        TEXTURE2D(_MetallicMap);  SAMPLER(sampler_MetallicMap);
        TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
        TEXTURE2D(_EmissionMap);  SAMPLER(sampler_EmissionMap);
        ENDHLSL

        // ------------------------------------------------------------------
        // FORWARD LIT
        // ------------------------------------------------------------------
        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local _SPECULAR_ON
            #pragma shader_feature_local _RIM_ON

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #ifndef LIGHT_LOOP_BEGIN
            #define LIGHT_LOOP_BEGIN(count) for (uint lightIndex = 0u; lightIndex < count; ++lightIndex) {
            #define LIGHT_LOOP_END }
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 tangentWS  : TEXCOORD3; // w = sign
                half   fogFactor  : TEXCOORD4;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS   = nrm.normalWS;
                OUT.tangentWS  = float4(nrm.tangentWS, IN.tangentOS.w * GetOddNegativeScale());
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor  = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            // Ступенчатая toon-функция освещения
            half ToonStep(half d)
            {
                half lit = smoothstep(_ShadowThreshold - _ShadowSoftness,
                                      _ShadowThreshold + _ShadowSoftness, d);
                if (_ExtraBands >= 0.999)
                {
                    half bands = floor(_ExtraBands + 0.5);
                    half b = saturate(floor(saturate(d) * (bands + 1.0)) / bands);
                    lit = min(lit, b);
                }
                return lit;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // --- сэмплы карт ---
                half4 baseTex  = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                half3 albedo   = baseTex.rgb;
                half  metallic = SAMPLE_TEXTURE2D(_MetallicMap, sampler_MetallicMap, IN.uv).r * _MetallicStrength;
                half  aoTex    = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, IN.uv).g;
                half  occlusion = lerp(1.0h, aoTex, _OcclusionStrength);
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);

                // --- нормаль в мире ---
                float sgn = IN.tangentWS.w;
                float3 bitangent = sgn * cross(IN.normalWS, IN.tangentWS.xyz);
                half3 normalWS = normalize(TransformTangentToWorld(normalTS,
                                    half3x3(IN.tangentWS.xyz, bitangent, IN.normalWS)));
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // --- главный свет + тени ---
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndl = saturate(dot(normalWS, mainLight.direction));
                half mainD = ndl * mainLight.shadowAttenuation;
                half litMain = ToonStep(mainD);

                // металл слегка гасит диффуз
                half3 diffAlbedo = albedo * lerp(1.0h, 1.0h - _MetallicDiffuseDim, metallic);

                // тень — не чёрная, а тонированная
                half3 mainDiffuse = lerp(_ShadowTint.rgb, half3(1,1,1), litMain) * mainLight.color;

                // --- ambient (SH) ---
                half3 ambient = SampleSH(normalWS) * _IndirectStrength;

                // --- дополнительные источники ---
                half3 addLight = half3(0,0,0);
                #if defined(_ADDITIONAL_LIGHTS) || defined(USE_FORWARD_PLUS)
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light l = GetAdditionalLight(lightIndex, IN.positionWS, half4(1,1,1,1));
                    half d = saturate(dot(normalWS, l.direction)) * l.distanceAttenuation * l.shadowAttenuation;
                    addLight += ToonStep(d) * l.color;
                LIGHT_LOOP_END
                #endif

                half3 color = diffAlbedo * (mainDiffuse + addLight + ambient) * occlusion;

                // --- toon-спекуляр ---
                #if defined(_SPECULAR_ON)
                half3 H = normalize(mainLight.direction + viewDirWS);
                half ndh = saturate(dot(normalWS, H));
                half specRaw = pow(ndh, exp2((1.0h - _SpecSize) * 9.0h + 1.0h));
                half spec = smoothstep(0.5h - _SpecSoftness, 0.5h + _SpecSoftness, specRaw);
                half3 specTint = lerp(_SpecColor2.rgb, _SpecColor2.rgb * albedo * 2.0h, metallic);
                half specBoost = 1.0h + metallic * 2.0h;
                color += spec * specTint * _SpecIntensity * specBoost
                         * litMain * mainLight.color * occlusion;
                #endif

                // --- rim light ---
                #if defined(_RIM_ON)
                half fres = pow(1.0h - saturate(dot(normalWS, viewDirWS)), _RimPower);
                half rim = smoothstep(_RimThreshold - _RimSoftness, _RimThreshold + _RimSoftness, fres);
                half rimMask = lerp(1.0h, litMain, _RimLightMask);
                color += rim * rimMask * _RimColor.rgb * _RimIntensity * occlusion;
                #endif

                // --- эмиссия ---
                color += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, IN.uv).rgb * _EmissionColor.rgb;

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // OUTLINE (инвертированный «корпус», ширина в пикселях экрана)
        // ------------------------------------------------------------------
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half   fogFactor  : TEXCOORD0;
            };

            Varyings OutlineVert(Attributes IN)
            {
                Varyings OUT;
                float4 posCS = TransformObjectToHClip(IN.positionOS.xyz);

                if (_OutlineWidth > 0.001)
                {
                    float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                    float3 normalVS = TransformWorldToViewDir(normalWS);
                    float2 offsetNDC = normalize(normalVS.xy + 1e-5) * _OutlineWidth * 2.0 / _ScreenParams.xy;
                    posCS.xy += offsetNDC * posCS.w;
                }
                else
                {
                    posCS = float4(0, 0, 0, -1); // отбрасываем пасс
                }

                OUT.positionCS = posCS;
                OUT.fogFactor = ComputeFogFactor(posCS.z);
                return OUT;
            }

            half4 OutlineFrag(Varyings IN) : SV_Target
            {
                half3 c = MixFog(_OutlineColor.rgb, IN.fogFactor);
                return half4(c, 1.0h);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // SHADOW CASTER
        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
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
                return OUT;
            }

            half4 ShadowFrag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // DEPTH ONLY
        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings  { float4 positionCS : SV_POSITION; };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half DepthFrag(Varyings IN) : SV_Target { return IN.positionCS.z; }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // DEPTH NORMALS (для SSAO и декалей URP)
        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            Varyings DepthNormalsVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 DepthNormalsFrag(Varyings IN) : SV_Target
            {
                return half4(normalize(IN.normalWS), 0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
