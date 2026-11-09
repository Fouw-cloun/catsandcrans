// ============================================================================
//  CatCooks/Diorama Ring  —  слой диорамы
//  Цилиндрическое кольцо вокруг игровой зоны. Unity 6 / URP.
// ----------------------------------------------------------------------------
//  ЗАЧЕМ
//
//  Дальний город можно строить коробками, а можно нарисовать. Нарисованный
//  стоит несколько отрисовок на весь горизонт вместо сотен, и выглядит лучше:
//  художник рисует силуэт целиком, а не собирает его из параллелепипедов.
//
//  Несколько таких колец на разных радиусах дают параллакс. Настоящий, а не
//  поддельный: ближнее кольцо смещается относительно дальнего ровно потому,
//  что оно ближе.
//
//  ═══ ПОЧЕМУ КОЛЬЦА НЕ ДВИГАЮТСЯ ЗА КАМЕРОЙ ═══
//
//  Обычно диораму привязывают к камере, чтобы она не кончалась. Для открытого
//  мира иначе нельзя, но здесь игровая зона — три башни, и игрок смещается по
//  горизонтали от силы на сотню метров.
//
//  Кольцо на шестистах метрах при таком смещении почти не меняет вид, и
//  двигать его незачем. А привязка к камере как раз и выдаёт приём: слои
//  начинают заметно «ехать» при быстром повороте, и горизонт перестаёт быть
//  местом, превращаясь в обои.
//
//  ═══ ПОЧЕМУ ПОСЛЕ НЕПРОЗРАЧНОЙ ГЕОМЕТРИИ, А НЕ ДО ═══
//
//  Соблазн поставить кольца ПЕРЕД непрозрачной геометрией велик: тогда башни
//  закрасят их сами, и не нужна ни сортировка, ни проверка глубины.
//
//  Так не работает. Скайбокс рисуется ПОСЛЕ непрозрачной очереди, и он
//  закрашивает всё, что не записало глубину. Кольцо с выключенным ZWrite
//  глубину не пишет — и просто исчезает под небом. Снаружи это выглядит как
//  «объекты в сцене есть, а на экране пусто».
//
//  Поэтому очередь прозрачная. Перекрытие башнями при этом всё равно
//  бесплатное: к моменту отрисовки колец глубина от них уже записана, и
//  ZTest LEqual отсекает закрытые места сам.
//
//  Сортировки между слоями тоже нет: порядок задан номером очереди, от
//  дальнего кольца к ближнему, и спорить им не о чем.
//
//  ═══ ЦЕНА ═══
//
//  Одна отрисовка на слой. Перерисовка ограничена полосой горизонта, а не
//  всем экраном: кольцо занимает по высоте столько, сколько ему задали.
// ============================================================================
Shader "CatCooks/Diorama Ring"
{
    Properties
    {
        [Header(Art)]
        [MainTexture] _MainTex ("Layer Art (RGBA)", 2D) = "white" {}
        [MainColor]   _Color ("Tint", Color) = (1,1,1,1)

        [Tooltip(How many times the art repeats around the ring. Use a seamless texture)]
        _Repeat ("Repeat Around", Range(1, 16)) = 4

        [Header(Drift)]
        [Tooltip(Sideways drift. Tiny values only. Real skylines do not move so this is for clouds and haze layers)]
        _Scroll ("Drift Speed", Range(-0.05, 0.05)) = 0

        [Tooltip(Second sampling of the same texture at another scale and speed. Costs one extra fetch and kills the repetition)]
        _Layer2 ("Second Pass Blend", Range(0,1)) = 0
        _Layer2Scale ("Second Pass Scale", Range(0.2, 3)) = 1.7
        _Layer2Scroll ("Second Pass Drift", Range(-0.05, 0.05)) = 0.004

        [Header(Swirl)]
        [Toggle(_SWIRL)] _UseSwirl ("Swirl", Float) = 0

        [Tooltip(Slow churning of the cloud mass. This is NOT wind. Wind moves a cloud sideways as a whole and reads as a sliding texture. Churning changes its SHAPE in place which is what real clouds do and what sells them as volume)]
        _Swirl ("Swirl Amount", Range(0, 0.12)) = 0.03
        _SwirlScale ("Swirl Scale", Range(0.5, 8)) = 2.2
        _SwirlSpeed ("Swirl Speed", Range(0, 1)) = 0.12

        [Header(Fade)]
        [Tooltip(The band must dissolve at both edges or the cylinder shows its seams as two hard horizontal lines)]
        _FadeTop ("Fade Top", Range(0, 0.9)) = 0.35
        _FadeBottom ("Fade Bottom", Range(0, 0.9)) = 0.18

        [Header(Haze)]
        [Tooltip(MUST match the Haze Color on the sky and the city or the layer will float)]
        _HazeColor ("Haze Color", Color) = (0.714, 0.616, 0.608, 1)

        [Tooltip(How far this layer is sunk into the air. Further rings take more)]
        _Haze ("Haze Amount", Range(0,1)) = 0.5

        [Header(Setup)]
        [Tooltip(Draw order. Lower draws first. Give the farthest ring the lowest number)]
        [IntRange] _QueueOffset ("Queue Offset", Range(-100, 0)) = -50
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"

            // Прозрачная очередь: после непрозрачной геометрии И после неба.
            // Точный номер ставит DioramaRig при сборке, по полю Queue Offset.
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "DioramaRing"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual

            // ОТСЕЧЕНИЕ ВЫКЛЮЧЕНО, и это не расточительство.
            //
            // Камера стоит ВНУТРИ цилиндра, поэтому ближняя стенка всегда за
            // спиной и в кадр не попадает: рисуется ровно одна стенка, как
            // при любом отсечении. Зато отпадает вопрос, какая сторона граней
            // считается лицевой — а он зависит от порядка вершин и молча
            // делает кольцо невидимым, если угадать неправильно.
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment _SWIRL

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color, _HazeColor;
                float _Repeat, _Scroll;
                float _Layer2, _Layer2Scale, _Layer2Scroll;
                float _FadeTop, _FadeBottom, _Haze;
                float _Swirl, _SwirlScale, _SwirlSpeed;
                float _QueueOffset;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                // Вертикальная координата полосы, 0 внизу и 1 наверху.
                half   vpos       : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);

                float2 uv = IN.uv;
                uv.x *= _Repeat;

                OUT.uv = float2(uv.x + _Time.y * _Scroll, uv.y);

                // Второй проход по той же текстуре в другом масштабе и с
                // другой скоростью. Один лишний семпл, а повторяемость по
                // кругу пропадает: два несовпадающих периода не совпадут
                // ещё очень долго.
                OUT.uv2 = float2(uv.x * _Layer2Scale + _Time.y * _Layer2Scroll,
                                 uv.y * _Layer2Scale);

                // Вертикальная координата едет в пиксель как есть.
                // Затухание считается ТАМ — см. Frag.
                OUT.vpos = IN.uv.y;
                return OUT;
            }

            /// ЗАВИХРЕНИЕ.
            ///
            /// Это НЕ ветер. Ветер сдвигает облако целиком, и на кольце он
            /// читается как ползущая текстура — приём выдаёт себя сразу.
            ///
            /// Здесь облако меняет ФОРМУ на месте: развёртка гуляет по двум
            /// несовпадающим волнам, и масса медленно перетекает сама в себя.
            /// Настоящие облака ведут себя именно так, и именно это придаёт
            /// им объём на плоской картинке.
            ///
            /// Периоды взяты несоизмеримыми — 6.283 против 4.0 и 3.7 против
            /// 5.9. Совпади они, узор начал бы повторяться раз в несколько
            /// секунд, и вместо облаков получилась бы пульсация.
            float2 Swirl(float2 uv)
            {
                float t = _Time.y * _SwirlSpeed;
                float sx = uv.x * 6.283 * _SwirlScale;
                float sy = uv.y * 4.0 * _SwirlScale;

                return float2(
                    sin(sx + t) * cos(sy + t * 0.71),
                    cos(sx * 0.59 - t * 0.83) * sin(sy * 1.47 - t * 0.47)
                ) * _Swirl;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float2 uv2 = IN.uv2;

            #if defined(_SWIRL)
                float2 w = Swirl(IN.uv);
                uv += w;

                // Второй проход вихрится в противофазе. Одинаковое смещение
                // двигало бы оба слоя как один, и весь смысл второго прохода
                // — несовпадение — пропал бы.
                uv2 -= w * 0.7;
            #endif

                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                if (_Layer2 > 0.001)
                {
                    half4 c2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv2);
                    c = lerp(c, max(c, c2), _Layer2);
                }

                c *= _Color;

                // Цвет уходит в дымку, прозрачность — нет: слой должен
                // потерять детали, но не раствориться целиком, иначе за ним
                // окажется пустое небо.
                c.rgb = lerp(c.rgb, _HazeColor.rgb, _Haze);

                // ═══ РАСТВОРЕНИЕ КРАЁВ — В ПИКСЕЛЕ, А НЕ В ВЕРШИНЕ ═══
                //
                // Здесь раньше стоял расчёт в вершинном шейдере, и это была
                // ошибка, которая гасила кольцо целиком.
                //
                // У кольца по вертикали ВСЕГО ДВА РЯДА ВЕРШИН: низ и верх.
                // У нижней uv.y равен нулю, у верхней единице — ровно те две
                // точки, где затухание по построению равно нулю. Между нулём
                // и нулём интерполировать нечего, и альфа получалась нулевой
                // по всей полосе.
                //
                // Снаружи это выглядит неотличимо от «материал не работает»:
                // форма в сцене есть, обводка выделения идёт по облакам, а на
                // экране пусто.
                //
                // В пикселе координата уже интерполирована и проходит все
                // значения от нуля до единицы. Стоит это два smoothstep на
                // пиксель — на полосе горизонта незаметно.
                half top = smoothstep(1.0h, 1.0h - max(0.01h, _FadeTop), IN.vpos);
                half bottom = smoothstep(0.0h, max(0.01h, _FadeBottom), IN.vpos);

                c.a *= top * bottom;

                return c;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
