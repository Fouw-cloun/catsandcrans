// CatCooks — СВЕТ ПРОПСОВ (CityPropGlow): фонари, подсветка щитов, мигалки антенн и вертолётной площадки.
// Никаких настоящих источников света — только прибавочные (additive) кусочки одного меша на пропс = 1 вызов отрисовки:
//   лампа (яркое ядро), ореол к камере, конус света вниз, пятно света на земле, «заливка» лица щита.
// Данные в вершинах:
//   color — цвет (a — сила);  uv0 — координаты для спада (-1..1, у конусов/заливки y 0..1 — 1 сверху);
//   uv1 — x тип спада (0 мягкий круг, 1 ядро, 2 градиент сверху вниз), y только ночью (1) / всегда (0), z мигание (Гц, 0 — нет), w фаза;
//   uv2 — x размер ореола к камере (ед.; 0 — обычный кусок меша), y доля «горит» при мигании, z 1 — «только вблизи»
//         (пятна на земле, конусы: плавно гаснут к _CC_GlowFar.y, дальше их выключает CityGlowBatcher).
// Ночь — _CC_Night (CityAtmosphere; в тёмную погоду тоже растёт — фонари зажигаются в грозу).
Shader "CatCooks/City/Prop Glow"
{
    Properties
    {
        _Intensity ("Сила", Range(0, 4)) = 1
        _DayK ("«Всегда» днём (доля)", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+4" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "PropGlow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Intensity, _DayK;
            CBUFFER_END
            half _CC_Night;
            float4 _CC_GlowFar;    // x — начало угасания «ближних» кусков (ед.), y — конец (0 — не гасить)

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 p : TEXCOORD1;
                float4 b : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float4 p : TEXCOORD2;
                float duty : TEXCOORD3;
                float fog : TEXCOORD4;
                half fade : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                if (v.b.x > 0.0)                                                      // ореол: квад к камере вокруг точки
                {
                    float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                    float3 toCam = normalize(_WorldSpaceCameraPos - ws);
                    ws += (right * v.uv.x + up * v.uv.y) * v.b.x + toCam * v.b.x * 0.6;   // чуть к камере — не режется своей моделью
                }
                o.positionCS = TransformWorldToHClip(ws);
                o.color = v.color;
                o.uv = v.uv;
                o.p = v.p;
                o.duty = v.b.y > 0.0 ? v.b.y : 0.4;
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.fade = 1.0h;
                if (v.b.z > 0.5 && _CC_GlowFar.y > _CC_GlowFar.x)
                    o.fade = (half)(1.0 - smoothstep(_CC_GlowFar.x, _CC_GlowFar.y, distance(ws, _WorldSpaceCameraPos)));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half f;
                if (i.p.x < 0.5) { half r = (half)length(i.uv); f = saturate(1.0h - r); f *= f; }                 // мягкий круг
                else if (i.p.x < 1.5) { half r = (half)length(i.uv); f = smoothstep(1.0h, 0.35h, r); }             // ядро лампы
                else { f = pow(saturate((half)i.uv.y), 1.6h) * (1.0h - pow(saturate(abs((half)i.uv.x)), 4.0h)); } // сверху вниз
                half night = saturate(_CC_Night);
                half k = i.p.y > 0.5 ? smoothstep(0.12h, 0.55h, night) : lerp(_DayK, 1.0h, night);
                if (i.p.z > 0.0)                                                     // мигание
                {
                    float ph = frac(_Time.y * i.p.z + i.p.w);
                    k *= lerp(0.06h, 1.0h, (half)(smoothstep(0.0, 0.05, ph) * (1.0 - smoothstep(i.duty, i.duty + 0.08, ph))));
                }
                half3 col = i.color.rgb * i.color.a * f * k * _Intensity * i.fade;
                col = MixFogColor(col, half3(0, 0, 0), i.fog);
                return half4(col, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
