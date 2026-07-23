// =============================================================
//  StylizedSky.shader  (Skybox)  v2
//  Градиент + солнце + toon-облака. Добавлено: плотность (Coverage),
//  потемнение облаков (CloudDarkness) и пасмурность неба (Overcast)
//  -> можно делать ясно / пасмурно / грозовые тучи.
//  URP, Unity 2022.3+ / Unity 6. Параметры пушит SkyController.
// =============================================================
Shader "Custom/StylizedSky"
{
    Properties
    {
        [Header(Sky Gradient)]
        _ZenithColor  ("Zenith Color",  Color) = (0.30, 0.55, 0.90, 1)
        _HorizonColor ("Horizon Color", Color) = (0.75, 0.85, 0.95, 1)
        _GroundColor  ("Ground Color",  Color) = (0.55, 0.60, 0.65, 1)
        _SkyExponent  ("Sky Exponent",  Range(0.1, 4)) = 1.3

        [Header(Sun)]
        _SunDir      ("Sun Direction",  Vector) = (0.3, 0.6, 0.3, 0)
        _SunColor    ("Sun Color",      Color)  = (1.0, 0.95, 0.85, 1)
        _SunSize     ("Sun Size",       Range(0.9, 1.0)) = 0.995
        _SunSoftness ("Sun Softness",   Range(0.0001, 0.02)) = 0.003
        _SunIntensity("Sun Intensity",  Range(0, 5)) = 2.0
        _SunGlow     ("Sun Glow",       Range(0, 2)) = 0.5
        _SunGlowExp  ("Sun Glow Falloff", Range(1, 64)) = 16

        [Header(Clouds)]
        _CloudColor       ("Cloud Lit Color",    Color) = (1, 1, 1, 1)
        _CloudShadowColor ("Cloud Shadow Color", Color) = (0.70, 0.75, 0.85, 1)
        _Coverage    ("Coverage (density)", Range(0, 1)) = 0.5
        _CloudSoft   ("Cloud Edge Softness", Range(0.01, 0.5)) = 0.12
        _CloudScale  ("Cloud Scale",  Range(0.2, 8)) = 2.0
        _CloudHorizonFade ("Horizon Fade", Range(0.01, 0.5)) = 0.12
        _CloudDarkness ("Cloud Darkness (storm)", Range(0, 1)) = 0.0
        _Overcast    ("Sky Overcast", Range(0, 1)) = 0.0
        _Wind        ("Wind (xy)",    Vector) = (0.01, 0.006, 0, 0)
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off  ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor, _HorizonColor, _GroundColor;
                float  _SkyExponent;
                float4 _SunDir, _SunColor;
                float  _SunSize, _SunSoftness, _SunIntensity, _SunGlow, _SunGlowExp;
                float4 _CloudColor, _CloudShadowColor;
                float  _Coverage, _CloudSoft, _CloudScale, _CloudHorizonFade, _CloudDarkness, _Overcast;
                float4 _Wind;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float hash(float2 p){ p = frac(p*float2(123.34,345.45)); p += dot(p,p+34.345); return frac(p.x*p.y); }
            float valueNoise(float2 p)
            {
                float2 i = floor(p); float2 f = frac(p);
                float2 u = f*f*(3.0-2.0*f);
                float a=hash(i), b=hash(i+float2(1,0)), c=hash(i+float2(0,1)), d=hash(i+float2(1,1));
                return lerp(lerp(a,b,u.x), lerp(c,d,u.x), u.y);
            }
            float fbm(float2 p){ float v=0,a=0.5; for(int k=0;k<5;k++){v+=a*valueNoise(p);p*=2.0;a*=0.5;} return v; }

            half4 frag (Varyings i) : SV_Target
            {
                float3 dir = normalize(i.dir);

                // --- градиент неба ---
                float t = saturate(dir.y);
                float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(t, _SkyExponent));
                if (dir.y < 0.0)
                    sky = lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-dir.y * 4.0));

                // пасмурность: небо сереет и темнеет
                float3 overcastSky = lerp(sky, sky * float3(0.5,0.53,0.58), 1.0);
                sky = lerp(sky, overcastSky, _Overcast);

                // --- солнце (гаснет при пасмурности) ---
                float sunVis = 1.0 - _Overcast * 0.85;
                float3 sunDir = normalize(_SunDir.xyz);
                float sd = dot(dir, sunDir);
                float disk = smoothstep(_SunSize, _SunSize + _SunSoftness, sd);
                sky += _SunColor.rgb * disk * _SunIntensity * sunVis;
                sky += _SunColor.rgb * pow(saturate(sd), _SunGlowExp) * _SunGlow * sunVis;

                // --- облака ---
                if (dir.y > 0.001)
                {
                    float2 uv = dir.xz / (dir.y + 0.15) * _CloudScale + _Wind.xy * _Time.y;
                    float n = fbm(uv);

                    float edge = 1.0 - _Coverage;
                    float body = smoothstep(edge, edge + _CloudSoft, n);
                    float core = smoothstep(edge + 0.12, edge + 0.12 + _CloudSoft, n);
                    body *= smoothstep(0.0, _CloudHorizonFade, dir.y);

                    float sunFacing = saturate(dot(normalize(float3(uv * 0.05, 1.0)), sunDir) * 0.5 + 0.5);

                    // грозовое потемнение
                    float3 stormGrey = float3(0.26, 0.28, 0.33);
                    float3 litC    = lerp(_CloudColor.rgb,       stormGrey * 1.4, _CloudDarkness);
                    float3 shadowC = lerp(_CloudShadowColor.rgb, stormGrey * 0.6, _CloudDarkness);

                    float3 cloudCol = lerp(shadowC, litC, saturate(core + sunFacing * 0.4 * sunVis));
                    sky = lerp(sky, cloudCol, saturate(body));
                }

                return half4(sky, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
