// CatCooks — ПАР ИЗО РТА в мороз: клубочки от игрока (точки и время выдоха — CityWinter, массив _CC_Breath),
// плывут вперёд, поднимаются, растут и тают мультяшной эрозией. Остаются в воздухе там, где выдохнуты.
Shader "CatCooks/City/Breath"
{
    Properties
    {
        _Color ("Цвет", Color) = (0.94, 0.97, 1, 1)
        _Alpha ("Непрозрачность", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+12" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Breath"
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

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Alpha;
            CBUFFER_END
            float4 _CC_Breath[16];     // xyz — где выдохнут, w — время выдоха
            float4 _CC_BreathV[16];    // xyz — скорость (ед./с)
            float4 _CC_BreathP;        // x размер (ед.), y жизнь (с), z сила, w время сейчас

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 info : TEXCOORD1;   // x жизнь 0..1, y случайное, z гаснет у камеры
            };

            static const float2 kCorner[6] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 0), float2(1, 1), float2(0, 1) };

            Varyings vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                o.positionCS = float4(2, 2, 2, 1);
                if (_CC_BreathP.z < 0.01 || iid >= 16) return o;
                float4 b = _CC_Breath[iid];
                float age = _CC_BreathP.w - b.w;
                float life = max(_CC_BreathP.y, 0.2);
                if (age < 0.0 || age > life) return o;
                float f = age / life;
                float slow = 1.0 - f * 0.6;                                          // тормозит в воздухе
                float3 ctr = b.xyz + _CC_BreathV[iid].xyz * age * slow + float3(0, 0.12 * _CC_RainMeter, 0) * age;
                float size = _CC_BreathP.x * (0.35 + 1.3 * sqrt(f));
                float camD = length(ctr - GetCameraPositionWS());
                float nearF = saturate((camD - 0.2 * _CC_RainMeter) / (0.3 * _CC_RainMeter));   // у самой камеры не лепим облако на весь экран
                if (nearF < 0.01) return o;
                float3 R = UNITY_MATRIX_V[0].xyz, U = UNITY_MATRIX_V[1].xyz;
                float2 c = kCorner[vid % 6];
                o.positionCS = TransformWorldToHClip(ctr + (R * (c.x - 0.5) + U * (c.y - 0.5)) * 2.0 * size);
                o.uv = c;
                o.info = float3(f, CR_Hash11(b.w * 13.1 + iid), nearF);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float f = i.info.x, rnd = i.info.y;
                float2 p = i.uv * 2.0 - 1.0;
                float d = length(p);
                float n = CR_Noise(i.uv * 3.0 + rnd * 17.0 + float2(0, -f)) * 0.65 + CR_Noise(i.uv * 6.5 + rnd * 5.0) * 0.35;
                float shape = (1.0 - d) + (n - 0.5) * 0.7;
                float a = CR_AAStep(0.12 + f * 0.75, shape);
                if (a < 0.01) discard;
                Light ml = GetMainLight();
                float3 amb = max(SampleSH(float3(0, 1, 0)), 0.0);
                float lit = CR_AAStep(0.0, p.y + (n - 0.5) * 0.5 + 0.1);
                float3 col = _Color.rgb * lerp(amb * 1.05 + 0.05, amb + ml.color * 0.6, lit);
                return half4(col, a * _Alpha * _CC_BreathP.z * smoothstep(0.0, 0.12, f) * i.info.z);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
