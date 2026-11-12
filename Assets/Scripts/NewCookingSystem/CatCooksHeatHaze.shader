// ============================================================================
//  CatCooks / Heat Haze
//
//  Марево над горячим: прямоугольник (низ — у горячего, верх — тает) берёт картинку
//  сцены за собой (_CameraOpaqueTexture) и колышет её шумом, бегущим вверх.
//  Нужен Opaque Texture в URP Asset (меню CatCooks → Огонь и жар → Включить Opaque Texture).
//  Очередь 2510: рисуется ДО остальной прозрачности (пламя, жидкость, частицы),
//  поэтому их не «стирает» — колышется только непрозрачная сцена.
//  Сила — _Strength (ставит CCHeatFx по температуре), края мягкие.
// ============================================================================
Shader "CatCooks/Heat Haze"
{
    Properties
    {
        _Strength ("Сила", Range(0, 0.05)) = 0.012
        _Freq ("Размер волн", Range(1, 30)) = 9
        _Speed ("Скорость вверх", Range(0, 6)) = 1.6
        _Tint ("Лёгкий оттенок (альфа — сила)", Color) = (1, 0.97, 0.9, 0.0)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-490" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        ZWrite Off
        ZTest LEqual
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "HeatHaze"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Strength;
                float _Freq, _Speed;
                half4 _Tint;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screen : TEXCOORD1;
                float distK : TEXCOORD2;
                float3 wp : TEXCOORD3;
            };

            float hash31(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float vnoise(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash31(i + float3(0, 0, 0)), hash31(i + float3(1, 0, 0)), f.x),
                                 lerp(hash31(i + float3(0, 1, 0)), hash31(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(hash31(i + float3(0, 0, 1)), hash31(i + float3(1, 0, 1)), f.x),
                                 lerp(hash31(i + float3(0, 1, 1)), hash31(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            V vert(A v)
            {
                V o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.wp = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.screen = ComputeScreenPos(o.positionCS);
                o.uv = v.uv;
                // дальше — слабее на экране (иначе издалека «кипит» весь кадр)
                float d = length(_WorldSpaceCameraPos - ws);
                o.distK = 1.0 / max(0.6, d);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float2 uv = i.uv;
                // маска: мягкие бока, снизу проявляется, кверху тает
                half m = smoothstep(0.0, 0.28, uv.x) * smoothstep(1.0, 0.72, uv.x);
                m *= smoothstep(0.0, 0.12, uv.y) * pow(saturate(1.0 - uv.y), 1.3);
                float time = _Time.y;
                float3 p = float3(uv.x * _Freq * 0.6, uv.y * _Freq - time * _Speed, time * 0.35);
                float2 n = float2(vnoise(p), vnoise(p + float3(17.3, 5.1, 3.7))) - 0.5;
                n += (float2(vnoise(p * 2.3 + 11.0), vnoise(p * 2.3 + 29.0)) - 0.5) * 0.5;
                float2 suv = i.screen.xy / i.screen.w;
                float2 off = n * _Strength * m * i.distK * 2.0;
                half3 scene = SampleSceneColor(suv + off);
                scene = lerp(scene, scene * _Tint.rgb, _Tint.a * m);
                return half4(scene, saturate(m * 1.4));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
