// Частицы погоды (капли, брызги, круги, искры, песок). Альфа или аддитив — через _SrcBlend/_DstBlend.
// Мягкое пересечение с геометрией (soft particles) по текстуре глубины.
Shader "CatCooks/FX Particle"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _SoftFade ("Soft Fade (m)", Range(0, 3)) = 0.3
        [HideInInspector] _SrcBlend ("", Float) = 5
        [HideInInspector] _DstBlend ("", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; half4 _Tint; float _SoftFade; float _SrcBlend, _DstBlend;
            CBUFFER_END
            struct A { float4 pos : POSITION; half4 col : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; half4 col : COLOR; float2 uv : TEXCOORD0; float4 scr : TEXCOORD1; };
            V vert(A v)
            {
                V o;
                o.pos = TransformObjectToHClip(v.pos.xyz);
                o.col = v.col * _Tint;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.scr = ComputeScreenPos(o.pos);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.col;
                if (_SoftFade > 0.001)
                {
                    float2 suv = i.scr.xy / i.scr.w;
                    float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                    float self = i.scr.w;
                    c.a *= saturate((scene - self) / _SoftFade);
                }
                c.rgb *= (_SrcBlend < 1.5) ? c.a : 1.0;   // аддитив: яркость гаснет вместе с альфой
                return c;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
