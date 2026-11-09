// CatCooks — МУСОР В ПОРЫВ: листья и бумажки кувыркаются по ветру вокруг точки взгляда камеры.
// Без частиц: N квадов, полёт и кувырок в вершинном шейдере. Сколько — от силы порыва (_CC_Gust.x).
Shader "CatCooks/City/Wind Debris"
{
    Properties
    {
        _Size ("Размер, м", Range(0.03, 0.4)) = 0.14
        _Leaf1 ("Лист 1", Color) = (0.55, 0.68, 0.25, 1)
        _Leaf2 ("Лист 2", Color) = (0.9, 0.55, 0.2, 1)
        _Paper ("Бумажка", Color) = (0.95, 0.93, 0.86, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+17" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "WindDebris"
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

            CBUFFER_START(UnityPerMaterial)
                float _Size;
                float4 _Leaf1, _Leaf2, _Paper;
            CBUFFER_END
            #include "CityWind.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 info : TEXCOORD1;   // x тип (0 лист, 1 бумажка), y оттенок, z свет грани, w гаснет
            };

            static const float2 kCorner[6] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 0), float2(1, 1), float2(0, 1) };

            float3x3 RotAxis(float3 a, float ang)
            {
                float s = sin(ang), c = cos(ang), t = 1.0 - c;
                return float3x3(t * a.x * a.x + c, t * a.x * a.y - s * a.z, t * a.x * a.z + s * a.y,
                                t * a.x * a.y + s * a.z, t * a.y * a.y + c, t * a.y * a.z - s * a.x,
                                t * a.x * a.z - s * a.y, t * a.y * a.z + s * a.x, t * a.z * a.z + c);
            }

            Varyings vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                o.positionCS = float4(2, 2, 2, 1);
                float strength = _CC_Gust.x;
                if (strength < 0.01) return o;
                float life = 2.6;
                float tt = _Time.y / life + CR_Hash11(iid * 5.31);
                float cyc = floor(tt), f = frac(tt);
                float4 s = float4(CR_Hash32(float2(iid * 2.3, cyc * 1.9)), CR_Hash11(iid * 4.1 + cyc * 7.7));

                float m = _CC_RainMeter;
                float3 dir = normalize(float3(_CC_Gust.y, 0, _CC_Gust.z) + float3(1e-4, 0, 0));
                float3 perp = float3(-dir.z, 0, dir.x);
                float3 fc = _CC_GustFocus.xyz;
                float R = _CC_GustP.x;
                float run = 14.0 * m;                                                        // пролетает за жизнь
                float2 xz = fc.xz + (perp.xz * (s.x - 0.5) + dir.xz * (s.z - 0.5)) * 2.0 * R - dir.xz * run * 0.5;
                if (CR_Hash11(iid * 0.71 + cyc * 2.39) > strength * CW_Wave(xz + dir.xz * run * 0.3)) return o;   // волна порыва
                float y0 = CW_Floor(xz);
                // полёт: вдоль ветра, подскоки и снижение, виляет вбок
                float3 p = float3(xz.x, y0, xz.y) + dir * f * run * (0.7 + 0.6 * s.y)
                         + perp * sin(f * 6.0 + s.w * 6.28) * 0.6 * m
                         + float3(0, 1, 0) * ((0.2 + 2.2 * s.w) * m + abs(sin(f * (3.0 + 4.0 * s.x) + s.z * 6.28)) * 0.9 * m);
                // кувырок вокруг случайной оси
                float3 ax = normalize(CR_Hash32(float2(iid * 0.9, 3.3)) - 0.5 + 1e-3);
                float3x3 rot = RotAxis(ax, _Time.y * (4.0 + 6.0 * s.x) + s.y * 6.28);
                float2 c = kCorner[vid % 6];
                bool paper = s.w > 0.72;
                float sz = _Size * m * (paper ? 1.4 : 1.0) * (0.7 + 0.6 * s.z);
                float3 lp = mul(rot, float3((c.x - 0.5) * sz, 0, (c.y - 0.5) * sz * (paper ? 0.75 : 1.0)));
                float3 nrm = mul(rot, float3(0, 1, 0));
                p += lp;
                Light ml = GetMainLight();
                o.positionCS = TransformWorldToHClip(p);
                o.uv = c;
                o.info = float4(paper ? 1 : 0, s.y, abs(dot(nrm, ml.direction)), smoothstep(0.0, 0.1, f) * (1.0 - smoothstep(0.85, 1.0, f)));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float a;
                float3 base;
                if (i.info.x < 0.5)
                {
                    // лист: миндалина с прожилкой
                    float d = length(float2(p.x * 1.7, p.y));
                    float w = 1.0 - abs(p.y) * 0.55;
                    a = CR_AAStep(0.0, w - length(float2(p.x * 1.9 / max(w, 0.2), p.y)) * 0.98);
                    base = lerp(_Leaf1.rgb, _Leaf2.rgb, i.info.y);
                    base *= 1.0 - 0.25 * CR_AAStep(0.0, 0.06 - abs(p.x));
                }
                else
                {
                    // бумажка: прямоугольник с загнутым уголком
                    a = CR_AAStep(0.0, 0.92 - max(abs(p.x), abs(p.y)));
                    base = _Paper.rgb * (1.0 - 0.2 * step(0.55, p.x + p.y));
                }
                if (a < 0.01) discard;
                float3 amb = max(SampleSH(float3(0, 1, 0)), 0.0);
                Light ml = GetMainLight();
                float lit = step(0.35, i.info.z);
                float3 col = base * (amb * 0.9 + ml.color * lerp(0.25, 0.85, lit));
                return half4(col, a * i.info.w);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
