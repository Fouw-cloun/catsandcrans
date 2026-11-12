// ============================================================================
//  CatCooks / Butter   (v18)
//
//  СЛИВОЧНОЕ МАСЛО — плотное, гладкое, с лёгким блеском (мультяшно). v18.2: без крапинок:
//    • мягкая ступенька света (не пластик), тёплая «подсветка изнутри» в тени;
//    • широкий слабый блик — масло чуть лоснится; полоски-мазки дают сами меши;
//    • едва заметная рябь (следы ножа) — шумом по миру, без текстур и швов.
//  Цвет — из цвета вершин (цвет продукта). В Linear-проекте переводится из sRGB.
// ============================================================================
Shader "CatCooks/Butter"
{
    Properties
    {
        _Sheen ("Блеск", Range(0, 1)) = 0.35
        _SheenSize ("Размер блика", Range(4, 128)) = 22
        _Warm ("Тёплая тень (масло светится изнутри)", Range(0, 1)) = 0.45
        _ShadowTint ("Цвет тени", Color) = (0.86, 0.66, 0.46, 1)
        _Ripple ("Рябь от ножа", Range(0, 1)) = 0.25
        _RippleScale ("Рябь: на метр", Float) = 60
        _Ambient ("Окружение", Range(0, 2)) = 0.9
        _Rim ("Светлый край", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" "RenderPipeline"="UniversalPipeline" }
        Cull Back

        Pass
        {
            Name "Butter"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Sheen, _SheenSize, _Warm, _Ripple, _Ambient, _Rim;
                float _RippleScale;
                half4 _ShadowTint;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; half4 color : COLOR; };

            float hash31(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float vn3(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash31(i), hash31(i + float3(1,0,0)), f.x), lerp(hash31(i + float3(0,1,0)), hash31(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(hash31(i + float3(0,0,1)), hash31(i + float3(1,0,1)), f.x), lerp(hash31(i + float3(0,1,1)), hash31(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            V vert(A v)
            {
                V o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                half3 alb = i.color.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    alb = SRGBToLinear(alb);
                #endif
                float3 n = normalize(i.normalWS);
                // v18.2: рябь от ножа отключена — с бликом она давала белые «крапинки» (_Ripple оставлен для совместимости)

                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 vdir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half ndl = dot(n, L.direction);
                half lit = smoothstep(-0.05, 0.35, ndl) * L.shadowAttenuation;
                half3 shadowC = _ShadowTint.rgb * lerp(0.55, 0.8, _Warm);
                half3 shade = lerp(shadowC, 1.0, lit);
                half3 col = alb * (L.color * shade + SampleSH(n) * _Ambient * 0.5);
                // блик: широкий, мягкий, ступенькой
                float3 hv = normalize(L.direction + vdir);
                half sp = pow(saturate(dot(n, hv)), max(4.0h, _SheenSize * 0.5h));   // v18.2: мягкий широкий блик, без порога
                col += L.color * sp * _Sheen * lit * 0.6;                          // v18.7: масло лоснится заметнее
                // светлый край (масло на свету читается объёмом)
                half fr = pow(1.0 - saturate(dot(n, vdir)), 3.0);
                col += alb * fr * _Rim;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
