Shader "CatCooks/Food Lab/Toon Mass"
{
    Properties
    {
        _BaseColor("Tint",Color)=(1,1,1,1)
        _ShadeColor("Soft shadow tint",Color)=(0.52,0.43,0.34,1)
        _Specular("Broad highlight",Range(0,1))=.24
        _GrainScale("Food grain / metre",Float)=120
        _GrainAmount("Grain strength",Range(0,.25))=.06
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor, _ShadeColor;
        float _Specular, _GrainScale, _GrainAmount;
        CBUFFER_END
        struct Attributes {float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 color:COLOR; float2 props:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID};
        struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float3 normalWS:TEXCOORD1;float4 color:COLOR;float2 props:TEXCOORD2;float fog:TEXCOORD3;UNITY_VERTEX_OUTPUT_STEREO};
        Varyings Vert(Attributes i)
        {
            Varyings o=(Varyings)0;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            VertexPositionInputs p=GetVertexPositionInputs(i.positionOS.xyz);
            o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.normalWS=TransformObjectToWorldNormal(i.normalOS);
            o.color=i.color*_BaseColor;o.props=i.props;o.fog=ComputeFogFactor(p.positionCS.z);return o;
        }
        float Hash3(float3 p){return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453);}
        half4 Frag(Varyings i):SV_Target
        {
            float3 n=normalize(i.normalWS),v=GetWorldSpaceNormalizeViewDir(i.positionWS);
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
            float4 shadowCoord=ComputeScreenPos(TransformWorldToHClip(i.positionWS));
            #else
            float4 shadowCoord=TransformWorldToShadowCoord(i.positionWS);
            #endif
            Light light=GetMainLight(shadowCoord);
            float ndl=dot(n,light.direction);float bands=.30+.42*smoothstep(-.15,.08,ndl)+.28*smoothstep(.48,.68,ndl);
            float shadow=lerp(.3,1,light.shadowAttenuation);float lit=bands*shadow;
            float3 diffuse=i.color.rgb*lerp(_ShadeColor.rgb,1,lit);
            float3 ambient=max(SampleSH(n),0)*.65;
            float3 col=diffuse*(ambient+light.color*lit*.85);
            float grain=Hash3(floor(i.positionWS*_GrainScale));col*=1+(grain-.5)*_GrainAmount;
            float3 halfVector=SafeNormalize(light.direction+v);
            float shine=pow(saturate(dot(n,halfVector)),lerp(70,12,i.props.x));
            col+=smoothstep(.35,.72,shine)*_Specular*(1-i.props.x*.65)*light.color*light.shadowAttenuation;
            return half4(MixFog(col,i.fog),1);
        }
        half4 DepthFrag(Varyings i):SV_Target{return 0;}
        float3 _LightDirection, _LightPosition;
        Varyings ShadowVert(Attributes i)
        {
            Varyings o=Vert(i);
            float3 direction=_LightDirection;
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
            direction=normalize(_LightPosition-o.positionWS);
            #endif
            o.positionCS=TransformWorldToHClip(ApplyShadowBias(o.positionWS,normalize(o.normalWS),direction));
            #if UNITY_REVERSED_Z
            o.positionCS.z=min(o.positionCS.z,o.positionCS.w*UNITY_NEAR_CLIP_VALUE);
            #else
            o.positionCS.z=max(o.positionCS.z,o.positionCS.w*UNITY_NEAR_CLIP_VALUE);
            #endif
            return o;
        }
        ENDHLSL
        Pass
        {
            Name "Forward" Tags {"LightMode"="UniversalForwardOnly"}
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"}
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags {"LightMode"="DepthOnly"}
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    FallBack Off
}
