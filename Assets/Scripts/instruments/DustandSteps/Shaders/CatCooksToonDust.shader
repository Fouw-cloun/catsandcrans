Shader "CatCooks/Dust and Smoke/Toon Puff"
{
    Properties
    {
        _Softness ("Band softness", Range(0,1)) = .22
        _Opacity ("Global opacity", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent"
            "Queue"="Transparent+10" "DisableBatching"="True" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend One OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CatCooksPuffStyle.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Softness, _Opacity;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 colour : COLOR;
                float4 data : TEXCOORD1; // phase, age, erosion, grain
                float3 shape : TEXCOORD2;
                float4 style : TEXCOORD3;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 colour : COLOR;
                float4 data : TEXCOORD2;
                float3 shape : TEXCOORD3;
                float fog : TEXCOORD4;
                float4 style : TEXCOORD5;
            };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.style = v.style; o.colour = v.colour; o.data = v.data; o.shape = v.shape;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 normal = normalize(i.normalWS);
                float3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float facing = saturate(dot(normal,view));
                float3 colour = CCPColour(saturate(i.colour.rgb),normal,view,_Softness);
                if (i.data.w < .5) colour = CCPPattern(colour,i.shape,i.data.x,i.style);
                float alpha = saturate(i.colour.a) * saturate(_Opacity);
                if (i.data.w < .5)
                    alpha *= smoothstep(.0,.17,facing) * CCPDissolve(i.shape,i.data.x,i.data.y,i.data.z);
                alpha *= smoothstep(.06,.28,distance(GetCameraPositionWS(),i.positionWS));
                colour = MixFog(colour,i.fog);
                return half4(colour * alpha,alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
