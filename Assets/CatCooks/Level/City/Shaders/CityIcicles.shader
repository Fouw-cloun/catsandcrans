// CatCooks — СОСУЛЬКИ на краях балок, навесов, зонтов (те же точки, что у капели: CityDripEdge + края зонтов).
// Растут в мороз (_CC_Icicles.x 0..1), у каждой своя длина и толщина, бугристые, полупрозрачный голубой лёд с бликом,
// на кончике иногда висит и блестит капля. Буфер точек — _CC_Drips (CityRainDrips у CityWinter).
Shader "CatCooks/City/Icicles"
{
    Properties
    {
        _Ice ("Лёд", Color) = (0.74, 0.88, 1, 1)
        _Edge ("Кромка", Color) = (0.45, 0.62, 0.85, 1)
        _Alpha ("Непрозрачность", Range(0, 1)) = 0.88
        _Share ("Доля точек с сосулькой", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+8" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Icicles"
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
                float4 _Ice, _Edge;
                float _Alpha, _Share;
            CBUFFER_END
            StructuredBuffer<float4> _CC_Drips;
            float4 _CC_Icicles;    // x рост 0..1, y макс. длина (ед.), z толщина (ед.), w капли на кончике (0..1)

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 info : TEXCOORD1;   // x вид (0 сосулька, 1 капля), y случайное, z блеск, w —
            };

            static const float2 kCorner[6] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 0), float2(1, 1), float2(0, 1) };

            Varyings vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                o.positionCS = float4(2, 2, 2, 1);
                float grow = _CC_Icicles.x;
                if (grow < 0.005) return o;
                float4 a = _CC_Drips[iid * 2];
                float seed = a.w;
                if (CR_Hash11(seed * 13.7) > _Share) return o;
                float3 p0 = a.xyz;
                float h1 = CR_Hash11(seed * 7.3), h2 = CR_Hash11(seed * 3.1), h3 = CR_Hash11(seed * 5.9);
                float gi = saturate(grow * 1.5 - h3 * 0.5);                          // растут не все сразу
                if (gi < 0.01) return o;
                float len = _CC_Icicles.y * (0.25 + 0.75 * pow(h1, 1.5)) * gi;
                float wid = _CC_Icicles.z * (0.6 + 0.8 * h2) * (0.55 + 0.45 * gi);
                float3 cam = GetCameraPositionWS();
                float3 V = normalize(cam - p0);
                float3 U = float3(0, 1, 0);
                float3 R = cross(U, V); float rl = length(R); R = rl > 1e-4 ? R / rl : float3(1, 0, 0);
                uint q = vid / 6;
                float2 c = kCorner[vid % 6];
                float3 wp;
                if (q == 0)
                {
                    wp = p0 + R * (c.x - 0.5) * 2.0 * wid - U * (1.0 - c.y) * len + U * 0.01 * _CC_RainMeter;
                    o.info = float4(0, h1, 0, 0);
                }
                else
                {
                    // капля на кончике: набухает и блестит, раз в несколько секунд
                    float per = 3.0 + 4.0 * h2;
                    float f = frac(_Time.y / per + h3);
                    if (_CC_Icicles.w < 0.01 || CR_Hash11(seed * 2.3) > _CC_Icicles.w || f > 0.85) return o;
                    float r = wid * 0.55 * smoothstep(0.0, 0.8, f);
                    float3 tip = p0 - U * (len + r * 0.8);
                    wp = tip + (R * (c.x - 0.5) + U * (c.y - 0.5)) * 2.0 * max(r, 1e-4);
                    o.info = float4(1, h1, f, 0);
                }
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = c;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                Light ml = GetMainLight();
                float3 amb = max(SampleSH(float3(0, 1, 0)), 0.0);
                float3 lit = amb * 1.1 + ml.color * 0.5 + _CC_Flash.rgb * _CC_Flash.a;
                if (i.info.x > 0.5)
                {
                    float d = length(p);
                    float a = CR_AAStep(0.0, 1.0 - d);
                    if (a < 0.01) discard;
                    float hl = CR_AAStep(0.0, 0.3 - length(p - float2(-0.3, 0.3)));
                    return half4(_Ice.rgb * lit + hl * (ml.color * 0.6 + 0.4), a * 0.9);
                }
                // сосулька: сужается к низу, бугорки, светлая полоса-блик, тёмная кромка
                float y = i.uv.y;                                       // 1 — у основания, 0 — кончик
                float bumps = 1.0 + 0.12 * sin(y * 26.0 + i.info.y * 40.0) * (1.0 - y * 0.5);
                float half_w = lerp(0.04, 1.0, pow(y, 0.85)) * bumps;
                float x = abs(p.x) / max(half_w, 1e-3);
                float a = CR_AAStep(0.0, 1.0 - x);
                if (a < 0.01) discard;
                float edge = CR_AAStep(0.0, x - 0.7);
                float hl = CR_AAStep(0.0, 0.18 - abs(p.x / max(half_w, 1e-3) + 0.35)) * (0.4 + 0.6 * y);
                float tw = step(0.97, CR_Hash11(floor(_Time.y * 3.0) + i.info.y * 91.0)) * step(0.3, y);
                float3 col = lerp(_Ice.rgb, _Edge.rgb, edge * 0.7) * lit + hl * (ml.color * 0.7 + 0.35) + tw * 0.8;
                return half4(col, a * _Alpha * lerp(0.75, 1.0, y));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
