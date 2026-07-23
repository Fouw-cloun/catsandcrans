Shader "Custom/HoseURP"
{
    // Рифлёный шланг: чередование серого и чёрного вдоль длины (по UV.y, который
    // RopeMeshRenderer гонит вдоль верёвки), плюс лёгкая «гофра» — затенение по
    // рёбрам, чтобы читались объёмные кольца, как на гофрошланге.
    Properties
    {
        [Header(Colors)]
        _ColorA ("Серый (кольцо)", Color) = (0.55, 0.55, 0.55, 1)
        _ColorB ("Чёрный (кольцо)", Color) = (0.06, 0.06, 0.06, 1)

        [Header(Ribbing)]
        _RibFreq ("Частота колец (вдоль длины)", Float) = 8.0
        _RibSharp ("Резкость границы колец", Range(0.5, 20)) = 6.0
        _GrooveDepth ("Глубина гофры (затенение)", Range(0, 1)) = 0.35

        [Header(Surface)]
        _Smoothness ("Гладкость", Range(0,1)) = 0.35
        _Metallic ("Металличность", Range(0,1)) = 0.0
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
            #pragma multi_compile_fog

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
                float  fogFactor   : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorA;
                float4 _ColorB;
                float  _RibFreq;
                float  _RibSharp;
                float  _GrooveDepth;
                float  _Smoothness;
                float  _Metallic;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = pos.positionCS;
                OUT.positionWS  = pos.positionWS;
                OUT.normalWS    = nrm.normalWS;
                OUT.uv          = IN.uv;
                OUT.fogFactor   = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Позиция вдоль длины шланга — UV.y (RopeMeshRenderer её накапливает).
                float along = IN.uv.y * _RibFreq;

                // Чередование серого/чёрного: волна 0..1 с резкой границей.
                float wave = frac(along);                       // 0..1 внутри кольца
                float band = saturate((abs(wave - 0.5) - 0.25) * _RibSharp + 0.5);
                // band ~ 0 в центре кольца, ~1 на границе → чередуем два цвета по половинам
                float half01 = step(0.5, wave);                 // 0 = серая половина, 1 = чёрная
                half3 baseCol = lerp(_ColorA.rgb, _ColorB.rgb, half01);

                // «Гофра»: затемняем по границам колец, чтобы читался объём кольца.
                float groove = 1.0 - _GrooveDepth * (1.0 - abs(sin(wave * 3.14159265)));
                baseCol *= groove;

                // Освещение URP.
                float3 N = normalize(IN.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float ndotl = saturate(dot(N, mainLight.direction));
                half3 lighting = mainLight.color * (ndotl * mainLight.shadowAttenuation);
                half3 ambient = SampleSH(N);

                half3 color = baseCol * (lighting + ambient);

                // Доп. источники света.
                #ifdef _ADDITIONAL_LIGHTS
                uint count = GetAdditionalLightsCount();
                for (uint li = 0; li < count; li++)
                {
                    Light l = GetAdditionalLight(li, IN.positionWS);
                    float d = saturate(dot(N, l.direction));
                    color += baseCol * l.color * (d * l.distanceAttenuation * l.shadowAttenuation);
                }
                #endif

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        // Тень от шланга.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct AttrS { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct VarS  { float4 positionHCS : SV_POSITION; };

            VarS vertShadow (AttrS IN)
            {
                VarS OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 pos = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    pos.z = min(pos.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    pos.z = max(pos.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionHCS = pos;
                return OUT;
            }

            half4 fragShadow (VarS IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
