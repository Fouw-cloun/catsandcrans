// v22: CatCooks / Build Ghost — призрачный шаблон постройки: полупрозрачный цвет, светлый край по силуэту,
// бегущие полосы «чертежа». Без света и теней, не пишет глубину. Лежит в Resources — попадает в сборку сама.
Shader "CatCooks/Build Ghost"
{
    Properties
    {
        _Color ("Цвет", Color) = (0.45, 0.8, 1, 0.28)
        _Rim ("Светлый край", Range(0, 2)) = 1
        _Stripes ("Полосы чертежа (на метр)", Float) = 30
        _StripeAlpha ("Яркость полос", Range(0, 1)) = 0.25
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Name "Ghost"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Rim, _StripeAlpha;
                float _Stripes;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };
            V vert(A v)
            {
                V o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 vd = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1.0h - saturate(abs(dot(n, vd))), 2.0h) * _Rim;
                half stripe = step(0.5h, frac((i.positionWS.y + i.positionWS.x * 0.3) * _Stripes - _Time.y * 0.6)) * _StripeAlpha;
                half4 c = _Color;
                c.rgb += rim * 0.6h + stripe * 0.3h;
                c.a = saturate(c.a + rim * 0.5h + stripe * c.a);
                return c;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
