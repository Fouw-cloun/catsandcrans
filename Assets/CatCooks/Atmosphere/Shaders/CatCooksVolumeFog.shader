// =============================================================
//  CatCooksVolumeFog.shader
//  Объёмный туман и песчаная пыль. Реймарчинг по боксу. URP 17 / Unity 6.
//
//  ═══ ПОЧЕМУ НЕ ЧАСТИЦЫ ═══
//
//  У билборда всегда есть силуэт. Его можно рвать шумом, мять варпом и резать
//  порогом, но он остаётся конечной плоской фигурой, и на достаточно чистом
//  фоне глаз её найдёт. У тумана силуэта нет вообще: есть плотность в объёме,
//  которая копится вдоль луча. Это принципиально другой класс задачи, и
//  решается он трассировкой, а не спрайтами.
//
//  ═══ КАК РАБОТАЕТ ═══
//
//  Рисуется бокс, у него включено Cull Front — то есть рендерятся ЗАДНИЕ
//  грани. Это нужно, чтобы при входе камеры внутрь объёма передние грани не
//  срезались ближней плоскостью и туман не исчезал целиком.
//
//  Для каждого пикселя считается пересечение луча камеры с боксом (slab-тест),
//  и по отрезку внутри бокса делается шестнадцать шагов. На каждом шаге
//  плотность превращается в поглощение по закону Бугера — Ламберта, цвет
//  копится с учётом оставшегося светопропускания.
//
//  ═══ ЧЕГО НЕТ В ТИПОВЫХ РЕАЛИЗАЦИЯХ И ЧТО ЗДЕСЬ ДОБАВЛЕНО ═══
//
//  1. ОБРЕЗКА ПО ГЛУБИНЕ СЦЕНЫ. Без неё туман рисуется поверх всего, что
//     стоит внутри объёма: экскаваторов, башни, построек. Луч обрезается по
//     буферу глубины, и туман честно оказывается ЗА ними.
//
//  2. ДИЗЕРИНГ СТАРТА ЛУЧА. Шестнадцать шагов без него дают видимые
//     концентрические пластины — самый узнаваемый артефакт реймарчинга.
//     Сдвиг старта по interleaved gradient noise размазывает их в шум,
//     который глаз не читает. Стоит одну инструкцию.
//
//  3. ШАГ В МЕТРАХ, А НЕ В ОБЪЕКТНЫХ ЕДИНИЦАХ. Бокс масштабируется
//     неравномерно (например 60 × 12 × 60). Если копить плотность в объектных
//     единицах, она поедет при любой смене размера зоны. Длина шага
//     переводится в мировые метры один раз на пиксель.
//
//  4. ДЕТАЛИЗАЦИЯ ПРОЦЕДУРНЫМ ШУМОМ. Симуляция 256×256 на шестьдесят метров
//     даёт ячейку в четверть метра — вблизи это мыло. Поверх текстуры
//     симуляции идёт мелкий трёхмерный шум, который даёт структуру на любом
//     расстоянии и ничего не стоит по памяти.
//
//  5. РАССЕЯНИЕ НА СОЛНЦЕ. Пыль против солнца светится — это то, из-за чего
//     песчаная буря читается как буря, а не как коричневый фильтр.
//
//  ═══ ТРЕБОВАНИЯ ═══
//
//  В URP-ассете должна быть включена Depth Texture. Без неё обрезка по
//  глубине не работает и туман закроет всю геометрию.
// =============================================================
Shader "CatCooks/VolumeFog"
{
    Properties
    {
        [Header(Plotnost)]
        _DensityTex ("Поле плотности (симуляция)", 2D) = "white" {}

        // Экстинкция на МЕТР, а не абстрактная плотность. Задаётся из C# как
        // 3 / дальность_видимости: на этой дистанции остаётся 5% света.
        // Число не зависит ни от размера зоны, ни от числа шагов.
        _Extinction ("Экстинкция на метр", Range(0, 0.5)) = 0.025

        _SimAmount ("Вклад симуляции", Range(0, 1)) = 1

        [Space(6)]
        _Contrast   ("Контраст плотности", Range(0.3, 4)) = 1
        _ClearHoles ("Чистые промежутки", Range(0, 0.9)) = 0.1

        [Header(Cvet)]
        _ColorTop    ("Цвет вверху", Color) = (0.86, 0.90, 0.98, 1)
        _ColorBottom ("Цвет внизу", Color) = (0.45, 0.50, 0.62, 1)
        _Emission    ("Яркость", Range(0, 4)) = 1

        [Header(Vysota)]
        _HeightFalloff ("Спад по высоте", Range(0.1, 8)) = 2
        _HeightCenter  ("Середина спада", Range(-0.5, 0.5)) = -0.1

        [Header(Detalizaciya)]
        _DetailAmount ("Сила мелкого шума", Range(0, 1)) = 0.5
        _DetailScale  ("Крупность мелкого шума", Range(0.05, 3)) = 0.35
        _DetailSpeed  ("Скорость мелкого шума", Range(0, 2)) = 0.15

        [Space(6)]
        _MacroAmount ("Сила КРУПНОЙ неоднородности", Range(0, 1)) = 0.5
        _MacroScale  ("Крупность, метры", Range(3, 120)) = 28
        _Anisotropy   ("Растяжение по ветру", Range(1, 8)) = 1
        _WindDir      ("Направление ветра (xz)", Vector) = (1, 0, 0, 0)

        [Header(Solnce)]
        _SunScatter   ("Рассеяние вперёд", Range(0, 3)) = 0.4
        _SunSharpness ("Резкость короны", Range(1, 32)) = 8
        _SunTint      ("Цвет рассеяния", Color) = (1, 0.93, 0.78, 1)

        [Header(Kachestvo)]
        [IntRange] _Steps ("Шагов трассировки", Range(4, 48)) = 16
        _Dither ("Дизеринг шага", Range(0, 1)) = 1
        _EdgeFade ("Растворение у граней бокса", Range(0, 0.5)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector"= "True"
        }

        Pass
        {
            Name "CatCooksVolumeFog"
            Tags { "LightMode" = "UniversalForward" }

            // Премультиплицированная альфа: цвет уже накоплен с учётом
            // светопропускания, домножать его на альфу второй раз нельзя.
            Blend One OneMinusSrcAlpha
            ZWrite Off

            // Рендерим ЗАДНИЕ грани. Иначе при входе камеры внутрь объёма
            // передние грани срежет ближней плоскостью и туман пропадёт.
            Cull Front

            // Всегда: задняя грань бокса может оказаться за геометрией, и
            // обычный тест глубины выбросил бы пиксель целиком вместе с
            // туманом ПЕРЕД этой геометрией. Правильная окклюзия делается
            // обрезкой луча по глубине внутри шейдера, а не тестом.
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_DensityTex);
            SAMPLER(sampler_DensityTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorTop, _ColorBottom, _SunTint, _WindDir;
                float  _Extinction, _SimAmount, _Emission;
                float  _Contrast, _ClearHoles;
                float  _MacroAmount, _MacroScale;
                float  _HeightFalloff, _HeightCenter;
                float  _DetailAmount, _DetailScale, _DetailSpeed, _Anisotropy;
                float  _SunScatter, _SunSharpness;
                float  _Steps, _Dither, _EdgeFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 cameraOS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 screenPos  : TEXCOORD3;
            };

            float Hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // Трёхмерный шум значений. Он дороже двумерного, но объёму нужен
            // именно трёхмерный: двумерный, натянутый по высоте, читается как
            // вертикальные полосы.
            float Noise3(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash3(i + float3(0, 0, 0));
                float n100 = Hash3(i + float3(1, 0, 0));
                float n010 = Hash3(i + float3(0, 1, 0));
                float n110 = Hash3(i + float3(1, 1, 0));
                float n001 = Hash3(i + float3(0, 0, 1));
                float n101 = Hash3(i + float3(1, 0, 1));
                float n011 = Hash3(i + float3(0, 1, 1));
                float n111 = Hash3(i + float3(1, 1, 1));

                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            // Пересечение луча с единичным кубом [-0.5, 0.5] в объектном
            // пространстве. Возвращает вход и выход по параметру t.
            float2 BoxIntersect(float3 ro, float3 rd)
            {
                float3 inv = 1.0 / (rd + 1e-7);
                float3 t0 = (-0.5 - ro) * inv;
                float3 t1 = ( 0.5 - ro) * inv;

                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);

                return float2(max(max(tmin.x, tmin.y), tmin.z),
                              min(min(tmax.x, tmax.y), tmax.z));
            }

            Varyings vert(Attributes v)
            {
                Varyings o;

                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);

                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.screenPos  = ComputeScreenPos(pos.positionCS);
                o.positionOS = v.positionOS.xyz;
                o.cameraOS   = TransformWorldToObject(GetCameraPositionWS());

                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 ro = i.cameraOS;
                float3 rd = normalize(i.positionOS - ro);

                float2 t = BoxIntersect(ro, rd);

                // Камера может быть ВНУТРИ объёма — тогда старт это сама
                // камера, а не точка входа в бокс.
                t.x = max(t.x, 0.0);

                if (t.y <= t.x) return half4(0, 0, 0, 0);

                // ── ПЕРЕВОД В МЕТРЫ ──
                // Бокс масштабируется неравномерно, поэтому единица параметра
                // t в объектном пространстве это разное количество метров в
                // разных направлениях. Считаем множитель один раз.
                float objToWorld = length(mul((float3x3)UNITY_MATRIX_M, rd));

                // ── ОБРЕЗКА ПО ГЛУБИНЕ СЦЕНЫ ──
                // Без неё туман рисуется поверх всего, что стоит внутри
                // объёма. Переводим глубину сцены в расстояние ВДОЛЬ ЛУЧА:
                // буфер хранит расстояние по оси взгляда, а луч идёт под
                // углом к ней.
                float2 sUV = i.screenPos.xy / max(1e-5, i.screenPos.w);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(sUV), _ZBufferParams);

                float3 rdW = normalize(i.positionWS - GetCameraPositionWS());
                float cosA = max(dot(rdW, GetViewForwardDir()), 1e-4);
                float sceneAlongRay = sceneEye / cosA;

                float tFarWorld = min(t.y * objToWorld, sceneAlongRay);
                float tNearWorld = t.x * objToWorld;

                if (tFarWorld <= tNearWorld) return half4(0, 0, 0, 0);

                int steps = (int)max(4.0, _Steps);
                float stepWorld = (tFarWorld - tNearWorld) / steps;
                float stepObj = stepWorld / max(objToWorld, 1e-5);

                // ── ДИЗЕРИНГ ──
                // Шестнадцать шагов без сдвига дают видимые пластины. Сдвиг
                // старта на долю шага размазывает их в шум.
                float jitter = InterleavedGradientNoise(i.positionCS.xy, 0) * _Dither;

                float3 p = ro + rd * (t.x + stepObj * (0.5 + jitter));
                float3 dp = rd * stepObj;

                float2 windXZ = normalize(_WindDir.xz + float2(1e-4, 0));
                float scroll = _Time.y * _DetailSpeed;

                float3 acc = 0.0;
                float transmittance = 1.0;

                // Рассеяние вперёд: смотрим ли мы в сторону солнца.
                Light ml = GetMainLight();
                float forward = saturate(dot(rdW, -ml.direction));
                float corona = pow(forward, _SunSharpness);

                [loop]
                for (int s = 0; s < steps; s++)
                {
                    // Крупная форма из симуляции. Координаты бокса [-0.5,0.5]
                    // переводим в UV текстуры.
                    float sim = SAMPLE_TEXTURE2D_LOD(_DensityTex, sampler_DensityTex,
                                                     p.xz + 0.5, 0).r;
                    sim = lerp(1.0, sim, _SimAmount);

                    // Мелкая структура. Растягиваем координаты ПОПЕРЁК ветра,
                    // чтобы клочья вытягивались вдоль потока: это главное, чем
                    // несомая пыль отличается от стоячего тумана.
                    float3 wp = p;
                    float along = dot(wp.xz, windXZ);
                    float across = dot(wp.xz, float2(-windXZ.y, windXZ.x));
                    wp.xz = float2(along / max(1.0, _Anisotropy), across);

                    float detail = Noise3(wp / max(0.02, _DetailScale)
                                          + float3(scroll, scroll * 0.3, -scroll));

                    // Мелкий шум с СОХРАНЕНИЕМ СРЕДНЕГО: обычное умножение на
                    // шум со средним 0.5 тихо съело бы половину плотности.
                    float d = sim * saturate(1.0 + (detail - 0.5) * 2.0 * _DetailAmount);

                    // КРУПНАЯ НЕОДНОРОДНОСТЬ. Симуляция даёт форму в масштабе
                    // метров, мелкий шум — сантиметров, а между ними была
                    // дыра: на десятках метров среда оставалась равномерной и
                    // читалась как ровная заливка.
                    //
                    // Считается в МИРОВЫХ координатах, а не в объектных:
                    // иначе рисунок ездит вместе с зоной за камерой.
                    float macro = Noise3(i.positionWS / max(1.0, _MacroScale)
                                         + float3(scroll * 0.15, 0, -scroll * 0.15));
                    d *= saturate(1.0 + (macro - 0.5) * 2.0 * _MacroAmount);

                    // Контраст вокруг единицы: среднее не уезжает, меняется
                    // только разброс между плотным и редким.
                    d = pow(max(d, 0.0), _Contrast);

                    // ЧИСТЫЕ ПРОМЕЖУТКИ. Без них у среды нет по-настоящему
                    // пустых мест, и она всегда немного мутная везде. Срез
                    // снизу даёт честные разрывы, сквозь которые видно.
                    d = max(0.0, d - _ClearHoles) / max(0.05, 1.0 - _ClearHoles);

                    // Спад по высоте. Туман лежит внизу, у пыли спад мягче и
                    // она поднимается выше.
                    d *= saturate(exp(-(p.y - _HeightCenter) * _HeightFalloff * 2.0));

                    // Растворение у граней бокса, иначе объём обрывается
                    // видимой прямой линией.
                    float3 edge = saturate((0.5 - abs(p)) / max(0.001, _EdgeFade));
                    d *= edge.x * edge.y * edge.z;

                    // Экстинкция на метр. Дальше она умножается на длину шага
                    // В МЕТРАХ, поэтому результат не зависит ни от размера
                    // зоны, ни от числа шагов.
                    d *= _Extinction;

                    if (d > 0.0005)
                    {
                        // Поглощение на шаге по Бугеру — Ламберту.
                        float a = 1.0 - exp(-d * stepWorld);

                        // Цвет по высоте: низ плотнее и темнее, верх светлее.
                        float3 col = lerp(_ColorBottom.rgb, _ColorTop.rgb,
                                          saturate(p.y - _HeightCenter + 0.5));

                        // Корона вокруг солнца. Тем ярче, чем плотнее среда.
                        col += _SunTint.rgb * ml.color * (corona * _SunScatter);

                        acc += col * (a * transmittance);
                        transmittance *= 1.0 - a;
                    }

                    p += dp;

                    // Дальше копить нечего: среда уже непрозрачна.
                    if (transmittance < 0.004) break;
                }

                return half4(acc * _Emission, 1.0 - transmittance);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
