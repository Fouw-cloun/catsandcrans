// CatCooks — КАПЕЛЬ С КРАЁВ (балки, навесы, зонты). Точки — буфер _CC_Drips (CityRainDrips): [2i] позиция+seed,
// [2i+1] x высота падения (минус — пола нет), y множитель частоты. Цикл капли: набухает на краю → срывается →
// падает с ускорением (вытягивается) → колечко на полу. Частота — от дождя, после дождя реже и затихает.
Shader "CatCooks/City/Rain Drips"
{
    Properties
    {
        _Alpha ("Непрозрачность капли", Range(0, 1)) = 0.85
        _RingAlpha ("Колечко на полу", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+16" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "RainDrips"
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
                float _Alpha, _RingAlpha;
            CBUFFER_END

            StructuredBuffer<float4> _CC_Drips;
            float4 _CC_Drip;     // x частота 0..1, y радиус капли (ед.), z дальность (ед.), w g (ед./с²)

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 info : TEXCOORD1;   // x тип (0 капля, 1 колечко), y вытянутость / прогресс кольца, z гаснет вдали, w случайное
                float3 view : TEXCOORD2;
            };

            static const float2 kCorner[6] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 0), float2(1, 1), float2(0, 1) };

            Varyings vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                o.positionCS = float4(2, 2, 2, 1);
                float4 a = _CC_Drips[iid * 2];
                float4 b = _CC_Drips[iid * 2 + 1];
                float rate = _CC_Drip.x * b.y;
                if (rate < 0.001) return o;

                float3 p0 = a.xyz;
                float seed = a.w;
                float3 cam = GetCameraPositionWS();
                float dist = length(p0 - cam);
                if (dist > _CC_Drip.z) return o;
                float fade = 1.0 - smoothstep(_CC_Drip.z * 0.7, _CC_Drip.z, dist);

                float m = _CC_RainMeter;
                float r = _CC_Drip.y;
                float g = max(_CC_Drip.w, 0.1);
                float fall = abs(b.x);
                bool floorHit = b.x > 0.0;
                float Tf = sqrt(2.0 * fall / g);
                const float splashT = 0.32;
                float P = lerp(3.4, 0.75, saturate(rate)) * (0.7 + 0.6 * CR_Hash11(seed * 31.7));
                P = max(P, Tf + splashT + 0.35);
                float tc = _Time.y / P + seed * 13.1;
                float cyc = floor(tc);
                float t = frac(tc) * P;
                if (CR_Hash11(seed * 71.3 + cyc * 1.618) > saturate(rate * 1.4)) return o;   // слабый дождь — капает не каждый раз
                float swellT = P - Tf - splashT;

                float3 toCam = cam - p0;
                float3 vd = toCam / max(length(toCam), 1e-4);
                uint q = vid / 6;
                float2 c = kCorner[vid % 6];
                float3 wp;
                if (q == 0)
                {
                    float3 ctr; float rad; float vel = 0;
                    if (t < swellT)
                    {
                        float s = t / swellT;
                        s = s * s;                                                 // набухает медленно, потом быстрее
                        rad = r * (0.3 + 0.7 * s);
                        ctr = p0 - float3(0, rad * 0.9, 0);
                    }
                    else if (t < swellT + Tf)
                    {
                        float tau = t - swellT;
                        vel = g * tau;
                        rad = r;
                        ctr = p0 - float3(0, r + 0.5 * g * tau * tau, 0);
                    }
                    else return o;
                    float3 up = float3(0, 1, 0);
                    float3 right = cross(up, vd);
                    float rl = length(right);
                    right = rl > 1e-4 ? right / rl : float3(1, 0, 0);
                    float stretch = 1.0 + min(vel * 0.03 / max(rad, 1e-4), 5.0);       // в полёте тянется по скорости
                    float hw = rad, hh = rad * stretch;
                    wp = ctr + right * (c.x - 0.5) * 2.0 * hw + up * (c.y - 0.5) * 2.0 * hh + up * (hh - rad) * 0.6;
                    o.info = float4(0, stretch, fade, seed);
                }
                else
                {
                    if (!floorHit || t < swellT + Tf) return o;
                    float k = saturate((t - swellT - Tf) / splashT);
                    float3 ctr = p0 - float3(0, fall - 0.012 * m, 0);
                    float size = r * (1.6 + 4.5 * k);
                    wp = ctr + float3((c.x - 0.5) * 2.0 * size, 0, (c.y - 0.5) * 2.0 * size);
                    o.info = float4(1, k, fade, seed);
                }
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = c;
                o.view = vd;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 lit = CR_Light(i.view);
                float2 p = i.uv * 2.0 - 1.0;
                if (i.info.x < 0.5)
                {
                    // капля-слеза: низ круглый, верх сужается; блик и тёмная кромка — мультяшно, край чёткий
                    float A = i.info.y;
                    float2 q = float2(p.x, p.y * A);                               // в радиусах капли
                    float top = max(q.y, 0.0);
                    float narrow = lerp(1.0, 0.45, saturate(top / max(A, 1.0)));   // верх уже — слеза
                    float2 qq = float2(q.x / narrow, q.y - clamp(q.y, -(A - 1.0), (A - 1.0)));
                    float d = length(qq);
                    float body = CR_AAStep(0.0, 1.0 - d);
                    if (body < 0.01) discard;
                    float hl = CR_AAStep(0.0, 0.32 - length(qq - float2(-0.35, 0.3)));
                    float edge = CR_AAStep(0.0, d - 0.72);
                    float3 col = lit * lerp(1.05, 0.7, edge) + hl * (lit * 0.6 + 0.35);
                    return half4(col, body * _Alpha * lerp(0.75, 1.0, hl) * i.info.z);
                }
                // колечко на полу: разбегается и тает
                float k = i.info.y;
                float d = length(p);
                float ring = CR_AAStep(0.0, 0.1 - abs(d - 0.82));
                float dot0 = CR_AAStep(0.0, 0.18 * (1.0 - k) - d) * step(k, 0.35);
                float a = max(ring * (1.0 - k), dot0) * _RingAlpha * i.info.z;
                if (a < 0.01) discard;
                return half4(lit * 1.1, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
