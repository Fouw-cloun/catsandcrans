Shader "Custom/WaterPuddleURP"
{
    // ЛУЖА v3 — рваные УНИКАЛЬНЫЕ края (выкройка по шуму) + пена + кольца ряби.
    // Рисуется ПОВЕРХ земли (Offset), не проваливается, ZWrite off.
    Properties
    {
        [Header(Colors)]
        _WaterColor ("Вода", Color) = (0.12, 0.5, 0.95, 0.6)
        [HDR]_FoamColor ("Пена (край и хлопья)", Color) = (2.5, 3.0, 3.2, 1)

        [Header(Noise)]
        [NoScaleOffset]_Noise ("Шум (хлопья/капли)", 2D) = "white" {}
        _NoiseTiling ("Тайлинг хлопьев", Float) = 3
        _FlakeCutoff ("Порог хлопьев", Range(0.2, 0.95)) = 0.62

        [Header(Torn Edge)]
        [NoScaleOffset]_ShapeNoise ("Шум ФОРМЫ (выкройка края)", 2D) = "white" {}
        _ShapeTiling ("Тайлинг формы", Float) = 1.3
        _ShapeSeed ("Семя формы (уникальность)", Vector) = (0,0,0,0)
        _EdgeTear ("Рваность края", Range(0, 0.6)) = 0.3

        [Header(Ripples)]
        _RippleSpots ("Сколько ОЧАГОВ ряби (в разных местах)", Range(1, 6)) = 3
        _RippleRings ("Колец в одном очаге", Range(1, 5)) = 2
        _RippleSpeed ("Скорость расхождения", Float) = 0.5
        _RippleSharp ("Резкость колец", Range(2, 40)) = 14
        _RippleScale ("Размер очага ряби", Range(0.5, 4)) = 2.0
        _RippleWobble ("Кривизна колец (0=ровные)", Range(0, 0.5)) = 0.18

        [Header(Edge)]
        _EdgeFoamWidth ("Ширина пены по краю", Range(0.02, 0.4)) = 0.12
        _EdgeSoftness ("Мягкость края", Range(0.01, 0.5)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            // Рисуем чуть "ближе" камеры — лужа всегда поверх земли, без z-fight.
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 posOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 posCS : SV_POSITION; float2 uv : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _WaterColor, _FoamColor;
                float _NoiseTiling, _FlakeCutoff;
                float4 _ShapeSeed;
                float _ShapeTiling, _EdgeTear;
                float _RippleSpots, _RippleRings, _RippleSpeed, _RippleSharp, _RippleScale, _RippleWobble;
                float _EdgeFoamWidth, _EdgeSoftness;
            CBUFFER_END

            TEXTURE2D(_Noise);      SAMPLER(sampler_Noise);
            TEXTURE2D(_ShapeNoise); SAMPLER(sampler_ShapeNoise);

            V vert(A IN)
            {
                V OUT;
                OUT.posCS = TransformObjectToHClip(IN.posOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(V IN) : SV_Target
            {
                float2 c = IN.uv - 0.5;
                float dist = length(c) * 2.0;   // 0 центр .. 1 край
                float t = _Time.y;

                // ── ВЫКРОЙКА КРАЯ: сдвигаем радиус края шумом формы. Семя
                //    (_ShapeSeed) уникально у каждой лужи → все разные формы.
                float shape = SAMPLE_TEXTURE2D(_ShapeNoise, sampler_ShapeNoise,
                    IN.uv * _ShapeTiling + _ShapeSeed.xy).r;
                float tornDist = dist + (shape - 0.5) * 2.0 * _EdgeTear;

                // Хлопья пены на поверхности.
                float n = SAMPLE_TEXTURE2D(_Noise, sampler_Noise,
                    IN.uv * _NoiseTiling + _ShapeSeed.xy + t * 0.05).r;
                float flakes = smoothstep(_FlakeCutoff, _FlakeCutoff + 0.08, n);

                // ── РЯБЬ: несколько ОЧАГОВ в РАЗНЫХ местах лужи, не по центру.
                //    Каждый очаг — свои кольца от своей точки, слегка кривые.
                float ring = 0.0;
                int spots = (int)round(_RippleSpots);
                for (int ri = 0; ri < spots; ri++)
                {
                    // Псевдослучайная точка очага внутри лужи (стабильная по seed).
                    float fi = (float)ri + _ShapeSeed.x * 0.37 + _ShapeSeed.y * 0.19;
                    float2 spot = float2(
                        frac(sin(fi * 12.9898) * 43758.5453),
                        frac(sin(fi * 78.233)  * 12345.6789)) - 0.5; // -0.5..0.5
                    spot *= 0.9; // держим внутри лужи

                    float2 rel = (IN.uv - 0.5) - spot;
                    // Кривизна: искажаем радиус шумом формы (кольца не идеальные).
                    float wob = (SAMPLE_TEXTURE2D(_ShapeNoise, sampler_ShapeNoise,
                                 IN.uv * 1.7 + fi).r - 0.5) * _RippleWobble;
                    float rd = length(rel) * _RippleScale + wob;

                    float rr = sin((rd * _RippleRings - t * _RippleSpeed + fi) * 6.2831853);
                    rr = pow(saturate(rr), _RippleSharp);
                    rr *= saturate(1.0 - rd);        // затухает от очага
                    ring = max(ring, rr);            // очаги накладываются, не гасят
                }
                ring *= (1.0 - dist * 0.6) * saturate(n + 0.4);

                // Пена по (рваному) краю.
                float edgeFoam = smoothstep(1.0 - _EdgeFoamWidth - 0.05, 1.0 - 0.02, tornDist);

                // Мягкое исчезание по РВАНОМУ радиусу — уникальная форма лужи.
                float edge = saturate((1.0 - tornDist) / _EdgeSoftness);

                float foamMask = saturate(flakes * 0.8 + ring + edgeFoam);
                float3 col = lerp(_WaterColor.rgb, _FoamColor.rgb, foamMask);
                float a = max(_WaterColor.a, foamMask) * edge;

                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
