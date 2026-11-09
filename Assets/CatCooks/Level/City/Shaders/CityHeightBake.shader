// CatCooks — служебный: пишет высоту точки (мир Y) в карту высот города. Рисует CityHeightShadows при запекании.
Shader "Hidden/CatCooks/City/HeightBake"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "HeightBake"
            ZWrite On ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float y : TEXCOORD0; };
            V vert(A v)
            {
                V o;
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(p);
                o.y = p.y;
                return o;
            }
            float4 frag(V i) : SV_Target { return float4(i.y, 0, 0, 1); }
            ENDHLSL
        }
    }
    Fallback Off
}
