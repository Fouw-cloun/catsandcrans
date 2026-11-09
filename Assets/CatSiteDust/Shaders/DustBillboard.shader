Shader "CatSite/Dust/Toon Billboard URP"
{
    Properties
    {
        _AgeTint("Late lifetime tint",Color)=(.9,.93,.98,1)
        _Variation("Per puff shade variation",Range(0,.3))=.12
        _EdgeSoftness("Soft edge width",Range(.01,.3))=.07
        _DensityShading("Internal density shading",Range(0,.4))=.14
        _ColorGain("Overall brightness",Range(.1,1.5))=.8
        _SwirlStrength("Swirl distortion",Range(0,.5))=.22
        _SceneLight("Scene light influence",Range(0,1))=.3
        _PuffMap("2x2 puff data: R density G shade B erosion",2D)="white"{}
        _ShadeTint("Shadow tint",Color)=(.72,.64,.52,1)
        _ErodeStart("Erosion start",Range(0,1))=.35
        _Opacity("Opacity",Range(0,1))=.7
        [Toggle(_SOFTPARTICLES_ON)] _SoftParticles("Depth softening (requires Depth Texture)",Float)=0
        _SoftDistance("Depth softening distance",Range(.01,1))=.18
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
        Pass
        {
            Name "DustBillboard"
            Tags {"LightMode"="UniversalForward"}
            Blend One OneMinusSrcAlpha
            ZWrite Off Cull Off ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _SOFTPARTICLES_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #if defined(_SOFTPARTICLES_ON)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #endif
            TEXTURE2D(_PuffMap);SAMPLER(sampler_PuffMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _ShadeTint,_AgeTint;
                float _Variation,_EdgeSoftness,_DensityShading;
                float _ErodeStart,_Opacity,_SoftDistance,_SoftParticles,_ColorGain,_SwirlStrength,_SceneLight;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;half4 color:COLOR;float4 data:TEXCOORD0;};
            struct Varyings {float4 positionCS:SV_POSITION;float4 data:TEXCOORD0;half4 color:COLOR;float eyeDepth:TEXCOORD1;half fog:TEXCOORD2;};
            Varyings Vert(Attributes v)
            {
                Varyings o;float3 w=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(w);o.eyeDepth=-TransformWorldToView(w).z;
                o.data=v.data;o.color=v.color;o.fog=ComputeFogFactor(o.positionCS.z);return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float tile=min(floor(i.data.w*4),3);
                float2 tileXY=float2(fmod(tile,2),floor(tile/2));
                float2 p=i.data.xy-.5;
                float angle=_SwirlStrength*i.data.z*(1-saturate(length(p)*1.5));
                float sn=sin(angle),cs=cos(angle);
                p=float2(cs*p.x-sn*p.y,sn*p.x+cs*p.y)+.5;
                float2 uv=(saturate(p)*.96+.02+tileXY)*.5;
                half3 mask=SAMPLE_TEXTURE2D(_PuffMap,sampler_PuffMap,uv).rgb;
                float erosion=saturate((i.data.z-_ErodeStart)/max(1-_ErodeStart,.01));
                float density=mask.r-erosion*(.55+mask.b*.45);
                float aa=max(fwidth(density),_EdgeSoftness);
                half alpha=smoothstep(.07-aa,.07+aa,density)*i.color.a*_Opacity;
                #if defined(_SOFTPARTICLES_ON)
                float raw=SampleSceneDepth(GetNormalizedScreenSpaceUV(i.positionCS));
                float eye=LinearEyeDepth(raw,_ZBufferParams);
                float orthoRaw=raw;
                #if UNITY_REVERSED_Z
                orthoRaw=1-raw;
                #endif
                float orthoEye=lerp(_ProjectionParams.y,_ProjectionParams.z,orthoRaw);
                eye=lerp(eye,orthoEye,unity_OrthoParams.w);
                alpha*=saturate((eye-i.eyeDepth)/max(_SoftDistance,.001));
                #endif
                half3 rgb=i.color.rgb*lerp(_ShadeTint.rgb,half3(1,1,1),smoothstep(.25,.65,mask.g));
                rgb*=lerp(1-_Variation,1,i.data.w);
                rgb*=lerp(1-_DensityShading,1,smoothstep(.15,.8,mask.b));
                rgb*=lerp(half3(1,1,1),_AgeTint.rgb,smoothstep(.2,1,i.data.z));
                Light key=GetMainLight();
                rgb*=_ColorGain*lerp(half3(1,1,1),clamp(key.color,half3(.35,.35,.35),half3(1,1,1)),_SceneLight);
                rgb=MixFog(rgb,i.fog);
                return half4(rgb*alpha,alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
