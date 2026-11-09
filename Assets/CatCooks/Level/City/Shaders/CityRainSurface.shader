// CatCooks — МОКРЫЕ ПОВЕРХНОСТИ, ЛЁД И СНЕГ поверх кадра (экранный проход, CityRainSurfaceFeature). Работает на ЛЮБЫХ
// материалах: по глубине кадра восстанавливаем точку и нормаль, мочим всё, что смотрит вверх:
//   ЭТАПЫ по мокрости: 0–0.33 тёмные капли (пятнышки копятся и сливаются) → 0.33–0.5 весь пол тёмный, блик →
//   0.5–0.75 лужицы в низинах → до 1 лужи растут, ливень заливает всё. Сохнет в обратном порядке (пятнами).
//   Маска: если на City Rain включено Wet Only Marked — мокнут только объекты с City Wet Surface.
//   • темнеет и насыщается, мультяшный блик полосой;
//   • лужи в плоских местах (растут с мокростью), в них отражения: экранные (окна, неон, дома) + небо;
//   • рябь от капель в лужах (без текстур), частота — от силы дождя.
// Мокрость: в зоне — карта мокрости (копится под открытым небом, под зонтом и навесом сухо, сохнет после дождя),
// вне зоны — общая. Вдали гаснет (город далеко мокнет своими шейдерами).
// ЗИМА (v2.1): замёрзшие лужи-плиты с объёмом (верх с преломлением Ice004, бортик, тень), иней по краю;
// снег с сугробами (гребни по ветру, рябь, крапинки тонкого слоя), мультяшный свет с тенями от солнца;
// крупные чёткие следы-лапки (считаются формулой, без пикселей); пелена снегопада/бурана с «занавесями» по ветру.
Shader "Hidden/CatCooks/City/Rain Surface"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "RainSurface"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "CityRain.hlsl"

            float  _CC_Wet;            // общая мокрость 0..1
            float  _CC_WetMapOn;
            float4 _CC_WetRect;        // xy минимум XZ, zw 1/размер
            TEXTURE2D(_CC_WetMap); SAMPLER(sampler_CC_WetMap);
            float4 _CC_WetP;           // x лужи (сколько), y размер пятна луж (ед.), z рябь, w дальность мокрости (ед.)
            float4 _CC_WetP2;          // x темнеть, y блик, z отражения, w дальность луж (ед.)
            float4 _CC_SkyZenith, _CC_SkyHorizon;
            float  _CC_WetMaskUse;
            // ── зима (CityWinter) ──
            float  _CC_WinterOn, _CC_Frost, _CC_Blizzard;              // _CC_SnowCover — в CityCommon.hlsl
            float4 _CC_FrostP;          // x лёд (сколько), y размер пятен льда (ед.), z иней, w трещины
            float4 _CC_IceP;            // x тайл текстуры льда (ед.), y —, z —, w непрозрачность льда
            float4 _CC_IceP2;           // x размер текстуры (пикс.), y есть текстура, z —, w блики-полоски
            float4 _CC_IceTint;         // цвет толщи льда
            TEXTURE2D(_CC_IceToon); SAMPLER(sampler_CC_IceToon);   // мультяшная текстура льда (цвет)
            TEXTURE2D(_CC_SnowToon); SAMPLER(sampler_CC_SnowToon); // мультяшный снег: R высота, G свет, B гребни
            float4 _CC_Amb;             // rgb — свет неба/окружения сверху (из RenderSettings), w — задан
            float  _CC_HailCover;       // град на полу 0..1 (CityHail)
            float  _CC_HeatOn;          // жара (CityHeat)
            float  _CC_FogOn;           // туман (CityFog)
            float4 _CC_Fog;             // x низкий, y вокруг этажа, z густой
            float4 _CC_FogP;            // x пол (мир Y), y высота низкого тумана (ед.), z плотность низкого (1/ед.), w длина стены вокруг (ед.)
            float4 _CC_FogP2;           // x видимость густого (ед.), y волны верха 0..1, zw дрейф (по ветру)
            float4 _CC_FogRect;         // xy минимум XZ игровой зоны, zw максимум
            float4 _CC_FogCol;          // цвет тумана (холодный, от окружения)
            float4 _CC_Heat;            // x марево, y миражи, z солнечный ореол, w зной (цвет)
            float4 _CC_HeatP;           // x сила дрожи (пикс.), y размер волн (пикс.), z скорость
            float4 _CC_SunScreen;       // xyz направление на солнце, w есть
            float  _CC_HailWet;         // мокрые пятна от растаявшего града 0..1
            float4 _CC_SnowTex;         // x тайл снега (ед.), y размер текстуры, z есть текстура, w барханы 0..1 (метель)
            float4 _CC_DriftP;          // xy направление ветра (медленное), z рельеф сугробов, w рябь
            float4 _CC_SnowVis;         // x видимость (ед.), y макс. пелена, z позёмка, w тени (0..1)
            float4 _CC_SnowFall;        // xyz накопленный сдвиг снега (ветер + падение)
            float4 _CC_PawP;            // x размер лапы (ед.), y сколько следов, z глубина
            float4 _CC_Paws[48];        // xy позиция XZ, z угол (рад), w засыпано 0..1
            float  _CC_WetDry;         // 0..1 «сохнет» (дождь кончился): пол сохнет пятнами, у луж влажная кайма
            TEXTURE2D_X(_CC_WetMask);

            // Тёмные капли: клетки ~9 см, в каждой пятнышко со своим порогом; растут и сливаются с мокростью
            float DropLayer(float2 p, float w, float seed)
            {
                float2 cell = floor(p), f = frac(p);
                float3 h = CR_Hash32(cell + seed);
                float appear = h.z * 0.92;                                     // когда пятнышко появляется (0..1 этапа)
                float grow = saturate((w - appear) / 0.35);
                float2 c = 0.2 + 0.6 * h.xy;
                float2 dv = f - c;
                dv.x *= 1.0 + 0.35 * (h.y - 0.5);                              // чуть овальные
                float d = length(dv);
                float r = lerp(0.12, 0.52, grow) * step(appear, w);
                float fw = max(fwidth(d), 1e-4);
                return saturate((r - d) / fw + 0.5);
            }

            float Drops(float2 xz, float stage)
            {
                float cs = 0.09 * _CC_RainMeter;
                float a = DropLayer(xz / cs, stage, 3.1);
                float b = DropLayer(xz / cs * 0.73 + 11.7, stage, 9.4);
                return max(a, b);
            }

            float SceneEyeAt(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            // Рябь: клетки, в каждой своё время и центр; кольцо бежит и тает. xy — наклон нормали, z — блик кольца
            float3 RippleLayer(float2 p, float t, float rate)
            {
                float2 cell = floor(p), f = frac(p);
                float3 h = CR_Hash32(cell + 17.0);
                float ph = frac(t * (0.8 + 0.4 * h.z) + h.z * 7.0);
                float on = step(h.x * 0.999, rate);
                float2 c = 0.25 + 0.5 * h.xy;
                float2 dv = f - c;
                float d = length(dv);
                float R = ph * 0.45;
                float x = clamp((d - R) * 30.0, -3.14159, 0.0);
                float ring = sin(x) * (1.0 - ph) * on;
                float2 tilt = d > 1e-4 ? dv / d * ring : 0;
                return float3(tilt, saturate(-ring));
            }

            float3 Ripples(float2 xz, float rate)
            {
                float cs = 0.42 * _CC_RainMeter;
                float t = _Time.y * 1.6;
                float3 a = RippleLayer(xz / cs, t, rate);
                float3 b = RippleLayer(xz / cs * 1.37 + 0.5, t * 1.13 + 0.3, rate);
                return a + b * 0.8;
            }

            // Отражение в луже: шаги по отражённому лучу в мире, проверка по буферу глубины; промах — небо
            float3 Reflect(float3 pos, float3 R, out float hit)
            {
                hit = 0;
                float m = _CC_RainMeter;
                float stepL = 0.5 * m;
                float3 p = pos + R * 0.2 * m;
                float3 res = 0;
                [loop] for (int k = 0; k < 14; k++)
                {
                    p += R * stepL;
                    stepL *= 1.32;
                    float4 cs = TransformWorldToHClip(p);
                    if (cs.w <= 0.01) break;
                    float4 sp = ComputeScreenPos(cs);
                    float2 uv = sp.xy / sp.w;
                    if (any(uv < 0.0) || any(uv > 1.0)) break;
                    float se = SceneEyeAt(uv);
                    if (cs.w > se && cs.w - se < 1.2 * m + cs.w * 0.06)
                    {
                        res = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                        float2 e = min(uv, 1.0 - uv);
                        hit = saturate(min(e.x, e.y) * 12.0);           // у краёв экрана гаснет
                        break;
                    }
                }
                return res;
            }

            float3 PosAt(float2 q)
            {
                return ComputeWorldSpacePosition(q, SampleSceneDepth(q), UNITY_MATRIX_I_VP);
            }

            half4 FragWet(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float raw = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                if (raw < 1e-6) return src;
            #else
                if (raw > 1.0 - 1e-6) return src;
            #endif
                float3 pos = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 cam = GetCameraPositionWS();
                float3 toCam = cam - pos;
                float dist = length(toCam);
                float3 v = toCam / max(dist, 1e-4);
                // нормаль по глубине: берём соседа с меньшим скачком (на стыках мешей не ломается — нет полос по стыкам)
                float2 tx = float2(1.0 / _ScreenParams.x, 0.0), ty = float2(0.0, 1.0 / _ScreenParams.y);
                float3 pl = PosAt(uv - tx), pr = PosAt(uv + tx), pd = PosAt(uv - ty), pu = PosAt(uv + ty);
                float3 ddxP = (length(pr - pos) < length(pos - pl)) ? pr - pos : pos - pl;
                float3 ddyP = (length(pu - pos) < length(pos - pd)) ? pu - pos : pos - pd;
                float3 n = cross(ddyP, ddxP);
                float nl = length(n);
                n = nl > 1e-8 ? n / nl : float3(0, 1, 0);
                if (dot(n, v) < 0.0) n = -n;
                float up = smoothstep(0.55, 0.85, n.y);
                if (up < 0.001) return src;

                // мокрость в этой точке
                float wet = _CC_Wet;
                if (_CC_WetMapOn > 0.5)
                {
                    float2 wuv = (pos.xz - _CC_WetRect.xy) * _CC_WetRect.zw;
                    if (all(wuv >= 0.0) && all(wuv <= 1.0))
                    {
                        float hb;
                        bool onTop = CR_BlockHeight(pos.xz, hb) && pos.y > hb - 0.25 * _CC_RainMeter;   // купол зонта сам мокнет
                        if (!onTop) wet = SAMPLE_TEXTURE2D_LOD(_CC_WetMap, sampler_CC_WetMap, wuv, 0).r;
                    }
                }
                if (_CC_WetMaskUse > 0.5)
                    wet *= LOAD_TEXTURE2D_X(_CC_WetMask, uint2(input.positionCS.xy)).r;   // мокнет только отмеченное
                wet *= up * (1.0 - smoothstep(_CC_WetP.w * 0.7, _CC_WetP.w, dist));
                if (wet < 0.003) return src;

                Light ml = GetMainLight();
                float3 col = src.rgb;
                // 1. темнеет и насыщается
                float luma = dot(col, float3(0.299, 0.587, 0.114));
                float3 wetCol = lerp(luma.xxx, col, 1.0 + 0.3 * wet) * lerp(1.0, 0.58, wet * _CC_WetP2.x);
                // 2. мультяшный блик полосой
                float3 hv = normalize(ml.direction + v);
                float spec = pow(saturate(dot(n, hv)), 48.0);
                float band = smoothstep(0.45, 0.55, spec);
                wetCol += ml.color * band * 0.35 * wet * _CC_WetP2.y;

                // этапы: плёнка (весь пол тёмный) от 0.25 до 0.5, раньше — только капли
                // сохнет неровно: крупные пятна высыхают раньше (шум ~1.6 м), мокрое держится дольше
                float patch = CR_Noise(pos.xz / (1.6 * _CC_RainMeter)) * 0.7 + CR_Noise(pos.xz / (0.55 * _CC_RainMeter) + 7.1) * 0.3 - 0.5;
                float film = smoothstep(0.25, 0.5, wet + patch * 0.4 * _CC_WetDry);
                float drops = (wet < 0.52) ? Drops(pos.xz, saturate(wet / 0.33)) : 1.0;
                float cover = max(film, drops * 0.9);
                wetCol = lerp(wetCol, wetCol - ml.color * band * 0.35 * wet * _CC_WetP2.y, 1.0 - film);   // капли матовые, блик — у плёнки

                // 3. лужи: плоско, близко, растут с мокростью (с 0.5)
                float pw = saturate((wet - 0.5) / 0.5);
                float pm = 0;
                if (pw > 0.001 && n.y > 0.93 && dist < _CC_WetP2.w)
                {
                    float2 q = pos.xz / max(_CC_WetP.y, 0.01);
                    float nz = CR_Noise(q) * 0.65 + CR_Noise(q * 2.7 + 5.3) * 0.35;
                    float edge = 1.05 - pw * _CC_WetP.x;
                    float fw = max(fwidth(nz), 1e-4);
                    pm = saturate((nz - edge) / fw + 0.5) * (1.0 - smoothstep(_CC_WetP2.w * 0.6, _CC_WetP2.w, dist));
                    float rim = saturate(1.0 - abs(nz - edge) / (fw * 1.5)) * 0.35;
                    if (pm > 0.002)
                    {
                        float rate = saturate(_CC_Rain * 1.3) * step(0.01, _CC_Rain);
                        float3 rp = rate > 0.0 ? Ripples(pos.xz, rate) * _CC_WetP.z : 0;
                        float3 pn = normalize(float3(rp.x * 0.6, 1.0, rp.y * 0.6));
                        float3 R = reflect(-v, pn);
                        float hit;
                        float3 refl = Reflect(pos, R, hit);
                        float3 sky = lerp(_CC_SkyHorizon.rgb, _CC_SkyZenith.rgb, saturate(R.y * 1.6));
                        refl = lerp(sky, refl, hit);
                        float fres = 0.25 + 0.65 * pow(1.0 - saturate(dot(v, pn)), 4.0);
                        float3 puddle = lerp(wetCol * 0.5, refl, fres * _CC_WetP2.z);
                        puddle += (ml.color * 0.25 + 0.2) * rp.z * 0.6;               // блик колец ряби
                        wetCol = lerp(wetCol, puddle, pm);
                    }
                    wetCol += rim * pw * 0.15 * (1.0 - pm);                              // светлая кромка лужи
                    // лужа уходит от краёв — вокруг остаётся тёмная влажная кайма
                    float halo = smoothstep(edge - 0.16, edge - 0.02, nz) * (1.0 - pm) * _CC_WetDry;
                    wetCol *= 1.0 - 0.22 * halo;
                }
                float strength = lerp(0.75, 1.0, film);                          // отдельные капли чуть светлее плёнки
                col = lerp(col, wetCol, cover * strength);
                return half4(col, src.a);
            }

            // Вороной: расстояние до ближайшего края клетки (для крупных трещин во льду)
            float CrackDist(float2 p)
            {
                float2 c = floor(p), f = frac(p);
                float d1 = 8.0, d2 = 8.0;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 o = float2(x, y);
                    float2 r = o + CR_Hash32(c + o).xy * 0.85 + 0.075 - f;
                    float d = dot(r, r);
                    if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                }
                return sqrt(d2) - sqrt(d1);
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float Noise3(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(Hash31(i), Hash31(i + float3(1, 0, 0)), f.x);
                float b = lerp(Hash31(i + float3(0, 1, 0)), Hash31(i + float3(1, 1, 0)), f.x);
                float c = lerp(Hash31(i + float3(0, 0, 1)), Hash31(i + float3(1, 0, 1)), f.x);
                float d = lerp(Hash31(i + float3(0, 1, 1)), Hash31(i + float3(1, 1, 1)), f.x);
                return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
            }

            float3 SkyAt(float3 R)
            {
                return lerp(_CC_SkyHorizon.rgb, _CC_SkyZenith.rgb, saturate(R.y * 1.6));
            }

            float SMin(float a, float b, float k)
            {
                float h = saturate(0.5 + 0.5 * (b - a) / k);
                return lerp(b, a, h) - k * h * (1.0 - h);
            }

            // ── КОШАЧЬЯ ЛАПА: расстояние до контура (в долях ширины лапы, y — вперёд) ──
            // подушечка с тремя долями снизу + 4 овальных пальчика дугой
            float PawSdf(float2 p)
            {
                float2 q = (p - float2(0.0, -0.13)) / float2(0.30, 0.215);
                float pad = (length(q) - 1.0) * 0.215;
                pad = SMin(pad, length(p - float2(-0.165, -0.29)) - 0.115, 0.07);
                pad = SMin(pad, length(p - float2(0.0, -0.335)) - 0.125, 0.07);
                pad = SMin(pad, length(p - float2(0.165, -0.29)) - 0.115, 0.07);
                float2 m = float2(abs(p.x), p.y);
                float t1 = (length((m - float2(0.335, 0.125)) / float2(0.098, 0.125)) - 1.0) * 0.098;   // крайние
                float t2 = (length((m - float2(0.122, 0.305)) / float2(0.104, 0.13)) - 1.0) * 0.104;    // средние
                return min(pad, min(t1, t2));
            }

            // Следы рядом с точкой: d0 глубина тут, d1 — чуть ближе к свету (для тени стенки), rim — валик
            void Paws(float2 xz, float2 lxz, float pixW, out float d0, out float d1, out float rim)
            {
                d0 = 0; d1 = 0; rim = 0;
                float P = max(_CC_PawP.x, 1e-3);
                float R2 = P * P * 0.42;
                int cnt = (int)_CC_PawP.y;
                float wall = max(0.03, pixW / P * 1.2);
                [loop] for (int i = 0; i < 48; i++)
                {
                    if (i >= cnt) break;
                    float4 pw = _CC_Paws[i];
                    float2 dl = xz - pw.xy;
                    if (dot(dl, dl) > R2) continue;
                    float sn, cs;
                    sincos(pw.z, sn, cs);
                    float2 fwd = float2(sn, cs), side = float2(cs, -sn);
                    float2 lp = float2(dot(dl, side), dot(dl, fwd)) / P;
                    float2 lo = float2(dot(lxz, side), dot(lxz, fwd)) * 0.045;
                    float dep = 1.0 - pw.w;
                    float a0 = PawSdf(lp), a1 = PawSdf(lp + lo);
                    d0 = max(d0, saturate(-a0 / wall + 0.5) * dep);
                    d1 = max(d1, saturate(-a1 / wall + 0.5) * dep);
                    rim = max(rim, saturate(1.0 - abs(a0 - 0.05) / 0.04) * dep);
                }
            }

            // ── СУГРОБЫ: высота снега (метры на входе). Сугробики ~3×1 м по ветру, бугорки, рябь поперёк ветра ──
            float DriftH(float2 xz)
            {
                float2 wd = _CC_DriftP.xy;
                float2 r = float2(dot(xz, wd), dot(xz, float2(-wd.y, wd.x)));
                return CR_Noise(float2(r.x * 0.35, r.y * 1.0)) * 0.65 + CR_Noise(xz * 1.6 + 3.7) * 0.35;
            }

            // где снег толще (для порога покрытия)
            float SnowHC(float2 xm, float h)
            {
                return h * 0.85 + CR_Noise(xm * 5.0 + 2.2) * 0.15;
            }

            // ── ЛЁД: замёрзшие лужи (шум в мировых единицах) ──
            float IceMask(float2 xz)
            {
                float2 q = xz / max(_CC_FrostP.y, 0.01);
                return CR_Noise(q) * 0.62 + CR_Noise(q * 2.1 + 4.1) * 0.3 + CR_Noise(q * 5.3 + 1.7) * 0.08;
            }

            // ── ИСКОРКА: в клетке одна круглая точка 1–2 пикселя на экране, мягко мерцает (без полос и дуг) ──
            float Glitter(float3 pos, float2 uv, float cell, float density, float footprint)
            {
                float2 gc = floor(pos.xz / cell);
                float3 h = CR_Hash32(gc + 7.7);
                if (h.z > density) return 0.0;
                float3 cp = float3((gc.x + 0.2 + 0.6 * h.x) * cell, pos.y, (gc.y + 0.2 + 0.6 * h.y) * cell);
                float4 sp = ComputeScreenPos(TransformWorldToHClip(cp));
                float2 dpx = (sp.xy / max(sp.w, 1e-4) - uv) * _ScreenParams.xy;
                float tw = sin(_Time.y * (0.8 + h.x * 1.5) + h.y * 40.0) * 0.5 + 0.5;
                tw = tw * tw * tw;
                float fade = saturate(cell / max(footprint, 1e-5) * 0.25 - 0.5);       // клетка меньше ~3 пикс — не искрим
                return saturate(1.4 - length(dpx)) * tw * fade;
            }

            // ── Мягкое укрытие для зимы: 9 проб карты укрытий (±0.4 м) + зонты. Квадратов карты не видно. ──
            float WinterCover(float3 p)
            {
                float hb;
                if (CR_BlockHeight(p.xz, hb) && p.y < hb) return 1.0;
                if (_CC_RainOccOn < 0.5) return 0.0;
                float m = _CC_RainMeter;
                float acc = 0.0;
                float h;
                if (CR_OccHeight(p, h) && p.y < h - 0.05 * m) acc += 1.0;
                if (CR_OccHeight(p + float3(0.5 * m, 0, 0), h) && p.y < h - 0.05 * m) acc += 1.0;
                if (CR_OccHeight(p - float3(0.5 * m, 0, 0), h) && p.y < h - 0.05 * m) acc += 1.0;
                if (CR_OccHeight(p + float3(0, 0, 0.5 * m), h) && p.y < h - 0.05 * m) acc += 1.0;
                if (CR_OccHeight(p - float3(0, 0, 0.5 * m), h) && p.y < h - 0.05 * m) acc += 1.0;
                return smoothstep(0.55, 1.0, acc / 5.0);                     // балки и тонкие перекрытия снег не прячут — только сплошная крыша
            }

            // ── СНЕЖНАЯ ПЕЛЕНА: «занавеси» снега вдоль луча, едут по ветру (объёмный шум, 6 проб) ──
            float SnowCurtain(float3 cam, float3 dir, float maxD, float m)
            {
                float2 wd = _CC_DriftP.xy;
                float3 adv = _CC_SnowFall.xyz * float3(0.9, 0.25, 0.9);
                float acc = 0.0, ws = 0.0;
                float d = 1.6 * m;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float w = d < maxD ? 1.0 : 0.0;
                    float3 p = cam + dir * min(d, maxD) - adv;
                    float along = dot(p.xz, wd), across = dot(p.xz, float2(-wd.y, wd.x));
                    float3 q = float3(along * 0.3, p.y * 0.7, across) / (2.6 * m);
                    float n = Noise3(q) * 0.62 + Noise3(q * 2.9 + 5.1) * 0.38;
                    acc += n * w;
                    ws += w;
                    d *= 2.4;
                }
                return ws > 0.0 ? acc / ws : 0.5;
            }

            // ЗИМА v2.3: лужи льда + снег мягким слоем поверх. Производные (fwidth) — только вне ветвлений по пикселю
            // (ветки по глобальным значениям _CC_Frost / _CC_SnowCover одинаковы для всего кадра — там можно).
            half4 Winter(Varyings input, half4 c)
            {
                float2 uv = input.texcoord;
                float raw = SampleSceneDepth(uv);
                float m = _CC_RainMeter;
                float3 cam = GetCameraPositionWS();
            #if UNITY_REVERSED_Z
                bool skyPix = raw < 1e-6;
            #else
                bool skyPix = raw > 1.0 - 1e-6;
            #endif
                float3 pos = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 toCam = cam - pos;
                float dist = skyPix ? 1e5 : length(toCam);
                float3 v = toCam / max(length(toCam), 1e-4);
                Light ml = GetMainLight();
                // окружение берём из глобала (_CC_Amb ставит CityWinter): в полноэкранном проходе SH объекта не задан — снег выходил серым
                float3 amb = _CC_Amb.w > 0.5 ? _CC_Amb.rgb : max(SampleSH(float3(0, 1, 0)), 0.0);
                float3 L = ml.direction;
                // тени — из самой картинки (мягкие тени движка), карту теней не трогаем: дёшево и без «лесенки»
                const float3 kLum = float3(0.299, 0.587, 0.114);
                float lumRef = dot(amb * 0.6 + ml.color * saturate(L.y) * 0.6, kLum);
                float sh = skyPix ? 1.0 : lerp(1.0, smoothstep(0.45, 0.9, dot(c.rgb, kLum) / max(lumRef, 0.02)), _CC_SnowVis.w);
                float2 lxz = L.xz;
                float lxl = length(lxz);
                lxz = lxl > 1e-3 ? lxz / lxl : float2(0.6, 0.8);
                float3 col = c.rgb;

                // нормаль по глубине: берём соседа с меньшим скачком (на стыках мешей не ломается — нет полос по стыкам)
                float2 tx = float2(1.0 / _ScreenParams.x, 0.0), ty = float2(0.0, 1.0 / _ScreenParams.y);
                float3 pl = PosAt(uv - tx), pr = PosAt(uv + tx), pd = PosAt(uv - ty), pu = PosAt(uv + ty);
                float3 ddxP = (length(pr - pos) < length(pos - pl)) ? pr - pos : pos - pl;
                float3 ddyP = (length(pu - pos) < length(pos - pd)) ? pu - pos : pos - pd;
                float3 n = cross(ddyP, ddxP);
                float nl = length(n);
                n = nl > 1e-8 ? n / nl : float3(0, 1, 0);
                if (dot(n, v) < 0.0) n = -n;
                float ndv = saturate(dot(n, v));
                float pixW = dist * 2.0 / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);
                float foot = pixW / max(ndv, 0.15);
                float cover = skyPix ? 1.0 : WinterCover(pos);                              // 1 — под сплошной крышей / зонтом

                // ── маски льда (только если мороз — ветка по глобальному значению) ──
                float ice = 0, bevel = 0, rimI = 0, rime = 0;
                if (_CC_Frost > 0.001)
                {
                    float edgeI = 1.0 - _CC_Frost * _CC_FrostP.x * 0.28 + cover;                    // лёд — редкими лужами
                    float mI = IceMask(pos.xz);
                    float fwI = max(fwidth(mI), 1e-4);
                    ice = saturate((mI - edgeI) / fwI + 0.5);
                    bevel = saturate(1.0 - (mI - edgeI) / (fwI * 2.5 + 0.03)) * ice;
                    rimI = saturate(1.0 - abs(mI - edgeI) / (fwI * 2.0 + 0.06)) * (1.0 - ice);
                    float rn = CR_Noise(pos.xz / (0.12 * m)) * 0.6 + CR_Noise(pos.xz / (0.4 * m)) * 0.4;
                    rime = saturate((rn - (1.0 - _CC_Frost * 0.5 * _CC_FrostP.z) - cover) / max(fwidth(rn), 1e-4) + 0.5) * 0.3;
                }

                // ── маска снега: мультяшная текстура барханов-ряби (R высота, G запечённый свет, B гребни) ──
                // сначала снег ложится кучками на вершинах сугробиков, потом заполняет всё
                float snow = 0, t0 = 0, sShade = 1, sCrest = 0;
                if (_CC_SnowCover > 0.001)
                {
                    float stile = max(_CC_SnowTex.x, 1e-3);
                    float4 st = float4(0.5, 0.92, 0, 0);
                    if (_CC_SnowTex.z > 0.5)
                    {
                        float slod = max(0.0, log2(max(foot / stile * _CC_SnowTex.y, 1e-4)));
                        float2 suv = pos.xz / stile;
                        st = SAMPLE_TEXTURE2D_LOD(_CC_SnowToon, sampler_CC_SnowToon, suv, slod);
                    }
                    float big = CR_Noise(pos.xz / (4.0 * m) + 3.1);                          // крупная неровность — плитка не повторяется
                    float drift = _CC_SnowTex.w;                                             // 0 — ровный слой (снегопад), 1 — барханы (метель)
                    float hcFlat = 0.55 + (big - 0.5) * 0.25;                                // ровный слой: ложится почти везде одинаково
                    float hcDrift = st.r * 0.6 + big * 0.4;
                    float hc = lerp(hcFlat, hcDrift, drift);
                    float thr = 1.0 - _CC_SnowCover * 1.15 + cover * 1.2;
                    float wE = lerp(0.18, 0.05, drift);                                      // ровный слой — очень мягкий край
                    snow = smoothstep(thr - wE, thr + wE, hc);
                    t0 = saturate((hc - thr) * 3.0);
                    snow *= lerp(saturate(0.35 + _CC_SnowCover * 0.9), 1.0, drift);          // тонкий слой — полупрозрачный, сквозь него виден пол
                    sShade = lerp(0.93, st.g, drift);
                    sCrest = st.b * drift;
                }

                if (!skyPix)
                {
                    float up = smoothstep(0.55, 0.85, n.y);
                    float k = up * (1.0 - smoothstep(_CC_WetP.w * 0.7, _CC_WetP.w, dist));
                    if (_CC_WetMaskUse > 0.5) k *= LOAD_TEXTURE2D_X(_CC_WetMask, uint2(input.positionCS.xy)).r;
                    k *= 1.0 - cover;
                    if (k > 0.001)
                    {
                        // ════ ЛЁД ════
                        if (_CC_Frost > 0.001)
                        {
                            float3 rimeCol = float3(0.88, 0.94, 1.0) * (amb * 1.1 + ml.color * 0.45 * sh);
                            col = lerp(col, rimeCol, saturate(rime + rimI * 0.75) * k);
                            if (ice > 0.002)
                            {
                                // МУЛЬТЯШНЫЙ ЛЁД: одна выборка цветной текстуры (плиты, фаски, царапины-блики), бесшовная
                                float tile = max(_CC_IceP.x, 1e-3);
                                float3 tc = float3(0.62, 0.84, 0.96);
                                if (_CC_IceP2.y > 0.5)
                                {
                                    float lod = max(0.0, log2(max(foot / tile * _CC_IceP2.x, 1e-4)));
                                    tc = SAMPLE_TEXTURE2D_LOD(_CC_IceToon, sampler_CC_IceToon, pos.xz / tile, lod).rgb;
                                }
                                float3 lightIce = amb * 1.25 + ml.color * 0.55 * sh * saturate(L.y * 1.3 + 0.2);
                                float3 iceCol = lerp(col, tc * lightIce * 1.05, 0.8 + 0.2 * _CC_IceP.w);   // чуть-чуть просвечивает пол
                                float3 white = float3(0.95, 0.98, 1.0) * (amb * 1.3 + ml.color * 0.6 * sh);
                                iceCol = lerp(iceCol, white, bevel * 0.6);                                  // светлая кромка лужи
                                col = lerp(col, iceCol, ice * k);
                            }
                        }
                        // ════ СНЕГ (поверх льда) ════
                        if (_CC_SnowCover > 0.001 && snow > 0.002)
                        {
                            float3 snowBase = float3(0.95, 0.97, 1.0);
                            float sun = saturate(L.y * 1.3 + 0.15);
                            float3 litC = snowBase * (amb * 1.15 + ml.color * 0.8 * sun * sh);
                            float3 shC = snowBase * amb * float3(0.68, 0.78, 1.0) * 1.25;
                            float3 snowCol = lerp(shC, litC, sShade);                                  // кучки: светлый верх, голубая тень
                            snowCol += sCrest * 0.1 * (ml.color * sh + amb);
                            snowCol += Glitter(pos, uv, 0.07 * m, 0.08, foot) * (0.2 + ml.color * 0.6 * sh);
                            // следы-лапки
                            float alpha = snow * (0.6 + 0.4 * saturate(t0 * 2.0));                 // у края — тонкий полупрозрачный слой
                            if (_CC_PawP.y > 0.5)
                            {
                                float d0, d1, rim;
                                Paws(pos.xz, lxz * _CC_PawP.x, pixW, d0, d1, rim);
                                if (d0 + rim > 0.001)
                                {
                                    float3 cSh = snowBase * amb * float3(0.72, 0.8, 1.0) * 1.2;
                                    float shadowW = saturate((d0 - d1) * 2.5);
                                    float litW = saturate((d1 - d0) * 2.5);
                                    snowCol = lerp(snowCol, lerp(shC, litC, 0.85), saturate(d0 + rim * 0.6));          // в следе рябь примята — ровно
                                    snowCol = lerp(snowCol, snowCol * float3(0.82, 0.88, 0.98), d0 * 0.5 * _CC_PawP.z);
                                    snowCol = lerp(snowCol, cSh, shadowW * 0.8 * _CC_PawP.z);
                                    snowCol += litW * 0.2 * (ml.color * sh + amb * 0.3) * _CC_PawP.z;
                                    snowCol += rim * (1.0 - d0) * 0.06 * (ml.color * sh + amb);
                                    alpha *= 1.0 - d0 * saturate(1.0 - t0 * 2.0) * 0.8;
                                }
                            }
                            col = lerp(col, snowCol, alpha * k);
                        }
                    }
                }

                // ════ ПЕЛЕНА ════
                if (_CC_SnowVis.y > 0.001)
                {
                    float3 dir = -v;
                    float cn = SnowCurtain(cam, dir, min(dist, 70.0 * m), m);
                    float dens = 0.4 + 2.0 * cn * cn;
                    float veil = (1.0 - exp(-dist / max(_CC_SnowVis.x, 1e-3) * dens)) * _CC_SnowVis.y;
                    float fwdS = pow(saturate(dot(dir, L)), 6.0);
                    float3 veilCol = (amb * 1.15 + ml.color * (0.12 + 0.35 * fwdS) + 0.05) * float3(0.92, 0.95, 1.0);
                    float luma = dot(col, float3(0.299, 0.587, 0.114));
                    col = lerp(col, lerp(col, luma.xxx, 0.35), _CC_Blizzard * (1.0 - veil));
                    col = lerp(col, veilCol, saturate(veil));
                }
                return half4(col, c.a);
            }

            // ══ ГРАД НА ПОЛУ: россыпь неровных льдинок (молочное ядро, насыщенный голубой край, тёмный контур, блик);
            //    где градина растаяла — остаётся мокрое тёмное пятнышко (_CC_HailWet сохнет дольше, чем тает лёд) ══
            float HailBeads(float2 xz, float cell, float cover, float wet, float pixW, out float2 dOut, out float rOut, out float wetSpot, out float seed)
            {
                float a = 0.0;
                dOut = 0; rOut = 1; wetSpot = 0; seed = 0;
                float2 q = xz / cell;
                float aa = pixW / cell * 1.2 + 1e-4;
                [unroll] for (int j = 0; j < 3; j++)
                {
                    float2 c = floor(q), f = frac(q);
                    float3 h = CR_Hash32(c + j * 13.7);
                    float lim = 0.75 - j * 0.15;
                    float on = step(h.z, cover * lim);
                    float was = step(h.z, wet * lim) * (1.0 - on);                                   // была градина, растаяла
                    float2 ctr = 0.25 + 0.5 * h.xy;
                    float r = (0.1 + 0.12 * frac(h.z * 37.1)) * (0.7 + 0.3 * saturate(max(cover, wet) * 1.5));
                    float2 d = f - ctr;
                    float an = atan2(d.y, d.x);
                    float R = r * (1.0 + 0.16 * sin(an * 3.0 + h.x * 6.0) + 0.08 * sin(an * 5.0 + h.y * 9.0));   // неровная
                    float b = saturate((R - length(d)) / aa + 0.5) * on;
                    if (b > a) { a = b; dOut = d; rOut = R; seed = h.x; }
                    wetSpot = max(wetSpot, saturate((r * 1.5 - length(d)) / (aa + r * 0.5)) * was);
                    q = q * 1.41 + float2(0.37, 0.71);
                    aa *= 1.41;
                }
                return a;
            }

            half4 HailGround(Varyings input, half4 c)
            {
                float2 uv = input.texcoord;
                float raw = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                if (raw < 1e-6) return c;
            #else
                if (raw > 1.0 - 1e-6) return c;
            #endif
                float m = _CC_RainMeter;
                float3 pos = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 cam = GetCameraPositionWS();
                float dist = length(cam - pos);
                float3 v = (cam - pos) / max(dist, 1e-4);
                float2 tx = float2(1.0 / _ScreenParams.x, 0.0), ty = float2(0.0, 1.0 / _ScreenParams.y);
                float3 pl = PosAt(uv - tx), pr = PosAt(uv + tx), pd = PosAt(uv - ty), pu = PosAt(uv + ty);
                float3 ddxP = (length(pr - pos) < length(pos - pl)) ? pr - pos : pos - pl;
                float3 ddyP = (length(pu - pos) < length(pos - pd)) ? pu - pos : pos - pd;
                float3 n = cross(ddyP, ddxP);
                float nl = length(n);
                n = nl > 1e-8 ? n / nl : float3(0, 1, 0);
                if (dot(n, v) < 0.0) n = -n;
                float k = smoothstep(0.55, 0.85, n.y) * (1.0 - smoothstep(_CC_WetP.w * 0.7, _CC_WetP.w, dist));
                if (_CC_WetMaskUse > 0.5) k *= LOAD_TEXTURE2D_X(_CC_WetMask, uint2(input.positionCS.xy)).r;
                k *= 1.0 - WinterCover(pos);                                                     // под крышей града нет
                if (k < 0.001) return c;
                float pixW = dist * 2.0 / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);
                float foot = pixW / max(saturate(dot(n, v)), 0.15);
                float cell = 0.12 * m;                                                          // льдинки на полу крупнее и реже
                float2 d; float r, wetSpot, seed;
                float bead = HailBeads(pos.xz, cell, _CC_HailCover, _CC_HailWet, foot, d, r, wetSpot, seed);
                float nearF = saturate(cell / max(foot, 1e-5) * 0.33 - 0.5);
                Light ml = GetMainLight();
                float3 amb = _CC_Amb.w > 0.5 ? _CC_Amb.rgb : max(SampleSH(float3(0, 1, 0)), 0.0);
                float3 lightC = amb * 1.15 + ml.color * 0.7;
                float3 col = c.rgb;
                // мокрые пятна от растаявших градин: темнее, чуть насыщеннее, с бликом
                float luma = dot(col, float3(0.299, 0.587, 0.114));
                float3 wetCol = lerp(luma.xxx, col, 1.3) * 0.62;
                col = lerp(col, wetCol, wetSpot * nearF * k * 0.85);
                // льдинки
                float2 dn = d / max(r, 1e-4);
                float t = length(dn);
                float3 base = lerp(float3(0.92, 0.97, 1.0), float3(0.32, 0.68, 1.0), smoothstep(0.15, 0.7, t));
                float rings = frac(t * 2.4 + seed * 3.0);
                base = lerp(base, float3(0.92, 0.97, 1.0), smoothstep(0.82, 0.97, rings) * 0.5 * step(0.3, t));
                float3 beadCol = base * lightC;
                beadCol += pow(saturate(1.0 - length(dn - float2(-0.35, -0.35)) * 2.4), 2.0) * (ml.color * 0.9 + 0.35);
                beadCol = lerp(beadCol, float3(0.08, 0.26, 0.55) * lightC, saturate((t - 0.8) * 6.0));
                col = lerp(col, beadCol, bead * nearF * k);
                col = lerp(col, lightC * float3(0.75, 0.9, 1.0), _CC_HailCover * 0.3 * (1.0 - nearF) * k);   // вдали — голубоватая россыпь
                return half4(col, c.a);
            }

            // ══ ЖАРА ══
            // МАРЕВО: воздух дрожит — картинка колышется и «течёт» вверх. Сильнее всего над раскалённым полом вскользь
            // (дальше 3 м) и вдали у горизонта; передние предметы в дрожь фона не затягиваются.
            // МИРАЖ: вдали на полу вскользь появляются «лужи» отражённого неба и домов — как над асфальтом в зной.
            half4 HeatHaze(Varyings input)
            {
                float2 uv = input.texcoord;
                float m = _CC_RainMeter;
                float raw = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                bool skyPix = raw < 1e-6;
            #else
                bool skyPix = raw > 1.0 - 1e-6;
            #endif
                float3 pos = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 cam = GetCameraPositionWS();
                float3 toCam = cam - pos;
                float dist = skyPix ? 1e4 : length(toCam);
                float3 v = toCam / max(length(toCam), 1e-4);
                float3 n = cross(ddy(pos), ddx(pos));
                float nl = length(n);
                n = nl > 1e-8 ? n / nl : float3(0, 1, 0);
                if (dot(n, v) < 0.0) n = -n;
                float up = skyPix ? 0.0 : smoothstep(0.6, 0.9, n.y);
                float graze = up * saturate(1.0 - v.y * 3.0) * saturate((dist - 3.0 * m) / (8.0 * m));   // пол вскользь
                float far = skyPix ? saturate(1.0 - abs(v.y) * 5.0) : saturate((dist - 8.0 * m) / (50.0 * m));   // горизонт и даль
                float str = _CC_Heat.x * saturate(far * 0.55 + graze * 0.9);
                // дрожь: волны, бегущие вверх (в пикселях — одинаково на любом экране)
                float t = _Time.y * _CC_HeatP.z;
                float2 sp = uv * _ScreenParams.xy / max(_CC_HeatP.y, 1.0);
                float n1 = sin(sp.y * 1.7 - t * 4.0 + sin(sp.x * 0.9 + t * 1.3) * 1.6);
                float n2 = sin(sp.y * 3.3 - t * 6.7 + sin(sp.x * 1.9 - t * 0.8) * 2.2);
                float n3 = sin(sp.x * 0.6 + sp.y * 0.4 - t * 2.1);
                float2 off = float2(n1 * 0.55 + n2 * 0.35 + n3 * 0.1, (n2 - n1) * 0.3) * str * _CC_HeatP.x / _ScreenParams.xy;
                float2 uv2 = clamp(uv + off, 0.001, 0.999);
                // не тянуть в дрожь передние предметы
                float e2 = LinearEyeDepth(SampleSceneDepth(uv2), _ZBufferParams);
                float e1 = skyPix ? 1e4 : LinearEyeDepth(raw, _ZBufferParams);
                if (e2 < e1 * 0.85 - 0.3 * m) uv2 = uv;
                half4 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv2, 0);
                // мираж: «лужи» неба и отражений вдали на полу
                if (_CC_Heat.y > 0.001 && !skyPix)
                {
                    float mg = up * saturate(1.0 - v.y * 7.0) * saturate((dist - 12.0 * m) / (20.0 * m));
                    if (mg > 0.001)
                    {
                        float2 q = pos.xz / (5.0 * m) + float2(_Time.y * 0.05, 0.0);
                        float pn = CR_Noise(q) * 0.65 + CR_Noise(q * 2.3 + 3.1) * 0.35;
                        float pool = smoothstep(0.42, 0.62, pn + sin(_Time.y * 1.7 + pos.x * 0.3 / m) * 0.04) * mg * _CC_Heat.y;
                        if (pool > 0.002)
                        {
                            float3 R = reflect(-v, float3(0, 1, 0));
                            R.xz += float2(n1, n2) * 0.02;                                       // отражение дрожит
                            float hit;
                            float3 refl = Reflect(pos, normalize(R), hit);
                            float3 sky = lerp(_CC_SkyHorizon.rgb, _CC_SkyZenith.rgb, saturate(R.y * 1.6)) * 1.1;
                            refl = lerp(sky, refl, hit * 0.8);
                            c.rgb = lerp(c.rgb, refl, pool * 0.75);
                        }
                    }
                }
                return c;
            }

            // ЗНОЙ (цвет): тёплый пересвет и насыщенность, выбеленная тёплая даль, солнечный ореол (гаснет, если солнце
            // закрыто), в пекле — оранжевые дрожащие края экрана.
            half4 HeatGrade(Varyings input, half4 c)
            {
                float2 uv = input.texcoord;
                float m = _CC_RainMeter;
                float g = _CC_Heat.w;
                float3 col = c.rgb;
                float raw = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                bool skyPix = raw < 1e-6;
            #else
                bool skyPix = raw > 1.0 - 1e-6;
            #endif
                float dist = skyPix ? 1e4 : LinearEyeDepth(raw, _ZBufferParams);
                if (g > 0.001)
                {
                    float luma = dot(col, float3(0.299, 0.587, 0.114));
                    col = lerp(luma.xxx, col, 1.0 + 0.18 * g);                                     // сочнее
                    col *= lerp(float3(1, 1, 1), float3(1.14, 1.0, 0.8), g * 0.6);                 // теплее
                    col = col * (1.0 + 0.22 * g) + 0.03 * g;                                       // пересвет
                    float far = skyPix ? 0.6 : saturate((dist - 15.0 * m) / (90.0 * m));
                    float3 warmHaze = _CC_SkyHorizon.rgb * float3(1.15, 1.02, 0.85) + 0.05;
                    col = lerp(col, warmHaze, far * 0.4 * g);                                      // даль выбелена жаром
                }
                // солнечный ореол
                float4 scs = TransformWorldToHClip(GetCameraPositionWS() + _CC_SunScreen.xyz * 1000.0);
                float4 ssp = ComputeScreenPos(scs);
                float2 sunUV = ssp.xy / max(ssp.w, 1e-4);
                bool sunOn = _CC_SunScreen.w > 0.5 && scs.w > 0.0 && all(sunUV > -0.3) && all(sunUV < 1.3);
                if (_CC_Heat.z > 0.001 && sunOn)
                {
                    float2 d = (uv - sunUV) * float2(_ScreenParams.x / _ScreenParams.y, 1.0);
                    float r = length(d);
                    float rawS = SampleSceneDepth(clamp(sunUV, 0.001, 0.999));
                #if UNITY_REVERSED_Z
                    float sunSky = rawS < 1e-6 ? 1.0 : 0.25;
                #else
                    float sunSky = rawS > 1.0 - 1e-6 ? 1.0 : 0.25;
                #endif
                    float pulse = 1.0 + 0.06 * sin(_Time.y * 2.3);
                    float glare = (exp(-r * 2.6) * 0.35 + exp(-r * 11.0) * 0.6) * pulse;
                    float2 gp = sunUV - (sunUV - 0.5) * 2.0;                  // блик-«призрак» напротив солнца
                    float ghost = exp(-length((uv - lerp(sunUV, gp, 0.65)) * float2(_ScreenParams.x / _ScreenParams.y, 1.0)) * 18.0) * 0.12;
                    col += float3(1.0, 0.82, 0.55) * (glare + ghost) * _CC_Heat.z * sunSky;
                }
                // пекло: края экрана оранжевые и дрожат
                float hot = saturate((g - 0.6) / 0.4);
                if (hot > 0.001)
                {
                    float2 cuv = uv - 0.5;
                    float wob = sin(uv.y * 40.0 - _Time.y * 5.0) * 0.015 + sin(uv.x * 23.0 + _Time.y * 3.0) * 0.01;
                    float vig = smoothstep(0.45, 0.95, length(cuv * float2(1.2, 1.0)) * 1.45 + wob);
                    col = lerp(col, col * float3(1.18, 0.82, 0.55) + float3(0.06, 0.02, 0.0), vig * 0.45 * hot);
                }
                return half4(col, c.a);
            }

            // ══ ТУМАН (CityFog) ══
            // НИЗКИЙ: слой от пола до половины роста игрока, верх слоя волнуется и плывёт по ветру; пол под ним не видно.
            // ВОКРУГ ЭТАЖА: за краем игровой зоны и ниже пола всё тонет в тумане — этаж как остров.
            // ГУСТОЙ: туман везде, видимость несколько метров, внутри медленно плывут клубы.
            float FogLayerTop(float2 xz, float t)
            {
                float2 q = xz / (2.2 * _CC_RainMeter) + _CC_FogP2.zw * t;
                float w = CR_Noise(q) * 0.6 + CR_Noise(q * 2.7 + 5.3) * 0.4 - 0.5;
                return _CC_FogP.x + _CC_FogP.y * (1.0 + w * _CC_FogP2.y);
            }

            half4 Fog(Varyings input, half4 c)
            {
                float2 uv = input.texcoord;
                float m = _CC_RainMeter;
                float raw = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                bool skyPix = raw < 1e-6;
            #else
                bool skyPix = raw > 1.0 - 1e-6;
            #endif
                float3 cam = GetCameraPositionWS();
                float3 pos = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 dir = pos - cam;
                float dist = length(dir);
                dir /= max(dist, 1e-4);
                if (skyPix) dist = 200.0 * m;
                float3 P = cam + dir * dist;
                float t = _Time.y;
                float od = 0.0;

                // 1. низкий туман: длина пути луча внутри слоя (верх волнуется) × плотность × клубы
                if (_CC_Fog.x > 0.001)
                {
                    float topP = FogLayerTop(P.xz, t);
                    float topC = FogLayerTop(cam.xz, t);
                    float y0 = cam.y, y1 = P.y;
                    float inLen;
                    if (y0 <= topC && y1 <= topP) inLen = dist;                                  // камера в тумане
                    else if (y1 < topP && y0 > y1)
                    {
                        float top = topP;                                                         // от входа в слой до точки
                        inLen = dist * saturate((top - y1) / max(y0 - y1, 1e-4));
                    }
                    else if (y0 < topC && y1 > y0) inLen = dist * saturate((topC - y0) / max(y1 - y0, 1e-4));
                    else inLen = 0.0;
                    // мягкий верх: чуть выше слоя — тонкая дымка
                    float soft = saturate(1.0 - (y1 - topP) / max(_CC_FogP.y * 0.35, 1e-3)) * step(topP, y1) * 0.25 * min(dist, 4.0 * m);
                    inLen += soft;
                    // клубы внутри слоя
                    float3 mid = P - dir * inLen * 0.5;
                    float3 qn = float3(mid.x, mid.y * 2.0, mid.z) / (1.6 * m) + float3(_CC_FogP2.z, 0.0, _CC_FogP2.w) * t * 1.3;
                    float puff = 0.55 + 0.9 * (Noise3(qn) * 0.65 + Noise3(qn * 2.3 + 7.1) * 0.35);
                    od += inLen * _CC_FogP.z * puff * _CC_Fog.x;
                }

                // 2. вокруг этажа: за краем зоны и ниже пола — стена тумана
                if (_CC_Fog.y > 0.001)
                {
                    float2 dOut2 = max(max(_CC_FogRect.xy - P.xz, P.xz - _CC_FogRect.zw), 0.0);
                    float outD = length(dOut2);
                    float below = max(_CC_FogP.x - P.y - 0.3 * m, 0.0);
                    float o2 = (outD + below * 1.5) / max(_CC_FogP.w, 1e-3);
                    if (skyPix) o2 = 6.0 * saturate(1.0 - dir.y * 1.6);                           // у горизонта небо тонет, вверху видно
                    od += o2 * 3.0 * _CC_Fog.y;
                }

                // 3. густой туман: видимость в метрах, внутри плывут клубы (4 пробы вдоль луча)
                if (_CC_Fog.z > 0.001)
                {
                    float acc = 0.0, ws = 0.0;
                    float d = 1.0 * m;
                    [unroll] for (int i = 0; i < 4; i++)
                    {
                        float w = d < dist ? 1.0 : 0.0;
                        float3 q = (cam + dir * min(d, dist)) / (3.0 * m) + float3(_CC_FogP2.z, 0.05, _CC_FogP2.w) * t;
                        acc += (Noise3(q) * 0.6 + Noise3(q * 2.1 + 3.3) * 0.4) * w;
                        ws += w;
                        d *= 2.5;
                    }
                    float cn = ws > 0.0 ? acc / ws : 0.5;
                    float vis = max(_CC_FogP2.x, 0.1);
                    float o3 = dist / vis * (0.55 + 0.9 * cn);
                    if (skyPix) o3 = 4.0 * saturate(1.2 - dir.y);
                    od += o3 * _CC_Fog.z;
                }

                float T = exp(-od);
                Light ml = GetMainLight();
                float fwd = pow(saturate(dot(dir, ml.direction)), 5.0);
                float3 fogCol = _CC_FogCol.rgb + ml.color * fwd * 0.18;                          // к солнцу туман светится
                return half4(lerp(fogCol, c.rgb, T), c.a);
            }

            // Облегчённый проход для ЖЁЛТОЙ зоны погоды: нормаль из производных (без 4 чтений глубины), тёмные пятна мокрого
            // и ровный снег как на крышах. Без луж-отражений, льда, лап и града. pos, dpx, dpy — считаются в Frag до ветвлений.
            half4 WeatherLite(Varyings input, half4 c, float3 pos, float3 dpx, float3 dpy)
            {
                float3 n = cross(dpy, dpx);
                float nl = length(n);
                n = nl > 1e-8 ? n / nl : float3(0, 1, 0);
                float3 toCam = GetCameraPositionWS() - pos;
                if (dot(n, toCam) < 0.0) n = -n;
                float up = smoothstep(0.55, 0.85, n.y);
                if (up < 0.001) return c;
                float dist = length(toCam);
                half3 col = c.rgb;
                float wet = _CC_Wet * up * (1.0 - smoothstep(_CC_WetP.w * 0.7, _CC_WetP.w, dist));
                if (_CC_WetMaskUse > 0.5) wet *= LOAD_TEXTURE2D_X(_CC_WetMask, uint2(input.positionCS.xy)).r;
                if (wet > 0.003)
                {
                    float luma = dot(col, float3(0.299, 0.587, 0.114));
                    half3 wc = (half3)(lerp(luma.xxx, (float3)col, 1.0 + 0.3 * wet) * lerp(1.0, 0.58, wet * _CC_WetP2.x));
                    col = lerp(col, wc, (half)saturate(wet * 1.5));
                }
                if (_CC_WinterOn > 0.5 && _CC_SnowCover > 0.001)
                {
                    float mtr = _CC_RainMeter > 0.0 ? _CC_RainMeter : 3.5;
                    half sm = CC_SnowMask((half)n.y, pos.xz / mtr);
                    Light ml = GetMainLight();
                    half3 amb = _CC_Amb.w > 0.5 ? (half3)_CC_Amb.rgb : max(SampleSH(half3(0, 1, 0)), 0.0h);
                    half3 lit = amb * 0.6h + ml.color * (half)saturate(ml.direction.y) * 0.6h;
                    col = CC_SnowApply(col, sm, lit);
                }
                return half4(col, c.a);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // ЗОНЫ ПОГОДЫ: красная — всё как было; жёлтая — слабее и дешевле (WeatherLite); вне зон осадков, мокрого, льда,
                // града и жары нет (остаётся туман). Зон нет (_CC_WxN = 0) — как раньше, ничего не считаем.
                float2 wx = float2(1.0, 1.0);
                float3 posZ = float3(0.0, 0.0, 0.0), dpx = float3(0.0, 0.0, 0.0), dpy = float3(0.0, 0.0, 0.0);
                bool skyZ = false;
                if (_CC_WxN > 0.5)
                {
                    float rawZ = SampleSceneDepth(input.texcoord);
                #if UNITY_REVERSED_Z
                    skyZ = rawZ < 1e-6;
                #else
                    skyZ = rawZ > 1.0 - 1e-6;
                #endif
                    posZ = ComputeWorldSpacePosition(input.texcoord, rawZ, UNITY_MATRIX_I_VP);
                    dpx = ddx(posZ); dpy = ddy(posZ);
                    wx = skyZ ? _CC_WxCam.xy : CC_WxMask2(posZ.xz);
                }

                half4 c;
                if (wx.x < 0.004)
                {
                    c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord, 0);
                }
                else if (_CC_WxLite > 0.5 && wx.y < 0.5)
                {
                    half4 src = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord, 0);
                    c = skyZ ? src : WeatherLite(input, src, posZ, dpx, dpy);
                    c = lerp(src, c, (half)wx.x);
                }
                else
                {
                    // мороз/снег: лужи замёрзли — экранные отражения луж не рисуем (они давали «двоение-троение» предметов сквозь лёд и снег)
                    bool frozen = _CC_WinterOn > 0.5 && (_CC_Frost > 0.05 || _CC_SnowCover > 0.05);
                    bool hot = _CC_HeatOn > 0.5 && !frozen;                                        // жара (CityHeat)
                    if (frozen) c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord, 0);
                    else if (hot && _CC_Heat.x + _CC_Heat.y > 0.001) c = HeatHaze(input);          // марево + миражи (сухо — луж нет)
                    else c = FragWet(input);
                    if (_CC_WinterOn > 0.5) c = Winter(input, c);
                    if (_CC_HailCover > 0.001 || _CC_HailWet > 0.001) c = HailGround(input, c);
                    if (hot) c = HeatGrade(input, c);
                    if (wx.x < 0.996)                                                              // край красной зоны: плавно к «ничего»
                    {
                        half4 src2 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord, 0);
                        c = lerp(src2, c, (half)wx.x);
                    }
                }
                if (_CC_FogOn > 0.5) c = Fog(input, c);
                return c;
            }
            ENDHLSL
        }
    }
    Fallback Off
}