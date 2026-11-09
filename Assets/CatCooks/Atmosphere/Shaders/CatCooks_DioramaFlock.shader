// ============================================================================
//  CatCooks/Diorama Flock  —  птицы и всё летающее вдали
//  Unity 6 / URP.
// ----------------------------------------------------------------------------
//  ═══ ГЛАВНОЕ: НИ ОДНОГО ОБЪЕКТА И НИ ОДНОГО UPDATE ═══
//
//  Обычно птиц делают объектами со скриптом полёта. Сотня птиц — это сотня
//  вызовов Update, сотня трансформов и сотня отрисовок. Для декорации, до
//  которой двести метров, это несуразно дорого.
//
//  Здесь вся стая — ОДИН статичный меш из квадратов. У каждого квадрата свой
//  номер, записанный в цвет вершин, и от этого номера вершинный шейдер
//  считает орбиту, высоту, скорость и фазу взмаха. Процессор не участвует
//  вообще: он однажды залил меш и забыл.
//
//  Одна отрисовка на всю стаю, ноль работы на кадр.
//
//  ═══ ПОЧЕМУ ОТСЕЧЕНИЕ ПО АЛЬФЕ, А НЕ СМЕШИВАНИЕ ═══
//
//  Птица — маленькое тёмное пятно. Полупрозрачность ей не нужна, а стоит
//  дорого: прозрачные объекты надо сортировать по глубине, и сотня птиц дала
//  бы сотню отдельных отрисовок в правильном порядке.
//
//  Отсечение по альфе оставляет стаю НЕПРОЗРАЧНОЙ: одна отрисовка, честная
//  запись глубины, правильное перекрытие башнями без всякой сортировки.
//
//  ═══ ЧТО КЛАСТЬ В ТЕКСТУРУ ═══
//
//  Силуэт птицы белым по прозрачному. Цвет берётся из материала, потому что
//  на таком расстоянии птица — это силуэт и ничего больше.
// ============================================================================
Shader "CatCooks/Diorama Flock"
{
    Properties
    {
        [Header(Art)]
        [MainTexture] _MainTex ("Silhouette (A)", 2D) = "white" {}
        [MainColor]   _Color ("Tint", Color) = (0.22, 0.24, 0.28, 1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.4

        [Header(Flight)]
        [Tooltip(Metres per second along the orbit. Real birds at distance read slower than you expect)]
        _Speed ("Speed", Range(0, 20)) = 3.5

        [Tooltip(How much individual birds differ in speed. Zero makes the flock move like one rigid object)]
        _SpeedVary ("Speed Variation", Range(0, 1)) = 0.45

        [Tooltip(Vertical bob height in metres)]
        _Bob ("Bob Height", Range(0, 20)) = 4
        _BobSpeed ("Bob Speed", Range(0, 4)) = 0.5

        [Header(Wings)]
        [Tooltip(Flaps per second. Zero holds the wings out for gliding birds and planes)]
        _Flap ("Flap Speed", Range(0, 12)) = 3.5

        [Tooltip(How far the silhouette squashes on the downstroke. This is what reads as flapping from far away)]
        _FlapAmount ("Flap Depth", Range(0, 0.9)) = 0.55

        [Header(Size)]
        _Size ("Size (m)", Range(0.1, 40)) = 2.5
        _SizeVary ("Size Variation", Range(0, 1)) = 0.35

        [Header(Haze)]
        _HazeColor ("Haze Color", Color) = (0.714, 0.616, 0.608, 1)
        _HazeStart ("Haze Start (m)", Float) = 150
        _HazeEnd ("Haze End (m)", Float) = 600
        _HazeAmount ("Haze Amount", Range(0,1)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
        }

        Pass
        {
            Name "Flock"
            Tags { "LightMode" = "UniversalForwardOnly" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color, _HazeColor;
                float _Cutoff;
                float _Speed, _SpeedVary, _Bob, _BobSpeed;
                float _Flap, _FlapAmount;
                float _Size, _SizeVary;
                float _HazeStart, _HazeEnd, _HazeAmount;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                // positionOS.xz — центр орбиты, positionOS.y — высота.
                // Строит их DioramaRig при сборке меша.
                float4 positionOS : POSITION;

                // Угол квадрата, 0..1
                float2 uv : TEXCOORD0;

                // НОМЕР ЭКЗЕМПЛЯРА. Одинаковый у всех четырёх вершин квадрата,
                // разный между квадратами. Из него растёт вся индивидуальность
                // птицы: скорость, размер, фаза взмаха, направление полёта.
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half   haze       : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float seedA = IN.color.r;
                float seedB = IN.color.g;
                float seedC = IN.color.b;

                // ── ОРБИТА ──
                //
                // Птица ходит по кругу вокруг точки, заданной при сборке.
                // Радиус берётся из самой позиции: чем дальше вершина от
                // центра стаи, тем шире её круг. Это дешевле, чем хранить
                // радиус отдельно, и выглядит естественно — крайние птицы
                // облетают стаю по большой дуге.
                float radius = length(IN.positionOS.xz);
                float base = atan2(IN.positionOS.z, IN.positionOS.x);

                float speed = _Speed * lerp(1.0 - _SpeedVary, 1.0 + _SpeedVary, seedA);

                // Половина стаи летит в обратную сторону: односторонняя
                // карусель читается как механизм, а не как птицы.
                float dir = seedC > 0.5 ? 1.0 : -1.0;

                float angle = base + _Time.y * speed / max(1.0, radius) * dir;

                float3 centre;
                centre.x = cos(angle) * radius;
                centre.z = sin(angle) * radius;
                centre.y = IN.positionOS.y
                         + sin(_Time.y * _BobSpeed + seedB * 6.283) * _Bob;

                float3 centreWS = TransformObjectToWorld(centre);

                // ── БИЛБОРД ──
                float3 toCam = GetWorldSpaceViewDir(centreWS);
                float dist = length(toCam);
                float3 fwd = toCam / max(0.001, dist);

                float3 right = normalize(cross(float3(0, 1, 0), fwd));
                float3 up = cross(fwd, right);

                // ── ВЗМАХ ──
                //
                // Квадрат сплющивается по вертикали. На двухстах метрах
                // силуэт — это несколько пикселей, и разглядеть настоящие
                // крылья невозможно; мигание высоты читается как взмах
                // безошибочно и стоит одного синуса.
                float flap = _Flap > 0.001
                    ? 1.0 - abs(sin(_Time.y * _Flap + seedB * 9.42)) * _FlapAmount
                    : 1.0;

                float size = _Size * lerp(1.0 - _SizeVary, 1.0 + _SizeVary, seedB);

                float2 corner = (IN.uv - 0.5) * size;

                // Наклон по ходу движения: горизонтальный силуэт выглядит
                // приклеенным к экрану.
                float tilt = dir * 0.18;
                corner = float2(corner.x - corner.y * tilt, corner.y * flap);

                float3 world = centreWS + right * corner.x + up * corner.y;

                OUT.positionCS = TransformWorldToHClip(world);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);

                OUT.haze = saturate((dist - _HazeStart)
                         / max(1.0, _HazeEnd - _HazeStart)) * _HazeAmount;

                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                clip(t.a - _Cutoff);

                half3 col = lerp(_Color.rgb, _HazeColor.rgb, IN.haze);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
