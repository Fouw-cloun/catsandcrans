// =============================================================
//  CatCooks_SplashParticle.shader
//
//  Крошечный шейдер для брызг дождя.
//
//  ЗАЧЕМ СВОЙ, А НЕ ВСТРОЕННЫЙ
//   У URP-шейдера частиц прозрачность включается КЛЮЧЕВЫМИ СЛОВАМИ и
//   состоянием прохода, а не полями материала. Выставить _Surface = 1
//   из кода недостаточно: режим смешивания, запись глубины и очередь
//   остаются непрозрачными — частицы выходят белыми квадратами с
//   игнорируемой альфой. Ровно это и было видно.
//
//   Здесь прозрачность зашита в сам проход, настраивать нечего и
//   сломать нечем.
//
//  ВОДА, А НЕ БУМАГА. Форма кольца приходит из текстуры, но цвет тут
//  не плоский: к краю добавляется подсветка, потому что тонкая плёнка
//  воды на просвет всегда ярче своей середины. Без этого кольцо
//  читается как наклейка.
//
//  Цвет берётся из ЦВЕТА ЧАСТИЦЫ, поэтому один материал обслуживает и
//  кольца, и брызги, и любые их оттенки от погоды.
// =============================================================
Shader "CatCooks/Splash Particle"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Shape", 2D) = "white" {}
        _Tint ("Tint", Color) = (0.85, 0.92, 1, 1)

        // Подсветка кромки: доля, на которую край ярче середины.
        _EdgeGlow ("Edge Glow", Range(0, 2)) = 0.7

        // Общая яркость. Вода отражает небо, поэтому под грозой брызги
        // тусклее, а в солнечный ливень вспыхивают.
        _Brightness ("Brightness", Range(0, 3)) = 1.15
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "Transparent"
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector"= "True"
            "PreviewType"    = "Plane"
        }

        Pass
        {
            Name "CatCooksSplash"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float  _EdgeGlow, _Brightness;
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
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                // Плёнка воды на просвет ярче в тонком месте. Берём это
                // от самой альфы: где её меньше, там подсветка сильнее.
                half thin = saturate(1.0 - tex.a);
                half3 col = _Tint.rgb * i.color.rgb
                          * (1.0 + thin * _EdgeGlow) * _Brightness;

                half a = tex.a * i.color.a * _Tint.a;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
