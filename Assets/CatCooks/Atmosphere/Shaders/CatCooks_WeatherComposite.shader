// =============================================================
//  CatCooks_WeatherComposite.shader
//  ОДИН полноэкранный проход на всю погоду:
//  тени облаков + туман + рамка + вспышка.
//
//  ЗАЧЕМ ОБЪЕДИНЯТЬ
//   Раньше туман и тени были отдельными проходами. Каждый заново читал
//   буфер глубины и восстанавливал мировую позицию — самая дорогая часть
//   работы, и она делалась дважды. Добавить третий проход под эффекты
//   камеры значило бы делать её трижды.
//
//   Здесь глубина читается ОДИН раз, мировая позиция считается ОДИН раз,
//   и её используют и тени, и туман. Экранные эффекты идут следом,
//   в том же проходе, по уже готовому цвету.
//
//   Экономия не в математике эффектов, а в том, что пропали два лишних
//   прохода по всему экрану вместе с чтением depth и обратной матрицей.
//
//  ПОРЯДОК ВАЖЕН и повторяет физику:
//   1. Тени облаков   — гасят освещение поверхности
//   2. Туман          — стоит между поверхностью и глазом
//   3. Рамка          — виньетирование оптики
//   4. Вспышка        — засветка сенсора
//  Поменяй местами — и туман начнёт светиться сквозь тени.
//
//  Любой блок выключается своим параметром в ноль и стоит один if.
//
//  УСТАНОВКА: Full Screen Pass Renderer Feature, Injection Point =
//  Before Rendering Transparents, Requirements = Color + Depth.
//
//  URP 17 / Unity 6.
// =============================================================
Shader "Hidden/CatCooks/Weather Composite"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "CatCooksWeatherComposite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // Полноэкранный проход своими руками: путь к Blit.hlsl менялся
            // между версиями пакета и ломается при обновлении URP.
            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);
            float4 _BlitScaleBias;

            // ---- тени облаков (пишет CatCooksCloudShadows) ----
            float4 _CatCS_Params;      // x метры, y покрытие, z резкость, w густота
            float4 _CatCS_Offset;
            float4 _CatCS_Color;
            float  _CatCS_Penumbra;
            float  _CatCS_HeightFade;
            float  _CatCS_MaxDistance;
            float  _CatCS_Contrast;
            float4 _CatCS_Zone;        // xy центр (мир XZ), z радиус (0 — везде), w мягкость края

            // ---- туман (пишет CatCooksFogController) ----
            float4 _CatFogColorNear, _CatFogColorFar, _CatFogSunColor;
            float  _CatFogStart, _CatFogEnd, _CatFogDensity, _CatFogCurve;
            float  _CatFogHeightBottom, _CatFogHeightTop, _CatFogHeightAmount;
            float  _CatFogSunPower, _CatFogSunAmount;
            float  _CatFogBands, _CatFogToon, _CatFogSkyBand;

            // ---- эффекты камеры (пишет CatCooksWeatherFX) ----
            float4 _CatFX_Vignette;    // x сила, y степень, z мягкость, w desat
            float4 _CatFX_VignetteColor;
            float4 _CatFX_Flash;       // x яркость вспышки
            float4 _CatFX_FlashColor;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 texcoord   : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                float2 uv = GetFullScreenTriangleTexCoord(input.vertexID);
                float2 scale = (abs(_BlitScaleBias.x) + abs(_BlitScaleBias.y) < 1e-5)
                               ? float2(1, 1) : _BlitScaleBias.xy;
                o.texcoord = uv * scale + _BlitScaleBias.zw;
                return o;
            }

            // ================================================================ шум
            float hash21(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y * 43758.5453);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Три октавы обычного шума. БЕЗ billow — и вот почему.
            //
            // billow это 1-|2n-1|, и у него среднее НЕ 0.5, а около 0.72:
            // модуль заворачивает обе половины распределения вверх.
            // Я же сравнивал результат с порогом, посчитанным от 0.5.
            // Отсюда и метания: при нулевом контрасте шум схлопывался
            // ниже порога и вся земля уходила в тень, а с контрастом
            // улетал выше него — и теней не было вовсе.
            //
            // У обычного fbm среднее честно 0.5, и порог работает
            // предсказуемо. Округлость пятен добираем контрастом.
            float patchFbm(float2 p)
            {
                float v = 0, a = 0.5, norm = 0;
                [unroll]
                for (int k = 0; k < 3; k++)
                {
                    v += a * vnoise(p);
                    norm += a;
                    p = p * 2.03 + 7.1;
                    a *= 0.5;
                }
                return v / norm;
            }

            // ================================================================ frag
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv);
                float3 col = src.rgb;

                // ---------- ОБЩАЯ ЧАСТЬ: глубина и мировая позиция ----------
                // Читается ОДИН раз на пиксель и используется и тенями,
                // и туманом. Раньше это делалось в двух проходах подряд.
                float rawDepth = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                bool isSky = rawDepth < 1e-6;
            #else
                bool isSky = rawDepth > 1.0 - 1e-6;
            #endif

                float3 posWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 toPix = posWS - _WorldSpaceCameraPos;
                float3 viewDir = normalize(toPix);
                float dist = length(toPix);

                // ---------- 1. ТЕНИ ОБЛАКОВ ----------
                if (_CatCS_Params.w > 0.001 && !isSky)
                {
                    // Те же грабли: невыставленные глобали равны нулю.
                    // Нулевой размер пятна дал бы деление на единицу и
                    // маску размером в метр, нулевая дальность — тени
                    // ровно в точке камеры.
                    float csPatch = _CatCS_Params.x > 1.0 ? _CatCS_Params.x : 480.0;
                    float csMaxDist = _CatCS_MaxDistance > 1.0
                                      ? _CatCS_MaxDistance : 2000.0;
                    float csPenum = _CatCS_Penumbra > 0.001 ? _CatCS_Penumbra : 0.35;
                    // Маска по МИРОВЫМ XZ — тень приклеена к земле
                    // и не плывёт при движении камеры.
                    float2 p = posWS.xz / csPatch;
                    // СКЛАДЫВАЕМ, а не перемножаем. Произведение двух
                    // шумов сжимает разброс к середине и заодно сдвигает
                    // среднее — предсказать, где окажется порог,
                    // становится невозможно. Взвешенная сумма сохраняет
                    // и среднее 0.5, и разброс.
                    float a = patchFbm(p + _CatCS_Offset.xy);
                    float b = patchFbm(p * 0.63 + _CatCS_Offset.zw);
                    float n = a * 0.6 + b * 0.4;

                    // РАСТЯЖКА ДИАПАЗОНА. Произведение двух нормированных
                    // billow-шумов живёт около середины, и порог резал его
                    // почти поровну — вместо отдельных пятен получалась
                    // ровная полутень по всей земле.
                    //
                    // ЗАЩИТА ОТ НУЛЯ. Глобальные шейдерные параметры, если
                    // их никто не выставил, равны НУЛЮ. При нулевом
                    // контрасте формула схлопывает шум ровно в 0.5, порог
                    // оказывается выше — и вся земля уходит в сплошную
                    // тень, неподвижную и одинаковую. Ровно то, что было
                    // видно. Поэтому ноль трактуем как единицу.
                    float csContrast = _CatCS_Contrast > 0.01 ? _CatCS_Contrast : 1.8;
                    n = saturate((n - 0.5) * csContrast + 0.5);

                    // Порог симметричен относительно 0.5: на покрытии 0.5
                    // ровно половина земли в тени. Раньше диапазон был
                    // смещён вверх, и даже при среднем покрытии тень
                    // накрывала почти всё.
                    float edge = lerp(0.85, 0.15, _CatCS_Params.y);
                    float core = smoothstep(edge, edge + _CatCS_Params.z, n);
                    float penum = smoothstep(edge - csPenum, edge + csPenum, n);
                    float shade = lerp(penum, core, 0.7);

                    float far = 1.0 - smoothstep(csMaxDist * 0.6, csMaxDist, dist);
                    float high = 1.0 - saturate((posWS.y - _CatCS_HeightFade) * 0.02);
                    // только над игровой зоной: облака видны над головой, а не на всём дальнем городе
                    float zone = _CatCS_Zone.z > 0.5
                        ? 1.0 - smoothstep(_CatCS_Zone.z, _CatCS_Zone.z + max(_CatCS_Zone.w, 1.0), length(posWS.xz - _CatCS_Zone.xy))
                        : 1.0;
                    float amount = (1.0 - shade) * _CatCS_Params.w * far * high * zone;

                    col *= lerp(1.0.xxx, _CatCS_Color.rgb, amount);
                }

                // ---------- 2. ТУМАН ----------
                if (_CatFogDensity > 0.001)
                {
                    float fog;
                    if (isSky)
                    {
                        // У неба нет глубины: заливать его по дистанции
                        // нельзя, иначе пропадёт весь градиент. Только
                        // узкая полоса у горизонта для стыковки с землёй.
                        // Sky Band = 0 — небо не трогаем вовсе. Дымку у горизонта
                        // рисует само небо (Haze). Иначе любой объект без глубины
                        // (шейдер без DepthOnly/DepthNormals) получает поверх себя
                        // полосу горизонта — «полоса сквозь всё».
                        if (_CatFogSkyBand <= 0.0005) fog = 0.0;
                        else
                        {
                            float band = _CatFogSkyBand;
                            fog = pow(saturate(1.0 - abs(viewDir.y) / band), 2.0);
                        }
                    }
                    else
                    {
                        float span = max(_CatFogEnd - _CatFogStart, 0.001);
                        float distFactor = saturate((dist - _CatFogStart) / span);
                        distFactor = pow(distFactor, max(_CatFogCurve, 0.01));

                        float hSpan = max(_CatFogHeightTop - _CatFogHeightBottom, 0.001);
                        float hf = saturate((_CatFogHeightTop - posWS.y) / hSpan);
                        hf = hf * hf;

                        fog = distFactor * lerp(1.0, hf, saturate(_CatFogHeightAmount));
                    }

                    float bands = max(_CatFogBands, 2.0);
                    float banded = floor(fog * bands) / (bands - 1.0);
                    fog = lerp(fog, saturate(banded), saturate(_CatFogToon));
                    fog = saturate(fog * _CatFogDensity);

                    if (fog > 0.001)
                    {
                        float3 fc = lerp(_CatFogColorNear.rgb, _CatFogColorFar.rgb,
                                         saturate(fog));
                        Light main = GetMainLight();
                        float sun = saturate(dot(viewDir, normalize(_MainLightPosition.xyz)));
                        float scatter = pow(sun, max(_CatFogSunPower, 0.01))
                                      * saturate(_CatFogSunAmount);
                        fc = lerp(fc, _CatFogSunColor.rgb * main.color, scatter);
                        col = lerp(col, fc, fog);
                    }
                }

                // ---------- 3. РАМКА ----------
                if (_CatFX_Vignette.x > 0.001)
                {
                    // РАМКА ПО КРАЯМ, А НЕ ОВАЛ ИЗ ЦЕНТРА.
                    //
                    // Раньше считалось расстояние от центра — то есть
                    // затемнение эллипсом, и оно неизбежно лезло в середину
                    // кадра. Теперь берётся расстояние до БЛИЖАЙШЕЙ КРОМКИ
                    // экрана: полоса ровной ширины идёт по периметру,
                    // центр не трогается вообще, а по углам темнее само
                    // собой — там близко сразу две кромки.
                    float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                    float dx = min(uv.x, 1.0 - uv.x) * aspect;
                    float dy = min(uv.y, 1.0 - uv.y);
                    float edgeDist = min(dx, dy);

                    // z — ШИРИНА полосы в долях высоты экрана.
                    float band = max(_CatFX_Vignette.z, 0.01);
                    float v = 1.0 - smoothstep(0.0, band, edgeDist);
                    v = pow(saturate(v), _CatFX_Vignette.y);
                    v *= _CatFX_Vignette.x;

                    col = lerp(col, _CatFX_VignetteColor.rgb * col, v);

                    // По краям гасим цвет: в непогоду периферия зрения
                    // обесцвечивается первой.
                    float lum = dot(col, float3(0.299, 0.587, 0.114));
                    col = lerp(col, lum.xxx, v * _CatFX_Vignette.w);
                }

                // ---------- 4. ВСПЫШКА ----------
                if (_CatFX_Flash.x > 0.001)
                {
                    // Засветка сенсора: аддитивно и по всему кадру.
                    col += _CatFX_FlashColor.rgb * _CatFX_Flash.x;
                }

                return half4(col, src.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
