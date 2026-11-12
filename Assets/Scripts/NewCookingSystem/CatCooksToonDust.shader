// ============================================================================
//  CatCooks / Toon Dust   (v18)
//
//  МУЛЬТЯШНАЯ ПЫЛЬ: клубки-«пуфы» (меш-частицы), матовые, чуть прозрачные.
//    • две ступеньки света: светлая сторона и мягкая цветная тень — форма читается, как в мультике;
//    • без бликов и «металла»; к краю силуэта чуть прозрачнее — клубок мягкий, но форма устойчивая;
//    • цвет и прозрачность — из цвета частицы (цвет продукта). В Linear-проекте — из sRGB.
// ============================================================================
Shader "CatCooks/Toon Dust"
{
    Properties
    {
        _Opacity ("Непрозрачность", Range(0, 1)) = 0.82
        _EdgeSoft ("Мягкий край (прозрачнее к силуэту)", Range(0, 1)) = 0.35
        _ShadowTint ("Цвет тени", Color) = (0.78, 0.68, 0.66, 1)
        _ShadowAmt ("Глубина тени", Range(0, 1)) = 0.32
        _Lighten ("Пыль светлее продукта", Range(0, 1)) = 0.18
        _Step ("Граница света", Range(-0.5, 0.8)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "ToonDust"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Opacity, _EdgeSoft, _ShadowAmt, _Lighten, _Step;
                half4 _ShadowTint;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; half4 color : COLOR; };

            V vert(A v)
            {
                V o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                half3 alb = i.color.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    alb = SRGBToLinear(alb);
                #endif
                alb = lerp(alb, 1.0, _Lighten * 0.5);
                float3 n = normalize(i.normalWS);
                Light L = GetMainLight();
                half ndl = dot(n, L.direction);
                half lit = smoothstep(_Step - 0.06, _Step + 0.06, ndl);                 // ступенька, а не градиент
                half3 shade = lerp(lerp(1.0, _ShadowTint.rgb, _ShadowAmt * 1.6), 1.0, lit);
                half3 amb = SampleSH(half3(0, 1, 0));
                half3 col = alb * shade * saturate(L.color * 0.75 + amb * 0.45);
                float3 vdir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half edge = 1.0 - saturate(dot(n, vdir));
                half a = i.color.a * _Opacity * (1.0 - _EdgeSoft * smoothstep(0.5, 1.0, edge));
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
