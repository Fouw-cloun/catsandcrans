// CatCooks — простой мультяшный шейдер для процедурных вещей города: вход в метро, самолёты, дирижабль,
// воздушные шары, птицы. Цвет в вершинах × текстура (баннеры) × цвет; тёплая ступенька света как у домов,
// подсветка из сцены (SH). Свечение (_EmissionColor) — для глобусов-фонарей и окон. «Яма» (_PitDepth):
// ниже нуля объекта темнеет к _PitDark — спуск в метро уходит в темноту. Трафарет/глубина/отсечение — из свойств
// (яма метро рисуется только внутри «дырки»). Без теней. URP, SRP Batcher.
Shader "CatCooks/City/Toon"
{
    Properties
    {
        [MainTexture] _BaseMap ("Текстура", 2D) = "white" {}
        [MainColor] _BaseColor ("Цвет", Color) = (1, 1, 1, 1)
        _ShadowColor ("Тень — цвет", Color) = (0.62, 0.50, 0.55, 1)
        _LightTint ("Свет — оттенок", Color) = (1, 0.95, 0.86, 1)
        _Ambient ("Подсветка", Range(0, 2)) = 0.7
        _Unlit ("Без света (0 — свет, 1 — плоский цвет)", Range(0, 1)) = 0
        [HDR] _EmissionColor ("Свечение", Color) = (0, 0, 0, 1)
        _PitDepth ("Яма: глубина затемнения (0 — нет)", Float) = 0
        _PitDark ("Яма: цвет в глубине", Color) = (0.05, 0.05, 0.09, 1)
        [Header(Render state)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Отсечение", Float) = 2
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [IntRange] _StencilRef ("Трафарет: значение", Range(0, 255)) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Трафарет: сравнение", Float) = 8
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _ShadowColor, _LightTint, _EmissionColor, _PitDark;
            half _Ambient, _Unlit;
            float _PitDepth;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        ENDHLSL

        Pass
        {
            Name "ForwardToon"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull] ZTest [_ZTest] ZWrite On
            Stencil { Ref [_StencilRef] ReadMask 64 Comp [_StencilComp] }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"   // GPU Resident Drawer (Unity 6)
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CityHeightShadow.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half4 color : TEXCOORD2;
                float yOS : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                o.yOS = v.positionOS.y;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                return o;
            }

            float4 _CC_Flash, _CC_FlashDir;   // вспышка молнии (CityStorm)
            float _CC_SnowCover, _CC_Meter;   // снег на верхних гранях (CityWinter)
            float TH21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float TNoise(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f); return lerp(lerp(TH21(i), TH21(i + float2(1, 0)), f.x), lerp(TH21(i + float2(0, 1)), TH21(i + float2(1, 1)), f.x), f.y); }
            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 nw = i.normalWS;
                nw = dot(nw, nw) > 1e-10 ? normalize(nw) : float3(0, 1, 0);          // битая нормаль → не NaN (иначе белая вспышка в bloom)
                half3 n = (half3)nw * (front ? 1.0h : -1.0h);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 albedo = tex.rgb * i.color.rgb * _BaseColor.rgb;
                Light ml = GetMainLight(TransformWorldToShadowCoord(i.positionWS));      // теперь и тени от домов/навесов
                half wrap = saturate((dot(n, ml.direction) + 0.35h) / 1.35h);
                half sh = lerp(1.0h, ml.shadowAttenuation, 0.85h);
                sh = min(sh, lerp(1.0h, CC_HeightShadow(i.positionWS, n, ml.direction), CC_HeightFar(i.positionWS) * (half)_CC_HMapP.x));
                half hao = lerp(1.0h, CC_HeightAO(i.positionWS, n), (half)_CC_HMapP.y);
                half ramp = smoothstep(0.3h, 0.6h, wrap) * sh;
                half shLum = max(dot(SampleSH(half3(0, 1, 0)), half3(0.333h, 0.333h, 0.333h)), 0.0h);
                half3 light = lerp(_ShadowColor.rgb * saturate(shLum * 1.6h), ml.color * _LightTint.rgb, ramp) + SampleSH(n) * _Ambient * 0.5h * hao;
                light *= lerp(1.0h, hao, 0.35h);
                half3 col = lerp(albedo * light, albedo, _Unlit);
                if (_CC_SnowCover > 0.001)
                {
                    float2 ps = i.positionWS.xz / (_CC_Meter > 0.0 ? _CC_Meter : 3.5);
                    float sn = TNoise(ps / 1.4) * 0.55 + TNoise(ps / 0.35) * 0.45;
                    half sm = smoothstep(0.55h, 0.8h, n.y) * (half)saturate((sn - (1.05 - _CC_SnowCover * 1.3)) / 0.05 + 0.5);
                    col = lerp(col, half3(0.93h, 0.96h, 1.0h) * light * 1.05h, sm * (1.0h - _Unlit));   // снег на верхушках пропсов
                }
                if (_PitDepth > 0.0)
                {
                    half d = saturate(-i.yOS / _PitDepth);
                    col = lerp(col, _PitDark.rgb, d * d * (3.0h - 2.0h * d));
                }
                col += _EmissionColor.rgb * i.color.rgb;
                if (_CC_Flash.a > 0.001)                                          // молния
                    col += albedo * (half3)_CC_Flash.rgb * (half)_CC_Flash.a * (0.3h + 0.7h * saturate(dot(n, (half3)_CC_FlashDir.xyz) * 0.5h + 0.5h));
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"   // GPU Resident Drawer (Unity 6)
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vertDepth(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half fragDepth(V i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            // тень от объекта (зонт, навесы). Включается галкой Cast Shadows у рендерера — фон и мелочь её не включают.
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vertSC
            #pragma fragment fragSC
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"   // GPU Resident Drawer (Unity 6)
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };
            V vertSC(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v);
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 ld = normalize(_LightPosition - p);
            #else
                float3 ld = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(p, n, ld));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = cs;
                return o;
            }
            half4 fragSC(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vertDN
            #pragma fragment fragDN
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"   // GPU Resident Drawer (Unity 6)
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vertDN(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 fragDN(V i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
