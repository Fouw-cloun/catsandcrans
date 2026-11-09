// ============================================================================
//  CatCooks/Liquid Stream — мультяшная струя (Unity 6 / URP)
// ----------------------------------------------------------------------------
//  Приёмы из стилизованных водопадов (Blender/Shader Graph):
//   • цвет ступеньками: тёмный край → основной → светлые прожилки → белая пена;
//   • прожилки — шум, растянутый ВДОЛЬ струи и бегущий вниз (uv.y = длина, м);
//   • пена у кромки, откуда льётся, и внизу, где струя бьёт;
//   • рваный край «простыни» и хвост, который рвётся на куски (alpha clip шумом);
//   • поверхность дрожит (смещение вершин шумом);
//   • блик — узкая полоса вдоль струи, свет от солнца двумя тонами.
//
//  Входы меша (их пишет LiquidStreamRibbon):
//   uv0.x — обхват (0..1), uv0.y — длина по струе, м
//   uv1.x — доля длины 0 (кромка) .. 1 (конец), uv1.y — «простыня» 0..1 (широкий слив с края)
//   uv2.x — радиус в этой точке, м (для дрожания), uv2.y — 1 у места падения
//   color — не используется для цвета (цвет — свойства), alpha — видимость
// ============================================================================
Shader "CatCooks/Liquid Stream"
{
    Properties
    {
        _Deep ("Тёмный (край)", Color) = (0.10, 0.45, 0.55, 1)
        _Mid ("Основной", Color) = (0.25, 0.70, 0.78, 1)
        _Light ("Светлые прожилки", Color) = (0.65, 0.92, 0.95, 1)
        _FoamColor ("Пена", Color) = (0.97, 1, 1, 1)
        _Opacity ("Непрозрачность", Range(0, 1)) = 0.9

        _FlowSpeed ("Скорость прожилок", Float) = 2.2
        _StreakScale ("Прожилки: поперёк (x), вдоль (y)", Vector) = (3, 10, 0, 0)
        _StreakAmount ("Прожилок и пены", Range(0, 1)) = 0.55
        _EdgeDark ("Тёмный край (ширина)", Range(0, 1)) = 0.45
        _CoreLight ("Светлая сердцевина", Range(0, 1)) = 0.35
        _LipFoam ("Пена у кромки (доля длины)", Range(0, 0.5)) = 0.08
        _BottomFoam ("Пена внизу (доля длины)", Range(0, 0.5)) = 0.12
        _BreakUp ("Хвост рвётся", Range(0, 1)) = 0.5
        _Wobble ("Дрожание (доля радиуса)", Range(0, 0.6)) = 0.18
        _Gloss ("Блик-полоса", Range(0, 2)) = 0.8
        _ShadeStrength ("Тень от солнца", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Deep, _Mid, _Light, _FoamColor;
                half  _Opacity;
                float _FlowSpeed;
                float4 _StreakScale;
                half  _StreakAmount, _EdgeDark, _CoreLight, _LipFoam, _BottomFoam, _BreakUp, _Wobble, _Gloss, _ShadeStrength;
            CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Stream"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct A
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                float2 uv0        : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
                float2 uv2        : TEXCOORD2;
            };
            struct V
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 uv         : TEXCOORD2;   // xy — обхват/длина, zw — доля/простыня
                float2 ex         : TEXCOORD3;   // радиус, падение
                half   alpha      : TEXCOORD4;
                half   fog        : TEXCOORD5;
            };

            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), f.x),
                            lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), f.x), f.y);
            }
            // шум, бесшовный по обхвату (period — число ячеек вокруг)
            float vnoiseWrap(float2 p, float period)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                float2 i1 = i + 1;
                i.x = fmod(i.x + period * 64, period); i1.x = fmod(i1.x + period * 64, period);
                return lerp(lerp(hash21(i), hash21(float2(i1.x, i.y)), f.x),
                            lerp(hash21(float2(i.x, i1.y)), hash21(i1), f.x), f.y);
            }

            V vert(A v)
            {
                V o;
                float t = _Time.y;
                // дрожание: шум бежит вдоль струи
                float n = vnoiseWrap(float2(v.uv0.x * 6, v.uv0.y * 9 - t * _FlowSpeed * 1.3), 6) - 0.5;
                float3 pos = v.positionOS.xyz + v.normalOS * n * _Wobble * 2 * v.uv2.x;
                VertexPositionInputs p = GetVertexPositionInputs(pos);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = float4(v.uv0, v.uv1);
                o.ex = v.uv2;
                o.alpha = v.color.a;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(V i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float t = _Time.y;
                float around = i.uv.x, along = i.uv.y, frac01 = i.uv.z, sheet = i.uv.w;
                float3 N = normalize(i.normalWS) * IS_FRONT_VFACE(facing, 1.0, -1.0);
                float3 Vd = normalize(GetWorldSpaceViewDir(i.positionWS));
                half ndv = saturate(abs(dot(N, Vd)));

                // ── прожилки: шум, растянутый вдоль и бегущий вниз ──
                float cellsAround = max(2, round(_StreakScale.x * 2));
                float2 fp = float2(around * cellsAround, along * _StreakScale.y - t * _FlowSpeed);
                float s1 = vnoiseWrap(fp, cellsAround);
                float s2 = vnoiseWrap(fp * float2(2, 1.7) + float2(0, 3.1), cellsAround * 2);
                float streak = saturate((s1 * 0.65 + s2 * 0.35 - 0.5) * 1.7 + 0.5);   // контраст: чёткие прожилки

                // ── ступеньки тона ──
                half3 col = _Mid.rgb;
                // тёмный край (силуэт струи) — как контур, но цветной
                half edge = 1 - ndv;
                col = lerp(col, _Deep.rgb, step(1 - _EdgeDark * 0.7, edge + (streak - 0.5) * 0.25));
                // светлая сердцевина — где смотрим «в лоб»
                col = lerp(col, _Light.rgb, step(1 - _CoreLight * 0.6, ndv + (streak - 0.5) * 0.3) * 0.9);
                // прожилки
                half thrL = lerp(0.82, 0.5, _StreakAmount);
                col = lerp(col, _Light.rgb, step(thrL, streak));
                half thrF = lerp(0.95, 0.68, _StreakAmount);
                col = lerp(col, _FoamColor.rgb, step(thrF, streak));

                // ── пена у кромки и внизу (рваная граница) ──
                float fn = vnoiseWrap(float2(around * 10, along * 6 - t * _FlowSpeed * 0.6), 10);
                half lip = step(frac01, _LipFoam * (0.6 + fn * 0.8));
                half bot = step(1 - _BottomFoam * (0.6 + fn * 0.8), frac01) + step(0.5, i.ex.y) * step(0.55, fn);
                col = lerp(col, _FoamColor.rgb, saturate(lip + bot));

                // ── свет: два тона + блик-полоса ──
                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = dot(N, L.direction);
                half lit = step(0, ndl) * lerp(1, L.shadowAttenuation, 0.6);
                col *= lerp(1 - _ShadeStrength, 1, lit);
                col *= lerp(half3(1, 1, 1), L.color, 0.6);
                half3 Hh = normalize(L.direction + Vd);
                half spec = step(0.93, pow(saturate(dot(N, Hh)), 6) + (streak - 0.5) * 0.15) * _Gloss;
                col += spec * 0.5 * L.color;
                col += SampleSH(N) * _Mid.rgb * 0.25;

                // ── прозрачность и рваные края ──
                half a = _Opacity * i.alpha;
                a = lerp(a, 1, saturate(lip + bot) * 0.8);           // пена непрозрачная
                a = lerp(a, 1, edge * 0.35);                          // край плотнее (френель)
                // «простыня»: рваный боковой край
                float side = abs(cos(around * 6.2831853));            // 1 — боковые края листа (концы большой оси)
                float ragged = vnoise(float2(along * 14 - t * _FlowSpeed * 1.5, around * 3)) * 0.35;
                clip(1 - sheet * step(0.62 + ragged, side) - 0.5);
                // хвост рвётся на куски
                float tailStart = lerp(0.98, 0.55, _BreakUp);
                float tearN = vnoiseWrap(float2(around * 4, along * 7 - t * _FlowSpeed * 2), 4);
                clip(tearN - smoothstep(tailStart, 1.08, frac01) + 0.001);

                col = MixFog(col, i.fog);
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
