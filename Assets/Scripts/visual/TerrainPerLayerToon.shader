// ============================================================================
//  TerrainPerLayer.shader
//  URP • 4 слоя • индивидуальные настройки каждого слоя
//
//  Каждый параметр — Vector4, где X=слой0, Y=слой1, Z=слой2, W=слой3.
//  Порядок слоёв совпадает с порядком в Terrain -> Paint Texture.
//
//  ВАЖНО: Draw Instanced должен быть ВЫКЛЮЧЕН.
// ============================================================================
Shader "Custom/Terrain/PerLayerToon"
{
    Properties
    {
        [HideInInspector] _Control ("Control", 2D) = "red" {}
        [HideInInspector] _Splat0 ("L0", 2D) = "grey" {}
        [HideInInspector] _Splat1 ("L1", 2D) = "grey" {}
        [HideInInspector] _Splat2 ("L2", 2D) = "grey" {}
        [HideInInspector] _Splat3 ("L3", 2D) = "grey" {}
        [HideInInspector] _Normal0 ("N0", 2D) = "bump" {}
        [HideInInspector] _Normal1 ("N1", 2D) = "bump" {}
        [HideInInspector] _Normal2 ("N2", 2D) = "bump" {}
        [HideInInspector] _Normal3 ("N3", 2D) = "bump" {}
        [HideInInspector] _Mask0 ("M0", 2D) = "grey" {}
        [HideInInspector] _Mask1 ("M1", 2D) = "grey" {}
        [HideInInspector] _Mask2 ("M2", 2D) = "grey" {}
        [HideInInspector] _Mask3 ("M3", 2D) = "grey" {}
        [HideInInspector] _NormalScale0 ("", Float) = 1
        [HideInInspector] _NormalScale1 ("", Float) = 1
        [HideInInspector] _NormalScale2 ("", Float) = 1
        [HideInInspector] _NormalScale3 ("", Float) = 1
        [HideInInspector] _Metallic0 ("", Float) = 0
        [HideInInspector] _Metallic1 ("", Float) = 0
        [HideInInspector] _Metallic2 ("", Float) = 0
        [HideInInspector] _Metallic3 ("", Float) = 0
        [HideInInspector] _Smoothness0 ("", Float) = 0.1
        [HideInInspector] _Smoothness1 ("", Float) = 0.1
        [HideInInspector] _Smoothness2 ("", Float) = 0.1
        [HideInInspector] _Smoothness3 ("", Float) = 0.1
        [HideInInspector] _LayerHasMask0 ("", Float) = 0
        [HideInInspector] _LayerHasMask1 ("", Float) = 0
        [HideInInspector] _LayerHasMask2 ("", Float) = 0
        [HideInInspector] _LayerHasMask3 ("", Float) = 0
        [HideInInspector] _TerrainHolesTexture ("", 2D) = "white" {}

        // ---------- ПОСЛОЙНЫЕ: xyzw = слой 0,1,2,3 ----------
        _LayerStochastic     ("Stochastic on/off",  Vector) = (1,1,1,1)
        _LayerUVRotation     ("UV rotation (deg)",  Vector) = (0,25,55,80)
        _LayerBrightness     ("Brightness",         Vector) = (1,1,1,1)
        _LayerSaturation     ("Saturation",         Vector) = (1,1,1,1)
        _LayerContrast       ("Contrast",           Vector) = (1,1,1,1)
        _LayerMacro          ("Macro influence",    Vector) = (1,1,1,1)
        _LayerNormalMul      ("Normal multiplier",  Vector) = (1,1,1,1)
        _LayerHeightContrast ("Height contrast",    Vector) = (1,1,1,1)

        // ---------- ГЛОБАЛЬНЫЕ ----------
        _HeightTransition ("Height transition", Range(0.001,1)) = 0.2
        _StochasticScale  ("Stochastic cell scale", Range(0.05,2)) = 0.35
        _StochasticBlend  ("Stochastic sharpness", Range(1,8)) = 3.0
        _MacroTex   ("Macro variation", 2D) = "grey" {}
        _MacroScale ("Macro tiling", Float) = 0.004
        _MacroStrength ("Macro strength", Range(0,1)) = 0.25
        _MacroTint  ("Macro tint (hue only)", Color) = (0.9,0.95,0.8,1)
        _MacroTintStrength ("Macro tint strength", Range(0,1)) = 0.3
        _OcclusionStrength ("Occlusion strength", Range(0,1)) = 0.5
        _DetailFadeStart ("Detail fade start (m)", Float) = 25
        _DetailFadeEnd   ("Detail fade end (m)", Float) = 90

        [Header(Toon Lighting)]
        _ShadeSteps    ("Shade steps", Range(1,6)) = 3
        _ShadeFloor    ("Shadow floor (brightness)", Range(0,1)) = 0.62
        _LightWrap     ("Light wrap", Range(0,1)) = 0.45
        _ShadowTint    ("Shadow tint", Color) = (0.62, 0.72, 0.95, 1)
        _ShadowStrength("Cast shadow strength", Range(0,1)) = 0.55
        _AmbientBoost  ("Ambient boost", Range(0,3)) = 1.4
        _LightBoost    ("Direct light boost", Range(0,3)) = 1.1
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

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _ALPHATEST_ON

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Control); SAMPLER(sampler_Control);
            TEXTURE2D(_Splat0);  SAMPLER(sampler_Splat0);
            TEXTURE2D(_Splat1); TEXTURE2D(_Splat2); TEXTURE2D(_Splat3);
            TEXTURE2D(_Normal0); TEXTURE2D(_Normal1); TEXTURE2D(_Normal2); TEXTURE2D(_Normal3);
            TEXTURE2D(_Mask0); TEXTURE2D(_Mask1); TEXTURE2D(_Mask2); TEXTURE2D(_Mask3);
            TEXTURE2D(_MacroTex); SAMPLER(sampler_MacroTex);
            #ifdef _ALPHATEST_ON
                TEXTURE2D(_TerrainHolesTexture); SAMPLER(sampler_TerrainHolesTexture);
            #endif

            CBUFFER_START(UnityPerMaterial)
                float4 _Control_ST, _Control_TexelSize;
                float4 _Splat0_ST, _Splat1_ST, _Splat2_ST, _Splat3_ST;
                float4 _MacroTex_ST;
                float4 _LayerStochastic, _LayerUVRotation, _LayerBrightness, _LayerSaturation;
                float4 _LayerContrast, _LayerMacro, _LayerNormalMul, _LayerHeightContrast;
                float _NormalScale0, _NormalScale1, _NormalScale2, _NormalScale3;
                float _Metallic0, _Metallic1, _Metallic2, _Metallic3;
                float _Smoothness0, _Smoothness1, _Smoothness2, _Smoothness3;
                float _LayerHasMask0, _LayerHasMask1, _LayerHasMask2, _LayerHasMask3;
                float _HeightTransition, _StochasticScale, _StochasticBlend;
                float _MacroScale, _MacroStrength, _MacroTintStrength;
                float4 _MacroTint;
                float _OcclusionStrength, _DetailFadeStart, _DetailFadeEnd;
                float _ShadeSteps, _ShadeFloor, _LightWrap, _ShadowStrength;
                float _AmbientBoost, _LightBoost;
                float4 _ShadowTint;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            // ---------- утилиты ----------
            float2 RotateUV(float2 uv, float deg)
            {
                float a = radians(deg);
                float s = sin(a), c = cos(a);
                return float2(uv.x * c - uv.y * s, uv.x * s + uv.y * c);
            }

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
                if (t.z > 0.0) { w = float3(t.z, t.y, t.x); v0 = baseId; v1 = baseId + float2(0,1); v2 = baseId + float2(1,0); }
                else { w = float3(-t.z, 1.0 - t.y, 1.0 - t.x); v0 = baseId + float2(1,1); v1 = baseId + float2(1,0); v2 = baseId + float2(0,1); }
            }

            float4 StochSample(TEXTURE2D_PARAM(tex, samp), float2 uv, float2 dx, float2 dy)
            {
                float3 w; float2 v0, v1, v2;
                TriangleGrid(uv * _StochasticScale, w, v0, v1, v2);
                float4 a = SAMPLE_TEXTURE2D_GRAD(tex, samp, uv + StochHash(v0), dx, dy);
                float4 b = SAMPLE_TEXTURE2D_GRAD(tex, samp, uv + StochHash(v1), dx, dy);
                float4 c = SAMPLE_TEXTURE2D_GRAD(tex, samp, uv + StochHash(v2), dx, dy);
                float3 pw = pow(abs(w), _StochasticBlend);
                pw /= (pw.x + pw.y + pw.z + 1e-5);
                return a * pw.x + b * pw.y + c * pw.z;
            }

            // Индивидуальная цветокоррекция слоя — ДО смешивания
            half3 LayerGrade(half3 c, int i, half3 macroMul)
            {
                c *= lerp(half3(1,1,1), macroMul, saturate(_LayerMacro[i]));
                c = (c - 0.5) * _LayerContrast[i] + 0.5;
                half l = dot(c, half3(0.2126, 0.7152, 0.0722));
                c = lerp(l.xxx, c, _LayerSaturation[i]);
                return max(c * _LayerBrightness[i], 0.0);
            }

            #define FETCH_LAYER(idx, sp, nm, mk, ST, A, N, M)                                  \
            {                                                                                  \
                float2 _u = RotateUV(IN.uv * ST.xy + ST.zw, _LayerUVRotation[idx]);            \
                float2 _dx = ddx(_u), _dy = ddy(_u);                                           \
                UNITY_BRANCH if (_LayerStochastic[idx] > 0.5)                                  \
                {                                                                              \
                    A = StochSample(TEXTURE2D_ARGS(sp, sampler_Splat0), _u, _dx, _dy);         \
                    N = StochSample(TEXTURE2D_ARGS(nm, sampler_Splat0), _u, _dx, _dy);         \
                    M = StochSample(TEXTURE2D_ARGS(mk, sampler_Splat0), _u, _dx, _dy);         \
                }                                                                              \
                else                                                                           \
                {                                                                              \
                    A = SAMPLE_TEXTURE2D_GRAD(sp, sampler_Splat0, _u, _dx, _dy);               \
                    N = SAMPLE_TEXTURE2D_GRAD(nm, sampler_Splat0, _u, _dx, _dy);               \
                    M = SAMPLE_TEXTURE2D_GRAD(mk, sampler_Splat0, _u, _dx, _dy);               \
                }                                                                              \
            }

            // ============================================================
            //  TOON LIGHTING
            //  Ступенчатый свет со светлым «полом» тени и цветным оттенком.
            //  Тень никогда не уходит в чёрный — отсюда мультяшная яркость.
            // ============================================================
            half3 ToonLighting(half3 albedo, float3 normalWS, float3 positionWS, half occlusion)
            {
                float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
                // ВАЖНО: перегрузка с positionWS — только она применяет light cookie,
                // то есть тени от облаков из SkyController.
                Light main = GetMainLight(shadowCoord, positionWS, half4(1,1,1,1));

                // light wrap: свет «заворачивается» за терминатор, тёмная сторона не чернеет
                half ndl = dot(normalWS, main.direction);
                half t = saturate(lerp(ndl, ndl * 0.5 + 0.5, _LightWrap));

                // тень от объектов, ослабленная (в мультяшке она мягкая)
                half sh = lerp(1.0, main.shadowAttenuation, _ShadowStrength);
                t = min(t, sh);

                // разбиение на ступени со сглаживанием края
                half steps = max(1.0, floor(_ShadeSteps));
                half scaled = t * steps;
                half fr = frac(scaled);
                half aa = saturate(fwidth(scaled) * 0.7);
                half band = (floor(scaled) + smoothstep(0.5 - aa, 0.5 + aa, fr)) / steps;
                band = saturate(band);

                // светлый пол: минимальная яркость в тени
                half lit = lerp(_ShadeFloor, 1.0, band);

                // цветной оттенок тени
                half3 tint = lerp(_ShadowTint.rgb, half3(1,1,1), band);

                half3 direct = albedo * main.color * lit * tint * _LightBoost;
                half3 ambient = albedo * SampleSH(normalWS) * _AmbientBoost
                              * lerp(1.0, occlusion, _OcclusionStrength);

                half3 col = direct + ambient;

                #ifdef _ADDITIONAL_LIGHTS
                    uint cnt = GetAdditionalLightsCount();
                    for (uint li = 0u; li < cnt; li++)
                    {
                        Light al = GetAdditionalLight(li, positionWS);
                        half an = saturate(dot(normalWS, al.direction) * 0.5 + 0.5);
                        half ab = floor(an * steps) / steps;
                        col += albedo * al.color * ab * al.distanceAttenuation * al.shadowAttenuation;
                    }
                #endif

                return col;
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = IN.uv;
                OUT.fogFactor = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                #ifdef _ALPHATEST_ON
                    float hole = SAMPLE_TEXTURE2D(_TerrainHolesTexture, sampler_TerrainHolesTexture, IN.uv).r;
                    clip(hole == 0.0 ? -1 : 1);
                #endif

                float2 cUV = (IN.uv * (_Control_TexelSize.zw - 1.0) + 0.5) * _Control_TexelSize.xy;
                float4 control = SAMPLE_TEXTURE2D(_Control, sampler_Control, cUV);

                // макро-множитель считаем один раз, слои берут его в своей дозе
                half macro = SAMPLE_TEXTURE2D(_MacroTex, sampler_MacroTex, IN.positionWS.xz * _MacroScale).r;
                half macroS = (macro - 0.5) * 2.0;
                half3 macroMul = 1.0 + macroS * _MacroStrength;
                half3 tintN = _MacroTint.rgb / max(1e-4, dot(_MacroTint.rgb, half3(0.333, 0.333, 0.333)));
                macroMul *= lerp(half3(1,1,1), tintN, _MacroTintStrength * saturate(macroS * 0.5 + 0.5));

                float4 a0, a1, a2, a3, n0, n1, n2, n3, m0, m1, m2, m3;
                FETCH_LAYER(0, _Splat0, _Normal0, _Mask0, _Splat0_ST, a0, n0, m0)
                FETCH_LAYER(1, _Splat1, _Normal1, _Mask1, _Splat1_ST, a1, n1, m1)
                FETCH_LAYER(2, _Splat2, _Normal2, _Mask2, _Splat2_ST, a2, n2, m2)
                FETCH_LAYER(3, _Splat3, _Normal3, _Mask3, _Splat3_ST, a3, n3, m3)

                m0 = lerp(float4(_Metallic0, 1, 0.5, _Smoothness0), m0, _LayerHasMask0);
                m1 = lerp(float4(_Metallic1, 1, 0.5, _Smoothness1), m1, _LayerHasMask1);
                m2 = lerp(float4(_Metallic2, 1, 0.5, _Smoothness2), m2, _LayerHasMask2);
                m3 = lerp(float4(_Metallic3, 1, 0.5, _Smoothness3), m3, _LayerHasMask3);

                // индивидуальная коррекция каждого слоя ДО блендинга
                a0.rgb = LayerGrade(a0.rgb, 0, macroMul);
                a1.rgb = LayerGrade(a1.rgb, 1, macroMul);
                a2.rgb = LayerGrade(a2.rgb, 2, macroMul);
                a3.rgb = LayerGrade(a3.rgb, 3, macroMul);

                // высоты с индивидуальным контрастом
                float4 h = float4(pow(saturate(m0.b), _LayerHeightContrast[0]),
                                  pow(saturate(m1.b), _LayerHeightContrast[1]),
                                  pow(saturate(m2.b), _LayerHeightContrast[2]),
                                  pow(saturate(m3.b), _LayerHeightContrast[3]));
                float4 hw = control * (h + 0.001);
                float maxH = max(max(hw.x, hw.y), max(hw.z, hw.w));
                float4 w = max(hw - (maxH - _HeightTransition), 0.0);
                w /= (w.x + w.y + w.z + w.w + 1e-6);

                half3 albedo = a0.rgb * w.x + a1.rgb * w.y + a2.rgb * w.z + a3.rgb * w.w;
                half metallic   = m0.r * w.x + m1.r * w.y + m2.r * w.z + m3.r * w.w;
                half occlusion  = m0.g * w.x + m1.g * w.y + m2.g * w.z + m3.g * w.w;
                half smoothness = m0.a * w.x + m1.a * w.y + m2.a * w.z + m3.a * w.w;
                occlusion = lerp(1.0, occlusion, _OcclusionStrength);

                float dist = length(GetCameraPositionWS() - IN.positionWS);
                half detailFade = 1.0 - saturate((dist - _DetailFadeStart) / max(0.001, _DetailFadeEnd - _DetailFadeStart));

                half3 nTS = UnpackNormalScale(n0, _NormalScale0 * _LayerNormalMul[0]) * w.x
                          + UnpackNormalScale(n1, _NormalScale1 * _LayerNormalMul[1]) * w.y
                          + UnpackNormalScale(n2, _NormalScale2 * _LayerNormalMul[2]) * w.z
                          + UnpackNormalScale(n3, _NormalScale3 * _LayerNormalMul[3]) * w.w;
                nTS = normalize(lerp(half3(0,0,1), nTS, detailFade));

                float3 N = normalize(IN.normalWS);
                float3 T = normalize(cross(N, float3(0,0,1)));
                float3 B = cross(T, N);
                float3 normalWS = normalize(nTS.x * T + nTS.y * B + nTS.z * N);

                half4 col = half4(ToonLighting(albedo, normalWS, IN.positionWS, occlusion), 1.0);
                col.rgb = MixFog(col.rgb, IN.fogFactor);
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex SV
            #pragma fragment SF
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
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
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex DV
            #pragma fragment DF
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; };
            V DV(A IN) { V o; o.positionCS = TransformObjectToHClip(IN.positionOS.xyz); return o; }
            half4 DF(V IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    CustomEditor "TerrainPerLayerGUI"
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
