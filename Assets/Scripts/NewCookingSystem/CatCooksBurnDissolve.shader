// ============================================================================
//  CatCooks / Burn Dissolve
//
//  Продукт сгорает: сначала ОБУГЛИВАЕТСЯ (_Char 0→1: темнеет до угля, по нему тлеют
//  оранжевые прожилки), потом РАССЫПАЕТСЯ (_Burn 0→1: прогорает дырами по шуму, сверху
//  раньше, край прогара светится оранжево-жёлтым). Подставляется вместо материалов продукта
//  на время горения (FoodBurnable), текстура и цвет берутся из старого материала.
//  _MinMaxY — высота меша в его осях (для «сверху раньше»), _NoiseScale — под размер меша.
// ============================================================================
Shader "CatCooks/Burn Dissolve"
{
    Properties
    {
        _BaseMap ("Текстура", 2D) = "white" {}
        _BaseColor ("Цвет", Color) = (1, 1, 1, 1)
        _Char ("Обуглилось", Range(0, 1)) = 0
        _Burn ("Прогорело", Range(0, 1)) = 0
        _EdgeWidth ("Ширина края прогара", Range(0, 0.3)) = 0.09
        [HDR] _EdgeCol ("Край прогара", Color) = (4, 1.3, 0.25, 1)
        [HDR] _EmberCol ("Тлеющие прожилки", Color) = (3, 0.7, 0.1, 1)
        _NoiseScale ("Шум: размер", Float) = 12
        _MinMaxY ("Высота меша (мин, макс)", Vector) = (-0.5, 0.5, 0, 0)
        _CharColor ("Цвет угля", Color) = (0.05, 0.04, 0.035, 1)
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        Cull Off

        Pass
        {
            Name "BurnDissolve"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor, _EdgeCol, _EmberCol, _CharColor;
                half _Char, _Burn, _EdgeWidth;
                float _NoiseScale;
                float4 _MinMaxY;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2; float3 positionWS : TEXCOORD3;
            };

            float hash31(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float vnoise(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash31(i + float3(0, 0, 0)), hash31(i + float3(1, 0, 0)), f.x),
                                 lerp(hash31(i + float3(0, 1, 0)), hash31(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(hash31(i + float3(0, 0, 1)), hash31(i + float3(1, 0, 1)), f.x),
                                 lerp(hash31(i + float3(0, 1, 1)), hash31(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            V vert(A v)
            {
                V o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            half4 frag(V i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 p = i.positionOS * _NoiseScale;
                float h = saturate((i.positionOS.y - _MinMaxY.x) / max(1e-4, _MinMaxY.y - _MinMaxY.x));
                float n = vnoise(p) * 0.6 + vnoise(p * 2.7 + 13.0) * 0.4;
                // прогорает: шум + сверху раньше
                float d = n * 0.72 + (1.0 - h) * 0.28;
                float cut = _Burn * 1.08;
                clip(d - cut);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                half3 alb = lerp(tex.rgb, _CharColor.rgb, smoothstep(0.0, 1.0, _Char));

                // свет: мягкая ступенька + окружение
                half3 nrm = normalize(i.normalWS) * IS_FRONT_VFACE(facing, 1.0, -1.0);
                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = saturate(dot(nrm, L.direction));
                half lit = smoothstep(0.3, 0.45, ndl * L.shadowAttenuation) * 0.65 + 0.35;
                half3 col = alb * (L.color * lit + SampleSH(nrm) * 0.6);

                // тлеющие прожилки по углю (мерцают)
                float cr = vnoise(p * 1.9 + float3(0, _Time.y * 0.4, 0));
                half crack = 1.0 - smoothstep(0.0, 0.05, abs(cr - 0.5));
                half flick = 0.7 + 0.3 * sin(_Time.y * 7.0 + n * 20.0);
                col += _EmberCol.rgb * crack * smoothstep(0.4, 1.0, _Char) * flick * (1.0 - _Burn * 0.5);

                // светящийся край прогара
                half edge = 1.0 - smoothstep(0.0, _EdgeWidth, d - cut);
                col = lerp(col, _EdgeCol.rgb, edge * step(0.001, _Burn));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
