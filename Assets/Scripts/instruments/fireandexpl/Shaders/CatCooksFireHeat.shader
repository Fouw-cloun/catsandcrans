Shader "CatCooks/Fire V3/Heat"
{
    Properties
    {
        _Pixels ("Distortion pixels", Range(0,2)) = .7
        _Opacity ("Blend", Range(0,.25)) = .12
        [HideInInspector] _FlowTime ("Time", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent"
            "Queue"="Transparent" "DisableBatching"="True" }
        Pass
        {
            Name "LocalHeat"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back ZWrite Off ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "CatCooksFireNoise.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Pixels, _Opacity, _FlowTime;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float3 shape : TEXCOORD2;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 shape : TEXCOORD3;
            };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv; o.shape = v.shape;
                return o;
            }
            float EyeDepth(float raw)
            {
                float perspective = LinearEyeDepth(raw, _ZBufferParams);
                #if UNITY_REVERSED_Z
                    raw = 1.0 - raw;
                #endif
                float ortho = lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
                return lerp(perspective, ortho, unity_OrthoParams.w);
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float2 pixel = rcp(_ScaledScreenParams.xy);
                float2 motion = float2(CCFNoise(i.shape * 4 + float3(0,-_FlowTime,0)),
                    CCFNoise(i.shape * 4 + float3(8,-_FlowTime * 1.2,3))) * 2 - 1;
                float2 offset = motion * clamp(_Pixels, 0, 2) * pixel;
                float2 sampleUV = clamp(uv + offset, pixel, 1.0 - pixel);
                float front = -TransformWorldToView(i.positionWS).z;
                float sceneDepth = EyeDepth(SampleSceneDepth(sampleUV));
                // Reject foreground leakage before offset sampling is blended in.
                float depthFade = saturate((sceneDepth - front) / .18);
                float3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float facing = saturate(dot(normalize(i.normalWS), view));
                float silhouette = smoothstep(.05, .6, facing) * smoothstep(0, .22, i.uv.y)
                    * (1.0 - smoothstep(.72, 1, i.uv.y));
                float nearFade = smoothstep(.20, .65, distance(i.positionWS, GetCameraPositionWS()));
                float2 blur = pixel * min(clamp(_Pixels, 0, 2), .8);
                half3 colour = SampleSceneColor(sampleUV) * .4;
                colour += SampleSceneColor(clamp(sampleUV + float2(blur.x,0), pixel, 1-pixel)) * .15;
                colour += SampleSceneColor(clamp(sampleUV - float2(blur.x,0), pixel, 1-pixel)) * .15;
                colour += SampleSceneColor(clamp(sampleUV + float2(0,blur.y), pixel, 1-pixel)) * .15;
                colour += SampleSceneColor(clamp(sampleUV - float2(0,blur.y), pixel, 1-pixel)) * .15;
                return half4(colour, clamp(_Opacity, 0, .25) * silhouette * nearFade * depthFade);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
