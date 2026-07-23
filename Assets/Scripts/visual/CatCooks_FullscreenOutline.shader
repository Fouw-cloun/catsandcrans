// ============================================================================
//  CatCooks/Fullscreen Outline   —   Unity 6 (URP 17) / Render Graph
//
//  Обводка всей сцены по разрывам ГЛУБИНЫ и НОРМАЛЕЙ.
//  В отличие от обводки на меше (inverted hull), рисует ещё и ВНУТРЕННИЕ
//  линии: границы деталей, стыки досок, края ступенек — «рисованный» вид.
//
//  ИСПОЛЬЗОВАНИЕ (без единой строчки C#):
//   1. Материал на основе этого шейдера.
//   2. Universal Renderer Data → Add Renderer Feature → Full Screen Pass Renderer Feature
//   3. Pass Material = твой материал
//      Injection Point = After Rendering Post Processing
//      Requirements = Depth + Normal   (ОБЯЗАТЕЛЬНО!)
//
//  Совет: ставь ПОСЛЕ пост-обработки, тогда линии остаются чистыми и по ним
//  не «плывёт» Bloom. Если хочешь, чтобы обводка сама слегка светилась —
//  переставь на Before Rendering Post Processing.
// ============================================================================
Shader "CatCooks/Fullscreen Outline"
{
    Properties
    {
        [Header(Outline)]
        _OutlineColor ("Outline Color", Color) = (0.16, 0.10, 0.09, 1)
        _Thickness ("Thickness (px)", Range(1, 5)) = 1.4
        _Opacity ("Opacity", Range(0, 1)) = 1

        [Header(Depth Edges)]
        _DepthThreshold ("Depth Threshold", Range(0.001, 3)) = 0.35
        _DepthNormalBias ("Grazing Angle Bias", Range(0, 3)) = 1.2

        [Header(Normal Edges)]
        _NormalThreshold ("Normal Threshold", Range(0, 1)) = 0.45
        _NormalSoftness ("Normal Edge Softness", Range(0.01, 0.5)) = 0.12

        [Header(Distance Fade)]
        _FadeStart ("Fade Start (m)", Float) = 25
        _FadeEnd ("Fade End (m)", Float) = 60
        _TintByScene ("Tint Outline By Scene Color", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "CatCooksOutline"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float4 _OutlineColor;
            float  _Thickness;
            float  _Opacity;
            float  _DepthThreshold;
            float  _DepthNormalBias;
            float  _NormalThreshold;
            float  _NormalSoftness;
            float  _FadeStart;
            float  _FadeEnd;
            float  _TintByScene;

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                float3 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                // --- четыре точки по диагоналям (крест Робертса) ---
                float2 texel = _Thickness * _BlitTexture_TexelSize.xy;
                float2 uv0 = uv + float2(-texel.x, -texel.y);
                float2 uv1 = uv + float2( texel.x,  texel.y);
                float2 uv2 = uv + float2(-texel.x,  texel.y);
                float2 uv3 = uv + float2( texel.x, -texel.y);

                float d0 = LinearEyeDepth(SampleSceneDepth(uv0), _ZBufferParams);
                float d1 = LinearEyeDepth(SampleSceneDepth(uv1), _ZBufferParams);
                float d2 = LinearEyeDepth(SampleSceneDepth(uv2), _ZBufferParams);
                float d3 = LinearEyeDepth(SampleSceneDepth(uv3), _ZBufferParams);
                float dC = LinearEyeDepth(SampleSceneDepth(uv),  _ZBufferParams);

                float3 n0 = SampleSceneNormals(uv0);
                float3 n1 = SampleSceneNormals(uv1);
                float3 n2 = SampleSceneNormals(uv2);
                float3 n3 = SampleSceneNormals(uv3);
                float3 nC = SampleSceneNormals(uv);

                // --- край по глубине ---
                float depthDiff = abs(d1 - d0) + abs(d3 - d2);

                // на скользящих углах (пол, уходящий вдаль) глубина скачет сама по себе —
                // поднимаем порог, иначе весь пол покроется линиями
                float3 viewDir = normalize(float3((uv - 0.5) * 2.0, 1.0));
                float grazing = 1.0 - saturate(abs(dot(nC, float3(0,0,1))));
                float dThresh = _DepthThreshold * (1.0 + grazing * _DepthNormalBias * 8.0) * (dC * 0.05 + 1.0);
                float depthEdge = step(dThresh, depthDiff);

                // --- край по нормалям ---
                float normalDiff = (1.0 - saturate(dot(n0, n1))) + (1.0 - saturate(dot(n2, n3)));
                float normalEdge = smoothstep(_NormalThreshold - _NormalSoftness,
                                              _NormalThreshold + _NormalSoftness, normalDiff);

                float edge = saturate(max(depthEdge, normalEdge));

                // небо не обводим
                edge *= step(dC, _ProjectionParams.z - 1.0);

                // --- затухание с расстоянием: дальние объекты без линий ---
                edge *= 1.0 - saturate((dC - _FadeStart) / max(_FadeEnd - _FadeStart, 0.001));

                // линия чуть подкрашивается цветом сцены — мягче, «акварельнее»
                float3 lineColor = lerp(_OutlineColor.rgb, _OutlineColor.rgb * (0.5 + sceneColor), _TintByScene);

                float3 outCol = lerp(sceneColor, lineColor, edge * _Opacity);
                return float4(outCol, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
