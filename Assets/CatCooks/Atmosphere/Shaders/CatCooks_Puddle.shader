// Лужа: растекается от центра (Fill), неровный «рассыпанный» край, отражает небо,
// в дождь по ней бегут круги. Для молока/масла — свой цвет через _Liquid.
Shader "CatCooks/Puddle"
{
    Properties
    {
        _Fill ("Fill 0..1", Range(0,1)) = 1
        _Liquid ("Liquid Color (a = своя жидкость)", Color) = (0.6,0.7,0.8,0)
        _SkyColor ("Sky Reflection", Color) = (0.7,0.85,0.95,1)
        _Rain ("Rain 0..1", Range(0,1)) = 0
        _Seed ("Seed", Float) = 0
        _Alpha ("Opacity", Range(0,1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-20" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _Fill, _Rain, _Seed, _Alpha; half4 _Liquid, _SkyColor;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 ws : TEXCOORD1; };
            float h21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vn(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(h21(i), h21(i + float2(1,0)), f.x), lerp(h21(i + float2(0,1)), h21(i + 1), f.x), f.y);
            }
            V vert(A v) { V o; o.pos = TransformObjectToHClip(v.pos.xyz); o.uv = v.uv * 2 - 1; o.ws = TransformObjectToWorld(v.pos.xyz); return o; }
            half4 frag(V i) : SV_Target
            {
                float2 p = i.uv;
                float r = length(p);
                float a = atan2(p.y, p.x);
                // неровный край: несколько «языков» + мелкая рябь края
                float edgeN = vn(float2(a * 2.2 + _Seed, _Seed * 1.7)) * 0.35 + vn(p * 3.0 + _Seed) * 0.25;
                float radius = _Fill * (0.62 + edgeN);
                float body = smoothstep(radius, radius - 0.06, r);
                if (body <= 0.001) discard;
                float rim = smoothstep(radius - 0.14, radius - 0.05, r) * body;     // светлая кромка
                // круги от капель: ячейки, в каждой своё время
                float rings = 0;
                if (_Rain > 0.01)
                {
                    float2 g = i.ws.xz * 1.6;
                    float2 c = floor(g);
                    for (int k = 0; k < 4; k++)
                    {
                        float2 cc = c + float2(k & 1, k >> 1);
                        float t = frac(_Time.y * 0.9 + h21(cc) * 7.0);
                        float2 ctr = cc + float2(h21(cc + 3.1), h21(cc + 7.7));
                        float d = length(g - ctr);
                        float ring = 1 - smoothstep(0.0, 0.05, abs(d - t * 0.55));
                        rings += ring * (1 - t) * step(h21(cc + 1.3), _Rain);
                    }
                }
                half3 water = lerp(_SkyColor.rgb * 0.85, _Liquid.rgb, _Liquid.a);
                half3 col = water + rim * 0.25 + rings * 0.35;
                return half4(col, body * _Alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
