// ============================================================================
//  CatCooks/Toon Bark   —   Unity 6 (URP 17)
//
//  Ствол и ветки. Использует ТОТ ЖЕ ветер, что и крона (CatCooksWind.hlsl),
//  поэтому дерево гнётся целиком.
//
//  ВАЖНО: параметры Bend Start / Tree Height должны совпадать со значениями
//  в материале кроны, иначе ствол и листва разъедутся при сильном ветре.
// ============================================================================
Shader "CatCooks/Toon Bark"
{
    Properties
    {
        [Header(Textures)]
        _BaseMap ("Base Color", 2D) = "white" {}
        _BaseTint ("Оттенок", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Сила нормалей", Range(0,3)) = 0.8

        [Header(Wind)]
        _WindMult ("Множитель ветра", Range(0,3)) = 1
        _TrunkStiffness ("Жёсткость ствола", Range(0,1)) = 0.35
        _BendStart ("Изгиб начинается с Y (м)", Float) = 0.5
        _TreeHeight ("Высота дерева (м)", Float) = 6
        _WindFadeStart ("Ветер слабеет с (м)", Float) = 60
        _WindFadeEnd ("Ветер выключен на (м)", Float) = 140

        [Header(Toon Lighting)]
        _ShadeSteps ("Ступеней света", Range(1,6)) = 3
        _ShadeFloor ("Пол тени (яркость)", Range(0,1)) = 0.55
        _LightWrap ("Заворот света", Range(0,1)) = 0.4
        _ShadowTint ("Оттенок тени", Color) = (0.55, 0.66, 0.92, 1)
        _ShadowStrength ("Сила падающих теней", Range(0,1)) = 0.55
        _AmbientBoost ("Ambient", Range(0,3)) = 1.3
        _LightBoost ("Прямой свет", Range(0,3)) = 1.1

        [Header(Per Tree Variation)]
        _VarTint ("Цвет вариации", Color) = (0.9, 0.85, 0.78, 1)
        _VarAmount ("Разброс цвета", Range(0,1)) = 0.25
        _VarBrightness ("Разброс яркости", Range(0,0.6)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "CatCooksWind.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _BumpMap_ST;
            float4 _BaseTint, _ShadowTint, _VarTint;
            float _BumpScale;
            float _WindMult, _TrunkStiffness, _BendStart, _TreeHeight;
            float _WindFadeStart, _WindFadeEnd;
            float _ShadeSteps, _ShadeFloor, _LightWrap, _ShadowStrength;
            float _AmbientBoost, _LightBoost;
            float _VarAmount, _VarBrightness;
        CBUFFER_END

        float3 CatBarkWind(float3 positionOS, float3 posWS)
        {
            float3 originWS = CatObjectOriginWS();
            float dist = distance(GetCameraPositionWS(), posWS);
            float fade = 1.0 - saturate((dist - _WindFadeStart) /
                                        max(0.001, _WindFadeEnd - _WindFadeStart));
            fade *= _WindMult;

            float hMask = saturate((positionOS.y - _BendStart) /
                                   max(0.001, _TreeHeight - _BendStart));

            return CatWindMainBend(posWS, originWS, hMask, _TrunkStiffness) * fade;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            struct A
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct V
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                float treeHash : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            V Vert(A IN)
            {
                V OUT = (V)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                posWS += CatBarkWind(IN.positionOS.xyz, posWS);

                VertexNormalInputs n = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.positionWS = posWS;
                OUT.normalWS = n.normalWS;
                OUT.tangentWS = float4(n.tangentWS, IN.tangentOS.w);
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                OUT.treeHash = CatHash13(floor(CatObjectOriginWS() * 7.31));
                return OUT;
            }

            half4 Frag(V IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                #ifdef LOD_FADE_CROSSFADE
                    LODFadeCrossFade(IN.positionCS);
                #endif

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseTint.rgb;

                half h = IN.treeHash;
                albedo = lerp(albedo, albedo * _VarTint.rgb * 1.5, h * _VarAmount);
                albedo *= 1.0 + (h - 0.5) * 2.0 * _VarBrightness;

                float3 Ngeo = normalize(IN.normalWS);
                half3 nTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                float3 T = normalize(IN.tangentWS.xyz);
                float3 B = normalize(cross(Ngeo, T) * IN.tangentWS.w);
                float3 N = normalize(nTS.x * T + nTS.y * B + nTS.z * Ngeo);

                float3 posWS = IN.positionWS;
                float4 shadowCoord = TransformWorldToShadowCoord(posWS);
                Light main = GetMainLight(shadowCoord, posWS, half4(1, 1, 1, 1));

                float ndl = dot(N, main.direction);
                float t = saturate(lerp(ndl, ndl * 0.5 + 0.5, _LightWrap));
                t = min(t, lerp(1.0, main.shadowAttenuation, _ShadowStrength));

                float ls = max(1.0, floor(_ShadeSteps));
                float scaled = t * ls;
                float aa = saturate(fwidth(scaled) * 0.7);
                float band = saturate((floor(scaled) + smoothstep(0.5 - aa, 0.5 + aa, frac(scaled))) / ls);

                float lit = lerp(_ShadeFloor, 1.0, band);
                half3 tint = lerp(_ShadowTint.rgb, half3(1, 1, 1), band);

                half3 col = albedo * main.color * lit * tint * _LightBoost;
                col += albedo * SampleSH(N) * _AmbientBoost;

                col = MixFog(col, IN.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex SV
            #pragma fragment SF
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            struct A
            {
                float4 positionOS : POSITION; float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct V { float4 positionCS : SV_POSITION; };

            V SV(A IN)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 pw = TransformObjectToWorld(IN.positionOS.xyz);
                pw += CatBarkWind(IN.positionOS.xyz, pw);
                float3 nw = TransformObjectToWorldNormal(IN.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(pw, nw, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                return o;
            }
            half4 SF(V IN) : SV_Target
            {
                #ifdef LOD_FADE_CROSSFADE
                    LODFadeCrossFade(IN.positionCS);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex DV
            #pragma fragment DF
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };
            V DV(A IN)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 pw = TransformObjectToWorld(IN.positionOS.xyz);
                pw += CatBarkWind(IN.positionOS.xyz, pw);
                o.positionCS = TransformWorldToHClip(pw);
                return o;
            }
            half4 DF(V IN) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex NV
            #pragma fragment NF
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct A
            {
                float4 positionOS : POSITION; float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            V NV(A IN)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 pw = TransformObjectToWorld(IN.positionOS.xyz);
                pw += CatBarkWind(IN.positionOS.xyz, pw);
                o.positionCS = TransformWorldToHClip(pw);
                o.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return o;
            }
            half4 NF(V IN) : SV_Target { return half4(normalize(IN.normalWS) * 0.5 + 0.5, 0); }
            ENDHLSL
        }
    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
