// Стена тумана / облачное кольцо / песчаная стена. Пучится и шевелится (смещение
// вершин шумом), верхний край рваный и «ветвистый», мягко входит в дома
// (по глубине). Используется слоями тумана в городе и песчаной бурей.
Shader "CatCooks/Fog Wall"
{
    Properties
    {
        _ColorBottom ("Color Bottom", Color) = (0.9,0.85,0.8,1)
        _ColorTop ("Color Top", Color) = (0.85,0.9,0.95,1)
        _Opacity ("Opacity", Range(0,1)) = 0.6
        _Billow ("Billow (m)", Range(0,80)) = 20
        _NoiseScale ("Noise Scale", Range(0.5,20)) = 5
        _Speed ("Speed", Range(0,1)) = 0.08
        _TopRagged ("Top Edge Raggedness", Range(0,1)) = 0.6
        _SoftFade ("Soft Fade (m)", Range(0.1,200)) = 40
        _Puff ("Puffiness (cloud look)", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-60" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _ColorBottom, _ColorTop; half _Opacity, _Billow, _NoiseScale, _Speed, _TopRagged, _SoftFade, _Puff;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 scr : TEXCOORD1; float n1 : TEXCOORD2; };
            float h21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vn(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(h21(i), h21(i + float2(1,0)), f.x), lerp(h21(i + float2(0,1)), h21(i + 1), f.x), f.y);
            }
            float fbm(float2 p) { return vn(p) * 0.55 + vn(p * 2.1 + 7.3) * 0.28 + vn(p * 4.3 + 1.7) * 0.17; }
            V vert(A v)
            {
                V o;
                float t = _Time.y * _Speed;
                // u — по окружности (0..1), v — по высоте (0..1)
                float2 np = float2(v.uv.x * _NoiseScale * 6.0 + t, v.uv.y * 1.5 - t * 0.7);
                float n = fbm(np);
                // смещение в МИРОВЫХ метрах — не зависит от масштаба кольца
                float3 pw = TransformObjectToWorld(v.pos.xyz);
                float3 nw = normalize(TransformObjectToWorldDir(v.n));
                pw += nw * (n - 0.5) * _Billow * (0.4 + v.uv.y);                    // пучится, сильнее вверху
                pw.y += (vn(np * 0.7 + 3.0) - 0.5) * _Billow * 0.5 * v.uv.y;
                o.pos = TransformWorldToHClip(pw);
                o.uv = v.uv;
                o.scr = ComputeScreenPos(o.pos);
                o.n1 = n;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float2 np = float2(i.uv.x * _NoiseScale * 10.0 + t * 1.3, i.uv.y * 2.5 - t);
                float detail = fbm(np);
                // рваный «ветвистый» верх: порог по высоте плюс шум
                float top = 1.0 - smoothstep(0.35 + detail * 0.65 * _TopRagged - 0.15, 0.95, i.uv.y + (detail - 0.5) * 0.35 * _TopRagged);
                float bottom = smoothstep(0.0, 0.08, i.uv.y);
                float puff = lerp(1.0, smoothstep(0.35, 0.6, detail + i.n1 * 0.3), _Puff);
                float a = top * bottom * puff * _Opacity;
                // мягко входит в дома
                float2 suv = i.scr.xy / i.scr.w;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                a *= saturate((scene - i.scr.w) / _SoftFade);
                half3 col = lerp(_ColorBottom.rgb, _ColorTop.rgb, saturate(i.uv.y + (detail - 0.5) * 0.3));
                col += (detail - 0.5) * 0.08;                         // мягкий объём
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
