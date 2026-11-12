// ============================================================================
//  CatCooks / Toon Poof
//
//  Объёмный мультяшный пуф (комок пены/пыли) — частица-меш (LiquidFx.PoofMesh).
//  Как в «Mario-like dust»: непрозрачный, свет двумя тонами с мягкой ступенькой,
//  холодная тень, светлый ободок (rim) и маленький блик. Прозрачности нет —
//  пуф исчезает, сжимаясь в точку (размер по жизни частицы).
//  Цвет — цвет частицы (молочная пена). У камеры пуф «рассыпается» (дизеринг),
//  чтобы не залеплял экран.
// ============================================================================
Shader "CatCooks/Toon Poof"
{
    Properties
    {
        _ShadowTint ("Тень: оттенок (холодный)", Color) = (0.74, 0.8, 0.95, 1)
        _ShadeDark ("Тень: насколько темнее", Range(0, 1)) = 0.86
        _RampMid ("Ступенька света: где", Range(0, 1)) = 0.48
        _RampSoft ("Ступенька света: мягкость", Range(0.01, 0.5)) = 0.07
        _RimColor ("Ободок: цвет", Color) = (1, 1, 1, 1)
        _Rim ("Ободок: сила", Range(0, 1)) = 0.45
        _RimPower ("Ободок: узость", Range(1, 8)) = 3.5
        _Spec ("Блик: сила", Range(0, 1)) = 0.6
        _SpecSize ("Блик: размер", Range(0.8, 1)) = 0.965
        _LightMix ("Цвет света сцены", Range(0, 1)) = 0.35
        _NearFade ("Тает у камеры: от, до (м)", Vector) = (0.25, 0.7, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        ZWrite On
        Cull Back

        Pass
        {
            Name "ToonPoof"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowTint; half _ShadeDark, _RampMid, _RampSoft;
                half4 _RimColor; half _Rim, _RimPower, _Spec, _SpecSize, _LightMix;
                float4 _NearFade;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct V
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1;
                half4 color : COLOR; half fog : TEXCOORD2; half nearK : TEXCOORD3;
            };

            V vert(A v)
            {
                V o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                float dist = length(_WorldSpaceCameraPos - ws);
                o.nearK = saturate((dist - _NearFade.x) / max(1e-3, _NearFade.y - _NearFade.x));
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                // у камеры — рассыпается точками (непрозрачный шейдер, без сортировки)
                float ign = frac(52.9829189 * frac(dot(i.positionCS.xy, float2(0.06711056, 0.00583715))));
                clip(i.nearK - ign * 0.999);

                half3 n = normalize(i.normalWS);
                half3 vdir = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light L = GetMainLight();
                half ndl = dot(n, L.direction) * 0.5h + 0.5h;
                half lit = smoothstep(_RampMid - _RampSoft, _RampMid + _RampSoft, ndl);

                half3 base = i.color.rgb;
                half3 col = lerp(base * _ShadowTint.rgb * _ShadeDark, base, lit);
                col *= lerp(1.0h, L.color, _LightMix);

                half rim = pow(1.0h - saturate(dot(n, vdir)), _RimPower);
                col = lerp(col, _RimColor.rgb, saturate(rim * _Rim * (0.4h + 0.6h * lit)));

                half3 hv = normalize(L.direction + vdir);
                half spec = step(_SpecSize, dot(n, hv));
                col = lerp(col, half3(1, 1, 1), spec * _Spec);

                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
