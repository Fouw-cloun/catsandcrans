// CatCooks — ВЫЖЖЕННОЕ ПЯТНО от удара молнии (квад на полу, ставит CityStorm). Мультяшно: рваная звезда копоти,
// трещины-лучи, тёмная серединка, угольки тлеют и остывают, потом пятно тает. _Age / _Seed / _Life — от CityStorm.
Shader "CatCooks/City/Scorch"
{
    Properties
    {
        _Soot ("Копоть", Color) = (0.05, 0.045, 0.045, 1)
        _Ember ("Угольки", Color) = (1, 0.45, 0.1, 1)
        _EmberBoost ("Яркость угольков (HDR)", Range(1, 8)) = 4
        _EmberSeconds ("Угольки тлеют, с", Range(1, 30)) = 8
        _Age ("Возраст (ставит код)", Float) = 0
        _Seed ("Случайное (ставит код)", Float) = 0
        _Life ("Жизнь (ставит код)", Float) = 45
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Scorch"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -2, -2
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CityRain.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Soot, _Ember;
                float _EmberBoost, _EmberSeconds, _Age, _Seed, _Life;
            CBUFFER_END

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }

            half4 frag(V i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float r = length(p);
                float ang = atan2(p.y, p.x);
                float sd = _Seed * 37.0;
                float n = CR_Noise(p * 2.5 + sd) * 0.65 + CR_Noise(p * 6.0 + sd * 1.7) * 0.35;
                // пятно копоти: круглое с короткими рваными лучами
                float rays = 0.5 + 0.5 * cos(ang * 6.0 + n * 2.5 + sd);
                rays = rays * rays * rays;
                float edge = 0.5 + 0.16 * rays + (n - 0.5) * 0.16;
                float body = CR_AAStep(r, edge);
                float inner = CR_AAStep(r, edge * 0.55);                               // две ступени: край светлее, середина чёрная
                // тонкие трещины-лучи от центра, чуть за край
                float seg = 6.28318 / 7.0;
                float da = abs(frac((ang + (n - 0.5) * 0.6 + sd) / seg + 0.5) - 0.5) * seg * r;
                float crackLen = min(edge + 0.14 + 0.12 * CR_Hash11(floor((ang + sd) / seg) + _Seed * 13.0), 0.93);
                float crack = CR_AAStep(da, 0.012) * step(edge * 0.4, r) * CR_AAStep(r, crackLen);
                float a = max(body * lerp(0.5, 0.92, inner), crack * 0.85);
                // угольки: мелкие точки в середине, тлеют и остывают; горячая сердцевина гаснет быстрее
                float heat = saturate(1.0 - _Age / max(_EmberSeconds, 0.1));
                heat *= heat;
                float2 g = p * 14.0 + sd;
                float h = CR_Hash21(floor(g));
                float spark = CR_AAStep(length(frac(g) - 0.5), 0.2 * h) * step(0.64, h) * CR_AAStep(r, edge * 0.6);
                float core = CR_AAStep(r, 0.08 + 0.02 * sin(_Time.y * 11.0 + sd)) * saturate(1.0 - _Age / max(_EmberSeconds * 0.35, 0.1));
                float flick = 0.7 + 0.3 * sin(_Time.y * 9.0 + h * 25.0);
                float glow = saturate(spark * flick * heat + core);
                if (a < 0.01 && glow < 0.01) discard;
                float3 col = _Soot.rgb * lerp(1.8, 0.5, inner);
                col = lerp(col, _Ember.rgb * _EmberBoost, glow);
                float fade = 1.0 - smoothstep(_Life * 0.7, _Life, _Age);
                return half4(col, saturate(max(a, glow)) * fade);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
