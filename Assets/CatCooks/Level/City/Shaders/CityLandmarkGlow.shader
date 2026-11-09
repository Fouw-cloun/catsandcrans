// CatCooks — СВЕЧЕНИЕ СФЕР ОРИЕНТИРА (CityLandmarkGlow): копия поверхности сфер светится их же цветом из атласа
// (тускло, медленно «дышит»), вокруг каждой — мягкий ореол к камере. Ночью сильнее, днём — DayGlow.
// uv2.x: 0 — поверхность, 1 — ореол (uv1 — угол квада, uv2.y — размер ореола, uv0 — средний цвет сферы в атласе).
Shader "CatCooks/City/Landmark Glow"
{
    Properties
    {
        _BaseMap ("Атлас (ставит код)", 2D) = "white" {}
        _Glow ("Свечение поверхности", Range(0, 3)) = 0.6
        _Halo ("Ореол", Range(0, 2)) = 0.35
        _DayGlow ("Днём (доля)", Range(0, 1)) = 0.15
        _Pulse ("Дыхание", Range(0, 1)) = 0.15
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+5" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "LandmarkGlow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Offset -1, -1
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CityCommon.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Glow, _Halo, _DayGlow, _Pulse;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; float2 uv2 : TEXCOORD2; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 info : TEXCOORD1; float3 tint : TEXCOORD2; };

            V vert(A i)
            {
                V o;
                float3 wp = TransformObjectToWorld(i.pos.xyz);
                o.tint = 0;
                if (i.uv2.x > 0.5)
                {
                    float sc = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));   // масштаб объекта
                    float r = i.uv2.y * sc;
                    float3 R = UNITY_MATRIX_V[0].xyz, U = UNITY_MATRIX_V[1].xyz;
                    wp += (R * i.uv1.x + U * i.uv1.y) * r;
                    o.tint = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, i.uv, 0).rgb;
                }
                o.pos = TransformWorldToHClip(wp);
                o.uv = i.uv;
                o.info = float3(i.uv2.x, i.uv1.x, i.uv1.y);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float night = lerp(_DayGlow, 1.0, saturate((float)_CC_Night));
                float pulse = 1.0 - _Pulse * (0.5 + 0.5 * sin(_Time.y * 1.3));
                if (i.info.x > 0.5)
                {
                    float d = length(i.info.yz);
                    float g = saturate(1.0 - d);
                    g = g * g * g;
                    return half4(i.tint * g * _Halo * night * pulse, 0);
                }
                float3 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                return half4(c * _Glow * night * pulse, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
