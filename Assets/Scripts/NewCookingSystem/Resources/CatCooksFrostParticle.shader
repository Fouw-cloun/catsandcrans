// v19.5: CatCooks / Frost Particle — простая частица-картинка (снежинка) с прозрачностью и цветом частицы.
// Лежит в Resources — попадает в сборку сама. Не зависит от «Sprites/Default» (в URP-проекте его может не быть —
// тогда частицы рисовались непрозрачными квадратиками).
Shader "CatCooks/Frost Particle"
{
    Properties
    {
        _MainTex ("Картинка", 2D) = "white" {}
        _Boost ("Яркость", Range(0.5, 3)) = 1.3
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Name "Frost"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _Boost;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            V vert(A v)
            {
                V o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half4 c = t * i.color;
                c.rgb *= _Boost;
                return c;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
