Shader "CatCooks/Fire V3/Body"
{
    Properties
    {
        _Outer ("Outer orange", Color) = (0.95,0.12,0.015,1)
        _Middle ("Middle amber", Color) = (1,0.37,0.035,1)
        _Inner ("Inner yellow", Color) = (1,0.78,0.16,1)
        _Core ("Small pale accents", Color) = (1,0.94,0.51,1)
        _Emission ("Emission", Range(0.3,2)) = 1.15
        _Sway ("Sway", Range(0,1)) = 0.55
        _Erosion ("Tip erosion", Range(0,1)) = 0.55
        _Softness ("Colour softness", Range(0,1)) = 0.22
        _Intensity ("Presence", Range(0,1)) = 1
        [HideInInspector] _FlowTime ("Time", Float) = 0
        [HideInInspector] _Wind ("Local bounded wind", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout"
            "Queue"="AlphaTest" "DisableBatching"="True" }
        HLSLINCLUDE
        #include "CatCooksFireBodyPass.hlsl"
        ENDHLSL
        Pass
        {
            Name "FireBody"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back ZWrite On ZTest LEqual Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Back ZWrite On ZTest LEqual ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma target 3.5
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode"="DepthNormalsOnly" }
            Cull Back ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment NormalsFrag
            #pragma target 3.5
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    Fallback Off
}
