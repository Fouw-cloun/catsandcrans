Shader "Custom/WaterDropletURP"
{
    // Капли воды для частиц: круглая капля с ярким БЛИКОМ-глазом и голубым
    // ореолом, мягкий край, HDR-свечение (ловит Bloom). Additive.
    // Работает с Particle System (читает vertex color = Start Color частицы).
    Properties
    {
        [HDR]_Color ("Цвет (HDR)", Color) = (1.5, 2.8, 3.5, 1)
        [HDR]_CoreColor ("Блик (центр)", Color) = (3.5, 4.0, 4.2, 1)
        _CoreSize ("Размер блика", Range(0.02, 0.5)) = 0.18
        _Softness ("Мягкость края", Range(0.01, 0.6)) = 0.35
        _Stretch ("Вытянутость капли (0=круг)", Range(0, 0.6)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Blend SrcAlpha One      // Additive — капли светятся
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A
            {
                float4 posOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;     // цвет частицы из Particle System
            };
            struct V
            {
                float4 posCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _CoreColor;
                float _CoreSize, _Softness, _Stretch;
            CBUFFER_END

            V vert(A IN)
            {
                V OUT;
                OUT.posCS = TransformObjectToHClip(IN.posOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(V IN) : SV_Target
            {
                float2 c = IN.uv - 0.5;
                // Вытянутость (капля-слеза): сжимаем по Y.
                c.y *= (1.0 + _Stretch * 2.0);
                float d = length(c) * 2.0;      // 0 центр .. 1 край

                // Тело капли: мягкий круг.
                float body = saturate((1.0 - d) / _Softness);
                // Яркий блик-глаз в центре.
                float core = saturate((_CoreSize - d) / _CoreSize);
                core = pow(core, 2.0);

                float3 col = _Color.rgb * body + _CoreColor.rgb * core;
                col *= IN.color.rgb;            // умножаем на цвет частицы

                float a = saturate(body + core) * IN.color.a;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
