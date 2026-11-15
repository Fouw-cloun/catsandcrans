// v23.1: CatCooks / Boil Bubble — мультяшный пузырь кипения (куполок на поверхности жидкости).
// Цвет — цвет жидкости (MaterialPropertyBlock _BaseColor), тун-свет в две ступени, светлый ободок по краю
// и круглый блик. _Pop 0..1 — пузырь лопается: сверху открывается рваная дырка (шум) и съедает купол.
// Лежит в Resources — попадает в сборку сама. Без текстур, без прозрачности (alpha clip) — дёшево.
Shader "CatCooks/Boil Bubble"
{
    Properties
    {
        _BaseColor ("Цвет (ставит код)", Color) = (1, 1, 1, 1)
        _Pop ("Лопается 0..1 (ставит код)", Range(0, 1)) = 0
        _Rim ("Светлый ободок", Range(0, 2)) = 0.8
        _Spec ("Блик", Range(0, 2)) = 1.0
        _Shade ("Тень (тун)", Range(0, 1)) = 0.72
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Pass
        {
            Name "BoilBubble"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Pop;
                half _Rim;
                half _Spec;
                half _Shade;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            V vert(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            float Hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(Hash3(i), Hash3(i + float3(1, 0, 0)), f.x);
                float b = lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x);
                float c = lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x);
                float d = lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x);
                return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
            }

            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                // лопается: дырка от макушки (0, 0.5, 0) с рваным краем
                float d = length(i.positionOS - float3(0, 0.5, 0)) / 0.7071;
                float n = Noise3(i.positionOS * 9.0) * 0.3;
                clip(d + n - _Pop * 1.35 - 0.001);

                float3 N = normalize(i.normalWS);
                float3 Vd = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light L = GetMainLight();
                // v23.5: аниме-пузырь — плоская светлая заливка, тёмная обводка по краю, белый блик-«боб» и точка
                half3 baseC = _BaseColor.rgb;
                half warm = saturate((baseC.r - baseC.b) * 1.8);
                half3 shadowMul = lerp(half3(0.68, 0.72, 0.94), half3(0.8, 0.58, 0.5), warm);
                half ndl = dot(N, L.direction) * 0.5 + 0.5;
                half3 col = lerp(baseC * shadowMul, baseC, smoothstep(0.42, 0.48, ndl)) * lerp(half3(1, 1, 1), min(L.color, 1.2), 0.6);
                half fr = 1.0 - saturate(dot(N, Vd));
                half outline = smoothstep(0.62, 0.68, fr);
                col = lerp(col, baseC * shadowMul * 0.55, outline * _Rim);
                half3 Hh = normalize(L.direction + Vd);
                half spec = smoothstep(0.9, 0.915, dot(N, Hh));
                half dotS = smoothstep(0.985, 0.99, dot(N, normalize(Hh + float3(0.25, -0.1, 0.25))));
                col = lerp(col, half3(1, 1, 1), saturate(spec + dotS) * _Spec * 0.9);
                // край дырки — белая плёнка
                float edge = 1.0 - saturate((d + n - _Pop * 1.35) / 0.08);
                col = lerp(col, half3(1, 1, 1), edge * step(0.001, _Pop) * 0.8);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
