// CatCooks — ПЫЛИНКИ В СОЛНЦЕ: в тихую солнечную погоду в воздухе медленно плавают светлые точки (пыль, пыльца),
// мерцают на солнце. Коробка вокруг точки взгляда камеры, у краёв гаснут. Сила — _CC_Motes.x (CityStorm по погоде).
Shader "CatCooks/City/Wind Motes"
{
    Properties
    {
        _Color ("Цвет", Color) = (1, 0.95, 0.8, 1)
        _Glow ("Свечение на солнце", Range(0, 4)) = 1.6
        _Alpha ("Непрозрачность", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+12" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "WindMotes"
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
            #include "CityRain.hlsl"
            #include "CityWind.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Glow, _Alpha;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 info : TEXCOORD1;   // x яркость (мерцание), y гаснет у краёв
            };

            static const float2 kCorner[6] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 0), float2(1, 1), float2(0, 1) };

            Varyings vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                o.positionCS = float4(2, 2, 2, 1);
                float strength = _CC_Motes.x;
                if (strength < 0.01) return o;
                if (CR_Hash11(iid * 0.917) > strength) return o;
                float m = _CC_RainMeter;
                float R = _CC_Motes.y;
                float3 s = CR_Hash32(float2(iid * 1.37, 0.71));
                float sd = CR_Hash11(iid * 4.13);
                float t = _Time.y;
                float3 dir = CW_Dir();
                // медленный дрейф по ветру + «плавание»
                float2 drift = dir.xz * t * 0.25 * m + float2(sin(t * 0.37 + sd * 6.28), cos(t * 0.29 + sd * 4.1)) * 0.6 * m;
                float2 lo = _CC_GustFocus.xz - R;
                float2 xz = frac((s.xy * 2.0 * R + drift - lo) / (2.0 * R)) * 2.0 * R + lo;   // коробка едет за взглядом
                float2 e = min(xz - lo, lo + 2.0 * R - xz) / (0.2 * R);
                float edge = saturate(min(e.x, e.y));
                float y = CW_Floor(xz) + (0.2 + 4.8 * s.z) * m + sin(t * 0.6 + sd * 6.28) * 0.3 * m;
                float3 ctr = float3(xz.x, y, xz.y);
                float4 cs0 = TransformWorldToHClip(ctr);
                float pix = 2.0 * max(cs0.w, 0.01) / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y);
                float size = max(_CC_Motes.z * (0.6 + 0.8 * sd), pix * 2.0);            // не меньше ~2 пикселей
                float3 Rv = UNITY_MATRIX_V[0].xyz, Uv = UNITY_MATRIX_V[1].xyz;
                float2 c = kCorner[vid % 6];
                float3 wp = ctr + (Rv * (c.x - 0.5) + Uv * (c.y - 0.5)) * 2.0 * size;
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = c;
                float tw = 0.55 + 0.45 * sin(t * (2.0 + 3.0 * sd) + sd * 20.0);            // мерцание
                o.info = float2(tw, edge * smoothstep(0.0, 0.3, strength));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float d = length(i.uv * 2.0 - 1.0);
                float core = CR_AAStep(d, 0.45);
                float halo = saturate(1.0 - d) * 0.35;
                float a = max(core, halo) * i.info.y * _Alpha;
                if (a < 0.01) discard;
                Light ml = GetMainLight();
                float3 amb = max(SampleSH(float3(0, 1, 0)), 0.0);
                float3 col = _Color.rgb * (amb * 0.8 + ml.color * _Glow * i.info.x);
                return half4(col, a * lerp(0.6, 1.0, i.info.x));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
