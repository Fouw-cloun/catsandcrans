// CatCooks — «дырка» в земле для входа в метро (земля — обычная модель улицы, вырезать её нельзя).
// Два материала на одной плоскости над проёмом:
//   1) «Метка» (очередь 2501): где плоскость видна — ставит бит трафарета 64, ничего не рисует.
//   2) «Сброс» (очередь 2502): только там, где метка, — глубину на самую дальнюю (земля «исчезает» для глубины).
// Потом яма (материал Toon, очередь 2503, трафарет Equal 64) рисуется внутри проёма, ниже уровня земли.
// Очереди > 2500 — после непрозрачных и неба, поэтому земля и предметы перед проёмом уже на месте.
Shader "CatCooks/City/Metro Hole"
{
    Properties
    {
        [Toggle] _WriteFar ("Сброс глубины (1 — второй материал)", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Трафарет: сравнение", Float) = 8
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilPass ("Трафарет: запись", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+501" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _WriteFar;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Hole"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off ZTest [_ZTest] ZWrite [_ZWrite] ColorMask 0
            Offset -1, -1
            Stencil { Ref 64 ReadMask 64 WriteMask 64 Comp [_StencilComp] Pass [_StencilPass] }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half4 frag(V i, out float depth : SV_Depth) : SV_Target
            {
                #if UNITY_REVERSED_Z
                    float far = 0.0;
                #else
                    float far = 1.0;
                #endif
                depth = _WriteFar > 0.5 ? far : i.positionCS.z;
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
