// URP-шейдер верёвки: базовое освещение + процедурные "витки" волокон по UV.
// U идёт вокруг трубы, V — вдоль длины (см. RopeMeshRenderer). Из этого делаем
// диагональные бороздки (кручёная верёвка) и лёгкое затемнение в канавках,
// плюс fake-normal возмущение для рельефа. Работает в Universal RP.
Shader "Custom/RopeURP"
{
    Properties
    {
        _BaseColor   ("Base Color", Color) = (0.62, 0.47, 0.28, 1)
        _DarkColor   ("Groove Color", Color) = (0.30, 0.21, 0.11, 1)
        _Strands     ("Strands", Range(1, 12)) = 3
        _Twist       ("Twist", Range(0, 30)) = 10
        _GrooveDepth ("Groove Depth", Range(0, 1)) = 0.55
        _Bump        ("Fake Bump", Range(0, 1)) = 0.4
        _Smoothness  ("Smoothness", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _DarkColor;
                float _Strands, _Twist, _GrooveDepth, _Bump, _Smoothness;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                OUT.positionWS  = pos.positionWS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv          = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // диагональная координата вдоль витков: U (вокруг) + скрут по длине V
                float strandCoord = IN.uv.x * _Strands + IN.uv.y * _Twist;
                float g = frac(strandCoord);                 // 0..1 внутри пряди
                float groove = abs(g - 0.5) * 2.0;           // 0 в центре пряди, 1 в канавке

                float darken = smoothstep(1.0 - _GrooveDepth, 1.0, groove);
                float3 albedo = lerp(_BaseColor.rgb, _DarkColor.rgb, darken);

                // fake-normal: наклоняем нормаль по градиенту канавок для объёма
                float3 N = normalize(IN.normalWS);
                float bump = (g - 0.5) * _Bump;
                float3 tangentish = normalize(cross(N, float3(0,1,0)) + 1e-4);
                N = normalize(N + tangentish * bump);

                // основной свет + тень
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float ndotl = saturate(dot(N, mainLight.direction));
                float3 lighting = mainLight.color * (ndotl * mainLight.shadowAttenuation);

                // ambient
                lighting += SampleSH(N);

                // дополнительные источники
                #ifdef _ADDITIONAL_LIGHTS
                uint count = GetAdditionalLightsCount();
                for (uint i = 0; i < count; i++)
                {
                    Light l = GetAdditionalLight(i, IN.positionWS);
                    lighting += l.color * (saturate(dot(N, l.direction)) * l.distanceAttenuation * l.shadowAttenuation);
                }
                #endif

                // немного спекуляра
                float3 V = normalize(GetCameraPositionWS() - IN.positionWS);
                float3 H = normalize(mainLight.direction + V);
                float spec = pow(saturate(dot(N, H)), lerp(4.0, 64.0, _Smoothness)) * _Smoothness;

                float3 color = albedo * lighting + spec * mainLight.color * mainLight.shadowAttenuation;
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // Тень от верёвки (caster)
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct V { float4 positionHCS:SV_POSITION; };

            V shadowVert (A IN)
            {
                V OUT;
                float3 ws = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(IN.normalOS);
                float4 hcs = TransformWorldToHClip(ApplyShadowBias(ws, nw, _LightDirection));
                #if UNITY_REVERSED_Z
                    hcs.z = min(hcs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    hcs.z = max(hcs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionHCS = hcs;
                return OUT;
            }
            half4 shadowFrag (V IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
