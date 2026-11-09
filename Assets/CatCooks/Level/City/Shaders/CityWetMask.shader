// CatCooks — маска «что мокнет»: отмеченные City Wet Surface объекты рисуются белым поверх глубины кадра
// (только видимые части). Читает проход мокрости (CityRainSurfaceFeature). Сам по себе на материалы не ставится.
Shader "Hidden/CatCooks/City/Wet Mask"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "WetMask"
            ZWrite Off ZTest LEqual Cull Back
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"   // GPU Resident Drawer / BRG
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _WetMaskUnused;
            CBUFFER_END

            struct A { float4 pos : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            V Vert(A i)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = TransformObjectToHClip(i.pos.xyz);
                return o;
            }

            half4 Frag(V i) : SV_Target { return half4(1, 1, 1, 1); }
            ENDHLSL
        }
    }
    Fallback Off
}
