// ============================================================================
//  TerrainAntiTiling.shader
//  URP • 4 слоя за один проход • height-blend • стохастический тайлинг
//  + макро-вариация крупного масштаба
//
//  ВАЖНО: этот шейдер НЕ поддерживает Draw Instanced.
//  Terrain Settings -> снять галку "Draw Instanced" перед использованием.
//  (почему — см. TERRAIN_SETUP.md)
// ============================================================================
Shader "Custom/Terrain/AntiTiling"
{
    Properties
    {
        // --- эти имена Unity заполняет сама из Terrain Layers, не переименовывать ---
        [HideInInspector] _Control ("Control (RGBA)", 2D) = "red" {}
        [HideInInspector] _Splat0 ("Layer 0", 2D) = "grey" {}
        [HideInInspector] _Splat1 ("Layer 1", 2D) = "grey" {}
        [HideInInspector] _Splat2 ("Layer 2", 2D) = "grey" {}
        [HideInInspector] _Splat3 ("Layer 3", 2D) = "grey" {}
        [HideInInspector] _Normal0 ("Normal 0", 2D) = "bump" {}
        [HideInInspector] _Normal1 ("Normal 1", 2D) = "bump" {}
        [HideInInspector] _Normal2 ("Normal 2", 2D) = "bump" {}
        [HideInInspector] _Normal3 ("Normal 3", 2D) = "bump" {}
        [HideInInspector] _Mask0 ("Mask 0", 2D) = "grey" {}
        [HideInInspector] _Mask1 ("Mask 1", 2D) = "grey" {}
        [HideInInspector] _Mask2 ("Mask 2", 2D) = "grey" {}
        [HideInInspector] _Mask3 ("Mask 3", 2D) = "grey" {}
        [HideInInspector] _NormalScale0 ("NS0", Float) = 1.0
        [HideInInspector] _NormalScale1 ("NS1", Float) = 1.0
        [HideInInspector] _NormalScale2 ("NS2", Float) = 1.0
        [HideInInspector] _NormalScale3 ("NS3", Float) = 1.0
        [HideInInspector] _Metallic0 ("M0", Float) = 0.0
        [HideInInspector] _Metallic1 ("M1", Float) = 0.0
        [HideInInspector] _Metallic2 ("M2", Float) = 0.0
        [HideInInspector] _Metallic3 ("M3", Float) = 0.0
        [HideInInspector] _Smoothness0 ("S0", Float) = 0.1
        [HideInInspector] _Smoothness1 ("S1", Float) = 0.1
        [HideInInspector] _Smoothness2 ("S2", Float) = 0.1
        [HideInInspector] _Smoothness3 ("S3", Float) = 0.1
        [HideInInspector] _LayerHasMask0 ("HM0", Float) = 0.0
        [HideInInspector] _LayerHasMask1 ("HM1", Float) = 0.0
        [HideInInspector] _LayerHasMask2 ("HM2", Float) = 0.0
        [HideInInspector] _LayerHasMask3 ("HM3", Float) = 0.0
        [HideInInspector] _TerrainHolesTexture ("Holes", 2D) = "white" {}

        // --- наши настройки ---
        [Header(Height Blending)]
        _HeightTransition ("Height Transition", Range(0.001, 1.0)) = 0.2
        _HeightContrast   ("Height Contrast",   Range(0.1, 4.0))  = 1.0

        [Header(Anti Tiling)]
        [Toggle(_STOCHASTIC_ON)] _StochasticOn ("Stochastic Tiling", Float) = 1
        _StochasticScale ("Stochastic Cell Scale", Range(0.05, 2.0)) = 0.35
        _StochasticBlend ("Stochastic Blend Sharpness", Range(1.0, 8.0)) = 3.0

        [Header(Macro Variation)]
        _MacroTex   ("Macro Variation (grayscale)", 2D) = "grey" {}
        _MacroScale ("Macro Tiling", Float) = 0.004
        _MacroStrength ("Macro Strength", Range(0, 1)) = 0.25
        _MacroTint  ("Macro Tint (hue only)", Color) = (0.9, 0.95, 0.8, 1)
        _MacroTintStrength ("Macro Tint Strength", Range(0, 1)) = 0.3

        [Header(Color Correction)]
        _Brightness ("Brightness", Range(0.5, 2.5)) = 1.15
        _Saturation ("Saturation", Range(0, 2)) = 1.1
        _AlbedoContrast ("Contrast", Range(0.5, 2)) = 1.15
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 0.5

        [Header(Distance Detail Fade)]
        _DetailFadeStart ("Detail Fade Start (m)", Float) = 25
        _DetailFadeEnd   ("Detail Fade End (m)",   Float) = 90
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry-100"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "TerrainCompatible" = "True"
        }

        // ====================== FORWARD ======================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex TerrainVert
            #pragma fragment TerrainFrag

            #pragma shader_feature_local _STOCHASTIC_ON
            #pragma shader_feature_local _ALPHATEST_ON

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ------------------------------------------------ объявления
            TEXTURE2D(_Control);    SAMPLER(sampler_Control);
            TEXTURE2D(_Splat0);     SAMPLER(sampler_Splat0);
            TEXTURE2D(_Splat1); TEXTURE2D(_Splat2); TEXTURE2D(_Splat3);
            TEXTURE2D(_Normal0); TEXTURE2D(_Normal1); TEXTURE2D(_Normal2); TEXTURE2D(_Normal3);
            TEXTURE2D(_Mask0);   TEXTURE2D(_Mask1);   TEXTURE2D(_Mask2);   TEXTURE2D(_Mask3);
            TEXTURE2D(_MacroTex); SAMPLER(sampler_MacroTex);
            #ifdef _ALPHATEST_ON
                TEXTURE2D(_TerrainHolesTexture); SAMPLER(sampler_TerrainHolesTexture);
            #endif

            CBUFFER_START(UnityPerMaterial)
                float4 _Control_ST;
                float4 _Control_TexelSize;
                float4 _Splat0_ST, _Splat1_ST, _Splat2_ST, _Splat3_ST;
                float4 _MacroTex_ST;
                float _NormalScale0, _NormalScale1, _NormalScale2, _NormalScale3;
                float _Metallic0, _Metallic1, _Metallic2, _Metallic3;
                float _Smoothness0, _Smoothness1, _Smoothness2, _Smoothness3;
                float _LayerHasMask0, _LayerHasMask1, _LayerHasMask2, _LayerHasMask3;
                float _HeightTransition, _HeightContrast;
                float _StochasticScale, _StochasticBlend;
                float _MacroScale, _MacroStrength, _MacroTintStrength;
                float4 _MacroTint;
                float _Brightness, _Saturation, _AlbedoContrast, _OcclusionStrength;
                float _DetailFadeStart, _DetailFadeEnd;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
                float4 shadowCoord: TEXCOORD4;
            };

            // ============================================================
            //  СТОХАСТИЧЕСКИЙ ТАЙЛИНГ (треугольная сетка, 3 выборки)
            //  Разбивает плоскость на треугольники, в каждом сдвигает UV
            //  на случайный вектор -> регулярный узор рассыпается.
            // ============================================================
            float2 StochHash(float2 p)
            {
                return frac(sin(float2(dot(p, float2(127.1, 311.7)),
                                       dot(p, float2(269.5, 183.3)))) * 43758.5453);
            }

            void TriangleGrid(float2 uv, out float3 w, out float2 v0, out float2 v1, out float2 v2)
            {
                const float2x2 toSkewed = float2x2(1.0, 0.0, -0.57735027, 1.15470054);
                float2 sk = mul(toSkewed, uv);
                float2 baseId = floor(sk);
                float3 t = float3(frac(sk), 0);
                t.z = 1.0 - t.x - t.y;

                if (t.z > 0.0)
                {
                    w  = float3(t.z, t.y, t.x);
                    v0 = baseId; v1 = baseId + float2(0, 1); v2 = baseId + float2(1, 0);
                }
                else
                {
                    w  = float3(-t.z, 1.0 - t.y, 1.0 - t.x);
                    v0 = baseId + float2(1, 1); v1 = baseId + float2(1, 0); v2 = baseId + float2(0, 1);
                }
            }

            // Одна «стохастическая» выборка. dx/dy передаём явно,
            // иначе на границах треугольников mip-уровень скачет и лезут швы.
            #define STOCH_SAMPLE(tex, samp, uv, dx, dy, outVal)                          \
            {                                                                            \
                float3 _w; float2 _v0, _v1, _v2;                                         \
                TriangleGrid((uv) * _StochasticScale, _w, _v0, _v1, _v2);                \
                float4 _a = SAMPLE_TEXTURE2D_GRAD(tex, samp, (uv) + StochHash(_v0), dx, dy); \
                float4 _b = SAMPLE_TEXTURE2D_GRAD(tex, samp, (uv) + StochHash(_v1), dx, dy); \
                float4 _c = SAMPLE_TEXTURE2D_GRAD(tex, samp, (uv) + StochHash(_v2), dx, dy); \
                float3 _pw = pow(abs(_w), _StochasticBlend);                             \
                _pw /= (_pw.x + _pw.y + _pw.z + 1e-5);                                   \
                outVal = _a * _pw.x + _b * _pw.y + _c * _pw.z;                           \
            }

            #ifdef _STOCHASTIC_ON
                #define SAMPLE_LAYER(tex, samp, uv, dx, dy, o) STOCH_SAMPLE(tex, samp, uv, dx, dy, o)
            #else
                #define SAMPLE_LAYER(tex, samp, uv, dx, dy, o) o = SAMPLE_TEXTURE2D_GRAD(tex, samp, uv, dx, dy);
            #endif

            // ============================================================
            //  HEIGHT BLEND: слой с большей высотой «перекрывает» соседа
            //  вместо линейного растворения -> трава прорастает сквозь песок
            // ============================================================
            float4 HeightBlendWeights(float4 control, float4 heights)
            {
                heights = pow(saturate(heights), _HeightContrast);
                float4 hw = control * (heights + 0.001);
                float maxH = max(max(hw.x, hw.y), max(hw.z, hw.w));
                float4 w = max(hw - (maxH - _HeightTransition), 0.0);
                return w / (w.x + w.y + w.z + w.w + 1e-6);
            }

            // ------------------------------------------------ vertex
            Varyings TerrainVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv         = IN.uv;
                OUT.fogFactor  = ComputeFogFactor(p.positionCS.z);
                OUT.shadowCoord = GetShadowCoord(p);
                return OUT;
            }

            // ------------------------------------------------ fragment
            half4 TerrainFrag(Varyings IN) : SV_Target
            {
                #ifdef _ALPHATEST_ON
                    float hole = SAMPLE_TEXTURE2D(_TerrainHolesTexture, sampler_TerrainHolesTexture, IN.uv).r;
                    clip(hole == 0.0 ? -1 : 1);
                #endif

                // splatmap с поправкой на полтекселя, иначе слои «подтекают» по краям
                float2 cUV = (IN.uv * (_Control_TexelSize.zw - 1.0) + 0.5) * _Control_TexelSize.xy;
                float4 control = SAMPLE_TEXTURE2D(_Control, sampler_Control, cUV);

                float2 uv0 = IN.uv * _Splat0_ST.xy + _Splat0_ST.zw;
                float2 uv1 = IN.uv * _Splat1_ST.xy + _Splat1_ST.zw;
                float2 uv2 = IN.uv * _Splat2_ST.xy + _Splat2_ST.zw;
                float2 uv3 = IN.uv * _Splat3_ST.xy + _Splat3_ST.zw;

                float2 dx0 = ddx(uv0), dy0 = ddy(uv0);
                float2 dx1 = ddx(uv1), dy1 = ddy(uv1);
                float2 dx2 = ddx(uv2), dy2 = ddy(uv2);
                float2 dx3 = ddx(uv3), dy3 = ddy(uv3);

                float4 a0, a1, a2, a3;
                SAMPLE_LAYER(_Splat0, sampler_Splat0, uv0, dx0, dy0, a0)
                SAMPLE_LAYER(_Splat1, sampler_Splat0, uv1, dx1, dy1, a1)
                SAMPLE_LAYER(_Splat2, sampler_Splat0, uv2, dx2, dy2, a2)
                SAMPLE_LAYER(_Splat3, sampler_Splat0, uv3, dx3, dy3, a3)

                float4 m0, m1, m2, m3;
                SAMPLE_LAYER(_Mask0, sampler_Splat0, uv0, dx0, dy0, m0)
                SAMPLE_LAYER(_Mask1, sampler_Splat0, uv1, dx1, dy1, m1)
                SAMPLE_LAYER(_Mask2, sampler_Splat0, uv2, dx2, dy2, m2)
                SAMPLE_LAYER(_Mask3, sampler_Splat0, uv3, dx3, dy3, m3)

                // слой без mask map -> берём значения из слайдеров
                m0 = lerp(float4(_Metallic0, 1, 0.5, _Smoothness0), m0, _LayerHasMask0);
                m1 = lerp(float4(_Metallic1, 1, 0.5, _Smoothness1), m1, _LayerHasMask1);
                m2 = lerp(float4(_Metallic2, 1, 0.5, _Smoothness2), m2, _LayerHasMask2);
                m3 = lerp(float4(_Metallic3, 1, 0.5, _Smoothness3), m3, _LayerHasMask3);

                float4 w = HeightBlendWeights(control, float4(m0.b, m1.b, m2.b, m3.b));

                half3 albedo = (a0.rgb * w.x + a1.rgb * w.y + a2.rgb * w.z + a3.rgb * w.w);
                half  metallic   = m0.r * w.x + m1.r * w.y + m2.r * w.z + m3.r * w.w;
                half  occlusion  = m0.g * w.x + m1.g * w.y + m2.g * w.z + m3.g * w.w;
                half  smoothness = m0.a * w.x + m1.a * w.y + m2.a * w.z + m3.a * w.w;

                // ---- нормали ----
                float dist = length(GetCameraPositionWS() - IN.positionWS);
                half detailFade = 1.0 - saturate((dist - _DetailFadeStart) / max(0.001, _DetailFadeEnd - _DetailFadeStart));

                float4 n0, n1, n2, n3;
                SAMPLE_LAYER(_Normal0, sampler_Splat0, uv0, dx0, dy0, n0)
                SAMPLE_LAYER(_Normal1, sampler_Splat0, uv1, dx1, dy1, n1)
                SAMPLE_LAYER(_Normal2, sampler_Splat0, uv2, dx2, dy2, n2)
                SAMPLE_LAYER(_Normal3, sampler_Splat0, uv3, dx3, dy3, n3)

                half3 nTS = UnpackNormalScale(n0, _NormalScale0) * w.x
                          + UnpackNormalScale(n1, _NormalScale1) * w.y
                          + UnpackNormalScale(n2, _NormalScale2) * w.z
                          + UnpackNormalScale(n3, _NormalScale3) * w.w;
                nTS = normalize(lerp(half3(0, 0, 1), nTS, detailFade));

                // TBN для террейна: U идёт по +X, V по +Z
                float3 N = normalize(IN.normalWS);
                float3 T = normalize(cross(N, float3(0, 0, 1)));
                float3 B = cross(T, N);
                float3 normalWS = normalize(nTS.x * T + nTS.y * B + nTS.z * N);

                // ---- макро-вариация: убивает ощущение плитки на дистанции ----
                // Множитель колеблется ВОКРУГ 1.0, поэтому средняя яркость не падает.
                half macro = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex,
                                              IN.positionWS.xz * _MacroScale).r;
                half macroS = (macro - 0.5) * 2.0;                  // -1..1, среднее 0
                half3 macroMul = 1.0 + macroS * _MacroStrength;     // яркостная вариация

                // Тинт нормализуем по собственной яркости -> меняет только оттенок
                half3 tintN = _MacroTint.rgb / max(1e-4, dot(_MacroTint.rgb, half3(0.333, 0.333, 0.333)));
                macroMul *= lerp(half3(1, 1, 1), tintN, _MacroTintStrength * saturate(macroS * 0.5 + 0.5));
                albedo *= macroMul;

                // ---- цветокоррекция (компенсация потери контраста от стохастики) ----
                albedo = (albedo - 0.5) * _AlbedoContrast + 0.5;
                half lum = dot(albedo, half3(0.2126, 0.7152, 0.0722));
                albedo = lerp(lum.xxx, albedo, _Saturation);
                albedo = max(albedo * _Brightness, 0.0);

                // AO уже частично запечён в base color -> ослабляем, иначе тени удваиваются
                occlusion = lerp(1.0, occlusion, _OcclusionStrength);

                // ---- освещение ----
                SurfaceData sd = (SurfaceData)0;
                sd.albedo = albedo;
                sd.metallic = metallic;
                sd.smoothness = smoothness;
                sd.occlusion = occlusion;
                sd.alpha = 1.0;
                sd.normalTS = nTS;

                InputData id = (InputData)0;
                id.positionWS = IN.positionWS;
                id.normalWS = normalWS;
                id.viewDirectionWS = normalize(GetCameraPositionWS() - IN.positionWS);
                id.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                id.fogCoord = IN.fogFactor;
                id.bakedGI = SampleSH(normalWS);
                id.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                id.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(id, sd);
                color.rgb = MixFog(color.rgb, IN.fogFactor);
                return color;
            }
            ENDHLSL
        }

        // ====================== SHADOW CASTER ======================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; };

            V ShadowVert(A IN)
            {
                V OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = cs;
                return OUT;
            }
            half4 ShadowFrag(V IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ====================== DEPTH ONLY ======================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; };

            V DepthVert(A IN)
            {
                V OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }
            half4 DepthFrag(V IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
