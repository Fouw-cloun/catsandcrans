// CatCooks — ПЫЛЬНАЯ ПОЛОСА В ПОРЫВ: вытянутые по ветру пятна пыли скользят низко над полом и тают по шуму
// (две мультяшные ступени плотности). В дождь вместо пыли — водяная взвесь (цвет Spray). Густеют в волне порыва.
// Лист всегда повёрнут к камере вокруг оси ветра: сверху — лежит на полу, сбоку — стоит стеной.
Shader "CatCooks/City/Wind Dust"
{
    Properties
    {
        _Alpha ("Непрозрачность", Range(0, 1)) = 0.32
        _Dust ("Пыль", Color) = (0.86, 0.79, 0.66, 1)
        _Spray ("Водяная взвесь (в дождь)", Color) = (0.86, 0.9, 0.95, 1)
        _Count ("Пятен (доля от Max Dust)", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+13" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "WindDust"
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
                float _Alpha, _Count;
                float4 _Dust, _Spray;
            CBUFFER_END

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
                float strength = _CC_Gust.x * _Count;
                if (strength < 0.01) return o;
                float life = 2.8;
                float tt = _Time.y / life + CR_Hash11(iid * 3.71);
                float cyc = floor(tt), f = frac(tt);
                float4 s = float4(CR_Hash32(float2(iid * 1.9, cyc * 2.7)), CR_Hash11(iid * 5.3 + cyc * 3.9));

                float m = _CC_RainMeter;
                float3 dir = CW_Dir();
                float3 perp = float3(-dir.z, 0, dir.x);
                float3 fc = _CC_GustFocus.xyz;
                float R = _CC_GustP.x;
                float run = 10.0 * m;
                float2 xz = fc.xz + (perp.xz * (s.x - 0.5) + dir.xz * (s.z - 0.5)) * 2.0 * R - dir.xz * run * 0.5;
                if (CR_Hash11(iid * 0.53 + cyc * 1.37) > strength * CW_Wave(xz + dir.xz * run * 0.4)) return o;
                float y = CW_Floor(xz) + (0.25 + 0.6 * s.w) * m;
                float3 ctr = float3(xz.x, y, xz.y) + dir * f * run * (0.8 + 0.4 * s.y);

                float grow = 0.6 + 0.6 * f;
                float Lx = (4.0 + 4.0 * s.x) * m * grow;
                float Ly = (0.8 + 0.9 * s.y) * m * grow;
                float3 cam = GetCameraPositionWS();
                float3 V = normalize(cam - ctr);
                float3 B = cross(dir, V);
                float bl = length(B);
                B = bl > 1e-3 ? B / bl : float3(0, 1, 0);
                float2 c = kCorner[vid % 6];
                float3 wp = ctr + dir * (c.x - 0.5) * 2.0 * Lx + B * (c.y - 0.5) * 2.0 * Ly;
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = c;
                o.info = float4(f, s.y, saturate(strength * 1.6), o.positionCS.w);
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float f = i.info.x, rnd = i.info.y;
                float2 p = i.uv * 2.0 - 1.0;
                float d = length(float2(p.x, p.y * 1.15));
                // шум тянется по ветру и бежит назад — пыль «рвётся» хвостом
                float n = CR_Noise(float2(p.x * 2.2 - f * 2.0 + rnd * 13.0, p.y * 3.0 + rnd * 5.0)) * 0.6
                        + CR_Noise(float2(p.x * 5.0 - f * 3.5, p.y * 6.0 + rnd * 9.0)) * 0.4;
                float shape = (1.0 - d) + (n - 0.5) * 0.9 - max(p.x, 0.0) * 0.25;   // голова плотнее, хвост рваный
                float thr = 0.12 + f * 0.6;
                float a1 = CR_AAStep(thr, shape);
                float a2 = CR_AAStep(thr + 0.22, shape);
                float a = a1 * 0.55 + a2 * 0.45;
                if (a < 0.01) discard;
                float2 suv = i.screen.xy / i.screen.w;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float soft = saturate((scene - i.info.w) / (0.5 * _CC_RainMeter));     // мягко у пола и стен
                Light ml = GetMainLight();
                float3 amb = max(SampleSH(float3(0, 1, 0)), 0.0);
                float3 base = lerp(_Dust.rgb, _Spray.rgb, saturate(_CC_Rain * 2.5));
                base = lerp(base, float3(0.95, 0.97, 1.0), saturate(_CC_SnowNow * 2.0));       // снег — позёмка
                float3 col = base * (amb * 0.9 + ml.color * lerp(0.35, 0.7, a2));
                float fade = smoothstep(0.0, 0.15, f);
                return half4(col, a * soft * fade * _Alpha * i.info.z);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
