// ============================================================================
//  CatCooks/Toon Water  —  мультяшная вода для реки и озёр (URP 17 / Unity 6)
//
//  • Градиент глубины (мелководье светлое, глубина насыщенная)
//  • Пена у берегов и вокруг предметов (по глубине сцены)
//  • Бегущие блики-«зайчики» и toon-спекуляр от солнца
//  • Лёгкое искажение дна (рефракция) + вершинные волны
//
//  ВАЖНО: в URP Asset должны быть включены Depth Texture и Opaque Texture!
//  Материал ставится на плоскость воды (её создаёт генератор террейна).
// ============================================================================
Shader "CatCooks/Toon Water"
{
    Properties
    {
        [Header(Color)]
        _ShallowColor ("Shallow Color", Color) = (0.45, 0.85, 0.85, 0.55)
        _DeepColor ("Deep Color", Color) = (0.06, 0.32, 0.45, 0.95)
        _DepthFade ("Depth Fade Distance (m)", Range(0.1, 20)) = 3.5

        [Header(Foam)]
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 1)
        _FoamDistance ("Foam Width (m)", Range(0, 3)) = 0.45
        _FoamNoise ("Foam Noise Texture", 2D) = "white" {}
        _FoamNoiseScale ("Foam Noise Scale", Range(1, 40)) = 12
        _FoamCutoff ("Foam Cutoff", Range(0, 1)) = 0.55
        _FoamSpeed ("Foam Speed", Range(0, 2)) = 0.25

        [Header(Surface Ripples)]
        _RippleNoise ("Ripple Noise (or Normal)", 2D) = "gray" {}
        _RippleScale ("Ripple Scale", Range(1, 40)) = 8
        _RippleSpeed ("Ripple Speed", Range(0, 2)) = 0.15
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.35
        _SparkleCutoff ("Sparkle Cutoff", Range(0.5, 1)) = 0.82
        _SparkleColor ("Sparkle Color", Color) = (1, 1, 1, 1)

        [Header(Specular)]
        _SpecColorW ("Specular Color", Color) = (1, 1, 0.95, 1)
        _SpecSize ("Specular Size", Range(0, 1)) = 0.75
        _SpecIntensity ("Specular Intensity", Range(0, 3)) = 1.0

        [Header(Refraction)]
        _RefractionStrength ("Refraction Strength", Range(0, 0.1)) = 0.015

        [Header(Waves)]
        _WaveAmplitude ("Wave Amplitude (m)", Range(0, 0.5)) = 0.05
        _WaveFrequency ("Wave Frequency", Range(0, 5)) = 1.2
        _WaveSpeed ("Wave Speed", Range(0, 5)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ToonWater"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float  _DepthFade;
                float4 _FoamColor;
                float  _FoamDistance;
                float4 _FoamNoise_ST;
                float  _FoamNoiseScale;
                float  _FoamCutoff;
                float  _FoamSpeed;
                float4 _RippleNoise_ST;
                float  _RippleScale;
                float  _RippleSpeed;
                float  _RippleStrength;
                float  _SparkleCutoff;
                float4 _SparkleColor;
                float4 _SpecColorW;
                float  _SpecSize;
                float  _SpecIntensity;
                float  _RefractionStrength;
                float  _WaveAmplitude;
                float  _WaveFrequency;
                float  _WaveSpeed;
            CBUFFER_END

            TEXTURE2D(_FoamNoise);   SAMPLER(sampler_FoamNoise);
            TEXTURE2D(_RippleNoise); SAMPLER(sampler_RippleNoise);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float4 screenPos   : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                half   fogFactor   : TEXCOORD3;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float3 posOS = IN.positionOS.xyz;
                float3 posW = TransformObjectToWorld(posOS);

                // мягкие перекрёстные волны
                float t = _Time.y * _WaveSpeed;
                float w = sin(posW.x * _WaveFrequency + t) * 0.6
                        + sin(posW.z * _WaveFrequency * 1.37 + t * 1.23) * 0.4;
                posW.y += w * _WaveAmplitude;

                OUT.positionWS = posW;
                OUT.positionCS = TransformWorldToHClip(posW);
                OUT.screenPos  = ComputeScreenPos(OUT.positionCS);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;

                // --- рябь: два слоя, ползущие в разные стороны ---
                float2 ruv = IN.positionWS.xz / _RippleScale;
                float t = _Time.y * _RippleSpeed;
                float r1 = SAMPLE_TEXTURE2D(_RippleNoise, sampler_RippleNoise, ruv + float2(t, t * 0.6)).r;
                float r2 = SAMPLE_TEXTURE2D(_RippleNoise, sampler_RippleNoise, ruv * 1.7 - float2(t * 0.8, t * 0.4)).r;
                float ripple = (r1 + r2) * 0.5;
                float2 rippleOffset = (float2(r1, r2) - 0.5) * 2.0 * _RippleStrength;

                // --- глубина воды ---
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                float surfaceEye = IN.screenPos.w;
                float waterDepth = max(sceneEye - surfaceEye, 0);

                // --- рефракция дна ---
                float2 refrUV = screenUV + rippleOffset * _RefractionStrength;
                float refrDepthRaw = SampleSceneDepth(refrUV);
                float refrEye = LinearEyeDepth(refrDepthRaw, _ZBufferParams);
                // не искажаем то, что стоит ПЕРЕД водой (ноги кота у берега)
                if (refrEye < surfaceEye) refrUV = screenUV;
                float3 sceneColor = SampleSceneColor(refrUV);

                // --- цвет воды по глубине ---
                float fade = saturate(waterDepth / _DepthFade);
                float4 waterCol = lerp(_ShallowColor, _DeepColor, fade);

                float3 color = lerp(sceneColor, waterCol.rgb, waterCol.a);

                // --- пена у берегов ---
                float foamLine = 1.0 - saturate(waterDepth / max(_FoamDistance, 0.001));
                float2 fuv = IN.positionWS.xz / _FoamNoiseScale + float2(_Time.y * _FoamSpeed * 0.1, 0);
                float foamNoise = SAMPLE_TEXTURE2D(_FoamNoise, sampler_FoamNoise, fuv).r;
                float foam = step(_FoamCutoff, foamLine * foamLine + foamNoise * foamLine * 0.8);
                color = lerp(color, _FoamColor.rgb, foam * _FoamColor.a);

                // --- освещение и блики ---
                Light mainLight = GetMainLight();
                float3 normalWS = normalize(IN.normalWS + float3(rippleOffset.x, 0, rippleOffset.y) * 0.6);
                float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));

                float3 H = normalize(mainLight.direction + viewDir);
                float ndh = saturate(dot(normalWS, H));
                float specRaw = pow(ndh, exp2((1.0 - _SpecSize) * 10.0 + 1.0));
                float spec = smoothstep(0.45, 0.55, specRaw);
                color += spec * _SpecColorW.rgb * _SpecIntensity * mainLight.color;

                // «зайчики» — искристые пятна на поверхности
                float sparkle = step(_SparkleCutoff, ripple);
                color += sparkle * _SparkleColor.rgb * 0.5 * (1.0 - foam);

                color = MixFog(color, IN.fogFactor);
                // альфа = 1: прозрачность уже учтена смешиванием с SampleSceneColor
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
