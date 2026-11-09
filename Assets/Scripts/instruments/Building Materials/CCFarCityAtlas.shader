Shader "CatCooks/Environment/Far City Atlas"
{
    Properties
    {
        [MainTexture] _BaseMap("Shared city atlas (sRGB)", 2D) = "white" {}
        [MainColor] _Tint("Overall tint", Color) = (1,1,1,1)
        _ShadowTint("Soft shaded faces", Color) = (0.78,0.81,0.81,1)
        _LightDirection("Art light direction (world XYZ)", Vector) = (0.4,0.8,-0.35,0)
        _FaceContrast("Face contrast", Range(0,1)) = 0.75
        _HazeColor("Warm distance haze", Color) = (0.85,0.79,0.72,1)
        _HazeStart("Haze begins (metres)", Float) = 250
        _HazeEnd("Haze maximum (metres)", Float) = 1400
        _HazeStrength("Haze strength", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back ZWrite On ZTest LEqual
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _Tint, _ShadowTint, _HazeColor;
            float4 _LightDirection;
            float _HazeStart, _HazeEnd;
            half _FaceContrast, _HazeStrength;
        CBUFFER_END
        struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
        struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half3 shade:TEXCOORD1; half haze:TEXCOORD2; };
        Varyings Vert(Attributes v)
        {
            Varyings o;
            float3 world = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(world);
            o.uv = v.uv; // Atlas UVs must not repeat across tile boundaries.
            half3 normal = TransformObjectToWorldNormal(v.normalOS);
            float3 lightDir = _LightDirection.xyz * rsqrt(max(dot(_LightDirection.xyz, _LightDirection.xyz), 0.0001));
            half facing = saturate(dot(normal, lightDir) * 0.5 + 0.5);
            o.shade = lerp(half3(1,1,1), lerp(_ShadowTint.rgb, half3(1,1,1), facing), _FaceContrast);
            float distanceToCamera = distance(world, GetCameraPositionWS());
            o.haze = smoothstep(_HazeStart, max(_HazeStart + 1.0, _HazeEnd), distanceToCamera) * _HazeStrength;
            return o;
        }
        half4 Frag(Varyings i):SV_Target
        {
            half3 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _Tint.rgb * i.shade;
            return half4(lerp(color, _HazeColor.rgb, i.haze), 1);
        }
        ENDHLSL
        Pass
        {
            Name "FarCityColor"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            float4 DepthVert(Attributes v):SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half4 DepthFrag():SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
