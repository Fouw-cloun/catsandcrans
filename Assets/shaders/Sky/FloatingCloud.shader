// =============================================================
//  FloatingCloud.shader
//  Мягкое toon/аниме облако для ближнего слоя (billboard, смотрит на камеру).
//  Вешается на Quad. Даёт параллакс и объём небу маленького мира.
//  Дрейфует сам; несколько таких квадов с разным размером = живое небо.
//  URP, Unity 2022.3+ / Unity 6.
// =============================================================
Shader "Custom/FloatingCloud"
{
    Properties
    {
        [Header(Colors)]
        _LitColor    ("Lit (top) Color",    Color) = (1, 1, 1, 1)
        _ShadowColor ("Shadow (bottom) Color", Color) = (0.72, 0.78, 0.88, 1)
        _Steps       ("Toon Shading Steps", Range(2, 8)) = 3

        [Header(Shape)]
        _NoiseScale ("Noise Scale", Range(1, 8)) = 3.0
        _Coverage   ("Coverage",    Range(0, 1)) = 0.5
        _EdgeSoft   ("Edge Softness", Range(0.01, 0.5)) = 0.15
        _Wind       ("Drift (xy)",  Vector) = (0.03, 0.0, 0, 0)

        [Header(Billboard)]
        [Toggle] _Billboard ("Face Camera", Float) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _BILLBOARD_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _LitColor, _ShadowColor;
                float  _Steps, _NoiseScale, _Coverage, _EdgeSoft;
                float4 _Wind;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert (Attributes v)
            {
                Varyings o;
            #ifdef _BILLBOARD_ON
                // разворачиваем quad лицом к камере, сохраняя мировой масштаб/позицию объекта
                float3 centerWS = TransformObjectToWorld(float3(0,0,0));
                float3 right = normalize(UNITY_MATRIX_V._m00_m01_m02);
                float3 up    = normalize(UNITY_MATRIX_V._m10_m11_m12);
                float3 scale = float3(
                    length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20)),
                    length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21)),
                    1);
                float3 posWS = centerWS + right * v.positionOS.x * scale.x + up * v.positionOS.y * scale.y;
                o.positionCS = TransformWorldToHClip(posWS);
            #else
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
            #endif
                o.uv = v.uv;
                return o;
            }

            float hash(float2 p){ p = frac(p*float2(123.34,345.45)); p += dot(p,p+34.345); return frac(p.x*p.y); }
            float valueNoise(float2 p)
            {
                float2 i = floor(p); float2 f = frac(p);
                float2 u = f*f*(3.0-2.0*f);
                float a=hash(i), b=hash(i+float2(1,0)), c=hash(i+float2(0,1)), d=hash(i+float2(1,1));
                return lerp(lerp(a,b,u.x), lerp(c,d,u.x), u.y);
            }
            float fbm(float2 p){ float v=0,a=0.5; for(int k=0;k<5;k++){v+=a*valueNoise(p);p*=2.0;a*=0.5;} return v; }

            half4 frag (Varyings i) : SV_Target
            {
                float2 uv = i.uv;

                // радиальное затухание к краям квадрата (форма клуба)
                float2 c = uv - 0.5;
                float falloff = saturate(1.0 - length(c) * 2.0);

                float n = fbm(uv * _NoiseScale + _Wind.xy * _Time.y);
                float d = falloff * (n + _Coverage);

                float alpha = smoothstep(0.5, 0.5 + _EdgeSoft, d);
                if (alpha <= 0.001) discard;

                // toon-затенение по вертикали: верх освещён, низ в тени
                float shade = floor(saturate(uv.y) * _Steps) / _Steps;
                float3 col = lerp(_ShadowColor.rgb, _LitColor.rgb, shade);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
