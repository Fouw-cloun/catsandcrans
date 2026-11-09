// CatCooks — УДАР МОЛНИИ В ЗЕМЛЮ: корона шипов, искры по дугам, куски бетона с кувырком, крошка,
// кольцо бетонной пыли по полу, клуб дыма. Без частиц: 96 квадов, всё в вершинном шейдере от времени удара. Ставит CityStorm (удар в зону и близкие удары).
// _CC_Impact: xyz точка, w возраст (с, <0 — нет); _CC_ImpactN: xyz нормаль поверхности, w метр (ед.);
// _CC_ImpactCol: rgb цвет сияния молнии.
Shader "CatCooks/City/Lightning Impact"
{
    Properties
    {
        _HotColor ("Горячее (искры, шипы)", Color) = (1, 0.95, 0.85, 1)
        _Boost ("Яркость (HDR)", Range(1, 10)) = 4
        _Rock ("Куски бетона", Color) = (0.58, 0.56, 0.53, 1)
        _RockDark ("Куски: обгоревшая сторона", Color) = (0.16, 0.15, 0.15, 1)
        _Dust ("Бетонная пыль", Color) = (0.72, 0.7, 0.66, 1)
        _Smoke ("Дым", Color) = (0.32, 0.32, 0.34, 1)
        _Scale ("Размер (множитель)", Range(0.3, 3)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+19" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "LightningImpact"
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
                float4 _HotColor, _Rock, _RockDark, _Dust, _Smoke;
                float _Boost, _Scale;
            CBUFFER_END
            float4 _CC_Impact, _CC_ImpactN, _CC_ImpactCol;

            #define N_SPARK 26
            #define N_ROCK 22
            #define N_SPIKE 14
            #define N_SMOKE 7
            #define N_DUST 9
            #define N_PEB 20

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 info : TEXCOORD1;   // x вид (0 искра, 1 кусок, 2 шип, 3 дым, 4 пыль, 5 крошка), y жизнь 0..1, z случайное, w свет грани
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
                float age = _CC_Impact.w;
                if (age < 0.0) return o;
                float m = _CC_ImpactN.w * _Scale;
                float3 P = _CC_Impact.xyz;
                float3 N = normalize(_CC_ImpactN.xyz + 1e-5);
                float3 Tg = normalize(cross(N, abs(N.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0)));
                float3 Bt = cross(N, Tg);
                float3 cam = GetCameraPositionWS();
                float3 g = float3(0, -9.8, 0) * m;
                float3 r3 = CR_Hash32(float2(iid * 1.31, 7.7));
                float rr = CR_Hash11(iid * 9.17);
                float2 c = kCorner[vid % 6];
                float kind; float life01; float3 wp;
                Light ml = GetMainLight();

                if (iid < N_SPARK)
                {
                    // искры: вылетают веером и падают по дуге, хвост — по скорости
                    float life = 0.45 + 0.45 * rr;
                    if (age > life) return o;
                    float a = r3.x * 6.28318;
                    float3 dir = normalize(N * (0.5 + 0.9 * r3.y) + (Tg * cos(a) + Bt * sin(a)) * (0.6 + 0.6 * r3.z));
                    float v0 = (5.0 + 9.0 * r3.z) * m;
                    float3 pos = P + dir * v0 * age + 0.5 * g * age * age;
                    float3 vel = dir * v0 + g * age;
                    float vl = length(vel);
                    float3 T = vel / max(vl, 1e-4);
                    float3 V = normalize(cam - pos);
                    float3 S = cross(T, V); float sl = length(S); S = sl > 1e-4 ? S / sl : Tg;
                    float len = 0.05 * m + vl * 0.035;
                    float wdt = 0.025 * m;
                    wp = pos + S * (c.x - 0.5) * 2.0 * wdt - T * c.y * len;
                    kind = 0; life01 = age / life;
                }
                else if (iid < N_SPARK + N_ROCK)
                {
                    // осколки: тяжелее, выше и медленнее, кувыркаются, ложатся на пол
                    float life = 1.1 + 0.4 * rr;
                    if (age > life) return o;
                    float a = r3.x * 6.28318;
                    float3 dir = normalize(N * (1.0 + 0.8 * r3.y) + (Tg * cos(a) + Bt * sin(a)) * (0.5 + 0.5 * r3.z));
                    float big = r3.y * r3.y;                                             // редкие крупные куски
                    float v0 = (3.0 + 4.5 * r3.z) * m * (1.0 - 0.35 * big);
                    float3 pos = P + dir * v0 * age + 0.5 * g * age * age;
                    float h = dot(pos - P, N);
                    if (h < 0.02 * m) pos -= N * (h - 0.02 * m);                         // лёг на пол
                    float3 ax = normalize(CR_Hash32(float2(iid * 0.77, 3.1)) - 0.5 + 1e-3);
                    float spin = h > 0.03 * m ? age * (8.0 + 10.0 * rr) : (0.0 + (8.0 + 10.0 * rr) * 0.4);
                    float3x3 rot = RotAxis(ax, spin + rr * 6.28);
                    float sz = (0.06 + 0.1 * rr + 0.16 * big) * m;
                    float3 lp = mul(rot, float3((c.x - 0.5) * sz, (c.y - 0.5) * sz * (0.6 + 0.6 * r3.y), 0));
                    float3 nrm = mul(rot, float3(0, 0, 1));
                    wp = pos + lp;
                    kind = 1; life01 = age / life;
                    o.info.w = abs(dot(nrm, ml.direction));
                }
                else if (iid < N_SPARK + N_ROCK + N_SPIKE)
                {
                    // корона шипов: выстреливают вверх-наружу и втягиваются
                    float i = iid - N_SPARK - N_ROCK;
                    float life = 0.32 + 0.1 * rr;
                    if (age > life) return o;
                    float a = (i + rr * 0.6) / N_SPIKE * 6.28318;
                    float tilt = 0.35 + 0.55 * r3.y;
                    float3 axis = normalize(N * cos(tilt) + (Tg * cos(a) + Bt * sin(a)) * sin(tilt));
                    float t01 = age / life;
                    float L = (0.5 + 0.7 * r3.z) * m * sin(saturate(t01 * 1.6) * 1.5708) * (1.0 - smoothstep(0.55, 1.0, t01));
                    float3 base = P + N * 0.02 * m;
                    float3 V = normalize(cam - base);
                    float3 S = cross(axis, V); float sl = length(S); S = sl > 1e-4 ? S / sl : Tg;
                    float wb = 0.07 * m * (1.0 - t01 * 0.5);
                    wp = base + axis * c.y * L + S * (c.x - 0.5) * 2.0 * wb * (1.0 - c.y);   // к кончику сужается в точку
                    kind = 2; life01 = t01;
                }
                else if (iid < N_SPARK + N_ROCK + N_SPIKE + N_SMOKE + N_DUST + N_PEB && iid >= N_SPARK + N_ROCK + N_SPIKE + N_SMOKE)
                {
                    float j = iid - (N_SPARK + N_ROCK + N_SPIKE + N_SMOKE);
                    if (j < N_DUST)
                    {
                        // кольцо бетонной пыли: клубки разбегаются по полу от центра и тают
                        float life = 0.9 + 0.3 * rr;
                        if (age > life) return o;
                        float t01 = age / life;
                        float a = (j + rr * 0.5) / N_DUST * 6.28318;
                        float3 rad = Tg * cos(a) + Bt * sin(a);
                        float dist = (0.3 + 2.4 * (1.0 - (1.0 - t01) * (1.0 - t01))) * m;      // быстро, потом тормозит
                        float sz = lerp(0.25, 0.75, t01) * m * (0.8 + 0.4 * r3.z);
                        float3 ctr = P + rad * dist + N * (sz * 0.55 + 0.05 * m);
                        float3 R = UNITY_MATRIX_V[0].xyz, U = UNITY_MATRIX_V[1].xyz;
                        wp = ctr + (R * (c.x - 0.5) + U * (c.y - 0.5)) * 2.0 * sz;
                        kind = 4; life01 = t01;
                    }
                    else
                    {
                        // крошка: мелкие быстрые камешки
                        float life = 0.6 + 0.4 * rr;
                        if (age > life) return o;
                        float a = r3.x * 6.28318;
                        float3 dir = normalize(N * (0.4 + 0.9 * r3.y) + (Tg * cos(a) + Bt * sin(a)) * (0.7 + 0.6 * r3.z));
                        float v0 = (5.0 + 6.0 * r3.z) * m;
                        float3 pos = P + dir * v0 * age + 0.5 * g * age * age;
                        float h = dot(pos - P, N);
                        if (h < 0.01 * m) pos -= N * (h - 0.01 * m);
                        float sz = (0.02 + 0.025 * rr) * m;
                        float3 R = UNITY_MATRIX_V[0].xyz, U = UNITY_MATRIX_V[1].xyz;
                        wp = pos + (R * (c.x - 0.5) + U * (c.y - 0.5)) * 2.0 * sz;
                        kind = 5; life01 = age / life;
                    }
                }
                else if (iid < N_SPARK + N_ROCK + N_SPIKE + N_SMOKE)
                {
                    // клуб дыма: появляется чуть позже, растёт, поднимается и тает
                    float i = iid - N_SPARK - N_ROCK - N_SPIKE;
                    float t0 = 0.12 + 0.12 * rr;
                    float life = 1.4 + 0.5 * r3.x;
                    float t = age - t0;
                    if (t < 0.0 || t > life) return o;
                    float t01 = t / life;
                    float a = (i / N_SMOKE + rr * 0.3) * 6.28318;
                    float3 ctr = P + N * (0.25 + 1.3 * t01 * (0.6 + 0.6 * r3.y)) * m + (Tg * cos(a) + Bt * sin(a)) * (0.25 + 0.5 * t01) * m;
                    float sz = lerp(0.3, 0.9, sqrt(t01)) * m * (0.8 + 0.4 * r3.z);
                    float3 R = UNITY_MATRIX_V[0].xyz, U = UNITY_MATRIX_V[1].xyz;
                    wp = ctr + (R * (c.x - 0.5) + U * (c.y - 0.5)) * 2.0 * sz;
                    kind = 3; life01 = t01;
                }
                else return o;

                o.positionCS = TransformWorldToHClip(wp);
                o.uv = c;
                o.info.xyz = float3(kind, life01, rr);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float kind = i.info.x, t = i.info.y, rnd = i.info.z;
                float2 p = i.uv * 2.0 - 1.0;
                float3 glowC = _CC_ImpactCol.rgb;
                if (kind < 0.5)
                {
                    // искра: горячая голова, хвост остывает к цвету молнии
                    float a = CR_AAStep(0.0, 0.85 - abs(p.x)) * (1.0 - i.uv.y * 0.7);
                    if (a < 0.01) discard;
                    float3 col = lerp(_HotColor.rgb, glowC, saturate(t * 1.3 + i.uv.y * 0.5)) * _Boost;
                    return half4(col, a * (1.0 - smoothstep(0.7, 1.0, t)));
                }
                if (kind < 1.5)
                {
                    // осколок: тёмный многоугольник, освещённая грань чуть светлее, первые мгновения тлеет
                    float sh = max(abs(p.x) + abs(p.y) * 0.6, abs(p.y));
                    float a = CR_AAStep(0.0, 0.9 - sh - (CR_Hash11(rnd * 31.0 + floor(atan2(p.y, p.x) * 1.2)) * 0.25));
                    if (a < 0.01) discard;
                    Light ml = GetMainLight();
                    float3 amb = max(SampleSH(float3(0, 1, 0)), 0.0);
                    float lit = step(0.4, i.info.w);
                    // бетон: светлая грань / тень ступенькой, у части кусков обгоревший угол, тёмная кромка
                    float charred = step(0.55, CR_Hash11(rnd * 17.0)) * CR_AAStep(0.2, p.x + p.y * 0.7);
                    float3 base = lerp(_Rock.rgb, _RockDark.rgb, charred);
                    float rim = CR_AAStep(0.0, sh - 0.62);
                    float3 col = base * (amb * 0.9 + ml.color * lerp(0.2, 0.75, lit)) * lerp(1.0, 0.6, rim);
                    col += glowC * _Boost * 0.4 * saturate(1.0 - t * 4.0) * charred;
                    return half4(col, a * (1.0 - smoothstep(0.85, 1.0, t)));
                }
                if (kind < 2.5)
                {
                    // шип: белое ядро по центру, сияние цвета молнии по краю
                    float w = abs(p.x) / max(1.0 - i.uv.y, 0.05);
                    float core = CR_AAStep(0.0, 0.35 - w);
                    float body = CR_AAStep(0.0, 1.0 - w);
                    if (body < 0.01) discard;
                    float3 col = (glowC * body + _HotColor.rgb * core) * _Boost;
                    return half4(col, body * (1.0 - smoothstep(0.6, 1.0, t)));
                }
                if (kind > 4.5)
                {
                    // крошка: маленький угловатый камешек
                    float a5 = CR_AAStep(0.0, 0.85 - max(abs(p.x), abs(p.y)) - abs(p.x + p.y) * 0.2);
                    if (a5 < 0.01) discard;
                    Light ml5 = GetMainLight();
                    float3 amb5 = max(SampleSH(float3(0, 1, 0)), 0.0);
                    float3 col5 = _Rock.rgb * (amb5 * 0.9 + ml5.color * lerp(0.2, 0.7, step(0.5, rnd)));
                    return half4(col5, a5 * (1.0 - smoothstep(0.8, 1.0, t)));
                }
                if (kind > 3.5)
                {
                    // бетонная пыль: светлый клуб с мультяшной кромкой, тает эрозией
                    float d4 = length(p);
                    float n4 = CR_Noise(i.uv * 3.0 + rnd * 11.0 + float2(t * 1.5, 0)) * 0.6 + CR_Noise(i.uv * 6.5 + rnd * 3.0) * 0.4;
                    float sh4 = (1.0 - d4) + (n4 - 0.5) * 0.8;
                    float a4 = CR_AAStep(0.12 + t * 0.75, sh4);
                    if (a4 < 0.01) discard;
                    Light ml4 = GetMainLight();
                    float3 amb4 = max(SampleSH(float3(0, 1, 0)), 0.0);
                    float lit4 = CR_AAStep(0.0, p.y + (n4 - 0.5) * 0.5);
                    float3 col4 = _Dust.rgb * lerp(amb4 * 0.95, amb4 + ml4.color * 0.55, lit4);
                    return half4(col4, a4 * 0.7 * smoothstep(0.0, 0.08, t));
                }
                // дым: мультяшный клуб, тает эрозией по шуму, верх светлее
                float d = length(p);
                float n = CR_Noise(i.uv * 3.0 + rnd * 17.0 + float2(0, -t * 1.2)) * 0.65 + CR_Noise(i.uv * 7.0 + rnd * 5.0) * 0.35;
                float shape = (1.0 - d) + (n - 0.5) * 0.7;
                float a = CR_AAStep(0.15 + t * 0.7, shape);
                if (a < 0.01) discard;
                Light ml = GetMainLight();
                float3 amb = max(SampleSH(float3(0, 1, 0)), 0.0);
                float lit = CR_AAStep(0.0, p.y + (n - 0.5) * 0.5 + 0.1);
                float3 col = _Smoke.rgb * lerp(amb * 0.9, amb + ml.color * 0.6, lit);
                return half4(col, a * 0.65 * smoothstep(0.0, 0.1, t));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
