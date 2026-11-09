// Дальний город (генератор CatCooks Skyline).
//  • Цвет фасада — в цвете вершин (палитра генератора), окна — процедурные:
//    сетка, ленты, высокие узкие, стекло с импостами. Вдали окна плавно
//    сливаются в ровный тон — никакой ряби.
//  • Мультяшный свет: 2 ступени от солнца + заполняющий.
//  • Воздушная перспектива: чем дальше — тем ближе к цвету дымки погоды
//    (глобальные _CC_SkyFog*, пишет CatCooksSkylineGlobals) — на закате
//    город розовеет, ночью синеет.
//  • Ночью часть окон горит (_CC_Night).
//  • Пишет глубину (DepthOnly + DepthNormals) — туман и SSAO его видят.
Shader "CatCooks/Skyline"
{
    Properties
    {
        _WindowDark ("Window Color", Color) = (0.24,0.31,0.42,1)
        _WindowLit ("Window Lit (night)", Color) = (1,0.82,0.48,1)
        _ShadowTint ("Shadow Tint", Color) = (0.62,0.6,0.78,1)
        _ShadowSoft ("Shadow Softness", Range(0.01,0.5)) = 0.12
        _WindowFadeStart ("Windows fade start (m)", Float) = 350
        _WindowFadeEnd ("Windows fade end (m)", Float) = 900
        _LitChance ("Lit windows at night", Range(0,1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _WindowDark, _WindowLit, _ShadowTint; half _ShadowSoft, _WindowFadeStart, _WindowFadeEnd, _LitChance;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "Forward" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float4 _CC_SkyFogNear, _CC_SkyFogFar, _CC_SkyParams;   // params: start, end, power, night
            float _CC_SkyHazeMax, _CC_StreetHaze; float4 _CC_Weather, _CC_SunCol;
            struct A { float4 pos : POSITION; float3 n : NORMAL; half4 col : COLOR; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct V { float4 pos : SV_POSITION; half4 col : COLOR; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; float3 ws : TEXCOORD2; float3 n : TEXCOORD3; };
            float h21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            V vert(A v)
            {
                V o; o.ws = TransformObjectToWorld(v.pos.xyz); o.pos = TransformWorldToHClip(o.ws);
                o.n = TransformObjectToWorldNormal(v.n); o.col = v.col; o.uv = v.uv; o.uv2 = v.uv2; return o;
            }
            // маска окна: 1 — стекло, 0 — стена. style: 1 сетка, 2 ленты, 3 узкие высокие, 4 стекло с импостами
            float WindowMask(float2 uv, float style, out float2 cell)
            {
                float2 sz = style < 1.5 ? float2(3.2, 3.4) : style < 2.5 ? float2(6.0, 3.6) : style < 3.5 ? float2(2.4, 4.2) : float2(2.0, 3.6);
                float2 g = uv / sz; cell = floor(g); float2 f = frac(g);
                float2 win = style < 1.5 ? float2(0.55, 0.5) : style < 2.5 ? float2(0.92, 0.42) : style < 3.5 ? float2(0.42, 0.66) : float2(0.9, 0.86);
                float2 lo = (1 - win) * 0.5, hi = 1 - lo;
                float m = step(lo.x, f.x) * step(f.x, hi.x) * step(lo.y, f.y) * step(f.y, hi.y);
                return m * step(1.0, cell.y);          // первый этаж без окон-сетки
            }
            half4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.n);
                Light L = GetMainLight();
                float ndl = dot(n, L.direction);
                float lit = smoothstep(-_ShadowSoft, _ShadowSoft, ndl);
                half3 base = i.col.rgb;
                half3 shade = base * _ShadowTint.rgb;
                half3 c = lerp(shade, base * (0.85 + 0.25 * L.color), lit);
                c += base * 0.08 * saturate(n.y);                     // крыши чуть светлее
                // градиент по высоте и тёплый отсвет улицы (как у домов среднего слоя)
                float gy = saturate((i.ws.y - _CC_Weather.z) / 80.0);
                c *= lerp(0.7, 1.05, gy);
                c += base * half3(0.95, 0.62, 0.42) * 0.25 * (1 - gy) * (1 - lit * 0.6);
                // погода: мокрое темнее, пыль желтит
                c = lerp(c, c * half3(1.05, 0.95, 0.78), _CC_Weather.y * 0.5);
                c *= 1 - _CC_Weather.x * 0.18;

                float dist = distance(_WorldSpaceCameraPos, i.ws);
                float night = _CC_SkyParams.w;
                if (i.uv2.x > 0.5 && abs(n.y) < 0.5)
                {
                    float2 cell; float wm = WindowMask(i.uv, i.uv2.x, cell);
                    float fade = 1 - saturate((dist - _WindowFadeStart) / max(1, _WindowFadeEnd - _WindowFadeStart));
                    half3 glass = lerp(_WindowDark.rgb, base * 0.55, 0.25);
                    // вдали окно сливается в средний тон фасада
                    float far = lerp(0.35, 1.0, fade);
                    c = lerp(c, lerp(c * 0.82, glass * (0.75 + 0.35 * lit), far), wm);
                    // ночью — часть окон горит
                    float on = step(h21(cell + i.uv2.y * 17.3), _LitChance) * wm * night;
                    c = lerp(c, _WindowLit.rgb * (1.2 + 0.4 * h21(cell * 3.1)), on);
                }
                // воздушная перспектива: к цвету дымки, сверху — чуть меньше
                float start = _CC_SkyParams.x, end = max(start + 1, _CC_SkyParams.y);
                float k = pow(saturate((dist - start) / (end - start)), max(0.2, _CC_SkyParams.z));
                k *= lerp(1.0, 0.75, saturate((i.ws.y - _WorldSpaceCameraPos.y + 50) / 300));
                half3 fogc = lerp(_CC_SkyFogNear.rgb, _CC_SkyFogFar.rgb, saturate(k * 1.2));
                float hz = exp(-max(0, i.ws.y - _CC_Weather.z) / max(1, _CC_Weather.w));
                float ks = _CC_StreetHaze * hz;
                c = lerp(c, fogc, saturate(k * (_CC_SkyHazeMax > 0.001 ? _CC_SkyHazeMax : 0.85) + ks * 0.75));
                return half4(c, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex v
            #pragma fragment f
            float4 v(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half4 f() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex v
            #pragma fragment f
            struct DA { float4 p : POSITION; float3 n : NORMAL; };
            struct DV { float4 p : SV_POSITION; float3 n : TEXCOORD0; };
            DV v(DA i) { DV o; o.p = TransformObjectToHClip(i.p.xyz); o.n = TransformObjectToWorldNormal(i.n); return o; }
            half4 f(DV i) : SV_Target { return half4(normalize(i.n), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
