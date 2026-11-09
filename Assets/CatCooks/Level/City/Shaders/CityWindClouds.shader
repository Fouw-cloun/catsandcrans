// CatCooks — ТУЧКИ ПЫЛИ МЕСТАМИ: в порыв по полу катятся клубы пыли разного объёма (от маленького клубка до
// большой тучи), каждая — 7 мультяшных шариков, вращаются как катящийся ком, тают по шуму. В дождь — водяная взвесь.
Shader "CatCooks/City/Wind Clouds"
{
    Properties
    {
        _Alpha ("Непрозрачность", Range(0, 1)) = 0.6
        _Dust ("Пыль", Color) = (0.8, 0.74, 0.62, 1)
        _Spray ("Водяная взвесь (в дождь)", Color) = (0.84, 0.88, 0.93, 1)
        _MaxScale ("Самая большая туча (×)", Range(1, 5)) = 2.6
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+13" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "WindClouds"
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
            #include "CityWind.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Alpha, _MaxScale;
                float4 _Dust, _Spray;
            CBUFFER_END

            #define PUFFS 7

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 info : TEXCOORD1;   // x фаза, y случайное, z видимость, w глубина глаза
                float4 screen : TEXCOORD2;
            };

            static const float2 kCorner[6] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 0), float2(1, 1), float2(0, 1) };

            Varyings vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                o.positionCS = float4(2, 2, 2, 1);
                float strength = _CC_WindFx.w;
                if (strength < 0.01) return o;
                uint cloud = iid / PUFFS;
                float pi = (float)(iid % PUFFS);
                float life = 4.2;
                float tt = _Time.y / life + CR_Hash11(cloud * 3.31);
                float cyc = floor(tt), f = frac(tt);
                float3 s = CR_Hash32(float2(cloud * 1.7, cyc * 2.9));
                float sc = 0.5 + (_MaxScale - 0.5) * s.y * s.y;                               // разный объём: чаще мелкие
                float m = _CC_RainMeter;
                float3 dir = CW_Dir();
                float3 perp = float3(-dir.z, 0, dir.x);
                float R = _CC_GustP.x;
                float run = 8.0 * m;
                float2 xz = _CC_GustFocus.xz + perp.xz * (s.x - 0.5) * 2.0 * R + dir.xz * (s.z - 0.5) * 2.0 * R - dir.xz * run * 0.5;
                float g = CW_Gate(strength * CW_Wave(xz + dir.xz * run * 0.4), CR_Hash11(cloud * 0.71 + cyc * 1.9));
                if (g < 0.01) return o;
                float2 cxz = xz + dir.xz * f * run;
                float fl = CW_Floor(cxz);
                float grow = 0.7 + 0.5 * f;
                // шарик внутри тучи: эллипсоид, вращается вокруг поперечной оси — туча «катится»
                float3 r3 = (CR_Hash32(float2(cloud * 5.1 + pi * 1.3, 0.57)) - 0.5) * 2.0;
                float3 off = (dir * r3.x * 1.5 + float3(0, 1, 0) * r3.y * 0.6 + perp * r3.z * 1.0) * sc * m * grow * 0.55;
                off = mul(CW_RotAxis(perp, f * run / max(sc * m, 1e-3) * 0.6), off);
                float psz = (0.45 + 0.35 * CR_Hash11(cloud * 9.7 + pi)) * sc * m * grow;
                float3 ctr = float3(cxz.x, fl, cxz.y) + float3(0, sc * m * 0.55, 0) + off;
                ctr.y = max(ctr.y, fl + psz * 0.45);
                float3 Rv = UNITY_MATRIX_V[0].xyz, Uv = UNITY_MATRIX_V[1].xyz;
                float2 c = kCorner[vid % 6];
                float3 wp = ctr + (Rv * (c.x - 0.5) + Uv * (c.y - 0.5)) * 2.0 * psz;
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = c;
                o.info = float4(f, CR_Hash11(cloud * 2.3 + pi * 7.1 + cyc), g, o.positionCS.w);
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float f = i.info.x, rnd = i.info.y;
                float2 p = i.uv * 2.0 - 1.0;
                float d = length(p);
                float n = CR_Noise(i.uv * 2.8 + rnd * 17.0 + float2(-f * 1.2, 0)) * 0.65 + CR_Noise(i.uv * 6.5 + rnd * 5.0) * 0.35;
                float shape = (1.0 - d) + (n - 0.5) * 0.6;
                float thr = 0.1 + smoothstep(0.55, 1.0, f) * 0.75;                          // тает к концу жизни
                float a = CR_AAStep(thr, shape);
                if (a < 0.01) discard;
                float2 suv = i.screen.xy / i.screen.w;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float soft = saturate((scene - i.info.w) / (0.45 * _CC_RainMeter));
                Light ml = GetMainLight();
                float3 amb = max(SampleSH(float3(0, 1, 0)), 0.0);
                float lit = CR_AAStep(0.0, dot(p, normalize(float2(0.35, 1.0))) + (n - 0.5) * 0.4 + 0.05);   // освещённый верх ступенькой
                float3 base = lerp(_Dust.rgb, _Spray.rgb, saturate(_CC_Rain * 2.5));
                base = lerp(base, float3(0.95, 0.97, 1.0), saturate(_CC_SnowNow * 2.0));       // снег — позёмка
                float3 col = base * lerp(amb * 0.85, amb + ml.color * 0.6, lit);
                return half4(col, a * soft * _Alpha * i.info.z * smoothstep(0.0, 0.12, f));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
