// CatCooks — ЛЕТАЮЩИЙ МУСОР В ПОРЫВ: пакеты (парят петлями, надуваются), газеты (хлопают половинками),
// стаканчики и банки (катятся по полу), картонки (скачут и вертятся), изредка вывернутый зонтик кувырком.
// «Тонна мусора» (_CC_WindFx2.x): всё время, со всех сторон.
// Без частиц и текстур: формы рисуются в шейдере, полёт и кувырок — в вершинном. Густеют в волне порыва.
Shader "CatCooks/City/Wind Junk"
{
    Properties
    {
        _Scale ("Размер (множитель)", Range(0.3, 3)) = 1
        _Paper ("Газета", Color) = (0.93, 0.9, 0.82, 1)
        _Ink ("Печать", Color) = (0.25, 0.25, 0.28, 1)
        _Card ("Картон", Color) = (0.72, 0.53, 0.33, 1)
        _Outline ("Обводка (темнее)", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+17" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "WindJunk"
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
                float _Scale, _Outline;
                float4 _Paper, _Ink, _Card;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 info : TEXCOORD1;   // x вид, y случайное, z свет грани, w видимость
                float panelB : TEXCOORD2; // 1 — вторая половина газеты
            };

            static const float2 kCorner[6] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 0), float2(1, 1), float2(0, 1) };

            Varyings vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                o.positionCS = float4(2, 2, 2, 1);
                float strength = _CC_WindFx.x;
                float trash = _CC_WindFx2.x;                                                  // тонна мусора
                if (max(strength, trash) < 0.01) return o;
                float life = 5.5;
                float tt = _Time.y / life + CR_Hash11(iid * 2.17);
                float cyc = floor(tt), f = frac(tt);
                float3 s = CR_Hash32(float2(iid * 1.3, cyc * 2.1));
                float s4 = CR_Hash11(iid * 7.1 + cyc * 3.3);
                float kind = s4 < 0.3 ? 0 : (s4 < 0.58 ? 1 : (s4 < 0.76 ? 2 : (s4 < 0.93 ? 3 : 4)));
                uint q = vid / 6;
                if (q == 1 && kind != 1) return o;                                          // вторая половина — только у газеты

                float m = _CC_RainMeter * _Scale;
                float mu = _CC_RainMeter;
                // в «тонне мусора» каждый предмет летит со своей стороны
                float3 dir0 = CW_Dir();
                float da = (CR_Hash11(iid * 4.9 + cyc * 2.3) - 0.5) * 6.28318 * trash;
                float3 dir = float3(dir0.x * cos(da) - dir0.z * sin(da), 0, dir0.x * sin(da) + dir0.z * cos(da));
                float3 perp = float3(-dir.z, 0, dir.x);
                float R = _CC_GustP.x;
                float2 xz0 = _CC_GustFocus.xz + perp.xz * (s.x - 0.5) * 2.0 * R - dir.xz * R * 1.1;
                float speedK = kind == 0 ? 0.9 : (kind == 1 ? 1.0 : (kind == 2 ? 0.65 : (kind == 3 ? 0.8 : 0.85)));
                float run = 2.4 * R * speedK;
                float2 xz = xz0 + dir.xz * f * run + perp.xz * sin(f * 7.0 + s.z * 6.28) * 0.8 * mu;
                float g = CW_Gate(max(strength * CW_Wave(xz), trash), CR_Hash11(iid * 0.91 + cyc * 1.7));
                if (g < 0.01) return o;
                float fl = CW_Floor(xz);
                float t = _Time.y;
                float y, size; float3 ax; float ang; float2 dim = float2(1, 1);
                if (kind < 0.5)        // пакет: петли вверх-вниз, медленный кувырок
                {
                    size = 0.45 * m; y = (0.6 + 2.2 * s.y) * mu + sin(f * 6.28 * 1.3 + s.z * 6.28) * 1.0 * mu + 0.6 * mu;
                    ax = normalize(float3(0.3, 1, 0.2) + s - 0.5); ang = t * (1.5 + s.x) + sin(t * 2.3) * 0.6;
                }
                else if (kind < 1.5)   // газета: подскоки, хлопает
                {
                    size = 0.32 * m; dim = float2(1.0, 1.35); y = (0.15 + 1.8 * s.y * abs(sin(f * 3.14159 * 3.0 + s.z))) * mu + 0.1 * mu;
                    ax = normalize(float3(1, 0.4, 0.3) + s - 0.5); ang = t * (2.5 + 2.0 * s.x);
                }
                else if (kind < 2.5)   // стаканчик / банка: катится по полу
                {
                    size = 0.09 * m; dim = float2(0.75, 1.0); y = size * 0.8;
                    ax = perp; ang = f * run / max(size, 1e-3) * 0.9;
                }
                else if (kind < 3.5)   // картонка: скачет, быстро вертится
                {
                    size = 0.3 * m; dim = float2(1.25, 0.85); y = (0.1 + 0.9 * abs(sin(f * 3.14159 * 4.0 + s.z * 3.0))) * mu + 0.05 * mu;
                    ax = normalize(float3(0.5, 1, 0.5) + s - 0.5); ang = t * (5.0 + 3.0 * s.x);
                }
                else                   // зонтик: вывернутый, кувырком высоко
                {
                    size = 0.45 * m; y = (1.0 + 2.0 * s.y) * mu + sin(f * 6.0 + s.x * 6.28) * 0.6 * mu;
                    ax = normalize(float3(1, 0.6, 0.4) + s - 0.5); ang = t * (3.0 + 2.0 * s.x);
                }
                float3 ctr = float3(xz.x, fl + y, xz.y);
                float2 c = kCorner[vid % 6];
                float3 lp;
                float3 cam = GetCameraPositionWS();
                Light ml = GetMainLight();
                float3 nrm;
                if (kind > 1.5 && kind < 2.5)
                {
                    // стаканчик: «спрайт» к камере, катится — поворот вокруг оси взгляда
                    float3 Rv = UNITY_MATRIX_V[0].xyz, Uv = UNITY_MATRIX_V[1].xyz;
                    float ca = cos(ang), sa = sin(ang);
                    float2 l = float2((c.x - 0.5) * 2.0 * size * dim.x, (c.y - 0.5) * 2.0 * size * dim.y);
                    l = float2(l.x * ca - l.y * sa, l.x * sa + l.y * ca);
                    lp = Rv * l.x + Uv * l.y;
                    nrm = normalize(cam - ctr);
                }
                else
                {
                    float3x3 rot = CW_RotAxis(ax, ang + s.y * 6.28);
                    float3 l;
                    if (kind > 0.5 && kind < 1.5)
                    {
                        // газета: две половины на «сгибе» по x=0, вторая хлопает
                        float flap = sin(t * 11.0 + s.x * 6.28) * 1.1;
                        float lx = c.x * size * dim.x;
                        float ly = (c.y - 0.5) * 2.0 * size * dim.y;
                        if (q == 0) l = float3(-lx, ly, 0);
                        else l = float3(lx * cos(flap), ly, lx * sin(flap));
                        o.panelB = q;
                    }
                    else
                    {
                        float puff = kind < 0.5 ? 1.0 + 0.12 * sin(t * 5.0 + s.z * 6.28) : 1.0;  // пакет «дышит»
                        l = float3((c.x - 0.5) * 2.0 * size * dim.x * puff, (c.y - 0.5) * 2.0 * size * dim.y, 0);
                    }
                    lp = mul(rot, l);
                    nrm = mul(rot, float3(0, 0, 1));
                }
                o.positionCS = TransformWorldToHClip(ctr + lp);
                o.uv = c;
                float fade = smoothstep(0.0, 0.08, f) * (1.0 - smoothstep(0.9, 1.0, f));
                o.info = float4(kind, CR_Hash11(iid * 3.7 + cyc), abs(dot(nrm, ml.direction)), g * fade);
                return o;
            }

            float Box(float2 p, float2 b, float r) { float2 d = abs(p) - b + r; return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - r; }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float kind = i.info.x, rnd = i.info.y;
                float2 p = i.uv * 2.0 - 1.0;
                float d;           // < 0 — внутри формы
                float3 base;
                float aMul = 1.0;
                if (kind < 0.5)
                {
                    // пакет: мягкий мешок + две ручки-петли сверху, складки
                    float body = Box(p - float2(0, -0.12), float2(0.72, 0.62), 0.28);
                    float2 e1 = p - float2(-0.38, 0.62), e2 = p - float2(0.38, 0.62);
                    float ears = min(abs(length(e1) - 0.2) - 0.07, abs(length(e2) - 0.2) - 0.07);
                    ears = max(ears, -(p.y - 0.5));
                    d = min(body, ears);
                    float3 tints[3] = { float3(0.95, 0.95, 0.93), float3(0.75, 0.87, 0.97), float3(0.98, 0.78, 0.84) };
                    base = tints[(int)(rnd * 2.99)];
                    base *= 1.0 - 0.12 * step(0.55, CR_Noise(p * float2(3.0, 9.0) + rnd * 20.0));   // складки
                    aMul = 0.88;
                }
                else if (kind < 1.5)
                {
                    // газета: строки текста, заголовок, колонки; обратная сторона — без заголовка
                    d = Box(p, float2(0.92, 0.95), 0.03);
                    base = _Paper.rgb;
                    float2 g2 = i.uv;
                    float rows = step(0.55, frac(g2.y * 18.0)) * step(0.12, g2.x) * step(g2.x, 0.92) * step(0.1, g2.y) * step(g2.y, 0.72);
                    float head = step(0.78, g2.y) * step(g2.y, 0.9) * step(0.15, g2.x) * step(g2.x, 0.88) * (front ? 1.0 : 0.0);
                    float pic = (front && i.panelB < 0.5) ? step(0.3, g2.y) * step(g2.y, 0.6) * step(0.2, g2.x) * step(g2.x, 0.7) : 0.0;
                    base = lerp(base, _Ink.rgb, saturate(rows * 0.45 + head * 0.85 + pic * 0.6));
                    if (!front) base *= 0.92;
                }
                else if (kind < 2.5)
                {
                    // стаканчик (кофе) или банка: трапеция, крышка, полоса
                    float w = lerp(0.62, 0.82, i.uv.y);
                    d = max(abs(p.x) - w, abs(p.y) - 0.92);
                    bool can = rnd > 0.5;
                    base = can ? float3(0.78, 0.8, 0.84) : float3(0.95, 0.94, 0.9);
                    float band = step(abs(p.y + 0.05), 0.3);
                    float3 bandC = can ? (rnd > 0.75 ? float3(0.85, 0.2, 0.2) : float3(0.2, 0.55, 0.85)) : float3(0.55, 0.36, 0.22);
                    base = lerp(base, bandC, band);
                    base = lerp(base, base * 0.7, step(0.78, p.y));                         // крышка / край
                }
                else if (kind < 3.5)
                {
                    // картонка: лента и сгиб
                    d = Box(p, float2(0.92, 0.9), 0.06);
                    base = _Card.rgb;
                    base = lerp(base, base * 1.18, step(abs(p.y), 0.12));                   // скотч
                    base = lerp(base, base * 0.82, step(abs(p.x - 0.1), 0.02));             // сгиб
                }
                else
                {
                    // зонтик: фестончатый круг из 8 цветных клиньев, ручка-крюк
                    float r = length(p);
                    float ang = atan2(p.y, p.x);
                    float scal = 0.82 + 0.06 * cos(ang * 8.0);
                    float canopy = r - scal;
                    float2 hp = p - float2(0.05, -0.2);
                    float stick = max(abs(hp.x) - 0.04, abs(hp.y + 0.35) - 0.45);
                    float hook = max(abs(length(p - float2(0.2, -0.8)) - 0.15) - 0.04, p.y + 0.8);
                    d = min(canopy, min(stick, hook));
                    float seg = floor((ang + 3.14159) / 6.28318 * 8.0);
                    float3 cA = rnd > 0.5 ? float3(0.9, 0.25, 0.3) : float3(0.25, 0.45, 0.85);
                    base = (fmod(seg, 2.0) < 1.0) ? cA : float3(0.95, 0.92, 0.85);
                    base = canopy < 0.0 ? base : float3(0.18, 0.16, 0.15);
                }
                float fw = max(fwidth(d), 1e-4);
                float a = saturate(-d / fw + 0.5);
                if (a < 0.01) discard;
                float rim = saturate((d + 0.07) / fw + 0.5);                                  // тёмная обводка по краю
                Light ml = GetMainLight();
                float3 amb = max(SampleSH(float3(0, 1, 0)), 0.0);
                float lit = step(0.35, i.info.z);
                float3 col = base * (amb * 0.95 + ml.color * lerp(0.25, 0.8, lit));
                col *= lerp(1.0, 1.0 - _Outline, rim);
                return half4(col, a * aMul * i.info.w);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
