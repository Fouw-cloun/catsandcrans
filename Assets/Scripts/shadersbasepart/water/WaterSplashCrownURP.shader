Shader "Custom/WaterSplashCrownURP"
{
    // ═══════════════════════════════════════════════════════════════════
    // КОРОНА УДАРА — «вода поднимается вокруг струи» (как на референсах:
    // бурлящее кольцо в точке падения).
    //
    // Меш: открытый ЦИЛИНДР-юбка (труба без крышек), UV.y: 0 низ → 1 верх.
    // Шейдер: вертексы ДЫШАТ (верх кольца ходит вверх-вниз по шуму), верхний
    // край РВАНЫЙ (растворяется по шуму — брызги-язычки), низ плотный.
    // Ставится скриптом WaterJet в точку удара.
    // ═══════════════════════════════════════════════════════════════════
    Properties
    {
        [Header(Colors)]
        _BodyColor ("Тело воды", Color) = (0.15, 0.55, 0.95, 0.85)
        [HDR]_FoamColor ("Пена/верх", Color) = (3.0, 3.6, 4.0, 1)

        [Header(Noise)]
        [NoScaleOffset]_Noise ("Шум", 2D) = "white" {}
        _NoiseTiling ("Тайлинг (X вокруг, Y ввысь)", Vector) = (3, 1, 0, 0)
        _ScrollSpeed ("Скорость бурления", Float) = 1.5

        [Header(Shape)]
        _WaveHeight ("Высота дыхания верха, м", Range(0, 0.6)) = 0.18
        _TearCutoff ("Рваность верхнего края", Range(0.1, 0.9)) = 0.45
        _FoamBand ("Полоса пены сверху", Range(0.05, 0.9)) = 0.45

        _Intensity ("Напор 0..1 (ставит скрипт)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 posOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 posCS : SV_POSITION; float2 uv : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _BodyColor, _FoamColor;
                float4 _NoiseTiling;
                float _ScrollSpeed, _WaveHeight, _TearCutoff, _FoamBand, _Intensity;
            CBUFFER_END

            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

            V vert(A IN)
            {
                V OUT;
                float t = _Time.y * _ScrollSpeed;

                // ДЫШАЩИЙ ВЕРХ: смещаем вертексы вверх по шуму. Чем выше вертекс
                // (uv.y), тем сильнее гуляет — низ прибит к земле.
                float wobble = SAMPLE_TEXTURE2D_LOD(_Noise, sampler_Noise,
                    float2(IN.uv.x * _NoiseTiling.x + t * 0.35, t * 0.2), 0).r;
                float lift = (wobble - 0.5) * 2.0 * _WaveHeight * IN.uv.y * _Intensity;

                float3 pos = IN.posOS.xyz;
                pos.y += lift;

                OUT.posCS = TransformObjectToHClip(pos);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(V IN) : SV_Target
            {
                float t = _Time.y * _ScrollSpeed;

                // Бурлящий шум: крутится вокруг кольца + ползёт вверх.
                float n = SAMPLE_TEXTURE2D(_Noise, sampler_Noise,
                    IN.uv * _NoiseTiling.xy + float2(t * 0.4, -t * 0.9)).r;

                // РВАНЫЙ ВЕРХ: чем выше, тем жёстче порог по шуму — верхний край
                // распадается на язычки-брызги.
                float tearThreshold = lerp(0.0, _TearCutoff + 0.4, IN.uv.y);
                float body = smoothstep(tearThreshold, tearThreshold + 0.12, n + 0.35);

                // Пена: верхняя полоса кольца — яркая белая.
                float foam = smoothstep(1.0 - _FoamBand, 1.0, IN.uv.y + (n - 0.5) * 0.3);

                float3 col = lerp(_BodyColor.rgb, _FoamColor.rgb, foam);
                float a = _BodyColor.a * body * _Intensity;
                a = max(a, foam * body * _Intensity);

                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
