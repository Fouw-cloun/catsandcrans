// =============================================================
//  PuffyCloud.shader  v3 (SOFT)
//  Мягкие размытые облака из сфер (ближе к референсам).
//   - Мягкие края: силуэт растворяется в небе через Alpha-To-Coverage
//     (дёшево, БЕЗ проблем сортировки прозрачности, пишет глубину).
//   - Мягкий шейдинг: плавный градиент свет->тень (Banding = 0),
//     либо лёгкие toon-ступени (Banding > 0).
//   - Светлые пушистые края (Rim), небесный ambient (гасит желтизну).
//
//  ВАЖНО: для мягких краёв включи MSAA в URP Asset (Quality -> Anti Aliasing
//  = 2x или 4x). Без MSAA края станут резче.
//  URP, Unity 2022.3+ / Unity 6.
// =============================================================
Shader "Custom/PuffyCloud"
{
    Properties
    {
        [Header(Colors)]
        _LitColor    ("Lit Color",    Color) = (1, 1, 1, 1)
        _ShadowColor ("Shadow Color", Color) = (0.70, 0.76, 0.88, 1)
        _SunTint     ("Sun Tint (0=white)", Range(0, 1)) = 0.25
        _AmbientBoost("Sky Ambient",  Range(0, 1.5)) = 0.55

        [Header(Shading Softness)]
        _Banding   ("Toon Banding (0=soft)", Range(0, 1)) = 0.2
        _RampSteps ("Toon Steps",   Range(2, 6)) = 3
        _ShadowLift("Shadow Lift",  Range(0, 1)) = 0.15

        [Header(Soft Edges)]
        _EdgeFade      ("Edge Fade (softness)", Range(0, 1)) = 0.7
        _EdgeFadePower ("Edge Fade Power", Range(0.5, 6)) = 2.5
        _EdgeNoise     ("Edge Irregularity", Range(0, 1)) = 0.45

        [Header(Fluff)]
        _BumpStrength ("Bump Strength", Range(0, 3)) = 0.7
        _NoiseScale    ("Noise Scale", Range(0.05, 3)) = 0.45
        _NoiseStrength ("Noise Shade", Range(0, 0.6)) = 0.2

        [Header(Rim)]
        _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3.0
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.5

        [Header(Storm)]
        _CloudDarkness ("Cloud Darkness", Range(0, 1)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "PuffyForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            AlphaToMask On      // <- мягкие края через покрытие сэмплов (нужен MSAA)

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float _GlobalCloudDarkness;

            CBUFFER_START(UnityPerMaterial)
                float4 _LitColor, _ShadowColor, _RimColor;
                float  _SunTint, _AmbientBoost;
                float  _Banding, _RampSteps, _ShadowLift;
                float  _EdgeFade, _EdgeFadePower, _EdgeNoise;
                float  _BumpStrength, _NoiseScale, _NoiseStrength;
                float  _RimPower, _RimStrength, _CloudDarkness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            float hash3(float3 p){ p = frac(p*0.3183099 + 0.1); p *= 17.0; return frac(p.x*p.y*p.z*(p.x+p.y+p.z)); }
            float noise3(float3 p)
            {
                float3 i = floor(p); float3 f = frac(p);
                f = f*f*(3.0-2.0*f);
                float n000=hash3(i), n100=hash3(i+float3(1,0,0));
                float n010=hash3(i+float3(0,1,0)), n110=hash3(i+float3(1,1,0));
                float n001=hash3(i+float3(0,0,1)), n101=hash3(i+float3(1,0,1));
                float n011=hash3(i+float3(0,1,1)), n111=hash3(i+float3(1,1,1));
                float x00=lerp(n000,n100,f.x), x10=lerp(n010,n110,f.x);
                float x01=lerp(n001,n101,f.x), x11=lerp(n011,n111,f.x);
                return lerp(lerp(x00,x10,f.y), lerp(x01,x11,f.y), f.z);
            }
            float fbm3(float3 p){ float v=0,a=0.5; for(int k=0;k<3;k++){v+=a*noise3(p);p*=2.03;a*=0.5;} return v; }

            half4 frag (Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 wp = i.positionWS * _NoiseScale;

                // лёгкая бугристость
                float e = 0.35;
                float n0 = fbm3(wp);
                float3 grad = float3(
                    fbm3(wp + float3(e,0,0)) - n0,
                    fbm3(wp + float3(0,e,0)) - n0,
                    fbm3(wp + float3(0,0,e)) - n0);
                N = normalize(N - grad * _BumpStrength);

                float3 V = normalize(GetCameraPositionWS() - i.positionWS);
                Light mainLight = GetMainLight();
                float ndl = dot(N, mainLight.direction) * 0.5 + 0.5;
                ndl = lerp(_ShadowLift, 1.0, ndl); // приподнимаем тени (мягче)

                // мягкий градиент, опционально лёгкие ступени
                float smoothShade = smoothstep(0.0, 1.0, ndl);
                float steps  = max(2.0, _RampSteps);
                float banded = saturate(floor(ndl * steps) / (steps - 1.0));
                float shade  = lerp(smoothShade, banded, _Banding);
                float3 col = lerp(_ShadowColor.rgb, _LitColor.rgb, shade);

                // небесный ambient (голубой, гасит желтизну)
                col += SampleSH(N) * _AmbientBoost;

                // светлый пушистый край
                float ndv = saturate(dot(N, V));
                float rim = pow(1.0 - ndv, _RimPower);
                col += _RimColor.rgb * rim * _RimStrength;

                // затенение шумом
                col *= lerp(1.0 - _NoiseStrength, 1.0, n0);

                // гроза
                float dark = saturate(_CloudDarkness + _GlobalCloudDarkness);
                col = lerp(col, col * float3(0.32, 0.34, 0.40), dark);

                // частичный тон солнца
                col *= lerp(float3(1,1,1), mainLight.color, _SunTint);

                // --- МЯГКИЙ КРАЙ: alpha гаснет к силуэту (растворяется в небе) ---
                float fres = pow(1.0 - ndv, _EdgeFadePower);
                float alpha = 1.0 - fres * _EdgeFade;
                alpha *= lerp(1.0, n0, _EdgeNoise);   // неровная кромка
                alpha = saturate(alpha);

                return half4(col, alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual Cull Back

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct V { float4 positionCS:SV_POSITION; };

            V shadowVert (A v)
            {
                V o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    o.positionCS.z = min(o.positionCS.z, o.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z = max(o.positionCS.z, o.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }
            half4 shadowFrag (V i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
