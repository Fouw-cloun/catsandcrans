// ============================================================================
//  CatCooks/Toon Puddle   —   Unity 6 (URP 17)
//
//  Мелкая лужа / лужица после дождя. Плоский диск, лежит на земле.
//
//  • мягкий край: лужа растворяется в землю, без резкой линии
//  • отражение неба через SH — дешёвое, без рефлекшн-пробов
//  • toon-блик пятном, а не физкорректный
//  • кольцевая рябь + капли (для дождя)
//  • глубинный фейд по краю, если включён Depth Texture в URP
//
//  Меш: плоский диск с vertex alpha (1 в центре, 0 по краю).
//  Генерируется скриптом catcooks_decor_generator.py, пресет "puddle".
// ============================================================================
Shader "CatCooks/Toon Puddle"
{
    Properties
    {
        [Header(Water)]
        _ShallowColor ("Цвет у края", Color) = (0.55, 0.68, 0.62, 0.35)
        _DeepColor ("Цвет в центре", Color) = (0.20, 0.34, 0.38, 0.85)
        _DepthRange ("Переход край-центр", Range(0.05, 2)) = 0.7
        _EdgeSoft ("Мягкость края", Range(0.01, 1)) = 0.45

        [Header(Sky)]
        _SkyStrength ("Отражение неба", Range(0,2)) = 0.75
        _SkyTint ("Оттенок отражения", Color) = (0.82, 0.90, 1.0, 1)
        _Fresnel ("Крутизна отражения", Range(0.5, 8)) = 3

        [Header(Ripples)]
        _RippleTex ("Карта ряби (нормали, опц.)", 2D) = "bump" {}
        _RippleScale ("Масштаб ряби", Float) = 2.5
        _RippleSpeed ("Скорость ряби", Range(0,3)) = 0.35
        _RippleStrength ("Сила ряби", Range(0,1)) = 0.25

        [Header(Rings)]
        _RingCount ("Колец", Range(0,8)) = 3
        _RingSpeed ("Скорость колец", Range(0,4)) = 1.2
        _RingWidth ("Толщина кольца", Range(0.005, 0.2)) = 0.035
        _RingColor ("Цвет колец", Color) = (1,1,1,1)
        _RingStrength ("Сила колец", Range(0,2)) = 0.5

        [Header(Toon Specular)]
        _SpecColor ("Цвет блика", Color) = (1, 0.98, 0.9, 1)
        _SpecSize ("Размер блика", Range(0.001, 0.3)) = 0.06
        _SpecStrength ("Сила блика", Range(0,3)) = 0.9

        [Header(Lighting)]
        _ShadeFloor ("Пол тени", Range(0,1)) = 0.7
        _ShadowStrength ("Сила теней", Range(0,1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardUnlitWater"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_RippleTex); SAMPLER(sampler_RippleTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _RippleTex_ST;
                float4 _ShallowColor, _DeepColor, _SkyTint, _RingColor, _SpecColor;
                float _DepthRange, _EdgeSoft;
                float _SkyStrength, _Fresnel;
                float _RippleScale, _RippleSpeed, _RippleStrength;
                float _RingCount, _RingSpeed, _RingWidth, _RingStrength;
                float _SpecSize, _SpecStrength;
                float _ShadeFloor, _ShadowStrength;
            CBUFFER_END

            struct A
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            struct V
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float edge : TEXCOORD3;      // vertex alpha: 1 центр, 0 край
                float fogFactor : TEXCOORD4;
            };

            V Vert(A IN)
            {
                V o = (V)0;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                o.uv = IN.uv;
                o.edge = IN.color.a;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(V IN) : SV_Target
            {
                float3 posWS = IN.positionWS;
                float3 N = normalize(IN.normalWS);
                float3 V_ = normalize(GetCameraPositionWS() - posWS);

                // ---- рябь через две прокрутки карты нормалей ----
                float2 ruv = posWS.xz * _RippleScale * 0.1;
                float t = _Time.y * _RippleSpeed;
                half3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_RippleTex, sampler_RippleTex,
                                                         ruv + float2(t * 0.13, t * 0.09)));
                half3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_RippleTex, sampler_RippleTex,
                                                         ruv * 1.7 - float2(t * 0.08, t * 0.15)));
                float3 rip = normalize(n1 + n2);
                N = normalize(N + float3(rip.x, 0, rip.y) * _RippleStrength);

                // ---- глубина: центр темнее и плотнее ----
                float deep = saturate(IN.edge / max(0.01, _DepthRange));
                half4 water = lerp(_ShallowColor, _DeepColor, deep);

                // ---- отражение неба ----
                float fres = pow(1.0 - saturate(dot(N, V_)), _Fresnel);
                float3 refl = reflect(-V_, N);
                half3 sky = SampleSH(refl) * _SkyTint.rgb;
                water.rgb = lerp(water.rgb, sky, saturate(fres * _SkyStrength));

                // ---- бегущие кольца ----
                if (_RingCount > 0.5 && _RingStrength > 0.001)
                {
                    float d = length(IN.uv - 0.5) * 2.0;
                    float phase = frac(_Time.y * _RingSpeed * 0.25);
                    half ring = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        if (i >= (int)_RingCount) break;
                        float r = frac(phase + i / max(1.0, _RingCount));
                        ring += smoothstep(_RingWidth, 0.0, abs(d - r)) * (1.0 - r);
                    }
                    water.rgb += _RingColor.rgb * ring * _RingStrength * IN.edge;
                }

                // ---- свет и toon-блик ----
                Light main = GetMainLight(TransformWorldToShadowCoord(posWS), posWS, half4(1,1,1,1));
                half sh = lerp(1.0, main.shadowAttenuation, _ShadowStrength);
                half lit = lerp(_ShadeFloor, 1.0, sh);
                water.rgb *= main.color * lit + SampleSH(N) * 0.4;

                float3 Hv = normalize(main.direction + V_);
                float spec = pow(saturate(dot(N, Hv)), 64.0);
                spec = smoothstep(0.5 - _SpecSize, 0.5 + _SpecSize, spec);
                water.rgb += _SpecColor.rgb * spec * _SpecStrength * sh;

                // ---- мягкий край ----
                water.a *= smoothstep(0.0, _EdgeSoft, IN.edge);

                water.rgb = MixFog(water.rgb, IN.fogFactor);
                return water;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
