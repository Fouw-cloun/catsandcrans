// CatCooks — ГРАД. Градины в коробке вокруг камеры: падают быстро (короткий след движения), бьются о пол,
// крыши, столы и ящики (высота — из карты укрытий дождя), отскакивают 2 раза с разлётом ледяной крошки,
// лежат и тают. Сила, размер, ветер — CityHail (_CC_HailP, _CC_HailP2, _CC_HailWind).
Shader "CatCooks/City/Hail"
{
    Properties
    {
        _Color ("(цвет в шейдере: молочное ядро + голубой лёд)", Color) = (0.86, 0.94, 1, 1)
        _Alpha ("Непрозрачность", Range(0, 1)) = 1
        _Chips ("Крошка при ударе", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+13" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Hail"
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
                float4 _Color;
                float _Alpha, _Chips;
            CBUFFER_END
            float4 _CC_HailP;      // x доля градин, y скорость падения (ед./с), z размер (радиус, ед.), w ширина коробки
            float4 _CC_HailP2;     // x высота падения (ед.), y высота отскока (ед.), z сколько лежит (с), w g (ед./с²)
            float4 _CC_HailWind;   // xy — ветер (XZ, ед./с), z выдержка следа (с)
            float4 _CC_Amb;        // окружение (ставит CityWinter / CityHail)

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 info : TEXCOORD1;   // x вид (0 градина, 1 след, 2 крошка), y длина следа / прогресс крошки, z альфа, w случайное
                float4 rot : TEXCOORD2;    // x поворот, y раскол 0..1 (после удара — два куска разлетаются)
            };

            static const float2 kCorner[6] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 0), float2(1, 1), float2(0, 1) };

            Varyings vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                o.positionCS = float4(2, 2, 2, 1);
                if (CR_Hash11(iid * 0.7131 + 0.27) > _CC_HailP.x) return o;
                float m = _CC_RainMeter;
                float g = _CC_HailP2.w;
                float3 s = CR_Hash32(float2(iid * 1.371, 2.9));
                float s4 = CR_Hash11(iid * 3.117 + 0.7);
                float v = _CC_HailP.y * (0.85 + 0.3 * s4);
                float tf = _CC_HailP2.x / max(v, 1e-3);                         // падение
                float h1 = _CC_HailP2.y * (0.4 + 0.9 * s.z);
                float vb = sqrt(2.0 * g * h1);
                float tb1 = 2.0 * vb / g, tb2 = tb1 * 0.5;                       // два отскока (второй — вчетверо ниже)
                float tl = _CC_HailP2.z * (0.6 + 0.8 * s.x);                     // лежит
                float T = tf + tb1 + tb2 + tl;
                float tt = _Time.y / T + s.y;
                float cyc = floor(tt);
                float lt = frac(tt) * T;
                // где упадёт в этом цикле: точка в мире, коробка едет за камерой (без прыжков, пока точка внутри)
                float3 cam = GetCameraPositionWS();
                float2 B = _CC_HailP.ww;
                float2 r = CR_Hash32(float2(iid * 0.913 + 3.1, cyc * 1.71 + 0.4)).xy;
                float2 lo = cam.xz - B * 0.5;
                float2 base = lo + frac((r * B * 7.31 - lo) / B) * B;
                float2 e2 = min(base - lo, lo + B - base) / (B * 0.1);
                float fade = saturate(min(e2.x, e2.y));
                float ground = _CC_GroundY, hh;
                if (CR_OccHeight(float3(base.x, 0.0, base.y), hh)) ground = hh;  // пол, крыша, стол — что выше всего
                float size = _CC_HailP.z * (0.6 + 0.8 * s4);
                float2 skd = normalize(CR_Hash32(float2(iid * 2.3, cyc)).xy - 0.5 + 1e-4);
                float sk = 0.3 * m * (0.4 + s.x);                               // градины скачут в сторону
                float3 p, vel = 0;
                float alpha = fade;
                float impact = -1.0;
                float crack = 0.0;
                if (lt < tf)
                {
                    float rem = tf - lt;
                    p = float3(base.x - _CC_HailWind.x * rem, ground + v * rem, base.y - _CC_HailWind.y * rem);
                    vel = float3(_CC_HailWind.x, -v, _CC_HailWind.y);
                }
                else if (lt < tf + tb1)
                {
                    float u = lt - tf;
                    float2 xz = base + skd * sk * u;
                    p = float3(xz.x, ground + vb * u - 0.5 * g * u * u, xz.y);
                    impact = u / 0.3;
                    crack = saturate(u / max(tb1, 1e-3));                        // от удара раскололась — куски расходятся
                    size *= 0.85;
                }
                else if (lt < tf + tb1 + tb2)
                {
                    float u = lt - tf - tb1;
                    float2 xz = base + skd * sk * (tb1 + u * 0.6);
                    p = float3(xz.x, ground + vb * 0.5 * u - 0.5 * g * u * u, xz.y);
                    crack = 1.0;
                    size *= 0.8;
                }
                else
                {
                    float u = (lt - tf - tb1 - tb2) / max(tl, 1e-3);
                    float2 xz = base + skd * sk * (tb1 + tb2 * 0.6);
                    p = float3(xz.x, ground, xz.y);
                    float melt = smoothstep(0.1, 1.0, u);                        // тает по чуть-чуть
                    crack = 1.0;
                    size *= 0.8 * (1.0 - melt * 0.75);
                    alpha *= 1.0 - smoothstep(0.7, 1.0, u);
                }
                p.y += size * 0.9;
                if (alpha < 0.01) return o;

                float4 cs0 = TransformWorldToHClip(p);
                float pix = 2.0 * max(cs0.w, 0.01) / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);
                float sz = max(size, pix * 1.2);
                float3 V = normalize(cam - p);
                float2 c = kCorner[vid % 6];
                bool chip = vid >= 6;
                float3 wp;
                if (chip)
                {
                    // ледяная крошка при первом ударе: короткая вспышка-разлёт
                    if (impact < 0.0 || impact > 1.0 || _Chips < 0.01) return o;
                    float3 R = UNITY_MATRIX_V[0].xyz, U = UNITY_MATRIX_V[1].xyz;
                    float rad = max(size * 6.0, pix * 8.0);
                    float3 gp = float3(base.x, ground + size * 0.5, base.y);
                    wp = gp + (R * (c.x - 0.5) + U * (c.y - 0.5)) * 2.0 * rad;
                    o.info = float4(2, impact, fade, s.z);
                }
                else
                {
                    float3 ax = vel * _CC_HailWind.z;
                    float3 axP = ax - V * dot(ax, V);
                    float len = min(length(axP), 18.0 * pix);
                    if (len > sz * 1.5)
                    {
                        float3 U = normalize(axP);
                        float3 R = normalize(cross(U, V));
                        wp = p + R * (c.x - 0.5) * 2.0 * sz + U * lerp(-len, sz, c.y);
                        o.info = float4(1, len / sz, alpha, s.z);
                    }
                    else
                    {
                        float3 R = UNITY_MATRIX_V[0].xyz, U = UNITY_MATRIX_V[1].xyz;
                        float big = 1.0 + crack * 0.8;                                   // место под разлетевшиеся куски
                        wp = p + (R * (c.x - 0.5) + U * (c.y - 0.5)) * 2.0 * sz * big;
                        o.info = float4(0, big, alpha, s.z);
                    }
                }
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = c;
                o.rot = float4(s.y * 6.2832 + _Time.y * (s.x - 0.5) * 7.0 * (1.0 - crack), crack, 0, 0);
                return o;
            }

            // Неровная граненая льдинка: расстояние до контура (<0 внутри), t — 0 в центре .. 1 у края, facet — номер грани
            float Lump(float2 q, float seed, out float t, out float facet)
            {
                float r = length(q);
                float an = atan2(q.y, q.x);
                const float N = 7.0;
                float fa = 6.2832 / N;
                float sector = floor((an + 3.14159) / fa);
                float local = fmod(an + 3.14159, fa) - fa * 0.5;
                float poly = cos(fa * 0.5) / cos(local);                                         // семь граней
                float R = 0.8 + 0.1 * sin(an * 3.0 + seed * 6.0) + 0.06 * sin(an * 5.0 + seed * 11.0) + 0.04 * sin(an * 2.0 + seed * 3.0);
                R *= lerp(1.0, poly, 0.55);
                t = r / max(R, 1e-3);
                facet = frac(sin((sector + seed * 17.0) * 12.9898) * 43758.5);
                return r - R;
            }

            half4 frag(Varyings i) : SV_Target
            {
                Light ml = GetMainLight();
                float3 amb = _CC_Amb.w > 0.5 ? _CC_Amb.rgb : max(SampleSH(float3(0, 1, 0)), 0.0);
                float3 lit = amb * 1.2 + ml.color * 0.6;
                float a;
                float3 col;
                float kind = i.info.x;
                float2 p = i.uv * 2.0 - 1.0;
                if (kind < 0.5)
                {
                    // ЛЬДИНКА: неровная, с гранями; внутри молочное ядро и слои-кольца, насыщенный голубой лёд,
                    // тёмно-синий мультяшный контур, блик. После удара — два куска расходятся
                    float big = max(i.info.y, 1.0);
                    float2 q = p * big;
                    float sn, cs;
                    sincos(i.rot.x, sn, cs);
                    q = float2(q.x * cs - q.y * sn, q.x * sn + q.y * cs);
                    float cr = i.rot.y;
                    float t1, f1, t2, f2;
                    float sd;
                    float t, facet;
                    if (cr > 0.001)
                    {
                        float off = 0.55 * cr;
                        float d1 = Lump((q - float2(off, 0.05)) / 0.68, i.info.w, t1, f1) * 0.68;
                        float d2 = Lump((q + float2(off, -0.05)) / 0.6, i.info.w + 0.37, t2, f2) * 0.6;
                        sd = min(d1, d2);
                        t = d1 < d2 ? t1 : t2;
                        facet = d1 < d2 ? f1 : f2;
                        sd = lerp(Lump(q, i.info.w, t1, f1), sd, saturate(cr * 4.0));          // мгновенный раскол без скачка
                    }
                    else sd = Lump(q, i.info.w, t, facet);
                    float fw = max(fwidth(sd), 1e-4);
                    a = saturate(-sd / fw + 0.5);
                    float3 ice = float3(0.32, 0.68, 1.0);                                          // насыщенный голубой
                    float3 milk = float3(0.92, 0.97, 1.0);
                    float rings = frac(t * 2.6 + i.info.w * 3.0);
                    float3 base = lerp(milk, ice, smoothstep(0.18, 0.62, t));                      // молочное ядро → голубой лёд
                    base = lerp(base, milk, smoothstep(0.82, 0.97, rings) * 0.55 * step(0.3, t)); // слои-кольца
                    base *= 0.9 + 0.2 * facet;                                                     // грани чуть разные
                    float3 lightC = amb * 1.15 + ml.color * 0.7;
                    col = base * lightC;
                    col += pow(saturate(1.0 - length(q - float2(-0.3, 0.33)) * 3.2), 2.0) * (ml.color * 0.9 + 0.4);   // блик
                    col += pow(saturate(1.0 - length(q - float2(0.22, -0.28)) * 7.0), 2.0) * 0.25; // второй, мелкий
                    col = lerp(col, float3(0.08, 0.26, 0.55) * lightC, saturate((sd + 0.1) / 0.06)); // контур
                }
                else if (kind < 1.5)
                {
                    // след падения: яркая голова, тающий хвост
                    float L = i.info.y;
                    float al = i.uv.y * (L + 1.0) - L;
                    float head = saturate(1.0 - length(float2(p.x, al)));
                    float tail = al < 0.0 ? pow(saturate(1.0 + al / L), 1.5) * saturate(1.0 - p.x * p.x) : 0.0;
                    a = saturate(max(head * 1.6, tail * 0.55));
                    col = float3(0.6, 0.85, 1.0) * lit;
                }
                else
                {
                    // крошка: 6 осколков разлетаются и гаснут
                    float pr = i.info.y;
                    a = 0.0;
                    [unroll] for (int k = 0; k < 6; k++)
                    {
                        float an = k * 1.0472 + i.info.w * 6.28;
                        float2 cp = float2(cos(an), sin(an) * 0.6 + 0.25) * (0.2 + pr * 0.75);
                        cp.y -= pr * pr * 0.5;                                         // падают обратно
                        float r = 0.11 * (1.0 - pr * 0.6);
                        float2 dq = p - cp;
                        float sa = sin(an * 3.0 + pr * 6.0), ca = cos(an * 3.0 + pr * 6.0);
                        dq = float2(dq.x * ca - dq.y * sa, dq.x * sa + dq.y * ca);
                        a = max(a, saturate((r - (abs(dq.x) * 1.6 + abs(dq.y))) / 0.03));          // осколок-ромбик
                    }
                    a *= (1.0 - pr) * _Chips;
                    col = lerp(float3(0.45, 0.78, 1.0), float3(0.9, 0.97, 1.0), 0.4) * lit * 1.1;
                }
                a *= _Alpha * i.info.z;
                if (a < 0.01) discard;
                col += _CC_Flash.rgb * _CC_Flash.a * 0.8;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
