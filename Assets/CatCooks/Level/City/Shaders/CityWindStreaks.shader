// CatCooks — ПОРЫВЫ ВЕТРА: пачки по 6 параллельных волнистых линий-«вжух», пролетают по ветру низко над полом,
// густеют в волне порыва (фронт бежит через зону), редкие пачки закручиваются петлёй.
// Без частиц: N лент по 32 звена, всё в вершинном шейдере. Сколько линий — от силы порыва (_CC_Gust.x).
// _CC_Gust: x порыв 0..1, yz направление ветра (XZ), w —; _CC_GustP: x радиус вокруг камеры, y длина линии,
// z толщина (ед.), w жизнь линии (с).
Shader "CatCooks/City/Wind Streaks"
{
    Properties
    {
        _Alpha ("Непрозрачность", Range(0, 1)) = 0.8
        _Color ("Цвет", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+18" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "WindStreaks"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CityRain.hlsl"

            #include "CityWind.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Alpha;
                float4 _Color;
            CBUFFER_END

            #define SEG 32
            #define LINES 6          // линий в пачке

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;      // x поперёк 0..1, y вдоль видимой части 0..1
                float fade : TEXCOORD1;
            };

            static const float2 kCorner[6] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 0), float2(1, 1), float2(0, 1) };

            // путь пачки: общий для всех её линий (поэтому они идут параллельно, волной)
            float3 PathAt(float u, float3 start, float3 dir, float3 perp, float4 s, float len, float li)
            {
                float amp = len * 0.05 * (0.5 + s.x);
                float3 p = start + dir * u * len
                         + perp * sin(u * 3.14159 * (1.0 + s.y) + s.z * 6.28) * amp
                         + float3(0, 1, 0) * sin(u * 6.28318 + s.x * 6.28) * amp * 0.35
                         + float3(0, 1, 0) * sin(u * 3.14159 * 7.0 + s.y * 6.28 + li * 0.45) * len * 0.012;   // мелкая рябь
                if (s.w > 0.8)
                {
                    float th = 6.28318 * smoothstep(0.42, 0.62, u);                   // завиток у редких пачек
                    float rl = len * 0.07;
                    p += (-dir * sin(th) + float3(0, 1, 0) * (1.0 - cos(th))) * rl;
                }
                return p;
            }

            Varyings vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                o.positionCS = float4(2, 2, 2, 1);
                float strength = _CC_Gust.x;
                if (strength < 0.01) return o;
                uint group = iid / LINES;
                float li = (float)(iid % LINES);
                float life = max(_CC_GustP.w, 0.3);
                float tt = _Time.y / life + CR_Hash11(group * 7.13);
                float cyc = floor(tt), f = frac(tt);
                float4 s = float4(CR_Hash32(float2(group * 1.7, cyc * 3.1)), CR_Hash11(group * 3.3 + cyc * 9.7));

                float3 dir = CW_Dir();
                float3 perp = float3(-dir.z, 0, dir.x);
                float3 fc = _CC_GustFocus.xyz;
                float R = _CC_GustP.x, len = _CC_GustP.y * (0.7 + 0.6 * s.y);
                float m = _CC_RainMeter;
                float2 xz = fc.xz + (perp.xz * (s.x - 0.5) + dir.xz * (s.z - 0.5)) * 2.0 * R - dir.xz * len * 0.5;
                if (CR_Hash11(group * 0.37 + cyc * 1.618) > strength * CW_Wave(xz + dir.xz * len * 0.5)) return o;   // волна порыва
                float lr = CR_Hash11(group * 13.1 + li * 7.7 + cyc * 3.3);
                if (lr < 0.18) return o;                                                // часть линий пропускаем — живее

                float spacing = 0.16 * m * (0.7 + 0.6 * s.x);
                float offs = (li - (LINES - 1) * 0.5) * spacing;
                float3 start = float3(xz.x, CW_Floor(xz) + (0.5 + 3.0 * s.w * s.w) * m, xz.y) + float3(0, offs, 0) + perp * offs * 0.35;
                float lenL = len * (0.85 + 0.3 * lr);

                // видимая часть бежит вдоль пути; линии пачки чуть разъезжаются по времени
                float head = f * 1.45 - lr * 0.12, tail = head - 0.32 - 0.15 * lr;
                uint seg = vid / 6;
                float2 c = kCorner[vid % 6];
                float k = (seg + c.y) / SEG;
                float u = lerp(tail, head, k);
                if (u < 0.0 || u > 1.0) return o;
                float3 p = PathAt(u, start, dir, perp, s, lenL, li);
                float3 pn = PathAt(min(u + 0.01, 1.0), start, dir, perp, s, lenL, li);
                float3 pp = PathAt(max(u - 0.01, 0.0), start, dir, perp, s, lenL, li);
                float3 cam = GetCameraPositionWS();
                float3 T = normalize(pn - pp + 1e-5);
                float3 V = normalize(cam - p);
                float3 S = cross(T, V);
                float sl = length(S);
                S = sl > 1e-4 ? S / sl : perp;
                float taper = sin(saturate(k) * 3.14159);
                float w = _CC_GustP.z * (0.5 + 0.7 * lr) * taper;
                p += S * (c.x - 0.5) * 2.0 * w;
                o.positionCS = TransformWorldToHClip(p);
                o.uv = float2(c.x, k);
                o.fade = smoothstep(0.0, 0.12, f) * (1.0 - smoothstep(0.75, 1.0, f)) * saturate(strength * 1.5);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float x = abs(i.uv.x - 0.5) * 2.0;
                float a = CR_AAStep(0.0, 0.85 - x);
                if (a < 0.01) discard;
                float3 amb = max(SampleSH(float3(0, 1, 0)), 0.0);
                Light ml = GetMainLight();
                float3 col = _Color.rgb * (amb * 0.7 + ml.color * 0.6 + 0.35);
                return half4(col, a * _Alpha * i.fade);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
