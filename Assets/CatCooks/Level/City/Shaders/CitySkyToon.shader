// CatCooks — мультяшное небо города (замена Art Sky): те же свойства, но облака — кучевые «комки» (шары по клеткам)
// с ясными круглыми краями, тёмным плоским дном и затенением от солнца, медленно «кипят» и плывут по ветру.
// Дешевле прежнего: 2 замера крупной формы + 2 слоя комков (9 точек) против ~14 октав шума на пиксель.
// SkyController и Mood Director пишут в него те же параметры (_Coverage, _CloudSoft, _Wind, цвета…).
Shader "CatCooks/City/Sky Toon"
{
    Properties
    {
        [Header(City V2 Art Controls)]
        _ArtGradientExponent("Blue sky reach (lower = more blue)",Range(.15,2))=.55
        _ArtSkySaturation("Sky saturation",Range(0,1.6))=1.12
        _ArtHazeCap("Maximum sky horizon haze height",Range(0,.3))=.025
        _ArtHazeStrength("Sky haze strength",Range(0,1))=.35
        _ArtCloudSoftness("Cloud edge softness multiplier",Range(.3,1.5))=.75
        _ArtGlow("Sun glow / halo multiplier",Range(0,1))=.35
        [Header(Sky Gradient)]
        _ZenithColor  ("Zenith Color",  Color) = (0.30, 0.55, 0.90, 1)
        _HorizonColor ("Horizon Color", Color) = (0.75, 0.85, 0.95, 1)
        _GroundColor  ("Ground Color",  Color) = (0.55, 0.60, 0.65, 1)
        _SkyExponent  ("Sky Exponent",  Range(0.1, 4)) = 1.3

        [Header(Cartoon Banding)]
        _SkyBands  ("Gradient Bands (1 = off)", Range(1, 8)) = 1
        _BandBlend ("Band Softness", Range(0.01, 1)) = 0.6

        [Header(Horizon Haze)]
        _HazeColor  ("Haze Color", Color) = (0.86, 0.90, 0.94, 1)
        _HazeHeight ("Haze Height (0 = off)", Range(0, 0.6)) = 0.05
        _HazePower  ("Haze Falloff", Range(0.5, 6)) = 2.5

        [Header(Sun)]
        _SunDir       ("Sun Direction",  Vector) = (0.3, 0.6, 0.3, 0)
        _SunColor     ("Sun Color",      Color)  = (1.0, 0.95, 0.85, 1)
        _SunSize      ("Sun Size",       Range(0.9, 1.0)) = 0.995
        _SunSoftness  ("Sun Softness",   Range(0.0001, 0.02)) = 0.002
        _SunIntensity ("Sun Intensity",  Range(0, 5)) = 2.0
        _SunGlow      ("Sun Glow",       Range(0, 2)) = 0.5
        _SunGlowExp   ("Sun Glow Falloff", Range(1, 64)) = 16
        _SunHalo      ("Sun Halo Ring",  Range(0, 1)) = 0.2

        [Header(Clouds)]
        _CloudColor       ("Cloud Lit Color",    Color) = (1, 1, 1, 1)
        _CloudShadowColor ("Cloud Shadow Color", Color) = (0.70, 0.75, 0.85, 1)
        _Coverage         ("Coverage (density)", Range(0, 1)) = 0.5
        _CloudSoft        ("Cloud Edge Softness", Range(0.01, 0.5)) = 0.18
        _CloudScale       ("Cloud Scale", Range(0.2, 8)) = 2.0
        _CloudHorizonFade ("Horizon Fade", Range(0.01, 0.5)) = 0.12
        _CloudDarkness    ("Cloud Darkness (storm)", Range(0, 1)) = 0.0
        _Overcast         ("Sky Overcast", Range(0, 1)) = 0.0
        _Wind             ("Wind (xy)", Vector) = (0.01, 0.006, 0, 0)
        _CloudEvolve      ("Cloud Evolve (shape change speed)", Range(0, 0.3)) = 0.05
        _CloudHorizonBias ("Clouds Gather At Horizon", Range(0, 1)) = 0
        _CloudStretch     ("Cirrus Stretch (along wind)", Range(1, 5)) = 1

        [Header(Cloud Shape)]
        _CloudPuff   ("Puffiness (billow)", Range(0, 1)) = 0.85
        _CloudWarp   ("Domain Warp", Range(0, 1.5)) = 0.40
        _CloudDetail ("Detail Octaves", Range(3, 6)) = 4

        // Эрозия работает ТОЛЬКО у кромки: высокочастотный шум выгрызает
        // край, оставляя ядро плотным. Без неё сплошная облачность
        // выглядит залитым пятном без рельефа.
        _CloudErosion    ("Edge Erosion", Range(0, 1)) = 0.45
        _CloudErosionScale ("Erosion Scale", Range(1, 24)) = 9

        // Округлость: поджимает плотность к её середине, отчего доли
        // становятся куполами, а не рваными кляксами.
        _CloudRound ("Roundness", Range(0, 1)) = 0.45

        [Header(Toon Cumulus)]
        _PuffScale ("Комки: размер (больше — мельче)", Range(0.3, 4)) = 1.4
        _PuffBoil ("Комки: шевеление", Range(0, 1)) = 0.35
        _BaseShade ("Тёмное плоское дно", Range(0, 1)) = 0.45
        _FormScale ("Крупные формы: размер", Range(0.05, 1)) = 0.35
        _SkyHeight ("Облака выше и мельче", Range(0.5, 4)) = 1.8

        [Header(Cloud Light)]
        _CloudLightShift  ("Self Shadow Offset", Range(0, 0.4)) = 0.13
        _CloudShadowDepth ("Self Shadow Depth", Range(0, 1)) = 0.60
        _CloudRim         ("Sun Rim", Range(0, 2)) = 0.6

        [Header(Cartoon Clouds)]
        _CloudOutline      ("Outline Strength (0 = off)", Range(0, 1)) = 0
        _CloudOutlineWidth ("Outline Width", Range(0.005, 0.2)) = 0.04
        _CloudSteps        ("Shading Steps", Range(2, 6)) = 4
        _CloudToon         ("Toon Amount", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _ArtGradientExponent,_ArtSkySaturation,_ArtHazeCap,_ArtHazeStrength,_ArtCloudSoftness,_ArtGlow;
                float4 _ZenithColor, _HorizonColor, _GroundColor;
                float  _SkyExponent;
                float  _SkyBands, _BandBlend;
                float4 _HazeColor;
                float  _HazeHeight, _HazePower;
                float4 _SunDir, _SunColor;
                float  _SunSize, _SunSoftness, _SunIntensity;
                float  _SunGlow, _SunGlowExp, _SunHalo;
                float4 _CloudColor, _CloudShadowColor;
                float  _Coverage, _CloudSoft, _CloudScale;
                float  _CloudHorizonFade, _CloudDarkness, _Overcast;
                float4 _Wind;
                float  _CloudEvolve;
                float  _CloudHorizonBias;
                float  _CloudStretch;
                float  _CloudPuff, _CloudWarp, _CloudDetail;
                float  _CloudErosion, _CloudErosionScale, _CloudRound;
                float  _CloudLightShift, _CloudShadowDepth, _CloudRim;
                float  _CloudOutline, _CloudOutlineWidth, _CloudSteps, _CloudToon;
                float  _PuffScale, _PuffBoil, _BaseShade, _FormScale, _SkyHeight;
            CBUFFER_END
            // Сдвиг ветром и «кипение» копит мост «Окна ночью» (CityAtmosphere) по кадрам: если считать их как
            // ветер × время, любая смена силы/направления ветра (её делает погода) дёргает всё небо рывком.
            float2 _CC_SkyOffset;
            float  _CC_SkyEvolveT;
            float  _CC_SkyDriven;
            float  _CC_SkySize, _CC_SkyAmount, _CC_SkyForm;     // настройки неба с моста (их погода/стиль не перетирают)
            // v3 — ПОГОДА В ФОРМЕ ОБЛАКОВ (мост берёт плавную смесь погоды у пульта):
            float4 _CC_SkyW;        // x сила непогоды, y вытянутость (перистость), z «пряди», w ширина
            float  _CC_SkyWOn;      // 1 — данные есть
            float4 _CC_SkyLayers;   // x верхний ярус (перистые), y нижняя пелена, z рваные тучи, w — общая плотность комков
        ENDHLSL

        Pass
        {
            Name "CatCooksSkyToon"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            // ============================================================ шум
            float hash1(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }
            float2 hash2(float2 p)
            {
                float n = hash1(p);
                return float2(n, hash1(p + n * 17.31 + 3.7));
            }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash1(i), hash1(i + float2(1, 0)), u.x), lerp(hash1(i + float2(0, 1)), hash1(i + float2(1, 1)), u.x), u.y);
            }

            // КОМКИ. Каждая клетка — шар со случайным центром и радиусом; берём профиль купола sqrt(1-r²).
            // Максимум по соседям даёт кучевые «бугры» с ясными круглыми краями, а не жидкие разводы fbm.
            // t шевелит центры — облако медленно «кипит», а не просто едет.
            float Balls(float2 p, float t)
            {
                float2 i = floor(p), f = frac(p);
                float h = 0.0;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y);
                    float2 o = hash2(i + g);
                    o = 0.5 + 0.36 * sin(6.2831 * o + t * (0.5 + o.yx));
                    float2 d = g + o - f;
                    float r = 0.42 + 0.38 * hash1(i + g + 7.7);
                    float b = saturate(1.0 - dot(d, d) / (r * r));
                    h = max(h, sqrt(b));
                }
                return h;
            }

            // Плотность: крупные формы (где вообще облака) + комки по краю. cheap — для затенения: без мелких комков.
            float EvoT()
            {
                float base = _CC_SkyDriven > 0.5 ? _CC_SkyEvolveT : _Time.y * _CloudEvolve * 6.0;
                return base * (0.3 + _PuffBoil);
            }

            float Density(float2 uv, bool cheap)
            {
                float t = EvoT();
                float2 w = (float2(vnoise(uv * 0.11 + 11.3), vnoise(uv * 0.11 + 27.7)) - 0.5) * _CloudWarp * 2.0;
                float2 fu = (uv + w) * _FormScale * (_CC_SkyDriven > 0.5 ? _CC_SkyForm : 1.0);
                float form = vnoise(fu + t * 0.02) * 0.65 + vnoise(fu * 2.1 + 5.1 - t * 0.015) * 0.35;
                float big = Balls(uv * _PuffScale * 0.5, t);
                float n = form + (big - 0.55) * 0.42 * _CloudPuff;
                if (!cheap)
                {
                    float small = Balls(uv * _PuffScale * 1.35 + 3.3, t * 1.4);
                    n += (small - 0.55) * 0.2 * _CloudPuff * (0.5 + _CloudErosion);
                }
                return lerp(n, smoothstep(0.12, 0.88, n), _CloudRound);
            }

            float Fbm3(float2 p)
            {
                return vnoise(p) * 0.55 + vnoise(p * 2.03 + 7.1) * 0.3 + vnoise(p * 4.1 + 2.7) * 0.15;
            }

            // ВЕРХНИЙ ЯРУС — перистые: тонкие пряди вдоль ветра, «конские хвосты». Почти без объёма.
            float Cirrus(float2 uv, float2 wd, float t)
            {
                float2 wp = float2(-wd.y, wd.x);
                float2 p = float2(dot(uv, wd) * 0.18, dot(uv, wp) * 3.2);
                p += (float2(vnoise(p * 0.7 + 3.1), vnoise(p * 0.7 + 9.4)) - 0.5) * 1.4;     // пряди изгибаются
                float n = Fbm3(p + float2(t * 0.01, 0.0));
                float ridge = 1.0 - abs(vnoise(p * float2(1.0, 3.0) + 5.5) * 2.0 - 1.0);     // тонкие нити внутри
                float area = smoothstep(0.5, 0.8, Fbm3(uv * 0.06 + 13.7));                  // где вообще есть перистые
                return saturate(smoothstep(0.5, 0.78, n) * (0.45 + 0.55 * ridge) * area);
            }

            // НИЖНЯЯ ПЕЛЕНА — слоистые (пасмурно): широкие мягкие пласты, ближе к земле и крупнее.
            float Sheet(float2 uv, float t)
            {
                float2 w = (float2(vnoise(uv * 0.15 + 1.3), vnoise(uv * 0.15 + 8.8)) - 0.5) * 1.6;
                return Fbm3((uv + w) * 0.35 + t * 0.01);
            }

            float SoftBand(float t, float bands, float blend)
            {
                if (bands <= 1.001) return t;
                float scaled = t * bands;
                float idx = floor(scaled);
                float f = scaled - idx;
                float s = smoothstep(0.5 - blend * 0.5, 0.5 + blend * 0.5, f);
                return saturate((idx + s) / bands);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float3 sunDir = normalize(_SunDir.xyz);

                float t = saturate(dir.y);
                float g = pow(t, _ArtGradientExponent);
                g = SoftBand(g, _SkyBands, _BandBlend);
                float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, g);
                if (dir.y < 0.0) sky = lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-dir.y * 4.0));
                float skyLuma = dot(sky, float3(0.2126, 0.7152, 0.0722));
                sky = max(0, lerp(skyLuma.xxx, sky, _ArtSkySaturation));
                sky = lerp(sky, sky * float3(0.50, 0.53, 0.58), _Overcast);

                float sunVis = 1.0 - _Overcast * 0.85;
                float sd = dot(dir, sunDir);
                float disk = smoothstep(_SunSize, _SunSize + _SunSoftness, sd);
                sky += _SunColor.rgb * disk * _SunIntensity * sunVis;
                sky += _SunColor.rgb * pow(saturate(sd), _SunGlowExp) * _SunGlow * sunVis * _ArtGlow;
                float halo = smoothstep(_SunSize - 0.02, _SunSize - 0.004, sd) * (1.0 - disk);
                sky += _SunColor.rgb * halo * _SunHalo * sunVis * _ArtGlow;

                if (dir.y > 0.001)
                {
                    float drv = _CC_SkyDriven > 0.5 ? 1.0 : 0.0;
                    float wOn = _CC_SkyWOn > 0.5 ? 1.0 : 0.0;
                    // погода (плавная смесь с пульта) или запасные значения
                    float sev   = lerp(0.2, _CC_SkyW.x, wOn);
                    float str   = lerp(_CloudStretch, max(_CC_SkyW.y, 1.0), wOn);
                    float wisp  = lerp(0.2, _CC_SkyW.z, wOn);
                    float4 lay  = lerp(float4(1, 1, 1, 1), _CC_SkyLayers, drv * step(0.001, dot(_CC_SkyLayers, 1.0)));
                    float sunLow = (1.0 - saturate(sunDir.y * 3.0)) * step(-0.12, sunDir.y);    // закат/рассвет
                    float evo = EvoT();

                    float sizeK = lerp(1.0, _CC_SkySize, drv);
                    float2 offs = (_CC_SkyDriven > 0.5 ? _CC_SkyOffset : _Wind.xy * _Time.y * 2.0) * _SkyHeight;
                    float2 wd = normalize(_Wind.xy + float2(1e-5, 0.0));
                    float2 wp = float2(-wd.y, wd.x);
                    float hz = smoothstep(0.0, _CloudHorizonFade, dir.y);

                    // ── 1. ВЕРХНИЙ ЯРУС: перистые (ясно, перистая погода, перед грозой) ──
                    float cirAmt = saturate(0.05 + wisp * 1.1 + (str - 1.0) * 0.12) * (1.0 - _Overcast * 0.85) * lay.x;
                    if (cirAmt > 0.01)
                    {
                        float2 uvH = dir.xz / (dir.y + 0.25) * _CloudScale * 2.6 * sizeK + offs * 1.7;
                        float c = Cirrus(uvH, wd, evo) * cirAmt * hz;
                        float3 cc = lerp(_CloudColor.rgb, _HorizonColor.rgb, 0.25);
                        cc = lerp(cc, _SunColor.rgb * 1.1 + _CloudColor.rgb * 0.2, sunLow * 0.65);   // закат подсвечивает пряди
                        cc = lerp(cc, float3(0.5, 0.52, 0.58), _CloudDarkness * 0.7);
                        sky = lerp(sky, cc, saturate(c * 0.6));
                    }

                    // ── 2. СРЕДНИЙ ЯРУС: кучевые комки (сила непогоды — крупнее и выше, перистая — реже) ──
                    float sevK = lerp(1.18, 0.72, sev);                                  // гроза — крупные массивы
                    float2 uv0 = dir.xz / (dir.y + 0.12) * _CloudScale * _SkyHeight * sizeK * sevK;
                    float2 uv = uv0;
                    if (str > 1.001) uv = wd * (dot(uv, wd) / str) + wp * dot(uv, wp);
                    uv += offs;

                    float cov = _Coverage * lerp(1.0, _CC_SkyAmount, drv) * lay.w + _CloudHorizonBias * 0.4 * saturate(0.35 - dir.y);
                    cov *= 1.0 - 0.6 * saturate((wisp - 0.45) * 2.0);                     // перистая погода — кучевых мало
                    cov = saturate(max(cov, _Overcast * 0.8));
                    float n = Density(uv, false);
                    float edge = 1.0 - cov;
                    float soft = max(0.006, _CloudSoft * _ArtCloudSoftness * 0.6);
                    float body = smoothstep(edge, edge + soft, n) * hz;

                    if (body > 0.001)
                    {
                        // свет: шаг к солнцу (затенение изнутри) и шаг «вниз по небу» (к горизонту) — тёмное плоское дно
                        float2 sunOff = normalize(sunDir.xz + 1e-4) * _CloudLightShift;
                        float nSun = Density(uv + sunOff, true);
                        float2 down = normalize(uv0 + 1e-4) * _CloudLightShift * 0.8;
                        float nDown = Density(uv + down, true);
                        float occl = smoothstep(edge - 0.02, edge + 0.22, nSun);
                        float bottom = saturate((n - nDown) * 4.0) * (1.0 - smoothstep(edge, edge + 0.35, nDown));
                        float baseSh = _BaseShade * lerp(0.8, 1.5, sev);                  // грозовые — тяжёлое тёмное дно
                        float lit = 1.0 - occl * _CloudShadowDepth - bottom * baseSh;
                        lit = saturate(lit * 0.9 + (n - edge) * 0.5);
                        float steps = max(_CloudSteps, 2.0);
                        float banded = floor(lit * steps + 0.5) / steps;
                        lit = lerp(lit, saturate(banded), _CloudToon);

                        float3 stormGrey = float3(0.26, 0.28, 0.33);
                        float3 litC = lerp(_CloudColor.rgb, stormGrey * 1.4, _CloudDarkness);
                        float3 shdC = lerp(_CloudShadowColor.rgb, stormGrey * 0.6, _CloudDarkness);
                        float3 cloudCol = lerp(shdC, litC, saturate(lit));
                        // низкое солнце подсвечивает дно тёплым (закатные кучевые)
                        cloudCol += _SunColor.rgb * bottom * sunLow * 0.45 * sunVis;

                        float rim = smoothstep(edge, edge + soft * 1.2, n) * (1.0 - smoothstep(edge + soft * 1.2, edge + soft * 4.0, n));
                        cloudCol += _SunColor.rgb * rim * (1.0 - occl) * _CloudRim * sunVis * (0.5 + 0.5 * saturate(sd)) * (1.0 + sunLow);

                        if (_CloudOutline > 0.001)
                        {
                            float ob = smoothstep(edge, edge + _CloudOutlineWidth, n) * (1.0 - smoothstep(edge + _CloudOutlineWidth, edge + _CloudOutlineWidth * 2.2, n));
                            cloudCol = lerp(cloudCol, shdC * 0.72, ob * _CloudOutline);
                        }
                        cloudCol = lerp(cloudCol, _HazeColor.rgb, (1.0 - smoothstep(0.0, 0.25, dir.y)) * 0.35);
                        sky = lerp(sky, cloudCol, saturate(body));
                    }

                    // ── 3. НИЖНЯЯ ПЕЛЕНА: слоистые пласты (пасмурно, снег) — ближе, крупнее, ползут быстрее ──
                    float shAmt = saturate((_Overcast - 0.2) / 0.6) * lay.y;
                    float2 uvL = dir.xz / (dir.y + 0.05) * _CloudScale * 0.55 * sizeK + offs * 1.35;
                    if (shAmt > 0.01)
                    {
                        float ns = Sheet(uvL, evo);
                        float e = 1.0 - shAmt * 1.05;
                        float a = smoothstep(e, e + 0.22, ns) * smoothstep(0.0, _CloudHorizonFade * 0.6, dir.y);
                        float l = saturate(0.42 + (ns - e) * 0.6 + saturate(sd) * 0.25);
                        l = lerp(l, floor(l * 3.0 + 0.5) / 3.0, _CloudToon);
                        float3 sc = lerp(_CloudShadowColor.rgb, _CloudColor.rgb, l);
                        sc = lerp(sc, float3(0.3, 0.32, 0.37), _CloudDarkness * 0.85);
                        sc = lerp(sc, _HazeColor.rgb, (1.0 - smoothstep(0.0, 0.3, dir.y)) * 0.4);
                        sky = lerp(sky, sc, saturate(a * 0.95));
                    }

                    // ── 4. РВАНЫЕ ТУЧИ: тёмные клочья низко, несутся по ветру (гроза, шторм, ураган) ──
                    float scAmt = saturate((_CloudDarkness - 0.35) / 0.4) * lay.z;
                    if (scAmt > 0.01)
                    {
                        float2 q = uvL * 0.7 + offs * 1.2 + wd * evo * 0.15;
                        q = wd * dot(q, wd) * 0.5 + wp * dot(q, wp);                          // вытянуты по ветру
                        float ns = Fbm3(q);
                        float a = smoothstep(0.56, 0.66, ns) * scAmt * (1.0 - smoothstep(0.1, 0.5, dir.y)) * smoothstep(0.015, 0.07, dir.y);
                        float3 dc = lerp(float3(0.16, 0.17, 0.2), _CloudShadowColor.rgb * 0.45, 0.3);
                        sky = lerp(sky, dc, saturate(a));
                    }
                }

                if (_HazeHeight > 0.001)
                {
                    float haze = pow(saturate(1.0 - abs(dir.y) / max(0.0001, min(_HazeHeight, _ArtHazeCap))), _HazePower);
                    sky = lerp(sky, _HazeColor.rgb, saturate(haze) * _ArtHazeStrength);
                }
                return half4(sky, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
