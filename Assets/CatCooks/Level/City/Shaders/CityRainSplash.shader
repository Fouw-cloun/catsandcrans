// CatCooks — всплески капель ВЕЗДЕ: на полу этажа, крышах, улицах, перилах. Без коллайдеров и частиц.
// Сетка клеток вокруг камеры (привязана к миру); в каждой клетке раз в «жизнь всплеска» — новое место.
// Высоту поверхности находим так:
//   • в зоне (красный модуль) — по карте укрытий: первая поверхность сверху (пол, навес, балка);
//   • вне зоны — лучом сверху вниз по буферу глубины камеры (screen-space): первая видимая поверхность.
// Каждый всплеск — 2 квада: кольцо на поверхности + корона с капельками (мультяшные, чёткие края).
Shader "CatCooks/City/Rain Splash"
{
    Properties
    {
        _RingAlpha ("Кольцо: непрозрачность", Range(0, 1)) = 0.6
        _CrownAlpha ("Корона: непрозрачность", Range(0, 1)) = 0.8
        _Spikes ("Корона: лепестков", Range(3, 9)) = 5
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+15" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "RainSplash"
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "CityRain.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _RingAlpha, _CrownAlpha, _Spikes;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 info : TEXCOORD1;   // x фаза 0..1, y тип (0 кольцо, 1 корона), z случайное, w гаснет вдали
                float3 view : TEXCOORD2;
            };

            static const float2 kCorner[6] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 0), float2(1, 1), float2(0, 1) };

            // экранные UV и глубина глаза точки
            bool ProjectUV(float3 wp, out float2 uv, out float eye)
            {
                float4 cs = TransformWorldToHClip(wp);
                eye = cs.w;
                uv = 0;
                if (cs.w <= 0.01) return false;
                float4 sp = ComputeScreenPos(cs);
                uv = sp.xy / sp.w;
                return all(uv > 0.002) && all(uv < 0.998);
            }

            float SceneEye(float2 uv)
            {
                float raw = LoadSceneDepth(uint2(uv * _ScreenParams.xy));
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            // Поверхность под точкой xz: первая видимая сверху (вне зоны). false — не нашли / не видна.
            bool MarchDown(float2 xz, float top, float bottom, out float y)
            {
                y = bottom;
                float yAir = top;
                bool air = false;
                float yHit = bottom;
                bool hit = false;
                [loop] for (int k = 1; k <= 20; k++)
                {
                    float yy = lerp(top, bottom, k / 20.0);
                    float2 uv; float eye;
                    if (!ProjectUV(float3(xz.x, yy, xz.y), uv, eye)) { air = false; continue; }
                    if (eye > SceneEye(uv)) { if (air) { hit = true; yHit = yy; } break; }   // ушли «за» поверхность
                    air = true; yAir = yy;                                                     // ещё видно воздух
                }
                if (!hit) return false;
                [unroll] for (int b = 0; b < 6; b++)
                {
                    float ym = (yAir + yHit) * 0.5;
                    float2 uv; float eye;
                    ProjectUV(float3(xz.x, ym, xz.y), uv, eye);
                    if (eye > SceneEye(uv)) yHit = ym; else yAir = ym;
                }
                y = yHit;
                float2 uv2; float eye2;
                if (!ProjectUV(float3(xz.x, y, xz.y), uv2, eye2)) return false;
                float thick = 0.25 * _CC_RainMeter + eye2 * 0.015;
                return abs(eye2 - SceneEye(uv2)) < thick;                                   // видна, не за стеной
            }

            Varyings vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                o.positionCS = float4(2, 2, 2, 1);
                float rate = _CC_RainSplash.x;
                if (rate < 0.001) return o;

                float N = _CC_RainSplash2.z;
                float C = _CC_RainSplash2.y;
                uint perCell = (uint)max(_CC_RainSplash2.w, 1.0);
                uint cellIdx = iid / perCell;
                uint sub = iid % perCell;
                float3 cam = GetCameraPositionWS();
                float2 camCell = floor(cam.xz / C);
                float2 cell = camCell + float2(cellIdx % (uint)N, cellIdx / (uint)N) - floor(N * 0.5);

                float life = max(_CC_RainSplash.y, 0.05);
                float ph0 = CR_Hash21(cell * 1.17 + sub * 7.3);
                float tt = _Time.y / life + ph0;
                float cyc = floor(tt);
                float f = frac(tt);
                float3 s = CR_Hash32(cell * 0.731 + float2(cyc * 1.618, sub * 3.1));
                if (s.z > rate) return o;

                float2 xz = (cell + s.xy) * C;
                float dist = max(length(xz - cam.xz), 1e-3);
                float maxD = N * 0.5 * C;
                if (CR_Hash11(s.x * 91.0 + cyc) > saturate(1.15 - dist / maxD)) return o;   // вдали реже
                // за спиной камеры — не считаем (экономия: половина сетки обычно не в кадре)
                float3 fwd = -UNITY_MATRIX_V[2].xyz;
                float fl = length(fwd.xz);
                if (fl > 0.25 && dist > 2.0 * _CC_RainMeter && dot((xz - cam.xz) / dist, fwd.xz / fl) < -0.15) return o;

                float y;
                float hOcc;
                float hBlk;
                bool onBlk = CR_BlockHeight(xz, hBlk);
                if (onBlk)
                {
                    // капля бьёт в купол зонта
                    y = hBlk;
                    float2 uvb; float eb;
                    if (!ProjectUV(float3(xz.x, y, xz.y), uvb, eb)) return o;
                    if (eb > SceneEye(uvb) + 0.3 * _CC_RainMeter + eb * 0.015) return o;
                }
                else if (CR_OccHeight(float3(xz.x, 0, xz.y), hOcc))
                {
                    y = hOcc;
                    float2 uvz; float ez;
                    if (!ProjectUV(float3(xz.x, y, xz.y), uvz, ez)) return o;
                    if (ez > SceneEye(uvz) + 0.3 * _CC_RainMeter + ez * 0.015) return o;      // закрыта чем-то ближе
                }
                else
                {
                    float top = cam.y + 25.0 * _CC_RainMeter;
                    float bottom = _CC_GroundY - 0.5 * _CC_RainMeter;
                    if (!MarchDown(xz, top, bottom, y)) return o;
                }

                float m = _CC_RainMeter;
                float3 c0 = float3(xz.x, y + 0.01 * m, xz.y);
                float3 toCam = cam - c0;
                float d = length(toCam);
                float3 vd = toCam / max(d, 1e-4);
                float size = _CC_RainSplash.z * (0.75 + 0.5 * CR_Hash11(s.x * 13.0 + cyc));
                float fade = 1.0 - smoothstep(maxD * 0.7, maxD, dist);

                uint q = vid / 6;
                float2 c = kCorner[vid % 6];
                float3 wp;
                if (q == 0)
                {
                    // кольцо — плоско на поверхности
                    wp = c0 + float3((c.x - 0.5) * 2.0 * size, 0, (c.y - 0.5) * 2.0 * size);
                }
                else
                {
                    if (_CC_RainSplash.w < 0.01) return o;
                    float3 right = cross(float3(0, 1, 0), vd);
                    float rl = length(right);
                    right = rl > 1e-4 ? right / rl : float3(1, 0, 0);
                    // квад шире формы (×1.4): капельки разлетаются в стороны; масштаб по X и Y один — форма не тянется
                    float hw = size * 0.9 * 1.4;
                    float hh = size * 0.9 * max(_CC_RainSplash.w, 0.3);
                    wp = c0 + right * (c.x - 0.5) * 2.0 * hw + float3(0, c.y * hh, 0);
                }
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = c;
                o.info = float4(f, q, s.x, fade);
                o.view = vd;
                return o;
            }

            float SegDist(float2 p, float2 a, float2 b, out float h)
            {
                float2 pa = p - a, ba = b - a;
                h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-6));
                return length(pa - ba * h);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float f = i.info.x;
                float rnd = i.info.z;
                float a = 0;
                if (i.info.y < 0.5)
                {
                    // КОЛЬЦО: бежит наружу и тает; на сильном дожде — второе, внутреннее
                    float2 p = i.uv * 2.0 - 1.0;
                    float r = length(p);
                    float e = 1.0 - pow(1.0 - f, 2.2);                    // быстрый старт, плавный конец
                    float th = lerp(0.16, 0.035, f);
                    float ring = 1.0 - CR_AAStep(th * 0.5, abs(r - e * 0.92));
                    float e2 = saturate(f * 1.6 - 0.25);
                    float ring2 = (1.0 - CR_AAStep(th * 0.35, abs(r - e2 * 0.55))) * step(0.35, _CC_RainSplash.x) * step(0.001, e2);
                    a = max(ring, ring2 * 0.6) * pow(1.0 - f, 1.4) * _RingAlpha;
                }
                else
                {
                    // КОРОНА: чаша воды раскрывается, лепестки взлетают веером, с кончиков отрываются капли
                    float g = saturate(f / 0.5);
                    float2 p = float2((i.uv.x * 2.0 - 1.0) * 1.4, i.uv.y);
                    float rise = sin(g * 3.14159);
                    float df = saturate((f - 0.32) / 0.68);
                    float sd = 1e3;
                    int n = (int)_Spikes;
                    [loop] for (int k = 0; k < n; k++)
                    {
                        float kk = (k + 0.5) / n * 2.0 - 1.0;
                        float hr = CR_Hash11(rnd * 31.0 + k * 1.7);
                        float2 base = float2(kk * 0.2 * (0.6 + 0.4 * g), 0.02);
                        float2 tip = float2(kk * (0.3 + 0.55 * g), (0.4 + 0.35 * (1.0 - abs(kk)) + 0.15 * (hr - 0.5)) * rise * 0.72);
                        float h;
                        float dd = SegDist(p, base, tip, h);
                        float w = lerp(0.11, 0.035, h) * (1.0 - g * 0.25);
                        sd = min(sd, dd - w + (1.0 - rise) * 0.09);
                        if (g < 0.85) sd = min(sd, length(p - tip) - 0.05 * (1.0 - g * 0.6));      // капля на кончике
                        if (_CC_RainSplash2.x > 0.01 && df > 0.0 && hr > 0.25)
                        {
                            float2 dp = tip + float2(kk * 0.5, 0.65 + 0.3 * hr) * df - float2(0, 1.1) * df * df;
                            sd = min(sd, length(p - dp) - 0.055 * (1.0 - df * 0.5) * _CC_RainSplash2.x);
                        }
                    }
                    // чаша: линза воды у основания, раскрывается и опадает
                    float2 bp = p / float2(0.3 + 0.35 * g, 0.05 * (1.0 - df) + 0.16 * rise);
                    float bowl = max((length(bp) - 1.0) * 0.12, -p.y) + df * 0.06;
                    sd = min(sd, bowl);
                    a = (1.0 - CR_AAStep(0.0, sd)) * (1.0 - smoothstep(0.6, 1.0, f)) * _CrownAlpha;
                }
                a *= i.info.w;
                float3 col = CR_Light(i.view) * 1.15;
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
