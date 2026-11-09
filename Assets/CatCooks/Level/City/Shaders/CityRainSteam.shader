// CatCooks — ПАР ПОСЛЕ ДОЖДЯ: дождь кончился, выглянуло солнце — от мокрого пола зоны поднимаются мягкие
// мультяшные клубочки (эрозия по шуму, чёткий край, освещённый верх). Только в зоне дождя и только там,
// где пол ещё мокрый (карта мокрости). Сила — _CC_Steam.x (CityRain: солнце × «сохнет» × мокрость).
Shader "CatCooks/City/Rain Steam"
{
    Properties
    {
        _Alpha ("Непрозрачность", Range(0, 1)) = 0.32
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+14" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "RainSteam"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "CityRain.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Alpha;
            CBUFFER_END

            float4 _CC_Steam;      // x сила, y клетка (ед.), z клеток по стороне, w жизнь клубка (с)
            float  _CC_WetMapOn;
            float4 _CC_WetRect;
            TEXTURE2D(_CC_WetMap); SAMPLER(sampler_CC_WetMap);

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 info : TEXCOORD1;   // x фаза, y случайное, z сила, w глубина глаза
                float4 screen : TEXCOORD2;
            };

            static const float2 kCorner[6] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 0), float2(1, 1), float2(0, 1) };

            Varyings vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                o.positionCS = float4(2, 2, 2, 1);
                float strength = _CC_Steam.x;
                if (strength < 0.001 || _CC_WetMapOn < 0.5) return o;
                float N = _CC_Steam.z, C = _CC_Steam.y;
                float3 cam = GetCameraPositionWS();
                float2 cell = floor(cam.xz / C) + float2(iid % (uint)N, iid / (uint)N) - floor(N * 0.5);
                float life = max(_CC_Steam.w, 0.5);
                float tt = _Time.y / life + CR_Hash21(cell * 1.31);
                float cyc = floor(tt), f = frac(tt);
                float3 s = CR_Hash32(cell * 0.913 + cyc * 1.618);
                float2 xz = (cell + s.xy) * C;

                float2 wuv = (xz - _CC_WetRect.xy) * _CC_WetRect.zw;
                if (any(wuv < 0.0) || any(wuv > 1.0)) return o;
                float wet = SAMPLE_TEXTURE2D_LOD(_CC_WetMap, sampler_CC_WetMap, wuv, 0).r;
                if (wet < 0.12) return o;
                if (s.z > strength * saturate(wet * 1.6)) return o;
                float h;
                if (!CR_OccHeight(float3(xz.x, 0, xz.y), h)) return o;

                float m = _CC_RainMeter;
                float size = lerp(0.18, 0.6, f) * m * (0.8 + 0.4 * s.x);
                float sway = sin(_Time.y * 0.9 + s.y * 6.28) * 0.12 * m * f;
                float3 ctr = float3(xz.x + sway, h + size * 0.55 + f * 0.9 * m, xz.y);
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                float2 c = kCorner[vid % 6];
                float3 wp = ctr + right * (c.x - 0.5) * 2.0 * size + up * (c.y - 0.5) * 2.0 * size;
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = c;
                o.info = float4(f, s.x, strength, o.positionCS.w);
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float f = i.info.x, rnd = i.info.y;
                float2 p = i.uv * 2.0 - 1.0;
                float d = length(p);
                float n = CR_Noise(i.uv * 3.2 + rnd * 17.0 + float2(0, -f * 1.4)) * 0.65 + CR_Noise(i.uv * 7.0 + rnd * 5.0) * 0.35;
                float shape = (1.0 - d) + (n - 0.5) * 0.7;
                float thr = 0.12 + f * 0.75;                                     // клубок тает (эрозия растёт)
                float a = CR_AAStep(thr, shape);
                if (a < 0.01) discard;
                // мягко у пола (не резать о плитку)
                float2 suv = i.screen.xy / i.screen.w;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float soft = saturate((scene - i.info.w) / (0.35 * _CC_RainMeter));
                Light ml = GetMainLight();
                float3 amb = max(SampleSH(float3(0, 1, 0)), 0.0);
                float litTop = CR_AAStep(0.0, p.y + (n - 0.5) * 0.5 + 0.1);        // освещённый верх, тень снизу — ступенькой
                float3 col = lerp(amb * 1.1 + 0.08, amb + ml.color * 0.75, litTop);
                float fadeIn = smoothstep(0.0, 0.18, f);
                return half4(col, a * soft * fadeIn * _Alpha * saturate(i.info.z * 1.5));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
