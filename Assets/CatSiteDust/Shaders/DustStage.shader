Shader "CatSite/Dust/Demo Stage"
{
    Properties { _BaseColor("Color",Color)=(.5,.5,.5,1) }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
        Pass
        {
            Tags {"LightMode"="UniversalForwardOnly"}
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            CBUFFER_END
            struct A {float4 p:POSITION;float3 n:NORMAL;};
            struct V {float4 p:SV_POSITION;half3 n:TEXCOORD0;};
            V Vert(A v){V o;o.p=TransformObjectToHClip(v.p.xyz);o.n=TransformObjectToWorldNormal(v.n);return o;}
            half4 Frag(V i):SV_Target{return half4(_BaseColor.rgb*lerp(.68,1,saturate(dot(normalize(i.n),normalize(float3(-.4,.8,-.3)))*.5+.5)),1);}
            ENDHLSL
        }
    }
}
