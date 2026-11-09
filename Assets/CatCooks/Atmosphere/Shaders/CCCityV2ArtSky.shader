Shader "CatCooks/CityV2/Art Sky"
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
        Tags
        {
            "Queue"          = "Background"
            "RenderType"     = "Background"
            "PreviewType"    = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }
        Cull Off
        ZWrite Off

        Pass
        {
            Name "CatCooksStylizedSky"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

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
            CBUFFER_END

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
            float hash(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash(i);
                float b = hash(i + float2(1, 0));
                float c = hash(i + float2(0, 1));
                float d = hash(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // ВОТ ОТКУДА БЕРЁТСЯ ПУШИСТОСТЬ.
            // Обычный fbm складывает гладкие волны и всегда даёт мыло.
            // abs(2n-1) сгибает октаву пополам: округлые комки с резкими
            // ложбинами между ними. _CloudPuff = 0 возвращает прежний вид.
            float cloudFbm(float2 p, int octaves)
            {
                float v = 0.0, a = 0.5, norm = 0.0;
                // ЭВОЛЮЦИЯ ФОРМЫ: каждая октава плывёт со своей скоростью,
                // поэтому облако не просто едет, а перетекает — растёт,
                // тает, меняет силуэт. Раньше все октавы сдвигались вместе
                // (только _Wind), и небо выглядело как статичная картинка.
                float2 evo = float2(_Time.y, -_Time.y * 0.73) * _CloudEvolve;
                for (int k = 0; k < 6; k++)
                {
                    if (k >= octaves) break;
                    float n = valueNoise(p + evo * (0.35 + k * 0.55));
                    float billow = 1.0 - abs(n * 2.0 - 1.0);
                    v += a * lerp(n, billow, _CloudPuff);
                    norm += a;
                    p = p * 2.07 + 13.1;   // сдвиг убирает выравнивание октав
                    a *= 0.52;
                }
                return v / max(norm, 0.0001);
            }

            // ============================================================
            //  ЦЕНА. Скайбокс считается НА КАЖДЫЙ ПИКСЕЛЬ ЭКРАНА — это
            //  самый дорогой шейдер в системе просто по площади. Раньше
            //  плотность считалась дважды по пять октав с варпом и
            //  эрозией: около 14 октав на пиксель всего кадра.
            //
            //  Второй замер нужен только для самозатенения, а тени внутри
            //  облака мягкие — мелкие детали в них не читаются. Поэтому
            //  для него отдельная дешёвая версия: две октавы, без варпа
            //  и без эрозии.
            // ============================================================
            float cloudDensityCheap(float2 uv)
            {
                float2 evo = float2(_Time.y, -_Time.y * 0.73) * _CloudEvolve;
                float n = valueNoise(uv + evo * 0.35) * 0.5 + valueNoise(uv * 2.07 + 13.1 + evo * 0.9) * 0.26;
                n /= 0.76;
                return lerp(n, smoothstep(0.15, 0.85, n), _CloudRound);
            }

            // Плотность в точке. Полная версия — для самой точки.
            float cloudDensity(float2 uv, int octaves)
            {
                float2 ew = float2(-_Time.y * 0.41, _Time.y * 0.29) * _CloudEvolve;
                float2 w = float2(valueNoise(uv * 0.5 + 11.3 + ew),
                                  valueNoise(uv * 0.5 + 27.7 - ew)) - 0.5;
                float n = cloudFbm(uv + w * _CloudWarp, octaves);

                // ОКРУГЛОСТЬ. smoothstep поджимает значения к середине:
                // склоны становятся круче, вершины — площе, и доли читаются
                // как купола, а не как рваные кляксы.
                n = lerp(n, smoothstep(0.15, 0.85, n), _CloudRound);

                // ЭРОЗИЯ КРОМКИ. Мелкий шум выгрызает край и не трогает
                // ядро — именно этого не хватало сплошной облачности,
                // которая выглядела залитым пятном.
                // На нуле пропускаем замер целиком: это лишний sample
                // на каждом пикселе экрана.
                if (_CloudErosion > 0.01)
                {
                    float fine = valueNoise(uv * _CloudErosionScale + 31.7 + float2(_Time.y, _Time.y * 0.6) * _CloudEvolve * 2.0);
                    n -= (1.0 - fine) * _CloudErosion * 0.35;
                }

                return n;
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

            // ============================================================ frag
            half4 frag(Varyings i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float3 sunDir = normalize(_SunDir.xyz);

                // ------------------------------------------- градиент неба
                float t = saturate(dir.y);
                float g = pow(t, _ArtGradientExponent);
                g = SoftBand(g, _SkyBands, _BandBlend);

                float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, g);
                if (dir.y < 0.0)
                    sky = lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-dir.y * 4.0));

                float skyLuma=dot(sky,float3(.2126,.7152,.0722));
                sky=max(0,lerp(skyLuma.xxx,sky,_ArtSkySaturation));
                sky = lerp(sky, sky * float3(0.50, 0.53, 0.58), _Overcast);

                // ------------------------------------------- солнце
                float sunVis = 1.0 - _Overcast * 0.85;
                float sd = dot(dir, sunDir);

                float disk = smoothstep(_SunSize, _SunSize + _SunSoftness, sd);
                sky += _SunColor.rgb * disk * _SunIntensity * sunVis;
                sky += _SunColor.rgb * pow(saturate(sd), _SunGlowExp) * _SunGlow * sunVis * _ArtGlow;

                float halo = smoothstep(_SunSize - 0.02, _SunSize - 0.004, sd) * (1.0 - disk);
                sky += _SunColor.rgb * halo * _SunHalo * sunVis * _ArtGlow;

                // ------------------------------------------- облака
                if (dir.y > 0.001)
                {
                    int oct = (int)round(_CloudDetail);

                    // Проекция полусферы: у горизонта тайлы растягиваются,
                    // слой получает перспективу.
                    float2 uv = dir.xz / (dir.y + 0.15) * _CloudScale;
                    // ПЕРИСТОСТЬ: тянем облака вдоль ветра — вместо круглых клякс
                    // получаются мягкие вытянутые пряди (cirrus).
                    if (_CloudStretch > 1.001)
                    {
                        float2 wd = normalize(_Wind.xy + float2(1e-5, 0.0));
                        float2 wp = float2(-wd.y, wd.x);
                        uv = wd * (dot(uv, wd) / _CloudStretch) + wp * dot(uv, wp);
                    }
                    uv += _Wind.xy * _Time.y;

                    float n = cloudDensity(uv, oct);
                    // Облака собираются к горизонту, над головой — чистое небо (как в Shady Job).
                    float cov = saturate(_Coverage + _CloudHorizonBias * (0.35 - dir.y));
                    float edge = 1.0 - cov;

                    // Мягкий край — как в первой версии.
                    float body = smoothstep(edge, edge + max(.005,_CloudSoft*_ArtCloudSoftness), n);
                    body *= smoothstep(0.0, _CloudHorizonFade, dir.y);

                    if (body > 0.001)
                    {
                        // САМОЗАТЕНЕНИЕ. Шагаем в сторону солнца и смотрим,
                        // остались ли мы внутри облака. Остались — точку
                        // прикрывает то, что стоит между ней и солнцем.
                        float2 sunOff = normalize(sunDir.xz + 1e-4) * _CloudLightShift;
                        float nSun = cloudDensityCheap(uv + sunOff);

                        float occl = smoothstep(edge - 0.04, edge + 0.26, nSun);
                        float lit = 1.0 - occl * _CloudShadowDepth;

                        // Плотное ядро чуть светлее — там больше рассеяния.
                        lit = saturate(lit * 0.88 + (n - edge) * 0.55);

                        float steps = max(_CloudSteps, 2.0);
                        float banded = floor(lit * steps) / (steps - 1.0);
                        lit = lerp(lit, saturate(banded), _CloudToon);

                        float3 stormGrey = float3(0.26, 0.28, 0.33);
                        float3 litC = lerp(_CloudColor.rgb,       stormGrey * 1.4, _CloudDarkness);
                        float3 shdC = lerp(_CloudShadowColor.rgb, stormGrey * 0.6, _CloudDarkness);

                        float3 cloudCol = lerp(shdC, litC, saturate(lit));

                        // Светящийся край: тонкая полоса у кромки, где облако
                        // просвечивает насквозь. Только с солнечной стороны.
                        float rimBand = smoothstep(edge, edge + _CloudSoft * 0.7, n)
                                      * (1.0 - smoothstep(edge + _CloudSoft * 0.7,
                                                          edge + _CloudSoft * 2.4, n));
                        cloudCol += _SunColor.rgb * rimBand * (1.0 - occl)
                                  * _CloudRim * sunVis;

                        // Контур по умолчанию выключен — это он делал из
                        // облаков лужи.
                        if (_CloudOutline > 0.001)
                        {
                            float ob = smoothstep(edge, edge + _CloudOutlineWidth, n)
                                     * (1.0 - smoothstep(edge + _CloudOutlineWidth,
                                                         edge + _CloudOutlineWidth * 2.2, n));
                            cloudCol = lerp(cloudCol, shdC * 0.72, ob * _CloudOutline);
                        }

                        sky = lerp(sky, cloudCol, saturate(body));
                    }
                }

                // ------------------------------------------- дымка горизонта
                if (_HazeHeight > 0.001)
                {
                    float haze = pow(saturate(1.0 - abs(dir.y) / max(.0001,min(_HazeHeight,_ArtHazeCap))), _HazePower);
                    sky = lerp(sky, _HazeColor.rgb, saturate(haze)*_ArtHazeStrength);
                }

                return half4(sky, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
