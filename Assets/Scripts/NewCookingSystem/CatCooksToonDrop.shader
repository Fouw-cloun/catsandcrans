// ============================================================================
//  CatCooks / Toon Drop
//
//  Мультяшная частица жидкости: капля, клуб пены, клякса на полу.
//  Текстура — три канала-маски (их генерирует LiquidFx):
//    R — форма (альфа), G — заливка (0 — тёмная обводка, 1 — тело),
//    B — блик (белый «боб»).
//  Цвет — цвет частицы (молоко, сливки, вода). Обводка — тот же цвет, темнее.
//  Offset — для клякс на полу (не мерцают с полом).
//  v5: G — свет на теле (0.1 обводка, 0.3..1 тень→свет): глянцевая капля
//      с холодной тенью и мягкой «мультяшной» ступенькой; у камеры частица тает.
// ============================================================================
Shader "CatCooks/Toon Drop"
{
    Properties
    {
        _MainTex ("Маски (R форма, G заливка, B блик)", 2D) = "white" {}
        _OutlineDark ("Обводка: насколько темнее цвета", Range(0, 1)) = 0.56
        _OutlineTint ("Обводка: оттенок (v10: холодный, не коричневый)", Color) = (0.8, 0.86, 0.98, 1)
        _ShadeDark ("Тень: насколько темнее", Range(0, 1)) = 0.8
        _ShadowTint ("Тень: оттенок (холодный)", Color) = (0.78, 0.84, 0.96, 1)
        _Toon ("Мультяшность (ступенька света)", Range(0, 1)) = 0.55
        _RampMid ("Ступенька: где", Range(0, 1)) = 0.5
        _RampSoft ("Ступенька: мягкость", Range(0.01, 0.5)) = 0.12
        _BodyDim ("Тело темнее блика", Range(0.7, 1)) = 0.93
        _Highlight ("Сила блика", Range(0, 1.5)) = 1
        _Brighten ("Осветлить тело", Range(0, 0.5)) = 0.03
        _NearFade ("Тает у камеры: от, до (м)", Vector) = (0.3, 0.85, 0, 0)
        _Tint ("Оттенок и прозрачность (скрипт, карточки всплеска)", Color) = (1, 1, 1, 1)
        [HideInInspector] _OffsetFactor ("", Float) = 0
        [HideInInspector] _OffsetUnits ("", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Offset [_OffsetFactor], [_OffsetUnits]

        Pass
        {
            Name "ToonDrop"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _OutlineDark; half4 _OutlineTint; half _ShadeDark, _Highlight, _Brighten;
                half4 _ShadowTint; half _Toon, _RampMid, _RampSoft, _BodyDim; float4 _NearFade; half4 _Tint;
                float _OffsetFactor, _OffsetUnits;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; half fog : TEXCOORD1; half nearK : TEXCOORD2; };

            V vert(A v)
            {
                V o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                float dist = length(_WorldSpaceCameraPos - ws);
                o.nearK = saturate((dist - _NearFade.x) / max(1e-3, _NearFade.y - _NearFade.x));
                o.color = v.color * _Tint;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                half4 m = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half3 body = saturate(i.color.rgb * (1.0h + _Brighten) + _Brighten * 0.5h) * _BodyDim;
                // свет на теле: плавный объём + мультяшная ступенька
                half t = saturate((m.g - 0.3h) / 0.7h);
                half step1 = smoothstep(_RampMid - _RampSoft, _RampMid + _RampSoft, t);
                half litK = lerp(t, step1, _Toon);
                half3 dark = body * _ShadowTint.rgb * _ShadeDark;
                half3 col = lerp(dark, body, litK);
                // тонкая обводка — тот же цвет, темнее
                half outlineK = 1.0h - smoothstep(0.16h, 0.26h, m.g);
                half3 outline = body * _OutlineDark * _OutlineTint.rgb * 1.6h;
                col = lerp(col, outline, outlineK);
                col = lerp(col, half3(1, 1, 1), saturate(m.b * _Highlight));
                col = MixFog(col, i.fog);
                return half4(col, m.r * i.color.a * i.nearK);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
