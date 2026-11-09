Shader "CatCooks/Thermometer"
{
    Properties
    {
        [Header(Fill)]
        _Fill ("Fill 0..1", Range(0,1)) = 0.0
        _FillAxis ("Fill Axis (0=X 1=Y 2=Z)", Float) = 2
        _MinBound ("Min Bound (object space)", Float) = 0.0
        _MaxBound ("Max Bound (object space)", Float) = 0.172

        [Header(Gradient)]
        _ColdColor ("Cold", Color) = (0.25, 0.55, 1, 1)
        _WarmColor ("Warm", Color) = (1, 0.8, 0.2, 1)
        _HotColor  ("Hot",  Color) = (1, 0.2, 0.08, 1)
        _WarmAt ("Warm At", Range(0,1)) = 0.5

        [Header(Empty part)]
        _EmptyColor ("Empty", Color) = (0.12, 0.13, 0.16, 1)
        _EdgeSoft ("Edge Softness", Range(0.001, 0.2)) = 0.02

        [Header(Target band)]
        _BandLow ("Band Low", Range(0,1)) = 0.4
        _BandHigh ("Band High", Range(0,1)) = 0.6
        _BandColor ("Band Color", Color) = (0.3, 1, 0.4, 1)
        _BandStrength ("Band Strength", Range(0,1)) = 0.0

        [Header(Glow)]
        _Emission ("Emission", Range(0,4)) = 1.2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _Fill;
                float  _FillAxis;
                float  _MinBound;
                float  _MaxBound;
                float4 _ColdColor;
                float4 _WarmColor;
                float4 _HotColor;
                float  _WarmAt;
                float4 _EmptyColor;
                float  _EdgeSoft;
                float  _BandLow;
                float  _BandHigh;
                float4 _BandColor;
                float  _BandStrength;
                float  _Emission;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float  along      : TEXCOORD1;   // 0..1 вдоль оси столбика
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);

                // Позиция вдоль выбранной оси В ПРОСТРАНСТВЕ ОБЪЕКТА.
                // Именно объекта, а не мира: градусник могут наклонять и
                // разворачивать к камере, а заливка должна оставаться на месте.
                float p = IN.positionOS.z;
                if (_FillAxis < 0.5) p = IN.positionOS.x;
                else if (_FillAxis < 1.5) p = IN.positionOS.y;

                OUT.along = saturate((p - _MinBound) / max(0.0001, _MaxBound - _MinBound));
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // Градиент по высоте: снизу холодно, сверху раскалено.
                // Две ступени, а не одна: без промежуточного тёплого цвета
                // синий переходит в красный через грязный фиолетовый.
                half3 grad = IN.along < _WarmAt
                    ? lerp(_ColdColor.rgb, _WarmColor.rgb, IN.along / max(0.0001, _WarmAt))
                    : lerp(_WarmColor.rgb, _HotColor.rgb,
                           (IN.along - _WarmAt) / max(0.0001, 1.0 - _WarmAt));

                // Граница заливки мягкая: резкий край выглядит как ошибка,
                // а не как уровень жидкости.
                half filled = smoothstep(_Fill + _EdgeSoft, _Fill - _EdgeSoft, IN.along);
                half3 col = lerp(_EmptyColor.rgb, grad, filled);

                // Целевой коридор подсвечивается прямо на шкале — игроку не
                // приходится запоминать числа, он целится по картинке.
                half band = step(_BandLow, IN.along) * step(IN.along, _BandHigh);
                col = lerp(col, _BandColor.rgb, band * _BandStrength);

                // Простое затенение по нормали, чтобы цилиндр читался объёмным.
                half ndl = saturate(dot(normalize(IN.normalWS), normalize(float3(0.4, 1, 0.3))));
                col *= lerp(0.65, 1.0, ndl);

                // Заполненная часть светится: раскалённая ртуть должна
                // выделяться в темноте кухни.
                col += grad * (_Emission * filled * IN.along * 0.4);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
