// CatCooks — СНЕГОПАД (v2). Хлопья в коробке вокруг камеры (как дождь): падают, кружат, сносятся ветром.
//   • Вблизи — настоящие снежинки: дендриты (6 лучей с веточками, у каждой свой узор) и звёздчатые пластинки;
//     вращаются и кувыркаются (сплющиваются при повороте), иногда вспыхивают на свету. Дальше — мягкие комочки.
//   • Буран: снежинки НЕ растягиваются — рисуется след движения (яркая голова + тающий хвост, как выдержка камеры),
//     длина хвоста — от настоящей скорости хлопка на экране; хлопья мечутся турбулентностью; добавляется второй,
//     плотный слой хлопьев у самой камеры (видимость почти ноль — вместе с пеленой в City Rain Surface).
// Под крышей / зонтом (карта укрытий зоны) хлопьев нет. Сила и вид — CityWinter (_CC_SnowP, _CC_SnowP2, _CC_SnowP3).
Shader "CatCooks/City/Snow"
{
    Properties
    {
        _Alpha ("Непрозрачность", Range(0, 1)) = 0.95
        _StarShare ("(не используется в v2)", Range(0, 1)) = 0.35
        _Color ("Цвет", Color) = (1, 1, 1, 1)
        _Glint ("Вспышки снежинок на свету", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+14" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Snow"
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
                float _Alpha, _StarShare, _Glint;
                float4 _Color;
            CBUFFER_END
            float4 _CC_SnowP;      // x доля хлопьев, y скорость падения (ед./с), z размер (ед.), w ширина коробки
            float4 _CC_SnowP2;     // x высота коробки, y кружение (ед.), z буран 0..1, w сколько хлопьев в основном слое
            float4 _CC_SnowP3;     // x доля ближнего слоя (буран), y выдержка следа (с), z размер ближней коробки (×), w доля «витринных» снежинок
            float4 _CC_SnowFall;   // xyz накопленный сдвиг (падение + ветер)
            float4 _CC_SnowWind;   // xy — ветер сейчас (XZ, ед./с)

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 info : TEXCOORD1;   // x вид (0 комочек, 1 дендрит, 2 пластинка, 3 след), y длина следа (в ширинах), z гаснет, w случайное
                float4 rot : TEXCOORD2;    // x угол, y кувырок (сплющивание), z вспышка
            };

            static const float2 kCorner[6] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 0), float2(1, 1), float2(0, 1) };

            Varyings vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                o.positionCS = float4(2, 2, 2, 1);
                bool nearL = (float)iid >= _CC_SnowP2.w;
                float share = nearL ? _CC_SnowP3.x : _CC_SnowP.x;
                if (CR_Hash11(iid * 0.6131 + 0.17) > share) return o;
                float m = _CC_RainMeter;
                float3 s = CR_Hash32(float2(iid * 1.713, 4.1));
                float s4 = CR_Hash11(iid * 2.917 + 1.3);
                float bl = _CC_SnowP2.z;
                float3 B = float3(_CC_SnowP.w, _CC_SnowP2.x, _CC_SnowP.w) * (nearL ? _CC_SnowP3.z : 1.0);
                float3 cam = GetCameraPositionWS();
                float spd = 0.65 + 0.7 * s4;
                float t = _Time.y;
                float3 p = s * B + _CC_SnowFall.xyz * spd;
                // кружение (тихий снег)
                float sw = _CC_SnowP2.y * (0.4 + s4);
                float3 ph = s * 6.2832;
                float3 fr = float3(0.9 * (0.6 + s.x), 0.5, 0.7 * (0.6 + s.y));
                p += float3(sin(t * fr.x + ph.z), sin(t * fr.y + ph.y) * 0.3, cos(t * fr.z + ph.x)) * sw;
                float3 velT = float3(cos(t * fr.x + ph.z) * fr.x, cos(t * fr.y + ph.y) * 0.3 * fr.y, -sin(t * fr.z + ph.x) * fr.z) * sw;
                // турбулентность бурана: мечутся быстро и широко
                float tb = bl * m * 0.55 * (0.5 + s.z);
                float3 f2 = float3(2.3, 3.1, 1.9) * (0.7 + 0.6 * s4);
                float3 a2 = t * f2 + ph * float3(2.0, 3.0, 2.0);
                p += float3(sin(a2.x), sin(a2.y) * 0.6, sin(a2.z)) * tb;
                velT += float3(cos(a2.x) * f2.x, cos(a2.y) * 0.6 * f2.y, cos(a2.z) * f2.z) * tb;

                float3 lo = cam - B * float3(0.5, 0.45, 0.5);
                p = lo + frac((p - lo) / B) * B;                                   // коробка едет за камерой
                if (CR_Covered(p)) return o;
                float3 e3 = min(p - lo, lo + B - p) / (B * 0.12);
                float fade = saturate(min(e3.x, min(e3.y, e3.z)));
                float dist = length(p - cam);
                fade *= saturate((dist - 0.5 * m) / (0.8 * m));                    // у самого носа не лепим огромный хлопок

                float size = _CC_SnowP.z * (0.5 + 1.1 * s4 * s4);
                bool showcase = s.x < _CC_SnowP3.w && dist < 5.5 * m && !nearL;
                if (showcase) size *= 1.9;
                float4 cs0 = TransformWorldToHClip(p);
                float pix = 2.0 * max(cs0.w, 0.01) / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);
                size = max(size, pix * 1.3);
                size = min(size, 28.0 * pix);                                      // у самой камеры снежинка не больше ~55 пикселей

                float3 V = normalize(cam - p);
                float3 vel = float3(_CC_SnowWind.x, -_CC_SnowP.y, _CC_SnowWind.y) * spd + velT;
                float3 ax = vel * _CC_SnowP3.y;                                    // путь за выдержку
                float3 axP = ax - V * dot(ax, V);
                float len = length(axP);
                float lenMax = 22.0 * pix;                                         // хвост не длиннее ~22 пикселей — без «гиперпрыжка»
                if (len > lenMax) { axP *= lenMax / len; len = lenMax; }
                float2 c = kCorner[vid % 6];
                float3 wp;
                float w = max(size * 0.5, pix * 0.8);
                if (len > w * 1.6)
                {
                    // след движения: голова в p, хвост назад по скорости
                    float3 U = axP / len;
                    float3 R = normalize(cross(U, V));
                    wp = p + R * (c.x - 0.5) * 2.0 * w + U * lerp(-len, w, c.y);
                    o.info = float4(3, len / w, fade, s.y);
                }
                else
                {
                    float3 R = UNITY_MATRIX_V[0].xyz, U = UNITY_MATRIX_V[1].xyz;
                    wp = p + R * (c.x - 0.5) * 2.0 * size + U * (c.y - 0.5) * 2.0 * size;
                    float kind = showcase ? (s.y < 0.62 ? 1.0 : 2.0) : ((dist < 11.0 * m && s.z < 0.45) ? 2.0 : 0.0);
                    o.info = float4(kind, 0, fade, s.y);
                }
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = c;
                float ang = s.y * 6.2832 + t * (s.z - 0.5) * 1.6;
                float tumble = 0.4 + 0.6 * abs(cos(t * (0.5 + s4) + s.x * 9.0));
                float glint = pow(saturate(sin(t * (2.5 + s4 * 4.0) + s.z * 40.0)), 24.0);
                o.rot = float4(ang, tumble, glint, 0);
                return o;
            }

            float SdSeg(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }

            // шестиугольник, вершины на осях лучей
            float SdHex(float2 p, float r)
            {
                const float3 k = float3(-0.866025404, 0.5, 0.577350269);
                p = abs(p.yx);
                p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
                p -= float2(clamp(p.x, -k.z * r, k.z * r), r);
                return length(p) * sign(p.y);
            }

            // сводим точку в 1/12 круга: q.x вдоль луча, q.y ≥ 0 поперёк
            float2 Fold6(float2 p)
            {
                float r = length(p);
                float ang = atan2(p.y, p.x);
                ang -= 1.0471976 * floor(ang / 1.0471976 + 0.5);
                ang = abs(ang);
                return float2(cos(ang), sin(ang)) * r;
            }

            // ДЕНДРИТ: 6 лучей, на каждом 3 веточки под 60°, у каждой снежинки свои длины и места веточек
            float Dendrite(float2 p, float seed)
            {
                float2 q = Fold6(p);
                float3 h = CR_Hash32(float2(seed * 91.7, 3.1));
                float3 h2 = CR_Hash32(float2(seed * 37.3, 8.9));
                float w = 0.03 + 0.016 * h.z;
                float d = SdSeg(q, float2(0, 0), float2(0.9, 0)) - w * (1.0 - 0.45 * saturate(q.x));
                const float2 dir = float2(0.5, 0.8660254);
                float hi[3] = { h2.x, h2.y, h2.z };
                [unroll] for (int i = 0; i < 3; i++)
                {
                    float t = 0.3 + 0.19 * i + (hi[i] - 0.5) * 0.07;
                    float len = (0.36 - 0.085 * i) * (0.5 + 0.8 * hi[i]);
                    float2 b0 = float2(t, 0);
                    d = min(d, SdSeg(q, b0, b0 + dir * len) - w * 0.72);
                    if (i < 2 && h.x > 0.4)                                          // мелкие веточки ближе к центру
                    {
                        float2 b1 = float2(t * 0.55, 0);
                        d = min(d, SdSeg(q, b1, b1 + dir * len * 0.35) - w * 0.6);
                    }
                }
                if (h.x > 0.55) d = min(d, SdHex(q - float2(0.86, 0), 0.06));      // кристаллик на конце луча
                float core = SdHex(p, 0.1 + 0.07 * h.y);
                if (h.y > 0.5) core = max(core, -SdHex(p, 0.04 + 0.03 * h.y));     // полый центр
                return min(d, core);
            }

            // ЗВЁЗДЧАТАЯ ПЛАСТИНКА: полупрозрачный шестиугольник с рёбрами и каймой; mask — плёнка, ribs — рёбра
            void Plate(float2 p, float seed, out float film, out float ribs)
            {
                float3 h = CR_Hash32(float2(seed * 53.1, 1.7));
                float R = 0.62 + 0.22 * h.x;
                float2 q = Fold6(p);
                float hex = SdHex(p, R * 0.866);
                // лепестки: пластинка вдоль каждого луча (звезда из пластин) у части
                float petal = SdSeg(q, float2(0, 0), float2(0.9, 0)) - (0.16 + 0.1 * h.z) * (1.0 - 0.5 * saturate(q.x));
                float shape = h.y > 0.5 ? min(hex, petal) : hex;
                float fw = max(fwidth(shape), 1e-4);
                film = saturate(-shape / fw + 0.5);
                float rim = abs(shape + 0.045) - 0.022;
                float rib = SdSeg(q, float2(0, 0), float2(R, 0)) - 0.022;
                float inner = abs(SdHex(p, R * 0.42)) - 0.018;
                float dr = min(min(rim, rib), inner);
                ribs = saturate(-dr / max(fwidth(dr), 1e-4) + 0.5) * film;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float a;
                float kind = i.info.x;
                if (kind > 2.5)
                {
                    // след движения: x поперёк, along в ширинах (хвост −L..0, голова −1..1)
                    float L = i.info.y;
                    float x = i.uv.x * 2.0 - 1.0;
                    float al = i.uv.y * (L + 1.0) - L;
                    float head = saturate(1.0 - length(float2(x, al)));
                    float tail = al < 0.0 ? pow(saturate(1.0 + al / L), 1.8) * saturate(1.0 - x * x) : 0.0;
                    a = max(head * head * 1.4, tail * 0.45);
                    a = saturate(a) * lerp(0.8, 0.4, saturate((L - 3.0) / 10.0));    // длинные — бледнее
                }
                else
                {
                    float2 p = i.uv * 2.0 - 1.0;
                    float sn, cs;
                    sincos(i.rot.x, sn, cs);
                    p = float2(p.x * cs - p.y * sn, p.x * sn + p.y * cs);
                    p.x /= max(i.rot.y, 0.15);                                     // кувырок: снежинка повернулась ребром
                    if (kind < 0.5)
                    {
                        float d = length(p);
                        a = max(CR_AAStep(0.0, 0.5 - d), saturate(1.0 - d) * 0.45);
                    }
                    else if (kind < 1.5)
                    {
                        float d = Dendrite(p * 1.04, i.info.w);
                        float fw = max(fwidth(d), 1e-4);
                        a = saturate(-d / fw + 0.5) + exp(-max(d, 0.0) * 22.0) * 0.18;  // чёткий узор + лёгкое свечение
                    }
                    else
                    {
                        float film, ribs;
                        Plate(p, i.info.w, film, ribs);
                        a = film * 0.45 + ribs * 0.6;
                    }
                }
                a = saturate(a);
                if (a < 0.01) discard;
                Light ml = GetMainLight();
                float3 amb = max(SampleSH(float3(0, 1, 0)), 0.0);
                float3 col = _Color.rgb * (amb * 1.15 + ml.color * 0.45 + 0.05) + _CC_Flash.rgb * _CC_Flash.a * 0.8;
                col += ml.color * i.rot.z * 0.9 * _Glint * step(kind, 2.5);
                return half4(col, a * _Alpha * i.info.z);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
