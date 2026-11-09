// =============================================================
//  CatCooks_Fire.shader
//  Костёр под котлом. URP 17 / Unity 6.
//
//  ═══ ПОЧЕМУ ЭТО МЕШ, А НЕ ПАРТИКЛЫ ═══
//
//  Приличный костёр на билбордах — это 200–400 частиц. Каждая: CPU-симуляция,
//  перестройка вершинного буфера каждый кадр, сортировка по глубине и, главное,
//  ОВЕРДРО. Частицы стоят стопкой, и один пиксель экрана прогоняется через
//  фрагментный шейдер по двадцать-тридцать раз. Пять костров на площадке — и
//  прозрачка съедает кадр целиком.
//
//  Здесь вместо этого ОДИН низкополигональный меш-оболочка: цилиндр без
//  крышек, гранёный, вершин полтораста. Овердро ровно два слоя (передняя и
//  задняя стенка), CPU не делает ничего, вершинный буфер статичен, сортировать
//  нечего. Форму пламени даёт не геометрия, а эрозия шумом в шейдере.
//
//  Итог: один DrawCall на костёр, ноль CPU, овердро меньше в десять раз.
//  Материал у всех костров ОБЩИЙ — параметры гонятся через
//  MaterialPropertyBlock (см. FireVisualController), поэтому GPU Instancing
//  собирает их в один вызов.
//
//  ═══ ОБТЕКАНИЕ КОТЛА ═══
//
//  Огонь не должен проходить сквозь котёл — он должен облизывать его снизу и
//  выплёскиваться по бокам. Делается это В ВЕРШИННОМ ШЕЙДЕРЕ, то есть почти
//  бесплатно: выше уровня дна котла и внутри его радиуса вершины расталкиваются
//  наружу по радиусу. Оболочка расходится воронкой вокруг посуды.
//
//  Фрагмент дополнительно гасит альфу там, где пламя всё-таки оказалось прямо
//  под днищем: даже расталкивание не спасает от совсем узких котлов.
//
//  Считать это через реальную геометрию (depth-тест, коллизии) было бы дороже
//  на порядки и всё равно выглядело бы хуже — настоящее пламя не обрезается по
//  препятствию, а обтекает его.
//
//  ═══ ЯЗЫКИ СВЕРХ ЛИМИТА ═══
//
//  _Height задаёт, докуда пламя достаёт «в среднем». Но огонь, ровно
//  обрезанный по высоте, выглядит газовой конфоркой, а не костром. Поэтому
//  поверх идёт ВТОРОЙ шум — медленный и крупный, — который локально
//  приподнимает порог. Отдельные языки прорываются выше лимита и опадают.
//  Это чистый эффект: на подводимое тепло он не влияет никак.
//
//  ═══ ДВА УРОВНЯ КАЧЕСТВА ═══
//
//  FIRE_SIMPLE снимает вторую октаву шума и языки — вдвое дешевле фрагмент.
//  Переключает FireVisualController по расстоянию до камеры. Дальний костёр
//  занимает тридцать пикселей, и разницы там не видно.
//
//  ═══ ТРЕБОВАНИЯ К МЕШУ ═══
//
//  Цилиндр БЕЗ КРЫШЕК, пивот в основании, локальная Y вверх, высота 1, радиус 1.
//  Развёртка: U по окружности (0..1), V по высоте (0 низ, 1 верх).
//  Годится стандартный Cylinder с удалёнными крышками; лучше — 16 сегментов
//  по кругу и 8 по высоте.
// =============================================================
Shader "CatCooks/Fire"
{
    Properties
    {
        [Header(Palette)]
        _ColorCore ("Ядро (низ, самое горячее)", Color) = (1.0, 0.92, 0.55, 1)
        _ColorMid  ("Середина",                  Color) = (1.0, 0.45, 0.08, 1)
        _ColorTip  ("Кромка (верх, остывающая)", Color) = (0.75, 0.10, 0.02, 1)

        _Intensity ("Яркость свечения", Range(0, 6)) = 2.0

        [Header(Shape)]
        // Гонится из FireVisualController по мощности костра.
        _Height    ("Высота пламени 0..1", Range(0, 1)) = 0.6
        _Sharpness ("Резкость кромки", Range(0.02, 0.8)) = 0.22
        _Taper     ("Сужение кверху", Range(0, 1)) = 0.45
        _BaseFade  ("Растворение у самой земли", Range(0, 0.5)) = 0.08

        [Header(Tongues)]
        _Tongue      ("Сила языков сверх лимита", Range(0, 1)) = 0.35
        _TongueScale ("Крупность языков", Range(0.5, 8)) = 2.5
        _TongueSpeed ("Скорость языков", Range(0, 3)) = 0.6

        [Header(Noise)]
        _NoiseScaleU ("Шум по окружности", Range(1, 16)) = 5
        _NoiseScaleV ("Шум по высоте",     Range(1, 16)) = 3
        _ScrollSpeed ("Скорость подъёма",  Range(0, 6)) = 1.8
        _Swirl       ("Закрутка",          Range(0, 2)) = 0.35

        [Header(Obstacle)]
        // Мировая Y дна котла и его радиус. Ставит FireVisualController.
        // Y = 9999 означает «препятствия нет».
        _ObstacleY      ("Дно котла (мировая Y)", Float) = 9999
        _ObstacleRadius ("Радиус котла", Float) = 0
        _Flare          ("Сила обтекания", Range(0, 2)) = 0.8
        _FlareFade      ("Мягкость входа в обтекание", Range(0.01, 1)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CatCooksFire"
            Tags { "LightMode" = "UniversalForward" }

            // Аддитивно с учётом альфы: пламя СВЕТИТСЯ, а не закрашивает.
            // Сортировка при этом не нужна вовсе — сложение коммутативно, и
            // два костра друг за другом не мерцают при повороте камеры.
            Blend SrcAlpha One
            ZWrite Off
            // Оболочка двусторонняя: изнутри видно заднюю стенку, и пламя
            // читается объёмным, а не плёнкой.
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile_local_fragment _ FIRE_SIMPLE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorCore, _ColorMid, _ColorTip;
                float  _Intensity;
                float  _Height, _Sharpness, _Taper, _BaseFade;
                float  _Tongue, _TongueScale, _TongueSpeed;
                float  _NoiseScaleU, _NoiseScaleV, _ScrollSpeed, _Swirl;
                float  _ObstacleY, _ObstacleRadius, _Flare, _FlareFade;
            CBUFFER_END

            // Инстансные поля: у каждого костра своя высота, свой котёл и своя
            // фаза. Без фазы соседние костры пульсировали бы синхронно, и вся
            // площадка дышала бы как один организм.
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float,  _InstHeight)
                UNITY_DEFINE_INSTANCED_PROP(float,  _InstPhase)
                UNITY_DEFINE_INSTANCED_PROP(float,  _InstObstacleY)
                UNITY_DEFINE_INSTANCED_PROP(float,  _InstObstacleRadius)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstTint)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                // x — доля высоты, y — «насколько мы под котлом»,
                // z — фаза инстанса, w — высота пламени инстанса.
                float4 data       : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Дешёвый 2D value-noise. Текстуру не берём намеренно: выборка из
            // памяти на прозрачной геометрии с большим овердро обходится
            // дороже, чем эта арифметика, и тянет за собой лишний ассет.
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float noise2(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                float h01 = saturate(v.positionOS.y);

                float height   = UNITY_ACCESS_INSTANCED_PROP(Props, _InstHeight);
                float phase    = UNITY_ACCESS_INSTANCED_PROP(Props, _InstPhase);
                float obstY    = UNITY_ACCESS_INSTANCED_PROP(Props, _InstObstacleY);
                float obstR    = UNITY_ACCESS_INSTANCED_PROP(Props, _InstObstacleRadius);

                // Сужение кверху: пламя не колонна.
                float3 posOS = v.positionOS.xyz;
                posOS.xz *= lerp(1.0, 1.0 - _Taper, h01);

                float3 posWS = TransformObjectToWorld(posOS);
                float3 baseWS = TransformObjectToWorld(float3(0, 0, 0));

                // ── ОБТЕКАНИЕ КОТЛА ──
                // Выше дна и внутри радиуса — расталкиваем наружу. Всё в
                // вершинном шейдере: вершин полтораста, стоит ноль.
                float flow = 0.0;
                if (obstR > 0.001)
                {
                    float3 rel = posWS - baseWS;
                    float radial = length(rel.xz);

                    // Плавный вход, иначе на границе виден залом.
                    float above  = saturate((posWS.y - obstY) / _FlareFade);
                    float inside = 1.0 - saturate(radial / obstR);
                    flow = above * inside;

                    float2 dir = radial > 1e-4 ? rel.xz / radial : float2(1, 0);
                    posWS.xz += dir * (flow * _Flare * obstR);
                }

                o.positionCS = TransformWorldToHClip(posWS);
                o.uv   = v.uv;
                o.data = float4(h01, flow, phase, height);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                float h01    = i.data.x;
                float flow   = i.data.y;
                float phase  = i.data.z;
                float height = max(0.001, i.data.w);

                float t = _Time.y + phase;

                // Закрутка: сдвиг по окружности растёт с высотой, и пламя
                // «завивается». Одна строка, а читается как движение воздуха.
                float2 uv = i.uv;
                uv.x += h01 * _Swirl;

                float2 p = float2(uv.x * _NoiseScaleU, uv.y * _NoiseScaleV - t * _ScrollSpeed);

                float n = noise2(p);
            #ifndef FIRE_SIMPLE
                // Вторая октава — мелкая рябь по краям. На дальних кострах
                // отключается: там она меньше пикселя.
                n = n * 0.65 + noise2(p * 2.17 + 13.7) * 0.35;
            #endif

                // Порог по высоте. Чем выше точка относительно разрешённой
                // высоты, тем более сильный шум нужен, чтобы пламя там выжило.
                float cutoff = h01 / height;

            #ifndef FIRE_SIMPLE
                // ── ЯЗЫКИ СВЕРХ ЛИМИТА ──
                // Медленный крупный шум локально опускает порог: отдельные
                // языки выстреливают выше _Height и опадают. Ровно обрезанное
                // по высоте пламя выглядит конфоркой, а не костром.
                float tongue = noise2(float2(uv.x * _TongueScale, t * _TongueSpeed));
                cutoff -= tongue * tongue * _Tongue;
            #endif

                float alpha = smoothstep(cutoff - _Sharpness, cutoff + _Sharpness, n);

                // У самой земли растворяем: жёсткий срез по низу выдаёт цилиндр.
                alpha *= smoothstep(0.0, _BaseFade, h01);

                // Под днищем котла гасим остатки — расталкивания хватает не
                // всегда, узкий котёл пламя всё равно пробьёт.
                alpha *= 1.0 - flow * 0.75;

                clip(alpha - 0.01);

                // Палитра по ОСТАВШЕМУСЯ ЗАПАСУ, а не по высоте: язык,
                // выстреливший вверх, остаётся горячим по всей длине, а
                // затухающий край краснеет — как в настоящем огне.
                float heat = saturate(1.0 - cutoff);
                float3 col = lerp(_ColorTip.rgb, _ColorMid.rgb, saturate(heat * 1.8));
                col = lerp(col, _ColorCore.rgb, saturate((heat - 0.55) * 2.2));

                float4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _InstTint);
                col *= tint.rgb;

                return half4(col * _Intensity * alpha, alpha * tint.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
