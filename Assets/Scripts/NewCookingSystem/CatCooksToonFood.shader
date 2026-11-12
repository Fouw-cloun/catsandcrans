// ============================================================================
//  CatCooks / Toon Food   (v19.1)
//
//  ПИЩЕВОЙ ТУН-ШЕЙДЕР для однотонных масс: масло, паста, пюре, тесто, кремы.
//    • свет ступеньками (как в мультике): тень цветная и мягкая, светлая сторона — ровная;
//    • ОБВОДКА 1 — силуэт: тонкая линия вокруг кучки и её комков (вывернутая оболочка, ширина в пикселях —
//      одинаковая вблизи и вдали, не мерцает);
//    • ОБВОДКА 2 — контур склонов: где поверхность уходит от взгляда, рисуется линия чуть темнее основного
//      цвета (сглажена по экрану — без «пикселей»): горки и комки читаются даже в тени;
//    • блик ступенькой (масло лоснится, паста блестит), светлый край;
//    • «мраморные» разводы (паста, смешивание) — полосы светлее/темнее, закручены шумом.
//  Цвет — из цвета вершин (цвет продукта). В Linear-проекте переводится из sRGB.
//  Свойства масла (Блеск, Тёплая тень, Цвет тени, Светлый край) — те же, что у CatCooks/Butter.
// ============================================================================
Shader "CatCooks/Toon Food"
{
    Properties
    {
        [Header(Svet)]
        _ShadowTint ("Цвет тени", Color) = (0.86, 0.66, 0.46, 1)
        _Warm ("Тень светлее (масло светится изнутри)", Range(0, 1)) = 0.45
        _Step ("Граница света", Range(-0.5, 0.8)) = 0.05
        _StepSoft ("Мягкость ступеньки", Range(0.005, 0.3)) = 0.05
        _Ambient ("Окружение", Range(0, 2)) = 0.9
        [Header(Blesk)]
        _Sheen ("Блеск", Range(0, 1)) = 0.35
        _SheenSize ("Размер блика (больше — меньше пятно)", Range(4, 128)) = 22
        _Rim ("Светлый край", Range(0, 1)) = 0.2
        [Header(Obvodka)]
        _OutlineWidth ("Силуэт: ширина, пикс", Range(0, 6)) = 1.6
        _OutlineDark ("Силуэт: темнее цвета", Range(0, 1)) = 0.5
        _LineWidth ("Контур склонов: порог (больше — шире)", Range(0, 0.8)) = 0.32
        _LineDark ("Контур склонов: темнее цвета", Range(0, 1)) = 0.62
        [Header(Mramor)]
        _Marble ("Разводы: сила", Range(0, 1)) = 0
        _MarbleScale ("Разводы: на метр", Float) = 9
        _MarbleLight ("Разводы: светлые полосы ярче в", Range(1, 2)) = 1.35
        // совместимость с материалами CatCooks/Butter
        [HideInInspector] _Ripple ("", Float) = 0
        [HideInInspector] _RippleScale ("", Float) = 60
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _ShadowTint;
            half _Warm, _Step, _StepSoft, _Ambient, _Sheen, _SheenSize, _Rim;
            half _OutlineWidth, _OutlineDark, _LineWidth, _LineDark;
            half _Marble, _MarbleLight; float _MarbleScale;
            half _Ripple; float _RippleScale;
        CBUFFER_END

        float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
        float vn(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), f.x), lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), f.x), f.y);
        }
        ENDHLSL

        // ── основной проход ──
        Pass
        {
            Name "ToonFood"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; half4 color : COLOR; };

            V vert(A v)
            {
                V o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                half3 alb = i.color.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    alb = SRGBToLinear(alb);
                #endif
                float3 n = normalize(i.normalWS);
                float3 vdir = normalize(GetWorldSpaceViewDir(i.positionWS));

                // мраморные разводы (закручены шумом)
                if (_Marble > 0.001)
                {
                    float2 p = i.positionWS.xz * _MarbleScale;
                    float2 w = float2(vn(p * 0.6 + 3.1), vn(p * 0.6 + 9.7)) - 0.5;
                    float s = sin((p.x * 0.8 + p.y * 0.5 + w.x * 5.0 + w.y * 3.0) * 2.2);
                    half light = smoothstep(0.55, 0.85, s);
                    half dark = smoothstep(0.6, 0.9, -s);
                    alb = lerp(alb, alb * _MarbleLight, light * _Marble);
                    alb = lerp(alb, alb * 0.72, dark * _Marble * 0.8);
                }

                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = dot(n, L.direction);
                half lit = smoothstep(_Step - _StepSoft, _Step + _StepSoft, ndl) * L.shadowAttenuation;
                half3 shadowC = _ShadowTint.rgb * lerp(0.5, 0.8, _Warm);
                half3 shade = lerp(shadowC, 1.0, lit);
                half3 col = alb * (L.color * shade + SampleSH(n) * _Ambient * 0.45);

                // блик ступенькой
                float3 h = normalize(L.direction + vdir);
                half sp = pow(saturate(dot(n, h)), _SheenSize);
                half spw = fwidth(sp) + 1e-4;
                sp = smoothstep(0.5 - spw, 0.5 + spw, sp) * _Sheen * lit;
                col += L.color * sp * 0.55;
                // светлый край
                half nv = saturate(dot(n, vdir));
                col += alb * pow(1.0 - nv, 4.0) * _Rim;

                // контур склонов: линия там, где поверхность уходит от взгляда (сглажена по экрану)
                half lw = fwidth(nv) * 1.2 + 1e-4;
                half edgeLine = 1.0 - smoothstep(_LineWidth - lw, _LineWidth + lw, nv);
                col = lerp(col, alb * _LineDark * (0.55 + 0.45 * lit) * L.color, edgeLine * step(0.001, _LineWidth));
                return half4(col, 1);
            }
            ENDHLSL
        }

        // ── силуэт: вывернутая оболочка, ширина в пикселях ──
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; };

            V vert(A v)
            {
                V o;
                float3 pw = TransformObjectToWorld(v.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(v.normalOS);
                float4 pc = TransformWorldToHClip(pw);
                float3 nc = TransformWorldToHClipDir(nw);
                float2 d = nc.xy;
                float len = length(d);
                d = len > 1e-5 ? d / len : float2(0, 0);
                pc.xy += d * _OutlineWidth * pc.w * 2.0 / _ScreenParams.xy;
                o.positionCS = pc;
                o.color = v.color;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                half3 c = i.color.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    c = SRGBToLinear(c);
                #endif
                if (_OutlineWidth <= 0.001) discard;
                return half4(c * _OutlineDark, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
