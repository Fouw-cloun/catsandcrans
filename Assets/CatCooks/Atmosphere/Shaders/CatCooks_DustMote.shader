// ============================================================================
//  CatCooks/Dust Mote  —  мягкая светящаяся пылинка для Particle System.
//  Точка рисуется процедурно (текстура не нужна), аддитивная, с depth fade.
//  URP / Unity 6. Используется скриптом SunDustParticles.
// ============================================================================
Shader "CatCooks/Dust Mote"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 0.95, 0.8, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1.4
        _Softness ("Dot Softness", Range(0.05, 0.5)) = 0.3
        _DepthFade ("Depth Fade", Range(0.01, 2)) = 0.4
        _NearFade ("Near Camera Fade (FP)", Range(0, 2)) = 0.6

    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "DustMote"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Intensity;
                float  _Softness;
                float  _DepthFade;
                float  _NearFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;      // цвет и альфа из Particle System
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                float4 screenPos  : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.screenPos  = ComputeScreenPos(o.positionCS);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // процедурная мягкая точка: яркое ядро + пушистый ореол
                float d = length(i.uv - 0.5) * 2.0;
                float dot = 1.0 - smoothstep(1.0 - _Softness * 2.0, 1.0, d);
                float core = 1.0 - smoothstep(0.0, 0.35, d);
                float a = (dot * 0.6 + core * 0.6) * i.color.a;

                // мягкое пересечение с геометрией
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                a *= saturate((sceneEye - i.screenPos.w) / _DepthFade);
                a *= saturate((i.screenPos.w - 0.15) / max(_NearFade, 0.001));

                return half4(_Color.rgb * i.color.rgb * a * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
