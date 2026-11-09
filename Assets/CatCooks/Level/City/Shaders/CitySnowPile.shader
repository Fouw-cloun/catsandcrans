// CatCooks — СУГРОБ-КУЧКА (настоящая 3D-модель, растёт в метель; спавнит CityWinter).
// Цвет и рисунок — как у снега на полу (кучка вырастает из слоя без шва), форма: светлый склон к солнцу, голубая тень.
// Рисуется ПОСЛЕ экранного прохода снега (очередь Transparent-50, но непрозрачно и с записью глубины) —
// поэтому следы-лапки и снежный слой пола на кучку не ложатся, а под кучкой скрыты.
Shader "CatCooks/City/Snow Pile"
{
    Properties
    {
        _Color ("Цвет снега", Color) = (0.95, 0.97, 1, 1)
        _ShadowColor ("Цвет тени", Color) = (0.62, 0.74, 0.95, 1)
        _Rim ("Холодная кайма", Range(0, 1)) = 0.35
        _Sparkle ("(не используется)", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-50" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "SnowPile"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            ZTest LEqual
            Cull Back
            Blend Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CityRain.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _ShadowColor;
                float _Rim, _Sparkle;
            CBUFFER_END
            TEXTURE2D(_CC_SnowToon); SAMPLER(sampler_CC_SnowToon);   // тот же рисунок, что у снега на полу
            float4 _CC_SnowTex;                                      // x тайл (ед.), y размер, z есть текстура, w барханы 0..1
            float4 _CC_Amb;                                          // то же окружение, что у снега на полу

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float yOS : TEXCOORD2;     // высота в кучке 0..1 (у пола — 0)
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.yOS = v.positionOS.y;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetCameraPositionWS() - i.positionWS);
                Light ml = GetMainLight();
                float3 L = ml.direction;
                float3 amb = _CC_Amb.w > 0.5 ? _CC_Amb.rgb : max(SampleSH(float3(0, 1, 0)), 0.0);
                // те же цвета, что у снега на полу (City Rain Surface) — кучка вырастает из слоя без шва
                float sun = saturate(L.y * 1.3 + 0.15);
                float3 litC = _Color.rgb * (amb * 1.15 + ml.color * 0.8 * sun);
                float3 shC = _Color.rgb * amb * _ShadowColor.rgb * 1.35;
                float sShade = 0.93;
                if (_CC_SnowTex.z > 0.5)
                {
                    float4 st = SAMPLE_TEXTURE2D(_CC_SnowToon, sampler_CC_SnowToon, i.positionWS.xz / max(_CC_SnowTex.x, 1e-3));
                    sShade = lerp(0.93, st.g, _CC_SnowTex.w);
                }
                float3 floorCol = lerp(shC, litC, sShade);
                // форма кучки: склон к солнцу светлее, от солнца — голубая тень; в пасмурно — свет неба сверху
                float rel = dot(n, L) - L.y;
                float skyT = saturate(n.y * 0.6 + 0.4);
                float shape = saturate(0.8 + rel * 1.8) * lerp(0.7, 1.0, skyT);
                shape = smoothstep(0.0, 1.0, shape) * lerp(1.0, sShade, 0.5);
                float3 pileCol = lerp(shC, litC, shape);
                pileCol += pow(1.0 - saturate(dot(n, v)), 3.0) * _Rim * (amb * 0.5 + 0.03) * float3(0.85, 0.92, 1.0);
                float f = smoothstep(0.0, 0.35, i.yOS);                                       // у основания — цвет пола
                float3 col = lerp(floorCol, pileCol, f);
                col += _CC_Flash.rgb * _CC_Flash.a * 0.5;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
