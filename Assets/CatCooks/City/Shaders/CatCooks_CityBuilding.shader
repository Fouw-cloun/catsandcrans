// Дома среднего слоя v2 — «живописный» фасад как в Shady Job и реакция на погоду.
//  1. Нормалка атласа: окна и карнизы объёмные, у каждого окна своя тень.
//  2. Градиент по высоте + тёплый отражённый свет от улицы снизу.
//  3. Стекло отражает небо погоды (френель + скользящий блик), ночью — огни.
//  4. Вариативность: у каждого дома свой оттенок, потёки и грязь шумом.
//  5. Дымка: по дальности и «в улицах» — низ тонет, верх чистый.
//  6. Подсветка рёбер со стороны солнца.
//  7. Погода: мокрые фасады темнее и блестят, пыль желтит.
// Пишет глубину, отбрасывает тень.
Shader "CatCooks/City Building"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        [Normal] _BumpMap ("Atlas Normal", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0,2)) = 1
        _ShadowTint ("Shadow Tint", Color) = (0.62,0.56,0.78,1)
        _ShadowSoft ("Shadow Softness", Range(0.01,0.5)) = 0.08
        _Saturation ("Saturation", Range(0,2)) = 1.08
        [Header(Height gradient and bounce)]
        _BaseDark ("Base darkening", Range(0,1)) = 0.35
        _GradHeight ("Gradient height (m)", Float) = 60
        _Bounce ("Warm bounce from street", Color) = (0.95,0.62,0.42,1)
        _BounceStr ("Bounce strength", Range(0,1)) = 0.35
        [Header(Glass)]
        _WindowKey ("Window glass color in atlas", Color) = (0.47,0.58,0.59,1)
        _WindowTol ("Window tolerance", Range(0.01,0.5)) = 0.09
        _GlassRefl ("Glass sky reflection", Range(0,1)) = 0.55
        _WindowLit ("Lit window (night)", Color) = (1,0.82,0.48,1)
        _LitChance ("Lit windows at night", Range(0,1)) = 0.4
        [Header(Variation)]
        _HueVar ("Per-building tint", Range(0,0.3)) = 0.1
        _Dirt ("Dirt streaks", Range(0,1)) = 0.25
        [Header(Edges)]
        _RimStr ("Sun edge highlight", Range(0,1)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST; half4 _ShadowTint, _Bounce, _WindowKey, _WindowLit;
            half _BumpScale, _ShadowSoft, _Saturation, _BaseDark, _GradHeight, _BounceStr, _WindowTol, _GlassRefl, _LitChance, _HueVar, _Dirt, _RimStr;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "Forward" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float4 _CC_SkyFogNear, _CC_SkyFogFar, _CC_SkyParams, _CC_SkyZenith, _CC_SkyHorizon, _CC_SunCol, _CC_Weather;
            float _CC_SkyHazeMax, _CC_StreetHaze;
            struct A { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 ws : TEXCOORD1; float3 n : TEXCOORD2; float4 t : TEXCOORD3; float3 obj : TEXCOORD4; };
            float h21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vn(float2 p) { float2 i = floor(p), f = frac(p); f = f*f*(3-2*f);
                return lerp(lerp(h21(i), h21(i+float2(1,0)), f.x), lerp(h21(i+float2(0,1)), h21(i+1), f.x), f.y); }
            V vert(A v)
            {
                UNITY_SETUP_INSTANCE_ID(v); V o;
                o.ws = TransformObjectToWorld(v.pos.xyz); o.pos = TransformWorldToHClip(o.ws);
                o.n = TransformObjectToWorldNormal(v.n);
                o.t = float4(TransformObjectToWorldDir(v.t.xyz), v.t.w);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.obj = TransformObjectToWorld(float3(0,0,0));          // «паспорт» дома для вариаций
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half3 raw = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                // 4. оттенок дома
                float hb = h21(i.obj.xz * 0.013);
                half3 tint = 1 + (half3(hb, h21(i.obj.zx * 0.021), h21(i.obj.xz * 0.031 + 7)) - 0.5) * _HueVar * 2;
                half3 alb = raw * tint;
                half lum = dot(alb, half3(0.299, 0.587, 0.114));
                alb = lerp(lum.xxx, alb, _Saturation);
                float isWin = 1 - smoothstep(_WindowTol * 0.6, _WindowTol, distance(raw, _WindowKey.rgb));
                // 1. нормаль из атласа
                float3 n0 = normalize(i.n);
                float3 nt = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                float3 tw = normalize(i.t.xyz); float3 bw = cross(n0, tw) * i.t.w;
                float3 n = normalize(nt.x * tw + nt.y * bw + nt.z * n0);
                // потёки и грязь (вертикальные, сильнее внизу)
                float streak = vn(float2(i.ws.x * 0.6 + i.ws.z * 0.6, i.ws.y * 0.04 + hb * 9));
                float gy = saturate((i.ws.y - _CC_Weather.z) / max(1, _GradHeight));
                alb *= 1 - _Dirt * 0.25 * smoothstep(0.55, 0.9, streak) * (1 - gy * 0.6);
                // 7. погода: пыль желтит, мокрое темнее
                alb = lerp(alb, alb * half3(1.05, 0.95, 0.78), _CC_Weather.y * 0.6);
                alb *= 1 - _CC_Weather.x * 0.22;

                Light L = GetMainLight(TransformWorldToShadowCoord(i.ws));
                float ndl = dot(n, L.direction);
                float lit = smoothstep(-_ShadowSoft, _ShadowSoft, ndl) * lerp(0.3, 1.0, L.shadowAttenuation);
                half3 c = lerp(alb * _ShadowTint.rgb, alb * (0.85 + 0.35 * L.color), lit);
                // 2. градиент по высоте + тёплый отсвет улицы снизу (на теневой стороне особенно)
                c *= lerp(1 - _BaseDark, 1.05, gy);
                c += alb * _Bounce.rgb * _BounceStr * (1 - gy) * saturate(-n0.y * 0.5 + 0.5) * (1 - lit * 0.6);
                c += alb * 0.06 * saturate(n0.y);
                // 3. стекло: небо + френель + скользящий блик
                float3 vdir = normalize(_WorldSpaceCameraPos - i.ws);
                float fres = pow(1 - saturate(dot(n0, vdir)), 3);
                float3 r = reflect(-vdir, n);
                half3 sky = lerp(_CC_SkyHorizon.rgb, _CC_SkyZenith.rgb, saturate(r.y * 1.5));
                float glint = pow(saturate(dot(r, L.direction)), 64) * 1.5;
                half3 glass = lerp(c, sky * (0.7 + 0.3 * lit), saturate(_GlassRefl * (0.45 + fres) + _CC_Weather.x * 0.25)) + glint * L.color * lit;
                c = lerp(c, glass, isWin);
                // мокрый фасад блестит
                c += _CC_Weather.x * fres * sky * 0.25 * (1 - isWin);
                // 6. подсветка рёбер со стороны солнца
                float rim = pow(1 - saturate(dot(n0, vdir)), 4) * saturate(dot(n0, L.direction) + 0.3);
                c += _RimStr * rim * _CC_SunCol.rgb * alb * 1.5;
                // ночь: огни в окнах
                float night = _CC_SkyParams.w;
                if (night > 0.01)
                {
                    float2 cell = floor(i.ws.xz * 0.35 + i.ws.y * 0.3);
                    float on = step(h21(cell + floor(i.ws.y / 3.5)), _LitChance) * isWin * night;
                    c = lerp(c, _WindowLit.rgb * 1.3, on);
                }
                // 5. дымка: дальность + уличная (у земли гуще)
                float dist = distance(_WorldSpaceCameraPos, i.ws);
                float start = _CC_SkyParams.x, end = max(start + 1, _CC_SkyParams.y);
                float k = pow(saturate((dist - start) / (end - start)), max(0.2, _CC_SkyParams.z));
                float hz = exp(-max(0, i.ws.y - _CC_Weather.z) / max(1, _CC_Weather.w));
                float ks = _CC_StreetHaze * hz * saturate(dist / 250);
                half3 fogc = lerp(_CC_SkyFogNear.rgb, _CC_SkyFogFar.rgb, saturate(k * 1.2));
                c = lerp(c, fogc, saturate(k * (_CC_SkyHazeMax > 0.001 ? _CC_SkyHazeMax : 0.85) + ks * 0.75));
                return half4(c, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex v
            #pragma fragment f
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            struct SA { float4 p : POSITION; float3 n : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 v(SA i) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 ws = TransformObjectToWorld(i.p.xyz); float3 nw = TransformObjectToWorldNormal(i.n);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, nw, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 f() : SV_Target { return 0; }
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
