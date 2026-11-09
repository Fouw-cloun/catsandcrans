// =============================================================
//  CatCooks_Leaf.shader
//  Мультяшный лист для системы частиц. URP 17 / Unity 6.
//
//  ═══ СИЛУЭТ СЧИТАЕТСЯ, А НЕ БЕРЁТСЯ ИЗ ТЕКСТУРЫ ═══
//
//  Форма листа выводится формулой из UV. Причин три, и экономия памяти среди
//  них последняя:
//
//    • Текстура листа — это ОДИН лист. Пять форм означают пять текстур или
//      атлас с раскладкой; здесь форма меняется числом.
//    • Края остаются чёткими на любом расстоянии. Текстура на мелкой частице
//      превращается в мыло, а формула — нет.
//    • Изгиб листа анимируется. У текстуры он запечён навсегда.
//
//  ═══ ПОДДЕЛКА ОБЪЁМА ═══
//
//  Частица — плоский квад. Настоящего объёма у неё нет, но лист и не должен
//  выглядеть объёмным: в рисованной мультипликации он плоский, зато у него
//  есть СГИБ — светлая половина и тёмная.
//
//  Сгиб здесь идёт по прожилке и разворачивается вместе с частицей. Когда
//  лист переворачивается в полёте, светлая и тёмная половины меняются местами
//  сами — и плоский квад читается как перевернувшийся лист.
//
//  ═══ ЦВЕТ БЕРЁТСЯ ИЗ СИСТЕМЫ ЧАСТИЦ ═══
//
//  Start Color в Particle System умножается на всё. Значит разброс оттенков
//  задаётся там (Random Between Two Colors), а шейдер про палитру ничего не
//  знает — и один материал обслуживает и осеннюю листву, и весеннюю.
// =============================================================
Shader "CatCooks/Leaf"
{
    Properties
    {
        [Header(Shape)]
        _Width  ("Ширина листа", Range(0.05, 1)) = 0.42
        _Sharp  ("Заострённость концов", Range(0.2, 3)) = 0.72
        _Curve  ("Изгиб силуэта", Range(-0.4, 0.4)) = 0.12
        _EdgeSoft ("Мягкость края", Range(0.001, 0.1)) = 0.012

        [Header(Vein)]
        _VeinColor ("Цвет прожилки", Color) = (1, 1, 1, 0.55)
        _VeinWidth ("Толщина прожилки", Range(0, 0.12)) = 0.022
        _SideVeins ("Боковых прожилок", Range(0, 12)) = 5
        _SideVeinGain ("Заметность боковых", Range(0, 1)) = 0.3

        [Header(Fold)]
        _FoldStrength ("Сила сгиба", Range(0, 1)) = 0.45
        _FoldColor    ("Цвет теневой половины", Color) = (0.55, 0.5, 0.35, 1)

        [Header(Light)]
        _RimGain  ("Подсветка края", Range(0, 2)) = 0.35
        _RimColor ("Цвет подсветки", Color) = (1, 0.97, 0.8, 1)

        [Header(Wear)]
        _Notch ("Рваность края", Range(0, 1)) = 0.35
        _NotchScale ("Частота рваности", Range(2, 40)) = 14
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Pass
        {
            Name "CatCooksLeaf"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _Width, _Sharp, _Curve, _EdgeSoft;
                float4 _VeinColor;
                float  _VeinWidth, _SideVeins, _SideVeinGain;
                float  _FoldStrength;
                float4 _FoldColor;
                float  _RimGain;
                float4 _RimColor;
                float  _Notch, _NotchScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            float hash11(float x)
            {
                return frac(sin(x * 127.1) * 43758.5453);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;

                // ── ОСЬ ЛИСТА ──
                // Изгиб: центральная линия уезжает вбок по синусу. Прямой лист
                // выглядит перышком, изогнутый — настоящим.
                float y = saturate(uv.y);
                float bend = sin(y * 3.14159) * _Curve;
                float cx = 0.5 + bend;

                // ── СИЛУЭТ ──
                // Полуширина по синусу в степени: показатель управляет тем,
                // насколько остры концы. При 1 получается ровный овал, ниже —
                // заострённый лист, выше — почти круглый.
                float halfW = pow(sin(y * 3.14159), _Sharp) * _Width * 0.5;

                // Рваный край: мелкие зазубрины по контуру. Без них лист
                // выглядит пластиковым, с ними — живым.
                float notch = (hash11(floor(y * _NotchScale)) - 0.5) * _Notch * 0.08;
                halfW += notch;

                float dx = abs(uv.x - cx);
                float alpha = 1.0 - smoothstep(halfW - _EdgeSoft, halfW + _EdgeSoft, dx);

                // Черенок: короткий хвостик снизу, иначе лист висит в воздухе
                // обрубком.
                float stem = (1.0 - smoothstep(0.006, 0.014, abs(uv.x - cx)))
                             * (1.0 - smoothstep(0.0, 0.12, y));
                alpha = saturate(alpha + stem * 0.9);

                clip(alpha - 0.02);

                // ── ЦВЕТ ──
                // Основа приходит из Start Color системы частиц: палитра
                // живёт там, шейдер про неё ничего не знает.
                float3 col = i.color.rgb;

                // ── СГИБ ──
                // Одна половина листа темнее другой. Это не освещение, а
                // рисованная условность: именно она превращает плоский квад в
                // «перевернувшийся лист», когда частица вращается.
                float side = saturate((uv.x - cx) / max(0.001, halfW) * 0.5 + 0.5);
                col = lerp(_FoldColor.rgb * col, col, smoothstep(0.35, 0.75, side));
                col = lerp(i.color.rgb, col, _FoldStrength);

                // ── ПРОЖИЛКИ ──
                float vein = 1.0 - smoothstep(_VeinWidth * 0.5, _VeinWidth * 0.5 + 0.004, dx);
                col = lerp(col, _VeinColor.rgb, vein * _VeinColor.a);

                // Боковые прожилки: наклонные линии от центра к краю. Считаются
                // одной операцией frac — дешевле, чем любая текстура.
                if (_SideVeins > 0.5)
                {
                    float t = frac((y * _SideVeins) + (uv.x - cx) * 2.2);
                    float side_v = 1.0 - smoothstep(0.0, 0.12, abs(t - 0.5));
                    col = lerp(col, _VeinColor.rgb, side_v * _SideVeinGain * 0.35 * alpha);
                }

                // ── ПОДСВЕТКА КРАЯ ──
                // Тонкая светлая кайма по контуру. Лист тонкий и просвечивает
                // на солнце именно по краю.
                float rim = smoothstep(halfW - _EdgeSoft * 3.0, halfW, dx);
                col += _RimColor.rgb * rim * _RimGain * alpha;

                return half4(col, alpha * i.color.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
