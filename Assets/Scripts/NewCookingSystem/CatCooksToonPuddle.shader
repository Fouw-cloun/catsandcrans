// ============================================================================
//  CatCooks / Milk Puddle   (v18.1: было «Toon Puddle» — имя совпало с лужей-декором из trees, молоко рисовалось как вода)
//
//  Мультяшная лужа (молоко, вода, сливки). Сетку строит LiquidPuddle:
//    uv0.x — доля до края (0 центр, 1 край лужи, 1..1.25 — мокрый ореол на полу),
//    uv1   — локальные координаты, м (блики, рябь),
//    uv2   — смещение от центра своей кляксы (лужа или капля-спутник) — для бортика.
//  • чёткий край с мягким сглаживанием и тёмной обводкой цвета жидкости;
//  • светлый «бортик» — у края лужа чуть выпуклая (мениск), ловит свет;
//  • плоская двухтоновая заливка, без реализма;
//  • белые блики-«бобы» и крупное отражение-окно;
//  • круги от струи (_Rings) — светлые мультяшные линии;
//  • мокрый ореол: пол вокруг лужи чуть темнее.
//  • v10.3 — ДЫРЫ И ШВЫ: по глубине сцены (что видит камера, не коллайдеры) — где настоящая
//    поверхность ниже уровня лужи (шов, люк, дыра), лужа не рисуется: «стекает» в дыру,
//    по краю дыры — тёмная кромка, как у края лужи. Нужна галка Depth Texture в URP Asset
//    (кнопка в окне «Ведро: заменить модель»). Нет глубины — лужа рисуется как раньше.
// ============================================================================
Shader "CatCooks/Milk Puddle"
{
    Properties
    {
        _BaseColor ("Цвет (ставит скрипт)", Color) = (0.97, 0.97, 0.95, 1)
        _Opacity ("Непрозрачность тела", Range(0, 1)) = 0.96
        _OutlineDark ("Обводка: темнее цвета", Range(0, 1)) = 0.62
        _OutlineWidth ("Обводка: ширина (доля радиуса)", Range(0, 0.15)) = 0.035
        _LipWidth ("Бортик: ширина", Range(0, 0.4)) = 0.16
        _LipLight ("Бортик: светлее", Range(0, 0.6)) = 0.22
        _ShadeDark ("Тень на бортике", Range(0, 1)) = 0.86
        _Sparkle ("Блики-бобы", Range(0, 1)) = 0.85
        _Window ("Отражение-окно", Range(0, 1)) = 0.35
        _WetDark ("Мокрый ореол на полу", Range(0, 1)) = 0.22
        _RingSpeed ("Круги: скорость, м/с", Float) = 0.5
        _RingWidth ("Круги: ширина, м", Float) = 0.025
        _RingStrength ("Круги: сила", Range(0, 2)) = 1
        _Agitation ("Возбуждение (скрипт)", Range(0, 1)) = 0
        _Dry ("Высыхание (скрипт): 1 — свежая", Range(0, 1)) = 1
        _HoleCut ("v10.3: не ложиться на дыры и швы (по глубине сцены)", Range(0, 1)) = 1
        _HoleDepth ("v10.3: глубже этого под лужей — дыра, м", Float) = 0.012
        _HoleRim ("v10.3: (не используется с v10.4)", Range(0, 6)) = 2.5
        _HoleGap ("v10.4: зазор от щели до края лужи, м (по обе стороны — свой ровный край)", Range(0, 0.1)) = 0.025
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-20" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Offset -1, -1

        Pass
        {
            Name "ToonPuddle"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor; half _Opacity, _OutlineDark, _OutlineWidth, _LipWidth, _LipLight, _ShadeDark;
                half _Sparkle, _Window, _WetDark; float _RingSpeed, _RingWidth; half _RingStrength, _Agitation, _Dry;
                half _HoleCut; float _HoleDepth; half _HoleRim; float _HoleGap;
            CBUFFER_END
            float4 _Rings[4];

            struct A { float4 positionOS : POSITION; half4 color : COLOR; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; float2 uv2 : TEXCOORD2; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float2 uv0 : TEXCOORD1; float2 uv1 : TEXCOORD2; half fog : TEXCOORD3; float2 uv2 : TEXCOORD4; float4 screenPos : TEXCOORD5; };

            V vert(A v)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.uv0 = v.uv0; o.uv1 = v.uv1; o.uv2 = v.uv2;
                o.fog = ComputeFogFactor(p.positionCS.z);
                o.screenPos = ComputeScreenPos(p.positionCS);
                return o;
            }

            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), f.x), lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), f.x), f.y);
            }

            /// Насколько настоящая поверхность сцены ниже уровня лужи в этой точке экрана, м (0 — нет глубины).
            float SceneDrop(float2 suv, float puddleY)
            {
                float raw = SampleSceneDepth(suv);
                #if UNITY_REVERSED_Z
                    if (raw <= 1e-6) return 0;                                  // дальняя плоскость / глубины нет
                #else
                    if (raw >= 1 - 1e-6) return 0;
                #endif
                float3 sw = ComputeWorldSpacePosition(suv, raw, UNITY_MATRIX_I_VP);
                return puddleY - sw.y;
            }

            /// 1 — в этой точке плоскости лужи (мир) под ней дыра/шов.
            float HoleAt(float3 p, float py)
            {
                float4 cs = TransformWorldToHClip(p);
                float4 sp = ComputeScreenPos(cs);
                return step(_HoleDepth, SceneDrop(sp.xy / max(1e-5, sp.w), py));
            }

            /// Сглаженный минимум: два края сливаются скруглённым углом радиуса k.
            float SMin(float a, float b, float k)
            {
                float h = max(k - abs(a - b), 0.0) / max(k, 1e-6);
                return min(a, b) - h * h * k * 0.25;
            }

            half4 frag(V i) : SV_Target
            {
                float u = i.uv0.x;

                // ── v10.4: дыры и швы ДЕЛЯТ лужу: по обе стороны щели — свой ровный край с кромкой ──
                // Расстояние до края лужи (в долях u) объединяем с расстоянием до дыры (зазор Hole Gap)
                // сглаженным минимумом — получается две лужи со скруглёнными углами, а не прорезь.
                float du = length(float2(ddx(u), ddy(u)));
                float dp = length(float2(length(ddx(i.positionWS)), length(ddy(i.positionWS))));
                float uPerM = du / max(dp, 1e-6);                               // сколько u на метр здесь
                float holeU = 1e3;                                               // до «края у дыры», в долях u
                if (_HoleCut > 0.5)
                {
                    float2 suv = i.screenPos.xy / max(1e-5, i.screenPos.w);
                    float py = i.positionWS.y - 0.004;                          // лужа чуть приподнята над полом
                    clip(_HoleDepth - SceneDrop(suv, py));
                    float G = max(0.004, _HoleGap);
                    float far = G * 1.6;
                    half any = 0;
                    [unroll] for (int d0 = 0; d0 < 6; d0++)
                    {
                        float a0 = d0 * 1.0472;
                        any += HoleAt(i.positionWS + float3(cos(a0), 0, sin(a0)) * far, py);
                    }
                    if (any > 0)                                                 // дыра рядом — меряем точнее
                    {
                        float hd = far * 1.25;
                        [unroll] for (int r = 4; r >= 1; r--)
                        {
                            float rad = far * r / 4.0;
                            half hit = 0;
                            [unroll] for (int d1 = 0; d1 < 8; d1++)
                            {
                                float a1 = d1 * 0.7854 + r * 0.31;
                                hit += HoleAt(i.positionWS + float3(cos(a1), 0, sin(a1)) * rad, py);
                            }
                            if (hit > 0) hd = rad;
                        }
                        holeU = (hd - G) * uPerM;
                    }
                }
                float aa = max(fwidth(u) * 1.2, 1e-4);
                half3 base = _BaseColor.rgb;

                // ── мокрый ореол за краем лужи ──
                if (u > 1.0)
                {
                    clip(holeU + 0.5 * max(0.004, _HoleGap) * uPerM);            // ореол тоже не в щели
                    half k = saturate(1.0 - (u - 1.0) / 0.25);
                    half3 wet = base * 0.25h;
                    return half4(MixFog(wet, i.fog), k * k * _WetDark * _Dry);
                }

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half sh = lerp(0.75h, 1.0h, light.shadowAttenuation);

                // ── бортик: у края лужа выпуклая; свет — с одной стороны, тень — с другой ──
                float2 dir = normalize(i.uv2 + 1e-5);
                float kU = max(0.004, _HoleGap) * uPerM;
                float fU = SMin(1.0 - u, holeU, kU);                           // до ближайшего края (лужи или щели)
                clip(fU);
                half lip = 1.0h - smoothstep(_OutlineWidth, _LipWidth, fU);
                half facing = dot(dir, normalize(light.direction.xz + 1e-4));
                half3 col = base;
                col = lerp(col, base * _ShadeDark, lip * step(facing, -0.15h));
                col = lerp(col, saturate(base + _LipLight), lip * step(0.15h, facing));

                // ── круги от струи — светлые линии ──
                half ring = 0;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float age = _Rings[k].w;
                    if (age < 0) continue;
                    float r = length(i.positionWS.xz - _Rings[k].xz);
                    float d = abs(r - age * _RingSpeed) / max(1e-3, _RingWidth);
                    ring = max(ring, (1.0 - smoothstep(0.35, 0.6, d)) * saturate(1.0 - age * 0.8));
                }
                col = lerp(col, saturate(base * 1.12h + 0.08h), ring * _RingStrength * 0.8h);

                // ── блики: окно и «бобы» (мультяшные отражения) ──
                float2 q = i.uv1;
                float2 wq = q * 3.0 + float2(0.4, -0.3);
                half window = step(abs(wq.x - 0.35), 0.18) * step(abs(wq.y + 0.2), 0.08) * step(u, 0.75);
                float2 bq = q * 9.0;
                float2 bi = floor(bq), bf = frac(bq) - 0.5;
                half pick = step(0.86, hash21(bi + 7.7));
                float2 bs = float2(bf.x * 1.0 + bf.y * 0.55, bf.y * 1.6 - bf.x * 0.4);
                half bean = (1.0h - smoothstep(0.16, 0.2, length(bs))) * pick * step(u, 0.85);
                bean *= 0.6h + 0.4h * sin(_Time.y * 1.7 + bi.x * 3.1);           // мерцают
                half wobble = _Agitation * (vnoise(q * 25.0 + _Time.y * 3.0) - 0.5h) * 0.12h;
                col = lerp(col, half3(1, 1, 1), saturate((window * _Window + bean * _Sparkle) * (1.0h + wobble)) * _Dry);

                // ── свет: плоско, двумя тонами ──
                half ndl = saturate(dot(half3(0, 1, 0), light.direction) * 0.5h + 0.5h);
                col *= lerp(0.88h, 1.0h, step(0.45h, ndl)) * lerp(light.color, 1.0h, 0.55h) * sh;

                // ── обводка: тёмная кромка цвета жидкости ──
                half edgeLine = 1.0h - smoothstep(_OutlineWidth - aa, _OutlineWidth + aa, fU);
                col = lerp(col, base * _OutlineDark, edgeLine);

                // v9.2: сохнет — плавно тает до нуля (вторая половина сушки), а не щёлкает в конце
                half alpha = smoothstep(0.0h, aa, fU) * lerp(_Opacity * 0.6h, _Opacity, _Dry) * smoothstep(0.0h, 0.55h, _Dry);
                col = MixFog(col, i.fog);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
