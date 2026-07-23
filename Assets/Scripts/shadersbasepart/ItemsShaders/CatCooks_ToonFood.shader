// ============================================================================
//  CatCooks/Toon Food  —  «аппетитный» toon-шейдер для еды и ингредиентов (URP)
//  Всё из Toon Props ПЛЮС:
//   • Fake SSS (подповерхностное свечение) — тёплая «просветка» на краях,
//     как у спелых фруктов, теста, мяса. Толщину можно брать из AO.
//   • Двойной спекуляр: острый «мокрый» блик + широкий мягкий отблеск (sheen).
//   • Glaze — френелевый «влажный/глянцевый» ободок, эффект глазури и сока.
//   • Juice-грейдинг: насыщенность, яркость, тёплые тени.
//   • Top Light — мягкая фейковая подсветка сверху («свет витрины»).
// ============================================================================
Shader "CatCooks/Toon Food"
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
        _MetallicStrength ("Metallic Strength", Range(0, 1)) = 0.5
        _OcclusionMap ("AO Map", 2D) = "white" {}
        _OcclusionStrength ("AO Strength", Range(0, 1)) = 1

        [Header(Juice Color Grading)]
        _Saturation ("Saturation", Range(0, 2)) = 1.15
        _Brightness ("Brightness", Range(0.5, 1.6)) = 1.05
        _Gamma ("Contrast (Gamma)", Range(0.5, 1.6)) = 1.0

        [Header(Toon Lighting)]
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.42
        _ShadowSoftness ("Shadow Softness", Range(0.001, 0.5)) = 0.09
        _ShadowTint ("Shadow Tint (warm!)", Color) = (0.85, 0.58, 0.5, 1)
        _ExtraBands ("Extra Light Bands (0 = off)", Range(0, 4)) = 0
        _IndirectStrength ("Ambient (SH) Strength", Range(0, 2)) = 1

        [Header(Fake SSS Subsurface)]
        [Toggle(_SSS_ON)] _SssOn ("Enable SSS", Float) = 1
        _SSSColor ("SSS Color", Color) = (1.0, 0.45, 0.25, 1)
        _SSSStrength ("SSS Strength", Range(0, 3)) = 0.9
        _SSSDistortion ("SSS Normal Distortion", Range(0, 1)) = 0.4
        _SSSPower ("SSS Focus", Range(1, 16)) = 4
        _SSSAmbient ("SSS Ambient (everywhere)", Range(0, 1)) = 0.15
        [Toggle(_SSS_AO_THICKNESS)] _SssAoThickness ("Use AO As Thickness (thin = bright)", Float) = 0

        [Header(Wet Specular)]
        [Toggle(_SPECULAR_ON)] _SpecularOn ("Enable Specular", Float) = 1
        _SpecColor2 ("Hot Spot Color", Color) = (1, 1, 1, 1)
        _SpecSize ("Hot Spot Size", Range(0, 1)) = 0.25
        _SpecSoftness ("Hot Spot Edge Softness", Range(0.001, 0.5)) = 0.04
        _SpecIntensity ("Hot Spot Intensity", Range(0, 3)) = 1.1
        _SheenColor ("Soft Sheen Color", Color) = (1, 0.9, 0.75, 1)
        _SheenSize ("Soft Sheen Size", Range(0, 1)) = 0.8
        _SheenIntensity ("Soft Sheen Intensity", Range(0, 2)) = 0.35

        [Header(Glaze Fresnel Coat)]
        [Toggle(_GLAZE_ON)] _GlazeOn ("Enable Glaze", Float) = 1
        _GlazeColor ("Glaze Color", Color) = (1, 0.85, 0.7, 1)
        _GlazePower ("Glaze Power", Range(0.5, 10)) = 4
        _GlazeThreshold ("Glaze Threshold", Range(0, 1)) = 0.4
        _GlazeSoftness ("Glaze Edge Softness", Range(0.001, 0.5)) = 0.12
        _GlazeIntensity ("Glaze Intensity", Range(0, 3)) = 0.6
        _GlazeLightMask ("Glaze Only On Lit Side", Range(0, 1)) = 0.5

        [Header(Top Light Showcase)]
        _TopLightColor ("Top Light Color", Color) = (1, 0.96, 0.88, 1)
        _TopLightStrength ("Top Light Strength", Range(0, 1)) = 0.18

        [Header(Emission)]
        _EmissionMap ("Emission Map", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)

        [Header(Outline)]
        _OutlineColor ("Outline Color", Color) = (0.25, 0.1, 0.08, 1)
        _OutlineWidth ("Outline Width (px)", Range(0, 8)) = 1.2
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
            half   _OcclusionStrength;
            half   _Saturation;
            half   _Brightness;
            half   _Gamma;
            half   _ShadowThreshold;
            half   _ShadowSoftness;
            half4  _ShadowTint;
            half   _ExtraBands;
            half   _IndirectStrength;
            half4  _SSSColor;
            half   _SSSStrength;
            half   _SSSDistortion;
            half   _SSSPower;
            half   _SSSAmbient;
            half4  _SpecColor2;
            half   _SpecSize;
            half   _SpecSoftness;
            half   _SpecIntensity;
            half4  _SheenColor;
            half   _SheenSize;
            half   _SheenIntensity;
            half4  _GlazeColor;
            half   _GlazePower;
            half   _GlazeThreshold;
            half   _GlazeSoftness;
            half   _GlazeIntensity;
            half   _GlazeLightMask;
            half4  _TopLightColor;
            half   _TopLightStrength;
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
            Name "ToonFoodForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local _SPECULAR_ON
            #pragma shader_feature_local _SSS_ON
            #pragma shader_feature_local _SSS_AO_THICKNESS
            #pragma shader_feature_local _GLAZE_ON

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
                float4 tangentWS  : TEXCOORD3;
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

            // «Сочный» грейдинг альбедо
            half3 Juice(half3 c)
            {
                c = pow(abs(c), _Gamma);                       // контраст
                half lum = dot(c, half3(0.299h, 0.587h, 0.114h));
                c = lerp(lum.xxx, c, _Saturation);             // насыщенность
                return saturate(c * _Brightness);              // яркость
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // --- карты ---
                half4 baseTex  = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                half3 albedo   = Juice(baseTex.rgb);
                half  metallic = SAMPLE_TEXTURE2D(_MetallicMap, sampler_MetallicMap, IN.uv).r * _MetallicStrength;
                half  aoTex    = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, IN.uv).g;
                half  occlusion = lerp(1.0h, aoTex, _OcclusionStrength);
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);

                float sgn = IN.tangentWS.w;
                float3 bitangent = sgn * cross(IN.normalWS, IN.tangentWS.xyz);
                half3 normalWS = normalize(TransformTangentToWorld(normalTS,
                                    half3x3(IN.tangentWS.xyz, bitangent, IN.normalWS)));
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // --- главный свет ---
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndl = saturate(dot(normalWS, mainLight.direction));
                half mainD = ndl * mainLight.shadowAttenuation;
                half litMain = ToonStep(mainD);

                // тёплая тень = аппетит
                half3 mainDiffuse = lerp(_ShadowTint.rgb, half3(1,1,1), litMain) * mainLight.color;

                half3 ambient = SampleSH(normalWS) * _IndirectStrength;

                // фейковый свет «витрины» сверху
                half topMask = saturate(normalWS.y);
                half3 topLight = topMask * _TopLightColor.rgb * _TopLightStrength;

                // --- доп. источники ---
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

                half3 color = albedo * (mainDiffuse + addLight + ambient + topLight) * occlusion;

                // --- Fake SSS: тёплая просветка ---
                #if defined(_SSS_ON)
                half thickness = 1.0h;
                #if defined(_SSS_AO_THICKNESS)
                    thickness = 1.0h - aoTex; // тонкие/углублённые места «просвечивают»
                    thickness = saturate(thickness * 1.5h);
                #endif
                half3 sssH = normalize(mainLight.direction + normalWS * _SSSDistortion);
                half sssDot = pow(saturate(dot(viewDirWS, -sssH)), _SSSPower);
                half fresBack = pow(1.0h - saturate(dot(normalWS, viewDirWS)), 2.0h);
                half sss = (sssDot + fresBack * _SSSAmbient) * _SSSStrength * thickness;
                color += sss * _SSSColor.rgb * albedo * mainLight.color;
                #endif

                // --- двойной «мокрый» спекуляр ---
                #if defined(_SPECULAR_ON)
                half3 H = normalize(mainLight.direction + viewDirWS);
                half ndh = saturate(dot(normalWS, H));

                // 1) острый горячий блик — «капля света»
                half hotRaw = pow(ndh, exp2((1.0h - _SpecSize) * 9.0h + 2.0h));
                half hot = smoothstep(0.5h - _SpecSoftness, 0.5h + _SpecSoftness, hotRaw);
                half3 hotTint = lerp(_SpecColor2.rgb, _SpecColor2.rgb * albedo * 2.0h, metallic * 0.5h);
                color += hot * hotTint * _SpecIntensity * (1.0h + metallic)
                         * litMain * mainLight.color * occlusion;

                // 2) широкий мягкий отблеск — «свежесть»
                half sheenRaw = pow(ndh, exp2((1.0h - _SheenSize) * 6.0h + 0.5h));
                half sheen = smoothstep(0.2h, 0.8h, sheenRaw);
                color += sheen * _SheenColor.rgb * _SheenIntensity
                         * litMain * mainLight.color * occlusion;
                #endif

                // --- глазурь / влажный ободок ---
                #if defined(_GLAZE_ON)
                half fres = pow(1.0h - saturate(dot(normalWS, viewDirWS)), _GlazePower);
                half glaze = smoothstep(_GlazeThreshold - _GlazeSoftness,
                                        _GlazeThreshold + _GlazeSoftness, fres);
                half glazeMask = lerp(1.0h, litMain, _GlazeLightMask);
                color += glaze * glazeMask * _GlazeColor.rgb * _GlazeIntensity * occlusion;
                #endif

                // --- эмиссия ---
                color += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, IN.uv).rgb * _EmissionColor.rgb;

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // OUTLINE
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
                    posCS = float4(0, 0, 0, -1);
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
        // DEPTH NORMALS
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
