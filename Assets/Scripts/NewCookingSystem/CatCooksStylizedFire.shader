// ============================================================================
//  CatCooks / Stylized Fire
//
//  Огонёк «как в Zelda» — частица-язык пламени (вертикальный билборд). Текстура не нужна:
//  форма — капля (круглый низ, язык кверху), всё рисуется шумом:
//    • UV колышется шумом, бегущим вверх (язык виляет);
//    • ЭРОЗИЯ: по ходу жизни частицы в языке прогрызаются дыры по шуму (сверху раньше),
//      края дыр — тёмно-оранжевая кайма (как в ролике), в конце язык рассыпается;
//    • цвет снизу оранжевый, кверху светло-жёлтый, середина ярче (ядро).
//  Цвет вершины частицы (ставит CCFireFx): R — зерно шума, G — яркость, A — 1 − «износ»
//  (Color over Lifetime гонит A от 1 к 0 — это и есть эрозия, а не прозрачность).
// ============================================================================
Shader "CatCooks/Stylized Fire"
{
    Properties
    {
        [HDR] _Bottom ("Низ (оранжевый)", Color) = (1, 0.38, 0.06, 1)
        [HDR] _Top ("Верх (светло-жёлтый)", Color) = (1, 0.88, 0.5, 1)
        [HDR] _Edge ("Кайма дыр", Color) = (0.95, 0.22, 0.03, 1)
        _Glow ("Яркость", Range(0.5, 4)) = 1.35
        _Distort ("Колыхание", Range(0, 0.4)) = 0.16
        _ErodeScale ("Размер дыр", Range(1, 12)) = 4.5
        _EdgeWidth ("Ширина каймы", Range(0, 0.2)) = 0.07
        _Speed ("Скорость шума вверх", Range(0, 6)) = 2.2
        _Additive ("Свечение (0 — плотное, 1 — аддитив)", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        ZWrite Off
        Cull Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "StylizedFire"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Bottom, _Top, _Edge;
                half _Glow, _Distort, _ErodeScale, _EdgeWidth, _Speed, _Additive;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };

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
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            // ширина капли на высоте y (0..1): круглый низ, язык кверху
            float DropWidth(float y)
            {
                const float cy = 0.3, r = 0.3;
                if (y < cy) return sqrt(saturate(r * r - (y - cy) * (y - cy)));
                float k = saturate((y - cy) / (0.97 - cy));
                return r * pow(1.0 - k, 1.25);
            }

            half4 frag(V i) : SV_Target
            {
                float seed = i.color.r * 37.0;
                float life = 1.0 - i.color.a;
                float t = _Time.y;
                float2 uv = i.uv;
                // колыхание: сильнее кверху
                float dn = vnoise(float3(uv.x * 3.0 + seed, uv.y * 3.0 - t * _Speed, seed)) - 0.5;
                float dn2 = vnoise(float3(uv.x * 6.0 - seed, uv.y * 6.0 - t * _Speed * 1.6, seed + 5.0)) - 0.5;
                uv.x += (dn + dn2 * 0.5) * _Distort * (0.25 + uv.y);
                // форма
                float w = DropWidth(uv.y);
                float edgeD = w - abs(uv.x - 0.5);
                half shape = smoothstep(0.0, 0.015, edgeD);
                // эрозия: дыры растут по жизни, сверху раньше
                float e = vnoise(float3(uv * _ErodeScale + float2(0, -t * _Speed * 0.6), seed + 11.0)) * 0.7
                        + vnoise(float3(uv * _ErodeScale * 2.3 + float2(seed, -t * _Speed), seed + 3.0)) * 0.3;
                float th = life * 1.05 + uv.y * 0.45 * (0.25 + life) - 0.08;
                half a = shape * step(th, e);
                clip(a - 0.01);
                // цвет: низ оранжевый → верх светлый; ядро ярче; кайма дыр и края тёмно-оранжевая
                half3 col = lerp(_Bottom.rgb, _Top.rgb, smoothstep(0.15, 0.75, uv.y + dn * 0.2));
                col *= 1.0 + 0.35 * saturate(edgeD / 0.12);
                half rim = 1.0 - smoothstep(_EdgeWidth * 0.4, _EdgeWidth, e - th);
                half outer = 1.0 - smoothstep(0.0, 0.03, edgeD);
                col = lerp(col, _Edge.rgb, saturate(max(rim, outer * 0.7)));
                col *= _Glow * (0.6 + i.color.g * 0.8);
                return half4(col * a, a * (1.0 - _Additive));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
