// ============================================================================
//  CatCooks/Toon Mountain   —   Unity 6 (URP 17)
//
//  Простой мультяшный шейдер для гор с готовой текстурой и развёрткой.
//  Текстура показывается как есть, без перекраски.
//
//  Что делает:
//   • Toon-свет: ступенчатые тени со светлым «полом» и цветным оттенком
//   • Контровой свет по силуэту — отделяет гряды друг от друга
//   • Воздушная перспектива — дальние горы тают в цвете тумана
//   • Обводка inverted hull, работает на любой дистанции
//   • Тени от облаков через light cookie (SkyController)
//
//  Цвет дымки и оттенок теней может вести CatCooksMountainHaze.cs
//  вслед за пресетами погоды.
// ============================================================================
Shader "CatCooks/Toon Mountain"
{
    Properties
    {
        [Header(Textures)]
        _BaseMap ("Base Color", 2D) = "white" {}
        _BaseTint ("Оттенок (белый = как есть)", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Сила нормалей", Range(0,3)) = 0.7
        _MaskMap ("Mask (G = AO, A = smoothness)", 2D) = "white" {}
        _MaskAO ("Влияние AO", Range(0,1)) = 0.5
        _SpecStrength ("Блик", Range(0,2)) = 0.25

        [Header(Toon Lighting)]
        _ShadeSteps     ("Ступеней света", Range(1, 6)) = 3
        _ShadeFloor     ("Пол тени (яркость)", Range(0, 1)) = 0.58
        _LightWrap      ("Заворот света", Range(0, 1)) = 0.45
        _ShadowTint     ("Оттенок тени", Color) = (0.60, 0.70, 0.95, 1)
        _ShadowStrength ("Сила падающих теней", Range(0, 1)) = 0.5
        _AmbientBoost   ("Ambient", Range(0, 3)) = 1.3
        _LightBoost     ("Прямой свет", Range(0, 3)) = 1.1

        [Header(Rim Light)]
        _RimColor    ("Цвет контрового", Color) = (1, 0.93, 0.75, 1)
        _RimPower    ("Резкость контрового", Range(0.5, 16)) = 4
        _RimStrength ("Сила контрового", Range(0, 3)) = 0.8

        [Header(Aerial Perspective)]
        _HazeColor ("Цвет дымки", Color) = (0.78, 0.82, 0.86, 1)
        _HazeStart ("Дымка от (м)", Float) = 150
        _HazeEnd   ("Полная дымка (м)", Float) = 900
        _HazeMax   ("Максимум дымки", Range(0, 1)) = 0.75

        [Header(Outline)]
        _OutlineColor ("Цвет обводки", Color) = (0.18, 0.16, 0.20, 1)
        _OutlineWidth ("Ширина обводки", Range(0, 12)) = 2.2
        _OutlineFadeStart ("Обводка тоньше от (м)", Float) = 400
        _OutlineFadeEnd   ("Обводка исчезает (м)", Float) = 1400
        [Toggle(_OUTLINE_FROM_POSITION)] _OutlineFromPos ("Раздувать по позиции", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _BumpMap_ST, _MaskMap_ST;
            float4 _BaseTint;
            float _BumpScale, _MaskAO, _SpecStrength;
            float _ShadeSteps, _ShadeFloor, _LightWrap, _ShadowStrength;
            float4 _ShadowTint;
            float _AmbientBoost, _LightBoost;
            float4 _RimColor;
            float _RimPower, _RimStrength;
            float4 _HazeColor;
            float _HazeStart, _HazeEnd, _HazeMax;
            float4 _OutlineColor;
            float _OutlineWidth, _OutlineFadeStart, _OutlineFadeEnd, _OutlineFromPos;
        CBUFFER_END
        ENDHLSL

        // ===================== ОБВОДКА =====================
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma shader_feature_local _OUTLINE_FROM_POSITION

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float fade : TEXCOORD0; };

            V OutlineVert(A IN)
            {
                V OUT;
                #ifdef _OUTLINE_FROM_POSITION
                    float3 dir = normalize(IN.positionOS.xyz);
                #else
                    float3 dir = normalize(IN.normalOS);
                #endif

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = normalize(TransformObjectToWorldDir(dir));
                float dist = length(GetCameraPositionWS() - posWS);

                float w = _OutlineWidth * 0.0015 * dist;
                float fade = 1.0 - saturate((dist - _OutlineFadeStart) /
                                            max(0.001, _OutlineFadeEnd - _OutlineFadeStart));
                w *= fade;

                posWS += nrmWS * w;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.fade = fade;
                return OUT;
            }

            half4 OutlineFrag(V IN) : SV_Target
            {
                clip(IN.fade - 0.01);
                return half4(_OutlineColor.rgb, 1);
            }
            ENDHLSL
        }

        // ===================== ОСНОВНОЙ ПРОХОД =====================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);

            struct A
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };
            struct V
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            V Vert(A IN)
            {
                V OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS = n.normalWS;
                OUT.tangentWS = float4(n.tangentWS, IN.tangentOS.w);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            half4 Frag(V IN) : SV_Target
            {
                float3 posWS = IN.positionWS;
                float3 Ngeo = normalize(IN.normalWS);

                // ---------- текстуры ----------
                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half4 maskTex = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, IN.uv);
                half3 albedo = baseTex.rgb * _BaseTint.rgb;

                half3 nTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                float3 T = normalize(IN.tangentWS.xyz);
                float3 B = normalize(cross(Ngeo, T) * IN.tangentWS.w);
                float3 N = normalize(nTS.x * T + nTS.y * B + nTS.z * Ngeo);

                half occlusion = lerp(1.0, maskTex.g, _MaskAO);
                half smoothness = maskTex.a;

                // ---------- toon-свет ----------
                float4 shadowCoord = TransformWorldToShadowCoord(posWS);
                // перегрузка с positionWS обязательна: только она применяет light cookie,
                // то есть тени от облаков
                Light main = GetMainLight(shadowCoord, posWS, half4(1, 1, 1, 1));

                float ndl = dot(N, main.direction);
                float t = saturate(lerp(ndl, ndl * 0.5 + 0.5, _LightWrap));
                t = min(t, lerp(1.0, main.shadowAttenuation, _ShadowStrength));

                float ls = max(1.0, floor(_ShadeSteps));
                float scaled = t * ls;
                float fr = frac(scaled);
                float aa = saturate(fwidth(scaled) * 0.7);
                float band = saturate((floor(scaled) + smoothstep(0.5 - aa, 0.5 + aa, fr)) / ls);

                float lit = lerp(_ShadeFloor, 1.0, band);
                half3 tint = lerp(_ShadowTint.rgb, half3(1, 1, 1), band);

                half3 col = albedo * main.color * lit * tint * _LightBoost;
                col += albedo * SampleSH(N) * _AmbientBoost * occlusion;

                float3 V_ = normalize(GetCameraPositionWS() - posWS);

                // ---------- toon-блик ----------
                float3 H = normalize(main.direction + V_);
                float spec = pow(saturate(dot(N, H)), exp2(smoothness * 9.0) + 1.0);
                spec = smoothstep(0.35, 0.45, spec);
                col += main.color * spec * _SpecStrength * band;

                // ---------- контровой по силуэту ----------
                float rim = pow(saturate(1.0 - saturate(dot(V_, N))), _RimPower);
                rim *= saturate(ndl * 0.5 + 0.5);      // только со стороны солнца
                col += _RimColor.rgb * rim * _RimStrength;

                // ---------- воздушная перспектива ----------
                float dist = length(GetCameraPositionWS() - posWS);
                float haze = saturate((dist - _HazeStart) / max(0.001, _HazeEnd - _HazeStart));
                col = lerp(col, _HazeColor.rgb, saturate(haze * _HazeMax));

                col = MixFog(col, IN.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // ===================== СЛУЖЕБНЫЕ ПРОХОДЫ =====================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex SV
            #pragma fragment SF
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; };
            V SV(A IN)
            {
                V o;
                float3 pw = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(IN.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(pw, nw, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                return o;
            }
            half4 SF(V IN) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex DV
            #pragma fragment DF
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; };
            V DV(A IN) { V o; o.positionCS = TransformObjectToHClip(IN.positionOS.xyz); return o; }
            half4 DF(V IN) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex NV
            #pragma fragment NF
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            V NV(A IN)
            {
                V o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return o;
            }
            half4 NF(V IN) : SV_Target { return half4(normalize(IN.normalWS) * 0.5 + 0.5, 0); }
            ENDHLSL
        }
    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
