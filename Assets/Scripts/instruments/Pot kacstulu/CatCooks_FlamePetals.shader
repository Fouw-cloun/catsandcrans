// =============================================================
//  CatCooks_FlamePetals.shader
//  Мультяшное пламя из лепестков. URP 17 / Unity 6.
//  Работает в паре с FlamePetalMesh.cs — тот строит геометрию.
//
//  ═══ ЧТО ЗДЕСЬ ПРОИСХОДИТ ═══
//
//  Геометрия даёт СИЛУЭТ (лепестки-карточки кольцами), шейдер даёт ЖИЗНЬ:
//  колыхание, высоту по мощности костра, температурные полосы и рваную кромку.
//
//  Разделение принципиальное. Всё, что можно выразить формой, выражено формой:
//  силуэт не зависит ни от шума, ни от разрешения, ни от качества шейдера. А всё,
//  что должно двигаться, двигается в ВЕРШИННОМ шейдере — 96 вершин против сотен
//  тысяч пикселей.
//
//  ═══ ТЕМПЕРАТУРА ИЗ НОМЕРА КОЛЬЦА ═══
//
//  В uv1.y приходит ringT: 0 — сердцевина, 1 — край. Из него выводится цвет:
//  внутренние лепестки бело-жёлтые, внешние красные. Так у пламени появляется
//  температурное расслоение, которого нет ни у одной однослойной оболочки.
//
//  Полосы при этом ЖЁСТКИЕ (step, а не плавный градиент) — это и делает огонь
//  мультяшным. Плавный переход дал бы реалистичное дымное пламя.
//
//  ═══ ТРИ УРОВНЯ ДВИЖЕНИЯ ═══
//
//  Настоящее пламя дрожит на нескольких масштабах сразу, и именно это отличает
//  живой огонь от анимированной картинки:
//    • колыхание всего лепестка (медленное, у каждого своя фаза);
//    • пульсация высоты (быстрая, общая для костра);
//    • рваная кромка шумом (мелкая, на самом верху).
//
//  Одного уровня мало: только колыхание — и пламя выглядит флагом на ветру,
//  только шум — и оно стоит на месте, кипя.
//
//  ═══ РАЗДУВ ═══
//
//  _InstHeight приходит из FireVisualController и равен мощности костра. Он
//  тянет лепестки ВВЕРХ и слегка разводит НАРУЖУ: раздутый мехами костёр не
//  просто выше, он ещё и шире — как настоящий, которому дали воздуха.
//
//  ═══ ОПТИМИЗАЦИЯ ═══
//
//  Один меш, один DrawCall, никакого CPU. Овердро низкое: лепестки тонкие и
//  почти не перекрываются, в отличие от стопки билбордов у частиц. Шум — две
//  октавы процедурного value-noise без единой выборки из текстуры.
// =============================================================
Shader "CatCooks/Flame Petals"
{
    Properties
    {
        [Header(Temperature)]
        _ColorHot  ("Сердцевина (горячее)", Color) = (1.0, 0.96, 0.72, 1)
        _ColorMid  ("Середина",             Color) = (1.0, 0.55, 0.10, 1)
        _ColorCool ("Край (холоднее)",      Color) = (0.85, 0.16, 0.04, 1)

        _CoreBoost ("Пересвет сердцевины (для Bloom)", Range(1, 6)) = 2.2

        [Header(Toon bands)]
        _BandLow  ("Порог кромки",     Range(0, 1)) = 0.30
        _BandMid  ("Порог середины",   Range(0, 1)) = 0.50
        _BandHigh ("Порог сердцевины", Range(0, 1)) = 0.72
        _BandSoft ("Сглаживание полос", Range(0, 0.25)) = 0.035

        [Header(Shape)]
        _Height   ("Высота 0..1", Range(0, 1)) = 0.7
        _TipNoise ("Рваность кромки", Range(0, 1)) = 0.55
        _SideFade ("Скругление боков", Range(0, 1)) = 0.65
        _BaseFade ("Растворение у земли", Range(0, 0.4)) = 0.05

        [Header(Motion)]
        _SwayAmount ("Колыхание", Range(0, 0.6)) = 0.16
        _SwaySpeed  ("Скорость колыхания", Range(0, 6)) = 1.7
        _Pulse      ("Пульсация высоты", Range(0, 0.4)) = 0.12
        _PulseSpeed ("Скорость пульсации", Range(0, 12)) = 5.5

        [Header(Noise)]
        _NoiseScale ("Крупность шума", Range(0.5, 12)) = 3.0
        _NoiseSpeed ("Скорость шума", Range(0, 6)) = 2.2

        [Header(Blow)]
        _Spread ("Разведение наружу при раздуве", Range(0, 1)) = 0.25
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
            Name "CatCooksFlamePetals"
            Tags { "LightMode" = "UniversalForward" }

            // Аддитивно с учётом альфы: пламя светится, а не закрашивает.
            // Сортировка при этом не нужна — сложение коммутативно, и лепестки
            // не мерцают при повороте камеры друг относительно друга.
            Blend SrcAlpha One
            ZWrite Off
            // Обратные стороны уже продублированы в меше — режем лишнее здесь.
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local_fragment _ FIRE_SIMPLE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorHot, _ColorMid, _ColorCool;
                float  _CoreBoost;
                float  _BandLow, _BandMid, _BandHigh, _BandSoft;
                float  _Height, _TipNoise, _SideFade, _BaseFade;
                float  _SwayAmount, _SwaySpeed, _Pulse, _PulseSpeed;
                float  _NoiseScale, _NoiseSpeed, _Spread;
            CBUFFER_END

            // Высота, фаза и оттенок — на инстанс: костров на площадке
            // несколько, материал у них ОДИН, а состояние у каждого своё.
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float,  _InstHeight)
                UNITY_DEFINE_INSTANCED_PROP(float,  _InstPhase)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstTint)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 petal      : TEXCOORD1;   // фаза, ringT, высота
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 petal      : TEXCOORD1;
                float2 extra      : TEXCOORD2;   // фаза инстанса, высота
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

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
                return lerp(lerp(hash21(i),                hash21(i + float2(1, 0)), f.x),
                            lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), f.x), f.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                float height = UNITY_ACCESS_INSTANCED_PROP(Props, _InstHeight);
                float phase  = UNITY_ACCESS_INSTANCED_PROP(Props, _InstPhase);

                float petalPhase = v.petal.x;
                float ringT      = v.petal.y;

                float h01 = saturate(v.uv.y);
                float t = _Time.y + phase;

                float3 pos = v.positionOS.xyz;

                // ── ВЫСОТА ПО МОЩНОСТИ ──
                // Пульсация добавляется поверх и у каждого лепестка своя фаза:
                // без этого весь костёр дышал бы как один кусок резины.
                float pulse = 1.0 + sin(t * _PulseSpeed + petalPhase * 6.283) * _Pulse;
                float grow = lerp(0.35, 1.0, height) * pulse;
                pos.y *= grow;

                // ── РАЗДУВ ──
                // Раздутый костёр не просто выше — он ещё и шире. Разводим
                // лепестки наружу пропорционально высоте точки: основание
                // остаётся на месте, расходятся кончики.
                float2 outward = normalize(float2(pos.x, pos.z) + 0.0001);
                pos.xz += outward * (height * _Spread * h01 * (0.4 + ringT));

                // ── КОЛЫХАНИЕ ──
                // Квадрат по высоте: основание лепестка стоит неподвижно, а
                // кончик гуляет. Линейная зависимость выглядела бы так, будто
                // лепесток качается целиком, вырванный из костра.
                float sway = sin(t * _SwaySpeed + petalPhase * 6.283) * _SwayAmount;
                pos.x += sway * h01 * h01;
                pos.z += cos(t * _SwaySpeed * 0.83 + petalPhase * 5.1) * _SwayAmount * h01 * h01;

                o.positionCS = TransformObjectToHClip(pos);
                o.uv = v.uv;
                o.petal = v.petal;
                o.extra = float2(phase, height);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                float h01 = saturate(i.uv.y);
                float ringT = i.petal.y;
                float petalPhase = i.petal.x;
                float height = max(0.001, i.extra.y);
                float t = _Time.y + i.extra.x;

                // ── шум ──
                float2 np = float2(i.uv.x * _NoiseScale + petalPhase * 7.3,
                                   i.uv.y * _NoiseScale - t * _NoiseSpeed);
                float n = noise2(np);
            #ifndef FIRE_SIMPLE
                n = n * 0.65 + noise2(np * 2.3 + 4.7) * 0.35;
            #endif

                // ── форма ──
                // Скругление боков: без него лепесток остаётся трапецией с
                // прямыми рёбрами, и стык карточек виден как гранёный контур.
                float side = sin(saturate(i.uv.x) * 3.14159);
                float mask = lerp(1.0, side, _SideFade);

                // Запас по высоте: чем выше точка, тем меньше маска. Кромка
                // съедается шумом тем сильнее, чем ближе к верхушке.
                float remain = saturate(1.0 - h01);
                mask *= lerp(remain, remain * (0.45 + n * 0.85), _TipNoise);

                // Растворение у самой земли: жёсткий срез снизу выдаёт
                // геометрию карточек.
                mask *= smoothstep(0.0, _BaseFade + 0.001, h01);

                float value = mask;

                // ── ТООН-ПОЛОСЫ ──
                // Три жёсткие ступени вместо градиента. Именно ступени делают
                // огонь мультяшным: плавный переход дал бы реалистичное
                // дымное пламя.
                float s = _BandSoft;
                float aOuter = smoothstep(_BandLow  - s, _BandLow  + s, value);
                float aMid   = smoothstep(_BandMid  - s, _BandMid  + s, value);
                float aCore  = smoothstep(_BandHigh - s, _BandHigh + s, value);

                if (aOuter <= 0.004) discard;

                // ── ТЕМПЕРАТУРА ──
                // Номер кольца сдвигает всю палитру: внутренние лепестки
                // холоднее не бывают, внешние горячее не бывают.
                float3 cool = lerp(_ColorMid.rgb, _ColorCool.rgb, ringT);
                float3 mid  = lerp(_ColorHot.rgb, _ColorMid.rgb, ringT);

                float3 col = cool;
                col = lerp(col, mid, aMid);
                // Сердцевину уводим за единицу — свечение даст Bloom, отдельный
                // аддитивный слой для этого не нужен.
                col = lerp(col, _ColorHot.rgb * _CoreBoost, aCore * (1.0 - ringT * 0.7));

                float4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _InstTint);

                return half4(col * tint.rgb, aOuter * tint.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
