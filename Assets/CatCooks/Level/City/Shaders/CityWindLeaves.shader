// CatCooks — ЛИСТЬЯ, КОТОРЫЕ ПАДАЮТ НА ПОЛ: влетают сверху по ветру, планируют маятником, ложатся на пол
// и лежат (в порыв подрагивают и съезжают), потом тают. Чем ветренее погода — тем больше (_CC_WindFx.z).
// Листопады: осенние цвета (+ красный) или летние зелёные; летние не ложатся — долго кружат и тают в воздухе.
Shader "CatCooks/City/Wind Leaves"
{
    Properties
    {
        _Size ("Размер, м", Range(0.05, 0.4)) = 0.13
        _Leaf1 ("Зелёный", Color) = (0.5, 0.66, 0.24, 1)
        _Leaf2 ("Жёлтый", Color) = (0.95, 0.78, 0.25, 1)
        _Leaf3 ("Оранжевый", Color) = (0.92, 0.48, 0.18, 1)
        _Leaf4 ("Бурый", Color) = (0.55, 0.32, 0.18, 1)
        _LeafRed ("Осень: красный", Color) = (0.82, 0.24, 0.16, 1)
        _Summer1 ("Лето: светло-зелёный", Color) = (0.62, 0.84, 0.3, 1)
        _Summer2 ("Лето: зелёный", Color) = (0.36, 0.66, 0.24, 1)
        _Summer3 ("Лето: тёмно-зелёный", Color) = (0.22, 0.48, 0.2, 1)
        _Period ("Цикл листа (с): полёт + лежит", Range(6, 40)) = 18
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+11" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "WindLeaves"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -1, -1
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
                float _Size, _Period;
                float4 _Leaf1, _Leaf2, _Leaf3, _Leaf4, _LeafRed, _Summer1, _Summer2, _Summer3;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 info : TEXCOORD1;   // x оттенок, y свет грани, z лежит (1), w видимость
            };

            static const float2 kCorner[6] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 0), float2(1, 1), float2(0, 1) };

            Varyings vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                o.positionCS = float4(2, 2, 2, 1);
                float strength = _CC_WindFx.z;
                if (strength < 0.01) return o;
                float P = max(_Period, 4.0);
                float tt = _Time.y / P + CR_Hash11(iid * 1.93);
                float cyc = floor(tt), f = frac(tt);
                float3 s = CR_Hash32(float2(iid * 2.11, cyc * 1.37));
                float g = CW_Gate(strength, CR_Hash11(iid * 0.37 + cyc * 1.1));
                if (g < 0.01) return o;

                float m = _CC_RainMeter;
                float3 dir = CW_Dir();
                float3 perp = float3(-dir.z, 0, dir.x);
                float R = _CC_GustP.x * 1.1;
                float windy = saturate(_CC_WindFx.y);
                float lieOK = _CC_WindFx2.w;
                // ложатся — короткий полёт и долго лежат; не ложатся (лето) — долго кружат в воздухе
                float flyF = lerp(0.55 + 0.3 * s.z, 0.16 + 0.12 * s.z, lieOK);
                float flyT = flyF * P;
                float tf = f * P;
                float drift = flyT * (0.8 + 2.2 * windy) * m;
                float2 xzL = _CC_GustFocus.xz + perp.xz * (s.x - 0.5) * 2.0 * R + dir.xz * (s.y - 0.5) * 2.0 * R;   // где ляжет
                float2 xz0 = xzL - dir.xz * drift;
                float yL = CW_Floor(xzL) + 0.012 * m;
                float y0 = yL + (3.0 + 5.0 * s.z) * m;
                float t = _Time.y;
                float3 pos; float3x3 rot; float lying;
                if (tf < flyT)
                {
                    // полёт: вниз с маятником (вбок-вверх), кувырок-трепет
                    float u = tf / flyT;
                    float2 xz = lerp(xz0, xzL, u) + perp.xz * sin(u * 3.14159 * 4.0 + s.x * 6.28) * 0.6 * m;
                    float y = lerp(y0, yL, u * u * (3.0 - 2.0 * u) * 0.3 + u * 0.7) + abs(sin(u * 3.14159 * 4.0 + s.x * 6.28)) * 0.25 * m;
                    pos = float3(xz.x, max(y, yL), xz.y);
                    rot = mul(CW_RotAxis(dir, sin(t * 5.0 + s.y * 6.28) * 1.2), CW_RotAxis(float3(0, 1, 0), t * (1.5 + s.x * 2.0) + s.z * 6.28));
                    lying = 0;
                }
                else
                {
                    // лежит на полу: плоско, в порыв подрагивает и чуть съезжает по ветру
                    float gust = _CC_Gust.x;
                    float2 sk = dir.xz * gust * 0.35 * m * (0.5 + 0.5 * sin(t * 3.0 + s.x * 6.28)) + perp.xz * gust * 0.08 * m * sin(t * 7.0 + s.y * 6.28);
                    pos = float3(xzL.x + sk.x, yL, xzL.y + sk.y);
                    float flip = gust * 0.35 * sin(t * 9.0 + s.z * 6.28) * step(0.6, s.y);   // края приподнимаются
                    rot = mul(CW_RotAxis(float3(0, 1, 0), s.z * 6.28 + s.x * 2.0), CW_RotAxis(float3(1, 0, 0), 1.5708 + flip));
                    lying = 1;
                }
                float2 c = kCorner[vid % 6];
                float sz = _Size * m * (0.75 + 0.5 * s.y);
                float3 lp = mul(rot, float3((c.x - 0.5) * sz, (c.y - 0.5) * sz * 1.25, 0));
                float3 nrm = mul(rot, float3(0, 0, 1));
                Light ml = GetMainLight();
                o.positionCS = TransformWorldToHClip(pos + lp);
                o.uv = c;
                float fade = smoothstep(0.0, 0.02, f) * (1.0 - smoothstep(0.86, 1.0, f));
                if (lieOK < 0.5) fade *= 1.0 - smoothstep(flyF - 0.06, flyF, f);            // не ложатся — тают у пола
                o.info = float4(CR_Hash11(iid * 5.3 + cyc), abs(dot(nrm, ml.direction)), lying, g * fade);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                // лист: миндалина с черешком и прожилкой
                float w = 1.0 - abs(p.y) * 0.55;
                float d = length(float2(p.x * 1.9 / max(w, 0.2), p.y * 1.02)) - w * 0.98;
                float stem = max(abs(p.x) - 0.04, max(-p.y - 1.0, p.y + 0.8));
                d = min(d, stem);
                float a = CR_AAStep(0.0, -d);
                if (a < 0.01) discard;
                float h = i.info.x;
                float3 base = h < 0.25 ? _Leaf1.rgb : (h < 0.5 ? _Leaf2.rgb : (h < 0.78 ? _Leaf3.rgb : _Leaf4.rgb));
                float3 autumn = h < 0.3 ? _Leaf2.rgb : (h < 0.58 ? _Leaf3.rgb : (h < 0.8 ? _LeafRed.rgb : _Leaf4.rgb));
                float3 summer = h < 0.35 ? _Summer1.rgb : (h < 0.72 ? _Summer2.rgb : _Summer3.rgb);
                base = lerp(base, autumn, saturate(_CC_WindFx2.y));
                base = lerp(base, summer, saturate(_CC_WindFx2.z));
                base *= 1.0 - 0.22 * CR_AAStep(0.0, 0.05 - abs(p.x)) * step(p.y, 0.8);   // прожилка
                float rim = CR_AAStep(0.0, d + 0.12);
                Light ml = GetMainLight();
                float3 amb = max(SampleSH(float3(0, 1, 0)), 0.0);
                float lit = step(0.35, i.info.y);
                float3 col = base * (amb * 0.95 + ml.color * lerp(0.25, 0.8, lit)) * lerp(1.0, 0.75, rim);
                col *= lerp(1.0, 0.88, i.info.z);                                         // лежащие чуть темнее (тень от пола)
                return half4(col, a * i.info.w);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
