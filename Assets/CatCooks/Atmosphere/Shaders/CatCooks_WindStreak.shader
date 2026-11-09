// =============================================================
//  CatCooks_WindStreak.shader
//  Мультяшные линии ветра: тонкая кривая, уходящая в завиток.
//  Геометрия — лента, которую строит CatCooksWindStreaks по кривой
//  с петлёй на конце.
//
//  ПОЧЕМУ ЛЕНТА, А НЕ РИСОВАНИЕ В ШЕЙДЕРЕ
//   Петлю можно нарисовать и в фрагменте, через расстояние до
//   параметрической спирали, но это дорого и не даёт управлять
//   толщиной по длине. Лента строится один раз при старте, дальше
//   почти бесплатна, а весь характер задаётся геометрией.
//
//  КЛЮЧЕВОЙ ПРИЁМ — ПРОРИСОВКА. Штрих не появляется целиком: он
//  вычерчивается от головы к хвосту и так же стирается. Именно это
//  читается как рисованная анимация, а не как «пролетела текстурка».
//  За это отвечает _Head: положение пера вдоль штриха.
//
//  u — вдоль штриха (0 хвост … 1 голова), v — поперёк (0…1).
//
//  URP 17 / Unity 6.
// =============================================================
Shader "CatCooks/Wind Streak"
{
    Properties
    {
        [Header(Look)]
        _Color    ("Color", Color) = (1, 1, 1, 1)
        _Opacity  ("Opacity", Range(0, 1)) = 0.8
        _Softness ("Edge Softness", Range(0.01, 1)) = 0.5

        [Header(Stroke)]
        _Head     ("Pen Position", Range(-0.5, 1.5)) = 1
        _TrailLen ("Trail Length", Range(0.05, 1.5)) = 0.55
        _TipFade  ("Tail Fade", Range(0.01, 1)) = 0.4

        [Header(Fades)]
        _Fade       ("Global Fade", Range(0, 1)) = 1
        _CameraFade ("Camera Fade Distance", Range(0.1, 30)) = 3
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "CatCooksWindStreak"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Per-streak через MaterialPropertyBlock. Вне CBUFFER.
            float _Head;
            float _Fade;

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Opacity, _Softness;
                float  _TrailLen, _TipFade;
                float  _CameraFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float  camDist    : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = v.uv;
                o.camDist = distance(GetCameraPositionWS(), posWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Поперёк штриха: мягкая линия, а не жёсткая полоса.
                // ВНИМАНИЕ: переменную нельзя назвать line — это
                // зарезервированное слово HLSL (тип примитива для
                // геометрических шейдеров). Компилятор споткнётся.
                float across = abs(i.uv.y - 0.5) * 2.0;
                float stroke = 1.0 - smoothstep(1.0 - _Softness, 1.0, across);

                // Плотное ядро по центру ленты. Одна мягкая линия на фоне
                // неба размывается до невидимости — нужна яркая сердцевина
                // с мягкой обводкой вокруг, как у росчерка кистью.
                stroke += (1.0 - smoothstep(0.0, 0.45, across)) * 0.55;
                stroke = saturate(stroke);

                // ПРОРИСОВКА. Перо стоит в _Head, за ним тянется хвост
                // длиной _TrailLen. Всё, что впереди пера, ещё не нарисовано.
                float behind = _Head - i.uv.x;
                float drawn = smoothstep(0.0, 0.04, behind)
                            * (1.0 - smoothstep(_TrailLen * 0.55, _TrailLen, behind));

                // Хвост штриха истончается — как настоящий росчерк пером.
                float taper = smoothstep(0.0, _TipFade, i.uv.x);

                float a = stroke * drawn * taper * _Opacity * _Fade;

                // Не бьём в лицо при подлёте вплотную.
                a *= saturate(i.camDist / _CameraFade);

                if (a <= 0.003) discard;
                return half4(_Color.rgb, saturate(a));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
